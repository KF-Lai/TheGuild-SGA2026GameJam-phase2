# Trait System 系統設計文件

_建立時間：2026-04-20_
_狀態：設計中_
_系統 ID：C-05_

---

## 1. 概要（Overview）

C-05 Trait System 定義冒險者個性特質的靜態資料與隨機抽取機制。每個特質（`TraitTable`）歸屬三種 `effectType` 之一：`stat`（數值修正，影響成功率/死亡率，由 FT-02 套用）、`behavior`（行為偏好，影響 willingness，由 FT-03 套用）、`condition`（條件觸發，於任務結算時由 FT-04 執行額外效果）。`TraitGroupTable` 定義特質的隨機抽取群組，每個群組指定可選特質池（`traitIDs`）、抽取數量（`pickCount`）與抽取模式（`pickMode`）。`ProfessionTraitPool` 將職業映射到一組 `traitGroupIDs`，決定該職業的冒險者在生成時會從哪些群組抽取特質。C-05 不持有任何 runtime 狀態；特質抽取在冒險者生成時由 C-02 `BuildTraitList` 呼叫一次，結果儲存於 `AdventurerInstance.traitIDs`。

## 2. 玩家幻想（Player Fantasy）

職業是冒險者的骨架，種族是他的外貌，特質才是他的靈魂。

「艾克是個戰士」——這是第一印象。「艾克是個熱血的戰士，但有點倒楣」——這才是一個人。特質讓每位冒險者從數值面板變成有個性的存在。玩家看到「貪財」會忍不住笑，看到「幸運兒」會想把他留給最危險的任務，看到「膽小」會嘆氣然後只敢派 F 難度。

`condition` 特質是驚喜的來源：「倒楣鬼」在一連串失敗後讓玩家哭笑不得，「亡命徒」在確定死亡的瞬間奇蹟存活讓玩家鬆一口氣——或者沒有，讓玩家久久無法釋懷。這些意外不是 bug，是故事。

## 3. 詳細規則（Detailed Rules）

### 3.1 TraitTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `traitID` | `int` (PK) | 從 1 起；`0` 為 null sentinel |
| `name` | `string` | 特質顯示名稱 |
| `description` | `string` | UI 顯示描述 |
| `effectType` | `string` | `stat` / `behavior` / `condition` |
| `effectTarget` | `string` | 效果目標識別符（見 3.2） |
| `effectValue` | `float` | 數值參數；condition 機率類型範圍 `[0, 1]`，delta 類型可為負 |

---

### 3.2 effectTarget 完整變數空間

這是系統允許特質作用的所有合法變數，內容量產時只能使用此清單內的值。

**stat 類（數值修正，FT-02 套用）：**

| effectTarget | 說明 | effectValue 意義 |
|-------------|------|-----------------|
| `success_all` | 所有任務類型成功率 | delta（如 +0.08） |
| `success_1` | 討伐任務成功率 | delta |
| `success_2` | 護送任務成功率 | delta |
| `success_3` | 採集任務成功率 | delta |
| `success_4` | 調查任務成功率 | delta |
| `death_all` | 所有任務類型死亡率 | delta（負值表示降低） |
| `death_1` | 討伐任務死亡率 | delta |
| `death_2` | 護送任務死亡率 | delta |
| `death_3` | 採集任務死亡率 | delta |
| `death_4` | 調查任務死亡率 | delta |

**behavior 類（意願修正，FT-03 套用）：**

| effectTarget | 說明 | effectValue 意義 |
|-------------|------|-----------------|
| `willingness_all` | 所有任務意願 | delta |
| `willingness_type_1` | 討伐任務意願 | delta |
| `willingness_type_2` | 護送任務意願 | delta |
| `willingness_type_3` | 採集任務意願 | delta |
| `willingness_type_4` | 調查任務意願 | delta |
| `willingness_diff_S` | S 難度（含以上）任務意願 | delta |
| `willingness_diff_A` | A 難度（含以上）任務意願 | delta |
| `willingness_diff_low` | F/E 難度任務意願 | delta |

**condition 類（結算觸發，FT-04 套用）：**

