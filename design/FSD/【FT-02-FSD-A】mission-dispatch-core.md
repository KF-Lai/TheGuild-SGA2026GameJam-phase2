# 【FT-02-FSD-A】功能規格說明書 — Mission Dispatch Core（成功率計算 + 派遣 + 計時）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-02】mission-dispatch.md`（版本：2026-04-22） |
| 對應 Data-Specs | `【FT-02-DS】success-rate-table.md`（_待建_；CSV: `SuccessRateTable.csv`，owner = FT-02）<br>`【C-01-DS】mission-difficulty-table.md`（消費端引用；`baseDeathRate` 欄位）<br>`【F-01-DS】system-constants.md`（消費端引用；`STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY`、`ESCORT_TYPE_ID`） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-02-FSD-A 涵蓋「成功率/死亡率計算」、「派遣動作執行」、「任務完成計時與事件發布」三項核心職責。本子單元持有 `_activeMissions` 執行期狀態，是 FT-02 系統的主要狀態擁有者。CommissionBoard 池管理邏輯由 FT-02-FSD-B 負責。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- `CalculateRates(instanceID, missionID) : (float, float)` — rankDiff 查表 + 三層修正 + clamp
- `Dispatch(instanceID, missionID, DispatchSource) : bool` — 10 步驟派遣序列（GDD §3.5）
- `TickCompletionCheck()` — 每 OnSecondTick 掃描到期 ActiveMission，發布 `OnMissionCompleted`
- `RemoveActiveMission(activeMissionID)` — FT-04 結算後清理
- `GetActiveMission*` 查詢 API
- 離線任務完成批次處理（`OnOfflineResolved` 訂閱，發布 `OnOfflineMissionsResolved`）
- `ISaveable` 持久化（`activeMissions`、`_nextActiveMissionID`）
- `ActiveMission` 資料結構與 `DispatchSource` enum

**Out-of-Scope**
- CommissionBoard 池管理（→ FT-02-FSD-B）
- willingness 計算（→ FT-03）
- 結算骰與後果（→ FT-04）
- 金流（→ FT-05）
- behavior / condition 特質套用（→ FT-03 / FT-04）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準：

| AC | 可驗證條件 |
| --- | --- |
| AC-MD2-01 | EditMode test：C 階戰士 vs B 難度討伐（**無 trait**），`CalculateRates` 回傳 `(0.55, 0.18)` |
| AC-MD2-01b | EditMode test：C 階戰士 vs B 難度討伐 + 強運 trait（successDelta=+0.08），`CalculateRates` 回傳 `(0.63, 0.18)`（FSD-Codex-Reoprts-260427 T1-FT02 補：對齊 GDD §4.2 完整三層疊加範例） |
| AC-MD2-02 | EditMode test：S 階 vs D 難度，`rankDiff = 3`，baseSuccess = 0.95 |
| AC-MD2-03 | EditMode test：F 階 vs A 難度，`rankDiff = -3`，baseSuccess = 0.10 |
| AC-MD2-04~10 | EditMode test：職業/種族/特質三層修正各獨立驗證（含正負向） |
| AC-MD2-11 | EditMode test：修正疊加後 clamp 至 `[0, 1]`，不出現負值或超過 1.0 |
| AC-MD2-12 | PlayMode 或 EditMode test：`Dispatch` 成功後冒險者狀態 = Dispatched，`currentMissionID` 正確 |
| AC-MD2-13 | EditMode test：非 Idle 冒險者呼叫 `Dispatch` 回傳 false，狀態不變 |
| AC-MD2-14 | EditMode test：進行中任務達上限時 `Dispatch` 回傳 false |
| AC-MD2-15 | EditMode test：Dispatch 成功後 `GetActiveMissions()` 含新 ActiveMission，rate 與 CalculateRates 一致 |
| AC-MD2-16 | EditMode test：`completionTimestamp = dispatchTimestamp + duration × 60`；護送用 EscortDuration |
| AC-MD2-17 | EditMode test：`TickCompletionCheck` 在到期後發布 `OnMissionCompleted`，帶正確 `activeMissionID` |
| AC-MD2-18 | EditMode test：離線多任務到期，重啟後首 Tick 觸發全部 `OnMissionCompleted` |
| AC-MD2-19 | EditMode test：`RemoveActiveMission` 後 `GetActiveMissions()` 不含該任務，Count 減 1 |
| AC-MD2-20 | EditMode test：`GetActiveMissionByAdventurer` 回傳正確任務或 null |
| AC-MD2-21~23 | EditMode test：`OnCommissionAccepted` 僅發布一次，且在 C-02 UpdateStatus 之前；payload 與 GetBaseReward 一致 |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要（核心職責定義、不負責項目）
- §3.1 SuccessRateTable 資料表定義
- §3.2 基礎死亡率來源（消費 C-01 MissionDifficultyTable）
- §3.3 難度索引
- §3.4 成功率計算流程（修正疊加順序）
- §3.5 派遣動作（10 步驟）
- §3.6 ActiveMission 資料結構
- §3.6a Enum 宣告（DispatchSource、CommissionSource）
- §3.7 任務完成檢查
- §3.8 查詢 API（CalculateRates、Dispatch、GetActiveMissions、RemoveActiveMission 等）
- §3.10 事件契約集中表
- §4.1 rankDiff 計算公式
- §4.2 完整成功率/死亡率計算（含範例）
- §4.3 派遣時長計算
- §4.4 完成時間計算
- §5.1~5.4 邊緣案例（資料載入、CalculateRates、Dispatch、任務完成檢查、存檔相關）
- §6.1 上游依賴（F-01、F-02、C-01~C-05、FT-07、C-06）
- §6.4 ISaveable 持久化契約
- §7.1~7.3 可調參數（SuccessRateTable、baseDeathRate、SystemConstants）
- §8 驗收標準（AC-MD2-01 ~ AC-MD2-23）

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-02-DS】success-rate-table.md`（_待建_） | `SuccessRateTable.csv` | `rankDiff`、`successRate` | 根據 rankDiff 查詢基礎成功率 |
| `【C-01-DS】mission-difficulty-table.md` | `MissionDifficultyTable.csv` | `baseDeathRate` | 取任務基礎死亡率（透過 C-01 API） |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY`、`ESCORT_TYPE_ID` | 職業修正幅度與護送類型判斷 |

