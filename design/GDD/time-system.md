# Time System 系統設計文件

_建立時間：2026-04-19_
_狀態：已完成_
_系統 ID：F-02_

---

## 1. 概要（Overview）

Time System 負責遊戲內所有時間相關的計算，分為兩個模式：**即時模式**（遊戲執行中）使用 Unity `Time.deltaTime` 累積秒數，驅動任務倒數計時器；**離線模式**（遊戲重啟時）比對儲存的 UTC 時間戳與當前時間，計算離線期間流逝的秒數，一次性推進所有進行中任務。Time System 同時管理每日重置邏輯（UTC 00:00），觸發招募板免費刷新次數歸零等每日事件。所有時間以 **UTC** 儲存，避免玩家調整系統時區造成的欺騙問題。

## 2. 玩家幻想（Player Fantasy）

玩家的核心幻想是「時間真實流逝」——派出去的冒險者不是按下按鈕就立刻回來，而是真的在外面冒險。下班後打開遊戲，發現早上派出去的隊伍已經回來了（或者再也回不來了）。這種真實感讓每次開啟遊戲都像是打開一封「冒險報告」，而不是操作一個即時系統。Time System 在背後默默支撐這個幻想：時間永遠在走，不管玩家有沒有開著遊戲。

## 3. 詳細規則（Detailed Rules）

### 3.0 初始化與執行順序

1. Time System 的 Script Execution Order 必須晚於 DataManager、但早於所有下游系統（F-03 Resource Management、FT-02 Mission Dispatch 等）
2. `Awake`：從 DataManager 讀取 `DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS` 等常數
3. 事件訂閱與退訂在 `OnEnable` / `OnDisable` 中管理（確保物件啟閉時不漏退訂或重複訂閱）
4. `Initialize(lastActiveTimestamp)` 由 Save/Load 系統在載入完成後呼叫，執行離線計算

### 3.1 時間單位與儲存格式

1. 所有時間以 **UTC** 儲存，型別為 `long`（Unix timestamp，秒）
2. 任務時長單位為**分鐘**，內部運算統一轉換為**秒**（`× 60`）
3. 玩家顯示時長取整至 30 分鐘倍數（僅顯示用，不影響計算）

### 3.2 即時模式（遊戲執行中）

1. Time System 在每個 `Update` 累積 `Time.unscaledDeltaTime`（使用 unscaled 確保遊戲暫停時仍計時，因為本系統追蹤的是真實 UTC 時間），每累積 1 秒廣播一次 `OnSecondTick(currentUTCTimestamp)`；若單幀掉幀超過 1 秒，使用 `while (_accumulator >= 1f)` 迴圈補發遺漏的 tick，確保不因低幀率丟失秒數
2. 每秒重算所有進行中任務的 `remainingSeconds = dispatchTimestamp + durationSeconds - nowUTC`（時間戳法，與離線模式一致，無累積誤差）
3. `remainingSeconds ≤ 0` 時，向 Outcome Resolution 發送結算請求（透過 EventBus 廣播 `OnMissionExpired`）
4. UI 顯示倒數以**分鐘**為單位，不顯示秒數

### 3.6 任務計時器註冊

Time System 維護一份進行中任務的計時器清單。其他系統透過以下 API 管理任務計時：

1. `RegisterMission(missionInstanceId, dispatchTimestamp, durationSeconds)` — 派遣時呼叫，加入計時器清單
2. `UnregisterMission(missionInstanceId)` — 任務結算後呼叫，從清單移除（防止重複到期事件）
3. 計時器清單需由 FT-10 Save/Load 序列化；載入時由 Mission Dispatch 重新呼叫 `RegisterMission` 還原

### 3.3 離線模式（遊戲重啟時）

1. 遊戲關閉前記錄 `lastActiveTimestamp`（UTC Unix timestamp）
2. 遊戲啟動時計算 `offlineSeconds = now - lastActiveTimestamp`
3. `offlineSeconds` 上限為 **7 天（604800 秒）**，超過則截斷（防止極端離線造成異常）
4. 對所有進行中任務執行 `remainingSeconds -= offlineSeconds`
5. 計算完成後，顯示「離線摘要畫面」：「你離開了 X 小時 Y 分鐘，有 N 個任務已完成」，玩家按確認後才執行所有結算
6. 確認後依序結算，結算完成以通知列表呈現所有結果

