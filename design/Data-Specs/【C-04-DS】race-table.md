# 【C-04-DS】RaceTable

種族定義表：每筆對應一個冒險者種族，持有對特定任務類型的成功率與死亡率修正值（JSON 格式）。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/RaceTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based；`modifiers` 欄位於 DataManager 載入後另行解析 JSON）
- **註冊位置**：C-04 RaceSystem（DataManager.GetAll<RaceData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Race.RaceData`
- **讀取 API**：
  - `DataManager.Get<RaceData>(raceID)` — 單筆
  - `DataManager.GetAll<RaceData>()` — 全列表
  - C-04 公開封裝：`GetRace(int raceID)`、`GetSuccessDelta(int raceID, int typeID)`、`GetDeathDelta(int raceID, int typeID)`（GDD §3.3）
- **消費者**：
  - C-02 Adventurer Management：`raceID = 0` 時呼叫 `RollRace(professionID)` 加權抽種族（GDD §6.2）
  - FT-02 Mission Dispatch：`GetSuccessDelta` / `GetDeathDelta` 套用種族成功率與死亡率修正（GDD §6.2）
  - P-02 Main UI：顯示冒險者種族名稱（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `raceID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別符；`0` 為 null sentinel |
| `name` | `string` | ✓ | — | 種族顯示名稱 |
| `description` | `string` | ✓ | — | UI 顯示描述 |
| `modifiers` | `string`（JSON） | ✓ | — | 修正陣列（見格式定義）；空字串或 `[]` 視為無修正 |

> **modifiers JSON 格式**（GDD §3.1）：
> ```json
> [
>   { "typeID": 2, "successDelta": 0.0, "deathDelta": -0.08 },
>   { "typeID": 3, "successDelta": 0.08, "deathDelta": 0.0 }
> ]
> ```
> - `typeID`：對應 MissionTypeTable（1=討伐、2=護送、3=採集、4=調查）
> - `successDelta`：成功率修正（正加負減）；`0.0` 表示無修正
> - `deathDelta`：死亡率修正（正加負減）；`0.0` 表示無修正
> - 未列出的 typeID 視為兩項均為 `0.0`
>
> **實作注意**：Unity `JsonUtility` 不支援直接解析 top-level JSON array，需使用 wrapper DTO 或改用 Newtonsoft.Json（GDD §4.2）。DataManager 載入後快取為 `Dictionary<int, RaceModifierEntry>`（key = typeID），供 O(1) 查詢（GDD §4.2）。

## 約束 / 不變量

- `modifiers` JSON 格式錯誤時：`Debug.LogError` 記錄 `raceID`，該種族修正視為空列表（GDD §5.1）
- `modifiers` 中的 `typeID` 不存在於 MissionTypeTable：`Debug.LogWarning`，過濾該條目，其餘正常（GDD §5.1）
- `GetRace(0)` 回傳 `null` 並 `Debug.LogWarning`（GDD §5.2）
- 同一 `raceID` 重複：後者覆蓋，`Debug.LogWarning`（GDD §5.1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `modifiers[].typeID` | `MissionTypeTable.typeID` | 弱約束（載入時驗證，不存在則過濾並警告） |

## 變更注意事項

- 修改後需 domain reload 才生效
- 新增種族：CSV 加行分配唯一 `raceID`，同時在 C-03 `ProfessionTable.raceIDs` / `raceWeights` 各職業列加入新 `raceID` 與 weight（GDD §7.1）
- `successDelta` / `deathDelta` 調整後影響 FT-02 成功率 / 死亡率計算（GDD §7.1）

## 範例

```csv
# === RaceTable 種族定義表（Game Jam 4 種種族）===
# modifiers 欄位為 JSON 字串；含逗號需以 "..." 包裹

raceID,1,2,3,4
name,人類,精靈,獸人,魔族
description,適應力強，護送任務穩健，採集效率高,敏銳善感，調查能力超群,蠻勇無畏，戰場存活率高，調查能力不足,戰鬥本能強，護送任務風險較高
modifiers,"[{""typeID"":2,""successDelta"":0.0,""deathDelta"":-0.08},{""typeID"":3,""successDelta"":0.08,""deathDelta"":0.0}]","[{""typeID"":4,""successDelta"":0.10,""deathDelta"":-0.05}]","[{""typeID"":1,""successDelta"":0.0,""deathDelta"":-0.08},{""typeID"":4,""successDelta"":-0.05,""deathDelta"":0.0}]","[{""typeID"":1,""successDelta"":0.10,""deathDelta"":0.0},{""typeID"":2,""successDelta"":0.0,""deathDelta"":0.05}]"
```
（數值對齊 GDD §3.1 Game Jam 初始資料；modifiers 說明見 GDD §3.1 表格）

## 附錄

### 安全範圍與調參指引（來源 GDD §7.1）

| 調整項目 | 安全範圍 | 影響 |
|---|---|---|
| 各種族 `successDelta` | `-0.20 ~ +0.20` | 超過 ±0.20 會壓過職業擅長/弱點（±0.15~0.20），破壞職業為主、種族為輔的設計層次 |
| 各種族 `deathDelta` | `-0.10 ~ +0.10` | 超過 ±0.10 可能讓某種族在高難度任務中過於無敵或過於脆弱 |
