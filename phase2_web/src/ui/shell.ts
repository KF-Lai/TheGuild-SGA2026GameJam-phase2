// shell.ts — Main UI Shell
// Pure DOM implementation. No React/Vue. No game logic.
// All text from ui-strings.ts. State flows in via renderShell(); actions flow out via callbacks.

import type { Mission, Adventurer, DispatchRecord, OutcomeResult } from '../types'
import { UI_STRINGS } from './ui-strings'
import { renderCheatButton } from './cheat-menu'
import type { CheatCallbacks } from './cheat-menu'
import { getReputationLabel } from '../systems/resource'

export type { CheatCallbacks }

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface ShellData {
  gold: number
  pendingGold: number
  reputation: number
  guildLevel: number
  commissions: Mission[]
  roster: Adventurer[]
  activeMissions: DispatchRecord[]
  pendingResults: OutcomeResult[]
  /** Optional: supply when the player has been offline > 3 days */
  longOfflineDays?: number
}

export interface ShellCallbacks {
  onDispatch: (missionId: string, adventurerId: string) => void
  onSettlementDismiss: () => void
  onSave: () => void
  onExport: () => void
  onImport: (file: File) => void
  cheatCallbacks?: CheatCallbacks
}

// ---------------------------------------------------------------------------
// Module-level state (shell is a singleton per app element)
// ---------------------------------------------------------------------------

/** Bankruptcy warning threshold — consumers may override via MAX_DEBT */
export const MAX_DEBT = -500

let _root: HTMLElement | null = null
let _callbacks: ShellCallbacks | null = null
let _selectedMissionId: string | null = null
let _selectedAdventurerId: string | null = null
let _lastData: ShellData | null = null
let _cheatPanelOpen = false

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Mount the shell into `appEl` and register callbacks.
 * Call once on startup. Renders a loading placeholder immediately.
 */
export function initShell(appEl: HTMLElement, callbacks: ShellCallbacks): void {
  _root = appEl
  _callbacks = callbacks
  _selectedMissionId = null
  _selectedAdventurerId = null
  _root.innerHTML = ''
  _root.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'height:100%',
    'min-height:100vh',
    'background:#0d0d0d',
    'color:#c8b98a',
    'font-family:\'Courier New\',monospace',
    'font-size:14px',
  ].join(';')

  const loading = document.createElement('div')
  loading.id = 'shell-loading'
  loading.style.cssText = 'padding:16px;text-align:center;opacity:0.7'
  loading.textContent = UI_STRINGS.loadingText
  _root.appendChild(loading)
}

/**
 * Re-render the entire shell with fresh data.
 * Safe to call on every game tick or state change.
 */
export function renderShell(data: ShellData): void {
  if (!_root || !_callbacks) return
  _lastData = data

  // When pending results appear, deselect everything — overlay takes focus.
  if (data.pendingResults.length > 0) {
    _selectedMissionId = null
    _selectedAdventurerId = null
  }

  _root.innerHTML = ''

  const statusBar = renderStatusBar(data)
  const mainArea = renderMainArea(data)
  const toolbar = renderToolbar()

  _root.appendChild(statusBar)
  _root.appendChild(mainArea)
  _root.appendChild(toolbar)

  if (data.pendingResults.length > 0) {
    const overlay = renderOverlay(data.pendingResults, data.longOfflineDays)
    _root.appendChild(overlay)
  }
}

// ---------------------------------------------------------------------------
// Component: StatusBar
// ---------------------------------------------------------------------------

function renderStatusBar(data: ShellData): HTMLElement {
  const bar = document.createElement('div')
  bar.id = 'status-bar'
  bar.style.cssText = [
    'display:flex',
    'gap:24px',
    'padding:8px 16px',
    'border-bottom:1px solid #333',
    'flex-shrink:0',
    'flex-wrap:wrap',
  ].join(';')

  bar.appendChild(buildGoldField(data.gold))
  bar.appendChild(buildPendingField(data.pendingGold))
  bar.appendChild(buildReputationField(data.reputation))

  return bar
}

function buildGoldField(gold: number): HTMLElement {
  const span = document.createElement('span')
  const isBankrupt = gold < MAX_DEBT
  const isDebt = gold < 0

  if (isBankrupt) {
    span.className = 'gold-bankrupt'
    span.style.cssText = 'color:#ff4444;animation:blink 1s step-end infinite'
    span.textContent = `${UI_STRINGS.statusGold}: ${gold}g ${UI_STRINGS.statusBankruptWarn}`
  } else if (isDebt) {
    span.className = 'gold-debt'
    span.style.cssText = 'color:#ff4444'
    span.textContent = `${UI_STRINGS.statusGold}: ${gold}g ${UI_STRINGS.statusDebt}`
  } else {
    span.className = 'gold-normal'
    span.textContent = `${UI_STRINGS.statusGold}: ${gold}g`
  }
  return span
}

