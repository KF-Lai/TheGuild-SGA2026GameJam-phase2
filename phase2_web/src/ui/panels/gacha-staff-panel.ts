/**
 * GachaStaffPanel — Stage 5.6
 *
 * 面試 + 職員名冊 panel，結合 FT-08（Gacha）與 FT-12（Staff）。
 * 上半：面試候選池（錄用 / 不錄用 / 手動刷新）
 * 下半：職員名冊（切換指派建築 / 解雇）
 *
 * Jam 簡化延後項目：
 *   - 手動刷新冷卻倒數顯示：目前僅顯示費用（200g），不顯示剩餘秒數。
 *     完整版需 getStaffLoungeRefreshSec() 注入 + 計時器更新。
 *   - 解雇資遣費動態查詢：目前 hardcode（米拉/凱拉 80g，譚恩 120g）。
 *     完整版需 staff.ts 暴露 getSeverancePay(staffID) API。
 *   - 職員離職三態（Working / OnLeave / Resigned）：Jam 全程 Working，無三態 UI。
 *   - 薪水結算顯示：目前 panel 不顯示薪水欄位（triggerDailySalary 未被觸發）。
 */

import type { GuildState } from '../../types'
import type { GachaState, GachaCandidateCard, RecruitResult, ManualRefreshResult } from '../../systems/gacha'
import type { StaffRosterState, StaffInstanceFull, AssignResult, FireResult } from '../../systems/staff'
import {
  getStaffWillingnessBonus,
  getAccountantCommissionBonus,
  getAccountantPenaltyBonus,
  getRecruitRefreshReductionSec,
  isSuccessRatePreviewEnabled,
} from '../../systems/staff'
import { eventBus } from '../../core/events'

// ---------------------------------------------------------------------------
// 公開介面
// ---------------------------------------------------------------------------

export interface GachaStaffPanelCtx {
  getGuild: () => GuildState
  getGacha: () => GachaState
  getStaffRoster: () => StaffRosterState
  /** 取 staff 系統是否已解鎖（FT-07.IsStaffSystemUnlocked） */
  isStaffSystemUnlocked: () => boolean
  /** 取目前金幣（用於 manualRefresh 按鈕 disable 判斷） */
  getGold: () => number

  /** 玩家點「錄用」：傳 candidateID + buildingID → main.ts 呼叫 gacha.tryRecruit */
  onRecruit: (candidateID: number, buildingID: number) => RecruitResult
  /** 玩家點「不錄用」：呼叫 gacha.dismissCandidate */
  onDismiss: (candidateID: number) => boolean
  /** 玩家點「刷新」：呼叫 gacha.tryManualRefresh */
  onManualRefresh: () => ManualRefreshResult

  /** 玩家點「切換指派」：呼叫 staff.tryAssignStaff */
  onAssign: (instanceId: string, buildingID: number) => AssignResult
  /** 玩家點「解雇」：呼叫 staff.tryFireStaff */
  onFire: (instanceId: string) => FireResult
}

export interface GachaStaffPanelHandle {
  root: HTMLElement
  dispose: () => void
  refresh: () => void
}

// ---------------------------------------------------------------------------
// Jam 三角色資料（hardcode，與 staff.ts STAFF_TABLE 對齊）
// ---------------------------------------------------------------------------

/** 三角色的 slotBuildingIDs 與效果描述 */
const STAFF_BUILDING_INFO: Record<number, { slotBuildingIDs: number[]; effectDesc: string }> = {
  501: { slotBuildingIDs: [1],  effectDesc: '委託板：意願 +5%、成功率預覽' },
  502: { slotBuildingIDs: [5],  effectDesc: '預備金保險櫃：傭金 +2%、賠償 -2%' },
  503: { slotBuildingIDs: [4],  effectDesc: '公會櫃臺：招募刷新 -2 小時' },
}

const BUILDING_NAMES: Record<number, string> = {
  1: '委託板',
  4: '公會櫃臺',
  5: '預備金保險櫃',
}

