# 【C-01-DS】MissionCategoryTable

任務類別查找表：區分任務進入哪個觸發池（常規委託板 / 新手引導 / 公會升等 / 陣營劇情），作為 MissionTemplate.categoryID 的引用基礎。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/MissionCategoryTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-01 MissionDatabase.RegisterTables()（GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Mission.MissionCategoryData`
- **讀取 API**：
  - `DataManager.Get<MissionCategoryData>(categoryID)` — 單筆
  - `DataManager.GetAll<MissionCategoryData>()` — 全列表
- **消費者**：
  - C-01 MissionDatabase：`GetCategoryName(int categoryID)` 供 UI 顯示；篩選邏輯依 `categoryID` 分流（GDD §3.3）
  - FT-05 Commission Flow：常規委託池只取 `categoryID = 0` 的模板（GDD §3.2 規則 2）
  - FT-06 Guild Core：`GetTemplatesByCategory(categoryID=2)` 觸發公會升等考驗任務（GDD §6.2）
  - FT-09 Faction Story：`GetTemplatesByCategory(categoryID=3)` 觸發陣營劇情特殊任務（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `categoryID` | `int` (PK) | ✓ | ≥ 0 | 任務類別識別符 |
| `categoryName` | `string` | ✓ | — | 類別機器識別名稱（英文，供程式碼邏輯參考） |

> `categoryID = 0` 是合法的 regular 分類，不作為 null sentinel 使用。本表 PK 從 `0` 起，與全專案「PK ≥ 1、0 為 null sentinel」的慣例不同，係 GDD §3.1 MissionCategoryTable 設計決定。

## 約束 / 不變量

- Game Jam 版本固定 4 筆：0=regular、1=tutorial、2=guild_challenge、3=faction_story（GDD §3.1）
- `MissionCategoryTable` 完全空白時：`Debug.LogError`，所有依賴 categoryID 的查詢回傳空列表（GDD §5.3）
- `categoryID != 0` 的模板不進入 Commission Flow 常規生成池（GDD §3.2 規則 2）
- 不得刪除 `categoryID = 0`（regular）；所有常規委託依賴此值

## Cross-ref

無

## 變更注意事項

- 新增類別後需在對應觸發系統（FT-06 / FT-09 等）實作觸發邏輯（GDD §7.2）
- 修改後需 domain reload 才生效

## 範例

```csv
# === MissionCategoryTable 任務類別 ===
# categoryID=0(regular) 進入 Commission Flow 生成池；其餘由特定系統觸發

categoryID,0,1,2,3
categoryName,regular,tutorial,guild_challenge,faction_story
```
