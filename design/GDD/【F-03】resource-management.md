# Resource Management 系統設計文件

_建立時間：2026-04-19_
_狀態：設計中_
_系統 ID：F-03_

---

## 1. 概要（Overview）

Resource Management 是遊戲中金幣與聲望兩項核心資源的唯一管理者。所有系統若需增減資源，必須透過 Resource Management 的 API（`AddGold`、`AddReputation`）執行，不得直接修改資源值。金幣硬性下限由 `_currentBankruptcyThreshold` 決定（預設 `-100`，由上游系統透過 `SetBankruptcyThreshold(int)` API 被動寫入；典型推送者為 C-06 World Danger System，危險度升級時推送更寬鬆的下限），上限為 9,999,999；聲望限制在 -100 至 100 之間。金幣進入負值時啟動破產倒數警告，倒數期長度由 `_currentWarningDuration` 決定（預設 `86400` 秒，由上游透過 `SetBankruptcyWarningDuration(int)` 主動推送；典型推送者為 FT-07 Guild Building System，啟動時與預備金保險櫃升級時推送對應秒數）。Resource Management 作為 Foundation 層，**不主動依賴任何 Core 層系統**——門檻資訊與警告期長度皆由上游主動推送，保持分層單向。Resource Management 同時負責在資源變動時廣播事件，供 UI 與其他系統即時更新。

## 2. 玩家幻想（Player Fantasy）

金幣是公會存亡的命脈——不是抽象數字，而是每一個決策的重量。玩家接下高難度委託時感受到的緊張，來自「如果冒險者失敗，我要賠錢」的真實壓力。負債狀態不是遊戲結束，而是一種絕境感：你必須接下更多委託才能翻身，而每一單都是賭注。聲望則是公會在世界上的臉面——不僅影響委託品質與冒險者意願，更決定你能承受多少債務。高聲望帶來更寬裕的緩衝，但聲望崩盤時，債務壓力會立刻撲面而來。資源管理的張力正是這款遊戲「有重量的決策」支柱的數值基礎。

## 3. 詳細規則（Detailed Rules）

### 3.1 資源種類

| 資源 | 型別 | 初始值 | 範圍 |
|------|------|--------|------|
| 金幣（Gold） | `int` | `100` | `_currentBankruptcyThreshold`（預設 `-100`，由上游推送更新）~ `GOLD_MAX (9,999,999)` |
| 聲望（Reputation） | `int` | `0` | `-100` ~ `100` |

### 3.2 金幣規則

1. 所有金幣增減透過 `AddGold(int amount)` 執行，`amount` 可為正（收入）或負（支出）
2. 執行前檢查：`currentGold + amount < GetCurrentBankruptcyThreshold()` → 拒絕操作，回傳 `false`，不執行增減
3. 金幣上限為 `GOLD_MAX = 9,999,999`；超過上限時靜默截斷（clamp），不拒絕操作
4. 負值為**債務狀態**，不影響任務接受，但建設/招募等主動支出仍受破產門檻限制
5. 金幣硬性下限（`GetCurrentBankruptcyThreshold()`）由 `_currentBankruptcyThreshold` 決定；上游系統透過 `SetBankruptcyThreshold(int newThreshold)` API 被動推送更新。F-03 **不主動查詢、不訂閱上游事件**——呼叫方（如 C-06）負責在門檻變化時主動推送。破產倒數秒數由上游（典型推送者為 FT-07 預備金保險櫃）透過 `SetBankruptcyWarningDuration` 主動推送；F-03 不主動查表
6. **初始值**：`_currentBankruptcyThreshold` 在 `Awake` 階段初始化為預設值 `-100`；上游（如 C-06）於啟動流程中主動呼叫 `SetBankruptcyThreshold` 覆寫為當前正確值。若啟動完成後仍無任何推送，F-03 繼續沿用預設 `-100`，不報錯（允許 F-03 在測試情境或缺少 C-06 的場景下獨立運作）
7. **允許破產支出路徑**：`AddGoldAllowBankruptcy(int amount)` 為 FT-05 Guild Gold Flow（預收傭金、結算入帳/賠償、維護費與薪水扣款）及未來類似金流場景的專用 API。語義與 `AddGold` 相同——仍 clamp `GOLD_MAX`、仍廣播 `OnGoldChanged`、仍更新破產警告狀態——但**略過規則 2 的 reject 檢查**：即使 `currentGold + amount < GetCurrentBankruptcyThreshold()` 仍執行；若結算後金幣低於門檻，立即觸發 `Bankrupt`（由 `EvaluateWarningState()` 處理）。**單次呼叫保證**：`netDelta` 為線性加減，`prevGold → currentGold` 單調變化，單次呼叫至多產生一次狀態過渡（`Normal↔Warning` 或 `Warning↔Bankrupt`），**不可能**「穿越零線兩次」（即不會於同一呼叫內先 Warning 再恢復 Normal）；FT-05 預收/結算快照可依此保證做狀態前後對照（見 FT-05 §5.5.3）

### 3.3 聲望規則

