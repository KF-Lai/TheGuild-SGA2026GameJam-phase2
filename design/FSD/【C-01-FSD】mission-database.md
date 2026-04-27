# 【C-01-FSD】功能規格說明書 — Mission Database

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-01】mission-database.md`（版本：2026-04-27） |
| 對應 Data-Specs | `【C-01-DS】mission-template.md`<br>`【C-01-DS】mission-type-table.md`<br>`【C-01-DS】mission-category-table.md`<br>`【C-01-DS】mission-difficulty-table.md` |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中（P-01 主體覆核後判定為 Tech Debt 沿用，非實際衝突） |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-01 Mission Database 是任務資料的統一存取層，架設於 F-01 DataManager 之上。它載入並驗證四張機制資料表（`MissionTemplate`、`MissionDifficultyTable`、`MissionTypeTable`、`MissionCategoryTable`），提供 13 個查詢 API 給上層系統，並作為 D-02 Mission Content Database（`MissionNamePool`）的 Facade，讓上層系統透過單一接口取得完整任務資訊（機制 + 顯示文字）。本系統不持有任何執行時狀態；委託板當前任務清單由 FT-02 / FT-05 管理。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- 四張機制資料表的載入、FK 驗證、結構性約束檢查
- 13 個 GDD §3.3 定義的查詢 API
- 護送任務時長計算（`GetEscortDuration`）
- D-02 `MissionNamePool` Facade（`GetMissionText`）
- 所有 GDD §5 邊緣案例的防禦性處理

**Out-of-Scope**
- 委託板執行時任務清單管理（FT-02、FT-05 負責）
- 陣營分數累積邏輯（FT-09 負責）
- 存檔序列化（FT-10 負責，本系統僅提供 `GetTemplate` 供驗證）
- UI 顯示排版（P-02 負責，本系統只提供資料）
- `MissionNamePool` CSV 的內容維護（D-02 範疇）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，程式可驗證條件如下：

| DoD ID | 驗收條件 | 驗證方式 |
| --- | --- | --- |
| DoD-01 | CSV 載入零錯誤：四張表全部載入成功，Console 無 `LogError`（正常資料下） | EditMode test：`MissionDatabaseLoader_LoadsAllTables_NoError()` |
| DoD-02 | AC-MD-01：`GetRegularTemplates("D")` 回傳與 CSV 一致的 D 難度 `categoryID=0` 模板 | EditMode test：`GetRegularTemplates_D_ReturnsOnlyRegularD()` |
| DoD-03 | AC-MD-02：F 難度護送模板觸發 `LogError`，不進入查詢結果 | EditMode test：`EscortConstraint_FDifficulty_LogsErrorAndSkips()` |
| DoD-04 | AC-MD-03：`typeID=99` 觸發 `LogError`，模板被跳過 | EditMode test：`UnknownTypeID_LogsErrorAndSkips()` |
| DoD-05 | AC-MD-04：`factionID=99` 觸發 `LogWarning`，模板仍可查詢並視為 `FACTION_NEUTRAL_ID` | EditMode test：`UnknownFactionID_FallsBackToNeutral()` |
| DoD-06 | AC-MD-05/06/14/15/16：難度表各欄位查詢回傳 GDD §3.1 初始資料值 | EditMode test：`DifficultyTable_AllValues_MatchGDDDefaults()` |
| DoD-07 | AC-MD-07/08：`GetEscortDuration("D")` 100 次呼叫結果在 `[90, 150]` 且不全相同 | EditMode test：`GetEscortDuration_D_InRangeAndRandom()` |
| DoD-08 | AC-MD-09/10：`GetMissionText` 正常路徑回傳非空值；空池路徑回傳 fallback 並 `LogWarning` | EditMode test（需 mock D-02 資料） |
| DoD-09 | AC-MD-11：`IsValidCombination` 三項邏輯回傳值正確 | EditMode test：`IsValidCombination_EscortRules()` |
| DoD-10 | AC-MD-12/13：`GetTemplatesByCategory` 與 `GetTypeName` 回傳值正確 | EditMode test |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要：系統職責、「委託板當前任務由 FT-05 管理」範疇界定
- §3.1 詳細規則 — 四張資料表定義（欄位、FK、PK）
- §3.2 詳細規則 — 結構性約束（護送限 D~A；非 regular 不進常規池；neutral factionID 語義）
- §3.3 詳細規則 — 13 個查詢 API 簽名與說明
- §4.1 公式 — 護送任務時長計算（`GetEscortDuration`）
- §4.2 公式 — 文字查詢隨機抽取（`GetMissionText`）
- §4.3 公式 — 約束驗證（`IsValidCombination`）
- §5 邊緣案例 — 5.1 資料載入、5.2 查詢階段、5.3 資料一致性
- §6 依賴關係 — 上下游系統
- §7 可調參數 — SystemConstants 與各資料表調整範圍
- §8 驗收標準 — AC-MD-01~16

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-01-DS】mission-template.md` | `MissionTemplate.csv` | `missionID`, `difficulty`, `typeID`, `factionID`, `categoryID` | 任務模板載入、FK 驗證、篩選查詢 |
| `【C-01-DS】mission-type-table.md` | `MissionTypeTable.csv` | `typeID`, `typeName` | 類型名稱查詢；FK 驗證 typeID 合法性 |
| `【C-01-DS】mission-category-table.md` | `MissionCategoryTable.csv` | `categoryID`, `categoryName` | 類別名稱查詢；FK 驗證 categoryID 合法性 |
| `【C-01-DS】mission-difficulty-table.md` | `MissionDifficultyTable.csv` | `difficulty`, `baseReward`, `baseDuration`, `baseDeathRate`, `factionScoreDelta` | 難度數值查詢；FT-02 / FT-09 共用消費 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `ESCORT_TYPE_ID`, `FACTION_NEUTRAL_ID`, `ESCORT_DURATION_MULTIPLIER_MIN`, `ESCORT_DURATION_MULTIPLIER_MAX` | 護送約束判斷、護送時長計算 |

