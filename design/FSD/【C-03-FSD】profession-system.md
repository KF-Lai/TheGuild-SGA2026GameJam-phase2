# 【C-03-FSD】功能規格說明書 — Profession System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-03】profession-system.md`（版本：2026-04-19） |
| 對應 Data-Specs | `【C-03-DS】profession-table.md` |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-03 Profession System 負責從 `ProfessionTable.csv` 載入職業靜態資料、執行資料完整性驗證，並對外提供職業查詢 API。系統完全資料驅動：新增職業只需在 CSV 加行，程式碼無需改動。系統不持有任何 runtime 狀態，所有查詢均回傳不可變資料物件。

### 1.2 In-Scope / Out-of-Scope

**In-Scope：**
- `ProfessionTable.csv` 載入與解析（含 `raceIDs`/`raceWeights`/`traitGroupIDs` 多值欄位）
- 資料完整性驗證：`strongTypeIDs`/`weakTypeIDs` 互斥、`baseProfessionID` 合法性、`tier≥2` 規則
- 所有 GDD §3.4 定義的查詢 API：`GetProfession`、`GetAllProfessions`、`GetBaseProfessions`、`IsStrongType`、`IsWeakType`、`GetUpgradePaths`、`GetBaseProfession`
- `IProfessionService` 介面定義，供下游系統注入使用

**Out-of-Scope：**
- 成功率修正計算（由 FT-02 Mission Dispatch 執行）
- 種族加權抽取邏輯（由 C-04 Race System 執行）
- 特質群組抽取邏輯（由 C-05 Trait System 執行）
- 職業升級觸發（Game Jam 版本不啟用，欄位預留）
- 任何 UI 顯示邏輯（由 P-02 消費 `GetProfession(professionID).name`）

### 1.3 完成目標（Definition of Done）

| ID | 條件 | 驗證方式 |
| --- | --- | --- |
| DoD-01 | `GetAllProfessions()` 回傳 7 筆（Game Jam 初始資料） | EditMode test：Assert.AreEqual(7, service.GetAllProfessions().Count) |
| DoD-02 | `GetBaseProfessions()` 僅回傳 `tier=1` 職業，共 7 筆 | EditMode test：驗證每筆 `tier == 1`，count == 7 |
| DoD-03 | `IsStrongType(1, 1)` 回傳 `true`；`IsWeakType(1, 4)` 回傳 `true` | EditMode test：對應 GDD AC-PS-03 / AC-PS-04 |
| DoD-04 | `IsStrongType(7, 任意typeID)` 全回傳 `false`；`IsWeakType(7, 任意typeID)` 全回傳 `false` | EditMode test：傭兵（ID=7）測試 typeID=1,2,3,4 |
| DoD-05 | `IsStrongType(4, 2)` 與 `IsStrongType(4, 4)` 皆回傳 `true`（斥侯多重擅長） | EditMode test：對應 GDD AC-PS-07 |
| DoD-06 | 載入含 `strongTypeIDs`/`weakTypeIDs` 相同 typeID 的 CSV 行時，`Debug.LogError` 且該職業不被載入 | EditMode test：注入違規 CSV，驗證字典不含該 professionID |
| DoD-07 | `strongTypeIDs`/`weakTypeIDs` 含不存在 MissionTypeTable 的 typeID 時，`Debug.LogWarning`，過濾該 typeID，其餘正常載入 | EditMode test |
| DoD-08 | `GetProfession(0)` 回傳 `null` 並輸出 `Debug.LogWarning` | EditMode test |
| DoD-09 | `GetUpgradePaths(任意ID)` 在 Game Jam 版本（全 tier=1）回傳空列表，不報錯 | EditMode test |
| DoD-10 | CSV 零硬編碼：`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY` 來自 `SystemConstants.csv` | 程式碼 review：無魔術數字 0.20 / 0.15 |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 內容摘要 |
| --- | --- |
| C-03 §1 概要 | 系統定位：純靜態查詢，F-01 DataManager 之上 |
| C-03 §3.1 | ProfessionTable 欄位定義（含多值欄位、null sentinel 規範） |
| C-03 §3.2 | 擅長/弱點規則（互斥、過濾 `0`、修正由 FT-02 套用） |
| C-03 §3.3 | 升級結構規則（tier 驗證、`baseProfessionID` 合法性） |
| C-03 §3.4 | 查詢 API 簽名與語義 |
| C-03 §4.1 | 成功率修正公式（規格定義；實作在 FT-02） |
| C-03 §5.1 | 載入階段邊緣案例（6 條） |
| C-03 §5.2 | 查詢階段邊緣案例（3 條） |
| C-03 §6 | 依賴關係（上游：F-01、C-01；下游：C-02、C-04、C-05、FT-01、FT-02） |
| C-03 §7 | 可調參數（`STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY` 來自 SystemConstants） |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-03-DS】profession-table.md` | `ProfessionTable.csv` | 全部欄位（`professionID`、`name`、`description`、`strongTypeIDs`、`weakTypeIDs`、`tier`、`baseProfessionID`、`raceIDs`、`raceWeights`、`traitGroupIDs`） | 職業靜態資料載入與查詢 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY` | 成功率修正常數（由 FT-02 使用；C-03 僅作為規格定義來源） |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 介面 / 呼叫方式 |
| --- | --- | --- |
| F-01 DataManager | 載入並解析 `ProfessionTable.csv`，提供泛型 `GetAll<ProfessionData>()` | `IDataManager.GetAll<ProfessionData>()` |
| C-01 Mission Database | 提供合法 typeID 集合，供載入時驗證 `strongTypeIDs`/`weakTypeIDs` | `IMissionDatabaseService.GetAllMissionTypeIds()` 回傳 `IReadOnlyList<int>` |