1. 所有聲望增減透過 `AddReputation(int amount)` 執行
2. 聲望結果 `clamp` 至 `-100 ~ 100`，不報錯
3. 聲望標籤（label）由當前聲望值對應 `ReputationLabelTable` 查詢，Resource Management 不直接維護標籤狀態
4. `AddReputation` 執行後，呼叫 `EvaluateWarningState()` 進行統一狀態校正（§3.6 rule 10）；涵蓋：聲望上升使門檻寬鬆導致 `Warning` 解除條件、聲望下降使門檻緊縮導致 `currentGold < threshold` 立即 `Bankrupt`（雖然實務上破產門檻由 C-06 `maxDebt` 控制，與聲望無關，但 `warningDurationSec` 查詢是聲望相關的，故此處仍需狀態校正）

### 3.4 事件廣播

1. `AddGold` 與 `AddGoldAllowBankruptcy` 執行成功後，皆廣播 `OnGoldChanged(int newValue, int delta)`
2. `AddReputation` 執行後，廣播 `OnReputationChanged(int newValue, int delta)`
3. 破產警告狀態變更時，廣播 `OnBankruptcyWarningStateChanged(BankruptcyWarningState newState)`
4. 任何系統不得跳過 API 直接修改資源值
5. **重入防護**：`AddGold` / `AddGoldAllowBankruptcy` / `AddReputation` 共用同一 `_isProcessing` flag 防止事件訂閱者在 callback 中再次呼叫任一金幣/聲望 API 造成遞迴。若偵測到重入，`Debug.LogError` 並拒絕操作
6. 事件訂閱與退訂在 `OnEnable` / `OnDisable` 中管理（訂閱 `TimeSystem.OnSecondTick`、`TimeSystem.OnOfflineResolved`）
7. **初始化順序**：F-03 的 Script Execution Order 必須晚於 F-01 DataManager、F-02 Time System。`Awake` 階段從 DataManager 讀取常數（`GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX`），並將 `_currentBankruptcyThreshold` 初始化為預設 `-100`；`Start` 階段不主動查詢任何上游，等待上游透過 `SetBankruptcyThreshold` 推送正確值（C-06 在其 `Start` 階段主動推送 E 階 `maxDebt = -100`；見 C-06 §3.4）

> **FSD 回註（2026-04-26，源自 `【F-03-FSD】resource-management.md` §8.3 條目 D3）**：rule 3 的事件 payload 為 superset。實作發布 `OnBankruptcyStateChangedEvent(BankruptcyWarningState previousState, BankruptcyWarningState currentState)`，含「前狀態」與「新狀態」雙欄位（GDD 規範僅 `newState`）。優點：下游可直接區分轉移類型（如 `Warning→Normal` 自然恢復 vs `Bankrupt→Normal` 重置），不需自行維護前狀態快照。對既有訂閱者向後兼容（讀 `currentState` 等同 GDD 規範的 `newState`）。

### 3.5 查詢與寫入 API

1. `GetGold() : int` — 取得當前金幣
2. `GetReputation() : int` — 取得當前聲望
3. `CanAfford(int amount) : bool` — 檢查是否能支出 `amount`（考慮破產門檻）
4. `GetCurrentBankruptcyThreshold() : int` — 回傳 `_currentBankruptcyThreshold`；由 `SetBankruptcyThreshold` 寫入，F-03 不主動查詢上游
5. `GetBankruptcyWarningState() : BankruptcyWarningState` — 取得當前破產警告狀態
6. `GetBankruptcyWarningRemainingSeconds() : long` — 取得破產倒數剩餘秒數；非 `Warning` 狀態時回傳 `0`
7. `SetBankruptcyThreshold(int newThreshold) : void` — **寫入 API**，由上游（典型呼叫者為 C-06 World Danger System）於啟動、危險度升級等時機主動推送新門檻。寫入 `_currentBankruptcyThreshold` 後呼叫 `EvaluateWarningState()` 進行狀態校正（確保金幣 `< threshold` 時立即 `Bankrupt`）。F-03 不對 `newThreshold` 做方向性檢查（寬鬆 / 緊縮皆接受），呼叫方需自行保證業務語義（C-06 保證只寬鬆）
8. `SetBankruptcyWarningDuration(int newDurationSec) : void` — **寫入 API**，由上游（典型呼叫者為 FT-07 Guild Building System，啟動時與預備金保險櫃升級時）主動推送破產倒數秒數。寫入 `_currentWarningDuration`；不影響已進入 `Warning` 時鎖定的 `_warningDurationSec`（鎖定值於下次重新進入 `Warning` 時才讀取新 `_currentWarningDuration`）。F-03 **不對 `newDurationSec` 做業務語義檢查**（業務語義由 FT-07 保證）
9. `GetBankruptcyWarningDuration() : long` — 回傳 `_currentWarningDuration`；反映上游最近一次推送的值，預設 `86400`（未推送時沿用）

### 3.6 破產警告（Bankruptcy Warning）

破產警告是金幣進入負值後的緩衝機制。玩家在此期間可以完全正常遊玩，但 UI 持續顯示警告與倒數計時，直到觸發恢復或破產。

#### 狀態定義

| 狀態（`BankruptcyWarningState`） | 條件 |
|--------------------------------|------|
| `Normal` | `gold >= 0` |
| `Warning` | `GetCurrentBankruptcyThreshold() <= gold < 0`，且倒數未到期 |
| `Bankrupt` | 倒數到期時 `gold < 0` |

#### 規則

