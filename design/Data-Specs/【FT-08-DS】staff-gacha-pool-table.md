# 【FT-08-DS】StaffGachaPoolTable

Gacha 面試池定義表，持有各池的職員清單、稀有度層內權重、公會等級閘與保留時限；供 FT-08 每次 Refresh 時確定候選池範圍與職員 roll 機率。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffGachaPoolTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 初始化（DataManager bootstrap；FT-08 §6.4 資料表依賴）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.GetAll<StaffGachaPoolData>()` 取全池列表；FT-08 以 `minGuildLevel ≤ currentLevel ≤ maxGuildLevel` 篩選可用池（FT-08 §3.2）
- **消費者**：
  - FT-08 GachaSystem：`RollOneSlot` 流程池結構 / 閘判斷 / `staffWeights` 比例 roll（FT-08 §3.2 / §3.3.3）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `poolID` | `int` (PK) | ✓ | ≥ 1 | 唯一池識別碼（Jam 版：1 = 常駐通用、2 = 中高等） |
| `poolName` | `string` | ✓ | — | 開發辨識名稱（不直接顯示 UI） |
| `minGuildLevel` | `int` | ✓ | [1, 5] | 該池開放最低公會等級 |
| `maxGuildLevel` | `int` | ✓ | [1, 5] | 該池開放最高公會等級；DataManager 驗證 `≥ minGuildLevel` |
| `eligibleStaffIDs` | `int[]`（`\|` 分隔） | ✓ | — | 該池可 roll 的職員 `staffID` 清單 |
| `staffWeights` | `int[]`（`\|` 分隔） | ✓ | ≥ 0 per item | 平行於 `eligibleStaffIDs`，稀有度層內 roll 權重；`0` = 保留不抽出 |
| `reserveTimeLimitSec` | `int` | ✓ | > 0 | 本池候選的保留時限（秒）；建議範圍 [3600, 604800]（FT-08 §7.1.3） |
| `storyFlagRequired` | `string` | ✓ | `""` | 🔒 FT-09 劇情 flag ID 預留欄；Jam 版強制 `""` |
| `factionIDRequired` | `int` | ✓ | `0` | 🔒 FT-09 陣營 ID 預留欄；Jam 版強制 `0` |
| `minReputation` | `int` | ✓ | `0` | 🔒 聲望門檻預留欄；Jam 版強制 `0` |
| `eventStartTimestamp` | `long` | ✓ | `0` | 🔒 限時活動起始 UTC 時間戳預留欄；Jam 版強制 `0` |
| `eventEndTimestamp` | `long` | ✓ | `0` | 🔒 限時活動結束 UTC 時間戳預留欄；Jam 版強制 `0` |

## 約束 / 不變量

- `minGuildLevel ≤ maxGuildLevel`；違反 → `StaffGachaPoolTableValidationException("poolID={id}: minGuildLevel 不得大於 maxGuildLevel")`（FT-08 §3.2）
- `eligibleStaffIDs.Count == staffWeights.Count`；不等 → 拋錯（FT-08 §3.2）
- `staffWeights[i] ≥ 0`；`sum(staffWeights) == 0` → 拋錯（池無可抽項）（FT-08 §3.2）
- `reserveTimeLimitSec > 0`（FT-08 §3.2）
- 5 個預留閘欄位（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`）任一 ≠ 預設值（Jam 版）→ `StaffGachaPoolTableValidationException("poolID={id}: Jam 版不支援此閘參數，請保持預設值")`（FT-08 §3.2）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `eligibleStaffIDs` | `StaffTable.staffID` | 弱約束（FT-08 `RollOneSlot` 驗證 candidate.staffID 是否存在；parser 不強制） |

## 變更注意事項

- 修改後需 domain reload 才生效（FT-08 §6.4）
- 新增池：分配唯一 `poolID`，`eligibleStaffIDs` 與 `staffWeights` 長度必須一致
- 修改 `minGuildLevel` / `maxGuildLevel` 閘值：影響 FT-08 可用池選擇；需同步驗證現有面試 session 的降級行為（FT-08 §5.5）
- Post-Jam 啟用預留閘：修改 DataManager 驗證邏輯，移除預設值強制檢查（FT-08 §3.2 設計意圖）

## 範例

```csv
# === StaffGachaPoolTable ===
# Jam 版 2 個池；pool 2 從 L3 開放
# 預留閘欄位全部保持預設值（Jam 版不啟用）

poolID,1,2
poolName,Common Pool,Advanced Pool
minGuildLevel,1,3
maxGuildLevel,5,5
eligibleStaffIDs,101|102|103|104,201|202|203
staffWeights,10|10|10|1,10|10|5
reserveTimeLimitSec,604800,604800
storyFlagRequired,"",""
factionIDRequired,0,0
minReputation,0,0
eventStartTimestamp,0,0
eventEndTimestamp,0,0
```
（每個欄位一列；2 筆池記錄，數值對齊 FT-08 §3.2 Jam 版範例與 §7.1.3 調參；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（FT-08 §7.1.3）

| 欄位 | 安全範圍 | 說明 |
|---|---|---|
| `minGuildLevel` / `maxGuildLevel` | [1, 5] | 與公會等級系統（FT-06）對齊 |
| `staffWeights` per item | [0, 1000] | 相對權重；0 = 保留不抽（已錄用但保留在池） |
| `reserveTimeLimitSec` | [3600, 604800]（1h~7d）| 過短玩家看不到；過長占用保留 slot |
