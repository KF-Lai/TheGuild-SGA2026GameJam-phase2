/**
 * FT-01 Adventurer Recruitment System — Phase 2 Web Jam 版本
 * 設計來源：design/GDD/【FT-01】adventurer-recruitment.md
 *
 * 職責：
 *   - 維護新手池（F/E 階）與老手池（D~S 階）
 *   - 自動刷新（固定 24h，hardcode）與手動刷新（每日 1 次免費）
 *   - 新手免費接納；老手需金幣＋聲望門檻驗證
 *   - 透過 callback 注入 F-03 Resource 相關操作
 *   - emit 事件：'recruit:pool_refreshed' / 'recruit:success'
 *
 * Jam 簡化說明（完整清單見模組底部）：
 *   - F-01 DataManager 不存在 → 常數全部 hardcode
 *   - F-02 Time System 不存在 → 用 Date.now() 直接取時間
 *   - FT-07 Guild Building 不存在 → 刷新間隔固定 24h
 *   - FT-12 Staff 不存在 → recruitRefreshReductionSec = 0
 *   - C-05 Trait 暫無 → growthTraits 使用預設 [] (createAdventurer 已處理)
 *   - AdventurerTemplate / isUnique 過濾邏輯延後
 */

import type { Adventurer, Rank, GuildLevel } from '../types'
import { createAdventurer } from './adventurer'
import { getMaxDifficulty } from './guild'
import { eventBus } from '../core/events'
import {
  RECRUIT_POOL_SIZE,
  DAILY_FREE_REFRESH,
  REFRESH_COST,
} from '../data/constants'

// ── 模組常數（Jam hardcode）────────────────────────────────────────────────────

/** 自動刷新間隔下限（秒），防止刷新近乎即時（GDD §7.1）。 */
const MIN_RECRUIT_REFRESH_INTERVAL_SEC = 3600

/** 基礎自動刷新間隔（毫秒）：24h。FT-07 不存在故 hardcode（GDD §7.4 L1 = 86400s）。 */
const BASE_AUTO_REFRESH_INTERVAL_MS = 24 * 3600 * 1000

/** FT-12 未實作，刷新縮短秒數固定 0（GDD §4.1）。 */
const RECRUIT_REFRESH_REDUCTION_SEC = 0

// ── 老手招募費用表（GDD §7.2 RecruitCostTable）───────────────────────────────

interface RecruitCostRow {
  cost: number
  reputationReq: number
}

/**
 * 老手各階級的招募費用與聲望門檻。
 * 資料來源：GDD §7.2 預設值。
 */
const RECRUIT_COST_TABLE: Partial<Record<Rank, RecruitCostRow>> = {
  D: { cost: 100,  reputationReq: 0  },
  C: { cost: 300,  reputationReq: 20 },
  B: { cost: 700,  reputationReq: 40 },
  A: { cost: 1500, reputationReq: 60 },
  S: { cost: 2000, reputationReq: 80 },
}

// ── 老手階級加權表（GDD §3.4 / §4.4）────────────────────────────────────────

interface VeteranRankWeight {
  rank: Rank
  weight: number
}

/**
 * 老手候選者階級加權隨機表。
 * D:40 / C:30 / B:18 / A:9 / S:3（GDD §3.4 §7.3）。
 */
const VETERAN_RANK_WEIGHTS: VeteranRankWeight[] = [
  { rank: 'D', weight: 40 },
  { rank: 'C', weight: 30 },
  { rank: 'B', weight: 18 },
  { rank: 'A', weight: 9  },
  { rank: 'S', weight: 3  },
]

// ── 階級索引輔助（GDD §4.4 RANK_INDEX）──────────────────────────────────────

const RANK_INDEX: Record<string, number> = {
  F: 0, E: 1, D: 2, C: 3, B: 4, A: 5, S: 6, SS: 7, SSS: 8,
}

// ── 型別定義 ────────────────────────────────────────────────────────────────

/**
 * 候選池內的招募候選者。
 * candidateID 為候選池內唯一 ID，每次刷新重新分配。
 */
