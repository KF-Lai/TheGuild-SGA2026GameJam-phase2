/**
 * FT-05 Guild Gold Flow — The Guild Phase 2 Web
 * 實作：design/GDD/【FT-05】guild-gold-flow.md
 *
 * 職責：
 *   1. 預收 API（prepayCommission）：呼叫端決定觸發時機
 *   2. 結算 API（computeCommissionNet）：純計算 + 執行 addGoldAllowBankruptcy + emit
 *   3. 維護費管線（chargeMaintenance）：完整實作，不主動訂閱（FT-07 jam 不發布）
 *   4. 薪水管線（chargeSalary）：完整實作，不主動訂閱（FT-12 jam 不發布）
 *   5. 加成查詢：透過 BonusHandlers callback 注入（jam 預設全 0）
 *
 * 注意：本模組為「純函式計算層」，不取代 outcome.ts / dispatch.ts 的既有金流邏輯。
 * 呼叫端負責在適當時機呼叫各 API，並自行執行 AddGold 與後續 emit。
 *
 * Phase 2 Web Jam 延後項目（不在本 Stage 範疇）：
 *   - 主動訂閱 OnCommissionAccepted / OnMissionResolved（呼叫端直接呼叫代替）
 *   - 主動訂閱 OnGuildMaintenanceDue / OnStaffSalaryDue（FT-07/FT-12 jam 不發布）
 *   - FT-12 AccountantBonus 真實實作（目前 stub 回 0）
 *   - FT-07 BuildingPenaltyBonus 真實實作（目前 stub 回 0）
 *   - P-03 Notification 破產狀態轉移通知（消費 breakdown 的 bankruptcyState 欄位）
 */

import { eventBus } from '../core/events'
import { COMMISSION_RATE, PENALTY_RATE } from '../data/constants'

// ---------------------------------------------------------------------------
// 公開型別
// ---------------------------------------------------------------------------

/** F-03 破產警告狀態 */
export type BankruptcyWarningState = 'Normal' | 'Warning' | 'Bankrupt'

/** 委託結算明細（GDD §3.1.1） */
export interface CommissionBreakdown {
  missionId: string
  adventurerId: string
  /** Difficulty 字串，快照自 Outcome */
  missionDifficulty: string
  isSuccess: boolean
  baseReward: number
  effectiveCommissionRate: number
  effectivePenaltyRate: number
  accountantCommissionBonus: number
  accountantPenaltyBonus: number
  buildingPenaltyBonus: number
  /** 成功時 baseReward × commissionRate（取整）；失敗時 0 */
  commissionGoldAmount: number
  /** 失敗時 baseReward × penaltyRate（正整數，取整）；成功時 0 */
  penaltyGoldAmount: number
  /** 快照自 Outcome.conditionGoldBonus；成敗皆加 */
  conditionGoldBonus: number
  /** 實際傳入 addGoldAllowBankruptcy 的淨額 */
  netDelta: number
  /** 結算前金幣快照 */
  goldBefore: number
  /** 結算後金幣快照 */
  goldAfter: number
  bankruptcyStateBefore: BankruptcyWarningState
  bankruptcyStateAfter: BankruptcyWarningState
  /** 結算 UTC Unix 毫秒時間戳 */
  settleTimestamp: number
}

/** 設施維護費明細（GDD §3.1.2） */
export interface MaintenanceBreakdown {
  /** buildingID → cost 的 defensive copy */
  items: Record<number, number>
  /** 合計扣款（正整數） */
  totalAmount: number
  /** = -totalAmount */
  netDelta: number
  goldBefore: number
  goldAfter: number
  bankruptcyStateBefore: BankruptcyWarningState
  bankruptcyStateAfter: BankruptcyWarningState
  /** UTC Unix 毫秒時間戳（由呼叫端傳入，支援離線追認） */
  chargeTimestamp: number
}

