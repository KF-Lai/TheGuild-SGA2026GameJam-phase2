/**
 * C-06 World Danger System — Phase 2 Web 簡化版
 *
 * 設計來源：design/GDD/【C-06】world-danger-system.md
 *
 * 職責：
 *  - 管理全局世界危險度（E → D → C → B → A，單向遞增）
 *  - 升階條件：時間閘 + 進度閘（累積任務數）+ 陣營閘
 *  - 升階後主動推送 maxDebt（callback 注入式）並 emit 'danger:level_changed'
 *  - 對 FT-02 提供 getPoolWeights() 查詢任務池難度分布
 *
 * Phase 2 Web 簡化說明（相較 Unity C# 版）：
 *  - 無 MonoBehaviour；入口為 createWorldDangerState() + initialize()
 *  - F-01 DataManager 不存在 → 資料表直接 hardcode 於模組頂部
 *  - F-02 Time System → 使用 Date.now() 取得當前時間（毫秒轉秒）
 *  - F-03 SetBankruptcyThreshold → callback 注入式（setBankruptcyThresholdHandler()）
 *  - FT-09 Faction Story 停用 → onFactionScoreUpdated() API 保留，但 jam 階段不被呼叫
 *  - ISaveable Unity 介面 → 改為 serialize() / deserialize() 純函式
 */

import type { WorldDanger, Difficulty } from '../types';
import { eventBus } from '../core/events';

// ── 型別定義 ─────────────────────────────────────────────────────────────────

/** WorldDangerTable 單行資料（對應 GDD §3.1 欄位定義） */
export interface WorldDangerData {
  dangerLevel: WorldDanger;
  name: string;
  /** 時間閘：升至此階所需最低遊戲天數 */
  timeThreshold: number;
  /** 進度閘：累積接受任務數門檻 */
  missionCountReq: number;
  /** 進度閘：計入 acceptedMissionCount 的最低任務難度 */
  minDifficulty: Difficulty;
  /** 陣營閘：任意陣營累積分數需達到此值；0 = 不設此閘 */
  factionScoreReq: number;
  /** F~E 難度合計權重 */
  weightF_E: number;
  /** D 難度權重 */
  weightD: number;
  /** C 難度權重 */
  weightC: number;
  /** B 難度權重 */
  weightB: number;
  /** A 難度權重 */
  weightA: number;
  /** S~SSS 難度合計權重 */
  weightS_SSS: number;
  /** 負值；金幣不可低於此值（maxDebt 含義為最低金幣限制，為負數） */
  maxDebt: number;
}

/** 任務池難度分布權重（FT-02 Mission Dispatch 查詢用） */
export interface MissionPoolWeights {
  F_E: number;
  D: number;
  C: number;
  B: number;
  A: number;
  S_SSS: number;
}

/** C-06 Runtime 狀態（對應 GDD §3.2） */
export interface WorldDangerState {
  currentDangerLevel: WorldDanger;
  /** 朝下一階累積的接受任務數；升階後重置為 0 */
  acceptedMissionCount: number;
  /** 遊戲開始的 Unix timestamp（秒），時間閘基準 */
  gameStartTimestamp: number;
  /** FT-09 推送的任意陣營最高累積分數快取；jam 階段初始為 0 */
  cachedMaxFactionScore: number;
}

/** serialize() 產出的簡化序列化物件（對應 GDD §6.4） */
export interface SerializedWorldDanger {
  currentDangerLevel: WorldDanger;
  acceptedMissionCount: number;
  gameStartTimestamp: number;
  cachedMaxFactionScore: number;
}

// ── WorldDangerTable 資料表（GDD §3.1 Game Jam 初始資料，hardcode）────────────

/**
 * 資料表依危險度順序排列（E → A）。
 * 升階邏輯透過 DANGER_LEVEL_ORDER 索引找下一階，不依賴表格順序。
 */
