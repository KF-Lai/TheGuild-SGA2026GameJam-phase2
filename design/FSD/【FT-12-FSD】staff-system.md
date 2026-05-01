# 【FT-12-FSD】功能規格說明書 — Staff System（職員系統）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-12】staff-system.md`（版本：2026-04-27 design-review v2 修訂） |
| 對應 Data-Specs | `【FT-12-DS】staff-table.md`（_待建_，CSV：`StaffTable.csv`，owner = FT-12）<br>`【FT-08-DS】staff-tuning.md`（_待建_，CSV：`StaffTuning.csv`，FT-08 / FT-12 共用 owner）<br>消費端：`【F-01-DS】system-constants.md`（`OFFLINE_MAX_SECONDS`） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體（Opus 4.7 + xhigh） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-28 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-12 Staff System 為公會職員的玩法系統，負責已錄用職員的名冊管理（StaffInstance 生命週期）、三態狀態機（Working / Reallocating / OnLeave）、Slot 指派與切換冷卻、效果聚合查詢 API（5 個 Get/Is API + UI flag OR 聚合）、解雇流程（含資遣費）、自動轉休假機制（Reallocating 超時），以及 Phase 2 的薪水管線骨架（Jam 版不發薪）。FT-12 透過 `HireStaff(candidateCard)` 同步 API 接收 FT-08 錄用候選並建立實例；不感知 gacha 細節。整體啟停由 `FT-07.IsStaffSystemUnlocked()`（職員休息室 L1）惰性閘控。

### 1.2 In-Scope / Out-of-Scope

**In-Scope（Jam 範疇）**：

- StaffInstance schema 與 `_nextInstanceID` counter（§3.1）
- StaffTable / StaffTuning 載入與資料驗證（§3.2 / §5.4）
- `HireStaff` 入職流程（§3.3）
- 三態狀態機與 `TryAssignStaff` / `TryUnassignStaff` / `TryGoOnLeave` / `TryReturnFromLeave`（§3.5 / §3.6）
- 切換冷卻（B-5 案：僅由「進入 Working」transition 觸發）
- Reallocating 自動轉假（§3.7，含具/無 slot 能力分流）
- `TryFireStaff` 解雇流程（§3.8，含資遣費扣款 via FT-05）
- Effect 聚合 5 API + UI flag OR 聚合（§3.4，即時遍歷無快取）
- 系統降級行為（§3.10 全部 API 早退）
- 5 個對外事件（含 Phase 2 `OnStaffSalaryDue` 不發布的無 op 路徑）
- ISaveable 持久化（OwnerKey `ft12StaffSystem`、Critical、含 RestoreFromSave 修復邏輯）

**Out-of-Scope**：

- 薪水扣款的實際發布與 FT-05 訂閱（§3.9 / §4.2，Phase 2；Jam 版 `OnDailyReset` 不訂閱、`OnStaffSalaryDue` 永不發布）
- 職員圖鑑 / 立繪 / 文字內容（Post-Jam D 層）
- FT-09 陣營路線對職員影響（`StaffTable.factionID` 預留欄位）
- 職員技能 / 升星 / 成長（Post-Jam）
- 職員相互關係（Post-Jam）
- UI 隱藏 / 禁用（屬 P-02 訂閱 FT-07 自行處理，FT-12 不規範 UI）
- FT-08 gacha 內部機制（保底、保留、刷新）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 全部 AC（AC-1、AC-3~AC-20、AC-26~AC-35 為 Jam 驗收；AC-2 / AC-21~AC-25 / AC-36 為 Phase 2 不驗收）：

- [ ] DataManager 載入 `StaffTable.csv` 通過 §5.4 全部驗證；非法資料拋 `StaffTableValidationException` 或 LogWarning + clamp（依 §5.4 表格）
- [ ] 系統未解鎖時，所有 `Try*` API 回傳 `STAFF_SYSTEM_LOCKED`、所有 `Get*` API 回傳 `0`、`Is*` API 回傳 `false`、不發任何 `OnStaff*` 事件（AC-1、AC-31）
- [ ] `HireStaff` 成功後 instance state = `Reallocating`、`reallocatingStartTimestamp` 依 slot 能力分流（AC-3、AC-5）
- [ ] `TryAssignStaff` 通過 capacity / cooldown / OnLeave 等檢查，state 轉換、事件順序符合 §3.11.2（AC-6~AC-8）
- [ ] `TryUnassignStaff` / `TryGoOnLeave` / `TryReturnFromLeave` 行為與事件發布順序符合 §3.11.2 表格（AC-9~AC-11）
- [ ] `CheckReallocatingAutoLeave` 在 `OnMinuteTick` + 自節流（每 3600s）觸發；具 slot 能力職員逾時轉 OnLeave，無 slot 能力穩態不轉假（AC-12）
- [ ] `TryFireStaff` 扣 severancePay（透過 FT-05 `AddGoldAllowBankruptcy`）、發 `OnStaffFired`、移除 instance；Working 職員直接銷毀不執行 unassign（AC-13~AC-15）
- [ ] 5 個查詢 API 即時遍歷 roster 並在演算法尾端套 §4.1 cap；OnLeave / 非 Working slot 條件依 §3.4.1 分流（AC-16~AC-20）
- [ ] StaffInstance[] / `_nextInstanceID` / `_lastSalaryTimestamp` 持久化；`RestoreFromSave` 對非法 state / buildingID / capacity 衝突依 §6.7 step 3~7 修復；缺 staffID 拋 Critical（AC-26~AC-28、AC-32~AC-34）
- [ ] EventBus 訂閱者 throw 互不影響（AC-30）
- [ ] 50 位 roster 下單次 effect 聚合 < 1ms（hot path 禁 LINQ alloc，AC-35）

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

