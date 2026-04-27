# 【FT-12-DS】StaffTable

職員靜態定義表，每筆對應一位可招募的職員，持有稀有度、薪水、資遣費、效果清單與 slot 指派能力；供 FT-08 gacha 池過濾與 FT-12 入職、效果聚合、薪水計算使用。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StaffTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-12 StaffSystem 初始化（DataManager bootstrap；FT-12 §6.4 資料表依賴）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.Get<StaffData>(staffID)` 單筆查詢；`DataManager.GetAll<StaffData>()` 全列表（供 FT-08 池過濾）（FT-12 §3.2）
- **消費者**：
  - FT-12 StaffSystem：`HireStaff` 入職流程（§3.3）；effect 聚合計算（§3.4）；薪水計算（§3.9 / Phase 2）；解雇流程（§3.8）
  - FT-08 GachaSystem：`eligibleStaffIDs` FK 驗證；`minGuildLevel` 篩選 candidate pool（FT-08 §6.4）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `staffID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別碼；不可與 `TrashItemTable.trashItemID` 衝突 |
| `name` | `string` | ✓ | — | 職員顯示名稱 |
| `rarity` | `int` | ✓ | 1~5 | 稀有度 |
| `salary` | `int` | ✓ | [0, 10000] | 每日薪水（gold；Phase 2）；Jam 版全 `0` |
| `severancePay` | `int` | ✓ | [0, 5000] | 解雇資遣費（gold）；Jam 預設範圍 50~500 視稀有度 |
| `isFiller` | `bool` | ✓ | true/false | 平庸職員標記；`true` = 平庸職員（可入職但 effect 數值低） |
| `factionID` | `int` | ✓ | ≥ 0 | 陣營 ID（`0` = neutral）；Jam 版 FT-09 不消費此欄 |
| `minGuildLevel` | `int` | ✓ | [1, 5] | 可錄用最低公會等級（FT-08 gacha 池過濾條件之一） |
| `effectIDs` | `string[]`（`\|` 分隔，`StaffEffect` enum）| ✓ | — | 職員效果清單；無效果填 `0`（null sentinel，見下方說明） |
| `effectValues` | `float[]`（`\|` 分隔）| ✓ | 見附錄 | 平行於 `effectIDs` 的效果數值；passive 正值、penalty 類負值 |
| `slotBuildingIDs` | `int[]`（`\|` 分隔）| ✓ | ≥ 0 | 合格 slot 建築 ID 候選清單；`0` = 無 slot 指派能力 |
| `uiFlagIDs` | `string[]`（`\|` 分隔，`StaffUIFlag` enum）| ✓ | — | UI 功能旗標清單；無 flag 填 `0`（null sentinel） |
| `uiFlagBuildingIDs` | `int[]`（`\|` 分隔）| ✓ | ≥ 0 | 平行於 `uiFlagIDs` 的所需 `assignedBuildingID`；`0` = 無需指派 |

> `effectIDs` / `uiFlagIDs` 使用 C# enum 名稱（`string` 型別）；`effectIDs` 無效果時填單一 `0`（DataManager 過濾 null sentinel，程式層不收到 `0`）。`StaffEffect` / `StaffUIFlag` 白名單見 FT-12 §3.2。

## 約束 / 不變量

