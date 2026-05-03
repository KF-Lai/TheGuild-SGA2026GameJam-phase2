# Time / Tick System — 時間系統

> **文件狀態**：架構確立，細節待設計
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 1 — 離線 / 長期經營感
> **Jam 版範圍**：MVP，核心框架

## 概覽

**Time / Tick System（時間系統）** 是遊戲所有計時邏輯的核心驅動器。它負責：

1. **任務計時**：追蹤進行中任務的剩餘時間，時間到期後通知 Outcome Resolution 進行結算
2. **候選池刷新計時**：每 12 小時觸發 Adventurer Management 的候選池更新
3. **離線進度**：玩家重新開啟遊戲時，計算離線期間應完成的任務與刷新事件

本系統不處理任何遊戲邏輯——它只是時鐘，告訴其他系統「時間到了」。

## 玩家幻想

時間系統是放置型遊戲體驗的基礎：**玩家離開，世界繼續運轉**。回到遊戲時看到冒險者已歸來、有任務待結算、候選池有新面孔——這種「世界不等人」的感覺，讓公會感覺是真實存在的地方，而不是暫停的棋盤。

## 詳細設計

### 時鐘機制

遊戲以**真實時間**（wall-clock time）計時，使用 `Date.now()` 取得毫秒時間戳。

所有計時事件以「預計結束時間戳（`endTimestamp`）」而非「剩餘時間」記錄，確保離線進度正確計算。

### 任務計時

每筆進行中任務記錄：

```typescript
interface ActiveMissionTimer {
  missionId:    string;
  adventurerId: string;
  startTimestamp: number;  // Date.now() at dispatch
  endTimestamp:   number;  // startTimestamp + actualDuration（毫秒）
}
```

**Tick 頻率**：前台每 **10 秒**檢查一次到期任務（`setInterval`）。

**到期判斷**：
```
if Date.now() >= endTimestamp → 任務到期，通知 Outcome Resolution
```

### 候選池刷新計時

```typescript
candidatePoolNextRefreshAt: number;  // 下次刷新的時間戳
```

每次前台 Tick 同時檢查是否到達刷新時間。到期後：
1. 觸發 Adventurer Management 重建候選池
2. 更新 `candidatePoolNextRefreshAt = Date.now() + 43200000`（12 小時）

### 離線進度

玩家重新開啟遊戲時（頁面載入）：

```
offlineDuration = Date.now() - lastSaveTimestamp

對每筆 activeMission：
  if endTimestamp <= Date.now() → 標記為「待結算」

對候選池刷新：
  while candidatePoolNextRefreshAt <= Date.now():
    重建候選池（只保留最後一次，中間過期的不補算）
    candidatePoolNextRefreshAt += 43200000
```

> 離線結算在頁面載入時一次性處理，不播放動畫，直接顯示結果摘要。

### 與其他系統的互動

| 系統 | 方向 | 操作 |
|------|------|------|
| **Mission Dispatch（3）** | 讀取 | 接單時取得 `actualDuration`，寫入 `endTimestamp` |
| **Outcome Resolution（7）** | 觸發 | 任務到期後呼叫結算函式 |
| **Adventurer Management（2）** | 觸發 | 候選池到期後呼叫刷新函式 |
| **Save / Load（10）** | 讀寫 | 序列化所有時間戳（`endTimestamp`、`candidatePoolNextRefreshAt`、`lastSaveTimestamp`） |

## 公式

```
endTimestamp = startTimestamp + actualDuration × 60 × 1000
// actualDuration 單位為分鐘，轉換為毫秒

candidatePoolNextRefreshAt = lastRefreshTimestamp + REFRESH_INTERVAL_MS
// REFRESH_INTERVAL_MS = 43200000（12 小時）
```

## 極端情況

**1. 玩家長時間離線（數天）**

多筆任務同時到期。離線進度在頁面載入時批次處理，全部標記為待結算後一次顯示摘要。

**2. 候選池連續刷新多次**

只保留最後一批候選，中間過期的批次不補算。玩家只看到「最新的那批人」。

**3. 系統時間被調整（玩家改本地時鐘）**

Jam 版不防護時間作弊。Post-Jam 可考慮 server-side timestamp 驗證。

**4. `setInterval` 在背景 tab 被瀏覽器節流**

現代瀏覽器在背景 tab 會將 `setInterval` 最低節流至 1 秒甚至 1 分鐘。本系統以 `endTimestamp` 為準（不靠計數），重新前台化後立即補算，不影響結果正確性。

## 依賴關係

**上游依賴**：無（時間系統不依賴其他遊戲系統）

**下游依賴**

| 系統 | 依賴性質 |
|------|---------|
| **Mission Dispatch（3）** | 寫入計時資料 |
| **Outcome Resolution（7）** | 被觸發 |
| **Adventurer Management（2）** | 被觸發 |
| **Save / Load（10）** | 序列化時間戳 |

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| Tick 頻率 | `time-tick.ts` | 10000ms（10 秒） | 5000 ~ 30000ms | 調低提高任務到期的即時感；調高降低 CPU 佔用 |
| 候選池刷新間隔 | `time-tick.ts` | 43200000ms（12 小時） | 14400000 ~ 86400000ms | 控制新冒險者出現頻率 |

## 驗收標準

- [ ] 任務派遣後，`endTimestamp = startTimestamp + actualDuration × 60000`
- [ ] Tick 每 10 秒執行一次，正確找出到期任務
- [ ] 到期任務觸發 Outcome Resolution，且只觸發一次
- [ ] 離線重開後，所有到期任務被正確標記為待結算
- [ ] 候選池離線期間多次到期時，只保留最後一批
- [ ] Save / Load 序列化後，所有時間戳還原正確

## 待解問題

- 前台 Tick UI 動態（倒數顯示）的更新頻率待 Main UI Shell 設計時決定
