# 【FT-08-DS】StaffRarityProbTable

各稀有度層的基礎抽取機率表，供 FT-08 動態歸一化後 roll 每次面試 slot 的稀有度結果。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffRarityProbTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 初始化（DataManager bootstrap；FT-08 §6.4 資料表依賴）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.GetAll<StaffRarityProbData>()` 取全層列表；FT-08 §4.1.5 動態歸一化後使用（FT-08 §4.1.5 / §7.1.2）
- **消費者**：
  - FT-08 GachaSystem：`RollOneSlot` 步驟 2 稀有度 roll（§3.3.3）；動態歸一化邏輯（§4.1.5）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rarity` | `int` (PK) | ✓ | 1~5 | 稀有度層索引（1 = 最低、5 = 最高） |
| `prob` | `float` | ✓ | [0.0, 1.0] | 基礎稀有度機率（歸一化前）；各層加總建議 = 1.0 |

## 約束 / 不變量

- 必須覆蓋全部 5 個稀有度層（1~5）；（GDD 未指定缺行時行為）
- `prob` 總和 ≠ 1.0 → `LogWarning` + 動態歸一化（§4.1.5）；不強制修正（FT-08 §5.4）
- 所有 `prob[r] ≥ 0`；（GDD 未指定違反時驗證行為）
- `emptyTiers` 不得包含全部 5 層（至少一層非空）→ DataManager 驗證；全空池 → `StaffGachaPoolTableValidationException`（FT-08 §4.1.5）

## Cross-ref

無

## 變更注意事項

- 修改後需 domain reload 才生效
- 調整 `prob` 時注意 §4.1.5 動態歸一化：runtime 自動補正總和，但建議設計時保持加總 = 1.0 以便直觀驗算
- 此表為全局稀有度基礎機率；稀有度層內各職員的相對機率由 `StaffGachaPoolTable.staffWeights` 決定（§4.1.6）

## 範例

```csv
# === StaffRarityProbTable ===
# 基礎稀有度機率，對齊 FT-08 §4.1.5 Jam 初始值
# 5 層加總 = 1.00；runtime 動態歸一化補償空層

rarity,1,2,3,4,5
prob,0.40,0.25,0.15,0.15,0.05
```
（每個欄位一列；5 筆稀有度層記錄，數值對齊 FT-08 §4.1.5 Jam 預設；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（FT-08 §7.1.2）

| 欄位 | Jam 預設值 | 安全範圍 | 說明 |
|---|---|---|---|
| `prob`（1★~5★）| 0.40 / 0.25 / 0.15 / 0.15 / 0.05 | [0.0, 1.0]；總和 = 1.0 | 5★ 機率降至 0 不影響保底機制（pityCounter 仍累積）；調高 5★ 降低 playtest 挫折感 |
