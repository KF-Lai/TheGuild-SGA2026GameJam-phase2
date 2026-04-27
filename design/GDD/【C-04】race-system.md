# Race System 系統設計文件

_建立時間：2026-04-20_
_狀態：設計中_
_系統 ID：C-04_

---

## 1. 概要（Overview）

C-04 Race System 定義遊戲中所有冒險者種族的靜態資料，以 `RaceTable` 為核心，儲存每個種族對特定任務類型的成功率修正（`successModifiers`）與死亡率修正（`deathModifiers`）。**Phase 2 起，「職業 → 種族隨機池」的權重欄位（`raceIDs` / `raceWeights`）已合併入 C-03 `ProfessionTable`**（同 PK = professionID 自然 1:1 收斂；原 `ProfessionRacePool.csv` 不再存在），C-04 仍負責 `RollRace(professionID)` 加權抽取演算法與安全範圍規範，但讀取入口改為 `C-03.GetProfession(professionID)` 取 `raceIDs` / `raceWeights`。系統架構在 F-01 DataManager 之上，提供種族查詢 API；修正值的套用由 FT-02 Mission Dispatch 負責，C-04 僅提供原始資料。C-04 不持有任何 runtime 狀態，亦不感知任何冒險者實例。

## 2. 玩家幻想（Player Fantasy）

種族是冒險者身分的第二層印記，職業讓玩家知道「他能做什麼」，種族讓玩家感受到「他是什麼樣的存在」。精靈的敏銳、獸人的蠻勇、魔族的神秘——這些印象在玩家看到種族名稱的瞬間已經成形。

種族不是擺設。當玩家漸漸發現「精靈派調查任務死亡率特別低」，或者「獸人去護送任務比想像中頑強」，那個「原來如此」的頓悟感，讓名冊裡每個冒險者的組合都開始有了策略意義——不只是職業配對，還有種族加成的微妙差異。這一層發現不是教學告訴玩家的，而是玩家自己在派遣中慢慢摸索出來的。

## 3. 詳細規則（Detailed Rules）

### 3.1 RaceTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `raceID` | `int` (PK) | 從 1 起；`0` 為 null sentinel |
| `name` | `string` | 種族顯示名稱 |
| `description` | `string` | UI 顯示描述 |
| `modifiers` | `string`（JSON） | 修正陣列，見格式定義 |

**modifiers JSON 格式：**

```json
[
  { "typeID": 2, "successDelta": 0.0, "deathDelta": -0.08 },
  { "typeID": 3, "successDelta": 0.08, "deathDelta": 0.0 }
]
```

- `typeID`：對應 MissionTypeTable（1=討伐、2=護送、3=採集、4=調查）
- `successDelta`：成功率修正（正加負減），`0.0` 表示無修正
- `deathDelta`：死亡率修正（正加負減），`0.0` 表示無修正
- 未列出的 typeID 視為兩項均為 `0.0`

**Game Jam 初始資料（4 種種族）：**

| raceID | name | modifiers 說明 |
|--------|------|---------------|
| 1 | 人類 | 護送死亡率 -8%、採集成功率 +8% |
| 2 | 精靈 | 調查成功率 +10%、調查死亡率 -5% |
| 3 | 獸人 | 討伐死亡率 -8%、調查成功率 -5% |
| 4 | 魔族 | 討伐成功率 +10%、護送死亡率 +5% |

> **`raceID = 1`（人類）為 fallback 保留 ID**：`§4.1 RollRace` 在 profession 查無、raceIDs/raceWeights 異常等多種錯誤路徑均 fallback 回傳 `1`（人類）。新增種族時**不可重排或覆寫 `raceID=1`**；若移除人類為基礎種族的設計，需同步修改 §4.1 偽碼的 fallback 值與全部相關 EC 文字。

---

### 3.2 職業 → 種族隨機池欄位（消費 C-03.ProfessionTable）