/** 各角色解雇資遣費（hardcode；與 staff.ts severancePay 同值） */
const SEVERANCE_PAY: Record<number, number> = {
  501: 80,
  502: 120,
  503: 80,
}

/** 各角色立繪檔名（不含 .png；對應 public/images/characters/staff/） */
const STAFF_PORTRAIT_NAMES: Record<number, string> = {
  501: 'mira_default',
  502: 'tan_default',
  503: 'kaira_default',
}
const STAFF_PORTRAIT_BASE = '/images/characters/staff/'

/** 建立 staff portrait img；查無對映則回傳隱藏占位 */
function buildStaffPortraitImg(staffID: number, alt: string): HTMLImageElement {
  const img = document.createElement('img')
  const portrait = STAFF_PORTRAIT_NAMES[staffID]
  if (portrait) {
    img.src = `${STAFF_PORTRAIT_BASE}${portrait}.png`
    img.alt = alt
  }
  img.style.cssText = [
    'width:36px',
    'height:36px',
    'border-radius:4px',
    'object-fit:cover',
    'object-position:top center',
    'flex-shrink:0',
    'border:1px solid #4a4a4a',
  ].join(';')
  img.onerror = () => { img.style.display = 'none' }
  return img
}

/** 建築邊框顏色（依建築 ID） */
const BUILDING_BORDER_COLOR: Record<number, string> = {
  1: '#40805040',  // 委託板 — 綠
  4: '#3060a040',  // 公會櫃臺 — 藍
  5: '#5040a040',  // 保險櫃 — 紫
}

const BUILDING_ACCENT_COLOR: Record<number, string> = {
  1: '#70b870',  // 綠
  4: '#6090d0',  // 藍
  5: '#9070d0',  // 紫
}

const MANUAL_REFRESH_COST = 200

// ---------------------------------------------------------------------------
// 樣式常數
// ---------------------------------------------------------------------------

const BG_PANEL    = '#1a1a1f'
const BG_CARD     = '#22222a'
const TEXT_MAIN   = '#e8d8a0'
const TEXT_DIM    = '#888888'
const TEXT_WARN   = '#c87040'
const BTN_PRIMARY_BG    = '#2a3a20'
const BTN_PRIMARY_COLOR = '#90d060'
const BTN_DISMISS_BG    = '#2a1a10'
const BTN_DISMISS_COLOR = '#c08060'
const BTN_FIRE_BG       = '#3a1010'
const BTN_FIRE_COLOR    = '#d04040'
const RARITY_COLORS = ['#888888', '#60a8d0', '#60c880', '#d0a030', '#cc5555', '#cc44cc']

// ---------------------------------------------------------------------------
// 小工具函式
// ---------------------------------------------------------------------------

/** 建立有 cssText 的 HTMLElement */
function el(tag: string, cssText?: string, text?: string): HTMLElement {
  const e = document.createElement(tag)
  if (cssText) e.style.cssText = cssText
  if (text !== undefined) e.textContent = text
  return e
}

/** 建立 div */
function div(cssText?: string, text?: string): HTMLDivElement {
  return el('div', cssText, text) as HTMLDivElement
}

/** 稀有度星星字串（rarity=4 → ★★★★） */
function rarityStars(rarity: number): string {
  return '★'.repeat(Math.max(0, rarity))
}

/** 取稀有度顏色 */
function rarityColor(rarity: number): string {
  return RARITY_COLORS[Math.min(rarity, RARITY_COLORS.length - 1)] ?? '#888888'
}

/** 建立按鈕 */
function buildBtn(
  text: string,
  bg: string,
  color: string,
  hoverBg: string,
): HTMLButtonElement {
  const btn = document.createElement('button')
  btn.textContent = text
  btn.style.cssText = [
    `background:${bg}`,
    `color:${color}`,
    'border:1px solid currentColor',
    'border-radius:4px',
    'padding:4px 12px',
    'font-size:12px',
    'cursor:pointer',
    'transition:background 0.15s',
    'white-space:nowrap',
    'opacity:1',
  ].join(';')
  btn.addEventListener('mouseenter', () => { if (!btn.disabled) btn.style.background = hoverBg })
  btn.addEventListener('mouseleave', () => { if (!btn.disabled) btn.style.background = bg })
  return btn
}

