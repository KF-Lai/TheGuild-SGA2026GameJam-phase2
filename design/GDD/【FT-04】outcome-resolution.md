# Outcome Resolution 系統設計文件

_建立時間：2026-04-22_
_完成時間：2026-04-22_
_狀態：已設計_
_系統 ID：FT-04_

---

## 1. 概要（Overview）

FT-04 Outcome Resolution 負責在任務計時完成時執行「結算判定」：訂閱 FT-02 的 `OnMissionCompleted` 事件，從 `ActiveMission` 取出派遣時快照的 `finalSuccessRate` / `finalDeathRate`，執行兩次獨立擲骰（`successRoll` 決定成功/失敗、`deathRoll` 決定存活/死亡；成功時死亡率套用 `DEATH_RATE_ON_SUCCESS_MULTIPLIER = 0.5` 折扣），再依序套用該冒險者的 condition 類特質（C-05 § 4.4 規格），映射至 5 種最終結果之一：成功+存活（Idle）、成功+存活(Wounded)（被 `on_death_survive` 救活 → Wounded）、成功+死亡（Dead）、失敗+存活（Wounded，6h 恢復）、失敗+死亡（Dead）。FT-04 據此呼叫 C-02 更新冒險者狀態、呼叫 F-03 `AddReputation` 調整聲望、發布 `OnMissionResolved` 事件供 FT-05 Commission Flow 處理金流、供 FT-09 Faction Story 累積 styleTag、供 P-03 Notification 推播通知，並呼叫 FT-02 `RemoveActiveMission` 清理 runtime 任務。

**核心職責**：

1. 訂閱 `OnMissionCompleted`，從快照執行結算擲骰
2. 套用 condition 類特質（含救活機制）
3. 映射至 5 種最終結果，更新冒險者狀態
4. 計算並套用聲望變化（`ReputationDeltaTable` + `on_*_reputation*` 特質）
5. 發布 `OnMissionResolved` 事件（含 Outcome 快照）供下游系統處理
6. 清理 ActiveMission

**不負責**：

- 成功率/死亡率計算（FT-02 派遣時已快照）
- willingness / 接受拒絕（FT-03）
- 金流（傭金、賠償、預收對帳、condition gold bonus 全由 FT-05 處理）
- 冒險者恢復計時（C-02 `TickWoundedRecovery`）

**資料表**：`ReputationDeltaTable`（PK = difficulty）、`SystemConstants`（`DEATH_RATE_ON_SUCCESS_MULTIPLIER`）

---

## 2. 玩家幻想（Player Fantasy）

你按下派遣的那一刻，其實什麼都還沒發生——那只是一個承諾。真正的重量在幾小時後，當你再次打開遊戲，或從工作中回來坐到電腦前，看到公會總覽那行熟悉的通知：「艾克回來了。」

你屏住呼吸點開。結算視窗會給你五種答案之一。

**成功 + 存活**：報酬入帳、聲望上升、艾克回到名冊，狀態 Idle。你鬆口氣，但那不是鬆懈，是「他做到了」的滿足感。82% 的成功率你按下派遣時覺得穩，結果證明你看得準。

**成功 + 存活(Wounded)**：最稀有、也最震撼的一種。結算視窗跑出一行字——「亡命徒特質觸發，死亡 → 被救活」。任務成功了、報酬進帳了，但艾克帶著重傷回來。你看到的不是勝利，是奇蹟：在 18% 死亡率的死亡判定裡，他本來已經倒下，卻被自己的求生意志拉了回來。6 小時的 Wounded 計時讓你看著這個角色，心想「他這次不是差點沒命——他已經死過一次了」。

**成功 + 死亡**：任務完成了，傭金進了你口袋——但艾克再也不會回來了。B 難度任務死亡率本來就 18%，你以為那是可以承受的風險。公會長的工作就是在這種時刻理解：「我做了對的決策，但他付出了代價。」這不是遊戲懲罰你，是這個世界的規則。

**失敗 + 存活（Wounded）**：他帶傷回來，名冊上的狀態變成 Wounded，倒數 6 小時。你點開他的卡片，看到「失敗 + 生還」，那一刻你比看到死亡更難受——因為他還活著、下次還能用，但你知道是自己判斷失誤把他推進了不該進的任務。同樣是 Wounded，跟「成功被救活」放在一起看，你會發現 Wounded 這個狀態本身就是一個小小的道德羅盤：它既可以是失敗後的僥倖，也可以是勝利後的代價。

**失敗 + 死亡**：最糟的情況。委託方要你賠償，聲望崩跌，而艾克的那一格現在永遠寫著 Dead。你要麼手動除名、讓名冊位置空出來；要麼保留，讓他成為你不敢再犯同樣錯誤的墓碑。

而在所有這些結果之上，還有**意外**。「倒楣鬼」特質讓穩贏任務在最後一刻失手；「亡命徒」讓死亡判定的瞬間奇蹟存活——你眼睜睜看著結算視窗跑出「死亡 → 被救活 → 重傷」的訊息鏈，從絕望直接折返到「他活著」。這些不是 bug，是故事。你不會記得 100 次順利的成功，但你會記得那次 F 階採集任務死了人，或那次 A 難度靠 5% 機率撿回一命。

**核心情感**：**結算不是懲罰，是揭露**。遊戲不是在扣你分，是在把你每一個決定的真實重量攤開給你看。離線機制讓這份重量更誠實——你不是在看即時動畫、你是在承受已經發生的事。而 condition 特質是 NPC 命運感的最後一層——直到最後一刻，他們還是有自己的故事：有的人會在穩贏的任務搞砸，有的人會從必死的絕境爬回來。

---

## 3. 詳細規則（Detailed Rules）

### 3.1 Outcome 資料結構

`Outcome` 是結算管線中流轉的 runtime 物件，不對應任何 CSV。結算完成後其快照存入 `OnMissionResolved` 事件 payload，供 FT-05 / FT-09 / P-03 / P-02 消費。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `activeMissionID` | `int` | 對應 FT-02 ActiveMission，結算後供清理 |
| `missionID` | `int` | FK → C-01 MissionTemplate |
| `adventurerInstanceID` | `int` | FK → C-02 AdventurerInstance |
| `missionDifficulty` | `string` | 快照自 MissionTemplate（F ~ SSS） |
| `missionTypeID` | `int` | 快照自 MissionTemplate，供下游分類 |
| `missionFactionID` | `int` | 快照自 MissionTemplate，供 FT-09 Faction Story |
| `baseReward` | `int` | 快照自 `C01.GetBaseReward(difficulty)`，供 condition gold bonus 計算 |
| `successRoll` | `float` | `[0, 1)` 擲骰值（debug / UI 顯示） |
| `deathRoll` | `float` | `[0, 1)` 擲骰值（debug / UI 顯示） |
| `adjustedDeathRate` | `float` | 實際用於 `deathRoll` 比較的死亡率（成功時已套用 0.5 折扣） |
| `isSuccess` | `bool` | 由 `successRoll` 判定 |
| `isDead` | `bool` | 由 `deathRoll` 判定；condition 特質可改為 `false` |
| `isWounded` | `bool` | 失敗+存活 或 condition 救活後設 `true` |
| `finalStatus` | `enum` | `Idle` / `Wounded` / `Dead`，由上述布林值映射 |
| `reputationDelta` | `int` | 基礎（ReputationDeltaTable）+ `on_*_reputation*` condition 修正 |
| `conditionGoldBonus` | `int` | `on_success_gold_bonus` 觸發產生的 bonus 金額；**由 FT-05 消費**，FT-04 不呼叫 F-03 `AddGold` |
| `triggeredConditionTraits` | `int[]` | 實際觸發（機率判定通過）的 condition traitID；供 P-03 通知與 Debug UI |

