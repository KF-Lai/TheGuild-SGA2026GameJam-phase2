# Mission Database 系統設計文件

_建立時間：2026-04-19_
_狀態：設計中_
_系統 ID：C-01_

---

## 1. 概要（Overview）

C-01 Mission Database 是遊戲中所有任務資料的統一存取接口，架構在 F-01 DataManager 之上。它持有四張機制資料表：`MissionTemplate`（任務定義）、`MissionDifficultyTable`（難度對應的 baseReward / baseDuration / baseDeathRate / factionScoreDelta，集中所有「按難度查找」的數值，由 FT-02 / FT-09 共用消費）、`MissionTypeTable`（任務類型）、`MissionCategoryTable`（任務類別），並作為 D-02 Mission Content Database 的 Facade，代理 `MissionNamePool` 的文字查詢，讓上層系統透過單一接口取得完整的任務資訊（機制 + 顯示文字）。Mission Database 不持有任何 runtime 狀態，委託板的當前任務清單由 FT-05 Commission Flow 管理。它同時編碼任務生成的結構性約束（護送任務限 D~A 難度；SSS 任務不進入常規生成池），作為 Commission Flow 生成新委託時的唯一驗證依據。

## 2. 玩家幻想（Player Fantasy）

委託板是公會長每次打開遊戲的第一眼。任務的存在感——難度、類型、危險程度——必須讓玩家在三秒內感受到「這張可以接」或「這張太危險了」的直覺壓力。對設計師而言，Mission Database 應讓新增或調整委託像修改試算表一樣簡單：改一行 CSV，下次啟動立即見效，不需要碰任何程式碼。

## 3. 詳細規則（Detailed Rules）

### 3.1 資料表定義

**MissionTemplate**（任務模板，PK = `missionID`）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `missionID` | `int` (PK) | 純數字唯一識別符 |
| `difficulty` | `string` | F / E / D / C / B / A / S / SS / SSS |
| `typeID` | `int` (FK → MissionTypeTable) | 任務類型 |
| `factionID` | `int` (FK → FactionRouteTable) | 陣營歸屬；`0` = neutral（不計入任何陣營分數） |
| `categoryID` | `int` (FK → MissionCategoryTable) | 任務類別，決定進入哪個池 |

**MissionDifficultyTable**（難度 → 所有按難度查找的數值，PK = `difficulty`，9 行覆蓋 F~SSS 全難度）

集中跨系統使用的「按難度數值」於單一表格：`baseReward` / `baseDuration` 由 C-01 自身與 P-02 / FT-04 / FT-05 消費；`baseDeathRate` 由 FT-02 Mission Dispatch 消費（FT-02 §3.2 改為查 C-01 API）；`factionScoreDelta` 由 FT-09 Faction Story 消費（FT-09 §3.2.3 改為查 C-01 API）。設計師調整任務難度體感時所有數值同檔可見。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `difficulty` | `string` (PK) | F / E / D / C / B / A / S / SS / SSS（9 種，全覆蓋） |
| `baseReward` | `int` | 基礎報酬（金幣）|
| `baseDuration` | `int` | 基礎時長（分鐘）|
| `baseDeathRate` | `float` | 基礎死亡率，`[0.0, 1.0]`；FT-02 §3.4 套用 |
| `factionScoreDelta` | `int` | 任務成功且 `factionID != 0` 時對應陣營的加分值，必須 ≥ 0；FT-09 §3.3.2 套用 |

**Game Jam 初始資料：**

| difficulty | baseReward | baseDuration | baseDeathRate | factionScoreDelta |
|---|---|---|---|---|
| F | 30 | 5 | 0.02 | 1 |
| E | 80 | 12 | 0.06 | 1 |
| D | 200 | 30 | 0.10 | 2 |
| C | 400 | 60 | 0.13 | 3 |
| B | 600 | 480 | 0.18 | 5 |
| A | 800 | 840 | 0.40 | 8 |
| S | 1200 | 1200 | 0.50 | 13 |
| SS | 2000 | 1800 | 0.60 | 20 |
| SSS | 3000 | 3240 | 0.70 | 30 |