1. 當 `AddGold` 執行後 `currentGold < 0`（且先前狀態為 `Normal`），進入 `Warning` 狀態
2. 進入 `Warning` 時：記錄 `_bankruptcyWarningStartTime`（UTC Unix timestamp），並從 `_currentWarningDuration` 鎖定 `_warningDurationSec`（中途此值若再被 `SetBankruptcyWarningDuration` 推送，不影響已鎖定值）
3. 警告期間玩家可執行所有操作，不受任何限制
4. 每次 `AddGold` 後：
   - 若 `currentGold >= 0` → 狀態回復為 `Normal`，清除 `_bankruptcyWarningStartTime` 與 `_warningDurationSec`，廣播 `OnBankruptcyWarningStateChanged(Normal)`
   - 若 `currentGold < 0` 且仍在倒數期內 → 維持 `Warning`，**不重置**計時器
5. Time System 每秒 tick 時，Resource Management 檢查：若 `currentUTC - _bankruptcyWarningStartTime >= _warningDurationSec`，且 `currentGold < 0` → 轉為 `Bankrupt`，廣播 `OnBankruptcyWarningStateChanged(Bankrupt)`
6. 上游呼叫 `SetBankruptcyThreshold(newThreshold)` 時，F-03 更新 `_currentBankruptcyThreshold` 並呼叫 `EvaluateWarningState()`（維持原邏輯）；C-06 等典型呼叫者保證只寬鬆（更負），因此一般不會觸發立即破產，但 `EvaluateWarningState` 仍防禦性校正狀態。上游呼叫 `SetBankruptcyWarningDuration(newDurationSec)` 時，僅更新 `_currentWarningDuration`，**不影響已進入 `Warning` 而鎖定的 `_warningDurationSec`**（鎖定不變，下次重新進入 `Warning` 時才讀取新值）
7. 離線回補時，若離線期間倒數已到期且金幣仍為負值 → 於離線摘要結算後立即判定 `Bankrupt`
8. `Bankrupt` 狀態的後果由上層系統（破產機制）處理；Resource Management 僅負責廣播狀態，不自行執行懲罰
9. `ResetBankruptcyState()` 為保留 API：Game Jam 階段 FT-06 Game Over 為終態（見 FT-06 §4.5），無上層系統呼叫此 API；保留定義供未來擴充（作弊復活、管理員模式等）使用
10. **狀態一致性保證**：在以下時機點執行 `EvaluateWarningState()` 進行狀態校正——(a) 存檔載入後、(b) 離線結算完成後、(c) `AddReputation` 執行後、(d) `SetBankruptcyThreshold` 執行後。此方法檢查 `currentGold < 0` 但 `_warningState == Normal` 的不一致狀態，並根據當前條件修正為 `Warning` 或 `Bankrupt`

### 3.7 ReputationLabelTable 資料表定義

`ReputationLabelTable.csv` 為聲望標籤對照表，依聲望區間查詢對應的顯示文字。Resource Management 不直接維護標籤狀態（§3.3 rule 3）；UI 消費者透過 F-01 DataManager 查詢，F-03 本身不持有標籤。

#### 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|------|------|------|------|------|
| ID | string | ✓ | — | PK，流水號字串（如 `"1"` ~ `"10"`），僅供排序與 debug |
| minReputation | int | ✓ | -100 ~ 100 | 區間下限（含） |
| maxReputation | int | ✓ | -100 ~ 100 | 區間上限（含） |
| label | string | ✓ | — | 對應聲望區間的顯示文字標籤（設計師填入） |

> `ID` 為 CSV 規範第一欄 string PK，不被業務邏輯引用。`label` 文字內容 GDD 未指定（見 gdd-gap）。

#### 約束 / 不變量

- 共 **10 筆**，完整覆蓋 `-100 ~ 100`（§4.4）
- **無缺口**：任意整數聲望值必須落入某一列的 `[minReputation, maxReputation]`
- **不重疊**：各列區間互不交叉；多列命中時 `Where` 取第一個符合列
- **`minReputation ≤ maxReputation`**（單列內）
- `minReputation` / `maxReputation` 值域：`[REPUTATION_MIN, REPUTATION_MAX]`（即 -100 ~ 100，與 `SystemConstants` 常數對齊）

#### 查詢方式（§4.4）

```
label = ReputationLabelTable.Where(r => currentReputation >= r.minReputation
                                     && currentReputation <= r.maxReputation)
```

F-03 本身不執行此查詢；由下游 UI（P-02）透過 F-01 DataManager 直接取得標籤後顯示。

#### GDD 缺漏（gdd-gap）

- `label` 欄位的 10 個文字值 GDD 未指定，由設計師填入 CSV
- PK 欄位命名（`ID`）GDD 未明示，依 CSV 第一欄為 string PK 慣例補足

---

## 4. 公式（Formulas）

### 4.1 金幣增減

```
AddGold(amount):
    if _isProcessing → Debug.LogError, return false   // 重入防護
    _isProcessing = true

    threshold = GetCurrentBankruptcyThreshold()
    newGold = (long)currentGold + (long)amount         // 用 long 中間值防止 int 溢位
    if newGold < threshold → _isProcessing = false, return false

    prevGold = currentGold
    currentGold = (int)min(newGold, GOLD_MAX)    // 下限由前述 reject 保證，此處僅 clamp 上限
    actualDelta = currentGold - prevGold

    if actualDelta != 0:                         // delta=0 時跳過廣播，避免不必要的 UI 更新
        broadcast OnGoldChanged(currentGold, actualDelta)
        UpdateBankruptcyWarning(prevGold, currentGold)

    _isProcessing = false
    return true
```

