# Commission Flow（委託經濟循環）

> **狀態**：審查中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果
> **Jam 版範圍**：MVP，完整實作

## 概覽

**Commission Flow（委託經濟循環）** 是遊戲金幣流的完整管道，負責委託從接單到結算的所有金幣交易。

委託進入公會時，玩家選擇接受，系統立即向委託人收取全額報酬（`baseReward`）存入公會金庫；這筆錢在任務期間由公會持有。任務結束後，根據 Outcome Resolution 提供的結算記錄：成功則公會抽取 20% 作為傭金，剩餘 80% 返還（支付冒險者與委託完成費用）；失敗則全額退回委託人，並額外賠償 10% 作為違約金。

本系統是「接受委託」與「任務結算」兩個玩家動作之間的金融橋梁——它讓每一筆委託都對公會帳戶產生真實的數字變化，讓玩家感受到公會是一門「真正在經營的生意」。

## 玩家幻想

Commission Flow 服務的玩家幻想是：**感覺自己在管一間真正在做生意的公會，而不是在玩數字遊戲**。

接受一份委託的瞬間，金庫裡多了一筆錢——但那筆錢不是你的，是押金。你派出冒險者，等待的每一分鐘都是風險懸在空中。成功了，20% 傭金悄悄落入口袋，不多但踏實；失敗了，押金退回、再賠一成，帳本上多一筆紅字，下次要更謹慎。

這個系統不應該讓玩家感覺「失敗也沒關係」——也不該讓玩家覺得「賠一次就回不來了」。10% 的賠償是設計過的痛點：夠痛到讓你在意，不痛到讓你崩潰。真正的壓力來自於**同時有多筆委託在跑**：有幾筆賺、有幾筆賠，每次打開遊戲的淨結果才是你真正的經營成績。

## 詳細設計

### 核心規則

**兩個核心概念**

```typescript
gold:        number   // 實際持有金（可為負數，負值 = 債務，下筆收入優先抵償）
pendingGold: number   // 待結清義務金（所有進行中委託的最大潛在支出，唯讀衍生值）
```

`pendingGold` 不儲存，由 Commission Flow 即時計算，供 UI 顯示用。

**觸發點 1 — 接受委託**

```
1. 玩家點擊接受委託
2. Commission Flow 確認委託尚未被接受（防重複）
3. gold += baseReward                    → Resource Management（無條件加入，可自由使用）
4. preCollectedAmount = baseReward 記錄於 DispatchRecord（Mission Dispatch 持有）
```

**觸發點 2 — 處理結算**（由 Outcome Resolution 呼叫）

```
1. 接收 SettlementRecord
2. 依 outcome 執行扣款（允許 gold 進入負值）：

   SUCCESS / PYRRHIC：
     gold -= floor(preCollectedAmount × (1 − COMMISSION_RATE))   // 返還 80%
     公會淨得 floor(preCollectedAmount × COMMISSION_RATE)         // 淨得 20%

   FAILURE / DEATH：
     gold -= preCollectedAmount                                   // 退回全額
     gold -= floor(preCollectedAmount × COMPENSATION_RATE)        // 違約賠償 10%
     公會淨損 floor(preCollectedAmount × COMPENSATION_RATE)       // 淨損 10%

3. 若 gold 進入負值，玩家進入「債務狀態」，下筆正收益自動抵償
```

`**pendingGold` 計算（唯讀）**

```typescript
// 最壞情況估算：所有進行中任務全部失敗
pendingGold = activeMissions.reduce(
  (sum, r) => sum + r.preCollectedAmount + floor(r.preCollectedAmount × COMPENSATION_RATE),
  0
)
// 即 sum of (preCollectedAmount × 1.10) for each active mission
```

### 狀態與轉換

Commission Flow 不持有獨立狀態，以 `activeMissions` 的存在隱性表示：

```
委託被接受 → DispatchRecord 加入 activeMissions → pendingGold 增加
任務結算  → DispatchRecord 從 activeMissions 移除 → pendingGold 減少
```

**gold 的可能狀態**


| gold 範圍        | 狀態名稱 | 說明                                    |
| -------------- | ---- | ------------------------------------- |
| > 0            | 正常   | 可自由花費                                 |
| = 0            | 清空   | 無可用資金，但無債務                            |
| < 0            | 債務   | 下筆收入先抵償，非 Commission Flow 支出被拒絕       |
| < MAX_DEBT（負數） | 超限警告 | 調整旋鈕軟性上限（如 E 危險度：−100），供 UI 提示用，不強制阻擋 |