/** 職員薪水明細（GDD §3.1.3） */
export interface SalaryBreakdown {
  /** staffInstanceId → salary 的 defensive copy */
  items: Record<string, number>
  totalAmount: number
  netDelta: number
  goldBefore: number
  goldAfter: number
  bankruptcyStateBefore: BankruptcyWarningState
  bankruptcyStateAfter: BankruptcyWarningState
  chargeTimestamp: number
}

/**
 * 結算輸入（呼叫端從 SettlementRecord 投影）
 * GDD §3.4
 */
export interface CommissionSettleInput {
  missionId: string
  adventurerId: string
  /** Difficulty 字串 */
  missionDifficulty: string
  isSuccess: boolean
  baseReward: number
  /** C-05 condition 加成；jam 階段可傳 0 */
  conditionGoldBonus: number
}

/**
 * Resource API 注入介面
 * 依賴倒置：gold-flow 不直接依賴 resource.ts，透過 callback 解耦
 */
export interface GoldFlowResourceDeps {
  /** 取得目前金幣量 */
  getGold: () => number
  /**
   * 標準加金（正值）；受 GOLD_MAX clamp。
   * 用於預收（不允許負值進此路徑）。
   * @returns true 表示完整入帳；false 表示因 clamp 而部分入帳（呼叫端可讀 getGold delta 確認）
   */
  addGold: (positiveAmount: number) => boolean
  /**
   * 允許突破破產門檻的金幣增減。
   * 用於結算扣款、維護費、薪水。
   */
  addGoldAllowBankruptcy: (delta: number) => void
  /** 取得目前破產警告狀態 */
  getBankruptcyWarningState: () => BankruptcyWarningState
}

/**
 * FT-12 / FT-07 加成查詢（stub-injectable）
 * jam 預設全 0，整合時注入真實實作
 */
export interface BonusHandlers {
  /** 會計職員傭金加成（+2%）；jam stub 回 0 */
  getAccountantCommissionBonus: () => number
  /** 會計職員賠償率加成（-2%）；jam stub 回 0 */
  getAccountantPenaltyBonus: () => number
  /** 建築層賠償率加成（預留擴充）；jam stub 回 0 */
  getBuildingPenaltyBonus: () => number
}

// ---------------------------------------------------------------------------
// 模組私有狀態
// ---------------------------------------------------------------------------

/** 注入的 Resource 依賴；未注入時所有執行 API 均早退 */
let _deps: GoldFlowResourceDeps | null = null

/** 加成查詢處理器；預設全 stub 回 0 */
const _defaultBonusHandlers: BonusHandlers = {
  getAccountantCommissionBonus: () => 0,
  getAccountantPenaltyBonus:    () => 0,
  getBuildingPenaltyBonus:      () => 0,
}
let _bonusHandlers: BonusHandlers = { ..._defaultBonusHandlers }

// ---------------------------------------------------------------------------
// 設定 API
// ---------------------------------------------------------------------------

/**
 * 注入 Resource 依賴。
 * 必須在呼叫 prepayCommission / computeCommissionNet / chargeMaintenance / chargeSalary 前呼叫。
 */
export function setResourceHandlers(deps: GoldFlowResourceDeps): void {
  _deps = deps
}

/**
 * 部分注入加成查詢 handlers。
 * 未傳入的 handler 維持 stub（回 0）。
 * jam 預設全 stub；FT-12 / FT-07 整合時注入真實實作。
 */
export function setBonusHandlers(handlers: Partial<BonusHandlers>): void {
  _bonusHandlers = {
    ..._defaultBonusHandlers,
    ...handlers,
  }
}

// ---------------------------------------------------------------------------
// 內部工具
// ---------------------------------------------------------------------------

/**
 * ExecuteGoldFlow 共用機制（GDD §3.8）
 * 套入 goldBefore/After 與 bankruptcyState 快照，執行 addGoldAllowBankruptcy。
 * @param breakdown 必須已設定 netDelta；函式直接修改傳入物件的 gold/state 欄位
 */
