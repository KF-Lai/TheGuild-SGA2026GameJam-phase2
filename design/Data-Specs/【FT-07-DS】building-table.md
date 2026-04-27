# 【FT-07-DS】BuildingTable

儲存 6 棟公會建築各等級的效果值、升級費用與公會等級需求，為 FT-07 `TryUpgradeBuilding()` 與效果值查詢 API 的唯一資料源。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/BuildingTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-07 GuildBuildingSystem 的 `RegisterTables()`（FT-07 §6.1 上游依賴：F-01 DataManager `LoadBuildingTable()`）
- **資料類別**：`BuildingData`（GDD 未指定完整 namespace；FT-07 §7.1）
- **讀取 API**：`DataManager.GetAll<BuildingData>()` → 依 `(buildingID, level)` 組合鍵分組（FT-07 §4.2 pseudocode `BuildingTable[buildingID].upgradeData[level]`）；`DataManager.Get<BuildingData>($"{buildingID}_{level}")` 供單筆查詢
- **消費者**：
  - FT-07 GuildBuildingSystem：`TryUpgradeBuilding(buildingID)` §3.3 — 讀 `upgradeCost` / `guildLevelReq` 做升級閘門判定
  - FT-07 GuildBuildingSystem：`GetMissionSlotCount()` / `GetRosterCap()` / `GetMaxConcurrentMissions()` / `GetRecruitRefreshInterval()` / `GetBankruptcyWarningSeconds()` / `IsStaffSystemUnlocked()` §3.4 — 讀 `effectValue` 對應等級行
  - FT-08 GachaSystem：`BuildingTable[6, level].effectValue` §4.1.3 — 面試自動刷新間隔（via FT-07 `GetBuildingLevel(6)`，L1=86400s → L5=21600s 階梯；L0 時 `IsStaffSystemUnlocked()=false`，FT-08 降級不讀此欄）
  - FT-12 StaffSystem：`BuildingTable[buildingID].slotCount` §3.5 — 直接讀資料表取各建築職員 slot capacity（FT-07 §6.2）
  - FT-10 SaveLoad：`RestoreFromSave()` §6.5 — 驗證 `currentLevel <= maxLevel`（clamp 超出值）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `buildingID` | `int` | ✓ | 1..6 | 複合主鍵欄 1；對應 §3.2 六棟建築（FT-07 §7.1） |
| `name` | `string` | ✓ | 非空 | 建築顯示名稱；UI 顯示用（FT-07 §7.1） |
| `maxLevel` | `int` | ✓ | 3..5（依建築） | 最高等級上限；同一 `buildingID` 的所有 level 行必須一致（FT-07 §7.1） |
| `level` | `int` | ✓ | 0..maxLevel | 複合主鍵欄 2；職員休息室含 `level=0` 行，其他建築從 `level=1` 起（FT-07 §7.1） |
| `effectValue` | `int` | ✓ | ≥ 0 | 該等級效果值；語意依 `buildingID` 而異（見下方附錄）（FT-07 §7.1） |
| `upgradeCost` | `int` | ✓ | ≥ 0 | **升至本等級**所需金幣；最小 level 行固定為 0（FT-07 §7.1） |
| `guildLevelReq` | `int` | ✓ | 0、3、4、5 | **升至本等級**所需公會等級；最小 level 行與 level=2 固定為 0；level>=3 依設計填 3/4/5（FT-07 §7.1、§7.3） |
| `slotCount` | `int` | ✓ | ≥ 0 | 建築可同時容納的職員數；Jam 版各建築所有 level 行值相同（FT-07 §7.1、§7.3） |
| `maintenanceCost` | `int` | ✓ | ≥ 0 | **Phase 2**：每日維護費金幣；L0/L1 填 0；Jam 版 runtime 不讀此欄（FT-07 §3.8、§7.1） |

> **複合主鍵**：`(buildingID, level)`，每棟建築每個等級各佔一筆記錄，共 29 筆（職員休息室 L0~L5 共 6 筆，其餘 5 棟各 L1~L5 共 5/3/5/5/5 筆）。CSV column-based 格式以合成字串鍵 `"{buildingID}_{level}"` 作為 PK 列值（如 `1_1`、`6_0`），規則見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)。

> **初始等級**：由該 `buildingID` 最小 `level` 行決定——職員休息室最小 level=0（未建造），其他建築最小 level=1（初始已建造，FT-07 §7.1）。

## 約束 / 不變量

- 必須包含且僅包含 6 個 `buildingID`（1~6）；缺任一 buildingID → DataManager 載入失敗，拋 `MissingBuildingDataException`，遊戲不啟動（FT-07 §5.4）
- 每個 `buildingID` 的 `level` 行必須完整覆蓋其等級範圍：buildingID=2 需 L1~L3（3 筆）、buildingID=6 需 L0~L5（6 筆）、其餘建築需 L1~L5（5 筆）
- 同一 `buildingID` 內所有行的 `maxLevel` 必須一致（FT-07 §7.1）
- `guildLevelReq` 僅允許值 0、3、4、5；最小 level 行與 level=2 固定為 0（FT-07 §7.1、§7.3 設計決策）
- `upgradeCost` 最小 level 行固定為 0（初始態無升級費，FT-07 §7.1）
- `slotCount` 同一 `buildingID` 所有 level 行值相同（Jam 版固定值原則，FT-07 §7.3）
- `maintenanceCost` L0/L1 固定為 0（FT-07 §7.1）；**Jam 版 runtime 不讀此欄**（Phase 2 規格，FT-07 §3.8）
- 職員休息室（buildingID=6）L0 行的 `effectValue` / `upgradeCost` / `guildLevelReq` / `slotCount` 固定為 0（未建造態，FT-07 §7.1）
- **設計約束（parser 不驗證；違反僅輸出 Warning，與安全範圍硬下限區分）**：
  - `effectValue`（buildingID=2）L1~L3 單調不增（刷新間隔每級縮短，FT-07 §7.1）
  - `effectValue`（buildingID=5）L1~L5 單調不減（破產倒數每級延長，FT-07 §7.1）
  - `effectValue`（buildingID=6）L1~L5 單調不增（FT-08 面試自動刷新間隔每級縮短，FT-07 §7.3 / FT-08 §4.1.3）
  - `effectValue`（buildingID=1/3/4）各 level 單調不減（容量類每級不縮，FT-07 §7.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `guildLevelReq` | FT-06 `GuildLevelTable.level` 公會等級值域 | 弱約束（parser 不驗證；FT-07 §3.3 閘門 2 以 `FT06.GetCurrentLevel() < guildLevelReq` 判定） |
