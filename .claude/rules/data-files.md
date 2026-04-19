---
paths:
  - "TheGuild-unity/Assets/Resources/Data/**"
---

# Data File Rules

## CSV 資料表 ID 型別使用原則

本專案採用 CSV 作為主要資料格式。欄位型別依以下原則選擇：

**使用 `int`（數字 ID）：**
- 所有實體資料表的 Primary Key（PK）— 如 `missionId`、`adventurerId`、`traitId`
- 跨表引用的 Foreign Key（FK）— 如 `typeId`、`categoryId`、`professionId`
- 可能擴充的列舉類別 — 如任務類型、任務類別、職業、種族

**使用 `string`：**
- 具有顯示語義的等級符號 — 如難度 `F/E/D/C/B/A/S/SS/SSS`、冒險者階級 `F~S`（同時作為識別符與顯示值）
- 語義標籤（tag）而非 FK 引用 — 如 `styleTag`（light/mixed/dark）
- Key-Value 常數表（`SystemConstants`）的 key 欄位

**原則說明：**
- 避免在跨系統引用中使用 string 比對（typo 風險高、無法 IDE 追蹤）
- 新增類型/類別只需在對應的查找表加一行，不需改程式碼
- 等級符號（F~SSS）因跨所有系統廣泛使用且具備自解釋性，例外維持 string

## 欄位命名規範

- 欄位名稱中的「ID」縮寫一律使用**大寫** `ID` / `IDs`，不使用 `Id` / `Ids`
  - ✅ `professionID`、`missionID`、`typeIDs`、`strongTypeIDs`
  - ❌ `professionId`、`missionId`、`typeIds`、`strongTypeIds`
- 此規範適用於所有 CSV 欄位名稱、GDD 資料表定義、C# 欄位名稱（`public int professionID`）

## CSV Null Sentinel 規範

- 所有 `int` 欄位無值時填 `0`，**不得留白**
- `0` 為全專案統一的 null sentinel；所有 PK 從 `1` 起，確保 `0` 永遠不與合法 ID 衝突
- 多值欄位（`|` 分隔的 int[]）無值時填單一 `0`，DataManager 解析後自動過濾，程式層不會收到 `0`
- `string` 欄位無值時填空字串 `""`，不填 `"null"` 或 `"0"`

- All JSON files must be valid JSON — broken JSON blocks the entire build pipeline
- File naming: lowercase with underscores, following `[system]_[name].json` pattern
- Every data file must have a documented schema (in the corresponding design doc)
- Numeric values must include companion docs explaining what the numbers mean
- Use consistent key naming: `camelCase` for keys within JSON files
- No orphaned data entries — every entry must be referenced by code or another data file
- Include sensible defaults for all optional fields

## Examples

**Correct** (`quest_templates.json`):

```json
{
  "goblinHunt": {
    "displayName": "Goblin Hunt",
    "difficulty": 2,
    "baseReward": 50,
    "durationMinutes": 30,
    "requiredSkill": "combat",
    "minLevel": 1
  }
}
```

**Incorrect** (`QuestData.json`):

```json
{
  "Goblin Hunt": { "reward": 50 }
}
```

Violations: spaces in key, uppercase filename, no `[system]_[name]` pattern, missing required fields.
