# 工項需求書：F-02 Time System

_建立時間：2026-04-23_
_系統 ID：F-02_
_分級：**Large**_（新 Foundation 系統；同時建立 EventBus 基礎設施，多下游系統依賴）
_對應 GDD：`design/GDD/[F-02] time-system.md`_
_交付對象：Codex（實作）_

---

## 1. 工項概要（Summary）

實作遊戲時間基礎設施 `TimeSystem`：Singleton MonoBehaviour，負責即時模式（`Update` 每秒 tick）與離線模式（重啟時一次性推進）兩套時間計算，管理每日重置（UTC `DAILY_RESET_HOUR`:00）、任務計時器註冊與到期廣播、Tick 暫停機制。所有事件透過 EventBus 廣播，不直接持有下游引用。

**同時建立** `Core/Events/EventBus` 基礎設施：因 Time System 是第一個事件發布者，EventBus 必須在本工項內一併產出。後續 F-03、FT-04、C-06 等系統將重用此 EventBus。

**交付流程**：階段一 read-only 實作 → 審查 → 階段二 workspace-write 寫入；最多 2 次來回。

---

## 2. 目標與範圍（Goals & Scope）

### 2.1 In Scope

- `TimeSystem.cs`（Singleton MonoBehaviour，`DontDestroyOnLoad`）
- EventBus 基礎設施（`EventBus.cs`、`GameEvents.cs` 事件定義集中檔）
- 即時模式：`_accumulator` 累積 `Time.unscaledDeltaTime`、`while` 迴圈補發掉幀、`OnSecondTick` / `OnMinuteTick` 發布節奏
- **兩段式離線流程**（對應 GDD §3.3）：
  - 階段 A `Initialize(lastActiveTimestamp)`：由 FT-10 於載入完成後呼叫，計算 `offlineSeconds` 與到期任務快照：
    - **一般路徑（有任務／跨日）**：**不發布** `OnMissionExpired` / `OnOfflineResolved` / `OnDailyReset`；發布 `OnOfflinePending` 供 UI 顯示離線摘要
    - **捷徑（無任務 + 無跨日）**：不發 `OnOfflinePending`（UI 不顯示摘要），直接發布 `OnOfflineResolved(offlineSeconds, 0)` 完結離線流程（對應 GDD §5.1「直接進入遊戲」語意，同時給 F-03 破產判定觸發機會）
    - **無效路徑（`offlineSeconds <= 0`）**：首次啟動／時間欺騙／無離線，直接標 `Resolved`，不發任何事件
  - 階段 B `ConfirmOfflineResolution()`：由 UI（離線摘要確認按鈕）呼叫；此時才依序發布 `OnMissionExpired`（逐一）→ `OnDailyReset`（跨日補發一次）→ `OnOfflineResolved`
- 每日重置：UTC 日期比對、`OnDailyReset` 發布、離線跨多日僅觸發一次
- 任務計時器：`RegisterMission` / `UnregisterMission`、每秒重算 `remainingSeconds = dispatch + duration - nowUTC`、≤ 0 發 `OnMissionExpired`
- Tick 暫停機制：`PauseTick()` 冪等 API（GDD §3.7）——**僅影響即時模式**（`Update` 驅動的 tick），**不影響**離線模式的 `Initialize` / `ConfirmOfflineResolution`
- 時間欺騙防護：UTC 儲存、系統時間調回視為 0
- EditMode + PlayMode 單元測試涵蓋 AC-01~AC-20

### 2.2 Out of Scope

- **Save/Load 整合**：`Initialize` / `ConfirmOfflineResolution` 僅提供 API，實際呼叫由 FT-10 / P-02 處理
- **離線摘要 UI**：僅發 `OnOfflinePending` / `OnOfflineResolved` 事件，不實作畫面（P-02 範疇）
- **FT-11 Offline Resolver**：離線 NPC 自主接單追認非本工項範圍；本工項明確實作「離線不補發 `OnMinuteTick`」
- **UI 時長顯示格式化**（原 AC-TS-03）：`GetDisplayString(seconds)` 歸 UI 工項，**不**由 TimeSystem 提供
- **網路時間校正**：單機遊戲，不做
- **玩家暫停**：本工項只做 `PauseTick()`，不實作 `ResumeTick()`

---

## 3. 依賴與前置條件（Dependencies & Preconditions）

### 3.1 上游依賴

| 依賴項 | 狀態 | 備註 |
|-------|------|------|
| F-01 DataManager | ⚠️ **必須先完成** | 讀 `DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS` |
| `SystemConstants.csv`（最低欄位） | ⚠️ 由本工項**產出測試用 CSV** | 生產用 CSV 由 systems-designer 後續填充 |

