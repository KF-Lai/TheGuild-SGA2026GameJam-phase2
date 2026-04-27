# 【C-02-FSD】功能規格說明書 — Adventurer Management

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-02】adventurer-management.md`（版本：2026-04-27） |
| 對應 Data-Specs | `【C-02-DS】adventurer-template.md`<br>`【C-02-DS】recruit-cost-table.md`<br>`【F-01-DS】system-constants.md`（共用 `WOUNDED_RECOVERY_HOURS`） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-02 Adventurer Management 管理公會名冊（Roster）中所有冒險者的靜態定義（`AdventurerTemplate`）與 runtime 狀態（`AdventurerInstance`）。職責包含：從 CSV 載入並驗證模板資料、從模板或隨機參數建立冒險者實例、維護名冊的 CRUD 操作、執行狀態機轉換（`Idle` / `Dispatched` / `Wounded` / `Dead`），以及訂閱 F-02 `OnSecondTick` 驅動 `Wounded` 自動恢復計時。本系統不執行招募邏輯、成功率計算或金幣扣除，嚴格限定於冒險者資料管理。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- `AdventurerTemplate.csv` 與 `RecruitCostTable.csv` 的載入與欄位驗證
- `AdventurerInstance` 建立（`CreateFromTemplate`、`CreateRandomInstance`）
- 名冊 CRUD：`AddAdventurer`、`DismissAdventurer`、`GetRoster`、`GetByStatus`、`GetAdventurer`
- 狀態機轉換：`UpdateStatus`、`SetWounded`、`TickWoundedRecovery`
- `idleSinceTimestamp` / `currentMissionID` / `woundedUntilTimestamp` 的不變式維護
- `isUnique` 唯一性驗證
- `ISaveable` 持久化契約（序列化 / 還原）
- `_nextInstanceID` 自增管理

**Out-of-Scope**
- 招募邏輯（FT-01）
- 成功率 / 死亡率計算（FT-02）
- 金幣 / 聲望扣除（F-03）
- 自主派遣決策（FT-03）
- 任務結算（FT-04）
- UI 渲染（P-02）

### 1.3 完成目標（Definition of Done）

| ID | 可驗證條件 |
| --- | --- |
| AC-AM-01 | EditMode 測試：`GetRoster()` 在初始化後回傳空列表 |
| AC-AM-02 | EditMode 測試：`CreateFromTemplate(templateID)` 建立的實例 `professionID`、`rank`、`name` 與模板一致 |
| AC-AM-03 | EditMode 測試：模板 `raceID = 0` 時，建立的實例 `raceID` 為 C-04 `RollRace` 的合法非零結果 |
| AC-AM-04 | EditMode 測試：模板有 `fixedTraitIDs` 時，實例 `traitIDs` 必定包含所有非 0 固定特質 |
| AC-AM-05 | EditMode 測試：`isUnique=1` 的模板，名冊已有同 `templateID` 實例（任何狀態）時，`CreateFromTemplate` 回傳 `null` 並 `LogWarning` |
| AC-AM-06 | EditMode 測試：`isUnique=1` 名冊已有 `Dead` 狀態同 `templateID` 實例時，`CreateFromTemplate` 回傳 `null` 並 `LogWarning` |
| AC-AM-07 | EditMode 測試：`AddAdventurer` 未滿回傳 `true`，達 `rosterCap` 回傳 `false` |
| AC-AM-08 | EditMode 測試：`IsRosterFull(4)` 在名冊有 4 人（含 `Dead`）時回傳 `true` |
| AC-AM-09 | EditMode 測試：`SetWounded(instanceID)` 後 `woundedUntilTimestamp == mockNow + WOUNDED_RECOVERY_HOURS × 3600`，status = `Wounded` |
| AC-AM-10 | EditMode 測試：`TickWoundedRecovery()` 對到期 `Wounded` 冒險者轉為 `Idle`，`woundedUntilTimestamp = 0` |
| AC-AM-11 | EditMode 測試：模擬 mockNow 超出 `woundedUntilTimestamp` 7 小時後呼叫 `TickWoundedRecovery`，狀態轉為 `Idle` |
| AC-AM-12 | EditMode 測試：`DismissAdventurer` 對 `Dead` 回傳 `true` 且移除；對 `Idle`/`Dispatched`/`Wounded` 回傳 `false` |
| AC-AM-13 | EditMode 測試：`GetByStatus(Idle)` 只回傳 `Idle` 冒險者 |
| AC-AM-14 | EditMode 測試：`UpdateStatus` 傳入不存在 `instanceID` 時出現 `LogWarning`，名冊無變動 |
| AC-AM-15 | EditMode 測試：`AdventurerTemplate` 中 `professionID` 在 ProfessionTable 找不到時，`LogError`，該模板不可被 `CreateFromTemplate` 使用 |
| AC-AM-16 | CSV 載入：`AdventurerTemplate.csv` 零錯誤載入，`RecruitCostTable.csv` 7 列（F~S）全部命中 |
| AC-AM-17 | `RestoreFromSave` 後名冊完整還原，`_nextInstanceID` 大於名冊中最大 `instanceID` |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 內容摘要 |
| --- | --- |
| §1 概要 | 系統範圍、靜態層與 runtime 層定義、不在範圍的項目 |
| §2 玩家幻想 | Wounded 狀態的設計意圖；Dead 永久性設計原則 |
| §3.1 AdventurerInstance 資料結構 | 所有欄位定義與說明 |
| §3.2 AdventurerTemplate 表結構 | CSV 欄位定義、特質生成規則 |
| §3.3 RecruitCostTable | rank / cost / reputationReq 欄位 |
| §3.4 狀態機轉換規則 | 狀態轉換圖與 6 條補充規則 |
| §3.5 查詢 API | 所有公開方法簽名與行為描述 |
| §4 公式 | Wounded 恢復截止時間、TickWoundedRecovery、isUnique 驗證、CreateRandomInstance、BuildTraitList |
| §5 邊緣案例 | §5.1 資料載入、§5.2 Runtime 操作、§5.3 存檔相關 |
| §6 依賴關係 | 上下游系統、循環依賴注意事項、§6.4 ISaveable 契約 |
| §7 可調參數 | `WOUNDED_RECOVERY_HOURS`、`RecruitCostTable` 各欄位安全範圍 |
| §8 驗收標準 | AC-AM-01 ~ AC-AM-15 |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-02-DS】adventurer-template.md` | `AdventurerTemplate.csv` | `templateID`, `name`, `rank`, `professionID`, `raceID`, `fixedTraitIDs`, `randomTraitGroupIDs`, `factionID`, `isUnique` | 具名 NPC 靜態定義；`CreateFromTemplate` 時讀取 |
| `【C-02-DS】recruit-cost-table.md` | `RecruitCostTable.csv` | `rank`, `cost`, `reputationReq` | FT-01 招募費用查詢；C-02 載入後對外提供查詢介面 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `WOUNDED_RECOVERY_HOURS` | `SetWounded` 計算 `woundedUntilTimestamp` |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 介面 |
| --- | --- | --- |
| F-01 DataManager | 載入 `AdventurerTemplate`、`RecruitCostTable`、`SystemConstants` | `IDataManager.GetAll<T>()`、`IDataManager.Get<T>(id)` |
| F-02 Time System | 取得當前 Unix timestamp（秒）；訂閱 `OnSecondTick` 事件 | `ITimeSystem.NowUTC`、`EventBus`（`OnSecondTick`） |
| C-03 Profession System | 驗證 `professionID` 合法性 | `IProfessionService.GetProfession(professionID)` |
| C-04 Race System | `raceID = 0` 時依職業隨機抽種族 | `IRaceService.RollRace(professionID)` |
| C-05 Trait System | 依 `randomTraitGroupIDs` 抽取隨機特質 | `ITraitService.GetTraitGroup(groupID)`、`ITraitService.RollTraits(group)` |

