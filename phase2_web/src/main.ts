/**
 * main.ts — The Guild Phase 2 Web 整合入口
 *
 * 職責：
 *   1. 建立 / 載入 AppState（新遊戲 or 存檔還原）
 *   2. injectDeps + initializeSystems（Bootstrap 順序）
 *   3. mountUI + subscribeEvents
 *   4. startTickLoop
 *   5. 存檔 / 載入 / 重置 wrapper
 *
 * 設計決策（依 spec §4 / §5）：
 *   - 金流直接模型：outcome.ts 計算 goldDelta（正值=收入，負值=賠款），dispatch 不預收
 *   - Phase 1 UI（shell.ts / guild-hall-scene.ts）完全不引入，避免 ui-strings 缺檔錯誤
 *   - save-load：使用 save-wrapper.ts（獨立 IndexedDB store 'saves_ext'，不動既有 save-load.ts）
 *
 * Jam 延後項目（console.log stub 已標注）：
 *   - F-03 callback：world-danger / building bankruptcy 回呼為 console.log stub
 *   - FT-12 薪水管線：chargeSalary handler 已注入但 jam 不主動觸發
 *   - C-05 behavior trait：getBehaviorTraitHandler 永遠 return 0
 *   - 任務過期退款（missionPool 超時退 baseReward）：tick-loop 尚未實作
 */

import './styles/main.css'

// systems
import * as resource    from './systems/resource'
import * as adventurer  from './systems/adventurer'
import * as worldDanger from './systems/world-danger'
import * as recruitment from './systems/recruitment'
import * as npcDecision from './systems/npc-decision'
import * as building    from './systems/building'
import * as staff       from './systems/staff'
import * as gacha       from './systems/gacha'
import { createOutcomeState } from './systems/outcome'

// data
import { generateMissionPool } from './data/missions'
import { GOLD_INITIAL } from './data/constants'

// main sub-modules
import type { AppState } from './main/types'
import { injectDeps, initializeSystems }  from './main/bootstrap'
import { subscribeEvents }                from './main/event-handlers'
import { mountUI }                        from './main/ui-mount'
import { startTickLoop }                  from './main/tick-loop'
import {
  saveAppState,
  loadAppState,
  deleteAppSave,
}                                         from './main/save-wrapper'

// overlays
import { showGameOverOverlay } from './ui/overlays/game-over-overlay'
import { showSettlementOverlay } from './ui/overlays/settlement-overlay'

// types
import type { GuildState } from './types'
import type { BuildingState as BuildingSystemState } from './systems/building'

// ── 初始任務池大小 ─────────────────────────────────────────────────────────────
const INITIAL_MISSION_POOL_SIZE = 5

// ── 新遊戲 AppState 工廠 ────────────────────────────────────────────────────────

/**
 * 建立全新 AppState（起始 3 冒險者、金幣 200、聲望 0）。
 */
function createInitialAppState(): AppState {
  const resources = resource.createResourceState()
  resource.addGold(resources, GOLD_INITIAL)

  const adventurers = adventurer.createStartingAdventurers()

  // 初始任務池
  const missionPool = generateMissionPool(
    INITIAL_MISSION_POOL_SIZE,
    new Set<string>(),
    new Set<string>(),
    'E',
    0,
  )
  for (const m of missionPool) {
    m.postedAt = Date.now()
  }

  // systems/building.ts 的 BuildingState（buildingID / currentLevel）
  const buildingStates: BuildingSystemState[] = building.createBuildingStates()

  const guildState: GuildState = {
    resources,
    adventurers,
    missionPool,
    pendingReview: [],
    activeMissions: [],
    completedMissionIds: new Set<string>(),
    guildLevel: 1,
    worldDanger: 'E',
    dangerLevel: 0,
    staffRoster: [],
    // GuildState.buildings 使用 types/index.ts 的 BuildingState（buildingId/level）
    // 此處以空陣列佔位，building system 的狀態存在 AppState.buildings
    buildings: [],
  }

  const autoPickup = npcDecision.createAutoPickupTracker()
  // 標記起始冒險者為 idle（autoPickup 追蹤初始化）
  for (const adv of adventurers) {
    npcDecision.markIdle(autoPickup, adv.id)
  }

  return {
    guild:        guildState,
    outcomeState: createOutcomeState(),
    worldDanger:  worldDanger.createWorldDangerState(),
    recruitment:  recruitment.createRecruitmentState(),
    buildings:    buildingStates,
    staffRoster:  staff.createStaffRosterState(),
    gacha:        gacha.createGachaState(),
    autoPickup,
    bankruptcyWarningStart: null,
    totalAdventurersHired:  adventurers.length,
    totalAdventurersDead:   0,
    totalMissionsCompleted: 0,
    gameStartMs:            Date.now(),
  }
}

// ── 程式入口 ────────────────────────────────────────────────────────────────────

