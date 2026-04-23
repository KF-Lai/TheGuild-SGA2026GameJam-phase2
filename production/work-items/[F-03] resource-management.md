# 工項需求書：F-03 Resource Management

_建立時間：2026-04-23_
_系統 ID：F-03_
_分級：**Large**_（新 Foundation 系統；金幣／聲望唯一管理者，多下游依賴破產狀態）
_對應 GDD：`design/GDD/[F-03] resource-management.md`_
_交付對象：Codex（實作）_

---

## 1. 工項概要（Summary）

實作金幣與聲望唯一管理者 `ResourceManagement`：Singleton MonoBehaviour，負責金幣（可負值，有動態硬下限）與聲望（`-100 ~ 100`）的統一管理；提供 `AddGold`（嚴格檢查破產門檻）、`AddGoldAllowBankruptcy`（FT-05 專用，允許跨越門檻）、`AddReputation`、`CanAfford`、`SetBankruptcyThreshold`（上游推送門檻）等 API；維護破產警告狀態機（`Normal` / `Warning` / `Bankrupt`），訂閱 `OnSecondTick` 驅動倒數檢查、訂閱 `OnOfflineResolved` 執行離線破產判定；所有資源變動透過 EventBus 廣播。

**分層原則**：F-03 作為 Foundation 層，**不主動依賴任何 Core 層系統**；破產門檻由上游（典型為 C-06 World Danger System）透過 `SetBankruptcyThreshold` API 被動推送。

**交付流程**：階段一 read-only → 審查 → 階段二 workspace-write；最多 2 次來回。

---

## 2. 目標與範圍（Goals & Scope）

### 2.1 In Scope

- `ResourceManagement.cs`（Singleton MonoBehaviour）
- `BankruptcyWarningState` enum（由本工項**唯一擁有**）
- 金幣 API：`AddGold`、`AddGoldAllowBankruptcy`、`GetGold`、`CanAfford`（皆含重入防護）
- 聲望 API：`AddReputation`、`GetReputation`
- 破產門檻 API：`SetBankruptcyThreshold`、`GetCurrentBankruptcyThreshold`（預設 `-100`）
- 破產警告狀態機：`Normal` / `Warning` / `Bankrupt`；`EvaluateWarningState()` 統一狀態校正入口
- 破產倒數：訂閱 `OnSecondTick` 驅動；`GetBankruptcyWarningRemainingSeconds()` 查詢
- 離線破產判定：訂閱 `OnOfflineResolved` 執行，**一律呼叫** `EvaluateWarningState()`（對應 GDD §3.6 rule 10 (b)）
- 保留 API：`ResetBankruptcyState()`（本 Game Jam 無呼叫者，但需實作可用）
- 事件廣播：`OnGoldChanged`、`OnReputationChanged`、`OnBankruptcyWarningStateChanged`
- 單元測試涵蓋 AC-RM-01~AC-RM-24

### 2.2 Out of Scope

- **C-06 World Danger System**：本工項不實作 C-06；`SetBankruptcyThreshold` 僅由測試模擬呼叫
- **聲望標籤（`ReputationLabelTable`）維護**：F-03 不查詢標籤，標籤由 UI 或 FT-06 自行透過 DataManager 查（刻意與 F-03 GDD §6.1 中提到的 `ReputationLabelTable` 解耦，對應 GDD §3.3 rule 3「F-03 不直接維護標籤狀態」——此處明示解決 GDD §6.1 提及依賴但 §3.3 又聲明不維護的內部語意張力）
- **Save/Load 整合**：僅提供序列化所需的 getter，實際讀寫由 FT-10 處理
- **破產後果執行**：F-03 僅廣播 `Bankrupt` 狀態，Game Over 流程由 FT-06 處理

---

## 3. 依賴與前置條件（Dependencies & Preconditions）

### 3.1 上游依賴

| 依賴項 | 狀態 | 備註 |
|-------|------|------|
| F-01 DataManager | ⚠️ **必須先完成** | 讀 `GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX`；查 `BankruptcyThresholdTable` |
| F-02 Time System | ⚠️ **必須先完成** | 訂閱 `OnSecondTick` 驅動破產倒數；訂閱 `OnOfflineResolved` 執行離線判定；讀 `NowUTC` 寫入 `_bankruptcyWarningStartTime` |
| EventBus（F-02 產出） | ⚠️ **必須先完成** | 事件訂閱／發布基礎設施 |
| `BankruptcyThresholdTable.csv`（測試用） | ⚠️ 由本工項產出 | 生產版由 systems-designer 填充 |
| `SystemConstants.csv`（擴充） | ⚠️ 由本工項產出測試用 CSV | 追加 `GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX` |

**硬依賴**：F-01 與 F-02 必須在 F-03 啟動前完成並通過驗收。

### 3.2 下游影響（不可破壞的契約）

