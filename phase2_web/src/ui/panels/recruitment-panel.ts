/**
 * 招募 Panel — Phase 2 新增
 * Stage 5.4 實作
 *
 * 職責：
 *   - 新手池（F/E 階）卡片列表，免費接納
 *   - 老手池（D~S 階）卡片列表，顯示費用 + 聲望門檻
 *   - 刷新 bar（自動刷新倒數 + 免費次數 + 手動刷新按鈕）
 *   - 依金幣 / 聲望 / 名冊容量動態 enable/disable 按鈕
 *
 * 訂閱事件：
 *   - 'recruit:pool_refreshed' → 兩池重繪
 *   - 'recruit:success'        → 兩池重繪（候選從池移除）
 *   - 'gold:changed'           → 老手按鈕 enable 狀態更新
 *   - 'reputation:changed'     → 老手按鈕 enable 狀態更新
 *   - 'building:upgraded'      → rosterCap 可能變化，重繪
 *   - 'guild:level_up'         → 老手池上限可能變化，重繪
 *
 * Jam 簡化延後項目（見底部）
 */

import type { GuildLevel } from '../../types'
import type { RecruitmentState, RecruitCandidate } from '../../systems/recruitment'
import { getNextAutoRefreshTimestamp, getFreeRefreshRemaining } from '../../systems/recruitment'
import { PROFESSION_TRAITS } from '../../data/traits'
// Phase 2 Jam 種族 trait 停用，UI 不再顯示種族
const PORTRAIT_BASE_PATH = '/images/characters/adventurers/'
import { DAILY_FREE_REFRESH, REFRESH_COST } from '../../data/constants'
import { eventBus } from '../../core/events'
import type { GuildState } from '../../types'

// ---------------------------------------------------------------------------
// 公開介面
// ---------------------------------------------------------------------------

export interface RecruitmentPanelCtx {
  /** 取最新 GuildState（含 resources、adventurers） */
  getGuild: () => GuildState
  /** 取 RecruitmentState（兩池 + 刷新狀態） */
  getRecruitment: () => RecruitmentState
  /** 取名冊容量上限（由 building.getRosterCap 提供） */
  getRosterCap: () => number
  /** 取目前公會等級（由 guild.getGuildLevel(reputation) 提供） */
  getGuildLevel: () => GuildLevel
  /** 玩家點「接納新手」：成功時應 emit recruit:success */
  onRecruitRookie: (candidateID: number) => void
  /** 玩家點「邀請老手」：成功時應扣金幣 + emit recruit:success */
  onRecruitVeteran: (candidateID: number) => void
  /** 玩家點「手動刷新」：依 freeRefreshRemaining 決定免費或付費 */
  onManualRefresh: () => void
}

export interface RecruitmentPanelHandle {
  root: HTMLElement
  dispose: () => void
  refresh: () => void
}

// ---------------------------------------------------------------------------
// 顏色常數（與 adventurer-roster-panel 一致）
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

/** 新手卡邊框色（淺綠） */
const ROOKIE_BORDER = '#5a8a5a'
/** 老手卡邊框色（金棕） */
const VETERAN_BORDER = '#8a6a4a'
/** 按鈕暖橘 */
const BTN_COLOR = '#f0c060'
/** 按鈕 disabled 灰 */
const BTN_DISABLED_COLOR = '#555555'

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

/**
 * 格式化剩餘毫秒為 「H:MM:SS」字串。
 * 若 ms <= 0 回傳 '已到期'。
 */
function formatCountdown(ms: number): string {
  if (ms <= 0) return '已到期'
  const totalSec = Math.floor(ms / 1000)
  const hours    = Math.floor(totalSec / 3600)
  const minutes  = Math.floor((totalSec % 3600) / 60)
  const secs     = totalSec % 60
  const mm = String(minutes).padStart(2, '0')
  const ss = String(secs).padStart(2, '0')
  return `${hours}:${mm}:${ss}`
}

// ---------------------------------------------------------------------------
// 刷新 Bar
// ---------------------------------------------------------------------------

