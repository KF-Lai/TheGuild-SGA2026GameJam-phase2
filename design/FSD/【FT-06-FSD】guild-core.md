# 【FT-06-FSD】功能規格說明書 — Guild Core

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-06】guild-core.md`（版本：2026-04-22 設計完成） |
| 對應 Data-Specs | `【FT-06-DS】guild-level-table.md`（_待建_，CSV：`GuildLevelTable.csv`） |
| 撰寫者 | Claude Code 主體（直接撰寫，無 subagent） |
| Review 者 | Claude Code 主體（自檢） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27（依 design-review 修補 C1~C5 / I1~I3：F-03 事件正名為 `OnBankruptcyStateChangedEvent` + payload 雙欄位、F-01 載表改 `GetAll<T>` + `RegisterTable<T>` 模式、Game Over snapshot 緩存於 `_pendingSnapshot` field、`RestoreFromSave` 補 null 守衛、runtime 用 `GameOverState` enum、Loader.Load 補 7 項驗證；§8.5 補登 8 筆衝突處理紀錄；§8.3 B-03 改為「已修正」） |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-06 Guild Core 是公會層級狀態的統一管理系統，承載三大職責：(1) 訂閱 F-03 聲望事件、依 `GuildLevelTable` 即時判定升級並逐級發布事件；(2) 訂閱 F-03 破產事件、驅動 Pending → Over 兩階段 Game Over 流程；(3) 維護公會基礎識別資料（玩家輸入名稱、創立 UTC 時間戳）。本 FSD 將 GDD §3 詳細規則拆解為 5 支 C# Script，並整合 ISaveable 持久化契約。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**

- 公會等級判定演算法、連跳 queue（逐 frame 發送）
- Game Over 兩階段狀態機（Active → Pending → Over）
- 公會名稱輸入處理（strip 控制字元、全型字截斷、後綴組合）
- `GuildLevelTable.csv` 載入、欄位驗證、查詢 API
- `GuildState` 序列化／還原（ISaveable）
- 同步查詢 API：`GetCurrentLevel`、`GetCurrentTitle`、`GetMaxRecruitableRank`、`GetMaxMissionDifficulty`、`GetMaxDifficulty`（deprecated）、`GetGuildDisplayName`、`GetFoundingTimestamp`、`IsGameOverPending`、`IsGameOver`
- 事件發布：`OnGuildInitialized` / `OnGuildLoaded` / `OnGuildLevelChanged` / `OnGameOverPending` / `OnGameOver`

**Out-of-Scope**

- 聲望數值運算（F-03 負責）
- 破產判定邏輯（F-03 負責）
- 名冊上限（`GetRosterCap`）／並行任務上限（`GetMaxConcurrentMissions`）查詢（FT-07 接管）
- 存檔 I/O 與封存策略（FT-10 負責）
- 結算畫面內容、訃聞 UI、升級 toast（P-02 / P-03 負責）
- F-02 tick 暫停的實作細節（FT-06 僅呼叫 `PauseTick()`）
- P-01 公會名稱輸入框 UI（FT-06 僅做防禦性 strip + truncate）
- TextMeshPro Rich Text 標籤對偵測（Phase 3，Jam 不實作）

### 1.3 完成目標（Definition of Done）

對應 GDD §8 驗收標準，補充程式可驗證條件：

- **DoD-1**：`GuildLevelTable.csv` 載入後通過 §3.5 欄位驗證（Lv1 threshold = 0、threshold 嚴格升冪、`maxRecruitableRank`／`maxMissionDifficulty` 單調不降）；違規 `Debug.LogError` + 拋例外。
- **DoD-2**：AC-1 ~ AC-3（初始化）：新遊戲後 `GetCurrentLevel == 1`、`GetCurrentTitle == "新手冒險者公會"`、`GetGuildDisplayName` 對應 §3.9 組合規則、`GetFoundingTimestamp == TimeSystem.Instance.NowUTC` 初始化瞬間值。
- **DoD-3**：AC-4 ~ AC-7（等級系統）：單級升級、四級連跳、聲望回跌、Lv5 封頂四種情境之 EditMode／PlayMode 測試通過。
- **DoD-4**：AC-8（容量 API）：`GetMaxRecruitableRank` / `GetMaxMissionDifficulty` 即時讀表、與當前 `currentLevel` 對應列一致；`GetMaxDifficulty` deprecated alias 行為等同 `GetMaxRecruitableRank` 並輸出 deprecation log。
- **DoD-5**：AC-9 ~ AC-12（Game Over）：Pending 進入後 F-02 tick 未暫停、`ConfirmGameOver` 呼叫後 tick 已暫停、非 Pending 狀態呼叫 `ConfirmGameOver` 為冪等、Pending／Over 期間聲望事件不再觸發升級。
- **DoD-6**：AC-13（存讀檔）：`OnGuildInitialized` 在新遊戲時發出；`OnGuildLoaded` 在 FT-10 還原後發出，payload `currentLevel` 與存檔一致；不補發連跳事件。
- **DoD-7**：AC-14 ~ AC-15b（公會名稱）：超長字串截斷至 8 全型字、控制字元 strip、Rich Text 標籤逐字計入。
- **DoD-8**：CSV 載入零錯誤、所有上述 AC 對應的 EditMode／PlayMode 測試通過。

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要：系統三大職責、不負責清單、`GuildLevelTable` 欄位構成
- §2 玩家幻想：MDA Aesthetics（成長／終局）、情緒節點 → §3.3 對映表來源
- §3.1 ~ §3.11 詳細規則：`GuildState`、初始化、等級判定、連跳、表結構、容量 API、Game Over Pending／Over、名稱處理、事件發布契約、查詢 API 總表
- §4.1 ~ §4.5 公式：等級判定公式、升級判定、連跳事件序列、名稱長度公式、Game Over 狀態轉移
- §5.1 ~ §5.5 邊緣案例：等級異常、連跳異常、Game Over 異常、名稱異常、訂閱／生命週期異常
- §6.1 ~ §6.6 依賴：上下游、事件契約矩陣、反向依賴、開發順序、ISaveable 契約
- §7.1 ~ §7.4 可調參數：`GuildLevelTable`、程式碼常數、不可調參數、調整檢查清單
- §8 驗收標準：AC-1 ~ AC-15b

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-06-DS】guild-level-table.md`（_待建_） | `GuildLevelTable.csv` | `level`、`reputationThreshold`、`title`、`maxDifficulty`（deprecated）、`maxRecruitableRank`、`maxMissionDifficulty` | 等級判定門檻、UI 稱號、容量 API 回傳值 |

> 本 FSD 不引用 `SystemConstants.csv`；FT-06 程式碼常數（`GUILD_NAME_MAX_FULLWIDTH` 等）以 GDD §7.2 規範定義於 `GuildCoreConstants.cs`，不入 SystemConstants。

### 2.3 上游依賴系統

> 依 FSD-index §2.10：本 FSD 敘述以 `IXxxService` 命名為可讀性而保留；實作 PR 直接呼叫 concrete singleton（`TimeSystem.Instance` / `ResourceManagement.Instance` / `DataManager.Instance` / `EventBus`），不新增 interface 包裝。

