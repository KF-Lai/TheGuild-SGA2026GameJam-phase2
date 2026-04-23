using System;
using System.Collections.Generic;

namespace TheGuild.Core.Data
{
    /// <summary>
    /// 無置回隨機抽樣工具（uniform / weighted）。
    /// </summary>
    public static class RandomPool
    {
        /// <summary>
        /// 從來源池抽取指定數量元素（無置回）。
        /// </summary>
        public static List<T> PickWithoutReplacement<T>(
            IReadOnlyList<T> source,
            int pickCount,
            string pickMode,
            IReadOnlyList<float> weights,
            Random random,
            out bool fallbackToUniform)
        {
            fallbackToUniform = false;

            if (source == null || source.Count == 0 || pickCount <= 0)
            {
                return new List<T>(0);
            }

            Random rng = random ?? new Random();
            List<int> remainingIndices = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                remainingIndices.Add(i);
            }

            List<float> remainingWeights = BuildRemainingWeights(source.Count, weights);
            bool useWeighted = string.Equals(pickMode, "weighted", StringComparison.OrdinalIgnoreCase);
            if (useWeighted && !HasPositiveWeight(remainingWeights))
            {
                useWeighted = false;
                fallbackToUniform = true;
            }

            int safePickCount = pickCount > source.Count ? source.Count : pickCount;
            List<T> result = new List<T>(safePickCount);

            for (int pickIndex = 0; pickIndex < safePickCount; pickIndex++)
            {
                if (remainingIndices.Count == 0)
                {
                    break;
                }

                int selectedIndexInRemaining;
                if (useWeighted)
                {
                    selectedIndexInRemaining = PickWeightedIndex(remainingWeights, rng, out bool hasValidWeight);
                    if (!hasValidWeight)
                    {
                        useWeighted = false;
                        fallbackToUniform = true;
                        selectedIndexInRemaining = rng.Next(remainingIndices.Count);
                    }
                }
                else
                {
                    selectedIndexInRemaining = rng.Next(remainingIndices.Count);
                }

                int selectedSourceIndex = remainingIndices[selectedIndexInRemaining];
                result.Add(source[selectedSourceIndex]);
                remainingIndices.RemoveAt(selectedIndexInRemaining);
                remainingWeights.RemoveAt(selectedIndexInRemaining);
            }

            return result;
        }

        private static List<float> BuildRemainingWeights(int count, IReadOnlyList<float> weights)
        {
            List<float> result = new List<float>(count);

            if (weights == null)
            {
                for (int i = 0; i < count; i++)
                {
                    result.Add(1f);
                }

                return result;
            }

            for (int i = 0; i < count; i++)
            {
                if (i < weights.Count)
                {
                    float value = weights[i];
                    result.Add(value > 0f ? value : 0f);
                }
                else
                {
                    result.Add(1f);
                }
            }

            return result;
        }

        private static bool HasPositiveWeight(IReadOnlyList<float> weights)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static int PickWeightedIndex(IReadOnlyList<float> weights, Random random, out bool hasValidWeight)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                total += weights[i];
            }

            if (total <= 0f)
            {
                hasValidWeight = false;
                return 0;
            }

            double roll = random.NextDouble() * total;
            float cumulative = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative)
                {
                    hasValidWeight = true;
                    return i;
                }
            }

            hasValidWeight = true;
            return weights.Count - 1;
        }
    }
}