### 2.3 上游依賴系統

| 系統 | 依賴內容 |
| --- | --- |
| F-01 DataManager | `GetTable<T>` 取得四張機制表；`PickRandomWhere<MissionNameData>` 查詢 D-02 `MissionNamePool`；`GetInt`/`GetFloat` 取得 SystemConstants |

### 2.4 下游被依賴系統

| 系統 | 依賴的 API |
| --- | --- |
| FT-02 Mission Dispatch | `GetTemplate`, `GetBaseDeathRate` |
| FT-04 Outcome Resolution | `GetTemplate`, `GetBaseReward` |
| FT-06 Guild Core | `GetTemplatesByCategory(2)` |
| FT-09 Faction Story | `GetTemplatesByCategory(3)`, `GetTemplate`, `GetFactionScoreDelta` |
| FT-10 Save/Load | `GetTemplate`（驗證存檔 missionID 合法性） |
| P-02 Main UI | `GetTemplate`, `GetBaseReward`, `GetBaseDuration`, `GetMissionText`, `GetTypeName`, `GetCategoryName` |
| FT-05 Guild Gold Flow | 間接依賴（透過 FT-02 傳入的 `baseReward` 執行金流，不直接呼叫 C-01） |

### 2.5 跨系統事件契約

C-01 Mission Database 為純查詢系統，**不發布任何事件，也不訂閱任何事件**。資料載入完成後直接可查詢；上層系統在需要時主動呼叫 API。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家打開委託板，在三秒內直覺感受任務的難度、類型與危險程度，決定「這張可以接」或「太危險了」。委託的存在感來自清晰的難度分層與多樣的任務類型組合。

### 3.2 系統目的還原

Mission Database 是任務資料的統一真相來源（single source of truth），讓上層系統（派遣、結算、UI）透過穩定 API 取得完整任務資訊，同時讓設計師透過修改 CSV 即可調整所有委託屬性，無需碰觸程式碼。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 三秒感受任務壓力 | 委託板顯示難度標籤、報酬、預估時長 | `GetTemplate` + `GetBaseReward` + `GetBaseDuration` 回傳資料，P-02 渲染 |
| 護送任務有「長途遠征」感 | 護送委託時長明顯長於同難度一般任務（3~5 倍） | `GetEscortDuration` 套用 `ESCORT_DURATION_MULTIPLIER_MIN/MAX` 均勻隨機倍率 |
| 高難度任務有語義上的威懾感 | SSS 任務不在常規委託板出現 | `categoryID != 0` 的模板在 `GetRegularTemplates` 中被過濾 |
| 設計師改 CSV 立即見效 | 重啟遊戲後委託屬性反映最新 CSV | 所有數值來自 `MissionDifficultyTable` / `MissionTemplate`，無寫死值 |
| 任務有名稱與描述文字 | 委託板顯示任務名稱、描述段落 | `GetMissionText` 代理 D-02 `MissionNamePool` 隨機抽取 |
| 護送任務不出現極端難度 | 委託板護送任務只在 D~A 難度出現 | `IsValidCombination` 在資料載入時過濾違規模板 |
| 陣營劇情任務由特定系統觸發 | 玩家在特定條件下接到特殊任務 | `GetTemplatesByCategory(3)` 供 FT-09 專用存取 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

