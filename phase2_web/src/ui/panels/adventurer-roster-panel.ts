/**
 * 冒險者名冊 Panel — Phase 2 升級版
 * Stage 5.2 實作
 *
 * 相對於 Phase 1 guild-hall-scene.ts buildAdventurerHall，本模組新增：
 *   - bio 顯示（最多兩行）
 *   - gender icon（♂/♀ 顏色區分）
 *   - wounded 狀態 badge（woundedUntil 透過事件取得但 Adventurer type 無此欄位，僅顯示 status）
 *   - growthTraits chip 列表
 *   - status 分色顯示
 *
 * 已知 jam 簡化延後項目：
 *   - woundedUntil 倒數：EventMap['adventurer:wounded'] 有 woundedUntil 欄位，
 *     但 Adventurer type 本身無此欄位，故目前僅顯示「療傷中」不含精確倒數時間。
 *     待 types/index.ts 補 woundedUntil?: number 後可解。
 */

import type { Adventurer, GuildState } from '../../types'
import { PROFESSION_TRAITS } from '../../data/traits'
import { GROWTH_TRAITS, nextXPMilestone, XP_MILESTONES } from '../../data/growth-traits'
import { eventBus } from '../../core/events'

/** 立繪基底路徑（public/ 內路徑，Vite 會自動處理） */
const PORTRAIT_BASE_PATH = '/images/characters/adventurers/'

// ---------------------------------------------------------------------------
// 公開介面
// ---------------------------------------------------------------------------

export interface AdventurerRosterPanelCtx {
  /** 取 GuildState（含 adventurers） */
  getGuild: () => GuildState
  /** 取名冊容量上限（由 building.getRosterCap 提供） */
  getRosterCap: () => number
  /** 玩家點選冒險者顯示詳情（可選回呼） */
  onSelect?: (adventurerID: string) => void
}

export interface AdventurerRosterPanelHandle {
  root: HTMLElement
  dispose: () => void
  refresh: () => void
}

// ---------------------------------------------------------------------------
// 顏色常數
// ---------------------------------------------------------------------------

const RANK_COLORS: Record<string, string> = {
  F: '#888888',
  E: '#aaaaaa',
  D: '#60a8d0',
  C: '#60c880',
  B: '#a060d0',
  A: '#d0a030',
  S: '#ff6030',
}

const STATUS_COLORS: Record<string, string> = {
  idle:       '#78c878',
  on_mission: '#d09040',
  wounded:    '#d04040',
  dead:       '#666666',
}

const STATUS_LABELS: Record<string, string> = {
  idle:       '待命中',
  on_mission: '執行任務中',
  wounded:    '療傷中',
  dead:       '已陣亡',
}

const GENDER_ICONS: [string, string] = ['♂', '♀']
const GENDER_COLORS: [string, string] = ['#6ab0e0', '#e085b0']

// ---------------------------------------------------------------------------
// 輔助函式
// ---------------------------------------------------------------------------

/** 取 rank 對應顏色，未知 rank 回 grey */
function rankColor(rank: string): string {
  return RANK_COLORS[rank] ?? '#888888'
}

/** 建立帶 cssText 的 div */
function div(css: string): HTMLDivElement {
  const el = document.createElement('div')
  el.style.cssText = css
  return el
}

/** 建立 span */
function span(text: string, css: string): HTMLSpanElement {
  const el = document.createElement('span')
  el.style.cssText = css
  el.textContent = text
  return el
}

// ---------------------------------------------------------------------------
// 子元件：冒險者卡片
// ---------------------------------------------------------------------------

