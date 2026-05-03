/**
 * Stage 5.3 — GuildOverviewPanel（公會總覽面板）
 *
 * 純新增 panel；phase 1 不存在。
 * 提供「公會狀態總攬」儀表板：
 *  - 公會等級 + 稱號（FT-06）
 *  - 聲望 + 距下一級門檻進度條
 *  - 金幣 / 破產警告狀態
 *  - 世界危險度（C-06）+ 名稱 + 任務池權重視覺化
 *  - 進行中任務數 / 上限（FT-07）
 *
 * vanilla DOM；無框架依賴。
 */

import type { GuildState, GuildLevel, WorldDanger, Difficulty } from '../../types'
import type { WorldDangerState } from '../../systems/world-danger'
import { getCurrentLevel, getDangerData, getMaxDebt, getPoolWeights } from '../../systems/world-danger'
import { eventBus } from '../../core/events'

// ---------------------------------------------------------------------------
// 公開介面
// ---------------------------------------------------------------------------

export interface GuildOverviewPanelCtx {
  getGuild: () => GuildState
  getWorldDanger: () => WorldDangerState
  /** main.ts 注入：building.getMaxConcurrentMissions(states) */
  getMaxConcurrentMissions: () => number
  /** main.ts 注入：guild.getGuildLevel(reputation) */
  computeGuildLevel: (reputation: number) => GuildLevel
  /** main.ts 注入：guild.getGuildTitle(level) */
  getGuildTitle: (level: GuildLevel) => string
  /** main.ts 注入：guild.getMaxDifficulty(level) */
  getMaxDifficulty: (level: GuildLevel) => Difficulty
  /**
   * 下一級聲望門檻。null 表示已達最高等級。
   * 由 main.ts 注入，避免直接存取 guild.ts 的 internal GUILD_LEVEL_TABLE。
   */
  getNextLevelThreshold: (currentLevel: GuildLevel) => number | null
}

export interface GuildOverviewPanelHandle {
  root: HTMLElement
  dispose: () => void
  refresh: () => void
}

// ---------------------------------------------------------------------------
// 顏色常數
// ---------------------------------------------------------------------------

/** 危險度等級 → 主題色 */
const DANGER_COLOR: Record<WorldDanger, string> = {
  E: '#4caf50',  // 綠
  D: '#f0c060',  // 黃
  C: '#ff9800',  // 橙
  B: '#f44336',  // 紅
  A: '#9c27b0',  // 紫
}

/** 任務池難度區段 → 顏色（F_E 灰 / D 黃 / C 橙 / B 紅 / A 深紅 / S_SSS 紫） */
const POOL_SEGMENT_COLORS: Record<string, string> = {
  F_E:   '#888888',
  D:     '#f0c060',
  C:     '#ff9800',
  B:     '#f44336',
  A:     '#b71c1c',
  S_SSS: '#9c27b0',
}

/** 任務池難度區段顯示標籤 */
const POOL_SEGMENT_LABELS: Record<string, string> = {
  F_E:   'F~E',
  D:     'D',
  C:     'C',
  B:     'B',
  A:     'A',
  S_SSS: 'S+',
}

// ---------------------------------------------------------------------------
// 掛載入口
// ---------------------------------------------------------------------------

/**
 * 掛載公會總覽面板。
 * @param parent 父元素（mount target）
 * @param ctx    資料注入 context
 * @returns      root 元素 + dispose（清除訂閱）+ refresh（手動刷新）
 */
