# Resource Management 系統設計文件

_建立時間：2026-04-19_
_狀態：設計中_
_系統 ID：F-03_

---

## 1. 概要（Overview）

Resource Management 是遊戲中金幣與聲望兩項核心資源的唯一管理者。所有系統若需增減資源，必須透過 Resource Management 的 API（`AddGold`、`AddReputation`）執行，不得直接修改資源值。金幣範圍為破產門檻（依當前聲望動態查詢）至上限 9,999,999；聲望限制在 -100 至 100 之間。金幣進入負值時啟動破產倒數警告，倒數期長度與破產門檻皆由 `BankruptcyThresholdTable` 依聲望段查詢。Resource Management 同時負責在資源變動時廣播事件，供 UI 與其他系統即時更新。

## 2. 玩家幻想（Player Fantasy）

金幣是公會存亡的命脈——不是抽象數字，而是每一個決策的重量。玩家接下高難度委託時感受到的緊張，來自「如果冒險者失敗，我要賠錢」的真實壓力。負債狀態不是遊戲結束，而是一種絕境感：你必須接下更多委託才能翻身，而每一單都是賭注。聲望則是公會在世界上的臉面——不僅影響委託品質與冒險者意願，更決定你能承受多少債務。高聲望帶來更寬裕的緩衝，但聲望崩盤時，債務壓力會立刻撲面而來。資源管理的張力正是這款遊戲「有重量的決策」支柱的數值基礎。

## 3. 詳細規則（Detailed Rules）

### 3.1 資源種類

| 資源 | 型別 | 初始值 | 範圍 |
|------|------|--------|------|
| 金幣（Gold） | `int` | `100` | `GetCurrentBankruptcyThreshold()` ~ `GOLD_MAX (9,999,999)` |
| 聲望（Reputation） | `int` | `0` | `-100` ~ `100` |

### 3.2 金幣規則

1. 所有金幣增減透過 `AddGold(int amount)` 執行，`amount` 可為正（收入）或負（支出）
2. 執行前檢查：`currentGold + amount < GetCurrentBankruptcyThreshold()` → 拒絕操作，回傳 `false`，不執行增減
3. 金幣上限為 `GOLD_MAX = 9,999,999`；超過上限時靜默截斷（clamp），不拒絕操作
4. 負值為**債務狀態**，不影響任務接受，但建設/招募等主動支出仍受破產門檻限制
5. 破產門檻由 `BankruptcyThresholdTable` 依當前聲望查詢（`GetCurrentBankruptcyThreshold()`），聲望變動時門檻即時更新

### 3.3 聲望規則

1. 所有聲望增減透過 `AddReputation(int amount)` 執行
2. 聲望結果 `clamp` 至 `-100 ~ 100`，不報錯
3. 聲望標籤（label）由當前聲望值對應 `ReputationLabelTable` 查詢，Resource Management 不直接維護標籤狀態
4. `AddReputation` 執行後，立即重新查詢 `GetCurrentBankruptcyThreshold()`；若 `currentGold < newThreshold` 且 `currentGold < 0` → 立即觸發 `Bankrupt`

### 3.4 事件廣播

1. `AddGold` 執行成功後，廣播 `OnGoldChanged(int newValue, int delta)`
2. `AddReputation` 執行後，廣播 `OnReputationChanged(int newValue, int delta)`
3. 破產警告狀態變更時，廣播 `OnBankruptcyWarningStateChanged(BankruptcyWarningState newState)`
4. 任何系統不得跳過 API 直接修改資源值
5. **重入防護**：`AddGold` 與 `AddReputation` 內部使用 `_isProcessing` flag 防止事件訂閱者在 callback 中再次呼叫同一 API 造成遞迴。若偵測到重入，`Debug.LogError` 並拒絕操作
6. 事件訂閱與退訂在 `OnEnable` / `OnDisable` 中管理（訂閱 `TimeSystem.OnSecondTick`、`TimeSystem.OnOfflineResolved`）

### 3.5 查詢 API

1. `GetGold() : int` — 取得當前金幣
2. `GetReputation() : int` — 取得當前聲望
3. `CanAfford(int amount) : bool` — 檢查是否能支出 `amount`（考慮破產門檻）
4. `GetCurrentBankruptcyThreshold() : int` — 依當前聲望查詢 `BankruptcyThresholdTable`，回傳破產門檻
5. `GetBankruptcyWarningState() : BankruptcyWarningState` — 取得當前破產警告狀態
6. `GetBankruptcyWarningRemainingSeconds() : long` — 取得破產倒數剩餘秒數；非 `Warning` 狀態時回傳 `0`