引用 `【FT-12】staff-system.md`：§1（系統範圍 / Jam 範疇）、§2（玩家幻想 / 設計原則）、§3.1（StaffInstance schema）、§3.2（StaffTable Schema + StaffEffect / StaffUIFlag enum 白名單）、§3.3（Hire 流程與失敗路徑）、§3.4（Effect 聚合演算法 + UI flag OR）、§3.5（Slot 指派 / TryAssignStaff / TryUnassignStaff）、§3.6（三態狀態機 + 冷卻 B-5 語意）、§3.7（自動轉假觸發條件 + 入口掃描表 + 無 slot 能力例外）、§3.8（Fire 流程 + 事件與 Remove 順序 B-7）、§3.9（薪水管線 Phase 2）、§3.10（降級行為總表）、§3.11（事件契約 5 個 + 發布順序）、§4.1（聚合上限公式）、§4.2（薪水公式 Phase 2）、§5（5 子節邊緣案例）、§6.7（ISaveable 契約 + RestoreFromSave M-5 修復步驟）、§7（CSV 與 StaffTuning 可調參數）、§8（驗收標準）。

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-12-DS】staff-table.md`（_待建_） | `StaffTable.csv` | `staffID` / `name` / `rarity` / `salary` / `severancePay` / `isFiller` / `factionID` / `minGuildLevel` / `effectIDs` / `effectValues` / `slotBuildingIDs` / `uiFlagIDs` / `uiFlagBuildingIDs` | 職員模板查詢、effect 聚合、UI flag OR、解雇資遣費 |
| `【FT-08-DS】staff-tuning.md`（_待建_，FT-08 / FT-12 共用） | `StaffTuning.csv` | `EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` / `BUILDING_SWITCH_COOLDOWN_SECONDS` / `REALLOCATING_AUTO_LEAVE_SECONDS` / `ROSTER_CAP` | Effect 聚合上限、進入 Working 冷卻、自動轉假閾值、名冊容量 |
| 消費端：`【F-01-DS】system-constants.md` | `SystemConstants.csv` | `OFFLINE_MAX_SECONDS`（Phase 2 薪水補發 cap） | §4.2.3 / §3.9.4 離線補發次數 clamp |

### 2.3 上游依賴系統

引用本 FSD-index §2.10「Service 介面命名規範」（裁決方案 A）：FSD 敘述以 `IXxxService` / `IXxx` 命名維持可讀性，實作 PR 直接呼叫對應 concrete singleton。

| 上游 | API / 事件 | 用途 |
| --- | --- | --- |
| F-01 DataManager | `DataManager.Instance.Get<StaffData>(int)` / `GetAll<StaffData>()` / `GetInt(string)`（StaffTuning） | 載入 StaffTable / StaffTuning |
| F-02 Time System | `TimeSystem.Instance.NowUTC`、`OnMinuteTick` 訂閱（Jam 版 auto-leave）、`OnDailyReset` 訂閱（**Phase 2 才訂閱**） | 時間戳、自動轉假掃描節流、薪水觸發 |
| F-03 Resource Mgmt（透過 FT-05 中介） | 經由 `IGoldFlowService.AddGoldAllowBankruptcy(int delta)` 扣資遣費 | 解雇資遣費 |
| FT-05 Guild Gold Flow | `IGoldFlowService.AddGoldAllowBankruptcy(int)` | 資遣費扣款（Jam）+ 薪水扣款訂閱（Phase 2） |
| FT-07 Guild Building System | `IBuildingService.IsStaffSystemUnlocked() : bool`、`IBuildingService.GetBuildingLevel(int buildingID) : int`、訂閱 `OnBuildingUpgraded`（§3.10.5）；capacity 透過 `IDataManager.Get<BuildingData>((buildingID, level)).slotCount` **直接讀 CSV**（FT-07 FSD §8.3 B-02 既定路徑：FT-07 不新增 `GetSlotCount` API） | 系統閘 + 建築當前等級 + capacity 查詢（直接讀 BuildingTable）+ 解鎖 callback |
| FT-08 Gacha System | （單向被動接收）FT-08 呼叫 FT-12 `HireStaff(CandidateCard)` | 接收錄用候選；FT-12 不反向呼叫 FT-08 |
| EventBus | `EventBus.Subscribe<T>` / `Publish<T>`（含 OnBuildingUpgraded / OnMinuteTick / OnDailyReset 訂閱端、5 個對外事件 publish） | 事件分發 |

### 2.4 下游被依賴系統

| 下游 | 介面 | 用途 |
| --- | --- | --- |
| FT-08 Gacha | 呼叫 `HireStaff(CandidateCard) : HireResult`、查詢 `GetRecruitRefreshReductionSec() : int` | 錄用交棒、面試 auto refresh 加成 |
| FT-03 NPC Decision | 查詢 `GetStaffWillingnessBonus() : float` | NPC 接受意願加成 |
| FT-05 Guild Gold Flow | 訂閱 `OnStaffSalaryDue`（**Phase 2**）、查詢 `GetAccountantCommissionBonus()` / `GetAccountantPenaltyBonus()` | 薪水扣款（Phase 2）、傭金 / 罰款修正 |
| FT-02 Mission Dispatch / P-02 委託板 | 查詢 `IsSuccessRatePreviewEnabled() : bool` | UI 顯示成功率預覽旗標 |
| P-02 Main UI | 訂閱 `OnStaffHired` / `OnStaffFired` / `OnStaffAssigned` / `OnStaffStateChanged`；呼叫所有 `Try*` API；查詢 `GetStaffStateView(int) : StaffStateView`（§3.6.6 internal） | 名冊 UI、指派 UI、解雇確認、狀態圖示 |
| P-03 Notification | 訂閱 `OnStaffHired` / `OnStaffFired` / `OnStaffStateChanged`（轉假時） | 桌面通知 |
| FT-10 Save/Load | FT-12 實作 `ISaveable`，`OwnerKey = "ft12StaffSystem"`、`IsCritical = true` | 序列化 StaffInstance[] / `_nextInstanceID` / `_lastSalaryTimestamp` |

### 2.5 跨系統事件契約

對應 GDD §3.11 與 §6.3。

| 事件 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnStaffHired` | 出 | `{ int instanceID, int staffID, long hiredTimestamp }` | `HireStaff` 成功且 `roster.Add` 之後（§3.11.2 row 1）；P-02 / P-03 訂閱 |
| `OnStaffFired` | 出 | `{ int instanceID, int staffID, long firedTimestamp, int severancePaid }` | `TryFireStaff` 在 `roster.Remove` **之前**發布（§3.8.4）；P-02 / P-03 訂閱 |
| `OnStaffAssigned` | 出 | `{ int instanceID, int oldBuildingID, int newBuildingID }` | `TryAssignStaff` 成功（含切建築）/ `TryUnassignStaff`（newBuildingID = 0）/ `TryGoOnLeave`（從 Working 進 OnLeave 時發；§3.11.2 row 6）；P-02 訂閱 |
| `OnStaffStateChanged` | 出 | `{ int instanceID, StaffState oldState, StaffState newState }` | 任何 state transition 後發布（idempotent assign 例外）；P-02 / P-03 訂閱 |
| `OnStaffSalaryDue` | 出（**Phase 2**） | `{ long dueTimestamp, Dictionary<int,int> perStaffSalary, int totalAmount }` | **Jam 版永不發布**；Phase 2 啟用後由 `OnSalaryTick` 觸發；FT-05 訂閱端走 `AddGoldAllowBankruptcy(-totalAmount)` |
| `OnBuildingUpgraded` | 入 | `{ int buildingID, int fromLevel, int toLevel }` | FT-07 發布；FT-12 訂閱以實作 §3.10.5 解鎖 callback（**Phase 2 才有非 no-op 邏輯**） |
| `OnMinuteTick` | 入 | `{ long nowUtcSeconds }` | F-02 發布；FT-12 訂閱以節流呼叫 `CheckReallocatingAutoLeave` |
| `OnDailyReset` | 入 | F-02 既有 payload | **Phase 2 才訂閱**；觸發 `OnSalaryTick(now)` |

> **事件 struct 命名後綴慣例**：實際 C# struct 採 `OnXxxEvent` 後綴（對齊 F-02 FSD `OnMinuteTickEvent` / `OnDailyResetEvent`、FT-07 FSD `OnBuildingUpgradedEvent`）。本表為可讀性省略 `-Event` 後綴，EventBus 訂閱與發布時以實際 struct 名為準（例：`EventBus.Publish<OnStaffHiredEvent>(...)`、`EventBus.Subscribe<OnMinuteTickEvent>(...)`）。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

公會的職員是有名字、有臉、有能力差異的個人；玩家錄用後在名冊看到他們、指派到某棟建築立即看到 effect 加成；當公會養不起 1★ 跑腿時按下解雇要付資遣費，操作不可逆；公會壯大後看 5+ 職員各司其職的成就感。

### 3.2 系統目的還原

提供職員的「玩法層」：名冊（誰在公會）、狀態（在哪、做什麼）、能力（effect 數值與條件）、開除（沉重決策）。FT-12 不執行面試（屬 FT-08），不規範 UI 樣式（屬 P-02），純粹是「職員在 runtime 怎麼存在、怎麼提供加成、怎麼被管理」的後端邏輯。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 「會記得名字的人」 | 名冊每筆有 instanceID + staffID + name；解雇後 instanceID 永不重複 | `_nextInstanceID` 單調遞增 counter（§3.1）；`OnStaffFired` payload 含 staffID 給 P-03 顯示 name |
| 「指派立即生效」 | TryAssignStaff 後下次 effect query 立刻反映 | `StaffEffectAggregator` 即時遍歷 roster、不快取（§3.4.2）；`OnStaffAssigned` 不需 invalidate cache |
| 「真實的開除」 | 解雇按鈕顯示「將支付 X gold 資遣費」、不可逆 | `TryFireStaff` 發 `OnStaffFired` 含 `severancePaid`、`roster.Remove` 後 instanceID 永不回收（§3.8） |
| 「三態語義清楚」 | UI 顯示 Working / Reallocating / OnLeave 三種圖示；切建築受冷卻限制 | StaffState enum + `BUILDING_SWITCH_COOLDOWN_SECONDS` 冷卻只在「進入 Working」觸發（§3.6.4 B-5 案）；`GetStaffStateView` 暴露剩餘時間 |
| 「玩家忘記指派時系統不卡」 | 錄用 12h 後職員自動轉 OnLeave，玩家須主動 TryReturnFromLeave + TryAssignStaff | `CheckReallocatingAutoLeave` 在 OnMinuteTick + 自節流每 3600s 掃描；具 slot 能力職員逾時轉假（§3.7） |
| 「無 slot 能力職員（純 passive）穩定提供加成」 | 1★ 純加意願職員錄用後永久提供 willingness +0.01，不會自動轉假 | `HasSlotCapability` 判定 → `reallocatingStartTimestamp = 0` 為穩態標記（§3.7.5、§3.3.2 B-9 A 案） |
| 「Phase 2 薪水承諾不是 Jam 包袱」 | Jam 版錄用即免費、不扣薪 | FT-12 Jam 版不訂閱 `OnDailyReset`、`OnStaffSalaryDue` 永不發布（§3.9 整節 Phase 2） |
| 「降級時不崩潰」 | 職員休息室拆除後 UI 不顯示但資料保留；重建後立即恢復 | §3.10.4 持久化資料保留；惰性閘控（API 入口 query `IsStaffSystemUnlocked`），不主動訂閱降級 callback |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（FSD 不拆分；單份 FSD 對應 5 個 Script）。

### 4.2 拆分理由

對齊 §2.4 標準與 FT-08 / FT-09 先例：

