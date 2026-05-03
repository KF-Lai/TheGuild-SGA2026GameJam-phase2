# Outcome Resolution（任務結算）
<!--
  還原來源：Cursor agent transcript 765098c1-4ce5-4480-af28-321deae1f4d2（Write/StrReplace 重播）。
  若與 src/ 衝突，依專案約定以對話紀錄與本 GDD 為準。
-->


> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果
> **Jam 版範圍**：MVP，完整實作

## 概覽

**Outcome Resolution（任務結算）** 是公會收益的結算閘門。當 Time / Tick System 判定任務計時結束後，本系統自動執行：擲骰決定任務成功或失敗、判斷冒險者是否死亡、更新冒險者狀態、調整公會聲望，並向 Commission Flow 輸出結算記錄（傭金收取或賠償支付的依據）。

玩家不參與結算過程本身——他們離線時任務在背景計時，回來時結果已定。Outcome Resolution 是「派出去」與「拿回來」之間的橋梁：它把 Mission Dispatch 建立的派遣記錄，轉化為冒險者的命運與公會的帳目變動。

沒有這個系統，任務就只是單純地「消失」，沒有後果，核心的委託循環便無法閉合。

## 玩家幻想

Outcome Resolution 服務的玩家幻想是：**打開遊戲的那一秒，感覺過去的決策正在兌現**。

結算是玩家每次回來時的第一個儀式——滑過任務清單，看哪個成功了、哪個失敗了、有沒有人沒回來。這個「揭曉時刻」是遊戲中最有重量的 5 秒鐘：成功讓人心安（或竊喜），失敗讓人懊悔（或接受），死亡讓人久久放不下。

這個系統必須讓玩家感受到**後果是真實的**：傭金是真的賺到了；賠償是真的在痛；那個被你送去高難度任務的 C 階冒險者，回不來了。Outcome Resolution 是「你的決定，你負責」這句話的具體實現。

這個系統不應該讓玩家覺得「反正也沒差」——但也不該讓任何一次失敗感覺毀滅性。10% 的賠償讓失敗有代價，卻不致命；死亡讓遊戲有重量，卻有名冊補充的空間。每一次結算都是一個問號換成句號，讓玩家帶著新的資訊去做下一輪決策。

## 詳細設計

### 核心規則

**DispatchRecord 的來源與生命週期**

進行中任務以 `DispatchRecord` 陣列形式存放於 Mission Dispatch 的狀態層（`activeMissions: DispatchRecord[]`）。Mission Dispatch 於接單時建立記錄；Time/Tick System 計時到期時以 `dispatchId` 通知 Outcome Resolution；Outcome Resolution 讀取並消費（移除）記錄後完成結算。Save/Load 序列化 `activeMissions` 以保存進行中任務。

```typescript
interface DispatchRecord {
  dispatchId:        string;            // 派遣記錄唯一 ID
  adventurerId:      string;
  missionId:         string;
  missionDifficulty: MissionDifficulty;
  baseReward:        number;
  preCollectedAmount: number;           // 預收報酬（= baseReward，供 Commission Flow 傭金/賠償計算）
  finalSuccessRate:  number;
  finalDeathRate:    number;
  startTimestamp:    number;
  endTimestamp:      number;
}
```

**4 種結算結果**

| 結果代碼 | 成功擲骰 | 致死？ | 冒險者狀態 | 傭金/賠償 | 聲望 | outcome 字串值 |
|---------|---------|--------|----------|---------|------|--------------|
| `SUCCESS` | 通過 | 否 | → `idle` | 收 20% | 成功表 | `'success'` |
| `PYRRHIC` | 通過 | 是 | → 移除 | 收 20% | 成功表 | `'pyrrhic'` |
| `FAILURE` | 未通過 | 否 | → `idle` | 賠 10% | 失敗表 | `'failure'` |
| `DEATH` | 未通過 | 是 | → 移除 | 賠 10% | 失敗表 | `'death'` |

> **注意**：死亡擲骰在成功與失敗時都執行。「致死？」欄表示死亡擲骰的結果，不代表 roll 本身不執行。

