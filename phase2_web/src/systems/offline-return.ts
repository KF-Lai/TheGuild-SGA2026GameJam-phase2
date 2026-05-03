/**
 * Offline Return Event System — 「信使歸來」
 * Implements: design/gdd-offline-return.md
 *
 * Pure business logic for assembling an OfflineReturnEvent from post-settlement
 * data. No DOM, no UI imports. All numeric constants are tunable by designers.
 *
 * Entry point:
 *   buildOfflineReturnEvent(params) → OfflineReturnEvent
 *
 * Call order in main.ts::startGame():
 *   1. processOfflineProgress(...)  → OfflineProgressResult | null
 *   2. (callbacks fire: handleExpiredMissions populates outcomeState)
 *   3. buildOfflineReturnEvent(result, settledRecords, ...)
 *   4. If bonus active → apply gold, push toast
 *   5. showOfflineReturnOverlay(event)
 */

import type {
  OfflineReturnEvent,
  OfflineResolution,
  Adventurer,
  Mission,
  DispatchRecord,
  NpcMissionResolution,
  NpcActiveMission,
  MissionType,
  Rank,
} from '../types'
import { OFFLINE_THRESHOLD_MS } from './tick'
import type { OfflineProgressResult } from './tick'
import type { SettlementRecord } from './outcome'
import { calcRates } from './dispatch'

// ---------------------------------------------------------------------------
// Constants — designer tuning knobs (gdd-offline-return.md §調整旋鈕)
// ---------------------------------------------------------------------------

/** Maximum offline duration that still qualifies for bonus commission (30 minutes). */
export const BONUS_COMMISSION_WINDOW_MS = 1_800_000

/**
 * Percentage of the guild's normal commission income awarded as bonus.
 * Formula: floor(floor(preCollectedAmount × 0.20) × BONUS_COMMISSION_RATE)
 */
export const BONUS_COMMISSION_RATE = 0.05

// ---------------------------------------------------------------------------
// Name lookup helper type
// ---------------------------------------------------------------------------

interface NameLookup {
  missionName: string
  adventurerName: string
}

// ---------------------------------------------------------------------------
// NPC Autonomous Mission Simulation
// Implements: design/gdd-npc-autonomous-mission.md
// ---------------------------------------------------------------------------

/** Rank ordering used to compare adventurer rank vs mission difficulty. */
const RANK_ORDER: Rank[] = ['F', 'E', 'D', 'C', 'B', 'A', 'S']

const NPC_MISSION_TYPES: MissionType[] = ['討伐', '護送', '採集', '調查']

/**
 * Fraction of offline duration before an idle NPC starts a mission.
 * Models the adventurer not immediately taking a quest the moment the player leaves.
 */
const NPC_START_DELAY_MAX_FRACTION = 0.3

/** NPC guild fee: fraction of baseReward given to the guild on NPC success. */
const NPC_GUILD_FEE_RATE = 0.2

/** Output of simulateNpcMissions — intentionally separate from guild state mutation. */
export interface NpcSimulationResult {
  resolutions: NpcMissionResolution[]
  activeMissions: NpcActiveMission[]
  newDispatchRecords: DispatchRecord[]
}

/**
 * Pure simulation of idle adventurers autonomously taking missions during offline.
 *
 * Does NOT mutate any argument. Caller is responsible for applying results to guild state.
 *
 * Per adventurer:
 *  1. Pick a mission from missionPool where difficulty <= adv.rank (or generate an F-rank fallback).
 *  2. Randomise a start delay within [0, offlineDurationMs * NPC_START_DELAY_MAX_FRACTION].
 *  3. If mission ends before offlineEndTimestamp → State 2 (resolved, goldDelta applied by caller).
 *     Otherwise → State 1 (still running, DispatchRecord returned for caller to push).
 */
