# NPC Decision System 系統設計文件

_建立時間：2026-04-22_
_狀態：已設計_
_系統 ID：FT-03_

---

## 1. 概要（Overview）

FT-03 NPC Decision System 負責兩種 NPC 決策行為。

**推薦判斷（Recommendation Decision）**：當玩家推薦某任務給某冒險者時，FT-03 從 FT-02 取得該配對的 `finalSuccessRate` / `finalDeathRate`，套用冒險者 behavior 類特質的意願修正，加上隨機抖動（jitter）後與 `ACCEPTANCE_THRESHOLD` 比較，決定接受或拒絕。接受則通知 FT-02 執行派遣；拒絕則回報拒絕原因供 UI 顯示。

**閒置自主接單（Autonomous Quest Pickup）**：冒險者進入 Idle 狀態後，等待 `AUTO_PICKUP_IDLE_MINUTES` 分鐘，此後每隔 `AUTO_PICKUP_INTERVAL_MINUTES`（10 分鐘）掃描委託板上所有「已審核但尚未被推薦、且任務難度在公會當前等級上限以內」的可用任務，對每位符合條件的 Idle 冒險者進行意願計算，若觸發則由系統代為推薦並走完同一套決策流程。玩家無法中斷已觸發的自主接單；防止搶單的唯一手段是在計算視窗前主動推薦給目標冒險者。

**不負責**：
- 成功率 / 死亡率計算（FT-02）
- 結算骰與後果（FT-04）
- 金流（FT-05）
- condition 類特質套用（FT-04）

---

## 2. 玩家幻想（Player Fantasy）

你把那張 C 難度調查令推薦給艾克——你的法師，調查是他的拿手好戲。成功率 75%，你以為板上釘釘。

但艾克搖搖頭，拒絕了。他有「膽小」特質，面對超過門檻的死亡率時意願就是不夠。

你嘆口氣，換了盾衛試試——雖然成功率掉到 55%，但盾衛不在乎死，他接了。

第二天早上你打開遊戲，發現委託板上那張你想留給精英的 B 難度任務消失了。艾克自己去了——他昨晚閒了太久，偷偷去接了單。有時候，最難管的不是任務，而是那群有自己想法的人。

核心情感：**「你推薦，但他們決定」**——玩家感受到 NPC 的個性，而不是在操控棋子。自主接單強化「公會是活的」的感受，讓玩家離開一段時間後回來時有意外之喜（或驚嚇）。

---

## 3. 詳細規則（Detailed Rules）

### 3.1 推薦判斷流程（Recommendation Decision Flow）

1. 玩家觸發推薦（選定任務 + 冒險者）
2. FT-02 `CalculateRates(instanceID, missionID)` → `(finalSuccessRate, finalDeathRate)`
3. FT-03 計算 `effectiveScore`（見 Section 4）
4. 判斷：
   - `effectiveScore ≥ ACCEPTANCE_THRESHOLD` → **接受**，呼叫 FT-02 `Dispatch(instanceID, missionID)`
   - `effectiveScore < ACCEPTANCE_THRESHOLD` → **拒絕**，回傳 `DecisionResult`（含拒絕原因代碼）

**DecisionResult 資料結構**：

| 欄位 | 型別 | 說明 |
|------|------|------|
| `accepted` | `bool` | 是否接受 |
| `effectiveScore` | `float` | 最終意願分數（UI 可選顯示） |
| `rejectionReason` | `RejectionReason?` | 拒絕時的原因代碼；接受時為 `null` |

**RejectionReason enum**：

| 值 | 觸發條件 | UI 顯示意義 |
|----|---------|------------|
| `TooRisky` | `willingnessScore`（未加 jitter）< `ACCEPTANCE_THRESHOLD`，且 `finalDeathRate × DEATH_AVERSION > finalSuccessRate` | 死亡率過高，不願冒此險 |
| `NotInterested` | `willingnessScore` ≥ `ACCEPTANCE_THRESHOLD`，但加上 jitter 後低於門檻 | 今天就是不想接 |
| `NotWilling` | behavior 特質修正後低於門檻（特質將 willingness 壓低） | 個性使然，拒絕此類任務 |

