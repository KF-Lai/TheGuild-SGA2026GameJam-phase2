# 【FT-02-FSD-B】功能規格說明書 — Commission Board（委託板池管理）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-02】mission-dispatch.md`（版本：2026-04-22） |
| 對應 Data-Specs | `【FT-02-DS】success-rate-table.md`（_待建_；FT-02-B 不直接引用，登記以示完整） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-02-FSD-B 涵蓋「委託板池管理」職責：維護 `_regularMissionPool`（常規委託）與 `_staticMissionPool`（劇情靜態委託）兩個執行期清單，管理委託注入、查詢、派遣後移除，以及三軌補池生成（啟動填池 / 每日重置 / 危險度升階）。本子單元對外暴露 `ICommissionBoardService` 介面，`MissionDispatchService`（FT-02-A）透過此介面在 Dispatch step 10 執行移除。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- `_regularMissionPool` / `_staticMissionPool` 狀態持有與查詢
- `PostRegularMission(int missionID) : PostResult`（內部生成流程呼叫）
- `InjectStaticMission(int missionID) : InjectStaticMissionResult`（FT-09 外部注入入口）
- `RemoveMissionFromBoard(int missionID)`（派遣後由 FT-02-A Dispatch step 10 呼叫）
- `GetAvailableCommissions`、`GetCommissionsBySource`、`IsCommissionOnBoard` 查詢 API
- 三軌補池觸發（啟動填池 / F-02 `OnDailyResetEvent` / C-06 `OnDangerLevelChangedEvent`）
- 抽取規則（加權 difficulty → 均勻 missionID → 冪等保護 → 收斂保護）
- `OnCommissionPosted` 事件發布
- ISaveable 部分序列化（`_regularMissionPool`、`_staticMissionPool`）

**Out-of-Scope**
- 成功率/死亡率計算（→ FT-02-FSD-A）
- 派遣序列 / `_activeMissions` 狀態（→ FT-02-FSD-A）
- 任務模板定義（→ C-01）
- 危險度池權重定義（→ C-06）
- 劇情委託觸發邏輯（→ FT-09）

### 1.3 完成目標（Definition of Done）

