# 工項需求書：F-01 DataManager

_建立時間：2026-04-23_
_系統 ID：F-01_
_分級：**Large**_（新 Foundation 系統，跨系統基礎建設，後續所有系統依賴）
_對應 GDD：`design/GDD/[F-01] data-manager.md`_
_交付對象：Codex（實作）_

---

## 1. 工項概要（Summary）

實作遊戲資料基礎設施 `DataManager`：Singleton MonoBehaviour，**以下游延遲註冊模式**運作——F-01 本身不知道任何下游資料類別，由各下游系統透過 `DataManager.RegisterTable<T>(string tableName)` 於 `Awake` 前自行註冊；DataManager 於 `Awake` 階段依註冊清單逐表載入 `Assets/Resources/Data/Tables/{tableName}.csv`，解析並快取為記憶體字典，提供以主鍵 ID 查詢單筆／全表／條件篩選與加權無放回抽樣的泛型 API。所有下游系統（F-02、F-03、C-01~C-06、FT-01~FT-10）透過 `DataManager.Instance.Get<T>(id)` 取得資料。

**交付流程**：
1. 階段一：Codex 以 `sandbox="read-only"` 提出完整實作（含測試用 CSV 範例與 EditMode 單元測試骨架）
2. 階段二：Claude Code 審查 → APPROVED → Codex 以 `sandbox="workspace-write"` 寫入
3. 最多 2 次來回；仍未解決需通知使用者介入

---

## 2. 目標與範圍（Goals & Scope）

### 2.1 In Scope（本工項範圍內）

- `DataManager.cs`（Singleton MonoBehaviour，`DontDestroyOnLoad`，雙實例自我銷毀）
- CSV 解析器（header row + PK 字串索引 + `|` 多值分隔 + `#` 註解行 + 雙引號逸出）
- 查詢 API：`Get<T>(string)`、`Get<T>(int)`、`GetAll<T>()`、`GetWhere<T>(Predicate)`、`GetFloat(string)`、`GetInt(string)`
- 隨機池 API：`PickRandom<T>(string groupID, int? overrideCount)`、`PickRandomWhere<T>(Predicate, int)`
- 載入期、查詢期、隨機池、生命週期四類 edge case 的錯誤處理（全部走 `Debug.LogError/LogWarning`，不中斷遊戲）
- Script Execution Order 設定（DataManager 最早執行）
- EditMode 單元測試：涵蓋 AC-01~AC-12（見 §7 驗收條件）
- 預留**空的** CSV 目錄結構 `Assets/Resources/Data/Tables/`（Codex 不生成實際資料表；由 systems-designer 後續填充）

### 2.2 Out of Scope（不做）

- **不實作任何遊戲系統資料類別**（`ProfessionData`、`MissionData` 等由各 Core 層系統各自定義；本工項僅提供測試用的 stub 資料類別如 `TestTableData`）
- **不實作** ScriptableObject wrapper（本專案採「CSV 解析到 POCO 字典」策略，非 ScriptableObject）
- **不生成實際 CSV 內容**（`SystemConstants.csv`、`BankruptcyThresholdTable.csv` 等由後續工項處理）
- **不實作** Save/Load、EventBus、UI 整合
- **不處理** CSV 熱重載（Hot Reload）；只在 `Awake` 載入一次

### 2.3 載入策略（契約）

遵循 GDD §3.1 rule 2「**依序載入所有已知表格**」——**不採** `Resources.LoadAll` 全掃描。

**下游延遲註冊模式**（本專案分層原則）：

- F-01 本身**不知道任何下游資料類別**，維持「零遊戲系統依賴」
- 下游系統（F-02、F-03、C-01~C-06 等）透過 `DataManager.RegisterTable<T>(string tableName)` 於 `[RuntimeInitializeOnLoadMethod]` 或 `Awake` 前自行註冊自己的表格
- DataManager 於 `Awake` 階段依「已註冊清單」逐一以 `Resources.Load<TextAsset>(path + tableName)` 讀取；回傳 `null` 時明確 `LogError("表格 {tableName} 未找到")`
- 此策略同時滿足：
  - 可偵測缺檔（AC-DM-02）；若改用 `LoadAll` 則無法判斷「少了哪張」
  - 不違反分層（F-01 Core/Data 不引用 Gameplay/Resources 或任何下游 namespace）

