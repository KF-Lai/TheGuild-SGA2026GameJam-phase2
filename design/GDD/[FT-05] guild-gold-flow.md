# Guild Gold Flow 系統設計文件

_建立時間：2026-04-22_
_狀態：設計完成（Section 1-8 全部完成，待 /design-review）_
_系統 ID：FT-05_
_原名：Commission Flow（2026-04-22 重新命名以反映擴張後的「公會全金流」範疇）_

---

## 設計決策備忘（Design Decisions Memo）

> 本節記錄骨架建立前已與設計者對齊的關鍵決策，供後續逐節撰寫時參照。撰寫完成後可移除。

1. **預收時機**（Q1）：FT-05 訂閱統一的 `OnCommissionAccepted(missionID, baseReward, source)` 事件執行 `AddGold(+baseReward)` 預收。事件 source 可為 `PlayerManual`（玩家手動審核通過）或 `OfflineAutoPick`（離線自主接單追認）；FT-05 不區分 source。FT-05 **不**負責上限規則、離線追認時序（屬 FT-03 / 未來 FT-11 職責）
2. **成功結算金流**（Q2）：**一次淨結算**——`AddGold(+baseReward × effectiveCommissionRate + conditionGoldBonus)`；不做「預收+退冒險者分成」的兩次金流
3. **失敗結算金流**（Q3）：**一次淨結算**——`AddGold(-baseReward × effectivePenaltyRate + conditionGoldBonus)`；不做「全額退客戶+賠償」的兩次金流
4. **破產門檻突破**（Q4）：FT-05 使用 F-03 新 API `AddGoldAllowBankruptcy(int)` 執行結算扣款，允許突破破產門檻觸發 `Warning` / `Bankrupt`。F-03 GDD 需同步新增此 API
5. **加成查詢策略**（Q5）：**即時查詢**——不訂閱 FT-06 / FT-07 / FT-08 / C-05 的變動事件維護快取，而是每次結算時即時呼叫 `FT08.GetAccountantBonus()` / `FT07.IsAccountantSlotActive()` 等查詢 API。介面保留但 Game Jam 階段可以 null-check（系統未實作時回傳無加成）
6. **事件發布**（Q6）：FT-05 發布聚合事件 `OnCommissionSettled(activeMissionID, netDelta, CommissionBreakdown)` 與 `OnCommissionPrepaid(missionID, prepaidAmount)`，供 P-02 / P-03 / FT-10 消費
7. **conditionGoldBonus 通用加法**（Q7）：成功與失敗路徑**皆加**`outcome.conditionGoldBonus` 至淨額。現階段 C-05 僅 `on_success_gold_bonus`（失敗時永為 0），但未來擴充 `on_fail_gold_bonus` 時 FT-05 程式無需改動
8. **加成的有效率計算**：
   - `effectiveCommissionRate = COMMISSION_RATE + accountantCommissionBonus`（職員提供 +2%）
   - `effectivePenaltyRate = max(0, PENALTY_RATE + accountantPenaltyBonus + buildingPenaltyBonus)`（會計職員提供 -2%、預備金保險櫃提供額外調整；加成可疊加，但 penaltyRate 下限為 0）
   - Trait 類金流加成（若未來擴充）走 `conditionGoldBonus` 通道，不改變 rate

---

## 1. 概要（Overview）

FT-05 Guild Gold Flow 是公會所有金流操作的統一執行者，涵蓋兩大類別：**委託金流**（預收、結算）與**固定支出**（設施維護費、職員薪水）。

**委託金流**：訂閱 `OnCommissionAccepted` 事件執行預收（`AddGold(+baseReward)`），訂閱 FT-04 `OnMissionResolved` 事件執行結算——成功淨得 `baseReward × effectiveCommissionRate + conditionGoldBonus`，失敗淨損 `baseReward × effectivePenaltyRate` 再加 `conditionGoldBonus`。**固定支出**：訂閱 FT-07 `OnGuildMaintenanceDue` 執行設施維護費扣款，訂閱 FT-08 `OnStaffSalaryDue` 執行職員薪水扣款。

所有外流金流（結算賠償、維護費、薪水）透過 F-03 新 API `AddGoldAllowBankruptcy` 執行，允許突破破產門檻觸發 `Warning` / `Bankrupt`，符合「有重量的決策」設計支柱。所有金流事件 payload 皆含 `bankruptcyStateBefore` / `bankruptcyStateAfter` 狀態快照，供 P-03 Notification 辨識「破產警告開始」、「破產觸發」、「負債已結清」三類狀態轉移並選擇對應通知模板。職員加成（FT-08 會計 `+2% 傭金率`、`-2% 賠償率`）與建築加成（FT-07 預備金保險櫃預留擴充）於結算時即時查詢；Game Jam 階段若下游系統尚未實作，FT-05 以預設值降級運作。

**核心職責**：

1. 訂閱 `OnCommissionAccepted`，執行委託預收並發布 `OnCommissionPrepaid`
2. 訂閱 FT-04 `OnMissionResolved`，執行委託結算並發布 `OnCommissionSettled`
3. 訂閱 FT-07 `OnGuildMaintenanceDue`，執行設施維護費扣款並發布 `OnMaintenanceCharged`
4. 訂閱 FT-08 `OnStaffSalaryDue`，執行職員薪水扣款並發布 `OnSalaryCharged`
5. 即時查詢 FT-08 / FT-07 的職員加成、建築加成，計算 `effectiveCommissionRate` / `effectivePenaltyRate`
6. 消費 `Outcome.conditionGoldBonus` 特質獎勵（通用加法，不區分成敗）
7. 所有金流事件 payload 快照 `bankruptcyStateBefore` / `bankruptcyStateAfter` 供 P-03 渲染狀態轉移通知

**不負責**：

- 結算擲骰、condition 套用（FT-04）
- 上限規則、離線自主接單時序（FT-03 / 未來 FT-11）
- 聲望變動（FT-04 直接呼叫 F-03 `AddReputation`）
- 金幣上下限、破產警告狀態機內部邏輯（F-03 自行管理；FT-05 僅讀取狀態）
- 維護費 / 薪水**金額計算**與**觸發時機**（FT-07 / FT-08 各自決定，透過 `Due` 事件傳入金額與時間戳）
- 破產觸發後的後果處理（FT-06 Guild Core 依 F-03 `OnBankruptcyWarningStateChanged` 處理）
- `Outcome.conditionGoldBonus` 的產生（C-05 透過 FT-04 的 `ApplyConditionTraits`）

**資料表**：`SystemConstants`（`COMMISSION_RATE = 0.20`、`PENALTY_RATE = 0.10`）

---

## 2. 玩家幻想（Player Fantasy）

你按下「接受這張委託」的那一刻，公會帳戶就跳了一筆正數——B 難度任務 600g 入帳。那一瞬間你確實感覺到「有錢了」。但你心裡清楚，那不是獎賞，是押金。客戶把整筆委託金先放在你這裡，是因為信任公會會把任務交給對的人，而你之後還得連本帶利地交代清楚。帳戶餘額跳動的那個動畫，每次看都還是有一點愉悅——但那種愉悅本身就是設計好的一場騙局。

接下來的幾十分鐘，那筆錢就靜靜躺在你的金幣欄位裡，看得到、摸得到、但你不敢花。你會習慣性點開公會總覽，確認那 600g 還在——然後又想起派出去的是那位 C 階戰士，rankDiff 是 -1，成功率 63%。那筆預收不屬於你，它屬於還沒發生的結果。真正讓你坐立難安的，不是冒險者會不會回來，是那 600g 究竟最後會淨得 120g（+20% 傭金）還是淨賠 60g（-10% 賠償）——一單成功和一單失敗之間，是 180g 的差距。那差距會慢慢把你推到「寧可不接這單」的邊緣，或是相反，把你推到「我一定要接這單」的那一頭。

結算的那一瞬間，才是這個系統真正存在的理由。成功——`AddGold(+120g)`，帳戶淨增，聲望同步升起來；失敗——`AddGold(-60g)`，帳戶淨扣，有時候那一扣直接穿過零線、穿過你原本的負債，連帶觸發「破產倒數開始」的紅字跳出來。你眼睜睜看著那個數字從「還有 40g」變成「-20g，倒數 48 小時」。這不是遊戲在懲罰你，是你當初接那單的時候，帳就已經這樣算好了——系統只是忠實執行。而在極少數情況下，會冒出一行你沒預期的字：「冒險者特質觸發：額外金錢獎勵 +300g。」那不是常態，那是這個冷冰冰的金流系統偶爾對你釋出的善意。