| 上游系統 | 介面 | 用途 | 類型 |
| --- | --- | --- | --- |
| F-01 DataManager | `IDataManager.RegisterTable<GuildLevelEntry>("GuildLevelTable")` + `IDataManager.GetAll<GuildLevelEntry>()` | 註冊並載入等級表 CSV（對齊 F-01 FSD 2026-04-27 去中心化機制：F-01 不提供 `GetTable<T>`，下游各自 `RegisterTable` 後以 `GetAll<T>` 取得 `IReadOnlyList<T>` 再自建 dictionary） | 控制 + 同步查詢 |
| F-02 Time System | `ITimeService.NowUTC : long` | 取創立時間、升級時間戳、確認時間戳 | 同步查詢 |
| F-02 Time System | `ITimeService.PauseTick(): void` | Game Over 階段 2 後暫停時間 | 控制 API |
| F-03 Resource Management | Event `OnReputationChanged(int newValue, int delta)` | 觸發等級判定 | 事件訂閱 |
| F-03 Resource Management | Event `OnBankruptcyStateChangedEvent(BankruptcyWarningState PreviousState, BankruptcyWarningState CurrentState)` | 觸發 Game Over 流程；FT-06 僅讀 `CurrentState`、忽略 `PreviousState` | 事件訂閱 |
| F-03 Resource Management | `IResourceService.GetGold(): int` / `GetReputation(): int` | Game Over snapshot | 同步查詢 |
| EventBus | `Publish<T>(T)` / `Subscribe<T>` / `Unsubscribe<T>` | 發布／訂閱本系統事件 | 服務 |

**硬依賴**：F-01／F-02／F-03 均為必要依賴，任一缺失 FT-06 無法運作；`Awake()` 階段 cache instance reference。

### 2.4 下游被依賴系統

| 下游系統 | 介面 / 事件 | 用途 |
| --- | --- | --- |
| FT-01 Adventurer Recruitment | `GetMaxRecruitableRank()` | 老手邀請最高冒險者階級上限 |
| FT-02 / P-02 / C-06（待採用） | `GetMaxMissionDifficulty()` | 委託板難度過濾 |
| FT-03 NPC Decision System | `GetMaxMissionDifficulty()` | 自主接單候選任務過濾 |
| FT-07 Guild Building System | `GetCurrentLevel()` | 升級聲望閘門判定 |
| FT-08 Gacha System | `GetCurrentLevel()` | `StaffGachaPoolTable.minGuildLevel` 過濾、刷新成本索引 |
| FT-10 Save/Load | `OnGuildInitialized` / `OnGameOver` 事件、ISaveable | 首次存檔／封存存檔／序列化還原 |
| P-01 Intro / Main Menu | `SetGuildName(string)` / `OnGuildInitialized` | 新遊戲流程 |
| P-02 HUD & Screens | `OnGuildLevelChanged` / `OnGameOverPending` / `OnGameOver` / `GetGuildDisplayName` / `GetCurrentTitle` / `IsGameOverPending` / `IsGameOver` / `ConfirmGameOver` | HUD、訃聞、結算畫面、確認動作 |
| P-03 Notification | `OnGuildLevelChanged` | 升級 toast |

### 2.5 跨系統事件契約

**訂閱（FT-06 → 接收）**

| 事件 | 來源 | Payload | 處理動作 |
| --- | --- | --- | --- |
| `OnReputationChangedEvent` | F-03 | `int newValue, int delta` | §5.4-A 等級判定流程；`gameOverState != Active` 時直接 return |
| `OnBankruptcyStateChangedEvent` | F-03 | `BankruptcyWarningState PreviousState, BankruptcyWarningState CurrentState`（值域：`Normal` / `Warning` / `Bankrupt`） | §5.4-C Pending 觸發；FT-06 僅判讀 `CurrentState == Bankrupt`、忽略 `PreviousState`；非 Bankrupt 或已 Pending／Over 時忽略 |

**發布（FT-06 → 發送）**

| 事件 | 訂閱者 | Payload 摘要 | 發布時機 |
| --- | --- | --- | --- |
| `OnGuildInitializedEvent` | P-02、FT-10 | `displayName, foundingTimestamp` | 新遊戲 `InitializeAsNewGame` 末段 |
| `OnGuildLoadedEvent` | P-02 | `displayName, currentLevel` | FT-10 `RestoreFromSave` 末段 |
| `OnGuildLevelChangedEvent` | P-02、P-03、FT-01、FT-02 | 11 欄位（見 §5.3） | `Update()` 從 queue 取出一級時 |
| `OnGameOverPendingEvent` | P-02 | 6 欄位 snapshot（見 §5.3） | 收到首次 Bankrupt 事件當下 |
| `OnGameOverEvent` | P-02、FT-10 | Pending payload + `confirmTimestamp` | `ConfirmGameOver()` 被呼叫 |

> 訂閱／發布皆透過 `EventBus`（static class）；FT-06 在 `Awake()` 訂閱、`OnDestroy()` 解除（§5.4-F）。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

GDD §2 描繪兩種對比情緒：「成長曲線」是玩家從 Lv1 小作坊一路經營至 Lv5 名聲顯赫公會、看見聲望累積轉化為稱號變化與容量解鎖的成就感；「終局敘事」是破產後不直接彈出 GAME OVER，而是兩階段（訃聞→結算）給予敘事重量，讓「失去的不只是進度，是自己命名的公會」。玩家以「我是這間公會的創立者」自居，每一次升級瞬間都要被系統「即時看見」。

### 3.2 系統目的還原

GDD §1 將 FT-06 定位為「公會層級狀態的統一管理系統」，承擔三件事：訂閱 F-03 聲望／破產事件 → 維護公會等級與 Game Over 狀態；提供同步查詢 API 給下游（容量、稱號、顯示名稱、Game Over 旗標）；儲存公會識別資料（玩家輸入名稱、創立 UTC 時間戳）並持久化。FT-06 不參與聲望／破產／存檔 I/O 的運算，只做「狀態整合 + 事件廣播」。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 升級即時回饋 | 聲望達標瞬間 HUD／toast 出現升級事件，不等日結 | F-03 `OnReputationChanged` 事件當 frame 排入 `pendingLevelUpQueue`，下一 frame 由 `Update()` 發 `OnGuildLevelChangedEvent` |
| 連跳的儀式感 | Lv1→Lv4 也逐級播放動畫、單級可獨立呈現 | `pendingLevelUpQueue` 逐項排入 + `LEVEL_UP_QUEUE_INTERVAL_FRAMES` 間隔節流；payload 攜帶 `isMultiJump` / `finalTargetLv` |
| 稱號勝於數字 | UI 強調「新手→初階→中階…」變化 | payload 同時帶 `fromTitle` / `toTitle`，下游可選擇顯示稱號或數字 |
| 容量解鎖具體好處 | 升級後「能招更高階冒險者」「能接更難任務」 | `GetMaxRecruitableRank` / `GetMaxMissionDifficulty` 即時讀 `GuildLevelTable[currentLevel]` 不快取 |
| 破產的沉重 | 訃聞畫面先彈出，玩家手動確認後才進結算 | `gameOverState` 兩階段 enum（`Pending` → `Over`），`ConfirmGameOver()` 手動觸發轉換 |
| 兩階段給予緩衝 | Pending 期間時間仍流動、結算期間時間凍結 | Pending 不呼叫 `PauseTick()`；Over 階段發 `OnGameOver` 後立即呼叫 `TimeSystem.Instance.PauseTick()` |
| 公會命名個人化 | 「約翰的公會」呈現於 HUD、訃聞、結算 | 玩家輸入 → `ComposeDisplayName` strip + truncate + 後綴組合 → `displayName` 持久化、所有事件 payload 攜帶 |
| 不降級的成就感 | 聲望回跌時等級不滑落 | `OnReputationChanged` handler 內 `targetLevel <= currentLevel` 時 early return（GDD §3.3 / §5.1.1） |
| 等級頂峰可炫耀 | Lv5 達成後即使再累積聲望也維持頂級稱號 | §5.1.4：頂級狀態 `targetLevel == currentLevel == 5` 時不發事件，但 API 仍回傳 |
| Game Over 優先級 | 連跳途中破產時，停止彈升級通知、直接彈訃聞 | `Update()` 取 queue 前檢查 `gameOverState != Active`，非 Active 直接 `Clear()`（GDD §5.2.2） |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（單 FSD 對應 5 支 Script）。

### 4.2 拆分理由

不拆分依據：