---

## 3. 依賴與前置條件（Dependencies & Preconditions）

### 3.1 上游依賴

| 依賴項 | 狀態 | 備註 |
|-------|------|------|
| Unity 2D URP 專案骨架 | ✅ 已存在 | `TheGuild-unity/` |
| `UnityEngine.Resources` | ✅ Unity 內建 | CSV 以 `TextAsset` 方式載入 |
| `UnityEngine.MonoBehaviour` | ✅ Unity 內建 | 生命週期 |

**DataManager 零遊戲系統依賴**。

### 3.2 下游影響（供 Codex 了解不可破壞的介面契約）

所有 Core / Feature / Presentation 層系統都會呼叫本工項提供的 API。請**嚴格遵守 GDD §4.4 的 API 簽名表**，尤其 `where T : class` 泛型約束、int 主鍵多載、回傳型別（`IReadOnlyList<T>` 不是 `List<T>`）。

---

## 4. 實作要求（Implementation Requirements）

### 4.1 檔案位置

```
TheGuild-unity/Assets/Scripts/Core/Data/
├── DataManager.cs              # Singleton 主類別
├── CsvParser.cs                # 純 C# CSV 解析器（無 MonoBehaviour 依賴，利於測試）
├── RandomPool.cs               # 加權無放回抽樣工具（純 C#）
└── GroupPoolData.cs            # 群組池資料結構（對應 TraitGroupTable / ProfessionRacePool 等）

TheGuild-unity/Assets/Resources/Data/Tables/
└── .gitkeep                    # 保留目錄結構，實際 CSV 由後續工項填充

TheGuild-unity/Assets/Scripts/Tests/EditMode/Core/Data/
└── DataManagerTests.cs         # EditMode 單元測試
```

### 4.2 命名空間

```csharp
namespace TheGuild.Core.Data
```

### 4.3 公開 API 簽名（不可更動）

對應 GDD §4.4，以下簽名為契約：

```csharp
public sealed class DataManager : MonoBehaviour
{
    public static DataManager Instance { get; private set; }

    // 表格註冊（下游系統於 Awake 前呼叫；靜態方法、不需 Instance）
    public static void RegisterTable<T>(string tableName) where T : class, new();
    public static void RegisterSystemConstantsTable(string tableName);  // 特殊：key-value 表
    public static void RegisterGroupPoolTable<T>(string tableName)      // 群組池（GroupPoolData 或其子型別）
        where T : class, new();

    // 單筆查詢
    public T Get<T>(string id) where T : class;
    public T Get<T>(int id) where T : class;          // 內部轉 id.ToString()

    // 全表查詢
    public IReadOnlyList<T> GetAll<T>() where T : class;

    // 條件篩選
    public IReadOnlyList<T> GetWhere<T>(System.Predicate<T> predicate) where T : class;

    // SystemConstants 專用
    public float GetFloat(string key);
    public int GetInt(string key);

    // 隨機池
    public List<T> PickRandom<T>(string groupID, int? overrideCount = null) where T : class;
    public List<T> PickRandomWhere<T>(System.Predicate<T> predicate, int count) where T : class;
}
```

**註冊 API 使用範例**（F-03 使用）：

```csharp
// F-03 ResourceManagement.cs 內
[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void RegisterTables()
{
    DataManager.RegisterTable<BankruptcyThresholdData>("BankruptcyThresholdTable");
    DataManager.RegisterSystemConstantsTable("SystemConstants");   // 冪等：重複註冊同名表僅 LogWarning，不報錯
}
```

**註冊契約**：
- 同名表格重複註冊 → `LogWarning("表格 {tableName} 已由 {Type} 註冊，後續註冊以 {NewType} 覆蓋")`
- 註冊必須於 DataManager `Awake` 之前完成；之後註冊 → `LogError("DataManager 已載入，表格 {tableName} 註冊失敗")`，該表不會被載入
- `RegisterSystemConstantsTable` 多次呼叫同一 `tableName` 視為冪等，僅首次註冊生效

