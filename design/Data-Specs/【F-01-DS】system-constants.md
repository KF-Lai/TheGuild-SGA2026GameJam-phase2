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

| key | 型別 | 範圍 | 用途 | 消費者 |
|---|---|---|---|---|
| DAILY_RESET_HOUR | int | 0~23 | 每日重置 UTC 小時 | F-02 |
| OFFLINE_MAX_SECONDS | long | >0 | 離線時間上限（秒） | F-02 |
| GOLD_INITIAL | int | ≥0 | 公會初始金幣 | F-03 |
| GOLD_MAX | int | >0 | 金幣上限 | F-03 |
| REPUTATION_MIN | int | <0 | 聲望下限 | F-03 |
| REPUTATION_MAX | int | >0 | 聲望上限 | F-03 |

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
