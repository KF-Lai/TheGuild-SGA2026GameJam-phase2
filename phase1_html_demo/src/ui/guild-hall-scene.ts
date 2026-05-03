// guild-hall-scene.ts — Guild Hall Main UI (v2 Redesign)
// Layout: 16:9 responsive, modern card design
// Replaces shell.ts as the primary game UI.
//
// Architecture:
//   - InfoBar (top): guild stats + current time
//   - LeftPanel (26%): guild master office, desk, milestone, counter, tools
//   - RightPanel (74%): commission board (horizontal scroll) + adventurer hall (grid)
//   - LogBar (bottom): colored message log
//   - Toolbar: save/export/import/cheat
//   - Modal panels: A (review), B (recommend), E (construction), G (guild master mgmt)
//   - Settlement overlay: small centered card popup

import type { Mission, Adventurer, DispatchRecord, OutcomeResult, OfflineReturnEvent } from '../types'
import { UI_STRINGS } from './ui-strings'
import { renderCheatButton } from './cheat-menu'
import type { CheatCallbacks } from './cheat-menu'
import { getReputationLabel } from '../systems/resource'
import { PROFESSION_TRAITS } from '../data/traits'
import { GROWTH_TRAITS, nextXPMilestone, XP_MILESTONES } from '../data/growth-traits'
import { RACE_MODIFIERS } from '../data/races'
import { calcPartyRates, maxPartySize } from '../systems/dispatch'
import { recruitCost } from '../systems/adventurer'

export type { CheatCallbacks }

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

export const MAX_DEBT = -500
const MAX_MSG_LINES = 5
const QUEST_BOARD_MAX = 20
const ROSTER_DISPLAY_MAX = 15
const REVIEW_PANEL_MAX = 8

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface GuildHallData {
  gold: number
  pendingGold: number
  reputation: number
  guildLevel: number
  worldDanger: string
  commissions: Mission[]
  pendingReview: Mission[]
  roster: Adventurer[]
  activeMissions: DispatchRecord[]
  pendingResults: OutcomeResult[]
  candidateAdventurers: Adventurer[]
  messageLog: string[]
  longOfflineDays?: number
}

export interface GuildHallCallbacks {
  onDispatch: (missionId: string, adventurerId: string) => void
  onPartyDispatch: (missionId: string, adventurerIds: string[]) => void
  onReviewAccept: (missionId: string) => void
  onReviewReject: (missionId: string) => void
  onRecruit: (adventurerId: string) => void
  onFire: (adventurerId: string) => void
  onSettlementDismiss: () => void
  onSave: () => void
  onExport: () => void
  onImport: (file: File) => void
  cheatCallbacks?: CheatCallbacks
}

// ---------------------------------------------------------------------------
// Module-level state
// ---------------------------------------------------------------------------

let _root: HTMLElement | null = null
let _callbacks: GuildHallCallbacks | null = null
let _lastData: GuildHallData | null = null

let _recommendMissionId: string | null = null
let _recommendPartyIds: string[] = []
let _fireCandidateId: string | null = null
let _recruitCandidateId: string | null = null
let _messageLog: string[] = []
let _fullMessageLog: string[] = []
let _logExpanded = false

// Re-render survival registry — functions registered here are called after every DOM rebuild.
// To keep a panel/card open across every-second re-renders:
//   open:  _registerRestore('my-key', () => { /* re-show logic */ })
//   close: _unregisterRestore('my-key')
const _restoreRegistry = new Map<string, () => void>()
function _registerRestore(key: string, fn: () => void): void { _restoreRegistry.set(key, fn) }
function _unregisterRestore(key: string): void { _restoreRegistry.delete(key) }

let _cheatMenuOpen = false
let _floatingAdvCardPos: { top: number; left: number } | null = null

// Recommend-panel notification badge
let _recommendNotify = false
let _prevMissionCount = -1
let _prevIdleCount = -1
let _floatingCommCardPos: { top: number; left: number } | null = null

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export function initGuildHall(appEl: HTMLElement, callbacks: GuildHallCallbacks): void {
  _root = appEl
  _callbacks = callbacks
  _recommendMissionId = null
  _recommendPartyIds = []
  _fireCandidateId = null
  _recruitCandidateId = null
  _restoreRegistry.clear()
  _cheatMenuOpen = false
  _messageLog = []
  _recommendNotify = false
  _prevMissionCount = -1
  _prevIdleCount = -1

  _root.innerHTML = ''
  _root.style.cssText = [
    'position:relative',
    'width:100%',
    'height:100vh',
    'min-height:560px',
    'background:#0a0a0f',
    'color:#c8b98a',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'overflow:hidden',
    'display:flex',
    'flex-direction:column',
  ].join(';')

  const loading = document.createElement('div')
  loading.style.cssText = 'padding:24px;text-align:center;opacity:0.6;font-size:16px'
  loading.textContent = UI_STRINGS.hallLoading
  _root.appendChild(loading)
}

export function renderGuildHall(data: GuildHallData): void {
  if (!_root || !_callbacks) return
  _lastData = data
  _messageLog = data.messageLog.slice(0, MAX_MSG_LINES)
  _fullMessageLog = data.messageLog.slice(0, 30)

  if (data.pendingResults.length > 0) {
    _recommendMissionId = null
    _recommendPartyIds = []
  }

  // Detect new missions or newly idle adventurers → trigger recommend badge
  const curMissionCount = data.commissions.length
  const curIdleCount = data.roster.filter(a => a.status === 'idle').length
  const hasPair = curMissionCount > 0 && curIdleCount > 0
  if (hasPair && (_prevMissionCount >= 0 || _prevIdleCount >= 0)) {
    if (curMissionCount > _prevMissionCount || curIdleCount > _prevIdleCount) {
      _recommendNotify = true
    }
  }
  _prevMissionCount = curMissionCount
  _prevIdleCount = curIdleCount

  _root.innerHTML = ''

  const scene = document.createElement('div')
  scene.id = 'guild-hall-scene'
  scene.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'width:100%',
    'height:100%',
    'position:relative',
    'overflow:hidden',
  ].join(';')

  scene.appendChild(buildInfoBar(data))

  const mainArea = document.createElement('div')
  mainArea.style.cssText = [
    'display:flex',
    'flex:1',
    'overflow:hidden',
    'min-height:0',
    'gap:0',
  ].join(';')
  mainArea.appendChild(buildLeftPanel(data))
  mainArea.appendChild(buildRightPanel(data))
  scene.appendChild(mainArea)

  scene.appendChild(buildLogBar(data))
  scene.appendChild(buildToolbar())

  // Modals
  scene.appendChild(buildPanelA(data))
  scene.appendChild(buildPanelB(data))
  scene.appendChild(buildPanelE())
  scene.appendChild(buildPanelG(data))

  if (data.pendingResults.length > 0) {
    scene.appendChild(buildSettlementOverlay(data.pendingResults, data.longOfflineDays))
  }

  _root.appendChild(scene)

  // Restore all registered panels / overlays / floating cards after DOM rebuild
  for (const fn of _restoreRegistry.values()) fn()
}