### 2.3 上游依賴系統

> **介面命名規範**：本表中以 `IXxxService` / `IXxxSystem` 命名的介面僅為敘述方便（沿用 GDD 用語），實作契約以既有 concrete singleton 為準（`DataManager.Instance` / `TimeSystem.Instance` / `ResourceManagement.Instance` / static `EventBus`）。詳見 `FSD-index.md` §2.10。

| 系統 | 依賴內容 | 呼叫介面 |
| --- | --- | --- |
| F-01 DataManager | 載入 `SuccessRateTable.csv` | `IDataManager.GetAll<SuccessRateRow>()` |
| F-02 Time System | 取當前 Unix 秒；訂閱 `OnSecondTick`；訂閱 `OnOfflineResolved` | `ITimeSystem.NowUTC`、`OnSecondTick`、`OnOfflineResolved` |
| C-01 Mission Database | 查詢任務模板、基礎死亡率、時長、基礎獎勵 | `IC01MissionDatabase.GetTemplate()`、`GetBaseDeathRate()`、`GetBaseDuration()`、`GetEscortDuration()`、`GetBaseReward()` |
| C-02 Adventurer Management | 查詢冒險者（rank, professionID, raceID, traitIDs, status）；更新狀態 | `IC02AdventurerRoster.GetAdventurer()`、`UpdateStatus()` |
| C-03 Profession System | 查詢擅長/弱點類型 | `IC03ProfessionService.IsStrongType()`、`IsWeakType()` |
| C-04 Race System | 查詢種族成功率/死亡率修正值 | `IC04RaceService.GetSuccessDelta()`、`GetDeathDelta()` |
| C-05 Trait System | 查詢 stat 類特質 | `IC05TraitService.GetTrait()` |
| FT-07 Guild Building | 查詢最大同時任務數 | `IFT07GuildBuilding.GetMaxConcurrentMissions()` |
| C-06 World Danger | 派遣後通知危險度計數 | `IC06WorldDanger.OnMissionAccepted(difficulty)` |

### 2.4 下游被依賴系統