> 判斷優先序與三階段中間值（對齊 §4.1 公式步驟）:
> - **Step 1 後** `willingnessScoreBase` = `finalSuccessRate − finalDeathRate × DEATH_AVERSION`
> - **Step 2 後** `willingnessScoreAfterTraits` = `willingnessScoreBase + Σ(behavior trait deltas)`
> - **Step 3 後** `willingnessScoreAfterStaff` = `willingnessScoreAfterTraits + FT08.GetStaffWillingnessBonus()`
> - **Step 4 後** `effectiveScore` = `willingnessScoreAfterStaff + jitter`(±WILLINGNESS_JITTER)
>
> `rejectionReason` 由「**哪一階段首次讓分數低於 `ACCEPTANCE_THRESHOLD`**」決定:
> - `willingnessScoreBase < threshold` 且死亡率主導(`finalDeathRate × DEATH_AVERSION > finalSuccessRate`)→ `TooRisky`
> - `willingnessScoreAfterTraits < threshold` 但 `willingnessScoreBase ≥ threshold` → `NotWilling`(behavior 特質壓低)
> - `effectiveScore < threshold` 但 `willingnessScoreAfterStaff ≥ threshold` → `NotInterested`(jitter 拉低)

---

### 3.2 委託官加成（Guild Staff Bonus）

FT-08 委託官職員的被動加成（推薦接受率 +5%）以 `staffWillingnessBonus` 形式加入意願計算。FT-03 透過 FT-08 查詢當前生效的加成值（未招募時為 `0`）。此加成在 behavior 特質修正後、jitter 前套用。

---

### 3.3 閒置自主接單流程（Autonomous Quest Pickup Flow）

**狀態追蹤（per 冒險者）**：

| 欄位 | 說明 |
|------|------|
| `idleSinceTimestamp` | 冒險者最近一次進入 `Idle` 的時間戳（Unix 秒）；派遣或死亡時清除 |
| `lastAutoPickupTimestamp` | 最近一次觸發自主接單計算的時間戳；初始為 `0` |

**觸發檢查**（FT-03 訂閱 F-02 `OnMinuteTick`，每分鐘執行一次）：

```
AutoPickupTick():
    now = F02.NowUTC
    foreach adventurer in C02.GetByStatus(Idle):
        idleMinutes = (now - adventurer.idleSinceTimestamp) / 60
        if idleMinutes < AUTO_PICKUP_IDLE_MINUTES: continue       // 冷卻中
        sinceLastPickup = (now - adventurer.lastAutoPickupTimestamp) / 60
        if sinceLastPickup < AUTO_PICKUP_INTERVAL_MINUTES: continue  // 間隔未到
        TryAutoPickup(adventurer, now)
```

**選任務邏輯**（TryAutoPickup）：

```
TryAutoPickup(adventurer, now):
    candidates = FT02.GetAvailableCommissions()
                     .Where(missionID => DIFFICULTY_INDEX(C01.GetTemplate(missionID).difficulty)
                                          <= DIFFICULTY_INDEX(FT06.GetMaxMissionDifficulty()))
    // 已審核、無人推薦進行中(由 FT-02 §3.9.4 派遣後自動從池移除)、難度 ≤ 公會可接最高任務難度
    bestMission = null
    bestScore   = -∞
    foreach mission in candidates:
        (s, d) = FT02.CalculateRates(adventurer.instanceID, mission.missionID)
        score  = CalcEffectiveScore(adventurer, mission, s, d)  // 含 behavior 特質 + staffBonus + jitter
        if score > bestScore:
            bestScore   = score
            bestMission = mission
    adventurer.lastAutoPickupTimestamp = now
    if bestMission != null and bestScore >= ACCEPTANCE_THRESHOLD:
        FT02.Dispatch(adventurer.instanceID, bestMission.missionID)
        EventBus.Publish(OnAutoPickup, adventurer.instanceID, bestMission.missionID)
    // bestScore < ACCEPTANCE_THRESHOLD：本輪無任務被接受，僅更新 timestamp
```

> 玩家無法中斷已觸發的自主接單。防止任務被搶走的唯一手段是在計算視窗前主動推薦。
> `OnAutoPickup` 事件供 P-03 Notification System 顯示通知。

---

### 3.4 查詢與操作 API

