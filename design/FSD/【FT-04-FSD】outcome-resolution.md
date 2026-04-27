# 【FT-04-FSD】功能規格說明書 — Outcome Resolution

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-04】outcome-resolution.md`（版本：2026-04-22） |
| 對應 Data-Specs | `【FT-04-DS】reputation-delta-table.md`（`ReputationDeltaTable.csv`）<br>`【F-01-DS】system-constants.md`（`SystemConstants.csv`，消費端：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`）<br>`【C-01-DS】mission-difficulty-table.md`（`MissionDifficultyTable.csv`，消費端：`baseReward`） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-04 Outcome Resolution 在 `OnMissionCompleted` 事件觸發時，對單筆 ActiveMission 執行原子性結算管線：從 FT-02 派遣快照取得成功率／死亡率，執行兩次獨立擲骰，套用 C-05 condition 特質修正（含 `on_death_survive` 救活），映射至 5 種最終結果（`Idle` / `Wounded` / `Dead` × 成功/失敗），依 `ReputationDeltaTable` 計算聲望變化，更新冒險者狀態，最後發布 `OnMissionResolved` 事件供下游系統消費。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- 訂閱 `OnMissionCompleted`，從 `ActiveMission` 快照執行結算擲骰
- 套用 C-05 `ApplyConditionTraits`（condition 特質救活、聲望 bonus、gold bonus 填值）
- 映射 5 種最終結果（`Outcome.finalStatus`）
- 計算 `reputationDelta`（基礎 + condition 疊加）並呼叫 `F03.AddReputation`
- 呼叫 C-02 更新冒險者狀態（`UpdateStatus` / `SetWounded`）
- 發布 `OnMissionResolved(Outcome)`
- 呼叫 `FT02.RemoveActiveMission` 清理 runtime 任務
- `Outcome` DTO 的建構與欄位填寫

**Out-of-Scope**
- 成功率／死亡率計算（FT-02 派遣時已快照）
- willingness / 接受拒絕（FT-03）
- 金流（傭金、賠償、`conditionGoldBonus` 的 `AddGold` 呼叫，全由 FT-05 執行）
- 冒險者 Wounded 恢復計時（C-02 `TickWoundedRecovery`）
- 結算歷史持久化（FT-10）
- condition 特質本身的擲骰邏輯（C-05 § 4.4 規格負責）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準 AC-OR-01 ～ AC-OR-28，以下為程式可驗證的補充條件：