export function mountGuildOverviewPanel(
  parent: HTMLElement,
  ctx: GuildOverviewPanelCtx,
): GuildOverviewPanelHandle {
  // ── 建立根元素 ──────────────────────────────────────────────────────────────
  const root = document.createElement('div')
  root.className = 'guild-overview-panel'
  root.style.cssText = [
    'display:flex',
    'flex-direction:column',
    'gap:0',
    'padding:12px',
    'overflow-y:auto',
    'height:100%',
    'box-sizing:border-box',
    'font-family:\'Courier New\',monospace',
    'font-size:13px',
    'color:#c8b98a',
    'background:#0d0d0d',
  ].join(';')

  // ── section 容器佔位（稍後 refresh 填充） ────────────────────────────────────
  const secGuild    = _createSection()
  const secResource = _createSection()
  const secDanger   = _createSection()
  const secMission  = _createSection()

  root.appendChild(secGuild)
  root.appendChild(secResource)
  root.appendChild(secDanger)
  root.appendChild(secMission)

  parent.appendChild(root)

  // ── 首次渲染 ────────────────────────────────────────────────────────────────
  _renderAll(ctx, secGuild, secResource, secDanger, secMission)

  // ── EventBus 訂閱 ───────────────────────────────────────────────────────────
  const _onRefresh = () => _renderAll(ctx, secGuild, secResource, secDanger, secMission)

  eventBus.on('guild:level_up',       _onRefresh)
  eventBus.on('reputation:changed',   _onRefresh)
  eventBus.on('gold:changed',         _onRefresh)
  eventBus.on('danger:level_changed', _onRefresh)
  eventBus.on('mission:completed',    _onRefresh)

  // ── 清理函式 ────────────────────────────────────────────────────────────────
  function dispose(): void {
    eventBus.off('guild:level_up',       _onRefresh)
    eventBus.off('reputation:changed',   _onRefresh)
    eventBus.off('gold:changed',         _onRefresh)
    eventBus.off('danger:level_changed', _onRefresh)
    eventBus.off('mission:completed',    _onRefresh)
    root.remove()
  }

  function refresh(): void {
    _renderAll(ctx, secGuild, secResource, secDanger, secMission)
  }

  return { root, dispose, refresh }
}

// ---------------------------------------------------------------------------
// 渲染協調器
// ---------------------------------------------------------------------------

/** 一次更新全部 section 內容（清空再重建子元素） */
function _renderAll(
  ctx: GuildOverviewPanelCtx,
  secGuild:    HTMLElement,
  secResource: HTMLElement,
  secDanger:   HTMLElement,
  secMission:  HTMLElement,
): void {
  const guild = ctx.getGuild()
  const wd    = ctx.getWorldDanger()

  _fillGuildSection(secGuild, guild, ctx)
  _fillResourceSection(secResource, guild)
  _fillDangerSection(secDanger, wd)
  _fillMissionSection(secMission, guild, ctx)
}

// ---------------------------------------------------------------------------
// Section：公會資訊
// ---------------------------------------------------------------------------

/**
 * 顯示：稱號 + 等級徽章、聲望進度條、可接最高難度。
 */
function _fillGuildSection(
  section: HTMLElement,
  guild: GuildState,
  ctx: GuildOverviewPanelCtx,
): void {
  _clearSection(section)

  const rep          = guild.resources.reputation
  const currentLevel = ctx.computeGuildLevel(rep)
  const title        = ctx.getGuildTitle(currentLevel)
  const maxDiff      = ctx.getMaxDifficulty(currentLevel)
  const nextThresh   = ctx.getNextLevelThreshold(currentLevel)

  // 標題列：稱號 + 等級標籤
  const titleRow = document.createElement('div')
  titleRow.style.cssText = 'display:flex;align-items:center;gap:8px;margin-bottom:6px'

  const badge = document.createElement('span')
  badge.style.cssText = [
    'padding:2px 6px',
    'border:1px solid #f0c060',
    'color:#f0c060',
    'font-size:11px',
    'white-space:nowrap',
  ].join(';')
  badge.textContent = `Lv${currentLevel}`

  const titleEl = document.createElement('span')
  titleEl.style.cssText = 'font-size:14px;font-weight:bold'
  titleEl.textContent = title

  titleRow.appendChild(badge)
  titleRow.appendChild(titleEl)
  section.appendChild(titleRow)

  // 可接難度標示
  const diffRow = document.createElement('div')
  diffRow.style.cssText = 'font-size:12px;margin-bottom:8px;opacity:0.8'
  diffRow.textContent = `可接最高難度：${maxDiff}`
  section.appendChild(diffRow)

  // 聲望進度條
  section.appendChild(_buildRepBar(rep, currentLevel, nextThresh))
}

/**
 * 建立聲望進度條區塊。
 * - 若 nextThreshold 為 null（最高等級），顯示「已達上限」
 * - jam 簡化：顯示「current / nextThreshold」，不計算 prev threshold 做精確進度
 */
