using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// 護送任務時長計算器。
    /// </summary>
    public sealed class EscortDurationCalculator
    {
        private const string MinMultiplierKey = "ESCORT_DURATION_MULTIPLIER_MIN";
        private const string MaxMultiplierKey = "ESCORT_DURATION_MULTIPLIER_MAX";

        private readonly IReadOnlyDictionary<string, MissionDifficultyData> _difficultyByKey;

        public EscortDurationCalculator(IReadOnlyDictionary<string, MissionDifficultyData> difficultyByKey)
        {
            _difficultyByKey = difficultyByKey ?? new Dictionary<string, MissionDifficultyData>(StringComparer.Ordinal);
        }

        public int GetEscortDuration(string difficulty)
        {
            int baseDuration = GetBaseDuration(difficulty);
            if (DataManager.Instance == null)
            {
                Debug.LogWarning("[EscortDurationCalculator] DataManager.Instance 為 null，返回 baseDuration。");
                return baseDuration;
            }

            float min = DataManager.Instance.GetFloat(MinMultiplierKey);
            float max = DataManager.Instance.GetFloat(MaxMultiplierKey);
            if (max < min)
            {
                Debug.LogWarning($"[EscortDurationCalculator] 乘數範圍異常 min={min}, max={max}，已交換。");
                float swap = min;
                min = max;
                max = swap;
            }

            float multiplier = UnityEngine.Random.Range(min, max);
            return Mathf.CeilToInt(baseDuration * multiplier);
        }

        private int GetBaseDuration(string difficulty)
        {
            string key = NormalizeDifficulty(difficulty);
            if (_difficultyByKey.TryGetValue(key, out MissionDifficultyData row))
            {
                return row.baseDuration;
            }

            return 0;
        }

        private static string NormalizeDifficulty(string difficulty)
        {
            return string.IsNullOrWhiteSpace(difficulty) ? string.Empty : difficulty.Trim();
        }
    }
}