| `upgradeCost` | F-03 `GetGold()` / `AddGold()` | 弱約束（FT-07 §3.3 閘門 3 以 `F03.GetGold() < upgradeCost` 判定，通過後扣款） |

## 變更注意事項

- 修改後 DataManager 重新載入即時生效（FT-07 不快取）
- 調整 `effectValue`（buildingID=5）影響 F-03 破產倒數緩衝秒數（透過 `SetBankruptcyWarningDuration` 推送），需確認 D 難度任務時長（約 9,000s）仍小於 L1 緩衝（10,800s，FT-07 §7.2 注意事項）
- 調整 `effectValue`（buildingID=6 L1~L5）影響 FT-08 面試自動刷新間隔，需維持 L1=86400 → L5=21600 的單調下降階梯（FT-07 §7.3 / FT-08 §4.1.3）；L0 為未建造守門值（`effectValue=0`），不可改非 0
- 調整 `upgradeCost` 影響玩家金幣積累節奏；建議各建築總費用不超過同期預期累積收入的 50%（FT-07 §7.2）
- 縮短 `effectValue`（buildingID=2）刷新間隔時最短建議 ≥ 3600s（1h，FT-07 §7.2）
- `slotCount` Jam 版不隨 level 變動；若 Post-Jam 需 level-scaled slot，需同步調整 FT-08 §3.7 capacity 重算流程（FT-07 §7.3）
- `maintenanceCost` Phase 2 啟用前只需保持 schema 完整性，Jam 版改動此欄不影響 runtime 行為（FT-07 §3.8 / §8.8）

## 範例

```csv
# === 建築升級表 ===
# PK: buildingID_level（複合主鍵；格式 {buildingID}_{level}）
# 對齊 FT-07 §7.1 完整預設值（5 筆示例：委託板 L1/L2、招募廣告欄 L1、職員休息室 L0/L1）

buildingID_level,1_1,1_2,2_1,6_0,6_1
buildingID,1,1,2,6,6
name,委託板,委託板,招募廣告欄,職員休息室,職員休息室
maxLevel,5,5,3,5,5
level,1,2,1,0,1
effectValue,5,8,86400,0,86400
upgradeCost,0,150,0,0,500
guildLevelReq,0,0,0,0,0
slotCount,3,3,0,0,0
maintenanceCost,0,15,0,0,0
```

（每個欄位一列；5 筆示例記錄，完整 29 筆數值對齊 FT-07 §7.1 完整預設值表；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### effectValue 語意對應表（FT-07 §3.4）

| buildingID | 建築名稱 | effectValue 語意 | 單位 |
|---|---|---|---|
| 1 | 委託板 | 委託槽數（`GetMissionSlotCount()`） | 整數（槽） |
| 2 | 招募廣告欄 | 候選池刷新間隔（`GetRecruitRefreshInterval()`） | 秒 |
| 3 | 公會大廳 | 冒險者名冊上限（`GetRosterCap()`） | 整數（人） |
| 4 | 公會櫃臺 | 同時任務上限（`GetMaxConcurrentMissions()`） | 整數（件） |
| 5 | 預備金保險櫃 | 破產倒數緩衝（`GetBankruptcyWarningSeconds()`） | 秒 |
| 6 | 職員休息室 | FT-08 面試自動刷新間隔（L0 = 0，L1~L5 漸降） | 秒 |

### slotCount 固定值（FT-07 §7.1 / §7.3）

| buildingID | slotCount | 說明 |
|---|---|---|
| 1 | 3 | 委託板（FT-12 §4.2 `RecruitRefreshOnCounter` slot capacity） |
| 2 | 0 | 招募廣告欄（不提供職員位置） |
| 3 | 0 | 公會大廳（不提供職員位置） |
| 4 | 2 | 公會櫃臺（FT-12 §4.2 `RecruitRefreshOnCounter` slot capacity 受此限制） |
| 5 | 1 | 預備金保險櫃（FT-12 §3.4.3 `AccountantPenaltyOnVault` slot capacity 自然封頂） |
| 6 | 0 | 職員休息室（不提供職員位置） |

### 安全範圍與調參指引（FT-07 §7.2）

| buildingID | 調整目標 | 安全範圍 |
|---|---|---|
| 2 | 刷新間隔（秒） | 最短 ≥ 3600（1h），避免刷新過快失去等待感 |
| 5 | 破產倒數（秒） | 最短 1800（30min），最長 604800（7 天）；L1 必須 > D 難度任務時長（約 9,000s） |
| 3、4 | 名冊/任務上限 | 名冊建議 ≤ 50；並行任務建議 ≤ 名冊上限的 70% |
| 1~6 | 升級費用 | 各建築總費用建議不超過同期預期累積收入的 50% |