**核心情感**：**金幣不會騙人**。FT-04 給你的是敘事的重量（誰活、誰死），FT-05 給你的是**帳本的重量**。預收那一跳、等待期間那個不敢花的餘額、結算那一刻的淨額——這三拍節奏把「接一單」從一個點的決定，拉長成一段有呼吸的過程。你會慢慢學會，金幣上的那個數字永遠分成兩半：一半是你的，一半是別人暫存在這裡的。而區分這兩半的本領，就是這個遊戲想教你的東西。

---

## 3. 詳細規則（Detailed Rules）

### 3.1 事件 Payload 資料結構

FT-05 發布三類金流事件，各自攜帶獨立的 Breakdown 結構。皆為 runtime 物件，不對應 CSV；訂閱者消費後可自行 copy，FT-05 不維護歷史。

#### 3.1.1 CommissionBreakdown（委託結算明細）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `activeMissionID` | `int` | 對應 FT-02 ActiveMission；結算時 FT-04 已呼叫 RemoveActiveMission，此欄位僅供追溯 |
| `missionID` | `int` | FK → C-01 MissionTemplate |
| `adventurerInstanceID` | `int` | FK → C-02 AdventurerInstance |
| `missionDifficulty` | `string` | 快照自 Outcome |
| `isSuccess` | `bool` | 快照自 Outcome |
| `baseReward` | `int` | 快照自 Outcome（原始 baseReward，與預收實際入帳金額無關） |
| `effectiveCommissionRate` | `float` | 本次結算實際採用的傭金率（含職員加成） |
| `effectivePenaltyRate` | `float` | 本次結算實際採用的賠償率（含職員加成、下限 0） |
| `accountantCommissionBonus` | `float` | 會計職員傭金加成（Jam 預設 `+0.02`；未招募 `0`） |
| `accountantPenaltyBonus` | `float` | 會計職員賠償加成（Jam 預設 `-0.02`；未指派 `0`） |
| `buildingPenaltyBonus` | `float` | 建築層額外賠償加成（Jam 預設 `0`，預留擴充） |
| `commissionGoldAmount` | `int` | 成功時撥付傭金金額（`baseReward × effectiveCommissionRate`，取整）；失敗時 `0` |
| `penaltyGoldAmount` | `int` | 失敗時賠償金額（**正整數**，`baseReward × effectivePenaltyRate`，取整）；成功時 `0` |
| `conditionGoldBonus` | `int` | 快照自 `Outcome.conditionGoldBonus`；成敗皆加（通用加法） |
| `netDelta` | `int` | 實際傳入 `F03.AddGoldAllowBankruptcy` 的淨額 |
| `goldBefore` | `int` | 結算前金幣快照（`F03.GetGold()`） |
| `goldAfter` | `int` | 結算後金幣快照（`F03.GetGold()`） |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 結算前 F-03 破產警告狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 結算後 F-03 破產警告狀態（與 before 比較即得轉移類型） |
| `settleTimestamp` | `long` | 結算當下的 UTC Unix seconds（供 FT-10 存檔、P-02 時序、debug log） |

#### 3.1.2 MaintenanceBreakdown（設施維護費明細）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `items` | `Dictionary<int, int>` | 各設施扣款細項：`buildingID → cost`（由 FT-07 傳入，FT-05 做 defensive copy 轉存） |
| `totalAmount` | `int` | 合計扣款（正整數，即 `items.Values.Sum()`） |
| `netDelta` | `int` | 實際傳入 `F03.AddGoldAllowBankruptcy` 的淨額（= `-totalAmount`） |
| `goldBefore` | `int` | 扣款前金幣快照 |
| `goldAfter` | `int` | 扣款後金幣快照 |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 扣款前 F-03 狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 扣款後 F-03 狀態 |
| `chargeTimestamp` | `long` | UTC Unix seconds（由 FT-07 `Due` 事件傳入，支援離線追認） |

#### 3.1.3 SalaryBreakdown（職員薪水明細）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `items` | `Dictionary<int, int>` | 各職員薪水細項：`staffID → salary`（由 FT-08 傳入） |
| `totalAmount` | `int` | 合計扣款 |
| `netDelta` | `int` | = `-totalAmount` |
| `goldBefore` | `int` | 扣款前金幣快照 |
| `goldAfter` | `int` | 扣款後金幣快照 |
| `bankruptcyStateBefore` | `BankruptcyWarningState` | 扣款前狀態 |
| `bankruptcyStateAfter` | `BankruptcyWarningState` | 扣款後狀態 |
| `chargeTimestamp` | `long` | UTC Unix seconds（由 FT-08 `Due` 事件傳入） |

---

### 3.2 預收管線（Prepayment Pipeline）

FT-05 在 `OnEnable` 訂閱 `OnCommissionAccepted(int missionID, int baseReward, CommissionSource source)`。

```
OnCommissionAccepted(missionID, baseReward, source):
    1. if baseReward <= 0 → Debug.LogError, return                  // 防禦性
    2. prevGold       = F03.GetGold()
    3. F03.AddGold(+baseReward)                                      // 預收為正值，不觸發破產；可能因 GOLD_MAX clamp 而實際入帳較少
    4. prepaidAmount  = F03.GetGold() - prevGold                     // 實際入帳（clamp 後的 delta）
    5. EventBus.Publish(OnCommissionPrepaid, new { missionID, prepaidAmount, source })
```

**關鍵原則**：

- 預收使用 F-03 標準 `AddGold`（不突破下限），因預收為正值 **不會觸發破產**
- FT-05 **不判斷** `source`（PlayerManual / OfflineAutoPick 皆一視同仁）
- **不做重複檢查**（信任上游 FT-02 / FT-03 / 未來 FT-11 的契約）；若同 `missionID` 重複發布，第二次會再次預收，屬上游 bug

---

### 3.3 委託結算管線（Commission Settlement Pipeline）

FT-05 在 `OnEnable` 訂閱 FT-04 `OnMissionResolved(Outcome outcome)`。

```
OnMissionResolved(outcome):
    1. breakdown = BuildCommissionBreakdown(outcome)
    2. QueryStaffBonuses(breakdown)                                  // 見 3.4
    3. breakdown.effectiveCommissionRate = COMMISSION_RATE + breakdown.accountantCommissionBonus
    4. breakdown.effectivePenaltyRate    = max(0, PENALTY_RATE
                                                 + breakdown.accountantPenaltyBonus
                                                 + breakdown.buildingPenaltyBonus)
    5. if outcome.isSuccess:
         ComputeSuccessNet(outcome, breakdown)                       // 見 3.5
       else:
         ComputeFailureNet(outcome, breakdown)                       // 見 3.5
    6. ExecuteGoldFlow(breakdown)                                    // 見 3.8（共用：金幣與狀態快照 + AddGoldAllowBankruptcy）
    7. breakdown.settleTimestamp = F02.NowUTC
    8. EventBus.Publish(OnCommissionSettled, breakdown)
```

**順序不可調換**：

- 步驟 2-4（加成查詢 + 有效率計算）必須在步驟 5（淨額計算）之前
- 步驟 6 `ExecuteGoldFlow` 內部負責金幣與破產狀態的 before/after 快照（見 3.8），為所有扣款管線共用機制

**不 try-catch**：下游 F-03 / 訂閱者拋例外時 FT-05 不吞錯；屬嚴重錯誤，需 root cause 修復。

---

### 3.4 加成查詢與有效率計算

FT-05 在每次結算時**即時查詢**（不維護快取、不訂閱變動事件）：

```
QueryStaffBonuses(breakdown):
    // 會計職員傭金加成（被動，招募即生效）
    breakdown.accountantCommissionBonus = (FT08 == null) ? 0f
                                          : FT08.GetAccountantCommissionBonus()

    // 會計職員賠償加成（需指派至「預備金保險櫃」slot）
    // FT-08 內部判斷：會計已招募 AND 已指派 AND 預備金保險櫃已解鎖
    breakdown.accountantPenaltyBonus    = (FT08 == null) ? 0f
                                          : FT08.GetAccountantPenaltyBonus()

    // 建築層賠償加成（Jam 未使用，預留未來建築直接提供賠償加成的擴充）
    breakdown.buildingPenaltyBonus      = (FT07 == null) ? 0f
                                          : FT07.GetBuildingPenaltyBonus()
```

