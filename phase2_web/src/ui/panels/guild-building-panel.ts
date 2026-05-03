/**
 * Stage 5.5 — GuildBuildingPanel（公會建設面板）
 *
 * 純新增 panel；phase 1 不存在。
 * 顯示 6 棟建築的當前等級、效果值、升級費用，
 * 並提供雙軌閘門（金幣 + 聲望）狀態與升級按鈕。
 *
 * 設計來源：FT-07 §3.5、phase2-web-spec.md §10
 * vanilla DOM；無框架依賴。
 */

import type { GuildState, GuildLevel } from '../../types'
import type { BuildingState, UpgradeResult } from '../../systems/building'
import { eventBus } from '../../core/events'

// ---------------------------------------------------------------------------
// 公開介面
// ---------------------------------------------------------------------------

export interface NextLevelInfo {
  nextLevel: number
  upgradeCost: number
  guildLevelReq: number
  currentEffectValue: number
  nextEffectValue: number
}

export interface GuildBuildingPanelCtx {
  getGuild: () => GuildState
  getBuildings: () => BuildingState[]
  getGuildLevel: () => GuildLevel
  /** main.ts 注入：呼叫 building.canUpgrade(states, buildingID) */
  canUpgrade: (buildingID: number) => UpgradeResult
  /** main.ts 注入：呼叫 building.tryUpgradeBuilding(states, buildingID) */
  onUpgrade: (buildingID: number) => UpgradeResult
  /** 取下一級的升級資訊，若已滿級回 null */
  getNextLevelInfo: (buildingID: number) => NextLevelInfo | null
  /** 取當前 effectValue（用於顯示） */
  getCurrentEffectValue: (buildingID: number) => number
}

export interface GuildBuildingPanelHandle {
  root: HTMLElement
  dispose: () => void
  refresh: () => void
}

// ---------------------------------------------------------------------------
// 建築 meta（hardcode 6 棟名稱與 maxLevel；effectValue 由 ctx 注入）
// ---------------------------------------------------------------------------

interface BuildingDisplayMeta {
  buildingID: number
  name: string
  maxLevel: number
}

/** 6 棟建築固定 meta（GDD §3.2）*/
const BUILDING_DISPLAY: BuildingDisplayMeta[] = [
  { buildingID: 1, name: '委託板',       maxLevel: 5 },
  { buildingID: 2, name: '招募廣告欄',   maxLevel: 3 },
  { buildingID: 3, name: '公會大廳',     maxLevel: 5 },
  { buildingID: 4, name: '公會櫃臺',     maxLevel: 5 },
  { buildingID: 5, name: '預備金保險櫃', maxLevel: 5 },
  { buildingID: 6, name: '職員休息室',   maxLevel: 5 },
]

// ---------------------------------------------------------------------------
// 顏色常數
// ---------------------------------------------------------------------------

const COLOR_GOLD    = '#f0c060'  // 主色調（橘金）
const COLOR_BG_CARD = '#2a2a2a' // card 背景
const COLOR_BORDER  = '#4a4a4a' // card 邊框（一般）
const COLOR_BORDER_LOUNGE = '#7a6a4a' // 職員休息室 Lv0 邊框（可建造）
const COLOR_OK      = '#4caf50' // 閘門通過（綠）
const COLOR_FAIL    = '#f44336' // 閘門不通（紅）
const COLOR_TEXT    = '#c8b98a' // 一般文字
const COLOR_NEXT    = '#6dbf6d' // 下個等級預覽文字（綠）
const COLOR_BTN_EN  = COLOR_GOLD  // 升級按鈕 enabled
const COLOR_BTN_DIS = '#555555'   // 升級按鈕 disabled
const COLOR_FG_DIM  = 'rgba(200,185,138,0.6)' // 次要文字

// ---------------------------------------------------------------------------
// 掛載入口
// ---------------------------------------------------------------------------

/**
 * 掛載公會建設面板。
 * @param parent 父元素（mount target）
 * @param ctx    資料注入 context
 * @returns      root + dispose + refresh
 */
