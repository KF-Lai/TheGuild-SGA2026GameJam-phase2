# Mission Dispatch 系統設計文件

_建立時間：2026-04-22_
_狀態：設計中_
_系統 ID：FT-02_

---

## 1. 概要（Overview）

FT-02 Mission Dispatch 負責「推薦」機制的核心計算：當玩家選定一個任務並推薦給某位冒險者時，系統計算該配對的**最終成功率**與**最終死亡率**，作為 FT-03 NPC Decision（接受/拒絕判斷）與 FT-04 Outcome Resolution（結算骰）的輸入。

**核心職責**：
1. 計算 `rankDiff`（冒險者階級 vs 任務難度的等級差）
2. 查表取得 `baseSuccessRate`（SuccessRateTable）與 `baseDeathRate`（C-01 `MissionDifficultyTable.baseDeathRate`，透過 `C-01.GetBaseDeathRate` 查詢）
3. 依序套用三層修正：職業（C-03）→ 種族（C-04）→ 特質 stat 類（C-05）
4. Clamp 最終值至 `[0, 1]`
5. 提供派遣 API：將冒險者狀態從 `Idle` → `Dispatched`，綁定任務 ID，記錄派遣時間與預計完成時間

**不負責**：
- willingness / 接受拒絕判斷（FT-03）
- 結算骰與後果（FT-04）
- 金流（FT-05）
- behavior / condition 特質套用（FT-03 / FT-04）

**資料表**：`SuccessRateTable`（PK = rankDiff，FT-02 owner）；基礎死亡率改為消費 C-01 `MissionDifficultyTable.baseDeathRate`（原 `DeathRateTable` 已合併入該表，FT-02 不再獨立持有）

## 2. 玩家幻想（Player Fantasy）

派遣是公會長最關鍵的一刻——你正在決定一個人的命運。

你盯著委託板上那張 B 難度討伐令，再看看名冊裡唯一的 C 階戰士。成功率 70%，看起來不差——但死亡率 18% 讓你猶豫。他是人類，對討伐沒有特別加成；他有「幸運兒」特質，但那是結算時的事，不會幫他提高成功率。你知道如果把這單推薦給那個 D 階遊俠，成功率只剩 35%——而且遊俠弱點是護送，不是討伐，所以至少沒有減益……

這就是派遣系統的核心體驗：**在有限資訊中做出有重量的選擇**。系統不替你做決定，它只把數字攤在你面前——成功率、死亡率、職業適性——然後等你按下「推薦」。每一個百分比都是一條人命的賭注。

## 3. 詳細規則（Detailed Rules）

### 3.1 SuccessRateTable 資料表定義

| 欄位 | 型別 | 說明 |
|------|------|------|
| `rankDiff` | `int` (PK) | 冒險者階級索引 − 任務難度索引，範圍 -3 ~ +3 |
| `successRate` | `float` | 基礎成功率 |

**Game Jam 初始資料**（沿用 game-concept）：

| rankDiff | +3 | +2 | +1 | 0 | -1 | -2 | -3 |
|----------|----|----|----|----|----|----|-----|
| successRate | 0.95 | 0.85 | 0.70 | 0.55 | 0.35 | 0.20 | 0.10 |

### 3.2 基礎死亡率來源（消費 C-01.MissionDifficultyTable）

FT-02 不再獨立持有 `DeathRateTable`；基礎死亡率欄位 `baseDeathRate` 已併入 C-01 `MissionDifficultyTable`（owner = C-01，schema 與 Jam 預設值見 C-01 §3.1）。FT-02 §3.4 透過 `C-01.GetBaseDeathRate(difficulty) : float` API 查詢，回傳 `[0.0, 1.0]` 區間值。

**Jam 預設值**（資料來源 C-01 `MissionDifficultyTable.baseDeathRate`，沿用 game-concept）：

| F | E | D | C | B | A | S | SS | SSS |
|---|---|---|---|---|---|---|----|-----|
| 0.02 | 0.06 | 0.10 | 0.13 | 0.18 | 0.40 | 0.45 | 0.40 | 0.70 |

> 數值來源 C-01 `MissionDifficultyTable.csv`，FT-02 不獨立維護；若兩處不一致以 C-01 為準（AC-MD-14）。

> 調整 `baseDeathRate` 直接改 C-01 `MissionDifficultyTable.csv`，FT-02 不需重新編譯，亦不需新增 `DeathRateTable.csv`。

### 3.3 難度索引（Difficulty Index）

階級與難度共用統一索引：F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8

- 冒險者階級範圍：F~S（索引 0~6）
- 任務難度範圍：F~SSS（索引 0~8）
- `rankDiff = clamp(RANK_INDEX(adventurer.rank) - DIFFICULTY_INDEX(mission.difficulty), -3, +3)`

> clamp 至 `-3 ~ +3`。超低匹配（rankDiff < -3）一律使用 rankDiff = -3 的 10% 成功率，Game Jam 規模下已足夠懲罰極端錯配。

### 3.4 成功率計算流程（修正疊加順序）

```
CalculateRates(adventurer, mission):
    1. rankDiff = clamp(RANK_INDEX(adventurer.rank) - DIFFICULTY_INDEX(mission.difficulty), -3, +3)
    2. baseSuccess = SuccessRateTable[rankDiff].successRate
    3. baseDeath   = C-01.GetBaseDeathRate(mission.difficulty)   // 取自 MissionDifficultyTable.baseDeathRate

    // 第一層：職業修正（僅成功率）
    4. baseSuccess = ApplyProfessionModifier(baseSuccess, adventurer.professionID, mission.typeID)

    // 第二層：種族修正（成功率 + 死亡率）
    5. (baseSuccess, baseDeath) = ApplyRaceModifier(baseSuccess, baseDeath, adventurer.raceID, mission.typeID)

    // 第三層：特質 stat 修正（成功率 + 死亡率）
    6. (baseSuccess, baseDeath) = ApplyStatTraits(baseSuccess, baseDeath, adventurer.traitIDs, mission.typeID)

    // 最終 clamp
    7. finalSuccess = clamp(baseSuccess, 0.0, 1.0)
    8. finalDeath   = clamp(baseDeath, 0.0, 1.0)

    return (finalSuccess, finalDeath)
```

