# Phase 2 Web — 技術規格書

_建立：2026-05-03 | 範疇：Game Jam Phase 2 Web 移植版（TS + Vite）_
_來源：phase1_html_demo → phase2_web/_

---

## 0. 文件目的

本文件固化 Phase 2 Web 移植期間（Stage 0 ~ Stage 6）已實質採用或拍板的設計決策，作為：

- 跨 Stage subagent 實作的標準 context
- main.ts 整合（Stage 6.1）的接口契約
- Post-Jam 重構的決策原點

GDD（`design/GDD/`）為遊戲設計層真相來源；本文件為 **Phase 2 Web 移植層** 的工程約定，遇衝突時以本文件為準（已標明 Jam 範疇縮減）。

---

## 1. 技術棧與目錄結構

| 項目 | 選用 |
|---|---|
| 語言 / 編譯 | TypeScript 5.x（strict mode）+ Vite |
| UI | Vanilla DOM（無框架）|
| 持久化 | IndexedDB（DB: `the-guild-db`，store: `saves`）|
| 部署 | Netlify（`netlify.toml` 沿用 phase 1）|

### 目錄

```
phase2_web/
├── index.html                  // 入口（不動）
├── tsconfig.json               // TS 設定（不動）
├── netlify.toml                // 部署（不動）
├── package.json                // 僅改 name
└── src/
    ├── core/
    │   └── events.ts           // EventBus + EventMap
    ├── data/
    │   ├── constants.ts        // SystemConstants（取代 F-01 CSV）
    │   ├── traits.ts           // C-03 Profession（內嵌）
    │   ├── races.ts            // C-04 Race（沿用 phase 1）
    │   ├── growth-traits.ts    // C-05 Trait（沿用）
    │   ├── bios.ts             // 角色 bio 文本
    │   └── missions.ts         // 任務硬編碼
    ├── systems/
    │   ├── resource.ts         // F-03 Resource（沿用）
    │   ├── tick.ts             // F-02 簡化（沿用）
    │   ├── offline-return.ts   // FT-11 簡化（沿用）
    │   ├── adventurer.ts       // C-02
    │   ├── dispatch.ts         // FT-02
    │   ├── outcome.ts          // FT-04
    │   ├── save-load.ts        // FT-10（IndexedDB）
    │   ├── guild.ts            // FT-06
    │   ├── world-danger.ts     // C-06
    │   ├── recruitment.ts      // FT-01
    │   ├── npc-decision.ts     // FT-03
    │   ├── building.ts         // FT-07
    │   └── gold-flow.ts        // FT-05（部分 dead code，見 §6）
    ├── ui/
    │   ├── shell.ts            // 主框架
    │   ├── guild-hall-scene.ts // 場景
    │   ├── main-menu.ts        // 主選單
    │   └── cheat-menu.ts       // debug
    ├── styles/
    │   └── main.css
    ├── types/
    │   └── index.ts            // 共享型別
    └── main.ts                 // 整合入口（Stage 6.1 才建立）
```

---

## 2. 模組規範（強制）

### 2.1 純函式 + state-passing 模式

所有 systems 採以下結構，**禁用 class、禁用 module-scoped runtime state（注入式 deps / handler 例外）**：

```typescript
// 1. 公開型別
export interface XxxState { ... }

// 2. 建立初始 state
export function createXxxState(): XxxState { ... }

// 3. 操作函式（接受 state，直接 mutate 或回傳新值）
export function doSomething(state: XxxState, ...args): Result { ... }

// 4. 序列化對稱
export function serialize(state: XxxState): SerializedXxxState { ... }
export function deserialize(data: SerializedXxxState): XxxState { ... }
```

### 2.2 跨系統 dep 注入

**禁止 system 直接 import 另一 system 的 runtime mutable 函式**（避免循環依賴與測試難度）。改採 callback 注入：

```typescript
export interface XxxResourceDeps {
  getGold: () => number
  addGold: (delta: number) => boolean
  // ...
}

let _deps: XxxResourceDeps | null = null

export function setResourceHandlers(deps: XxxResourceDeps): void {
  _deps = deps
}
```

例外：**static 工具函式 / 常數**（`getMaxDifficulty(level)` / `PROFESSION_TRAITS` 等）可以直接 import。

### 2.3 EventBus 事件命名

格式：`'<domain>:<verb>'`（kebab-case，domain 為 system 簡稱或核心概念）