export interface RecruitCandidate {
  /** 候選池內唯一 ID（自增，刷新後重置）。 */
  candidateID: number
  /** 預生成的冒險者實例（尚未加入名冊）。 */
  adventurer: Adventurer
  /** 招募費用；新手固定 0，老手依 RecruitCostTable。 */
  cost: number
  /** 聲望門檻；新手固定 0，老手依 RecruitCostTable。 */
  reputationReq: number
}

/**
 * FT-01 Recruitment 系統的完整可序列化狀態。
 */
export interface RecruitmentState {
  /** 新手候選池（F/E 階）。 */
  rookiePool: RecruitCandidate[]
  /** 老手候選池（D~S 階）。 */
  veteranPool: RecruitCandidate[]
  /** 最近一次刷新的 UTC 毫秒時間戳記。 */
  lastRefreshTimestamp: number
  /** 本週期剩餘免費手動刷新次數。 */
  freeRefreshRemaining: number
  /** 最近一次每日重置的 UTC 毫秒時間戳記（用來判斷是否該重置免費次數）。 */
  lastDailyResetTimestamp: number
  /** 候選者 ID 自增計數（跨刷新累加）。 */
  nextCandidateId: number
}

/**
 * 序列化格式（存檔用）。
 * 與 RecruitmentState 欄位一致；獨立型別方便未來版本升遷。
 */
export interface SerializedRecruitmentState {
  rookiePool: RecruitCandidate[]
  veteranPool: RecruitCandidate[]
  lastRefreshTimestamp: number
  freeRefreshRemaining: number
  lastDailyResetTimestamp: number
  nextCandidateId: number
}

// ── F-03 Resource callback 介面 ──────────────────────────────────────────────

/**
 * 注入的 F-03 Resource 操作集合。
 * 呼叫端負責傳入對應 ResourceState 的閉包。
 */
export interface ResourceDeps {
  /** 判斷目前金幣是否足夠支付指定費用。 */
  canAfford: (amount: number) => boolean
  /**
   * 調整金幣；amount 為正時增加，為負時減少。
   * 回傳 true 表示操作成功；false 表示失敗（例如不足時呼叫端拒絕）。
   */
  addGold: (amount: number) => boolean
  /** 取得目前聲望值。 */
  getReputation: () => number
}

// ── 模組級 deps（由 setResourceHandlers 注入）──────────────────────────────

let _deps: ResourceDeps | null = null

// ── 內部工具函式 ────────────────────────────────────────────────────────────

/**
 * 依加權表進行加權隨機抽取，回傳被選中的 rank。
 * 權重不需正規化；使用前已依公會等級過濾。
 */
function weightedRandom(table: VeteranRankWeight[]): Rank {
  const total = table.reduce((sum, entry) => sum + entry.weight, 0)
  let roll = Math.random() * total
  for (const entry of table) {
    roll -= entry.weight
    if (roll <= 0) return entry.rank
  }
  // 浮點偏差防護：回傳最後一項
  return table[table.length - 1].rank
}

/**
 * 依公會等級計算老手可招募的最高階級（最高 S）。
 * getMaxDifficulty 可能回傳 SS/SSS，但老手池上限為 S。
 */
function getVeteranMaxRank(guildLevel: GuildLevel): Rank {
  const maxDiff = getMaxDifficulty(guildLevel)
  const maxDiffIndex = RANK_INDEX[maxDiff] ?? 8
  // 老手池最高 S（index 6）
  const cappedIndex = Math.min(maxDiffIndex, RANK_INDEX['S'])
  // 從 RANK_INDEX 反查 Rank 字串
  const rankEntry = Object.entries(RANK_INDEX).find(([, idx]) => idx === cappedIndex)
  return (rankEntry ? rankEntry[0] : 'S') as Rank
}

/**
 * 隨機抽取老手階級，受公會等級限制。
 * 超出上限的階級權重歸零後重新加權隨機（GDD §4.4）。
 */
