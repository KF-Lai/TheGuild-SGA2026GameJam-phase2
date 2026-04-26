# 【F-02-FSD】功能規格說明書 — Time System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【F-02】time-system.md`（版本：2026-04-19，狀態：已完成） |
| 對應 Data-Specs | `【F-01-DS】system-constants.md`（共用常數表） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（自檢）；Claude Code 主體（方案 A 採納後 patch；FSD-index v2 對齊 patch） |
| 狀態 | 已完成 |
| 最近更新 | 2026-04-26（採納 §8.3 方案 A 並同步 §2.4 / §2.5 / §4.4 / §5.2 / §5.3 / §5.4 / §7 / §8；對 F-02 與 FT-02 GDD 加 FSD 回註；對齊 FSD-index v2 升級規範同步 §0 / §2.2 / §6.1 / §6.3 結構） |

---

## 1. 概要（Overview）

### 1.1 系統範圍

Time System 是全專案唯一的時間來源，提供以 UTC Unix timestamp 為基準的「即時模式」與「離線模式」兩種時間推進機制。即時模式以 `Update()` 累積 `Time.unscaledDeltaTime` 並透過 EventBus 廣播 `OnSecondTick` / `OnMinuteTick`；離線模式由 Save/Load 系統觸發 `Initialize(lastActiveTimestamp)`，比對當前 UTC 計算離線秒數並廣播 `OnOfflineResolved`。同時負責每日重置（UTC `DAILY_RESET_HOUR`:00 跨越）與 Game Over 階段的 Tick 暫停。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**

- 即時模式秒級／分鐘級 tick 廣播（`OnSecondTick` / `OnMinuteTick`）。
- 離線時差計算與 `OnOfflineResolved` 事件發布。
- 每日重置事件（`OnDailyReset`）跨日偵測（即時與啟動兩條路徑）。
- 提供 `NowUTC` 唯一時間查詢 API。
- 提供 `PauseTick()` 冪等暫停 API（Game Over 階段使用）。
- 時間欺騙防護（負離線秒數視為 0、超上限截斷）。

**Out-of-Scope**

- 任務剩餘秒數計算與任務到期事件（屬 FT-02 §3.7 `TickCompletionCheck`）。
- 離線摘要 UI 呈現（屬 UI 層；UI 訂閱 FT-02 `OnOfflineMissionsResolved` 取得完整摘要）。
- 離線完成任務數計算（`completedCount`）與 `OnOfflineMissionsResolved` 發布（屬 FT-02 §6.1 FSD 回註職責；採方案 A 後 F-02 不持有任務列表故不負責）。
- 「離線摘要 DTO」聚合（屬 FT-02／UI 層；F-02 僅提供 `OfflineSeconds` 原始值）。
- 離線期間 NPC 自主接單補算（屬 FT-11 Offline Resolver；Jam 階段不實作）。
- 網路時間校正（單機範圍）。
- 玩家手動暫停／時間加速（不在當前範圍）。

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，補充程式可驗證條件：

1. **即時模式 EditMode 測試**：以 mock `Time.unscaledDeltaTime` 驅動，60 次 1 秒推進後 `OnSecondTick` 被發布 60 次、`OnMinuteTick` 被發布 1 次。
2. **掉幀補發測試**：單次 deltaTime = 2.5f 推進，`OnSecondTick` 在同一幀內被發布 2 次（補發第 3 秒餘額剩 0.5）。
3. **離線計算 EditMode 測試**：mock `lastActiveTimestamp = now - 3600`，呼叫 `Initialize`，`OnOfflineResolved.offlineSeconds == 3600`。
4. **離線截斷測試**：`lastActiveTimestamp = now - 8 days`，截斷為 `OFFLINE_MAX_SECONDS`（604800）。
5. **負離線測試**：`lastActiveTimestamp > now`，`OnOfflineResolved.offlineSeconds == 0`，不觸發任何結算。
6. **每日重置測試**：mock UTC 日期跨越，`OnDailyReset` 發布恰一次。
7. **`PauseTick` 冪等測試**：連續呼叫 3 次無例外；呼叫後至少 5 秒內 `OnSecondTick` 不再發布；`NowUTC` 仍可正常回傳。
8. **CSV 載入零錯誤**：DataManager 讀取 `DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS` 無 KeyNotFoundException。

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- `【F-02】time-system.md` §1 概要、§2 玩家幻想、§3.0–§3.7 詳細規則、§4.1–§4.4 公式、§5.1–§5.4 邊緣案例、§6.1–§6.3 依賴、§7 可調參數、§8 驗收標準。
- `【F-01】data-manager.md` §3 詳細規則：`GetInt(key)` API。
- `【FT-02】mission-dispatch.md` §3.7：`TickCompletionCheck` 訂閱 `OnSecondTick`。
- `【FT-06】guild-core.md` §4.5：Game Over 狀態機呼叫 `PauseTick()`。

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `DAILY_RESET_HOUR`（int，預設 0，安全範圍 0–23） | 每日重置觸發小時（UTC） |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `OFFLINE_MAX_SECONDS`（int，預設 604800，安全範圍 86400–604800） | 離線秒數截斷上限 |