- 三大職責（等級系統 / Game Over / 公會基礎狀態）共用同一 `GuildState` 物件與同一 ISaveable owner（`ft06Guild`），切到不同 FSD 會出現「跨 FSD 修改同一狀態欄位」的耦合，違反 SRP 的對外定義（同一 owner 同一檔）。
- Game Over 與等級系統有明確互動（Pending 狀態凍結等級判定，連跳途中觸發 Game Over 必須清空 queue）；放在同一服務內較容易維持狀態機不變式。
- 預估總行數 650~830，未達拆分硬閾（單 Script > 500 行才觸發 §2.4 拆分判斷；本系統最大單檔 `GuildCoreService.cs` 預估 300~380 行）。
- 既有同類系統（C-06 5 Script、C-01 4 Script、FT-04 4 Script）均選擇「未拆分 FSD + 多 Script」配置，FT-06 沿用此模式。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `GuildCoreTypes.cs` | `Assets/Scripts/Gameplay/Guild/GuildCoreTypes.cs` | 定義 `GuildState` / `GameOverState` enum / `GuildLevelEntry` DTO / 5 個事件 payload struct / `LevelUpPayload` 內部 queue 元素 | （純資料） | 130~170 行 |
| `GuildCoreConstants.cs` | `Assets/Scripts/Gameplay/Guild/GuildCoreConstants.cs` | 定義 `GUILD_NAME_MAX_FULLWIDTH` / `GUILD_NAME_SUFFIX` / `DEFAULT_GUILD_NAME` / `LEVEL_UP_QUEUE_INTERVAL_FRAMES` / `OWNER_KEY` / `GUILD_LEVEL_TABLE_NAME` 常數 | （純常數） | 30~50 行 |
| `GuildLevelDatabaseLoader.cs` | `Assets/Scripts/Gameplay/Guild/GuildLevelDatabaseLoader.cs` | `GuildLevelTable.csv` 解析、欄位驗證（§3.5 / §7.4）、提供 `GetEntry(level)` / `FindTargetLevel(rep)` 查表方法 | `IDataManager` | 120~160 行 |
| `GuildNameUtility.cs` | `Assets/Scripts/Gameplay/Guild/GuildNameUtility.cs` | 公會名稱字串處理：`StripControlChars` / `IsFullWidth` / `CountDisplayChars` / `TruncateToFullwidthLimit` / `ComposeDisplayName` / `IsValidGuildName` 純函式 | `System.Globalization` | 80~120 行 |
| `GuildCoreService.cs` | `Assets/Scripts/Gameplay/Guild/GuildCoreService.cs` | MonoBehaviour 主服務：訂閱 F-03 事件、連跳 queue 排程／逐 frame 發送、Game Over 兩階段狀態機、ISaveable 序列化／還原、所有對外 Get API 與 `ConfirmGameOver` / `SetGuildName` | `IDataManager`、`ITimeService`、`IResourceService`、`EventBus`、`ISaveable`、`GuildLevelDatabaseLoader`、`GuildNameUtility`、`GuildCoreConstants`、`GuildCoreTypes` | 290~370 行 |

合計：650~870 行。

### 4.5 類別關係

```
GuildCoreService (MonoBehaviour, ISaveable)
   ├─ uses → GuildLevelDatabaseLoader（持有 IReadOnlyList<GuildLevelEntry> + Dictionary<int, GuildLevelEntry>，提供查表）
   ├─ uses → GuildNameUtility（呼叫 ComposeDisplayName / StripControlChars）
   ├─ holds → GuildState（單一實例；序列化 POCO，gameOverState 為字串）
   ├─ holds → GameOverState _gameOverState（runtime enum，所有比較／轉移使用；與 _state.gameOverState 字串透過 SetGameOverState helper 同步）
   ├─ holds → Queue<LevelUpPayload> _pendingLevelUpQueue
   ├─ holds → OnGameOverPendingEvent _pendingSnapshot（§5.4-C 緩存，§5.4-D 重用）
   ├─ subscribes → EventBus.Subscribe<OnReputationChangedEvent / OnBankruptcyStateChangedEvent>
   └─ publishes → EventBus.Publish<OnGuildInitializedEvent / OnGuildLoadedEvent
                                    / OnGuildLevelChangedEvent / OnGameOverPendingEvent
                                    / OnGameOverEvent>

GuildLevelDatabaseLoader
   ├─ depends → DataManager.Instance.RegisterTable<GuildLevelEntry>(GUILD_LEVEL_TABLE_NAME)
   ├─ depends → DataManager.Instance.GetAll<GuildLevelEntry>()
   └─ exposes → GetEntry(int level) / FindTargetLevel(int rep) / GetAll()

GuildCoreTypes（純 plain types）
   ├─ GuildState（serializable POCO；含 gameOverState: string）
   ├─ GameOverState（runtime enum: Active / Pending / Over；序列化前透過 .ToString() 轉成 _state.gameOverState）
   ├─ GuildLevelEntry（CSV row DTO）
   ├─ LevelUpPayload（runtime queue 元素：fromLv, toLv, finalTargetLv, isMultiJump, reputationAtUpgrade）
   └─ 5 事件 struct（On*Event）

GuildNameUtility / GuildCoreConstants：純 static utility / 常數
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

`GuildCoreService` 對外暴露的方法（皆為 `public`，回傳值不可為 `null` 除非註明）：

| 簽名 | 用途 | 備註 |
| --- | --- | --- |
| `void SetGuildName(string rawInput)` | 新遊戲 P-01 流程：玩家輸入公會名稱 | 必須在 `InitializeAsNewGame` 呼叫之前；內部 `ComposeDisplayName` 處理 |
| `int GetCurrentLevel()` | 回傳當前公會等級（1~5） | — |
| `string GetCurrentTitle()` | 回傳 `GuildLevelTable[currentLevel].title` | 即時查表，不快取 |
| `string GetMaxRecruitableRank()` | 回傳老手招募最高冒險者階級（D~S） | 即時查表 |
| `string GetMaxMissionDifficulty()` | 回傳常規任務最高難度（D~SS） | 即時查表 |
| `string GetMaxDifficulty()` | **[Deprecated]** alias，內部呼叫 `GetMaxRecruitableRank` 並 `Debug.LogWarning` | 下次 review 移除 |
| `string GetGuildDisplayName()` | 回傳完整顯示名稱（含「公會」後綴） | — |
| `long GetFoundingTimestamp()` | 回傳公會創立時間（UTC Unix seconds） | — |
| `bool IsGameOverPending()` | `gameOverState == Pending` | — |
| `bool IsGameOver()` | `gameOverState == Over` | — |
| `void ConfirmGameOver()` | P-02 玩家確認訃聞後呼叫，觸發階段 2 | 非 Pending 狀態冪等（log warning + return） |

**ISaveable 介面**（`ISaveable.cs`，FT-10 定義）：

| 簽名 | 用途 |
| --- | --- |
| `string OwnerKey { get; }` | 回傳 `"ft06Guild"`（`GuildCoreConstants.OWNER_KEY`） |
| `bool IsCritical { get; }` | 回傳 `true` |
| `string Serialize()` | 序列化 `GuildState` 5 欄位為 JSON |
| `void RestoreFromSave(string ownerJson)` | 反序列化並驗證；違規拋例外（FT-10 觸發整檔回退） |
| `void InitializeAsNewGame()` | 設定 §6.4 預設值；配合 `SetGuildName` 後呼叫 |

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnReputationChangedEvent` | 訂閱（F-03 → FT-06） | `int newValue, int delta` | `gameOverState == Active` 時觸發等級判定流程 |
| `OnBankruptcyStateChangedEvent` | 訂閱（F-03 → FT-06） | `BankruptcyWarningState PreviousState, BankruptcyWarningState CurrentState` | `CurrentState == Bankrupt && _gameOverState == GameOverState.Active` 時觸發 Pending；忽略 `PreviousState` |
| `OnGuildInitializedEvent` | 發布 | `string displayName, long foundingTimestamp` | `InitializeAsNewGame` 末段（含 `SetGuildName` 已呼叫） |
| `OnGuildLoadedEvent` | 發布 | `string displayName, int currentLevel` | `RestoreFromSave` 末段 |
| `OnGuildLevelChangedEvent` | 發布 | 11 欄位（見 §5.3） | `Update()` 從 `_pendingLevelUpQueue` 取出一項時 |
| `OnGameOverPendingEvent` | 發布 | 6 欄位 snapshot | `OnBankruptcyStateChangedEvent` handler 首次符合條件時（`CurrentState == Bankrupt && _gameOverState == GameOverState.Active`） |
| `OnGameOverEvent` | 發布 | Pending payload + `long confirmTimestamp` | `ConfirmGameOver` 內，`PauseTick` 之後 |

