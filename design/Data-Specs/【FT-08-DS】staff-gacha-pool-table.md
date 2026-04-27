# 【FT-08-DS】StaffGachaPoolTable

面試 gacha 的多池架構定義表，管理每個池的開放閘控、可抽職員清單、層內 roll 權重及保留時限。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffGachaPoolTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 的 `RegisterTables()`（FT-08 §6.4）
- **資料類別**：`StaffGachaPoolData`（GDD 未指定完整 namespace；FT-08 §3.2）
- **讀取 API**：`DataManager.Get<StaffGachaPoolData>(poolID)` / `GetAll<StaffGachaPoolData>()`
- **消費者**：
  - FT-08 GachaSystem：池架構 / 主閘過濾 / `staffWeights` 層內 roll / `reserveTimeLimitSec` 保留時限判定（§3.2, §3.3, §4.1.5, §4.1.6）
  - FT-09 FactionStorySystem：`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp` 5 個預留閘（Post-Jam 啟用）

## 欄位定義

| 欄位 | 型別 | 必填 | parser 驗證範圍 | 設計安全範圍 | 說明 |
|---|---|---|---|---|---|
| `poolID` | int | ✓ | ≥ 1（PK unique） | — | PK；唯一池識別碼（Jam 版 1 = 常駐通用 Lv1~5，2 = 中高等 Lv3~5） |
| `poolName` | string | ✓ | 非空 | — | 開發辨識名稱（不直接顯示於 UI）|
| `minGuildLevel` | int | ✓ | [1, 5] | [1, 5] | 池開放的最低公會等級 |
| `maxGuildLevel` | int | ✓ | [1, 5] 且 `≥ minGuildLevel` | [1, 5] | 池開放的最高公會等級 |
| `eligibleStaffIDs` | int[] | ✓ | 至少 1 個；每值須存在於 StaffTable | — | 池可 roll 的職員 `staffID` 清單（`\|` 分隔）|
| `staffWeights` | int[] | ✓ | `Count == eligibleStaffIDs.Count`；每值 ≥ 0；sum > 0 | 每值 [0, 1000] | 平行於 `eligibleStaffIDs`，各職員於其稀有度層內的 roll 權重；0 = 保留不抽出 |
| `reserveTimeLimitSec` | int | ✓ | > 0（GDD §3.2） | [3600, 604800]（1h~7d，§7.1.3）| 本池候選的保留時限（秒）|
| `storyFlagRequired` | string | ✗ | Jam 版必為 `""`（非預設拋例外）| `""` | 🔒 FT-09 劇情 flag ID 預留欄 |
| `factionIDRequired` | int | ✗ | Jam 版必為 `0`（非預設拋例外）| `0` | 🔒 FT-09 陣營 ID 預留欄 |
| `minReputation` | int | ✗ | Jam 版必為 `0`（非預設拋例外）| `0` | 🔒 聲望門檻預留欄 |
| `eventStartTimestamp` | long | ✗ | Jam 版必為 `0`（非預設拋例外）| `0` | 🔒 限時活動起始 UTC 時間戳預留欄 |
| `eventEndTimestamp` | long | ✗ | Jam 版必為 `0`（非預設拋例外）| `0` | 🔒 限時活動結束 UTC 時間戳預留欄 |

> **欄位範圍欄語意**：「parser 驗證範圍」為 DataManager 載入時必須驗證的硬約束（違反拋例外或跳過）；「設計安全範圍」為設計師調參指引（超出時 playtest 體驗會失衡，但 parser 不阻擋）。

## 約束 / 不變量

- `minGuildLevel ≤ maxGuildLevel`（違反拋 `StaffGachaPoolTableValidationException("poolID={id}: minGuildLevel 不得大於 maxGuildLevel")`）（FT-08 §3.2）
- `eligibleStaffIDs.Count == staffWeights.Count`（平行 CSV 長度必須一致）（FT-08 §3.2）
- 每個 `staffWeights[i] ≥ 0`；`sum(staffWeights) > 0`（全零時拋 `StaffGachaPoolTableValidationException`，池無可抽項）（FT-08 §3.2）
- `reserveTimeLimitSec > 0`（FT-08 §3.2）
- 5 個預留閘欄位（`storyFlagRequired` ~ `eventEndTimestamp`）Jam 版一律保持預設值；任一非預設值拋 `StaffGachaPoolTableValidationException("poolID={id}: Jam 版不支援此閘參數，請保持預設值")`（FT-08 §3.2）

