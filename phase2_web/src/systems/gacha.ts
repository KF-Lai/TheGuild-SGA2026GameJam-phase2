/**
 * FT-08 Gacha System — Jam 簡化版
 *
 * 三角色固定池（米拉 501 / 譚恩 502 / 凱拉 503）。
 * 無保底（pity）、無垃圾物品（trash items）、無多池架構。
 * 候選池 = GACHA_POOL_STAFF_IDS 過濾掉已雇用的 staffID。
 * 刷新間隔由 FT-07 建築系統注入（getStaffLoungeRefreshSec），不 hardcode。
 */

import { eventBus } from '../core/events';

// ---------------------------------------------------------------------------
// 常數（Jam hardcode）
// ---------------------------------------------------------------------------

/** Jam 三角色 staffID（對齊 staff.ts STAFF_TABLE） */
const GACHA_POOL_STAFF_IDS: readonly number[] = [501, 502, 503];

/** Jam hardcode 名稱與稀有度查詢表（對應 staff.ts STAFF_TABLE） */
const STAFF_META_LOOKUP: Readonly<Record<number, { name: string; rarity: number }>> = {
  501: { name: '米拉', rarity: 3 },
  502: { name: '譚恩', rarity: 4 },
  503: { name: '凱拉', rarity: 3 },
};

/** 手動刷新固定費用（金幣） */
const MANUAL_REFRESH_COST = 200;

// ---------------------------------------------------------------------------
// 型別
// ---------------------------------------------------------------------------

/**
 * 簡化版候選卡：只記 staffID + name + rarity。
 * 無 trash、無 reserved、無 slot（GDD §3 砍掉）。
 */
export interface GachaCandidateCard {
  /** 候選 ID：跨刷新自增，確保全 session 唯一 */
  candidateID: number;
  staffID: number;
  name: string;
  rarity: number;
}

export interface GachaState {
  /** 當前候選池（可能為空，三角色全雇用時） */
  currentCandidates: GachaCandidateCard[];
  /**
   * 最近一次刷新的 UTC ms 時間戳。
   * createGachaState() 設 0；initialize() 後 checkAutoRefresh 觸發初始填充。
   */
  lastRefreshTimestamp: number;
  /** 自增候選 ID（跨刷新累加，不 reset） */
  nextCandidateID: number;
}

export type GachaRefreshSource = 'auto' | 'manual_paid' | 'initial';

export type RecruitResult =
  | 'SUCCESS'
  | 'STAFF_SYSTEM_LOCKED'
  | 'CANDIDATE_NOT_FOUND'
  | 'BUILDING_NOT_ELIGIBLE'   // FT-12.hireStaff 拒絕
  | 'BUILDING_FULL'            // 同上
  | 'INVALID_STAFF_ID';        // 不應發生，但保留防禦

export type ManualRefreshResult =
  | 'SUCCESS'
  | 'STAFF_SYSTEM_LOCKED'
  | 'GOLD_INSUFFICIENT';

/** 跨系統 dep 注入介面（避免 gacha.ts 直接 import 其他 system） */
export interface GachaSystemDeps {
  /** FT-07：Staff System 是否已解鎖 */
  isUnlocked: () => boolean;
  /**
   * FT-07：取當前 Staff Lounge 刷新間隔（秒）。
   * isUnlocked() === false 時不會走到此。
   */
  getStaffLoungeRefreshSec: () => number;
  /** F-03：取當前金幣數量 */
  getGold: () => number;
  /** F-03：嚴格扣款；金幣不足時回 false（不扣） */
  spendGold: (amount: number) => boolean;
}

/**
 * hireStaff callback 注入介面。
 * main.ts 整合時注入，gacha.ts 不直接 import staff.ts runtime state。
 */
export type HireStaffFn = (
  staffID: number,
  buildingID: number
) =>
  | { ok: true; instanceId: string }
  | { ok: false; reason: string };

// ---------------------------------------------------------------------------
// 序列化格式
// ---------------------------------------------------------------------------

export interface SerializedGachaState {
  currentCandidates: GachaCandidateCard[];
  lastRefreshTimestamp: number;
  nextCandidateID: number;
}

