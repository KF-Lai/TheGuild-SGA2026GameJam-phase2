import type { OutcomeType, GuildLevel, WorldDanger } from '../types';

// ---- 事件 Payload 定義 ----

export interface EventMap {
  'mission:completed': {
    dispatchId: string;
    outcome: OutcomeType;
    goldDelta: number;
    reputationDelta: number;
  };
  'adventurer:died': {
    adventurerId: string;
    adventurerName: string;
    missionId: string;
  };
  'adventurer:wounded': {
    adventurerId: string;
    woundedUntil: number;
  };
  'gold:changed': {
    newValue: number;
    delta: number;
  };
  'reputation:changed': {
    newValue: number;
    delta: number;
  };
  'guild:level_up': {
    newLevel: GuildLevel;
  };
  'danger:level_changed': {
    newLevel: WorldDanger;
  };
  'staff:hired': {
    staffId: number;
    instanceId: string;
  };
  'staff:fired': {
    instanceId: string;
    staffID: number;
  };
  'staff:assigned': {
    instanceId: string;
    oldBuildingID: number;
    newBuildingID: number;
  };
  'tick:minute': {
    timestamp: number;
  };
  // FT-01 Recruitment
  'recruit:pool_refreshed': {
    /** 'auto' | 'manual_free' | 'manual_paid' */
    source: 'auto' | 'manual_free' | 'manual_paid';
  };
  'recruit:success': {
    adventurerId: string;
    source: 'rookie' | 'veteran';
  };
  // FT-03 NPC Decision
  'npc:auto_pickup': {
    adventurerId: string;
    missionId: string;
  };
  // FT-07 Guild Building
  'building:upgraded': {
    buildingID: number;
    fromLevel: number;
    toLevel: number;
  };
  // FT-08 Gacha
  'gacha:refreshed': {
    /** 刷新來源 */
    source: 'auto' | 'manual_paid' | 'initial';
    /** 刷新後候選池大小 */
    count: number;
  };
  'gacha:hired': {
    staffID: number;
    instanceId: string;
  };
  // FT-05 Guild Gold Flow
  'commission:prepaid': {
    missionId: string;
    prepaidAmount: number;
  };
  'commission:settled': {
    missionId: string;
    netDelta: number;
    /** CommissionBreakdown — subscriber cast to import('../systems/gold-flow').CommissionBreakdown */
    breakdown: unknown;
  };
  'maintenance:charged': {
    totalAmount: number;
    /** MaintenanceBreakdown — subscriber cast to import('../systems/gold-flow').MaintenanceBreakdown */
    breakdown: unknown;
  };
  'salary:charged': {
    totalAmount: number;
    /** SalaryBreakdown — subscriber cast to import('../systems/gold-flow').SalaryBreakdown */
    breakdown: unknown;
  };
}

// ---- EventBus 實作 ----

type Handler<T> = (payload: T) => void;
// 內部存儲用 unknown 化 handler，避免 TS 對 K-distributed indexer 的型別限制
// （public API 仍然透過 generic K 維持型別安全）
type AnyHandler = Handler<unknown>;

class EventBus {
  private _listeners: Map<keyof EventMap, Set<AnyHandler>> = new Map();

  on<K extends keyof EventMap>(event: K, handler: Handler<EventMap[K]>): void {
    let bucket = this._listeners.get(event);
    if (!bucket) {
      bucket = new Set();
      this._listeners.set(event, bucket);
    }
    bucket.add(handler as AnyHandler);
  }

  off<K extends keyof EventMap>(event: K, handler: Handler<EventMap[K]>): void {
    this._listeners.get(event)?.delete(handler as AnyHandler);
  }

  emit<K extends keyof EventMap>(event: K, payload: EventMap[K]): void {
    const bucket = this._listeners.get(event);
    if (!bucket) return;
    // 複製 Set 防止 handler 內部呼叫 off 導致迭代異常
    for (const handler of Array.from(bucket)) {
      (handler as Handler<EventMap[K]>)(payload);
    }
  }
}

export const eventBus = new EventBus();
