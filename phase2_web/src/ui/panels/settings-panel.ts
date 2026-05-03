/**
 * Stage 5.7 — SettingsPanel（設定面板）
 *
 * 功能：
 *   - 顯示 3 個存檔 slot（slot0 主存檔 / slot1 / slot2）的有無狀態
 *   - 各 slot 提供「儲存 / 載入 / 刪除」操作
 *   - 重置遊戲（清除所有存檔 + 重啟）需二次確認
 *
 * 使用 save-load.listSaves 取得 slot 狀態，操作後自動 refresh。
 * 重置確認優先使用 confirm-overlay.showConfirm；fallback 為 window.confirm。
 *
 * mountSettingsPanel(parent, ctx) → SettingsPanelHandle
 *   .root     已掛載的根元素
 *   .dispose  解除掛載 + 清理事件
 *   .refresh  重新讀取 listSaves 並更新 UI
 */

import type { GuildState } from '../../types'
import { listSaves } from '../../systems/save-load'

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

export interface SettingsPanelCtx {
  /** 取得目前 GuildState */
  getGuild: () => GuildState
  /** 儲存到指定 slot */
  onSave: (slot: string) => Promise<void>
  /**
   * 載入指定 slot；回傳 true = 成功，false = 失敗（版本不符 / 找不到）。
   * main.ts 負責將載入的 state 套用到 runtime。
   */
  onLoad: (slot: string) => Promise<boolean>
  /** 刪除指定 slot */
  onDelete: (slot: string) => Promise<void>
  /** 清除所有存檔並重啟遊戲（由 main.ts 實作） */
  onReset: () => Promise<void>
}

export interface SettingsPanelHandle {
  root: HTMLElement
  dispose: () => void
  /** 重新讀取 listSaves 並刷新整個 slot 區塊 */
  refresh: () => Promise<void>
}

// ---------------------------------------------------------------------------
// Slot 設定
// ---------------------------------------------------------------------------

const ALL_SLOTS = ['slot0', 'slot1', 'slot2'] as const
type SlotId = typeof ALL_SLOTS[number]

const SLOT_LABELS: Record<SlotId, string> = {
  slot0: '主存檔（自動存檔）',
  slot1: '存檔 2',
  slot2: '存檔 3',
}

// ---------------------------------------------------------------------------
// mountSettingsPanel
// ---------------------------------------------------------------------------