### 與其他系統的互動


| 系統                         | 方向  | 資料                                             |
| -------------------------- | --- | ---------------------------------------------- |
| **Mission Database（6）**    | 讀取  | 接受委託時查詢 `baseReward`（雙重確認，DispatchRecord 亦含此值） |
| **Resource Management（4）** | 寫入  | 接受時 `gold += baseReward`；結算時依 outcome 扣減（允許負值） |
| **Mission Dispatch（3）**    | 讀取  | 讀取 `activeMissions` 計算 `pendingGold`           |
| **Outcome Resolution（7）**  | 被呼叫 | 結算完成後呼叫 `processSettlement(SettlementRecord)`  |


## 公式

### 接受委託

```
gold += baseReward
preCollectedAmount = baseReward           // 記入 DispatchRecord
```

### 結算：SUCCESS / PYRRHIC

```
returnAmount = floor(preCollectedAmount × (1 − COMMISSION_RATE))
gold -= returnAmount

淨收入 = preCollectedAmount − returnAmount   ← source of truth
```

> `preCollectedAmount − floor(preCollectedAmount × 0.80)` 因 floor 取整，結果不一定等於 `floor(preCollectedAmount × 0.20)`（可能差 1 金）。淨收入以前者為準，`floor(x × 0.20)` 僅為估算，不作為實作基準。

### 結算：FAILURE / DEATH

```
penaltyAmount = floor(preCollectedAmount × COMPENSATION_RATE)
gold -= preCollectedAmount + penaltyAmount

淨損失 = penaltyAmount
       = floor(baseReward × 0.10)
```

> 以上兩條結算公式中，`gold` 允許進入負值（債務狀態），Resource Management 不設地板。

### pendingGold（唯讀，即時計算）

```typescript
pendingGold = activeMissions.reduce((sum, record) =>
  sum + record.preCollectedAmount + Math.floor(record.preCollectedAmount * COMPENSATION_RATE),
  0
)
// 最壞情況估算：所有進行中任務全部失敗
// = Σ floor(preCollectedAmount × 1.10) per active mission
```

### 常數定義

```typescript
const COMMISSION_RATE   = 0.20    // 成功傭金比率（20%）
const COMPENSATION_RATE = 0.10    // 失敗賠償比率（10%）

// MAX_DEBT：動態債務警告閾值，依世界危險度調整
// ⚠️ 依賴 World Danger System（12），尚未設計，暫定以下值
const WORLD_DANGER_DEBT_MULTIPLIER: Record<WorldDangerLevel, number> = {
  E: 1,    // MAX_DEBT = -100
  D: 5,    // MAX_DEBT = -500
  C: 10,   // MAX_DEBT = -1000
  B: 25,   // MAX_DEBT = -2500
  A: 50,   // MAX_DEBT = -5000
};

function getMaxDebt(dangerLevel: WorldDangerLevel): number {
  return -100 * WORLD_DANGER_DEBT_MULTIPLIER[dangerLevel];
}
```

> `MAX_DEBT` 為遊戲結局/失敗條件的判定基準（`gold < MAX_DEBT` 時觸發），與 World Danger System 掛鉤，早期遊戲容錯小（E 危險度：−100），後期隨收益規模擴大（A 危險度：−5000）。詳細失敗條件待後續遊戲結局系統設計時確認。

### 期望收益驗算（代表情境）

> 期望值公式：`E = P(success/pyrrhic) × baseReward × 0.20 − P(failure/death) × baseReward × 0.10`


| 情境                                                | P(成功) | baseReward | 期望淨值                                             | 備註            |
| ------------------------------------------------- | ----- | ---------- | ------------------------------------------------ | ------------- |
| **初期** F 階 × E 難度（baseReward≈50）                  | 75%   | 50         | `50×0.20×0.75 − 50×0.10×0.25` ≈ **+6 金**         | 新手情境，正期望、低風險  |
| **初期** E 階 × D 難度（baseReward≈100）                 | 65%   | 100        | `100×0.20×0.65 − 100×0.10×0.35` ≈ **+10 金**      | 早期主流，穩定正期望    |
| **主流** C 階遊俠 × B 採集（baseReward≈600）               | 53.5% | 600        | `600×0.20×0.535 − 600×0.10×0.465` ≈ **+36 金**    | 中期核心情境        |
| **主流** B 階戰士 × B 同階（baseReward≈600）               | 56%   | 600        | `600×0.20×0.56 − 600×0.10×0.44` ≈ **+41 金**      | 同階派遣，正期望穩定    |
| **高端** S 階 × S 難度（baseReward≈2000）                | 55%   | 2000       | `2000×0.20×0.55 − 2000×0.10×0.45` ≈ **+130 金**   | 高收益但死亡率 30%   |
| **極端** S 階 × SSS 難度（rankDiff=−2，baseReward≈10000） | 20%   | 10000      | `10000×0.20×0.20 − 10000×0.10×0.80` ≈ **−400 金** | 設計意圖：禁忌任務，負期望 |


