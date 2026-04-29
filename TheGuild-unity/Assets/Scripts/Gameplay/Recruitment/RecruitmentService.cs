using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Recruitment.Events;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Staff;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Recruitment
{
    [DefaultExecutionOrder(200)]
    public sealed class RecruitmentService : MonoBehaviour, ISaveable
    {
        private const string OwnerKeyValue = "ft01Recruitment";
        private const int RookieCandidateStartId = 1;
        private const int VeteranCandidateStartId = 1001;

        private const string KeyRecruitPoolSize = "RECRUIT_POOL_SIZE";
        private const string KeyDailyFreeRefresh = "DAILY_FREE_REFRESH";
        private const string KeyRefreshCost = "REFRESH_COST";
        private const string KeyMinRefreshIntervalSec = "MIN_RECRUIT_REFRESH_INTERVAL_SEC";

        private int _recruitPoolSize;
        private int _dailyFreeRefresh;
        private int _refreshCost;
        private int _minRecruitRefreshIntervalSec;

        private long _lastRefreshTimestamp;
        private int _freeRefreshRemaining;
        private bool _hasState;

        private List<RecruitCandidate> _rookiePool = new List<RecruitCandidate>(4);
        private List<RecruitCandidate> _veteranPool = new List<RecruitCandidate>(4);

        private DataManager _dataManager;
        private TimeSystem _timeSystem;
        private ResourceManagement _resourceService;
        private AdventurerRoster _roster;
        private ProfessionService _professionService;
        private RaceService _raceService;
        private TraitService _traitService;
        private GuildCoreService _guildCoreService;
        private BuildingService _buildingService;
        private StaffService _staffService;
        private RecruitmentPoolGenerator _poolGenerator;

        public static RecruitmentService Instance { get; private set; }

        public string OwnerKey => OwnerKeyValue;
        public bool IsCritical => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<VeteranRankWeightEntry>("VeteranRankWeightTable");
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Subscribe(EventNames.OnDailyReset, HandleDailyReset);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Unsubscribe(EventNames.OnDailyReset, HandleDailyReset);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Unsubscribe(EventNames.OnDailyReset, HandleDailyReset);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (!_hasState)
            {
                InitializeAsNewGame();
            }
        }

        public IReadOnlyList<RecruitCandidate> GetRookiePool()
        {
            return _rookiePool;
        }

        public IReadOnlyList<RecruitCandidate> GetVeteranPool()
        {
            return _veteranPool;
        }

        public bool RecruitRookie(int candidateID)
        {
            if (IsRosterFull())
            {
                return false;
            }

            RecruitCandidate candidate = TryGetCandidate(_rookiePool, candidateID);
            if (candidate == null || candidate.AdventurerInstance == null)
            {
                return false;
            }

            if (!_roster.AddAdventurer(candidate.AdventurerInstance))
            {
                return false;
            }

            _rookiePool.Remove(candidate);
            EventBus.Publish(new OnRecruitSuccessEvent(candidate.AdventurerInstance.instanceID, RecruitSource.Rookie));
            return true;
        }

        public bool RecruitVeteran(int candidateID)
        {
            if (IsRosterFull())
            {
                return false;
            }

            RecruitCandidate candidate = TryGetCandidate(_veteranPool, candidateID);
            if (candidate == null || candidate.AdventurerInstance == null)
            {
                return false;
            }

            if (_resourceService.GetReputation() < candidate.ReputationReq)
            {
                return false;
            }

            if (!_resourceService.CanAfford(candidate.Cost))
            {
                return false;
            }

            if (!_resourceService.AddGold(-candidate.Cost))
            {
                return false;
            }

            if (!_roster.AddAdventurer(candidate.AdventurerInstance))
            {
                if (!_resourceService.AddGold(candidate.Cost))
                {
                    Debug.LogError("[RecruitmentService] RecruitVeteran rollback failed: AddGold(+cost) returned false.");
                }

                return false;
            }

            _veteranPool.Remove(candidate);
            EventBus.Publish(new OnRecruitSuccessEvent(candidate.AdventurerInstance.instanceID, RecruitSource.Veteran));
            return true;
        }

        public bool ManualRefresh()
        {
            if (_freeRefreshRemaining > 0)
            {
                _freeRefreshRemaining -= 1;
            }
            else
            {
                if (!_resourceService.CanAfford(_refreshCost))
                {
                    return false;
                }

                if (!_resourceService.AddGold(-_refreshCost))
                {
                    return false;
                }
            }

            ExecuteRefresh();
            _lastRefreshTimestamp = GetNowUtc();
            return true;
        }

        public int GetFreeRefreshRemaining()
        {
            return _freeRefreshRemaining;
        }

        public long GetNextAutoRefreshTimestamp()
        {
            return _lastRefreshTimestamp + GetRefreshIntervalSeconds();
        }

        public string Serialize()
        {
            RecruitmentSaveData dto = new RecruitmentSaveData
            {
                lastRefreshTimestamp = _lastRefreshTimestamp,
                freeRefreshRemaining = _freeRefreshRemaining,
                rookiePool = ToSaveList(_rookiePool),
                veteranPool = ToSaveList(_veteranPool)
            };

            return JsonUtility.ToJson(dto);
        }

        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            RecruitmentSaveData dto = JsonUtility.FromJson<RecruitmentSaveData>(ownerJson);
            if (dto == null)
            {
                InitializeAsNewGame();
                return;
            }

            _lastRefreshTimestamp = dto.lastRefreshTimestamp;
            _freeRefreshRemaining = Mathf.Clamp(dto.freeRefreshRemaining, 0, _dailyFreeRefresh);
            _rookiePool = FromSaveList(dto.rookiePool);
            _veteranPool = FromSaveList(dto.veteranPool);
            _hasState = true;
        }

        public void InitializeAsNewGame()
        {
            _freeRefreshRemaining = _dailyFreeRefresh;
            _lastRefreshTimestamp = GetNowUtc();
            ExecuteRefresh();
            _hasState = true;
        }

        internal bool CheckAutoRefresh()
        {
            long now = GetNowUtc();
            long next = _lastRefreshTimestamp + GetRefreshIntervalSeconds();
            if (now < next)
            {
                return false;
            }

            ExecuteRefresh();
            _lastRefreshTimestamp = now;
            return true;
        }

        internal int GetCurrentRefreshIntervalSecondsForTests()
        {
            return GetRefreshIntervalSeconds();
        }

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

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

        private void InitializeInstance()
        {
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

            _dataManager = DataManager.Instance;
            _timeSystem = TimeSystem.Instance;
            _resourceService = ResourceManagement.Instance;
            _roster = AdventurerRoster.Instance;
            _professionService = ProfessionService.Instance;
            _raceService = RaceService.Instance;
            _traitService = TraitService.Instance;
            _guildCoreService = GuildCoreService.Instance;
            _buildingService = BuildingService.Instance;
            _staffService = StaffService.Instance;

            CacheConstants();

            if (_roster != null)
            {
                _poolGenerator = new RecruitmentPoolGenerator(
                    _roster.GetFactory(),
                    _roster,
                    _professionService,
                    _raceService,
                    _traitService,
                    _guildCoreService,
                    _dataManager);
            }
        }

        private void CacheConstants()
        {
            if (_dataManager == null)
            {
                Debug.LogError("[RecruitmentService] DataManager.Instance is null; constants fallback to safe minimum.");
                _recruitPoolSize = 1;
                _dailyFreeRefresh = 0;
                _refreshCost = 0;
                _minRecruitRefreshIntervalSec = 1;
                return;
            }

            _recruitPoolSize = Mathf.Max(1, _dataManager.GetInt(KeyRecruitPoolSize));
            _dailyFreeRefresh = Mathf.Max(0, _dataManager.GetInt(KeyDailyFreeRefresh));
            _refreshCost = Mathf.Max(0, _dataManager.GetInt(KeyRefreshCost));
            _minRecruitRefreshIntervalSec = Mathf.Max(1, _dataManager.GetInt(KeyMinRefreshIntervalSec));
        }

        private void HandleSecondTick(OnSecondTickEvent _)
        {
            CheckAutoRefresh();
        }

        private void HandleDailyReset()
        {
            _freeRefreshRemaining = _dailyFreeRefresh;
        }

        private void ExecuteRefresh()
        {
            if (_poolGenerator == null)
            {
                _rookiePool = new List<RecruitCandidate>(0);
                _veteranPool = new List<RecruitCandidate>(0);
                return;
            }

            HashSet<int> usedUniqueTemplateIds = CollectRosterUniqueTemplateIds();
            _rookiePool = _poolGenerator.GenerateRookiePool(_recruitPoolSize, usedUniqueTemplateIds, RookieCandidateStartId);
            _veteranPool = _poolGenerator.GenerateVeteranPool(
                _recruitPoolSize,
                usedUniqueTemplateIds,
                GetMaxRecruitableRankSafe(),
                VeteranCandidateStartId);

            EventBus.Publish(new OnPoolRefreshedEvent());
        }

        private HashSet<int> CollectRosterUniqueTemplateIds()
        {
            HashSet<int> used = new HashSet<int>();
            if (_roster == null || _dataManager == null)
            {
                return used;
            }

            IReadOnlyList<AdventurerInstance> roster = _roster.GetRoster();
            for (int i = 0; i < roster.Count; i++)
            {
                AdventurerInstance inst = roster[i];
                if (inst == null || inst.templateID <= 0)
                {
                    continue;
                }

                AdventurerTemplate template = _dataManager.Get<AdventurerTemplate>(inst.templateID);
                if (template != null && template.isUnique == 1)
                {
                    used.Add(inst.templateID);
                }
            }

            return used;
        }

        private int GetRefreshIntervalSeconds()
        {
            int baseSec = _buildingService != null
                ? Mathf.Max(1, (int)_buildingService.GetRecruitRefreshInterval().TotalSeconds)
                : _minRecruitRefreshIntervalSec;

            int reductionSec = 0;
            if (_buildingService != null && _buildingService.IsStaffSystemUnlocked() && _staffService != null)
            {
                reductionSec = Mathf.Max(0, _staffService.GetRecruitRefreshReductionSec());
            }

            int intervalSec = baseSec - reductionSec;
            return Mathf.Max(intervalSec, _minRecruitRefreshIntervalSec);
        }

        private string GetMaxRecruitableRankSafe()
        {
            if (_guildCoreService == null)
            {
                return "D";
            }

            string rank = _guildCoreService.GetMaxRecruitableRank();
            return string.IsNullOrEmpty(rank) ? "D" : rank;
        }

        private bool IsRosterFull()
        {
            if (_roster == null || _buildingService == null)
            {
                return true;
            }

            return _roster.IsRosterFull(_buildingService.GetRosterCap());
        }

        private long GetNowUtc()
        {
            return _timeSystem != null
                ? _timeSystem.NowUTC
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static RecruitCandidate TryGetCandidate(List<RecruitCandidate> pool, int candidateID)
        {
            if (pool == null || candidateID <= 0)
            {
                return null;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                RecruitCandidate candidate = pool[i];
                if (candidate != null && candidate.CandidateID == candidateID)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static List<RecruitCandidateSaveData> ToSaveList(List<RecruitCandidate> source)
        {
            List<RecruitCandidateSaveData> result = new List<RecruitCandidateSaveData>(source == null ? 0 : source.Count);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                RecruitCandidate c = source[i];
                if (c == null || c.AdventurerInstance == null)
                {
                    continue;
                }

                result.Add(new RecruitCandidateSaveData
                {
                    candidateID = c.CandidateID,
                    cost = c.Cost,
                    reputationReq = c.ReputationReq,
                    adventurer = ToAdventurerSave(c.AdventurerInstance)
                });
            }

            return result;
        }

        private static List<RecruitCandidate> FromSaveList(List<RecruitCandidateSaveData> source)
        {
            List<RecruitCandidate> result = new List<RecruitCandidate>(source == null ? 0 : source.Count);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                RecruitCandidateSaveData c = source[i];
                AdventurerInstance adventurer = FromAdventurerSave(c.adventurer);
                if (adventurer == null)
                {
                    continue;
                }

                result.Add(new RecruitCandidate
                {
                    CandidateID = c.candidateID,
                    Cost = c.cost,
                    ReputationReq = c.reputationReq,
                    AdventurerInstance = adventurer
                });
            }

            return result;
        }

        private static AdventurerInstanceSaveData ToAdventurerSave(AdventurerInstance source)
        {
            return new AdventurerInstanceSaveData
            {
                instanceID = source.instanceID,
                templateID = source.templateID,
                name = source.name,
                rank = source.rank,
                professionID = source.professionID,
                raceID = source.raceID,
                traitIDs = source.traitIDs == null ? Array.Empty<int>() : (int[])source.traitIDs.Clone(),
                factionID = source.factionID,
                status = (int)source.status,
                currentMissionID = source.currentMissionID,
                woundedUntilTimestamp = source.woundedUntilTimestamp,
                idleSinceTimestamp = source.idleSinceTimestamp,
                lastAutoPickupTimestamp = source.lastAutoPickupTimestamp
            };
        }

        private static AdventurerInstance FromAdventurerSave(AdventurerInstanceSaveData source)
        {
            if (source == null)
            {
                return null;
            }

            AdventurerStatus status = Enum.IsDefined(typeof(AdventurerStatus), source.status)
                ? (AdventurerStatus)source.status
                : AdventurerStatus.Idle;

            return new AdventurerInstance
            {
                instanceID = source.instanceID,
                templateID = source.templateID,
                name = source.name,
                rank = source.rank,
                professionID = source.professionID,
                raceID = source.raceID,
                traitIDs = source.traitIDs == null ? Array.Empty<int>() : (int[])source.traitIDs.Clone(),
                factionID = source.factionID,
                status = status,
                currentMissionID = source.currentMissionID,
                woundedUntilTimestamp = source.woundedUntilTimestamp,
                idleSinceTimestamp = source.idleSinceTimestamp,
                lastAutoPickupTimestamp = source.lastAutoPickupTimestamp
            };
        }

        [Serializable]
        private sealed class RecruitmentSaveData
        {
            public long lastRefreshTimestamp;
            public int freeRefreshRemaining;
            public List<RecruitCandidateSaveData> rookiePool;
            public List<RecruitCandidateSaveData> veteranPool;
        }

        [Serializable]
        private sealed class RecruitCandidateSaveData
        {
            public int candidateID;
            public int cost;
            public int reputationReq;
            public AdventurerInstanceSaveData adventurer;
        }

        [Serializable]
        private sealed class AdventurerInstanceSaveData
        {
            public int instanceID;
            public int templateID;
            public string name;
            public string rank;
            public int professionID;
            public int raceID;
            public int[] traitIDs;
            public int factionID;
            public int status;
            public int currentMissionID;
            public long woundedUntilTimestamp;
            public long idleSinceTimestamp;
            public long lastAutoPickupTimestamp;
        }
    }
}