> 載入順序：F-01 DataManager 完成 → C-01 Mission Database 完成 → C-03 Profession System 執行 `Initialize()`。

### 2.4 下游被依賴系統

| 系統 | 依賴內容 |
| --- | --- |
| C-02 Adventurer Management | `GetProfession(professionID)` 取職業名稱、描述用於 UI |
| C-04 Race System | `GetProfession(professionID).RaceIDs` / `.RaceWeights` 讀取種族隨機池 |
| C-05 Trait System | `GetProfession(professionID).TraitGroupIDs` 讀取特質群組列表 |
| FT-01 Recruitment | `GetBaseProfessions()` 取 `tier=1` 職業列表作為招募池 |
| FT-02 Mission Dispatch | `IsStrongType(professionID, typeID)`、`IsWeakType(professionID, typeID)` 套用成功率修正 |
| FT-04 Outcome Resolution | `GetProfession(professionID)` 取職業資訊用於結算事件（間接，由 C-02 職業資料隨冒險者實例傳遞） |

### 2.5 跨系統事件契約

C-03 Profession System 不發布任何事件，亦不訂閱任何事件。系統為純同步查詢模式；初始化完成後由 Bootstrap 序列保證上下游順序，無需事件通知。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家在招募或派遣任務時，三秒內即可從職業名稱判斷「這個冒險者能做什麼、不能做什麼」。當玩家發現「法師派去採集全軍覆沒」，那個「原來如此」的頓悟感是職業系統的核心報酬。職業是角色個性的第一層暗示，不是冰冷的數值標籤。

### 3.2 系統目的還原

C-03 以 `ProfessionTable.csv` 為單一資料來源，定義 7 種職業的擅長/弱點類型、升級階層，以及種族池和特質群組欄位（供 C-04/C-05 消費）。系統設計為完全資料驅動的靜態查詢服務，提供 `IProfessionService` 介面供所有下游系統以依賴注入方式呼叫，自身不持有任何 runtime 狀態。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 三秒識別職業定位 | UI 顯示職業名稱與描述；不同職業在委託板上有不同的勝率預覽 | `GetProfession(professionID).Name` / `.Description` 提供顯示字串；`IsStrongType`/`IsWeakType` 支援 FT-02 即時計算勝率修正 |
| 擅長加成帶來頓悟感 | 派錯職業成功率明顯下降，派對職業成功率明顯上升 | `IsStrongType`/`IsWeakType` O(1) `HashSet` 查詢；修正常數由 `SystemConstants.csv` 的 `STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY` 驅動 |
| 傭兵作為無修正基準線 | 傭兵任何任務成功率不加不減，方便玩家感受修正幅度 | `strongTypeIDs=0`/`weakTypeIDs=0` 解析後為空 `HashSet`，`IsStrongType`/`IsWeakType` 恆回傳 `false` |
| 完全資料驅動，設計師可自由調整 | 新增/修改職業不需重新編譯，只改 CSV 即生效 | `ProfessionDatabaseLoader` 解析 CSV 後填入字典，`ProfessionService` 讀取字典提供查詢；職業數量與欄位值全來自 CSV |
| 種族池/特質群組供生成多樣性 | 同職業冒險者會有不同種族與特質組合 | `ProfessionData.RaceIDs`/`.RaceWeights`/`.TraitGroupIDs` 欄位由 C-04/C-05 讀取後執行加權抽取 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。C-03 職責單一（靜態資料查詢），GDD 未定義多個獨立職責分區，預估總規模遠低於 500 行，規劃為 1 份 FSD 對應 4 個 Script。