**結算流程（每筆任務計時到期時執行一次）**

```
1. Time/Tick System 計時到期，呼叫 `onMissionExpired(dispatchId, isLongOffline)`
   Outcome Resolution 以 dispatchId 從 activeMissions 查找完整 DispatchRecord
   （`isLongOffline` 供通知顯示使用，不影響結算邏輯）
2. successRoll = Math.random() < finalSuccessRate
3. deathRoll   = Math.random() < finalDeathRate
4. 判斷 outcome：
     successRoll && !deathRoll → SUCCESS
     successRoll &&  deathRoll → PYRRHIC
    !successRoll && !deathRoll → FAILURE
    !successRoll &&  deathRoll → DEATH
5. 更新冒險者狀態（→ Adventurer Management）
6. 更新公會聲望（→ Resource Management）
7. 建立 SettlementRecord，加入 pendingResults 未讀清單
8. 從 activeMissions 移除已結算的 DispatchRecord
9. 觸發通知（→ Notification/Feedback，弱依賴）
```

**未讀結算清單（pendingResults）**

```typescript
// 所有者：Outcome Resolution（本系統的持久化狀態層）
pendingResults: SettlementRecord[]  // 已結算但玩家尚未確認查看的記錄
```

結算後加入 `pendingResults`；玩家在 UI 確認查看後由 UI 層呼叫 `dismissResult(dispatchId)` 移除。Save/Load 序列化此清單以保存離線期間的待讀結果。`pendingResults` 無數量上限（玩家離線再久也不會遺失結果）。

### 狀態與轉換

**任務狀態機**

```
[ACTIVE]  ──計時到期──→  [RESOLVED]
```

RESOLVED 後，DispatchRecord 加上 `outcome` 欄位封存，不可再改變。

**冒險者狀態轉換**（由本系統寫入，定義於 Adventurer Management）

```
[on_mission] ──SUCCESS/FAILURE──→ [idle]  （status='idle', currentMissionId=null）
[on_mission] ──PYRRHIC/DEATH────→ [dead]  （從名冊移除）
```

### 與其他系統的互動

| 系統 | 方向 | 資料 |
|------|------|------|
| **Time/Tick System（9）** | 觸發（輸入）| `onMissionExpired(dispatchId: string, isLongOffline: boolean)`；`isLongOffline = true` 表示任務到期超過 3 天 |
| **Adventurer Management（2）** | 寫入 | SUCCESS/FAILURE：`status='idle'`，`currentMissionId=null`<br>PYRRHIC/DEATH：從名冊移除 |
| **Resource Management（4）** | 寫入 | `REPUTATION_DELTA[difficulty][success/failure]` |
| **Commission Flow（15）** | 輸出（⚠️ 暫定） | `SettlementRecord`（見公式節定義） |
| **Notification/Feedback（16）** | 觸發（弱依賴） | outcome 事件通知 |

**⚠️ 暫定介面：Commission Flow 尚未設計，以下為 Outcome Resolution 對外輸出的合約草案：**

```typescript
interface SettlementRecord {
  dispatchId:         string;            // 派遣記錄 ID
  missionId:          string;
  adventurerId:       string;
  outcome:            'success' | 'pyrrhic' | 'failure' | 'death';
  missionDifficulty:  MissionDifficulty;
  baseReward:         number;            // 來自 Mission Database，傭金計算基準
  preCollectedAmount: number;            // 接單時從委託人收取的金額（Jam 版 = baseReward）
  resolvedAt:         number;            // Unix timestamp
}
```

> Commission Flow 將用 `outcome`、`baseReward`、`preCollectedAmount` 計算金幣流向：
> - **接受委託時**：`gold += preCollectedAmount`（全額預收）
> - **SUCCESS / PYRRHIC**：`gold -= floor(preCollectedAmount × 0.80)`（返還 80%，guild 淨得 20%）
> - **FAILURE / DEATH**：`gold -= preCollectedAmount + floor(preCollectedAmount × 0.10)`（全額退回 + 10% 賠償，淨損失 10%）
>
> Jam 版 `preCollectedAmount === baseReward`（全額預收）。

