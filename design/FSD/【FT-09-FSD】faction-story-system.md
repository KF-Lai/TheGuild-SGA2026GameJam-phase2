# 【FT-09-FSD】功能規格說明書 — Faction Story System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-09】faction-story-system.md`（版本：2026-04-27） |
| 對應 Data-Specs | `【FT-09-DS】faction-route-table.md`（`FactionRouteTable.csv`，owner = FT-09）<br>`【FT-09-DS】story-stage-table.md`（`StoryStageTable.csv`，owner = FT-09）<br>`【C-01-DS】mission-difficulty-table.md`（消費端：`factionScoreDelta` 欄位，owner = C-01）<br>`【F-01-DS】system-constants.md`（消費端：`FACTION_NEUTRAL_ID`） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體（Opus 4.7 + xhigh） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-28 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-09 Faction Story System 將 GDD 規範的「線性陣營劇情」轉譯為一個 Bootstrap-once、事件驅動的 Gameplay 服務：訂閱 FT-04 `OnMissionResolved` 累積 `_factionScores`，於 threshold 達標時解鎖 `StoryStageTable` 階段並透過「對話 → 委託」雙軌呈現（事件給 P-02、`InjectStaticMission` 推給 FT-02），同時將任意陣營當前最高分推送給 C-06。所有狀態由 ISaveable 持久化、降級行為一致、無 runtime 啟停切換。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：
- 1 條陣營路線（Jam）+ 多陣營邏輯（Post-Jam 即用，無 runtime 改動）
- 訂閱 `OnMissionResolved`、過濾 / 計分 / 階段解鎖判定 / 雙軌呈現 / 路線完結
- 5 個對外發布事件 + 7 個對外 API + 1 個直接呼叫 C-06 / 1 個直接呼叫 FT-02
- ISaveable 持久化（4 個 runtime 容器序列化為 List 中介結構）
- 啟停二元判定 + 降級行為（API、事件、推送、流程、SaveData 全面對齊）
- 12 條邊緣案例（EC-1~EC-12）程式對策
- F-1~F-3 三條 runtime 公式實作

**Out-of-Scope**（明確排除，對應 GDD §8.5）：
- 第 2 條陣營路線啟用、跨陣營對抗、多分支劇情樹
- FT-08 / FT-12 職員陣營傾向 runtime 消費（schema 預留，FT-09 端零依賴）
- 接受時計分（`OnCommissionAccepted` 不訂閱）
- 階段委託特殊規則（雙重保險、必須特定冒險者）/ 階段任務過期
- Runtime 啟用/停用熱切換、事件 replay、SaveData versioning
- F-4 / F-5 設計師 offline 工具公式（不在 runtime 執行，不實作為程式碼）
- P-02 對話視窗 UI / P-03 桌面通知（FT-09 僅發事件，UI 由 P-02 / P-03 自行實作）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準（AC-F-1~AC-F-8 / AC-EC-1~AC-EC-12 / AC-D-1~AC-D-9 / AC-T-1~AC-T-4），程式可驗證條件如下：

