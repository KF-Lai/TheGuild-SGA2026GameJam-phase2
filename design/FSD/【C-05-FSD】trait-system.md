# 【C-05-FSD】功能規格說明書 — Trait System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-05】trait-system.md`（版本：2026-04-20） |
| 對應 Data-Specs | `【C-05-DS】trait-table.md`<br>`【C-05-DS】trait-group-table.md` |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（自檢） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-05 Trait System 負責：從 CSV 載入特質靜態資料（`TraitTable`、`TraitGroupTable`）；提供查詢 API 供下游取得特質定義；以 Fisher-Yates 無放回抽樣執行群組特質抽取（`RollTraits`）；透過 `IProfessionService` 橋接職業 → 群組映射。本系統不持有任何 runtime 冒險者狀態，不執行成功率/死亡率/willingness 的實際套用計算（套用由 FT-02/FT-03/FT-04 各自負責），不持有 `ProfessionTraitPool`（合併於 C-03 `ProfessionTable`）。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- `TraitTable.csv` 與 `TraitGroupTable.csv` 的 CSV 載入與快取
- 特質查詢 API：`GetTrait`、`GetAllTraits`、`GetTraitsByType`
- 群組查詢 API：`GetTraitGroup`
- 職業群組查詢 API：`GetProfessionGroups`（橋接 C-03 `IProfessionService`）
- `RollTraits`：依 `pickMode = "uniform"` 執行 Fisher-Yates 無放回抽樣
- `effectType` / `effectTarget` 的合法性驗證（載入期）與零值（`0`）sentinel 處理

**Out-of-Scope**
- `stat` / `behavior` / `condition` 特質效果的實際套用計算（分別由 FT-02、FT-03、FT-04 執行）
- `weighted` pickMode 的完整實作（GDD §3.3 標為 Game Jam 預留）
- 冒險者實例的特質去重（由 C-02 `BuildTraitList` 的 `Distinct()` 負責）
- UI 顯示（由 P-02 消費 `GetTrait(id).name/.description`）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，補充可驗證條件：