/** 設定 button disabled 狀態並調整外觀 */
function setDisabled(btn: HTMLButtonElement, disabled: boolean): void {
  btn.disabled = disabled
  btn.style.opacity = disabled ? '0.35' : '1'
  btn.style.cursor  = disabled ? 'not-allowed' : 'pointer'
}

/** 建立 select dropdown */
function buildSelect(
  options: Array<{ value: string; label: string }>,
  selectedValue: string,
): HTMLSelectElement {
  const sel = document.createElement('select')
  sel.style.cssText = [
    `background:${BG_PANEL}`,
    `color:${TEXT_MAIN}`,
    'border:1px solid #3a3a4a',
    'border-radius:4px',
    'padding:3px 6px',
    'font-size:12px',
    'cursor:pointer',
    'width:100%',
  ].join(';')
  for (const opt of options) {
    const o = document.createElement('option')
    o.value = opt.value
    o.textContent = opt.label
    if (opt.value === selectedValue) o.selected = true
    sel.appendChild(o)
  }
  return sel
}

// ---------------------------------------------------------------------------
// 子元件：加成總覽 chip 列
// ---------------------------------------------------------------------------

function buildBonusSection(roster: StaffRosterState): HTMLElement {
  const section = div([
    'display:flex',
    'flex-wrap:wrap',
    'gap:6px',
    'padding:8px 12px',
    'border-bottom:1px solid #2a2a3a',
    'background:#1a1a28',
  ].join(';'))

  const label = div('font-size:11px;color:#555;margin-right:4px;align-self:center', '當前加成：')
  section.appendChild(label)

  const willing = getStaffWillingnessBonus(roster)
  const commission = getAccountantCommissionBonus(roster)
  const penalty = getAccountantPenaltyBonus(roster)
  const refreshSec = getRecruitRefreshReductionSec(roster)
  const previewEnabled = isSuccessRatePreviewEnabled(roster)

  const chips: Array<{ label: string; active: boolean }> = [
    { label: `意願 +${Math.round(willing * 100)}%`,         active: willing !== 0 },
    { label: `傭金 +${Math.round(commission * 100)}%`,      active: commission !== 0 },
    { label: `賠償 ${Math.round(penalty * 100)}%`,          active: penalty !== 0 },
    { label: `刷新 -${Math.round(refreshSec / 3600)}h`,     active: refreshSec !== 0 },
    { label: `成功率預覽：${previewEnabled ? '開' : '關'}`, active: previewEnabled },
  ]

  for (const chip of chips) {
    const c = div([
      'padding:2px 7px',
      'border-radius:3px',
      'font-size:11px',
      chip.active
        ? 'background:#2a3a20;border:1px solid #60903060;color:#90c060'
        : 'background:#202028;border:1px solid #2a2a38;color:#555',
    ].join(';'), chip.label)
    section.appendChild(c)
  }

  return section
}

// ---------------------------------------------------------------------------
// 子元件：候選人卡片
// ---------------------------------------------------------------------------

