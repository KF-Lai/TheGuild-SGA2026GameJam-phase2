# 【F-01-DS】SystemConstants

跨系統共用的 key-value 常數表。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/SystemConstants.csv`
- **解析方式**：`CsvParser.ParseSystemConstants`（特殊 key/value 解析，與一般 `Parse` 不同）
- **註冊位置**：由各消費者系統呼叫 `DataManager.RegisterSystemConstantsTable("SystemConstants")`（目前由 F-02 TimeSystem 註冊）
- **讀取 API**：`DataManager.Instance.GetInt(key)` / `GetFloat(key)` / `GetString(key)` / `GetBool(key)`
- **消費者**：
  - F-02 TimeSystem：`DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS`
  - F-03 ResourceManagement：`GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX`
  - C-01 MissionDatabase：`ESCORT_TYPE_ID`、`FACTION_NEUTRAL_ID`、`ESCORT_DURATION_MULTIPLIER_MIN`、`ESCORT_DURATION_MULTIPLIER_MAX`
  - C-02 AdventurerManagement：`WOUNDED_RECOVERY_HOURS`
  - FT-01 Recruitment：`RECRUIT_POOL_SIZE`、`DAILY_FREE_REFRESH`、`REFRESH_COST`、`MIN_RECRUIT_REFRESH_INTERVAL_SEC`
  - FT-02 MissionDispatch：`STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY`
  - FT-03 NpcDecision：`DEATH_AVERSION`、`ACCEPTANCE_THRESHOLD`、`WILLINGNESS_JITTER`、`AUTO_PICKUP_IDLE_MINUTES`、`AUTO_PICKUP_INTERVAL_MINUTES`（後兩項 minutes 為 FT-03 Tech Debt，載入時轉秒；Phase 2 應遷移為 `_SECONDS` key）
  - FT-04 OutcomeResolution：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`
  - FT-05 GuildGoldFlow：`COMMISSION_RATE`、`PENALTY_RATE`

## 欄位定義

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| key | string | ✓ | 常數名稱，UPPER_SNAKE_CASE，全專案唯一 |
| value | string | ✓ | 字串形式的值，由消費者決定如何解析（`GetInt` 等） |
| description | string | ✗ | 人類可讀說明，**parser 會忽略**，純文件用途 |

> 說明：parser 只認得 `key` 與 `value` 欄位（大小寫不敏感、順序不限）。其他欄位（如 `description`、`owner`、`unit`）一律忽略，可自由新增供設計師備註。

## 約束 / 不變量

- `key` 必須全專案唯一（重複時後者覆蓋前者，並產生 warning）
- `key` 命名一律 `UPPER_SNAKE_CASE`（與 C# `const` 慣例對齊）
- `value` 若為時間，**單位必須是「秒」或「小時」**（見 memory `feedback_time_units.md`），key 名稱要明示單位（如 `OFFLINE_MAX_SECONDS`、`BANKRUPTCY_GRACE_HOURS`）
- 新增 key 時必須同時更新本規格書下方「已註冊 key 清單」與消費者系統的 GDD

## Cross-ref

無（純設定值，不引用其他表）。

## 變更注意事項

- 修改 `value` 即時生效（重啟遊戲後讀取）
- 刪除 key 前必須先確認所有消費者已不再 `GetInt`/`GetFloat` 該 key，否則 fallback 到 default 值（通常為 `0`）
- 改 key 名稱屬 breaking change，需同步改所有消費者程式碼

## 已註冊 key 清單