export function mountGuildBuildingPanel(
  parent: HTMLElement,
  ctx: GuildBuildingPanelCtx,
): GuildBuildingPanelHandle {
  // ── 建立根元素 ──────────────────────────────────────────────────────────────
  const root = document.createElement('div')
  root.className = 'guild-building-panel'
  root.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'gap:0',
    'padding:12px',
    'overflow-y:auto',
    'height:100%',
    'box-sizing:border-box',
    `font-family:'Courier New',monospace`,
    'font-size:13px',
    `color:${COLOR_TEXT}`,
    'background:#0d0d0d',
  ].join(';')

  // ── 標頭 ────────────────────────────────────────────────────────────────────
  const header = document.createElement('div')
  header.style.cssText = [
    'display:flex',
    'align-items:center',
    'gap:8px',
    'font-size:15px',
    'font-weight:bold',
    `color:${COLOR_GOLD}`,
    'margin-bottom:12px',
    'padding-bottom:8px',
    `border-bottom:1px solid ${COLOR_BORDER}`,
    'letter-spacing:1px',
  ].join(';')
  const buildingHeroIcon = document.createElement('img')
  buildingHeroIcon.src = '/images/scene/A-04_default.png'
  buildingHeroIcon.alt = ''
  buildingHeroIcon.style.cssText = 'height:40px;object-fit:contain;flex-shrink:0'
  buildingHeroIcon.onerror = () => { buildingHeroIcon.style.display = 'none' }
  const buildingTitleSpan = document.createElement('span')
  buildingTitleSpan.textContent = '公會建設'
  header.appendChild(buildingHeroIcon)
  header.appendChild(buildingTitleSpan)
  root.appendChild(header)

  // ── grid 容器 ───────────────────────────────────────────────────────────────
  const grid = document.createElement('div')
  grid.style.cssText = [
    'display:grid',
    'grid-template-columns:repeat(auto-fill,minmax(280px,1fr))',
    'gap:10px',
  ].join(';')
  root.appendChild(grid)

  parent.appendChild(root)

  // ── 首次渲染 ────────────────────────────────────────────────────────────────
  _renderGrid(grid, ctx)

  // ── EventBus 訂閱 ───────────────────────────────────────────────────────────
  const _onBuildingUpgraded = () => _renderGrid(grid, ctx)
  const _onGoldChanged      = () => _refreshAllButtons(grid, ctx)
  const _onGuildLevelUp     = () => _refreshAllButtons(grid, ctx)

  eventBus.on('building:upgraded', _onBuildingUpgraded)
  eventBus.on('gold:changed',      _onGoldChanged)
  eventBus.on('guild:level_up',    _onGuildLevelUp)

  // ── 清理函式 ────────────────────────────────────────────────────────────────
  function dispose(): void {
    eventBus.off('building:upgraded', _onBuildingUpgraded)
    eventBus.off('gold:changed',      _onGoldChanged)
    eventBus.off('guild:level_up',    _onGuildLevelUp)
    root.remove()
  }

  function refresh(): void {
    _renderGrid(grid, ctx)
  }

  return { root, dispose, refresh }
}

// ---------------------------------------------------------------------------
// Grid 渲染（完整重繪）
// ---------------------------------------------------------------------------

/**
 * 清空 grid 並重建 6 張建築 card。
 * 事件 'building:upgraded' 觸發完整重繪（等級 / 效果值已變更）。
 */
function _renderGrid(grid: HTMLElement, ctx: GuildBuildingPanelCtx): void {
  grid.innerHTML = ''

  const buildings = ctx.getBuildings()

  for (const meta of BUILDING_DISPLAY) {
    const state = buildings.find(s => s.buildingID === meta.buildingID) ?? null
    if (!state) {
      console.error(`[GuildBuildingPanel] 找不到 buildingID=${meta.buildingID} 的 BuildingState`)
      grid.appendChild(_createErrorCard(meta))
      continue
    }
    grid.appendChild(_createBuildingCard(meta, state, ctx))
  }
}

// ---------------------------------------------------------------------------
// 建築 card 建立
// ---------------------------------------------------------------------------

/**
 * 建立單棟建築 card DOM。
 * card 包含：名稱 + 等級、當前效果顯示、下個等級預覽、費用、雙軌閘門、升級按鈕。
 */
