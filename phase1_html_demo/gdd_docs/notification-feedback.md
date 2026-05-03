# Notification / Feedback（通知回饋）



> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果
> **Jam 版範圍**：次要，處理即時通知與里程碑顯示（離線批次結算由 Main UI Shell 的 Settlement Overlay 負責）

## 概覽

**Notification / Feedback（通知回饋）** 是遊戲的即時反饋層——在玩家執行操作或系統達成條件時，以**自動消失的 Toast 通知**呈現輕量文字提示，讓玩家知道「剛剛發生了什麼」。

Jam 版採用**單一 Toast 佇列**：所有通知（里程碑、操作確認、拒絕提示）均進入同一個佇列，依序顯示，每則通知在畫面停留。

本系統**不負責離線結算的批次顯示**——那由 Main UI Shell 的 Settlement Overlay 負責。本系統只處理玩家主動操作期間的即時回饋與 Progression/Unlock 推送的里程碑事件。

無持久化需求（通知消失後不存檔）。

## 玩家幻想

遊戲的決策都有重量，不是因為結果巨大，而是因為你**親眼看見**了。

你推薦一份委託，Aria 接下了——Toast 一閃，那個瞬間你知道了。你的公會第一次欠債，帳本翻紅，一行文字靜靜提醒你：這件事發生過。第一次有人死在你的委託上，里程碑出現的那一刻，公會不再只是數字組成的東西。

玩家不需要盯著畫面等通知——通知自己找上門，說完就走。不打擾，但**不沉默**。

## 詳細設計

### 1. 通知類型定義

```typescript
type NotificationType =
  | 'milestone'          // 里程碑（title + body，Progression/Unlock 推送）
  | 'dispatch_accepted'  // 冒險者接受推薦（Mission Dispatch）
  | 'dispatch_refused'   // 冒險者拒絕推薦（Mission Dispatch）
  | 'mission_complete'   // 任務完成即時提示（Outcome Resolution）
  | 'mission_failed'     // 任務失敗即時提示（Outcome Resolution）
  | 'gold_gained'        // 金幣增加（Commission Flow / Guild Building）
  | 'gold_lost'          // 金幣減少（Commission Flow / Guild Building）
  | 'reputation_gained'  // 聲望增加（Resource Management）
  | 'reputation_lost'    // 聲望減少（Resource Management）
  | 'operation_error'    // 操作失敗（任意系統，金幣不足、名冊已滿等）

interface NotificationPayload {
  type:    NotificationType
  message: string           // 主顯示文字（里程碑為 title；其餘為單行文字）
  detail?: string           // 里程碑的 body 文字（其餘為 undefined）
  id:      string           // 唯一 ID（UUID），用於移除特定 Toast
}
```

---

### 2. Toast 佇列邏輯

**顯示上限**：同時最多 **3 則** Toast 顯示於畫面（最新在最上）。

**插入規則**（優先級）：


| 類型          | 插入方式                        |
| ----------- | --------------------------- |
| `milestone` | **插隊至佇列首位**（立即顯示或插入顯示中的最上方） |
| 其他所有類型      | 追加至佇列尾端                     |


**顯示時間**：


| 類型                                                                    | 停留時間  |
| --------------------------------------------------------------------- | ----- |
| `milestone`                                                           | 5 秒   |
| `mission_complete` / `mission_failed`                                 | 3 秒   |
| `dispatch_accepted` / `dispatch_refused`                              | 2 秒   |
| `gold_gained` / `gold_lost` / `reputation_gained` / `reputation_lost` | 2 秒   |
| `operation_error`                                                     | 2.5 秒 |


**顯示位置**：主介面右下角（Jam 版）；後期可替換為畫面任意角落。

---

### 3. 推送介面（`notification.push()`）

```typescript
interface NotificationService {
  // 推送一則通知；里程碑由 Progression/Unlock 自動傳入 detail
  push(payload: NotificationPayload): void

  // Progression/Unlock 初始化後呼叫，清空啟動佇列
  flushStartupQueue(): void
}
```

呼叫範例：