### 5.3 資料結構

**`GuildState`**（POCO，FT-10 序列化欄位；JsonUtility 直接序列化）

```
public class GuildState {
    public string guildName;         // 玩家輸入（不含後綴）
    public string displayName;       // 含「公會」後綴
    public long foundingTimestamp;   // UTC Unix seconds
    public int currentLevel;         // 1..5，僅升不降
    public string gameOverState;     // "Active" / "Pending" / "Over"（序列化用 string；runtime 不直接讀此欄位）
}
```

**`GameOverState` enum**（runtime 用，所有比較與狀態轉移皆透過此 enum；序列化前由 `GuildCoreService` 將 enum `.ToString()` 同步寫入 `_state.gameOverState` 字串）

```
public enum GameOverState { Active, Pending, Over }
```

> **runtime / 序列化雙欄位策略**：`GuildCoreService` 內部維護兩份狀態——`private GameOverState _gameOverState`（runtime cache，所有判斷／比較／轉移使用）與 `_state.gameOverState`（序列化字串，僅供 `JsonUtility` 序列化）。轉移時透過 helper `SetGameOverState(GameOverState s) { _gameOverState = s; _state.gameOverState = s.ToString(); }` 同步更新兩者；`RestoreFromSave` 反序列化後以 `Enum.Parse` 將字串還原為 `_gameOverState`。此策略避免「`if (_state.gameOverState != "Active")`」字串比較拼錯，同時維持 FT-10 ISaveable JSON schema 不變。

**`GuildLevelEntry`**（CSV row DTO，由 `GuildLevelDatabaseLoader` 生成）

```
public class GuildLevelEntry {
    public int level;
    public int reputationThreshold;
    public string title;
    public string maxDifficulty;          // [Deprecated]
    public string maxRecruitableRank;
    public string maxMissionDifficulty;
}
```

**`LevelUpPayload`**（內部 queue 元素，**不**序列化）

```
internal struct LevelUpPayload {
    public int fromLv;
    public int toLv;
    public int finalTargetLv;
    public bool isMultiJump;
    public int reputationAtUpgrade;
}
```

**`OnGuildInitializedEvent`**

```
public struct OnGuildInitializedEvent {
    public string displayName;
    public long foundingTimestamp;
}
```

**`OnGuildLoadedEvent`**

```
public struct OnGuildLoadedEvent {
    public string displayName;
    public int currentLevel;
}
```

**`OnGuildLevelChangedEvent`**（對齊 GDD §3.4 / §4.3）

```
public struct OnGuildLevelChangedEvent {
    public int fromLv;
    public int toLv;
    public string fromTitle;
    public string toTitle;
    public string newMaxDifficulty;          // [Deprecated]，等同 newMaxRecruitableRank
    public string newMaxRecruitableRank;
    public string newMaxMissionDifficulty;
    public int reputationAtUpgrade;
    public long upgradeTimestamp;            // 取出 queue 發送時 stamp
    public bool isMultiJump;
    public int finalTargetLv;
}
```

**`OnGameOverPendingEvent`**（對齊 GDD §3.7）

```
public struct OnGameOverPendingEvent {
    public long pendingTimestamp;
    public int finalGoldBeforeGameOver;
    public int finalReputation;
    public int finalLevel;
    public string finalTitle;
    public string guildDisplayName;
    public long foundingTimestamp;
}
```

**`OnGameOverEvent`**（對齊 GDD §3.8）

```
public struct OnGameOverEvent {
    public long pendingTimestamp;
    public int finalGoldBeforeGameOver;
    public int finalReputation;
    public int finalLevel;
    public string finalTitle;
    public string guildDisplayName;
    public long foundingTimestamp;
    public long confirmTimestamp;
}
```

### 5.4 內部資料流

#### 5.4-A 聲望變動 → 等級判定

```
F-03.ResourceManagement.UpdateReputation
  → EventBus.Publish<OnReputationChangedEvent>(newValue, delta)
      → GuildCoreService.HandleReputationChanged(evt)
          ├─ 守衛：if (_gameOverState != GameOverState.Active) return
          ├─ 步驟 1：targetLevel = _loader.FindTargetLevel(evt.newValue)
          ├─ 步驟 2：alreadyScheduledTop = _state.currentLevel + _pendingLevelUpQueue.Count
          ├─ 步驟 3：if (targetLevel <= alreadyScheduledTop) return     // 不降級／無新增級數
          ├─ 步驟 4：FOR k = (alreadyScheduledTop + 1) TO targetLevel:  // 補 enqueue 新增級數
          │             _pendingLevelUpQueue.Enqueue(new LevelUpPayload {
          │                 fromLv = k - 1,
          │                 toLv   = k,
          │                 finalTargetLv = targetLevel,
          │                 isMultiJump = (targetLevel - _state.currentLevel) > 1,
          │                 reputationAtUpgrade = evt.newValue
          │             })
          ├─ 步驟 5：FOR each existing payload in _pendingLevelUpQueue（含舊有與新加入）：
          │             payload.finalTargetLv = targetLevel                                  // 統一更新最終目標
          │             payload.isMultiJump = (targetLevel - _state.currentLevel) > 1        // 總跳數 > 1 即 true
          │             payload.reputationAtUpgrade = evt.newValue                           // 採用最新觸發點聲望
          └─ 步驟 6：（不在此 frame 發事件；Update 接手）
```

> **連跳期間再變動規則**（對應 GDD §3.4 擴充規則明細）：
> 1. **新增 queue（步驟 2~4）**：若新 `targetLevel > _state.currentLevel + _pendingLevelUpQueue.Count`（既有最終級），補 enqueue 缺漏級數；若不大於則完全忽略（不降級、不重排），步驟 3 直接 return。
> 2. **回填既有 payload（步驟 5）**：對 queue 內**所有** pending payload（含舊有與新加入的）統一更新 `finalTargetLv`、`isMultiJump`、`reputationAtUpgrade`——確保下游（P-02 動畫、P-03 toast）收到的所有連跳事件 `isMultiJump` 旗標一致、`reputationAtUpgrade` 反映最終觸發那次的聲望（敘事一致性）。
> 3. **fromLv / toLv 不更新**：每級的 `fromLv`／`toLv` 為原本排程時的等級值，擴充不改寫——維持「逐級遞增」的事件序列。

#### 5.4-B 連跳 queue 逐 frame 發送

```
Unity.Update
  → GuildCoreService.Update()
      ├─ if (_pendingLevelUpQueue.Count == 0):
      │     _levelUpFrameCounter = 0
      │     return
      ├─ if (_gameOverState != GameOverState.Active):
      │     // §5.2.2：Game Over 優先級高於升級動畫
      │     _pendingLevelUpQueue.Clear()
      │     _levelUpFrameCounter = 0
      │     return
      ├─ _levelUpFrameCounter += 1
      ├─ if (_levelUpFrameCounter < LEVEL_UP_QUEUE_INTERVAL_FRAMES): return
      ├─ _levelUpFrameCounter = 0
      ├─ payload = _pendingLevelUpQueue.Dequeue()
      ├─ _state.currentLevel = payload.toLv
      ├─ entryFrom = _loader.GetEntry(payload.fromLv)
      ├─ entryTo   = _loader.GetEntry(payload.toLv)
      ├─ evt = new OnGuildLevelChangedEvent {
      │     fromLv = payload.fromLv,
      │     toLv   = payload.toLv,
      │     fromTitle = entryFrom.title,
      │     toTitle   = entryTo.title,
      │     newMaxDifficulty = entryTo.maxDifficulty,
      │     newMaxRecruitableRank = entryTo.maxRecruitableRank,
      │     newMaxMissionDifficulty = entryTo.maxMissionDifficulty,
      │     reputationAtUpgrade = payload.reputationAtUpgrade,
      │     upgradeTimestamp = TimeSystem.Instance.NowUTC,
      │     isMultiJump = payload.isMultiJump,
      │     finalTargetLv = payload.finalTargetLv
      │ }
      └─ EventBus.Publish<OnGuildLevelChangedEvent>(evt)
```

