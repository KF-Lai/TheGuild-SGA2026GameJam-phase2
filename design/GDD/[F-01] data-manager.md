aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa# DataManager 系統設計文件

_建立時間：2026-04-19_
_狀態：已完成_
_系統 ID：F-01_

---

## 1. 概要（Overview）

DataManager 是遊戲的資料基礎設施，以 Singleton MonoBehaviour 形式存在，在 `Awake` 階段將 `Assets/Resources/Data/Tables/` 目錄下的所有 CSV 檔案透過 `Resources.Load<TextAsset>` 全量解析並快取為記憶體字典（`Dictionary<string, T>`）。所有遊戲系統透過 `DataManager.Instance.Get<T>(id)` 以主鍵 ID 取得資料，確保每張表只被解析一次。DataManager 同時提供隨機池工具 `PickRandom<T>`，根據群組表（如 `TraitGroupTable`、`ProfessionRacePool`）的 `pickCount`、`pickMode` 與加權定義執行隨機抽樣，實現零硬編碼的資料驅動內容生成。

## 2. 玩家幻想（Player Fantasy）

DataManager 的目標受眾是設計師與開發者。設計師應能在不接觸任何 C# 程式碼的情況下，只透過修改 Google Sheets 並匯出 CSV，完成新冒險者、新任務、新 Trait 的新增或數值調整，並在下次遊戲啟動時立即看到效果。開發者在實作新系統時，只需要定義對應的資料類別（`[System.Serializable] class XxxData`）並呼叫 `DataManager.Instance.Get<XxxData>(id)`，無需關心 CSV 解析細節。

## 3. 詳細規則（Detailed Rules）

### 3.1 初始化規則

1. DataManager GameObject 掛載 `DontDestroyOnLoad`，全遊戲生命週期只存在一個實例
2. `Awake` 時依序載入所有已知表格（硬編碼表格名稱列表），載入失敗則 `Debug.LogError` 並跳過（不中斷遊戲）
3. 所有系統的 `Awake` 執行順序必須晚於 DataManager（透過 Unity Script Execution Order 設定）

### 3.2 CSV 解析規則

1. CSV 第一列為欄位標題（header row），後續每列為一筆資料
2. 第一欄固定為主鍵（Primary Key），型別為 `string`
3. 欄位分隔符：`,`（逗號）；若欄位值本身含逗號，用雙引號包裹
4. 多值欄位（如 `traitIDs`、`raceIDs`）以 `|` 分隔儲存在單一欄位中，由 DataManager 拆分為 `string[]`
5. 空白列與 `#` 開頭的列視為註解，跳過不解析

### 3.3 查詢 API 規則

1. `Get<T>(string id)` — 以主鍵查單筆，找不到回傳 `null` 並 `Debug.LogWarning`
2. `GetAll<T>()` — 回傳該表所有資料的 `IReadOnlyList<T>`
3. `GetFloat(string key)` / `GetInt(string key)` — 專用於 `SystemConstants` 表的 key-value 查詢
4. 所有查詢為唯讀，外部系統不得修改快取內容
5. 所有泛型 API 需加上 `where T : class` 約束，因 `Get<T>` 回傳 `null` 要求 T 為參考型別
6. 所有 CSV 的 PK 在 DataManager 內一律以 `string` 索引。使用 int PK 的表格（如 `missionID`、`professionID`），呼叫方需傳入 `id.ToString()`。為簡化使用，DataManager 額外提供 `Get<T>(int id)` 多載，內部轉換為 `Get<T>(id.ToString())`

### 3.4 隨機池 API 規則

1. `PickRandom<T>(string groupID, int? overrideCount = null)` — 依群組表定義抽樣
2. `pickMode` 支援兩種：
   - `"weighted"` — 依 `weights` 欄位加權隨機（總和不必為 1，自動正規化）
   - `"uniform"` — 等機率隨機
3. `pickCount` 定義抽幾個；若 `overrideCount` 有值則覆蓋表格定義
4. 抽樣**不重複**（無放回抽樣），若池大小 < pickCount 則抽完所有項目

