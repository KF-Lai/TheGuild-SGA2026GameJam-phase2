/**
 * CommissionBoardPanel — Stage 5.1
 *
 * 功能：委託板 panel，Phase 2 升級版。
 *   - successRate preview（由 staff.isSuccessRatePreviewEnabled 控制顯示/隱藏百分比）
 *   - 接單意願分數預覽（previewEffectiveScore，不含 jitter）
 *   - calcRates 顯示 finalSuccessRate / finalDeathRate
 *   - maxDifficulty 鎖定（依 getMaxDifficulty(guildLevel)）
 *   - 進行中任務倒數列表（active missions）
 *   - 訂閱 EventBus 自動 refresh
 *
 * 不修改：shell.ts / guild-hall-scene.ts / systems / types / events.ts / constants
 */

import type { GuildState, Mission, Adventurer, DispatchRecord, Difficulty } from '../../types'
import { eventBus } from '../../core/events'
import { calcRates } from '../../systems/dispatch'
import { previewEffectiveScore } from '../../systems/npc-decision'
import { isSuccessRatePreviewEnabled } from '../../systems/staff'
import type { StaffRosterState } from '../../systems/staff'

// ── 型別 ──────────────────────────────────────────────────────────────────────

export interface CommissionBoardPanelCtx {
  /** 取最新 GuildState（含 missionPool / activeMissions / adventurers / guildLevel） */
  getGuild: () => GuildState
  /** 取 staff roster 用於 isSuccessRatePreviewEnabled 判定 */
  getStaffRoster: () => any
  /** 取最大派遣槽位（由 building.getMaxConcurrentMissions 提供） */
  getMaxConcurrentMissions: () => number
  /** 取最高可接難度（由 guild.getMaxDifficulty 提供） */
  getMaxDifficulty: () => Difficulty
  /** 玩家點「派遣」按鈕：執行派遣（main.ts 串接 tryDispatch + addGold prepay） */
  onDispatch: (adventurerID: string, missionID: string) => void
  /** 玩家點「推薦」按鈕（先讓 NPC 決定意願）：呼叫 makeDecision */
  onRecommend: (adventurerID: string, missionID: string) => void
}

export interface CommissionBoardPanelHandle {
  root: HTMLElement
  /** 解除事件訂閱、移除 DOM */
  dispose: () => void
  /** 強制重繪（可由外部呼叫，事件訂閱亦會自動觸發） */
  refresh: () => void
}

// ── 難度相關常數 ──────────────────────────────────────────────────────────────

/** 難度完整索引順序（含 SS / SSS） */
const DIFFICULTY_ORDER: Difficulty[] = ['F', 'E', 'D', 'C', 'B', 'A', 'S', 'SS', 'SSS']

/** 依難度回傳顏色（與 phase 1 difficultyColor 一致） */
function difficultyColor(diff: Difficulty): string {
  if (diff === 'F' || diff === 'E') return '#888888'
  if (diff === 'D' || diff === 'C') return '#c8b832'
  if (diff === 'B' || diff === 'A') return '#e08020'
  return '#cc3333' // S / SS / SSS
}

/** 難度索引（F=0 … SSS=8） */
function diffIndex(d: Difficulty): number {
  return DIFFICULTY_ORDER.indexOf(d)
}

// ── 樣式常數 ──────────────────────────────────────────────────────────────────

const PANEL_BG    = '#1a1a1a'
const TEXT_MAIN   = '#e8d8a0'
const TEXT_DIM    = '#888888'
const TEXT_LOCKED = '#555555'
const BTN_COLOR   = '#f0c060'
const BTN_BG      = '#3a2a10'
const BTN_HOVER   = '#4a3a18'

// ── 小工具 ────────────────────────────────────────────────────────────────────

/** 格式化百分比（0.75 → '75%'） */
function pct(v: number): string {
  return `${Math.round(v * 100)}%`
}