### 4.4 內部常數

定義在 `DataManager.cs` 頂部（private const）：

| 常數 | 值 | 說明 |
|------|----|------|
| `DATA_TABLE_PATH` | `"Data/Tables/"` | `Resources.Load<TextAsset>(DATA_TABLE_PATH + tableName)` 用；相對於 `Resources/` |
| `LIST_SEPARATOR` | `'\|'` | 多值欄位分隔符 |
| `COMMENT_PREFIX` | `'#'` | CSV 註解列前綴 |
| `LOG_MISSING_KEY` | `true` | 查詢不存在 ID 時是否 `LogWarning` |

### 4.5 CSV 解析規則（對應 GDD §3.2）

1. 第一列為 header，第一欄為主鍵（`string` 索引）
2. `,` 分隔欄位；欄位值含逗號時以 `"` 包裹，支援 `""` 逸出雙引號
3. 多值欄位以 `|` 分隔，回傳 `string[]`
4. 空行與 `#` 開頭的行跳過
5. 欄位數與 header 不符：`LogWarning(列號)` 跳過該列，**不中斷**整表解析
6. PK 重複：後者覆蓋前者，`LogWarning(重複 ID)`
7. **反射綁定機制**：依目標類別 `T` 的 `public` 欄位（或屬性）名稱與 header 匹配，自動轉型。**支援型別清單（契約）**：`string`、`int`、`long`、`float`、`double`、`bool`、`string[]`；不支援的型別記錄 `LogError` 並將該欄位設為預設值

> **註**：`long` 支援為 F-03 `BankruptcyThresholdData.warningDurationSec` 需求；`double` 為前瞻性擴充。


### 4.6 隨機池規則（對應 GDD §3.4 + §4.1~§4.2）

- `GroupPoolData` 欄位（通用池資料結構，供 `TraitGroupTable`、`ProfessionRacePool` 等共用）：
  - `groupID : string`（PK）
  - `groupName : string`
  - `memberIDs : string[]`（**池成員 ID 陣列**）
  - `pickCount : int`
  - `pickMode : string`（`"weighted"` / `"uniform"`）
  - `weights : float[]`

**CSV header 策略（定案）**：**所有群組池 CSV 統一使用 `memberIDs` 作為 header 欄位名**，不支援別名（如 `traitIDs` / `raceIDs`）。此決策理由：
- 反射綁定機制以「欄位名完全匹配」運作，別名會增加實作與測試複雜度
- 各群組池表格實務上僅在 CSV 內容語意不同，欄位結構一致，統一命名可共用 `GroupPoolData` 類別無子型別
- 若後續有語意差異需求，再以 `DataManager.RegisterGroupPoolTable<TSubclass>` 註冊子型別擴充
- `PickRandom<T>`：
  1. 從群組池讀 `groupID` 對應的 `GroupPoolData`；不存在 → 回傳空 `List<T>()` + `LogError`
  2. 將 `memberIDs` 解析為 `T` 的實例列表（透過 `Get<T>(id)`；找不到的 ID 跳過 + `LogWarning`）
  3. 無放回抽樣 `pickCount`（或 `overrideCount`）次；每次抽後移除已選並重新正規化剩餘權重
  4. 池大小 < `pickCount`：抽完所有項目後停止，不報錯
  5. `uniform` 模式：每次等機率從剩餘池抽一個
  6. `weighted` 模式且 `weights` 全為 0：退化為 `uniform` + `LogWarning`

### 4.7 生命週期（對應 GDD §3.1 + §5.4）

