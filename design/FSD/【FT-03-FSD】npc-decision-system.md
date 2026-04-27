# 【FT-03-FSD】功能規格說明書 — NPC Decision System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-03】npc-decision-system.md`（版本：2026-04-22） |
| 對應 Data-Specs | `【F-01-DS】system-constants.md`（共用 `DEATH_AVERSION` / `ACCEPTANCE_THRESHOLD` / `WILLINGNESS_JITTER` / `AUTO_PICKUP_IDLE_MINUTES` / `AUTO_PICKUP_INTERVAL_MINUTES`） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（Self-Review） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-03 NPC Decision System 負責計算「冒險者是否願意接受任務」的意願分數（effectiveScore），並執行兩種決策行為：玩家推薦時的接受/拒絕判斷，以及冒險者閒置後的自主接單排程。本系統為純決策計算層，不持有任何任務或冒險者的主要狀態，也不直接執行派遣——接受後的派遣動作交由呼叫方（P-02 推薦路徑）或直接呼叫 FT-02 API（自主接單路徑）完成。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：
- `effectiveScore` 四步驟公式（基礎意願 → behavior 特質修正 → 委託官加成 → jitter）
- `MakeDecision` 推薦判斷 API 與 `RejectionReason` 分類
- `PreviewEffectiveScore` 預覽 API（不含 jitter）
- `AutoPickupTick` 排程（訂閱 `OnMinuteTick`）與 `TryAutoPickup` 邏輯
- `MatchesBehaviorTarget` behavior 特質匹配規則
- `OnAutoPickup` 事件發布

**Out-of-Scope**：
- 成功率 / 死亡率計算（FT-02 負責）
- 任務結算與後果（FT-04 負責）
- 金流（FT-05 負責）
- condition 類特質套用（FT-04 負責）
- 離線期間補算自主接單（FT-11 負責，Jam 範疇外）
- `idleSinceTimestamp` / `lastAutoPickupTimestamp` 持久化（由 C-02 AdventurerInstance 一併序列化）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準，以下每條皆有可驗證的程式條件：