#### 5.4-C 破產通知 → Game Over 階段 1

```
F-03.ResourceManagement.EvaluateBankruptcy
  → EventBus.Publish<OnBankruptcyStateChangedEvent>(previousState, currentState=Bankrupt)
      → GuildCoreService.HandleBankruptcyState(evt)
          ├─ 守衛：if (evt.CurrentState != BankruptcyWarningState.Bankrupt) return     // 忽略 PreviousState
          ├─ 守衛：if (_gameOverState != GameOverState.Active) return                  // 冪等：已 Pending/Over
          ├─ 步驟 1：SetGameOverState(GameOverState.Pending)        // helper：同步寫 _gameOverState enum + _state.gameOverState 字串
          ├─ 步驟 2：_pendingSnapshot = BuildGameOverSnapshot()      // 緩存於 field（§5.4-D 直接重用，不重 snapshot）
          │             ├─ pendingTimestamp = TimeSystem.Instance.NowUTC
          │             ├─ finalGoldBeforeGameOver = ResourceManagement.Instance.GetGold()
          │             ├─ finalReputation = ResourceManagement.Instance.GetReputation()
          │             ├─ finalLevel = _state.currentLevel
          │             ├─ finalTitle = _loader.GetEntry(_state.currentLevel).title
          │             ├─ guildDisplayName = _state.displayName
          │             └─ foundingTimestamp = _state.foundingTimestamp
          └─ 步驟 3：EventBus.Publish<OnGameOverPendingEvent>(_pendingSnapshot)
```

#### 5.4-D 玩家確認 → Game Over 階段 2

```
P-02.GameOverScreen.OnConfirmClicked
  → GuildCoreService.ConfirmGameOver()
      ├─ 守衛：if (_gameOverState != GameOverState.Pending):
      │           Debug.LogWarning($"ConfirmGameOver called in state {_gameOverState}")
      │           return     // 冪等
      ├─ 步驟 1：TimeSystem.Instance.PauseTick()
      ├─ 步驟 2：SetGameOverState(GameOverState.Over)        // helper：同步寫 _gameOverState enum + _state.gameOverState 字串
      ├─ 步驟 3：confirmTimestamp = TimeSystem.Instance.NowUTC（PauseTick 後 NowUTC 仍可讀，F-02 FSD §5.1 保證）
      └─ 步驟 4：EventBus.Publish<OnGameOverEvent>(_pendingSnapshot + confirmTimestamp)
                  // 沿用 §5.4-C 緩存的 _pendingSnapshot，不重新 snapshot——對齊 GDD §3.8「沿用階段 1 payload + confirmTimestamp」
```

> **設計選擇**：(1) `PauseTick()` 在發 `OnGameOver` **之前**呼叫，確保 invariant「`gameOverState == Over` ⇒ tick 已暫停」（GDD §4.5）；訂閱者收到事件時 tick 已停。(2) `_pendingSnapshot` 於 §5.4-C 緩存後不重建——`pendingTimestamp` / `finalGold` / `finalReputation` / `finalLevel` 等欄位反映「進入 Pending 那刻的快照」，與 `confirmTimestamp`（玩家確認瞬間）合併為 `OnGameOverEvent`。

#### 5.4-E 公會名稱輸入 → 組合

```
P-01.IntroScreen.OnGuildNameSubmit(rawInput)
  → GuildCoreService.SetGuildName(rawInput)
      ├─ 步驟 1：composed = GuildNameUtility.ComposeDisplayName(rawInput)
      │             ├─ stripped = StripControlChars(rawInput)
      │             ├─ trimmed = stripped.Trim()
      │             ├─ truncated = TruncateToFullwidthLimit(trimmed, GUILD_NAME_MAX_FULLWIDTH)
      │             ├─ if (string.IsNullOrEmpty(truncated)) return DEFAULT_GUILD_NAME
      │             └─ return truncated + GUILD_NAME_SUFFIX
      ├─ 步驟 2：_state.guildName = (rawInput 經 strip + trim + truncate 後的 raw 部分；不含後綴)
      └─ 步驟 3：_state.displayName = composed
```

#### 5.4-F 生命週期

```
GuildCoreService.Awake
  → 步驟 0：DataManager.Instance.RegisterTable<GuildLevelEntry>("GuildLevelTable")  // 對齊 F-01 FSD 2026-04-27 去中心化註冊
  → 步驟 1：_loader = new GuildLevelDatabaseLoader()
  → 步驟 2：_loader.Load()                       // 內部呼叫 DataManager.Instance.GetAll<GuildLevelEntry>() 並執行下方「Loader.Load 驗證項」
  → 步驟 3：_state = new GuildState()             // 待 InitializeAsNewGame 或 RestoreFromSave 填充
  → 步驟 4：_gameOverState = GameOverState.Active // runtime enum 預設；尚未呼叫 InitializeAsNewGame 前不對外有效
  → 步驟 5：_pendingSnapshot = default            // OnGameOverPendingEvent 緩存，§5.4-D 重用
  → 步驟 6：EventBus.Subscribe<OnReputationChangedEvent>(HandleReputationChanged)
  → 步驟 7：EventBus.Subscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyState)

GuildCoreService.InitializeAsNewGame                    // FT-10 Bootstrap 呼叫（首次遊玩；或 RestoreFromSave 收到 null 時內部委派）
  → 步驟 1：（若 SetGuildName 未先呼叫，displayName 已預設「公會」）
  → 步驟 2：_state.foundingTimestamp = TimeSystem.Instance.NowUTC
  → 步驟 3：_state.currentLevel = 1
  → 步驟 4：SetGameOverState(GameOverState.Active)         // helper：同步寫 _gameOverState enum 與 _state.gameOverState 字串
  → 步驟 5：EventBus.Publish<OnGuildInitializedEvent>(_state.displayName, _state.foundingTimestamp)

GuildCoreService.RestoreFromSave(ownerJson)             // FT-10 載入呼叫
  → 步驟 0：if (string.IsNullOrEmpty(ownerJson)):          // 對齊 FT-10 GDD §3.7 ISaveable 契約：null 視為首次遊玩
  │             InitializeAsNewGame(); return
  → 步驟 1：_state = JsonUtility.FromJson<GuildState>(ownerJson)
  → 步驟 2：驗證 _state != null、currentLevel ∈ [1,5]、gameOverState ∈ {"Active","Pending","Over"}；違規拋例外
  → 步驟 3：_gameOverState = (GameOverState)Enum.Parse(typeof(GameOverState), _state.gameOverState)   // runtime enum 同步（步驟 2 已驗證字串值域）
  → 步驟 4：（不重判等級、不補發連跳、不重發 Pending／Over 事件；_pendingSnapshot 維持 default——
              Pending／Over 還原態下 P-02 透過 IsGameOverPending() / IsGameOver() 自行查詢顯示，不重發事件）
  → 步驟 5：EventBus.Publish<OnGuildLoadedEvent>(_state.displayName, _state.currentLevel)

GuildCoreService.OnDestroy
  → EventBus.Unsubscribe<OnReputationChangedEvent>(HandleReputationChanged)
  → EventBus.Unsubscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyState)
```