> 注意：`actualDelta` 為實際生效的變動量（可能因 GOLD_MAX clamp 而小於傳入的 `amount`）。
> 注意：中間計算使用 `long` 避免極端輸入（如 `amount = int.MaxValue`）造成 `int` 溢位。
> 注意：clamp 下限已由前方的 reject 檢查保證（`newGold >= threshold`），`min` 僅處理上限截斷。

### 4.1a 允許破產的金幣增減（`AddGoldAllowBankruptcy`）

FT-05 Guild Gold Flow 專用；語義差異見 §3.2 rule 7。

```
AddGoldAllowBankruptcy(amount):
    if _isProcessing → Debug.LogError, return false    // 重入防護（與 AddGold 共用 flag）
    _isProcessing = true

    newGold = (long)currentGold + (long)amount         // 用 long 中間值防止 int 溢位
    // 與 AddGold 不同：不檢查 threshold，允許 newGold < threshold（允許加深債務）

    prevGold = currentGold
    currentGold = (int)min(newGold, GOLD_MAX)          // 仍 clamp 上限；下限不截斷
    actualDelta = currentGold - prevGold

    if actualDelta != 0:
        broadcast OnGoldChanged(currentGold, actualDelta)
        EvaluateWarningState()    // 統一入口：處理 Normal↔Warning 切換 + threshold 突破→Bankrupt

    _isProcessing = false
    return true
```

**與 `AddGold` 的語義對照**：

| 情況 | `AddGold` | `AddGoldAllowBankruptcy` |
|---|---|---|
| `newGold >= 0` | 執行 → 可能清 Warning | 執行 → 可能清 Warning |
| `threshold <= newGold < 0` | 執行 → 進 Warning | 執行 → 進 Warning |
| `newGold < threshold` | **reject**（回傳 `false`，金幣不變） | **執行** → 立即 Bankrupt |
| `newGold > GOLD_MAX` | 靜默 clamp 至 `GOLD_MAX` | 靜默 clamp 至 `GOLD_MAX` |
| 廣播 `OnGoldChanged` | 有 | 有 |
| 破產警告更新入口 | `UpdateBankruptcyWarning` | `EvaluateWarningState`（涵蓋 Bankrupt 分支） |

> **單次 netDelta 不穿越零線兩次**：`amount` 為常數，`prevGold → currentGold` 為單調線性變化，單次呼叫至多產生一次狀態過渡（`Normal↔Warning` 或 `Warning↔Bankrupt`）。FT-05 預收/結算流程可依此保證：若 `bankruptcyStateBefore == Warning`、`bankruptcyStateAfter == Normal`，則金幣確實由負回正（不會發生「正→負→正」的假陽性）。

### 4.2 聲望增減

```
AddReputation(amount):
    if _isProcessing → Debug.LogError, return    // 重入防護（與 AddGold 一致）
    _isProcessing = true

    newValue = clamp(currentReputation + amount, REPUTATION_MIN, REPUTATION_MAX)
    delta = newValue - currentReputation
    currentReputation = newValue

    if delta != 0:                               // delta=0 時跳過廣播
        broadcast OnReputationChanged(currentReputation, delta)

    // 聲望變動後執行統一狀態校正入口（§3.6 rule 10）；涵蓋 Normal↔Warning 切換與 threshold 突破→Bankrupt
    EvaluateWarningState()

    _isProcessing = false
```

> 注意：`delta` 為實際生效的變動量（可能因 clamp 而小於傳入的 `amount`）。

### 4.3 支出能力檢查

```
CanAfford(amount):
    return currentGold - amount >= GetCurrentBankruptcyThreshold()
```

> **FSD 回註（2026-04-26，源自 `【F-03-FSD】resource-management.md` §8.3 條目 D6）**：實作於本公式之前加 `if (amount <= 0) return true` 短路，語義等價（負支出視為收入或零支出，必能負擔）。短路提升熱路徑效率，且避免 `currentGold - amount` 在 `amount` 極大負值時造成 `int` 溢位（雖實務上不會發生，但屬防禦性實作）。

### 4.4 聲望標籤查詢（參考，非 RM 內部邏輯）

```
label = ReputationLabelTable.Where(r => currentReputation >= r.minReputation
                                     && currentReputation <= r.maxReputation)
```

依 `ReputationLabelTable` 定義，10 個標籤涵蓋 -100 ~ 100 全範圍。

### 4.5 破產警告狀態機

