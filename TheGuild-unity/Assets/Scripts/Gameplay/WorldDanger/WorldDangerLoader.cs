using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.WorldDanger
{
    /// <summary>
    /// 載入與驗證 WorldDangerTable。
    /// </summary>
    public sealed class WorldDangerLoader
    {
        private static readonly HashSet<string> ValidDangerLevels = new HashSet<string>(StringComparer.Ordinal)
        {
            "E", "D", "C", "B", "A"
        };

        private static readonly HashSet<string> ValidDifficulties = new HashSet<string>(StringComparer.Ordinal)
        {
            "F", "E", "D", "C", "B", "A", "S", "SS", "SSS"
        };

        public IReadOnlyDictionary<string, WorldDangerData> LoadAll()
        {
            Dictionary<string, WorldDangerData> dict = new Dictionary<string, WorldDangerData>(StringComparer.Ordinal);
            if (DataManager.Instance == null)
            {
                Debug.LogError("[WorldDangerLoader] DataManager.Instance 為 null，無法載入 WorldDangerTable。");
                return dict;
            }

            IReadOnlyList<WorldDangerData> rows = DataManager.Instance.GetAll<WorldDangerData>();
            for (int i = 0; i < rows.Count; i++)
            {
                WorldDangerData row = rows[i];
                if (row == null)
                {
                    continue;
                }

                if (!TryNormalizeDangerLevel(row, out string level))
                {
                    continue;
                }

                NormalizeMinDifficulty(row);
                ValidateWeights(row);
                ValidateMaxDebt(row);

                if (dict.ContainsKey(level))
                {
                    Debug.LogWarning($"[WorldDangerLoader] dangerLevel={level} 重複，後者覆蓋前者。");
                }

                dict[level] = row;
            }

            return dict;
        }

        private static bool TryNormalizeDangerLevel(WorldDangerData row, out string normalizedLevel)
        {
            normalizedLevel = string.Empty;

            string original = row.dangerLevel ?? string.Empty;
            string normalized = original.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                Debug.LogError("[WorldDangerLoader] dangerLevel 空值，略過該列。");
                return false;
            }

            if (!string.Equals(original, normalized, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[WorldDangerLoader] dangerLevel 大小寫不一致：{original} -> {normalized}");
            }

            if (!ValidDangerLevels.Contains(normalized))
            {
                Debug.LogError($"[WorldDangerLoader] dangerLevel 非法：{normalized}，略過該列。");
                return false;
            }

            row.dangerLevel = normalized;
            normalizedLevel = normalized;
            return true;
        }

        private static void NormalizeMinDifficulty(WorldDangerData row)
        {
            string normalized = (row.minDifficulty ?? string.Empty).Trim().ToUpperInvariant();
            if (!ValidDifficulties.Contains(normalized))
            {
                Debug.LogError($"[WorldDangerLoader] dangerLevel={row.dangerLevel} 的 minDifficulty 非法：{row.minDifficulty}，改寫為 F。");
                normalized = "F";
            }

            row.minDifficulty = normalized;
        }

        private static void ValidateWeights(WorldDangerData row)
        {
            long total =
                row.weightF_E +
                row.weightD +
                row.weightC +
                row.weightB +
                row.weightA +
                row.weightS_SSS;

            if (total == 0)
            {
                Debug.LogError($"[WorldDangerLoader] dangerLevel={row.dangerLevel} 權重全為 0。");
            }
        }

        private static void ValidateMaxDebt(WorldDangerData row)
        {
            if (row.maxDebt == 0)
            {
                Debug.LogError($"[WorldDangerLoader] dangerLevel={row.dangerLevel} 的 maxDebt 為 0。");
            }
        }
    }
}