| ID | 驗收條件 | 驗證方式 |
| --- | --- | --- |
| AC-TS-01 | `GetAllTraits()` 回傳數量與 `TraitTable.csv` 行數一致 | EditMode test：載入 mock CSV，斷言 count 相等 |
| AC-TS-02 | `GetTraitsByType("stat")` 只含 `effectType = "stat"` 的特質 | EditMode test：斷言過濾結果 effectType 全為 "stat" |
| AC-TS-03 | `GetTraitsByType("condition")` 只含 `effectType = "condition"` | EditMode test |
| AC-TS-04 | `GetTrait(0)` 回傳 `null` 並出現 `LogWarning` | EditMode test：`LogAssert.Expect(LogType.Warning, ...)` |
| AC-TS-05 | `effectTarget` 不合法的特質在啟動時出現 `LogError`，`GetAllTraits()` 不含該特質 | EditMode test：注入含非法 effectTarget 的 mock CSV |
| AC-TS-06 | `RollTraits(uniform, pickCount=1, pool=5)` 呼叫 100 次，每次回傳 1 個 traitID，分布覆蓋全部 5 個 | EditMode test：HashSet 累計 100 次結果 |
| AC-TS-07 | `RollTraits(pickCount=1, pool=1 有效 ID)` 回傳該 ID 並出現 `LogWarning` | EditMode test |
| AC-TS-08 | `GetProfessionGroups(1)` 回傳戰士對應的 3 個群組（groupID = 1, 3, 4） | EditMode test：mock C-03 回傳 traitGroupIDs=[1,3,4] |
| AC-TS-09~15 | 詳見 GDD §8 原文（效果套用測試由 FT-02/03/04 負責，此處不重複列舉） | FT-02/03/04 FSD 各自規劃 PlayMode test |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §3.1：TraitTable 資料表欄位定義
- §3.2：effectTarget 完整合法變數空間（`stat` / `behavior` / `condition` 三組）
- §3.3：TraitGroupTable 資料表欄位定義與 Game Jam 初始群組（5 個）
- §3.4：職業 → 特質群組消費端說明（traitGroupIDs 合併於 C-03 ProfessionTable）
- §3.5：查詢 API 簽名清單
- §4.1：RollTraits 演算法（Fisher-Yates 無放回抽樣）
- §4.2~§4.4：stat/behavior/condition 效果套用偽碼（規格定義給下游，C-05 不實作套用）
- §5：邊緣案例（載入期驗證 + runtime 查詢防衛）
- §6：上下游依賴關係
- §7：可調參數安全範圍
- §8：驗收標準

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-05-DS】trait-table.md` | `TraitTable.csv` | `traitID`, `name`, `description`, `effectType`, `effectTarget`, `effectValue` | 載入所有特質靜態定義，建立 `traitID → TraitData` 快取 |
| `【C-05-DS】trait-group-table.md` | `TraitGroupTable.csv` | `groupID`, `groupName`, `traitIDs`, `pickCount`, `pickMode` | 載入特質群組定義，建立 `groupID → TraitGroupData` 快取 |
| `【C-03-DS】profession-table.md` | `ProfessionTable.csv` | `professionID`, `traitGroupIDs` | 消費端引用：runtime 透過 `IProfessionService` 取 traitGroupIDs，不直接讀 CSV |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 介面 |
| --- | --- | --- |
| F-01 DataManager | 載入 `TraitTable.csv` 與 `TraitGroupTable.csv`，提供 `GetAll<T>()` 原始記錄列表 | `IDataManager` |
| C-03 Profession System | runtime 查詢職業的 `traitGroupIDs` 欄位；提供合法 professionID 集合 | `IProfessionService` |

### 2.4 下游被依賴系統

| 系統 | 依賴內容 | 使用介面 |
| --- | --- | --- |
| C-02 Adventurer Management | 生成冒險者時依職業群組抽取特質 | `ITraitService.GetProfessionGroups(professionID)`、`ITraitService.RollTraits(group)` |
| FT-01 Adventurer Recruitment | 生成招募候選冒險者時觸發 C-02 生成流程（間接消費） | — |
| FT-02 Mission Dispatch | 讀取 `stat` 類特質定義，計算成功率/死亡率修正 | `ITraitService.GetTraitsByType("stat")`、`ITraitService.GetTrait(id)` |
| FT-03 NPC Decision | 讀取 `behavior` 類特質定義，計算 willingness 修正 | `ITraitService.GetTraitsByType("behavior")`、`ITraitService.GetTrait(id)` |
| FT-04 Outcome Resolution | 讀取 `condition` 類特質定義，執行結算觸發 | `ITraitService.GetTraitsByType("condition")`、`ITraitService.GetTrait(id)` |
| P-02 Main UI | 顯示冒險者特質名稱與描述 | `ITraitService.GetTrait(id).name`、`.description` |

### 2.5 跨系統事件契約

C-05 為純靜態資料查詢系統，**不發布任何事件，不訂閱任何事件**。特質資料在 F-01 DataManager 初始化後即可查詢；無 runtime 狀態異動需通知。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

每位冒險者因特質而有獨特個性：「熱血戰士」、「膽小法師」、「倒楣遊俠」。玩家根據特質作出派遣決策（高危任務選幸運兒，低難度任務送膽小鬼），`condition` 特質在結算時帶來驚喜或心痛的意外，使每筆任務結果成為一段故事而非純粹數值計算。

### 3.2 系統目的還原

提供冒險者特質的靜態資料層與群組抽取機制，讓 C-02 能在生成時隨機化特質組合，讓 FT-02/FT-03/FT-04 能依特質定義執行對應的數值修正或觸發效果，同時確保所有參數皆來自 CSV 且可熱調整。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 同職業冒險者各有不同個性 | 同為戰士，一個顯示「幸運兒」，另一個顯示「倒楣鬼」 | `TraitGroupTable.traitIDs` 定義群組特質池；`RollTraits` 以 Fisher-Yates 抽樣確保同批次不重複且隨機 |
| 特質影響派遣數值決策 | 同難度任務，帶「強運」的冒險者成功率面板數字更高 | `GetTraitsByType("stat")` 回傳 FT-02 所需的 delta 欄位；FT-02 以加法疊加後 clamp |
| 特質影響 NPC 意願行為 | 「膽小」冒險者拒絕 S 難度任務 | `GetTraitsByType("behavior")` 回傳 FT-03 所需的 willingness delta；effectTarget 區分任務類型與難度段 |
| 結算時出現意外效果 | 「亡命徒」在死亡判定後存活；「強運兒」在成功時多賺一筆 | `GetTraitsByType("condition")` 回傳 FT-04 所需的觸發機率與 effectTarget；FT-04 按 GDD §4.4 偽碼執行 |
| 職業決定特質方向性 | 戰士偏向戰鬥數值+戰鬥行為特質，傭兵無固定戰鬥傾向 | `GetProfessionGroups(professionID)` 橋接 C-03 `IProfessionService` 取 traitGroupIDs，再反查 `TraitGroupTable` |
| 特質數值可熱調整 | 設計師改 CSV 即可調整特質強度，不需重新編譯 | `effectValue` 存於 `TraitTable.csv`；`pickCount`、`traitIDs` 池存於 `TraitGroupTable.csv`；§6.3 嚴禁寫死清單對應 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

是（4 個 Script，對齊 C-03/C-04 的職責結構）。

### 4.2 拆分理由

- `TraitData` / `TraitGroupData`：純資料類別，職責為序列化映射，無行為邏輯，與查詢 / 抽取 Script 分離有助單元測試注入 mock 資料。
- `ITraitService`：對外介面隔離，下游（C-02/FT-02/03/04/P-02）依賴介面而非具體實作，符合 DIP；同時允許 EditMode test mock。
- `TraitDatabaseLoader`：單一職責：讀 F-01 DataManager → 驗證 → 建 dict 快取。不持有查詢 / 抽取邏輯，預估 150~200 行。
- `TraitService`：單一職責：提供查詢 API 與 RollTraits 演算法，持有 `IProfessionService` 引用以實作 `GetProfessionGroups`。預估 150~200 行。
- 合計 4 Script，遠低於「3~8 Script」上限，各類別預估規模均 < 300 行，無需再拆分 FSD。

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| C-05-A | 資料類別 | TraitData / TraitGroupData 欄位定義 | §3.1、§3.3 |
| C-05-B | 服務介面 | ITraitService 查詢 API 與抽取 API 宣告 | §3.5 |
| C-05-C | 資料載入器 | TraitDatabaseLoader：CSV 解析、驗證、快取 | §3.1~§3.3、§5.1 |
| C-05-D | 服務實作 | TraitService：查詢 + RollTraits + GetProfessionGroups | §3.4、§3.5、§4.1 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `TraitData.cs` | `Assets/Scripts/Gameplay/Trait/TraitData.cs` | 定義特質靜態欄位（traitID、name、description、effectType、effectTarget、effectValue） | — | `< 40 行` |
| `TraitGroupData.cs` | `Assets/Scripts/Gameplay/Trait/TraitGroupData.cs` | 定義特質群組靜態欄位（groupID、groupName、traitIDs[]、pickCount、pickMode） | — | `< 40 行` |
| `ITraitService.cs` | `Assets/Scripts/Gameplay/Trait/ITraitService.cs` | 宣告 C-05 所有對外查詢與抽取 API 介面 | — | `< 50 行` |
| `TraitDatabaseLoader.cs` | `Assets/Scripts/Gameplay/Trait/TraitDatabaseLoader.cs` | 從 F-01 DataManager 讀取 TraitTable / TraitGroupTable 並驗證合法性，建立 ID → 資料 dict 快取 | `IDataManager` | `150~200 行` |
| `TraitService.cs` | `Assets/Scripts/Gameplay/Trait/TraitService.cs` | 實作 ITraitService：提供特質/群組查詢 API、Fisher-Yates RollTraits、GetProfessionGroups 橋接 | `ITraitService`、`IProfessionService`、`TraitDatabaseLoader` | `150~200 行` |

### 4.5 類別關係（可選）

```
TraitDatabaseLoader
  ├─ 依賴：IDataManager（F-01）
  ├─ 建立：Dictionary<int, TraitData>
  └─ 建立：Dictionary<int, TraitGroupData>

