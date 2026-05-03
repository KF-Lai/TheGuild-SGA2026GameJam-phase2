/**
 * FT-07 Guild Building System — Phase 2 Web Jam 版本
 * 設計來源：design/GDD/【FT-07】guild-building-system.md
 *
 * 職責：
 *   - 維護 6 棟建築的等級狀態（委託板/招募廣告欄/公會大廳/公會櫃臺/預備金保險櫃/職員休息室）
 *   - 升級流程：雙軌閘門制（金幣軸 + 聲望軸）
 *   - 效果值查詢 API：BuildingTable 直接查表，不快取
 *   - 升級後 emit 'building:upgraded'；保險櫃升級後推送破產倒數秒數
 *
 * Jam 簡化說明（完整清單見模組底部）：
 *   - F-01 DataManager 不存在 → BuildingTable 直接 hardcode 於模組頂部
 *   - F-03 SetBankruptcyWarningDuration → setBankruptcyWarningHandler callback 注入
 *   - F-02 OnDailyReset → 不訂閱（§3.8 維護費管線 Jam no-op）
 *   - 不需 ISaveable → serialize() / deserialize() 純函式
 */

import { eventBus } from '../core/events'

// ── 公開型別定義 ──────────────────────────────────────────────────────────────

/**
 * BuildingTable 單筆升級資料（對應 GDD §7.1 schema）。
 */
export interface BuildingUpgradeRow {
  buildingID: number
  level: number
  effectValue: number
  upgradeCost: number
  guildLevelReq: number
  slotCount: number
  /** Jam 版不使用，保留 schema 完整性（AC-21）*/
  maintenanceCost: number
}

/**
 * 單棟建築的 meta 資訊（名稱、上限、升級資料表）。
 */
export interface BuildingMeta {
  buildingID: number
  name: string
  maxLevel: number
  /** 各等級升級資料；index = level（職員休息室含 level=0 行）*/
  upgradeData: Record<number, BuildingUpgradeRow>
}

/**
 * 單棟建築的 runtime 狀態。
 */
export interface BuildingState {
  buildingID: number
  currentLevel: number
}

/**
 * tryUpgradeBuilding / canUpgrade 回傳的結果碼。
 */
export type UpgradeResult =
  | 'SUCCESS'
  | 'ALREADY_MAX'
  | 'GUILD_LEVEL_INSUFFICIENT'
  | 'GOLD_INSUFFICIENT'
  | 'BUILDING_NOT_FOUND'

/**
 * F-03 Resource 操作集合（callback 注入式）。
 */
export interface BuildingResourceDeps {
  getGold: () => number
  /** 回傳 true 表示調整成功；false 表示拒絕（不足等） */
  addGold: (delta: number) => boolean
  getGuildLevel: () => number
}

/**
 * serialize() 的輸出型別。
 */
export interface SerializedBuildingStates {
  states: Array<{ buildingID: number; currentLevel: number }>
}

// ── BuildingTable hardcode（GDD §7.1 完整 28 行）────────────────────────────

/**
 * BuildingTable：6 棟建築的完整等級資料。
 * 資料來源：GDD §7.1 完整預設值。
 * key = buildingID（1~6）。
 */