- 系統雖然功能多但職責高度耦合（state machine / slot / hire / fire / aggregate 共享 roster 內部結構），跨檔案會增加 internal access 與測試裝配成本
- 預估合計 1300~1600 行落在 FT-08（1170~1460）/ FT-09（1000~1200）相近區間，採 5 Script 同檔策略一致
- 唯一接近 500 行門檻的 `StaffService` 仍可控（450~550 行範圍），且 §3.9 薪水管線 Phase 2 大部分為早退 stub 無實質負擔；若實作期確實超過 500 行，於 §8.3 提出 Phase 2 拆分建議（將 SalaryPipeline 抽成獨立 Script）

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| - | （未拆分） | - | - |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `StaffTypes.cs` | `Assets/Scripts/Gameplay/Staff/StaffTypes.cs` | 集中定義 StaffInstance / 4 個 result enum / 4 個事件 payload struct / StaffState / StaffEffect / StaffUIFlag / StaffStateView / CandidateCard 介面型別 | UnityEngine（`Serializable`） | 200~280 行 |
| `IStaffService.cs` | `Assets/Scripts/Gameplay/Staff/IStaffService.cs` | 對外公開 API 介面（5 query + 6 mutator + 1 GetInstance + 1 GetStateView + ISaveable 簽章） | StaffTypes | 100~140 行 |
| `StaffTableLoader.cs` | `Assets/Scripts/Gameplay/Staff/StaffTableLoader.cs` | 透過 DataManager 載入 StaffTable / StaffTuning，套用 §3.2 / §5.4 全部驗證並 cache 為唯讀 lookup | `IDataManager` | 250~320 行 |
| `StaffEffectAggregator.cs` | `Assets/Scripts/Gameplay/Staff/StaffEffectAggregator.cs` | 5 個查詢 API（4 數值 SUM + cap、1 UI flag OR）即時遍歷 roster；hot path 禁 alloc | StaffTableLoader、StaffService（讀 roster snapshot 介面）、`IBuildingService`（系統閘） | 280~350 行 |
| `StaffService.cs` | `Assets/Scripts/Gameplay/Staff/StaffService.cs` | 核心服務：roster 字典、`_nextInstanceID` counter、HireStaff / TryAssignStaff / TryUnassignStaff / TryFireStaff / TryGoOnLeave / TryReturnFromLeave / CheckReallocatingAutoLeave / OnSalaryTick(Phase 2)；ISaveable 序列化與 RestoreFromSave 修復；訂閱 OnMinuteTick + 自節流；OnDestroy / OnDisable 對稱解除 EventBus 訂閱 | StaffTableLoader、StaffEffectAggregator、`IBuildingService`（系統閘 + GetBuildingLevel）、`IDataManager`（capacity 直查 `BuildingTable`）、`IGoldFlowService`、`ITimeSystem`、`EventBus` | 450~550 行 |

**合計預估**：1280~1640 行；StaffService 為最大檔案，若實作期超 500 行則於 §8.3 規劃 Post-Jam 將 `OnSalaryTick` / `ProcessOfflineSalary` 抽成 `StaffSalaryPipeline.cs`。

### 4.5 類別關係

```
                                 ┌──────────────────────────┐
                                 │       IStaffService      │
                                 │ (interface, 公開 API)    │
                                 └──────────┬───────────────┘
                                            │ implements
                                            ▼
                              ┌─────────────────────────────────┐
                              │         StaffService            │
                              │  - _roster: Dict<int, Inst>     │
                              │  - _nextInstanceID, _lastSal..  │
                              │  - HireStaff / TryAssign / ...  │
                              │  - CheckReallocatingAutoLeave   │
                              │  - ISaveable: Serialize/Restore │
                              └──┬───────────┬──────────┬───────┘
            uses (lookup +       │           │          │ uses (snapshot)
            validation cache)    │           │          │
                                 ▼           ▼          ▼
                  ┌─────────────────────┐  ┌──────────────────────────┐
                  │  StaffTableLoader   │  │  StaffEffectAggregator   │
                  │  - StaffData lookup │  │  - 5 query API impls     │
                  │  - StaffTuning load │  │  - cap / OR logic        │
                  └─────────────────────┘  └──────────────────────────┘
                              ▲                       ▲
                              │ uses                  │ uses
                              │                       │
                  ┌───────────┴────────────┐  ┌───────┴────────────────┐
                  │   IDataManager (F-01)  │  │  IBuildingService(FT07)│
                  └────────────────────────┘  └────────────────────────┘

  (上層公用) StaffTypes：StaffInstance / Enum / Event payload / DTO
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

`IStaffService`（由 `StaffService` 實作；singleton 取得：`StaffService.Instance`）：

```csharp
// ─── Hire / Fire ─────────────────────────────────────────
HireResult HireStaff(CandidateCard candidate);
TryFireStaffResult TryFireStaff(int instanceID);

// ─── Slot 指派 ───────────────────────────────────────────
AssignResult TryAssignStaff(int instanceID, int buildingID);
UnassignResult TryUnassignStaff(int instanceID);

// ─── 狀態切換 ────────────────────────────────────────────
GoOnLeaveResult TryGoOnLeave(int instanceID);
ReturnFromLeaveResult TryReturnFromLeave(int instanceID);

// ─── 查詢（roster）──────────────────────────────────────
StaffInstance GetInstance(int instanceID);          // 找不到回 null
IReadOnlyDictionary<int, StaffInstance> GetRoster(); // P-02 名冊讀取用
StaffStateView GetStaffStateView(int instanceID);    // §3.6.6

// ─── Effect 聚合（透過 StaffEffectAggregator 委派；介面同層暴露）
float GetStaffWillingnessBonus();
float GetAccountantCommissionBonus();
float GetAccountantPenaltyBonus();
int   GetRecruitRefreshReductionSec();
bool  IsSuccessRatePreviewEnabled();

// ─── 內部觸發（測試 / Bootstrap 用，不對外）─────────────
internal void CheckReallocatingAutoLeave();
internal void OnStaffSystemBoot();    // FT-10 Bootstrap 呼叫；Jam 版僅做 auto-leave 補掃
```

result enum（皆於 `StaffTypes.cs` 定義；對齊 GDD §3.3.1 / §3.5.2 / §3.6.3 / §3.8.1）：

```csharp
enum HireStaffResult       { OK, STAFF_SYSTEM_LOCKED, INVALID_STAFF_ID, ROSTER_FULL, DUPLICATE_INSTANCE }
enum AssignResult          { SUCCESS, STAFF_SYSTEM_LOCKED, STAFF_NOT_FOUND, BUILDING_NOT_ELIGIBLE,
                              BUILDING_FULL, STAFF_ON_LEAVE, SWITCH_COOLDOWN }
enum UnassignResult        { SUCCESS, STAFF_SYSTEM_LOCKED, STAFF_NOT_FOUND, STAFF_NOT_ASSIGNED, STAFF_ON_LEAVE }
enum GoOnLeaveResult       { SUCCESS, STAFF_SYSTEM_LOCKED, STAFF_NOT_FOUND, ALREADY_ON_LEAVE }
enum ReturnFromLeaveResult { SUCCESS, STAFF_SYSTEM_LOCKED, STAFF_NOT_FOUND, NOT_ON_LEAVE }
enum TryFireStaffResult    { SUCCESS, STAFF_SYSTEM_LOCKED, STAFF_NOT_FOUND }

struct HireResult { public HireStaffResult result; public int instanceID; }
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnStaffHired` | 出 | `int instanceID, int staffID, long hiredTimestamp` | `HireStaff` 成功後 `roster.Add` 之後；P-02 名冊加入 / P-03 toast |
| `OnStaffFired` | 出 | `int instanceID, int staffID, long firedTimestamp, int severancePaid` | `TryFireStaff` 在 `roster.Remove` 之前發（§3.8.4 設計決策不可調）；P-02 / P-03 |
| `OnStaffAssigned` | 出 | `int instanceID, int oldBuildingID, int newBuildingID` | TryAssign 成功（state 改 / 切建築皆發）、TryUnassign（newBuildingID=0）、TryGoOnLeave 從 Working 進 OnLeave（M-4 案，oldBuildingID > 0 時）；P-02 / FT-05（被動感知不需處理） |
| `OnStaffStateChanged` | 出 | `int instanceID, StaffState oldState, StaffState newState` | 任何狀態 transition 之後（idempotent assign 不發、單純切建築 Working→Working state 不變不發 OnStaffStateChanged 但發 OnStaffAssigned）；P-02 / P-03（轉假時） |
| `OnStaffSalaryDue` | 出（**Phase 2**） | `long dueTimestamp, Dictionary<int,int> perStaffSalary, int totalAmount` | **Jam 版永不發布**；Phase 2 由 `OnSalaryTick` 觸發；FT-05 訂閱 `AddGoldAllowBankruptcy(-totalAmount)` |
| `OnBuildingUpgraded` | 入 | FT-07 既有 payload `{ int buildingID, int fromLevel, int toLevel }` | FT-12 訂閱以實作 §3.10.5：當 `buildingID == 6 && fromLevel == 0 && toLevel == 1` 觸發 `OnStaffSystemUnlocked`（**Phase 2 才有薪水 reset 邏輯**） |
| `OnMinuteTick` | 入 | F-02 既有 payload | FT-12 訂閱以節流呼叫 `CheckReallocatingAutoLeave`；自節流為「累計 ≥ 3600s 才執行一次」（GDD §3.7.3） |
| `OnDailyReset` | 入 | F-02 既有 payload | **Phase 2 才訂閱**；觸發 `OnSalaryTick(now)` |

### 5.3 資料結構

`StaffTypes.cs` 公開定義：

```csharp
public enum StaffState { Working, Reallocating, OnLeave }