### 2.3 上游依賴系統

| 系統               | 依賴內容                                                          |
| ------------------ | ----------------------------------------------------------------- |
| F-01 DataManager   | `GetInt("DAILY_RESET_HOUR")`、`GetInt("OFFLINE_MAX_SECONDS")` 載入常數 |
| Core EventBus      | 透過 `EventBus.Publish<T>(payload)` 發布事件                     |
| FT-10 Save/Load    | 載入存檔後呼叫 `Initialize(lastActiveTimestamp)` 觸發離線計算     |
| FT-06 Guild Core   | Game Over 階段 2 呼叫 `PauseTick()` 凍結時間                      |

### 2.4 下游被依賴系統

| 系統                          | 訂閱事件 / 呼叫 API                                                |
| ----------------------------- | ------------------------------------------------------------------ |
| FT-02 Mission Dispatch        | `OnSecondTick` 驅動 `TickCompletionCheck`；派遣時讀取 `NowUTC`；`OnOfflineResolved(offlineSeconds)` 觸發 `completedCount` 計算與 `OnOfflineMissionsResolved` 發布（見 FT-02 §6.1 FSD 回註） |
| FT-01 Adventurer Recruitment  | `OnDailyReset` 重置免費刷新次數                                    |
| C-06 World Danger System      | `OnDailyReset` 觸發 `CheckLevelUp()`；`NowUTC` 計算 `GetElapsedDays()` |
| FT-10 Save/Load               | 儲存時讀 `NowUTC` 寫入 `lastActiveTimestamp`                       |
| F-03 Resource Management      | `OnSecondTick` 驅動破產倒數；`OnOfflineResolved(offlineSeconds)` 判定離線期間是否觸發破產（不需 `completedCount`） |
| C-02 Adventurer Management    | `OnSecondTick` 驅動 Wounded 恢復計時                               |
| FT-03 NPC Decision            | `OnMinuteTick` 驅動 NPC 自主接單檢查                               |
| FT-06 Guild Core              | `NowUTC` 取 Game Over 時間戳                                       |

> 註：離線摘要 UI 不再直接訂閱 F-02 事件；改訂閱 FT-02 `OnOfflineMissionsResolved` 取得含 `completedCount` 的完整摘要（採方案 A 後的職責劃分，見 §8.5）。

### 2.5 跨系統事件契約

| 事件                | 方向     | Payload                                       | 發布時機 / 訂閱目的                           |
| ------------------- | -------- | --------------------------------------------- | --------------------------------------------- |
| `OnSecondTick`      | F-02 → * | `long currentUTCTimestamp`                    | 即時模式每累積 1 秒；下游驅動秒級檢查         |
| `OnMinuteTick`      | F-02 → * | `long currentUTCTimestamp`                    | 即時模式每累積 60 秒；下游驅動分鐘級檢查      |
| `OnDailyReset`      | F-02 → * | 無（空 struct 或 unit）                       | UTC `DAILY_RESET_HOUR`:00 跨越；下游重置每日狀態 |
| `OnOfflineResolved` | F-02 → FT-02 / F-03 | `long offlineSeconds`                | `Initialize` 計算完成；FT-02 推進任務並發 `OnOfflineMissionsResolved`、F-03 判定離線破產 |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家認為「時間真實流逝」——下班後打開遊戲，早上派出去的隊伍真的回來了（或永遠回不來了）。每次開啟遊戲像打開一封冒險報告，而非操作即時系統。時間在玩家不在場時持續推進。

### 3.2 系統目的還原

提供全專案唯一的時間來源，支援即時與離線兩種模式。即時模式驅動秒級／分鐘級事件；離線模式以 UTC 時差一次性推進進行中任務。同時管理每日重置與時間欺騙防護。

### 3.3 對映表