## 4. 公式（Formulas）

### 4.1 加權隨機正規化

```
weights = [w₁, w₂, ..., wₙ]
total = Σ wᵢ
normalizedWeights[i] = wᵢ / total
```

抽樣時對 `[0, 1)` 隨機數累積比對正規化權重。

**範例**：`raceIDs = human|elf|orc`，`weights = 50|30|20`
- total = 100
- 機率：human 50%、elf 30%、orc 20%

### 4.2 無置回抽樣

```
pool = 原始列表副本（shallow copy）
result = []
repeat pickCount times:
    i = weightedRandom(pool)
    result.add(pool[i])
    pool.removeAt(i)          // 移除已抽項目
    重新正規化剩餘權重
return result
```

若 `pool.Count < pickCount`：抽完所有剩餘項目後停止，不報錯。

### 4.3 SystemConstants 型別轉換

```
GetFloat(key) = float.Parse(dict[key].value)
GetInt(key)   = int.Parse(dict[key].value)
```

解析失敗（非數字）：回傳 `0` 並 `Debug.LogError`。

### 4.4 完整 API 一覽

| API | 簽名 | 說明 |
|-----|------|------|
| 單筆查詢 | `Get<T>(string id) : T where T : class` | 以主鍵查單筆，找不到回傳 `null` 並 `LogWarning` |
| 全表查詢 | `GetAll<T>() : IReadOnlyList<T> where T : class` | 回傳該表所有資料 |
| 系統常數 | `GetFloat(string key) : float` | 查 `SystemConstants` 浮點值 |
| 系統常數 | `GetInt(string key) : int` | 查 `SystemConstants` 整數值 |
| 群組隨機 | `PickRandom<T>(string groupID, int? overrideCount) : List<T> where T : class` | 依群組表定義加權無放回抽樣 |
| 條件篩選 | `GetWhere<T>(Predicate<T> predicate) : IReadOnlyList<T> where T : class` | 回傳符合條件的所有資料 |
| 條件隨機 | `PickRandomWhere<T>(Predicate<T> predicate, int count) : List<T> where T : class` | 從符合條件的子集中隨機抽取，等機率抽樣 |
| int 多載查詢 | `Get<T>(int id) : T where T : class` | 以 int 主鍵查單筆，內部轉為 string 後查詢 |

**使用範例：**
```csharp
// 取得特定職業
var warrior = DataManager.Instance.Get<ProfessionData>("warrior");

// 取得所有 C 難度討伐任務
var missions = DataManager.Instance.GetWhere<MissionData>(
    m => m.difficulty == "C" && m.type == "討伐");

// 從符合當前世界狀態的事件中隨機抽一個
var evt = DataManager.Instance.PickRandomWhere<EventData>(
    e => e.triggerCondition == currentWorldState, 1);

// 依群組表抽取 Trait（pickCount 由表格定義）
var traits = DataManager.Instance.PickRandom<TraitData>("warrior_traits");
```

## 5. 邊緣案例（Edge Cases）

### 5.1 載入階段

| 情況                                  | 處理方式                                   |
| ----------------------------------- | -------------------------------------- |
| CSV 檔案不存在（`Resources.Load` 回傳 null） | `Debug.LogError` 記錄缺失表格名稱，跳過該表，其他表繼續載入 |
| CSV 格式錯誤（欄位數與 header 不符）            | `Debug.LogWarning` 記錄列號，跳過該列，繼續解析      |
| 主鍵重複（同一表中兩列有相同 ID）                  | 後者覆蓋前者，`Debug.LogWarning` 提示衝突 ID      |
| CSV 完全空白（只有 header，無資料列）            | 建立空字典，不報錯（合法狀態，如尚未填充的表格）               |

### 5.2 查詢階段

