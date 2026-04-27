# 【C-04-FSD】功能規格說明書 — Race System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-04】race-system.md`（版本：2026-04-20） |
| 對應 Data-Specs | `【C-04-DS】race-table.md` |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（自檢） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-04 Race System 是 Core 層的靜態資料查詢系統，負責：(1) 從 F-01 DataManager 載入並快取 `RaceTable.csv`；(2) 暴露種族查詢 API（`GetRace`、`GetAllRaces`、`GetSuccessDelta`、`GetDeathDelta`）；(3) 實作加權隨機抽種族演算法（`RollRace`），透過 `IProfessionService` 讀取 `ProfessionTable` 中已合併的 `raceIDs` / `raceWeights` 欄位。系統不持有任何 runtime 狀態，亦不感知冒險者實例，修正值套用由 FT-02 負責。

### 1.2 In-Scope / Out-of-Scope

**In-Scope：**
- `RaceTable.csv` 載入、解析（含 JSON `modifiers` 欄位）、快取
- `modifiers` 解析後建立 `Dictionary<int, RaceModifierEntry>` 供 O(1) 查詢
- 公開 `IRaceService` 介面定義
- `GetRace`、`GetAllRaces`、`GetSuccessDelta`、`GetDeathDelta` 四支 API 實作
- `RollRace(professionID)` 加權隨機演算法（含安全範圍驗證與 fallback）
- 所有邊緣案例對策（格式錯誤、長度不一致、不存在 raceID 過濾）

**Out-of-Scope：**
- 修正值的套用（由 FT-02 Mission Dispatch 負責）
- `ProfessionTable` 中 `raceIDs` / `raceWeights` 欄位的 owner 與 schema（屬 C-03）
- 冒險者實例管理（屬 C-02）
- UI 顯示邏輯（屬 P-02）
- `ProfessionRacePool.csv`（已廢棄，不存在）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，補充可程式驗證條件：