1. `Awake`：Singleton 檢查 → 已有實例則 `Destroy(gameObject)` 後 return
2. 否則：`Instance = this`、`DontDestroyOnLoad(gameObject)`、呼叫 `LoadAllTables()`
3. `LoadAllTables`：
   - 迭代**已註冊清單**（由下游 `[RuntimeInitializeOnLoadMethod]` 預先呼叫 `RegisterTable<T>` 累積；見 §4.7a）
   - 對每筆註冊項執行 `Resources.Load<TextAsset>(DATA_TABLE_PATH + registration.TableName)`
   - 回傳 `null` → `LogError("[DataManager] 表格 {registration.TableName} 未找到")`，跳過此表，其他表繼續載入
   - 成功 → `CsvParser.Parse(textAsset.text, registration.DataType)` 解析為 `Dictionary<string, object>`（`PK → 資料實例`）
   - 結果存入 `Dictionary<Type, Dictionary<string, object>>`（以 `registration.DataType` 為外層 key，不以表名為 key——查詢時統一以 `typeof(T)` 路徑訪問）
   - 若 `registration.IsSystemConstants == true`：額外建立 `Dictionary<string, string>` 快取給 `GetFloat`/`GetInt` 使用
4. 註冊時機驗證：`LoadAllTables` 完成後設 `_loaded = true`；之後呼叫 `RegisterTable` → `LogError`（太晚）
5. Script Execution Order：建立 `DataManager` prefab 並在 `ProjectSettings/ScriptExecutionOrder.asset` 設定優先級為 `-1000`（確保 `Awake` 早於所有其他 MonoBehaviour；**下游的 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 更早於任何 `Awake`**，保證註冊已完成）

### 4.7a 延遲註冊機制（契約）

F-01 **不**維護任何靜態表格清單；改由下游系統主動註冊。內部結構：

```csharp
// DataManager 內部（private static）
private static readonly List<TableRegistration> _pendingRegistrations = new();
private static bool _loaded = false;

private readonly struct TableRegistration
{
    public System.Type DataType { get; }
    public string TableName { get; }
    public bool IsSystemConstants { get; }
    public bool IsGroupPool { get; }
}

// 公開註冊 API 實作（簡化）
public static void RegisterTable<T>(string tableName) where T : class, new()
{
    if (_loaded) { Debug.LogError($"DataManager 已載入，表格 {tableName} 註冊失敗"); return; }
    // 重複檢查、推入 _pendingRegistrations
}
```

**查詢路徑**：
- `Get<T>(id)` / `GetAll<T>()`：以 `typeof(T)` 查 `Dictionary<Type, Dictionary<string, object>>`；找不到 `Type` → `LogError("[DataManager] 型別 {typeof(T).Name} 未註冊，請於下游系統 [RuntimeInitializeOnLoadMethod] 中呼叫 DataManager.RegisterTable<{typeof(T).Name}>(\"{tableName}\")")` 並回 `null` / 空列表
- **穩定性**：呼叫方依賴自己的 `Type`，不依賴檔名或反射約定；F-01 Core/Data 不引用任何下游 namespace
- **`SystemConstants` 特殊處理**：透過 `RegisterSystemConstantsTable("SystemConstants")` 註冊；F-01 內部載入此表時以 key-value 方式解析為 `Dictionary<string, string>` 快取給 `GetFloat`/`GetInt`。此表**不**產生 POCO 列表（`GetAll<SystemConstantData>()` 不適用）

**本工項負責**：
- 提供 `RegisterTable<T>` / `RegisterSystemConstantsTable` / `RegisterGroupPoolTable<T>` API
- 在 EditMode 測試中以測試 stub 類別（`TestTableData` 等，位於測試 assembly 內）驗證註冊+載入+查詢的完整流程
- **不**在 production 程式碼中註冊任何表格；production 的第一次註冊由 F-02 / F-03 等後續工項負責

### 4.8 重要技術細節

- **所有泛型 API 必須 `where T : class`**（因為 `Get<T>` 要回傳 `null`）
- **禁用 `FindObjectOfType`、`GameObject.Find`**（違反專案 coding-standards）
- 所有 `Debug.LogError/LogWarning` 訊息須包含：查詢的 ID / key、型別名稱、表格名稱（如適用），方便除錯
- 單元測試須可在**不啟動 Play Mode** 的 EditMode 環境下執行（`CsvParser`、`RandomPool` 保持為純 C#，不依賴 `MonoBehaviour`）

