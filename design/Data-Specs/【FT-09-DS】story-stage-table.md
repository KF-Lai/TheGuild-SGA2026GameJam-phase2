# 【FT-09-DS】StoryStageTable

陣營劇情階段表：儲存各劇情階段的分數門檻、對應劇情委託 ID 及對話鍵，是 FT-09 階段解鎖判定與雙軌呈現（對話視窗 + 委託注入）的唯一資料來源。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/StoryStageTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-09 `Bootstrap` Step A（§3.1.2）；`DataManager.GetAll<StoryStageData>()` 在 Bootstrap 時批次讀取
- **資料類別**：`TheGuild.Gameplay.FactionStory.StoryStageData`
- **讀取 API**：`DataManager.Get<StoryStageData>(key)` / `DataManager.GetWhere<StoryStageData>(predicate)`
- **消費者**：
  - FT-09 Faction Story System：啟用條件驗證（「至少 1 筆有效記錄」，§3.1.1）
  - FT-09 Faction Story System：階段解鎖判定（`currentScore >= scoreThreshold`，§3.4）
  - FT-09 Faction Story System：雙軌呈現——發布 `OnFactionStoryStageUnlocked`（payload 含 `stageID` / `dialogueKey` / `missionID`，§3.5）
  - FT-09 Bootstrap：補發未確認對話階段（§3.1.2 Step F）

## 欄位定義

C# 資料類別簽名（`TheGuild.Gameplay.FactionStory.StoryStageData`）：

```csharp
public sealed class StoryStageData {
    public int    stageID;                  // PK
    public int    factionID;                // FK → FactionRouteTable
    public int    stageIndex;
    public int    scoreThreshold;
    public int    missionID;                // FK → MissionTemplate（categoryID == 3）
    public string dialogueKey;              // FK → DialogueTable（弱約束；owner 待定）
    // v3.1 新增（P3.1-004）
    public string dialogueVariantMode;      // 預設 "none"；三值：none / ophelia_alive_dead / styletag_bias
    public string specialEventKey;          // 預設 ""；Stage 4 = "ophelia_missing"
    public string unlockBlockerCondition;   // 預設 ""；Stage 5 = "npc:ophelia:status==Idle"
}
```

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `stageID` | int | ✓ | ≥ 1（全表唯一） | PK；唯一識別碼（全域唯一，非僅同陣營內） |
| `factionID` | int | ✓ | ≥ 1（`!= FACTION_NEUTRAL_ID(0)`） | 隸屬陣營（FK → `FactionRouteTable`） |
| `stageIndex` | int | ✓ | ≥ 1，同 `factionID` 內從 1 連號 | 該陣營路線中的階段序號 |
| `scoreThreshold` | int | ✓ | ≥ 1，同 `factionID` 內嚴格遞增 | 觸發此階段所需的陣營分數門檻（`currentScore >= scoreThreshold` 時解鎖） |
| `missionID` | int | ✓ | > 0（FK → `MissionTemplate`，`categoryID == 3`） | 注入委託板的劇情委託 ID |
| `dialogueKey` | string | ✓ | — | 對話內容鍵（FK → `DialogueTable`，owner 待定，§3.2.2）；未找到時 `Debug.LogWarning` + 空對話框 fallback |
| **v3.1 新增（P3.1-004）** | | | | |
| `dialogueVariantMode` | string | — | `"none"` / `"styletag_bias"` / `"ophelia_alive_dead"` | 對話鍵解析模式（預設 `"none"`）；解析邏輯見 GDD §3.4.7 |
| `specialEventKey` | string | — | 任意字串；Stage 4 = `"ophelia_missing"` | 解鎖對話確認後供 P-02 發布特殊事件的鍵（預設 `""`）；P-02 在 ConfirmDialogue 後讀此值決定是否發布 `OnOpheliaMissingNight` |
| `unlockBlockerCondition` | string | — | 任意條件語法字串；Stage 5 = `"npc:ophelia:status==Idle"` | 分數達標時若此條件為真則暫緩解鎖（預設 `""`）；語法與解析邏輯見 GDD §3.4.8 |

## 約束 / 不變量

- `factionID == 0` → 載入時拋 `StoryStageTableValidationException("stageID={id}: factionID must be non-neutral")`，跳過（§3.2.2）
- `factionID` 在 `FactionRouteTable` 找不到 → 拋 `StoryStageTableValidationException`，跳過（§3.2.2）
- 同 `factionID` 內 `stageIndex` 必須從 1 開始連號（1, 2, 3, ...）；缺號或重複 → 拋例外，跳過該階段（§3.2.2）
- 同 `factionID` 內 `scoreThreshold` 必須嚴格遞增（`stageIndex=1` < `stageIndex=2` < ...）；違反 → 拋例外（§3.2.2）
- `missionID` 在 `MissionTemplate` 找不到，或對應 `categoryID != 3` → 拋 `StoryStageTableValidationException`，跳過（§3.2.2）
- `dialogueKey` 在 `DialogueTable` 找不到 → `Debug.LogWarning`，不跳過階段，runtime 顯示空對話框（§3.2.2）
- **v3.1 新增（P3.1-004）**：`dialogueVariantMode` 不在 `{"none", "styletag_bias", "ophelia_alive_dead"}` 三值內 → `Debug.LogWarning`，退為 `"none"` fallback，不跳過階段
- **v3.1 新增（P3.1-004）**：`specialEventKey` / `unlockBlockerCondition` 欄位缺失時視為空字串（`""`），不拋例外（向後相容舊資料）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `factionID` | `FactionRouteTable.factionID` | FK 強約束（載入時驗證，§3.2.2）|
| `missionID` | `MissionTemplate.missionID` | FK 強約束（載入時驗證 `categoryID == 3`，§3.2.2）|
| `dialogueKey` | `DialogueTable`（key，owner 待定） | 弱約束（找不到時 LogWarning + fallback，不跳過階段，§3.2.2）|
| **v3.1 新增（P3.1-004）** | | |
| `dialogueVariantMode` | 無外部 FK；三值枚舉，由 FT-09 runtime 解析 | 無約束（非法值退為 `"none"`）|
| `specialEventKey` | 由 P-02 消費；`"ophelia_missing"` 對應 `OnOpheliaMissingNight` 事件 | 弱約束（P-02 未識別的 key 靜默忽略）|
| `unlockBlockerCondition` | `"npc:{npcID}:status=={status}"` 語法；C-02 冒險者 instance 狀態查詢 | 弱約束（Jam 版僅實作 1 種語法；不識別語法 → LogWarning + 視為無 blocker）|