| ID | 驗收條件 | 驗證方式 |
| --- | --- | --- |
| AC-RS-01 | DataManager 初始化後，`GetAllRaces()` 回傳 4 筆資料 | EditMode 測試：載入 CSV，斷言 Count == 4 |
| AC-RS-02 | `GetRace(1).name` == `"人類"` | EditMode 測試 |
| AC-RS-03 | `GetSuccessDelta(1, 3)` == `0.08f` | EditMode 測試 |
| AC-RS-04 | `GetDeathDelta(1, 2)` == `-0.08f` | EditMode 測試 |
| AC-RS-05 | `GetSuccessDelta(1, 1)` == `0.0f`（人類對討伐無修正） | EditMode 測試 |
| AC-RS-06 | `GetSuccessDelta(2, 4)` == `0.10f`（精靈調查） | EditMode 測試 |
| AC-RS-07 | `GetDeathDelta(4, 2)` == `0.05f`（魔族護送，正值） | EditMode 測試 |
| AC-RS-08 | `GetRace(0)` 回傳 `null` 且出現 `LogWarning` | EditMode 測試（LogAssert） |
| AC-RS-09 | `modifiers` JSON 格式錯誤時啟動出現 `LogError`，`GetSuccessDelta` 回傳 `0.0f` | EditMode 測試（LogAssert） |
| AC-RS-10 | `RollRace(7)` 呼叫 1000 次，raceID=1 約 50%、raceID=3 約 30%、raceID=4 約 20%（±5%） | PlayMode 統計測試 |
| AC-RS-11 | `RollRace` 傳入不存在 professionID，回傳 `1` 且出現 `LogError` | EditMode 測試（LogAssert） |
| AC-RS-12 | `raceIDs` 與 `raceWeights` 長度不一致時出現 `LogError`，`RollRace` 回傳 `1` | EditMode 測試（LogAssert） |
| AC-RS-13 | 新增一筆 CSV 行，`GetAllRaces()` 回傳新數量，修正值查詢正確 | 手動：替換 CSV → play mode 驗證 |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要：系統定位、`ProfessionRacePool` 廢棄說明
- §3.1：`RaceTable` 欄位定義（`raceID`、`name`、`description`、`modifiers` JSON 格式）
- §3.2：`raceIDs` / `raceWeights` 消費端規範（owner = C-03 `ProfessionTable`）
- §3.3：查詢 API 簽名
- §4.1：`RollRace` 加權隨機演算法
- §4.2：`modifiers` JSON 解析（JsonUtility wrapper DTO 注意事項）
- §4.3：修正值套用公式（規格定義，套用由 FT-02 執行）
- §5：全部邊緣案例（§5.1 載入、§5.2 查詢）
- §6.1~§6.3：依賴關係與循環依賴說明
- §7.1~§7.2：可調參數安全範圍
- §8：驗收標準

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-04-DS】race-table.md` | `RaceTable.csv` | `raceID`、`name`、`description`、`modifiers` | 種族靜態資料載入，為 C-04 owner |
| `【C-03-DS】profession-table.md` | `ProfessionTable.csv` | `raceIDs`、`raceWeights` | `RollRace` 時透過 `IProfessionService` 讀取（owner = C-03，C-04 為消費端） |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 介面 |
| --- | --- | --- |
| F-01 DataManager | 載入並快取 `RaceTable.csv` | `IDataManager.GetAll<RaceData>()` |
| C-03 Profession System | 取 `raceIDs` / `raceWeights` 執行 `RollRace`；驗證 professionID 存在 | `IProfessionService.GetProfession(int professionID)` |
| C-01 Mission Database | 提供合法 `typeID` 集合，供 `modifiers` 載入時驗證 | `IMissionDatabaseService.GetAllMissionTypes()` |

### 2.4 下游被依賴系統

| 系統 | 依賴內容 | 使用介面 |
| --- | --- | --- |
| C-02 Adventurer Management | 冒險者生成時（`raceID == 0`）呼叫加權抽種族 | `IRaceService.RollRace(int professionID)` |
| FT-01 Adventurer Recruitment | 生成冒險者時取得 `raceID` | `IRaceService.RollRace(int professionID)` |
| FT-02 Mission Dispatch | 取種族成功率與死亡率修正 | `IRaceService.GetSuccessDelta(int raceID, int typeID)`、`IRaceService.GetDeathDelta(int raceID, int typeID)` |
| FT-04 Outcome Resolution | 間接透過 FT-02 消費修正值（無直接 API 呼叫） | — |
| P-02 Main UI | 顯示冒險者種族名稱 | `IRaceService.GetRace(int raceID)` |

### 2.5 跨系統事件契約

C-04 為純靜態查詢系統，不訂閱任何事件，亦不發布任何事件。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

種族是冒險者的第二層身份印記。精靈的敏銳、獸人的蠻勇、魔族的神秘——玩家在看到種族名稱的瞬間已有具體感受。這個感受不靠教學灌輸，而是玩家在派遣任務的過程中，自然發現「精靈調查死亡率特別低」「獸人去護送比想像頑強」，從而形成策略判斷，讓名冊的組合開始有了超越職業配對的第二層意義。

### 3.2 系統目的還原

提供種族靜態資料服務：以 `RaceTable.csv` 為單一資料來源，快取解析後的修正值並暴露 O(1) 查詢 API，同時實作職業 → 種族的加權隨機抽取演算法，讓上層系統（冒險者生成、任務成功率計算）可以透過介面取得結果，無須了解資料結構細節。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 種族帶來策略層次 | 不同種族在特定任務類型的成功率與死亡率有差異 | `RaceTable.modifiers` JSON 欄位解析為 `Dictionary<int, RaceModifierEntry>`；`GetSuccessDelta` / `GetDeathDelta` O(1) 查詢後由 FT-02 套用 |
| 種族與職業有視覺關聯（精靈多出現在法師） | 同職業冒險者種族分布符合設計意圖 | `RollRace` 讀取 `ProfessionTable.raceIDs / raceWeights`，執行加權隨機抽取（累積區間演算法） |
| 玩家自行探索種族加成 | 無強制提示；玩家通過任務結算結果推算規律 | C-04 只提供原始 delta 值，不寫死任何 tooltip 文字；UI 顯示由 P-02 自行決定呈現方式 |
| 資料可隨設計需求調整 | 設計師改 CSV 即改種族加成，不需重新編譯 | `RaceTable.csv` 全 data-driven；`RollRace` 讀取 `ProfessionTable.csv` 中的 `raceWeights`，同樣不需改程式 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。C-04 職責範疇窄且單一（靜態資料載入 + 修正值查詢 + 加權抽取）；預估 4 個 Script 總行數遠低於 500 行門檻，不觸發拆分條件。

### 4.2 拆分理由

不適用（未拆分）。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `RaceData` | `Assets/Scripts/Gameplay/Race/RaceData.cs` | 單一種族的靜態資料容器（含 `modifiers` 快取 Dict） | — | `< 60 行` |
| `IRaceService` | `Assets/Scripts/Gameplay/Race/IRaceService.cs` | 定義 C-04 對外公開的查詢與抽取 API 契約 | — | `< 30 行` |
| `RaceDatabaseLoader` | `Assets/Scripts/Gameplay/Race/RaceDatabaseLoader.cs` | 從 DataManager 載入 `RaceTable.csv`，解析 `modifiers` JSON，建立快取 | `IDataManager`、`IMissionDatabaseService` | `100~150 行` |
| `RaceService` | `Assets/Scripts/Gameplay/Race/RaceService.cs` | 實作 `IRaceService`：查詢 API + `RollRace` 加權抽取 | `IRaceService`（自身）、`IProfessionService` | `120~160 行` |

### 4.5 類別關係（可選）

```
RaceDatabaseLoader
    ├─ 依賴 IDataManager（載入 RaceTable）
    ├─ 依賴 IMissionDatabaseService（驗證 typeID）
    └─ 產出 Dictionary<int, RaceData> → 注入 RaceService