| DoD | 說明 | 驗證方式 |
| --- | --- | --- |
| DoD-01 | CSV 完整 → `IsFactionStoryEnabled() == true`；任一表缺失 / 全 neutral / `MissionDifficultyTable` 缺難度 → `false` 並降級 | EditMode test：mock 三種 CSV 場景斷言 `_isEnabled` |
| DoD-02 | 成功 + `factionID != 0` 計分；neutral / 失敗 / 未知 factionID 一律不計分、不發 `OnFactionScoreChanged` | EditMode：注入 mock `OnMissionResolved` 三組 outcome，斷言 `_factionScores` 與事件計數 |
| DoD-03 | 線性掃描升序解鎖；同 frame 跨多階段時 FIFO 入隊；已解鎖階段不重複觸發 | EditMode：場景 (a)(b)(c) 對應 AC-F-3，斷言事件序列與 queue 狀態 |
| DoD-04 | `ConfirmDialogue` 隊首匹配 → `InjectStaticMission` → 出隊 → 發 `OnFactionStoryDialogueConfirmed`；不匹配 / FT-02 失敗均不出隊 | EditMode：mock FT-02 三組回應，斷言 result enum 與 queue 大小 |
| DoD-05 | 劇情委託失敗 / 死亡時 `_unlockedStageIndices` 不回退；發 `OnFactionStoryStageResolved(isSuccess=false, isDead=true)` | EditMode：對應 AC-F-5 |
| DoD-06 | 路線完結事件一次性；重啟 Bootstrap 不重發 `OnFactionRouteCompleted` | EditMode：對應 AC-F-6，重啟後 EventBus 計數 = 0 |
| DoD-07 | C-06 在 Bootstrap 後與每次成功計分後各收到 1 次 `OnFactionScoreUpdated`；降級時不呼叫 | EditMode：mock C-06，記錄呼叫歷史 |
| DoD-08 | SaveData 4 欄位完整序列化／反序列化；Bootstrap Step F 對 `_pendingDialogueStages` 內每個 stageID 重發事件 | EditMode：對應 AC-F-8，JSON snapshot + 重啟事件計數 |
| DoD-09 | EC-1~EC-12 全部 12 條對策對應 §7 對策表落地 | EditMode + PlayMode 混合 |
| DoD-10 | 5 Script 結構落地、無 Find / FindObjectOfType、無寫死 `factionScoreDelta` / `scoreThreshold` / `FACTION_NEUTRAL_ID` | Code review + grep |
| DoD-11 | EventBus 訂閱 `OnMissionResolved` / 解除訂閱對稱（OnEnable / OnDisable 或對應生命週期） | Code review |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要、§3.1（啟停與降級）、§3.2（資料表 schema）、§3.3（分數累積管線）、§3.4（階段解鎖判定）、§3.5（雙軌呈現流程）、§3.6（階段推進規則）、§3.7（API / 事件 / runtime 狀態）
- §4（公式 F-1~F-5；F-1~F-3 為 runtime，F-4 / F-5 為 offline 工具）
- §5（邊緣案例 EC-1~EC-12，附錄 B 詳述）
- §6（依賴；§6.1 上游 5 條、§6.2 下游 5 條、§6.3 雙向契約 10 條、§6.5 ISaveable 契約）
- §7（可調參數 A1~A5 + C1~C3）
- §8（驗收標準）

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-09-DS】faction-route-table.md` | `FactionRouteTable.csv` | `factionID`、`name`、`description` | §3.2.1 陣營路線；FT-09 owner |
| `【FT-09-DS】story-stage-table.md` | `StoryStageTable.csv` | `stageID`、`factionID`、`stageIndex`、`scoreThreshold`、`missionID`、`dialogueKey` | §3.2.2 劇情階段；FT-09 owner |
| `【C-01-DS】mission-difficulty-table.md` | `MissionDifficultyTable.csv` | `factionScoreDelta`（消費端） | §3.2.3 / §3.3.2 分數加權；owner = C-01，FT-09 透過 `IMissionDatabaseService.GetFactionScoreDelta(difficulty)` 消費 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `FACTION_NEUTRAL_ID`（消費端） | §3.2.1 / §3.2.2 / §3.3.2 過濾 neutral 任務；owner = F-01 |

### 2.3 上游依賴系統

引用 `IXxxService` 為 FSD 敘述形式；實作契約對齊 FSD-index §2.10 — 直接呼叫對應 concrete singleton（`DataManager.Instance` / `MissionDatabaseService.Instance` / `MissionDispatchService.Instance` / `WorldDangerService.Instance` 等），不新增 interface 包裝層。

| 上游系統 | 接觸點 | 對端對應章節 |
| --- | --- | --- |
| **F-01 DataManager** | `IDataManager.Get<FactionRouteData>()` / `Get<StoryStageData>()` / `Initialize()` 完成判定（§3.1.1 啟用條件） | F-01 FSD §5.1（`Get<T>`） |
| **C-01 Mission Database** | `IMissionDatabaseService.GetFactionScoreDelta(string difficulty) : int`、`GetTemplate(int missionID) : MissionTemplate` | 【C-01-FSD】§5.1 line 199 / line 99（FT-09 已登記為消費端） |
| **FT-04 Outcome Resolution** | 訂閱事件 `OnMissionResolvedEvent { Outcome outcome }`；消費欄位 `missionFactionID`、`isSuccess`、`missionDifficulty`、`missionID`、`isDead` | 【FT-04-FSD】§5.2 / §5.3（`Outcome` 含 5 欄位） |
| **FT-02 Mission Dispatch** | `IMissionDispatchService.InjectStaticMission(int missionID) : InjectStaticMissionResult` | 【FT-02-FSD-A】/【FT-02-FSD-B】（已暴露） |
| **F-02 Time System**（間接） | 不直接消費；`OnMissionResolved` 由 FT-04 經 F-02 推進結算後發布，FT-09 被動接收 | — |
| **EventBus** | `EventBus.Subscribe<OnMissionResolvedEvent>(handler)` / `Publish<T>` | static EventBus |
| **SystemConstants** | `FACTION_NEUTRAL_ID = 0`（透過 `DataManager.GetSystemConstantInt("FACTION_NEUTRAL_ID")` 取得） | F-01 FSD |

> 引用 service interface 命名僅為敘述方便；實作 PR 直接呼叫 concrete singleton（FSD-index §2.10）。

### 2.4 下游被依賴系統

| 下游系統 | 消費契約 | 用途 |
| --- | --- | --- |
| **C-06 World Danger System** | FT-09 主動呼叫 `WorldDangerService.OnFactionScoreUpdated(int newMaxScore)`（直接 API，不走 EventBus） | C-06 §3.3 / §4.5 陣營閘判定（C-06 FSD §5.4 觸發點 5） |
| **P-02 Main UI Framework**（待設計） | 訂閱 5 事件（§2.5）+ 呼叫 `IFactionStoryService.ConfirmDialogue(stageID)` + 呼叫 `MissionDispatchService.Dispatch(...)` | 對話視窗 / 委託板 / 結算面板 / 路線完結 UI |
| **P-03 Notification System**（待設計、可選） | 可選訂閱 4 事件（`OnFactionStoryStageUnlocked` / `OnFactionStoryDialogueConfirmed` / `OnFactionStoryStageResolved` / `OnFactionRouteCompleted`） | 桌面 / 系統通知推播 |
| **FT-08 Gacha System**（Jam 無 runtime 連接） | `StaffGachaPoolTable` 5 個預留閘 schema；Post-Jam 才啟用 | — |
| **FT-12 Staff System**（Jam 無 runtime 連接） | `StaffTable.factionID` schema 預留 | — |
| **FT-10 Save/Load System** | FT-09 實作 `ISaveable`（OwnerKey = `"factionStorySaveData"`、IsCritical = `false`） | FT-10 拓撲序列化 row 10 / Degradable 分類 |

### 2.5 跨系統事件契約

| 事件 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMissionResolvedEvent` | 訂閱（FT-04 → FT-09） | `Outcome outcome`（含 5 個消費欄位） | FT-04 結算管線 step 11；FT-09 `_isEnabled == true` 時訂閱（§3.1.2 Step C） |
| `OnFactionScoreChangedEvent` | 發布（FT-09 → P-02 可選） | `int factionID, int oldScore, int newScore, int delta` | §3.3.2 Step 4；分數實際變動時發（filter pass 後） |
| `OnFactionStoryStageUnlockedEvent` | 發布（FT-09 → P-02 / P-03） | `int stageID, int factionID, int stageIndex, int missionID, string dialogueKey` | §3.4.2 Step 3 解鎖 / §3.1.2 Step F Bootstrap 補發 |
| `OnFactionStoryDialogueConfirmedEvent` | 發布（FT-09 → P-02 / P-03） | `int stageID, int factionID, int missionID` | §3.5.2 Step 6；玩家確認對話且 FT-02 注入成功後 |
| `OnFactionStoryStageResolvedEvent` | 發布（FT-09 → P-02 / P-03） | `int stageID, int factionID, int stageIndex, int missionID, bool isSuccess, bool isDead` | §3.6.2 Step 9；劇情委託（categoryID=3）結算後 |
| `OnFactionRouteCompletedEvent` | 發布（FT-09 → P-02 / P-03） | `int factionID, int finalStageID, bool finalIsSuccess, int totalStages` | §3.6.3；最末階段結算且 `_routeCompletedFlags` 未含此 factionID |

> 訂閱 / 發布順序保證：同一 `OnMissionResolved` 觸發鏈內依序為 `OnFactionScoreChanged` → `OnFactionStoryStageUnlocked`（若解鎖）→ `OnFactionStoryStageResolved`（若 categoryID=3）→ `OnFactionRouteCompleted`（若最末階段）。EventBus 序列化發布保證跨 frame 順序。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家於日常審核 / 派遣 / 結算的決策中累積陣營傾向，在某個結算之後突然彈出對話視窗——「我做的每件事原來都被記下了」。揭曉時刻的對話張力 + 委託板上紅框任務的視覺差異，雙軌呈現「世界回應」與「玩法閉環」。

### 3.2 系統目的還原

FT-09 是公會的線性陣營劇情系統，透過事件驅動將「結算 → 計分 → 解鎖 → 雙軌呈現 → 階段推進」串成不可逆的編年史敘事。被動驅動 + 主動回應，玩家節奏完全自定，FT-09 不催促也不阻擋核心循環。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 沉默累積中的命運推力 | 玩家未感知具體分數但持續推進；某個結算後對話視窗彈出 | `FactionStoryScoreAccumulator.HandleOnMissionResolved` 訂閱 `OnMissionResolvedEvent`，於同 frame 內計分 → 階段判定 → 發 `OnFactionStoryStageUnlockedEvent`；不暴露分數 UI 屬 P-02 範疇 |
| 揭曉時刻的張力（對話視窗） | 普通結算後突然彈出非日常的對話框 | `FactionStoryService.ConfirmDialogue` + `OnFactionStoryStageUnlockedEvent` payload 帶 `dialogueKey` 給 P-02 自行讀 DialogueTable |
| 揭曉時刻的張力（委託板視覺差異） | 委託板出現紅框 / 不同顏色任務 | `MissionDispatchService.InjectStaticMission(missionID)` 於 FT-02 端發 `OnCommissionPosted(source=Static)`，P-02 依 `source` 呈現視覺差異（FT-02 端職責） |
| 結局揭曉的牽引 | 完成劇情委託後播 epilogue / 路線完結時最終回顧 | `OnFactionStoryStageResolvedEvent`（含 `isSuccess` / `isDead`）+ `OnFactionRouteCompletedEvent`（一次性）給 P-02 |
| 線性而非分支 | 玩家無多線敘事決策、對話視窗順序固定 | `_pendingDialogueStages: Queue<int>` FIFO + `_unlockedStageIndices: Dictionary<int,int>` 單調遞增；不重複解鎖 |
| 失敗不扣分的累積感 | 玩家失敗後分數不退回、階段不重置 | `HandleOnMissionResolved` 過濾 `isSuccess == false` 早退；`_unlockedStageIndices` 寫入後不可逆 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（單一 FSD，5 Script）。

