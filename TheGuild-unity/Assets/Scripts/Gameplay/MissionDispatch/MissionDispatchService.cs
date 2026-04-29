using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.WorldDanger;
using UnityEngine;

namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// FT-02-A 任務派遣服務（concrete singleton MonoBehaviour）。
    /// 持有 _activeMissions 執行期狀態、執行 Dispatch 10 步序列、TickCompletionCheck、ISaveable stub。
    /// GDD §3.5 / §3.7 / §3.8；FSD §5.1 / §5.4 流程 B / C / D。
    /// </summary>
    [DefaultExecutionOrder(130)]   // 在 AdventurerRoster=110 / Recovery=120 之後
    public sealed class MissionDispatchService : MonoBehaviour, ISaveable
    {
        // BuildingService.Instance 不可用時（測試環境 / 啟動異常）的保底值。
        private const int FALLBACK_MAX_CONCURRENT_MISSIONS = 5;

        private readonly List<ActiveMission> _activeMissions = new List<ActiveMission>(16);
        private readonly HashSet<int> _publishedCompletionIDs = new HashSet<int>();

        private int _nextActiveMissionID = 1;
        private MissionRateCalculator _calculator;

        public static MissionDispatchService Instance { get; private set; }

        // ── ISaveable Stub ────────────────────────────────────────────────────────

        /// <summary>ISaveable stub：owner key。</summary>
        public string OwnerKey => "ft02Dispatch";

        /// <summary>ISaveable stub：Degradable（非 critical）。</summary>
        public bool IsCritical => false;

        /// <summary>
        /// 序列化 FT-02 整體狀態：activeMissions + _nextActiveMissionID + 兩池（委派 CommissionBoardService）。
        /// FSD §6.4 / §5.3 ISaveable（OwnerKey 共用 "ft02Dispatch"）。
        /// </summary>
        public string Serialize()
        {
            FT02SaveDTO dto = new FT02SaveDTO
            {
                activeMissions = new List<ActiveMission>(_activeMissions),
                nextActiveMissionID = _nextActiveMissionID,
                regularMissionPool = new List<int>(),
                staticMissionPool = new List<int>(),
            };

            if (CommissionBoardService.Instance != null)
            {
                (List<int> regular, List<int> staticPool) = CommissionBoardService.Instance.GetSerializableState();
                dto.regularMissionPool = regular;
                dto.staticMissionPool = staticPool;
            }
            else
            {
                Debug.LogWarning("[MissionDispatchService] Serialize: CommissionBoardService.Instance 為 null，兩池序列化為空。");
            }

            return JsonUtility.ToJson(dto);
        }

        /// <summary>
        /// 從 JSON 還原 FT-02-A 狀態。
        /// FSD §5.4 流程 D restore 順序。
        /// </summary>
        public void RestoreFromSave(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[MissionDispatchService] RestoreFromSave: json 為空，無操作。");
                return;
            }

            FT02SaveDTO dto;
            try
            {
                dto = JsonUtility.FromJson<FT02SaveDTO>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MissionDispatchService] RestoreFromSave 反序列化失敗：{ex.Message}");
                throw;
            }

            if (dto == null)
            {
                Debug.LogError("[MissionDispatchService] RestoreFromSave: dto 反序列化為 null。");
                return;
            }

            _activeMissions.Clear();
            _publishedCompletionIDs.Clear();

            List<ActiveMission> raw = dto.activeMissions ?? new List<ActiveMission>();
            for (int i = 0; i < raw.Count; i++)
            {
                ActiveMission m = raw[i];
                if (m == null)
                {
                    continue;
                }

                // 冒險者找不到 → 跳過 + 發 OnMissionCancelledEvent（FSD §5.4 流程 D）
                if (AdventurerRoster.Instance == null)
                {
                    Debug.LogError($"[MissionDispatchService] RestoreFromSave: AdventurerRoster.Instance 為 null（C-02 應先於 FT-02-A restore），activeMissionID={m.activeMissionID} 跳過並發 OnMissionCancelled。");
                    EventBus.Publish(new OnMissionCancelledEvent(m.activeMissionID));
                    continue;
                }

                bool adventurerMissing = AdventurerRoster.Instance.GetAdventurer(m.adventurerInstanceID) == null;
                if (adventurerMissing)
                {
                    Debug.LogError($"[MissionDispatchService] RestoreFromSave: activeMissionID={m.activeMissionID} adventurerInstanceID={m.adventurerInstanceID} 找不到，跳過並發 OnMissionCancelled。");
                    EventBus.Publish(new OnMissionCancelledEvent(m.activeMissionID));
                    continue;
                }

                // missionID 找不到 → 保留 ActiveMission（快照已存）+ LogWarning（FSD §5.4 流程 D）
                bool templateMissing = MissionDatabaseService.Instance != null &&
                                       MissionDatabaseService.Instance.GetTemplate(m.missionID) == null;
                if (templateMissing)
                {
                    Debug.LogWarning($"[MissionDispatchService] RestoreFromSave: activeMissionID={m.activeMissionID} missionID={m.missionID} 找不到（CSV 已移除），保留快照。");
                }

                _activeMissions.Add(m);
            }

            // _nextActiveMissionID 取 Max(存檔值, max(activeMissionID)+1)（FSD §5.4 流程 D）
            int maxID = 0;
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                if (_activeMissions[i].activeMissionID > maxID)
                {
                    maxID = _activeMissions[i].activeMissionID;
                }
            }

            int savedNext = dto.nextActiveMissionID;
            int requiredNext = maxID + 1;
            _nextActiveMissionID = savedNext >= requiredNext ? savedNext : requiredNext;

            // 委派 FT-02-B 還原兩池（FSD §5.3：A 為主 ISaveable，B 暴露 RestoreState 由 A 委派）。
            if (CommissionBoardService.Instance != null)
            {
                CommissionBoardService.Instance.RestoreState(
                    dto.regularMissionPool ?? new List<int>(),
                    dto.staticMissionPool ?? new List<int>());
            }
            else
            {
                Debug.LogError("[MissionDispatchService] RestoreFromSave: CommissionBoardService.Instance 為 null，兩池無法還原。");
            }
        }

        /// <summary>
        /// 新遊戲初始化（空狀態）。
        /// FSD §6.4 InitializeAsNewGame()。
        /// </summary>
        public void InitializeAsNewGame()
        {
            _activeMissions.Clear();
            _publishedCompletionIDs.Clear();
            _nextActiveMissionID = 1;
        }

        // ── 公開 API（FSD §5.1）────────────────────────────────────────────────────

        /// <summary>
        /// 計算冒險者對任務的最終成功率/死亡率（委派給 MissionRateCalculator）。
        /// 純計算，不變更任何狀態。
        /// FSD §5.1 / §5.4 流程 A。
        /// </summary>
        public (float success, float death) CalculateRates(int instanceID, int missionID)
        {
            return _calculator.CalculateRates(instanceID, missionID);
        }

        /// <summary>
        /// 執行 10 步派遣序列。
        /// GDD §3.5；FSD §5.4 流程 B。
        /// </summary>
        /// <returns>成功派遣回 true；任一前置驗證失敗回 false。</returns>
        public bool Dispatch(int instanceID, int missionID, DispatchSource source)
        {
            // ── 前置驗證 ────────────────────────────────────────────────────────

            AdventurerInstance adv = AdventurerRoster.Instance != null
                ? AdventurerRoster.Instance.GetAdventurer(instanceID)
                : null;
            if (adv == null)
            {
                Debug.LogWarning($"[MissionDispatchService] Dispatch: instanceID={instanceID} 找不到冒險者，回 false。");
                return false;
            }

            long nowUTC = TimeSystem.Instance != null ? TimeSystem.Instance.NowUTC : 0L;
            if (nowUTC == 0L)
            {
                Debug.LogError("[MissionDispatchService] Dispatch: TimeSystem.Instance.NowUTC == 0，回 false。");
                return false;
            }

            MissionTemplate tmpl = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;
            if (tmpl == null)
            {
                Debug.LogError($"[MissionDispatchService] Dispatch: missionID={missionID} 找不到任務模板，回 false。");
                return false;
            }

            if (adv.status != AdventurerStatus.Idle)
            {
                Debug.LogWarning($"[MissionDispatchService] Dispatch: instanceID={instanceID} status={adv.status}（非 Idle），回 false。");
                return false;
            }

            if (GetActiveMissionCount() >= GetMaxConcurrentMissions())
            {
                Debug.LogWarning($"[MissionDispatchService] Dispatch: 進行中任務已達上限 {GetMaxConcurrentMissions()}，回 false。");
                return false;
            }

            // 同冒險者已有 ActiveMission（防禦）
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                if (_activeMissions[i].adventurerInstanceID == instanceID)
                {
                    Debug.LogError($"[MissionDispatchService] Dispatch: instanceID={instanceID} 已有 ActiveMission={_activeMissions[i].activeMissionID}，回 false。");
                    return false;
                }
            }

            // ── Step 1 / 2：取任務快照與基礎獎勵 ──────────────────────────────

            string difficulty = tmpl.difficulty;
            int baseReward = MissionDatabaseService.Instance.GetBaseReward(difficulty);

            // ── Step 3：計算成功率/死亡率 ──────────────────────────────────────

            (float finalSuccess, float finalDeath) = _calculator.CalculateRates(instanceID, missionID);

            // ── Step 4：計算時長與完成時間戳 ────────────────────────────────────
            // duration 單位為分鐘（Tech Debt TD-01；FSD §8.3）
            int durationMinutes = tmpl.typeID == _calculator.EscortTypeID
                ? MissionDatabaseService.Instance.GetEscortDuration(difficulty)
                : MissionDatabaseService.Instance.GetBaseDuration(difficulty);

            long dispatchTimestamp    = nowUTC;
            long completionTimestamp  = dispatchTimestamp + (long)durationMinutes * 60L;

            // ── Step 5：建立 ActiveMission，加入列表 ────────────────────────────

            int activeMissionID = _nextActiveMissionID++;
            ActiveMission mission = new ActiveMission
            {
                activeMissionID       = activeMissionID,
                missionID             = missionID,
                adventurerInstanceID  = instanceID,
                finalSuccessRate      = finalSuccess,
                finalDeathRate        = finalDeath,
                dispatchTimestamp     = dispatchTimestamp,
                completionTimestamp   = completionTimestamp,
            };
            _activeMissions.Add(mission);

            // ── Step 6：發布 OnCommissionAcceptedEvent（先於 Step 7 UpdateStatus）────

            EventBus.Publish(new OnCommissionAcceptedEvent(missionID, baseReward, source));

            // ── Step 7：更新冒險者狀態 ──────────────────────────────────────────

            AdventurerRoster.Instance.UpdateStatus(instanceID, AdventurerStatus.Dispatched, activeMissionID);

            // ── Step 8：發布 OnAdventurerDispatchedEvent ─────────────────────────

            EventBus.Publish(new OnAdventurerDispatchedEvent(instanceID, activeMissionID));

            // ── Step 9：通知 WorldDanger 計數 ────────────────────────────────────

            if (WorldDangerService.Instance != null)
            {
                WorldDangerService.Instance.OnMissionAccepted(difficulty);
            }

            // ── Step 10：從委託板移除 ───────────────────────────────────────────

            if (CommissionBoardService.Instance != null)
            {
                CommissionBoardService.Instance.RemoveMissionFromBoard(missionID);
            }
            else
            {
                Debug.LogWarning($"[MissionDispatchService] Dispatch: step 10 — CommissionBoardService.Instance 為 null，跳過 RemoveMissionFromBoard(missionID={missionID})。");
            }

            return true;
        }

        /// <summary>取得所有進行中任務（唯讀）。</summary>
        public IReadOnlyList<ActiveMission> GetActiveMissions()
        {
            return _activeMissions;
        }

        /// <summary>依 activeMissionID 查詢；找不到回 null。</summary>
        public ActiveMission GetActiveMission(int activeMissionID)
        {
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                if (_activeMissions[i].activeMissionID == activeMissionID)
                {
                    return _activeMissions[i];
                }
            }

            return null;
        }

        /// <summary>依 adventurerInstanceID 查詢；找不到回 null。</summary>
        public ActiveMission GetActiveMissionByAdventurer(int instanceID)
        {
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                if (_activeMissions[i].adventurerInstanceID == instanceID)
                {
                    return _activeMissions[i];
                }
            }

            return null;
        }

        /// <summary>進行中任務數量。</summary>
        public int GetActiveMissionCount()
        {
            return _activeMissions.Count;
        }

        /// <summary>
        /// FT-04 結算後呼叫：移除指定 ActiveMission。
        /// FSD §5.1。
        /// </summary>
        public void RemoveActiveMission(int activeMissionID)
        {
            for (int i = _activeMissions.Count - 1; i >= 0; i--)
            {
                if (_activeMissions[i].activeMissionID == activeMissionID)
                {
                    _activeMissions.RemoveAt(i);
                    return;
                }
            }

            Debug.LogWarning($"[MissionDispatchService] RemoveActiveMission: activeMissionID={activeMissionID} 不存在，無操作。");
        }

        // ── RuntimeInitializeOnLoadMethod ────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<SuccessRateRow>("SuccessRateTable");
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Subscribe<OnOfflineResolvedEvent>(HandleOfflineResolvedEvent);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Unsubscribe<OnOfflineResolvedEvent>(HandleOfflineResolvedEvent);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── Test Support ──────────────────────────────────────────────────────────

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

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        // ── 私有方法 ──────────────────────────────────────────────────────────────

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
                    UnityEngine.Object.DestroyImmediate(gameObject);