> 所有初期/主流情境期望值為正，確認 20%/10% 設定可行。SSS 高難度期望為負，屬設計意圖（高風險懲罰），建議以聲望門檻限制出現時機。

## 極端情況

**1. gold 進入負值（正常債務狀態）**

任務失敗且玩家已花掉預收款，`gold -= preCollectedAmount + penaltyAmount` 後 gold < 0。此為設計意圖，不報錯。玩家進入債務狀態，後續所有正收益（傭金、新委託預收）自動填補負值，直到 gold ≥ 0。非 Commission Flow 的支出（雇用、建設）在 gold < 支出金額時被拒絕。

**2. gold < MAX_DEBT（危險度觸發閾值）**

`gold < getMaxDebt(worldDangerLevel)` 時觸發遊戲失敗條件（待遊戲結局系統設計確認）。各危險度門檻：E：−100、D：−500、C：−1000、B：−2500、A：−5000。`MAX_DEBT` 不強制阻擋操作，僅為判定基準；UI 在 gold 接近閾值時給予警告。

**3. 多筆任務同時結算且 gold 不足**

同一 tick 多筆 FAILURE 依序執行，每筆獨立扣款，gold 可能疊加為極大負值。按序處理（非原子），不影響其他系統的同 tick 操作。

**4. 接受委託後花光預收款，任務成功**

成功仍需 `gold -= floor(baseReward × 0.80)`，若此時 gold 不足則進入負值——即使任務成功，淨得的 20% 也要先填補負值。屬設計內高風險操作，玩家自行承擔。

**5. 同一委託重複接受（防重複觸發）**

接受委託前確認 `activeMissions` 中尚無對應 `dispatchId`，若已存在則靜默拒絕。防止 UI bug 造成同一委託重複預收。不以 `missionId` 去重，因委託板上可能同時存在多張相同類型但不同 `dispatchId` 的委託，以 `missionId` 去重會誤擋合法第二筆。

**6. preCollectedAmount = 0（異常任務）**

結算時淨收入與淨損失均為 0，無金幣流動，正常通過，不影響流程。

**7. pendingGold > gold（風險暴露狀態）**

`pendingGold > gold` 是完全正常的狀態（玩家花了部分預收款）。UI 以醒目顏色提示風險，不阻擋操作。此狀態下若所有任務全部失敗，gold 將進入負值甚至突破 `MAX_DEBT`。

## 依賴關係

**上游依賴（Commission Flow 依賴的系統）**


| 系統                          | 依賴性質        | 說明                                                                |
| --------------------------- | ----------- | ----------------------------------------------------------------- |
| **Mission Database（6）**     | 弱依賴         | 接受委託時讀取 `baseReward`（DispatchRecord 已含此值，以 DispatchRecord 為主要來源）  |
| **Resource Management（4）**  | 強依賴         | 所有 `gold` 增減透過此系統執行                                               |
| **Mission Dispatch（3）**     | 強依賴         | 讀取 `activeMissions` 計算 `pendingGold`；接受委託時寫入 `preCollectedAmount` |
| **Outcome Resolution（7）**   | 強依賴         | 結算完成後呼叫 `processSettlement(SettlementRecord)`                     |
| **World Danger System（12）** | 強依賴（⚠️ 未設計） | 讀取當前危險度以計算動態 `MAX_DEBT`                                           |


**下游依賴（依賴 Commission Flow 的系統）**

無。本系統為金幣流終點，所有輸出進入 Resource Management，無其他系統讀取 Commission Flow 的輸出。

**依賴方向**

```
Mission Database（6）
Resource Management（4）
Mission Dispatch（3）      ──→  Commission Flow（15）  ──→  （金流終點）
Outcome Resolution（7）
World Danger System（12）
```

