/**
 * Stage 5.7 — ConfirmOverlay（通用確認彈窗）
 *
 * 取代瀏覽器原生 confirm()，提供可自訂樣式的確認 / 取消 modal。
 * API：showConfirm(options) → Promise<boolean>
 *   - resolve(true)  = 玩家點「確認」
 *   - resolve(false) = 玩家點「取消」、ESC 鍵、backdrop 點擊
 *
 * 每次呼叫重新建立 DOM，關閉後自動移除，不殘留。
 */

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

export interface ConfirmOptions {
  title: string
  message: string
  /** 確認按鈕標籤，預設「確認」 */
  confirmLabel?: string
  /** 取消按鈕標籤，預設「取消」 */
  cancelLabel?: string
  /**
   * 確認按鈕主色：
   *   'primary'  → 藍 #3a7ad4
   *   'danger'   → 紅 #c44040
   *   'warning'  → 橙 #c47a20
   * 預設 'primary'
   */
  variant?: 'primary' | 'danger' | 'warning'
}

// ---------------------------------------------------------------------------
// 顏色對映
// ---------------------------------------------------------------------------

const VARIANT_COLOR: Record<NonNullable<ConfirmOptions['variant']>, string> = {
  primary: '#3a7ad4',
  danger:  '#c44040',
  warning: '#c47a20',
}

// ---------------------------------------------------------------------------
// 公開 API
// ---------------------------------------------------------------------------

/**
 * 顯示通用確認彈窗。
 * @returns Promise<boolean>  true = 確認，false = 取消 / ESC / backdrop
 */
export function showConfirm(options: ConfirmOptions): Promise<boolean> {
  const {
    title,
    message,
    confirmLabel = '確認',
    cancelLabel  = '取消',
    variant      = 'primary',
  } = options

  return new Promise<boolean>((resolve) => {
    let resolved = false

    // --- 解決並清理 ---
    function finish(result: boolean): void {
      if (resolved) return
      resolved = true
      document.removeEventListener('keydown', onKeyDown)
      overlay.remove()
      resolve(result)
    }

    // --- ESC 鍵 = 取消 ---
    function onKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') {
        e.preventDefault()
        finish(false)
      }
    }
    document.addEventListener('keydown', onKeyDown)

    // -----------------------------------------------------------------------
    // DOM：backdrop
    // -----------------------------------------------------------------------
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

    // backdrop 點擊 = 取消（只響應直接點擊 backdrop，不響應卡片冒泡）
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) finish(false)
    })

    // -----------------------------------------------------------------------
    // DOM：卡片
    // -----------------------------------------------------------------------
    const card = document.createElement('div')
    card.style.cssText = [
      'background:#1a1a1a',
      'border:1px solid #4a4a4a',
      'border-radius:8px',
      'padding:24px',
      'width:320px',
      'max-width:90vw',
      'color:#e8d8a0',
      "font-family:'Segoe UI',sans-serif",
      'display:flex',
      'flex-direction:column',
      'gap:12px',
      'box-shadow:0 8px 32px rgba(0,0,0,0.8)',
    ].join(';')

    // 阻止卡片點擊冒泡到 backdrop
    card.addEventListener('click', (e) => e.stopPropagation())

    // 標題
    const titleEl = document.createElement('div')
    titleEl.style.cssText = 'font-size:16px;font-weight:bold;color:#e8d8a0'
    titleEl.textContent = title

    // 訊息
    const msgEl = document.createElement('div')
    msgEl.style.cssText = 'font-size:14px;color:#b8a878;line-height:1.5'
    msgEl.textContent = message

    // 按鈕列
    const btnRow = document.createElement('div')
    btnRow.style.cssText = [
      'display:flex',
      'justify-content:flex-end',
      'gap:10px',
      'margin-top:8px',
    ].join(';')

    // 取消按鈕
    const cancelBtn = document.createElement('button')
    cancelBtn.style.cssText = buildBtnStyle('#666', 'transparent', '#a89868')
    cancelBtn.textContent = cancelLabel
    cancelBtn.addEventListener('click', () => finish(false))
    addHoverStyle(cancelBtn, '#3a3a3a')

    // 確認按鈕
    const confirmBtn = document.createElement('button')
    const confirmColor = VARIANT_COLOR[variant]
    confirmBtn.style.cssText = buildBtnStyle(confirmColor, confirmColor, '#ffffff')
    confirmBtn.textContent = confirmLabel
    confirmBtn.addEventListener('click', () => finish(true))
    addHoverStyle(confirmBtn, darken(confirmColor))

    btnRow.appendChild(cancelBtn)
    btnRow.appendChild(confirmBtn)

    card.appendChild(titleEl)
    card.appendChild(msgEl)
    card.appendChild(btnRow)
    overlay.appendChild(card)
    document.body.appendChild(overlay)

    // 自動聚焦確認按鈕（方便鍵盤操作）
    confirmBtn.focus()
  })
}

// ---------------------------------------------------------------------------
// 內部樣式輔助
// ---------------------------------------------------------------------------

function buildBtnStyle(borderColor: string, bgColor: string, textColor: string): string {
  return [
    `border:1px solid ${borderColor}`,
    `background:${bgColor}`,
    `color:${textColor}`,
    'border-radius:4px',
    'padding:6px 18px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
    'transition:background 0.15s',
  ].join(';')
}

/**
 * 滑鼠懸停時切換背景色（簡易 hover 效果；inline style 無法用 :hover）。
 */
function addHoverStyle(btn: HTMLButtonElement, hoverBg: string): void {
  const originalBg = btn.style.background
  btn.addEventListener('mouseenter', () => { btn.style.background = hoverBg })
  btn.addEventListener('mouseleave', () => { btn.style.background = originalBg })
}

/**
 * 簡易色彩加深（16 進位 hex，每分量 -30）。
 * 僅用於確認按鈕懸停效果。
 */
function darken(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  const clamp = (v: number) => Math.max(0, Math.min(255, v))
  const toHex  = (v: number) => clamp(v).toString(16).padStart(2, '0')
  return `#${toHex(r - 30)}${toHex(g - 30)}${toHex(b - 30)}`
}