| 下游系統 | 依賴介面 |
|---------|---------|
| C-06 World Danger | `SetBankruptcyThreshold`（主動推送） |
| FT-04 Outcome Resolution | `AddGold`、`AddReputation` |
| FT-05 Guild Gold Flow | `AddGoldAllowBankruptcy`、`CanAfford`（**唯一使用 `AllowBankruptcy` 的系統**） |
| FT-06 Guild Core | `GetReputation`、訂閱 `OnBankruptcyWarningStateChanged(Bankrupt)` |
| FT-07 Guild Building | `CanAfford`、`AddGold` |
| FT-08 Guild Staff | `CanAfford`、`AddGold` |
| FT-01 Recruitment | `CanAfford`、`AddGold` |
| FT-10 Save/Load | 序列化 6 個欄位（見 §4.11） |
| P-02 Main UI | `OnGoldChanged`、`OnReputationChanged`、`OnBankruptcyWarningStateChanged`、`GetBankruptcyWarningRemainingSeconds` |

---

## 4. 實作要求（Implementation Requirements）

### 4.1 檔案位置

```
TheGuild-unity/Assets/Scripts/Gameplay/Resources/
├── ResourceManagement.cs           # Singleton MonoBehaviour
├── BankruptcyWarningState.cs       # enum（唯一定義處）
├── BankruptcyThresholdData.cs      # POCO，對應 BankruptcyThresholdTable 欄位
├── ResourceSnapshot.cs             # 序列化 DTO（供 FT-10）
└── Events/
    └── ResourceEvents.cs           # 本層事件：OnGoldChangedEvent / OnReputationChangedEvent / OnBankruptcyWarningStateChangedEvent

TheGuild-unity/Assets/Scripts/Tests/EditMode/Gameplay/Resources/
├── ResourceManagementTests.cs
└── TestResources/
    ├── SystemConstants.csv         # 擴充版（追加金幣/聲望常數）
    └── BankruptcyThresholdTable.csv

TheGuild-unity/Assets/Scripts/Tests/PlayMode/Gameplay/Resources/
└── ResourceManagementPlayModeTests.cs  # OnSecondTick 驅動的倒數測試
```

**分層原則（關鍵）**：
- 路徑遵循 `directory-structure.md`——`ResourceManagement` 屬 Gameplay（與金幣經濟相關），不屬 Core 基礎設施
- `BankruptcyWarningState` enum **僅在本工項定義**；F-02 工項書已修訂為不建立占位檔
- **F-03 事件（`OnGoldChangedEvent` 等）放在 `Gameplay/Resources/Events/ResourceEvents.cs`，不放在 `Core/Events/GameEvents.cs`**——避免 Core/Events 依賴 Gameplay/Resources 的 `BankruptcyWarningState`
- 訂閱者（UI、FT-06 等）需 `using TheGuild.Gameplay.Resources.Events;`；F-03 自己使用事件亦然
- `Core/Events/GameEvents.cs` 保留給 Core 層事件（`OnSecondTickEvent` 等）

### 4.2 命名空間

```csharp
namespace TheGuild.Gameplay.Resources
```

### 4.3 列舉定義

```csharp
public enum BankruptcyWarningState
{
    Normal,
    Warning,
    Bankrupt
}
```

### 4.4 公開 API 簽名

```csharp
public sealed class ResourceManagement : MonoBehaviour
{
    public static ResourceManagement Instance { get; private set; }

    // 查詢
    public int GetGold();
    public int GetReputation();

    /// <summary>檢查是否能支出 amount；amount <= 0 視為「不會讓金幣下降」恆回 true。</summary>
    public bool CanAfford(int amount);
    public int GetCurrentBankruptcyThreshold();
    public BankruptcyWarningState GetBankruptcyWarningState();
    public long GetBankruptcyWarningRemainingSeconds();

    // 金幣寫入
    public bool AddGold(int amount);                       // 嚴格：跨越門檻時 reject
    public bool AddGoldAllowBankruptcy(int amount);        // FT-05 專用：允許跨越門檻

    // 聲望寫入
    public void AddReputation(int amount);

    // 門檻推送（上游主動呼叫）
    public void SetBankruptcyThreshold(int newThreshold);

    // 保留 API（Game Jam 無呼叫者）
    public void ResetBankruptcyState();

    // 序列化（供 FT-10）
    public ResourceSnapshot CreateSnapshot();
    public void RestoreSnapshot(ResourceSnapshot snapshot);
}

/// <summary>
/// 序列化用 DTO（可序列化普通 class），供 FT-10 Save/Load 使用。
/// 採 [System.Serializable] class 而非 readonly struct，確保 Unity JsonUtility 與一般序列化器相容。
/// </summary>
[System.Serializable]
public class ResourceSnapshot
{
    public int CurrentGold;
    public int CurrentReputation;
    public BankruptcyWarningState WarningState;
    public long BankruptcyWarningStartTime;
    public long WarningDurationSec;
    public int CurrentBankruptcyThreshold;
}
```

