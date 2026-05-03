/**
 * Stage 5.7 — SettlementOverlay（任務結算彈窗）
 *
 * Phase 2 升級版結算彈窗，沿用 phase 1 邏輯並加入新欄位：
 *   - adventurerName / missionName / outcome icon
 *   - goldDelta（+綠 / -紅）
 *   - reputationDelta
 *   - wounded 標記（woundedUntil 存在時顯示）
 *
 * 防禦：records 空陣列時直接呼叫 onDismissAll，不顯示 overlay。
 * ESC 鍵觸發 onDismissAll；backdrop 點擊不關閉（強制按全部確認）。
 * 每次呼叫重新建立 DOM，確認後自動移除，不殘留。
 */

import type { SettlementRecord } from '../../systems/outcome'

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

export interface SettlementOverlayProps {
  records: SettlementRecord[]
  onDismissAll: () => void
}

// ---------------------------------------------------------------------------
// Outcome 顯示設定
// ---------------------------------------------------------------------------

interface OutcomeDisplay {
  icon:  string
  color: string
  label: string
}

const OUTCOME_DISPLAY: Record<string, OutcomeDisplay> = {
  SUCCESS:  { icon: '✓',  color: '#78c878', label: '成功' },
  PYRRHIC:  { icon: '⚠',  color: '#d09040', label: '慘勝' },
  FAILURE:  { icon: '✗',  color: '#d04040', label: '失敗' },
  DEATH:    { icon: '💀', color: '#888888', label: '死亡' },
}

const FALLBACK_DISPLAY: OutcomeDisplay = { icon: '?', color: '#888888', label: '未知' }

// ---------------------------------------------------------------------------
// 公開 API
// ---------------------------------------------------------------------------

/**
 * 顯示結算彈窗。
 * records 為空時直接觸發 onDismissAll，不建立 DOM。
 */
export function showSettlementOverlay(props: SettlementOverlayProps): void {
  const { records, onDismissAll } = props

  // 防禦：空陣列直接回呼
  if (records.length === 0) {
    onDismissAll()
    return
  }

  let dismissed = false

  function dismiss(): void {
    if (dismissed) return
    dismissed = true
    document.removeEventListener('keydown', onKeyDown)
    overlay.remove()
    onDismissAll()
  }

  // ESC 鍵 = 全部確認
  function onKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Escape') {
      e.preventDefault()
      dismiss()
    }
  }
  document.addEventListener('keydown', onKeyDown)

  // -------------------------------------------------------------------------
  // DOM：backdrop（固定滿屏遮罩，點擊不關閉）
  // -------------------------------------------------------------------------
  const overlay = document.createElement('div')
  overlay.style.cssText = [
    'position:fixed',
    'inset:0',
    'background:rgba(0,0,0,0.7)',
    'z-index:9999',
    'display:flex',
    'align-items:center',
    'justify-content:center',
  ].join(';')

  // backdrop 點擊不關閉
  overlay.addEventListener('click', (e) => e.stopPropagation())

  // -------------------------------------------------------------------------
  // DOM：內容卡
  // -------------------------------------------------------------------------
  const card = document.createElement('div')
  card.style.cssText = [
    'background:#1a1a1a',
    'border:1px solid #4a4a4a',
    'border-radius:8px',
    'padding:0',
    'width:min(560px,92vw)',
    'max-height:80vh',
    'display:flex',
    'flex-direction:column',
    'color:#e8d8a0',
    "font-family:'Segoe UI',sans-serif",
    'box-shadow:0 16px 60px rgba(0,0,0,0.9)',
    'overflow:hidden',
  ].join(';')

  card.appendChild(buildHeader(records.length))
  card.appendChild(buildRecordList(records))
  card.appendChild(buildFooter(dismiss))

  overlay.appendChild(card)
  document.body.appendChild(overlay)
}

// ---------------------------------------------------------------------------
// 內部建構函式
// ---------------------------------------------------------------------------

/** header：「結算結果（N 筆）」 */
function buildHeader(count: number): HTMLElement {
  const header = document.createElement('div')
  header.style.cssText = [
    'padding:14px 20px',
    'border-bottom:1px solid #2a2a3a',
    'background:#141420',
    'flex-shrink:0',
    'display:flex',
    'align-items:center',
    'gap:8px',
  ].join(';')

  const title = document.createElement('span')
  title.style.cssText = 'font-size:15px;font-weight:bold;color:#e8d8a0;letter-spacing:1px'
  title.textContent = `結算結果（${count} 筆）`

  header.appendChild(title)
  return header
}

/** 結算記錄清單（可捲動） */
function buildRecordList(records: SettlementRecord[]): HTMLElement {
  const list = document.createElement('div')
  list.style.cssText = [
    'flex:1',
    'overflow-y:auto',
    'padding:12px 16px',
    'display:flex',
    'flex-direction:column',
    'gap:8px',
  ].join(';')

  records.forEach(record => {
    list.appendChild(buildRecordRow(record))
  })

  return list
}