#endif
                }
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            _calculator = new MissionRateCalculator();
            _calculator.Initialize();
        }

        private void HandleSecondTick(OnSecondTickEvent evt)
        {
            TickCompletionCheck(evt.NowUTC);
        }

        private void HandleOfflineResolvedEvent(OnOfflineResolvedEvent evt)
        {
            HandleOfflineResolved(evt.OfflineSeconds);
        }

        /// <summary>
        /// 每 OnSecondTick 掃描到期 ActiveMission，發布 OnMissionCompletedEvent。
        /// 熱路徑：index loop 避免 alloc。
        /// FSD §5.4 流程 C。
        /// </summary>
        private void TickCompletionCheck(long nowUTC)
        {
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                ActiveMission m = _activeMissions[i];
                if (_publishedCompletionIDs.Contains(m.activeMissionID))
                {
                    continue;
                }

                if (nowUTC >= m.completionTimestamp)
                {
                    EventBus.Publish(new OnMissionCompletedEvent(m.activeMissionID));
                    _publishedCompletionIDs.Add(m.activeMissionID);
                }
            }
        }

        /// <summary>
        /// 訂閱 OnOfflineResolvedEvent：批次計算離線完成任務並發布 OnOfflineMissionsResolvedEvent。
        /// FSD §5.4 流程 D。
        /// 不直接發 OnMissionCompletedEvent，讓 TickCompletionCheck 下一秒處理。
        /// </summary>
        private void HandleOfflineResolved(long offlineSeconds)
        {
            long now = TimeSystem.Instance != null ? TimeSystem.Instance.NowUTC : 0L;

            // 收集離線期間完成但尚未發布的 ActiveMission ID
            List<int> completedIDs = new List<int>(_activeMissions.Count);
            for (int i = 0; i < _activeMissions.Count; i++)
            {
                ActiveMission m = _activeMissions[i];
                if (now >= m.completionTimestamp && !_publishedCompletionIDs.Contains(m.activeMissionID))
                {
                    completedIDs.Add(m.activeMissionID);
                }
            }

            EventBus.Publish(new OnOfflineMissionsResolvedEvent(
                offlineSeconds,
                completedIDs.Count,
                completedIDs.ToArray()));
        }

        /// <summary>
        /// 最大同時任務數：優先呼叫 BuildingService.GetMaxConcurrentMissions()，
        /// instance 不可用時退回 FALLBACK_MAX_CONCURRENT_MISSIONS。
        /// </summary>
        private static int GetMaxConcurrentMissions()
        {
            return BuildingService.Instance != null
                ? BuildingService.Instance.GetMaxConcurrentMissions()
                : FALLBACK_MAX_CONCURRENT_MISSIONS;
        }

        // ── 序列化 DTO ────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-02 ISaveable 共用 DTO（OwnerKey="ft02Dispatch"）：含 FT-02-A activeMissions + FT-02-B 兩池。
        /// FSD-A §5.3 ISaveable。
        /// </summary>
        [Serializable]
        private sealed class FT02SaveDTO
        {
            // FT-02-A 部分
            public List<ActiveMission> activeMissions;
            public int nextActiveMissionID;

            // FT-02-B 部分（從 CommissionBoardService 委派取得）
            public List<int> regularMissionPool;
            public List<int> staticMissionPool;
        }
    }
}
