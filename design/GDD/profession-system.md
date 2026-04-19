# Profession System 系統設計文件

_建立時間：2026-04-19_
_狀態：設計中_
_系統 ID：C-03_

---

## 1. 概要（Overview）

C-03 Profession System 定義遊戲中所有冒險者職業的靜態資料，以 `ProfessionTable` 為核心，儲存每個職業的擅長類型（`strongTypeIDs`）、弱點類型（`weakTypeIDs`）、職業階層（`tier`）與升級來源（`baseProfessionID`）。架構在 F-01 DataManager 之上，提供職業查詢 API，並作為 C-04 Race System 和 C-05 Trait System 隨機池的引用依據。FT-02 Mission Dispatch 查詢職業的擅長/弱點以套用成功率修正。系統設計為完全資料驅動：新增職業只需在 CSV 加行，職業升級路線透過 `baseProfessionID` 自引用定義，程式碼無需改動。Profession System 不持有任何 runtime 狀態。

## 2. 玩家幻想（Player Fantasy）

職業是玩家第一眼辨識冒險者的方式。「這個是戰士，派討伐沒問題」——這個直覺判斷在三秒內完成。職業不是冰冷的數值標籤，而是角色個性的第一層暗示：斥侯的機敏、盾衛的穩重、傭兵的世故。玩家在招募時看到職業，腦中已經開始想像這個冒險者能做什麼、不能做什麼。當他們慢慢發現「法師派去採集全軍覆沒」，那個「原來如此」的頓悟感，正是職業系統設計的核心報酬。

## 3. 詳細規則（Detailed Rules）

### 3.1 ProfessionTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `professionID` | `int` (PK) | 唯一識別符，從 1 起；`0` 為全專案保留 null sentinel |
| `name` | `string` | 職業名稱（顯示用） |
| `description` | `string` | 職業定位描述（UI 顯示） |
| `strongTypeIDs` | `int[]`（`\|` 分隔） | 擅長的任務 typeID 列表；無擅長填 `0`（null sentinel） |
| `weakTypeIDs` | `int[]`（`\|` 分隔） | 弱點的任務 typeID 列表；無弱點填 `0`（null sentinel） |
| `tier` | `int` | 職業階層（Game Jam 全為 `1`；升級後為 `2`、`3`…） |
| `baseProfessionID` | `int` | 升級來源職業；tier=1 填 `0`（null sentinel），tier≥2 填來源 professionID |

> **CSV null 規範**：所有 int 欄位無值時填 `0`，不得留白。`0` 為全專案統一的 null sentinel，DataManager 解析後由各系統判斷 `0` 的語義（忽略 / 視為無值）。

**Game Jam 初始資料（7 種職業，全為 tier=1）：**

| professionID | name | strongTypeIDs | weakTypeIDs | tier | baseProfessionID |
|-------------|------|--------------|------------|------|-----------------|
| 1 | 戰士 | 1 | 4 | 1 | 0 |
| 2 | 法師 | 4 | 3 | 1 | 0 |
| 3 | 遊俠 | 3 | 2 | 1 | 0 |
| 4 | 斥侯 | 2\|4 | 1 | 1 | 0 |
| 5 | 盾衛 | 2 | 4 | 1 | 0 |
| 6 | 治癒師 | 2 | 1 | 1 | 0 |
| 7 | 傭兵 | 0 | 0 | 1 | 0 |

---

### 3.2 擅長/弱點規則

1. 一個職業可有 **0 到多個**擅長 typeID 與弱點 typeID（以 `|` 分隔存於 CSV）
2. 值為 `0` 表示無擅長/無弱點（null sentinel），DataManager 解析後過濾掉 `0`，程式層不會看到 `0` 出現在列表中
3. 同一 typeID **不得同時**出現在 `strongTypeIDs` 與 `weakTypeIDs`；載入時驗證，違規則 `Debug.LogError` 並跳過該職業
4. 成功率修正由 FT-02 Mission Dispatch 套用，Profession System 僅提供原始資料

---

### 3.3 升級結構規則

1. `tier=1` 為基礎職業，`baseProfessionID = 0`（無來源）
2. `tier≥2` 的升級職業必須有合法的 `baseProfessionID`（指向存在的職業）；載入時驗證，違規則 `Debug.LogError` 並跳過
3. Game Jam 版本所有職業均為 `tier=1`，升級路線欄位預留但不啟用

