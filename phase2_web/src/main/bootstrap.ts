/**
 * main/bootstrap.ts — 系統初始化與 deps 注入
 *
 * Bootstrap 順序（依 spec §5.2）：
 *   1. setXxxHandlers（deps 注入）
 *   2. initialize（各 system 啟動）
 *
 * Jam 延後項目：
 *   - F-03 callback：world-danger / building 的 bankruptcy 推送為 console.log stub
 *   - FT-12 薪水管線：setSalaryChargeHandler 已注入但 jam 不主動觸發
 */

import * as resource    from '../systems/resource'
import * as worldDanger from '../systems/world-danger'
import * as building    from '../systems/building'
import * as recruitment from '../systems/recruitment'
import * as npcDecision from '../systems/npc-decision'
import * as goldFlow    from '../systems/gold-flow'
import * as staff       from '../systems/staff'
import * as gacha       from '../systems/gacha'

import type { AppState } from './types'
import type { GuildLevel } from '../types'
import { getGuildLevel } from '../systems/guild'

// ── building.ts 的 slotCount 對映（與 GDD §7.1 一致）──────────────────────────
// buildingID → slotCount（硬編碼；委託板=3，公會櫃臺=2，保險櫃=1，其餘=0）
const BUILDING_SLOT_MAP: Record<number, number> = { 1: 3, 4: 2, 5: 1 }

/**
 * 取得 staffRoster 中所有已雇用職員的 staffID 集合。
 */
export function getHiredStaffIDs(state: AppState): Set<number> {
  return new Set(state.staffRoster.roster.map(s => s.staffID))
}

/**
 * 注入所有 system 的跨系統 deps。
 * 必須在 initializeSystems() 之前呼叫。
 *
 * @param state 完整 AppState（deps 透過閉包存取 state）
 */
export function injectDeps(state: AppState): void {
  // 1. world-danger：注入 F-03 SetBankruptcyThreshold callback（Jam stub）
  worldDanger.setBankruptcyThresholdHandler((maxDebt) => {
    // Jam 階段：僅記錄；future 整合 F-03 後寫入 ResourceState
    console.log(`[F-03 stub] bankruptcy threshold = ${maxDebt}`)
  })

  // 2. building：注入 Resource deps 與 F-03 bankruptcy warning callback
  building.setResourceHandlers({
    getGold: () => state.guild.resources.gold,
    addGold: (delta) => {
      if (delta < 0) {
        // 消費路徑：使用 spendGold 確保不透支
        return resource.spendGold(state.guild.resources, -delta)
      }
      // 收入路徑：直接 addGold
      resource.addGold(state.guild.resources, delta)
      return true
    },
    getGuildLevel: () => getGuildLevel(state.guild.resources.reputation),
  })
  building.setBankruptcyWarningHandler((seconds) => {
    // Jam 階段：僅記錄
    console.log(`[F-03 stub] bankruptcy warning duration = ${seconds}s`)
  })

  // 3. recruitment：注入 Resource deps
  recruitment.setResourceHandlers({
    canAfford: (amount) => state.guild.resources.gold >= amount,
    addGold: (delta) => {
      if (delta < 0 && state.guild.resources.gold + delta < 0) return false
      resource.addGold(state.guild.resources, delta)
      return true
    },
    getReputation: () => state.guild.resources.reputation,
  })

  // 4. staff：注入 system deps + salary handler
  staff.setSystemDeps({
    isUnlocked: () => building.isStaffSystemUnlocked(state.buildings),
    getBuildingSlotCount: (id) => BUILDING_SLOT_MAP[id] ?? 0,
    spendGold: (amount) => resource.spendGold(state.guild.resources, amount),
  })
  staff.setSalaryChargeHandler((items, ts) => {
    // Jam 不主動觸發薪水，handler 保留供未來使用
    goldFlow.chargeSalary(items, ts)
  })

  // 5. gacha：注入 system deps
  gacha.setSystemDeps({
    isUnlocked: () => building.isStaffSystemUnlocked(state.buildings),
    getStaffLoungeRefreshSec: () => building.getStaffLoungeRefreshSec(state.buildings),
    getGold: () => state.guild.resources.gold,
    spendGold: (amount) => resource.spendGold(state.guild.resources, amount),
  })

  // 6. npc-decision：注入 FT-12 / C-05 handler（Jam stubs）
  npcDecision.setStaffWillingnessBonusHandler(
    () => staff.getStaffWillingnessBonus(state.staffRoster),
  )
  npcDecision.setBehaviorTraitHandler(() => 0) // C-05 stub

  // 7. gold-flow：注入 Resource deps 與 bonus handlers
  goldFlow.setResourceHandlers({
    getGold: () => state.guild.resources.gold,
    addGold: (positiveAmount) => {
      resource.addGold(state.guild.resources, positiveAmount)
      return true
    },
    addGoldAllowBankruptcy: (delta) => resource.addGold(state.guild.resources, delta),
    getBankruptcyWarningState: () => 'Normal', // Jam 簡化
  })
  goldFlow.setBonusHandlers({
    getAccountantCommissionBonus: () =>
      staff.getAccountantCommissionBonus(state.staffRoster),
    getAccountantPenaltyBonus: () =>
      staff.getAccountantPenaltyBonus(state.staffRoster),
    getBuildingPenaltyBonus: () => 0,
  })
}

/**
 * 依序初始化各 system（deps 必須已注入）。
 * 對應 spec §5.2 步驟 3。
 *
 * @param state 完整 AppState
 */
export function initializeSystems(state: AppState): void {
  const guildLevel = getGuildLevel(state.guild.resources.reputation)

  // world-danger：設定 gameStartTimestamp 並推送初始 maxDebt
  worldDanger.initialize(state.worldDanger)

  // building：注入 deps 並推送保險櫃破產倒數
  building.initialize(state.buildings, {
    getGold: () => state.guild.resources.gold,
    addGold: (delta) => {
      if (delta < 0) {
        return resource.spendGold(state.guild.resources, -delta)
      }
      resource.addGold(state.guild.resources, delta)
      return true
    },
    getGuildLevel: () => getGuildLevel(state.guild.resources.reputation),
  })

  // recruitment：注入 deps 並立即 checkAutoRefresh（填充候選池）
  recruitment.initialize(
    state.recruitment,
    {
      canAfford: (amount) => state.guild.resources.gold >= amount,
      addGold: (delta) => {
        if (delta < 0 && state.guild.resources.gold + delta < 0) return false
        resource.addGold(state.guild.resources, delta)
        return true
      },
      getReputation: () => state.guild.resources.reputation,
    },
    guildLevel as GuildLevel,
  )

  // gacha：立即 checkAutoRefresh（若職員室已解鎖）
  gacha.initialize(state.gacha, getHiredStaffIDs(state))
}