| 系統 | 依賴本子單元的內容 |
| --- | --- |
| FT-03 NPC Decision | 呼叫 `CalculateRates`；呼叫 `Dispatch(..., NpcAutoPick)` |
| FT-04 Outcome Resolution | 訂閱 `OnMissionCompleted`；呼叫 `GetActiveMission`、`RemoveActiveMission` |
| FT-05 Guild Gold Flow | 訂閱 `OnCommissionAccepted` |
| P-02 Main UI | 呼叫 `CalculateRates`（預覽）、`Dispatch`、`GetActiveMissions*` |
| P-02 離線摘要 UI | 訂閱 `OnOfflineMissionsResolved` |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnSecondTick` | 訂閱（來自 F-02） | `long nowUTC` | 每秒驅動 `TickCompletionCheck()` |
| `OnOfflineResolved` | 訂閱（來自 F-02） | `long offlineSeconds` | 批次計算並發布離線完成摘要 |
| `OnCommissionAccepted` | 發布 | `(int missionID, int baseReward, DispatchSource source)` | `Dispatch` step 6，先於 C-02 UpdateStatus |
| `OnAdventurerDispatched` | 發布 | `(int instanceID, int activeMissionID)` | `Dispatch` step 8 |
| `OnMissionCompleted` | 發布 | `(int activeMissionID)` | `TickCompletionCheck` 首次偵測到期，每筆任務僅發 1 次 |
| `OnMissionCancelled` | 發布 | `(int activeMissionID)` | `RestoreFromSave` 驗證失敗（missionID / adventurerInstanceID 無效） |
| `OnOfflineMissionsResolved` | 發布 | `(long offlineSeconds, int completedCount, int[] completedActiveMissionIDs)` | 訂閱 `OnOfflineResolved` 後計算完成，通知離線摘要 UI |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家盯著委託板，選好任務與冒險者，按下「推薦」的那一刻是公會長最重壓的決策——成功率、死亡率攤在眼前，背後是人命與資源的賭注。系統不替玩家做決定，只把計算好的數字呈現出來，讓每個百分點都有重量。

### 3.2 系統目的還原

FT-02 是「決策支援 + 狀態推進器」：計算最終成功率/死亡率提供決策依據；派遣成功後轉移冒險者狀態、鎖定任務、觸發金流預收；以 OnSecondTick 計時，到期後發布 `OnMissionCompleted` 驅動 FT-04 結算。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 成功率有重量，每個百分點都有意義 | UI 顯示精確百分比，職業/種族/特質各別修正可讀 | `MissionRateCalculator.CalculateRates`：rankDiff 查表 + 三層加法修正 + 單次 clamp |
| 選對職業的冒險者明顯更好 | 擅長 +20%、弱點 -15%，數字變化玩家直覺感受到 | C-03 `IsStrongType`/`IsWeakType` + `SystemConstants.STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY` |
| 派遣後冒險者「真的走了」 | 名冊狀態轉 Dispatched，委託板移除該任務 | `Dispatch` 10 步序列；step 7 呼叫 C-02 UpdateStatus；step 10 從委託板池移除 |
| 任務「真的在倒數」 | 進行中任務顯示倒數計時 | `completionTimestamp = dispatchTimestamp + duration × 60`；`TickCompletionCheck` 每秒掃描 |
| 打開遊戲，之前派出去的任務已完成 | 登入後看到離線結算摘要 | 訂閱 F-02 `OnOfflineResolved`；掃描 `activeMissions` 計算完成數；發布 `OnOfflineMissionsResolved` |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

是（FT-02 整體系統拆為 FSD-A / FSD-B）。本 FSD-A 內部不再進一步拆分子 FSD；但對應 3 個 Script。

### 4.2 拆分理由

FT-02 GDD §3.4（成功率計算）、§3.5（派遣序列 + 狀態管理）、§3.9（CommissionBoard 池管理）三大職責彼此獨立，預估合計超過 700 行。FSD-A 專注計算核心 + 狀態機，FSD-B 專注委託板池（見 `【FT-02-FSD-B】commission-board.md`）。符合 §2.4 拆分標準（職責分區明顯、單 Script > 500 行門檻）。

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| FT-02-A | Mission Dispatch Core | 成功率計算 + 派遣序列 + 計時 + ISaveable | §3.1~§3.8、§3.10、§4、§5、§6.4、§8 |
| FT-02-B | Commission Board | 池管理 + 委託生成 + InjectStaticMission | §3.9、§3.9.1~§3.9.6 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `ActiveMission` | `Assets/Scripts/Gameplay/Mission/ActiveMission.cs` | 持有單一進行中任務的所有快照欄位（純資料類別，無行為） | — | `< 50 行` |
| `MissionRateCalculator` | `Assets/Scripts/Gameplay/Mission/MissionRateCalculator.cs` | 輸入冒險者 + 任務 → 輸出 `(finalSuccess, finalDeath)` | `IC01MissionDatabase`、`IC02AdventurerRoster`、`IC03ProfessionService`、`IC04RaceService`、`IC05TraitService`、`IDataManager` | `150~200 行` |
| `MissionDispatchService` | `Assets/Scripts/Gameplay/Mission/MissionDispatchService.cs` | 持有 `_activeMissions` 狀態、執行 Dispatch 10 步序列、TickCompletionCheck、ISaveable | `MissionRateCalculator`、`IC01MissionDatabase`、`IC02AdventurerRoster`、`IFT07GuildBuilding`、`IC06WorldDanger`、`ITimeSystem`、`EventBus`、`ICommissionBoardService` | `350~420 行` |
| `MissionDispatchEvents` | `Assets/Scripts/Gameplay/Mission/Events/MissionDispatchEvents.cs` | 集中宣告 typed event struct：`OnCommissionAcceptedEvent { int missionID; int baseReward; DispatchSource source; }`、`OnAdventurerDispatchedEvent { int instanceID; int activeMissionID; }`、`OnMissionCompletedEvent { int activeMissionID; }`、`OnMissionCancelledEvent { int activeMissionID; }`、`OnOfflineMissionsResolvedEvent { long offlineSeconds; int completedCount; int[] completedActiveMissionIDs; }` | — | `< 50 行` |
| `MissionTimer`（既有，**裁決移除**） | 現位於 `Assets/Scripts/Core/Time/MissionTimer.cs`；CT-03 已裁決：FT-02-A `MissionDispatchService.TickCompletionCheck` 為唯一任務計時 owner，本檔案連同 `TimeSystem.cs` 內 `_missionTimers` / `RegisterMission` / `UnregisterMission` / `CheckMissionTimers` / `OnMissionExpiredEvent` 路徑全數移除 | （已裁決刪除；§8.3 D-01 詳） | — | — |

### 4.5 類別關係

```
MissionDispatchService
    ├─ 持有 List<ActiveMission>              （狀態資料）
    ├─ 呼叫 MissionRateCalculator            （計算子）
    └─ 實作 IMissionDispatchService          （對外介面）
           ├─ CalculateRates(int, int)
           ├─ Dispatch(int, int, DispatchSource)
           ├─ GetActiveMission*(...)
           └─ RemoveActiveMission(int)

