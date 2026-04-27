# 【F-01-FSD】功能規格說明書 — DataManager

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【F-01】data-manager.md`（版本：2026-04-19） |
| 對應 Data-Specs | `【F-01-DS】system-constants.md` |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（自檢）；Claude Code 主體（§8.3 條目 3 裁決方案 A patch） |
| 狀態 | 已完成 |
| 最近更新 | 2026-04-26（裁決 §8.3 條目 3 採方案 A：已補 `GetString` / `GetBool` 實作至 `DataManager.cs`，並對 F-01 GDD §3.3 / §4.3 / §4.4 加 FSD 回註） |

---

## 1. 概要（Overview）

### 1.1 系統範圍

DataManager 是遊戲的資料基礎設施（Foundation 層），統一處理 CSV 表格的註冊、載入、快取與查詢。職責限定為「資料讀取通路」：透過 `Resources.Load<TextAsset>` 取得 CSV 文字、由 `CsvParser` 反射綁定為強型別物件、以 `Dictionary` 為主鍵索引、再透過 `Get<T>` / `GetAll<T>` / `GetWhere<T>` / `GetFloat` / `GetInt` / `PickRandom<T>` 對外提供唯讀查詢。隨機池工具支援 `weighted` 與 `uniform` 兩種無置回抽樣模式。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：

- CSV 文字解析（含主鍵列、欄位分隔、引號跳脫、註解列、空行）
- 反射式型別綁定（`int` / `long` / `float` / `double` / `bool` / `string` / `string[]` / `float[]`）
- 表格註冊機制（`RegisterTable<T>` / `RegisterSystemConstantsTable` / `RegisterGroupPoolTable<T>`）
- 表格快取（`Dictionary<Type, Dictionary<string, object>>`）
- 公開查詢 API（單筆／全表／條件篩選／系統常數／隨機池）
- 多 DataManager 實例的去重（後者自我銷毀）
- `Get<T>` 整數主鍵多載

**Out-of-Scope**：

- 業務語意（不知道 `MissionData.difficulty` 代表什麼）
- 資料驗證（不檢查 ID 是否合法、不檢查欄位值區間）
- 即時熱重載（重啟遊戲才會重新載入）
- 序列化／存檔（由 FT-10 處理）
- ScriptableObject 載入（本系統只處理 CSV；如需 SO 由下游各自實作）
- 群組隨機池的「保底」機制（FT-08 自帶 PITY 邏輯，本系統只提供基礎抽樣）

### 1.3 完成目標（Definition of Done）

- 所有下游系統呼叫 `RegisterTable<T>(tableName)` 後，`DataManager.Awake` 執行完畢時 `Get<T>(id)` 回傳對應實例（測試方式：以 `SetTableTextProviderForTests` 注入測試 CSV，呼叫 `InitializeForTests` 後驗證 `Get<T>`）
- 缺檔表格在 Console 輸出 `LogError` 並跳過該表，其他表載入不受影響
- `Get<T>("不存在ID")` 回傳 `null` 並 `LogWarning`；`GetFloat("不存在 key")` 回傳 `0` 並 `LogError`
- `PickRandom<T>(groupID)` 在 100 次呼叫下，各成員出現次數與 `weights` 正規化機率分布誤差 ±10% 內（uniform 模式同等機率分布）
- 場景中放置兩個 DataManager GameObject，運行後僅保留一個（後到者自我 Destroy）
- 所有公開 API 在 `_loaded == false` 時回傳預設值並 `LogError`，不丟例外（單元測試覆蓋）

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要：系統定位與資料驅動策略
- §3.1~§3.4：初始化、CSV 解析、查詢 API、隨機池規則
- §4.1~§4.3：加權正規化、無置回抽樣、SystemConstants 型別轉換公式
- §5.1~§5.4：邊緣案例（載入／查詢／隨機池／生命週期）
- §6：依賴關係（無上游、所有 Core/Feature 為下游）
- §7：可調參數（程式碼常數）
- §8：驗收標準

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `key`、`value` | 經由 `RegisterSystemConstantsTable` 載入；`description` 欄位 parser 忽略，純註解用途 |

### 2.3 上游依賴系統

**無**。F-01 為 Foundation 層零依賴系統，僅依賴 Unity 內建 API：

- `UnityEngine.Resources.Load<TextAsset>`
- `UnityEngine.MonoBehaviour` / `DontDestroyOnLoad`
- `System.Reflection`（CsvParser 反射綁定）
- `System.Random`（隨機池）

### 2.4 下游被依賴系統

所有需要查資料表的系統，依其使用方式分類：

| 下游系統 | 使用 API |
| --- | --- |
| F-02 Time System | `RegisterSystemConstantsTable("SystemConstants")`、`GetInt`（`DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS`） |
| F-03 Resource Management | `Get<BankruptcyThresholdData>`、`GetInt`（`GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX`） |
| C-01 Mission Database | `Get<MissionData>`、`GetAll<MissionData>`、`GetWhere<MissionData>` |
| C-02 Adventurer Management | `Get<AdventurerTemplate>`、`PickRandom<T>` |
| C-03 Profession System | `Get<ProfessionData>`、`GetAll<ProfessionData>` |
| C-04 Race System | `Get<RaceData>`、`GetAll<ProfessionRacePoolData>` |
| C-05 Trait System | `Get<TraitData>`、`PickRandom<TraitData>`（透過 `RegisterGroupPoolTable<TraitGroupData>`） |
| C-06 World Danger System | `Get<WorldDangerData>`、`GetAll<MissionPoolWeightData>`、`GetAll<DebtLimitData>` |
| FT-02 Mission Dispatch | `GetFloat`（`SuccessRateTable`、`DeathRateTable`） |
| FT-03 NPC Decision | `GetFloat`（`ACCEPTANCE_THRESHOLD`、`WILLINGNESS_JITTER`、`DEATH_AVERSION` 等共用 willingness 常數） |
| FT-04 Outcome Resolution | `GetFloat`（`ReputationDeltaTable`）、`GetInt` |
| FT-05 Guild Gold Flow | `GetFloat`（`COMMISSION_RATE`、`PENALTY_RATE`） |
| FT-06 Guild Core | `Get<GuildLevelData>` |
| FT-07 Guild Building | `Get<BuildingData>`、`GetAll<BuildingData>` |
| FT-08 Gacha System | `Get<StaffGachaPoolData>` / `Get<StaffRefreshCostData>` / `Get<StaffRarityProbData>` / `Get<TrashItemData>`（4 個 FT-08 owner CSV）；`Get<StaffData>`（驗證 candidate.staffID）；`StaffTuning.csv` gacha 常數 |
| FT-12 Staff System | `Get<StaffData>` / `GetAll<StaffData>`、`StaffTuning.csv` 系統常數（與 FT-08 共用 owner） |
| FT-09 Faction Story | `Get<StoryNodeData>`、`GetWhere` |
| FT-10 Save/Load | `GetAll<T>`（反序列化時驗證 ID） |
| P-02 Main UI Framework | `GetAll`、`GetWhere`（UI 呈現） |

### 2.5 跨系統事件契約

DataManager **不發布也不訂閱任何事件**。下游系統的唯一假設為：DataManager `Awake` 完成後（即 `_loaded == true`），所有已註冊表格皆可查詢。下游應於 `Start` 或之後呼叫 API；註冊呼叫應於 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` 階段執行，確保早於 `Awake`。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