/** 單筆結算記錄行 */
function buildRecordRow(record: SettlementRecord): HTMLElement {
  const disp = OUTCOME_DISPLAY[record.outcome] ?? FALLBACK_DISPLAY

  const row = document.createElement('div')
  row.style.cssText = [
    'padding:10px 14px',
    'border:1px solid #2a2a3a',
    'border-radius:6px',
    'background:#131318',
    'display:flex',
    'align-items:center',
    'gap:10px',
    'font-size:14px',
    'flex-wrap:wrap',
  ].join(';')

  // outcome icon + 標籤
  const outcomeEl = document.createElement('span')
  outcomeEl.style.cssText = `color:${disp.color};font-weight:bold;flex-shrink:0;min-width:40px`
  outcomeEl.textContent = `${disp.icon} ${disp.label}`

  // 任務名 + 冒險者名
  const infoEl = document.createElement('span')
  infoEl.style.cssText = 'flex:1;color:#c8b98a;min-width:120px'
  infoEl.textContent = buildInfoText(record)

  // goldDelta
  const goldEl = document.createElement('span')
  const goldPositive = record.goldDelta >= 0
  const goldAbs = Math.abs(record.goldDelta)
  goldEl.style.cssText = `color:${goldPositive ? '#78c878' : '#d04040'};font-weight:bold;flex-shrink:0`
  goldEl.textContent = goldPositive ? `+${goldAbs}g` : `-${goldAbs}g`

  // reputationDelta
  const repEl = document.createElement('span')
  const repPositive = record.reputationDelta >= 0
  repEl.style.cssText = `color:${repPositive ? '#90b8d0' : '#ee8844'};font-size:13px;flex-shrink:0`
  repEl.textContent = repPositive
    ? `+${record.reputationDelta}聲`
    : `${record.reputationDelta}聲`

  row.appendChild(outcomeEl)
  row.appendChild(infoEl)
  row.appendChild(repEl)
  row.appendChild(goldEl)

  // wounded 標記（FAILURE + woundedUntil 存在）
  if (record.woundedUntil !== undefined) {
    const woundedEl = buildWoundedBadge(record.woundedUntil)
    // 換行顯示（新增一整行）
    row.style.flexWrap = 'wrap'
    row.appendChild(woundedEl)
  }

  return row
}

function buildInfoText(record: SettlementRecord): string {
  return `${record.adventurerName} — ${record.missionName}`
}

/** wounded 標記（橙色，顯示預計恢復時間） */
function buildWoundedBadge(woundedUntil: number): HTMLElement {
  const badge = document.createElement('span')
  badge.style.cssText = [
    'color:#d09040',
    'font-size:12px',
    'border:1px solid #d09040',
    'border-radius:4px',
    'padding:1px 6px',
    'flex-basis:100%',
    'margin-top:4px',
  ].join(';')

  const now         = Date.now()
  const remainingMs = woundedUntil - now
  const label       = remainingMs > 0 ? `受傷，恢復：${formatDuration(remainingMs)}` : '傷勢已恢復'
  badge.textContent = `⚕ ${label}`
  return badge
}

/** footer：「全部確認」按鈕 */
function buildFooter(onConfirm: () => void): HTMLElement {
  const footer = document.createElement('div')
  footer.style.cssText = [
    'padding:12px 20px',
    'border-top:1px solid #2a2a3a',
    'background:#141420',
    'flex-shrink:0',
    'display:flex',
    'justify-content:flex-end',
  ].join(';')

  const btn = document.createElement('button')
  btn.style.cssText = buildBtnStyle('#c8b98a', '#2a2a4a', '#c8b98a')
  btn.textContent = '全部確認'
  btn.addEventListener('click', onConfirm)
  addHoverStyle(btn, '#3a3a5a')

  footer.appendChild(btn)
  return footer
}

// ---------------------------------------------------------------------------
// 樣式輔助
// ---------------------------------------------------------------------------

function buildBtnStyle(borderColor: string, bgColor: string, textColor: string): string {
  return [
    `border:1px solid ${borderColor}`,
    `background:${bgColor}`,
    `color:${textColor}`,
    'border-radius:5px',
    'padding:8px 24px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
  ].join(';')
}

function addHoverStyle(btn: HTMLButtonElement, hoverBg: string): void {
  const originalBg = btn.style.background
  btn.addEventListener('mouseenter', () => { btn.style.background = hoverBg })
  btn.addEventListener('mouseleave', () => { btn.style.background = originalBg })
}

/** 將毫秒格式化為「X 小時 Y 分」或「Y 分」 */
function formatDuration(ms: number): string {
  const totalMin = Math.ceil(ms / 60_000)
  const hours    = Math.floor(totalMin / 60)
  const minutes  = totalMin % 60
  if (hours > 0 && minutes > 0) return `${hours} 小時 ${minutes} 分`
  if (hours > 0) return `${hours} 小時`
  return `${minutes} 分`
}