| AC | 可驗證條件 |
| --- | --- |
| AC-CB-01 | EditMode test：`InjectStaticMission` 注入合法 `categoryID=3` 任務後，`GetAvailableCommissions()` 含該任務；發布 `OnCommissionPosted(missionID, Static)` 一次 |
| AC-CB-02 | EditMode test：`InjectStaticMission` 傳入 `categoryID != 3` 回傳 `WRONG_CATEGORY`，池不變 |
| AC-CB-03 | EditMode test：重複注入同一 missionID 回傳 `ALREADY_ON_BOARD`，池不重複 |
| AC-CB-04 | EditMode test：`PostRegularMission` 注入合法 `categoryID=0` 任務後，`GetCommissionsBySource(Regular)` 含該任務；發布 `OnCommissionPosted(missionID, Regular)` 一次 |
| AC-CB-05 | EditMode test：`RemoveMissionFromBoard` 移除後，`GetAvailableCommissions()` 不含該任務 |
| AC-CB-06 | EditMode test：`IsCommissionOnBoard` 在注入後回 true，移除後回 false |
| AC-CB-07 | EditMode test：`GetAvailableCommissions` 回傳 `_regularMissionPool ∪ _staticMissionPool` 合集（不重複） |
| AC-CB-08 | EditMode test：兩池均空時 `GetAvailableCommissions` 回傳空集合，不報錯 |
| AC-CB-09 | EditMode test：補池抽取規則驗證——連續 N 次重複，超過收斂上限後 LogWarning 並停止 |
| AC-CB-10 | EditMode test：存檔後反序列化，`_regularMissionPool` / `_staticMissionPool` 正確還原；不合法 missionID 跳過並 LogWarning |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §3.9 委託板池服務（CommissionBoard）
- §3.9.1 池結構
- §3.9.2 注入 API（InjectStaticMission、PostRegularMission、Jam 階段常規生成時序、抽取規則）
- §3.9.3 查詢 API
- §3.9.4 派遣後從池移除
- §3.9.5 事件契約（OnCommissionPosted）
- §3.9.6 持久化
- §3.6a Enum 宣告（CommissionSource）
- §3.10 事件契約集中表（OnCommissionPosted 部分）
- §5.6 邊緣案例（CommissionBoard 相關）
- §6.2 下游依賴（FT-09、P-02、FT-10 對本子單元的依賴）

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-01-DS】mission-difficulty-table.md` | `MissionDifficultyTable.csv` | `categoryID`（透過 `GetTemplate`）、`difficulty` | 驗證注入任務的 categoryID；取常規模板清單 |

> FT-02-B 透過 C-01 API 間接消費，不直接讀取 CSV。

### 2.3 上游依賴系統

> **介面命名規範**：本表中以 `IXxxService` / `IXxxSystem` 命名的介面僅為敘述方便（沿用 GDD 用語），實作契約以既有 concrete singleton 為準（`DataManager.Instance` / `TimeSystem.Instance` / `ResourceManagement.Instance` / static `EventBus`）。詳見 `FSD-index.md` §2.10。

| 系統 | 依賴內容 | 呼叫介面 |
| --- | --- | --- |
| C-01 Mission Database | 驗證 missionID（GetTemplate）、取常規模板清單（GetRegularTemplates）；categoryID 驗證 | `IC01MissionDatabase.GetTemplate()`、`GetRegularTemplates(difficulty)` |
| F-02 Time System | 訂閱 typed `OnDailyResetEvent` 驅動每日補池 | `OnDailyResetEvent` 事件 |
| C-06 World Danger System | 訂閱 typed `OnDangerLevelChangedEvent` 驅動危險度升階補池；查詢池權重 | `OnDangerLevelChangedEvent` 事件、`IC06WorldDanger.GetPoolWeights()` |
| FT-07 Guild Building | 查詢委託板容量 | `IFT07GuildBuilding.GetMissionSlotCount()` |
| FT-02-A MissionDispatchService | 呼叫 `RemoveMissionFromBoard` | `ICommissionBoardService`（本介面） |

### 2.4 下游被依賴系統

| 系統 | 依賴本子單元的內容 |
| --- | --- |
| FT-03 NPC Decision | 呼叫 `GetAvailableCommissions()` 取可自主接單任務 |
| FT-09 Faction Story | 呼叫 `InjectStaticMission(missionID)` 注入劇情委託 |
| FT-10 Save/Load | 序列化 / 反序列化 `_regularMissionPool`、`_staticMissionPool` |
| P-02 Main UI | 呼叫 `GetAvailableCommissions()`、訂閱 `OnCommissionPosted` 更新委託板顯示 |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnDailyResetEvent` | 訂閱（F-02） | 空 payload | `OnEnable` 訂閱；觸發每日重置補池 |
| `OnDangerLevelChangedEvent` | 訂閱（C-06） | `(string newDangerLevel)` | `OnEnable` 訂閱；觸發危險度升階補池 |
| `OnCommissionPostedEvent` | 發布 | `(int missionID, CommissionSource source)` | `PostRegularMission` 或 `InjectStaticMission` 成功後 |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

打開遊戲總有委託可接；新任務不定時出現（每日更新、危險局勢升溫後選項變多）；劇情委託以特殊格式出現在委託板，玩家知道這是重要任務。系統確保委託板「不空」並維持整體難度感。

### 3.2 系統目的還原

FT-02-B 是委託供給端的排程器與狀態持有者：維護兩池、生成常規委託填池、接受 FT-09 靜態注入，並在任務被派遣後即時移除，確保委託板始終呈現「可接、不重複、難度合理」的任務清單。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 打開遊戲總有委託可接 | 委託板顯示至少 1 張任務 | 啟動填池：新遊戲首次進入主場景後補滿至 `GetMissionSlotCount()` |
| 每天有新委託出現 | 委託板每日刷新，缺額自動補上 | 訂閱 typed `OnDailyResetEvent`，計算 deficit 並補池 |
| 局勢緊張時高難度委託增多 | 危險度升階後委託難度分布改變 | 訂閱 typed `OnDangerLevelChangedEvent`（payload：`string newDangerLevel`），依新權重重新補池 |
| 劇情委託有別於普通委託 | 靜態委託在 UI 呈現差異（source=Static） | `_staticMissionPool` 獨立子集；`OnCommissionPosted` 帶 `CommissionSource.Static` |
| 派出任務後委託板少一張 | 派遣後委託板立即移除該任務 | Dispatch step 10 呼叫 `RemoveMissionFromBoard`；`_staticMissionPool` 優先，否則 `_regularMissionPool` |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。FT-02-B 整體職責單一（委託板池管理），對應 1 個主 Script + 2 個支援型別（enum + result 碼）。