**`CanAfford` 邊界契約**：`amount <= 0` → 恆回 `true`（視為「不會讓金幣下降」；呼叫方傳入 0 或負值視為免費或加值操作）。

### 4.5 事件定義（`Gameplay/Resources/Events/ResourceEvents.cs`）

```csharp
namespace TheGuild.Gameplay.Resources.Events
{
    public readonly struct OnGoldChangedEvent
    {
        public int NewValue { get; }
        public int Delta { get; }      // 實際生效變動量（可能因 clamp 而小於輸入）；以 int 儲存，極端場景行為見 §4.8 AddGoldAllowBankruptcy
    }

    public readonly struct OnReputationChangedEvent
    {
        public int NewValue { get; }
        public int Delta { get; }
    }

    public readonly struct OnBankruptcyWarningStateChangedEvent
    {
        public BankruptcyWarningState NewState { get; }   // enum 定義於 TheGuild.Gameplay.Resources
    }
}
```

**分層理由**：若將事件放在 `Core/Events/GameEvents.cs`，會強制 `Core/Events` 引用 `TheGuild.Gameplay.Resources.BankruptcyWarningState`，違反 Foundation→Gameplay 的依賴方向。放回 Gameplay 層確保：
- Core 只依賴 Unity；Gameplay 可依賴 Core；反之不成立
- 所有 UI / FT-06 / FT-05 等訂閱者本就屬 Gameplay 或 Presentation 層，`using Gameplay.Resources.Events` 無分層問題

### 4.6 內部欄位

```csharp
private int _currentGold;
private int _currentReputation;
private BankruptcyWarningState _warningState = BankruptcyWarningState.Normal;
private long _bankruptcyWarningStartTime;   // 0 表示未在 Warning
private long _warningDurationSec;            // 進 Warning 時鎖定
private int _currentBankruptcyThreshold = -100;   // 預設值，由上游覆寫
private bool _isProcessing;                  // 重入防護（AddGold / AddGoldAllowBankruptcy / AddReputation 共用）
```

### 4.7 生命週期與初始化（對應 GDD §3.4 rule 7）

**`Awake` 完成所有初始化**（對應 GDD 明文「Awake 階段從 DataManager 讀取常數」）：

```
Awake:
    Singleton 檢查 → 重複則 Destroy
    DontDestroyOnLoad
    讀常數（DataManager）：
        _currentGold = DataManager.Instance.GetInt("GOLD_INITIAL")      // 預設 100
        _goldMax     = DataManager.Instance.GetInt("GOLD_MAX")          // 9,999,999
        _repMin      = DataManager.Instance.GetInt("REPUTATION_MIN")    // -100
        _repMax      = DataManager.Instance.GetInt("REPUTATION_MAX")    // 100
    _currentReputation = 0
    _currentBankruptcyThreshold = -100   // 預設值，等待上游推送覆寫
    _warningState = Normal
    // 不主動查詢 C-06，等待上游推送

Start:
    // 空實作；預留供未來擴充
    // C-06 在其 Start 階段會主動呼叫 SetBankruptcyThreshold；
    // F-03 Script Execution Order 早於 C-06，保證 C-06 Start 執行時 F-03 Instance 已就緒

OnEnable:
    EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick)
    EventBus.Subscribe<OnOfflineResolvedEvent>(HandleOfflineResolved)

OnDisable:
    EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick)
    EventBus.Unsubscribe<OnOfflineResolvedEvent>(HandleOfflineResolved)
```

**Script Execution Order**：
- `DataManager`：`-1000`
- `TimeSystem`：`-900`
- `ResourceManagement`：`-800`（`Awake` 早於 C-06 `-700`，保證 C-06 Start 時 F-03.Instance 已就緒）

### 4.8 核心演算法（對應 GDD §4.1~§4.6）

**AddGold**（§4.1）：

```
AddGold(amount) -> bool:
    if _isProcessing:
        Debug.LogError("ResourceManagement reentrancy detected in AddGold")
        return false
    _isProcessing = true
    try:
        threshold = _currentBankruptcyThreshold
        newGold = (long)_currentGold + (long)amount     // long 防溢位
        if newGold < threshold:
            return false

        prevGold = _currentGold
        _currentGold = (int)Math.Min(newGold, _goldMax)
        actualDelta = _currentGold - prevGold
        if actualDelta != 0:
            EventBus.Publish(new OnGoldChangedEvent(_currentGold, actualDelta))
            UpdateBankruptcyWarning(prevGold, _currentGold)
        return true
    finally:
        _isProcessing = false
```

**AddGoldAllowBankruptcy**（§4.1a）：