| 情況 | 處理方式 |
|------|---------|
| `Get<T>(id)` 查詢不存在的 ID | 回傳 `null`，`Debug.LogWarning` 印出查詢的 ID 與型別名稱 |
| `GetWhere<T>` 無符合條件的資料 | 回傳空列表，不報錯（呼叫方負責處理空列表） |
| `GetFloat`/`GetInt` 查詢不存在的 key | 回傳 `0`，`Debug.LogError` |
| `GetFloat` 查詢到非數字值 | 回傳 `0`，`Debug.LogError` 印出 key 與實際值 |

### 5.3 隨機池階段

| 情況 | 處理方式 |
|------|---------|
| `PickRandom` groupID 不存在 | 回傳空列表，`Debug.LogError` |
| `pickCount` > 池大小 | 抽完所有項目後停止，不報錯，不補足 |
| `PickRandomWhere` 篩選後池為空 | 回傳空列表，不報錯（呼叫方負責處理） |
| weights 全為 0 | 退化為等機率隨機，`Debug.LogWarning` |

### 5.4 生命週期

| 情況 | 處理方式 |
|------|---------|
| 場景中存在多個 DataManager | 後者 `Awake` 時偵測到已有實例，自我銷毀（`Destroy(gameObject)`） |
| 系統在 DataManager 初始化完成前呼叫 `Get<T>` | 因 Script Execution Order 保證 DataManager 先執行，此情況不應發生；若發生回傳 `null` 並 `Debug.LogError` |

## 6. 依賴關係（Dependencies）

### 6.1 DataManager 的依賴（上游）

**無**。DataManager 不依賴任何遊戲系統，僅依賴 Unity 內建 API：
- `UnityEngine.Resources`（CSV 載入）
- `UnityEngine.MonoBehaviour`（生命週期）

### 6.2 依賴 DataManager 的系統（下游）

所有系統在初始化時都必須等 DataManager 完成載入。

| 系統 | 使用的 API | 用途 |
|------|-----------|------|
| C-01 Mission Database | `Get<MissionData>`, `GetAll<MissionData>`, `GetWhere` | 任務模板查詢、難度池篩選 |
| C-02 Adventurer Management | `Get<AdventurerTemplate>`, `PickRandom` | 冒險者模板、生成時隨機池 |
| C-03 Profession System | `Get<ProfessionData>`, `GetAll` | 職業定義查詢 |
| C-04 Race System | `Get<RaceData>`, `GetAll<ProfessionRacePoolData>` | 種族查詢、職業種族池載入（加權隨機由 C-04 自行實作） |
| C-05 Trait System | `Get<TraitData>`, `PickRandom` | 特質查詢、依群組抽特質 |
| F-02 Time System | `GetInt`（`DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS`） | 每日重置時間、離線時間上限常數 |
| F-03 Resource Management | `Get<BankruptcyThresholdData>`, `GetInt`（`GOLD_MAX`、`GOLD_INITIAL`、`REPUTATION_MIN`、`REPUTATION_MAX`） | 破產門檻表、金幣上下限常數、聲望邊界常數 |
| C-06 World Danger System | `Get<WorldDangerData>`, `GetAll<MissionPoolWeightData>`, `GetAll<DebtLimitData>` | 危險度表、任務池權重表、債務上限表 |
| FT-02 Mission Dispatch | `GetFloat`（`SuccessRateTable`、`DeathRateTable`） | 成功率/死亡率查表 |
| FT-04 Outcome Resolution | `GetFloat`（`ReputationDeltaTable`）、`GetInt` | 聲望增減查表 |
| FT-05 Commission Flow | `GetFloat`（`COMMISSION_RATE`、`PENALTY_RATE`） | 傭金/賠償率常數 |
| FT-06 Guild Core | `Get<GuildLevelData>` | 公會等級門檻查詢 |
| FT-07 Guild Building | `Get<BuildingData>`, `GetAll` | 建設項目定義 |
| FT-08 Guild Staff | `Get<StaffData>`, `GetAll` | 職員定義 |
| FT-09 Faction Story | `Get<StoryNodeData>`, `GetWhere` | 劇情節點查詢 |
| FT-10 Save/Load | `GetAll<T>`（各表）| 反序列化時驗證 ID 合法性 |
| P-02 Main UI | `GetAll`、`GetWhere` | UI 呈現用資料查詢 |