function rollVeteranRank(guildLevel: GuildLevel): Rank {
  const maxRank = getVeteranMaxRank(guildLevel)
  const maxIndex = RANK_INDEX[maxRank]

  const filtered = VETERAN_RANK_WEIGHTS.filter(
    (entry) => RANK_INDEX[entry.rank] <= maxIndex,
  )

  if (filtered.length === 0) {
    // 防禦性 fallback：理論上公會 Lv1 時 D 階不會被過濾
    console.error('FT-01 rollVeteranRank: 所有老手階級被過濾，fallback to D')
    return 'D'
  }

  return weightedRandom(filtered)
}

/**
 * 從 RecruitCostTable 取得指定階級的費用與聲望門檻。
 * F/E 階（新手）不在表中，回傳 { cost: 0, reputationReq: 0 }。
 */
function lookupCost(rank: Rank): RecruitCostRow {
  return RECRUIT_COST_TABLE[rank] ?? { cost: 0, reputationReq: 0 }
}

/**
 * 產生一批新手候選者（F/E 階，50/50）。
 * @param count 要生成的候選者數量
 * @param startId 起始 candidateID
 */
function generateRookieCandidates(
  count: number,
  startId: number,
): { candidates: RecruitCandidate[]; nextId: number } {
  const candidates: RecruitCandidate[] = []
  let id = startId
  for (let i = 0; i < count; i++) {
    const rank: Rank = Math.random() < 0.5 ? 'F' : 'E'
    const adv = createAdventurer(rank)
    candidates.push({
      candidateID: id++,
      adventurer: adv,
      cost: 0,
      reputationReq: 0,
    })
  }
  return { candidates, nextId: id }
}

/**
 * 產生一批老手候選者（D~S 階，加權隨機）。
 * @param count 要生成的候選者數量
 * @param startId 起始 candidateID
 * @param guildLevel 公會等級（決定老手池階級上限）
 */
function generateVeteranCandidates(
  count: number,
  startId: number,
  guildLevel: GuildLevel,
): { candidates: RecruitCandidate[]; nextId: number } {
  const candidates: RecruitCandidate[] = []
  let id = startId
  for (let i = 0; i < count; i++) {
    const rank = rollVeteranRank(guildLevel)
    const adv = createAdventurer(rank)
    const { cost, reputationReq } = lookupCost(rank)
    candidates.push({
      candidateID: id++,
      adventurer: adv,
      cost,
      reputationReq,
    })
  }
  return { candidates, nextId: id }
}

/**
 * 執行一次池刷新：清空兩個池並重新生成候選者。
 * 更新 lastRefreshTimestamp，並 emit 'recruit:pool_refreshed'。
 *
 * @param state RecruitmentState（直接 mutate）
 * @param guildLevel 公會等級（決定老手池上限）
 * @param source 刷新來源，用於 event payload
 */
function executeRefresh(
  state: RecruitmentState,
  guildLevel: GuildLevel,
  source: 'auto' | 'manual_free' | 'manual_paid',
): void {
  const now = Date.now()

  // 刷新新手池
  const rookieResult = generateRookieCandidates(RECRUIT_POOL_SIZE, state.nextCandidateId)
  state.rookiePool = rookieResult.candidates
  state.nextCandidateId = rookieResult.nextId

  // 刷新老手池
  const veteranResult = generateVeteranCandidates(
    RECRUIT_POOL_SIZE,
    state.nextCandidateId,
    guildLevel,
  )
  state.veteranPool = veteranResult.candidates
  state.nextCandidateId = veteranResult.nextId

  // 更新刷新時間戳
  state.lastRefreshTimestamp = now

  // 發布池刷新事件
  eventBus.emit('recruit:pool_refreshed', { source })
}

/**
 * 計算目前有效的自動刷新間隔（毫秒）。
 * FT-12 不存在，reductionSec = 0；FT-07 不存在，baseSec = 86400。
 * 套用 MIN_RECRUIT_REFRESH_INTERVAL_SEC 下限（GDD §4.1）。
 */
