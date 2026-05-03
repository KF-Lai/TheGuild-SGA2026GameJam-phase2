// Adventurer Management System
// Implements: design/gdd/adventurer-management.md
//
// Responsibilities:
//   - Adventurer generation (name, rank, trait)
//   - Starting roster (3 adventurers, F~E rank)
//   - Candidate pool generation (batch of 4, novice/veteran split)
//   - Recruit cost calculation
//   - Adventurer lifecycle state mutations (idle / on_mission / dead)
//
// This module is pure data — it does NOT touch ResourceState.
// The caller (UI or game loop) must check gold and call resource system
// separately before recruiting a veteran.

import type { Adventurer, Rank, RaceId } from '../types'
import { getRandomProfessionId, type ProfessionId } from '../data/traits'
import { getRandomRace } from '../data/races'
import { generateBio } from '../data/bios'

// Re-export ProfessionId so callers can import it from this module if needed.
export type { ProfessionId }

// ── Configuration (all tunable values in one place) ──────────────────────────
// Tuning knobs listed in adventurer-management.md § 調整旋鈕

/** Maximum roster size. Exported so callers (e.g. main.ts) share the same constant. */
export const ROSTER_CAP = 15

const CONFIG = {
  /** Maximum roster size. */
  ROSTER_CAP,

  /** Number of starting adventurers given to the player. */
  STARTING_COUNT: 3,

  /** Fixed candidate pool batch size per refresh. */
  BATCH_SIZE: 4,

  /** Refresh interval in seconds (12 hours). */
  REFRESH_INTERVAL_SECONDS: 43_200,

  /** Novice ratio drawn uniformly from [NOVICE_RATIO_MIN, NOVICE_RATIO_MAX]. */
  NOVICE_RATIO_MIN: 0.40,
  NOVICE_RATIO_MAX: 0.60,

  /**
   * Weighted distribution for veteran rank selection.
   * Weights sum to 1.00.
   */
  VETERAN_RANK_WEIGHTS: [
    { rank: 'D' as Rank, weight: 0.40 },
    { rank: 'C' as Rank, weight: 0.30 },
    { rank: 'B' as Rank, weight: 0.18 },
    { rank: 'A' as Rank, weight: 0.09 },
    { rank: 'S' as Rank, weight: 0.03 },
  ],

  /**
   * Base reward reference values used to derive invitation cost.
   * Formula: invitationCost = round(BASE_REWARD[rank] × COST_FACTOR × U(COST_JITTER_MIN, COST_JITTER_MAX))
   * Derived from GDD cost midpoints: midpoint = BASE_REWARD × 0.5
   * D:60, C:150, B:300, A:500, S:1000 → BASE_REWARD = midpoint / 0.5
   */
  BASE_REWARD: {
    D: 120,
    C: 300,
    B: 600,
    A: 1_000,
    S: 2_000,
  } as Partial<Record<Rank, number>>,

  /** Fraction of BASE_REWARD used as invitation cost base. */
  COST_FACTOR: 0.5,

  /** Uniform jitter bounds applied to invitation cost. */
  COST_JITTER_MIN: 0.85,
  COST_JITTER_MAX: 1.15,
}

// ── Name pool ────────────────────────────────────────────────────────────────
// Chinese fantasy-style names (≥ 20 entries, per spec).
// Allows duplicates at runtime — `id` (UUID) is the unique key (GDD § 極端情況 7).