**MissionTypeTable**（任務類型，PK = `typeID`）

| `typeID` | `typeName` | 說明 |
|----------|-----------|------|
| 1 | 討伐 | 戰鬥型 |
| 2 | 護送 | 保護型，限 D~A 難度（設計意圖：護送委託為長時間、較高成功率、中報酬的委託形式；F/E 難度時長過短、報酬過低，不符合護送「長途遠征」的設計目標；S/SS/SSS 難度死亡率過高，與護送「保護目標安全送達」的語義矛盾。**CSV 填表規範**：`typeID = 2` 的 `MissionTemplate` 只能填入 `difficulty = D / C / B / A`；其他難度不得生成護送任務） |
| 3 | 採集 | 資源型 |
| 4 | 調查 | 探索型 |

**MissionCategoryTable**（任務類別，PK = `categoryID`）

| `categoryID` | `categoryName` | 說明 |
|--------------|---------------|------|
| 0 | regular | 常規委託，進入 Commission Flow 生成池 |
| 1 | tutorial | 新手引導任務，由開局流程觸發 |
| 2 | guild_challenge | 公會升等考驗，由 FT-06 Guild Core 觸發 |
| 3 | faction_story | 陣營劇情特殊任務，由 FT-09 Faction Story 觸發 |

---

### 3.2 結構性約束（硬規則）

1. `typeID = 2`（護送）僅允許出現於 `difficulty = D / C / B / A`；違規模板載入時 `Debug.LogError` 並跳過
2. `categoryID != 0`（非 regular）的模板不進入 Commission Flow 常規生成池
3. `factionID = 0`（neutral）表示此委託不計入任何陣營分數，適用於教學任務、純功能性委託（如護送 F~C）
4. `factionID` 的陣營定義由 `FactionRouteTable` 維護；Mission Database 不直接解讀陣營語義，僅提供 FK 值

---

### 3.3 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 單筆模板 | `GetTemplate(int missionID) : MissionTemplate` | 找不到回傳 `null` |
| 常規池篩選 | `GetRegularTemplates(string difficulty) : IReadOnlyList<MissionTemplate>` | 僅 `categoryID = 0` |
| 依難度+類型 | `GetRegularTemplates(string difficulty, int typeID) : IReadOnlyList<MissionTemplate>` | 常規池，加 typeID 篩選 |
| 特殊類別查詢 | `GetTemplatesByCategory(int categoryID) : IReadOnlyList<MissionTemplate>` | 供特殊觸發系統使用 |
| 報酬查詢 | `GetBaseReward(string difficulty) : int` | 查 `MissionDifficultyTable.baseReward` |
| 時長查詢 | `GetBaseDuration(string difficulty) : int` | 查 `MissionDifficultyTable.baseDuration`，回傳分鐘 |
| 護送時長 | `GetEscortDuration(string difficulty) : int` | `baseDuration × random(ESCORT_MIN, ESCORT_MAX)`，回傳分鐘 |
| 死亡率查詢 | `GetBaseDeathRate(string difficulty) : float` | 查 `MissionDifficultyTable.baseDeathRate`；FT-02 §3.4 消費 |
| 陣營加分查詢 | `GetFactionScoreDelta(string difficulty) : int` | 查 `MissionDifficultyTable.factionScoreDelta`；FT-09 §3.3 消費 |
| 文字查詢 | `GetMissionText(string difficulty, int typeID) : (string name, string desc)` | D-02 Facade，隨機抽一筆 |
| 約束驗證 | `IsValidCombination(string difficulty, int typeID) : bool` | 護送限 D~A |
| 類型名稱 | `GetTypeName(int typeID) : string` | 查 `MissionTypeTable`，供 UI 顯示用 |
| 類別名稱 | `GetCategoryName(int categoryID) : string` | 查 `MissionCategoryTable`，供 UI 顯示用 |
| 全類型列表 | `GetAllMissionTypes() : IReadOnlyList<MissionTypeData>` | 回傳所有合法 typeID；供 C-03 / C-04 / C-05 載入時驗證 typeID 合法性 |

