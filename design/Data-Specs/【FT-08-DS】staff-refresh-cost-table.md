# 【FT-08-DS】StaffRefreshCostTable

各公會等級的面試手動刷新費與面試欄張數配置表，供 FT-08 計算手動刷新金幣消耗與面試欄數 N。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffRefreshCostTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 初始化（DataManager bootstrap；FT-08 §6.4 資料表依賴）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.Get<StaffRefreshCostData>(guildLevel)` 單筆查詢；FT-08 §4.1.2 引用 `cost`，§4.1.1 引用 `interviewSlotCount`（FT-08 §3.3.6）
- **消費者**：
  - FT-08 GachaSystem：`TryManualRefresh` 扣款查詢（§4.1.2）；面試欄張數 N 查詢（§4.1.1 / §3.3.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `guildLevel` | `int` (PK) | ✓ | 1~5 | 公會等級索引（Jam 版覆蓋 1~5） |
| `cost` | `int` | ✓ | [0, 5000] | 手動刷新費（gold）；`0` = 免費刷新 |
| `interviewSlotCount` | `int` | ✓ | [1, 5] | 該等級面試欄張數 N（§4.1.1） |

## 約束 / 不變量

- `guildLevel` PK 連續 1~5（Jam 版）；缺行 → `StaffRefreshCostTableValidationException("missing guildLevel={n}")`（FT-08 §3.3.6）
- 缺行時 DataManager fallback：`cost = 0`（玩家可免費刷新，臨時降級）（FT-08 §5.4）
- `cost ≥ 0`（FT-08 §3.3.6）
- `1 ≤ interviewSlotCount ≤ 5`（FT-08 §3.3.6）

## Cross-ref

無

## 變更注意事項

- 修改後需 domain reload 才生效
- 調整 `cost`：連動 FT-08 §4.1.2 手動刷新費公式及玩家金幣消耗節奏；參考 §7.1.1 安全範圍
- 調整 `interviewSlotCount`：影響每次展示的候選張數 N，連動 §4.1.7 pityCounter 增量（`pityCounter += N` per refresh）
- Jam 版 `guildLevel` 範圍 1~5；Post-Jam 擴充等級時需同步新增行並更新驗證邏輯

## 範例

```csv
# === StaffRefreshCostTable ===
# Jam 版 5 個公會等級的刷新費與面試欄數
# 對齊 FT-08 §3.3.6 Jam 預設值

guildLevel,1,2,3,4,5
cost,100,150,250,400,600
interviewSlotCount,3,3,4,4,5
```
（每個欄位一列；5 筆等級記錄，數值對齊 FT-08 §3.3.6 Jam 版範例；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（FT-08 §7.1.1）

| 欄位 | Jam 預設值 | 安全範圍 | 說明 |
|---|---|---|---|
| `cost` | 100 / 150 / 250 / 400 / 600 | [0, 5000] | 過低（=0）失去金幣壓力；過高玩家不刷 |
| `interviewSlotCount` | 3 / 3 / 4 / 4 / 5 | [1, 5] | 控制每次展示候選數 N（與 `pityCounter += N` 連動） |
