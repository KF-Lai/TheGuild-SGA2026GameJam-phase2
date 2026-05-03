/**
 * FT-12 Staff System — Jam 簡化版
 * 三角色 hardcode（501 米拉 / 502 譚恩 / 503 凱拉），全程 Working，無三態狀態機。
 * 加成聚合：Passive 效果全 roster 計入；Slot 效果需 assignedBuildingID 吻合。
 */

import { eventBus } from '../core/events';

// ---------------------------------------------------------------------------
// 型別
// ---------------------------------------------------------------------------

/** staff.ts 內部使用的 effect ID（PascalCase，對齊 GDD §3.4） */
export type StaffEffectId =
  | 'Willingness'               // Passive：影響 NPC 接單意願
  | 'AccountantCommission'      // Passive：傭金率加成
  | 'AccountantPenaltyOnVault'  // Slot：賠償率減免（指派 buildingID=5）
  | 'RecruitRefreshOnCounter';  // Slot：招募刷新減量（指派 buildingID=4）

/** UI flag ID */
export type StaffUIFlagId =
  | 'SuccessRatePreview'; // UI flag：委託審核 UI 顯示成功率預估（指派 buildingID=1）

/**
 * 職員 instance 格式（多 effect 支援）。
 * types/index.ts 的 StaffInstance 為 phase 1 單 effect 殘留，此處定義新格式。
 */
export interface StaffInstanceFull {
  instanceId: string;
  staffID: number;
  name: string;
  rarity: number;
  /** Jam 簡化：所有職員固定 Working 狀態 */
  isWorking: true;
  /** 指派的建築 ID；0 = 未指派（不應出現在 Jam 場景，但 unassign 時允許） */
  assignedBuildingID: number;
  hiredTimestamp: number;
}

export interface StaffRosterState {
  roster: StaffInstanceFull[];
  nextInstanceCounter: number;
  lastSalaryTimestamp: number;
}

export type HireResult =
  | { ok: true; instanceId: string }
  | { ok: false; reason: 'STAFF_SYSTEM_LOCKED' | 'INVALID_STAFF_ID' | 'BUILDING_NOT_ELIGIBLE' | 'BUILDING_FULL' };

export type AssignResult =
  | 'SUCCESS'
  | 'STAFF_NOT_FOUND'
  | 'BUILDING_NOT_ELIGIBLE'
  | 'BUILDING_FULL'
  | 'STAFF_SYSTEM_LOCKED';

export type FireResult =
  | 'SUCCESS'
  | 'STAFF_NOT_FOUND'
  | 'GOLD_INSUFFICIENT'
  | 'STAFF_SYSTEM_LOCKED';

/** 跨系統 dep 注入介面 */
export interface StaffSystemDeps {
  /** FT-07：Staff System 是否已解鎖 */
  isUnlocked: () => boolean;
  /** FT-07：查詢建築的職員槽位數量 */
  getBuildingSlotCount: (buildingID: number) => number;
  /** F-03：嚴格扣款（解雇資遣費用） */
  spendGold: (amount: number) => boolean;
}

/** 序列化格式 */
export interface SerializedStaffRoster {
  roster: {
    instanceId: string;
    staffID: number;
    assignedBuildingID: number;
    hiredTimestamp: number;
  }[];
  nextInstanceCounter: number;
  lastSalaryTimestamp: number;
}

// ---------------------------------------------------------------------------
// 三角色資料表（hardcode）
// ---------------------------------------------------------------------------

interface StaffTableRow {
  staffID: number;
  name: string;
  rarity: number;
  salary: number;
  severancePay: number;
  effectIDs: StaffEffectId[];
  effectValues: number[];
  /** 此職員可指派的建築 ID 清單（合法 slot） */
  slotBuildingIDs: number[];
  uiFlagIDs: StaffUIFlagId[];
  /** uiFlagIDs[i] 對應需要指派的 buildingID */
  uiFlagBuildingIDs: number[];
}

