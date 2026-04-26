# World Danger System 系統設計文件

_建立時間：2026-04-20_
_狀態：設計中_
_系統 ID：C-06_

---

## 1. 概要（Overview）

C-06 World Danger System 管理遊戲全局的世界危險度，這是一個單向遞增的壓力指標，共 5 階（E→D→C→B→A），每階升級需同時滿足**時間閘**（遊戲經過天數）、**進度閘**（累積接受任務數量）與**陣營閘**（任意陣營累積分數）。危險度影響兩個下游系統：（1）**F-03 Resource Management** — C-06 於啟動與升階時**主動推送** `SetBankruptcyThreshold(maxDebt)`，F-03 被動接收作為金幣硬性下限（危險度越高，負債空間越大）；（2）**FT-02 Mission Dispatch** — 查詢 `GetPoolWeights()` 決定任務池的難度分布偏移（危險度越高，高難度任務出現比例越大）。世界危險度只升不降，即使玩家公會聲望下滑或金幣虧損也不會倒退。C-06 持有唯一的 runtime 狀態：`currentDangerLevel`（當前危險度）與 `acceptedMissionCount`（累積接受任務數），並在每次任務被接受時由 FT-02 呼叫 `OnMissionAccepted()` 更新計數，由 F-02 Time System 每日觸發升級檢查。

> **分層原則**：作為 Core 層系統，C-06 依賴 Foundation 層（F-01 / F-02）；對 F-03 的門檻更新採用**主動推送**而非被動拉取，避免 F-03 反向依賴 C-06（違反 Foundation 層零 Core 依賴原則）。

## 2. 玩家幻想（Player Fantasy）

世界危險度是公會長感受到的「時代壓力」。遊戲一開始是和平年代，任務溫和、風險可控——但這只是暫時的。世界在悄悄改變，玩家每次接受任務、每天讓時間流逝，都在推動世界走向更黑暗的階段。

玩家不會直接操控世界危險度，但會感受到它的存在：委託板上開始出現以前沒見過的高難度任務，原本安全的冒險者開始面對真正的死亡風險。「以前 B 難度已經很危險了，現在怎麼連 A 難度都出來了？」這種壓迫感的來源不是懲罰，而是世界本身的演進節奏。

最高階的「末世」不是遊戲結束——它是最後的考驗，是公會從破舊小屋走到傳說殿堂後必須面對的終極壓力。能在末世中撐下去的公會，才是真正的傳奇。

## 3. 詳細規則（Detailed Rules）

### 3.1 WorldDangerTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `dangerLevel` | `string` (PK) | E / D / C / B / A |
| `name` | `string` | 顯示名稱（和平 / 動盪 / 暗湧 / 危局 / 末世） |
| `timeThreshold` | `int` | 時間閘：升至此階所需最低遊戲天數 |
| `missionCountReq` | `int` | 進度閘：累積接受任務數門檻 |
| `minDifficulty` | `string` | 進度閘：計入 `acceptedMissionCount` 的最低任務難度 |
| `factionScoreReq` | `int` | 陣營閘：任意陣營累積分數需達到此值；`0` = 不設此閘 |

**Game Jam 初始資料：**

| dangerLevel | name | timeThreshold | missionCountReq | minDifficulty | factionScoreReq |
|------------|------|--------------|----------------|--------------|----------------|
| E | 和平 | 0 | 0 | F | 0 |
| D | 動盪 | 1 | 10 | F | 0 |
| C | 暗湧 | 3 | 15 | D | 5 |
| B | 危局 | 7 | 20 | C | 12 |
| A | 末世 | 14 | 25 | B | 20 |

> E 階為起始狀態，三閘均為 0，不需升級條件。E、D 階不設陣營閘（`factionScoreReq = 0`）；C 階起需至少一個陣營累積分數達到門檻，象徵世界格局開始被陣營勢力拉動。

---

### 3.2 MissionPoolWeights 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `dangerLevel` | `string` (PK) | E / D / C / B / A |
| `weightF_E` | `int` | F~E 難度合計權重 |
| `weightD` | `int` | D 難度權重 |
| `weightC` | `int` | C 難度權重 |
| `weightB` | `int` | B 難度權重 |
| `weightA` | `int` | A 難度權重 |
| `weightS_SSS` | `int` | S~SSS 難度合計權重 |

**Game Jam 初始資料：**

