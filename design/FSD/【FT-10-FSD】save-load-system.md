# 【FT-10-FSD】功能規格說明書 — Save/Load System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
|---|---|
| 對應 GDD | `【FT-10】save-load-system.md`（版本：2026-04-27 design-review 通過；2026-05-02 D-01 patch 補 C-02 序列化 gender/bio） |
| 對應 Data-Specs | `【F-01-DS】system-constants.md`（消費端：`SAVE_AUTO_INTERVAL_SEC` / `SAVE_BACKUP_COUNT` / `SAVE_FILE_NAME` / `SAVE_BAK_PREFIX` / `SAVE_GAMEOVER_PREFIX` 共 5 keys，§7.2 規範） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體（Opus 4.7 + xhigh） |
| 狀態 | 審查中（v1.1 patched 2026-05-02） |
| 最近更新 | 2026-05-02 |

## 1. 概要（Overview）

### 1.1 系統範圍

FT-10 Save/Load System 為公會所有持久化資料的統一存取層，採 Unity `Application.persistentDataPath` + 單一 JSON 主存檔（`save.json`）+ N 份 backup rotation 的檔案佈局；以「事件標記 dirty + 節流自動寫入 + 強制寫入」三軌觸發策略處理 ALL owner 系統的序列化抽取與反序列化還原；並負責 Bootstrap 五階段順序協調、損毀回退鏈、Game Over 終末檔封存、`ResetToNewGame` 流程。本系統不擁有任何業務狀態，僅作 read / write 編解碼與 I/O。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：

- 三軌寫入策略（事件標記 / 節流 / 強制）的觸發判定與互斥保護
- Bootstrap Phase A~E 五階段（讀檔 → schemaMeta 解析 → per-owner 還原 → F-02 離線交棒 → 完成事件）
- Owner 還原拓撲順序（F-03 → C-06 → C-02 → FT-06 → FT-07 → FT-02 → FT-08 → FT-12 → FT-01 → FT-09 → FT-03）
- Critical / Degradable 失敗分流（critical 觸發整檔回退；degradable 走 `InitializeAsNewGame()`）
- 原子寫入（`save.tmp` → `File.Replace` → `save.bak1`）與 backup rotation（先 rotate 後 Replace）
- 損毀回退鏈（`save.json → bak1 → ⋯ → bak{N}`）
- Game Over 封存（強制寫入 + 終末檔複製 + `OnGameSealed` 發布；保留主存檔 Over 旗標）
- `ResetToNewGame()`（刪主存檔 + 全 backup；終末檔不動；發布 `OnGameReset`）
- `ISaveable` 契約定義（`OwnerKey` / `IsCritical` / `Serialize` / `RestoreFromSave` / `InitializeAsNewGame`）
- 事件訂閱與 `OnDestroy` 對稱解除
- SystemConstants 5 個 SAVE_* keys 載入與安全範圍 clamp

**Out-of-Scope**：

- 多槽存檔 UI（Post-Jam）
- 雲端同步 / Steam Cloud（Post-Jam）
- Schema versioning / 跨版本遷移腳本（Post-Jam；FT-10 GDD §1 既定不引入 `version: int`）
- 存檔加密 / 防作弊簽章（Post-Jam）
- 收支日誌持久化（FT-05 Jam 範疇外）
- 結算畫面 / 離線摘要 UI（屬 P-02 / FT-04 範疇）
- 任務結算邏輯（FT-04 接收 `OnOfflineResolved` 自處）
- 跨平台 atomic primitive（Jam Windows-only）
- Scene reload（責任歸 P-02 訂閱 `OnGameReset` 後執行）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 八子節 AC 共 38 條（AC-1.1~1.6 / AC-2.1~2.7 / AC-3.1~3.5 / AC-4.1~4.3 / AC-5.1~5.2 / AC-6.1~6.2 / AC-7.1~7.3 / AC-EC-1~12）；以下為程式可驗證的 DoD 條目（每條對應一組 AC 並可由 EditMode / PlayMode 測試案例覆蓋）：