> **Loader.Load 驗證項**（對齊 GDD §5.1.3 / §7.4 / §1.3 DoD-1；任一違規 `Debug.LogError` + `throw InvalidDataException`）：
>
> 1. `entries.Count >= 1`（非空）
> 2. `entries[0].level == 1 && entries[0].reputationThreshold == 0`
> 3. `entries.Select(e => e.level)` 嚴格升冪且連續（1, 2, 3, ...）
> 4. `entries.Select(e => e.reputationThreshold)` 嚴格升冪
> 5. `entries.Select(e => e.maxRecruitableRank)` 單調不降（依 C-01 難度軸序：F < E < D < C < B < A < S）
> 6. `entries.Select(e => e.maxMissionDifficulty)` 單調不降（依 C-01 難度軸序：F < E < D < C < B < A < S < SS < SSS）
> 7. 所有 `entries[k].title` 為非空字串
>
> 驗證後 Loader 自建 `Dictionary<int, GuildLevelEntry> _byLevel`（key = `level`）供 `GetEntry(level)` 與 `FindTargetLevel(rep)` 使用——F-01 不提供主鍵索引 API（F-01 FSD 2026-04-27），下游自建 dictionary 為標準模式。

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `GuildLevelTable` | `level`、`reputationThreshold`、`title`、`maxDifficulty`、`maxRecruitableRank`、`maxMissionDifficulty` | `【FT-06-DS】guild-level-table.md`（_待建_） | 等級判定門檻、UI 稱號、容量 API 回傳值 | `GuildCoreService.Awake` 內先呼叫 `DataManager.Instance.RegisterTable<GuildLevelEntry>("GuildLevelTable")`，再由 `GuildLevelDatabaseLoader.Load` 透過 `DataManager.Instance.GetAll<GuildLevelEntry>()` 取得 `IReadOnlyList<GuildLevelEntry>` 並自建 `Dictionary<int, GuildLevelEntry>`（對齊 F-01 FSD 2026-04-27 去中心化機制：F-01 不提供 `GetTable<T>`） |

### 6.2 引用的 ScriptableObject

無。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 等級 1~5 的聲望門檻（`reputationThreshold`） | `GuildLevelTable.reputationThreshold` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 等級稱號字串（`title`） | `GuildLevelTable.title` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 老手招募階級上限（`maxRecruitableRank`） | `GuildLevelTable.maxRecruitableRank` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 任務難度上限（`maxMissionDifficulty`） | `GuildLevelTable.maxMissionDifficulty` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 連跳間隔 frame 數（`LEVEL_UP_QUEUE_INTERVAL_FRAMES`） | `GuildCoreConstants.LEVEL_UP_QUEUE_INTERVAL_FRAMES` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 公會名稱長度上限（`GUILD_NAME_MAX_FULLWIDTH`） | `GuildCoreConstants.GUILD_NAME_MAX_FULLWIDTH` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 公會名稱後綴（`GUILD_NAME_SUFFIX`） | `GuildCoreConstants.GUILD_NAME_SUFFIX` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 預設名稱（`DEFAULT_GUILD_NAME`） | `GuildCoreConstants.DEFAULT_GUILD_NAME` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| ISaveable owner key（`OWNER_KEY = "ft06Guild"`） | `GuildCoreConstants.OWNER_KEY` | 對應「四、程式實作原則」第 9 條：參數表格化 |

> 連跳順序、不降級、Game Over 兩階段等屬於設計決策（GDD §7.3），實作以程式邏輯固定，不歸入此表。

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1.1 聲望回跌 | `HandleReputationChanged` 比較 `targetLevel <= _state.currentLevel` 即 return，不發事件不調 `currentLevel` | `GuildCoreService` | EditMode 測試：模擬 rep 700→250，驗證 `OnGuildLevelChangedEvent` 未被發布、`GetCurrentLevel` 不變 |
| §5.1.2 聲望為負 | 同上分支（`targetLevel = 1 <= currentLevel`） | `GuildCoreService` | EditMode：注入 rep = -10、初始 Lv1，驗證無事件、無 log |
| §5.1.3 表設定錯誤（Lv1 threshold ≠ 0） | `GuildLevelDatabaseLoader.Load` 驗證 `entries[0].reputationThreshold == 0`，違規 `Debug.LogError` + `throw InvalidDataException` | `GuildLevelDatabaseLoader` | EditMode：注入錯誤 CSV，驗證 `Awake` 拋例外 |
| §5.1.4 Lv5 後再加聲望 | `targetLevel == currentLevel == 5`，§5.1.1 同分支 return | `GuildCoreService` | EditMode：Lv5 + rep 從 1500 升至 3000，驗證無事件 |
| §5.2.1 連跳期間遊戲被 PauseTick | `Update()` 不依賴 `TimeSystem.Tick`；Unity 引擎 `Update()` 不受 `F-02 PauseTick` 影響 | `GuildCoreService.Update` | PlayMode：在連跳中呼叫 `PauseTick`，驗證 `OnGuildLevelChangedEvent` 仍逐 frame 發出 |
| §5.2.2 連跳中觸發 Game Over | `Update()` 開頭檢查 `_gameOverState != GameOverState.Active` → `Clear()` queue 並 return；`HandleBankruptcyState` 修改狀態後下一 frame `Update` 即清空 | `GuildCoreService.Update` | PlayMode：佈置 4 級 queue，第 2 級發送後注入 Bankrupt 事件，驗證後續 2 級不發 |
| §5.2.3 存檔時 queue 非空 | `Serialize` 只寫 `_state` 5 欄位，`_pendingLevelUpQueue` 為 runtime-only；`RestoreFromSave` 不重建 queue | `GuildCoreService` ISaveable | EditMode：佈置 queue 非空、呼叫 `Serialize`，驗證 JSON 不含 queue；`RestoreFromSave` 後 queue 為空 |
| §5.3.1 玩家永不確認訃聞 | `gameOverState = Pending` 仍會被 `Serialize` 寫入；下次讀檔由 P-02 透過 `IsGameOverPending` 重新顯示訃聞；FT-06 `RestoreFromSave` 不重發 `OnGameOverPending` | `GuildCoreService` ISaveable | EditMode：序列化 Pending 態 + 反序列化，驗證 `IsGameOverPending == true`、`OnGameOverPendingEvent` 未發 |
| §5.3.2 重複收到 Bankrupt | `HandleBankruptcyState` 守衛 `_gameOverState != GameOverState.Active` → return；冪等 | `GuildCoreService` | EditMode：連發兩次 `OnBankruptcyStateChangedEvent(_, Bankrupt)`，驗證 `OnGameOverPendingEvent` 只發一次 |
| §5.3.3 `ConfirmGameOver` 在非 Pending 呼叫 | `ConfirmGameOver` 守衛 `_gameOverState != GameOverState.Pending` → `Debug.LogWarning` + return | `GuildCoreService` | EditMode：在 Active／Over 狀態呼叫，驗證 `OnGameOverEvent` 未發、log 出現 warning |
| §5.3.4 Pending 期間收聲望變化 | `HandleReputationChanged` 守衛 `_gameOverState != GameOverState.Active` → return | `GuildCoreService` | EditMode：Pending 態下注入 `OnReputationChangedEvent`，驗證 `OnGuildLevelChangedEvent` 未發 |
| §5.4.1 純空白輸入 | `ComposeDisplayName` 內 `Trim` 後 empty → 回傳 `DEFAULT_GUILD_NAME` | `GuildNameUtility` | EditMode：`ComposeDisplayName("   ") == "公會"` |
| §5.4.2 超長字串 | `TruncateToFullwidthLimit` 達 8 全型即 break；不拋錯 | `GuildNameUtility` | EditMode：`ComposeDisplayName("我的冒險者公會管理員") == "我的冒險者公會管公會"` |
| §5.4.3 控制字元 / Rich Text | `StripControlChars` 過濾 `UnicodeCategory.Control`；Rich Text 標籤逐字計入（不解析） | `GuildNameUtility` | EditMode：`ComposeDisplayName("約翰\n的") == "約翰的公會"`、`ComposeDisplayName("<b>約翰的</b>") == "<b>約翰的</b>公會"` |
| §5.4.4 輸入「公會」 | 視同一般字元，`displayName == "公會公會"` | `GuildNameUtility` | EditMode：`ComposeDisplayName("公會") == "公會公會"` |
| §5.5.1 FT-06 早於 F-03 初始化 | `EventBus.Subscribe` 為 lazy，先訂閱無妨；F-03 後續發布事件時正常路由 | EventBus（Core 層保證） | PlayMode：DefaultExecutionOrder 安排 FT-06 先於 F-03，驗證首次 rep 變動正常觸發 |
| §5.5.2 FT-06 銷毀仍訂閱 | `OnDestroy` 解除所有 `EventBus.Subscribe`；對稱 | `GuildCoreService.OnDestroy` | EditMode：`Destroy(guildCoreGameObject)` 後注入事件，驗證無 null ref |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 系統狀態（GuildState） | §5.3、§4.4（Types） | 對齊 | 5 序列化欄位 + runtime-only queue |
| §3.2 初始化流程（新遊戲 / 讀檔） | §5.4-F、§5.1 ISaveable | 對齊 | `InitializeAsNewGame` / `RestoreFromSave` 末段發事件、不重發 Pending／Over |
| §3.3 公會等級判定演算法 | §5.4-A、§7（§5.1.1） | 對齊 | 守衛 `gameOverState != Active` early return |
| §3.4 連跳處理（Multi-Jump Queue） | §5.4-B、§5.3（payload）、§4.4 Update | 對齊 | 11 欄位 payload、`_levelUpFrameCounter` 節流、queue 擴充規則於 §5.4-A 補述 |
| §3.5 公會等級表 | §6.1、§7.4 驗證 | 對齊 | 6 欄位、Loader 驗證 |
| §3.6 容量查詢 API | §5.1 | 對齊 | 7 個查詢 + 1 deprecated alias |
| §3.7 Game Over 階段 1（Pending） | §5.4-C、§5.3 payload、§7（§5.3.2） | 對齊 | 6 欄位 snapshot、冪等 |
| §3.8 Game Over 階段 2（Over） | §5.4-D、§5.3 payload | 對齊 | `PauseTick` 在發事件前；FT-10 封存契約僅靠 `OnGameOver` |
| §3.9 公會名稱處理 | §5.4-E、§4.4 GuildNameUtility | 對齊 | strip + trim + truncate + 後綴 |
| §3.10 事件發布契約 | §5.2 | 對齊 | 5 個發布事件、訂閱者列舉 |
| §3.11 查詢 API 總表 | §5.1 | 對齊 | 含 `IsGameOverPending` / `IsGameOver` / `ConfirmGameOver` |