**硬依賴**：F-01 DataManager 必須在 F-02 啟動前完成實作並通過驗收。

### 3.2 下游影響（不可破壞的契約）

| 下游系統 | 依賴介面 |
|---------|---------|
| F-03 Resource Management | `OnSecondTick`、`OnOfflineResolved`、`NowUTC` |
| FT-02 Mission Dispatch | `NowUTC`、`RegisterMission` |
| FT-04 Outcome Resolution | `OnMissionExpired` |
| FT-01 Recruitment | `OnDailyReset` |
| C-06 World Danger | `OnDailyReset`、`NowUTC` |
| FT-03 NPC Decision | `OnMinuteTick` |
| FT-06 Guild Core | `PauseTick()`、`NowUTC` |
| FT-10 Save/Load | `Initialize(lastActiveTimestamp)` |
| C-02 Adventurer Management | `OnSecondTick` |
| P-03 Notification | `OnMissionExpired` |

---

## 4. 實作要求（Implementation Requirements）

### 4.1 檔案位置

```
TheGuild-unity/Assets/Scripts/Core/Events/
├── EventBus.cs                 # 泛型 pub/sub（無 MonoBehaviour 依賴，純 C#）
└── GameEvents.cs               # 全專案事件定義集中檔

TheGuild-unity/Assets/Scripts/Core/Time/
├── TimeSystem.cs               # Singleton MonoBehaviour
├── MissionTimer.cs             # 任務計時器記錄（struct / class）
└── OfflineSummary.cs           # 離線摘要資料結構（供 UI 顯示用）

TheGuild-unity/Assets/Tests/EditMode/Core/Events/
└── EventBusTests.cs

TheGuild-unity/Assets/Tests/EditMode/Core/Time/
└── TimeSystemTests.cs          # EditMode 部分（離線、每日重置、任務計時推算）

TheGuild-unity/Assets/Tests/PlayMode/Core/Time/
└── TimeSystemPlayModeTests.cs  # PlayMode 部分（Update 驅動的即時模式、OnSecondTick、PauseTick）
```

**註**：`BankruptcyWarningState` 不在本工項；由 F-03 唯一擁有並定義（見 F-03 工項書 §4.3）。本工項**不**建立任何占位檔，`TimeSystem` 與 EventBus 不引用該 enum。

### 4.2 命名空間

```csharp
namespace TheGuild.Core.Events
namespace TheGuild.Core.Time
```

### 4.3 EventBus 公開 API

```csharp
public static class EventBus
{
    public static void Subscribe<T>(System.Action<T> handler);
    public static void Unsubscribe<T>(System.Action<T> handler);
    public static void Publish<T>(T eventData);

    // 無 payload 的事件
    public static void Subscribe(string eventName, System.Action handler);
    public static void Unsubscribe(string eventName, System.Action handler);
    public static void Publish(string eventName);
}
```

**事件定義集中檔** `GameEvents.cs`：

```csharp
public readonly struct OnSecondTickEvent       { public long NowUTC { get; } /* ctor */ }
public readonly struct OnMinuteTickEvent       { public long NowUTC { get; } }
public readonly struct OnMissionExpiredEvent   { public string MissionInstanceId { get; } }

/// <summary>離線計算完成、摘要就緒，等待玩家確認；尚未執行結算。</summary>
public readonly struct OnOfflinePendingEvent   { public OfflineSummary Summary { get; } }

/// <summary>玩家確認離線摘要後，所有結算與每日重置已處理完成。</summary>
public readonly struct OnOfflineResolvedEvent  { public long OfflineSeconds { get; } public int CompletedCount { get; } }

// 無 payload
public static class EventNames
{
    public const string OnDailyReset = "OnDailyReset";
}
```

**`OfflineSummary`**（`Core/Time/OfflineSummary.cs`）：

```csharp
public readonly struct OfflineSummary
{
    public long OfflineSeconds { get; }
    public int CompletedCount { get; }
    public IReadOnlyList<string> CompletedMissionInstanceIds { get; }
    public bool CrossesDailyReset { get; }
}
```

**設計考量**：
- 強型別事件（struct payload）走泛型 Subscribe；無 payload 事件走字串 key
- `OnOfflinePending` 與 `OnOfflineResolved` 分離，忠實對應 GDD §3.3 的「先顯示摘要 → 玩家確認 → 才結算」流程
- `OfflineSummary.CompletedMissionInstanceIds` 讓 UI 可預覽將結算的任務清單

### 4.4 TimeSystem 公開 API