| API | 簽名 | 說明 |
|-----|------|------|
| 推薦判斷 | `MakeDecision(int instanceID, int missionID) : DecisionResult` | 計算 effectiveScore,回傳接受/拒絕(含 RejectionReason)；冒險者非 Idle 回傳 `accepted=false, rejectionReason=null`(§5.1) |
| 預覽分數 | `PreviewEffectiveScore(int instanceID, int missionID) : float` | 計算當下 effectiveScore（不含 jitter）供 P-02 顯示;可選 API,主流程使用 `MakeDecision` |
| 取得 Idle since | `GetIdleSinceTimestamp(int instanceID) : long` | 透過 C-02 AdventurerInstance 查詢(C-02 §3.1 補欄位後) |

### 3.5 事件契約

| 事件 | 觸發時機 | Payload | 訂閱者 |
|------|---------|--------|-------|
| `OnAutoPickup` | §3.3 自主接單成功派遣後 | `(int adventurerInstanceID, int missionID)` | P-03 Notification System(顯示「冒險者自行接取委託」) |

> FT-03 不發布其他事件;`OnCommissionAccepted` 由 FT-02 §3.5 step 3 發布(單一發布點原則,見 §6.3)。

---

## 4. 公式（Formulas）

### 4.1 effectiveScore 計算

```
CalcEffectiveScore(adventurer, mission, finalSuccessRate, finalDeathRate):

    // Step 1: 基礎意願分
    willingnessScore = finalSuccessRate - finalDeathRate × DEATH_AVERSION

    // Step 2: behavior 特質修正（加法疊加）
    foreach traitID in adventurer.traitIDs:
        trait = C05.GetTrait(traitID)
        if trait.effectType != "behavior": continue
        if MatchesBehaviorTarget(trait.effectTarget, mission): willingnessScore += trait.effectValue

    // Step 3: 委託官加成
    willingnessScore += FT08.GetStaffWillingnessBonus()

    // Step 4: jitter
    effectiveScore = willingnessScore + Random.Range(-WILLINGNESS_JITTER, +WILLINGNESS_JITTER)

    return effectiveScore
```

> jitter 在推薦判斷與自主接單中使用相同邏輯，製造「NPC 有時做出次優決策」的擬真感。

---

### 4.2 MatchesBehaviorTarget 規則

| effectTarget | 匹配條件 |
|-------------|---------|
| `willingness_all` | 永遠匹配 |
| `willingness_type_1` | `mission.typeID == 1`（討伐） |
| `willingness_type_2` | `mission.typeID == 2`（護送） |
| `willingness_type_3` | `mission.typeID == 3`（採集） |
| `willingness_type_4` | `mission.typeID == 4`（調查） |
| `willingness_diff_S` | `DIFFICULTY_INDEX(mission.difficulty) >= 6`（S/SS/SSS） |
| `willingness_diff_A` | `DIFFICULTY_INDEX(mission.difficulty) >= 5`（A/S/SS/SSS） |
| `willingness_diff_low` | `DIFFICULTY_INDEX(mission.difficulty) <= 1`（F/E） |

---

### 4.3 計算範例

**範例：C 階法師（「膽小」特質：`willingness_diff_A -0.30`）推薦 A 難度調查任務**

| 步驟 | willingnessScore | 說明 |
|------|-----------------|------|
| 基礎值 | `0.70 - 0.40 × 0.5 = 0.50` | finalSuccess=0.70, finalDeath=0.40 |
| 特質修正 | `0.50 - 0.30 = 0.20` | 膽小，A 難度意願 -0.30 |
| 委託官加成 | `0.20 + 0.05 = 0.25` | 委託官在職 |
| jitter | `0.25 + (-0.02) = 0.23` | 隨機抖動 |
| 判斷 | **拒絕**（0.23 < 0.25） | RejectionReason: `NotWilling` |

**範例：同一法師推薦 C 難度調查任務（不觸發膽小）**

| 步驟 | willingnessScore | 說明 |
|------|-----------------|------|
| 基礎值 | `0.75 - 0.13 × 0.5 = 0.685` | finalSuccess=0.75（法師擅長調查+0.20後）, finalDeath=0.13 |
| 特質修正 | `0.685`（不變） | C 難度不觸發 `willingness_diff_A` |
| 委託官加成 | `0.685 + 0.05 = 0.735` | |
| jitter | `0.735 + (+0.03) = 0.765` | |
| 判斷 | **接受**（0.765 ≥ 0.25） | |