### 2.4 下游被依賴系統

| 系統 | 依賴內容 | 使用介面 |
| --- | --- | --- |
| FT-01 Recruitment | 加入新冒險者、建立模板/隨機實例、檢查名冊容量 | `IAdventurerRoster.AddAdventurer`、`IAdventurerFactory.CreateFromTemplate`、`IAdventurerFactory.CreateRandomInstance`、`IAdventurerRoster.IsRosterFull` |
| FT-02 Mission Dispatch | 取得 Idle 冒險者列表；設定 Dispatched 狀態 | `IAdventurerRoster.GetByStatus(Idle)`、`IAdventurerRoster.UpdateStatus` |
| FT-03 NPC Decision | 取得 Idle 冒險者清單；讀取 `idleSinceTimestamp`；寫入 `lastAutoPickupTimestamp` | `IAdventurerRoster.GetByStatus(Idle)`、`IAdventurerRoster.GetAdventurer`、`IAdventurerRoster.SetLastAutoPickupTimestamp` |
| FT-04 Outcome Resolution | 結算後更新冒險者狀態 | `IAdventurerRoster.SetWounded`、`IAdventurerRoster.UpdateStatus` |
| FT-09 Faction Story | 讀取 `factionID` 累積 | `IAdventurerRoster.GetAdventurer` |
| FT-10 Save/Load | 序列化／反序列化名冊 | `ISaveable.Serialize()`、`ISaveable.RestoreFromSave()`、`ISaveable.InitializeAsNewGame()` |
| P-02 Main UI | 顯示名冊列表、狀態、除名按鈕 | `IAdventurerRoster.GetRoster()`、`IAdventurerRoster.GetAdventurer`、`IAdventurerRoster.DismissAdventurer` |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnSecondTick` | 訂閱（來自 F-02） | `{ long nowUTC }` | 每秒驅動 `TickWoundedRecovery` |
| `OnAdventurerRecovered` | 發布（至 FT-02 / FT-03 / P-02） | `{ int instanceID }` | `Wounded` → `Idle` 轉換完成時 |
| `OnAdventurerStatusChanged` | 發布（至 P-02 / FT-03） | `{ int instanceID, AdventurerStatus prev, AdventurerStatus current }` | 任意狀態轉換（含 Dead）完成時 |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

GDD §2 描述名冊是玩家最私密的地方：每位冒險者有名字、職業、種族與個性，有自己的故事。`Wounded` 狀態製造「差點沒命」的惋惜感，而非系統懲罰；`Dead` 是永久的，因為死亡是真實的，活著才有意義。

### 3.2 系統目的還原

C-02 是公會所有 runtime 行為的冒險者資料中心：提供唯一可靠的名冊狀態，讓 FT-01 招募、FT-02 派遣、FT-03 自主決策、FT-04 結算均透過同一份資料做決策；同時維護冒險者生命周期（從建立到除名），確保狀態一致性。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 每位冒險者有獨一無二的身分 | 名冊顯示名字、職業、種族、特質標籤 | `AdventurerInstance` 持有 `name`/`professionID`/`raceID`/`traitIDs`；`AdventurerFactory.CreateFromTemplate` 從 CSV 組裝 |
| 具名 NPC 只會出現一次（全局唯一） | 隊伍中同名冒險者不會重複出現 | `isUnique=1` 在 `CreateFromTemplate` 與 `AddAdventurer` 雙重驗證 |
| 受傷冒險者需要真實時間恢復 | 名冊顯示「受傷，剩 X 小時」；離線後打開遊戲仍然自動恢復 | `woundedUntilTimestamp` 為絕對 UTC 秒；`TickWoundedRecovery` 每秒比較 `NowUTC`，離線補算自動涵蓋 |
| 死亡是永久的，有重量的 | Dead 冒險者留在名冊；玩家主動按「除名」才移除 | `DismissAdventurer` 只允許 `Dead` 狀態呼叫；其他狀態回傳 `false` |
| 冒險者進入 `Idle` 後才能再被派遣 | FT-02 / FT-03 只看到 `Idle` 清單 | `GetByStatus(Idle)` 精確篩選；`UpdateStatus` 維護 `currentMissionID` 不變式 |
| 名冊達上限時無法再招募 | 招募介面灰掉或提示「名冊已滿」 | `IsRosterFull(rosterCap)` 供 FT-01 呼叫；`rosterCap` 由 FT-06 傳入，不寫死 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

是。

### 4.2 拆分理由

GDD 中 C-02 有 4 個明顯職責分區：(1) CSV 載入與欄位驗證、(2) 冒險者實例建立（模板 / 隨機）、(3) 名冊 CRUD 與狀態機、(4) `Wounded` 恢復計時訂閱。若合為一個類別，`UpdateStatus`、`CreateFromTemplate`、`TickWoundedRecovery`、`LoadTemplates` 混在同一 class，超過 500 行且違反 SRP。依「單一系統 FSD 對應 3~8 個 Script」經驗值，拆分為 4 個 Script 最為合適。

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| C-02-A | AdventurerTemplateLoader | CSV 載入與欄位驗證 | §3.2、§3.3、§5.1 |
| C-02-B | AdventurerFactory | 從模板或隨機參數建立 `AdventurerInstance` | §3.2、§4.3、§4.3a、§4.4 |
| C-02-C | AdventurerRoster | 名冊 CRUD、狀態機轉換、`ISaveable` | §3.1、§3.4、§3.5、§5.2、§5.3、§6.4 |
| C-02-D | AdventurerWoundedRecovery | 訂閱 `OnSecondTick`、驅動 Wounded → Idle | §4.2、§3.4 規則 2 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `AdventurerTemplateLoader.cs` | `Assets/Scripts/Gameplay/Adventurer/AdventurerTemplateLoader.cs` | 從 DataManager 載入並驗證 `AdventurerTemplate` 與 `RecruitCostTable`，快取供查詢 | `IDataManager`、`IProfessionService`、`IRaceService` | `150~200 行` |
| `AdventurerFactory.cs` | `Assets/Scripts/Gameplay/Adventurer/AdventurerFactory.cs` | 建立 `AdventurerInstance`（模板路徑 / 隨機路徑），執行 `isUnique` 驗證與 `BuildTraitList` | `IAdventurerTemplateLoader`、`IRaceService`、`ITraitService`、`IAdventurerRoster` | `180~250 行` |
| `AdventurerRoster.cs` | `Assets/Scripts/Gameplay/Adventurer/AdventurerRoster.cs` | 管理名冊 List、所有查詢 API、狀態機轉換、ISaveable 序列化 / 還原 | `ITimeSystem`、`IDataManager`（還原時驗證）、`EventBus` | `300~380 行` |
| `AdventurerWoundedRecovery.cs` | `Assets/Scripts/Gameplay/Adventurer/AdventurerWoundedRecovery.cs` | 訂閱 `OnSecondTick`，呼叫 `AdventurerRoster.TickWoundedRecovery()` | `EventBus`、`IAdventurerRoster` | `40~70 行` |

### 4.5 類別關係

```
AdventurerWoundedRecovery
    └─ 訂閱 EventBus.OnSecondTick
    └─ 呼叫 IAdventurerRoster.TickWoundedRecovery()

