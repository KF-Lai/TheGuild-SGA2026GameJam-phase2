using System;
using System.Collections.Generic;
using System.Globalization;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Staff;
using UnityEngine;

// 實作依據：【FT-08-FSD】gacha-system.md §3.2 / §3.3.6 / §3.5.2 / §4.1.5 / §4.1.6 / §6.4

namespace TheGuild.Gameplay.Gacha
{
    public sealed class GachaTableLoader
    {
        private const string POOL_TABLE_NAME = "StaffGachaPoolTable";
        private const string REFRESH_COST_TABLE_NAME = "StaffRefreshCostTable";
        private const string RARITY_PROB_TABLE_NAME = "StaffRarityProbTable";
        private const string TRASH_TABLE_NAME = "TrashItemTable";
        private const string STAFF_TUNING_TABLE_NAME = "StaffTuning";

        private const int MIN_GUILD_LEVEL = 1;
        private const int MAX_GUILD_LEVEL = 5;
        private const int MIN_RARITY = 1;
        private const int MAX_RARITY = 5;
        private const float PROB_SUM_EPSILON = 0.0001f;

        private const string KEY_PITY_THRESHOLD = "PITY_THRESHOLD";
        private const string KEY_TRASH_ROLL_RATE_AT_RARITY_1 = "TRASH_ROLL_RATE_AT_RARITY_1";
        private const string KEY_MIN_AUTO_REFRESH_INTERVAL_SEC = "MIN_AUTO_REFRESH_INTERVAL_SEC";
        private const string KEY_MAX_RESERVE_FALLBACK = "MAX_RESERVE_FALLBACK";
        private const string KEY_INTERVIEW_AUTO_REFRESH_PREFIX = "INTERVIEW_AUTO_REFRESH_INTERVAL_L";
        private const string KEY_INTERVIEW_SLOT_COUNT_PREFIX = "INTERVIEW_SLOT_COUNT_L";

        private static readonly IReadOnlyList<int> s_emptyIntList = new List<int>(0);
        private static readonly IReadOnlyDictionary<int, int> s_emptyWeightMap = new Dictionary<int, int>(0);
        private static readonly IReadOnlyList<TrashItemData> s_emptyTrashItems = new List<TrashItemData>(0);

        private readonly Dictionary<int, StaffGachaPoolData> _poolByID = new Dictionary<int, StaffGachaPoolData>(8);
        private readonly List<StaffGachaPoolData> _allPools = new List<StaffGachaPoolData>(8);
        private readonly Dictionary<int, StaffRefreshCostData> _refreshCostByGuildLevel = new Dictionary<int, StaffRefreshCostData>(MAX_GUILD_LEVEL);
        private readonly Dictionary<int, float> _baseProbByRarity = new Dictionary<int, float>(MAX_RARITY);
        private readonly Dictionary<int, Dictionary<int, List<int>>> _eligibleByPoolAndRarity =
            new Dictionary<int, Dictionary<int, List<int>>>(8);
        private readonly Dictionary<int, Dictionary<int, int>> _staffWeightsByPool =
            new Dictionary<int, Dictionary<int, int>>(8);
        private readonly List<TrashItemData> _trashItems = new List<TrashItemData>(16);
        private readonly int[] _autoRefreshIntervalByLevel = new int[MAX_GUILD_LEVEL + 1];

        public bool IsReady { get; private set; }

        public int PityThreshold { get; private set; }
        public float TrashRollRateAtRarity1 { get; private set; }
        public int MinAutoRefreshIntervalSec { get; private set; }
        public int MaxReserveFallback { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<StaffGachaPoolData>(POOL_TABLE_NAME);
            DataManager.RegisterTable<StaffRefreshCostData>(REFRESH_COST_TABLE_NAME);
            DataManager.RegisterTable<StaffRarityProbData>(RARITY_PROB_TABLE_NAME);
            DataManager.RegisterTable<TrashItemData>(TRASH_TABLE_NAME);

            // Pre-flight grep shows FT-12 does not currently register StaffTuning.
            DataManager.RegisterTable<StaffTuningEntry>(STAFF_TUNING_TABLE_NAME);
        }

        public void Initialize()
        {
            ResetState();

            if (DataManager.Instance == null)
            {
                throw new StaffGachaPoolTableValidationException("DataManager.Instance is null.");
            }

            try
            {
                Dictionary<int, StaffData> staffByID = LoadStaffLookupByID();
                LoadPoolTable(staffByID);
                LoadRefreshCostTable();
                LoadRarityProbTable();
                LoadTrashItemTable(staffByID);
                LoadTuningTable();
                IsReady = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GachaTableLoader] Initialize failed: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }

        public StaffGachaPoolData GetPool(int poolID)
        {
            EnsureReady();
            if (!_poolByID.TryGetValue(poolID, out StaffGachaPoolData pool))
            {
                throw new KeyNotFoundException($"Pool not found: poolID={poolID}");
            }

            return pool;
        }

        public IReadOnlyList<StaffGachaPoolData> GetAllPools()
        {
            EnsureReady();
            return _allPools;
        }

        public int GetSlotCount(int guildLevel)
        {
            EnsureReady();
            if (!_refreshCostByGuildLevel.TryGetValue(guildLevel, out StaffRefreshCostData row))
            {
                throw new KeyNotFoundException($"RefreshCost row missing: guildLevel={guildLevel}");
            }

            return row.interviewSlotCount;
        }

        public int GetRefreshCost(int guildLevel)
        {
            EnsureReady();
            if (!_refreshCostByGuildLevel.TryGetValue(guildLevel, out StaffRefreshCostData row))
            {
                throw new KeyNotFoundException($"RefreshCost row missing: guildLevel={guildLevel}");
            }

            return row.cost;
        }

        public float GetBaseProb(int rarity)
        {
            EnsureReady();
            if (!_baseProbByRarity.TryGetValue(rarity, out float prob))
            {
                throw new KeyNotFoundException($"Rarity probability missing: rarity={rarity}");
            }

            return prob;
        }

        public IReadOnlyList<int> GetEligibleStaffByRarity(int poolID, int rarity)
        {
            EnsureReady();
            if (rarity < MIN_RARITY || rarity > MAX_RARITY)
            {
                throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "rarity must be in [1, 5].");
            }

            if (!_eligibleByPoolAndRarity.TryGetValue(poolID, out Dictionary<int, List<int>> byRarity))
            {
                throw new KeyNotFoundException($"Pool not found: poolID={poolID}");
            }

            return byRarity.TryGetValue(rarity, out List<int> eligible) ? eligible : s_emptyIntList;
        }

        public IReadOnlyDictionary<int, int> GetStaffWeights(int poolID)
        {
            EnsureReady();
            if (!_staffWeightsByPool.TryGetValue(poolID, out Dictionary<int, int> weights))
            {
                throw new KeyNotFoundException($"Pool not found: poolID={poolID}");
            }

            return weights ?? s_emptyWeightMap;
        }

        public IReadOnlyList<TrashItemData> GetTrashItems()
        {
            EnsureReady();
            return _trashItems.Count == 0 ? s_emptyTrashItems : _trashItems;
        }

        public int GetAutoRefreshIntervalSec(int buildingLevel)
        {
            EnsureReady();
            if (buildingLevel < MIN_GUILD_LEVEL || buildingLevel > MAX_GUILD_LEVEL)
            {
                throw new ArgumentOutOfRangeException(nameof(buildingLevel), buildingLevel, "buildingLevel must be in [1, 5].");
            }

            return _autoRefreshIntervalByLevel[buildingLevel];
        }

        private void ResetState()
        {
            IsReady = false;
            PityThreshold = 0;
            TrashRollRateAtRarity1 = 0f;
            MinAutoRefreshIntervalSec = 0;
            MaxReserveFallback = 0;

            _poolByID.Clear();
            _allPools.Clear();
            _refreshCostByGuildLevel.Clear();
            _baseProbByRarity.Clear();
            _eligibleByPoolAndRarity.Clear();
            _staffWeightsByPool.Clear();
            _trashItems.Clear();

            for (int i = 0; i < _autoRefreshIntervalByLevel.Length; i++)
            {
                _autoRefreshIntervalByLevel[i] = 0;
            }
        }

        private static Dictionary<int, StaffData> LoadStaffLookupByID()
        {
            IReadOnlyList<StaffData> rows = DataManager.Instance.GetAll<StaffData>();
            if (rows == null || rows.Count == 0)
            {
                throw new StaffGachaPoolTableValidationException("StaffTable is empty; cannot validate gacha pools.");
            }

            Dictionary<int, StaffData> byID = new Dictionary<int, StaffData>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                StaffData row = rows[i];
                if (row == null || row.staffID <= 0)
                {
                    continue;
                }

                if (row.rarity < MIN_RARITY || row.rarity > MAX_RARITY)
                {
                    throw new StaffGachaPoolTableValidationException($"StaffTable rarity out of range: staffID={row.staffID}, rarity={row.rarity}");
                }

                if (byID.ContainsKey(row.staffID))
                {
                    throw new StaffGachaPoolTableValidationException($"StaffTable duplicate staffID={row.staffID}");
                }

                byID[row.staffID] = row;
            }