const STAFF_TABLE: Record<number, StaffTableRow> = {
  501: {
    staffID: 501,
    name: '米拉',
    rarity: 3,
    salary: 30,
    severancePay: 80,
    effectIDs: ['Willingness'],
    effectValues: [0.05],
    slotBuildingIDs: [1],          // 委託板
    uiFlagIDs: ['SuccessRatePreview'],
    uiFlagBuildingIDs: [1],
  },
  502: {
    staffID: 502,
    name: '譚恩',
    rarity: 4,
    salary: 40,
    severancePay: 120,
    effectIDs: ['AccountantCommission', 'AccountantPenaltyOnVault'],
    effectValues: [0.02, -0.02],
    slotBuildingIDs: [5],          // 預備金保險櫃
    uiFlagIDs: [],
    uiFlagBuildingIDs: [],
  },
  503: {
    staffID: 503,
    name: '凱拉',
    rarity: 3,
    salary: 30,
    severancePay: 80,
    effectIDs: ['RecruitRefreshOnCounter'],
    effectValues: [7200],
    slotBuildingIDs: [4],          // 公會櫃臺
    uiFlagIDs: [],
    uiFlagBuildingIDs: [],
  },
};

// ---------------------------------------------------------------------------
// 加成上限常數
// ---------------------------------------------------------------------------

const EFFECT_MAX_WILLINGNESS_BONUS = 0.20;
const EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS = 0.10;
const EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS = -0.10;   // 負值下限（floor cap）
const EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC = 14400; // 4h = 14400 秒

// ---------------------------------------------------------------------------
// 注入式依賴與 handler（module-scoped，非 runtime state）
// ---------------------------------------------------------------------------

let _deps: StaffSystemDeps | null = null;
let _salaryChargeHandler: ((items: Record<string, number>, dueTimestamp: number) => void) | null = null;

export function setSystemDeps(deps: StaffSystemDeps): void {
  _deps = deps;
}

export function setSalaryChargeHandler(
  fn: (items: Record<string, number>, dueTimestamp: number) => void
): void {
  _salaryChargeHandler = fn;
}

// ---------------------------------------------------------------------------
// 初始狀態
// ---------------------------------------------------------------------------

export function createStaffRosterState(): StaffRosterState {
  return {
    roster: [],
    nextInstanceCounter: 1,
    lastSalaryTimestamp: 0,
  };
}

// ---------------------------------------------------------------------------
// 內部工具
// ---------------------------------------------------------------------------

/** 計算指定建築上目前已指派的職員數（不含 assignedBuildingID=0） */
function countStaffOnBuilding(state: StaffRosterState, buildingID: number): number {
  return state.roster.filter(s => s.assignedBuildingID === buildingID).length;
}

/** 驗證建築 ID 對此 staffID 是否合法 */
function isBuildingEligible(staffID: number, buildingID: number): boolean {
  return STAFF_TABLE[staffID]?.slotBuildingIDs.includes(buildingID) ?? false;
}

/** 生成新的 instanceId */
function genInstanceId(state: StaffRosterState): string {
  return `staff_${state.nextInstanceCounter++}`;
}

// ---------------------------------------------------------------------------
// 公開 API — 人事操作
// ---------------------------------------------------------------------------

/**
 * 招募職員。
 * Jam 簡化：必須同步傳入 buildingID，入職即 Working 並完成指派。
 */
export function hireStaff(
  state: StaffRosterState,
  candidate: { staffID: number },
  buildingID: number
): HireResult {
  if (!_deps?.isUnlocked()) {
    return { ok: false, reason: 'STAFF_SYSTEM_LOCKED' };
  }

  const tbl = STAFF_TABLE[candidate.staffID];
  if (!tbl) {
    console.error(`[StaffSystem] hireStaff: 未知 staffID=${candidate.staffID}`);
    return { ok: false, reason: 'INVALID_STAFF_ID' };
  }

  if (!isBuildingEligible(candidate.staffID, buildingID)) {
    return { ok: false, reason: 'BUILDING_NOT_ELIGIBLE' };
  }

  const slotCount = _deps.getBuildingSlotCount(buildingID);
  if (countStaffOnBuilding(state, buildingID) >= slotCount) {
    return { ok: false, reason: 'BUILDING_FULL' };
  }

  const instanceId = genInstanceId(state);
  const instance: StaffInstanceFull = {
    instanceId,
    staffID: candidate.staffID,
    name: tbl.name,
    rarity: tbl.rarity,
    isWorking: true,
    assignedBuildingID: buildingID,
    hiredTimestamp: Date.now(),
  };

  state.roster.push(instance);
  eventBus.emit('staff:hired', { staffId: candidate.staffID, instanceId });
  return { ok: true, instanceId };
}