```csharp
public sealed class TimeSystem : MonoBehaviour
{
    public static TimeSystem Instance { get; private set; }

    /// <summary>當前 UTC Unix timestamp（秒）。PauseTick 後仍可正確回傳。</summary>
    public long NowUTC { get; }

    /// <summary>
    /// 【離線階段 A】由 FT-10 Save/Load 於載入完成後呼叫；僅計算摘要，不發布結算事件。
    /// 計算完成後發布 OnOfflinePending 供 UI 顯示摘要。
    /// 若 offlineSeconds == 0（首次啟動 / 時間欺騙 / 無離線）：不發布任何事件，直接標記 resolved。
    /// </summary>
    public void Initialize(long lastActiveTimestamp);

    /// <summary>
    /// 【離線階段 B】由 UI（離線摘要確認按鈕）呼叫；
    /// 依序發布 OnMissionExpired（逐一） → OnDailyReset（若跨日） → OnOfflineResolved。
    /// 冪等；若已 resolved 或未處於 pending 狀態則忽略。
    /// </summary>
    public void ConfirmOfflineResolution();

    /// <summary>派遣任務時呼叫，加入計時器清單。durationSeconds 為秒（由呼叫方負責單位轉換）。</summary>
    public void RegisterMission(string missionInstanceId, long dispatchTimestamp, int durationSeconds);

    /// <summary>任務結算後呼叫，從清單移除。</summary>
    public void UnregisterMission(string missionInstanceId);

    /// <summary>FT-06 Guild Core 於 Game Over 階段 2 呼叫；冪等；僅停止即時模式 tick。</summary>
    public void PauseTick();

    // 序列化支援（供 FT-10）
    public IReadOnlyList<MissionTimer> GetActiveMissionTimers();
    public long GetLastActiveTimestamp();
}

public readonly struct MissionTimer
{
    public string MissionInstanceId { get; }
    public long DispatchTimestamp { get; }
    public int DurationSeconds { get; }
}
```

**單位契約**：`RegisterMission.durationSeconds` 統一為**秒**。GDD §3.1 rule 2 所述「任務時長單位為分鐘」指**資料層（`DurationTable.baseDuration`）**；**呼叫方（FT-02 Mission Dispatch）負責 `× 60` 轉換**後傳入 F-02。F-02 內部一律以秒運算，不做分鐘概念。

### 4.5 即時模式實作細節（對應 GDD §3.2、§3.7）

```
Update():
    if _tickPaused: return
    _accumulator += Time.unscaledDeltaTime
    while _accumulator >= 1f:
        _accumulator -= 1f
        long now = NowUTC
        EventBus.Publish(new OnSecondTickEvent(now))
        CheckMissionTimers(now)
        CheckDailyResetCrossing(now)
        _minuteAccumulator += 1
        if _minuteAccumulator >= 60:
            _minuteAccumulator = 0
            EventBus.Publish(new OnMinuteTickEvent(now))
```

- `Time.unscaledDeltaTime`：即使遊戲暫停仍計時（追蹤真實 UTC）
- 單幀掉幀補發：`while` 迴圈確保不漏秒
- `CheckMissionTimers` 以**時間戳法**重算 `remainingSeconds`，避免累積誤差

### 4.5a 任務到期去重（契約）

`OnMissionExpired` 對同一 `missionInstanceId` **只發布一次**；配合下游 FT-04 尚未呼叫 `UnregisterMission` 前避免重複發。

```csharp
private readonly HashSet<string> _publishedExpirations = new();

// 統一入口（CheckMissionTimers 與 ConfirmOfflineResolution 皆呼叫此方法）
private void PublishMissionExpired(string missionInstanceId)
{
    if (_publishedExpirations.Contains(missionInstanceId)) return;
    _publishedExpirations.Add(missionInstanceId);
    EventBus.Publish(new OnMissionExpiredEvent(missionInstanceId));
}

// CheckMissionTimers 改走 PublishMissionExpired：
foreach timer in _missionTimers:
    long remaining = timer.DispatchTimestamp + timer.DurationSeconds - now
    if remaining <= 0:
        PublishMissionExpired(timer.MissionInstanceId)
```

**與 `UnregisterMission` 互動**：
- `UnregisterMission(id)`：從 `_missionTimers` 移除 **且** 從 `_publishedExpirations` 移除（允許同一 `id` 未來重複使用——雖然實務上 missionInstanceId 唯一，但此清理保持 HashSet 不累積殭屍）
- **正常流程**：Timer 到期 → `PublishMissionExpired` → FT-04 結算 → FT-04 呼叫 `UnregisterMission` → HashSet 清除

### 4.6 離線模式實作細節（GDD §3.3，兩段式）

**階段 A：`Initialize(lastActiveTimestamp)`**——僅計算，依無／有任務分歧：

