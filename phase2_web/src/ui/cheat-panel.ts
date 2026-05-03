/**
 * ui/cheat-panel.ts — Phase 2 金手指 Panel
 *
 * 取代 phase 1 cheat-menu.ts（該檔案視為 dead code，不修改）。
 * 五個區塊共 22 個按鈕。
 * 樣式全部 inline style（jam 範疇）；橘色 #ff6600 邊框風格沿用 phase 1。
 */

import type { CheatActions } from '../main/cheat-actions'

// ── 公開介面 ──────────────────────────────────────────────────────────────────

export interface CheatPanelHandle {
  /** 浮動 panel 的根元素（已掛入 DOM） */
  root: HTMLElement
  /** 移除 DOM 元素並解除所有事件監聽 */
  dispose: () => void
}

// ── 常數 ─────────────────────────────────────────────────────────────────────

const ORANGE  = '#ff6600'
const DARK_BG = '#1a1a1a'
const GOLD    = '#c8b98a'
const BORDER  = '#555'
const TITLE_BORDER = '#553300'

// ── 輔助：建立 cheat 按鈕 ────────────────────────────────────────────────────

function makeBtn(label: string, onClick: () => void): HTMLButtonElement {
  const btn = document.createElement('button')
  btn.style.cssText = [
    'padding:4px 6px',
    'background:transparent',
    `color:${GOLD}`,
    `border:1px solid ${BORDER}`,
    "font-family:'Courier New',monospace",
    'font-size:12px',
    'cursor:pointer',
    'text-align:left',
  ].join(';')
  btn.textContent = label
  btn.addEventListener('click', onClick)
  btn.addEventListener('mouseenter', () => {
    btn.style.borderColor = ORANGE
    btn.style.color = '#ff9933'
  })
  btn.addEventListener('mouseleave', () => {
    // active（已按下 toggle）時保持橘色，其他恢復預設
    if (!btn.dataset.active) {
      btn.style.borderColor = BORDER
      btn.style.color = GOLD
    }
  })
  return btn
}

/** 建立區塊標題列 */
function makeSection(title: string): HTMLElement {
  const el = document.createElement('div')
  el.style.cssText = [
    `color:${ORANGE}`,
    'font-size:11px',
    'margin:8px 0 4px',
    `border-bottom:1px solid ${TITLE_BORDER}`,
    'padding-bottom:2px',
    "font-family:'Courier New',monospace",
  ].join(';')
  el.textContent = title
  return el
}

/** 建立 2-col grid 容器 */
function makeGrid(): HTMLElement {
  const grid = document.createElement('div')
  grid.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:4px'
  return grid
}

// ── 主要 mount 函式 ────────────────────────────────────────────────────────────

/**
 * 掛載金手指 panel 至 parent 元素。
 *
 * @param parent  掛載點（通常是 top-bar 的 wrapper）
 * @param actions CheatActions 集合（由 createCheatActions 產生）
 */
