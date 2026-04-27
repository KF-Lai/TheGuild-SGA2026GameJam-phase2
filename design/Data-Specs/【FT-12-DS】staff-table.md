# 【FT-12-DS】StaffTable

職員模板表：定義每位職員的稀有度、薪水、效果清單與 slot 指派能力，是 FT-12 效果聚合 API 與 FT-08 gacha 池過濾的靜態資料來源。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-12 Staff System 初始化時（§3.2）；`DataManager.GetAll<StaffData>()` 批次載入
- **資料類別**：`TheGuild.Gameplay.StaffSystem.StaffData`
- **讀取 API**：`DataManager.Get<StaffData>(key)` / `DataManager.GetAll<StaffData>()`
- **消費者**：
  - FT-12 Staff System：`HireStaff` 入職驗證（§3.3.2 Step 2）
  - FT-12 Staff System：效果聚合 API（`GetStaffWillingnessBonus` / `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` / `GetRecruitRefreshReductionSec` / `IsSuccessRatePreviewEnabled`，§3.4）
  - FT-08 Gacha System：`minGuildLevel` 候選池過濾條件之一（§3.2 / data-index 注意事項 9）
  - FT-10 Save/Load System：`staffID` FK 合法性驗證（反序列化 `StaffInstance[]` 時透過 `DataManager.Get<StaffData>` 確認，FT-10 §3.3）
  - FT-09 Faction Story System：`factionID` Post-Jam 預留（Jam 版無 runtime 依賴，§3.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `staffID` | int | ✓ | ≥ 1 | 唯一識別碼（PK） |
| `name` | string | ✓ | — | 職員顯示名稱 |
| `rarity` | int | ✓ | [1, 5] | 稀有度；決定 `effectIDs` / `effectValues` 最大數量上限（§3.2）|
| `salary` | int | ✓ | [0, 10000] | 每日薪水（金幣）；Jam 版全設 0（薪水管線 Phase 2 實作，§7.1.1）|
| `severancePay` | int | ✓ | [0, 5000] | 解雇資遣費（金幣）；Jam 版建議 1★≈50、5★≈500（§7.5）|
| `isFiller` | bool | ✓ | true / false | 平庸職員標記；共享 schema，差異以數值欄位表達（§3.2）|
| `factionID` | int | ✓ | ≥ 0（0 = neutral） | 陣營 ID；Jam 版僅保留欄位，FT-09 Post-Jam 消費（§3.2）|
| `minGuildLevel` | int | ✓ | [1, 5] | 可錄用最低公會等級；FT-08 gacha 池過濾條件之一（§7.1.1）|
| `effectIDs` | string[] | ✓ | `StaffEffect` 白名單；空 = `""` | 職員效果清單（`\|` 分隔 enum 名稱；白名單：`Willingness` / `AccountantCommission` / `AccountantPenaltyOnVault` / `RecruitRefreshOnCounter`，§3.2）|
| `effectValues` | float[] | ✓ | 依 effectID 各異（見附錄）；空 = `""` | 平行於 `effectIDs` 的加成數值；passive 為正值，penalty 類為負值；`RecruitRefreshOnCounter` 單位為秒（§3.2）|
| `slotBuildingIDs` | int[] | ✓ | buildingID ≥ 1；無 slot 能力 = `0` | 合格 slot 候選清單（`\|` 分隔）；空或全 `0` = 無 slot 指派能力（§3.2）|
| `uiFlagIDs` | string[] | ✓ | `StaffUIFlag` 白名單；空 = `""` | UI 功能旗標清單（`\|` 分隔 enum 名稱；白名單：`SuccessRatePreview`，§3.2）|
| `uiFlagBuildingIDs` | int[] | ✓ | buildingID ≥ 1；空 = `0` | 平行於 `uiFlagIDs`，旗標啟用所需的 `assignedBuildingID`（§3.2）|

> `effectIDs` / `uiFlagIDs` 為 string enum 名稱，不做 int 轉型；`effectValues` / `slotBuildingIDs` / `uiFlagBuildingIDs` 由 DataManager 拆分後轉型。
>
> **多值欄位空值規約**：`int[]` 多值欄位（`slotBuildingIDs` / `uiFlagBuildingIDs`）空值填單一 `0`，DataManager 解析後過濾為空陣列（依 `data-files.md` 通則）；`string[]` 多值欄位（`effectIDs` / `effectValues` / `uiFlagIDs`）空值填 `""`，DataManager 拆分後得空陣列。

## 約束 / 不變量