```
AddGoldAllowBankruptcy(amount) -> bool:
    if _isProcessing: LogError, return false
    _isProcessing = true
    try:
        // 以 long 計算中間值
        newGoldLong = (long)_currentGold + (long)amount

        // 下限保護：clamp 至 int.MinValue，防止 cast 溢位
        newGoldClamped = Math.Max(newGoldLong, (long)int.MinValue)

        prevGold = _currentGold
        _currentGold = (int)Math.Min(newGoldClamped, _goldMax)       // clamp 上限；下限保護

        // delta 計算：以 long 中間值取差，防止 int 相減溢位
        actualDeltaLong = (long)_currentGold - (long)prevGold

        // Delta 契約：事件 payload 型別為 int；極端場景（actualDeltaLong 超出 int 範圍）
        // → 截斷為 int.MinValue / int.MaxValue 並記錄 LogWarning，不 reject 操作
        int actualDelta;
        if (actualDeltaLong < int.MinValue):
            actualDelta = int.MinValue
            Debug.LogWarning($"ResourceManagement: AddGoldAllowBankruptcy delta 超出 int 下限（{actualDeltaLong}），截斷為 int.MinValue")
        elif (actualDeltaLong > int.MaxValue):
            actualDelta = int.MaxValue
            Debug.LogWarning($"ResourceManagement: AddGoldAllowBankruptcy delta 超出 int 上限，截斷")
        else:
            actualDelta = (int)actualDeltaLong

        if actualDelta != 0:
            EventBus.Publish(new OnGoldChangedEvent(_currentGold, actualDelta))
            EvaluateWarningState()   // 統一入口
        return true
    finally:
        _isProcessing = false
```

**極端值契約**：
- 一般業務邏輯不會觸及 `int.MinValue` 邊界（正常最大支出不超過 `GOLD_MAX`）
- 為防禦極端測試輸入（如 `AddGoldAllowBankruptcy(int.MinValue)`），中間以 `long` 運算避免溢位
- 若 `actualDelta` 超出 `int` 範圍：事件 payload 截斷為 `int.MinValue` / `int.MaxValue`，並 `LogWarning` 記錄實際值
- `AddGold` 分支因先做 `newGold < threshold` reject，自然避開此風險；不需溢位保護

**AddReputation**（§4.2）：

```
AddReputation(amount):
    if _isProcessing: LogError, return
    _isProcessing = true
    try:
        newValue = Clamp(_currentReputation + amount, _repMin, _repMax)
        delta = newValue - _currentReputation
        _currentReputation = newValue
        if delta != 0:
            EventBus.Publish(new OnReputationChangedEvent(_currentReputation, delta))
        EvaluateWarningState()
    finally:
        _isProcessing = false
```

**SetBankruptcyThreshold**（§4.6）：

```
SetBankruptcyThreshold(newThreshold):
    _currentBankruptcyThreshold = newThreshold
    EvaluateWarningState()
```

**UpdateBankruptcyWarning**（§4.5，AddGold 成功後呼叫；涵蓋進 Warning 與退出 Warning，**不**處理 Bankrupt）：

```
UpdateBankruptcyWarning(prevGold, newGold):
    if newGold >= 0:
        if _warningState == Warning:
            _warningState = Normal
            _bankruptcyWarningStartTime = 0
            _warningDurationSec = 0
            EventBus.Publish(new OnBankruptcyWarningStateChangedEvent(Normal))
        return

    // newGold < 0
    if _warningState == Normal:
        EnterWarning()
    // 若已在 Warning → 不重置計時器
```

**EnterWarning**（共用入口）：

```
EnterWarning():
    _warningState = Warning
    _bankruptcyWarningStartTime = TimeSystem.Instance.NowUTC
    _warningDurationSec = LookupWarningDuration(_currentReputation)
    EventBus.Publish(new OnBankruptcyWarningStateChangedEvent(Warning))
```

**EvaluateWarningState**（§4.5，統一狀態校正入口；四個時機呼叫：存檔載入、離線結算、`AddReputation`、`SetBankruptcyThreshold`、`AddGoldAllowBankruptcy`）：

```
EvaluateWarningState():
    threshold = _currentBankruptcyThreshold
    if _currentGold >= 0:
        if _warningState == Warning:
            _warningState = Normal
            _bankruptcyWarningStartTime = 0
            _warningDurationSec = 0
            EventBus.Publish(new OnBankruptcyWarningStateChangedEvent(Normal))
        return

    // _currentGold < 0
    if _currentGold < threshold:
        TriggerBankruptcy()
        return

    if _warningState == Normal:
        EnterWarning()
```

**HandleSecondTick**（§4.5，訂閱 `OnSecondTickEvent`）：

```
HandleSecondTick(OnSecondTickEvent evt):
    if _warningState != Warning: return
    elapsed = evt.NowUTC - _bankruptcyWarningStartTime
    if elapsed >= _warningDurationSec:
        TriggerBankruptcy()
```

**HandleOfflineResolved**（§4.5，對應 GDD §3.6 rule 10 (b)「離線結算完成後呼叫 `EvaluateWarningState`」）：