## 4. 公式（Formulas）

### 4.1 護送任務時長

```
GetEscortDuration(difficulty):
    baseDuration = MissionDifficultyTable[difficulty].baseDuration   // 分鐘
    multiplier   = random(ESCORT_DURATION_MULTIPLIER_MIN,
                          ESCORT_DURATION_MULTIPLIER_MAX)   // 均勻隨機浮點
    return ceil(baseDuration × multiplier)                  // 取整，單位：分鐘
```

- `ESCORT_DURATION_MULTIPLIER_MIN = 3.0`（SystemConstants）
- `ESCORT_DURATION_MULTIPLIER_MAX = 5.0`（SystemConstants）
- 範例：D 難度 `baseDuration = 150` 分鐘 → 護送時長 = `ceil(150 × random(3.0, 5.0))` = **450 ~ 750 分鐘**

### 4.2 任務文字隨機抽取（D-02 Facade）

```
GetMissionText(difficulty, typeID):
    pool = DataManager.PickRandomWhere<MissionNameData>(
               predicate: m => m.difficulty == difficulty
                            && m.typeID == typeID,
               count: 1)
    if pool.isEmpty → return ("未知委託", "（無描述）")   // 防禦性回退，資料缺失時不 crash
    return (pool[0].missionName, pool[0].missionDesc)
```

> `MissionNamePool`（D-02）篩選欄位為 `difficulty + typeID`，每次隨機抽取一筆，不保證跨次不重複。

### 4.3 約束驗證

```
IsValidCombination(difficulty, typeID):
    if typeID == ESCORT_TYPE_ID:   // ESCORT_TYPE_ID = 2（SystemConstants，不 hardcode）
        return difficulty ∈ {D, C, B, A}
    return true
```

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `MissionTemplate` 中的 `typeID` 在 `MissionTypeTable` 找不到對應行 | `Debug.LogError` 記錄 `missionID`，跳過該模板 |
| 護送任務（`typeID = ESCORT_TYPE_ID`）出現在 F/E/S/SS/SSS 難度 | `Debug.LogError` 記錄 `missionID`，跳過該模板 |
| `categoryID` 在 `MissionCategoryTable` 找不到對應行 | `Debug.LogError`，跳過該模板 |
| `factionID` 在 `FactionRouteTable` 找不到對應行（且不為 `FACTION_NEUTRAL_ID`） | `Debug.LogWarning`，將 `factionID` 視為 `FACTION_NEUTRAL_ID` 處理，不跳過模板 |
| `MissionDifficultyTable` 缺少某難度的行 | `Debug.LogError`，該難度的 `GetBaseReward` / `GetBaseDuration` 回傳 `0`、`GetBaseDeathRate` 回傳 `0.0`、`GetFactionScoreDelta` 回傳 `0` |
| `MissionDifficultyTable.baseDeathRate` 不在 `[0.0, 1.0]` | `Debug.LogError`，clamp 至範圍內 |
| `MissionDifficultyTable.factionScoreDelta < 0` | `Debug.LogError`，視為 `0`（對齊舊 FT-09 EC 行為） |

### 5.2 查詢階段

| 情況 | 處理方式 |
|------|---------|
| `GetTemplate(missionID)` 查詢不存在的 ID | 回傳 `null`，`Debug.LogWarning` |
| `GetRegularTemplates(difficulty)` 無符合條件模板 | 回傳空列表，不報錯（Commission Flow 負責處理空池） |
| `GetMissionText(difficulty, typeID)` 對應的 D-02 池為空 | 回傳 `("未知委託", "（無描述）")`，`Debug.LogWarning` |
| `GetEscortDuration` 傳入非護送難度（如 F） | 仍執行計算並回傳結果，不拋錯（約束驗證由 `IsValidCombination` 負責，此 API 不重複驗證） |

### 5.3 資料一致性