- `staffID == 0` → DataManager 載入時拋 `StaffTableValidationException`，跳過該行（§5.4）
- `staffID` 重複（同一筆 PK 出現多次）→ DataManager 通則行為（gdd-gap；GDD 未明訂，建議比照 `FactionRouteTable` 規範拋 `StaffTableValidationException` + 跳過重複行）
- `effectIDs.Count == effectValues.Count ≤ rarity 對應上限`（1★=1、2★=1、3★=2、4★=2、5★=3）；違反 → 拋 `StaffTableValidationException`（§3.2 / §5.4）
- `uiFlagIDs.Count == uiFlagBuildingIDs.Count`；違反 → 拋 `StaffTableValidationException`（§3.4.6）
- 每個 `uiFlagBuildingIDs[i]` 必須存在於該職員的 `slotBuildingIDs` 中；違反 → 拋 `StaffTableValidationException`（§3.4.6）
- `effectIDs` 中的 enum 名稱必須在 `StaffEffect` 白名單內；未列白名單 → 載入時拋錯（§3.2）
- `uiFlagIDs` 中的 enum 名稱必須在 `StaffUIFlag` 白名單內；未列白名單 → 載入時拋錯（§3.2）
- `salary < 0` → 載入時拋 `StaffTableValidationException`（§5.3，N-11 案 critical fail-fast）；`salary = 0` 為 Jam 版合法值
- `severancePay < 0` → gdd-gap（GDD 未明訂；建議比照 `salary` 拋 `StaffTableValidationException`，待 GDD owner 補入 §5.3）
- `minGuildLevel < 1` → `LogWarning` + clamp 至 1（§5.4）；`minGuildLevel > 5` → gdd-gap（GDD 未指定 clamp / 拋錯行為，§7.1.1 僅給安全範圍 [1, 5]）
- `rarity ∈ [1, 5]`；超出範圍 → gdd-gap（GDD 未直接規定，但 `effectIDs.Count ≤ rarity 對應上限` 驗證會間接捕捉 rarity > 5：未定義的上限映射造成驗證失敗）
- 同 `staffID` 可對應多個 `StaffInstance`（允許重複錄用同一模板，§3.1；與上述 PK 重複限制不衝突——前者為 runtime instance，後者為靜態表 PK）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `slotBuildingIDs` | `BuildingTable.buildingID` | 弱約束（parser 不強制；FT-12 slot 指派流程 §3.5 runtime 查詢 buildingID 合法性）|
| `uiFlagBuildingIDs` | `BuildingTable.buildingID` | 弱約束（同上；§3.4.6 intra-row 強制：`uiFlagBuildingIDs[i]` 須在同行 `slotBuildingIDs` 內）|
| `factionID` | `FactionRouteTable.factionID` | 弱約束（Jam 版 FT-09 不消費，Post-Jam 時 FT-09 需消費此欄，§3.2）|
| `staffID` | `StaffInstance.staffID` | FK 弱約束（FT-10 反序列化時透過 `DataManager.Get<StaffData>` 驗證，FT-10 §3.3）|

## 變更注意事項

- 新增 `effectID` enum 值：需同步 `StaffEffect` C# enum + §3.4 聚合演算法 + 下游 GDD（§3.2）
- 新增 `uiFlagID` enum 值：需同步 `StaffUIFlag` C# enum + §3.4.6 聚合演算法 + 消費者 GDD（§3.2）
- 修改 `effectValues` 上限：`EFFECT_MAX_*` 常數在 `StaffTuning.csv` 而非此表；聚合上限不由此表控制（§3.4.2 / §7.2）
- 修改 `rarity`：同步檢查 `effectIDs.Count ≤ 新 rarity 對應上限`；否則載入時拋 `StaffTableValidationException`
- 修改 `slotBuildingIDs`：同步驗證 `uiFlagBuildingIDs[i]` 是否仍在 `slotBuildingIDs` 內（§3.4.6 約束）
- 生效時機：重啟遊戲後（CSV 為 Resources 內嵌，runtime 不熱更新）
- `StaffTable.factionID` Post-Jam 啟用時，需同步更新 FT-09 runtime 消費邏輯（FT-09 §1 設計來源）

## 範例

```csv
# === StaffTable — 職員模板 ===
# 對應 §3.2 資料表範例（說明用，§7 調參）
# salary 欄位以下範例展示 Phase 2 預期調參梯度；Jam 版可全填 0（薪水管線未啟用，§7.1.1）

staffID,101,102,103,104,105
name,克勞德·會計師,艾蓮·委託官,蘿絲·櫃台小姐,無名小職員,咖啡杯
rarity,3,2,2,1,1
salary,40,30,30,10,0
severancePay,120,80,80,10,0
isFiller,false,false,false,true,true
factionID,0,0,0,0,0
minGuildLevel,2,1,1,1,1
effectIDs,AccountantCommission|AccountantPenaltyOnVault,Willingness,RecruitRefreshOnCounter,Willingness,""
effectValues,0.02|-0.02,0.05,7200,0.01,""
slotBuildingIDs,5,1,4,0,0
uiFlagIDs,"",SuccessRatePreview,"","",""
uiFlagBuildingIDs,0,1,0,0,0
```
（每個欄位一列；轉置格式規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**effectID / effectValue 安全範圍**（資料來源：§7.1.2）

| effectID | 類型 | effectValue 安全範圍 | 消費 API | 生效條件 |
|---|---|---|---|---|
| `Willingness` | Passive | [0.0, 0.20] | `GetStaffWillingnessBonus` | Working / Reallocating 皆生效 |
| `AccountantCommission` | Passive（M-1） | [0.0, 0.10] | `GetAccountantCommissionBonus` | Working / Reallocating 皆生效 |
| `AccountantPenaltyOnVault` | Slot | [-0.10, 0.0] | `GetAccountantPenaltyBonus` | Working AND `assignedBuildingID == 5` |
| `RecruitRefreshOnCounter` | Slot | [0, 21600]（秒） | `GetRecruitRefreshReductionSec` | Working AND `assignedBuildingID == 4` |

**uiFlagID 安全範圍**（資料來源：§7.1.3）

| uiFlagID | 所需 buildingID | 消費 API | 聚合語意 |
|---|---|---|---|
| `SuccessRatePreview` | `1`（委託板） | `IsSuccessRatePreviewEnabled` | OR（任一職員符合即 true）|

**minGuildLevel 與 severancePay 安全範圍**（資料來源：§7.1.1 / §7.5）

| 欄位 | Jam 安全範圍 | 平衡建議 |
|---|---|---|
| `salary` | [0, 10000]；Jam 全設 0 | Phase 2 實作後依稀有度設計梯度 |
| `severancePay` | [0, 5000]；1★≈50、5★≈500 | 高稀有度解雇應有重量感 |
| `minGuildLevel` | [1, 5] | 高稀有度職員建議 minGuildLevel ≥ 2，避免開局即可抽到 |