- **DoD-01**：訂閱 §3.2.1 全部 11 類事件後，`OnGoldChanged` 觸發只標記 `_isDirty = true`、`save.json` mtime 不變（AC-1.1）
- **DoD-02**：`SAVE_AUTO_INTERVAL_SEC = 60` 且 `_isDirty = true` 時，距離上次寫入 ≥ 60 秒節流檢查觸發 `ExecuteSave`、`save.json` 落地、`OnSaveCompleted` 發布（AC-1.2）；無 dirty 時不寫入但更新 `_lastSaveTime`（AC-1.3）
- **DoD-03**：`OnApplicationQuit` 與 FT-06 `OnGameOver` 觸發強制寫入忽略 `_isDirty`（AC-1.4 / AC-1.5）；`_isSaving` 互斥保護防止並行寫入（AC-1.6）
- **DoD-04**：Script Execution Order F-01 = -200 / FT-10 = -100 / 其他 owner = 0（AC-2.1）；Bootstrap Phase A→E 順序正確（AC-2.2）；owner 還原順序對齊 §3.3.3（AC-2.3）
- **DoD-05**：Critical owner（F-03 / C-02 / FT-06 / FT-08 / FT-12）`RestoreFromSave` 拋例外觸發 backup 回退、`loadedFromBackupIndex` 自增（AC-2.4 / AC-EC-3）；Degradable owner（C-06 / FT-01 / FT-02 / FT-03 / FT-07 / FT-09）拋例外 log warning + `InitializeAsNewGame()` 不阻擋 Bootstrap（AC-2.5 / AC-EC-4）
- **DoD-06**：Phase D 呼叫 `F-02.Initialize(lastActiveTimestamp)` 觸發離線計算（AC-2.6）；首次遊玩 `HasSaveFile() == false` → Phase E 統一發布 `OnLoadCompleted(-1)`（AC-2.7）
- **DoD-07**：`ExecuteSave` 中介過程出現 `save.tmp`、完成後 `save.json` 為新版本、舊 `save.json` 進入 `save.bak1`、`save.tmp` 被 `File.Replace` 消費不殘留（AC-3.1）；連續 5 次寫入 + `SAVE_BACKUP_COUNT = 3` 時 save=gen-5 / bak1=gen-4 / bak2=gen-3 / bak3=gen-2、gen-1 已覆寫消失（AC-3.2）
- **DoD-08**：損毀回退鏈順序 `save.json → bak1 → bak2 → bak3`、單份失敗 log warning 跳到下一份、成功時 `loadedFromBackupIndex` 對應索引（AC-3.3）；全部失敗先發布 `OnLoadFailed(ex)` 再發布 `OnLoadCompleted(-1)`（AC-3.4 / AC-EC-2）；正式 UI 不彈出對話框（AC-3.5）
- **DoD-09**：`OnGameOver` 觸發 §3.5.1 三步驟：強制寫入 → `File.Copy` 終末檔（命名 `save_gameover_<unix>.json`）→ 發布 `OnGameSealed`（AC-4.1）；主存檔保留 `gameOverState = "Over"` 旗標（AC-4.2）；下次啟動 `IsGameOver() == true`、F-02 不發 `OnMissionCompleted`（AC-4.3 / AC-EC-7）
- **DoD-10**：`ResetToNewGame()` 刪主存檔 + 全 backup、終末檔保留、發布 `OnGameReset`（AC-5.1）；P-02 訂閱後 reload scene 走首次遊玩流程（AC-5.2）
- **DoD-11**：`Dictionary<K,V>` / `Queue<T>` / `HashSet<T>` 透過 owner 端 `Serialize` / `RestoreFromSave` 內 List 中介轉換來回正確（AC-6.1 / AC-6.2）
- **DoD-12**：`MarkDirty` / `ForceSave` / `HasSaveFile` / `IsGameOver` / `GetLoadedFromBackupIndex` / `ResetToNewGame` 公開 API 行為符合 §3.7.1 規格（AC-7.1 / AC-7.2）；標準 Bootstrap 事件順序為 owner-restored → `OnLoadCompleted`（AC-7.3）
- **DoD-13**：12 條 EC 皆可被 PlayMode / EditMode 測試或 console 觀察驗證（AC-EC-1~AC-EC-12）；EC-1 寫入閃退、EC-5 寫入無權限、EC-9 時鐘倒退等以 mock / fake 注入驗證
- **DoD-14**：`SAVE_AUTO_INTERVAL_SEC ∈ [10, 600]`、`SAVE_BACKUP_COUNT ∈ [1, 10]` clamp 由 FT-10 自身於 `InitTuning` 執行；CSV 缺漏 fail-safe 至 §7.2 預設值

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要與三大職責（Persistence Pipeline / Bootstrap Orchestration / Game Over Sealing） → FSD §1 / §3.2 / §4 / §5.4
- §2 玩家幻想（情緒節點 + 設計原則） → FSD §3
- §3.1 檔案佈局與 SaveData Schema（§3.1.1 路徑 / §3.1.2 Root JSON Schema / §3.1.3 不參與序列化系統） → FSD §5.3 / §6
- §3.2 三軌寫入策略（§3.2.1 事件 / §3.2.2 節流 / §3.2.3 強制 / §3.2.4 互斥） → FSD §5.1 / §5.2 / §5.4 / §7
- §3.3 Bootstrap 順序（§3.3.1 Execution Order / §3.3.2 五階段 / §3.3.3 拓撲順序 / §3.3.4 critical/degradable） → FSD §5.4 / §7
- §3.4 Backup Rotation 與損毀回退（§3.4.1 原子寫入 / §3.4.2 回退鏈 / §3.4.3 玩家可見性） → FSD §5.4 / §7
- §3.5 Game Over 封存與 Reset（§3.5.1 OnGameOver / §3.5.2 ResetToNewGame） → FSD §5.4
- §3.6 序列化容器規範（§3.6.1 容器轉換 / §3.6.2 ISaveable） → FSD §5.3
- §3.7 API 與 Runtime 狀態（§3.7.1 公開 API / §3.7.2 事件契約 / §3.7.3 runtime 欄位 / §3.7.4 owner 註冊） → FSD §5.1 / §5.2 / §5.3
- §4.1~§4.5 公式（節流判定 / lastActiveTimestamp / 終末檔名 / rotation 索引 / 回退索引） → FSD §5.4 / §8.2
- §5 邊緣案例（EC-1~EC-12 + §5.13 索引） → FSD §7
- §6 依賴關係（§6.1 上游 / §6.2 下游 / §6.3 事件矩陣 / §6.4 反向依賴） → FSD §2.3 / §2.4 / §2.5
- §7 可調參數（§7.1~§7.5） → FSD §6
- §8 驗收標準（AC-1.1~AC-EC-12，38 條） → FSD §1.3

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md`（消費端） | `SystemConstants.csv` | `SAVE_AUTO_INTERVAL_SEC` / `SAVE_BACKUP_COUNT` / `SAVE_FILE_NAME` / `SAVE_BAK_PREFIX` / `SAVE_GAMEOVER_PREFIX` | 三軌節流間隔 / backup 代數 / 主存檔名 / backup 前綴 / 終末檔前綴；`InitTuning` 載入時於 [10,600] / [1,10] clamp（GDD §7.5） |

> **採 Service 介面命名約定**（FSD-index §2.10）：本 FSD 敘述中保留 `IDataManager` / `ITimeService` / `IResourceService` / `IEventBus` 命名；實作 PR 直接呼叫 `DataManager.Instance` / `TimeSystem.Instance` / `ResourceManagement.Instance` / static `EventBus`，不新增 interface 包裝。

### 2.3 上游依賴系統

| 上游系統 | 消費介面 / 資料 | 用途 |
|---|---|---|
| F-01 DataManager | `IDataManager.GetSystemConstant<T>(key, default)` / 各 owner 的 `Get<T>` / `GetAll<T>`（owner 端呼叫） | FT-10 自身載入 5 個 SAVE_* 常數；各 owner 在 `RestoreFromSave` 內驗證 ID 合法性（FT-10 不直接呼叫） |
| F-02 Time System | `ITimeSystem.Initialize(long lastActiveTimestamp)` | Phase D 離線計算交棒；FT-10 為唯一呼叫者 |
| F-03 Resource Mgmt | `ISaveable` 實作 | Phase C 第 1 順位還原 6 個欄位 |
| C-02 Adventurer Mgmt | `ISaveable` 實作 | 第 3 順位還原 `AdventurerInstance[]`（含 FT-03 idle 時間戳；**v1.1（2026-05-02 D-01 patch）** 含 `gender:int` / `bio:string` 欄位） |
| C-06 World Danger | `ISaveable` 實作 | 第 2 順位還原 `currentDangerLevel` / `gameStartTimestamp` |
| FT-01 Recruitment | `ISaveable` 實作 | 第 9 順位還原候選池與刷新狀態 |
| FT-02 Mission Dispatch | `ISaveable` 實作 | 第 6 順位還原 `activeMissions[]` / `_nextActiveMissionID`；FT-02 自行重新訂閱 F-02 `OnSecondTick` |
| FT-03 NPC Decision | `ISaveable` 實作（薄層） | 第 11 順位；無實質欄位（idle 時間戳由 C-02 一併還原），僅做訂閱重建 |
| FT-04 Outcome Resolution | `OnMissionResolved` 事件 | 訂閱以標記 `_isDirty`（FT-04 無持久狀態，不參與序列化） |
| FT-05 Guild Gold Flow | `OnCommissionSettled` 事件 | 同上；Jam 版不持久化結算歷史 |
| FT-06 Guild Core | `ISaveable` 實作 + `OnGuildInitialized` / `OnGuildLevelChanged` / `OnGameOver` 事件 | 第 4 順位還原 `GuildState`；訂閱三事件分別觸發 dirty 標記與封存流程 |
| FT-07 Guild Building | `ISaveable` 實作 + `OnBuildingUpgraded` 事件 | 第 5 順位還原 `BuildingState[]`；訂閱事件標記 dirty |
| FT-08 Gacha System | `ISaveable` 實作 + `CandidateCardValidationException` | 第 7 順位還原 `StaffPlayerState` + `CandidateCard[]`；critical 路徑捕獲 fail-fast 例外 |
| FT-09 Faction Story | `ISaveable` 實作 + `OnFactionStoryStageUnlocked` 事件 | 第 10 順位（degradable）還原 `FactionStorySaveData` |
| FT-12 Staff System | `ISaveable` 實作 + `OnStaffHired` / `OnStaffSalaryDue` 事件 | 第 8 順位還原 `StaffInstance[]`；critical 路徑驗證 staffID |

> **關鍵時序**：FT-10 在 Awake（Execution Order = -100）內讀檔；Start（同一 frame 後）呼叫 owner `RestoreFromSave`；其他 owner Awake 在 FT-10 之後執行，因此 owner Awake 內可預設初始化但**不應發布事件**（避免 FT-10 訂閱前漏接）。owner 端應於 `RestoreFromSave` 完成後或 Start 內首次發布事件。

### 2.4 下游被依賴系統

| 下游系統 | 消費介面 | 用途 |
|---|---|---|
| P-02 Main UI Framework（待設計） | `HasSaveFile()` / `IsGameOver()` / `GetLoadedFromBackupIndex()` 查詢 | 啟動畫面分支判斷（首次遊玩 / 繼續遊戲 / Game Over 雙選項） |
| P-02 Main UI Framework | `ResetToNewGame()` 呼叫 + `OnGameReset` 訂閱 | 玩家點「開新公會」執行 + scene reload |
| P-02 Main UI Framework | `OnGameSealed` / `OnLoadCompleted` 訂閱 | Game Over 封存後 UI / Bootstrap 完成後進主畫面 |
| P-02 Main UI Framework | `OnLoadFailed` 訂閱（可選） | 診斷用，不作流程驅動 |
| P-03 Notification System（待設計） | `OnSaveCompleted` / `OnGameSealed` 訂閱（可選） | 桌面通知（Jam 版可隱藏對齊「玩家不該意識到存檔」原則） |
| 各 owner 系統（10 個 ISaveable） | `MarkDirty()` 呼叫 | 特殊變動主動標記（一般情況走事件訂閱自動標記） |
| FT-04 Outcome Resolution | F-02 `OnOfflineResolved`（FT-10 不直接呼叫） | 離線到期任務結算；FT-10 僅交棒 lastActiveTimestamp |

### 2.5 跨系統事件契約

| 方向 | 事件名稱 | Payload | 發布時機 / 訂閱目的 |
|---|---|---|---|
| 入 | `OnGuildInitialized` | `GuildInitializedEventArgs` | FT-06 首次建立公會；FT-10 標記 dirty |
| 入 | `OnGuildLevelChanged` | `GuildLevelChangedEventArgs(int newLevel)` | FT-06 公會升級；FT-10 標記 dirty |
| 入 | `OnMissionResolved` | `MissionResolvedEventArgs` | FT-04 任務結算；FT-10 標記 dirty |
| 入 | `OnCommissionSettled` | `CommissionSettledEventArgs` | FT-05 委託結算；FT-10 標記 dirty |
| 入 | `OnRecruitSuccess` | `RecruitSuccessEventArgs` | FT-01 招募成功；FT-10 標記 dirty |
| 入 | `OnBuildingUpgraded` | `BuildingUpgradedEventArgs` | FT-07 建築升級；FT-10 標記 dirty |
| 入 | `OnStaffHired` / `OnStaffSalaryDue` | FT-12 events | FT-12 雇用 / 薪水扣款；FT-10 標記 dirty |
| 入 | `OnFactionStoryStageUnlocked` | `FactionStoryStageUnlockedEventArgs` | FT-09 階段解鎖；FT-10 標記 dirty |
| 入 | `OnDangerLevelChanged` | `DangerLevelChangedEventArgs(int newLevel)` | C-06 危險度升階；FT-10 標記 dirty |
| 入 | `OnGoldChanged` / `OnReputationChanged` | F-03 events | F-03 金幣 / 聲望變動；FT-10 標記 dirty |
| 入 | `OnGameOver` | `GameOverEventArgs` | FT-06 公會破產終局；FT-10 觸發封存流程 §5.4 |
| 出 | `OnSaveCompleted` | `SaveCompletedEventArgs`（空 payload） | 每次 `ExecuteSave` 成功完成；P-03 / Debug 訂閱（可選） |
| 出 | `OnSaveFailed` | `SaveFailedEventArgs(Exception ex)` | `ExecuteSave` 拋例外；Debug；正式 UI 不顯示（§3.4.3） |
| 出 | `OnLoadCompleted` | `LoadCompletedEventArgs(int loadedFromBackupIndex)` | Bootstrap Phase E 收尾單一信號；payload `-1` = 首次遊玩 / 全 backup 失敗；P-02 主流程信號 |
| 出 | `OnLoadFailed` | `LoadFailedEventArgs(Exception ex)` | 全部 backup 失敗時，先發布此事件後再發 `OnLoadCompleted(-1)`；P-02 / 診斷工具可選訂閱（不作流程驅動） |
| 出 | `OnGameSealed` | `GameSealedEventArgs(string gameoverFilePath, long sealedTimestamp)` | §5.4 OnGameOver 流程 Step 3；P-02 / P-03 訂閱 |
| 出 | `OnGameReset` | `GameResetEventArgs`（空 payload） | `ResetToNewGame` Step 4；P-02 訂閱執行 scene reload |

**事件規範**：

- 入向 11 類事件**僅標記 `_isDirty = true`**，不立即寫入；payload 內容不解析（FT-10 純編解碼，不檢視 owner 業務語意）
- 出向 6 個事件透過 `EventBus.Publish<T>` 發布；`OnLoadFailed` 必先於 `OnLoadCompleted` 發布以保證診斷工具收到 ex 細節後才走 Phase E 收尾
- 訂閱機制：FT-10 在 Awake 內統一 `EventBus.Subscribe<T>`，於 `OnDestroy` 對稱解除，避免 memory leak

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

> 玩家不該意識到「存檔」這件事——下班前看一眼委託板，派出三隊冒險者就直接關筆電；隔天開機，遊戲視窗自動跳出來：「你離開了 14 小時 32 分鐘，有 2 個任務已完成」。閃退、停電、家貓踩到電源鍵，最多丟掉一分鐘的進度。Game Over 後的公會被永久封存進公會編年史，過往的故事不會被「開新公會」覆蓋——FT-10 是這種無感安全感與敘事承諾的隱形載體。

### 3.2 系統目的還原

> 為公會所有持久化資料提供統一存取層：以 `Application.persistentDataPath` 為儲存後端、JSON 主存檔 + N 份 backup rotation 為佈局，三軌觸發策略（事件 / 節流 / 強制）平衡即時性與磁碟負擔；Bootstrap 五階段協調各 owner 還原並交棒 F-02 觸發離線計算；Critical / Degradable 失敗分流保證核心系統損壞時整檔回退、可降級系統損壞時不阻擋核心循環；Game Over 終末檔封存實現「公會編年史」設計承諾。FT-10 不擁有業務狀態，僅作 read / write 編解碼與 I/O。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
|---|---|---|
| 「不需要找存檔點」（無感安全感） | 沒有手動存檔按鈕；任何時機關閉視窗皆無進度損失警告 | 事件標記 dirty + 節流自動寫入（`SAVE_AUTO_INTERVAL_SEC = 60`）+ `OnApplicationQuit` 強制寫入三軌策略（§5.4 三軌觸發流程） |
| 「最多丟掉一分鐘」（閃退韌性） | 閃退後重啟僅丟最近 60 秒進度 | `Time.unscaledTime` 節流公式 + `_isDirty` 旗標捕捉所有 owner 變動快照（§4.1 公式） |
| 「我之前的進度好像不見了，但沒看到錯誤訊息」（隱性 fail-safe） | 全部 backup 損毀時直接走首次遊玩，無對話框 | 損毀回退鏈 `save.json → bak1 → ⋯ → bak{N}` + 全失敗發布 `OnLoadFailed → OnLoadCompleted(-1)` 但正式 UI 不顯示（§5.4 Bootstrap） |
| 「離線回應的揭曉」（隔夜開機看到摘要） | 啟動畫面顯示「你離開了 X 小時 Y 分鐘，有 N 個任務已完成」 | Phase D 呼叫 `F-02.Initialize(lastActiveTimestamp)`；F-02 內部 clamp `OFFLINE_MAX_SECONDS = 604800` 後發布 `OnOfflineResolved`；FT-04 / P-02 接手 |
| 「公會編年史」（破產的公會被永久封存） | Game Over 後啟動畫面有「回顧結算」按鈕；新公會不覆蓋舊終末檔 | `OnGameOver` 觸發 `File.Copy` 終末檔（檔名 `save_gameover_<unix>.json`）；`ResetToNewGame` 刪主存檔 + 全 backup **但不刪終末檔**（§5.4 終末封存與 Reset） |
| 「不可讀檔復活」（破產為終局） | Game Over 後 IsGameOver=true；F-02 不再發 `OnMissionCompleted` | 主存檔保留 `gameOverState = "Over"` 旗標；下次啟動 FT-06 還原 Over 態；終末檔不參與 backup 回退鏈（§7 EC-7） |
| 「Backup 是隱性 fail-safe」 | 玩家不會看到「載入備份？」對話框；最多回退至 bak3（最多丟 180 秒） | `loadedFromBackupIndex` payload 僅供 debug overlay；正式 UI 不呈現索引；§4.5 損失估算公式（`loadedIdx × SAVE_AUTO_INTERVAL_SEC`） |
| 「不擁有業務狀態」（解耦邊界） | owner 變更內部欄位無需修改 FT-10 | `ISaveable` 介面（`OwnerKey` / `IsCritical` / `Serialize` / `RestoreFromSave` / `InitializeAsNewGame`）；FT-10 純 string 嵌套組裝 root JSON |

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（FSD 不拆分）；單份 FSD 對應 6 個 Script。

### 4.2 拆分理由

不拆分 FSD 但於 Script 層內部分區（§4.4）。理由：

- FT-10 為**單一持久化系統**，三大職責（Persistence Pipeline / Bootstrap Orchestration / Game Over Sealing）皆圍繞同一份 `save.json` 展開，操作邊界共享 `_isDirty` / `_isSaving` / `_schemaMeta` 等核心狀態，業務語意上不可拆解
- 預估總行數 ~900~1180 行（明顯超過 500 行單檔上限），必須於 Script 層拆分；但 FSD 層拆分反而增加交叉引用負擔
- Script 層分區（Types / Interface / IO / Bootstrap / Service / ISaveable）遵循 GDD §3 已存在的職責分區（§3.1 schema / §3.2 trigger / §3.3 bootstrap / §3.4 IO / §3.5 game over / §3.6 ISaveable / §3.7 API），對映清晰
- 對齊 FSD-index §2.4 經驗值「單一系統 FSD 對應 3~8 個 Script」，6 為合理區間

### 4.3 拆分結果

不適用（FSD 未拆分）；§4.4 直接列 Script 清單。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
|---|---|---|---|---|
| `SaveLoadTypes.cs` | `Assets/Scripts/Gameplay/Save/SaveLoadTypes.cs` | 定義 `SchemaMeta` / `SaveDataRoot` 序列化包裝、6 個對外事件 struct payload、`CriticalRestoreFailedException`（純資料，零行為） | UnityEngine `Serializable` 屬性 | 150~200 行 |
| `ISaveable.cs` | `Assets/Scripts/Gameplay/Save/ISaveable.cs` | 定義 owner 系統必須實作的契約介面（`OwnerKey` / `IsCritical` / `Serialize` / `RestoreFromSave` / `InitializeAsNewGame`） | 無 | 30~50 行 |
| `ISaveLoadService.cs` | `Assets/Scripts/Gameplay/Save/ISaveLoadService.cs` | 定義 FT-10 對外公開 API 介面（`MarkDirty` / `ForceSave` / `HasSaveFile` / `IsGameOver` / `GetLoadedFromBackupIndex` / `ResetToNewGame`） | 無 | 50~70 行 |
| `SaveFileIO.cs` | `Assets/Scripts/Gameplay/Save/SaveFileIO.cs` | 純 I/O 操作層：原子寫入（`save.tmp` → `File.Replace`）、backup rotation（先 rotate 後 Replace）、損毀回退讀檔、終末檔複製、刪檔 | `System.IO`（`File` / `Path`） | 220~290 行 |
| `SaveLoadBootstrap.cs` | `Assets/Scripts/Gameplay/Save/SaveLoadBootstrap.cs` | Bootstrap Phase A~E 五階段流程、owner 還原拓撲執行、Critical/Degradable 失敗分流、F-02 離線計算交棒 | `ISaveable` / `ITimeSystem` / `IEventBus` / `SaveFileIO` | 200~270 行 |
| `SaveLoadService.cs` | `Assets/Scripts/Gameplay/Save/SaveLoadService.cs` | MonoBehaviour 主體 + Singleton：實作 `ISaveLoadService`；三軌觸發策略 / 事件訂閱與解除 / owner 註冊（`FindObjectsByType<MonoBehaviour>().OfType<ISaveable>()`）/ 節流 Update / `OnApplicationQuit` / `OnGameOver` 處理 / `ResetToNewGame` 流程 / `InitTuning` | `IDataManager` / `ITimeSystem` / `IEventBus` / `SaveFileIO` / `SaveLoadBootstrap` / `ISaveable` | 350~430 行 |

**Script 路徑慣例**：對齊 FSD-index §5.4，全部以 Unity Asset Database 為準從 `Assets/...` 起算；FT-0x 前綴對映 `Gameplay/` 子目錄，新建 `Gameplay/Save/` 為本系統專屬目錄。

**模組化邊界**：

- `SaveLoadTypes` 為純資料 + 例外，無依賴他模組；可被 owner 系統直接 `using` 引用 payload 型別
- `ISaveable` 與 `ISaveLoadService` 為純介面，零實作；分離至獨立檔案以支援未來 mock / test
- `SaveFileIO` 為 stateless 純函式靜態類；不持有狀態、不發布事件；可獨立 EditMode 測試（傳入 fake path）
- `SaveLoadBootstrap` 為 stateless helper（傳入 saveables list 與 IO 結果）；協調 5 階段流程；發布 `OnLoadCompleted` / `OnLoadFailed` 由 `SaveLoadService` 執行（Bootstrap 僅回傳 outcome）
- `SaveLoadService` 為 MonoBehaviour 入口；持有所有 runtime 狀態（`_isDirty` / `_isSaving` / `_lastSaveTime` / `_schemaMeta` / `_saveables` / `_loadedFromBackupIndex`）；其餘模組無 runtime 狀態

### 4.5 類別關係（可選）

```
[MonoBehaviour] SaveLoadService : ISaveLoadService
        │  (Singleton + DontDestroyOnLoad)
        │
        ├─ uses → SaveFileIO (static)         // 原子寫入 + 回退讀檔 + 終末檔複製
        ├─ uses → SaveLoadBootstrap (static)   // Phase A~E 流程協調
        ├─ holds → List<ISaveable>             // owner 註冊表（拓撲排序後）
        ├─ holds → SchemaMeta                  // root meta 欄位（FT-10 自有）
        ├─ subscribes → 11 categories of EventBus events  // §3.2.1
        └─ publishes → OnSaveCompleted / OnSaveFailed / OnLoadCompleted / OnLoadFailed / OnGameSealed / OnGameReset