---

### 3.2 結算流程（Resolution Pipeline）

FT-04 在 `OnEnable` 階段訂閱 FT-02 `OnMissionCompleted` 事件。

```
OnMissionCompleted(activeMissionID):
    1. activeMission = FT02.GetActiveMission(activeMissionID)
       if activeMission == null → Debug.LogError, return
    2. adventurer    = C02.GetAdventurer(activeMission.adventurerInstanceID)
       if adventurer == null → Debug.LogError, return
       if adventurer.status != Dispatched → Debug.LogError, return   // 邊緣案例見 §5.2
    3. template      = C01.GetTemplate(activeMission.missionID)
       if template == null → Debug.LogError, return
    4. outcome = BuildOutcomeSnapshot(activeMission, adventurer, template)
    5. RollSuccessAndDeath(outcome, activeMission)             // 見 3.3
    6. ApplyBaseReputationDelta(outcome)                        // 見 3.6
    7. C05.ApplyConditionTraits(outcome, adventurer.traitIDs)   // 見 3.4（C-05 § 4.4）
    8. outcome.finalStatus = MapFinalStatus(outcome)            // 見 3.5
    9. ApplyAdventurerStatus(outcome)                           // 見 3.7
   10. F03.AddReputation(outcome.reputationDelta)
   11. EventBus.Publish(OnMissionResolved, outcome)
   12. FT02.RemoveActiveMission(outcome.activeMissionID)
```

**順序不可調換**。關鍵原則：

- **擲骰（5）先於 condition（7）**：condition 只能修正已擲出的結果，不能影響擲骰本身
- **基礎聲望 delta（6）先於 condition（7）**：condition `on_*_reputation*` 以 `+=` 疊加於基礎 delta 上；反過來則基礎會覆蓋 condition 修正
- **condition（7）先於 MapFinalStatus（8）**：救活結果必須反映在最終狀態映射
- **ApplyAdventurerStatus（9）先於 AddReputation（10）**：冒險者狀態先確定，避免 F-03 觸發破產事件時 UI 讀到半成品
- **Publish（11）先於 RemoveActiveMission（12）**：訂閱者在 ActiveMission 清理前仍可透過 `outcome.activeMissionID` 追溯

> **跨系統時序註記（Bankruptcy × Reputation）**：步驟 10 `AddReputation` 若在 `currentGold < 0` 且倒數到期時可能觸發 F-03 破產狀態轉為 `Bankrupt`，發生在步驟 11 `OnMissionResolved` **之前**。此情境的預期行為由 FT-05 § 5.5.1 承接——FT-05 接到事件後仍執行 `AddGoldAllowBankruptcy` 完成金流，`bankruptcyStateBefore / After` 皆快照為 `Bankrupt`。Game Over 終止時機由 FT-06 Game Over 流程控制（F-02 `PauseTick`），不在 FT-04 / FT-05 範疇。

---

### 3.3 兩次獨立擲骰

```
RollSuccessAndDeath(outcome, activeMission):
    // 成功骰
    outcome.successRoll = Random.Range(0.0, 1.0)    // [0, 1)
    outcome.isSuccess   = outcome.successRoll < activeMission.finalSuccessRate

    // 成功時死亡率折扣
    if outcome.isSuccess:
        outcome.adjustedDeathRate = activeMission.finalDeathRate × DEATH_RATE_ON_SUCCESS_MULTIPLIER
    else:
        outcome.adjustedDeathRate = activeMission.finalDeathRate

    // 死亡骰
    outcome.deathRoll = Random.Range(0.0, 1.0)
    outcome.isDead    = outcome.deathRoll < outcome.adjustedDeathRate

    // 失敗+存活 預寫 isWounded（讓 5 種結果的 MapFinalStatus 統一由 3 個 bool 推導）
    outcome.isWounded = (!outcome.isSuccess && !outcome.isDead)
```

- `DEATH_RATE_ON_SUCCESS_MULTIPLIER = 0.5`（SystemConstants）
- 兩次擲骰**獨立**，不共用骰子
- `adjustedDeathRate` 快照於 Outcome，供 UI / debug 顯示「原本 X%，成功折扣後 X×0.5」

---

### 3.4 condition 特質套用

FT-04 委託 C-05 執行 condition 特質的結算邏輯（C-05 § 4.4 `ApplyConditionTraits` 規格）。該規格按 `adventurer.traitIDs` 順序逐一檢查並修改 outcome：

| effectTarget | 作用 | Outcome 修改 |
|-------------|------|-------------|
| `on_success_gold_bonus` | 成功時機率觸發 | `conditionGoldBonus += baseReward × 0.5` |
| `on_success_reputation_bonus` | 成功時固定加值 | `reputationDelta += effectValue`（正數） |
| `on_fail_reputation` | 失敗時固定扣值 | `reputationDelta += effectValue`（負數） |
| `on_death_survive` | 死亡時機率救活（成功/失敗皆判） | `isDead = false, isWounded = true` |
| `on_fail_survive` | 僅失敗+死亡時機率救活 | `isDead = false, isWounded = true` |

**FT-04 對 C-05 § 4.4 規格的實作補充**：

1. Outcome 無通用 `goldDelta` 欄位——C-05 § 4.4 的 `on_success_gold_bonus` 改寫入 `conditionGoldBonus`（介面對齊，語義不變，C-05 § 4.4 已同步更新）
2. 觸發成功（機率判定通過）的 condition traitID 由 C-05 追加至 `outcome.triggeredConditionTraits`
3. `on_death_survive` 與 `on_fail_survive` 同時存在時，依 `traitIDs` 陣列順序執行；先觸發者將 `isDead` 改為 `false` 後，後者自然跳過（C-05 規格已保證）

---

### 3.5 5 種最終結果映射

**擲骰後（condition 之前）的初始值**：

| 擲骰組合 | `isSuccess` | `isDead` | `isWounded` |
|---------|-------------|----------|-------------|
| 成功 + 存活 | `true` | `false` | `false` |
| 成功 + 死亡 | `true` | `true` | `false` |
| 失敗 + 存活 | `false` | `false` | **`true`**（預寫） |
| 失敗 + 死亡 | `false` | `true` | `false` |