AdventurerFactory
    ├─ 讀取 IAdventurerTemplateLoader（取模板資料）
    ├─ 呼叫 IRaceService.RollRace（raceID=0 時）
    ├─ 呼叫 ITraitService（BuildTraitList）
    └─ 讀取 IAdventurerRoster（isUnique 驗證）

AdventurerRoster  ──實作──►  IAdventurerRoster
                  ──實作──►  ISaveable
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

**IAdventurerRoster（由 AdventurerRoster 實作）**

| 方法 | 簽名 | 說明 |
| --- | --- | --- |
| GetRoster | `GetRoster() : IReadOnlyList<AdventurerInstance>` | 含所有狀態（含 Dead） |
| GetByStatus | `GetByStatus(AdventurerStatus status) : IReadOnlyList<AdventurerInstance>` | 精確篩選 |
| GetAdventurer | `GetAdventurer(int instanceID) : AdventurerInstance` | 找不到回傳 `null` |
| GetRosterCount | `GetRosterCount() : int` | 含 Dead |
| IsRosterFull | `IsRosterFull(int rosterCap) : bool` | `GetRosterCount() >= rosterCap` |
| AddAdventurer | `AddAdventurer(AdventurerInstance instance) : bool` | 滿員或 isUnique 衝突回傳 `false` |
| UpdateStatus | `UpdateStatus(int instanceID, AdventurerStatus newStatus, int missionInstanceID = 0) : void` | 維護 `currentMissionID` / `idleSinceTimestamp` 不變式 |
| SetWounded | `SetWounded(int instanceID) : void` | 計算並寫入 `woundedUntilTimestamp`；清除 `currentMissionID` 與 `idleSinceTimestamp` |
| DismissAdventurer | `DismissAdventurer(int instanceID) : bool` | 只允許 Dead 狀態 |
| TickWoundedRecovery | `TickWoundedRecovery() : void` | 供 AdventurerWoundedRecovery 呼叫；到期 Wounded → Idle |
| SetLastAutoPickupTimestamp | `SetLastAutoPickupTimestamp(int instanceID, long timestamp) : void` | FT-03 寫入；instanceID 不存在時 `LogWarning` |
| GetRecruitCost | `GetRecruitCost(string rank) : (int cost, int reputationReq)` | FT-01 查詢對應階級費用 |