是。

### 4.2 拆分理由

GDD §3.3 定義 13 個 API，職責涵蓋：(A) 資料載入與 FK 驗證、(B) 一般查詢分發、(C) 護送時長計算（依賴 SystemConstants 隨機邏輯）、(D) D-02 文字 Facade（依賴 `PickRandomWhere` 與 fallback 邏輯）。若集中於單一類別：
- 職責超過 1 種，違反 SRP。
- FK 驗證邏輯（3 種類型 × 多種 fallback）+ 13 方法 + 護送計算 + D-02 proxy，預估 400~600 行，逼近或超過 500 行門檻。

對照 §2.4 拆分原則：「單一類別承擔 > 1 種職責 → 考慮拆分」，判定拆分為 4 個 Script，對應 4 種職責。

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| C-01-A | MissionDatabaseLoader | 載入四張 CSV、執行 FK 驗證與結構性約束過濾，產出四個唯讀字典 | §3.1、§3.2、§5.1、§5.3 |
| C-01-B | MissionDatabaseService | 對外門面（Facade），持有 Loader 產出的字典，實作 11 個一般查詢 API（排除護送時長與文字查詢） | §3.3（除 `GetEscortDuration` 與 `GetMissionText`） |
| C-01-C | EscortDurationCalculator | 單一方法：`GetEscortDuration(difficulty)`，讀取 `baseDuration` 與 SystemConstants 乘數，套用 `ceil(baseDuration × random(MIN, MAX))` | §3.3 `GetEscortDuration`、§4.1 |
| C-01-D | MissionTextFacade | 單一方法：`GetMissionText(difficulty, typeID)`，代理 DataManager `PickRandomWhere<MissionNameData>`，處理空池 fallback | §3.3 `GetMissionText`、§4.2 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `MissionDatabaseLoader.cs` | `Assets/Scripts/Gameplay/Mission/MissionDatabaseLoader.cs` | 載入並驗證四張任務 CSV，產出給 Service 使用的唯讀查找字典 | `IDataManager` | `250~350 行` |
| `MissionDatabaseService.cs` | `Assets/Scripts/Gameplay/Mission/MissionDatabaseService.cs` | 持有 Loader 字典並實作 11 個一般查詢 API，作為外部系統的單一入口 | `MissionDatabaseLoader`（組合）、`EscortDurationCalculator`、`MissionTextFacade` | `200~280 行` |
| `EscortDurationCalculator.cs` | `Assets/Scripts/Gameplay/Mission/EscortDurationCalculator.cs` | 計算護送任務時長（baseDuration × 均勻隨機倍率，ceil 取整） | `IDataManager`（讀 SystemConstants） | `40~80 行` |
| `MissionTextFacade.cs` | `Assets/Scripts/Gameplay/Mission/MissionTextFacade.cs` | 代理 DataManager 查詢 D-02 MissionNamePool，處理空池 fallback | `IDataManager` | `50~90 行` |

### 4.5 類別關係（可選）

```
MissionDatabaseService
  ├─ 組合 MissionDatabaseLoader   (Loader 在 Awake 完成，Service 持有結果字典)
  ├─ 組合 EscortDurationCalculator
  └─ 組合 MissionTextFacade

MissionDatabaseLoader     ─→  IDataManager (F-01)
EscortDurationCalculator  ─→  IDataManager (F-01, 讀 SystemConstants)
MissionTextFacade         ─→  IDataManager (F-01, PickRandomWhere)
```

外部系統只依賴 `MissionDatabaseService` 公開介面，不直接存取 Loader / Calculator / TextFacade。

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

以下均定義於 `MissionDatabaseService`（對外唯一入口）。

