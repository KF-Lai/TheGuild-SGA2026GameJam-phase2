/**
 * Guild Core System
 * Implements: design/gdd/guild-core.md
 *
 * Responsibilities:
 *   - Store and manage guild level (1~5)
 *   - Expose upgrade thresholds and per-level capacity constants
 *   - Determine upgrade eligibility
 *   - Perform guild level-up (level only increases, never decreases)
 *
 * Guild Core does NOT enforce limits directly — it only exposes constants.
 * Enforcement is the responsibility of Mission Dispatch and Adventurer Management.
 *
 * All gameplay values are sourced from GUILD_LEVEL_TABLE in the FT-06 section.
 * Designers: adjust GUILD_LEVEL_TABLE entries (reputationThreshold / maxMissions / maxDifficulty).
 */

import type { Difficulty, GuildLevel } from '../types'
import { eventBus } from '../core/events'

// ---------------------------------------------------------------------------
// Constants (design/gdd/guild-core.md — "公式" section)
// ---------------------------------------------------------------------------

// 舊常數表已移除；所有數值統一由 GUILD_LEVEL_TABLE（下方 FT-06 段落）提供。

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

export interface GuildCoreState {
  /** Current guild level. Valid range: 1 ~ 5. */
  guildLevel: number
}

/**
 * Create a fresh GuildCoreState for a new save.
 * Edge case #4 (guild-core.md): new save always starts at Lv1.
 */
export function createGuildState(): GuildCoreState {
  return { guildLevel: 1 }
}

// ---------------------------------------------------------------------------
// Logic
// ---------------------------------------------------------------------------

/**
 * Returns true if the guild meets the reputation threshold to upgrade.
 *
 * Edge cases handled:
 *   - Lv5 always returns false (already at max)
 *   - Negative reputation: returns false (reputation must meet threshold)
 *   - Level only ever increases; canUpgrade does not consider downgrade
 */
export function canUpgrade(guildLevel: number, reputation: number): boolean {
  if (guildLevel >= 5) return false
  return reputation >= GUILD_LEVEL_TABLE[(guildLevel + 1) as GuildLevel].reputationThreshold
}

/**
 * Increments guildLevel by 1 in-place.
 * Has no effect if already at max level (Lv5).
 *
 * Design contract (guild-core.md):
 *   - Level only increases, never decreases.
 *   - Caller must verify canUpgrade() before calling this function.
 */
export function upgradeGuild(state: GuildCoreState): void {
  if (state.guildLevel >= 5) return
  state.guildLevel++
}

// ---------------------------------------------------------------------------
// Accessors (convenience wrappers for downstream systems)
// ---------------------------------------------------------------------------

/**
 * Returns the maximum number of concurrent active missions for the given level.
 * Used by Mission Dispatch before creating a DispatchRecord.
 */
export function getMaxMissions(guildLevel: number): number {
  return GUILD_LEVEL_TABLE[(guildLevel as GuildLevel)]?.maxMissions ?? 2
}

/**
 * Returns the highest allowed commission difficulty for the given level.
 * Used by Mission Dispatch to filter the mission pool displayed on the board.
 */
export function getMaxCommissionRank(guildLevel: number): Difficulty {
  return getMaxDifficulty((guildLevel as GuildLevel))
}

// ---------------------------------------------------------------------------
// FT-06 Guild Core Phase 2 — 等級表、升級判斷、Game Over 判定
// ---------------------------------------------------------------------------

interface GuildLevelRow {
  title: string
  reputationThreshold: number
  maxDifficulty: Difficulty
  maxMissions: number
}

const GUILD_LEVEL_TABLE: Record<GuildLevel, GuildLevelRow> = {
  1: { title: '新手冒險者公會',       reputationThreshold: 0,   maxDifficulty: 'D', maxMissions: 2 },
  2: { title: '初階冒險者公會',       reputationThreshold: 30,  maxDifficulty: 'C', maxMissions: 3 },
  3: { title: '中階冒險者公會',       reputationThreshold: 80,  maxDifficulty: 'B', maxMissions: 4 },
  4: { title: '高階冒險者公會',       reputationThreshold: 200, maxDifficulty: 'A', maxMissions: 5 },
  5: { title: '名聲顯赫的冒險者公會', reputationThreshold: 400, maxDifficulty: 'S', maxMissions: 6 },
}

/** 依聲望值計算公會等級（從最高往下找第一個門檻已達到的等級）。 */
export function getGuildLevel(reputation: number): GuildLevel {
  for (let lv = 5 as GuildLevel; lv >= 1; lv--) {
    if (reputation >= GUILD_LEVEL_TABLE[lv as GuildLevel].reputationThreshold) {
      return lv as GuildLevel
    }
  }
  return 1
}

/** 回傳該等級可接受的最高任務難度。 */
export function getMaxDifficulty(level: GuildLevel): Difficulty {
  return GUILD_LEVEL_TABLE[level].maxDifficulty
}

/** 回傳等級對應的公會稱號。 */
export function getGuildTitle(level: GuildLevel): string {
  return GUILD_LEVEL_TABLE[level].title
}

/**
 * 升級檢查：若聲望已達更高等級，emit guild:level_up 並回傳新等級；否則回傳 null。
 * 等級只增不減。
 */
export function checkLevelUp(currentLevel: GuildLevel, reputation: number): GuildLevel | null {
  const newLevel = getGuildLevel(reputation)
  if (newLevel > currentLevel) {
    eventBus.emit('guild:level_up', { newLevel })
    return newLevel
  }
  return null
}

/**
 * Game Over 判定：金幣為負且破產警告計時已超過指定期限。
 *
 * @param gold                  目前金幣數
 * @param bankruptcyWarningStart 破產警告開始的時間戳（ms），未觸發則為 null
 * @param warningDurationMs     寬限期長度（ms）
 */
export function isGameOver(
  gold: number,
  bankruptcyWarningStart: number | null,
  warningDurationMs: number,
): boolean {
  return (
    gold < 0 &&
    bankruptcyWarningStart !== null &&
    Date.now() - bankruptcyWarningStart >= warningDurationMs
  )
}
