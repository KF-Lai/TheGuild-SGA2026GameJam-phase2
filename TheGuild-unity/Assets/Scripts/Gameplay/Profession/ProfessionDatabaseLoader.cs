using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using UnityEngine;

namespace TheGuild.Gameplay.Profession
{
    /// <summary>
    /// C-03 職業資料載入與驗證。
    /// </summary>
    public sealed class ProfessionDatabaseLoader
    {
        public ProfessionDatabaseCache Build()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[ProfessionDatabaseLoader] DataManager.Instance is null, return empty cache.");
                return ProfessionDatabaseCache.Empty;
            }

            IReadOnlyList<ProfessionData> rawList = DataManager.Instance.GetAll<ProfessionData>();
            HashSet<int> allProfessionIDs = BuildAllProfessionIDSet(rawList);
            HashSet<int> validTypeIDs = BuildValidTypeIDSet();

            Dictionary<int, ProfessionData> professionByID = new Dictionary<int, ProfessionData>();
            List<ProfessionData> allProfessions = new List<ProfessionData>(rawList.Count);
            List<ProfessionData> baseProfessions = new List<ProfessionData>();
            Dictionary<int, List<ProfessionData>> upgradePathBuffer = new Dictionary<int, List<ProfessionData>>();

            for (int i = 0; i < rawList.Count; i++)
            {
                ProfessionData row = rawList[i];
                if (row == null)
                {
                    continue;
                }

                int professionID = row.professionID;
                if (professionID <= 0)
                {
                    Debug.LogError("[ProfessionDatabaseLoader] professionID must be > 0, skip row.");
                    continue;
                }

                if (!TryParseIDSet(row.strongTypeIDs, row.professionID, "strongTypeIDs", out HashSet<int> strongTypeIDSet) ||
                    !TryParseIDSet(row.weakTypeIDs, row.professionID, "weakTypeIDs", out HashSet<int> weakTypeIDSet) ||
                    !TryParseIDArray(row.raceIDs, row.professionID, "raceIDs", out int[] raceIDs) ||
                    !TryParseIntArray(row.raceWeights, row.professionID, "raceWeights", out int[] raceWeights) ||
                    !TryParseIDArray(row.traitGroupIDs, row.professionID, "traitGroupIDs", out int[] traitGroupIDs))
                {
                    continue;
                }

                if (HasIntersection(strongTypeIDSet, weakTypeIDSet))
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} strongTypeIDs and weakTypeIDs overlap, skip row.");
                    continue;
                }

                if (raceWeights.Length != raceIDs.Length)
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} raceWeights.Length({raceWeights.Length}) != raceIDs.Length({raceIDs.Length}), skip row.");
                    continue;
                }

                if (HasNonPositiveValue(raceWeights))
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} raceWeights contains non-positive value, skip row.");
                    continue;
                }

                if (row.tier >= 2 && row.baseProfessionID == 0)
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} tier={row.tier} requires non-zero baseProfessionID, skip row.");
                    continue;
                }

                if (row.tier >= 2 && !allProfessionIDs.Contains(row.baseProfessionID))
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} baseProfessionID={row.baseProfessionID} not found in CSV, skip row.");
                    continue;
                }

                if (validTypeIDs != null)
                {
                    strongTypeIDSet = FilterInvalidTypeIDs(strongTypeIDSet, validTypeIDs, professionID, "strongTypeIDs");
                    weakTypeIDSet = FilterInvalidTypeIDs(weakTypeIDSet, validTypeIDs, professionID, "weakTypeIDs");
                }

                row.FreezeCollections(strongTypeIDSet, weakTypeIDSet, raceIDs, raceWeights, traitGroupIDs);

                if (professionByID.ContainsKey(professionID))
                {
                    Debug.LogWarning($"[ProfessionDatabaseLoader] duplicate professionID={professionID}, later row overrides previous row.");
                }

                professionByID[professionID] = row;
                allProfessions.Add(row);

                if (row.tier == 1)
                {
                    baseProfessions.Add(row);
                }

                if (row.tier >= 2)
                {
                    if (!upgradePathBuffer.TryGetValue(row.baseProfessionID, out List<ProfessionData> list))
                    {
                        list = new List<ProfessionData>();
                        upgradePathBuffer[row.baseProfessionID] = list;
                    }

                    list.Add(row);
                }
            }

            allProfessions.Sort((a, b) => a.professionID.CompareTo(b.professionID));
            baseProfessions.Sort((a, b) => a.professionID.CompareTo(b.professionID));

            Dictionary<int, IReadOnlyList<ProfessionData>> upgradePathsByBaseID = new Dictionary<int, IReadOnlyList<ProfessionData>>();
            foreach (KeyValuePair<int, List<ProfessionData>> pair in upgradePathBuffer)
            {
                pair.Value.Sort((a, b) => a.professionID.CompareTo(b.professionID));
                upgradePathsByBaseID[pair.Key] = pair.Value.ToArray();
            }

            return new ProfessionDatabaseCache(
                professionByID,
                allProfessions.ToArray(),
                baseProfessions.ToArray(),
                upgradePathsByBaseID);
        }

        private static HashSet<int> BuildAllProfessionIDSet(IReadOnlyList<ProfessionData> rawList)
        {
            HashSet<int> allProfessionIDs = new HashSet<int>();
            for (int i = 0; i < rawList.Count; i++)
            {
                ProfessionData row = rawList[i];
                if (row != null && row.professionID > 0)
                {
                    allProfessionIDs.Add(row.professionID);
                }
            }

            return allProfessionIDs;
        }

        private static HashSet<int> BuildValidTypeIDSet()
        {
            if (MissionDatabaseService.Instance == null)
            {
                Debug.LogError("[ProfessionDatabaseLoader] MissionDatabaseService.Instance is null, skip typeID filtering.");
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

        private static HashSet<int> FilterInvalidTypeIDs(
            HashSet<int> source,
            HashSet<int> validTypeIDs,
            int professionID,
            string fieldName)
        {
            HashSet<int> filtered = new HashSet<int>();
            foreach (int typeID in source)
            {
                if (!validTypeIDs.Contains(typeID))
                {
                    Debug.LogWarning($"[ProfessionDatabaseLoader] professionID={professionID} {fieldName} contains unknown typeID={typeID}, removed.");
                    continue;
                }

                filtered.Add(typeID);
            }

            return filtered;
        }

        private static bool TryParseIDSet(string[] rawTokens, int professionID, string fieldName, out HashSet<int> values)
        {
            values = new HashSet<int>();
            if (!TryParseIDArray(rawTokens, professionID, fieldName, out int[] parsed))
            {
                return false;
            }

            for (int i = 0; i < parsed.Length; i++)
            {
                values.Add(parsed[i]);
            }

            return true;
        }

        private static bool TryParseIDArray(string[] rawTokens, int professionID, string fieldName, out int[] values)
        {
            values = Array.Empty<int>();
            if (rawTokens == null || rawTokens.Length == 0)
            {
                return true;
            }

            List<int> buffer = new List<int>(rawTokens.Length);
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i] == null ? string.Empty : rawTokens[i].Trim();
                if (string.IsNullOrEmpty(token) || token == "0")
                {
                    continue;
                }

                if (!int.TryParse(token, out int parsed))
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} {fieldName}[{i}]={token} is not int, skip row.");
                    return false;
                }

                if (parsed <= 0)
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} {fieldName}[{i}]={parsed} must be > 0, skip row.");
                    return false;
                }

                buffer.Add(parsed);
            }

            values = buffer.ToArray();
            return true;
        }

        private static bool TryParseIntArray(string[] rawTokens, int professionID, string fieldName, out int[] values)
        {
            values = Array.Empty<int>();
            if (rawTokens == null || rawTokens.Length == 0)
            {
                return true;
            }

            List<int> buffer = new List<int>(rawTokens.Length);
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i] == null ? string.Empty : rawTokens[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (!int.TryParse(token, out int parsed))
                {
                    Debug.LogError($"[ProfessionDatabaseLoader] professionID={professionID} {fieldName}[{i}]={token} is not int, skip row.");
                    return false;
                }

                buffer.Add(parsed);
            }

            if (buffer.Count == 1 && buffer[0] == 0)
            {
                values = Array.Empty<int>();
                return true;
            }

            values = buffer.ToArray();
            return true;
        }

        private static bool HasIntersection(HashSet<int> left, HashSet<int> right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            HashSet<int> smaller = left.Count <= right.Count ? left : right;
            HashSet<int> larger = left.Count <= right.Count ? right : left;
            foreach (int value in smaller)
            {
                if (larger.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNonPositiveValue(int[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] <= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class ProfessionDatabaseCache
    {
        private static readonly IReadOnlyList<ProfessionData> EmptyList = Array.Empty<ProfessionData>();

        public static ProfessionDatabaseCache Empty { get; } = new ProfessionDatabaseCache(
            new Dictionary<int, ProfessionData>(),
            EmptyList,
            EmptyList,
            new Dictionary<int, IReadOnlyList<ProfessionData>>());

        public ProfessionDatabaseCache(
            IReadOnlyDictionary<int, ProfessionData> professionByID,
            IReadOnlyList<ProfessionData> allProfessions,
            IReadOnlyList<ProfessionData> baseProfessions,
            IReadOnlyDictionary<int, IReadOnlyList<ProfessionData>> upgradePathsByBaseID)
        {
            ProfessionByID = professionByID ?? new Dictionary<int, ProfessionData>();
            AllProfessions = allProfessions ?? EmptyList;
            BaseProfessions = baseProfessions ?? EmptyList;
            UpgradePathsByBaseID = upgradePathsByBaseID ?? new Dictionary<int, IReadOnlyList<ProfessionData>>();
        }

        public IReadOnlyDictionary<int, ProfessionData> ProfessionByID { get; }
        public IReadOnlyList<ProfessionData> AllProfessions { get; }
        public IReadOnlyList<ProfessionData> BaseProfessions { get; }
        public IReadOnlyDictionary<int, IReadOnlyList<ProfessionData>> UpgradePathsByBaseID { get; }
    }
}