> 三層修正均為**加法疊加**，最終一次 clamp。順序不影響數學結果（加法交換律），但程式碼保持固定順序便於 debug。

### 3.5 派遣動作（Dispatch Action）

玩家推薦任務給冒險者後，若 FT-03 判定冒險者接受，FT-02 執行派遣（步驟即執行順序）：

1. **取任務快照**：`template = C-01.GetTemplate(missionID)`；`difficulty = template.difficulty`
2. **取基礎獎勵**：`baseReward = C-01.GetBaseReward(difficulty)`（C-01 §3）
3. **快照成功率/死亡率**：`(finalSuccessRate, finalDeathRate) = CalculateRates(instanceID, missionID)`（§3.4 / §4.2）
4. **計算預計時長與完成時間**：
   - `duration = mission.typeID == ESCORT_TYPE_ID ? C-01.GetEscortDuration(difficulty) : C-01.GetBaseDuration(difficulty)`
   - `dispatchTimestamp = F-02.NowUTC`
   - `completionTimestamp = dispatchTimestamp + duration × 60`
   - **單位備註**：`duration` 為**分鐘**（沿用 C-01 既定 CSV 單位），× 60 後得秒數加上 Unix 秒 timestamp。**Tech Debt**：全專案時間單位規範為「秒/小時」，C-01 + FT-02 + F-02 既有「分鐘」屬遺留共識，未來統一遷移時三系統需同步更新
5. **建立 ActiveMission 並分配 ID**：`activeMissionID = _nextActiveMissionID++`；建構 `ActiveMission { activeMissionID, missionID, adventurerInstanceID = instanceID, finalSuccessRate, finalDeathRate, dispatchTimestamp, completionTimestamp }`，並 `_activeMissions.Add(activeMission)`
6. **發布傭金接受事件**：`EventBus.Publish(OnCommissionAccepted, missionID, baseReward, source)`
   - `source` 為 `DispatchSource` enum（§3.6a 正式宣告），由呼叫端（P-02 / FT-03）透過 `Dispatch` 參數傳入（見 §3.8）
   - FT-05 Guild Gold Flow 訂閱此事件執行預收傭金（`AddGoldAllowBankruptcy(-baseReward)`，見 F-03 §3.2 rule 7、FT-05 §3.9.1）
   - **挑 FT-02 為單一發布點**：FT-03 自主接單也透過 `FT-02.Dispatch` 間接發布，避免雙重發布；FT-04 結算、FT-11 離線回補同理各自專屬事件
   - **時序保證**：本步驟在 C-02 `UpdateStatus`（step 7）之前發布，符合 FT-05 §3.7 「預收入帳優先於任務時鐘啟動」契約
7. 呼叫 C-02 `UpdateStatus(instanceID, Dispatched, activeMissionID)`（走 C-02 §3.5 overload，自動設定 `currentMissionID = activeMissionID`，維持 invariant）
8. 發布 `EventBus.Publish(OnAdventurerDispatched, instanceID, activeMissionID)`
9. 通知 C-06 危險度計數：`C06.OnMissionAccepted(difficulty)`（C-06 §3 `OnMissionAccepted` 規格；若難度達下一階 `minDifficulty` 則 `acceptedMissionCount += 1`、觸發 `CheckLevelUp()`）
10. **從委託板池移除該 `missionID`**：先以 `IsCommissionOnBoard(missionID)` 判定；若 `_staticMissionPool.Contains(missionID)` 則從 `_staticMissionPool` 移除（靜態優先），否則從 `_regularMissionPool` 移除；若兩池均無（如 NPC 自主接單未經委託板）則靜默忽略

> **順序保證**：步驟 6 `OnCommissionAccepted` 在步驟 7 `UpdateStatus`（C-02 狀態變更）之前發布——FT-05 預收失敗時（理論上 `AddGoldAllowBankruptcy` 不會 reject，但若未來改語義），Dispatch 可於步驟 6 收到異常即 rollback（Game Jam 階段不實作 rollback，信任 F-03 保證）。
>
> **`baseReward` 快照**：步驟 2 取的 `baseReward` 為派遣當下的任務基礎獎勵，FT-02 `ActiveMission` 不額外儲存（結算時 FT-04 依 §3.6「快照不變」規則由 C-01 重新查詢；`baseReward` 不隨時間變動，見 C-01）。

### 3.6 ActiveMission 資料結構

`ActiveMission` 是 runtime 物件，代表一個進行中的任務實例。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `activeMissionID` | `int` | Runtime 唯一 ID，由 FT-02 自增分配 |
| `missionID` | `int` | FK → MissionTemplate（C-01）|
| `adventurerInstanceID` | `int` | FK → AdventurerInstance（C-02）|
| `finalSuccessRate` | `float` | 派遣時快照的最終成功率 |
| `finalDeathRate` | `float` | 派遣時快照的最終死亡率 |
| `dispatchTimestamp` | `long` | 派遣時間（Unix 秒）|
| `completionTimestamp` | `long` | 預計完成時間（Unix 秒）|

> `finalSuccessRate` / `finalDeathRate` 在派遣時快照，不隨後續修正值變動而改變。FT-04 結算時直接使用 ActiveMission 中快照的數值。

### 3.6a Enum 宣告

```csharp
public enum DispatchSource
{
    PlayerManual,    // 玩家於 P-02 委託板手動派遣
    NpcAutoPick,     // FT-03 自主接單（runtime）
    OfflineAutoPick  // FT-11 離線回補（Jam 階段未實作；保留位）
}

public enum CommissionSource
{
    Regular,         // 由 §3.9.2 PostRegularMission 注入
    Static           // 由 §3.9.2 InjectStaticMission 注入（FT-09 劇情委託）
}
```

> Jam 階段 `DispatchSource` 只實際使用 `PlayerManual` / `NpcAutoPick`；`OfflineAutoPick` 為 FT-11 預留，FT-05 §3.7 已對齊三值。

### 3.7 任務完成檢查

FT-02 訂閱 F-02 `OnSecondTick`，每 Tick 檢查所有 ActiveMission：

