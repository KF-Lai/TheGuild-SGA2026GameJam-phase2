# 【FT-08-DS】StaffRefreshCostTable

面試手動刷新費用與面試欄張數的等級索引表，依公會等級決定每次手動刷新的金幣成本及候選展示張數 N。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffRefreshCostTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 的 `RegisterTables()`（FT-08 §6.4）
- **資料類別**：`StaffRefreshCostData`（GDD 未指定完整 namespace；FT-08 §3.3.6）
- **讀取 API**：`DataManager.Get<StaffRefreshCostData>(guildLevel)`
- **消費者**：
  - FT-08 GachaSystem：`cost` 用於手動刷新費扣款（`TryManualRefresh`，§3.3.4 / §4.1.2）；`interviewSlotCount` 決定面試欄張數 N（§3.3.2 step 3 / §4.1.1）

## 欄位定義

| 欄位 | 型別 | 必填 | parser 驗證範圍 | 設計安全範圍 | 說明 |
|---|---|---|---|---|---|
| `guildLevel` | int | ✓ | 連續 1~5（缺行拋例外） | [1, 5]（Jam 版） | PK；公會等級索引 |
| `cost` | int | ✓ | ≥ 0（負值拋例外） | [0, 5000]（§7.1.1） | 手動刷新費（gold）；過低失去金幣壓力、過高玩家不刷 |
| `interviewSlotCount` | int | ✓ | [1, 5] | [3, 5]（§7.1.1） | 該等級的面試欄張數 N；N 決定每次 refresh 展示的候選數量 |

## 約束 / 不變量

- `guildLevel` PK 連續 1~5（Jam 版）；缺行拋 `StaffRefreshCostTableValidationException("missing guildLevel={n}")`（FT-08 §3.3.6）
- `cost ≥ 0`（FT-08 §3.3.6）
- `1 ≤ interviewSlotCount ≤ 5`（FT-08 §3.3.6）

> **GDD 矛盾仲裁紀錄**：FT-08 GDD §3.3.6（schema）要求缺行拋例外；§5.1（edge cases）原為「LogError + fallback cost = 0」。2026-04-27 仲裁採 §3.3.6 fail-fast 路徑（與 StaffGachaPoolTable / StaffTable 等 critical data 處理一致），FT-08 §5.1 已同步修正。

## Cross-ref

無

## 變更注意事項

- `cost` 修改後下次 `TryManualRefresh` 即時生效；不影響已展示的 `currentCandidates`
- `interviewSlotCount` 修改後，N 值在下次 `ExecuteRefresh` 的 step 3 重新讀取；若新 N < 舊 N，超出範圍的 slot 在補刷判定前 truncate（FT-08 §5.6）
- 影響 `maxReserve = max(1, N-1)`（§4.1.9）與 `pityCounter += N` 累加節奏（§4.1.7）

## 範例

```csv
# === StaffRefreshCostTable ===
# 手動刷新費（cost）與面試欄張數（interviewSlotCount）依公會等級遞增
# 對齊 FT-08 §3.3.6 Jam 版範例

guildLevel,1,2,3,4,5
cost,100,150,250,400,600
interviewSlotCount,3,3,4,4,5
```

（每個欄位一列；5 筆記錄（等級 1~5），數值對齊 FT-08 §3.3.6 Jam 版範例；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-08 §7.1.1）

| guildLevel | cost 預設 | cost 安全範圍 | interviewSlotCount 預設 |
|---|---|---|---|
| 1 | 100 | [0, 5000] | 3 |
| 2 | 150 | [0, 5000] | 3 |
| 3 | 250 | [0, 5000] | 4 |
| 4 | 400 | [0, 5000] | 4 |
| 5 | 600 | [0, 5000] | 5 |

> `interviewSlotCount` 的安全範圍由 §3.3.6 驗證規則限定為 [1, 5]；`cost` 安全範圍來自 §7.1.1。