| ID | 完成條件 | 驗證方式 |
| --- | --- | --- |
| AC-ND3-01 | `MakeDecision` 無特質無加成時，effectiveScore 落在 `willingnessScoreBase ± WILLINGNESS_JITTER` 區間，且 `accepted=true` | EditMode test：固定 Random seed，斷言範圍與 accepted 值 |
| AC-ND3-02 | `willingness_diff_A -0.30` 特質：A 難度任務減 0.30；C 難度任務不受影響 | EditMode test：構造 mock 冒險者與任務，比較兩次 `MakeDecision` 的 willingnessScore 差值 |
| AC-ND3-03 | willingnessScoreBase=0.20 + 最大正 jitter → `accepted=true`；+ 負 jitter → `accepted=false` | EditMode test：注入固定 jitter 值，斷言 `accepted` 結果 |
| AC-ND3-04 | 委託官在職 `GetStaffWillingnessBonus()=0.05`；未招募 `=0.0`，公式其餘部分結果一致 | EditMode test：mock FT-12 介面兩次，斷言 effectiveScore 差值 = 0.05 |
| AC-ND3-05 | `RejectionReason` 三分支判斷正確：TooRisky / NotWilling / NotInterested | EditMode test：三組參數各斷言對應 rejectionReason |
| AC-ND3-06 | 非 `Idle` 冒險者呼叫 `MakeDecision` 回傳 `accepted=false`，不觸發派遣 | EditMode test：mock C-02 回傳 Dispatched 狀態，斷言無 Dispatch 呼叫 |
| AC-ND3-07 | `AutoPickupTick` 在 `AUTO_PICKUP_IDLE_MINUTES` 到達前不對冒險者觸發 `TryAutoPickup` | EditMode test：模擬 tick 呼叫，stub idleMinutes < 10，斷言無派遣 |
| AC-ND3-08 | 閒置滿 10 分鐘後觸發 `TryAutoPickup`，選取 effectiveScore 最高任務 | PlayMode / EditMode test：stub tick，斷言選取 bestMissionID |
| AC-ND3-09 | 自主接單後 `lastAutoPickupTimestamp` 更新；下次至少間隔 `AUTO_PICKUP_INTERVAL_MINUTES` | EditMode test：斷言 `SetLastAutoPickupTimestamp` 以正確時間戳呼叫 |
| AC-ND3-10 | 所有任務 effectiveScore < ACCEPTANCE_THRESHOLD 時不派遣，仍更新 timestamp | EditMode test：stub 所有任務分數低，斷言無 Dispatch 但 timestamp 更新 |
| AC-ND3-11 | 自主接單成功後 `OnAutoPickup` 攜帶正確 `adventurerInstanceID` 與 `missionID` | EditMode test：訂閱事件，驗證 payload |
| AC-ND3-12 | `FT02.Dispatch()` 回傳 `false` 時靜默失敗，不重試，不拋例外 | EditMode test：stub Dispatch 回傳 false，斷言無例外且事件未發布 |
| AC-ND3-13 | 未知 `effectTarget` 跳過特質，輸出 `Debug.LogWarning`，不影響其餘計算 | EditMode test：注入未知 effectTarget，比較 effectiveScore 與無特質結果一致 |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 內容摘要 |
| --- | --- |
| §1 概要 | 系統職責邊界：推薦判斷 + 自主接單；不負責的項目清單 |
| §3.1 | 推薦判斷流程：4 步驟、DecisionResult、RejectionReason 判斷優先序 |
| §3.2 | 委託官加成：`FT12.GetStaffWillingnessBonus()` 調用時機與 cap 責任歸屬 |
| §3.3 | 自主接單流程：觸發條件、`AutoPickupTick`、`TryAutoPickup` 選任務邏輯 |
| §3.4 | 查詢與操作 API：`MakeDecision` / `PreviewEffectiveScore` 簽名與語意 |
| §3.5 | 事件契約：`OnAutoPickup` payload 與訂閱者 |
| §4.1 | effectiveScore 公式（4 步驟）與不 clamp 規定 |
| §4.2 | `MatchesBehaviorTarget` 規則表 |
| §4.3 | 計算範例（兩組） |
| §5 | 邊緣案例：§5.1 推薦判斷 4 項、§5.2 自主接單 6 項 |
| §6 | 依賴關係：上游 7 個系統、下游 3 個系統、循環依賴注意事項、ISaveable 契約 |
| §7 | 可調參數：SystemConstants 5 個、TraitTable behavior 值、調整原則 |
| §8 | 驗收標準 13 條 |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `DEATH_AVERSION`、`ACCEPTANCE_THRESHOLD`、`WILLINGNESS_JITTER`、`AUTO_PICKUP_IDLE_MINUTES`、`AUTO_PICKUP_INTERVAL_MINUTES` | effectiveScore 公式常數；自主接單觸發閾值與間隔 |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 使用介面 |
| --- | --- | --- |
| F-01 DataManager | 載入 SystemConstants 常數 | `IDataManager.GetConstant<float>(key)` |
| F-02 Time System | 取得當前 Unix 秒時間戳；訂閱 `OnMinuteTick` 驅動 `AutoPickupTick` | `ITimeSystem.NowUTC`、`EventBus.Subscribe<OnMinuteTick>` |
| C-02 Adventurer Management | 查詢 Idle 冒險者列表；讀取 `idleSinceTimestamp` / `lastAutoPickupTimestamp`；更新 `lastAutoPickupTimestamp` | `IAdventurerRoster.GetByStatus(AdventurerStatus.Idle)`、`IAdventurerRoster.GetAdventurer(instanceID)`、`IAdventurerRoster.SetLastAutoPickupTimestamp(instanceID, timestamp)` |
| C-05 Trait System | 查詢 behavior 類特質的 `effectTarget` / `effectValue` | `ITraitService.GetTrait(traitID)` |
| FT-02 Mission Dispatch | 計算成功率/死亡率；自主接單路徑執行派遣；取得委託板可用任務列表 | `IMissionRateCalculator.CalculateRates(instanceID, missionID)`、`IMissionDispatchService.Dispatch(instanceID, missionID, DispatchSource)`、`ICommissionBoardService.GetAvailableCommissions()` |
| FT-06 Guild Core | 取得公會可接受的最高任務難度，過濾自主接單候選任務 | `IGuildCoreService.GetMaxMissionDifficulty() : string` |
| FT-12 Staff System | 查詢委託官被動意願加成（已 cap） | `IStaffService.GetStaffWillingnessBonus() : float` |