| dangerLevel | F~E | D | C | B | A | S~SSS |
|------------|-----|---|---|---|---|-------|
| E 和平 | 40 | 30 | 20 | 8 | 2 | 0 |
| D 動盪 | 20 | 35 | 25 | 15 | 5 | 0 |
| C 暗湧 | 10 | 20 | 35 | 25 | 8 | 2 |
| B 危局 | 5 | 10 | 25 | 35 | 20 | 5 |
| A 末世 | 0 | 5 | 15 | 30 | 35 | 15 |

---

### 3.3 DebtLimitTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `dangerLevel` | `string` (PK) | E / D / C / B / A |
| `maxDebt` | `int` | 負值；金幣不可低於此值，由 F-03 Resource Management 強制執行 |

**Game Jam 初始資料：**

| dangerLevel | maxDebt |
|------------|---------|
| E | -100 |
| D | -500 |
| C | -1000 |
| B | -2500 |
| A | -5000 |

---

### 3.4 Runtime 狀態

| 欄位 | 型別 | 說明 |
|------|------|------|
| `currentDangerLevel` | `string` | 當前危險度，在 `Awake` 階段初始化為 `"E"`；`Start` 階段主動呼叫 `F-03.SetBankruptcyThreshold(GetMaxDebt())` 推送 E 階初始債務上限（`-100`）至 F-03。Script Execution Order 需確保 C-06 的 `Start` 晚於 F-03 的 `Start`（F-03 已在自身 Awake 設好預設 `-100`，C-06 在 Start 推送覆寫為當前正確值） |
| `acceptedMissionCount` | `int` | 當前階段累積接受任務數（依下一階 `minDifficulty` 門檻計入），**每次升階重置為 0**，初始為 `0` |
| `gameStartTimestamp` | `long` | 遊戲開始的 Unix timestamp（秒），用於計算遊戲天數；由 FT-10 Save/Load 在新遊戲建立時寫入，載入存檔時還原 |
| `_cachedMaxFactionScore` | `int` | FT-09 主動推送的任意陣營最高累積分數快取；初始為 `0` |

---

### 3.5 升級規則

1. 世界危險度**只升不降**，無任何條件可使其倒退
2. 每次升級需同時滿足三閘：
   - **時間閘**：`elapsedDays >= nextLevel.timeThreshold`
   - **進度閘**：`acceptedMissionCount >= nextLevel.missionCountReq`
   - **陣營閘**：`nextLevel.factionScoreReq == 0` OR `_cachedMaxFactionScore >= nextLevel.factionScoreReq`
3. `elapsedDays = floor((currentTimestamp - gameStartTimestamp) / 86400)`
4. 進度閘計數規則：任務難度索引 >= `minDifficulty` 索引才計入；難度固定索引順序：F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8
5. 陣營閘分數由 FT-09 主動推送：FT-09 在任意陣營分數更新時呼叫 `C-06.OnFactionScoreUpdated(newMaxScore)`，C-06 以 `_cachedMaxFactionScore` 快取最新值；C-06 不直接查詢 FT-09
6. 升階成功後，`acceptedMissionCount` 重置為 `0`，從新的 `minDifficulty` 門檻重新計數
7. 升級時：
   - **主動推送**債務上限：呼叫 `F-03.SetBankruptcyThreshold(GetMaxDebt())` 將新階 `maxDebt` 推送至 F-03（F-03 被動接收，不主動查詢）
   - 發布 `EventBus.Publish(OnDangerLevelChanged, newLevel)`，通知其他下游（如 FT-02 更新任務池權重、P-02 UI 更新顯示）

---

### 3.6 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 當前危險度 | `GetCurrentLevel() : string` | 回傳當前 `dangerLevel` |
| 危險度資料 | `GetDangerData(string dangerLevel) : WorldDangerData` | 找不到回傳 `null` |
| 債務上限 | `GetMaxDebt() : int` | 回傳當前危險度的 `maxDebt` |
| 任務池權重 | `GetPoolWeights() : MissionPoolWeights` | 回傳當前危險度的難度分布權重 |
| 接受任務通知 | `OnMissionAccepted(string difficulty) : void` | FT-02 §3.5 派遣流程呼叫；符合 `minDifficulty` 則計數 +1，並呼叫 `CheckLevelUp()` |
| 每日升級檢查 | `CheckLevelUp() : void` | F-02 每日重置時呼叫，亦可由 `OnMissionAccepted` / `OnFactionScoreUpdated` 觸發 |
| 陣營分數推送 | `OnFactionScoreUpdated(int newMaxScore) : void` | FT-09 呼叫；更新 `_cachedMaxFactionScore` 並立即呼叫 `CheckLevelUp()` |