| 幻想／目的                       | 玩家可感知的具體現象                                       | 對應的技術手段                                                                                          |
| -------------------------------- | ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| 時間真實流逝（即時）             | 任務倒數計時器每秒減少，倒數歸零後自動觸發結算             | `Update()` 累積 `Time.unscaledDeltaTime`，每滿 1 秒發 `OnSecondTick`；FT-02 訂閱以推進任務              |
| 時間真實流逝（離線）             | 關遊戲一段時間後重啟，看到「離開了 X 小時 Y 分鐘」摘要畫面 | `Initialize(lastActiveTimestamp)` 計算 `nowUTC - lastActiveTimestamp`，發 `OnOfflineResolved`           |
| 時間單位統一、跨時區一致         | 玩家換時區或調系統時間，遊戲時間不被影響                   | 全部時間以 UTC `long`（Unix timestamp，秒）儲存；`DateTimeOffset.UtcNow.ToUnixTimeSeconds()`            |
| 防止時間欺騙                     | 玩家把系統時間調回過去後重啟，沒有任何結算發生             | `offlineSeconds = clamp(now - last, 0, OFFLINE_MAX_SECONDS)`；下限 0、上限 7 天                         |
| 每日節律（招募刷新、世界升階）   | 過了凌晨 0 點，招募板免費刷新次數歸零                      | 啟動時比對 UTC 日期跨越；即時模式偵測 `DAILY_RESET_HOUR`:00 跨越；發 `OnDailyReset`                     |
| 低頻週期性檢查（NPC 自主接單）   | NPC 每隔一段時間自主決定要不要接任務                       | 內部 `_minuteAccumulator` 計數 60 次 `OnSecondTick` 後發 `OnMinuteTick`                                 |
| 掉幀不丟秒數                     | 即使遊戲卡頓 2 秒，倒數仍正確推進 2 秒                     | `while (_accumulator >= 1f)` 補發遺漏 tick                                                              |
| Game Over 終態凍結               | 玩家確認破產訃聞後，畫面靜止、不再有事件觸發               | `PauseTick()` 冪等 API；旗標 `_tickPaused` 在 `Update()` 早退                                           |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。

### 4.2 拆分理由

不拆分理由（依 FSD-index §2.4）：

1. **規模未達門檻**：核心邏輯為 `Update()` tick 累積、`Initialize` 離線計算、每日重置偵測、`PauseTick`，預估 < 300 行。
2. **職責耦合緊**：三項主功能皆共享 `_accumulator`、`_tickPaused`、`NowUTC` 內部狀態；分散到多個 Script 反而需要額外暴露 internal API。
3. **既有實作已單檔**：`TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs` 已是單一 MonoBehaviour 實作，本 FSD 沿用而非重組。
4. **不符拆分標準**：GDD §3 內部無明顯職責分區（每日重置與每秒 tick 共享 `Update()` 流程）。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script              | 路徑                                                            | 職責（SRP 一句話）                                                                                                | 主要依賴                                | 預估規模 |
| ------------------- | --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | --------------------------------------- | -------- |
| `TimeSystem`        | `Assets/Scripts/Core/Time/TimeSystem.cs`                        | 維持 UTC 時間源、累積 `Update()` deltaTime 並廣播 tick／每日重置／離線結算事件，提供 `Initialize` 與 `PauseTick`。 | `IDataManager`、`EventBus`              | 200~300 行 |
| `TimeEvents`        | `Assets/Scripts/Core/Time/TimeEvents.cs`                        | 集中定義 `OnSecondTick` / `OnMinuteTick` / `OnDailyReset` / `OnOfflineResolved` 的 payload struct（避免裝箱）。   | —                                       | < 50 行   |

> 註：既有檔案 `Assets/Scripts/Core/Time/MissionTimer.cs` 屬 FT-02 Mission Dispatch 範疇（任務計時器），非本 FSD 規劃對象。本 FSD 不對該檔做變更。

### 4.5 類別關係（可選）

```
TimeSystem (MonoBehaviour)
  ├─ depends on  : IDataManager (取常數)
  ├─ publishes   : EventBus<OnSecondTickEvent>
  │                EventBus<OnMinuteTickEvent>
  │                EventBus<OnDailyResetEvent>
  │                EventBus<OnOfflineResolvedEvent>
  └─ exposes API : NowUTC : long
                   Initialize(long lastActiveTimestamp) : void
                   PauseTick() : void

TimeEvents     (struct payloads)    ── 事件 payload 集合（不含 OfflineSummary；採方案 A 後該 DTO 屬 FT-02／UI 層）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

| 簽名                                         | 用途                                                                                                            |
| -------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `long NowUTC { get; }`                       | 回傳當前 UTC Unix timestamp（秒），來源 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`；任何時候皆可呼叫，不受 `_tickPaused` 影響。 |
| `void Initialize(long lastActiveTimestamp)`  | Save/Load 載入完成後呼叫；計算 `offlineSeconds = clamp(NowUTC - lastActiveTimestamp, 0, OFFLINE_MAX_SECONDS)`，發 `OnOfflineResolved`；同時觸發跨日偵測補發 `OnDailyReset`（若跨越）。 |
| `void PauseTick()`                           | 設 `_tickPaused = true`，停止 `Update()` 累積與所有 tick／每日重置發布；冪等（重複呼叫無副作用）；不影響 `NowUTC` 查詢。 |