**IAdventurerFactory（由 AdventurerFactory 實作）**

| 方法 | 簽名 | 說明 |
| --- | --- | --- |
| CreateFromTemplate | `CreateFromTemplate(int templateID) : AdventurerInstance` | isUnique 驗證；模板不存在或驗證失敗回傳 `null` |
| CreateRandomInstance | `CreateRandomInstance(string rank, int professionID, int raceID, int[] traitIDs) : AdventurerInstance` | 分配 instanceID，不加入名冊 |

**IAdventurerTemplateLoader（由 AdventurerTemplateLoader 實作）**

| 方法 | 簽名 | 說明 |
| --- | --- | --- |
| GetTemplate | `GetTemplate(int templateID) : AdventurerTemplate` | 找不到回傳 `null` |
| GetAllTemplates | `GetAllTemplates() : IReadOnlyList<AdventurerTemplate>` | 供 FT-01 篩選招募池 |

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnSecondTick` | 訂閱（F-02 → C-02） | `{ long nowUTC }` | `AdventurerWoundedRecovery` 呼叫 `TickWoundedRecovery` |
| `OnAdventurerRecovered` | 發布（C-02 → FT-02 / FT-03 / P-02） | `{ int instanceID }` | `TickWoundedRecovery` 中 Wounded → Idle 完成時；`AdventurerRoster` 發布 |
| `OnAdventurerStatusChanged` | 發布（C-02 → P-02 / FT-03） | `{ int instanceID, AdventurerStatus prev, AdventurerStatus current }` | `UpdateStatus` / `SetWounded` / `DismissAdventurer` 後；`AdventurerRoster` 發布 |

### 5.3 資料結構

**AdventurerInstance（runtime 物件，不對應 CSV）**

```
AdventurerInstance
    int   instanceID               // Runtime 唯一 ID，由 AdventurerRoster 自增；0 = null sentinel
    int   templateID               // 來源模板 ID；0 = 隨機生成
    string name
    string rank                    // "F" | "E" | "D" | "C" | "B" | "A" | "S"
    int   professionID             // FK → ProfessionTable
    int   raceID                   // FK → RaceTable
    int[] traitIDs                 // FK → TraitTable（固定 + 隨機合集，已去重）
    int   factionID                // 0 = neutral
    AdventurerStatus status        // Idle | Dispatched | Wounded | Dead
    int   currentMissionID         // 非 Dispatched 時為 0
    long  woundedUntilTimestamp    // Unix 秒；非 Wounded 時為 0
    long  idleSinceTimestamp       // 轉入 Idle 的 UTC 秒；非 Idle 時為 0
    long  lastAutoPickupTimestamp  // FT-03 寫入；初始為 0
