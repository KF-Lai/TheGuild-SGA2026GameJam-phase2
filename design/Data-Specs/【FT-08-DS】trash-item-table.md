# 【FT-08-DS】TrashItemTable

面試 1★ 層的「空抽」物品池，定義各 trash item 的名稱與風味文字，為候選卡增加世界觀紋理而非職員實體。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/TrashItemTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-08 GachaSystem 的 `RegisterTables()`（FT-08 §6.4）
- **資料類別**：`TrashItemData`（GDD 未指定完整 namespace；FT-08 §3.5.2）
- **讀取 API**：`DataManager.Get<TrashItemData>(trashItemID)` / `GetAll<TrashItemData>()`
- **消費者**：
  - FT-08 GachaSystem：`rollFromTrashItemTable()` 於 `RollOneSlot` step 4（§3.3.3 / §3.5.3）；`RestoreFromSave` 驗證 `trashItemID` 存在（§6.7）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `trashItemID` | int | ✓ | parser：≥ 1（GDD §3.5.2）；DS 慣例：≥ 9001（與 StaffTable ID 空間隔離，待升級至 GDD） | PK；唯一識別碼；不可與任一 `StaffTable.staffID` 衝突 |
| `name` | string | ✓ | 非空 | 物品名稱（顯示用，例：「咖啡杯」「履歷紙團」）|
| `flavorText` | string | ✓ | 非空 | 風味文字（writer agent 提供；呼應「面試會收到奇怪履歷」世界觀）|

## 約束 / 不變量

- `trashItemID ≥ 1`；不可與任一 `StaffTable.staffID` 衝突（違反拋 `TrashItemTableValidationException("trashItemID={id} collides with StaffTable")`）（FT-08 §3.5.2）
- `name` / `flavorText` 非空、非 null（FT-08 §3.5.2）
- PK unique（同表內）（FT-08 §3.5.2）
- `TrashItemTable.csv` 為空時，`ShouldRollTrash()` 跳過 trash roll；1★ 層結果全部為 staff（FT-08 §5.4）
- 建議數量 5~15 個（§7.1.4）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `trashItemID` | `StaffTable.staffID`（FT-12 owner）| 反向衝突驗證（DataManager 載入時主動驗證；弱約束，Jam 版以 `trashItemID ≥ 9001` 慣例隔離 ID 空間）|

## 變更注意事項

- 新增 trash item 後下次 `rollFromTrashItemTable()` 即時生效（均等加入 roll 池，`GetAll` 全表等權重）
- 修改 `name` / `flavorText` 於 DataManager 重載後即時生效；不影響已存在的 `CandidateCard`（`trashItemID` 靜態儲存，UI 顯示時 lookup）
- 刪除 `trashItemID` 後若 `StaffPlayerState.reservedCandidates` 中仍有對應 `trashItemID` 的舊卡 → `RestoreFromSave` 拋 `CandidateCardValidationException`（FT-08 §6.7 critical 路徑）

## 範例

```csv
# === TrashItemTable ===
# 1★ 層「空抽」物品池；30% 機率出現（TRASH_ROLL_RATE_AT_RARITY_1 = 0.30，見 FT-08 §3.5.3）
# flavorText 由 writer agent 提供

trashItemID,9001,9002,9003,9004,9005
name,咖啡杯,履歷紙團,過期身份證,借據,退稿小說
flavorText,冷掉的咖啡，杯緣印著陌生的口紅印。,揉爛的履歷，最上方寫著「無經驗無熱情」。,拍照時的笑容已經過期五年。,不知道借了誰多少錢。,第一章寫得很好，第二章開始就崩了。
```

（每個欄位一列；5 筆記錄（§3.5.2 Jam 版範例），數值對齊 FT-08 §3.5.2 設計值；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-08 §7.1.4）

| 欄位 | 安全範圍 | 影響 |
|---|---|---|
| `trashItemID` | parser：≥ 1；DS 慣例：≥ 9001（不與 StaffTable.staffID 撞號） | DS 自訂 ID 隔離慣例；建議升級至 GDD §3.5.2 後即可移除「DS 慣例」標註 |
| `name` / `flavorText` | 含義須與「面試收到奇怪履歷」主題一致（FT-08 §7.1.4）| 偏離主題破壞世界觀紋理；過嚴肅失去 trash 幽默感 |
| 表格筆數 | 5~15 個（§7.1.4）| 過少重複感高；過多稀釋每個物品的印象 |

> Trash roll 全體機率 ≈ `baseProb[1] × TRASH_ROLL_RATE_AT_RARITY_1 = 0.40 × 0.30 = 0.12`（FT-08 §3.5.3 / §7.5）；調整數量不影響 roll 機率，每個物品均等被選中。
