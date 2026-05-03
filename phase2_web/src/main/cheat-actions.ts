/**
 * main/cheat-actions.ts — Phase 2 金手指動作工廠
 *
 * 接收 AppState + requestRefresh callback，產生各 cheat 操作的閉包集合。
 * 不修改任何 system 模組；透過 system 公開 API 或直接 mutate state。
 */

import * as resource    from '../systems/resource'
import * as adventurer  from '../systems/adventurer'
import * as outcome     from '../systems/outcome'
import * as staff       from '../systems/staff'
import * as gacha       from '../systems/gacha'
import * as recruitment from '../systems/recruitment'
import * as guild       from '../systems/guild'
import { generateMissionPool } from '../data/missions'
import { eventBus } from '../core/events'
import type { AppState } from './types'
import type { GuildLevel, Rank, WorldDanger } from '../types'

// ── forceAccept 全域 flag（模組級狀態）────────────────────────────────────────

/** dispatch 路徑讀取此 flag；cheat panel toggle 寫入 */
let _forceAcceptFlag = false

export function isForceAccept(): boolean {
  return _forceAcceptFlag
}

// ── CheatActions 介面 ─────────────────────────────────────────────────────────

export interface CheatActions {
  // 金幣 / 聲望
  addGold:             (amount: number) => void
  changeReputation:    (delta: number) => void
  setReputationToLv5:  () => void

  // 委託 / 時間
  refreshMissionPool:  () => void
  completeAllMissions: () => void
  failAllMissions:     () => void
  advanceTime:         (minutes: number) => void

  // 冒險者
  refreshRecruitPools: () => void
  giveAdventurers:     (count: number, rank: 'D' | 'S') => void
  toggleForceAccept:   (enabled: boolean) => void
  isForceAcceptEnabled:() => boolean

  // 建築 / 職員
  upgradeAllBuildings: () => void
  unlockStaffSystem:   () => void
  hireAllStaff:        () => void
  fireAllStaff:        () => void
  refreshGachaPool:    () => void

  // 世界 / 破產
  increaseDanger:      () => void
  decreaseDanger:      () => void
  simulateBankruptcy:  () => void

  // 通用
  requestUIRefresh:    () => void
}

// ── 危險度順序表（與 world-danger.ts 對齊）───────────────────────────────────

const DANGER_ORDER: WorldDanger[] = ['E', 'D', 'C', 'B', 'A']

// ── 建築最大等級表（對齊 building.ts BUILDING_TABLE）────────────────────────

const BUILDING_MAX_LEVEL: Record<number, number> = {
  1: 5, 2: 3, 3: 5, 4: 5, 5: 5, 6: 5,
}

// ── 工廠函式 ──────────────────────────────────────────────────────────────────

/**
 * 建立 cheat actions 集合，注入 AppState 與 UI refresh callback。
 *
 * @param state          AppState（直接 mutate）
 * @param requestRefresh 每次 cheat 後要求 UI 重繪
 */