```typescript
// 里程碑（Progression/Unlock 呼叫）
notification.push({
  type: 'milestone',
  message: MILESTONE_STRINGS[id].title,
  detail:  MILESTONE_STRINGS[id].body,
  id: uuid()
})

// 操作反饋（Mission Dispatch 呼叫）
notification.push({
  type: 'dispatch_accepted',
  message: `${adventurer.name} 接受了「${mission.title}」`,
  id: uuid()
})

// 資源變化（Commission Flow 呼叫）
notification.push({
  type: 'gold_gained',
  message: `+${amount}g`,
  id: uuid()
})
```

---

### 4. 文字表

操作類通知的文字由呼叫方動態生成（含冒險者名、任務名、金額等變數），本系統**不維護靜態文字**。`operation_error` 類的固定錯誤文字由呼叫方提供，可引用各自系統的 strings 檔。

---

### 5. 與 Settlement Overlay 的分工


| 場景                 | 負責系統                                                                |
| ------------------ | ------------------------------------------------------------------- |
| 玩家離線後歸來，多筆任務結算     | Main UI Shell：Settlement Overlay（批次顯示）                              |
| 玩家操作期間，單筆任務即時完成    | Notification / Feedback：`mission_complete` / `mission_failed` Toast |
| 兩者同時（任務剛完成，玩家立刻回來） | Settlement Overlay 優先；Toast 不重複觸發（由 Outcome Resolution 決定推送目標）      |


## 公式

### Toast 停留時間常數

```typescript
// 單位：毫秒
const TOAST_DURATION: Record<NotificationType, number> = {
  milestone:          5000,
  mission_complete:   3000,
  mission_failed:     3000,
  dispatch_accepted:  2000,
  dispatch_refused:   2000,
  gold_gained:        2000,
  gold_lost:          2000,
  reputation_gained:  2000,
  reputation_lost:    2000,
  operation_error:    2500,
}

const MAX_VISIBLE_TOASTS = 3
```

### 佇列管理邏輯

```
push(payload):
  if payload.type === 'milestone':
    queue.unshift(payload)       // 里程碑插隊首位
  else:
    queue.push(payload)          // 其他追加尾端
  tryDisplay()

tryDisplay():
  while visibleToasts.length < MAX_VISIBLE_TOASTS && queue.length > 0:
    toast = queue.shift()
    show(toast)
    setTimeout(() => dismiss(toast.id), TOAST_DURATION[toast.type])

dismiss(id):
  remove toast from visibleToasts
  tryDisplay()                   // 有空位時自動顯示佇列中的下一則
```

## 極端情況


| #   | 情況                                                           | 處理方式                                                                                |
| --- | ------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| 1   | **佇列爆炸**（短時間推送大量通知，如離線後觸發多個里程碑）                              | 無上限佇列，依序排隊；里程碑依舊插隊首位；玩家等待消耗，不截斷；若 `queue.length > 20` 可捨棄最舊的非里程碑通知                  |
| 2   | **在 Settlement Overlay 顯示期間推送 Toast**                        | Settlement Overlay 優先（覆蓋主介面），Toast 仍推入佇列；Overlay 確認後 Toast 正常顯示                     |
| 3   | **頁面切換／重整**                                                  | 無持久化，通知佇列清空；Toast 本就是即時反饋，屬設計意圖                                                     |
| 4   | **多個里程碑同時推送**（如 `first_veteran` + `first_s_adventurer` 連續觸發） | 兩則均插隊，依推送順序決定先後；停留時間各自獨立計算                                                          |
| 5   | **Notification 未初始化時 Progression/Unlock 推送事件**               | Progression/Unlock 已實作 `milestoneQueue` 啟動佇列；本系統初始化完成後呼叫 `flushStartupQueue()` 統一推入 |
| 6   | `**gold_gained` / `gold_lost` 高頻推送**（同一 tick 多次結算）           | 正常排隊消耗；Jam 版不合併；Post-Jam 可考慮「+多筆合併」顯示                                               |


## 依賴關係

**上游（推送方）**