### 2.4 下游被依賴系統

| 系統 | 依賴內容 | 使用介面 |
| --- | --- | --- |
| P-02 Main UI | 呼叫 `MakeDecision` 取得接受/拒絕結果；呼叫 `PreviewEffectiveScore` 顯示分數（可選） | `INpcDecisionService.MakeDecision(instanceID, missionID)`、`INpcDecisionService.PreviewEffectiveScore(instanceID, missionID)` |
| P-03 Notification System | 訂閱 `OnAutoPickup` 事件，顯示「冒險者自行接取委託」通知 | `EventBus.Subscribe<OnAutoPickupEvent>` |
| FT-10 Save/Load | `idleSinceTimestamp` / `lastAutoPickupTimestamp` 透過 C-02 AdventurerInstance 一併序列化；FT-03 ISaveable 回傳空 JSON，`RestoreFromSave` 時重新訂閱 `OnMinuteTick` | `ISaveable`（`OwnerKey = "ft03Decision"`，`IsCritical = false`） |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMinuteTick` | 訂閱（來自 F-02） | `float nowUTC` | 驅動 `AutoPickupTick`，每分鐘掃描 Idle 冒險者 |
| `OnAutoPickupEvent` | 發布（FT-03 發出） | `int adventurerInstanceID, int missionID` | 自主接單成功後通知 P-03 顯示通知；GDD §3.5 Log API 待更新，不阻礙 FSD |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家推薦任務給冒險者，但冒險者有自己的性格——「膽小」的法師拒絕死亡率高的任務，盾衛卻毫不在乎。玩家離開一段時間回來後，發現委託板上那張留給精英的任務被閒不住的冒險者自己接走了。核心情感：「你推薦，但他們決定」，以及「公會是活的」的持續感受。

### 3.2 系統目的還原

為任務配對提供 NPC 決策層：以成功率、死亡率、behavior 特質、委託官加成與隨機抖動計算冒險者的接單意願；當意願分數達到門檻時接受任務，否則回傳拒絕原因。閒置一段時間後，冒險者自主掃描可用委託並接取意願最高的任務，製造「NPC 有自主性」的玩法張力。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 冒險者有個性，不是棋子 | 相同任務對不同冒險者的接受/拒絕結果不同；拒絕時顯示原因代碼（TooRisky / NotWilling / NotInterested） | `MatchesBehaviorTarget` 逐 behavior 特質累加 `effectValue`；四步驟 effectiveScore 公式；`RejectionReason` 由首次低於門檻的步驟決定 |
| jitter 製造「NPC 有時做次優決策」 | 邊緣案例（分數接近門檻）下接受/拒絕結果不完全可預測 | `Random.Range(-WILLINGNESS_JITTER, +WILLINGNESS_JITTER)` 在 Step 4 加入 |
| 委託官提升整體接單率 | 招募委託官後，冒險者接任務的機率略微提升 | `IStaffService.GetStaffWillingnessBonus()` 在 Step 3 加入，cap 責任在 FT-12 |
| 公會是活的（自主接單） | 玩家下線後回來發現委託板變少；難得一見的好任務可能被提前接走 | 訂閱 `OnMinuteTick`；`AutoPickupTick` 依 `idleSinceTimestamp` 與 `lastAutoPickupTimestamp` 守衛觸發；`TryAutoPickup` 選 effectiveScore 最高任務 |
| 玩家可搶在 NPC 前推薦 | 在計算視窗前手動推薦即可搶先；已被推薦（進入派遣流程）的任務從委託板移除 | `GetAvailableCommissions()` 回傳的列表由 FT-02 §3.9.4 派遣後自動移除，確保不重複接取 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。

### 4.2 拆分理由

FT-03 的三個子邏輯（推薦判斷、自主接單排程、effectiveScore 公式）共享同一個 `CalcEffectiveScore` 核心方法，無法在不重複邏輯的前提下分離。預估總行數 250~350 行，遠低於 500 行拆分門檻。規劃 3 個 Script：型別定義（< 50 行）、服務介面（< 30 行）、服務實作（200~280 行）。

### 4.3 拆分結果

（不適用，未拆分。）

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `NpcDecisionTypes.cs` | `Assets/Scripts/Gameplay/Decision/NpcDecisionTypes.cs` | 定義 `DecisionResult`、`RejectionReason` enum、`OnAutoPickupEvent` DTO | 無（純資料型別） | < 50 行 |
| `INpcDecisionService.cs` | `Assets/Scripts/Gameplay/Decision/INpcDecisionService.cs` | 宣告 FT-03 對外公開 API 介面（`MakeDecision`、`PreviewEffectiveScore`） | 無（純介面） | < 30 行 |
| `NpcDecisionService.cs` | `Assets/Scripts/Gameplay/Decision/NpcDecisionService.cs` | 實作 willingness 公式、推薦判斷、自主接單排程，訂閱 `OnMinuteTick`，發布 `OnAutoPickupEvent` | `IDataManager`、`ITimeSystem`、`IAdventurerRoster`、`ITraitService`、`IMissionRateCalculator`、`IMissionDispatchService`、`ICommissionBoardService`、`IGuildCoreService`、`IStaffService`、`EventBus`、`MissionDifficultyUtil` | 200~280 行 |

### 4.5 類別關係（可選）

```
INpcDecisionService
    └─ NpcDecisionService（implements）
           ├─ uses NpcDecisionTypes（DecisionResult, RejectionReason, OnAutoPickupEvent）
           ├─ subscribes EventBus<OnMinuteTick>
           └─ publishes EventBus<OnAutoPickupEvent>
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// INpcDecisionService.cs
public interface INpcDecisionService
{
    /// 計算冒險者對指定任務的決策。純計算，不執行 dispatch。
    /// 冒險者非 Idle 時：回傳 accepted=false, rejectionReason=null。
    DecisionResult MakeDecision(int instanceID, int missionID);

