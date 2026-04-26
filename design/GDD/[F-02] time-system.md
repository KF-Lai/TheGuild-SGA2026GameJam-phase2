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
2. 任務計時由 FT-02 Mission Dispatch 自行維護（訂閱 `OnSecondTick` 驅動 `TickCompletionCheck`，見 FT-02 §3.7）；F-02 不重算任務剩餘秒數，亦不發布任務到期事件
4. UI 顯示倒數以**分鐘**為單位，不顯示秒數
5. **`OnMinuteTick` 發布節奏**：每發出 60 次 `OnSecondTick`（即累積 60 秒）後，發布一次 `OnMinuteTick(currentUTCTimestamp)`，供下游低頻週期性檢查（如 FT-03 NPC 自主接單）使用。內部維護 `_minuteAccumulator : int` 計數 `OnSecondTick` 次數；達 `60` 即發布 `OnMinuteTick` 並歸零
6. **離線不補發 `OnMinuteTick`**：離線期間不重播每分鐘的 tick（即使離線跨越 N 分鐘）；重啟後 `_minuteAccumulator` 歸零，於下一次自然累積 60 秒時發一次。NPC 自主接單的離線處理由 FT-11 Offline Resolver 接管（Jam 階段 FT-11 未實作，離線期間 NPC 自主接單暫停；對應行為於 FT-03 §5 Edge Cases 說明）

### 3.6 任務計時器（已移至 FT-02）

FT-02 Mission Dispatch 自行維護 `activeMissions` 列表並訂閱 `OnSecondTick` 進行計時；F-02 不擁有任務計時器清單，亦不提供 `RegisterMission` / `UnregisterMission` API。詳見 FT-02 §3.7。

### 3.3 離線模式（遊戲重啟時）

1. 遊戲關閉前記錄 `lastActiveTimestamp`（UTC Unix timestamp）
2. 遊戲啟動時計算 `offlineSeconds = now - lastActiveTimestamp`
3. `offlineSeconds` 上限為 `OFFLINE_MAX_SECONDS`（預設 604800 秒，即 7 天），超過則截斷（防止極端離線造成異常）
4. 對所有進行中任務執行 `remainingSeconds -= offlineSeconds`
5. 計算完成後，顯示「離線摘要畫面」：「你離開了 X 小時 Y 分鐘，有 N 個任務已完成」，玩家按確認後才執行所有結算
6. 確認後依序結算，結算完成以通知列表呈現所有結果

### 3.4 每日重置（Daily Reset）

1. 每次遊戲啟動時，檢查上次重置日期（UTC 日期）是否與當前日期不同
2. 若跨過至少一個 UTC `DAILY_RESET_HOUR`:00（預設 00:00），觸發每日重置事件（透過 EventBus）
3. 每日重置訂閱者：
   - FT-01 Adventurer Recruitment：招募板免費手動刷新次數歸零（→ `DAILY_FREE_REFRESH`）
   - C-06 World Danger System：呼叫 `CheckLevelUp()` 進行時間閘升階檢查
4. 遊戲執行中持續監聽 UTC `DAILY_RESET_HOUR`:00 跨越，即時觸發重置（給開著遊戲跨重置時間的玩家）

### 3.5 時間欺騙防護

1. 所有時間戳以 UTC 儲存，本地時區變更不影響計算
2. 若 `offlineSeconds < 0`（系統時間被調回過去），視為 0 秒離線，不觸發任何結算
3. 不實作網路時間校正（Game Jam 範圍，純單機）

### 3.7 Tick 暫停機制（Tick Pause Mechanism）

提供 `PauseTick()` API 供 FT-06 Guild Core 於 Game Over 階段 2（玩家確認破產訃聞後）呼叫，停止時間推進。

| API       | 簽名                 | 語義                                                                                                                                         |
| --------- | -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| 暫停 Tick | `PauseTick() : void` | 停止 `_accumulator` 累積、不再發布 `OnSecondTick`；**不影響** MonoBehaviour `Update()`、Unity 引擎層或 UI 動畫；**冪等**（重複呼叫無副作用） |

**內部旗標**：`_tickPaused : bool`（預設 `false`）

**`Update()` 行為變更**：

```
Update():
    if _tickPaused: return                        // 停止累積、停止發 OnSecondTick / OnMinuteTick
    _accumulator += Time.unscaledDeltaTime
    while _accumulator >= 1f:
        _accumulator -= 1f
        EventBus.Publish(OnSecondTick, nowUTC)
        _minuteAccumulator += 1
        if _minuteAccumulator >= 60:
            _minuteAccumulator = 0
            EventBus.Publish(OnMinuteTick, nowUTC)
```