TraitService : ITraitService
  ├─ 依賴：TraitDatabaseLoader（取快取 dict）
  ├─ 依賴：IProfessionService（C-03，取 traitGroupIDs）
  └─ 實作：GetTrait / GetAllTraits / GetTraitsByType /
           GetTraitGroup / RollTraits / GetProfessionGroups
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

定義於 `ITraitService`：

```
// 單筆特質查詢；traitID=0 或不存在回傳 null + LogWarning
GetTrait(int traitID) : TraitData

// 全特質列表（只讀視圖）
GetAllTraits() : IReadOnlyList<TraitData>

// 依 effectType 篩選（"stat" / "behavior" / "condition"）
GetTraitsByType(string effectType) : IReadOnlyList<TraitData>

// 單筆群組查詢；groupID=0 或不存在回傳 null + LogWarning
GetTraitGroup(int groupID) : TraitGroupData

// 依群組定義抽取 traitID 列表（Fisher-Yates uniform / fallback 全回傳）
RollTraits(TraitGroupData group) : int[]

// 取職業對應的群組列表；透過 IProfessionService 橋接 traitGroupIDs；
// professionID 不存在或 traitGroupIDs 為空 → 回傳空列表 + LogWarning
GetProfessionGroups(int professionID) : IReadOnlyList<TraitGroupData>
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| — | — | — | C-05 不發布亦不訂閱事件（純靜態查詢服務） |

### 5.3 資料結構

**TraitData**（CSV 列映射）：
```
TraitData
  int    traitID         // PK，0 = null sentinel
  string name
  string description
  string effectType      // "stat" | "behavior" | "condition"
  string effectTarget    // 合法清單見 GDD §3.2
  float  effectValue     // 依 effectTarget 語意解讀（delta 或機率）