```
TickCompletionCheck():
    now = F-02.NowUTC
    foreach mission in activeMissions:
        if mission.activeMissionID in _publishedCompletionIDs: continue   // 去重：每筆任務僅發 1 次
        if now >= mission.completionTimestamp:
            EventBus.Publish(OnMissionCompleted, mission.activeMissionID)
            _publishedCompletionIDs.Add(mission.activeMissionID)
            // FT-04 訂閱此事件進行結算
```

> 結算後由 FT-04 呼叫 FT-02 `RemoveActiveMission(activeMissionID)` 清理；`RemoveActiveMission` 內部同步從 `_publishedCompletionIDs` 移除該 ID（避免長期累積）。
>
> **去重保證**：`_publishedCompletionIDs : HashSet<int>` 確保即使 FT-04 延遲呼叫 `RemoveActiveMission`，`OnMissionCompleted` 對同一 `activeMissionID` 也只發送 1 次。`_publishedCompletionIDs` 不需序列化（重啟後 ActiveMission 可能仍在到期狀態，重啟首 tick 重新發 1 次符合預期；FT-04 端 idempotent 處理）。
>
> **FT-02 為任務計時與事件的單一發布點**：F-02 §3 不再維護任務計時器清單，所有任務時序狀態由 FT-02 `activeMissions` 擁有。下游系統（FT-04、P-03）訂閱 `OnMissionCompleted` 而非 F-02 事件。

### 3.8 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 計算成功率/死亡率 | `CalculateRates(int instanceID, int missionID) : (float success, float death)` | UI 預覽用，不執行派遣 |
| 執行派遣 | `Dispatch(int instanceID, int missionID, DispatchSource source) : bool` | 冒險者必須 Idle，失敗回傳 `false`；`source` 由呼叫端指定（P-02 傳 `PlayerManual`、FT-03 自主接單傳 `NpcAutoPick`），用於 §3.5 step 6 發布 `OnCommissionAccepted` |
| 取得進行中任務 | `GetActiveMissions() : IReadOnlyList<ActiveMission>` | |
| 單筆查詢 | `GetActiveMission(int activeMissionID) : ActiveMission` | 找不到回傳 `null` |
| 依冒險者查詢 | `GetActiveMissionByAdventurer(int instanceID) : ActiveMission` | 找不到回傳 `null` |
| 移除已結算任務 | `RemoveActiveMission(int activeMissionID) : void` | FT-04 結算後呼叫 |
| 進行中任務數量 | `GetActiveMissionCount() : int` | 派遣前檢查用（對照 FT-07 `GetMaxConcurrentMissions()` 上限） |
| 取得可用委託 | `GetAvailableCommissions() : IReadOnlyList<int>` | 回傳 `_regularMissionPool ∪ _staticMissionPool` 合集；FT-03 自主接單 + P-02 委託板 UI 用（§3.9.3） |
| 依來源取得委託 | `GetCommissionsBySource(CommissionSource source) : IReadOnlyList<int>` | `source ∈ {Regular, Static}`；供需要區分的訂閱者（§3.9.3） |
| 委託是否在池中 | `IsCommissionOnBoard(int missionID) : bool` | 冪等檢查用（§3.9.3） |
| 注入靜態委託 | `InjectStaticMission(int missionID) : InjectStaticMissionResult` | FT-09 劇情委託注入入口；驗證 `categoryID == 3`（§3.9.2） |

### 3.9 委託板池服務（CommissionBoard）

FT-02 同時擔任「委託板的 runtime 任務池」管理者，持有玩家當前可接的所有委託 missionID 清單。池分兩個子集：

#### 3.9.1 池結構

| 欄位 | 型別 | 說明 |
|---|---|---|
| `_regularMissionPool` | `List<int>` | 由常規生成流程（C-06 加權 + C-01 模板）注入的委託 missionID；玩家手動審核 / FT-03 自主接單可派遣 |
| `_staticMissionPool` | `List<int>` | 由 FT-09 等劇情系統透過 `InjectStaticMission` 注入的靜態委託 missionID（`MissionTemplate.categoryID == 3`）；永久停留至玩家派遣或外部移除 |

> 兩池並存於同一委託板；`GetAvailableCommissions()` 一律回傳合集，呼叫者（FT-03 / P-02）無需區分。

#### 3.9.2 注入 API

**`InjectStaticMission(int missionID) → InjectStaticMissionResult`**

| 回傳碼 | 觸發條件 |
|---|---|
| `OK` | 注入成功；`missionID` 加入 `_staticMissionPool`，發布 `OnCommissionPosted(missionID, source=Static)` |
| `UNKNOWN_MISSION_ID` | `C01.GetTemplate(missionID)` 回 null |
| `WRONG_CATEGORY` | `template.categoryID != 3` |
| `ALREADY_ON_BOARD` | `missionID` 已存在於 `_staticMissionPool` 或 `_regularMissionPool`（冪等保護） |
| `BOARD_DISABLED` | FT-02 暫停或委託板服務未啟用（Jam 階段不會發生） |

呼叫者：FT-09 §3.5.2 ConfirmDialogue Step 4 透過此 API 注入劇情委託。

**`PostRegularMission(int missionID) → PostResult`**（內部 API）

由 FT-02 內部委託生成流程呼叫；將 missionID 加入 `_regularMissionPool` 並發布 `OnCommissionPosted(missionID, source=Regular)`。

| `PostResult` 回傳碼 | 觸發條件 |
|---|---|
| `OK` | 注入成功；missionID 加入 `_regularMissionPool`，發布 `OnCommissionPosted(missionID, source=Regular)` |
| `UNKNOWN_MISSION_ID` | `C01.GetTemplate(missionID)` 回 null |
| `WRONG_CATEGORY` | `template.categoryID != 0`（常規池僅接受 `categoryID == 0`） |
| `ALREADY_ON_BOARD` | `missionID` 已存在於任一池（冪等保護，由 `IsCommissionOnBoard` 判定） |

**Jam 階段常規生成時序**（最簡規則，三軌並存）:

| 軌道 | 觸發 | 動作 |
|---|---|---|
| **啟動填池** | 新遊戲首次進入主場景 + Bootstrap 完成後 | 補滿至 `FT07.GetMissionSlotCount()`(委託板等級決定;對應 §7 Tuning Knob 由 FT-07 BuildingTable 控制),依下方「抽取規則」逐筆呼叫 `PostRegularMission(missionID)` |
| **每日重置補池** | F-02 `OnDailyReset` 訂閱 callback | 計算缺額 `deficit = max(0, FT07.GetMissionSlotCount() - _regularMissionPool.Count - _staticMissionPool.Count)`(`GetMissionSlotCount` 由 FT-07 §3.4 提供);若 deficit > 0,依下方「抽取規則」逐筆 `PostRegularMission` |
| **危險度升階補池** | C-06 `OnDangerLevelChanged` 訂閱 callback | 同每日重置:依 `C-06.GetPoolWeights()`(已升階為新權重)補滿至 `FT07.GetMissionSlotCount()` |

**抽取規則**（每筆）：
1. **抽 difficulty**：依 `C-06.GetPoolWeights()` 對 6 個權重 bucket 做加權隨機（取自 `WorldDangerTable[currentDangerLevel]` 的 `weightF_E` / `weightD` / `weightC` / `weightB` / `weightA` / `weightS_SSS` 欄位群）。命中 bucket 後依以下規則展開為單一難度：
   - `weightF_E` → 50% F / 50% E
   - `weightD` / `weightC` / `weightB` / `weightA` → 直接對應該單一難度
   - `weightS_SSS` → 50% S / 50% SS（SSS 不計入常規池；剔除後將 SSS 的 1/3 份額平分回 S 與 SS）
2. **抽 missionID**：呼叫 `C-01.GetRegularTemplates(difficulty)`（C-01 §3.3 帶參版本）取該難度的常規模板清單（`categoryID == 0`），從清單中均勻隨機抽一筆
3. **冪等保護**：同 `missionID` 已在 `_regularMissionPool` 或 `_staticMissionPool`（透過 `IsCommissionOnBoard(missionID)` 判定）→ 跳過該筆，重抽 difficulty + missionID
4. **收斂保護**：若連續 N 次抽到重複（N = 目標補池筆數 × 2）→ 放棄剩餘補池，`Debug.LogWarning`「regular pool 收斂（模板池可能不足）」

**設計理由**:Jam 階段不需即時生成(避免 frame-bound 邏輯複雜化);啟動 + 每日 + 危險度升階三個觸發點足以維持「打開遊戲總有委託可接」的核心循環體感。Post-Jam 可擴充為基於時間流逝的連續生成。

#### 3.9.3 查詢 API

| API | 簽名 | 說明 |
|---|---|---|
| 取得可用委託 | `GetAvailableCommissions() : IReadOnlyList<int>` | 回傳 `_regularMissionPool ∪ _staticMissionPool` 的合集；FT-03 自主接單（§3.3）+ P-02 委託板 UI 用 |
| 依 source 取得 | `GetCommissionsBySource(CommissionSource source) : IReadOnlyList<int>` | source ∈ {Regular, Static}；供需要區分的訂閱者 |
| 是否已在池中 | `IsCommissionOnBoard(int missionID) : bool` | 用於冪等檢查 |

#### 3.9.4 派遣後從池移除

§3.5 step 10 已規範：`Dispatch` 成功後，由 FT-02 內部於 `OnAdventurerDispatched`（step 8）之後將 missionID 從對應池移除（`_staticMissionPool` 優先；不存在則嘗試 `_regularMissionPool`）；不發 `OnCommissionRemoved` 事件（派遣本身已透過 `OnAdventurerDispatched` 通知下游）。

#### 3.9.5 事件契約（委託板池專屬，集中表見 §3.10）

**`OnCommissionPosted(int missionID, CommissionSource source)`**：任意池注入新 missionID 時發布。
- `source` enum：`Regular`（常規生成）/ `Static`（劇情靜態注入）
- 訂閱者：P-02 Main UI（委託板 UI 顯示新委託；Static 可呈現視覺差異）

#### 3.9.6 持久化

`_regularMissionPool` 與 `_staticMissionPool` 由 FT-10 序列化（透過 FT-02 的 ISaveable 實作）；載入後直接還原（missionID 由 C-01 驗證合法性，不合法則跳過並 LogWarning）。

### 3.10 事件契約（集中表）

FT-02 全部對外事件統一規範如下；其他章節中的事件描述為 contextual 引用，最終 payload / 觸發時機以本表為準。

| 事件 | Payload | 觸發時機 | 訂閱者 |
|---|---|---|---|
| `OnCommissionPosted` | `(int missionID, CommissionSource source)` | `PostRegularMission` / `InjectStaticMission` 成功注入後（§3.9.2） | P-02 Main UI |
| `OnCommissionAccepted` | `(int missionID, int baseReward, DispatchSource source)` | `Dispatch` step 6（§3.5），先於 C-02 `UpdateStatus` | FT-05 Guild Gold Flow |
| `OnAdventurerDispatched` | `(int instanceID, int activeMissionID)` | `Dispatch` step 8（§3.5） | P-02 Main UI、P-03 Notification **【→Log API待更新】** |
| `OnMissionCompleted` | `(int activeMissionID)` | `TickCompletionCheck`（§3.7）首次到期偵測，每筆任務僅發 1 次（去重見 §3.7） | FT-04 Outcome Resolution |
| `OnMissionCancelled` | `(int activeMissionID)` | `RestoreFromSave` 驗證失敗（§5.5 / §6.4 step 3） | P-02 Main UI、FT-04（idempotent；無 ActiveMission 則 no-op） |
| `OnOfflineMissionsResolved` | `(long offlineSeconds, int completedCount, int[] completedActiveMissionIDs)` | F-02 `OnOfflineResolved` 訂閱回呼處理完成後（§6.1 FSD 回註） | P-02 離線摘要 UI |

> 訂閱者責任：不修改傳入 payload；FT-02 為單一發布點，下游若需歷史紀錄需自行 copy。

---

## 4. 公式（Formulas）

### 4.1 rankDiff 計算

```
CalcRankDiff(adventurerRank, missionDifficulty):
    rankIndex = RANK_INDEX[adventurerRank]       // F=0, E=1, D=2, C=3, B=4, A=5, S=6
    diffIndex = DIFFICULTY_INDEX[missionDifficulty]  // F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8
    return clamp(rankIndex - diffIndex, -3, +3)
```