MissionRateCalculator
    └─ 實作 IMissionRateCalculator（或直接注入 MissionDispatchService）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

**MissionRateCalculator**

```
CalculateRates(int instanceID, int missionID) : (float success, float death)
    // UI 預覽或派遣前查詢；純計算，不變更任何狀態
```

**MissionDispatchService**

```
Dispatch(int instanceID, int missionID, DispatchSource source) : bool
    // 執行 10 步派遣序列；冒險者必須 Idle，否則回傳 false

GetActiveMissions() : IReadOnlyList<ActiveMission>
GetActiveMission(int activeMissionID) : ActiveMission         // 找不到回 null
GetActiveMissionByAdventurer(int instanceID) : ActiveMission  // 找不到回 null
GetActiveMissionCount() : int
RemoveActiveMission(int activeMissionID) : void               // FT-04 結算後呼叫
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnSecondTick` | 訂閱（F-02） | `long nowUTC` | `OnEnable` 訂閱；驅動 `TickCompletionCheck()` |
| `OnOfflineResolved` | 訂閱（F-02） | `long offlineSeconds` | `OnEnable` 訂閱；批次發布離線完成摘要 |
| `OnCommissionAccepted` | 發布 | `(int missionID, int baseReward, DispatchSource source)` | Dispatch step 6 |
| `OnAdventurerDispatched` | 發布 | `(int instanceID, int activeMissionID)` | Dispatch step 8 |
| `OnMissionCompleted` | 發布 | `(int activeMissionID)` | TickCompletionCheck 首次偵測到期 |
| `OnMissionCancelled` | 發布 | `(int activeMissionID)` | RestoreFromSave 驗證失敗 |
| `OnOfflineMissionsResolved` | 發布 | `(long offlineSeconds, int completedCount, int[] completedActiveMissionIDs)` | OnOfflineResolved 處理完成後 |

### 5.3 資料結構

**ActiveMission（純資料類別）**

```csharp
public class ActiveMission
{
    public int  activeMissionID;        // Runtime 唯一 ID（FT-02 自增）
    public int  missionID;              // FK → MissionTemplate（C-01）
    public int  adventurerInstanceID;   // FK → AdventurerInstance（C-02）
    public float finalSuccessRate;      // 派遣時快照，不再變動
    public float finalDeathRate;        // 派遣時快照，不再變動
    public long  dispatchTimestamp;     // Unix 秒
    public long  completionTimestamp;   // Unix 秒（dispatchTimestamp + duration × 60）
}
```

**DispatchSource enum（GDD §3.6a）**

```csharp
public enum DispatchSource
{
    PlayerManual,    // 玩家於 P-02 手動派遣
    NpcAutoPick,     // FT-03 自主接單
    OfflineAutoPick  // FT-11 預留（Jam 階段未實作）
}
```

**SuccessRateRow（資料載入 DTO）**

```csharp
public class SuccessRateRow
{
    public int   rankDiff;      // PK：-3 ~ +3
    public float successRate;   // [0, 1]；載入時 clamp
}
```

**ISaveable 序列化 payload（A/B 共用 OwnerKey，FSD-Codex-Reoprts-260427 T1-FT02 明確化）**

