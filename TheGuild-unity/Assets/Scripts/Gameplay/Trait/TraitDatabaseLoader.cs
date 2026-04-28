using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Trait
{
    /// <summary>
    /// C-05 特質資料載入器：驗證 CSV 並建立快取。
    /// </summary>
    public sealed class TraitDatabaseLoader
    {
        private static readonly IReadOnlyList<TraitData> EmptyTraitList = Array.Empty<TraitData>();
        private static readonly IReadOnlyList<TraitGroupData> EmptyGroupList = Array.Empty<TraitGroupData>();

        private static readonly HashSet<string> ValidEffectTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "stat",
            "behavior",
            "condition"
        };

        // GDD §3.2：stat 10 + behavior 8 + condition 5 = 23
        private static readonly HashSet<string> ValidEffectTargets = new HashSet<string>(StringComparer.Ordinal)
        {
            "success_all",
            "success_1",
            "success_2",
            "success_3",
            "success_4",
            "death_all",
            "death_1",
            "death_2",
            "death_3",
            "death_4",
            "willingness_all",
            "willingness_type_1",
            "willingness_type_2",
            "willingness_type_3",
            "willingness_type_4",
            "willingness_diff_S",
            "willingness_diff_A",
            "willingness_diff_low",
            "on_success_gold_bonus",
            "on_fail_reputation",
            "on_death_survive",
            "on_fail_survive",
            "on_success_reputation_bonus"
        };

        public TraitDatabaseCache Build()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[TraitDatabaseLoader] DataManager.Instance is null, return empty cache.");
                return TraitDatabaseCache.Empty;
            }

            IReadOnlyList<TraitData> rawTraits = DataManager.Instance.GetAll<TraitData>();
            Dictionary<int, TraitData> traitByID = new Dictionary<int, TraitData>();
            List<TraitData> allTraits = new List<TraitData>(rawTraits.Count);

            Dictionary<string, List<TraitData>> traitsByTypeBuffer =
                new Dictionary<string, List<TraitData>>(StringComparer.Ordinal)
                {
                    { "stat", new List<TraitData>() },
                    { "behavior", new List<TraitData>() },
                    { "condition", new List<TraitData>() }
                };

            for (int i = 0; i < rawTraits.Count; i++)
            {
                TraitData row = rawTraits[i];
                if (row == null)
                {
                    continue;
                }

                if (row.traitID <= 0)
                {
                    Debug.LogError("[TraitDatabaseLoader] traitID must be > 0, skip row.");
                    continue;
                }

                string normalizedType = NormalizeEffectType(row.effectType, row.traitID);
                if (normalizedType == null)
                {
                    continue;
                }

                string normalizedTarget = NormalizeEffectTarget(row.effectTarget, row.traitID);
                if (normalizedTarget == null)
                {
                    continue;
                }

                row.effectType = normalizedType;
                row.effectTarget = normalizedTarget;

                if (traitByID.ContainsKey(row.traitID))
                {
                    Debug.LogWarning($"[TraitDatabaseLoader] duplicate traitID={row.traitID}, later row overrides previous row.");
                }

                traitByID[row.traitID] = row;
                allTraits.Add(row);
                traitsByTypeBuffer[normalizedType].Add(row);
            }

            allTraits.Sort((a, b) => a.traitID.CompareTo(b.traitID));

            Dictionary<string, IReadOnlyList<TraitData>> traitsByType =
                new Dictionary<string, IReadOnlyList<TraitData>>(StringComparer.Ordinal)
                {
                    { "stat", traitsByTypeBuffer["stat"].ToArray() },
                    { "behavior", traitsByTypeBuffer["behavior"].ToArray() },
                    { "condition", traitsByTypeBuffer["condition"].ToArray() }
                };

            IReadOnlyList<TraitGroupData> rawGroups = DataManager.Instance.GetAll<TraitGroupData>();
            Dictionary<int, TraitGroupData> groupByID = new Dictionary<int, TraitGroupData>();
            List<TraitGroupData> allGroups = new List<TraitGroupData>(rawGroups.Count);

            for (int i = 0; i < rawGroups.Count; i++)
            {
                TraitGroupData row = rawGroups[i];
                if (row == null)
                {
                    continue;
                }

                if (row.groupID <= 0)
                {
                    Debug.LogError("[TraitDatabaseLoader] groupID must be > 0, skip row.");
                    continue;
                }

                if (!TryParseTraitIDs(row.traitIDs, row.groupID, out int[] parsedTraitIDs))
                {
                    continue;
                }

                List<int> filteredTraitIDs = new List<int>(parsedTraitIDs.Length);
                for (int idIndex = 0; idIndex < parsedTraitIDs.Length; idIndex++)
                {
                    int traitID = parsedTraitIDs[idIndex];
                    if (!traitByID.ContainsKey(traitID))
                    {
                        Debug.LogWarning($"[TraitDatabaseLoader] groupID={row.groupID} has unknown traitID={traitID}, removed.");
                        continue;
                    }

                    filteredTraitIDs.Add(traitID);
                }

                if (row.pickCount <= 0)
                {
                    Debug.LogError($"[TraitDatabaseLoader] groupID={row.groupID} pickCount={row.pickCount} must be > 0, fallback to 1.");
                    row.pickCount = 1;
                }

                row.pickMode = NormalizePickMode(row.pickMode, row.groupID);
                row.SetTraitIDs(filteredTraitIDs.ToArray());

                if (groupByID.ContainsKey(row.groupID))
                {
                    Debug.LogWarning($"[TraitDatabaseLoader] duplicate groupID={row.groupID}, later row overrides previous row.");
                }

                groupByID[row.groupID] = row;
                allGroups.Add(row);
            }

            allGroups.Sort((a, b) => a.groupID.CompareTo(b.groupID));

            return new TraitDatabaseCache(
                traitByID,
                allTraits.ToArray(),
                traitsByType,
                groupByID,
                allGroups.ToArray());
        }

        private static string NormalizeEffectType(string rawValue, int traitID)
        {
            string normalized = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
            if (!ValidEffectTypes.Contains(normalized))
            {
                Debug.LogError($"[TraitDatabaseLoader] traitID={traitID} effectType={rawValue} is invalid, skip row.");
                return null;
            }

            return normalized;
        }

        private static string NormalizeEffectTarget(string rawValue, int traitID)
        {
            string normalized = (rawValue ?? string.Empty).Trim();
            if (!ValidEffectTargets.Contains(normalized))
            {
                Debug.LogError($"[TraitDatabaseLoader] traitID={traitID} effectTarget={rawValue} is invalid, skip row.");
                return null;
            }

            return normalized;
        }

        private static string NormalizePickMode(string rawValue, int groupID)
        {
            string normalized = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized != "uniform" && normalized != "weighted")
            {
                Debug.LogError($"[TraitDatabaseLoader] groupID={groupID} pickMode={rawValue} is invalid, fallback to uniform.");
                return "uniform";
            }

            if (normalized == "weighted")
            {
                Debug.LogWarning($"[TraitDatabaseLoader] groupID={groupID} pickMode=weighted is reserved, fallback to uniform.");
                return "uniform";
            }

            return normalized;
        }

        private static bool TryParseTraitIDs(string[] rawTokens, int groupID, out int[] values)
        {
            values = Array.Empty<int>();
            if (rawTokens == null || rawTokens.Length == 0)
            {
                return true;
            }

            List<int> parsed = new List<int>(rawTokens.Length);
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i] == null ? string.Empty : rawTokens[i].Trim();
                if (string.IsNullOrEmpty(token) || token == "0")
                {
                    continue;
                }

                if (!int.TryParse(token, out int traitID))
                {
                    Debug.LogError($"[TraitDatabaseLoader] groupID={groupID} traitIDs[{i}]={token} is not int, skip row.");
                    return false;
                }

                if (traitID <= 0)
                {
                    Debug.LogError($"[TraitDatabaseLoader] groupID={groupID} traitIDs[{i}]={traitID} must be > 0, skip row.");
                    return false;
                }

                parsed.Add(traitID);
            }

            values = parsed.ToArray();
            return true;
        }
    }

    public sealed class TraitDatabaseCache
    {
        private static readonly IReadOnlyList<TraitData> EmptyTraitList = Array.Empty<TraitData>();
        private static readonly IReadOnlyList<TraitGroupData> EmptyGroupList = Array.Empty<TraitGroupData>();

        public static TraitDatabaseCache Empty { get; } = new TraitDatabaseCache(
            new Dictionary<int, TraitData>(),
            EmptyTraitList,
            new Dictionary<string, IReadOnlyList<TraitData>>(StringComparer.Ordinal),
            new Dictionary<int, TraitGroupData>(),
            EmptyGroupList);

        public TraitDatabaseCache(
            IReadOnlyDictionary<int, TraitData> traitByID,
            IReadOnlyList<TraitData> allTraits,
            IReadOnlyDictionary<string, IReadOnlyList<TraitData>> traitsByType,
            IReadOnlyDictionary<int, TraitGroupData> groupByID,
            IReadOnlyList<TraitGroupData> allGroups)
        {
            TraitByID = traitByID ?? new Dictionary<int, TraitData>();
            AllTraits = allTraits ?? EmptyTraitList;
            TraitsByType = traitsByType ?? new Dictionary<string, IReadOnlyList<TraitData>>(StringComparer.Ordinal);
            GroupByID = groupByID ?? new Dictionary<int, TraitGroupData>();
            AllGroups = allGroups ?? EmptyGroupList;
        }

        public IReadOnlyDictionary<int, TraitData> TraitByID { get; }
        public IReadOnlyList<TraitData> AllTraits { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<TraitData>> TraitsByType { get; }
        public IReadOnlyDictionary<int, TraitGroupData> GroupByID { get; }
        public IReadOnlyList<TraitGroupData> AllGroups { get; }
    }
}
