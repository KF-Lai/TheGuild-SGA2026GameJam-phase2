/**
 * Resource Management System — The Guild
 * Implements: design/gdd/resource-management.md
 *
 * Manages the guild's two core resources: gold and reputation.
 * Enforces legal value ranges and converts reputation to display labels.
 * This module owns no game logic beyond resource state rules.
 */

import type { Difficulty, ResourceState } from '../types'

// ---------------------------------------------------------------------------
// Constants — all tunable values live here (see GDD §調整旋鈕)
// ---------------------------------------------------------------------------

/** Reputation label thresholds, evaluated high-to-low. */
const REPUTATION_LABELS: ReadonlyArray<{ readonly min: number; readonly label: string }> = [
  { min: 81,   label: '名震四方' },
  { min: 61,   label: '名聲遠播' },
  { min: 41,   label: '聲望漸隆' },
  { min: 21,   label: '初露鋒芒' },
  { min: 0,    label: '默默無名' },
  { min: -10,  label: '名聲不佳' },
  { min: -30,  label: '惡評漸起' },
  { min: -55,  label: '惡名外傳' },
  { min: -80,  label: '聲名狼藉' },
] as const

/** Fallback label for the bottom of the reputation range (-81 ~ -100). */
const REPUTATION_BOTTOM_LABEL = '臭名昭著'

/** Reputation deltas per mission difficulty (success = positive, failure = negative). */
const REPUTATION_DELTA: Readonly<Record<Difficulty, { readonly success: number; readonly failure: number }>> = {
  F:   { success: +1,  failure: -8  },
  E:   { success: +1,  failure: -7  },
  D:   { success: +3,  failure: -5  },
  C:   { success: +4,  failure: -4  },
  B:   { success: +5,  failure: -3  },
  A:   { success: +10, failure: -2  },
  S:   { success: +13, failure: -1  },
  SS:  { success: +16, failure: -5  },
  SSS: { success: +20, failure: -10 },
}

const REPUTATION_MIN = -100
const REPUTATION_MAX = 100

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Returns a fresh ResourceState with gold = 0 and reputation = 0. */
export function createResourceState(): ResourceState {
  return { gold: 0, reputation: 0 }
}

/** Adds the given amount to gold. Amount may be negative (e.g., Commission Flow penalty payments). */
export function addGold(state: ResourceState, amount: number): void {
  state.gold += amount
}

/**
 * Deducts the given amount from gold.
 * Returns true on success, false if the balance is insufficient (state unchanged).
 */
export function spendGold(state: ResourceState, amount: number): boolean {
  if (state.gold < amount) return false
  state.gold -= amount
  return true
}

/** Applies a reputation delta, clamping the result to [-100, 100]. */
export function changeReputation(state: ResourceState, delta: number): void {
  state.reputation = Math.max(REPUTATION_MIN, Math.min(REPUTATION_MAX, state.reputation + delta))
}

/** Maps a raw reputation score to the corresponding display label. */
export function getReputationLabel(reputation: number): string {
  for (const { min, label } of REPUTATION_LABELS) {
    if (reputation >= min) return label
  }
  return REPUTATION_BOTTOM_LABEL
}

/** Returns the reputation delta for a completed mission given its difficulty and outcome. */
export function getReputationDelta(difficulty: Difficulty, success: boolean): number {
  const entry = REPUTATION_DELTA[difficulty]
  return success ? entry.success : entry.failure
}
