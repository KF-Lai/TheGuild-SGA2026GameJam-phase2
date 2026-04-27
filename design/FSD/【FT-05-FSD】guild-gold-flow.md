# 【FT-05-FSD】功能規格說明書 — Guild Gold Flow

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-05】guild-gold-flow.md`（版本：2026-04-22，設計決策備忘 8 條已定案） |
| 對應 Data-Specs | `【F-01-DS】system-constants.md`（共用金流常數：`COMMISSION_RATE` / `PENALTY_RATE` / `GOLD_INITIAL` 等） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-05 Guild Gold Flow 是公會所有金流操作的統一執行者。系統訂閱四個上游事件，分別執行**委託預收**（`OnCommissionAccepted` → `AddGold`）、**委託結算**（`OnMissionResolved` → `AddGoldAllowBankruptcy`，含加成查詢與淨額計算）、**設施維護費扣款**（`OnGuildMaintenanceDue`，Phase 2 no-op）、**職員薪水扣款**（`OnStaffSalaryDue`，Phase 2 no-op）。所有外流金流透過 F-03 `AddGoldAllowBankruptcy` 執行，允許突破破產門檻。每次金流均快照 `bankruptcyStateBefore / After` 並隨事件 payload 發布，供下游 P-02 / P-03 渲染金幣動畫與破產通知。Jam 版委託金流完整實作；維護費 / 薪水管線因上游 FT-07 / FT-12 Jam 版不發布對應事件，程式雖到位但為 no-op。

### 1.2 In-Scope / Out-of-Scope

**In-Scope（Jam 版）**

- 訂閱 `OnCommissionAccepted`，執行預收並發布 `OnCommissionPrepaid`
- 訂閱 FT-04 `OnMissionResolved`，即時查詢加成，計算有效率，執行結算並發布 `OnCommissionSettled`
- 共用 `ExecuteGoldFlow` 機制（金幣 + 破產狀態 before/after 快照 + `AddGoldAllowBankruptcy` 呼叫）
- 訂閱 `OnGuildMaintenanceDue` / `OnStaffSalaryDue`（程式實作到位，Jam 版上游不發布，no-op）
- 所有輸入防禦性驗證（`baseReward ≤ 0`、`totalAmount ≤ 0`、`null` dict）
- FT-07 / FT-12 降級策略（null-check → bonus = 0）

**Out-of-Scope**

- 結算擲骰、condition trait 套用（FT-04 職責）
- 上限規則、離線追認時序（FT-03 / 未來 FT-11）
- 聲望變動（FT-04 直接呼叫 F-03 `AddReputation`）
- 金幣上下限、破產狀態機內部邏輯（F-03）
- 維護費 / 薪水金額計算與觸發時機（FT-07 / FT-12）
- 破產後的遊戲結束流程（FT-06 Guild Core）
- 金流歷史快取 / 收支日誌（FT-10 Save/Load，Jam 範疇外）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，補充程式可驗證條件：

| AC ID | 驗收條件 | 驗證方式 |
| --- | --- | --- |
| AC-1 | `OnCommissionAccepted(id=100, baseReward=600, PlayerManual)` 後金幣 `+600`，`OnCommissionPrepaid(100, 600, PlayerManual)` 已發布 | EditMode / PlayMode 事件流測試 |
| AC-2 | `baseReward ≤ 0` 時金幣不變、未發布 `OnCommissionPrepaid`、console 有 `[FT-05] error` | EditMode 單元測試 |
| AC-3 | 金幣 = `9,999,900`，預收 500 → 金幣 = `9,999,999`，`prepaidAmount = 99` | EditMode 測試 |
| AC-4 | 成功結算 `baseReward=600`，FT-12 null → `netDelta=+120`，`effectiveCommissionRate=0.20` | EditMode 事件流測試 |
| AC-5 | 失敗結算 `baseReward=600`，FT-12 null → `netDelta=-60`，`effectivePenaltyRate=0.10` | EditMode 事件流測試 |
| AC-6 | FT-12 回傳 `+0.02 / -0.02` → 成功 `commissionGoldAmount=132`；失敗 `penaltyGoldAmount=48` | EditMode mock FT-12 測試 |
| AC-7 | `conditionGoldBonus=500`：成功 `netDelta = commission+500`；失敗 `netDelta = -penalty+500` | EditMode 事件流測試 |
| AC-8 | `CommissionBreakdown` **必填欄位已填入；分支合法為 0 的欄位依路徑驗證**（FSD-Codex-Reoprts-260427 T1-FT05 修正：原「20 欄皆非空值」過嚴 — `conditionGoldBonus=0`、未招募會計 bonus=0、成功時 `penaltyGoldAmount=0`、失敗時 `commissionGoldAmount=0` 為合法分支值）；非 clamp 情境下 `goldBefore + netDelta == goldAfter` | EditMode 斷言測試（依分支驗證對應欄位） |
| AC-9 _(Phase 2 / Deferred)_ | 維護費 `totalAmount=95`，金幣 `500→405`；defensive copy 驗證。**FSD-Codex-Reoprts-260427 T1-FT05：標 Phase 2**（FT-07 維護費觸發未在 Jam 範圍） | Phase 2 EditMode 測試 |
| AC-10 _(Phase 2 / Deferred)_ | 薪水 `totalAmount=90`，金幣 `500→410`；`SalaryBreakdown.items` 含 2 項。**T1-FT05：標 Phase 2**（FT-12 職員薪水觸發未在 Jam 範圍） | Phase 2 EditMode 測試 |
| AC-11 _(Phase 2 / Deferred)_ | `totalAmount ≤ 0` 或 `items=null` → 金幣不變、未發布 `Charged` 事件、console error。**T1-FT05：標 Phase 2** | Phase 2 EditMode 測試 |
| AC-Jam-12 _(Jam 驗收新增)_ | 無 `OnGuildMaintenanceDue` / `OnStaffSalaryDue` 上游事件時，`GoldFlowService` 不產生固定支出金流；金幣不變、`Charged` 事件未發布（T1-FT05 補：取代 AC-9/10/11 的 Jam 驗收） | EditMode 測試 |
| AC-12 | 金幣 `40`，失敗 `baseReward=600` → 金幣 `-20`，`before=Normal, after=Warning` | EditMode 測試 |
| AC-13 | 金幣 `-80`（Warning），成功 `baseReward=600` + 會計加成 → 金幣 `+52`，`before=Warning, after=Normal` | EditMode 測試 |
| AC-14 | 維護費 / 薪水扣款觸發 `Normal → Warning` 快照正確 | EditMode 測試 |
| AC-15a | `OnEnable` 訂閱 4 個 In 事件；`OnDisable` 取消；重複 enable/disable 無 listener leak | PlayMode 生命週期測試 |
| AC-15b | FT-07 / FT-12 = null 時，AC-4 / AC-5 仍通過（預設率不變） | EditMode 降級測試 |
| AC-15c | CSV 修改 `COMMISSION_RATE=0.30` 後，`commissionGoldAmount` 由 120 變 180（無需改程式） | 手動驗證：改 CSV → 重啟 → 觀察結算結果 |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 引用目的 |
| --- | --- |
| §1 概要（含 Scope Note 2026-04-23） | 系統範圍、Jam vs Phase 2 分界、7 項核心職責 |
| §2 玩家幻想 | 「帳本的重量」三拍節奏設計意圖 |
| §3.1 Payload 資料結構（§3.1.1~§3.1.3） | CommissionBreakdown / MaintenanceBreakdown / SalaryBreakdown 20+ 欄定義 |
| §3.2 預收管線 | `OnCommissionAccepted` 處理流程（防禦、`AddGold`、`prepaidAmount = GetGold() - prevGold`） |
| §3.3 委託結算管線 | `OnMissionResolved` 六步處理順序（不可調換） |
| §3.4 加成查詢與有效率計算 | FT-12 / FT-07 null-check 降級；`effectiveCommissionRate` / `effectivePenaltyRate` 計算 |
| §3.5 成功 / 失敗淨額計算 | `ComputeSuccessNet` / `ComputeFailureNet` 公式；`Mathf.RoundToInt` Banker's rounding |
| §3.6 維護費扣款管線（Phase 2） | `OnGuildMaintenanceDue` 處理；defensive copy；Phase 2 no-op 說明 |
| §3.7 職員薪水扣款管線（Phase 2） | `OnStaffSalaryDue` 處理；Phase 2 no-op 說明 |
| §3.8 破產狀態轉移快照機制（共用） | `ExecuteGoldFlow` 四步流程；狀態轉移對照表 |
| §3.9 事件契約 | 上游 4 個 In 事件 + 下游 4 個 Out 事件的 payload 定義與訂閱者責任 |
| §3.10 查詢 API | 純事件驅動，無對外查詢 API |
| §4 公式（§4.1~§4.3） | 有效率公式、淨額公式、完整結算範例 A~F |
| §5 邊緣案例（§5.1~§5.6） | 輸入驗證、時序異常、數值極值、降級、破產互動、訂閱者異常 |
| §6 依賴關係（§6.1~§6.5） | 上下游依賴、事件契約矩陣、反向依賴登記清單、開發序 |
| §7 可調參數（§7.1~§7.4） | `COMMISSION_RATE` / `PENALTY_RATE`、外部 Knob、隱藏常數、平衡指引 |
| §8 驗收標準（§8.1~§8.6） | AC-1~AC-15c |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `COMMISSION_RATE`、`PENALTY_RATE` | 基礎傭金率與賠償率；於 `GoldFlowService` 啟動時由 F-01 DataManager 讀入 |

### 2.3 上游依賴系統

> **介面命名規範**：本表中以 `IXxxService` / `IXxxSystem` 命名的介面僅為敘述方便（沿用 GDD 用語），實作契約以既有 concrete singleton 為準（`DataManager.Instance` / `TimeSystem.Instance` / `ResourceManagement.Instance` / static `EventBus`）。詳見 `FSD-index.md` §2.10。

| 依賴系統 | 介面型態 | 用途 | 降級策略 |
| --- | --- | --- | --- |
| F-01 DataManager | API（`IDataManager.GetFloat`） | 讀 `COMMISSION_RATE` / `PENALTY_RATE` | 無（必備） |
| F-02 Time System | API（`ITimeService.NowUTC`） | 寫入 `settleTimestamp` / `chargeTimestamp` | 無（必備，時間源統一） |
| F-03 Resource Management | API（`IResourceService`） | `AddGold` / `AddGoldAllowBankruptcy` / `GetGold` / `GetBankruptcyWarningState` | 無（必備） |
| FT-04 Outcome Resolution | Event（`OnMissionResolved`） | 消費 `Outcome`：`isSuccess`、`baseReward`、`missionDifficulty`、`conditionGoldBonus` | 無此事件 → 委託結算未觸發，預收仍正常運作 |
| FT-07 Guild Building | Event（`OnGuildMaintenanceDue`，Phase 2） + API（`GetBuildingPenaltyBonus`） | 維護費扣款；建築賠償加成（Jam = 0） | `null` → `buildingPenaltyBonus = 0`；事件未發 → no-op |
| FT-12 Staff System | Event（`OnStaffSalaryDue`，Phase 2） + API（`GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus`） | 薪水扣款；會計加成查詢 | `null` → 兩 bonus 皆 `0`；事件未發 → no-op |
| FT-02 / FT-03 / 未來 FT-11 | Event（`OnCommissionAccepted`） | 發布預收觸發事件 | 無此事件 → 無預收流程，結算仍可執行但語義殘缺 |
| EventBus | 基礎設施（`EventBus.Subscribe` / `Publish`） | 訂閱 In 事件；發布 Out 事件 | 無（必備） |
| C-05 Trait System（間接透過 FT-04） | 資料（`Outcome.conditionGoldBonus`） | 特質獎勵通道（通用加法，成敗皆加） | 無 trait → `conditionGoldBonus = 0` |

### 2.4 下游被依賴系統

| 消費系統 | 訂閱事件 | 主要用途 |
| --- | --- | --- |
| P-02 Main UI | `OnCommissionPrepaid`、`OnCommissionSettled`、`OnMaintenanceCharged`、`OnSalaryCharged` | 金幣動畫、結算視窗明細、扣款細項顯示 |
| P-03 Notification | 全 4 個 Out 事件 | 依 `bankruptcyStateBefore / After` 轉移對照表選通知模板 |
| FT-10 Save/Load | 全 4 個 Out 事件（Jam 範疇外） | 未來收支日誌歷史 |
| FT-06 Guild Core | 不直接訂閱 FT-05；透過 F-03 `OnBankruptcyWarningStateChanged` | Game Over 流程（FT-05 僅觸發狀態轉移，不介入結果） |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 定義位置 / 發布時機 |
| --- | --- | --- | --- |
| `OnCommissionAccepted` | In | `(int missionID, int baseReward, DispatchSource source)` | FT-05 定義契約；FT-02 於玩家審核通過後發布（`PlayerManual`）；FT-02 代 FT-03 於 NPC 自主接單時發布（`NpcAutoPick`）；未來 FT-11 於離線追認時發布（`OfflineAutoPick`）。**enum 由 FT-02 FSD-A 定義（§5.3）**，FT-05 不重複宣告 |
| `OnMissionResolved` | In | `Outcome` | FT-04 §3.8；FT-04 於任務結算後發布 |
| `OnGuildMaintenanceDue` | In | `(long dueTimestamp, Dictionary<int,int> perBuildingCost, int totalAmount)` | FT-05 定義契約；FT-07 協同 F-02 tick 於每日重置時發布（Phase 2） |
| `OnStaffSalaryDue` | In | `(long dueTimestamp, Dictionary<int,int> perStaffSalary, int totalAmount)` | FT-05 定義契約；FT-12 協同 F-02 tick 定期發布（Phase 2） |
| `OnCommissionPrepaid` | Out | `(int missionID, int prepaidAmount, DispatchSource source)` | FT-05 發布；預收 `AddGold` 完成後立即發布；source 透傳自上游 `OnCommissionAccepted` |
| `OnCommissionSettled` | Out | `CommissionBreakdown` | FT-05 發布；委託結算 `ExecuteGoldFlow` 完成、timestamp 寫入後發布 |
| `OnMaintenanceCharged` | Out | `MaintenanceBreakdown` | FT-05 發布；維護費 `ExecuteGoldFlow` 完成後發布（Phase 2） |
| `OnSalaryCharged` | Out | `SalaryBreakdown` | FT-05 發布；薪水 `ExecuteGoldFlow` 完成後發布（Phase 2） |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

GDD §2 的核心幻想：接受委託的瞬間金幣跳動帶來「有錢了」的短暫愉悅，但玩家清楚那筆錢是押金，不敢花用；等待期間持續感知「那個數字」的真實重量；結算那一刻才是整個系統存在的理由——成功淨得傭金，失敗淨損賠償，最壞情況直接穿越零線觸發破產倒數紅字。這三拍節奏把「接一單」從點的決定拉長成有呼吸的過程，讓玩家學會金幣永遠分成「自己的」與「押在委託上的」兩半。

### 3.2 系統目的還原

GDD §1：FT-05 是公會所有金流操作的統一執行者。委託金流（預收 + 結算）提供「帳本的重量」，讓每一張委託的風險與收益可量化感知；固定支出（維護費 + 薪水）在 Phase 2 導入「公會規模越大成本越高」的正向壓力，防止玩家無限擴張而喪失決策張力。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 接委託瞬間金幣跳動 | 委託板審核通過後金幣欄 `+baseReward` 動畫 | 訂閱 `OnCommissionAccepted`，呼叫 `F03.AddGold(+baseReward)`，發布 `OnCommissionPrepaid`（P-02 消費動畫） |
| 預收金不敢花用（視覺暗示） | 金幣欄顯示「押金中 n 筆」或特殊色調 | `OnCommissionPrepaid` payload 含 `missionID` / `prepaidAmount`，P-02 自行決策顯示邏輯（FT-05 不渲染） |
| 結算淨得 / 淨損、傭金率透明 | 結算視窗顯示 baseReward、rate、conditionBonus、netDelta | `OnCommissionSettled` payload 帶完整 `CommissionBreakdown`（20 欄），P-02 渲染明細視窗 |
| 失敗時金幣可能穿越零線 | 金幣變負、破產警告紅字倒數 | `AddGoldAllowBankruptcy` 允許突破零線；`bankruptcyStateBefore / After` 快照讓 P-03 辨識「Normal→Warning」轉移並選對應通知模板 |
| 會計 / 保險櫃讓損益改變 | 結算明細顯示「傭金率 22%」「賠償率 8%」 | 每次結算即時查詢 FT-12 `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus`；FT-07 `GetBuildingPenaltyBonus`；breakdown 欄位快照查詢結果供 P-02 顯示 |
| 金流可調（難度調整） | 修改 CSV 後傭金率立即生效 | `COMMISSION_RATE` / `PENALTY_RATE` 來自 F-01 DataManager 讀入的 `SystemConstants.csv`，不寫死於程式 |
| Phase 2：公會成本感 | 定期扣款 + 破產警告擴大 | `ExecuteGoldFlow` 共用機制讓維護費 / 薪水走相同快照流程；breakdown 結構對稱委託結算，P-03 可用同一模板顯示不同來源的破產警告 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。

### 4.2 拆分理由

預估 3 個 Script 合計約 350~420 行，低於 500 行拆分門檻。四條管線（預收、結算、維護費、薪水）共用 `ExecuteGoldFlow` 與三個 Breakdown 資料型別，在資料層高度耦合，強制拆分反而增加跨 Script 型別依賴。GDD §3 未出現獨立的池管理或複雜演算法子系統（不同於 FT-02 的 CommissionBoard 池管理）。維護費與薪水為 Phase 2 no-op，本身每管線僅 ~20 行有效邏輯，不構成獨立 Script 理由。`DispatchSource` enum 從 FT-02 FSD-A §5.3 引用，FT-05 不重複定義（2026-04-27 patch 對齊 GDD P-001）。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `GoldFlowTypes` | `Assets/Scripts/Gameplay/GoldFlow/GoldFlowTypes.cs` | 定義金流相關資料型別：`CommissionBreakdown`、`MaintenanceBreakdown`、`SalaryBreakdown`（**不**重複定義 `DispatchSource`，從 FT-02 FSD-A §5.3 引用） | 無（純資料） | `60~80 行` |
| `IGoldFlowService` | `Assets/Scripts/Gameplay/GoldFlow/IGoldFlowService.cs` | 定義 FT-05 的空介面（供未來 mock 測試與 DI 使用） | 無 | `< 20 行` |
| `GoldFlowService` | `Assets/Scripts/Gameplay/GoldFlow/GoldFlowService.cs` | MonoBehaviour：訂閱 4 個 In 事件，執行預收 / 結算 / 維護費 / 薪水 4 條管線，呼叫 F-03 `AddGold` / `AddGoldAllowBankruptcy`，發布 4 個 Out 事件 | `IResourceService`（F-03）、`IDataManager`（F-01）、`ITimeService`（F-02）、`IStaffService`（FT-12，可 null）、`IGuildBuildingService`（FT-07，可 null）、`EventBus` | `250~300 行` |
| `GoldFlowEvents` | `Assets/Scripts/Gameplay/GoldFlow/Events/GoldFlowEvents.cs` | 集中宣告 typed event struct：`OnCommissionPrepaidEvent`、`OnCommissionSettledEvent`、`OnMaintenanceChargedEvent`、`OnSalaryChargedEvent`（payload 欄位以 §3.1 / §3.9 既有定義為準） | — | `< 50 行` |

### 4.5 類別關係

```
GoldFlowTypes（純資料型別）
    ← 被 GoldFlowService 建構實例
    ← 被 P-02 / P-03 / FT-10 消費（透過事件 payload）