| 已使用 domain | 例子 |
|---|---|
| `mission:` | `mission:completed` |
| `adventurer:` | `adventurer:died` / `adventurer:wounded` |
| `gold:` / `reputation:` | `gold:changed` / `reputation:changed` |
| `guild:` / `danger:` | `guild:level_up` / `danger:level_changed` |
| `recruit:` | `recruit:pool_refreshed` / `recruit:success` |
| `npc:` | `npc:auto_pickup` |
| `building:` | `building:upgraded` |
| `commission:` / `maintenance:` / `salary:` | gold-flow 三類金流事件 |
| `staff:` / `tick:` | 預留 |

**新增事件規則**：
- 加入 `EventMap` 時必須含完整 payload type
- breakdown 之類大型結構在 EventMap 中用 `unknown` 避免循環 import，subscriber 自行 cast

---

## 3. Jam 範疇縮減清單

下列系統 / 機制在 Phase 2 Web Jam 階段**停用或降級**。對應 GDD 條目仍保留 schema 完整性，但 runtime 行為不啟用：

| GDD ID | 範疇縮減 | 處理方式 |
|---|---|---|
| F-01 DataManager | 完全砍掉 | 所有資料表硬編碼於 `.ts` 模組頂部 |
| F-02 Time System | 砍 OnMinuteTick / OnDailyReset 訂閱 | 改用 `Date.now()` + 外部 tick 函式 |
| C-04 race trait | 完全停用 | RACE_MODIFIERS 所有 success/death modifier 改 0；UI（5.2 / 5.4）移除種族顯示；adventurer.raceId 欄位仍保留供 fallback |
| C-05 behavior trait | 暫無資料 | `getBehaviorWillingnessDelta` 永遠 return 0（stub） |
| FT-08 Gacha | 簡化版 | 三角色固定（米拉 501 / 譚恩 502 / 凱拉 503），無保底、無垃圾物品 |
| FT-09 Faction Story | 完全停用 | `OnFactionScoreUpdated` 為 stub，`cachedMaxFactionScore` 恆 0 |
| FT-12 Staff | 簡化版 | 三角色，無 OnLeave 三態，全程 Working；加成查詢 stub return 0 |
| FT-07 維護費 | Phase 2 規格 / Jam no-op | `BuildingTable.maintenanceCost` 欄位保留 schema，不訂閱 OnDailyReset |
| FT-12 薪水 | 同上 | `gold-flow.chargeSalary` 完整實作但不被觸發 |
| P-01 Desktop Window | 完全砍掉 | 不實作 |
| P-03 Notification | 簡化版 | 用 phase 1 既有 log/toast；不實作完整通知系統 |
| 美術 | 部分 | Stage 6.2 才置入，部分 panel 用 Unity 內建 / minimal styling |

---

## 4. 金流模型決策（拍板：路徑 A）

### 4.1 採用 phase 1 既有「預收 / 退款」三步金流

**流程**：

```
1. 玩家審核委託（onReviewAccept）
     → addGold(+baseReward)         // 預收（押金入帳）
     → mission 加入 missionPool
2. 派遣（tryDispatch）
     → 不動金流；preCollectedAmount 僅 snapshot
3. 結算（resolveOutcome）
     → goldDelta 計算於 outcome.ts
     → SUCCESS: -floor(baseReward × (1 - COMMISSION_RATE))
     → FAILURE: -(baseReward + floor(baseReward × PENALTY_RATE))
     → main.ts:96 套用 addGold(state.resources, settlement.goldDelta)
4. 委託過期未接（在 missionPool 超時）
     → addGold(-baseReward)         // 退還客戶
```

**公會總帳淨變化**（B 難度 600g 為例）：

| 結果 | 預收 | 結算 | 淨值 |
|---|---|---|---|
| SUCCESS | +600 | -480 | **+120**（傭金 20%） |
| FAILURE | +600 | -660 | **-60**（賠款 10%） |

### 4.2 gold-flow.ts 的部分 dead code 處理

`gold-flow.ts` 已實作的 `prepayCommission` / `computeCommissionNet` **Jam 階段不被呼叫**（路徑 A 走 outcome.ts）。但保留下列價值：

- `CommissionBreakdown` / `MaintenanceBreakdown` / `SalaryBreakdown` 型別 → P-03 通知 / FT-10 存檔可用
- `chargeMaintenance` / `chargeSalary` → FT-07 / FT-12 整合時直接呼叫
- `BonusHandlers` 注入機制 → FT-12 加成接點預留
- `ExecuteGoldFlow` 共用機制（goldBefore / goldAfter / bankruptcyState 快照） → 維護費 / 薪水管線使用