function buildPendingField(pendingGold: number): HTMLElement {
  const span = document.createElement('span')
  span.textContent = `${UI_STRINGS.statusPending}: ${pendingGold}g`
  return span
}

function buildReputationField(reputation: number): HTMLElement {
  const span = document.createElement('span')
  span.textContent = `${UI_STRINGS.statusReputation}: ${getReputationLabel(reputation)}`
  return span
}

// ---------------------------------------------------------------------------
// Component: MainArea (CommissionPanel + RosterPanel side-by-side)
// ---------------------------------------------------------------------------

function renderMainArea(data: ShellData): HTMLElement {
  const area = document.createElement('div')
  area.id = 'main-area'
  area.style.cssText = [
    'display:flex',
    'flex:1',
    'overflow:hidden',
    'position:relative',
  ].join(';')

  area.appendChild(renderCommissions(data))
  area.appendChild(renderRoster(data))

  return area
}

// ---------------------------------------------------------------------------
// Component: CommissionPanel
// ---------------------------------------------------------------------------

function renderCommissions(data: ShellData): HTMLElement {
  const panel = document.createElement('div')
  panel.id = 'commission-panel'
  panel.style.cssText = [
    'flex:1',
    'display:flex',
    'flex-direction:column',
    'border-right:1px solid #333',
    'padding:12px',
    'overflow-y:auto',
    'min-width:0',
  ].join(';')

  const title = document.createElement('h2')
  title.style.cssText = 'font-size:14px;margin-bottom:8px;border-bottom:1px solid #444;padding-bottom:4px'
  title.textContent = UI_STRINGS.commissionPanelTitle
  panel.appendChild(title)

  if (data.commissions.length === 0) {
    const empty = document.createElement('p')
    empty.style.cssText = 'opacity:0.6;padding:4px 0'
    empty.textContent = UI_STRINGS.commissionEmpty
    panel.appendChild(empty)
  } else {
    const list = document.createElement('ul')
    list.style.cssText = 'list-style:none;flex:1'
    data.commissions.forEach(mission => {
      list.appendChild(buildCommissionCard(mission, data))
    })
    panel.appendChild(list)
  }

  // Detail panel
  const detail = document.createElement('div')
  detail.style.cssText = 'height:160px;overflow-y:auto;border-top:1px solid #444;padding:8px;margin-top:8px'

  const selectedMission = _selectedMissionId
    ? data.commissions.find(m => m.id === _selectedMissionId)
    : undefined

  if (selectedMission) {
    const nameColor = selectedMission.isTemplate ? '#7a9ab5' : '#c8b98a'

    const nameEl = document.createElement('div')
    nameEl.style.cssText = `font-size:14px;color:${nameColor};margin-bottom:4px`
    nameEl.textContent = selectedMission.name
    detail.appendChild(nameEl)

    const typeEl = document.createElement('div')
    typeEl.style.cssText = 'font-size:12px;margin-bottom:2px'
    typeEl.textContent = `類型：${selectedMission.type}`
    detail.appendChild(typeEl)

    const diffEl = document.createElement('div')
    diffEl.style.cssText = 'font-size:12px;margin-bottom:2px'
    diffEl.textContent = `難度：${selectedMission.difficulty}`
    detail.appendChild(diffEl)

    const rewardEl = document.createElement('div')
    rewardEl.style.cssText = 'font-size:12px;margin-bottom:4px'
    rewardEl.textContent = `報酬：${selectedMission.baseReward}g`
    detail.appendChild(rewardEl)

    const descEl = document.createElement('div')
    descEl.style.cssText = 'font-size:12px;opacity:0.8;white-space:pre-wrap'
    descEl.textContent = selectedMission.description
    detail.appendChild(descEl)
  } else {
    const placeholder = document.createElement('div')
    placeholder.style.cssText = 'opacity:0.4;text-align:center;padding-top:60px;font-size:12px'
    placeholder.textContent = '— 選擇委託以查看詳情 —'
    detail.appendChild(placeholder)
  }

  panel.appendChild(detail)

  return panel
}