// ---------------------------------------------------------------------------
// Module-scoped dep（注入式，非 runtime state）
// ---------------------------------------------------------------------------

let _deps: GachaSystemDeps | null = null;

// ---------------------------------------------------------------------------
// 公開 API — 初始化
// ---------------------------------------------------------------------------

/** 建立預設 GachaState（候選池空，lastRefreshTimestamp=0 待初始刷新） */
export function createGachaState(): GachaState {
  return {
    currentCandidates: [],
    lastRefreshTimestamp: 0,
    nextCandidateID: 1,
  };
}

/**
 * 注入跨系統 deps。
 * 必須在 initialize() 之前呼叫。
 */
export function setSystemDeps(deps: GachaSystemDeps): void {
  _deps = deps;
}

/**
 * 初始化：觸發 checkAutoRefresh 立即填充候選池。
 * lastRefreshTimestamp=0 確保首次 checkAutoRefresh 必定觸發。
 */
export function initialize(state: GachaState, hiredStaffIDs: Set<number>): void {
  checkAutoRefresh(state, hiredStaffIDs);
}

// ---------------------------------------------------------------------------
// 內部工具
// ---------------------------------------------------------------------------

/**
 * 依 hiredStaffIDs 過濾後生成候選池。
 * 已雇用的 staffID 不進入候選清單；三角色全雇用時回空陣列。
 */
function generateCandidates(
  state: GachaState,
  hiredStaffIDs: Set<number>
): GachaCandidateCard[] {
  const result: GachaCandidateCard[] = [];
  for (const staffID of GACHA_POOL_STAFF_IDS) {
    if (hiredStaffIDs.has(staffID)) continue;
    const meta = STAFF_META_LOOKUP[staffID];
    if (!meta) continue; // 防禦：資料表缺項
    result.push({
      candidateID: state.nextCandidateID++,
      staffID,
      name: meta.name,
      rarity: meta.rarity,
    });
  }
  return result;
}

/**
 * 執行刷新並 emit gacha:refreshed。
 * 無論候選池是否為空都執行（三角色全雇用時 count=0，UI 顯示「目前無候選」）。
 */
function executeRefresh(
  state: GachaState,
  hiredStaffIDs: Set<number>,
  source: GachaRefreshSource
): void {
  state.currentCandidates = generateCandidates(state, hiredStaffIDs);
  state.lastRefreshTimestamp = Date.now();
  eventBus.emit('gacha:refreshed', { source, count: state.currentCandidates.length });
}

// ---------------------------------------------------------------------------
// 公開 API — 候選池查詢
// ---------------------------------------------------------------------------

/** 取當前候選池（唯讀） */
export function getCurrentCandidates(
  state: GachaState
): readonly GachaCandidateCard[] {
  return state.currentCandidates;
}

// ---------------------------------------------------------------------------
// 公開 API — 自動刷新
// ---------------------------------------------------------------------------

/**
 * 檢查並執行自動刷新。
 * 重啟後呼叫：若 lastRefreshTimestamp=0 或刷新間隔已到則執行一次刷新（不補算多次）。
 * 系統未解鎖則直接返回（不變動 state、不 emit）。
 */
export function checkAutoRefresh(
  state: GachaState,
  hiredStaffIDs: Set<number>
): void {
  if (!_deps?.isUnlocked()) return;

  const now = Date.now();
  const intervalMs = _deps.getStaffLoungeRefreshSec() * 1000;
  if (intervalMs <= 0) return; // 防禦：避免無限觸發

  if (now >= state.lastRefreshTimestamp + intervalMs) {
    // lastRefreshTimestamp=0 時（初始或重置）以 'initial' 來源標記
    const source: GachaRefreshSource =
      state.lastRefreshTimestamp === 0 ? 'initial' : 'auto';
    executeRefresh(state, hiredStaffIDs, source);
  }
}

// ---------------------------------------------------------------------------
// 公開 API — 手動刷新
// ---------------------------------------------------------------------------

/**
 * 嘗試手動付費刷新候選池。
 * 系統未解鎖 → STAFF_SYSTEM_LOCKED；金幣不足 → GOLD_INSUFFICIENT。
 */