ISaveable (各 owner 實作):
        ├─ F-03 ResourceManagement       (IsCritical=true)
        ├─ C-02 AdventurerRoster         (IsCritical=true)
        ├─ FT-06 GuildCoreService        (IsCritical=true)
        ├─ FT-08 GachaService            (IsCritical=true)
        ├─ FT-12 StaffService            (IsCritical=true)
        ├─ C-06 WorldDangerService       (IsCritical=false)
        ├─ FT-01 RecruitmentService      (IsCritical=false)
        ├─ FT-02 MissionDispatchService  (IsCritical=false)
        ├─ FT-03 NpcDecisionService      (IsCritical=false, 薄層)
        ├─ FT-07 BuildingService         (IsCritical=false)
        └─ FT-09 FactionStoryService     (IsCritical=false)
```

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// SaveLoadService.Instance 提供
public interface ISaveLoadService {
    // 三軌觸發控制
    void MarkDirty();                        // §3.7.1：標記 _isDirty = true，不立即寫入
    void ForceSave();                        // §3.7.1：立即執行 ExecuteSave 忽略 _isDirty

    // 啟動畫面分支查詢
    bool HasSaveFile();                      // §3.7.1：save.json 或任一 backup 是否存在
    bool IsGameOver();                       // §3.7.1：_schemaMeta.gameOverState == "Over"
    int  GetLoadedFromBackupIndex();         // §3.7.1：0 = 主存檔；1~N = bak1~bak{N}；-1 = 首次遊玩

    // Game Over 後玩家點「開新公會」
    void ResetToNewGame();                   // §3.5.2：刪主存檔 + 全 backup；發布 OnGameReset

    // 事件（透過 EventBus 發布；提供 IDelegate 形式僅作 IDE 提示）
    // OnSaveCompleted / OnSaveFailed / OnLoadCompleted / OnLoadFailed / OnGameSealed / OnGameReset
}

// 各 owner 實作
public interface ISaveable {
    string OwnerKey { get; }                 // root JSON 子區塊 key（例："f03Resources" / "factionStorySaveData"）
    bool   IsCritical { get; }               // §3.3.4：true = 還原失敗觸發整檔回退；false = log + InitializeAsNewGame()
    string Serialize();                      // 回傳該 owner 的 JSON 字串（Dict/Queue/HashSet 須轉 List 中介）
    void   RestoreFromSave(string ownerJson); // 從 JSON 字串還原；ownerJson 可能為 null（首次遊玩 / 缺欄位 EC-10）
                                             // 契約：若 ownerJson == null，必須立即呼叫 InitializeAsNewGame() 並 return
    void   InitializeAsNewGame();            // §3.3.4：degradable 還原失敗時呼叫；首次遊玩亦走此路徑
}
```