    /// 計算 willingnessScoreAfterStaff（Step 3 後，不含 jitter），供 UI 預覽。
    float PreviewEffectiveScore(int instanceID, int missionID);
}
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMinuteTick` | 訂閱（F-02 發出） | `float nowUTC` | 驅動 `AutoPickupTick`，每「分鐘」（= 60 秒，F-02 alias）掃描一次 |
| `OnAutoPickupEvent` | 發布（FT-03 發出） | `int AdventurerInstanceID, int MissionID` | 自主接單成功後；P-03 訂閱顯示通知（`【→Log API 待更新】`） |

### 5.3 資料結構

```csharp
// NpcDecisionTypes.cs

public enum RejectionReason
{
    TooRisky,      // willingnessScoreBase < ACCEPTANCE_THRESHOLD
    NotWilling,    // willingnessScoreAfterTraits < threshold，但 Base >= threshold
    NotInterested  // effectiveScore < threshold，但 willingnessScoreAfterStaff >= threshold
}

public readonly struct DecisionResult
{
    public readonly bool Accepted;
    public readonly float EffectiveScore;
    public readonly RejectionReason? RejectionReason; // accepted=true 時為 null
}

public readonly struct OnAutoPickupEvent
{
    public readonly int AdventurerInstanceID;
    public readonly int MissionID;
}
```

常數（來自 SystemConstants.csv，Awake 期間透過 `IDataManager` 載入，快取至 private 欄位）：

| 欄位名（private） | CSV key | 型別 |
| --- | --- | --- |
| `_deathAversion` | `DEATH_AVERSION` | `float` |
| `_acceptanceThreshold` | `ACCEPTANCE_THRESHOLD` | `float` |
| `_willingnessJitter` | `WILLINGNESS_JITTER` | `float` |
| `_autoPickupIdleSeconds` | `AUTO_PICKUP_IDLE_MINUTES` × 60 | `float`（換算秒） |
| `_autoPickupIntervalSeconds` | `AUTO_PICKUP_INTERVAL_MINUTES` × 60 | `float`（換算秒） |

> Tech Debt TD-01：`AUTO_PICKUP_IDLE_MINUTES` / `AUTO_PICKUP_INTERVAL_MINUTES` CSV key 沿用分鐘命名（F-02 + FT-02 + FT-03 三系統共識），載入時乘以 60 轉換為秒；待未來統一遷移時三系統同步更新 key 名稱。

### 5.4 內部資料流

**觸發點 A：P-02 呼叫推薦判斷**

```
P-02.OnRecommendButtonClicked(instanceID, missionID)
  → INpcDecisionService.MakeDecision(instanceID, missionID)
      ├─ 1. C-02.GetAdventurer(instanceID)：若狀態 != Idle → 回傳 {Accepted=false, RejectionReason=null}
      ├─ 2. FT-02.CalculateRates(instanceID, missionID) → (finalSuccessRate, finalDeathRate)
      ├─ 3. CalcEffectiveScore(adventurer, missionTemplate, s, d)
      │       ├─ 3a. willingnessScoreBase = s - d × _deathAversion
      │       ├─ 3b. foreach traitID: if MatchesBehaviorTarget → += effectValue
      │       ├─ 3c. willingnessScoreAfterStaff = score + FT-12.GetStaffWillingnessBonus()
      │       └─ 3d. effectiveScore = willingnessScoreAfterStaff + Random.Range(-_jitter, +_jitter)
      ├─ 4. 判斷 RejectionReason（依首次低於 _acceptanceThreshold 的步驟）
      └─ 5. 回傳 DecisionResult（不執行 Dispatch）
  ← P-02 收到 Accepted=true 時，P-02 自行呼叫 FT-02.Dispatch(instanceID, missionID, PlayerManual)