| 方法簽名 | 說明 | 錯誤行為 |
| --- | --- | --- |
| `GetTemplate(int missionID) : MissionTemplate` | 取得單筆任務模板 | 找不到回傳 `null`，`Debug.LogWarning` |
| `GetRegularTemplates(string difficulty) : IReadOnlyList<MissionTemplate>` | 篩選常規池（`categoryID=0`）指定難度模板 | 無結果回傳空列表，不報錯 |
| `GetRegularTemplates(string difficulty, int typeID) : IReadOnlyList<MissionTemplate>` | 篩選常規池加 typeID | 同上 |
| `GetTemplatesByCategory(int categoryID) : IReadOnlyList<MissionTemplate>` | 取得特定類別所有模板 | 無結果回傳空列表 |
| `GetBaseReward(string difficulty) : int` | 查 `MissionDifficultyTable.baseReward` | 缺失難度回傳 `0` |
| `GetBaseDuration(string difficulty) : int` | 查 `MissionDifficultyTable.baseDuration`（單位：分鐘，見 §8.3） | 缺失難度回傳 `0` |
| `GetEscortDuration(string difficulty) : int` | 護送時長 = `ceil(baseDuration × random(MIN, MAX))`（單位：分鐘，見 §8.3） | 缺失難度使用 `baseDuration=0` |
| `GetBaseDeathRate(string difficulty) : float` | 查 `MissionDifficultyTable.baseDeathRate`；值 clamp `[0.0, 1.0]` | 缺失難度回傳 `0.0` |
| `GetFactionScoreDelta(string difficulty) : int` | 查 `MissionDifficultyTable.factionScoreDelta`；值 clamp `≥0` | 缺失難度回傳 `0` |
| `GetMissionText(string difficulty, int typeID) : (string name, string desc)` | D-02 Facade，隨機抽一筆；空池回傳 `("未知委託", "（無描述）")` | 空池 `Debug.LogWarning` |
| `IsValidCombination(string difficulty, int typeID) : bool` | 護送（`typeID=ESCORT_TYPE_ID`）限 D/C/B/A；其他類型恆回傳 `true` | — |
| `GetTypeName(int typeID) : string` | 查 `MissionTypeTable.typeName` | 找不到回傳 `null`，`Debug.LogWarning` |
| `GetCategoryName(int categoryID) : string` | 查 `MissionCategoryTable.categoryName` | 找不到回傳 `null`，`Debug.LogWarning` |
| `GetAllMissionTypes() : IReadOnlyList<MissionTypeData>` | 回傳所有合法 typeID 列表，供 C-03/C-04/C-05 驗證 | 空表回傳空列表 |

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| （無） | — | — | C-01 為純查詢系統，不發布也不訂閱事件 |

### 5.3 資料結構

```csharp
// MissionTemplate — 對應 MissionTemplate.csv 每一列
public class MissionTemplate
{
    public int    missionID;
    public string difficulty;
    public int    typeID;
    public int    factionID;
    public int    categoryID;
}

// MissionDifficultyData — 對應 MissionDifficultyTable.csv 每一列
public class MissionDifficultyData
{
    public string difficulty;
    public int    baseReward;
    public int    baseDuration;    // 單位：分鐘（見 §8.3 時間單位問題）
    public float  baseDeathRate;
    public int    factionScoreDelta;
}

// MissionTypeData — 對應 MissionTypeTable.csv 每一列
public class MissionTypeData
{
    public int    typeID;
    public string typeName;
}

// MissionCategoryData — 對應 MissionCategoryTable.csv 每一列
public class MissionCategoryData
{
    public int    categoryID;
    public string categoryName;
}

// MissionNameData — 對應 D-02 MissionNamePool.csv 每一列（由 MissionTextFacade 使用）
public class MissionNameData
{
    public string difficulty;
    public int    typeID;
    public string missionName;
    public string missionDesc;
}
```

### 5.4 內部資料流

**觸發點 1：系統初始化（`MissionDatabaseLoader.Awake()`）**

