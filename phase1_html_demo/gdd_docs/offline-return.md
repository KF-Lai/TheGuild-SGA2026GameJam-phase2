# 離線回歸事件系統 — 「信使歸來」

> **狀態**：設計完成
> **作者**：系統設計師 + Claude Code Game Studios
> **最後更新**：2026-04-08
> **支柱對應**：支柱 1 — 離線 / 長期經營感
> **Jam 版範圍**：MVP，完整實作

## 概覽

**離線回歸事件系統（「信使歸來」事件系統）** 是遊戲離線回歸體驗的敘事層——當玩家重新開啟遊戲並經歷 `processOfflineProgress()` 時，系統將任務結算、候選池刷新等機械化事件包裝成一場「信使抵達、歸來冒險者彙報」的敘事事件，並可觸發一次性的「返回獎勵」（bonus commission）。

此系統零改動 `tick.ts` 的核心邏輯——僅在已有的 `processOfflineProgress()` 結果上附加獎勵判定與事件構築。

## 玩家幻想

玩家離開公會去睡覺、去上班、去度假，回到遊戲時看到的不是數字列表，而是一幕景象：**一名信使風塵僕僕地衝進公會大廳，手裡拿著冒險者們的成果彙報**。「你的人回來了！看看他們帶回什麼。」這個瞬間，離線期間發生的一切——任務成敗、錢進錢出、池子裡誰來誰走——都不再是抽象的 log 條目，而是一個**真實的歸來事件**。

獎勵機制進一步強化這種感覺：快點回來看看，信使還沒離開呢，有額外的返回獎勵。30 分鐘內回來的話，每筆成功任務多賺 5%；慢到沒趕上的話，就是錯過了。時間壓力來自於獎勵的稀缺性，而非懲罰。

## 詳細設計

### 1. 觸發條件

```
當 processOfflineProgress() 執行時，以下條件決定是否顯示「信使歸來」事件：

離線時長 >= OFFLINE_THRESHOLD_MS（10 分鐘）
  → 觸發「信使歸來」事件彙報（Offline Return Summary）
  → 檢查是否符合 Bonus Commission 資格

離線時長 < OFFLINE_THRESHOLD_MS
  → 跳過事件，直接應用結算結果到遊戲狀態
```

**常數定義**

```typescript
const OFFLINE_THRESHOLD_MS = 600_000            // 10 分鐘
const BONUS_COMMISSION_WINDOW_MS = 1_800_000    // 30 分鐘（1800 秒）
const BONUS_COMMISSION_RATE = 0.05              // 成功任務額外 5% 金幣
```

### 2. 「信使歸來」事件結構

當條件滿足時，系統組裝一個 `OfflineReturnEvent` 物件：

```typescript
interface OfflineReturnEvent {
  // 事件元數據
  eventId: string                               // UUID，用於去重與日誌追蹤
  offlineStartTimestamp: number                 // lastSavedAt（離線開始時間）
  offlineEndTimestamp: number                   // Date.now()（玩家歸來時間）
  offlineDurationMs: number                     // 離線時長（毫秒）
  
  // 結算批次
  resolutions: OfflineResolution[]              // 所有到期任務的結算結果
  candidatePoolRefreshed: boolean               // 是否觸發了候選池刷新
  
  // 獎勵資格
  bonusCommissionEligible: boolean              // 是否符合返回獎勵條件（見 §3）
  bonusCommissionActive: boolean                // true = 玩家在 30 分鐘內回來；false = 超時
  bonusCommissionAmount: number                 // 實際應得獎勵金額（為 0 如果不符合）
}

interface OfflineResolution {
  missionId: string
  missionName: string
  adventurerName: string
  outcome: OutcomeType                          // 'SUCCESS' | 'PYRRHIC' | 'FAILURE' | 'DEATH'
  goldDelta: number                             // 扣款金額（負數 = 扣，正數 = 收，已含佣金結算）
  bonusGoldFromBonus?: number                   // 本次獲得的返回獎勵額（僅成功時非零）
}
```

### 3. 返回獎勵（Bonus Commission）

**計算時機**：在 `processOfflineProgress()` 內，所有任務結算完成後。

**資格判定**

