using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Resources.Events;
using UnityEngine;

// FT-06 Guild Core — 主服務
// 實作依據：【FT-06-FSD】guild-core.md §4.4 / §5.1~§5.4
// 職責（SRP）：訂閱 F-03 事件、連跳 queue 排程、Game Over 兩階段狀態機、
//              ISaveable 序列化、對外查詢 API

// ── ISaveable TODO ────────────────────────────────────────────────────────────
// TODO(FT-10)：待 ISaveable 介面建立後，於類別宣告加上 `: ISaveable`。
// 目前保留全部 ISaveable 方法簽名（OwnerKey / IsCritical / Serialize /
// RestoreFromSave / InitializeAsNewGame）為 public，FT-10 整合時無需修改方法本身。
// ─────────────────────────────────────────────────────────────────────────────

namespace TheGuild.Gameplay.Guild
{
    /// <summary>
    /// FT-06 公會核心服務（concrete singleton MonoBehaviour）。
    /// 管理公會等級、Game Over 兩階段狀態機、公會識別資料。
    /// GDD §1 / §3；FSD §4.4 / §5。
    /// </summary>
    // DefaultExecutionOrder=150：晚於 ResourceManagement（無顯式順序，估約 100~120）
    // 與 OutcomeResolutionService（140）；確保訂閱時上游已初始化。
    [DefaultExecutionOrder(150)]
    public sealed class GuildCoreService : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────────

        /// <summary>全域唯一實例。</summary>
        public static GuildCoreService Instance { get; private set; }

        // ── Private Fields ───────────────────────────────────────────────────────

        private GuildLevelDatabaseLoader _loader;
        private GuildState _state;

        // runtime 用 enum（所有比較與轉移）；對應 _state.gameOverState 字串
        // 雙欄位策略：FSD §5.3 / §5.4 SetGameOverState helper 同步維護兩者
        private GameOverState _gameOverState;

        // 連跳 queue（疑點 2 決策：List<T> 取代 Queue<T>，以支援 §5.4-A 步驟 5 in-place 回填）
        // 最多 4 個元素（GuildLevelTable 最多 5 級，單次最多升 4 級），RemoveAt(0) 無效能問題
        private readonly List<LevelUpPayload> _pendingLevelUpQueue = new List<LevelUpPayload>(4);

        // §5.4-B frame 節流計數器
        private int _levelUpFrameCounter;

        // §5.4-C snapshot 緩存，§5.4-D ConfirmGameOver 直接重用
        private OnGameOverPendingEvent _pendingSnapshot;

        // ── RuntimeInitializeOnLoadMethod ────────────────────────────────────────