**API 行為摘要**：

- `MarkDirty` / `ForceSave`：呼叫前 `SaveLoadService.Instance != null` 由呼叫方保證；空 reference 為 owner 端時序錯誤（不在 FT-10 範疇內 catch）
- `HasSaveFile`：實作以 `File.Exists(savePath) || bakPaths.Any(File.Exists)`；不嘗試解析內容（純存在性判斷，O(N+1)）
- `IsGameOver`：Bootstrap 完成前回傳 false（`_schemaMeta` 預設 `gameOverState = "Active"`）；Bootstrap 完成後反映 schemaMeta 真實值
- `GetLoadedFromBackupIndex`：Bootstrap 完成前回傳 0（預設）；完成後反映實際載入來源
- `ResetToNewGame`：完整流程詳見 §5.4 ResetToNewGame 資料流；P-02 為唯一呼叫者（Jam 範疇）

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
|---|---|---|---|
| `OnGuildInitialized` | 入 | `GuildInitializedEventArgs` | FT-06 首次建立公會；FT-10 標記 dirty |
| `OnGuildLevelChanged` | 入 | `GuildLevelChangedEventArgs` | FT-06 公會升級；標記 dirty |
| `OnMissionResolved` | 入 | `MissionResolvedEventArgs` | FT-04 任務結算；標記 dirty |
| `OnCommissionSettled` | 入 | `CommissionSettledEventArgs` | FT-05 委託結算；標記 dirty |
| `OnRecruitSuccess` | 入 | `RecruitSuccessEventArgs` | FT-01 招募成功；標記 dirty |
| `OnBuildingUpgraded` | 入 | `BuildingUpgradedEventArgs` | FT-07 建築升級；標記 dirty |
| `OnStaffHired` | 入 | `StaffHiredEventArgs` | FT-12 雇用；標記 dirty |
| `OnStaffSalaryDue` | 入 | `StaffSalaryDueEventArgs` | FT-12 薪水扣款；標記 dirty |
| `OnFactionStoryStageUnlocked` | 入 | `FactionStoryStageUnlockedEventArgs` | FT-09 階段解鎖；標記 dirty |
| `OnDangerLevelChanged` | 入 | `DangerLevelChangedEventArgs` | C-06 危險度升階；標記 dirty |
| `OnGoldChanged` | 入 | F-03 event | F-03 金幣變動；標記 dirty |
| `OnReputationChanged` | 入 | F-03 event | F-03 聲望變動；標記 dirty |
| `OnGameOver` | 入 | `GameOverEventArgs` | FT-06 公會破產終局；觸發 §5.4 封存流程 |
| `OnSaveCompleted` | 出 | 空（`SaveCompletedEventArgs`） | 每次 `ExecuteSave` 成功完成；P-03 / Debug（可選） |
| `OnSaveFailed` | 出 | `SaveFailedEventArgs(Exception ex)` | `ExecuteSave` 拋例外；正式 UI 不顯示 |
| `OnLoadCompleted` | 出 | `LoadCompletedEventArgs(int loadedFromBackupIndex)` | Phase E 統一收尾；P-02 主流程信號（-1 = 首次 / 全失敗） |
| `OnLoadFailed` | 出 | `LoadFailedEventArgs(Exception ex)` | 全 backup 失敗時於 `OnLoadCompleted(-1)` 之前發布；P-02 / 診斷可選訂閱 |
| `OnGameSealed` | 出 | `GameSealedEventArgs(string filePath, long timestamp)` | §5.4 OnGameOver 流程 Step 3 |
| `OnGameReset` | 出 | 空（`GameResetEventArgs`） | `ResetToNewGame` Step 4；P-02 訂閱執行 reload |

**事件規範補充**：

- 入向事件採 `EventBus.Subscribe<T>(handler)` 訂閱；每個 handler 內**只執行 `_isDirty = true`**，不解析 payload（FT-10 純編解碼設計）
- 出向事件採 `EventBus.Publish<T>(args)` 發布；payload struct 標 `[Serializable]` 但不參與存檔序列化（runtime-only 通知）
- `OnDestroy` 統一解除全部訂閱（透過 `_eventUnsubscribers: List<Action>` 收集 unsubscribe 委派並一次性執行）

### 5.3 資料結構

```csharp
// === SchemaMeta（FT-10 擁有的 root meta） ===
[Serializable]
public class SchemaMeta {
    public long   lastActiveTimestamp;       // UTC Unix 秒；F-02 離線計算輸入
    public string gameOverState;             // "Active" / "Over"
    public int    loadedFromBackupIndex;     // 0 = 主存檔；1~N = bak{N}；-1 = 首次遊玩 / 全失敗
}

// === SaveDataRoot（JsonUtility wrapper；JsonUtility 不支援 Dictionary，
//     故以具名欄位 + 各 owner 子 JSON 字串嵌套）===
[Serializable]
public class SaveDataRoot {
    public SchemaMeta schemaMeta;            // FT-10 自有
    public string f03Resources;              // owner JSON 字串（owner 自行 JsonUtility.ToJson）
    public string c02Adventurers;
    public string c06WorldDanger;
    public string ft01Recruitment;
    public string ft02Dispatch;
    public string ft03Decision;              // 薄層；可為 "{}"
    public string ft06Guild;
    public string ft07Buildings;
    public string ft08Staff;
    public string ft12Staff;
    public string factionStorySaveData;
}

// === ISaveable（§5.1 已列）===
// === ISaveLoadService（§5.1 已列）===

// === 事件 Payload（runtime-only） ===
[Serializable] public struct SaveCompletedEventArgs { }
[Serializable] public struct SaveFailedEventArgs    { public Exception exception; }
[Serializable] public struct LoadCompletedEventArgs { public int loadedFromBackupIndex; }
[Serializable] public struct LoadFailedEventArgs    { public Exception exception; }
[Serializable] public struct GameSealedEventArgs    { public string gameoverFilePath; public long sealedTimestamp; }
[Serializable] public struct GameResetEventArgs     { }

// === Critical 還原失敗例外（§3.3.4） ===
public class CriticalRestoreFailedException : Exception {
    public string OwnerName { get; }
    public CriticalRestoreFailedException(string ownerName, Exception inner)
        : base($"Critical owner {ownerName} restore failed", inner)
    {
        OwnerName = ownerName;
    }
}
```

**設計理由**：

- `SaveDataRoot` 採具名欄位 + 嵌套 JSON 字串：`JsonUtility` 不支援 `Dictionary<string, object>`，故根層必須以 `[Serializable] class` 列出全部 owner key；owner 端各自 `Serialize()` 回傳 JSON 字串嵌入
- 字串嵌套導致 root JSON 內含跳脫字元（`\"`）但對 `JsonUtility.FromJson` 無解析問題；owner 解碼時取出字串再 `FromJson<T>` 一次
- 事件 struct 採 `[Serializable]` 純為對齊 EventBus 慣例；不參與存檔
- `SchemaMeta.gameOverState` 採 `string` 而非 `enum`：對齊 GDD §3.1.2 既定 schema、避免 enum 序列化版本相容問題（Post-Jam schema 演化容易）

### 5.4 內部資料流

#### 5.4.1 Awake / Start：Bootstrap 五階段

