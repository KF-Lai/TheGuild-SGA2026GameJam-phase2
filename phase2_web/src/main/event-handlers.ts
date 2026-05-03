/**
 * main/event-handlers.ts — EventBus 訂閱與後續處理
 *
 * 所有 eventBus.on() 呼叫集中於此；呼叫端（main.ts）持有 unsubscribe 清單。
 * 訂閱函式回傳 dispose 函式，方便整個 game session 結束時清除。
 */

import * as resource    from '../systems/resource'
import * as adventurer  from '../systems/adventurer'
import * as npcDecision from '../systems/npc-decision'
import * as guild       from '../systems/guild'

import { eventBus } from '../core/events'
import type { AppState } from './types'
import type { NotificationAreaHandle } from '../ui/notification-area'
import type { GuildLevel } from '../types'

// ── outcome 文字對應 ────────────────────────────────────────────────────────────

function outcomeText(outcome: string): string {
  switch (outcome) {
    case 'SUCCESS':  return '成功'
    case 'PYRRHIC':  return '慘勝'
    case 'FAILURE':  return '失敗'
    case 'DEATH':    return '死亡'
    default:         return outcome
  }
}

/**
 * 訂閱所有 EventBus 事件，串接到 AppState 更新與 UI 通知。
 * 回傳 dispose 函式，呼叫後解除所有訂閱。
 *
 * @param state         完整 AppState
 * @param notifications 通知區 handle（push toast）
 */
export function subscribeEvents(
  state: AppState,
  notifications: NotificationAreaHandle,
): () => void {
  // ── mission:completed ─────────────────────────────────────────────────────
  const onMissionCompleted = (payload: {
    dispatchId: string
    outcome: string
    goldDelta: number
    reputationDelta: number
  }) => {
    const idx = state.guild.activeMissions.findIndex(m => m.id === payload.dispatchId)
    if (idx === -1) return

    const dispatchRecord = state.guild.activeMissions[idx]
    state.guild.activeMissions.splice(idx, 1)
    state.guild.completedMissionIds.add(dispatchRecord.missionId)

    resource.addGold(state.guild.resources, payload.goldDelta)
    resource.changeReputation(state.guild.resources, payload.reputationDelta)

    // 統計計數
    state.totalMissionsCompleted++

    // 套用冒險者狀態
    // partyMembers 涵蓋所有隊員（含非 lead）
    const memberIds = dispatchRecord.partyMembers.map(m => m.id)

    for (const memberId of memberIds) {
      const adv = state.guild.adventurers.find(a => a.id === memberId)
      if (!adv) continue

      if (payload.outcome === 'DEATH' || payload.outcome === 'PYRRHIC') {
        adventurer.markDead(adv)
        const advIdx = state.guild.adventurers.findIndex(a => a.id === adv.id)
        if (advIdx >= 0) state.guild.adventurers.splice(advIdx, 1)
        state.totalAdventurersDead++
        // 清除 autoPickup 追蹤
        npcDecision.markBusy(state.autoPickup, memberId)
      } else {
        adventurer.markIdle(adv)
        npcDecision.markIdle(state.autoPickup, memberId)
      }
    }

    // 觸發公會升級檢查
    const currentLevel = guild.getGuildLevel(state.guild.resources.reputation)
    const newLevel = guild.checkLevelUp(currentLevel as GuildLevel, state.guild.resources.reputation)
    if (newLevel !== null) {
      state.guild.guildLevel = newLevel
      notifications.push({
        type: 'success',
        message: `公會升級至 Lv${newLevel}！`,
        autoDismissMs: 6000,
      })
    }

    // 推送結算 toast
    const sign = payload.goldDelta >= 0 ? '+' : ''
    notifications.push({
      type: payload.outcome === 'SUCCESS'
        ? 'success'
        : payload.outcome === 'FAILURE'
          ? 'warning'
          : 'danger',
      message: `任務 ${dispatchRecord.missionName}：${outcomeText(payload.outcome)} ${sign}${payload.goldDelta}g`,
      autoDismissMs: 5000,
    })
  }

  // ── adventurer:died ────────────────────────────────────────────────────────
  const onAdventurerDied = (payload: {
    adventurerId: string
    adventurerName: string
    missionId: string
  }) => {
    notifications.push({
      type: 'danger',
      message: `${payload.adventurerName} 在任務中陣亡`,
      autoDismissMs: 6000,
    })
  }

  // ── adventurer:wounded ─────────────────────────────────────────────────────
  const onAdventurerWounded = (payload: {
    adventurerId: string
    woundedUntil: number
  }) => {
    const adv = state.guild.adventurers.find(a => a.id === payload.adventurerId)
    if (adv) {
      adv.status = 'wounded'
      adv.woundedUntil = payload.woundedUntil
    }
  }

  // ── recruit:success ────────────────────────────────────────────────────────
  const onRecruitSuccess = (payload: {
    adventurerId: string
    source: 'rookie' | 'veteran'
  }) => {
    state.totalAdventurersHired++
    const label = payload.source === 'rookie' ? '新手' : '老手'
    notifications.push({
      type: 'success',
      message: `招募了新${label}冒險者`,
      autoDismissMs: 4000,
    })
  }

  // ── building:upgraded ─────────────────────────────────────────────────────
  const onBuildingUpgraded = (payload: {
    buildingID: number
    fromLevel: number
    toLevel: number
  }) => {
    notifications.push({
      type: 'success',
      message: `建築升級至 Lv${payload.toLevel}`,
      autoDismissMs: 4000,
    })
  }

  // ── staff:hired ────────────────────────────────────────────────────────────
  const onStaffHired = (payload: { staffId: number; instanceId: string }) => {
    notifications.push({
      type: 'success',
      message: `錄用職員（instance ${payload.instanceId.slice(0, 12)}）`,
      autoDismissMs: 4000,
    })
  }

  // ── guild:level_up ─────────────────────────────────────────────────────────
  // （公會升級通知由 onMissionCompleted 內直接 push，此訂閱僅更新 state.guild.guildLevel）
  const onGuildLevelUp = (payload: { newLevel: number }) => {
    state.guild.guildLevel = payload.newLevel
  }

  // ── danger:level_changed ───────────────────────────────────────────────────
  const onDangerLevelChanged = (payload: { newLevel: string }) => {
    notifications.push({
      type: 'warning',
      message: `世界危險度上升至 ${payload.newLevel} 階`,
      autoDismissMs: 6000,
    })
  }

  // 訂閱
  eventBus.on('mission:completed', onMissionCompleted as any)
  eventBus.on('adventurer:died', onAdventurerDied)
  eventBus.on('adventurer:wounded', onAdventurerWounded)
  eventBus.on('recruit:success', onRecruitSuccess)
  eventBus.on('building:upgraded', onBuildingUpgraded)
  eventBus.on('staff:hired', onStaffHired)
  eventBus.on('guild:level_up', onGuildLevelUp as any)
  eventBus.on('danger:level_changed', onDangerLevelChanged as any)

  // 回傳 dispose 函式（解除全部訂閱）
  return () => {
    eventBus.off('mission:completed', onMissionCompleted as any)
    eventBus.off('adventurer:died', onAdventurerDied)
    eventBus.off('adventurer:wounded', onAdventurerWounded)
    eventBus.off('recruit:success', onRecruitSuccess)
    eventBus.off('building:upgraded', onBuildingUpgraded)
    eventBus.off('staff:hired', onStaffHired)
    eventBus.off('guild:level_up', onGuildLevelUp as any)
    eventBus.off('danger:level_changed', onDangerLevelChanged as any)
  }
}
