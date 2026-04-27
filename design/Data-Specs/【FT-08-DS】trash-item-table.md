# 【FT-08-DS】TrashItemTable

Trash 物品定義表，持有 1★ 層 trash roll 命中時展示的物品名稱與風味文字。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/TrashItemTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 初始化（DataManager bootstrap；FT-08 §6.4 資料表依賴）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.GetAll<TrashItemData>()` 取全列表；FT-08 §3.5.3 `ShouldRollTrash` 觸發後隨機選取（FT-08 §3.5.2）
- **消費者**：
  - FT-08 GachaSystem：`RollOneSlot` 步驟 4 trash 判定後選取 trash item（§3.5.3 / §3.5.4）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `trashItemID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別碼；不可與任一 `StaffTable.staffID` 衝突 |
| `name` | `string` | ✓ | 非空 | 物品名稱（顯示用，例：「咖啡杯」「履歷紙團」） |
| `flavorText` | `string` | ✓ | 非空 | 風味文字（writer agent 提供） |
| `iconAssetID` | `string` | ✗ | `""` | 視覺資產 ID 預留欄；Jam 版使用通用 trash icon，強制 `""` |

## 約束 / 不變量

- `trashItemID ≥ 1`；不可與任一 `StaffTable.staffID` 衝突 → `TrashItemTableValidationException("trashItemID={id} collides with StaffTable")`（FT-08 §3.5.2）
- `name` / `flavorText` 非空 / 非 null（FT-08 §3.5.2）
- PK unique（同表內）（FT-08 §3.5.2）
- `TrashItemTable.csv` 為空時，trash roll 跳過；1★ 層全部回退為正常 staff（FT-08 §5.4）
- 建議數量 5~15 個（FT-08 §3.5.2）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `trashItemID` | `StaffTable.staffID`（衝突禁止） | 弱約束（TrashItemTable DataManager 端驗證；parser 不強制） |

## 變更注意事項

- 修改後需 domain reload 才生效
- 新增 trash item：建議分配 ID ≥ 9001（慣例，避免與 staffID 衝突；FT-08 §3.5.2 範例以 9001~9005 為基準）
- 刪除 trash item：不影響現有 StaffInstance；下次 trash roll 從剩餘清單選取
- `flavorText` 內容可由 writer agent 使用 `gemini_generate_narrative` 產生初稿

## 範例

```csv
# === TrashItemTable ===
# Jam 版 trash 物品，對齊 FT-08 §3.5.2 範例
# trashItemID 從 9001 起，避免與 StaffTable.staffID 衝突

trashItemID,9001,9002,9003
name,咖啡杯,履歷紙團,過期身份證
flavorText,"冷掉的咖啡，杯緣印著陌生的口紅印。","揉爛的履歷，最上方寫著「無經驗無熱情」。",拍照時的笑容已經過期五年。
iconAssetID,"","",""
```
（每個欄位一列；3 筆範例記錄，數值對齊 FT-08 §3.5.2 Jam 版範例；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）
