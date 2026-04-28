# 【FT-12-DS】StaffTuning

FT-12 StaffSystem owner 的系統常數 key-value 表（FT-08 GachaSystem 為消費端引用），集中管理保底閾值、trash 機率、auto refresh 間隔、effect 聚合上限、狀態切換冷卻、auto-leave 自節流間隔等可調參數，避免 SystemConstants 因子系統爆量。

> **Owner 變更紀錄（2026-04-28，T5 裁決）**：原 `【FT-08-DS】staff-tuning.md` 標為 FT-08 / FT-12 共用 owner；T5 排查批次裁決後 owner 統一為 FT-12（不再共用），DS 檔名前綴從 `【FT-08-DS】` 改為 `【FT-12-DS】`，FT-08 改標為消費端引用。實作端 CSV 欄位、key 命名、解析方式皆不變；CSV 檔案路徑 `StaffTuning.csv` 不變。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffTuning.csv`
- **Owner**：FT-12 StaffSystem
- **消費端**：FT-08 GachaSystem
- **解析方式**：`CsvParser.ParseSystemConstants`（key-value 格式；**僅解析第一組** `key` 列 + 緊接的 `value` 列；後續任何 `key` / `value` / 其他列一律被忽略，**不支援多區塊**）
- **註冊位置**：FT-12（owner）於 `RegisterTables()` 註冊本表；FT-08（消費端）不另行註冊，直接以 `DataManager.GetInt` / `GetFloat` 查詢
- **資料類別**：無（key-value 表，由各消費者以 `DataManager.GetInt` / `GetFloat` 查詢）
- **讀取 API**：`DataManager.GetInt("KEY")` / `DataManager.GetFloat("KEY")`（消費者依 key 名稱查詢）
- **消費者**：
  - FT-12 StaffSystem（owner）：`EFFECT_MAX_*`（§4.1.3）/ `BUILDING_SWITCH_COOLDOWN_SECONDS`（§3.6.4）/ `REALLOCATING_AUTO_LEAVE_SECONDS`（§3.7）/ `AUTO_LEAVE_SCAN_INTERVAL_SECONDS`（§3.7.3 自節流掃描間隔，T18 補登）/ `ROSTER_CAP`（Post-Jam §3.3.4）
  - FT-08 GachaSystem（消費端）：`PITY_THRESHOLD`（§3.4 / §4.1.8）/ `TRASH_ROLL_RATE_AT_RARITY_1`（§3.5.3）/ `MIN_AUTO_REFRESH_INTERVAL_SEC`（§4.1.3）/ `INTERVIEW_AUTO_REFRESH_INTERVAL_L*`（§4.1.3 / §6.5）/ `INTERVIEW_SLOT_COUNT_L*`（§7.2）/ `MAX_RESERVE_FALLBACK`（§4.1.9）

## 欄位定義

key-value 表無固定資料欄位；schema 為：

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| `key` | string (PK) | ✓ | 常數識別名稱（全大寫 + 底線，各 owner 分組）|
| `value` | string | ✓ | 常數值（消費者以型別轉換讀取）|
| `description` | string | ✗ | 說明文字；`ParseSystemConstants` **不解析此列**（parser 找到 `key` 列後只讀緊接的 `value` 列就結束欄位映射），保留為人類可讀備註。實務上將 description 列放在 value 列**之後**且不再放任何 `key` / `value` 列，避免擾動 parser |

## 約束 / 不變量

- `key` PK unique（同表內）；新增 key 須在本 DS §附錄「已註冊 key 清單」同步登記並標註 owner GDD（data-index.md 注意事項 4）
- 不可刪除已被 runtime 消費的 key；僅可修改 `value`（data-index.md 注意事項 4）
- **CSV 結構**：整檔僅一組 `key` 列 + 一組 `value` 列（`ParseSystemConstants` 限制）；FT-12 owner 與 FT-08 消費端 key 透過 `description` 列或 `#` 註解列分組標註，**不可**用「另起一組 `key,...` 列」分隔，否則第二組以後全部讀不到

## 雙源真相與優先序

部分 key 在其他資料表也有對應欄位，runtime 消費時以其他表為準，本表為設計參考鏡像：