---

## 5. 邊緣案例（Edge Cases）

### 5.1 推薦判斷

| 情況 | 處理方式 |
|------|---------|
| 冒險者狀態非 `Idle`（Dispatched / Wounded / Dead） | FT-03 回傳 `DecisionResult { accepted=false, rejectionReason=null }`，並 `Debug.LogWarning`；UI 層應在允許推薦前先檢查狀態 |
| behavior 特質 `effectValue` 極端負值（如 -0.9），使 willingnessScore 成大負值 | 不 clamp，讓分數自然低於門檻，結果為必拒絕；RejectionReason = `NotWilling` |
| 委託官未招募 | `FT08.GetStaffWillingnessBonus()` 回傳 `0`，公式照常運作，無需特殊處理 |
| 未知的 `effectTarget`（CSV 資料錯誤） | `MatchesBehaviorTarget` 回傳 `false`，跳過該特質，`Debug.LogWarning`；不影響其餘計算 |

### 5.2 自主接單

| 情況 | 處理方式 |
|------|---------|
| 委託板上無可用任務（`candidates` 為空） | 更新 `lastAutoPickupTimestamp`，靜默跳過，等下一個 interval |
| 所有可用任務的 `bestScore` 均 < `ACCEPTANCE_THRESHOLD` | 更新 `lastAutoPickupTimestamp`，本輪無接單，靜默跳過 |
| `FT02.Dispatch()` 回傳 `false`（任務在計算瞬間被搶走，或進行中任務數達上限） | 自主接單視為失敗，不重試，等下一個 interval |
| 離線期間累積多個 `AUTO_PICKUP_INTERVAL` 未觸發 | **離線期間 NPC 自主接單暫停**（對齊 F-02 §3.2 rule 6：離線不補發 `OnMinuteTick`）；重啟後依正常節奏接收 `OnMinuteTick`，不主動補算離線期間錯過的 interval。Jam 階段 FT-11 Offline Resolver 未實作，離線自主接單能力延後至 Post-Jam |
| 存檔讀取後 `lastAutoPickupTimestamp = 0`（新遊戲或首次載入） | 冒險者進入 `Idle` 後正常等待 `AUTO_PICKUP_IDLE_MINUTES` 再觸發，視同正常流程 |
| 冒險者在 `TryAutoPickup` 執行中途死亡或被玩家推薦（理論上不可能，因為是同一幀邏輯） | 防禦性：`FT02.Dispatch()` 內部檢查狀態，回傳 `false` 時自主接單靜默失敗 |

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-03 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `SystemConstants`（DEATH_AVERSION、ACCEPTANCE_THRESHOLD、WILLINGNESS_JITTER、AUTO_PICKUP_IDLE_MINUTES、AUTO_PICKUP_INTERVAL_MINUTES） | `DataManager.GetConstant<float>(key)` |
| F-02 Time System | 取得當前 timestamp；訂閱 `OnMinuteTick` 驅動自主接單檢查 | `NowUTC`、Tick 回呼 |
| C-02 Adventurer Management | 查詢 Idle 冒險者列表；讀取 `idleSinceTimestamp`、`lastAutoPickupTimestamp`；更新這兩個欄位 | `GetByStatus(Idle)`、`GetAdventurer(instanceID)` |
| C-05 Trait System | 查詢 behavior 類特質 effectTarget / effectValue | `GetTrait(traitID)` |
| FT-02 Mission Dispatch | 計算成功率/死亡率（推薦判斷與自主接單均使用）；推薦接受後執行派遣（自主接單傳 `source = NpcAutoPick`，FT-02 內部發布 `OnCommissionAccepted` 驅動 FT-05 預收，FT-03 本身**不**直接發布該事件）；自主接單時讀取委託板可用任務（依 FT-02 委託板池服務） | `CalculateRates(instanceID, missionID)`、`Dispatch(instanceID, missionID, source)`、`GetAvailableCommissions() : IReadOnlyList<int>`（FT-02 §3.9.3） |
| FT-06 Guild Core | 自主接單過濾「難度 ≤ 公會可接最高任務難度」（§3.3 TryAutoPickup 候選任務過濾） | `GetMaxMissionDifficulty() : string`（FT-06 §3.6） |
| FT-08 Guild Staff System | 查詢委託官被動加成值 | `GetStaffWillingnessBonus() : float` |