| 情況 | 處理方式 |
|------|---------|
| 同一 `missionID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為） |
| `MissionTypeTable` 或 `MissionCategoryTable` 完全空白 | `Debug.LogError`，所有依賴 typeID/categoryID 的查詢回傳空列表 |

## 6. 依賴關係（Dependencies）

### 6.1 Mission Database 的依賴（上游）

| 系統 | 用途 |
|------|------|
| F-01 DataManager | 查詢 `MissionTemplate`、`MissionDifficultyTable`、`MissionTypeTable`、`MissionCategoryTable`；讀取 `ESCORT_TYPE_ID`、`FACTION_NEUTRAL_ID`、`ESCORT_DURATION_MULTIPLIER_MIN/MAX` 等常數 |
| F-01 DataManager（代理 D-02） | `PickRandomWhere<MissionNameData>` 查詢 `MissionNamePool` |

### 6.2 依賴 Mission Database 的系統（下游）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| FT-05 Guild Gold Flow | 預收時透過 FT-02 傳入的 `baseReward` 執行金流；C-01 不直接被 FT-05 呼叫（委託生成由 FT-02 經 C-01 完成） | — |
| FT-02 Mission Dispatch | 取得任務難度與類型供成功率計算；查詢基礎死亡率（原 `DeathRateTable` 已合併入 `MissionDifficultyTable`） | `GetTemplate`, `GetBaseDeathRate` |
| FT-04 Outcome Resolution | 結算時讀取 `missionDifficulty` / `missionTypeID` / `missionFactionID` 快照入 Outcome；讀取 `baseReward` 供 C-05 `on_success_gold_bonus` 計算 | `GetTemplate`, `GetBaseReward` |
| FT-06 Guild Core | 觸發公會升等考驗任務 | `GetTemplatesByCategory(categoryId=2)` |
| FT-09 Faction Story | `categoryID=3` 陣營劇情任務消費者（FT-09 §3.2 觸發條件）；讀取 `factionID` 供分數累積（FT-09 §3.3.2 Step 1 過濾）；查詢陣營加分權重（FT-09 §3.3.3 `GetFactionScoreDelta` 消費；原 `MissionFactionScoreWeight` 已合併入 `MissionDifficultyTable`） | `GetTemplatesByCategory(categoryID=3)`, `GetTemplate`, `GetFactionScoreDelta` |
| FT-10 Save/Load | 驗證存檔中的 `missionID` 仍合法 | `GetTemplate` |
| P-02 Main UI | 顯示委託板任務資訊（名稱、難度、類型、時長、報酬） | `GetTemplate`, `GetBaseReward`, `GetBaseDuration`, `GetMissionText`, `GetTypeName` |

### 6.3 循環依賴注意

Mission Database 依賴 F-01 DataManager，DataManager 不依賴 Mission Database——**無循環依賴**。D-02 Mission Content Database 對上層系統透明，僅透過 Mission Database 的 Facade API 存取。

## 7. 可調參數（Tuning Knobs）

### 7.1 全域常數（SystemConstants.csv）

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `ESCORT_DURATION_MULTIPLIER_MIN` | `3.0` | `2.0 ~ 4.0` | 護送任務最短時長倍率；低於 2.0 護送失去「長途遠征」感 |
| `ESCORT_DURATION_MULTIPLIER_MAX` | `5.0` | `4.0 ~ 8.0` | 護送任務最長時長倍率；高於 8.0 玩家可能覺得等待過久 |
| `ESCORT_TYPE_ID` | `2` | 固定，不調整 | 護送約束判斷依據，改動需同步更新 MissionTypeTable |
| `FACTION_NEUTRAL_ID` | `0` | 固定，不調整 | neutral 保留值，改動會破壞所有 factionID = 0 的模板 |

### 7.2 資料表調整

| 表格 | 可調內容 | 注意事項 |
|------|---------|---------|
| `MissionTemplate` | 新增/修改任務模板 | 護送任務需符合 D~A 難度約束 |
| `MissionDifficultyTable.baseReward` | 各難度 `baseReward` | 調整後影響整體金幣流入速度，需搭配 Commission Flow 傭金率評估 |
| `MissionDifficultyTable.baseDuration` | 各難度 `baseDuration` | 護送時長 = baseDuration × 3~5 倍，調整時注意護送任務的實際等待時間 |
| `MissionDifficultyTable.baseDeathRate` | 各難度基礎死亡率 | 與 FT-02 死亡率公式對齊；A/SS 階為高難度死亡尖峰，調整時同步檢查 FT-02 §3.4 與 §7 |
| `MissionDifficultyTable.factionScoreDelta` | 各難度陣營加分 | 必須 ≥ 0；A 以上指數遞增以對應「dark 路線靠高難度任務累積」精神，與 FT-09 §7 / `StoryStageTable.scoreThreshold` 同時校準 |
| `MissionTypeTable` | 新增任務類型 | 新增後需同步更新 `ProfessionTable` 的擅長/弱點欄位 |
| `MissionCategoryTable` | 新增任務類別 | 新增後需在對應觸發系統（FT-06/FT-09 等）實作觸發邏輯 |
| `FactionRouteTable` | 新增陣營 | 新增後需在 FT-09 實作分數累積與劇情觸發 |

## 8. 驗收標準（Acceptance Criteria）

| ID | 測試項目 | 通過條件 |
|----|---------|---------|
| AC-MD-01 | 模板載入 | 啟動後 `GetRegularTemplates("D")` 回傳所有 D 難度、`categoryID=0` 的模板，數量與 CSV 一致 |
| AC-MD-02 | 護送約束載入驗證 | CSV 中加入一筆 F 難度護送模板，啟動後 Console 出現 `LogError`，該模板不出現於任何查詢結果 |
| AC-MD-03 | FK 驗證 | CSV 中加入一筆 `typeID = 99`（不存在），啟動後 `LogError`，該模板被跳過 |
| AC-MD-04 | factionID fallback | CSV 中加入一筆 `factionID = 99`（不存在），啟動後 `LogWarning`，模板仍可查詢，`factionID` 視為 `FACTION_NEUTRAL_ID` |
| AC-MD-05 | 報酬查詢 | `GetBaseReward("SSS")` 回傳 `3000` |
| AC-MD-06 | 時長查詢 | `GetBaseDuration("B")` 回傳 `480` |
| AC-MD-07 | 護送時長範圍 | `GetEscortDuration("D")` 回傳值在 `[90, 150]` 之間（30 × 3.0 ~ 30 × 5.0） |
| AC-MD-08 | 護送時長隨機性 | 呼叫 `GetEscortDuration("D")` 100 次，結果不全相同 |
| AC-MD-09 | 文字查詢 | `GetMissionText("C", 1)` 回傳非空的 name 與 desc |
| AC-MD-10 | 文字查詢空池 | D-02 中無 `difficulty="F", typeID=4` 的資料時，`GetMissionText("F", 4)` 回傳 `("未知委託", "（無描述）")` 並出現 `LogWarning` |
| AC-MD-11 | 約束驗證 | `IsValidCombination("F", 2)` 回傳 `false`；`IsValidCombination("D", 2)` 回傳 `true`；`IsValidCombination("SSS", 1)` 回傳 `true` |
| AC-MD-12 | 特殊類別查詢 | `GetTemplatesByCategory(3)` 只回傳 `categoryID=3` 的模板 |
| AC-MD-13 | 類型名稱查詢 | `GetTypeName(1)` 回傳 `"討伐"`；`GetTypeName(99)` 回傳 `null` 並出現 `LogWarning` |
| AC-MD-14 | 死亡率查詢 | `GetBaseDeathRate("F")` 回傳 `0.02`；`GetBaseDeathRate("SSS")` 回傳 `0.70` |
| AC-MD-15 | 陣營加分查詢 | `GetFactionScoreDelta("F")` 回傳 `1`；`GetFactionScoreDelta("SSS")` 回傳 `30` |
| AC-MD-16 | 缺失行 fallback | CSV 移除 `difficulty="B"` 行，啟動 `LogError`；`GetBaseReward("B")` 回傳 `0`、`GetBaseDeathRate("B")` 回傳 `0.0`、`GetFactionScoreDelta("B")` 回傳 `0` |