RaceService : IRaceService
    ├─ 持有 Dictionary<int, RaceData>（由 RaceDatabaseLoader 注入）
    ├─ 依賴 IProfessionService（RollRace 取 raceIDs / raceWeights）
    └─ 暴露 IRaceService 給 C-02 / FT-01 / FT-02 / P-02

RaceData
    ├─ 欄位：raceID, name, description
    └─ 欄位：_modifierCache : Dictionary<int, RaceModifierEntry>（載入後建立）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// IRaceService
RaceData GetRace(int raceID);
IReadOnlyList<RaceData> GetAllRaces();
float GetSuccessDelta(int raceID, int typeID);
float GetDeathDelta(int raceID, int typeID);
int RollRace(int professionID);
```

| 方法 | 回傳 | 說明 |
| --- | --- | --- |
| `GetRace(int raceID)` | `RaceData`（`null` 表示不存在） | raceID=0 回傳 `null` 並 LogWarning |
| `GetAllRaces()` | `IReadOnlyList<RaceData>` | 全種族列表，順序與 CSV 行序一致 |
| `GetSuccessDelta(int raceID, int typeID)` | `float` | 不存在的 raceID → `0.0f` + LogWarning；不存在的 typeID → `0.0f`，不報錯 |
| `GetDeathDelta(int raceID, int typeID)` | `float` | 同上 |
| `RollRace(int professionID)` | `int`（raceID） | 失敗時回傳 fallback `1`（人類）並 LogError |

### 5.2 事件清單

C-04 不發布也不訂閱任何事件。

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| — | — | — | — |

### 5.3 資料結構

```csharp
// RaceData（資料容器）
public class RaceData
{
    public int raceID;
    public string name;
    public string description;
    // 載入後由 RaceDatabaseLoader 建立快取，外部不直接存取
    // _modifierCache : Dictionary<int, RaceModifierEntry>
}

// RaceModifierEntry（modifiers JSON 解析結果，單一條目）
public class RaceModifierEntry
{
    public int typeID;
    public float successDelta;
    public float deathDelta;
}

// JsonUtility wrapper DTO（解析 top-level JSON array 用）
[Serializable]
internal class RaceModifierListWrapper
{
    public List<RaceModifierEntry> modifiers;
}
```

**注意**：Unity `JsonUtility` 不支援直接解析 top-level JSON array。`RaceDatabaseLoader` 使用 `RaceModifierListWrapper` 包裝解析；若替換為 Newtonsoft.Json 則不需 wrapper，實作時二擇一並於 §8.2 登記。

### 5.4 內部資料流

**觸發點 1：系統初始化（由 Bootstrap / DataManager 呼叫）**

```
RaceDatabaseLoader.Load()
  → IDataManager.GetAll<RaceData>() : List<RaceData>
      ├─ 步驟 1：for each RaceData row
      │     ├─ if modifiers 欄位為空或 "[]" → _modifierCache = {}，繼續
      │     ├─ 嘗試 JSON 解析（JsonUtility + wrapper DTO）
      │     │   ├─ 解析失敗 → Debug.LogError(raceID)，_modifierCache = {}，繼續
      │     │   └─ 解析成功 → 建立 List<RaceModifierEntry>
      │     └─ for each entry：
      │           ├─ if typeID 不在 IMissionDatabaseService.GetAllMissionTypes() → Debug.LogWarning，過濾
      │           └─ 加入 _modifierCache[typeID] = entry
      ├─ 步驟 2：若同一 raceID 在 CSV 重複 → Debug.LogWarning，後者覆蓋（DataManager 標準行為）
      └─ 步驟 3：建立 Dictionary<int, RaceData>，注入 RaceService