public enum StaffEffect    // §3.2 白名單；新增需同步 enum + 聚合演算法
{
    Willingness,
    AccountantCommission,
    AccountantPenaltyOnVault,
    RecruitRefreshOnCounter
}

public enum StaffUIFlag    // §3.2 UI flag 白名單
{
    SuccessRatePreview
}

[Serializable]
public class StaffInstance
{
    public int       instanceID;
    public int       staffID;
    public StaffState currentState;
    public int       assignedBuildingID;
    public long      reallocatingStartTimestamp;
    public long      buildingSwitchCooldownEndTimestamp;
    public long      hiredTimestamp;
}

[Serializable]
public class StaffData       // StaffTable 一行；DataManager Get<T>
{
    public int       staffID;
    public string    name;
    public int       rarity;
    public int       salary;
    public int       severancePay;
    public bool      isFiller;
    public int       factionID;
    public int       minGuildLevel;
    public List<StaffEffect> effectIDs;
    public List<float>       effectValues;
    public List<int>         slotBuildingIDs;
    public List<StaffUIFlag> uiFlagIDs;
    public List<int>         uiFlagBuildingIDs;
}

public struct StaffStateView
{
    public StaffState currentState;
    public int        assignedBuildingID;
    public int        reallocatingRemainingSec;
    public int        switchCooldownRemainingSec;
}

public struct CandidateCard  // FT-08 帶來；FT-12 僅讀 staffID
{
    public int staffID;
    // 其他欄位由 FT-08 GachaTypes 定義；FT-12 不感知
}

// 4 個 result enum + HireResult 見 §5.1
// 4 個事件 payload struct（OnStaffHired / OnStaffFired / OnStaffAssigned / OnStaffStateChanged / OnStaffSalaryDue）皆於 StaffTypes.cs 定義
```

`StaffTuning` 載入策略：透過 `DataManager.Instance.GetInt(string key)` 逐 key 查；StaffTableLoader cache 為 readonly fields：`_effectMaxWillingness`、`_effectMaxAccountantCommission`、`_effectMaxAccountantPenalty`、`_effectMaxRecruitRefresh`、`_buildingSwitchCooldownSec`、`_reallocatingAutoLeaveSec`、`_rosterCap`。

### 5.4 內部資料流

#### 5.4.1 Bootstrap（FT-10 呼叫順序）

```
FT-10.Bootstrap → 拓撲順序至 FT-12
  → StaffService.OnBootstrap()
      ├─ 步驟 1：StaffTableLoader.Load()
      │           └─ DataManager.GetAll<StaffData>()，套 §5.4 全驗證；違規 throw / LogWarning
      ├─ 步驟 2：StaffTuning constants 載入（cache 至 fields）
      ├─ 步驟 3：EventBus.Subscribe<OnBuildingUpgraded>(callback)
      ├─ 步驟 4：EventBus.Subscribe<OnMinuteTick>(throttled CheckReallocatingAutoLeave)
      ├─ 步驟 5：if (StaffPhase2.SalaryEnabled)：EventBus.Subscribe<OnDailyResetEvent>(_ => OnSalaryTick(TimeSystem.NowUTC))
      │           // StaffPhase2.SalaryEnabled 為 C# const（值 false）；定義於 StaffTypes.cs
      │           // Jam 版編譯期常數 false → 不訂閱、ProcessOfflineSalary 不呼叫；Phase 2 啟用時改 true
      ├─ 步驟 6：if (RestoreFromSave)：執行 §6.7 step 1~7 修復
      └─ 步驟 7：CheckReallocatingAutoLeave()（OnStaffSystemBoot 補掃，§3.7.3）
              if (StaffPhase2.SalaryEnabled) ProcessOfflineSalary()  // §3.9.4 補發迴圈；Jam 版 const false 直接略過
```

**Phase 2 旗標機制（採方案 b：C# 編譯期常數）**：

```csharp
// StaffTypes.cs
public static class StaffPhase2
{
    public const bool SalaryEnabled = false;   // Jam 版 false；Phase 2 啟用時改 true
}
```

理由：
- Jam 版 `const false` 編譯期分支消除（dead code elimination），效能零負擔
- 不需 CSV 改動、不需新增 SystemConstants key
- Phase 2 啟用時單行 patch（改 `true`）即可同時開啟 `OnSalaryTick` 訂閱、`ProcessOfflineSalary` 補發、`OnStaffSystemUnlocked` 內 `_lastSalaryTimestamp ← now` 三條相關路徑
- 對齊 §1.2 Out-of-Scope「Phase 2 註記」與 §8.3 B-08（M-3 解鎖 reset 範圍 Jam vs Phase 2 兩條分支）

#### 5.4.2 HireStaff（§3.3.2）

```
FT-08.TryRecruit
  → StaffService.HireStaff(CandidateCard candidate)
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return STAFF_SYSTEM_LOCKED
      ├─ 步驟 2：data = StaffTableLoader.Get(candidate.staffID)
      │          if (data == null) → LogError + return INVALID_STAFF_ID
      ├─ 步驟 3：if (Jam 版且 _roster.Count >= _rosterCap) → return ROSTER_FULL
      ├─ 步驟 4：hasSlot = data.slotBuildingIDs.Any(id => id > 0)
      │          instance = new StaffInstance {
      │              instanceID = _nextInstanceID++,
      │              staffID = candidate.staffID,
      │              currentState = Reallocating,
      │              assignedBuildingID = 0,
      │              reallocatingStartTimestamp = hasSlot ? now : 0,
      │              buildingSwitchCooldownEndTimestamp = 0,
      │              hiredTimestamp = now
      │          }
      ├─ 步驟 5：_roster.Add(instance.instanceID, instance)
      ├─ 步驟 6：EventBus.Publish(new OnStaffHired(...))
      └─ 步驟 7：return { OK, instance.instanceID }
```

#### 5.4.3 TryAssignStaff（§3.5.3）

```
P-02 / 玩家
  → StaffService.TryAssignStaff(instanceID, buildingID)
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return STAFF_SYSTEM_LOCKED
      ├─ 步驟 1.5：CheckReallocatingAutoLeave()（入口掃描，§3.7.3 表）
      ├─ 步驟 2：if (!_roster.TryGetValue(instanceID, out staff)) → return STAFF_NOT_FOUND
      ├─ 步驟 3：data = StaffTableLoader.Get(staff.staffID)
      │          if (!data.slotBuildingIDs.Contains(buildingID)) → return BUILDING_NOT_ELIGIBLE
      ├─ 步驟 4：idempotent 短路（B-6 案，先於冷卻檢查）：
      │          if (staff.currentState == Working && staff.assignedBuildingID == buildingID)
      │              → return SUCCESS（不發事件、不查冷卻）
      ├─ 步驟 5：if (staff.currentState == OnLeave) → return STAFF_ON_LEAVE
      ├─ 步驟 6：if (now < staff.buildingSwitchCooldownEndTimestamp) → return SWITCH_COOLDOWN
      ├─ 步驟 7：currentInBuilding = _roster.Values.Count(s => s.assignedBuildingID == buildingID
      │                                                        && s.currentState == Working)
      │          buildingLevel = IBuildingService.GetBuildingLevel(buildingID)
      │          buildingData  = IDataManager.Get<BuildingData>((buildingID, buildingLevel))   // 直接讀 BuildingTable CSV
      │          capacity      = buildingData.slotCount
      │          if (currentInBuilding >= capacity) → return BUILDING_FULL
      ├─ 步驟 8：oldBuildingID = staff.assignedBuildingID
      │          oldState = staff.currentState
      │          staff.assignedBuildingID = buildingID
      │          staff.currentState = Working
      │          staff.reallocatingStartTimestamp = 0
      │          staff.buildingSwitchCooldownEndTimestamp = now + _buildingSwitchCooldownSec
      ├─ 步驟 9：EventBus.Publish(new OnStaffAssigned(instanceID, oldBuildingID, buildingID))
      │          if (oldState != Working)
      │              EventBus.Publish(new OnStaffStateChanged(instanceID, oldState, Working))
      └─ 步驟 10：return SUCCESS