| DoD | 驗證方式 |
| --- | --- |
| EditMode test：`finalSuccessRate=0.0` 時 1000 次擲骰全部 `isSuccess == false` | NUnit `[TestCase]` 參數化 |
| EditMode test：`finalSuccessRate=1.0` 時 1000 次擲骰全部 `isSuccess == true` | NUnit `[TestCase]` 參數化 |
| EditMode test：管線步驟 2 mock 回傳 null → `OnMissionResolved` 未發布、ActiveMission 未清理 | NUnit + mock IAdventurerRoster |
| EditMode test：`on_death_survive` 強制觸發後 `(isSuccess=T, isDead=T)` → `finalStatus == Wounded` | NUnit + mock ITraitService |
| EditMode test：B 難度成功結算後 F-03 聲望增加 `+8` | NUnit + mock IResourceService |
| EditMode test：`OnMissionResolved` 在 `RemoveActiveMission` 之前發布（訂閱者內可查到 ActiveMission） | NUnit 事件序列驗證 |
| Unity Editor 啟動後 Console 零 Error（`ReputationDeltaTable` 9 行全部載入） | UnityMCP `read_console` |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 用途 |
| --- | --- |
| §1 概要、§3.1 Outcome 資料結構 | Outcome DTO 欄位定義 |
| §3.2 結算流程 | 管線步驟 1-12 與順序約束 |
| §3.3 兩次獨立擲骰 | `RollSuccessAndDeath` 公式 |
| §3.4 condition 特質套用 | `ApplyConditionTraits` 委託規格 |
| §3.5 5 種最終結果映射 | `MapFinalStatus` 邏輯 |
| §3.6 聲望計算 | `ApplyBaseReputationDelta` 查表邏輯 |
| §3.7 冒險者狀態更新 | `ApplyAdventurerStatus` 呼叫規格 |
| §3.8 事件發布與清理 | `OnMissionResolved` 契約與 `RemoveActiveMission` 時機 |
| §3.9 查詢 API | 確認 FT-04 無對外公開 API |
| §4.1 擲骰判定公式 | 等價實作參考 |
| §4.2 聲望 delta 公式 | 等價實作參考 |
| §4.3 完整結算範例 A/B/C | 驗收測試用例來源 |
| §5 邊緣案例（§5.1 ~ §5.7） | §7 對策對應來源 |
| §6 依賴關係 | §2.3 / §2.4 依賴表來源 |
| §7 可調參數 | §6.3 嚴禁寫死清單來源 |
| §8 驗收標準 | §1.3 DoD 對應來源 |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| [`【FT-04-DS】reputation-delta-table.md`](../Data-Specs/【FT-04-DS】reputation-delta-table.md) | `ReputationDeltaTable.csv` | `difficulty`, `successDelta`, `failDelta` | 依任務難度查詢基礎聲望變化 |
| [`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md) | `SystemConstants.csv` | `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | 成功路徑死亡率折扣係數 |
| [`【C-01-DS】mission-difficulty-table.md`](../Data-Specs/【C-01-DS】mission-difficulty-table.md) | `MissionDifficultyTable.csv` | `baseReward` | 快照至 `Outcome.baseReward`，供 C-05 `on_success_gold_bonus` 計算 `conditionGoldBonus` |

### 2.3 上游依賴系統

| 系統 | 呼叫／訂閱介面 | 用途 |
| --- | --- | --- |
| **F-01 DataManager** | `IDataManager.GetTable<ReputationDeltaTable>()`<br>`IDataManager.GetTable<SystemConstants>()` | 載入聲望表與系統常數 |
| **FT-02 Mission Dispatch** | 訂閱事件 `OnMissionCompleted(int activeMissionID)`<br>`IMissionDispatchService.GetActiveMission(id)`<br>`IMissionDispatchService.RemoveActiveMission(id)` | 觸發結算；取派遣快照；清理 ActiveMission |
| **C-01 Mission Database** | `IMissionDatabaseService.GetTemplate(missionID)`<br>`IMissionDatabaseService.GetBaseReward(difficulty)` | 取任務 metadata 快照；取 baseReward |
| **C-02 Adventurer Management** | `IAdventurerRoster.GetAdventurer(instanceID)`<br>`IAdventurerRoster.UpdateStatus(id, status)`<br>`IAdventurerRoster.SetWounded(id)` | 讀取 traitIDs；更新最終狀態 |
| **C-05 Trait System** | `ITraitService.ApplyConditionTraits(Outcome, int[] traitIDs)` | 委託執行 condition 特質對 Outcome 的修改 |
| **F-03 Resource Management** | `IResourceService.AddReputation(int delta)` | 套用聲望變化 |

### 2.4 下游被依賴系統

| 系統 | 消費方式 | 使用 Outcome 欄位 |
| --- | --- | --- |
| **FT-05 Guild Gold Flow** | 訂閱 `OnMissionResolved` | `isSuccess`, `missionDifficulty`, `baseReward`, `conditionGoldBonus` |
| **FT-09 Faction Story** | 訂閱 `OnMissionResolved` | `missionFactionID`, `isSuccess`, `missionTypeID` |
| **P-02 Main UI** | 訂閱 `OnMissionResolved` | `adventurerInstanceID`, `finalStatus`, `successRoll`, `deathRoll`, `triggeredConditionTraits` |
| **P-03 Notification System** | 訂閱 `OnMissionResolved` | `finalStatus`, `triggeredConditionTraits`, `missionDifficulty` |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload 結構 | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMissionCompleted` | **訂閱**（來自 FT-02） | `int activeMissionID` | FT-02 計時完成後通知 FT-04 啟動結算管線 |
| `OnMissionResolved` | **發布**（由 FT-04 發出） | `Outcome outcome`（見 §5.3） | 結算管線步驟 11 完成後，通知 FT-05 / FT-09 / P-02 / P-03 |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家在離線或掛機後回到遊戲，面對已發生的事實——派遣時的每一個判斷，在結算視窗裡被誠實地攤開。五種結果（成功+存活、成功+救活後Wounded、成功+死亡、失敗+存活Wounded、失敗+死亡）各有不同的重量感；condition 特質在最後一刻改變命運，構成讓玩家記住的故事片段。

### 3.2 系統目的還原

FT-04 是核心循環「任務結算」的執行者：訂閱 FT-02 的完成觸發，從派遣時快照的成功率／死亡率擲骰判定，套用 condition 特質修正，映射最終冒險者狀態，計算並套用聲望，發布結算事件供金流、陣營、UI、通知四個下游消費。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 結算不是即時的——你承受已發生的事 | 開遊戲才看到結算視窗，不是看動畫時決定 | FT-02 計時完成後 `OnMissionCompleted` 觸發，FT-04 在同幀完成，UI 被動消費 `OnMissionResolved` |
| 成功率在派遣時就鎖定 | 調整派遣不影響已在途任務 | 從 `ActiveMission.finalSuccessRate` / `finalDeathRate` 快照讀取，不重新計算 |
| 5 種不同重量的結算結果 | 視窗顯示的狀態與 condition 觸發訊息不同 | `MapFinalStatus` 3-bool 映射表；`triggeredConditionTraits` 供 P-02 / P-03 選模板 |
| condition 特質在最後改變命運 | 「亡命徒特質觸發，死亡 → 被救活」訊息鏈 | `ApplyConditionTraits` 在擲骰後、`MapFinalStatus` 前修改 `isDead` / `isWounded` |
| 難度愈高聲望回報愈大，失敗懲罰也愈重 | 成功 SSS 大幅增加聲望；失敗 SSS 聲望崩跌 | `ReputationDeltaTable` 依 `difficulty` 查 `successDelta` / `failDelta`；condition 疊加 |
| 死亡有代價，不是直接回收 | 冒險者狀態變 Dead，保留在名冊直到手動除名 | `C02.UpdateStatus(id, Dead)` 保留 roster entry；FT-04 不自動移除 |
| 成功路徑死亡率打折，鼓勵冒險 | 成功後告知「死亡率從 40% 降至 20%」 | `adjustedDeathRate = finalDeathRate × DEATH_RATE_ON_SUCCESS_MULTIPLIER`；快照於 Outcome 供 UI 顯示 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（單一 FSD，規劃 4 個 Script）。

### 4.2 拆分理由

GDD §3.2 明確說明「順序不可調換」，整個結算管線是原子性交易——從接收事件到清理 ActiveMission 的 12 步驟不可分割發布。「拆分 FSD」意即將這個原子流程的規格分散到多份文件，會導致步驟順序約束無法在單一 FSD 中完整表達，違反「不可拆解完整、不可分割的系統」原則（FSD-index §2.4）。

Script 層面仍做職責分離：`OutcomeData.cs`（DTO）+ `IOutcomeResolutionService.cs`（介面）+ `OutcomeResolutionService.cs`（管線 orchestrator）+ `OutcomeReputationCalculator.cs`（聲望計算，抽出避免 Service 臃腫）。預估總行數 ~500 行，在合理範圍內。

### 4.3 拆分結果

不適用（單一 FSD）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `OutcomeData.cs` | `Assets/Scripts/Gameplay/Outcome/OutcomeData.cs` | 定義 `Outcome` DTO、`OutcomeStatus` enum 及相關型別常數 | — | `~80 行` |
| `IOutcomeResolutionService.cs` | `Assets/Scripts/Gameplay/Outcome/IOutcomeResolutionService.cs` | 宣告 FT-04 對外介面（目前為空，保留供未來 debug API 擴充） | — | `~20 行` |
| `OutcomeResolutionService.cs` | `Assets/Scripts/Gameplay/Outcome/OutcomeResolutionService.cs` | 執行結算管線 12 步驟（訂閱事件、擲骰、委託 C-05、更新狀態、發布事件、清理） | `IDataManager`, `IMissionDispatchService`, `IMissionDatabaseService`, `IAdventurerRoster`, `ITraitService`, `IResourceService`, `EventBus` | `~300 行` |
| `OutcomeReputationCalculator.cs` | `Assets/Scripts/Gameplay/Outcome/OutcomeReputationCalculator.cs` | 依 `ReputationDeltaTable` 計算 `baseDelta`，供 `OutcomeResolutionService` 呼叫 | `IDataManager` | `~80 行` |

### 4.5 類別關係（可選）

```
OutcomeResolutionService
  ├─ 使用 OutcomeData（DTO）
  ├─ 使用 OutcomeReputationCalculator（計算 baseDelta）
  ├─ 委託 ITraitService.ApplyConditionTraits
  ├─ 呼叫 IAdventurerRoster（讀取 traitIDs、更新狀態）
  ├─ 呼叫 IMissionDispatchService（取快照、清理）
  ├─ 呼叫 IMissionDatabaseService（取 template、baseReward）
  ├─ 呼叫 IResourceService.AddReputation
  └─ 發布 EventBus → OnMissionResolved(Outcome)

OutcomeReputationCalculator
  └─ 使用 IDataManager.GetTable<ReputationDeltaTable>()
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

FT-04 **無對外公開 API**（GDD §3.9）。所有輸入來自 `OnMissionCompleted` 事件訂閱，所有輸出透過 `OnMissionResolved` 事件與直接呼叫。`IOutcomeResolutionService` 介面目前保持空（無方法），僅作為 Service Locator 註冊佔位與未來 debug API 擴充點。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMissionCompleted` | **訂閱** | `int activeMissionID` | FT-02 發布後啟動結算管線；在 `OnEnable` 訂閱，`OnDisable` 取消訂閱 |
| `OnMissionResolved` | **發布** | `Outcome outcome` | 管線步驟 11，在 `RemoveActiveMission` 之前發布；payload 為同一 `Outcome` 物件（訂閱者若需保留應自行 copy） |

### 5.3 資料結構

#### `OutcomeStatus` enum（定義於 `OutcomeData.cs`）

```
public enum OutcomeStatus
{
    Idle,
    Wounded,
    Dead
}
```

#### `Outcome` DTO（定義於 `OutcomeData.cs`）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `activeMissionID` | `int` | 對應 FT-02 ActiveMission，結算後供清理 |
| `missionID` | `int` | FK → C-01 MissionTemplate |
| `adventurerInstanceID` | `int` | FK → C-02 AdventurerInstance |
| `missionDifficulty` | `string` | 快照自 MissionTemplate（F ~ SSS） |
| `missionTypeID` | `int` | 快照自 MissionTemplate，供下游分類 |
| `missionFactionID` | `int` | 快照自 MissionTemplate，供 FT-09 Faction Story |
| `baseReward` | `int` | 快照自 `C01.GetBaseReward(difficulty)`，供 condition gold bonus 計算 |
| `successRoll` | `float` | `[0, 1)` 擲骰值，供 UI / debug 顯示 |
| `deathRoll` | `float` | `[0, 1)` 擲骰值，供 UI / debug 顯示 |
| `adjustedDeathRate` | `float` | 實際用於 `deathRoll` 比較的死亡率（成功時已套用折扣係數） |
| `isSuccess` | `bool` | 由 `successRoll < finalSuccessRate` 判定 |
| `isDead` | `bool` | 由 `deathRoll < adjustedDeathRate` 判定；condition 特質可改為 `false` |
| `isWounded` | `bool` | 失敗+存活時預寫 `true`；condition 救活後設 `true` |
| `finalStatus` | `OutcomeStatus` | `MapFinalStatus` 從上述三布林值映射 |
| `reputationDelta` | `int` | 基礎（ReputationDeltaTable）+ `on_*_reputation*` condition 疊加 |
| `conditionGoldBonus` | `int` | `on_success_gold_bonus` 觸發產生的 bonus；**FT-05 消費**，FT-04 不呼叫 `AddGold` |
| `triggeredConditionTraits` | `int[]` | 機率判定通過的 condition traitID 清單；初始化為 `new int[0]`，由 C-05 追加 |

#### `ReputationDeltaEntry` 查詢結構（僅在 `OutcomeReputationCalculator` 內部使用）

```
struct ReputationDeltaEntry { int successDelta; int failDelta; }
```

F-01 `GetTable<ReputationDeltaTable>()` 回傳 `Dictionary<string, ReputationDeltaEntry>`，PK 為 `difficulty` 字串。

### 5.4 內部資料流

#### 觸發點：EventBus → OnMissionCompleted(activeMissionID)

```
EventBus → OutcomeResolutionService.OnMissionCompleted(activeMissionID)
    ├─ 步驟 1：activeMission = IMissionDispatchService.GetActiveMission(activeMissionID)
    │           if activeMission == null → Debug.LogError, return
    ├─ 步驟 2：adventurer = IAdventurerRoster.GetAdventurer(activeMission.adventurerInstanceID)
    │           if adventurer == null → Debug.LogError, return
    │           if adventurer.status != Dispatched → Debug.LogError, return
    ├─ 步驟 3：template = IMissionDatabaseService.GetTemplate(activeMission.missionID)
    │           if template == null → Debug.LogError, return
    ├─ 步驟 4：outcome = BuildOutcomeSnapshot(activeMission, adventurer, template)
    │           // 填入 activeMissionID, missionID, adventurerInstanceID
    │           // 填入 missionDifficulty, missionTypeID, missionFactionID
    │           // baseReward = IMissionDatabaseService.GetBaseReward(template.difficulty)
    │           // triggeredConditionTraits = new int[0]
    ├─ 步驟 5：RollSuccessAndDeath(outcome, activeMission)
    │           outcome.successRoll = Random.Range(0f, 1f)
    │           outcome.isSuccess   = outcome.successRoll < activeMission.finalSuccessRate
    │           if outcome.isSuccess:
    │               outcome.adjustedDeathRate = activeMission.finalDeathRate × _deathRateMultiplier
    │           else:
    │               outcome.adjustedDeathRate = activeMission.finalDeathRate
    │           outcome.deathRoll = Random.Range(0f, 1f)
    │           outcome.isDead    = outcome.deathRoll < outcome.adjustedDeathRate
    │           outcome.isWounded = (!outcome.isSuccess && !outcome.isDead)
    ├─ 步驟 6：ApplyBaseReputationDelta(outcome)
    │           // OutcomeReputationCalculator.GetBaseDelta(difficulty, isSuccess)
    │           //   → 查 ReputationDeltaTable；缺行回傳 0 並 LogError
    │           outcome.reputationDelta = baseDelta
    ├─ 步驟 7：ITraitService.ApplyConditionTraits(outcome, adventurer.traitIDs)
    │           // C-05 § 4.4 規格；修改 outcome.isDead / isWounded / reputationDelta / conditionGoldBonus
    │           // 觸發的 traitID 追加至 outcome.triggeredConditionTraits
    ├─ 步驟 8：outcome.finalStatus = MapFinalStatus(outcome)
    │           if outcome.isDead    → Dead
    │           if outcome.isWounded → Wounded
    │           else                 → Idle
    ├─ 步驟 9：ApplyAdventurerStatus(outcome)
    │           switch outcome.finalStatus:
    │               Idle:    IAdventurerRoster.UpdateStatus(adventurerInstanceID, Idle)
    │               Wounded: IAdventurerRoster.SetWounded(adventurerInstanceID)
    │               Dead:    IAdventurerRoster.UpdateStatus(adventurerInstanceID, Dead)
    ├─ 步驟 10：IResourceService.AddReputation(outcome.reputationDelta)
    ├─ 步驟 11：EventBus.Publish(new OnMissionResolvedEvent(outcome))
    └─ 步驟 12：IMissionDispatchService.RemoveActiveMission(outcome.activeMissionID)
```

#### `_deathRateMultiplier` 初始化（Awake / 注入時載入）

```
OutcomeResolutionService.Awake()
    → _deathRateMultiplier = IDataManager.GetTable<SystemConstants>()["DEATH_RATE_ON_SUCCESS_MULTIPLIER"]
      if key 不存在 → Debug.LogError, _deathRateMultiplier = 0.5f (fallback)
      if value 超出 [0, 1] → Debug.LogWarning, clamp 至 [0, 1]
```

#### `OutcomeReputationCalculator.GetBaseDelta(difficulty, isSuccess)`

```
GetBaseDelta(difficulty, isSuccess):
    entry = _reputationTable.TryGetValue(difficulty)
    if not found → Debug.LogError, return 0
    if entry.successDelta < 0 或 entry.failDelta > 0 → Debug.LogWarning（符號異常）
    return isSuccess ? entry.successDelta : entry.failDelta
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `ReputationDeltaTable` | `difficulty` (PK), `successDelta`, `failDelta` | [`【FT-04-DS】reputation-delta-table.md`](../Data-Specs/【FT-04-DS】reputation-delta-table.md) | 依難度查詢成功/失敗聲望基礎變化；由 `OutcomeReputationCalculator` 快取 | F-01 DataManager 啟動時載入；FT-04 在 `Awake` 取 reference |
| `SystemConstants` | `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | [`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md) | 成功路徑死亡率折扣係數；`OutcomeResolutionService._deathRateMultiplier` | F-01 DataManager 啟動時載入；FT-04 在 `Awake` 讀取 |
| `MissionDifficultyTable` | `difficulty`, `baseReward` | [`【C-01-DS】mission-difficulty-table.md`](../Data-Specs/【C-01-DS】mission-difficulty-table.md) | `BuildOutcomeSnapshot` 時取 `baseReward` 快照至 Outcome；owner = C-01 | F-01 DataManager 啟動時載入；透過 `IMissionDatabaseService.GetBaseReward` 存取 |