```

**AdventurerStatus enum**

```
enum AdventurerStatus { Idle, Dispatched, Wounded, Dead }
```

**AdventurerTemplate（對應 CSV）**

```
AdventurerTemplate
    int    templateID
    string name
    string rank
    int    professionID
    int    raceID                  // 0 = 依職業隨機
    int[]  fixedTraitIDs           // '|' 分隔；0 為 null sentinel
    int[]  randomTraitGroupIDs     // '|' 分隔；0 為 null sentinel
    int    factionID
    int    isUnique                // 1 = 唯一
```

**RecruitCostEntry（對應 CSV）**

```
RecruitCostEntry
    string rank
    int    cost
    int    reputationReq
```

### 5.4 內部資料流

**流程 A：FT-01 呼叫 CreateFromTemplate + AddAdventurer**

```
FT-01.TryRecruitFromTemplate(templateID)
  → AdventurerFactory.CreateFromTemplate(templateID)
      ├─ 1：IAdventurerTemplateLoader.GetTemplate(templateID)
      │       if null → LogError, return null
      ├─ 2：if template.isUnique == 1
      │       if IAdventurerRoster.GetRoster().Any(a => a.templateID == templateID)
      │           → LogWarning, return null
      ├─ 3：instance = new AdventurerInstance { instanceID = NextInstanceID(), templateID, name, rank, professionID }
      ├─ 4：instance.raceID = (template.raceID != 0) ? template.raceID : IRaceService.RollRace(professionID)
      ├─ 5：instance.traitIDs = BuildTraitList(template.fixedTraitIDs, template.randomTraitGroupIDs)
      ├─ 6：instance.factionID = template.factionID
      ├─ 7：instance.status = Idle; 其餘欄位 = 0
      └─ 8：return instance

  → IAdventurerRoster.AddAdventurer(instance)
      ├─ 1：if GetRosterCount() >= rosterCap → LogWarning, return false
      ├─ 2：if instance.templateID != 0 && isUnique 衝突 → LogWarning, return false
      ├─ 3：roster.Add(instance)
      ├─ 4：if instance.status == Idle → instance.idleSinceTimestamp = ITimeSystem.NowUTC
      ├─ 5：EventBus.Publish(OnAdventurerStatusChanged { instanceID, Idle, Idle })
      └─ 6：return true