function _appendOfflineReturnOverlay(event: OfflineReturnEvent, scene: HTMLElement): void {
  document.getElementById('offline-return-overlay')?.remove()

  const backdrop = document.createElement('div')
  backdrop.id = 'offline-return-overlay'
    backdrop.style.cssText = [
      'position:absolute', 'inset:0',
      'background:rgba(0,0,0,0.7)',
      'display:flex', 'align-items:center', 'justify-content:center',
      'z-index:200',
    ].join(';')

    const card = document.createElement('div')
    card.style.cssText = [
      'background:#141420', 'border:1px solid #4a4a6a', 'border-radius:10px',
      'width:min(520px,90vw)', 'max-height:70vh',
      'display:flex', 'flex-direction:column',
      'box-shadow:0 16px 60px rgba(0,0,0,0.9)', 'overflow:hidden',
    ].join(';')

    // Header
    const offlineMin = Math.round(event.offlineDurationMs / 60_000)
    const header = document.createElement('div')
    header.style.cssText = 'padding:14px 20px;border-bottom:1px solid #2a2a3a;background:#1a1a28;flex-shrink:0'
    header.innerHTML = [
      `<div style="font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px">📬 信使歸來</div>`,
      `<div style="font-size:13px;color:#888;margin-top:4px">您離開了 ${offlineMin} 分鐘`,
      event.candidatePoolRefreshed ? ' ｜ 招募候補已更新' : '',
      `</div>`,
    ].join('')
    card.appendChild(header)

    // Resolutions list
    const list = document.createElement('div')
    list.style.cssText = 'flex:1;overflow-y:auto;padding:12px 20px;display:flex;flex-direction:column;gap:6px'

    if (event.resolutions.length === 0) {
      const empty = document.createElement('div')
      empty.style.cssText = 'color:#555;font-size:14px;padding:8px 0'
      empty.textContent = '離線期間無任務結算。'
      list.appendChild(empty)
    } else {
      event.resolutions.forEach(r => {
        const row = document.createElement('div')
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:8px 10px;border:1px solid #2a2a3a;border-radius:5px;background:#131318;font-size:14px'
        const badgeColor = r.outcome === 'SUCCESS' ? '#a8c890' : r.outcome === 'PYRRHIC' ? '#ee9944' : '#ee6644'
        const badgeText = r.outcome === 'SUCCESS' ? '成功' : r.outcome === 'PYRRHIC' ? '慘勝' : r.outcome === 'DEATH' ? '陣亡' : '失敗'
        const isRSuccess = r.outcome === 'SUCCESS' || r.outcome === 'PYRRHIC'
        const rGoldAbs = Math.abs(r.goldDelta)
        const goldStr = isRSuccess ? `+${rGoldAbs}g` : `-${rGoldAbs}g`
        row.innerHTML = [
          `<span style="color:${badgeColor};font-weight:bold;flex-shrink:0">[${badgeText}]</span>`,
          `<span style="flex:1;color:#c8b98a">${r.adventurerName} — ${r.missionName}</span>`,
          `<span style="color:${isRSuccess ? '#6ac86a' : '#ee6644'};font-weight:bold">${goldStr}</span>`,
        ].join('')
        list.appendChild(row)
      })
    }
    card.appendChild(list)

    // --- NPC Active Missions (State 1: still running) ---
    if (event.npcActiveMissions.length > 0) {
      const npcActiveSection = document.createElement('div')
      npcActiveSection.style.cssText = 'padding:10px 20px;border-top:1px solid #2a2a3a;flex-shrink:0'

      const npcActiveHeader = document.createElement('div')
      npcActiveHeader.style.cssText = 'font-size:13px;color:#7a9ac0;font-weight:bold;margin-bottom:6px;letter-spacing:0.5px'
      npcActiveHeader.textContent = '[ 仍在任務中 ]'
      npcActiveSection.appendChild(npcActiveHeader)

      for (const m of event.npcActiveMissions) {
        const remainMin = Math.floor(m.remainingMs / 60_000)
        const remainH   = Math.floor(remainMin / 60)
        const remainM   = remainMin % 60
        const timeStr   = remainH > 0 ? `剩 ${remainH}h ${remainM}m` : `剩 ${remainM}m`

        const row = document.createElement('div')
        row.style.cssText = 'font-size:13px;color:#8899bb;padding:3px 0'
        row.textContent = `\u2694 ${m.adventurerName} \u2014 ${m.missionName}\u3000${timeStr}`
        npcActiveSection.appendChild(row)
      }

      list.appendChild(npcActiveSection)
    }

    // --- NPC Resolved Missions (State 2: completed offline) ---
    if (event.npcResolutions.length > 0) {
      const npcResolvedSection = document.createElement('div')
      npcResolvedSection.style.cssText = 'padding:10px 20px;border-top:1px solid #2a2a3a;flex-shrink:0'

      const npcResolvedHeader = document.createElement('div')
      npcResolvedHeader.style.cssText = 'font-size:13px;color:#9a8060;font-weight:bold;margin-bottom:6px;letter-spacing:0.5px'
      npcResolvedHeader.textContent = '[ NPC 自主結算 ]'
      npcResolvedSection.appendChild(npcResolvedHeader)

      for (const r of event.npcResolutions) {
        const row = document.createElement('div')
        row.style.cssText = 'font-size:13px;padding:3px 0'
        if (r.outcome === 'SUCCESS') {
          row.style.color = '#a8c890'
          row.textContent = `\u2714 ${r.adventurerName} \u5b8c\u6210\u300c${r.missionName}\u300d +${r.goldDelta}g`
        } else {
          row.style.color = '#cc7755'
          row.textContent = `\u2718 ${r.adventurerName} \u5931\u6557\u300c${r.missionName}\u300d`
        }
        npcResolvedSection.appendChild(row)
      }

      list.appendChild(npcResolvedSection)
    }

    // Bonus banner
    if (event.bonusCommissionEligible) {
      const banner = document.createElement('div')
      if (event.bonusCommissionActive) {
        banner.style.cssText = 'padding:10px 20px;background:#1a2a1a;border-top:1px solid #3a5a3a;font-size:14px;color:#a8c890;flex-shrink:0'
        banner.textContent = `🎁 回歸獎勵：+${event.bonusCommissionAmount}g 已入帳`
      } else {
        banner.style.cssText = 'padding:10px 20px;background:#1a1a1a;border-top:1px solid #3a3a3a;font-size:13px;color:#666;flex-shrink:0'
        banner.textContent = '⌛ 回歸獎勵已過期（超過 30 分鐘）'
      }
      card.appendChild(banner)
    }

    // Footer
    const footer = document.createElement('div')
    footer.style.cssText = 'padding:12px 20px;border-top:1px solid #2a2a3a;text-align:right;flex-shrink:0;background:#1a1a28'
    const confirmBtn = document.createElement('button')
    confirmBtn.style.cssText = 'padding:8px 24px;background:#2a2a4a;color:#c8b98a;border:1px solid #c8b98a;border-radius:5px;font-size:14px;cursor:pointer'
    confirmBtn.textContent = '確認'
    confirmBtn.addEventListener('click', () => {
      backdrop.remove()
      _unregisterRestore('offline-return')
    })
    footer.appendChild(confirmBtn)
    card.appendChild(footer)

    backdrop.appendChild(card)
    scene.appendChild(backdrop)
}

export function showOfflineReturnOverlay(event: OfflineReturnEvent): void {
  _registerRestore('offline-return', () => {
    const scene = document.getElementById('guild-hall-scene') ?? _root
    if (scene) _appendOfflineReturnOverlay(event, scene)
  })
  // Wait for DOM to be ready (called before first renderGuildHall)
  requestAnimationFrame(() => {
    const scene = document.getElementById('guild-hall-scene') ?? _root
    if (!scene) return
    _appendOfflineReturnOverlay(event, scene)
  })
}

export function pushMessage(msg: string): void {
  _messageLog.unshift(msg)
  if (_messageLog.length > MAX_MSG_LINES) _messageLog = _messageLog.slice(0, MAX_MSG_LINES)
  _fullMessageLog.unshift(msg)
  if (_fullMessageLog.length > 30) _fullMessageLog = _fullMessageLog.slice(0, 30)
  if (_lastData) {
    _lastData = { ..._lastData, messageLog: [..._fullMessageLog] }
    renderGuildHall(_lastData)
  }
}

// ---------------------------------------------------------------------------
// Info Bar (top)
// ---------------------------------------------------------------------------

function buildInfoBar(data: GuildHallData): HTMLElement {
  const bar = document.createElement('div')
  bar.style.cssText = [
    'display:flex',
    'align-items:center',
    'gap:20px',
    'padding:8px 20px',
    'background:#12121a',
    'border-bottom:1px solid #2a2a3a',
    'flex-shrink:0',
    'flex-wrap:wrap',
  ].join(';')

  // Guild name / level
  const guildName = document.createElement('span')
  guildName.style.cssText = 'font-weight:bold;font-size:15px;color:#e8d8a0;letter-spacing:1px'
  guildName.textContent = `【 公會 Lv${data.guildLevel} 】`
  bar.appendChild(guildName)

  const sep = () => {
    const s = document.createElement('span')
    s.style.cssText = 'color:#444;font-size:14px'
    s.textContent = '|'
    return s
  }

  // Gold
  const goldSpan = document.createElement('span')
  const isBankrupt = data.gold < MAX_DEBT
  if (isBankrupt) {
    goldSpan.style.cssText = 'color:#ff4444;animation:blink 1s step-end infinite;font-size:14px'
    goldSpan.textContent = `金幣: ${data.gold}g ${UI_STRINGS.statusBankruptWarn}`
  } else if (data.gold < 0) {
    goldSpan.style.cssText = 'color:#ff8844;font-size:14px'
    goldSpan.textContent = `金幣: ${data.gold}g ${UI_STRINGS.statusDebt}`
  } else {
    goldSpan.style.cssText = 'color:#f0c060;font-size:14px'
    goldSpan.textContent = `金幣: ${data.gold}g`
  }
  bar.appendChild(sep()); bar.appendChild(goldSpan)

  // Pending gold
  if (data.pendingGold > 0) {
    const pendingSpan = document.createElement('span')
    pendingSpan.style.cssText = 'color:#a8c890;font-size:14px'
    pendingSpan.textContent = `預收: ${data.pendingGold}g`
    bar.appendChild(sep()); bar.appendChild(pendingSpan)
  }

  // Reputation
  const repSpan = document.createElement('span')
  repSpan.style.cssText = 'font-size:14px'
  repSpan.textContent = `聲望: ${getReputationLabel(data.reputation)}`
  bar.appendChild(sep()); bar.appendChild(repSpan)

  // Active missions count
  const activeCnt = document.createElement('span')
  activeCnt.style.cssText = 'font-size:14px;color:#90b8d0'
  activeCnt.textContent = `執行中: ${data.activeMissions.length} 件`
  bar.appendChild(sep()); bar.appendChild(activeCnt)

  // Spacer
  const spacer = document.createElement('div')
  spacer.style.cssText = 'flex:1'
  bar.appendChild(spacer)

  // Current time
  const now = new Date()
  const timeStr = now.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const timeSpan = document.createElement('span')
  timeSpan.style.cssText = 'font-size:14px;color:#888;font-family:monospace'
  timeSpan.textContent = timeStr
  bar.appendChild(timeSpan)

  return bar
}

// ---------------------------------------------------------------------------
// Left Panel (26%)
// ---------------------------------------------------------------------------

function buildLeftPanel(data: GuildHallData): HTMLElement {
  const panel = document.createElement('div')
  panel.style.cssText = [
    'width:26%',
    'min-width:200px',
    'display:flex',
    'flex-direction:column',
    'background:#0d0d14',
    'border-right:1px solid #2a2a3a',
    'overflow-y:auto',
    'flex-shrink:0',
    'gap:2px',
    'padding:10px 10px',
  ].join(';')

  // Section label
  const officeLabel = document.createElement('div')
  officeLabel.style.cssText = 'font-size:12px;color:#555;text-transform:uppercase;letter-spacing:2px;margin-bottom:6px'
  officeLabel.textContent = UI_STRINGS.hallOffice
  panel.appendChild(officeLabel)

  // Guild master card (click → Panel G)
  const gmCard = buildSideCard(
    UI_STRINGS.hallGuildMaster,
    '冒險者名單管理',
    '#8a7a4a',
    () => { rebuildPanelG(data); showPanel('panel-g') }
  )
  panel.appendChild(gmCard)

  // Desk card (click → Panel A: review commissions)
  const pendingCount = data.pendingReview.length
  const deskCard = buildSideCard(
    UI_STRINGS.hallDesk,
    `待審核委託: ${pendingCount > 0 ? `<span style="color:#f0a060;font-weight:bold">${pendingCount}</span>` : '0'} 件`,
    pendingCount > 0 ? '#6a4a2a' : '#2a2a2a',
    () => { rebuildPanelA(data); showPanel('panel-a') },
    true
  )
  panel.appendChild(deskCard)

  // Divider
  const div1 = document.createElement('div')
  div1.style.cssText = 'border-top:1px solid #222;margin:8px 0'
  panel.appendChild(div1)

  // Milestone section
  const milestoneLabel = document.createElement('div')
  milestoneLabel.style.cssText = 'font-size:12px;color:#555;text-transform:uppercase;letter-spacing:2px;margin-bottom:6px'
  milestoneLabel.textContent = '狀態'
  panel.appendChild(milestoneLabel)

  const milestoneCard = buildMilestoneCard(data)
  panel.appendChild(milestoneCard)

  // Divider
  const div2 = document.createElement('div')
  div2.style.cssText = 'border-top:1px solid #222;margin:8px 0'
  panel.appendChild(div2)

  // Reception / recommend (click → Panel B)
  const counterLabel = document.createElement('div')
  counterLabel.style.cssText = 'font-size:12px;color:#555;text-transform:uppercase;letter-spacing:2px;margin-bottom:6px'
  counterLabel.textContent = UI_STRINGS.hallReception
  panel.appendChild(counterLabel)

  const counterCard = buildSideCard(
    UI_STRINGS.hallReceptionCounter,
    `空閒: ${data.roster.filter(a => a.status === 'idle').length} 人 | 委託: ${data.commissions.filter(m => !data.activeMissions.some(r => r.missionId === m.id)).length} 件`,
    '#1a2a1a',
    () => {
      _recommendNotify = false
      _recommendMissionId = null
      _recommendPartyIds = []
      rebuildPanelB(data)
      showPanel('panel-b')
    }
  )
  counterCard.style.position = 'relative'
  if (_recommendNotify) {
    const badge = document.createElement('div')
    badge.style.cssText = [
      'position:absolute',
      'top:50%',
      'right:8px',
      'transform:translateY(-50%)',
      'width:18px',
      'height:18px',
      'border-radius:50%',
      'background:#d03030',
      'color:#fff',
      'font-size:12px',
      'font-weight:bold',
      'display:flex',
      'align-items:center',
      'justify-content:center',
      'pointer-events:none',
    ].join(';')
    badge.textContent = '!'
    counterCard.appendChild(badge)
  }
  panel.appendChild(counterCard)

  // Divider
  const div3 = document.createElement('div')
  div3.style.cssText = 'border-top:1px solid #222;margin:8px 0'
  panel.appendChild(div3)

  // Tools room
  const toolsLabel = document.createElement('div')
  toolsLabel.style.cssText = 'font-size:12px;color:#555;text-transform:uppercase;letter-spacing:2px;margin-bottom:6px'
  toolsLabel.textContent = '工具室'
  panel.appendChild(toolsLabel)

  const toolsCard = buildSideCard(
    UI_STRINGS.hallBackDoor,
    '點擊進入公會建設',
    '#1a1a2a',
    () => showPanel('panel-e')
  )
  panel.appendChild(toolsCard)

  return panel
}