const BUILDING_TABLE: Record<number, BuildingMeta> = {
  1: {
    buildingID: 1,
    name: '委託板',
    maxLevel: 5,
    upgradeData: {
      1: { buildingID: 1, level: 1, effectValue: 5,  upgradeCost: 0,    guildLevelReq: 0, slotCount: 3, maintenanceCost: 0   },
      2: { buildingID: 1, level: 2, effectValue: 8,  upgradeCost: 150,  guildLevelReq: 0, slotCount: 3, maintenanceCost: 15  },
      3: { buildingID: 1, level: 3, effectValue: 11, upgradeCost: 350,  guildLevelReq: 3, slotCount: 3, maintenanceCost: 30  },
      4: { buildingID: 1, level: 4, effectValue: 14, upgradeCost: 700,  guildLevelReq: 4, slotCount: 3, maintenanceCost: 55  },
      5: { buildingID: 1, level: 5, effectValue: 17, upgradeCost: 1200, guildLevelReq: 5, slotCount: 3, maintenanceCost: 80  },
    },
  },
  2: {
    buildingID: 2,
    name: '招募廣告欄',
    maxLevel: 3,
    upgradeData: {
      1: { buildingID: 2, level: 1, effectValue: 86400, upgradeCost: 0,   guildLevelReq: 0, slotCount: 0, maintenanceCost: 0  },
      2: { buildingID: 2, level: 2, effectValue: 57600, upgradeCost: 250, guildLevelReq: 0, slotCount: 0, maintenanceCost: 20 },
      3: { buildingID: 2, level: 3, effectValue: 28800, upgradeCost: 600, guildLevelReq: 3, slotCount: 0, maintenanceCost: 40 },
    },
  },
  3: {
    buildingID: 3,
    name: '公會大廳',
    maxLevel: 5,
    upgradeData: {
      1: { buildingID: 3, level: 1, effectValue: 10, upgradeCost: 0,    guildLevelReq: 0, slotCount: 0, maintenanceCost: 0   },
      2: { buildingID: 3, level: 2, effectValue: 15, upgradeCost: 400,  guildLevelReq: 0, slotCount: 0, maintenanceCost: 20  },
      3: { buildingID: 3, level: 3, effectValue: 20, upgradeCost: 900,  guildLevelReq: 3, slotCount: 0, maintenanceCost: 45  },
      4: { buildingID: 3, level: 4, effectValue: 25, upgradeCost: 1600, guildLevelReq: 4, slotCount: 0, maintenanceCost: 80  },
      5: { buildingID: 3, level: 5, effectValue: 30, upgradeCost: 2500, guildLevelReq: 5, slotCount: 0, maintenanceCost: 120 },
    },
  },
  4: {
    buildingID: 4,
    name: '公會櫃臺',
    maxLevel: 5,
    upgradeData: {
      1: { buildingID: 4, level: 1, effectValue: 5,  upgradeCost: 0,    guildLevelReq: 0, slotCount: 2, maintenanceCost: 0   },
      2: { buildingID: 4, level: 2, effectValue: 8,  upgradeCost: 250,  guildLevelReq: 0, slotCount: 2, maintenanceCost: 20  },
      3: { buildingID: 4, level: 3, effectValue: 11, upgradeCost: 500,  guildLevelReq: 3, slotCount: 2, maintenanceCost: 40  },
      4: { buildingID: 4, level: 4, effectValue: 14, upgradeCost: 900,  guildLevelReq: 4, slotCount: 2, maintenanceCost: 70  },
      5: { buildingID: 4, level: 5, effectValue: 18, upgradeCost: 1400, guildLevelReq: 5, slotCount: 2, maintenanceCost: 100 },
    },
  },
  5: {
    buildingID: 5,
    name: '預備金保險櫃',
    maxLevel: 5,
    upgradeData: {
      1: { buildingID: 5, level: 1, effectValue: 10800,  upgradeCost: 0,    guildLevelReq: 0, slotCount: 1, maintenanceCost: 0  },
      2: { buildingID: 5, level: 2, effectValue: 21600,  upgradeCost: 200,  guildLevelReq: 0, slotCount: 1, maintenanceCost: 15 },
      3: { buildingID: 5, level: 3, effectValue: 43200,  upgradeCost: 450,  guildLevelReq: 3, slotCount: 1, maintenanceCost: 30 },
      4: { buildingID: 5, level: 4, effectValue: 86400,  upgradeCost: 800,  guildLevelReq: 4, slotCount: 1, maintenanceCost: 50 },
      5: { buildingID: 5, level: 5, effectValue: 172800, upgradeCost: 1500, guildLevelReq: 5, slotCount: 1, maintenanceCost: 75 },
    },
  },
  6: {
    buildingID: 6,
    name: '職員休息室',
    maxLevel: 5,
    upgradeData: {
      0: { buildingID: 6, level: 0, effectValue: 0,     upgradeCost: 0,     guildLevelReq: 0, slotCount: 0, maintenanceCost: 0   },
      1: { buildingID: 6, level: 1, effectValue: 86400, upgradeCost: 500,   guildLevelReq: 0, slotCount: 0, maintenanceCost: 0   },
      2: { buildingID: 6, level: 2, effectValue: 64800, upgradeCost: 1500,  guildLevelReq: 0, slotCount: 0, maintenanceCost: 40  },
      3: { buildingID: 6, level: 3, effectValue: 43200, upgradeCost: 3000,  guildLevelReq: 3, slotCount: 0, maintenanceCost: 90  },
      4: { buildingID: 6, level: 4, effectValue: 28800, upgradeCost: 7000,  guildLevelReq: 4, slotCount: 0, maintenanceCost: 160 },
      5: { buildingID: 6, level: 5, effectValue: 21600, upgradeCost: 15000, guildLevelReq: 5, slotCount: 0, maintenanceCost: 240 },
    },
  },
}