C-04 不再持有 `ProfessionRacePool.csv`；「每個職業的種族隨機池」欄位 `raceIDs` / `raceWeights` 已合併入 C-03 `ProfessionTable`（owner = C-03，schema 見 C-03 §3.1）。C-04 §4.1 的 `RollRace(professionID)` 演算法透過 `C-03.GetProfession(professionID)` 讀取 `raceIDs` / `raceWeights` 欄位後執行加權抽取（業務語意與安全範圍仍由 C-04 規範，§7.2 描述）。

**Jam 預設值**（資料來源 C-03 `ProfessionTable.raceIDs` / `raceWeights`，設計意圖由 C-04 維護）：

| professionID | name | raceIDs | raceWeights | 設計意圖 |
|-------------|------|---------|---------|---------|
| 1 | 戰士 | 1\|3 | 60\|40 | 人類為主，獸人次之 |
| 2 | 法師 | 2\|4 | 55\|45 | 精靈偏多，魔族次之 |
| 3 | 遊俠 | 1\|2 | 50\|50 | 人類與精靈各半 |
| 4 | 斥侯 | 2\|1 | 60\|40 | 精靈為主，人類次之 |
| 5 | 盾衛 | 1\|3 | 70\|30 | 以人類為主 |
| 6 | 治癒師 | 2\|1 | 60\|40 | 精靈偏多，人類次之 |
| 7 | 傭兵 | 1\|3\|4 | 50\|30\|20 | 人類為主，獸人魔族次之 |

> 調整種族池權重直接改 C-03 `ProfessionTable.csv` 的 `raceIDs` / `raceWeights` 欄位；C-04 不需重新編譯，亦不需新增 `ProfessionRacePool.csv`。

---

### 3.3 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 單筆查詢 | `GetRace(int raceID) : RaceData` | 找不到回傳 `null` |
| 全種族列表 | `GetAllRaces() : IReadOnlyList<RaceData>` | |
| 成功率修正 | `GetSuccessDelta(int raceID, int typeID) : float` | 無修正回傳 `0.0` |
| 死亡率修正 | `GetDeathDelta(int raceID, int typeID) : float` | 無修正回傳 `0.0` |
| 加權抽種族 | `RollRace(int professionID) : int` | 透過 `C-03.GetProfession(professionID)` 讀取 `raceIDs` / `raceWeights` 後加權隨機，回傳 `raceID` |

## 4. 公式（Formulas）

### 4.1 加權隨機抽種族

```
RollRace(professionID):
    profession = C-03.GetProfession(professionID)
    if profession == null → Debug.LogError, return 1  // fallback 人類（raceID=1）
    raceIDs     = profession.raceIDs
    raceWeights = profession.raceWeights
    if raceIDs.IsEmpty OR raceIDs.Length != raceWeights.Length →
        Debug.LogError, return 1                       // fallback 人類

    totalWeight = raceWeights.Sum()
    roll = Random.Range(0, totalWeight)               // [0, totalWeight)
    cumulative = 0
    for i in 0..raceIDs.Length:
        cumulative += raceWeights[i]
        if roll < cumulative:
            return raceIDs[i]

    return raceIDs.Last()                              // 防禦性 fallback（理論上不到達）
```

> 加權隨機由 C-04 自行實作（不依賴 F-01 DataManager 的隨機工具），確保邏輯獨立性。

範例（傭兵，weights = [50, 30, 20]，total = 100）：
- roll 0~49 → raceID=1（人類）
- roll 50~79 → raceID=3（獸人）
- roll 80~99 → raceID=4（魔族）

---

### 4.2 modifiers JSON 解析

```
ParseModifiers(jsonString):
    if jsonString is empty or "[]" → return empty list
    entries = JsonUtility.FromJson<List<RaceModifierEntry>>(jsonString)
    return entries

RaceModifierEntry:
    int   typeID
    float successDelta
    float deathDelta
```

> `RaceData` 在 DataManager 載入時即解析 JSON，結果快取為 `Dictionary<int, RaceModifierEntry>`（key = typeID），供 `GetSuccessDelta` / `GetDeathDelta` O(1) 查詢。
>
> **實作注意**：Unity `JsonUtility` 不支援直接解析 top-level JSON array，實作時需使用 wrapper DTO（如 `{"modifiers": [...]}`）或改用 Newtonsoft.Json。