```
OwnerKey = "ft02Dispatch"   （唯一 ISaveable 註冊點：MissionDispatchService）
IsCritical = false（Degradable）

合併 DTO（同一 owner key 下序列化）：
[Serializable]
class FT02SaveDTO {
    // FT-02-A 部分（MissionDispatchService 持有）
    public List<ActiveMission> activeMissions;
    public int                  _nextActiveMissionID;

    // FT-02-B 部分（CommissionBoardService 持有，由 A 委派序列化）
    public List<int> _regularMissionPool;
    public List<int> _staticMissionPool;
}
```

**A/B 協調介面**：唯一 `ISaveable` owner 為 `MissionDispatchService`（A），`CommissionBoardService`（B）暴露兩個內部 method 供 A 委派：

```
ICommissionBoardService（FT-02-B 公開介面追加，FSD-Codex-Reoprts-260427 T1-FT02 補）：
  GetSerializableState() : (List<int> regular, List<int> staticPool)
      // 回傳當前兩池的快照（B 內部 list 的淺複製）
  RestoreState(List<int> regular, List<int> staticPool) : void
      // 還原兩池內容；對 missionID 逐筆驗證（C-01 GetTemplate != null + categoryID 一致），
      // 失敗條目 LogWarning 並跳過；不發 OnCommissionPostedEvent（避免 restore 時誤發事件）
```

**Restore 順序**：FT-10 Save/Load 驗證 `MissionDispatchService.RestoreFromSave(json)` 流程：(1) 反序列化 `FT02SaveDTO`；(2) 設 `activeMissions` / `_nextActiveMissionID`；(3) 呼叫 `_commissionBoardService.RestoreState(...)`；(4) 廣播一次性 UI 重建訊號（透過下游 P-02 自行訂閱 `OnSaveLoadComplete`，不在此 FSD 規範）。

### 5.4 內部資料流

**流程 A：CalculateRates（UI 預覽 / 派遣前）**

```
呼叫者（P-02 UI 或 MissionDispatchService）.CalculateRates(instanceID, missionID)
  → MissionRateCalculator.CalculateRates(instanceID, missionID)
      ├─ IC02.GetAdventurer(instanceID)  → null 時回傳 (0.0, 0.0) + LogWarning
      ├─ IC01.GetTemplate(missionID)     → null 時回傳 (0.0, 0.0) + LogWarning
      ├─ Step 1：rankDiff = clamp(RANK_INDEX[adv.rank] - DIFF_INDEX[tmpl.difficulty], -3, +3)
      ├─ Step 2：baseSuccess = _successRateMap[rankDiff]（缺行 → LogError，回 0.0）
      ├─ Step 3：baseDeath = IC01.GetBaseDeathRate(tmpl.difficulty)
      ├─ Step 4：職業修正（C-03 IsStrongType/IsWeakType）
      │     if IsStrongType → baseSuccess += STRONG_TYPE_BONUS
      │     elif IsWeakType → baseSuccess -= WEAK_TYPE_PENALTY
      ├─ Step 5：種族修正（C-04）
      │     baseSuccess += IC04.GetSuccessDelta(adv.raceID, tmpl.typeID)
      │     baseDeath   += IC04.GetDeathDelta(adv.raceID, tmpl.typeID)
      ├─ Step 6：特質 stat 修正（C-05，逐 traitID）
      │     foreach traitID in adv.traitIDs:
      │         trait = IC05.GetTrait(traitID) → null 則 LogWarning + continue
      │         if trait.effectType != "stat": continue
      │         if effectTarget matches "success_all" or "success_{typeID}" → baseSuccess += effectValue
      │         if effectTarget matches "death_all" or "death_{typeID}" → baseDeath += effectValue
      ├─ Step 7：finalSuccess = clamp(baseSuccess, 0.0, 1.0)
      └─ Step 8：finalDeath   = clamp(baseDeath, 0.0, 1.0)
  return (finalSuccess, finalDeath)
```

**流程 B：Dispatch（10 步派遣序列）**

