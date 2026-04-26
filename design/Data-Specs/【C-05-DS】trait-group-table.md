# 【C-05-DS】TraitGroupTable

特質群組定義表：每筆對應一個特質隨機抽取群組，持有可選特質池、抽取數量與抽取模式，由 C-02 `BuildTraitList` 在冒險者生成時消費。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/TraitGroupTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-05 TraitSystem（DataManager.GetAll<TraitGroupData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Trait.TraitGroupData`
- **讀取 API**：
  - `DataManager.Get<TraitGroupData>(groupID)` — 單筆
  - `DataManager.GetAll<TraitGroupData>()` — 全列表
  - C-05 公開封裝：`GetTraitGroup(int groupID)`、`RollTraits(TraitGroupData group)`、`GetProfessionGroups(int professionID)`（GDD §3.5）
- **消費者**：
  - C-02 Adventurer Management：`BuildTraitList` 依 `randomTraitGroupIDs` 遍歷群組，各抽一次（GDD §4.4 / §6.2）
  - C-05 TraitSystem 自身：`GetProfessionGroups(professionID)` 讀取 `ProfessionTable.traitGroupIDs` 後反查本表（GDD §3.4）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `groupID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別符；`0` 為 null sentinel |
| `groupName` | `string` | ✓ | — | 群組名稱（設計用，不顯示給玩家） |
| `traitIDs` | `int[]`（`\|` 分隔） | ✓ | — | 此群組的可選特質 ID 池；FK → TraitTable；不存在的 ID（非 0）被過濾並 `Debug.LogWarning`（GDD §5.1） |
| `pickCount` | `int` | ✓ | ≥ 1 | 從池中抽取的特質數量 |
| `pickMode` | `string` | ✓ | `uniform` / `weighted` | `uniform` = 無放回抽樣（Fisher-Yates）；`weighted` = 預留未來擴充（Game Jam 不使用）；不合法值 fallback `uniform` 並 `Debug.LogError`（GDD §5.1） |

## 約束 / 不變量

- `pickCount` 大於有效 `traitIDs` 數量時：`Debug.LogWarning`，回傳所有有效特質（不補足）（GDD §5.1）
- `pickMode` 不在 {`uniform`, `weighted`}：`Debug.LogError`，fallback 使用 `uniform`（GDD §5.1）
- `traitIDs` 中不存在的 ID（非 0）被過濾；過濾後為空仍可處理（回傳空列表，`BuildTraitList` 不 crash）
- Game Jam 版本僅使用 `uniform` 模式（GDD §3.3）
- Game Jam 版本固定 5 個群組：groupID 1~5（GDD §3.3）
- 同一 `groupID` 重複：後者覆蓋，`Debug.LogWarning`（GDD §5.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `traitIDs` | `TraitTable.traitID` | 弱約束（載入時驗證，不存在則過濾並警告） |

## 變更注意事項

- 修改後需 domain reload 才生效
- 調整 `pickCount`：Game Jam 建議維持 1；調高至 2 會增加每位冒險者特質數量（GDD §7.2）
- 群組 `traitIDs` 池大小建議 ≥ 4 個特質/群組，避免同職業冒險者特質重複率過高（GDD §7.2）
- 新增群組後需同步更新 C-03 `ProfessionTable.traitGroupIDs`

## 範例

```csv
# === TraitGroupTable 特質群組定義（Game Jam 5 個群組）===
# pickMode=uniform 表示無放回抽樣；traitIDs 由內容量產後填入

groupID,1,2,3,4,5
groupName,combat_stat,behavior_general,behavior_combat,condition_luck,condition_survival
traitIDs,1|2|3|4|5,6|7|8|9|10,11|12|13|14|15,16|17|18|19|20,21|22|23|24|25
pickCount,1,1,1,1,1
pickMode,uniform,uniform,uniform,uniform,uniform
```
（traitIDs 為佔位示範，實際 ID 由 TraitTable 內容量產後填入；每群組 ≥ 4 個有效 traitID，GDD §7.2）

## 附錄

### Game Jam 群組設計意圖（來源 GDD §3.3）

| groupID | groupName | 說明 | pickCount |
|---|---|---|---|
| 1 | combat_stat | 戰鬥數值特質池（成功率/死亡率修正） | 1 |
| 2 | behavior_general | 通用行為特質池 | 1 |
| 3 | behavior_combat | 戰鬥傾向行為特質池 | 1 |
| 4 | condition_luck | 幸運/厄運條件觸發池 | 1 |
| 5 | condition_survival | 求生條件觸發池 | 1 |