## 4. 公式（Formulas）

### 4.1 遊戲天數計算

```
GetElapsedDays():
    now = F-02 TimeSystem.NowUTC
    return floor((now - gameStartTimestamp) / 86400)
```

範例：`gameStartTimestamp = 1745000000`，當前 `= 1745259200`（3 天後）
→ `floor(259200 / 86400) = 3` 天

---

### 4.2 升級檢查

```
CheckLevelUp():
    nextLevel = GetNextDangerLevel(currentDangerLevel)
    if nextLevel == null → 已是最高階（A），無操作

    data = GetDangerData(nextLevel)
    elapsedDays = GetElapsedDays()

    timeOK    = elapsedDays >= data.timeThreshold
    missionOK = acceptedMissionCount >= data.missionCountReq
    factionOK = data.factionScoreReq == 0
                OR _cachedMaxFactionScore >= data.factionScoreReq

    if timeOK AND missionOK AND factionOK:
        currentDangerLevel = nextLevel
        acceptedMissionCount = 0
        F03.SetBankruptcyThreshold(GetMaxDebt())   // 方案A：主動推送新階 maxDebt 至 F-03
        EventBus.Publish(OnDangerLevelChanged, nextLevel)
        CheckLevelUp()   // 遞迴檢查是否可連續升階
```

> 遞迴呼叫確保玩家長時間離線後回來，可一次跳越多個危險度階段，直到條件不符為止。

---

### 4.3.5 陣營分數推送（FT-09 呼叫）

```
OnFactionScoreUpdated(newMaxScore):
    _cachedMaxFactionScore = newMaxScore
    CheckLevelUp()   // 分數更新後立即嘗試升階
```

---

### 4.2.5 啟動時推送初始門檻至 F-03（方案A）

```
Start():
    // Awake 階段已將 currentDangerLevel 設為 "E"
    // Start 階段主動推送初始 maxDebt 至 F-03（覆寫 F-03 的預設 -100）
    F03.SetBankruptcyThreshold(GetMaxDebt())

    // 載入存檔時，若存檔中 currentDangerLevel 已非 "E"，此推送會帶入正確的階別 maxDebt
    // （FT-10 Save/Load 負責在 Awake 之後 Start 之前還原 currentDangerLevel）
```

> **Script Execution Order 要求**：F-03 < C-06，確保 F-03 的 `Awake`（設預設 `-100`）與 `Start`（訂閱 F-02 事件）都在 C-06 的 `Start`（主動推送）之前執行。

---

### 4.3 任務接受計數

```
OnMissionAccepted(difficulty):
    nextLevel = GetNextDangerLevel(currentDangerLevel)
    if nextLevel == null → return   // 已是最高階

    nextLevelData = GetDangerData(nextLevel)
    if DifficultyIndex(difficulty) >= DifficultyIndex(nextLevelData.minDifficulty):
        acceptedMissionCount++
        CheckLevelUp()
```

> 計數門檻取的是**下一個目標階的 `minDifficulty`**，而非當前階。確保玩家從一開始就在累積升至下一階的進度。

難度固定索引：F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `WorldDangerTable` 缺少某危險度行（如缺 B） | `Debug.LogError`，缺少的階視為不存在，升級跳過該階直接嘗試下一階 |
| `MissionPoolWeights` 缺少某危險度行 | `Debug.LogError`，`GetPoolWeights()` 回傳 E 階權重作為 fallback |
| `DebtLimitTable` 缺少某危險度行 | `Debug.LogError`，`GetMaxDebt()` 回傳 `-100`（E 階預設值）作為 fallback |
| `minDifficulty` 不在合法難度值 {F, E, D, C, B, A, S, SS, SSS} | `Debug.LogError`，視為 `"F"`（最寬鬆門檻） |
| `dangerLevel` 欄位大小寫不一致（如 `e` vs `E`） | DataManager 載入時統一轉為大寫，`Debug.LogWarning` |

---

### 5.2 Runtime 操作

