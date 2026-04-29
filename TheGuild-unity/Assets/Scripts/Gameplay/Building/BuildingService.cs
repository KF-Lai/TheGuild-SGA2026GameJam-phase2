using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Resources;
using UnityEngine;

// FT-07 Guild Building System — 主服務
// 實作依據：【FT-07-FSD】guild-building-system.md §4.4 / §5.1~§5.4
// 職責（SRP）：持有 BuildingState[6]、實作 7 個查詢 API、TryUpgradeBuilding、
//              ISaveable 序列化（Serialize / RestoreFromSave / InitializeAsNewGame）、
//              保險櫃啟動與升級時推送破產倒數秒數至 F-03

// ── ISaveable TODO ────────────────────────────────────────────────────────────
// TODO(FT-10)：待 ISaveable 介面建立後，於類別宣告加上 `: ISaveable`，
// 並將 IBuildingService 中的 ISaveable 衍生簽名移至獨立介面。
// 目前保留全部 ISaveable 方法簽名（OwnerKey / IsCritical / Serialize /
// RestoreFromSave / InitializeAsNewGame）為 public，FT-10 整合時無需修改方法本身。
// ─────────────────────────────────────────────────────────────────────────────

namespace TheGuild.Gameplay.Building
{
    /// <summary>
    /// FT-07 建築服務（concrete singleton MonoBehaviour）。
    /// 管理 6 棟建築的等級狀態、升級流程與效果值查詢。
    /// DefaultExecutionOrder=160：晚於 FT-06 GuildCoreService（150），確保 Awake 時上游已初始化。
    /// FSD §4.4 / §5.1~§5.4.3。
    /// </summary>
    [DefaultExecutionOrder(160)]
    public sealed class BuildingService : MonoBehaviour, IBuildingService, ISaveable
    {
        // ── Singleton ────────────────────────────────────────────────────────────

        /// <summary>全域唯一實例。</summary>
        public static BuildingService Instance { get; private set; }

        // ── ISaveable 常數 ───────────────────────────────────────────────────────

        /// <summary>FT-10 ISaveable owner 識別鍵。</summary>
        public string OwnerKey => "ft07Buildings";

        /// <summary>FT-10 ISaveable 是否關鍵（本系統非關鍵）。</summary>
        public bool IsCritical => false;

        // ── Private Fields ───────────────────────────────────────────────────────

        private BuildingTableLoader _loader;

        // index = buildingID - 1（0~5 對應 buildingID 1~6）
        private BuildingState[] _buildingStates = new BuildingState[6];

        // ── RuntimeInitializeOnLoadMethod ────────────────────────────────────────

        /// <summary>
        /// 在場景載入前向 DataManager 註冊 BuildingTable。
        /// 對齊 F-01 FSD 去中心化機制（下游各自 RegisterTable）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<BuildingRow>("BuildingTable");
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
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

            // 建立 loader 並載入/驗證資料表（驗證失敗時 Unity 會 catch Awake 例外並輸出 console error）
            // DataManager.Instance null guard：對齊 FT-04 / FT-06 既有模式，降級而非 NRE
            _loader = new BuildingTableLoader();
            if (DataManager.Instance != null)
            {
                _loader.Initialize(DataManager.Instance.GetAll<BuildingRow>());
            }
            else
            {
                Debug.LogError("[BuildingService] Awake: DataManager.Instance 為 null，loader 未初始化，後續查詢將拋 NRE。");
            }

            // 初始化 _buildingStates 陣列（等級值由後續 InitializeAsNewGame 或 RestoreFromSave 填充）
            for (int i = 0; i < _buildingStates.Length; i++)
            {
                _buildingStates[i] = new BuildingState
                {
                    buildingID = i + 1,
                    currentLevel = 0
                };
            }
        }

        private void Start()
        {
            // 啟動推送：不論新遊戲或讀檔，統一在 Start 推送保險櫃倒數秒數至 F-03。
            // FSD §5.4.1 步驟 1~2 / §5.4.2 步驟 3（搬移至此）。
            // 等價性說明：InitializeAsNewGame / RestoreFromSave 均在 FT-10 Bootstrap 的 Awake 階段執行，
            // Start 時狀態已就緒，可安全推送。FSD §8.4 GDD 回註。
            int seconds = GetBankruptcyWarningSeconds();
            ResourceManagement.Instance.SetBankruptcyWarningDuration(seconds);

            // Jam 版 Phase 2 封鎖：不訂閱 OnDailyResetEvent。
            // Phase 2 啟用時：於 OnEnable 加 EventBus.Subscribe<OnDailyResetEvent>(OnDailyReset)，
            //                 於 OnDisable 加 EventBus.Unsubscribe<OnDailyResetEvent>(OnDailyReset)。
            // FSD §5.4.5 / §7 Case 8.8（AC-20）。
        }

        private void OnEnable()
        {
            // Jam 版無事件訂閱。Phase 2 在此新增 OnDailyResetEvent 訂閱。
        }