### 4.3 Post-Jam 重構選項（路徑 C，不在 Jam 範疇）

若 Post-Jam 要把金流中央化，把 outcome.ts 的 `goldDelta` 計算移到 `gold-flow.computeCommissionNet`，並把 `computeCommissionNet` 的數學改為「結算時退還非佣金部分」（對齊 phase 1 模型），不採 GDD §3.5 字面公式（避免「失敗仍賺 +540」的 fantasy 衝突）。需同步補 GDD 註記。

---

## 5. 整合骨架（Stage 6.1 main.ts 約定）

### 5.1 AppState 集合

`main.ts` 持有所有 system state，注入 deps 給各 system：

```typescript
interface AppState {
  guild:           GuildState           // 含 resources / adventurers / missionPool / activeMissions / completedMissionIds / guildLevel / worldDanger
  outcomeState:    OutcomeState
  worldDanger:     WorldDangerState
  recruitment:     RecruitmentState
  buildings:       BuildingState[]
  autoPickup:      AutoPickupTracker
}
```

### 5.2 Bootstrap 順序

```
1. createXxxState() × 6（或從 save-load.ts deserialize）
2. setXxxHandlers(deps) 注入：
   - world-danger.setBankruptcyThresholdHandler(maxDebt => /* TODO F-03 整合 */)
   - building.setResourceHandlers({ getGold, addGold, getGuildLevel })
   - building.setBankruptcyWarningHandler(seconds => /* TODO F-03 整合 */)
   - recruitment.setResourceHandlers({ canAfford, addGold, getReputation })
   - gold-flow.setResourceHandlers({ getGold, addGold, addGoldAllowBankruptcy, getBankruptcyWarningState })
   - npc-decision.setStaffWillingnessBonusHandler(() => 0)  // FT-12 stub
3. initialize() 各 system：
   - world-danger.initialize(state)        // 推送 E 階 maxDebt
   - building.initialize(states, deps)     // 推送保險櫃倒數秒數
   - recruitment.initialize(state, deps, guildLevel)  // 立即 checkAutoRefresh
4. 訂閱 EventBus 事件，串接到 UI / save-load
5. 啟動 tick 迴圈（每分鐘呼叫 npc-decision.tickAutoPickup）
```

### 5.3 已知整合衝突點

| 衝突 | 解 |
|---|---|
| `adventurer.ts:ROSTER_CAP=15` vs `building.getRosterCap(states)` | main.ts 一律用 `building.getRosterCap`；ROSTER_CAP 保留作 fallback default |
| outcome.ts 已 emit `'mission:completed'` 含 goldDelta | main.ts 訂閱此事件 → `addGold(state.resources, payload.goldDelta)` 套用，不走 gold-flow |
| `recruitment.ts` hardcode 24h 自動刷新 | main.ts 整合時可改用 `building.getRecruitRefreshIntervalSec(states)` 取代 `BASE_AUTO_REFRESH_INTERVAL_MS`（需擴 recruitment 接口） |
| `dispatch.ts:tryDispatch` 內含 willingness 檢查 + `npc-decision.makeDecision` 也算 willingness | 推薦路徑 P-02 → makeDecision → tryDispatch(forceAccept=true)；自主接單 → tickAutoPickup → tryDispatch(forceAccept=true) |

---

## 6. 存檔結構（save-load.ts）

### 6.1 IndexedDB 存檔記錄

```typescript
interface SaveRecord {
  slot: string                  // 'slot0' | 'slot1' | 'slot2'
  saveVersion: number           // 目前 1
  savedAt: number               // ms timestamp
  state: SerializedAppState     // 整個 AppState 的 serialize 集合
}
```

### 6.2 SerializedAppState 集合（Stage 6.1 擴充）

```typescript
interface SerializedAppState {
  guild:        SerializedGuildState           // 沿用 phase 1
  outcomeState: SerializedOutcomeState
  worldDanger:  SerializedWorldDanger          // C-06 §6.4
  recruitment:  SerializedRecruitmentState     // FT-01 §6.4
  buildings:    SerializedBuildingStates       // FT-07 §6.5
  // npc-decision: 無業務 state，autoPickup tracker 透過 adventurer 欄位存
}
```

### 6.3 缺欄位 fallback 規則

deserialize 時若缺某 system 的欄位（舊存檔），**用該 system 的 `createXxxState()` 預設值補**。已在 `save-load.ts:loadGame` 實作 adventurer 欄位 fallback；新系統的擴充走 `migrate()` stub。

