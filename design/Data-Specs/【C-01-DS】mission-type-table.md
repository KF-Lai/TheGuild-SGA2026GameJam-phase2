# 【C-01-DS】MissionTypeTable

任務類型查找表：定義遊戲中所有合法的任務類型，作為 MissionTemplate.typeID 引用基礎與跨系統 typeID 驗證依據。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/MissionTypeTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-01 MissionDatabase.RegisterTables()（GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Mission.MissionTypeData`
- **讀取 API**：
  - `DataManager.Get<MissionTypeData>(typeID)` — 單筆
  - `DataManager.GetAll<MissionTypeData>()` — 全列表（供 `GetAllMissionTypes()` 回傳合法 typeID 集合）
- **消費者**：
  - C-01 MissionDatabase：`GetTypeName(int typeID)` 供 UI 顯示；`GetAllMissionTypes()` 供 C-03 / C-04 載入時驗證（GDD §3.3）
  - C-03 Profession System：載入時驗證 `strongTypeIDs` / `weakTypeIDs` 合法性（C-03 §6.1）
  - C-04 Race System：載入時驗證 `modifiers.typeID` 合法性（C-04 §6.1）
  - P-02 Main UI：顯示任務類型名稱（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `typeID` | `int` (PK) | ✓ | ≥ 1 | 任務類型唯一識別符；`0` 為 null sentinel |
| `typeName` | `string` | ✓ | — | 任務類型顯示名稱 |

## 約束 / 不變量

- Game Jam 版本固定 4 筆：typeID 1=討伐、2=護送、3=採集、4=調查（GDD §3.1）
- `MissionTypeTable` 完全空白時：`Debug.LogError`，所有依賴 typeID 查詢的回傳為空列表（GDD §5.3）
- `typeID = 2`（護送）具有護送約束語意，由 C-01 `IsValidCombination` 硬式判斷（GDD §3.2 / §4.3）；`ESCORT_TYPE_ID = 2` 存於 SystemConstants，不 hardcode（GDD §7.1）
- `GetTypeName(int typeID)` 傳入不存在的 typeID：回傳 `null` 並出現 `Debug.LogWarning`（GDD §3.3 AC-MD-13）

## Cross-ref

無

## 變更注意事項

- 新增任務類型後需同步更新 `ProfessionTable.strongTypeIDs` / `weakTypeIDs` 及 `RaceTable.modifiers`（GDD §7.2）
- 刪除既有 typeID 會導致 MissionTemplate / ProfessionTable / RaceTable 中引用該 typeID 的行載入時報錯或被過濾

## 範例

```csv
# === MissionTypeTable 任務類型 ===
# typeID=2 護送任務在 MissionTemplate 中僅允許 D~A 難度（GDD §3.2）

typeID,1,2,3,4
typeName,討伐,護送,採集,調查
```