```

**觸發點 B：F-02 OnMinuteTick（自主接單排程）**

```
F-02.OnMinuteTick(nowUTC)
  → NpcDecisionService.AutoPickupTick(nowUTC)
      foreach adventurer in C-02.GetByStatus(Idle):
          ├─ idleSeconds = nowUTC - adventurer.idleSinceTimestamp
          │   if idleSeconds < _autoPickupIdleSeconds: continue
          ├─ sinceLastSeconds = nowUTC - adventurer.lastAutoPickupTimestamp
          │   if sinceLastSeconds < _autoPickupIntervalSeconds: continue
          └─ TryAutoPickup(adventurer, nowUTC)
                ├─ 1. candidates = FT-02.GetAvailableCommissions()
                │       .Where(id => DifficultyIndex(C-01.GetTemplate(id).difficulty)
                │                    <= DifficultyIndex(FT-06.GetMaxMissionDifficulty()))
                ├─ 2. 遍歷 candidates，CalcEffectiveScore 取 bestMissionID（最高 score）
                ├─ 3. C-02.SetLastAutoPickupTimestamp(adventurer.instanceID, nowUTC)
                └─ 4. if bestMissionID != -1 and bestScore >= _acceptanceThreshold:
                          FT-02.Dispatch(adventurer.instanceID, bestMissionID, NpcAutoPick)
                              ├─ if Dispatch 回傳 false：靜默失敗，不重試
                              └─ if Dispatch 回傳 true：EventBus.Publish(new OnAutoPickupEvent(...))