```
MissionDatabaseLoader.LoadAll()
  → IDataManager.GetTable<MissionDifficultyData>()
      ├─ 建立 _difficultyDict: Dictionary<string, MissionDifficultyData>
      ├─ foreach row: 若 baseDeathRate 不在 [0.0,1.0] → LogError + Clamp
      └─ 若 factionScoreDelta < 0 → LogError + 設為 0

  → IDataManager.GetTable<MissionTypeData>()
      └─ 建立 _typeDict: Dictionary<int, MissionTypeData>
         若表完全空白 → LogError

  → IDataManager.GetTable<MissionCategoryData>()
      └─ 建立 _categoryDict: Dictionary<int, MissionCategoryData>
         若表完全空白 → LogError

  → IDataManager.GetTable<MissionTemplate>()
      └─ foreach row:
          ├─ 若 typeID 不在 _typeDict → LogError + 跳過
          ├─ 若 categoryID 不在 _categoryDict → LogError + 跳過
          ├─ 若 typeID == ESCORT_TYPE_ID && difficulty 不在 {D,C,B,A} → LogError + 跳過
          ├─ 若 factionID != FACTION_NEUTRAL_ID && factionID 不在 FactionRouteTable
          │     → LogWarning + 將 factionID 重設為 FACTION_NEUTRAL_ID
          ├─ 若 missionID 重複 → LogWarning + 後者覆蓋前者
          └─ 加入 _templateDict: Dictionary<int, MissionTemplate>
```

**觸發點 2：外部系統呼叫 `GetRegularTemplates(difficulty)`**

```
MissionDatabaseService.GetRegularTemplates(difficulty)
  → 查 _templateDict 所有 entry
      └─ where: difficulty == 參數 && categoryID == 0
      └─ 回傳 IReadOnlyList<MissionTemplate>（可為空列表）
```

**觸發點 3：FT-02 呼叫 `GetBaseDeathRate(difficulty)`**

```
MissionDatabaseService.GetBaseDeathRate(difficulty)
  → 查 _difficultyDict[difficulty]
      ├─ 找到 → 回傳 baseDeathRate（已在載入時 clamp）
      └─ 找不到 → LogError（若未在載入時報過） + 回傳 0.0f
```

**觸發點 4：呼叫 `GetEscortDuration(difficulty)`**

```
EscortDurationCalculator.GetEscortDuration(difficulty)
  → MissionDatabaseService.GetBaseDuration(difficulty) → baseDurationMin
  → min = IDataManager.GetFloat("ESCORT_DURATION_MULTIPLIER_MIN")
  → max = IDataManager.GetFloat("ESCORT_DURATION_MULTIPLIER_MAX")
  → multiplier = UnityEngine.Random.Range(min, max)   // 均勻浮點
  → return Mathf.CeilToInt(baseDurationMin * multiplier)
```

**觸發點 5：P-02 呼叫 `GetMissionText(difficulty, typeID)`**