- `effectIDs.Count == effectValues.Count ≤ rarity 對應上限`（1★=1, 2★=1, 3★=2, 4★=2, 5★=3）；違反 → `StaffTableValidationException`（FT-12 §3.2）
- `effectIDs` 每個非 `0` 值必須在 `StaffEffect` 白名單（`Willingness` / `AccountantCommission` / `AccountantPenaltyOnVault` / `RecruitRefreshOnCounter`）；否則 DataManager 拋錯（FT-12 §3.2）
- `uiFlagIDs` 每個非 `0` 值必須在 `StaffUIFlag` 白名單（`SuccessRatePreview`）；否則 DataManager 拋錯（FT-12 §3.2）
- `uiFlagIDs.Count == uiFlagBuildingIDs.Count`（平行數組；GDD 未指定驗證行為）
- `staffID` 不可與任一 `TrashItemTable.trashItemID` 衝突（FT-08 §3.5.2 TrashItemTable 端驗證）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `effectIDs` | `StaffEffect` C# enum | FK 強約束（DataManager 載入驗證，非 CSV FK） |
| `uiFlagIDs` | `StaffUIFlag` C# enum | FK 強約束（DataManager 載入驗證） |
| `slotBuildingIDs` | `BuildingTable.buildingID`（FT-07）| 弱約束（GDD 未指定 parser 驗證） |
| `factionID` | `FactionRouteTable.factionID`（FT-09）| 弱約束（Jam 版 FT-09 不消費） |
| `staffID` | `StaffGachaPoolTable.eligibleStaffIDs`（FT-08）| 弱約束（FT-08 pool 端驗證） |

## 變更注意事項

- 修改後需 domain reload 才生效（FT-12 §6.4）
- 修改 `effectIDs` / `effectValues`：影響 FT-12 §3.4 effect 聚合計算；聚合上限見 `StaffTuning.csv` `EFFECT_MAX_*`（FT-12 §4.1）
- 修改 `minGuildLevel`：影響 FT-08 gacha 池候選過濾；下調讓職員出現在更早階段的面試池
- 新增 `effectID` enum 值：須同步擴充 FT-12 §3.2 `StaffEffect` 白名單 + C# enum + §3.4 聚合演算法，並視需要新增消費端 API（FT-12 §3.2）
- 新增 `uiFlagID` enum 值：須同步擴充 FT-12 §3.2 `StaffUIFlag` 白名單 + C# enum + §3.4.6 OR 聚合邏輯，並同步消費者 GDD（FT-12 §3.2）

## 範例

```csv
# === StaffTable ===
# 職員靜態定義，對齊 FT-12 §3.2 Jam 版資料範例
# effectIDs / uiFlagIDs 使用 StaffEffect / StaffUIFlag enum 名稱

staffID,101,102,103
name,克勞德·會計師,艾蓮·委託官,無名小職員
rarity,3,2,1
salary,40,30,10
severancePay,120,80,10
isFiller,false,false,true
factionID,0,0,0
minGuildLevel,2,1,1
effectIDs,AccountantCommission|AccountantPenaltyOnVault,Willingness,Willingness
effectValues,0.02|-0.02,0.05,0.01
slotBuildingIDs,5,1,0
uiFlagIDs,0,SuccessRatePreview,0
uiFlagBuildingIDs,0,1,0
```
（每個欄位一列；3 筆職員記錄，數值對齊 FT-12 §3.2 Jam 版範例（§7.1.1 / §7.1.2 調參）；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**effectIDs / effectValues 安全範圍（FT-12 §7.1.2）**

| effectID（enum）| 類型 | effectValue 安全範圍 | 消費方 |
|---|---|---|---|
| `Willingness` | Passive | [0.0, 0.20] | FT-03 NPC 接受意願加成 |
| `AccountantCommission` | Passive（M-1）| [0.0, 0.10] | FT-05 委託傭金加成（Working / Reallocating 皆生效） |
| `AccountantPenaltyOnVault` | Slot | [-0.10, 0.0] | FT-05 委託失敗罰款減免（需 Working AND assignedBuildingID == 5）|
| `RecruitRefreshOnCounter` | Slot | [0, 21600]（秒）| FT-08 自動刷新間隔減量（需 Working AND assignedBuildingID == 4）|

**uiFlagIDs 白名單（FT-12 §7.1.3）**

| uiFlagID（enum）| 對應 buildingID | 對應 API | 消費者 |
|---|---|---|---|
| `SuccessRatePreview` | `1`（委託板）| `IsSuccessRatePreviewEnabled() : bool` | P-02 委託審核 UI |
