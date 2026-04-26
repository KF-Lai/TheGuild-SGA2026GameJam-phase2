# Faction Story System 系統設計文件

_建立時間：2026-04-25_
_狀態：草稿（8 節全完成，待 design-review）_
_最後更新：2026-04-26_
_系統 ID：FT-09_

**🔖 全 8 節完成**：§1 ~ §8 全部寫入。下一步：(a) 執行 `/design-review FT-09`；(b) 通過後處理 §6.4 反向依賴 9 個 GDD 更新；(c) 進入 Codex 實作階段。

---

## 設計來源（Design Inputs）

本 GDD 基於以下決策來源撰寫：

- `design/gdd/game-concept.md` §「陣營劇情系統」— 2 條路線（Jam 版 1 條）、styleTag、3-5 個劇情階段原始概念
- `design/gdd/systems-index.md` — `FactionRouteTable`、`StoryStageTable` schema 概述
- 依賴 GDD 約束：
  - `[FT-04] outcome-resolution.md` — `OnMissionResolved(Outcome)` 事件、`Outcome.missionFactionID` / `Outcome.isSuccess` 欄位（FT-04 §3.1 / §6 line 274）
  - `[C-01] mission-database.md` — `GetTemplatesByCategory(categoryID=3)` 取陣營劇情特殊任務、`MissionTemplate.factionID` FK（C-01 §3.2 / §6 line 189）
  - `[C-06] world-danger-system.md` — `OnFactionScoreUpdated(int newMaxScore)` 主動推送契約（C-06 §3.3 / §4.5 / §6.3）
  - `[F-01] data-manager.md` — `Get<FactionRouteData>` / `Get<StoryStageData>` / `GetWhere` 查詢（F-01 §6 line 185）
  - `[FT-08] gacha-system.md` — `StaffGachaPoolTable` 5 個預留閘欄位 schema；Jam 版**無 runtime 依賴**
  - `[FT-12] staff-system.md` — `StaffTable.factionID` 預留欄位 schema；Jam 版**無 runtime 依賴**
- 全局規則：`feedback_time_units`（時間單位僅秒/小時）、`feedback_data_driven`（一切由 CSV 驅動，零硬編碼）

---

## 1. 概要（Overview）

**FT-09 Faction Story System 是公會的線性陣營劇情系統**：透過訂閱 FT-04 `OnMissionResolved` 事件，依任務 `factionID` 與任務難度從 C-01 `MissionDifficultyTable.factionScoreDelta` 取加權分數（僅成功結算計入；該欄位由 C-01 `GetFactionScoreDelta(difficulty)` 提供），更新陣營累積分數；當該分數越過 `StoryStageTable.triggerCondition.threshold` 時解鎖下一劇情階段，以「對話視窗 + 特殊靜態委託」雙軌呈現——FT-09 發布 `OnFactionStoryStageUnlocked` 事件供 P-02 開啟對話視窗（讀 `dialogueKey`），玩家確認後 FT-09 透過 FT-02 將階段對應的 `missionID`（`MissionTemplate.categoryID = 3`）注入委託板供玩家手動接單。系統涵蓋三大職責：

1. **分數累積（Score Accrual）**：訂閱 FT-04 `OnMissionResolved`，依 `Outcome.missionFactionID` / `Outcome.isSuccess` / `Outcome.missionDifficulty` 透過 `C-01.GetFactionScoreDelta(difficulty)` 取加權分數（資料來源 C-01 `MissionDifficultyTable.factionScoreDelta`），更新對應陣營累積值；`factionID == FACTION_NEUTRAL_ID(0)` 或 `isSuccess == false` 一律不計入
2. **階段解鎖與雙軌呈現（Stage Unlock & Dual-Track Presentation）**：監聽分數變化，當任意陣營當前累積分數越過下一階段 `StoryStageTable.triggerCondition.threshold` 時，先發布 `OnFactionStoryStageUnlocked` 事件（payload 含 `stageID` / `dialogueKey` / `missionID`）；玩家於對話視窗確認後（P-02 訂閱該事件並負責 UI），FT-09 透過 FT-02 將階段 `missionID` 注入委託板；階段委託走標準派遣 → 結算流程，結算後的階段推進規則由 §3 詳述
3. **下游推送（Downstream Push）**：陣營分數更新時主動呼叫 `C-06.OnFactionScoreUpdated(maxScore)`，將任意陣營當前最高分數推送給世界危險度系統（Jam 版單路線 → maxScore 即該唯一陣營分數，Post-Jam 多陣營自動取 max）

**Jam 版範疇**：

- **1 條陣營路線**（線性劇情）：`FactionRouteTable` 僅 1 筆有效記錄（例：`factionID = 1`），其他任務一律 `factionID = FACTION_NEUTRAL_ID`；第 2 條路線 schema 預留，Post-Jam 啟用
- **3 ~ 5 個劇情階段**：`StoryStageTable` 依 `stageIndex` 線性排列，依分數 threshold 順序解鎖
- **單向加分**：僅成功結算且 `factionID != 0` 計入；失敗 / neutral / 未接受 一律不變動分數
- **降級行為**：`FactionRouteTable` 無有效路線（僅剩 neutral）時整系統靜默停用——不訂閱事件、不推送 C-06、不發布階段解鎖，不阻擋核心循環
- **文本來源**：階段對話（`dialogueKey` → DialogueTable）與劇情委託描述（`missionID` → MissionNamePool）由 haiku / gemini 在內容階段生成，FT-09 GDD 不規範文本內容、僅規範 schema 與觸發條件

**不在 Jam 範疇**：

- 第 2 條陣營路線（schema 已預留，Post-Jam）
- 跨陣營對抗（成功一邊扣另一邊）
- FT-12 職員陣營傾向影響分數（FT-12 已預留 `StaffTable.factionID`，FT-09 Jam 版不消費）
- 多分支劇情樹（階段線性，不支援分歧）
- 接受時計分（FT-09 不訂閱 FT-02 `OnCommissionAccepted`，僅監聽結算事件）
- 階段任務本身的特殊規則（如必須特定冒險者派遣、雙重保險等）；Jam 版階段任務走 FT-02 標準派遣流程

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-09 於 Jam 版聚焦兩個核心情緒，對齊 `game-concept.md`「敘事劇情」（優先度 1）與「探索與發現」（優先度 3），並支撐**支柱 2「命運由你塑造」**：

**沉默累積中的命運推力（Narrative / Submission）**

- 玩家每一次審核、每一次成功結算，都在無形中推動陣營分數——當某個 threshold 被悄然跨過，世界就會以一個劇情階段回應
- 不是「選 A 路線 vs B 路線」的明選，而是**日積月累的傾向**最終獲得回應
- 玩家不需要主動規劃陣營進度，劇情會在某個結算之後突然到來——「我做的每件事，原來都被記下了」

**揭曉時刻的張力（Discovery / Sensation）**

- 對話視窗在普通結算後彈出時的「異常感」——這不是日常結算通知，是世界在跟你說話
- 特殊靜態委託出現在委託板上時的「視覺差異」——劇情任務以 `categoryID = 3` 為錨，P-02 可呈現不同框型 / 顏色
- 完成劇情委託後的「結局揭曉」——這個階段完了，但下一個還在前方，留下牽引

### 玩家幻想敘事

> 「我已經半個多月沒怎麼接 F 級委託了——我的冒險者夠強了，每天都接 B、A 等級的硬骨頭。今天傍晚剛結算完一單成功的 A 級討伐，正要把名單存檔，桌面突然彈出一段對話——是公會門口傳來的消息：『北方邊境傳來戰報……』。我從來沒料到我這幾週的『忽視小委託、專注硬骨頭』，竟然把公會推到了這個位置。委託板上多出一筆紅框任務，我還沒準備好——但這就是我選擇的方向，不是嗎？」

### 設計原則（FT-09 如何強化幻想）

1. **被動驅動，主動回應**：分數累積完全由玩家既有的「審核 / 派遣 / 結算」決策驅動，不要求玩家額外操作；階段解鎖時的對話視窗才是「主動回應」的時刻——這個節奏分配讓「不知不覺塑造命運」可信
2. **里程碑而非進度條**：玩家不需要看到 `currentScore / threshold` 這種進度條 UI（Jam 版可由 P-02 選擇隱藏）——劇情階段解鎖本身就是最強的回饋
3. **對話 + 委託雙軌的功能分工**：對話視窗提供「敘事張力」與「世界擴展」，委託提供「玩法閉環」——兩者結合避免純文字 / 純玩法的失衡
4. **單向加分對應「累積感」**：失敗不扣分的設計讓玩家相信「我做過的每件好事都還算數」——對應「公會編年史」的敘事直覺
5. **線性而非分支**：Jam 版「線性劇情」的取捨換來文本量可控（haiku / gemini 各階段生成）+ 設計複雜度可控；階段本身的張力靠 `dialogueKey` 文本撐起，不靠分支選擇

### 關鍵情緒階段（Emotional Beats）

| 階段 | 觸發事件 | 期望情緒 |
|------|---------|---------|
| 首次劇情解鎖 | 累積分數越過第一個 threshold，`OnFactionStoryStageUnlocked` 首次發布 | 驚喜、世界擴展 |
| 對話視窗彈出 | P-02 訂閱事件後彈出對話框，讀 `dialogueKey` | 沉浸、敘事張力 |
| 劇情委託入板 | 玩家確認對話後，`categoryID = 3` 任務注入委託板 | 期待、決策重量 |
| 劇情委託成功結算 | 階段 `missionID` 經 FT-04 結算為 success | 成就、推進感 |
| 最後一個階段完成 | 該陣營所有階段解鎖並完成 | 完滿、回顧（Jam 版終局信號） |

---

## 3. 詳細規則（Detailed Rules）

> 本章採 **§3.1 → §3.7** 七子節結構：啟停 → schema → 分數累積 → 階段解鎖判定 → 雙軌呈現 → 階段推進 → API 與 runtime 狀態。

### 3.1 系統啟停與降級行為（System Activation & Degradation）

#### 3.1.1 啟用條件（Activation Condition）

FT-09 啟用採**二元判定**——啟動時根據 CSV 資料載入結果一次性決定 `_isEnabled` flag，不在 runtime 動態切換：

| 條件 | 判定 |
|------|------|
| `FactionRouteTable` 載入成功且至少有 1 筆 `factionID != FACTION_NEUTRAL_ID` 的記錄 | 必要 |
| `StoryStageTable` 載入成功且至少有 1 筆有效記錄 | 必要 |
| C-01 `MissionDifficultyTable` 載入成功且涵蓋全部 9 種難度（F~SSS）—— `factionScoreDelta` 欄位 ≥ 0 | 必要（owner = C-01；FT-09 透過 `C-01.GetFactionScoreDelta` 消費）|

- **全部滿足** → `_isEnabled = true`，FT-09 進入正常運作模式
- **任一不滿足** → `_isEnabled = false`，FT-09 進入降級模式（§3.1.3）

> 設計動機：FT-09 是 Jam 後段功能（Day 7-8），核心循環必須在 FT-09 缺席時仍可運作；同時也支援「玩家想跳過劇情、純玩經營」的開發 / playtest 場景——只需清空 `FactionRouteTable` 即可全系統靜默停用。

#### 3.1.2 Bootstrap 流程

FT-09 Bootstrap 在以下時機執行（依時序）：

```
Bootstrap(SaveData? saveData):
    Step A: DataManager.Get<FactionRouteData>() / Get<StoryStageData>()
            // baseDeathRate / factionScoreDelta 改為消費 C-01 MissionDifficultyTable，FT-09 不直接 Get
    Step B: 驗證 §3.1.1 啟用條件 → 設定 _isEnabled

    IF NOT _isEnabled:
        return  // 降級結束 Bootstrap，不訂閱事件、不還原狀態

    Step C: EventBus.Subscribe<OnMissionResolved>(HandleOnMissionResolved)
    Step D: 還原狀態（若 saveData != null）：
        _factionScores         = saveData.factionScores         ?? new Dictionary<int,int>()
        _unlockedStageIndices   = saveData.unlockedStageIndices   ?? new Dictionary<int,int>()
        _pendingDialogueStages  = saveData.pendingDialogueStages  ?? new Queue<int>()
    Step E: 推送初始分數至 C-06：
        C-06.OnFactionScoreUpdated(GetMaxFactionScore())   // 即使全 0 也推一次，保持 C-06 同步
    Step F: 補發未確認對話階段：
        FOR each stageID IN _pendingDialogueStages:
            EventBus.Publish(OnFactionStoryStageUnlocked(stageID, ...))
```

> Step F 處理「玩家上次離線時階段已解鎖但對話視窗未確認」的場景——重發事件讓 P-02 重新彈出對話框。詳細 `_pendingDialogueStages` 入隊 / 出隊規則見 §3.4 / §3.5。

#### 3.1.3 降級行為（Degradation Contract）

當 `_isEnabled == false` 時，FT-09 進入降級模式。本表彙整所有 API / 事件 / 內部流程的降級行為：

**對外 API 行為**：

| API | 降級時行為 |
|---|---|
| `IsFactionStoryEnabled() : bool` | return `false` |
| `GetCurrentFactionScore(int factionID) : int` | return `0` |
| `GetMaxFactionScore() : int` | return `0` |
| `GetUnlockedStageIndex(int factionID) : int` | return `-1`（無階段解鎖） |
| `ConfirmDialogue(int stageID) : Result` | return `STORY_SYSTEM_DISABLED` |

**事件發布**：

| 事件 | 降級時 |
|---|---|
| `OnFactionStoryStageUnlocked` | 不發布（無 `OnMissionResolved` 訂閱可觸發）|
| `OnFactionScoreChanged`（給 P-02 訂閱）| 不發布 |
| `C-06.OnFactionScoreUpdated()` 主動推送 | 不執行（C-06.`_cachedMaxFactionScore` 維持 `0` 初始值；C-06 §5 已涵蓋此場景）|

**內部流程**：

| 流程 | 降級時 |
|---|---|
| `HandleOnMissionResolved`（FT-04 事件 callback）| 不訂閱、不執行 |
| 階段解鎖判定（§3.4）| 不執行 |
| FT-02 注入劇情委託（§3.5）| 不執行 |

**持久化資料**：

降級期間，FT-09 在 SaveData 的相關欄位**寫入空 / 預設值**：

- `factionScores = {}`
- `unlockedStageIndices = {}`
- `pendingDialogueStages = []`

> 玩家若在 Post-Jam 啟用 FT-09（補上 CSV）後載入舊存檔，所有狀態從零開始，符合「降級期間沒有有意義狀態」的直覺。

#### 3.1.4 啟用 / 降級的 runtime 切換

Jam 版**不支援 runtime 熱切換**：`_isEnabled` 在 Bootstrap 後不再變更。原因：

1. CSV 是 Resources 內嵌資源，runtime 不熱更新
2. 「玩家中途啟用 / 停用陣營劇情」不在 Jam 玩法範疇
3. 簡化降級邏輯——避免 runtime 訂閱 / 取消訂閱、半啟用半停用的不一致狀態

> Post-Jam 若需要支援（例如 DLC 解鎖陣營劇情），需新增 `EnableFactionStory()` / `DisableFactionStory()` API 並補對應的 runtime callback。

---

### 3.2 資料表 Schema

本節定義 FT-09 owner 的 2 張 CSV 表：`FactionRouteTable`（§3.2.1）、`StoryStageTable`（§3.2.2）；§3.2.3 描述「陣營加分權重」的消費端來源——資料合併入 C-01 `MissionDifficultyTable.factionScoreDelta`（owner = C-01），FT-09 不再持有 `MissionFactionScoreWeight.csv`。引用既有常數 `FACTION_NEUTRAL_ID = 0`（`SystemConstants.csv`），FT-09 不新增 SystemConstants 條目。

#### 3.2.1 FactionRouteTable

**檔案位置**：`Assets/Resources/Data/Tables/FactionRouteTable.csv`

**Schema**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `factionID` | int (PK) | 唯一識別碼；**必須 `!= FACTION_NEUTRAL_ID(0)`**（neutral 是保留值，不在此表登記） |
| `name` | string | 陣營顯示名稱（例：「秩序」「混沌」） |
| `description` | string | 陣營描述（UI 用） |

**驗證規則**（DataManager 載入時）：

- `factionID == 0` → `FactionRouteTableValidationException("factionID=0 reserved for neutral")`，跳過該行
- `factionID` 重複 → `FactionRouteTableValidationException("duplicate factionID")`，跳過重複行

**Jam 預設**（單路線）：

| factionID | name | description |
|---|---|---|
| 1 | 秩序 | 守護世界既有秩序的陣營路線 |

> Jam 版僅 1 筆有效記錄；第 2 筆（如 `factionID = 2` / 混沌）schema 預留，Post-Jam 加入即可（前提是 §3.4 / §3.5 的多陣營邏輯全部 ready，Jam 實作仍以單路線為設計目標）。

---

#### 3.2.2 StoryStageTable

**檔案位置**：`Assets/Resources/Data/Tables/StoryStageTable.csv`

**Schema**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `stageID` | int (PK) | 唯一識別碼(單表全域唯一) |
| `factionID` | int (FK → `FactionRouteTable`) | 隸屬陣營；**必須 `!= FACTION_NEUTRAL_ID(0)`** |
| `stageIndex` | int | 該陣營路線中的階段序號（從 `1` 開始連號） |
| `scoreThreshold` | int | 觸發此階段所需的陣營分數門檻（解鎖判定為 `currentScore >= scoreThreshold`，§3.4） |
| `missionID` | int (FK → `MissionTemplate`) | 注入委託板的劇情委託 ID；**該 `MissionTemplate.categoryID` 必須 == `3`（faction_story）** |
| `dialogueKey` | string (FK → `DialogueTable`) | 對話內容鍵（P-02 對話視窗讀取） |

**驗證規則**（DataManager 載入時）：