### 4.2 拆分理由

不適用（未拆分）。

### 4.3 拆分結果

不適用。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `CommissionBoardService` | `Assets/Scripts/Gameplay/Mission/CommissionBoardService.cs` | 持有並管理兩個委託池，提供注入、查詢、移除、三軌補池生成 | `IC01MissionDatabase`、`IFT07GuildBuilding`、`IC06WorldDanger`、`EventBus` | `250~320 行` |
| `CommissionBoardEnums` | `Assets/Scripts/Gameplay/Mission/CommissionBoardEnums.cs` | 定義 `CommissionSource`、`PostResult`、`InjectStaticMissionResult` 三個 enum | — | `< 40 行` |
| `CommissionBoardEvents` | `Assets/Scripts/Gameplay/Mission/Events/CommissionBoardEvents.cs` | 集中宣告 typed event struct：`OnCommissionPostedEvent { int missionID; CommissionSource source; }` | — | `< 50 行` |

> `ICommissionBoardService` 介面可直接定義於 `CommissionBoardService.cs` 頂部，或獨立為 `ICommissionBoardService.cs`（實作者自行決定，不影響規格）。

### 4.5 類別關係

```
CommissionBoardService
    ├─ 持有 List<int> _regularMissionPool
    ├─ 持有 List<int> _staticMissionPool
    └─ 實作 ICommissionBoardService
           ├─ PostRegularMission(int) : PostResult
           ├─ InjectStaticMission(int) : InjectStaticMissionResult
           ├─ RemoveMissionFromBoard(int)
           ├─ GetAvailableCommissions() : IReadOnlyList<int>
           ├─ GetCommissionsBySource(CommissionSource) : IReadOnlyList<int>
           └─ IsCommissionOnBoard(int) : bool
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

**CommissionBoardService（via ICommissionBoardService）**

```
PostRegularMission(int missionID) : PostResult
    // 內部生成流程呼叫；注入常規委託至 _regularMissionPool
    // PostResult：OK / UNKNOWN_MISSION_ID / WRONG_CATEGORY / ALREADY_ON_BOARD

InjectStaticMission(int missionID) : InjectStaticMissionResult
    // FT-09 外部注入入口；驗證 categoryID == 3
    // InjectStaticMissionResult：OK / UNKNOWN_MISSION_ID / WRONG_CATEGORY / ALREADY_ON_BOARD / BOARD_DISABLED

RemoveMissionFromBoard(int missionID) : void
    // FT-02-A Dispatch step 10 呼叫；靜態池優先移除，否則常規池；兩池均無則靜默忽略

GetAvailableCommissions() : IReadOnlyList<int>
    // 回傳 _regularMissionPool ∪ _staticMissionPool 合集

GetCommissionsBySource(CommissionSource source) : IReadOnlyList<int>
    // source ∈ {Regular, Static}

IsCommissionOnBoard(int missionID) : bool
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnDailyResetEvent` | 訂閱（F-02） | 空 payload | `OnEnable` 訂閱；觸發 `RefillPool(deficit)` |
| `OnDangerLevelChangedEvent` | 訂閱（C-06） | `(string newDangerLevel)` | `OnEnable` 訂閱；觸發 `RefillPool(deficit)` |
| `OnCommissionPostedEvent` | 發布 | `(int missionID, CommissionSource source)` | `PostRegularMission` / `InjectStaticMission` 成功後 |

### 5.3 資料結構

**CommissionSource enum（GDD §3.6a）**

```csharp
public enum CommissionSource
{
    Regular,    // 由 PostRegularMission 注入（常規生成）
    Static      // 由 InjectStaticMission 注入（FT-09 劇情靜態委託）
}
```

**PostResult enum**

```csharp
public enum PostResult
{
    OK,
    UNKNOWN_MISSION_ID,
    WRONG_CATEGORY,
    ALREADY_ON_BOARD
}
```

**InjectStaticMissionResult enum**

```csharp
public enum InjectStaticMissionResult
{
    OK,
    UNKNOWN_MISSION_ID,
    WRONG_CATEGORY,
    ALREADY_ON_BOARD,
    BOARD_DISABLED    // Jam 階段不會發生；預留
}
```

**ISaveable 序列化 payload（FT-02-B 部分）**

```
OwnerKey = "ft02Dispatch"（與 FT-02-A 共用同一 owner key）
IsCritical = false（Degradable）
序列化欄位：
  _regularMissionPool : List<int>
  _staticMissionPool  : List<int>