```
// AddGold 後觸發
UpdateBankruptcyWarning(prevGold, newGold):
    if newGold >= 0:
        if _warningState == Warning:
            _warningState = Normal
            _bankruptcyWarningStartTime = 0
            _warningDurationSec = 0
            broadcast OnBankruptcyWarningStateChanged(Normal)
        return

    if newGold < 0 and _warningState == Normal:
        _warningState = Warning
        _bankruptcyWarningStartTime = currentUTCTimestamp
        _warningDurationSec = _currentWarningDuration   // 進入 Warning 時從 _currentWarningDuration 鎖定
        broadcast OnBankruptcyWarningStateChanged(Warning)

    // gold < 0 且已在 Warning → 不重置計時器，繼續倒數

// Time System 每秒 tick 時呼叫
OnTimeTick(currentUTCTimestamp):
    if _warningState != Warning: return
    elapsed = currentUTCTimestamp - _bankruptcyWarningStartTime
    if elapsed >= _warningDurationSec:
        TriggerBankruptcy()

// 聲望變動後觸發
TriggerBankruptcy():
    _warningState = Bankrupt
    broadcast OnBankruptcyWarningStateChanged(Bankrupt)

// 剩餘秒數查詢
GetBankruptcyWarningRemainingSeconds():
    if _warningState != Warning: return 0
    elapsed = currentUTCTimestamp - _bankruptcyWarningStartTime
    return max(0, _warningDurationSec - elapsed)

// 離線回補（訂閱 TimeSystem.OnOfflineResolved，玩家確認離線摘要後呼叫）
OnOfflineResolved(long offlineSeconds, int completedCount):
    if _warningState != Warning: return
    if currentGold < 0:
        elapsed = currentUTCTimestamp - _bankruptcyWarningStartTime
        if elapsed >= _warningDurationSec:
            TriggerBankruptcy()

// 保留 API（Game Jam 階段無呼叫者；見 §3 規則 9）
ResetBankruptcyState():
    _warningState = Normal
    _bankruptcyWarningStartTime = 0
    _warningDurationSec = 0
    broadcast OnBankruptcyWarningStateChanged(Normal)

// 狀態一致性校正（存檔載入後、離線結算後、AddReputation 後呼叫）
EvaluateWarningState():
    threshold = GetCurrentBankruptcyThreshold()
    if currentGold >= 0:
        if _warningState == Warning:
            // 金幣已回正但狀態仍為 Warning → 修正
            _warningState = Normal
            _bankruptcyWarningStartTime = 0
            _warningDurationSec = 0
            broadcast OnBankruptcyWarningStateChanged(Normal)
        return

    // currentGold < 0
    if currentGold < threshold:
        TriggerBankruptcy()    // 超過門檻，直接破產
        return

    if _warningState == Normal:
        // gold < 0 但狀態仍為 Normal → 修正為 Warning
        _warningState = Warning
        _bankruptcyWarningStartTime = currentUTCTimestamp
        _warningDurationSec = _currentWarningDuration   // 進入 Warning 時從 _currentWarningDuration 鎖定
        broadcast OnBankruptcyWarningStateChanged(Warning)
```

### 4.6 破產門檻查詢與寫入

```
GetCurrentBankruptcyThreshold():
    return _currentBankruptcyThreshold   // 由 SetBankruptcyThreshold 寫入；預設 -100

// 上游（典型呼叫者為 C-06）於啟動、危險度升級等時機主動呼叫
SetBankruptcyThreshold(newThreshold):
    _currentBankruptcyThreshold = newThreshold
    // 執行 EvaluateWarningState() 以確保狀態一致性：
    // - 若 newThreshold 比先前寬鬆（更負），且當前金幣仍高於新門檻，維持原狀態
    // - 若 newThreshold 比先前緊縮，且金幣已低於新門檻，立即 Bankrupt（防禦性，正常情境不會發生）
    EvaluateWarningState()

// 上游（典型呼叫者為 FT-07）於啟動時與保險櫃升級時主動呼叫
SetBankruptcyWarningDuration(newDurationSec):
    _currentWarningDuration = newDurationSec
    // 不影響已鎖定的 _warningDurationSec（進入 Warning 時才鎖定）
    // 不呼叫 EvaluateWarningState()：警告期長度的改變不影響當前是否應進入 Warning

// 查詢當前推送值
GetBankruptcyWarningDuration():
    return _currentWarningDuration   // 預設 86400；未接收過推送時沿用預設
```

## 5. 邊緣案例（Edge Cases）

### 5.1 金幣

| 情況 | 處理方式 |
|------|---------|
| `AddGold(-500)` 會使金幣低於破產門檻 | 拒絕操作，回傳 `false`，金幣不變，呼叫方負責處理拒絕 |
| `AddGold(0)` | 允許，`actualDelta = 0`，**不廣播**事件（delta=0 跳過廣播，避免不必要的 UI 更新） |
| `AddGold(9999999)` 使金幣超過 `GOLD_MAX` | 靜默 clamp 至 `9,999,999`，`actualDelta` 為實際增量，不報錯 |
| 傭金收入使負債金幣回正 | 正常執行，同步清除 Warning 狀態 |

### 5.2 聲望

| 情況 | 處理方式 |
|------|---------|
| `AddReputation(50)` 但聲望已為 `80`（會超過 100） | clamp 至 100，`delta = 20`，不報錯 |
| `AddReputation(0)` | 允許，delta = 0，**不廣播**事件；仍執行 `GetCurrentBankruptcyThreshold()` 防禦性重算 |
| 聲望已達 `-100` 再扣減 | clamp 至 -100，delta = 0 |
| 上游呼叫 `SetBankruptcyThreshold` 推送更寬鬆門檻（更負） | `_currentBankruptcyThreshold` 更新，`EvaluateWarningState` 校正狀態；`currentGold` 仍在新門檻上方，不觸發破產 |

### 5.3 初始化