function buildCandidateCard(
  candidate: GachaCandidateCard,
  isUnlocked: boolean,
  ctx: GachaStaffPanelCtx,
  onRefreshPanel: () => void,
): HTMLElement {
  const buildingInfo = STAFF_BUILDING_INFO[candidate.staffID]
  const slotBuildingIDs = buildingInfo?.slotBuildingIDs ?? []
  const effectDesc = buildingInfo?.effectDesc ?? '（未知效果）'

  const card = div([
    'padding:10px 12px',
    'border:1px solid #2a2a3a',
    'border-radius:6px',
    `background:${BG_CARD}`,
    'display:flex',
    'flex-direction:column',
    'gap:6px',
  ].join(';'))

  // ── 行 1：portrait + 稀有度星 + 名稱 ──
  const row1 = div('display:flex;align-items:center;gap:8px')
  row1.appendChild(buildStaffPortraitImg(candidate.staffID, candidate.name))
  const starsEl = div('font-size:13px;flex-shrink:0', rarityStars(candidate.rarity))
  starsEl.style.color = rarityColor(candidate.rarity)
  row1.appendChild(starsEl)
  row1.appendChild(div([
    'font-size:14px',
    'font-weight:bold',
    `color:${TEXT_MAIN}`,
    'flex:1',
  ].join(';'), candidate.name))
  card.appendChild(row1)

  // ── 行 2：效果描述 ──
  card.appendChild(div(`font-size:11px;color:#7a8a6a`, effectDesc))

  // ── 行 3：建築 dropdown（僅一個 slotBuilding，但仍用 select 保持一致） ──
  const dropOpts = slotBuildingIDs.map(bid => ({
    value: String(bid),
    label: BUILDING_NAMES[bid] ?? `建築 ${bid}`,
  }))
  const defaultBid = String(slotBuildingIDs[0] ?? '')
  const buildingSelect = buildSelect(dropOpts, defaultBid)
  card.appendChild(buildingSelect)

  // ── 行 4：按鈕列 ──
  const btnRow = div('display:flex;gap:8px')

  const recruitBtn = buildBtn('錄用', BTN_PRIMARY_BG, BTN_PRIMARY_COLOR, '#3a5030')
  const dismissBtn = buildBtn('不錄用', BTN_DISMISS_BG, BTN_DISMISS_COLOR, '#3a2a18')

  setDisabled(recruitBtn, !isUnlocked)
  setDisabled(dismissBtn, !isUnlocked)

  recruitBtn.addEventListener('click', () => {
    const bid = parseInt(buildingSelect.value, 10)
    if (!bid) {
      alert('請選擇指派建築')
      return
    }
    const result = ctx.onRecruit(candidate.candidateID, bid)
    if (result === 'SUCCESS') {
      onRefreshPanel()
    } else if (result === 'BUILDING_FULL') {
      alert('該建築職員已滿')
    } else if (result === 'BUILDING_NOT_ELIGIBLE') {
      alert('此職員無法指派到該建築')
    } else {
      // STAFF_SYSTEM_LOCKED / CANDIDATE_NOT_FOUND / INVALID_STAFF_ID
      alert(`錄用失敗：${result}`)
    }
  })

  dismissBtn.addEventListener('click', () => {
    ctx.onDismiss(candidate.candidateID)
    onRefreshPanel()
  })

  btnRow.appendChild(recruitBtn)
  btnRow.appendChild(dismissBtn)
  card.appendChild(btnRow)

  return card
}

// ---------------------------------------------------------------------------
// 子元件：面試候選池 section
// ---------------------------------------------------------------------------