export function mountCheatPanel(
  parent: HTMLElement,
  actions: CheatActions,
): CheatPanelHandle {
  // ── 浮動 panel 容器 ──────────────────────────────────────────────────────
  const root = document.createElement('div')
  root.style.cssText = [
    'position:absolute',
    'top:calc(100% + 4px)',
    'right:0',
    `background:${DARK_BG}`,
    `border:1px solid ${ORANGE}`,
    'padding:8px 12px 12px',
    'z-index:200',
    'width:320px',
    'max-height:70vh',
    'overflow-y:auto',
    'box-sizing:border-box',
  ].join(';')

  // ── 區塊 1：金幣 / 聲望（8 個按鈕）──────────────────────────────────────
  root.appendChild(makeSection('區塊 1：金幣 / 聲望'))
  const grid1 = makeGrid()
  grid1.appendChild(makeBtn('+100 金',       () => actions.addGold(100)))
  grid1.appendChild(makeBtn('+1000 金',      () => actions.addGold(1000)))
  grid1.appendChild(makeBtn('+10000 金',     () => actions.addGold(10000)))
  grid1.appendChild(makeBtn('-100 金',       () => actions.addGold(-100)))
  grid1.appendChild(makeBtn('-1000 金',      () => actions.addGold(-1000)))
  grid1.appendChild(makeBtn('+10 聲望',      () => actions.changeReputation(10)))
  grid1.appendChild(makeBtn('-10 聲望',      () => actions.changeReputation(-10)))
  grid1.appendChild(makeBtn('聲望直推 Lv5', () => actions.setReputationToLv5()))
  root.appendChild(grid1)

  // ── 區塊 2：委託 / 任務（5 個按鈕）──────────────────────────────────────
  root.appendChild(makeSection('區塊 2：委託 / 任務'))
  const grid2 = makeGrid()
  grid2.appendChild(makeBtn('刷新委託池',    () => actions.refreshMissionPool()))
  grid2.appendChild(makeBtn('強制全部成功', () => actions.completeAllMissions()))
  grid2.appendChild(makeBtn('強制全部失敗', () => actions.failAllMissions()))
  grid2.appendChild(makeBtn('推進 30 分鐘', () => actions.advanceTime(30)))
  grid2.appendChild(makeBtn('推進 1 小時',  () => actions.advanceTime(60)))
  grid2.appendChild(makeBtn('推進 5 小時',  () => actions.advanceTime(300)))
  root.appendChild(grid2)

  // ── 區塊 3：冒險者 / 招募（4 個按鈕）────────────────────────────────────
  root.appendChild(makeSection('區塊 3：冒險者 / 招募'))
  const grid3 = makeGrid()
  grid3.appendChild(makeBtn('刷新招募池',      () => actions.refreshRecruitPools()))
  grid3.appendChild(makeBtn('給予 5 名 D 階', () => actions.giveAdventurers(5, 'D')))
  grid3.appendChild(makeBtn('給予 5 名 S 階', () => actions.giveAdventurers(5, 'S')))

  // forceAccept toggle 按鈕（需要動態更新標籤）
  const forceAcceptBtn = makeBtn(
    `100%接受 [${actions.isForceAcceptEnabled() ? '開' : '關'}]`,
    () => {
      const next = !actions.isForceAcceptEnabled()
      actions.toggleForceAccept(next)
      forceAcceptBtn.textContent = `100%接受 [${next ? '開' : '關'}]`
      if (next) {
        forceAcceptBtn.dataset.active = '1'
        forceAcceptBtn.style.borderColor = ORANGE
        forceAcceptBtn.style.color = '#ff9933'
      } else {
        delete forceAcceptBtn.dataset.active
        forceAcceptBtn.style.borderColor = BORDER
        forceAcceptBtn.style.color = GOLD
      }
    },
  )
  grid3.appendChild(forceAcceptBtn)
  root.appendChild(grid3)

  // ── 區塊 4：建築 / 職員（5 個按鈕）──────────────────────────────────────
  root.appendChild(makeSection('區塊 4：建築 / 職員'))
  const grid4 = makeGrid()
  grid4.appendChild(makeBtn('全建築升 Max',  () => actions.upgradeAllBuildings()))
  grid4.appendChild(makeBtn('解鎖職員系統', () => actions.unlockStaffSystem()))
  grid4.appendChild(makeBtn('直接錄用三職員',() => actions.hireAllStaff()))
  grid4.appendChild(makeBtn('解雇所有職員', () => actions.fireAllStaff()))
  grid4.appendChild(makeBtn('刷新面試池',   () => actions.refreshGachaPool()))
  root.appendChild(grid4)

  // ── 區塊 5：世界 / Game Over 測試（3 個按鈕）────────────────────────────
  root.appendChild(makeSection('區塊 5：世界 / Game Over'))
  const grid5 = makeGrid()
  grid5.appendChild(makeBtn('危險度 +1',   () => actions.increaseDanger()))
  grid5.appendChild(makeBtn('危險度 -1',   () => actions.decreaseDanger()))
  grid5.appendChild(makeBtn('模擬破產',    () => actions.simulateBankruptcy()))
  root.appendChild(grid5)

  // ── 掛入 DOM ─────────────────────────────────────────────────────────────
  parent.appendChild(root)

  return {
    root,
    dispose: () => {
      if (root.parentElement) {
        root.parentElement.removeChild(root)
      }
    },
  }
}