**降級策略**（Jam 開發序依賴未完成時）：

- FT-08 尚未實作 → 全部回傳 `0`，`effectiveCommissionRate = 0.20`、`effectivePenaltyRate = 0.10`，基本金流仍可運作
- FT-07 尚未實作 → `buildingPenaltyBonus = 0`，不影響正常結算

**有效率範圍**（Jam 階段）：

- `effectiveCommissionRate ∈ [0.20, 0.22]`（最高 +2%）
- `effectivePenaltyRate ∈ [0.06, 0.10]`（下限來自 `max(0, ...)`；未來加成疊加時允許至 `0`，代表極端配置下無賠償）

---

### 3.5 成功 / 失敗淨額計算

```
ComputeSuccessNet(outcome, breakdown):
    breakdown.commissionGoldAmount = Mathf.RoundToInt(
                                        outcome.baseReward * breakdown.effectiveCommissionRate)
    breakdown.penaltyGoldAmount    = 0
    breakdown.netDelta             = breakdown.commissionGoldAmount + outcome.conditionGoldBonus

ComputeFailureNet(outcome, breakdown):
    breakdown.commissionGoldAmount = 0
    breakdown.penaltyGoldAmount    = Mathf.RoundToInt(
                                        outcome.baseReward * breakdown.effectivePenaltyRate)
    breakdown.netDelta             = -breakdown.penaltyGoldAmount + outcome.conditionGoldBonus
```

**取整策略**：`Mathf.RoundToInt`（銀行家捨入，`.5` 四捨至偶數，避免累積偏差）。

**成功範例**：

| 情境 | baseReward | rate | conditionBonus | commission | netDelta |
|------|-----------|------|----------------|-----------|----------|
| B 難度無會計 | 600 | 0.20 | 0 | 120 | **+120** |
| B 難度有會計 | 600 | 0.22 | 0 | 132 | **+132** |
| S 難度有會計 + `on_success_gold_bonus` | 1200 | 0.22 | 600 | 264 | **+864** |

**失敗範例**：

| 情境 | baseReward | rate | conditionBonus | penalty | netDelta |
|------|-----------|------|----------------|---------|----------|
| B 難度無加成 | 600 | 0.10 | 0 | 60 | **-60** |
| B 難度會計+slot | 600 | 0.06 | 0 | 36 | **-36** |
| A 難度 + 未來 `on_fail_gold_bonus` | 800 | 0.10 | 200 | 80 | **+120**（罕見） |

> `conditionGoldBonus` 成敗皆加（通用加法），但 C-05 Jam 階段僅實作 `on_success_gold_bonus`，失敗路徑實務上恆為 `0`。保留通道供未來擴充，FT-05 程式無需改動。

---

### 3.6 設施維護費扣款管線（Maintenance Pipeline）

FT-05 在 `OnEnable` 訂閱 FT-07 `OnGuildMaintenanceDue(long dueTimestamp, Dictionary<int, int> perBuildingCost, int totalAmount)`。

```
OnGuildMaintenanceDue(dueTimestamp, perBuildingCost, totalAmount):
    1. if totalAmount <= 0 → Debug.LogError, return                 // 防禦性
    2. breakdown = new MaintenanceBreakdown {
           items           = new Dictionary<int, int>(perBuildingCost),  // defensive copy
           totalAmount     = totalAmount,
           netDelta        = -totalAmount,
           chargeTimestamp = dueTimestamp
       }
    3. ExecuteGoldFlow(breakdown)                                    // 見 3.8
    4. EventBus.Publish(OnMaintenanceCharged, breakdown)
```

**關鍵原則**：

- FT-05 **不計算**維護費金額（由 FT-07 依 `BuildingTable` 計算後傳入）
- FT-05 **不判斷**觸發時機（由 FT-07 或 F-02 tick 決定，例如每日 00:00 重置時觸發）
- 維護費扣款**可能觸發破產**（使用 `AddGoldAllowBankruptcy`）
- `items` 做 defensive copy，避免上游 Dictionary 被意外共用

---

### 3.7 職員薪水扣款管線（Salary Pipeline）

FT-05 在 `OnEnable` 訂閱 FT-08 `OnStaffSalaryDue(long dueTimestamp, Dictionary<int, int> perStaffSalary, int totalAmount)`。

```
OnStaffSalaryDue(dueTimestamp, perStaffSalary, totalAmount):
    1. if totalAmount <= 0 → Debug.LogError, return
    2. breakdown = new SalaryBreakdown {
           items           = new Dictionary<int, int>(perStaffSalary),  // defensive copy
           totalAmount     = totalAmount,
           netDelta        = -totalAmount,
           chargeTimestamp = dueTimestamp
       }
    3. ExecuteGoldFlow(breakdown)
    4. EventBus.Publish(OnSalaryCharged, breakdown)
```

**關鍵原則**：

- FT-05 **不計算**薪水金額（由 FT-08 依 `StaffTable` 計算後傳入）
- FT-05 **不判斷**觸發時機（由 FT-08 或 F-02 tick 決定）
- 薪水扣款**可能觸發破產**

---

### 3.8 破產狀態轉移快照機制（共用）

所有扣款管線共用此機制，確保 P-03 能辨識任何金流事件導致的狀態轉移：

```
ExecuteGoldFlow(breakdown):                              // breakdown 為 Commission/Maintenance/Salary 之一
    1. breakdown.goldBefore            = F03.GetGold()
    2. breakdown.bankruptcyStateBefore = F03.GetBankruptcyWarningState()
    3. F03.AddGoldAllowBankruptcy(breakdown.netDelta)    // 允許突破破產門檻
    4. breakdown.goldAfter             = F03.GetGold()
    5. breakdown.bankruptcyStateAfter  = F03.GetBankruptcyWarningState()
```

**狀態轉移對照表**（訂閱者責任，FT-05 不渲染）：

| `bankruptcyStateBefore → bankruptcyStateAfter` | 語義 | P-03 通知模板建議 |
|------------------------------------------------|------|-----------------|
| `Normal → Normal` | 一般金流，金幣仍為正 | 一般結算通知 |
| `Normal → Warning` | 金流使金幣轉負，觸發破產警告 | **破產警告開始**（含 F-03 倒數秒數） |
| `Warning → Warning` | 金流未讓金幣回正，倒數繼續 | 一般結算通知（可選：附加「倒數中」提示） |
| `Warning → Normal` | 金流使金幣回到非負，警告解除 | **負債已結清** |
| `Warning → Bankrupt` | 金流當下倒數剛到期（罕見） | **破產觸發** |
| `Bankrupt → Bankrupt` | 結算前已破產（如 FT-04 § 3.2 step 10 `AddReputation` 先觸發破產，再發 `OnMissionResolved`；或前一 tick 倒數到期） | 一般結算通知（Game Over 流程由 FT-06 單獨負責；FT-05 仍完成金流） |
| `Normal → Bankrupt` | 不可能（F-03 狀態機保證必先經過 `Warning`） | — |

> `Warning → Bankrupt` 的轉移通常由 F-02 每秒 tick 觸發而非金流觸發。若在金流扣款的**瞬間**倒數剛好到期且 F-03 在 `AddGoldAllowBankruptcy` 內部完成 tick，則會在此機制中被快照到。

**設計理由**：將狀態轉移識別**資料留在 payload、邏輯交給訂閱者**——符合資料驅動原則，未來擴充新通知類型（如「瀕臨破產」提醒）無需改動 FT-05。

---

### 3.9 事件契約

#### 3.9.1 上游事件（Consumer Contracts — FT-05 訂閱）

**`OnCommissionAccepted(int missionID, int baseReward, CommissionSource source)`**

由 FT-05 定義契約，上游依此實作。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `missionID` | `int` | FK → C-01 MissionTemplate |
| `baseReward` | `int` | 快照自 `C01.GetBaseReward(difficulty)`；上游負責取得，FT-05 不重查 |
| `source` | `CommissionSource` enum | `PlayerManual` / `NpcAutoPick` / `OfflineAutoPick`（與 FT-02 § 3.5 `Dispatch` source 對齊） |

