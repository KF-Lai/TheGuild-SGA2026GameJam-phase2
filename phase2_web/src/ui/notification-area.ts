// notification-area.ts — 通知區（Toast 訊息流）
// 職責：在右上角以卡片堆疊形式顯示即時通知。
// 設計：自身不訂閱 EventBus；由 main.ts 訂閱事件後呼叫 push()。

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

export interface NotificationItem {
  id: string
  type: 'info' | 'success' | 'warning' | 'danger'
  message: string
  timestamp: number
  /** 自動消失毫秒；0 = 不自動消失（需手動關閉） */
  autoDismissMs?: number
}

export interface NotificationAreaHandle {
  root: HTMLElement
  /** 推送一則通知，回傳新通知 id */
  push: (item: Omit<NotificationItem, 'id' | 'timestamp'>) => string
  /** 主動移除指定通知 */
  dismiss: (id: string) => void
  /** 移除所有通知 */
  dismissAll: () => void
  /** 卸載並移除根元素 */
  dispose: () => void
}

// ---------------------------------------------------------------------------
// 色彩對映
// ---------------------------------------------------------------------------

const TYPE_COLORS: Record<NotificationItem['type'], string> = {
  info:    '#4a90d9',
  success: '#78c878',
  warning: '#d09040',
  danger:  '#d04040',
}

const TYPE_ICONS: Record<NotificationItem['type'], string> = {
  info:    'ℹ',
  success: '✓',
  warning: '⚠',
  danger:  '✕',
}

// ---------------------------------------------------------------------------
// 內部狀態
// ---------------------------------------------------------------------------

interface ToastEntry {
  item: NotificationItem
  el: HTMLElement
  timerId: ReturnType<typeof setTimeout> | null
}

// ---------------------------------------------------------------------------
// 主函式
// ---------------------------------------------------------------------------

/**
 * 掛載通知區到 parent，回傳操作 handle。
 *
 * @param parent              - 掛載對象（通常是 document.body 或 #app）
 * @param options.maxItems    - 最多同時顯示幾則（預設 5）
 * @param options.defaultAutoDismissMs - 未指定 autoDismissMs 時的預設值（預設 4000）
 */
export function mountNotificationArea(
  parent: HTMLElement,
  options?: { maxItems?: number; defaultAutoDismissMs?: number },
): NotificationAreaHandle {
  const maxItems = options?.maxItems ?? 5
  const defaultAutoDismissMs = options?.defaultAutoDismissMs ?? 4000

  // 建立根容器（fixed 右上角）
  const root = document.createElement('div')
  root.id = 'notification-area'
  root.style.cssText = [
    'position:fixed',
    'top:16px',
    'right:16px',
    'z-index:8000',
    'display:flex',
    'flex-direction:column',
    'gap:8px',
    'pointer-events:none',
    'width:320px',
  ].join(';')
  parent.appendChild(root)

  // 以 Map 追蹤所有活躍的 toast
  const toasts = new Map<string, ToastEntry>()

  // -------------------------------------------------------------------------
  // 內部：移除單筆 toast（含淡出動畫）
  // -------------------------------------------------------------------------
  function _removeEntry(id: string): void {
    const entry = toasts.get(id)
    if (!entry) return

    // 清除自動消失計時器
    if (entry.timerId !== null) {
      clearTimeout(entry.timerId)
    }

    // 淡出動畫：opacity 1 → 0，再移除 DOM
    entry.el.style.opacity = '0'
    entry.el.style.transform = 'translateX(20px)'
    setTimeout(() => {
      entry.el.remove()
    }, 250)

    toasts.delete(id)
  }

  // -------------------------------------------------------------------------
  // 內部：建立 toast DOM 元素
  // -------------------------------------------------------------------------
  function _createToastEl(item: NotificationItem): HTMLElement {
    const color = TYPE_COLORS[item.type]
    const icon = TYPE_ICONS[item.type]

    const card = document.createElement('div')
    card.dataset.notifId = item.id
    card.style.cssText = [
      'display:flex',
      'align-items:flex-start',
      'gap:8px',
      'padding:10px 12px',
      `background:#1e1e1e`,
      `border-left:3px solid ${color}`,
      'border-radius:4px',
      'box-shadow:0 2px 8px rgba(0,0,0,0.6)',
      'pointer-events:all',
      'cursor:default',
      'opacity:0',
      'transform:translateX(20px)',
      'transition:opacity 0.2s ease, transform 0.2s ease',
      'font-family:"Courier New",monospace',
      'font-size:13px',
      'color:#c8b98a',
      'box-sizing:border-box',
    ].join(';')

    // 圖示
    const iconEl = document.createElement('span')
    iconEl.textContent = icon
    iconEl.style.cssText = `color:${color};flex-shrink:0;font-size:14px;line-height:1.4;`

    // 訊息文字
    const msgEl = document.createElement('span')
    msgEl.textContent = item.message
    msgEl.style.cssText = 'flex:1;line-height:1.5;word-break:break-word;'

    // 關閉按鈕
    const closeBtn = document.createElement('button')
    closeBtn.textContent = '×'
    closeBtn.style.cssText = [
      'background:none',
      'border:none',
      'color:#666',
      'cursor:pointer',
      'font-size:16px',
      'line-height:1',
      'padding:0',
      'flex-shrink:0',
      'transition:color 0.15s',
    ].join(';')
    closeBtn.addEventListener('mouseenter', () => { closeBtn.style.color = '#c8b98a' })
    closeBtn.addEventListener('mouseleave', () => { closeBtn.style.color = '#666' })
    closeBtn.addEventListener('click', () => _removeEntry(item.id))

    card.appendChild(iconEl)
    card.appendChild(msgEl)
    card.appendChild(closeBtn)

    return card
  }

  // -------------------------------------------------------------------------
  // push：推送新通知
  // -------------------------------------------------------------------------
  function push(item: Omit<NotificationItem, 'id' | 'timestamp'>): string {
    const id = `notif-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
    const timestamp = Date.now()
    const autoDismissMs = item.autoDismissMs ?? defaultAutoDismissMs
    const fullItem: NotificationItem = { ...item, id, timestamp, autoDismissMs }

    // 超出上限：移除最舊一筆
    if (toasts.size >= maxItems) {
      const oldestId = toasts.keys().next().value
      if (oldestId !== undefined) {
        _removeEntry(oldestId)
      }
    }

    const el = _createToastEl(fullItem)
    root.appendChild(el)

    // 強制 reflow，讓 transition 生效
    void el.offsetHeight
    el.style.opacity = '1'
    el.style.transform = 'translateX(0)'

    // 自動消失計時器
    let timerId: ReturnType<typeof setTimeout> | null = null
    if (autoDismissMs > 0) {
      timerId = setTimeout(() => _removeEntry(id), autoDismissMs)
    }

    toasts.set(id, { item: fullItem, el, timerId })
    return id
  }

  // -------------------------------------------------------------------------
  // dismiss：主動移除指定通知
  // -------------------------------------------------------------------------
  function dismiss(id: string): void {
    _removeEntry(id)
  }

  // -------------------------------------------------------------------------
  // dismissAll：清除所有通知
  // -------------------------------------------------------------------------
  function dismissAll(): void {
    for (const id of Array.from(toasts.keys())) {
      _removeEntry(id)
    }
  }

  // -------------------------------------------------------------------------
  // dispose：卸載整個通知區
  // -------------------------------------------------------------------------
  function dispose(): void {
    dismissAll()
    root.remove()
  }

  return { root, push, dismiss, dismissAll, dispose }
}