function buildCommissionCard(mission: Mission, data: ShellData): HTMLElement {
  const isSelected = _selectedMissionId === mission.id
  const isActive = data.activeMissions.some(r => r.missionId === mission.id)

  const li = document.createElement('li')
  li.style.cssText = [
    'padding:6px 8px',
    'margin-bottom:4px',
    'cursor:pointer',
    'border:1px solid transparent',
    isSelected ? 'background:#333;border-color:#c8b98a' : '',
    isActive ? 'opacity:0.5;cursor:default' : '',
  ].filter(Boolean).join(';')

  const bullet = isSelected ? '[●]' : '[○]'
  const templateTag = mission.isTemplate ? ' [生]' : ''
  li.textContent = `${bullet} ${mission.name} ${mission.difficulty}${templateTag}`
  li.style.color = mission.isTemplate ? '#7a9ab5' : (isSelected ? '#c8b98a' : 'inherit')

  if (!isActive) {
    li.addEventListener('click', () => {
      _selectedMissionId = mission.id
      if (_lastData) renderShell(_lastData)
    })
  }

  return li
}

// ---------------------------------------------------------------------------
// Component: RosterPanel
// ---------------------------------------------------------------------------

function renderRoster(data: ShellData): HTMLElement {
  const panel = document.createElement('div')
  panel.id = 'roster-panel'
  panel.style.cssText = [
    'flex:1',
    'display:flex',
    'flex-direction:column',
    'padding:12px',
    'overflow-y:auto',
    'min-width:0',
  ].join(';')

  const title = document.createElement('h2')
  title.style.cssText = 'font-size:14px;margin-bottom:8px;border-bottom:1px solid #444;padding-bottom:4px'
  title.textContent = UI_STRINGS.rosterPanelTitle
  panel.appendChild(title)

  const available = data.roster.filter(a => a.status !== 'dead')

  if (available.length === 0) {
    const empty = document.createElement('p')
    empty.style.cssText = 'opacity:0.6;padding:4px 0'
    empty.textContent = UI_STRINGS.rosterEmpty
    panel.appendChild(empty)
  } else {
    const list = document.createElement('ul')
    list.style.cssText = 'list-style:none;flex:1'
    available.forEach(adventurer => {
      list.appendChild(buildAdventurerCard(adventurer))
    })
    panel.appendChild(list)
  }

  // Selection indicator + dispatch button
  const bottom = document.createElement('div')
  bottom.style.cssText = 'margin-top:8px;border-top:1px solid #444;padding-top:8px'

  if (_selectedAdventurerId) {
    const found = data.roster.find(a => a.id === _selectedAdventurerId)
    if (found) {
      const sel = document.createElement('div')
      sel.style.cssText = 'font-size:12px;margin-bottom:6px'
      sel.textContent = `${UI_STRINGS.rosterSelected}${found.name}`
      bottom.appendChild(sel)
    }
  }

  const canDispatch = _selectedMissionId !== null && _selectedAdventurerId !== null
  const dispatchBtn = document.createElement('button')
  dispatchBtn.style.cssText = [
    'float:right',
    'padding:4px 12px',
    'background:transparent',
    'color:#c8b98a',
    'border:1px solid #c8b98a',
    'font-family:inherit',
    'font-size:13px',
    'cursor:pointer',
    canDispatch ? '' : 'opacity:0.4;cursor:not-allowed',
  ].filter(Boolean).join(';')
  dispatchBtn.textContent = UI_STRINGS.recommendButton
  dispatchBtn.disabled = !canDispatch

  if (canDispatch) {
    dispatchBtn.addEventListener('click', () => {
      if (_selectedMissionId && _selectedAdventurerId && _callbacks) {
        _callbacks.onDispatch(_selectedMissionId, _selectedAdventurerId)
        _selectedMissionId = null
        _selectedAdventurerId = null
      }
    })
  }

  bottom.appendChild(dispatchBtn)
  panel.appendChild(bottom)

  return panel
}

function buildAdventurerCard(adventurer: Adventurer): HTMLElement {
  const isSelected = _selectedAdventurerId === adventurer.id
  const isIdle = adventurer.status === 'idle'

  const li = document.createElement('li')
  li.style.cssText = [
    'padding:6px 8px',
    'margin-bottom:4px',
    'cursor:pointer',
    'border:1px solid transparent',
    isSelected ? 'background:#333;border-color:#c8b98a' : '',
    !isIdle ? 'opacity:0.5;cursor:default' : '',
  ].filter(Boolean).join(';')

  const statusLabel = adventurer.status === 'idle'
    ? UI_STRINGS.rosterStatusAvailable
    : UI_STRINGS.rosterStatusOnMission

  li.textContent = `▸ ${adventurer.name}  ${adventurer.rank}  [${statusLabel}]`

  if (isIdle) {
    li.addEventListener('click', () => {
      _selectedAdventurerId = adventurer.id
      if (_lastData) renderShell(_lastData)
    })
  }

  return li
}