> 不提供 `ResumeTick()`：Game Over 為終態（FT-06 §4.5）。

### 5.2 事件清單

| 事件名稱                  | 方向     | Payload                                                       | 發布時機 / 訂閱目的                                                                                          |
| ------------------------- | -------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `OnSecondTickEvent`       | 發布     | `long currentUTCTimestamp`                                    | `Update()` 中 `_accumulator >= 1f` 時發布；單幀掉幀以 `while` 迴圈補發；下游：FT-02 任務計時、F-03 破產倒數、C-02 Wounded 恢復 |
| `OnMinuteTickEvent`       | 發布     | `long currentUTCTimestamp`                                    | 每發 60 次 `OnSecondTick` 後發布一次（`_minuteAccumulator >= 60`）；離線**不**補發；下游：FT-03 NPC 自主接單 |
| `OnDailyResetEvent`       | 發布     | （空 payload）                                                | (a) `Initialize` 時若 UTC 日期跨越；(b) 即時模式 `Update()` 偵測到 `DAILY_RESET_HOUR`:00 跨越；下游：FT-01 招募刷新、C-06 升階檢查 |
| `OnOfflineResolvedEvent`  | 發布     | `long offlineSeconds`                                         | `Initialize` 計算完成後立即發布；下游：FT-02（計算 `completedCount` 並另發 `OnOfflineMissionsResolved`）、F-03（離線破產判定） |

> `_tickPaused == true` 後，所有事件停止發布（包含 `OnSecondTick` 連帶的 `OnMinuteTick` 與 `OnDailyReset`）。`NowUTC` 不受影響。

### 5.3 資料結構

```csharp
// 事件 payload（struct，避免裝箱）
public readonly struct OnSecondTickEvent
{
    public readonly long CurrentUTCTimestamp;
    public OnSecondTickEvent(long t) { CurrentUTCTimestamp = t; }
}

public readonly struct OnMinuteTickEvent
{
    public readonly long CurrentUTCTimestamp;
    public OnMinuteTickEvent(long t) { CurrentUTCTimestamp = t; }
}

public readonly struct OnDailyResetEvent { }

public readonly struct OnOfflineResolvedEvent
{
    public readonly long OfflineSeconds;
    public OnOfflineResolvedEvent(long s) { OfflineSeconds = s; }
}
```

> 採方案 A 後 F-02 不持有 `OfflineSummary` DTO。UI 訂閱 FT-02 `OnOfflineMissionsResolved(offlineSeconds, completedCount, completedActiveMissionIDs[])` 取得完整摘要並自行格式化（屬 UI 層）。

內部欄位：

```csharp
[SerializeField] private bool _tickPaused;          // PauseTick 旗標
private float _accumulator;                          // 累積 unscaledDeltaTime，>=1f 時發 tick
private int   _minuteAccumulator;                    // 累積 OnSecondTick 次數，>=60 時發 OnMinuteTick
private int   _dailyResetHour;                       // 從 DataManager 載入
private int   _offlineMaxSeconds;                    // 從 DataManager 載入
private int   _lastResetUtcDayOfYear;                // 上次發 OnDailyReset 時的 UTC dayOfYear
private int   _lastResetUtcYear;                     // 上次發 OnDailyReset 時的 UTC year
```

### 5.4 內部資料流

**啟動流程（由 FT-10 觸發）**

```
FT-10.LoadComplete
  → TimeSystem.Initialize(lastActiveTimestamp)
      ├─ nowUTC := DateTimeOffset.UtcNow.ToUnixTimeSeconds()
      ├─ rawDelta := nowUTC - lastActiveTimestamp
      ├─ offlineSeconds := Clamp(rawDelta, 0, _offlineMaxSeconds)
      ├─ if (lastActiveTimestamp == 0) → 首次啟動，offlineSeconds = 0
      ├─ EventBus.Publish(new OnOfflineResolvedEvent(offlineSeconds))
      ├─ 若跨越 UTC `DAILY_RESET_HOUR`:00 → EventBus.Publish(new OnDailyResetEvent())
      └─ 更新 _lastResetUtcYear / _lastResetUtcDayOfYear
```