```

**觸發點 2：FT-02 查詢修正值**

```
FT-02.CalculateSuccessRate(raceID, typeID)
  → IRaceService.GetSuccessDelta(raceID, typeID)
      ├─ if raceID == 0 或 raceID 不在快取 → Debug.LogWarning，return 0.0f
      └─ if typeID 不在 _modifierCache → return 0.0f（無修正，正常情境）
         else → return _modifierCache[typeID].successDelta
```

**觸發點 3：C-02 / FT-01 生成冒險者**

```
AdventurerFactory.Create(professionID)
  → IRaceService.RollRace(professionID)
      ├─ IProfessionService.GetProfession(professionID)
      │   ├─ 回傳 null → Debug.LogError，return 1（fallback 人類）
      │   └─ 取得 profession.raceIDs、profession.raceWeights
      ├─ if raceIDs 為空 → Debug.LogError，return 1
      ├─ if raceIDs.Length != raceWeights.Length → Debug.LogError，return 1
      ├─ 過濾 raceIDs 中不存在於 RaceTable 的 ID → Debug.LogWarning，排除對應 weight
      ├─ if 過濾後 raceIDs 為空 → Debug.LogError，return 1
      ├─ totalWeight = raceWeights.Sum()
      ├─ roll = Random.Range(0, totalWeight)  // [0, totalWeight)
      ├─ 累積區間掃描 → return 第一個 cumulative > roll 的 raceID
      └─ 防禦性 fallback：return raceIDs.Last()
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `RaceTable.csv` | `raceID`、`name`、`description`、`modifiers`（JSON） | `【C-04-DS】race-table.md` | 種族靜態資料（C-04 owner），載入後解析 modifiers 建立快取 | Bootstrap / DataManager 初始化時（一次性） |
| `ProfessionTable.csv` | `raceIDs`（pipe-separated int）、`raceWeights`（pipe-separated int） | `【C-03-DS】profession-table.md` | `RollRace` 時透過 `IProfessionService` 讀取（owner = C-03，C-04 為消費端） | 透過 `IProfessionService.GetProfession()` 按需讀取，不由 C-04 直接載入 |

### 6.2 引用的 ScriptableObject

無。C-04 為純靜態資料系統，所有調整來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 各種族 `successDelta` / `deathDelta` | `RaceTable.csv`.`modifiers` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 各職業 `raceIDs` / `raceWeights` 分布 | `ProfessionTable.csv`.`raceIDs` / `raceWeights` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| fallback raceID（`1`） | `RaceTable.csv` 第一筆合法 raceID（Game Jam 固定為 `1`） | 注意：Game Jam 範圍內 raceID=1 為人類的假設安全；若動態取第一筆，於 §8.3 登記 |

---

## 7. 邊緣案例對策（Edge Case Handling）

