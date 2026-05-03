# Guild Core（公會核心）

> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果；支柱 1 — 離線進行，回來收結果
> **Jam 版範圍**：次要，公會等級與聲望門檻

## 概覽

**Guild Core（公會核心）** 是整個遊戲最高層的進程容器——它不做任何計算，只追蹤**公會的等級**與**聲望值**，並根據這兩個數值決定「公會目前能做什麼」。

Jam 版的公會等級從 1 開始，分為 5 個等級。等級不是靠時間自動升的，而是**聲望達到閾值後手動（或自動）升級**。等級越高，可以接的委託上限越多、可以招募的冒險者上限越多，也是未來 Guild Building（公會建設）系統的解鎖前提。

聲望（`reputation`）已在 Resource Management 定義為有增減的數值；Guild Core 的職責是將這個數字**轉換為等級門檻判斷**，以及在升級時觸發對應的解鎖事件。

## 玩家幻想

你不是一出發就是大公會。你是從一間漏風的辦公室開始的，只有幾個願意接低階委託的新人，委託板上全是 F 等級的打雜工作。

但隨著任務一件件完成，那些在酒館裡聽說你公會名字的人，開始覺得你「值得信賴」。聲望不是數字，是口碑——等到口碑夠了，更難的委託才願意交給你，更有本事的冒險者才願意考慮你。

公會升等是一個里程碑，不是自動發生的獎勵。你知道自己快升了，你選擇什麼時候確認它。

## 詳細設計

### 1. 公會等級表


| 等級   | 公會稱號     | 聲望升級門檻 | 最大同時任務 | 名冊容量上限 | 可接委託難度上限 |
| ---- | -------- | ------ | ------ | ------ | -------- |
| Lv 1 | 無名者之所    | —      | 2      | 4      | D        |
| Lv 2 | 旅人的落腳處   | ≥ 30   | 3      | 6      | C        |
| Lv 3 | 刀劍立誓之地   | ≥ 60   | 4      | 9      | B        |
| Lv 4 | 英雄歸來之所   | ≥ 100  | 5      | 12     | A        |
| Lv 5 | 傳說尚未終結之處 | ≥ 150  | 6      | 15     | SSS      |


> 聲望門檻與 Main UI Shell 的聲望形容詞對照表刻意對齊，讓玩家從 UI 文字即可感知「我快升等了」。

---

### 2. 手動升級流程

```
1. 玩家回到主介面
2. 若 reputation >= UPGRADE_THRESHOLD[currentLevel] → Main UI Shell 顯示「公會可升等！」提示
3. 玩家點擊底部工具列的「升等」按鈕（平時隱藏，達標時顯示）
4. 確認對話框：顯示新等級帶來的各項容量變化
5. 玩家確認 → guildLevel++，顯示升等演出文字
```

---

### 3. 資料結構

```typescript
interface GuildCoreState {
  guildLevel: number   // 1 ~ 5
}

// 依賴 ResourceState.reputation（定義於 Resource Management）
```

---

### 4. 限制的執行者

Guild Core 只**儲存**等級；實際限制的執行由各子系統負責：


| 限制項目     | 執行者                   | 如何使用                                                                       |
| -------- | --------------------- | -------------------------------------------------------------------------- |
| 最大同時任務數  | Mission Dispatch      | 建立 `DispatchRecord` 前確認 `activeMissions.length < MAX_MISSIONS[guildLevel]` |
| 名冊容量上限   | Adventurer Management | 招募前確認 `roster.length < effectiveMaxRoster()`；數值來自 `MAX_ROSTER_BY_GUILD_LEVEL` |
| 可接委託難度上限 | Mission Dispatch      | 過濾委託池，超過上限難度的委託不顯示於委託板                                                     |


## 公式

### 升級門檻常數

```typescript
const UPGRADE_THRESHOLD: Record<number, number> = {
  1: 30,       // Lv1 → Lv2
  2: 60,       // Lv2 → Lv3
  3: 100,      // Lv3 → Lv4
  4: 150,      // Lv4 → Lv5
  5: Infinity  // 已達最高等級
}
```

---

### 各等級容量常數

```typescript
const MAX_MISSIONS: Record<number, number> = { 1:2, 2:3, 3:4, 4:5, 5:6 }
// 名冊上限已移至 adventurer-management.ts — MAX_ROSTER_BY_GUILD_LEVEL（本檔不再重複常數）

// MissionRank 順序：F < E < D < C < B < A < S < SS < SSS
const MAX_COMMISSION_RANK: Record<number, MissionRank> = {
  1: 'D', 2: 'C', 3: 'B', 4: 'A', 5: 'SSS'
}
```

---

### 升級資格判斷

```typescript
function canUpgrade(guildLevel: number, reputation: number): boolean {
  return guildLevel < 5 && reputation >= UPGRADE_THRESHOLD[guildLevel]
}
```