            return byID;
        }

        private void LoadPoolTable(IReadOnlyDictionary<int, StaffData> staffByID)
        {
            IReadOnlyList<StaffGachaPoolData> rows = DataManager.Instance.GetAll<StaffGachaPoolData>();
            if (rows == null || rows.Count == 0)
            {
                throw new StaffGachaPoolTableValidationException($"{POOL_TABLE_NAME} is empty.");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                StaffGachaPoolData row = rows[i] ?? throw new StaffGachaPoolTableValidationException($"{POOL_TABLE_NAME}[{i}] is null.");
                if (_poolByID.ContainsKey(row.poolID))
                {
                    throw new StaffGachaPoolTableValidationException($"Duplicate poolID={row.poolID}");
                }

                if (row.minGuildLevel < MIN_GUILD_LEVEL || row.minGuildLevel > MAX_GUILD_LEVEL ||
                    row.maxGuildLevel < MIN_GUILD_LEVEL || row.maxGuildLevel > MAX_GUILD_LEVEL ||
                    row.minGuildLevel > row.maxGuildLevel)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: invalid guild level range [{row.minGuildLevel}, {row.maxGuildLevel}]");
                }

                string storyFlag = row.storyFlagRequired == null ? string.Empty : row.storyFlagRequired.Trim();
                if (storyFlag.Length != 0 || row.factionIDRequired != 0 || row.minReputation != 0 ||
                    row.eventStartTimestamp != 0L || row.eventEndTimestamp != 0L)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: Jam reserved gates must be defaults.");
                }

                if (row.reserveTimeLimitSec <= 0)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: reserveTimeLimitSec must be > 0.");
                }

                if (row.eligibleStaffIDs == null || row.staffWeights == null ||
                    row.eligibleStaffIDs.Length == 0 || row.eligibleStaffIDs.Length != row.staffWeights.Length)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: eligibleStaffIDs/staffWeights must be non-empty and same length.");
                }

                int totalWeight = 0;
                bool hasNonEmptyTier = false;
                Dictionary<int, int> weightByStaff = new Dictionary<int, int>(row.eligibleStaffIDs.Length);
                Dictionary<int, List<int>> byRarity = new Dictionary<int, List<int>>(MAX_RARITY);
                for (int rarity = MIN_RARITY; rarity <= MAX_RARITY; rarity++)
                {
                    byRarity[rarity] = new List<int>(8);
                }

                for (int j = 0; j < row.eligibleStaffIDs.Length; j++)
                {
                    int staffID = row.eligibleStaffIDs[j];
                    int weight = row.staffWeights[j];
                    if (staffID <= 0 || weight < 0)
                    {
                        throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: invalid staffID/weight at index={j}.");
                    }

                    if (weightByStaff.ContainsKey(staffID))
                    {
                        throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: duplicate staffID={staffID} in eligibleStaffIDs.");
                    }

                    if (!staffByID.TryGetValue(staffID, out StaffData staff))
                    {
                        throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: staffID={staffID} not found in StaffTable.");
                    }

                    weightByStaff[staffID] = weight;
                    totalWeight += weight;
                    if (weight > 0)
                    {
                        byRarity[staff.rarity].Add(staffID);
                        hasNonEmptyTier = true;
                    }
                }

                if (totalWeight <= 0)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: staffWeights sum must be > 0.");
                }

                if (!hasNonEmptyTier)
                {
                    throw new StaffGachaPoolTableValidationException($"poolID={row.poolID}: pool must have at least one non-empty rarity tier.");
                }

                _poolByID[row.poolID] = row;
                _allPools.Add(row);
                _staffWeightsByPool[row.poolID] = weightByStaff;
                _eligibleByPoolAndRarity[row.poolID] = byRarity;
            }
        }

        private void LoadRefreshCostTable()
        {
            IReadOnlyList<StaffRefreshCostData> rows = DataManager.Instance.GetAll<StaffRefreshCostData>();
            if (rows == null || rows.Count == 0)
            {
                throw new StaffRefreshCostTableValidationException($"{REFRESH_COST_TABLE_NAME} is empty.");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                StaffRefreshCostData row = rows[i] ?? throw new StaffRefreshCostTableValidationException($"{REFRESH_COST_TABLE_NAME}[{i}] is null.");
                if (row.guildLevel < MIN_GUILD_LEVEL || row.guildLevel > MAX_GUILD_LEVEL)
                {
                    throw new StaffRefreshCostTableValidationException($"guildLevel must be in [1, 5], got {row.guildLevel}.");
                }

                if (_refreshCostByGuildLevel.ContainsKey(row.guildLevel))
                {
                    throw new StaffRefreshCostTableValidationException($"duplicate guildLevel={row.guildLevel}");
                }

                if (row.cost < 0)
                {
                    throw new StaffRefreshCostTableValidationException($"guildLevel={row.guildLevel}: cost must be >= 0.");
                }

                if (row.interviewSlotCount < 1 || row.interviewSlotCount > 5)
                {
                    throw new StaffRefreshCostTableValidationException($"guildLevel={row.guildLevel}: interviewSlotCount must be in [1, 5].");
                }

                _refreshCostByGuildLevel[row.guildLevel] = row;
            }

            for (int level = MIN_GUILD_LEVEL; level <= MAX_GUILD_LEVEL; level++)
            {
                if (!_refreshCostByGuildLevel.ContainsKey(level))
                {
                    throw new StaffRefreshCostTableValidationException($"missing guildLevel={level}");
                }
            }
        }

        private void LoadRarityProbTable()
        {
            IReadOnlyList<StaffRarityProbData> rows = DataManager.Instance.GetAll<StaffRarityProbData>();
            if (rows == null || rows.Count == 0)
            {
                throw new StaffGachaPoolTableValidationException($"{RARITY_PROB_TABLE_NAME} is empty.");
            }

            float sum = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                StaffRarityProbData row = rows[i] ?? throw new StaffGachaPoolTableValidationException($"{RARITY_PROB_TABLE_NAME}[{i}] is null.");
                if (row.rarity < MIN_RARITY || row.rarity > MAX_RARITY)
                {
                    throw new StaffGachaPoolTableValidationException($"rarity must be in [1, 5], got {row.rarity}.");
                }

                if (_baseProbByRarity.ContainsKey(row.rarity))
                {
                    throw new StaffGachaPoolTableValidationException($"duplicate rarity={row.rarity}");
                }

                if (float.IsNaN(row.prob) || float.IsInfinity(row.prob) || row.prob < 0f || row.prob > 1f)
                {
                    throw new StaffGachaPoolTableValidationException($"rarity={row.rarity}: prob must be in [0, 1].");
                }

                _baseProbByRarity[row.rarity] = row.prob;
                sum += row.prob;
            }

            for (int rarity = MIN_RARITY; rarity <= MAX_RARITY; rarity++)
            {
                if (!_baseProbByRarity.ContainsKey(rarity))
                {
                    throw new StaffGachaPoolTableValidationException($"missing rarity={rarity} in {RARITY_PROB_TABLE_NAME}");
                }
            }

            if (Mathf.Abs(sum - 1f) > PROB_SUM_EPSILON)
            {
                Debug.LogWarning($"[GachaTableLoader] {RARITY_PROB_TABLE_NAME} sum={sum} (expected 1.0).");
            }
        }

        private void LoadTrashItemTable(IReadOnlyDictionary<int, StaffData> staffByID)
        {
            IReadOnlyList<TrashItemData> rows = DataManager.Instance.GetAll<TrashItemData>();
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                TrashItemData row = rows[i] ?? throw new StaffGachaPoolTableValidationException($"{TRASH_TABLE_NAME}[{i}] is null.");
                if (row.trashItemID <= 0)
                {
                    throw new StaffGachaPoolTableValidationException($"{TRASH_TABLE_NAME}: trashItemID must be > 0.");
                }

                if (!seen.Add(row.trashItemID))
                {
                    throw new StaffGachaPoolTableValidationException($"{TRASH_TABLE_NAME}: duplicate trashItemID={row.trashItemID}.");
                }

                if (staffByID.ContainsKey(row.trashItemID))
                {
                    throw new StaffGachaPoolTableValidationException($"{TRASH_TABLE_NAME}: trashItemID={row.trashItemID} collides with StaffTable.staffID.");
                }

                _trashItems.Add(row);
            }
        }

        private void LoadTuningTable()
        {
            IReadOnlyList<StaffTuningEntry> rows = DataManager.Instance.GetAll<StaffTuningEntry>();
            if (rows == null || rows.Count == 0)
            {
                throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME} is empty.");
            }

            Dictionary<string, string> tuning = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                StaffTuningEntry row = rows[i] ?? throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME}[{i}] is null.");
                if (string.IsNullOrWhiteSpace(row.key))
                {
                    throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME}[{i}] has empty key.");
                }

                string key = row.key.Trim();
                if (tuning.ContainsKey(key))
                {
                    throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME}: duplicate key={key}.");
                }

                tuning[key] = row.value == null ? string.Empty : row.value.Trim();
            }

            PityThreshold = ParseRequiredInt(tuning, KEY_PITY_THRESHOLD);
            TrashRollRateAtRarity1 = ParseRequiredFloat(tuning, KEY_TRASH_ROLL_RATE_AT_RARITY_1);
            MinAutoRefreshIntervalSec = ParseRequiredInt(tuning, KEY_MIN_AUTO_REFRESH_INTERVAL_SEC);
            MaxReserveFallback = ParseRequiredInt(tuning, KEY_MAX_RESERVE_FALLBACK);

            if (PityThreshold <= 0)
            {
                throw new StaffGachaPoolTableValidationException($"{KEY_PITY_THRESHOLD} must be > 0.");
            }

            if (TrashRollRateAtRarity1 < 0f || TrashRollRateAtRarity1 > 1f)
            {
                throw new StaffGachaPoolTableValidationException($"{KEY_TRASH_ROLL_RATE_AT_RARITY_1} must be in [0, 1].");
            }

            if (MinAutoRefreshIntervalSec <= 0)
            {
                throw new StaffGachaPoolTableValidationException($"{KEY_MIN_AUTO_REFRESH_INTERVAL_SEC} must be > 0.");
            }

            if (MaxReserveFallback < 1)
            {
                throw new StaffGachaPoolTableValidationException($"{KEY_MAX_RESERVE_FALLBACK} must be >= 1.");
            }

            for (int level = MIN_GUILD_LEVEL; level <= MAX_GUILD_LEVEL; level++)
            {
                int interval = ParseRequiredInt(tuning, KEY_INTERVIEW_AUTO_REFRESH_PREFIX + level.ToString(CultureInfo.InvariantCulture));
                int tunedSlotCount = ParseRequiredInt(tuning, KEY_INTERVIEW_SLOT_COUNT_PREFIX + level.ToString(CultureInfo.InvariantCulture));
                if (interval <= 0)
                {
                    throw new StaffGachaPoolTableValidationException($"INTERVIEW_AUTO_REFRESH_INTERVAL_L{level} must be > 0.");
                }

                if (tunedSlotCount < 1 || tunedSlotCount > 5)
                {
                    throw new StaffGachaPoolTableValidationException($"INTERVIEW_SLOT_COUNT_L{level} must be in [1, 5].");
                }

                _autoRefreshIntervalByLevel[level] = interval;

                if (_refreshCostByGuildLevel.TryGetValue(level, out StaffRefreshCostData refreshRow) &&
                    refreshRow.interviewSlotCount != tunedSlotCount)
                {
                    throw new StaffRefreshCostTableValidationException(
                        $"INTERVIEW_SLOT_COUNT_L{level} ({tunedSlotCount}) must match StaffRefreshCostTable.interviewSlotCount ({refreshRow.interviewSlotCount}).");
                }
            }
        }

        private static int ParseRequiredInt(IReadOnlyDictionary<string, string> tuning, string key)
        {
            if (!tuning.TryGetValue(key, out string raw))
            {
                throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME} missing required key={key}");
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME} key={key} is not a valid int: {raw}");
            }

            return parsed;
        }

        private static float ParseRequiredFloat(IReadOnlyDictionary<string, string> tuning, string key)
        {
            if (!tuning.TryGetValue(key, out string raw))
            {
                throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME} missing required key={key}");
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                throw new StaffGachaPoolTableValidationException($"{STAFF_TUNING_TABLE_NAME} key={key} is not a valid float: {raw}");
            }

            return parsed;
        }

        private void EnsureReady()
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("GachaTableLoader is not initialized.");
            }
        }
    }
}
