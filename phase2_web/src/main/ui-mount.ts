/**
 * main/ui-mount.ts — UI 骨架建構與 tab 切換邏輯
 *
 * 建立 top-bar（HUD）、tab-nav、panel-area，並管理 panel mount/dispose 對稱。
 * onRefreshUI 供外部（tick-loop 等）驅動 panel 重繪。
 */

import { mountCommissionBoardPanel }  from '../ui/panels/commission-board-panel'
import { mountAdventurerRosterPanel } from '../ui/panels/adventurer-roster-panel'
import { mountGuildOverviewPanel }    from '../ui/panels/guild-overview-panel'
import { mountRecruitmentPanel }      from '../ui/panels/recruitment-panel'
import { mountGuildBuildingPanel }    from '../ui/panels/guild-building-panel'
import { mountGachaStaffPanel }       from '../ui/panels/gacha-staff-panel'
import { mountSettingsPanel }         from '../ui/panels/settings-panel'
import { mountNotificationArea }      from '../ui/notification-area'
import type { NotificationAreaHandle } from '../ui/notification-area'

import type { AppState } from './types'
import {
  makeCommissionCtx,
  makeRosterCtx,
  makeOverviewCtx,
  makeRecruitmentCtx,
  makeBuildingCtx,
  makeGachaStaffCtx,
  makeSettingsCtx,
} from './panel-ctx'
import { getGuildLevel, getGuildTitle, getReputationLabel } from './helpers'
import type { ResourceState } from '../types'

// ── Tab 設定 ──────────────────────────────────────────────────────────────────

type TabId = 'commission' | 'roster' | 'overview' | 'recruitment' | 'building' | 'gacha' | 'settings'

const TAB_CONFIG: Array<{ id: TabId; label: string }> = [
  { id: 'commission',  label: '委託板' },
  { id: 'roster',      label: '名冊' },
  { id: 'overview',    label: '公會總覽' },
  { id: 'recruitment', label: '招募' },
  { id: 'building',    label: '建設' },
  { id: 'gacha',       label: '面試職員' },
  { id: 'settings',    label: '設定' },
]

/** Panel handle 需要的最小介面 */
interface PanelHandle {
  dispose: () => void
  refresh?: () => void | Promise<void>
}

// ── 公開 API ──────────────────────────────────────────────────────────────────

export interface UIMountResult {
  notifications: NotificationAreaHandle
  /** 驅動當前 panel 重繪 */
  refreshCurrentPanel: () => void
  /** 切換到指定 tab */
  switchTab: (tabId: TabId) => void
  /** 更新 HUD 顯示 */
  refreshHUD: () => void
  /** 卸載整個 UI */
  dispose: () => void
}

/**
 * 掛載完整 UI 骨架（top-bar + tab-nav + panel-area + notification-area）。
 *
 * @param app          #app 元素
 * @param state        AppState
 * @param saveFns      存檔操作（由 main.ts 傳入）
 */