### 3.4 每日重置（Daily Reset）

1. 每次遊戲啟動時，檢查上次重置日期（UTC 日期）是否與當前日期不同
2. 若跨過至少一個 UTC 00:00，觸發每日重置事件（透過 EventBus）
3. 每日重置訂閱者：
   - 招募板：免費手動刷新次數歸零（→ `DAILY_FREE_REFRESH`）
4. 遊戲執行中持續監聽 UTC 00:00 跨越，即時觸發重置（給開著遊戲跨午夜的玩家）

### 3.5 時間欺騙防護

1. 所有時間戳以 UTC 儲存，本地時區變更不影響計算
2. 若 `offlineSeconds < 0`（系統時間被調回過去），視為 0 秒離線，不觸發任何結算
3. 不實作網路時間校正（Game Jam 範圍，純單機）

## 4. 公式（Formulas）

### 4.1 離線時間計算

```
offlineSeconds = clamp(nowUTC - lastActiveTimestamp, 0, 604800)
```

- `nowUTC`：`DateTimeOffset.UtcNow.ToUnixTimeSeconds()`
- `lastActiveTimestamp`：上次關閉遊戲時儲存的 UTC Unix timestamp
- `clamp` 下限為 0（防時間欺騙），上限為 604800（7 天）

### 4.2 任務剩餘時間推進

```
remainingSeconds = dispatchTimestamp + durationSeconds - nowUTC
```

- `dispatchTimestamp`：派遣時記錄的 UTC Unix timestamp
- `durationSeconds`：`baseDuration（分鐘）× 60`
- 結果 ≤ 0 即視為完成

> 注意：採用「目標完成時間戳」而非「累積扣除」，確保即時模式與離線模式結算結果一致，不因累積誤差導致時間偏差。

### 4.3 離線摘要顯示格式

```
offlineHours   = offlineSeconds / 3600        （取整）
offlineMinutes = (offlineSeconds % 3600) / 60  （取整）
completedCount = 進行中任務中 remainingSeconds ≤ 0 的數量
```

顯示字串：`「你離開了 {offlineHours} 小時 {offlineMinutes} 分鐘，有 {completedCount} 個任務已完成」`

### 4.4 顯示時長格式（UI 用）

```
displayMinutes = ceil(remainingSeconds / 60)
displayHours   = displayMinutes / 60   （取整）
displayMins    = displayMinutes % 60   （取整）
```

顯示規則：
- 不足 1 小時：`「X 分鐘」`
- 1 小時以上且分鐘為 0：`「X 小時」`
- 1 小時以上且分鐘不為 0：`「X 小時 Y 分鐘」`

**範例：**
- 4823 秒 → 81 分鐘 → `「1 小時 21 分鐘」`
- 3600 秒 → 60 分鐘 → `「1 小時」`
- 1200 秒 → 20 分鐘 → `「20 分鐘」`

## 5. 邊界情況（Edge Cases）

### 5.1 離線時間

| 情況 | 處理方式 |
|------|---------|
| `offlineSeconds < 0`（系統時間被調回） | 視為 0 秒，不結算，不顯示離線摘要畫面 |
| `offlineSeconds > 604800`（超過 7 天） | 截斷為 604800 秒，正常顯示離線摘要 |
| 首次啟動（無 `lastActiveTimestamp`） | 視為 0 秒離線，跳過離線模式 |
| 離線期間無任何進行中任務 | 跳過離線摘要畫面，直接進入遊戲 |

### 5.2 任務計時

| 情況 | 處理方式 |
|------|---------|
| 同一幀內多個任務同時到期 | 全部加入結算佇列，依序逐一結算 |
| 任務派遣後立即關閉遊戲再開啟 | 以 `dispatchTimestamp + durationSeconds - nowUTC` 重算，結果正確 |
| 玩家在任務結算瞬間關閉遊戲 | 下次啟動時 `remainingSeconds ≤ 0`，正常觸發離線結算 |

### 5.3 每日重置

| 情況 | 處理方式 |
|------|---------|
| 離線期間跨越多個 UTC 00:00 | 只觸發一次每日重置（重置是狀態，不是計數） |
| 玩家開著遊戲跨過 UTC 00:00 | 即時觸發重置，`DAILY_FREE_REFRESH` 歸零 |
| 每日重置與離線結算同時發生 | 先執行離線結算，再觸發每日重置 |