IGoldFlowService（空介面）
    ← GoldFlowService 實作

GoldFlowService（MonoBehaviour）
    → IResourceService     （F-03；必備）
    → IDataManager         （F-01；必備）
    → ITimeService         （F-02；必備）
    → IStaffService        （FT-12；可 null，降級回傳 0）
    → IGuildBuildingService（FT-07；可 null，降級回傳 0）
    → EventBus             （訂閱 In / 發布 Out）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

FT-05 採純事件驅動設計，**無對外公開查詢 API**（GDD §3.10）。

`IGoldFlowService` 為空介面（標記用途），不含方法。訂閱者若需保留金流歷史，自行 copy breakdown 物件快取；FT-05 不維護任何歷史清單或 runtime 快取。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload 型別 | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnCommissionAccepted` | In（訂閱） | `(int missionID, int baseReward, DispatchSource source)` | FT-02 / FT-03 / FT-11 發布委託預收觸發；FT-05 執行 `AddGold` 後發布 `OnCommissionPrepaid`。enum 從 FT-02 FSD-A §5.3 引用（見 §5.3） |
| `OnMissionResolved` | In（訂閱） | `Outcome` | FT-04 發布；FT-05 執行完整結算流程 |
| `OnGuildMaintenanceDue` | In（訂閱） | `(long, Dictionary<int,int>, int)` | FT-07 發布（Phase 2）；FT-05 執行維護費扣款 |
| `OnStaffSalaryDue` | In（訂閱） | `(long, Dictionary<int,int>, int)` | FT-12 發布（Phase 2）；FT-05 執行薪水扣款 |
| `OnCommissionPrepaid` | Out（發布） | `(int missionID, int prepaidAmount, DispatchSource source)` | `AddGold` 完成後立即；P-02 更新金幣動畫；P-03 可選預收通知；source 透傳自上游 |
| `OnCommissionSettled` | Out（發布） | `CommissionBreakdown` | `ExecuteGoldFlow` + timestamp 寫入後；P-02 顯示結算明細；P-03 依狀態轉移選通知模板；FT-10 未來訂閱收支日誌 |
| `OnMaintenanceCharged` | Out（發布） | `MaintenanceBreakdown` | 維護費 `ExecuteGoldFlow` 後（Phase 2）；P-02 顯示設施扣款細項 |
| `OnSalaryCharged` | Out（發布） | `SalaryBreakdown` | 薪水 `ExecuteGoldFlow` 後（Phase 2）；P-02 顯示職員薪水細項 |

### 5.3 資料結構

#### CommissionBreakdown（DTO，對應 `GoldFlowTypes.cs`）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `activeMissionID` | `int` | FK → FT-02 ActiveMission（僅供追溯，結算時 FT-04 已移除） |
| `missionID` | `int` | FK → C-01 MissionTemplate |
| `adventurerInstanceID` | `int` | FK → C-02 AdventurerInstance |
| `missionDifficulty` | `string` | 快照自 Outcome |
| `isSuccess` | `bool` | 快照自 Outcome |
| `baseReward` | `int` | 快照自 Outcome |
| `effectiveCommissionRate` | `float` | 本次採用的傭金率（含職員加成） |
| `effectivePenaltyRate` | `float` | 本次採用的賠償率（含加成，下限 0） |
| `accountantCommissionBonus` | `float` | 會計傭金加成（未招募為 `0`） |
| `accountantPenaltyBonus` | `float` | 會計賠償加成（未指派保險櫃為 `0`） |
| `buildingPenaltyBonus` | `float` | 建築賠償加成（Jam 預設 `0`） |
| `commissionGoldAmount` | `int` | 成功傭金金額；失敗為 `0` |
| `penaltyGoldAmount` | `int` | 失敗賠償金額（正整數）；成功為 `0` |
| `conditionGoldBonus` | `int` | 快照自 `Outcome.conditionGoldBonus`；成敗皆加 |
| `netDelta` | `int` | 傳入 `AddGoldAllowBankruptcy` 的淨額 |
| `goldBefore` | `int` | 結算前金幣快照 |
| `goldAfter` | `int` | 結算後金幣快照 |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 結算前破產狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 結算後破產狀態 |
| `settleTimestamp` | `long` | UTC Unix seconds（`ITimeService.NowUTC`） |

#### MaintenanceBreakdown（DTO）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `items` | `Dictionary<int, int>` | `buildingID → cost`（defensive copy） |
| `totalAmount` | `int` | 合計扣款（正整數） |
| `netDelta` | `int` | `= -totalAmount` |
| `goldBefore` | `int` | 扣款前快照 |
| `goldAfter` | `int` | 扣款後快照 |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 扣款前狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 扣款後狀態 |
| `chargeTimestamp` | `long` | UTC Unix seconds（由 FT-07 `Due` 事件傳入） |

#### SalaryBreakdown（DTO）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `items` | `Dictionary<int, int>` | `instanceID → salary`（defensive copy；FT-05 僅 iterate sum） |
| `totalAmount` | `int` | 合計扣款 |
| `netDelta` | `int` | `= -totalAmount` |
| `goldBefore` | `int` | 扣款前快照 |
| `goldAfter` | `int` | 扣款後快照 |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 扣款前狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 扣款後狀態 |
| `chargeTimestamp` | `long` | UTC Unix seconds（由 FT-12 `Due` 事件傳入） |

#### `DispatchSource`（enum，**從 FT-02 引用，不在 GoldFlowTypes.cs 重複定義**）

依 GDD §3.6a 定義於 FT-02（FSD-A §5.3）：

```csharp
public enum DispatchSource
{
    PlayerManual,    // 玩家手動審核通過
    NpcAutoPick,     // FT-03 自主接單（FT-02 代為發布）
    OfflineAutoPick  // 未來 FT-11 離線追認
}
```

**2026-04-27 patch（對齊 GDD P-001）**：原本 FT-05 在 `GoldFlowTypes.cs` 自行定義名為 `CommissionSource` 的同義 enum，與 FT-02 FSD-B `CommissionSource = {Regular, Static}`（委託板池注入事件 `OnCommissionPosted` 用）名稱衝突。GDD P-001 已將 FT-05 GDD 中所有事件 source 型別改為 `DispatchSource`，本 FSD 同步移除 `CommissionSource` 自行定義並從 FT-02 引用。`GoldFlowTypes.cs` 不再宣告任何 source enum。

### 5.4 內部資料流

#### 管線 A — 委託預收（`OnCommissionAccepted`）

```
EventBus.OnCommissionAccepted(missionID, baseReward, source)
    → GoldFlowService.HandleCommissionAccepted(missionID, baseReward, source)
        ├─ 防禦：if baseReward <= 0 → Debug.LogError, return
        ├─ prevGold = _resourceService.GetGold()
        ├─ _resourceService.AddGold(+baseReward)          // 預收為正值，不觸發破產
        ├─ prepaidAmount = _resourceService.GetGold() - prevGold  // clamp 後的實際入帳
        └─ EventBus.Publish(OnCommissionPrepaid(missionID, prepaidAmount, source))