// ── 模組級 deps / handler（注入式）──────────────────────────────────────────

/** F-03 Resource 操作（由 setResourceHandlers / initialize 注入）*/
let _deps: BuildingResourceDeps | null = null

/**
 * F-03 SetBankruptcyWarningDuration 的注入式 callback。
 * 保險櫃升級或系統啟動時推送倒數秒數（GDD §4.4 / §4.5）。
 */
let _bankruptcyWarningHandler: ((seconds: number) => void) | null = null

// ── 輔助函式 ────────────────────────────────────────────────────────────────

/**
 * 在 states 陣列中找到指定 buildingID 的狀態物件。
 * 找不到時回傳 null。
 */
function _findState(states: BuildingState[], buildingID: number): BuildingState | null {
  return states.find((s) => s.buildingID === buildingID) ?? null
}

/**
 * 推送破產倒數秒數至 F-03（若 handler 已注入）。
 * 未注入時輸出提示 log（與 world-danger.ts 同模式）。
 */
function _pushBankruptcyWarning(seconds: number): void {
  if (_bankruptcyWarningHandler) {
    _bankruptcyWarningHandler(seconds)
  } else {
    console.log(`[FT-07] 破產倒數秒數更新 → ${seconds}（F-03 handler 尚未注入）`)
  }
}

// ── 公開 API ─────────────────────────────────────────────────────────────────

/**
 * 建立初始 BuildingState 陣列（新遊戲預設值）。
 * buildingID 1~5 初始 Lv1；buildingID 6（職員休息室）初始 Lv0（未建造）。
 * 對應 GDD §3.1 / §6.5 InitializeAsNewGame。
 */
export function createBuildingStates(): BuildingState[] {
  return [
    { buildingID: 1, currentLevel: 1 },
    { buildingID: 2, currentLevel: 1 },
    { buildingID: 3, currentLevel: 1 },
    { buildingID: 4, currentLevel: 1 },
    { buildingID: 5, currentLevel: 1 },
    { buildingID: 6, currentLevel: 0 },
  ]
}

/**
 * 注入 F-03 Resource handlers（可在 initialize 之前或之後呼叫）。
 * 多次呼叫以最後一次為準。
 */
export function setResourceHandlers(deps: BuildingResourceDeps): void {
  _deps = deps
}

/**
 * 注入 F-03 SetBankruptcyWarningDuration callback。
 * 對應 GDD §4.4 / §6.2.1 主動推送對象。
 *
 * @param handler - 接受秒數並寫入 F-03 的函式
 */
export function setBankruptcyWarningHandler(handler: (seconds: number) => void): void {
  _bankruptcyWarningHandler = handler
}

/**
 * 初始化 Building 系統：注入 deps 並推送保險櫃當前等級的破產倒數秒數。
 * 對應 GDD §4.4 Start() 行為。
 *
 * @param states 建築狀態陣列
 * @param deps F-03 Resource 操作集合
 */
export function initialize(states: BuildingState[], deps: BuildingResourceDeps): void {
  setResourceHandlers(deps)
  // 推送保險櫃當前等級的破產倒數秒數（§4.4）
  _pushBankruptcyWarning(getBankruptcyWarningSeconds(states))
}

/**
 * 取得指定建築的當前等級。
 * 找不到 buildingID 時回傳 -1 並輸出錯誤。
 */
export function getBuildingLevel(states: BuildingState[], buildingID: number): number {
  const state = _findState(states, buildingID)
  if (!state) {
    console.error(`[FT-07] getBuildingLevel：找不到 buildingID=${buildingID}`)
    return -1
  }
  return state.currentLevel
}

/**
 * 判斷是否可升級（不扣款，僅回傳判斷結果）。
 * 'SUCCESS' 表示可以升級；其他值為失敗原因。
 * 對應 GDD §4.1 CanUpgrade() 偽碼。
 *
 * @param states 建築狀態陣列
 * @param buildingID 目標建築 ID
 */