### 3.6 破產警告（Bankruptcy Warning）

破產警告是金幣進入負值後的緩衝機制。玩家在此期間可以完全正常遊玩，但 UI 持續顯示警告與倒數計時，直到觸發恢復或破產。

#### 狀態定義

| 狀態（`BankruptcyWarningState`） | 條件 |
|--------------------------------|------|
| `Normal` | `gold >= 0` |
| `Warning` | `GetCurrentBankruptcyThreshold() <= gold < 0`，且倒數未到期 |
| `Bankrupt` | 倒數到期時 `gold < 0`，或 `gold < GetCurrentBankruptcyThreshold()`（因聲望下滑觸發） |

#### 規則

1. 當 `AddGold` 執行後 `currentGold < 0`（且先前狀態為 `Normal`），進入 `Warning` 狀態
2. 進入 `Warning` 時：記錄 `_bankruptcyWarningStartTime`（UTC Unix timestamp），並從 `BankruptcyThresholdTable` 查詢當前聲望段的 `warningDurationSec`，鎖定為 `_warningDurationSec`（中途聲望變動不改變此值）
3. 警告期間玩家可執行所有操作，不受任何限制
4. 每次 `AddGold` 後：
   - 若 `currentGold >= 0` → 狀態回復為 `Normal`，清除 `_bankruptcyWarningStartTime` 與 `_warningDurationSec`，廣播 `OnBankruptcyWarningStateChanged(Normal)`
   - 若 `currentGold < 0` 且仍在倒數期內 → 維持 `Warning`，**不重置**計時器
5. Time System 每秒 tick 時，Resource Management 檢查：若 `currentUTC - _bankruptcyWarningStartTime >= _warningDurationSec`，且 `currentGold < 0` → 轉為 `Bankrupt`，廣播 `OnBankruptcyWarningStateChanged(Bankrupt)`
6. `AddReputation` 執行後，若新門檻高於 `currentGold`（且 `currentGold < 0`）→ 立即轉為 `Bankrupt`，跳過倒數
7. 離線回補時，若離線期間倒數已到期且金幣仍為負值 → 於離線摘要結算後立即判定 `Bankrupt`
8. `Bankrupt` 狀態的後果由上層系統（破產機制）處理；Resource Management 僅負責廣播狀態，不自行執行懲罰
9. 上層系統解除破產後，呼叫 `ResetBankruptcyState()`，Resource Management 回到 `Normal`
10. **狀態一致性保證**：在以下時機點執行 `EvaluateWarningState()` 進行狀態校正——(a) 存檔載入後、(b) 離線結算完成後、(c) `AddReputation` 執行後。此方法檢查 `currentGold < 0` 但 `_warningState == Normal` 的不一致狀態，並根據當前條件修正為 `Warning` 或 `Bankrupt`

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
    currentGold = (int)clamp(newGold, threshold, GOLD_MAX)
    actualDelta = currentGold - prevGold
    broadcast OnGoldChanged(currentGold, actualDelta)
    UpdateBankruptcyWarning(prevGold, currentGold)

    _isProcessing = false
    return true
```

> 注意：`actualDelta` 為實際生效的變動量（可能因 GOLD_MAX clamp 而小於傳入的 `amount`）。
> 注意：中間計算使用 `long` 避免極端輸入（如 `amount = int.MaxValue`）造成 `int` 溢位。

### 4.2 聲望增減

```
AddReputation(amount):
    newValue = clamp(currentReputation + amount, REPUTATION_MIN, REPUTATION_MAX)
    delta = newValue - currentReputation
    currentReputation = newValue
    broadcast OnReputationChanged(currentReputation, delta)

    // 聲望變動後立即檢查破產門檻
    newThreshold = GetCurrentBankruptcyThreshold()
    if currentGold < 0 and currentGold < newThreshold:
        TriggerBankruptcy()
```

> 注意：`delta` 為實際生效的變動量（可能因 clamp 而小於傳入的 `amount`）。

### 4.3 支出能力檢查

```
CanAfford(amount):
    return currentGold - amount >= GetCurrentBankruptcyThreshold()
```

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
        _warningDurationSec = BankruptcyThresholdTable.GetWarningDuration(currentReputation)
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
OnOfflineResolved():
    if _warningState != Warning: return
    if currentGold < 0:
        elapsed = currentUTCTimestamp - _bankruptcyWarningStartTime
        if elapsed >= _warningDurationSec:
            TriggerBankruptcy()

// 上層系統解除破產後呼叫
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
        _warningDurationSec = BankruptcyThresholdTable.GetWarningDuration(currentReputation)
        broadcast OnBankruptcyWarningStateChanged(Warning)
```

