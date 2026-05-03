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
import { eventBus } from '../core/events'
import { COMMISSION_RATE, PENALTY_RATE, WOUNDED_RECOVERY_HOURS } from '../data/constants'

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
  missionName: string
  adventurerId: string
  adventurerName: string
  outcome: import('../types').OutcomeType
  missionDifficulty: Difficulty
  baseReward: number
  preCollectedAmount: number
  /** Unix timestamp (ms) when resolution occurred */
  resolvedAt: number
  /** Positive = guild earned, negative = guild paid out */
  goldDelta: number
  reputationDelta: number
  /** Unix timestamp (ms) until which the adventurer is wounded. Only set when outcome = FAILURE and wounded roll succeeds. */
  woundedUntil?: number
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

// COMMISSION_RATE and PENALTY_RATE imported from '../data/constants'

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

  // GDD §公式 — 直接模型：dispatch 不預收，結算時直接計算淨額
  // SUCCESS/PYRRHIC: guild 賺取 COMMISSION_RATE 傭金（正值）
  // FAILURE/DEATH:   guild 支付 PENALTY_RATE 賠償（負值）
  const isSuccess = outcome === 'SUCCESS' || outcome === 'PYRRHIC'
  const preCollectedAmount = record.preCollectedAmount
  const goldDelta = isSuccess
    ? Math.floor(preCollectedAmount * COMMISSION_RATE)
    : -Math.floor(preCollectedAmount * PENALTY_RATE)

  // GDD §聲望更新 — PYRRHIC counts as success, DEATH counts as failure
  const reputationDelta = getReputationDelta(difficulty, isSuccess)

  // Phase 2: FAILURE 有 30% 機率進入 wounded 狀態
  const woundedUntil = (outcome === 'FAILURE' && Math.random() < 0.3)
    ? Date.now() + WOUNDED_RECOVERY_HOURS * 3_600_000
    : undefined

  const settlementRecord: SettlementRecord = {
    dispatchId:         record.id,
    missionId:          record.missionId,
    missionName:        record.missionName,
    adventurerId:       record.adventurerId,
    adventurerName:     record.adventurerName,
    outcome,
    missionDifficulty:  difficulty,
    baseReward,
    preCollectedAmount: record.preCollectedAmount,
    resolvedAt:         Date.now(),
    goldDelta,
    reputationDelta,
    ...(woundedUntil !== undefined && { woundedUntil }),
  }

  eventBus.emit('mission:completed', {
    dispatchId:       record.id,
    outcome,
    goldDelta,
    reputationDelta,
  })

  if (outcome === 'DEATH' || outcome === 'PYRRHIC') {
    eventBus.emit('adventurer:died', {
      adventurerId:   record.adventurerId,
      adventurerName: record.adventurerName,
      missionId:      record.missionId,
    })
  }

  if (woundedUntil !== undefined) {
    eventBus.emit('adventurer:wounded', {
      adventurerId: record.adventurerId,
      woundedUntil,
    })
  }

  return settlementRecord
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
