import type { OutcomeType } from '../types';

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
  'gold:changed': {
    newValue: number;
    delta: number;
  };
  'reputation:changed': {
    newValue: number;
    delta: number;
  };
  'guild:level_up': {
    newLevel: number;
  };
  'danger:level_changed': {
    newLevel: number;
  };
  'staff:hired': {
    staffId: number;
    instanceId: string;
  };
  'tick:minute': {
    timestamp: number;
  };
}

// ---- EventBus 實作 ----

type Handler<T> = (payload: T) => void;

class EventBus {
  private _listeners: {
    [K in keyof EventMap]?: Set<Handler<EventMap[K]>>;
  } = {};

  on<K extends keyof EventMap>(event: K, handler: Handler<EventMap[K]>): void {
    let bucket = this._listeners[event];
    if (!bucket) {
      bucket = new Set();
      this._listeners[event] = bucket;
    }
    bucket.add(handler);
  }

  off<K extends keyof EventMap>(event: K, handler: Handler<EventMap[K]>): void {
    this._listeners[event]?.delete(handler);
  }

  emit<K extends keyof EventMap>(event: K, payload: EventMap[K]): void {
    const bucket = this._listeners[event];
    if (!bucket) return;
    // 複製 Set 防止 handler 內部呼叫 off 導致迭代異常
    for (const handler of Array.from(bucket)) {
      handler(payload);
    }
  }
}

export const eventBus = new EventBus();