function calcAutoRefreshIntervalMs(): number {
  const baseSec = BASE_AUTO_REFRESH_INTERVAL_MS / 1000
  const intervalSec = Math.max(baseSec - RECRUIT_REFRESH_REDUCTION_SEC, MIN_RECRUIT_REFRESH_INTERVAL_SEC)
  return intervalSec * 1000
}

/**
 * 判斷並執行跨日免費刷新次數重置（GDD §4.3）。
 *
 * 用 UTC 日序號（Math.floor(ts / 86400000)）比較，避免 getUTCDate() 在
 * 跨月同日（4/30→5/30）或跨年同月日誤判沒跨日的問題。
 * 不同時視為跨日，重置 freeRefreshRemaining 並更新時間戳。
 *
 * lastDailyResetTimestamp 為 0 時（新遊戲首次）一律觸發重置與時間戳寫入。
 */
function checkDailyReset(state: RecruitmentState): void {
  const now = Date.now()
  const MS_PER_DAY = 86_400_000
  const currentDayIndex = Math.floor(now / MS_PER_DAY)
  const lastResetDayIndex = Math.floor(state.lastDailyResetTimestamp / MS_PER_DAY)

  if (currentDayIndex !== lastResetDayIndex) {
    state.freeRefreshRemaining = DAILY_FREE_REFRESH
    state.lastDailyResetTimestamp = now
  }
}

// ── 公開 API ─────────────────────────────────────────────────────────────────

/**
 * 建立初始 RecruitmentState。
 * 初始池為空；呼叫 initialize() 後 checkAutoRefresh() 會立即填充。
 *
 * 注意：lastRefreshTimestamp 必須設為 0，否則 checkAutoRefresh 看到
 *      `now < now + 24h` 不會觸發初始刷新，新遊戲開局池永遠為空（GDD §6.4
 *      InitializeAsNewGame 規定 checkAutoRefresh 應立即填充）。
 */
export function createRecruitmentState(): RecruitmentState {
  return {
    rookiePool: [],
    veteranPool: [],
    lastRefreshTimestamp: 0,
    freeRefreshRemaining: DAILY_FREE_REFRESH,
    lastDailyResetTimestamp: 0,
    nextCandidateId: 1,
  }
}

/**
 * 注入 F-03 Resource handlers（單獨使用，不需整體 initialize）。
 * 可在 initialize() 之前或之後呼叫；多次呼叫以最後一次為準。
 */
export function setResourceHandlers(deps: ResourceDeps): void {
  _deps = deps
}

/**
 * 初始化 Recruitment 系統：注入 deps 並立即執行 checkAutoRefresh。
 * 呼叫此函式後系統進入完整工作狀態（GDD §6.4 InitializeAsNewGame 行為）。
 *
 * @param state RecruitmentState
 * @param deps F-03 Resource callback 集合
 * @param guildLevel 公會等級（用於初始池生成）
 */
export function initialize(
  state: RecruitmentState,
  deps: ResourceDeps,
  guildLevel: GuildLevel = 1,
): void {
  setResourceHandlers(deps)
  checkDailyReset(state)
  checkAutoRefresh(state, guildLevel)
}

/**
 * 取得新手候選池（唯讀）。
 */
export function getRookiePool(state: RecruitmentState): readonly RecruitCandidate[] {
  return state.rookiePool
}

/**
 * 取得老手候選池（唯讀）。
 */
export function getVeteranPool(state: RecruitmentState): readonly RecruitCandidate[] {
  return state.veteranPool
}

/**
 * 接納新手冒險者（免費）。
 *
 * @param state RecruitmentState
 * @param candidateID 目標候選者 ID
 * @param addToRoster 呼叫端提供的加入名冊函式；回傳 true 表示成功
 * @returns 成功回傳 true；名冊已滿或找不到候選者時回傳 false
 */