| key | runtime 真相來源 | 本表角色 |
|---|---|---|
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1` ~ `L5` | `BuildingTable[buildingID=6].upgradeData[L].effectValue`（FT-08 §4.1.3 / §7.3）| 設計鏡像；CI 應檢查兩處數值一致 |
| `INTERVIEW_SLOT_COUNT_L1` ~ `L5` | `StaffRefreshCostTable[guildLevel].interviewSlotCount`（FT-08 §4.1.1）| 設計鏡像；CI 應檢查兩處數值一致 |

> 衝突時 runtime 程式碼一律走「真相來源」表；本表對應 key 的存在僅為設計師調參時的單一視窗。

## Cross-ref

| key | 引用 | 引用方式 |
|---|---|---|
| `PITY_THRESHOLD` | FT-08 §4.1.8 `shouldForce5Star` 判定 | 直接查詢 |
| `TRASH_ROLL_RATE_AT_RARITY_1` | FT-08 §3.5.3 `ShouldRollTrash()` | 直接查詢 |
| `MIN_AUTO_REFRESH_INTERVAL_SEC` | FT-08 §4.1.3 `autoRefreshIntervalSec` floor 截斷 | 直接查詢 |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | FT-12 §4.1.3 `GetRecruitRefreshReductionSec()` cap | FT-08 §4.1.3 間接引用 |
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | FT-12 §3.6.4 進入 Working transition 觸發冷卻 | 直接查詢 |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | FT-12 §3.7 自動轉休假條件 | 直接查詢 |
| `AUTO_LEAVE_SCAN_INTERVAL_SECONDS` | FT-12 §3.7.3 OnMinuteTick 自節流掃描間隔 | 直接查詢（T18 補登） |

## 變更注意事項

- 修改任何 key 的 `value` 於 DataManager 重載後即時生效
- `PITY_THRESHOLD` 調低影響 5★ 稀有性；調高讓保底無意義（FT-08 §7.2）
- `TRASH_ROLL_RATE_AT_RARITY_1` 超過 0.50 時 trash 變主要挫折來源，不建議（FT-08 §7.5）
- `MIN_AUTO_REFRESH_INTERVAL_SEC` 下調超出 `INTERVIEW_AUTO_REFRESH_INTERVAL_L5` 才有實際效果（FT-08 §4.1.3）
- `BUILDING_SWITCH_COOLDOWN_SECONDS` 設為 0 允許玩家任意切建築，破壞職員指派決策壓力（FT-12 §7.2）
- `AUTO_LEAVE_SCAN_INTERVAL_SECONDS` 過小（< 60s）會頻繁掃描造成 OnMinuteTick 負擔，過大（> 7200s）使 auto-leave 觸發延遲明顯（FT-12 §3.7.3）
- `ROSTER_CAP` 為 Post-Jam 預留，Jam 版不啟用（FT-12 §3.3.4：Jam 版不限）

## 範例

```csv
# === StaffTuning ===
# Owner = FT-12 StaffSystem；FT-08 GachaSystem 為消費端引用
# 整檔僅一組 key 列 + 一組 value 列；owner 分組以 # 註解列說明（parser 忽略註解）
# FT-12 owner: BUILDING_SWITCH_COOLDOWN_SECONDS / REALLOCATING_AUTO_LEAVE_SECONDS / AUTO_LEAVE_SCAN_INTERVAL_SECONDS / EFFECT_MAX_*
# FT-08 消費端: PITY_THRESHOLD / TRASH_ROLL_RATE_AT_RARITY_1 / MIN_AUTO_REFRESH_INTERVAL_SEC / MAX_RESERVE_FALLBACK / INTERVIEW_*

key,PITY_THRESHOLD,TRASH_ROLL_RATE_AT_RARITY_1,MIN_AUTO_REFRESH_INTERVAL_SEC,BUILDING_SWITCH_COOLDOWN_SECONDS,REALLOCATING_AUTO_LEAVE_SECONDS,AUTO_LEAVE_SCAN_INTERVAL_SECONDS
value,60,0.30,3600,21600,43200,3600
description,FT-08 §4.1.8 保底閾值,FT-08 §3.5.3 1★ trash 機率,FT-08 §4.1.3 自動刷新最低間隔秒,FT-12 §3.6.4 進入 Working 冷卻秒,FT-12 §3.7 自動轉休假閾值秒,FT-12 §3.7.3 OnMinuteTick 自節流掃描間隔秒
```

（key-value 格式；整檔**僅一組** `key` 列 + 一組 `value` 列；`description` 列為人類備註，parser 不解析；owner 分組以 `#` 註解列標註；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

> **新增 key 操作流程**：在現有 `key` 列末端追加 column → `value` 列同位置追加值 → `description` 列同位置追加備註 → 在「已註冊 key 清單」附錄登記 owner GDD。**禁止**另起新的 `key,...` 列。

## 附錄

### 已註冊 key 清單

#### FT-12 owner（FT-12 §7.2 / §4.1.3 / §3.7.3）