| 情況 | 處理方式 |
|------|---------|
| 首次啟動（無存檔） | 金幣初始化為 `100`，聲望初始化為 `0`，警告狀態為 `Normal` |
| 載入存檔後，金幣為負值且 `_bankruptcyWarningStartTime` 存在 | 以存檔時間戳恢復 `Warning` 狀態，繼續倒數 |
| 載入存檔後，金幣為正值，但存檔中保有 `_bankruptcyWarningStartTime` | 忽略，清除時間戳，狀態為 `Normal` |

### 5.4 破產警告

| 情況 | 處理方式 |
|------|---------|
| 金幣從正值降為負值 | 進入 `Warning`,鎖定 `_warningDurationSec = _currentWarningDuration`(由上游 FT-07 推送的當前值,§3.5 `SetBankruptcyWarningDuration`) |
| 金幣在 Warning 期間再次減少（仍為負值） | 維持 `Warning`,**不重置**計時器 |
| 金幣在 Warning 期間恢復至 `>= 0` | 回到 `Normal`,清除計時器 |
| 金幣正負反覆振盪（多次進出 Warning） | 每次從正轉負,以**當下**`_currentWarningDuration` 重新鎖定 `_warningDurationSec` 並開始新計時器 |
| 聲望於 Warning 期間上升（門檻寬鬆） | 不影響已鎖定的 `_warningDurationSec`；若 `currentGold >= newThreshold`，邏輯不變（gold 仍為負，繼續倒數） |
| Warning 期間，上游推送 `SetBankruptcyThreshold` 使門檻更寬鬆 | `_currentBankruptcyThreshold` 更新，`currentGold` 仍高於新門檻，維持 `Warning`，倒數繼續 |
| 離線期間倒數已到期，金幣仍為負值 | 離線摘要結算後立即判定 `Bankrupt` |
| 離線期間金幣因結算恢復正值，但倒數本已到期 | 以金幣狀態為準：`gold >= 0` → `Normal`（不觸發 Bankrupt） |

## 6. 依賴關係（Dependencies）

### 6.1 Resource Management 的依賴（上游）

| 系統 | 用途 | 介面 |
|------|------|------|
| F-01 DataManager | 讀取 `REPUTATION_MIN`、`REPUTATION_MAX`、`GOLD_MAX`、`GOLD_INITIAL` 常數；查詢 `ReputationLabelTable`（聲望標籤顯示用）。`BankruptcyThresholdTable` 之 `warningDurationSec` 欄位已不作 runtime 查詢（詳見 §7.2） | 同步查詢 |
| F-02 Time System | 訂閱 `OnSecondTick` 驅動破產倒數檢查；訂閱 `OnOfflineResolved`（玩家確認離線摘要後）執行離線破產判定 | 事件訂閱 |
| FT-07 Guild Building System | 啟動時與預備金保險櫃升級時，主動呼叫 `SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())` 推送破產倒數秒數 | `SetBankruptcyWarningDuration` |

> **Foundation 層零 Core 依賴**：F-03 在同層只依賴 F-01 / F-02；FT-07 是 Core 層，其推送方向為 FT-07 → F-03（FT-07 主動呼叫 F-03 的寫入 API），F-03 **不依賴 FT-07**（不查詢、不訂閱事件）。此設計保留在 FT-07 未實作時 F-03 可獨立運作（沿用預設 `_currentWarningDuration = 86400`）。破產門檻同理，由上游（如 C-06）透過 `SetBankruptcyThreshold` 主動推送。

### 6.2 依賴 Resource Management 的系統（下游）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| C-06 World Danger System | 啟動時與危險度升級時，主動呼叫 `SetBankruptcyThreshold(maxDebt)` 推送門檻更新 | `SetBankruptcyThreshold` |
| FT-04 Outcome Resolution | 結算後增減金幣與聲望 | `AddGold`, `AddReputation` |
| FT-05 Guild Gold Flow | 預收傭金、退還、結算入帳、賠償、維護費與薪水扣款 | `AddGoldAllowBankruptcy`, `CanAfford` |
| FT-06 Guild Core | 讀取聲望判定公會升等；訂閱 `OnBankruptcyWarningStateChanged(Bankrupt)` 進入 Game Over 終態流程（見 FT-06 §4.5、§5），Game Over 為終態，**不**呼叫 `ResetBankruptcyState()` | `GetReputation`, `OnBankruptcyWarningStateChanged` |
| FT-07 Guild Building | 建設前檢查能否支出、建設後扣款 | `CanAfford`, `AddGold` |
| FT-08 Guild Staff | 招募前檢查能否支出、招募後扣款 | `CanAfford`, `AddGold` |
| FT-01 Adventurer Recruitment | 老手邀請費用支出 | `CanAfford`, `AddGold` |
| FT-10 Save/Load | 序列化 `currentGold`、`currentReputation`、`_warningState`、`_bankruptcyWarningStartTime`、`_warningDurationSec`、`_currentBankruptcyThreshold` | `GetGold`, `GetReputation`, `GetBankruptcyWarningState` |
| P-02 Main UI | 訂閱資源變動事件與破產警告狀態事件，即時更新顯示 | `OnGoldChanged`, `OnReputationChanged`, `OnBankruptcyWarningStateChanged`, `GetBankruptcyWarningRemainingSeconds` |

### 6.3 循環依賴注意