```
Initialize(lastActiveTimestamp):
    long now = NowUTC
    long raw = now - lastActiveTimestamp
    long offlineSeconds = clamp(raw, 0, OFFLINE_MAX_SECONDS)
    _lastActiveTimestamp = lastActiveTimestamp

    if offlineSeconds <= 0:
        // 首次啟動 / 時間欺騙 / 無離線 → 直接標 resolved，不發任何事件
        _offlineState = OfflineState.Resolved
        return

    List<string> completedIds = []
    foreach timer in _missionTimers:
        long remaining = timer.DispatchTimestamp + timer.DurationSeconds - now
        if remaining <= 0:
            completedIds.Add(timer.MissionInstanceId)

    bool crossesReset = ComputeDailyResetCrossing(lastActiveTimestamp, now)

    // 無任務且無跨日：直接跳過確認流程，一次性完結（對應 GDD §5.1「離線期間無任何進行中任務 → 不顯示離線摘要畫面，直接進入遊戲」）
    if completedIds.Count == 0 and not crossesReset:
        // 仍發 OnOfflineResolved 讓 F-03 離線破產判定有機會執行；但不發 OnOfflinePending（UI 不顯示摘要）
        _offlineState = OfflineState.Resolved
        EventBus.Publish(new OnOfflineResolvedEvent(offlineSeconds, 0))
        return

    // 有任務或跨日 → 進入 Pending，等待 UI 確認
    _pendingSummary = new OfflineSummary(offlineSeconds, completedIds.Count, completedIds, crossesReset)
    _offlineState = OfflineState.Pending
    EventBus.Publish(new OnOfflinePendingEvent(_pendingSummary))
```

**GDD 對齊**：
- 無任務 + 無跨日 → 不發 `OnOfflinePending`（UI 無摘要畫面），仍發 `OnOfflineResolved`（F-03 破產判定可執行）——對應 GDD §5.1 `「離線期間無任何進行中任務 → 不顯示離線摘要畫面，直接進入遊戲」` 的語意
- 有任務 或 跨日 → 進入 Pending，UI 顯示摘要待確認

**階段 B：`ConfirmOfflineResolution()`**——玩家確認後，依序結算：

```
ConfirmOfflineResolution():
    if _offlineState != OfflineState.Pending:
        return   // 冪等：已 resolved 或未 pending 則忽略

    // 順序：OnMissionExpired（逐一）→ OnDailyReset（若跨日）→ OnOfflineResolved
    foreach id in _pendingSummary.CompletedMissionInstanceIds:
        PublishMissionExpired(id)   // 走去重入口（§4.5a）

    if _pendingSummary.CrossesDailyReset:
        _lastResetUtcDate = DateTimeOffset.FromUnixTimeSeconds(NowUTC).UtcDateTime.Date
        EventBus.Publish(EventNames.OnDailyReset)

    EventBus.Publish(new OnOfflineResolvedEvent(_pendingSummary.OfflineSeconds, _pendingSummary.CompletedCount))

    _offlineState = OfflineState.Resolved
    _pendingSummary = default
```

**內部狀態機**：

```csharp
private enum OfflineState { Uninitialized, Pending, Resolved }
private OfflineState _offlineState = OfflineState.Uninitialized;
private OfflineSummary _pendingSummary;
```

**順序保證**（對應 GDD §5.3「結算先於重置」）：`OnMissionExpired` → `OnDailyReset` → `OnOfflineResolved`。此順序**在 `ConfirmOfflineResolution` 內**確保，不在 `Initialize` 內發布。

### 4.7 每日重置實作（GDD §3.4）

使用**完整 UTC Date**（年+月+日）避免 `DayOfYear` 跨年問題：

```csharp
// 內部狀態
private System.DateTime _lastResetUtcDate;   // 僅保留 Date 部分（時分秒 = 0）

// 即時模式每秒 tick 時呼叫
private void CheckDailyResetCrossing(long currentUtcSeconds)
{
    var currentUtc = System.DateTimeOffset.FromUnixTimeSeconds(currentUtcSeconds).UtcDateTime;
    var currentDate = currentUtc.Date;

    // 尚未跨到新 Date，或雖同 Date 但尚未過 DAILY_RESET_HOUR 閾值 → 不觸發
    if (currentDate <= _lastResetUtcDate) return;
    if (currentUtc.Hour < _dailyResetHour) return;

    _lastResetUtcDate = currentDate;
    EventBus.Publish(EventNames.OnDailyReset);
}

// 離線階段用（僅計算是否跨日，不發事件）
private bool ComputeDailyResetCrossing(long lastActiveUtc, long nowUtc)
{
    var last = System.DateTimeOffset.FromUnixTimeSeconds(lastActiveUtc).UtcDateTime;
    var now = System.DateTimeOffset.FromUnixTimeSeconds(nowUtc).UtcDateTime;
    // 跨越了至少一個 DAILY_RESET_HOUR:00
    var lastResetBoundary = last.Date.AddHours(_dailyResetHour);
    if (last.Hour >= _dailyResetHour) lastResetBoundary = lastResetBoundary.AddDays(1);
    return lastResetBoundary <= now;
}
```