function _createBuildingCard(
  meta: BuildingDisplayMeta,
  state: BuildingState,
  ctx: GuildBuildingPanelCtx,
): HTMLElement {
  const isLounge   = meta.buildingID === 6
  const isUnbuilt  = isLounge && state.currentLevel === 0
  const isMaxLevel = state.currentLevel >= meta.maxLevel

  // card 容器
  const card = document.createElement('div')
  card.dataset['buildingId'] = String(meta.buildingID)
  card.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'gap:8px',
    'padding:12px',
    `background:${COLOR_BG_CARD}`,
    `border:1px solid ${isUnbuilt ? COLOR_BORDER_LOUNGE : COLOR_BORDER}`,
    'border-radius:4px',
    'box-sizing:border-box',
  ].join(';')

  // ── 名稱 + 等級 ──────────────────────────────────────────────────────────
  card.appendChild(_buildTitleRow(meta, state))

  // ── 當前效果顯示 ──────────────────────────────────────────────────────────
  card.appendChild(_buildEffectRow(meta, state, ctx))

  // ── 下個等級預覽（已滿級 / 特殊顯示）────────────────────────────────────
  card.appendChild(_buildNextLevelRow(meta, state, ctx))

  // ── 升級費用 ──────────────────────────────────────────────────────────────
  const nextInfo = isMaxLevel ? null : ctx.getNextLevelInfo(meta.buildingID)
  card.appendChild(_buildCostRow(nextInfo, isMaxLevel))

  // ── 雙軌閘門狀態 ──────────────────────────────────────────────────────────
  card.appendChild(_buildGateRow(meta, state, nextInfo, ctx, isMaxLevel))

  // ── 升級按鈕 ──────────────────────────────────────────────────────────────
  card.appendChild(_buildUpgradeButton(meta, state, nextInfo, ctx, isMaxLevel))

  return card
}

// ---------------------------------------------------------------------------
// card 各子元件
// ---------------------------------------------------------------------------

/**
 * 名稱 + 等級標籤列
 * 格式：「委託板　Lv2 / 5」
 */
function _buildTitleRow(meta: BuildingDisplayMeta, state: BuildingState): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = 'display:flex;align-items:center;justify-content:space-between'

  const nameEl = document.createElement('span')
  nameEl.style.cssText = `font-weight:bold;font-size:14px;color:${COLOR_GOLD}`
  nameEl.textContent = meta.name

  const levelBadge = document.createElement('span')
  const isMax = state.currentLevel >= meta.maxLevel
  levelBadge.style.cssText = [
    'font-size:11px',
    `color:${isMax ? COLOR_GOLD : COLOR_TEXT}`,
    `border:1px solid ${isMax ? COLOR_GOLD : COLOR_BORDER}`,
    'padding:1px 5px',
    'border-radius:2px',
  ].join(';')
  levelBadge.textContent = `Lv${state.currentLevel} / ${meta.maxLevel}`

  row.appendChild(nameEl)
  row.appendChild(levelBadge)
  return row
}

/**
 * 當前效果值顯示行
 * 職員休息室 Lv0 → 顯示「未建造」
 */
function _buildEffectRow(
  meta: BuildingDisplayMeta,
  state: BuildingState,
  ctx: GuildBuildingPanelCtx,
): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = `font-size:12px;color:${COLOR_TEXT}`

  const isLounge  = meta.buildingID === 6
  const isUnbuilt = isLounge && state.currentLevel === 0

  if (isUnbuilt) {
    row.textContent = '現況：未建造'
    row.style.color = COLOR_FG_DIM
  } else {
    const effectVal = ctx.getCurrentEffectValue(meta.buildingID)
    row.textContent = `現況：${_formatEffect(meta.buildingID, effectVal, state.currentLevel)}`
  }

  return row
}

/**
 * 下個等級效果預覽行
 * - 已滿級 → 「已達上限」（灰）
 * - 職員休息室 Lv0 → 「建造後解鎖職員系統」（綠）
 * - 一般 → 「→ N 單位」（綠）
 */
