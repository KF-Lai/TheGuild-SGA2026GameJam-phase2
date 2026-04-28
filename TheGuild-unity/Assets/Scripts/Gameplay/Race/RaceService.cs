using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Profession;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TheGuild.Gameplay.Race
{
    /// <summary>
    /// C-04 種族系統服務（concrete singleton）。
    /// </summary>
    public sealed class RaceService : MonoBehaviour
    {
        // GDD §3.1 保留 ID，非寫死值。
        private const int FALLBACK_RACE_ID = 1;

        private static readonly IReadOnlyList<RaceData> EmptyList = Array.Empty<RaceData>();

        private IReadOnlyDictionary<int, RaceData> _raceByID = new Dictionary<int, RaceData>();
        private IReadOnlyList<RaceData> _allRaces = EmptyList;

        public static RaceService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<RaceData>("RaceTable");
        }

        public RaceData GetRace(int raceID)
        {
            if (raceID == 0)
            {
                Debug.LogWarning("[RaceService] GetRace(0): 0 is null sentinel, return null.");
                return null;
            }

            if (_raceByID.TryGetValue(raceID, out RaceData row))
            {
                return row;
            }

            Debug.LogWarning($"[RaceService] GetRace: unknown raceID={raceID}, return null.");
            return null;
        }

        public IReadOnlyList<RaceData> GetAllRaces()
        {
            return _allRaces;
        }

        public float GetSuccessDelta(int raceID, int typeID)
        {
            if (!TryGetRaceForModifierLookup(raceID, nameof(GetSuccessDelta), out RaceData race))
            {
                return 0f;
            }

            if (race.TryGetModifier(typeID, out RaceModifierEntry entry))
            {
                return entry.successDelta;
            }

            return 0f;
        }

        public float GetDeathDelta(int raceID, int typeID)
        {
            if (!TryGetRaceForModifierLookup(raceID, nameof(GetDeathDelta), out RaceData race))
            {
                return 0f;
            }

            if (race.TryGetModifier(typeID, out RaceModifierEntry entry))
            {
                return entry.deathDelta;
            }

            return 0f;
        }

        public int RollRace(int professionID)
        {
            if (ProfessionService.Instance == null)
            {
                Debug.LogError("[RaceService] RollRace: ProfessionService.Instance is null, return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            ProfessionData profession = ProfessionService.Instance.GetProfession(professionID);
            if (profession == null)
            {
                Debug.LogError($"[RaceService] RollRace: unknown professionID={professionID}, return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            IReadOnlyList<int> raceIDs = profession.RaceIDs;
            IReadOnlyList<int> raceWeights = profession.RaceWeights;

            if (raceIDs == null || raceIDs.Count == 0)
            {
                Debug.LogError($"[RaceService] RollRace: professionID={professionID} has empty raceIDs, return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            if (raceWeights == null || raceIDs.Count != raceWeights.Count)
            {
                Debug.LogError(
                    $"[RaceService] RollRace: professionID={professionID} raceIDs.Count({raceIDs.Count}) != raceWeights.Count({(raceWeights == null ? -1 : raceWeights.Count)}), return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            List<int> filteredRaceIDs = new List<int>(raceIDs.Count);
            List<int> filteredWeights = new List<int>(raceIDs.Count);

            for (int i = 0; i < raceIDs.Count; i++)
            {
                int raceID = raceIDs[i];
                int weight = raceWeights[i];

                if (!_raceByID.ContainsKey(raceID))
                {
                    Debug.LogWarning($"[RaceService] RollRace: professionID={professionID} has unknown raceID={raceID}, removed.");
                    continue;
                }

                if (weight <= 0)
                {
                    Debug.LogWarning($"[RaceService] RollRace: professionID={professionID} raceID={raceID} has non-positive weight={weight}, removed.");
                    continue;
                }

                filteredRaceIDs.Add(raceID);
                filteredWeights.Add(weight);
            }

            if (filteredRaceIDs.Count == 0)
            {
                Debug.LogError($"[RaceService] RollRace: professionID={professionID} has no valid race candidates, return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            int totalWeight = 0;
            for (int i = 0; i < filteredWeights.Count; i++)
            {
                totalWeight += filteredWeights[i];
            }

            if (totalWeight <= 0)
            {
                Debug.LogError($"[RaceService] RollRace: professionID={professionID} totalWeight={totalWeight}, return fallback raceID=1.");
                return FALLBACK_RACE_ID;
            }

            int roll = Random.Range(0, totalWeight);
            int cumulative = 0;
            for (int i = 0; i < filteredRaceIDs.Count; i++)
            {
                cumulative += filteredWeights[i];
                if (roll < cumulative)
                {
                    return filteredRaceIDs[i];
                }
            }

            // 防禦性 fallback：理論上不會走到，但可避免極端資料造成無回傳。
            return filteredRaceIDs[filteredRaceIDs.Count - 1];
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
            RaceDatabaseLoader loader = new RaceDatabaseLoader();
            RaceDatabaseCache cache = loader.Build();
            _raceByID = cache.RaceByID;
            _allRaces = cache.AllRaces;
        }

        private bool TryGetRaceForModifierLookup(string methodName, int raceID, out RaceData race)
        {
            if (raceID == 0)
            {
                Debug.LogWarning($"[RaceService] {methodName}: raceID=0 is null sentinel, return 0.");
                race = null;
                return false;
            }

            if (!_raceByID.TryGetValue(raceID, out race))
            {
                Debug.LogWarning($"[RaceService] {methodName}: unknown raceID={raceID}, return 0.");
                return false;
            }

            return true;
        }

        private bool TryGetRaceForModifierLookup(int raceID, string methodName, out RaceData race)
        {
            return TryGetRaceForModifierLookup(methodName, raceID, out race);
        }
    }
}