```

**觸發點 C：P-02 呼叫預覽（可選）**

```
P-02.OnHoverMissionSlot(instanceID, missionID)
  → INpcDecisionService.PreviewEffectiveScore(instanceID, missionID)
      ├─ 1. FT-02.CalculateRates(instanceID, missionID) → (s, d)
      ├─ 2. willingnessScoreBase = s - d × _deathAversion
      ├─ 3. foreach traitID: += effectValue（behavior 特質）
      ├─ 4. += FT-12.GetStaffWillingnessBonus()
      └─ 回傳 willingnessScoreAfterStaff（不加 jitter）
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SystemConstants.csv` | `DEATH_AVERSION`、`ACCEPTANCE_THRESHOLD`、`WILLINGNESS_JITTER`、`AUTO_PICKUP_IDLE_MINUTES`、`AUTO_PICKUP_INTERVAL_MINUTES` | `【F-01-DS】system-constants.md` | effectiveScore 公式常數；自主接單觸發與間隔閾值 | `NpcDecisionService.Awake()`（透過 `IDataManager.GetConstant<float>(key)` 載入並快取） |

### 6.2 引用的 ScriptableObject

無。FT-03 所有可調參數來自 `SystemConstants.csv`。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `DEATH_AVERSION` | `SystemConstants.csv` → `DEATH_AVERSION` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `ACCEPTANCE_THRESHOLD` | `SystemConstants.csv` → `ACCEPTANCE_THRESHOLD` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `WILLINGNESS_JITTER` | `SystemConstants.csv` → `WILLINGNESS_JITTER` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `AUTO_PICKUP_IDLE_MINUTES`（秒值） | `SystemConstants.csv` → `AUTO_PICKUP_IDLE_MINUTES` × 60 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `AUTO_PICKUP_INTERVAL_MINUTES`（秒值） | `SystemConstants.csv` → `AUTO_PICKUP_INTERVAL_MINUTES` × 60 | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1 冒險者狀態非 Idle（Dispatched / Wounded / Dead） | `MakeDecision` 開頭查 `C-02.GetAdventurer(instanceID).Status`；非 Idle 直接回傳 `{Accepted=false, RejectionReason=null}` 並 `Debug.LogWarning`；不執行任何計算 | `NpcDecisionService` | EditMode test（AC-ND3-06）：mock 非 Idle 狀態，斷言 Dispatch 未被呼叫 |
| §5.1 behavior 特質 effectValue 極端負值（如 -0.9） | 不 clamp effectiveScore，讓分數自然低於門檻；`RejectionReason = NotWilling`（若 Base >= threshold 但 AfterTraits < threshold） | `NpcDecisionService` | EditMode test（AC-ND3-02 延伸）：注入極端 effectValue，斷言結果為 NotWilling |
| §5.1 委託官未招募 | `IStaffService.GetStaffWillingnessBonus()` 回傳 `0`；Step 3 加 0 不改變分數，公式正常執行 | `NpcDecisionService` | EditMode test（AC-ND3-04）：mock 回傳 0，斷言 effectiveScore 與無 Step 3 等值 |
| §5.1 未知 effectTarget（CSV 資料錯誤） | `MatchesBehaviorTarget` 對未知值回傳 `false` 並 `Debug.LogWarning`；跳過該特質，不影響其餘計算 | `NpcDecisionService` | EditMode test（AC-ND3-13）：注入未知 effectTarget 特質，比較 effectiveScore 與無特質結果一致 |
| §5.2 委託板無可用任務（candidates 為空） | `TryAutoPickup` 內 candidates 集合為空；`SetLastAutoPickupTimestamp` 仍更新；不派遣，靜默跳過 | `NpcDecisionService` | EditMode test（AC-ND3-10 延伸）：stub GetAvailableCommissions 回傳空列表，斷言無 Dispatch |
| §5.2 所有任務 bestScore < ACCEPTANCE_THRESHOLD | 遍歷後 `bestMissionID = -1` 或 `bestScore < threshold`；更新 `lastAutoPickupTimestamp`，不執行派遣 | `NpcDecisionService` | EditMode test（AC-ND3-10）：斷言 Dispatch 未呼叫但 timestamp 已更新 |
| §5.2 `FT02.Dispatch()` 回傳 false（任務被搶 / 派遣數上限） | 自主接單視為失敗；不重試；`OnAutoPickupEvent` 不發布；等待下一個 interval | `NpcDecisionService` | EditMode test（AC-ND3-12）：stub Dispatch 回傳 false，斷言事件未發布且無例外 |
| §5.2 離線期間累積多個 interval 未觸發 | 依 F-02 §3.2 rule 6（離線不補發 `OnMinuteTick`）；重啟後依正常節奏接收 tick；不主動補算離線期間；FT-11 Offline Resolver 為 Jam 範疇外 | `NpcDecisionService` | 手動驗證：重啟遊戲，確認冒險者在重啟後正常等待 idle 時間再觸發（非立即補算） |
| §5.2 存檔讀取後 lastAutoPickupTimestamp = 0 | `idleMinutes` / `sinceLastPickup` 計算結果大（因 timestamp = 0 → 差值 = nowUTC）；正常通過守衛條件，等待 `AUTO_PICKUP_IDLE_SECONDS` 後觸發，視同正常流程 | `NpcDecisionService` | EditMode test：設定 timestamp = 0，nowUTC = 800 秒，模擬首次載入觸發節奏 |
| §5.2 TryAutoPickup 執行中途冒險者死亡或被推薦（同幀邏輯理論上不可能） | 防禦性：`FT02.Dispatch()` 內部已驗證狀態，回傳 false 時自主接單靜默失敗 | `NpcDecisionService` | 回歸 AC-ND3-12 即涵蓋此情境 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 推薦判斷流程（4 步驟、DecisionResult、RejectionReason 優先序） | §5.1、§5.3、§5.4 觸發點 A | 對齊 | RejectionReason 三分支完整對應 §3.1 判斷優先序表 |
| §3.2 委託官加成（Step 3 位置、cap 責任歸屬） | §5.4 觸發點 A Step 3c | 對齊 | cap 責任在 FT-12；FT-03 直接消費回傳值 |
| §3.3 自主接單流程（AutoPickupTick、TryAutoPickup、DIFFICULTY_INDEX 過濾） | §5.4 觸發點 B | 對齊 | `MissionDifficultyUtil.DifficultyIndex` 為共用 helper，FSD §4.4 已標注 |
| §3.4 查詢與操作 API 簽名（MakeDecision、PreviewEffectiveScore） | §5.1 | 對齊 | |
| §3.5 OnAutoPickup 事件契約 | §5.2 | 對齊 | `【→Log API 待更新】` 標記保留，不阻礙 FSD |

### 8.2 公式對齊或替代說明

採用 GDD §4.1 公式原式，無替代。四步驟順序與 GDD 完全一致（Base → Traits → Staff → Jitter）。`MatchesBehaviorTarget` 規則表（GDD §4.2）完整對應，共 8 個 effectTarget 值。`effectiveScore` 不 clamp（對齊 GDD §4.1 備註），值域為任意實數，僅與 `ACCEPTANCE_THRESHOLD` 比較。

### 8.3 未能實現的規則與修改建議

無無法實現的規則。以下為建議項（不阻礙實作啟動）：

**B-01**：`MissionDifficultyUtil.DifficultyIndex(string)` 為 FT-02 與 FT-03 共用 helper；建議確認其歸屬位置（FT-02 FSD §4.4 已規劃，FT-03 直接引用）。若 FT-02 尚未實作此 util，可暫時由 FT-03 Script 目錄提供，待 FT-02 完成後遷移。

**B-02**：GDD §3.5 `OnAutoPickup` 標注「`【→Log API待更新】`」；FSD 採用 `EventBus.Publish<OnAutoPickupEvent>` 模式，與其他事件一致。待 P-03 Notification System 設計確定後，GDD §3.5 / §6.2 需更新訂閱者說明，不影響 FT-03 本身實作。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 無 | — | — | 本 FSD 無需對 GDD 加回註（無規格偏差，無替代公式，無職責邊界澄清）。B-01/B-02 為建議項，待後續系統 FSD 撰寫時確認。 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 無 | — | — | 無衝突。時間單位 Tech Debt（`AUTO_PICKUP_*_MINUTES` 分鐘命名）已在 GDD §3.3 / §7.3 明確標注為 F-02 + FT-02 + FT-03 三系統共識，非衝突；FSD 載入時乘 60 轉換為秒，不阻礙撰寫。 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：13 條驗收標準每條均有可測試的 EditMode test 或手動步驟
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：5 個幻想／目的皆有對應技術手段
- [x] §4.1~§4.4：拆分判斷有結論（否）；Script 清單 3 個，欄位齊全（路徑、SRP、依賴介面、預估規模）
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、三個觸發點偽碼齊備
- [x] §6.1~§6.3：引用 CSV 含對應 Data-Specs；5 個常數全部列入嚴禁寫死清單，對齊原則第 9 條
- [x] §7：GDD §5.1（4 項）+ §5.2（6 項）共 10 項邊緣案例全部有對策（包含處理方式、涉及 Script、驗證方式）
- [x] §8.1 對齊清單覆蓋 GDD §3 全部 5 個小節（§3.1~§3.5）
- [x] §8.2~§8.5：公式對齊（採原式）、無法實現項（無）、GDD 回註（無）、衝突紀錄（無）皆如實登記
- [x] FSD-index：§6.1 FT-03 row 更新、§6.2 F-01-DS 追加 FT-03-FSD、§7.1 新增進度列、§7.2 新增 review 列，同步完成（見索引更新）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（Self-Review） | 通過 | 通過 | 通過 | 無衝突；Tech Debt TD-01 已標注；建議項 B-01/B-02 不阻礙實作；§5.3 TD-01 換算規則明確（CSV 分鐘值 × 60 = 秒快取欄位） |
