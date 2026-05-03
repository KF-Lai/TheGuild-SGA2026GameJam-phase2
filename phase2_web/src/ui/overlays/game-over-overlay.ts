// game-over-overlay.ts — Game Over 全屏覆蓋層
// 顯示公會結束原因、最終統計數據，並強制玩家選擇「重新開始」或「主選單」。
// 設計：backdrop 點擊與 ESC 均不關閉（強制選擇）。

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

export interface GameOverStats {
  daysPlayed: number
  guildLevel: number
  reputation: number
  totalAdventurersHired: number
  totalAdventurersDead: number
  totalMissionsCompleted: number
  finalGold: number
}

export interface GameOverProps {
  reason: 'BANKRUPTCY' | 'PLAYER_QUIT'
  /** 玩家最終統計（main.ts 計算後傳入） */
  stats: GameOverStats
  /** 玩家點「重新開始」 */
  onRestart: () => void
  /** 玩家點「主選單」 */
  onMainMenu: () => void
}

// ---------------------------------------------------------------------------
// 模組內部狀態（singleton overlay）
// ---------------------------------------------------------------------------

let _overlayEl: HTMLElement | null = null
let _escHandler: ((e: KeyboardEvent) => void) | null = null

// ---------------------------------------------------------------------------
// 常數
// ---------------------------------------------------------------------------

const REASON_LABELS: Record<GameOverProps['reason'], string> = {
  BANKRUPTCY:  '公會傾覆',
  PLAYER_QUIT: '結束遊戲',
}

const REASON_SUBTITLES: Record<GameOverProps['reason'], string> = {
  BANKRUPTCY:  '金庫告罄，信譽盡失。公會就此畫上句點。',
  PLAYER_QUIT: '冒險者們目送你離開，故事就此落幕。',
}

// ---------------------------------------------------------------------------
// 主函式
// ---------------------------------------------------------------------------

/**
 * 顯示 Game Over 覆蓋層。
 * 重複呼叫時會自動移除前一個，保證不殘留舊 DOM。
 */
export function showGameOverOverlay(props: GameOverProps): void {
  // 清除前次殘留
  _cleanup()

  const overlay = document.createElement('div')
  overlay.id = 'game-over-overlay'
  overlay.style.cssText = [
    'position:fixed',
    'inset:0',
    'z-index:9500',
    'display:flex',
    'align-items:center',
    'justify-content:center',
    'background:linear-gradient(135deg, #1a0a0a 0%, #000 100%)',
    'opacity:0',
    'transition:opacity 1.5s ease',
    'font-family:"Courier New",monospace',
  ].join(';')

  // 阻止 backdrop 點擊穿透到後面
  overlay.addEventListener('click', (e) => e.stopPropagation())

  overlay.appendChild(_buildContent(props))
  document.body.appendChild(overlay)
  _overlayEl = overlay

  // 阻止 ESC 關閉（不綁 ESC → 讓玩家只能按按鈕）
  _escHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      e.preventDefault()
      e.stopPropagation()
    }
  }
  window.addEventListener('keydown', _escHandler, true)

  // 強制 reflow，讓 transition 生效
  void overlay.offsetHeight
  overlay.style.opacity = '1'
}

// ---------------------------------------------------------------------------
// 內部：建立 content card
// ---------------------------------------------------------------------------

function _buildContent(props: GameOverProps): HTMLElement {
  const card = document.createElement('div')
  card.style.cssText = [
    'background:#0d0505',
    'border:1px solid #5a1a1a',
    'border-radius:6px',
    'padding:40px 48px',
    'max-width:520px',
    'width:90vw',
    'color:#c8b98a',
    'box-shadow:0 0 60px rgba(208,64,64,0.2)',
    'display:flex',
    'flex-direction:column',
    'gap:24px',
    'box-sizing:border-box',
  ].join(';')

  card.appendChild(_buildHeader(props))
  card.appendChild(_buildStats(props.stats))
  card.appendChild(_buildFooter(props))

  return card
}

// ---------------------------------------------------------------------------
// 內部：標題區
// ---------------------------------------------------------------------------

function _buildHeader(props: GameOverProps): HTMLElement {
  const header = document.createElement('div')
  header.style.cssText = 'text-align:center;'

  const title = document.createElement('div')
  title.textContent = REASON_LABELS[props.reason]
  title.style.cssText = [
    'font-size:clamp(28px,4vw,42px)',
    'color:#d04040',
    'font-weight:bold',
    'letter-spacing:0.1em',
    'margin-bottom:8px',
  ].join(';')

  const subtitle = document.createElement('div')
  subtitle.textContent = REASON_SUBTITLES[props.reason]
  subtitle.style.cssText = [
    'font-size:clamp(11px,1.4vw,14px)',
    'color:#7a5a5a',
    'line-height:1.6',
  ].join(';')

  header.appendChild(title)
  header.appendChild(subtitle)
  return header
}