範例：
- C 階戰士 vs B 難度討伐：`clamp(3 - 4, -3, +3) = -1`
- S 階遊俠 vs D 難度採集：`clamp(6 - 2, -3, +3) = +3`（上限 cap）
- F 階傭兵 vs A 難度護送：`clamp(0 - 5, -3, +3) = -3`（下限 cap）

### 4.2 完整成功率/死亡率計算（含範例）

```
CalculateRates(adventurer, mission):
    // Step 1: 基礎值
    rankDiff    = CalcRankDiff(adventurer.rank, mission.difficulty)
    baseSuccess = SuccessRateTable[rankDiff]
    baseDeath   = C-01.GetBaseDeathRate(mission.difficulty)   // 取自 MissionDifficultyTable

    // Step 2: 職業修正（C-03，僅成功率）
    if C03.IsStrongType(adventurer.professionID, mission.typeID):
        baseSuccess += STRONG_TYPE_BONUS          // +0.20
    elif C03.IsWeakType(adventurer.professionID, mission.typeID):
        baseSuccess -= WEAK_TYPE_PENALTY          // -0.15

    // Step 3: 種族修正（C-04，成功率 + 死亡率）
    baseSuccess += C04.GetSuccessDelta(adventurer.raceID, mission.typeID)
    baseDeath   += C04.GetDeathDelta(adventurer.raceID, mission.typeID)

    // Step 4: 特質 stat 修正（C-05，成功率 + 死亡率，加法疊加）
    foreach traitID in adventurer.traitIDs:
        trait = C05.GetTrait(traitID)
        if trait.effectType != "stat": continue
        if trait.effectTarget == "success_all" or "success_{mission.typeID}":
            baseSuccess += trait.effectValue
        if trait.effectTarget == "death_all" or "death_{mission.typeID}":
            baseDeath += trait.effectValue

    // Step 5: clamp
    finalSuccess = clamp(baseSuccess, 0.0, 1.0)
    finalDeath   = clamp(baseDeath, 0.0, 1.0)

    return (finalSuccess, finalDeath)
```

**完整計算範例**：

> C 階戰士（professionID=1, raceID=1 人類, traitIDs 含 `success_1 +0.08`）派遣 B 難度討伐（typeID=1）

| 步驟 | 成功率 | 死亡率 | 說明 |
|------|--------|--------|------|
| 基礎值 | 0.35 | 0.18 | rankDiff = clamp(3-4) = -1 → 35%；B 難度死亡率 18% |
| +職業 | 0.55 | 0.18 | 戰士擅長討伐 → +0.20 |
| +種族 | 0.55 | 0.18 | 人類對討伐無修正（successDelta=0, deathDelta=0） |
| +特質 | 0.63 | 0.18 | `success_1 +0.08` → +0.08 |
| clamp | **0.63** | **0.18** | 最終值 |

### 4.3 派遣時長計算

```
CalcDuration(mission):
    if mission.typeID == ESCORT_TYPE_ID:        // typeID = 2
        return C01.GetEscortDuration(mission.difficulty)    // baseDuration × random(3.0, 5.0)
    else:
        return C01.GetBaseDuration(mission.difficulty)      // 分鐘
```

### 4.4 完成時間計算

```
CalcCompletionTimestamp(dispatchTimestamp, durationMinutes):
    return dispatchTimestamp + durationMinutes × 60    // 轉為秒
```

範例：D 難度討伐，`baseDuration = 150` 分鐘
- `completionTimestamp = 1745000000 + 150 × 60 = 1745009000`（2.5 小時後）

> **時間單位 Tech Debt**：`durationMinutes` 沿用 C-01 既定分鐘單位（與 F-02 §3 規範一致），與全專案規則「秒/小時」存在歷史共識差異。未來統一遷移時 C-01 / F-02 / FT-02 三系統需同步更新（參見 §3.5 step 4 備註）。

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `SuccessRateTable` 缺少某 rankDiff 的行（如缺 -2） | `Debug.LogError`，該 rankDiff 查詢回傳 `0.0`（最保守值） |
| C-01 `MissionDifficultyTable` 缺某 difficulty 行 | C-01 `LogError`，`GetBaseDeathRate` 回傳 `0.0`（C-01 EC，FT-02 不額外處理） |
| `SuccessRateTable` 的 `successRate` 超出 `[0, 1]` 範圍 | `Debug.LogWarning`，載入時 clamp 至 `[0, 1]` |
| C-01 `MissionDifficultyTable.baseDeathRate` 超出 `[0, 1]` 範圍 | C-01 `LogError` 並 clamp（C-01 EC，FT-02 不額外處理） |

### 5.2 CalculateRates

| 情況 | 處理方式 |
|------|---------|
| `instanceID` 對應的冒險者不存在（C-02 回傳 `null`） | `Debug.LogWarning`，回傳 `(0.0, 0.0)` |
| `missionID` 對應的模板不存在（C-01 回傳 `null`） | `Debug.LogWarning`，回傳 `(0.0, 0.0)` |
| 冒險者 `professionID` 在 C-03 找不到 | C-03 `IsStrongType` / `IsWeakType` 回傳 `false`，無修正（C-03 內部已報錯） |
| 冒險者 `raceID` 在 C-04 找不到 | C-04 `GetSuccessDelta` / `GetDeathDelta` 回傳 `0.0`，無修正（C-04 內部已報錯） |
| 冒險者 `traitIDs` 含不存在的 traitID | C-05 `GetTrait` 回傳 `null`，跳過該特質，`Debug.LogWarning` |
| 所有修正疊加後成功率 > 1.0 或 < 0.0 | 最終 clamp 處理，不報錯（合法狀態） |
| 所有修正疊加後死亡率 < 0.0 | clamp 至 0.0（種族/特質可能將死亡率壓至負值，屬正常設計） |

### 5.3 Dispatch

