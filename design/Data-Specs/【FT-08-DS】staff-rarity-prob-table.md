# 【FT-08-DS】StaffRarityProbTable

面試稀有度基礎機率表，定義 1★~5★ 各稀有度在動態歸一化前的原始 roll 機率。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffRarityProbTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 的 `RegisterTables()`（FT-08 §6.4）
- **資料類別**：`StaffRarityProbData`（GDD 未指定完整 namespace；FT-08 §4.1.5）
- **讀取 API**：`DataManager.Get<StaffRarityProbData>(rarity)` / `GetAll<StaffRarityProbData>()`
- **消費者**：
  - FT-08 GachaSystem：`RollOneSlot` step 2 稀有度動態歸一化（§3.3.3 / §4.1.5）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rarity` | int | ✓ | [1, 5] | PK；稀有度等級（1★~5★） |
| `prob` | float | ✓ | [0.0, 1.0] | 基礎機率（`baseProb[r]`）；總和 ≠ 1.0 時 LogWarning，runtime 動態歸一化（§4.1.5）|

## 約束 / 不變量

- `rarity` PK 須涵蓋 1~5（Jam 版）
- 每個 `prob ∈ [0.0, 1.0]`（FT-08 §7.1.2）
- 總和 ≠ 1.0 時 LogWarning；DataManager 不拋例外，runtime 由 §4.1.5 動態歸一化保證 `effectiveProb` 總和 = 1.0（FT-08 §5.4）

## 跨表約束（非本表 parser 驗證）

> 以下規則由其他表的 parser / runtime 驗證，列於此處僅供 Codex 在實作 StaffGachaPoolTable parser 與 §4.1.5 動態歸一化時交叉參考；本表 parser **不**負責驗證。

- **「至少一稀有度層非空」（`|emptyTiers| < 5`）**：owner = StaffGachaPoolTable parser（與 StaffTable 聯合判定）；全空池拋 `StaffGachaPoolTableValidationException`（FT-08 §4.1.5）。
- 「空層」定義：某 `poolID` 中稀有度 `r` 的 `poolEligibleByRarity(poolID, r)` 為空集合（非 `prob == 0`）；判定依賴 `StaffGachaPoolTable.eligibleStaffIDs` × `StaffTable[id].rarity`，與本表 `prob` 無關。

## Cross-ref

無

## 變更注意事項

- 修改 `prob` 後下次 `RollOneSlot` step 2 即時生效（重算 `effectiveProb`）
- 調低 5★ 機率（如 0.05 → 0.02）使保底壓力增大、`PITY_THRESHOLD` 的配合性需重新評估（FT-08 §7.2）
- 所有 `prob` 加總後歸一化，調整時以相對比例為主，不需嚴格加總至 1.0

## 範例

```csv
# === StaffRarityProbTable ===
# 面試稀有度基礎機率，對齊 FT-08 §4.1.5 設計值
# 總和 = 1.00；動態歸一化邏輯見 FT-08 §4.1.5

rarity,1,2,3,4,5
prob,0.40,0.25,0.15,0.15,0.05
```

（每個欄位一列；5 筆記錄（1★~5★），數值對齊 FT-08 §4.1.5 `baseProb` 設計值；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-08 §7.1.2）

| rarity | 預設 prob | 安全範圍 | 影響 |
|---|---|---|---|
| 1 | 0.40 | [0.0, 1.0] | 1★ 基礎出現率；調高使低稀有度更常見、trash 全體機率隨之上升 |
| 2 | 0.25 | [0.0, 1.0] | — |
| 3 | 0.15 | [0.0, 1.0] | — |
| 4 | 0.15 | [0.0, 1.0] | 4★ 與 5★ 差距過小時削弱 5★ 稀有感 |
| 5 | 0.05 | [0.0, 1.0] | 過高（> 0.15）破壞稀有性；過低（< 0.02）讓非保底 5★ 幾乎不可能 |

> 調整時搭配 `PITY_THRESHOLD` 一起評估（FT-08 §7.2）；池中某層無職員（emptyTiers）時 §4.1.5 動態歸一化自動重分配，不需手動調整 `prob`。