function buildInterviewSection(
  gacha: GachaState,
  isUnlocked: boolean,
  gold: number,
  ctx: GachaStaffPanelCtx,
  onRefreshPanel: () => void,
): HTMLElement {
  const section = div([
    'display:flex',
    'flex-direction:column',
    'gap:8px',
    'padding:12px',
  ].join(';'))

  // ── 小標題列：標題 + 候選數 + 刷新按鈕 ──
  const titleRow = div('display:flex;align-items:center;gap:8px;margin-bottom:4px')

  titleRow.appendChild(div([
    'font-size:14px',
    'font-weight:bold',
    `color:${TEXT_MAIN}`,
    'letter-spacing:1px',
  ].join(';'), '[ 面試候選 ]'))

  const countBadge = div([
    'font-size:12px',
    `color:${TEXT_DIM}`,
    'flex:1',
  ].join(';'), `候選：${gacha.currentCandidates.length} 位`)
  titleRow.appendChild(countBadge)

  const refreshBtn = buildBtn(`刷新（${MANUAL_REFRESH_COST}g）`, '#1a1a2a', '#6080c0', '#252535')
  setDisabled(refreshBtn, !isUnlocked || gold < MANUAL_REFRESH_COST)
  refreshBtn.title = gold < MANUAL_REFRESH_COST
    ? `金幣不足（需 ${MANUAL_REFRESH_COST}g）`
    : `手動刷新候選池（費用 ${MANUAL_REFRESH_COST}g）`
  refreshBtn.addEventListener('click', () => {
    const result = ctx.onManualRefresh()
    if (result === 'GOLD_INSUFFICIENT') {
      alert(`金幣不足，無法刷新（需 ${MANUAL_REFRESH_COST}g）`)
      return
    }
    if (result === 'STAFF_SYSTEM_LOCKED') {
      alert('職員系統尚未解鎖')
      return
    }
    // SUCCESS：事件 gacha:refreshed 會觸發 panel refresh
    onRefreshPanel()
  })
  titleRow.appendChild(refreshBtn)
  section.appendChild(titleRow)

  // ── 候選卡列表 ──
  if (gacha.currentCandidates.length === 0) {
    section.appendChild(div([
      'padding:16px',
      'text-align:center',
      `color:${TEXT_DIM}`,
      'font-size:13px',
      'border:1px dashed #2a2a3a',
      'border-radius:4px',
    ].join(';'), '目前無候選，可手動刷新'))
    return section
  }

  const cardGrid = div([
    'display:grid',
    'grid-template-columns:repeat(auto-fill,minmax(200px,1fr))',
    'gap:8px',
  ].join(';'))

  for (const candidate of gacha.currentCandidates) {
    cardGrid.appendChild(buildCandidateCard(candidate, isUnlocked, ctx, onRefreshPanel))
  }

  section.appendChild(cardGrid)
  return section
}

// ---------------------------------------------------------------------------
// 子元件：職員名冊 card
// ---------------------------------------------------------------------------