## 公式

### 成功擲骰

```
successRoll    = Math.random()
outcome_success = successRoll < finalSuccessRate
```

### 死亡擲骰

```
deathRoll    = Math.random()
outcome_death = deathRoll < finalDeathRate
```

> `finalDeathRate` 由 Mission Dispatch 計算後傳入，本系統不重算。兩次擲骰獨立執行（成功與失敗皆可觸發死亡）。

### baseMissionDeathRate（本系統擁有，供 Mission Dispatch 讀取）

| 難度 | F | E | D | C | B | A | S | SS | SSS |
|------|---|---|---|---|---|---|---|----|-----|
| 基礎死亡率 | 2% | 6% | 10% | 13% | 18% | 25% | 30% | 38% | 50% |

```typescript
const BASE_MISSION_DEATH_RATE: Record<MissionDifficulty, number> = {
  F:   0.02,
  E:   0.06,
  D:   0.10,
  C:   0.13,
  B:   0.18,
  A:   0.25,
  S:   0.30,
  SS:  0.38,
  SSS: 0.50,
};
```

### SettlementRecord 中 baseReward 的來源

```
baseReward = MissionDatabase[missionId].reward
```

純查表，不計算。Commission Flow 將以此為基準計算傭金與賠償：

```
// 接受委託時（由 Commission Flow 執行）
預收金額  = preCollectedAmount                                        // = baseReward

// 結算時（SUCCESS / PYRRHIC）
返還金額  = floor(preCollectedAmount × (1 − COMMISSION_RATE))        // 預設 0.80
guild 淨得 = floor(preCollectedAmount × COMMISSION_RATE)             // 預設 0.20

// 結算時（FAILURE / DEATH）
返還金額  = preCollectedAmount                                        // 全額退回
賠償金額  = floor(preCollectedAmount × COMPENSATION_RATE)            // 預設 0.10
guild 淨損 = floor(preCollectedAmount × COMPENSATION_RATE)           // 淨損 10%
```

> `COMMISSION_RATE` 與 `COMPENSATION_RATE` 屬於 Commission Flow 的調整旋鈕，在此僅作參考定義。

### 代表性情境的期望死亡率（設計驗證）

| 情境 | finalSuccessRate | finalDeathRate | P(PYRRHIC) | P(DEATH) | 總死亡率 | 期望金幣淨值 |
|------|----------------|---------------|-----------|---------|--------|-----------|
| C 階遊俠 × B 採集（baseReward~600）| 55% | 8% | 4.4% | 3.6% | **8%** | +82 金 |
| B 階戰士 × B 戰鬥（同階，baseReward~600）| 70% | 18% | 12.6% | 5.4% | **18%** | +78 金 |
| S 階 × S 難度（無特質，baseReward~2000）| 55% | 30% | 16.5% | 13.5% | **30%** | +110 金 |
| S 階 × SSS 難度（rankDiff=−2，baseReward~10000）| 20% | 65% | 13% | 52% | **65%** | −480 金 |

> 期望值計算：`E = P(success/pyrrhic) × baseReward × 0.20 − P(failure/death) × baseReward × 0.10`
> SSS 期望為負值，屬設計意圖（高風險高懲罰內容）。

> SSS 難度等效死亡率 65% 屬於「高難度禁忌任務」設計意圖，建議透過聲望門檻限制出現時機。

### 聲望更新

```
isSuccess       = outcome === 'success' || outcome === 'pyrrhic'
reputationDelta = REPUTATION_DELTA[missionDifficulty][isSuccess ? 'success' : 'failure']
ResourceManagement.updateReputation(reputationDelta)
```

> `REPUTATION_DELTA` 表定義於 `resource-management.md`，本系統引用不重定義。PYRRHIC 視同 success，DEATH 視同 failure。

## 極端情況

**1. `finalSuccessRate = 0`（等級差 ≤ −4，無職業加成）**

成功擲骰必然失敗，但死亡擲骰仍獨立執行。結果只會是 `FAILURE` 或 `DEATH`，不可能出現 `SUCCESS`/`PYRRHIC`。公式自然覆蓋，不需特殊處理。