| key | 型別 | 範圍 | 預設值 | 用途 | 消費者 |
|---|---|---|---|---|---|
| DAILY_RESET_HOUR | int | 0~23 | 0 | 每日重置 UTC 小時 | F-02 |
| OFFLINE_MAX_SECONDS | long | >0 | 604800 | 離線時間上限（秒）| F-02 |
| GOLD_INITIAL | int | ≥0 | 200 | 公會初始金幣 | F-03 |
| GOLD_MAX | int | >0 | 9999999 | 金幣上限 | F-03 |
| REPUTATION_MIN | int | <0 | -100 | 聲望下限 | F-03 |
| REPUTATION_MAX | int | >0 | 100 | 聲望上限 | F-03 |
| ESCORT_TYPE_ID | int | >0 | 4 | 護送任務 typeID | C-01 |
| FACTION_NEUTRAL_ID | int | ≥0 | 0 | 中立陣營 ID | C-01 |
| ESCORT_DURATION_MULTIPLIER_MIN | float | [1.0, 10.0] | 3.0 | 護送任務時長乘數下限 | C-01 |
| ESCORT_DURATION_MULTIPLIER_MAX | float | [1.0, 10.0] | 5.0 | 護送任務時長乘數上限（>= MIN）| C-01 |
| WOUNDED_RECOVERY_HOURS | int | [1, 168] | 24 | 受傷自動恢復小時數 | C-02 |
| RECRUIT_POOL_SIZE | int | [1, 10] | 4 | 招募池大小（新手/老手各一池）| FT-01 |
| DAILY_FREE_REFRESH | int | [0, 5] | 1 | 每日免費刷新次數 | FT-01 |
| REFRESH_COST | int | ≥0 | 50 | 付費刷新金幣成本 | FT-01 |
| MIN_RECRUIT_REFRESH_INTERVAL_SEC | int | >0 | 28800 | 自動刷新間隔下限（秒，8h）| FT-01 |
| STRONG_TYPE_BONUS | float | [0.0, 1.0] | 0.20 | 職業擅長類型成功率加成 | FT-02 |
| WEAK_TYPE_PENALTY | float | [0.0, 1.0] | 0.15 | 職業弱點類型成功率減成 | FT-02 |
| DEATH_AVERSION | float | [0.0, 5.0] | 1.5 | NPC 死亡率厭惡係數 | FT-03 |
| ACCEPTANCE_THRESHOLD | float | [-1.0, 1.0] | 0.0 | NPC 自主接單門檻 | FT-03 |
| WILLINGNESS_JITTER | float | [0.0, 0.5] | 0.10 | NPC 意願擲骰隨機幅度 | FT-03 |
| AUTO_PICKUP_IDLE_MINUTES | int | [1, 1440] | 10 | NPC 自主接單前 Idle 等待分鐘（Tech Debt：載入時 ×60 轉秒；Phase 2 應改 `AUTO_PICKUP_IDLE_SECONDS`）| FT-03 |
| AUTO_PICKUP_INTERVAL_MINUTES | int | [1, 1440] | 30 | NPC 自主接單冷卻分鐘（Tech Debt：載入時 ×60 轉秒）| FT-03 |
| DEATH_RATE_ON_SUCCESS_MULTIPLIER | float | [0.0, 1.0] | 0.5 | 任務成功時死亡率折扣係數（FSD-Codex-Reoprts-260427 CT-08 裁決：FSD §5.4 fallback 0.5 為準；GDD AC-OR-02 寫 1.0 已於 FT-04 §8.5 登記為衝突）| FT-04 |
| COMMISSION_RATE | float | [0.05, 0.50] | 0.20 | 委託基礎傭金率 | FT-05 |
| PENALTY_RATE | float | [0.05, 0.30] | 0.10 | 委託失敗賠償率 | FT-05 |

## 範例

```csv
# === 系統常數 ===
# parser 只認 key 與 value 欄位；description 純供設計師備註

key,DAILY_RESET_HOUR,OFFLINE_MAX_SECONDS,GOLD_INITIAL,GOLD_MAX,REPUTATION_MIN,REPUTATION_MAX
value,0,604800,200,9999999,-100,100
description,每日重置 UTC 小時（0~23）,離線時間上限（7 天 = 604800 秒）,公會初始金幣,金幣上限,聲望下限,聲望上限

# 下面這條暫時關掉（測試用）
#STARTING_DEBT,500,起始負債
```