```
Unity lifecycle.Awake
  → SaveLoadService.Awake()
      ├─ 步驟 1：DontDestroyOnLoad + Singleton 設定
      ├─ 步驟 2：InitTuning() — 從 IDataManager 載入 5 個 SAVE_* 常數並 clamp
      ├─ 步驟 3：InitPaths() — 組合 savePath / bakPaths[N] / gameoverPrefix
      ├─ 步驟 4：CollectSaveables() — FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<ISaveable>()
      │                              → SortByRestoreOrder（OwnerKey-indexed dictionary，§5.4.1.A）
      │                              → _saveables / _criticalSaveables 填入
      └─ 步驟 5：SubscribeAllEvents() — 訂閱 13 類事件（11 dirty 標記 + OnGameOver + Unity Quit hook）
                                       並 push 對應 unsubscribe Action 至 _eventUnsubscribers（§5.4.1.B）

Unity lifecycle.Start（同 frame 後）
  → SaveLoadService.Start()
      → SaveLoadBootstrap.Execute(_saveables, savePath, bakPaths)
          // === 外層 retry loop：candidate idx 0 = save.json；1~N = bak1~bak{N}；N+1 = 全失敗哨兵 ===
          for (int idx = 0; idx <= bakPaths.Length; idx++):
              if (idx == bakPaths.Length + 1):  // 不會進來，僅作哨兵；下行 break-out 會先觸發
                  break

              ├─ Phase A：讀檔
              │   if (idx > bakPaths.Length):
              │       → 全失敗哨兵：發 OnLoadFailed(lastEx) → 跳 Phase E(loadedFromIndex = -1) → return
              │   if (!File.Exists(candidates[idx])):
              │       continue                                  // 檔案不存在，下一份
              │   try:
              │       rootJson = SaveFileIO.ReadCandidate(candidates[idx])
              │   catch (IOException / UnauthorizedAccessException ex):
              │       LogWarning + lastEx = ex; continue        // 讀檔失敗，下一份
              │
              ├─ Phase B：解析 SaveDataRoot
              │   try:
              │       root = JsonUtility.FromJson<SaveDataRoot>(rootJson)
              │       if (root == null || root.schemaMeta == null):
              │           throw new InvalidDataException("schemaMeta null")
              │   catch (Exception ex):
              │       LogWarning + lastEx = ex; continue        // 解析失敗，下一份
              │   _schemaMeta = root.schemaMeta                  // 暫存於 service runtime（每 retry 覆寫）
              │
              ├─ Phase C：per-owner 還原（§3.3.3 拓撲順序）
              │   try:
              │       foreach owner in _saveables:
              │           ownerJson = ExtractOwnerJson(root, owner.OwnerKey)  // §5.4.1.C；缺欄位回 null
              │           try:
              │               owner.RestoreFromSave(ownerJson)   // 契約：null 必走 InitializeAsNewGame
              │           catch (Exception innerEx):
              │               if (owner.IsCritical):
              │                   LogError → throw new CriticalRestoreFailedException(owner.OwnerKey, innerEx)
              │               else:
              │                   LogWarning → owner.InitializeAsNewGame()  // degradable 不傳遞例外
              │   catch (CriticalRestoreFailedException ex):
              │       lastEx = ex
              │       ResetAllSaveables()                        // §5.4.1.D：強制全部 owner 回到預設
              │       continue                                   // retry 下一份 backup
              │
              ├─ loadedFromIndex = idx → break out of retry loop
          end for
          // === retry loop 結束 ===

          ├─ Phase D：F-02 離線計算交棒
          │   timestamp = _schemaMeta.lastActiveTimestamp  // 來自 Phase B 最後成功解析的 SchemaMeta；
          │                                                  // 若從 bak{k} 載入，則為 bak{k} 寫入時的時間戳
          │                                                  // F-02 內部 clamp(now - timestamp, 0, OFFLINE_MAX_SECONDS)
          │   → ITimeSystem.Initialize(timestamp)
          │       F-02 發 OnOfflineResolved → FT-04 接手結算（FT-10 不參與）
          │
          └─ Phase E：發布 OnLoadCompleted（單一收尾信號）
              // 全失敗路徑：retry loop 哨兵已先發 OnLoadFailed
              EventBus.Publish(new LoadCompletedEventArgs(loadedFromIndex))

  // 注意：OnGameReset 事件僅由 ResetToNewGame 路徑發布，不在 Bootstrap Phase E 發布
```

**§5.4.1.A — `SortByRestoreOrder` 實作機制**：

採 **OwnerKey-indexed dictionary**（不依賴 owner concrete type，符合 FT-10 解耦邊界）：

```csharp
private static readonly Dictionary<string, int> OWNER_RESTORE_ORDER = new() {
    { "f03Resources",         1 },   // F-03 Resource Mgmt
    { "c06WorldDanger",       2 },   // C-06 World Danger
    { "c02Adventurers",       3 },   // C-02 Adventurer Mgmt（含 FT-03 idle 時間戳；v1.1 含 gender/bio）
    { "ft06Guild",            4 },   // FT-06 Guild Core
    { "ft07Buildings",        5 },   // FT-07 Guild Building
    { "ft02Dispatch",         6 },   // FT-02 Mission Dispatch
    { "ft08Staff",            7 },   // FT-08 Gacha
    { "ft12Staff",            8 },   // FT-12 Staff System
    { "ft01Recruitment",      9 },   // FT-01 Recruitment
    { "factionStorySaveData", 10 },  // FT-09 Faction Story
    { "ft03Decision",         11 },  // FT-03 NPC Decision（薄層）
};

List<ISaveable> SortByRestoreOrder(IEnumerable<ISaveable> all) {
    var sorted = new List<ISaveable>();
    foreach (var s in all) {
        if (!OWNER_RESTORE_ORDER.ContainsKey(s.OwnerKey)) {
            Debug.LogWarning($"Unknown ISaveable OwnerKey: {s.OwnerKey} — appended at end of restore order");
        }
        sorted.Add(s);
    }
    return sorted.OrderBy(s => OWNER_RESTORE_ORDER.TryGetValue(s.OwnerKey, out var n) ? n : int.MaxValue).ToList();
}
```

新增 owner 時必須同步更新 `OWNER_RESTORE_ORDER`（compile-time 安全：未在 dict 內的 ISaveable 觸發 LogWarning 並排序至最末）。

**§5.4.1.B — `SubscribeAllEvents` 與 `_eventUnsubscribers` 機制**：

```csharp
private List<Action> _eventUnsubscribers = new();

void SubscribeAllEvents() {
    Subscribe<GuildInitializedEventArgs>(_ => _isDirty = true);
    Subscribe<GuildLevelChangedEventArgs>(_ => _isDirty = true);
    Subscribe<MissionResolvedEventArgs>(_ => _isDirty = true);
    Subscribe<CommissionSettledEventArgs>(_ => _isDirty = true);
    Subscribe<RecruitSuccessEventArgs>(_ => _isDirty = true);
    Subscribe<BuildingUpgradedEventArgs>(_ => _isDirty = true);
    Subscribe<StaffHiredEventArgs>(_ => _isDirty = true);
    Subscribe<StaffSalaryDueEventArgs>(_ => _isDirty = true);
    Subscribe<FactionStoryStageUnlockedEventArgs>(_ => _isDirty = true);
    Subscribe<DangerLevelChangedEventArgs>(_ => _isDirty = true);
    Subscribe<GoldChangedEventArgs>(_ => _isDirty = true);
    Subscribe<ReputationChangedEventArgs>(_ => _isDirty = true);
    Subscribe<GameOverEventArgs>(OnGameOverHandler);
    // OnApplicationQuit 走 Unity lifecycle 回呼，不透過 EventBus
}

void Subscribe<T>(Action<T> handler) where T : struct {
    EventBus.Subscribe<T>(handler);
    _eventUnsubscribers.Add(() => EventBus.Unsubscribe<T>(handler));
}

void OnDestroy() {
    foreach (var unsub in _eventUnsubscribers) unsub();
    _eventUnsubscribers.Clear();
}
```

**§5.4.1.C — `ExtractOwnerJson(root, ownerKey)`**：

由於 `SaveDataRoot` 為具名欄位 class，不用 reflection；以 switch / dictionary 對映：

```csharp
string ExtractOwnerJson(SaveDataRoot root, string ownerKey) {
    string raw = ownerKey switch {
        "f03Resources"         => root.f03Resources,
        "c02Adventurers"       => root.c02Adventurers,
        "c06WorldDanger"       => root.c06WorldDanger,
        "ft01Recruitment"      => root.ft01Recruitment,
        "ft02Dispatch"         => root.ft02Dispatch,
        "ft03Decision"         => root.ft03Decision,
        "ft06Guild"            => root.ft06Guild,
        "ft07Buildings"        => root.ft07Buildings,
        "ft08Staff"            => root.ft08Staff,
        "ft12Staff"            => root.ft12Staff,
        "factionStorySaveData" => root.factionStorySaveData,
        _                      => null,                         // 未知 ownerKey
    };
    return string.IsNullOrEmpty(raw) ? null : raw;             // 缺欄位 / 空字串視為 null（觸發 owner InitializeAsNewGame）
}
```

**§5.4.1.D — `ResetAllSaveables`（retry 前強制 owner 重置）**：

```csharp
void ResetAllSaveables() {
    // 對齊 GDD §3.3.4 + §5.4.1 retry loop：critical 失敗時，前面已部分還原的 owner 須回到預設
    // 避免下一輪 candidate 載入時殘留前一輪部分還原狀態
    foreach (var owner in _saveables) {
        try { owner.InitializeAsNewGame(); }
        catch (Exception ex) { Debug.LogError($"InitializeAsNewGame failed for {owner.OwnerKey}: {ex}"); }
    }
}
```

#### 5.4.2 三軌寫入策略