**2. `finalDeathRate` 可能為 0（低難度 + 有利等級差 + 職業加成）**

當 `baseMissionDeathRate + deathRateModifier + RANK_DIFF_DEATH_MOD ≤ 0` 時，`clamp` 後 `finalDeathRate = 0`，死亡擲骰必然為 false。結果只會是 `SUCCESS` 或 `FAILURE`，永不出現 `PYRRHIC`/`DEATH`。需三項因子的總和才能確定是否為 0，非僅等級差單一條件。符合設計意圖。

**3. 多筆任務同時計時到期**

同一 tick 內可能多筆 DispatchRecord 同時到期。每筆獨立結算，互不干擾。Adventurer Management 和 Resource Management 的寫入按序執行（非原子操作），無並發問題（單執行緒 JavaScript）。

**4. 同一冒險者的兩筆任務同時到期（bug 情境）**

這不應發生——冒險者在 `on_mission` 狀態時 Mission Dispatch 不允許派遣第二次。若因 bug 出現此情況，以第一筆結算為準；第二筆結算時若冒險者已不在名冊或已 idle，記錄錯誤日誌並跳過冒險者狀態更新，其餘結算照常執行。

**5. 結算時 `missionId` 在 Mission Database 中找不到（資料損毀）**

`baseReward` 無法查表 → 以 `baseReward = 0` 處理（傭金/賠償皆為 0），記錄錯誤日誌，冒險者狀態更新與聲望變動照常執行，不因資料異常阻擋整體結算流程。

**6. 連續失敗導致聲望觸底（−100）**

由 Resource Management 的 `clamp` 處理，聲望鎖定在 −100。Outcome Resolution 不需特殊處理，直接傳入 `reputationDelta` 即可。

**7. PYRRHIC 冒險者移除後，同 tick 再次對其操作**

冒險者移除後名冊中已無此記錄。後續對此 `adventurerId` 的任何操作應靜默忽略（no-op），不拋例外，不影響其他結算進行。

## 依賴關係

**上游依賴（Outcome Resolution 依賴的系統）**

| 系統 | 依賴性質 | 說明 |
|------|---------|------|
| **Mission Dispatch（3）** | 強依賴 | 接收 DispatchRecord（adventurerId、missionId、finalSuccessRate、finalDeathRate） |
| **Adventurer Management（2）** | 強依賴 | 寫入冒險者狀態：idle（存活）或移除（死亡） |
| **Resource Management（4）** | 強依賴 | 寫入聲望變動 `REPUTATION_DELTA` |
| **Mission Database（6）** | 弱依賴 | 查詢 `baseReward`（用於 SettlementRecord） |
| **Time / Tick System（9）** | 強依賴 | 計時到期事件觸發結算 |

**下游依賴（依賴 Outcome Resolution 的系統）**

| 系統 | 依賴性質 | 從此取得的資料 |
|------|---------|-------------|
| **Commission Flow（15）** | 強依賴 | SettlementRecord（outcome、baseReward → 計算傭金/賠償） |
| **Notification / Feedback（16）** | 弱依賴 | outcome 事件通知（結算完成後觸發） |

**依賴方向**

```
Time / Tick System（9）
Mission Dispatch（3）
Adventurer Management（2）  ──→  Outcome Resolution（7）  ──→  Commission Flow（15）
Resource Management（4）                                   ──→  Notification/Feedback（16）
Mission Database（6）
```

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| `BASE_MISSION_DEATH_RATE` 各級值 | `outcome-resolution.ts` | 見公式節 | ×0.5 ~ ×2 | 整體縮放死亡率；調高讓遊戲更緊張，調低讓冒險者更好養 |
| `COMMISSION_RATE` | `commission-flow.ts`（⚠️ 暫定） | 0.20（20%）| 0.05 ~ 0.40 | 傭金比率；調高讓玩家收入更多，調低壓縮經濟空間 |
| `COMPENSATION_RATE` | `commission-flow.ts`（⚠️ 暫定） | 0.10（10%）| 0.05 ~ 0.25 | 賠償比率；調高讓失敗更痛，調低降低新手挫敗感 |