**初始化與序列化**：
- `Awake` 時 `_lastResetUtcDate = DateTimeOffset.FromUnixTimeSeconds(NowUTC).UtcDateTime.Date`（當前 UTC 日期為起點，避免啟動瞬間誤觸發）
- FT-10 Save/Load 時序列化 `_lastResetUtcDate`；載入後 `TimeSystem.Initialize` 內部比對當前日期決定是否觸發跨日事件
- **離線跨多日僅觸發一次**：`ConfirmOfflineResolution` 內直接將 `_lastResetUtcDate` 設為當前日期（發一次 `OnDailyReset` 後），即時模式後續不再補發

### 4.8 PauseTick（GDD §3.7）

- `_tickPaused` 預設 `false`
- `PauseTick()` 設為 `true`；冪等（重複呼叫不拋例外）
- **不影響**：`NowUTC` 查詢、MonoBehaviour `Update` 本身、Unity 引擎層
- **僅影響即時模式**：
  - `OnSecondTick` 停止發布（因由 `Update` 驅動）
  - `OnMinuteTick` 停止發布（因由 `OnSecondTick` 累積驅動）
  - 即時模式的 `OnMissionExpired`（由 `CheckMissionTimers` 驅動）停止發布
  - 即時模式的 `OnDailyReset`（由 `CheckDailyResetCrossing` 驅動）停止發布
- **不影響離線模式**（對應 GDD §3.7「PauseTick() 不影響離線模式」）：
  - `Initialize` 仍可呼叫並發布 `OnOfflinePending`
  - `ConfirmOfflineResolution` 仍可呼叫並依序發布 `OnMissionExpired` → `OnDailyReset` → `OnOfflineResolved`
  - 離線事件發布路徑**不經過** `_tickPaused` 檢查
- **未提供** `ResumeTick()`；Game Over 為終態

### 4.9 SystemConstants 讀取與註冊

**前置註冊（必備）**：F-01 採延遲註冊模式；F-02 需於 DataManager `Awake` 前註冊 `SystemConstants` 表格：

```csharp
// TimeSystem.cs 內
[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void RegisterTables()
{
    TheGuild.Core.Data.DataManager.RegisterSystemConstantsTable("SystemConstants");
}
```

**冪等性**：`RegisterSystemConstantsTable` 對同一 `tableName` 多次呼叫僅首次生效（F-01 §4.3 契約）；F-03 亦會註冊 `SystemConstants`，兩工項皆安全執行，不衝突。

**Awake 讀常數**：
- `Awake`：檢查 `DataManager.Instance != null`；若為 `null` → `LogError` 並以硬編碼預設值（`DAILY_RESET_HOUR = 0`、`OFFLINE_MAX_SECONDS = 604800`）繼續運作（測試友善）
- 否則：
  ```
  _dailyResetHour = DataManager.Instance.GetInt("DAILY_RESET_HOUR")
  _offlineMaxSeconds = DataManager.Instance.GetInt("OFFLINE_MAX_SECONDS")
  ```

**執行時序**：
1. `BeforeSceneLoad`：F-02 / F-03 的 `RegisterTables()` 被呼叫 → `_pendingRegistrations` 累積
2. Scene 載入：`DataManager` `Awake`（優先級 `-1000`）→ 依 `_pendingRegistrations` 載入所有 CSV → `SystemConstants` 快取就緒
3. `TimeSystem` `Awake`（優先級 `-900`）→ `DataManager.Instance.GetInt(...)` 取值成功

### 4.10 Script Execution Order

`ProjectSettings/ScriptExecutionOrder.asset` 設定：
- `DataManager`：`-1000`（已於 F-01 設定）
- `TimeSystem`：`-900`

### 4.11 生命週期

- `Awake`：Singleton 檢查、`DontDestroyOnLoad`、讀常數
- `OnEnable`：訂閱（本工項 TimeSystem 不訂閱任何事件；預留 `OnEnable`/`OnDisable` 空殼）
- `Update`：§4.5 pseudo
- `OnDisable` / `OnDestroy`：**僅退訂自己訂閱過的事件**（本工項 TimeSystem 未訂閱任何事件，故無需清理；契約：發布者**不**清空 EventBus 全部訂閱，避免誤刪他人訂閱）

### 4.11a EventBus 生命週期契約