---

### 3.4 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 單筆查詢 | `GetProfession(int professionID) : ProfessionData` | 找不到回傳 `null` |
| 全職業列表 | `GetAllProfessions() : IReadOnlyList<ProfessionData>` | 含所有 tier |
| 基礎職業列表 | `GetBaseProfessions() : IReadOnlyList<ProfessionData>` | 僅 `tier=1`（招募池使用） |
| 擅長判斷 | `IsStrongType(int professionID, int typeID) : bool` | 該職業是否擅長此任務類型 |
| 弱點判斷 | `IsWeakType(int professionID, int typeID) : bool` | 該職業是否弱點此任務類型 |
| 升級路線查詢 | `GetUpgradePaths(int professionID) : IReadOnlyList<ProfessionData>` | 回傳 `baseProfessionID == professionID` 的所有職業 |
| 升級來源查詢 | `GetBaseProfession(int professionID) : ProfessionData` | 回傳 `baseProfessionID` 對應職業；tier=1 回傳 `null` |

## 4. 公式（Formulas）

### 4.1 成功率修正套用（由 FT-02 執行，此為規格定義）

```
ApplyProfessionModifier(baseSuccessRate, professionID, typeID):
    if IsStrongType(professionID, typeID):
        return baseSuccessRate + STRONG_TYPE_BONUS    // +0.20
    if IsWeakType(professionID, typeID):
        return baseSuccessRate - WEAK_TYPE_PENALTY    // -0.15
    return baseSuccessRate                            // 傭兵或無修正職業
```

- `STRONG_TYPE_BONUS = 0.20`（SystemConstants）
- `WEAK_TYPE_PENALTY = 0.15`（SystemConstants）
- 結果不在此處 clamp，由 FT-02 最終 clamp 至 `[0, 1]`

### 4.2 多重擅長的疊加（斥侯案例）

```
// 斥侯（strongTypeIDs = [2, 4]）派護送任務（typeID=2）
IsStrongType(4, 2) → true → +STRONG_TYPE_BONUS

// 斥侯派討伐任務（typeID=1）
IsStrongType(4, 1) → false
IsWeakType(4, 1)   → true → -WEAK_TYPE_PENALTY
```