function _buildNextLevelRow(
  meta: BuildingDisplayMeta,
  state: BuildingState,
  ctx: GuildBuildingPanelCtx,
): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = 'font-size:12px'

  const isMaxLevel = state.currentLevel >= meta.maxLevel
  const isLounge   = meta.buildingID === 6
  const isUnbuilt  = isLounge && state.currentLevel === 0

  if (isMaxLevel) {
    row.textContent = '升級預覽：已達上限'
    row.style.color = COLOR_FG_DIM
    return row
  }

  if (isUnbuilt) {
    // 職員休息室 Lv0 特殊文字
    row.textContent = '建造後解鎖職員系統'
    row.style.color = COLOR_NEXT
    return row
  }

  const nextInfo = ctx.getNextLevelInfo(meta.buildingID)
  if (!nextInfo) {
    // getNextLevelInfo 理應不為 null（isMaxLevel 已排除），防禦性處理
    row.textContent = '升級預覽：資料異常'
    row.style.color = COLOR_FAIL
    return row
  }

  row.textContent = `→ ${_formatEffect(meta.buildingID, nextInfo.nextEffectValue, nextInfo.nextLevel)}`
  row.style.color = COLOR_NEXT
  return row
}

/**
 * 升級費用顯示行
 * - 已滿級 → 不顯示費用（空行佔位）
 * - 建造費用 0 → 「免費」
 */
function _buildCostRow(nextInfo: NextLevelInfo | null, isMaxLevel: boolean): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = `font-size:12px;color:${COLOR_FG_DIM}`

  if (isMaxLevel || !nextInfo) {
    // 已滿級不顯示費用
    row.textContent = ''
    return row
  }

  row.textContent = nextInfo.upgradeCost === 0
    ? '升級費用：免費'
    : `升級費用：${nextInfo.upgradeCost}g`

  return row
}

/**
 * 雙軌閘門狀態列
 * 顯示：金幣閘 ✓/✗　聲望閘 ✓/✗
 * 已滿級時隱藏閘門顯示。
 */
function _buildGateRow(
  _meta: BuildingDisplayMeta,
  _state: BuildingState,
  nextInfo: NextLevelInfo | null,
  ctx: GuildBuildingPanelCtx,
  isMaxLevel: boolean,
): HTMLElement {
  const row = document.createElement('div')
  row.style.cssText = 'display:flex;gap:12px;font-size:11px'

  if (isMaxLevel || !nextInfo) {
    // 已滿級不顯示閘門
    return row
  }

  const guild      = ctx.getGuild()
  const gold       = guild.resources.gold
  const guildLevel = ctx.getGuildLevel()

  const goldOk  = gold >= nextInfo.upgradeCost || nextInfo.upgradeCost === 0
  const reqLevel = nextInfo.guildLevelReq
  const levelOk  = (guildLevel as number) >= reqLevel

  // 金幣閘
  const goldGate = document.createElement('span')
  goldGate.style.color = goldOk ? COLOR_OK : COLOR_FAIL
  goldGate.textContent = goldOk
    ? `✓ 金幣 (${gold}g)`
    : `✗ 金幣不足 (${gold}/${nextInfo.upgradeCost}g)`

  // 聲望閘（reqLevel=0 時恆通過）
  const levelGate = document.createElement('span')
  levelGate.style.color = levelOk ? COLOR_OK : COLOR_FAIL
  levelGate.textContent = reqLevel === 0
    ? '✓ 無聲望要求'
    : levelOk
      ? `✓ 公會 Lv${guildLevel}`
      : `✗ 需公會 Lv${reqLevel}`

  row.appendChild(goldGate)
  row.appendChild(levelGate)
  return row
}

/**
 * 建立升級按鈕，並綁定點擊事件。
 * 按鈕外觀依 canUpgrade 結果設定。
 */