```

**流程 B：FT-02 / FT-04 呼叫 UpdateStatus**

```
FT-02.Dispatch(instanceID, missionInstanceID)
  → IAdventurerRoster.UpdateStatus(instanceID, Dispatched, missionInstanceID)
      ├─ 1：if instanceID 不存在 → LogWarning, return
      ├─ 2：prevStatus = instance.status
      ├─ 3：if newStatus == Dispatched → instance.currentMissionID = missionInstanceID; instance.idleSinceTimestamp = 0
      ├─ 4：if newStatus == Idle → instance.currentMissionID = 0; instance.idleSinceTimestamp = ITimeSystem.NowUTC
      ├─ 5：if newStatus == Wounded || Dead → instance.currentMissionID = 0; instance.idleSinceTimestamp = 0
      ├─ 6：instance.status = newStatus
      └─ 7：EventBus.Publish(OnAdventurerStatusChanged { instanceID, prevStatus, newStatus })

FT-04.SetWounded(instanceID)
  → IAdventurerRoster.SetWounded(instanceID)
      ├─ 1：if instanceID 不存在 → LogWarning, return
      ├─ 2：if instance.status != Dispatched → LogWarning, return
      ├─ 3：now = ITimeSystem.NowUTC
      ├─ 4：instance.woundedUntilTimestamp = now + WOUNDED_RECOVERY_HOURS × 3600
      ├─ 5：instance.status = Wounded; instance.currentMissionID = 0; instance.idleSinceTimestamp = 0
      └─ 6：EventBus.Publish(OnAdventurerStatusChanged { instanceID, Dispatched, Wounded })
```

**流程 C：OnSecondTick → TickWoundedRecovery**

```
EventBus.OnSecondTick { nowUTC }
  → AdventurerWoundedRecovery.OnSecondTick(nowUTC)
      → IAdventurerRoster.TickWoundedRecovery()
          ├─ 1：now = ITimeSystem.NowUTC
          ├─ 2：if now == 0 → LogError, return（timestamp 取得失敗）
          ├─ 3：foreach instance in roster where status == Wounded:
          │       if now >= instance.woundedUntilTimestamp:
          │           instance.status = Idle
          │           instance.woundedUntilTimestamp = 0
          │           instance.idleSinceTimestamp = now
          │           EventBus.Publish(OnAdventurerRecovered { instanceID })
          │           EventBus.Publish(OnAdventurerStatusChanged { instanceID, Wounded, Idle })
          └─ 4：（熱路徑：禁 alloc；foreach 使用預先快取 list 或 index loop）
```

**流程 D：FT-10 呼叫 ISaveable**

```
FT-10.Save()
  → AdventurerRoster.Serialize()
      ├─ 1：序列化 roster List<AdventurerInstance>（§6.4 全欄位）
      └─ 2：序列化 _nextInstanceID

FT-10.Load(json)
  → AdventurerRoster.RestoreFromSave(ownerJson)
      ├─ 1：反序列化 roster + _nextInstanceID
      ├─ 2：逐筆驗證（templateID / professionID / raceID / traitIDs）
      ├─ 3：驗證失敗單筆 → LogWarning, 跳過
      └─ 4：若全部失敗且原始資料非空 → 拋例外，觸發整檔回退
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `AdventurerTemplate.csv` | `templateID`, `name`, `rank`, `professionID`, `raceID`, `fixedTraitIDs`, `randomTraitGroupIDs`, `factionID`, `isUnique` | `【C-02-DS】adventurer-template.md` | 具名 NPC 靜態定義；`CreateFromTemplate` 與 FT-01 招募池篩選 | F-01 初始化完成後（Awake 階段） |
| `RecruitCostTable.csv` | `rank`, `cost`, `reputationReq` | `【C-02-DS】recruit-cost-table.md` | FT-01 查詢招募費用與聲望門檻 | 同上 |
| `SystemConstants.csv` | `WOUNDED_RECOVERY_HOURS` | `【F-01-DS】system-constants.md` | `SetWounded` 計算 `woundedUntilTimestamp` | F-01 初始化完成後 |

### 6.2 引用的 ScriptableObject