```
軌道一：事件標記
  EventBus.Publish(any of 11 dirty events)
    → SaveLoadService.OnDirtyEvent(args)
        └─ 步驟 1：_isDirty = true（不解析 payload）

軌道二：節流自動寫入
  Update() 每 frame
    → if (Time.unscaledTime - _lastSaveTime >= _saveAutoIntervalSec):
          if (_isDirty):
              ExecuteSave()        // 走原子寫入
              _isDirty = false
              _lastSaveTime = Time.unscaledTime
          else:
              _lastSaveTime = Time.unscaledTime  // 更新檢查時間避免每 frame 比對

軌道三：強制寫入
  Unity lifecycle.OnApplicationQuit
    → ExecuteSave()  // 忽略 _isDirty 旗標
  EventBus.Publish(OnGameOver)
    → SaveLoadService.OnGameOverHandler(args)
        ├─ 步驟 1：ExecuteSave()  // 強制寫入 — schemaMeta.gameOverState 已由 FT-06 更新為 "Over"
        ├─ 步驟 2：SaveFileIO.CopyToGameoverFile(savePath, gameoverPath)
        │           gameoverPath = $"{persistentDataPath}/{SAVE_GAMEOVER_PREFIX}{unixTimestamp}.json"
        │           try-catch IOException（同秒重觸發 / 磁碟空間不足）→ log warning 不重複封存
        └─ 步驟 3：EventBus.Publish(new GameSealedEventArgs(gameoverPath, unixTimestamp))
```

#### 5.4.3 ExecuteSave 原子寫入（§3.4.1）

```
SaveLoadService.ExecuteSave()
  → if (_isSaving) return    // 互斥保護（§3.2.4）
  → _isSaving = true
  → try:
       ├─ 步驟 1：_schemaMeta.lastActiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
       ├─ 步驟 2：rootJson = AssembleRootJson()
       │           ├─ root = new SaveDataRoot { schemaMeta = _schemaMeta }
       │           ├─ foreach owner in _saveables:
       │           │     SetField(root, owner.OwnerKey, owner.Serialize())
       │           └─ return JsonUtility.ToJson(root)
       │
       ├─ 步驟 3：SaveFileIO.WriteAtomic(savePath, bakPaths, rootJson)
       │           ├─ File.WriteAllText(savePath + ".tmp", rootJson, Encoding.UTF8)
       │           ├─ RotateOlderBackups(bakPaths)
       │           │     for i from N-1 downto 1:
       │           │         if File.Exists(bakPaths[i-1]):
       │           │             File.Move(bakPaths[i-1], bakPaths[i], overwrite=true)
       │           ├─ if File.Exists(savePath):
       │           │     File.Replace(savePath + ".tmp", savePath, bakPaths[0])
       │           └─ else:
       │                 File.Move(savePath + ".tmp", savePath)
       │
       ├─ 步驟 4：_isDirty = false
       └─ 步驟 5：EventBus.Publish(new SaveCompletedEventArgs())
    catch (Exception ex):
       ├─ Debug.LogError($"Save failed: {ex}")
       └─ EventBus.Publish(new SaveFailedEventArgs(ex))
    finally:
       └─ _isSaving = false
```

#### 5.4.4 ResetToNewGame（§3.5.2）

```
SaveLoadService.ResetToNewGame()
  ├─ 步驟 1：if File.Exists(savePath): File.Delete(savePath)
  ├─ 步驟 2：foreach bak in bakPaths: if File.Exists(bak): File.Delete(bak)
  │           // 終末檔 save_gameover_*.json 不動（公會編年史承諾）
  ├─ 步驟 3：_isDirty = false; _lastSaveTime = 0; _schemaMeta = new SchemaMeta { gameOverState = "Active" }
  └─ 步驟 4：EventBus.Publish(new GameResetEventArgs())
              // P-02 訂閱後執行 SceneManager.LoadScene(bootstrapScene)
              // FT-10 不主動 reload scene
```

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
|---|---|---|---|---|
| `SystemConstants` | `SAVE_AUTO_INTERVAL_SEC`（int 秒） | `【F-01-DS】system-constants.md`（消費端） | 三軌節流間隔；安全範圍 [10, 600]；預設 60 | `SaveLoadService.Awake.InitTuning()`（在 SubscribeAllEvents 之前） |
| `SystemConstants` | `SAVE_BACKUP_COUNT`（int 份數） | `【F-01-DS】system-constants.md`（消費端） | Backup rotation 代數；安全範圍 [1, 10]；預設 3 | 同上 |
| `SystemConstants` | `SAVE_FILE_NAME`（string） | `【F-01-DS】system-constants.md`（消費端） | 主存檔檔名；預設 `save.json`；不建議修改 | 同上 |
| `SystemConstants` | `SAVE_BAK_PREFIX`（string） | `【F-01-DS】system-constants.md`（消費端） | Backup 前綴；實際檔名 `{prefix}{i}` for i ∈ [1, N]；預設 `save.bak`；不建議修改 | 同上 |
| `SystemConstants` | `SAVE_GAMEOVER_PREFIX`（string） | `【F-01-DS】system-constants.md`（消費端） | 終末檔前綴；實際檔名 `{prefix}{unixTimestamp}.json`；預設 `save_gameover_`；不建議修改 | 同上 |

**載入規範**（GDD §7.5）：

```csharp
void InitTuning() {
    _saveAutoIntervalSec = DataManager.GetSystemConstant<int>("SAVE_AUTO_INTERVAL_SEC", 60);
    _saveBackupCount     = DataManager.GetSystemConstant<int>("SAVE_BACKUP_COUNT", 3);
    _saveFileName        = DataManager.GetSystemConstant<string>("SAVE_FILE_NAME", "save.json");
    _saveBakPrefix       = DataManager.GetSystemConstant<string>("SAVE_BAK_PREFIX", "save.bak");
    _saveGameoverPrefix  = DataManager.GetSystemConstant<string>("SAVE_GAMEOVER_PREFIX", "save_gameover_");

    // 安全範圍 clamp（GDD §7.5：FT-10 自身執行而非 F-01）
    _saveAutoIntervalSec = Mathf.Clamp(_saveAutoIntervalSec, 10, 600);
    _saveBackupCount     = Mathf.Clamp(_saveBackupCount, 1, 10);
}
```

### 6.2 引用的 ScriptableObject

無。FT-10 全部可調參數透過 `SystemConstants.csv` 提供。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
|---|---|---|
| `_saveAutoIntervalSec` | `SystemConstants.SAVE_AUTO_INTERVAL_SEC` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_saveBackupCount` | `SystemConstants.SAVE_BACKUP_COUNT` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_saveFileName` | `SystemConstants.SAVE_FILE_NAME` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_saveBakPrefix` | `SystemConstants.SAVE_BAK_PREFIX` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_saveGameoverPrefix` | `SystemConstants.SAVE_GAMEOVER_PREFIX` | 對應「四、程式實作原則」第 9 條：參數表格化 |

**結構性常數（明示不入 CSV，對齊 GDD §7.4）**：