### 8.2 公式對齊或替代說明

- §4.1 等級判定公式：採 GDD 偽碼「從高到低線性掃描，O(5)」，於 `GuildLevelDatabaseLoader.FindTargetLevel` 實作；無替代。
- §4.2 升級判定公式：採 GDD 偽碼，於 `GuildCoreService.HandleReputationChanged` §5.4-A 步驟 2~4 實作；無替代。
- §4.3 連跳事件序列生成：採 GDD 偽碼，於 §5.4-A 步驟 5（enqueue）+ §5.4-B（dequeue 取出時 stamp `upgradeTimestamp`）兩階段實作；`upgradeTimestamp` 在發送瞬間 stamp，符合 GDD 注意事項。
- §4.4 公會名稱長度驗證：採 GDD 偽碼 `IsValidGuildName` + `CountDisplayChars` + `IsFullWidth`，於 `GuildNameUtility` 實作；防禦性處理走 `ComposeDisplayName` 靜默截斷（GDD §3.9 / §5.4.2 一致）；無替代。
- §4.5 Game Over 狀態轉移：採 GDD 狀態機，invariant「Over ⇒ tick paused」「!Active ⇒ 不處理 OnReputationChanged」於 §5.4-A／§5.4-D 實作；無替代。

### 8.3 未能實現的規則與修改建議

| 編號 | 項目 | 影響 | 建議處理 |
| --- | --- | --- | --- |
| B-01 | `【FT-06-DS】guild-level-table.md` Data-Specs 尚未建立 | FSD §0 / §2.2 / §6.1 三處 Data-Specs 引用標「_待建_」；FSD-index §6.1 / §6.2 待建檔後同步補連結 | 由 DS-designer 後續以 `/design-DS FT-06` 補建；不阻礙 FSD 撰寫與後續實作（CSV 檔案路徑與欄位名已於本 FSD §6.1 / GDD §3.5 / §7.1 明確定義） |
| B-02 | `BankruptcyWarningState` enum 來源 | FT-06 訂閱 F-03 `OnBankruptcyStateChangedEvent`，需引用 F-03 定義的 `BankruptcyWarningState` enum；此 enum 應位於 F-03 Script（`Assets/Scripts/Gameplay/Resources/`）並由 FT-06 import | 實作時直接引用 `BankruptcyWarningState`（位於 F-03）；若 F-03 FSD 規劃將 enum 移入共用 Types 檔，FT-06 同步 import 路徑。本 FSD 不另定義 enum |
| ~~B-03~~ | ~~`GuildLevelTable` 透過 `DataManager.GetTable<T>` 載入的具體 API 形式~~ | **2026-04-27 已修正**：F-01 FSD（2026-04-27 更新）明文「F-01 不新增 `GetTable<T>` API，下游一律以 `GetAll<T>` + 自建 dictionary 為準」 | 已修正：§2.3 / §5.4-F / §6.1 全面改採 `RegisterTable<GuildLevelEntry>("GuildLevelTable")` + `GetAll<GuildLevelEntry>()` 模式；`GuildLevelDatabaseLoader` 自建 `Dictionary<int, GuildLevelEntry>` 提供 `GetEntry(level)` / `FindTargetLevel(rep)`。詳見 §8.5 衝突 003 |
| B-04 | GDD §3.4 payload `newMaxDifficulty` 標 [Deprecated]、下次 review 應移除 | 本 FSD 沿用 GDD：保留欄位於 `OnGuildLevelChangedEvent` 與 `GetMaxDifficulty()` API | 下次 review FT-06 時統一移除：(1) `OnGuildLevelChangedEvent.newMaxDifficulty` 欄位、(2) `GetMaxDifficulty()` API、(3) `GuildLevelTable.maxDifficulty` 欄位、(4) GDD §3.5 / §7.1 對應描述。本 FSD 不主動移除以維持向後相容 |
| B-05 | §3.7 Pending 期間「不接受新任務、不結算」由 P-02 UI 阻塞 | FT-06 不涉入；GDD 明文 P-02 責任 | 屬 P-02 設計範疇；FT-06 僅保證 `IsGameOverPending` 旗標可被 P-02 查詢 |

> **無真實阻礙性衝突**；以上五項皆為待補項或外部依賴，不影響 FSD 完成性與後續實作啟動。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | （本次撰寫無對 GDD 的回註；GDD §3.4 連跳期間 payload `finalTargetLv` 擴充規則已於本 FSD §5.4-A 補述，不需回註 GDD） |

### 8.5 衝突處理紀錄