---

## 5. 資料表契約（Data Table Contract）

本工項**不生成**實際 CSV 內容，但需支援以下欄位類型以供後續系統使用：

| 類型 | 範例欄位 | CSV 表達 |
|------|----------|----------|
| `string` | `name`, `description` | `Warrior` |
| `int` | `cost`, `difficulty` | `100` |
| `long` | `warningDurationSec` | `86400` |
| `float` | `successRate`, `weight` | `0.75` |
| `double` | （前瞻擴充） | `0.123456789` |
| `bool` | `isUnique` | `true` / `false` |
| `string[]`（多值） | `memberIDs`, `raceIDs` | `human\|elf\|orc` |

**測試用 CSV 範例**（Codex 需在 EditMode 測試中使用，路徑：`Assets/Tests/EditMode/Core/Data/TestResources/`）：

```csv
# TestTable.csv
id,name,cost,weight,tags
warrior,勇者,100,0.5,melee|physical
mage,法師,150,0.3,ranged|magical
# 這是註解，會被跳過
rogue,盜賊,120,0.2,"melee|stealth,unique"
```

```csv
# TestSystemConstants.csv
key,value,description
TEST_RATE,0.2,測試用比例
TEST_COUNT,5,測試用整數
```

---

## 6. 技術限制（Technical Constraints）

### 6.1 語言與風格

- **C# / .NET**（Unity 2D URP 2022.3+ LTS）
- 命名慣例：`PascalCase`（public）、`_camelCase`（private field）、`camelCase`（local）
- **註釋以繁體中文撰寫**（`//`、`/// <summary>`）
- 識別符號與 Unity API 保持英文
- 公開 API 必須有 `/// <summary>` XML 文件註釋

### 6.2 架構規則

- 單一方法不超過 40 行（資料宣告除外）
- 單一方法循環複雜度 < 10
- 不使用 `static` 單例（本工項例外：`DataManager.Instance` 為經設計批准的 MonoBehaviour Singleton，但**僅此一例**；其他系統仍用 DI）
- 所有 gameplay 數值禁止硬編碼（本工項例外：§4.4 的 DataManager 內部常數屬於基礎設施常數）

### 6.3 測試要求

- EditMode 單元測試使用 NUnit（Unity Test Framework 內建）
- 測試檔案命名：`DataManagerTests.cs`、`CsvParserTests.cs`、`RandomPoolTests.cs`
- 不可依賴 Play Mode；`CsvParser` 與 `RandomPool` 必須為純 C#（無 `MonoBehaviour` 依賴）
- 加權隨機的統計驗證（AC-DM-09）：同 groupID 呼叫 100 次，誤差容忍 ±10%（對齊 GDD §8）

**測試 CSV 注入策略**：

正式執行路徑為 `Assets/Resources/Data/Tables/`，但測試 CSV 不應污染正式 Resources 目錄。建議兩種方案擇一（Codex 實作時選定並說明）：

1. **方案 A：Resources 子目錄隔離** — 測試 CSV 放於 `Assets/Tests/EditMode/Core/Data/Resources/Data/Tables/`；Unity 會將 `Tests/.../Resources/` 視為 Resources 的一部分，但僅 Test assembly 編譯時可見；正式 build 不包含。需驗證此行為在 Unity 2022.3+ 仍成立
2. **方案 B：測試專用 API** — `DataManager` 提供 `internal` 測試 API（如 `LoadFromText(string tableName, string csvText, Type type)`），測試時直接注入 CSV 字串，不走 `Resources.Load`；以 `[InternalsVisibleTo("Tests.EditMode.Core.Data")]` 暴露

**建議方案 B**，因：
- 與正式 Resources 隔離最徹底
- 測試啟動速度更快（無 I/O）
- CsvParser / RandomPool 本就為純 C#，天然支援字串注入

Codex 回報時須明確說明所選方案與理由。

### 6.4 禁用事項