### 6.4 saveVersion 升級

- `saveVersion > CURRENT`：拒絕載入（log warning）
- `saveVersion < CURRENT`：在 `migrate()` 內逐版本升級，目前為 stub passthrough

---

## 7. 命名與錯誤處理規範

### 7.1 識別符號

- public：`PascalCase`（型別 / 介面）/ `camelCase`（函式 / 變數）
- private module-scoped：`_camelCase`（前綴底線）
- 常數：`UPPER_SNAKE_CASE`
- 檔名：`kebab-case.ts`

### 7.2 註釋

- 一律繁體中文
- 識別符號 / Unity API / 設計術語保英文（如 `BuildingTable`、`MonoBehaviour`、`MDA Framework`）
- 敏感分支必須註明對應 GDD 章節（如 `// GDD §4.2`）

### 7.3 錯誤處理

| 情境 | 處理 |
|---|---|
| 防禦性檢查失敗（資料不全 / dep 未注入） | `console.error` + 早退（return 預設值或 false）|
| 資料降級可恢復（如 §5.5 clamp） | `console.warn` + 修正後繼續 |
| 致命錯誤（無法復原） | throw（讓 main.ts 處理） |
| 邊緣案例正常運作 | 不 log（避免噪音） |

**禁用 try-catch 吞錯**：F-03 / 訂閱者拋例外時不吞，由 main.ts 全域處理。

---

## 8. TypeScript 規範

- `tsconfig.json` strict mode 必須 ON
- 禁用 `any`（必要時用 `unknown` + 顯式 cast）
- mutable global state 限定於 systems 內部 `_deps` / `_handler` pattern，必須有對應 setter
- circular import 防範：`events.ts` 不 import systems；breakdown 之類複雜 payload 在 EventMap 中用 `unknown` 並由 subscriber cast

### 8.1 已知 TS 限制

- EventBus 內部 `_listeners` 用 `Map<keyof EventMap, Set<Handler<unknown>>>` 取代 `{[K in keyof EventMap]?: Set<Handler<EventMap[K]>>}`，避免 K-distributed indexer 在 strict mode 的 TS2322 推斷錯誤（public API 仍 type-safe）

---

## 9. 開發協作規範（與全域 CLAUDE.md 對齊）

### 9.1 工項分級（本任務專屬）

- **禁用 Codex / GPT MCP**（Phase 2 Web 移植期間）
- 全部由 Sonnet subagent 實作，最多並行 3 個
- Opus + xhigh 親自審查（不下放 subagent 審查）
- Small 工項可由主體直接做

### 9.2 Stage 進度追蹤

`production/phase2-web-index.md` 為單一真相來源；每 Stage 完成後同步狀態（未開始 / 進行中 / 驗證 / 完成）。

### 9.3 Commit 訊息

繁體中文 subject + description；技術 prefix（`feat:` / `fix:` / `docs:`）與 `Co-Authored-By` trailer 保持英文標準。

---

## 10. 跨 Stage TODO（整合時處理）

| Stage | TODO |
|---|---|
| 5.x UI | UI 用 `building.getRosterCap` / `getMissionSlotCount` / `getMaxConcurrentMissions` 取代硬編碼 |
| 5.x UI | UI 訂閱 `recruit:pool_refreshed` / `building:upgraded` / `commission:settled` 等事件刷新 |
| 6.1 main.ts | F-03 callback 注入：world-danger 的 maxDebt / building 的 bankruptcyWarning |
| 6.1 main.ts | npc-decision tick 接到主迴圈（每分鐘呼叫 tickAutoPickup） |
| 6.1 main.ts | recruitment.checkAutoRefresh 接到主迴圈或 visibility change |
| 6.1 main.ts | save-load 擴充 SerializedAppState 集合 + migrate() 邏輯 |
| 6.1 main.ts | 推薦 dispatch 路徑：P-02 → makeDecision → tryDispatch(forceAccept=true) |
| Post-Jam | gold-flow 重構為金流中央化（路徑 C），需 GDD 註記 |
| Post-Jam | AUTO_PICKUP_*_MINUTES 移到 constants.ts |
| Post-Jam | AdventurerTemplate / isUnique 過濾邏輯重新接入 |

---

## 11. 變更紀錄

| 日期 | 變更 | 對應 commit |
|---|---|---|
| 2026-05-03 | 初版建立 | TBD |