### 4.6 破產門檻查詢

```
GetCurrentBankruptcyThreshold():
    return BankruptcyThresholdTable
        .Where(r => currentReputation >= r.reputationMin
                 && currentReputation <= r.reputationMax)
        .bankruptcyThreshold
```

## 5. 邊緣案例（Edge Cases）

### 5.1 金幣

| 情況 | 處理方式 |
|------|---------|
| `AddGold(-500)` 會使金幣低於破產門檻 | 拒絕操作，回傳 `false`，金幣不變，呼叫方負責處理拒絕 |
| `AddGold(0)` | 允許，`actualDelta = 0`，廣播 `OnGoldChanged(currentGold, 0)` |
| `AddGold(9999999)` 使金幣超過 `GOLD_MAX` | 靜默 clamp 至 `9,999,999`，`actualDelta` 為實際增量，不報錯 |
| 傭金收入使負債金幣回正 | 正常執行，同步清除 Warning 狀態 |

### 5.2 聲望

| 情況 | 處理方式 |
|------|---------|
| `AddReputation(50)` 但聲望已為 `80`（會超過 100） | clamp 至 100，`delta = 20`，不報錯 |
| `AddReputation(0)` | 允許，delta = 0，廣播事件，仍執行門檻重算 |
| 聲望已達 `-100` 再扣減 | clamp 至 -100，delta = 0 |
| 聲望下滑使門檻收緊，`currentGold` 已低於新門檻 | 立即觸發 `Bankrupt`，跳過倒數 |

### 5.3 初始化

| 情況 | 處理方式 |
|------|---------|
| 首次啟動（無存檔） | 金幣初始化為 `100`，聲望初始化為 `0`，警告狀態為 `Normal` |
| 載入存檔後，金幣為負值且 `_bankruptcyWarningStartTime` 存在 | 以存檔時間戳恢復 `Warning` 狀態，繼續倒數 |
| 載入存檔後，金幣為正值，但存檔中保有 `_bankruptcyWarningStartTime` | 忽略，清除時間戳，狀態為 `Normal` |

### 5.4 破產警告

| 情況 | 處理方式 |
|------|---------|
| 金幣從正值降為負值 | 進入 `Warning`，記錄起始時間與當下聲望段的 `warningDurationSec` |
| 金幣在 Warning 期間再次減少（仍為負值） | 維持 `Warning`，**不重置**計時器 |
| 金幣在 Warning 期間恢復至 `>= 0` | 回到 `Normal`，清除計時器 |
| 金幣正負反覆振盪（多次進出 Warning） | 每次從正轉負，以**當下**聲望段重新查詢 `warningDurationSec` 並開始新計時器 |
| 聲望於 Warning 期間上升（門檻寬鬆） | 不影響已鎖定的 `_warningDurationSec`；若 `currentGold >= newThreshold`，邏輯不變（gold 仍為負，繼續倒數） |
| 聲望於 Warning 期間下滑（門檻收緊），`currentGold < newThreshold` | 立即觸發 `Bankrupt`，跳過剩餘倒數 |
| 離線期間倒數已到期，金幣仍為負值 | 離線摘要結算後立即判定 `Bankrupt` |
| 離線期間金幣因結算恢復正值，但倒數本已到期 | 以金幣狀態為準：`gold >= 0` → `Normal`（不觸發 Bankrupt） |

## 6. 依賴關係（Dependencies）

### 6.1 Resource Management 的依賴（上游）

| 系統 | 用途 |
|------|------|
| F-01 DataManager | 讀取 `REPUTATION_MIN`、`REPUTATION_MAX`、`GOLD_MAX` 常數；查詢 `BankruptcyThresholdTable`、`ReputationLabelTable` |
| F-02 Time System | 訂閱 `OnSecondTick` 驅動破產倒數檢查；訂閱 `OnOfflineResolved`（玩家確認離線摘要後）執行離線破產判定 |