## 調整旋鈕


| 旋鈕                             | 位置                   | 目前值                    | 安全範圍         | 說明                                              |
| ------------------------------ | -------------------- | ---------------------- | ------------ | ----------------------------------------------- |
| `COMMISSION_RATE`              | `commission-flow.ts` | 0.20（20%）              | 0.05 ~ 0.40  | 調高增加成功收益；調低壓縮獲利空間；影響整體金幣收入節奏                    |
| `COMPENSATION_RATE`            | `commission-flow.ts` | 0.10（10%）              | 0.05 ~ 0.25  | 調高讓失敗更痛；調低降低新手挫敗感；與 `COMMISSION_RATE` 共同決定期望值正負 |
| `WORLD_DANGER_DEBT_MULTIPLIER` | `commission-flow.ts` | E:1/D:5/C:10/B:25/A:50 | 各值 ×0.5 ~ ×2 | 調整各危險度的最大債務容忍；值越大玩家越晚觸發遊戲失敗條件                   |


> `COMMISSION_RATE` 與 `COMPENSATION_RATE` 需配對調整，確保主流情境的期望值為正。建議驗證：`COMMISSION_RATE × P(success) > COMPENSATION_RATE × P(failure)`。

## 視覺／音效需求

[待設計]

## UI 需求

- 主介面常駐顯示兩個數字：**實際持有金**（`gold`，可為負數）與**待結清義務金**（`pendingGold`）
- `gold` 為負值時以紅色顯示，並附上「債務中」標籤
- `gold` 接近 `MAX_DEBT`（差距 ≤ 10%）時顯示警告提示
- 接受委託時即時更新 `pendingGold` 顯示
- 結算後即時更新 `gold` 與 `pendingGold`

## 驗收標準

**接受委託驗收**

- 接受委託後 `gold` 增加 `baseReward`，金額與 DispatchRecord 中的 `baseReward` 一致
- `DispatchRecord.preCollectedAmount` 等於接受時的 `baseReward`
- 同一 `missionId` 第二次接受被靜默拒絕，`gold` 不重複增加

**結算驗收**

- SUCCESS：`gold` 減少 `floor(preCollectedAmount × 0.80)`，淨得 `preCollectedAmount − floor(preCollectedAmount × 0.80)`
- PYRRHIC：金幣流與 SUCCESS 完全相同
- FAILURE：`gold` 減少 `preCollectedAmount + floor(preCollectedAmount × 0.10)`
- DEATH：金幣流與 FAILURE 完全相同
- 結算後 `gold` 可為負值，不被 clamp 至 0

**pendingGold 驗收**

- 無進行中任務時 `pendingGold = 0`
- 接受一筆 B 難度委託（`baseReward = 600`）後：`pendingGold = floor(600 × 1.10) = 660`
- 任務結算後，對應委託從 `pendingGold` 計算中移除，數值即時更新

**債務狀態驗收**

- `gold < 0` 時，雇用冒險者、公會建設等支出被拒絕（非 Commission Flow 支出）
- `gold < 0` 且 Guild Building `reserve = true`（已建設**預備金保險櫃**）時，接受新委託仍可執行（`gold += baseReward` 可將負值拉回）；`reserve = false` 時接受委託被拒絕（由 Main UI Shell 層攔截，Commission Flow 本身不感知 reserve 狀態）
- E 危險度：`gold < -100` 觸發 MAX_DEBT 警告提示
- A 危險度：`gold < -5000` 觸發 MAX_DEBT 警告提示

**整合驗收**

- Outcome Resolution 呼叫 `processSettlement()` 後，`gold` 正確更新，誤差為 0
- Resource Management 的 `gold` 數值與 Commission Flow 計算結果完全一致
- `pendingGold` 隨 `activeMissions` 增減即時更新，無延遲

## 待解問題


| 問題                     | 狀態    | 備註                                                                                                                                                  |
| ---------------------- | ----- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| 遊戲失敗條件的完整定義            | 待設計   | `gold < MAX_DEBT` 僅為觸發條件之一，完整失敗邏輯待遊戲結局系統確認                                                                                                          |
| World Danger System 介面 | ✅ 已解決 | `Commission Flow` 直接讀取 `WorldDangerState.currentLevel` 傳入 `getMaxDebt(currentLevel)`，對應乘數 E:×1/D:×5/C:×10/B:×25/A:×50，與 world-danger-system.md 完全對齊 |