```

#### 5.4.4 TryUnassignStaff（§3.5.4）

```
P-02 / 玩家
  → StaffService.TryUnassignStaff(instanceID)
      ├─ 步驟 1：系統閘 + 入口掃描 + 找職員（同 5.4.3 step 1 / 1.5 / 2）
      ├─ 步驟 2：if (staff.assignedBuildingID == 0) → return STAFF_NOT_ASSIGNED
      ├─ 步驟 3：if (staff.currentState == OnLeave) → return STAFF_ON_LEAVE
      ├─ 步驟 4：oldBuildingID = staff.assignedBuildingID
      │          oldState = staff.currentState   // == Working
      │          staff.assignedBuildingID = 0
      │          staff.currentState = Reallocating
      │          staff.reallocatingStartTimestamp = now
      │          // 不重置 cooldown（B-5 沿用倒數）
      ├─ 步驟 5：EventBus.Publish(new OnStaffAssigned(instanceID, oldBuildingID, 0))
      │          EventBus.Publish(new OnStaffStateChanged(instanceID, oldState, Reallocating))
      └─ 步驟 6：return SUCCESS
```

#### 5.4.5 TryGoOnLeave（§3.6 + M-4 案，§3.11.2 row 6）

```
P-02 / 玩家
  → StaffService.TryGoOnLeave(instanceID)
      ├─ 步驟 1：系統閘 + 入口掃描 + 找職員
      ├─ 步驟 2：if (staff.currentState == OnLeave) → return ALREADY_ON_LEAVE
      ├─ 步驟 3：oldBuildingID = staff.assignedBuildingID（快照）
      │          oldState = staff.currentState
      │          staff.currentState = OnLeave
      │          staff.assignedBuildingID = 0
      │          staff.reallocatingStartTimestamp = 0
      │          // 不重置 cooldown
      ├─ 步驟 4：if (oldBuildingID > 0)
      │              EventBus.Publish(new OnStaffAssigned(instanceID, oldBuildingID, 0))
      ├─ 步驟 5：EventBus.Publish(new OnStaffStateChanged(instanceID, oldState, OnLeave))
      └─ 步驟 6：return SUCCESS
```

#### 5.4.6 TryReturnFromLeave（§3.6.3）

```
P-02 / 玩家
  → StaffService.TryReturnFromLeave(instanceID)
      ├─ 步驟 1：系統閘 + 入口掃描 + 找職員
      ├─ 步驟 2：if (staff.currentState != OnLeave) → return NOT_ON_LEAVE
      ├─ 步驟 3：oldState = staff.currentState   // == OnLeave
      │          staff.currentState = Reallocating
      │          staff.reallocatingStartTimestamp = now
      ├─ 步驟 4：EventBus.Publish(new OnStaffStateChanged(instanceID, oldState, Reallocating))
      └─ 步驟 5：return SUCCESS
```

#### 5.4.7 TryFireStaff（§3.8.2）

```
P-02 / 玩家
  → StaffService.TryFireStaff(instanceID)
      ├─ 步驟 1：系統閘 + 入口掃描 + 找職員
      ├─ 步驟 2：data = StaffTableLoader.Get(staff.staffID)
      │          severancePay = data.severancePay
      ├─ 步驟 3：IGoldFlowService.AddGoldAllowBankruptcy(-severancePay)
      ├─ 步驟 4：EventBus.Publish(new OnStaffFired(instanceID, staff.staffID, now, severancePay))
      │          // 先發事件、後 Remove（§3.8.4 不可調設計決策）
      ├─ 步驟 5：_roster.Remove(instanceID)
      └─ 步驟 6：return SUCCESS
```

#### 5.4.8 CheckReallocatingAutoLeave（§3.7.3）

```
F-02.OnMinuteTick（節流：累計秒差 ≥ 3600s）
  → StaffService.CheckReallocatingAutoLeave()
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return
      ├─ 步驟 2：foreach staff in _roster.Values:
      │          ├─ if (staff.currentState != Reallocating) continue
      │          ├─ if (staff.reallocatingStartTimestamp == 0) continue   // 無 slot 能力穩態 / 舊存檔
      │          ├─ if (now - staff.reallocatingStartTimestamp < _reallocatingAutoLeaveSec) continue
      │          ├─ data = StaffTableLoader.Get(staff.staffID)
      │          ├─ if (!data.slotBuildingIDs.Any(id => id > 0)) continue   // HasSlotCapability=false
      │          ├─ oldState = staff.currentState
      │          ├─ staff.currentState = OnLeave
      │          │   staff.assignedBuildingID = 0
      │          │   staff.reallocatingStartTimestamp = 0
      │          │   // 不重置 cooldown
      │          └─ EventBus.Publish(new OnStaffStateChanged(staff.instanceID, oldState, OnLeave))
      └─ 步驟 3：_lastAutoLeaveScanTimestamp = now
```

#### 5.4.9 Effect 聚合（§3.4.2）

```
FT-03 / FT-05 / FT-08 / FT-02
  → StaffEffectAggregator.GetXxxBonus()  ← 即時遍歷無快取
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return 0 / false
      ├─ 步驟 2：sum = 0；foreach staff in StaffService.GetRoster().Values:
      │          ├─ Passive 類：if (staff.currentState == OnLeave) continue
      │          ├─ Slot 類：if (staff.currentState != Working) continue
      │          ├─ data = StaffTableLoader.Get(staff.staffID)
      │          ├─ idx = indexOf(targetEffectID, data.effectIDs)
      │          ├─ if (idx < 0) continue
      │          ├─ Slot 類額外：if (staff.assignedBuildingID != requiredBuildingID) continue
      │          └─ sum += data.effectValues[idx]
      ├─ 步驟 3：sum = clamp(sum, ...)（min 上限 / max 下限對齊 §4.1）
      └─ 步驟 4：return sum
```

UI flag OR 聚合（§3.4.6）：

```
P-02 / FT-02
  → StaffEffectAggregator.IsSuccessRatePreviewEnabled()
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return false
      └─ 步驟 2：foreach staff in roster:
                 ├─ if (staff.currentState != Working) continue
                 ├─ data = StaffTableLoader.Get(staff.staffID)
                 ├─ idx = indexOf(SuccessRatePreview, data.uiFlagIDs)
                 ├─ if (idx < 0) continue
                 └─ if (staff.assignedBuildingID == data.uiFlagBuildingIDs[idx]) → return true
                 return false（無一符合）
```

#### 5.4.10 OnSalaryTick（**Phase 2**；§3.9.3 + §4.2）

> **參數來源澄清**：F-02 `OnDailyResetEvent` payload 為空 struct（F-02 FSD §5.2）；訂閱 callback 內 `dueTimestamp` 由 `TimeSystem.Instance.NowUTC` 取得，**非**從事件 payload 取欄位。§5.4.1 step 5 訂閱簽章 `_ => OnSalaryTick(TimeSystem.NowUTC)`。

```
F-02.OnDailyResetEvent（Phase 2 才訂閱）
  → StaffService.OnSalaryTick(dueTimestamp = TimeSystem.NowUTC)
      ├─ 步驟 1：if (!IBuildingService.IsStaffSystemUnlocked()) → return（§3.10）
      ├─ 步驟 2：if (now < _lastSalaryTimestamp) → return（時鐘倒退，§5.1）
      ├─ 步驟 3：if (dueTimestamp <= _lastSalaryTimestamp) → return（重複守護）
      ├─ 步驟 4：perStaffSalary = AssemblePerStaffSalary()  // atomic snapshot，OnLeave 排除
      ├─ 步驟 5：totalAmount = Σ perStaffSalary.Values
      ├─ 步驟 6：EventBus.Publish(new OnStaffSalaryDue(dueTimestamp, perStaffSalary, totalAmount))
      └─ 步驟 7：_lastSalaryTimestamp = dueTimestamp
```

`OnStaffSystemBoot` 末段（Phase 2）執行 `ProcessOfflineSalary` 補發迴圈，邏輯依 §3.9.4。Jam 版整段 § 3.9 不執行。

#### 5.4.11 ISaveable Restore（§6.7 step 1~7）

```
FT-10.RestoreFromSave(json)
  → StaffService.RestoreFromSave(string ownerJson)
      ├─ 步驟 1：JsonUtility 反序列化 → list/counters
      ├─ 步驟 2：foreach instance：
      │          ├─ data = DataManager.Get<StaffData>(instance.staffID)
      │          ├─ if (data == null) → throw CriticalRestoreFailedException
      │          ├─ if (currentState ∉ {Working, Reallocating, OnLeave})
      │          │       → throw CriticalRestoreFailedException
      │          ├─ if (state == OnLeave) → assignedBuildingID = 0
      │          ├─ if (state == Reallocating) → assignedBuildingID = 0
      │          └─ if (state == Working)：套 §6.7 step 3 五分支修復（reset to Reallocating + LogWarning）
      ├─ 步驟 3：foreach buildingID（被 Working 佔用）：
      │          ├─ if (count > capacity)：按 (hiredTimestamp ASC, instanceID ASC) 雙鍵排序保留前 N，其餘 reset（M-5）
      │          │   // 雙鍵保證決定性：相同 hiredTimestamp 時以 instanceID 升序為次要鍵，避免不同 reload 結果不同
      ├─ 步驟 4：duplicate instanceID 檢查 → throw CriticalRestoreFailedException
      ├─ 步驟 5：_nextInstanceID = max(_nextInstanceID, max(roster.Keys) + 1)（M-5）
      └─ 步驟 6：完成；不補發任何事件（§6.7 step 8）