function buildStaffCard(
  staff: StaffInstanceFull,
  isUnlocked: boolean,
  ctx: GachaStaffPanelCtx,
  onRefreshPanel: () => void,
): HTMLElement {
  const buildingInfo = STAFF_BUILDING_INFO[staff.staffID]
  const slotBuildingIDs = buildingInfo?.slotBuildingIDs ?? []
  const effectDesc = buildingInfo?.effectDesc ?? '（未知效果）'
  const severancePay = SEVERANCE_PAY[staff.staffID] ?? 0

  const accentColor = BUILDING_ACCENT_COLOR[staff.assignedBuildingID] ?? '#666666'
  const borderBg    = BUILDING_BORDER_COLOR[staff.assignedBuildingID] ?? '#2a2a3a20'

  const card = div([
    'padding:10px 12px',
    `border:1px solid ${accentColor}55`,
    'border-radius:6px',
    `background:${BG_CARD}`,
    `box-shadow:0 0 8px ${borderBg}`,
    'display:flex',
    'flex-direction:column',
    'gap:6px',
  ].join(';'))

  // ── 行 1：portrait + 稀有度星 + 名稱 + 當前指派建築 ──
  const row1 = div('display:flex;align-items:center;gap:8px')
  row1.appendChild(buildStaffPortraitImg(staff.staffID, staff.name))
  const starsEl = div('font-size:13px;flex-shrink:0', rarityStars(staff.rarity))
  starsEl.style.color = rarityColor(staff.rarity)
  row1.appendChild(starsEl)
  row1.appendChild(div([
    'font-size:14px',
    'font-weight:bold',
    `color:${TEXT_MAIN}`,
    'flex:1',
  ].join(';'), staff.name))

  const currentBuildingName = staff.assignedBuildingID
    ? (BUILDING_NAMES[staff.assignedBuildingID] ?? `建築${staff.assignedBuildingID}`)
    : '（未指派）'
  row1.appendChild(div([
    'font-size:11px',
    `color:${accentColor}`,
    'padding:1px 5px',
    'border-radius:3px',
    `background:${accentColor}22`,
    'white-space:nowrap',
  ].join(';'), currentBuildingName))
  card.appendChild(row1)

  // ── 行 2：效果描述 ──
  card.appendChild(div(`font-size:11px;color:#7a8a6a`, effectDesc))

  // ── 行 3：切換指派 dropdown ──
  const assignLabel = div(`font-size:11px;color:${TEXT_DIM};margin-bottom:2px`, '指派建築：')
  card.appendChild(assignLabel)

  const assignOpts = slotBuildingIDs.map(bid => ({
    value: String(bid),
    label: BUILDING_NAMES[bid] ?? `建築 ${bid}`,
  }))
  const assignSelect = buildSelect(assignOpts, String(staff.assignedBuildingID))
  assignSelect.disabled = !isUnlocked

  assignSelect.addEventListener('change', () => {
    const newBid = parseInt(assignSelect.value, 10)
    if (!newBid) return
    const result = ctx.onAssign(staff.instanceId, newBid)
    if (result !== 'SUCCESS') {
      // 還原 select 顯示
      assignSelect.value = String(staff.assignedBuildingID)
      if (result === 'BUILDING_FULL') {
        alert('該建築職員已滿')
      } else if (result === 'BUILDING_NOT_ELIGIBLE') {
        alert('此職員無法指派到該建築')
      }
    }
    onRefreshPanel()
  })
  card.appendChild(assignSelect)

  // ── 行 4：解雇按鈕 ──
  const fireBtn = buildBtn(`解雇（賠 ${severancePay}g）`, BTN_FIRE_BG, BTN_FIRE_COLOR, '#4a1818')
  setDisabled(fireBtn, !isUnlocked)

  fireBtn.addEventListener('click', () => {
    const confirmed = confirm(
      `確認解雇「${staff.name}」？\n將支付 ${severancePay}g 資遣費，此操作不可逆。`
    )
    if (!confirmed) return

    const result = ctx.onFire(staff.instanceId)
    if (result === 'GOLD_INSUFFICIENT') {
      alert(`金幣不足，無法支付 ${severancePay}g 資遣費`)
      return
    }
    if (result === 'STAFF_NOT_FOUND') {
      // 不應發生，防禦日誌
      console.error(`[GachaStaffPanel] tryFireStaff: instanceId=${staff.instanceId} 找不到`)
      return
    }
    // SUCCESS 或 STAFF_SYSTEM_LOCKED：
    // 事件 staff:fired 會觸發 panel refresh
    onRefreshPanel()
  })
  card.appendChild(fireBtn)

  return card
}

// ---------------------------------------------------------------------------
// 子元件：職員名冊 section
// ---------------------------------------------------------------------------

function buildRosterSection(
  roster: StaffRosterState,
  isUnlocked: boolean,
  ctx: GachaStaffPanelCtx,
  onRefreshPanel: () => void,
): HTMLElement {
  const section = div([
    'display:flex',
    'flex-direction:column',
    'gap:8px',
    'padding:12px',
  ].join(';'))

  // ── 標題 ──
  section.appendChild(div([
    'font-size:14px',
    'font-weight:bold',
    `color:${TEXT_MAIN}`,
    'letter-spacing:1px',
    'margin-bottom:4px',
  ].join(';'), `[ 職員名冊 ] ${roster.roster.length}/3`))

  if (roster.roster.length === 0) {
    section.appendChild(div([
      'padding:16px',
      'text-align:center',
      `color:${TEXT_DIM}`,
      'font-size:13px',
      'border:1px dashed #2a2a3a',
      'border-radius:4px',
    ].join(';'), '尚未錄用任何職員'))
    return section
  }

  const cardGrid = div([
    'display:grid',
    'grid-template-columns:repeat(auto-fill,minmax(220px,1fr))',
    'gap:8px',
  ].join(';'))

  for (const staff of roster.roster) {
    cardGrid.appendChild(buildStaffCard(staff, isUnlocked, ctx, onRefreshPanel))
  }

  section.appendChild(cardGrid)
  return section
}

// ---------------------------------------------------------------------------
// 主元件：mountGachaStaffPanel
// ---------------------------------------------------------------------------