DataManager 的「玩家」是設計師與開發者：設計師應能僅修改 Google Sheets 並匯出 CSV，於下次啟動立即看到效果；開發者應能僅定義 `[Serializable] class XxxData` 與呼叫 `Get<XxxData>(id)`，無需關心解析細節。

### 3.2 系統目的還原

提供統一的資料驅動基礎設施：每張表只解析一次、查詢以主鍵 ID 字典化、隨機池支援加權無置回抽樣，並對缺資料情境提供可預期的預設值與診斷訊息。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 設計師調表即生效 | 修改 CSV 後重啟遊戲，`GetAll<T>().Count` 即時反映新資料列數 | `Resources.Load<TextAsset>` + `CsvParser.Parse` 全量重新解析；無編譯依賴 |
| 設計師加新欄位不需通知工程師 | CSV 新增欄位後，只要在對應 `XxxData` 加上同名 public field 即可 | 反射綁定（`FieldInfo` / `PropertyInfo` 名稱對應 CSV header） |
| 設計師無需懂程式新增內容 | 新增任務只需新增一列 CSV | 主鍵字串字典化、跨表 ID 引用全為字串／整數 |
| 開發者一行取資料 | `DataManager.Instance.Get<T>(id)` 即得 | Singleton + 泛型 + 唯讀字典 |
| 開發者隨機抽資料免重寫邏輯 | `PickRandom<T>("warrior_traits")` 即得 | `RegisterGroupPoolTable` 預先載入群組表，`RandomPool.PickWithoutReplacement` 統一抽樣演算法 |
| 載入失敗不中斷遊戲 | 缺一張表 Console 紅字但其他系統照常運作 | 註冊→載入分階段；解析包 `try/catch`、缺檔僅 LogError 跳過 |
| 查詢失敗有可診斷訊息 | Console 看到「找不到資料：ID=xxx，型別=YyyData，表格=ZzzTable」 | 統一 LogWarning / LogError 格式，含上下文欄位 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（一份 FSD 對應整個 F-01 系統）。

### 4.2 拆分理由

`DataManager.cs`（557 行）與 `CsvParser.cs`（521 行）雖兩者皆超 500 行，但同屬「資料載入流程」單一職責：DataManager 是有狀態的註冊／快取／查詢端，CsvParser 是無狀態的解析子工具，兩者強耦合且生命週期同步。`RandomPool` 與 `GroupPoolData` 為輔助工具與 DTO。下游系統只感知 DataManager 對外 API，無從拆分子單元。

### 4.3 拆分結果