        private void OnDisable()
        {
            // Jam 版無事件退訂。Phase 2 在此新增 OnDailyResetEvent 退訂。
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── ISaveable Methods ─────────────────────────────────────────────────────

        /// <summary>
        /// 序列化 6 棟建築 <see cref="BuildingState"/> 為 JSON。
        /// FSD §5.4.2（Serialize 形狀：BuildingStateList.states[]）。
        /// </summary>
        public string Serialize()
        {
            BuildingStateList wrapper = new BuildingStateList
            {
                states = new List<BuildingState>(_buildingStates)
            };
            return JsonUtility.ToJson(wrapper);
        }

        /// <summary>
        /// 從存檔 JSON 還原建築狀態；超出 maxLevel 的等級 clamp + LogWarning。
        /// 不在本方法內推送保險櫃倒數秒數（統一由 Start 推送）。
        /// FSD §5.4.2。
        /// </summary>
        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                // 空字串視為新遊戲資料，委派給 InitializeAsNewGame
                InitializeAsNewGame();
                return;
            }

            BuildingStateList wrapper = JsonUtility.FromJson<BuildingStateList>(ownerJson);
            if (wrapper == null || wrapper.states == null)
            {
                Debug.LogError("[BuildingService] RestoreFromSave：反序列化結果為 null，改用 InitializeAsNewGame。");
                InitializeAsNewGame();
                return;
            }