export function tryManualRefresh(
  state: GachaState,
  hiredStaffIDs: Set<number>
): ManualRefreshResult {
  if (!_deps?.isUnlocked()) return 'STAFF_SYSTEM_LOCKED';
  if (_deps.getGold() < MANUAL_REFRESH_COST) return 'GOLD_INSUFFICIENT';
  if (!_deps.spendGold(MANUAL_REFRESH_COST)) return 'GOLD_INSUFFICIENT';

  executeRefresh(state, hiredStaffIDs, 'manual_paid');
  return 'SUCCESS';
}

// ---------------------------------------------------------------------------
// 公開 API — 招募決策
// ---------------------------------------------------------------------------

/**
 * 嘗試招募候選人。
 *
 * 流程：
 * 1. 系統解鎖檢查
 * 2. 候選 ID 查找
 * 3. 呼叫注入的 hireStaff callback
 * 4. 成功 → 從候選池移除、emit gacha:hired
 * 5. 失敗 → 映射 hireStaff reason 至 RecruitResult
 *
 * 注意：gacha 系統不持有 hired roster（由 FT-12 staff.ts 管理）。
 * 候選池移除僅針對本次候選 ID；下次 refresh 時會依 hiredStaffIDs 重新過濾。
 *
 * @param state      GachaState
 * @param candidateID 候選卡的 candidateID
 * @param buildingID  要指派的建築 ID（傳給 hireStaff）
 * @param hireStaff  由 main.ts 注入的 callback
 */
export function tryRecruit(
  state: GachaState,
  candidateID: number,
  buildingID: number,
  hireStaff: HireStaffFn
): RecruitResult {
  if (!_deps?.isUnlocked()) return 'STAFF_SYSTEM_LOCKED';

  const idx = state.currentCandidates.findIndex(c => c.candidateID === candidateID);
  if (idx === -1) return 'CANDIDATE_NOT_FOUND';

  const candidate = state.currentCandidates[idx];
  const result = hireStaff(candidate.staffID, buildingID);

  if (!result.ok) {
    // 映射 staff.ts HireResult reason 至 RecruitResult
    switch (result.reason) {
      case 'BUILDING_NOT_ELIGIBLE': return 'BUILDING_NOT_ELIGIBLE';
      case 'BUILDING_FULL':         return 'BUILDING_FULL';
      case 'STAFF_SYSTEM_LOCKED':   return 'STAFF_SYSTEM_LOCKED';
      case 'INVALID_STAFF_ID':      return 'INVALID_STAFF_ID';
      default:
        // 未知 reason，以 STAFF_SYSTEM_LOCKED 作為安全 fallback
        return 'STAFF_SYSTEM_LOCKED';
    }
  }

  // 招募成功：從候選池移除；hired roster 由 staff.ts 管理
  state.currentCandidates.splice(idx, 1);
  eventBus.emit('gacha:hired', { staffID: candidate.staffID, instanceId: result.instanceId });
  return 'SUCCESS';
}

/**
 * 不錄用候選人（玩家主動拒絕）。
 * 將候選卡從當前池移除；下次 refresh 時該 staffID 若未雇用則重新出現。
 *
 * @returns true 成功移除；false 找不到該 candidateID
 */
export function dismissCandidate(
  state: GachaState,
  candidateID: number
): boolean {
  const idx = state.currentCandidates.findIndex(c => c.candidateID === candidateID);
  if (idx === -1) return false;
  state.currentCandidates.splice(idx, 1);
  return true;
}

// ---------------------------------------------------------------------------
// 序列化
// ---------------------------------------------------------------------------

export function serialize(state: GachaState): SerializedGachaState {
  return {
    currentCandidates: [...state.currentCandidates],
    lastRefreshTimestamp: state.lastRefreshTimestamp,
    nextCandidateID: state.nextCandidateID,
  };
}

export function deserialize(data: SerializedGachaState): GachaState {
  return {
    currentCandidates: [...data.currentCandidates],
    lastRefreshTimestamp: data.lastRefreshTimestamp,
    nextCandidateID: data.nextCandidateID,
  };
}