export function simulateNpcMissions(
  idleAdventurers: Adventurer[],
  missionPool: Mission[],
  offlineStartTimestamp: number,
  offlineEndTimestamp: number,
  _guildLevel: number,
): NpcSimulationResult {
  const resolutions: NpcMissionResolution[] = []
  const activeMissions: NpcActiveMission[] = []
  const newDispatchRecords: DispatchRecord[] = []

  const offlineDurationMs = offlineEndTimestamp - offlineStartTimestamp

  // Track which missions have been assigned to avoid double-booking
  const assignedMissionIds = new Set<string>()

  for (const adv of idleAdventurers) {
    // Pick a mission whose difficulty is within adventurer's rank
    const advRankIdx = RANK_ORDER.indexOf(adv.rank)
    const eligible = missionPool.filter(
      m => !assignedMissionIds.has(m.id) && RANK_ORDER.indexOf(m.difficulty as Rank) <= advRankIdx,
    )

    let mission: Mission
    if (eligible.length > 0) {
      mission = eligible[Math.floor(Math.random() * eligible.length)]
      assignedMissionIds.add(mission.id)
    } else {
      // Fallback: generate a synthetic F-rank mission
      mission = {
        id: `npc-auto-${adv.id}-${Date.now()}-${Math.random().toString(36).slice(2)}`,
        name: '日常委託',
        type: NPC_MISSION_TYPES[Math.floor(Math.random() * NPC_MISSION_TYPES.length)],
        difficulty: 'F',
        baseReward: 60 + Math.floor(Math.random() * 40),
        duration: 30 + Math.floor(Math.random() * 30), // 30-59 minutes
        description: '普通的日常委託。',
        isTemplate: false,
        tier: 'common',
      }
    }

    // Randomise start time within first 30% of offline period
    const startDelay = Math.floor(Math.random() * offlineDurationMs * NPC_START_DELAY_MAX_FRACTION)
    const missionStartTime = offlineStartTimestamp + startDelay
    const missionEndTime = missionStartTime + mission.duration * 60 * 1000

    const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)

    if (missionEndTime > offlineEndTimestamp) {
      // --- State 1: Mission still running ---
      const remainingMs = missionEndTime - offlineEndTimestamp

      const record: DispatchRecord = {
        id: `npc-dispatch-${adv.id}-${Date.now()}-${Math.random().toString(36).slice(2)}`,
        missionId: mission.id,
        missionName: mission.name,
        adventurerId: adv.id,
        adventurerName: adv.name,
        partyMembers: [{ id: adv.id, name: adv.name, professionId: adv.professionId }],
        activeSynergies: [],
        startTimestamp: missionStartTime,
        endTimestamp: missionEndTime,
        finalSuccessRate,
        finalDeathRate,
        preCollectedAmount: Math.floor(mission.baseReward * NPC_GUILD_FEE_RATE),
        difficulty: mission.difficulty,
      }

      newDispatchRecords.push(record)
      activeMissions.push({ adventurerName: adv.name, missionName: mission.name, remainingMs })
    } else {
      // --- State 2: Mission completed while offline ---
      const succeeded = Math.random() < finalSuccessRate
      const goldDelta = succeeded ? Math.floor(mission.baseReward * NPC_GUILD_FEE_RATE) : 0

      resolutions.push({
        adventurerName: adv.name,
        missionName: mission.name,
        outcome: succeeded ? 'SUCCESS' : 'FAILURE',
        goldDelta,
      })
    }
  }

  return { resolutions, activeMissions, newDispatchRecords }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export interface BuildOfflineReturnEventParams {
  /** Result from processOfflineProgress — must be non-null (caller checks). */
  progressResult: OfflineProgressResult
  /**
   * Settlement records produced by handleExpiredMissions during the offline sweep.
   * Only records resolved in this sweep should be included (filter by resolvedAt).
   */
  settledRecords: SettlementRecord[]
  /**
   * Lookup function to retrieve human-readable names for a dispatch.
   * Decouples offline-return.ts from DispatchRecord array traversal.
   */
  lookupNames: (missionId: string, adventurerId: string) => NameLookup
  /** Idle adventurers at the time the player left (for NPC autonomous mission simulation). */
  idleAdventurers: Adventurer[]
  /** Available mission pool for NPC selection. */
  missionPool: Mission[]
  /** Current guild level (used for future scaling; passed to simulateNpcMissions). */
  guildLevel: number
}

