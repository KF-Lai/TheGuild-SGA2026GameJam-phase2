/**
 * Growth Trait System — Data & XP Logic
 * Implements: design/gdd/growth-traits.md
 *
 * XP milestones unlock traits that modify calcRates() or provide passive
 * bonuses for future systems. Only 'success_rate' and 'death_rate' trait
 * types are applied in the current dispatch system.
 */

import type { TraitEffect, AdventurerGrowthTrait } from '../types'

// ---------------------------------------------------------------------------
// Trait definitions
// ---------------------------------------------------------------------------

/**
 * All available growth traits, keyed by id.
 * Traits are shared across all adventurers; individual adventurers track
 * which trait ids they have unlocked (see Adventurer.growthTraits).
 */
export const GROWTH_TRAITS: Readonly<Record<string, TraitEffect>> = {
  iron_will: {
    id: 'iron_will',
    name: '鐵意志',
    description: '任務死亡率降低 5%',
    effectType: 'death_rate',
    value: -0.05,
  },
  veteran_instinct: {
    id: 'veteran_instinct',
    name: '老兵直覺',
    description: '任務成功率提升 5%',
    effectType: 'success_rate',
    value: 0.05,
  },
  battle_hardened: {
    id: 'battle_hardened',
    name: '身經百戰',
    description: '任務死亡率再降低 5%',
    effectType: 'death_rate',
    value: -0.05,
  },
  tactical_mastery: {
    id: 'tactical_mastery',
    name: '戰術大師',
    description: '任務成功率再提升 8%',
    effectType: 'success_rate',
    value: 0.08,
  },
  survivor: {
    id: 'survivor',
    name: '生存本能',
    description: '被動：在戰鬥中更難陣亡（未來戰鬥系統）',
    effectType: 'passive',
    value: 0,
  },
} as const

// ---------------------------------------------------------------------------
// XP milestone table
// ---------------------------------------------------------------------------

/**
 * XP thresholds at which traits are unlocked (in ascending order).
 * Each milestone unlocks the trait at the corresponding index.
 * Milestones beyond the table length grant no additional traits.
 */
export const XP_MILESTONES: ReadonlyArray<{ xpRequired: number; traitId: string }> = [
  { xpRequired: 10,  traitId: 'iron_will' },
  { xpRequired: 30,  traitId: 'veteran_instinct' },
  { xpRequired: 70,  traitId: 'battle_hardened' },
  { xpRequired: 150, traitId: 'tactical_mastery' },
  { xpRequired: 300, traitId: 'survivor' },
] as const

// ---------------------------------------------------------------------------
// XP per outcome
// ---------------------------------------------------------------------------

/**
 * XP awarded per mission outcome.
 * DEATH and PYRRHIC award 0 XP — the adventurer does not survive to learn.
 */
export const XP_PER_OUTCOME: Readonly<Record<string, number>> = {
  SUCCESS:  5,
  FAILURE:  2,
  PYRRHIC:  0,
  DEATH:    0,
} as const

// ---------------------------------------------------------------------------
// Public helpers
// ---------------------------------------------------------------------------

/**
 * Returns the total XP required to display in UI (next milestone threshold).
 * Returns Infinity when all milestones have been cleared.
 */
export function nextXPMilestone(currentXP: number): number {
  const next = XP_MILESTONES.find(m => m.xpRequired > currentXP)
  return next ? next.xpRequired : Infinity
}

/**
 * Computes which new traits should be unlocked given old and new XP values.
 * Returns trait ids that crossed a milestone threshold.
 */
export function computeNewTraits(
  oldXP: number,
  newXP: number,
  alreadyUnlocked: ReadonlyArray<AdventurerGrowthTrait>,
): string[] {
  const unlockedIds = new Set(alreadyUnlocked.map(t => t.traitId))
  return XP_MILESTONES
    .filter(m => m.xpRequired > oldXP && m.xpRequired <= newXP && !unlockedIds.has(m.traitId))
    .map(m => m.traitId)
}

/**
 * Returns all TraitEffect objects that are active for a given set of unlocked traits.
 * Only returns traits whose effectType is 'success_rate' or 'death_rate'
 * (passive traits are excluded — they are reserved for future combat systems).
 */
export function getActiveRateEffects(
  growthTraits: ReadonlyArray<AdventurerGrowthTrait>,
): TraitEffect[] {
  return growthTraits
    .map(t => GROWTH_TRAITS[t.traitId])
    .filter((t): t is TraitEffect => !!t && t.effectType !== 'passive')
}