| 系統                       | 依賴性質 | 推送的通知類型                                                          |
| ------------------------ | ---- | ---------------------------------------------------------------- |
| Progression / Unlock（8）  | 弱依賴  | `milestone`（透過 `notification.push()` 推送）                         |
| Mission Dispatch（3）      | 弱依賴  | `dispatch_accepted`、`dispatch_refused`                           |
| Outcome Resolution（7）    | 弱依賴  | `mission_complete`、`mission_failed`（即時通知，不重複 Settlement Overlay） |
| Commission Flow（15）      | 弱依賴  | `gold_gained`、`gold_lost`                                        |
| Guild Building（5）        | 弱依賴  | `gold_lost`（建設扣款）、`operation_error`（金幣不足）                        |
| Resource Management（4）   | 弱依賴  | `reputation_gained`、`reputation_lost`                            |
| Adventurer Management（2） | 弱依賴  | `gold_lost`（老手邀請費用）、`operation_error`（名冊已滿）                      |
| Guild Core（1）            | 弱依賴  | `operation_error`（聲望不足無法升等）                                      |


**下游（消費方）**


| 系統                | 依賴性質 | 說明                                    |
| ----------------- | ---- | ------------------------------------- |
| Main UI Shell（11） | 強依賴  | Toast 渲染至主介面右下角 Toast 區域；負責 DOM 更新與動畫 |


**持久化**：無 — 通知消失後不存檔。

**依賴方向圖**

```
Progression/Unlock（8）
Mission Dispatch（3）
Outcome Resolution（7）      ──[push()]──▶  Notification / Feedback（16）  ──▶  Main UI Shell（11）
Commission Flow（15）
Guild Building（5）
Resource Management（4）
Adventurer Management（2）
Guild Core（1）
```

## 調整旋鈕


| 旋鈕                   | 位置                         | 目前值  | 說明                                  |
| -------------------- | -------------------------- | ---- | ----------------------------------- |
| `TOAST_DURATION`     | `notification-feedback.ts` | 見公式節 | 各類型通知的停留時間（毫秒）；調高讓玩家有更多時間閱讀，調低降低打擾感 |
| `MAX_VISIBLE_TOASTS` | `notification-feedback.ts` | 3    | 同時可見通知上限；調高資訊密度高但畫面雜亂，調低則佇列積壓更明顯    |
| 佇列爆炸截斷閾值             | `notification-feedback.ts` | 20   | `queue.length > 20` 時開始捨棄最舊的非里程碑通知  |


## 驗收標準

**基本功能驗收**

- 呼叫 `push({ type: 'dispatch_accepted', ... })` 後，Toast 出現在右下角，2 秒後自動消失
- 呼叫 `push({ type: 'milestone', ... })` 後，Toast 顯示 title + body，5 秒後自動消失
- 里程碑 Toast 停留時間（5 秒）與操作 Toast 停留時間（2 秒）相互獨立

**佇列邏輯驗收**

- 同時推送 5 則通知，畫面最多顯示 3 則，其餘排隊等待
- 任意 Toast 消失後，佇列中的下一則立即顯示
- 推送 `operation_error` 後推送 `milestone`，`milestone` 顯示在 `operation_error` 之前（插隊驗證）

**里程碑整合驗收**

- Progression/Unlock 觸發 `first_death` 後，Toast 顯示 `MILESTONE_STRINGS.first_death.title`（標題）與 `body`（說明文字）
- 連續觸發 `first_veteran` 與 `first_s_adventurer`，兩則 Toast 依序顯示，互不干擾

**啟動佇列驗收**

- 在 Notification / Feedback 初始化前觸發里程碑，初始化後呼叫 `flushStartupQueue()`，里程碑正確顯示

**Settlement Overlay 分工驗收**

- Settlement Overlay 顯示期間推送 Toast，Toast 推入佇列，Overlay 確認後正常顯示
- 任務透過 Settlement Overlay 結算時，不重複觸發 `mission_complete` Toast（由 Outcome Resolution 負責區分推送目標）

**邊界驗收**

- 佇列超過 20 則非里程碑通知時，最舊的非里程碑通知被捨棄；里程碑通知不被捨棄
- 頁面重整後通知佇列清空，不恢復任何未顯示的通知

