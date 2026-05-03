/**
 * main/tick-loop.ts — 每分鐘主迴圈
 *
 * 職責：
 *   - 檢查 activeMissions 是否到期，若是則 resolveOutcome（並 emit mission:completed）
 *   - 呼叫 npcDecision.tickAutoPickup（自主接單）
 *   - 呼叫 recruitment.checkAutoRefresh（招募池自動刷新）
 *   - 呼叫 gacha.checkAutoRefresh（面試池自動刷新）
 *   - 呼叫 worldDanger.checkLevelUp（危險度升階）
 *   - 補充 missionPool（任務板自動填充）
 *   - 檢查破產 / Game Over
 *
 * Jam 延後項目：
 *   - 任務過期退款（mission.postedAt + MISSION_EXPIRE_MS 超時）
 */

import * as outcome     from '../systems/outcome'
import * as adventurer  from '../systems/adventurer'
import * as npcDecision from '../systems/npc-decision'
import * as recruitment from '../systems/recruitment'
import * as gacha       from '../systems/gacha'
import * as worldDanger from '../systems/world-danger'
import * as dispatch    from '../systems/dispatch'
import * as guild       from '../systems/guild'

import { generateMissionPool } from '../data/missions'
import type { AppState } from './types'
import type { GuildLevel } from '../types'
import { getHiredStaffIDs } from './bootstrap'

/** 任務板目標容量（Jam 固定 5 則；FT-07 實際槽位由 building 決定） */
const MISSION_POOL_TARGET = 5

/**
 * 啟動每分鐘 tick 迴圈。
 * 回傳 stopLoop 函式（呼叫後清除 setInterval）。
 *
 * @param state         AppState
 * @param onRefreshUI   要求 UI 重繪的回呼（tab 切換已 mount 的 panel 刷新）
 * @param onGameOver    Game Over 觸發後的導向回呼
 */
export function startTickLoop(
  state: AppState,
  onRefreshUI: () => void,
  onGameOver: () => void,
): () => void {
  const intervalId = setInterval(() => tick(state, onRefreshUI, onGameOver), 60_000)
  return () => clearInterval(intervalId)
}

/**
 * 單次 tick 邏輯（可供測試直接呼叫）。
 */
export function tick(
  state: AppState,
  onRefreshUI: () => void,
  onGameOver: () => void,
): void {
  const now = Date.now()
  const guildLevel = guild.getGuildLevel(state.guild.resources.reputation) as GuildLevel

  // 1. 檢查 activeMissions 到期
  const expiredIndices: number[] = []
  state.guild.activeMissions.forEach((record, idx) => {
    if (now >= record.endTimestamp) {
      expiredIndices.push(idx)
    }
  })
  // 從後往前移除，避免 index 偏移
  for (let i = expiredIndices.length - 1; i >= 0; i--) {
    const record = state.guild.activeMissions[expiredIndices[i]]
    const difficulty = record.difficulty
    const baseReward = record.preCollectedAmount

    // resolveOutcome 會 emit 'mission:completed'，event-handlers.ts 負責後續處理
    const settlement = outcome.resolveOutcome(record, difficulty, baseReward)
    state.outcomeState.pendingResults.push(settlement)
    // activeMissions 的移除由 event-handlers.ts 的 mission:completed 訂閱完成
  }

  // 2. 受傷恢復檢查
  for (const adv of state.guild.adventurers) {
    if (adv.status === 'wounded' && adv.woundedUntil !== undefined && now >= adv.woundedUntil) {
      adventurer.markIdle(adv)
      npcDecision.markIdle(state.autoPickup, adv.id, now)
      adv.woundedUntil = undefined
    }
  }

  // 3. 自主接單 tick
  const idleAdvs = state.guild.adventurers.filter(a => a.status === 'idle')
  const availableMissions = [...state.guild.missionPool]

  npcDecision.tickAutoPickup(
    state.autoPickup,
    idleAdvs,
    availableMissions,
    (adv, mission) => {
      const result = dispatch.tryDispatch(
        adv,
        mission,
        state.guild.activeMissions,
        guildLevel,
        { forceAccept: true },
      )
      if (result.accepted) {
        state.guild.activeMissions.push(result.record)
        const mIdx = state.guild.missionPool.findIndex(m => m.id === mission.id)
        if (mIdx >= 0) state.guild.missionPool.splice(mIdx, 1)
        return true
      }
      return false
    },
    guildLevel,
    now,
  )

  // 4. 補充 missionPool（任務板不足目標容量時自動填充）
  replenishMissionPool(state, guildLevel)

  // 5. recruitment 自動刷新檢查
  recruitment.checkAutoRefresh(state.recruitment, guildLevel)

  // 6. gacha 自動刷新檢查
  gacha.checkAutoRefresh(state.gacha, getHiredStaffIDs(state))

  // 7. world-danger 升階檢查
  worldDanger.checkLevelUp(state.worldDanger)

  // 8. 破產 / Game Over 判定
  const bankruptcyWarningSeconds = 10800 // fallback；實際由 building 注入
  if (state.guild.resources.gold < 0) {
    if (state.bankruptcyWarningStart === null) {
      state.bankruptcyWarningStart = now
    }
    if (guild.isGameOver(state.guild.resources.gold, state.bankruptcyWarningStart, bankruptcyWarningSeconds * 1000)) {
      onGameOver()
      return
    }
  } else {
    // 金幣轉正：清除破產警告計時
    state.bankruptcyWarningStart = null
  }

  // 9. 要求 UI 重繪
  onRefreshUI()
}

/**
 * 補充 missionPool 到目標容量。
 * 使用 generateMissionPool 從任務資料庫隨機生成。
 */
function replenishMissionPool(state: AppState, _guildLevel: GuildLevel): void {
  const currentSize = state.guild.missionPool.length
  if (currentSize >= MISSION_POOL_TARGET) return

  const needed = MISSION_POOL_TARGET - currentSize
  const activeMissionIds = new Set(state.guild.activeMissions.map(r => r.missionId))

  // 生成足夠任務補充缺口
  const newMissions = generateMissionPool(
    needed + 2, // 多生成一些以過濾 completedIds
    state.guild.completedMissionIds,
    activeMissionIds,
    state.worldDanger.currentDangerLevel,
    state.guild.resources.reputation,
  )

  // 只取需要的數量，設定 postedAt
  const toAdd = newMissions.slice(0, needed)
  for (const m of toAdd) {
    m.postedAt = Date.now()
    state.guild.missionPool.push(m)
  }
}