```
bonusCommissionEligible = 
  (至少有 1 筆任務結算為 SUCCESS 或 PYRRHIC) && 
  (今次離線滿足 OFFLINE_THRESHOLD_MS)

bonusCommissionActive = 
  bonusCommissionEligible && 
  (offlineEndTimestamp - offlineStartTimestamp <= BONUS_COMMISSION_WINDOW_MS)
```

若 `bonusCommissionActive = false`（超過 30 分鐘未歸），獎勵自動失效；無過期提示，玩家看到會理解。

**獎勵金額計算**

```typescript
if (bonusCommissionActive) {
  successAndPyrrhicMissions = resolutions.filter(
    r => r.outcome === 'SUCCESS' || r.outcome === 'PYRRHIC'
  )
  
  // 每筆成功任務的獲得金額（已扣除佣金後） × 5%
  bonusCommissionAmount = successAndPyrrhicMissions.reduce((sum, mission) => {
    earnedAmount = Math.floor(mission.preCollectedAmount × 0.20)  // 正常 20% 佣金
    bonusAmount  = Math.floor(earnedAmount × BONUS_COMMISSION_RATE)
    return sum + bonusAmount
  }, 0)
} else {
  bonusCommissionAmount = 0
}
```

**應用方式**

```
1. 先執行所有任務的正常結算（金幣加減，由 Commission Flow 負責）
2. 若 bonusCommissionActive = true，額外執行：
   gold += bonusCommissionAmount
   通知 Notification / Feedback：
   {
     type: 'gold_gained',
     message: `信使獎勵 +${bonusCommissionAmount}g（歸來獎勵）`,
     id: uuid()
   }
```

### 4. 與 processOfflineProgress() 的整合

現有 `tick.ts` 的 `processOfflineProgress()` 不改動；改動點僅在**呼叫方**（主介面殼或遊戲載入器）：

```typescript
// 舊流程（原封不動）
processOfflineProgress(tickState, activeMissions, callbacks)

// 新增：組裝 OfflineReturnEvent（可選的包裝層）
const offlineEvent = buildOfflineReturnEvent(
  tickState.lastSavedAt,
  Date.now(),
  activeMissions,           // 已結算後的列表
  resourceState.gold,       // 結算後的最終金幣值
)

// 若滿足離線門檻，顯示事件彙報
if (offlineEvent.offlineDurationMs >= OFFLINE_THRESHOLD_MS) {
  showOfflineReturnSummary(offlineEvent)
}
```

### 5. UI 呈現

**離線回歸摘要視窗（Offline Return Summary Overlay）**

- 獨立於常規訊息日誌，在頁面載入後立即顯示（非 Modal，玩家可點擊背景關閉）
- 構成：
  - **信使敘述**：「信使為您帶回 X 筆任務的結算彙報」（支持本地化）
  - **任務批次列表**：以表格或卡片列出每筆 OfflineResolution（冒險者名、任務名、結果、金幣變動）
  - **返回獎勵提示**：
    - 符合且未超時：`"返回獎勵：+Xg（限時獎勵）"` ← 綠色強調
    - 符合但超時（離線 > 30 分鐘）：`"錯過了返回獎勵（離線太久了）"` ← 灰色降級
    - 不符合（離線 < 10 分鐘 或 無成功任務）：不顯示獎勵區塊
  - **確認按鈕**：「好的，我了解了」→ 關閉視窗，遊戲進入正常主介面
- **位置**：畫面中央（z-index > main UI，< modal）

**與訊息日誌的分工**

| 場景 | 歸屬 |
|------|------|
| 玩家在線期間任務完成的即時提示 | 訊息日誌（Toast） |
| 玩家回歸時的批次結算彙報 | 離線回歸摘要（Overlay） |
| 操作反饋（接受委託、建設完成等） | 訊息日誌（Toast） |

### 6. 極端情況

**1. 無成功任務，全部失敗**

`bonusCommissionEligible = false`；不顯示獎勵區塊。「信使歸來」事件仍然顯示，但只列出虧損；玩家看到虧損清單後關閉視窗。

**2. 離線超過 30 分鐘但有成功任務**

`bonusCommissionActive = false`；獎勵金額為 0，UI 顯示「錯過了返回獎勵」（非懲罰語氣，而是「遺憾」）。玩家理解機制後下次會更快回來。