- `factionID == 0` → `StoryStageTableValidationException("stageID={id}: factionID must be non-neutral")`，跳過
- `factionID` 在 `FactionRouteTable` 找不到 → `StoryStageTableValidationException`，跳過
- 同 `factionID` 內 `stageIndex` 必須從 `1` 開始連號（1, 2, 3, ...）；缺號或重複拋例外，跳過該階段
- 同 `factionID` 內 `scoreThreshold` 必須**嚴格遞增**（`stageIndex=1` < `stageIndex=2` < ...）；違反拋例外
- `missionID` 在 `MissionTemplate` 找不到，或對應的 `categoryID != 3` → `StoryStageTableValidationException`，跳過
- `dialogueKey` 在 `DialogueTable` 找不到 → `Debug.LogWarning`（不跳過階段，runtime fallback 顯示空對話框）

**Jam 預設**（3 個階段，秩序路線）：

| stageID | factionID | stageIndex | scoreThreshold | missionID | dialogueKey |
|---|---|---|---|---|---|
| 1001 | 1 | 1 | 10 | 9001 | `story.order.stage1` |
| 1002 | 1 | 2 | 30 | 9002 | `story.order.stage2` |
| 1003 | 1 | 3 | 60 | 9003 | `story.order.stage3` |

> 範例 threshold（10 / 30 / 60）對應「中前期 B+ 任務 7~10 單後解鎖第一階段」的節奏（依 §3.2.3 權重表，B 任務一單 +5 分）；最終值由 §7 與 playtest 校準。`missionID 9001~9003` 假設由 C-01 Mission Database 在內容階段提供（`categoryID=3`）。

---

#### 3.2.3 陣營加分權重來源（消費 C-01.MissionDifficultyTable）

FT-09 不再獨立持有 `MissionFactionScoreWeight` 表；陣營加分欄位 `factionScoreDelta` 已併入 C-01 `MissionDifficultyTable`（owner = C-01，schema 與 Jam 預設值見 C-01 §3.1）。FT-09 §3.3 透過 `C-01.GetFactionScoreDelta(difficulty) : int` API 查詢，回傳 `>= 0` 的整數。

**Jam 預設**（資料來源 C-01 `MissionDifficultyTable.factionScoreDelta`，呼應原設計「dark 路線靠高難度任務累積」精神）：

| difficulty | factionScoreDelta |
|---|---|
| F | 1 |
| E | 1 |
| D | 2 |
| C | 3 |
| B | 5 |
| A | 8 |
| S | 13 |
| SS | 20 |
| SSS | 30 |

> 設計意圖：權重按難度近似指數遞增，讓 S+ 高難度任務在陣營推進中具備壓倒性貢獻——對應原設計「dark 路線主要透過 S 級任務累積」的精神，同時讓中低難度也能緩慢推進，避免新手玩家完全無進度體感。

**降級觸發**：C-01 `MissionDifficultyTable` 載入失敗（包含 `factionScoreDelta` 行缺失）時，C-01 自身回傳 `0` 並 `LogError`；FT-09 收到全 0 權重後分數無法累積，等同 §3.1.1 的「閘關閉」效果，但 FT-09 本身不額外進入降級狀態（C-01 EC 由 C-01 owner 負責）。

> 調整 `factionScoreDelta` 直接改 C-01 `MissionDifficultyTable.csv`，FT-09 不需重新編譯，亦不需新增 `MissionFactionScoreWeight.csv`。

---

### 3.3 分數累積管線（Score Accrual Pipeline）

#### 3.3.1 事件訂閱

FT-09 在 Bootstrap Step C 訂閱 FT-04 的 `OnMissionResolved` 事件（§3.1.2）：

```csharp
EventBus.Subscribe<OnMissionResolved>(HandleOnMissionResolved);
```

訂閱條件：`_isEnabled == true`。降級時不訂閱（§3.1.3）。

---

#### 3.3.2 HandleOnMissionResolved 流程

當 FT-04 結算完成發布事件時，FT-09 執行：

```
HandleOnMissionResolved(Outcome outcome):
    Step 1: 過濾 - 不計分的任務早退
        IF outcome.missionFactionID == FACTION_NEUTRAL_ID(0):
            return  // neutral 任務不計入任何陣營
        IF outcome.isSuccess == false:
            return  // 失敗不加分（單向加分，§1）
        IF NOT FactionRouteTable.Contains(outcome.missionFactionID):
            Debug.LogWarning("OnMissionResolved with unknown factionID={id}, ignored")
            return  // 防禦：C-01 §5.1 已 fallback 為 neutral，這裡為雙重保險

    Step 2: 計算加分數值
        delta = C-01.GetFactionScoreDelta(outcome.missionDifficulty)   // 取自 MissionDifficultyTable.factionScoreDelta
        IF delta == 0:
            return  // 該難度權重為 0，不變動

    Step 3: 更新陣營分數
        oldScore = _factionScores.GetValueOrDefault(outcome.missionFactionID, 0)
        newScore = oldScore + delta
        _factionScores[outcome.missionFactionID] = newScore

    Step 4: 發布 OnFactionScoreChanged 事件（給 P-02 訂閱）
        EventBus.Publish(new OnFactionScoreChanged(
            factionID: outcome.missionFactionID,
            oldScore: oldScore,
            newScore: newScore,
            delta: delta
        ))

    Step 5: 觸發階段解鎖判定（§3.4）
        CheckStageUnlock(outcome.missionFactionID, oldScore, newScore)

    Step 6: 推送 C-06 陣營閘
        maxScore = GetMaxFactionScore()
        C-06.OnFactionScoreUpdated(maxScore)
```

---

#### 3.3.3 過濾規則總表

| 過濾條件 | 處理 | 原因 |
|---|---|---|
| `outcome.missionFactionID == 0`（neutral） | 早退，不計入 | `FACTION_NEUTRAL_ID` 保留語意；C-01 §3.2 已規範 neutral 任務不計入任何陣營分數 |
| `outcome.isSuccess == false` | 早退，不計入 | 單向加分（§1 / §2）；失敗不扣分 |
| `outcome.missionFactionID` 未在 `FactionRouteTable` 註冊 | 早退並 `LogWarning` | 防禦性檢查；C-01 §5.1 已 fallback 為 neutral，但跨系統契約以雙重保險為準 |
| 該難度 `factionScoreDelta == 0` | 早退（不發 `OnFactionScoreChanged`、不推 C-06） | 分數無變化 → 無需通知下游 |

---

#### 3.3.4 GetMaxFactionScore 演算法

```
GetMaxFactionScore() -> int:
    IF NOT _isEnabled:
        return 0
    IF _factionScores.IsEmpty:
        return 0
    return _factionScores.Values.Max()
```

- Jam 版單路線時，`GetMaxFactionScore()` 即唯一陣營的當前分數
- Post-Jam 多陣營時，自動取最大值，無需修改 C-06 契約

---

#### 3.3.5 推送 C-06 的時機

`C-06.OnFactionScoreUpdated(maxScore)` 在以下時機呼叫：

| 時機 | 觸發點 | 說明 |
|---|---|---|
| 分數成功更新後 | `HandleOnMissionResolved` Step 6 | 每次成功計分都推送 |
| Bootstrap 完成後 | §3.1.2 Step E | 即使全 0 也推一次，保持 C-06 同步 |

**不推送的情況**：

- 過濾規則早退時（neutral / 失敗 / 未知 factionID / delta=0）→ 分數無變化，C-06 已快取的值仍有效
- 降級模式下（`_isEnabled == false`）→ §3.1.3 已規範

---

#### 3.3.6 執行緒安全與重入

- FT-09 假設所有事件 callback 在 Unity 主執行緒上執行
- `HandleOnMissionResolved` 不重入（同一 frame 不會被同事件再次觸發；FT-04 §3 已規範事件序列化發布）
- `_factionScores` 是 `Dictionary<int, int>`，主執行緒直接讀寫，不需 lock

---

### 3.4 階段解鎖判定（Stage Unlock Detection）

#### 3.4.1 _unlockedStageIndices 結構

`_unlockedStageIndices: Dictionary<int, int>`：

- 鍵：`factionID`
- 值：該陣營**最大已解鎖的 stageIndex**（`0` = 尚未解鎖任何階段，`1` = 已解鎖第 1 階段，依此類推）

> 因 §3.2.2 規範同 `factionID` 內 `stageIndex` 嚴格連號從 1 起 + `scoreThreshold` 嚴格遞增，單一整數即可表達該陣營的解鎖進度，不需 `HashSet<stageID>` 之類的集合。

---

#### 3.4.2 CheckStageUnlock 流程

由 §3.3.2 Step 5 呼叫：

```
CheckStageUnlock(int factionID, int oldScore, int newScore):
    Step 1: 取得該陣營的階段清單（依 stageIndex 升序）
        stages = StoryStageTable.GetByFactionID(factionID)
                              .OrderBy(s => s.stageIndex)
        IF stages.IsEmpty:
            return  // 該陣營無階段

    Step 2: 取得當前已解鎖進度
        currentMaxIndex = _unlockedStageIndices.GetValueOrDefault(factionID, 0)

    Step 3: 線性掃過未解鎖的階段（stageIndex > currentMaxIndex）
        FOR each stage IN stages WHERE stage.stageIndex > currentMaxIndex:
            IF newScore < stage.scoreThreshold:
                break  // threshold 嚴格遞增，後續階段必定未達標

            // 此階段達標 → 解鎖
            _unlockedStageIndices[factionID] = stage.stageIndex
            _pendingDialogueStages.Enqueue(stage.stageID)
            EventBus.Publish(new OnFactionStoryStageUnlocked(
                stageID:      stage.stageID,
                factionID:   factionID,
                stageIndex:   stage.stageIndex,
                missionID:   stage.missionID,
                dialogueKey: stage.dialogueKey
            ))
```

---

#### 3.4.3 同 frame 多階段解鎖

當單次分數變動跨越多個階段 threshold 時（例如 SSS 任務一單 +30 分跨過 `stageIndex 2 / 3` 兩階段）：

- §3.4.2 Step 3 線性掃過所有達標階段，**按 `stageIndex` 升序逐個解鎖**
- `_pendingDialogueStages` 是 FIFO Queue，保證玩家在 P-02 對話視窗看到的順序為 `stageIndex 1 → 2 → 3`
- `OnFactionStoryStageUnlocked` 事件**逐個發布**（不批次合併），P-02 端訂閱者依事件抵達順序處理

> 設計動機：避免「批次事件」需要訂閱者自行排序的複雜性；逐個事件 + FIFO Queue 提供天然的順序保證。

---

#### 3.4.4 不重複解鎖

由於 §3.4.2 Step 3 只掃 `stageIndex > currentMaxIndex` 的階段，已解鎖的階段不會被重複觸發，即使分數因 Bootstrap 還原 / 跨 frame 累積等原因再次「跨越」其 threshold。

> 反證場景：Jam 版單向加分，分數不會倒退；但若 Post-Jam 引入「成敗對稱」或「跨陣營對抗」（負分），需重新檢視此契約——§5 邊緣案例提醒。

---

#### 3.4.5 _pendingDialogueStages 入隊規則

| 入隊時機 | 觸發點 |
|---|---|
| 階段解鎖 | §3.4.2 Step 3 |
| Bootstrap 補發 | §3.1.2 Step F（從 SaveData 還原） |

**出隊時機**：玩家於對話視窗按確認 → P-02 呼叫 `ConfirmDialogue(stageID)`（§3.5 詳述）。

**順序保證**：FIFO Queue；同 frame 解鎖多階段時依 `stageIndex` 升序入隊。

---

#### 3.4.6 OnFactionStoryStageUnlocked 事件 payload

事件 schema（C# record/struct，§3.7 正式定義）：

```csharp
[Serializable]
public readonly struct OnFactionStoryStageUnlocked
{
    public readonly int    stageID;
    public readonly int    factionID;
    public readonly int    stageIndex;
    public readonly int    missionID;
    public readonly string dialogueKey;

    public OnFactionStoryStageUnlocked(int stageID, int factionID, int stageIndex, int missionID, string dialogueKey)
    {
        this.stageID = stageID;
        this.factionID = factionID;
        this.stageIndex = stageIndex;
        this.missionID = missionID;
        this.dialogueKey = dialogueKey;
    }
}
```

**訂閱者**：

| 訂閱者 | 用途 |
|---|---|
| **P-02 Main UI** | 開啟對話視窗，讀 `dialogueKey` 顯示文本 |
| **P-03 Notification System**（可選） | 推播桌面通知「新劇情解鎖」 **【→Log API待更新】** |
| **FT-09 自身** | 不訂閱（解鎖邏輯由 §3.4.2 同步處理，無需事件回呼） |

> P-02 / P-03 訂閱契約於 §6 反向依賴清單登記。

---

### 3.5 雙軌呈現流程（Dual-Track Presentation）

#### 3.5.1 雙軌定義

當階段解鎖（§3.4）後，劇情以**對話軌**與**委託軌**雙軌呈現，**順序強制為對話 → 委託**：

| 軌道 | 觸發 | 載體 | 玩家動作 |
|---|---|---|---|
| **對話軌**（Dialogue Track） | `OnFactionStoryStageUnlocked` 事件 | P-02 對話視窗（讀 `dialogueKey` → DialogueTable） | 看完點「確認」 |
| **委託軌**（Mission Track） | 玩家確認對話後 | 委託板新增一筆 `categoryID = 3` 任務（透過 FT-02 `InjectStaticMission`） | 接單 → 派遣 → FT-04 結算 |

> 設計動機：對話軌負責「世界回應 / 敘事張力」，委託軌負責「玩法閉環」（玩家實際派冒險者執行）；強制對話先於委託，確保玩家先讀劇情、再做派遣決策——避免劇情委託混在常規委託池裡被忽略。

---

#### 3.5.2 ConfirmDialogue API

P-02 對話視窗按下確認時，呼叫 FT-09 的 `ConfirmDialogue(int stageID)`：

```csharp
public enum ConfirmDialogueResult
{
    OK,
    STORY_SYSTEM_DISABLED,    // 系統降級
    INVALID_STAGE_ID,          // stageID 不在 _pendingDialogueStages 隊首
    INJECT_FAILED             // FT-02.InjectStaticMission 回傳失敗（極罕見）
}

public ConfirmDialogueResult ConfirmDialogue(int stageID);
```

**內部流程**：

```
ConfirmDialogue(int stageID) -> ConfirmDialogueResult:
    Step 1: 啟用檢查
        IF NOT _isEnabled:
            return STORY_SYSTEM_DISABLED

    Step 2: 隊首匹配檢查
        IF _pendingDialogueStages.IsEmpty:
            Debug.LogWarning("ConfirmDialogue called but queue empty, stageID={id}")
            return INVALID_STAGE_ID
        IF _pendingDialogueStages.Peek() != stageID:
            Debug.LogWarning("ConfirmDialogue out-of-order: expected={expected}, got={got}")
            return INVALID_STAGE_ID

    Step 3: 取階段資料
        stage = StoryStageTable.Get(stageID)
        // stage 必存在（§3.4.2 入隊時已驗證）；若資料表 runtime 變動則例外

    Step 4: 注入委託板
        result = FT-02.InjectStaticMission(stage.missionID)
        IF result != Inject.OK:
            Debug.LogError("InjectStaticMission failed for missionID={id}, result={r}")
            return INJECT_FAILED
            // 注意：此時不出隊，玩家可重試（極罕見路徑）

    Step 5: 出隊
        _pendingDialogueStages.Dequeue()

    Step 6: 發布確認事件（給 P-02 收尾、P-03 可選 toast）
        EventBus.Publish(new OnFactionStoryDialogueConfirmed(
            stageID:    stageID,
            factionID:  stage.factionID,
            missionID:  stage.missionID
        ))

    return OK
```

> Step 2 的「隊首匹配」是嚴格順序契約：P-02 必須**依事件順序**呼叫 `ConfirmDialogue`——同 frame 多階段解鎖（§3.4.3）時，P-02 應依 FIFO 順序逐個彈窗、玩家逐個確認，不允許跳號確認。

---

#### 3.5.3 FT-02 對外契約：InjectStaticMission（新增 API）

FT-09 要求 FT-02 提供以下新 API（FT-02 GDD §3 將補章節，反向依賴於 §6 登記）：

```csharp
public enum InjectStaticMissionResult
{
    OK,
    UNKNOWN_MISSION_ID,        // C-01.GetTemplate 回傳 null
    WRONG_CATEGORY,            // template.categoryID != 3
    ALREADY_ON_BOARD,          // 同一 missionID 已在靜態池或進行中（idempotency）
    BOARD_DISABLED             // FT-02 停用或委託板暫停
}

public InjectStaticMissionResult InjectStaticMission(int missionID);
```

**FT-02 端職責**（FT-09 不實作；FT-02 §3.9 已補完整規格）：

1. 驗證 `missionID` 存在於 `MissionTemplate`，且 `categoryID == 3`
2. 維護 `_regularMissionPool` 與 `_staticMissionPool` 雙池（runtime；持久化於 SaveData；FT-02 §3.9.1）
3. 將 `missionID` 加入 `_staticMissionPool`，並發布 `OnCommissionPosted(missionID, source=Static)`（FT-02 §3.9.5）給 P-02 訂閱以更新委託板 UI；`source=Static` 讓 P-02 可呈現視覺差異（紅框等）
4. 派遣（`Dispatch`）成功後，**從 `_staticMissionPool` 移除該 `missionID`**（§3.5 step 10；靜態池優先）
5. 派遣後續流程（ActiveMission / `OnMissionCompleted` / FT-04 結算）走 FT-02 既有路徑，與常規任務一致——FT-04 結算時 `Outcome.missionFactionID` 由 `MissionTemplate.factionID` 帶入，**FT-09 §3.3.2 不需特殊處理**

**冪等性**：

- 同一 `missionID` 重複注入 → 回傳 `ALREADY_ON_BOARD`，不重複加入 `_staticMissionPool`
- FT-09 端因階段 ID 出隊保護（§3.5.2 Step 5），正常路徑不會重複呼叫

> **不在 FT-02 InjectStaticMission 範疇**：靜態委託的「過期 / 自動取消」邏輯。Jam 版靜態委託**永久停留在板上直到玩家派遣**，不過期；若 Post-Jam 需要過期機制，由 FT-02 自行擴充（FT-09 不關心）。

---

#### 3.5.4 失敗路徑與重試