/**
 * Assembles a complete OfflineReturnEvent from post-settlement data.
 *
 * Bonus commission eligibility (gdd-offline-return.md §3):
 *   - At least one SUCCESS or PYRRHIC outcome
 *   - offline >= OFFLINE_THRESHOLD_MS
 *   - bonus is ACTIVE only if offline <= BONUS_COMMISSION_WINDOW_MS
 *
 * Bonus amount formula per success/pyrrhic mission:
 *   floor(floor(preCollectedAmount × 0.20) × BONUS_COMMISSION_RATE)
 *
 * Note: This function is PURE — it does not apply gold or push notifications.
 * The caller (main.ts) is responsible for applying bonusCommissionAmount to
 * ResourceState and pushing the corresponding toast.
 */
export function buildOfflineReturnEvent(
  params: BuildOfflineReturnEventParams,
): OfflineReturnEvent {
  const { progressResult, settledRecords, lookupNames, idleAdventurers, missionPool, guildLevel } = params
  const { offlineStartTimestamp, offlineEndTimestamp, offlineDurationMs, candidatePoolRefreshed } = progressResult

  // --- Build resolutions (without bonus amounts first) ----------------------
  const resolutions: OfflineResolution[] = settledRecords.map((record) => {
    const names = lookupNames(record.missionId, record.adventurerId)
    return {
      missionId:      record.missionId,
      missionName:    names.missionName,
      adventurerName: names.adventurerName,
      outcome:        record.outcome,
      goldDelta:      record.goldDelta,
    }
  })

  // --- Bonus commission eligibility -----------------------------------------
  const hasSuccessfulMission = resolutions.some(
    (r) => r.outcome === 'SUCCESS' || r.outcome === 'PYRRHIC',
  )

  const bonusCommissionEligible =
    hasSuccessfulMission && offlineDurationMs >= OFFLINE_THRESHOLD_MS

  const bonusCommissionActive =
    bonusCommissionEligible && offlineDurationMs <= BONUS_COMMISSION_WINDOW_MS

  // --- Bonus amount calculation (gdd-offline-return.md §公式彙總) -----------
  let bonusCommissionAmount = 0

  if (bonusCommissionActive) {
    for (let i = 0; i < resolutions.length; i++) {
      const resolution = resolutions[i]
      const settled = settledRecords[i]
      if (resolution.outcome === 'SUCCESS' || resolution.outcome === 'PYRRHIC') {
        const normalCommission = Math.floor(settled.preCollectedAmount * 0.20)
        const bonusAmount = Math.floor(normalCommission * BONUS_COMMISSION_RATE)
        resolution.bonusGoldFromBonus = bonusAmount
        bonusCommissionAmount += bonusAmount
      }
    }
  }

  // --- NPC autonomous mission simulation (gdd-npc-autonomous-mission.md) ----
  const npcSim = simulateNpcMissions(
    idleAdventurers,
    missionPool,
    offlineStartTimestamp,
    offlineEndTimestamp,
    guildLevel,
  )

  return {
    eventId:                  crypto.randomUUID(),
    offlineStartTimestamp,
    offlineEndTimestamp,
    offlineDurationMs,
    resolutions,
    candidatePoolRefreshed,
    bonusCommissionEligible,
    bonusCommissionActive,
    bonusCommissionAmount,
    npcResolutions:     npcSim.resolutions,
    npcActiveMissions:  npcSim.activeMissions,
    npcDispatchRecords: npcSim.newDispatchRecords,
  }
}