不拆分。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `DataManager` | `Assets/Scripts/Core/Data/DataManager.cs` | 表格註冊、Awake 載入、快取與唯讀查詢 API | `MonoBehaviour`、`Resources`、`CsvParser`、`RandomPool`、`GroupPoolData` | 550~600 行 |
| `CsvParser` | `Assets/Scripts/Core/Data/CsvParser.cs` | CSV 文字解析、反射綁定、型別轉換 | `System.Reflection`、`System.Globalization` | 500~550 行 |
| `RandomPool` | `Assets/Scripts/Core/Data/RandomPool.cs` | 無置回抽樣（uniform / weighted）演算法 | `System.Random` | 150~200 行 |
| `GroupPoolData` | `Assets/Scripts/Core/Data/GroupPoolData.cs` | 群組隨機池資料結構（DTO） | 無 | < 30 行 |

> **規模觀察**：DataManager 與 CsvParser 兩檔皆在 500 行邊界。短期內維持現狀（職責已分離、可讀性可接受）；若新增 SO 載入或熱重載功能，再依 §2.4 拆分原則檢討。

### 4.5 類別關係（可選）

```
[Static Caller]                  [Instance API Caller]
        │                                  │
        │ RegisterTable<T>                 │ Get<T> / GetAll / PickRandom...
        ▼                                  ▼
┌──────────────────────────────────────────────┐
│              DataManager (MB)                │
│  - _pendingRegistrations (static)            │
│  - _tableCache : Dictionary<Type, ...>       │
│  - _systemConstants : Dictionary<string,...> │
│  - _groupPools : Dictionary<string, GPD>     │
└──────────┬───────────────────────────────────┘
           │ 呼叫
           ▼
   ┌───────────────┐    ┌──────────────────┐
   │  CsvParser    │    │  RandomPool      │
   │  (static)     │    │  (static)        │
   └───────────────┘    └────────┬─────────┘
                                 │ 操作
                                 ▼
                         ┌──────────────┐
                         │ GroupPoolData│
                         │  (DTO)       │
                         └──────────────┘
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

**靜態 API（註冊階段，於下游 `[RuntimeInitializeOnLoadMethod]` 呼叫）**：

| 簽名 | 用途 |
| --- | --- |
| `static void RegisterTable<T>(string tableName) where T : class, new()` | 註冊一般資料表，PK 為第一欄 |
| `static void RegisterSystemConstantsTable(string tableName)` | 註冊 key-value 系統常數表（重複註冊保留首次） |
| `static void RegisterGroupPoolTable<T>(string tableName) where T : class, new()` | 註冊群組池表（型別需為 `GroupPoolData` 或子型別） |

**實例 API（查詢階段，下游 `Start` 之後呼叫）**：

| 簽名 | 回傳 / 失敗行為 |
| --- | --- |
| `T Get<T>(string id) where T : class` | 找不到回 `null` + LogWarning；空字串 ID 回 `null` + LogWarning |
| `T Get<T>(int id) where T : class` | 內部轉 `id.ToString(CultureInfo.InvariantCulture)` |
| `IReadOnlyList<T> GetAll<T>() where T : class` | 表格未註冊回空列表 + LogError |
| `IReadOnlyList<T> GetWhere<T>(Predicate<T> predicate) where T : class` | `predicate == null` 回空列表 + LogError；無符合條目回空列表 |
| `float GetFloat(string key)` | 查無 key 或解析失敗回 `0f` + LogError |
| `int GetInt(string key)` | 查無 key 或解析失敗回 `0` + LogError |
| `List<T> PickRandom<T>(string groupID, int? overrideCount = null) where T : class` | groupID 無對應池回空列表 + LogError；池內成員 `Get<T>` 失敗則跳過 + LogWarning |
| `List<T> PickRandomWhere<T>(Predicate<T> predicate, int count) where T : class` | 等機率無置回抽樣；`count<=0` 回空列表 |

**靜態屬性**：

| 屬性 | 說明 |
| --- | --- |
| `static DataManager Instance { get; private set; }` | 單一實例；後到者於 `Awake` 自我銷毀 |

**內部測試 API**（`internal`，供 EditMode 測試使用）：`SetTableTextProviderForTests`、`SetRandomForTests`、`InitializeForTests`、`ResetForTests`。

### 5.2 事件清單

DataManager 不發布也不訂閱任何事件。

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| 無 | — | — | — |

### 5.3 資料結構

**`GroupPoolData`**（`Assets/Scripts/Core/Data/GroupPoolData.cs`）：

| 欄位 | 型別 | 預設值 | 說明 |
| --- | --- | --- | --- |
| `groupID` | `string` | `null` | 群組 ID；若為空則以表格 PK 為 ID |
| `groupName` | `string` | `null` | 顯示名稱（純註解） |
| `memberIDs` | `string[]` | `Array.Empty<string>()` | 池內成員 ID 列表 |
| `pickCount` | `int` | `1` | 預設抽幾個 |
| `pickMode` | `string` | `"uniform"` | `"uniform"` 或 `"weighted"` |
| `weights` | `float[]` | `Array.Empty<float>()` | 對應 `memberIDs` 的權重；缺項視為 `1f` |

**`TableRegistration`**（DataManager 內部 `readonly struct`）：欄位 `DataType` / `TableName` / `IsSystemConstants` / `IsGroupPool`。

**`SystemConstantsMarker`**（DataManager 內部 `private sealed class`）：型別佔位符，使 `RegisterSystemConstantsTable` 可走相同的註冊管道。

**`CsvRow`**（CsvParser 內部 `readonly struct`）：欄位 `Columns` / `LineNumber`，用於診斷訊息。

**`MemberBinding`**（CsvParser 內部 `sealed class`）：包裝 `FieldInfo` 或 `PropertyInfo`，提供統一的 `SetValue` 與 `MemberType` 存取。

### 5.4 內部資料流

#### 5.4.1 註冊階段（下游觸發）

```
下游系統.[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
  → DataManager.RegisterTable<XxxData>("XxxTable")
      ├─ 步驟 1：if (string.IsNullOrWhiteSpace(tableName)) → LogError 返回
      ├─ 步驟 2：if (_loaded == true) → LogError 返回（不允許運行時新增）
      ├─ 步驟 3：建立 TableRegistration(typeof(T), tableName, isSystemConstants=false, isGroupPool=false)
      ├─ 步驟 4：if (_registrationIndexByTableName 已含 tableName)
      │            ├─ if (keepExistingOnDuplicate && 雙方皆 SystemConstants) → LogWarning 忽略
      │            └─ else → LogWarning 並覆蓋既有註冊
      └─ 步驟 5：寫入 _pendingRegistrations 與 _registrationIndexByTableName
```

#### 5.4.2 載入階段（DataManager.Awake）

```
Unity.RuntimeStart
  → DataManager.Awake → InitializeInstance
      ├─ 步驟 1：if (Instance == this && _loaded) → 已初始化返回
      ├─ 步驟 2：if (Instance != null && Instance != this) → Destroy(gameObject) 返回
      ├─ 步驟 3：Instance = this；DontDestroyOnLoad(gameObject)
      ├─ 步驟 4：LoadAllTables
      │            └─ for each registration in _pendingRegistrations
      │                 ├─ csvText = Resources.Load<TextAsset>(DATA_TABLE_PATH + tableName)?.text
      │                 ├─ if (csvText == null) → LogError 跳過該表
      │                 ├─ try
      │                 │    ├─ if (IsSystemConstants)
      │                 │    │    ├─ constants = CsvParser.ParseSystemConstants(csvText, ...)
      │                 │    │    └─ MergeSystemConstants(constants, tableName)
      │                 │    └─ else
      │                 │         ├─ parsed = CsvParser.Parse(csvText, dataType, ...)
      │                 │         ├─ _tableCache[dataType] = parsed
      │                 │         ├─ _tableNameByType[dataType] = tableName
      │                 │         └─ if (IsGroupPool) → CacheGroupPools(parsed, tableName)
      │                 └─ catch (Exception ex) → LogError 跳過該表
      └─ 步驟 5：_loaded = true
```

#### 5.4.3 查詢階段（下游 `Start` 之後）

```
下游系統.Start / Update
  → DataManager.Instance.Get<XxxData>(id)
      ├─ 步驟 1：TryGetLoadedTable(typeof(XxxData), out table, out tableName)
      │            ├─ if (!_loaded) → LogError 回 false
      │            ├─ if (_tableCache 含 dataType) → 取出 table，回 true
      │            └─ else → LogError「型別未註冊」返回 false
      ├─ 步驟 2：if (string.IsNullOrEmpty(id)) → LogWarning 回 null
      ├─ 步驟 3：if (table.TryGetValue(id, out raw)) → return raw as T
      └─ 步驟 4：→ LogWarning 回 null
```

#### 5.4.4 隨機池抽樣（下游觸發）

```
下游系統.SomeFlow
  → DataManager.Instance.PickRandom<TraitData>("warrior_traits", overrideCount=null)
      ├─ 步驟 1：if (string.IsNullOrEmpty(groupID)) → LogError 回空列表
      ├─ 步驟 2：if (!_groupPools.TryGetValue(groupID, out pool)) → LogError 回空列表
      ├─ 步驟 3：if (pool.memberIDs == null || Length == 0) → 回空列表（不報錯）
      ├─ 步驟 4：targetCount = overrideCount ?? pool.pickCount
      │         if (targetCount <= 0) → 回空列表
      ├─ 步驟 5：for each memberId in pool.memberIDs
      │            ├─ item = Get<T>(memberId)
      │            ├─ if (item == null) → LogWarning 跳過
      │            └─ else → candidates.Add(item)、weights.Add(GetWeightAt(pool, i))
      ├─ 步驟 6：result = RandomPool.PickWithoutReplacement(candidates, targetCount, pool.pickMode, weights, _random, out fallbackToUniform)
      │            ├─ 若 weights 全 ≤ 0 → fallback 為 uniform，第一次轉換時設旗標
      │            ├─ for pickIndex in [0, min(pickCount, source.Count))
      │            │    ├─ if (useWeighted) → PickWeightedIndex（cumulative roll）
      │            │    └─ else → rng.Next(remainingIndices.Count)
      │            └─ 將選中項 add 到 result，從 remainingIndices / remainingWeights 移除
      └─ 步驟 7：if (fallbackToUniform && pickMode == "weighted") → LogWarning 提示退化
```

#### 5.4.5 SystemConstants 查詢（下游觸發）

```
下游系統.SomeFlow
  → DataManager.Instance.GetInt("DAILY_RESET_HOUR")
      ├─ 步驟 1：TryGetSystemConstantRawValue(key, out value)
      │            ├─ if (string.IsNullOrEmpty(key)) → LogError 回 false
      │            └─ if (!_systemConstants.TryGetValue(key, out value)) → LogError 回 false
      ├─ 步驟 2：if (int.TryParse(value, NumberStyles.Integer, InvariantCulture)) → return parsed
      └─ 步驟 3：→ LogError 回 0
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

DataManager 是「載入器」，本身不消費業務表，但**載入所有下游表**。本節僅列 F-01 自身的設定資料：

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SystemConstants.csv` | `key`、`value`（`description` 解析時忽略） | `【F-01-DS】system-constants.md` | 跨系統共用常數的 key-value 倉儲；F-01 提供載入機制（`RegisterSystemConstantsTable`）與查詢 API（`GetInt`、`GetFloat`） | 由首位呼叫 `RegisterSystemConstantsTable` 的系統觸發（目前為 F-02 TimeSystem，於 `[RuntimeInitializeOnLoadMethod]` 註冊）；DataManager.Awake 時統一載入 |

### 6.2 引用的 ScriptableObject

無。F-01 完全以 CSV + `[Serializable] class` 為資料載體，未使用 ScriptableObject。

### 6.3 嚴禁寫死清單

F-01 自身有少數「載入機制常數」屬程式碼層級配置（GDD §7 已歸類為開發期常數），不從 CSV 載入；下游系統的所有遊戲規則參數則必須走 `SystemConstants` 或自身專屬 CSV：

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `DAILY_RESET_HOUR`（下游 F-02 用） | `SystemConstants.csv` `key=DAILY_RESET_HOUR`、`value` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `OFFLINE_MAX_SECONDS`（下游 F-02 用） | `SystemConstants.csv` `key=OFFLINE_MAX_SECONDS`、`value` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `GOLD_INITIAL` / `GOLD_MAX`（下游 F-03 用） | `SystemConstants.csv` 對應 key、`value` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `REPUTATION_MIN` / `REPUTATION_MAX`（下游 F-03 用） | `SystemConstants.csv` 對應 key、`value` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 群組池 `pickCount` / `pickMode` / `weights` | 各群組表（如 `TraitGroupTable.csv`）對應欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |

> **F-01 程式碼常數（不在嚴禁寫死清單內，屬載入機制配置）**：`DATA_TABLE_PATH = "Data/Tables/"`、`LIST_SEPARATOR = '|'`、`COMMENT_PREFIX = '#'`、`LOG_MISSING_KEY = true`。GDD §7 已宣告為開發期常數，修改需同步所有 CSV 或 Build 設定，不視為遊戲調參。

---

## 7. 邊緣案例對策（Edge Case Handling）

### 7.1 載入階段

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| CSV 檔案不存在（`Resources.Load` 回傳 null） | `LoadAllTables` 偵測 `csvText == null` → `Debug.LogError($"[DataManager] 表格 {tableName} 未找到")` 並 `continue`；其他表照常載入 | `DataManager.LoadAllTables` | EditMode：以 `SetTableTextProviderForTests` 注入回傳 null，驗證 LogError 與其他表續載 |
| CSV 格式錯誤（欄位數與 header 不符） | `CsvParser.Parse` 偵測 `row.Columns.Count != headers.Count` → `LogWarning` 含列號 → `continue` | `CsvParser.Parse` | EditMode：注入欄位數異常的 CSV，驗證 LogWarning 與正常列仍解析 |
| 主鍵重複（同一表中兩列有相同 ID） | `CsvParser.Parse` 偵測 `result.ContainsKey(id)` → `LogWarning("主鍵重複，後者覆蓋前者")` 後寫入 | `CsvParser.Parse` | EditMode：注入兩列同 PK，驗證最終值為後者 + LogWarning |
| CSV 完全空白（只有 header，無資料列） | `CsvParser.Parse` 跑完 for 迴圈無寫入；回傳空 dict；DataManager 寫入 `_tableCache` 為空字典 | `CsvParser.Parse`、`DataManager.LoadAllTables` | EditMode：注入只有 header 的 CSV，`GetAll<T>()` 回傳 0 |

### 7.2 查詢階段

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `Get<T>(id)` 查詢不存在的 ID | `Get<T>` `table.TryGetValue` 失敗 → `LogWarning` 含 ID 與型別名 → 回 `null` | `DataManager.Get<T>` | EditMode：對已註冊型別查不存在 ID，驗證回 null + LogWarning |
| `GetWhere<T>` 無符合條件的資料 | for 迴圈不寫入；回傳空 `List<T>`；不報錯 | `DataManager.GetWhere<T>` | EditMode：注入永遠不成立的 predicate，驗證回空列表無 log |
| `GetFloat`/`GetInt` 查詢不存在的 key | `TryGetSystemConstantRawValue` 失敗 → `LogError` 含 key → 回 `0` | `DataManager.GetFloat`、`GetInt`、`TryGetSystemConstantRawValue` | EditMode：查不存在 key，驗證回 0 + LogError |
| `GetFloat` 查詢到非數字值 | `float.TryParse` 失敗 → `LogError` 含 key 與實際 value → 回 `0f` | `DataManager.GetFloat` | EditMode：注入 `key=A,value=abc`，驗證回 0f + LogError |

### 7.3 隨機池階段

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `PickRandom` groupID 不存在 | `_groupPools.TryGetValue` 失敗 → `LogError` 含 groupID 與型別名 → 回空列表 | `DataManager.PickRandom<T>` | EditMode：呼叫不存在 groupID，驗證回空列表 + LogError |
| `pickCount` > 池大小 | `RandomPool.PickWithoutReplacement` 取 `safePickCount = min(pickCount, source.Count)`；不報錯 | `RandomPool.PickWithoutReplacement` | EditMode：池有 3 項、pickCount=10，驗證回傳 3 項 |
| `PickRandomWhere` 篩選後池為空 | `GetWhere` 回空 → `RandomPool.PickWithoutReplacement` `source.Count == 0` 直接回空列表 | `DataManager.PickRandomWhere<T>`、`RandomPool` | EditMode：注入永遠不成立的 predicate，驗證回空列表 |
| weights 全為 0 | `RandomPool.HasPositiveWeight` 為 false → `useWeighted = false` + `fallbackToUniform = true`；`PickRandom` 偵測旗標 → `LogWarning` 提示退化 | `RandomPool.PickWithoutReplacement`、`DataManager.PickRandom<T>` | EditMode：注入 weights = `[0,0,0]`，驗證回傳項目分布為 uniform + LogWarning |

### 7.4 生命週期

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 場景中存在多個 DataManager | `InitializeInstance` 偵測 `Instance != null && Instance != this` → `Destroy(gameObject)`（Edit 模式用 `DestroyImmediate`）後返回 | `DataManager.InitializeInstance` | PlayMode：場景放兩個 DataManager，驗證 `Instance` 唯一且後到者已銷毀 |
| 系統在 DataManager 初始化完成前呼叫 `Get<T>` | `TryGetLoadedTable` 偵測 `_loaded == false` → `LogError("尚未完成載入")` → 回 `null`；GDD §6.3 要求下游於 `Start` 之後呼叫，由註冊機制（`[RuntimeInitializeOnLoadMethod]`）保證註冊早於 `Awake`，故正常情境不應發生 | `DataManager.TryGetLoadedTable` | EditMode：未呼叫 `InitializeForTests` 直接查詢，驗證回 null + LogError |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 初始化規則（全條目） | §5.4.2、§7.4 | 部分對齊 | §3.1.2「硬編碼表格名稱列表」與 §3.1.3「Script Execution Order」與實作不一致；實作改採下游 `[RuntimeInitializeOnLoadMethod]` + `RegisterTable` 去中心化機制；見 §8.3 條目 1、9 |
| §3.2 CSV 解析規則（全條目） | §5.4.2、§7.1 | 對齊 | header / PK / `,` 分隔 / `|` 多值 / `#` 註解皆對齊；實作另支援 `long` / `double` / `bool` / `float[]`，見 §8.3 條目 4 |
| §3.3 查詢 API 規則（全條目） | §5.1、§5.4.3、§5.4.5、§7.2 | 對齊 | `Get<T>` / `GetAll<T>` / `GetWhere<T>` / `GetFloat` / `GetInt` / `Get<T>(int)` 完整對齊；`GetString` / `GetBool` 已於 2026-04-26 補實作（裁決方案 A，見 §8.3 條目 3 與 §8.5），GDD §3.3 / §4.3 / §4.4 已加 FSD 回註 |
| §3.4 隨機池 API 規則（全條目） | §5.1、§5.4.4、§7.3 | 對齊 | `PickRandom<T>` 簽名、`weighted` / `uniform` 兩模式、`overrideCount` 多載、無置回抽樣皆對齊；GDD §3.4.4「池小於 pickCount 抽完所有」對應實作 `safePickCount = min(...)` |

### 8.2 公式對齊或替代說明

- **GDD §4.1 加權隨機正規化**：實作 `RandomPool.PickWeightedIndex` 採用「累積權重對 `roll = rng.NextDouble() * total`」單次掃描，**未顯式正規化為機率**，但結果等價（只需 `roll < cumulative` 即選中）。等價證明：機率 = `wᵢ / total`，與「先除 total 再對 [0,1) 比較」一致。
- **GDD §4.2 無置回抽樣**：實作以 `remainingIndices` + `remainingWeights` 雙列表執行 `RemoveAt` 達成「移除已抽項目」+「重新正規化剩餘權重」。後者藉由「每輪重新計算 `total = Σ remainingWeights`」自動完成。
- **GDD §4.3 SystemConstants 型別轉換**：實作改用 `int.TryParse` / `float.TryParse` + `CultureInfo.InvariantCulture`（GDD 寫 `int.Parse` / `float.Parse`），語意等價但避免例外開銷與 culture 偏差。解析失敗回 `0` + LogError 與 GDD 一致。

### 8.3 未能實現的規則與修改建議（含逆向發現的偏差）

| 序 | 規則 / 偏差 | 實作現況 | 修改建議 |
| --- | --- | --- | --- |
| 1 | GDD §3.1.2 寫「`Awake` 時依序載入所有已知表格（**硬編碼表格名稱列表**）」 | 實作改採**去中心化註冊**：下游系統於 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 呼叫 `DataManager.RegisterTable<T>(tableName)`，DataManager `Awake` 才執行 `LoadAllTables` | **回註 GDD**：將 §3.1.2 改為「`Awake` 時依序載入所有透過 `RegisterTable` 註冊的表格；下游系統於 `[RuntimeInitializeOnLoadMethod]` 階段註冊」。優點：Foundation 層對 Core 層零硬編碼，新增表格不需改 F-01 |
| 2 | GDD §3.3 未提及 `RegisterSystemConstantsTable` 註冊位置 | 實作要求**首位需要 `GetInt` / `GetFloat` 的系統**主動呼叫 `RegisterSystemConstantsTable`；目前由 F-02 TimeSystem 註冊，DataSpec §基本資訊已標註 | **回註 GDD**：在 §3.1 補充「SystemConstants 透過 `RegisterSystemConstantsTable` 註冊；首位呼叫者觸發載入，後續重複呼叫忽略」；DataSpec 已正確記載 |
| 3 | DataSpec §基本資訊列出「讀取 API：`GetInt` / `GetFloat` / `GetString` / `GetBool`」 | **已裁決並落地（2026-04-26 採方案 A）**：補實作 `GetString(string key) : string`（找不到回 `string.Empty` + LogError）與 `GetBool(string key) : bool`（接受 `"true"/"false"` case-insensitive；找不到 key 或解析失敗回 `false` + LogError）至 `DataManager.cs`；同時對 F-01 GDD §3.3 / §4.3 / §4.4 加 FSD 回註。三向（DataSpec、GDD、實作）已對齊 | 已完成；無後續行動 |
| 4 | GDD §3.2 僅描述 CSV 為字串、`|` 多值拆分為 `string[]` | `CsvParser.ConvertValue` 額外支援 `int` / `long` / `float` / `double` / `bool` / `float[]` | **回註 GDD**：在 §3.2 補一條「`CsvParser` 依目標 field 型別自動轉換為 `int` / `long` / `float` / `double` / `bool` / `string[]` / `float[]`；解析失敗 LogError 回該型別預設值」 |
| 5 | GDD §3.4 假設「群組表（如 TraitGroupTable）」直接被 `PickRandom` 識別 | 實作要求另呼叫 `RegisterGroupPoolTable<T>(tableName)`；型別需為 `GroupPoolData` 或子型別；註冊後 `LoadAllTables` 於 `IsGroupPool == true` 分支呼叫 `CacheGroupPools` 將項目寫入 `_groupPools` | **回註 GDD**：在 §3.4 補「群組表須以 `RegisterGroupPoolTable<T>` 註冊；T 須為 `GroupPoolData` 或繼承之型別；DataManager 於載入後自動快取 `_groupPools`」 |
| 6 | GDD §5.1「CSV 完全空白」對應「建立空字典」 | 實作差異：`csvText == null`（檔案不存在）→ LogError 跳過，**不**建立空字典；`csvText` 存在但只有 header → `_tableCache[dataType]` 為空字典；GDD §5.1 沒區分這兩個情境 | **回註 GDD**：在 §5.1 拆為兩列：(a)「檔案不存在」LogError 跳過該表（不建空字典）；(b)「只有 header」建立空字典不報錯 |
| 7 | GDD §3.4.3「`overrideCount` 有值則覆蓋表格定義」 | 實作 `targetCount = overrideCount.HasValue ? overrideCount.Value : pool.pickCount`；接著 `if (targetCount <= 0) → 回空列表`（多一層保護） | 與 GDD 等價，無修改建議 |
| 8 | GDD §5.3「weights 全為 0 → 退化等機率」單階段 | 實作有兩處 fallback：(a) 進入 PickWithoutReplacement 前 `HasPositiveWeight == false` 立即 fallback；(b) 抽樣中途權重列表 `total <= 0` 二次 fallback。`PickRandom` 偵測 fallback 旗標 + `pickMode==weighted` 才 LogWarning | **回註 GDD**：在 §5.3 補「fallback 在進入抽樣前與抽樣中途均可能觸發，僅當原 pickMode 為 weighted 時 LogWarning」 |
| 9 | GDD §3.1.3 / §6.4 要求 Script Execution Order 確保 DataManager `Awake` 早於下游 | 實作**未在程式碼設定** Script Execution Order；改用 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 註冊 + `_loaded` 旗標保證載入完才查詢；若下游違反呼叫順序，`TryGetLoadedTable` 偵測 `!_loaded` 會 LogError 回預設值（不丟例外） | **回註 GDD**：在 §3.1 與 §6.4 補「Script Execution Order 非必要設定；改以 `RegisterTable` 註冊機制 + `_loaded` 旗標保證查詢時資料已就緒」 |

> **總結**：上述 9 項偏差皆**不違反 GDD 設計意圖**，多為實作補強（更精確、更具防呆）或機制改良（去中心化註冊）。建議採「FSD 回註 GDD」處理；條目 3（`GetString` / `GetBool` 缺實作）已於 2026-04-26 採方案 A 補實作並對齊三向（見 §8.4 / §8.5）。其餘 8 項待 F-03 FSD 完成後一次性回註 GDD。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-26 | `【F-01】data-manager.md` | §3.3 第 3 條末 | 條目 3 裁決方案 A：補上 `GetString` / `GetBool` 兩 API 至條文中，對齊 DataSpec 與實作 |
| 2026-04-26 | `【F-01】data-manager.md` | §4.3 SystemConstants 型別轉換 | 條目 3 裁決方案 A：公式補上 `GetString` / `GetBool` 對應規則與失敗回傳；同步註明實作改用 `TryParse` + `CultureInfo.InvariantCulture` 與原 `Parse` 等價 |
| 2026-04-26 | `【F-01】data-manager.md` | §4.4 完整 API 一覽 | 條目 3 裁決方案 A：表格新增 `GetString` / `GetBool` 兩列含失敗回傳行為 |
| 2026-04-26 | `【F-01】data-manager.md` | §3.1 末尾 | 條目 1 + 9 合併回註：rule 2 去中心化 `RegisterTable` 註冊機制；rule 3 Script Execution Order 改用 `[RuntimeInitializeOnLoadMethod]` + `_loaded` 旗標保證載入順序 |
| 2026-04-26 | `【F-01】data-manager.md` | §3.2 末尾 | 條目 4 回註：`CsvParser.ConvertValue` 額外支援 `int` / `long` / `float` / `double` / `bool` / `float[]`（rule 4 為 `string[]` 最小規範） |
| 2026-04-26 | `【F-01】data-manager.md` | §3.3 末尾 | 條目 2 回註：補充 `RegisterSystemConstantsTable` 註冊位置——首位需要 `GetInt/GetFloat/GetString/GetBool` 的系統主動於 `[RuntimeInitializeOnLoadMethod]` 階段呼叫；後續重複呼叫忽略 |
| 2026-04-26 | `【F-01】data-manager.md` | §3.4 末尾 | 條目 5 回註：群組池須以 `RegisterGroupPoolTable<T>` 註冊；T 須為 `GroupPoolData` 或繼承之型別 |
| 2026-04-26 | `【F-01】data-manager.md` | §5.1 末尾 | 條目 6 回註：「CSV 完全空白」拆兩情境——`csvText == null` 不建空字典；`csvText` 只有 header 建空字典；對下游 `Get<T>/GetAll<T>` 回傳行為無語意差異 |
| 2026-04-26 | `【F-01】data-manager.md` | §5.3 末尾 | 條目 8 回註：weights fallback 為兩階段觸發（進入抽樣前 + 抽樣中途）；僅 `pickMode == "weighted"` 時 LogWarning |
| 2026-04-26 | `【F-01】data-manager.md` | §6.4 末尾 | 條目 9 回註（與 §3.1 連動）：實作改用 `[RuntimeInitializeOnLoadMethod]` 取代 Script Execution Order；本表順序仍是分層概念依賴的設計參考 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-26 | DataSpec 列出 `GetInt` / `GetFloat` / `GetString` / `GetBool` 4 個 API，但 GDD §3.3 與實作只列 / 實作 2 個（`GetInt` / `GetFloat`），`GetString` / `GetBool` 缺失 | F-01 GDD §3.3 / §4.3 / §4.4；F-01 DataSpec §基本資訊；`DataManager.cs` | 使用者裁決採方案 A：補實作 `GetString` / `GetBool` 至 `DataManager.cs`（緊接 `GetInt` 之後）；F-01 GDD 三章節（§3.3 / §4.3 / §4.4）加 FSD 回註說明補入；DataSpec 與 FSD 不變 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥
- [x] §1.3 完成目標可被測試驗證
- [x] §2.1~§2.5 四向皆列舉
- [x] §3.3 對映表覆蓋所有幻想／目的
- [x] §4 Script 清單欄位齊全（含路徑、SRP、依賴介面、預估規模）
- [x] §5 API/事件/資料結構/資料流齊備（事件章節明列「無」）
- [x] §6 CSV 引用含對應 Data-Specs；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（GDD §5 全 14 條覆蓋）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（§3.1~§3.4 全列）
- [x] §8.2~§8.5 如實登記（§8.5 衝突無，§8.4 為待執行回註清單）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（撰寫者於本 FSD 寫入後執行）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-26 | unity-specialist subagent | 通過 | 通過 | 通過 | 9 項逆向發現偏差列入 §8.3，建議採「FSD 回註 GDD」處理；DataSpec 承諾但實作缺失的 `GetString` / `GetBool` 需另派工項補齊（見 §8.3 條目 3）；其餘 8 項偏差皆為實作補強或機制改良，不違反設計意圖 |
| 2026-04-26 | Claude Code 主體（§8.3 條目 3 裁決方案 A patch） | 通過 | 通過 | 通過 | 補實作 `GetString` / `GetBool` 至 `DataManager.cs`；F-01 GDD §3.3 / §4.3 / §4.4 加 FSD 回註；§8.1 §3.3 列改「對齊」；§8.3 條目 3 標已裁決並落地；§8.4 補登 3 筆 GDD 回註紀錄；§8.5 補登衝突處理；狀態轉「已完成」 |
