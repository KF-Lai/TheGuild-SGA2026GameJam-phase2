# Resource Management — 資源管理

> **文件狀態**：已設計
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **Jam 版範圍**：MVP

## 概覽

**Resource Management（資源管理）** 負責管理公會的兩種核心資源：**金幣**與**聲望**。它提供資源的讀寫介面，確保數值在合法範圍內，並將聲望數值轉換為可顯示的文字標籤。

本系統不決定資源如何被使用——那是 Mission Dispatch、Adventurer Management、Outcome Resolution 等系統的工作。Resource Management 只負責「資源的狀態與規則」。

Post-Jam 構想中的材料 / 裝備循環、派系信任度等資源另見 `design/post-jam-concepts.md`。

## 玩家幻想

玩家不直接看到「金幣: 320 / 聲望: 47」這樣的數字——他們看到的是「金庫還算充裕」和「聲望漸隆」。聲望以文字標籤呈現，讓公會在城裡的地位感覺像是真實的社會評價，而不是遊戲數值。

金幣是行動資本——多了有底氣，少了要謹慎。聲望是長線積累——慢慢建立，一次失誤可以重傷。

## 詳細設計

### 資料結構

```typescript
interface ResourceState {
  gold:       number;  // 非負整數，無上限
  reputation: number;  // 整數，範圍 -100 ~ 100
}
```

### 聲望等級標籤

聲望以 10 個文字標籤顯示，從正面到負面：


| 分數範圍       | 標籤   |
| ---------- | ---- |
| 81 ~ 100   | 名震四方 |
| 61 ~ 80    | 名聲遠播 |
| 41 ~ 60    | 聲望漸隆 |
| 21 ~ 40    | 初露鋒芒 |
| 0 ~ 20     | 默默無名 |
| -1 ~ -10   | 名聲不佳 |
| -11 ~ -30  | 惡評漸起 |
| -31 ~ -55  | 惡名外傳 |
| -56 ~ -80  | 聲名狼藉 |
| -81 ~ -100 | 臭名昭著 |


**起始聲望**：0（「默默無名」）

### 聲望變動表（REPUTATION_DELTA）

每筆任務結算時，依難度套用聲望變動：


| 難度  | 成功 +Δ | 失敗 −Δ |
| --- | ----- | ----- |
| F   | +1    | -8    |
| E   | +1    | -7    |
| D   | +3    | -5    |
| C   | +4    | -4    |
| B   | +5    | -3    |
| A   | +10   | -2    |
| S   | +13   | -1    |
| SS  | +16   | -5    |
| SSS | +20   | -10   |


> 高難度任務失敗的聲望懲罰較低（A/S 難度），因為「嘗試高難度」本身已帶有積極意義。SS/SSS 因期待過高，失敗懲罰略高。

### 與其他系統的互動


| 系統                           | 方向  | 操作                  |
| ---------------------------- | --- | ------------------- |
| **Adventurer Management（2）** | 寫入  | 邀請老手冒險者時扣除金幣        |
| **Outcome Resolution（7）**    | 寫入  | 任務結算後寫入金幣與聲望變動      |
| **Mission Dispatch（3）**      | 讀取  | 讀取聲望分數，決定高難度任務出現機率  |
| **Save / Load（10）**          | 讀寫  | 序列化 `ResourceState` |


## 公式

### 金幣更新

```typescript
function updateGold(state: ResourceState, delta: number): ResourceState {
  return {
    ...state,
    gold: Math.max(0, state.gold + delta)
  };
}
```

> 金幣不可為負（最低為 0），無上限。

### 聲望更新

```typescript
function updateReputation(state: ResourceState, delta: number): ResourceState {
  return {
    ...state,
    reputation: Math.max(-100, Math.min(100, state.reputation + delta))
  };
}
```

### 聲望標籤查詢

```typescript
function getReputationLabel(reputation: number): string {
  if (reputation >= 81)  return '名震四方';
  if (reputation >= 61)  return '名聲遠播';
  if (reputation >= 41)  return '聲望漸隆';
  if (reputation >= 21)  return '初露鋒芒';
  if (reputation >= 0)   return '默默無名';
  if (reputation >= -10) return '名聲不佳';
  if (reputation >= -30) return '惡評漸起';
  if (reputation >= -55) return '惡名外傳';
  if (reputation >= -80) return '聲名狼藉';
  return '臭名昭著';
}
```

## 極端情況

**1. 金幣扣減後低於 0**

`updateGold` 以 `Math.max(0, ...)` 保護，結果為 0 而非負數。

**2. 聲望超出 ±100 範圍**

`updateReputation` 以 `clamp(-100, 100)` 保護，超出部分無效。長期在 100 的公會繼續成功也不會變更標籤。

**3. 聲望恰好為 0**

分類為「默默無名」（0 ~ 20 區間），屬於非負區間的最低點，是遊戲起始狀態。

**4. 同一 tick 多筆任務結算**

每筆任務結算獨立呼叫 `updateGold` / `updateReputation`，累加正確。

## 依賴關係

**上游依賴**：無

**下游依賴**


| 系統                           | 依賴性質      |
| ---------------------------- | --------- |
| **Adventurer Management（2）** | 邀請費用查詢與扣款 |
| **Outcome Resolution（7）**    | 任務結算寫入    |
| **Mission Dispatch（3）**      | 聲望影響任務池   |
| **Save / Load（10）**          | 序列化       |


## 調整旋鈕


| 旋鈕                     | 位置                       | 目前值 | 安全範圍 | 說明           |
| ---------------------- | ------------------------ | --- | ---- | ------------ |
| `REPUTATION_DELTA` 成功值 | `resource-management.ts` | 見上表 | ±50% | 調整聲望上升速度     |
| `REPUTATION_DELTA` 失敗值 | `resource-management.ts` | 見上表 | ±50% | 調整失敗懲罰力度     |
| 聲望標籤閾值                 | `resource-management.ts` | 見上表 | 可微調  | 調整各標籤的分數範圍感受 |


## 驗收標準

- 初始狀態：`gold = 0`，`reputation = 0`，標籤為「默默無名」
- `updateGold` 扣減超過餘額時，結果為 0 而非負數
- `updateReputation` 超出 ±100 時，結果 clamp 至邊界
- `getReputationLabel(0)` 回傳「默默無名」
- `getReputationLabel(-1)` 回傳「名聲不佳」
- `getReputationLabel(100)` 回傳「名震四方」
- `getReputationLabel(-100)` 回傳「臭名昭著」

## 待解問題

[無]