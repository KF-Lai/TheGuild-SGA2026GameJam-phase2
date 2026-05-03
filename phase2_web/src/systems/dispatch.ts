/**
 * Mission Dispatch System
 *
 * Implements: design/gdd/mission-dispatch.md
 * Pillar: 支柱 3 — 決策有真實後果
 *
 * Responsibilities:
 *  - calcRates: compute finalSuccessRate and finalDeathRate for an adventurer/mission pair
 *  - tryDispatch: run NPC willingness check and, if accepted, produce a DispatchRecord
 *
 * NOTE: clamp range for rankDiff is [−3, +3] per implementation spec
 * (GDD prose lists −4 as lower bound; spec instruction takes precedence).
 */

import type { Adventurer, DispatchRecord, Mission, PartyMember } from '../types'
import { PROFESSION_TRAITS } from '../data/traits'
import { getActiveRateEffects } from '../data/growth-traits'
import { getRaceModifier } from '../data/races'
import { getMaxMissions } from './guild'
import { BASE_MISSION_DEATH_RATE } from './outcome'

// ── Rank / Difficulty index tables ───────────────────────────────────────────

const RANKS = ['F', 'E', 'D', 'C', 'B', 'A', 'S'] as const
const DIFFICULTIES = ['F', 'E', 'D', 'C', 'B', 'A', 'S', 'SS', 'SSS'] as const

function rankIndex(rank: typeof RANKS[number]): number {
  return RANKS.indexOf(rank)
}

function diffIndex(difficulty: typeof DIFFICULTIES[number]): number {
  return DIFFICULTIES.indexOf(difficulty)
}

// ── Config tables ─────────────────────────────────────────────────────────────
// Source: mission-dispatch.md §公式

/**
 * BASE_SUCCESS_RATE keyed by rankDiff (+3 … −3).
 * Index 0 = rankDiff +3, index 6 = rankDiff −3.
 */
const BASE_SUCCESS_RATE: Record<number, number> = {
  3:  0.95,
  2:  0.85,
  1:  0.70,
  0:  0.55,
  '-1': 0.35,
  '-2': 0.20,
  '-3': 0.10,
}

/**
 * RANK_DIFF_DEATH_MOD keyed by rankDiff (+3 … −3).
 * Values are additive percentage points expressed as decimals.
 */
const RANK_DIFF_DEATH_MOD: Record<number, number> = {
  3:   -0.20,
  2:   -0.10,
  1:   -0.05,
  0:    0.00,
  '-1': 0.05,
  '-2': 0.15,
  '-3': 0.25,
}

// ── NPC willingness constants ─────────────────────────────────────────────────
// Source: mission-dispatch.md §NPC 意願常數

const ACCEPTANCE_THRESHOLD = 0.25
const DEATH_AVERSION       = 0.5
const ACCEPTANCE_JITTER    = 0.10

// ── Helpers ───────────────────────────────────────────────────────────────────

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

/** Returns a uniformly distributed random number in [min, max). */
function randBetween(min: number, max: number): number {
  return min + Math.random() * (max - min)
}

// ── Public types ──────────────────────────────────────────────────────────────

export type DispatchResult =
  | { accepted: true;  record: DispatchRecord }
  | { accepted: false; reason: 'willingness' | 'capacity' | 'rank_limit' }

// ── Core API ──────────────────────────────────────────────────────────────────

/**
 * Calculate finalSuccessRate and finalDeathRate for a given adventurer / mission pair.
 *
 * Formula source: mission-dispatch.md §公式
 *
 * rankDiff = clamp(rankIndex(adv) − diffIndex(mission), −3, +3)
 * finalSuccessRate = clamp(BASE_SUCCESS_RATE[rankDiff] + professionMod + raceMod + growthTraitMod, 0, 1)
 * finalDeathRate   = clamp(BASE_MISSION_DEATH_RATE[diff] + professionDeathMod + raceDeathMod + rankDiffDeathMod + growthTraitDeathMod, 0, 1)
 *
 * Race modifiers apply additively (混合模式):
 *   - All modifiers from profession, race, and growth traits stack together
 *   - Final clamp to [0, 1] prevents stacking abuse
 */
