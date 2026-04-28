using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Profession
{
    /// <summary>
    /// C-03 職業資料查詢服務（concrete singleton）。
    /// </summary>
    public sealed class ProfessionService : MonoBehaviour
    {
        private static readonly IReadOnlyList<ProfessionData> EmptyList = Array.Empty<ProfessionData>();

        private IReadOnlyDictionary<int, ProfessionData> _professionByID = new Dictionary<int, ProfessionData>();
        private IReadOnlyList<ProfessionData> _allProfessions = EmptyList;
        private IReadOnlyList<ProfessionData> _baseProfessions = EmptyList;
        private IReadOnlyDictionary<int, IReadOnlyList<ProfessionData>> _upgradePathsByBaseID =
            new Dictionary<int, IReadOnlyList<ProfessionData>>();

        public static ProfessionService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<ProfessionData>("ProfessionTable");
        }

        public ProfessionData GetProfession(int professionID)
        {
            if (professionID == 0)
            {
                Debug.LogWarning("[ProfessionService] GetProfession(0): 0 is null sentinel, return null.");
                return null;
            }

            if (_professionByID.TryGetValue(professionID, out ProfessionData row))
            {
                return row;
            }

            return null;
        }

        public IReadOnlyList<ProfessionData> GetAllProfessions()
        {
            return _allProfessions;
        }

        public IReadOnlyList<ProfessionData> GetBaseProfessions()
        {
            return _baseProfessions;
        }

        public bool IsStrongType(int professionID, int typeID)
        {
            if (!TryGetProfessionForTypeCheck(professionID, nameof(IsStrongType), out ProfessionData row))
            {
                return false;
            }

            return row.ContainsStrongType(typeID);
        }

        public bool IsWeakType(int professionID, int typeID)
        {
            if (!TryGetProfessionForTypeCheck(professionID, nameof(IsWeakType), out ProfessionData row))
            {
                return false;
            }

            return row.ContainsWeakType(typeID);
        }

        public IReadOnlyList<ProfessionData> GetUpgradePaths(int professionID)
        {
            if (professionID <= 0)
            {
                return EmptyList;
            }

            if (_upgradePathsByBaseID.TryGetValue(professionID, out IReadOnlyList<ProfessionData> rows))
            {
                return rows;
            }

            return EmptyList;
        }

        public ProfessionData GetBaseProfession(int professionID)
        {
            if (professionID <= 0)
            {
                return null;
            }

            if (!_professionByID.TryGetValue(professionID, out ProfessionData row))
            {
                return null;
            }

            if (row.Tier <= 1 || row.BaseProfessionID <= 0)
            {
                return null;
            }

            if (_professionByID.TryGetValue(row.BaseProfessionID, out ProfessionData baseRow))
            {
                return baseRow;
            }

            return null;
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
            ProfessionDatabaseLoader loader = new ProfessionDatabaseLoader();
            ProfessionDatabaseCache cache = loader.Build();
            _professionByID = cache.ProfessionByID;
            _allProfessions = cache.AllProfessions;
            _baseProfessions = cache.BaseProfessions;
            _upgradePathsByBaseID = cache.UpgradePathsByBaseID;
        }

        private bool TryGetProfessionForTypeCheck(string methodName, int professionID, out ProfessionData row)
        {
            if (professionID <= 0)
            {
                Debug.LogWarning($"[ProfessionService] {methodName}: unknown professionID={professionID}, return false.");
                row = null;
                return false;
            }

            if (!_professionByID.TryGetValue(professionID, out row))
            {
                Debug.LogWarning($"[ProfessionService] {methodName}: unknown professionID={professionID}, return false.");
                return false;
            }

            return true;
        }

        private bool TryGetProfessionForTypeCheck(int professionID, string methodName, out ProfessionData row)
        {
            return TryGetProfessionForTypeCheck(methodName, professionID, out row);
        }
    }
}