/** 格式化剩餘毫秒為「X 分鐘」或「< 1 分鐘」 */
function formatRemainingMs(ms: number): string {
  const minutes = Math.ceil(ms / 60_000)
  if (minutes <= 0) return '< 1 分鐘'
  return `${minutes} 分鐘`
}

/** 建立一個 div，套用 cssText */
function el(tag: string, cssText?: string, text?: string): HTMLElement {
  const e = document.createElement(tag)
  if (cssText) e.style.cssText = cssText
  if (text !== undefined) e.textContent = text
  return e
}

// ── Panel 主函式 ──────────────────────────────────────────────────────────────

export function mountCommissionBoardPanel(
  parent: HTMLElement,
  ctx: CommissionBoardPanelCtx,
): CommissionBoardPanelHandle {
  // 根容器
  const root = el('div', [
    `background:${PANEL_BG}`,
    `color:${TEXT_MAIN}`,
    'font-family:monospace,sans-serif',
    'font-size:14px',
    'padding:12px',
    'border-radius:6px',
    'border:1px solid #2a2a2a',
    'display:flex',
    'flex-direction:column',
    'gap:12px',
    'max-height:100%',
    'overflow-y:auto',
  ].join(';'))

  parent.appendChild(root)

  // 目前選中的冒險者（per missionId map）
  const selectedAdventurer: Map<string, string> = new Map()

  // ── 主渲染函式 ────────────────────────────────────────────────────────────

  function refresh(): void {
    root.innerHTML = ''

    const guild         = ctx.getGuild()
    const staffRoster   = ctx.getStaffRoster() as StaffRosterState
    const maxSlots      = ctx.getMaxConcurrentMissions()
    const maxDiff       = ctx.getMaxDifficulty()
    const maxDiffIdx    = diffIndex(maxDiff)
    const activeCount   = guild.activeMissions.length
    const showRates     = isSuccessRatePreviewEnabled(staffRoster)
    const idleAdvs      = guild.adventurers.filter(a => a.status === 'idle')

    // 清理已派遣任務的選中狀態（保持一致）
    for (const [mid] of Array.from(selectedAdventurer)) {
      const missionStillAvail = guild.missionPool.some(m => m.id === mid)
      if (!missionStillAvail) selectedAdventurer.delete(mid)
    }

    root.appendChild(buildHeader(activeCount, maxSlots))
    root.appendChild(buildMissionList(guild, idleAdvs, maxDiffIdx, showRates, activeCount, maxSlots))
    root.appendChild(buildActiveList(guild.activeMissions, guild.missionPool))
  }

  // ── 區塊：header ──────────────────────────────────────────────────────────

  function buildHeader(activeCount: number, maxSlots: number): HTMLElement {
    const header = el('div', [
      'display:flex',
      'align-items:center',
      'gap:8px',
      'border-bottom:1px solid #2a2a2a',
      'padding-bottom:8px',
    ].join(';'))

    // A-02_new 切換：5 分鐘內有新任務則顯示 NEW 變體
    const NEW_THRESHOLD_MS = 5 * 60_000
    const guildSnapshot = ctx.getGuild()
    const hasNewMission = guildSnapshot.missionPool.some(
      (m) => m.postedAt !== undefined && Date.now() - m.postedAt < NEW_THRESHOLD_MS
    )
    const heroIcon = document.createElement('img')
    heroIcon.src = hasNewMission ? '/images/scene/A-02_new.png' : '/images/scene/A-02_idle.png'
    heroIcon.alt = ''
    heroIcon.className = 'hero-icon hero-icon-commission'
    heroIcon.onerror = () => { heroIcon.style.display = 'none' }

    const title = el('div', [
      'font-size:16px',
      'font-weight:bold',
      `color:${TEXT_MAIN}`,
      'letter-spacing:1px',
    ].join(';'), '【 委託板 】')

    const slotBadge = el('div', [
      'margin-left:auto',
      'font-size:13px',
      `color:${activeCount >= maxSlots ? '#cc4444' : TEXT_DIM}`,
    ].join(';'), `派遣中 ${activeCount} / ${maxSlots}`)

    header.appendChild(heroIcon)
    header.appendChild(title)
    header.appendChild(slotBadge)
    return header
  }

  // ── 區塊：委託池列表 ──────────────────────────────────────────────────────

  function buildMissionList(
    guild: GuildState,
    idleAdvs: Adventurer[],
    maxDiffIdx: number,
    showRates: boolean,
    activeCount: number,
    maxSlots: number,
  ): HTMLElement {
    const section = el('div', 'display:flex;flex-direction:column;gap:8px')

    const sectionTitle = el('div', `font-size:13px;color:${TEXT_DIM};font-weight:bold`, '可接委託')
    section.appendChild(sectionTitle)

    if (guild.missionPool.length === 0) {
      section.appendChild(el('div', `color:${TEXT_DIM};opacity:0.6;padding:8px 0`, '目前無委託'))
      return section
    }

    for (const mission of guild.missionPool) {
      const isActive  = guild.activeMissions.some(r => r.missionId === mission.id)
      const isLocked  = diffIndex(mission.difficulty) > maxDiffIdx
      section.appendChild(buildMissionCard(
        mission, isActive, isLocked, idleAdvs, showRates, activeCount, maxSlots, guild,
      ))
    }

    return section
  }

  // ── 委託卡片 ──────────────────────────────────────────────────────────────

  function buildMissionCard(
    mission: Mission,
    isActive: boolean,
    isLocked: boolean,
    idleAdvs: Adventurer[],
    showRates: boolean,
    activeCount: number,
    maxSlots: number,
    _guild: GuildState,
  ): HTMLElement {
    const diffColor = difficultyColor(mission.difficulty)

    const card = el('div', [
      'padding:10px 12px',
      'border:1px solid #2a2a3a',
      'border-radius:6px',
      `background:${isLocked ? '#111111' : '#131318'}`,
      isLocked || isActive ? 'opacity:0.5' : '',
      'position:relative',
    ].filter(Boolean).join(';'))

    // ── 難度標章 + 任務名稱 ──

    const topRow = el('div', 'display:flex;align-items:center;gap:8px;margin-bottom:6px')

    const diffBadge = el('div', [
      'padding:2px 8px',
      'border-radius:3px',
      `background:${diffColor}22`,
      `border:1px solid ${diffColor}`,
      `color:${diffColor}`,
      'font-size:12px',
      'font-weight:bold',
      'letter-spacing:1px',
      'display:flex',
      'align-items:center',
      'gap:4px',
    ].join(';'))
    const rankIconImg = document.createElement('img')
    rankIconImg.src = `/images/icons/rank/F-C-05_${mission.difficulty}.png`
    rankIconImg.alt = mission.difficulty
    rankIconImg.className = 'icon-rank'
    rankIconImg.onerror = () => { rankIconImg.style.display = 'none' }
    const diffText = el('span', '', mission.difficulty)
    diffBadge.appendChild(rankIconImg)
    diffBadge.appendChild(diffText)
    topRow.appendChild(diffBadge)

    if (mission.tier !== 'common') {
      const tierColor = mission.tier === 'gold' ? '#f0c060' : '#aaaaee'
      const tierTag = el('div', [
        'padding:1px 5px',
        'border-radius:3px',
        `background:${tierColor}22`,
        `border:1px solid ${tierColor}`,
        `color:${tierColor}`,
        'font-size:11px',
        'font-weight:bold',
      ].join(';'), mission.tier === 'gold' ? '金' : '銀')
      topRow.appendChild(tierTag)
    }

    if (isLocked) {
      topRow.appendChild(el('div', `font-size:16px;margin-left:auto`, '🔒'))
    }

    card.appendChild(topRow)

    // 任務名稱
    card.appendChild(el('div', [
      'font-size:14px',
      'font-weight:bold',
      `color:${isLocked ? TEXT_LOCKED : '#d8c890'}`,
      'margin-bottom:4px',
    ].join(';'), mission.name))

    // 類型 + 時長 + 報酬
    const metaRow = el('div', 'display:flex;gap:12px;margin-bottom:6px')
    metaRow.appendChild(el('div', `font-size:12px;color:${TEXT_DIM}`, mission.type))
    metaRow.appendChild(el('div', `font-size:12px;color:${TEXT_DIM}`, `${mission.duration} 分鐘`))
    metaRow.appendChild(el('div', 'font-size:13px;color:#f0c060;font-weight:bold', `${mission.baseReward}g`))
    card.appendChild(metaRow)

    // ── 進行中標記 ──

    if (isActive) {
      card.appendChild(el('div', `font-size:13px;color:#4a8a4a;font-style:italic`, '▶ 進行中'))
      return card
    }

    // ── 鎖定標記 ──

    if (isLocked) {
      card.appendChild(el('div', `font-size:12px;color:${TEXT_LOCKED};font-style:italic`, '公會等級不足，無法接受此委託'))
      return card
    }

    // ── 選擇冒險者 dropdown ──

    const dropdownWrap = el('div', 'margin-bottom:6px')
    const label = el('div', `font-size:12px;color:${TEXT_DIM};margin-bottom:3px`, '選擇冒險者')
    dropdownWrap.appendChild(label)

    const select = document.createElement('select')
    select.style.cssText = [
      `background:${PANEL_BG}`,
      `color:${TEXT_MAIN}`,
      'border:1px solid #3a3a3a',
      'border-radius:3px',
      'padding:3px 6px',
      'font-size:13px',
      'width:100%',
      'cursor:pointer',
    ].join(';')

    // 預設提示選項
    const defaultOpt = document.createElement('option')
    defaultOpt.value = ''
    defaultOpt.textContent = idleAdvs.length === 0 ? '無可派遣冒險者' : '--- 選擇冒險者 ---'
    defaultOpt.disabled = idleAdvs.length === 0
    select.appendChild(defaultOpt)

    for (const adv of idleAdvs) {
      const opt = document.createElement('option')
      opt.value = adv.id
      opt.textContent = `${adv.name} [${adv.rank}] ${adv.professionId}`
      if (selectedAdventurer.get(mission.id) === adv.id) opt.selected = true
      select.appendChild(opt)
    }

    if (selectedAdventurer.get(mission.id) && !idleAdvs.some(a => a.id === selectedAdventurer.get(mission.id))) {
      // 之前選的冒險者不再 idle，清除
      selectedAdventurer.delete(mission.id)
    }

    // 讓 select 保持當前選中狀態（若有）
    if (!selectedAdventurer.has(mission.id) && idleAdvs.length > 0) {
      // 不自動選；等使用者選擇
    }

    select.addEventListener('change', () => {
      const advId = select.value
      if (advId) {
        selectedAdventurer.set(mission.id, advId)
      } else {
        selectedAdventurer.delete(mission.id)
      }
      // 即時更新 preview 區（重繪整張卡片成本太高，改局部更新 previewBox）
      updatePreviewBox(previewBox, mission, advId, idleAdvs, showRates)
      updateButtons(dispatchBtn, advId, activeCount, maxSlots)
    })

    dropdownWrap.appendChild(select)
    card.appendChild(dropdownWrap)

    // ── 成功率 / 意願分 preview 區 ──

    const previewBox = el('div', [
      'padding:6px 8px',
      'border-radius:4px',
      'background:#0a0a14',
      'border:1px solid #2a2a3a',
      'font-size:12px',
      'margin-bottom:6px',
      'min-height:40px',
    ].join(';'))

    const currentAdvId = selectedAdventurer.get(mission.id) ?? ''
    updatePreviewBox(previewBox, mission, currentAdvId, idleAdvs, showRates)
    card.appendChild(previewBox)

    // ── 按鈕列 ──

    const btnRow = el('div', 'display:flex;gap:8px')

    // 推薦按鈕
    const recommendBtn = document.createElement('button')
    recommendBtn.style.cssText = buildBtnStyle(BTN_BG, BTN_COLOR, false)
    recommendBtn.textContent = '推薦'
    recommendBtn.title = '讓 NPC 決定是否接受此委託'
    recommendBtn.addEventListener('click', () => {
      const advId = selectedAdventurer.get(mission.id)
      if (!advId) return
      ctx.onRecommend(advId, mission.id)
    })
    recommendBtn.addEventListener('mouseenter', () => {
      recommendBtn.style.background = BTN_HOVER
    })
    recommendBtn.addEventListener('mouseleave', () => {
      recommendBtn.style.background = BTN_BG
    })

    // 派遣按鈕
    const dispatchBtn = document.createElement('button')
    dispatchBtn.style.cssText = buildBtnStyle(BTN_BG, BTN_COLOR, false)
    dispatchBtn.textContent = '派遣'
    dispatchBtn.title = '強制派遣選中冒險者'
    dispatchBtn.addEventListener('click', () => {
      const advId = selectedAdventurer.get(mission.id)
      if (!advId) return
      ctx.onDispatch(advId, mission.id)
    })
    dispatchBtn.addEventListener('mouseenter', () => {
      if (!dispatchBtn.disabled) dispatchBtn.style.background = BTN_HOVER
    })
    dispatchBtn.addEventListener('mouseleave', () => {
      dispatchBtn.style.background = BTN_BG
    })

    // 初始狀態
    const initAdvId = selectedAdventurer.get(mission.id) ?? ''
    recommendBtn.disabled = !initAdvId || idleAdvs.length === 0
    recommendBtn.style.opacity = recommendBtn.disabled ? '0.4' : '1'
    updateButtons(dispatchBtn, initAdvId, activeCount, maxSlots)

    btnRow.appendChild(recommendBtn)
    btnRow.appendChild(dispatchBtn)
    card.appendChild(btnRow)

    return card
  }

  // ── 已派遣任務倒數列表 ─────────────────────────────────────────────────────

  function buildActiveList(
    activeMissions: DispatchRecord[],
    _missionPool: Mission[],
  ): HTMLElement {
    const section = el('div', 'display:flex;flex-direction:column;gap:6px')

    const sectionTitle = el('div', `font-size:13px;color:${TEXT_DIM};font-weight:bold`, '進行中任務')
    section.appendChild(sectionTitle)

    if (activeMissions.length === 0) {
      section.appendChild(el('div', `color:${TEXT_DIM};opacity:0.5;padding:6px 0`, '無進行中任務'))
      return section
    }

    const now = Date.now()

    for (const record of activeMissions) {
      const remainingMs = Math.max(0, record.endTimestamp - now)
      const row = el('div', [
        'display:flex',
        'align-items:center',
        'gap:8px',
        'padding:7px 10px',
        'border:1px solid #1a3a2a',
        'border-radius:4px',
        'background:#0a1a10',
      ].join(';'))

      // 冒險者名
      row.appendChild(el('div', `font-size:13px;color:#8ad8a0;min-width:70px`, record.adventurerName))

      // 箭頭
      row.appendChild(el('div', `color:${TEXT_DIM}`, '→'))

      // 任務名
      row.appendChild(el('div', `font-size:13px;color:${TEXT_MAIN};flex:1`, record.missionName))

      // 倒數
      row.appendChild(el('div', `font-size:12px;color:${TEXT_DIM};margin-left:auto;white-space:nowrap`,
        remainingMs === 0 ? '結算中…' : `剩餘 ${formatRemainingMs(remainingMs)}`))

      // 成功率
      row.appendChild(el('div', `font-size:12px;color:#6a9a6a;white-space:nowrap`,
        `${pct(record.finalSuccessRate)}`))

      section.appendChild(row)
    }

    return section
  }

  // ── Preview box 局部更新 ──────────────────────────────────────────────────

  function updatePreviewBox(
    box: HTMLElement,
    mission: Mission,
    advId: string,
    idleAdvs: Adventurer[],
    showRates: boolean,
  ): void {
    box.innerHTML = ''

    if (!advId) {
      box.appendChild(el('div', `color:${TEXT_DIM};font-style:italic`, '選擇冒險者後顯示預估數值'))
      return
    }

    const adv = idleAdvs.find(a => a.id === advId)
    if (!adv) {
      box.appendChild(el('div', `color:${TEXT_DIM};font-style:italic`, '冒險者資料讀取失敗'))
      return
    }

    const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)
    const willingnessScore = previewEffectiveScore(adv, mission)

    const ratesRow = el('div', 'display:flex;gap:12px;margin-bottom:3px')

    // 成功率（依 flag 決定顯示數字或「？」）
    ratesRow.appendChild(el('div', `color:#6ab86a`, `成功率：${showRates ? pct(finalSuccessRate) : '？'}`))
    // 死亡率（依 flag 決定顯示數字或「？」）
    ratesRow.appendChild(el('div', `color:#c85050`, `死亡率：${showRates ? pct(finalDeathRate) : '？'}`))
    box.appendChild(ratesRow)

    // 接單意願分（always show，顯示未含 jitter 的分數）
    const willingnessColor = willingnessScore >= 0.3
      ? '#8ab8ea'
      : willingnessScore >= 0
        ? '#c8c850'
        : '#c87050'

    box.appendChild(el('div', `color:${willingnessColor}`,
      `意願分數：${willingnessScore.toFixed(2)}（不含 jitter）`))
  }

  // ── 按鈕局部更新 ──────────────────────────────────────────────────────────

  function updateButtons(
    btn: HTMLButtonElement,
    advId: string,
    activeCount: number,
    maxSlots: number,
  ): void {
    const canDispatch = !!advId && activeCount < maxSlots
    btn.disabled = !canDispatch
    btn.style.opacity = canDispatch ? '1' : '0.4'
    btn.style.cursor = canDispatch ? 'pointer' : 'not-allowed'
  }

  // ── 按鈕樣式 ──────────────────────────────────────────────────────────────

  function buildBtnStyle(bg: string, color: string, _disabled: boolean): string {
    return [
      `background:${bg}`,
      `color:${color}`,
      'border:1px solid #6a5020',
      'border-radius:4px',
      'padding:5px 14px',
      'font-size:13px',
      'cursor:pointer',
      'flex:1',
      'transition:background 0.15s',
    ].join(';')
  }

  // ── 事件訂閱 ──────────────────────────────────────────────────────────────

  const onRefresh = () => refresh()

  eventBus.on('mission:completed',    onRefresh)
  eventBus.on('commission:settled',   onRefresh)
  eventBus.on('gold:changed',         onRefresh)
  eventBus.on('reputation:changed',   onRefresh)
  eventBus.on('guild:level_up',       onRefresh)
  eventBus.on('building:upgraded',    onRefresh)
  eventBus.on('staff:assigned',       onRefresh)
  eventBus.on('staff:hired',          onRefresh)
  eventBus.on('staff:fired',          onRefresh)

  // ── 初始渲染 ──────────────────────────────────────────────────────────────

  refresh()

  // ── dispose ───────────────────────────────────────────────────────────────

  function dispose(): void {
    eventBus.off('mission:completed',    onRefresh)
    eventBus.off('commission:settled',   onRefresh)
    eventBus.off('gold:changed',         onRefresh)
    eventBus.off('reputation:changed',   onRefresh)
    eventBus.off('guild:level_up',       onRefresh)
    eventBus.off('building:upgraded',    onRefresh)
    eventBus.off('staff:assigned',       onRefresh)
    eventBus.off('staff:hired',          onRefresh)
    eventBus.off('staff:fired',          onRefresh)

    if (root.parentElement) {
      root.parentElement.removeChild(root)
    }
  }

  return { root, dispose, refresh }
}