| 發布場景 | 發布者 | source |
|---------|--------|--------|
| 玩家手動審核通過委託 | FT-02 Mission Dispatch | `PlayerManual` |
| 閒置冒險者自主接單（runtime） | FT-02 Mission Dispatch（**間接發布**：FT-03 自主接單呼叫 `FT02.Dispatch(..., NpcAutoPick)`，由 FT-02 代為發布，符合單一發布點原則——見 FT-02 § 3.5、FT-03 § 6.3） | `NpcAutoPick` |
| 離線期間自主接單追認 | 未來 FT-11 Offline Resolver | `OfflineAutoPick` |

**時序規則**：本事件必須在 ActiveMission 建立**之前**發布——確保預收入帳優先於任務時鐘啟動。若 `AddGold` 預收因 `GOLD_MAX` clamp 而實際入帳少於 `baseReward`，上游不需補償；屬玩家自身「金幣已爆表」的設計後果。

**`OnMissionResolved(Outcome outcome)`** — 由 FT-04 發布，詳見 FT-04 § 3.8。FT-05 消費 `isSuccess` / `baseReward` / `missionDifficulty` / `conditionGoldBonus` 等欄位。

**`OnGuildMaintenanceDue(long dueTimestamp, Dictionary<int, int> perBuildingCost, int totalAmount)`**

| 欄位 | 型別 | 說明 |
|------|------|------|
| `dueTimestamp` | `long` | 應扣款時刻的 UTC Unix seconds（支援離線追認） |
| `perBuildingCost` | `Dict<int, int>` | `buildingID → cost`，由 FT-07 查 `BuildingTable` 計算 |
| `totalAmount` | `int` | 合計（= `perBuildingCost.Values.Sum()`），FT-07 預先計算便利 FT-05 使用 |

**發布者**：FT-07 Guild Building（協同 F-02 Time System 定期 tick，例如每日 00:00 重置時觸發）。

**`OnStaffSalaryDue(long dueTimestamp, Dictionary<int, int> perStaffSalary, int totalAmount)`**

| 欄位 | 型別 | 說明 |
|------|------|------|
| `dueTimestamp` | `long` | 應扣款時刻的 UTC Unix seconds |
| `perStaffSalary` | `Dict<int, int>` | `staffID → salary`，由 FT-08 查 `StaffTable` 計算 |
| `totalAmount` | `int` | 合計 |

**發布者**：FT-08 Guild Staff（協同 F-02 Time System 定期 tick）。

> 雙向依賴登記見 § 6；FT-02 / FT-03 / FT-04 / FT-07 / FT-08 GDD 在本系統通過後需補反向聲明。

---

#### 3.9.2 下游事件（Publisher Contracts — FT-05 發布）

**`OnCommissionPrepaid(int missionID, int prepaidAmount, CommissionSource source)`**

| 訂閱者 | 行為 |
|--------|------|
| P-02 Main UI | 更新金幣顯示動畫（+prepaidAmount），委託板狀態改為「進行中」 |
| P-03 Notification | （可選）推播預收通知；`OfflineAutoPick` 併入離線摘要 |

**`OnCommissionSettled(CommissionBreakdown breakdown)`**

| 訂閱者 | 行為 |
|--------|------|
| P-02 Main UI | 結算視窗顯示 `breakdown` 完整明細（baseReward、commission/penalty、conditionBonus、netDelta、goldBefore→goldAfter） |
| P-03 Notification | 依 `isSuccess` + `bankruptcyStateBefore/After` 選擇通知模板（見 3.8 狀態轉移對照表） |
| FT-10 Save/Load | Jam 範疇外，未來擴充收支日誌時訂閱 |

**`OnMaintenanceCharged(MaintenanceBreakdown breakdown)`**

| 訂閱者 | 行為 |
|--------|------|
| P-02 Main UI | 顯示維護費扣款明細（可選列於公會總覽） |
| P-03 Notification | 依 `bankruptcyStateBefore/After` 選擇通知模板 |

**`OnSalaryCharged(SalaryBreakdown breakdown)`**

| 訂閱者 | 行為 |
|--------|------|
| P-02 Main UI | 顯示薪水扣款明細 |
| P-03 Notification | 依 `bankruptcyStateBefore/After` 選擇通知模板 |

**訂閱者責任**：

- 不修改傳入的 breakdown 物件（FT-05 不防禦，訂閱者自律）
- 訂閱者拋例外時 FT-05 不 try-catch；EventBus 層負責隔離

---

### 3.10 查詢 API

FT-05 採**純事件驅動**設計，**無對外公開查詢 API**：

- 所有金流明細透過事件 payload 傳遞（預收、結算、維護費、薪水 共 4 種事件）
- 訂閱者若需保留歷史（如 P-02 結算視窗重新開啟、FT-10 存檔收支日誌），自行 copy breakdown 快取
- FT-05 **不維護**：歷史清單、runtime 快取、加成快取

> 與 FT-04 § 3.9 同設計風格——杜絕下游強耦合，跨系統通訊統一走 EventBus。

---

## 4. 公式（Formulas）

### 4.1 有效率公式（Effective Rates）

本系統僅使用兩條有效率公式，驅動結算淨額計算。

```
effectiveCommissionRate = COMMISSION_RATE + accountantCommissionBonus
effectivePenaltyRate    = max(0, PENALTY_RATE + accountantPenaltyBonus + buildingPenaltyBonus)
```

**變數定義**：

| 變數 | 型別 | 來源 | Jam 值域 | 說明 |
|------|------|------|---------|------|
| `COMMISSION_RATE` | `float` | `SystemConstants` CSV | `0.20`（定值） | 基礎傭金率 |
| `PENALTY_RATE` | `float` | `SystemConstants` CSV | `0.10`（定值） | 基礎賠償率 |
| `accountantCommissionBonus` | `float` | FT-08 `GetAccountantCommissionBonus()` | `{0, +0.02}` | 會計職員已招募 → `+0.02`；否則 `0` |
| `accountantPenaltyBonus` | `float` | FT-08 `GetAccountantPenaltyBonus()` | `{0, -0.02}` | 會計招募 AND 已指派預備金保險櫃 → `-0.02`；否則 `0` |
| `buildingPenaltyBonus` | `float` | FT-07 `GetBuildingPenaltyBonus()` | `0`（保留擴充） | 建築層額外賠償加成，Jam 未使用 |

**值域**：

- `effectiveCommissionRate ∈ [0.20, 0.22]`
- `effectivePenaltyRate ∈ [0.06, 0.10]`（下限由 `max(0, ...)` 保證 ≥ 0；未來加成疊加時允許至 `0`）

**情境舉例**：

| 情境 | 會計招募？ | 指派保險櫃？ | `effectiveCommissionRate` | `effectivePenaltyRate` |
|------|-----------|-------------|---------------------------|------------------------|
| Jam 開局 | No | — | `0.20` | `0.10` |
| 僅招募會計（無指派） | Yes | No | `0.22` | `0.10` |
| 會計 + 保險櫃 | Yes | Yes | `0.22` | `0.06` |
| 會計未招募、保險櫃已解鎖但無人指派 | No | No | `0.20` | `0.10` |
| 系統降級（FT-08 null） | — | — | `0.20` | `0.10` |

---

### 4.2 淨額公式（Net Delta）

```
// 成功結算
commissionGoldAmount = Mathf.RoundToInt(baseReward × effectiveCommissionRate)
penaltyGoldAmount    = 0
netDelta             = commissionGoldAmount + conditionGoldBonus

// 失敗結算
commissionGoldAmount = 0
penaltyGoldAmount    = Mathf.RoundToInt(baseReward × effectivePenaltyRate)
netDelta             = -penaltyGoldAmount + conditionGoldBonus

// 維護費扣款（來自 OnGuildMaintenanceDue）
netDelta = -totalAmount

// 薪水扣款（來自 OnStaffSalaryDue）
netDelta = -totalAmount
```

**變數定義**：

