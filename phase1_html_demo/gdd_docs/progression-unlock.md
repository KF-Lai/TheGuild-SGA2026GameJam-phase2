# Progression / Unlock（進程解鎖）
<!--
  還原來源：Cursor agent transcript 765098c1-4ce5-4480-af28-321deae1f4d2（Write/StrReplace 重播）。
  若與 src/ 衝突，依專案約定以對話紀錄與本 GDD 為準。
-->


> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果
> **Jam 版範圍**：次要，管理解鎖事件的觸發與記錄

## 概覽

**Progression / Unlock（進程解鎖）** 是整個遊戲的「里程碑記錄者」——它不主動做任何事，而是**監聽其他系統的狀態變化**，在特定條件達成時觸發一次性的解鎖事件，並通知 Main UI Shell 顯示里程碑訊息。

Jam 版的解鎖事件清單是固定的：每個事件有一個觸發條件（可能是 guildLevel 升級、世界危險度變化、首次完成某種任務、或達到聲望門檻），觸發後標記為「已發生」，不再重複觸發。

本系統的輸出只有兩種：**通知顯示**（傳遞至 Notification / Feedback）與**狀態記錄**（存入存檔）。它不產生任何實際的遊戲數值效果——那些效果由各自的系統自行管理。

## 玩家幻想

公會的歷史不是數字，是故事。第一次有人死在你的委託上，第一次接到傳說級的任務，第一次讓世界認識你的公會——這些時刻只發生一次。

玩家不需要主動追蹤「我還差多少才能解鎖什麼」——里程碑在它該發生的時候自然出現，像是一封來自公會歷史的短箋，告訴你：這一刻值得記住。

## 詳細設計

### 1. 里程碑事件清單（24 個）

| ID | 事件名稱 | 觸發條件 | 觸發者 |
|----|---------|---------|-------|
| `first_dispatch` | 第一份委託 | 首次建立 `DispatchRecord` | Mission Dispatch |
| `first_success` | 第一次成功 | 首次任務結果為 SUCCESS | Outcome Resolution |
| `first_failure` | 我們辜負了他 | 首次任務結果為 FAILURE | Outcome Resolution |
| `first_pyrrhic` | 雖勝猶悲 | 首次任務結果為 PYRRHIC | Outcome Resolution |
| `first_death` | 公會的第一個犧牲 | 首次有冒險者死亡 | Outcome Resolution |
| `first_veteran` | 不只是過客 | 首次邀請老手冒險者（rank D+）| Adventurer Management |
| `first_s_adventurer` | 傳奇同行 | 首次 S 階冒險者加入名冊 | Adventurer Management |
| `first_dismissed` | 那扇門關上了 | 首次使用審查處開除冒險者 | Adventurer Management |
| `first_building` | 公會開始成形 | 首次完成任意建設 | Guild Building |
| `first_debt` | 公會第一次欠債 | `gold` 首次低於 0 | Commission Flow |
| `survived_debt` | 從深淵爬回來 | 金幣從負值回正（首次從 < 0 升至 ≥ 0）| Commission Flow |
| `first_ss_attempt` | 那是個賭注 | 首次向冒險者推薦 SS/SSS 任務 | Mission Dispatch |
| `all_slots_active` | 公會全力運轉 | 首次所有任務槽同時佔滿 | Mission Dispatch |
| `missions_10` | 十份委託 | 累計完成（任何結果）10 份委託 | Outcome Resolution |
| `missions_30` | 三十份委託 | 累計完成 30 份委託 | Outcome Resolution |
| `guild_lv2` | 旅人開始知道這裡 | guildLevel 升至 2 | Guild Core |
| `guild_lv3` | 刀劍在此立誓 | guildLevel 升至 3 | Guild Core |
| `guild_lv4` | 英雄們選擇了這裡 | guildLevel 升至 4 | Guild Core |
| `guild_lv5` | 傳說尚未終結 | guildLevel 升至 5 | Guild Core |
| `reputation_100` | 傳奇之名 | `reputation` 首次達到 100 | Resource Management |
| `world_danger_d` | 世界開始動盪 | 危險度升至 D | World Danger System |
| `world_danger_c` | 暗湧已至 | 危險度升至 C | World Danger System |
| `world_danger_b` | 危局降臨 | 危險度升至 B | World Danger System |
| `world_danger_a` | 末世已來 | 危險度升至 A | World Danger System |

---

### 2. 里程碑文字表（`milestone-strings.ts`）