| 日期 | 衝突 ID | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- | --- |
| 2026-04-27 | 001 (C1) | F-03 FSD 事件名為 `OnBankruptcyStateChangedEvent`（無 "Warning"），FT-06 GDD §3.7 與 FSD 草稿全文使用 `OnBankruptcyWarningStateChangedEvent` | F-03 FSD §5.2 / §6.3；FT-06 GDD §3.7、FT-06 FSD 草稿 §2.3 / §2.5 / §4.5 / §5.2 / §5.4-C / §5.4-F / §7（§5.3.2）/ §8.3 B-02 | 以 F-03 FSD（已實作）為準：FSD 全面改為 `OnBankruptcyStateChangedEvent`。FT-06 GDD §3.7 / §6.1 / §6.3 由反向依賴批次更新（與 FT-05 / FT-08 同批） |
| 2026-04-27 | 002 (C2) | F-03 payload 為 `(BankruptcyWarningState PreviousState, BankruptcyWarningState CurrentState)` 雙欄位，GDD §3.7 / FSD 草稿以單欄位 `BankruptcyWarningState newState` 描述 | F-03 FSD §5.2、F-03 GDD FSD 回註（§3.4 末尾 D3）；FT-06 GDD §3.7；FT-06 FSD §2.3 / §2.5 / §5.2 / §5.4-C | 以 F-03 實作為準（superset 向後兼容）：FSD §5.4-C 守衛改 `evt.CurrentState != BankruptcyWarningState.Bankrupt`；§2.5 / §5.2 payload 表改雙欄位並註明「FT-06 僅讀 `CurrentState`、忽略 `PreviousState`」。GDD §3.7 由反向依賴批次更新 |
| 2026-04-27 | 003 (C3) | F-01 FSD（2026-04-27 更新）明文「F-01 不新增 `GetTable<T>` API」，FT-06 FSD 草稿 §2.3 / §6.1 / §5.4-F 仍以 `GetTable<T>` 載表 | F-01 FSD §2.4 / §3.1（去中心化註冊機制）；FT-06 FSD 草稿 §2.3 / §6.1 / §5.4-F；§8.3 B-03 | FSD 全面改採 `DataManager.RegisterTable<GuildLevelEntry>("GuildLevelTable")` + `GetAll<GuildLevelEntry>()` 模式；`GuildLevelDatabaseLoader` 自建 `Dictionary<int, GuildLevelEntry>` 索引；§8.3 B-03 改為「已修正」並 cross-link 此衝突 |
| 2026-04-27 | 004 (C4) | GDD §3.8 規定「OnGameOver payload：沿用階段 1 payload + confirmTimestamp」，FSD 草稿 §5.4-D 卻在 ConfirmGameOver 內呼叫 `BuildGameOverSnapshot()` 重新 snapshot，會覆蓋 `pendingTimestamp` / `finalGold` / `finalReputation` / `finalLevel` 等欄位 | FT-06 GDD §3.8；FT-06 FSD 草稿 §5.4-C / §5.4-D | 以 GDD 為準：§5.4-C 觸發 Pending 時把 snapshot 緩存於 `_pendingSnapshot` field（§5.4-F Awake 步驟 5 初始化）；§5.4-D 直接重用 `_pendingSnapshot + confirmTimestamp` 發 `OnGameOverEvent`，不重新 snapshot |
| 2026-04-27 | 005 (C5) | FT-10 ISaveable 契約規定 `RestoreFromSave(ownerJson)` 收到 `null` 必須走 `InitializeAsNewGame()` 並 return；FSD 草稿 §5.4-F 步驟 1 直接 `JsonUtility.FromJson<GuildState>(ownerJson)` 無 null 守衛，會 NPE | FT-10 GDD §3.7（ISaveable 介面）；FT-06 FSD 草稿 §5.4-F | §5.4-F `RestoreFromSave` 增補步驟 0：`if (string.IsNullOrEmpty(ownerJson)) { InitializeAsNewGame(); return; }`；§5.1 ISaveable 表將檔名 typo `IsSaveable.cs` 修正為 `ISaveable.cs` |
| 2026-04-27 | 006 (I1) | `GuildState.gameOverState` 為 `string` 序列化欄位，但 FSD 草稿偽碼大量寫成 `_state.gameOverState != Active`（無引號）會被 C# 視為 enum 比較且編譯失敗 | FT-06 FSD 草稿 §5.4-A / §5.4-B / §5.4-C / §5.4-D / §5.4-F / §7（§5.2.2 / §5.3.2 / §5.3.4） | 採雙欄位策略：runtime 用 `private GameOverState _gameOverState`（enum，所有比較／轉移使用）+ `_state.gameOverState`（字串，僅供 JsonUtility 序列化）；以 `SetGameOverState(GameOverState s)` helper 同步寫入兩者；`RestoreFromSave` 用 `Enum.Parse` 從字串還原 enum。§5.3 補「runtime / 序列化雙欄位策略」說明區塊 |
| 2026-04-27 | 007 (I2) | GDD §3.4 連跳期間擴充 queue 規則僅敘述「擴充 queue + 更新 finalTargetLv」，未明確 `isMultiJump` 與 `reputationAtUpgrade` 是否同步更新 | FT-06 GDD §3.4；FT-06 FSD 草稿 §5.4-A | §5.4-A 偽碼補步驟 5：擴充時所有 pending payload（含舊有與新加入）統一更新 `finalTargetLv = targetLevel`、`isMultiJump = (totalJumps > 1)`、`reputationAtUpgrade = evt.newValue`（採用最新觸發點，敘事一致）。`fromLv`／`toLv` 不更新（維持逐級遞增）。後續 GDD §3.4 由反向依賴批次補述 |
| 2026-04-27 | 008 (I3) | GDD §5.1.3 列出 Lv1 threshold ≠ 0 的防禦驗證，FSD 草稿 §5.4-F Loader.Load 僅敘述「讀 DataManager + 驗證」未列具體驗證項 | FT-06 GDD §5.1.3 / §7.4；FT-06 FSD 草稿 §5.4-F | §5.4-F 末尾新增「Loader.Load 驗證項」7 條清單（非空 / Lv1.threshold==0 / level 連續 / threshold 嚴格升冪 / maxRecruitableRank 單調不降 / maxMissionDifficulty 單調不降 / title 非空），對齊 GDD §5.1.3 與 §7.4，可直接轉 EditMode 測試 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist

- [x] §0 文件資訊：對應 GDD、Data-Specs、撰寫者／Review 者／狀態／日期填妥
- [x] §1.3 完成目標：8 條 DoD 皆可被 EditMode／PlayMode 測試或手動步驟驗證
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：覆蓋 GDD §2 全部幻想／目的（10 條對映）
- [x] §4 Script 清單：5 Script，欄位（路徑、SRP、依賴、規模）齊全；無單檔 > 500 行
- [x] §5 API/事件/資料結構/資料流：API 12 條、事件 7 條、資料結構 8 個 struct/class/enum、資料流 6 段（A~F）齊備
- [x] §6 CSV 引用含對應 Data-Specs（雖標 _待建_，欄位完整）；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例：GDD §5.1~§5.5 共 16 條案例皆有對策、涉及 Script、驗證方式
- [x] §8.1 對齊清單：覆蓋 GDD §3.1~§3.11 二層粒度
- [x] §8.2~§8.5 公式對齊／無法實現項／GDD 回註／衝突紀錄如實登記
- [x] FSD-index：§6.1 三方映射、§7.1 撰寫進度、§7.2 自檢紀錄將同步更新

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | Claude Code 主體（自檢） | 通過 | 通過 | 通過 | 正向 FSD（無既有 Guild Script）；FSD 未拆分（5 Script）；建議項 B-01 ~ B-05 皆不阻礙實作 |
| 2026-04-27 | Claude Code 主體（design-review 修補後重檢） | 通過 | 通過 | 通過 | C1~C5 / I1~I3 共 8 條跨系統衝突／實作缺陷已修補：F-03 事件正名為 `OnBankruptcyStateChangedEvent` + payload 雙欄位、F-01 載表改 `RegisterTable<T>` + `GetAll<T>` 去中心化模式、Game Over snapshot 緩存於 `_pendingSnapshot` field、`RestoreFromSave` 補 `IsNullOrEmpty` 守衛、runtime 用 `GameOverState` enum（雙欄位策略）、queue 擴充規則明列（finalTargetLv / isMultiJump / reputationAtUpgrade 統一回填）、Loader.Load 補 7 項驗證；§8.5 補登 8 筆衝突處理紀錄；§8.3 B-03 改為「已修正」；待主體覆核後轉「已完成」 |