function _buildRepBar(
  rep: number,
  currentLevel: GuildLevel,
  nextThresh: number | null,
): HTMLElement {
  const wrap = document.createElement('div')
  wrap.style.cssText = 'margin-bottom:4px'

  const labelRow = document.createElement('div')
  labelRow.style.cssText = 'display:flex;justify-content:space-between;font-size:11px;margin-bottom:3px;opacity:0.8'

  if (nextThresh === null) {
    // 最高等級
    labelRow.innerHTML = '<span>聲望</span><span style="color:#f0c060">已達最高等級</span>'
    wrap.appendChild(labelRow)

    const barBg = _createBarBg()
    const barFg = _createBarFg('100')
    barFg.style.background = '#f0c060'
    barBg.appendChild(barFg)
    wrap.appendChild(barBg)
  } else {
    const displayed = Math.max(rep, 0)
    const pct = Math.min(Math.floor((displayed / nextThresh) * 100), 100)

    const leftLabel = document.createElement('span')
    leftLabel.textContent = `聲望 ${rep}`

    const rightLabel = document.createElement('span')
    rightLabel.textContent = `下一級 ${nextThresh}（Lv${(currentLevel + 1) as number}）`

    labelRow.appendChild(leftLabel)
    labelRow.appendChild(rightLabel)
    wrap.appendChild(labelRow)

    const barBg = _createBarBg()
    const barFg = _createBarFg(String(pct))
    barBg.appendChild(barFg)
    wrap.appendChild(barBg)
  }

  return wrap
}

// ---------------------------------------------------------------------------
// Section：資源狀態
// ---------------------------------------------------------------------------

/**
 * 顯示：金幣（含負值警告色）
 * jam 簡化：破產倒數 callback 由 ctx 預留，本版僅顯示金幣負值警告文字。
 */
function _fillResourceSection(section: HTMLElement, guild: GuildState): void {
  _clearSection(section)

  const gold     = guild.resources.gold
  const isNeg    = gold < 0
  const goldColor = isNeg ? '#f44336' : '#c8b98a'

  const row = document.createElement('div')
  row.style.cssText = `display:flex;align-items:center;gap:8px;font-size:13px;color:${goldColor}`

  const label = document.createElement('span')
  label.textContent = '金幣：'

  const value = document.createElement('span')
  value.style.cssText = `font-weight:bold;color:${goldColor}`
  value.textContent = `${gold}g`

  row.appendChild(label)
  row.appendChild(value)

  if (isNeg) {
    const warn = document.createElement('span')
    warn.style.cssText = 'font-size:11px;color:#f44336;opacity:0.9'
    warn.textContent = '— 金幣不足，注意破產風險'
    row.appendChild(warn)
  }

  section.appendChild(row)
}

// ---------------------------------------------------------------------------
// Section：世界危險度
// ---------------------------------------------------------------------------

/**
 * 顯示：危險度等級徽章 + 名稱、任務池權重 mini-bar、債務上限。
 * 危險度資料缺失時 fallback 為 E 階。
 */
function _fillDangerSection(section: HTMLElement, wd: WorldDangerState): void {
  _clearSection(section)

  const danger   = getCurrentLevel(wd)
  const data     = getDangerData(danger) ?? getDangerData('E')
  const maxDebt  = getMaxDebt(wd)
  const name     = data?.name ?? '未知'
  const color    = DANGER_COLOR[danger] ?? DANGER_COLOR['E']

  // 危險度標題列
  const titleRow = document.createElement('div')
  titleRow.style.cssText = 'display:flex;align-items:center;gap:8px;margin-bottom:8px'

  const badge = document.createElement('span')
  badge.style.cssText = [
    `background:${color}`,
    'color:#0d0d0d',
    'font-weight:bold',
    'padding:2px 7px',
    'font-size:12px',
    'border-radius:2px',
  ].join(';')
  badge.textContent = danger  // 字母徽章：E / D / C / B / A

  const nameEl = document.createElement('span')
  nameEl.style.cssText = `font-size:13px;color:${color};font-weight:bold`
  nameEl.textContent = name

  titleRow.appendChild(badge)
  titleRow.appendChild(nameEl)
  section.appendChild(titleRow)

  // 任務池權重 mini-bar
  section.appendChild(_buildPoolWeightBar(wd))

  // 債務上限
  const debtRow = document.createElement('div')
  debtRow.style.cssText = 'font-size:11px;margin-top:6px;opacity:0.75'
  debtRow.textContent = `當前債務上限：${maxDebt}g`
  section.appendChild(debtRow)
}

/**
 * 建立任務池難度分布 mini-bar（6 段橫條）。
 * 依各段 weight 佔 total 的比例決定寬度。
 */