```

**TraitGroupData**（CSV 列映射）：
```
TraitGroupData
  int    groupID         // PK，0 = null sentinel
  string groupName
  int[]  traitIDs        // pipe 分隔解析，0 過濾
  int    pickCount
  string pickMode        // "uniform" | "weighted"（Game Jam 僅 uniform）
```

### 5.4 內部資料流

**流程 A — 系統初始化（TraitDatabaseLoader）**

```
Bootstrap（F-01 DataManager 初始化後觸發）
  → TraitDatabaseLoader.Load()
      ├─ IDataManager.GetAll<TraitData>()
      │    ├─ foreach row：
      │    │    ├─ if traitID == 0 → skip
      │    │    ├─ if effectType ∉ {"stat","behavior","condition"} → LogError, skip
      │    │    ├─ if effectTarget ∉ GDD §3.2 合法清單 → LogError, skip
      │    │    └─ _traitDict[traitID] = row（重複 ID → 覆蓋 + LogWarning）
      │    └─ 建立 _traitsByType[effectType] 分組快取
      ├─ IDataManager.GetAll<TraitGroupData>()
      │    └─ foreach row：
      │         ├─ if groupID == 0 → skip
      │         ├─ 過濾 traitIDs 中不存在於 _traitDict 的 ID（非0）→ LogWarning
      │         ├─ if pickMode ∉ {"uniform","weighted"} → LogError, fallback "uniform"
      │         └─ _groupDict[groupID] = row（重複 ID → 覆蓋 + LogWarning）
      └─ 標記 IsLoaded = true
```

**流程 B — C-02 BuildTraitList 呼叫**

```
C-02.AdventurerFactory.BuildTraitList(professionID)
  → ITraitService.GetProfessionGroups(professionID)
      ├─ IProfessionService.GetProfession(professionID) → ProfessionData
      ├─ if ProfessionData == null || traitGroupIDs 為空 → LogWarning, 回傳 []
      └─ foreach groupID in ProfessionData.traitGroupIDs：
           ├─ GetTraitGroup(groupID)
           └─ if null → LogWarning, skip（否則加入結果列表）

  → foreach group in groups：
      ITraitService.RollTraits(group)
          ├─ pool = group.traitIDs.Filter(id != 0)
          ├─ if pool.Count < group.pickCount → LogWarning, return pool（全回傳）
          ├─ if group.pickMode == "uniform"：
          │    Fisher-Yates Shuffle(pool) → Take(group.pickCount) → return traitIDs[]
          └─ （weighted 預留）→ fallback uniform

  → 累積所有 traitID（固定 + 隨機）
  → C-02 對結果執行 Distinct() 去重（C-05 不負責去重）