| 情況 | 處理方式 |
|------|---------|
| 冒險者狀態非 `Idle`（Dispatched / Wounded / Dead） | 回傳 `false`，`Debug.LogWarning` |
| 進行中任務數已達 FT-07 `GetMaxConcurrentMissions()` 上限 | 回傳 `false`，`Debug.LogWarning` |
| `missionID` 對應的模板不存在 | 回傳 `false`，`Debug.LogError` |
| 同一冒險者已有 ActiveMission（理論不可能，因狀態已是 Dispatched） | 回傳 `false`，`Debug.LogError`（防禦性） |
| `Dispatch` 呼叫時 F-02 timestamp 回傳 0 | `Debug.LogError`，回傳 `false`，不變更任何狀態 |

### 5.4 任務完成檢查

| 情況 | 處理方式 |
|------|---------|
| 離線期間多個任務到期 | `TickCompletionCheck` 一次 Tick 觸發多個 `OnMissionCompleted` 事件，FT-04 依序結算 |
| `RemoveActiveMission` 傳入不存在的 `activeMissionID` | `Debug.LogWarning`，無操作 |
| FT-04 結算後忘記呼叫 `RemoveActiveMission` | ActiveMission 殘留，但 `_publishedCompletionIDs` 已記錄該 ID，後續 Tick **不再重發** `OnMissionCompleted`（§3.7 去重機制保證每筆任務發布 1 次） |
| 重啟後 `_publishedCompletionIDs` 為空，已到期但未結算的 ActiveMission | 重啟首 Tick 重新發布 1 次 `OnMissionCompleted`（符合預期：FT-04 idempotent 處理重啟後重發） |

### 5.5 存檔相關

| 情況 | 處理方式 |
|------|---------|
| 需序列化的狀態 | `activeMissions` 列表、`_nextActiveMissionID` 自增計數器 |
| 載入存檔後 `activeMissionID` 計數器重建 | 取所有 ActiveMission 中最大 `activeMissionID + 1` |
| 載入存檔後重新訂閱計時事件 | FT-02 在 `RestoreFromSave` 內確認已訂閱 F-02 `OnSecondTick`（若 `OnEnable` 已訂閱則不重複）；`activeMissions` 還原後立即由 `OnSecondTick` 驅動 `TickCompletionCheck`，無需呼叫任何 F-02 計時器 API |
| 存檔中 `missionID` 在當前 CSV 找不到 | `Debug.LogWarning`，保留 ActiveMission（finalSuccessRate / finalDeathRate 已快照），結算不受影響 |
| 存檔中 `adventurerInstanceID` 在名冊中找不到 | `Debug.LogError`，移除該 ActiveMission，發布 `OnMissionCancelled` 事件 |

### 5.6 委託板池服務（CommissionBoard）

| 情況 | 處理方式 |
|------|---------|
| `InjectStaticMission` 傳入的 `missionID` 在 C-01 找不到 | 回傳 `UNKNOWN_MISSION_ID`，不加入任何池（對應 FT-09 EC-7） |
| `InjectStaticMission` 傳入的 `missionID` 對應 `categoryID != 3` | 回傳 `WRONG_CATEGORY`，不加入任何池（對應 FT-09 EC-7） |
| `InjectStaticMission` 同一 `missionID` 重複注入 | 回傳 `ALREADY_ON_BOARD`，冪等保護，不重複加入 |
| 派遣時 `missionID` 同時存在於 `_staticMissionPool` 與 `_regularMissionPool`（理論不應發生，`ALREADY_ON_BOARD` 保護） | `_staticMissionPool` 優先移除（§3.9.4 靜態優先規則）；`Debug.LogWarning` 提示異常狀態 |
| `GetAvailableCommissions` 於兩池均空時呼叫 | 回傳空集合，不報錯 |
| 載入存檔後 `_regularMissionPool` 或 `_staticMissionPool` 內含不合法 `missionID`（C-01 找不到） | 跳過該 missionID，`Debug.LogWarning`；保留合法項目 |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-02 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `SuccessRateTable`（FT-02 owner） | `DataManager.GetAll<T>()` |
| F-02 Time System | 取得當前 timestamp（派遣時間）；訂閱 `OnSecondTick` 驅動完成檢查；訂閱 `OnOfflineResolved(offlineSeconds)` 計算並發布離線任務結算事件（見下方 FSD 回註）；F-02 不提供計時器清單 API | `NowUTC`、`OnSecondTick`、`OnOfflineResolved` 回呼 |
| C-01 Mission Database | 查詢任務模板（difficulty, typeID）、任務時長、基礎死亡率（原 `DeathRateTable` 已合併入 C-01 `MissionDifficultyTable`） | `GetTemplate(missionID)`、`GetBaseDuration(difficulty)`、`GetEscortDuration(difficulty)`、`GetBaseDeathRate(difficulty)` |
| C-02 Adventurer Management | 查詢冒險者資料（rank, professionID, raceID, traitIDs, status）；更新狀態 | `GetAdventurer(instanceID)`、`GetByStatus(Idle)`、`UpdateStatus()` |
| C-03 Profession System | 查詢職業擅長/弱點 | `IsStrongType(professionID, typeID)`、`IsWeakType(professionID, typeID)` |
| C-04 Race System | 查詢種族成功率/死亡率修正 | `GetSuccessDelta(raceID, typeID)`、`GetDeathDelta(raceID, typeID)` |
| C-05 Trait System | 查詢 stat 類特質的修正值 | `GetTrait(traitID)` |
| FT-07 Guild Building System | 查詢最大同時任務數（派遣前檢查；由公會櫃臺等級決定） | `GetMaxConcurrentMissions()` |
| C-06 World Danger System | 派遣後通知 C-06 計數，驅動危險度升級 | `OnMissionAccepted(difficulty)`（見 §3.5 步驟 9） |

> **FSD 回註（2026-04-26，源自 `【F-02-FSD】time-system.md` §8.3 方案 A）**：FT-02 額外承擔以下職責，待 FT-02 FSD 細化規格：
> - 訂閱 F-02 `OnOfflineResolved(long offlineSeconds)`；
> - 依自身 `activeMissions` 計算 `completedCount = activeMissions.Count(m => F-02.NowUTC >= m.completionTimestamp)`，並收集對應的 `activeMissionID` 清單；
> - 發布 `OnOfflineMissionsResolved(long offlineSeconds, int completedCount, int[] completedActiveMissionIDs)` 供離線摘要 UI 訂閱；
> - `TickCompletionCheck`（§3.7）持續處理逐一完成的 `OnMissionCompleted`，與本職責並行不衝突。
> 本回註不修改 §3.7 既有規則，僅補充 FT-02 FSD 階段必須涵蓋的離線結算介面契約。