### 4.2 拆分理由

> 本 FSD 未拆分為 A/B；§4.3 略過。

不拆分判斷依據（對齊 FSD-index §2.4）：

- **單檔規模**：5 Script 合計預估 1000~1200 行；單一 Script 最大為 `FactionStoryService.cs`（350~400 行），未越 500 行拆分門檻
- **職責邊界不分明**：GDD §3.3（分數累積）/ §3.4（階段解鎖）/ §3.5（雙軌呈現）/ §3.6（階段推進）四節共用 `_factionScores` / `_unlockedStageIndices` / `_pendingDialogueStages` / `_routeCompletedFlags` 四個跨章節 runtime 狀態，且 §3.3 Step 6（推 C-06）與 §3.6 Step 7~10（路線完結）皆嵌入 `HandleOnMissionResolved` 同一函式——強行拆 FSD 會把同一函式切碎、跨單元同步成本反而上升
- **對齊既有風格**：FT-06（5 Script）、FT-07（4 Script）、FT-08（5 Script）皆採「未拆分 FSD + 多 Script」處理；FT-02 因「dispatch core / commission board 兩個獨立池狀態」才拆 A/B。FT-09 無此邊界
- **Codex 實作粒度**：5 Script 各 SRP 清晰，無單檔肥大；介面 + 數據型別 + Loader + Accumulator + 主服務的分工符合 4~5 個 Codex 子工項的批次發包顆粒度

### 4.3 拆分結果

未拆分（略）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `FactionStoryTypes.cs` | `Assets/Scripts/Gameplay/FactionStory/FactionStoryTypes.cs` | 集中宣告 5 個事件 struct、`ConfirmDialogueResult` enum、SaveData entry struct（`FactionScoreEntry` / `StageIndexEntry` / `FactionStorySaveData`）與 `FACTION_STORY_CATEGORY_ID = 3` 常數 | `System` | 200~250 行 |
| `IFactionStoryService.cs` | `Assets/Scripts/Gameplay/FactionStory/IFactionStoryService.cs` | 定義 7 個對外 API 簽名（§5.1），供 Service Locator / 測試 mock 使用 | — | < 50 行 |
| `FactionStoryTableLoader.cs` | `Assets/Scripts/Gameplay/FactionStory/FactionStoryTableLoader.cs` | 載入 `FactionRouteData` / `StoryStageData`、執行 §3.2.1~§3.2.2 驗證、建立 `GetByFactionID(int)` / `GetByMissionID(int)` 索引 | `IDataManager` | 200~250 行 |
| `FactionStoryScoreAccumulator.cs` | `Assets/Scripts/Gameplay/FactionStory/FactionStoryScoreAccumulator.cs` | §3.3 計分管線（Step 1~6）+ §3.4 階段解鎖判定（CheckStageUnlock 線性掃描）；持有 `_factionScores` / `_unlockedStageIndices` / `_pendingDialogueStages` 但不直接訂閱事件 | `IMissionDatabaseService`、`FactionStoryTableLoader`、`EventBus`、`IWorldDangerService` | 200~250 行 |
| `FactionStoryService.cs` | `Assets/Scripts/Gameplay/FactionStory/FactionStoryService.cs` | Bootstrap §3.1.2、訂閱 / 解除 `OnMissionResolvedEvent`、實作 7 個對外 API（含 `ConfirmDialogue` §3.5.2）、§3.6 階段推進與路線完結、`ISaveable` 持久化 | `FactionStoryTableLoader`、`FactionStoryScoreAccumulator`、`IMissionDispatchService`、`IMissionDatabaseService`、`IWorldDangerService`、`EventBus` | 350~400 行 |

### 4.5 類別關係（可選）

```
                                   ┌─────────────────────────────┐
                                   │   IFactionStoryService      │
                                   │   (interface, 7 APIs)        │
                                   └──────────────┬──────────────┘
                                                  │ implements
                                                  ▼
   ┌──────────────────────────────────────────────────────────────────┐
   │                  FactionStoryService (singleton)                 │
   │  - Bootstrap / Subscribe / ConfirmDialogue / ISaveable           │
   │  - Owns runtime state via Accumulator                            │
   └─┬─────────────────┬─────────────────┬─────────────────┬──────────┘
     │                 │                 │                 │
     ▼                 ▼                 ▼                 ▼
 TableLoader     ScoreAccumulator     EventBus       MissionDispatch
 (load CSV)      (calc + check)       (sub/pub)       (InjectStatic)
                       │
                       ▼
                  WorldDanger
                  (OnFactionScoreUpdated)
                       │
                       ▼
                  MissionDatabase
                  (GetFactionScoreDelta / GetTemplate)

 [Types] FactionStoryTypes.cs：所有事件 struct / enum / SaveData entry / const
```

`FactionStoryService` 是唯一對外的 singleton；`Accumulator` 為其組合成員（持有計分相關 runtime 狀態）；`TableLoader` 為內部 helper。三者構成 SRP 清晰的內聚單元。

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

對應 GDD §3.7.1。所有 API 暴露於 `IFactionStoryService`，由 `FactionStoryService.Instance` 實作。

| API 簽名 | 用途 | 降級時行為 |
| --- | --- | --- |
| `bool IsFactionStoryEnabled()` | 查詢 `_isEnabled` | return `false` |
| `int GetCurrentFactionScore(int factionID)` | 查 `_factionScores[factionID]`，未登記回 `0` | return `0` |
| `int GetMaxFactionScore()` | F-2 公式（§3.3.4） | return `0` |
| `int GetUnlockedStageIndex(int factionID)` | 查 `_unlockedStageIndices[factionID]`，未登記回 `0` | return `-1`（區分降級與「未解鎖」） |
| `ConfirmDialogueResult ConfirmDialogue(int stageID)` | §3.5.2 雙軌切換 | return `STORY_SYSTEM_DISABLED` |
| `int GetPendingDialogueCount()` | 取 `_pendingDialogueStages.Count`（給 P-02 顯示徽章） | return `0` |
| `bool IsRouteCompleted(int factionID)` | 查 `_routeCompletedFlags.Contains(factionID)` | return `false` |