| 變數 | 型別 | 來源 | 說明 |
|------|------|------|------|
| `baseReward` | `int` | `Outcome.baseReward` | C-01 MissionTemplate.baseReward，隨難度階梯 |
| `conditionGoldBonus` | `int` | `Outcome.conditionGoldBonus` | 由 FT-04 `ApplyConditionTraits` 累加後傳入，成敗皆加（通用加法） |
| `totalAmount` | `int` | `OnGuildMaintenanceDue` / `OnStaffSalaryDue` | 上游（FT-07 / FT-08）聚合後傳入 |

**取整策略**：`Mathf.RoundToInt`（銀行家捨入，`.5` 捨入至偶數）

- `120.5` → `120`
- `121.5` → `122`
- 單筆偏差最多 `0.5` 金；千筆以上累積期望值為 `0`，優於普通 `round`

**值域**（Jam 階段，B 難度 `baseReward = 600` 範例）：

| 情境 | netDelta 範圍 |
|------|--------------|
| 成功，無 condition bonus | `+120 ~ +132`（rate `0.20~0.22`） |
| 成功 + `on_success_gold_bonus (+600)` | `+720 ~ +732` |
| 失敗，無 condition bonus | `-60 ~ -36`（rate `0.10~0.06`） |
| 失敗 + 未來 `on_fail_gold_bonus (+200)` | `+140 ~ +164`（罕見翻正） |

---

### 4.3 完整結算流程範例

以下範例皆假設 `COMMISSION_RATE = 0.20`、`PENALTY_RATE = 0.10`，以檔案頂層「設計決策備忘」記載的加成規則計算。

**範例 A：Normal → Normal，B 難度成功（Jam 開局典型）**

```
Context:  current gold = 1200, 無會計, 無特質金錢獎勵
Input:    baseReward = 600, isSuccess = true, conditionGoldBonus = 0
公式:     commissionGoldAmount = RoundToInt(600 × 0.20) = 120
          netDelta = 120 + 0 = +120
金流:     AddGoldAllowBankruptcy(+120)
          goldBefore = 1200, goldAfter = 1320
狀態:     bankruptcyStateBefore = Normal, bankruptcyStateAfter = Normal
結果:     P-03 一般結算通知；P-02 金幣動畫 +120
```

**範例 B：Normal → Warning，B 難度失敗觸發破產警告**

```
Context:  current gold = 40, 無加成, 無特質獎勵
Input:    baseReward = 600, isSuccess = false, conditionGoldBonus = 0
公式:     penaltyGoldAmount = RoundToInt(600 × 0.10) = 60
          netDelta = -60 + 0 = -60
金流:     AddGoldAllowBankruptcy(-60)
          goldBefore = 40, goldAfter = -20
狀態:     bankruptcyStateBefore = Normal, bankruptcyStateAfter = Warning
結果:     P-03 推播「破產警告開始」（含 F-03 倒數秒數）；P-02 金幣變紅 -20
```

**範例 C：Warning → Normal，委託成功還清負債**

```
Context:  current gold = -80（Warning 倒數中）, 會計+保險櫃全配
Input:    baseReward = 600, isSuccess = true, conditionGoldBonus = 0
公式:     effectiveCommissionRate = 0.20 + 0.02 = 0.22
          commissionGoldAmount = RoundToInt(600 × 0.22) = 132
          netDelta = 132 + 0 = +132
金流:     AddGoldAllowBankruptcy(+132)
          goldBefore = -80, goldAfter = +52
狀態:     bankruptcyStateBefore = Warning, bankruptcyStateAfter = Normal
結果:     P-03 推播「負債已結清」；F-03 停止倒數
```

**範例 D：Normal → Warning，維護費扣款觸發破產警告**

```
Context:  current gold = 50, 公會有 3 棟設施
Input:    OnGuildMaintenanceDue(perBuildingCost = {1: 30, 2: 25, 3: 40}, totalAmount = 95)
公式:     netDelta = -95
金流:     AddGoldAllowBankruptcy(-95)
          goldBefore = 50, goldAfter = -45
狀態:     bankruptcyStateBefore = Normal, bankruptcyStateAfter = Warning
結果:     P-03 推播「破產警告開始」（維護費變體通知）；P-02 顯示三項設施扣款明細
```

**範例 E：Normal → Warning，職員薪水扣款觸發破產警告**

```
Context:  current gold = 30, 公會有 2 名職員（會計 50g、助理 40g）
Input:    OnStaffSalaryDue(perStaffSalary = {101: 50, 102: 40}, totalAmount = 90)
公式:     netDelta = -90
金流:     AddGoldAllowBankruptcy(-90)
          goldBefore = 30, goldAfter = -60
狀態:     bankruptcyStateBefore = Normal, bankruptcyStateAfter = Warning
結果:     P-03 推播「破產警告開始」（薪水變體通知）；P-02 顯示兩名職員薪水明細
```

**範例 F：Warning → Bankrupt，Warning 倒數末期薪水扣款壓垮公會（罕見）**

```
Context:  current gold = -120（Warning 倒數剩 ~1 秒）, 2 名職員
Input:    OnStaffSalaryDue(totalAmount = 90)
前提:     F-03 在 AddGoldAllowBankruptcy 內部完成 F-02 tick，剛好讓倒數到期
公式:     netDelta = -90
金流:     AddGoldAllowBankruptcy(-90)
          goldBefore = -120, goldAfter = -210
狀態:     bankruptcyStateBefore = Warning, bankruptcyStateAfter = Bankrupt
結果:     P-03 推播「破產觸發」；FT-06 Guild Core 依 F-03 OnBankruptcyWarningStateChanged 接管遊戲結束流程

註: Warning → Bankrupt 轉移通常由 F-02 每秒 tick 獨立觸發。若偶發於金流瞬間
    （F-03 tick-in-call 實作），payload 的 bankruptcyStateAfter 會快照為 Bankrupt，
    FT-05 仍正確識別。維護費扣款同理可觸發此轉移（將 OnStaffSalaryDue 換成
    OnGuildMaintenanceDue 即可，結構完全對稱）。
```

---

## 5. 邊緣案例（Edge Cases）

### 5.1 輸入驗證（Input Validation）

**Case 5.1.1** — `OnCommissionAccepted` 的 `baseReward ≤ 0`
- **行為**：`Debug.LogError("[FT-05] Invalid baseReward: {v} for mission {id}")`，**不**呼叫 `AddGold`、**不**發布 `OnCommissionPrepaid`，立即 `return`
- **理由**：上游（C-01 / FT-02）bug，FT-05 不補正

**Case 5.1.2** — `OnGuildMaintenanceDue` / `OnStaffSalaryDue` 的 `totalAmount ≤ 0`
- **行為**：`Debug.LogError`、return；**不**執行扣款、**不**發布 `Charged` 事件
- **理由**：無扣款項不應發事件；負值屬上游 bug

**Case 5.1.3** — `perBuildingCost` / `perStaffSalary` 為 `null`
- **行為**：`Debug.LogError`、return
- **理由**：避免後續 breakdown 寫入 `NullReferenceException`

**Case 5.1.4** — `items.Count == 0` 但 `totalAmount > 0`（不一致）
- **行為**：以 `totalAmount` 為準執行扣款，`breakdown.items = new Dictionary<int, int>()` 空字典，`Debug.LogWarning`
- **理由**：資料不一致屬上游 bug，但仍執行避免遊戲卡死；WARN 供測試抓出

**Case 5.1.5** — `Outcome == null`（FT-04 契約違反）
- **行為**：`Debug.LogError`、return
- **理由**：FT-04 `OnMissionResolved` 契約要求 `outcome` 非 null

**Case 5.1.6** — `outcome.baseReward == 0`（特殊任務）
- **行為**：**仍執行結算**，`commissionGoldAmount = 0`、`penaltyGoldAmount = 0`，`netDelta = conditionGoldBonus`（可能為 0）；`AddGoldAllowBankruptcy(0)` 為 no-op 但仍快照狀態並發布 `OnCommissionSettled`
- **理由**：保留未來「試煉 / 劇情任務」`baseReward = 0` 的擴充空間

---

### 5.2 上下游時序異常（Event Ordering）

**Case 5.2.1** — 同 `missionID` 重複 `OnCommissionAccepted`
- **行為**：**不去重**，第二次仍 `AddGold(+baseReward)` 並再次發布 `OnCommissionPrepaid`
- **理由**：去重需 runtime dictionary，違反無狀態原則；重複預收讓金幣異常上升**肉眼可見**，優於靜默吞錯