function executeGoldFlow(breakdown: {
  netDelta: number
  goldBefore: number
  goldAfter: number
  bankruptcyStateBefore: BankruptcyWarningState
  bankruptcyStateAfter: BankruptcyWarningState
}): void {
  // _deps 未注入的防禦由呼叫端（公開 API）負責，此處假設已注入
  const deps = _deps!
  breakdown.goldBefore             = deps.getGold()
  breakdown.bankruptcyStateBefore  = deps.getBankruptcyWarningState()
  deps.addGoldAllowBankruptcy(breakdown.netDelta)
  breakdown.goldAfter              = deps.getGold()
  breakdown.bankruptcyStateAfter   = deps.getBankruptcyWarningState()
}

// ---------------------------------------------------------------------------
// 公開 API
// ---------------------------------------------------------------------------

/**
 * 委託預收（GDD §3.2）
 *
 * 呼叫端從 OnCommissionAccepted 取得 baseReward 後直接呼叫。
 * 執行 deps.addGold(+baseReward)，emit commission:prepaid，回傳實際入帳金額。
 *
 * @returns 實際入帳金額（可能因 GOLD_MAX clamp 而小於 baseReward）；防禦失敗回傳 0
 */
export function prepayCommission(missionId: string, baseReward: number): number {
  // 防禦：_deps 未注入
  if (!_deps) {
    console.error('[FT-05] prepayCommission 失敗：ResourceDeps 尚未注入')
    return 0
  }

  // 防禦：baseReward 非正數（GDD §極端情況 1）
  if (baseReward <= 0) {
    console.error(`[FT-05] prepayCommission 失敗：baseReward 必須為正數，收到 ${baseReward}`)
    return 0
  }

  // 預收：取得前後差額以應對 GOLD_MAX clamp
  const goldBefore = _deps.getGold()
  _deps.addGold(baseReward)
  const prepaidAmount = _deps.getGold() - goldBefore

  eventBus.emit('commission:prepaid', { missionId, prepaidAmount })

  return prepaidAmount
}

/**
 * 委託結算淨額計算（GDD §3.4 / §3.5）
 *
 * 依公式計算 effectiveCommissionRate / effectivePenaltyRate / netDelta，
 * 透過 executeGoldFlow 套入金幣快照，執行 addGoldAllowBankruptcy(netDelta)，
 * emit commission:settled，回傳完整 CommissionBreakdown。
 *
 * 呼叫端**不需要**額外執行 AddGold；本函式已執行扣款/入帳。
 * 呼叫端負責其他後處理（更新 UI、存檔等）。
 */