```typescript
// milestone-strings.ts — 所有里程碑顯示文字，邏輯層不出現裸字串
export const MILESTONE_STRINGS: Record<MilestoneId, { title: string; body: string }> = {
  first_dispatch:     { title: "第一份委託",         body: "公會的第一份委託已派出。無名者之所，也許真的有機會在這個世界留下名字。" },
  first_success:      { title: "第一次成功",         body: "他們回來了。帶著報酬，帶著塵土，帶著活著的證明。" },
  first_failure:      { title: "我們辜負了他",       body: "失敗的委託，是公會欠下的一筆帳。不是金幣，是信任。" },
  first_pyrrhic:      { title: "雖勝猶悲",           body: "任務完成了。但代價是什麼，只有你知道。" },
  first_death:        { title: "公會的第一個犧牲",   body: "他的名字應該被記住。在這份委託之後，這裡再也不只是一間辦公室。" },
  first_veteran:      { title: "不只是過客",         body: "有人選擇了這裡，不是因為沒有其他去處，而是因為這裡值得。" },
  first_s_adventurer: { title: "傳奇同行",           body: "S 階的冒險者不輕易投效任何公會。你的名聲，終於傳到了他們的耳裡。" },
  first_dismissed:    { title: "那扇門關上了",       body: "不是每段緣分都有好的結局。公會長的職責，有時候是知道什麼時候說再見。" },
  first_building:     { title: "公會開始成形",       body: "那塊空著的牆壁，現在有東西了。" },
  first_debt:         { title: "公會第一次欠債",     body: "帳本上出現了紅字。這不是終點，但你需要開始小心了。" },
  survived_debt:      { title: "從深淵爬回來",       body: "帳已還清。這段記憶不會消失，但公會還在。" },
  first_ss_attempt:   { title: "那是個賭注",         body: "沒有人說這是個好主意。但你還是推薦了。" },
  all_slots_active:   { title: "公會全力運轉",       body: "所有任務槽都有人。這個公會，已經不再是當初那間漏風的辦公室了。" },
  missions_10:        { title: "十份委託",           body: "十份委託，有成有敗，有生有死。公會的歷史，正在一頁一頁寫下。" },
  missions_30:        { title: "三十份委託",         body: "三十份委託完成。這個世界漸漸知道你的公會存在。" },
  guild_lv2:          { title: "旅人開始知道這裡",   body: "公會升等了。更多的冒險者聽說了這個名字，更多的委託主找上門來。" },
  guild_lv3:          { title: "刀劍在此立誓",       body: "這裡已不只是落腳處。是歸屬。" },
  guild_lv4:          { title: "英雄們選擇了這裡",   body: "名聲傳遍了王國。英雄們知道，這裡值得他們的刀。" },
  guild_lv5:          { title: "傳說尚未終結",       body: "你的公會已是傳奇。但世界還在繼續惡化，傳奇也必須繼續。" },
  reputation_100:     { title: "傳奇之名",           body: "聲望達到了傳奇的門檻。詩人已在為你的公會立傳。" },
  world_danger_d:     { title: "世界開始動盪",       body: "邊境有騷亂的消息傳來。委託的內容，開始不再只是打雜。" },
  world_danger_c:     { title: "暗湧已至",           body: "城鎮開始戒備。那些在暗處的東西，已經不再隱藏。" },
  world_danger_b:     { title: "危局降臨",           body: "大規模的衝突爆發了。你的公會，是少數還在堅持接委託的地方。" },
  world_danger_a:     { title: "末世已來",           body: "秩序已崩。每一次推薦，都可能是你最後一次看到那個人。" },
}

export type MilestoneId = keyof typeof MILESTONE_STRINGS
```

---

### 3. 資料結構

```typescript
interface ProgressionState {
  triggered:     MilestoneId[]   // 已觸發的事件 ID（Set 於執行期，序列化為陣列）
  totalMissions: number           // 累計完成委託數（含 SUCCESS / FAILURE / PYRRHIC / DEATH）
}
```

---

### 4. 觸發機制

各系統在條件達成時呼叫 `progression.check(id)`：

```typescript
function check(id: MilestoneId): void {
  if (triggeredSet.has(id)) return              // 冪等：已觸發則忽略
  triggeredSet.add(id)
  notification.push({ type: 'milestone', id })  // 通知 Notification/Feedback 顯示
}
```

累計委託數的特殊處理：

```typescript
// Outcome Resolution 每次結算後呼叫
function onMissionResolved(): void {
  state.totalMissions++
  if (state.totalMissions === 10) progression.check('missions_10')
  if (state.totalMissions === 30) progression.check('missions_30')
}
```

## 公式

### 觸發條件摘要