function buildSideCard(
  title: string,
  subtitle: string,
  borderColor: string,
  onClick: () => void,
  htmlSubtitle = false
): HTMLElement {
  const card = document.createElement('div')
  card.style.cssText = [
    'padding:10px 14px',
    'cursor:pointer',
    `border:1px solid ${borderColor}`,
    'border-radius:6px',
    'background:#131318',
    'margin-bottom:6px',
    'transition:background 0.15s',
    'user-select:none',
  ].join(';')

  card.addEventListener('mouseenter', () => { card.style.background = '#1c1c28' })
  card.addEventListener('mouseleave', () => { card.style.background = '#131318' })

  const titleEl = document.createElement('div')
  titleEl.style.cssText = 'font-size:14px;font-weight:bold;color:#d8c890;margin-bottom:4px'
  titleEl.textContent = title
  card.appendChild(titleEl)

  const subEl = document.createElement('div')
  subEl.style.cssText = 'font-size:13px;color:#888'
  if (htmlSubtitle) subEl.innerHTML = subtitle
  else subEl.textContent = subtitle
  card.appendChild(subEl)

  card.addEventListener('click', onClick)
  return card
}

function buildMilestoneCard(data: GuildHallData): HTMLElement {
  const card = document.createElement('div')
  card.style.cssText = [
    'padding:10px 14px',
    'border:1px solid #2a2a3a',
    'border-radius:6px',
    'background:#131318',
    'margin-bottom:6px',
    'font-size:14px',
  ].join(';')

  const rows: [string, string][] = [
    ['冒險者人數', `${data.roster.filter(a => a.status !== 'dead').length} / 15`],
    ['任務完成今日', `${data.activeMissions.length} 件執行中`],
    ['公會等級', `Lv ${data.guildLevel}`],
    ['世界危險度', `${data.worldDanger}`],
  ]

  rows.forEach(([label, value]) => {
    const row = document.createElement('div')
    row.style.cssText = 'display:flex;justify-content:space-between;padding:3px 0;border-bottom:1px solid #1a1a24;font-size:14px'
    row.innerHTML = `<span style="color:#888">${label}</span><span style="color:#c8b98a">${value}</span>`
    card.appendChild(row)
  })

  return card
}

// ---------------------------------------------------------------------------
// Right Panel (74%)
// ---------------------------------------------------------------------------

function buildRightPanel(data: GuildHallData): HTMLElement {
  const panel = document.createElement('div')
  panel.style.cssText = [
    'flex:1',
    'display:flex',
    'flex-direction:column',
    'overflow:hidden',
    'min-width:0',
  ].join(';')

  panel.appendChild(buildCommissionBoard(data))
  panel.appendChild(buildAdventurerHall(data))

  return panel
}

// ---------------------------------------------------------------------------
// Commission Board (top of right panel, horizontal scroll)
// ---------------------------------------------------------------------------

function buildCommissionBoard(data: GuildHallData): HTMLElement {
  const section = document.createElement('div')
  section.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'padding:10px 16px 6px',
    'border-bottom:1px solid #2a2a3a',
    'flex-shrink:0',
    'background:#0d0d14',
  ].join(';')

  // Header row
  const header = document.createElement('div')
  header.style.cssText = 'display:flex;align-items:center;margin-bottom:8px;gap:12px'

  const title = document.createElement('div')
  title.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px'
  title.textContent = `【 ${UI_STRINGS.hallQuestBoard} 】`
  header.appendChild(title)

  const hint = document.createElement('div')
  hint.style.cssText = 'font-size:13px;color:#555'
  header.appendChild(hint)

  const countBadge = document.createElement('div')
  countBadge.style.cssText = 'margin-left:auto;font-size:13px;color:#888'
  countBadge.textContent = `${data.commissions.length} 件`
  header.appendChild(countBadge)
  section.appendChild(header)

  // Horizontal scroll container
  const scrollRow = document.createElement('div')
  scrollRow.style.cssText = [
    'display:flex',
    'gap:10px',
    'overflow-x:auto',
    'padding-bottom:8px',
    'scrollbar-width:thin',
    'scrollbar-color:#333 #111',
  ].join(';')

  const visible = data.commissions.slice(0, QUEST_BOARD_MAX)
  const hasIdle = data.roster.some(a => a.status === 'idle')

  if (visible.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'opacity:0.4;padding:16px;font-size:14px;color:#888'
    empty.textContent = UI_STRINGS.commissionEmpty
    scrollRow.appendChild(empty)
  } else {
    visible.forEach(mission => {
      const isActive = data.activeMissions.some(r => r.missionId === mission.id)
      scrollRow.appendChild(buildCommissionCard(mission, isActive, hasIdle))
    })
  }

  section.appendChild(scrollRow)
  return section
}

function buildCommissionCard(mission: Mission, isActive: boolean, hasIdleAdventurer: boolean): HTMLElement {
  const card = document.createElement('div')
  card.dataset.missionId = mission.id

  const diffColor = difficultyColor(mission.difficulty)
  card.style.cssText = [
    'flex-shrink:0',
    'width:160px',
    'padding:10px 12px',
    'border:1px solid #2a2a3a',
    'border-radius:6px',
    'background:#131318',
    'cursor:pointer',
    'position:relative',
    'user-select:none',
    isActive ? 'opacity:0.45' : '',
  ].filter(Boolean).join(';')

  card.addEventListener('mouseenter', () => {
    if (!isActive) card.style.borderColor = '#4a4a6a'
  })
  card.addEventListener('mouseleave', () => {
    card.style.borderColor = '#2a2a3a'
  })

  // Difficulty badge
  const badge = document.createElement('div')
  badge.style.cssText = [
    'display:inline-block',
    'padding:2px 8px',
    'border-radius:3px',
    `background:${diffColor}22`,
    `border:1px solid ${diffColor}`,
    `color:${diffColor}`,
    'font-size:12px',
    'font-weight:bold',
    'margin-bottom:6px',
    'letter-spacing:1px',
  ].join(';')
  badge.textContent = mission.difficulty
  card.appendChild(badge)

  // Tier tag
  if (mission.tier && mission.tier !== 'common') {
    const tierTag = document.createElement('span')
    const tierColor = mission.tier === 'gold' ? '#f0c060' : '#aaaaee'
    const tierLabel = mission.tier === 'gold' ? '金' : '銀'
    tierTag.style.cssText = [
      'display:inline-block',
      'padding:1px 5px',
      'border-radius:3px',
      `background:${tierColor}22`,
      `border:1px solid ${tierColor}`,
      `color:${tierColor}`,
      'font-size:11px',
      'font-weight:bold',
      'margin-right:4px',
      'vertical-align:middle',
    ].join(';')
    tierTag.textContent = tierLabel
    card.appendChild(tierTag)
  }

  // Mission name
  const nameEl = document.createElement('div')
  nameEl.style.cssText = [
    'font-size:14px',
    'font-weight:bold',
    `color:${mission.isTemplate ? '#7a9ab5' : '#d8c890'}`,
    'margin-bottom:6px',
    'line-height:1.3',
    'overflow:hidden',
    'display:-webkit-box',
    '-webkit-line-clamp:2',
    '-webkit-box-orient:vertical',
  ].join(';')
  nameEl.textContent = mission.name
  card.appendChild(nameEl)

  // Type
  const typeEl = document.createElement('div')
  typeEl.style.cssText = 'font-size:13px;color:#888;margin-bottom:2px'
  typeEl.textContent = mission.type
  card.appendChild(typeEl)

  // Reward
  const rewardEl = document.createElement('div')
  rewardEl.style.cssText = 'font-size:14px;color:#f0c060;font-weight:bold;margin-top:4px'
  rewardEl.textContent = `${mission.baseReward}g`
  card.appendChild(rewardEl)

  // Active indicator
  if (isActive) {
    const activeEl = document.createElement('div')
    activeEl.style.cssText = 'font-size:12px;color:#90b8d0;margin-top:4px'
    activeEl.textContent = '執行中...'
    card.appendChild(activeEl)
  } else if (!hasIdleAdventurer) {
    const noAdvEl = document.createElement('div')
    noAdvEl.style.cssText = 'font-size:12px;color:#666;margin-top:4px'
    noAdvEl.textContent = '無閒置傭兵'
    card.appendChild(noAdvEl)
  }

  card.addEventListener('click', (e) => {
    e.stopPropagation()
    showFloatingCommissionCard(mission, card)
  })

  return card
}