| effectTarget | 觸發時機 | effectValue 意義 |
|-------------|---------|-----------------|
| `on_success_gold_bonus` | 任務成功時 | 觸發機率；觸發後額外獲得 baseReward × 50% 金幣 |
| `on_fail_reputation` | 任務失敗時 | 固定值（負數）；直接加到聲望 delta 上 |
| `on_death_survive` | 死亡判定觸發時（成功或失敗皆算） | 觸發機率；觸發後死亡改為 Wounded |
| `on_fail_survive` | 僅失敗結算的死亡判定觸發時 | 觸發機率；觸發後死亡改為 Wounded |
| `on_success_reputation_bonus` | 任務成功時 | 固定值（正數）；額外加到聲望 delta 上 |

> **`on_death_survive` vs `on_fail_survive` 區別**：`on_death_survive` 不論成功或失敗，只要死亡判定觸發就有機會豁免（亡命徒）；`on_fail_survive` 僅在失敗結算的死亡判定才觸發（慘勝時不啟動）。

---

### 3.3 TraitGroupTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `groupID` | `int` (PK) | 從 1 起；`0` 為 null sentinel |
| `groupName` | `string` | 群組名稱（設計用，不顯示給玩家） |
| `traitIDs` | `int[]`（`\|` 分隔） | 此群組的可選特質 ID 池 |
| `pickCount` | `int` | 從池中抽取的特質數量 |
| `pickMode` | `string` | `uniform`（不重複抽取）/ `weighted`（加權，可重複，預留未來擴充） |

> Game Jam 版本僅使用 `uniform` 模式。術語與 F-01 DataManager 保持一致。

**Game Jam 初始群組（5 個）：**

| groupID | groupName | 說明 | pickCount |
|---------|-----------|------|-----------|
| 1 | combat_stat | 戰鬥數值特質池（成功率 / 死亡率修正） | 1 |
| 2 | behavior_general | 通用行為特質池 | 1 |
| 3 | behavior_combat | 戰鬥傾向行為特質池 | 1 |
| 4 | condition_luck | 幸運 / 厄運條件觸發池 | 1 |
| 5 | condition_survival | 求生條件觸發池 | 1 |

> 實際 `traitIDs` 由內容量產後填入。

---

### 3.4 職業 → 特質群組欄位（消費 C-03.ProfessionTable）

C-05 不再持有 `ProfessionTraitPool.csv`；「每個職業的特質群組」欄位 `traitGroupIDs` 已合併入 C-03 `ProfessionTable`（owner = C-03，schema 見 C-03 §3.1）。C-05 §3.5 的 `GetProfessionGroups(professionID)` API 透過 `C-03.GetProfession(professionID)` 讀取 `traitGroupIDs` 欄位後依群組 ID 反查 `TraitGroupTable`（業務語意與安全範圍仍由 C-05 規範，§7.3 描述）。

每個職業對應的特質群組數量決定該職業冒險者實例化時的隨機特質抽取批次：對每個群組各抽一次，固定抽取 3 個特質。

**Jam 預設值**（資料來源 C-03 `ProfessionTable.traitGroupIDs`，設計意圖由 C-05 維護）：

| professionID | name | traitGroupIDs | 設計意圖 |
|-------------|------|--------------|---------|
| 1 | 戰士 | 1\|3\|4 | 戰鬥數值 + 戰鬥行為 + 幸運/厄運 |
| 2 | 法師 | 1\|2\|4 | 戰鬥數值 + 通用行為 + 幸運/厄運 |
| 3 | 遊俠 | 1\|2\|5 | 戰鬥數值 + 通用行為 + 求生 |
| 4 | 斥侯 | 1\|2\|4 | 戰鬥數值 + 通用行為 + 幸運/厄運 |
| 5 | 盾衛 | 1\|3\|5 | 戰鬥數值 + 戰鬥行為 + 求生 |
| 6 | 治癒師 | 1\|2\|5 | 戰鬥數值 + 通用行為 + 求生 |
| 7 | 傭兵 | 2\|4\|5 | 通用行為 + 幸運/厄運 + 求生（無固定戰鬥傾向） |

> 調整職業特質群組分布直接改 C-03 `ProfessionTable.csv` 的 `traitGroupIDs` 欄位；C-05 不需重新編譯，亦不需新增 `ProfessionTraitPool.csv`。

---

