using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using UnityEngine;

namespace TheGuild.Gameplay.Race
{
    /// <summary>
    /// C-04 種族資料載入器。
    /// </summary>
    public sealed class RaceDatabaseLoader
    {
        private static readonly IReadOnlyDictionary<int, RaceModifierEntry> EmptyModifierCache =
            new Dictionary<int, RaceModifierEntry>();

        public RaceDatabaseCache Build()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[RaceDatabaseLoader] DataManager.Instance is null, return empty cache.");
                return RaceDatabaseCache.Empty;
            }

            IReadOnlyList<RaceData> rawList = DataManager.Instance.GetAll<RaceData>();
            HashSet<int> validTypeIDs = BuildValidTypeIDSet();

            Dictionary<int, RaceData> raceByID = new Dictionary<int, RaceData>();
            List<RaceData> allRaces = new List<RaceData>(rawList.Count);

            for (int i = 0; i < rawList.Count; i++)
            {
                RaceData row = rawList[i];
                if (row == null)
                {
                    continue;
                }

                if (row.raceID <= 0)
                {
                    Debug.LogError("[RaceDatabaseLoader] raceID must be > 0, skip row.");
                    continue;
                }

                IReadOnlyDictionary<int, RaceModifierEntry> cache = ParseModifiers(row, validTypeIDs);
                row.SetModifierCache(cache);

                if (raceByID.ContainsKey(row.raceID))
                {
                    Debug.LogWarning($"[RaceDatabaseLoader] duplicate raceID={row.raceID}, later row overrides previous row.");
                }

                raceByID[row.raceID] = row;
                allRaces.Add(row);
            }

            allRaces.Sort((a, b) => a.raceID.CompareTo(b.raceID));
            return new RaceDatabaseCache(raceByID, allRaces.ToArray());
        }

        private static HashSet<int> BuildValidTypeIDSet()
        {
            if (MissionDatabaseService.Instance == null)
            {
                Debug.LogError("[RaceDatabaseLoader] MissionDatabaseService.Instance is null, skip typeID filtering.");
                return null;
            }

            IReadOnlyList<MissionTypeData> missionTypes = MissionDatabaseService.Instance.GetAllMissionTypes();
            HashSet<int> validTypeIDs = new HashSet<int>();
            for (int i = 0; i < missionTypes.Count; i++)
            {
                MissionTypeData row = missionTypes[i];
                if (row != null)
                {
                    validTypeIDs.Add(row.typeID);
                }
            }

            return validTypeIDs;
        }

        private static IReadOnlyDictionary<int, RaceModifierEntry> ParseModifiers(RaceData race, HashSet<int> validTypeIDs)
        {
            string rawJson = race.modifiers == null ? string.Empty : race.modifiers.Trim();
            if (string.IsNullOrEmpty(rawJson) || rawJson == "[]")
            {
                return EmptyModifierCache;
            }

            RaceModifierListWrapper wrapper;
            try
            {
                string wrappedJson = "{\"modifiers\":" + rawJson + "}";
                wrapper = JsonUtility.FromJson<RaceModifierListWrapper>(wrappedJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceDatabaseLoader] raceID={race.raceID} modifiers JSON parse failed: {ex.Message}");
                return EmptyModifierCache;
            }

            if (wrapper == null || wrapper.modifiers == null || wrapper.modifiers.Count == 0)
            {
                return EmptyModifierCache;
            }

            Dictionary<int, RaceModifierEntry> cache = new Dictionary<int, RaceModifierEntry>();
            for (int i = 0; i < wrapper.modifiers.Count; i++)
            {
                RaceModifierEntry entry = wrapper.modifiers[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.typeID <= 0)
                {
                    Debug.LogWarning($"[RaceDatabaseLoader] raceID={race.raceID} modifiers[{i}] has invalid typeID={entry.typeID}, removed.");
                    continue;
                }

                if (validTypeIDs != null && !validTypeIDs.Contains(entry.typeID))
                {
                    Debug.LogWarning($"[RaceDatabaseLoader] raceID={race.raceID} modifiers contains unknown typeID={entry.typeID}, removed.");
                    continue;
                }

                if (cache.ContainsKey(entry.typeID))
                {
                    Debug.LogWarning($"[RaceDatabaseLoader] raceID={race.raceID} duplicate modifier typeID={entry.typeID}, later entry overrides previous entry.");
                }

                cache[entry.typeID] = entry;
            }

            return cache.Count == 0 ? EmptyModifierCache : cache;
        }
    }

    public sealed class RaceDatabaseCache
    {
        private static readonly IReadOnlyList<RaceData> EmptyList = Array.Empty<RaceData>();

        public static RaceDatabaseCache Empty { get; } = new RaceDatabaseCache(
            new Dictionary<int, RaceData>(),
            EmptyList);

        public RaceDatabaseCache(
            IReadOnlyDictionary<int, RaceData> raceByID,
            IReadOnlyList<RaceData> allRaces)
        {
            RaceByID = raceByID ?? new Dictionary<int, RaceData>();
            AllRaces = allRaces ?? EmptyList;
        }

        public IReadOnlyDictionary<int, RaceData> RaceByID { get; }
        public IReadOnlyList<RaceData> AllRaces { get; }
    }
}