### 6.2 引用的 ScriptableObject

無（FT-04 全資料驅動由 CSV 提供）。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `DEATH_RATE_ON_SUCCESS_MULTIPLIER`（成功時死亡率折扣） | `SystemConstants.csv` 的 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` 欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `successDelta`（各難度成功聲望變化） | `ReputationDeltaTable.csv` 的 `successDelta` 欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `failDelta`（各難度失敗聲望變化） | `ReputationDeltaTable.csv` 的 `failDelta` 欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `baseReward`（任務基礎報酬） | `MissionDifficultyTable.csv` 的 `baseReward` 欄位 | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| **§5.1** `ReputationDeltaTable` 缺少某 difficulty 行 | `OutcomeReputationCalculator.GetBaseDelta` 查 Dictionary 失敗 → `Debug.LogError`，回傳 `(0, 0)`（聲望不變動），流程繼續 | `OutcomeReputationCalculator` | EditMode test：移除某 difficulty → 驗證 `reputationDelta == 0` + Console LogError |
| **§5.1** `successDelta` 為負值或 `failDelta` 為正值 | `Debug.LogWarning`（符號異常），但依 CSV 值執行，不修正 | `OutcomeReputationCalculator` | EditMode test：設 `successDelta = -1` → 驗證 Console LogWarning，值仍套用 |
| **§5.1** `SystemConstants` 缺少 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` | `Debug.LogError`，fallback `_deathRateMultiplier = 0.5f` | `OutcomeResolutionService` | EditMode test：空 SystemConstants → 驗證 fallback 值 `0.5` |
| **§5.1** `DEATH_RATE_ON_SUCCESS_MULTIPLIER` 超出 `[0, 1]` | `Debug.LogWarning`，`Awake` 載入時 clamp 至 `[0, 1]` | `OutcomeResolutionService` | EditMode test：設值 `1.5` → 驗證 `_deathRateMultiplier == 1.0` |
| **§5.2** `activeMissionID` 查無 ActiveMission | `Debug.LogError`，`return`，管線中止，ActiveMission 不清理 | `OutcomeResolutionService` | EditMode test：傳不存在 ID → 驗證 `OnMissionResolved` 未發布 |
| **§5.2** 冒險者狀態非 `Dispatched` | `Debug.LogError`，`return`，防禦性檢查 | `OutcomeResolutionService` | EditMode test：mock `status = Idle` → 驗證管線中止 |
| **§5.2** 同一 `activeMissionID` 重複發布 `OnMissionCompleted` | 第二次步驟 1 `GetActiveMission` 回傳 null（已清理）→ `LogError` 後安全退出 | `OutcomeResolutionService` | EditMode test：連續兩次發布 → 驗證 `OnMissionResolved` 只觸發一次 |
| **§5.2** `finalSuccessRate` / `finalDeathRate` 超出 `[0, 1]` | FT-02 派遣時已 clamp；若仍超出，於步驟 5 `RollSuccessAndDeath` 入口 clamp 至 `[0, 1]` 並 `LogWarning` | `OutcomeResolutionService` | EditMode test：mock 超界值 → 驗證 clamp 後正確擲骰 |
| **§5.3** `finalSuccessRate = 1.0` | `successRoll ∈ [0, 1)` 必 `< 1.0` → 100% 成功 | `OutcomeResolutionService` | EditMode test：1000 次全部 `isSuccess == true` |
| **§5.3** `finalSuccessRate = 0.0` | `successRoll ≥ 0` 不可能 `< 0.0` → 100% 失敗 | `OutcomeResolutionService` | EditMode test：1000 次全部 `isSuccess == false` |
| **§5.3** `finalDeathRate = 0.0` | `isDead` 必為 `false` | `OutcomeResolutionService` | EditMode test：驗證 `isDead == false` |
| **§5.4** 冒險者無任何 condition 特質 | `ApplyConditionTraits` 以空陣列呼叫，C-05 直接回傳；`conditionGoldBonus == 0`、`triggeredConditionTraits.Length == 0`、`reputationDelta == baseDelta` | `OutcomeResolutionService` | EditMode test：mock 空 traitIDs → 驗證三欄位 |
| **§5.4** 同時持有 `on_death_survive` 與 `on_fail_survive` | C-05 § 4.4 依 `traitIDs` 順序執行；先觸發者改 `isDead = false`，後者因 `isDead == false` 自然跳過（C-05 規格保證） | `ITraitService` | EditMode test：mock C-05 驗證只有第一個追加至 `triggeredConditionTraits` |
| **§5.4** condition 特質 `effectTarget` 為未知值 | C-05 `ApplyConditionTraits` 輸出 `LogWarning` 並跳過，FT-04 不額外處理 | `ITraitService` | 由 C-05 測試覆蓋 |
| **§5.4** condition 執行中拋出例外 | FT-04 不 try-catch，例外向上傳播，結算中止（步驟 8-12 不執行）；屬嚴重錯誤需 root cause 修復 | `OutcomeResolutionService` | 手動 Console 觀察 |
| **§5.5** `missionDifficulty` 不在 ReputationDeltaTable | `OutcomeReputationCalculator.GetBaseDelta` 回傳 0 + `LogError`，流程繼續（聲望不變動） | `OutcomeReputationCalculator` | 同 §5.1 第一列 |
| **§5.5** `F03.AddReputation` 觸發破產警告 | F-03 自行處理；FT-04 不感知此狀態變化 | `IResourceService` | F-03 FSD 覆蓋 |
| **§5.6** `OnMissionResolved` 訂閱者拋出例外 | FT-04 不 try-catch；依賴 EventBus 隔離設計（單一訂閱者錯誤不影響其他） | `EventBus` | EventBus 單元測試覆蓋 |
| **§5.6** `RemoveActiveMission` 傳入 ID 已不存在 | FT-02 內部 `LogWarning` 無操作；FT-04 不重複檢查 | `IMissionDispatchService` | FT-02 FSD 覆蓋 |
| **§5.7** 離線期間多個 ActiveMission 到期 | FT-02 `TickCompletionCheck` 依序發布多個 `OnMissionCompleted`；FT-04 每筆獨立結算，擲骰、condition、事件各自獨立 | `OutcomeResolutionService` | EditMode test：mock 3 筆 → 驗證 3 次 `OnMissionResolved` |
| **§5.7** 結算中途玩家強制關閉（pipeline 執行到一半） | 重啟後 FT-02 仍持有 ActiveMission，`TickCompletionCheck` 重發 `OnMissionCompleted`，FT-04 重新擲骰；冒險者若已非 `Dispatched` 則步驟 2 擋掉，ActiveMission 殘留（需存檔層補救）；已知 Save Scum 風險，Game Jam 不防禦 | `OutcomeResolutionService` | 知悉，無自動化測試 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 Outcome 資料結構（15 欄位） | §5.3 `Outcome` DTO | 對齊 | 欄位一一對應，型別與說明完整 |
| §3.2 結算流程（管線步驟 1-12） | §5.4 內部資料流 | 對齊 | 12 步驟完整映射，順序約束與關鍵原則全部保留 |
| §3.2 跨系統時序註記（Bankruptcy × Reputation） | §5.4 步驟 10 說明 | 對齊 | 已在資料流備註「AddReputation 可能觸發 F-03 破產狀態轉移，Game Over 由 FT-06 控制」 |
| §3.3 兩次獨立擲骰 | §5.4 步驟 5 | 對齊 | `successRoll` / `deathRoll` 獨立亂數；成功折扣公式對齊 |
| §3.4 condition 特質套用（5 種 effectTarget） | §2.3 上游依賴、§5.4 步驟 7 | 對齊 | 委託 `ITraitService.ApplyConditionTraits`；`conditionGoldBonus` 命名對齊 C-05 § 4.4 |
| §3.5 5 種最終結果映射（MapFinalStatus） | §5.4 步驟 8 | 對齊 | 3-bool 映射表與 `MapFinalStatus` 邏輯完整對應 |
| §3.6 聲望計算（查表 + condition 疊加） | §5.4 步驟 6 / `OutcomeReputationCalculator` | 對齊 | `ApplyBaseReputationDelta` 先於 condition，條件 `+=` 疊加 |
| §3.7 冒險者狀態更新（UpdateStatus / SetWounded） | §5.4 步驟 9 | 對齊 | switch 三分支對應 Idle / Wounded / Dead；呼叫簽名與 C-02 § 3.5 對齊 |
| §3.8 事件發布與 ActiveMission 清理 | §5.4 步驟 11-12 | 對齊 | Publish 先於 RemoveActiveMission；訂閱者表格對應 GDD §3.8 |
| §3.9 查詢 API（無對外 API） | §5.1 | 對齊 | `IOutcomeResolutionService` 介面保持空，符合無對外 API 設計 |

