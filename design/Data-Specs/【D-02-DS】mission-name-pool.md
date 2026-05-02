# 【D-02-DS】MissionNamePool

任務名稱與描述的文字資料池，供 C-01 Mission Database 的 `GetMissionText(difficulty, typeID)` 隨機抽取一筆回傳給上層系統。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/MissionNamePool.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：（GDD 未指定；依專案慣例由 F-01 DataManager 統一於 `RuntimeInitializeOnLoadMethod` 載入）
- **資料類別**：（GDD 未指定；建議命名 `MissionNameData`，FQN 由實作端依專案 namespace 決定）
- **讀取 API**：`DataManager.PickRandomWhere<MissionNameData>(m => m.difficulty == difficulty && m.typeID == typeID, count: 1)`（由 C-01 § 4.2 偽碼指定）
- **消費者**：
  - C-01 MissionDatabase：透過 F-01 DataManager 查詢 `MissionNamePool`；`GetMissionText(difficulty, typeID)` 為對上層系統的 Facade，D-02 無獨立 runtime class（D-02 § 6.2 / C-01 § 6.1）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `nameID` | `int` (PK) | ✓ | ≥ 1，全表唯一 | 唯一識別符，從 1 開始，不重複 |
| `difficulty` | `string` | ✓ | F / E / D / C / B / A / S / SS / SSS | 任務難度，與 C-01 `MissionDifficultyTable.difficulty` 對應 |
| `typeID` | `int` (FK → C-01 MissionTypeTable) | ✓ | 1=討伐 / 2=護送 / 3=採集 / 4=調查 | 任務類型；護送（typeID=2）限 difficulty = D/C/B/A |
| `missionName` | `string` | ✓ | 最長 20 字 | 任務名稱；應傳達任務性質，不含括號或標點符號堆疊 |
| `missionDesc` | `string` | ✓ | 最長 60 字 | 任務描述；公告文體，語氣正式；F/E 平淡、S+ 沉重；不含程式識別符或裸數值 |

## 約束 / 不變量

- 有效 (difficulty × typeID) 組合共 **31 種**：3 非護送類型（typeID 1/3/4）× 9 難度 + 護送（typeID=2）× 4 難度（D/C/B/A）（D-02 § 3.3）
- 每種有效組合至少 **3 筆**；全表總計 **≥ 93 筆**（D-02 § 3.3 / § 4）
- 護送（typeID=2）× 無效難度（F/E/S/SS/SSS）不得填入（D-02 § 3.3）；違規行不觸發 runtime 錯誤，但 AC-D02-02 以人工驗證攔截
- `nameID` 全表唯一；重複時 DataManager 後者覆蓋前者並輸出 `Debug.LogWarning`（D-02 § 5）
- `missionName` ≤ 20 字、`missionDesc` ≤ 60 字；超限不觸發 runtime 錯誤，屬資料填寫規範問題（D-02 § 5）
- `missionDesc` 不得含小數點數字（如 `0.13`）或程式識別符（如 `baseReward`）（D-02 § 3.2）
- 所有文字必須為繁體中文；奇幻地名、生物名可自創，同一名稱跨 row 需一致（D-02 § 3.2）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `typeID` | C-01 MissionTypeTable.typeID | 弱約束（DataManager 不自動 FK 檢查；C-01 `GetMissionText` 以 typeID 作為篩選值；無效 typeID 導致空池 fallback） |

> `difficulty` 為 string 等級符號，依 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md) 規範不使用 int FK，與 C-01 `MissionDifficultyTable.difficulty` 保持字面一致即可。

## 變更注意事項

- 新增或修改 CSV 後需重啟遊戲（domain reload），DataManager 在啟動時一次性載入所有 CSV
- 新增某 (difficulty × typeID) 組合的行：無需改任何程式碼，下次啟動即生效（D-02 § 1 零程式碼原則）
- 若某組合從有 ≥1 筆降至 0 筆：C-01 `GetMissionText` 觸發 fallback `("未知委託", "（無描述）")` 並輸出 `LogWarning`（C-01 § 4.2）
- 修改文字風格（語氣、用詞）後需人工審閱全部 93+ 筆確保一致性（D-02 § 7）

## 範例

```csv
# === MissionNamePool - 任務名稱描述池 ===
# 覆蓋 31 種有效 (difficulty × typeID) 組合，每組至少 3 筆
# 護送（typeID=2）限 difficulty = D/C/B/A；F/E/S/SS/SSS 不得出現護送行
# 詳見 【D-02】mission-content-database.md §3.1 / §3.3

nameID,1,2,3,4,5
difficulty,F,F,E,D,D
typeID,1,3,4,2,1
missionName,討伐周邊野狼群,採集藥草,調查失蹤農夫,護送行商前往東鎮,討伐巢穴地精
missionDesc,目標：村莊周邊出沒的野狼，擾民已久，請協助清除。,委託方：藥師工會。請至北方丘陵採集五葉草，新鮮為佳。,委託方：村長。鄰村農夫失蹤三日，請協助查明下落。,委託方：東陸商會。護送布匹商隊安抵東鎮，沿途有山賊出沒。,目標：東礦坑地精族群。已確認規模約二十隻，建議二人以上出動。
```

## 附錄

### 有效組合矩陣（覆蓋確認用）

| typeID \ difficulty | F | E | D | C | B | A | S | SS | SSS |
|---|---|---|---|---|---|---|---|---|---|
| 1 討伐 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| 2 護送 | ✗ | ✗ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ |
| 3 採集 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| 4 調查 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

> ✓ = 有效，需 ≥3 筆；✗ = 無效，不得填入（護送任務限 D~A 難度，C-01 § 3.1）

### gdd-gap 列表

| 欄位 / 設定 | 缺漏說明 |
|---|---|
| 資料類別 FQN | GDD 未指定 C# class 全名與 namespace；由實作端依專案慣例決定（建議 `MissionNameData`） |
| 解析器註冊位置 | GDD 未明示 RegisterTables 呼叫位置；依專案 DataManager 慣例於 `RuntimeInitializeOnLoadMethod` 統一載入 |