export function mountGachaStaffPanel(
  parent: HTMLElement,
  ctx: GachaStaffPanelCtx,
): GachaStaffPanelHandle {

  // ── 根容器 ──
  const root = div([
    'display:flex',
    'flex-direction:column',
    'height:100%',
    `background:${BG_PANEL}`,
    `color:#e0e0e0`,
    'font-family:monospace,sans-serif',
    'overflow:hidden',
  ].join(';'))

  parent.appendChild(root)

  // ── 主渲染函式 ──
  function render(): void {
    root.innerHTML = ''

    const isUnlocked = ctx.isStaffSystemUnlocked()
    const gacha      = ctx.getGacha()
    const roster     = ctx.getStaffRoster()
    const gold       = ctx.getGold()

    // ── Header ──
    const header = div([
      'display:flex',
      'align-items:center',
      'justify-content:space-between',
      'padding:12px 16px 8px',
      'flex-shrink:0',
      'border-bottom:1px solid #2a2a3a',
    ].join(';'))

    const gachaHeroIcon = document.createElement('img')
    gachaHeroIcon.src = '/images/scene/A-06_default.png'
    gachaHeroIcon.alt = ''
    gachaHeroIcon.style.cssText = 'height:36px;object-fit:contain;flex-shrink:0;margin-right:8px'
    gachaHeroIcon.onerror = () => { gachaHeroIcon.style.display = 'none' }

    const gachaTitleWrap = div('display:flex;align-items:center')
    gachaTitleWrap.appendChild(gachaHeroIcon)
    const titleEl = div([
      'font-size:15px',
      'font-weight:bold',
      `color:${TEXT_MAIN}`,
      'letter-spacing:2px',
    ].join(';'), '面試 / 職員名冊')
    gachaTitleWrap.appendChild(titleEl)
    header.appendChild(gachaTitleWrap)

    if (!isUnlocked) {
      const lockWarn = div([
        'font-size:12px',
        `color:${TEXT_WARN}`,
        'display:flex',
        'align-items:center',
        'gap:4px',
      ].join(';'), '[ 需先建造職員休息室 ]')
      header.appendChild(lockWarn)
    }

    root.appendChild(header)

    // ── 加成總覽 ──
    root.appendChild(buildBonusSection(roster))

    // ── 捲動主體 ──
    const scrollBody = div([
      'flex:1',
      'overflow-y:auto',
      'min-height:0',
      isUnlocked ? '' : 'opacity:0.4;pointer-events:none',
    ].filter(Boolean).join(';'))

    // 面試候選池
    scrollBody.appendChild(
      buildInterviewSection(gacha, isUnlocked, gold, ctx, render)
    )

    // 分隔線
    const divider = div([
      'height:1px',
      'margin:0 12px',
      'background:linear-gradient(to right,transparent,#3a3a4a,transparent)',
    ].join(';'))
    scrollBody.appendChild(divider)

    // 職員名冊
    scrollBody.appendChild(
      buildRosterSection(roster, isUnlocked, ctx, render)
    )

    root.appendChild(scrollBody)
  }

  // 初次渲染
  render()

  // ── 事件訂閱 ──

  const onRefresh = () => render()

  eventBus.on('gacha:refreshed',   onRefresh)
  eventBus.on('gacha:hired',       onRefresh)
  eventBus.on('staff:hired',       onRefresh)
  eventBus.on('staff:fired',       onRefresh)
  eventBus.on('staff:assigned',    onRefresh)
  eventBus.on('gold:changed',      onRefresh)
  eventBus.on('building:upgraded', onRefresh)

  // ── dispose ──
  function dispose(): void {
    eventBus.off('gacha:refreshed',   onRefresh)
    eventBus.off('gacha:hired',       onRefresh)
    eventBus.off('staff:hired',       onRefresh)
    eventBus.off('staff:fired',       onRefresh)
    eventBus.off('staff:assigned',    onRefresh)
    eventBus.off('gold:changed',      onRefresh)
    eventBus.off('building:upgraded', onRefresh)
    root.remove()
  }

  return { root, dispose, refresh: render }
}