export function computeCommissionNet(input: CommissionSettleInput): CommissionBreakdown {
  // 防禦：_deps 未注入
  if (!_deps) {
    console.error('[FT-05] computeCommissionNet 失敗：ResourceDeps 尚未注入')
    // 回傳空殼 breakdown，呼叫端可依 netDelta = 0 判斷失敗
    return _emptyCommissionBreakdown(input)
  }

  // 即時查詢加成（jam 預設全 0）
  const accountantCommissionBonus = _bonusHandlers.getAccountantCommissionBonus()
  const accountantPenaltyBonus    = _bonusHandlers.getAccountantPenaltyBonus()
  const buildingPenaltyBonus      = _bonusHandlers.getBuildingPenaltyBonus()

  // GDD §3.4 有效率計算
  const effectiveCommissionRate = COMMISSION_RATE + accountantCommissionBonus
  const effectivePenaltyRate    = Math.max(
    0,
    PENALTY_RATE + accountantPenaltyBonus + buildingPenaltyBonus,
  )

  // GDD §3.5 成功 / 失敗路徑
  let commissionGoldAmount: number
  let penaltyGoldAmount: number
  let netDelta: number

  if (input.isSuccess) {
    commissionGoldAmount = Math.round(input.baseReward * effectiveCommissionRate)
    penaltyGoldAmount    = 0
    netDelta             = commissionGoldAmount + input.conditionGoldBonus
  } else {
    commissionGoldAmount = 0
    penaltyGoldAmount    = Math.round(input.baseReward * effectivePenaltyRate)
    netDelta             = -penaltyGoldAmount + input.conditionGoldBonus
  }

  // 建立 breakdown（goldBefore/After 與 bankruptcyState 由 executeGoldFlow 填入）
  const breakdown: CommissionBreakdown = {
    missionId:               input.missionId,
    adventurerId:            input.adventurerId,
    missionDifficulty:       input.missionDifficulty,
    isSuccess:               input.isSuccess,
    baseReward:              input.baseReward,
    effectiveCommissionRate,
    effectivePenaltyRate,
    accountantCommissionBonus,
    accountantPenaltyBonus,
    buildingPenaltyBonus,
    commissionGoldAmount,
    penaltyGoldAmount,
    conditionGoldBonus:      input.conditionGoldBonus,
    netDelta,
    goldBefore:              0,  // executeGoldFlow 填入
    goldAfter:               0,  // executeGoldFlow 填入
    bankruptcyStateBefore:   'Normal',  // executeGoldFlow 填入
    bankruptcyStateAfter:    'Normal',  // executeGoldFlow 填入
    settleTimestamp:         Date.now(),
  }

  // 執行金幣增減並套入快照
  executeGoldFlow(breakdown)

  eventBus.emit('commission:settled', {
    missionId: input.missionId,
    netDelta,
    breakdown,
  })

  return breakdown
}

/**
 * 設施維護費扣款管線（GDD §3.6）
 *
 * 完整實作；不主動訂閱 OnGuildMaintenanceDue（FT-07 jam 不發布）。
 * 呼叫端（FT-07 整合後）在適當時機直接呼叫。
 *
 * @param items         buildingID → cost 細項（函式內部做 defensive copy）
 * @param dueTimestamp  UTC Unix 毫秒時間戳（由呼叫端傳入，支援離線追認）
 */
export function chargeMaintenance(
  items: Record<number, number>,
  dueTimestamp: number,
): MaintenanceBreakdown {
  // 防禦：_deps 未注入
  if (!_deps) {
    console.error('[FT-05] chargeMaintenance 失敗：ResourceDeps 尚未注入')
    return _emptyMaintenanceBreakdown(items, dueTimestamp)
  }

  // defensive copy 避免上游修改
  const itemsCopy = { ...items }
  const totalAmount = Object.values(itemsCopy).reduce((sum, v) => sum + v, 0)

  // 防禦：totalAmount 非正數（GDD §極端情況）
  if (totalAmount <= 0) {
    console.error(`[FT-05] chargeMaintenance 失敗：totalAmount 必須為正數，計算結果為 ${totalAmount}`)
    return _emptyMaintenanceBreakdown(items, dueTimestamp)
  }

  const netDelta = -totalAmount

  const breakdown: MaintenanceBreakdown = {
    items:                 itemsCopy,
    totalAmount,
    netDelta,
    goldBefore:            0,
    goldAfter:             0,
    bankruptcyStateBefore: 'Normal',
    bankruptcyStateAfter:  'Normal',
    chargeTimestamp:       dueTimestamp,
  }

  executeGoldFlow(breakdown)

  eventBus.emit('maintenance:charged', { totalAmount, breakdown })

  return breakdown
}

/**
 * 職員薪水扣款管線（GDD §3.7）
 *
 * 完整實作；不主動訂閱 OnStaffSalaryDue（FT-12 jam 不發布）。
 * 呼叫端（FT-12 整合後）在適當時機直接呼叫。
 *
 * @param items         staffInstanceId → salary 細項（函式內部做 defensive copy）
 * @param dueTimestamp  UTC Unix 毫秒時間戳
 */