**condition 介入後可能的狀態轉移**：

| 擲骰後 | condition 事件 | 轉移後 |
|-------|--------------|-------|
| `(T, T, F)` 成功+死亡 | `on_death_survive` 觸發 | `(T, F, T)` |
| `(F, T, F)` 失敗+死亡 | `on_death_survive` 或 `on_fail_survive` 觸發 | `(F, F, T)` |

**MapFinalStatus**（condition 套用完畢後）：

```
MapFinalStatus(outcome):
    if outcome.isDead:    return Dead
    if outcome.isWounded: return Wounded
    return Idle
```

**最終映射表（5 種結果）**：

| `(isSuccess, isDead, isWounded)` | `finalStatus` | 可達來源 |
|--------------------------------|---------------|---------|
| `(T, F, F)` | **Idle** | 成功 + 存活 |
| `(T, F, T)` | **Wounded** | 成功+死亡 被 `on_death_survive` 救活 |
| `(T, T, F)` | **Dead** | 成功 + 死亡（無救活） |
| `(F, F, T)` | **Wounded** | 失敗+存活；或失敗+死亡被 `on_*_survive` 救活 |
| `(F, T, F)` | **Dead** | 失敗 + 死亡（無救活） |

> `(F, F, F)` 與 `(T, T, T)` 為**不可能狀態**：前者因失敗+存活在擲骰階段已預寫 `isWounded=true`；後者因 condition 救活強制將 `isDead` 設為 `false`。

---

### 3.6 聲望計算

```
ApplyBaseReputationDelta(outcome):
    entry = ReputationDeltaTable[outcome.missionDifficulty]
    if outcome.isSuccess:
        outcome.reputationDelta = entry.successDelta
    else:
        outcome.reputationDelta = entry.failDelta
```

**`ReputationDeltaTable` schema**：

| 欄位 | 型別 | 說明 |
|------|------|------|
| `difficulty` | `string` (PK) | F / E / D / C / B / A / S / SS / SSS |
| `successDelta` | `int` | 成功時聲望變化（正整數） |
| `failDelta` | `int` | 失敗時聲望變化（負整數） |

**Game Jam 初始資料**（建議值，實際數值需調平驗證）：

| difficulty | successDelta | failDelta |
|-----------|-------------|-----------|
| F | +3 | -1 |
| E | +4 | -2 |
| D | +5 | -3 |
| C | +6 | -4 |
| B | +8 | -6 |
| A | +6 | -8 |
| S | +8 | -10 |
| SS | +10 | -12 |
| SSS | +12 | -15 |

> **基礎 delta 套用於 condition 之前**（見 3.2 步驟 6 / 7）；condition 特質 `on_*_reputation*` 以 `+=` 方式疊加於基礎 delta 上。最終 `reputationDelta` 傳入 F-03 `AddReputation`，該系統自行處理 `[-100, 100]` clamp 與破產警告連動，FT-04 不額外 clamp。

---

### 3.7 冒險者狀態更新

```
ApplyAdventurerStatus(outcome):
    switch outcome.finalStatus:
        case Idle:    C02.UpdateStatus(outcome.adventurerInstanceID, Idle)
        case Wounded: C02.SetWounded(outcome.adventurerInstanceID)
        case Dead:    C02.UpdateStatus(outcome.adventurerInstanceID, Dead)
```

- `C02.UpdateStatus(id, Idle)` 對 `Dispatched` 狀態呼叫時自動清除 `currentMissionID`（由 C-02 § 3.5 合約保證）
- `C02.SetWounded` 依 C-02 § 4.1 規格計算並寫入 `woundedUntilTimestamp`（`currentUTC + WOUNDED_RECOVERY_HOURS × 3600`），並自動清除 `currentMissionID`
- `C02.UpdateStatus(id, Dead)` 自動清除 `currentMissionID = 0`，冒險者保留於名冊直至手動除名

---

### 3.8 事件發布與 ActiveMission 清理

**事件**：

```
event OnMissionResolved(Outcome outcome);
```

訂閱者責任：

| 訂閱者 | 行為 |
|--------|------|
| **FT-05 Guild Gold Flow** | 依 `isSuccess`、`missionDifficulty`、`baseReward`、`conditionGoldBonus` 計算傭金 / 賠償 / 對帳，呼叫 F-03 `AddGoldAllowBankruptcy` |
| **FT-09 Faction Story** | 依 `missionFactionID`、`isSuccess` 累積該陣營的 styleTag |
| **P-03 Notification** | 依 `finalStatus`、`triggeredConditionTraits` 選擇通知模板，推播桌面通知 |
| **P-02 Main UI** | 更新名冊、刷新結算視窗、顯示結算動畫 |

事件傳遞後，FT-04 立即呼叫 `FT02.RemoveActiveMission(outcome.activeMissionID)` 清理。訂閱者若需保留結算歷史，應自行 copy outcome（FT-04 不維護歷史清單）。

---

### 3.9 查詢 API

FT-04 本身**無對外公開 API**：所有輸入來自 FT-02 `OnMissionCompleted` 事件訂閱，所有輸出透過 `OnMissionResolved` 事件與對 C-02 / F-03 / FT-02 的直接呼叫完成。此設計避免下游系統與 FT-04 產生強耦合，所有跨系統通訊統一走 EventBus。

> 若未來需要「重新結算」或「手動觸發結算」的 debug 功能，可新增 `ResolveNow(int activeMissionID)` 這類測試 API，Game Jam 階段不納入範疇。

---

## 4. 公式（Formulas）

### 4.1 擲骰判定公式

```
// 成功骰
successRoll = Random.Range(0.0, 1.0)          // [0, 1)
isSuccess   = successRoll < finalSuccessRate

// 成功時死亡率折扣
adjustedDeathRate = finalDeathRate × DEATH_RATE_ON_SUCCESS_MULTIPLIER   if isSuccess
                    finalDeathRate                                       otherwise

// 死亡骰
deathRoll = Random.Range(0.0, 1.0)
isDead    = deathRoll < adjustedDeathRate

// 失敗+存活 預寫 isWounded
isWounded = (!isSuccess && !isDead)
```

**變數定義**：

| 變數 | 型別 | 範圍 | 來源 |
|------|------|------|------|
| `finalSuccessRate` | `float` | `[0.0, 1.0]` | FT-02 ActiveMission 快照（派遣時鎖定） |
| `finalDeathRate` | `float` | `[0.0, 1.0]` | FT-02 ActiveMission 快照（派遣時鎖定） |
| `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | `float` | `[0.0, 1.0]`（預設 `0.5`） | SystemConstants |
| `adjustedDeathRate` | `float` | `[0.0, 1.0]` | 本公式計算 |
| `successRoll` / `deathRoll` | `float` | `[0.0, 1.0)` | `Random.Range` |
| `isSuccess` / `isDead` / `isWounded` | `bool` | `{true, false}` | 本公式計算 |

> **兩次擲骰獨立**，不共用亂數。`adjustedDeathRate` 僅在成功路徑打 0.5 折扣；失敗路徑使用原始 `finalDeathRate`。

---

### 4.2 聲望 delta 公式

```
baseDelta = ReputationDeltaTable[difficulty].successDelta   if isSuccess
            ReputationDeltaTable[difficulty].failDelta      otherwise