### 8.2 公式對齊或替代說明

GDD §4.1（擲骰公式）與 §4.2（聲望 delta 公式）採**完整對應實作**，無替代。

- §4.1：`successRoll < finalSuccessRate` 判定與 `adjustedDeathRate = finalDeathRate × multiplier`，對應 §5.4 步驟 5
- §4.2：`baseDelta + conditionDelta` 累加模式，對應 §5.4 步驟 6-7
- §4.3 範例 A/B/C：作為 DoD 測試用例來源（§1.3）

### 8.3 未能實現的規則與修改建議

無。GDD §3 所有規則皆可直接映射至 Script 設計。

建議項（不阻礙實作）：
- **B-01**：`OnMissionResolved` 目前傳遞同一 `Outcome` 物件引用，訂閱者若修改欄位可能影響後續訂閱者（GDD §5.6 已知風險）。Game Jam 範疇內以「訂閱者自律不修改」為約定；若未來發現問題可在 `OutcomeResolutionService` 改為傳遞 shallow copy。
- **B-02**：`IOutcomeResolutionService` 目前為空介面，佔位意義大於功能。若確認 Game Jam 不需要 debug API，可考慮直接去除介面層（但保留不造成任何問題，建議維持以備日後擴充）。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-27 | `【FT-04】outcome-resolution.md` | §3.4 | FSD 回註：`on_success_gold_bonus` 的 `conditionGoldBonus` 欄位命名已在 FSD §5.3 與 C-05 § 4.4 對齊確認；FT-04 不呼叫 `AddGold`，金流全由 FT-05 從 `OnMissionResolved.conditionGoldBonus` 消費 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-27 | 時間單位（「分鐘」）：FT-04 GDD 全文未使用「分鐘」單位，僅 `WOUNDED_RECOVERY_HOURS` 用「小時」，與全域規範相符；無衝突 | FT-04 GDD | 非衝突，沿用 |
| 2026-04-27 | `baseReward` 來源：GDD §3.1 標示「快照自 `C01.GetBaseReward(difficulty)`」，但 `MissionDifficultyTable` 的 owner 為 C-01；FT-04 透過 `IMissionDatabaseService.GetBaseReward` 存取，不直接讀 CSV，符合依賴層級 | FT-04 GDD / C-01 GDD | 非衝突，沿用介面存取 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥（對應 GDD 版本、Data-Specs 三份、撰寫者、狀態、日期）
- [x] §1.3 完成目標可被測試驗證（EditMode tests + UnityMCP console 驗證，每條有明確驗證方式）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節引用表、Data-Specs 引用表、上游 6 系統、下游 4 系統、事件契約 2 條）
- [x] §3.3 對映表覆蓋所有幻想／目的（7 列，對應 GDD §2 五種結果 + condition 特質 + 聲望設計）
- [x] §4 Script 清單欄位齊全（4 個 Script，路徑從 Assets/ 起算，SRP / 依賴介面 / 預估規模均填）
- [x] §5 API/事件/資料結構/資料流齊備（無對外 API；2 個事件含 payload；Outcome DTO 15 欄位；步驟 1-12 偽碼 + 初始化流程 + ReputationCalculator 流程）
- [x] §6 CSV 引用含對應 Data-Specs（3 表，對應 Data-Specs 連結；§6.3 嚴禁寫死清單 4 項，對齊原則第 9 條）
- [x] §7 邊緣案例皆有對策（對應 GDD §5.1 ~ §5.7 全部 17 條案例，每條含涉及 Script 與驗證方式）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（§3.1 ~ §3.9 共 10 條，全部「對齊」）
- [x] §8.2~§8.5 如實登記（公式完整對應；無未實現規則；GDD 回註 1 筆；衝突 2 筆均非衝突）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（見後續 Edit 操作）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過 | 正向 FSD（無既有 Outcome Script）；FSD 未拆分（4 Script）；無真實衝突；建議項 B-01（Outcome 物件引用修改風險）/ B-02（空介面層取捨）不阻礙實作；待主體複核後轉「已完成」 |