function _buildUpgradeButton(
  meta: BuildingDisplayMeta,
  _state: BuildingState,
  nextInfo: NextLevelInfo | null,
  ctx: GuildBuildingPanelCtx,
  isMaxLevel: boolean,
): HTMLElement {
  const btn = document.createElement('button')
  btn.dataset['upgradeBtn'] = String(meta.buildingID)
  btn.style.cssText = [
    'width:100%',
    'padding:6px 0',
    'font-size:13px',
    'font-weight:bold',
    'border:none',
    'border-radius:3px',
    'cursor:pointer',
    `font-family:'Courier New',monospace`,
    'margin-top:2px',
    'transition:opacity 0.15s ease',
  ].join(';')

  // 設定初始按鈕狀態
  _applyButtonState(btn, meta.buildingID, nextInfo, ctx, isMaxLevel)

  // 點擊事件
  btn.addEventListener('click', () => {
    const result = ctx.onUpgrade(meta.buildingID)
    if (result !== 'SUCCESS') {
      // jam 範疇：alert 即可（P-03 通知系統未實作）
      const msg = _upgradeResultToMessage(result, nextInfo)
      alert(`升級失敗：${msg}`)
    }
    // SUCCESS 時 building:upgraded 事件會自動觸發 _renderGrid
  })

  return btn
}

// ---------------------------------------------------------------------------
// 按鈕狀態刷新（用於 gold:changed / guild:level_up 事件）
// ---------------------------------------------------------------------------

/**
 * 在不重建整個 card 的情況下，只刷新 grid 內所有升級按鈕的狀態。
 * 用於 gold:changed / guild:level_up 事件（效果值未變，只有閘門狀態可能改變）。
 */
function _refreshAllButtons(grid: HTMLElement, ctx: GuildBuildingPanelCtx): void {
  const buildings = ctx.getBuildings()

  for (const meta of BUILDING_DISPLAY) {
    const state = buildings.find(s => s.buildingID === meta.buildingID) ?? null
    if (!state) continue

    const isMaxLevel = state.currentLevel >= meta.maxLevel
    const nextInfo   = isMaxLevel ? null : ctx.getNextLevelInfo(meta.buildingID)

    // 找到對應 card 內的升級按鈕
    const btn = grid.querySelector<HTMLButtonElement>(
      `[data-building-id="${meta.buildingID}"] [data-upgrade-btn="${meta.buildingID}"]`
    )
    if (!btn) continue

    _applyButtonState(btn, meta.buildingID, nextInfo, ctx, isMaxLevel)

    // 同步更新 card 內的閘門狀態列（重新建立並替換）
    const card = grid.querySelector<HTMLElement>(`[data-building-id="${meta.buildingID}"]`)
    if (!card) continue
    _refreshCardGateRow(card, meta, state, nextInfo, ctx, isMaxLevel)
  }
}

/**
 * 套用升級按鈕的視覺狀態（依 canUpgrade 結果）。
 */
function _applyButtonState(
  btn: HTMLButtonElement,
  buildingID: number,
  nextInfo: NextLevelInfo | null,
  ctx: GuildBuildingPanelCtx,
  isMaxLevel: boolean,
): void {
  if (isMaxLevel) {
    btn.disabled = true
    btn.textContent = '已達上限'
    btn.style.background = COLOR_BTN_DIS
    btn.style.color = '#888'
    btn.style.cursor = 'default'
    return
  }

  const result = ctx.canUpgrade(buildingID)

  switch (result) {
    case 'SUCCESS':
      btn.disabled = false
      btn.textContent = '升級'
      btn.style.background = COLOR_BTN_EN
      btn.style.color = '#1a1a1a'
      btn.style.cursor = 'pointer'
      break

    case 'ALREADY_MAX':
      btn.disabled = true
      btn.textContent = '已達上限'
      btn.style.background = COLOR_BTN_DIS
      btn.style.color = '#888'
      btn.style.cursor = 'default'
      break

    case 'GOLD_INSUFFICIENT':
      btn.disabled = true
      btn.textContent = '金幣不足'
      btn.style.background = COLOR_BTN_DIS
      btn.style.color = '#888'
      btn.style.cursor = 'default'
      break

    case 'GUILD_LEVEL_INSUFFICIENT': {
      btn.disabled = true
      const req = nextInfo?.guildLevelReq ?? 0
      btn.textContent = req > 0 ? `需公會 Lv${req}` : '等級不足'
      btn.style.background = COLOR_BTN_DIS
      btn.style.color = '#888'
      btn.style.cursor = 'default'
      break
    }

    default:
      btn.disabled = true
      btn.textContent = '資料異常'
      btn.style.background = COLOR_BTN_DIS
      btn.style.color = '#888'
      btn.style.cursor = 'default'
  }
}

