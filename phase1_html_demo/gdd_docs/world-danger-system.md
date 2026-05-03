# World Danger System（世界危險度）



> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 1 — 離線進行，回來收結果；支柱 3 — 決策有真實後果
> **Jam 版範圍**：次要，影響 Commission Flow 與 Mission Dispatch

## 概覽

**World Danger System（世界危險度）** 是一個**被動升壓系統**——隨著時間推進，世界的威脅程度持續上升，逼迫玩家不斷應對更高風險的環境。

危險度分為 5 個等級：**E（和平）→ D（動盪）→ C（暗湧）→ B（危局）→ A（末世）**。等級越高，任務失敗和冒險者死亡的代價越大，公會允許的最大債務也越深——玩家必須在世界失控之前建立足夠強大的公會，或在崩潰前搏一把。

在 Jam 版中，危險度作為**全局靜態常數**起始於 E，由 Time / Tick System 的 tick 計數被動推進。它不是玩家可以直接操控的數值，而是遊戲節奏的背景壓力。危險度唯一的輸出是：影響 `MAX_DEBT`（Commission Flow）和任務池過濾（Mission Dispatch）。

## 玩家幻想

玩家從不直接感受「世界危險度」這個數字——他感受到的，是**委託板上貼出來的任務越來越兇險**，是**熟悉的冒險者越來越難派出去而不擔心回不來**，是**公會的金庫即使撐著也越來越吃緊**。

世界的惡化是背景音樂，不是前景警報。玩家不需要主動管理它，但每次回來都能感覺到「這個世界好像又變差了一點」。等到危局或末世，連基本的委託都開始動搖公會的根基——這時候的每一次推薦，都是一場賭注。

世界在走向末世，而你的公會是少數還在堅持的存在。

## 詳細設計

### 1. 危險度等級定義


| 等級  | 名稱  | 意象             |
| --- | --- | -------------- |
| E   | 和平  | 世界平靜，偶有小亂      |
| D   | 動盪  | 邊境騷動，商路不安      |
| C   | 暗湧  | 暗勢力抬頭，城鎮開始戒備   |
| B   | 危局  | 大規模衝突爆發，公會岌岌可危 |
| A   | 末世  | 秩序崩潰，每一次委託都是賭命 |


---

### 2. 升級雙重條件

升至下一等級需**同時滿足**以下兩個條件：


| 條件                                                                | 說明                                          |
| ----------------------------------------------------------------- | ------------------------------------------- |
| **時間閘**：`worldAgeMs >= TIME_THRESHOLD[currentLevel]`              | 遊戲開始後累積的真實時間達到閾值（含離線時間）                     |
| **進度閘**：`levelAcceptCount >= LEVEL_ACCEPT_REQUIRED[currentLevel]` | 冒險者在本等級期間，接受**難度 ≥ 當前等級對應最低難度**的委託，累積達到 X 件 |


> **「接受委託」的定義**：冒險者同意推薦、進入任務（`DispatchRecord` 建立）即計入，無論最終成功或失敗。

升級時重置 `levelAcceptCount = 0`，開始累計下一等級的進度。

---

### 3. 各等級對應的難度門檻與進度要求


| 當前等級  | 升至  | 最低任務難度（計入計數） | 時間閾值 | 接受數要求 |
| ----- | --- | ------------ | ---- | ----- |
| E（和平） | D   | F 及以上（所有任務）  | 1 天  | 10 件  |
| D（動盪） | C   | D 及以上        | 3 天  | 15 件  |
| C（暗湧） | B   | C 及以上        | 7 天  | 20 件  |
| B（危局） | A   | B 及以上        | 14 天 | 25 件  |
| A（末世） | —   | —            | —    | —     |


> 時間與接受數均為**調整旋鈕**，Jam 版可依實際節奏調整。

---

### 4. 任務池比例偏移

危險度越高，高難度委託在委託板上出現的權重越大，低難度委託不完全消失但越來越稀少：


| 世界危險度 | F~E | D   | C   | B   | A   | S~SSS |
| ----- | --- | --- | --- | --- | --- | ----- |
| E（和平） | 40% | 30% | 20% | 8%  | 2%  | 0%    |
| D（動盪） | 20% | 35% | 25% | 15% | 5%  | 0%    |
| C（暗湧） | 10% | 20% | 35% | 25% | 8%  | 2%    |
| B（危局） | 5%  | 10% | 25% | 35% | 20% | 5%    |
| A（末世） | 0%  | 5%  | 15% | 30% | 35% | 15%   |