```
呼叫者（P-02 PlayerManual 或 FT-03 NpcAutoPick）.Dispatch(instanceID, missionID, source)
  → MissionDispatchService.Dispatch(instanceID, missionID, source)
      ├─ 前置驗證：
      │     adv = IC02.GetAdventurer(instanceID)  → null/非Idle → false + LogWarning
      │     F-02.NowUTC == 0 → false + LogError
      │     tmpl = IC01.GetTemplate(missionID)    → null → false + LogError
      │     adv.status != Idle                    → false + LogWarning
      │     GetActiveMissionCount() >= IFT07.GetMaxConcurrentMissions() → false + LogWarning
      │     _activeMissions.Any(m => m.adventurerInstanceID == instanceID) → false + LogError
      ├─ Step 1：取任務快照（template, difficulty）
      ├─ Step 2：baseReward = IC01.GetBaseReward(difficulty)
      ├─ Step 3：(finalSuccess, finalDeath) = CalculateRates(instanceID, missionID)
      ├─ Step 4：duration = typeID == ESCORT_TYPE_ID
      │               ? IC01.GetEscortDuration(difficulty)
      │               : IC01.GetBaseDuration(difficulty)
      │           dispatchTimestamp = F-02.NowUTC
      │           completionTimestamp = dispatchTimestamp + duration × 60
      ├─ Step 5：activeMissionID = _nextActiveMissionID++
      │           mission = new ActiveMission { ... }
      │           _activeMissions.Add(mission)
      ├─ Step 6：EventBus.Publish(OnCommissionAccepted, missionID, baseReward, source)
      ├─ Step 7：IC02.UpdateStatus(instanceID, Dispatched, activeMissionID)
      ├─ Step 8：EventBus.Publish(OnAdventurerDispatched, instanceID, activeMissionID)
      ├─ Step 9：IC06.OnMissionAccepted(difficulty)
      └─ Step 10：_commissionBoard.RemoveMissionFromBoard(missionID)  // 委託 FT-02-B
  return true
```

**流程 C：TickCompletionCheck（每 OnSecondTick）**

```
F-02.OnSecondTick → MissionDispatchService.TickCompletionCheck(nowUTC)
  → foreach mission in _activeMissions:
      if _publishedCompletionIDs.Contains(mission.activeMissionID): continue
      if nowUTC >= mission.completionTimestamp:
          EventBus.Publish(OnMissionCompleted, mission.activeMissionID)
          _publishedCompletionIDs.Add(mission.activeMissionID)
```

**流程 D：OnOfflineResolved 批次離線處理**

```
F-02.OnOfflineResolved(offlineSeconds)
  → MissionDispatchService.HandleOfflineResolved(offlineSeconds)
      ├─ now = F-02.NowUTC
      ├─ completedIDs = _activeMissions
      │       .Where(m => now >= m.completionTimestamp
      │                && !_publishedCompletionIDs.Contains(m.activeMissionID))
      │       .Select(m => m.activeMissionID).ToArray()
      ├─ completedCount = completedIDs.Length
      └─ EventBus.Publish(OnOfflineMissionsResolved, offlineSeconds, completedCount, completedIDs)
      // TickCompletionCheck 在下一 Tick 逐一發布 OnMissionCompleted，與本流程並行不衝突
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SuccessRateTable.csv` | `rankDiff`、`successRate` | `【FT-02-DS】success-rate-table.md`（_待建_） | MissionRateCalculator 建立 `_successRateMap[rankDiff]` | `MissionRateCalculator.Initialize()`（F-01 載入完成後） |
| `MissionDifficultyTable.csv`（C-01 owner） | `baseDeathRate` | `【C-01-DS】mission-difficulty-table.md` | 透過 `IC01.GetBaseDeathRate(difficulty)` 取得基礎死亡率 | C-01 已載入（FT-02 不直接讀，委派 C-01） |
| `SystemConstants.csv`（F-01 owner） | `STRONG_TYPE_BONUS`、`WEAK_TYPE_PENALTY`、`ESCORT_TYPE_ID` | `【F-01-DS】system-constants.md` | 職業修正幅度與護送判斷 | F-01 DataManager 啟動時載入 |

### 6.2 引用的 ScriptableObject