// ---------------------------------------------------------------------------
// 內部：統計列表
// ---------------------------------------------------------------------------

function _buildStats(stats: GameOverStats): HTMLElement {
  const container = document.createElement('div')
  container.style.cssText = [
    'border-top:1px solid #3a1a1a',
    'border-bottom:1px solid #3a1a1a',
    'padding:20px 0',
    'display:flex',
    'flex-direction:column',
    'gap:10px',
  ].join(';')

  const rows: Array<{ label: string; value: string | number; highlight?: boolean }> = [
    { label: '經營天數',     value: stats.daysPlayed,              highlight: true },
    { label: '公會等級',     value: `Lv. ${stats.guildLevel}` },
    { label: '聲望',         value: stats.reputation },
    { label: '招募冒險者數', value: stats.totalAdventurersHired },
    { label: '陣亡冒險者數', value: stats.totalAdventurersDead,    highlight: stats.totalAdventurersDead > 0 },
    { label: '完成任務數',   value: stats.totalMissionsCompleted,  highlight: true },
    { label: '最終金幣',     value: `${stats.finalGold} G` },
  ]

  for (const row of rows) {
    container.appendChild(_buildStatRow(row.label, row.value, row.highlight ?? false))
  }

  return container
}

function _buildStatRow(
  label: string,
  value: string | number,
  highlight: boolean,
): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = [
    'display:flex',
    'justify-content:space-between',
    'align-items:baseline',
    'font-size:clamp(12px,1.4vw,15px)',
  ].join(';')

  const labelEl = document.createElement('span')
  labelEl.textContent = label
  labelEl.style.color = '#7a6a5a'

  const valueEl = document.createElement('span')
  // 空值顯示 em dash
  const display = (value === undefined || value === null || value === '') ? '—' : String(value)
  valueEl.textContent = display
  valueEl.style.cssText = [
    `color:${highlight ? '#e8d4a0' : '#c8b98a'}`,
    `font-size:${highlight ? 'clamp(14px,1.6vw,18px)' : 'inherit'}`,
    'font-weight:bold',
  ].join(';')

  row.appendChild(labelEl)
  row.appendChild(valueEl)
  return row
}

// ---------------------------------------------------------------------------
// 內部：按鈕區
// ---------------------------------------------------------------------------

function _buildFooter(props: GameOverProps): HTMLElement {
  const footer = document.createElement('div')
  footer.style.cssText = [
    'display:flex',
    'gap:12px',
    'justify-content:center',
    'flex-wrap:wrap',
  ].join(';')

  // 重新開始按鈕（橘色）
  const restartBtn = _buildButton('重新開始', '#f0c060', '#1a0a00', () => {
    _cleanup()
    props.onRestart()
  })

  // 主選單按鈕（灰色）
  const menuBtn = _buildButton('主選單', '#888', '#111', () => {
    _cleanup()
    props.onMainMenu()
  })

  footer.appendChild(restartBtn)
  footer.appendChild(menuBtn)
  return footer
}

function _buildButton(
  label: string,
  color: string,
  bgColor: string,
  onClick: () => void,
): HTMLElement {
  const btn = document.createElement('button')
  btn.textContent = label
  btn.style.cssText = [
    `background:${bgColor}`,
    `color:${color}`,
    `border:1px solid ${color}`,
    'border-radius:4px',
    'padding:10px 28px',
    'font-family:"Courier New",monospace',
    'font-size:clamp(12px,1.4vw,15px)',
    'cursor:pointer',
    'transition:background 0.15s, color 0.15s',
    'min-width:120px',
  ].join(';')

  btn.addEventListener('mouseenter', () => {
    btn.style.background = color
    btn.style.color = bgColor || '#000'
  })
  btn.addEventListener('mouseleave', () => {
    btn.style.background = bgColor
    btn.style.color = color
  })
  btn.addEventListener('click', onClick)
  return btn
}

// ---------------------------------------------------------------------------
// 內部：清除 overlay
// ---------------------------------------------------------------------------

function _cleanup(): void {
  if (_escHandler) {
    window.removeEventListener('keydown', _escHandler, true)
    _escHandler = null
  }
  if (_overlayEl) {
    _overlayEl.remove()
    _overlayEl = null
  }
}