```
HandleOfflineResolved(OnOfflineResolvedEvent evt):
    // 先處理離線破產倒數到期判定（若適用）
    if _warningState == Warning and _currentGold < 0:
        elapsed = TimeSystem.Instance.NowUTC - _bankruptcyWarningStartTime
        if elapsed >= _warningDurationSec:
            TriggerBankruptcy()
            return   // 已進入終態，無需再 Evaluate

    // 離線期間金幣可能因結算回正或下跌至門檻以下 → 統一校正
    EvaluateWarningState()
```

**設計說明**：GDD §3.6 rule 10 明確列出四個校正時機：(a) 存檔載入後、(b) 離線結算完成後、(c) `AddReputation` 執行後、(d) `SetBankruptcyThreshold` 執行後。先前的工項書版本只在 `gold < 0 且倒數到期` 分支判 Bankrupt，未涵蓋 `gold 回正但狀態仍 Warning` 或 `gold < threshold` 的修正情境，違反 GDD 契約。修訂後一律呼叫 `EvaluateWarningState()`。

**TriggerBankruptcy**（冪等）：

```
TriggerBankruptcy():
    if _warningState == Bankrupt:
        return   // 已是 Bankrupt，不重複發事件（冪等）
    _warningState = Bankrupt
    _bankruptcyWarningStartTime = 0
    _warningDurationSec = 0
    EventBus.Publish(new OnBankruptcyWarningStateChangedEvent(Bankrupt))
```

**冪等原因**：`HandleOfflineResolved` 結束前會呼叫 `EvaluateWarningState()`；若已於先前分支觸發 Bankrupt，再次進入不應重複廣播。其他時機（`AddGoldAllowBankruptcy`、`HandleSecondTick`、`SetBankruptcyThreshold`）亦受此保護。

**GetBankruptcyWarningRemainingSeconds**：

```
GetBankruptcyWarningRemainingSeconds():
    if _warningState != Warning: return 0
    elapsed = TimeSystem.Instance.NowUTC - _bankruptcyWarningStartTime
    return Math.Max(0, _warningDurationSec - elapsed)
```

### 4.9 `BankruptcyThresholdTable` 查詢

```
LookupWarningDuration(reputation):
    // 從 DataManager.GetAll<BankruptcyThresholdData>() 取得所有段
    // 找出 reputation 落入的 [reputationMin, reputationMax] 段
    // 回傳該段的 warningDurationSec
    // 找不到（理論上不應發生）：回傳 86400 並 LogError
```

`BankruptcyThresholdData` POCO 欄位對應 CSV：

| 欄位 | 類型 | 說明 |
|------|------|------|
| `reputationMin` | `int` | 聲望段下界（PK） |
| `reputationMax` | `int` | 聲望段上界 |
| `warningDurationSec` | `long` | 警告倒數長度（秒） |

### 4.10 重入防護細節

- 三個寫入 API（`AddGold`、`AddGoldAllowBankruptcy`、`AddReputation`）共用 `_isProcessing` flag
- 訂閱者在 callback 中呼叫任一寫入 API → 偵測到 `_isProcessing == true` → `LogError` 並拒絕
- `try...finally` 確保 flag 復位

### 4.11 序列化契約（為 FT-10 預留）

`CreateSnapshot` 需輸出以下 6 個欄位，`RestoreSnapshot` 需反向還原：

1. `_currentGold`
2. `_currentReputation`
3. `_warningState`
4. `_bankruptcyWarningStartTime`
5. `_warningDurationSec`
6. `_currentBankruptcyThreshold`

**`RestoreSnapshot` 後必須呼叫 `EvaluateWarningState()`** 以校正還原狀態（對應 GDD §3.6 rule 10 的 (a) 存檔載入後時機）。

### 4.12 禁用事項

- 禁止外部直接修改 `_currentGold` / `_currentReputation`（以 `private` 欄位 + 寫入 API 強制）
- 禁止跳過重入防護
- 禁止在 `Update` 中執行破產倒數檢查（必須訂閱 `OnSecondTick`，避免雙重計時）
- 禁用 `FindObjectOfType`、`GameObject.Find`

---

## 5. 資料表契約（Data Table Contract）

### 5.1 測試用 `SystemConstants.csv`

```csv
key,value,description
DAILY_RESET_HOUR,0,每日重置時間
OFFLINE_MAX_SECONDS,604800,離線時間上限（秒）
GOLD_INITIAL,100,起始金幣
GOLD_MAX,9999999,金幣上限
REPUTATION_MIN,-100,聲望下限
REPUTATION_MAX,100,聲望上限
```

### 5.2 測試用 `BankruptcyThresholdTable.csv`

| reputationMin | reputationMax | warningDurationSec |
|---------------|---------------|---------------------|
| -100 | -1 | 86400 |
| 0 | 19 | 172800 |
| 20 | 39 | 259200 |
| 40 | 59 | 259200 |
| 60 | 79 | 345600 |
| 80 | 100 | 604800 |

```csv
reputationMin,reputationMax,warningDurationSec
-100,-1,86400
0,19,172800
20,39,259200
40,59,259200
60,79,345600
80,100,604800
```

