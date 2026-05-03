// Adventurer Trait System — profession data table
// Implements: design/gdd/adventurer-trait-system.md
// Jam version: Type-2 profession traits only (Type-1 race and Type-3 dynamic are cut)
//
// NOTE: Adventurer.trait stores AdventurerTrait ('討伐' | '採集' | '防護').
// Each profession maps to its primary trait category for roster display.
// Full modifier tables are exported here for use by Mission Dispatch and
// Outcome Resolution — they read by professionId, not by trait category.

import type { MissionType, ProfessionId } from '../types'

// Re-export so callers can import ProfessionId from this module.
export type { ProfessionId }

// ── Types ────────────────────────────────────────────────────────────────────

export interface ProfessionTrait {
  id: ProfessionId
  name: string
  /** Additive success-rate modifier per mission type (percentage points, e.g. +20 = +20%) */
  successRateModifier: Record<MissionType, number>
  /** Additive death-rate modifier per mission type (percentage points, e.g. -15 = -15%) */
  deathRateModifier: Record<MissionType, number>
}

// ── Data table ───────────────────────────────────────────────────────────────
// Source: adventurer-trait-system.md — "成功率修正表" and "死亡率修正表"

export const PROFESSION_TRAITS: Record<ProfessionId, ProfessionTrait> = {
  warrior: {
    id: 'warrior',
    name: '戰士',
    successRateModifier: { 討伐: 20, 護送: 0, 採集: 0, 調查: -15 },
    deathRateModifier:   { 討伐: -15, 護送: -5, 採集: -5, 調查: 0 },
  },
  mage: {
    id: 'mage',
    name: '法師',
    successRateModifier: { 討伐: 0, 護送: 0, 採集: -15, 調查: 20 },
    deathRateModifier:   { 討伐: 10, 護送: 0, 採集: 0, 調查: -10 },
  },
  ranger: {
    id: 'ranger',
    name: '遊俠',
    successRateModifier: { 討伐: 0, 護送: -15, 採集: 20, 調查: 0 },
    deathRateModifier:   { 討伐: -5, 護送: 0, 採集: -15, 調查: -5 },
  },
  scout: {
    id: 'scout',
    name: '斥侯',
    successRateModifier: { 討伐: -15, 護送: 20, 採集: 0, 調查: 20 },
    deathRateModifier:   { 討伐: 5, 護送: -10, 採集: -5, 調查: -10 },
  },
  guardian: {
    id: 'guardian',
    name: '盾衛',
    successRateModifier: { 討伐: 0, 護送: 20, 採集: 0, 調查: -15 },
    deathRateModifier:   { 討伐: -10, 護送: -20, 採集: -5, 調查: -5 },
  },
  healer: {
    id: 'healer',
    name: '治癒師',
    successRateModifier: { 討伐: -15, 護送: 20, 採集: 0, 調查: 0 },
    deathRateModifier:   { 討伐: 5, 護送: -15, 採集: -5, 調查: -5 },
  },
  mercenary: {
    id: 'mercenary',
    name: '傭兵',
    successRateModifier: { 討伐: 0, 護送: 0, 採集: 0, 調查: 0 },
    deathRateModifier:   { 討伐: -5, 護送: -5, 採集: 0, 調查: 0 },
  },
}

export const PROFESSION_IDS: ProfessionId[] = Object.keys(PROFESSION_TRAITS) as ProfessionId[]

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Returns a uniformly random profession ID (each of 7 has 1/7 probability). */
export function getRandomProfessionId(): ProfessionId {
  return PROFESSION_IDS[Math.floor(Math.random() * PROFESSION_IDS.length)]
}

/**
 * Returns the AdventurerTrait category for a given profession ID.
 * Used when constructing an Adventurer whose `trait` field is AdventurerTrait.
 */
export function getRandomProfession(): ProfessionId {
  return getRandomProfessionId()
}