- **Subscribe / Unsubscribe 配對**：每個訂閱者於 `OnEnable` Subscribe、`OnDisable` Unsubscribe，必須配對；違反則 `EventBus` 可能累積殭屍 handler
- **EventBus 本身是 `static`、無生命週期**；Scene 切換時 EventBus 不重置
- **Publish 者不擁有訂閱清單**：`TimeSystem` / `ResourceManagement` 等發布者**不得**在自身 `OnDestroy` 中清空 EventBus 的整個事件訂閱表
- **測試支援**：`EventBus` 提供 `ClearAll()`（`internal` 或 `#if UNITY_EDITOR`）供 EditMode 測試重置；正式執行期不呼叫

### 4.12 禁用事項

- 禁用 `DateTime.Now`（含本地時區）；一律 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`
- 禁用 `Time.deltaTime`（本工項需要 `unscaledDeltaTime`，因遊戲暫停時仍需計時）
- 禁用 `FindObjectOfType` / `GameObject.Find`

---

## 5. 資料表契約（Data Table Contract）

**測試用 `SystemConstants.csv`**（放在 `Assets/Tests/EditMode/Core/Time/TestResources/`）：

```csv
key,value,description
DAILY_RESET_HOUR,0,每日重置時間（UTC 0-23）
OFFLINE_MAX_SECONDS,604800,離線時間上限（秒）
```

---

## 6. 技術限制（Technical Constraints）

### 6.1 通用

- 遵循 F-01 工項需求書 §6.1（命名慣例、繁中註釋、XML 文件）
- 單一方法 < 40 行、複雜度 < 10
- 所有 `public` API 具 `/// <summary>` 註釋
- 事件發布／訂閱透過 EventBus，**禁止**直接 C# event 跨系統（Time System 內部如需私有事件可例外）

### 6.2 時間精度與效能

- `Update` 內避免 allocation（熱路徑）：`_missionTimers` 使用 `List<MissionTimer>` 或 `Dictionary<string, MissionTimer>`，不每幀 new
- 大量任務（>100）時仍需維持 60 FPS；若效能疑慮，可在 Codex 回報時提議 `Dictionary` + `SortedSet` 優化結構

### 6.3 測試

- EditMode：純邏輯單元測試（`Initialize` 模擬離線、時間戳推算、EventBus 訂閱／發布）
- PlayMode：`Update` 驅動的即時模式、`PauseTick`、`OnSecondTick` 發布節奏
- 測試 CSV 獨立於生產 CSV，放在測試 asmdef 內

---

## 7. 驗收條件（Acceptance Criteria）

對應 GDD §8：