```

> FT-02-A 與 FT-02-B 共用同一 `OwnerKey = "ft02Dispatch"`，Serialize / Restore 協調方式：`MissionDispatchService`（FT-02-A）作為主 ISaveable 實作者，呼叫 `CommissionBoardService.GetSerializableState()` / `RestoreState(dto)` 組合整包 JSON；FT-10 只看到一個 `"ft02Dispatch"` owner。

### 5.4 內部資料流

**流程 A：InjectStaticMission（FT-09 注入劇情委託）**

```
FT-09.ConfirmDialogue Step 4.InjectStaticMission(missionID)
  → CommissionBoardService.InjectStaticMission(missionID)
      ├─ tmpl = IC01.GetTemplate(missionID)
      │     null → return UNKNOWN_MISSION_ID
      ├─ tmpl.categoryID != 3 → return WRONG_CATEGORY
      ├─ IsCommissionOnBoard(missionID) → return ALREADY_ON_BOARD
      ├─ _staticMissionPool.Add(missionID)
      └─ EventBus.Publish(OnCommissionPosted, missionID, CommissionSource.Static)
  return OK
```

**流程 B：PostRegularMission（內部生成流程）**

```
CommissionBoardService.PostRegularMission(missionID)
  → tmpl = IC01.GetTemplate(missionID)
      null → return UNKNOWN_MISSION_ID
  → tmpl.categoryID != 0 → return WRONG_CATEGORY
  → IsCommissionOnBoard(missionID) → return ALREADY_ON_BOARD
  → _regularMissionPool.Add(missionID)
  → EventBus.Publish(OnCommissionPosted, missionID, CommissionSource.Regular)
  return OK
```

**流程 C：RefillPool（三軌補池共用邏輯）**

```
// 觸發點 1：新遊戲啟動後 Bootstrap 完成
// 觸發點 2：F-02.OnDailyResetEvent callback
// 觸發點 3：C-06.OnDangerLevelChangedEvent callback
CommissionBoardService.RefillPool()
  → slotCount = IFT07.GetMissionSlotCount()
  → currentCount = _regularMissionPool.Count + _staticMissionPool.Count
  → deficit = max(0, slotCount - currentCount)
  → if deficit == 0: return
  → weights = IC06.GetPoolWeights()                // WorldDangerTable[currentDangerLevel]
  → attempts = 0; maxAttempts = deficit × 2
  → filled = 0
  → while filled < deficit && attempts < maxAttempts:
        attempts++
        difficulty = RollDifficulty(weights)        // 加權隨機 → bucket → 展開單一難度
        templates = IC01.GetRegularTemplates(difficulty)
        if templates 為空: continue
        missionID = templates[UnityEngine.Random.Range(0, templates.Count)]
        result = PostRegularMission(missionID)
        if result == OK: filled++
        // ALREADY_ON_BOARD 時 attempt 遞增，不計 filled
  → if filled < deficit:
        Debug.LogWarning($"regular pool 收斂（模板池可能不足），目標 {deficit} 筆，實際填入 {filled} 筆")
```

**流程 D：RemoveMissionFromBoard（Dispatch step 10）**

```
MissionDispatchService.Dispatch step 10 → ICommissionBoardService.RemoveMissionFromBoard(missionID)
  → if _staticMissionPool.Contains(missionID):
      _staticMissionPool.Remove(missionID)
      if _regularMissionPool.Contains(missionID): Debug.LogWarning("missionID 同時存在兩池（異常）")
      return
  → if _regularMissionPool.Contains(missionID):
      _regularMissionPool.Remove(missionID)
      return
  → // 兩池均無（NPC 自主接單未透過委託板）→ 靜默忽略