> 每個任務只套用**一個**修正（擅長或弱點或無），不累加。一個 typeID 不可能同時是擅長與弱點（規則 3.2.3 保證）。

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `strongTypeIDs` 與 `weakTypeIDs` 有相同 typeID | `Debug.LogError` 記錄 `professionID`，跳過該職業 |
| `strongTypeIDs` 或 `weakTypeIDs` 包含不存在於 `MissionTypeTable` 的 typeID（非 0） | `Debug.LogWarning` 記錄 `professionID` 與違規 typeID，過濾掉該 typeID，其餘正常載入 |
| `tier≥2` 的職業 `baseProfessionID = 0` | `Debug.LogError`，跳過該職業 |
| `baseProfessionID` 指向不存在的 professionID | `Debug.LogError`，跳過該職業 |
| 多值欄位僅含 `0`（如 `strongTypeIDs = "0"`） | 解析為空列表，`IsStrongType` 永遠回傳 `false` |
| 同一 `professionID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為） |

### 5.2 查詢階段

| 情況 | 處理方式 |
|------|---------|
| `GetProfession(0)` | 回傳 `null`，`Debug.LogWarning`（0 為 null sentinel，不是合法 ID） |
| `GetUpgradePaths(professionID)` 無任何職業以此為 base | 回傳空列表，不報錯（Game Jam 期間正常） |
| `IsStrongType` / `IsWeakType` 傳入不存在的 professionID | 回傳 `false`，`Debug.LogWarning` |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（C-03 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `ProfessionTable.csv`，提供泛型查詢 | `DataManager.GetAll<ProfessionData>()` |
| C-01 Mission Database | 提供合法 typeID 集合（MissionTypeTable），供載入時驗證 `strongTypeIDs`/`weakTypeIDs` | `MissionDatabase.GetAllMissionTypes()` 回傳所有合法 typeID |

> **載入順序**：F-01 DataManager 必須先完成載入，C-01 Mission Database 必須先完成載入，C-03 Profession System 才能進行跨表驗證。

### 6.2 下游依賴（依賴 C-03 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| C-02 Adventurer Management | 每位冒險者持有 `professionID`；UI 顯示職業名稱與描述 | `GetProfession(professionID)` |
| C-04 Race System | 種族隨機池按職業適性過濾（待設計） | `GetProfession(professionID)` |
| C-05 Trait System | 特性隨機池按職業過濾（待設計） | `GetProfession(professionID)` |
| FT-01 Recruitment | 取得 tier=1 職業列表作為招募池 | `GetBaseProfessions()` |
| FT-02 Mission Dispatch | 查詢職業擅長/弱點套用成功率修正 | `IsStrongType(professionID, typeID)`、`IsWeakType(professionID, typeID)` |

### 6.3 循環依賴注意事項

- C-03 依賴 C-01 進行 typeID 驗證，C-01 不依賴 C-03，**無循環依賴**
- 若未來 C-01 需要按職業過濾任務模板，應透過 EventBus 或查詢參數傳遞 professionID，而非讓 C-01 直接引用 C-03

## 7. 可調參數（Tuning Knobs）

> C-03 本身是純靜態資料查詢系統，無 runtime 狀態，不持有 ScriptableObject。職業資料的調整直接修改 `ProfessionTable.csv` 即可，無需改程式碼。成功率修正值定義於 SystemConstants，由 FT-02 Mission Dispatch 套用。

| 參數 | 位置 | 預設值 | 安全範圍 | 影響 |
|------|------|--------|---------|------|
| `STRONG_TYPE_BONUS` | `SystemConstants.csv` | `0.20` | `0.10 ~ 0.35` | 職業擅長任務的成功率加成；過高會讓職業匹配成為唯一最優策略，降低派遣多樣性 |
| `WEAK_TYPE_PENALTY` | `SystemConstants.csv` | `0.15` | `0.05 ~ 0.30` | 職業弱點任務的成功率懲罰；過高會讓錯誤派遣近乎必敗，造成玩家焦慮感過強 |

**職業資料調整原則（`ProfessionTable.csv`）：**
- 新增職業：在 CSV 加行，分配唯一 `professionID`，程式碼無需更動
- 調整擅長/弱點：直接修改 `strongTypeIDs`/`weakTypeIDs` 欄位
- 傭兵（`professionID=7`）維持 `strongTypeIDs=0`、`weakTypeIDs=0`，作為無修正基準線，可用於評估其他職業修正幅度是否合理

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-PS-01 | DataManager 初始化後，`GetAllProfessions()` 回傳 7 筆資料（Game Jam 初始職業） |
| AC-PS-02 | `GetBaseProfessions()` 僅回傳 `tier=1` 的職業，共 7 筆 |
| AC-PS-03 | `IsStrongType(1, 1)` 回傳 `true`（戰士擅長討伐 typeID=1） |
| AC-PS-04 | `IsWeakType(1, 4)` 回傳 `true`（戰士弱點魔法 typeID=4） |
| AC-PS-05 | `IsStrongType(7, 任意typeID)` 全回傳 `false`（傭兵無擅長） |
| AC-PS-06 | `IsWeakType(7, 任意typeID)` 全回傳 `false`（傭兵無弱點） |
| AC-PS-07 | `IsStrongType(4, 2)` 與 `IsStrongType(4, 4)` 皆回傳 `true`（斥侯多重擅長） |
| AC-PS-08 | CSV 中 `strongTypeIDs` 與 `weakTypeIDs` 有相同 typeID 時，`Debug.LogError` 且該職業不被載入 |
| AC-PS-09 | `strongTypeIDs`/`weakTypeIDs` 包含不存在於 MissionTypeTable 的 typeID 時，`Debug.LogWarning` 且過濾該 typeID，其餘正常載入 |
| AC-PS-10 | `GetProfession(0)` 回傳 `null` 並輸出 `Debug.LogWarning` |
| AC-PS-11 | `GetUpgradePaths(任意professionID)` 在 Game Jam 版本回傳空列表，不報錯 |
| AC-PS-12 | 新增一筆 tier=2 職業至 CSV 並設定合法 `baseProfessionID`，`GetUpgradePaths(baseProfessionID)` 回傳該升級職業 |
| AC-PS-13 | tier=2 職業 `baseProfessionID=0` 時，`Debug.LogError` 且該職業不被載入 |