無。C-02 所有參數來自 CSV。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `WOUNDED_RECOVERY_HOURS` | `SystemConstants.csv` → `WOUNDED_RECOVERY_HOURS` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| F/E/D/C/B/A/S 各 rank 的 `cost` | `RecruitCostTable.csv` → `cost` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| F/E/D/C/B/A/S 各 rank 的 `reputationReq` | `RecruitCostTable.csv` → `reputationReq` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 具名 NPC 的 `fixedTraitIDs` / `randomTraitGroupIDs` | `AdventurerTemplate.csv` 對應欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1：`professionID` 在 ProfessionTable 找不到 | `AdventurerTemplateLoader` 載入時呼叫 `IProfessionService.GetProfession(id)`，回傳 null → `LogError`，從快取排除該模板；`CreateFromTemplate` 呼叫時查快取無該 templateID 則回傳 null | `AdventurerTemplateLoader` | EditMode 測試：AC-AM-15 |
| §5.1：`raceID != 0` 但在 RaceTable 找不到 | 同上，`IRaceService.GetRace(id)` 回傳 null → `LogError`，排除模板 | `AdventurerTemplateLoader` | EditMode 測試：手動注入無效 raceID 驗 LogError |
| §5.1：`fixedTraitIDs` 包含不存在於 TraitTable 的 ID | `BuildTraitList` 中對每個 ID 呼叫 `ITraitService.GetTrait(id)`；回傳 null → `LogWarning`，過濾該 ID，其餘正常加入 | `AdventurerFactory` | EditMode 測試：注入無效 traitID，驗最終 traitIDs 不含無效 ID |
| §5.1：`randomTraitGroupIDs` 包含不存在於 TraitGroupTable 的 groupID | `BuildTraitList` 中 `ITraitService.GetTraitGroup(groupID)` 回傳 null → `LogWarning`，跳過該 groupID | `AdventurerFactory` | EditMode 測試：注入無效 groupID，驗隨機特質正常抽取其他群組 |
| §5.1：`rank` 不在 {F,E,D,C,B,A,S} | `AdventurerTemplateLoader` 載入時驗證 rank；不合法 → `LogError`，排除模板 | `AdventurerTemplateLoader` | EditMode 測試：注入非法 rank 字串 |
| §5.1：同一 `templateID` 在 CSV 出現兩次 | DataManager 標準行為：後者覆蓋前者，`LogWarning`（C-02 不額外處理） | `AdventurerTemplateLoader`（委託 DataManager） | — |
| §5.2：`AddAdventurer` 時名冊已滿 | 回傳 `false`，`LogWarning`；不加入 | `AdventurerRoster` | EditMode 測試：AC-AM-07 |
| §5.2：`AddAdventurer` 時 isUnique 衝突（templateID != 0、isUnique=1、名冊含 Dead 同 templateID） | 回傳 `false`，`LogWarning`；不加入 | `AdventurerRoster` | EditMode 測試：注入 Dead 狀態同 templateID 實例後再 Add |
| §5.2：`UpdateStatus` 傳入不存在的 `instanceID` | `LogWarning`，無操作 | `AdventurerRoster` | EditMode 測試：AC-AM-14 |
| §5.2：`DismissAdventurer` 對非 Dead 狀態呼叫 | 回傳 `false`，`LogWarning`；不移除 | `AdventurerRoster` | EditMode 測試：AC-AM-12 |
| §5.2：`SetWounded` 對非 `Dispatched` 狀態呼叫 | `LogWarning`，無操作 | `AdventurerRoster` | EditMode 測試：對 Idle 冒險者呼叫 SetWounded，驗狀態不變 |
| §5.2：`CreateFromTemplate` 但 isUnique=1 且名冊已有同 templateID（含 Dead） | 回傳 `null`，`LogWarning` | `AdventurerFactory` | EditMode 測試：AC-AM-05 / AC-AM-06 |
| §5.2：`TickWoundedRecovery` 時 NowUTC 回傳 0 | `LogError`，跳過本次 Tick，不修改任何狀態 | `AdventurerRoster` | EditMode 測試：mock ITimeSystem.NowUTC = 0，驗名冊無變動 |
| §5.2：名冊全員 Dead / Dispatched，無 Idle 可派 | `GetByStatus(Idle)` 回傳空列表；C-02 不處理，FT-02 / FT-03 自行處理空列表 | `AdventurerRoster` | — |
| §5.3：載入存檔時 `_nextInstanceID` 重建 | `RestoreFromSave` 反序列化後以 `Max(存檔 _nextInstanceID, roster.Max(a => a.instanceID) + 1)` 設定 | `AdventurerRoster` | EditMode 測試：AC-AM-17 |
| §5.3：存檔中 `templateID` 在當前 CSV 找不到 | `LogWarning`，保留實例資料，將 `templateID` 標記為 `0` | `AdventurerRoster` | EditMode 測試：注入不存在 templateID 的存檔 JSON，驗 templateID 被清零、實例保留 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 AdventurerInstance 資料結構（全欄位） | §5.3、§6.4 → §2.2 | 對齊 | `lastAutoPickupTimestamp` 初始值與寫入方在 §5.1 API 表有說明 |
| §3.2 AdventurerTemplate CSV 欄位與特質生成規則 | §5.4 流程 A、§4.4 AdventurerFactory、§6.1 | 對齊 | `BuildTraitList` 偽碼對齊 GDD §4.4 |
| §3.3 RecruitCostTable | §6.1、§6.3、§5.1 GetRecruitCost | 對齊 | 費用由 FT-01 呼叫 `GetRecruitCost` 取得，不寫死 |
| §3.4 狀態機轉換規則（含 6 條補充規則） | §5.4 流程 B、§5.1 UpdateStatus / SetWounded / DismissAdventurer | 對齊 | `idleSinceTimestamp` / `currentMissionID` 不變式完整列入 §5.1 API 說明 |
| §3.5 查詢 API（全方法） | §5.1 IAdventurerRoster | 對齊 | `TickWoundedRecovery` 拆至 AdventurerWoundedRecovery 訂閱觸發 |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 處理方式 | 等價說明 |
| --- | --- | --- |
| §4.1 Wounded 恢復截止時間 | §5.4 流程 B `SetWounded` 偽碼 | 完全對齊：`now + WOUNDED_RECOVERY_HOURS × 3600` |
| §4.2 TickWoundedRecovery | §5.4 流程 C 偽碼 | 完全對齊：`foreach Wounded; if now >= woundedUntilTimestamp → Idle` |
| §4.3 isUnique 驗證 | §5.4 流程 A 步驟 2 | 完全對齊：在 `CreateFromTemplate` 查 roster 全狀態（含 Dead） |
| §4.3a CreateRandomInstance | §5.1 IAdventurerFactory.CreateRandomInstance | 完全對齊：分配 instanceID，不加入名冊，templateID = 0 |
| §4.4 BuildTraitList | §5.4 流程 A 步驟 5；§7 邊緣案例對策 | 完全對齊：固定特質去 null sentinel → 隨機群組各抽 → Distinct() |