**Case 5.2.2** — `OnMissionResolved` 無前置預收（mission 未經 FT-05 預收就結算）
- **行為**：**不比對**預收紀錄（FT-05 不維護），照 `outcome` 完整結算
- **理由**：§3.5 定案——以 Outcome 為準，FT-05 為純執行者

**Case 5.2.3** — `OnCommissionAccepted` 遲於 `OnMissionResolved`（事件倒置）
- **行為**：分別獨立執行；結算先發生，預收後到達仍正常 `AddGold`
- **理由**：兩者無交互依賴，EventBus 層若有 queue 可能如此

**Case 5.2.4** — `OnGuildMaintenanceDue` 與 `OnStaffSalaryDue` 同 frame 觸發
- **行為**：依 EventBus 發布順序依次呼叫 `ExecuteGoldFlow`，各自獨立發 `Charged` 事件
- **理由**：順序由上游決定；P-03 累積兩通知，語義清晰

---

### 5.3 數值極值與取整（Numeric Extremes / Rounding）

**Case 5.3.1** — 成功結算後金幣超過 `GOLD_MAX` 被 F-03 clamp
- **觸發**：`goldBefore + netDelta > 9,999,999`（例如玩家囤金 9,900,000，收到大額 S 難度成功結算）
- **行為**：`AddGoldAllowBankruptcy(+netDelta)` 由 F-03 內部 clamp 至 `GOLD_MAX`；`goldAfter = 9,999,999`；`breakdown.netDelta` 保留**公式計算的原始值**（**不**回寫為 clamp 後的實際差額）
- **理由**：`netDelta` 代表「應支付」，實際支付差額訂閱者可透過 `goldAfter - goldBefore` 反推；保留原始值供 P-02 顯示「本應 +800g，金幣上限吃掉 200g」這類提示

**Case 5.3.2** — `GOLD_MAX` clamp 導致 `prepaidAmount < baseReward`
- **行為**：`prepaidAmount = F03.GetGold() - prevGold` 傳入 `OnCommissionPrepaid` 為實際入帳，**不補償**；結算時仍以 `outcome.baseReward` 計算
- **理由**：§3.9.1 時序規則已定案——屬玩家「金幣爆表」後果
- **UX 後果與建議**：
  - **失敗路徑淨虧**：預收被 clamp 時玩家少押金，若任務失敗仍以 `outcome.baseReward × effectivePenaltyRate` 扣賠償（與預收無關）——最終結果為「玩家純虧 penalty 金，未得任何回填」
  - **P-02 Main UI 責任**（建議）：派遣前檢查 `F03.GetGold() + baseReward > GOLD_MAX`，於預覽面板顯示警告「金幣即將達上限，本筆預收 `baseReward - (GOLD_MAX - goldBefore)` 將被截斷」
  - **P-03 Notification 責任**（建議）：訂閱 `OnCommissionPrepaid` 時若 `prepaidAmount < baseReward`，推播「預收被截斷」桌面通知，協助玩家意識到下次需先花費金幣再接單

**Case 5.3.3** — Banker's rounding 在 `.5` 邊界
- **行為**：`Mathf.RoundToInt(120.5) = 120`、`Mathf.RoundToInt(121.5) = 122`（捨入至偶數）
- **理由**：§4.2 已說明，避免單向累積偏差

**Case 5.3.4** — 結算時 `outcome.baseReward` 與預收時 `baseReward` 不一致
- **行為**：**以 `outcome.baseReward` 為準**，不比對預收值
- **金流後果**：若上游允許中途調整，可能產生淨差（例如預收 600g、結算 `baseReward = 1000g`，玩家實收超過 `600 × rate`）
- **Game Jam 階段**：此情境**不會發生**——FT-02 § 3.6 保證 `ActiveMission` 快照不變；C-01 `baseReward` 來自 CSV、Jam 階段無熱重載或 buff/debuff 動態修改管道。保留本規則為契約基準，供未來 FT-11 Offline Resolver 或事件系統若允許中途調整時沿用
- **理由**：FT-04 快照 `ActiveMission` 當下值為權威來源，FT-05 不跨系統比對；單一資料來源原則

---

### 5.4 加成查詢降級（Bonus Query Degradation）

**Case 5.4.1** — FT-07 / FT-08 尚未實作（Jam 開發序）
- **行為**：§3.4 null-check 分支回傳 `0f`，使用預設 `COMMISSION_RATE / PENALTY_RATE`
- **理由**：允許 FT-05 先於下游整合；§4.3 範例 A/B 仍完整運作

**Case 5.4.2** — FT-08 `GetAccountantCommissionBonus()` 拋例外
- **行為**：**不** `try-catch`，例外向上傳播、結算中斷
- **理由**：§3.3 原則——下游拋例外為嚴重錯誤，需 root cause 修復

**Case 5.4.3** — `effectivePenaltyRate` 被加成壓至負數
- **行為**：`max(0, ...)` 攔截至 `0`，失敗結算零賠償；**不**發 `Debug.LogWarning`
- **理由**：下限保護由 §3.4 / §4.1 定義；不噪音化 console

**Case 5.4.4** — `effectiveCommissionRate > 1.0`（無上限保護）
- **行為**：直接以計算結果執行（例如 `rate = 1.3` 使傭金超過原 `baseReward`）
- **理由**：屬 CSV 設計級異常，受表格審查把關；FT-05 不 clamp，保留「配出超額傭金」作為未來攻略空間

---

### 5.5 破產狀態機互動（Bankruptcy State Machine Interaction）

**Case 5.5.1** — 結算當下已在 `Bankrupt` 狀態
- **行為**：**仍執行** `AddGoldAllowBankruptcy` 與發布 `OnCommissionSettled`；`bankruptcyStateBefore / After` 皆為 `Bankrupt`
- **理由**：FT-05 為純執行者，終止時機由 FT-06 Game Over 流程決定（暫停 F-02 tick → 上游不再發事件）

**Case 5.5.2** — `AddGoldAllowBankruptcy(+X)` 讓 `Warning → Normal`（還債）
- **行為**：快照 `bankruptcyStateBefore = Warning, bankruptcyStateAfter = Normal`；P-03 依 §3.8 對照表選「負債已結清」模板（§4.3 範例 C）
- **理由**：F-03 狀態機內部負責此轉移

**Case 5.5.3** — 單次金流跨越 `Normal → Warning → Normal`（理論不可能）
- **行為**：不處理；F-03 保證單一 `netDelta` 無法同時穿零線兩次
- **理由**：F-03 GDD 應明確此保證（反向依賴登記時提醒）

**Case 5.5.4** — `Warning → Bankrupt` 金流觸發（罕見）
- 已於 §4.3 範例 F 示範；`ExecuteGoldFlow` 快照機制完整覆蓋

---

### 5.6 訂閱者異常（Subscriber Faults）

**Case 5.6.1** — P-02 / P-03 訂閱者拋例外
- **行為**：**不** `try-catch`，例外向上傳播至 EventBus 層
- **後果**：EventBus 若有隔離 → 其他訂閱者正常；若無 → 整個事件流斷裂
- **理由**：§3.3 / §3.9 原則——FT-05 不吞錯

**Case 5.6.2** — 訂閱者修改傳入的 `breakdown` 物件
- **行為**：**不**防禦（不 deep copy、不凍結）
- **後果**：後續訂閱者收到被污染資料
- **理由**：訂閱者自律原則；若未來出問題再考慮 immutable struct

**Case 5.6.3** — 事件發布時訂閱者尚未註冊
- **行為**：正常發布；EventBus 層決定 drop 或 queue，FT-05 **不** replay
- **理由**：訂閱者初始化順序為 Core / EventBus 職責

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（Upstream Dependencies）