---

## 6. 技術限制（Technical Constraints）

- 遵循 F-01 / F-02 工項需求書的通用規則（命名、繁中註釋、XML 文件、禁用 API）
- 單一方法 < 40 行、複雜度 < 10
- 中間計算統一以 `long` 避免 `int` 溢位
- `delta == 0` 時**不**廣播 `OnGoldChanged` / `OnReputationChanged`（避免 UI 多餘更新）
- 事件訂閱／退訂在 `OnEnable` / `OnDisable`，不在 `Awake` / `Start`（避免 OnDestroy 漏退訂）
- 單元測試分 EditMode（純狀態機邏輯）與 PlayMode（`OnSecondTick` 驅動倒數）

---

## 7. 驗收條件（Acceptance Criteria）

全數對應 GDD §8，加上序列化驗收：

| AC ID | 測試項目 | 通過條件 |
|-------|----------|---------|
| AC-RM-01 | `AddGold` reject | 金幣 0、門檻 -100，呼叫 `AddGold(-9999)` → 回 `false`、金幣不變、不廣播 |
| AC-RM-02 | `AddGold` 上限截斷 | 金幣 9,000,000、呼叫 `AddGold(2,000,000)` → 金幣 = 9,999,999，事件 delta = 999,999 |
| AC-RM-03 | `AddGold` 成功廣播 | `AddGold(100)` → `OnGoldChangedEvent(100, 100)` 發布一次 |
| AC-RM-04 | 聲望 clamp | 聲望 80、`AddReputation(50)` → 聲望 100、delta = 20 |
| AC-RM-05 | 進 Warning | 金幣 50、`AddGold(-100)` → 金幣 -50、狀態轉 Warning、事件發布 |
| AC-RM-06 | Warning 不受限 | Warning 下 `CanAfford`、`AddGold`（合法）、`AddReputation` 均正常 |
| AC-RM-07 | Warning 回 Normal | Warning 下 `AddGold(100)` 使金幣回 0 → 狀態 Normal、計時器清除 |
| AC-RM-08 | Warning 不重置計時器 | Warning 下 `AddGold(-10)` → `_bankruptcyWarningStartTime` 不變 |
| AC-RM-09 | 倒數觸發 Bankrupt | 模擬 `_warningDurationSec` 秒後 `OnSecondTick` 觸發 → 狀態 Bankrupt、事件發布 |
| AC-RM-10 | 推送更新門檻 | 門檻 -100、金幣 -50（Warning）→ `SetBankruptcyThreshold(-500)` → 門檻 -500、金幣 -50 仍在新範圍內 → 維持 Warning |
| AC-RM-11 | 離線破產判定 | 離線期間 `_warningDurationSec` 過期且金幣 < 0 → `OnOfflineResolved` 觸發後狀態 Bankrupt |
| AC-RM-12 | 離線恢復正值不破產 | 離線期間結算使金幣回正 → 倒數已到期也不觸發 Bankrupt（狀態 Normal） |
| AC-RM-13 | 多次進出 Warning | 金幣正負振盪 3 次 → 每次從正轉負以**當下**聲望段重查 `warningDurationSec` |
| AC-RM-14 | 存檔還原 Warning | `CreateSnapshot` → `RestoreSnapshot` → `GetBankruptcyWarningState` = Warning，剩餘秒數正確遞減 |
| AC-RM-15 | `ResetBankruptcyState` | 呼叫後狀態回 Normal、時間戳清除、事件發布 |
| AC-RM-16 | `AddGoldAllowBankruptcy` 廣播 | 金幣 100、呼叫 `AddGoldAllowBankruptcy(-50)` → 金幣 50、事件 delta = -50 |
| AC-RM-17 | `AllowBankruptcy` 跨門檻 | 金幣 -50、門檻 -100、`AddGoldAllowBankruptcy(-200)` → 金幣 -250、狀態 Bankrupt；對照：`AddGold(-200)` 回 `false` 且金幣不變 |
| AC-RM-18 | `AllowBankruptcy` 上限截斷 | 同 AC-02 但走 `AllowBankruptcy` 分支 |
| AC-RM-19 | 單次不穿越零線兩次 | 任一 `AddGoldAllowBankruptcy` 呼叫至多發 1 次 `OnGoldChanged` + 至多 1 次 `OnBankruptcyWarningStateChanged` |
| AC-RM-20 | 門檻預設值 | 啟動後無推送 → `GetCurrentBankruptcyThreshold()` = -100；`SetBankruptcyThreshold(-500)` 後 = -500 |
| AC-RM-21 | 門檻緊縮觸 Bankrupt | 金幣 -150、Warning、門檻 -200 → `SetBankruptcyThreshold(-100)` → `EvaluateWarningState` 轉 Bankrupt |
| AC-RM-22 | 重入防護 | 訂閱 `OnGoldChangedEvent` 的 handler 在 callback 中呼叫 `AddGold(10)` → LogError、第二次呼叫回 `false`、金幣不變 |
| AC-RM-23 | `delta == 0` 不廣播 | `AddGold(0)`、`AddReputation(0)` → 不發 `OnGoldChanged` / `OnReputationChanged` |
| AC-RM-24 | `AddReputation` 觸發狀態校正 | 金幣 -50（Warning）、`AddReputation(…)` → `EvaluateWarningState` 被呼叫（間接驗證：透過額外修改門檻使 `currentGold < threshold` 再觸發 `AddReputation` 應轉 Bankrupt） |
| AC-RM-25 | `HandleOfflineResolved` 一律呼叫 `EvaluateWarningState` | 金幣回正但狀態仍 Warning 時觸發 `OnOfflineResolved` → 狀態校正為 Normal（對應 GDD §3.6 rule 10 (b)） |
| AC-RM-26 | `CanAfford(0)` 與負值 | `CanAfford(0)` 恆回 `true`；`CanAfford(-100)` 恆回 `true`（加值操作永不拒絕） |
| AC-RM-27 | `Awake` 完成初始化 | `Awake` 執行後（不等 `Start`）：`GetGold()` 回 `GOLD_INITIAL`、`GetCurrentBankruptcyThreshold()` 回 `-100`、`GetBankruptcyWarningState()` 回 `Normal` |
| AC-RM-28 | `TriggerBankruptcy` 冪等 | 狀態已是 `Bankrupt` 時，再次呼叫 `TriggerBankruptcy` 不重複廣播 `OnBankruptcyWarningStateChangedEvent` |
| AC-RM-29 | `AddGoldAllowBankruptcy` delta 溢位 | 呼叫 `AddGoldAllowBankruptcy(int.MinValue)`（金幣 0）→ 不 crash、不拋例外；`actualDelta` 截斷至 `int.MinValue` + `LogWarning`；狀態轉 `Bankrupt` |
| AC-RM-30 | 事件 namespace 歸屬 | `OnGoldChangedEvent` 等事件定義於 `TheGuild.Gameplay.Resources.Events`；`Core/Events/GameEvents.cs` 未引用 `BankruptcyWarningState` |
| AC-RM-31 | `RegisterTables` 於啟動前執行 | DataManager `Awake` 時，F-03 的 `BankruptcyThresholdTable` 已被註冊並成功載入；`DataManager.GetAll<BankruptcyThresholdData>()` 回傳 6 筆 |

