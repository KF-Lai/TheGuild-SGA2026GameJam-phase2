using System;
using System.Collections.Generic;
using System.Linq;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.Outcome.Events;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Outcome
{
    /// <summary>
    /// FT-04 任務結算服務（concrete singleton MonoBehaviour）。
    /// 訂閱 OnMissionCompleted，執行 12 步結算管線：擲骰判定 → condition 特質套用
    /// → 狀態映射 → 聲望更新 → 發布 OnMissionResolved → 清理 ActiveMission。
    /// FSD §5.4 / GDD §3.2；步驟順序嚴格不可調換（原子性交易）。
    /// </summary>
    [DefaultExecutionOrder(140)]   // 晚於 MissionDispatchService=130
    public sealed class OutcomeResolutionService : MonoBehaviour, IOutcomeResolutionService
    {
        private readonly OutcomeReputationCalculator _calculator = new OutcomeReputationCalculator();

        // 成功時死亡率折扣係數，來自 SystemConstants.DEATH_RATE_ON_SUCCESS_MULTIPLIER
        private float _deathRateMultiplier;

        // === v3.1 patch P3.1-003：FT-09 StyleTag bias hook（FT-09 實作完成前 fallback Dark）===
        // FT-09 缺席時此 Func 始終回傳 StyleTag.Dark，CalcAdjustedJitter 的 Light 分支不生效。
        // FT-09 完成後可替換此 Func 以對接 FactionStoryService.GetCurrentStyleTagBias()。
        internal Func<StyleTag> StyleTagBiasProvider = () => StyleTag.Dark;

        /// <summary>全域單例。</summary>
        public static OutcomeResolutionService Instance { get; private set; }

        // ── RuntimeInitializeOnLoadMethod ────────────────────────────────────────

        /// <summary>
        /// 在場景載入前向 DataManager 註冊 ReputationDeltaTable。
        /// 對齊 C-01 / C-02 等既有系統的相同模式。
        /// FSD §6.1；DataManager.RegisterTable 必須早於 Awake。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<ReputationDeltaData>("ReputationDeltaTable");
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            // 訂閱 FT-02 發出的任務完成事件，觸發結算管線
            EventBus.Subscribe<OnMissionCompletedEvent>(HandleMissionCompleted);
        }

        private void OnDisable()
        {
            // 對稱取消訂閱，防止 GC 後仍收到事件
            EventBus.Unsubscribe<OnMissionCompletedEvent>(HandleMissionCompleted);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── Test Support ──────────────────────────────────────────────────────────

        /// <summary>
        /// 測試用初始化（對齊 AdventurerRoster / MissionDispatchService 既有模式）。
        /// </summary>
        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        /// <summary>
        /// 測試用 Reset（銷毀 singleton 並清空靜態狀態）。
        /// </summary>
        internal static void ResetForTests()
        {
            if (Instance != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(Instance.gameObject);
#else
                Destroy(Instance.gameObject);
#endif
                Instance = null;
            }
        }

        // ── 主結算管線 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 處理 FT-02 發出的任務完成事件，執行 12 步結算管線。
        /// FSD §5.4；步驟順序依 GDD §3.2 嚴格約束，不可調換。
        /// </summary>
        private void HandleMissionCompleted(OnMissionCompletedEvent evt)
        {
            // 步驟 1：取得 ActiveMission 快照
            ActiveMission activeMission = MissionDispatchService.Instance != null
                ? MissionDispatchService.Instance.GetActiveMission(evt.ActiveMissionID)
                : null;
            if (activeMission == null)
            {
                Debug.LogError(
                    $"[OutcomeResolutionService] 步驟 1：找不到 activeMissionID={evt.ActiveMissionID} 的 ActiveMission，結算中止。");
                return;
            }

            // 步驟 2：取得冒險者實例並驗證狀態
            AdventurerInstance adventurer = AdventurerRoster.Instance != null
                ? AdventurerRoster.Instance.GetAdventurer(activeMission.adventurerInstanceID)
                : null;
            if (adventurer == null)
            {
                Debug.LogError(
                    $"[OutcomeResolutionService] 步驟 2：找不到 adventurerInstanceID={activeMission.adventurerInstanceID} 的冒險者，結算中止。");
                return;
            }

            // 非 Dispatched 狀態屬資料不一致（存檔中斷等邊緣案例），防禦性中止
            if (adventurer.status != AdventurerStatus.Dispatched)
            {
                Debug.LogError(
                    $"[OutcomeResolutionService] 步驟 2：冒險者 instanceID={adventurer.instanceID} 狀態為 {adventurer.status}（非 Dispatched），結算中止。");
                return;
            }

            // 步驟 3：取得任務模板
            MissionTemplate template = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(activeMission.missionID)
                : null;
            if (template == null)
            {
                Debug.LogError(
                    $"[OutcomeResolutionService] 步驟 3：找不到 missionID={activeMission.missionID} 的任務模板，結算中止。");
                return;
            }

            // 步驟 4：建立 Outcome 快照（填入靜態欄位）
            Outcome outcome = BuildOutcomeSnapshot(activeMission, template);

            // 步驟 5：執行兩次獨立擲骰，判定成功/死亡/受傷
            RollSuccessAndDeath(outcome, activeMission, template);

            // 步驟 6：從 ReputationDeltaTable 取基礎聲望 delta
            outcome.reputationDelta = _calculator.GetBaseDelta(outcome.missionDifficulty, outcome.isSuccess);

            // 步驟 7：套用 condition 特質修正（FSD CT-06 裁決：FT-04 為 owner，不依賴 C-05 ApplyConditionTraits）
            ApplyConditionTraits(outcome, adventurer.traitIDs, activeMission);

            // 步驟 8：映射最終冒險者狀態（三布林 → OutcomeStatus）
            outcome.finalStatus = MapFinalStatus(outcome);

            // 步驟 9：更新 C-02 冒險者狀態
            ApplyAdventurerStatus(outcome);

            // 步驟 10：套用聲望變化至 F-03（可能觸發破產狀態轉移，FT-06 控制；FT-04 不感知）
            ResourceManagement.Instance.AddReputation(outcome.reputationDelta);

            // 步驟 11：發布 OnMissionResolved 事件（必須先於步驟 12 RemoveActiveMission）
            // FSD §3.8：訂閱者（FT-05/FT-09/P-02/P-03）此時仍可查到 ActiveMission
            EventBus.Publish(new OnMissionResolvedEvent(outcome));

            // 步驟 12：清理 ActiveMission（此後 GetActiveMission 回傳 null）
            MissionDispatchService.Instance.RemoveActiveMission(outcome.activeMissionID);
        }

        // ── 管線子步驟 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 步驟 4：以 ActiveMission + MissionTemplate 建立 Outcome 快照。
        /// 填入靜態欄位（擲骰前可確定的資料）；triggeredConditionTraits 初始為空陣列。
        /// </summary>
        private static Outcome BuildOutcomeSnapshot(ActiveMission activeMission, MissionTemplate template)
        {
            Outcome outcome = new Outcome
            {
                activeMissionID       = activeMission.activeMissionID,
                missionID             = activeMission.missionID,
                adventurerInstanceID  = activeMission.adventurerInstanceID,
                missionDifficulty     = template.difficulty,
                missionTypeID         = template.typeID,
                missionFactionID      = template.factionID,
                // baseReward 從 C-01 取得，讓 FT-05 / condition 計算倍率用
                baseReward = MissionDatabaseService.Instance != null
                    ? MissionDatabaseService.Instance.GetBaseReward(template.difficulty)
                    : 0,
                triggeredConditionTraits = Array.Empty<int>()
            };

            return outcome;
        }

        /// <summary>
        /// 步驟 5：執行兩次獨立擲骰。
        /// 順序：successRoll → isSuccess → adjustedDeathRate → deathRoll → isDead → isWounded。
        /// 兩骰完全獨立，改變 successRoll 不影響 deathRoll（FSD §5.4 / GDD §3.3）。
        /// finalSuccessRate / finalDeathRate 若超出 [0,1] 先 clamp 並 LogWarning（FSD §7 §5.2）。
        /// isScriptedDeath=1 時 short-circuit，不擲骰直接設哨兵值（v3.1 patch P3.1-003）。
        /// </summary>
        private void RollSuccessAndDeath(Outcome outcome, ActiveMission activeMission, MissionTemplate template)
        {
            // === v3.1 patch P3.1-003：isScriptedDeath short-circuit ===
            if (template.isScriptedDeath == 1)
            {
                outcome.isSuccess         = false;
                outcome.isDead            = true;
                outcome.isWounded         = false;
                outcome.adjustedDeathRate = 1.0f;
                outcome.successRoll       = -1.0f;  // 哨兵值（正常 [0,1)），標記未擲骰
                outcome.deathRoll         = -1.0f;
                return;
            }

            // 入口 clamp：FT-02 派遣時已 clamp，但仍可能因資料異常超界
            float clampedSuccess = Mathf.Clamp01(activeMission.finalSuccessRate);
            if (!Mathf.Approximately(clampedSuccess, activeMission.finalSuccessRate))
            {
                Debug.LogWarning(
                    $"[OutcomeResolutionService] finalSuccessRate={activeMission.finalSuccessRate} 超出 [0,1]，clamp 至 {clampedSuccess}。");
            }

            float clampedDeath = Mathf.Clamp01(activeMission.finalDeathRate);
            if (!Mathf.Approximately(clampedDeath, activeMission.finalDeathRate))
            {
                Debug.LogWarning(
                    $"[OutcomeResolutionService] finalDeathRate={activeMission.finalDeathRate} 超出 [0,1]，clamp 至 {clampedDeath}。");
            }

            // 第一骰：成功判定（Random.value 回傳 [0, 1)，clampedSuccess=1.0 時必成功，0.0 時必失敗）
            outcome.successRoll = UnityEngine.Random.value;
            outcome.isSuccess = outcome.successRoll < clampedSuccess;

            // 成功路徑死亡率打折（鼓勵冒險的設計幻想，FSD §3.3）
            outcome.adjustedDeathRate = outcome.isSuccess
                ? clampedDeath * _deathRateMultiplier
                : clampedDeath;

            // 第二骰：死亡判定（與第一骰完全獨立）
            outcome.deathRoll = UnityEngine.Random.value;
            outcome.isDead = outcome.deathRoll < outcome.adjustedDeathRate;

            // 失敗+存活 → 受傷（成功或死亡均不受傷）
            outcome.isWounded = !outcome.isSuccess && !outcome.isDead;
        }

        /// <summary>
        /// 步驟 7：套用 condition 特質修正。
        /// FT-04 內部 method（FSD CT-06 裁決：FT-04 為 owner，不依賴 C-05 ApplyConditionTraits）。
        /// 對每個 traitID 取得定義後判定 effectType == "condition" 並擲骰觸發機率。
        /// 重要：on_death_survive 可將 isDead 改為 false；後續 MapFinalStatus 會以修改後值映射。
        /// isScriptedDeath=1 時禁止 on_death_survive / on_fail_survive 觸發（v3.1 patch P3.1-003）。
        /// </summary>
        private void ApplyConditionTraits(Outcome outcome, int[] traitIDs, ActiveMission activeMission)
        {
            if (traitIDs == null || traitIDs.Length == 0)
            {
                return;
            }

            // === v3.1 patch P3.1-003：isScriptedDeath 守衛 ===
            // 劇本必死任務禁止 on_death_survive / on_fail_survive 觸發
            MissionTemplate activeTemplate = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(activeMission.missionID)
                : null;
            if (activeTemplate != null && activeTemplate.isScriptedDeath == 1)
            {
                traitIDs = traitIDs.Where(traitID =>
                {
                    TraitData trait = TraitService.Instance != null
                        ? TraitService.Instance.GetTrait(traitID)
                        : null;
                    return trait != null
                        && !string.Equals(trait.effectTarget, "on_death_survive", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(trait.effectTarget, "on_fail_survive", StringComparison.OrdinalIgnoreCase);
                }).ToArray();
            }

            // 預分配容量避免熱路徑 alloc；通常特質數量不多
            List<int> triggered = new List<int>(traitIDs.Length);

            for (int i = 0; i < traitIDs.Length; i++)
            {
                TraitData trait = TraitService.Instance != null
                    ? TraitService.Instance.GetTrait(traitIDs[i])
                    : null;

                // null：traitID=0 sentinel 或找不到，自然跳過
                if (trait == null)
                {
                    continue;
                }

                // 只處理 condition 類型特質（大小寫不敏感，FSD §7 §5.4 疑點 2）
                if (!string.Equals(trait.effectType, "condition", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // effectValue 在 condition 特質中作為觸發機率 [0,1]
                // Random.value < effectValue → 觸發（effectValue=1.0 必觸發，0.0 必不觸發）
                if (UnityEngine.Random.value >= trait.effectValue)
                {
                    continue;
                }

                triggered.Add(traitIDs[i]);

                // effectTarget 正規化（trim + lowercase）
                string target = (trait.effectTarget ?? string.Empty).Trim().ToLowerInvariant();

                switch (target)
                {
                    case "on_death_survive":
                        // 救活：死亡翻轉為受傷（FSD §5.4 步驟 7 / §7 §5.4）
                        if (outcome.isDead)
                        {
                            outcome.isDead = false;
                            outcome.isWounded = true;
                        }
                        break;

                    case "on_success_gold_bonus":
                        // effectValue 詮釋為 baseReward 的倍率（待 GDD/FSD 後續澄清）
                        // 理由：effectValue 主要語意已是觸發機率，用原值作為金幣數量語義不一致；
                        // 以倍率形式 Mathf.RoundToInt(effectValue * baseReward) 更符合「bonus」語意。
                        if (outcome.isSuccess)
                        {
                            outcome.conditionGoldBonus += Mathf.RoundToInt(trait.effectValue * outcome.baseReward);
                        }
                        break;

                    case "on_success_reputation":
                        if (outcome.isSuccess)
                        {
                            outcome.reputationDelta += Mathf.RoundToInt(trait.effectValue);
                        }
                        break;

                    case "on_fail_reputation":
                        if (!outcome.isSuccess)
                        {
                            outcome.reputationDelta += Mathf.RoundToInt(trait.effectValue);
                        }
                        break;

                    case "on_death_reputation":
                        // 注意：此處使用 condition 套用後的 isDead（on_death_survive 可能已翻轉）
                        if (outcome.isDead)
                        {
                            outcome.reputationDelta += Mathf.RoundToInt(trait.effectValue);
                        }
                        break;

                    default:
                        // 未知 effectTarget 只記錄警告，不影響其他特質繼續執行（FSD §7 §5.4）
                        // 不從 triggered 移除：triggeredConditionTraits 為「機率判定通過」清單（FSD §5.3），
                        // 即使 effectTarget 未知無法套用狀態，仍應登記為「擲骰通過」供下游 UI 顯示。
                        Debug.LogWarning(
                            $"[OutcomeResolutionService] ApplyConditionTraits: 未知 condition effectTarget={trait.effectTarget}，跳過套用但保留於 triggered。");
                        break;
                }
            }

            outcome.triggeredConditionTraits = triggered.Count == 0
                ? Array.Empty<int>()
                : triggered.ToArray();
        }

        /// <summary>
        /// 步驟 8：三布林值映射至 OutcomeStatus（FSD §5.4 / GDD §3.5）。
        /// 優先順序：Dead > Wounded > Idle。
        /// </summary>
        private static OutcomeStatus MapFinalStatus(Outcome outcome)
        {
            if (outcome.isDead)
            {
                return OutcomeStatus.Dead;
            }

            if (outcome.isWounded)
            {
                return OutcomeStatus.Wounded;
            }

            return OutcomeStatus.Idle;
        }

        /// <summary>
        /// 步驟 9：依 finalStatus 更新 C-02 冒險者狀態（FSD §5.4 / GDD §3.7）。
        /// Wounded 呼叫 SetWounded（內部計算 woundedUntilTimestamp），其餘呼叫 UpdateStatus。
        /// </summary>
        private static void ApplyAdventurerStatus(Outcome outcome)
        {
            if (AdventurerRoster.Instance == null)
            {
                Debug.LogError("[OutcomeResolutionService] 步驟 9：AdventurerRoster.Instance 為 null，無法更新冒險者狀態。");
                return;
            }

            switch (outcome.finalStatus)
            {
                case OutcomeStatus.Idle:
                    AdventurerRoster.Instance.UpdateStatus(outcome.adventurerInstanceID, AdventurerStatus.Idle);
                    break;

                case OutcomeStatus.Wounded:
                    AdventurerRoster.Instance.SetWounded(outcome.adventurerInstanceID);
                    break;

                case OutcomeStatus.Dead:
                    AdventurerRoster.Instance.UpdateStatus(outcome.adventurerInstanceID, AdventurerStatus.Dead);
                    break;

                default:
                    Debug.LogError(
                        $"[OutcomeResolutionService] 步驟 9：未知 finalStatus={outcome.finalStatus}，無法更新冒險者狀態。");
                    break;
            }
        }

        // ── 私有方法 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// === v3.1 patch P3.1-003：styleTag jitter modifier (FB-M1) ===
        /// 依當前 styleTag bias / dangerLevel / mission.factionID 調整 jitter。
        /// bias=Light + dangerLevel >= 2 (C 暗湧) + mission.factionID=1 (女神陣營) → baseJitter - 0.04f
        /// 其他情境回傳 baseJitter（不變）。
        /// 設計用途：FB-M1「玩家最近運氣特別差」的機制感受來源。
        /// 依賴 FT-09 GetCurrentStyleTagBias()——透過 StyleTagBiasProvider hook（line ~35），
        /// FT-09 缺席時 fallback 回 StyleTag.Dark，此公式不生效。
        /// 注意：v3.1 階段此方法為**預留方法**，待 FT-09 實作完成後由呼叫端串接 jitter pipeline。
        /// </summary>
        internal float CalcAdjustedJitter(float baseJitter, MissionTemplate mission, int currentDangerLevelIndex)
        {
            StyleTag bias = StyleTagBiasProvider != null
                ? StyleTagBiasProvider.Invoke()
                : StyleTag.Dark;

            if (bias == StyleTag.Light
                && currentDangerLevelIndex >= 2
                && mission.factionID == 1)
            {
                return baseJitter - 0.04f;
            }

            return baseJitter;
        }

        /// <summary>
        /// Singleton 初始化：DontDestroyOnLoad → 建立 calculator → 讀取常數。
        /// 對齊 ResourceManagement / MissionDispatchService 既有模式。
        /// </summary>
        private void InitializeInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
#if UNITY_EDITOR
                    DestroyImmediate(gameObject);
#endif
                }
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            // 初始化聲望計算器（從 DataManager 取全部 ReputationDeltaData）
            if (DataManager.Instance != null)
            {
                _calculator.Initialize(DataManager.Instance.GetAll<ReputationDeltaData>());
            }
            else
            {
                Debug.LogError("[OutcomeResolutionService] Awake: DataManager.Instance 為 null，聲望計算器使用空表啟動。");
                _calculator.Initialize(null);
            }

            // 讀取死亡率折扣係數（DataManager 查無 key 時內部 LogError 並回傳 0f）
            float rawMultiplier = DataManager.Instance != null
                ? DataManager.Instance.GetFloat("DEATH_RATE_ON_SUCCESS_MULTIPLIER")
                : 0f;

            // 0f 表示 key 缺失（DataManager 已 LogError），使用 fallback 0.5（FSD §5.4 / §8.5 CT-08 裁決）
            if (rawMultiplier == 0f)
            {
                _deathRateMultiplier = 0.5f;
                Debug.LogWarning("[OutcomeResolutionService] DEATH_RATE_ON_SUCCESS_MULTIPLIER 缺失或為 0，使用 fallback 0.5。");
            }
            else if (rawMultiplier < 0f || rawMultiplier > 1f)
            {
                // 超出 [0,1] clamp + LogWarning（FSD §7 §5.1）
                _deathRateMultiplier = Mathf.Clamp01(rawMultiplier);
                Debug.LogWarning(
                    $"[OutcomeResolutionService] DEATH_RATE_ON_SUCCESS_MULTIPLIER={rawMultiplier} 超出 [0,1]，clamp 至 {_deathRateMultiplier}。");
            }
            else
            {
                _deathRateMultiplier = rawMultiplier;
            }
        }
    }
}
