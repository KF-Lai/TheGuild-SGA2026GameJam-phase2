/**
 * main/helpers.ts — 共用輔助函式（避免 circular import）
 *
 * guild.ts 與 resource.ts 的 pure utility 包裝，
 * 集中於此供 ui-mount.ts / panel-ctx.ts 等引用。
 */

import { getGuildLevel as _getGuildLevel, getGuildTitle as _getGuildTitle } from '../systems/guild'
import { getReputationLabel as _getReputationLabel } from '../systems/resource'
import type { GuildLevel } from '../types'

/** 從聲望值取公會等級 */
export function getGuildLevel(reputation: number): GuildLevel {
  return _getGuildLevel(reputation)
}

/** 從等級取公會稱號 */
export function getGuildTitle(level: GuildLevel): string {
  return _getGuildTitle(level)
}

/** 從聲望值取顯示標籤 */
export function getReputationLabel(reputation: number): string {
  return _getReputationLabel(reputation)
}