### 4.2 拆分理由

不適用（未拆分 FSD）。Script 層面依 SRP 原則切分：資料結構、載入驗證、介面定義、服務實作各一檔，符合「3~8 個 Script」的經驗值。

### 4.3 拆分結果

不適用（未拆分 FSD）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `ProfessionData.cs` | `Assets/Scripts/Gameplay/Profession/ProfessionData.cs` | 儲存單一職業的所有靜態欄位（純資料物件，無方法） | — | < 40 行 |
| `IProfessionService.cs` | `Assets/Scripts/Gameplay/Profession/IProfessionService.cs` | 定義 C-03 所有對外查詢 API 的介面契約 | — | < 40 行 |
| `ProfessionDatabaseLoader.cs` | `Assets/Scripts/Gameplay/Profession/ProfessionDatabaseLoader.cs` | 從 DataManager 取得 `ProfessionData` 列表、執行資料驗證（互斥、tier、baseProfessionID）、構建查詢字典 | `IDataManager`、`IMissionDatabaseService` | 120~170 行 |
| `ProfessionService.cs` | `Assets/Scripts/Gameplay/Profession/ProfessionService.cs` | 實作 `IProfessionService`，持有 `ProfessionDatabaseLoader` 構建的字典，提供所有查詢方法 | `IProfessionService`（實作）、`ProfessionDatabaseLoader` | 100~140 行 |

### 4.5 類別關係（可選）

```
ProfessionDatabaseLoader
  ├─ 依賴 IDataManager（F-01）
  ├─ 依賴 IMissionDatabaseService（C-01，typeID 驗證）
  └─ 構建 Dictionary<int, ProfessionData>
         │
         ▼
ProfessionService : IProfessionService
  └─ 持有 Dictionary<int, ProfessionData>（由 Loader 注入）
         │
         ├─ C-02 AdventurerManagement（消費）
         ├─ C-04 RaceSystem（消費 raceIDs/raceWeights）
         ├─ C-05 TraitSystem（消費 traitGroupIDs）
         ├─ FT-01 Recruitment（消費 GetBaseProfessions）
         └─ FT-02 MissionDispatch（消費 IsStrongType/IsWeakType）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

以下方法定義於 `IProfessionService`，由 `ProfessionService` 實作：

| 方法簽名 | 回傳 | 說明 |
| --- | --- | --- |
| `GetProfession(int professionID) : ProfessionData` | `ProfessionData` 或 `null` | 依 professionID 查詢；`professionID=0` 回傳 `null` 並 `Debug.LogWarning` |
| `GetAllProfessions() : IReadOnlyList<ProfessionData>` | 所有已載入職業 | 含所有 tier |
| `GetBaseProfessions() : IReadOnlyList<ProfessionData>` | `tier=1` 的職業清單 | 供招募池使用 |
| `IsStrongType(int professionID, int typeID) : bool` | `bool` | 職業不存在時回傳 `false` 並 `Debug.LogWarning` |
| `IsWeakType(int professionID, int typeID) : bool` | `bool` | 同上 |
| `GetUpgradePaths(int professionID) : IReadOnlyList<ProfessionData>` | 以此職業為 `baseProfessionID` 的升級職業清單 | 無升級路線時回傳空列表 |
| `GetBaseProfession(int professionID) : ProfessionData` | `ProfessionData` 或 `null` | `tier=1` 職業回傳 `null` |

以下方法定義於 `ProfessionDatabaseLoader`，供初始化序列呼叫（非 `IProfessionService` 部分）：

| 方法簽名 | 說明 |
| --- | --- |
| `Initialize(IDataManager dataManager, IMissionDatabaseService missionDb) : void` | 執行載入與驗證；呼叫前 F-01 + C-01 必須已完成初始化 |
| `GetLoadedDictionary() : IReadOnlyDictionary<int, ProfessionData>` | 供 `ProfessionService` 建構時注入 |

### 5.2 事件清單

無。C-03 不發布、不訂閱任何 EventBus 事件。

### 5.3 資料結構

#### `ProfessionData`（純資料物件）

```
ProfessionData
  int ProfessionId          // PK；從 1 起
  string Name               // 職業名稱
  string Description        // 職業定位描述
  HashSet<int> StrongTypeIds  // 擅長任務 typeID（DataManager 解析後已過濾 0）
  HashSet<int> WeakTypeIds    // 弱點任務 typeID（同上）
  int Tier                  // 職業階層（Game Jam 全為 1）
  int BaseProfessionId      // tier=1 為 0（null sentinel）；tier≥2 為來源 professionID
  int[] RaceIds             // 種族隨機池 ID 陣列（供 C-04 消費）
  int[] RaceWeights         // 對應種族權重陣列（長度 == RaceIds.Length）
  int[] TraitGroupIds       // 特質群組 ID 陣列（供 C-05 消費）