export function calcRates(
  adv: Adventurer,
  mission: Mission,
): { finalSuccessRate: number; finalDeathRate: number; rankDiff: number } {
  const rd = clamp(rankIndex(adv.rank) - diffIndex(mission.difficulty), -3, 3)

  const trait = PROFESSION_TRAITS[adv.professionId]
  const successMod = trait.successRateModifier[mission.type] / 100
  const deathMod   = trait.deathRateModifier[mission.type]   / 100

  // Race modifiers (additive)
  const raceModifier = getRaceModifier(adv.raceId ?? 'human')
  const raceSuccessMod = raceModifier.successRateModifier
  const raceDeathMod = raceModifier.deathRateModifier

  // Growth trait modifiers — only 'success_rate' and 'death_rate' types applied here.
  const traitEffects = getActiveRateEffects(adv.growthTraits ?? [])
  let traitSuccessMod = 0
  let traitDeathMod   = 0
  for (const effect of traitEffects) {
    if (effect.effectType === 'success_rate') traitSuccessMod += effect.value
    if (effect.effectType === 'death_rate')   traitDeathMod   += effect.value
  }

  const baseSuccess = BASE_SUCCESS_RATE[rd]
  const finalSuccessRate = clamp(baseSuccess + successMod + raceSuccessMod + traitSuccessMod, 0, 1)

  const baseDeathRate   = BASE_MISSION_DEATH_RATE[mission.difficulty]
  const rankDeathMod    = RANK_DIFF_DEATH_MOD[rd]
  const finalDeathRate  = clamp(baseDeathRate + deathMod + raceDeathMod + rankDeathMod + traitDeathMod, 0, 1)

  return { finalSuccessRate, finalDeathRate, rankDiff: rd }
}

/**
 * Attempt to dispatch an adventurer on a mission.
 *
 * Rejection reasons (in priority order):
 *  1. 'capacity'    — activeMissions already at guild max
 *  2. 'rank_limit'  — adventurer rank is lower than mission difficulty (rankDiff < 0 guard;
 *                     kept soft — only hard-blocks if designer enables; currently no hard block
 *                     is specified in the GDD, willingness check serves as natural gate)
 *  3. 'willingness' — effectiveScore < ACCEPTANCE_THRESHOLD
 *
 * On acceptance, mutates adv (status + currentMissionId) and returns a DispatchRecord.
 *
 * Source: mission-dispatch.md §接單決策
 */
export function tryDispatch(
  adv: Adventurer,
  mission: Mission,
  activeMissions: DispatchRecord[],
  guildLevel: number,
  options?: { forceAccept?: boolean },
): DispatchResult {
  // Guard: capacity
  if (activeMissions.length >= getMaxMissions(guildLevel)) {
    return { accepted: false, reason: 'capacity' }
  }

  const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)

  if (!options?.forceAccept) {
    // Willingness check
    const willingnessScore = finalSuccessRate - finalDeathRate * DEATH_AVERSION
    const jitter           = randBetween(-ACCEPTANCE_JITTER, ACCEPTANCE_JITTER)
    const effectiveScore   = willingnessScore + jitter

    if (effectiveScore < ACCEPTANCE_THRESHOLD) {
      return { accepted: false, reason: 'willingness' }
    }
  }

  // Build DispatchRecord
  const startTimestamp = Date.now()
  const endTimestamp   = startTimestamp + mission.duration * 60 * 1000

  const record: DispatchRecord = {
    id: (typeof crypto !== 'undefined' && crypto.randomUUID)
      ? crypto.randomUUID()
      : Date.now().toString(),
    missionId:           mission.id,
    missionName:         mission.name,
    adventurerId:        adv.id,
    adventurerName:      adv.name,
    partyMembers:        [{ id: adv.id, name: adv.name, professionId: adv.professionId }],
    activeSynergies:     [],
    startTimestamp,
    endTimestamp,
    finalSuccessRate,
    finalDeathRate,
    preCollectedAmount:  mission.baseReward,
  }

  // Mutate adventurer state (idle → on_mission)
  adv.status           = 'on_mission'
  adv.currentMissionId = record.id

  return { accepted: true, record }
}

// ── Party System (隊伍系統) ────────────────────────────────────────────────────

/** Max party size allowed by mission difficulty. */
export function maxPartyByDifficulty(difficulty: string): number {
  if (['F', 'E'].includes(difficulty)) return 1
  if (['D', 'C'].includes(difficulty)) return 2
  if (['B', 'A'].includes(difficulty)) return 3
  return 4 // S, SS, SSS
}

/** Max party size allowed by guild level. */
export function maxPartyByGuildLevel(guildLevel: number): number {
  return Math.min(4, Math.max(1, guildLevel))
}

/** Effective max party size = min(difficulty cap, guild level cap). */
export function maxPartySize(difficulty: string, guildLevel: number): number {
  return Math.min(maxPartyByDifficulty(difficulty), maxPartyByGuildLevel(guildLevel))
}

// Synergy detection
interface SynergyResult {
  successBonus: number   // additive, e.g. 0.15
  deathMod:     number   // multiplicative on death chance, e.g. 0.90
  rewardMult:   number   // multiplicative on reward, e.g. 1.20
  labels:       string[] // display names
}