interface RefreshBarElements {
  root: HTMLElement
  countdownEl: HTMLSpanElement
  freeCountEl: HTMLSpanElement
  refreshBtn: HTMLButtonElement
}

/** 建立刷新 bar 靜態結構，回傳元素引用供後續 update 使用 */
function buildRefreshBar(): RefreshBarElements {
  const root = div([
    'display:flex',
    'align-items:center',
    'gap:12px',
    'padding:8px 16px',
    'background:#16161a',
    'border-bottom:1px solid #3a3a3a',
    'flex-shrink:0',
    'flex-wrap:wrap',
  ].join(';'))

  // 倒數顯示
  const countdownLabel = span('下次自動刷新：', 'font-size:12px;color:#888')
  const countdownEl    = span('--:--:--', 'font-size:12px;color:#c0a060;min-width:60px')
  root.appendChild(countdownLabel)
  root.appendChild(countdownEl)

  // 分隔
  const sep = span('|', 'font-size:12px;color:#444;margin:0 2px')
  root.appendChild(sep)

  // 免費次數顯示
  const freeLabel = span('免費刷新：', 'font-size:12px;color:#888')
  const freeCountEl = span(`0 / ${DAILY_FREE_REFRESH}`, 'font-size:12px;color:#90c890')
  root.appendChild(freeLabel)
  root.appendChild(freeCountEl)

  // 手動刷新按鈕
  const refreshBtn = document.createElement('button')
  refreshBtn.style.cssText = [
    'margin-left:auto',
    'padding:4px 12px',
    'border:none',
    'border-radius:4px',
    `background:${BTN_COLOR}`,
    'color:#1a1a1f',
    'font-size:12px',
    'font-weight:bold',
    'cursor:pointer',
    'white-space:nowrap',
  ].join(';')
  root.appendChild(refreshBtn)

  return { root, countdownEl, freeCountEl, refreshBtn }
}

/**
 * 更新刷新 bar 的動態數值。
 * 由 tick 或 refresh() 呼叫。
 */
function updateRefreshBar(
  els: RefreshBarElements,
  state: RecruitmentState,
  gold: number,
  onRefresh: () => void,
): void {
  const nextTs      = getNextAutoRefreshTimestamp(state)
  const remaining   = nextTs - Date.now()
  const freeLeft    = getFreeRefreshRemaining(state)
  const isFree      = freeLeft > 0
  const canPayRefresh = gold >= REFRESH_COST

  // 倒數
  els.countdownEl.textContent = formatCountdown(remaining)

  // 免費次數
  els.freeCountEl.textContent = `${freeLeft} / ${DAILY_FREE_REFRESH}`
  els.freeCountEl.style.color = freeLeft > 0 ? '#90c890' : '#888'

  // 按鈕文字與 enable 狀態
  if (isFree) {
    els.refreshBtn.textContent = '刷新（免費）'
    els.refreshBtn.disabled    = false
    els.refreshBtn.style.background = BTN_COLOR
    els.refreshBtn.style.cursor     = 'pointer'
    els.refreshBtn.style.color      = '#1a1a1f'
    els.refreshBtn.title = ''
    // 重新綁定（先移除舊綁定）
    els.refreshBtn.onclick = onRefresh
  } else {
    const label = `刷新（${REFRESH_COST}g）`
    els.refreshBtn.textContent = label
    const canClick = canPayRefresh
    els.refreshBtn.disabled    = !canClick
    els.refreshBtn.style.background = canClick ? BTN_COLOR : BTN_DISABLED_COLOR
    els.refreshBtn.style.cursor     = canClick ? 'pointer' : 'not-allowed'
    els.refreshBtn.style.color      = canClick ? '#1a1a1f' : '#888'
    els.refreshBtn.title = canClick ? '' : '金幣不足'
    els.refreshBtn.onclick = canClick ? onRefresh : null
  }
}

// ---------------------------------------------------------------------------
// 招募候選卡片
// ---------------------------------------------------------------------------