```

> `StrongTypeIds`/`WeakTypeIds` 使用 `HashSet<int>` 以支援 O(1) `IsStrongType`/`IsWeakType` 查詢，由 `ProfessionDatabaseLoader` 在載入時轉換。

### 5.4 內部資料流

**觸發點 1：Bootstrap 呼叫 `ProfessionDatabaseLoader.Initialize()`（系統初始化）**

```
Bootstrap.Initialize()（F-01 + C-01 完成後執行）
  → ProfessionDatabaseLoader.Initialize(dataManager, missionDb)
      ├─ 步驟 1：dataManager.GetAll<ProfessionData>() 取得原始列表
      ├─ 步驟 2：missionDb.GetAllMissionTypeIds() 取得合法 typeID 集合（validTypeIds）
      ├─ 步驟 3：對每筆 rawData 執行驗證：
      │   ├─ if (strongTypeIds ∩ weakTypeIds ≠ ∅)
      │   │     → Debug.LogError($"professionID={id} strongTypeIDs 與 weakTypeIDs 有相同 typeID")
      │   │     → 跳過（不加入字典）
      │   ├─ if (tier >= 2 && baseProfessionId == 0)
      │   │     → Debug.LogError($"professionID={id} tier≥2 但 baseProfessionID=0")
      │   │     → 跳過
      │   ├─ if (tier >= 2 && !dict.ContainsKey(baseProfessionId))
      │   │     → Debug.LogError($"professionID={id} baseProfessionID={baseProfessionId} 不存在")
      │   │     → 跳過
      │   ├─ 過濾 strongTypeIds 中不在 validTypeIds 的 id（記 LogWarning）
      │   ├─ 過濾 weakTypeIds 中不在 validTypeIds 的 id（記 LogWarning）
      │   └─ 將驗證後的 ProfessionData 加入 _dict[professionId]
      └─ 步驟 4：以 _dict 值構建 _baseProfessionsList（filter tier==1）
```

**觸發點 2：下游呼叫 `IsStrongType(professionID, typeID)`（熱路徑，每次 Dispatch 呼叫）**

```
FT-02 MissionDispatch.CalculateSuccessRate(professionId, typeId)
  → IProfessionService.IsStrongType(professionId, typeId)
      ├─ if (professionId == 0 || !_dict.ContainsKey(professionId))
      │     → Debug.LogWarning; return false
      └─ return _dict[professionId].StrongTypeIds.Contains(typeId)  // O(1)