```
MissionTextFacade.GetMissionText(difficulty, typeID)
  → IDataManager.PickRandomWhere<MissionNameData>(
        predicate: m => m.difficulty == difficulty && m.typeID == typeID,
        count: 1)
      ├─ 結果非空 → 回傳 (missionName, missionDesc)
      └─ 結果為空 → LogWarning + 回傳 ("未知委託", "（無描述）")
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `MissionTemplate.csv` | `missionID`, `difficulty`, `typeID`, `factionID`, `categoryID` | `【C-01-DS】mission-template.md` | 任務模板主表，所有查詢 API 的資料來源 | `MissionDatabaseLoader.Awake()` |
| `MissionTypeTable.csv` | `typeID`, `typeName` | `【C-01-DS】mission-type-table.md` | FK 驗證與 `GetTypeName` 查詢 | `MissionDatabaseLoader.Awake()` |
| `MissionCategoryTable.csv` | `categoryID`, `categoryName` | `【C-01-DS】mission-category-table.md` | FK 驗證與 `GetCategoryName` 查詢 | `MissionDatabaseLoader.Awake()` |
| `MissionDifficultyTable.csv` | `difficulty`, `baseReward`, `baseDuration`, `baseDeathRate`, `factionScoreDelta` | `【C-01-DS】mission-difficulty-table.md` | 難度數值查詢；FT-02 死亡率、FT-09 陣營加分共用 | `MissionDatabaseLoader.Awake()` |
| `SystemConstants.csv` | `ESCORT_TYPE_ID`, `FACTION_NEUTRAL_ID`, `ESCORT_DURATION_MULTIPLIER_MIN`, `ESCORT_DURATION_MULTIPLIER_MAX` | `【F-01-DS】system-constants.md` | 護送約束判斷與護送時長計算 | `Awake()` 時透過 `IDataManager.GetInt`/`GetFloat` 讀取 |
| `MissionNamePool.csv`（D-02） | `difficulty`, `typeID`, `missionName`, `missionDesc` | 目前 `_待建_`（D-02 DS 尚未建立） | `GetMissionText` D-02 Facade | 每次 `GetMissionText` 呼叫時透過 `PickRandomWhere` 即時查詢 |

### 6.2 引用的 ScriptableObject

無。C-01 全部資料透過 F-01 DataManager 讀取 CSV，不使用 ScriptableObject 儲存任務資料。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `ESCORT_TYPE_ID`（護送類型 ID，值 = 2） | `SystemConstants.csv` → `ESCORT_TYPE_ID` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `FACTION_NEUTRAL_ID`（中立陣營 ID，值 = 0） | `SystemConstants.csv` → `FACTION_NEUTRAL_ID` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `ESCORT_DURATION_MULTIPLIER_MIN`（護送倍率下限，值 = 3.0） | `SystemConstants.csv` → `ESCORT_DURATION_MULTIPLIER_MIN` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `ESCORT_DURATION_MULTIPLIER_MAX`（護送倍率上限，值 = 5.0） | `SystemConstants.csv` → `ESCORT_DURATION_MULTIPLIER_MAX` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `baseReward`（各難度基礎報酬） | `MissionDifficultyTable.csv` → `baseReward` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `baseDuration`（各難度基礎時長） | `MissionDifficultyTable.csv` → `baseDuration` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `baseDeathRate`（各難度基礎死亡率） | `MissionDifficultyTable.csv` → `baseDeathRate` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `factionScoreDelta`（各難度陣營加分） | `MissionDifficultyTable.csv` → `factionScoreDelta` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 護送允許難度集合（D/C/B/A） | `IsValidCombination` 邏輯依賴 `ESCORT_TYPE_ID`；難度集合由設計約束固定（GDD §3.2 硬規則），不從表格讀取（見 §8.3） | — |

---

## 7. 邊緣案例對策（Edge Case Handling）

### GDD §5.1 資料載入

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `MissionTemplate` 的 `typeID` 在 `MissionTypeTable` 找不到 | 載入時 `Debug.LogError` 記錄 `missionID`，`continue` 跳過該列不加入字典 | `MissionDatabaseLoader` | EditMode：注入含 `typeID=99` 的 CSV，確認該模板不在查詢結果，Console 有 `LogError` |
| 護送任務（`typeID=ESCORT_TYPE_ID`）出現在 F/E/S/SS/SSS 難度 | 載入時 `Debug.LogError` 記錄 `missionID`，`continue` 跳過 | `MissionDatabaseLoader` | DoD-03；EditMode：AC-MD-02 |
| `categoryID` 在 `MissionCategoryTable` 找不到 | 載入時 `Debug.LogError` 記錄 `missionID`，`continue` 跳過 | `MissionDatabaseLoader` | EditMode：注入含無效 `categoryID` 的 CSV |
| `factionID` 在 `FactionRouteTable` 找不到（且不為 `FACTION_NEUTRAL_ID`） | `Debug.LogWarning`，將該列 `factionID` 重設為 `FACTION_NEUTRAL_ID`，模板仍加入字典 | `MissionDatabaseLoader` | DoD-05；EditMode：AC-MD-04 |
| `MissionDifficultyTable` 缺少某難度的行 | 載入時 `Debug.LogError`；對應難度的 `GetBaseReward`/`GetBaseDuration` 回傳 `0`，`GetBaseDeathRate` 回傳 `0.0f`，`GetFactionScoreDelta` 回傳 `0` | `MissionDatabaseLoader`、`MissionDatabaseService` | DoD-06；EditMode：AC-MD-16 |
| `baseDeathRate` 不在 `[0.0, 1.0]` | 載入時 `Debug.LogError`，`Mathf.Clamp(value, 0f, 1f)` 修正後儲存 | `MissionDatabaseLoader` | EditMode：注入 `baseDeathRate=1.5`，確認儲存值 = 1.0 |
| `factionScoreDelta < 0` | 載入時 `Debug.LogError`，設為 `0` 後儲存 | `MissionDatabaseLoader` | EditMode：注入 `factionScoreDelta=-5`，確認儲存值 = 0 |

### GDD §5.2 查詢階段

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `GetTemplate(missionID)` 查詢不存在的 ID | 回傳 `null`，`Debug.LogWarning` | `MissionDatabaseService` | EditMode：`GetTemplate(-1)` 回傳 null 並有 LogWarning |
| `GetRegularTemplates(difficulty)` 無符合條件模板 | 回傳 `Array.Empty<MissionTemplate>()` 或等價空列表，不報錯 | `MissionDatabaseService` | EditMode：查詢無資料的難度，確認空列表且無 LogError |
| `GetMissionText(difficulty, typeID)` 對應 D-02 池為空 | 回傳 `("未知委託", "（無描述）")`，`Debug.LogWarning` | `MissionTextFacade` | DoD-08；EditMode：AC-MD-10 |
| `GetEscortDuration` 傳入非護送難度（如 F） | 仍執行計算並回傳結果，不拋錯（約束驗證由 `IsValidCombination` 負責） | `EscortDurationCalculator` | EditMode：確認 `GetEscortDuration("F")` 不拋例外 |

### GDD §5.3 資料一致性

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 同一 `missionID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning` 記錄重複的 `missionID`（沿用 DataManager 標準行為；Loader 在加入字典前檢查是否已存在） | `MissionDatabaseLoader` | EditMode：注入含重複 ID 的 CSV，確認最終保留後者值並有 LogWarning |
| `MissionTypeTable` 或 `MissionCategoryTable` 完全空白 | 載入時 `Debug.LogError`；所有依賴 `typeID`/`categoryID` 的查詢回傳空列表 | `MissionDatabaseLoader` | EditMode：注入空表，確認 `GetAllMissionTypes()` 回傳空列表 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 MissionTemplate 欄位定義 | §5.3、§6.1 | 對齊 | 五欄位全覆蓋 |
| §3.1 MissionDifficultyTable 欄位定義 | §5.3、§6.1 | 對齊 | 五欄位全覆蓋；初始資料值列於 GDD，FSD §6.3 嚴禁寫死 |
| §3.1 MissionTypeTable 欄位定義 | §5.3、§6.1 | 對齊 | |
| §3.1 MissionCategoryTable 欄位定義 | §5.3、§6.1 | 對齊 | |
| §3.2 結構性約束（護送限 D~A；非 regular 不進常規池；factionID=0 語義；FK 傳遞） | §4.4、§5.4、§7 | 對齊 | 四條硬規則全部在 Loader 與 Service 實作 |
| §3.3 全部 13 個查詢 API | §5.1 | 對齊 | 13 個方法全部列出，含簽名與錯誤行為 |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 採用方式 | 備註 |
| --- | --- | --- |
| §4.1 護送時長 = `ceil(baseDuration × random(MIN, MAX))` | 直接採用 GDD 公式；實作為 `Mathf.CeilToInt(baseDuration * Random.Range(min, max))` | `Random.Range(float, float)` 為均勻浮點，等價 GDD「均勻隨機浮點」 |
| §4.2 文字查詢 = `PickRandomWhere(difficulty, typeID)` | 直接採用；空池 fallback 同 GDD | — |
| §4.3 約束驗證 = `typeID == ESCORT_TYPE_ID → difficulty ∈ {D,C,B,A}` | 直接採用；`ESCORT_TYPE_ID` 不寫死，讀 SystemConstants | — |

### 8.3 未能實現的規則與修改建議

**P-01：時間單位 Tech Debt 沿用（非衝突）**

**主體覆核結果（2026-04-27）**：subagent 初稿將此項列為「衝突」過度敏感。F-02 GDD §3.1.2 已明定「任務時長單位為分鐘，內部運算統一轉換為秒（× 60）」；FT-02 GDD §3.5（line 112）進一步註記：「**Tech Debt**：全專案時間單位規範為『秒/小時』，C-01 + FT-02 + F-02 既有『分鐘』屬遺留共識，未來統一遷移時三系統需同步更新」。

C-01 FSD 沿用 GDD 既有共識：
- `MissionDifficultyTable.baseDuration` CSV 欄位儲存分鐘整數（沿用 GDD §3.1 初始資料）
- `GetBaseDuration` / `GetEscortDuration` 回傳分鐘（API 介面層沿用，避免破壞 FT-02 既有 `× 60` 轉換邏輯）
- 內部對 `Time.unscaledDeltaTime`／`Unix timestamp` 等運算層仍以秒為單位（對齊 F-02 §3.1.1 規範）

**未來統一遷移**：跨系統遷移屬 C-01 + FT-02 + F-02 協同變更（含 GDD §3.1 數值換算、API 命名 `Sec` 後綴、所有消費端調整），非單一 FSD 範疇，不在本 FSD 處理。

**修改建議**：保留現狀，等待專案層級的時間單位統一遷移工項；FSD 狀態可直接轉「已完成」。

**P-02：護送允許難度集合的寫死問題**

`IsValidCombination` 中護送允許難度集合 `{D, C, B, A}` 目前設計為硬規則（GDD §3.2 rule 1 明確列出），沒有對應的 CSV 欄位可查詢。若未來設計師希望調整護送難度範圍，需同時修改程式碼。

**修改建議**：可於 `MissionTypeTable.csv` 增加 `allowedDifficultyMin` / `allowedDifficultyMax` 欄位，或新增獨立映射表。目前保持現狀，待 GDD 更新後再處理。

**P-03：`MissionNamePool` D-02 Data-Specs 尚未建立**

`MissionNamePool.csv` 的 Data-Specs 目前標注為 `_待建_`。`MissionTextFacade` 的實作需依賴 D-02 DS 確認欄位名（尤其 `difficulty` 欄位值是否與 `MissionDifficultyTable` 完全對齊）。

**修改建議**：D-02 FSD 撰寫時應建立 `【D-02-DS】mission-name-pool.md`，並回填本 FSD §6.1 的 Data-Specs 連結。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 無 | — | — | 本 FSD 尚未對 GDD 新增任何回註；待 P-01 時間單位問題裁決後補登 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-27 | `baseDuration` 單位為「分鐘」議題（subagent 初稿登為「衝突」） | C-01 GDD §3.1 / §8、F-02 GDD §3.1.2、FT-02 GDD §3.5 line 112、全域 CLAUDE.md | 主體覆核判定**非衝突**：F-02 GDD §3.1.2 + FT-02 GDD line 112 已明確將「分鐘」標為跨系統 Tech Debt 共識；C-01 沿用此共識為合規處理。詳見 §8.3 P-01 覆核說明。FSD 標「審查中」僅待主體最終確認，不需使用者裁決。 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用（4 份 C-01-DS + 1 份 F-01-DS）、撰寫者、狀態、日期皆填妥
- [x] §1.3 完成目標：10 條 DoD 每條均可被 EditMode 測試驗證
- [x] §2.1~§2.5：GDD 章節引用（§1/§3.1~3.3/§4/§5/§6/§7/§8）、Data-Specs（5 份）、上游（F-01）、下游（7 系統）、事件契約（無，已說明理由）皆列舉
- [x] §3.3 對映表：7 條，覆蓋 GDD §1 系統目的與 §2 玩家幻想所有面向
- [x] §4.1~§4.4：拆分判斷有結論（拆 4 個 Script）；Script 清單路徑、SRP、依賴介面、預估規模全部填妥
- [x] §5.1~§5.4：13 個 API 簽名全列出；事件清單（無）說明；4 個 DTO 資料結構定義；5 個觸發點資料流偽碼齊備
- [x] §6.1~§6.3：6 張 CSV 含對應 Data-Specs；§6.3 嚴禁寫死清單 8 項，皆對齊原則第 9 條
- [x] §7：GDD §5 三節（5.1/5.2/5.3）共 14 個邊緣案例，全部給出具體程式對策（無「妥善處理」）
- [x] §8.1 對齊清單：覆蓋 GDD §3 所有節（§3.1 四表 / §3.2 / §3.3），二層粒度
- [x] §8.2~§8.5：公式對齊 3 條已勾選；未能實現 3 項（P-01/P-02/P-03）；GDD 回註「無」已登記；衝突紀錄 1 條（P-01 時間單位）
- [x] FSD-index §6.1 / §7.1 / §7.2 / §7.3 已同步更新（見下方 Edit 操作）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過（含 3 項待裁決事項） | §8.5 登記時間單位衝突；P-01 需使用者裁決；P-02/P-03 為建議項，不阻礙實作 |
| 2026-04-27 | Claude Code 主體（覆核 patch） | 通過 | 通過 | 通過 | P-01 覆核：F-02 GDD §3.1.2 + FT-02 GDD line 112 已將「分鐘」明確標為 Tech Debt 共識；非衝突，FSD 沿用即合規。§8.3 P-01 / §8.5 衝突紀錄已修正措辭。 |
