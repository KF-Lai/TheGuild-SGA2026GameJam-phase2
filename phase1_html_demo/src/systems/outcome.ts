/**
 * Outcome Resolution System — The Guild
 * Implements: design/gdd/outcome-resolution.md
 *
 * Resolves dispatched missions into one of four outcomes: SUCCESS, PYRRHIC,
 * FAILURE, or DEATH. Owns the pending results queue (pendingResults) and the
 * base mission death rate table. Does NOT mutate adventurer or resource state
 * directly — callers are responsible for applying goldDelta / reputationDelta
 * and updating adventurer status after receiving a SettlementRecord.
 *
 * Design doc §核心規則:
 *   successRoll = Math.random() < finalSuccessRate
 *   deathRoll   = Math.random() < finalDeathRate
 *   Both rolls are independent; death can trigger on a success (→ PYRRHIC).
 */

import type { Difficulty, DispatchRecord, Adventurer, OutcomeType } from '../types'
import { getReputationDelta } from './resource'
import { XP_PER_OUTCOME, computeNewTraits } from '../data/growth-traits'

// ---------------------------------------------------------------------------
// Re-exports (OutcomeType is defined in ../types to avoid duplication)
// ---------------------------------------------------------------------------

export type { OutcomeType } from '../types'

// ---------------------------------------------------------------------------
// SettlementRecord — contract with Commission Flow (GDD §與其他系統的互動)
// ---------------------------------------------------------------------------

/**
 * Immutable record produced after resolving a DispatchRecord.
 * goldDelta: positive = guild income, negative = guild expenditure.
 * Callers apply goldDelta and reputationDelta to ResourceState.
 */
export interface SettlementRecord {
  /** Maps to DispatchRecord.id */
  dispatchId: string
  missionId: string
  adventurerId: string
  outcome: import('../types').OutcomeType
  missionDifficulty: Difficulty
  baseReward: number
  preCollectedAmount: number
  /** Unix timestamp (ms) when resolution occurred */
  resolvedAt: number
  /** Positive = guild earned, negative = guild paid out */
  goldDelta: number
  reputationDelta: number
}

// ---------------------------------------------------------------------------
// OutcomeState — persistent state layer owned by this system
// ---------------------------------------------------------------------------

/** Persistent state for Outcome Resolution. Serialize for save/load. */
export interface OutcomeState {
  /** Resolved records not yet acknowledged by the player. No upper limit. */
  pendingResults: SettlementRecord[]
}

// ---------------------------------------------------------------------------
// Configuration — all tunable values (GDD §調整旋鈕)
// ---------------------------------------------------------------------------

/**
 * Base death rates per mission difficulty.
 * Source of truth for Mission Dispatch to read when computing finalDeathRate.
 * GDD §baseMissionDeathRate
 */
export const BASE_MISSION_DEATH_RATE: Readonly<Record<Difficulty, number>> = {
  F:   0.02,
  E:   0.06,
  D:   0.10,
  C:   0.13,
  B:   0.18,
  A:   0.25,
  S:   0.30,
  SS:  0.38,
  SSS: 0.50,
} as const

/**
 * Guild's net commission rate on successful missions.
 * GDD §公式 — Commission Flow 的調整旋鈕，此處作為本地常數使用。
 * goldDelta (SUCCESS/PYRRHIC) = +floor(baseReward * COMMISSION_RATE)
 */
const COMMISSION_RATE = 0.20

/**
 * Guild's compensation rate on failed missions.
 * goldDelta (FAILURE/DEATH) = -floor(baseReward * COMPENSATION_RATE)
 */
const COMPENSATION_RATE = 0.28

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Returns a fresh OutcomeState with an empty pending queue. */
export function createOutcomeState(): OutcomeState {
  return { pendingResults: [] }
}

/**
 * Resolves a single DispatchRecord into a SettlementRecord.
 *
 * Rolls two independent dice (successRoll, deathRoll) and maps to outcome:
 *   success && !death → SUCCESS
 *   success &&  death → PYRRHIC
 *  !success && !death → FAILURE
 *  !success &&  death → DEATH
 *
 * GDD §結算流程 steps 2–4.
 *
 * NOTE: This function does NOT mutate adventurer or resource state.
 * The caller must:
 *   - Apply record.goldDelta to ResourceState via addGold / spendGold
 *   - Apply record.reputationDelta via changeReputation
 *   - Set adventurer status to 'idle' (SUCCESS/FAILURE) or remove from roster
 *     (PYRRHIC/DEATH) — GDD §冒險者狀態轉換
 *   - Push the returned record to OutcomeState.pendingResults
 *   - Remove the DispatchRecord from activeMissions
 *
 * @param record   - The DispatchRecord being resolved (from activeMissions)
 * @param difficulty - Mission difficulty (not stored on DispatchRecord in types/index.ts)
 * @param baseReward - Mission base reward; pass 0 if missionId is missing from DB (GDD §極端情況 5)
 */