        /// <summary>
        /// 在場景載入前向 DataManager 註冊 GuildLevelTable。
        /// 對齊 F-01 FSD 2026-04-27 去中心化機制（下游各自 RegisterTable）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<GuildLevelEntry>(GuildCoreConstants.GUILD_LEVEL_TABLE_NAME);
        }

        // ── ISaveable Properties（TODO FT-10：add `: ISaveable` to class declaration） ──

        /// <summary>FT-10 ISaveable owner 識別鍵。</summary>
        public string OwnerKey => GuildCoreConstants.OWNER_KEY;

        /// <summary>FT-10 ISaveable 是否為關鍵存檔（違規拋例外觸發整檔回退）。</summary>
        public bool IsCritical => true;

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            // 訂閱 F-03 聲望變動事件（等級判定觸發點）
            EventBus.Subscribe<OnReputationChangedEvent>(HandleReputationChanged);

            // 訂閱 F-03 破產狀態變更事件（Game Over 觸發點）
            EventBus.Subscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyState);
        }

        private void OnDisable()
        {
            // 對稱取消訂閱，防止 GC 後仍收到事件（FSD §5.5.2）
            EventBus.Unsubscribe<OnReputationChangedEvent>(HandleReputationChanged);
            EventBus.Unsubscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyState);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            ProcessLevelUpQueue();
        }

        // ── ISaveable Methods ─────────────────────────────────────────────────────

        /// <summary>
        /// 序列化 GuildState 5 欄位為 JSON。
        /// 注意：runtime 用的 _pendingLevelUpQueue 不序列化（FSD §5.2.3）。
        /// </summary>
        public string Serialize()
        {
            return JsonUtility.ToJson(_state);
        }

        /// <summary>
        /// 從存檔 JSON 還原 GuildState。
        /// FSD §5.4-F RestoreFromSave 步驟 0~5。
        /// </summary>
        public void RestoreFromSave(string ownerJson)
        {
            // 步驟 0：null/空字串視為首次遊玩，委派給 InitializeAsNewGame
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            // 步驟 1：反序列化
            _state = JsonUtility.FromJson<GuildState>(ownerJson);

            // 步驟 2：資料驗證
            if (_state == null)
            {
                Debug.LogError("[GuildCoreService] RestoreFromSave：反序列化結果為 null，存檔可能損毀");
                throw new InvalidOperationException("GuildState deserialization returned null. Save data may be corrupted.");
            }

            if (_state.currentLevel < 1 || _state.currentLevel > 5)
            {
                Debug.LogError($"[GuildCoreService] RestoreFromSave：currentLevel 超出範圍 [1,5]，實際值={_state.currentLevel}");
                throw new InvalidOperationException($"GuildState.currentLevel out of range [1,5]: {_state.currentLevel}");
            }

            if (_state.gameOverState != "Active" &&
                _state.gameOverState != "Pending" &&
                _state.gameOverState != "Over")
            {
                Debug.LogError($"[GuildCoreService] RestoreFromSave：gameOverState 非法值=\"{_state.gameOverState}\"");
                throw new InvalidOperationException($"GuildState.gameOverState invalid value: \"{_state.gameOverState}\"");
            }

            // 步驟 3：同步 runtime enum（步驟 2 已驗證字串值域，此處 Parse 不會失敗）
            _gameOverState = (GameOverState)Enum.Parse(typeof(GameOverState), _state.gameOverState);

            // 步驟 4：不重判等級、不補發連跳、不重發 Pending/Over 事件
            // _pendingSnapshot 維持 default；P-02 透過 IsGameOverPending/IsGameOver 自行查詢

            // 步驟 5：發布讀取完成事件
            EventBus.Publish(new OnGuildLoadedEvent(_state.displayName, _state.currentLevel));
        }

        /// <summary>
        /// 新遊戲初始化：設定預設等級、創立時間、Active 狀態，發布初始化事件。
        /// FT-10 Bootstrap 呼叫；或 RestoreFromSave 收到 null 時內部委派。
        /// FSD §5.4-F InitializeAsNewGame 步驟 1~5。
        /// </summary>
        public void InitializeAsNewGame()
        {
            // 步驟 1：若 SetGuildName 未先呼叫，displayName 已預設為 DEFAULT_GUILD_NAME
            if (string.IsNullOrEmpty(_state.displayName))
            {
                _state.displayName = GuildCoreConstants.DEFAULT_GUILD_NAME;
            }

            // 步驟 2：創立時間戳（UTC Unix seconds）
            _state.foundingTimestamp = TimeSystem.Instance.NowUTC;

            // 步驟 3：等級預設 1
            _state.currentLevel = 1;

            // 步驟 4：設定 Active 狀態（helper 同步寫 enum 與字串）
            SetGameOverState(GameOverState.Active);

            // 步驟 5：發布初始化事件
            EventBus.Publish(new OnGuildInitializedEvent(_state.displayName, _state.foundingTimestamp));
        }

        // ── Public Query API（FSD §5.1）─────────────────────────────────────────

        /// <summary>回傳當前公會等級（1~5）。</summary>
        public int GetCurrentLevel()
        {
            return _state.currentLevel;
        }

        /// <summary>回傳 GuildLevelTable[currentLevel].title（即時查表，不快取）。</summary>
        public string GetCurrentTitle()
        {
            return _loader.GetEntry(_state.currentLevel)?.title ?? string.Empty;
        }

        /// <summary>回傳老手招募最高冒險者階級（即時查表）。</summary>
        public string GetMaxRecruitableRank()
        {
            return _loader.GetEntry(_state.currentLevel)?.maxRecruitableRank ?? string.Empty;
        }

        /// <summary>回傳常規任務最高難度（即時查表）。</summary>
        public string GetMaxMissionDifficulty()
        {
            return _loader.GetEntry(_state.currentLevel)?.maxMissionDifficulty ?? string.Empty;
        }

        /// <summary>
        /// [Deprecated] 等同 GetMaxRecruitableRank。
        /// 下游請改用 <see cref="GetMaxRecruitableRank"/>。
        /// </summary>
        public string GetMaxDifficulty()
        {
            // deprecated alias，輸出 deprecation log 後轉呼叫正確方法
            Debug.LogWarning("[GuildCoreService] GetMaxDifficulty is deprecated; use GetMaxRecruitableRank");
            return GetMaxRecruitableRank();
        }

        /// <summary>回傳完整顯示名稱（含「公會」後綴）。</summary>
        public string GetGuildDisplayName()
        {
            return _state.displayName;
        }

        /// <summary>回傳公會創立時間（UTC Unix seconds）。</summary>
        public long GetFoundingTimestamp()
        {
            return _state.foundingTimestamp;
        }

        /// <summary>是否處於 Game Over 等待玩家確認的 Pending 狀態。</summary>
        public bool IsGameOverPending()
        {
            return _gameOverState == GameOverState.Pending;
        }

        /// <summary>是否已完成 Game Over（玩家已確認訃聞，時間已凍結）。</summary>
        public bool IsGameOver()
        {
            return _gameOverState == GameOverState.Over;
        }

        // ── Public Control API ───────────────────────────────────────────────────

        /// <summary>
        /// 設定公會名稱（P-01 介紹畫面玩家輸入後呼叫）。
        /// 應在 <see cref="InitializeAsNewGame"/> 呼叫之前執行。
        /// FSD §5.4-E。
        /// </summary>
        public void SetGuildName(string rawInput)
        {
            // 步驟 1：組合完整顯示名稱（ComposeDisplayName 內部執行 strip/trim/truncate/加後綴）
            string composed = GuildNameUtility.ComposeDisplayName(rawInput);

            // 步驟 2：保存 raw 部分（不含後綴）
            // 取截斷後的 raw：strip → trim → truncate（不加後綴）
            string stripped = GuildNameUtility.StripControlChars(rawInput ?? string.Empty);
            string trimmed = stripped.Trim();
            string rawPart = GuildNameUtility.TruncateToFullwidthLimit(trimmed, GuildCoreConstants.GUILD_NAME_MAX_FULLWIDTH);
            _state.guildName = rawPart; // 若空字串，維持空字串（displayName 已是 DEFAULT_GUILD_NAME）

            // 步驟 3：保存完整顯示名稱
            _state.displayName = composed;
        }

        /// <summary>
        /// 玩家確認訃聞後呼叫，觸發 Game Over 階段 2。
        /// 非 Pending 狀態呼叫為冪等（LogWarning + return）。
        /// FSD §5.4-D。
        /// </summary>
        public void ConfirmGameOver()
        {
            // 守衛：非 Pending 狀態冪等
            if (_gameOverState != GameOverState.Pending)
            {
                Debug.LogWarning(
                    $"[GuildCoreService] ConfirmGameOver 在非 Pending 狀態呼叫（當前狀態：{_gameOverState}），已忽略");
                return;
            }

            // 步驟 1：先 PauseTick，確保 invariant「gameOverState == Over ⇒ tick 已暫停」（FSD §5.4-D 設計選擇）
            TimeSystem.Instance.PauseTick();

            // 步驟 2：轉為 Over 狀態
            SetGameOverState(GameOverState.Over);

            // 步驟 3：取確認時間戳（PauseTick 後 NowUTC 仍可讀，F-02 FSD §5.1 保證）
            long confirmTimestamp = TimeSystem.Instance.NowUTC;

            // 步驟 4：發布 Game Over 事件（沿用 §5.4-C 緩存的 _pendingSnapshot，不重新 snapshot）
            EventBus.Publish(new OnGameOverEvent(
                _pendingSnapshot.PendingTimestamp,
                _pendingSnapshot.FinalGoldBeforeGameOver,
                _pendingSnapshot.FinalReputation,
                _pendingSnapshot.FinalLevel,
                _pendingSnapshot.FinalTitle,
                _pendingSnapshot.GuildDisplayName,
                _pendingSnapshot.FoundingTimestamp,
                confirmTimestamp));
        }

        // ── Test Support ──────────────────────────────────────────────────────────

        /// <summary>
        /// 測試用初始化（對齊 OutcomeResolutionService / AdventurerRoster 既有模式）。
        /// </summary>
        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        /// <summary>
        /// 測試用 Reset（清除 singleton 實例與 static 狀態）。
        /// </summary>
        internal static void ResetForTests()
        {
            if (Instance != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(Instance.gameObject);
#else
                Destroy(Instance.gameObject);
#endif
                Instance = null;
            }
        }

        // ── Private Methods ───────────────────────────────────────────────────────

        private void InitializeInstance()
        {
            // Singleton 防衛
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            // 初始化 loader（RegisterTable 已在 RuntimeInitializeOnLoadMethod 執行）
            _loader = new GuildLevelDatabaseLoader();
            _loader.Load(DataManager.Instance.GetAll<GuildLevelEntry>());

            // 初始化 state（待 InitializeAsNewGame 或 RestoreFromSave 填充）
            _state = new GuildState
            {
                displayName = GuildCoreConstants.DEFAULT_GUILD_NAME
            };

            // runtime enum 預設 Active（尚未呼叫 InitializeAsNewGame 前不對外有效）
            _gameOverState = GameOverState.Active;
            _state.gameOverState = GameOverState.Active.ToString();

            // §5.4-C snapshot 緩存預設值
            _pendingSnapshot = default;
        }

        /// <summary>
        /// §5.4-A：處理聲望變動事件，判定目標等級並填充連跳 queue。
        /// 疑點 1 決策：使用 evt.CurrentReputation（等同 FSD 偽碼的 evt.newValue）。
        /// </summary>
        private void HandleReputationChanged(OnReputationChangedEvent evt)
        {
            // 守衛：非 Active 狀態不處理升級（FSD §5.3.4）
            if (_gameOverState != GameOverState.Active)
            {
                return;
            }

            // 步驟 1：計算目標等級（CurrentReputation 等同 FSD 偽碼的 newValue）
            // 疑點 1：實際 payload 欄位為 CurrentReputation，非 FSD 文件寫的 newValue
            int targetLevel = _loader.FindTargetLevel(evt.CurrentReputation);

            // 步驟 2：計算當前已排程到的最高等級
            int alreadyScheduledTop = _state.currentLevel + _pendingLevelUpQueue.Count;

            // 步驟 3：目標等級不超過已排程，不降級也不重排
            if (targetLevel <= alreadyScheduledTop)
            {
                return;
            }

            // 步驟 4：補 enqueue 缺漏的升級任務
            for (int k = alreadyScheduledTop + 1; k <= targetLevel; k++)
            {
                _pendingLevelUpQueue.Add(new LevelUpPayload
                {
                    fromLv = k - 1,
                    toLv = k,
                    finalTargetLv = targetLevel,
                    isMultiJump = (targetLevel - _state.currentLevel) > 1,
                    reputationAtUpgrade = evt.CurrentReputation
                });
            }

            // 步驟 5：回填所有 pending payload 的 finalTargetLv / isMultiJump / reputationAtUpgrade
            // （含舊有與新加入的，確保敘事一致性；fromLv/toLv 不更新——維持逐級遞增序列）
            // 疑點 2 決策：List<T> in-place 修改，無需 dequeue/enqueue 重建
            bool newIsMultiJump = (targetLevel - _state.currentLevel) > 1;
            for (int i = 0; i < _pendingLevelUpQueue.Count; i++)
            {
                LevelUpPayload p = _pendingLevelUpQueue[i];
                p.finalTargetLv = targetLevel;
                p.isMultiJump = newIsMultiJump;
                p.reputationAtUpgrade = evt.CurrentReputation;
                _pendingLevelUpQueue[i] = p;
            }

            // 步驟 6：此 frame 不發事件，交由 Update() 接手
        }

        /// <summary>
        /// §5.4-B：Update() 每 LEVEL_UP_QUEUE_INTERVAL_FRAMES 取出一個 payload 並發布升級事件。
        /// 熱路徑：只在 queue 非空時進行，節流計數也在 queue 非空時才累積。
        /// </summary>
        private void ProcessLevelUpQueue()
        {
            if (_pendingLevelUpQueue.Count == 0)
            {
                // queue 空時重置計數器，不進行任何 alloc
                _levelUpFrameCounter = 0;
                return;
            }

            // §5.2.2：Game Over 優先於升級動畫——直接清空 queue
            if (_gameOverState != GameOverState.Active)
            {
                _pendingLevelUpQueue.Clear();
                _levelUpFrameCounter = 0;
                return;
            }

            // frame 節流：未達間隔則本 frame 不發事件
            _levelUpFrameCounter += 1;
            if (_levelUpFrameCounter < GuildCoreConstants.LEVEL_UP_QUEUE_INTERVAL_FRAMES)
            {
                return;
            }

            _levelUpFrameCounter = 0;

            // 取出最前一個 payload（List.RemoveAt(0)，最多 4 個元素，無效能問題）
            LevelUpPayload payload = _pendingLevelUpQueue[0];
            _pendingLevelUpQueue.RemoveAt(0);

            // 更新當前等級
            _state.currentLevel = payload.toLv;

            // 取兩端 entry（即時讀表）
            GuildLevelEntry entryFrom = _loader.GetEntry(payload.fromLv);
            GuildLevelEntry entryTo = _loader.GetEntry(payload.toLv);

            if (entryFrom == null || entryTo == null)
            {
                // 資料異常不發事件，已由 GetEntry 輸出 LogError
                return;
            }

            // 組合並發布升級事件
            EventBus.Publish(new OnGuildLevelChangedEvent(
                fromLv: payload.fromLv,
                toLv: payload.toLv,
                fromTitle: entryFrom.title,
                toTitle: entryTo.title,
                newMaxDifficulty: entryTo.maxDifficulty,         // deprecated 欄位
                newMaxRecruitableRank: entryTo.maxRecruitableRank,
                newMaxMissionDifficulty: entryTo.maxMissionDifficulty,
                reputationAtUpgrade: payload.reputationAtUpgrade,
                upgradeTimestamp: TimeSystem.Instance.NowUTC,   // 取出發送瞬間時間戳
                isMultiJump: payload.isMultiJump,
                finalTargetLv: payload.finalTargetLv));
        }

        /// <summary>
        /// §5.4-C：處理破產事件，觸發 Game Over 階段 1（Pending）。
        /// </summary>
        private void HandleBankruptcyState(OnBankruptcyStateChangedEvent evt)
        {
            // 守衛：只處理 Bankrupt 狀態（忽略 PreviousState）
            if (evt.CurrentState != BankruptcyWarningState.Bankrupt)
            {
                return;
            }

            // 守衛：已 Pending/Over 時冪等（FSD §5.3.2）
            if (_gameOverState != GameOverState.Active)
            {
                return;
            }

            // 步驟 1：轉為 Pending 狀態
            SetGameOverState(GameOverState.Pending);

            // 步驟 2：建立並緩存 Game Over snapshot（§5.4-D 直接重用，不重新 snapshot）
            _pendingSnapshot = new OnGameOverPendingEvent(
                pendingTimestamp: TimeSystem.Instance.NowUTC,
                finalGoldBeforeGameOver: ResourceManagement.Instance.GetGold(),
                finalReputation: ResourceManagement.Instance.GetReputation(),
                finalLevel: _state.currentLevel,
                finalTitle: _loader.GetEntry(_state.currentLevel)?.title ?? string.Empty,
                guildDisplayName: _state.displayName,
                foundingTimestamp: _state.foundingTimestamp);

            // 步驟 3：發布 Game Over Pending 事件
            EventBus.Publish(_pendingSnapshot);
        }

        /// <summary>
        /// 同步更新 runtime enum 與序列化字串，確保兩者一致。
        /// 所有狀態轉移必須透過此 helper，不直接賦值。
        /// </summary>
        private void SetGameOverState(GameOverState s)
        {
            _gameOverState = s;
            _state.gameOverState = s.ToString();
        }
    }
}