**內部呼叫（非 IFactionStoryService 暴露，但實作於 service）**：
- `Bootstrap(SaveData? saveData)`：由 FT-10 / 全域 Bootstrap 統籌器在 DataManager / EventBus 就緒後呼叫一次（§3.1.2）
- `Shutdown()`：`OnDisable` / 應用程式結束時呼叫，解除 `OnMissionResolvedEvent` 訂閱（對稱原則）
- `Serialize() : string` / `RestoreFromSave(string ownerJson)` / `InitializeAsNewGame()`：ISaveable 介面（FT-10 §3.6.1 規範；確切簽名待 FT-10 FSD 定案，本 FSD §8.3 B-04 登記）

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnMissionResolvedEvent` | 訂閱（FT-04 → FT-09） | `Outcome outcome` | FT-04 §3.8 結算完成；`_isEnabled == true` 時訂閱（§3.1.2 Step C） |
| `OnFactionScoreChangedEvent` | 發布（→ P-02 可選） | `int factionID, int oldScore, int newScore, int delta` | §3.3.2 Step 4，計分實際變動 |
| `OnFactionStoryStageUnlockedEvent` | 發布（→ P-02 / P-03） | `int stageID, int factionID, int stageIndex, int missionID, string dialogueKey` | §3.4.2 Step 3 / §3.1.2 Step F |
| `OnFactionStoryDialogueConfirmedEvent` | 發布（→ P-02 / P-03） | `int stageID, int factionID, int missionID` | §3.5.2 Step 6 |
| `OnFactionStoryStageResolvedEvent` | 發布（→ P-02 / P-03） | `int stageID, int factionID, int stageIndex, int missionID, bool isSuccess, bool isDead` | §3.6.2 Step 9，`categoryID=3` 任務結算 |
| `OnFactionRouteCompletedEvent` | 發布（→ P-02 / P-03） | `int factionID, int finalStageID, bool finalIsSuccess, int totalStages` | §3.6.3，最末階段且 `_routeCompletedFlags` 未含 |

**直接 API 推送（非 EventBus 事件，§3.7.3 + C-06 FSD §5.4 觸發點 5 確認）**：

| 推送 | 目標 | 時機 |
| --- | --- | --- |
| `WorldDangerService.OnFactionScoreUpdated(int newMaxScore)` | C-06 | §3.1.2 Step E（Bootstrap 後即使全 0 也推一次）+ §3.3.2 Step 6（每次計分變動）|
| `MissionDispatchService.InjectStaticMission(int missionID)` | FT-02 | §3.5.2 Step 4（`ConfirmDialogue` 隊首匹配後）|

### 5.3 資料結構

定義於 `FactionStoryTypes.cs`：

```
// ─── 常數 ───
public const int FACTION_STORY_CATEGORY_ID = 3;   // GDD §3.2.2 / §3.6.2 Step 7

// ─── Result Enum ───
public enum ConfirmDialogueResult
{
    OK,                       // 注入成功，已出隊
    STORY_SYSTEM_DISABLED,    // _isEnabled == false
    INVALID_STAGE_ID,         // queue 空 / 隊首不匹配 / stage 不存在
    INJECT_FAILED             // FT-02 InjectStaticMission 回非 OK
}

// ─── 事件 Struct（皆 readonly struct）───
public readonly struct OnFactionScoreChangedEvent {
    public readonly int factionID, oldScore, newScore, delta;
    // ctor
}
public readonly struct OnFactionStoryStageUnlockedEvent {
    public readonly int stageID, factionID, stageIndex, missionID;
    public readonly string dialogueKey;
    // ctor
}
public readonly struct OnFactionStoryDialogueConfirmedEvent {
    public readonly int stageID, factionID, missionID;
    // ctor
}
public readonly struct OnFactionStoryStageResolvedEvent {
    public readonly int stageID, factionID, stageIndex, missionID;
    public readonly bool isSuccess, isDead;
    // ctor
}
public readonly struct OnFactionRouteCompletedEvent {
    public readonly int factionID, finalStageID, totalStages;
    public readonly bool finalIsSuccess;
    // ctor
}

// ─── SaveData Entry（Unity JsonUtility 中介層）───
[Serializable] public struct FactionScoreEntry { public int factionID; public int score; }
[Serializable] public struct StageIndexEntry   { public int factionID; public int stageIndex; }

[Serializable] public sealed class FactionStorySaveData {
    public List<FactionScoreEntry> factionScores;
    public List<StageIndexEntry>   unlockedStageIndices;
    public List<int>               pendingDialogueStages;
    public List<int>               routeCompletedFlags;
}
```

**Runtime 狀態欄位**（位於 `FactionStoryScoreAccumulator` + `FactionStoryService`，序列化時轉 List 中介）：

| 欄位 | 型別 | 持有者 | 用途 | 持久化 |
| --- | --- | --- | --- | --- |
| `_isEnabled` | `bool` | `FactionStoryService` | 啟用 flag | 否（Bootstrap 重算） |
| `_factionScores` | `Dictionary<int, int>` | `FactionStoryScoreAccumulator` | 各陣營當前分數 | 是 |
| `_unlockedStageIndices` | `Dictionary<int, int>` | `FactionStoryScoreAccumulator` | 各陣營已解鎖 stageIndex（單調遞增） | 是 |
| `_pendingDialogueStages` | `Queue<int>` | `FactionStoryScoreAccumulator` | 待確認 stageID（FIFO） | 是 |
| `_routeCompletedFlags` | `HashSet<int>` | `FactionStoryService` | 已發過 RouteCompleted 的 factionID | 是 |

### 5.4 內部資料流

**觸發點 1：Bootstrap（由全域 Bootstrap 統籌器 / FT-10 在 DataManager + EventBus 就緒後呼叫）**

```
GlobalBootstrap.OnFactionStoryBootstrap(saveData)
  → FactionStoryService.Bootstrap(saveData)
      ├─ Step A：TableLoader.Load()
      │     ├─ FactionRouteData = DataManager.Get<FactionRouteData>()
      │     └─ StoryStageData   = DataManager.Get<StoryStageData>()
      ├─ Step B：驗證啟用條件（§3.1.1）
      │     ├─ FactionRouteTable 至少 1 筆 factionID != FACTION_NEUTRAL_ID
      │     ├─ StoryStageTable 至少 1 筆 valid 記錄
      │     └─ MissionDifficultyTable 9 種難度齊全
      │           （透過 IMissionDatabaseService.GetFactionScoreDelta(d) for d in [F..SSS]
      │            連續 9 次呼叫，全 ≥ 0 始通過）
      ├─ if NOT _isEnabled: LogError + return  // 降級
      ├─ Step C：EventBus.Subscribe<OnMissionResolvedEvent>(HandleOnMissionResolved)
      ├─ Step D：還原 SaveData（若 saveData != null）
      │     ├─ _factionScores         ← saveData.factionScores         ?? new Dict
      │     ├─ _unlockedStageIndices  ← saveData.unlockedStageIndices  ?? new Dict
      │     ├─ _pendingDialogueStages ← saveData.pendingDialogueStages ?? new Queue
      │     └─ _routeCompletedFlags   ← saveData.routeCompletedFlags   ?? new HashSet
      │     （含 EC-10 過濾：未知 factionID / stageIndex 超範圍 → LogWarning + 過濾）
      ├─ Step E：WorldDangerService.OnFactionScoreUpdated(GetMaxFactionScore())
      └─ Step F：foreach stageID in _pendingDialogueStages:
                    EventBus.Publish(OnFactionStoryStageUnlockedEvent(...))
                    （讀回 StoryStageData.GetByStageID(stageID) 補齊 payload；
                     若 stage 已不存在 → LogError + 跳過該 stageID 但不出隊，待 ConfirmDialogue 再處理）