### 6.2 下游依賴（依賴 FT-02 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| FT-03 NPC Decision | 讀取 `CalculateRates` 的成功率/死亡率作為 willingness 計算輸入；自主接單呼叫 `Dispatch(..., NpcAutoPick)` 觸發預收；自主接單前呼叫 `GetAvailableCommissions()` 取可派遣任務清單 | `CalculateRates(instanceID, missionID)`、`Dispatch(..., source)`、`GetAvailableCommissions()` |
| FT-04 Outcome Resolution | 訂閱 `OnMissionCompleted` 事件、讀取 ActiveMission 快照數值進行結算、結算後呼叫移除 | `GetActiveMission(activeMissionID)`、`RemoveActiveMission(activeMissionID)` |
| FT-05 Guild Gold Flow | 訂閱 `OnCommissionAccepted(missionID, baseReward, source)` 執行預收傭金 | `OnCommissionAccepted` 事件 |
| FT-09 Faction Story System | 呼叫 `InjectStaticMission(missionID)` 於玩家確認劇情對話後注入靜態劇情委託（§3.9.2）；訂閱 `OnCommissionPosted(missionID, source=Static)` 可選 | `InjectStaticMission(missionID)` |
| FT-10 Save/Load | 序列化/反序列化 `activeMissions` 列表及 `_regularMissionPool` / `_staticMissionPool` 兩池 | `GetActiveMissions()`、`_regularMissionPool`、`_staticMissionPool`（§3.9.6） |
| P-02 Main UI | 顯示成功率/死亡率預覽、進行中任務列表與倒數計時；玩家手動派遣呼叫 `Dispatch(..., PlayerManual)`；委託板 UI 呼叫 `GetAvailableCommissions()` 並訂閱 `OnCommissionPosted` 更新顯示 | `CalculateRates()`、`GetActiveMissions()`、`GetActiveMissionByAdventurer()`、`Dispatch(..., source)`、`GetAvailableCommissions()`、`OnCommissionPosted` 事件 |

### 6.3 循環依賴注意事項

- FT-02 依賴 FT-07 查詢 `GetMaxConcurrentMissions()`，FT-07 不依賴 FT-02——**無循環依賴**
- FT-03 讀取 FT-02 `CalculateRates`，FT-02 不依賴 FT-03——**無循環依賴**
- FT-04 訂閱 FT-02 事件並呼叫 `RemoveActiveMission`，FT-02 不依賴 FT-04——**無循環依賴**
- FT-02 呼叫 C-06 `OnMissionAccepted`，C-06 不依賴 FT-02——**無循環依賴**

### 6.4 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft02Dispatch"` |
| `IsCritical` | `false`（Degradable；還原失敗時清空進行中任務，玩家失去任務進度但核心循環仍可繼續） |

**`Serialize()` 序列化欄位**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `activeMissions` | `List<ActiveMission>` | 所有進行中任務（§3.5 既定結構） |
| `_nextActiveMissionID` | `int` | 下一個可用 activeMissionID |
| `_regularMissionPool` | `List<int>` | 常規委託池（missionID 列表，§3.9.6） |
| `_staticMissionPool` | `List<int>` | 靜態劇情委託池（missionID 列表，§3.9） |

> `_publishedCompletionIDs : HashSet<int>`（§3.7 去重結構）**不**序列化；重啟後為空集合，已到期但未結算的 ActiveMission 於首 Tick 重新發 1 次 `OnMissionCompleted`，符合 §5.4 row 4 預期。

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化上述欄位。
2. 逐筆驗證 `ActiveMission.missionID` 透過 `C-01.GetTemplate(missionID)` 確認合法、`adventurerInstanceID` 透過 `C-02.GetAdventurer(instanceID)` 確認存在。
3. 驗證失敗的單筆 `ActiveMission`：從列表移除並發布 `OnMissionCancelled(activeMissionID)`（對齊 §5.5；C-02 的冒險者狀態由 C-02 在 RestoreFromSave 時已設為 Idle，此事件僅通知下游）。
4. 還原完成後，FT-02 自行重新訂閱 `F-02.OnSecondTick` 觸發 `TickCompletionCheck`（§5.5 既定，無需外部呼叫）。

**`InitializeAsNewGame()` 預設值**：空 `activeMissions` 列表，`_nextActiveMissionID = 1`，`_regularMissionPool` / `_staticMissionPool` 各為空列表。

對應 FT-10 §3.3.3 拓撲順序 row 6、§3.3.4 Degradable 分類、§6.1 #10（FT-10 設計來源清單）。

## 7. 可調參數（Tuning Knobs）

### 7.1 SuccessRateTable.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| rankDiff = 0 的 `successRate` | `0.55` | `0.40 ~ 0.65` | 同階匹配的基準線；過高讓派遣無風險，過低讓同階任務也令人卻步 |
| rankDiff = -1 的 `successRate` | `0.35` | `0.25 ~ 0.45` | 低一階的挑戰感；搭配職業擅長 +0.20 後應可拉回 0.55 附近，讓「對的人接對的任務」有意義 |
| rankDiff = +3 的 `successRate` | `0.95` | `0.90 ~ 0.99` | 碾壓任務的安全感；不設為 1.0 保留微小失敗可能性 |
| rankDiff = -3 的 `successRate` | `0.10` | `0.05 ~ 0.15` | 極端錯配的懲罰；過低接近必敗，過高讓玩家賭博心態增加 |

### 7.2 C-01 MissionDifficultyTable.baseDeathRate（FT-02 消費）

