# 【C-01-DS】MissionTemplate

任務模板定義表：每筆對應一個可在委託板出現的任務，持有任務的基礎屬性與跨表引用。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/MissionTemplate.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-01 MissionDatabase.RegisterTables()（GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Mission.MissionTemplate`
- **讀取 API**：
  - `DataManager.Get<MissionTemplate>(missionID)` — 單筆查詢
  - `DataManager.GetWhere<MissionTemplate>(predicate)` — 篩選（供 GetRegularTemplates / GetTemplatesByCategory）
- **消費者**：
  - C-01 MissionDatabase：`GetTemplate`、`GetRegularTemplates`、`GetTemplatesByCategory` API（GDD §3.3）
  - FT-02 Mission Dispatch：取得任務難度與類型（GDD §6.2）；**v3.1 新增（P3.1-001）** 讀取 `requiredTraitID` 進行派遣前置條件驗證
  - FT-03（派遣驗證）：**v3.1 新增（P3.1-001）** 消費 `requiredTraitID`
  - FT-04 Outcome Resolution：結算時讀取 `missionDifficulty` / `missionTypeID` / `missionFactionID` 快照（GDD §6.2）；**v3.1 新增（P3.1-001）** 讀取 `isScriptedDeath` 執行 short-circuit 擲骰
  - FT-05 Commission Flow：**v3.1 新增（P3.1-001）** `SelectMissionFromPool` 讀取 `minDangerLevel` 過濾候選（GDD §6.2）
  - FT-06 Guild Core：`GetTemplatesByCategory(categoryID=2)` 觸發公會升等考驗（GDD §6.2）
  - FT-09 Faction Story：`GetTemplatesByCategory(categoryID=3)` 觸發陣營劇情任務，讀取 `factionID`（GDD §6.2）
  - FT-10 Save/Load：驗證存檔 `missionID` 仍合法（GDD §6.2）
  - P-02 Main UI：顯示委託板任務資訊（名稱、難度、類型、時長、報酬）（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `missionID` | `int` (PK) | ✓ | ≥ 1 | 純數字唯一識別符；`0` 為全專案保留 null sentinel |
| `difficulty` | `string` | ✓ | F/E/D/C/B/A/S/SS/SSS | 對應 MissionDifficultyTable.difficulty |
| `typeID` | `int` | ✓ | ≥ 1 | FK → MissionTypeTable.typeID |
| `factionID` | `int` | ✓ | ≥ 0 | FK → FactionRouteTable；`0` = neutral（不計入任何陣營分數） |
| `categoryID` | `int` | ✓ | ≥ 0 | FK → MissionCategoryTable.categoryID |
| `isScriptedDeath` | `int` | - | 0 / 1 | **v3.1 新增（P3.1-001）** 1 = 強制必死任務，FT-04 short-circuit 擲骰；**僅允許 `categoryID = 3`**，否則載入時 `LogError` 並重置為 `0`；預設 `0` |
| `minDangerLevel` | `int` | - | 0 ~ 4 | **v3.1 新增（P3.1-001）** 最小世界危險度索引（E=0, D=1, C=2, B=3, A=4）；FT-05 `SelectMissionFromPool` 以此過濾；`0` = 無限制；預設 `0` |
| `requiredTraitID` | `int` | - | ≥ 0 | **v3.1 新增（P3.1-001）** FK → C-05 TraitTable.traitID；派遣前置條件，冒險者需持有此 trait；`0` = 無限制；FT-02 / FT-03 消費；預設 `0` |

## 約束 / 不變量

- `typeID = 2`（護送）僅允許出現於 `difficulty = D / C / B / A`；違規時 `Debug.LogError`，跳過該模板（GDD §3.2 規則 1）
- `categoryID != 0`（非 regular）的模板不進入 Commission Flow 常規生成池（GDD §3.2 規則 2）
- `factionID = 0` 表示 neutral，不計入任何陣營分數（GDD §3.2 規則 3）
- `factionID` 在 FactionRouteTable 找不到且非 `0`：`Debug.LogWarning`，視為 neutral，模板不跳過（GDD §5.1）
- `typeID` 在 MissionTypeTable 找不到：`Debug.LogError`，跳過該模板（GDD §5.1）
- `categoryID` 在 MissionCategoryTable 找不到：`Debug.LogError`，跳過該模板（GDD §5.1）
- 同一 `missionID` 在 CSV 重複：後者覆蓋前者，`Debug.LogWarning`（GDD §5.3）
- **v3.1 新增（P3.1-001）** `isScriptedDeath == 1` 且 `categoryID != 3`：`Debug.LogError`，重置 `isScriptedDeath` 為 `0`，模板仍載入不跳過（GDD §5.1）
- **v3.1 新增（P3.1-001）** `minDangerLevel` 不在 `[0, 4]`：`Debug.LogError`，重置為 `0`（GDD §5.1）
- **v3.1 新增（P3.1-001）** `requiredTraitID > 0` 且 TraitTable 中找不到對應 `traitID`：`Debug.LogError`，重置為 `0`（GDD §5.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `difficulty` | `MissionDifficultyTable.difficulty` | 弱約束（parser 不檢查；DifficultyTable 缺行時 API 回傳 0 / 0.0） |
| `typeID` | `MissionTypeTable.typeID` | FK 強約束（載入時驗證，違規跳過模板） |
| `categoryID` | `MissionCategoryTable.categoryID` | FK 強約束（載入時驗證，違規跳過模板） |
| `factionID` | `FactionRouteTable.factionID` | 弱約束（找不到時視為 neutral，不跳過模板） |
| `requiredTraitID` | `C-05 TraitTable.traitID` | **v3.1 新增（P3.1-001）** FK 強約束（`> 0` 時驗證；找不到時 `LogError`，重置為 `0`，模板仍載入） |

## 變更注意事項

- 修改後需 domain reload（重啟 Unity）才生效
- 新增護送任務（`typeID=2`）必須確保 `difficulty ∈ {D, C, B, A}`，否則載入時跳過
- 新增任務類型（typeID）需先在 `MissionTypeTable` 新增對應行，並同步更新 `ProfessionTable` 的擅長/弱點欄位（GDD §7.2）
- 新增陣營（factionID）需先在 `FactionRouteTable` 新增並在 FT-09 實作分數累積邏輯（GDD §7.2）

## 範例

```csv
# === MissionTemplate 任務模板 ===
# difficulty 護送任務(typeID=2)只能填 D/C/B/A；categoryID=0 進入常規委託生成池

missionID,1,2,3,4,5
difficulty,F,D,C,B,B
typeID,1,2,3,1,4
factionID,0,1,0,2,0
categoryID,0,0,0,0,1
```
（數值對齊 GDD §3.1 / §3.2 設計意圖：護送限 D~A；factionID=0=neutral；tutorial/special 另設 categoryID）

## 變更紀錄

| 日期 | 版本 | 變更摘要 |
|------|------|---------|
| 2026-04-19 | v1.0 | 初版建立 |
| 2026-04-30 | v1.1 | v3.1 patch P3.1-001：新增 `isScriptedDeath` / `minDangerLevel` / `requiredTraitID` 三欄位定義、約束規則、Cross-ref 條目；補充 FT-03 / FT-04 / FT-05 消費者說明。完整 patch summary 見 `design/_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` |