---

## 8. 交付物清單（Deliverables）

1. **原始碼**
   - `Assets/Scripts/Gameplay/Resources/ResourceManagement.cs`（含 `[RuntimeInitializeOnLoadMethod] RegisterTables()` 靜態方法，在 `Awake` 前向 DataManager 註冊 `BankruptcyThresholdTable` 與 `SystemConstants`）
   - `Assets/Scripts/Gameplay/Resources/BankruptcyWarningState.cs`（本工項唯一擁有）
   - `Assets/Scripts/Gameplay/Resources/BankruptcyThresholdData.cs`
   - `Assets/Scripts/Gameplay/Resources/ResourceSnapshot.cs`
   - `Assets/Scripts/Gameplay/Resources/Events/ResourceEvents.cs`（3 個 struct：`OnGoldChangedEvent` / `OnReputationChangedEvent` / `OnBankruptcyWarningStateChangedEvent`）
   - **不修改** `Core/Events/GameEvents.cs`（Core 層不引用 Gameplay 型別）
   - **不修改** F-01 任何檔案（透過 `DataManager.RegisterTable` 延遲註冊，不改 F-01 原始碼）
2. **測試**
   - `Assets/Tests/EditMode/Gameplay/Resources/ResourceManagementTests.cs`
   - `Assets/Tests/PlayMode/Gameplay/Resources/ResourceManagementPlayModeTests.cs`
   - `Assets/Tests/EditMode/Gameplay/Resources/TestResources/SystemConstants.csv`
   - `Assets/Tests/EditMode/Gameplay/Resources/TestResources/BankruptcyThresholdTable.csv`
   - 對應 asmdef 檔
3. **專案設定**
   - `ProjectSettings/ScriptExecutionOrder.asset` 更新（`ResourceManagement: -800`）
4. **實作回報**
   - `ResourceSnapshot` 採 `[System.Serializable] class` 的決策與 Unity JsonUtility 相容性驗證
   - `EvaluateWarningState` 呼叫時機完整清單（驗證涵蓋 GDD §3.6 rule 10 的四個時機 + `AddGoldAllowBankruptcy`）
   - `BankruptcyWarningState` enum 的命名空間歸屬確認（`TheGuild.Gameplay.Resources`），以及事件定義路徑（`Gameplay/Resources/Events/ResourceEvents.cs`）
   - `RegisterTables()` 註冊說明：F-03 同時註冊 `BankruptcyThresholdTable` 與 `SystemConstants`；若 F-02 先註冊 `SystemConstants`，依 F-01 §4.3 冪等契約僅首次生效，F-03 重複註冊不報錯
   - AC 通過清單（AC-RM-01 ~ AC-RM-31）
   - 任何偏離 GDD 或本需求書的決策與原因