```

#### 管線 B — 委託結算（`OnMissionResolved`）

```
EventBus.OnMissionResolved(outcome)
    → GoldFlowService.HandleMissionResolved(outcome)
        ├─ 防禦：if outcome == null → Debug.LogError, return
        ├─ breakdown = BuildCommissionBreakdown(outcome)
        ├─ 加成查詢（即時）：
        │   breakdown.accountantCommissionBonus = FT12?.GetAccountantCommissionBonus() ?? 0f
        │   breakdown.accountantPenaltyBonus    = FT12?.GetAccountantPenaltyBonus() ?? 0f
        │   breakdown.buildingPenaltyBonus      = FT07?.GetBuildingPenaltyBonus() ?? 0f
        ├─ 有效率計算：
        │   breakdown.effectiveCommissionRate = COMMISSION_RATE + accountantCommissionBonus
        │   breakdown.effectivePenaltyRate    = max(0, PENALTY_RATE + accountantPenaltyBonus + buildingPenaltyBonus)
        ├─ 淨額計算：
        │   if outcome.isSuccess:
        │       commissionGoldAmount = Mathf.RoundToInt(baseReward × effectiveCommissionRate)
        │       penaltyGoldAmount    = 0
        │       netDelta             = commissionGoldAmount + conditionGoldBonus
        │   else:
        │       commissionGoldAmount = 0
        │       penaltyGoldAmount    = Mathf.RoundToInt(baseReward × effectivePenaltyRate)
        │       netDelta             = -penaltyGoldAmount + conditionGoldBonus
        ├─ ExecuteGoldFlow(breakdown)                   // 見共用機制
        ├─ breakdown.settleTimestamp = _timeService.NowUTC
        └─ EventBus.Publish(OnCommissionSettled(breakdown))