```

**觸發點 2：FT-04 結算事件（核心管線，§3.3.2 + §3.6.2）**

```
EventBus.Publish(OnMissionResolvedEvent { outcome })
  → FactionStoryService.HandleOnMissionResolved(outcome)
      │
      ├─ Step 1：過濾早退
      │     ├─ if outcome.missionFactionID == FACTION_NEUTRAL_ID → return
      │     ├─ if !outcome.isSuccess                              → goto Step 7（仍走劇情委託判定）
      │     └─ if !TableLoader.HasFaction(outcome.missionFactionID)
      │              → LogWarning + goto Step 7
      │
      ├─ Step 2：取 delta
      │     delta = MissionDatabaseService.GetFactionScoreDelta(outcome.missionDifficulty)
      │     if delta == 0 → goto Step 7（仍走劇情委託判定）
      │
      ├─ Step 3：更新分數
      │     oldScore = _factionScores.GetValueOrDefault(outcome.missionFactionID, 0)
      │     newScore = oldScore + delta
      │     _factionScores[outcome.missionFactionID] = newScore
      │
      ├─ Step 4：EventBus.Publish(OnFactionScoreChangedEvent(...))
      │
      ├─ Step 5：CheckStageUnlock(outcome.missionFactionID, oldScore, newScore)
      │     │（位於 Accumulator）
      │     ├─ stages = TableLoader.GetByFactionID(factionID).OrderBy(stageIndex)
      │     ├─ currentMaxIndex = _unlockedStageIndices.GetValueOrDefault(factionID, 0)
      │     └─ foreach stage in stages where stage.stageIndex > currentMaxIndex:
      │             if newScore < stage.scoreThreshold → break
      │             _unlockedStageIndices[factionID] = stage.stageIndex
      │             _pendingDialogueStages.Enqueue(stage.stageID)
      │             EventBus.Publish(OnFactionStoryStageUnlockedEvent(...))
      │
      ├─ Step 6：WorldDangerService.OnFactionScoreUpdated(GetMaxFactionScore())
      │
      ├─ Step 7：劇情委託識別
      │     template = MissionDatabaseService.GetTemplate(outcome.missionID)
      │     if template == null OR template.categoryID != FACTION_STORY_CATEGORY_ID
      │         → return
      │
      ├─ Step 8：反查階段
      │     stage = TableLoader.GetByMissionID(outcome.missionID)
      │     if stage == null → LogWarning + return
      │
      ├─ Step 9：EventBus.Publish(OnFactionStoryStageResolvedEvent(
      │             stageID, factionID, stageIndex, missionID,
      │             outcome.isSuccess, outcome.isDead))
      │
      └─ Step 10：CheckRouteCompletion(stage.factionID, outcome.isSuccess)
            ├─ stages = TableLoader.GetByFactionID(factionID)
            ├─ maxIndex = stages.Count
            ├─ currentIndex = _unlockedStageIndices.GetValueOrDefault(factionID, 0)
            ├─ if currentIndex < maxIndex → return
            ├─ if _routeCompletedFlags.Contains(factionID) → return
            ├─ _routeCompletedFlags.Add(factionID)
            └─ EventBus.Publish(OnFactionRouteCompletedEvent(
                    factionID, stages.Last().stageID, finalIsSuccess, maxIndex))
```

> **關鍵決策**：Step 1 / Step 2 早退使用 `goto Step 7` 而非 `return`，因為「失敗 / delta=0 / neutral」雖不計分，但若 `outcome.missionID` 為劇情委託（categoryID=3）仍須走 §3.6.2 路徑發 `OnFactionStoryStageResolvedEvent`（玩家失敗時 P-02 仍須播敗北 epilogue）。`missionFactionID == 0` 的特殊處理：劇情委託按 GDD §3.2.2 強制 `factionID != 0`，故 missionFactionID == 0 必非劇情委託，可安全 return。

**觸發點 3：P-02 玩家確認對話**

```
P-02.OnDialogueConfirmClicked(stageID)
  → FactionStoryService.ConfirmDialogue(stageID)
      ├─ Step 1：if !_isEnabled → return STORY_SYSTEM_DISABLED
      ├─ Step 2：if _pendingDialogueStages.IsEmpty
      │              OR _pendingDialogueStages.Peek() != stageID
      │           → LogWarning + return INVALID_STAGE_ID
      ├─ Step 3：stage = TableLoader.GetByStageID(stageID)
      │           if stage == null → LogError + return INVALID_STAGE_ID
      ├─ Step 4：result = MissionDispatchService.InjectStaticMission(stage.missionID)
      │           if result != OK → LogError + return INJECT_FAILED
      │           （不出隊；玩家可重試）
      ├─ Step 5：_pendingDialogueStages.Dequeue()
      ├─ Step 6：EventBus.Publish(OnFactionStoryDialogueConfirmedEvent(
      │              stageID, stage.factionID, stage.missionID))
      └─ return OK
```

**觸發點 4：FT-10 SaveData 序列化**

```
SaveSystem.RequestSerialize(ownerKey="factionStorySaveData")
  → FactionStoryService.Serialize() : string
      ├─ data = new FactionStorySaveData
      ├─ data.factionScores         ← _factionScores       .Select(kv => new FactionScoreEntry(...))  .ToList()
      ├─ data.unlockedStageIndices  ← _unlockedStageIndices.Select(kv => new StageIndexEntry  (...))  .ToList()
      ├─ data.pendingDialogueStages ← _pendingDialogueStages.ToList()
      ├─ data.routeCompletedFlags   ← _routeCompletedFlags  .ToList()
      └─ return JsonUtility.ToJson(data)
```

**觸發點 5：FT-10 SaveData 反序列化（GDD §6.5）**

```
SaveSystem.RequestRestore(ownerKey="factionStorySaveData", json)
  → FactionStoryService.RestoreFromSave(json)
      ├─ try { data = JsonUtility.FromJson<FactionStorySaveData>(json); }
      │   catch { LogError + InitializeAsNewGame + return }       // EC-10(c)
      ├─ if data == null → InitializeAsNewGame + return            // EC-10(a)
      ├─ _factionScores         ← rebuild dict from entries (filter unknown factionID, log warning)  // EC-10(d)
      ├─ _unlockedStageIndices  ← rebuild dict (clamp stageIndex to maxStageIndex of factionID)       // EC-10(e)
      ├─ _pendingDialogueStages ← new Queue<int>(data.pendingDialogueStages ?? new List<int>())       // EC-10(b)
      └─ _routeCompletedFlags   ← new HashSet<int>(data.routeCompletedFlags ?? new List<int>())
