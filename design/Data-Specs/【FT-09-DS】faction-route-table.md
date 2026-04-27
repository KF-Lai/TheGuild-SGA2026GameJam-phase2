# 【FT-09-DS】FactionRouteTable

陣營路線定義表：儲存陣營 ID、顯示名稱與描述，是 FT-09 分數累積管線辨識「有效陣營任務」與多路線推進的依據。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/FactionRouteTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-09 `Bootstrap` Step A（§3.1.2）；`DataManager.GetAll<FactionRouteData>()` 在 Bootstrap 時批次讀取
- **資料類別**：`TheGuild.Gameplay.FactionStory.FactionRouteData`
- **讀取 API**：`DataManager.Get<FactionRouteData>(key)` / `DataManager.GetAll<FactionRouteData>()`
- **消費者**：
  - FT-09 Faction Story System：啟用條件驗證（「至少 1 筆 `factionID != FACTION_NEUTRAL_ID(0)`」，§3.1.1）
  - FT-09 Faction Story System：`HandleOnMissionResolved` 過濾（`FactionRouteTable.Contains(outcome.missionFactionID)`，§3.3.2 Step 1）
  - StoryStageTable：`factionID` FK 引用驗證（§3.2.2 驗證規則）

## 欄位定義

C# 資料類別簽名（`TheGuild.Gameplay.FactionStory.FactionRouteData`）：

```csharp
public sealed class FactionRouteData {
    public int    factionID;     // PK
    public string name;
    public string description;
}
```

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `factionID` | int | ✓ | ≥ 1（`!= FACTION_NEUTRAL_ID(0)`） | PK；唯一識別碼；`0` 為 neutral 保留值，此表不登記 |
| `name` | string | ✓ | — | 陣營顯示名稱（例：「秩序」「混沌」） |
| `description` | string | ✓ | — | 陣營描述（UI 用） |

## 約束 / 不變量

- `factionID == 0` → 載入時拋 `FactionRouteTableValidationException("factionID=0 reserved for neutral")`，跳過該行（§3.2.1）
- `factionID` 重複 → 拋 `FactionRouteTableValidationException("duplicate factionID")`，跳過重複行（§3.2.1）
- 有效行數為 0（所有行 `factionID == 0` 或表為空）→ FT-09 啟用條件未滿足，`_isEnabled = false`，整系統靜默降級（§3.1.1）
- Jam 版：1 筆有效記錄（`factionID = 1`）；Post-Jam 推薦 2-3 筆，> 5 筆文本與 playtest 成本爆炸（§7.2.4）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `factionID` | `StoryStageTable.factionID` | FK 強約束（`StoryStageTable` 載入時驗證每筆 `factionID` 必須在 `FactionRouteTable` 中存在，§3.2.2）|
| `factionID` | `SystemConstants.FACTION_NEUTRAL_ID` | 弱約束（parser 不強制；排除邏輯在 FT-09 Bootstrap / `HandleOnMissionResolved` 執行）|

## 變更注意事項

- 新增路線後，需同步補充 `StoryStageTable` 中對應 `factionID` 的階段記錄，否則該路線分數雖會累積但無階段可解鎖
- 刪除路線後，`StoryStageTable` 中對應 `factionID` 的行將在載入時觸發 `StoryStageTableValidationException` 並跳過
- 清空此表（全部 neutral 或零行）→ `_isEnabled = false`，可作為靜默停用劇情系統的開關（§3.1.2 設計動機）
- 生效時機：重啟遊戲後（CSV 為 Resources 內嵌，runtime 不熱更新，§3.1.4）

## 範例

```csv
# === FactionRouteTable — 陣營路線定義 ===
# 對應 §3.2.1 Jam 預設值（1 條秩序路線）
# Jam 版僅 1 筆；Post-Jam 新增路線直接在此追加

factionID,1
name,秩序
description,守護世界既有秩序的陣營路線
```
（每個欄位一列；轉置格式規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（資料來源：§7.2.4 A4 旋鈕）

| 旋鈕 | Jam 預設 | 安全範圍 | 影響玩法 |
|---|---|---|---|
| 有效路線數（行數，`factionID != 0`） | 1（factionID=1 秩序） | Jam 鎖 1；Post-Jam 推薦 2-3；> 5 文本與 playtest 成本爆炸（§7.2.4） | > 1 → 多陣營並行累積，`GetMaxFactionScore()` 取 max，C-06 收到的 maxScore 可能來自不同陣營；任務 factionID 分布不均會造成路線推進速度差異 |