| 失敗點 | FT-09 行為 | 玩家可見效果 |
|---|---|---|
| `_isEnabled == false`（runtime 不會發生，因為 P-02 不該收到事件） | 回傳 `STORY_SYSTEM_DISABLED` | P-02 應顯示錯誤訊息或隱藏對話視窗 |
| 隊首不匹配（P-02 順序錯誤） | `LogWarning` + 回傳 `INVALID_STAGE_ID`，**不出隊** | P-02 應重新依 `_pendingDialogueStages` 順序呼叫 |
| `FT-02.InjectStaticMission` 失敗（資料異常） | `LogError` + 回傳 `INJECT_FAILED`，**不出隊**（保留可重試狀態） | P-02 顯示錯誤；玩家下次點確認可重試 |
| 玩家直接關閉遊戲不確認 | 對話階段留在 `_pendingDialogueStages`；下次 Bootstrap §3.1.2 Step F 補發事件 | 重啟後對話視窗重新彈出 |

> 設計動機：所有失敗路徑都**不出隊**——`_pendingDialogueStages` 是「玩家尚未確認的對話階段」唯一真實來源，出隊條件僅一個（成功確認 → 委託成功注入板）。

---

#### 3.5.5 雙軌的時序保證

```
T0  分數累積 → 跨越 threshold
T1  §3.4.2 解鎖階段：
      - _unlockedStageIndices[factionID] 更新
      - _pendingDialogueStages.Enqueue(stageID)
      - EventBus.Publish(OnFactionStoryStageUnlocked)
T2  P-02 收到事件 → 加入對話佇列、視時機彈出對話視窗
T3  玩家點確認 → P-02 呼叫 FT-09.ConfirmDialogue(stageID)
T4  FT-09 呼叫 FT-02.InjectStaticMission(missionID)
T5  FT-02 將 missionID 加入 _staticMissionPool、發布 OnCommissionPosted(missionID, source=Static)
T6  P-02 委託板 UI 重新整理 → 玩家可見劇情委託（Static 可呈現視覺差異）
T7  玩家在委託板選擇接單 → 進入既有派遣流程（FT-02.Dispatch）
T8  派遣完成 → FT-02 從 _staticMissionPool 移除 missionID
T9  完成 / 結算（FT-04）→ §3.6 階段推進規則處理結局
```

- **T1 → T2 的延遲**容許：P-02 可選擇「立即彈窗」或「待玩家結束當前 UI 流程後彈窗」（如戰鬥結算動畫播完）；FT-09 不規範彈窗時機，僅保證事件順序
- **T6 → T7 的時間長短**完全由玩家節奏決定：劇情委託在板上不過期（§3.5.3 註）
- **T7 → T8**：派遣後委託即從靜態池消失（不可被其他冒險者重複接單）

---

#### 3.5.6 P-02 訂閱契約（資訊性）

P-02 須訂閱兩個事件以實作雙軌呈現（反向依賴於 §6 登記）：

| 事件 | 用途 | 來源 |
|---|---|---|
| `OnFactionStoryStageUnlocked` | 加入對話佇列、彈出對話視窗 | FT-09 §3.4.2 |
| `OnCommissionPosted(missionID, source=Static)` | 委託板 UI 顯示劇情委託；`source=Static` 可呈現視覺差異 | FT-02 §3.9.5 |

並提供以下呼叫：

| 呼叫 | 目標 | 時機 |
|---|---|---|
| `FT-09.ConfirmDialogue(stageID)` | FT-09 | 玩家點對話視窗確認 |
| `FT-02.Dispatch(instanceID, missionID, source=PlayerManual)` | FT-02 | 玩家在委託板選擇劇情委託並指派冒險者 |

> P-02 GDD 待設計時，本節作為對話視窗 / 委託板 UI 的功能性需求依據。

---

### 3.6 階段推進規則（Stage Progression Rules）

#### 3.6.1 推進語意（Progression Semantics）

**階段推進的真實時間點**：FT-09 將階段視為「已推進」的時間點是 **§3.4.2 解鎖時**（`_unlockedStageIndices[factionID]` 寫入），而非「劇情委託結算後」——一旦解鎖，階段不可回退。對應之後的劇情委託結算結果（成功 / 失敗 / 死亡）**不影響**：

- `_unlockedStageIndices[factionID]` 的值
- 下一階段的解鎖判定（仍由分數累積驅動）

> 設計動機：玩家派遣劇情委託失敗甚至冒險者死亡，是「公會編年史」的一部分，不應用「重做階段」懲罰玩家節奏選擇（呼應 §2 設計原則 4「單向加分對應累積感」）。劇情委託結算結果本身的張力由 §3.6.2 專屬事件承擔，文本層由 P-02 自行決定如何呈現。

---

#### 3.6.2 OnFactionStoryStageResolved 事件

FT-09 訂閱 FT-04 `OnMissionResolved`（§3.3.1 既有訂閱），在 `HandleOnMissionResolved` 後段對劇情委託額外處理：

```
HandleOnMissionResolved(Outcome outcome) [在 §3.3.2 Step 6 後追加]:
    Step 7: 識別劇情委託結算
        template = C-01.GetTemplate(outcome.missionID)
        IF template == null OR template.categoryID != 3:
            return  // 非劇情委託，已處理完畢

    Step 8: 反查階段
        stage = StoryStageTable.GetByMissionID(outcome.missionID)
        IF stage == null:
            Debug.LogWarning("OnMissionResolved with categoryID=3 but no matching stage, missionID={id}")
            return

    Step 9: 發布劇情階段結算事件
        EventBus.Publish(new OnFactionStoryStageResolved(
            stageID:     stage.stageID,
            factionID:   stage.factionID,
            stageIndex:  stage.stageIndex,
            missionID:   outcome.missionID,
            isSuccess:   outcome.isSuccess,
            isDead:      outcome.isDead   // FT-04 結局欄位；§3.3.2 不消費，§3.6 才消費
        ))

    Step 10: 路線完結判定（§3.6.3）
        CheckRouteCompletion(stage.factionID, outcome.isSuccess)
```

**Step 7 / 8 的順序**：在 §3.3.2 Step 6 之後執行，確保「分數加成 + C-06 推送」已完成。劇情委託本身也計分（依 §3.3.3 過濾規則總表，只要 `factionID != 0` 且 `isSuccess == true` 就計入）——這是設計上的容許行為，§7 Tuning Knobs 會說明如何避免雙重計分過量（`scoreThreshold` 規劃時將劇情委託計分計入緩衝）。

**事件 schema**：

```csharp
[Serializable]
public readonly struct OnFactionStoryStageResolved
{
    public readonly int  stageID;
    public readonly int  factionID;
    public readonly int  stageIndex;
    public readonly int  missionID;
    public readonly bool isSuccess;
    public readonly bool isDead;

    public OnFactionStoryStageResolved(int stageID, int factionID, int stageIndex, int missionID, bool isSuccess, bool isDead)
    {
        this.stageID = stageID;
        this.factionID = factionID;
        this.stageIndex = stageIndex;
        this.missionID = missionID;
        this.isSuccess = isSuccess;
        this.isDead = isDead;
    }
}
```

**訂閱者**：

| 訂閱者 | 用途 |
|---|---|
| **P-02 Main UI** | 結算後播放階段 epilogue 對話 / 顯示劇情結算面板（讀 `dialogueKey` 的 epilogue 變體 ── 文本由 dialogueKey 在 DialogueTable 中分組，FT-09 不規範） |
| **P-03 Notification System**（可選） | 推播桌面通知「劇情委託結算」 **【→Log API待更新】** |

> P-02 / P-03 訂閱契約於 §6 反向依賴清單登記。

---

#### 3.6.3 路線完結判定（Route Completion）

**最後階段的判定**：階段為該陣營「最後一個階段」當且僅當 `stage.stageIndex == StoryStageTable.GetByFactionID(factionID).Count`（§3.2.2 強制 `stageIndex` 從 1 連號，故 `Count` 即最大 index）。

```
CheckRouteCompletion(int factionID, bool finalIsSuccess):
    stages = StoryStageTable.GetByFactionID(factionID)
    maxIndex = stages.Count
    currentIndex = _unlockedStageIndices.GetValueOrDefault(factionID, 0)

    IF currentIndex < maxIndex:
        return  // 還有階段未解鎖

    // 確認最後階段的劇情委託「已結算過」——避免重複觸發
    IF _routeCompletedFlags.Contains(factionID):
        return  // 已發布過，避免重複

    _routeCompletedFlags.Add(factionID)
    EventBus.Publish(new OnFactionRouteCompleted(
        factionID:       factionID,
        finalStageID:    stages.Last().stageID,
        finalIsSuccess:  finalIsSuccess,
        totalStages:     maxIndex
    ))
```

> 由於 `CheckRouteCompletion(factionID, finalIsSuccess)` 在 §3.6.2 Step 10 呼叫，呼叫時 `outcome` 仍在範疇——`finalIsSuccess` 直接傳入 `outcome.isSuccess`。

---

#### 3.6.4 _routeCompletedFlags 結構

`_routeCompletedFlags: HashSet<int>`：

- 元素：已發布過 `OnFactionRouteCompleted` 的 `factionID`
- 持久化於 SaveData（§3.7.4）
- Bootstrap 還原時直接還原（不重發 `OnFactionRouteCompleted`，與 `_pendingDialogueStages` 不同——已完結事件視為一次性訊號，重啟不重播）

> 設計動機：路線完結是「終局信號」，重啟時若重發會干擾玩家（每次開啟遊戲都跳「劇情完結」notification）；對話階段則因玩家可能未確認而需要補發（§3.1.2 Step F）。兩者語意不同。

---

#### 3.6.5 OnFactionRouteCompleted 事件

```csharp
[Serializable]
public readonly struct OnFactionRouteCompleted
{
    public readonly int  factionID;
    public readonly int  finalStageID;
    public readonly bool finalIsSuccess;
    public readonly int  totalStages;

    public OnFactionRouteCompleted(int factionID, int finalStageID, bool finalIsSuccess, int totalStages)
    {
        this.factionID = factionID;
        this.finalStageID = finalStageID;
        this.finalIsSuccess = finalIsSuccess;
        this.totalStages = totalStages;
    }
}
```

**訂閱者**：

| 訂閱者 | 用途 |
|---|---|
| **P-02 Main UI** | 顯示「路線完結」UI（如成就視窗、回顧介面） |
| **P-03 Notification System**（可選） | 推播桌面通知「陣營路線完結」 **【→Log API待更新】** |
| **C-06 World Danger System**（可選） | Post-Jam 可訂閱以解鎖最終難度層級；Jam 版不訂閱 |

> Jam 版 P-02 / P-03 對此事件的呈現可極簡（一個對話框 + 文本「公會的傳奇就此寫成」即可），不規範 UI 細節。

---

#### 3.6.6 玩家行為自由度（Free Pacing）

FT-09 **不干涉**玩家對劇情委託的派遣節奏：

| 玩家行為 | FT-09 反應 |
|---|---|
| 玩家不派遣劇情委託，繼續做常規任務 | 分數繼續累積；下一階段 threshold 達標時仍解鎖、入隊、發 `OnFactionStoryStageUnlocked` |
| 玩家在劇情委託懸而未派遣時跨越下一階段 threshold | 兩個階段的劇情委託**並存於 `_staticMissionPool`**（FT-02 端兩個 `categoryID=3` 任務同時在板）、`_pendingDialogueStages` 內順序排列 |
| 玩家直接無視所有劇情委託，繼續累積分數至最大 | 所有階段解鎖、所有對話進入 `_pendingDialogueStages`、所有劇情委託在板；玩家任何時刻可接、可不接 |
| 玩家派遣劇情委託但失敗 / 死亡 | 走 §3.6.2 標準流程；階段不重置；不再次入隊 |

> 設計動機：呼應 §2「被動驅動，主動回應」——玩家節奏完全自定，FT-09 僅做「累積 → 解鎖 → 通知」，不主動催促或阻擋。

---

#### 3.6.7 階段計分回流（Score Re-accrual from Story Missions）

劇情委託本身是 `factionID != 0` 的任務，結算成功時透過 §3.3.2 也會加分到對應陣營。這意味著：

- 階段 N 解鎖後，玩家派遣階段 N 的劇情委託成功 → 該陣營 `currentScore` 額外 +`C-01.GetFactionScoreDelta(difficulty)`
- 此額外分數可能直接跨越下一階段 threshold → 立即解鎖階段 N+1
- 行為一致 §3.4.2 流程，無特殊處理

**設計師責任**（§7 Tuning Knobs 規範）：規劃 `scoreThreshold` 時須將「劇情委託本身的計分」納入考量，預留至少一個**典型常規任務難度的權重**作為 buffer，避免「玩家剛解鎖 N，N 委託一結算就立刻彈出 N+1」的緊湊節奏（除非那是設計目標）。

> 範例：若階段 N = `scoreThreshold 30`、階段 N+1 = `scoreThreshold 60`，且階段 N 的 `missionID` 是 A 級（+8）→ 結算後仍距 N+1 約 22 分，需要更多常規任務累積；間距 ≥ 任意一個任務難度的權重值即可。

---

### 3.7 對外 API、事件契約與 Runtime 狀態（API Surface, Event Contracts & Runtime State）

本節彙整 FT-09 對外的所有 API、發布 / 訂閱的事件、以及內部 runtime 狀態欄位的正式契約，作為 §4 公式 / §5 邊緣案例 / §6 依賴 / §8 驗收標準的引用基準。

---

#### 3.7.1 對外 API（公開方法）

| API | 簽名 | 降級時行為 | 引用章節 |
|---|---|---|---|
| 啟用查詢 | `IsFactionStoryEnabled() : bool` | return `false` | §3.1 |
| 取陣營分數 | `GetCurrentFactionScore(int factionID) : int` | return `0` | §3.3 |
| 取陣營最高分 | `GetMaxFactionScore() : int` | return `0` | §3.3.4 |
| 取已解鎖階段 index | `GetUnlockedStageIndex(int factionID) : int` | return `-1` | §3.4 |
| 確認對話 | `ConfirmDialogue(int stageID) : ConfirmDialogueResult` | return `STORY_SYSTEM_DISABLED` | §3.5.2 |
| 取待確認階段數 | `GetPendingDialogueCount() : int` | return `0` | §3.5 |
| 路線是否完結 | `IsRouteCompleted(int factionID) : bool` | return `false` | §3.6 |

> 上述為唯讀查詢 + 唯一可變動操作 `ConfirmDialogue`。FT-09 不對外暴露分數修改 / 階段重置 / 強制觸發等 API（Jam 版範疇）。

---

#### 3.7.2 發布的事件（Outbound Events）

| 事件 | 發布時機 | Payload | 訂閱者 |
|---|---|---|---|
| `OnFactionScoreChanged` | §3.3.2 Step 4 | `(int factionID, int oldScore, int newScore, int delta)` | P-02（可選） |
| `OnFactionStoryStageUnlocked` | §3.4.2 Step 3 / §3.1.2 Step F | `(int stageID, int factionID, int stageIndex, int missionID, string dialogueKey)` | P-02、P-03（可選） **【→Log API待更新】** |
| `OnFactionStoryDialogueConfirmed` | §3.5.2 Step 6 | `(int stageID, int factionID, int missionID)` | P-02、P-03（可選） **【→Log API待更新】** |
| `OnFactionStoryStageResolved` | §3.6.2 Step 9 | `(int stageID, int factionID, int stageIndex, int missionID, bool isSuccess, bool isDead)` | P-02、P-03（可選） **【→Log API待更新】** |
| `OnFactionRouteCompleted` | §3.6.3 | `(int factionID, int finalStageID, bool finalIsSuccess, int totalStages)` | P-02、P-03（可選） **【→Log API待更新】**、C-06（Post-Jam） |

**事件發布順序保證**：

- 同一 `OnMissionResolved` 觸發鏈內，順序為 `OnFactionScoreChanged` → `OnFactionStoryStageUnlocked`（若有解鎖）→ `OnFactionStoryStageResolved`（若該結算為劇情委託）→ `OnFactionRouteCompleted`（若該結算為最後階段）
- 跨 frame 的事件依 EventBus 序列化保證（FT-04 §3 約束）

---

#### 3.7.3 訂閱的事件（Inbound Events）

| 事件 | 來源 | 處理 | 訂閱條件 |
|---|---|---|---|
| `OnMissionResolved` | FT-04 | `HandleOnMissionResolved`（§3.3.2 + §3.6.2） | `_isEnabled == true` |

**主動推送（非事件）**：

| 呼叫 | 目標 | 時機 | 引用章節 |
|---|---|---|---|
| `C-06.OnFactionScoreUpdated(int newMaxScore)` | C-06 | Bootstrap Step E + 每次 §3.3.2 Step 6 | §3.3.5 |
| `FT-02.InjectStaticMission(int missionID)` | FT-02 | §3.5.2 Step 4 | §3.5.3 |

> FT-09 不訂閱任何 P-02 / P-03 / FT-02 事件——這些下游系統純粹消費 FT-09 的輸出，無回流訊號。

---

#### 3.7.4 Runtime 狀態欄位

| 欄位 | 型別 | 用途 | 持久化 | 引用章節 |
|---|---|---|---|---|
| `_isEnabled` | `bool` | 啟用 flag | 否（Bootstrap 重算） | §3.1 |
| `_factionScores` | `Dictionary<int, int>` | 各陣營當前累積分數 | 是 | §3.3 |
| `_unlockedStageIndices` | `Dictionary<int, int>` | 各陣營已解鎖最大 stageIndex | 是 | §3.4.1 |
| `_pendingDialogueStages` | `Queue<int>` | 待玩家確認的對話 stageID（FIFO） | 是 | §3.4.5 |
| `_routeCompletedFlags` | `HashSet<int>` | 已完結路線 factionID | 是 | §3.6.4 |

**SaveData 對應欄位**(全部使用 List 系列容器,對齊 Unity JsonUtility 限制):

```csharp
[Serializable]
public struct FactionScoreEntry { public int factionID; public int score; }

[Serializable]
public struct StageIndexEntry { public int factionID; public int stageIndex; }

[Serializable]
public sealed class FactionStorySaveData
{
    public List<FactionScoreEntry> factionScores;        // _factionScores 序列化為 List
    public List<StageIndexEntry>   unlockedStageIndices;  // _unlockedStageIndices 序列化為 List
    public List<int>               pendingDialogueStages; // _pendingDialogueStages 序列化為 List
    public List<int>               routeCompletedFlags;   // _routeCompletedFlags 序列化為 List
}
```

