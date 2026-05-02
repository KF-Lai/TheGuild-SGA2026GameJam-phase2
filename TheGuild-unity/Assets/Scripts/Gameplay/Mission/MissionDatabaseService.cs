using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// C-01 任務資料庫查詢服務（concrete singleton）。
    /// 必須在 RaceService（order=0）之前完成 Awake，DataManager（order=-300）之後。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class MissionDatabaseService : MonoBehaviour
    {
        private const string EscortTypeIDKey = "ESCORT_TYPE_ID";
        private const string FactionNeutralIDKey = "FACTION_NEUTRAL_ID";

        private static readonly IReadOnlyList<MissionTemplate> EmptyTemplateList = Array.Empty<MissionTemplate>();
        private static readonly IReadOnlyList<MissionTypeData> EmptyTypeList = Array.Empty<MissionTypeData>();

        private static Func<int, bool> _factionRouteValidatorForTests;

        // === v3.1 patch P3.1-001 ===
        /// <summary>TraitTable FK 驗證器（測試注入用）；null 時跳過 requiredTraitID FK 驗證。</summary>
        private static Func<int, bool> _traitTableValidatorForTests;

        private IReadOnlyDictionary<int, MissionTemplate> _templateByID = new Dictionary<int, MissionTemplate>();
        private IReadOnlyDictionary<string, MissionDifficultyData> _difficultyByKey =
            new Dictionary<string, MissionDifficultyData>(StringComparer.Ordinal);
        private IReadOnlyDictionary<int, MissionTypeData> _typeByID = new Dictionary<int, MissionTypeData>();
        private IReadOnlyDictionary<int, MissionCategoryData> _categoryByID = new Dictionary<int, MissionCategoryData>();
        private IReadOnlyDictionary<string, IReadOnlyList<MissionTemplate>> _regularByDifficulty =
            new Dictionary<string, IReadOnlyList<MissionTemplate>>(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>> _regularByDifficultyType =
            new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>>(StringComparer.Ordinal);
        private IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>> _byCategory =
            new Dictionary<int, IReadOnlyList<MissionTemplate>>();
        private IReadOnlyList<MissionTypeData> _allMissionTypes = EmptyTypeList;

        private EscortDurationCalculator _escortDurationCalculator;
        private MissionTextFacade _missionTextFacade;

        private int _escortTypeID;
        private int _factionNeutralID;

        public static MissionDatabaseService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<MissionTemplate>("MissionTemplate");
            DataManager.RegisterTable<MissionDifficultyData>("MissionDifficultyTable");
            DataManager.RegisterTable<MissionTypeData>("MissionTypeTable");
            DataManager.RegisterTable<MissionCategoryData>("MissionCategoryTable");
            // D-02 任務名稱池：MissionTextFacade 透過 PickRandomWhere<MissionNameData> 消費。
            DataManager.RegisterTable<MissionNameData>("MissionNamePool");
        }

        public MissionTemplate GetTemplate(int missionID)
        {
            if (_templateByID.TryGetValue(missionID, out MissionTemplate row))
            {
                return row;
            }

            Debug.LogWarning($"[MissionDatabaseService] 找不到 missionID={missionID} 的模板。");
            return null;
        }

        public IReadOnlyList<MissionTemplate> GetRegularTemplates(string difficulty)
        {
            string key = NormalizeDifficulty(difficulty);
            if (_regularByDifficulty.TryGetValue(key, out IReadOnlyList<MissionTemplate> list))
            {
                return list;
            }

            return EmptyTemplateList;
        }

        public IReadOnlyList<MissionTemplate> GetRegularTemplates(string difficulty, int typeID)
        {
            string key = NormalizeDifficulty(difficulty);
            if (_regularByDifficultyType.TryGetValue(key, out IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>> map) &&
                map.TryGetValue(typeID, out IReadOnlyList<MissionTemplate> list))
            {
                return list;
            }

            return EmptyTemplateList;
        }

        public IReadOnlyList<MissionTemplate> GetTemplatesByCategory(int categoryID)
        {
            if (_byCategory.TryGetValue(categoryID, out IReadOnlyList<MissionTemplate> list))
            {
                return list;
            }

            return EmptyTemplateList;
        }

        public int GetBaseReward(string difficulty)
        {
            return TryGetDifficulty(difficulty, out MissionDifficultyData row) ? row.baseReward : 0;
        }

        public int GetBaseDuration(string difficulty)
        {
            return TryGetDifficulty(difficulty, out MissionDifficultyData row) ? row.baseDuration : 0;
        }

        public int GetEscortDuration(string difficulty)
        {
            return _escortDurationCalculator.GetEscortDuration(difficulty);
        }

        public float GetBaseDeathRate(string difficulty)
        {
            return TryGetDifficulty(difficulty, out MissionDifficultyData row) ? row.baseDeathRate : 0f;
        }

        public int GetFactionScoreDelta(string difficulty)
        {
            return TryGetDifficulty(difficulty, out MissionDifficultyData row) ? row.factionScoreDelta : 0;
        }

        public (string name, string desc) GetMissionText(string difficulty, int typeID)
        {
            return _missionTextFacade.GetMissionText(difficulty, typeID);
        }

        public bool IsValidCombination(string difficulty, int typeID)
        {
            if (typeID != _escortTypeID)
            {
                return true;
            }

            return MissionDatabaseLoader.IsEscortDifficultyAllowed(difficulty);
        }

        public string GetTypeName(int typeID)
        {
            if (_typeByID.TryGetValue(typeID, out MissionTypeData row))
            {
                return row.typeName;
            }

            Debug.LogWarning($"[MissionDatabaseService] 找不到 typeID={typeID} 的名稱。");
            return null;
        }

        public string GetCategoryName(int categoryID)
        {
            if (_categoryByID.TryGetValue(categoryID, out MissionCategoryData row))
            {
                return row.categoryName;
            }

            Debug.LogWarning($"[MissionDatabaseService] 找不到 categoryID={categoryID} 的名稱。");
            return null;
        }

        public IReadOnlyList<MissionTypeData> GetAllMissionTypes()
        {
            return _allMissionTypes;
        }

        internal static void SetFactionRouteValidatorForTests(Func<int, bool> validator)
        {
            _factionRouteValidatorForTests = validator;
        }

        // === v3.1 patch P3.1-001 ===
        internal static void SetTraitTableValidatorForTests(Func<int, bool> validator)
        {
            _traitTableValidatorForTests = validator;
        }

        internal static void ResetForTests()
        {
            _factionRouteValidatorForTests = null;
            _traitTableValidatorForTests = null; // === v3.1 patch P3.1-001 ===
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

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        private void Awake()
        {
            InitializeInstance();
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

            InitializeDatabase();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitializeDatabase()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[MissionDatabaseService] DataManager.Instance 為 null，MissionDatabase 使用空資料啟動。");
                _escortDurationCalculator = new EscortDurationCalculator(_difficultyByKey);
                _missionTextFacade = new MissionTextFacade();
                return;
            }

            _escortTypeID = DataManager.Instance.GetInt(EscortTypeIDKey);
            _factionNeutralID = DataManager.Instance.GetInt(FactionNeutralIDKey);

            MissionDatabaseLoader loader = new MissionDatabaseLoader(
                _escortTypeID,
                _factionNeutralID,
                _factionRouteValidatorForTests,
                _traitTableValidatorForTests); // === v3.1 patch P3.1-001 ===
            MissionDatabaseCache cache = loader.Build();

            _templateByID = cache.TemplateByID;
            _difficultyByKey = cache.DifficultyByKey;
            _typeByID = cache.TypeByID;
            _categoryByID = cache.CategoryByID;
            _regularByDifficulty = cache.RegularTemplatesByDifficulty;
            _regularByDifficultyType = cache.RegularTemplatesByDifficultyAndType;
            _byCategory = cache.TemplatesByCategory;
            _allMissionTypes = cache.AllMissionTypes;

            _escortDurationCalculator = new EscortDurationCalculator(_difficultyByKey);
            _missionTextFacade = new MissionTextFacade();
        }

        private bool TryGetDifficulty(string difficulty, out MissionDifficultyData row)
        {
            string key = NormalizeDifficulty(difficulty);
            return _difficultyByKey.TryGetValue(key, out row);
        }

        private static string NormalizeDifficulty(string difficulty)
        {
            return string.IsNullOrWhiteSpace(difficulty) ? string.Empty : difficulty.Trim();
        }
    }
}