---

### 升級執行

```typescript
function upgradeGuild(state: GuildCoreState): GuildCoreState {
  if (state.guildLevel >= 5) return state
  return { guildLevel: state.guildLevel + 1 }
}
```

## 極端情況


| #   | 情況                          | 說明                          | 處理方式                                             |
| --- | --------------------------- | --------------------------- | ------------------------------------------------ |
| 1   | **聲望跌回門檻以下**（已升等後聲望下降）      | 任務失敗或賠償導致聲望負成長              | 不降級；等級只升不降，聲望下降不觸發降等，但升等按鈕不再顯示                   |
| 2   | **聲望為負值**（聲名狼藉）             | 大量失敗累積                      | 不影響現有等級；升等按鈕隱藏直到聲望重新達標                           |
| 3   | **已達 Lv5 後繼續獲得聲望**          | 超過 150 繼續累積                 | 聲望繼續記錄，不做任何截頂；升等按鈕永久隱藏                           |
| 4   | **首次遊玩**（新存檔）               | 無任何歷史資料                     | `guildLevel = 1`，`reputation = 0`，不顯示升等按鈕        |
| 5   | **存檔損毀**（`guildLevel` 為無效值） | `undefined`、`NaN`、> 5 或 < 1 | Save/Load 的 `validateSave` 強制回填 `guildLevel = 1` |


---

## 依賴關係


| 系統                       | 方向   | 類型  | 說明                                                                |
| ------------------------ | ---- | --- | ----------------------------------------------------------------- |
| Resource Management（4）   | 上游讀取 | 強   | 讀取 `reputation` 判斷升等資格                                            |
| Mission Dispatch（3）      | 下游讀取 | 強   | 讀取 `MAX_MISSIONS[guildLevel]` 與 `MAX_COMMISSION_RANK[guildLevel]` |
| Adventurer Management（2） | 下游讀取 | 強   | 名冊上限定義於該模組之 `MAX_ROSTER_BY_GUILD_LEVEL`（非 Guild Core 常數）        |
| Guild Building（5）        | 下游依賴 | 弱   | Guild Building 解鎖條件以 `guildLevel` 為前提（未設計）                        |
| Progression / Unlock（8）  | 下游依賴 | 弱   | 解鎖系統可監聽 `guildLevel` 變化觸發事件（未設計）                                  |
| Save / Load（10）          | 持久化  | 強   | `GuildCoreState`（含 `guildLevel`）納入存檔                              |


### 依賴方向

```
Resource Management (4) ──[reputation]──▶ Guild Core (1) ──▶ Mission Dispatch (3)
                                                 │          ──▶ Adventurer Management (2)
                                                 └──▶ Guild Building / Unlock（未設計）
```

---

## 調整旋鈕


| 旋鈕                    | 位置              | 目前值           | 安全範圍      | 說明                    |
| --------------------- | --------------- | ------------- | --------- | --------------------- |
| `UPGRADE_THRESHOLD`   | `guild-core.ts` | 30/60/100/150 | ×0.5 ~ ×2 | 調低讓玩家升等更快；調高拉長成長曲線    |
| `MAX_MISSIONS`        | `guild-core.ts` | 2/3/4/5/6     | 1 ~ 8     | 調低增加排隊感；調高降低管理壓力      |
| `MAX_ROSTER_BY_GUILD_LEVEL` | `adventurer-management.ts` | 4/6/9/12/15 | 2 ~ 20 | 名冊上限唯一來源；櫃台建設後固定 20 |
| `MAX_COMMISSION_RANK` | `guild-core.ts` | D/C/B/A/SSS   | 每級可各降一級   | 調低讓玩家更早接到高難任務，加快世界危險感 |
| 公會稱號文字                | `ui-strings.ts` | 見詳細設計節        | —         | 純文案調整，不影響邏輯           |


---

## 驗收標準

- 新存檔建立時，`guildLevel = 1`，對應稱號「無名者之所」
- `reputation < 30` 時，不顯示升等按鈕
- `reputation >= 30` 且 `guildLevel = 1` 時，主介面顯示「公會可升等！」提示與升等按鈕
- 玩家點擊確認後，`guildLevel` 從 1 升至 2，稱號更新為「旅人的落腳處」
- 升等後，Mission Dispatch 允許的最大同時任務數即時更新為新上限
- 升等後，Adventurer Management 允許的名冊容量即時更新
- 升等後，Mission Dispatch 的委託難度上限即時更新（新難度委託開始出現）
- 升等後聲望下降至門檻以下，不觸發降級，升等按鈕隱藏直到重新達標
- `guildLevel = 5` 時，升等按鈕永久隱藏，稱號顯示「傳說尚未終結之處」