function detectSynergies(members: Adventurer[], mission: Mission): SynergyResult {
  const result: SynergyResult = { successBonus: 0, deathMod: 1.0, rewardMult: 1.0, labels: [] }
  const profs = new Set(members.map(m => m.professionId))

  // Warrior + Healer → +15% success
  if (profs.has('warrior') && profs.has('healer')) {
    result.successBonus += 0.15
    result.labels.push('戰士+治療師')
  }
  // Scout + Ranger → +20% reward
  if (profs.has('scout') && profs.has('ranger')) {
    result.rewardMult *= 1.20
    result.labels.push('斥候+遊俠')
  }
  // Mage + any → +10% success on 討伐 missions
  if (profs.has('mage') && members.length >= 2 && mission.type === '討伐') {
    result.successBonus += 0.10
    result.labels.push('法師討伐')
  }
  // Guardian + any → death chance ×0.90
  if (profs.has('guardian') && members.length >= 2) {
    result.deathMod *= 0.90
    result.labels.push('守護者庇護')
  }

  return result
}

/**
 * Calculate party-level success/death rates.
 * Uses weighted average: highest member = 50%, rest = 50%.
 */
export function calcPartyRates(
  members: Adventurer[],
  mission: Mission,
): { finalSuccessRate: number; finalDeathRate: number; activeSynergies: string[]; rewardMult: number } {
  if (members.length === 0) return { finalSuccessRate: 0, finalDeathRate: 1, activeSynergies: [], rewardMult: 1 }
  if (members.length === 1) {
    const solo = calcRates(members[0], mission)
    return { finalSuccessRate: solo.finalSuccessRate, finalDeathRate: solo.finalDeathRate, activeSynergies: [], rewardMult: 1 }
  }

  const rates = members.map(m => calcRates(m, mission))
  const maxIdx = rates.reduce((best, r, i) => r.finalSuccessRate > rates[best].finalSuccessRate ? i : best, 0)
  const others = rates.filter((_, i) => i !== maxIdx)
  const othersAvg = others.length > 0
    ? others.reduce((s, r) => s + r.finalSuccessRate, 0) / others.length
    : rates[maxIdx].finalSuccessRate

  const synergy = detectSynergies(members, mission)
  const baseSuccess = 0.50 * rates[maxIdx].finalSuccessRate + 0.50 * othersAvg
  const finalSuccessRate = clamp(baseSuccess + synergy.successBonus, 0, 1)

  // Death rate: average across members, then apply guardian mod
  const avgDeath = rates.reduce((s, r) => s + r.finalDeathRate, 0) / rates.length
  const finalDeathRate = clamp(avgDeath * synergy.deathMod, 0, 1)

  return { finalSuccessRate, finalDeathRate, activeSynergies: synergy.labels, rewardMult: synergy.rewardMult }
}

export type PartyDispatchResult =
  | { accepted: true;  record: DispatchRecord }
  | { accepted: false; reason: 'willingness' | 'capacity' | 'party_too_large' | 'members_busy' }

/**
 * Dispatch a party (1–4 adventurers) on a mission.
 * First member in array is treated as lead.
 */
export function tryPartyDispatch(
  members: Adventurer[],
  mission: Mission,
  activeMissions: DispatchRecord[],
  guildLevel: number,
  options?: { forceAccept?: boolean },
): PartyDispatchResult {
  if (activeMissions.length >= getMaxMissions(guildLevel)) {
    return { accepted: false, reason: 'capacity' }
  }
  const cap = maxPartySize(mission.difficulty, guildLevel)
  if (members.length > cap) {
    return { accepted: false, reason: 'party_too_large' }
  }
  if (members.some(m => m.status !== 'idle')) {
    return { accepted: false, reason: 'members_busy' }
  }

  const { finalSuccessRate, finalDeathRate, activeSynergies, rewardMult } = calcPartyRates(members, mission)

  if (!options?.forceAccept) {
    const willingnessScore = finalSuccessRate - finalDeathRate * DEATH_AVERSION
    const jitter = randBetween(-ACCEPTANCE_JITTER, ACCEPTANCE_JITTER)
    if (willingnessScore + jitter < ACCEPTANCE_THRESHOLD) {
      return { accepted: false, reason: 'willingness' }
    }
  }

  const lead = members[0]
  const startTimestamp = Date.now()
  const endTimestamp   = startTimestamp + mission.duration * 60 * 1000
  const adjustedReward = Math.floor(mission.baseReward * rewardMult)

  const partyMembersList: PartyMember[] = members.map(m => ({
    id: m.id, name: m.name, professionId: m.professionId,
  }))

  const record: DispatchRecord = {
    id: (typeof crypto !== 'undefined' && crypto.randomUUID)
      ? crypto.randomUUID()
      : Date.now().toString(),
    missionId:          mission.id,
    missionName:        mission.name,
    adventurerId:       lead.id,
    adventurerName:     lead.name,
    partyMembers:       partyMembersList,
    activeSynergies,
    startTimestamp,
    endTimestamp,
    finalSuccessRate,
    finalDeathRate,
    preCollectedAmount: adjustedReward,
  }

  members.forEach(m => {
    m.status = 'on_mission'
    m.currentMissionId = record.id
  })

  return { accepted: true, record }
}
