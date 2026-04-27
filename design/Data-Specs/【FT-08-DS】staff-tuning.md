# 【FT-08-DS】StaffTuning

FT-08 GachaSystem 與 FT-12 StaffSystem 共用的系統常數 key-value 表，集中儲存 gacha 保底閾值 / 自動刷新間隔 / 職員冷卻秒數等調參；與 SystemConstants.csv 切分，避免子系統常數污染全局命名空間。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffTuning.csv`
- **解析方式**：`CsvParser.ParseSystemConstants`（key-value 表，亦為 column-based）
- **註冊位置**：FT-08 GachaSystem 與 FT-12 StaffSystem 各自初始化時讀取自身 key（FT-08 §7.2 / FT-12 §7.2）
- **資料類別**：無（key-value 表，直接以 `string key → value` 存取）
- **讀取 API**：（GDD 未指定確切方法名稱；行為同 SystemConstants key-value 查詢）
- **消費者**：
  - FT-08 GachaSystem：gacha 保底 / 刷新間隔 / 面試欄相關常數（FT-08 §7.2）
  - FT-12 StaffSystem：職員冷卻 / 自動轉休假相關常數（FT-12 §7.2）

## 欄位定義

key-value 格式；每筆記錄由 `key`（string PK）與 `value`（string，由消費者解析為目標型別）組成。完整已註冊 key 清單見「附錄」。

## 約束 / 不變量

- `key` 為非空 string；PK unique（同表內）
- 新增 key 時須在 DS 附錄「已註冊 key 清單」同步更新，並標明 owner GDD（data-index.md 注意事項 #4）
- `EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` 定義於 FT-12 §4.1，但 §7.2 未明確指定存於 StaffTuning.csv；具體存放表格待 FT-12 實作確認（gdd-gap：`EFFECT_MAX_*@StaffTuning`）
- `INTERVIEW_SLOT_COUNT_L1~L5` 列於 FT-08 §7.2，但 `interviewSlotCount` 亦存於 `StaffRefreshCostTable`（§3.3.6）；GDD 未指明兩者優先來源（gdd-gap：`INTERVIEW_SLOT_COUNT_L1~L5@StaffTuning`）

## Cross-ref

無

## 變更注意事項

- 修改後需 domain reload 才生效
- 修改 `PITY_THRESHOLD`：影響 FT-08 §3.4 / §4.1.8 保底觸發頻率
- 修改 `INTERVIEW_AUTO_REFRESH_INTERVAL_L1~L5`：影響 FT-08 §4.1.3 自動刷新頻率及 §4.1.4 離線補刷次數計算
- 修改 `BUILDING_SWITCH_COOLDOWN_SECONDS`：影響 FT-12 §3.6.4 Working 狀態冷卻體驗
- 修改 `REALLOCATING_AUTO_LEAVE_SECONDS`：影響 FT-12 §3.7 自動轉休假時機
- 此表同時被 FT-08 / FT-12 讀取；調整前確認兩系統均不受影響

## 範例

```csv
# === StaffTuning ===
# FT-08 / FT-12 共用系統常數，對齊 FT-08 §7.2 + FT-12 §7.2 Jam 預設值

key,PITY_THRESHOLD,TRASH_ROLL_RATE_AT_RARITY_1,INTERVIEW_AUTO_REFRESH_INTERVAL_L1,INTERVIEW_AUTO_REFRESH_INTERVAL_L5,BUILDING_SWITCH_COOLDOWN_SECONDS,REALLOCATING_AUTO_LEAVE_SECONDS
value,60,0.30,86400,21600,21600,43200
```
（key-value 格式；完整 key 清單見附錄；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**已註冊 key 清單**

| key | owner | 預設值 | 型別 | 安全範圍 | 影響 |
|---|---|---|---|---|---|
| `PITY_THRESHOLD` | FT-08 | `60` | int | [40, 100] | 保底觸發閾值（§4.1.8）；過低讓 5★ 太常見，過高失去保底意義（FT-08 §7.2） |
| `TRASH_ROLL_RATE_AT_RARITY_1` | FT-08 | `0.30` | float | [0.0, 1.0] | 1★ 層 trash 觸發機率（§3.5.3）；過低失去 trash 紋理、過高惹人厭（FT-08 §7.2） |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1` | FT-08 | `86400`（24h）| int（秒）| [3600, 86400] | L1 自動刷新間隔（FT-08 §7.2） |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L2` | FT-08 | （GDD 未指定）| int（秒）| [3600, 86400] | L2 自動刷新間隔 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L3` | FT-08 | （GDD 未指定）| int（秒）| [3600, 86400] | L3 自動刷新間隔 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L4` | FT-08 | （GDD 未指定）| int（秒）| [3600, 86400] | L4 自動刷新間隔 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L5` | FT-08 | `21600`（6h）| int（秒）| [3600, 86400] | L5 自動刷新間隔（最短）（FT-08 §7.2） |
| `INTERVIEW_SLOT_COUNT_L1` ~ `L5` | FT-08 | `3` ~ `7` | int | [1, 10] | 面試欄張數；gdd-gap：可能與 `StaffRefreshCostTable.interviewSlotCount` 重複定義（FT-08 §7.2） |
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | FT-12 | `21600`（6h）| int（秒）| [0, 86400] | 進入 Working 時觸發冷卻秒數（FT-12 §3.6.4）；過短玩家頻繁切換 effect（FT-12 §7.2） |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | FT-12 | `43200`（12h）| int（秒）| [3600, 86400] | 自動轉休假觸發秒數（FT-12 §3.7）；對應離線 8h 醒來指派的玩家節奏（FT-12 §7.2） |