---

### 5. 資料結構

```typescript
interface WorldDangerState {
  currentLevel:     DangerLevel           // 'E' | 'D' | 'C' | 'B' | 'A'
  gameStartedAt:    number                // Unix ms，首次建立存檔時設定，不再變動
  levelAcceptCount: number                // 本等級累積接受件數（升級後歸零）
}

type DangerLevel = 'E' | 'D' | 'C' | 'B' | 'A'
```

---

### 6. 升級檢查時機

每次 Mission Dispatch 建立新 `DispatchRecord`（冒險者接受推薦）後，執行一次升級檢查：

```
1. 若任務難度 >= 當前等級的計數門檻 → levelAcceptCount++
2. 若 worldAgeMs >= TIME_THRESHOLD[currentLevel]
   且 levelAcceptCount >= LEVEL_ACCEPT_REQUIRED[currentLevel]
   → currentLevel 升一級，levelAcceptCount 歸零
```

## 公式

### `worldAgeMs` 計算

```typescript
// 每次需要時動態計算，不持久化（只存 gameStartedAt）
const worldAgeMs = Date.now() - worldDangerState.gameStartedAt
```

---

### 時間閾值常數

```typescript
// 1 天 = 86_400_000 ms
const TIME_THRESHOLD: Record<DangerLevel, number> = {
  E: 1  * 86_400_000,   // 1 天
  D: 3  * 86_400_000,   // 3 天
  C: 7  * 86_400_000,   // 7 天
  B: 14 * 86_400_000,   // 14 天
  A: Infinity            // 末世不再升級
}
```

---

### 接受數要求常數

```typescript
const LEVEL_ACCEPT_REQUIRED: Record<DangerLevel, number> = {
  E: 10,
  D: 15,
  C: 20,
  B: 25,
  A: Infinity   // 末世不再升級
}
```

---

### 計入計數的最低難度

```typescript
// MissionRank: 'F'|'E'|'D'|'C'|'B'|'A'|'S'|'SS'|'SSS'（依序）
const LEVEL_MIN_RANK: Record<DangerLevel, MissionRank> = {
  E: 'F',  // 任何任務都計入
  D: 'D',
  C: 'C',
  B: 'B',
  A: 'A'   // 末世期間仍可記錄，但不觸發升級
}
```

---

### 升級判斷

```typescript
function checkLevelAdvance(state: WorldDangerState): WorldDangerState {
  if (state.currentLevel === 'A') return state   // 已達最高等級
  const worldAgeMs = Date.now() - state.gameStartedAt
  const timeMet  = worldAgeMs >= TIME_THRESHOLD[state.currentLevel]
  const countMet = state.levelAcceptCount >= LEVEL_ACCEPT_REQUIRED[state.currentLevel]
  if (timeMet && countMet) {
    const NEXT: Record<string, DangerLevel> = { E:'D', D:'C', C:'B', B:'A' }
    return { ...state, currentLevel: NEXT[state.currentLevel], levelAcceptCount: 0 }
  }
  return state
}
```

---

### `MAX_DEBT` 與危險度的對應

（定義於 Commission Flow，此處僅作索引）

```typescript
// WORLD_DANGER_DEBT_MULTIPLIER（在 commission-flow.ts 中定義）
// E:×1 / D:×5 / C:×10 / B:×25 / A:×50
// MAX_DEBT = -100 * WORLD_DANGER_DEBT_MULTIPLIER[currentLevel]
```

## 極端情況