無。FT-02-A 僅消費 CSV（透過 F-01 DataManager 或 C-01 API）。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `STRONG_TYPE_BONUS`（職業擅長加成值） | `SystemConstants.csv.STRONG_TYPE_BONUS` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `WEAK_TYPE_PENALTY`（職業弱點懲罰值） | `SystemConstants.csv.WEAK_TYPE_PENALTY` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `ESCORT_TYPE_ID`（護送任務類型 ID） | `SystemConstants.csv.ESCORT_TYPE_ID` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `successRate`（各 rankDiff 基礎成功率） | `SuccessRateTable.csv.successRate` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `baseDeathRate`（各難度基礎死亡率） | `MissionDifficultyTable.csv.baseDeathRate`（C-01 owner） | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `SuccessRateTable` 缺某 rankDiff 行 | `Debug.LogError`；查詢回傳 0.0（最保守值） | `MissionRateCalculator` | EditMode test：移除 rankDiff=0 行，驗證回傳 (0.0, baseDeath) |
| `SuccessRateTable.successRate` 超出 [0, 1] | 載入時 clamp 至 [0, 1]，`Debug.LogWarning` | `MissionRateCalculator` | EditMode test：注入越界值，確認載入後已 clamp |
| C-01 `GetBaseDeathRate` 缺難度行 | C-01 內部 LogError 並回傳 0.0；FT-02-A 不額外處理 | `IC01MissionDatabase` | 由 C-01 FSD 覆蓋 |
| `instanceID` 對應冒險者不存在 | `CalculateRates` 回傳 (0.0, 0.0) + LogWarning | `MissionRateCalculator` | EditMode test |
| `missionID` 對應模板不存在 | `CalculateRates` 回傳 (0.0, 0.0) + LogWarning | `MissionRateCalculator` | EditMode test |
| traitID 在 C-05 找不到 | `GetTrait` 回 null，跳過該特質 + LogWarning | `MissionRateCalculator` | EditMode test |
| 修正疊加後超出 [0, 1] | 最終 clamp，不報錯（合法狀態） | `MissionRateCalculator` | EditMode test：極端正/負修正疊加 |
| 冒險者狀態非 Idle | `Dispatch` 回傳 false + LogWarning，不變更任何狀態 | `MissionDispatchService` | EditMode test |
| 進行中任務數達上限 | `Dispatch` 回傳 false + LogWarning | `MissionDispatchService` | EditMode test |
| `Dispatch` 時 `missionID` 不存在 | 回傳 false + LogError | `MissionDispatchService` | EditMode test |
| `Dispatch` 時 F-02.NowUTC == 0 | 回傳 false + LogError，不變更任何狀態 | `MissionDispatchService` | EditMode test（mock F-02 回傳 0） |
| 同一冒險者已有 ActiveMission（防禦） | 回傳 false + LogError | `MissionDispatchService` | EditMode test |
| 離線多任務到期 | `TickCompletionCheck` 單次 Tick 觸發多個 `OnMissionCompleted` | `MissionDispatchService` | EditMode test：mock nowUTC 超過多個 completionTimestamp |
| `RemoveActiveMission` 傳入不存在 ID | LogWarning，無操作 | `MissionDispatchService` | EditMode test |
| FT-04 忘記呼叫 RemoveActiveMission | `_publishedCompletionIDs` 已記錄，後續 Tick 不重發 `OnMissionCompleted` | `MissionDispatchService` | EditMode test：驗證去重機制 |
| 重啟後 `_publishedCompletionIDs` 為空 | 首 Tick 重發已到期任務的 `OnMissionCompleted`（符合預期；FT-04 端 idempotent） | `MissionDispatchService` | 手動驗證（重啟場景，確認重發一次） |
| 存檔 `missionID` 在當前 CSV 找不到 | LogWarning；保留 ActiveMission（快照已存，結算不受影響） | `MissionDispatchService` | EditMode test（mock C-01 GetTemplate 回 null） |
| 存檔 `adventurerInstanceID` 找不到 | LogError；移除 ActiveMission；發布 `OnMissionCancelled` | `MissionDispatchService` | EditMode test |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 SuccessRateTable 定義 | §5.3 資料結構（SuccessRateRow）、§6.1 | 對齊 | — |
| §3.2 基礎死亡率來源（消費 C-01） | §2.2 Data-Specs 引用、§5.4 流程 A | 對齊 | — |
| §3.3 難度索引（共用 F~SSS） | §5.4 流程 A Step 1、§5.3 SuccessRateRow | 對齊 | — |
| §3.4 成功率計算流程（三層修正） | §5.4 流程 A | 對齊 | — |
| §3.5 派遣動作（10 步序列） | §5.4 流程 B | 對齊 | step 10 委託 ICommissionBoardService（FT-02-B）；邏輯等價 |
| §3.6 ActiveMission 資料結構 | §5.3 | 對齊 | — |
| §3.6a Enum 宣告 | §5.3 DispatchSource、CommissionSource（CommissionSource 在 FT-02-B §5.3） | 對齊 | CommissionSource 定義移至 FT-02-B；FT-02-A 只需 DispatchSource |
| §3.7 任務完成檢查（TickCompletionCheck） | §5.4 流程 C | 對齊 | — |
| §3.8 查詢 API | §5.1 公開 API | 對齊 | CommissionBoard 相關 API 在 FT-02-B |
| §3.10 事件契約集中表（FT-02-A 部分） | §2.5、§5.2 | 對齊 | `OnCommissionPosted` 由 FT-02-B 發布 |

### 8.2 公式對齊或替代說明

- GDD §4.1 `rankDiff = clamp(rankIndex - diffIndex, -3, +3)`：直接採用，`_successRateMap` 以 `Dictionary<int, float>` 實作（key = rankDiff）。
- GDD §4.2 三層加法疊加公式：直接採用，`MissionRateCalculator.CalculateRates` 按序執行步驟 4~6，最終一次 clamp。
- GDD §4.3 / §4.4 時長與完成時間：直接採用；`duration × 60` 為分鐘轉秒（Tech Debt 共識，見 §8.3）。
- GDD §4.2 範例（C 階戰士 vs B 難度討伐 → 0.63 / 0.18）：由 AC-MD2-01b EditMode test 覆蓋完整三層疊加；AC-MD2-01 為「無 trait」截斷版本，兩者共同覆蓋 GDD §4.2 全部分支（FSD-Codex-Reoprts-260427 T1-FT02 修正）。

