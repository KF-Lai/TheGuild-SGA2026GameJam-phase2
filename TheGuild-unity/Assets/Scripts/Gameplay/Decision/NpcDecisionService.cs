using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Decision.Events;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Staff;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Decision
{
    [DefaultExecutionOrder(210)]
    public sealed class NpcDecisionService : MonoBehaviour, INpcDecisionService, ISaveable
    {
        private const string KeyDeathAversion = "DEATH_AVERSION";
        private const string KeyAcceptanceThreshold = "ACCEPTANCE_THRESHOLD";
        private const string KeyWillingnessJitter = "WILLINGNESS_JITTER";
        private const string KeyAutoPickupIdleMinutes = "AUTO_PICKUP_IDLE_MINUTES";
        private const string KeyAutoPickupIntervalMinutes = "AUTO_PICKUP_INTERVAL_MINUTES";

        private static readonly Dictionary<string, int> DifficultyIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = 0,
                ["E"] = 1,
                ["D"] = 2,
                ["C"] = 3,
                ["B"] = 4,
                ["A"] = 5,
                ["S"] = 6
            };

        private float _deathAversion;
        private float _acceptanceThreshold;
        private float _willingnessJitter;
        private float _autoPickupIdleSeconds;
        private float _autoPickupIntervalSeconds;

        private DataManager _dataManager;
        private TimeSystem _timeSystem;
        private AdventurerRoster _roster;
        private TraitService _traitService;
        private MissionDispatchService _dispatchService;
        private CommissionBoardService _commissionBoard;
        private MissionDatabaseService _missionDatabase;
        private GuildCoreService _guildCore;
        private StaffService _staffService;
        private System.Random _rng;

        public static NpcDecisionService Instance { get; private set; }
        public string OwnerKey => "ft03Decision";
        public bool IsCritical => false;

        public string Serialize()
        {
            return "{}";
        }

        public void RestoreFromSave(string ownerJson)
        {
        }

        public void InitializeAsNewGame()
        {
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterSystemConstantsTable("SystemConstants");
        }

        private readonly struct ScoreBreakdown
        {
            public ScoreBreakdown(float baseScore, float afterTraits, float afterStaff, float finalScore)
            {
                BaseScore = baseScore;
                AfterTraits = afterTraits;
                AfterStaff = afterStaff;
                FinalScore = finalScore;
            }

            public float BaseScore { get; }
            public float AfterTraits { get; }
            public float AfterStaff { get; }
            public float FinalScore { get; }
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnMinuteTickEvent>(HandleMinuteTick);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnMinuteTickEvent>(HandleMinuteTick);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public DecisionResult MakeDecision(int instanceID, int missionID)
        {
            AdventurerInstance adventurer = _roster != null ? _roster.GetAdventurer(instanceID) : null;
            if (adventurer == null)
            {
                return new DecisionResult(false, 0f, null);
            }

            if (adventurer.status != AdventurerStatus.Idle)
            {
                Debug.LogWarning($"[NpcDecisionService] MakeDecision: instanceID={instanceID} status={adventurer.status} is not Idle.");
                return new DecisionResult(false, 0f, null);
            }

            MissionTemplate mission = _missionDatabase != null ? _missionDatabase.GetTemplate(missionID) : null;
            if (mission == null)
            {
                return new DecisionResult(false, 0f, RejectionReason.TooRisky);
            }

            ScoreBreakdown breakdown = BuildScoreBreakdown(instanceID, adventurer, mission, includeJitter: true);
            bool accepted = breakdown.FinalScore >= _acceptanceThreshold;
            RejectionReason? reason = accepted ? null : ResolveRejectionReason(breakdown);
            return new DecisionResult(accepted, breakdown.FinalScore, reason);
        }

        public float PreviewEffectiveScore(int instanceID, int missionID)
        {
            AdventurerInstance adventurer = _roster != null ? _roster.GetAdventurer(instanceID) : null;
            MissionTemplate mission = _missionDatabase != null ? _missionDatabase.GetTemplate(missionID) : null;
            if (adventurer == null || mission == null)
            {
                return 0f;
            }

            ScoreBreakdown breakdown = BuildScoreBreakdown(instanceID, adventurer, mission, includeJitter: false);
            return breakdown.AfterStaff;
        }

        internal void InitializeForTests(int? seed = null, System.Random rng = null)
        {
            InitializeInstance();
            if (rng != null)
            {
                _rng = rng;
            }
            else if (seed.HasValue)
            {
                _rng = new System.Random(seed.Value);
            }
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

            CacheDependencies();
            LoadConstants();
            if (_rng == null)
            {
                _rng = new System.Random();
            }
        }

        private void CacheDependencies()
        {
            _dataManager = DataManager.Instance;
            _timeSystem = TimeSystem.Instance;
            _roster = AdventurerRoster.Instance;
            _traitService = TraitService.Instance;
            _dispatchService = MissionDispatchService.Instance;
            _commissionBoard = CommissionBoardService.Instance;
            _missionDatabase = MissionDatabaseService.Instance;
            _guildCore = GuildCoreService.Instance;
            _staffService = StaffService.Instance;
        }

        private void LoadConstants()
        {
            if (_dataManager == null)
            {
                Debug.LogError("[NpcDecisionService] DataManager.Instance is null, fallback constants applied.");
                _deathAversion = 1.5f;
                _acceptanceThreshold = 0f;
                _willingnessJitter = 0f;
                _autoPickupIdleSeconds = 600f;
                _autoPickupIntervalSeconds = 1800f;
                return;
            }

            _deathAversion = _dataManager.GetFloat(KeyDeathAversion);
            _acceptanceThreshold = _dataManager.GetFloat(KeyAcceptanceThreshold);
            _willingnessJitter = Mathf.Max(0f, _dataManager.GetFloat(KeyWillingnessJitter));
            _autoPickupIdleSeconds = Mathf.Max(0f, _dataManager.GetFloat(KeyAutoPickupIdleMinutes) * 60f);
            _autoPickupIntervalSeconds = Mathf.Max(0f, _dataManager.GetFloat(KeyAutoPickupIntervalMinutes) * 60f);
        }

        private ScoreBreakdown BuildScoreBreakdown(int instanceID, AdventurerInstance adventurer, MissionTemplate mission, bool includeJitter)
        {
            (float success, float death) rates = _dispatchService != null
                ? _dispatchService.CalculateRates(instanceID, mission.missionID)
                : (0f, 0f);

            float baseScore = rates.success - (rates.death * _deathAversion);
            float afterTraits = baseScore + GetBehaviorDelta(adventurer, mission);
            float afterStaff = afterTraits + (_staffService != null ? _staffService.GetStaffWillingnessBonus() : 0f);
            float finalScore = afterStaff + (includeJitter ? NextJitter() : 0f);
            return new ScoreBreakdown(baseScore, afterTraits, afterStaff, finalScore);
        }

        private float GetBehaviorDelta(AdventurerInstance adventurer, MissionTemplate mission)
        {
            if (_traitService == null || adventurer.traitIDs == null || adventurer.traitIDs.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < adventurer.traitIDs.Length; i++)
            {
                TraitData trait = _traitService.GetTrait(adventurer.traitIDs[i]);
                if (trait == null || !string.Equals(trait.effectType, "behavior", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (MatchesBehaviorTarget(trait.effectTarget, mission))
                {
                    sum += trait.effectValue;
                }
            }

            return sum;
        }

        private bool MatchesBehaviorTarget(string traitTarget, MissionTemplate mission)
        {
            string target = traitTarget == null ? string.Empty : traitTarget.Trim();
            if (target.Length == 0)
            {
                Debug.LogWarning("[NpcDecisionService] Unknown behavior effectTarget='', trait skipped.");
                return false;
            }

            if (string.Equals(target, "willingness_all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryParseIntSuffix(target, "willingness_type_", out int typeID))
            {
                return mission.typeID == typeID;
            }

            if (TryParseIntSuffix(target, "willingness_category_", out int categoryID))
            {
                return mission.categoryID == categoryID;
            }

            if (TryParseIntSuffix(target, "willingness_faction_", out int factionID))
            {
                return mission.factionID == factionID;
            }

            if (string.Equals(target, "willingness_diff_S", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetDifficultyIndex(mission.difficulty, out int difficulty) && difficulty >= DifficultyIndex["S"];
            }

            if (string.Equals(target, "willingness_diff_A", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetDifficultyIndex(mission.difficulty, out int difficulty) && difficulty >= DifficultyIndex["A"];
            }

            if (string.Equals(target, "willingness_diff_low", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetDifficultyIndex(mission.difficulty, out int difficulty) && difficulty <= DifficultyIndex["E"];
            }

            Debug.LogWarning($"[NpcDecisionService] Unknown behavior effectTarget='{target}', trait skipped.");
            return false;
        }

        private static bool TryParseIntSuffix(string raw, string prefix, out int value)
        {
            value = 0;
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(raw.Substring(prefix.Length), out value);
        }

        private bool TryGetDifficultyIndex(string difficulty, out int index)
        {
            return DifficultyIndex.TryGetValue((difficulty ?? string.Empty).Trim(), out index);
        }

        private float NextJitter()
        {
            if (_willingnessJitter <= 0f)
            {
                return 0f;
            }

            if (_rng == null)
            {
                _rng = new System.Random();
            }

            return ((float)_rng.NextDouble() * (2f * _willingnessJitter)) - _willingnessJitter;
        }

        private RejectionReason ResolveRejectionReason(ScoreBreakdown score)
        {
            if (score.BaseScore < _acceptanceThreshold)
            {
                return RejectionReason.TooRisky;
            }

            if (score.AfterTraits < _acceptanceThreshold)
            {
                return RejectionReason.NotWilling;
            }

            return RejectionReason.NotInterested;
        }

        private void HandleMinuteTick(OnMinuteTickEvent evt)
        {
            AutoPickupTick(evt.NowUTC);
        }

        private void AutoPickupTick(long nowUtc)
        {
            if (_roster == null || _commissionBoard == null || _missionDatabase == null || _guildCore == null || _dispatchService == null)
            {
                return;
            }

            IReadOnlyList<AdventurerInstance> idles = _roster.GetByStatus(AdventurerStatus.Idle);
            for (int i = 0; i < idles.Count; i++)
            {
                TryAutoPickup(idles[i], nowUtc);
            }
        }

        private void TryAutoPickup(AdventurerInstance adventurer, long nowUtc)
        {
            if (adventurer == null || nowUtc <= 0)
            {
                return;
            }

            if (adventurer.idleSinceTimestamp <= 0 || nowUtc - adventurer.idleSinceTimestamp < (long)_autoPickupIdleSeconds)
            {
                return;
            }

            if (adventurer.lastAutoPickupTimestamp > 0 &&
                nowUtc - adventurer.lastAutoPickupTimestamp < (long)_autoPickupIntervalSeconds)
            {
                return;
            }

            int guildMaxIndex;
            if (!TryGetDifficultyIndex(_guildCore.GetMaxMissionDifficulty(), out guildMaxIndex))
            {
                Debug.LogWarning("[NpcDecisionService] Guild max mission difficulty is invalid, skip auto pickup.");
                _roster.SetLastAutoPickupTimestamp(adventurer.instanceID, nowUtc);
                return;
            }

            IReadOnlyList<int> candidates = _commissionBoard.GetAvailableCommissions();
            int bestMissionID = -1;
            float bestScore = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                int missionID = candidates[i];
                MissionTemplate mission = _missionDatabase.GetTemplate(missionID);
                if (mission == null)
                {
                    continue;
                }

                if (!TryGetDifficultyIndex(mission.difficulty, out int missionDifficultyIndex))
                {
                    Debug.LogWarning($"[NpcDecisionService] Mission difficulty='{mission.difficulty}' is invalid, skipped.");
                    continue;
                }

                if (missionDifficultyIndex > guildMaxIndex)
                {
                    continue;
                }

                float score = BuildScoreBreakdown(adventurer.instanceID, adventurer, mission, includeJitter: false).AfterStaff;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMissionID = missionID;
                }
            }

            _roster.SetLastAutoPickupTimestamp(adventurer.instanceID, nowUtc);

            if (bestMissionID < 0 || bestScore < _acceptanceThreshold)
            {
                return;
            }

            bool dispatched = _dispatchService.Dispatch(adventurer.instanceID, bestMissionID, DispatchSource.NpcAutoPick);
            if (dispatched)
            {
                EventBus.Publish(new OnAutoPickupEvent(adventurer.instanceID, bestMissionID));
            }
        }
    }
}


