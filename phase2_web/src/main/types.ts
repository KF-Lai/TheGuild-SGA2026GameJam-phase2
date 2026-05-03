/**
 * main/types.ts — AppState 介面與輔助型別定義
 * main.ts 持有的頂層狀態集合。
 */

import type { GuildState } from '../types'
import type { OutcomeState } from '../systems/outcome'
import type { WorldDangerState } from '../systems/world-danger'
import type { RecruitmentState } from '../systems/recruitment'
import type { BuildingState } from '../systems/building'
import type { StaffRosterState } from '../systems/staff'
import type { GachaState } from '../systems/gacha'
import type { AutoPickupTracker } from '../systems/npc-decision'

/**
 * 整個遊戲的頂層 runtime 狀態集合。
 * 由 main.ts 持有，透過 ctx 工廠函式傳遞給各 panel。
 */
export interface AppState {
  guild:        GuildState
  outcomeState: OutcomeState
  worldDanger:  WorldDangerState
  recruitment:  RecruitmentState
  buildings:    BuildingState[]
  staffRoster:  StaffRosterState
  gacha:        GachaState
  autoPickup:   AutoPickupTracker
  /** 破產警告開始時間戳（ms）；未觸發為 null */
  bankruptcyWarningStart: number | null
  /** 總冒險者招募計數（含死亡）— Game Over 統計用 */
  totalAdventurersHired: number
  /** 死亡冒險者累計 */
  totalAdventurersDead: number
  /** 完成任務累計 */
  totalMissionsCompleted: number
  /** 遊戲開始時間戳（ms）— daysPlayed 計算用 */
  gameStartMs: number
}