export function recruitRookie(
  state: RecruitmentState,
  candidateID: number,
  addToRoster: (adv: Adventurer) => boolean,
): boolean {
  const idx = state.rookiePool.findIndex((c) => c.candidateID === candidateID)
  if (idx === -1) return false

  const candidate = state.rookiePool[idx]

  // 嘗試加入名冊；addToRoster 回 false 表示名冊已滿
  const success = addToRoster(candidate.adventurer)
  if (!success) return false

  // 加入成功才從池中移除
  state.rookiePool.splice(idx, 1)

  eventBus.emit('recruit:success', {
    adventurerId: candidate.adventurer.id,
    source: 'rookie',
  })

  return true
}

/**
 * 邀請老手冒險者（消耗金幣 + 聲望門檻）。
 *
 * 流程：
 *   1. 確認候選者存在
 *   2. 確認名冊未滿（addToRoster 失敗即代表滿員）
 *   3. 確認聲望門檻達成
 *   4. 確認金幣充足
 *   5. 扣款
 *   6. 呼叫 addToRoster；若失敗則回滾扣款
 *
 * @param state RecruitmentState
 * @param candidateID 目標候選者 ID
 * @param addToRoster 呼叫端提供的加入名冊函式
 * @param guildLevel 公會等級（僅供日後擴充，目前不直接使用）
 * @returns 成功回傳 true；各類失敗回傳 false
 */
export function recruitVeteran(
  state: RecruitmentState,
  candidateID: number,
  addToRoster: (adv: Adventurer) => boolean,
  guildLevel: GuildLevel,
): boolean {
  void guildLevel // 保留參數供未來擴充

  if (!_deps) {
    console.error('FT-01 recruitVeteran: ResourceDeps 尚未注入，請先呼叫 setResourceHandlers()')
    return false
  }

  const idx = state.veteranPool.findIndex((c) => c.candidateID === candidateID)
  if (idx === -1) return false

  const candidate = state.veteranPool[idx]

  // 聲望門檻檢查
  if (_deps.getReputation() < candidate.reputationReq) {
    return false
  }

  // 金幣充足檢查
  if (!_deps.canAfford(candidate.cost)) {
    return false
  }

  // 扣款
  const deducted = _deps.addGold(-candidate.cost)
  if (!deducted) {
    // canAfford 通過但 addGold 失敗（重入防護等極端情況）
    console.error('FT-01 recruitVeteran: canAfford 通過但 addGold 失敗，中止招募')
    return false
  }

  // 嘗試加入名冊
  const success = addToRoster(candidate.adventurer)
  if (!success) {
    // 加入失敗（名冊已滿或 isUnique 衝突），回滾扣款
    _deps.addGold(+candidate.cost)
    return false
  }

  // 加入成功才從池中移除
  state.veteranPool.splice(idx, 1)

  eventBus.emit('recruit:success', {
    adventurerId: candidate.adventurer.id,
    source: 'veteran',
  })

  return true
}

/**
 * 手動刷新候選池。
 * 優先消耗免費次數；耗盡後收取 REFRESH_COST（150g）。
 * 手動刷新成功後重置自動刷新計時器（GDD §3.2 rule 3）。
 *
 * @param state RecruitmentState
 * @param guildLevel 公會等級（決定老手池上限）
 * @returns 成功回傳 true；金幣不足時回傳 false
 */
export function manualRefresh(
  state: RecruitmentState,
  guildLevel: GuildLevel,
): boolean {
  // 先執行每日重置判斷
  checkDailyReset(state)

  let source: 'manual_free' | 'manual_paid'

  if (state.freeRefreshRemaining > 0) {
    // 消耗免費次數
    state.freeRefreshRemaining -= 1
    source = 'manual_free'
  } else {
    // 付費刷新：確認金幣足夠
    if (!_deps) {
      console.error('FT-01 manualRefresh: ResourceDeps 尚未注入，無法付費刷新')
      return false
    }
    if (!_deps.canAfford(REFRESH_COST)) {
      return false
    }
    _deps.addGold(-REFRESH_COST)
    source = 'manual_paid'
  }

  // 執行刷新（同時重置 lastRefreshTimestamp）
  executeRefresh(state, guildLevel, source)

  return true
}