> **離線結算職責劃分（採方案 A，2026-04-26）**：F-02 僅發 `OnOfflineResolvedEvent(offlineSeconds)`，不計算也不攜帶 `completedCount`。FT-02 訂閱本事件後依自身 `activeMissions` 計算 `completedCount` 與完成任務 ID 清單，發 `OnOfflineMissionsResolved(offlineSeconds, completedCount, completedActiveMissionIDs[])` 供 UI 訂閱（見 FT-02 §6.1 FSD 回註）。F-03 訂閱 `OnOfflineResolvedEvent` 時僅讀 `OfflineSeconds` 判定離線破產，不需 `completedCount`。

**即時模式（每幀）**

```
Update():
    if _tickPaused: return
    _accumulator += Time.unscaledDeltaTime
    while _accumulator >= 1f:
        _accumulator -= 1f
        nowUTC := DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        EventBus.Publish(new OnSecondTickEvent(nowUTC))
        _minuteAccumulator += 1
        if _minuteAccumulator >= 60:
            _minuteAccumulator = 0
            EventBus.Publish(new OnMinuteTickEvent(nowUTC))
        // 跨日偵測
        utcNow := DateTimeOffset.UtcNow
        if (utcNow.Year, utcNow.DayOfYear) != (_lastResetUtcYear, _lastResetUtcDayOfYear)
            && utcNow.Hour >= _dailyResetHour:
            EventBus.Publish(new OnDailyResetEvent())
            _lastResetUtcYear     = utcNow.Year
            _lastResetUtcDayOfYear = utcNow.DayOfYear
```

**Game Over 流程（由 FT-06 觸發）**

```
FT-06.OnGameOverStage2Confirmed
  → TimeSystem.PauseTick()
      └─ _tickPaused := true   // 冪等；之後 Update() 早退
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SystemConstants.csv` | `DAILY_RESET_HOUR` | `【F-01-DS】system-constants.md` | 每日重置觸發小時（UTC，0–23） | `TimeSystem.Awake()` 透過 `IDataManager.GetInt` |
| `SystemConstants.csv` | `OFFLINE_MAX_SECONDS` | `【F-01-DS】system-constants.md` | 離線秒數截斷上限（86400–604800） | `TimeSystem.Awake()` 透過 `IDataManager.GetInt` |

### 6.2 引用的 ScriptableObject

無。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `DAILY_RESET_HOUR`（每日重置小時） | `SystemConstants.csv` → `DAILY_RESET_HOUR` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `OFFLINE_MAX_SECONDS`（離線截斷上限） | `SystemConstants.csv` → `OFFLINE_MAX_SECONDS` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 任務時長（duration） | C-01 `MissionDifficultyTable.baseDuration` 欄位（屬 C-01 / FT-02 範疇；原 `DurationTable.csv` 已合併） | 對應「四、程式實作原則」第 9 條：參數表格化；F-02 不應出現任何任務時長相關常數 |

**允許的常數**（語言／單位定義，非可調參數，無對應表格）：

| 常數 | 出現位置 | 性質 |
| --- | --- | --- |
| `1f` | `_accumulator >= 1f` | 時間單位定義（1 秒），非可調 |
| `60` | `_minuteAccumulator >= 60` | 時間單位定義（1 分鐘 = 60 秒），非可調 |
| `3600` | `OfflineHours` 公式（GDD §4.3） | 時間單位定義（1 小時 = 3600 秒），非可調 |

---

## 7. 邊緣案例對策（Edge Case Handling）

對齊 GDD §5：