## 變更注意事項

- 修改 `scoreThreshold`：需重驗同 `factionID` 嚴格遞增約束；需以 F-4 / F-5 校準工具重算 buffer 安全性（§7.2.2 / §7.3）
- 修改 `missionID`：需確認目標 `MissionTemplate.categoryID == 3`；修改後需重算 F-4 SafeBuffer（§7.2.3）
- 新增 / 刪除階段：需同步調整同 `factionID` 所有後續 `stageIndex` 確保從 1 連號（§3.2.2 約束）
- `dialogueKey` 指向的 `DialogueTable` owner 尚未指派（Jam 降級：永遠 fallback 空對話框，不阻擋 FT-09 進入實作，§3.2.2）；Codex 實作時不需驗證 `DialogueTable` 存在性，找不到 key 僅 `Debug.LogWarning` 即可，不拋例外、不跳過階段
- 生效時機：重啟遊戲後（CSV 為 Resources 內嵌，runtime 不熱更新，§3.1.4）

## 範例

**v3.1 新增（P3.1-004）**：完整 5 行預設資料（奧蘿瑞女神陣營路線）

```csv
# === StoryStageTable — 陣營劇情階段（奧蘿瑞女神陣營，v3.1 正式版）===
# factionID=1：女神陣營（秩序路線）
# threshold=[8, 22, 50, 120, 200]：依各難度 factionScoreDelta 校準
# dialogueVariantMode：stage3/4 使用 styletag_bias；stage5 使用 ophelia_alive_dead
# specialEventKey：stage4 觸發 ophelia_missing 事件
# unlockBlockerCondition：stage5 等待奧菲莉雅 status==Idle

stageID,1001,1002,1003,1004,1005
factionID,1,1,1,1,1
stageIndex,1,2,3,4,5
scoreThreshold,8,22,50,120,200
missionID,9001,9002,9003,9004,9005
dialogueKey,story.aurorae.stage1,story.aurorae.stage2,story.aurorae.stage3,story.aurorae.stage4,story.aurorae.stage5
dialogueVariantMode,none,none,styletag_bias,styletag_bias,ophelia_alive_dead
specialEventKey,"","","",ophelia_missing,""
unlockBlockerCondition,"","","","",npc:ophelia:status==Idle
```
（每個欄位一列；轉置格式規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（資料來源：§7.2.2 / §7.2.3 / §7.2.5）

| 旋鈕 | Jam 預設 | 安全範圍 | 影響玩法 |
|---|---|---|---|
| `scoreThreshold`（秩序路線） | [8, 22, 50, 120, 200] | [1, ∞)；同陣營嚴格遞增 | 整體放大 → 解鎖更慢；間距均勻 → 節奏穩定；太低（如 [1,2,3]）→ 開局多階段對話視窗連發（§7.2.2）|
| `missionID`（範例難度） | 9001~9005 | 必須是 `categoryID=3` 的合法 `missionID`；對應難度 delta ≤ 下階段 buffer | 難度過高 → 玩家無法完成；過低 → 缺乏挑戰（§7.2.3）|
| 每陣營階段數 | 5 | [1, 10]；推薦 3~5 | 太少 → 路線完結太快敘事密度不足；> 5 → 文本量超出 Jam 可生成範圍（§7.2.5）|
| **v3.1 新增（P3.1-004）** | | | |
| `dialogueVariantMode` | `"none"` | `{"none", "styletag_bias", "ophelia_alive_dead"}` | 決定解鎖時刻的對話鍵解析維度；`styletag_bias` 提供三種 dark/mixed/light 變體；`ophelia_alive_dead` 在 Stage 5 結算後二次解析死活維度 |
| `specialEventKey` | `""` | 任意字串；目前實作值為 `"ophelia_missing"` | 觸發奧菲莉雅失蹤流程；空字串 = 無特殊事件 |
| `unlockBlockerCondition` | `""` | 任意條件語法字串；Jam 版實作 `"npc:{id}:status=={status}"` | 空字串 = 無 blocker；blocker 阻擋解鎖至條件解除 |

## 變更歷史

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-04-30 | v3.1 | v3.1 patch P3.1-004：StoryStageTable 新增 3 欄位（dialogueVariantMode/specialEventKey/unlockBlockerCondition）+ 5 行預設資料（奧蘿瑞女神陣營 Stage 1-5）+ 對應約束、Cross-ref、安全範圍。需 design-review 重跑（最大 patch）。|
