---
paths:
  - "TheGuild-unity/Assets/Resources/Data/**"
---

# Data File Rules

> 規範來源：`design/GDD/【F-01】data-manager.md` §3.2，全專案 CSV 一律遵守。
> 本檔為實作層唯一規範；`design/Data-Specs/data-index.md` 僅維護表格清單與狀態，不再重複規範條文。

## CSV 結構規則（column-based / 轉置格式，2026-04-26 起）

| 規則 | 說明 |
|---|---|
| 第一列 | PK 列（PK row）；第 0 欄是 PK 欄位名稱（如 `id`、`professionID`），第 1..N 欄是各筆記錄的 PK 值 |
| 後續每列 | 一個欄位的定義列；第 0 欄是欄位名稱，第 1..N 欄是該欄位在各筆記錄的值（與 PK 列同 column index 對齊） |
| 欄位分隔符 | `,`（逗號）；欄位值本身含逗號時，用 `"..."` 包裹整個欄位 |
| 多值欄位 | 以 `|` 分隔多個值，儲存在單一 cell 中（DataManager 拆分為 `string[]`） |
| 註解列 | 第 0 欄首字元為 `#` → 整列跳過，不解析（可放在任何位置，包括 PK 列前後或欄位列之間） |
| 空白列 | 整列所有欄位皆空 → 跳過 |
| PK 型別 | PK 值在 DataManager 內一律以 `string` 索引 |

**範例**（轉置格式）：

```csv
professionID,1,2,3
name,戰士,法師,遊俠
description,前線肉盾,遠程魔法,全地形採集
strongTypeIDs,1,4,3
raceIDs,1|3,2|4,1|2
raceWeights,60|40,55|45,50|50
```

→ 解析後得到 3 筆記錄，主鍵分別為 `1`/`2`/`3`，各筆記錄都有 `professionID`/`name`/... 等欄位。

**SystemConstants 特殊表**：解析方式同上（轉置），但 parser 只認 `key` 列與 `value` 列（任意順序、可夾註解列），其他列（如 `description`）忽略：

```csv
key,DAILY_RESET_HOUR,OFFLINE_MAX_SECONDS
value,0,604800
description,UTC 0~23,離線時間上限（秒）
```

## 符號速查

| 符號 | 用途 | 範例 |
|---|---|---|
| `,` | 欄位分隔符 | `warrior,10,melee` |
| `|` | 多值欄位內的值分隔符 | `traitIDs = brave|strong|quick` |
| `#` | 行首作為註解前綴，整列跳過 | `# 這是註解` |
| `"..."` | 包裹含逗號的欄位值 | `"Sword, Shield"` |

## 特殊值規範

| 型別 | Null sentinel | bool 合法值 |
|---|---|---|
| `int` | `0` | `true` / `false` / `TRUE` / `FALSE` / `1` / `0` |
| `string` | `""` | — |
| 多值欄位 | 單一 `0` | — |

- 所有 `int` 欄位無值時填 `0`，**不得留白**
- `0` 為全專案統一的 null sentinel；所有 PK 從 `1` 起，確保 `0` 永遠不與合法 ID 衝突
- 多值欄位（`|` 分隔的 int[]）無值時填單一 `0`，DataManager 解析後自動過濾，程式層不會收到 `0`
- `string` 欄位無值時填空字串 `""`，不填 `"null"` 或 `"0"`

## 命名規範

- 欄位名稱中的「ID」縮寫一律使用**大寫** `ID` / `IDs`，不使用 `Id` / `Ids`
  - ✅ `professionID`、`missionID`、`typeIDs`、`strongTypeIDs`
  - ❌ `professionId`、`missionId`、`typeIds`、`strongTypeIds`
- 此規範適用於所有 CSV 欄位名稱、GDD 資料表定義、C# 欄位名稱（`public int professionID`）
- CSV 檔名使用 PascalCase（如 `SystemConstants.csv`），DataSpec 規格書 md 使用 kebab-case（如 `system-constants.md`），兩者一一對映

## CSV 資料表 ID 型別使用原則

欄位型別依以下原則選擇：

**使用 `int`（數字 ID）：**
- 所有實體資料表的 Primary Key（PK）— 如 `missionID`、`adventurerID`、`traitID`
- 跨表引用的 Foreign Key（FK）— 如 `typeID`、`categoryID`、`professionID`
- 可能擴充的列舉類別 — 如任務類型、任務類別、職業、種族

**使用 `string`：**
- 具有顯示語義的等級符號 — 如難度 `F/E/D/C/B/A/S/SS/SSS`、冒險者階級 `F~S`（同時作為識別符與顯示值）
- 語義標籤（tag）而非 FK 引用 — 如 `styleTag`（light/mixed/dark）
- Key-Value 常數表（`SystemConstants`）的 key 欄位

**原則說明：**
- 避免在跨系統引用中使用 string 比對（typo 風險高、無法 IDE 追蹤）
- 新增類型／類別只需在對應的查找表加一行，不需改程式碼
- 等級符號（F~SSS）因跨所有系統廣泛使用且具備自解釋性，例外維持 string