> Runtime 狀態欄位仍用 `Dictionary` / `Queue` / `HashSet`(高效查詢);Save/Load 時序列化為上述 List/Entry 結構,反序列化後重建為 runtime 容器。Unity JsonUtility 不支援 Dictionary / Queue / HashSet 直接序列化,故引入 entry struct 中介層,對齊 FT-10 §3.6.1。

---

#### 3.7.5 讀取資料表的查詢路徑

| 查詢 | 路徑 | 來源 |
|---|---|---|
| 取陣營路線 | `DataManager.Get<FactionRouteData>().GetByID(factionID)` | F-01 §6 / §3.2.1 |
| 取陣營下所有階段 | `DataManager.Get<StoryStageData>().GetByFactionID(factionID)` | F-01 §6 / §3.2.2 |
| 依 missionID 反查階段 | `DataManager.Get<StoryStageData>().GetByMissionID(missionID)` | F-01 §6 / §3.6.2 Step 8 |
| 取難度權重 | `C-01.GetFactionScoreDelta(difficulty)` | C-01 §3 / FT-09 §3.2.3（owner 已轉移至 C-01 `MissionDifficultyTable`） |
| 取任務模板（驗證 categoryID） | `C-01.GetTemplate(missionID)` | C-01 §3 / §3.6.2 Step 7 |

> `GetByMissionID` 需 F-01 / StoryStageData 提供索引；若採線性掃描，Jam 版 3~5 階段規模下無效能問題。

---

#### 3.7.6 執行緒模型

- 所有 API、事件 callback、推送均在 **Unity 主執行緒**
- FT-09 不啟動任何背景執行緒 / async task
- 不需要 lock；`Dictionary` / `Queue` / `HashSet` 直接讀寫

---

## 4. 公式（Formulas）