```

#### 管線 C — 設施維護費（`OnGuildMaintenanceDue`，Phase 2）

```
EventBus.OnGuildMaintenanceDue(dueTimestamp, perBuildingCost, totalAmount)
    → GoldFlowService.HandleMaintenanceDue(dueTimestamp, perBuildingCost, totalAmount)
        ├─ 防禦：if totalAmount <= 0 → Debug.LogError, return
        ├─ 防禦：if perBuildingCost == null → Debug.LogError, return
        ├─ breakdown = new MaintenanceBreakdown {
        │       items = new Dictionary<int,int>(perBuildingCost),  // defensive copy
        │       totalAmount = totalAmount,
        │       netDelta = -totalAmount,
        │       chargeTimestamp = dueTimestamp
        │   }
        ├─ ExecuteGoldFlow(breakdown)
        └─ EventBus.Publish(OnMaintenanceCharged(breakdown))
```

#### 管線 D — 職員薪水（`OnStaffSalaryDue`，Phase 2）

```
EventBus.OnStaffSalaryDue(dueTimestamp, perStaffSalary, totalAmount)
    → GoldFlowService.HandleSalaryDue(dueTimestamp, perStaffSalary, totalAmount)
        ├─ 防禦：if totalAmount <= 0 → Debug.LogError, return
        ├─ 防禦：if perStaffSalary == null → Debug.LogError, return
        ├─ breakdown = new SalaryBreakdown {
        │       items = new Dictionary<int,int>(perStaffSalary),   // defensive copy
        │       totalAmount = totalAmount,
        │       netDelta = -totalAmount,
        │       chargeTimestamp = dueTimestamp
        │   }
        ├─ ExecuteGoldFlow(breakdown)
        └─ EventBus.Publish(OnSalaryCharged(breakdown))