function difficultyColor(diff: string): string {
  switch (diff) {
    case 'F': return '#888888'
    case 'E': return '#66aa66'
    case 'D': return '#6688cc'
    case 'C': return '#aa88ee'
    case 'B': return '#ee8844'
    case 'A': return '#ee4444'
    case 'S': return '#ffcc00'
    case 'SS': return '#ff8800'
    case 'SSS': return '#ff4488'
    default: return '#888888'
  }
}

// ---------------------------------------------------------------------------
// Adventurer Hall (grid)
// ---------------------------------------------------------------------------

function buildAdventurerHall(data: GuildHallData): HTMLElement {
  const section = document.createElement('div')
  section.style.cssText = [
    'flex:1',
    'display:flex',
    'flex-direction:column',
    'padding:10px 16px',
    'overflow:hidden',
    'min-height:0',
    'background:#0a0a0f',
  ].join(';')

  // Header
  const header = document.createElement('div')
  header.style.cssText = 'display:flex;align-items:center;gap:12px;margin-bottom:8px;flex-shrink:0'

  const title = document.createElement('div')
  title.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px'
  title.textContent = '【 冒險者大廳 】'
  header.appendChild(title)

  const alive = data.roster.filter(a => a.status !== 'dead')
  const onMission = alive.filter(a => a.status === 'on_mission').length
  const idle = alive.filter(a => a.status === 'idle').length

  const stats = document.createElement('div')
  stats.style.cssText = 'font-size:13px;color:#888'
  stats.innerHTML = `共 <span style="color:#c8b98a">${alive.length}</span> 名 ｜ 待命 <span style="color:#a8c890">${idle}</span> ｜ 任務中 <span style="color:#90b8d0">${onMission}</span>`
  header.appendChild(stats)

  section.appendChild(header)

  // Grid
  const grid = document.createElement('div')
  grid.style.cssText = [
    'display:grid',
    'grid-template-columns:repeat(3, 1fr)',
    'gap:8px',
    'overflow-y:auto',
    'flex:1',
    'min-height:0',
    'align-content:start',
    'padding-right:4px',
  ].join(';')

  const visible = alive.slice(0, ROSTER_DISPLAY_MAX)

  if (visible.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'grid-column:span 3;opacity:0.4;padding:24px;text-align:center;font-size:14px'
    empty.textContent = UI_STRINGS.rosterEmpty
    grid.appendChild(empty)
  } else {
    visible.forEach(adv => {
      grid.appendChild(buildAdventurerCard(adv, data))
    })
  }

  section.appendChild(grid)
  return section
}