| GDD §5 案例                                               | 程式處理方式                                                                                                                          | 涉及 Script   | 驗證方式                                                          |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ------------- | ----------------------------------------------------------------- |
| §5.1 `offlineSeconds < 0`（系統時間調回）                  | `Initialize` 內以 `Math.Max(0, raw)` 截為 0；發 `OnOfflineResolvedEvent(0)`；訂閱端（FT-02 / F-03）判 `offlineSeconds == 0` 時跳過後續處理 | `TimeSystem`  | EditMode：mock `lastActiveTimestamp = NowUTC + 100`，斷言事件 payload `offlineSeconds == 0` |
| §5.1 `offlineSeconds > OFFLINE_MAX_SECONDS`               | `Initialize` 內以 `Math.Min(raw, _offlineMaxSeconds)` 截上限；正常發 `OnOfflineResolved`                                              | `TimeSystem`  | EditMode：mock 8 天離線，斷言 `offlineSeconds == 604800`         |
| §5.1 首次啟動（無 `lastActiveTimestamp`）                  | FT-10 傳入 `lastActiveTimestamp = 0`；`Initialize` 偵測 `lastActiveTimestamp == 0` 時直接設 `offlineSeconds = 0`、不跨日偵測          | `TimeSystem`  | EditMode：傳 0，斷言 `offlineSeconds == 0` 且 `OnDailyReset` 不發 |
| §5.1 離線期間無進行中任務                                  | F-02 一律發 `OnOfflineResolvedEvent(offlineSeconds)`，不區分有無任務；FT-02 訂閱後若 `completedCount == 0` 自行決定是否發 `OnOfflineMissionsResolved`，UI 摘要顯示判斷屬 FT-02／UI 範疇 | `TimeSystem` | EditMode：mock 5 分鐘離線、零任務；斷言 F-02 仍發出 `OnOfflineResolvedEvent` 1 次 |
| §5.2 同一幀內多個任務同時到期                              | 屬 FT-02 範疇；F-02 僅保證 `OnSecondTick` 每秒一次、payload `currentUTCTimestamp` 一致，FT-02 自行佇列化結算                          | （無，跨系統） | FT-02 EditMode 測試                                              |
| §5.2 任務派遣後立即關閉再開啟                              | `Initialize` 觸發 `OnOfflineResolved`，FT-02 收到後依 `dispatchTimestamp + durationSeconds - NowUTC` 重算（採目標時間戳，不累積誤差）  | `TimeSystem`  | FT-02 EditMode：派遣後立即離線 3600 秒，斷言任務剩餘秒數正確     |
| §5.2 結算瞬間關閉遊戲                                     | 與上同；`remainingSeconds <= 0` 由 FT-02 在 `OnOfflineResolved` 訂閱端判定                                                            | （無，跨系統） | FT-02 EditMode 測試                                              |
| §5.3 離線跨越多個 UTC 00:00                                | `Initialize` 比對 `(年, dayOfYear)` 變化，**只發一次** `OnDailyResetEvent`；不論跨幾天都只發一次（重置是狀態，不是計數）              | `TimeSystem`  | EditMode：mock 3 天離線，斷言 `OnDailyReset` 被發布恰 1 次       |
| §5.3 玩家開著遊戲跨 UTC 00:00                              | `Update()` 每秒比對 `(_lastResetUtcYear, _lastResetUtcDayOfYear)` 與當前；跨越且 `Hour >= _dailyResetHour` 時即時發 `OnDailyReset`     | `TimeSystem`  | EditMode：mock 系統時間跨日，斷言 `Update()` 在跨越秒內發 `OnDailyReset` |
| §5.3 每日重置與離線結算同時發生                            | `Initialize` 內**先**發 `OnOfflineResolved`，**後**發 `OnDailyReset`（順序固定，不依賴訂閱者執行順序）                                  | `TimeSystem`  | EditMode：mock 3 天離線，斷言 `OnOfflineResolved` 先於 `OnDailyReset` |
| §5.4 `remainingSeconds = 0`（顯示「即將完成」）            | 屬 UI / FT-02 範疇；F-02 提供 `NowUTC` 與 `OnSecondTick`，UI 自行判定                                                                  | （無，跨系統） | UI 整合測試                                                      |
| §5.4 任務已過期但尚未結算（顯示「結算中...」）             | 屬 UI / FT-02 範疇；F-02 不參與                                                                                                       | （無，跨系統） | UI 整合測試                                                      |
| §3.7 `PauseTick` 後跨 UTC 00:00                            | `Update()` 早退（`if _tickPaused: return`），不發 `OnDailyReset` 也不發 `OnSecondTick`                                                | `TimeSystem`  | EditMode：呼叫 `PauseTick`，mock 跨日，斷言 `OnDailyReset` 不發  |
| §3.7 `PauseTick` 重複呼叫                                  | `_tickPaused = true`（冪等覆寫）；無例外                                                                                              | `TimeSystem`  | EditMode：連續呼叫 3 次，斷言不拋例外                            |
| §3.2 單幀掉幀超過 1 秒                                     | `while (_accumulator >= 1f)` 迴圈補發；同一幀內可發多次 `OnSecondTick`，必要時連帶補發 `OnMinuteTick`                                  | `TimeSystem`  | EditMode：mock deltaTime = 2.5f，斷言同一幀內 `OnSecondTick` 發 2 次 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目                                  | 對應 FSD 章節                | 是否對齊 | 備註                                                          |
| -------------------------------------------- | ---------------------------- | -------- | ------------------------------------------------------------- |
| §3.0.1 Script Execution Order                | §4.4（Script 註）            | 對齊     | 由 Unity Project Settings 配置，FSD 註明依賴順序             |
| §3.0.2 Awake 讀常數                          | §5.4 啟動流程、§6.1          | 對齊     | `IDataManager.GetInt` 兩個鍵                                  |
| §3.0.3 OnEnable/OnDisable 對稱訂閱           | §4.4 註                      | 對齊     | 遵循 `.claude/rules/gameplay-code.md` 預設模式                |
| §3.0.4 `Initialize(lastActiveTimestamp)` 呼叫 | §5.1、§5.4                   | 對齊     | API 簽名一致                                                  |
| §3.1.1 UTC `long` 儲存                        | §5.3、§5.4                   | 對齊     | `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`                   |
| §3.1.2 任務時長分→秒                          | §6.3                          | 對齊     | F-02 不持有任務時長，由 FT-02 / C-01 處理                     |
| §3.1.3 顯示取整 30 分鐘                       | §6.3 表後備註                 | 對齊     | UI 層處理，F-02 僅提供 `NowUTC`                              |
| §3.2.1 `OnSecondTick` + while 補發           | §5.4 即時模式偽碼            | 對齊     | 含 while 補發語意                                             |
| §3.2.2 任務計時由 FT-02 維護                  | §1.2 Out-of-Scope            | 對齊     | F-02 不重算任務剩餘秒數                                       |
| §3.2.4 UI 倒數以分鐘為單位                    | §6.3 表後備註                 | 對齊     | UI 層處理                                                     |
| §3.2.5 `OnMinuteTick` 每 60 秒一次            | §5.2、§5.4                   | 對齊     | `_minuteAccumulator >= 60`                                    |
| §3.2.6 離線不補發 `OnMinuteTick`              | §5.4、§7（OnMinuteTick 列）   | 對齊     | `Initialize` 不操作 `_minuteAccumulator`，重啟後歸零         |
| §3.3.1 關閉前記錄 `lastActiveTimestamp`       | §2.4（FT-10 列）              | 對齊     | FT-10 職責，F-02 不負責                                       |
| §3.3.2 `offlineSeconds = now - last`          | §5.1、§5.4                   | 對齊     | `Initialize` 內計算                                           |
| §3.3.3 `OFFLINE_MAX_SECONDS` 截斷            | §6.1、§7、§5.4               | 對齊     | `Math.Min` 截上限                                             |
| §3.3.4 對所有任務 `remainingSeconds -= offlineSeconds` | §5.4 流程備註            | 對齊     | 由 FT-02 訂閱 `OnOfflineResolved` 後執行（採方案 A）         |
| §3.3.5 離線摘要畫面                           | §1.2 Out-of-Scope、§2.4 註    | 對齊     | F-02 不負責摘要；UI 訂閱 FT-02 `OnOfflineMissionsResolved`（採方案 A，見 §8.5） |
| §3.3.6 確認後執行結算                         | §2.4 註                       | 對齊     | UI 確認後由 FT-02 / FT-04 協作完成（採方案 A）               |
| §3.4.1 跨日偵測（啟動）                       | §5.4 啟動流程                 | 對齊     | `(年, dayOfYear)` 比對                                       |
| §3.4.2 `DAILY_RESET_HOUR` 跨越                | §5.4 即時模式                 | 對齊     | `Hour >= _dailyResetHour` 條件                                |
| §3.4.3 訂閱者列表                             | §2.4                          | 對齊     | 下游表完整列出                                                |
| §3.4.4 即時跨重置                             | §5.4 即時模式                 | 對齊     | 每秒比對                                                      |
| §3.5.1 UTC 儲存防時區欺騙                     | §3.3、§5.3                   | 對齊     | 全部 UTC                                                      |
| §3.5.2 負離線視為 0                           | §5.1（Initialize）、§7        | 對齊     | `Math.Max(0, raw)`                                            |
| §3.5.3 不實作網路時間校正                     | §1.2 Out-of-Scope             | 對齊     | 明列                                                          |
| §3.7 `PauseTick` 冪等 + Update 早退           | §5.1、§5.4 Game Over          | 對齊     | 旗標 + 早退                                                  |