| key | 型別 | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|---|
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | int | `21600`（6h）| `[0, 86400]` | 進入 Working transition 觸發冷卻；過短允許頻繁切換 effect、過長挫折感（FT-12 §7.2）|
| `REALLOCATING_AUTO_LEAVE_SECONDS` | int | `43200`（12h）| `[3600, 86400]` | 自動轉休假觸發（具 slot 能力職員）；過短不友善（剛雇就被轉假）、過長無效（FT-12 §7.2）|
| `AUTO_LEAVE_SCAN_INTERVAL_SECONDS` | int | `3600`（1h）| `[60, 7200]` | OnMinuteTick 自節流掃描間隔（秒）；T18 補登，原為 GDD §3.7.3 文字描述「每 3600s」未進表（FT-12 §3.7.3）|
| `EFFECT_MAX_WILLINGNESS_BONUS` | float | `0.20` | （GDD 未指定）| Willingness 效果加總上限（FT-12 §4.1.3）|
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | float | `0.10` | （GDD 未指定）| 會計傭金效果加總上限（FT-12 §4.1.3）|
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | float | `-0.10` | （GDD 未指定）| 會計賠償效果加總下限（負值；FT-12 §4.1.3）|
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | int | `14400`（4h）| （GDD 未指定）| 招募刷新減量加總上限（秒）；影響 FT-08 §4.1.3 `staffReduction` cap（FT-12 §4.1.3）|
| `ROSTER_CAP` | int | （GDD 未指定）| — | 名冊上限（Post-Jam 預留；Jam 版 `HireStaff` 不限名冊數量，FT-12 §3.3.4）|

#### FT-08 消費端（FT-08 §7.2 / §4.1.3 / §6.5）

| key | 型別 | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|---|
| `PITY_THRESHOLD` | int | `60` | `[40, 100]` | 保底觸發閾值；過低 5★ 太常見失去稀有性、過高失去保底意義（FT-08 §7.2）|
| `TRASH_ROLL_RATE_AT_RARITY_1` | float | `0.30` | `[0.0, 1.0]` | 1★ 層 trash 觸發機率；過低（= 0）失去世界觀紋理、過高（> 0.5）成主要挫折來源（FT-08 §7.2）|
| `MIN_AUTO_REFRESH_INTERVAL_SEC` | int | `3600` | `[1800, 7200]` | 自動刷新間隔 floor 截斷值；防止 FT-12 職員加成疊加使刷新近乎即時（FT-08 §4.1.3）|
| `MAX_RESERVE_FALLBACK` | int | `1` | `[1, 1]` | `max(1, N - 1)` 在 N=1（面試欄 = 1）時的明示 fallback 常數；邏輯等價（FT-08 §4.1.9 / §8.3 B-05）|
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1` | int | `86400` | `[3600, 86400]` | 職員休息室 L1 基礎 auto refresh 間隔（秒）；FT-07 §7.1 同步（FT-08 §7.2）|
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L2` | int | `64800` | （GDD §7.2 未明列範圍，值引自 §4.1.3 FT-07 階梯）| 職員休息室 L2 基礎 auto refresh 間隔（秒） |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L3` | int | `43200` | （GDD §7.2 未明列範圍，值引自 §4.1.3 FT-07 階梯）| 職員休息室 L3 基礎 auto refresh 間隔（秒） |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L4` | int | `28800` | （GDD §7.2 未明列範圍，值引自 §4.1.3 FT-07 階梯）| 職員休息室 L4 基礎 auto refresh 間隔（秒） |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L5` | int | `21600` | `[3600, 86400]` | 職員休息室 L5 基礎 auto refresh 間隔（秒，最短）；FT-07 §7.1 同步（FT-08 §7.2）|
| `INTERVIEW_SLOT_COUNT_L1` | int | `3` | `[1, 10]` | 職員休息室 L1 面試欄張數（FT-08 §7.2；runtime 從 `StaffRefreshCostTable[1].interviewSlotCount` 讀取，見 §4.1.1）|
| `INTERVIEW_SLOT_COUNT_L2` | int | `3` | `[1, 10]` | 職員休息室 L2 面試欄張數 |
| `INTERVIEW_SLOT_COUNT_L3` | int | `4` | `[1, 10]` | 職員休息室 L3 面試欄張數 |
| `INTERVIEW_SLOT_COUNT_L4` | int | `4` | `[1, 10]` | 職員休息室 L4 面試欄張數 |
| `INTERVIEW_SLOT_COUNT_L5` | int | `5` | `[1, 10]` | 職員休息室 L5 面試欄張數 |

> **runtime 授權來源說明**：`INTERVIEW_AUTO_REFRESH_INTERVAL_L*` 的 runtime 讀取路徑為 `BuildingTable[buildingID=6].upgradeData[L].effectValue`（FT-08 §4.1.3 / §7.3）；`INTERVIEW_SLOT_COUNT_L*` 的 runtime 讀取路徑為 `StaffRefreshCostTable[guildLevel].interviewSlotCount`（§4.1.1）。StaffTuning 中的對應 key 為設計參數交叉對照用，確保兩表數值一致。