**未提供 `ResumeTick()`**：Game Over 為終態（FT-06 §4.5 狀態機保證），不需恢復。若未來新增玩家暫停功能再擴充。

**與其他功能的互動**：

- **離線計算**：`PauseTick()` 不影響離線模式。玩家在 Game Over 後關遊戲、隔日開啟時，`lastActiveTimestamp` 仍以當前 UTC 比對，但因 `gameOverState = Over` 存檔後，FT-10 讀檔時由 FT-06 直接進入結算畫面，不再執行任何任務結算（FT-02 不發 `OnMissionCompleted`、FT-04 不被觸發）
- **每日重置**：`PauseTick` 後不再偵測 UTC `DAILY_RESET_HOUR` 跨越，`OnDailyReset` 不發布——符合「遊戲已結束」語義
- **OnMinuteTick**：`PauseTick` 後 `OnMinuteTick` 不再發布（`_minuteAccumulator` 停止累加，因 `OnSecondTick` 不再觸發）

## 4. 公式（Formulas）

### 4.1 離線時間計算

```
offlineSeconds = clamp(nowUTC - lastActiveTimestamp, 0, OFFLINE_MAX_SECONDS)
```

- `nowUTC`：`DateTimeOffset.UtcNow.ToUnixTimeSeconds()`
- `lastActiveTimestamp`：上次關閉遊戲時儲存的 UTC Unix timestamp
- `clamp` 下限為 0（防時間欺騙），上限為 `OFFLINE_MAX_SECONDS`（預設 604800，即 7 天）

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

## 5. 邊緣案例（Edge Cases）

### 5.1 離線時間

| 情況                                               | 處理方式                                          |
| -------------------------------------------------- | ------------------------------------------------- |
| `offlineSeconds < 0`（系統時間被調回）             | 視為 0 秒，不結算，不顯示離線摘要畫面             |
| `offlineSeconds > OFFLINE_MAX_SECONDS`（超過上限） | 截斷為 `OFFLINE_MAX_SECONDS` 秒，正常顯示離線摘要 |
| 首次啟動（無 `lastActiveTimestamp`）               | 視為 0 秒離線，跳過離線模式                       |
| 離線期間無任何進行中任務                           | 跳過離線摘要畫面，直接進入遊戲                    |

### 5.2 任務計時

| 情況                         | 處理方式                                                         |
| ---------------------------- | ---------------------------------------------------------------- |
| 同一幀內多個任務同時到期     | 全部加入結算佇列，依序逐一結算                                   |
| 任務派遣後立即關閉遊戲再開啟 | 以 `dispatchTimestamp + durationSeconds - nowUTC` 重算，結果正確 |
| 玩家在任務結算瞬間關閉遊戲   | 下次啟動時 `remainingSeconds ≤ 0`，正常觸發離線結算              |

### 5.3 每日重置

| 情況                       | 處理方式                                   |
| -------------------------- | ------------------------------------------ |
| 離線期間跨越多個 UTC 00:00 | 只觸發一次每日重置（重置是狀態，不是計數） |
| 玩家開著遊戲跨過 UTC 00:00 | 即時觸發重置，`DAILY_FREE_REFRESH` 歸零    |
| 每日重置與離線結算同時發生 | 先執行離線結算，再觸發每日重置             |

### 5.4 顯示格式

| 情況                               | 處理方式                         |
| ---------------------------------- | -------------------------------- |
| `remainingSeconds = 0`             | 顯示「即將完成」，下一幀觸發結算 |
| 任務已過期但尚未結算（結算佇列中） | 顯示「結算中...」                |

## 6. 依賴關係（Dependencies）

### 6.1 Time System 的依賴（上游）

| 系統             | 用途                                                                       |
| ---------------- | -------------------------------------------------------------------------- |
| F-01 DataManager | `GetInt("DAILY_RESET_HOUR")`、`GetInt("OFFLINE_MAX_SECONDS")` 讀取系統常數 |

### 6.2 依賴 Time System 的系統（下游）