const NAME_POOL: string[] = [
  // Male names
'奧德里奇',  '布倫南',    '凱蘭',      '多里安',    '艾德文',
'費隆',      '加雷斯',    '哈德溫',    '伊瓦爾',    '賈雷斯',
'凱蘭',      '雷奧里克',  '馬雷克',    '諾蘭',      '奧斯溫',
'帕西瓦爾',  '昆圖斯',    '羅文',      '賽伯爾',    '塞隆',
'烏爾里克',  '凡斯',      '沃夫里克',  '山德',      '約里克',
'澤費爾',    '阿拉里克',  '巴斯蒂安',  '塞德里克',  '達文',
'艾德里克',  '弗洛里安',  '吉德翁',    '亨德里克',  '伊薩克',
'喬林',      '卡斯帕',    '盧西安',    '馬里烏斯',  '內文',
'奧林',      '帕克森',    '倫威克',    '斯特蘭',    '塔昆',
'尤瑟',      '維吉爾',    '威斯頓',    '賽蘭',      '贊恩',
// Female names
'塞拉菲娜',  '萊拉',      '艾琳',      '布琳',      '卡莉絲塔',
'丹妮拉',    '艾拉娜',    '菲奧娜',    '葛妮絲',    '赫莉亞',
'伊索德',    '潔絲敏',    '凱拉',      '洛倫娜',    '米拉',
'妮亞',      '奧莉莉亞',  '佩特拉',    '羅薇娜',    '希爾薇',
'塞薩莉',    '烏拉拉',    '薇樂莉雅',  '雷恩',      '賽拉',
'伊索德',    '扎拉',      '艾莉絲',    '布里賽絲',  '辛德拉',
'黛拉拉',    '伊凡',      '菲',        '葛拉蒂亞',  '哈露溫',
'伊瑪拉',    '茱妮拉',    '凱莉絲',    '莉莉爾',    '梅薇絲',
'尼瑞莎',    '昂丁',      '費德拉',    '昆娜拉',    '蘿絲溫',
'瑟夏',      '塔莉亞',    '伍瑪拉',    '薇薇安',    '溫妮',
]

// ── Internal utilities ────────────────────────────────────────────────────────

function randomName(): string {
  return NAME_POOL[Math.floor(Math.random() * NAME_POOL.length)]
}

/**
 * Generates a UUID v4-compatible string without external dependencies.
 * Sufficient for Jam-scope uniqueness requirements.
 */