| 項目 | 值 / 來源 | 理由 |
|---|---|---|
| Script Execution Order F-01 = -200 / FT-10 = -100 | Unity Project Settings | 配置型常數，runtime 不可變 |
| Owner 還原拓撲順序（§5.4.1 Phase C 列舉） | 硬編列表 in `SaveLoadService.SortByRestoreOrder` | 由依賴拓撲推導；新增 owner 須通過 review 後同步更新列表 |
| Critical / Degradable 分類 | `ISaveable.IsCritical` 由各 owner 自行宣告 | 由系統重要性語意決定；非運行時調整對象 |
| `File.Replace` / `File.Move` API | `System.IO` 平台原生 | Jam Windows-only（GDD §3.4.1） |
| `JsonUtility` 序列化器 | Unity 內建 | 零依賴選擇（GDD §1） |

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
|---|---|---|---|
| **EC-1**：寫入過程閃退 / 停電 | `ExecuteSave` 使用 `save.tmp` + `File.Replace`：未呼叫前 `save.json` 不變；中介檔殘留由下次 `File.WriteAllText` 直接覆寫，FT-10 不主動清理 | `SaveFileIO.WriteAtomic` | PlayMode：mock `File.Replace` 拋例外，斷言 `save.json` 內容仍為前一版本；磁碟檢視 `save.tmp` 殘留次次 ExecuteSave 後消失 |
| **EC-2**：全部 backup 損毀 | `ReadSaveFileWithFallback` 候選全失敗回傳 null；`SaveLoadBootstrap.Execute` 偵測 null → **先**發 `OnLoadFailed(ex)`，各 owner `InitializeAsNewGame()`，**再**發 `OnLoadCompleted(-1)`；正式 UI 不彈對話框（`loadedFromBackupIndex` 僅供 debug overlay） | `SaveFileIO.ReadWithFallback` / `SaveLoadBootstrap` / `SaveLoadService` | EditMode：以 fake path 寫入 4 份壞檔，斷言 `OnLoadFailed → OnLoadCompleted(-1)` 順序 + 各 owner `InitializeAsNewGame` 被呼叫 |
| **EC-3**：Critical owner 還原失敗 | Phase C try-catch 捕獲 `IsCritical = true` 例外 → 拋 `CriticalRestoreFailedException(ownerName, ex)` → 外層 Phase A 重進回退鏈下一份 backup（`loadedFromIndex++`）；全部 backup 都失敗時退化為 EC-2 | `SaveLoadBootstrap.Execute` | EditMode：F-03 `RestoreFromSave` 故意拋例外，bak1 完整；斷言 `loadedFromBackupIndex == 1` |
| **EC-4**：Degradable owner 還原失敗 | Phase C try-catch 捕獲 `IsCritical = false` 例外 → `Debug.LogWarning` + `owner.InitializeAsNewGame()` → 不傳遞例外，繼續下一個 owner；對齊 FT-09 EC-10 設計動機 | `SaveLoadBootstrap.Execute` | EditMode：FT-09 `RestoreFromSave` 故意拋例外；斷言 console 有 warning + FT-09 `_factionScores` 為空 + `loadedFromBackupIndex == 0` |
| **EC-5**：`persistentDataPath` 無寫入權限 | `File.WriteAllText` / `File.Replace` 拋 `UnauthorizedAccessException` / `IOException` → `ExecuteSave` catch + `EventBus.Publish(SaveFailedEventArgs(ex))`；不阻擋核心循環，下一節流週期仍嘗試（log spam 在所難免）；不嘗試替代路徑 | `SaveLoadService.ExecuteSave` / `SaveFileIO.WriteAtomic` | EditMode：mock `IFileSystem` 拋 `UnauthorizedAccessException`；斷言 `OnSaveFailed(ex)` 發布且遊戲繼續 Update |
| **EC-6**：終末檔複製 IOException | `SaveFileIO.CopyToGameoverFile` 內 `File.Copy(overwrite: false)` 拋 `IOException`（同秒重觸發 / 磁碟空間不足）→ catch + log warning；不重複封存、不阻擋 Step 3（仍發 `OnGameSealed`，filePath 為當前嘗試路徑）；視 `OnGameOver` 為 idempotent | `SaveFileIO.CopyToGameoverFile` / `SaveLoadService.OnGameOverHandler` | EditMode：先 `File.Create(gameoverPath)` 預佔；觸發 `OnGameOver`；斷言 console warning + `OnGameSealed` 仍發布 |
| **EC-7**：Game Over 後再次啟動 | Bootstrap 走標準 Phase A→E；FT-06 `RestoreFromSave` 還原 `gameOverState = "Over"` → `IsGameOver()` 回 true；F-02 內部對齊 §3.6 既定行為 — Over 態下不發 `OnMissionCompleted`、FT-04 不被觸發；P-02 啟動畫面分支呈現「回顧結算 / 開新公會」 | `SaveLoadService` / `SaveLoadBootstrap` | PlayMode：先觸發 `OnGameOver` → 重啟 → 斷言 `IsGameOver() == true` 且觀察無結算事件 |
| **EC-8**：`OnApplicationQuit` 與節流軌道同 frame | `_isSaving: bool` 互斥保護；先到者進入 `ExecuteSave()` 設旗、後到者第一行 `if (_isSaving) return` 直接返回；不重試（玩家進度已由先到者寫入） | `SaveLoadService.ExecuteSave` | EditMode：模擬 ExecuteSave 進行中第二次呼叫；斷言後到者立即 return + 磁碟內容完整 |
| **EC-9**：系統時鐘倒退 / 跳躍 | FT-10 不檢測；`ExecuteSave` 純取 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` 寫入；clamp 與容錯責任歸 F-02（`offlineSeconds = clamp(now - lastActiveTimestamp, 0, OFFLINE_MAX_SECONDS)`） | `SaveLoadService.ExecuteSave` | EditMode：mock `DateTimeOffset.UtcNow` 倒退；斷言 FT-10 寫入新值不檢查、F-02 計算 `offlineSeconds == 0` |
| **EC-10**：Owner 子區塊欄位缺失（schema 演化） | (a) Owner 區塊整個缺失 → `ExtractOwnerJson` 返回 null → owner 收到 null 後**契約規定**呼叫 `InitializeAsNewGame()` 並 return；(b) 區塊存在但內部欄位部分缺失 → owner 自行容忍（FT-10 不檢視）；(c) 區塊 JSON 無法解析 → owner 拋例外 → 進 EC-3 / EC-4 分流；不引入 `version: int` 欄位 | `SaveLoadBootstrap.ExtractOwnerJson` / 各 owner `RestoreFromSave` | EditMode：構造 root JSON 缺 `factionStorySaveData` 欄位；斷言 FT-09 `RestoreFromSave(null)` 被呼叫 → `InitializeAsNewGame` 被呼叫 |
| **EC-11**：玩家手動刪 `save.json` 但保留 backup | Bootstrap Phase A 候選 0 `File.Exists` 為 false → 跳到候選 1（`save.bak1`）→ 成功載入 `loadedFromIndex = 1`；下次節流寫入透過 `File.Move(savePath + ".tmp", savePath)`「首次寫入無 backup 來源」分支重建 `save.json` | `SaveFileIO.ReadWithFallback` / `SaveFileIO.WriteAtomic` | PlayMode：執行寫入 → 退出 → 手動刪 `save.json` 保留 bak1 → 重啟；斷言 `loadedFromBackupIndex == 1` 且下次寫入後 `save.json` 重生 |
| **EC-12**：終末檔被玩家手動刪除 | FT-10 不偵測終末檔遺失，不嘗試重建；不影響核心循環（終末檔不參與 Bootstrap 回退鏈）；P-02 歷史檢視介面呈現策略由 P-02 決定（建議列舉時 `File.Exists` 預檢，缺失條目不顯示或顯示「已刪除」標記）；FT-10 不維護終末檔索引清單，每次以 `Directory.GetFiles(persistentDataPath, "save_gameover_*.json")` 動態列舉 | （無 FT-10 主動程式碼；屬「不偵測」契約） | EditMode：手動刪除 `save_gameover_<ts>.json` → 啟動遊戲 → 斷言 Bootstrap 不受影響 + `IsGameOver()` 不依賴終末檔 |

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
|---|---|---|---|
| §3.1.1 儲存路徑與檔名 | §6.1 / §5.4.3 | 對齊 | savePath / bakPaths / save.tmp / save_gameover_*.json 全列入 |
| §3.1.2 Root JSON Schema | §5.3 `SaveDataRoot` | 對齊 | 採具名欄位 + 嵌套 owner JSON 字串；`ft03Decision` 薄層為 "{}" 或省略 |
| §3.1.3 不參與序列化的系統 | §2.3 上游依賴表 | 對齊 | F-01 / F-02 / C-01 / C-03~05 / FT-04 / FT-05 明示不消費 ISaveable |
| §3.2.1 軌道一：事件標記 | §5.2 / §5.4.2 軌道一 | 對齊 | 11 類事件全列舉；handler 只標 `_isDirty`，OnDestroy 對稱解除 |
| §3.2.2 軌道二：節流自動寫入 | §5.4.2 軌道二 / §6.1 | 對齊 | `Time.unscaledTime` + `_saveAutoIntervalSec`；空 dirty 時更新 `_lastSaveTime` 避免重複比對 |
| §3.2.3 軌道三：強制寫入 | §5.4.2 軌道三 | 對齊 | 不採 OnApplicationPause / Focus；理由對應 GDD 既定 |
| §3.2.4 寫入互斥保護 | §5.4.3 / §7 EC-8 | 對齊 | `_isSaving: bool` finally 清零；後到者 `if (_isSaving) return` |
| §3.3.1 Script Execution Order | §5.4.1 Awake / §6.3 結構性常數 | 對齊 | F-01 = -200 / FT-10 = -100 / 其他 = 0 |
| §3.3.2 Bootstrap 五階段 | §5.4.1 Phase A~E | 對齊 | 含 OnLoadFailed → OnLoadCompleted 順序契約（先後關係） |
| §3.3.3 Owner 還原拓撲順序 | §5.4.1 Phase C / §4.5 | 對齊 | 11 個 owner 順序硬編於 `SortByRestoreOrder` |
| §3.3.4 Critical / Degradable 分流 | §5.4.1 Phase C / §7 EC-3 / §7 EC-4 | 對齊 | Critical 列表（F-03/C-02/FT-06/FT-08/FT-12）；Degradable 列表（C-06/FT-01/FT-02/FT-03/FT-07/FT-09） |
| §3.4.1 原子寫入流程 | §5.4.3 ExecuteSave | 對齊 | 步驟順序：WriteTmp → RotateOlderBackups → File.Replace；先 rotate 後 Replace 順序保證對齊 GDD §3.4.1 trace 範例 |
| §3.4.2 損毀回退鏈 | §5.4.1 Phase A / §7 EC-2 / §7 EC-3 | 對齊 | 候選 0~N 順序；single-fail log warning 跳下一份；schemaMeta null 視為失敗 |
| §3.4.3 玩家可見性 | §3.3 對映表 / §5.2 / §7 EC-2 | 對齊 | 不彈對話框；`loadedFromBackupIndex` 僅 debug overlay；OnSaveFailed / OnLoadFailed 正式 UI 不顯示 |
| §3.5.1 OnGameOver 處理流程 | §5.4.2 軌道三 / §7 EC-6 | 對齊 | 三步驟：ExecuteSave → File.Copy（idempotent IOException 處理）→ OnGameSealed |
| §3.5.2 ResetToNewGame() 流程 | §5.4.4 ResetToNewGame | 對齊 | 刪主存檔 + 全 backup；終末檔不動；發 OnGameReset；FT-10 不主動 reload scene |
| §3.6.1 JsonUtility 不直接支援的容器 | §5.3 注釋 | 對齊 | Dict/Queue/HashSet 由各 owner 自行轉 List 中介；FT-10 不提供 helper |
| §3.6.2 Owner 序列化責任歸屬 | §5.1 ISaveable / §5.3 / §5.4.3 AssembleRootJson | 對齊 | ISaveable 介面 5 成員；FT-10 純 string 嵌套組裝 |
| §3.7.1 對外 Public API | §5.1 ISaveLoadService | 對齊 | 6 個方法簽名與用途；ExecuteSave / RotateOlderBackups 為 internal |
| §3.7.2 對外事件契約 | §5.2 事件清單 | 對齊 | 6 個出向事件；OnLoadFailed → OnLoadCompleted 順序契約 |
| §3.7.3 Runtime 狀態欄位 | §5.3 / §5.4.1 / §5.4.3 | 對齊 | `_isDirty / _isSaving / _lastSaveTime / _schemaMeta / _saveables / _criticalSaveables / _eventUnsubscribers` |
| §3.7.4 Owner 註冊機制 | §5.4.1 步驟 4 | 對齊 | `FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<ISaveable>()` + SortByRestoreOrder；對齊 Unity 6 新 API |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 對應實作 | 等價說明 |
|---|---|---|
| §4.1 節流寫入觸發判定 `(now - _lastSaveTime ≥ SAVE_AUTO_INTERVAL_SEC) ∧ _isDirty` | §5.4.2 軌道二偽碼 | 直接採用；`now = Time.unscaledTime`；空 dirty 時仍更新 `_lastSaveTime`（GDD §3.2.2 對齊） |
| §4.2 lastActiveTimestamp 取值 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` | §5.4.3 ExecuteSave 步驟 1 | 直接採用；同一 ExecuteSave 不重複取值 |
| §4.3 終末存檔檔名生成 `{prefix}{unix}.json` | §5.4.2 軌道三步驟 2 / §6.1 SAVE_GAMEOVER_PREFIX | 直接採用；`File.Copy(overwrite: false)` 配合 try-catch IOException 達成 idempotent |
| §4.4 Backup Rotation 索引映射 + 演算法（先 rotate 較舊端 後 File.Replace） | §5.4.3 ExecuteSave 步驟 3 + §7 EC-1 | 直接採用；步驟順序對齊 GDD §3.4.1 trace 範例（gen-3 → gen-4 寫入後 save=gen-4 / bak1=gen-3 / bak2=gen-2 / bak3=gen-1） |
| §4.5 Bootstrap 回退鏈索引 | §5.4.1 Phase A | 直接採用；`loadedFromBackupIndex` 對應 0~N 與 -1（全失敗） |