Resource Management 依賴 F-02 Time System 的 tick 事件，而 Time System 不依賴 Resource Management——**無循環依賴**。C-06 World Danger System 透過 `SetBankruptcyThreshold` 主動推送門檻至 F-03（觀察者模式的反向：由 Core 層 push 至 Foundation 層），F-03 不依賴 C-06——**無循環依賴、無反向依賴**。

### 6.4 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"f03Resources"` |
| `IsCritical` | `true`（金幣/聲望缺失導致後續 owner 還原產生不可預測狀態，整檔回退） |

**`Serialize()` 序列化欄位**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `currentGold` | `int` | 當前金幣 |
| `currentReputation` | `int` | 當前聲望 |
| `_warningState` | `string`（`"Normal"` / `"Warning"` / `"Bankrupt"`） | 破產警告狀態 |
| `_bankruptcyWarningStartTime` | `long` | Warning 開始的 UTC Unix 秒；Normal 時為 0 |
| `_warningDurationSec` | `long` | Warning 鎖定的倒數秒數 |
| `_currentBankruptcyThreshold` | `int` | 當前破產門檻（由 C-06 推送，預設 -100） |
| `_currentWarningDuration` | `long` | 上游推送的最新破產倒數秒數（由 FT-07 推送，預設 86400） |

不序列化：`_isProcessing`（runtime 旗標，還原後重置為 `false`）。

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化上述 7 個欄位。
2. 驗證 `_warningState` ∈ `{"Normal", "Warning", "Bankrupt"}`；違規拋例外（觸發整檔回退）。
3. 還原完成，不補發任何事件（UI 由 Bootstrap 後的首次查詢刷新）。

**`InitializeAsNewGame()` 預設值**：

| 欄位 | 初始值 |
|---|---|
| `currentGold` | `GOLD_INITIAL`（SystemConstants，預設 100） |
| `currentReputation` | `0` |
| `_warningState` | `"Normal"` |
| `_bankruptcyWarningStartTime` | `0` |
| `_warningDurationSec` | `0` |
| `_currentBankruptcyThreshold` | `-100` |
| `_currentWarningDuration` | `86400` |

對應 FT-10 §3.3.3 拓撲順序 row 1、§3.3.4 Critical 分類、§6.1 #3（FT-10 設計來源清單）。

## 7. 可調參數（Tuning Knobs）

### 7.1 全域常數（SystemConstants.csv）

| 參數 | 預設值 | 安全調整範圍 | 影響 |
|------|--------|------------|------|
| `GOLD_INITIAL` | `100` | `50 ~ 500` | 玩家起始資金；過低開局壓力過大，過高失去早期張力 |
| `GOLD_MAX` | `9,999,999` | 固定，不調整 | 金幣上限；視覺與資料型別安全邊界 |
| `REPUTATION_MIN` | `-100` | 固定，不調整 | 聲望下限 |
| `REPUTATION_MAX` | `100` | 固定，不調整 | 聲望上限 |

### 7.2 破產警告期表（BankruptcyThresholdTable.csv）——已 deprecated，保留為設計參考

> **Phase 2 變更**：破產倒數秒數改為依預備金保險櫃等級（FT-07 buildingID=5）動態推送，本表 `warningDurationSec` 欄位**不再 runtime 查詢**；F-03 以 `_currentWarningDuration`（由 FT-07 透過 `SetBankruptcyWarningDuration` 推送）取代。聲望標籤需求改由 `ReputationLabelTable` 提供，不依賴本表。本表保留作為設計參考（早期聲望 - 警告期對應意圖），**不被 runtime 邏輯使用**。

金幣硬性下限（`maxDebt`）改由 C-06 `WorldDangerTable` 的 `maxDebt` 欄位統一管控，不在此表設定。

| reputationMin | reputationMax | warningDurationSec | 說明（設計參考，非 runtime 值） |
|---------------|---------------|-------------------|------|
| -100 | -1 | 86400 | 惡名聲望：原設計 24h 警告 |
| 0 | 19 | 172800 | 無名公會：原設計 48h 警告 |
| 20 | 39 | 259200 | 初具規模：原設計 72h 警告 |
| 40 | 59 | 259200 | 具名公會：原設計 72h 警告 |
| 60 | 79 | 345600 | 知名公會：原設計 96h 警告 |
| 80 | 100 | 604800 | 頂級公會：原設計 7d 警告 |

**注意**：以上數值僅供設計回顧參考；實際破產倒數秒數以 FT-07 `BuildingTable[5]`（預備金保險櫃各等級 `effectValue`）為準（L1=10800s ~ L5=172800s）。

## 8. 驗收標準（Acceptance Criteria）