---

### 4.3 修正值套用（由 FT-02 執行，此為規格定義）

```
ApplyRaceModifier(baseSuccessRate, baseDeathRate, raceID, typeID):
    successDelta = GetSuccessDelta(raceID, typeID)
    deathDelta   = GetDeathDelta(raceID, typeID)
    return (baseSuccessRate + successDelta,
            baseDeathRate   + deathDelta)
```

- 結果不在此 clamp；FT-02 最終 clamp 成功率至 `[0, 1]`，死亡率至 `[0, 1]`
- 範例：獸人（raceID=3）派討伐（typeID=1），`baseDeathRate = 0.10` → `0.10 + (-0.08) = 0.02`

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `modifiers` JSON 欄位為空字串或 `[]` | 解析為空列表，`GetSuccessDelta` / `GetDeathDelta` 全回傳 `0.0`，不報錯 |
| `modifiers` JSON 格式錯誤（無法解析） | `Debug.LogError` 記錄 `raceID`，該種族的修正視為空列表 |
| `modifiers` 中的 `typeID` 不存在於 MissionTypeTable | `Debug.LogWarning` 記錄 `raceID` 與違規 `typeID`，過濾該條目，其餘正常載入 |
| C-03 `ProfessionTable` 的 `raceIDs` 與 `raceWeights` 長度不一致 | **驗證歸 C-03 Loader 負責**（C-03 §3.1 / §5.1，2026-04-27 patch）：載入時跳過該違規職業，`GetProfession(professionID)` 回 `null`；C-04 `RollRace` 走 §4.1 line 92-93 的 `profession == null → return 1` fallback 路徑，不重複驗證 |
| C-03 `ProfessionTable` 的 `raceIDs` 包含不存在於 RaceTable 的 raceID | `Debug.LogWarning`，過濾該 raceID 與對應 weight，其餘正常抽取 |
| C-03 `ProfessionTable` 該職業 `raceIDs` 為空（含過濾後） | `Debug.LogError`，`RollRace` 回傳 fallback `raceID=1` |
| 某職業在 C-03 `ProfessionTable` 中無對應行 | `Debug.LogError`，`RollRace` 回傳 fallback `raceID=1`（同 `GetProfession == null`） |
| 同一 `raceID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為） |

---

### 5.2 查詢階段

| 情況 | 處理方式 |
|------|---------|
| `GetRace(0)` | 回傳 `null`，`Debug.LogWarning`（0 為 null sentinel） |
| `GetSuccessDelta` / `GetDeathDelta` 傳入不存在的 `raceID` | 回傳 `0.0`，`Debug.LogWarning` |
| `GetSuccessDelta` / `GetDeathDelta` 傳入不存在的 `typeID` | 回傳 `0.0`，不報錯（該種族對此類型無修正，屬正常情況） |
| `RollRace` 傳入不存在的 `professionID` | `Debug.LogError`，回傳 fallback `raceID=1` |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（C-04 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `RaceTable`（C-04 owner，唯一持有的表） | `DataManager.GetAll<RaceData>()` |
| C-01 Mission Database | 提供合法 typeID 集合，供 modifiers 載入時驗證 | `MissionDatabase.GetAllMissionTypes()` |
| C-03 Profession System | runtime 讀取 `raceIDs` / `raceWeights` 欄位（合併入 ProfessionTable）；亦提供合法 professionID 集合 | `C-03.GetProfession(professionID)`、`C-03.GetAllProfessions()` |

---

### 6.2 下游依賴（依賴 C-04 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| C-02 Adventurer Management | 冒險者生成時（`raceID = 0`）呼叫加權抽種族 | `RollRace(professionID)` |
| FT-02 Mission Dispatch | 套用種族成功率與死亡率修正 | `GetSuccessDelta(raceID, typeID)`、`GetDeathDelta(raceID, typeID)` |
| P-02 Main UI | 顯示冒險者種族名稱 | `GetRace(raceID).name` |

---

### 6.3 循環依賴注意事項

- C-04 依賴 C-01 進行 typeID 驗證，C-01 不依賴 C-04——**無循環依賴**
- C-04 依賴 C-03 進行 professionID 驗證以及 runtime 讀取 `raceIDs` / `raceWeights` 欄位（合併入 ProfessionTable），C-03 不依賴 C-04——**無循環依賴**
- C-02 呼叫 C-04 的 `RollRace`，C-04 不感知任何冒險者實例——**無循環依賴**

## 7. 可調參數（Tuning Knobs）

> C-04 本身為純靜態資料查詢系統，無 runtime 狀態，無 ScriptableObject。所有調整直接修改 CSV 即可，無需改程式碼。

### 7.1 RaceTable.csv

| 調整項目 | 安全範圍 | 影響 |
|---------|---------|------|
| 各種族 `successDelta` | `-0.20 ~ +0.20` | 超過 ±0.20 會使種族加成壓過職業擅長/弱點（±0.15~0.20），破壞職業為主、種族為輔的設計層次 |
| 各種族 `deathDelta` | `-0.10 ~ +0.10` | 死亡率修正影響玩家對風險的真實感受；超過 ±0.10 可能讓某種族在高難度任務中過於無敵或過於脆弱 |
| 新增種族 | — | 在 CSV 新增一行並分配唯一 `raceID`，同時在 C-03 `ProfessionTable.csv` 各職業列的 `raceIDs` / `raceWeights` 欄加入新 `raceID` 與 `weight` |

---

### 7.2 C-03 ProfessionTable.csv（C-04 消費的 raceIDs / raceWeights 欄位）

| 調整項目 | 安全範圍 | 影響 |
|---------|---------|------|
| 各職業 `raceIDs` / `raceWeights` 分布 | weights 總和無需為 100，相對比例即可 | 調整職業與種族的視覺關聯性；偏斜過大（如 95:5）會讓該職業幾乎只出現一種種族，降低名冊多樣性 |

> 欄位實體位於 C-03 `ProfessionTable.csv`（owner = C-03）；C-04 為消費端，調整數值請改 C-03 CSV，並與 C-04 owner（本系統）同步意圖。

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-RS-01 | DataManager 初始化後，`GetAllRaces()` 回傳 4 筆資料（Game Jam 初始種族） |
| AC-RS-02 | `GetRace(1).name` 回傳 `"人類"` |
| AC-RS-03 | `GetSuccessDelta(1, 3)` 回傳 `0.08`（人類採集成功率 +8%） |
| AC-RS-04 | `GetDeathDelta(1, 2)` 回傳 `-0.08`（人類護送死亡率 -8%） |
| AC-RS-05 | `GetSuccessDelta(1, 1)` 回傳 `0.0`（人類對討伐無修正） |
| AC-RS-06 | `GetSuccessDelta(2, 4)` 回傳 `0.10`（精靈調查成功率 +10%） |
| AC-RS-07 | `GetDeathDelta(4, 2)` 回傳 `0.05`（魔族護送死亡率 +5%，正值） |
| AC-RS-08 | `GetRace(0)` 回傳 `null` 並出現 `LogWarning` |
| AC-RS-09 | `modifiers` JSON 格式錯誤時，啟動出現 `LogError`，該種族修正為空，`GetSuccessDelta` 回傳 `0.0` |
| AC-RS-10 | `RollRace(7)` 呼叫 1000 次，raceID=1 出現比例約 50%，raceID=3 約 30%，raceID=4 約 20%（允許 ±5% 誤差） |
| AC-RS-11 | `RollRace` 傳入不存在的 professionID 時，回傳 `1`（fallback 人類）並出現 `LogError` |
| AC-RS-12 | C-03 `ProfessionTable` 中 `raceIDs` 與 `raceWeights` 長度不一致時，啟動出現 `LogError`，該職業 `RollRace` 回傳 fallback `1` |
| AC-RS-13 | 新增一筆種族至 CSV 並設定合法 modifiers JSON，`GetAllRaces()` 回傳新數量，修正值查詢正確 |