### 8.3 未能實現的規則與修改建議

**偏差 D-01（任務計時責任歸屬，已裁決，FSD-Codex-Reoprts-260427 CT-03）**

- 裁決結果：**FT-02-A `MissionDispatchService` 為唯一任務計時 owner**；F-02 `TimeSystem` 不再持有任務計時器。理由：避免 F-02 與 FT-02-A 雙路徑並存造成「重複完成」或「離線摘要來源分裂」。
- 移除範圍（待實作 PR 處理）：
  1. 刪除 `Assets/Scripts/Core/Time/MissionTimer.cs`
  2. `Assets/Scripts/Core/Time/TimeSystem.cs` 移除 `_missionTimers` / `_publishedExpirations` / `_expiredMissionScratch` 欄位、`RegisterMission` / `UnregisterMission` / `CheckMissionTimers` / `PublishMissionExpired` 方法、`ConfirmOfflineResolution` 內 `OnMissionExpired` 發布迴圈
  3. `Assets/Scripts/Core/Events/GameEvents.cs` 移除或標 `[Obsolete]` `OnMissionExpiredEvent` struct
  4. `OfflineSummary` 不再持有 `CompletedMissionInstanceIds` / `CompletedCount`（改由 FT-02-A 訂閱 `OnOfflineResolvedEvent` 後計算）
- 完成判準：FT-02-A `MissionDispatchService.TickCompletionCheck` 訂閱 typed `OnSecondTickEvent` 後自行掃描 `_activeMissions` 發 `OnMissionCompletedEvent`；F-02 不接觸任何任務 ID。

**偏差 D-02（Save/Load owner 鏈接）**

- F-02 `OnOfflineResolvedEvent` 改為只含 `offlineSeconds`（已於 F-02 §8.3 第 1 條登記）；FT-02-A 訂閱後另發 `OnOfflineMissionsResolvedEvent(offlineSeconds, completedCount, completedActiveMissionIDs[])`。

**Tech Debt TD-01（duration 單位為分鐘）**

- `completionTimestamp = dispatchTimestamp + duration × 60`：`duration` 沿用 C-01 既定分鐘單位（GDD §3.5 step 4、§4.3/§4.4 明文 Tech Debt 共識）。
- FSD 沿用此共識；不構成衝突。未來遷移時 C-01 / F-02 / FT-02 三系統需同步更新。

**Prerequisite — 依賴未完成 FSD（FSD-Codex-Reoprts-260427 CT-10）**

本 FSD 對以下系統的 API / 事件契約引用，目前**尚未經對方 FSD 雙向驗證**，視為 stub 契約：

- FT-07 Guild Building（FSD 未存在）
- FT-10 Save/Load（FSD 未存在）
- P-02 Main UI（FSD 未存在）

實作時須以 mock / stub 介面驗證；對方 FSD 完成後須回頭做雙向對齊複查。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | 無需新增回註；GDD §6.1 已有 F-02 FSD 回註登記 FT-02 需涵蓋 `OnOfflineResolved` 訂閱職責。 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-27 | `duration` 單位為「分鐘」，與全域「秒/小時」規範存在歷史差異 | FT-02 §3.5 step 4、§4.4；F-02 GDD §3.1.2；C-01 GDD §3.1 | 主體確認為 Tech Debt 共識（非衝突），FSD 沿用；已於 §8.3 登記 TD-01 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：每條皆對應 AC-MD2-xx，可被 EditMode test 或 PlayMode 驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：5 條幻想／目的皆有對應技術手段
- [x] §4.1~§4.4：拆分理由成立；Script 清單路徑、SRP、依賴介面、預估規模齊全
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、四段資料流偽碼齊備
- [x] §6.1~§6.3：三張 CSV 引用含對應 Data-Specs；嚴禁寫死清單 5 項對齊原則第 9 條
- [x] §7：GDD §5.1~§5.5 邊緣案例全部對齊（§5.6 CommissionBoard 邊緣案例在 FT-02-B）
- [x] §8.1：對齊清單覆蓋 GDD §3.1~§3.10 二層粒度
- [x] §8.2~§8.5：公式對齊、偏差登記、回註、衝突處理如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 / §7.3 同步更新（見本次任務末段 Edit 操作）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent | 通過 | 通過 | 通過 | MissionTimer.cs 偏差登記 §8.3 D-01；TD-01 分鐘 Tech Debt 共識確認非衝突；FSD-B 拆分合理；CommissionSource 定義移至 FT-02-B |