| #   | 情況                          | 說明                                      | 處理方式                                                                    |
| --- | --------------------------- | --------------------------------------- | ----------------------------------------------------------------------- |
| 1   | **首次遊玩**（無 `gameStartedAt`） | 新存檔尚未設定起始時間戳                            | 建立存檔時設定 `gameStartedAt = Date.now()`，危險度初始為 E                           |
| 2   | **時鐘被向前撥動**（作弊）             | `Date.now() - gameStartedAt` 異常大，跳過多個等級 | 以 Save/Load 的 `savedAt` 驗證；異常差值超過合理範圍時，限制單次累加量不超過 `MAX_OFFLINE_MS`（3 天） |
| 3   | **同時滿足多個等級的升級條件**（長時間未登入）   | 時間閘已超過多個等級閾值                            | 每次只升一級，升級後重新呼叫 `checkLevelAdvance` 直到不再滿足為止（但進度閘仍需達標）                   |
| 4   | **進度閘永遠未達到**（玩家放置但不互動）      | 只靠時間無法觸發升級                              | 設計意圖正確：玩家必須有實際推薦行為才能推進；等待玩家回來操作                                         |
| 5   | **存檔被清除後重開**                | `gameStartedAt` 遺失                      | 重新建立存檔，`gameStartedAt` 重置為 `Date.now()`，危險度回到 E                         |
| 6   | **任務池比例表全為 0**（設定錯誤）        | 某危險度下加總比例為 0                            | 防衛性 fallback：至少保留 F 等級任務一件，防止空白委託板                                      |


## 依賴關係


| 系統                    | 方向   | 類型      | 說明                                                                                                                 |
| --------------------- | ---- | ------- | ------------------------------------------------------------------------------------------------------------------ |
| Time / Tick System（9） | 上游   | 弱（概念依賴） | `gameStartedAt` 使用相同的 `Date.now()` 時間基準；無直接 API 呼叫                                                                 |
| Mission Dispatch（3）   | 雙向   | 強       | Mission Dispatch 在建立 `DispatchRecord` 後呼叫 `worldDanger.onAccept(rank)`；World Danger 的任務池比例偏移表由 Mission Dispatch 讀取 |
| Commission Flow（15）   | 下游讀取 | 強       | Commission Flow 讀取 `currentLevel` 計算 `MAX_DEBT`                                                                    |
| Save / Load（10）       | 持久化  | 強       | `WorldDangerState`（含 `gameStartedAt`, `currentLevel`, `levelAcceptCount`）納入存檔                                      |


### 依賴方向

```
Time/Tick (9) ──[時間基準]──▶ World Danger (12) ──▶ Commission Flow (15)
                                      ▲                      │
                              Mission Dispatch (3) ◀──────────┘
                              （讀取比例偏移表）
```

---

## 調整旋鈕


| 旋鈕                      | 位置                       | 目前值                  | 安全範圍        | 說明                     |
| ----------------------- | ------------------------ | -------------------- | ----------- | ---------------------- |
| `TIME_THRESHOLD`        | `world-danger-system.ts` | E:1天/D:3天/C:7天/B:14天 | ×0.5 ~ ×3   | 調短加快世界惡化節奏；調長給玩家更多準備時間 |
| `LEVEL_ACCEPT_REQUIRED` | `world-danger-system.ts` | E:10/D:15/C:20/B:25  | ×0.5 ~ ×2   | 調低降低參與門檻；調高要求玩家更積極互動   |
| `LEVEL_MIN_RANK`        | `world-danger-system.ts` | E:F/D:D/C:C/B:B      | 可各自降一級      | 放寬計入難度門檻，讓進度閘更容易達成     |
| 任務池比例表                  | `world-danger-system.ts` | 見詳細設計節               | 各欄合計 = 100% | 調整危險度對任務組成的影響幅度        |


---

## 驗收標準

- 新存檔建立時，`currentLevel = 'E'`，`gameStartedAt = Date.now()`，`levelAcceptCount = 0`
- 冒險者接受任意 F+ 任務 10 件且時間達 1 天後，自動升至 D
- 升級後 `levelAcceptCount` 歸零，重新計數
- D 等級下，僅接受 D 及以上難度的委託才計入計數
- 時間未達閾值時，即使接受數已達，危險度不升級
- 接受數未達閾值時，即使時間已達，危險度不升級
- A 等級時，`checkLevelAdvance` 呼叫後直接返回，無任何副作用
- `MAX_DEBT` 在 Commission Flow 讀取 `currentLevel` 後正確計算（E:-100 / D:-500 / C:-1000 / B:-2500 / A:-5000）
- 任務池比例在危險度 A 時，F~E 任務比例為 0%，B+ 比例合計 ≥ 80%
- 任務池比例 fallback：任何情況下委託板至少有一件任務可顯示

