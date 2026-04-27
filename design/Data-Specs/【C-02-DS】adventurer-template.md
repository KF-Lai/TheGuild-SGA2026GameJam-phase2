# 【C-02-DS】AdventurerTemplate

具名 NPC 冒險者定義表：每筆對應一位可招募的具名冒險者，持有固定屬性與特質生成規則。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/AdventurerTemplate.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-02 AdventurerManagement（DataManager.GetAll<AdventurerTemplate>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Adventurer.AdventurerTemplate`
- **讀取 API**：
  - `DataManager.Get<AdventurerTemplate>(templateID)` — 單筆
  - `DataManager.GetAll<AdventurerTemplate>()` — 全列表（供 FT-01 招募池建立）
- **消費者**：
  - C-02 AdventurerManagement：`CreateFromTemplate(templateID)` 建立冒險者實例（GDD §4.3）
  - FT-01 Recruitment：篩選具名 NPC 填入招募池、isUnique 篩查（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `templateID` | `int` (PK) | ✓ | ≥ 1 | 模板唯一識別符；`0` 為 null sentinel（表示隨機生成實例，非固定模板） |
| `name` | `string` | ✓ | — | 具名 NPC 的固定顯示名稱 |
| `rank` | `string` | ✓ | F/E/D/C/B/A/S | 冒險者初始階級 |
| `professionID` | `int` | ✓ | ≥ 1 | FK → ProfessionTable；找不到時 `Debug.LogError`，跳過該模板（GDD §5.1） |
| `raceID` | `int` | ✓ | ≥ 0 | FK → RaceTable；`0` = 依職業隨機（C-04 `RollRace` 加權抽取）；非 0 但找不到時 `Debug.LogError`，跳過（GDD §5.1） |
| `fixedTraitIDs` | `int[]`（`\|` 分隔） | ✓ | — | 每次實例化必定擁有的特質 ID；無固定特質填 `0`（null sentinel）；不存在於 TraitTable 的非 0 ID 被過濾並 `Debug.LogWarning`（GDD §5.1） |
| `randomTraitGroupIDs` | `int[]`（`\|` 分隔） | ✓ | — | FK → TraitGroupTable；每個群組各抽一次隨機特質；不需隨機特質填 `0`；不存在的 groupID 被跳過並 `Debug.LogWarning`（GDD §5.1） |
| `factionID` | `int` | ✓ | ≥ 0 | 陣營歸屬；`0` = neutral |
| `isUnique` | `int` | ✓ | 0 或 1 | `1` = 唯一角色（全局只能實例化一次，含 Dead 狀態）；`0` = 可重複招募（GDD §3.2） |

> **特質生成規則**：實例化時，先將 `fixedTraitIDs` 全部加入；再對每個 `randomTraitGroupIDs` 群組依 C-05 `pickCount` / `pickMode` 抽取後加入；最終 `traitIDs` = 固定 ∪ 隨機（去重）（GDD C-02 §3.2；實例化偽代碼見 GDD C-02 §4.3）。

## 約束 / 不變量

- `rank` 必須在 {F, E, D, C, B, A, S}；違規時 `Debug.LogError`，跳過該模板（GDD §5.1）
- `professionID` 必須存在於 ProfessionTable；違規時 `Debug.LogError`，跳過（GDD §5.1）
- `raceID != 0` 必須存在於 RaceTable；違規時 `Debug.LogError`，跳過（GDD §5.1）
- `isUnique = 1` 的模板：名冊中已有任意狀態（含 Dead）的同 `templateID` 實例時，`CreateFromTemplate` 回傳 `null`（GDD §4.3）
- 同一 `templateID` 在 CSV 重複：後者覆蓋前者，`Debug.LogWarning`（GDD §5.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `professionID` | `ProfessionTable.professionID` | FK 強約束（載入時驗證，違規跳過模板） |
| `raceID` | `RaceTable.raceID` | FK 強約束（非 0 時驗證，違規跳過模板） |
| `fixedTraitIDs` | `TraitTable.traitID` | 弱約束（找不到時過濾並警告，其餘正常） |
| `randomTraitGroupIDs` | `TraitGroupTable.groupID` | 弱約束（找不到時跳過該 groupID，其餘正常） |
| `factionID` | `FactionRouteTable.factionID` | （GDD 未指定驗證行為） |

## 變更注意事項

- 修改後需 domain reload 才生效
- `isUnique = 1` 的模板已出現於名冊（含 Dead）後，不會再進入招募池（FT-01 呼叫 `CreateFromTemplate` 時 C-02 攔截）
- 新增具名 NPC：CSV 新增一行，分配唯一 `templateID`；`isUnique = 1` 適合有劇情意義的角色，`isUnique = 0` 適合可多次出現的特色冒險者原型（GDD §7.3）

## 範例

```csv
# === AdventurerTemplate 具名 NPC 冒險者模板 ===
# isUnique=1 表示全局只能出現一次（含 Dead 後不再招募）

templateID,1,2,3
name,艾克·鐵拳,莉亞·月影,傭兵甲
rank,C,D,E
professionID,1,4,7
raceID,3,2,0
fixedTraitIDs,0,0,0
randomTraitGroupIDs,1|3,1|2,2|4
factionID,0,1,0
isUnique,1,1,0
```
（fixedTraitIDs=0 表示無固定特質；randomTraitGroupIDs 對應 TraitGroupTable.groupID；isUnique=1 角色為具名主線 NPC）