## 跨表約束（StaffGachaPoolTable parser 跨表驗證）

- **池中至少一稀有度層非空**（FT-08 §4.1.5）：對每個 poolID 須存在 `r ∈ [1,5]` 使 `{ id ∈ eligibleStaffIDs | StaffTable[id].rarity == r ∧ staffWeights[indexOf(id)] > 0 } ≠ ∅`；全空池拋 `StaffGachaPoolTableValidationException`。此驗證需查 StaffTable.rarity，故 **DataManager 載入順序：StaffTable 先於 StaffGachaPoolTable**。
- **eligibleStaffIDs FK 存在性**：parser 不主動驗證（弱約束，與 §Cross-ref 一致）；FK 缺失於 runtime 由 `RollOneSlot`（§3.3.3）/ `RestoreFromSave`（§6.7）驗證並降級。但執行「至少一層非空」時若 `StaffTable[id]` 不存在會被視為「該 id 不貢獻任何稀有度層」處理。

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `eligibleStaffIDs` | `StaffTable.staffID`（FT-12 owner） | FK 弱約束（parser 不自動驗證；FT-08 `RollOneSlot` §3.3.3 / `RestoreFromSave` §6.7 做 runtime 驗證）|
| `factionIDRequired` | FT-09 faction ID（Post-Jam） | 預留，Jam 版無 runtime 引用 |

## 變更注意事項

- 修改 `eligibleStaffIDs` / `staffWeights` 後下次 refresh 即時生效；既有 `currentCandidates` 不受影響（FT-08 §3.2 設計註記：池開放/關閉僅影響未來面試結果）
- 修改 `minGuildLevel` / `maxGuildLevel` 後 DataManager 重新載入即生效；影響 `TrySwitchPool` 的 `POOL_LEVEL_LOCKED` 判定（FT-08 §3.3.4）
- 修改 `reserveTimeLimitSec` 只對新保留卡生效；既有 `reservedCandidates` 的到期時間以 `reservedTimestamp + reserveTimeLimitSec(c.poolID)` 動態計算（FT-08 §3.3.2 step 2）
- Post-Jam 啟用預留閘：同步移除 §3.2 DataManager 驗證邏輯 + 登記 FT-09 §6.2 下游消費（FT-08 §1 FT-09 runtime 依賴聲明）

## 範例

```csv
# === StaffGachaPoolTable ===
# Pool 1: 常駐通用（Lv1~5）；Pool 2: 中高等（Lv3~5）
# 5 個預留閘欄位一律保持預設值（Jam 版強制空值）

poolID,1,2
poolName,Common Pool,Advanced Pool
minGuildLevel,1,3
maxGuildLevel,5,5
eligibleStaffIDs,101|102|103|104|105,201|202|203
staffWeights,10|10|10|5|1,10|10|5
reserveTimeLimitSec,604800,604800
storyFlagRequired,"",""
factionIDRequired,0,0
minReputation,0,0
eventStartTimestamp,0,0
eventEndTimestamp,0,0
```

（每個欄位一列；2 筆記錄（Jam 版 2 池），數值對齊 FT-08 §3.2 Jam 版資料範例；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-08 §7.1.3）

| 欄位 | 安全範圍 | 影響 |
|---|---|---|
| `minGuildLevel` / `maxGuildLevel` | [1, 5]；`min ≤ max` | 玩家可切換此池的等級區間 |
| `eligibleStaffIDs` 各值 | StaffTable 中存在的合法 staffID | 決定池的「定性」（高等池靠 staffID 的 rarity 分布隱式決定）|
| `staffWeights` 各值 | [0, 1000]；sum > 0 | 稀有度層內各職員的出現比例；0 = 暫時下架 |
| `reserveTimeLimitSec` | [3600, 604800]（1h~7d） | 候選保留時限；過短讓玩家措手不及、過長減少「返回確認」的決策壓力 |

### Phase 標記

5 個預留閘欄位（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`）為 Post-Jam FT-09 Faction Story 啟用路徑而保留；Jam 版 DataManager 強制驗證全部為預設值。詳見 FT-08 §1「FT-09 runtime 依賴聲明」。