export function mountSettingsPanel(
  parent: HTMLElement,
  ctx: SettingsPanelCtx,
): SettingsPanelHandle {
  // -------------------------------------------------------------------------
  // 根元素（含右側書架裝飾）
  // -------------------------------------------------------------------------
  const root = document.createElement('div')
  root.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'gap:20px',
    'padding:16px',
    "font-family:'Segoe UI',sans-serif",
    'color:#e8d8a0',
    'max-width:520px',
    'position:relative',
  ].join(';')

  // ── 裝飾：A-16 書架（右側絕對定位） ──
  const settingsBookshelf = document.createElement('img')
  settingsBookshelf.src = '/images/scene/A-16_default.png'
  settingsBookshelf.alt = ''
  settingsBookshelf.style.cssText = 'position:absolute;top:8px;right:8px;height:280px;object-fit:contain;opacity:0.6;pointer-events:none;z-index:0'
  settingsBookshelf.onerror = () => { settingsBookshelf.style.display = 'none' }
  root.appendChild(settingsBookshelf)

  // -------------------------------------------------------------------------
  // 訊息列（操作結果提示，共用）
  // -------------------------------------------------------------------------
  const msgEl = document.createElement('div')
  msgEl.style.cssText = [
    'font-size:13px',
    'min-height:18px',
    'color:#a8d898',
    'transition:opacity 0.3s',
  ].join(';')
  msgEl.textContent = ''

  let msgTimer: ReturnType<typeof setTimeout> | null = null

  function showMsg(text: string, isError = false): void {
    msgEl.style.color = isError ? '#d07070' : '#a8d898'
    msgEl.textContent = text
    if (msgTimer !== null) clearTimeout(msgTimer)
    msgTimer = setTimeout(() => { msgEl.textContent = '' }, 4000)
  }

  // -------------------------------------------------------------------------
  // Slot 區塊（可替換的容器，refresh 時整段替換）
  // -------------------------------------------------------------------------
  const slotSection = document.createElement('div')
  slotSection.style.cssText = 'display:flex;flex-direction:column;gap:12px'

  // 段落標題
  const sectionTitle = document.createElement('div')
  sectionTitle.style.cssText = 'display:flex;align-items:center;gap:8px;font-size:15px;font-weight:bold;color:#c8b0a0;border-bottom:1px solid #3a3a3a;padding-bottom:6px'
  const settingsHeroIcon = document.createElement('img')
  settingsHeroIcon.src = '/images/scene/A-05_default.png'
  settingsHeroIcon.alt = ''
  settingsHeroIcon.style.cssText = 'height:32px;object-fit:contain;flex-shrink:0'
  settingsHeroIcon.onerror = () => { settingsHeroIcon.style.display = 'none' }
  const settingsTitleSpan = document.createElement('span')
  settingsTitleSpan.textContent = '存檔管理'
  sectionTitle.appendChild(settingsHeroIcon)
  sectionTitle.appendChild(settingsTitleSpan)

  root.appendChild(sectionTitle)
  root.appendChild(msgEl)
  root.appendChild(slotSection)

  // -------------------------------------------------------------------------
  // 重置區塊（靜態，不隨 refresh 變動）
  // -------------------------------------------------------------------------
  root.appendChild(buildResetSection(ctx, showMsg))

  parent.appendChild(root)

  // -------------------------------------------------------------------------
  // refresh：讀取 listSaves 並重繪 slot 卡片
  // -------------------------------------------------------------------------
  async function refresh(): Promise<void> {
    slotSection.innerHTML = ''

    let existingSlots: string[]
    try {
      existingSlots = await listSaves()
    } catch {
      const errEl = document.createElement('div')
      errEl.style.cssText = 'color:#d07070;font-size:14px'
      errEl.textContent = '無法讀取存檔列表（IndexedDB 錯誤）'
      slotSection.appendChild(errEl)
      return
    }

    const existingSet = new Set(existingSlots)

    for (const slot of ALL_SLOTS) {
      const card = buildSlotCard(slot, existingSet.has(slot), ctx, showMsg, refresh)
      slotSection.appendChild(card)
    }
  }

  // 初始載入
  refresh().catch(console.error)

  // -------------------------------------------------------------------------
  // dispose
  // -------------------------------------------------------------------------
  function dispose(): void {
    if (msgTimer !== null) clearTimeout(msgTimer)
    root.remove()
  }

  return { root, dispose, refresh }
}

// ---------------------------------------------------------------------------
// 單個 slot 卡片
// ---------------------------------------------------------------------------