> `baseDeathRate` 已合併入 C-01 `MissionDifficultyTable`（owner = C-01，§3.1）；下表為 FT-02 消費端視角的安全範圍與意圖，調整數值請改 C-01 的 CSV。

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| F 難度 `baseDeathRate` | `0.02` | `0.01 ~ 0.05` | 教學任務的死亡風險；過高嚇退新手，過低讓死亡系統無存在感 |
| B 難度 `baseDeathRate` | `0.18` | `0.10 ~ 0.25` | 中高難度的分水嶺；此處開始死亡成為實質威脅 |
| A 難度 `baseDeathRate` | `0.40` | `0.30 ~ 0.50` | 高難度的死亡壓力；搭配種族/特質降低後仍應維持 20%+ |
| SSS 難度 `baseDeathRate` | `0.70` | `0.50 ~ 0.80` | 終極任務；需要 `on_death_survive` 特質才有較好存活率（值與 C-01 AC-MD-14 對齊） |

### 7.3 SystemConstants.csv（FT-02 讀取的常數）

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `STRONG_TYPE_BONUS` | `0.20` | `0.10 ~ 0.35` | 職業擅長加成（C-03 定義，FT-02 套用）；過高讓職業匹配成為唯一策略 |
| `WEAK_TYPE_PENALTY` | `0.15` | `0.05 ~ 0.30` | 職業弱點懲罰（C-03 定義，FT-02 套用）；過高讓錯配近乎必敗 |
| `ESCORT_TYPE_ID` | `2` | 固定，不調整 | 護送任務判斷依據 |

### 7.4 資料表調整原則

- **SuccessRateTable**：調整時需驗證 rankDiff=-1 + 職業擅長後的「有效成功率」是否合理（預期約 0.55），確保「對的人做對的事」有明確回報
- **C-01 MissionDifficultyTable.baseDeathRate**：調整時需搭配 C-04 種族 `deathDelta`（±0.08~0.10）與 C-05 特質 `death_*`（±0.15）評估修正後的實際死亡率區間（CSV 由 C-01 維護）
- 兩張表格的調整均會影響 FT-03 NPC Decision 的 willingness 計算（`willingnessScore = finalSuccess - finalDeath × DEATH_AVERSION`），需連動評估

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-MD2-01 | `CalculateRates` 對 C 階戰士（professionID=1）派 B 難度討伐（typeID=1），baseSuccess = 0.35（rankDiff=-1），套用職業擅長後 = 0.55，無種族/特質修正時 finalSuccess = 0.55、finalDeath = 0.18 |
| AC-MD2-02 | `CalculateRates` 對 S 階冒險者派 D 難度任務，rankDiff = clamp(6-2) = +3，baseSuccess = 0.95 |
| AC-MD2-03 | `CalculateRates` 對 F 階冒險者派 A 難度任務，rankDiff = clamp(0-5) = -3，baseSuccess = 0.10 |
| AC-MD2-04 | 職業擅長修正：戰士（professionID=1）派討伐（typeID=1），成功率增加 `STRONG_TYPE_BONUS`（0.20） |
| AC-MD2-05 | 職業弱點修正：戰士（professionID=1）派調查（typeID=4），成功率減少 `WEAK_TYPE_PENALTY`（0.15） |
| AC-MD2-06 | 傭兵（professionID=7）無擅長/弱點，任何 typeID 成功率均無職業修正 |
| AC-MD2-07 | 種族修正：人類（raceID=1）派採集（typeID=3），成功率增加 0.08；死亡率不變 |
| AC-MD2-08 | 種族修正：獸人（raceID=3）派討伐（typeID=1），死亡率減少 0.08 |
| AC-MD2-09 | 特質 stat 修正：持有 `success_all +0.05` 特質的冒險者，任何 typeID 成功率均增加 0.05 |
| AC-MD2-10 | 特質 stat 修正：持有 `death_1 -0.05` 特質的冒險者，派討伐（typeID=1）死亡率減少 0.05；派護送（typeID=2）死亡率不變 |
| AC-MD2-11 | 多層修正疊加：所有修正加法疊加後，最終 clamp 至 `[0, 1]`；不會出現負成功率或超過 100% |
| AC-MD2-12 | `Dispatch` 對 `Idle` 冒險者成功後，冒險者狀態變為 `Dispatched`，`currentMissionID` 正確設定 |
| AC-MD2-13 | `Dispatch` 對 `Wounded` / `Dead` / `Dispatched` 冒險者回傳 `false`，狀態不變 |
| AC-MD2-14 | `Dispatch` 在進行中任務數達 `maxMissions` 時回傳 `false` |
| AC-MD2-15 | `Dispatch` 成功後 `GetActiveMissions()` 包含新建的 ActiveMission，`finalSuccessRate` / `finalDeathRate` 與 `CalculateRates` 一致 |
| AC-MD2-16 | ActiveMission 的 `completionTimestamp` = `dispatchTimestamp + duration × 60`；護送任務使用 `GetEscortDuration`，其餘使用 `GetBaseDuration` |
| AC-MD2-17 | `TickCompletionCheck` 在 `completionTimestamp` 到期時發布 `OnMissionCompleted` 事件，帶正確 `activeMissionID` |
| AC-MD2-18 | 離線期間多個任務到期，重啟後 `TickCompletionCheck` 一次 Tick 觸發所有到期任務的 `OnMissionCompleted` |
| AC-MD2-19 | `RemoveActiveMission` 呼叫後，`GetActiveMissions()` 不再包含該任務，`GetActiveMissionCount()` 減 1 |
| AC-MD2-20 | `GetActiveMissionByAdventurer(instanceID)` 回傳該冒險者的 ActiveMission；無派遣時回傳 `null` |
| AC-MD2-21 | `OnCommissionAccepted` 發布 — 玩家呼叫 `Dispatch(instanceID, missionID, PlayerManual)` 成功時，發布 `OnCommissionAccepted(missionID, baseReward, PlayerManual)` 恰一次，且在 C-02 `UpdateStatus` 與 `OnAdventurerDispatched` 之前 |
| AC-MD2-22 | NPC 自主接單發布 — FT-03 呼叫 `Dispatch(instanceID, missionID, NpcAutoPick)` 成功時，發布 `OnCommissionAccepted(missionID, baseReward, NpcAutoPick)` 恰一次；FT-03 本身不另外發布同名事件（單一發布點） |
| AC-MD2-23 | `baseReward` 取得 — 於 §3.5 step 2 呼叫 `C-01.GetBaseReward(template.difficulty)` 取得 `baseReward`；事件 payload 與此值一致 |