/**
 * 刷新 card 內的閘門狀態列（找到舊 gateRow 替換）。
 * 依 DOM 位置索引定位：card 的第 4 個子元素（index 4）為 gateRow。
 * 若定位失敗，不處理（安全降級）。
 */
function _refreshCardGateRow(
  card: HTMLElement,
  meta: BuildingDisplayMeta,
  state: BuildingState,
  nextInfo: NextLevelInfo | null,
  ctx: GuildBuildingPanelCtx,
  isMaxLevel: boolean,
): void {
  // card 子元素順序（依 _createBuildingCard）：
  // 0: titleRow / 1: effectRow / 2: nextLevelRow / 3: costRow / 4: gateRow / 5: btn
  const children = card.children
  if (children.length < 5) return

  const oldGateRow = children[4] as HTMLElement
  const newGateRow = _buildGateRow(meta, state, nextInfo, ctx, isMaxLevel)
  card.replaceChild(newGateRow, oldGateRow)
}

// ---------------------------------------------------------------------------
// 錯誤 card
// ---------------------------------------------------------------------------

/**
 * 建立資料異常時的佔位 card。
 */
function _createErrorCard(meta: BuildingDisplayMeta): HTMLElement {
  const card = document.createElement('div')
  card.style.cssText = [
    'padding:12px',
    `background:${COLOR_BG_CARD}`,
    `border:1px solid ${COLOR_FAIL}`,
    'border-radius:4px',
    `color:${COLOR_FAIL}`,
    'font-size:12px',
  ].join(';')
  card.textContent = `[${meta.name}] 資料異常（buildingID=${meta.buildingID}）`
  return card
}

// ---------------------------------------------------------------------------
// 顯示格式化
// ---------------------------------------------------------------------------

/**
 * 依 buildingID 把 effectValue 轉為顯示字串。
 * GDD §3.5 各建築語意：
 *   1 委託板          → 「N 委託槽」
 *   2 招募廣告欄       → 「N 小時」（秒 ÷ 3600）
 *   3 公會大廳        → 「N 名冒險者」
 *   4 公會櫃臺        → 「N 任務上限」
 *   5 預備金保險櫃    → 「N 小時破產緩衝」（秒 ÷ 3600）
 *   6 職員休息室 Lv0  → 呼叫端已特判，本函式 Lv1+ 用
 *                       → 「面試刷新 N 小時」（秒 ÷ 3600）
 *
 * @param buildingID
 * @param effectValue  raw 值（秒 or 個數）
 * @param level        當前等級（職員休息室 Lv0 由上層特判）
 */
function _formatEffect(buildingID: number, effectValue: number, level: number): string {
  switch (buildingID) {
    case 1:
      return `${effectValue} 委託槽`
    case 2:
      return `${_secToHour(effectValue)} 小時`
    case 3:
      return `${effectValue} 名冒險者`
    case 4:
      return `${effectValue} 任務上限`
    case 5:
      return `${_secToHour(effectValue)} 小時破產緩衝`
    case 6:
      if (level <= 0) return '未建造'
      return `面試刷新 ${_secToHour(effectValue)} 小時`
    default:
      return `${effectValue}`
  }
}

/**
 * 秒轉為小時字串（一位小數，整數時省略小數）。
 * 例：86400 → '24'、10800 → '3'、7200 → '2'
 */
function _secToHour(seconds: number): string {
  const hours = seconds / 3600
  return Number.isInteger(hours) ? String(hours) : hours.toFixed(1)
}

/**
 * UpgradeResult 代碼轉為使用者可讀錯誤訊息。
 */
function _upgradeResultToMessage(result: UpgradeResult, nextInfo: NextLevelInfo | null): string {
  switch (result) {
    case 'GOLD_INSUFFICIENT':
      return nextInfo ? `金幣不足（需 ${nextInfo.upgradeCost}g）` : '金幣不足'
    case 'GUILD_LEVEL_INSUFFICIENT':
      return nextInfo ? `需公會 Lv${nextInfo.guildLevelReq}` : '公會等級不足'
    case 'ALREADY_MAX':
      return '已達最高等級'
    case 'BUILDING_NOT_FOUND':
      return '建築資料找不到'
    default:
      return result
  }
}