| 系統                         | 依賴內容                                                                                        | 介面                                                      |
| ---------------------------- | ----------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| FT-02 Mission Dispatch       | 派遣時取得 `nowUTC` 記錄 `dispatchTimestamp`；訂閱 `OnSecondTick` 驅動 `TickCompletionCheck` 完成任務計時 | `TimeSystem.NowUTC`、`TimeSystem.OnSecondTick`            |
| FT-01 Adventurer Recruitment | 訂閱每日重置事件，招募板刷新次數歸零                                                            | `TimeSystem.OnDailyReset`                                 |
| C-06 World Danger System     | 訂閱每日重置事件，呼叫 `CheckLevelUp()` 進行時間閘升階檢查；讀取 `NowUTC` 於 `GetElapsedDays()` | `TimeSystem.OnDailyReset`、`TimeSystem.NowUTC`            |
| FT-10 Save/Load              | 儲存 `lastActiveTimestamp`；載入後交給 Time System 執行離線計算                                 | `TimeSystem.Initialize(lastActiveTimestamp)`              |
| F-03 Resource Management     | 訂閱每秒 tick 事件以驅動破產倒數檢查；訂閱離線結算完成事件以判定離線期間是否觸發破產            | `TimeSystem.OnSecondTick`, `TimeSystem.OnOfflineResolved` |
| C-02 Adventurer Management   | 訂閱每秒 tick 事件，驅動 Wounded 恢復計時檢查                                                   | `TimeSystem.OnSecondTick`                                 |
| FT-03 NPC Decision           | 訂閱每分鐘 tick 驅動自主接單檢查                                                                | `TimeSystem.OnMinuteTick`                                 |
| FT-04 Outcome Resolution     | 不直接依賴 F-02；計時完成由 FT-02 §3.7 `TickCompletionCheck` 驅動 `OnMissionCompleted` 發布後，FT-04 訂閱該事件執行結算 | —                                                         |
| FT-06 Guild Core             | Game Over 階段 2（玩家確認訃聞後）呼叫暫停 Tick；讀取當前 UTC 作為 Game Over 時間戳             | `TimeSystem.PauseTick()`, `TimeSystem.NowUTC`             |

### 6.3 EventBus 事件定義

Time System 透過 EventBus 廣播以下事件，不直接持有下游系統引用：

| 事件                | 觸發時機                                 | 攜帶資料                           |
| ------------------- | ---------------------------------------- | ---------------------------------- |
| `OnSecondTick`      | 每累積 1 秒（即時模式）                  | `currentUTCTimestamp : long`       |
| `OnMinuteTick`      | 每累積 60 秒（即時模式；離線**不**補發） | `currentUTCTimestamp : long`       |
| `OnDailyReset`      | UTC 00:00 跨越                           | 無                                 |
| `OnOfflineResolved` | 玩家確認離線摘要後                       | `offlineSeconds`, `completedCount` |

> **任務到期事件**：`OnMissionExpired` 已移除。任務計時完成事件由 FT-02 `TickCompletionCheck` 發布 `OnMissionCompleted`（見 FT-02 §3.7）；FT-04 訂閱 `OnMissionCompleted` 而非 F-02 事件。
>
> **註**：`PauseTick()`（§3.7）為同步 API 呼叫，**不**發布事件；呼叫後僅停止 `OnSecondTick` 的發布，`OnMinuteTick` / `OnDailyReset` / `OnOfflineResolved` 皆不再觸發（因皆由 `OnSecondTick` 或啟動流程驅動）。

## 7. 可調參數（Tuning Knobs）

| 參數                  | 來源                  | 預設值           | 安全範圍     | 影響面向                                                          |
| --------------------- | --------------------- | ---------------- | ------------ | ----------------------------------------------------------------- |
| `DAILY_RESET_HOUR`    | `SystemConstants.csv` | `0`（UTC 00:00） | 0–23         | 每日重置觸發時間                                                  |
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

### Tick 暫停（§3.7）

- [ ] 呼叫 `PauseTick()` 後，至少 5 秒內 `OnSecondTick` 不再被發布
- [ ] 呼叫 `PauseTick()` 後，即使跨越 UTC `DAILY_RESET_HOUR`，`OnDailyReset` 不發布
- [ ] 連續呼叫 `PauseTick()` 多次不拋例外、無副作用（冪等）
- [ ] 呼叫 `PauseTick()` 後，`NowUTC` 仍可正確回傳當前 UTC Unix timestamp（僅停止 tick 累積，不阻擋時間查詢）
- [ ] 呼叫 `PauseTick()` 後，`OnMinuteTick` 不再被發布（即使已累積 >30 秒至 `_minuteAccumulator`）

### OnMinuteTick（§3.2 rule 5-6）

- [ ] 即時模式連續執行 60 秒後，`OnMinuteTick` 被發布恰一次；再 60 秒後再一次
- [ ] `OnMinuteTick` payload 的 `currentUTCTimestamp` 等於第 60 / 120 / 180... 次 `OnSecondTick` 的 `currentUTCTimestamp`
- [ ] 離線 10 分鐘後重啟，立即不補發 `OnMinuteTick`；啟動後累積 60 秒時才發第一次
- [ ] 單幀掉幀補發（`while` 迴圈）若累積超過 60 秒，`OnMinuteTick` 可在同一幀內發多次（與 `OnSecondTick` 同步補）