const WORLD_DANGER_TABLE: WorldDangerData[] = [
  {
    dangerLevel: 'E',
    name: '和平',
    timeThreshold: 0,
    missionCountReq: 0,
    minDifficulty: 'F',
    factionScoreReq: 0,
    weightF_E: 40,
    weightD: 30,
    weightC: 20,
    weightB: 8,
    weightA: 2,
    weightS_SSS: 0,
    maxDebt: -100,
  },
  {
    dangerLevel: 'D',
    name: '動盪',
    timeThreshold: 1,
    missionCountReq: 10,
    minDifficulty: 'F',
    factionScoreReq: 0,
    weightF_E: 20,
    weightD: 35,
    weightC: 25,
    weightB: 15,
    weightA: 5,
    weightS_SSS: 0,
    maxDebt: -500,
  },
  {
    dangerLevel: 'C',
    name: '暗湧',
    timeThreshold: 3,
    missionCountReq: 15,
    minDifficulty: 'D',
    factionScoreReq: 5,
    weightF_E: 10,
    weightD: 20,
    weightC: 35,
    weightB: 25,
    weightA: 8,
    weightS_SSS: 2,
    maxDebt: -1000,
  },
  {
    dangerLevel: 'B',
    name: '危局',
    timeThreshold: 7,
    missionCountReq: 20,
    minDifficulty: 'C',
    factionScoreReq: 12,
    weightF_E: 5,
    weightD: 10,
    weightC: 25,
    weightB: 35,
    weightA: 20,
    weightS_SSS: 5,
    maxDebt: -2500,
  },
  {
    dangerLevel: 'A',
    name: '末世',
    timeThreshold: 14,
    missionCountReq: 25,
    minDifficulty: 'B',
    factionScoreReq: 20,
    weightF_E: 0,
    weightD: 5,
    weightC: 15,
    weightB: 30,
    weightA: 35,
    weightS_SSS: 15,
    maxDebt: -5000,
  },
];

// ── 危險度階序定義 ─────────────────────────────────────────────────────────────

/** 危險度固定升階順序（E 為起點，A 為最高） */
const DANGER_LEVEL_ORDER: WorldDanger[] = ['E', 'D', 'C', 'B', 'A'];

// ── 難度索引（對應 GDD §3.3 與 dispatch.ts 一致） ─────────────────────────────

/** 難度字串 → 數字索引；F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8 */
const DIFFICULTY_INDEX: Record<Difficulty, number> = {
  F: 0,
  E: 1,
  D: 2,
  C: 3,
  B: 4,
  A: 5,
  S: 6,
  SS: 7,
  SSS: 8,
};

// ── F-03 Bankruptcy Threshold Callback（注入式，非直接依賴）─────────────────────

/**
 * F-03 SetBankruptcyThreshold 的注入式 callback。
 * 由主程式呼叫 setBankruptcyThresholdHandler() 注入；
 * jam 階段若無人注入則僅 console.log 記錄當前 maxDebt。
 *
 * 延後項目：F-03 callback 未實際串接，待主程式（guild.ts / main.ts）注入。
 */
let _bankruptcyThresholdHandler: ((maxDebt: number) => void) | null = null;

/**
 * 注入 F-03 SetBankruptcyThreshold callback。
 * @param handler - 接受 maxDebt（負值）並寫入 F-03 的函式
 */
export function setBankruptcyThresholdHandler(
  handler: (maxDebt: number) => void
): void {
  _bankruptcyThresholdHandler = handler;
}

/** 內部呼叫：推送最新 maxDebt；若無 handler 則 console.log 記錄 */
function _pushMaxDebt(maxDebt: number): void {
  if (_bankruptcyThresholdHandler) {
    _bankruptcyThresholdHandler(maxDebt);
  } else {
    console.log(`[C-06] maxDebt 更新 → ${maxDebt}（F-03 handler 尚未注入）`);
  }
}

// ── 輔助函式 ──────────────────────────────────────────────────────────────────

/**
 * 依 dangerLevel 字串找資料表列。
 * 找不到回傳 null（§5.1 row 1 處理由呼叫端負責）。
 */