```

#### 5.4.12 Subscribe / Unsubscribe 生命週期

對齊 `.claude/rules/gameplay-code.md` 「Unity OnEnable / OnDisable 對稱原則」與 §四 程式實作原則第 8 條（OnEnable / OnDisable 對稱）。

```
StaffService.OnDestroy()  /  OnDisable()
  → 對稱解除 §5.4.1 step 3~5 註冊的訂閱：
      ├─ EventBus.Unsubscribe<OnBuildingUpgradedEvent>(...)
      ├─ EventBus.Unsubscribe<OnMinuteTickEvent>(...)
      └─ if (StaffPhase2.SalaryEnabled): EventBus.Unsubscribe<OnDailyResetEvent>(...)
```

實作要點：

- 訂閱時保存 callback handle（lambda 不可直接 unsubscribe）；建議以 method group 訂閱：`EventBus.Subscribe<OnMinuteTickEvent>(OnMinuteTickHandler)`，`OnDestroy` 時 `EventBus.Unsubscribe<OnMinuteTickEvent>(OnMinuteTickHandler)`
- `OnDisable` 與 `OnDestroy` 兩處皆需處理（場景切換 vs 應用程式結束）；若 EventBus 自身於 Domain Reload 已 reset 則 `OnDestroy` 為兜底
- 載入流程的 `RestoreFromSave` 不重新訂閱（訂閱已於 `OnBootstrap` 完成、`Awake` 階段持久）
- 5 個對外發布事件本身不在訂閱列表，無需解除

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `StaffTable.csv` | `staffID` / `name` / `rarity` / `salary` / `severancePay` / `isFiller` / `factionID` / `minGuildLevel` / `effectIDs` / `effectValues` / `slotBuildingIDs` / `uiFlagIDs` / `uiFlagBuildingIDs` | `【FT-12-DS】staff-table.md`（_待建_） | 職員模板查詢、effect 聚合、UI flag OR、解雇資遣費 | Bootstrap（StaffTableLoader.Load） |
| `StaffTuning.csv` | `EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` / `BUILDING_SWITCH_COOLDOWN_SECONDS` / `REALLOCATING_AUTO_LEAVE_SECONDS` / `ROSTER_CAP` | `【FT-08-DS】staff-tuning.md`（_待建_，FT-08 / FT-12 共用） | Effect 聚合上限、進入 Working 冷卻、自動轉假閾值、名冊容量 | Bootstrap（StaffTableLoader.Load） |
| `SystemConstants.csv` | `OFFLINE_MAX_SECONDS` | `【F-01-DS】system-constants.md` | Phase 2 §3.9.4 / §4.2.3 離線補發 cycle clamp | Bootstrap（透過 DataManager） |
| `BuildingTable.csv` | `slotCount`（buildingID = 4 / 5 / 6；複合鍵 `(buildingID, level)`） | `【FT-07-DS】building-table.md`（_待建_，owner = FT-07） | Capacity 查詢；FT-12 **直接讀 CSV**：`level = IBuildingService.GetBuildingLevel(buildingID)` → `IDataManager.Get<BuildingData>((buildingID, level)).slotCount`（FT-07 FSD §8.3 B-02 既定路徑，FT-07 不暴露 `GetSlotCount` API） | Bootstrap 由 F-01 載入；TryAssignStaff / RestoreFromSave 即時查詢 |

### 6.2 引用的 ScriptableObject

無。FT-12 純 CSV 驅動。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `salary`（每職員每日薪水） | `StaffTable.salary` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `severancePay`（解雇資遣費） | `StaffTable.severancePay` | 同上 |
| `effectValues[i]`（每 effect 的數值） | `StaffTable.effectValues` | 同上 |
| `slotBuildingIDs[i]`（職員可指派建築清單） | `StaffTable.slotBuildingIDs` | 同上 |
| `uiFlagBuildingIDs[i]` | `StaffTable.uiFlagBuildingIDs` | 同上 |
| `EFFECT_MAX_WILLINGNESS_BONUS` | `StaffTuning` | 同上 |
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | `StaffTuning` | 同上 |
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | `StaffTuning` | 同上 |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | `StaffTuning` | 同上 |
| `BUILDING_SWITCH_COOLDOWN_SECONDS`（進 Working 冷卻） | `StaffTuning` | 同上 |
| `REALLOCATING_AUTO_LEAVE_SECONDS`（自動轉假閾值） | `StaffTuning` | 同上 |
| `ROSTER_CAP`（名冊容量上限，Post-Jam） | `StaffTuning` | 同上 |
| `OFFLINE_MAX_SECONDS`（Phase 2 補發 cap） | `SystemConstants` | 同上 |
| `BuildingTable[(buildingID, level)].slotCount`（capacity） | `BuildingTable` 直接讀（透過 `IDataManager.Get<BuildingData>`；level 透過 `IBuildingService.GetBuildingLevel`） | 同上 |
| `Auto-leave 自節流間隔 = 3600s` | `StaffTuning`（建議補登 `AUTO_LEAVE_SCAN_INTERVAL_SECONDS`，§8.3 D-01） | 同上 |

僅以下純語意常數允許硬寫（屬「設計決策不可調」§7.4）：

- `StaffState` enum 三值名稱（Working / Reallocating / OnLeave）
- 6 個 result enum 的值
- ISaveable `OwnerKey = "ft12StaffSystem"`
- 事件 type 名稱與 payload 欄位名稱
- 「Slot effect 必須 Working」、「Passive effect 排除 OnLeave」這類條件分支邏輯（屬規則，非數值）
- `instanceID` 起始值 `1`（§6.7 InitializeAsNewGame 預設）
- 1 day = 86400 秒（時間單位常數，屬語言層）

---

## 7. 邊緣案例對策（Edge Case Handling）

對齊 GDD §5（5.1 ~ 5.5）共 5 子節 17 條邊緣案例。

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 系統時鐘倒退（now < hiredTimestamp） | `reallocatingRemainingSec` / cooldown 比較使用 `Mathf.Max(0, now - reallocatingStart)`；負值 clamp 至 0；不拋例外 | StaffService（GetStaffStateView）；StaffEffectAggregator 不受影響 | EditMode test：注入 mock TimeSystem 倒退時間 |
| 離線跨日（Phase 2 薪水補發） | §3.9.4 邏輯：`offlineDays = floor((now - lastSalaryTimestamp) / 86400) clamped by OFFLINE_MAX_SECONDS / 86400`；逐 cycle 呼叫 OnSalaryTick | StaffService.ProcessOfflineSalary（Phase 2） | EditMode test (Phase 2)：注入 lastSalaryTimestamp 偏移 |
| `reallocatingStartTimestamp == 0`（舊存檔 / 無 slot 能力穩態） | CheckReallocatingAutoLeave 直接 continue；TryAssignStaff 進 Working 時保持 0；TryUnassignStaff 設為 now | StaffService.CheckReallocatingAutoLeave / TryUnassignStaff | EditMode test：構造 `reallocatingStart=0` 的 Reallocating instance |
| `lastSalaryTimestamp == 0`（首次解鎖） | §3.9.7 `OnStaffSystemUnlocked` callback：無條件 reset 為 now；不補發歷史 | StaffService.OnStaffSystemUnlocked（Phase 2） | EditMode test (Phase 2) |
| `TryAssignStaff` 指派建築 capacity 已滿 | step 7 capacity 檢查 → `BUILDING_FULL`；roster 不變、不發事件 | StaffService.TryAssignStaff | EditMode test (AC-8) |
| `TryAssignStaff` 同建築重複指派（idempotent，B-6） | step 4 idempotent 短路：直接回 SUCCESS、不發事件、不查冷卻、不重設 cooldown | StaffService.TryAssignStaff | EditMode test (AC-7) |
| `TryAssignStaff` 冷卻中（B-5） | step 6 冷卻檢查 → `SWITCH_COOLDOWN`；任何「進入 Working」transition 皆被擋 | StaffService.TryAssignStaff | EditMode test：注入 cooldownEnd > now |
| `TryUnassignStaff` 對 OnLeave 職員 | step 3 → `STAFF_ON_LEAVE` | StaffService.TryUnassignStaff | EditMode test |
| `TryFireStaff` 對 Working 職員（B-7） | 不執行 unassign 流程；只發 OnStaffFired、不發 OnStaffAssigned / OnStaffStateChanged；effect 立即停用（roster.Remove 後即時遍歷不再含此 instance） | StaffService.TryFireStaff | EditMode test (AC-14) |
| HireStaff 後超 `REALLOCATING_AUTO_LEAVE_SECONDS` 未指派（具 slot 能力） | OnMinuteTick 自節流觸發 CheckReallocatingAutoLeave → 自動轉 OnLeave | StaffService.CheckReallocatingAutoLeave | PlayMode test：時間快進 |
| HireStaff 後超 `REALLOCATING_AUTO_LEAVE_SECONDS` 未指派（無 slot 能力） | `HasSlotCapability=false` 直接 continue；Reallocating 為穩態，passive effect 永久生效 | StaffService.CheckReallocatingAutoLeave | EditMode test：構造無 slotBuildingIDs 職員 |
| 離線跨日 N 天（Phase 2） | §3.9.4 補發迴圈逐 cycle 發 OnStaffSalaryDue | StaffService.ProcessOfflineSalary | EditMode test (Phase 2) |
| OnLeave 職員（薪水）（Phase 2） | AssemblePerStaffSalary 跳過 OnLeave；薪水 = 0 不入 perStaffSalary | StaffService.AssemblePerStaffSalary | EditMode test (Phase 2) |
| `StaffTable.salary` 為負值 | DataManager 載入時 throw `StaffTableValidationException`；salary = 0 為 Jam 合法值 | StaffTableLoader.Validate | EditMode test：注入負值 CSV |
| 同 frame 內多個 OnStaffSalaryDue 觸發（Phase 2） | EventBus 序列化發布；FT-05 訂閱者依序處理；§3.9.3 `dueTimestamp <= _lastSalaryTimestamp` 重複守護擋同日重複 | StaffService.OnSalaryTick | EditMode test (Phase 2) |
| `StaffTable.staffID == 0` | 載入時 LogWarning + 跳過該行（GDD §5.4 表格） | StaffTableLoader.Validate | EditMode test |
| `StaffTable.effectIDs` / `effectValues` 長度不一致 | 載入時 throw `StaffTableValidationException`（M-6 fail-fast） | StaffTableLoader.Validate | EditMode test |
| `StaffTable.minGuildLevel < 1` | LogWarning + clamp 至 1 | StaffTableLoader.Validate | EditMode test |
| `StaffInstance.staffID` 還原時找不到 StaffTable 行 | RestoreFromSave step 2 → throw `CriticalRestoreFailedException` | StaffService.RestoreFromSave | EditMode test (AC-27) |
| 未解鎖時呼叫 API | §3.10.1 全部走早退路徑、回 `STAFF_SYSTEM_LOCKED` / `0` / `false` | StaffService 所有 API；StaffEffectAggregator | EditMode test (AC-1、AC-31) |
| 解鎖瞬間（OnBuildingUpgraded buildingID=6 0→1） | §3.10.5 訂閱：Phase 2 `lastSalaryTimestamp ← now`；同時為跨閾值具 slot 能力職員 reset reallocatingStart ← now（M-3） | StaffService.OnStaffSystemUnlocked | EditMode test (AC-2 Phase 2) |
| 降級後再解鎖 | StaffInstance[] 全部保留；effect 立即恢復查詢；reallocatingStart reset 行為見上一行 | StaffService（資料保留無特殊邏輯） | EditMode test |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

對齊到 GDD §3.X 二層粒度。

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 StaffInstance schema | §5.3（StaffInstance struct）+ §6.7（持久化） | 對齊 | 8 欄位完整對應；`_nextInstanceID` 全域 counter 對應 |
| §3.2 StaffTable Schema + StaffEffect / StaffUIFlag enum | §5.3（StaffData / 兩 enum）+ §6.1 + §6.3 | 對齊 | 13 欄位 + 4 enum + 1 UIFlag 全列；rarity 對應 effect 數量上限交由 StaffTableLoader 驗證（§5.4） |
| §3.3 入職流程（HireFlow + 失敗路徑 + 設計原則） | §5.1（HireResult / HireStaffResult）+ §5.4.2 | 對齊 | step 1~7 完整對應；B-9 A 案 hasSlot 分流 reallocatingStart |
| §3.4 Effect 聚合（含 UI flag OR 與降級早退） | §5.4.9 + §6.1 + §4.1 | 對齊 | 5 API 演算法 + cap + UI flag OR 全對應；即時遍歷無快取保證 |
| §3.5 Slot 指派（TryAssignStaff / TryUnassignStaff） | §5.1 + §5.4.3 + §5.4.4 | 對齊 | 全 result enum 與 step 1~10 / 1~6 完整對應；B-5 / B-6 案語意保留 |
| §3.6 三態狀態機（含冷卻 B-5 語意） | §5.3（StaffState）+ §5.4 各 transition + §7 邊緣案例 | 對齊 | 9 條 transition 表 + 冷卻 gate + 持久化欄位驗證皆有處理路徑 |
| §3.7 Reallocating 自動轉假（含入口掃描表 + 無 slot 例外 + M-3 解鎖 reset） | §5.4.8 + §5.4.3 / §5.4.4 / §5.4.5 / §5.4.6 / §5.4.7 step 1.5 + §7 | 對齊 | 6 入口掃描全部加 step 1.5；HasSlotCapability 過濾與 §3.7.5 表三條穩態 / 切建築 / 降級 reset 全對應 |
| §3.8 解雇流程（含事件先於 Remove + Working 直接銷毀 B-7） | §5.4.7 + §5.2（OnStaffFired 順序）+ §7 | 對齊 | step 1~6 + §3.8.4 Publish 先於 Remove 不可調設計決策保留 |
| §3.9 薪水管線（**Phase 2 整節**） | §5.4.10 + §1.2（Out-of-Scope）+ §7（Phase 2 標記） | 對齊（整節 Phase 2） | Jam 版不訂閱 OnDailyReset、不發 OnStaffSalaryDue；事件契約保留 |
| §3.10 系統降級行為總表 | §5.1 / §5.4.2 ~ §5.4.10 各 API step 1 + §7（系統邊界） | 對齊 | 全 API 早退一律於 step 1 query `IsStaffSystemUnlocked`；M-3 解鎖 reset reallocatingStart 於 §5.4 對應位置 |
| §3.11 事件契約（5 出 + 4 入 + 發布順序） | §2.5 + §5.2 + §5.4 各流程 publish 步驟 + §3.8.4 | 對齊 | 9 條發布順序 row 全對應；OnStaffSalaryDue Jam 版 no-op 標記保留 |

### 8.2 公式對齊或替代說明

對應 GDD §4：

- §4.1 Effect 聚合上限公式：FSD §5.4.9 / §6.1 直接採用偽碼（min / max + cap）；不替代
- §4.2 薪水公式（Phase 2）：FSD §5.4.10 直接採用；4 條變數定義與範例完整保留；Jam 版整段不執行（B 案 2026-04-25 使用者裁示）

無公式替代。

### 8.3 未能實現的規則與修改建議

> 標記法：**B**（建議項，不阻擋實作）/ **D**（決策待定）/ **C**（衝突，需使用者裁決，已於 §8.5 登記）

| 編號 | 類別 | 來源 | 摘要 | 建議處理 |
| --- | --- | --- | --- | --- |
| B-01 | DS 待建 | §0 / §6.1 / §2.2 | `【FT-12-DS】staff-table.md` 與 `【FT-08-DS】staff-tuning.md`（FT-08 / FT-12 共用）皆 _待建_ | 由 DS-designer 補建；FSD 撰寫期沿用 GDD §3.2 / §7 schema |
| B-02 | DS 待建 | §6.1 | `【FT-07-DS】building-table.md` _待建_，FT-12 透過 `IBuildingService.GetSlotCount` 間接消費 `slotCount` | 待 FT-07 DS 落地後在 §2.2 / §6.1 補引用列 |
| B-03 | DS 命名 | §3.2 | StaffTuning 內 `EFFECT_MAX_*` 常數命名與 SystemConstants 各前綴不同（FT-12 / FT-08 共用）；未來 DS 撰寫需明確哪些 key 屬 FT-12 | DS-designer 撰寫 staff-tuning.md 時逐 key 標註 owner（FT-08 / FT-12 / 共用） |
| B-04 | UnFile-able 旗標 | §6.7 | ISaveable `IsCritical = true` 對應 FT-10 critical 路徑 | FT-10 FSD 待撰寫，本 FSD 先依 GDD §6.7 寫定 OwnerKey；FT-10 落地時 §2.5 補 ISaveable 簽章對齊 |
| B-05 | F-02 OnHourTick 缺位 | §3.7.3 / §5.4.8 | F-02 無 OnHourTick；FT-12 改訂閱 OnMinuteTick + 自節流（每 3600s） | 已採 GDD 既有方案，無需修正；建議 StaffTuning 補登 `AUTO_LEAVE_SCAN_INTERVAL_SECONDS` 表格化（D-01） |
| D-01 | 寫死建議 | §6.3 | `Auto-leave 自節流間隔 = 3600s` 目前在 GDD §3.7.3 文字描述「每 3600s」，未進 StaffTuning | 建議補登入 StaffTuning.csv（key = `AUTO_LEAVE_SCAN_INTERVAL_SECONDS`，預設 3600，安全範圍 60~7200）；目前 FSD 列入 §6.3 嚴禁寫死清單最後一行作為提醒 |
| B-06 | StaffService 接近 500 行 | §4.4 | StaffService 預估 450~550 行；若實作期確實超過 500 觸發 §2.4 拆分判斷 | 建議 Post-Jam 將 `OnSalaryTick` / `ProcessOfflineSalary` / `AssemblePerStaffSalary` 抽成 `StaffSalaryPipeline.cs`（Phase 2 啟用時順勢拆分） |
| B-07 | FT-08 CandidateCard 跨型別 | §5.3 | FT-12 `HireStaff(CandidateCard)` 引用 FT-08 GachaTypes；StaffTypes.cs 是否 mirror 定義或 reference | 建議直接 reference FT-08 命名空間的 CandidateCard struct，僅讀 `staffID` 欄位；避免重複定義造成 schema drift |
| B-08 | M-3 解鎖 reset 範圍 | §3.10.5 | OnStaffSystemUnlocked 同時要做 (a) Phase 2 `lastSalaryTimestamp ← now`、(b) 跨閾值具 slot 能力職員 reset reallocatingStart | Jam 版只執行 (b)；訂閱 callback 內以 `// Phase 2: lastSalaryTimestamp ← now` 註記保留 |
| B-09 | TryGoOnLeave 邊界 | §3.6.2 row 8 / §3.11.2 row 6 | TryGoOnLeave 從 `Reallocating` 進 `OnLeave` 時 oldBuildingID 已是 0 不發 OnStaffAssigned，與 M-4 表述對齊 | FSD §5.4.5 step 4 `if (oldBuildingID > 0)` 守護已涵蓋；無需額外修正 |
| B-10 | EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC 單位 | §4.1 / §6.1 | StaffTuning.csv 該 key 值為秒（int），與其他 float 比率混存於同表 | StaffTuning DS 落地時欄位 type 標 `int / float` 混型；StaffTableLoader 透過不同 GetInt / GetFloat API 讀取 |
| B-11 | FT-07 API 對齊（已修） | §2.3 / §5.4.3 step 7 / §6.1 / §6.3 | 2026-04-28 design-review 發現原 FSD 引用不存在的 `GetBuildingState(int).currentLevel` / `GetSlotCount(int, int)`；FT-07 FSD §5.1 line 203 / §8.3 B-02 明文「不新增 GetSlotCount API」 | 已 patch（2026-04-28）：API 改為 `IBuildingService.IsStaffSystemUnlocked()` + `GetBuildingLevel(int)`；capacity 改走 `IDataManager.Get<BuildingData>((buildingID, level)).slotCount` 直讀；§4.4 StaffService 依賴介面補 `IDataManager`；§6.3 對應行同步更新；GDD §6.1 「上游依賴」row 4 與 FT-07 FSD 不一致，§8.4 留回註意圖 |