**3. 離線時長 < 10 分鐘**

不觸發離線回歸摘要；直接應用結算到遊戲狀態，進入常規 UI。

**4. processOfflineProgress() 執行期間崩潰**

TickState 已更新 `lastSavedAt`（於 processOfflineProgress 末端）；下次載入時重新計算即可。無特殊處理。

**5. 同筆任務重複結算（不應發生）**

`processOfflineProgress()` 靠 DispatchRecord.id 去重，本系統不額外去重。若發生，代表上游 bug；本系統如實反映。

## 公式彙總

```typescript
// 觸發條件
offlineDuration = Date.now() - tickState.lastSavedAt
triggerReturnEvent = offlineDuration >= OFFLINE_THRESHOLD_MS

// 獎勵資格與金額
bonusCommissionEligible = 
  (successCount > 0) && (offlineDuration >= OFFLINE_THRESHOLD_MS)

bonusCommissionActive = 
  bonusCommissionEligible && (offlineDuration <= BONUS_COMMISSION_WINDOW_MS)

bonusCommissionAmount = {
  if bonusCommissionActive:
    successMissions.reduce((sum, m) => {
      baseBonus = floor(floor(m.preCollectedAmount × 0.20) × 0.05)
      return sum + baseBonus
    }, 0)
  else:
    0
}
```

## 依賴關係

**上游（必要系統）**

| 系統 | 性質 | 用途 |
|------|------|------|
| **時間 / Tick（時間系統）** | 強依賴 | 提供 `offlineDuration` 計算基準 |
| **結果解決（結算系統）** | 強依賴 | 讀取任務結算結果（outcome 與 goldDelta） |
| **委託流（佣金循環）** | 強依賴 | 計算成功任務的獲得金額，決定獎勵基數 |
| **通知 / 反饋（通知系統）** | 弱依賴 | 推送「返回獎勵」Toast |

**下游（依賴此系統的系統）**

| 系統 | 性質 | 用途 |
|------|------|------|
| **主介面殼（主介面）** | 強依賴 | 渲染離線回歸摘要 Overlay |
| **存檔 / 讀檔（存檔系統）** | 弱依賴 | 可選：記錄最後一次離線回歸事件的 eventId（用於統計） |

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| `OFFLINE_THRESHOLD_MS` | 常數 | 600000（10 分鐘） | 300000 ~ 1800000 | 觸發「信使歸來」事件的最低離線時長；調低讓短暫離線也有儀式感，調高則只有長休後才顯示 |
| `BONUS_COMMISSION_WINDOW_MS` | 常數 | 1800000（30 分鐘） | 600000 ~ 3600000 | 返回獎勵的有效期；調低製造更多時間壓力，調高則降低獎勵稀缺性 |
| `BONUS_COMMISSION_RATE` | 常數 | 0.05（5%） | 0.02 ~ 0.15 | 獎勵相對於正常佣金的百分比；調高讓獎勵更吸引，調低則降低回歸動機 |

## 驗收標準

- [ ] 離線 ≥ 10 分鐘後回歸，`OfflineReturnEvent` 正確組裝，包含所有結算結果
- [ ] 無成功任務時，`bonusCommissionEligible = false`，獎勵金額為 0
- [ ] 有成功任務且 ≤ 30 分鐘回歸：獎勵金額 = Σ floor(floor(preCollected × 0.20) × 0.05) per success mission
- [ ] 有成功任務但 > 30 分鐘回歸：`bonusCommissionActive = false`，獎勵金額為 0，UI 顯示「錯過」提示（非懲罰語）
- [ ] 離線回歸摘要正確渲染，包含所有任務的結算結果與獎勵提示
- [ ] 玩家點擊確認按鈕後，視窗關閉，遊戲進入常規主介面（所有結算已應用於 GuildState）
- [ ] 返回獎勵正確加入 `gold`，通知推送「信使獎勵 +Xg」Toast
- [ ] 離線 < 10 分鐘時不顯示離線回歸摘要，直接進入主介面

## 待解問題

- 本地化文字表（「信使為您帶回...」等敘事文本）由本地化系統待補
- 離線回歸摘要的美術風格與動畫時序待美術主任與 UX 設計師協商