export function getDangerData(level: WorldDanger): WorldDangerData | null {
  return WORLD_DANGER_TABLE.find(row => row.dangerLevel === level) ?? null;
}

/**
 * 取得下一個危險度階。
 * 若已是最高階 A，回傳 null。
 */
function getNextDangerLevel(current: WorldDanger): WorldDanger | null {
  const idx = DANGER_LEVEL_ORDER.indexOf(current);
  if (idx === -1 || idx === DANGER_LEVEL_ORDER.length - 1) return null;
  return DANGER_LEVEL_ORDER[idx + 1];
}

/**
 * 計算遊戲經過天數（GDD §4.1 公式）。
 * 若 gameStartTimestamp 為 0（存檔損毀），console.error 後回傳 0（時間閘視為未滿足）。
 * @param gameStartTimestamp - 遊戲開始的 Unix 秒（非毫秒）
 */
function getElapsedDays(gameStartTimestamp: number): number {
  if (gameStartTimestamp === 0) {
    console.error('[C-06] gameStartTimestamp 未初始化（可能存檔損毀）。時間閘視為未滿足，回傳 0 天。');
    return 0;
  }
  const nowSec = Math.floor(Date.now() / 1000);
  return Math.floor((nowSec - gameStartTimestamp) / 86400);
}

/**
 * 取得難度字串對應的索引值。
 * 非合法難度值時 console.error 並視為 'F'（最寬鬆，對應 §5.1 row 4）。
 */
function _difficultyIndex(difficulty: string): number {
  if (difficulty in DIFFICULTY_INDEX) {
    return DIFFICULTY_INDEX[difficulty as Difficulty];
  }
  console.error(`[C-06] 非合法難度值：'${difficulty}'，視為 'F'（最寬鬆門檻）`);
  return DIFFICULTY_INDEX['F'];
}

// ── 公開 API ──────────────────────────────────────────────────────────────────

/**
 * 建立初始 WorldDangerState（對應 GDD §6.4 InitializeAsNewGame 預設值）。
 * gameStartTimestamp 預設為 0；呼叫 initialize() 後會以 Date.now()/1000 填入。
 */
export function createWorldDangerState(): WorldDangerState {
  return {
    currentDangerLevel: 'E',
    acceptedMissionCount: 0,
    gameStartTimestamp: 0,
    cachedMaxFactionScore: 0,
  };
}

/**
 * 啟動初始化：設定 gameStartTimestamp 並推送 E 階初始 maxDebt 給 F-03。
 * 對應 GDD §4.3（Unity Start 階段的行為）。
 *
 * @param state - WorldDangerState（會直接修改）
 * @param gameStartTimestamp - 遊戲開始的 Unix 秒；若未傳入則使用 Date.now()/1000
 */
export function initialize(
  state: WorldDangerState,
  gameStartTimestamp?: number
): void {
  // 若外部（save-load / 新遊戲）已設定 gameStartTimestamp，不覆蓋
  if (gameStartTimestamp !== undefined) {
    state.gameStartTimestamp = gameStartTimestamp;
  } else if (state.gameStartTimestamp === 0) {
    // 新遊戲且未提供 timestamp：以當前時間為遊戲起點
    state.gameStartTimestamp = Math.floor(Date.now() / 1000);
  }

  // 推送當前危險度的 maxDebt 至 F-03（§4.3）
  const maxDebt = getMaxDebt(state);
  _pushMaxDebt(maxDebt);
}

/**
 * 取得當前世界危險度。
 * 對應 GDD §3.4 GetCurrentLevel()。
 */
export function getCurrentLevel(state: WorldDangerState): WorldDanger {
  return state.currentDangerLevel;
}

/**
 * 取得當前危險度的 maxDebt（負值金幣下限）。
 * 找不到資料時 console.error 並回傳 E 階預設值 -100（§5.1 row 3 fallback）。
 */