```

**觸發點 3：下游呼叫 `GetProfession(professionID)`（C-04/C-05 生成冒險者時）**

```
C-04 RaceSystem.DrawRace(professionId)
  → IProfessionService.GetProfession(professionId)
      ├─ if (professionId == 0)
      │     → Debug.LogWarning("0 為 null sentinel"); return null
      ├─ _dict.TryGetValue(professionId, out var data)
      │     → 找到：return data
      └─     → 找不到：return null（呼叫方負責 null 檢查）
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `ProfessionTable.csv` | `professionID`, `name`, `description`, `strongTypeIDs`, `weakTypeIDs`, `tier`, `baseProfessionID`, `raceIDs`, `raceWeights`, `traitGroupIDs` | `【C-03-DS】profession-table.md` | 職業靜態資料完整定義 | Bootstrap 初始化序列，F-01 + C-01 完成後 |
| `SystemConstants.csv` | `STRONG_TYPE_BONUS`, `WEAK_TYPE_PENALTY` | `【F-01-DS】system-constants.md` | 成功率修正常數（由 FT-02 讀取使用；C-03 §4.1 作為規格定義） | F-01 DataManager 初始化時（最優先） |

### 6.2 引用的 ScriptableObject

無。C-03 全部參數來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `STRONG_TYPE_BONUS`（+0.20） | `SystemConstants.csv` → key `STRONG_TYPE_BONUS` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `WEAK_TYPE_PENALTY`（-0.15） | `SystemConstants.csv` → key `WEAK_TYPE_PENALTY` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 職業初始資料 7 筆 | `ProfessionTable.csv` 全行 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `raceIDs`/`raceWeights`/`traitGroupIDs` 欄位值 | `ProfessionTable.csv` | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

