/**
 * NPC Decision System (FT-03)
 *
 * 實作：
 *   - 推薦判斷（makeDecision）：冒險者對玩家推薦任務的接受/拒絕計算
 *   - 閒置自主接單（tickAutoPickup）：每分鐘由外部呼叫，對符合條件的 idle 冒險者自動派遣
 *
 * 設計來源：design/GDD/【FT-03】npc-decision-system.md
 *
 * Jam 簡化規則：
 *   - F-01 DataManager 不存在 → 常數直接從 '../data/constants' 取
 *   - F-02 Time System → 改用 Date.now() 與外部呼叫 tickAutoPickup() 替代訂閱式 OnMinuteTick
 *   - C-05 behavior trait 暫無資料 → getBehaviorWillingnessDelta 永遠 return 0（stub）
 *   - FT-12 Staff → getStaffWillingnessBonus 暫返回 0（stub）
 */

import type { Adventurer, Mission, Difficulty } from '../types'
import { ACCEPTANCE_THRESHOLD, DEATH_AVERSION, WILLINGNESS_JITTER } from '../data/constants'
import { calcRates } from './dispatch'
import { getMaxDifficulty } from './guild'
import { eventBus } from '../core/events'
import type { GuildLevel } from '../types'

// ── 模組內部常數（未來統一移到 constants.ts）────────────────────────────────
// TODO: 將 AUTO_PICKUP_IDLE_MINUTES 與 AUTO_PICKUP_INTERVAL_MINUTES 移至 constants.ts
const AUTO_PICKUP_IDLE_MINUTES = 10
const AUTO_PICKUP_INTERVAL_MINUTES = 10

// ── Difficulty index helper ───────────────────────────────────────────────────
// 與 dispatch.ts 保持相同映射：F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8
const DIFFICULTY_ORDER: Difficulty[] = ['F', 'E', 'D', 'C', 'B', 'A', 'S', 'SS', 'SSS']

function difficultyIndex(d: Difficulty): number {
  return DIFFICULTY_ORDER.indexOf(d)
}

// ── 公開型別 ───────────────────────────────────────────────────────────────────

export type RejectionReason = 'TooRisky' | 'NotWilling' | 'NotInterested'

export interface DecisionResult {
  accepted: boolean
  /** 含 jitter 的最終意願分數 */
  effectiveScore: number
  /** Step 1 後：基礎意願分（debug 用） */
  willingnessScoreBase: number
  /** Step 2 後：加 behavior 特質修正 */
  willingnessScoreAfterTraits: number
  /** Step 3 後：加委託官加成 */
  willingnessScoreAfterStaff: number
  /** 拒絕原因；接受時為 null */
  rejectionReason: RejectionReason | null
}

/** 自主接單追蹤 — per 冒險者 */
export interface AutoPickupTracker {
  /** Map<adventurerId, idleSinceTs(ms)> */
  idleSince: Map<string, number>
  /** Map<adventurerId, lastAutoPickupTs(ms)> */
  lastPickup: Map<string, number>
}

/** tickAutoPickup 的單筆成功接單紀錄 */
export interface AutoPickupResult {
  adventurerId: string
  missionId: string
  effectiveScore: number
}

// ── 注入 Handler（FT-12 與 C-05 預留 hook）────────────────────────────────────

/**
 * FT-12 委託官意願加成查詢。
 * Jam 簡化：預設 stub 永遠回傳 0；未來由 FT-12 實作後透過 setStaffWillingnessBonusHandler 注入。
 */
let _staffWillingnessBonusHandler: () => number = () => 0

/**
 * C-05 behavior 特質意願修正查詢。
 * Jam 簡化：預設 stub 永遠回傳 0；未來由 C-05 實作後透過 setBehaviorTraitHandler 注入。
 */
let _behaviorTraitHandler: (adv: Adventurer, mission: Mission) => number = (_adv, _mission) => 0

/**
 * 注入 FT-12 委託官意願加成查詢函式。
 * @param fn 回傳目前生效的累計委託官加成值（FT-12 內部負責 cap）
 */
export function setStaffWillingnessBonusHandler(fn: () => number): void {
  _staffWillingnessBonusHandler = fn
}

/**
 * 注入 C-05 behavior 特質意願修正查詢函式。
 * @param fn (adv, mission) → 加法修正值（可為負）
 */
export function setBehaviorTraitHandler(fn: (adv: Adventurer, mission: Mission) => number): void {
  _behaviorTraitHandler = fn
}

// ── 私有計算輔助 ───────────────────────────────────────────────────────────────

/**
 * 在指定範圍內回傳均勻分布的隨機數。
 * @param min 下限（含）
 * @param max 上限（含）
 */
function randBetween(min: number, max: number): number {
  return min + Math.random() * (max - min)
}

/**
 * 依 GDD §4.1 公式計算 willingnessScoreAfterStaff（不含 jitter）。
 * 分三步驟累加，各中間值供 DecisionResult debug 欄位回傳。
 */