```

**流程 C — FT-02 讀取 stat 特質**

```
FT-02.SuccessRateCalculator.Calculate(traitIDs, typeID)
  → ITraitService.GetTraitsByType("stat") → IReadOnlyList<TraitData>
  → foreach traitID in adventurer.traitIDs：
       ITraitService.GetTrait(traitID) → TraitData
       if TraitData.effectType != "stat" → skip
       依 effectTarget 累加 delta 到 baseSuccessRate / baseDeathRate
  → clamp 由 FT-02 執行
```

（FT-03/FT-04 流程類似，effectType 篩選分別為 "behavior" / "condition"）

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `TraitTable.csv` | `traitID`, `name`, `description`, `effectType`, `effectTarget`, `effectValue` | `【C-05-DS】trait-table.md` | 全部特質靜態定義；建立 traitID → TraitData dict 與 effectType 分組快取 | F-01 DataManager 初始化後，`TraitDatabaseLoader.Load()` 一次載入 |
| `TraitGroupTable.csv` | `groupID`, `groupName`, `traitIDs`, `pickCount`, `pickMode` | `【C-05-DS】trait-group-table.md` | 群組定義；建立 groupID → TraitGroupData dict | 同上 |
| `ProfessionTable.csv` | `professionID`, `traitGroupIDs` | `【C-03-DS】profession-table.md` | 消費端：runtime 透過 `IProfessionService` 取 traitGroupIDs（C-05 不直接讀此 CSV） | 隨 C-03 初始化載入 |

### 6.2 引用的 ScriptableObject

無。C-05 全部參數來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `effectValue`（成功率 / 死亡率 delta 閾值） | `TraitTable.csv → effectValue` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `effectValue`（willingness delta 閾值） | `TraitTable.csv → effectValue` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `effectValue`（condition 觸發機率） | `TraitTable.csv → effectValue` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `pickCount`（群組抽取數量） | `TraitGroupTable.csv → pickCount` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `traitIDs`（群組特質池） | `TraitGroupTable.csv → traitIDs` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `traitGroupIDs`（職業群組映射） | `ProfessionTable.csv → traitGroupIDs`（owner：C-03） | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `effectType` 不在 {"stat","behavior","condition"} | `TraitDatabaseLoader.Load()`：`Debug.LogError`，跳過該特質（不加入 dict） | `TraitDatabaseLoader` | EditMode test：注入非法 effectType 的 mock 資料，斷言 dict 不含該 ID |
| `effectTarget` 不在 GDD §3.2 合法清單 | 同上：`Debug.LogError`，跳過 | `TraitDatabaseLoader` | EditMode test：同上 |
| `TraitGroupTable.traitIDs` 包含不存在於 TraitTable 的 ID（非0） | `Debug.LogWarning`，過濾該 traitID，其餘正常；載入期執行，不在 RollTraits 重複檢查 | `TraitDatabaseLoader` | EditMode test：注入孤立 traitID，斷言 group.traitIDs 不含該值 |
| `TraitGroupTable.pickCount` > 有效 traitIDs 數量 | `RollTraits`：`Debug.LogWarning`，回傳所有有效特質（不補足） | `TraitService` | EditMode test：pool=2、pickCount=3，斷言回傳長度 = 2 且有 Warning |
| `TraitGroupTable.pickMode` 不在 {"uniform","weighted"} | `TraitDatabaseLoader.Load()`：`Debug.LogError`，fallback 儲存 "uniform" | `TraitDatabaseLoader` | EditMode test：注入非法 pickMode，斷言 group.pickMode = "uniform" |
| C-03 `ProfessionTable.traitGroupIDs` 含不存在於 TraitGroupTable 的 groupID（非0） | `GetProfessionGroups`：`Debug.LogWarning`，跳過該 groupID，其餘正常 | `TraitService` | EditMode test：mock IProfessionService 回傳含孤立 groupID，斷言結果列表不含該群組 |
| 職業在 C-03 中無對應行 / `traitGroupIDs` 為空 | `GetProfessionGroups`：`Debug.LogWarning`，回傳空列表；C-02 `BuildTraitList` 抽取 0 個特質（保留 fixedTraitIDs）| `TraitService` | EditMode test：mock IProfessionService 回傳 null，斷言回傳空列表 |
| 同一 traitID 在 CSV 中出現兩次 | `TraitDatabaseLoader.Load()`：後者覆蓋前者 + `Debug.LogWarning`（DataManager 標準行為） | `TraitDatabaseLoader` | EditMode test：注入重複 ID，斷言 dict count = 1，最後一筆值被採用 |
| `GetTrait(0)` | `TraitService.GetTrait(0)`：回傳 `null` + `Debug.LogWarning` | `TraitService` | EditMode test：`LogAssert.Expect(Warning)`，斷言回傳 null |
| `GetTraitGroup(0)` | `TraitService.GetTraitGroup(0)`：回傳 `null` + `Debug.LogWarning` | `TraitService` | EditMode test：同上 |
| `ApplyConditionTraits` 中 effectTarget 為未知值（runtime 呼叫） | FT-04 執行套用邏輯時 `Debug.LogWarning`，跳過該特質，繼續處理其他特質（C-05 載入期已過濾，此為 FT-04 防衛層） | FT-04（C-05 不負責套用） | FT-04 FSD 規劃 |
| traitIDs 列表中有重複 traitID | C-02 `BuildTraitList` 已對最終列表執行 `Distinct()`，此情況不傳入本系統 | C-02 | C-02 FSD §7 |
| `on_death_survive` 與 `on_fail_survive` 同時存在 | 按 traitIDs 順序依次執行；先觸發者將 `isDead` 改 `false` 後，後者條件 `outcome.isDead` 為 false 自然跳過，無衝突（GDD §4.4 備註已說明） | FT-04（C-05 只提供資料） | FT-04 FSD 規劃 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 TraitTable 欄位定義（traitID/name/description/effectType/effectTarget/effectValue） | §5.3 TraitData 資料結構、§6.1 CSV 表 | 對齊 | 6 欄全部對應 |
| §3.2 effectTarget 完整合法變數空間（stat 10 項 + behavior 8 項 + condition 5 項） | §7 邊緣案例（effectTarget 非法處理）、§6.3 嚴禁寫死清單 | 對齊 | 合法清單由 TraitDatabaseLoader 驗證；不寫死於程式碼，以 CSV effectTarget 為準 |
| §3.3 TraitGroupTable 欄位定義與 Game Jam 初始群組 | §5.3 TraitGroupData 資料結構、§6.1 CSV 表 | 對齊 | pickMode "weighted" 預留，Game Jam 實作 fallback uniform |
| §3.4 職業 → 特質群組（消費 C-03 ProfessionTable.traitGroupIDs） | §2.3 上游依賴、§4.5 類別關係、§5.4 資料流 B、§5.1 GetProfessionGroups API | 對齊 | C-05 透過 IProfessionService 橋接，不直接讀 ProfessionTable CSV |
| §3.5 查詢 API 清單（GetTrait / GetAllTraits / GetTraitsByType / GetTraitGroup / RollTraits / GetProfessionGroups） | §5.1 公開 API、§4.4 ITraitService Script | 對齊 | 6 個 API 全部對應 |

### 8.2 公式對齊或替代說明

- **GDD §4.1 RollTraits**：採用 GDD 原始偽碼規格。Fisher-Yates Shuffle 由 `TraitService` 自行實作（`System.Random` 實例化後對 `pool` 陣列原地 shuffle），不依賴外部套件，結果與 GDD 偽碼等價。`weighted` 模式預留方法體，Game Jam 期間 fallback uniform 並 `Debug.LogWarning`。
- **GDD §4.2~§4.4 效果套用偽碼**：這些公式屬 FT-02/FT-03/FT-04 的實作規格，C-05 FSD 不重複規劃；FT-02/03/04 各自 FSD 撰寫時對齊 GDD §4.2~§4.4 對應章節。

### 8.3 未能實現的規則與修改建議

- **B-01（非阻礙項）**：`effectTarget` 合法清單在 `TraitDatabaseLoader` 中以 `HashSet<string>` 硬編碼驗證（GDD §3.2 清單共 23 項）。此清單本質上是業務規則常數，GDD 已是唯一真實來源，不適合額外用 CSV 維護。若未來 `effectTarget` 需要擴充，修改 `TraitDatabaseLoader` 的 HashSet 即可（屬 Small 工項），不涉及 FT-02/03/04 修改（新 effectTarget 需對應下游新增處理分支）。
- **B-02（非阻礙項）**：GDD §3.3 `pickMode` 欄位描述 "weighted" 為「預留未來擴充」，Game Jam 不實作。`TraitService.RollTraits` 對 `pickMode = "weighted"` 輸出 `Debug.LogWarning` 並 fallback uniform，確保不崩潰。建議 `TraitGroupTable.csv` 備註此限制。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-27 | `【C-05】trait-system.md` | §3.3 | FSD 回註：`pickMode = "weighted"` Game Jam 期間 fallback uniform，`TraitService.RollTraits` 輸出 LogWarning；正式擴充需補實作 `WeightedSample` 並移除 fallback |
| 2026-04-27 | `【C-05】trait-system.md` | §3.5 | FSD 回註：`GetProfessionGroups` 在 professionID 不存在或 traitGroupIDs 空時回傳空列表 + LogWarning（非例外）；C-02 `BuildTraitList` 對空列表的行為由 C-02 FSD §5.4 定義 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-27 | 無衝突發現。時間單位：C-05 GDD 無涉及秒/分/小時的計時欄位，不適用時間單位規則。跨系統邊界：effectTarget 合法清單與 FT-02/03/04 套用邏輯在 GDD §4.2~§4.4 對齊，無矛盾。 | — | 無需處理 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用（2 份）、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：AC-TS-01~08 每條皆有 EditMode test 驗證方式（AC-TS-09~15 由 FT-02/03/04 規劃）
- [x] §2.1~§2.5：GDD 章節（§3.1~§4.4、§5~§8）、Data-Specs（2 份 C-05-DS + C-03-DS 消費端）、上游（F-01/C-03）、下游（C-02/FT-01/FT-02/FT-03/FT-04/P-02）、事件契約（無）四向皆列舉
- [x] §3.3 對映表：6 個玩家幻想／系統目的皆有對應技術手段
- [x] §4.1~§4.4：拆分判斷有結論（4 Script）；Script 清單路徑、SRP、依賴介面、預估規模欄位齊全
- [x] §5.1~§5.4：API 簽名（6 個）、事件清單（無）、資料結構（TraitData / TraitGroupData）、資料流偽碼（A/B/C 三段）齊備
- [x] §6.1~§6.3：引用 CSV（2 張 owner + 1 張消費端）含對應 Data-Specs；嚴禁寫死清單 6 項對齊原則第 9 條
- [x] §7：GDD §5 所有邊緣案例（5.1 載入 8 項 + 5.2 runtime 5 項）皆有對策
- [x] §8.1：對齊清單覆蓋 GDD §3 全部小節（§3.1~§3.5）
- [x] §8.2~§8.5：公式對齊／建議項 B-01/B-02／GDD 回註 2 筆／衝突紀錄如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（見本次 Edit 操作）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（自檢） | 通過 | 通過 | 通過 | 章節編號與標題與 FSD-index §三完全一致；Script 職責清晰（4 Script 對齊 C-03/C-04 模式）；GDD §3.1~§3.5 全條目對齊；邊緣案例 13 項全有對策；無循環依賴；建議項 B-01/B-02 不阻礙實作 |