export function canUpgrade(states: BuildingState[], buildingID: number): UpgradeResult {
  const meta = BUILDING_TABLE[buildingID]
  if (!meta) {
    console.error(`[FT-07] canUpgrade：BuildingTable 找不到 buildingID=${buildingID}`)
    return 'BUILDING_NOT_FOUND'
  }

  const state = _findState(states, buildingID)
  if (!state) {
    console.error(`[FT-07] canUpgrade：狀態陣列找不到 buildingID=${buildingID}`)
    return 'BUILDING_NOT_FOUND'
  }

  const nextLevel = state.currentLevel + 1
  if (nextLevel > meta.maxLevel) {
    return 'ALREADY_MAX'
  }

  const nextRow = meta.upgradeData[nextLevel]
  if (!nextRow) {
    // 硬編碼資料表理論上不會缺漏，防禦性處理
    console.error(`[FT-07] canUpgrade：BuildingTable 缺少 buildingID=${buildingID} level=${nextLevel} 行`)
    return 'BUILDING_NOT_FOUND'
  }

  if (!_deps) {
    console.error('[FT-07] canUpgrade：ResourceDeps 尚未注入，請先呼叫 setResourceHandlers()')
    return 'GOLD_INSUFFICIENT'
  }

  // 聲望閘（guildLevelReq=0 時恆通過）
  if (_deps.getGuildLevel() < nextRow.guildLevelReq) {
    return 'GUILD_LEVEL_INSUFFICIENT'
  }

  // 金幣閘
  if (_deps.getGold() < nextRow.upgradeCost) {
    return 'GOLD_INSUFFICIENT'
  }

  return 'SUCCESS'
}

/**
 * 嘗試升級指定建築。
 * 通過所有閘門後扣款、更新等級、emit 事件、推送破產倒數（保險櫃專屬）。
 * 對應 GDD §3.3 TryUpgradeBuilding() 偽碼。
 *
 * @param states 建築狀態陣列（直接 mutate）
 * @param buildingID 目標建築 ID
 */
export function tryUpgradeBuilding(states: BuildingState[], buildingID: number): UpgradeResult {
  const result = canUpgrade(states, buildingID)
  if (result !== 'SUCCESS') return result

  // 此時 meta / state / nextRow 必然存在（canUpgrade SUCCESS 已確認）
  const meta = BUILDING_TABLE[buildingID]!
  const state = _findState(states, buildingID)!
  const nextLevel = state.currentLevel + 1
  const nextRow = meta.upgradeData[nextLevel]!

  // 扣款（嚴格不允許負值，主動消費）
  const deducted = _deps!.addGold(-nextRow.upgradeCost)
  if (!deducted) {
    // canUpgrade 通過但 addGold 失敗（重入防護等極端情況）
    console.error(`[FT-07] tryUpgradeBuilding：canUpgrade 通過但 addGold 失敗（buildingID=${buildingID}）`)
    return 'GOLD_INSUFFICIENT'
  }

  const fromLevel = state.currentLevel
  state.currentLevel = nextLevel

  // 發布升級事件（GDD §3.6）
  eventBus.emit('building:upgraded', {
    buildingID,
    fromLevel,
    toLevel: nextLevel,
  })

  // 保險櫃升級後推送新破產倒數秒數（GDD §4.5）
  if (buildingID === 5) {
    _pushBankruptcyWarning(getBankruptcyWarningSeconds(states))
  }

  return 'SUCCESS'
}

// ── 效果值查詢 API（GDD §3.4 / §4.2）────────────────────────────────────────

/**
 * 取得委託板同時開放的委託槽數。
 * buildingID=1，effectValue = 槽數。
 */
export function getMissionSlotCount(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 1)
  return BUILDING_TABLE[1]!.upgradeData[level]?.effectValue ?? 0
}

/**
 * 取得招募候選池自動刷新間隔（秒）。
 * buildingID=2，effectValue = 秒。
 */
export function getRecruitRefreshIntervalSec(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 2)
  return BUILDING_TABLE[2]!.upgradeData[level]?.effectValue ?? 86400
}

/**
 * 取得名冊上限（可容納冒險者數量）。
 * buildingID=3，effectValue = 人數。
 */
export function getRosterCap(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 3)
  return BUILDING_TABLE[3]!.upgradeData[level]?.effectValue ?? 0
}

/**
 * 取得同時可進行的任務上限。
 * buildingID=4，effectValue = 任務數。
 */
export function getMaxConcurrentMissions(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 4)
  return BUILDING_TABLE[4]!.upgradeData[level]?.effectValue ?? 0
}