### 6.2 依賴 Resource Management 的系統（下游）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| FT-04 Outcome Resolution | 結算後增減金幣與聲望 | `AddGold`, `AddReputation` |
| FT-05 Commission Flow | 預收傭金、退還、賠償 | `AddGold`, `CanAfford` |
| FT-06 Guild Core | 讀取聲望判定公會升等 | `GetReputation` |
| FT-07 Guild Building | 建設前檢查能否支出、建設後扣款 | `CanAfford`, `AddGold` |
| FT-08 Guild Staff | 招募前檢查能否支出、招募後扣款 | `CanAfford`, `AddGold` |
| FT-01 Adventurer Recruitment | 老手邀請費用支出 | `CanAfford`, `AddGold` |
| FT-10 Save/Load | 序列化 `currentGold`、`currentReputation`、`_warningState`、`_bankruptcyWarningStartTime`、`_warningDurationSec` | `GetGold`, `GetReputation`, `GetBankruptcyWarningState` |
| P-02 Main UI | 訂閱資源變動事件與破產警告狀態事件，即時更新顯示 | `OnGoldChanged`, `OnReputationChanged`, `OnBankruptcyWarningStateChanged`, `GetBankruptcyWarningRemainingSeconds` |
| FT-06 Guild Core | 訂閱 `OnBankruptcyWarningStateChanged(Bankrupt)`，執行破產後果（遊戲結束流程）；解除破產後呼叫 `ResetBankruptcyState()` | `OnBankruptcyWarningStateChanged`, `ResetBankruptcyState` |

### 6.3 循環依賴注意

Resource Management 依賴 F-02 Time System 的 tick 事件，而 Time System 不依賴 Resource Management——**無循環依賴**。Resource Management 同時管理金幣與聲望，`AddReputation` 後自行觸發門檻重算，無需訂閱外部事件。

## 7. 可調參數（Tuning Knobs）

### 7.1 全域常數（SystemConstants.csv）

| 參數 | 預設值 | 安全調整範圍 | 影響 |
|------|--------|------------|------|
| `GOLD_INITIAL` | `100` | `50 ~ 500` | 玩家起始資金；過低開局壓力過大，過高失去早期張力 |
| `GOLD_MAX` | `9,999,999` | 固定，不調整 | 金幣上限；視覺與資料型別安全邊界 |
| `REPUTATION_MIN` | `-100` | 固定，不調整 | 聲望下限 |
| `REPUTATION_MAX` | `100` | 固定，不調整 | 聲望上限 |

### 7.2 破產門檻表（BankruptcyThresholdTable.csv）

每個聲望段獨立設定破產門檻與警告期。調整時應維持「聲望越高，容忍債務越多，警告期越長」的單調遞增關係。

| reputationMin | reputationMax | bankruptcyThreshold | warningDurationSec | 說明 |
|---------------|---------------|---------------------|-------------------|------|
| -100 | -1 | -50 | 86400 | 惡名聲望：緩衝最少，24h 警告 |
| 0 | 19 | -100 | 172800 | 無名公會：標準起點，48h 警告 |
| 20 | 39 | -200 | 259200 | 初具規模：中等緩衝，72h 警告 |
| 40 | 59 | -400 | 259200 | 具名公會：加大緩衝，72h 警告 |
| 60 | 79 | -700 | 345600 | 知名公會：大緩衝，96h 警告 |
| 80 | 100 | -1000 | 604800 | 頂級公會：最大緩衝，7d 警告 |

**調整原則**：
- `bankruptcyThreshold`：安全範圍 `-2000 ~ -30`；不可為正值
- `warningDurationSec`：安全範圍 `43200 ~ 604800`（12h ~ 7d）；過短玩家無法反應，過長失去張力

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
| AC-RM-10 | 聲望下滑即時破產 | `Warning` 狀態下（gold = -150），降低聲望使門檻從 -200 收緊至 -100，立即廣播 `Bankrupt`，不等待倒數 |
| AC-RM-11 | 離線破產判定 | 離線超過 `_warningDurationSec` 且 gold < 0，重啟後離線摘要結算完成時觸發 `Bankrupt` |
| AC-RM-12 | 離線恢復正值不破產 | 離線期間任務結算使 gold 回正，即使倒數已到期，重啟後狀態為 `Normal` |
| AC-RM-13 | 多次進出 Warning | 金幣正負振盪 3 次，每次從正轉負重新查詢當下聲望段的 `warningDurationSec` 並開始新計時器 |
| AC-RM-14 | 存檔還原 Warning 狀態 | 存檔時 gold = -50（Warning），重新載入後 `GetBankruptcyWarningState()` = `Warning`，剩餘秒數正確減少 |
| AC-RM-15 | `ResetBankruptcyState` | 呼叫 `ResetBankruptcyState()` 後，狀態回到 `Normal`，時間戳清除，廣播事件 |