function calcWillingnessSteps(
  adv: Adventurer,
  mission: Mission,
  finalSuccessRate: number,
  finalDeathRate: number,
): {
  base: number
  afterTraits: number
  afterStaff: number
} {
  // Step 1：基礎意願分
  const base = finalSuccessRate - finalDeathRate * DEATH_AVERSION

  // Step 2：behavior 特質修正（Jam 階段 stub → 永遠 +0）
  const behaviorDelta = _behaviorTraitHandler(adv, mission)
  const afterTraits = base + behaviorDelta

  // Step 3：委託官加成（Jam 階段 stub → 永遠 +0）
  const staffBonus = _staffWillingnessBonusHandler()
  const afterStaff = afterTraits + staffBonus

  return { base, afterTraits, afterStaff }
}

/**
 * 依 willingness 三階段中間值決定 RejectionReason。
 * 判斷優先序（GDD §3.1）：
 *   1. base < threshold → TooRisky
 *   2. afterTraits < threshold（但 base ≥ threshold）→ NotWilling
 *   3. effectiveScore < threshold（但 afterStaff ≥ threshold）→ NotInterested（jitter 拉低）
 */
function determineRejectionReason(
  base: number,
  afterTraits: number,
  _effectiveScore: number,
): RejectionReason {
  if (base < ACCEPTANCE_THRESHOLD) return 'TooRisky'
  if (afterTraits < ACCEPTANCE_THRESHOLD) return 'NotWilling'
  // effectiveScore < ACCEPTANCE_THRESHOLD 但 afterStaff ≥ threshold（jitter 導致）
  return 'NotInterested'
}

// ── Tracker API ───────────────────────────────────────────────────────────────

/**
 * 建立空白的自主接單追蹤器。
 * 每次新遊戲或初始化時呼叫一次。
 */
export function createAutoPickupTracker(): AutoPickupTracker {
  return {
    idleSince: new Map(),
    lastPickup: new Map(),
  }
}

/**
 * 記錄冒險者進入 idle 的時間點。
 * 應在冒險者狀態切換為 idle 時呼叫（例如任務完成、恢復傷勢後）。
 *
 * @param tracker AutoPickupTracker 實例
 * @param advId   冒險者 ID
 * @param now     當前時間戳（ms）；預設 Date.now()
 */
export function markIdle(tracker: AutoPickupTracker, advId: string, now?: number): void {
  tracker.idleSince.set(advId, now ?? Date.now())
}

/**
 * 清除冒險者的 idle 與 lastPickup 紀錄。
 * 應在冒險者被派遣或死亡時呼叫。
 *
 * @param tracker AutoPickupTracker 實例
 * @param advId   冒險者 ID
 */
export function markBusy(tracker: AutoPickupTracker, advId: string): void {
  tracker.idleSince.delete(advId)
  tracker.lastPickup.delete(advId)
}

// ── 核心決策 API ───────────────────────────────────────────────────────────────

/**
 * 推薦判斷：計算冒險者對指定任務的接受/拒絕。
 *
 * 此函式為純決策計算層，不執行派遣（dispatch）。
 * 呼叫端（P-02）在收到 accepted=true 後自行呼叫 tryDispatch。
 *
 * 邊緣案例：冒險者狀態非 idle → 回傳 accepted=false, rejectionReason=null（§5.1）
 *
 * @param adv     冒險者物件
 * @param mission 任務物件
 * @returns DecisionResult（含各階段中間分數，供 UI debug 顯示）
 */
export function makeDecision(adv: Adventurer, mission: Mission): DecisionResult {
  // 邊緣案例：非 idle 狀態（Dispatched / Wounded / Dead）
  if (adv.status !== 'idle') {
    console.warn(
      `[FT-03] makeDecision 呼叫於非 idle 冒險者：id=${adv.id}, status=${adv.status}`,
    )
    return {
      accepted: false,
      effectiveScore: 0,
      willingnessScoreBase: 0,
      willingnessScoreAfterTraits: 0,
      willingnessScoreAfterStaff: 0,
      rejectionReason: null,
    }
  }

  const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)
  const { base, afterTraits, afterStaff } = calcWillingnessSteps(
    adv,
    mission,
    finalSuccessRate,
    finalDeathRate,
  )

  // Step 4：jitter（不 clamp，自然值）
  const jitter = randBetween(-WILLINGNESS_JITTER, WILLINGNESS_JITTER)
  const effectiveScore = afterStaff + jitter

  if (effectiveScore >= ACCEPTANCE_THRESHOLD) {
    return {
      accepted: true,
      effectiveScore,
      willingnessScoreBase: base,
      willingnessScoreAfterTraits: afterTraits,
      willingnessScoreAfterStaff: afterStaff,
      rejectionReason: null,
    }
  }

  return {
    accepted: false,
    effectiveScore,
    willingnessScoreBase: base,
    willingnessScoreAfterTraits: afterTraits,
    willingnessScoreAfterStaff: afterStaff,
    rejectionReason: determineRejectionReason(base, afterTraits, effectiveScore),
  }
}