| ID | 測試項目 | 通過條件 |
|----|---------|---------|
| AC-RM-01 | `AddGold` 拒絕邏輯 | 呼叫 `AddGold(-9999)` 使金幣低於當前破產門檻時，回傳 `false`，金幣不變，不廣播事件 |
| AC-RM-02 | `AddGold` 上限截斷 | 金幣為 `9,000,000` 時呼叫 `AddGold(2,000,000)`，金幣截斷為 `9,999,999`，`actualDelta = 999,999`，事件廣播正確 delta |
| AC-RM-03 | `AddGold` 成功廣播 | 合法呼叫 `AddGold(100)` 後，`OnGoldChanged(newValue, delta)` 被廣播一次，值正確 |
| AC-RM-04 | 聲望 clamp | `AddReputation(50)` 在聲望 `80` 時，聲望變為 `100`，delta = `20`，不報錯 |
| AC-RM-05 | 進入 Warning | 金幣從 `50` 呼叫 `AddGold(-100)` 後（金幣 = -50），狀態轉為 `Warning`，廣播 `OnBankruptcyWarningStateChanged(Warning)` |
| AC-RM-06 | Warning 期間正常遊玩 | `Warning` 狀態下，`CanAfford`、`AddGold`（不低於門檻的）、`AddReputation` 均正常執行，無額外限制 |
| AC-RM-07 | Warning 自然恢復 | `Warning` 狀態下呼叫 `AddGold(100)` 使金幣 >= 0，狀態回到 `Normal`，計時器清除 |
| AC-RM-08 | Warning 不重置計時器 | `Warning` 狀態下呼叫 `AddGold(-10)`（金幣更負），`_bankruptcyWarningStartTime` 不變 |
| AC-RM-09 | 倒數到期觸發 Bankrupt | 模擬 `_warningDurationSec` 秒流逝後（gold 仍 < 0），狀態轉為 `Bankrupt`，廣播事件 |
| AC-RM-10 | 上游推送更新債務上限 | 上游呼叫 `SetBankruptcyThreshold(-500)` 後，`GetCurrentBankruptcyThreshold()` 回傳 `-500`；原 `Warning` 狀態下 gold = -450（在新範圍內）→ 維持 `Warning`，不觸發 `Bankrupt` |
| AC-RM-11 | 離線破產判定 | 離線超過 `_warningDurationSec` 且 gold < 0，重啟後離線摘要結算完成時觸發 `Bankrupt` |
| AC-RM-12 | 離線恢復正值不破產 | 離線期間任務結算使 gold 回正，即使倒數已到期，重啟後狀態為 `Normal` |
| AC-RM-13 | 多次進出 Warning | 金幣正負振盪 3 次，每次從正轉負以當下 `_currentWarningDuration` 鎖定新的 `_warningDurationSec` 並開始新計時器 |
| AC-RM-14 | 存檔還原 Warning 狀態 | 存檔時 gold = -50（Warning），重新載入後 `GetBankruptcyWarningState()` = `Warning`，剩餘秒數正確減少 |
| AC-RM-15 | `ResetBankruptcyState` | 呼叫 `ResetBankruptcyState()` 後，狀態回到 `Normal`，時間戳清除，廣播事件 |
| AC-RM-16 | `AddGoldAllowBankruptcy` 成功廣播 | 金幣為 `100` 時呼叫 `AddGoldAllowBankruptcy(-50)` → 金幣 = `50`，`OnGoldChanged(50, -50)` 廣播一次 |
| AC-RM-17 | `AddGoldAllowBankruptcy` 跳過 threshold reject | 金幣為 `-50`（threshold = `-100`）時呼叫 `AddGoldAllowBankruptcy(-200)` → 金幣 = `-250`（< threshold），立即轉 `Bankrupt`，廣播 `OnBankruptcyWarningStateChanged(Bankrupt)`；`AddGold(-200)` 在相同條件下應回傳 `false`、金幣不變（對照組） |
| AC-RM-18 | `AddGoldAllowBankruptcy` 仍 clamp 上限 | 金幣為 `9,000,000` 時呼叫 `AddGoldAllowBankruptcy(2,000,000)` → 金幣 = `9,999,999`，`actualDelta = 999,999`，事件 delta 正確 |
| AC-RM-19 | `AddGoldAllowBankruptcy` 單次不穿越零線兩次 | 於各種 `prevGold` 與 `amount` 組合下，呼叫後事件廣播次數至多為 1 次 `OnGoldChanged` + 至多 1 次 `OnBankruptcyWarningStateChanged`（保證單次呼叫至多一次狀態過渡） |
| AC-RM-20 | `SetBankruptcyThreshold` 初始預設值 | 啟動後若無任何推送，`GetCurrentBankruptcyThreshold()` 回傳預設 `-100`；首次呼叫 `SetBankruptcyThreshold(-500)` 後回傳 `-500` |
| AC-RM-21 | `SetBankruptcyThreshold` 觸發狀態校正 | 金幣為 `-150` 且狀態為 `Warning`（既有門檻 `-200`），呼叫 `SetBankruptcyThreshold(-100)`（門檻緊縮至 `-100`）後，因金幣 `< newThreshold`，`EvaluateWarningState` 將狀態轉為 `Bankrupt`，廣播事件 |
| AC-RM-22 | `SetBankruptcyWarningDuration` 基本行為 | 啟動後無推送時，`GetBankruptcyWarningDuration()` 回傳預設 `86400`；呼叫 `SetBankruptcyWarningDuration(172800)` 後，`GetBankruptcyWarningDuration()` 回傳 `172800`；此後金幣從正轉負進入 `Warning`，`_warningDurationSec` 鎖定為 `172800` |
| AC-RM-23 | Warning 鎖定後再推送不影響已鎖定值 | 金幣進入 `Warning`（已鎖定 `_warningDurationSec = 172800`）後，呼叫 `SetBankruptcyWarningDuration(10800)`，`_warningDurationSec` 維持 `172800`（鎖定不變）；金幣回正清除 `Warning` 後再次進入負值，新的 `_warningDurationSec` 鎖定為 `10800` |