```

> Bootstrap Step F 的補發於 Bootstrap 流程內處理（觸發點 1 末段），`RestoreFromSave` 本身不發事件——避免 SaveData 還原時 EventBus 可能尚未就緒（EC-12(b)）。

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `FactionRouteTable.csv` | `factionID`、`name`、`description` | `【FT-09-DS】faction-route-table.md` | 陣營路線；驗證 `factionID != 0` / 重複；提供 `HasFaction(int)` 索引 | Bootstrap §3.1.2 Step A（一次性，runtime 不熱更新） |
| `StoryStageTable.csv` | `stageID`、`factionID`、`stageIndex`、`scoreThreshold`、`missionID`、`dialogueKey` | `【FT-09-DS】story-stage-table.md` | 階段定義；驗證 stageIndex 連號從 1 起、scoreThreshold 嚴格遞增、missionID 對應 categoryID=3、dialogueKey 對應 DialogueTable（缺失僅 LogWarning）；建立 `GetByFactionID(int)` / `GetByMissionID(int)` / `GetByStageID(int)` 索引 | Bootstrap §3.1.2 Step A |
| `MissionDifficultyTable.csv`（消費端） | `factionScoreDelta`（9 行 F~SSS） | `【C-01-DS】mission-difficulty-table.md` | F-1 計分權重；透過 `IMissionDatabaseService.GetFactionScoreDelta(difficulty) : int` 取得（owner = C-01） | Bootstrap §3.1.1 啟用驗證 + §3.3.2 Step 2 runtime 查詢 |
| `MissionTemplate.csv`（消費端） | `categoryID`、`factionID` | `【C-01-DS】mission-template.md` | §3.6.2 Step 7 識別劇情委託；透過 `IMissionDatabaseService.GetTemplate(int) : MissionTemplate` 取得 | runtime（每次 `OnMissionResolved` 處理時） |
| `SystemConstants.csv`（消費端） | `FACTION_NEUTRAL_ID = 0` | `【F-01-DS】system-constants.md` | 過濾 neutral 任務；透過 `IDataManager.GetSystemConstantInt("FACTION_NEUTRAL_ID")` 取得，於 TableLoader 載入時快取為私有欄位避免熱路徑反覆查詢 | Bootstrap §3.1.2 Step A 快取 |

### 6.2 引用的 ScriptableObject

無。FT-09 全部資料來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `FACTION_NEUTRAL_ID` | `SystemConstants.FACTION_NEUTRAL_ID` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `factionScoreDelta(difficulty)`（F-1 加分權重） | `MissionDifficultyTable.factionScoreDelta` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `scoreThreshold(stageIndex)` | `StoryStageTable.scoreThreshold` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `missionID(stageIndex)` | `StoryStageTable.missionID` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `dialogueKey(stageIndex)` | `StoryStageTable.dialogueKey` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `FactionRouteTable` 行數（Jam 鎖 1） | `FactionRouteTable.csv` 行數 | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 階段數（每陣營） | `StoryStageTable` 同 factionID 行數 | 對應「四、程式實作原則」第 9 條：參數表格化 |

> **設計常數例外**：`FACTION_STORY_CATEGORY_ID = 3` 為「劇情委託類別」的設計層識別語意（GDD §3.2.2 強制），定義於 `FactionStoryTypes.cs`；不視為可調參數，但仍以具名常數而非裸 `3` 出現於程式碼中，避免「魔術數字」反模式。

---

## 7. 邊緣案例對策（Edge Case Handling）

對應 GDD §5 / 附錄 B 共 12 條 EC：

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| EC-1 CSV 載入失敗 / 全 neutral / `MissionDifficultyTable` 缺難度 | Bootstrap §3.1.1 三條啟用條件全滿足才 `_isEnabled = true`；任一不滿足 → `_isEnabled = false`，不訂閱 `OnMissionResolvedEvent`、不推送 C-06、所有 API 回降級值 | `FactionStoryService.Bootstrap`、`FactionStoryTableLoader.Load`、`FactionStoryService.IsFactionStoryEnabled` 等 7 API | EditMode：注入空 / 全 neutral CSV → 斷言 `_isEnabled == false` 且 EventBus 訂閱列表不含 FT-09；對應 AC-EC-1 |
| EC-2 未知 factionID | `HandleOnMissionResolved` Step 1 第三條檢查：`!TableLoader.HasFaction(factionID)` → `Debug.LogWarning` + `goto Step 7` | `FactionStoryService.HandleOnMissionResolved` | EditMode：mock `OnMissionResolvedEvent { factionID=99 }`，斷言 `_factionScores` 不變 + console warning；對應 AC-EC-2 |
| EC-3 玩家未確認對話關遊戲 | `_pendingDialogueStages` 持久化於 SaveData；Bootstrap §3.1.2 Step F 對 queue 內每個 stageID 重發 `OnFactionStoryStageUnlockedEvent`（讀回 stage payload；若 stage 已不存在 → LogError 跳過但不出隊） | `FactionStoryService.Bootstrap` Step F | EditMode：模擬未確認 → 重啟 → 斷言事件數 = 關閉前 queue 大小；對應 AC-EC-3 |
| EC-4 同 frame 多階段解鎖 | `Accumulator.CheckStageUnlock` 線性掃描 `stageIndex > currentMaxIndex`，依升序逐個 `_unlockedStageIndices` 寫入 + `_pendingDialogueStages.Enqueue` + `EventBus.Publish`；不批次 | `FactionStoryScoreAccumulator.CheckStageUnlock` | EditMode：注入大 delta 跨 2+ 階段，斷言事件序列升序、queue FIFO；對應 AC-EC-4 |
| EC-5 劇情委託失敗 / 死亡 | `HandleOnMissionResolved` Step 1 失敗早退跳至 Step 7；Step 9 發 `OnFactionStoryStageResolvedEvent(isSuccess=false, isDead=...)`；`_unlockedStageIndices` 在 Step 5 之前不會因失敗而修改（失敗已早退），故不回退 | `FactionStoryService.HandleOnMissionResolved` Step 1 / Step 7-9 | EditMode：mock `(missionID=stage.missionID, fail, dead)`，斷言 stageIndex 不變 + 事件 payload；對應 AC-EC-5 |
| EC-6 玩家忽略所有劇情委託 | `_pendingDialogueStages` 持續入隊（無上限）；分數累積照常；`InjectStaticMission` 僅在 `ConfirmDialogue` 後呼叫 → 委託板上不會出現劇情委託直到玩家確認對話 | `FactionStoryScoreAccumulator.CheckStageUnlock` 無上限 enqueue | EditMode：跨完所有階段不呼叫 `ConfirmDialogue`，斷言 FT-02 mock 呼叫數 = 0；對應 AC-EC-6 |
| EC-7 `InjectStaticMission` 失敗 | `ConfirmDialogue` Step 4 收到非 OK → `Debug.LogError` + return `INJECT_FAILED`；不執行 Step 5 出隊、不發 ConfirmedEvent；玩家下次點確認可重試 | `FactionStoryService.ConfirmDialogue` Step 4 | EditMode：mock FT-02 回 `BOARD_DISABLED`，斷言 result + queue 大小不變；對應 AC-EC-7 |
| EC-8 P-02 順序錯誤 | `ConfirmDialogue` Step 2 隊首匹配檢查：`Peek() != stageID` → `Debug.LogWarning` + return `INVALID_STAGE_ID`；不出隊、不呼叫 FT-02 | `FactionStoryService.ConfirmDialogue` Step 2 | EditMode：queue=[1001,1002]、呼叫 ConfirmDialogue(1002)，斷言 result + queue 不變；對應 AC-EC-8 |
| EC-9 劇情委託計分跨下階段 | 走 §3.3.2 Step 1~6 正常路徑：Step 4 發 ScoreChanged → Step 5 觸發 CheckStageUnlock 立即解鎖 N+1 → Step 6 推 C-06；接續 Step 7~10 發 StageResolved（針對 N）+ 必要時 RouteCompleted。事件順序由 EventBus 序列化保證 | `HandleOnMissionResolved` Step 4-10 | EditMode：構造 stage N 委託 delta 跨 N+1 場景，斷言事件序列：ScoreChanged → StageUnlocked(N+1) → C-06 推送 → StageResolved(N)；對應 AC-EC-9 |
| EC-10 SaveData 反序列化異常（5 子情境） | `RestoreFromSave` 對 5 子情境分別處理：(a) data == null → 視為新遊戲；(b) 欄位 null → `?? new`；(c) `JsonUtility.FromJson` 例外 → catch + LogError + 視為新遊戲；(d) 未知 factionID → LogWarning 過濾；(e) stageIndex 超出 → clamp 至 maxStageIndex + LogWarning。**全程不拋例外至上層** | `FactionStoryService.RestoreFromSave` | EditMode：5 子情境 mock JSON，斷言 runtime 狀態 + console；對應 AC-EC-10 |
| EC-11 路線完結後計分 | 分數累積照常（Step 1-6）、C-06 推送照常；Step 5 CheckStageUnlock 線性掃描找不到 `stageIndex > currentMaxIndex` 的階段（已全解鎖），迴圈空轉、無新事件；Step 10 `_routeCompletedFlags.Contains` 守護避免重發 | `FactionStoryScoreAccumulator.CheckStageUnlock` 空轉 + `FactionStoryService.CheckRouteCompletion` | EditMode：`_routeCompletedFlags={1}`、觸發 factionID=1 success B → 斷言分數累積 + C-06 推送 + RouteCompleted 不重發；對應 AC-EC-11 |
| EC-12 Bootstrap 時序失準 | (a) DataManager 未就緒 → `Get<T>()` 回 null → 啟用條件不滿足 → `_isEnabled = false` + LogError；(b) EventBus 未就緒 → Subscribe 例外 → 由全域 Bootstrap 統籌器處理；(c) FT-04 事件早於訂閱 → 該次計分遺失（容許行為）；(d) FT-09 API 早於 Bootstrap → `_isEnabled` 預設 `false` → 走降級路徑 | `FactionStoryService.Bootstrap` Step A/B/C + 預設 `_isEnabled = false` | EditMode：(a) mock DataManager 未初始化 → 斷言降級；(d) Bootstrap 前呼叫 `IsFactionStoryEnabled` → 斷言 `false` 不拋例外；對應 AC-EC-12 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

對齊 GDD §3 詳細規則，二層粒度（§3.X）：

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 系統啟停與降級行為（含 §3.1.1~§3.1.4） | §5.4 觸發點 1（Bootstrap）、§5.1 降級時行為欄、§7 EC-1 / EC-12 | 對齊 | 三條啟用條件、5 個降級行為（API / 事件 / 流程 / SaveData / runtime 切換）皆於 FSD 各表逐項落地 |
| §3.2 資料表 Schema（含 §3.2.1~§3.2.3） | §6.1 引用的 CSV 表、§5.3 資料結構 | 對齊 | FT-09 owner 二表 + C-01 消費端 + DialogueTable 待定（§3.2.2 註：dialogueKey 僅 LogWarning fallback，本 FSD §8.3 B-02 登記） |
| §3.3 分數累積管線（含 §3.3.1~§3.3.6） | §5.4 觸發點 2 Step 1-6、§7 EC-2 | 對齊 | 過濾規則 4 條、F-2 GetMaxFactionScore、推送 C-06 時機表全對應 |
| §3.4 階段解鎖判定（含 §3.4.1~§3.4.6） | §5.4 觸發點 2 Step 5（CheckStageUnlock）、§5.3（OnFactionStoryStageUnlockedEvent payload 5 欄位）、§7 EC-4 | 對齊 | _unlockedStageIndices 單調遞增 + FIFO Queue + 不重複解鎖反證場景皆對齊 |
| §3.5 雙軌呈現流程（含 §3.5.1~§3.5.6） | §5.1 ConfirmDialogue API、§5.4 觸發點 3、§7 EC-7 / EC-8 | 對齊 | 對話 → 委託強制順序、`ConfirmDialogueResult` 4 enum 值、隊首匹配契約、失敗路徑表 4 行皆對應 |
| §3.6 階段推進規則（含 §3.6.1~§3.6.7） | §5.4 觸發點 2 Step 7-10、§5.3（StageResolved / RouteCompleted payload）、§7 EC-5 / EC-9 / EC-11 | 對齊 | 推進語意（解鎖即推進、結算不可逆）、CheckRouteCompletion 偽碼、Free Pacing 4 行為表、計分回流注意事項皆落地 |
| §3.7 對外 API、事件契約、Runtime 狀態 | §5.1（7 API）、§5.2（事件 + 直接推送）、§5.3（runtime 狀態欄位 5 個 + SaveData 中介）、§5.4（執行緒模型隱含於 EventBus 主執行緒契約） | 對齊 | API 簽名 / 事件方向 / payload / runtime 狀態欄位逐項對應 |

### 8.2 公式對齊或替代說明

| 公式 | FSD 處理 | 替代說明 |
| --- | --- | --- |
| F-1 陣營分數累積 | 直接採用 GDD §3.3.2 Step 3 + 附錄 A.1 偽碼，於 `FactionStoryService.HandleOnMissionResolved` 落地 | 無替代 |
| F-2 陣營最高分 | 直接採用 GDD §3.3.4 + 附錄 A.2，於 `IFactionStoryService.GetMaxFactionScore` 落地（降級 / 空字典 / max 三分支） | 無替代 |
| F-3 階段解鎖判定 | 直接採用 GDD §3.4.2 + 附錄 A.3 線性掃描提早終止偽碼，於 `FactionStoryScoreAccumulator.CheckStageUnlock` 落地 | 無替代 |
| F-4 階段間距 buffer | **不在 runtime 實作**：GDD §4 / 附錄 A.4 已標明為「設計師於 spreadsheet / Editor tool 校準時使用」 | 不轉譯為程式碼；§7 旋鈕 A2 / A3 變更後由設計師於 offline 工具預驗 |
| F-5 解鎖節奏推估 | **不在 runtime 實作**：GDD §4 / 附錄 A.5 已標明為「playtest 校準工具」 | 同 F-4，FSD 不規範實作 |

### 8.3 未能實現的規則與修改建議

| 編號 | 內容 | 影響 | 處理建議 |
| --- | --- | --- | --- |
| B-01 | FT-02 `InjectStaticMission` API 簽名與回傳 enum 值（OK / UNKNOWN_MISSION_ID / WRONG_CATEGORY / ALREADY_ON_BOARD / BOARD_DISABLED）已於 FT-02-FSD-A/B 出現，但本 FSD 撰寫時未逐字 grep 確認 5 enum 值齊全；若 FT-02 後續調整 enum，需同步更新 `FactionStoryTypes.ConfirmDialogueResult` 與 EC-7 對策 | 建議項，不阻擋實作 | Codex 實作 PR 前 grep `InjectStaticMissionResult` 於 FT-02 程式碼確認 |
| B-02 | `DialogueTable` owner 系統 Jam 版未指派；§3.2.2 規範「dialogueKey 找不到 → LogWarning + 空對話 fallback」，FSD 對齊但 P-02 對話視窗在 owner 定案前無真實文本 | 建議項，不阻擋 FT-09 實作（P-02 端可用 placeholder） | 待 P-02 GDD 設計時自行確認 owner，FT-09 端不需修改 |
| B-03 | P-02 / P-03 GDD 待設計，§6.4 反向依賴 #8 / #9 為 ⏳ 狀態；本 FSD 已暴露 5 事件 + ConfirmDialogue API 供 P-02 訂閱 / 呼叫，但 P-02 端契約尚未在其 FSD 確認 | 建議項，不阻擋 FT-09 實作 | 待 P-02 / P-03 FSD 撰寫時逐項對齊 |
| B-04 | `ISaveable` 介面（`OwnerKey` / `IsCritical` / `Serialize` / `RestoreFromSave` / `InitializeAsNewGame` 簽名）由 FT-10 FSD 規範，本 FSD 撰寫時 FT-10 FSD 尚未存在；`FactionStoryService` 暫以 GDD §6.5 描述為實作契約，FT-10 FSD 落地後可能需微調簽名 | 建議項，不阻擋實作（FT-10 為總後段系統，先實作 FT-09 再對齊 ISaveable 風險可控） | 待 FT-10 FSD 完成後 Codex 補丁對齊 |
| B-05 | GDD §3.1.2 Step F 補發事件時若 `StoryStageData.GetByStageID` 找不到該 stage（資料表縮減場景），GDD 未明文規範「補發失敗的對策」；本 FSD §5.4 觸發點 1 採「LogError + 跳過該 stageID 但不出隊」處理（保守保留 queue），與 EC-10(e) 夾擠精神一致但 GDD 無對應條目 | 建議項，§3 未涵蓋的補洞 | 建議於 GDD §3.1.2 Step F 加註「補發時 stage 不存在 → LogError + 不出隊（待後續 ConfirmDialogue 走 INVALID_STAGE_ID 路徑出錯時由玩家 / 開發者察覺）」；§8.4 已意圖回註 |
| B-06 | GDD §3.3.2 Step 1 過濾失敗早退時 GDD 偽碼直接 `return`，但 FT-09 §3.6.2 / EC-5 規範「劇情委託失敗 / 死亡仍須發 StageResolved」——若直接 return 會錯過劇情委託判定。本 FSD §5.4 觸發點 2 改為 `goto Step 7`（保留劇情委託判定路徑），結果語意與 GDD 一致但偽碼分支結構不同 | 建議項，公式等價但 GDD 偽碼可優化 | 建議於 GDD §3.3.2 Step 1 補註「失敗 / delta=0 早退時若該任務為 categoryID=3 仍須走 §3.6.2」；§8.4 已意圖回註 |

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-28 | `【FT-09】faction-story-system.md` | §3.1.2 Step F | FSD 回註：補發事件時若 stage 已被 CSV 縮減導致 `GetByStageID` 回 null，FSD 採「LogError + 不出隊」保守處理，與 EC-10(e) 夾擠原則一致；GDD 可考慮明文化此分支（B-05 修補意圖）。**[2026-04-28 T15 落地]** ✓ 已寫入 GDD §3.1.2 Step F，補規則：「stageID 不存在於 StoryStageTable 則 LogWarning + 從 queue 移除 + continue」+ FSD 回註說明段落。 |
| 2026-04-28 | `【FT-09】faction-story-system.md` | §3.3.2 Step 1 | FSD 回註：失敗 / delta=0 / 未知 factionID 早退時，若該 outcome 對應劇情委託（`categoryID == 3`），仍須走 §3.6.2 路徑發 `OnFactionStoryStageResolvedEvent`（對應 EC-5 失敗 / 死亡仍須播 epilogue）；建議 GDD 偽碼將 `return` 改為「跳至 Step 7」並加註此跨節依賴（B-06 修補意圖）。**[2026-04-28 T16 落地]** ✓ 已寫入 GDD §3.3.2 Step 6 後補「T16 修補規則」block：明示 Step 1~6 早退僅限分數累積路徑、§3.6.2 Step 7~10 必執行、實作建議用 `AccumulateScore` 內部方法包覆。 |

> **更新（2026-04-28）**：兩條回註已於 T15 / T16 裁決批次寫入 GDD（§3.1.2 Step F 防禦分支 / §3.3.2 Step 6 後 T16 修補規則 block）。FSD 邏輯與 GDD 已對齊。

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | 無真實衝突 | — | — |

> 撰寫過程未發現任何 GDD 內部或跨 GDD 規則衝突；§8.3 B-05 / B-06 為「GDD 偽碼可優化」性質的建議，非規則衝突。

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用 4 份、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：11 條 DoD 皆可被 EditMode／PlayMode 測試或手動步驟驗證
- [x] §2.1~§2.5：GDD 章節（§3.1~§3.7 + §4~§7）、Data-Specs（4 份）、上下游（5+5+1 systems）、事件契約（6 訂閱/發布 + 2 直接 API）四向皆列舉
- [x] §3.3 對映表：6 條覆蓋 §2 玩家幻想 5 個目標情緒 + §1 系統目的「線性陣營劇情 + 累積感」全部
- [x] §4.1~§4.4：未拆分 + 5 Script 清單欄位齊全（路徑、SRP、依賴、預估規模）
- [x] §5.1~§5.4：7 公開 API + 6 事件 + 2 直接推送 + 5 runtime 欄位 + SaveData 結構 + 5 觸發點偽碼齊備
- [x] §6.1~§6.3：5 引用表含對應 Data-Specs；§6.3 嚴禁寫死清單 7 項全對齊原則第 9 條
- [x] §7：12 條 EC 皆有對策、涉及 Script、驗證方式
- [x] §8.1：對齊清單覆蓋 GDD §3.1~§3.7 二層粒度
- [x] §8.2~§8.5：F-1~F-3 直接採用 / F-4~F-5 不在 runtime；6 條 §8.3 建議項 / 2 條 §8.4 GDD 回註意圖 / §8.5 無衝突如實登記
- [x] FSD-index：§6.1 / §7.1 / §7.2 同步更新（本次 patch 一併執行）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh） | 通過 | 通過 | 通過 | 正向 FSD（無既有 Faction / Story Script）；FSD 未拆分（5 Script：FactionStoryTypes / IFactionStoryService / FactionStoryTableLoader / FactionStoryScoreAccumulator / FactionStoryService，預估合計 1000~1200 行）；GDD §3.1~§3.7 共 7 子節全部「對齊」；GDD §4 公式 F-1~F-3 直接採用偽碼，F-4 / F-5 為 offline 工具不在 runtime；GDD §5 共 12 條 EC 皆有對策、涉及 Script、驗證方式；GDD §8 AC-F-1~F-8 / AC-EC-1~12 / AC-D-1~9 全對齊 §1.3 / §7；無真實衝突；建議項 B-01（FT-02 InjectStaticMission enum 確認）/ B-02（DialogueTable owner 待定）/ B-03（P-02 / P-03 待設計）/ B-04（ISaveable 介面待 FT-10 定案）/ B-05（GDD §3.1.2 Step F stage 不存在分支）/ B-06（GDD §3.3.2 Step 1 categoryID=3 跨節依賴）皆不阻擋實作；§8.4 兩條 GDD 回註意圖（§3.1.2 / §3.3.2）待主體覆核後寫入 GDD；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| 2026-04-30 | Claude Code 主體 | 待 patch | 待 patch | 待 patch | **v3.1 patch P3.1-004 同步紀錄**：FT-09 GDD 大幅修改已完成（StoryStageTable 新增 dialogueVariantMode / specialEventKey / unlockBlockerCondition + ResolveDialogueKey + GetCurrentStyleTagBias + OnFactionStoryStageEpilogue + unlockBlockerCondition 機制 + 訂閱 FT-04 OnAdventurerDied + OnOpheliaMissingNight/Returned + 5 行 Stage 預設資料）。FSD Script 設計待補：(1) FactionStoryTypes 新增 dialogueVariantMode enum / OnFactionStoryStageEpilogue / Ophelia 事件 struct；(2) IFactionStoryService 新增 GetCurrentStyleTagBias / TriggerDeferredStageCheck API；(3) FactionStoryService 實作 ResolveDialogueKey + unlockBlockerCondition + 訂閱 FT-04 OnAdventurerDied + OnOpheliaMissingNight 流程。完整 patch 規格見 `_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` §2.4。**需 design-review 重跑（最大 patch）**。 |
| 2026-04-30 | Claude Code 主體（GDD review R1/R3 修正同步） | 待 patch | 待 patch | 待 patch | **GDD review R1 + R3 修正同步紀錄**：上一行同步紀錄中提到的兩個設計點已於 GDD review 修正（已寫入 FT-09 GDD §3.4.8 / §3.6.10）：(1) **R1**：「訂閱 FT-04 OnAdventurerDied」 → 改為「在既有 HandleOnMissionResolved（§3.6.2）中加 Step 10 讀 outcome.isDead 計數」（FT-04 沒有獨立 OnAdventurerDied 事件，只發布 OnMissionResolved）；(2) **R3**：「EvaluateBlocker 用 C-02.GetAdventurerStatus(opheliaInstanceID)」 → 改為「`C02.GetRoster().FirstOrDefault(a => a.templateID == OPHELIA_TEMPLATE_ID)?.status`」（C-02 沒有 GetAdventurerStatus API；以 GetRoster 配合 OPHELIA_TEMPLATE_ID 取得 instance）。FSD Script 設計待補項目對應更新。完整 R1/R2/R3 修正紀錄見 GDD review 報告（2026-04-30）。 |
