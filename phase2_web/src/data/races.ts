/**
 * Racial Modifiers System — The Guild
 *
 * Phase 2 Web Jam 簡化（2026-05-03）：
 *   - 種族 trait 不實作；所有 modifier 歸 0，不影響 success / death rate
 *   - RaceId / name 保留供 fallback（types 與 adventurer.raceId 欄位仍在）
 *   - UI（5.2 名冊 / 5.4 招募）已移除種族顯示
 *   - dispatch.ts 仍呼叫 getRaceModifier，但結果為 0+0=0 不影響計算
 */

import type { RaceId } from '../types'

/**
 * A racial modifier set.
 * Phase 2 Jam 階段所有值為 0；保留結構供 post-jam 重新啟用。
 */
export interface RaceModifier {
  id: RaceId
  name: string
  description: string
  /** Additive success rate modifier (decimal). Jam 全為 0。*/
  successRateModifier: number
  /** Additive death rate modifier (decimal). Jam 全為 0。*/
  deathRateModifier: number
}

/**
 * Racial modifier database — Phase 2 Jam 全 0 版本。
 * post-jam 啟用時將恢復原始 ±8~±15% 設計（git history 可查）。
 */
export const RACE_MODIFIERS: Record<RaceId, RaceModifier> = {
  human:      { id: 'human',      name: '人類',     description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
  elf:        { id: 'elf',        name: '精靈',     description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
  dwarf:      { id: 'dwarf',      name: '矮人',     description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
  orc:        { id: 'orc',        name: '獸人',     description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
  halfling:   { id: 'halfling',   name: '混血人類', description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
  lizardfolk: { id: 'lizardfolk', name: '黑暗精靈', description: '（jam 範疇種族 trait 停用）', successRateModifier: 0, deathRateModifier: 0 },
}

/**
 * Retrieves the racial modifier for a given race ID.
 * Jam 階段一律回傳 0 modifier。
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