for each triggered trait in on_*_reputation* condition 特質:
    conditionDelta += trait.effectValue

reputationDelta = baseDelta + conditionDelta
```

**變數定義**：

| 變數 | 型別 | 範圍 | 來源 |
|------|------|------|------|
| `baseDelta` | `int` | `[-15, +12]`（依 ReputationDeltaTable 當前設定） | 3.6 節資料表 |
| `conditionDelta` | `int` | `[-5 × 特質數, +5 × 特質數]`（依 C-05 § 7.1 推薦範圍 `±5`） | condition 累加 |
| `reputationDelta` | `int` | 無界，最終 clamp 由 F-03 處理 | 本公式 |

> `reputationDelta` **不由 FT-04 clamp**；傳入 `F03.AddReputation` 後，F-03 自行 clamp 至 `[-100, 100]` 並觸發 `OnReputationChanged` 與破產警告狀態機。

---

### 4.3 完整結算範例

**範例 A：成功 + 存活（最常見路徑）**

> C 階戰士派 B 難度討伐（派遣時快照 `finalSuccessRate=0.63, finalDeathRate=0.18`），無 condition 特質觸發

| 步驟 | 值 | 說明 |
|------|----|------|
| `successRoll` | `0.42` | `< 0.63` → `isSuccess = true` |
| `adjustedDeathRate` | `0.09` | `0.18 × 0.5`（成功折扣） |
| `deathRoll` | `0.55` | `> 0.09` → `isDead = false` |
| `isWounded`（預寫） | `false` | 成功不走失敗+存活預寫分支 |
| condition 套用 | — | 無 condition 觸發 |
| MapFinalStatus | **Idle** | `(T, F, F)` → Idle |
| `baseDelta` | `+4` | B 難度 `successDelta` |
| `conditionDelta` | `0` | — |
| `reputationDelta` | **`+4`** | 傳入 `F03.AddReputation(+4)` |
| 冒險者狀態 | Dispatched → **Idle** | `C02.UpdateStatus(id, Idle)` |

---

**範例 B：成功 + 死亡 → `on_death_survive` 救活 → Wounded**

> A 階遊俠派 S 難度討伐（派遣時快照 `finalSuccessRate=0.70, finalDeathRate=0.40`），持有「亡命徒」特質（`on_death_survive`, `effectValue = 0.30`）

| 步驟 | 值 | 說明 |
|------|----|------|
| `successRoll` | `0.35` | `< 0.70` → `isSuccess = true` |
| `adjustedDeathRate` | `0.20` | `0.40 × 0.5`（成功折扣讓死亡率從 40% 降到 20%） |
| `deathRoll` | `0.12` | `< 0.20` → `isDead = true` |
| `isWounded`（預寫） | `false` | 成功不預寫 wounded |
| condition `on_death_survive` | `Random = 0.18 < 0.30` → 觸發 | `isDead = false`, `isWounded = true` |
| `triggeredConditionTraits` | `[亡命徒.traitID]` | 供通知與 Debug |
| MapFinalStatus | **Wounded** | `(T, F, T)` → Wounded |
| `baseDelta` | `+8` | S 難度 `successDelta` |
| `conditionDelta` | `0` | 亡命徒無聲望效果 |
| `reputationDelta` | **`+8`** | 傳入 `F03.AddReputation(+8)` |
| 冒險者狀態 | Dispatched → **Wounded（6h 恢復）** | `C02.SetWounded(id)` |

> 這是「成功+存活(Wounded)」的唯一到達路徑：必須先擲出成功+死亡，再由 condition 救活。

---

**範例 C：失敗 + 死亡 + 聲望 condition 疊加**

> F 階斥候派 C 難度調查（派遣時快照 `finalSuccessRate=0.25, finalDeathRate=0.13`），持有「玻璃心」特質（`on_fail_reputation`, `effectValue = -3`）

| 步驟 | 值 | 說明 |
|------|----|------|
| `successRoll` | `0.77` | `> 0.25` → `isSuccess = false` |
| `adjustedDeathRate` | `0.13` | 失敗路徑不打折 |
| `deathRoll` | `0.08` | `< 0.13` → `isDead = true` |
| `isWounded`（預寫） | `false` | `!isSuccess && !isDead` 為 `false`（isDead 是 true） |
| condition `on_fail_reputation` | 觸發（固定值，非機率） | `reputationDelta += -3` |
| `triggeredConditionTraits` | `[玻璃心.traitID]` | |
| MapFinalStatus | **Dead** | `(F, T, F)` → Dead |
| `baseDelta` | `-4` | C 難度 `failDelta` |
| `conditionDelta` | `-3` | 玻璃心 `on_fail_reputation` |
| `reputationDelta` | **`-7`** | 傳入 `F03.AddReputation(-7)` |
| 冒險者狀態 | Dispatched → **Dead**（保留於名冊） | `C02.UpdateStatus(id, Dead)` |

> condition 套用順序：`ApplyBaseReputationDelta` 先設 `reputationDelta = -4`，再由 C-05 `ApplyConditionTraits` 以 `+= -3` 疊加至 `-7`。

---

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `ReputationDeltaTable` 缺少某 difficulty 的行 | `Debug.LogError`；查詢時回傳 `(0, 0)`（聲望無變化，最保守） |
| `ReputationDeltaTable.successDelta` 為負值 或 `failDelta` 為正值 | `Debug.LogWarning`（符號異常可能是設計錯誤），但不修正，依 CSV 值執行 |
| `SystemConstants` 缺少 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | `Debug.LogError`，fallback 使用 `0.5`（與 § 3.3 預設值一致，避免靜默把遊戲感拉向硬核） |
| `DEATH_RATE_ON_SUCCESS_MULTIPLIER` 超出 `[0, 1]` | `Debug.LogWarning`，載入時 clamp 至 `[0, 1]` |

---

### 5.2 結算觸發（OnMissionCompleted）

| 情況 | 處理方式 |
|------|---------|
| 事件的 `activeMissionID` 在 FT-02 查無 ActiveMission | `Debug.LogError`，直接 `return`，不執行任何後續步驟 |
| `ActiveMission.adventurerInstanceID` 在 C-02 查無冒險者 | `Debug.LogError`，`return`；ActiveMission 不清理（屬資料錯誤，需人工介入） |
| `ActiveMission.missionID` 在 C-01 查無模板 | `Debug.LogError`，`return`；ActiveMission 不清理 |
| 冒險者狀態非 `Dispatched`（例如已被外部改成 Dead） | `Debug.LogError`，`return`；防禦性檢查，實務上不應發生 |
| 同一 `activeMissionID` 的 `OnMissionCompleted` 重複發布 | 第二次 `GetActiveMission` 回傳 `null`（已清理），按第一列處理，`LogError` 後安全退出 |
| `ActiveMission.finalSuccessRate` / `finalDeathRate` 超出 `[0, 1]` | FT-02 派遣時已 clamp；若仍超出視為資料異常，擲骰前 clamp 至 `[0, 1]` 並 `LogWarning` |

---

### 5.3 擲骰與 Outcome 邊界

| 情況 | 處理方式 |
|------|---------|
| `finalSuccessRate = 1.0` | `successRoll ∈ [0, 1)` 必 `< 1.0` → `isSuccess = true`（100% 成功） |
| `finalSuccessRate = 0.0` | `successRoll` 必 `≥ 0.0` → `isSuccess = false`（100% 失敗） |
| `finalDeathRate = 0.0` | `isDead` 必為 `false`（不可能死亡） |
| `finalDeathRate = 1.0`，成功折扣後 `adjustedDeathRate = 0.5` | 成功路徑 50% 機率死亡，符合設計 |
| `finalDeathRate = 1.0`，失敗路徑 | 失敗必死亡（100%） |
| `finalSuccessRate = 0.0` 且 `successRoll = 0.0` | `0.0 < 0.0` 為 `false` → `isSuccess = false`（「0% 成功率絕不成功」的一致性） |

---

### 5.4 condition 特質互動

| 情況 | 處理方式 |
|------|---------|
| 冒險者無任何 condition 特質 | `ApplyConditionTraits` 跳過，outcome 不變 |
| 同時持有 `on_death_survive` 與 `on_fail_survive`（例：死亡時） | 依 `traitIDs` 順序執行；先觸發者將 `isDead = false`，後者因 `isDead` 已為 false 自然跳過（C-05 § 4.4 保證） |
| `on_death_survive` 機率 `1.0` 且 `isDead = true` | 必救活，`isDead = false`, `isWounded = true` |
| `on_success_gold_bonus` 觸發多次（未來多特質擴充時） | 每次 `conditionGoldBonus += baseReward × 0.5` 累積；Game Jam 階段每冒險者最多 1 個此類特質 |
| condition 特質 `effectTarget` 為未知值 | C-05 `ApplyConditionTraits` 輸出 `LogWarning` 並跳過，FT-04 不額外處理 |
| condition 執行中拋出例外 | FT-04 不 try-catch；例外向上傳播，結算中止（步驟 8-12 不執行）。屬嚴重錯誤，需 root cause 修復 |

---

### 5.5 聲望計算

| 情況 | 處理方式 |
|------|---------|
| `missionDifficulty` 不在 `ReputationDeltaTable` 中 | `Debug.LogError`，`baseDelta = 0`，流程繼續（聲望不變動比中止結算安全） |
| `reputationDelta` 計算結果極大（例如 `+100`） | FT-04 不 clamp；F-03 `AddReputation` 自行 clamp 至 `[-100, 100]` |
| `F03.AddReputation` 因重入防護失敗 | F-03 內部 `LogError`，FT-04 不重試；此次聲望變動遺失（極端案例，需 root cause 修復） |
| 聲望變動觸發 F-03 破產警告 | F-03 自行處理；FT-04 不感知此狀態變化 |

---

### 5.6 事件發布與清理

| 情況 | 處理方式 |
|------|---------|
| `OnMissionResolved` 訂閱者拋出例外 | FT-04 不 try-catch；例外傳播由 EventBus 設計決定（假設 EventBus 為訂閱者隔離例外，即單一訂閱者錯誤不影響其他） |
| 訂閱者修改傳入的 `outcome` 物件 | FT-04 不防禦；傳遞同一物件的設計允許事件鏈延伸（類似 C-05 `ApplyConditionTraits` 的修改模式）。訂閱者自律，不應修改已結算資料 |
| `RemoveActiveMission` 傳入的 `activeMissionID` 已不存在 | FT-02 內部 `LogWarning` 無操作；FT-04 不重複檢查 |

---

### 5.7 離線與存檔

| 情況 | 處理方式 |
|------|---------|
| 離線期間多個 ActiveMission 到期 | FT-02 `TickCompletionCheck` 一次 Tick 發布多個 `OnMissionCompleted`；FT-04 依序結算，每筆獨立擲骰、獨立套用 condition、獨立發布 `OnMissionResolved` |
| 存檔載入時冒險者狀態為 Dispatched 但對應 ActiveMission 不存在 | FT-02 存檔策略負責（FT-02 § 5.5 已定義：移除 ActiveMission 並發布 `OnMissionCancelled`）；FT-04 不處理此情況 |
| 結算中途玩家強制關閉遊戲（pipeline 執行到一半） | 下次啟動時 FT-02 仍持有 ActiveMission（未清理），`TickCompletionCheck` 重發 `OnMissionCompleted`，FT-04 **重新擲骰、重新結算**（新亂數）。屬不可避免的重擲，玩家無法預期此行為——**需在 P-03 / UI 層避免在結算動畫中途允許玩家強制退出**。**已知風險（Save Scum）**：在純客戶端版本下，玩家可在結算前手動備份存檔、看到不好的結果後關 process、復原存檔重試，達成重擲效果。Game Jam 階段為純 C 端架構，**不防此行為**；未來若加入伺服器端（S 端），結算結果改由伺服器擲骰並下發，重擲問題即自然解決 |
| 結算步驟 9-11 之間遊戲強制關閉（狀態已改、聲望未加或事件未發） | 同上，重啟後整個 pipeline 重跑一次；C-02 冒險者狀態若已變更為 Dead 會在步驟 2 的「非 Dispatched」檢查時擋掉，結算中斷，ActiveMission 殘留（需存檔層補救）。屬罕見 crash 邊界，建議實作時以 Save-point 或 WAL 檔案緩解，但 Game Jam 不納入範疇 |

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-04 依賴的系統）

| 系統 | 依賴介面 | 用途 | 方向 |
|------|---------|------|------|
| **F-01 DataManager** | `GetTable<ReputationDeltaTable>()`, `GetTable<SystemConstants>()` | 取得 `successDelta` / `failDelta` 行、讀取 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | 單向，FT-04 → F-01 |
| **FT-02 Mission Dispatch** | Event: `OnMissionCompleted(int activeMissionID)`<br>API: `GetActiveMission(id) : ActiveMission`<br>API: `RemoveActiveMission(id) : void` | 觸發結算入口；讀取派遣時快照的 `finalSuccessRate` / `finalDeathRate` / `adventurerInstanceID` / `missionID`;結算完畢後清理 runtime 任務 | 單向，FT-04 → FT-02 |
| **C-01 Mission Database** | `GetTemplate(missionID) : MissionTemplate`<br>`GetBaseReward(difficulty) : int` | 取得 `missionDifficulty` / `missionTypeID` / `missionFactionID`（快照入 Outcome 供下游分類）；取得 `baseReward` 供 C-05 `on_success_gold_bonus` 計算 `conditionGoldBonus` | 單向，FT-04 → C-01 |
| **C-02 Adventurer Management** | `GetAdventurer(instanceID) : AdventurerInstance`<br>`UpdateStatus(id, Idle/Dead) : void`<br>`SetWounded(id) : void` | 讀取 `traitIDs` 供 C-05 套用；結算後依 `finalStatus` 更新冒險者；`UpdateStatus` / `SetWounded` 內部保證 `currentMissionID = 0` invariant（C-02 § 3.5） | 單向，FT-04 → C-02 |
| **C-05 Trait System** | `ApplyConditionTraits(Outcome outcome, int[] traitIDs) : void` | 委託 C-05 依 `effectTarget` 修改 Outcome（擲骰結果、聲望 delta、救活、gold bonus）；C-05 § 4.4 規格為唯一合約 | 單向，FT-04 → C-05 |
| **F-03 Resource Management** | `AddReputation(int delta) : void` | 聲望變動統一走 F-03；clamp 與破產警告連動由 F-03 自理 | 單向，FT-04 → F-03 |

> **F-02 Time System**：FT-04 不直接依賴——計時完成由 FT-02 `TickCompletionCheck` 消費 F-02 時間差後發布 `OnMissionCompleted`。FT-04 只訂閱事件。
>
> **C-03 Profession / C-04 Race**：FT-04 不直接依賴——冒險者的 profession / race 對結算結果的修正已於 FT-02 派遣時併入 `finalSuccessRate` / `finalDeathRate` 快照，FT-04 不重讀這些欄位。

---

### 6.2 下游依賴（依賴 FT-04 的系統）

| 系統 | 訂閱介面 | 行為 | FT-04 Outcome 欄位使用 |
|------|---------|------|----------------------|
| **FT-05 Guild Gold Flow** | Event: `OnMissionResolved(Outcome)` | 依結算結果執行金流：成功時撥付 `baseReward × (1 - COMMISSION_RATE)` + `conditionGoldBonus`；失敗時計算賠償；呼叫 F-03 `AddGoldAllowBankruptcy` | `isSuccess`, `missionDifficulty`, `baseReward`, `conditionGoldBonus`, `activeMissionID` |
| **FT-09 Faction Story** | Event: `OnMissionResolved(Outcome)` | 依 `missionFactionID` 累積該陣營的 styleTag；計分規則由 FT-09 最終決定 | `missionFactionID`, `isSuccess`, `missionTypeID` |
| **P-02 Main UI** | Event: `OnMissionResolved(Outcome)` | 更新名冊（狀態 Idle / Wounded / Dead）；刷新結算視窗與動畫；顯示 condition 觸發訊息 | `adventurerInstanceID`, `finalStatus`, `successRoll`, `deathRoll`, `triggeredConditionTraits` |
| **P-03 Notification System** | Event: `OnMissionResolved(Outcome)` | 依 `finalStatus` + `triggeredConditionTraits` 選擇 `NotificationTemplate`，推播桌面通知 | `finalStatus`, `triggeredConditionTraits`, `missionDifficulty` |
| **FT-10 Save/Load** | — | FT-04 **無持久狀態**（不維護結算歷史、無 runtime 快取）；不參與存檔序列化。訂閱者若需持久化歷史，各自負責 | — |

> **Outcome 物件生命週期**：由 FT-04 在結算管線中建立，經 `OnMissionResolved` 事件傳遞，事件消費完畢後丟棄。訂閱者若需保留應自行 copy（FT-04 不維護歷史清單）。

---

### 6.3 雙向依賴對照表

為確保 `.claude/rules/design-docs.md` 規定的「雙向依賴」要求，各系統 GDD 需有對應的反向聲明：

| 對方 GDD | 方向 | 對方 GDD 應記錄 FT-04 的位置 | 狀態 |
|---------|------|---------------------------|------|
| **FT-02** | FT-04 訂閱 FT-02 事件 | FT-02 § 6.2 下游列出 FT-04 為 `OnMissionCompleted` 訂閱者 | ✅ 已登記（FT-02 § 6.2） |
| **C-01** | FT-04 呼叫 C-01 查詢 | C-01 § 6.2 下游列出 FT-04 | ✅ 已登記（C-01 § 6.2，本輪補入） |
| **C-02** | FT-04 呼叫 C-02 更新狀態 | C-02 § 6.2 下游列出 FT-04；§ 3.5 `UpdateStatus` / `SetWounded` invariant 已對齊 | ✅ 已登記（C-02 § 6.2） |
| **C-05** | FT-04 委託 C-05 `ApplyConditionTraits` | C-05 § 6.2 下游列出 FT-04；§ 4.4 已對齊 `conditionGoldBonus` 命名 | ✅ 已登記（C-05 § 6.2） |
| **F-03** | FT-04 呼叫 `AddReputation` | F-03 § 6.2 下游列出 FT-04 為聲望變動來源之一 | ✅ 已登記（F-03 § 6.2） |
| **F-01** | FT-04 讀取 CSV 表 | F-01 § 6.2 下游列出 FT-04 | ✅ 已登記（F-01 § 6.2） |
| **FT-05** | FT-05 訂閱 `OnMissionResolved` | FT-05 § 6.1 上游列出 FT-04 | ✅ 已登記（FT-05 § 3.2／§ 6.1） |
| **FT-09** | FT-09 訂閱 `OnMissionResolved` | FT-09 § 6.1 上游列出 FT-04 | ⏳ FT-09 待設計 |
| **P-02 / P-03** | 訂閱 `OnMissionResolved` | P-02 / P-03 設計時須列出 FT-04 為上游 | ⏳ 待設計 |

> 未設計的下游（FT-09, P-02, P-03）在其 GDD 撰寫時須同步反向聲明。已設計的系統雙向依賴已全部對齊。

---

### 6.4 無循環依賴

FT-04 的所有依賴皆為**單向向上**（指向更基礎的層）：

```
FT-04 Outcome Resolution
  ├─► FT-02 Mission Dispatch ──► C-01, C-02, F-01, F-02
  ├─► C-01 Mission Database ──► F-01
  ├─► C-02 Adventurer Mgmt  ──► F-01
  ├─► C-05 Trait System     ──► F-01
  ├─► F-03 Resource Mgmt    ──► F-01
  └─► F-01 DataManager