export function createCheatActions(
  state: AppState,
  requestRefresh: () => void,
): CheatActions {
  return {
    // ── 金幣 / 聲望 ──────────────────────────────────────────────────────────

    addGold: (amount) => {
      resource.addGold(state.guild.resources, amount)
      requestRefresh()
    },

    changeReputation: (delta) => {
      resource.changeReputation(state.guild.resources, delta)
      requestRefresh()
    },

    setReputationToLv5: () => {
      // Lv5 門檻 = 400（對應 guild.ts GUILD_LEVEL_TABLE[5].reputationThreshold）
      const current = state.guild.resources.reputation
      resource.changeReputation(state.guild.resources, 400 - current)
      requestRefresh()
    },

    // ── 委託 / 時間 ───────────────────────────────────────────────────────────

    refreshMissionPool: () => {
      state.guild.missionPool.length = 0
      const activeMissionIds = new Set(state.guild.activeMissions.map(r => r.missionId))
      const newPool = generateMissionPool(
        5,
        state.guild.completedMissionIds,
        activeMissionIds,
        state.worldDanger.currentDangerLevel,
        state.guild.resources.reputation,
      )
      for (const m of newPool) {
        m.postedAt = Date.now()
        state.guild.missionPool.push(m)
      }
      requestRefresh()
    },

    completeAllMissions: () => {
      // 強制 finalSuccessRate=1 / finalDeathRate=0 後走正常 resolveOutcome 路徑：
      //   - resolveOutcome 內部 emit 'mission:completed'
      //   - event-handlers.ts 訂閱端負責套 goldDelta、聲望、移除 activeMissions、
      //     處理冒險者狀態、統計計數
      // 不手動清空 activeMissions，event-handler 自然 splice
      const now = Date.now()
      const toResolve = [...state.guild.activeMissions]
      for (const record of toResolve) {
        record.finalSuccessRate = 1.0
        record.finalDeathRate   = 0.0
        record.endTimestamp     = now

        const settlement = outcome.resolveOutcome(record, record.difficulty, record.preCollectedAmount)
        state.outcomeState.pendingResults.push(settlement)
      }
      requestRefresh()
    },

    failAllMissions: () => {
      // 強制 finalSuccessRate=0 / finalDeathRate=0 走 resolveOutcome；同上不手動清空。
      const now = Date.now()
      const toResolve = [...state.guild.activeMissions]
      for (const record of toResolve) {
        record.finalSuccessRate = 0.0
        record.finalDeathRate   = 0.0
        record.endTimestamp     = now

        const settlement = outcome.resolveOutcome(record, record.difficulty, record.preCollectedAmount)
        state.outcomeState.pendingResults.push(settlement)
      }
      requestRefresh()
    },

    advanceTime: (minutes) => {
      const ms = minutes * 60_000
      state.guild.activeMissions.forEach(r => {
        r.endTimestamp -= ms
      })
      // 招募池與 gacha 倒數也提前
      state.recruitment.lastRefreshTimestamp -= ms
      state.gacha.lastRefreshTimestamp       -= ms
      requestRefresh()
    },

    // ── 冒險者 ────────────────────────────────────────────────────────────────

    refreshRecruitPools: () => {
      state.recruitment.lastRefreshTimestamp = 0
      const guildLevel = guild.getGuildLevel(state.guild.resources.reputation) as GuildLevel
      recruitment.checkAutoRefresh(state.recruitment, guildLevel)
      requestRefresh()
    },

    giveAdventurers: (count, rank) => {
      for (let i = 0; i < count; i++) {
        const adv = adventurer.createAdventurer(rank as Rank)
        state.guild.adventurers.push(adv)
        state.totalAdventurersHired++
      }
      requestRefresh()
    },

    toggleForceAccept: (enabled) => {
      _forceAcceptFlag = enabled
      requestRefresh()
    },

    isForceAcceptEnabled: () => _forceAcceptFlag,

    // ── 建築 / 職員 ───────────────────────────────────────────────────────────

    upgradeAllBuildings: () => {
      for (const bs of state.buildings) {
        const maxLv = BUILDING_MAX_LEVEL[bs.buildingID] ?? bs.currentLevel
        bs.currentLevel = maxLv
      }
      // 廣播升級事件使 UI 刷新建築面板
      eventBus.emit('building:upgraded', { buildingID: 0, fromLevel: 0, toLevel: 0 })
      requestRefresh()
    },

    unlockStaffSystem: () => {
      const lounge = state.buildings.find(b => b.buildingID === 6)
      if (lounge && lounge.currentLevel === 0) {
        lounge.currentLevel = 1
        eventBus.emit('building:upgraded', { buildingID: 6, fromLevel: 0, toLevel: 1 })
      }
      requestRefresh()
    },

    hireAllStaff: () => {
      // 若職員系統未解鎖，先強制解鎖
      const lounge = state.buildings.find(b => b.buildingID === 6)
      if (lounge && lounge.currentLevel === 0) {
        lounge.currentLevel = 1
        eventBus.emit('building:upgraded', { buildingID: 6, fromLevel: 0, toLevel: 1 })
      }

      // 三職員：米拉 501 → 委託板 1 / 譚恩 502 → 保險櫃 5 / 凱拉 503 → 公會櫃臺 4
      const targets = [
        { staffID: 501, buildingID: 1 },
        { staffID: 502, buildingID: 5 },
        { staffID: 503, buildingID: 4 },
      ] as const

      for (const t of targets) {
        if (state.staffRoster.roster.some(s => s.staffID === t.staffID)) continue
        const result = staff.hireStaff(
          state.staffRoster,
          { staffID: t.staffID },
          t.buildingID,
        )
        if (!result.ok) {
          console.warn(`[cheat] hireAllStaff staffID=${t.staffID} 失敗: ${result.reason}`)
        }
      }
      requestRefresh()
    },

    fireAllStaff: () => {
      // 不走 tryFireStaff（會扣資遣費），直接清空 roster
      state.staffRoster.roster.length = 0
      requestRefresh()
    },

    refreshGachaPool: () => {
      state.gacha.lastRefreshTimestamp = 0
      const hired = new Set(state.staffRoster.roster.map(s => s.staffID))
      gacha.checkAutoRefresh(state.gacha, hired)
      requestRefresh()
    },

    // ── 世界 / 破產 ───────────────────────────────────────────────────────────

    increaseDanger: () => {
      const idx = DANGER_ORDER.indexOf(state.worldDanger.currentDangerLevel)
      if (idx >= 0 && idx < DANGER_ORDER.length - 1) {
        const next = DANGER_ORDER[idx + 1]
        state.worldDanger.currentDangerLevel = next
        state.worldDanger.acceptedMissionCount = 0
        eventBus.emit('danger:level_changed', { newLevel: next })
      }
      requestRefresh()
    },

    decreaseDanger: () => {
      const idx = DANGER_ORDER.indexOf(state.worldDanger.currentDangerLevel)
      if (idx > 0) {
        const prev = DANGER_ORDER[idx - 1]
        state.worldDanger.currentDangerLevel = prev
        eventBus.emit('danger:level_changed', { newLevel: prev })
      }
      // idx === 0（'E' 階）時靜默 return，不操作
      requestRefresh()
    },

    simulateBankruptcy: () => {
      // 設金幣為 -100（使用 addGold delta，確保事件發出）
      const currentGold = state.guild.resources.gold
      resource.addGold(state.guild.resources, -100 - currentGold)
      state.bankruptcyWarningStart = Date.now()
      requestRefresh()
    },

    // ── 通用 ─────────────────────────────────────────────────────────────────

    requestUIRefresh: requestRefresh,
  }
}