export function getMaxDebt(state: WorldDangerState): number {
  const data = getDangerData(state.currentDangerLevel);
  if (!data) {
    console.error(`[C-06] getMaxDebt：找不到 dangerLevel='${state.currentDangerLevel}' 的資料列，使用 fallback -100`);
    return -100;
  }
  if (data.maxDebt === 0) {
    console.error(`[C-06] maxDebt 欄位為 0（dangerLevel='${state.currentDangerLevel}'），使用 fallback -100`);
    return -100;
  }
  return data.maxDebt;
}

/**
 * 取得當前危險度的任務池難度分布權重。
 * 所有權重欄位為 0 時 console.error 並回傳 E 階 fallback（§5.1 row 2）。
 */
export function getPoolWeights(state: WorldDangerState): MissionPoolWeights {
  const data = getDangerData(state.currentDangerLevel);

  const _buildWeights = (row: WorldDangerData): MissionPoolWeights => ({
    F_E: row.weightF_E,
    D: row.weightD,
    C: row.weightC,
    B: row.weightB,
    A: row.weightA,
    S_SSS: row.weightS_SSS,
  });

  const _isAllZero = (w: MissionPoolWeights): boolean =>
    w.F_E === 0 && w.D === 0 && w.C === 0 && w.B === 0 && w.A === 0 && w.S_SSS === 0;

  if (!data) {
    console.error(`[C-06] getPoolWeights：找不到 dangerLevel='${state.currentDangerLevel}'，使用 E 階 fallback`);
    const fallback = getDangerData('E')!;
    return _buildWeights(fallback);
  }

  const weights = _buildWeights(data);
  if (_isAllZero(weights)) {
    console.error(`[C-06] dangerLevel='${state.currentDangerLevel}' 的所有 weight 欄位為 0，使用 E 階 fallback`);
    const fallback = getDangerData('E')!;
    return _buildWeights(fallback);
  }

  return weights;
}

/**
 * FT-02 呼叫：任務被接受時通知 C-06 更新計數並檢查升階。
 * 對應 GDD §4.4 / §3.4 OnMissionAccepted()。
 *
 * 計數條件：任務難度索引 >= 下一階 minDifficulty 索引才計入。
 * 若當前已是最高階 A，直接 return（§5.2 row 5）。
 *
 * @param state - WorldDangerState（會直接修改）
 * @param difficulty - 任務難度字串（Difficulty type）
 */
export function onMissionAccepted(
  state: WorldDangerState,
  difficulty: Difficulty
): void {
  // 已最高階：無操作
  if (state.currentDangerLevel === 'A') return;

  const nextLevel = getNextDangerLevel(state.currentDangerLevel);
  if (!nextLevel) return;

  const nextData = getDangerData(nextLevel);
  if (!nextData) {
    // 缺漏階，呼叫 checkLevelUp 讓它處理跳過邏輯
    checkLevelUp(state);
    return;
  }

  const missionDiffIdx = _difficultyIndex(difficulty);
  const minDiffIdx = _difficultyIndex(nextData.minDifficulty);

  // 難度達門檻才計入進度閘
  if (missionDiffIdx >= minDiffIdx) {
    state.acceptedMissionCount++;
    checkLevelUp(state);
  }
}

/**
 * 升階檢查：遞迴處理，支援離線補算跨多階一次到位。
 * 對應 GDD §4.2 CheckLevelUp() 偽碼。
 *
 * 每次成功升階：
 *  1. currentDangerLevel 設為 nextLevel
 *  2. acceptedMissionCount 重置為 0
 *  3. 推送新 maxDebt 給 F-03（_pushMaxDebt）
 *  4. emit 'danger:level_changed' 事件（每次升階觸發一次）
 *  5. 遞迴呼叫自身（繼續檢查是否可連續升階）
 *
 * 已最高階（A）：直接 return（§5.2 row 5）。
 * 資料缺漏階：console.error，直接升至缺漏階繼續遞迴（§4.2 偽碼）。
 */