### 6.2 下游依賴（依賴 FT-03 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| P-02 Main UI | 顯示接受/拒絕結果與原因；預覽 effectiveScore（可選） | `MakeDecision(instanceID, missionID) : DecisionResult` |
| P-03 Notification System | 訂閱 `OnAutoPickup` 事件，推播「冒險者自行接取委託」通知 | `EventBus.Subscribe(OnAutoPickup)` |
| FT-10 Save/Load | 序列化每位冒險者的 `idleSinceTimestamp`、`lastAutoPickupTimestamp` | 透過 C-02 AdventurerInstance 一併序列化 |

### 6.3 循環依賴注意事項

- FT-03 依賴 FT-02 執行 `CalculateRates` 與 `Dispatch`，FT-02 不依賴 FT-03——**無循環依賴**
- FT-03 依賴 FT-08 查詢加成，FT-08 不依賴 FT-03——**無循環依賴**
- FT-03 與 FT-05 Guild Gold Flow 為**間接關係**：FT-03 自主接單 → `FT-02.Dispatch(..., NpcAutoPick)` → FT-02 發布 `OnCommissionAccepted` → FT-05 執行預收。FT-03 不直接呼叫 FT-05 API、不訂閱 FT-05 事件，故 FT-05 不列入 FT-03 § 6.2 下游（單一發布點原則，見 FT-02 § 3.5）。

---

### 6.4 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft03Decision"` |
| `IsCritical` | `false`（Degradable；FT-03 本身無實例級持久狀態） |

**持久化責任說明**：

FT-03 不持有任何實例級狀態。`idleSinceTimestamp` 與 `lastAutoPickupTimestamp` 屬於 `AdventurerInstance`，由 C-02 於 row 3 一併序列化與還原（FT-10 §3.3.3 row 9 / §6.1 #8 既定）。

**`Serialize()` 回傳值**：`"{}"` — 空物件，無業務欄位。

**`RestoreFromSave(string ownerJson)` 行為**：不還原任何欄位；負責重新訂閱 `F-02.OnMinuteTick`（或 `OnSecondTick`）以驅動 `AutoPickupTick`，確保自主接單計時在 Bootstrap 後恢復。

**`InitializeAsNewGame()` 行為**：無任何狀態需重置，僅確認事件訂閱已在 `OnEnable` 中建立（通常由 MonoBehaviour 生命週期處理，此方法為 no-op）。

對應 FT-10 §3.3.3 拓撲順序 row 9、§3.3.4 Degradable 分類、§6.1 #8（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 SystemConstants.csv（FT-03 讀取的常數）

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `DEATH_AVERSION` | `0.5` | `0.3 ~ 0.8` | 越高 NPC 越怕死，高死亡率任務越難找到人接；過高會讓 S+ 任務幾乎無人願意接 |
| `ACCEPTANCE_THRESHOLD` | `0.25` | `0.15 ~ 0.40` | 門檻越高拒絕頻率越高；過低讓 NPC 幾乎來者不拒，失去個性感 |
| `WILLINGNESS_JITTER` | `0.10` | `0.05 ~ 0.20` | 越大決策越隨機；過大讓邊緣案例（willingnessScore ≈ ACCEPTANCE_THRESHOLD）結果完全無法預測 |
| `AUTO_PICKUP_IDLE_MINUTES` | `10` | `5 ~ 30` | 冒險者空閒多久後才開始考慮自主接單；越短「搶單」越頻繁，玩家反應時間越少 |
| `AUTO_PICKUP_INTERVAL_MINUTES` | `10` | `5 ~ 30` | 每次自主接單計算的間隔；與 `AUTO_PICKUP_IDLE_MINUTES` 搭配決定搶單節奏 |

### 7.2 behavior 特質數值（TraitTable.csv）

| 參數 | 建議範圍 | 影響 |
|------|---------|------|
| `willingness_all` delta | `-0.20 ~ +0.20` | 全面影響意願；超過 ±0.25 會讓特質壓倒基礎分，失去成功率/死亡率的核心作用 |
| `willingness_diff_S` / `willingness_diff_A` delta | `-0.40 ~ -0.15` | 主要用於「膽小」類特質；-0.30 左右能有效阻止低階 NPC 自願接高難度任務 |
| `willingness_diff_low` delta | `+0.10 ~ +0.30` | 主要用於「謹慎」類特質；讓 NPC 偏好低風險任務 |
| `willingness_type_*` delta | `-0.20 ~ +0.20` | 類型偏好；搭配職業擅長/弱點使用時，同向疊加需注意不要讓某類任務完全無人接 |

