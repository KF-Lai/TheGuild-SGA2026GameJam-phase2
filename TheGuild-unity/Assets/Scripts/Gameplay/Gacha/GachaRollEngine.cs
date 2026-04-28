using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// 實作依據：【FT-08-FSD】gacha-system.md §3.3.3 / §4.1.5 / §4.1.6 / §4.1.8 / §5.4.3

namespace TheGuild.Gameplay.Gacha
{
    /// <summary>
    /// Pure roll engine for one candidate slot.
    /// No player-state mutation is performed here.
    /// </summary>
    public sealed class GachaRollEngine
    {
        private const int MIN_RARITY = 1;
        private const int MAX_RARITY = 5;
        private const int RARITY_ONE = 1;
        private const int RARITY_FIVE = 5;

        private readonly GachaTableLoader _loader;
        private readonly Func<long> _nowUtcProvider;

        public GachaRollEngine(GachaTableLoader loader, Func<long> nowUtcProvider)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _nowUtcProvider = nowUtcProvider ?? throw new ArgumentNullException(nameof(nowUtcProvider));
        }

        public (CandidateCard card, bool pityHit) RollOneSlot(
            int slotIndex,
            int poolID,
            bool isFirstSlotInRefresh,
            int pityCounter)
        {
            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "slotIndex must be >= 0.");
            }

            bool pityHit = false;
            int rolledRarity;

            // Step 1 - pity check (FSD §4.1.8 / §5.4.3)
            IReadOnlyList<int> eligibleFiveStar = _loader.GetEligibleStaffByRarity(poolID, RARITY_FIVE);
            bool shouldForce5Star =
                isFirstSlotInRefresh &&
                pityCounter >= _loader.PityThreshold &&
                eligibleFiveStar != null &&
                eligibleFiveStar.Count > 0;

            if (shouldForce5Star)
            {
                rolledRarity = RARITY_FIVE;
                pityHit = true;
            }
            else
            {
                // Step 2 - rarity dynamic normalization (FSD §4.1.5 / §5.4.3)
                rolledRarity = RollRarityWithDynamicNormalization(poolID);
            }

            // Step 3 - staff weighted roll in selected rarity (FSD §4.1.6 / §5.4.3)
            IReadOnlyList<int> eligible = _loader.GetEligibleStaffByRarity(poolID, rolledRarity);
            if (eligible == null || eligible.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No eligible staff in rolled rarity. poolID={poolID}, rarity={rolledRarity}");
            }

            IReadOnlyDictionary<int, int> weights = _loader.GetStaffWeights(poolID);
            if (weights == null)
            {
                throw new InvalidOperationException($"Staff weights are null. poolID={poolID}");
            }

            int rolledStaffID = WeightedRoll(
                eligible,
                staffID =>
                {
                    int weight;
                    return weights.TryGetValue(staffID, out weight) ? weight : 0;
                });

            int trashItemID = 0;

            // Step 4 - trash detection (FSD §3.5.3 / §5.4.3)
            if (rolledRarity == RARITY_ONE && Random.value < _loader.TrashRollRateAtRarity1)
            {
                IReadOnlyList<TrashItemData> trashItems = _loader.GetTrashItems();
                if (trashItems != null && trashItems.Count > 0)
                {
                    int trashIndex = Random.Range(0, trashItems.Count);
                    trashItemID = trashItems[trashIndex].trashItemID;
                    rolledStaffID = 0;
                }
            }

            // Step 5 - build candidate card (FSD §5.4.3)
            CandidateCard card = new CandidateCard
            {
                poolID = poolID,
                slotIndex = slotIndex,
                staffID = rolledStaffID,
                trashItemID = trashItemID,
                rolledRarity = rolledRarity,
                rolledTimestamp = _nowUtcProvider(),
                isReserved = false,
                reservedTimestamp = 0L,
                reserveConsumedFlag = false
            };

            return (card, pityHit);
        }

        private int RollRarityWithDynamicNormalization(int poolID)
        {
            float normalizationDen = 1f;
            for (int rarity = MIN_RARITY; rarity <= MAX_RARITY; rarity++)
            {
                if (IsTierEmpty(poolID, rarity))
                {
                    normalizationDen -= _loader.GetBaseProb(rarity);
                }
            }

            // Defensive fail-fast: loader should guarantee at least one non-empty tier.
            if (normalizationDen <= 0f)
            {
                throw new InvalidOperationException(
                    $"Invalid rarity normalization denominator: {normalizationDen}. poolID={poolID}");
            }

            float roll = Random.value;
            float cumulative = 0f;
            int lastNonEmptyRarity = MIN_RARITY;

            for (int rarity = MIN_RARITY; rarity <= MAX_RARITY; rarity++)
            {
                if (IsTierEmpty(poolID, rarity))
                {
                    continue;
                }

                float effectiveProb = _loader.GetBaseProb(rarity) / normalizationDen;
                if (effectiveProb <= 0f)
                {
                    continue;
                }

                cumulative += effectiveProb;
                lastNonEmptyRarity = rarity;

                if (roll < cumulative)
                {
                    return rarity;
                }
            }

            // Precision fallback if cumulative < 1 due floating-point drift.
            return lastNonEmptyRarity;
        }

        private bool IsTierEmpty(int poolID, int rarity)
        {
            IReadOnlyList<int> eligible = _loader.GetEligibleStaffByRarity(poolID, rarity);
            return eligible == null || eligible.Count == 0;
        }

        private static T WeightedRoll<T>(IReadOnlyList<T> items, Func<T, int> weight)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (weight == null)
            {
                throw new ArgumentNullException(nameof(weight));
            }

            if (items.Count == 0)
            {
                throw new InvalidOperationException("WeightedRoll cannot roll from an empty item list.");
            }

            int totalWeight = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = weight(items[i]);
                if (w > 0)
                {
                    totalWeight += w;
                }
            }

            if (totalWeight <= 0)
            {
                throw new InvalidOperationException("WeightedRoll totalWeight must be > 0.");
            }

            int roll = Random.Range(0, totalWeight);
            bool hasFallback = false;
            T fallbackItem = default(T);

            for (int i = 0; i < items.Count; i++)
            {
                int w = weight(items[i]);
                if (w <= 0)
                {
                    continue;
                }

                fallbackItem = items[i];
                hasFallback = true;

                if (roll < w)
                {
                    return items[i];
                }

                roll -= w;
            }

            return hasFallback ? fallbackItem : items[items.Count - 1];
        }
    }
}