> 本章彙整 FT-09 的 5 條核心公式。F-1 ~ F-3 為 runtime 執行公式；F-4 / F-5 為設計師於資料表校準時使用的工具公式（不在 runtime 執行，但決定 §3.2.2 / §3.2.3 數值合理性）。完整公式內容（含正式形式、變數定義、值域、範例計算）詳見 [附錄 A：公式詳述](#appendix-a-formulas)。

### 4.1 公式索引

| 編號 | 名稱 | 類別 | 用途 | 詳述位置 |
|---|---|---|---|---|
| F-1 | 陣營分數累積 | runtime | §3.3.2 Step 3 計分核心 | [附錄 A.1](#appendix-a-1) |
| F-2 | 陣營最高分 | runtime | §3.3.4 推送 C-06 | [附錄 A.2](#appendix-a-2) |
| F-3 | 階段解鎖判定 | runtime | §3.4.2 線性掃描提早終止 | [附錄 A.3](#appendix-a-3) |
| F-4 | 階段間距 buffer 計算 | offline 工具 | 設計師校準 `scoreThreshold` | [附錄 A.4](#appendix-a-4) |
| F-5 | 解鎖節奏推估 | offline 工具 | playtest 後校準權重 | [附錄 A.5](#appendix-a-5) |

### 4.2 公式相依關係

- `F-1` → `F-3`：分數累積後觸發解鎖判定（§3.3.2 Step 5）
- `F-1` → `F-2`：分數變動後計算 max 推送 C-06（§3.3.2 Step 6）
- `F-4` / `F-5`：純設計工具，不影響 runtime；F-4 校準 buffer 安全性、F-5 校準解鎖節奏

### 4.3 值域速查

| 變數 | 值域 |
|---|---|
| `oldScore[factionID]` / `newScore[factionID]` / `currentScore` | `[0, ∞)`（單向加分，永不變負） |
| `delta(difficulty)` | `[0, 30]`（Jam 預設範圍 F=1 ~ SSS=30） |
| `stage.scoreThreshold` | `[1, ∞)`（必為正整數，同 factionID 內嚴格遞增） |
| `stage.stageIndex` | `[1, totalStages]`（從 1 連號） |
| `buffer` (F-4) | `(-∞, ∞)`；`SafeBuffer ⟺ buffer >= delta_min_typical` |
| `E[delta_per_mission]` (F-5) | `[0, 30]` |

詳細變數定義與範例計算詳見 [附錄 A：公式詳述](#appendix-a-formulas)。

---

## 5. 邊緣案例（Edge Cases）

> 本章採**索引格式**：每條 EC 列出觸發摘要、行為摘要、規範源章節、詳述附錄連結。完整描述（觸發 / 行為 / 玩家可見 / 規範細節）詳見 [附錄 B：邊緣案例詳述](#appendix-b-edge-cases)。每條 EC 皆遵守 `design-docs.md`「explicitly state what happens」原則——不留「應優雅處理」這類空話。

### 5.1 EC 索引表

| EC | 主題 | 觸發摘要 | 行為摘要 | 規範源 | 詳述位置 |
|---|---|---|---|---|---|
| EC-1 | CSV 載入失敗 | FactionRouteTable 全 neutral / StoryStageTable 空 / C-01 MissionDifficultyTable 缺難度行 | `_isEnabled = false`，降級 | §3.1.1 / §3.1.3 / §3.2 | [附錄 B.1](#appendix-b-1) |
| EC-2 | 未知 factionID | OnMissionResolved 帶不存在的 factionID | LogWarning + 早退，不計分 | §3.3.2 / §3.3.3 | [附錄 B.2](#appendix-b-2) |
| EC-3 | 未確認對話關遊戲 | `_pendingDialogueStages` 含未確認 stageID，玩家退出 | SaveData 持久化，Bootstrap Step F 補發事件 | §3.1.2 / §3.5.4 | [附錄 B.3](#appendix-b-3) |
| EC-4 | 同 frame 多階段解鎖 | 單次 OnMissionResolved 跨越 ≥2 階段 threshold | 線性掃描升序逐個解鎖、入隊、發事件 | §3.4.3 / §3.7.2 | [附錄 B.4](#appendix-b-4) |
| EC-5 | 劇情委託失敗 / 死亡 | 劇情委託結算 isSuccess=false 或 isDead=true | `_unlockedStageIndices` 不回退；發 StageResolved 含 isSuccess/isDead | §3.6.1 / §3.6.2 | [附錄 B.5](#appendix-b-5) |
| EC-6 | 玩家忽略劇情委託 | 持續累積分數，不確認任何對話視窗 | `_pendingDialogueStages` 持續入隊，核心循環不受影響 | §3.6.6 / §3.5.5 | [附錄 B.6](#appendix-b-6) |
| EC-7 | InjectStaticMission 失敗 | ConfirmDialogue 呼叫 FT-02 回非 OK | LogError + INJECT_FAILED；不出隊，可重試 | §3.5.2 / §3.5.4 | [附錄 B.7](#appendix-b-7) |
| EC-8 | P-02 順序錯誤 | ConfirmDialogue stageID != Queue.Peek() | LogWarning + INVALID_STAGE_ID；不出隊 | §3.5.2 / §3.5.4 | [附錄 B.8](#appendix-b-8) |
| EC-9 | 劇情委託計分跨下階段 | 階段 N 委託成功的 delta 直接使 newScore >= threshold(N+1) | 同 frame 內依序：ScoreChanged → StageUnlocked(N+1) → StageResolved(N) → RouteCompleted? | §3.6.7 / §3.7.2 | [附錄 B.9](#appendix-b-9) |
| EC-10 | SaveData 反序列化異常 | 5 種子情境（整區塊缺失 / 欄位 null / JSON 損毀 / 未知 factionID / stageIndex 超範圍） | 不阻擋 Bootstrap；過濾 stale key；夾擠 stageIndex | §3 未涵蓋（完整展開） | [附錄 B.10](#appendix-b-10) |
| EC-11 | 路線完結後計分 | 已完結路線繼續派該陣營任務 | 分數累積照常，但無新解鎖；RouteCompleted 不重發 | §3 未涵蓋（完整展開） | [附錄 B.11](#appendix-b-11) |
| EC-12 | Bootstrap 時序失準 | DataManager / EventBus 未就緒，事件早於訂閱 | 降級 + LogError；事件遺失視為可接受 | 部分（§3.1.2 假設） | [附錄 B.12](#appendix-b-12) |

### 5.2 EC 覆蓋類型

- **CSV / 資料異常**：EC-1
- **runtime 過濾**：EC-2
- **狀態持久化**：EC-3, EC-10
- **解鎖判定邊界**：EC-4, EC-9
- **流程分支**：EC-5, EC-6, EC-7, EC-8, EC-11
- **跨系統時序**：EC-12

### 5.3 §3 覆蓋程度

- **§3 內嵌規範**（EC-1~9）：每條 EC 對應 §3 既有條目；附錄 B 採「觸發 / 行為 / 玩家可見 / 規範源」短條目格式
- **§3 未涵蓋或部分涵蓋**（EC-10~12）：附錄 B 完整展開「觸發條件 / 系統行為 / 玩家可見 / 規範細節」四欄

完整描述詳見 [附錄 B：邊緣案例詳述](#appendix-b-edge-cases)。

---

## 6. 依賴關係（Dependencies）

> 本章定義 FT-09 與其他系統的雙向依賴契約。**§6.1** 列舉 FT-09 訂閱 / 呼叫的上游系統；**§6.2** 列舉消費 FT-09 事件 / API 的下游系統；**§6.3** 以契約表彙整每條依賴的「本端用什麼 / 對端要提供什麼 / 雙方文件互引位置」；**§6.4** 列出 9 個下游 GDD 須補的反向依賴章節。所有依賴遵循 `design-docs.md`「Dependencies must be bidirectional」規範。

### 6.1 上游依賴（Upstream Dependencies）

FT-09 在 runtime 訂閱 / 呼叫以下系統：

#### 6.1.1 FT-04 Outcome Resolution

| 項目 | 內容 |
|---|---|
| **接觸點** | 訂閱事件 `OnMissionResolved(Outcome)` |
| **消費欄位** | `outcome.missionFactionID`、`outcome.isSuccess`、`outcome.missionDifficulty`、`outcome.missionID`、`outcome.isDead` |
| **本端用途** | (a) §3.3 分數累積（前 4 欄位）；(b) §3.6.2 劇情委託結算識別（`missionID` → `categoryID`、`isDead` 用於 epilogue） |
| **對端契約** | FT-04 §3.1 結算流程、§3 Outcome schema 須包含上述欄位；FT-04 §6 line 274 已登記 |
| **失準行為** | 任一欄位缺失 → §3.3.2 過濾規則早退（EC-2）；事件未發布 → 該次任務不計分（容許行為，無錯誤） |

#### 6.1.2 F-01 Data Manager

| 項目 | 內容 |
|---|---|
| **接觸點** | 呼叫 API `DataManager.Get<FactionRouteData>()`、`Get<StoryStageData>()`；陣營加分權重改透過 C-01 `GetFactionScoreDelta` 消費（不再 Get FT-09 自有的 `MissionFactionScoreWeightData`） |
| **本端用途** | Bootstrap §3.1.2 Step A 一次性載入；runtime 各處讀表（§3.7.5 查詢路徑表） |
| **對端契約** | F-01 須登記 2 個 FT-09 owner Data 型別（`FactionRouteData` / `StoryStageData`）的查詢支援；CSV 載入失敗時回 null（FT-09 自行處理為降級） |
| **失準行為** | F-01 未就緒 → §3.1.1 啟用條件未滿足 → `_isEnabled = false`（EC-12(a)） |

#### 6.1.3 C-01 Mission Database

| 項目 | 內容 |
|---|---|
| **接觸點** | 呼叫 API `C-01.GetTemplate(missionID)` 讀 `MissionTemplate.categoryID` / `factionID`；`C-01.GetFactionScoreDelta(difficulty)` 讀 `MissionDifficultyTable.factionScoreDelta` |
| **本端用途** | §3.6.2 Step 7 識別劇情委託（`categoryID == 3`）；§3.3.2 Step 2 取陣營加分權重 |
| **對端契約** | C-01 §3.1 須維持 `MissionTemplate.categoryID` / `factionID` 與 `MissionDifficultyTable.factionScoreDelta` 欄位；C-01 §6.2 已登記 FT-09 消費 `GetFactionScoreDelta` |
| **失準行為** | `GetTemplate` 回 null → §3.6.2 Step 7 視為非劇情委託，跳過；`GetFactionScoreDelta` 回 0 → 該難度不計分（與 `factionID = 0` 同效，無錯誤） |

#### 6.1.4 FT-02 Mission Dispatch

| 項目 | 內容 |
|---|---|
| **接觸點** | 呼叫 API `FT-02.InjectStaticMission(int missionID) → InjectStaticMissionResult` |
| **本端用途** | §3.5.2 Step 4 將劇情委託 `missionID` 注入委託板靜態池 |
| **對端契約** | FT-02 §3.9 已補完整規格：`InjectStaticMission` API（§3.9.2）+ `_staticMissionPool` runtime 結構（§3.9.1）+ `OnCommissionPosted(missionID, source=Static)` 事件（§3.9.5）+ 派遣後從池移除規則（§3.9.4）；§6.2 已登記 FT-09 為下游 |
| **失準行為** | 回非 `OK` 結果 → §3.5.2 Step 4 不出隊、回 `INJECT_FAILED`，玩家可重試（EC-7） |

#### 6.1.5 SystemConstants

| 項目 | 內容 |
|---|---|
| **接觸點** | 引用常數 `FACTION_NEUTRAL_ID = 0` |
| **本端用途** | §3.2.1 / §3.2.2 驗證規則 + §3.3.2 過濾規則（neutral 任務不計分） |
| **對端契約** | `SystemConstants.csv` 須維持 `FACTION_NEUTRAL_ID` 條目，值 = 0；FT-09 不新增 SystemConstants 條目 |
| **失準行為** | 常數缺失 → 全域 Bootstrap 失敗（屬 SystemConstants 載入器職責，非 FT-09） |

---

### 6.2 下游推送（Downstream Push & Subscribers）

以下系統消費 FT-09 的事件 / API（FT-09 為事件源 / API 提供方）：

#### 6.2.1 C-06 World Danger System（主動推送）

| 項目 | 內容 |
|---|---|
| **接觸點** | FT-09 主動呼叫 `C-06.OnFactionScoreUpdated(int newMaxScore)` |
| **時機** | Bootstrap §3.1.2 Step E + 每次 §3.3.2 Step 6 |
| **對端職責** | C-06 須提供 `OnFactionScoreUpdated` public method，接收後更新 `_cachedMaxFactionScore` 並驅動陣營閘判定（C-06 §3.3 / §4.5） |
| **對端文件** | C-06 §3.3 / §4.5 / §6.3 已定義契約，§6 須登記 FT-09 為推送方 |
| **降級行為** | FT-09 降級時不呼叫；C-06 `_cachedMaxFactionScore` 維持 `0`（C-06 §5 涵蓋此場景） |

#### 6.2.2 P-02 Main UI Framework（待設計）

| 項目 | 內容 |
|---|---|
| **訂閱事件**（5 個） | `OnFactionScoreChanged`（可選）、`OnFactionStoryStageUnlocked`、`OnFactionStoryDialogueConfirmed`、`OnFactionStoryStageResolved`、`OnFactionRouteCompleted` |
| **呼叫 API** | `FT-09.ConfirmDialogue(stageID)` + `FT-02.Dispatch(instanceID, missionID, source=PlayerManual)`（劇情委託派遣） |
| **對端職責** | (a) 對話視窗管理（FIFO 順序彈窗，依 `_pendingDialogueStages`）；(b) 委託板 UI（顯示 `categoryID = 3` 任務的視覺差異）；(c) 結算面板（讀 `OnFactionStoryStageResolved` 播 epilogue）；(d) 路線完結 UI |
| **對端文件** | P-02 GDD **待設計**；§6.4 列為「FT-09 對話視窗 + 委託板 UI 需求依據」 |
| **時序保證** | P-02 須依事件順序呼叫 `ConfirmDialogue`（隊首匹配，§3.5.2 Step 2）；違反回 `INVALID_STAGE_ID`（EC-8） |

#### 6.2.3 P-03 Notification System（待設計，可選訂閱）

| 項目 | 內容 |
|---|---|
| **訂閱事件**（4 個，可選） | `OnFactionStoryStageUnlocked`、`OnFactionStoryDialogueConfirmed`、`OnFactionStoryStageResolved`、`OnFactionRouteCompleted` |
| **對端職責** | 桌面 / 系統通知推播（toast / OS notification） |
| **對端文件** | P-03 GDD **待設計**；§6.4 列為「可選訂閱」 |
| **失準行為** | P-03 未實作 / 未訂閱 → 無玩家可見影響（FT-09 事件廣播模型，無訂閱者不報錯） |

#### 6.2.4 FT-08 Gacha System（Jam 版無 runtime 依賴）

| 項目 | 內容 |
|---|---|
| **接觸點** | **Jam 版無** runtime 事件 / API 連接 |
| **Schema 預留** | FT-08 `StaffGachaPoolTable` 的 5 個預留閘（factionID / minDifficulty 等）為 **Post-Jam 啟用** |
| **對端文件** | FT-08 GDD 須在「設計來源」章節聲明「Jam 版**無 FT-09 runtime 依賴**；schema 預留 5 個閘欄位，Post-Jam 才啟用 FT-09 → FT-08 池閘消費」 |
| **未來契約**（Post-Jam 提示） | Post-Jam 若 FT-08 池閘消費陣營分數，走 `OnFactionScoreChanged` 訂閱模式 |

#### 6.2.5 FT-12 Staff System（Jam 版無 runtime 依賴）

| 項目 | 內容 |
|---|---|
| **接觸點** | **Jam 版無** runtime 事件 / API 連接 |
| **Schema 預留** | FT-12 `StaffTable.factionID` 欄位為 **Post-Jam 啟用** |
| **對端文件** | FT-12 GDD 須在「設計來源」章節聲明「Jam 版**無 FT-09 runtime 依賴**；schema 預留 staff factionID 欄位，Post-Jam 才啟用 FT-09 → FT-12 的職員陣營傾向消費」 |
| **未來契約**（Post-Jam 提示） | Post-Jam 若 FT-12 消費陣營分數,建議走 `OnFactionScoreChanged` 訂閱模式 |

---

### 6.3 雙向依賴契約檢查表

本表彙整 §6.1 + §6.2 為單一檢索視圖，便於審查時逐條驗證雙向引用是否齊全：

| # | 對端系統 | 方向 | 接觸點 | 本端章節 | 對端應提供 | 對端文件位置 |
|---|---------|------|-------|---------|----------|-----------|
| 1 | FT-04 | FT-09 ← FT-04 | 訂閱 `OnMissionResolved(Outcome)` | §3.3.2 / §3.6.2 | Outcome 含 `missionFactionID` / `isSuccess` / `missionDifficulty` / `missionID` / `isDead` | FT-04 §3.1 / §6 line 274 |
| 2 | F-01 | FT-09 → F-01 | 呼叫 `Get<FactionRouteData / StoryStageData>()`（陣營加分權重透過 C-01 消費） | §3.1.2 / §3.7.5 | 2 個 Data 型別查詢 + 載入失敗回 null | F-01 §6 |
| 3 | C-01 | FT-09 → C-01 | 呼叫 `GetTemplate(missionID)` 讀 `categoryID` / `factionID`；`GetFactionScoreDelta(difficulty)` 讀 `MissionDifficultyTable.factionScoreDelta` | §3.6.2 Step 7 / §3.3.2 Step 2 | `MissionTemplate.categoryID` 含 `3 = faction_story` 語意；`MissionDifficultyTable.factionScoreDelta` 9 行齊全 | C-01 §3.1 / §6.2 |
| 4 | FT-02 | FT-09 → FT-02 | 呼叫 `InjectStaticMission(missionID)` | §3.5.2 / §3.5.3 | `InjectStaticMission` API + `_staticMissionPool` + `OnCommissionPosted(source=Static)` 事件 | FT-02 §3.9（✅ 已補） |
| 5 | SystemConstants | FT-09 ← SystemConstants | 引用 `FACTION_NEUTRAL_ID = 0` | §3.2.1 / §3.2.2 / §3.3.2 | `FACTION_NEUTRAL_ID` 條目維持值 = 0 | `SystemConstants.csv` |
| 6 | C-06 | FT-09 → C-06 | 呼叫 `OnFactionScoreUpdated(maxScore)` | §3.1.2 / §3.3.5 | public method + `_cachedMaxFactionScore` + 陣營閘判定 | C-06 §3.3 / §4.5 / §6.3 |
| 7 | P-02 | FT-09 ← P-02 | 5 事件訂閱 + 呼叫 `ConfirmDialogue` | §3.4 / §3.5 / §3.6 / §3.7.2 | 對話視窗 / 委託板 / 結算面板 / 完結 UI；FIFO 順序契約 | P-02 GDD（**待設計**） |
| 8 | P-03 | FT-09 ← P-03 | 4 事件可選訂閱 | §3.7.2 | 通知推播（可選） | P-03 GDD（**待設計**） |
| 9 | FT-08 | 無 runtime 連接 | （Jam 版無） | §1 | StaffGachaPoolTable 5 預留閘 schema + Post-Jam 啟用提示 | FT-08「設計來源」章節 |
| 10 | FT-12 | 無 runtime 連接 | （Jam 版無） | §1 | StaffTable.factionID schema + Post-Jam 啟用提示 | FT-12「設計來源」章節 |

> **檢查方法**：本表 9 條依賴 → 對端文件須在其「設計來源」/「§6 依賴」章節登記 FT-09 為對端方（含本表 # 6 ~ # 9 的訂閱方向）；缺漏項目於 §6.4 列出。

---

### 6.4 反向依賴登記狀態（校驗結論）

> 對齊 `design-docs.md`「Dependencies must be bidirectional」原則。FT-09 的全部反向依賴在各對端 GDD 撰寫階段已完成登記；本表保留作為驗收 checklist。

| # | 對端 GDD | 登記位置 / 內容 | 狀態 |
|---|---------|--------------|------|
| 1 | `[FT-04]` | §6.3 雙向依賴表：FT-09 為 `OnMissionResolved` 訂閱者；payload 含 `isDead` 欄位確認 | ✅ |
| 2 | `[C-01]` | §6.2 下游表：`categoryID=3` 消費者 + `GetFactionScoreDelta` 消費；`StoryStageTable` 命名同步 | ✅ |
| 3 | `[C-06]` | §6.2 下游表：FT-09 為 `OnFactionScoreUpdated` 推送方；§3.1 `_cachedMaxFactionScore` 接收契約 | ✅ |
| 4 | `[F-01]` | §6.2 下游表：`Get<FactionRouteData>` / `Get<StoryStageData>` 查詢路徑 | ✅ |
| 5 | `[FT-02]` | §3.9 + §6：`InjectStaticMission` API + `_regularMissionPool` / `_staticMissionPool` 雙池 + `OnCommissionPosted(source=Static)` 事件 + 派遣後從池移除 | ✅ |
| 6 | `[FT-08]` | 「設計來源」章節：Jam 版**無 FT-09 runtime 依賴**聲明 + StaffGachaPoolTable 5 個預留閘 Post-Jam 啟用路徑 | ✅ |
| 6b | `[FT-12]` | 「設計來源」章節：Jam 版**無 FT-09 runtime 依賴**聲明 + StaffTable.factionID 欄位 Post-Jam 啟用路徑 | ✅ |
| 7 | `[systems-index.md]` | FT-09 列為 Day 7-8 系統；`MissionFactionScoreWeight` 合併入 C-01 `MissionDifficultyTable` 同步登記 | ✅ |
| 8 | `[P-02]` GDD | 對話視窗（FIFO + 隊首匹配）+ 委託板 UI（categoryID=3 視覺差異）+ 結算面板（StageResolved epilogue）+ 路線完結 UI；訂閱 5 事件 + 呼叫 `ConfirmDialogue` / `FT-02.Dispatch` | ⏳ 待 P-02 GDD 設計時自行登記 |
| 9 | `[P-03]` GDD | 4 事件可選訂閱（StageUnlocked / DialogueConfirmed / StageResolved / RouteCompleted） | ⏳ 待 P-03 GDD 設計時自行登記 |

**狀態**：7/9 對端登記已完成；P-02 / P-03 待 GDD 撰寫時自行登記（對齊 §1 設計來源對 P-02 / P-03 的待設計聲明）。

**驗證方式**：對 ✅ 項目，於 §6.3 檢查表逐條 grep 對端文件確認 FT-09 / FactionStory 關鍵字仍存在；缺漏時重新檢視。

---

### 6.5 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"factionStorySaveData"` |
| `IsCritical` | `false`（Degradable；對齊 FT-09 EC-10「SaveData 反序列化異常不阻擋 Bootstrap」既定決議） |

**`Serialize()` 序列化欄位**（對應 §3.7.4 `FactionStorySaveData` schema）：

| 欄位 | 型別 | Unity 序列化中介 | 說明 |
|---|---|---|---|
| `_factionScores` | `Dictionary<int,int>` | `List<FactionScoreEntry>` | 各陣營分數（factionID → score） |
| `_unlockedStageIndices` | `Dictionary<int,int>` | `List<StageIndexEntry>` | 各陣營已解鎖的最新 stageIndex（factionID → stageIndex）；`StageIndexEntry` 定義見 §3.7.4 |
| `_pendingDialogueStages` | `Queue<int>` | `List<int>` | 待玩家確認的對話階段 stageID 佇列（FIFO） |
| `_routeCompletedFlags` | `HashSet<int>` | `List<int>` | 已完結的路線 factionID 集合 |

`Dictionary` / `Queue` / `HashSet` 須轉 `List` 中介（對齊 §3.7.1 既定模式 + Unity `JsonUtility` 限制）。

**`RestoreFromSave(string ownerJson)` 行為**（對齊 §5.10 EC-10 五個子情境）：

1. 反序列化上述 4 個容器（由 `List` 轉回原始型別）。
2. 逐條驗證 `_factionScores` 中的 `factionID`：呼叫 `F-01.Get<FactionRouteData>(factionID)` 確認合法；未知 `factionID` 過濾並 `LogWarning`（EC-10 靜默降級，不拋例外）。
3. 逐條驗證 `_unlockedStageIndices` 的 stageIndex 範圍（對應 factionID 的合法 stage 數量）；超出範圍者過濾並 `LogWarning`。
4. 還原完成後執行 Bootstrap §3.1.2 Step F：對 `_pendingDialogueStages` 隊列中的每個 stageID 重新發布 `OnFactionStoryStageUnlocked`（確保重啟後玩家仍能看到待確認的對話視窗）。

**`InitializeAsNewGame()` 預設值**：

| 欄位 | 初始值 |
|---|---|
| `_factionScores` | 空字典 |
| `_unlockedStageIndices` | 空字典 |
| `_pendingDialogueStages` | 空佇列 |
| `_routeCompletedFlags` | 空集合 |

對應 FT-10 §3.3.3 拓撲順序 row 10、§3.3.4 Degradable 分類（EC-10）、§6.1 #17（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

> 本章彙整 FT-09 所有可調旋鈕，遵守 `design-docs.md`「Tuning knobs must specify safe ranges and what gameplay aspect they affect」規範。**§7.1** 列出旋鈕清單與分類；**§7.2** 逐個展開（位置 / 預設值 / 安全範圍 / 影響玩法 / 校準方法）；**§7.3** 給出推薦校準流程；**§7.4** 旋鈕互動矩陣，避免相互抵消的盲調。

### 7.1 旋鈕清單與分類

FT-09 旋鈕分 3 類：

| 類型 | 數量 | 載體 | 是否影響 runtime |
|------|------|------|----------------|
| **A. 資料表旋鈕（CSV）** | 5 | `Assets/Resources/Data/Tables/*.csv` | ✅ 直接影響 |
| **B. 常數旋鈕（SystemConstants）** | 0 | `SystemConstants.csv` | （FT-09 不新增條目） |
| **C. 設計師工具旋鈕（offline）** | 3 | F-4 / F-5 公式輸入（spreadsheet / Editor tool） | ❌ 不影響 runtime，僅校準輔助 |

**完整清單**：

| ID | 旋鈕 | 類型 | 影響面 |
|----|------|------|-------|
| A1 | C-01 `MissionDifficultyTable.factionScoreDelta`（9 個值，每難度一個） | A | 全域分數累積速度 |
| A2 | `StoryStageTable.scoreThreshold`（每階段一個值） | A | 階段解鎖節奏 |
| A3 | `StoryStageTable.missionID`（每階段對應的劇情委託） | A | 劇情委託難度 + buffer 安全性 |
| A4 | `FactionRouteTable` 行數（路線數） | A | 多陣營並行 / 單路線 |
| A5 | `StoryStageTable` 每陣營階段數 | A | 路線敘事密度 / 文本量 |
| C1 | `delta_min_typical`（F-4 安全閾值） | C | F-4 buffer 嚴格度 |
| C2 | `P[difficulty]` 分布（F-5 校準輸入） | C | F-5 預估準確度 |
| C3 | `P_success` 折扣（F-5 可選輸入） | C | F-5 預估準確度 |

---

### 7.2 各旋鈕詳述

#### 7.2.1 A1：C-01 MissionDifficultyTable.factionScoreDelta（FT-09 消費端）

| 欄位 | 內容 |
|---|---|
| **位置** | `Assets/Resources/Data/Tables/MissionDifficultyTable.csv`（C-01 owner），`factionScoreDelta` 欄；9 行（F~SSS 每難度一行） |
| **預設值（Jam）** | F=1, E=1, D=2, C=3, B=5, A=8, S=13, SS=20, SSS=30（§3.2.3 / C-01 §3.1） |
| **安全範圍** | `[0, 100]`；建議滿足單調非遞減 `delta(F) ≤ delta(E) ≤ ... ≤ delta(SSS)`（避免「打 D 比打 SSS 加分多」的反直覺） |
| **影響玩法** | (a) **整體 ×N** → 階段解鎖速度線性放大；(b) **高難度權重相對升高** → 鼓勵挑戰高難度任務（呼應原設計「dark 路線靠 S 級累積」精神）；(c) **某難度設 0** → 該難度任務不貢獻陣營分數（如 F=0 懲罰新手只接低難度） |
| **校準方法** | 用 F-5 對中前期 / 中後期分別預估解鎖節奏，與 playtest 對比；若實測比預估慢 30%+ 則整體 `×1.3`；若僅後期慢則拉高 S/SS/SSS 權重 |

#### 7.2.2 A2：StoryStageTable.scoreThreshold

| 欄位 | 內容 |
|---|---|
| **位置** | `Assets/Resources/Data/Tables/StoryStageTable.csv`，每階段一行的 `scoreThreshold` 欄 |
| **預設值（Jam 秩序）** | `[10, 30, 60]` |
| **安全範圍** | `[1, ∞)`；§3.2.2 強制同 `factionID` 內**嚴格遞增**；違反則該階段載入失敗 |
| **影響玩法** | (a) **整體放大** → 解鎖更慢，玩家中後期才看到劇情；(b) **間距均勻** → 節奏穩定；**遞減** → 後期加速；**遞增** → 後期收尾慢；(c) **太低**（如 `[1, 2, 3]`）→ 開局即 3 個對話彈窗連續轟炸（同 frame 多階段，EC-4） |
| **校準方法** | 用 F-5 反推「N 個任務後達 stage X」對齊設計目標（如「公會等級 3 時解鎖 stage 1」）；用 F-4 驗證每對相鄰 thresholds 的 buffer 安全性 |

#### 7.2.3 A3：StoryStageTable.missionID

| 欄位 | 內容 |
|---|---|
| **位置** | 同 A2，每階段一行的 `missionID` 欄 |
| **預設值（Jam）** | 9001 (B 級), 9002 (A 級), 9003 (SS 級)（範例） |
| **安全範圍** | 須是 `categoryID = 3` 的有效 `MissionTemplate.missionID`（§3.2.2 驗證強制）；該 missionID 對應的 `delta(difficulty)` 須 ≤ 該階段對下階段 buffer（F-4 SafeBuffer 條件） |
| **影響玩法** | (a) **劇情委託難度過高** → 玩家可能因公會等級 / 冒險者強度不足而無法完成（失敗→走 EC-5）；(b) **過低** → 缺乏挑戰；(c) **delta 過大** → buffer 不安全 → 階段 N 結算同 frame 跨入 N+1（EC-9） |
| **校準方法** | 以 F-4 對每對相鄰階段計算 `buffer = threshold[N+1] - threshold[N] - delta(missionDifficulty[N])`；若 unsafe（< C1）→ 換 missionID（換難度）或調下階段 threshold |

#### 7.2.4 A4：FactionRouteTable 行數

| 欄位 | 內容 |
|---|---|
| **位置** | `Assets/Resources/Data/Tables/FactionRouteTable.csv`，有效行數（`factionID != 0`） |
| **預設值（Jam）** | 1 條（factionID=1 秩序） |
| **安全範圍** | **Jam 鎖 1**；Post-Jam 推薦 2-3；> 5 → 文本與 playtest 成本爆炸 |
| **影響玩法** | (a) **>1** → 多陣營並行累積；`GetMaxFactionScore()` 取 max；C-06 收到的 maxScore 可能來自不同陣營；(b) **任務 factionID 分布不均** → 某陣營推進快 / 某陣營慢（玩家體感「秩序路線比混沌路線快」） |
| **校準方法** | Post-Jam 才開放；先以單路線 playtest 確立節奏基線後，再評估多路線是否需均衡權重 |

#### 7.2.5 A5：StoryStageTable 每陣營階段數

| 欄位 | 內容 |
|---|---|
| **位置** | 由該陣營 `stageIndex` 連號的最大值決定（§3.2.2） |
| **預設值（Jam）** | 3 個 |
| **安全範圍** | `[1, 10]`；推薦 3~5（呼應 `game-concept.md`）；`> 5` → 文本量超出 Jam 期內可生成範圍 |
| **影響玩法** | (a) **太少**（1-2）→ 路線完結太快，敘事密度不足；(b) **太多**（>5）→ 文本量爆炸（每階段一個 `dialogueKey` + `missionID`）；(c) **= 1** → 解鎖即完結，無中間張力 |
| **校準方法** | 依 §1 範疇與內容生成能力（haiku / gemini）確定上限；Jam 推薦 3；單路線 PoC 通過後再評估擴充 |

#### 7.2.6 C1：delta_min_typical（F-4 設計師工具參數）

| 欄位 | 內容 |
|---|---|
| **位置** | F-4 公式內變數（spreadsheet / Editor tool 設定） |
| **預設值（Jam）** | 2（即 D 級權重；意義：「玩家最低限度做一個 D 級常規任務後才會跨越下階段 threshold」） |
| **安全範圍** | `[1, 5]`；對應 F-1 ~ B 難度權重區間 |
| **影響玩法（間接）** | (a) **過小（=1）** → buffer 檢查太寬鬆，可能漏掉 EC-9 場景（劇情委託跨越下階段 threshold）；(b) **過大（>5）** → 過度保守，逼迫設計師拉開 thresholds，玩家覺得階段間距過長 |
| **校準方法** | 觀察 playtest 是否頻繁觸發 EC-9（同 frame 多階段）；若是設計目標，維持 2；若不希望發生，提高至 5（B 級權重） |

#### 7.2.7 C2：P【difficulty】分布（F-5 校準輸入）

| 欄位 | 內容 |
|---|---|
| **位置** | F-5 公式輸入，依玩家階段分組（中前期 / 中後期） |
| **預設值（Jam，預估）** | 中前期：`{D: 0.3, C: 0.4, B: 0.3}`；中後期：`{B: 0.4, A: 0.4, S: 0.2}` |
| **安全範圍** | 所有 `P[d] ≥ 0` 且 `Σ P[d] = 1.0`（誤差 ≤ 0.01） |
| **影響玩法（間接）** | F-5 預估失準 → 校準錯誤方向（如把 thresholds 調太低 / 太高） |
| **校準方法** | playtest 後對「玩家在達 stage X 時的任務難度直方圖」採樣，重新建模 P 分布；分階段給定（中前期 / 中後期）以提升精度 |

#### 7.2.8 C3：P_success 折扣（F-5 可選輸入）

| 欄位 | 內容 |
|---|---|
| **位置** | F-5 公式可選乘數 |
| **預設值（Jam，預估）** | 0.85（玩家平均 85% 成功率，依 FT-04 結算曲線推估） |
| **安全範圍** | `[0.5, 1.0]`；`< 0.5` 代表玩家頻繁失敗，需要先修核心平衡而非調 FT-09 |
| **影響玩法（間接）** | F-5 折扣低估 → 預估解鎖時間比實際短 → 設計師可能誤調高 thresholds |
| **校準方法** | playtest 取「所有任務（不限劇情）成功率」的 30 分鐘均值；劇情委託本身的成功率單獨追蹤（用於 EC-5 失敗結局頻率） |

---

### 7.3 推薦校準流程

```
Step 1: 內部 playtest（一場 30 分鐘 session）
        → 記錄每次成功結算的 difficulty 與時間戳
        → 記錄達 stage N 的時間 / 累積任務數
        → 記錄劇情委託成功率（單獨追蹤）

Step 2: F-5 反推 P 分布與 P_success
        → 實測 E[delta] = total_score / mission_count
        → 比對預設 P 分布（C2），調整建模
        → 計算實際 P_success（C3）

Step 3: F-4 驗證 buffer
        → 對每個 (stage N, stage N+1) 計算 buffer
        → 若 unsafe（buffer < C1）→ 標記待調 A2 或 A3

Step 4: 調整旋鈕（按優先順序）
        Priority 1: A2 (thresholds) — 影響最直接、最易理解、不影響其他系統
        Priority 2: A1 (weights 等比例縮放) — 影響全局節奏，但可能與 C-06 共振（高分數推升世界危險度）
        Priority 3: A3 (missionID 換難度) — 影響 buffer + 玩家挑戰感，但需 C-01 提供對應難度的劇情 missionID
        Priority 4: C1 (調整 buffer 寬鬆度) — 純 offline 工具旋鈕，影響後續設計判斷

Step 5: 重 playtest 驗證；若 ≥ 3 輪未收斂則重檢 §1 範疇與目標節奏
```

**校準目標範例**（Jam 內部目標，待 playtest 校準）：

| 指標 | 目標值 | 校準依據 |
|------|-------|---------|
| 達 stage 1 | 30 分鐘 session 內第 8~12 分鐘 | 中前期玩家節奏，避免太快 |
| 達 stage 3（路線完結） | 30 分鐘 session 內第 25~30 分鐘 | 中後期收尾，玩家未必能完成 |
| 同 frame 多階段事件發生率 | < 10%（除非設計目標） | 避免對話視窗連續轟炸 |
| 劇情委託失敗率 | < 30% | 保留挑戰感但不挫折 |

---

### 7.4 旋鈕互動矩陣

下表標記旋鈕間的互動：⊕ = 同向放大可相互抵消（盲調風險）；🔁 = 一方變動須重檢另一方；⛓ = 結構強制依賴（schema 層）；— = 無交互。

| ↓ × → | A1 weights | A2 thresholds | A3 missionID | A4 route count | A5 stage count |
|------|-----------|--------------|--------------|---------------|---------------|
| **A1 weights** | — | ⊕ | 🔁 (改變 buffer) | — | — |
| **A2 thresholds** | ⊕ | — | 🔁 (改變 buffer) | — | ⛓ (行數 = A5) |
| **A3 missionID** | 🔁 | 🔁 | — | — | ⛓ (行數 = A5) |
| **A4 route count** | — | — | — | — | ⛓ (每路線需獨立 A5) |
| **A5 stage count** | — | ⛓ | ⛓ | ⛓ | — |

**關鍵互動解讀**：

- **A1 ⊕ A2**：等比例同向放大會相互抵消（如 weights ×2 + thresholds ×2 = 解鎖節奏不變）→ 校準時應**鎖一動一**
- **A1 / A2 → A3**：權重或 threshold 調整後，必須用 F-4 重檢每階段 buffer 是否仍 safe（C1）
- **A3 → A1 / A2**：劇情委託換難度後，原本 safe 的 buffer 可能變 unsafe；尤其 A3 升難度更易出問題
- **A5 ⛓ A2 / A3**：階段數變動須同步 `StoryStageTable` 行數，且每行的 `scoreThreshold` / `missionID` 都需重新規劃
- **A4 ⛓ A5**：多路線時每個 route 須獨立規劃 A5（不要求各路線階段數相同，但每路線都需 ≥ 1 階段）
- **C1 / C2 / C3 → A1~A5**：offline 工具旋鈕僅影響 F-4 / F-5 預估結果，間接驅動 A1~A5 的調整方向；不直接影響 runtime

> **盲調預防**：本表的 ⊕ 與 🔁 標記應在 §7.3 校準流程 Step 4 的優先順序執行時逐項檢查；尤其 A1 ⊕ A2 是最易誤入的盲調陷阱（「我把 weights 翻倍但解鎖節奏沒變？」→ 因為 thresholds 也跟著翻倍）。

---

## 8. 驗收標準（Acceptance Criteria）

> 本章遵守 `design-docs.md`「Acceptance criteria must be testable — a QA tester must be able to verify pass/fail」規範。所有條目以「前置 / 操作 / 預期 / 驗證方式」四欄表達，可由人工或自動化測試執行。**§8.1** 功能性驗收（8 條 AC-F）；**§8.2** 邊緣案例驗收（12 條 AC-EC，對應 §5）；**§8.3** 跨系統契約驗收（9 條 AC-D，對應 §6.3）；**§8.4** 校準目標驗收（4 條 AC-T，對應 §7.3）；**§8.5** 明確排除項目（不在 Jam 驗收範圍）。

### 8.1 功能性驗收（Functional ACs）

#### AC-F-1：CSV 載入與啟用判定

| 欄位 | 內容 |
|---|---|
| **前置** | (a) Valid CSV：FT-09 owner 二表齊全（FactionRouteTable + StoryStageTable）+ C-01 `MissionDifficultyTable.factionScoreDelta` 9 行齊全 + 至少 1 條 valid 路線；(b) Invalid CSV：FactionRouteTable 全 neutral；(c) 缺失：C-01 `MissionDifficultyTable` 缺 SS 行 → `GetFactionScoreDelta("SS")` 回 0 |
| **操作** | 啟動遊戲，等 Bootstrap 完成 |
| **預期** | (a) `IsFactionStoryEnabled() == true`；(b)(c) `IsFactionStoryEnabled() == false` |
| **驗證方式** | 主控台呼叫 API 觀察回傳值；EditMode test 直接斷言 `_isEnabled` |

#### AC-F-2：分數累積（成功 / neutral / 失敗）

| 欄位 | 內容 |
|---|---|
| **前置** | `_isEnabled == true`；`_factionScores[1] = 0`（初始） |
| **操作** | 依序觸發 3 次 `OnMissionResolved`：(a) `(factionID=1, success, B)` (b) `(factionID=0, success, A)` (c) `(factionID=1, fail, SSS)` |
| **預期** | (a) 後 `_factionScores[1] == 5`；(b) 後 `_factionScores[1] == 5`（不變）；(c) 後 `_factionScores[1] == 5`（不變） |
| **驗證方式** | `GetCurrentFactionScore(1)` 在每次操作後查詢；EventBus log 應僅見 1 次 `OnFactionScoreChanged` 與 1 次 `C-06.OnFactionScoreUpdated(5)` |

#### AC-F-3：階段解鎖（單階段 / 同 frame 多階段 / 不重複）

| 欄位 | 內容 |
|---|---|
| **前置** | Jam 預設 thresholds=[10, 30, 60]；`_unlockedStageIndices[1] = 0` |
| **操作** | (a) `_factionScores[1] = 0` → 觸發 +12 → 觀察解鎖；(b) `_factionScores[1] = 28` → 觸發 +30 (SSS) → 觀察解鎖；(c) `_unlockedStageIndices[1] = 2` → 觸發 +5 → 觀察解鎖 |
| **預期** | (a) 解鎖 stage 1，1 個事件、`_pendingDialogueStages = [1001]`；(b) 解鎖 stage 1, 2，2 個事件依序、`_pendingDialogueStages = [1001, 1002]`；(c) 無新解鎖、無事件、queue 不變 |
| **驗證方式** | EventBus 訂閱 `OnFactionStoryStageUnlocked` 計數；查詢 `GetUnlockedStageIndex(1)` |

#### AC-F-4：雙軌呈現（對話 → 委託）

| 欄位 | 內容 |
|---|---|
| **前置** | 已解鎖 stage 1（`_pendingDialogueStages = [1001]`）；FT-02 mock：`InjectStaticMission` 回 OK |
| **操作** | 依序：(a) 呼叫 `ConfirmDialogue(1001)`；(b) 呼叫 `ConfirmDialogue(9999)`（不存在）；(c) 重複呼叫 `ConfirmDialogue(1001)`（已出隊） |
| **預期** | (a) 回 `OK`；FT-02 收到 `InjectStaticMission(9001)` 呼叫；發 `OnFactionStoryDialogueConfirmed`；queue 出隊；(b) 回 `INVALID_STAGE_ID`；FT-02 不被呼叫；(c) 回 `INVALID_STAGE_ID`（queue 為空） |
| **驗證方式** | mock FT-02 記錄呼叫；EventBus log 觀察事件序列；queue 大小斷言 |

#### AC-F-5：階段推進不可逆（劇情委託失敗）

| 欄位 | 內容 |
|---|---|
| **前置** | `_unlockedStageIndices[1] = 2`；玩家派遣 stage 2 劇情委託（missionID=9002, A 級） |
| **操作** | 觸發 `OnMissionResolved(missionID=9002, factionID=1, isSuccess=false, isDead=true)` |
| **預期** | `_unlockedStageIndices[1]` 仍為 2（不回退）；發 `OnFactionStoryStageResolved(stageID=1002, isSuccess=false, isDead=true)`；不重新入隊 1002 |
| **驗證方式** | 查詢 `GetUnlockedStageIndex(1)`；觀察 EventBus；確認 `_pendingDialogueStages` 不含 1002 |

#### AC-F-6：路線完結事件一次性

| 欄位 | 內容 |
|---|---|
| **前置** | `_unlockedStageIndices[1] = 3`（最末階段已解鎖）；玩家派遣 stage 3 劇情委託 |
| **操作** | (a) 觸發 stage 3 結算（success）；(b) 再次觸發任何 factionID=1 結算 |
| **預期** | (a) 發 `OnFactionRouteCompleted(factionID=1, finalStageID=1003, finalIsSuccess=true, totalStages=3)`；`_routeCompletedFlags = {1}`；(b) **不再發** `OnFactionRouteCompleted`；分數累積與 C-06 推送仍正常（EC-11） |
| **驗證方式** | EventBus 計數器應為 1；重啟遊戲後 Bootstrap 不重發（§3.6.4） |

#### AC-F-7：C-06 主動推送（Bootstrap + 每次計分）

| 欄位 | 內容 |
|---|---|
| **前置** | C-06 mock 記錄 `OnFactionScoreUpdated` 呼叫歷史 |
| **操作** | (a) Bootstrap（含載入 SaveData：`_factionScores = {1: 17}`）；(b) 觸發 `OnMissionResolved(factionID=1, success, B)` |
| **預期** | (a) C-06 收到 1 次 `OnFactionScoreUpdated(17)`（即使全 0 也推一次，§3.1.2 Step E）；(b) C-06 收到 1 次 `OnFactionScoreUpdated(22)` |
| **驗證方式** | mock C-06 呼叫歷史長度 + 參數值斷言 |

#### AC-F-8：SaveData 持久化與還原

| 欄位 | 內容 |
|---|---|
| **前置** | runtime 狀態：`_factionScores={1:17}`、`_unlockedStageIndices={1:2}`、`_pendingDialogueStages=[1003]`、`_routeCompletedFlags={}` |
| **操作** | (a) 觸發存檔；(b) 重啟並載入存檔；(c) 觀察 Bootstrap Step F 補發 |
| **預期** | (a) SaveData 4 欄位正確序列化（`pendingDialogueStages` 為 List `[1003]`）；(b) runtime 狀態完全還原；(c) `OnFactionStoryStageUnlocked(stageID=1003, ...)` 重發 1 次 |
| **驗證方式** | SaveData JSON 檢視；重啟後 API 查詢比對；EventBus log 觀察 Bootstrap 補發事件 |

---

### 8.2 邊緣案例驗收（Edge Case ACs）

對應 §5 EC-1 ~ EC-12，每條一個 AC-EC：

| AC | 驗收項目 | 觸發方式 | 預期行為 | 驗證方式 |
|----|---------|---------|---------|---------|
| AC-EC-1 | CSV 載入失敗 → 降級 | 移除 / 損壞任一 CSV 檔 | `_isEnabled=false`；FT-09 所有 API 回降級值；不訂閱 `OnMissionResolved` | API 查詢 + EventBus 訂閱列表斷言 |
| AC-EC-2 | 未知 factionID → LogWarning | 觸發 `OnMissionResolved(factionID=99, ...)`（99 不在 FactionRouteTable） | 早退；`Debug.LogWarning` 出現；分數不變 | console log + 分數查詢 |
| AC-EC-3 | 未確認對話關遊戲 → Bootstrap 補發 | 解鎖階段不確認 → 強制關閉 → 重啟 | Bootstrap Step F 對 queue 內每個 stageID 重發事件 | EventBus log（重啟後事件數 = 關閉前 queue 大小） |
| AC-EC-4 | 同 frame 多階段 → FIFO 順序 | 一次大幅加分跨 2+ 階段 | 事件依 stageIndex 升序逐個發；queue 同序 | EventBus 順序斷言 + queue 內容檢查 |
| AC-EC-5 | 劇情委託失敗 / 死亡 → 不重置 | 派遣劇情委託 → 結算 fail/dead | `_unlockedStageIndices` 不變；發 StageResolved(isSuccess=false) | API 查詢 + EventBus payload 斷言 |
| AC-EC-6 | 玩家忽略所有劇情委託 | 連續加分跨完所有階段，不點任何確認 | 所有事件已發；queue 含所有 stageID；委託板無劇情委託（未確認前不注入） | queue 大小 + FT-02 mock 呼叫次數 = 0 |
| AC-EC-7 | InjectStaticMission 失敗 → 不出隊 | mock FT-02 回 BOARD_DISABLED | `ConfirmDialogue` 回 INJECT_FAILED；queue 不變 | 回傳值 + queue 大小斷言 |
| AC-EC-8 | P-02 順序錯誤 | 隊首為 1001，呼叫 `ConfirmDialogue(1002)` | 回 INVALID_STAGE_ID；不出隊；不呼叫 FT-02 | 回傳值 + mock FT-02 呼叫數 |
| AC-EC-9 | 劇情委託計分跨下階段 | 階段 N 解鎖 → 結算 stage N 委託（其 delta 大於 buffer） | 同 frame 內依序：StageResolved(N) → StageUnlocked(N+1) | EventBus 序列斷言 |
| AC-EC-10 | SaveData 異常 → 不阻擋 Bootstrap | (a) 缺整區塊 (b) null 欄位 (c) JSON 損壞 (d) 未知 factionID (e) stageIndex 超範圍 | (a)(b)(c) 等同首次遊玩；(d) 過濾未知 key；(e) 夾擠至 maxStageIndex | runtime 狀態斷言 + console warning |
| AC-EC-11 | 路線完結後計分仍跑 | `_routeCompletedFlags={1}`；觸發 factionID=1 結算 | 分數仍累積；C-06 仍推送；不重發 RouteCompleted；不發 StageResolved（無 categoryID=3 任務存在） | 分數查詢 + EventBus 計數 |
| AC-EC-12 | Bootstrap 時序失準 | (a) 在 Bootstrap 前呼叫 API；(b) DataManager 未就緒 | (a) 走降級路徑（不拋例外）；(b) `_isEnabled=false` + LogError | API 回傳值 + console error |

---

### 8.3 跨系統契約驗收（Dependency ACs）

對應 §6.3 雙向依賴 9 條，每條一個 AC-D：

| AC | 對端 | 驗收項目 | 操作 / 驗證 |
|----|------|---------|-----------|
| AC-D-1 | FT-04 | FT-09 訂閱 `OnMissionResolved` 並消費 5 個欄位 | 發布事件後 FT-09 處理；EditMode test 注入 mock outcome 驗證 |
| AC-D-2 | F-01 | FT-09 透過 DataManager 取 3 個 Data 型別 | Bootstrap 時 DataManager log 顯示 3 次查詢；缺任一 → `_isEnabled=false` |
| AC-D-3 | C-01 | FT-09 透過 GetTemplate 識別 categoryID=3 | mock C-01 回 categoryID=3 → §3.6.2 路徑執行；回 ≠ 3 → 跳過 |
| AC-D-4 | FT-02 | FT-09 呼叫 InjectStaticMission | mock FT-02 觀察呼叫參數；驗 OK / UNKNOWN_MISSION_ID / WRONG_CATEGORY 各回應 |
| AC-D-5 | SystemConstants | FT-09 引用 `FACTION_NEUTRAL_ID = 0` | 修改該常數為 99 → factionID=0 任務不再被過濾（驗證 FT-09 確實讀常數而非硬編碼 0） |
| AC-D-6 | C-06 | FT-09 主動推送 `OnFactionScoreUpdated` | mock C-06 記錄呼叫歷史；參見 AC-F-7 |
| AC-D-7 | P-02 | 5 個事件可被外部訂閱、ConfirmDialogue 可被外部呼叫 | mock P-02 訂閱所有事件並呼叫 ConfirmDialogue；驗事件 payload schema 與回傳值 |
| AC-D-8 | P-03 | 4 個可選事件可被訂閱（不訂閱不報錯） | 不訂閱情境下 FT-09 流程不受影響 |
| AC-D-9 | FT-08 / FT-12 | Jam 版**無 runtime 連接** | grep FT-09 程式碼不應引用任何 FT-08 / FT-12 識別符號；三個系統各自獨立啟用 |

---

### 8.4 校準目標驗收（Tuning ACs）

對應 §7.3 校準目標範例：

| AC | 校準指標 | 目標值 | 量測方式 | Pass 條件 |
|----|---------|-------|---------|---------|
| AC-T-1 | 達 stage 1 時間 | session 內第 8~12 分鐘 | playtest 計時器記錄首個 StageUnlocked 時間戳 | 至少 3 場 playtest 中位數落在區間 |
| AC-T-2 | 達 stage 3（路線完結）時間 | session 內第 25~30 分鐘 | playtest 計時器記錄 RouteCompleted 時間戳 | 至少 3 場 playtest 中位數落在區間 |
| AC-T-3 | 同 frame 多階段事件率 | < 10%（除非設計目標） | playtest log 統計：(同 frame 解鎖 ≥2 階段次數) / (總解鎖事件數) | 比例 < 10% |
| AC-T-4 | 劇情委託失敗率 | < 30% | playtest log 統計：(劇情委託 isSuccess=false 次數) / (劇情委託結算總數) | 比例 < 30% |

> 校準目標驗收**不阻擋程式碼合入**——這些是**內部 playtest 後的調整指標**；若實測未達標，調整 §7.2 旋鈕（依 §7.3 流程）後重 playtest，不視為程式碼缺陷。

---

### 8.5 排除項目（Out of Jam Scope）

以下功能**明確不在** Jam 版 FT-09 驗收範圍。QA 不需測試、Codex 不需實作：

| 排除項目 | 對應規範 |
|---------|---------|
| 第 2 條陣營路線（factionID=2 等） | §1「不在 Jam 範疇」+ A4 旋鈕鎖 1 |
| 跨陣營對抗（成功一邊扣另一邊分數） | §1「不在 Jam 範疇」+ §3.4.4 反證場景 |
| FT-08 / FT-12 職員陣營傾向影響分數 | §1 + §6.2.4 / §6.2.5 + AC-D-9 |
| 多分支劇情樹（階段分歧） | §1「階段線性」 |
| 接受時計分（FT-09 訂閱 `OnCommissionAccepted`） | §1「僅監聽結算事件」 |
| 階段委託本身的特殊規則（如雙重保險、必須特定冒險者） | §1「Jam 版階段任務走 FT-02 標準派遣流程」 |
| 階段任務過期 / 自動取消 | §3.5.3「靜態委託永久停留在板上直到玩家派遣」 |
| Runtime 啟用 / 停用熱切換 | §3.1.4 |
| 事件 replay（補發 Bootstrap 前遺失的 OnMissionResolved） | EC-12 註 |
| SaveData versioning 機制 | EC-10 規範細節「Jam 版不引入 version 欄位」 |
| Endgame chapter（路線完結後的補充劇情） | EC-11 Post-Jam 擴充提示 |
| Jam 版 Notification（P-03）強制實作 | §6.2.3「可選訂閱」 |

> **設計動機**：明確排除項目可避免 Codex 在實作時過度推測、QA 在測試時誤判 fail；Post-Jam 階段啟用對應功能時，本表逐項解鎖。

---

### 8.6 整體驗收彙總

| 驗收類型 | 條目數 | 必須 pass 條件 |
|---------|-------|--------------|
| §8.1 功能性 | 8 條 AC-F | **全部 pass** 才視為 FT-09 功能正確 |
| §8.2 邊緣案例 | 12 條 AC-EC | **全部 pass** 才視為 FT-09 防禦性完備 |
| §8.3 跨系統契約 | 9 條 AC-D | **全部 pass** 才視為 FT-09 與依賴系統契約對齊 |
| §8.4 校準目標 | 4 條 AC-T | playtest 後**全部 pass** 視為節奏校準完成；未 pass 不阻擋合入，依 §7.3 校準 |
| §8.5 排除項目 | 12 項 | QA 不測試；Codex 不實作；Post-Jam 解鎖 |

**驗收順序建議**：

```
Step 1: AC-F-1 ~ AC-F-8（EditMode test 為主）
Step 2: AC-EC-1 ~ AC-EC-12（EditMode + PlayMode 混合）
Step 3: AC-D-1 ~ AC-D-9（與其他系統 mock / 整合測試）
Step 4: AC-T-1 ~ AC-T-4（內部 playtest，最後執行）
```

**驗收通過後**：FT-09 GDD 進入 Codex 實作階段；§6.4 反向依賴清單同步批次處理。

---

<a id="appendix-a-formulas"></a>
## 附錄 A：公式詳述（Formulas Detail）

> 本附錄存放 §4 公式章節的完整內容，包含 F-1~F-5 共 5 條公式的正式形式、變數定義、值域、範例計算。§4 主章節保留摘要與本附錄錨點連結。

<a id="appendix-a-1"></a>
### A.1 F-1：陣營分數累積（Score Accrual）

**正式形式**：

```
newScore[factionID] = oldScore[factionID] + delta(difficulty)
```

其中 `delta(difficulty)` 由過濾規則決定：

```
delta(difficulty) =
    C-01.GetFactionScoreDelta(difficulty)   // 取自 MissionDifficultyTable.factionScoreDelta
        if  outcome.isSuccess == true
        AND outcome.missionFactionID != FACTION_NEUTRAL_ID(0)
        AND outcome.missionFactionID ∈ FactionRouteTable
        AND difficulty ∈ {F, E, D, C, B, A, S, SS, SSS}

    0  (no-op，分數不變動，不發 OnFactionScoreChanged，不推 C-06)
        otherwise
```

**變數定義**：

| 變數 | 型別 | 含義 | 來源 |
|---|---|---|---|
| `oldScore[factionID]` | int | 該陣營當前累積分數 | `_factionScores.GetValueOrDefault(factionID, 0)` |
| `newScore[factionID]` | int | 計算後分數，寫回 `_factionScores[factionID]` | F-1 結果 |
| `delta(difficulty)` | int | 該難度任務加分權重 | C-01 `MissionDifficultyTable.factionScoreDelta`（§3.2.3 描述消費端，C-01 §3.1 為 schema） |
| `difficulty` | string | 任務難度（F~SSS 共 9 種） | `outcome.missionDifficulty`（FT-04） |
| `outcome.isSuccess` | bool | 任務是否成功結算 | FT-04 §3 |
| `outcome.missionFactionID` | int | 任務所屬陣營（0 = neutral） | FT-04 §3 / C-01 §3.2 |

**值域**：

- `oldScore[factionID] ∈ [0, ∞)`：單向加分，永不變負
- `delta(difficulty) ∈ [0, 30]`：Jam 預設範圍（F=1 ~ SSS=30）；§3.2.3 驗證強制 ≥ 0
- `newScore[factionID] ∈ [0, ∞)`：理論無上限；實務由最末階段 `scoreThreshold` 決定有意義上限（達到後仍可累積但無新解鎖）

**範例計算**：

| # | 場景 | 前置 | 過程 | 結果 |
|---|------|------|------|------|
| 1 | 單次成功 B 級任務 | `_factionScores[1] = 12`<br>outcome=(fid=1, success, B) | `delta = 5` | `_factionScores[1] = 17` |
| 2 | neutral 任務 | `_factionScores[1] = 17`<br>outcome=(fid=0, success, A) | 早退（fid==0），delta 不算 | `_factionScores[1] = 17`（不變） |
| 3 | 失敗任務 | `_factionScores[1] = 17`<br>outcome=(fid=1, fail, SSS) | 早退（!success） | `_factionScores[1] = 17`（不變） |
| 4 | 首次計分（從 0 起） | `_factionScores` 無 key=1<br>outcome=(fid=1, success, F) | `oldScore = 0`<br>`delta = 1` | `_factionScores[1] = 1` |

---

<a id="appendix-a-2"></a>
### A.2 F-2：陣營最高分（Max Faction Score）

**正式形式**：

```
GetMaxFactionScore() =
    0                              if NOT _isEnabled
    0                              if _factionScores.IsEmpty
    max(_factionScores.Values)     otherwise
```

**變數定義**：

| 變數 | 型別 | 含義 | 來源 |
|---|---|---|---|
| `_isEnabled` | bool | FT-09 啟用 flag | §3.1.1 |
| `_factionScores` | Dictionary<int, int> | 各陣營當前分數 | §3.7.4 |
| 回傳值 | int | 任意陣營當前最高分數，推送給 `C-06.OnFactionScoreUpdated` | §3.3.5 |

**值域**：

- 回傳值 ∈ [0, ∞)：與 F-1 一致，永不變負
- Jam 版單路線：等同 `_factionScores[1]`（若已計分）或 `0`
- Post-Jam 多路線：`max` 操作覆蓋多 key，無需修改 C-06 契約

**範例計算**：

| # | 場景 | 狀態 | 結果 | 推送行為 |
|---|------|------|------|---------|
| 1 | Jam 版單路線 | `_isEnabled=true`<br>`_factionScores = {1: 17}` | `max({17}) = 17` | `C-06.OnFactionScoreUpdated(17)` |
| 2 | Post-Jam 多路線預演 | `_factionScores = {1: 17, 2: 24}` | `max({17, 24}) = 24` | `C-06.OnFactionScoreUpdated(24)` |
| 3 | 降級時 | `_isEnabled = false` | `0` | 不推送（§3.1.3） |
| 4 | Bootstrap 初始 | `_isEnabled=true`<br>`_factionScores = {}` | `0` | `C-06.OnFactionScoreUpdated(0)`（§3.1.2 Step E 即使全 0 也推一次） |

---

<a id="appendix-a-3"></a>
### A.3 F-3：階段解鎖判定（Stage Unlock Check）

**正式形式**：

判定單一階段是否應該解鎖：

```
IsStageUnlockable(stage, currentScore) =
    (stage.stageIndex > _unlockedStageIndices.GetValueOrDefault(stage.factionID, 0))
    AND
    (currentScore >= stage.scoreThreshold)
```

線性掃描的提早終止條件（§3.4.2 Step 3 break 邏輯）：

```
ShouldStopScan(stage, currentScore) =
    (currentScore < stage.scoreThreshold)

// 因 §3.2.2 規範同 factionID 內 scoreThreshold 嚴格遞增，
// 一旦遇到未達標的階段，後續階段必定也未達標，可立即 break
```

**變數定義**：

| 變數 | 型別 | 含義 | 來源 |
|---|---|---|---|
| `stage` | StoryStageData | 候選階段 | `StoryStageTable.GetByFactionID(factionID)`（升序） |
| `stage.stageIndex` | int | 該陣營路線中的階段序號（從 1 連號） | §3.2.2 |
| `stage.scoreThreshold` | int | 解鎖門檻 | §3.2.2 |
| `currentScore` | int | F-1 計算後的 `newScore[factionID]` | §3.3.2 Step 3 |
| `_unlockedStageIndices[factionID]` | int | 該陣營已解鎖的最大 stageIndex（初始 0） | §3.4.1 |

**值域**：

- `stage.scoreThreshold ∈ [1, ∞)`：必為正整數，且同 factionID 內嚴格遞增
- `currentScore ∈ [0, ∞)`：F-1 結果
- `stage.stageIndex ∈ [1, totalStages]`：§3.2.2 強制連號
- 回傳值：`bool`

**範例計算**（Jam 預設秩序路線，`thresholds = [10, 30, 60]`）：

| # | 場景 | 前置 | 掃描過程 | 結果 |
|---|------|------|---------|------|
| 1 | 達標解鎖（從未解鎖開始） | `_unlockedStageIndices[1]=0`<br>`newScore=12` | stage 1（thr=10）：12≥10 ✅ 解鎖<br>stage 2（thr=30）：12<30 → break | 解鎖 stageID=1001<br>`_pendingDialogueStages = [1001]` |
| 2 | 同 frame 跨多階段 | `_unlockedStageIndices[1]=0`<br>SSS+30 → `newScore=58`（前一刻 28） | stage 1：58≥10 ✅<br>stage 2：58≥30 ✅<br>stage 3（thr=60）：58<60 → break | 依序解鎖 1001, 1002<br>`_pendingDialogueStages = [1001, 1002]`<br>發 2 個事件 |
| 3 | 不重複解鎖 | `_unlockedStageIndices[1]=2`<br>`newScore=35` | 只掃 stageIndex>2：<br>stage 3（thr=60）：35<60 → break | 無新解鎖 |
| 4 | 無候選階段達標 | `_unlockedStageIndices[1]=0`<br>`newScore=5` | stage 1（thr=10）：5<10 → break | 無解鎖 |

---

<a id="appendix-a-4"></a>
### A.4 F-4：階段間距 buffer 計算（設計師工具）

> **執行模型**：本公式**不在 runtime 執行**，純粹是設計師校準 `StoryStageTable.csv` 時用於檢查 `scoreThreshold` 數值合理性的工具；可用 spreadsheet 或 Unity Editor tool 預驗。對應 §3.6.7 規範「劇情委託本身計分回流」的 buffer 預留要求。

**正式形式**：

階段 N 結算後距下一階段的 buffer：

```
buffer(stageIndex_n) = scoreThreshold(stageIndex_{n+1}) - scoreThreshold(stageIndex_n)
                       - delta(missionDifficulty(stageIndex_n))
```

安全條件（§3.6.7 規範）：

```
SafeBuffer(stageIndex_n) ⟺ buffer(stageIndex_n) >= delta_min_typical

  其中 delta_min_typical = 設計師選定的「至少能容納一個典型常規任務難度的權重」
       Jam 預設推薦值 = max(delta(F), delta(E), delta(D)) = 2
       （即玩家最低限度做一個 D 級常規任務後才會跨越下一階段 threshold）
```

**變數定義**：

| 變數 | 型別 | 含義 | 來源 |
|---|---|---|---|
| `stageIndex_n` | int | 第 n 個階段（1-based） | §3.2.2 |
| `scoreThreshold(stageIndex_n)` | int | 第 n 階段的 threshold | `StoryStageTable.csv` |
| `missionDifficulty(stageIndex_n)` | string | 第 n 階段劇情委託的難度 | `MissionTemplate[stage.missionID].difficulty`（C-01） |
| `delta(difficulty)` | int | 該難度的權重 | C-01 `MissionDifficultyTable.factionScoreDelta`（§3.2.3 描述消費端） |
| `delta_min_typical` | int | 設計師判定的「典型常規任務」最小單位（推薦 2） | 設計師 judgment |

**值域**：

- `buffer ∈ (-∞, ∞)`：理論可為負（劇情委託本身計分就跨越下一階段 threshold）
- `SafeBuffer == true` ⟺ `buffer >= delta_min_typical`
- `delta_min_typical` 推薦範圍 [1, 5]：太小（=1）幾乎沒緩衝，太大（>5）逼迫玩家做大量常規任務

**範例計算**（Jam 預設秩序路線，`thresholds = [10, 30, 60]`，假設劇情委託難度為 9001=B(5), 9002=A(8), 9003=SS(20)）：

| # | 區間 | 計算 | 安全判定 | 解讀 |
|---|------|------|---------|------|
| 1 | stageIndex 1→2（劇情委託 1 為 B 級） | `30 - 10 - 5 = 15` | `15 >= 2` ✅ Safe | 解鎖 stage 1、結算 stage 1 委託（+5）後分數 = 15，距 stage 2（thr=30）還差 15，需額外常規任務累積 |
| 2 | stageIndex 2→3（劇情委託 2 為 A 級） | `60 - 30 - 8 = 22` | `22 >= 2` ✅ Safe | 解鎖 stage 2、結算 stage 2 委託（+8）後分數 ≈ 38，距 stage 3 還差 22 |
| 3 | 不安全範例（假設 thresholds 改為 [10, 30, 35]，stage 2 委託 A 級） | `35 - 30 - 8 = -3` | `-3 < 2` ❌ Unsafe | 解鎖 stage 2、結算 stage 2 委託成功的同 frame 立刻解鎖 stage 3（§3.4.3 同 frame 多階段路徑）—— 玩家未體驗到 stage 2 的「節奏停頓」 |

> 範例 3 的處理選擇（設計師決定）：要嘛接受此緊湊節奏（如終局收束、Boss rush 風格）、要嘛調高 stage 3 threshold 至 ≥ 40（`buffer = 40-30-8 = 2`，剛好滿足）。

---

<a id="appendix-a-5"></a>
### A.5 F-5：解鎖節奏推估（playtest 校準工具）

> **執行模型**：本公式**不在 runtime 執行**，是設計師於 playtest 後對比實際數據、校準 C-01 `MissionDifficultyTable.factionScoreDelta` 或 `StoryStageTable.scoreThreshold` 的工具；亦支援 §7 Tuning Knobs 的安全範圍預估。

**正式形式**：

給定典型任務難度分布 `P = {difficulty: probability}`（總和 = 1），每單任務的期望加分：

```
E[delta_per_mission] = Σ (P[difficulty] × delta(difficulty))
                       for difficulty ∈ {F, E, D, C, B, A, S, SS, SSS}
```

達到階段 N 的期望任務數：

```
E[missions_to_unlock_stage_N] = scoreThreshold(stageIndex_N) / E[delta_per_mission]
```

階段 N → N+1 的期望任務間距：

```
E[missions_between(N, N+1)] =
    (scoreThreshold(stageIndex_{N+1}) - scoreThreshold(stageIndex_N)) / E[delta_per_mission]
```

**前提假設**（明確列出避免誤用）：

1. 假設玩家任務皆為 `factionID != 0`（即都計入分數）；實務中 neutral 任務需以 `(1 - P_neutral)` 折扣
2. 假設成功率為 100%；實務中失敗率需以 `P_success` 折扣（有效 `E[delta] = E[delta] × P_success`）
3. 假設任務難度分布在達標前後不變；實務中玩家會隨公會等級提升任務難度，需分階段給定 `P`

**變數定義**：

| 變數 | 型別 | 含義 | 來源 |
|---|---|---|---|
| `P[difficulty]` | float ∈ [0, 1] | 玩家於該階段每接一個任務時，難度為 X 的機率 | playtest 觀察 / 設計目標 |
| `E[delta_per_mission]` | float | 每單任務期望加分 | 公式計算 |
| `scoreThreshold(stageIndex_N)` | int | 階段 N 的 threshold | `StoryStageTable` |
| `E[missions_to_unlock_stage_N]` | float | 達 stage N 的期望任務數 | 公式計算 |
| `P_success` | float ∈ [0, 1] | 成功率折扣（可選） | playtest 觀察 |

**值域**：

- `E[delta_per_mission] ∈ [0, 30]`：取決於分布，邊界對應「全部 F 級」（=1）與「全部 SSS」（=30）
- `E[missions_to_unlock_stage_N] ∈ [0, ∞)`：分布越偏低難度則越大

**範例計算**（Jam 預設權重表：F=1, E=1, D=2, C=3, B=5, A=8, S=13, SS=20, SSS=30；秩序路線 thresholds=[10, 30, 60]）：

| # | 玩家階段 | 難度分布 P | E[delta]/單 | 達 stage 1 | 達 stage 2 | 達 stage 3 |
|---|---------|----------|-----------|-----------|-----------|-----------|
| 1 | 中前期（D-B 集中） | D:0.3, C:0.4, B:0.3 | `0.3×2 + 0.4×3 + 0.3×5 = 3.3` | `10/3.3 ≈ 3.0 單` | `30/3.3 ≈ 9.1 單` | `60/3.3 ≈ 18.2 單` |
| 2 | 中後期（B-A 集中） | B:0.4, A:0.4, S:0.2 | `0.4×5 + 0.4×8 + 0.2×13 = 7.8` | `10/7.8 ≈ 1.3 單` | `30/7.8 ≈ 3.8 單` | `60/7.8 ≈ 7.7 單` |
| 3 | 含 50% 成功率折扣（同範例 2 分布） | 同上，P_success=0.5 | 有效 `7.8 × 0.5 = 3.9` | `≈ 2.6 單` | `≈ 7.7 單` | `≈ 15.4 單` |

> 校準範例：若 playtest 數據顯示中前期玩家平均 25 單即達 stage 3（範例 1 預測 18.2 單），代表實際 `E[delta] ≈ 60/25 = 2.4`，低於模型預測 3.3 → 可能 P 分布偏低（D 較多）或失敗率較高 → 對應調整：(a) 提高低難度權重 (b) 降低 stage 3 threshold (c) playtest 重採 P 分布。

---

---

<a id="appendix-b-edge-cases"></a>
## 附錄 B：邊緣案例詳述（Edge Cases Detail）

> 本附錄存放 §5 邊緣案例章節的完整內容，包含 EC-1~EC-12 全部展開描述。§5 主章節保留索引表與本附錄錨點連結。

<a id="appendix-b-1"></a>
### B.1 EC-1：CSV 載入失敗或部分缺失

| 項目 | 內容 |
|---|---|
| **觸發** | (a) `FactionRouteTable` 全為 neutral / 空表；(b) `StoryStageTable` 空表；(c) C-01 `MissionDifficultyTable` 缺任一難度行（F~SSS 9 種）→ `GetFactionScoreDelta` 回傳 `0`，等同全 0 權重；(d) FT-09 owner 二表任一檔案不存在 |
| **行為** | `_isEnabled = false`，FT-09 進入降級模式：不訂閱 `OnMissionResolved`、不推送 C-06、所有對外 API 回傳降級值 |
| **玩家可見** | 核心循環不受影響；劇情委託永不出現於委託板；P-02 對話視窗不會彈出 |
| **規範源** | §3.1.1 啟用條件 + §3.1.3 降級行為總表 + §3.2.x 各表驗證規則 |

---

<a id="appendix-b-2"></a>
### B.2 EC-2：runtime 收到未知 factionID 的 OnMissionResolved

| 項目 | 內容 |
|---|---|
| **觸發** | `outcome.missionFactionID` 不在 `FactionRouteTable` 註冊（C-01 fallback 失效或表異動） |
| **行為** | `Debug.LogWarning("OnMissionResolved with unknown factionID={id}, ignored")`，早退；不更新分數、不發 `OnFactionScoreChanged`、不推 C-06、不檢查階段解鎖 |
| **玩家可見** | 該次任務結算對陣營分數無貢獻；無錯誤訊息（僅開發者 console 警告） |
| **規範源** | §3.3.2 Step 1（雙重保險檢查） + §3.3.3 過濾規則總表 |

---

<a id="appendix-b-3"></a>
### B.3 EC-3：玩家未確認對話直接關閉遊戲

| 項目 | 內容 |
|---|---|
| **觸發** | 階段已解鎖、`_pendingDialogueStages` 含未確認 stageID、玩家不點 P-02 對話視窗確認鍵直接退出遊戲 |
| **行為** | SaveData 持久化 `_pendingDialogueStages`；下次 Bootstrap §3.1.2 Step F 對 queue 內每個 stageID 重發 `OnFactionStoryStageUnlocked` 事件，P-02 重新彈出對話視窗 |
| **玩家可見** | 重啟遊戲後對話視窗自動彈出，順序與離線前一致（FIFO） |
| **規範源** | §3.1.2 Step F 補發 + §3.4.5 入隊規則 + §3.5.4 失敗路徑表 |

---

<a id="appendix-b-4"></a>
### B.4 EC-4：單次分數變動同 frame 跨越多階段

| 項目 | 內容 |
|---|---|
| **觸發** | 單次 `OnMissionResolved` 帶來的 `delta` 使 `newScore` 同時跨越 ≥ 2 個階段 threshold（例：SSS +30 跨 stage 2/3） |
| **行為** | §3.4.2 Step 3 線性掃描所有達標階段，依 `stageIndex` 升序逐個解鎖、逐個入隊、逐個發 `OnFactionStoryStageUnlocked`；不批次合併 |
| **玩家可見** | P-02 依 FIFO 順序連續彈出多個對話視窗；玩家須依序確認；確認順序錯誤回 `INVALID_STAGE_ID`（EC-8） |
| **規範源** | §3.4.3 同 frame 多階段解鎖 + §3.7.2 事件發布順序保證 |

---

<a id="appendix-b-5"></a>
### B.5 EC-5：劇情委託派遣失敗 / 冒險者死亡

| 項目 | 內容 |
|---|---|
| **觸發** | 玩家派遣的劇情委託（`categoryID = 3`）結算 `outcome.isSuccess == false` 或 `outcome.isDead == true` |
| **行為** | `_unlockedStageIndices[factionID]` **不回退**；下一階段解鎖判定仍由分數累積驅動（不重做本階段）；發 `OnFactionStoryStageResolved(isSuccess=false, isDead=...)` 供 P-02 播放敗北 epilogue |
| **玩家可見** | 失敗的劇情委託不阻擋後續階段；epilogue 對話可呈現「失敗結局」分支文本（由 P-02 + DialogueTable 決定） |
| **規範源** | §3.6.1 推進語意（不可逆） + §3.6.2 OnFactionStoryStageResolved 事件 |

---

<a id="appendix-b-6"></a>
### B.6 EC-6：玩家全程忽略劇情委託，持續累積分數

| 項目 | 內容 |
|---|---|
| **觸發** | 玩家持續派遣常規任務，分數累積跨越所有階段 threshold，但不對任一階段對話視窗按確認 / 不接任一劇情委託 |
| **行為** | `_pendingDialogueStages` 持續入隊（無上限）；`_staticMissionPool` 內委託僅在玩家確認對話後注入，故對話未確認前不會出現於委託板；所有階段事件已發布 |
| **玩家可見** | 對話視窗依 P-02 策略堆疊或排程顯示；玩家可任意時刻處理；不接劇情委託不影響核心循環 |
| **規範源** | §3.6.6 玩家行為自由度表 + §3.5.5 雙軌時序（T6 → T7 完全由玩家節奏決定） |

---

<a id="appendix-b-7"></a>
### B.7 EC-7：FT-02.InjectStaticMission 注入失敗

| 項目 | 內容 |
|---|---|
| **觸發** | `ConfirmDialogue` Step 4 呼叫 FT-02 回傳非 `OK`（`UNKNOWN_MISSION_ID` / `WRONG_CATEGORY` / `BOARD_DISABLED` 等） |
| **行為** | `Debug.LogError`，`ConfirmDialogue` 回傳 `INJECT_FAILED`；**不出隊**（`_pendingDialogueStages` 保留 stageID）；不發 `OnFactionStoryDialogueConfirmed` 事件 |
| **玩家可見** | P-02 對話視窗應顯示錯誤訊息或保持開啟；玩家下次點確認可重試（極罕見路徑，通常代表資料表異動或 FT-02 自身故障） |
| **規範源** | §3.5.2 ConfirmDialogue Step 4 + §3.5.4 失敗路徑表 |

---

<a id="appendix-b-8"></a>
### B.8 EC-8：P-02 順序錯誤呼叫 ConfirmDialogue

| 項目 | 內容 |
|---|---|
| **觸發** | P-02 呼叫 `ConfirmDialogue(stageID)` 時，`stageID != _pendingDialogueStages.Peek()`（隊首不匹配） |
| **行為** | `Debug.LogWarning("ConfirmDialogue out-of-order: expected={expected}, got={got}")`，回傳 `INVALID_STAGE_ID`；**不出隊**、不注入委託 |
| **玩家可見** | 對話視窗應依正確順序呼叫；若 P-02 實作正確，此案例不會發生（防禦性檢查） |
| **規範源** | §3.5.2 ConfirmDialogue Step 2 + §3.5.4 失敗路徑表 |

---

<a id="appendix-b-9"></a>
### B.9 EC-9：劇情委託本身計分跨越下一階段 threshold

| 項目 | 內容 |
|---|---|
| **觸發** | 階段 N 解鎖、玩家派遣階段 N 劇情委託成功、其加分 (`C-01.GetFactionScoreDelta(difficulty)`) 直接使 `newScore >= scoreThreshold(N+1)` |
| **行為** | 同 frame 內：(a) §3.3.2 Step 4 發 `OnFactionScoreChanged`；(b) §3.3.2 Step 5 觸發 `CheckStageUnlock` 立即解鎖階段 N+1；(c) §3.6.2 Step 9 發 `OnFactionStoryStageResolved`（針對階段 N）；(d) §3.6.3 路線完結判定 |
| **玩家可見** | 階段 N 結算面板 + 階段 N+1 解鎖對話視窗緊接出現；節奏感依 §3.6.7 buffer（F-4 公式）規劃決定是否合理 |
| **規範源** | §3.6.7 階段計分回流 + §3.7.2 事件發布順序 + 設計師責任由 F-4 / §7 規範 |

---

<a id="appendix-b-10"></a>
### B.10 EC-10：SaveData 反序列化異常 / 欄位缺失

> §3 未明確規範。此 EC 補強 SaveData 升級 / 損壞場景的契約。

**觸發條件**：

- (a) 舊存檔未含 `FactionStorySaveData` 區塊（玩家從 FT-09 上線前的版本升級）
- (b) `FactionStorySaveData` 區塊存在但欄位部分缺失（如 `routeCompletedFlags == null`）
- (c) 反序列化拋例外（JSON 格式損壞、Dictionary key 型別不符等）
- (d) `_factionScores` 含未在 `FactionRouteTable` 註冊的 factionID（資料表縮減後載入舊存檔）
- (e) `_unlockedStageIndices[factionID]` 值大於該陣營實際 `StoryStageTable` 的最大 `stageIndex`（資料表縮減）

**系統行為**：

| 子場景 | 行為 |
|---|---|
| (a) 整區塊缺失 | Bootstrap §3.1.2 Step D 將所有狀態欄位初始化為**空容器**（`new Dictionary` / `new Queue` / `new HashSet`），等同首次遊玩；不阻擋 Bootstrap |
| (b) 部分欄位 null | 對該欄位執行 `?? new <DefaultContainer>()`（§3.1.2 Step D 已採用 `?? new` 模式）；其餘欄位正常還原 |
| (c) 反序列化例外 | 整個 `FactionStorySaveData` 視為 (a) 處理；`Debug.LogError("FactionStorySaveData deserialization failed: {ex}")`，不傳遞例外至上層（FT-09 Bootstrap 對全局存檔系統不可中斷） |
| (d) 未知 factionID | 在 `_factionScores` / `_unlockedStageIndices` / `_routeCompletedFlags` 載入後，**過濾掉未註冊的 factionID**（`Debug.LogWarning("Discarded stale factionID={id} from save data")`）；保留有效 key |
| (e) stageIndex 超出範圍 | `_unlockedStageIndices[factionID] = min(savedValue, maxStageIndex)`（夾擠至有效範圍）；`Debug.LogWarning` |

**玩家可見**：

- (a) 升級舊存檔：FT-09 從零開始累積，玩家視為「劇情系統首次出現」；無中斷
- (b)~(c)：與 (a) 等價，從零開始
- (d)~(e)：玩家可能損失部分劇情進度（被縮減的陣營 / 階段對應的解鎖狀態消失），但核心循環與其他陣營進度不受影響

**規範細節**：

- **不阻擋 Bootstrap 的設計動機**：FT-09 是 Jam 後段功能，玩家的核心進度（公會、冒險者、金幣）由其他系統管轄；FT-09 SaveData 損壞不應導致整存檔不可用
- **無自動修復**：FT-09 不嘗試「補回」缺失資料（如反推 `_factionScores` 應有值）；資料損壞即重置
- **持久化版本欄位（Post-Jam）**：Jam 版不引入 `version: int` 欄位；若 Post-Jam 需 schema 演進（如新增欄位且需特殊遷移），再導入 versioning 機制

---

<a id="appendix-b-11"></a>
### B.11 EC-11：路線完結後的後續結算行為

> §3 未明確規範。此 EC 釐清玩家在路線完結（已發 `OnFactionRouteCompleted`）後仍可能發生的計分 / 事件鏈行為。

**觸發條件**：

- 該 `factionID` 已完結（`_routeCompletedFlags.Contains(factionID) == true`）
- 玩家後續仍派遣該陣營的常規任務（`MissionTemplate.factionID == factionID` 且 `categoryID != 3`）並成功結算

**系統行為**：

| 子流程 | 行為 |
|---|---|
| 分數累積（§3.3.2） | **正常執行**：`_factionScores[factionID]` 持續增加，`OnFactionScoreChanged` 持續發布 |
| 階段解鎖判定（§3.4.2） | **執行但不解鎖**：線性掃描 `stageIndex > currentMaxIndex` 找不到任何階段（`currentMaxIndex == totalStages`），迴圈空轉、無新事件 |
| C-06 推送（§3.3.5） | **正常執行**：`GetMaxFactionScore()` 持續更新並推送 C-06；分數仍可能影響世界危險度（C-06 自行依其 threshold 處理） |
| OnFactionStoryStageResolved（§3.6.2） | **不發生**：路線完結後不會再有 `categoryID = 3` 任務（最後一個劇情委託已派遣完畢且從 `_staticMissionPool` 移除） |
| OnFactionRouteCompleted 重發 | **絕不重發**：`_routeCompletedFlags` 防護（§3.6.3 / §3.6.4） |

**玩家可見**：

- 該陣營分數可繼續上漲，但無新劇情解鎖（已完結的視覺反饋由 P-02 自行處理，如分數面板灰階 / 顯示「已完結」）
- C-06 世界危險度可能繼續攀升（依 C-06 的 threshold 設計）
- 不會再彈出 FT-09 任何對話視窗或路線完結通知

**規範細節**：

- **設計動機**：分數累積邏輯與階段解鎖邏輯解耦——前者持續為 C-06 提供訊號，後者由「是否還有未解鎖階段」自然守門
- **Post-Jam 擴充提示**：若需「完結後切換為 endgame 劇情」（如解鎖 epilogue chapter），需新增 `endgameStageIndex` 概念或第二批 `StoryStageTable` 記錄；Jam 版範疇不涵蓋
- **與 EC-9 的差異**：EC-9 是「劇情委託跨越下一 threshold」的同 frame 解鎖；EC-11 是「無下一階段可解鎖」的空轉場景

---

<a id="appendix-b-12"></a>
### B.12 EC-12：FT-09 Bootstrap 與依賴系統的時序保證

> §3 未明確規範跨系統 Bootstrap 順序。此 EC 釐清 FT-09 對前置系統的時序假設與失準後行為。

**觸發條件**：

- (a) FT-09 Bootstrap 時 `DataManager` 尚未 `Initialize()` 完畢（`Get<FactionRouteData>` 等查詢回 null）
- (b) FT-09 Bootstrap 時 `EventBus` 尚未就緒（`Subscribe` 拋例外）
- (c) FT-09 Bootstrap 完成前 FT-04 已發布 `OnMissionResolved`（無訂閱者，事件遺失）
- (d) FT-09 Bootstrap 完成前 P-02 嘗試呼叫 `ConfirmDialogue` / `IsFactionStoryEnabled`

**系統行為**：

| 子場景 | 行為 |
|---|---|
| (a) DataManager 未就緒 | `DataManager.Get<T>()` 回 null → §3.1.1 啟用判定為 false → `_isEnabled = false`（降級）；`Debug.LogError("FT-09 Bootstrap before DataManager ready, degraded")` |
| (b) EventBus 未就緒 | §3.1.2 Step C 拋例外 → 由全局 Bootstrap 統籌器捕捉並重試或停機；FT-09 不自行處理（屬全域基礎設施失誤） |
| (c) FT-04 事件早於 FT-09 訂閱 | 事件遺失，該次結算不計分；可接受（玩家無感，下次結算正常）。Bootstrap 順序契約應在全局 Bootstrap 統籌器規範 |
| (d) FT-09 API 早於 Bootstrap 呼叫 | `_isEnabled` 預設 `false` → 走降級路徑（`IsFactionStoryEnabled` 回 false / `ConfirmDialogue` 回 `STORY_SYSTEM_DISABLED`）；不拋例外 |

**玩家可見**：

- 正常 Bootstrap 順序下，玩家無感
- 異常順序下：FT-09 進入降級或事件遺失；不影響其他系統運作

**規範細節**：

- **時序契約（informational, 跨系統）**：全局 Bootstrap 順序須保證 `F-01 DataManager → 全域 EventBus → 各 Gameplay 系統（含 FT-04 / FT-09 / C-06）`；具體順序由 F-01 / 全域 Bootstrap 文件規範，FT-09 不重複定義
- **FT-09 內部防禦**：所有 API 預設返回降級值（§3.1.3）；Bootstrap 失敗不傳遞例外至上層
- **不重發 Bootstrap 前遺失的事件**：FT-04 不快取「歷史 outcome」；EC-12(c) 場景的遺失視為可接受（Jam 版不引入事件 replay 機制）
- **跨系統依賴責任歸屬**：Bootstrap 順序屬 F-01 / Bootstrap 統籌器職責；FT-09 GDD 此處僅文件化「假設」與「假設失準時的本系統行為」

---