| AC ID | 測試項目 | 通過條件 |
|-------|----------|---------|
| AC-TS-01 | 即時 1 分鐘結算 | `RegisterMission("m1", now, 60)` → 60 秒後 `OnMissionExpired("m1")` 被發布恰一次 |
| AC-TS-02 | 同幀多任務到期 | 兩任務同時到期，`OnMissionExpired` 皆被發布，無遺漏 |
| AC-TS-03 | `remainingSeconds = 0` | 下一幀即觸發 `OnMissionExpired` |
| AC-TS-04 | 離線 `Initialize` 只發 pending | 模擬離線 5 分鐘 + 1 個已到期任務，`Initialize` 後**僅**發 `OnOfflinePendingEvent`，**不**發 `OnMissionExpired` / `OnOfflineResolved` / `OnDailyReset` |
| AC-TS-05 | `ConfirmOfflineResolution` 依序發布 | 承 AC-TS-04，呼叫 `ConfirmOfflineResolution()` 後依序發布 `OnMissionExpired("m1")` → `OnOfflineResolved(300, 1)` |
| AC-TS-06 | 離線無任務且無跨日 | 模擬離線 5 分鐘但無進行中任務且未跨日 → **不**發 `OnOfflinePending`；**直接**發 `OnOfflineResolved(300, 0)`；`_offlineState` 為 `Resolved`（對應 GDD §5.1） |
| AC-TS-06b | 離線無任務但跨日 | 模擬離線跨越 `DAILY_RESET_HOUR` 但無到期任務 → 發 `OnOfflinePending`（讓 UI 顯示「跨日」摘要）；`Confirm` 後依序 `OnDailyReset` → `OnOfflineResolved` |
| AC-TS-07 | 時間調回 | `lastActiveTimestamp > now` → `offlineSeconds = 0`，**不**發 `OnOfflinePending`，狀態直接為 `Resolved` |
| AC-TS-08 | 離線上限 | 模擬離線 8 天 → `offlineSeconds` 截斷為 `604800`，不 crash |
| AC-TS-09 | 即時每日重置 | 即時模式下模擬跨越 UTC `DAILY_RESET_HOUR`，`OnDailyReset` 發布一次 |
| AC-TS-10 | 離線跨多日 | 離線跨 3 個 UTC 00:00，`summary.CrossesDailyReset == true`；`Confirm` 後 `OnDailyReset` 僅發一次 |
| AC-TS-11 | Confirm 順序保證 | 離線同時跨越結算與重置，`Confirm` 發布順序為：`OnMissionExpired` → `OnDailyReset` → `OnOfflineResolved` |
| AC-TS-12 | `Confirm` 冪等 | 連續呼叫 `ConfirmOfflineResolution()` 3 次，事件僅發布一輪 |
| AC-TS-13 | 即時與離線一致 | 相同任務在即時模式與離線模式結算時機相差 ≤ 1 秒 |
| AC-TS-14 | `PauseTick` 停止即時 tick | 呼叫 `PauseTick()` 後 5 秒內 `OnSecondTick` 不發布 |
| AC-TS-15 | `PauseTick` 阻斷即時每日重置 | `PauseTick()` 後即使跨越 `DAILY_RESET_HOUR`，`OnDailyReset`（即時路徑）不發 |
| AC-TS-16 | `PauseTick` **不**阻斷離線流程 | `PauseTick()` 後呼叫 `Initialize` 與 `ConfirmOfflineResolution`，`OnOfflinePending` / `OnMissionExpired` / `OnDailyReset` / `OnOfflineResolved` 仍正常發布 |
| AC-TS-17 | `PauseTick` 冪等 | 連續呼叫 `PauseTick()` 3 次不拋例外 |
| AC-TS-18 | `PauseTick` 後 `NowUTC` 仍可用 | `PauseTick()` 後 `NowUTC` 正確回傳 |
| AC-TS-19 | `OnMinuteTick` 節奏 | 連續執行 60 秒後 `OnMinuteTick` 發一次；再 60 秒再一次；其 payload `NowUTC` 等於第 60/120 次 `OnSecondTick` 的值 |
| AC-TS-20 | 離線不補 `OnMinuteTick` | 離線 10 分鐘後重啟，`Initialize` + `Confirm` 期間**不**發 `OnMinuteTick`；啟動後累積 60 秒才發第一次 |
| AC-TS-21 | 任務到期去重 | Timer 到期後連續 3 秒未呼叫 `UnregisterMission`，`OnMissionExpired` 只發布恰一次（§4.5a） |
| AC-TS-22 | 離線與即時不重發 | 離線 `Confirm` 發過 `OnMissionExpired("m1")` 後，即時模式同一 Timer 仍在清單中 → 不再發一次 |
| AC-TS-23 | 跨年每日重置 | 模擬 UTC 12/31 23:59 → 1/1 01:00 跨越，`OnDailyReset` 正確發一次（驗證使用完整 UtcDate 而非 DayOfYear） |
| AC-TS-24 | `UnregisterMission` 清 HashSet | 呼叫 `UnregisterMission("m1")` 後，若同 id 再次 `RegisterMission` 並到期 → `OnMissionExpired` 可再次發（不被去重阻擋） |
| AC-TS-25 | SystemConstants 註冊可用 | 啟動後 `TimeSystem` `Awake` 能讀到 `DAILY_RESET_HOUR` = 0、`OFFLINE_MAX_SECONDS` = 604800（證明 `RegisterSystemConstantsTable` 在 DataManager `Awake` 前已執行） |
| AC-TS-26 | SystemConstants 冪等重複註冊 | F-02 與 F-03 皆註冊 `SystemConstants` → DataManager 僅載入一次，無 `LogError`；`GetFloat`/`GetInt` 正常運作 |
| AC-EB-01 | EventBus 泛型訂閱 | `Subscribe<OnSecondTickEvent>(handler)` + `Publish(event)` → handler 被呼叫一次 |
| AC-EB-02 | EventBus 退訂 | `Unsubscribe` 後 `Publish` 不觸發 handler |
| AC-EB-03 | EventBus 字串鍵 | `Subscribe("OnDailyReset", handler)` + `Publish("OnDailyReset")` → handler 被呼叫 |
| AC-EB-04 | EventBus 多訂閱者 | 同一事件兩個 handler 皆被觸發，順序為 FIFO 或 LIFO（Codex 回報中宣告並一致實作） |
| AC-EB-05 | EventBus 生命週期隔離 | 發布者 A 呼叫自身的 OnDestroy 清理不影響訂閱者 B 的訂閱（驗證發布者不清空他人訂閱） |

---

## 8. 交付物清單（Deliverables）