- 禁用 `System.IO` 存取 `StreamingAssets`（本工項僅使用 `Resources.Load`）
- 禁止在非 `Awake` 時載入 CSV
- 禁止在 API 內部修改快取內容（所有查詢結果為唯讀）
- 禁止引入第三方 CSV 解析套件（自行實作，避免 package 管理負擔）

---

## 7. 驗收條件（Acceptance Criteria）

對應 GDD §8，每條皆須可由 EditMode 測試驗證：

| AC ID | 測試項目 | 通過條件 |
|-------|----------|---------|
| AC-DM-01 | 無錯載入 | 所有測試 CSV 載入完成後，Console 無任何 `LogError` |
| AC-DM-02 | 缺檔容錯 | 刪除任一測試 CSV 後重跑，Console 出現對應的 `LogError`，DataManager 仍初始化成功，其他表正常可查 |
| AC-DM-03 | `Get<T>` 找不到 | `Get<TestTableData>("不存在")` 回傳 `null` 且 `LogWarning` |
| AC-DM-04 | `GetAll<T>` 計數 | `GetAll<TestTableData>()` 回傳數量 = CSV 資料列數（扣除 header 與註解列） |
| AC-DM-05 | `GetFloat` 正常 | `GetFloat("TEST_RATE")` 回傳 `0.2f` |
| AC-DM-06 | `GetFloat` 不存在 | `GetFloat("UNKNOWN_KEY")` 回傳 `0f` 且 `LogError` |
| AC-DM-07 | `GetWhere` 篩選 | `GetWhere<TestTableData>(x => x.cost > 100)` 僅回傳 `cost > 100` 的項目 |
| AC-DM-08 | `PickRandom` 不重複 | 相同 groupID 呼叫一次，回傳 `pickCount` 個不重複項目 |
| AC-DM-09 | `PickRandom` 機率分布 | 同 groupID 呼叫 100 次，各項目出現次數符合 weights 比例（誤差 ±10%，對應 GDD §8） |
| AC-DM-10 | `PickRandom` 池不足 | `pickCount` = 5 但池僅 3 個 → 回傳 3 個，不 crash，不 error |
| AC-DM-11 | 雙實例銷毀 | 場景中放兩個 DataManager GameObject，執行後僅剩一個 |
| AC-DM-12 | int 主鍵多載 | `Get<TestMissionData>(123)` 與 `Get<TestMissionData>("123")` 回傳同一筆 |
| AC-DM-13 | 多值欄位解析 | CSV 欄位 `tags = "melee\|stealth,unique"` 解析為 `["melee", "stealth,unique"]`（雙引號內逗號保留、`\|` 拆分） |
| AC-DM-14 | 註解行跳過 | CSV 中 `#` 開頭列不計入 `GetAll<T>()` 結果 |
| AC-DM-15 | PK 重複覆蓋 | 同表兩列 PK 相同時，`Get` 回傳後者，Console 有 `LogWarning` 記錄重複 ID |
| AC-DM-16 | `long` 欄位解析 | 測試 CSV 含 `long` 欄位（如 `86400`），`GetAll<TestLongTableData>()` 回傳正確的 `long` 值 |
| AC-DM-17 | 未註冊型別查詢 | 呼叫 `Get<UnregisteredType>("x")` → 回傳 `null`、`LogError` 含「型別未註冊」訊息 |
| AC-DM-18 | 註冊太晚 | `DataManager` `Awake` 後呼叫 `RegisterTable<T>(...)` → `LogError`，該表不會被載入 |
| AC-DM-19 | 重複註冊 | 對同一 `tableName` 呼叫 `RegisterTable<T1>` 再 `RegisterTable<T2>` → `LogWarning`、以後者為準 |
| AC-DM-20 | `SystemConstants` 註冊 | `RegisterSystemConstantsTable("TestSystemConstants")` 後，`GetFloat("TEST_RATE")` 正確回傳 |

---

## 8. 交付物清單（Deliverables）

Codex 回報完成時，交付物須包含：

