/**
 * main/panel-ctx.ts — 各 panel 的 ctx 工廠函式
 *
 * 每個 panel 的 ctx 介面不同，此模組集中組建所有 ctx，
 * 避免 main.ts 過於冗長。
 *
 * Jam 延後項目：
 *   - getNextLevelThreshold：guild.ts 未直接暴露，此處透過 NEXT_LEVEL_REP 查表
 */

import * as dispatch    from '../systems/dispatch'
import * as guild       from '../systems/guild'
import * as building    from '../systems/building'
import * as recruitment from '../systems/recruitment'
import * as npcDecision from '../systems/npc-decision'
import * as staff       from '../systems/staff'
import * as gacha       from '../systems/gacha'

import type { AppState } from './types'
import type {
  CommissionBoardPanelCtx,
} from '../ui/panels/commission-board-panel'
import type {
  AdventurerRosterPanelCtx,
} from '../ui/panels/adventurer-roster-panel'
import type {
  GuildOverviewPanelCtx,
} from '../ui/panels/guild-overview-panel'
import type {
  RecruitmentPanelCtx,
} from '../ui/panels/recruitment-panel'
import type {
  GuildBuildingPanelCtx,
} from '../ui/panels/guild-building-panel'
import type {
  GachaStaffPanelCtx,
} from '../ui/panels/gacha-staff-panel'
import type {
  SettingsPanelCtx,
} from '../ui/panels/settings-panel'

import type { GuildLevel } from '../types'
import { getHiredStaffIDs } from './bootstrap'
import { isForceAccept } from './cheat-actions'

// ── 下一階聲望門檻查表（guild.ts GUILD_LEVEL_TABLE 內部私有，故在此複製）────────
// 對應 guild.ts 的 GUILD_LEVEL_TABLE；Jam hardcode 是安全的（雙方 hardcode 相同值）
const NEXT_LEVEL_REP: Record<GuildLevel, number | null> = {
  1: 30,
  2: 80,
  3: 200,
  4: 400,
  5: null, // 最高等級
}

// ── 委託板 ctx ─────────────────────────────────────────────────────────────────

export function makeCommissionCtx(
  state: AppState,
  onRefreshUI: () => void,
): CommissionBoardPanelCtx {
  return {
    getGuild: () => state.guild,
    getStaffRoster: () => state.staffRoster,
    getMaxConcurrentMissions: () => building.getMaxConcurrentMissions(state.buildings),
    getMaxDifficulty: () =>
      guild.getMaxDifficulty(guild.getGuildLevel(state.guild.resources.reputation)),

    onDispatch: (adventurerID, missionID) => {
      const adv = state.guild.adventurers.find(a => a.id === adventurerID)
      const mission = state.guild.missionPool.find(m => m.id === missionID)
      if (!adv || !mission) return

      const guildLevel = guild.getGuildLevel(state.guild.resources.reputation)
      const result = dispatch.tryDispatch(
        adv,
        mission,
        state.guild.activeMissions,
        guildLevel,
        { forceAccept: isForceAccept() },
      )
      if (result.accepted) {
        state.guild.activeMissions.push(result.record)
        // 從 missionPool 移除已派遣任務
        const mIdx = state.guild.missionPool.findIndex(m => m.id === missionID)
        if (mIdx >= 0) state.guild.missionPool.splice(mIdx, 1)
        onRefreshUI()
      }
    },

    onRecommend: (adventurerID, missionID) => {
      const adv = state.guild.adventurers.find(a => a.id === adventurerID)
      const mission = state.guild.missionPool.find(m => m.id === missionID)
      if (!adv || !mission) return

      // P-02 路徑：makeDecision → if accepted → tryDispatch(forceAccept=true)
      const decision = npcDecision.makeDecision(adv, mission)
      if (!decision.accepted) return

      const guildLevel = guild.getGuildLevel(state.guild.resources.reputation)
      const result = dispatch.tryDispatch(
        adv,
        mission,
        state.guild.activeMissions,
        guildLevel,
        { forceAccept: true },
      )
      if (result.accepted) {
        state.guild.activeMissions.push(result.record)
        const mIdx = state.guild.missionPool.findIndex(m => m.id === missionID)
        if (mIdx >= 0) state.guild.missionPool.splice(mIdx, 1)
        onRefreshUI()
      }
    },
  }
}

// ── 名冊 ctx ───────────────────────────────────────────────────────────────────

export function makeRosterCtx(state: AppState): AdventurerRosterPanelCtx {
  return {
    getGuild: () => state.guild,
    getRosterCap: () => building.getRosterCap(state.buildings),
  }
}

// ── 公會總覽 ctx ───────────────────────────────────────────────────────────────

export function makeOverviewCtx(state: AppState): GuildOverviewPanelCtx {
  return {
    getGuild: () => state.guild,
    getWorldDanger: () => state.worldDanger,
    getMaxConcurrentMissions: () => building.getMaxConcurrentMissions(state.buildings),
    computeGuildLevel: (reputation) => guild.getGuildLevel(reputation),
    getGuildTitle: (level) => guild.getGuildTitle(level),
    getMaxDifficulty: (level) => guild.getMaxDifficulty(level),
    getNextLevelThreshold: (currentLevel) => NEXT_LEVEL_REP[currentLevel] ?? null,
  }
}

// ── 招募 ctx ───────────────────────────────────────────────────────────────────