/**
 * 建立新手候選卡片（免費接納）。
 * 按鈕僅在名冊未滿時可點擊。
 */
function buildRookieCard(
  c: RecruitCandidate,
  rosterFull: boolean,
  onRecruit: (id: number) => void,
): HTMLElement {
  const adv = c.adventurer
  const card = document.createElement('li')
  card.style.cssText = [
    'list-style:none',
    'padding:10px 12px',
    `border:1px solid ${ROOKIE_BORDER}`,
    'border-radius:6px',
    'background:#1e251e',
    'display:flex',
    'flex-direction:column',
    'gap:5px',
  ].join(';')

  // 行 1：rank badge + name
  const row1 = div('display:flex;align-items:center;gap:6px;overflow:hidden')

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
  card.appendChild(row1)

  // 行 2：portrait + 職業（Phase 2 Jam 種族 trait 停用）
  const row2 = div('display:flex;align-items:center;gap:6px')
  if (adv.portrait) {
    const portraitImg = document.createElement('img')
    portraitImg.src = `${PORTRAIT_BASE_PATH}${adv.portrait}.png`
    portraitImg.alt = adv.name
    portraitImg.className = 'portrait-card-sm'
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
  profIcon.className = 'icon-profession'
  profIcon.onerror = () => { profIcon.style.display = 'none' }
  profTag.appendChild(profIcon)
  profTag.appendChild(document.createTextNode(profName))
  row2.appendChild(profTag)

  const freeTag = span('免費', [
    'font-size:11px',
    'padding:1px 6px',
    'border-radius:3px',
    'background:#1a4020',
    'color:#60c860',
    'margin-left:auto',
    'flex-shrink:0',
  ].join(';'))
  row2.appendChild(freeTag)
  card.appendChild(row2)

  // 行 3：bio（單行截斷）
  const bioText = adv.bio && adv.bio.trim().length > 0 ? adv.bio : '（無背景描述）'
  const bioEl   = div([
    'font-size:11px',
    'color:#667766',
    'white-space:nowrap',
    'overflow:hidden',
    'text-overflow:ellipsis',
  ].join(';'))
  bioEl.textContent = bioText
  card.appendChild(bioEl)

  // 行 4：接納按鈕
  const btn = document.createElement('button')
  btn.textContent = '接納'
  btn.disabled    = rosterFull
  btn.style.cssText = [
    'margin-top:4px',
    'padding:5px 0',
    'border:none',
    'border-radius:4px',
    `background:${rosterFull ? BTN_DISABLED_COLOR : BTN_COLOR}`,
    `color:${rosterFull ? '#888' : '#1a1a1f'}`,
    'font-size:13px',
    'font-weight:bold',
    `cursor:${rosterFull ? 'not-allowed' : 'pointer'}`,
    'width:100%',
  ].join(';')
  btn.title = rosterFull ? '名冊已滿' : ''
  if (!rosterFull) {
    btn.addEventListener('click', () => onRecruit(c.candidateID))
  }
  card.appendChild(btn)

  return card
}

/**
 * 建立老手候選卡片（需金幣 + 聲望門檻）。
 * 依金幣 / 聲望 / 名冊容量決定按鈕狀態。
 */
function buildVeteranCard(
  c: RecruitCandidate,
  gold: number,
  reputation: number,
  rosterFull: boolean,
  onRecruit: (id: number) => void,
): HTMLElement {
  const adv      = c.adventurer
  const canPay   = gold >= c.cost
  const canRep   = reputation >= c.reputationReq
  const canHire  = canPay && canRep && !rosterFull

  const card = document.createElement('li')
  card.style.cssText = [
    'list-style:none',
    'padding:10px 12px',
    `border:1px solid ${VETERAN_BORDER}`,
    'border-radius:6px',
    'background:#231e14',
    'display:flex',
    'flex-direction:column',
    'gap:5px',
  ].join(';')

  // 行 1：rank badge + name
  const row1 = div('display:flex;align-items:center;gap:6px;overflow:hidden')

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
  card.appendChild(row1)

  // 行 2：portrait + 職業（Phase 2 Jam 種族 trait 停用）
  const row2 = div('display:flex;align-items:center;gap:6px')
  if (adv.portrait) {
    const portraitImg = document.createElement('img')
    portraitImg.src = `${PORTRAIT_BASE_PATH}${adv.portrait}.png`
    portraitImg.alt = adv.name
    portraitImg.className = 'portrait-card-sm'
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
  profIcon.className = 'icon-profession'
  profIcon.onerror = () => { profIcon.style.display = 'none' }
  profTag.appendChild(profIcon)
  profTag.appendChild(document.createTextNode(profName))
  row2.appendChild(profTag)
  card.appendChild(row2)

  // 行 3：bio（單行截斷）
  const bioText = adv.bio && adv.bio.trim().length > 0 ? adv.bio : '（無背景描述）'
  const bioEl   = div([
    'font-size:11px',
    'color:#776655',
    'white-space:nowrap',
    'overflow:hidden',
    'text-overflow:ellipsis',
  ].join(';'))
  bioEl.textContent = bioText
  card.appendChild(bioEl)

  // 行 4：費用 + 聲望門檻
  const costRow = div('display:flex;align-items:center;gap:10px')

  const costEl = span(`邀請費：${c.cost}g`, [
    'font-size:12px',
    `color:${canPay ? '#c8a050' : '#d06060'}`,
  ].join(';'))
  costRow.appendChild(costEl)

  if (c.reputationReq > 0) {
    const repEl = span(`聲望需求：${c.reputationReq}`, [
      'font-size:12px',
      `color:${canRep ? '#80b0d0' : '#d06060'}`,
    ].join(';'))
    costRow.appendChild(repEl)
  }
  card.appendChild(costRow)

  // 行 5：邀請按鈕 + 原因提示
  const btnRow = div('display:flex;align-items:center;gap:8px;margin-top:2px')

  const btn = document.createElement('button')
  btn.textContent = '邀請'
  btn.disabled    = !canHire
  btn.style.cssText = [
    'padding:5px 16px',
    'border:none',
    'border-radius:4px',
    `background:${canHire ? BTN_COLOR : BTN_DISABLED_COLOR}`,
    `color:${canHire ? '#1a1a1f' : '#888'}`,
    'font-size:13px',
    'font-weight:bold',
    `cursor:${canHire ? 'pointer' : 'not-allowed'}`,
    'flex-shrink:0',
  ].join(';')
  if (canHire) {
    btn.addEventListener('click', () => onRecruit(c.candidateID))
  }
  btnRow.appendChild(btn)

  // 原因提示（金幣不足 / 聲望不足 / 名冊已滿）
  if (!canHire) {
    const reasons: string[] = []
    if (rosterFull) reasons.push('名冊已滿')
    else {
      if (!canPay) reasons.push('金幣不足')
      if (!canRep) reasons.push('聲望不足')
    }
    const hintEl = span(reasons.join('・'), 'font-size:11px;color:#d06060')
    btnRow.appendChild(hintEl)
  }

  card.appendChild(btnRow)

  return card
}

// ---------------------------------------------------------------------------
// 候選池區塊（section wrapper）
// ---------------------------------------------------------------------------

interface SectionElements {
  root: HTMLElement
  list: HTMLUListElement
  countEl: HTMLSpanElement
}

/** 建立帶標題的候選池 section */
function buildSection(title: string, borderColor: string): SectionElements {
  const root = div([
    'flex-shrink:0',
    'padding:12px 16px',
    'border-top:1px solid #2a2a2a',
  ].join(';'))

  // section header
  const header = div('display:flex;align-items:baseline;gap:8px;margin-bottom:10px')

  const titleEl = span(title, [
    'font-size:14px',
    'font-weight:bold',
    `color:${borderColor}`,
    'letter-spacing:1px',
  ].join(';'))
  header.appendChild(titleEl)

  const countEl = span('(0)', 'font-size:12px;color:#666')
  header.appendChild(countEl)
  root.appendChild(header)

  // 候選者 grid
  const list = document.createElement('ul')
  list.style.cssText = [
    'margin:0',
    'padding:0',
    'display:grid',
    'grid-template-columns:repeat(auto-fill,minmax(200px,1fr))',
    'gap:8px',
  ].join(';')
  root.appendChild(list)

  return { root, list, countEl }
}

// ---------------------------------------------------------------------------
// 主元件：mountRecruitmentPanel
// ---------------------------------------------------------------------------

export function mountRecruitmentPanel(
  parent: HTMLElement,
  ctx: RecruitmentPanelCtx,
): RecruitmentPanelHandle {

  // ── 計時器 ID（刷新 bar 每秒更新倒數） ──
  let _tickTimer: ReturnType<typeof setInterval> | null = null

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

  const heroIcon = document.createElement('img')
  heroIcon.src = '/images/scene/A-07_default.png'
  heroIcon.alt = ''
  heroIcon.className = 'hero-icon hero-icon-recruitment'
  heroIcon.onerror = () => { heroIcon.style.display = 'none' }

  const titleWrap = div('display:flex;align-items:center')
  titleWrap.appendChild(heroIcon)
  const titleEl = span('招募', [
    'font-size:15px',
    'font-weight:bold',
    'color:#e8d8a0',
    'letter-spacing:1px',
  ].join(';'))
  titleWrap.appendChild(titleEl)
  header.appendChild(titleWrap)

  const capacityEl = div('font-size:13px;color:#888')
  header.appendChild(capacityEl)
  root.appendChild(header)

  // ── 刷新 bar ──
  const refreshBarEls = buildRefreshBar()
  root.appendChild(refreshBarEls.root)

  // ── 滾動容器（包住兩個 section）──
  const scrollArea = div([
    'overflow-y:auto',
    'flex:1',
    'min-height:0',
    'display:flex',
    'flex-direction:column',
  ].join(';'))
  root.appendChild(scrollArea)

  // ── 新手池 section ──
  const rookieSec = buildSection('新手池  F / E 階', ROOKIE_BORDER)
  scrollArea.appendChild(rookieSec.root)

  // ── 老手池 section ──
  const veteranSec = buildSection('老手池  D ~ S 階', VETERAN_BORDER)
  scrollArea.appendChild(veteranSec.root)

  parent.appendChild(root)

  // ---------------------------------------------------------------------------
  // 渲染邏輯
  // ---------------------------------------------------------------------------

  /** 渲染整個 panel（header + refresh bar + 兩池） */
  function render(): void {
    const guild    = ctx.getGuild()
    const recruit  = ctx.getRecruitment()
    const cap      = ctx.getRosterCap()
    const total    = guild.adventurers.length
    const atCap    = total >= cap
    const gold     = guild.resources.gold
    const rep      = guild.resources.reputation

    // --- header ---
    capacityEl.style.color  = atCap ? '#d08030' : '#888'
    capacityEl.textContent  = `名冊 ${total} / ${cap}${atCap ? '（已滿）' : ''}`

    // --- refresh bar ---
    updateRefreshBar(refreshBarEls, recruit, gold, ctx.onManualRefresh)

    // --- 新手池 ---
    renderPool(
      rookieSec,
      recruit.rookiePool,
      '目前無新手候選（重啟後將自動填充）',
      (c) => buildRookieCard(c, atCap, ctx.onRecruitRookie),
    )

    // --- 老手池 ---
    renderPool(
      veteranSec,
      recruit.veteranPool,
      '目前無老手候選（重啟後將自動填充）',
      (c) => buildVeteranCard(c, gold, rep, atCap, ctx.onRecruitVeteran),
    )
  }

  /**
   * 通用池渲染器：清空 list 後重填卡片或空白訊息。
   * @param sec     目標 section 元素集
   * @param pool    候選者陣列
   * @param emptyMsg 空池提示文字
   * @param buildCard 卡片工廠函式
   */
  function renderPool(
    sec: SectionElements,
    pool: readonly RecruitCandidate[],
    emptyMsg: string,
    buildCard: (c: RecruitCandidate) => HTMLElement,
  ): void {
    sec.countEl.textContent = `(${pool.length})`
    sec.list.innerHTML = ''

    if (pool.length === 0) {
      const emptyEl = div([
        'grid-column:1/-1',
        'padding:16px',
        'text-align:center',
        'color:#555',
        'font-size:13px',
      ].join(';'))
      emptyEl.textContent = emptyMsg
      sec.list.appendChild(emptyEl)
      return
    }

    for (const c of pool) {
      sec.list.appendChild(buildCard(c))
    }
  }

  /** 僅更新刷新 bar（不重繪卡片，用於每秒 tick）*/
  function tickRefreshBar(): void {
    const guild   = ctx.getGuild()
    const recruit = ctx.getRecruitment()
    updateRefreshBar(refreshBarEls, recruit, guild.resources.gold, ctx.onManualRefresh)
  }

  // 初次渲染
  render()

  // 啟動每秒 tick（更新倒數顯示）
  _tickTimer = setInterval(tickRefreshBar, 1000)

  // ---------------------------------------------------------------------------
  // 事件訂閱
  // ---------------------------------------------------------------------------

  const onPoolRefreshed  = () => render()
  const onRecruitSuccess = () => render()
  const onGoldChanged    = () => render()
  const onRepChanged     = () => render()
  const onBuildingUpgraded = () => render()
  const onGuildLevelUp   = () => render()

  eventBus.on('recruit:pool_refreshed', onPoolRefreshed)
  eventBus.on('recruit:success',        onRecruitSuccess)
  eventBus.on('gold:changed',           onGoldChanged)
  eventBus.on('reputation:changed',     onRepChanged)
  eventBus.on('building:upgraded',      onBuildingUpgraded)
  eventBus.on('guild:level_up',         onGuildLevelUp)

  // ---------------------------------------------------------------------------
  // dispose
  // ---------------------------------------------------------------------------

  function dispose(): void {
    // 停止計時器
    if (_tickTimer !== null) {
      clearInterval(_tickTimer)
      _tickTimer = null
    }

    // 取消所有訂閱
    eventBus.off('recruit:pool_refreshed', onPoolRefreshed)
    eventBus.off('recruit:success',        onRecruitSuccess)
    eventBus.off('gold:changed',           onGoldChanged)
    eventBus.off('reputation:changed',     onRepChanged)
    eventBus.off('building:upgraded',      onBuildingUpgraded)
    eventBus.off('guild:level_up',         onGuildLevelUp)

    root.remove()
  }

  return { root, dispose, refresh: render }
}

// ---------------------------------------------------------------------------
// Jam 簡化延後清單
// ---------------------------------------------------------------------------
//
// 以下項目因 Phase 2 Web Jam 範疇限制，暫時延後實作：
//
// 1. 倒數精確度：目前顯示 H:MM:SS；
//    待 F-02 Time System 整合後可改訂閱 'tick:minute' 事件，降低 setInterval 依賴。
//
// 2. 候選卡片 hover tooltip：老手詳細能力說明（成功率加成等）尚未顯示，
//    待 C-03/C-04 詳細說明 tooltip 元件整合後補入。
//
// 3. 招募動畫 / 音效：候選消失後無視覺 feedback，
//    待 P-03 通知系統整合後補入 toast 提示。
//
// 4. 老手池超過螢幕高度時的折疊/分頁：
//    RECRUIT_POOL_SIZE = 4，目前每池最多 4 張，不需折疊；
//    若未來 RECRUIT_POOL_SIZE 增大需補加 collapsible 機制。
//
// 5. 聲望上限解鎖提示：老手卡未顯示「再提升 X 聲望即可邀請」的輔助提示，
//    待設計確認 UX 後補入。