export function resolveOutcome(
  record: DispatchRecord,
  difficulty: Difficulty,
  baseReward: number,
): SettlementRecord {
  // GDD §成功擲骰 / §死亡擲骰
  const successRoll = Math.random() < record.finalSuccessRate
  const deathRoll   = Math.random() < record.finalDeathRate

  // GDD §4 種結算結果
  let outcome: import('../types').OutcomeType
  if (successRoll && !deathRoll) {
    outcome = 'SUCCESS'
  } else if (successRoll && deathRoll) {
    outcome = 'PYRRHIC'
  } else if (!successRoll && !deathRoll) {
    outcome = 'FAILURE'
  } else {
    outcome = 'DEATH'
  }

  // GDD §公式 — Commission Flow 委託結算扣款計算（預收模型）
  // SUCCESS/PYRRHIC: deduct 80% return amount (guild keeps 20% commission)
  // FAILURE/DEATH:   deduct full refund + 10% penalty
  const isSuccess = outcome === 'SUCCESS' || outcome === 'PYRRHIC'
  const preCollectedAmount = record.preCollectedAmount
  const goldDelta = isSuccess
    ? -Math.floor(preCollectedAmount * (1 - COMMISSION_RATE))
    : -(preCollectedAmount + Math.floor(preCollectedAmount * COMPENSATION_RATE))

  // GDD §聲望更新 — PYRRHIC counts as success, DEATH counts as failure
  const reputationDelta = getReputationDelta(difficulty, isSuccess)

  return {
    dispatchId:         record.id,
    missionId:          record.missionId,
    adventurerId:       record.adventurerId,
    outcome,
    missionDifficulty:  difficulty,
    baseReward,
    preCollectedAmount: record.preCollectedAmount,
    resolvedAt:         Date.now(),
    goldDelta,
    reputationDelta,
  }
}

/**
 * Removes a single settlement record from the pending queue.
 * Called by UI after the player acknowledges the result.
 * GDD §未讀結算清單
 */
export function dismissResult(state: OutcomeState, dispatchId: string): void {
  const idx = state.pendingResults.findIndex(r => r.dispatchId === dispatchId)
  if (idx !== -1) {
    state.pendingResults.splice(idx, 1)
  }
}

/**
 * Clears all pending settlement records at once.
 * Corresponds to the "全部確認" button in GDD §UI 需求.
 */
export function dismissAll(state: OutcomeState): void {
  state.pendingResults.length = 0
}

// ---------------------------------------------------------------------------
// XP & Growth Trait Application — GDD §冒險者成長
// ---------------------------------------------------------------------------

/**
 * Result of applying an XP gain to an adventurer.
 * Pure function — caller is responsible for mutating the adventurer.
 */
export interface XPGainResult {
  /** XP delta that was awarded. */
  xpGained: number
  /** New XP total after gain. */
  newXP: number
  /** Trait ids newly unlocked by this XP gain (may be empty). */
  newTraitIds: string[]
}

/**
 * Computes the XP gain for a given outcome and returns new XP + unlocked traits.
 *
 * Pure function — does NOT mutate the adventurer. The caller must apply:
 *   adv.xp = result.newXP
 *   result.newTraitIds.forEach(id => adv.growthTraits.push({ traitId: id, unlockedAt: Date.now() }))
 *
 * DEATH and PYRRHIC return xpGained = 0 (adventurer did not survive).
 *
 * @param adv     - The adventurer receiving XP (read-only usage).
 * @param outcome - The mission outcome type.
 * @returns XPGainResult with xpGained, newXP, and newTraitIds.
 */
export function applyXPGain(
  adv: Readonly<Adventurer>,
  outcome: OutcomeType,
): XPGainResult {
  const xpGained = XP_PER_OUTCOME[outcome] ?? 0
  const oldXP    = adv.xp
  const newXP    = oldXP + xpGained

  const newTraitIds = xpGained > 0
    ? computeNewTraits(oldXP, newXP, adv.growthTraits)
    : []

  return { xpGained, newXP, newTraitIds }
}