/**
 * 切換職員指派建築。
 * Jam 簡化：無冷卻，立即切換。
 */
export function tryAssignStaff(
  state: StaffRosterState,
  instanceId: string,
  buildingID: number
): AssignResult {
  if (!_deps?.isUnlocked()) return 'STAFF_SYSTEM_LOCKED';

  const staff = state.roster.find(s => s.instanceId === instanceId);
  if (!staff) return 'STAFF_NOT_FOUND';

  if (!isBuildingEligible(staff.staffID, buildingID)) return 'BUILDING_NOT_ELIGIBLE';

  // 計算新建築目前人數（排除自身）
  const othersOnBuilding = state.roster.filter(
    s => s.instanceId !== instanceId && s.assignedBuildingID === buildingID
  ).length;
  const slotCount = _deps.getBuildingSlotCount(buildingID);
  if (othersOnBuilding >= slotCount) return 'BUILDING_FULL';

  const oldBuildingID = staff.assignedBuildingID;
  staff.assignedBuildingID = buildingID;
  eventBus.emit('staff:assigned', { instanceId, oldBuildingID, newBuildingID: buildingID });
  return 'SUCCESS';
}

/**
 * 取消指派（允許，但 effect 自動失效，因聚合條件不符）。
 * assignedBuildingID 設為 0。
 */
export function tryUnassignStaff(
  state: StaffRosterState,
  instanceId: string
): AssignResult {
  if (!_deps?.isUnlocked()) return 'STAFF_SYSTEM_LOCKED';

  const staff = state.roster.find(s => s.instanceId === instanceId);
  if (!staff) return 'STAFF_NOT_FOUND';

  const oldBuildingID = staff.assignedBuildingID;
  staff.assignedBuildingID = 0;
  eventBus.emit('staff:assigned', { instanceId, oldBuildingID, newBuildingID: 0 });
  return 'SUCCESS';
}

/**
 * 解雇職員（扣資遣費 + roster 移除）。
 */
export function tryFireStaff(
  state: StaffRosterState,
  instanceId: string
): FireResult {
  if (!_deps?.isUnlocked()) return 'STAFF_SYSTEM_LOCKED';

  const idx = state.roster.findIndex(s => s.instanceId === instanceId);
  if (idx === -1) return 'STAFF_NOT_FOUND';

  const staff = state.roster[idx];
  const tbl = STAFF_TABLE[staff.staffID];
  if (!_deps.spendGold(tbl.severancePay)) return 'GOLD_INSUFFICIENT';

  state.roster.splice(idx, 1);
  eventBus.emit('staff:fired', { instanceId, staffID: staff.staffID });
  return 'SUCCESS';
}

// ---------------------------------------------------------------------------
// 公開 API — 查詢
// ---------------------------------------------------------------------------

export function getRoster(state: StaffRosterState): readonly StaffInstanceFull[] {
  return state.roster;
}

/** 查名冊內是否有任一 instance 持有此 staffID */
export function isStaffHired(state: StaffRosterState, staffID: number): boolean {
  return state.roster.some(s => s.staffID === staffID);
}

// ---------------------------------------------------------------------------
// 加成聚合 API
// ---------------------------------------------------------------------------

/**
 * NPC 接單意願加成（Passive — 全 roster 計入，不受 buildingID 限制）。
 * 系統未解鎖回 0。
 */
export function getStaffWillingnessBonus(state: StaffRosterState): number {
  if (!_deps?.isUnlocked()) return 0;

  let total = 0;
  for (const staff of state.roster) {
    const tbl = STAFF_TABLE[staff.staffID];
    if (!tbl) continue;
    tbl.effectIDs.forEach((eid, i) => {
      if (eid === 'Willingness') total += tbl.effectValues[i];
    });
  }
  return Math.min(total, EFFECT_MAX_WILLINGNESS_BONUS);
}

/**
 * 傭金率加成（Passive — 全 roster 計入）。
 * 系統未解鎖回 0。
 */
export function getAccountantCommissionBonus(state: StaffRosterState): number {
  if (!_deps?.isUnlocked()) return 0;

  let total = 0;
  for (const staff of state.roster) {
    const tbl = STAFF_TABLE[staff.staffID];
    if (!tbl) continue;
    tbl.effectIDs.forEach((eid, i) => {
      if (eid === 'AccountantCommission') total += tbl.effectValues[i];
    });
  }
  return Math.min(total, EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS);
}