function buildSlotCard(
  slot: SlotId,
  hasData: boolean,
  ctx: SettingsPanelCtx,
  showMsg: (text: string, isError?: boolean) => void,
  refresh: () => Promise<void>,
): HTMLElement {
  const card = document.createElement('div')
  card.style.cssText = [
    'border:1px solid #3a3a3a',
    'border-radius:6px',
    'padding:12px 14px',
    'background:#141414',
    'display:flex',
    'align-items:center',
    'gap:12px',
    'flex-wrap:wrap',
  ].join(';')

  // Slot 名稱 + 狀態
  const labelArea = document.createElement('div')
  labelArea.style.cssText = 'flex:1;min-width:140px'

  const nameEl = document.createElement('div')
  nameEl.style.cssText = 'font-size:14px;color:#e8d8a0;font-weight:bold'
  nameEl.textContent = SLOT_LABELS[slot]

  const statusEl = document.createElement('div')
  statusEl.style.cssText = `font-size:12px;margin-top:3px;color:${hasData ? '#78c878' : '#888888'}`
  statusEl.textContent = hasData ? '已存檔' : '（空）'

  labelArea.appendChild(nameEl)
  labelArea.appendChild(statusEl)

  // 按鈕群
  const btnGroup = document.createElement('div')
  btnGroup.style.cssText = 'display:flex;gap:6px;flex-shrink:0'

  // 儲存按鈕
  const saveBtn = document.createElement('button')
  saveBtn.style.cssText = slotBtnStyle('#c8a060')
  saveBtn.textContent = '儲存'
  addHoverStyle(saveBtn, '#2a2010')
  saveBtn.addEventListener('click', async () => {
    setLoading(saveBtn, true)
    try {
      await ctx.onSave(slot)
      showMsg(`已儲存至 ${SLOT_LABELS[slot]}`)
      await refresh()
    } catch (err) {
      showMsg(`儲存失敗：${String(err)}`, true)
    } finally {
      setLoading(saveBtn, false)
    }
  })

  // 載入按鈕（slot 無資料時 disabled）
  const loadBtn = document.createElement('button')
  loadBtn.style.cssText = slotBtnStyle(hasData ? '#60a8c8' : '#444444')
  loadBtn.textContent = '載入'
  loadBtn.disabled = !hasData
  if (!hasData) loadBtn.style.opacity = '0.4'
  if (!hasData) loadBtn.style.cursor = 'not-allowed'
  if (hasData) addHoverStyle(loadBtn, '#102030')
  loadBtn.addEventListener('click', async () => {
    if (!hasData) return
    setLoading(loadBtn, true)
    try {
      const success = await ctx.onLoad(slot)
      if (success) {
        showMsg(`已載入 ${SLOT_LABELS[slot]}`)
      } else {
        showMsg(`載入失敗：存檔版本不符或不存在`, true)
      }
    } catch (err) {
      showMsg(`載入失敗：${String(err)}`, true)
    } finally {
      setLoading(loadBtn, false)
    }
  })

  // 刪除按鈕（slot 無資料時 disabled）
  const deleteBtn = document.createElement('button')
  deleteBtn.style.cssText = slotBtnStyle(hasData ? '#c84040' : '#444444')
  deleteBtn.textContent = '刪除'
  deleteBtn.disabled = !hasData
  if (!hasData) deleteBtn.style.opacity = '0.4'
  if (!hasData) deleteBtn.style.cursor = 'not-allowed'
  if (hasData) addHoverStyle(deleteBtn, '#200808')
  deleteBtn.addEventListener('click', async () => {
    if (!hasData) return
    // 刪除前確認（嘗試 confirm-overlay，fallback window.confirm）
    const confirmed = await confirmDelete(SLOT_LABELS[slot])
    if (!confirmed) return
    setLoading(deleteBtn, true)
    try {
      await ctx.onDelete(slot)
      showMsg(`已刪除 ${SLOT_LABELS[slot]}`)
      await refresh()
    } catch (err) {
      showMsg(`刪除失敗：${String(err)}`, true)
    } finally {
      setLoading(deleteBtn, false)
    }
  })

  btnGroup.appendChild(saveBtn)
  btnGroup.appendChild(loadBtn)
  btnGroup.appendChild(deleteBtn)

  card.appendChild(labelArea)
  card.appendChild(btnGroup)
  return card
}

// ---------------------------------------------------------------------------
// 重置區塊
// ---------------------------------------------------------------------------