### 8.2 公式對齊或替代說明

GDD §4 公式全部沿用，無替代：

- §4.1 離線時間：採 `Clamp(NowUTC - lastActiveTimestamp, 0, _offlineMaxSeconds)`，與 GDD 一致。
- §4.2 任務剩餘時間：採「目標完成時間戳」`dispatchTimestamp + durationSeconds - NowUTC`，由 FT-02 計算（F-02 提供 `NowUTC`）。
- §4.3 離線摘要顯示格式：採方案 A 後 F-02 **不持有** `OfflineSummary` DTO；公式中的 `OfflineHours = OfflineSeconds / 3600` 與 `OfflineMinutes = (OfflineSeconds % 3600) / 60` 由 UI 自行計算；`completedCount` 由 FT-02 在 `OnOfflineMissionsResolved` 提供。F-02 僅暴露 `OfflineSeconds` 原始值（GDD §4.3 公式之輸入），下游組合方式不變。
- §4.4 顯示時長格式：屬 UI 層，F-02 不實作。

### 8.3 未能實現的規則與修改建議

1. **`OnOfflineResolved.completedCount` 來源問題（已裁決：採方案 A，2026-04-26）**：GDD §6.3 原規定 payload 含 `completedCount`，但 F-02 不持有任務列表。經使用者裁決採方案 A：
   - F-02 發 `OnOfflineResolvedEvent(long offlineSeconds)`，**不**含 `completedCount`。
   - FT-02 訂閱後依自身 `activeMissions` 計算 `completedCount` 與完成任務 ID 清單，發 `OnOfflineMissionsResolved(offlineSeconds, completedCount, completedActiveMissionIDs[])`。
   - UI 訂閱 FT-02 事件取得完整摘要；F-03 訂閱 F-02 事件僅讀 `OfflineSeconds` 判定離線破產。
   - 已於 GDD §6.3、FT-02 §6.1 加 FSD 回註（見 §8.4）；本 FSD §2.4 / §2.5 / §4.4 / §5.2 / §5.3 / §5.4 / §7 同步更新。