**全部公式直接採用 GDD 偽碼，無替代方案**。

### 8.3 未能實現的規則與修改建議

| 編號 | 描述 | 影響 | 處理計畫 |
|---|---|---|---|
| **B-01** | `SaveLoadService.cs` 預估 350~430 行接近 500 行門檻；需於實作期監控行數，必要時進一步拆分（如 `SaveLoadEventSubscriber.cs` 抽出事件訂閱邏輯） | 不阻礙設計；屬實作期工程議題 | 實作期 reviewer 監控行數；超過 500 行於 §8.3 補登並評估二次拆分 |
| **B-02** | P-02 / P-03 GDD 待設計；下游契約（`OnGameSealed` / `OnLoadCompleted` / `OnGameReset` 訂閱者、scene reload 責任、debug overlay 呈現策略）只能依 FT-10 GDD 既定設計撰寫，待 P-02 GDD 完成後雙向校驗 | 不阻礙 FT-10 實作；P-02 撰寫時對齊本 FSD §2.4 即可 | P-02 GDD 撰寫時參考本 FSD §2.4 / §5.2；雙向依賴更新對齊 GDD §6.4 反向依賴清單 |
| **B-03** | `ISaveable.RestoreFromSave(null)` 契約規定「立即 InitializeAsNewGame() 並 return」；此契約須由各 owner FSD 同步登記（已於 FT-08 / FT-09 / FT-06 / FT-07 等 FSD §8.3 標 `B-04: ISaveable 簽名待 FT-10 定案`） | 不阻礙設計；屬契約傳遞議題 | FT-10 FSD 通過後，使用者統一安排批次更新各 owner FSD §6.3「ISaveable 契約」段落，確認 null 處理路徑一致 |
| **B-04** | F-01 `DataManager.GetSystemConstant<T>` 對 `string` 型別取值的可用性需確認；目前 F-01 FSD 已實作 `GetString` API（FSD-self-check-log 2026-04-26 patch 條目 3 已落地），但 `GetSystemConstant<string>` 泛型多載是否存在須於實作期驗證 | 影響 InitTuning 的 `SAVE_FILE_NAME` / `SAVE_BAK_PREFIX` / `SAVE_GAMEOVER_PREFIX` 載入；fallback 至硬編預設值 | 實作期確認；若泛型多載缺失則改呼叫 `DataManager.GetString("SAVE_FILE_NAME", "save.json")` |
| **B-05** ✓ 已解決 | Bootstrap Phase A 重進回退鏈下一份的實作細節：Critical owner 失敗時，已部分還原的其他 owner（如 F-03 已成功，C-06 失敗）需於下一輪 Bootstrap 開始前重置 | 影響 EC-3 處理路徑 | 已於 §5.4.1.D 落地 `ResetAllSaveables()` helper：retry loop 在 `catch (CriticalRestoreFailedException)` 區塊內、`continue` 至下一份 candidate 之前，強制呼叫所有 owner `InitializeAsNewGame()`；不再依賴 owner `RestoreFromSave` 冪等性假設；2026-04-28 design-review patch 落地 |
| **B-06** | `OnApplicationQuit` 在 Editor 模式下停止 Play 不一定觸發；EditMode test 須以 `OnDestroy` 或顯式呼叫 `ForceSave` 模擬 | 不阻礙設計；屬測試策略議題 | qa-tester 撰寫 PlayMode test 時注意 build 後驗證 vs Editor 模擬差異 |
| **B-07** | F-02 `Initialize(lastActiveTimestamp)` 呼叫時序：必須在 Phase D（所有 owner 還原完成後）；若某 owner 還原依賴「F-02 已 Initialize」狀態，此 owner 應不在 §3.3.3 拓撲順序內（FT-10 GDD 既定 owner 列表內無此情況） | 不阻礙設計；屬時序契約 | FT-10 內部 invariant；新增 owner 時 reviewer 須驗證不依賴 F-02 已 Initialize |
| **B-08** | 終末檔列舉 API（`Directory.GetFiles(persistentDataPath, "save_gameover_*.json")`）由 P-02 歷史檢視介面自行呼叫；FT-10 未提供 wrapper（GDD §7.5 EC-12 既定不維護索引清單） | 不阻礙設計；屬職責邊界 | P-02 設計時直接使用 `System.IO`；如未來需要排序 / 分頁等業務邏輯，再評估 FT-10 是否提供 helper |

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
|---|---|---|---|
| — | — | — | 無（FT-10 GDD 設計完整且通過 design-review，FSD 撰寫過程未發現需要回註修正之處） |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
|---|---|---|---|
| — | — | — | 無（FSD 撰寫過程未觸發 §2.5 衝突暫停流程） |

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥
- [x] §1.3 完成目標可被測試驗證（DoD-01~14 全部對應 GDD §8 AC 條目）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節引用 / Data-Specs / 上游 15 系統 / 下游 P-02 P-03 + 10 owner / 跨系統事件契約 19 條）
- [x] §3.3 對映表覆蓋所有幻想／目的（8 行對映；含「公會編年史」「不可讀檔復活」「Backup 隱性 fail-safe」三大 §2 設計原則）
- [x] §4 Script 清單欄位齊全（6 Script，路徑 / SRP / 依賴 / 預估規模四欄完整）
- [x] §5 API/事件/資料結構/資料流齊備（6 API + 19 事件 + 5 資料結構 + 4 資料流偽碼）
- [x] §6 CSV 引用含對應 Data-Specs；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（12 條 EC 全有對策、涉及 Script、驗證方式）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（22 行覆蓋 §3.1.1~§3.7.4）
- [x] §8.2~§8.5 如實登記（公式 5 條全直接採用、未實現 8 條 B-01~B-08、回註無、衝突無）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（後續同步處理）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
|---|---|---|---|---|---|
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh） | 通過 | 通過 | 通過 | 正向 FSD（無既有 Save/Load Script）；FSD 未拆分（6 Script：SaveLoadTypes / ISaveable / ISaveLoadService / SaveFileIO / SaveLoadBootstrap / SaveLoadService，預估 1000~1310 行）；GDD §3.1~§3.7 共 22 子節全部「對齊」；GDD §4.1~§4.5 公式直接採用偽碼（未替代）；GDD §5.1~§5.13 共 12 條 EC 皆有對策、涉及 Script、驗證方式；GDD §8 AC-1.1~AC-EC-12 共 38 條全對齊 §1.3 DoD-01~DoD-14；無真實衝突；建議項 B-01（SaveLoadService.cs 行數監控）/B-02（P-02 P-03 待設計）/B-03（ISaveable null 契約傳遞至各 owner FSD）/B-04（GetSystemConstant<string> 確認）/B-05（Bootstrap retry 冪等性）/B-06（OnApplicationQuit Editor 模擬）/B-07（F-02 Initialize 時序 invariant）/B-08（終末檔列舉 wrapper）皆不阻礙實作；§8.4 無 GDD 回註；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh）— design-review patch 後重 review | 通過 | 通過 | 通過 | `/design-review FT-10-FSD` 提出 5 條 implementability 建議（4 條 P0 / 1 條 P1）已全數於 §5.4.1 落地：(1) Phase A 補外層 retry loop 偽碼（idx 0~N + 哨兵；critical 失敗 `continue` 至下一份）；(2) §5.4.1.A 補 `SortByRestoreOrder` OwnerKey-indexed dictionary 實作偽碼（11 entries + 未知 key 排序至末）；(3) §5.4.1.B 補 `SubscribeAllEvents` + `_eventUnsubscribers` push Action 偽碼；(4) §5.4.1.C 補 `ExtractOwnerJson(root, ownerKey)` switch 對映偽碼（含空字串視為 null 規則）；(5) §5.4.1.D 補 `ResetAllSaveables()` helper 偽碼（retry 前強制全 owner `InitializeAsNewGame`，不再依賴 owner 冪等假設）；Phase D 偽碼補一行說明 timestamp 取自最後成功 Phase B 的 `_schemaMeta.lastActiveTimestamp`。§8.3 B-05 標「✓ 已解決」並引用 §5.4.1.D。其餘 7 條建議（B-01/02/03/04/06/07/08）維持原狀（皆為實作期 / 跨 FSD 同步議題，非 FSD 內可解決）；無新衝突 |
| 2026-04-30 | Claude Code 主體 | 待 patch | 待 patch | 待 patch | **v3.1 patch P3.1-006 同步紀錄**：FT-10 GDD 已寫入 v3.1 patch（InitializeAsNewGame Bootstrap 補奧菲莉雅初始化 + SaveData schema 標註 FT-09 v3.1 三個新持久化欄位）。FSD Script 設計待補：(1) SaveLoadBootstrap §5.4.1 Phase D 後新增 Step（呼叫 `C02.RegisterUniqueAdventurer(SystemConstants.OPHELIA_TEMPLATE_ID)`，位置在 C-02 名冊初始化後、FT-01 候選池初始化前）；(2) SaveData schema 引用文件補充 FT-09 三新欄位（factionStoryV31_pendingMissingNight / _totalAdventurerDeaths / _blockedStages，由 FT-09 owner 透過 ISaveable 持久化）。完整 patch 規格見 `_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` §3.2。 |