### 3.5 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 單筆特質 | `GetTrait(int traitID) : TraitData` | 找不到回傳 `null` |
| 全特質列表 | `GetAllTraits() : IReadOnlyList<TraitData>` | |
| 依類型篩選 | `GetTraitsByType(string effectType) : IReadOnlyList<TraitData>` | 供 FT-02/FT-03/FT-04 各自取自己需要的類型 |
| 群組查詢 | `GetTraitGroup(int groupID) : TraitGroupData` | 找不到回傳 `null` |
| 抽取特質 | `RollTraits(TraitGroupData group) : int[]` | 依 `pickCount` 與 `pickMode` 抽取，回傳 traitID 列表 |
| 職業特質池 | `GetProfessionGroups(int professionID) : IReadOnlyList<TraitGroupData>` | 透過 `C-03.GetProfession(professionID).traitGroupIDs` 取群組 ID 列表後反查 `TraitGroupTable`；供 C-02 `BuildTraitList` 使用 |

## 4. 公式（Formulas）

### 4.1 特質隨機抽取（RollTraits）

```
RollTraits(group):
    pool = group.traitIDs.Filter(id => id != 0)
    if pool.Count < group.pickCount:
        Debug.LogWarning("pool size < pickCount, returning all")
        return pool

    if group.pickMode == "uniform":
        return pool.Shuffle().Take(group.pickCount)   // 無放回抽樣；Shuffle 採 Fisher-Yates 演算法，由 C-05 自行實作

    // weighted 模式（預留，Game Jam 不使用）
    return WeightedSample(pool, group.pickCount)
```

---

### 4.2 stat 類修正套用（由 FT-02 執行，此為規格定義）

```
ApplyStatTraits(baseSuccessRate, baseDeathRate, traitIDs, typeID):
    foreach traitID in traitIDs:
        trait = GetTrait(traitID)
        if trait.effectType != "stat" → skip

        target = trait.effectTarget
        delta  = trait.effectValue

        if target == "success_all" or target == "success_{typeID}":
            baseSuccessRate += delta
        if target == "death_all" or target == "death_{typeID}":
            baseDeathRate += delta

    return (baseSuccessRate, baseDeathRate)
    // clamp 由 FT-02 最終執行
```

> **疊加規則**：多個 `stat` 特質採**加法疊加**（線性累加所有 delta 後一次 clamp），不做乘法或優先順序排序。

---

### 4.3 behavior 類意願修正套用（由 FT-03 執行，此為規格定義）

```
ApplyBehaviorTraits(baseWillingness, traitIDs, typeID, difficulty):
    foreach traitID in traitIDs:
        trait = GetTrait(traitID)
        if trait.effectType != "behavior" → skip

        target = trait.effectTarget
        delta  = trait.effectValue

        match target:
            "willingness_all"           → baseWillingness += delta
            "willingness_type_{typeID}" → baseWillingness += delta
            "willingness_diff_S"        → if difficulty ∈ {S,SS,SSS}: baseWillingness += delta
            "willingness_diff_A"        → if difficulty ∈ {A,S,SS,SSS}: baseWillingness += delta
            "willingness_diff_low"      → if difficulty ∈ {F,E}: baseWillingness += delta

    return baseWillingness
    // FT-03 再加 jitter 與門檻判斷
```

---

### 4.4 condition 類結算觸發（由 FT-04 執行，此為規格定義）

```
ApplyConditionTraits(outcome, traitIDs):
    // baseReward 透過 outcome.baseReward 取得（由 FT-04 在 BuildOutcomeSnapshot 時快照自 C01.GetBaseReward(difficulty)）
    foreach traitID in traitIDs:
        trait = GetTrait(traitID)
        if trait.effectType != "condition" → skip

        match trait.effectTarget:

            "on_success_gold_bonus":
                if outcome.isSuccess:
                    if Random.value < trait.effectValue:
                        outcome.conditionGoldBonus += outcome.baseReward × 0.5

            "on_fail_reputation":
                if !outcome.isSuccess:
                    outcome.reputationDelta += trait.effectValue  // 負數

            "on_success_reputation_bonus":
                if outcome.isSuccess:
                    outcome.reputationDelta += trait.effectValue  // 正數

            "on_death_survive":
                if outcome.isDead:
                    if Random.value < trait.effectValue:
                        outcome.isDead = false
                        outcome.isWounded = true

            "on_fail_survive":
                if !outcome.isSuccess and outcome.isDead:
                    if Random.value < trait.effectValue:
                        outcome.isDead = false
                        outcome.isWounded = true

    return outcome
```