/**
 * 取得破產觸發後的倒數緩衝時間（秒）。
 * buildingID=5，effectValue = 秒。
 */
export function getBankruptcyWarningSeconds(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 5)
  return BUILDING_TABLE[5]!.upgradeData[level]?.effectValue ?? 10800
}

/**
 * 取得職員休息室的面試自動刷新間隔（秒）。
 * buildingID=6，L0 時回傳 0（職員系統未解鎖）。
 * effectValue 為秒；L0 行 effectValue=0，對應未建造語意。
 */
export function getStaffLoungeRefreshSec(states: BuildingState[]): number {
  const level = getBuildingLevel(states, 6)
  if (level <= 0) return 0
  return BUILDING_TABLE[6]!.upgradeData[level]?.effectValue ?? 0
}

/**
 * 判斷職員系統是否已解鎖（職員休息室 >= Lv1）。
 * buildingID=6 currentLevel >= 1 時回 true。
 * 對應 GDD §4.2 IsStaffSystemUnlocked()。
 */
export function isStaffSystemUnlocked(states: BuildingState[]): boolean {
  return getBuildingLevel(states, 6) >= 1
}

// ── 序列化 / 反序列化（GDD §6.5 ISaveable 簡化版）───────────────────────────

/**
 * 將 BuildingState 陣列序列化為可 JSON.stringify 的純物件。
 * 對應 GDD §6.5 Serialize()。
 */
export function serialize(states: BuildingState[]): SerializedBuildingStates {
  return {
    states: states.map((s) => ({
      buildingID: s.buildingID,
      currentLevel: s.currentLevel,
    })),
  }
}

/**
 * 從序列化資料還原 BuildingState 陣列。
 * 對應 GDD §6.5 RestoreFromSave()。
 *
 * §5.5 clamp 邏輯：currentLevel > maxLevel 時 clamp 至 maxLevel 並輸出 console.warn（不拋例外）。
 * 若序列化資料中缺少某 buildingID，使用 createBuildingStates() 的預設值補齊。
 *
 * @param data SerializedBuildingStates（通常來自 JSON.parse）
 */
export function deserialize(data: SerializedBuildingStates): BuildingState[] {
  // 以預設值為基底，再覆蓋存檔資料
  const defaults = createBuildingStates()
  const result: BuildingState[] = defaults.map((defaultState) => {
    const saved = data.states?.find((s) => s.buildingID === defaultState.buildingID)
    if (!saved) return { ...defaultState }

    const meta = BUILDING_TABLE[saved.buildingID]
    if (!meta) {
      console.error(`[FT-07] deserialize：BuildingTable 找不到 buildingID=${saved.buildingID}，使用預設值`)
      return { ...defaultState }
    }

    // §5.5 clamp 邏輯
    let level = saved.currentLevel
    if (level > meta.maxLevel) {
      console.warn(
        `[FT-07] deserialize：buildingID=${saved.buildingID} currentLevel=${level} 超出 maxLevel=${meta.maxLevel}，clamp 至 ${meta.maxLevel}`
      )
      level = meta.maxLevel
    }

    return { buildingID: saved.buildingID, currentLevel: level }
  })

  return result
}

// ── Jam 簡化延後清單 ─────────────────────────────────────────────────────────
//
// 以下功能因 Phase 2 Web Jam 範疇限制，暫時延後實作：
//
// 1. §3.8 維護費管線（OnGuildMaintenanceDue 事件 + CalculateMaintenanceCosts()）
//    → Jam 版不訂閱 F-02 OnDailyReset；maintenanceCost 欄位僅保留 schema（AC-21）
//
// 2. FT-10 Save/Load ISaveable 正式接入
//    → serialize() / deserialize() 已備妥，待 FT-10 整合時直接使用
//
// 3. P-02 GuildBuildingPanel 整合
//    → canUpgrade() / tryUpgradeBuilding() API 已就緒，UI 直接呼叫即可
//
// 4. FT-01 / FT-02 讀取 API 整合
//    → getRecruitRefreshIntervalSec() / getRosterCap() / getMaxConcurrentMissions()
//       API 已就緒；recruitment.ts 目前仍 hardcode，待整合時替換
//
// 5. slotCount 欄位（FT-12 §3.5 slot 指派）
//    → 欄位已保留在 BuildingUpgradeRow，FT-12 直接讀取；本系統不使用