### 載入階段（GDD §5.1）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `strongTypeIDs` 與 `weakTypeIDs` 有相同 typeID | `Debug.LogError` 記錄 `professionID`，跳過該職業（不加入字典） | `ProfessionDatabaseLoader` | EditMode test：注入含衝突 typeID 的 CSV，Assert 字典不含該 ID |
| `strongTypeIDs` 或 `weakTypeIDs` 包含不存在於 MissionTypeTable 的 typeID（非 0） | `Debug.LogWarning` 記錄 `professionID` 與違規 typeID，過濾該 typeID，其餘欄位正常載入；職業仍加入字典 | `ProfessionDatabaseLoader` | EditMode test：注入含無效 typeID 的 CSV，Assert 欄位已過濾，職業存在於字典 |
| `tier≥2` 的職業 `baseProfessionID = 0` | `Debug.LogError`，跳過該職業 | `ProfessionDatabaseLoader` | EditMode test：注入 tier=2 且 baseProfessionID=0 的行，Assert 字典不含該 ID |
| `baseProfessionID` 指向不存在的 professionID | `Debug.LogError`，跳過該職業 | `ProfessionDatabaseLoader` | EditMode test：注入指向 999 的行，Assert 字典不含該 ID |
| 多值欄位僅含 `0`（如 `strongTypeIDs = "0"`） | DataManager 解析後 `0` 被過濾，轉為空 `HashSet<int>`；`IsStrongType` 恆回傳 `false` | `ProfessionDatabaseLoader`、`ProfessionService` | EditMode test：傭兵（ID=7）測試 IsStrongType 恆 false |
| 同一 `professionID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為，Loader 不額外處理） | `ProfessionDatabaseLoader`（被動接受 DataManager 結果） | 依賴 F-01 DataManager 既有 EditMode test 覆蓋 |

### 查詢階段（GDD §5.2）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `GetProfession(0)` | 回傳 `null`，`Debug.LogWarning`（0 為 null sentinel） | `ProfessionService` | EditMode test：Assert.IsNull(service.GetProfession(0)) + 檢查 LogWarning |
| `GetUpgradePaths(professionID)` 無任何職業以此為 base | 回傳空 `List<ProfessionData>`，不報錯 | `ProfessionService` | EditMode test：對任意 tier=1 職業呼叫，Assert.AreEqual(0, result.Count) |
| `IsStrongType` / `IsWeakType` 傳入不存在的 professionID | 回傳 `false`，`Debug.LogWarning` 記錄 professionID | `ProfessionService` | EditMode test：傳入 professionID=999，Assert false + 檢查 LogWarning |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 ProfessionTable 欄位定義（含多值欄位、null sentinel） | §5.3 ProfessionData 結構；§6.1 CSV 引用表 | 對齊 | `HashSet<int>` 轉換在 Loader 執行；null sentinel 0 由 DataManager 過濾 |
| §3.2 擅長/弱點規則（互斥、過濾 0、修正由 FT-02 套用） | §5.1 API；§7 邊緣案例 | 對齊 | `IsStrongType`/`IsWeakType` O(1)；互斥驗證在 Loader；修正計算不在 C-03 範圍 |
| §3.3 升級結構規則（tier 驗證、baseProfessionID 合法性） | §5.4 資料流（步驟 3）；§7 邊緣案例 | 對齊 | Loader 驗證 tier≥2 與 baseProfessionID 合法性；Game Jam 版本全 tier=1 |
| §3.4 查詢 API 簽名 | §5.1 公開 API | 對齊 | 7 個 API 全覆蓋，簽名與 GDD 一致 |

### 8.2 公式對齊或替代說明

GDD §4.1 `ApplyProfessionModifier` 公式在 C-03 FSD 中僅作規格定義，實際計算由 FT-02 Mission Dispatch 實作。`IsStrongType`/`IsWeakType` 回傳 `bool` 供 FT-02 分支判斷，等價於 GDD §4.1 的 if 條件。

GDD §4.2 多重擅長案例（斥侯）：`IsStrongType(4, 2)` 與 `IsStrongType(4, 4)` 各自獨立呼叫，FT-02 每次任務只套用一個修正（符合 GDD §4.2 備註「每個任務只套用一個修正」）；C-03 保證同一 typeID 不可能同時為擅長與弱點（§3.2.3），邏輯一致。

### 8.3 未能實現的規則與修改建議

無。C-03 所有 GDD 規則皆可在本 FSD 規劃的 4 個 Script 中完整實現。

建議項：
- **B-01（已解決，2026-04-27）**：原建議項描述「`raceWeights` 與 `raceIDs` 長度驗證未在 GDD §5 列為邊緣案例」。**已由 GDD P-003 patch 解決**：C-03 GDD §3.1 已新增「跨欄位長度驗證」note 明文「載入時驗證歸 C-03 Loader 負責」，§5.1 邊緣案例新增一行「raceWeights 長度不一致 → LogError + 跳過該職業」。本 FSD §4.4 `ProfessionDatabaseLoader` 在 `Initialize()` 對齊此驗證；C-04 FSD `RaceService.RollRace` 走 `profession == null` fallback 路徑，不重複驗證（與 C-04 FSD §8.4 patch 對齊）。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 無 | — | — | — |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 無 | — | — | — |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：每條皆可被 EditMode 測試或手動步驟驗證（DoD-01~DoD-10）
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉（§2.5 確認無事件）
- [x] §3.3 對映表：每個玩家幻想／系統目的至少一個對應的技術手段
- [x] §4.1~§4.4：拆分判斷有結論（否）；Script 清單欄位齊全（路徑、SRP、依賴介面、預估規模）
- [x] §5.1~§5.4：API 簽名（7 個）、事件清單（無）、資料結構（ProfessionData）、資料流偽碼（3 個觸發點）齊備
- [x] §6.1~§6.3：引用 CSV 表含對應 Data-Specs；嚴禁寫死清單對齊實作原則第 9 條
- [x] §7：GDD §5 每條邊緣案例皆有對策（§5.1 六條 + §5.2 三條，共 9 條全覆蓋）
- [x] §8.1：對齊清單覆蓋 GDD §3 每個小節（§3.1~§3.4 四節全覆蓋）
- [x] §8.2~§8.5：公式對齊／無法實現項／GDD 回註／衝突紀錄如實登記
- [x] FSD-index：§6.1 / §7.1 / §7.2 同步更新（見 FSD-index 更新）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過 | 無衝突；B-01 建議項（raceWeights 長度驗證責任歸屬）不阻礙實作；§8.3 已登記 |
| 2026-04-27 | Claude Code 主體（GDD P-003 對齊 patch） | 通過 | 通過 | 通過 | B-01 標「已解決」：GDD P-003 patch 已將 raceWeights 長度驗證歸屬於 C-03 Loader（§3.1 跨欄位驗證 note + §5.1 EC 行）；本 FSD §4.4 `ProfessionDatabaseLoader.Initialize()` 對齊；C-04 FSD §8.4 已同步更新（不再寫「由 C-04 RollRace 負責」） |
