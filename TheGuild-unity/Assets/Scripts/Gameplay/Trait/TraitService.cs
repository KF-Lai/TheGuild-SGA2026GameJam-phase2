using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Profession;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TheGuild.Gameplay.Trait
{
    /// <summary>
    /// C-05 特質服務（concrete singleton）。
    /// </summary>
    public sealed class TraitService : MonoBehaviour
    {
        private static readonly int[] EmptyIntArray = Array.Empty<int>();
        private static readonly IReadOnlyList<TraitData> EmptyTraitList = Array.Empty<TraitData>();
        private static readonly IReadOnlyList<TraitGroupData> EmptyGroupList = Array.Empty<TraitGroupData>();

        private IReadOnlyDictionary<int, TraitData> _traitByID = new Dictionary<int, TraitData>();
        private IReadOnlyList<TraitData> _allTraits = EmptyTraitList;
        private IReadOnlyDictionary<string, IReadOnlyList<TraitData>> _traitsByType =
            new Dictionary<string, IReadOnlyList<TraitData>>(StringComparer.Ordinal);
        private IReadOnlyDictionary<int, TraitGroupData> _groupByID = new Dictionary<int, TraitGroupData>();

        public static TraitService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<TraitData>("TraitTable");
            DataManager.RegisterTable<TraitGroupData>("TraitGroupTable");
        }

        public TraitData GetTrait(int traitID)
        {
            if (traitID == 0)
            {
                Debug.LogWarning("[TraitService] GetTrait(0): 0 is null sentinel, return null.");
                return null;
            }

            if (_traitByID.TryGetValue(traitID, out TraitData row))
            {
                return row;
            }

            return null;
        }

        public IReadOnlyList<TraitData> GetAllTraits()
        {
            return _allTraits;
        }

        public IReadOnlyList<TraitData> GetTraitsByType(string effectType)
        {
            string key = (effectType ?? string.Empty).Trim().ToLowerInvariant();
            if (_traitsByType.TryGetValue(key, out IReadOnlyList<TraitData> rows))
            {
                return rows;
            }

            return EmptyTraitList;
        }

        public TraitGroupData GetTraitGroup(int groupID)
        {
            if (groupID == 0)
            {
                Debug.LogWarning("[TraitService] GetTraitGroup(0): 0 is null sentinel, return null.");
                return null;
            }

            if (_groupByID.TryGetValue(groupID, out TraitGroupData row))
            {
                return row;
            }

            return null;
        }

        public int[] RollTraits(TraitGroupData group)
        {
            if (group == null)
            {
                Debug.LogWarning("[TraitService] RollTraits: group is null, return empty.");
                return EmptyIntArray;
            }

            IReadOnlyList<int> pool = group.TraitIDs;
            if (pool == null || pool.Count == 0)
            {
                return EmptyIntArray;
            }

            int pickCount = group.pickCount;
            if (pickCount <= 0)
            {
                Debug.LogWarning($"[TraitService] RollTraits: groupID={group.groupID} pickCount={pickCount} <= 0, return empty.");
                return EmptyIntArray;
            }

            if (pool.Count < pickCount)
            {
                Debug.LogWarning($"[TraitService] RollTraits: groupID={group.groupID} pickCount={pickCount} > pool.Count={pool.Count}, return all.");
                return ToArray(pool);
            }

            if (pool.Count == pickCount)
            {
                return ToArray(pool);
            }

            string mode = (group.pickMode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode == "weighted")
            {
                Debug.LogWarning($"[TraitService] RollTraits: groupID={group.groupID} pickMode=weighted is reserved, fallback to uniform.");
            }
            else if (mode != "uniform")
            {
                Debug.LogWarning($"[TraitService] RollTraits: groupID={group.groupID} pickMode={group.pickMode} is invalid, fallback to uniform.");
            }

            int[] shuffled = ToArray(pool);
            ShuffleInPlace(shuffled);

            int[] result = new int[pickCount];
            Array.Copy(shuffled, result, pickCount);
            return result;
        }

        public IReadOnlyList<TraitGroupData> GetProfessionGroups(int professionID)
        {
            if (ProfessionService.Instance == null)
            {
                Debug.LogError("[TraitService] GetProfessionGroups: ProfessionService.Instance is null, return empty.");
                return EmptyGroupList;
            }

            ProfessionData profession = ProfessionService.Instance.GetProfession(professionID);
            if (profession == null)
            {
                Debug.LogWarning($"[TraitService] GetProfessionGroups: unknown professionID={professionID}, return empty.");
                return EmptyGroupList;
            }

            IReadOnlyList<int> groupIDs = profession.TraitGroupIDs;
            if (groupIDs == null || groupIDs.Count == 0)
            {
                Debug.LogWarning($"[TraitService] GetProfessionGroups: professionID={professionID} has empty traitGroupIDs, return empty.");
                return EmptyGroupList;
            }

            List<TraitGroupData> result = new List<TraitGroupData>(groupIDs.Count);
            for (int i = 0; i < groupIDs.Count; i++)
            {
                int groupID = groupIDs[i];
                if (groupID == 0)
                {
                    continue;
                }

                if (!_groupByID.TryGetValue(groupID, out TraitGroupData group))
                {
                    Debug.LogWarning($"[TraitService] GetProfessionGroups: professionID={professionID} has unknown groupID={groupID}, removed.");
                    continue;
                }

                result.Add(group);
            }

            return result.Count == 0 ? EmptyGroupList : result.ToArray();
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

            TraitDatabaseLoader loader = new TraitDatabaseLoader();
            TraitDatabaseCache cache = loader.Build();
            _traitByID = cache.TraitByID;
            _allTraits = cache.AllTraits;
            _traitsByType = cache.TraitsByType;
            _groupByID = cache.GroupByID;
        }

        private static int[] ToArray(IReadOnlyList<int> source)
        {
            int[] result = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }
            return result;
        }

        private static void ShuffleInPlace(int[] values)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }
}
