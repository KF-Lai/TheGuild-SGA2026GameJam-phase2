# 【P-02-DS】SceneObjectStateTable

奧菲莉雅敘事物件的 stage 條件 → sprite / 對話 key 對映表；由 P-02 SceneObjectController 依當前 stage 與 activeEvents 動態解析場景物件呈現狀態（v3.1 P3.1-010）。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/SceneObjectStateTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：P-02 啟動序列 §3.8 step 4（透過 F-01 DataManager 載入）
- **資料類別**：`TheGuild.UI.SceneObjectStateTable`（GDD §3.6.4 程式碼泛型參數直接使用 `SceneObjectStateTable`；此類別同時作為 DataManager 泛型參數與資料記錄型別）
- **讀取 API**：`DataManager.Get<SceneObjectStateTable>()` 取得全表（等同 `GetAll<SceneObjectStateTable>()`），再以 `.WhereObjectID(objectID).OrderByDescending(r => r.priority)` 過濾排序（P-02 §3.6.4）；GDD 使用無參版本，實作時須確認 DataManager 支援此過載或改用 `GetAll<T>()`（需與 F-01 DataManager 實作對齊，gdd-gap）
- **消費者**：
  - P-02 SceneObjectController：`ResolveSceneObjectState(objectID)` §3.6.4；依 stageCondition 評估，回傳第一個成立 row 的 spriteVariant / dialogueKey / audioCue

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rowID` | int | ✓ | ≥ 1 | 合成 PK（GDD 未明示；objectID 非唯一，需此欄供 DataManager 索引） |
| `objectID` | string | ✓ | — | 場景物件唯一識別碼（v3.1：`ophelia_chair` / `ophelia_teacup` / `ophelia_guildbook` / `ophelia_crest` / `ophelia_door_note`） |
| `stageCondition` | string | ✓ | 合法語法見下方約束 | 觸發條件：`stageID >= N` / `stageID == N` / `stageID < N` / `event:KEY` |
| `spriteVariant` | string | ✓ | — | sprite 變體名稱；命名規則：對應 `Assets/Art/Scene/Ophelia/{objectID}_{spriteVariant}.png`（§3.6.2） |
| `dialogueKey` | string | ✗ | — | 玩家點擊時查詢 DialogueTable 的 key；空字串表不可互動（§3.6.5） |
| `priority` | int | ✓ | — | 同 objectID 多 row 時優先順序（高值優先）；第一個 stageCondition 成立的 row 生效（§3.6.2） |
| `audioCue` | string | ✗ | — | 觸發時音效 key（Post-Jam 預留；Jam 版填空字串 `""`）（§3.6.2） |

> `rowID` 為合成 PK，GDD §3.6.2 未明示此欄（gdd-gap）；objectID 在多 row 情境下非唯一，需合成鍵確保 DataManager 可索引。

## 約束 / 不變量

- 同一 objectID 的多個 row 依 priority 由高至低評估 stageCondition，第一個成立者生效；無 row 成立時回傳 `SceneObjectState.Default`（§3.6.2、§3.6.4）
- v3.1 P3.1-010 定義 10 個 row，涵蓋 5 個 objectID（§3.6.6）
- `stageCondition` 必須符合 §3.6.3 定義的四種語法之一；語法錯誤時 `EvaluateStageCondition` LogError，該 row 視為不成立（EC-22）
- `dialogueKey` 為空字串時對應物件不可互動，hover outline 不顯示（§3.6.5）
- `audioCue` Jam 版一律填空字串（§3.6.2）
- `event:KEY` 型 stageCondition 所用的 KEY 值：目前僅 `ophelia_missing`（由 `OnOpheliaMissingNight` 加入、`OnOpheliaReturned` 移除，§3.6.3）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `dialogueKey` | DialogueTable.dialogueKey | 弱約束（parser 不檢查；runtime 由 SceneObjectController 查詢）|
| `stageCondition`（stageID 部分） | FT-09 `GetUnlockedStageIndex(int factionID)` 回傳值 | 弱約束（runtime 由 `EvaluateStageCondition` §3.6.3 動態評估）|

## 變更注意事項

- 修改後即時生效：SceneObjectController 在每次 stage 事件觸發時重新呼叫 `ResolveSceneObjectState`（§3.6.1）；不需 domain reload
- 新增 stage 變體只需加 row，不需改程式碼（§7.3）
- 刪除或修改 `objectID` 需確認 §3.6.6 初始資料完整性（5 個 objectID 均需有至少一個 fallback row 或接受 Default 狀態）
- `spriteVariant` 命名異動需同步 `Assets/Art/Scene/Ophelia/` 素材檔名
- `event:KEY` 新增 KEY 值需同步更新 §3.6.3 `activeEvents` 維護邏輯（程式碼異動）

## 範例

```csv
# === SceneObjectStateTable：奧菲莉雅敘事物件 stage 對映 ===
# v3.1 P3.1-010；10 row / 5 objectID；rowID 為合成 PK；具體值依 P3.1-010 §4.2.1

rowID,1,2,3,4,5
objectID,ophelia_chair,ophelia_chair,ophelia_teacup,ophelia_guildbook,ophelia_door_note
stageCondition,stageID >= 1,stageID >= 3,stageID >= 2,stageID >= 1,event:ophelia_missing
spriteVariant,default,reading,warm,open,note_stuck
dialogueKey,ophelia_chair_s1,"",ophelia_teacup_s2,ophelia_book_s1,ophelia_door_note_missing
priority,10,20,10,10,50
audioCue,"","","","",""
```
（column-based 轉置格式；每欄一筆記錄；stageCondition 語法與 objectID 值均來自 GDD §3.6.2 / §3.6.3 / §3.6.6；具體 stageCondition 數值與 dialogueKey 依 P3.1-010 §4.2.1 填入；規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### Phase 標記

本表為 v3.1（P3.1-010）新增，Jam 版核心功能。`rowID` 合成 PK 為 gdd-gap，待 GDD §3.6.2 補正後對齊實作。