```

**流程 E：RestoreFromSave**

```
CommissionBoardService.RestoreState(poolDto)
  → 反序列化 _regularMissionPool（List<int>）、_staticMissionPool（List<int>）
  → 逐筆驗證：IC01.GetTemplate(missionID) == null → LogWarning + 跳過
  → 保留所有合法 missionID，兩池恢復正常狀態
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `MissionTemplate.csv`（C-01 owner） | `missionID`、`categoryID`、`difficulty` | `【C-01-DS】mission-template.md` | 驗證注入任務的 categoryID；取常規模板清單（委派 C-01） | C-01 已載入 |

> FT-02-B 本身不持有任何 CSV；所有資料查詢委派上游系統介面。

### 6.2 引用的 ScriptableObject

無。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 委託板容量（`missionSlotCount`） | `BuildingTable.csv.missionSlotCount`（FT-07 owner，透過 `GetMissionSlotCount()`） | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 難度池權重（`weightF_E`、`weightD`⋯）| `WorldDangerTable.csv`（C-06 owner，透過 `GetPoolWeights()`） | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 靜態委託 categoryID 驗證值（`3`） | `MissionCategoryTable.csv.categoryID`（C-01 owner，固定值 3 = 劇情靜態） | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 常規委託 categoryID 驗證值（`0`） | `MissionCategoryTable.csv.categoryID`（C-01 owner，固定值 0 = 常規） | 對應「四、程式實作原則」第 9 條：參數表格化 |

> 建議：categoryID 值（0 / 3）讀自 `SystemConstants` 或 C-01 提供的常數介面，不以 magic number 直接寫在 CommissionBoardService 邏輯分支中（Game Jam 範圍內可暫時使用明確常數，但需以命名常數包裝）。

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `InjectStaticMission` 傳入 missionID C-01 找不到 | 回傳 `UNKNOWN_MISSION_ID`，不加入任何池 | `CommissionBoardService` | EditMode test（mock C-01 GetTemplate 回 null） |
| `InjectStaticMission` 傳入 categoryID != 3 | 回傳 `WRONG_CATEGORY`，不加入 | `CommissionBoardService` | EditMode test |
| `InjectStaticMission` 同一 missionID 重複 | 回傳 `ALREADY_ON_BOARD`，冪等保護 | `CommissionBoardService` | EditMode test |
| 派遣時 missionID 同時存在兩池（理論不應發生） | `_staticMissionPool` 優先移除；`Debug.LogWarning` 提示異常 | `CommissionBoardService` | EditMode test（強制注入兩池） |
| `GetAvailableCommissions` 兩池均空 | 回傳空集合，不報錯 | `CommissionBoardService` | EditMode test |
| 載入存檔後池中含不合法 missionID | 跳過，LogWarning；保留合法項目 | `CommissionBoardService` | EditMode test（mock 反序列化含無效 ID） |
| 補池連續 N 次抽到重複 | N = deficit × 2 次上限後 LogWarning 放棄剩餘補池 | `CommissionBoardService` | EditMode test（mock 模板池僅 1 筆，目標補 5 筆） |
| `PostRegularMission` 傳入 categoryID != 0 | 回傳 `WRONG_CATEGORY` | `CommissionBoardService` | EditMode test |
| `RemoveMissionFromBoard` 兩池均無該 missionID | 靜默忽略（NPC 自主接單但任務不在委託板，屬預期行為） | `CommissionBoardService` | EditMode test（直接呼叫 Remove 不在池的 ID） |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.9.1 池結構（兩子集定義） | §5.3、§5.4 | 對齊 | — |
| §3.9.2 注入 API（PostRegularMission、InjectStaticMission、回傳碼） | §5.1、§5.4 流程 A/B | 對齊 | — |
| §3.9.2 Jam 階段常規生成時序（三軌） | §5.4 流程 C | 對齊 | — |
| §3.9.2 抽取規則（加權難度、均勻 missionID、冪等、收斂保護） | §5.4 流程 C | 對齊 | — |
| §3.9.3 查詢 API | §5.1 | 對齊 | — |
| §3.9.4 派遣後從池移除（靜態優先） | §5.4 流程 D | 對齊 | — |
| §3.9.5 OnCommissionPosted 事件契約 | §2.5、§5.2 | 對齊 | — |
| §3.9.6 持久化（ISaveable） | §5.3 ISaveable payload、§5.4 流程 E | 對齊 | 與 FT-02-A 共用 OwnerKey；序列化協調方式於 §5.3 說明 |
| §3.6a CommissionSource enum | §5.3 | 對齊 | CommissionSource 定義在 FT-02-B；DispatchSource 定義在 FT-02-A |