function buildAdventurerCard(
  adv: Adventurer,
  onSelect?: (id: string) => void,
): HTMLElement {
  const isWounded = adv.status === 'wounded'
  const isDead    = adv.status === 'dead'

  // 卡片根元素
  const card = document.createElement('li')
  card.dataset.advId = adv.id
  card.style.cssText = [
    'list-style:none',
    'padding:10px 12px',
    'border:1px solid #3a3a3a',
    'border-radius:6px',
    'background:#2a2a2a',
    `opacity:${isDead ? '0.5' : '1'}`,
    'cursor:pointer',
    'user-select:none',
    'display:flex',
    'flex-direction:column',
    'gap:4px',
    'transition:border-color 0.15s',
  ].join(';')

  card.addEventListener('mouseenter', () => {
    card.style.borderColor = '#5a5a6a'
  })
  card.addEventListener('mouseleave', () => {
    card.style.borderColor = '#3a3a3a'
  })
  if (onSelect) {
    card.addEventListener('click', () => onSelect(adv.id))
  }

  // ── 行 1：rank badge + name + gender icon ──
  const row1 = div('display:flex;align-items:center;gap:6px;overflow:hidden')

  // Rank badge
  const rankBadge = span(adv.rank, [
    'display:inline-block',
    'padding:1px 6px',
    'border-radius:3px',
    'font-size:11px',
    'font-weight:bold',
    `background:${rankColor(adv.rank)}22`,
    `border:1px solid ${rankColor(adv.rank)}`,
    `color:${rankColor(adv.rank)}`,
    'flex-shrink:0',
    'letter-spacing:1px',
  ].join(';'))
  row1.appendChild(rankBadge)

  // Name
  const nameEl = span(adv.name, [
    'font-size:14px',
    'font-weight:bold',
    'color:#e8d8a0',
    'overflow:hidden',
    'text-overflow:ellipsis',
    'white-space:nowrap',
    'flex:1',
  ].join(';'))
  row1.appendChild(nameEl)

  // Gender icon
  const gIdx  = adv.gender === 1 ? 1 : 0
  const gIcon = span(GENDER_ICONS[gIdx], `font-size:12px;color:${GENDER_COLORS[gIdx]};flex-shrink:0`)
  row1.appendChild(gIcon)

  card.appendChild(row1)

  // ── 行 2：portrait（若有）+ 職業 ──
  // Phase 2 Jam：種族 trait 停用，UI 不顯示種族
  const row2 = div('display:flex;align-items:center;gap:6px')

  // 立繪：若 adv.portrait 有值，顯示縮圖；無則跳過（fallback 用職業 emoji 之後可加）
  if (adv.portrait) {
    const portraitImg = document.createElement('img')
    portraitImg.src = `${PORTRAIT_BASE_PATH}${adv.portrait}.png`
    portraitImg.alt = adv.name
    portraitImg.style.cssText = [
      'width:32px',
      'height:32px',
      'border-radius:4px',
      'object-fit:cover',
      'object-position:top center',
      'flex-shrink:0',
      'border:1px solid #4a4a4a',
    ].join(';')
    portraitImg.onerror = () => { portraitImg.style.display = 'none' }
    row2.appendChild(portraitImg)
  }

  const profName = PROFESSION_TRAITS[adv.professionId]?.name ?? adv.professionId
  const profTag = document.createElement('div')
  profTag.style.cssText = [
    'font-size:12px',
    'padding:1px 6px',
    'border-radius:3px',
    'background:#3a3060',
    'color:#b0a0e0',
    'display:inline-flex',
    'align-items:center',
    'gap:3px',
  ].join(';')
  const profIcon = document.createElement('img')
  profIcon.src = `/images/icons/profession/F-C-02_${adv.professionId}.png`
  profIcon.alt = adv.professionId
  profIcon.style.cssText = 'width:14px;height:14px;object-fit:contain'
  profIcon.onerror = () => { profIcon.style.display = 'none' }
  profTag.appendChild(profIcon)
  profTag.appendChild(document.createTextNode(profName))
  row2.appendChild(profTag)

  card.appendChild(row2)

  // ── 行 3：status badge ──
  const statusColor = STATUS_COLORS[adv.status] ?? '#888'
  const statusLabel = STATUS_LABELS[adv.status] ?? adv.status
  const statusBadge = span(statusLabel, [
    'font-size:12px',
    'padding:1px 6px',
    'border-radius:3px',
    `background:${statusColor}22`,
    `border:1px solid ${statusColor}55`,
    `color:${statusColor}`,
    'align-self:flex-start',
  ].join(';'))
  card.appendChild(statusBadge)

  // ── 行 4：XP 進度條 ──
  const xp            = adv.xp ?? 0
  const nextMs        = nextXPMilestone(xp)
  const prevMs        = [...XP_MILESTONES].reverse().find(m => m.xpRequired <= xp)?.xpRequired ?? 0
  const segmentSize   = nextMs === Infinity ? 1 : nextMs - prevMs
  const xpInSegment   = xp - prevMs
  const xpPct         = nextMs === Infinity ? 100 : Math.min(100, Math.round((xpInSegment / segmentSize) * 100))
  const xpLabelText   = nextMs === Infinity ? `XP ${xp}（滿）` : `XP ${xp} / ${nextMs}`

  const xpRow = div('margin-top:2px')

  const xpLabel = div('font-size:10px;color:#666;margin-bottom:2px')
  xpLabel.textContent = xpLabelText
  xpRow.appendChild(xpLabel)

  const xpTrack = div('height:4px;border-radius:2px;background:#1e1e2a;overflow:hidden')
  const xpFill  = div([
    `width:${xpPct}%`,
    'height:100%',
    'border-radius:2px',
    'background:#6080d0',
    'transition:width 0.3s',
  ].join(';'))
  xpTrack.appendChild(xpFill)
  xpRow.appendChild(xpTrack)
  card.appendChild(xpRow)

  // ── 行 5：bio（最多兩行） ──
  const bioText = adv.bio && adv.bio.trim().length > 0 ? adv.bio : '（無背景描述）'
  const bioEl   = div([
    'font-size:11px',
    'color:#777',
    'line-height:1.4',
    'display:-webkit-box',
    '-webkit-line-clamp:2',
    '-webkit-box-orient:vertical',
    'overflow:hidden',
    'margin-top:2px',
  ].join(';'))
  bioEl.textContent = bioText
  card.appendChild(bioEl)

  // ── 行 6：growthTraits chips（最多 3 個，若有才顯示） ──
  if (adv.growthTraits && adv.growthTraits.length > 0) {
    const chipsRow = div('display:flex;flex-wrap:wrap;gap:4px;margin-top:4px')
    const displayTraits = adv.growthTraits.slice(0, 3)
    for (const gt of displayTraits) {
      const def = GROWTH_TRAITS[gt.traitId]
      const label = def ? def.name : gt.traitId
      const chip  = span(label, [
        'font-size:10px',
        'padding:1px 5px',
        'border-radius:3px',
        'background:#3a2a18',
        'border:1px solid #7a5a28',
        'color:#c8a050',
      ].join(';'))
      chipsRow.appendChild(chip)
    }
    if (adv.growthTraits.length > 3) {
      const more = span(`+${adv.growthTraits.length - 3}`, 'font-size:10px;color:#666;align-self:center')
      chipsRow.appendChild(more)
    }
    card.appendChild(chipsRow)
  }

  // ── wounded 額外說明（狀態細節列） ──
  // 備註：woundedUntil 倒數因 Adventurer type 缺欄位，目前僅顯示狀態文字
  if (isWounded) {
    const woundNote = div('font-size:11px;color:#d04040;margin-top:2px')
    woundNote.textContent = '需要休養，暫時無法派遣任務'
    card.appendChild(woundNote)
  }

  return card
}