1. **原始碼**
   - `Assets/Scripts/Core/Events/EventBus.cs`
   - `Assets/Scripts/Core/Events/GameEvents.cs`（包含 `OnSecondTickEvent` / `OnMinuteTickEvent` / `OnMissionExpiredEvent` / `OnOfflinePendingEvent` / `OnOfflineResolvedEvent`；**不引用** Gameplay namespace）
   - `Assets/Scripts/Core/Time/TimeSystem.cs`（含 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] RegisterTables()` 註冊 `SystemConstants`）
   - `Assets/Scripts/Core/Time/MissionTimer.cs`
   - `Assets/Scripts/Core/Time/OfflineSummary.cs`
2. **測試**
   - `Assets/Tests/EditMode/Core/Events/EventBusTests.cs`
   - `Assets/Tests/EditMode/Core/Time/TimeSystemTests.cs`
   - `Assets/Tests/PlayMode/Core/Time/TimeSystemPlayModeTests.cs`
   - `Assets/Tests/EditMode/Core/Time/TestResources/SystemConstants.csv`
   - 對應 asmdef 檔
3. **專案設定**
   - `ProjectSettings/ScriptExecutionOrder.asset` 更新（`TimeSystem: -900`）
4. **實作回報**
   - EventBus 多訂閱者觸發順序（FIFO 或 LIFO）
   - 離線兩段式流程的狀態機圖（`Uninitialized → Pending → Resolved`）
   - `_missionTimers` 選用資料結構與理由
   - `Initialize`／`ConfirmOfflineResolution`／`PauseTick` 互動矩陣（Pause 前/後、各階段行為對照表）
   - AC 通過清單

---

## 9. 審查重點（Review Checklist）

- [ ] 時間一律 UTC Unix timestamp（`long`）
- [ ] `Update` 使用 `Time.unscaledDeltaTime`
- [ ] 掉幀補發 `while` 迴圈存在
- [ ] 時間戳法（非累積扣減）計算 `remainingSeconds`
- [ ] **兩段式離線流程**：有任務／跨日才發 `OnOfflinePending`；無任務+無跨日直接發 `OnOfflineResolved`
- [ ] `ConfirmOfflineResolution` 冪等；事件順序 `OnMissionExpired` → `OnDailyReset` → `OnOfflineResolved`
- [ ] `PauseTick` 冪等、不影響 `NowUTC`、**不影響離線模式**（`Initialize` / `Confirm` 正常運作）
- [ ] **任務到期去重**：同 `missionInstanceId` 的 `OnMissionExpired` 只發一次；`UnregisterMission` 清除 HashSet
- [ ] 每日重置使用**完整 UTC Date**，跨年正確
- [ ] 離線不補 `OnMinuteTick`
- [ ] **`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] RegisterTables()`** 註冊 `SystemConstants` 表格
- [ ] EventBus 訂閱／退訂配對；發布者不清空他人訂閱
- [ ] **不**建立 `BankruptcyWarningState.cs` 占位檔；`Core/Events/GameEvents.cs` 只放 Core 層事件，**不**引用 `Gameplay.*` namespace
- [ ] `RegisterMission` 單位為秒，於 XML 註釋明確說明
- [ ] 註釋繁體中文，公開 API 具 XML 註釋
- [ ] 任務計時器支援序列化（為 FT-10 做準備）

---

## 10. Codex 調用參考

**階段一（read-only）**：

```
cd: D:/AI Repository/02_Project/SGA_2026_gamejam/TheGuild-SGA2026GameJam-phase2/TheGuild-unity
sandbox: "read-only"
SESSION_ID: <承接 F-01 session 或開新 session>
PROMPT:
依據 production/work-items/[F-02] time-system.md 實作 F-02 Time System 與 EventBus 基礎設施。
請先閱讀工項需求書與 design/GDD/[F-02] time-system.md，確認 F-01 DataManager 已存在於 Assets/Scripts/Core/Data/。
實作重點：
1. EventBus 支援泛型事件與字串鍵事件兩種模式（§4.3 + §4.11a 生命週期契約）
2. TimeSystem Update 迴圈與 PauseTick 冪等性（§4.5、§4.8）
3. **兩段式離線流程**（§4.6）：Initialize 只計算 + 發 OnOfflinePending；ConfirmOfflineResolution 才依序發布 OnMissionExpired → OnDailyReset → OnOfflineResolved
4. PauseTick **只影響即時模式**，不影響離線 Initialize / Confirm
5. **不建立** BankruptcyWarningState 占位檔（由 F-03 唯一擁有）
6. RegisterMission 單位為秒；分鐘→秒的轉換由呼叫方（FT-02）負責
7. EditMode + PlayMode 測試覆蓋所有 AC
完成後回報完整程式碼、實作決策、AC 通過清單，等待審查。
```

**階段二**：審查 APPROVED 後切 `workspace-write` 寫入。