```

#### 共用機制 — ExecuteGoldFlow（所有扣款管線共用）

```
ExecuteGoldFlow(breakdown)    // breakdown 為 Commission/Maintenance/Salary 之一
    ├─ breakdown.goldBefore            = _resourceService.GetGold()
    ├─ breakdown.bankruptcyStateBefore = _resourceService.GetBankruptcyWarningState()
    ├─ _resourceService.AddGoldAllowBankruptcy(breakdown.netDelta)
    ├─ breakdown.goldAfter             = _resourceService.GetGold()
    └─ breakdown.bankruptcyStateAfter  = _resourceService.GetBankruptcyWarningState()
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SystemConstants.csv` | `COMMISSION_RATE`（float）、`PENALTY_RATE`（float） | `【F-01-DS】system-constants.md` | 基礎傭金率、基礎賠償率；用於 `effectiveCommissionRate` / `effectivePenaltyRate` 計算 | `GoldFlowService.Awake()`，透過 `IDataManager.GetFloat(key)` 讀取並快取為 `_commissionRate` / `_penaltyRate` |

### 6.2 引用的 ScriptableObject

無。所有金流常數來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數 / 常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `COMMISSION_RATE`（預設 `0.20`） | `SystemConstants.csv.COMMISSION_RATE` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `PENALTY_RATE`（預設 `0.10`） | `SystemConstants.csv.PENALTY_RATE` | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1.1 `baseReward ≤ 0` | `Debug.LogError`；不呼叫 `AddGold`；不發布 `OnCommissionPrepaid`；`return` | `GoldFlowService.HandleCommissionAccepted` | AC-2（EditMode 單元測試） |
| §5.1.2 `totalAmount ≤ 0` | `Debug.LogError`；不執行扣款；不發布 `Charged` 事件；`return` | `GoldFlowService.HandleMaintenanceDue` / `HandleSalaryDue` | AC-11（EditMode 測試） |
| §5.1.3 `items == null` | `Debug.LogError`；`return` | 同上 | AC-11 |
| §5.1.4 `items.Count == 0` 但 `totalAmount > 0` | 以 `totalAmount` 為準執行扣款；`breakdown.items = new Dictionary<int,int>()`；`Debug.LogWarning` | 同上 | 手動注入不一致資料；確認 `goldAfter` 正確扣款、warning 出現 |
| §5.1.5 `Outcome == null` | `Debug.LogError`；`return` | `GoldFlowService.HandleMissionResolved` | EditMode 注入 null outcome |
| §5.1.6 `outcome.baseReward == 0` | 仍執行結算；`commissionGoldAmount = penaltyGoldAmount = 0`；`netDelta = conditionGoldBonus`；`AddGoldAllowBankruptcy(0)` 為 no-op 但仍快照狀態並發布事件 | 同上 | EditMode：注入 `baseReward=0` outcome，驗證事件發布及 breakdown 完整性 |
| §5.2.1 同 missionID 重複 `OnCommissionAccepted` | 不去重；第二次仍預收並發布（屬上游 bug，肉眼可見金幣異常） | `GoldFlowService.HandleCommissionAccepted` | 手動注入兩次相同 missionID；觀察 `goldAfter = goldBefore + 2×baseReward` |
| §5.2.2 結算無前置預收 | 不比對預收紀錄；照 `outcome` 完整結算 | `HandleMissionResolved` | EditMode：不發 `OnCommissionAccepted` 直接發 `OnMissionResolved`；確認結算正常 |
| §5.2.3 事件倒置（預收遲於結算） | 兩者無交互依賴，獨立執行；結算先發生，預收後到達仍正常 `AddGold` | 兩個 handler | PlayMode 模擬 EventBus queue 倒置 |
| §5.2.4 維護費 + 薪水同 frame 觸發 | 依 EventBus 順序依次執行 `ExecuteGoldFlow`；各自獨立發布 `Charged` 事件 | `HandleMaintenanceDue` / `HandleSalaryDue` | 手動同 frame 發布兩事件；確認 `goldAfter` 累計扣款正確 |
| §5.3.1 成功結算後超 `GOLD_MAX` | F-03 clamp；`goldAfter = GOLD_MAX`；`breakdown.netDelta` 保留公式計算原始值（不回寫 clamp 後差額） | `GoldFlowService`（`ExecuteGoldFlow` 不修改 `netDelta`） | AC-3 及類似情境；驗證 `netDelta ≠ goldAfter - goldBefore`（差異即 clamp 量） |
| §5.3.2 `GOLD_MAX` clamp 導致 `prepaidAmount < baseReward` | `prepaidAmount = GetGold() - prevGold`（實際入帳）；結算仍以 `outcome.baseReward` 計算（不補償） | `HandleCommissionAccepted` | AC-3 |
| §5.3.3 Banker's rounding `.5` 邊界 | `Mathf.RoundToInt(120.5) = 120`；`Mathf.RoundToInt(121.5) = 122`（Unity 預設行為） | `HandleMissionResolved` | EditMode 斷言：`baseReward=603`，rate=`0.20` → `RoundToInt(120.6) = 121` |
| §5.3.4 結算 `outcome.baseReward` 與預收時不一致 | 以 `outcome.baseReward` 為準；不比對預收值（單一資料來源原則） | `HandleMissionResolved` | 設計原則，無需程式測試；Jam 階段此情境不發生 |
| §5.4.1 FT-07 / FT-12 尚未實作（null） | null-check 回傳 `0f`；預設率生效（`COMMISSION_RATE` / `PENALTY_RATE`） | `HandleMissionResolved` | AC-15b |
| §5.4.2 FT-12 API 拋例外 | 不 try-catch；例外向上傳播；結算中斷 | `HandleMissionResolved` | 設計原則：嚴重錯誤需 root cause 修復，不吞錯 |
| §5.4.3 `effectivePenaltyRate` 被壓至負數 | `max(0, ...)` 攔截至 `0`；失敗結算零賠償；不發 `Debug.LogWarning` | `HandleMissionResolved` | EditMode：mock FT-12 回傳 `-0.15` → 驗證 `penaltyGoldAmount = 0` |
| §5.4.4 `effectiveCommissionRate > 1.0` | 直接以計算結果執行（不 clamp）；屬 CSV 設計級異常，受表格審查把關 | `HandleMissionResolved` | 設計決策：FT-05 不限制上限，保留攻略空間 |
| §5.5.1 結算前已在 `Bankrupt` 狀態 | 仍執行 `AddGoldAllowBankruptcy`；發布 `OnCommissionSettled`；`before/After` 皆為 `Bankrupt` | `HandleMissionResolved`，`ExecuteGoldFlow` | EditMode：設定 F-03 為 Bankrupt 狀態後觸發結算；驗證事件發布及欄位正確 |
| §5.5.2 `AddGoldAllowBankruptcy(+X)` 讓 `Warning → Normal` | `ExecuteGoldFlow` 快照正確；P-03 依對照表選「負債已結清」模板 | `ExecuteGoldFlow` | AC-13 |
| §5.5.3 單次金流跨越 `Normal → Warning → Normal`（理論不可能） | 不處理；F-03 保證單一 `netDelta` 不雙重穿越零線 | 無（設計保證） | 設計前提：F-03 GDD 需明確此保證（GDD 回註 §8.4） |
| §5.5.4 `Warning → Bankrupt` 金流觸發（罕見） | `ExecuteGoldFlow` 快照完整覆蓋；`bankruptcyStateAfter = Bankrupt` | `ExecuteGoldFlow` | PlayMode：模擬 F-03 在 `AddGoldAllowBankruptcy` 內 tick 到期；驗證 `after = Bankrupt` |
| §5.6.1 P-02 / P-03 訂閱者拋例外 | 不 try-catch；例外向上至 EventBus 層 | `GoldFlowService`（Publish 端不吞錯） | 設計原則；EventBus 層處理訂閱者隔離 |
| §5.6.2 訂閱者修改 breakdown 物件 | 不防禦（不 deep copy、不凍結）；訂閱者自律原則 | `GoldFlowService` | 設計決策；若未來出問題考慮改 immutable struct |
| §5.6.3 發布時訂閱者尚未註冊 | 正常發布；EventBus 決定 drop 或 queue；FT-05 不 replay | `GoldFlowService` | 設計原則；訂閱者初始化順序為 Core / EventBus 職責 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 Payload 資料結構（§3.1.1~§3.1.3） | §5.3（三個 DTO 定義） | 對齊 | 完整 20 欄 CommissionBreakdown + MaintenanceBreakdown + SalaryBreakdown 皆已列出 |
| §3.2 預收管線 | §5.4 管線 A | 對齊 | 防禦檢查、`AddGold`、`prepaidAmount = GetGold() - prevGold`、`OnCommissionPrepaid` 發布 |
| §3.3 委託結算管線 | §5.4 管線 B | 對齊 | 六步順序（不可調換）完整對應 |
| §3.4 加成查詢與有效率計算 | §5.4 管線 B（加成查詢段落） | 對齊 | null-check 降級；`effectiveCommissionRate` / `effectivePenaltyRate` 公式；降級 log 一次於 Awake |
| §3.5 成功 / 失敗淨額計算 | §5.4 管線 B（淨額計算段落）、§5.3 CommissionBreakdown 欄位 | 對齊 | `Mathf.RoundToInt`（Banker's rounding）；`conditionGoldBonus` 通用加法 |
| §3.6 維護費管線（Phase 2） | §5.4 管線 C | 對齊 | defensive copy；防禦；`ExecuteGoldFlow`；Phase 2 no-op 說明 |
| §3.7 薪水管線（Phase 2） | §5.4 管線 D | 對齊 | 與 §3.6 對稱；Phase 2 no-op 說明 |
| §3.8 破產狀態轉移快照機制 | §5.4 共用機制 ExecuteGoldFlow | 對齊 | 四步流程完整；狀態轉移對照表見 GDD §3.8（FSD 未重複，由 §7 邊緣案例對應） |
| §3.9 事件契約（§3.9.1~§3.9.2） | §2.5、§5.2 | 對齊 | In/Out 8 個事件 payload 完整列舉；訂閱者責任說明 |
| §3.10 查詢 API | §5.1 | 對齊 | 明確宣告無對外查詢 API |

### 8.2 公式對齊或替代說明

GDD §4 公式（有效率公式與淨額公式）直接採用，無替代。

- `effectiveCommissionRate = COMMISSION_RATE + accountantCommissionBonus`：對應 §5.4 管線 B 加成查詢後的計算行，一致。
- `effectivePenaltyRate = max(0, PENALTY_RATE + accountantPenaltyBonus + buildingPenaltyBonus)`：一致，下限 `max(0, ...)` 由程式確保。
- `commissionGoldAmount = Mathf.RoundToInt(baseReward × effectiveCommissionRate)`：直接使用。
- `penaltyGoldAmount = Mathf.RoundToInt(baseReward × effectivePenaltyRate)`：直接使用。
- `netDelta`（成功）= `commissionGoldAmount + conditionGoldBonus`；（失敗）= `-penaltyGoldAmount + conditionGoldBonus`：直接使用。
- 維護費 / 薪水 `netDelta = -totalAmount`：直接使用。

### 8.3 未能實現的規則與修改建議

**D-01（既有 Script 偏差）**：Glob 結果確認 `TheGuild-unity/Assets/Scripts/Gameplay/GoldFlow/` 目錄不存在，FT-05 目前無任何既有 Script，為正向 FSD。F-03 介面（`IResourceService`）已在 `Assets/Scripts/Gameplay/Resources/ResourceManagement.cs` 實作（含 `AddGoldAllowBankruptcy`），可直接依賴。

**B-01（已解決，FSD-Codex-Reoprts-260427 T1-FT05）**：原議題「F-03 保證單一 `netDelta` 無法同時穿零線兩次」已由 F-03 FSD 覆蓋（**AC-RM-19「單次至多一次過渡」**，F-03 §8.1 對齊 `AddGoldAllowBankruptcy` 單次線性變化保證）。本 FSD 不再阻塞此項；若需 GDD 回註，屬文件同步項而非 FT-05 blocker。

**B-02（建議項）**：GDD §5.3.1 說明「`breakdown.netDelta` 保留公式計算原始值（不回寫 clamp 後差額）」，建議 P-02 在消費 `OnCommissionSettled` 時透過 `goldAfter - goldBefore` 計算實際差額並與 `netDelta` 比較，若不相等顯示「金幣上限截斷」提示。此邏輯屬 P-02 職責，FT-05 不須改動。

**B-03（已解決，2026-04-27）**：原建議項描述 FT-05 與 FT-02 的 source enum 命名衝突（FT-05 原本宣告 `CommissionSource = {PlayerManual, NpcAutoPick, OfflineAutoPick}`，但 FT-02 FSD-B 的 `CommissionSource = {Regular, Static}` 是另一語義 enum）。**已由 GDD P-001 patch 解決**：FT-05 GDD §3.2 / §3.9.1 / §3.9.2 / §6.3 全部 4 處 source 型別改為 `DispatchSource`（與 FT-02 §3.6a 對齊）；本 FSD §2.5 / §4.2 / §4.4 / §5.2 / §5.3 同步更新。FT-05 不再宣告任何 source enum，從 FT-02 FSD-A §5.3 引用 `DispatchSource`。實作時 FT-02 直接傳入 `DispatchSource`，FT-05 透傳至 `OnCommissionPrepaid`，無 enum 轉換開銷。

**Prerequisite — 依賴未完成 FSD（FSD-Codex-Reoprts-260427 CT-10）**

本 FSD 對以下系統的 API / 事件契約引用，目前**尚未經對方 FSD 雙向驗證**，視為 stub 契約：

- FT-06 Guild Core（FSD 未存在）
- FT-07 Guild Building（FSD 未存在）
- FT-10 Save/Load（FSD 未存在）
- FT-12 Staff System（FSD 未存在）
- P-02 Main UI（FSD 未存在）
- P-03 Notification System（FSD 未存在）

實作時須以 mock / stub 介面驗證；對方 FSD 完成後須回頭做雙向對齊複查。

### 8.4 給 GDD 的回註紀錄

| 目標 GDD | 章節 | 回註內容 |
| --- | --- | --- |
| `【FT-05】guild-gold-flow.md` | §3.10 | FSD 回註：FT-05 確認採純事件驅動，`IGoldFlowService` 為空介面（標記用途），符合 GDD §3.10 「無對外查詢 API」設計。（2026-04-27） |
| `【F-03】resource-management.md` | §5.1（或 §6 依賴聲明） | FSD 回註：FT-05 FSD 要求 F-03 明確保證「單一 `AddGoldAllowBankruptcy` 呼叫不會使 `BankruptcyWarningState` 在同一呼叫內發生兩次轉移（即不可能 Normal→Warning→Normal）」。此為 FT-05 §7 §5.5.3 對策的前提假設。（2026-04-27） |

> 注意：以上回註在 FSD 審查通過後由主體實際寫入對應 GDD 檔案，本 FSD §8.4 僅記錄意圖。

### 8.5 衝突處理紀錄

無真實衝突。

GDD §3.1.2 + FT-02 GDD §3.5 line 112 的「分鐘」Tech Debt 已由任務提示確認為三系統共識，非時間單位衝突，FT-05 不涉及任何分鐘單位處理，無須處理。

---

## 附錄 A — Review 紀錄（FSD Review Log）

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（自檢） | 通過 | 通過 | 通過 | 正向 FSD（無既有 GoldFlow Script）；未拆分（3 Script，預估 350~420 行）；§3~§8 全節對齊 GDD §3 條目；GDD §4 公式直接採用；邊緣案例 §5.1~§5.6 全 22 個案例皆有對策；無真實衝突；建議項 B-01/B-02/B-03 不阻礙實作；待主體複核後轉「已完成」 |

---

## 附錄 B — Pre-Delivery Checklist

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：AC-1~AC-15c 每條皆可被 EditMode／PlayMode 測試或手動步驟驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉完整
- [x] §3.3 對映表：7 組幻想／目的各有對應技術手段
- [x] §4.1~§4.4：拆分判斷明確（否）；Script 清單含路徑、SRP、依賴介面、預估規模
- [x] §5.1~§5.4：API 簽名（§5.1）、事件 payload（§5.2）、資料結構（§5.3）、資料流偽碼（§5.4）齊備
- [x] §6.1~§6.3：CSV 表含對應 Data-Specs；嚴禁寫死清單對齊原則第 9 條
- [x] §7：GDD §5 全 22 個邊緣案例皆有具體對策（無「妥善處理」）
- [x] §8.1：對齊清單覆蓋 GDD §3 每個小節（§3.1~§3.10）
- [x] §8.2~§8.5：公式對齊、未能實現項、GDD 回註、衝突紀錄如實登記
- [x] 附錄 A：Review 三項結果全填，登記 Review 者與日期
- [x] FSD-index：§6.1 FT-05 列待更新（FSD 欄填入本檔連結）；§7.1 新增狀態列；§7.2 新增 review 紀錄；§6.2 F-01-DS 列追加 FT-05-FSD

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（自檢） | 通過 | 通過 | 通過 | 無真實衝突；TD-01 分鐘 Tech Debt（FT-05 不涉及）；建議項 B-01（F-03 補 netDelta 不雙重穿零保證）/ B-02（P-02 clamp 提示）/ B-03（CommissionSource vs DispatchSource enum 重疊）皆不阻礙實作 |
| 2026-04-27 | Claude Code 主體（GDD P-001 對齊 patch） | 通過 | 通過 | 通過 | B-03 已解決：GDD P-001 patch 將 FT-05 GDD §3.2 / §3.9.1 / §3.9.2 / §6.3 全部 source 型別由 `CommissionSource` 改為 `DispatchSource`（與 FT-02 §3.6a 對齊）；本 FSD §2.5 / §4.2 / §4.4 / §5.2 / §5.3 / §8.3 同步更新；`GoldFlowTypes.cs` 移除自定 `CommissionSource` enum 宣告，改為從 FT-02 FSD-A 引用 `DispatchSource` |