| 依賴系統 | 介面型態 | 用途 | 降級策略 |
|---------|---------|------|---------|
| F-01 DataManager | API | 讀 `SystemConstants` 的 `COMMISSION_RATE`、`PENALTY_RATE` | 無（必備） |
| F-02 Time System | API | `NowUTC` 寫入 `settleTimestamp` / `chargeTimestamp` | 無（必備，時間源統一以 F-02 為準） |
| F-03 Resource Management | API | `AddGold` / `AddGoldAllowBankruptcy`（**新增**）/ `GetGold` / `GetBankruptcyWarningState` | 無（必備） |
| FT-04 Outcome Resolution | Event | 訂閱 `OnMissionResolved`，消費 `Outcome` 快照 | 無此事件 → 委託結算路徑未觸發（預收仍運作） |
| FT-07 Guild Building | Event + API | 訂閱 `OnGuildMaintenanceDue`；查詢 `GetBuildingPenaltyBonus()` | `null` → `buildingPenaltyBonus = 0`（§3.4） |
| FT-08 Guild Staff | Event + API | 訂閱 `OnStaffSalaryDue`；查詢 `GetAccountantCommissionBonus()` / `GetAccountantPenaltyBonus()` | `null` → 兩 bonus 皆 `0`（§3.4） |
| FT-02 Mission Dispatch / FT-03 NPC Decision / (未來) FT-11 Offline Resolver | Event | 發布 `OnCommissionAccepted` | 無此事件 → 無預收流程（結算仍可運作，但金流語義殘缺） |
| Core.EventBus | 基礎設施 | 訂閱 / 發布 | 無（必備） |
| C-05 Trait System（間接透過 FT-04） | 資料 | `Outcome.conditionGoldBonus` 源頭 | 無 trait → `conditionGoldBonus = 0` |

---

### 6.2 下游依賴（Downstream Consumers）

| 消費系統 | 訂閱事件 | 主要用途 |
|---------|---------|---------|
| P-02 Main UI | `OnCommissionPrepaid` / `OnCommissionSettled` / `OnMaintenanceCharged` / `OnSalaryCharged` | 金幣動畫、結算視窗、扣款明細 |
| P-03 Notification | 全 4 事件 | 依 `bankruptcyStateBefore/After` 選通知模板（§3.8 對照表） |
| FT-10 Save/Load | （Jam 範疇外）全 4 事件 | 收支日誌歷史 |
| FT-06 Guild Core | **不直接訂閱 FT-05**；透過 F-03 `OnBankruptcyWarningStateChanged` 接管 | Game Over 流程 |

---

### 6.3 事件契約矩陣（Event Contract Matrix）

| 事件 | 方向 | Payload | 定義位置 |
|------|------|---------|---------|
| `OnCommissionAccepted` | In | `(int missionID, int baseReward, CommissionSource source)` | §3.9.1（**FT-05 定義契約**，上游實作） |
| `OnMissionResolved` | In | `Outcome` | FT-04 §3.8 |
| `OnGuildMaintenanceDue` | In | `(long dueTimestamp, Dict<int,int> perBuildingCost, int totalAmount)` | §3.9.1（**FT-05 定義契約**，FT-07 實作） |
| `OnStaffSalaryDue` | In | `(long dueTimestamp, Dict<int,int> perStaffSalary, int totalAmount)` | §3.9.1（**FT-05 定義契約**，FT-08 實作） |
| `OnCommissionPrepaid` | Out | `(int missionID, int prepaidAmount, CommissionSource source)` | §3.9.2 |
| `OnCommissionSettled` | Out | `CommissionBreakdown` | §3.9.2 / §3.1.1 |
| `OnMaintenanceCharged` | Out | `MaintenanceBreakdown` | §3.9.2 / §3.1.2 |
| `OnSalaryCharged` | Out | `SalaryBreakdown` | §3.9.2 / §3.1.3 |

---

### 6.4 反向依賴登記清單（Reverse-Dependency Registration Checklist）

FT-05 通過後，以下 GDD 需補雙向依賴聲明：

**已完成（2026-04-22 跨系統審查 blocker 批次）：**
- [x] **F-03 Resource Management** — `AddGoldAllowBankruptcy(int delta)` API 已新增；§3.2 rule 7、§4.1a 偽代碼、§8 AC-RM-16~19；§6.2 已列 FT-05
- [x] **FT-04 Outcome Resolution** — §6.2 已列 FT-05（`OnMissionResolved` 訂閱）
- [x] **FT-02 Mission Dispatch** — §3.5 已發布 `OnCommissionAccepted(missionID, baseReward, source)`；§6.2 已列 FT-05；`source` enum 對齊三值
- [x] **FT-03 NPC Decision** — §6.3 已註明 FT-03 → FT-02.Dispatch(NpcAutoPick) 間接發布；單一發布點原則

**待設計系統（對應 GDD 開工時再補）：**
- [ ] **FT-07 Guild Building** — 登記為 `OnGuildMaintenanceDue` 發布者 + `GetBuildingPenaltyBonus()` API
- [ ] **FT-08 Guild Staff** — 登記為 `OnStaffSalaryDue` 發布者 + `GetAccountantCommissionBonus()` / `GetAccountantPenaltyBonus()` API；內部處理「會計已招募 AND 已指派預備金保險櫃」判斷
- [ ] **P-02 Main UI** — 訂閱 FT-05 四事件，渲染金幣動畫與明細視窗
- [ ] **P-03 Notification** — 訂閱 FT-05 四事件，依 `bankruptcyStateBefore/After` 對照表選模板
- [ ] **未來 FT-11 Offline Resolver** — 登記為 `OnCommissionAccepted(source = OfflineAutoPick)` 發布者

---

### 6.5 Game Jam 開發序與依賴降級（Development Order & Degradation）

建議實作順序與中斷點：

1. **F-01 / F-02 / F-03**（必備基礎；F-03 需補 `AddGoldAllowBankruptcy`）
2. **Core.EventBus**（必備基礎）
3. **FT-05 本體 + FT-04**（成對開發，委託金流可單獨驗證）— 此時 FT-07 / FT-08 尚未實作，`buildingPenaltyBonus = 0`、`accountantBonus = 0`，§4.3 範例 A / B 可完整跑通
4. **FT-08 Guild Staff**（會計加成生效；§4.3 範例 C 的 `0.22` 傭金率可測；薪水扣款路徑可測）
5. **FT-07 Guild Building**（維護費扣款；§4.3 範例 D 可測）
6. **P-02 / P-03**（UI 與通知；完整玩家體驗）

**降級觀測性**：FT-05 於 `Awake` 檢查依賴注入時，若 FT-07 / FT-08 為 `null`，`Debug.Log("[FT-05] FT-07 not present, buildingPenaltyBonus defaults to 0")` 僅輸出一次，避免每次結算噪音。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 FT-05 直接擁有的調整值（SystemConstants CSV）

| Knob | 預設 | 安全區間 | 影響 |
|------|------|---------|------|
| `COMMISSION_RATE` | `0.20` | `[0.05, 0.50]` | 成功結算玩家淨得比例；過低讓接單無動力，過高喪失「預收是押金」張力 |
| `PENALTY_RATE` | `0.10` | `[0.05, 0.30]` | 失敗結算玩家淨損比例；過低破產威脅消失，過高單次失敗即破產 |

---

### 7.2 影響 FT-05 公式的外部 Knob（交叉參考）

| Knob | 擁有系統 | 預設 | 安全區間 | 影響 FT-05 的路徑 |
|------|---------|------|---------|-----------------|
| `ACCOUNTANT_COMMISSION_BONUS` | FT-08 | `+0.02` | `[0, +0.10]` | 疊加至 `effectiveCommissionRate` |
| `ACCOUNTANT_PENALTY_BONUS` | FT-08 | `-0.02` | `[-0.10, 0]` | 疊加至 `effectivePenaltyRate`（配合 `max(0, ...)` 下限） |
| `BUILDING_PENALTY_BONUS` 來源 | FT-07 | `0`（Jam 未使用） | `[-0.10, 0]` | 疊加至 `effectivePenaltyRate` |
| `BuildingTable.maintenanceCost` 各棟 | FT-07 | CSV | 視棟別 | `OnGuildMaintenanceDue.totalAmount` |
| `StaffTable.salary` 各職員 | FT-08 | CSV | 視職員 | `OnStaffSalaryDue.totalAmount` |
| `GOLD_MAX` | F-03 | `9,999,999` | 固定 | §5.3.1 / §5.3.2 clamp 行為 |
| 破產倒數秒數 | F-03 | 固定（見 F-03 GDD） | — | 不影響 FT-05 執行，僅影響 P-03 通知文字 |