// ---------------------------------------------------------------------------
// 主元件：mountAdventurerRosterPanel
// ---------------------------------------------------------------------------

export function mountAdventurerRosterPanel(
  parent: HTMLElement,
  ctx: AdventurerRosterPanelCtx,
): AdventurerRosterPanelHandle {

  // ── 根元素 ──
  const root = div([
    'display:flex',
    'flex-direction:column',
    'height:100%',
    'background:#1a1a1f',
    'color:#e0e0e0',
    'font-family:sans-serif',
    'overflow:hidden',
  ].join(';'))

  // ── Header ──
  const header = div([
    'display:flex',
    'align-items:center',
    'justify-content:space-between',
    'padding:12px 16px 8px',
    'flex-shrink:0',
    'border-bottom:1px solid #3a3a3a',
  ].join(';'))

  const titleEl = span('冒險者名冊', [
    'font-size:15px',
    'font-weight:bold',
    'color:#e8d8a0',
    'letter-spacing:1px',
  ].join(';'))
  header.appendChild(titleEl)

  const capacityEl = div('font-size:13px;color:#888')
  header.appendChild(capacityEl)
  root.appendChild(header)

  // ── 名冊 grid ──
  const grid = document.createElement('ul')
  grid.style.cssText = [
    'margin:0',
    'padding:12px 16px',
    'display:grid',
    'grid-template-columns:repeat(auto-fill,minmax(200px,1fr))',
    'gap:10px',
    'overflow-y:auto',
    'flex:1',
    'min-height:0',
    'align-content:start',
  ].join(';')
  root.appendChild(grid)

  parent.appendChild(root)

  // ── 渲染邏輯 ──
  function render(): void {
    const guild   = ctx.getGuild()
    const cap     = ctx.getRosterCap()
    const sorted  = guild.adventurers
    const visible = sorted.filter(a => a.status !== 'dead')
    const total   = guild.adventurers.length
    const atCap   = total >= cap

    // 更新 header capacity 顯示
    capacityEl.style.color = atCap ? '#d08030' : '#888'
    capacityEl.textContent = `${total} / ${cap}${atCap ? '  （已滿）' : ''}`

    // 清空 grid
    grid.innerHTML = ''

    if (visible.length === 0) {
      const empty = div([
        'grid-column:1/-1',
        'padding:32px',
        'text-align:center',
        'color:#555',
        'font-size:14px',
      ].join(';'))
      empty.textContent = '尚未招募任何冒險者'
      grid.appendChild(empty)
      return
    }

    // dead 若存在也一起顯示（排在最後）
    const deadVisible = sorted.filter(a => a.status === 'dead')
    const allVisible  = [...visible, ...deadVisible]

    for (const adv of allVisible) {
      grid.appendChild(buildAdventurerCard(adv, ctx.onSelect))
    }
  }

  // 初次渲染
  render()

  // ── 事件訂閱 ──
  const onAdventurerDied   = () => render()
  const onAdventurerWounded = () => render()
  const onRecruitSuccess   = () => render()
  const onMissionCompleted = () => render()
  const onBuildingUpgraded = () => render()

  eventBus.on('adventurer:died',    onAdventurerDied)
  eventBus.on('adventurer:wounded', onAdventurerWounded)
  eventBus.on('recruit:success',    onRecruitSuccess)
  eventBus.on('mission:completed',  onMissionCompleted)
  eventBus.on('building:upgraded',  onBuildingUpgraded)

  // ── dispose ──
  function dispose(): void {
    eventBus.off('adventurer:died',    onAdventurerDied)
    eventBus.off('adventurer:wounded', onAdventurerWounded)
    eventBus.off('recruit:success',    onRecruitSuccess)
    eventBus.off('mission:completed',  onMissionCompleted)
    eventBus.off('building:upgraded',  onBuildingUpgraded)
    root.remove()
  }

  return { root, dispose, refresh: render }
}