function _buildPoolWeightBar(wd: WorldDangerState): HTMLElement {
  const weights = getPoolWeights(wd)

  const segments: Array<{ key: string; value: number }> = [
    { key: 'F_E',   value: weights.F_E },
    { key: 'D',     value: weights.D },
    { key: 'C',     value: weights.C },
    { key: 'B',     value: weights.B },
    { key: 'A',     value: weights.A },
    { key: 'S_SSS', value: weights.S_SSS },
  ]

  const total = segments.reduce((sum, s) => sum + s.value, 0)

  const wrap = document.createElement('div')
  wrap.style.cssText = 'display:flex;flex-direction:column;gap:3px'

  // 橫條本體
  const barRow = document.createElement('div')
  barRow.style.cssText = [
    'display:flex',
    'height:10px',
    'border-radius:2px',
    'overflow:hidden',
    'background:#2a2a2a',
  ].join(';')

  segments.forEach(seg => {
    if (seg.value <= 0) return
    const pct = total > 0 ? (seg.value / total) * 100 : 0
    const segEl = document.createElement('div')
    segEl.style.cssText = [
      `width:${pct.toFixed(1)}%`,
      `background:${POOL_SEGMENT_COLORS[seg.key] ?? '#888'}`,
      'transition:width 0.3s ease',
    ].join(';')
    segEl.title = `${POOL_SEGMENT_LABELS[seg.key] ?? seg.key}: ${seg.value}`
    barRow.appendChild(segEl)
  })

  wrap.appendChild(barRow)

  // 圖例標籤列（只顯示 weight > 0 的段）
  const legendRow = document.createElement('div')
  legendRow.style.cssText = 'display:flex;gap:6px;flex-wrap:wrap'

  segments.filter(s => s.value > 0).forEach(seg => {
    const item = document.createElement('span')
    item.style.cssText = `font-size:10px;color:${POOL_SEGMENT_COLORS[seg.key] ?? '#888'}`
    item.textContent = `${POOL_SEGMENT_LABELS[seg.key] ?? seg.key}:${seg.value}`
    legendRow.appendChild(item)
  })

  wrap.appendChild(legendRow)
  return wrap
}

// ---------------------------------------------------------------------------
// Section：任務狀態
// ---------------------------------------------------------------------------

/**
 * 顯示：進行中任務數 / 上限（來自 building.getMaxConcurrentMissions）
 */
function _fillMissionSection(
  section: HTMLElement,
  guild: GuildState,
  ctx: GuildOverviewPanelCtx,
): void {
  _clearSection(section)

  const active = guild.activeMissions.length
  const max    = ctx.getMaxConcurrentMissions()
  const full   = active >= max

  const row = document.createElement('div')
  row.style.cssText = 'font-size:13px;display:flex;align-items:center;gap:8px'

  const label = document.createElement('span')
  label.textContent = '進行中任務：'

  const count = document.createElement('span')
  count.style.cssText = `font-weight:bold;color:${full ? '#f44336' : '#c8b98a'}`
  count.textContent = `${active} / ${max}`

  row.appendChild(label)
  row.appendChild(count)

  if (full) {
    const hint = document.createElement('span')
    hint.style.cssText = 'font-size:11px;color:#f44336;opacity:0.9'
    hint.textContent = '（任務欄已滿）'
    row.appendChild(hint)
  }

  section.appendChild(row)
}

// ---------------------------------------------------------------------------
// 共用輔助函式
// ---------------------------------------------------------------------------

/** 建立帶分隔線的 section 容器 */
function _createSection(): HTMLElement {
  const sec = document.createElement('div')
  sec.style.cssText = [
    'padding:10px 0',
    'border-bottom:1px solid #3a3a3a',
  ].join(';')
  return sec
}

/** 清空 section 內的所有子元素 */
function _clearSection(section: HTMLElement): void {
  section.innerHTML = ''
}

/** 建立進度條背景元素 */
function _createBarBg(): HTMLElement {
  const bg = document.createElement('div')
  bg.style.cssText = [
    'width:100%',
    'height:8px',
    'background:#2a2a2a',
    'border-radius:2px',
    'overflow:hidden',
  ].join(';')
  return bg
}

/**
 * 建立進度條前景元素
 * @param pctStr 進度百分比字串（'0' ~ '100'）
 */
function _createBarFg(pctStr: string): HTMLElement {
  const fg = document.createElement('div')
  fg.style.cssText = [
    `width:${pctStr}%`,
    'height:100%',
    'background:#f0c060',
    'border-radius:2px',
    'transition:width 0.3s ease',
  ].join(';')
  return fg
}