### 6.3 介面規範

下游系統對 DataManager 的唯一假設：**`Awake` 完成後，所有表格已可查詢**。DataManager 不提供非同步 callback，不發送事件——下游系統直接在自身 `Start` 或之後呼叫 API 即可。

### 6.4 Core 層初始化順序

Core 層系統間存在鏈式依賴，Script Execution Order 需確保以下順序：

| 順序 | 系統 | 依賴 |
|------|------|------|
| 1 | F-01 DataManager | 無 |
| 2 | F-02 Time System | F-01 |
| 3 | C-01 Mission Database | F-01 |
| 4 | C-03 Profession System | F-01, C-01 |
| 5 | C-04 Race System | F-01, C-01, C-03 |
| 6 | C-05 Trait System | F-01, C-03 |
| 7 | C-06 World Danger System | F-01, F-02 |
| 8 | F-03 Resource Management | F-01, F-02, C-06（`Start` 中呼叫 `C-06.GetMaxDebt()` 取得初始值） |
| 9 | C-02 Adventurer Management | F-01, F-02, C-03, C-04, C-05 |

> C-06 在 `Awake` 完成時將初始值（E 階）設好；F-03 在 `Start` 中呼叫 `C-06.GetMaxDebt()` 取得 `_currentMaxDebt` 初始值。

## 7. 可調參數（Tuning Knobs）

| 參數 | 位置 | 預設值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| `DATA_TABLE_PATH` | 程式碼常數 | `"Data/Tables/"` | — | CSV 載入根路徑（相對於 `Resources/`） |
| `LIST_SEPARATOR` | 程式碼常數 | `'|'` | — | 多值欄位分隔符，修改需同步更新所有 CSV |
| `COMMENT_PREFIX` | 程式碼常數 | `'#'` | — | CSV 註解列前綴 |
| `LOG_MISSING_KEY` | 程式碼常數（bool） | `true` | — | 查詢不存在 ID 時是否印 Warning；可在發布版關閉 |

> 以上參數為開發期常數，定義在 `DataManager.cs` 頂部，不從 CSV 載入。修改任一參數需同步更新對應的 CSV 或 Build 設定。

## 8. 驗收標準（Acceptance Criteria）

### 載入

- [ ] 遊戲啟動後，Console 無任何 `LogError`（所有 CSV 正常載入）
- [ ] 刪除任一 CSV 檔案，Console 出現對應的 `LogError`，遊戲不 crash
- [ ] 在 CSV 中新增一列資料，重新啟動遊戲後 `GetAll<T>()` 回傳的數量正確增加

### 查詢 API

- [ ] `Get<T>("不存在的ID")` 回傳 `null` 且 Console 出現 `LogWarning`
- [ ] `GetAll<T>()` 回傳數量與 CSV 資料列數相符（扣除 header 與註解列）
- [ ] `GetFloat("COMMISSION_RATE")` 回傳 `0.20`
- [ ] `GetFloat("不存在的key")` 回傳 `0` 且 Console 出現 `LogError`
- [ ] `GetWhere<MissionData>(m => m.difficulty == "C")` 只回傳難度為 C 的任務

### 隨機池

- [ ] `PickRandom<TraitData>("warrior_traits")` 不重複地回傳 `pickCount` 個項目
- [ ] 同一 groupID 呼叫 100 次，各項目出現次數符合 weights 定義的機率分布（誤差 ±10%）
- [ ] `PickRandom` 的 `pickCount` 大於池大小時，回傳池中所有項目，不 crash

### 生命週期

- [ ] 場景中放置兩個 DataManager GameObject，執行後只剩一個
- [ ] 所有遊戲系統在 `Start` 中呼叫 `Get<T>` 均能正確取得資料（Script Execution Order 正確）