```typescript
// 各觸發點的條件判斷（由觸發方系統執行）

// missions_10 / missions_30
progression.check('missions_10')  // totalMissions 達到 10 時
progression.check('missions_30')  // totalMissions 達到 30 時

// all_slots_active
if (activeMissions.length >= MAX_MISSIONS[guildLevel]) {
  progression.check('all_slots_active')
}

// survived_debt：需要前一個狀態是 gold < 0，現在 gold >= 0
if (prevGold < 0 && newGold >= 0) {
  progression.check('survived_debt')
}
```

> 其他事件條件為點位觸發（首次發生即呼叫），由各自系統在對應時機直接呼叫 `progression.check(id)`，無需額外公式。

---

## 極端情況

| # | 情況 | 說明 | 處理方式 |
|---|------|------|---------|
| 1 | **同一事件觸發兩次** | 例如任務失敗觸發兩次 `first_failure` | `check()` 冪等：`triggeredSet.has(id)` 時直接 return |
| 2 | **存檔損毀，`triggered` 遺失** | 重新載入後歷史里程碑消失 | 存檔驗證 fallback：`triggered = []`，重新開始記錄；不重播已消失的里程碑 |
| 3 | **`totalMissions` 回滾**（存檔損毀）| 計數從較大值回到較小值 | `missions_10 / missions_30` 已在 `triggered` 中記錄，不會重複觸發；計數不影響已有里程碑 |
| 4 | **新增里程碑 ID（版本更新）**| 舊存檔沒有新事件的記錄 | 舊存檔 `triggered` 中沒有新 ID → 可正常觸發，符合預期 |
| 5 | **Notification/Feedback 未初始化時觸發** | 初始化順序問題 | `notification` 為 null 時佇列事件，待 Notification/Feedback 初始化後統一推送 |

---

## 依賴關係

| 系統 | 方向 | 類型 | 說明 |
|------|------|------|------|
| Mission Dispatch（3） | 上游觸發 | 弱 | 首次派遣、首次 SS/SSS 推薦、任務槽全滿時呼叫 `check()` |
| Outcome Resolution（7） | 上游觸發 | 弱 | 首次各結果類型、冒險者死亡、累計委託數時呼叫 `check()` |
| Adventurer Management（2） | 上游觸發 | 弱 | 首次老手、首次 S 階、首次開除時呼叫 `check()` |
| Guild Building（5） | 上游觸發 | 弱 | 首次建設完成時呼叫 `check()` |
| Commission Flow（15） | 上游觸發 | 弱 | 首次欠債、金幣回正時呼叫 `check()` |
| Guild Core（1） | 上游觸發 | 弱 | 每次升等時呼叫 `check()` |
| World Danger System（12） | 上游觸發 | 弱 | 每次危險度升級時呼叫 `check()` |
| Resource Management（4） | 上游觸發 | 弱 | 聲望達 100 時呼叫 `check()` |
| Notification / Feedback（16） | 下游輸出 | 弱 | 接收 `{ type: 'milestone', id }` 並決定顯示方式 |
| Save / Load（10） | 持久化 | 強 | `ProgressionState`（`triggered[]` + `totalMissions`）納入存檔 |

> **所有上游依賴均為弱依賴**：Progression 系統只暴露一個 `check(id)` 函式，觸發方不需要了解 Progression 內部邏輯。

---

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 說明 |
|------|------|--------|------|
| `MILESTONE_STRINGS` | `milestone-strings.ts` | 見詳細設計節 | 修改任意里程碑的標題與說明文字，無需改動邏輯 |
| `missions_10 / missions_30` 門檻 | `progression-unlock.ts` | 10 / 30 | 調整累計委託里程碑的觸發節點 |
| 新增 / 移除事件 | `milestone-strings.ts` + 觸發方 | 24 個 | 新增事件只需在文字表新增一筆，並在對應系統加入 `check()` 呼叫 |

---

## 驗收標準

- [ ] 首次建立 `DispatchRecord` 後，`first_dispatch` 觸發，`triggered` 包含此 ID
- [ ] 同一事件的 `check()` 被呼叫兩次，第二次不觸發通知
- [ ] `totalMissions` 累計到 10，`missions_10` 觸發；累計到 30，`missions_30` 觸發
- [ ] `guildLevel` 升至 2、3、4、5 時，對應 `guild_lv2~5` 各自觸發一次
- [ ] 世界危險度升至 D/C/B/A 時，對應 `world_danger_*` 各自觸發一次
- [ ] `gold` 首次低於 0 時，`first_debt` 觸發
- [ ] `gold` 從負值回正時，`survived_debt` 觸發；此後即使再次回正也不重複觸發
- [ ] `ProgressionState` 存檔後重新載入，`triggered` 正確還原，不重播已觸發事件
- [ ] `MILESTONE_STRINGS` 中每個 ID 均有對應的 `title` 與 `body` 字串