無 GDD 規則無法實現的硬阻擋項；上列均為建議項或表格化補登提醒。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-28 | `【FT-12】staff-system.md` | §6.1 row 4（FT-07 上游依賴） | **回註意圖**（待主體覆核後寫入 GDD）：將「`GetBuildingState(buildingID).currentLevel`」改為「`GetBuildingLevel(buildingID) : int`（FT-07 FSD §5.1 既定簽章）；capacity 由 FT-12 直讀 `BuildingTable[(buildingID, level)].slotCount`」。對齊 FT-07 FSD §8.3 B-02「不新增 GetSlotCount API」決議與 FT-07 GDD §6.2 既有「FT-12 直讀 CSV」描述。 |

D-01「Auto-leave 自節流間隔表格化」屬建議項，待 DS 落地後若使用者同意再回註 GDD §3.7.3 / §7.2。

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |

無衝突。GDD §3.6.2 表格與 §3.11.2 發布順序表已整合 B-5 / M-3 / M-4 / B-7 / B-9 案，FSD 直接套用無內部矛盾；§3.9 整節 Phase 2 與 §3.10.2 / §3.10.3 一致註記「Jam 範疇下永不發布 / 不訂閱」，無需進一步裁決。

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥（GDD 版本、3 份 Data-Specs 引用標 _待建_、撰寫者 / Review 者 / 狀態 / 日期）
- [x] §1.3 完成目標可被測試驗證（11 條 DoD 條目皆對應 §8 AC-1~AC-35）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節、3 份 Data-Specs、6 上游、7 下游、8 事件契約）
- [x] §3.3 對映表覆蓋所有幻想／目的（8 條對映含 Jam / Phase 2 / 降級對應）
- [x] §4 Script 清單欄位齊全（5 Script，路徑 / SRP / 依賴介面 / 預估規模皆填）
- [x] §5 API/事件/資料結構/資料流齊備（5.1 + 5.2 + 5.3 + 5.4 11 個資料流）
- [x] §6 CSV 引用含對應 Data-Specs（4 表，3 份 DS 引用）；§6.3 嚴禁寫死清單 14 項對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（GDD §5 共 17 條全部有「程式處理 / 涉及 Script / 驗證方式」）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（§3.1~§3.11 共 11 子節全部「對齊」）
- [x] §8.2~§8.5 如實登記（公式直接採用 / 無替代；§8.3 列 10 條建議；§8.4 無回註；§8.5 無衝突）
- [ ] FSD-index §6.1 / §7.1 / §7.2 已同步更新（待本 FSD 寫入後執行 §3.7 step）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh） | 通過 | 通過 | 通過 | 結構：§0~§8 + 附錄 A 章節編號 / 標題 / 順序與 FSD-index §三完全一致；每章皆有實質內容。邏輯：未拆分理由成立（職責耦合 / 對齊 FT-08 / FT-09 先例 / StaffService 仍可控）；5 Script 職責清晰；API / 事件 / 資料流 11 個流圖前後一致；無自相矛盾。GDD 對齊：§3.1~§3.11 共 11 子節皆「對齊」；§4.1 公式直接採用；§5 共 17 條 EC 皆有對策；§6 7 上游 + 7 下游 + 8 事件契約完整列舉；§7 14 個寫死禁止項表格化；無真實衝突；§8.3 列 10 條建議項（B-01~B-10 + D-01）皆不阻擋實作。 |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh，design-review patch） | 通過 | 通過 | 通過 | 對應第二輪 `/design-review FT-12-FSD` NEEDS REVISION 全修。HIGH：§2.3 / §5.4.3 step 7 / §6.1 / §6.3 / §4.4 五處同步從 `GetBuildingState`+`GetSlotCount` 改為 `GetBuildingLevel` + `IDataManager.Get<BuildingData>` 直讀（B-11 登記）。MEDIUM：§5.2 補事件 struct `OnXxxEvent` 後綴慣例 note；§5.4.1 step 5 + 7 採方案 (b) C# const `StaffPhase2.SalaryEnabled = false` 取代「Phase 2 旗標」字面；新增 §5.4.12 Subscribe / Unsubscribe 生命週期說明（OnDestroy / OnDisable 對稱）。LOW：§5.4.10 入口偽碼澄清 `dueTimestamp = TimeSystem.NowUTC`（OnDailyResetEvent payload 為空 struct）；§5.4.11 step 3 排序加 `(hiredTimestamp ASC, instanceID ASC)` 雙鍵保證決定性。§8.4 新增 GDD 回註意圖 1 條（FT-12 GDD §6.1 row 4 待同步 FT-07 API）；§8.3 B-11 登記。本次 patch 不變動章節結構、不影響 GDD 對齊 11 子節「對齊」狀態、不影響 §1.3 DoD / §8.1 / §8.2 / §8.5 既有判定。 |
| 2026-04-30 | Claude Code 主體 | 待 patch | 待 patch | 待 patch | **v3.1 patch P3.1-008 同步紀錄**：FT-12 GDD + DS 已寫入 v3.1 patch（IsStaffHired API + StaffTable 5 個 Post-Jam 預埋欄位 + 米拉 501/譚恩 502/凱拉 503 三位敘事核心職員 CSV 預設資料）。FSD Script 設計待補：(1) IStaffService 補 `IsStaffHired(staffID) : bool` API 方法；(2) StaffService 實作該 API（`_activeRoster.Any(s => s.staffID == staffID)`）；(3) StaffData 結構新增 5 個 Post-Jam 預埋欄位（personalityDesc / intimacyLevel / isLeavePossible / mood / personalEventIDs），Jam 版 DataManager 載入但 runtime 不消費。完整 patch 規格見 `_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` §3.4。 |