export function checkLevelUp(state: WorldDangerState): void {
  // 已最高階：無操作
  if (state.currentDangerLevel === 'A') return;

  const nextLevel = getNextDangerLevel(state.currentDangerLevel);
  if (!nextLevel) return;

  const data = getDangerData(nextLevel);
  if (!data) {
    // §4.2 缺漏階跳過分支：直接升至缺漏階，遞迴繼續
    console.error(`[C-06] checkLevelUp：資料表缺少 dangerLevel='${nextLevel}'，跳過此階繼續嘗試`);
    state.currentDangerLevel = nextLevel;
    checkLevelUp(state);
    return;
  }

  const elapsedDays = getElapsedDays(state.gameStartTimestamp);

  const timeOK = elapsedDays >= data.timeThreshold;
  const missionOK = state.acceptedMissionCount >= data.missionCountReq;
  const factionOK =
    data.factionScoreReq === 0 ||
    state.cachedMaxFactionScore >= data.factionScoreReq;

  if (timeOK && missionOK && factionOK) {
    // 升階成功
    state.currentDangerLevel = nextLevel;
    state.acceptedMissionCount = 0;

    // 推送新 maxDebt 至 F-03
    _pushMaxDebt(getMaxDebt(state));

    // 發布事件（每次升階觸發一次）
    eventBus.emit('danger:level_changed', { newLevel: nextLevel });

    // 遞迴：繼續檢查是否可連續升階（離線補算）
    checkLevelUp(state);
  }
}

/**
 * FT-09 呼叫：陣營分數更新時推送最新最高分並立即嘗試升階。
 * 對應 GDD §4.5 OnFactionScoreUpdated()。
 *
 * jam 階段 FT-09 停用，此 API 保留介面但不會被呼叫。
 * 若當前已是最高階 A，直接 return（§5.2 row 5）。
 *
 * @param state - WorldDangerState（會直接修改）
 * @param newMaxScore - 任意陣營的最高累積分數（由 FT-09 計算後傳入）
 */
export function onFactionScoreUpdated(
  state: WorldDangerState,
  newMaxScore: number
): void {
  // 已最高階：無操作（§4.5 偽碼 + §5.2 row 5）
  if (state.currentDangerLevel === 'A') return;

  state.cachedMaxFactionScore = newMaxScore;
  checkLevelUp(state);
}

// ── 序列化 / 反序列化（對應 GDD §6.4 ISaveable 簡化版）──────────────────────────

/**
 * 將 WorldDangerState 序列化為可 JSON.stringify 的純物件。
 * 對應 GDD §6.4 Serialize() — 序列化欄位：currentDangerLevel / acceptedMissionCount /
 * gameStartTimestamp / cachedMaxFactionScore。
 */
export function serialize(state: WorldDangerState): SerializedWorldDanger {
  return {
    currentDangerLevel: state.currentDangerLevel,
    acceptedMissionCount: state.acceptedMissionCount,
    gameStartTimestamp: state.gameStartTimestamp,
    cachedMaxFactionScore: state.cachedMaxFactionScore,
  };
}

/**
 * 從序列化資料還原 WorldDangerState。
 * 對應 GDD §6.4 RestoreFromSave() — 驗證 currentDangerLevel 合法性。
 *
 * 驗證失敗（非法 dangerLevel）：console.error 後回傳全新 E 階狀態（Degradable 策略）。
 * 還原成功後，呼叫端應接著呼叫 initialize() 以推送正確 maxDebt 至 F-03。
 *
 * @param data - SerializedWorldDanger（通常來自 JSON.parse）
 */
export function deserialize(data: SerializedWorldDanger): WorldDangerState {
  const validLevels: WorldDanger[] = ['E', 'D', 'C', 'B', 'A'];

  if (!validLevels.includes(data.currentDangerLevel)) {
    console.error(
      `[C-06] deserialize：非法 currentDangerLevel='${data.currentDangerLevel}'，` +
      '重置為初始狀態（Degradable 策略）'
    );
    return createWorldDangerState();
  }

  return {
    currentDangerLevel: data.currentDangerLevel,
    acceptedMissionCount: data.acceptedMissionCount ?? 0,
    gameStartTimestamp: data.gameStartTimestamp ?? 0,
    cachedMaxFactionScore: data.cachedMaxFactionScore ?? 0,
  };
}