```

下游系統（FT-05, FT-09, P-02, P-03）透過 `OnMissionResolved` 事件單向訂閱，不反向呼叫 FT-04 API。FT-04 本身**無對外公開 API**（§ 3.9），杜絕下游產生強耦合或循環呼叫的可能。

---

## 7. 可調參數（Tuning Knobs）

FT-04 的可調參數皆以**資料驅動**為原則，透過 CSV 表格載入，不寫死在程式碼。參數分為三類：系統常數、聲望表、事件責任分界。

### 7.1 SystemConstants（系統常數）

| 參數 | 型別 | 預設值 | 安全範圍 | 影響的遊戲感受 |
|------|------|--------|---------|-------------|
| `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | `float` | `0.5` | `[0.0, 1.0]` | 成功路徑的死亡機率折扣。`1.0` = 死亡率不打折（高難度成功仍高機率死亡，偏硬核）；`0.5` = 成功時死亡機率減半（建議值，鼓勵玩家冒險接高難度）；`0.0` = 成功必存活（將「死於任務」完全限制在失敗路徑，降低隨機挫折） |
| `WOUNDED_RECOVERY_HOURS` | `int` | `6`（由 C-02 擁有） | `[1, 24]` | Wounded 冒險者休息時長。FT-04 僅透過 `C02.SetWounded` 間接觸發，實際值由 C-02 § 7 調整 |