---

### 7.3 隱藏（程式級）設計常數

以下不是 CSV knob，屬程式碼層級的設計決策，改動等於改 GDD：

| 項目 | 值 | 改動條件 |
|------|-----|---------|
| 取整策略 | `Mathf.RoundToInt`（Banker's rounding） | 需重審 §4.2 理由 |
| `effectivePenaltyRate` 下限 | `0`（由 `max(0, ...)`） | 改為允許負值需同步 F-03 與 UI 設計 |
| `effectiveCommissionRate` 上限 | 無上限 | 若未來需要，需新增 `EFFECTIVE_COMMISSION_CAP` knob |
| `conditionGoldBonus` 通用加法 | 成敗皆加 | 若分別處理需修 §3.5 |
| defensive copy 於維護費 / 薪水 items | 啟用（`new Dictionary<int,int>(source)`） | 不建議關閉 |

---

### 7.4 平衡指引

以預設值（`COMMISSION_RATE = 0.20`、`PENALTY_RATE = 0.10`、會計加成 `±0.02`）計算的單次結算淨額：

- **B 難度 `baseReward = 600`**
  - 最壞成功（無加成）：`+120`（`20%` 回收）
  - 最好成功（會計 + 保險櫃）：`+132`（`22%`）
  - 最壞失敗：`-60`
  - 最好失敗（會計 + 保險櫃）：`-36`
- **S 難度 `baseReward = 1200`**
  - 最壞失敗：`-120`（佔 Jam 起始金 `1000` 的 `12%`）
  - 成功 + `on_success_gold_bonus = 600`：`+864`

**設計意圖**：

- 單次成功回收應足以抵銷多次失敗的破產風險
- 單次 S 難度失敗在低金儲備時**可獨力觸發 Warning**——鼓勵玩家積累緩衝而不過度保守
- `COMMISSION_RATE / PENALTY_RATE` 比例（預設 `2:1`）保持成功獎勵大於失敗懲罰，避免挫敗

調整 `COMMISSION_RATE` 時優先保持 `COMMISSION_RATE ≥ 2 × PENALTY_RATE` 以維持風險收益比。

---

## 8. 驗收標準（Acceptance Criteria）

FT-05 假設實作為 `MonoBehaviour`，於 `OnEnable` 訂閱事件、`OnDisable` 取消訂閱。所有 AC 以 Unity Editor Play Mode 或 integration test harness 模擬事件流可驗證。

### 8.1 委託預收

**AC-1 正常預收**
- Given 金幣 = `1000`，FT-08 已招募會計（irrelevant for prepay）
- When 發布 `OnCommissionAccepted(missionID=100, baseReward=600, source=PlayerManual)`
- Then 金幣 = `1600`；EventBus 收到 `OnCommissionPrepaid(100, 600, PlayerManual)`

**AC-2 預收輸入驗證**
- Given 任意金幣
- When 發布 `OnCommissionAccepted(..., baseReward=0)` 或 `baseReward=-1`
- Then 金幣不變；**未**發布 `OnCommissionPrepaid`；console 有 `[FT-05]` error log

**AC-3 `GOLD_MAX` clamp 預收**
- Given 金幣 = `9,999,900`
- When 發布 `OnCommissionAccepted(..., baseReward=500)`
- Then 金幣 = `9,999,999`；`OnCommissionPrepaid.prepaidAmount = 99`（實際入帳，非 500）

---

### 8.2 委託結算

**AC-4 成功結算（無加成）**
- Given 金幣 = `1000`，FT-07 / FT-08 null
- When FT-04 發布 `OnMissionResolved(outcome{isSuccess=true, baseReward=600, conditionGoldBonus=0})`
- Then 金幣 = `1120`；`OnCommissionSettled.breakdown.netDelta = +120`、`commissionGoldAmount = 120`、`penaltyGoldAmount = 0`、`effectiveCommissionRate = 0.20`

**AC-5 失敗結算（無加成）**
- Given 金幣 = `1000`，FT-07 / FT-08 null
- When FT-04 發布 `OnMissionResolved(outcome{isSuccess=false, baseReward=600, conditionGoldBonus=0})`
- Then 金幣 = `940`；`netDelta = -60`、`penaltyGoldAmount = 60`、`effectivePenaltyRate = 0.10`

**AC-6 會計 + 保險櫃加成生效**
- Given 金幣 = `1000`，FT-08 回傳 `accountantCommissionBonus = +0.02, accountantPenaltyBonus = -0.02`
- When 成功結算（baseReward=600）→ `commissionGoldAmount = 132`、`effectiveCommissionRate = 0.22`
- When 失敗結算（baseReward=600）→ `penaltyGoldAmount = 36`、`effectivePenaltyRate = 0.08`

**AC-7 `conditionGoldBonus` 通用加法**
- 成功 + `conditionGoldBonus = 500`：`netDelta = commissionGoldAmount + 500`
- 失敗 + `conditionGoldBonus = 500`：`netDelta = -penaltyGoldAmount + 500`（可能翻正為正值）

**AC-8 `CommissionBreakdown` 完整性**
- 任一成功或失敗結算後，`breakdown` 的 20 個欄位（§3.1.1）皆非預設空值
- 非 clamp 情境下：`goldBefore + netDelta == goldAfter`

---

### 8.3 維護費 / 薪水扣款

**AC-9 維護費扣款 + defensive copy**
- Given 金幣 = `500`
- When 發布 `OnGuildMaintenanceDue(T, items={1:30, 2:25, 3:40}, totalAmount=95)`
- Then 金幣 = `405`；`OnMaintenanceCharged.breakdown.items` 含 3 項且內容相符
- And 上游於發布後修改原 dict（如 `items[1] = 999`），`breakdown.items[1]` 仍為 `30`（defensive copy）

**AC-10 薪水扣款**
- Given 金幣 = `500`
- When 發布 `OnStaffSalaryDue(T, items={101:50, 102:40}, totalAmount=90)`
- Then 金幣 = `410`；`OnSalaryCharged.breakdown.items` 含 2 項

**AC-11 扣款輸入驗證**
- `totalAmount ≤ 0` 或 `items = null` → 金幣不變、**未**發布 `Charged` 事件、console error log

---

### 8.4 破產狀態轉移

**AC-12 `Normal → Warning`（失敗結算觸發）**
- Given 金幣 = `40`, `bankruptcyState = Normal`
- When 失敗結算 `baseReward=600`（無加成）
- Then 金幣 = `-20`；`breakdown.bankruptcyStateBefore = Normal, bankruptcyStateAfter = Warning`

**AC-13 `Warning → Normal`（還清負債）**
- Given 金幣 = `-80`, `bankruptcyState = Warning`，會計 + 保險櫃全配
- When 成功結算 `baseReward=600`
- Then 金幣 = `+52`；`bankruptcyStateBefore = Warning, bankruptcyStateAfter = Normal`

**AC-14 維護費 / 薪水觸發 Warning**
- 金幣 = `50`，維護費 `totalAmount = 95` → 金幣 = `-45`、`before=Normal, after=Warning`
- 金幣 = `30`，薪水 `totalAmount = 90` → 金幣 = `-60`、`before=Normal, after=Warning`

---

### 8.5 生命週期與降級

**AC-15 MonoBehaviour 訂閱生命週期 + Jam 降級 + CSV 調整**
- 生命週期：FT-05 `OnEnable` 訂閱 4 個 In 事件；`OnDisable` 取消訂閱；重複 enable/disable 後事件只處理一次（無 leak）
- 降級：FT-07 / FT-08 為 `null` 時 AC-4 / AC-5 仍通過（預設 rate `0.20` / `0.10`）
- 資料驅動：修改 CSV `SystemConstants.COMMISSION_RATE = 0.30`、重啟後 AC-4 的 `commissionGoldAmount` 由 `120` 變為 `180`；無需改 FT-05 程式碼

---

### 8.6 備註：待驗證事項（Deferred Verification）

- **無 hardcoded 金流比例字面量**：`grep` 審查 FT-05 程式碼中 `0.20` / `0.10` / `0.22` 等字面量不應出現於金流計算路徑（應全部來自 `SystemConstants`）——列為 `/design-review` 或後續 code review 階段的人工審查項目，**不列為本 GDD 強制驗收**。