| 情況 | 處理方式 |
|------|---------|
| `OnFactionScoreUpdated` 尚未被 FT-09 呼叫過（初始狀態） | `_cachedMaxFactionScore = 0`；`factionScoreReq = 0` 的階正常通過，有門檻的階暫時無法升級，不報錯 |
| `gameStartTimestamp` 為 `0`（存檔損毀） | `Debug.LogError`，`GetElapsedDays()` 回傳 `0`，時間閘視為未滿足，系統不崩潰 |
| 玩家離線期間跨越多個危險度 | `CheckLevelUp()` 遞迴處理，一次補算到符合條件的最高階 |
| `acceptedMissionCount` 在存檔還原後繼續累積 | 直接延續，不重置；存檔需序列化此值 |
| 當前危險度已為 `A`（最高階），呼叫 `OnMissionAccepted` 或 `CheckLevelUp` | 直接 return，不計數、不升級、不報錯 |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（C-06 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `WorldDangerTable`、`MissionPoolWeights`、`DebtLimitTable` | `DataManager.GetAll<WorldDangerData>()` 等 |
| F-02 Time System | 取得當前 timestamp；每日重置時呼叫 `CheckLevelUp()` | `TimeSystem.NowUTC`、每日 Tick 回呼 |

---

### 6.2 下游依賴（依賴 C-06 的系統）

| 系統 | 依賴內容 | 使用介面（方向） |
|------|---------|---------|
| F-03 Resource Management | **C-06 主動推送**：啟動（§4.2.5）與升階（§4.2）時呼叫 F-03 寫入 API；F-03 不主動查詢 C-06 | `F-03.SetBankruptcyThreshold(int)`（C-06 → F-03）|
| FT-02 Mission Dispatch | 查詢任務池難度分布權重，生成委託時使用 | `GetPoolWeights()` |
| FT-02 Mission Dispatch | 每次派遣成功後呼叫 C-06 計數，驅動危險度升級 | `OnMissionAccepted(difficulty)`（FT-02 §3.5 於發布 `OnCommissionAccepted` 後呼叫） |
| FT-09 Faction Story System | 當任意陣營分數更新時推送至 C-06 | `OnFactionScoreUpdated(int newMaxScore)` |
| P-02 Main UI | 顯示當前世界危險度名稱與說明；訂閱 `OnDangerLevelChanged` 即時更新 UI | `GetCurrentLevel()`、`GetDangerData()`、`OnDangerLevelChanged` |

---

### 6.3 循環依賴注意事項

- FT-09 推送分數給 C-06（觀察者模式），C-06 不依賴 FT-09——**無循環依賴**
- FT-02 呼叫 C-06 `OnMissionAccepted`，C-06 不依賴 FT-02——**無循環依賴**
- C-06 主動呼叫 F-03 `SetBankruptcyThreshold`（Core → Foundation 單向推送），F-03 完全不知 C-06 存在——**無循環依賴，無反向依賴**

### 6.4 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"c06WorldDanger"` |
| `IsCritical` | `false`（Degradable；還原失敗時重置至 E 階，核心循環仍可進行） |

**`Serialize()` 序列化欄位**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `currentDangerLevel` | `string` | 當前危險度階級（`"E"` / `"D"` / `"C"` / `"B"` / `"A"`） |
| `acceptedMissionCount` | `int` | 朝下一階累積的任務計數 |
| `gameStartTimestamp` | `long` | 公會建立時的 UTC Unix 秒（時間閘基準） |
| `_cachedMaxFactionScore` | `int` | FT-09 最近推送的最大陣營分數（陣營閘依據） |

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化上述 4 個欄位。
2. 驗證 `currentDangerLevel` ∈ `{"E", "D", "C", "B", "A"}`；違規拋例外（觸發 `InitializeAsNewGame()`，對應 Degradable 策略）。
3. 還原完成後，立即呼叫 `F-03.SetBankruptcyThreshold(GetMaxDebt())` 推送當前危險度對應的破產門檻（§5.2 已定義 `gameStartTimestamp = 0` 的處理）。

**`InitializeAsNewGame()` 預設值**：

| 欄位 | 初始值 |
|---|---|
| `currentDangerLevel` | `"E"` |
| `acceptedMissionCount` | `0` |
| `gameStartTimestamp` | `NowUTC`（由 F-02 取得） |
| `_cachedMaxFactionScore` | `0` |

對應 FT-10 §3.3.3 拓撲順序 row 2、§3.3.4 Degradable 分類、§6.1 #5（FT-10 設計來源清單）。

## 7. 可調參數（Tuning Knobs）

> 時間閘、進度閘、陣營閘的所有閾值均定義於 `WorldDangerTable.csv`，修改平衡值直接改表格，程式碼無需改動。