> condition 特質按 `traitIDs` 順序依次執行。`on_death_survive` 與 `on_fail_survive` 若同時存在，先觸發者將 `isDead` 改為 `false` 後，後者條件不成立自然跳過，無衝突。

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `effectType` 不在 {`stat`, `behavior`, `condition`} | `Debug.LogError`，跳過該特質 |
| `effectTarget` 不在 3.2 合法變數清單內 | `Debug.LogError`，跳過該特質 |
| `TraitGroupTable` 的 `traitIDs` 包含不存在於 TraitTable 的 ID（非 0） | `Debug.LogWarning`，過濾該 traitID，其餘正常 |
| `TraitGroupTable` 的 `pickCount` 大於有效 `traitIDs` 數量 | `Debug.LogWarning`，回傳所有有效特質（不補足） |
| `TraitGroupTable` 的 `pickMode` 不在 {`uniform`, `weighted`} | `Debug.LogError`，fallback 使用 `uniform` |
| C-03 `ProfessionTable.traitGroupIDs` 包含不存在於 TraitGroupTable 的 groupID（非 0） | `Debug.LogWarning`，跳過該 groupID |
| 某職業在 C-03 `ProfessionTable` 中無對應行 / `traitGroupIDs` 為空 | `Debug.LogWarning`，`GetProfessionGroups` 回傳空列表；C-02 `BuildTraitList` 將抽取 0 個隨機特質（僅保留 `fixedTraitIDs`） |
| 同一 `traitID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為） |

---

### 5.2 Runtime 操作

| 情況 | 處理方式 |
|------|---------|
| `GetTrait(0)` | 回傳 `null`，`Debug.LogWarning` |
| `GetTraitGroup(0)` | 回傳 `null`，`Debug.LogWarning` |
| `ApplyConditionTraits` 中 `effectTarget` 為未知值 | `Debug.LogWarning`，跳過該特質，繼續處理其他特質 |
| `traitIDs` 列表中有重複 traitID（固定與隨機抽到相同） | C-02 `BuildTraitList` 已去重（`Distinct()`），此處不會出現重複 |
| condition 觸發後 `outcome.isDead` 被改為 `false`，後續仍有 condition 特質 | 後續特質正常執行，`isDead = false` 的條件判斷自然跳過死亡相關觸發 |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（C-05 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `TraitTable`、`TraitGroupTable`（C-05 owner，2 張表） | `DataManager.GetAll<TraitData>()`、`DataManager.GetAll<TraitGroupData>()` |
| C-03 Profession System | runtime 讀取 `traitGroupIDs` 欄位（合併入 ProfessionTable）；亦提供合法 professionID 集合 | `C-03.GetProfession(professionID)`、`C-03.GetAllProfessions()` |

---

### 6.2 下游依賴（依賴 C-05 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| C-02 Adventurer Management | 冒險者生成時依職業群組抽取隨機特質 | `GetProfessionGroups(professionID)`、`RollTraits(group)` |
| FT-02 Mission Dispatch | 套用 `stat` 類特質的成功率與死亡率修正 | `GetTraitsByType("stat")`、`GetTrait(traitID)` |
| FT-03 NPC Decision | 套用 `behavior` 類特質的 willingness 修正 | `GetTraitsByType("behavior")`、`GetTrait(traitID)` |
| FT-04 Outcome Resolution | 執行 `condition` 類特質的結算觸發效果 | `GetTraitsByType("condition")`、`GetTrait(traitID)` |
| P-02 Main UI | 顯示冒險者特質名稱與描述 | `GetTrait(traitID).name`、`.description` |

---

### 6.3 循環依賴注意事項

- C-05 依賴 C-03 進行 professionID 驗證，C-03 不依賴 C-05——**無循環依賴**
- FT-02 / FT-03 / FT-04 各自取需要的 effectType，C-05 不感知任何結算狀態——**無循環依賴**

## 7. 可調參數（Tuning Knobs）

> C-05 本身為純靜態資料查詢系統，無 runtime 狀態。所有調整直接修改 CSV 即可，程式碼無需改動。

### 7.1 TraitTable.csv

| 調整項目 | 安全範圍 | 影響 |
|---------|---------|------|
| `stat` 類 `effectValue`（成功率 / 死亡率 delta） | `-0.15 ~ +0.15` | 超過 ±0.15 會使特質加成壓過職業擅長/弱點（±0.15~0.20），破壞「職業為主、特質為輔」的層次 |
| `behavior` 類 `effectValue`（willingness delta） | `-0.30 ~ +0.20` | 負值上限 -0.30 可讓膽小型特質有效阻止高難度接單；正值上限 +0.20 避免熱血型特質完全消除拒絕可能性 |
| `condition` 類觸發機率（`effectValue` 為機率） | `0.05 ~ 0.30` | 低於 5% 玩家感知不到；高於 30% 特質效果過於頻繁，破壞驚喜感 |
| `on_fail_reputation` / `on_success_reputation_bonus` 的 `effectValue` | `-5 ~ +5` | 超過 ±5 會使單次結算聲望波動過大，破壞聲望系統的積累感 |

---

### 7.2 TraitGroupTable.csv

| 調整項目 | 安全範圍 | 影響 |
|---------|---------|------|
| `pickCount` | `1 ~ 2` | Game Jam 建議維持 1；調高至 2 會讓每位冒險者特質數量增加，名冊資訊量上升 |
| 群組 `traitIDs` 池大小 | 建議 ≥ 4 個特質 / 群組 | 池太小會讓同職業冒險者特質重複率過高，降低多樣性 |

---

### 7.3 新增特質原則

新增特質只需在 `TraitTable.csv` 加行，`effectTarget` 必須使用 3.2 定義的合法變數。若需要新的 `effectTarget`，需同步更新 FT-02 / FT-03 / FT-04 的套用邏輯（屬 Medium 工項）。

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-TS-01 | DataManager 初始化後，`GetAllTraits()` 回傳與 CSV 行數一致的特質數量 |
| AC-TS-02 | `GetTraitsByType("stat")` 只回傳 `effectType = "stat"` 的特質 |
| AC-TS-03 | `GetTraitsByType("condition")` 只回傳 `effectType = "condition"` 的特質 |
| AC-TS-04 | `GetTrait(0)` 回傳 `null` 並出現 `LogWarning` |
| AC-TS-05 | CSV 中 `effectTarget` 不在合法清單的特質，啟動出現 `LogError`，`GetAllTraits()` 不包含該特質 |
| AC-TS-06 | `RollTraits` 對 `pickMode = "unique"`、`pickCount = 1`、池大小 = 5 的群組呼叫 100 次，每次回傳 1 個 traitID，且分布覆蓋所有 5 個 traitID |
| AC-TS-07 | `RollTraits` 對 `pickCount = 1`、池只有 1 個有效 traitID 的群組，回傳該 traitID 並出現 `LogWarning` |
| AC-TS-08 | `GetProfessionGroups(1)` 回傳戰士職業對應的 3 個群組（groupID = 1, 3, 4） |
| AC-TS-09 | `ApplyStatTraits` 對持有 `success_1 +0.08` 特質的冒險者派討伐（typeID=1），成功率正確增加 0.08 |
| AC-TS-10 | `ApplyStatTraits` 對持有 `success_all +0.05` 特質的冒險者，無論 typeID 為何，成功率均增加 0.05 |
| AC-TS-11 | `ApplyBehaviorTraits` 對持有 `willingness_diff_S`（delta=-0.25）的冒險者，派 S 難度任務時 willingness 正確減少 0.25；派 D 難度時不影響 |
| AC-TS-12 | `ApplyConditionTraits` 對持有 `on_death_survive`（機率=1.0）的冒險者，`outcome.isDead=true` 輸入後，輸出 `isDead=false`、`isWounded=true` |
| AC-TS-13 | `ApplyConditionTraits` 對持有 `on_fail_survive`（機率=1.0）的冒險者，成功結算（`isSuccess=true`）時，`isDead` 維持不變 |
| AC-TS-14 | `ApplyConditionTraits` 對持有 `on_success_gold_bonus`（機率=1.0）的冒險者，成功結算後 `conditionGoldBonus` 增加 `outcome.baseReward × 0.5`（金流由 FT-05 消費此欄位執行，C-05 不直接套用 gold） |
| AC-TS-15 | 新增一個特質至 CSV，`GetAllTraits()` 包含新特質，套用邏輯正確反映 `effectTarget` |