export function makeRecruitmentCtx(
  state: AppState,
  onRefreshUI: () => void,
): RecruitmentPanelCtx {
  const guildLevel = () => guild.getGuildLevel(state.guild.resources.reputation) as GuildLevel

  return {
    getGuild: () => state.guild,
    getRecruitment: () => state.recruitment,
    getRosterCap: () => building.getRosterCap(state.buildings),
    getGuildLevel: guildLevel,

    onRecruitRookie: (candidateID) => {
      recruitment.recruitRookie(
        state.recruitment,
        candidateID,
        (adv) => {
          const cap = building.getRosterCap(state.buildings)
          if (state.guild.adventurers.length >= cap) return false
          state.guild.adventurers.push(adv)
          state.totalAdventurersHired++
          return true
        },
      )
      onRefreshUI()
    },

    onRecruitVeteran: (candidateID) => {
      recruitment.recruitVeteran(
        state.recruitment,
        candidateID,
        (adv) => {
          const cap = building.getRosterCap(state.buildings)
          if (state.guild.adventurers.length >= cap) return false
          state.guild.adventurers.push(adv)
          state.totalAdventurersHired++
          return true
        },
        guildLevel(),
      )
      onRefreshUI()
    },

    onManualRefresh: () => {
      recruitment.manualRefresh(state.recruitment, guildLevel())
      onRefreshUI()
    },
  }
}

// ── 建設 ctx ───────────────────────────────────────────────────────────────────

export function makeBuildingCtx(
  state: AppState,
  onRefreshUI: () => void,
): GuildBuildingPanelCtx {
  // BuildingTable 效果值輔助（依 buildingID 直接查表）
  // 效果單位說明（對應 building.ts effectValue 語義）：
  //   1=槽數 / 2=秒 / 3=人數 / 4=任務數 / 5=秒 / 6=秒
  function getCurrentEffectValue(buildingID: number): number {
    switch (buildingID) {
      case 1: return building.getMissionSlotCount(state.buildings)
      case 2: return building.getRecruitRefreshIntervalSec(state.buildings)
      case 3: return building.getRosterCap(state.buildings)
      case 4: return building.getMaxConcurrentMissions(state.buildings)
      case 5: return building.getBankruptcyWarningSeconds(state.buildings)
      case 6: return building.getStaffLoungeRefreshSec(state.buildings)
      default: return 0
    }
  }

  // 取下一級資訊（來自 building.ts BUILDING_TABLE 的已知 hardcode）
  // Jam 簡化：直接對照 upgradeCost / guildLevelReq（panel 亦已有 hardcode BUILDING_DISPLAY）
  // 此處僅暴露 effectValue 差異，其餘由 panel 自行查表
  function getNextLevelInfo(buildingID: number) {
    const states = state.buildings
    const cur = states.find(s => s.buildingID === buildingID)
    if (!cur) return null
    const nextLevel = cur.currentLevel + 1
    // 取用 canUpgrade 作為間接確認；真正的 cost/req 由 panel 查 hardcode 表
    // 由於 BuildingTable 是 module-private，透過 canUpgrade 的結果推斷 nextLevel 合法性
    const canResult = building.canUpgrade(states, buildingID)
    if (canResult === 'ALREADY_MAX') return null

    // 以 panel 可接受的 NextLevelInfo 格式回傳；effectValue 差無法在此精確計算，
    // 回傳 currentEffectValue 佔位，panel 必要時自行計算差值
    return {
      nextLevel,
      upgradeCost: 0,       // panel 自行從 BUILDING_DISPLAY 取
      guildLevelReq: 0,     // 同上
      currentEffectValue: getCurrentEffectValue(buildingID),
      nextEffectValue: getCurrentEffectValue(buildingID), // placeholder
    }
  }

  return {
    getGuild: () => state.guild,
    getBuildings: () => state.buildings,
    getGuildLevel: () => guild.getGuildLevel(state.guild.resources.reputation) as GuildLevel,
    canUpgrade: (buildingID) => building.canUpgrade(state.buildings, buildingID),
    onUpgrade: (buildingID) => {
      const result = building.tryUpgradeBuilding(state.buildings, buildingID)
      if (result === 'SUCCESS') onRefreshUI()
      return result
    },
    getNextLevelInfo: getNextLevelInfo,
    getCurrentEffectValue,
  }
}

// ── 面試職員 ctx ───────────────────────────────────────────────────────────────

export function makeGachaStaffCtx(
  state: AppState,
  onRefreshUI: () => void,
): GachaStaffPanelCtx {
  return {
    getGuild: () => state.guild,
    getGacha: () => state.gacha,
    getStaffRoster: () => state.staffRoster,
    isStaffSystemUnlocked: () => building.isStaffSystemUnlocked(state.buildings),
    getGold: () => state.guild.resources.gold,

    onRecruit: (candidateID, buildingID) => {
      return gacha.tryRecruit(
        state.gacha,
        candidateID,
        buildingID,
        (staffID, bID) => staff.hireStaff(state.staffRoster, { staffID }, bID),
      )
    },

    onDismiss: (candidateID) => gacha.dismissCandidate(state.gacha, candidateID),

    onManualRefresh: () => {
      const result = gacha.tryManualRefresh(state.gacha, getHiredStaffIDs(state))
      if (result === 'SUCCESS') onRefreshUI()
      return result
    },

    onAssign: (instanceId, buildingID) =>
      staff.tryAssignStaff(state.staffRoster, instanceId, buildingID),

    onFire: (instanceId) => {
      const result = staff.tryFireStaff(state.staffRoster, instanceId)
      if (result === 'SUCCESS') onRefreshUI()
      return result
    },
  }
}

// ── 設定 ctx ───────────────────────────────────────────────────────────────────

export function makeSettingsCtx(
  state: AppState,
  onSave: (slot: string) => Promise<void>,
  onLoad: (slot: string) => Promise<boolean>,
  onDelete: (slot: string) => Promise<void>,
  onReset: () => Promise<void>,
): SettingsPanelCtx {
  return {
    getGuild: () => state.guild,
    onSave,
    onLoad,
    onDelete,
    onReset,
  }
}