### GDD §5.1 載入邊緣案例

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `modifiers` JSON 欄位為空字串或 `"[]"` | 解析為空列表，`_modifierCache = {}`；`GetSuccessDelta` / `GetDeathDelta` 均回傳 `0.0f`；不報錯 | `RaceDatabaseLoader` | EditMode 測試：傳入空 modifiers 欄位，驗證查詢回傳 `0.0f` |
| `modifiers` JSON 格式錯誤（無法解析） | `Debug.LogError(raceID)`；該種族 `_modifierCache = {}`；其他種族正常 | `RaceDatabaseLoader` | EditMode 測試（LogAssert）：注入格式錯誤行，確認 LogError 且其他種族不受影響 |
| `modifiers` 中 `typeID` 不存在於 MissionTypeTable | `Debug.LogWarning(raceID, typeID)`；過濾該條目；其餘正常載入 | `RaceDatabaseLoader` | EditMode 測試（LogAssert）：注入非法 typeID，確認 LogWarning 且合法條目正常 |
| `raceIDs` 與 `raceWeights` 長度不一致（C-03 ProfessionTable） | 在 `RollRace` 內 `Debug.LogError(professionID)`；回傳 fallback `raceID=1` | `RaceService` | EditMode 測試（LogAssert）：mock IProfessionService 回傳長度不一致資料 |
| `raceIDs` 包含不存在於 RaceTable 的 raceID | `Debug.LogWarning`；過濾該 raceID 與對應 weight；其餘正常抽取 | `RaceService` | EditMode 測試：注入含無效 raceID 的職業資料，確認過濾後仍能正常抽取 |
| 過濾後 `raceIDs` 為空 | `Debug.LogError(professionID)`；`RollRace` 回傳 fallback `raceID=1` | `RaceService` | EditMode 測試（LogAssert） |
| 某職業在 C-03 ProfessionTable 中無對應行（`GetProfession == null`） | `Debug.LogError(professionID)`；`RollRace` 回傳 fallback `raceID=1` | `RaceService` | EditMode 測試（LogAssert）：mock GetProfession 回傳 null |
| 同一 `raceID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為，RaceDatabaseLoader 不額外處理） | `RaceDatabaseLoader` | EditMode 測試（LogAssert）：注入重複 raceID，確認最終為後者值 |

### GDD §5.2 查詢邊緣案例

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `GetRace(0)` | 回傳 `null`；`Debug.LogWarning("raceID 0 is null sentinel")` | `RaceService` | EditMode 測試（LogAssert） |
| `GetSuccessDelta` / `GetDeathDelta` 傳入不存在的 `raceID` | 回傳 `0.0f`；`Debug.LogWarning(raceID)` | `RaceService` | EditMode 測試（LogAssert） |
| `GetSuccessDelta` / `GetDeathDelta` 傳入不存在的 `typeID` | 回傳 `0.0f`；不報錯（該種族對此類型無修正，屬正常情況） | `RaceService` | EditMode 測試：確認無 Log 輸出，回傳 `0.0f` |
| `RollRace` 傳入不存在的 `professionID` | `Debug.LogError(professionID)`；回傳 fallback `raceID=1` | `RaceService` | EditMode 測試（LogAssert） |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 RaceTable 資料表定義（欄位型別、modifiers JSON 格式、4 種初始資料） | §5.3 資料結構、§6.1 CSV 表 | 對齊 | `modifiers` JSON 解析使用 wrapper DTO 規避 JsonUtility 限制 |
| §3.2 職業 → 種族隨機池（消費 C-03 ProfessionTable，不持有 ProfessionRacePool） | §2.3 上游依賴、§5.4 資料流觸發點 3、§6.1 CSV 表 | 對齊 | C-04 透過 `IProfessionService` 讀取，不直接存取 CSV |
| §3.3 查詢 API（5 支方法簽名與回傳規格） | §5.1 公開 API | 對齊 | — |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 實作方式 | 是否等價 | 備註 |
| --- | --- | --- | --- |
| §4.1 `RollRace` 加權隨機（累積區間演算法） | §5.4 觸發點 3 偽碼，逐一累積 weight 比對 roll | 等價 | GDD §4.1 演算法直接採用，無替代 |
| §4.2 `ParseModifiers` JSON 解析 + 快取 `Dictionary<int, RaceModifierEntry>` | `RaceDatabaseLoader.Load()` 步驟 1 | 等價 | Unity JsonUtility wrapper DTO 為 GDD §4.2 注意事項指定方案 |
| §4.3 `ApplyRaceModifier`（套用修正值）| **不在 C-04 實作**；由 FT-02 呼叫 `GetSuccessDelta` / `GetDeathDelta` 後自行套用 | 等價（C-04 提供原始 delta，FT-02 套用） | GDD §4.3 明確標示「由 FT-02 執行，此為規格定義」，C-04 符合此設計意圖 |

**JsonUtility vs Newtonsoft.Json 選擇：** 實作者可選用 Newtonsoft.Json（省略 wrapper DTO），需於 `RaceDatabaseLoader` 中統一採用，並確保 JSON 解析例外被 try-catch 包覆後 LogError。

### 8.3 未能實現的規則與修改建議

**B-01（已重新決議，2026-04-27 GDD P-003 patch）：`raceWeights` 長度驗證責任歸屬**

原 FSD 自決「由 `RaceService.RollRace` 負責」（C-04 執行）。**GDD P-003 patch 已將決議反轉為「由 C-03 Loader 統一處理」**：C-03 GDD §3.1 新增「跨欄位長度驗證」note，§5.1 邊緣案例新增「raceWeights 長度不一致 → LogError + 跳過該職業」一行。

更新後行為：
- C-03 Loader 載入時驗證並跳過違規職業，`GetProfession(professionID)` 對該職業回 `null`
- C-04 `RaceService.RollRace` 走 §5 偽碼的 `profession == null → return 1`（fallback 人類）路徑，不再走到 `raceIDs.Length != raceWeights.Length` 分支
- §5 邊緣案例表中對應行措辭已更新（line 313）；偽碼第 270 行的長度檢查保留為 **defensive 雙保險**，runtime 預期不會觸發

對齊狀態：本 FSD §8.4 GDD 回註已撤銷（不再寫「由 C-04 負責」）；C-03 FSD §8.3 B-01 已標「已解決」。

**B-02（已解決，2026-04-27 GDD P-004 patch）：fallback raceID=1 保留 ID 註記**

原建議項描述「GDD 硬寫 `return 1` 作為 fallback，假設 raceID=1 永遠為人類；建議 CSV 規範中備註保留」。**已由 GDD P-004 patch 解決**：C-04 GDD §3.1 Game Jam 初始資料表後已新增 note「`raceID = 1`（人類）為 fallback 保留 ID」，明文「禁止新增種族時重排或覆寫此 ID」。本 FSD §3.3 API 規格與 §5 邊緣案例 fallback 行為均保持指定值 `1`，已對齊。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-27 | `【C-04】race-system.md` | §3.2 | ~~FSD 確認：`raceIDs` / `raceWeights` 長度不一致驗證由 `RaceService.RollRace`（C-04）負責；C-03 Loader 不重複驗證~~ **已撤銷（2026-04-27 GDD P-003 patch）**：驗證歸屬反轉為「由 C-03 Loader 統一處理」，本回註不再寫入 GDD；C-04 GDD §5.1 對應行已自行更新為「驗證歸 C-03 Loader 處理；RollRace 走 profession == null fallback 路徑」 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | 無衝突 | — | — |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：每條皆可被 EditMode／PlayMode 測試或手動步驟驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：每個玩家幻想／系統目的至少一個對應的技術手段
- [x] §4.1~§4.4：拆分判斷有結論；Script 清單欄位齊全（含路徑、SRP、依賴介面、預估規模）
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、資料流偽碼齊備
- [x] §6.1~§6.3：引用 CSV 表含對應 Data-Specs；嚴禁寫死清單對齊實作原則第 9 條
- [x] §7：GDD §5 每條邊緣案例皆有對策（§5.1 共 8 條、§5.2 共 4 條，共 12 條全覆蓋）
- [x] §8.1 對齊清單覆蓋 GDD §3 每個小節（§3.1 / §3.2 / §3.3 三節全對齊）
- [x] §8.2~§8.5：公式對齊／無法實現項／GDD 回註／衝突紀錄如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（見本批次 Edit）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（自檢） | 通過 | 通過 | 通過 | 無衝突；建議項 B-01（raceWeights 驗證責任）已在本 FSD 確認由 C-04 負責，回報 C-03 FSD；B-02（fallback raceID=1）不阻礙實作 |
| 2026-04-27 | Claude Code 主體（GDD P-003 + P-004 對齊 patch） | 通過 | 通過 | 通過 | B-01 重新決議：GDD P-003 將 raceWeights 驗證歸屬反轉為 C-03 Loader（§3.1 / §5.1）；本 FSD §8.3 B-01 重寫、§8.4 GDD 回註撤銷。B-02 已解決：GDD P-004 已於 §3.1 新增 raceID=1 保留 ID note，本 FSD §3.3 / §5 fallback 行為對齊。§5 偽碼 `raceIDs.Length != raceWeights.Length` 檢查保留為 defensive 雙保險（runtime 預期不觸發） |