export function mountUI(
  app: HTMLElement,
  state: AppState,
  saveFns: {
    onSave: (slot: string) => Promise<void>
    onLoad: (slot: string) => Promise<boolean>
    onDelete: (slot: string) => Promise<void>
    onReset: () => Promise<void>
  },
): UIMountResult {
  // 清空 app
  app.innerHTML = ''
  app.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'height:100vh',
    'background:#111111',
    'color:#c8b98a',
    'font-family:"Courier New",Courier,monospace',
    'overflow:hidden',
  ].join(';')

  // ── Top Bar（HUD）────────────────────────────────────────────────────────────
  const topBar = document.createElement('header')
  topBar.id = 'top-bar'
  topBar.style.cssText = [
    'display:flex',
    'align-items:center',
    'justify-content:space-between',
    'padding:6px 16px',
    'background:#1a1400',
    'border-bottom:1px solid #3a2a10',
    'flex-shrink:0',
    'font-size:14px',
  ].join(';')

  // HUD: 公會名稱 + 等級稱號
  const hudTitle = document.createElement('div')
  hudTitle.id = 'hud-title'
  hudTitle.style.cssText = 'color:#f0c060;font-weight:bold;font-size:15px;'
  topBar.appendChild(hudTitle)

  // HUD: 金幣 / 聲望
  const hudStats = document.createElement('div')
  hudStats.id = 'hud-stats'
  hudStats.style.cssText = 'display:flex;gap:20px;font-size:13px;'
  topBar.appendChild(hudStats)

  // 初始 HUD 內容
  function refreshHUD(): void {
    const res: ResourceState = state.guild.resources
    const level = getGuildLevel(res.reputation)
    const title = getGuildTitle(level)
    hudTitle.textContent = `The Guild — Lv${level} ${title}`

    hudStats.innerHTML = ''

    const goldEl = document.createElement('span')
    goldEl.style.color = res.gold >= 0 ? '#f0c060' : '#e05050'
    goldEl.textContent = `金幣：${res.gold.toLocaleString()}`
    hudStats.appendChild(goldEl)

    const repEl = document.createElement('span')
    repEl.style.color = res.reputation >= 0 ? '#78c878' : '#e05050'
    repEl.textContent = `聲望：${res.reputation}（${getReputationLabel(res.reputation)}）`
    hudStats.appendChild(repEl)
  }

  refreshHUD()

  // ── Tab Nav ───────────────────────────────────────────────────────────────
  const tabNav = document.createElement('nav')
  tabNav.id = 'tab-nav'
  tabNav.style.cssText = [
    'display:flex',
    'gap:2px',
    'padding:6px 8px 0',
    'background:#1a1400',
    'border-bottom:2px solid #3a2a10',
    'flex-shrink:0',
  ].join(';')

  // ── Panel Area ────────────────────────────────────────────────────────────
  const panelArea = document.createElement('main')
  panelArea.id = 'panel-area'
  panelArea.style.cssText = [
    'flex:1',
    'overflow:auto',
    'background:#111111',
  ].join(';')
  app.appendChild(topBar)
  app.appendChild(tabNav)
  app.appendChild(panelArea)

  // ── Notification Area ─────────────────────────────────────────────────────
  const notifications = mountNotificationArea(app, {
    maxItems: 5,
    defaultAutoDismissMs: 5000,
  })

  // ── Tab 切換 ──────────────────────────────────────────────────────────────
  let currentTabId: TabId = 'commission'
  let currentPanelHandle: PanelHandle | null = null
  const tabButtonMap: Map<TabId, HTMLButtonElement> = new Map()

  function refreshCurrentPanel(): void {
    if (currentPanelHandle?.refresh) {
      void currentPanelHandle.refresh()
    }
  }

  function switchTab(tabId: TabId): void {
    // 卸載舊 panel
    if (currentPanelHandle) {
      currentPanelHandle.dispose()
      currentPanelHandle = null
    }
    panelArea.innerHTML = ''
    currentTabId = tabId

    // 更新 tab 按鈕樣式
    tabButtonMap.forEach((btn, id) => {
      const active = id === tabId
      btn.style.background = active ? '#3a2a10' : 'transparent'
      btn.style.color       = active ? '#f0c060' : '#a09070'
      btn.style.borderBottom = active ? '2px solid #f0c060' : '2px solid transparent'
    })

    // 共用的 onRefreshUI → 重繪 panel + HUD
    const onRefreshUI = () => {
      refreshHUD()
      refreshCurrentPanel()
    }

    switch (tabId) {
      case 'commission':
        currentPanelHandle = mountCommissionBoardPanel(
          panelArea,
          makeCommissionCtx(state, onRefreshUI),
        )
        break
      case 'roster':
        currentPanelHandle = mountAdventurerRosterPanel(
          panelArea,
          makeRosterCtx(state),
        )
        break
      case 'overview':
        currentPanelHandle = mountGuildOverviewPanel(
          panelArea,
          makeOverviewCtx(state),
        )
        break
      case 'recruitment':
        currentPanelHandle = mountRecruitmentPanel(
          panelArea,
          makeRecruitmentCtx(state, onRefreshUI),
        )
        break
      case 'building':
        currentPanelHandle = mountGuildBuildingPanel(
          panelArea,
          makeBuildingCtx(state, onRefreshUI),
        )
        break
      case 'gacha':
        currentPanelHandle = mountGachaStaffPanel(
          panelArea,
          makeGachaStaffCtx(state, onRefreshUI),
        )
        break
      case 'settings':
        currentPanelHandle = mountSettingsPanel(
          panelArea,
          makeSettingsCtx(
            state,
            saveFns.onSave,
            saveFns.onLoad,
            saveFns.onDelete,
            saveFns.onReset,
          ),
        )
        break
    }
  }

  // 建立 tab 按鈕
  for (const { id, label } of TAB_CONFIG) {
    const btn = document.createElement('button')
    btn.textContent = label
    btn.dataset.tab = id
    btn.style.cssText = [
      'padding:6px 14px',
      'border:none',
      'background:transparent',
      'color:#a09070',
      'font-family:inherit',
      'font-size:13px',
      'cursor:pointer',
      'border-bottom:2px solid transparent',
      'transition:color 0.15s, border-color 0.15s',
    ].join(';')
    btn.addEventListener('mouseenter', () => {
      if (id !== currentTabId) btn.style.color = '#c8b98a'
    })
    btn.addEventListener('mouseleave', () => {
      if (id !== currentTabId) btn.style.color = '#a09070'
    })
    btn.addEventListener('click', () => switchTab(id))
    tabNav.appendChild(btn)
    tabButtonMap.set(id, btn)
  }

  // 預設 tab
  switchTab('commission')

  function dispose(): void {
    if (currentPanelHandle) {
      currentPanelHandle.dispose()
      currentPanelHandle = null
    }
    notifications.dispose()
    app.innerHTML = ''
  }

  return { notifications, refreshCurrentPanel, switchTab, refreshHUD, dispose }
}