/**
 * 預覽意願分數（不含 jitter）。
 * 回傳 willingnessScoreAfterStaff，供 P-02 顯示推薦前的預估值。
 *
 * @param adv     冒險者物件
 * @param mission 任務物件
 * @returns willingnessScoreAfterStaff（Step 3 後，未加 jitter）
 */
export function previewEffectiveScore(adv: Adventurer, mission: Mission): number {
  const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)
  const { afterStaff } = calcWillingnessSteps(adv, mission, finalSuccessRate, finalDeathRate)
  return afterStaff
}

// ── 自主接單主迴圈 ──────────────────────────────────────────────────────────────

/**
 * 自主接單 tick — 每分鐘由外部呼叫一次（替代 F-02 OnMinuteTick 訂閱）。
 *
 * 對每位 idle 冒險者檢查：
 *   1. idle 時間 >= AUTO_PICKUP_IDLE_MINUTES
 *   2. 距上次自主接單 >= AUTO_PICKUP_INTERVAL_MINUTES
 * 符合條件者掃描可用任務（難度 ≤ 公會上限），選 effectiveScore 最高且 >= ACCEPTANCE_THRESHOLD 者派遣。
 *
 * 邊緣案例（GDD §5.2）：
 *   - 無可用任務 → 仍更新 lastPickupTs，靜默跳過
 *   - 所有任務 bestScore < threshold → 同上
 *   - dispatch 回傳 false → 靜默失敗，不重試
 *
 * @param tracker           AutoPickupTracker 實例
 * @param idleAdvs          目前 idle 冒險者列表（由呼叫端從 GuildState 取得）
 * @param availableMissions 委託板上可用任務列表
 * @param dispatch          派遣函式注入（回傳 true=成功，false=失敗/搶單）
 * @param guildLevel        目前公會等級（用於難度上限過濾）
 * @param now               當前時間戳（ms）；預設 Date.now()
 * @returns 成功自主接單的紀錄列表
 */
export function tickAutoPickup(
  tracker: AutoPickupTracker,
  idleAdvs: Adventurer[],
  availableMissions: Mission[],
  dispatch: (adv: Adventurer, mission: Mission) => boolean,
  guildLevel: number,
  now?: number,
): AutoPickupResult[] {
  const currentTime = now ?? Date.now()
  const results: AutoPickupResult[] = []

  // 取得公會當前最高難度索引
  const maxDiff = getMaxDifficulty(guildLevel as GuildLevel)
  const maxDiffIdx = difficultyIndex(maxDiff)

  for (const adv of idleAdvs) {
    // 防禦性檢查：狀態必須是 idle
    if (adv.status !== 'idle') continue

    // 確保 idleSince 有記錄（首次見到 → 初始化）
    let idleSinceTs = tracker.idleSince.get(adv.id)
    if (idleSinceTs === undefined) {
      // 首次在 tick 中見到此冒險者，記錄目前時間作為起點
      tracker.idleSince.set(adv.id, currentTime)
      idleSinceTs = currentTime
    }

    // 檢查 idle 時間是否已達門檻
    const idleMinutes = (currentTime - idleSinceTs) / 60_000
    if (idleMinutes < AUTO_PICKUP_IDLE_MINUTES) continue

    // 檢查距上次自主接單的間隔
    const lastPickupTs = tracker.lastPickup.get(adv.id) ?? 0
    const sinceLastPickupMin = (currentTime - lastPickupTs) / 60_000
    if (sinceLastPickupMin < AUTO_PICKUP_INTERVAL_MINUTES) continue

    // 過濾難度 ≤ 公會上限的任務
    const candidates = availableMissions.filter(
      m => difficultyIndex(m.difficulty) <= maxDiffIdx,
    )

    // 選出 effectiveScore 最高的任務
    let bestMission: Mission | null = null
    let bestScore = -Infinity

    for (const m of candidates) {
      const { finalSuccessRate, finalDeathRate } = calcRates(adv, m)
      const { afterStaff } = calcWillingnessSteps(adv, m, finalSuccessRate, finalDeathRate)
      const jitter = randBetween(-WILLINGNESS_JITTER, WILLINGNESS_JITTER)
      const score = afterStaff + jitter

      if (score > bestScore) {
        bestScore = score
        bestMission = m
      }
    }

    // 無論結果如何，均更新 lastPickupTs（§5.2 邊緣案例）
    tracker.lastPickup.set(adv.id, currentTime)

    // 若無符合條件的任務或最高分仍未達門檻，靜默跳過
    if (bestMission === null || bestScore < ACCEPTANCE_THRESHOLD) continue

    // 嘗試派遣（由呼叫端注入）
    const dispatchSuccess = dispatch(adv, bestMission)
    if (!dispatchSuccess) {
      // 靜默失敗（任務被搶 / 其他原因），不重試
      continue
    }

    // 派遣成功：發布 npc:auto_pickup 事件
    eventBus.emit('npc:auto_pickup', {
      adventurerId: adv.id,
      missionId: bestMission.id,
    })

    results.push({
      adventurerId: adv.id,
      missionId: bestMission.id,
      effectiveScore: bestScore,
    })
  }

  return results
}
