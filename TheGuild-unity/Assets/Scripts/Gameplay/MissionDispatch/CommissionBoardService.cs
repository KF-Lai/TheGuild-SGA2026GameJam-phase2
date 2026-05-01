using System.Collections.Generic;
using System.Linq;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.WorldDanger;
using TheGuild.Gameplay.WorldDanger.Events;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// FT-02-B 委託板池服務（concrete singleton MonoBehaviour）。
    /// 維護常規 / 靜態兩池、注入、查詢、移除、三軌補池生成。
    /// GDD §3.9；FSD §5.1 / §5.4 流程 A~E。
    /// </summary>
    [DefaultExecutionOrder(125)]   // 在 AdventurerRoster=110 之後、MissionDispatchService=130 之前
    public sealed class CommissionBoardService : MonoBehaviour
    {
        // BuildingService.Instance 不可用時（測試環境 / 啟動異常）的保底值。
        private const int FALLBACK_MISSION_SLOT_COUNT = 5;

        // v3.1 P3.1-007：SelectMissionFromPool 每 slot 最多重採次數。
        private const int MAX_FALLBACK_RETRY = 3;

        private readonly List<int> _regularMissionPool = new List<int>(8);
        private readonly List<int> _staticMissionPool = new List<int>(4);

        public static CommissionBoardService Instance { get; private set; }

        // ── 公開 API（FSD §5.1）────────────────────────────────────────────────────

        /// <summary>
        /// 注入常規委託至 _regularMissionPool（內部生成流程呼叫）。
        /// FSD §5.4 流程 B。
        /// </summary>
        public PostResult PostRegularMission(int missionID)
        {
            MissionTemplate tmpl = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;

            if (tmpl == null)
            {
                return PostResult.UNKNOWN_MISSION_ID;
            }

            if (tmpl.categoryID != CommissionCategory.RegularCategoryID)
            {
                return PostResult.WRONG_CATEGORY;
            }

            if (IsCommissionOnBoard(missionID))
            {
                return PostResult.ALREADY_ON_BOARD;
            }

            _regularMissionPool.Add(missionID);
            EventBus.Publish(new OnCommissionPostedEvent(missionID, CommissionSource.Regular));
            return PostResult.OK;
        }

        /// <summary>
        /// FT-09 注入劇情委託入口（categoryID 必須為 3）。
        /// FSD §5.4 流程 A。
        /// </summary>
        public InjectStaticMissionResult InjectStaticMission(int missionID)
        {
            MissionTemplate tmpl = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;

            if (tmpl == null)
            {
                return InjectStaticMissionResult.UNKNOWN_MISSION_ID;
            }

            if (tmpl.categoryID != CommissionCategory.StaticCategoryID)
            {
                return InjectStaticMissionResult.WRONG_CATEGORY;
            }

            if (IsCommissionOnBoard(missionID))
            {
                return InjectStaticMissionResult.ALREADY_ON_BOARD;
            }

            _staticMissionPool.Add(missionID);
            EventBus.Publish(new OnCommissionPostedEvent(missionID, CommissionSource.Static));
            return InjectStaticMissionResult.OK;
        }

        /// <summary>
        /// 派遣後從委託板移除（FSD §5.4 流程 D）。靜態池優先。
        /// </summary>
        public void RemoveMissionFromBoard(int missionID)
        {
            int staticIndex = _staticMissionPool.IndexOf(missionID);
            if (staticIndex >= 0)
            {
                _staticMissionPool.RemoveAt(staticIndex);
                if (_regularMissionPool.Contains(missionID))
                {
                    Debug.LogWarning($"[CommissionBoardService] RemoveMissionFromBoard: missionID={missionID} 同時存在兩池（異常）。");
                }
                return;
            }

            int regularIndex = _regularMissionPool.IndexOf(missionID);
            if (regularIndex >= 0)
            {
                _regularMissionPool.RemoveAt(regularIndex);
                return;
            }

            // 兩池均無：NPC 自主接單未透過委託板，靜默忽略。
        }

        /// <summary>
        /// 取得委託板可用任務（兩池合集，FSD §8.3 B-02 硬要求：distinct）。
        /// </summary>
        public IReadOnlyList<int> GetAvailableCommissions()
        {
            int rawCount = _regularMissionPool.Count + _staticMissionPool.Count;
            HashSet<int> seen = new HashSet<int>();
            List<int> result = new List<int>(rawCount);

            for (int i = 0; i < _regularMissionPool.Count; i++)
            {
                if (seen.Add(_regularMissionPool[i]))
                {
                    result.Add(_regularMissionPool[i]);
                }
            }

            for (int i = 0; i < _staticMissionPool.Count; i++)
            {
                if (seen.Add(_staticMissionPool[i]))
                {
                    result.Add(_staticMissionPool[i]);
                }
            }

            if (result.Count != rawCount)
            {
                Debug.LogWarning($"[CommissionBoardService] GetAvailableCommissions: Duplicate missionID detected after restore, repaired by distinct (raw={rawCount}, distinct={result.Count}).");
            }

            return result;
        }

        /// <summary>
        /// 依來源篩選（FSD §5.1）。
        /// </summary>
        public IReadOnlyList<int> GetCommissionsBySource(CommissionSource source)
        {
            return source == CommissionSource.Static
                ? new List<int>(_staticMissionPool)
                : new List<int>(_regularMissionPool);
        }

        /// <summary>
        /// 委託板上是否有此 missionID（含兩池）。
        /// </summary>
        public bool IsCommissionOnBoard(int missionID)
        {
            return _regularMissionPool.Contains(missionID) || _staticMissionPool.Contains(missionID);
        }

        // ── ISaveable 委派 API（供 FT-02-A MissionDispatchService 整合） ──────────

        /// <summary>
        /// 取得當前兩池快照（淺複製），供 FT-02-A 序列化用。
        /// FSD §5.3 ICommissionBoardService。
        /// </summary>
        internal (List<int> regular, List<int> staticPool) GetSerializableState()
        {
            return (new List<int>(_regularMissionPool), new List<int>(_staticMissionPool));
        }

        /// <summary>
        /// 還原兩池內容；對 missionID 逐筆驗證（GetTemplate != null + categoryID 一致），
        /// 失敗條目 LogWarning 並跳過；不發 OnCommissionPostedEvent（避免 restore 時誤發）。
        /// FSD §5.3 / §5.4 流程 E。
        /// </summary>
        internal void RestoreState(List<int> regular, List<int> staticPool)
        {
            _regularMissionPool.Clear();
            _staticMissionPool.Clear();

            if (regular != null)
            {
                for (int i = 0; i < regular.Count; i++)
                {
                    int missionID = regular[i];
                    if (!ValidateRestoreMission(missionID, CommissionCategory.RegularCategoryID, "regular"))
                    {
                        continue;
                    }
                    _regularMissionPool.Add(missionID);
                }
            }

            if (staticPool != null)
            {
                for (int i = 0; i < staticPool.Count; i++)
                {
                    int missionID = staticPool[i];
                    if (!ValidateRestoreMission(missionID, CommissionCategory.StaticCategoryID, "static"))
                    {
                        continue;
                    }
                    _staticMissionPool.Add(missionID);
                }
            }
        }

        private bool ValidateRestoreMission(int missionID, int expectedCategory, string poolLabel)
        {
            MissionTemplate tmpl = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;

            if (tmpl == null)
            {
                Debug.LogWarning($"[CommissionBoardService] RestoreState: {poolLabel} pool missionID={missionID} 找不到模板，跳過。");
                return false;
            }

            if (tmpl.categoryID != expectedCategory)
            {
                Debug.LogWarning($"[CommissionBoardService] RestoreState: {poolLabel} pool missionID={missionID} categoryID={tmpl.categoryID}（預期 {expectedCategory}），跳過。");
                return false;
            }

            return true;
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeInstance();
        }

        private void Start()
        {
            // 啟動填池：新遊戲首次進入主場景後補滿至 GetMissionSlotCount()。
            RefillPool();
        }

        private void OnEnable()
        {
            EventBus.Subscribe(EventNames.OnDailyReset, HandleDailyReset);
            EventBus.Subscribe<OnDangerLevelChangedEvent>(HandleDangerLevelChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe(EventNames.OnDailyReset, HandleDailyReset);
            EventBus.Unsubscribe<OnDangerLevelChangedEvent>(HandleDangerLevelChanged);
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
        }

        private void HandleDailyReset()
        {
            RefillPool();
        }

        private void HandleDangerLevelChanged(OnDangerLevelChangedEvent evt)
        {
            RefillPool();
        }

        /// <summary>
        /// 三軌補池共用邏輯：依當前危險度權重抽取常規任務填補至 slotCount。
        /// FSD §5.4 流程 C；GDD §3.9.2；v3.1 P3.1-007 加入 minDangerLevel 過濾與 per-slot 3 次 fallback。
        /// </summary>
        private void RefillPool()
        {
            int slotCount = GetMissionSlotCount();
            int currentCount = _regularMissionPool.Count + _staticMissionPool.Count;
            int deficit = slotCount - currentCount;

            if (deficit <= 0)
            {
                return;
            }

            if (WorldDangerService.Instance == null)
            {
                Debug.LogWarning("[CommissionBoardService] RefillPool: WorldDangerService.Instance 為 null，無法取池權重，跳過。");
                return;
            }

            MissionPoolWeights weights = WorldDangerService.Instance.GetPoolWeights();
            if (weights.IsAllZero)
            {
                Debug.LogWarning("[CommissionBoardService] RefillPool: 權重全為 0，無法抽取，跳過。");
                return;
            }

            if (MissionDatabaseService.Instance == null)
            {
                Debug.LogWarning("[CommissionBoardService] RefillPool: MissionDatabaseService.Instance 為 null，無法取模板，跳過。");
                return;
            }

            // v3.1 P3.1-007：每次 RefillPool 取一次 dangerIndex，同批補池期間不重複查詢。
            int currentDangerIndex = GetCurrentDangerIndex();

            int filled = 0;

            for (int slot = 0; slot < deficit; slot++)
            {
                int retryCount = 0;

                while (retryCount <= MAX_FALLBACK_RETRY)
                {
                    string difficulty = RollDifficulty(weights);
                    if (string.IsNullOrEmpty(difficulty))
                    {
                        retryCount++;
                        continue;
                    }

                    IReadOnlyList<MissionTemplate> templates = MissionDatabaseService.Instance.GetRegularTemplates(difficulty);

                    // v3.1 P3.1-007：過濾 minDangerLevel > currentDangerIndex 的任務。
                    List<MissionTemplate> candidates = templates != null
                        ? templates.Where(t => t.minDangerLevel <= currentDangerIndex).ToList()
                        : null;

                    if (candidates == null || candidates.Count == 0)
                    {
                        retryCount++;
                        if (retryCount > MAX_FALLBACK_RETRY)
                        {
                            Debug.LogWarning("[FT-05] SelectMissionFromPool: exhausted 3 retries, skip generation");
                        }
                        continue;
                    }

                    int pickIndex = Random.Range(0, candidates.Count);
                    int missionID = candidates[pickIndex].missionID;

                    PostResult result = PostRegularMission(missionID);
                    if (result == PostResult.OK)
                    {
                        filled++;
                        break;
                    }

                    // ALREADY_ON_BOARD / WRONG_CATEGORY / UNKNOWN_MISSION_ID：消耗一次 retry。
                    retryCount++;
                    if (retryCount > MAX_FALLBACK_RETRY)
                    {
                        Debug.LogWarning("[FT-05] SelectMissionFromPool: exhausted 3 retries, skip generation");
                    }
                }
                // retryCount > MAX_FALLBACK_RETRY 時此 slot 跳過，繼續下一個 slot。
            }

            if (filled < deficit)
            {
                Debug.LogWarning($"[CommissionBoardService] RefillPool: 收斂保護觸發，目標填入 {deficit} 筆，實際 {filled} 筆（模板池可能不足）。");
            }
        }

        /// <summary>
        /// 取得當前世界危險度索引（E=0, D=1, C=2, B=3, A=4）。
        /// v3.1 P3.1-007：WorldDangerService 為 null 或回傳未知值時降級為 0（E 期）。
        /// </summary>
        private static int GetCurrentDangerIndex()
        {
            if (WorldDangerService.Instance == null)
            {
                return 0;
            }

            string level = WorldDangerService.Instance.GetCurrentLevel();
            switch (level)
            {
                case "E": return 0;
                case "D": return 1;
                case "C": return 2;
                case "B": return 3;
                case "A": return 4;
                default:  return 0;
            }
        }

        /// <summary>
        /// 加權抽取 difficulty（GDD §3.9.2）。
        /// 規則：weightF_E 50% F / 50% E；weightS_SSS 50% S / 50% SS（SSS 剔除後份額平分）。
        /// </summary>
        private static string RollDifficulty(MissionPoolWeights weights)
        {
            int total = weights.weightF_E + weights.weightD + weights.weightC + weights.weightB + weights.weightA + weights.weightS_SSS;
            if (total <= 0)
            {
                return null;
            }

            int roll = Random.Range(0, total);
            int cursor = 0;

            cursor += weights.weightF_E;
            if (roll < cursor)
            {
                return Random.value < 0.5f ? "F" : "E";
            }

            cursor += weights.weightD;
            if (roll < cursor)
            {
                return "D";
            }

            cursor += weights.weightC;
            if (roll < cursor)
            {
                return "C";
            }

            cursor += weights.weightB;
            if (roll < cursor)
            {
                return "B";
            }

            cursor += weights.weightA;
            if (roll < cursor)
            {
                return "A";
            }

            // weightS_SSS bucket：SSS 剔除後份額平分回 S/SS。
            return Random.value < 0.5f ? "S" : "SS";
        }

        private static int GetMissionSlotCount()
        {
            return BuildingService.Instance != null
                ? BuildingService.Instance.GetMissionSlotCount()
                : FALLBACK_MISSION_SLOT_COUNT;
        }
    }
}