### 5.4 顯示格式

| 情況 | 處理方式 |
|------|---------|
| `remainingSeconds = 0` | 顯示「即將完成」，下一幀觸發結算 |
| 任務已過期但尚未結算（結算佇列中） | 顯示「結算中...」 |

## 6. 依賴關係（Dependencies）

### 6.1 Time System 的依賴（上游）

| 系統 | 用途 |
|------|------|
| F-01 DataManager | `GetInt("DAILY_RESET_HOUR")`、`GetInt("OFFLINE_MAX_SECONDS")` 讀取系統常數 |

### 6.2 依賴 Time System 的系統（下游）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| FT-02 Mission Dispatch | 派遣時取得 `nowUTC` 記錄 `dispatchTimestamp` | `TimeSystem.NowUTC` |
| FT-04 Outcome Resolution | 訂閱任務到期事件，執行結算 | `TimeSystem.OnMissionExpired` |
| FT-01 Adventurer Recruitment | 訂閱每日重置事件，招募板刷新次數歸零 | `TimeSystem.OnDailyReset` |
| FT-10 Save/Load | 儲存 `lastActiveTimestamp`；載入後交給 Time System 執行離線計算 | `TimeSystem.Initialize(lastActiveTimestamp)` |
| F-03 Resource Management | 訂閱每秒 tick 事件以驅動破產倒數檢查；訂閱離線結算完成事件以判定離線期間是否觸發破產 | `TimeSystem.OnSecondTick`, `TimeSystem.OnOfflineResolved` |
| P-03 Notification System | 訂閱任務到期事件，觸發結算通知 | `TimeSystem.OnMissionExpired` |

### 6.3 EventBus 事件定義

Time System 透過 EventBus 廣播以下事件，不直接持有下游系統引用：

| 事件 | 觸發時機 | 攜帶資料 |
|------|---------|---------|
| `OnSecondTick` | 每累積 1 秒（即時模式） | `currentUTCTimestamp : long` |
| `OnMissionExpired` | 任務 `remainingSeconds ≤ 0` | `missionInstanceID` |
| `OnDailyReset` | UTC 00:00 跨越 | 無 |
| `OnOfflineResolved` | 玩家確認離線摘要後 | `offlineSeconds`, `completedCount` |

## 7. 可調參數（Tuning Knobs）

| 參數 | 來源 | 預設值 | 安全範圍 | 影響面向 |
|------|------|--------|---------|---------|
| `DAILY_RESET_HOUR` | `SystemConstants.csv` | `0`（UTC 00:00） | 0–23 | 每日重置觸發時間 |
| `OFFLINE_MAX_SECONDS` | `SystemConstants.csv` | `604800`（7 天） | 86400–604800 | 離線時間上限，低於 1 天可能導致長期不開遊戲的玩家任務無法正常結算 |

> 兩個參數皆透過 DataManager `GetInt` 讀取，修改 CSV 即生效，不需改程式碼。

## 8. 驗收標準（Acceptance Criteria）

### 即時模式

- [ ] 派遣一個時長 1 分鐘的任務，60 秒後自動觸發結算
- [ ] 兩個任務同時到期，兩者皆觸發結算，無遺漏
- [ ] UI 倒數顯示格式正確（`「X 小時 Y 分鐘」` 或 `「X 分鐘」`）
- [ ] `remainingSeconds = 0` 時顯示「即將完成」

### 離線模式

- [ ] 關閉遊戲 5 分鐘後重新啟動，顯示離線摘要畫面，內容正確（時長與完成任務數）
- [ ] 離線摘要畫面顯示時，尚未執行結算；按確認後才結算
- [ ] 離線期間無任何進行中任務，不顯示離線摘要畫面，直接進入遊戲
- [ ] 將系統時間調回過去後啟動遊戲，不顯示離線摘要，無任何錯誤
- [ ] 模擬離線 8 天，`offlineSeconds` 截斷為 604800，不 crash

### 每日重置

- [ ] 模擬跨越 UTC 00:00，`DAILY_FREE_REFRESH` 歸零
- [ ] 離線期間跨越 3 個 UTC 00:00，重置只觸發一次
- [ ] 離線結算與每日重置同時發生時，結算先於重置執行

### 一致性

- [ ] 即時模式倒數到 0 的任務，與離線模式重算的相同任務，結算時機相差不超過 1 秒