function buildResetSection(
  ctx: SettingsPanelCtx,
  showMsg: (text: string, isError?: boolean) => void,
): HTMLElement {
  const section = document.createElement('div')
  section.style.cssText = [
    'border-top:1px solid #3a3a3a',
    'padding-top:16px',
    'display:flex',
    'flex-direction:column',
    'gap:8px',
  ].join(';')

  const title = document.createElement('div')
  title.style.cssText = 'font-size:15px;font-weight:bold;color:#c8b0a0;padding-bottom:6px'
  title.textContent = '重置遊戲'

  const desc = document.createElement('div')
  desc.style.cssText = 'font-size:13px;color:#888888'
  desc.textContent = '清除所有存檔並重新開始。此操作無法復原。'

  const resetBtn = document.createElement('button')
  resetBtn.style.cssText = resetBtnStyle()
  resetBtn.textContent = '重置遊戲'
  addHoverStyle(resetBtn, '#3a0808')
  resetBtn.addEventListener('click', async () => {
    const confirmed = await confirmReset()
    if (!confirmed) return
    setLoading(resetBtn, true)
    try {
      await ctx.onReset()
      // onReset 通常觸發頁面重載，不會執行到此；保留 fallback 提示
      showMsg('已重置遊戲')
    } catch (err) {
      showMsg(`重置失敗：${String(err)}`, true)
      setLoading(resetBtn, false)
    }
  })

  section.appendChild(title)
  section.appendChild(desc)
  section.appendChild(resetBtn)
  return section
}

// ---------------------------------------------------------------------------
// 確認對話框（嘗試 confirm-overlay，fallback window.confirm）
// ---------------------------------------------------------------------------

async function confirmDelete(slotLabel: string): Promise<boolean> {
  try {
    const { showConfirm } = await import('../overlays/confirm-overlay')
    return showConfirm({
      title: '確認刪除',
      message: `確定要刪除「${slotLabel}」的存檔嗎？此操作無法復原。`,
      confirmLabel: '刪除',
      variant: 'danger',
    })
  } catch {
    return window.confirm(`確定要刪除「${slotLabel}」的存檔嗎？`)
  }
}

async function confirmReset(): Promise<boolean> {
  try {
    const { showConfirm } = await import('../overlays/confirm-overlay')
    return showConfirm({
      title: '確認重置',
      message: '這將清除所有存檔並重新開始遊戲。確定要重置嗎？',
      confirmLabel: '重置',
      variant: 'danger',
    })
  } catch {
    return window.confirm('這將清除所有存檔並重新開始遊戲。確定要重置嗎？')
  }
}

// ---------------------------------------------------------------------------
// 樣式輔助
// ---------------------------------------------------------------------------

function slotBtnStyle(borderColor: string): string {
  return [
    'background:transparent',
    `color:${borderColor}`,
    `border:1px solid ${borderColor}`,
    'border-radius:4px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:13px',
    'cursor:pointer',
    'padding:4px 12px',
    'transition:background 0.15s',
  ].join(';')
}

function resetBtnStyle(): string {
  return [
    'background:transparent',
    'color:#c84040',
    'border:1px solid #c84040',
    'border-radius:4px',
    "font-family:'Segoe UI',sans-serif",
    'font-size:14px',
    'cursor:pointer',
    'padding:6px 20px',
    'align-self:flex-start',
    'transition:background 0.15s',
  ].join(';')
}

function addHoverStyle(btn: HTMLButtonElement, hoverBg: string): void {
  const originalBg = btn.style.background
  btn.addEventListener('mouseenter', () => { btn.style.background = hoverBg })
  btn.addEventListener('mouseleave', () => { btn.style.background = originalBg })
}

/** 操作進行中：禁用按鈕 + 顯示省略號文字 */
function setLoading(btn: HTMLButtonElement, loading: boolean): void {
  btn.disabled = loading
  if (loading) {
    btn.dataset['originalText'] = btn.textContent ?? ''
    btn.textContent = '…'
    btn.style.opacity = '0.6'
  } else {
    btn.textContent = btn.dataset['originalText'] ?? btn.textContent
    btn.style.opacity = ''
    delete btn.dataset['originalText']
  }
}