function formatCountdown(endTimestamp: number): string {
  const remaining = Math.max(0, endTimestamp - Date.now())
  const totalSeconds = Math.floor(remaining / 1000)
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

function buildAdventurerCard(adv: Adventurer, data: GuildHallData): HTMLElement {
  const isOnMission = adv.status === 'on_mission'
  const profName = PROFESSION_TRAITS[adv.professionId].name
  const dispatch = isOnMission
    ? data.activeMissions.find(r => r.adventurerId === adv.id)
    : undefined

  const card = document.createElement('div')
  card.dataset.advId = adv.id
  card.style.cssText = [
    'padding:10px 12px',
    'border:1px solid #2a2a3a',
    'border-radius:6px',
    isOnMission ? 'background:#0d1420' : 'background:#131318',
    'cursor:pointer',
    'user-select:none',
    'position:relative',
    'overflow:hidden',
  ].join(';')

  card.addEventListener('mouseenter', () => {
    card.style.borderColor = isOnMission ? '#2a4a6a' : '#4a4a6a'
  })
  card.addEventListener('mouseleave', () => {
    card.style.borderColor = '#2a2a3a'
  })

  // Mission countdown bar (if on mission)
  if (dispatch) {
    const countdown = formatCountdown(dispatch.endTimestamp)
    const timerBar = document.createElement('div')
    timerBar.style.cssText = [
      'position:absolute',
      'top:0',
      'left:0',
      'right:0',
      'background:#1a2a3a',
      'padding:3px 8px',
      'font-size:13px',
      'color:#90b8d0',
      'display:flex',
      'align-items:center',
      'gap:6px',
      'border-bottom:1px solid #2a3a4a',
    ].join(';')
    timerBar.innerHTML = `<span style="opacity:0.7">⏱</span> <span style="font-family:monospace;font-weight:bold">${countdown}</span>`
    card.appendChild(timerBar)
    card.style.paddingTop = '28px'
  }

  // Rank badge
  const rankBadge = document.createElement('div')
  rankBadge.style.cssText = [
    'display:inline-block',
    'padding:1px 7px',
    'border-radius:3px',
    'font-size:12px',
    'font-weight:bold',
    `background:${rankColor(adv.rank)}22`,
    `border:1px solid ${rankColor(adv.rank)}`,
    `color:${rankColor(adv.rank)}`,
    'margin-bottom:5px',
    'letter-spacing:1px',
  ].join(';')
  rankBadge.textContent = adv.rank
  card.appendChild(rankBadge)

  // Name + Race
  const nameEl = document.createElement('div')
  nameEl.style.cssText = 'display:flex;align-items:baseline;gap:6px;margin-bottom:3px;overflow:hidden'
  const nameSpan = document.createElement('span')
  nameSpan.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap'
  nameSpan.textContent = adv.name
  nameEl.appendChild(nameSpan)
  const raceName = adv.raceId ? (RACE_MODIFIERS[adv.raceId]?.name ?? '') : ''
  if (raceName) {
    const raceSpan = document.createElement('span')
    raceSpan.style.cssText = 'font-size:12px;color:#666;white-space:nowrap;flex-shrink:0'
    raceSpan.textContent = raceName
    nameEl.appendChild(raceSpan)
  }
  card.appendChild(nameEl)

  // Profession
  const profEl = document.createElement('div')
  profEl.style.cssText = 'font-size:13px;color:#888'
  profEl.textContent = profName
  card.appendChild(profEl)

  // Status
  const statusEl = document.createElement('div')
  statusEl.style.cssText = `font-size:13px;margin-top:4px;color:${isOnMission ? '#90b8d0' : '#a8c890'}`
  statusEl.textContent = isOnMission
    ? (dispatch ? `▶ ${dispatch.missionName.slice(0, 10)}...` : '任務中')
    : '待命中'
  card.appendChild(statusEl)

  // XP bar
  const xp = adv.xp ?? 0
  const nextMilestone = nextXPMilestone(xp)
  const prevMilestone = [...XP_MILESTONES].reverse().find(m => m.xpRequired <= xp)?.xpRequired ?? 0
  const xpInSegment   = xp - prevMilestone
  const segmentSize   = nextMilestone === Infinity ? 1 : nextMilestone - prevMilestone
  const xpPct         = nextMilestone === Infinity ? 100 : Math.min(100, Math.round((xpInSegment / segmentSize) * 100))

  const xpRow = document.createElement('div')
  xpRow.style.cssText = 'margin-top:6px'

  const xpLabel = document.createElement('div')
  xpLabel.style.cssText = 'font-size:11px;color:#666;margin-bottom:2px'
  xpLabel.textContent = nextMilestone === Infinity
    ? `XP ${xp}  (滿)`
    : `XP ${xp} / ${nextMilestone}`
  xpRow.appendChild(xpLabel)

  const xpTrack = document.createElement('div')
  xpTrack.style.cssText = [
    'height:4px',
    'border-radius:2px',
    'background:#1e1e2a',
    'overflow:hidden',
  ].join(';')
  const xpFill = document.createElement('div')
  xpFill.style.cssText = [
    `width:${xpPct}%`,
    'height:100%',
    'border-radius:2px',
    'background:#6a9a6a',
    'transition:width 0.3s',
  ].join(';')
  xpTrack.appendChild(xpFill)
  xpRow.appendChild(xpTrack)
  card.appendChild(xpRow)

  // Growth trait pills
  const traits = adv.growthTraits ?? []
  if (traits.length > 0) {
    const pillRow = document.createElement('div')
    pillRow.style.cssText = 'display:flex;flex-wrap:wrap;gap:3px;margin-top:5px'
    for (const gt of traits) {
      const def = GROWTH_TRAITS[gt.traitId]
      if (!def) continue
      const pill = document.createElement('span')
      pill.style.cssText = [
        'display:inline-block',
        'padding:1px 5px',
        'border-radius:3px',
        'font-size:10px',
        'background:#1a2a1a',
        'border:1px solid #3a5a3a',
        'color:#8ac88a',
        'cursor:default',
      ].join(';')
      pill.textContent = def.name
      pill.title = def.description
      pillRow.appendChild(pill)
    }
    card.appendChild(pillRow)
  }

  card.addEventListener('click', (e) => {
    e.stopPropagation()
    showFloatingAdventurerCard(adv, card, data)
  })

  return card
}

function rankColor(rank: string): string {
  switch (rank) {
    case 'F': return '#888'
    case 'E': return '#66aa66'
    case 'D': return '#6688cc'
    case 'C': return '#aa88ee'
    case 'B': return '#ee8844'
    case 'A': return '#ee4444'
    case 'S': return '#ffcc00'
    default: return '#888'
  }
}

// ---------------------------------------------------------------------------
// Log Bar (bottom)
// ---------------------------------------------------------------------------

function buildLogBar(data: GuildHallData): HTMLElement {
  const bar = document.createElement('div')
  bar.style.cssText = [
    'display:flex',
    'border-top:1px solid #2a2a3a',
    'background:#0d0d14',
    'flex-shrink:0',
    'min-height:70px',
    'max-height:100px',
    'overflow:hidden',
  ].join(';')

  // Label
  const label = document.createElement('div')
  label.style.cssText = [
    'flex-shrink:0',
    'padding:8px 14px',
    'border-right:1px solid #2a2a3a',
    'display:flex',
    'align-items:center',
    'font-size:14px',
    'color:#666',
    'letter-spacing:1px',
    'white-space:nowrap',
  ].join(';')
  label.textContent = '公會 LOG'
  bar.appendChild(label)

  // Log content
  const logContent = document.createElement('div')
  logContent.style.cssText = [
    'flex:1',
    'overflow-y:auto',
    'padding:6px 14px',
    'display:flex',
    'flex-direction:column',
    'gap:2px',
  ].join(';')

  const displayMsgs = _logExpanded ? _fullMessageLog : data.messageLog.slice(0, MAX_MSG_LINES)
  if (displayMsgs.length === 0) {
    const placeholder = document.createElement('div')
    placeholder.style.cssText = 'color:#333;font-size:14px;padding:6px 0'
    placeholder.textContent = '— 訊息記錄 —'
    logContent.appendChild(placeholder)
  } else {
    displayMsgs.forEach((msg, i) => {
      const line = document.createElement('div')
      line.style.cssText = `font-size:14px;opacity:${i === 0 ? '1' : String(Math.max(0.35, _logExpanded ? 0.7 : 0.85 - i * 0.15))}`
      line.innerHTML = colorizeLogMessage(msg)
      logContent.appendChild(line)
    })
  }
  bar.appendChild(logContent)

  // Expand toggle button
  const toggleBtn = document.createElement('button')
  toggleBtn.style.cssText = [
    'flex-shrink:0',
    'align-self:center',
    'margin:0 10px',
    'padding:2px 8px',
    'background:transparent',
    'color:#555',
    'border:1px solid #333',
    'border-radius:3px',
    'font-size:12px',
    'cursor:pointer',
    'white-space:nowrap',
  ].join(';')
  toggleBtn.textContent = _logExpanded ? '▲' : `[+${Math.max(0, _fullMessageLog.length - MAX_MSG_LINES)}]`
  toggleBtn.title = _logExpanded ? '收合記錄' : '展開完整記錄'
  toggleBtn.addEventListener('mouseenter', () => { toggleBtn.style.color = '#888' })
  toggleBtn.addEventListener('mouseleave', () => { toggleBtn.style.color = '#555' })
  toggleBtn.addEventListener('click', () => {
    _logExpanded = !_logExpanded
    if (_lastData) renderGuildHall(_lastData)
  })
  bar.appendChild(toggleBtn)

  // Adjust bar height when expanded
  if (_logExpanded) {
    bar.style.maxHeight = '180px'
  }

  return bar
}

function colorizeLogMessage(msg: string): string {
  // Escape HTML first
  const escaped = msg.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

  // Color gold amounts (e.g., +500g, -200g, 100g)
  let colored = escaped.replace(/([+-]?\d+)g/g, '<span style="color:#f0c060;font-weight:bold">$1g</span>')

  // Color success keywords
  colored = colored.replace(/(成功|完成|SUCCESS)/g, '<span style="color:#a8c890;font-weight:bold">$1</span>')

  // Color failure/death keywords
  colored = colored.replace(/(失敗|陣亡|DEATH|FAIL)/g, '<span style="color:#ee6644;font-weight:bold">$1</span>')

  // Color pyrrhic
  colored = colored.replace(/(慘勝|PYRRHIC)/g, '<span style="color:#ee9944;font-weight:bold">$1</span>')

  // Color adventurer dispatch
  colored = colored.replace(/(出發|派遣)/g, '<span style="color:#90b8d0">$1</span>')

  return colored
}

// ---------------------------------------------------------------------------
// Toolbar
// ---------------------------------------------------------------------------

function buildToolbar(): HTMLElement {
  const bar = document.createElement('div')
  bar.style.cssText = [
    'display:flex',
    'align-items:center',
    'gap:8px',
    'padding:6px 16px',
    'border-top:1px solid #2a2a3a',
    'flex-shrink:0',
    'background:#12121a',
  ].join(';')

  const saveBtn = makeToolBtn(UI_STRINGS.toolbarSave, () => {
    _callbacks?.onSave()
    // Brief visual feedback
    saveBtn.textContent = '✓ 已儲存'
    saveBtn.style.color = '#a8c890'
    setTimeout(() => {
      saveBtn.textContent = UI_STRINGS.toolbarSave
      saveBtn.style.color = '#c8b98a'
    }, 1500)
  })
  bar.appendChild(saveBtn)

  bar.appendChild(makeToolBtn(UI_STRINGS.toolbarExport, () => _callbacks?.onExport()))

  const importInput = document.createElement('input')
  importInput.type = 'file'
  importInput.accept = '.json'
  importInput.style.display = 'none'
  importInput.addEventListener('change', () => {
    const file = importInput.files?.[0]
    if (file && _callbacks) {
      _callbacks.onImport(file)
      importInput.value = ''
    }
  })
  bar.appendChild(importInput)
  bar.appendChild(makeToolBtn(UI_STRINGS.toolbarImport, () => importInput.click()))

  if (_callbacks?.cheatCallbacks) {
    bar.appendChild(renderCheatButton(
      _callbacks.cheatCallbacks,
      _cheatMenuOpen,
      (open) => { _cheatMenuOpen = open },
    ))
  }

  return bar
}

function makeToolBtn(label: string, onClick: () => void): HTMLButtonElement {
  const btn = document.createElement('button')
  btn.style.cssText = [
    'padding:4px 14px',
    'background:transparent',
    'color:#c8b98a',
    'border:1px solid #444',
    'border-radius:4px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
  ].join(';')
  btn.textContent = label
  btn.addEventListener('mouseenter', () => { btn.style.borderColor = '#888' })
  btn.addEventListener('mouseleave', () => { btn.style.borderColor = '#444' })
  btn.addEventListener('click', onClick)
  return btn
}

// ---------------------------------------------------------------------------
// Panel helpers
// ---------------------------------------------------------------------------

function showPanel(id: string): void {
  document.querySelectorAll('.gh-panel').forEach(p => {
    (p as HTMLElement).style.display = 'none'
  })
  const el = document.getElementById(id)
  if (el) el.style.display = 'flex'
  _registerRestore('panel', () => {
    document.querySelectorAll('.gh-panel').forEach(p => { (p as HTMLElement).style.display = 'none' })
    const el = document.getElementById(id)
    if (el) el.style.display = 'flex'
  })
}

function hidePanel(id: string): void {
  const el = document.getElementById(id)
  if (el) el.style.display = 'none'
  _unregisterRestore('panel')
}

function makePanelShell(id: string, title: string, width = '700px'): HTMLElement {
  const panel = document.createElement('div')
  panel.id = id
  panel.className = 'gh-panel'
  panel.style.cssText = [
    'display:none',
    'position:absolute',
    'top:50%',
    'left:50%',
    'transform:translate(-50%,-50%)',
    `width:${width}`,
    'max-width:90vw',
    'max-height:80vh',
    'background:#141420',
    'border:1px solid #4a4a6a',
    'border-radius:8px',
    'flex-direction:column',
    'z-index:50',
    'box-shadow:0 8px 40px rgba(0,0,0,0.8)',
    'overflow:hidden',
  ].join(';')

  const header = document.createElement('div')
  header.style.cssText = [
    'display:flex',
    'justify-content:space-between',
    'align-items:center',
    'padding:12px 18px',
    'border-bottom:1px solid #2a2a3a',
    'flex-shrink:0',
    'background:#1a1a28',
  ].join(';')

  const titleEl = document.createElement('span')
  titleEl.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px'
  titleEl.textContent = title
  header.appendChild(titleEl)

  const closeBtn = document.createElement('button')
  closeBtn.style.cssText = [
    'background:transparent',
    'color:#888',
    'border:1px solid #444',
    'border-radius:4px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
    'padding:2px 10px',
  ].join(';')
  closeBtn.textContent = UI_STRINGS.commissionCardClose
  closeBtn.addEventListener('click', () => hidePanel(id))
  header.appendChild(closeBtn)

  panel.appendChild(header)
  return panel
}

// ---------------------------------------------------------------------------
// Panel A — 審核委託
// ---------------------------------------------------------------------------

function buildPanelA(data: GuildHallData): HTMLElement {
  const panel = makePanelShell('panel-a', UI_STRINGS.reviewTitle)
  panel.appendChild(buildPanelABody(data))
  return panel
}

function rebuildPanelA(data: GuildHallData): void {
  const panel = document.getElementById('panel-a')
  if (!panel) return
  while (panel.children.length > 1) panel.removeChild(panel.lastChild!)
  panel.appendChild(buildPanelABody(data))
}

function buildPanelABody(data: GuildHallData): HTMLElement {
  const body = document.createElement('div')
  body.style.cssText = 'flex:1;overflow-y:auto;padding:14px 18px'

  if (data.pendingReview.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'opacity:0.5;padding:24px;text-align:center;font-size:14px'
    empty.textContent = UI_STRINGS.reviewEmpty
    body.appendChild(empty)
    return body
  }

  data.pendingReview.slice(0, REVIEW_PANEL_MAX).forEach(mission => {
    const row = document.createElement('div')
    row.style.cssText = [
      'display:flex',
      'align-items:center',
      'gap:14px',
      'padding:10px',
      'border:1px solid #2a2a3a',
      'border-radius:6px',
      'margin-bottom:8px',
      'background:#131318',
    ].join(';')

    const diffBadge = document.createElement('div')
    diffBadge.style.cssText = `padding:3px 10px;border-radius:4px;background:${difficultyColor(mission.difficulty)}22;border:1px solid ${difficultyColor(mission.difficulty)};color:${difficultyColor(mission.difficulty)};font-size:14px;font-weight:bold;flex-shrink:0`
    diffBadge.textContent = mission.difficulty
    row.appendChild(diffBadge)

    const info = document.createElement('div')
    info.style.cssText = 'flex:1;min-width:0'
    info.innerHTML = [
      `<div style="font-size:14px;font-weight:bold;color:${mission.isTemplate ? '#7a9ab5' : '#e8d8a0'};overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${mission.name}</div>`,
      `<div style="font-size:13px;color:#888;margin-top:2px">${mission.type} ｜ ${mission.baseReward}g ｜ ${Math.round(mission.duration / 60)}h</div>`,
    ].join('')
    row.appendChild(info)


    const acceptBtn = document.createElement('button')
    acceptBtn.style.cssText = panelBtnStyle('#3a7a3a')
    acceptBtn.textContent = UI_STRINGS.reviewAccept
    acceptBtn.addEventListener('click', () => _callbacks?.onReviewAccept(mission.id))

    const rejectBtn = document.createElement('button')
    rejectBtn.style.cssText = panelBtnStyle('#7a3a3a')
    rejectBtn.textContent = UI_STRINGS.reviewReject
    rejectBtn.addEventListener('click', () => _callbacks?.onReviewReject(mission.id))

    row.appendChild(rejectBtn)
    row.appendChild(acceptBtn)
    body.appendChild(row)
  })

  return body
}

// ---------------------------------------------------------------------------
// Panel B — 推薦委託
// ---------------------------------------------------------------------------

function buildPanelB(data: GuildHallData): HTMLElement {
  const panel = makePanelShell('panel-b', UI_STRINGS.recommendTitle, '760px')
  panel.appendChild(buildPanelBBody(data))
  return panel
}

function rebuildPanelB(data: GuildHallData): void {
  const panel = document.getElementById('panel-b')
  if (!panel) return
  while (panel.children.length > 1) panel.removeChild(panel.lastChild!)
  panel.appendChild(buildPanelBBody(data))
}

function buildPanelBBody(data: GuildHallData): HTMLElement {
  const body = document.createElement('div')
  body.style.cssText = 'flex:1;display:flex;flex-direction:column;overflow:hidden'

  const cols = document.createElement('div')
  cols.style.cssText = 'display:flex;flex:1;overflow:hidden;min-height:0'

  // Left: missions
  const missionsCol = document.createElement('div')
  missionsCol.style.cssText = 'flex:1;border-right:1px solid #2a2a3a;overflow-y:auto;padding:12px'

  const mTitle = document.createElement('div')
  mTitle.style.cssText = 'font-size:14px;font-weight:bold;color:#e8d8a0;margin-bottom:10px'
  mTitle.textContent = UI_STRINGS.recommendMissionsTitle
  missionsCol.appendChild(mTitle)

  const available = data.commissions.filter(m => !data.activeMissions.some(r => r.missionId === m.id))
  if (available.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'color:#666;font-size:14px;padding:8px'
    empty.textContent = UI_STRINGS.recommendMissionsEmpty
    missionsCol.appendChild(empty)
  } else {
    available.forEach(m => {
      const isSelected = _recommendMissionId === m.id
      const item = document.createElement('div')
      item.style.cssText = [
        'padding:8px 10px', 'cursor:pointer', 'border:1px solid',
        isSelected ? 'border-color:#6a6a9a;background:#1a1a2a' : 'border-color:#2a2a3a;background:#131318',
        'border-radius:5px', 'margin-bottom:6px', 'font-size:14px',
      ].join(';')
      item.innerHTML = `<span style="color:${difficultyColor(m.difficulty)};font-weight:bold">[${m.difficulty}]</span> ${m.name} <span style="color:#f0c060;float:right">${m.baseReward}g</span>`
      item.addEventListener('click', () => {
        _recommendMissionId = m.id
        _recommendPartyIds = []
        _recommendPartyIds = []
        if (_lastData) rebuildPanelB(_lastData)
      })
      missionsCol.appendChild(item)
    })
  }
  cols.appendChild(missionsCol)

  // Right: roster (multi-select for party)
  const rosterCol = document.createElement('div')
  rosterCol.style.cssText = 'flex:1;overflow-y:auto;padding:12px'

  // Determine party cap for selected mission
  const selectedMission = _recommendMissionId ? data.commissions.find(m => m.id === _recommendMissionId) : null
  const partyCap = selectedMission ? maxPartySize(selectedMission.difficulty, data.guildLevel) : 1

  const rTitle = document.createElement('div')
  rTitle.style.cssText = 'font-size:14px;font-weight:bold;color:#e8d8a0;margin-bottom:4px'
  rTitle.textContent = '【 隊伍系統 】'
  rosterCol.appendChild(rTitle)

  const rSubtitle = document.createElement('div')
  rSubtitle.style.cssText = 'font-size:12px;color:#666;margin-bottom:8px'
  rSubtitle.textContent = selectedMission
    ? `最多可選 ${partyCap} 人（已選 ${_recommendPartyIds.length}）`
    : '請先選擇委託'
  rosterCol.appendChild(rSubtitle)

  const nonDead = data.roster.filter(a => a.status !== 'dead')
  if (nonDead.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'color:#666;font-size:14px;padding:8px'
    empty.textContent = UI_STRINGS.recommendRosterEmpty
    rosterCol.appendChild(empty)
  } else {
    nonDead.forEach(adv => {
      const isOnMission = adv.status === 'on_mission'
      const isSelected = _recommendPartyIds.includes(adv.id)
      const atCap = !isSelected && _recommendPartyIds.length >= partyCap
      const profName = PROFESSION_TRAITS[adv.professionId].name

      const item = document.createElement('div')
      item.style.cssText = [
        'padding:8px 10px', 'border:1px solid',
        isSelected ? 'border-color:#6a9a6a;background:#1a2a1a' : 'border-color:#2a2a3a;background:#131318',
        (isOnMission || atCap) ? 'opacity:0.45;cursor:not-allowed' : 'cursor:pointer',
        'border-radius:5px', 'margin-bottom:6px', 'font-size:14px',
      ].filter(Boolean).join(';')

      const checkmark = isSelected ? '<span style="color:#6a9a6a;margin-right:6px">✔</span>' : ''
      const busyTag = isOnMission ? ' <span style="color:#90b8d0;font-size:12px">[任務中]</span>' : ''
      item.innerHTML = `${checkmark}<span style="color:${rankColor(adv.rank)};font-weight:bold">[${adv.rank}]</span> ${adv.name} <span style="color:#888;font-size:13px">${profName}${busyTag}</span>`

      if (!isOnMission && !atCap) {
        item.addEventListener('click', () => {
          if (_recommendPartyIds.includes(adv.id)) {
            _recommendPartyIds = _recommendPartyIds.filter(id => id !== adv.id)
          } else {
            _recommendPartyIds = [..._recommendPartyIds, adv.id]
          }
          
          if (_lastData) rebuildPanelB(_lastData)
        })
      }
      rosterCol.appendChild(item)
    })
  }
  cols.appendChild(rosterCol)
  body.appendChild(cols)

  // Footer — synergy preview + dispatch
  const footer = document.createElement('div')
  footer.style.cssText = [
    'padding:10px 16px', 'border-top:1px solid #2a2a3a', 'flex-shrink:0',
    'display:flex', 'align-items:center', 'gap:16px', 'background:#1a1a28',
  ].join(';')

  const summary = document.createElement('div')
  summary.style.cssText = 'flex:1;font-size:14px;line-height:1.6'

  if (selectedMission && _recommendPartyIds.length > 0 && _lastData) {
    const members = _recommendPartyIds.map(id => _lastData!.roster.find(a => a.id === id)).filter(Boolean) as typeof data.roster
    const rates = calcPartyRates(members, selectedMission)
    const synergyStr = rates.activeSynergies.length > 0
      ? `<span style="color:#d4a843"> ✦ ${rates.activeSynergies.join('、')}</span>`
      : ''
    const rewardDisplay = rates.rewardMult > 1
      ? Math.floor(selectedMission.baseReward * rates.rewardMult)
      : selectedMission.baseReward
    summary.innerHTML = [
      `<div><span style="color:#d8c890">${selectedMission.name}</span> ｜ <span style="color:${difficultyColor(selectedMission.difficulty)}">${selectedMission.difficulty}</span> ｜ <span style="color:#f0c060">+${rewardDisplay}g</span>${synergyStr}</div>`,
      `<div style="color:#888;font-size:13px">隊伍 ${members.map(m => m.name).join('、')} ｜ 成功率 <span style="color:#a8c890">${Math.round(rates.finalSuccessRate * 100)}%</span> ｜ 死亡率 <span style="color:#ee6644">${Math.round(rates.finalDeathRate * 100)}%</span></div>`,
    ].join('')
  } else {
    summary.style.color = '#555'
    summary.textContent = '— 選擇委託，再點選隊員出發 —'
  }
  footer.appendChild(summary)

  const canDispatch = selectedMission !== null && _recommendPartyIds.length > 0
  const dispatchBtn = document.createElement('button')
  dispatchBtn.style.cssText = [
    'padding:8px 20px', 'background:transparent', 'color:#c8b98a',
    'border:1px solid #c8b98a', 'border-radius:4px',
    "font-family:'Segoe UI',sans-serif", 'font-size:14px',
    canDispatch ? 'cursor:pointer' : 'opacity:0.4;cursor:not-allowed',
  ].join(';')
  dispatchBtn.textContent = _recommendPartyIds.length > 1 ? '組隊出發' : UI_STRINGS.recommendButton
  dispatchBtn.disabled = !canDispatch
  if (canDispatch) {
    dispatchBtn.addEventListener('click', () => {
      if (_recommendMissionId && _recommendPartyIds.length > 0 && _callbacks) {
        _callbacks.onPartyDispatch(_recommendMissionId, [..._recommendPartyIds])
        _recommendMissionId = null
        _recommendPartyIds = []
        _recommendPartyIds = []
        hidePanel('panel-b')
      }
    })
  }
  footer.appendChild(dispatchBtn)
  body.appendChild(footer)

  return body
}

// ---------------------------------------------------------------------------
// Panel E — 公會建設（佔位）
// ---------------------------------------------------------------------------

function buildPanelE(): HTMLElement {
  const panel = makePanelShell('panel-e', UI_STRINGS.hallConstructionTitle, '500px')
  const body = document.createElement('div')
  body.style.cssText = 'padding:48px;text-align:center;opacity:0.6;font-size:16px'
  body.textContent = UI_STRINGS.hallConstructionPlaceholder
  panel.appendChild(body)
  return panel
}

// ---------------------------------------------------------------------------
// Panel G — 公會長管理（招募 / 開除）
// ---------------------------------------------------------------------------

function buildPanelG(data: GuildHallData): HTMLElement {
  const panel = makePanelShell('panel-g', UI_STRINGS.guildMasterTitle, '760px')
  panel.appendChild(buildPanelGBody(data))
  return panel
}

function rebuildPanelG(data: GuildHallData): void {
  const panel = document.getElementById('panel-g')
  if (!panel) return
  while (panel.children.length > 1) panel.removeChild(panel.lastChild!)
  panel.appendChild(buildPanelGBody(data))
}

function buildPanelGBody(data: GuildHallData): HTMLElement {
  const body = document.createElement('div')
  body.style.cssText = 'flex:1;display:flex;overflow:hidden;min-height:0'

  // Left: roster (fire)
  const rosterCol = document.createElement('div')
  rosterCol.style.cssText = 'flex:1;border-right:1px solid #2a2a3a;overflow-y:auto;padding:12px'

  const rTitle = document.createElement('div')
  rTitle.style.cssText = 'font-size:14px;font-weight:bold;color:#e8d8a0;margin-bottom:10px'
  rTitle.textContent = UI_STRINGS.guildMasterRosterTitle
  rosterCol.appendChild(rTitle)

  data.roster.filter(a => a.status !== 'dead').forEach(adv => {
    const isSelected = _fireCandidateId === adv.id
    const isOnMission = adv.status === 'on_mission'
    const profName = PROFESSION_TRAITS[adv.professionId].name

    const row = document.createElement('div')
    row.style.cssText = [
      'display:flex',
      'align-items:center',
      'gap:8px',
      'padding:8px 10px',
      'margin-bottom:6px',
      'border:1px solid',
      isSelected ? 'border-color:#9a3a3a;background:#2a1a1a' : 'border-color:#2a2a3a;background:#131318',
      'border-radius:5px',
      'font-size:14px',
      isOnMission ? 'opacity:0.6;cursor:default' : 'cursor:pointer',
    ].filter(Boolean).join(';')

    const info = document.createElement('div')
    info.style.cssText = 'flex:1'
    info.innerHTML = `<span style="color:${rankColor(adv.rank)};font-weight:bold">[${adv.rank}]</span> ${adv.name} <span style="color:#888;font-size:13px">${profName}${isOnMission ? ' [任務中]' : ''}</span>`
    row.appendChild(info)

    if (!isOnMission) {
      row.addEventListener('click', () => {
        _fireCandidateId = adv.id
        if (_lastData) rebuildPanelG(_lastData)
      })
    }

    if (isSelected) {
      const fireBtn = document.createElement('button')
      fireBtn.style.cssText = panelBtnStyle('#7a3a3a') + ';font-size:13px;padding:3px 10px'
      fireBtn.textContent = UI_STRINGS.guildMasterFireButton
      fireBtn.addEventListener('click', (e) => {
        e.stopPropagation()
        _callbacks?.onFire(adv.id)
        _fireCandidateId = null
      })
      row.appendChild(fireBtn)
    }

    rosterCol.appendChild(row)
  })
  body.appendChild(rosterCol)

  // Right: candidates (recruit)
  const candidatesCol = document.createElement('div')
  candidatesCol.style.cssText = 'flex:1;overflow-y:auto;padding:12px'

  const cTitle = document.createElement('div')
  cTitle.style.cssText = 'font-size:14px;font-weight:bold;color:#e8d8a0;margin-bottom:10px'
  cTitle.textContent = UI_STRINGS.guildMasterCandidatesTitle
  candidatesCol.appendChild(cTitle)

  if (data.candidateAdventurers.length === 0) {
    const empty = document.createElement('div')
    empty.style.cssText = 'color:#666;font-size:14px;padding:8px'
    empty.textContent = UI_STRINGS.recruitNoCandidates
    candidatesCol.appendChild(empty)
  } else {
    data.candidateAdventurers.forEach(adv => {
      const isSelected = _recruitCandidateId === adv.id
      const profName = PROFESSION_TRAITS[adv.professionId].name
      const cost = adv.fixedRecruitCost ?? recruitCost(adv.rank)
      const canAfford = data.gold >= cost

      const row = document.createElement('div')
      row.style.cssText = [
        'display:flex',
        'align-items:center',
        'gap:8px',
        'padding:8px 10px',
        'margin-bottom:6px',
        'border:1px solid',
        isSelected ? 'border-color:#3a9a3a;background:#1a2a1a' : 'border-color:#2a2a3a;background:#131318',
        'border-radius:5px',
        'font-size:14px',
        'cursor:pointer',
        canAfford ? '' : 'opacity:0.5',
      ].filter(Boolean).join(';')

      const info = document.createElement('div')
      info.style.cssText = 'flex:1'
      info.innerHTML = `<span style="color:${rankColor(adv.rank)};font-weight:bold">[${adv.rank}]</span> ${adv.name} <span style="color:#888;font-size:13px">${profName}</span> <span style="color:#f0c060;float:right">${cost}g</span>`
      row.appendChild(info)

      row.addEventListener('click', () => {
        _recruitCandidateId = adv.id
        if (_lastData) rebuildPanelG(_lastData)
      })

      if (isSelected) {
        const recruitBtn = document.createElement('button')
        recruitBtn.style.cssText = panelBtnStyle('#3a7a3a') + ';font-size:13px;padding:3px 10px'
        recruitBtn.textContent = UI_STRINGS.guildMasterRecruitButton
        recruitBtn.addEventListener('click', (e) => {
          e.stopPropagation()
          _callbacks?.onRecruit(adv.id)
          _recruitCandidateId = null
        })
        row.appendChild(recruitBtn)
      }

      candidatesCol.appendChild(row)
    })
  }
  body.appendChild(candidatesCol)

  return body
}

// ---------------------------------------------------------------------------
// Panel C — 委託資訊卡（浮動）
// ---------------------------------------------------------------------------

function showFloatingCommissionCard(mission: Mission, anchor: HTMLElement, fixedPos?: { top: number; left: number }): void {
  document.querySelectorAll('.gh-floating-card').forEach(el => el.remove())

  const scene = document.getElementById('guild-hall-scene')
  if (!scene) return

  _registerRestore('floating-commission', () => {
    if (!_lastData || !_floatingCommCardPos) return
    const m = _lastData.commissions.find(x => x.id === mission.id) ?? _lastData.pendingReview.find(x => x.id === mission.id)
    if (!m) { _unregisterRestore('floating-commission'); return }
    showFloatingCommissionCard(m, anchor, _floatingCommCardPos)
  })
  _unregisterRestore('floating-adventurer')

  const card = document.createElement('div')
  card.className = 'gh-floating-card'
  card.style.cssText = [
    'position:absolute',
    'background:#1a1a28',
    'border:1px solid #4a4a6a',
    'border-radius:8px',
    'padding:14px 18px',
    'z-index:80',
    'min-width:240px',
    'max-width:320px',
    'box-shadow:0 8px 32px rgba(0,0,0,0.8)',
    'font-size:14px',
  ].join(';')

  let top: number, left: number
  if (fixedPos) {
    top = fixedPos.top
    left = fixedPos.left
  } else {
    const anchorRect = anchor.getBoundingClientRect()
    const sceneRect = scene.getBoundingClientRect()
    top = anchorRect.bottom - sceneRect.top + 6
    left = anchorRect.left - sceneRect.left
    // Keep within scene
    if (left + 320 > sceneRect.width) left = sceneRect.width - 330
    if (top + 300 > sceneRect.height) top = anchorRect.top - sceneRect.top - 310
  }
  _floatingCommCardPos = { top, left }

  card.style.top = `${top}px`
  card.style.left = `${left}px`

  const diffColor = difficultyColor(mission.difficulty)
  const COMM_EXPIRY_MS = 12 * 60 * 60 * 1_000
  let expiryRow = ''
  if (mission.postedAt) {
    const remainMs = COMM_EXPIRY_MS - (Date.now() - mission.postedAt)
    if (remainMs > 0) {
      const remainH = Math.floor(remainMs / 3_600_000)
      const remainM = Math.floor((remainMs % 3_600_000) / 60_000)
      const urgent = remainH < 2
      expiryRow = `  <span style="color:#666">時限</span><span style="color:${urgent ? '#ee6644' : '#888'}">${remainH}h ${remainM}m</span>`
    } else {
      expiryRow = `  <span style="color:#666">時限</span><span style="color:#ee6644">即將過期</span>`
    }
  }
  card.innerHTML = [
    `<div style="display:flex;align-items:center;gap:10px;margin-bottom:10px">`,
    `  <span style="padding:2px 10px;border-radius:3px;background:${diffColor}22;border:1px solid ${diffColor};color:${diffColor};font-size:13px;font-weight:bold">${mission.difficulty}</span>`,
    `  <span style="font-size:15px;font-weight:bold;color:${mission.isTemplate ? '#7a9ab5' : '#e8d8a0'}">${mission.name}</span>`,
    `</div>`,
    `<div style="display:grid;grid-template-columns:auto 1fr;gap:4px 12px;font-size:14px">`,
    `  <span style="color:#666">類型</span><span>${mission.type}</span>`,
    `  <span style="color:#666">報酬</span><span style="color:#f0c060;font-weight:bold">${mission.baseReward}g</span>`,
    `  <span style="color:#666">時長</span><span>${Math.round(mission.duration / 60)}h</span>`,
    expiryRow,
    `</div>`,
    `<div style="margin-top:10px;font-size:13px;color:#888;line-height:1.5;border-top:1px solid #2a2a3a;padding-top:8px">${mission.description}</div>`,
    `<div style="margin-top:10px;text-align:right">`,
    `  <button style="${panelBtnStyle('#444')};font-size:13px" id="close-float-c">${UI_STRINGS.commissionCardClose}</button>`,
    `</div>`,
  ].join('')

  scene.appendChild(card)
  card.querySelector('#close-float-c')?.addEventListener('click', () => {
    card.remove()
    _unregisterRestore('floating-commission')
    _floatingCommCardPos = null
  })

  setTimeout(() => {
    document.addEventListener('click', function handler(e) {
      if (!card.contains(e.target as Node)) {
        card.remove()
        _unregisterRestore('floating-commission')
        _floatingCommCardPos = null
        document.removeEventListener('click', handler)
      }
    })
  }, 0)
}

// ---------------------------------------------------------------------------
// Panel D — 傭兵資料卡（浮動）
// ---------------------------------------------------------------------------

function showFloatingAdventurerCard(adv: Adventurer, anchor: HTMLElement, data: GuildHallData, fixedPos?: { top: number; left: number }): void {
  document.querySelectorAll('.gh-floating-card').forEach(el => el.remove())

  const scene = document.getElementById('guild-hall-scene')
  if (!scene) return

  _registerRestore('floating-adventurer', () => {
    if (!_lastData || !_floatingAdvCardPos) return
    const a = _lastData.roster.find(x => x.id === adv.id)
    if (!a) { _unregisterRestore('floating-adventurer'); return }
    showFloatingAdventurerCard(a, anchor, _lastData, _floatingAdvCardPos)
  })
  _unregisterRestore('floating-commission')

  const profName = PROFESSION_TRAITS[adv.professionId].name
  const statusLabel = adv.status === 'idle'
    ? UI_STRINGS.rosterStatusAvailable
    : adv.status === 'on_mission'
    ? UI_STRINGS.rosterStatusOnMission
    : UI_STRINGS.adventurerDead

  const dispatch = data.activeMissions.find(r => r.adventurerId === adv.id)

  const card = document.createElement('div')
  card.className = 'gh-floating-card'
  card.style.cssText = [
    'position:absolute',
    'background:#1a1a28',
    'border:1px solid #4a4a6a',
    'border-radius:8px',
    'padding:14px 18px',
    'z-index:80',
    'min-width:240px',
    'box-shadow:0 8px 32px rgba(0,0,0,0.8)',
    'font-size:14px',
  ].join(';')

  let top: number, left: number
  if (fixedPos) {
    top = fixedPos.top
    left = fixedPos.left
  } else {
    const anchorRect = anchor.getBoundingClientRect()
    const sceneRect = scene.getBoundingClientRect()
    top = anchorRect.bottom - sceneRect.top + 6
    left = anchorRect.left - sceneRect.left
    if (left + 280 > sceneRect.width) left = sceneRect.width - 290
    if (top + 260 > sceneRect.height) top = anchorRect.top - sceneRect.top - 270
  }
  _floatingAdvCardPos = { top, left }

  card.style.top = `${top}px`
  card.style.left = `${left}px`

  const rColor = rankColor(adv.rank)
  let extraHtml = ''
  if (dispatch) {
    const countdown = formatCountdown(dispatch.endTimestamp)
    extraHtml = `
      <div style="margin-top:8px;padding:6px 10px;background:#1a2a3a;border-radius:4px;font-size:13px;color:#90b8d0">
        ⏱ 剩餘: <span style="font-family:monospace;font-weight:bold">${countdown}</span>
        <div style="color:#888;margin-top:2px;font-size:12px">▶ ${dispatch.missionName}</div>
      </div>
    `
  }

  const profTrait = PROFESSION_TRAITS[adv.professionId]
  const missionTypes: Array<{ key: string; label: string }> = [
    { key: '討伐', label: '討伐' },
    { key: '護送', label: '護送' },
    { key: '採集', label: '採集' },
    { key: '調查', label: '調查' },
  ]
  const modRows = missionTypes.map(({ key, label }) => {
    const mod = profTrait.successRateModifier[key as keyof typeof profTrait.successRateModifier] ?? 0
    const color = mod > 0 ? '#a8c890' : mod < 0 ? '#d07070' : '#888'
    const sign = mod > 0 ? '+' : ''
    return `  <span style="color:#666">${label}</span><span style="color:${color}">${sign}${mod}%</span>`
  }).join('\n')

  const growthTraits = adv.growthTraits ?? []
  const pillsHtml = growthTraits.length > 0
    ? `<div style="display:flex;flex-wrap:wrap;gap:3px;margin-top:8px">${
        growthTraits.map(gt => {
          const def = GROWTH_TRAITS[gt.traitId]
          if (!def) return ''
          return `<span style="display:inline-block;padding:1px 5px;border-radius:3px;font-size:10px;background:#1a2a1a;border:1px solid #3a5a3a;color:#8ac88a;cursor:default" title="${def.description}">${def.name}</span>`
        }).join('')
      }</div>`
    : ''

  const floatRaceName = adv.raceId ? (RACE_MODIFIERS[adv.raceId]?.name ?? '') : ''
  const bioHtml = adv.bio
    ? `<div style="margin-top:8px;padding:6px 8px;background:#111120;border-radius:4px;font-size:12px;color:#666;line-height:1.6">${adv.bio}</div>`
    : ''

  card.innerHTML = [
    `<div style="display:flex;align-items:center;gap:10px;margin-bottom:10px">`,
    `  <span style="padding:2px 10px;border-radius:3px;background:${rColor}22;border:1px solid ${rColor};color:${rColor};font-size:13px;font-weight:bold">${adv.rank}</span>`,
    `  <span style="font-size:15px;font-weight:bold;color:#e8d8a0">${adv.name}</span>`,
    floatRaceName ? `  <span style="font-size:13px;color:#888">${floatRaceName}</span>` : '',
    `</div>`,
    `<div style="display:grid;grid-template-columns:auto 1fr;gap:4px 12px;font-size:14px">`,
    `  <span style="color:#666">職業</span><span>${profName}</span>`,
    `  <span style="color:#666">狀態</span><span style="color:${adv.status === 'idle' ? '#a8c890' : '#90b8d0'}">${statusLabel}</span>`,
    `</div>`,
    bioHtml,
    `<div style="margin-top:8px;padding:6px 8px;background:#111120;border-radius:4px">`,
    `  <div style="color:#666;font-size:11px;margin-bottom:4px">職業任務加成</div>`,
    `  <div style="display:grid;grid-template-columns:auto 1fr auto 1fr;gap:3px 10px;font-size:12px">`,
    modRows,
    `  </div>`,
    `</div>`,
    pillsHtml,
    extraHtml,
    `<div style="margin-top:10px;text-align:right">`,
    `  <button style="${panelBtnStyle('#444')};font-size:13px" id="close-float-d">${UI_STRINGS.commissionCardClose}</button>`,
    `</div>`,
  ].join('')

  scene.appendChild(card)
  card.querySelector('#close-float-d')?.addEventListener('click', () => {
    card.remove()
    _unregisterRestore('floating-adventurer')
    _floatingAdvCardPos = null
  })

  setTimeout(() => {
    document.addEventListener('click', function handler(e) {
      if (!card.contains(e.target as Node)) {
        card.remove()
        _unregisterRestore('floating-adventurer')
        _floatingAdvCardPos = null
        document.removeEventListener('click', handler)
      }
    })
  }, 0)
}

// ---------------------------------------------------------------------------
// Settlement Overlay — small card popup
// ---------------------------------------------------------------------------

function buildSettlementOverlay(results: OutcomeResult[], longOfflineDays?: number): HTMLElement {
  // Backdrop
  const backdrop = document.createElement('div')
  backdrop.id = 'settlement-overlay'
  backdrop.style.cssText = [
    'position:absolute',
    'inset:0',
    'background:rgba(0,0,0,0.65)',
    'display:flex',
    'align-items:center',
    'justify-content:center',
    'z-index:100',
  ].join(';')
  backdrop.addEventListener('click', e => e.stopPropagation())

  // Card
  const card = document.createElement('div')
  card.style.cssText = [
    'background:#141420',
    'border:1px solid #4a4a6a',
    'border-radius:10px',
    'width:min(580px,90vw)',
    'max-height:75vh',
    'display:flex',
    'flex-direction:column',
    'box-shadow:0 16px 60px rgba(0,0,0,0.9)',
    'overflow:hidden',
  ].join(';')

  // Header
  const header = document.createElement('div')
  header.style.cssText = 'padding:14px 20px;border-bottom:1px solid #2a2a3a;background:#1a1a28;flex-shrink:0;display:flex;align-items:center;gap:10px'
  const headerTitle = document.createElement('span')
  headerTitle.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px'
  headerTitle.textContent = UI_STRINGS.settlementTitle
  header.appendChild(headerTitle)
  card.appendChild(header)

  // Offline notice
  if (longOfflineDays !== undefined && longOfflineDays > 0) {
    const notice = document.createElement('div')
    notice.style.cssText = 'padding:10px 20px;color:#ff8844;font-size:14px;background:#1a1210;border-bottom:1px solid #2a2a3a;flex-shrink:0'
    notice.textContent = UI_STRINGS.settlementLongOffline.replace('{days}', String(longOfflineDays))
    card.appendChild(notice)
  }

  // Results list
  const list = document.createElement('div')
  list.style.cssText = 'flex:1;overflow-y:auto;padding:14px 20px;display:flex;flex-direction:column;gap:8px'

  results.forEach(result => {
    const item = document.createElement('div')
    item.style.cssText = [
      'padding:10px 14px',
      'border:1px solid #2a2a3a',
      'border-radius:6px',
      'background:#131318',
      'display:flex',
      'align-items:center',
      'gap:12px',
      'font-size:14px',
    ].join(';')

    const badge = outcomeTypeBadge(result.type)
    const badgeColor = result.type === 'SUCCESS' ? '#a8c890'
      : result.type === 'PYRRHIC' ? '#ee9944'
      : '#ee6644'

    const badgeEl = document.createElement('span')
    badgeEl.style.cssText = `color:${badgeColor};font-weight:bold;flex-shrink:0`
    badgeEl.textContent = badge

    const infoEl = document.createElement('span')
    infoEl.style.cssText = 'flex:1;color:#c8b98a'
    infoEl.textContent = `${result.record.adventurerName} — ${result.record.missionName}`

    const goldEl = document.createElement('span')
    const isGoldSuccess = result.type === 'SUCCESS' || result.type === 'PYRRHIC'
    const goldAbs = Math.abs(result.goldDelta)
    goldEl.style.cssText = `color:${isGoldSuccess ? '#6ac86a' : '#ee6644'};font-weight:bold;flex-shrink:0`
    goldEl.textContent = isGoldSuccess ? `+${goldAbs}g` : `-${goldAbs}g`

    const repEl = document.createElement('span')
    const repPositive = result.reputationDelta >= 0
    repEl.style.cssText = `color:${repPositive ? '#90b8d0' : '#ee8844'};font-size:13px;flex-shrink:0`
    repEl.textContent = repPositive ? `+${result.reputationDelta}聲` : `${result.reputationDelta}聲`

    item.appendChild(badgeEl)
    item.appendChild(infoEl)
    item.appendChild(repEl)
    item.appendChild(goldEl)
    list.appendChild(item)
  })
  card.appendChild(list)

  // Footer
  const footer = document.createElement('div')
  footer.style.cssText = 'padding:12px 20px;border-top:1px solid #2a2a3a;text-align:right;flex-shrink:0;background:#1a1a28'

  const confirmBtn = document.createElement('button')
  confirmBtn.style.cssText = [
    'padding:8px 24px',
    'background:#2a2a4a',
    'color:#c8b98a',
    'border:1px solid #c8b98a',
    'border-radius:5px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
  ].join(';')
  confirmBtn.textContent = UI_STRINGS.settlementConfirm
  confirmBtn.addEventListener('click', () => {
    if (_callbacks) _callbacks.onSettlementDismiss()
  })
  footer.appendChild(confirmBtn)
  card.appendChild(footer)

  backdrop.appendChild(card)
  return backdrop
}

function outcomeTypeBadge(type: OutcomeResult['type']): string {
  switch (type) {
    case 'SUCCESS':  return UI_STRINGS.settlementSuccess
    case 'FAILURE':  return UI_STRINGS.settlementFail
    case 'DEATH':    return UI_STRINGS.settlementDeath
    case 'PYRRHIC':  return UI_STRINGS.settlementPyrrhic
    default:         return '?'
  }
}

// ---------------------------------------------------------------------------
// Shared style helpers
// ---------------------------------------------------------------------------

function panelBtnStyle(borderColor: string): string {
  return [
    'background:transparent',
    'color:#c8b98a',
    `border:1px solid ${borderColor}`,
    'border-radius:4px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
    'padding:4px 12px',
  ].join(';')
}