2. **既有 Script 偏差**：`Assets/Scripts/Core/Time/MissionTimer.cs` 存在於 F-02 目錄，但職責屬 FT-02。建議於 FT-02 FSD 撰寫時遷移至 `Assets/Scripts/Gameplay/Mission/`。本 FSD 不執行遷移。
3. **Script Execution Order 配置**：GDD §3.0.1 要求 F-02 早於下游系統。建議在 Unity Project Settings → Script Execution Order 設定 `TimeSystem` 為 `-100`（DataManager `-200`，下游皆為 0）。本 FSD 不修改 Project Settings，由實作 PR 一併處理。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-26 | `【F-02】time-system.md` | §6.3 表後（`OnOfflineResolved` 列） | 採方案 A：`OnOfflineResolved` payload 僅含 `offlineSeconds`；`completedCount` 改由 FT-02 `OnOfflineMissionsResolved` 提供；§4.3 `completedCount` 改由 FT-02 提供 |
| 2026-04-26 | `【FT-02】mission-dispatch.md` | §6.1 表後 | FT-02 額外承擔：訂閱 F-02 `OnOfflineResolved` → 計算 `completedCount` → 發 `OnOfflineMissionsResolved(offlineSeconds, completedCount, completedActiveMissionIDs[])`；不修改 §3.7 既有規則，待 FT-02 FSD 細化 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-26 | `OnOfflineResolved.completedCount` 來源不明：GDD §6.3 規定 payload 含 `completedCount`，但 F-02 不持有任務列表，無法填值。屬實作介面選擇，非 GDD 內部矛盾 | F-02 GDD §6.3、§4.3；F-02 FSD §2.5 / §5.2 / §5.3 / §5.4 / §7 / §8.3；FT-02 GDD §6.1 | 使用者裁決採方案 A：F-02 不發 `completedCount`，由 FT-02 訂閱後另發 `OnOfflineMissionsResolved`。已於 F-02 GDD §6.3 與 FT-02 GDD §6.1 加 FSD 回註，本 FSD 全章對齊更新 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

| 日期       | Review 者                | 結構 | 邏輯 | GDD 對齊 | 備註                                                     |
| ---------- | ------------------------ | ---- | ---- | -------- | -------------------------------------------------------- |
| 2026-04-26 | unity-specialist subagent | 通過 | 通過 | 通過     | 自檢通過；§8.3 第 1 點 `completedCount` 來源建議待裁決 |
| 2026-04-26 | Claude Code 主體（方案 A patch） | 通過 | 通過 | 通過     | 採方案 A：§2.4 移除離線摘要 UI 直訂；§2.5 / §5.2 payload 移除 `completedCount`；§5.3 移除 `OfflineSummary` struct；§5.4 改寫流程備註；§7 §5.1 第 4 列改寫；§4.4 移除 `OfflineSummary` Script 列；§8.1 §3.3.5 / §3.3.6 備註更新；§8.2 §4.3 對齊註記；§8.3 第 1 點標已裁決；§8.4 / §8.5 補登紀錄。F-02 GDD §6.3 與 FT-02 GDD §6.1 加 FSD 回註 |
| 2026-04-26 | Claude Code 主體（FSD-index v2 對齊 patch） | 通過 | 通過 | 通過     | 對齊 FSD-index 升級規範：§0 改表格化（補 Data-Specs 列）；§2.2 改 4 欄表格（含對應 CSV、引用欄位、用途）；§6.1 補「對應 Data-Specs」欄；§6.3 改 3 欄表格（項目／來源欄位／違反原則）含允許常數列舉。語意內容無變動，僅結構同步 |