### 7.1 WorldDangerTable.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `timeThreshold`（D 動盪） | 1 天 | 0 ~ 3 天 | 過短讓玩家開局即進入壓力；過長讓早期遊戲缺乏緊張感 |
| `timeThreshold`（A 末世） | 14 天 | 10 ~ 21 天 | 決定 Game Jam 體驗的節奏上限 |
| `missionCountReq`（C 暗湧） | 15 | 10 ~ 25 | 進度閘太寬鬆會讓玩家被動升危險度，太嚴格讓危險度停滯 |
| `factionScoreReq`（C 暗湧） | 5 | 3 ~ 10 | 陣營閘門檻過低幾乎無意義；過高讓陣營系統成為危險度升級瓶頸 |
| `factionScoreReq`（A 末世） | 20 | 15 ~ 30 | 應搭配 FT-09 陣營分數的累積速度校準 |

---

### 7.2 MissionPoolWeights.csv

| 參數 | 安全考量 |
|------|---------|
| 各危險度的難度權重分布 | `weightS_SSS` 在 E / D 階維持 `0`，避免玩家開局就面對 S 難度任務；A 末世的 `weightF_E` 設為 `0`，強制玩家面對高難度委託 |

---

### 7.3 DebtLimitTable.csv

| 參數 | 安全考量 |
|------|---------|
| `maxDebt` 各階數值 | 絕對值應隨危險度遞增；A 末世的 `-5000` 應搭配該階段任務報酬評估，確保玩家有機會還清債務 |

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-WD-01 | DataManager 初始化後，`GetCurrentLevel()` 回傳 `"E"` |
| AC-WD-02 | `GetDangerData("A").name` 回傳 `"末世"` |
| AC-WD-03 | `GetMaxDebt()` 在危險度 E 時回傳 `-100`；升至 D 後回傳 `-500` |
| AC-WD-04 | `GetPoolWeights()` 在危險度 E 時，`weightF_E = 40`、`weightS_SSS = 0` |
| AC-WD-05 | 時間閘測試：設 `gameStartTimestamp` 使 `elapsedDays = 1`，`acceptedMissionCount = 10`，`factionScoreReq = 0`，呼叫 `CheckLevelUp()`，危險度升至 D |
| AC-WD-06 | 進度閘未達測試：`elapsedDays = 1`，`acceptedMissionCount = 9`，`CheckLevelUp()` 後危險度維持 E |
| AC-WD-07 | 時間閘未達測試：`elapsedDays = 0`，`acceptedMissionCount = 10`，`CheckLevelUp()` 後危險度維持 E |
| AC-WD-08 | 陣營閘測試：危險度在 D，下一階 `factionScoreReq = 5`，呼叫 `OnFactionScoreUpdated(4)` → 維持 D；呼叫 `OnFactionScoreUpdated(5)` → 升至 C |
| AC-WD-09 | `OnMissionAccepted("F")` 在下一階 `minDifficulty = "D"` 時，`acceptedMissionCount` 不增加 |
| AC-WD-10 | `OnMissionAccepted("D")` 在下一階 `minDifficulty = "D"` 時，`acceptedMissionCount` 增加 1 |
| AC-WD-11 | 遞迴升級測試：同時滿足 D→C 與 C→B 的時間閘與陣營閘，且 D→C 的 `missionCountReq` 已達標，`CheckLevelUp()` 後危險度升至 C（而非 B），因升階後 `acceptedMissionCount` 重置為 0，需重新累積 B 階所需任務數 |
| AC-WD-12 | 離線補算：模擬離線 15 天並接受足夠任務與陣營分數，下次開啟呼叫 `CheckLevelUp()` 後危險度正確升至 A |
| AC-WD-13 | 危險度為 A（最高階）時，呼叫 `OnMissionAccepted` 與 `CheckLevelUp()` 均無操作，不報錯 |
| AC-WD-14 | `EventBus.OnDangerLevelChanged` 在每次升階時觸發一次，帶有正確的新 `dangerLevel` 值 |
| AC-WD-15 | C-06 啟動推送：`Start` 完成後，F-03 的 `GetCurrentBankruptcyThreshold()` 回傳 `-100`（E 階 `maxDebt`），非 F-03 的預設值 |
| AC-WD-16 | C-06 升階推送：由 E 升至 D 後，F-03 的 `GetCurrentBankruptcyThreshold()` 回傳 `-500`；升至 A 後回傳 `-5000` |
