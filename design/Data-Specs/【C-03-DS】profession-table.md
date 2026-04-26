# 【C-03-DS】ProfessionTable

職業定義表：每筆對應一個冒險者職業，持有擅長/弱點類型、升級結構，以及職業 → 種族隨機池（C-04 消費）與職業 → 特質群組（C-05 消費）三組欄位，為「職業」entity 的單一表。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/ProfessionTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-03 ProfessionSystem（DataManager.GetAll<ProfessionData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Profession.ProfessionData`
- **讀取 API**：
  - `DataManager.Get<ProfessionData>(professionID)` — 單筆
  - `DataManager.GetAll<ProfessionData>()` — 全職業列表
  - C-03 公開封裝：`GetProfession(int professionID)`、`GetAllProfessions()`、`GetBaseProfessions()`（GDD §3.4）
- **消費者**：
  - C-02 Adventurer Management：每位冒險者持有 `professionID`；UI 顯示職業名稱與描述（GDD §6.2）
  - C-04 Race System：`RollRace(professionID)` 讀取 `raceIDs` / `raceWeights` 執行加權抽種族（C-04 §3.2 / §4.1）
  - C-05 Trait System：`GetProfessionGroups(professionID)` 讀取 `traitGroupIDs` 反查 TraitGroupTable（C-05 §3.4）
  - FT-01 Recruitment：`GetBaseProfessions()` 取 tier=1 職業列表作為招募池（GDD §6.2）
  - FT-02 Mission Dispatch：`IsStrongType` / `IsWeakType` 套用成功率修正（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `professionID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別符；`0` 為全專案保留 null sentinel |
| `name` | `string` | ✓ | — | 職業顯示名稱 |
| `description` | `string` | ✓ | — | 職業定位描述（UI 顯示） |
| `strongTypeIDs` | `int[]`（`\|` 分隔） | ✓ | — | 擅長的任務 typeID 列表；無擅長填 `0`（null sentinel） |
| `weakTypeIDs` | `int[]`（`\|` 分隔） | ✓ | — | 弱點的任務 typeID 列表；無弱點填 `0`（null sentinel） |
| `tier` | `int` | ✓ | ≥ 1 | 職業階層；Game Jam 全為 `1` |
| `baseProfessionID` | `int` | ✓ | ≥ 0 | 升級來源職業；tier=1 填 `0`；tier≥2 填來源 professionID |
| `raceIDs` | `int[]`（`\|` 分隔） | ✓ | — | 職業冒險者可抽種族 ID 列表（C-04 §3.2 消費） |
| `raceWeights` | `int[]`（`\|` 分隔） | ✓ | > 0 per element | 對應每個 `raceIDs` 的相對權重；長度必須與 `raceIDs` 一致（GDD §3.1） |
| `traitGroupIDs` | `int[]`（`\|` 分隔） | ✓ | — | 職業冒險者生成時抽取的特質群組 ID 列表（C-05 §3.4 消費） |

## 約束 / 不變量

- 同一 typeID 不得同時出現在 `strongTypeIDs` 與 `weakTypeIDs`；違規時 `Debug.LogError`，跳過該職業（GDD §3.2 規則 3）
- `strongTypeIDs` / `weakTypeIDs` 包含不存在於 MissionTypeTable 的 typeID（非 0）：`Debug.LogWarning`，過濾該 typeID，其餘正常載入（GDD §5.1）
- `tier ≥ 2` 的職業必須有合法 `baseProfessionID`（非 0、指向存在職業）；違規時 `Debug.LogError`，跳過（GDD §3.3）
- `raceIDs` 與 `raceWeights` 長度必須一致；不一致時 `Debug.LogError`，C-04 `RollRace` 對此職業 fallback 回傳 `raceID=1`（C-04 §5.1）
- 多值欄位僅含 `0` 視為空列表（GDD §5.1）
- 同一 `professionID` 重複：後者覆蓋，`Debug.LogWarning`（GDD §5.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `strongTypeIDs` | `MissionTypeTable.typeID` | 弱約束（載入時驗證，不存在則過濾並警告） |
| `weakTypeIDs` | `MissionTypeTable.typeID` | 弱約束（同上） |
| `baseProfessionID` | `ProfessionTable.professionID`（自參） | FK 強約束（tier≥2 時驗證，違規跳過） |
| `raceIDs` | `RaceTable.raceID` | 弱約束（C-04 載入時驗證，不存在則過濾） |
| `traitGroupIDs` | `TraitGroupTable.groupID` | 弱約束（C-05 載入時驗證，不存在則跳過該 groupID） |

## 變更注意事項

- 修改後需 domain reload 才生效
- 新增職業：CSV 加行，分配唯一 `professionID`，同時填妥 `raceIDs` / `raceWeights` / `traitGroupIDs` 欄位（GDD §7）
- 調整 `raceIDs` / `raceWeights`：與 C-04 owner 同步意圖（GDD §7）
- 調整 `traitGroupIDs`：與 C-05 owner 同步意圖（GDD §7）
- 傭兵（`professionID=7`）維持 `strongTypeIDs=0`、`weakTypeIDs=0` 作為無修正基準線（GDD §7）

## 範例

```csv
# === ProfessionTable 職業定義表（Game Jam 7 種職業，全 tier=1）===
# raceWeights 為相對比例，不需加總為 100（GDD §3.1）

professionID,1,2,3,7
name,戰士,法師,遊俠,傭兵
description,前線肉盾與近戰主力,遠程魔法輸出,全地形採集與追蹤,無固定專長的雇傭戰士
strongTypeIDs,1,4,3,0
weakTypeIDs,4,3,2,0
tier,1,1,1,1
baseProfessionID,0,0,0,0
raceIDs,1|3,2|4,1|2,1|3|4
raceWeights,60|40,55|45,50|50,50|30|20
traitGroupIDs,1|3|4,1|2|4,1|2|5,2|4|5
```
（完整 7 行資料見 GDD §3.1 Game Jam 初始資料表；traitGroupIDs 設計意圖見 C-05 §3.4）