function generateId(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

/** Returns a float uniformly distributed in [min, max). */
function uniformRandom(min: number, max: number): number {
  return Math.random() * (max - min) + min
}

/**
 * Selects a rank using a weighted random table.
 * Weights do not need to be normalised — selection is relative.
 */
function weightedRankRandom(
  table: Array<{ rank: Rank; weight: number }>,
): Rank {
  const total = table.reduce((sum, entry) => sum + entry.weight, 0)
  let roll = Math.random() * total
  for (const entry of table) {
    roll -= entry.weight
    if (roll <= 0) return entry.rank
  }
  // Fallback to last entry (guards against floating-point overshoot).
  return table[table.length - 1].rank
}

/** Picks a random novice rank: F or E with equal probability. */
function randomNoviceRank(): Rank {
  return Math.random() < 0.5 ? 'F' : 'E'
}

/** Picks a random veteran rank using the configured weighted distribution. */
function randomVeteranRank(): Rank {
  return weightedRankRandom(CONFIG.VETERAN_RANK_WEIGHTS)
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Creates a single adventurer.
 *
 * @param rank - If provided, overrides random rank selection.
 *               If omitted, picks a novice rank (F or E).
 * @param raceId - If provided, uses specified race; otherwise picks at random.
 */
export function createAdventurer(rank?: Rank, raceId?: RaceId): Adventurer {
  const resolvedRank = rank ?? randomNoviceRank()
  const professionId = getRandomProfessionId()
  return {
    id: generateId(),
    name: randomName(),
    rank: resolvedRank,
    professionId,
    raceId: raceId ?? getRandomRace(),
    status: 'idle',
    currentMissionId: null,
    xp: 0,
    growthTraits: [],
    bio: generateBio(professionId, resolvedRank),
    gender: Math.random() < 0.5 ? 0 : 1,
  }
}

/**
 * Creates the initial roster of 3 adventurers at F or E rank.
 * Implements: adventurer-management.md § "起始冒險者：3 名（F~E 階）"
 */
export function createStartingAdventurers(): Adventurer[] {
  const adventurers: Adventurer[] = []
  for (let i = 0; i < CONFIG.STARTING_COUNT; i++) {
    adventurers.push(createAdventurer(randomNoviceRank()))
  }
  return adventurers
}

/**
 * Generates a fresh candidate pool for the recruitment board.
 *
 * Batch size is fixed at 4. Novice count is determined by a uniform
 * random ratio in [40%, 60%]; remainder are veterans with weighted
 * rank distribution.
 *
 * @param guildLevel - Current guild level (accepted for future scaling;
 *                     Jam version uses fixed weights regardless of level).
 */
export function generateCandidatePool(guildLevel: number): Adventurer[] {
  // guildLevel is reserved for post-Jam scaling of veteran rank weights.
  void guildLevel

  const noviceRatio = uniformRandom(CONFIG.NOVICE_RATIO_MIN, CONFIG.NOVICE_RATIO_MAX)
  const noviceCount = Math.round(CONFIG.BATCH_SIZE * noviceRatio)
  const veteranCount = CONFIG.BATCH_SIZE - noviceCount

  const pool: Adventurer[] = []

  for (let i = 0; i < noviceCount; i++) {
    const adv = createAdventurer(randomNoviceRank())
    adv.fixedRecruitCost = recruitCost(adv.rank)
    pool.push(adv)
  }
  for (let i = 0; i < veteranCount; i++) {
    const adv = createAdventurer(randomVeteranRank())
    adv.fixedRecruitCost = recruitCost(adv.rank)
    pool.push(adv)
  }

  return pool
}

/**
 * Calculates the gold cost to recruit a veteran adventurer of the given rank.
 *
 * Formula (GDD § 公式 — 老手邀請費用):
 *   invitationCost = round(BASE_REWARD[rank] × COST_FACTOR × U(0.85, 1.15))
 *
 * Returns 0 for novice ranks (F, E) — novices are free to accept.
 *
 * @param rank - The adventurer's rank.
 * @returns Gold cost as a non-negative integer.
 */
export function recruitCost(rank: Rank): number {
  const base = CONFIG.BASE_REWARD[rank]
  if (base === undefined) return 0 // F and E are free (novice)
  const jitter = uniformRandom(CONFIG.COST_JITTER_MIN, CONFIG.COST_JITTER_MAX)
  return Math.round(base * CONFIG.COST_FACTOR * jitter)
}

/**
 * Transitions an adventurer to `on_mission` state.
 * Called by Mission Dispatch when an adventurer is assigned to a mission.
 *
 * Mutates the adventurer in place (consistent with the rest of the
 * engine's pattern of direct state mutation on plain objects).
 */
export function markOnMission(adv: Adventurer, missionId: string): void {
  adv.status = 'on_mission'
  adv.currentMissionId = missionId
}

/**
 * Transitions an adventurer back to `idle` state after mission completion.
 * Called by Outcome Resolution on SUCCESS, FAILURE (survived), or PYRRHIC outcomes.
 */
export function markIdle(adv: Adventurer): void {
  adv.status = 'idle'
  adv.currentMissionId = null
}

/**
 * Marks an adventurer as dead.
 * The caller (Outcome Resolution) is responsible for removing the dead
 * adventurer from GuildState.adventurers immediately after this call.
 *
 * GDD § 極端情況 5: removal happens in the same tick as resolution.
 */
export function markDead(adv: Adventurer): void {
  adv.status = 'dead'
  adv.currentMissionId = null
}

/**
 * 排序名冊：isUnique=true 的冒險者永遠排在第一位。
 * Implements: C-02 Adventurer Management § unique 角色排序
 */
export function sortRoster(adventurers: Adventurer[]): Adventurer[] {
  return [...adventurers].sort((a, b) => {
    if (a.isUnique && !b.isUnique) return -1
    if (!a.isUnique && b.isUnique) return 1
    return 0
  })
}

/**
 * 判斷 wounded 冒險者是否已恢復（woundedUntil 為恢復時間戳記 ms）。
 * Implements: C-02 § wounded 狀態 / WOUNDED_RECOVERY_HOURS=6
 *
 * @param woundedUntil - 恢復完成的 Unix 毫秒時間戳記
 */
export function isWoundedRecovered(woundedUntil: number): boolean {
  return Date.now() >= woundedUntil
}