### 8.2 公式對齊或替代說明

- GDD §3.9.2 抽取規則「`weightF_E` → 50% F / 50% E；`weightS_SSS` → 50% S / 50% SS（SSS 剔除後份額平分回 S/SS）」：以 `RollDifficulty(weights)` 函式實作，邏輯直接對映 GDD 文字規則，無替代。
- GDD §3.9.2 補池公式：`deficit = max(0, slotCount - _regularMissionPool.Count - _staticMissionPool.Count)`：直接採用。

### 8.3 未能實現的規則與修改建議

**裁決 B-01（categoryID magic number，FSD-Codex-Reoprts-260427 T1-FT02 升級為硬要求）**

GDD §3.9.2 使用數值 `3`（Static）與 `0`（Regular）驗證 categoryID。**Codex 實作必須在 `CommissionBoardEnums.cs` 定義 `public const int RegularCategoryID = 0;` 與 `public const int StaticCategoryID = 3;`**；`PostRegularMission` / `InjectStaticMission` 邏輯分支引用常數，不允許裸數字 `0` / `3` 出現在 `.cs` 流程碼中（測試/註解可保留）。

**裁決 B-02（GetAvailableCommissions 去重，FSD-Codex-Reoprts-260427 T1-FT02 升級為硬要求）**

`IsCommissionOnBoard` 已跨兩池判斷，正常流程不應產生重複。但為防禦 corrupted save / restore 時兩池同 `missionID` 的腐敗狀態，**`GetAvailableCommissions()` 必須回傳 distinct list**：實作以 `_regularMissionPool.Concat(_staticMissionPool).Distinct().ToList()` 或等價邏輯；當 distinct 後 count 與 raw concat count 不一致時 `LogWarning("Duplicate missionID detected after restore, repaired by distinct")`。AC-CB-07 已對齊「合集（不重複）」語意。

**Prerequisite — 依賴未完成 FSD（FSD-Codex-Reoprts-260427 CT-10）**

本 FSD 對以下系統的 API / 事件契約引用，目前**尚未經對方 FSD 雙向驗證**，視為 stub 契約：

- FT-07 Guild Building（FSD 未存在）
- FT-09 Faction Story（FSD 未存在）
- FT-10 Save/Load（FSD 未存在）
- P-02 Main UI（FSD 未存在）

實作時須以 mock / stub 介面驗證；對方 FSD 完成後須回頭做雙向對齊複查。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | 無需新增回註。 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | — | — | 無衝突。 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：10 條 AC 可被 EditMode test 驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：5 條幻想／目的皆有對應技術手段
- [x] §4.1~§4.4：未拆分（理由成立）；Script 清單路徑、SRP、依賴介面、規模齊全
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、五段資料流偽碼齊備
- [x] §6.1~§6.3：CSV 引用含對應 Data-Specs；嚴禁寫死清單 4 項（含 2 個 categoryID 值）對齊原則第 9 條
- [x] §7：GDD §5.6 全部邊緣案例對齊（9 條）
- [x] §8.1：對齊清單覆蓋 GDD §3.9.1~§3.9.6 + §3.6a 二層粒度
- [x] §8.2~§8.5：公式對齊、建議項登記、回註（無）、衝突（無）如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 / §7.3 同步更新（見本次任務末段 Edit 操作）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過 | 建議項 B-01（categoryID 命名常數）/ B-02（合集去重策略）不阻礙實作；ISaveable 共用 OwnerKey 協調方式於 §5.3 明確說明 |