> **FT-04 擁有的常數僅有 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` 一個**。其他結算相關數值（`COMMISSION_RATE`, `PENALTY_RATE`, `REPUTATION_MIN/MAX`）分別屬於 FT-05 與 F-03，不在本 GDD 的可調範圍內。

---

### 7.2 ReputationDeltaTable（聲望變化表）

PK: `difficulty`（F / E / D / C / B / A / S / SS / SSS）

| 欄位 | 型別 | 預設值範圍 | 安全範圍 | 影響 |
|------|------|----------|---------|------|
| `successDelta` | `int` | `+1 ~ +12`（見 § 3.6） | `[+1, +30]`（若超過，會讓聲望上升過快，破壞 GuildLevelTable 門檻節奏） | 成功任務的聲望回報。建議維持「難度愈高 delta 愈大」的單調遞增，並確保 F ~ SSS 總跨距約為 10 倍（目前 1 : 12） |
| `failDelta` | `int` | `-1 ~ -15`（見 § 3.6） | `[-30, -1]`（若超過 `-30`，單次失敗易觸發破產警告，過度懲罰離線結算） | 失敗任務的聲望扣分。建議保持「失敗懲罰略高於成功回報」（高難度尤其明顯，SSS 為 `+12 / -15`），鼓勵穩健決策 |

**調整準則**：

- **平衡指標**：`successDelta + failDelta` 的算術平均應維持**接近 0 或略負**。目前 Game Jam 初始值的平均為 `-1.2`（略偏失敗懲罰），若玩家覺得聲望成長過難可微調至 `0` 附近
- **跨難度差距**：相鄰難度的 `successDelta` 差距建議 1~2 點（例：B=+4, A=+6）；`failDelta` 差距建議 2~3 點（例：B=-6, A=-8）。差距過小會讓難度抉擇無意義，差距過大會讓低難度無回報
- **觸發聲望門檻**：F-03 `AddReputation` 會觸發破產警告（依 `BankruptcyThresholdTable`）、FT-06 公會等級解鎖（依 `GuildLevelTable`）。調整本表時需連動檢查這兩個下游表的門檻是否仍合理

---

### 7.3 Outcome 欄位外溢控制（職責分界，非 tunable value）

下列為**設計決策的可調點**，非數值但影響系統範疇：

| 可調點 | 預設 | 替代選項 | 影響 |
|-------|------|---------|------|
| `conditionGoldBonus` 金流歸屬 | FT-05 消費（FT-04 只填欄位） | 若改為 FT-04 直接呼叫 `F03.AddGold`，可減少一次事件傳遞 | 維持現設計可讓金流完全由 FT-05 統一管理、方便未來加入稅率/工會抽成等複雜規則 |
| Condition 觸發的聲望 delta 是否受 `F-03` clamp 影響 | 是（最終 clamp 由 F-03 處理） | 若改為 FT-04 預先 clamp，可讓 Outcome 中的 `reputationDelta` 與實際套用值一致 | 維持現設計，避免 FT-04 重複實作 clamp 邏輯；代價是 Outcome 顯示的 delta 可能與實際生效值略有差異（僅在聲望接近上下限時發生） |
| 結算歷史持久化 | FT-04 不持久化，訂閱者自行保存 | 若需「結算日誌」功能，可新增 `ResolutionLogTable` 運行時表 | Game Jam 範疇內不需要，所有歷史顯示由 P-02 即時快取處理 |

---

### 7.4 CSV 資料表路徑

| 表格 | 路徑 | PK | 來源 |
|------|------|-----|------|
| `ReputationDeltaTable` | `Assets/Resources/Data/Tables/ReputationDeltaTable.csv` | `difficulty` | Google Sheets → Reputation Delta 頁簽 |
| `SystemConstants` | `Assets/Resources/Data/Tables/SystemConstants.csv` | `key` | Google Sheets → System Constants 頁簽 |

> 修改這兩個表後，需在 Unity Editor 重新匯入 CSV 或重啟遊戲才能生效（由 F-01 DataManager 控制，FT-04 不額外快取）。

---

## 8. 驗收標準（Acceptance Criteria）

每條標準皆為**可測試條件**——QA 測試者需能透過 Unity Editor、Debug UI 或單元測試明確判定 Pass / Fail。

### 8.1 資料載入與基礎設定

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-01 | 啟動遊戲後，F-01 DataManager 成功載入 `ReputationDeltaTable`，包含所有 9 個難度行（F, E, D, C, B, A, S, SS, SSS） | Debug UI 檢視 `ReputationDeltaTable.Count == 9` |
| AC-OR-02 | 啟動遊戲後，`SystemConstants` 含 `DEATH_RATE_ON_SUCCESS_MULTIPLIER`，數值為 `[0.0, 1.0]` 範圍內之 float | Debug UI 檢視該 key 存在且在範圍內；若缺失，Console 顯示 `LogError`，預設 `1.0` 仍可啟動 |
| AC-OR-03 | 若 `ReputationDeltaTable` 缺少某難度行，查詢該難度時回傳 `(successDelta=0, failDelta=0)` 並 `LogError` | 手動移除 CSV 某行 → 啟動後派遣該難度 → 結算時聲望不變動、Console 有 Error |

### 8.2 結算管線（Pipeline）

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-04 | FT-02 發布 `OnMissionCompleted(activeMissionID)` 後，FT-04 在同一幀內完成結算並發布 `OnMissionResolved(outcome)` | Unit test：假 FT-02 發事件 → 驗證 FT-04 於下一次 `Update()` 前發布事件 |
| AC-OR-05 | 結算管線嚴格按 § 3.2 順序執行（步驟 1-12）；若任一步驟失敗，步驟 8-12 不執行 | Unit test：步驟 2 插入 mock 讓 `GetAdventurer` 回傳 null → 驗證 `OnMissionResolved` 未發布、ActiveMission 未清理 |
| AC-OR-06 | 同一 `activeMissionID` 的 `OnMissionCompleted` 重複發布時，第二次呼叫安全退出（`LogError` 後無副作用） | Unit test：連續發布兩次 `OnMissionCompleted(id=42)` → 驗證 `OnMissionResolved` 只發一次、無重複狀態變更 |
| AC-OR-07 | 結算完成後，FT-02 的 ActiveMission 必被清除（`GetActiveMission(id)` 回傳 null） | Unit test：結算後呼叫 `FT02.GetActiveMission(id)` 驗證 null |

### 8.3 擲骰與機率判定

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-08 | `finalSuccessRate = 0.0` 的任務永遠失敗（1000 次擲骰 `isSuccess == false` 次數 `== 1000`） | Unit test：參數化 `finalSuccessRate=0.0`、跑 1000 次 |
| AC-OR-09 | `finalSuccessRate = 1.0` 的任務永遠成功（1000 次擲骰 `isSuccess == true` 次數 `== 1000`） | Unit test：參數化 `finalSuccessRate=1.0`、跑 1000 次 |
| AC-OR-10 | `finalSuccessRate = 0.5` 時，10000 次擲骰的 `isSuccess == true` 次數應落在 `[4800, 5200]` 區間（95% 信賴區間） | Unit test：設定固定 seed、統計分佈 |
| AC-OR-11 | 當 `isSuccess == true` 時，`outcome.adjustedDeathRate == finalDeathRate × DEATH_RATE_ON_SUCCESS_MULTIPLIER`；`isSuccess == false` 時，`outcome.adjustedDeathRate == finalDeathRate` | Unit test：參數化檢查兩路徑 |
| AC-OR-12 | `successRoll` 與 `deathRoll` 為獨立亂數（固定 seed 下兩者不相等的機率 `> 99.99%`） | Unit test：檢查 `successRoll != deathRoll` 在大量樣本中之比例 |

### 8.4 Condition 特質套用

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-13 | `on_death_survive` 特質在「成功 + 死亡」擲骰結果上觸發後，最終 `finalStatus == Wounded`、`isDead == false`、`isWounded == true` | Unit test：mock C-05 強制觸發 `on_death_survive`、擲出 `(isSuccess=T, isDead=T)` |
| AC-OR-14 | `on_death_survive` 在「失敗 + 死亡」擲骰結果上觸發後，最終 `finalStatus == Wounded` | Unit test：強制觸發、擲出 `(isSuccess=F, isDead=T)` |
| AC-OR-15 | 冒險者同時持有 `on_death_survive` 與 `on_fail_survive`，於「失敗+死亡」情境下依 `traitIDs` 順序檢查；首個通過機率判定並將 `isDead` 改為 `false` 後，後續同類 survive 特質因 `isDead == false` 自然跳過 | Unit test：traitIDs = `[A=on_death_survive (prob=1.0), B=on_fail_survive (prob=1.0)]` → 驗證 A 觸發後 `isDead == false`、B 因條件不成立跳過；`triggeredConditionTraits` 只含 A。反例：若 A 機率判定未通過（mock random），B 仍可觸發。|
| AC-OR-16 | 冒險者無任何 condition 特質時，`outcome.conditionGoldBonus == 0`、`outcome.triggeredConditionTraits.Length == 0`、`reputationDelta == baseDelta` | Unit test：mock 無特質冒險者 → 驗證三欄位 |
| AC-OR-17 | `on_success_gold_bonus` 觸發時，`outcome.conditionGoldBonus == baseReward × 0.5`（取整數） | Unit test：mock `baseReward=1000`、強制觸發 → 驗證 `conditionGoldBonus == 500` |

### 8.5 冒險者狀態更新

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-18 | 結算 `finalStatus == Idle` 時，冒險者 `status == Idle`、`currentMissionID == 0` | Unit test：驗證 C-02 兩欄位 |
| AC-OR-19 | 結算 `finalStatus == Wounded` 時，冒險者 `status == Wounded`、`currentMissionID == 0`、`woundedUntilTimestamp > currentUTC` | Unit test：驗證 C-02 三欄位 |
| AC-OR-20 | 結算 `finalStatus == Dead` 時，冒險者 `status == Dead`、`currentMissionID == 0`;仍保留於 roster（不自動除名） | Unit test：驗證 C-02 兩欄位 + roster 中仍可查到 |

### 8.6 聲望計算

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-21 | 成功 B 難度任務（無 condition）結算後，F-03 `GetReputation()` 相較結算前增加 `+4`（ReputationDeltaTable[B].successDelta） | Unit test：結算前後比對 |
| AC-OR-22 | 失敗 C 難度任務 + `on_fail_reputation(-3)` condition 觸發，F-03 聲望減少 `-4 + (-3) = -7` | Unit test：結算前後比對 |
| AC-OR-23 | 結算產生的 `reputationDelta` 極大值（例 `+200`），F-03 仍 clamp 至 `[-100, +100]` | Unit test：mock 聲望表某難度為 `+200` → 驗證 F-03 最終值不超過 100 |

### 8.7 事件發布

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-24 | `OnMissionResolved` 事件 payload 含 § 3.1 所有欄位且皆為正確型別（非 null、非 undefined） | Unit test：訂閱事件、斷言 payload 結構完整 |
| AC-OR-25 | `OnMissionResolved` 在 `RemoveActiveMission` 之前發布（訂閱者內仍可透過 `outcome.activeMissionID` 讀到原 ActiveMission） | Unit test：訂閱者於 callback 中呼叫 `FT02.GetActiveMission(outcome.activeMissionID)` 驗證非 null |
| AC-OR-26 | 訂閱者拋出例外時，同一 `OnMissionResolved` 的其他訂閱者仍能正常執行（依賴 EventBus 的隔離行為） | Unit test：註冊兩個訂閱者、第一個 throw → 驗證第二個被呼叫 |

### 8.8 離線結算

| ID | 驗收條件 | 驗證方式 |
|----|---------|---------|
| AC-OR-27 | 離線期間 3 個 ActiveMission 到期，重新開啟遊戲後 FT-04 依序完成 3 次結算，發布 3 次 `OnMissionResolved`，各自獨立擲骰 | Unit test：mock F-02 時間跳躍 → 驗證 3 次事件觸發、3 次 outcome 內容可能不同 |
| AC-OR-28 | 離線結算批次處理期間不阻塞主執行緒（3 筆任務結算於單幀內完成、無可感知延遲） | Profiler：量測單幀耗時 `< 16ms`（60fps 目標） |

### 8.9 範疇外（Game Jam 不驗收）

以下情境**不納入本輪驗收**，但列出供未來擴充參考：

- 結算過程中玩家強制關閉遊戲的崩潰復原（§ 5.7）
- 結算結果的歷史日誌持久化
- 結算動畫中途被打斷的 UX 行為
