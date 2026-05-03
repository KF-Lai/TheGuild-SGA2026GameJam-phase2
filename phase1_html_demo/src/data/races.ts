/**
 * Racial Modifiers System — The Guild
 * Implements: design/gdd/racial-modifiers.md (混合模式)
 *
 * Races provide additive modifiers to success_rate and death_rate.
 * All modifiers are applied in calcRates() via clamp() to prevent stacking abuse.
 *
 * Tuning parameters (§調整旋鈕):
 *   - Success rate range: ±8~±15% per race (applied additively)
 *   - Death rate range: ±5~±10% per race (applied additively as percentage points)
 *   - Modifiers clamp final rates to [0, 1] to prevent extreme values
 */

import type { RaceId } from '../types'

/**
 * A racial modifier set.
 * - successRateModifier: additive modifier to finalSuccessRate (e.g., 0.12 = +12%)
 * - deathRateModifier: additive modifier to finalDeathRate (e.g., -0.08 = -8%)
 */
export interface RaceModifier {
  id: RaceId
  name: string
  description: string
  /** Additive success rate modifier (decimal, e.g., 0.12 for +12%). */
  successRateModifier: number
  /** Additive death rate modifier (decimal, e.g., -0.08 for -8%). */
  deathRateModifier: number
}

/**
 * Racial modifier database.
 * All modifiers are normalized to ±8~±15% success, ±5~±10% death.
 * Modifiers apply symmetrically (both positive and negative races available).
 *
 * Design: Mixed Mode (混合模式)
 *   - All races available for recruitment
 *   - Modifiers fully stackable (controlled by clamp in calcRates)
 *   - NPC willingness affected by aggregated rates
 */
export const RACE_MODIFIERS: Record<RaceId, RaceModifier> = {
  human: {
    id: 'human',
    name: '人類',
    description: '適應力強，無特殊加成或減益。',
    successRateModifier: 0.00,
    deathRateModifier: 0.00,
  },
  elf: {
    id: 'elf',
    name: '精靈',
    description: '靈巧敏捷，成功率 +12%，死亡率 +8%（風險高但潛力大）。',
    successRateModifier: 0.12,
    deathRateModifier: 0.08,
  },
  dwarf: {
    id: 'dwarf',
    name: '矮人',
    description: '堅韌耐久，成功率 +8%，死亡率 -10%（穩定可靠）。',
    successRateModifier: 0.08,
    deathRateModifier: -0.10,
  },
  orc: {
    id: 'orc',
    name: '獸人',
    description: '力量強大，成功率 +15%，死亡率 +5%（易為弱者）。',
    successRateModifier: 0.15,
    deathRateModifier: 0.05,
  },
  halfling: {
    id: 'halfling',
    name: '混血人類',
    description: '幸運非凡，成功率 -8%，死亡率 -10%（穩妥但保守）。',
    successRateModifier: -0.08,
    deathRateModifier: -0.10,
  },
  lizardfolk: {
    id: 'lizardfolk',
    name: '黑暗精靈',
    description: '狡詐詭異，成功率 +10%，死亡率 +8%（奇特戰術）。',
    successRateModifier: 0.10,
    deathRateModifier: 0.08,
  },
}

/**
 * Retrieves the racial modifier for a given race ID.
 * @param raceId The race identifier.
 * @returns The racial modifier record.
 */
export function getRaceModifier(raceId: RaceId): RaceModifier {
  return RACE_MODIFIERS[raceId]
}

/**
 * Returns a random race ID with uniform probability.
 * Used during adventurer generation if no specific race is provided.
 */
export function getRandomRace(): RaceId {
  const races = Object.keys(RACE_MODIFIERS) as RaceId[]
  return races[Math.floor(Math.random() * races.length)]
}