/**
 * 檢查自動刷新是否到期，若已到期則執行一次刷新。
 * 重啟時立即呼叫以處理離線期間到期的情況（GDD §3.2 rule 4 / §5.3）。
 * 不補算多次：只執行一次（GDD §5.3）。
 *
 * @param state RecruitmentState
 * @param guildLevel 公會等級
 */
export function checkAutoRefresh(
  state: RecruitmentState,
  guildLevel: GuildLevel,
): void {
  const now = Date.now()
  const intervalMs = calcAutoRefreshIntervalMs()

  if (now >= state.lastRefreshTimestamp + intervalMs) {
    executeRefresh(state, guildLevel, 'auto')
  }
}

/**
 * 取得本週期剩餘免費手動刷新次數。
 */
export function getFreeRefreshRemaining(state: RecruitmentState): number {
  return state.freeRefreshRemaining
}

/**
 * 取得下次自動刷新的 UTC 毫秒時間戳記。
 */
export function getNextAutoRefreshTimestamp(state: RecruitmentState): number {
  return state.lastRefreshTimestamp + calcAutoRefreshIntervalMs()
}

/**
 * 序列化 RecruitmentState 為存檔格式。
 * 對應 GDD §6.4 ISaveable 契約（OwnerKey: "ft01Recruitment"）。
 */
export function serialize(state: RecruitmentState): SerializedRecruitmentState {
  return {
    rookiePool: [...state.rookiePool],
    veteranPool: [...state.veteranPool],
    lastRefreshTimestamp: state.lastRefreshTimestamp,
    freeRefreshRemaining: state.freeRefreshRemaining,
    lastDailyResetTimestamp: state.lastDailyResetTimestamp,
    nextCandidateId: state.nextCandidateId,
  }
}

/**
 * 從序列化資料還原 RecruitmentState。
 * 還原後應立即呼叫 checkAutoRefresh() 處理離線到期（GDD §6.4）。
 */
export function deserialize(data: SerializedRecruitmentState): RecruitmentState {
  return {
    rookiePool: [...data.rookiePool],
    veteranPool: [...data.veteranPool],
    lastRefreshTimestamp: data.lastRefreshTimestamp,
    freeRefreshRemaining: data.freeRefreshRemaining,
    lastDailyResetTimestamp: data.lastDailyResetTimestamp,
    nextCandidateId: data.nextCandidateId,
  }
}

// ── Jam 簡化延後清單 ─────────────────────────────────────────────────────────
//
// 以下功能因 Phase 2 Web Jam 範疇限制，暫時延後實作：
//
// 1. AdventurerTemplate 具名模板優先邏輯（GDD §3.3 Phase 1 / §3.4 Phase 1）
//    → 目前全部隨機生成，無 templateID 優先池
//
// 2. isUnique 過濾（GDD §3.3 rule 4 / §5.2 isUnique 衝突處理）
//    → createAdventurer 不回傳 templateID；isUnique 衝突判斷跳過
//
// 3. C-05 Trait 職業群組抽取（GDD §3.3 rule 3 / §4.5 Phase 2）
//    → growthTraits 固定 []（由 createAdventurer 預設）
//
// 4. FT-07 Guild Building 刷新間隔（GDD §7.4）
//    → BASE_AUTO_REFRESH_INTERVAL_MS 固定 24h
//
// 5. FT-12 公會櫃臺職員刷新縮短加成（GDD §4.1 FT-12 加成）
//    → RECRUIT_REFRESH_REDUCTION_SEC 固定 0
//
// 6. 每日重置訂閱 F-02 OnDailyReset 事件
//    → 改為呼叫 manualRefresh / checkAutoRefresh 時透過 checkDailyReset() 懶判斷
//
// 7. FT-10 Save/Load ISaveable 介面正式接入
//    → serialize / deserialize 已備妥，待 FT-10 整合時直接使用