/**
 * 賠償率減免（Slot — 需 assignedBuildingID = 5，負值，下限 floor cap）。
 * 系統未解鎖回 0。
 */
export function getAccountantPenaltyBonus(state: StaffRosterState): number {
  if (!_deps?.isUnlocked()) return 0;

  let total = 0;
  for (const staff of state.roster) {
    if (staff.assignedBuildingID !== 5) continue;
    const tbl = STAFF_TABLE[staff.staffID];
    if (!tbl) continue;
    tbl.effectIDs.forEach((eid, i) => {
      if (eid === 'AccountantPenaltyOnVault') total += tbl.effectValues[i];
    });
  }
  // 負值下限：不能低於 floor cap（即 total 比 floor cap 更負時，取 floor cap）
  return Math.max(total, EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS);
}

/**
 * 招募刷新時間減量秒數（Slot — 需 assignedBuildingID = 4）。
 * 系統未解鎖回 0。
 */
export function getRecruitRefreshReductionSec(state: StaffRosterState): number {
  if (!_deps?.isUnlocked()) return 0;

  let total = 0;
  for (const staff of state.roster) {
    if (staff.assignedBuildingID !== 4) continue;
    const tbl = STAFF_TABLE[staff.staffID];
    if (!tbl) continue;
    tbl.effectIDs.forEach((eid, i) => {
      if (eid === 'RecruitRefreshOnCounter') total += tbl.effectValues[i];
    });
  }
  return Math.min(total, EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC);
}

/**
 * 委託審核 UI 是否顯示成功率預估（UI flag — OR 聚合，需 assignedBuildingID = 1）。
 * 系統未解鎖回 false。
 */
export function isSuccessRatePreviewEnabled(state: StaffRosterState): boolean {
  if (!_deps?.isUnlocked()) return false;

  for (const staff of state.roster) {
    const tbl = STAFF_TABLE[staff.staffID];
    if (!tbl) continue;
    for (let i = 0; i < tbl.uiFlagIDs.length; i++) {
      if (
        tbl.uiFlagIDs[i] === 'SuccessRatePreview' &&
        staff.assignedBuildingID === tbl.uiFlagBuildingIDs[i]
      ) {
        return true;
      }
    }
  }
  return false;
}

// ---------------------------------------------------------------------------
// 薪水管線（Jam 不主動觸發，呼叫端決定時機）
// ---------------------------------------------------------------------------

/**
 * 計算當日薪水並呼叫注入的 salary handler。
 * 系統未解鎖或 handler 未注入則直接回傳。
 */
export function triggerDailySalary(
  state: StaffRosterState,
  now: number = Date.now()
): void {
  if (!_deps?.isUnlocked() || !_salaryChargeHandler) return;

  const items: Record<string, number> = {};
  for (const staff of state.roster) {
    const tbl = STAFF_TABLE[staff.staffID];
    if (tbl) items[staff.instanceId] = tbl.salary;
  }
  if (Object.keys(items).length === 0) return;

  _salaryChargeHandler(items, now);
  state.lastSalaryTimestamp = now;
}

// ---------------------------------------------------------------------------
// 序列化
// ---------------------------------------------------------------------------

export function serialize(state: StaffRosterState): SerializedStaffRoster {
  return {
    roster: state.roster.map(s => ({
      instanceId: s.instanceId,
      staffID: s.staffID,
      assignedBuildingID: s.assignedBuildingID,
      hiredTimestamp: s.hiredTimestamp,
    })),
    nextInstanceCounter: state.nextInstanceCounter,
    lastSalaryTimestamp: state.lastSalaryTimestamp,
  };
}

export function deserialize(data: SerializedStaffRoster): StaffRosterState {
  const roster: StaffInstanceFull[] = data.roster.map(r => {
    const tbl = STAFF_TABLE[r.staffID];
    return {
      instanceId: r.instanceId,
      staffID: r.staffID,
      name: tbl?.name ?? `Unknown(${r.staffID})`,
      rarity: tbl?.rarity ?? 1,
      isWorking: true as const,
      assignedBuildingID: r.assignedBuildingID,
      hiredTimestamp: r.hiredTimestamp,
    };
  });

  return {
    roster,
    nextInstanceCounter: data.nextInstanceCounter,
    lastSalaryTimestamp: data.lastSalaryTimestamp,
  };
}