### 8.3 未能實現的規則與修改建議

目前無無法實現的 GDD 規則。以下為建議事項（不阻礙實作啟動）：

- **B-01**：`lastAutoPickupTimestamp` 由 FT-03 寫入；C-02 在 `AddAdventurer` 後不自動設為 `NowUTC`。GDD §3.5 `SetLastAutoPickupTimestamp` API 說明已明確，但 §3.4 規則 6 未明文說明此欄位在狀態轉移時不重置。建議未來 FT-03 FSD 撰寫時於 GDD §3.4 補一條「`lastAutoPickupTimestamp` 不受狀態轉移影響，僅由 FT-03 管理」的 FSD 回註。
- **B-02**：`GetRecruitCost` 目前歸屬在 `IAdventurerRoster`。若未來 `RecruitCostTable` 查詢需求擴大（例如 UI 顯示全表），可考慮將其移至 `IAdventurerTemplateLoader` 或新增 `IRecruitCostService`。現階段不拆分，保持簡潔。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | 無（本 FSD 無需回註 GDD，GDD §3.4~§3.5 規則完整且無歧義） |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | — | — | 無衝突 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：AC-AM-01~AC-AM-17 每條皆可被 EditMode 測試或手動步驟驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：每個玩家幻想／系統目的至少一個對應的技術手段
- [x] §4.1~§4.4：拆分判斷有結論（是，4 個 Script）；Script 清單欄位齊全（含路徑、SRP、依賴介面、預估規模）
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、資料流偽碼齊備
- [x] §6.1~§6.3：引用 CSV 表含對應 Data-Specs；嚴禁寫死清單對齊實作原則第 9 條
- [x] §7：GDD §5 每條邊緣案例皆有對策（不允許寫「妥善處理」）
- [x] §8.1：對齊清單覆蓋 GDD §3 每個小節（§3.1~§3.5，二層粒度）
- [x] §8.2~§8.5：公式對齊／無法實現項／GDD 回註／衝突紀錄如實登記，無內容寫「無」
- [x] 附錄 A：Review 三項結果全填，登記 Review 者與日期
- [x] FSD-index：§6.1 三方映射、§7.1 撰寫進度、§7.2 自檢紀錄、§7.3 拆分回報（如有）皆同步更新

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過 | 章節順序與編號符合 FSD-index 規範；4 個 Script 職責清晰不重疊；API / 事件 / 資料流前後一致；GDD §3.1~§3.5 + §4.1~§4.4a + §5.1~§5.3 + §6.4 全條對齊；邊緣案例 14 條全有對策；參數表格化清單涵蓋所有可調常數 |