export function chargeSalary(
  items: Record<string, number>,
  dueTimestamp: number,
): SalaryBreakdown {
  // 防禦：_deps 未注入
  if (!_deps) {
    console.error('[FT-05] chargeSalary 失敗：ResourceDeps 尚未注入')
    return _emptySimpleBreakdown<SalaryBreakdown>(items as Record<string | number, number>, dueTimestamp)
  }

  // defensive copy
  const itemsCopy = { ...items }
  const totalAmount = Object.values(itemsCopy).reduce((sum, v) => sum + v, 0)

  if (totalAmount <= 0) {
    console.error(`[FT-05] chargeSalary 失敗：totalAmount 必須為正數，計算結果為 ${totalAmount}`)
    return _emptySimpleBreakdown<SalaryBreakdown>(items as Record<string | number, number>, dueTimestamp)
  }

  const netDelta = -totalAmount

  const breakdown: SalaryBreakdown = {
    items:                 itemsCopy,
    totalAmount,
    netDelta,
    goldBefore:            0,
    goldAfter:             0,
    bankruptcyStateBefore: 'Normal',
    bankruptcyStateAfter:  'Normal',
    chargeTimestamp:       dueTimestamp,
  }

  executeGoldFlow(breakdown)

  eventBus.emit('salary:charged', { totalAmount, breakdown })

  return breakdown
}

// ---------------------------------------------------------------------------
// 內部工具：空殼 breakdown 生成器（防禦失敗時使用，不執行扣款，不 emit）
// ---------------------------------------------------------------------------

function _emptyCommissionBreakdown(input: CommissionSettleInput): CommissionBreakdown {
  return {
    missionId:               input.missionId,
    adventurerId:            input.adventurerId,
    missionDifficulty:       input.missionDifficulty,
    isSuccess:               input.isSuccess,
    baseReward:              input.baseReward,
    effectiveCommissionRate: COMMISSION_RATE,
    effectivePenaltyRate:    PENALTY_RATE,
    accountantCommissionBonus: 0,
    accountantPenaltyBonus:    0,
    buildingPenaltyBonus:      0,
    commissionGoldAmount:    0,
    penaltyGoldAmount:       0,
    conditionGoldBonus:      input.conditionGoldBonus,
    netDelta:                0,
    goldBefore:              0,
    goldAfter:               0,
    bankruptcyStateBefore:   'Normal',
    bankruptcyStateAfter:    'Normal',
    settleTimestamp:         Date.now(),
  }
}

function _emptyMaintenanceBreakdown(
  items: Record<number, number>,
  chargeTimestamp: number,
): MaintenanceBreakdown {
  return {
    items:                 { ...items },
    totalAmount:           0,
    netDelta:              0,
    goldBefore:            0,
    goldAfter:             0,
    bankruptcyStateBefore: 'Normal',
    bankruptcyStateAfter:  'Normal',
    chargeTimestamp,
  }
}

/**
 * 薪水空殼 breakdown 生成器（與 MaintenanceBreakdown 結構相同）
 * 使用泛型減少重複：T 必須與 SalaryBreakdown 結構相符
 */
function _emptySimpleBreakdown<T extends {
  items: Record<string | number, number>
  totalAmount: number
  netDelta: number
  goldBefore: number
  goldAfter: number
  bankruptcyStateBefore: BankruptcyWarningState
  bankruptcyStateAfter: BankruptcyWarningState
  chargeTimestamp: number
}>(items: Record<string | number, number>, chargeTimestamp: number): T {
  return {
    items:                 { ...items },
    totalAmount:           0,
    netDelta:              0,
    goldBefore:            0,
    goldAfter:             0,
    bankruptcyStateBefore: 'Normal',
    bankruptcyStateAfter:  'Normal',
    chargeTimestamp,
  } as T
}