1. **原始碼**
   - `Assets/Scripts/Core/Data/DataManager.cs`（含 `RegisterTable` / `RegisterSystemConstantsTable` / `RegisterGroupPoolTable` 靜態 API）
   - `Assets/Scripts/Core/Data/CsvParser.cs`
   - `Assets/Scripts/Core/Data/RandomPool.cs`
   - `Assets/Scripts/Core/Data/GroupPoolData.cs`
2. **測試**
   - `Assets/Tests/EditMode/Core/Data/DataManagerTests.cs`
   - `Assets/Tests/EditMode/Core/Data/CsvParserTests.cs`
   - `Assets/Tests/EditMode/Core/Data/RandomPoolTests.cs`
   - `Assets/Tests/EditMode/Core/Data/TestResources/TestTable.csv`
   - `Assets/Tests/EditMode/Core/Data/TestResources/TestSystemConstants.csv`
   - `Assets/Tests/EditMode/Core/Data/Tests.EditMode.Core.Data.asmdef`（測試組件定義，引用 `UnityEngine.TestRunner`、`UnityEditor.TestRunner`、`nunit.framework` 與生產組件）
3. **專案設定**
   - `ProjectSettings/ScriptExecutionOrder.asset` 更新（DataManager 優先級 `-1000`）
   - `Assets/Resources/Data/Tables/.gitkeep`（空目錄佔位）
4. **實作回報**
   - 延遲註冊機制與 `SystemConstants` 特殊處理的實作摘要（§4.7 + §4.7a）
   - `RegisterTable` 重複註冊、太晚註冊的錯誤處理驗證
   - 測試 CSV 注入策略（見 §6.3）
   - 已通過的 AC-DM-01 ~ AC-DM-20 清單
   - 任何偏離 GDD 或本需求書的決策與原因

---

## 9. 審查重點（Review Checklist，由 Claude Code 使用）

- [ ] 所有公開 API 簽名與 §4.3 完全一致（含泛型約束）
- [ ] 載入策略採**下游延遲註冊**（§2.3 + §4.7 + §4.7a），**非** `Resources.LoadAll`、**非**靜態 `TableManifest`
- [ ] F-01 Core/Data **無任何** using / 引用下游 namespace（`Gameplay.*` / `Core.Time` / `Core.Events` 以外）
- [ ] 反射轉型支援 `string / int / long / float / double / bool / string[]`
- [ ] CSV 解析支援雙引號逸出（AC-DM-13）
- [ ] `Debug.LogError/LogWarning` 訊息包含足夠除錯資訊
- [ ] 生命週期遵守 §4.7（Singleton、DontDestroyOnLoad、Script Execution Order）
- [ ] 單元測試覆蓋所有 AC；EditMode 可執行
- [ ] 無硬編碼遊戲數值、無 `FindObjectOfType`、無第三方 CSV 套件
- [ ] 註釋為繁體中文，識別符號為英文
- [ ] 無破壞未來下游系統契約的偏離（尤其 F-03 需要的 `long` 型別支援）

---

## 10. Codex 調用參考

**階段一（實作，read-only）**：

```
cd: C:/gitlab/LKF/TheGuild-SGA2026GameJam-phase2
sandbox: "read-only"
PROMPT:
依據 production/work-items/[F-01] data-manager.md 實作 F-01 DataManager 系統。
請先閱讀工項需求書全文與 design/GDD/[F-01] data-manager.md，
然後產出所有交付物（§8）的完整程式碼。
實作時請特別注意：
1. §4.3 API 簽名為契約，不可更動（含靜態 Register 系列 API）
2. **延遲註冊機制**（§2.3 + §4.7a）：F-01 不維護任何靜態 TableManifest，不引用任何下游 namespace
3. CSV 解析支援型別：string / int / long / float / double / bool / string[]（§4.5）
4. 測試 CSV 注入策略（§6.3）請選定方案並說明
5. EditMode 測試須覆蓋 §7 所有 AC（AC-DM-01 ~ AC-DM-20）
實作完成後請回報完整程式碼、實作說明、AC 通過清單，等待審查。
```

**階段二（審查通過後，workspace-write）**：沿用 SESSION_ID，sandbox 切換為 `workspace-write`，請 Codex 寫入所有檔案。