### 7.3 調整原則

- 調整 `DEATH_AVERSION` 或 `ACCEPTANCE_THRESHOLD` 時，需重新驗算範例（4.3 節）確認典型配對的接受率符合預期
- `AUTO_PICKUP_IDLE_MINUTES` 與 `AUTO_PICKUP_INTERVAL_MINUTES` 建議成對調整，兩者相等時（均為 10 分鐘）代表冒險者空閒 10 分鐘後立即開始、之後每 10 分鐘一次

---

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-ND3-01 | `MakeDecision` 對 willingnessScore = 0.50（finalSuccess=0.70, finalDeath=0.40, DEATH_AVERSION=0.5）、無 behavior 特質、無委託官加成時，effectiveScore 落在 `0.40 ~ 0.60`（±WILLINGNESS_JITTER 範圍內），且回傳 `accepted=true` |
| AC-ND3-02 | `MakeDecision` 對持有 `willingness_diff_A -0.30` 特質的冒險者推薦 A 難度任務時，willingnessScore 減少 `0.30`；推薦 C 難度任務時 willingnessScore 不受此特質影響 |
| AC-ND3-03 | `MakeDecision` 對 willingnessScore = 0.20（低於 ACCEPTANCE_THRESHOLD=0.25）的情況，jitter 最大正值（+0.10）後 effectiveScore = 0.30，回傳 `accepted=true`；jitter 任意負值後 effectiveScore < 0.25，回傳 `accepted=false` |
| AC-ND3-04 | 委託官在職時，`GetStaffWillingnessBonus()` 回傳 `0.05`；未招募時回傳 `0.0`；兩種情況下公式其餘部分結果一致 |
| AC-ND3-05 | `RejectionReason` 判斷：基礎 willingnessScore（未加 jitter）≥ ACCEPTANCE_THRESHOLD 但加 jitter 後低於門檻 → `NotInterested`；behavior 特質將 willingnessScore 壓到門檻以下 → `NotWilling`；死亡率主導導致 willingnessScore 本身低於門檻 → `TooRisky` |
| AC-ND3-06 | 對非 `Idle` 冒險者（Dispatched / Wounded / Dead）呼叫 `MakeDecision` 回傳 `accepted=false`，不觸發任何派遣，不修改冒險者狀態 |
| AC-ND3-07 | 冒險者進入 `Idle` 後，`AUTO_PICKUP_IDLE_MINUTES`（10 分鐘）內 `AutoPickupTick` 不對其觸發 `TryAutoPickup` |
| AC-ND3-08 | 冒險者進入 `Idle` 滿 10 分鐘後，`AutoPickupTick` 觸發 `TryAutoPickup`；若委託板有 effectiveScore ≥ ACCEPTANCE_THRESHOLD 的任務，冒險者自動接取 effectiveScore 最高的那一個 |
| AC-ND3-09 | 自主接單觸發後，`lastAutoPickupTimestamp` 更新為當前時間；下一次 `TryAutoPickup` 至少在 `AUTO_PICKUP_INTERVAL_MINUTES`（10 分鐘）後才會再次執行 |
| AC-ND3-10 | 委託板上所有任務的 effectiveScore 均 < ACCEPTANCE_THRESHOLD 時，`TryAutoPickup` 不執行任何派遣，但仍更新 `lastAutoPickupTimestamp` |
| AC-ND3-11 | 自主接單成功後，發布 `OnAutoPickup` 事件，攜帶正確的 `adventurerInstanceID` 與 `missionID` |
| AC-ND3-12 | `FT02.Dispatch()` 於自主接單時回傳 `false`（任務被搶），`TryAutoPickup` 靜默失敗，不重試，不拋出例外 |
| AC-ND3-13 | behavior 特質 effectTarget 為未知值時，`MatchesBehaviorTarget` 回傳 `false`，跳過該特質並輸出 `Debug.LogWarning`，不影響其餘計算 |