> `COMMISSION_RATE` 與 `COMPENSATION_RATE` 屬於 Commission Flow 的調整旋鈕，此處列出作為參考；source of truth 待 Commission Flow GDD 設計後確認。

## 視覺／音效需求

[待設計]

## UI 需求

**結算結果呈現**

- 玩家回到遊戲時，若 `pendingResults` 非空，主介面顯示結算通知提示
- 結算列表預設以清單形式顯示所有未讀結果（效率優先，符合碎片時間設計）
- 每筆結果顯示：任務名稱、冒險者名稱、outcome 標籤（成功／慘勝／失敗／死亡）
- 點擊任一筆可展開詳細資訊（傭金/賠償金額、聲望變動、冒險者狀態）
- 提供「全部確認」按鈕，一次 dismiss 所有未讀記錄
- 確認後從 `pendingResults` 移除對應記錄

## 驗收標準

**結算結果驗收**

- [ ] `finalSuccessRate=1.0`、`finalDeathRate=0.0` → 結果必為 `SUCCESS`
- [ ] `finalSuccessRate=0.0`、`finalDeathRate=0.0` → 結果必為 `FAILURE`
- [ ] `finalSuccessRate=1.0`、`finalDeathRate=1.0` → 結果必為 `PYRRHIC`
- [ ] `finalSuccessRate=0.0`、`finalDeathRate=1.0` → 結果必為 `DEATH`
- [ ] 跑 1000 次 `finalSuccessRate=0.5` → 成功率落在 45%~55% 之間（統計驗證）

**冒險者狀態驗收**

- [ ] `SUCCESS`/`FAILURE` 後：冒險者 `status='idle'`，`currentMissionId=null`
- [ ] `PYRRHIC`/`DEATH` 後：冒險者從名冊移除，名額即時釋放

**聲望驗收**

- [ ] `SUCCESS`（B 難度）後：聲望增加 +5（符合 `REPUTATION_DELTA` 表）
- [ ] `FAILURE`（C 難度）後：聲望減少 −4
- [ ] `PYRRHIC` 視同 success：聲望增加
- [ ] `DEATH` 視同 failure：聲望減少

**SettlementRecord 驗收**

- [ ] `SUCCESS`/`PYRRHIC` 後：`SettlementRecord.outcome` 正確為 `'success'`/`'pyrrhic'`
- [ ] `baseReward` 與 Mission Database 查表結果一致
- [ ] `resolvedAt` 為結算時的 Unix timestamp，非派遣時間

**極端情況驗收**

- [ ] 多筆任務同 tick 到期：每筆獨立結算，結果互不干擾
- [ ] `missionId` 找不到時：`baseReward=0`，冒險者狀態與聲望仍正常更新，記錄錯誤日誌
- [ ] 冒險者移除後再次對其操作：靜默忽略，不拋例外

**整合驗收**

- [ ] 結算後 Resource Management 的 `reputation` 正確更新
- [ ] Commission Flow 能讀取 SettlementRecord 並計算正確金額
- [ ] Notification/Feedback 在結算後收到 outcome 事件

## 待解問題

以下問題已在設計過程中解決：

| 問題 | 決策 | 日期 |
|------|------|------|
| DispatchRecord 儲存位置 | 方案 C：獨立 `activeMissions: DispatchRecord[]`，掛在 Mission Dispatch 狀態層 | 2026-04-06 |
| Mission Dispatch 範例數字 | 已同步更新（B 難度死亡率 18% → 20%）| 2026-04-06 |
| Commission Flow 介面合約 | SettlementRecord 加入 `preCollectedAmount`，Jam 版 = baseReward | 2026-04-06 |
| 結算歷史記錄保留方式 | 未讀清單（`pendingResults`），玩家確認後移除，無數量上限 | 2026-04-06 |
| 離線多筆待結算呈現 | 清單預設顯示 + 點擊展開細節 + 全部確認按鈕 | 2026-04-06 |