async function main(): Promise<void> {
  const app = document.getElementById('app')
  if (!app) {
    console.error('[main] 找不到 #app 元素')
    return
  }

  // 顯示載入畫面（極簡）
  app.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100vh;color:#c8b98a;font-family:monospace;font-size:18px;">The Guild — 載入中…</div>'
  app.style.background = '#111111'

  // 嘗試從 slot0 載入存檔；失敗則新遊戲
  let state: AppState | null = null
  try {
    state = await loadAppState('slot0')
  } catch (err) {
    console.warn('[main] 載入存檔失敗，改為新遊戲', err)
  }

  if (!state) {
    state = createInitialAppState()
  }

  // Bootstrap：注入 deps + 初始化各 system
  injectDeps(state)
  initializeSystems(state)

  // 讓 TypeScript 確定 state 非 null（後續閉包使用）
  const s = state

  // ── 存檔操作函式 ────────────────────────────────────────────────────────────
  async function onSave(slot: string): Promise<void> {
    await saveAppState(s, slot)
  }

  async function onLoad(slot: string): Promise<boolean> {
    try {
      const loaded = await loadAppState(slot)
      if (!loaded) return false
      // 重新套用 loaded state 到 s（mutation in-place 確保閉包引用一致）
      Object.assign(s, loaded)
      injectDeps(s)
      initializeSystems(s)
      // 重新掛載 UI
      uiResult.dispose()
      void remountUI()
      return true
    } catch (err) {
      console.error('[main] onLoad 失敗', err)
      return false
    }
  }

  async function onDelete(slot: string): Promise<void> {
    await deleteAppSave(slot)
  }

  async function onReset(): Promise<void> {
    // 刪除所有 slot 存檔
    await Promise.allSettled(['slot0', 'slot1', 'slot2'].map(sl => deleteAppSave(sl)))
    // 重置為新遊戲
    const fresh = createInitialAppState()
    Object.assign(s, fresh)
    injectDeps(s)
    initializeSystems(s)
    uiResult.dispose()
    void remountUI()
  }

  // ── 掛載 UI ──────────────────────────────────────────────────────────────────

  let stopTickLoop: (() => void) | null = null
  let unsubscribeEvents: (() => void) | null = null
  let uiResult: ReturnType<typeof mountUI>

  function remountUI(): void {
    // 停止舊 tick / 事件訂閱
    if (stopTickLoop)        { stopTickLoop();        stopTickLoop = null }
    if (unsubscribeEvents)   { unsubscribeEvents();   unsubscribeEvents = null }

    uiResult = mountUI(app as HTMLElement, s, { onSave, onLoad, onDelete, onReset })

    // 訂閱 EventBus
    unsubscribeEvents = subscribeEvents(s, uiResult.notifications)

    // 結算彈窗：如有 pendingResults，UI 掛載後立即顯示
    if (s.outcomeState.pendingResults.length > 0) {
      showSettlementOverlay({
        records: [...s.outcomeState.pendingResults],
        onDismissAll: () => {
          s.outcomeState.pendingResults.length = 0
          uiResult.refreshCurrentPanel()
        },
      })
    }

    // 啟動 tick
    stopTickLoop = startTickLoop(s, () => {
      uiResult.refreshHUD()
      uiResult.refreshCurrentPanel()
      // 若有新 pendingResults，顯示結算彈窗
      if (s.outcomeState.pendingResults.length > 0) {
        showSettlementOverlay({
          records: [...s.outcomeState.pendingResults],
          onDismissAll: () => {
            s.outcomeState.pendingResults.length = 0
            uiResult.refreshCurrentPanel()
          },
        })
      }
    }, () => {
      // Game Over 回呼
      if (stopTickLoop) { stopTickLoop(); stopTickLoop = null }
      const daysPlayed = Math.floor((Date.now() - s.gameStartMs) / 86_400_000)
      showGameOverOverlay({
        reason: 'BANKRUPTCY',
        stats: {
          daysPlayed,
          guildLevel:             s.guild.guildLevel,
          reputation:             s.guild.resources.reputation,
          totalAdventurersHired:  s.totalAdventurersHired,
          totalAdventurersDead:   s.totalAdventurersDead,
          totalMissionsCompleted: s.totalMissionsCompleted,
          finalGold:              s.guild.resources.gold,
        },
        onRestart: () => {
          const fresh = createInitialAppState()
          Object.assign(s, fresh)
          injectDeps(s)
          initializeSystems(s)
          uiResult.dispose()
          remountUI()
        },
        onMainMenu: () => {
          // Jam 簡化：重啟即新遊戲（主選單 stub）
          const fresh = createInitialAppState()
          Object.assign(s, fresh)
          injectDeps(s)
          initializeSystems(s)
          uiResult.dispose()
          remountUI()
        },
      })
    })

    // 自動存檔（每 5 分鐘）
    startAutoSave(s)
  }

  // 首次掛載
  remountUI()

  // ── 瀏覽器關閉前自動存檔 ──────────────────────────────────────────────────
  window.addEventListener('beforeunload', () => {
    void saveAppState(s, 'slot0')
  })
}

// ── 自動存檔（5 分鐘）─────────────────────────────────────────────────────────

let _autoSaveTimer: ReturnType<typeof setInterval> | null = null

function startAutoSave(state: AppState): void {
  if (_autoSaveTimer !== null) clearInterval(_autoSaveTimer)
  _autoSaveTimer = setInterval(() => {
    void saveAppState(state, 'slot0').catch(err => {
      console.warn('[main] 自動存檔失敗', err)
    })
  }, 5 * 60_000)
}

// ── 啟動 ──────────────────────────────────────────────────────────────────────

main().catch(err => {
  console.error('[main] 啟動失敗', err)
  const app = document.getElementById('app')
  if (app) {
    app.innerHTML = `<div style="padding:32px;color:#e05050;font-family:monospace;">
      [錯誤] 遊戲啟動失敗：${String(err)}
    </div>`
  }
})