---

## 9. 審查重點（Review Checklist）

- [ ] 所有 API 簽名與 §4.4 完全一致
- [ ] **`Awake` 完成所有初始化**（讀 CSV 常數、設預設門檻）；`Start` 為空實作
- [ ] 中間計算使用 `long`，`AddGold(int.MaxValue)` 不溢位；`AddGoldAllowBankruptcy(int.MinValue)` 有下限保護
- [ ] 重入防護覆蓋三個寫入 API，`try...finally` 確保復位
- [ ] `delta == 0` 時不廣播
- [ ] `EvaluateWarningState` 於**五個時機**正確呼叫：載入、離線（一律）、`AddReputation`、`SetBankruptcyThreshold`、`AddGoldAllowBankruptcy`
- [ ] `AddGold` 的分支走 `UpdateBankruptcyWarning`（不經過 `EvaluateWarningState` 的 Bankrupt 路徑，因嚴格 reject 保證金幣不會 < threshold）
- [ ] `HandleOfflineResolved` **一律**呼叫 `EvaluateWarningState`（除已轉 Bankrupt 的 early return）
- [ ] 事件訂閱／退訂在 `OnEnable` / `OnDisable`
- [ ] 訂閱 `OnSecondTickEvent` 而非每幀檢查
- [ ] Script Execution Order 正確（`-800`，早於 C-06 `-700`）
- [ ] `BankruptcyWarningState` enum 僅在本工項定義；無跨工項重複或占位
- [ ] 事件定義於 `Gameplay/Resources/Events/ResourceEvents.cs`，`Core/Events/GameEvents.cs` **未被修改**
- [ ] `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] RegisterTables()` 於 F-03 `Awake` 前呼叫 `DataManager.RegisterTable<BankruptcyThresholdData>` 與 `RegisterSystemConstantsTable`
- [ ] **不修改** F-01 任何檔案（零跨工項耦合）
- [ ] `TriggerBankruptcy` 冪等（狀態已 Bankrupt 時不重複廣播）
- [ ] `AddGoldAllowBankruptcy` delta 以 `long` 計算，極端溢位截斷+LogWarning
- [ ] `CanAfford(0)` / `CanAfford(<0)` 恆回 `true`
- [ ] `ResourceSnapshot` 為 `[Serializable] class`，與 Unity JsonUtility 相容
- [ ] 測試覆蓋所有 AC，PlayMode 驗證倒數，EditMode 驗證狀態機
- [ ] 無硬編碼金幣／聲望常數；全部來自 CSV

---

## 10. Codex 調用參考

**階段一（read-only）**：

```
cd: C:/gitlab/LKF/TheGuild-SGA2026GameJam-phase2
sandbox: "read-only"
SESSION_ID: <承接 F-02 session 或開新 session>
PROMPT:
依據 production/work-items/[F-03] resource-management.md 實作 F-03 Resource Management。
請先閱讀工項需求書與 design/GDD/[F-03] resource-management.md。
確認上游已完成：
1. Assets/Scripts/Core/Data/DataManager.cs（F-01，含 `RegisterTable` / `RegisterSystemConstantsTable` 靜態 API）
2. Assets/Scripts/Core/Time/TimeSystem.cs（F-02，含 `RegisterSystemConstantsTable("SystemConstants")` 註冊）
3. Assets/Scripts/Core/Events/EventBus.cs、GameEvents.cs（F-02）
實作重點：
1. **`Awake` 完成所有初始化**（§4.7），與 GDD §3.4 rule 7 一致
2. `BankruptcyWarningState` enum 僅於本工項定義；F-02 不建立占位檔
3. 破產警告狀態機三分支（AddGold / AddGoldAllowBankruptcy / AddReputation）走對應入口（§4.8）
4. `HandleOfflineResolved` **一律**呼叫 `EvaluateWarningState`（§4.8 + AC-RM-25）
5. **F-03 事件放在 `Gameplay/Resources/Events/ResourceEvents.cs`**（§4.5），不修改 `Core/Events/GameEvents.cs`
6. **透過 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 向 DataManager 註冊表格**（§8 交付物），**不修改**任何 F-01 檔案
7. `TriggerBankruptcy` 冪等（§4.8）
8. `AddGoldAllowBankruptcy` delta 以 `long` 計算，溢位截斷+LogWarning（§4.8）
9. 重入防護共用 flag（§4.10 + AC-22）
10. `ResourceSnapshot` 採 `[Serializable] class`，6 欄位（§4.11）
11. 測試分 EditMode（狀態機邏輯）與 PlayMode（OnSecondTick 倒數）
完成後回報程式碼、實作決策、AC 通過清單。
```

**階段二**：審查 APPROVED 後切 `workspace-write` 寫入。