            // 還原各建築狀態，超出上限時 clamp + LogWarning
            for (int i = 0; i < wrapper.states.Count; i++)
            {
                BuildingState state = wrapper.states[i];
                int idx = state.buildingID - 1;

                // 邊界守衛
                if (idx < 0 || idx >= _buildingStates.Length)
                {
                    Debug.LogWarning(
                        $"[BuildingService] RestoreFromSave：存檔中 buildingID={state.buildingID} 超出合法範圍 [1,6]，已略過。");
                    continue;
                }

                int maxLv = _loader.GetMaxLevel(state.buildingID);
                if (state.currentLevel > maxLv)
                {
                    Debug.LogWarning(
                        $"[BuildingService] buildingID={state.buildingID}: clamped from {state.currentLevel} to {maxLv}");
                    state.currentLevel = maxLv;
                }

                // FSD §5.4.2：不在本方法推送 SetBankruptcyWarningDuration；推送統一在 Start 階段。
                _buildingStates[idx] = state;
            }
        }

        /// <summary>
        /// 新遊戲初始化：buildingID 1~5 currentLevel=1，buildingID 6 currentLevel=0。
        /// FSD §5.4.1 步驟 1~2。
        /// </summary>
        public void InitializeAsNewGame()
        {
            // buildingID 1~5：初始等級 1
            for (int i = 0; i < 5; i++)
            {
                _buildingStates[i].buildingID = i + 1;
                _buildingStates[i].currentLevel = 1;
            }

            // buildingID 6（職員休息室）：初始等級 0（尚未建造）
            _buildingStates[5].buildingID = 6;
            _buildingStates[5].currentLevel = 0;
        }

        // ── 查詢 API（FSD §5.1 / §5.4.4）──────────────────────────────────────

        /// <summary>
        /// 回傳指定建築的當前等級。
        /// buildingID 不在 1~6 → 拋 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>
        public int GetBuildingLevel(int buildingID)
        {
            if (buildingID < 1 || buildingID > 6)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildingID),
                    buildingID,
                    "buildingID 必須在 1~6 範圍內。");
            }

            return _buildingStates[buildingID - 1].currentLevel;
        }

        /// <summary>
        /// 委託板（buildingID=1）的委託槽位數（effectValue）。
        /// </summary>
        public int GetMissionSlotCount()
        {
            return _loader.GetRow(1, _buildingStates[0].currentLevel).effectValue;
        }

        /// <summary>
        /// 招募廣告欄（buildingID=2）的候選池刷新間隔（effectValue 秒 → TimeSpan）。
        /// </summary>
        public TimeSpan GetRecruitRefreshInterval()
        {
            int seconds = _loader.GetRow(2, _buildingStates[1].currentLevel).effectValue;
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// 公會大廳（buildingID=3）的冒險者名冊上限（effectValue）。
        /// </summary>
        public int GetRosterCap()
        {
            return _loader.GetRow(3, _buildingStates[2].currentLevel).effectValue;
        }

        /// <summary>
        /// 公會櫃臺（buildingID=4）的並行任務上限（effectValue）。
        /// </summary>
        public int GetMaxConcurrentMissions()
        {
            return _loader.GetRow(4, _buildingStates[3].currentLevel).effectValue;
        }

        /// <summary>
        /// 預備金保險櫃（buildingID=5）的破產警告持續秒數（effectValue）。
        /// </summary>
        public int GetBankruptcyWarningSeconds()
        {
            return _loader.GetRow(5, _buildingStates[4].currentLevel).effectValue;
        }

        /// <summary>
        /// 職員休息室（buildingID=6）currentLevel &gt;= 1 時回傳 true，解鎖 FT-08 / FT-12。
        /// </summary>
        public bool IsStaffSystemUnlocked()
        {
            return GetBuildingLevel(6) >= 1;
        }

        // ── 升級 API（FSD §5.1 / §5.4.3）────────────────────────────────────────

        /// <summary>
        /// 快速檢查指定建築是否可升級（三閘：已滿級 / 公會等級 / 金幣）。
        /// 不修改任何狀態，P-02 用於按鈕 enable 判定。
        /// FSD §5.4.3 步驟 2~5（只查詢不寫入）。
        /// </summary>
        public bool CanUpgrade(int buildingID)
        {
            if (buildingID < 1 || buildingID > 6)
            {
                return false;
            }

            BuildingState state = _buildingStates[buildingID - 1];
            BuildingRow row = _loader.GetRow(buildingID, state.currentLevel);

            // 閘門 1：已達最高等級
            if (state.currentLevel >= row.maxLevel)
            {
                return false;
            }

            int nextLv = state.currentLevel + 1;
            BuildingRow nextRow = _loader.GetRow(buildingID, nextLv);

            // 閘門 2：公會等級不足
            if (GuildCoreService.Instance.GetCurrentLevel() < nextRow.guildLevelReq)
            {
                return false;
            }

            // 閘門 3：金幣不足
            if (ResourceManagement.Instance.GetGold() < nextRow.upgradeCost)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 嘗試升級指定建築，回傳升級結果碼。
        /// 依照 FSD §5.4.3 十步驟流程（等價變體 V-01：保險櫃推送在事件發布之前）。
        /// </summary>
        public UpgradeResult TryUpgradeBuilding(int buildingID)
        {
            // 邊界守衛：與 GetBuildingLevel 行為一致，非法 buildingID 拋 ArgumentOutOfRangeException
            if (buildingID < 1 || buildingID > 6)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildingID),
                    buildingID,
                    "buildingID 必須在 1~6 範圍內。");
            }

            // 步驟 1：取當前狀態與資料列
            BuildingState state = _buildingStates[buildingID - 1];
            BuildingRow row = _loader.GetRow(buildingID, state.currentLevel);

            // 步驟 2：已達最高等級
            if (state.currentLevel >= row.maxLevel)
            {
                return UpgradeResult.ALREADY_MAX;
            }

            // 步驟 3：計算下一等級與對應資料列
            int nextLv = state.currentLevel + 1;
            BuildingRow nextRow = _loader.GetRow(buildingID, nextLv);

            // 步驟 4：公會等級閘門
            // 嚴禁寫 if nextLv >= 3 特例；guildLevelReq 由 CSV 資料決定（FSD §6.3 末條 / GDD §7.3）
            if (GuildCoreService.Instance.GetCurrentLevel() < nextRow.guildLevelReq)
            {
                return UpgradeResult.GUILD_LEVEL_INSUFFICIENT;
            }

            // 步驟 5：金幣閘門
            if (ResourceManagement.Instance.GetGold() < nextRow.upgradeCost)
            {
                return UpgradeResult.GOLD_INSUFFICIENT;
            }

            // 步驟 6：扣除升級費用（嚴格模式，不允許進入負值；GDD §5.2 / FSD §2.3）
            ResourceManagement.Instance.AddGold(-nextRow.upgradeCost);

            // 步驟 7：更新建築等級
            // BuildingState 為 class（引用型別），in-place 修改即可，無需 copy-back
            int fromLv = state.currentLevel;
            state.currentLevel = nextLv;
            // _buildingStates[buildingID - 1] 已持有同一引用，不需重新指派

            // 步驟 8：保險櫃推送（等價變體 V-01）
            // 必須在 EventBus.Publish 之前執行，確保 BuildingUpgradedEvent 訂閱者
            // 在回呼當下查 GetBankruptcyWarningSeconds() 即得新等級對應值。
            // FSD §5.4.3 步驟 8 / §8.2 等價變體 V-01。
            if (buildingID == 5)
            {
                ResourceManagement.Instance.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds());
            }

            // 步驟 9：發布升級事件
            EventBus.Publish(new BuildingUpgradedEvent(buildingID, fromLv, nextLv));

            // 步驟 10：回傳成功
            return UpgradeResult.SUCCESS;
        }

        // ── Test Support ──────────────────────────────────────────────────────────

        /// <summary>
        /// 測試用初始化（對齊既有 Singleton 模式）。
        /// </summary>
        internal void InitializeForTests()
        {
            _loader = new BuildingTableLoader();
            _loader.Initialize(DataManager.Instance.GetAll<BuildingRow>());

            for (int i = 0; i < _buildingStates.Length; i++)
            {
                _buildingStates[i] = new BuildingState
                {
                    buildingID = i + 1,
                    currentLevel = 0
                };
            }
        }

        /// <summary>
        /// 測試用 Reset（清除 singleton 實例）。
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

        // ── Nested Types ──────────────────────────────────────────────────────────

        /// <summary>
        /// 序列化用包裝類別（JsonUtility 不支援直接序列化頂層 List）。
        /// FSD §5.3 ISaveable Serialize JSON shape。
        /// </summary>
        [Serializable]
        private class BuildingStateList
        {
            public List<BuildingState> states;
        }
    }
}