// ---------------------------------------------------------------------------
// Component: SettlementOverlay
// ---------------------------------------------------------------------------

function renderOverlay(results: OutcomeResult[], longOfflineDays?: number): HTMLElement {
  const overlay = document.createElement('div')
  overlay.id = 'settlement-overlay'
  overlay.style.cssText = [
    'position:absolute',
    'inset:0',
    'background:#0d0d0d',
    'display:flex',
    'flex-direction:column',
    'z-index:100',
  ].join(';')

  // Title
  const titleBar = document.createElement('div')
  titleBar.style.cssText = 'padding:10px 16px;border-bottom:1px solid #444;font-weight:bold'
  titleBar.textContent = UI_STRINGS.settlementTitle
  overlay.appendChild(titleBar)

  // Long-offline notice
  if (longOfflineDays !== undefined && longOfflineDays > 0) {
    const notice = document.createElement('div')
    notice.style.cssText = 'padding:8px 16px;color:#ff4444;font-size:12px'
    notice.textContent = UI_STRINGS.settlementLongOffline.replace('{days}', String(longOfflineDays))
    overlay.appendChild(notice)
  }

  // Results list
  const list = document.createElement('ul')
  list.style.cssText = 'list-style:none;flex:1;overflow-y:auto;padding:12px 16px'

  results.forEach(result => {
    const li = document.createElement('li')
    li.style.cssText = 'padding:4px 0;border-bottom:1px solid #222'

    const badge = outcomeTypeBadge(result.type)
    const goldText = result.goldDelta >= 0
      ? `+${result.goldDelta}g`
      : `${result.goldDelta}g`

    li.textContent = `${badge} ${result.record.adventurerName} — "${result.record.missionName}"  ${goldText}`
    list.appendChild(li)
  })
  overlay.appendChild(list)

  // Confirm button
  const footer = document.createElement('div')
  footer.style.cssText = 'padding:10px 16px;border-top:1px solid #444;text-align:right'

  const confirmBtn = document.createElement('button')
  confirmBtn.style.cssText = [
    'padding:6px 16px',
    'background:transparent',
    'color:#c8b98a',
    'border:1px solid #c8b98a',
    'font-family:inherit',
    'font-size:13px',
    'cursor:pointer',
  ].join(';')
  confirmBtn.textContent = UI_STRINGS.settlementConfirm
  confirmBtn.addEventListener('click', () => {
    if (_callbacks) _callbacks.onSettlementDismiss()
  })

  footer.appendChild(confirmBtn)
  overlay.appendChild(footer)

  return overlay
}

function outcomeTypeBadge(type: OutcomeResult['type']): string {
  switch (type) {
    case 'SUCCESS':  return UI_STRINGS.settlementSuccess
    case 'FAILURE':  return UI_STRINGS.settlementFail
    case 'DEATH':    return UI_STRINGS.settlementDeath
    case 'PYRRHIC':  return UI_STRINGS.settlementPyrrhic
    default:         return ''
  }
}

// ---------------------------------------------------------------------------
// Component: Toolbar
// ---------------------------------------------------------------------------

function renderToolbar(): HTMLElement {
  const bar = document.createElement('div')
  bar.id = 'toolbar'
  bar.style.cssText = [
    'display:flex',
    'gap:8px',
    'padding:8px 16px',
    'border-top:1px solid #333',
    'flex-shrink:0',
    'position:relative',
  ].join(';')

  bar.appendChild(buildToolbarButton(UI_STRINGS.toolbarSave, () => {
    _callbacks?.onSave()
  }))

  bar.appendChild(buildToolbarButton(UI_STRINGS.toolbarExport, () => {
    _callbacks?.onExport()
  }))

  // Import requires a hidden file input
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

  bar.appendChild(buildToolbarButton(UI_STRINGS.toolbarImport, () => {
    importInput.click()
  }))

  if (_callbacks?.cheatCallbacks) {
    bar.appendChild(renderCheatButton(
      _callbacks.cheatCallbacks,
      _cheatPanelOpen,
      (open) => { _cheatPanelOpen = open },
    ))
  }

  return bar
}

function buildToolbarButton(label: string, onClick: () => void): HTMLElement {
  const btn = document.createElement('button')
  btn.style.cssText = [
    'padding:4px 12px',
    'background:transparent',
    'color:#c8b98a',
    'border:1px solid #555',
    'font-family:inherit',
    'font-size:13px',
    'cursor:pointer',
  ].join(';')
  btn.textContent = label
  btn.addEventListener('click', onClick)
  return btn
}

