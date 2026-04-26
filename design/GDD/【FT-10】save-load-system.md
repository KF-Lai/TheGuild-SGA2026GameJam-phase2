# Save/Load System 系統設計文件

_建立時間：2026-04-26_
_狀態：草稿（8 節全完成，待 design-review）_
_最後更新：2026-04-26_
_系統 ID：FT-10_

**🔖 全 8 節完成**：§1 ~ §8 全部寫入。下一步：(a) 執行 `/design-review FT-10`；(b) 通過後處理 §6.4 反向依賴 15 個 GDD 更新；(c) 進入 Codex 工項書建立階段。

---

## 設計來源（Design Inputs）

本 GDD 基於以下決策來源撰寫：

- `design/gdd/game-concept.md` —「桌面原生」「有重量的決策」「真實後果」三大支柱；Phase 2 改用 Unity 本地存檔（取代 Phase 1 localStorage）
- `design/gdd/systems-index.md` — FT-10 依賴 ALL 系統（序列化所有系統狀態）；屬 Feature 層
- 上游契約（FT-10 必須消費或被消費）：
  - `[F-01] data-manager.md` §6 — `GetAll<T>` 反序列化時驗證 ID 合法性
  - `[F-02] time-system.md` §3.6 / §6.2 — 計時器清單需序列化；`TimeSystem.Initialize(lastActiveTimestamp)` 由 FT-10 在載入後呼叫驅動離線計算
  - `[F-03] resource-management.md` §6.2 — 序列化 `currentGold` / `currentReputation` / `_warningState` / `_bankruptcyWarningStartTime` / `_warningDurationSec` / `_currentBankruptcyThreshold`
  - `[C-01] mission-database.md` §6 — 反序列化驗證 `missionID` 仍合法（透過 `GetTemplate`）
  - `[C-02] adventurer-management.md` §3.1 / §6 — `AdventurerInstance` runtime 物件由 FT-10 序列化整個名冊
  - `[C-06] world-danger-system.md` §3 — `currentDangerLevel` / `gameStartTimestamp` 由 FT-10 還原；Awake 設預設、FT-10 在 Start 之前還原
  - `[FT-01] adventurer-recruitment.md` §5.4 / §6 — 序列化候選池、刷新時間戳、免費刷新次數
  - `[FT-02] mission-dispatch.md` §5.5 / §6 — 序列化 `activeMissions` 列表、`_nextActiveMissionID`；載入後重建計數器，FT-02 自行重新訂閱 F-02 `OnSecondTick` 觸發 `TickCompletionCheck`（無需任何 F-02 計時器 API）
  - `[FT-03] npc-decision-system.md` §6 — 透過 C-02 `AdventurerInstance` 一併序列化 `idleSinceTimestamp` / `lastAutoPickupTimestamp`
  - `[FT-04] outcome-resolution.md` §6 — FT-04 **無持久狀態**，不參與序列化
  - `[FT-05] guild-gold-flow.md` — Jam 範疇外，未來擴充收支日誌時訂閱（FT-10 Jam 版不持久化結算歷史）
  - `[FT-06] guild-core.md` §3.1 / §3.2 / §3.5 — `GuildState` 全量序列化（`guildName` / `displayName` / `foundingTimestamp` / `currentLevel` / `gameOverState`）；訂閱 `OnGuildInitialized` 觸發首次存檔、訂閱 `OnGameOver` 封存存檔；`pendingLevelUpQueue` runtime-only 不序列化
  - `[FT-07] guild-building-system.md` §3.1 / §6 — `BuildingState[]` 全量序列化（6 棟建築 `currentLevel`）
  - `[FT-08] gacha-system.md` §3.1 / §6.7 — `StaffPlayerState` + `CandidateCard[]` 全量序列化；`reserveConsumedFlag` 永久化、`pityCounter` 跨會話累積、`lastAutoRefreshTimestamp` 全域單一計時器；驗證規則：`staffID > 0 → rolledRarity == StaffTable[staffID].rarity` / `staffID XOR trashItemID`，違規拋 `CandidateCardValidationException`
  - `[FT-12] staff-system.md` §3.1 / §6.7 — `StaffInstance[]` + `_nextInstanceID` + `_lastSalaryTimestamp` 全量序列化；驗證 `StaffInstance.staffID` 透過 F-01 `Get<StaffData>` 確認合法,違規拋 `CriticalRestoreFailedException`
  - `[FT-09] faction-story-system.md` §3.7 / §5.10 — `FactionStorySaveData` 區塊（`factionScores: Dict<int,int>` / `unlockedStageIndices` / `pendingDialogueStages` / `routeCompletedFlags`）；EC-10 SaveData 反序列化異常不阻擋 Bootstrap
- 全局規則：
  - `feedback_data_driven` — 一切由 CSV 驅動，零硬編碼
  - `feedback_time_units` — 時間單位僅秒 / 小時
  - `feedback_gates_data_driven` — 觸發閾值、刷新間隔、保留秒數等須由 CSV / SystemConstants 驅動

---

## 1. 概要（Overview）

**FT-10 Save/Load System 是公會所有持久化資料的統一存取層**：以 Unity `Application.persistentDataPath` 為儲存後端、單一 JSON 主存檔（`save.json`）+ N 份 backup rotation 為檔案佈局；採「事件標記 dirty + 節流自動寫入 + OnApplicationQuit 強制寫入」三軌觸發策略，避免高頻 I/O 同時保證關鍵狀態變動最遲於下次節流週期落地。本系統**不擁有任何遊戲狀態**——所有欄位皆從各 owner 系統的查詢 API 抽取（如 `F-03.GetGold()`、`FT-07.GetBuildingState()`），載入時呼叫各 owner 的還原 API；FT-10 僅負責**序列化編解碼、I/O、Bootstrap 順序協調、損毀回退、Game Over 封存**。系統涵蓋三大職責：

### A. 持久化管線（Persistence Pipeline）

依序列化契約對 ALL owner 系統的持久化 surface 進行抽取 → JSON 編碼 → 寫入 `save.json` → backup rotation。觸發來源三軌並存：

- **事件標記**：訂閱 owner 系統的關鍵變動事件（`OnGuildInitialized` / `OnMissionResolved` / `OnCommissionSettled` / `OnRecruitSuccess` / `OnBuildingUpgraded` / `OnGuildLevelChanged` / `OnFactionStoryStageUnlocked` 等）標記內部 `_isDirty` 旗標，**不立即寫入**
- **節流自動寫入**：每 `SAVE_AUTO_INTERVAL_SEC`（預設 60s，§7）檢查 `_isDirty`，為 `true` 則寫入並清旗
- **強制寫入**：`OnApplicationQuit` / `OnApplicationPause(true)` / `OnGameOver` 三個時機**忽略 dirty 旗標**強制寫入並清旗，確保 session 邊界永不漏存

每次寫入完成後依序輪替舊 backup（當前 `save.json` → `save.bak1`、舊 `save.bak1` → `save.bak2` ⋯ 第 N+1 代丟棄），形成 N+1 代版本歷史，`N = SAVE_BACKUP_COUNT`（§7）。

### B. Bootstrap 協調（Bootstrap Orchestration）

遊戲啟動時依固定順序驅動各 owner 系統的還原：

1. F-01 DataManager 完成 CSV 載入（前置條件，§3 指定 Script Execution Order）
2. FT-10 嘗試讀取 `save.json` → 失敗依序嘗試 `save.bak1..bak{N}`（§5 損毀回退）；全失敗即新遊戲
3. 各 owner 依**依賴拓撲順序**執行還原（F-03 → C-06 `currentDangerLevel` → C-02 名冊 → FT-06 → FT-07 → FT-02 `activeMissions` → FT-08 → FT-12 → FT-01 → FT-03 idle 時間戳 → FT-09），每階驗證 ID 合法性（透過 F-01 `GetAll<T>`）；驗證失敗的單筆資料依各 owner GDD 既定 EC 策略處理（FT-09 EC-10 靜默降級、FT-08 §3.2.3 fail-fast 拋 `CandidateCardValidationException` 等）
4. **離線計算交棒**：所有 owner 還原完成後，FT-10 呼叫 `F-02.Initialize(lastActiveTimestamp)` 觸發離線時間差計算；F-02 內部驅動任務推進、發布 `OnOfflineResolved`，由 FT-04 / P-02 接手結算與摘要畫面（FT-10 不參與結算）

Bootstrap 流程**不阻擋核心循環**：可降級系統（FT-09 等）的反序列化異常**不傳遞至上層**；僅 F-01 / F-03 / C-02 等 critical 系統反序列化失敗才觸發整檔回退至下一份 backup。

### C. Game Over 封存（Game Over Sealing）

訂閱 FT-06 `OnGameOver` 事件，執行「終末歸檔 + 主存檔保留為 Over 態」：

1. 立即執行一次強制寫入（同 A 軌道）保證 `gameOverState = Over` 等最終狀態完整
2. 將當前 `save.json` 複製為 `save_gameover_<unixTimestamp>.json` 終末存檔，永久保留
3. 主存檔 `save.json` **保留**且包含 `gameOverState = Over` 旗標——下次啟動時 FT-10 走標準載入流程、FT-06 還原 Over 態，由 P-02 偵測 `IsGameOver()` 提供「回顧結算 / 開新公會」雙選項；玩家點「開新公會」時 P-02 呼叫 FT-10 `ResetToNewGame()`（刪除 `save.json` 並由各 owner 重新初始化）
4. 終末存檔 `save_gameover_<unixTimestamp>.json` 為唯讀歷史紀錄，呈現策略由 P-02 決定（歷史檢視介面），FT-10 僅保證資料完整性與檔名規則；對應 game-concept「有重量的決策」支柱——破產為終局，唯一「繼續玩」的方式是建立新公會，不可從終末檔讀檔復活

### 核心職責

1. 對 ALL owner 系統執行序列化抽取與反序列化還原（不擁有狀態，僅編解碼與 I/O）
2. 三軌觸發策略平衡即時性與磁碟負擔
3. Bootstrap 順序協調 + 離線計算交棒至 F-02
4. Backup rotation 與 Game Over 封存的 fail-safe 機制

### 不負責

- 任何遊戲狀態的真實值（owner 系統各自負責，FT-10 僅 read / write）
- 離線時間差計算（F-02 `Initialize` 內部處理；FT-10 僅交棒）
- 任務結算與摘要顯示（F-02 觸發 → FT-04 執行 → P-02 顯示）
- Game Over 結算畫面內容（P-02 訂閱 `OnGameOver` 自處）
- 玩家手動匯出 / 雲端同步 / 多裝置（Jam 範疇外）

### Jam 範疇

- 單槽主存檔 `save.json` + N 份 backup（N 預設 3，§7 `SAVE_BACKUP_COUNT`）
- 終末存檔每次 Game Over 各自一份，依 unix timestamp 命名，永久保留不分代
- 序列化器：Unity `JsonUtility`（內建零依賴；`Dictionary` / `Queue` / `HashSet` 須轉 `List` 中介，與 FT-09 §3.7.1 既定模式對齊）
- **不引入** `version: int` schema 演進欄位（FT-09 EC-10 既定決議；Post-Jam 再導入）
- **不加密 / 不簽章**（單機遊戲、無經濟系統濫用風險）

### 不在 Jam 範疇

- 多槽存檔 UI（Post-Jam）
- 雲端同步 / Steam Cloud（Post-Jam）
- Schema versioning 與跨版本遷移腳本（Post-Jam）
- 存檔加密 / 防作弊簽章（Post-Jam）
- 收支日誌 / 結算歷史持久化（FT-05 §6.1 預留訂閱契約，Post-Jam）

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-10 在玩家層面幾乎是隱形系統——好的存讀檔體驗是「**讓玩家忘記它存在**」，但它支撐 game-concept 三個支柱與 FT-02 / F-02 共同實現的核心情緒。本系統聚焦三個面向：

**放心關掉的安全感（Submission / Comfort）**

- 玩家可以隨時關遊戲——不需要找「存檔點」、不需要記得按「儲存」按鈕
- 即便閃退、停電、家貓踩到電源鍵，**最多丟掉一分鐘的進度**（節流間隔內），核心狀態永遠安全
- 對應「桌面原生」支柱：遊戲嵌入工作環境，必須能在任何時機被中斷

**離線回應的揭曉（Sensation / Discovery）**

- 早上打開遊戲，看到「你離開了 9 小時 23 分鐘，有 5 個任務已完成」的摘要——這個瞬間的**期待與揭曉**是核心情緒節點
- 任務結算結果（誰活著、誰死了、賺了多少）以通知列表呈現，每一條都是「我不在的時候世界發生了什麼」
- 對應 game-concept 留存標的「好奇心」：派出去的冒險者結果如何？

**公會編年史（Narrative / Submission）**

- 終末存檔 `save_gameover_<timestamp>.json` 是公會故事的最後一頁——它會永遠在那裡，被歸檔、被回顧
- 公會破產後，玩家在啟動畫面看到「回顧結算」按鈕——可以再看一次自己經營過的這間公會的最終狀態
- 「開新公會」時主存檔被覆寫，但**過往公會的終末檔仍然保留**，形成個人遊戲史的層積
- 對應「有重量的決策」支柱：破產是真實終局，不能讀檔復活；但故事可以被銘記

### 玩家幻想敘事

> 「下班前我看了一眼委託板，派出去三隊冒險者就直接關了筆電——我知道他們在外面跑著任務，但不需要我盯著。隔天早上開機，遊戲視窗自動跳出來：『你離開了 14 小時 32 分鐘，有 2 個任務已完成』。我點進去，看到艾莉莎成功帶回了 800 金幣的傭金，但崔斯坦——他被派去那單 A 級護送——再也沒回來。我在通知列表裡看到他的名字、看到他的最後一次任務報告。公會的故事在我睡覺的時候繼續寫了下去，而我就是要繼續寫下去的人。」

### 設計原則（FT-10 如何強化幻想）

1. **零操作的安全感**：玩家不該意識到「存檔」這件事——沒有手動存檔按鈕（Jam 範疇）、沒有「請等待存檔完成」loading、沒有「未存檔的變更」警告。三軌觸發策略（事件 + 節流 + 退出）在背後保證一切，玩家只需專注於決策
2. **離線摘要交給 F-02**：FT-10 自身不參與「離線了多久」的呈現——這是 F-02 + P-02 的舞台。FT-10 的角色是**準時準確地交棒** `lastActiveTimestamp`，讓整個離線體驗成立
3. **Backup 是隱性 fail-safe**：玩家永遠不會看到「存檔損毀，是否載入備份？」的對話框（除非真的所有 backup 全壞，才走新遊戲）。回退邏輯在背後悄悄發生，玩家頂多回到稍早幾分鐘的狀態，而不是失去整週進度
4. **Game Over 是封存而非刪除**：對應 game-concept「冒險者來來去去——他們的名字留在公會的歷史中」——破產的公會也應該留在玩家的歷史中。終末檔是這個敘事承諾的技術載體
5. **不可讀檔復活**：拒絕任何「上次存檔點」式的錯誤回退——對應 game-concept 設計測試「這個決策有沒有可能導致不可逆的損失？」FT-10 的 Game Over 處理就是答案：可以，永久不可逆

### 關鍵情緒節點（Emotional Beats）

| 時機 | 觸發 | 期望情緒 |
|---|---|---|
| 第一次關遊戲後再開 | FT-10 載入 `save.json`，公會狀態完整還原 | 信任感、放鬆 |
| 隔夜開機看到離線摘要 | FT-10 交棒 `lastActiveTimestamp` → F-02 觸發摘要 | 期待、揭曉 |
| 閃退後重啟發現只丟一分鐘 | FT-10 節流寫入確保最近狀態 | 安心、無感 fail-safe |
| 進入 Game Over 訃聞 | FT-10 強制寫入 + 終末存檔複製 | 沉重、終局感 |
| 啟動畫面點「回顧結算」 | FT-10 唯讀載入終末檔（P-02 呈現） | 緬懷、公會編年史 |
| 按「開新公會」 | FT-10 `ResetToNewGame()` 刪 `save.json` | 重新開始、終末檔仍存的安心 |

## 3. 詳細規則（Detailed Rules）

> 本章採 **§3.1 → §3.7** 七子節結構：檔案佈局與 schema → 三軌寫入策略 → Bootstrap 順序 → backup rotation 與損毀回退 → Game Over 封存與 reset → 序列化容器規範 → API 與 runtime 狀態。所有規則對齊 §1 三大職責（Persistence Pipeline / Bootstrap Orchestration / Game Over Sealing）。

### 3.1 檔案佈局與 SaveData Schema

#### 3.1.1 儲存路徑與檔名

所有檔案位於 `Application.persistentDataPath`（Windows 預設：`%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\`），FT-10 不在此路徑外讀寫任何檔案。

| 檔案 | 用途 | 數量 | 生命週期 |
|---|---|---|---|
| `save.json` | 主存檔 | 1（單槽） | 由三軌寫入維護；`ResetToNewGame()` 刪除 |
| `save.bak1` ~ `save.bak{N}` | Backup rotation（N = `SAVE_BACKUP_COUNT` = 3） | 至多 N | 每次寫入主存檔時 rotate；`ResetToNewGame()` 一併刪除 |
| `save.tmp` | 寫入中介檔 | 0 ~ 1（瞬時） | 寫入完成後由 `File.Replace` 消費 |
| `save_gameover_<unixTimestamp>.json` | 終末存檔（公會編年史） | 不限 | 寫入後**永久保留**；`ResetToNewGame()` **不刪除** |

**設計理由**：

- **單槽主存檔**：Jam 範疇不提供多槽 UI（§1「不在 Jam 範疇」既定）
- **`save.tmp` 中介**：避免寫入過程中斷時 `save.json` 處於半損毀狀態（§3.4.1 原子寫入）
- **終末存檔不分代**：每個 Game Over 各自一份、unix timestamp 命名永遠唯一；磁碟成本可忽略（單檔 < 1MB），永久保留即玩家「個人遊戲史的層積」（§2 設計原則）

#### 3.1.2 Root JSON Schema

採**各 owner 系統獨立子區塊**結構，FT-10 僅擁有 schema 入口的 root meta 欄位，所有業務欄位由各 owner 系統自行定義；FT-10 不檢視子區塊內容（純編解碼）。

```json
{
  "schemaMeta": {
    "lastActiveTimestamp": 1714128000,
    "gameOverState": "Active",
    "loadedFromBackupIndex": 0
  },
  "f03Resources": { ... },
  "c02Adventurers": { ... },
  "c06WorldDanger": { ... },
  "ft01Recruitment": { ... },
  "ft02Dispatch": { ... },
  "ft03Decision": {},
  "ft06Guild": { ... },
  "ft07Buildings": { ... },
  "ft08Staff": { ... },
  "factionStorySaveData": { ... }
}
```

> **`ft03Decision` 為薄層 ISaveable**（對齊 FT-03 §6.4）:FT-03 自身**無 instance 級別持久化欄位**（`idleSinceTimestamp` / `lastAutoPickupTimestamp` 由 C-02 `AdventurerInstance` 一併序列化）,故 root 範例顯示為空物件 `{}`,實際 `Serialize()` 回傳 `"{}"`。允許省略此 key——若省略,Bootstrap Step C 對 FT-03 呼叫 `RestoreFromSave(null)` 等同 `InitializeAsNewGame()`,僅重新訂閱事件。

**`schemaMeta` 欄位**（FT-10 擁有）：

| 欄位 | 型別 | 用途 | 寫入時機 |
|---|---|---|---|
| `lastActiveTimestamp` | `long`（UTC Unix 秒） | F-02 離線計算輸入 | 每次寫入時取 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` |
| `gameOverState` | `string`（`"Active"` / `"Over"`） | Bootstrap 後 FT-06 還原依據；P-02 啟動畫面分支判斷 | FT-06 透過 `RestoreFromSave` 還原至 `GuildState`；FT-10 僅 read / write 字串 |
| `loadedFromBackupIndex` | `int`（0 = 主存檔；1~N = bak1~bak{N}） | Debug / 診斷用 | Bootstrap 載入成功後寫入；下次寫入時清零 |

**Owner 子區塊命名規則**：`{ownerID 小寫}{語意關鍵字}`。命名固定後不可變更（無 schema versioning，§1 既定）；命名衝突由 systems-index 集中保留。

**FT-10 不擁有業務欄位**：每個子區塊的內部 schema 由 owner GDD 規範（如 `factionStorySaveData` 結構由 FT-09 §3.7.4 規範）；FT-10 僅以 `string ownerJson = JsonUtility.ToJson(owner.Serialize())` 取得序列化結果並組裝至 root。

#### 3.1.3 不參與序列化的系統

| 系統 | 理由 |
|---|---|
| F-01 DataManager | CSV 載入結果 runtime 重建，無持久化狀態 |
| F-02 Time System | `lastActiveTimestamp` 由 FT-10 schemaMeta 持有，F-02 內部 `_currentTime` runtime-only |
| C-01 Mission Database | 模板表，runtime 由 F-01 載入 |
| C-03/C-04/C-05 Profession/Race/Trait | 純資料表查詢系統，無實例狀態 |
| FT-04 Outcome Resolution | 純函式系統，無持久狀態（§1 設計來源既定） |
| FT-05 Guild Gold Flow | 純流程系統；收支日誌持久化為 Post-Jam |

### 3.2 三軌寫入策略（Event-Mark / Throttle / Force）

#### 3.2.1 軌道一：事件標記

FT-10 訂閱以下 owner 系統事件，事件回呼**僅執行 `_isDirty = true`、不立即寫入**：

| 來源 | 事件 | 觸發語意 |
|---|---|---|
| FT-06 | `OnGuildInitialized` | 首次建立公會 |
| FT-06 | `OnGuildLevelChanged` | 公會升級 |
| FT-04 | `OnMissionResolved` | 任務結算 |
| FT-05 | `OnCommissionSettled` | 委託結算（金流落地） |
| FT-01 | `OnRecruitSuccess` | 招募成功 |
| FT-07 | `OnBuildingUpgraded` | 建築升級 |
| FT-12 | `OnStaffHired` / `OnStaffSalaryDue` | 職員雇用 / 薪水扣款 |
| FT-09 | `OnFactionStoryStageUnlocked` | 陣營劇情階段解鎖 |
| C-06 | `OnDangerLevelChanged` | 危險度升階 |
| F-03 | `OnGoldChanged` / `OnReputationChanged` | 金幣 / 聲望變動 |

**設計理由**：

- 事件清單**僅標記不寫入**避免 high-frequency 事件（如 `OnGoldChanged` 一秒可能觸發多次）造成 I/O 風暴
- 事件未列出但實際變動的狀態（如 FT-03 `idleSinceTimestamp` 持續更新），由節流寫入捕捉——只要任一事件曾觸發 `_isDirty`，下次節流即落地全部 owner 狀態快照
- 訂閱解除：`OnDestroy` 必須對應解除全部訂閱避免 memory leak

#### 3.2.2 軌道二：節流自動寫入

```
每 frame Update():
    if (Time.unscaledTime - _lastSaveTime >= SAVE_AUTO_INTERVAL_SEC) {
        if (_isDirty) {
            ExecuteSave();          // §3.4.1 原子寫入流程
            _isDirty = false;
            _lastSaveTime = Time.unscaledTime;
        } else {
            // 不寫入，但更新檢查時間避免每 frame 比對
            _lastSaveTime = Time.unscaledTime;
        }
    }
```

- `SAVE_AUTO_INTERVAL_SEC` 預設 `60` 秒（§7 可調）
- 採 `Time.unscaledTime` 不受 `Time.timeScale` 影響（避免 PauseTick 時節流停擺）
- **單一 global `_isDirty`**：任何 owner 變動都標記同一個 flag，寫入時序列化全部 owner 子區塊（不支援 partial write，因 JSON 整檔寫入無原子部分更新語意）

#### 3.2.3 軌道三：強制寫入

| 觸發點 | 來源 | 行為 |
|---|---|---|
| `OnApplicationQuit` | Unity lifecycle | 立即 `ExecuteSave()`，**忽略 `_isDirty`**（即便 false 也寫入，確保 `lastActiveTimestamp` 更新） |
| `OnGameOver` | FT-06 事件 | 立即 `ExecuteSave()` → 後續執行 §3.5 終末封存流程 |

**不採用** `OnApplicationPause(true)` / `OnApplicationFocus(false)`：

- 桌面 standalone 主要走 `OnApplicationQuit`，玩家正常關閉一定觸發
- Pause/Focus 在 Steam Overlay / Alt-Tab / 視窗最小化會頻繁觸發，無實質保護收益但增加 I/O 頻率
- 唯一風險：閃退 / 停電 / 進程被強制終結時 `OnApplicationQuit` 不一定執行——此情境由節流軌道兜底（最多丟失 60 秒進度，§2「閃退後重啟發現只丟一分鐘」承諾）

#### 3.2.4 寫入互斥保護

`ExecuteSave()` 為非 reentrant：執行中第二次呼叫直接 return，避免並行寫入損毀 `save.tmp`。實作以 `_isSaving: bool` 旗標守護，進入時設 true、finally block 清零。

### 3.3 Bootstrap 順序與還原流程

#### 3.3.1 Script Execution Order

由 Unity Project Settings → Script Execution Order 強制：

```
F-01 DataManager     : -200    // 最早執行（CSV 載入前置條件）
FT-10 SaveLoadSystem : -100    // F-01 之後、其他 owner 之前
其他 owner 系統      : 預設 0  // 由 FT-10 主動驅動，不依賴 Awake/Start 順序
```

**設計理由**：FT-10 在 Awake 內讀檔並驅動 owner 還原，必須晚於 F-01（CSV 已載入才能驗證 ID 合法性）、早於其他 owner 的 Start（否則 owner Start 邏輯可能跑在 stale 預設值上）。

#### 3.3.2 Bootstrap 主流程（5 階段）

```
Phase A: 讀檔
  ├─ HasSaveFile() == false  → 跳到 Phase E（首次遊玩）
  └─ HasSaveFile() == true   → ReadSaveFileWithFallback()（§3.4.2 回退鏈）
                                 ├─ 全部 backup 失敗 → 跳到 Phase E（記錄 Debug.LogError）
                                 └─ 任一份成功      → 進入 Phase B

Phase B: schemaMeta 解析
  ├─ 解析 schemaMeta 失敗  → 視為 Phase A 失敗，往下一份 backup 退（§3.4.2）
  └─ 解析成功              → 記錄 lastActiveTimestamp / gameOverState / loadedFromBackupIndex

Phase C: per-owner 還原（依拓撲順序）
  Order: F-03 → C-06 → C-02 → FT-06 → FT-07 → FT-02 → FT-08 → FT-12 → FT-01 → FT-03 → FT-09
  每個 owner 內部包覆 try-catch，失敗策略依 §3.3.4 critical/degradable 分流

Phase D: F-02 離線計算交棒
  TimeSystem.Initialize(schemaMeta.lastActiveTimestamp)
  → F-02 內部驅動任務推進、發布 OnOfflineResolved
  → FT-04 / P-02 接手結算與摘要畫面（FT-10 不參與）

Phase E: 完成 / 首次遊玩
  ├─ 首次遊玩：發布 OnGameReset 等價事件，各 owner 走預設初始化
  └─ 載入完成：發布 OnLoadCompleted（payload 含 loadedFromBackupIndex）
```

#### 3.3.3 Owner 還原拓撲順序

| 順序 | Owner | 還原內容 | 依賴前置 |
|---|---|---|---|
| 1 | F-03 Resource | `currentGold` / `currentReputation` / `_warningState` / `_bankruptcyWarningStartTime` / `_warningDurationSec` / `_currentBankruptcyThreshold` | 無 |
| 2 | C-06 World Danger | `currentDangerLevel` / `gameStartTimestamp` | F-03（若 SetBankruptcyThreshold push 觸發） |
| 3 | C-02 Adventurer | `AdventurerInstance[]`（含 FT-03 `idleSinceTimestamp` / `lastAutoPickupTimestamp`） | F-01（驗證 templateID） |
| 4 | FT-06 Guild Core | `GuildState`（`guildName` / `displayName` / `foundingTimestamp` / `currentLevel` / `gameOverState`） | F-03（聲望門檻判定） |
| 5 | FT-07 Buildings | `BuildingState[]`（6 棟 `currentLevel`） | FT-06（聲望閘） |
| 6 | FT-02 Dispatch | `activeMissions[]` / `_nextActiveMissionID`；還原後 FT-02 自行重新訂閱 F-02 `OnSecondTick` 觸發 `TickCompletionCheck`；`activeMissions` 還原即可立即被檢查，無需額外 API 呼叫 | C-01（驗證 missionID）/ C-02（驗證 adventurerID） |
| 7 | FT-08 Gacha | `StaffPlayerState` + `CandidateCard[]`；驗證 `staffID > 0 → rolledRarity == StaffTable[staffID].rarity` 違規拋 `CandidateCardValidationException`（critical） | F-01（驗證 staffID）/ FT-06（minGuildLevel 閘） |
| 8 | FT-12 Staff | `StaffInstance[]` + `_nextInstanceID` + `_lastSalaryTimestamp`；驗證 `StaffInstance.staffID` 透過 F-01 `Get<StaffData>` 確認合法,違規拋 `CriticalRestoreFailedException`（critical） | F-01（驗證 staffID）/ FT-08（候選卡先還原以保持 instance 一致性）|
| 9 | FT-01 Recruitment | 候選池 / 刷新時間戳 / 免費刷新次數 | C-02（候選即將注入名冊） |
| 10 | FT-03 NPC Decision | `idleSinceTimestamp` / `lastAutoPickupTimestamp` 由 C-02 一併還原；本階段僅做訂閱重建 | C-02 |
| 11 | FT-09 Faction Story | `factionScores` / `unlockedStageIndices` / `pendingDialogueStages` / `routeCompletedFlags` | F-01（驗證 factionID） |

**還原語意**：`RestoreFromSave` **直接套用儲存值**，**不重新驗證閘門條件**（如等級閘 / 聲望閘 / 升級條件——這些在寫入點時的閘門驗證已完成，還原時信任儲存值）；本表「依賴前置」欄列出的依賴僅指**還原順序**意義上的依賴（如 FT-07 在 FT-06 之後還原以確保 `currentLevel` 已就位供 `Awake` 後查詢使用）。

**驗證契約**：每個 owner 在 `RestoreFromSave(json)` 內**必須**對 ID 欄位透過 F-01 `Get<T>` / `GetAll<T>` 驗證合法性；驗證失敗的 ID 處理策略由各 owner GDD 自定（FT-09 EC-10 過濾未知 factionID、FT-08 §3.2.3 fail-fast 拋例外等）。

#### 3.3.4 失敗策略：Critical vs Degradable

```csharp
foreach (var owner in restoreOrder) {
    try {
        owner.RestoreFromSave(rootJson);
    }
    catch (Exception ex) {
        if (owner.IsCritical) {
            Debug.LogError($"Critical owner {owner.Name} restore failed: {ex}");
            throw new CriticalRestoreFailedException(owner.Name, ex);
            // 由 ReadSaveFileWithFallback 捕獲 → 退至下一份 backup
        } else {
            Debug.LogWarning($"Degradable owner {owner.Name} restore failed, treating as fresh: {ex}");
            owner.InitializeAsNewGame();  // owner 自行重置為預設值
            // 不傳遞例外，繼續下一個 owner
        }
    }
}
```

| 分類 | 系統 | 失敗行為 |
|---|---|---|
| **Critical** | F-03、C-02、FT-06 | 拋例外 → 整檔回退至下一份 backup（§3.4.2） |
| **Critical** | FT-08 / FT-12（`CandidateCardValidationException` / staffID 驗證） | 同上；FT-08 §3.2.3 既定 fail-fast |
| **Degradable** | C-06、FT-01、FT-02、FT-03、FT-07 | log warning + `InitializeAsNewGame()`；不阻擋其他 owner |
| **Degradable** | FT-09 | 對齊 FT-09 EC-10「不阻擋 Bootstrap」既定決議 |

**設計理由**：critical owner 失敗代表存檔結構性損壞（如金幣 / 公會等級缺失），繼續還原會產生不可預測的 runtime 狀態；degradable owner 失敗（如 FT-09 SaveData 區塊損壞）僅影響該系統功能、核心循環仍可進行。

### 3.4 Backup Rotation 與損毀回退

#### 3.4.1 原子寫入流程（Windows-only）

```csharp
void ExecuteSave() {
    if (_isSaving) return;
    _isSaving = true;
    try {
        // 1. 抽取所有 owner 序列化結果，組裝 root JSON
        string rootJson = AssembleRootJson();

        // 2. 寫入中介檔
        File.WriteAllText(savePath + ".tmp", rootJson, Encoding.UTF8);

        // 3. 原子替換 + 自動 rotation（Windows API File.Replace 內建支援）
        if (File.Exists(savePath)) {
            File.Replace(
                sourceFileName: savePath + ".tmp",
                destinationFileName: savePath,        // save.json
                destinationBackupFileName: bakPaths[0] // save.bak1
            );
        } else {
            File.Move(savePath + ".tmp", savePath);   // 首次寫入無 backup 來源
        }

        // 4. 手動 rotate bak2..bak{N}（File.Replace 僅處理 bak1，深層 rotation 自實作）
        RotateOlderBackups();

        // 5. 清旗
        _isDirty = false;
        OnSaveCompleted?.Invoke();
    }
    catch (Exception ex) {
        Debug.LogError($"Save failed: {ex}");
        // 中介檔可能殘留，由下次 ExecuteSave 覆寫；不嘗試刪除避免 race condition
    }
    finally {
        _isSaving = false;
    }
}

void RotateOlderBackups() {
    // 從最舊往最新移：bak3 ← bak2 ← bak1（File.Replace 已生成 bak1，本步驟處理 bak2/bak3）
    for (int i = SAVE_BACKUP_COUNT - 1; i >= 1; i--) {
        string from = bakPaths[i - 1];   // bak{i}
        string to   = bakPaths[i];       // bak{i+1}
        if (File.Exists(from)) {
            File.Move(from, to, overwrite: true);
        }
    }
}
```

**`File.Replace` 語意**（Windows）：

- 原子地將 `save.tmp` 內容移到 `save.json`，同時將舊 `save.json` 移到 `save.bak1`
- 若中途斷電 / 程序終結，磁碟上要不是「舊 `save.json` + 新 `save.tmp`」、要不是「新 `save.json` + 舊 `save.bak1`」，**永不出現半損毀的 `save.json`**

**設計理由**：Jam 範疇 Windows-only（§1 + 用戶決策確認），不為 Mac/Linux 寫 fallback；Post-Jam 跨平台時可改用 `FileStream + FileShare.None` 或 platform-specific atomic primitive。

#### 3.4.2 損毀回退鏈（Read with Fallback）

```csharp
string ReadSaveFileWithFallback(out int loadedFromIndex) {
    // 嘗試順序：save.json → save.bak1 → save.bak2 → save.bak3
    var candidates = new[] { savePath, bakPaths[0], bakPaths[1], bakPaths[2] };
    for (int i = 0; i < candidates.Length; i++) {
        if (!File.Exists(candidates[i])) continue;
        try {
            string json = File.ReadAllText(candidates[i], Encoding.UTF8);
            // 預檢：能否解出 schemaMeta（不解 owner 子區塊）
            var meta = JsonUtility.FromJson<SchemaMeta>(json);
            if (meta == null) throw new InvalidDataException("schemaMeta null");
            loadedFromIndex = i;
            return json;
        }
        catch (Exception ex) {
            Debug.LogWarning($"Load candidate {candidates[i]} failed: {ex}, trying next");
            continue;
        }
    }
    loadedFromIndex = -1;
    return null;  // 全部失敗 → 首次遊玩
}
```

**回退觸發條件**：

| 條件 | 行為 |
|---|---|
| 檔案不存在 | 跳到下一份 |
| 檔案讀取 IOException（權限 / 鎖定） | log + 跳到下一份 |
| JSON 解析例外 | log + 跳到下一份 |
| `schemaMeta` 為 null | log + 跳到下一份 |
| Critical owner restore 拋例外（Phase C） | log + 跳到下一份（**FT-10 在 Phase A 就重新進入回退鏈下一輪**） |

**全部失敗**：發布 `OnLoadFailed`，視為首次遊玩進入 Phase E；終末檔 `save_gameover_*.json` **不參與**回退鏈（純歷史紀錄、唯讀）。

#### 3.4.3 玩家可見性

對齊 §2「Backup 是隱性 fail-safe」：

- **不彈出**「存檔損毀，是否載入備份？」對話框
- 回退結果以 `OnLoadCompleted(loadedFromBackupIndex)` 事件傳遞給 P-02；P-02 可選擇在 console / debug overlay 顯示，但**正式 UI 不呈現** backup 索引
- 全部 backup 失敗時亦不彈出對話框，直接走首次遊玩流程；玩家會察覺「我之前的進度好像不見了」，但不會看到技術錯誤訊息

### 3.5 Game Over 封存與 ResetToNewGame

#### 3.5.1 OnGameOver 處理流程

訂閱 FT-06 `OnGameOver` 事件，依序執行：

```
Step 1: 立即 ExecuteSave()
  ├─ 忽略 _isDirty 旗標強制寫入
  └─ 此次寫入後 save.json 內 schemaMeta.gameOverState = "Over"
     （由 FT-06 在發 OnGameOver 前已更新 GuildState，FT-10 序列化時讀到的是 Over 態）

Step 2: 終末檔複製
  string gameoverPath = $"{persistentDataPath}/save_gameover_{unixTimestamp}.json";
  File.Copy(savePath, gameoverPath, overwrite: false);
  // unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
  // overwrite: false 因 unix 秒級時間戳已唯一；若極端 race 可加 .{milliseconds} 後綴

Step 3: 發布 OnGameSealed
  OnGameSealed?.Invoke(new GameSealedEventArgs {
      gameoverFilePath = gameoverPath,
      sealedTimestamp = unixTimestamp
  });
  // P-02 可訂閱以呈現「公會封存」UI
```

**主存檔保留**：`save.json` **不刪除**，內含 `gameOverState = "Over"` 旗標。下次啟動時 FT-10 走標準 Bootstrap → FT-06 還原 Over 態 → P-02 偵測 `IsGameOver()` 提供「回顧結算 / 開新公會」雙選項。

**對齊 §2 設計原則**：

- 不可讀檔復活：終末檔為唯讀歷史紀錄，**不參與**標準 Bootstrap 回退鏈（§3.4.2）
- 公會編年史：終末檔永久保留，未來「歷史檢視介面」由 P-02 自決呈現策略，FT-10 僅保證資料完整性與檔名規則

#### 3.5.2 ResetToNewGame() 流程

```csharp
public void ResetToNewGame() {
    // Step 1: 刪除主存檔與所有 backup
    if (File.Exists(savePath)) File.Delete(savePath);
    foreach (var bak in bakPaths) {
        if (File.Exists(bak)) File.Delete(bak);
    }

    // Step 2: 終末檔不動（公會編年史承諾）
    // save_gameover_*.json 保持原狀

    // Step 3: 重置內部狀態
    _isDirty = false;
    _lastSaveTime = 0;
    _loadedFromBackupIndex = 0;

    // Step 4: 發布事件供 P-02 reload scene
    OnGameReset?.Invoke();
}
```

**呼叫者**：P-02 在玩家於 Game Over 畫面點擊「開新公會」時呼叫。

**Scene reload 責任**：FT-10 **不**主動 `SceneManager.LoadScene`；發布 `OnGameReset` 後由 P-02 執行 reload。理由：

- FT-10 不擁有 scene 結構知識（哪個 scene 是 Bootstrap 入口）
- Reload scene 後所有 MonoBehaviour 走 Awake → Start 預設初始化路徑，與「首次遊玩」路徑共用，無需各 owner 額外實作 `Reset()` API
- 對齊用戶決策 #7：簡單可靠、不為「重置」寫許多 Reset API

**Backup 一併刪除的設計理由**：對齊用戶決策 #7+#8 與 §2「不可讀檔復活」原則。若保留 backup，玩家可技術上手動拷貝 `save.bak1` → `save.json` 復活破產公會，違反設計承諾。

### 3.6 序列化容器規範

#### 3.6.1 JsonUtility 不直接支援的容器

| 容器類型 | 處理方式 | 範例 |
|---|---|---|
| `Dictionary<K, V>` | 序列化前轉 `List<KvpPair>`，反序列化後重建 | FT-09 `_factionScores: Dict<int,int>` → `List<FactionScoreEntry>` |
| `Queue<T>` | 序列化前 `ToList()`，反序列化後 `new Queue<T>(list)` | FT-09 `_pendingDialogueStages: Queue<int>` |
| `HashSet<T>` | 序列化前 `ToList()`，反序列化後 `new HashSet<T>(list)` | FT-09 `_routeCompletedFlags: HashSet<int>`（對齊 FT-09 §3.7.4 真實型別；`_unlockedStageIndices` 為 `Dictionary<int,int>` 走前一列規則） |
| `interface` / `abstract class` 集合 | JsonUtility **完全不支援多型**；Jam 範疇避免使用 | — |
| Tuple `(A, B)` | 不支援；改用具名 struct/class 並標 `[Serializable]` | — |

**沿用 FT-09 §3.7.1 既定模式**：每個 owner 自行於內部定義 `Serialize()` / `RestoreFromSave()` 方法處理上述轉換；FT-10 **不提供** generic 容器轉換 helper（owner 各自的型別語意不一致，抽 helper 收益低）。

#### 3.6.2 Owner 序列化責任歸屬

```csharp
// FT-10 對外 contract（每個持久化 owner 必須實作）
public interface ISaveable {
    string OwnerKey { get; }              // root JSON 子區塊 key（如 "factionStorySaveData"）
    bool IsCritical { get; }              // §3.3.4 critical/degradable 分類
    string Serialize();                   // 回傳該 owner 的 JSON 字串
    void RestoreFromSave(string ownerJson); // 從 JSON 字串還原；ownerJson 可能為 null（首次遊玩 / 缺欄位）
                                            // 實作端契約：若 ownerJson 為 null，必須立即呼叫
                                            // InitializeAsNewGame() 並 return，不得繼續執行其餘還原邏輯
    void InitializeAsNewGame();           // 預設初始化，§3.3.4 degradable 還原失敗時呼叫
}
```

**FT-10 抽取與組裝**：

```csharp
string AssembleRootJson() {
    var root = new Dictionary<string, object>();
    root["schemaMeta"] = JsonUtility.ToJson(_schemaMeta);
    foreach (var owner in _saveables) {
        root[owner.OwnerKey] = owner.Serialize();  // string 嵌套：owner 已是 JSON
    }
    // 注意：JsonUtility 不支援 Dictionary，root 組裝實際以 root wrapper class 處理
    return WrapAndSerialize(root);
}
```

**設計理由**：FT-10 純編解碼、不擁有業務 schema。owner 變更內部欄位時**不需修改 FT-10**，僅需自行更新 `Serialize` / `RestoreFromSave`；解耦邊界清晰。

### 3.7 API 與 Runtime 狀態

#### 3.7.1 對外 Public API

| API | 簽章 | 用途 | 呼叫者 |
|---|---|---|---|
| `MarkDirty()` | `() → void` | 手動標記 `_isDirty = true`（一般情況用事件訂閱，此 API 為 owner 自定特殊變動使用） | 各 owner |
| `ForceSave()` | `() → void` | 立即執行 `ExecuteSave()`，忽略 `_isDirty` | FT-06 `OnGameOver` 內部 / debug 工具 |
| `HasSaveFile()` | `() → bool` | 檢查 `save.json` 或任一 backup 是否存在 | P-02 啟動畫面（決定「繼續遊戲 / 新遊戲」按鈕狀態） |
| `IsGameOver()` | `() → bool` | 讀 `_schemaMeta.gameOverState == "Over"`（Bootstrap 後可用） | P-02 啟動畫面分支 |
| `GetLoadedFromBackupIndex()` | `() → int` | 0 = 主存檔；1~N = bak{N}；-1 = 首次遊玩 | Debug 工具 |
| `ResetToNewGame()` | `() → void` | §3.5.2 完整流程 | P-02「開新公會」按鈕 |

**不對外暴露**：

- `ExecuteSave()` / `ReadSaveFileWithFallback()` / `RotateOlderBackups()` 為 internal，避免外部繞過三軌策略亂寫
- `_isDirty` flag 為 private，外部以 `MarkDirty()` 介入

#### 3.7.2 對外事件契約

| 事件 | Payload | 發布時機 | 訂閱者 |
|---|---|---|---|
| `OnSaveCompleted` | `void` | 每次 `ExecuteSave` 成功完成（含三軌全部觸發點） | Debug 工具 / P-03 通知（可選） **【→Log API待更新】** |
| `OnSaveFailed` | `Exception ex` | `ExecuteSave` 拋例外時 | Debug；正式 UI **不顯示**（§3.4.3 隱性 fail-safe） |
| `OnLoadCompleted` | `int loadedFromBackupIndex` | Bootstrap Phase C 全部 owner 還原完成（含全 backup 失敗的首次遊玩） | P-02（決定主畫面進入 / 啟動畫面分支） |
| `OnLoadFailed` | `Exception ex` | 全部 backup 失敗時 | 同上；P-02 走首次遊玩路徑 |
| `OnGameSealed` | `string gameoverFilePath, long sealedTimestamp` | §3.5.1 Step 3 | P-02 / P-03 **【→Log API待更新】** |
| `OnGameReset` | `void` | §3.5.2 Step 4 | P-02（執行 scene reload） |

#### 3.7.3 Runtime 狀態欄位

```csharp
public class SaveLoadSystem : MonoBehaviour {
    // 路徑（Awake 初始化）
    private string savePath;                    // {persistentDataPath}/save.json
    private string[] bakPaths;                  // bak1..bak{N}

    // 寫入控制
    private bool _isDirty;                      // §3.2.2 單一 global
    private bool _isSaving;                     // §3.2.4 互斥保護
    private float _lastSaveTime;                // §3.2.2 節流計時（unscaledTime）

    // Schema meta（每次寫入時更新 / 載入後填充）
    private SchemaMeta _schemaMeta;

    // Owner 註冊表（Awake 內透過 FindObjectsOfType 或 DI 收集）
    private List<ISaveable> _saveables;
    private List<ISaveable> _criticalSaveables; // _saveables.Where(s => s.IsCritical)

    // 訂閱清單（OnDestroy 統一解除）
    private List<Action> _eventUnsubscribers;
}

[Serializable]
public class SchemaMeta {
    public long lastActiveTimestamp;
    public string gameOverState;     // "Active" / "Over"
    public int loadedFromBackupIndex;
}
```

#### 3.7.4 Owner 註冊機制

Awake 內掃描所有實作 `ISaveable` 的 MonoBehaviour 並依拓撲順序排列：

```csharp
void Awake() {
    InitPaths();
    var allSaveables = FindObjectsOfType<MonoBehaviour>().OfType<ISaveable>();
    _saveables = SortByRestoreOrder(allSaveables);  // §3.3.3 拓撲順序硬編
    _criticalSaveables = _saveables.Where(s => s.IsCritical).ToList();
    SubscribeAllEvents();
    // 還原流程於下一 frame Start 執行（保證所有 owner Awake 已跑完）
}

void Start() {
    ExecuteBootstrap();   // §3.3.2 五階段
}
```

**`SortByRestoreOrder`**：以靜態列表硬編 §3.3.3 順序；新增 owner 時必須同步更新此列表（compile-time 安全：未在列表內的 `ISaveable` 觸發 Debug.LogWarning）。

> **Unity 6 API**：專案 Unity 版本 = `6000.4.2f1`（Unity 6），`FindObjectsOfType` 已標 obsolete；實作層應採 `Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<ISaveable>()` 等價形式。上方偽碼以舊 API 表達意圖，實作以新 API 為準。



## 4. 公式（Formulas）

> FT-10 屬 I/O / 序列化系統，無平衡向數值公式（成功率 / 機率 / 加權等）。本章列出系統所有**可參數化的計算規則**，並對齊 §3 既有實作描述。所有時間單位遵守全局規則「僅秒 / 小時」（`feedback_time_units`）。

### 4.1 節流寫入觸發判定

**公式**：

```
shouldExecuteSave(now) = (now - _lastSaveTime ≥ SAVE_AUTO_INTERVAL_SEC) ∧ _isDirty
```

**變數定義**：

| 符號 | 型別 | 來源 | 說明 |
|---|---|---|---|
| `now` | float | `Time.unscaledTime` | 當前 frame 的 unscaled 時間（秒） |
| `_lastSaveTime` | float | runtime 狀態 | 上次節流檢查時的 `Time.unscaledTime`（秒） |
| `SAVE_AUTO_INTERVAL_SEC` | int | `SystemConstants` §7 | 節流間隔，預設 60；安全範圍 [10, 600] |
| `_isDirty` | bool | runtime 狀態 | 任一訂閱事件觸發後為 true |

**值域**：`shouldExecuteSave ∈ {true, false}`

**範例計算**（`SAVE_AUTO_INTERVAL_SEC = 60`）：

| 情境 | `now` | `_lastSaveTime` | `_isDirty` | 計算 | 結果 |
|---|---|---|---|---|---|
| 才剛寫入 / 無變動 | 75.2 | 70.0 | false | 75.2 - 70.0 = 5.2 < 60 | 不寫入 |
| 滿足間隔但無變動 | 135.0 | 70.0 | false | 65.0 ≥ 60 但 dirty=false | 不寫入（更新 `_lastSaveTime` 至 135.0） |
| 滿足間隔且有變動 | 135.0 | 70.0 | true | 65.0 ≥ 60 ∧ true | **寫入**，清旗 |
| 高頻變動但未滿間隔 | 80.0 | 70.0 | true | 10.0 < 60 | 不寫入（保留 dirty） |

**邊界條件**：

- `Time.unscaledTime` 採 `float`，遊戲執行 24 小時後精度下降至 ~7.6e-6 秒，遠優於本系統秒級節流要求
- `Time.timeScale = 0`（暫停）時 `unscaledTime` 仍然推進，節流不停擺
- 強制寫入軌道（§3.2.3）**不經過此公式**，直接呼叫 `ExecuteSave()`

### 4.2 lastActiveTimestamp 取值

**公式**：

```
lastActiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
```

**變數定義**：

| 符號 | 型別 | 說明 |
|---|---|---|
| `lastActiveTimestamp` | long（UTC Unix 秒） | 寫入 `schemaMeta.lastActiveTimestamp`，供 F-02 離線計算輸入 |

**值域**：

- 2026-04-26 00:00:00 UTC ≈ `1745625600`
- `long` 容量至 2262 年無溢位風險
- 永遠 ≥ 0（Unix epoch 起算）

**取值時機**：每次 `ExecuteSave()` 進入時取一次，寫入 `_schemaMeta` 後序列化。同一次 `ExecuteSave` 不重複取值（避免寫入耗時造成 owner 序列化前後 timestamp 不一致）。

**離線秒數計算（F-02 內部處理，FT-10 不負責）**：

```
offlineSeconds = clamp(now - lastActiveTimestamp, 0, OFFLINE_MAX_SECONDS)
```

對齊 F-02 §3 公式（`OFFLINE_MAX_SECONDS = 604800` = 7 天）；FT-10 僅交棒 `lastActiveTimestamp`，不執行 clamp。

### 4.3 終末存檔檔名生成

**公式**：

```
gameoverFileName = $"save_gameover_{sealedUnixTimestamp}.json"
sealedUnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
```

**變數定義**：

| 符號 | 型別 | 說明 |
|---|---|---|
| `sealedUnixTimestamp` | long | `OnGameOver` 觸發瞬間的 UTC Unix 秒；§3.5.1 Step 2 取值 |
| `gameoverFileName` | string | 完整路徑 = `Path.Combine(persistentDataPath, gameoverFileName)` |

**範例**：

| 觸發時點（UTC） | `sealedUnixTimestamp` | 檔名 |
|---|---|---|
| 2026-04-26 14:32:18 | 1745677938 | `save_gameover_1745677938.json` |
| 2026-04-26 14:32:25 | 1745677945 | `save_gameover_1745677945.json` |

**唯一性保證**：

- Unix 秒級時間戳搭配 `File.Copy(overwrite: false)` 已足夠（單一玩家不可能 1 秒內連續 Game Over 兩次）
- 極端 race（如 Hot reload 重複觸發 `OnGameOver`）：`File.Copy` 會拋 `IOException`，由 §3.5.1 Step 2 try-catch 捕獲、不重複封存（subsequent `OnGameOver` 視為 idempotent）

### 4.4 Backup Rotation 索引映射

**公式**（`SAVE_BACKUP_COUNT = N = 3`）：

```
bakPaths[i] = Path.Combine(persistentDataPath, $"save.bak{i + 1}")
              for i ∈ [0, N - 1]
```

**索引映射**：

| `i`（0-indexed 內部陣列） | 實際檔名 | 語意 |
|---|---|---|
| 0 | `save.bak1` | 最新 backup（剛剛被 `File.Replace` 從 `save.json` 退役） |
| 1 | `save.bak2` | 次新（前一輪的 `save.bak1`） |
| 2 | `save.bak3` | 最舊（再下一輪會被新的 `save.bak2` 覆寫） |

**Rotation 演算法**（§3.4.1 `RotateOlderBackups` 對應）：

```
for i from (N - 1) downto 1:
    src = bakPaths[i - 1]   // bak{i}
    dst = bakPaths[i]       // bak{i+1}
    if File.Exists(src):
        File.Move(src, dst, overwrite=true)
```

**範例**（執行第三次寫入後的磁碟狀態）：

| 寫入次數 | save.json | save.bak1 | save.bak2 | save.bak3 |
|---|---|---|---|---|
| 第 1 次 | gen-1（剛寫） | — | — | — |
| 第 2 次 | gen-2 | gen-1 | — | — |
| 第 3 次 | gen-3 | gen-2 | gen-1 | — |
| 第 4 次 | gen-4 | gen-3 | gen-2 | gen-1 |
| 第 5 次 | gen-5 | gen-4 | gen-3 | gen-2 |（gen-1 被覆寫，永久丟失）|

**邊界條件**：

- `N = 1`：僅 `bakPaths[0]`，`RotateOlderBackups` for-loop 不進入（`N - 1 = 0` < 1），純由 `File.Replace` 處理
- `N = 0`：不允許（`SystemConstants` 安全範圍 [1, 10]，§7）；強制至少 1 份 backup 維持 fail-safe 承諾
- 寫入第 i 次（i ≤ N）時，bak{i+1..N} 不存在屬正常狀態，`File.Exists` 檢查為 false 跳過

### 4.5 Bootstrap 回退鏈索引

**公式**（讀檔候選順序，§3.4.2 對應）：

```
candidates = [savePath] ⊕ bakPaths
            = [save.json, save.bak1, save.bak2, save.bak3]
```

**`loadedFromBackupIndex` 對應表**：

| 載入來源 | `loadedFromBackupIndex` | 語意 |
|---|---|---|
| `save.json` 成功載入 | 0 | 主存檔正常（最常見路徑） |
| `save.bak1` 成功載入 | 1 | 主存檔損毀，最新 backup 接手 |
| `save.bak2` 成功載入 | 2 | 主存檔 + bak1 都損毀 |
| `save.bak3` 成功載入 | 3 | 僅最舊 backup 仍可用 |
| 全部失敗 | -1 | 視為首次遊玩 |

**載入損失估算**：

```
maxDataLossSeconds(loadedFromIndex) = loadedFromIndex × SAVE_AUTO_INTERVAL_SEC
                                      （上界，假設每次節流間隔都剛好寫入）
```

| `loadedFromIndex` | 最大損失（`SAVE_AUTO_INTERVAL_SEC = 60`）|
|---|---|
| 0 | 0 秒（含當下） |
| 1 | ≤ 60 秒 |
| 2 | ≤ 120 秒 |
| 3 | ≤ 180 秒 |
| -1 | 全部進度 |

**設計理由**：對齊 §2「閃退後重啟發現只丟一分鐘」承諾——即便回退至 bak3，也最多丟失 3 個節流週期（180 秒），遠優於「上次手動存檔點」式的回退語意。



## 5. 邊緣案例（Edge Cases）

> 本章採**混合格式**：EC-1 ~ EC-9 為 §3 已內嵌規範的場景，採「觸發 + 行為摘要 + 引用源」短條目；EC-10 ~ EC-12 為 §3 未明確規範或屬於跨系統時序 / 玩家手動干預的場景，採「觸發 / 行為 / 玩家可見 / 規範細節」完整展開。每條 EC 皆遵守 `design-docs.md`「explicitly state what happens」原則——不留「應優雅處理」這類空話。

### 5.1 EC-1：寫入過程中閃退 / 停電

- **觸發**：`ExecuteSave()` 執行到 `File.Replace` 之前進程被中斷（閃電 / 強制終結 / 系統當機）
- **行為**：磁碟上殘留 `save.tmp`（半完成或完整 JSON）；`save.json` 仍為前一次寫入的舊版本（`File.Replace` 為 atomic operation，未呼叫前 `save.json` 不變）；下次 Bootstrap 從 `save.json` 正常載入舊版本，最多丟失 `SAVE_AUTO_INTERVAL_SEC`（60 秒）的進度
- **`save.tmp` 清理**：FT-10 **不主動清理**殘留 `save.tmp`；下次 `ExecuteSave()` 的 `File.WriteAllText` 直接覆寫，無需特殊處理
- **規範源**：§3.4.1 原子寫入流程

### 5.2 EC-2：全部 backup 損毀

- **觸發**：`save.json` + `save.bak1..bak3` 全部不存在 / 解析失敗 / `schemaMeta` 為 null（極端磁碟損毀）
- **行為**：`ReadSaveFileWithFallback` 回傳 null，`loadedFromIndex = -1`；發布 `OnLoadFailed(ex)`；走首次遊玩流程（Phase E），各 owner 透過 `InitializeAsNewGame()` 預設初始化
- **玩家可見**：**無對話框**；正式 UI 不呈現「存檔損毀」訊息；玩家會察覺進度遺失但看不到技術錯誤——對齊 §2「不彈出『存檔損毀，是否載入備份？』的對話框」設計原則
- **規範源**：§3.4.2 損毀回退鏈 + §3.4.3 玩家可見性

### 5.3 EC-3：Critical Owner 還原失敗

- **觸發**：F-03 / C-02 / FT-06 / FT-08 / FT-12（critical 列表，§3.3.4）任一 `RestoreFromSave` 拋例外（如 ID 違反 F-01 驗證 / `CandidateCardValidationException`）
- **行為**：
  1. Phase C 內 try-catch 捕獲 → 拋出 `CriticalRestoreFailedException(ownerName, innerEx)`
  2. 由外層 catch 觸發回退至下一份 backup（`loadedFromIndex++`）
  3. 重新進入 Phase A 從新候選讀取
- **若全部 backup 都讓某個 critical owner 失敗**：等同 EC-2 全失敗，走首次遊玩
- **規範源**：§3.3.4 critical/degradable 分流 + §3.4.2 回退鏈觸發條件

### 5.4 EC-4：Degradable Owner 還原失敗

- **觸發**：C-06 / FT-01 / FT-02 / FT-03 / FT-07 / FT-09 任一 `RestoreFromSave` 拋例外或欄位缺失
- **行為**：log warning（`Debug.LogWarning("Degradable owner {ownerName} restore failed, treating as fresh: {ex}")`）→ 呼叫 `owner.InitializeAsNewGame()` 視為首次遊玩 → **不傳遞例外**至上層、繼續下一個 owner
- **不觸發整檔回退**：對齊 FT-09 EC-10「不阻擋 Bootstrap」設計動機——degradable owner 損壞不應導致整存檔不可用
- **規範源**：§3.3.4 critical/degradable 分流 + FT-09 §5.10 EC-10

### 5.5 EC-5：`persistentDataPath` 無寫入權限

- **觸發**：作業系統權限異常 / 防毒軟體鎖定 / 磁碟唯讀（極罕見，桌面 standalone 幾乎不會發生）
- **行為**：`File.WriteAllText` 拋 `UnauthorizedAccessException` / `IOException` → §3.4.1 catch 區塊 log error 並發布 `OnSaveFailed(ex)`；**不阻擋核心循環**——遊戲繼續以記憶體狀態運行，每次節流週期仍會嘗試寫入（log spam 在所難免）
- **不嘗試替代路徑**：FT-10 不嘗試降級到 `Application.dataPath` / 臨時資料夾；玩家若磁碟異常須自行排查
- **規範源**：§3.4.1 寫入流程 try-catch + §3.7.2 `OnSaveFailed`

### 5.6 EC-6：終末檔複製時 IOException

- **觸發**：`OnGameOver` 處理流程 §3.5.1 Step 2 `File.Copy(savePath, gameoverPath, overwrite: false)` 拋例外（如同秒重複觸發 `OnGameOver`、磁碟空間不足）
- **行為**：catch 後 log warning，**不重複封存**、**不阻擋**後續 Step 3（`OnGameSealed` 仍發布，payload 內 `gameoverFilePath` 為前一次成功路徑或當前嘗試路徑）；視為 idempotent
- **設計理由**：`OnGameOver` 在 FT-06 端為冪等事件來源（已進 Over 態後不再重複發），同秒重複幾乎不可能；本 EC 為防禦性處理而非常規路徑
- **規範源**：§3.5.1 Step 2 + §4.3 唯一性保證

### 5.7 EC-7：Game Over 後再次啟動

- **觸發**：玩家上次 session 觸發 `OnGameOver` 並完整封存（§3.5.1 三步驟），下次啟動遊戲
- **行為**：FT-10 走標準 Bootstrap（Phase A → E）→ FT-06 透過 `RestoreFromSave` 還原 `gameOverState = "Over"` → `OnLoadCompleted` 發布 → P-02 偵測 `IsGameOver()` 為 true → 啟動畫面呈現「回顧結算 / 開新公會」雙選項
- **離線計算交棒**：Phase D 仍呼叫 `F-02.Initialize(lastActiveTimestamp)`，但對齊 F-02 §3「`gameOverState = Over` 時不再執行任務結算」的既定行為——F-02 內部不發布 `OnMissionCompleted`、FT-04 不被觸發
- **規範源**：§3.3.2 Phase E + §3.5.1 主存檔保留 + F-02 §3.6 line 102

### 5.8 EC-8：`OnApplicationQuit` 與節流軌道同 frame 觸發

- **觸發**：玩家在節流週期到達的同 frame 點擊關閉視窗（極短時間窗）
- **行為**：先進入者（節流或 Quit）取得 `_isSaving = true` 鎖；後到者進入 `ExecuteSave()` 第一行 `if (_isSaving) return` 直接返回；前者完成後 `_isSaving` 清零，但此時遊戲已退出 frame，後者**不重試**（無重試邏輯，玩家進度仍由先到者寫入）
- **資料完整性**：先到者已寫入完整 snapshot，後到者放棄不影響資料正確性
- **規範源**：§3.2.4 寫入互斥保護

### 5.9 EC-9：系統時鐘倒退 / 跳躍

- **觸發**：玩家手動調整系統時間至過去 / 未來（時區切換 / 時鐘重設 / VM 快照還原）
- **行為**：FT-10 **不檢測**時鐘倒退；每次 `ExecuteSave()` 純取 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` 寫入 `lastActiveTimestamp`；clamp 與容錯責任歸 F-02
- **F-02 端處理**：`offlineSeconds = clamp(now - lastActiveTimestamp, 0, OFFLINE_MAX_SECONDS)`——若 `now < lastActiveTimestamp`（時鐘倒退），結果為負被 clamp 至 0，等同跳過離線模式
- **規範源**：§4.2 + F-02 §3 公式（FT-10 不重複 F-02 既有規範）

### 5.10 EC-10：Owner 子區塊欄位缺失（Schema 演化）

> §3 未明確規範。此 EC 補強「新增 owner 後讀舊存檔」的契約。

- **觸發**：玩家從未含某 owner 區塊的舊存檔升級（如未來 Post-Jam 新增 P-04 系統，舊玩家 `save.json` 無 `p04*` 區塊）

| 子情境 | 行為 |
|---|---|
| (a) Owner 區塊整個缺失 | FT-10 在 Phase C 偵測 `rootJson` 不含該 owner key → 傳入 `null` 給 `RestoreFromSave(null)` → owner 內部判斷 null 後呼叫 `InitializeAsNewGame()` 走預設路徑 |
| (b) Owner 區塊存在但內部欄位部分缺失 | 由 owner 自行處理（FT-10 不檢視內部 schema）；對齊 FT-09 §5.10 EC-10 模式：缺欄位視為預設值，不拋例外 |
| (c) Owner 區塊 JSON 損毀（無法解析） | `RestoreFromSave` 拋例外 → 進入 §3.3.4 critical/degradable 分流 |

- **玩家可見**：無對話框；缺失區塊的 owner 以預設值運行（如新增的成就系統舊玩家進度為 0）
- **規範細節**：
  - **不引入 `version: int` 欄位**（§1 既定 + 對齊 FT-09 EC-10）；schema 演化純依賴 owner 端「容忍缺失欄位」的編程紀律
  - Post-Jam 若需精確版本控制（migration script），由 FT-10 後續迭代加入；Jam 範疇不處理
- **設計理由**：對齊「資料驅動」全局原則——schema 變更頻繁時硬編 version 欄位收益低於 owner 自行容忍缺失欄位

### 5.11 EC-11：玩家手動刪除 `save.json` 但保留 backup

> §3 未明確規範。此 EC 補強「玩家檔案系統干預」的契約。

- **觸發**：玩家從 `persistentDataPath` 手動刪除 `save.json`（誤操作 / debug / 嘗試「軟重置」），`save.bak1..bak3` 仍存在
- **行為**：
  1. Bootstrap Phase A 進入 `ReadSaveFileWithFallback`
  2. 候選 0（`save.json`）`File.Exists` 為 false → 跳到候選 1（`save.bak1`）
  3. `save.bak1` 成功載入 → `loadedFromIndex = 1` → 正常進入 Phase B
- **玩家可見**：遊戲正常進入，但進度為 backup 版本（最多丟失 60 秒）；無錯誤訊息
- **`save.json` 重生**：下次節流寫入時透過 `File.Move(savePath + ".tmp", savePath)`（§3.4.1 「首次寫入無 backup 來源」分支）重建 `save.json`
- **規範源**：§3.4.1 寫入流程「首次寫入」分支 + §3.4.2 回退鏈
- **設計理由**：對齊 §2「Backup 是隱性 fail-safe」——玩家手動干預不視為惡意行為，FT-10 透明地以 backup 接手；若玩家想「徹底重置」應使用 `ResetToNewGame()`（透過遊戲內介面）而非手動刪檔

### 5.12 EC-12：終末檔被玩家手動刪除

> §3 未明確規範。此 EC 補強「公會編年史」承諾的容錯。

- **觸發**：玩家從 `persistentDataPath` 手動刪除 `save_gameover_<ts>.json`（出於磁碟整理 / 隱私 / 誤操作）
- **行為**：
  1. FT-10 **不偵測**終末檔遺失，不嘗試重建（已封存的 game state 無法復原）
  2. 不影響任何核心循環：終末檔不參與 Bootstrap 回退鏈（§3.4.2 既定）
  3. P-02「歷史檢視」介面對該條目的呈現策略：建議列舉時 `File.Exists` 預檢，缺失條目**不顯示**或顯示「已刪除」標記（呈現策略由 P-02 決定，FT-10 不規範）
- **玩家可見**：「公會編年史」中該筆紀錄消失；其他終末檔不受影響
- **規範細節**：
  - FT-10 不維護終末檔索引清單（每次需要時 `Directory.GetFiles(persistentDataPath, "save_gameover_*.json")` 動態列舉）
  - 不對玩家彈出警告（玩家手動刪除即明確意圖，違反「無感存檔」設計原則時不適合彈窗）
- **設計理由**：終末檔屬玩家可自由管理的歷史資產（對齊 §2「玩家個人遊戲史的層積」），不採用唯讀屬性 / 隱藏路徑等強制手段；公會編年史承諾的本質是「FT-10 不主動刪除」，而非「FT-10 強制保留」

### 5.13 EC 索引

| EC | 主題 | 規範來源 | 處理格式 |
|---|---|---|---|
| EC-1 | 寫入閃退 | §3.4.1 內嵌 | 短條目 |
| EC-2 | 全 backup 損毀 | §3.4.2 / §3.4.3 內嵌 | 短條目 |
| EC-3 | Critical owner 失敗 | §3.3.4 內嵌 | 短條目 |
| EC-4 | Degradable owner 失敗 | §3.3.4 / FT-09 EC-10 內嵌 | 短條目 |
| EC-5 | 寫入無權限 | §3.4.1 內嵌 | 短條目 |
| EC-6 | 終末檔複製 IOException | §3.5.1 內嵌 | 短條目 |
| EC-7 | Game Over 後重啟 | §3.5.1 / F-02 §3.6 內嵌 | 短條目 |
| EC-8 | Quit 與節流同 frame | §3.2.4 內嵌 | 短條目 |
| EC-9 | 時鐘倒退 | §4.2 / F-02 內嵌 | 短條目 |
| EC-10 | Schema 演化 | §3 未涵蓋 | 完整展開 |
| EC-11 | 手動刪 save.json | §3 未明確 | 完整展開 |
| EC-12 | 手動刪終末檔 | §3 未明確 | 完整展開 |



## 6. 依賴關係（Dependencies）

> 本章採四子節結構：§6.1 上游依賴（FT-10 消費的 API / 資料）→ §6.2 下游被依賴（消費 FT-10 的系統）→ §6.3 事件契約矩陣 → §6.4 反向依賴清單（待更新的他系統 GDD）。對齊 `design-docs.md`「Dependencies must be bidirectional」原則：本章列出的每條跨系統契約皆有對應的反向登記要求。

### 6.1 上游依賴（FT-10 消費的）

FT-10 為 ALL-依賴系統，但**消費層級僅限 owner 系統暴露的 `ISaveable` 介面**——FT-10 不直接讀取各 owner 的內部欄位，僅透過 `Serialize()` / `RestoreFromSave(json)` / `InitializeAsNewGame()` 三個方法溝通。

| # | 上游系統 | 消費 API / 資料 | 用途 | 規範源 |
|---|---|---|---|---|
| 1 | F-01 DataManager | `Get<T>(int id)` / `GetAll<T>()` | Bootstrap Phase C 期間，各 owner 在自身 `RestoreFromSave` 內呼叫驗證 ID 合法性；FT-10 本身**不直接呼叫**，但前置條件依賴 F-01 已完成 CSV 載入 | F-01 §6 line 185 |
| 2 | F-02 Time System | `Initialize(long lastActiveTimestamp)` | Phase D 離線計算交棒；FT-10 為**唯一呼叫者** | F-02 §3 line 24 / §6 line 210 |
| 3 | F-03 Resource Mgmt | `ISaveable` 實作（`Serialize` / `RestoreFromSave` / `InitializeAsNewGame`） | 序列化 `currentGold` / `currentReputation` / `_warningState` / `_bankruptcyWarningStartTime` / `_warningDurationSec` / `_currentBankruptcyThreshold` | §1 設計來源 + F-03 §6.2 |
| 4 | C-02 Adventurer Mgmt | `ISaveable` 實作 | 序列化 `AdventurerInstance[]` 完整名冊（含 FT-03 idle 時間戳） | §1 設計來源 + C-02 §3.1 / §6 |
| 5 | C-06 World Danger | `ISaveable` 實作 | 序列化 `currentDangerLevel` / `gameStartTimestamp` | §1 設計來源 + C-06 §3 |
| 6 | FT-01 Recruitment | `ISaveable` 實作 | 序列化候選池 / 刷新時間戳 / 免費刷新次數 | §1 設計來源 + FT-01 §5.4 / §6 |
| 7 | FT-02 Mission Dispatch | `ISaveable` 實作 | 序列化 `activeMissions[]` / `_nextActiveMissionID`；還原後 FT-02 自行重新訂閱 F-02 `OnSecondTick` 觸發 `TickCompletionCheck`，不需呼叫任何 F-02 計時器 API | §1 設計來源 + FT-02 §5.5 / §6 |
| 8 | FT-03 NPC Decision | `ISaveable` 實作（薄層；實際欄位由 C-02 一併處理） | `idleSinceTimestamp` / `lastAutoPickupTimestamp` 已內嵌於 `AdventurerInstance`；FT-03 僅做訂閱重建 | §1 設計來源 + FT-03 §6 |
| 9 | FT-06 Guild Core | `ISaveable` 實作 + `IsGameOver()` 查詢 + `OnGameOver` 事件 | 序列化 `GuildState` 全量；訂閱 `OnGameOver` 觸發 §3.5.1 封存流程 | §1 設計來源 + FT-06 §3.1 / §3.2 / §3.5 |
| 10 | FT-07 Guild Building | `ISaveable` 實作 | 序列化 `BuildingState[]`（6 棟 `currentLevel`） | §1 設計來源 + FT-07 §3.1 / §6 |
| 11 | FT-08 Gacha | `ISaveable` 實作 + `CandidateCardValidationException` 契約 | 序列化 `StaffPlayerState` + `CandidateCard[]`；驗證違規時 FT-08 fail-fast 拋例外，被 FT-10 §3.3.4 critical 路徑捕獲 | §1 設計來源 + FT-08 §3.1 / §3.2.3 / §6 |
| 12 | FT-09 Faction Story | `ISaveable` 實作 | 序列化 `FactionStorySaveData` 區塊；degradable 路徑 | §1 設計來源 + FT-09 §3.7 / §5.10 |

**訂閱事件清單**（§3.2.1 已列）：訂閱關係列入此處不重複展開，請見 §3.2.1 事件來源表。

**不消費的系統**（明示）：

| 系統 | 理由 |
|---|---|
| C-01 Mission Database | 純資料表查詢系統，無實例狀態；FT-02 持有的 `activeMissions` 內含 `missionID` 由 FT-02 自行驗證合法性 |
| C-03 / C-04 / C-05 | 純資料表，無實例狀態 |
| FT-04 Outcome Resolution | 純函式系統，無持久狀態（FT-04 §6 既定） |
| FT-05 Guild Gold Flow | 純流程系統；Jam 版不持久化結算歷史（§1 既定） |

### 6.2 下游被依賴（消費 FT-10 的）

| # | 下游系統 | 消費介面 | 用途 | 對應反向依賴 |
|---|---|---|---|---|
| 1 | P-02 Main UI Framework | `HasSaveFile()` / `IsGameOver()` / `GetLoadedFromBackupIndex()` 查詢 | 啟動畫面分支判斷（首次遊玩 / 繼續遊戲 / Game Over 雙選項）；§3.4.3 規範正式 UI 不顯示 backup 索引，但 debug overlay 可呈現 | P-02 GDD（**待設計**） |
| 2 | P-02 Main UI Framework | `ResetToNewGame()` 呼叫 + `OnGameReset` 訂閱 | 玩家點「開新公會」按鈕 → P-02 呼叫 `ResetToNewGame()` → 訂閱 `OnGameReset` 執行 scene reload（§3.5.2） | P-02 GDD（**待設計**） |
| 3 | P-02 Main UI Framework | `OnGameSealed` 訂閱 | Game Over 後封存完成回呼，P-02 可選擇呈現「公會封存」UI | P-02 GDD（**待設計**） |
| 4 | P-02 Main UI Framework | `OnLoadCompleted` / `OnLoadFailed` 訂閱 | Bootstrap 完成回呼，決定主畫面進入時機 | P-02 GDD（**待設計**） |
| 5 | P-03 Notification System | `OnSaveCompleted` 訂閱（可選） **【→Log API待更新】** | 桌面通知「進度已儲存」（Jam 版可隱藏，§2 設計原則「玩家不該意識到存檔」） | P-03 GDD（**待設計**） |
| 6 | 各 owner 系統（共 10 個） | `MarkDirty()` 呼叫（特殊事件用） | 一般情況走事件訂閱自動標記；特殊變動可主動呼叫 | 各 owner GDD §6 反向登記 |
| 7 | F-02 Time System | 接受 FT-10 呼叫的 `Initialize(lastActiveTimestamp)` | 離線計算交棒；F-02 §6 已登記 FT-10 為呼叫方 | F-02 §6 line 210（已登記） |
| 8 | FT-04 Outcome Resolution | 透過 F-02 `OnOfflineResolved` 接手結算（FT-10 不直接呼叫 FT-04） | 離線期間到期任務的結算由 F-02 觸發、FT-04 執行 | FT-04 §6 line 274（已登記 F-02 為觸發源） |

### 6.3 事件契約矩陣

| # | 方向 | 事件 / API | FT-10 角色 | 對方 | 規範源 |
|---|---|---|---|---|---|
| 1 | 入 | `OnGuildInitialized` | 訂閱者 | FT-06 發布 | §3.2.1 |
| 2 | 入 | `OnGuildLevelChanged` | 訂閱者 | FT-06 發布 | §3.2.1 |
| 3 | 入 | `OnMissionResolved` | 訂閱者 | FT-04 發布 | §3.2.1 |
| 4 | 入 | `OnCommissionSettled` | 訂閱者 | FT-05 發布 | §3.2.1 |
| 5 | 入 | `OnRecruitSuccess` | 訂閱者 | FT-01 發布 | §3.2.1 |
| 6 | 入 | `OnBuildingUpgraded` | 訂閱者 | FT-07 發布 | §3.2.1 |
| 7 | 入 | `OnStaffHired` / `OnStaffSalaryDue` | 訂閱者 | FT-12 發布 | §3.2.1 |
| 8 | 入 | `OnFactionStoryStageUnlocked` | 訂閱者 | FT-09 發布 | §3.2.1 |
| 9 | 入 | `OnDangerLevelChanged` | 訂閱者 | C-06 發布 | §3.2.1 |
| 10 | 入 | `OnGoldChanged` / `OnReputationChanged` | 訂閱者 | F-03 發布 | §3.2.1 |
| 11 | 入 | `OnGameOver` | 訂閱者（觸發 §3.5.1 封存流程） | FT-06 發布 | §3.5.1 |
| 12 | 出 | `OnSaveCompleted` | 發布者 | Debug / P-03 訂閱（可選） | §3.7.2 |
| 13 | 出 | `OnSaveFailed(Exception)` | 發布者 | Debug；正式 UI **不顯示** | §3.7.2 + §3.4.3 |
| 14 | 出 | `OnLoadCompleted(int loadedFromBackupIndex)` | 發布者 | P-02 訂閱 | §3.7.2 |
| 15 | 出 | `OnLoadFailed(Exception)` | 發布者 | P-02 訂閱（走首次遊玩） | §3.7.2 |
| 16 | 出 | `OnGameSealed(string filePath, long timestamp)` | 發布者 | P-02 / P-03 訂閱 **【→Log API待更新】** | §3.5.1 / §3.7.2 |
| 17 | 出 | `OnGameReset` | 發布者 | P-02 訂閱（執行 scene reload） | §3.5.2 / §3.7.2 |
| 18 | 同步 API | `Initialize(long lastActiveTimestamp)` | 呼叫者 | F-02 接收 | §3.3.2 Phase D |
| 19 | 同步 API | 各 owner 的 `Serialize()` / `RestoreFromSave(json)` / `InitializeAsNewGame()` | 呼叫者 | 各 owner 接收 | §3.6.2 ISaveable 介面 |
| 20 | 同步 API | `MarkDirty()` / `ForceSave()` / `HasSaveFile()` / `IsGameOver()` / `ResetToNewGame()` | 提供者 | P-02 / 各 owner 呼叫 | §3.7.1 |

### 6.4 反向依賴清單（待更新的他系統 GDD）

> 本清單對齊 `design-docs.md`「Dependencies must be bidirectional」原則；FT-10 GDD 通過 `/design-review` 後，須對下列 GDD 執行對應的雙向登記更新。**使用者已決定**：FT-10 整體通過 review 後再一次處理（對齊 FT-05 / FT-09 同類延後處理慣例，見記憶 `project_ft05_reverse_deps.md`）。

| # | 目標 GDD | 更新內容 | 章節落點 |
|---|---|---|---|
| 1 | `[F-01] data-manager.md` | §6 反向依賴登記：FT-10 為間接消費者（透過各 owner `RestoreFromSave` 內部呼叫 `Get<T>` / `GetAll<T>` 驗證 ID 合法性） | F-01 §6 反向依賴表 |
| 2 | `[F-02] time-system.md` | 確認 §6 line 210 已登記 FT-10 為 `Initialize(lastActiveTimestamp)` 唯一呼叫者；若描述不夠明確則補強 | F-02 §6（已存在，校驗即可） |
| 3 | `[F-03] resource-management.md` | 新增 §6 反向依賴：FT-10 透過 `ISaveable` 還原 6 個欄位；補 `ISaveable` 實作要求至 §3 / §6 | F-03 §3 / §6 |
| 4 | `[C-02] adventurer-management.md` | 新增 §6 反向依賴：FT-10 透過 `ISaveable` 序列化 `AdventurerInstance[]`；補 `ISaveable` 實作要求 | C-02 §3 / §6 |
| 5 | `[C-06] world-danger-system.md` | 新增 §6 反向依賴：FT-10 透過 `ISaveable` 還原 `currentDangerLevel` / `gameStartTimestamp`；明示 Awake 設預設、FT-10 在 Start 之前還原的時序 | C-06 §3 / §6 |
| 6 | `[FT-01] adventurer-recruitment.md` | 新增 §6 反向依賴：FT-10 透過 `ISaveable` 序列化候選池與刷新狀態；補 `ISaveable` 實作要求 | FT-01 §5.4 / §6 |
| 7 | `[FT-02] mission-dispatch.md` | 新增 §6 反向依賴：FT-10 還原後 FT-02 自行重新訂閱 F-02 `OnSecondTick`，`activeMissions` 還原即可立即被 `TickCompletionCheck` 驅動；補 `ISaveable` 實作要求 | FT-02 §5.5 / §6 |
| 8 | `[FT-03] npc-decision-system.md` | 新增 §6 反向依賴：FT-10 透過 C-02 `AdventurerInstance` 一併序列化 idle 時間戳；FT-03 自身的 `RestoreFromSave` 為薄層（僅做訂閱重建） | FT-03 §6 |
| 9 | `[FT-04] outcome-resolution.md` | 在 §6 / §1 既定範圍補一行「**無持久狀態，不參與序列化**」的明示宣告（已在 FT-10 §1 設計來源引用 FT-04 §6） | FT-04 §6 |
| 10 | `[FT-05] guild-gold-flow.md` | 在 §6 補一行「Jam 版不持久化結算歷史；FT-10 §1 既定範疇外」的明示宣告（Post-Jam 擴充收支日誌時再修訂） | FT-05 §6 |
| 11 | `[FT-06] guild-core.md` | 新增 §6 反向依賴：FT-10 訂閱 `OnGuildInitialized` / `OnGuildLevelChanged` / `OnGameOver`；透過 `ISaveable` 序列化 `GuildState`；提供 `IsGameOver()` 查詢給 FT-10 / P-02；明示 `pendingLevelUpQueue` runtime-only 不序列化 | FT-06 §3.1 / §3.2 / §3.5 / §6 |
| 12 | `[FT-07] guild-building-system.md` | 新增 §6 反向依賴：FT-10 透過 `ISaveable` 序列化 `BuildingState[]`；補 `ISaveable` 實作要求 | FT-07 §3.1 / §6 |
| 13 | `[FT-08] gacha-system.md` / `[FT-12] staff-system.md` | §6.7 反向依賴：FT-10 透過 `ISaveable` 各自序列化 (FT-08: StaffPlayerState+CandidateCard / FT-12: StaffInstance+_nextInstanceID+_lastSalaryTimestamp)；明示 `CandidateCardValidationException` 為 FT-10 critical 路徑捕獲源 | FT-08 §3.1 / §3.2.3 / §6 |
| 14 | `[FT-09] faction-story-system.md` | §6 line 1551 / §5.10 EC-10 已對齊 FT-10 設計；補一行明示「FT-10 §3.3.4 將 FT-09 列為 degradable，反序列化異常不阻擋 Bootstrap」雙向確認 | FT-09 §6（已存在，校驗即可） |
| 15 | `production/systems-index.md` line 50 / line 312 | 將 FT-10 GDD 狀態從「待設計」改為「已設計」；line 312 進度追蹤表將 GDD 欄位由 ⬜ 改為 ✅ | systems-index.md |

**處理時機**：對齊使用者既定慣例（記憶 `project_ft05_reverse_deps.md`）——本 GDD 完成 §1 ~ §8 全部撰寫並通過 `/design-review FT-10` 後，使用者統一安排一輪批次更新；本清單作為待辦索引，不在 FT-10 撰寫期間穿插處理。

**處理範圍邊界**：

- **不修改** `design/data-specs/` 下任何 CSV 規格書（FT-10 不引入新 CSV，§1 既定）
- **不修改** P-01 / P-02 / P-03 / D-01 / D-02 GDD（這些 GDD 尚未撰寫；P-02 撰寫時自然會引用 FT-10，無需提前登記）
- **不修改** F-01 已完成的 CSV schema（FT-10 不消費新欄位）



## 7. 可調參數（Tuning Knobs）

> 本章列出 FT-10 全部可調參數、預設值、安全範圍、影響說明。對齊 `design-docs.md`「Tuning knobs must specify safe ranges and what gameplay aspect they affect」要求；對齊 `feedback_data_driven`「一切由 CSV 驅動，零硬編碼」全局規則。

### 7.1 存放位置

| 表格 | 用途 | 理由 |
|---|---|---|
| `SystemConstants.csv` | 收錄 FT-10 全部可調參數（共 5 項） | FT-10 參數量少、無子系統規模；不另開獨立表（對比 FT-08 / FT-12 因子系統爆量另開 `StaffTuning.csv`） |

**不引入新表**：FT-10 §1 既定範疇外，不新增任何 CSV 表（無 `FactionRouteTable` 類比的內容資料表）。

### 7.2 參數清單

| key | 預設值 | 型別 | 安全範圍 | 影響的遊戲面向 |
|---|---|---|---|---|
| `SAVE_AUTO_INTERVAL_SEC` | `60` | int（秒） | `[10, 600]` | **節流寫入間隔**。下調 → 更即時、閃退損失下限更小，但 I/O 頻率上升、SSD 寫入壽命影響略增。上調 → I/O 省、但閃退最多丟失此秒數的進度。`< 10` 退化為「幾乎每事件即寫」，違反 §3.2 三軌策略設計動機；`> 600`（10 分鐘）違反 §2「最多丟掉一分鐘的進度」承諾 |
| `SAVE_BACKUP_COUNT` | `3` | int（份數） | `[1, 10]` | **Backup rotation 代數**。上調 → fail-safe 層數更深、容錯範圍更大，但每次寫入的 rotation 步驟增加、磁碟佔用線性上升。下調至 `1` 仍維持「主存檔損毀有 backup 接手」最低保證；`0` 不允許（違反 fail-safe 承諾，§3.4.1 已硬性禁止） |
| `SAVE_FILE_NAME` | `save.json` | string | — | **主存檔檔名**。理論可調，但**不建議修改**：玩家舊存檔遷移成本（需手動改名）+ 跨版本相容性（Post-Jam 雲端同步需穩定檔名）。預期此參數整個專案生命週期不變動 |
| `SAVE_BAK_PREFIX` | `save.bak` | string | — | **Backup 檔名前綴**。實際檔名為 `{prefix}{i}` for i ∈ [1, N]（如 `save.bak1`）。**不建議修改**：理由同上 |
| `SAVE_GAMEOVER_PREFIX` | `save_gameover_` | string | — | **終末檔檔名前綴**。實際檔名為 `{prefix}{unixTimestamp}.json`（如 `save_gameover_1745677938.json`）。**不建議修改**：跨版本玩家「公會編年史」連續性依賴穩定前綴；P-02 歷史檢視介面以此 prefix 為 glob pattern 列舉 |

### 7.3 平衡建議

#### `SAVE_AUTO_INTERVAL_SEC` 的選值取捨

| 預設值 | 適用情境 | 取捨 |
|---|---|---|
| `60`（建議） | 桌面 standalone Jam 範疇 | 平衡點：閃退損失上限 1 分鐘（§2 承諾），I/O 頻率合理 |
| `30` | 開發 / debug / 高頻變動測試 | 更即時看到效果；I/O 風暴風險可接受（debug 不在意磁碟壽命） |
| `120` | 移植到行動裝置（Post-Jam） | 行動裝置 I/O 較貴；但需與 §2 承諾重新協商（Game Jam 不適用） |
| `300+` | 不建議 | 已超出 §2「丟掉一分鐘」承諾範圍 |

#### `SAVE_BACKUP_COUNT` 的選值取捨

| 預設值 | 磁碟成本（單檔 ≤ 1MB） | Fail-safe 深度 |
|---|---|---|
| `1` | 2MB（save + bak1） | 主存檔損毀有 1 層保險 |
| `3`（建議） | 4MB | 連續 3 次寫入損毀仍有 fail-safe；§4.5 最多丟失 180 秒 |
| `5` | 6MB | 過度防禦；極端情境（如同時 5 次寫入損毀）幾乎不可能發生 |
| `10` | 11MB | 不建議；rotation 成本邊際遞減、磁碟空間累積但無實質保護增益 |

### 7.4 不可調參數（內部硬編，明示）

對齊 `feedback_gates_data_driven` 全局原則「閘 / 閾值必須 CSV 驅動」，但下列項目屬**結構性常數**而非平衡 / 閘門類參數，不放入 CSV：

| 項目 | 值 | 理由 |
|---|---|---|
| Script Execution Order：F-01 = -200、FT-10 = -100 | 固定 | Unity Project Settings 配置，非 runtime 可調；運行時改變會破壞 Bootstrap 順序 |
| Owner 還原拓撲順序（§3.3.3） | 硬編列表 | 依拓撲依賴推導而非經驗值；新增 owner 時須同步更新列表並通過 review |
| Critical / Degradable owner 分類 | 硬編列表（§3.3.4） | 由系統重要性 / 可降級性語意決定，非運行時調整對象；新分類須通過 design review |
| `File.Replace` / `File.Move` 等 I/O API | Windows 平台原生 | §1 既定 Jam 範疇 Windows-only；Post-Jam 跨平台屬架構變更非調參 |
| `JsonUtility` 序列化器 | Unity 內建 | §1 既定零依賴選擇；換序列化器屬架構變更非調參 |

### 7.5 配置存取規範

```csharp
// FT-10 啟動時讀取（Awake 內，於 SubscribeAllEvents 之前）
private int _saveAutoIntervalSec;
private int _saveBackupCount;
private string _saveFileName;
private string _saveBakPrefix;
private string _saveGameoverPrefix;

void InitTuning() {
    _saveAutoIntervalSec   = DataManager.GetSystemConstant<int>("SAVE_AUTO_INTERVAL_SEC", 60);
    _saveBackupCount       = DataManager.GetSystemConstant<int>("SAVE_BACKUP_COUNT", 3);
    _saveFileName          = DataManager.GetSystemConstant<string>("SAVE_FILE_NAME", "save.json");
    _saveBakPrefix         = DataManager.GetSystemConstant<string>("SAVE_BAK_PREFIX", "save.bak");
    _saveGameoverPrefix    = DataManager.GetSystemConstant<string>("SAVE_GAMEOVER_PREFIX", "save_gameover_");

    // 驗證安全範圍（落在 [min, max] 外時 log warning + clamp）
    _saveAutoIntervalSec = Mathf.Clamp(_saveAutoIntervalSec, 10, 600);
    _saveBackupCount     = Mathf.Clamp(_saveBackupCount, 1, 10);
}
```

**設計理由**：

- 預設值由 `DataManager.GetSystemConstant` 第二參數提供，CSV 缺漏時 fail-safe 至 §7.2 預設值
- 安全範圍 clamp 由 FT-10 自身執行而非 F-01 強制；F-01 為通用查詢層、不擁有業務語意（對齊 F-01 §6 既定職責邊界）



## 8. 驗收標準（Acceptance Criteria）

> 本章採八子節結構，對齊 §3 / §5 / §7 章節順序。每條 AC 採「**情境 / 操作 / 預期 / 驗證方式**」四欄格式，遵守 `design-docs.md`「testable — QA tester 能驗證 pass/fail」原則；可直接轉化為 Codex 工項書驗收條件 / Unity Test Framework EditMode 或 PlayMode 測試案例。

**驗證方式縮寫**：
- **Console** = `Debug.Log` / `LogWarning` / `LogError` 觀察
- **磁碟檢視** = `persistentDataPath` 下檔案存在性 / 內容比對
- **EventBus log** = 事件發布順序 / payload 觀察
- **API 斷言** = 程式碼斷言 FT-10 / owner 公開 API 回傳值
- **Runtime 狀態斷言** = 私有欄位透過 reflection / 測試專用 internal API 觀察

### 8.1 三軌寫入策略

#### AC-1.1：事件標記不立即寫入

| 欄位 | 內容 |
|---|---|
| **情境** | FT-10 訂閱中、節流週期未到 |
| **操作** | 觸發 `OnGoldChanged` 事件 1 次 |
| **預期** | (a) `_isDirty == true`；(b) 磁碟 `save.json` 修改時間**未變動**；(c) `OnSaveCompleted` 未發布 |
| **驗證方式** | Runtime 狀態斷言 + 磁碟檢視 + EventBus log |

#### AC-1.2：節流週期到達且 dirty 時寫入

| 欄位 | 內容 |
|---|---|
| **情境** | `SAVE_AUTO_INTERVAL_SEC = 60`，`_isDirty = true`，距離上次寫入 60 秒 |
| **操作** | 等待節流週期觸發（或測試環境直接撥 unscaledTime） |
| **預期** | (a) `ExecuteSave` 執行；(b) `save.json` 修改時間更新；(c) `_isDirty = false`；(d) `OnSaveCompleted` 發布 1 次 |
| **驗證方式** | 磁碟檢視 + Runtime 狀態斷言 + EventBus log |

#### AC-1.3：節流週期到達但無變動時不寫入

| 欄位 | 內容 |
|---|---|
| **情境** | `_isDirty = false`，距離上次寫入 60 秒 |
| **操作** | 等待節流週期觸發 |
| **預期** | (a) 不執行 `ExecuteSave`；(b) `save.json` 修改時間不變；(c) `_lastSaveTime` 更新避免每 frame 重複比對 |
| **驗證方式** | 磁碟檢視 + Runtime 狀態斷言 |

#### AC-1.4：OnApplicationQuit 強制寫入

| 欄位 | 內容 |
|---|---|
| **情境** | `_isDirty = false`（剛剛才寫過），玩家關閉視窗 |
| **操作** | 觸發 `OnApplicationQuit` Unity lifecycle |
| **預期** | (a) `ExecuteSave` 執行（**忽略 dirty flag**）；(b) `save.json` 修改時間更新；(c) `lastActiveTimestamp` 為當下 UTC 時間 |
| **驗證方式** | 磁碟檢視（讀回 schemaMeta） + Runtime 狀態斷言 |

#### AC-1.5：OnGameOver 強制寫入觸發封存

| 欄位 | 內容 |
|---|---|
| **情境** | 遊戲進行中觸發 Game Over（如金幣低於破產閾值持續超時） |
| **操作** | FT-06 發布 `OnGameOver` 事件 |
| **預期** | (a) FT-10 立即執行 `ExecuteSave`；(b) `schemaMeta.gameOverState == "Over"`；(c) 後續 §8.4 封存流程被觸發 |
| **驗證方式** | 磁碟檢視 + EventBus log |

#### AC-1.6：寫入互斥保護

| 欄位 | 內容 |
|---|---|
| **情境** | 節流寫入執行中（耗時 > 0），`OnApplicationQuit` 同 frame 觸發 |
| **操作** | 同 frame 兩條軌道呼叫 `ExecuteSave()` |
| **預期** | 後到者進入 `if (_isSaving) return` 直接返回；不出現並行寫入；磁碟資料完整 |
| **驗證方式** | Console（觀察是否有 `_isSaving` 重入 log） + 磁碟內容驗證 |

### 8.2 Bootstrap 與還原

#### AC-2.1：Script Execution Order 正確

| 欄位 | 內容 |
|---|---|
| **情境** | 遊戲啟動 |
| **操作** | 進入 Bootstrap scene |
| **預期** | F-01 Awake 完成 → FT-10 Awake 開始 → 其他 owner Awake；FT-10 Start 在所有 owner Awake 之後 |
| **驗證方式** | 各系統 Awake / Start 內 `Debug.Log` 觀察順序 |

#### AC-2.2：Bootstrap 五階段順序

| 欄位 | 內容 |
|---|---|
| **情境** | 存在合法 `save.json` |
| **操作** | 啟動遊戲 |
| **預期** | Phase A 讀檔 → Phase B 解析 schemaMeta → Phase C per-owner 還原（依 §3.3.3 拓撲順序）→ Phase D 呼叫 `F-02.Initialize` → Phase E 發布 `OnLoadCompleted` |
| **驗證方式** | Phase 邊界 Debug.Log + EventBus log |

#### AC-2.3：Owner 還原拓撲順序

| 欄位 | 內容 |
|---|---|
| **情境** | 存在合法 `save.json` 含全部 10 個 owner 子區塊 |
| **操作** | Phase C 執行 |
| **預期** | 還原順序為 F-03 → C-06 → C-02 → FT-06 → FT-07 → FT-02 → FT-08 → FT-12 → FT-01 → FT-03 → FT-09 |
| **驗證方式** | 各 owner `RestoreFromSave` 開頭 Debug.Log 觀察順序 |

#### AC-2.4：Critical owner 失敗觸發回退

| 欄位 | 內容 |
|---|---|
| **情境** | `save.json` 中 F-03 子區塊損毀（如 `currentGold` 為非數值字串）；`save.bak1` 完整 |
| **操作** | 啟動遊戲 |
| **預期** | (a) F-03 `RestoreFromSave` 拋例外；(b) FT-10 退至 `save.bak1` 重新進入 Phase A；(c) 從 bak1 成功載入；(d) `loadedFromBackupIndex == 1` |
| **驗證方式** | Console（`CriticalRestoreFailedException` log） + API 斷言 `GetLoadedFromBackupIndex() == 1` |

#### AC-2.5：Degradable owner 失敗不阻擋 Bootstrap

| 欄位 | 內容 |
|---|---|
| **情境** | `save.json` 中 FT-09 `factionStorySaveData` 區塊損毀；其他區塊完整 |
| **操作** | 啟動遊戲 |
| **預期** | (a) FT-09 `RestoreFromSave` 拋例外；(b) FT-10 log warning + 呼叫 `FT-09.InitializeAsNewGame()`；(c) 其他 owner 正常還原；(d) `OnLoadCompleted(loadedFromBackupIndex == 0)` 發布；(e) FT-09 系統運作但分數為 0 |
| **驗證方式** | Console + EventBus log + FT-09 API 斷言（`GetFactionScore == 0`） |

#### AC-2.6：F-02 離線計算交棒

| 欄位 | 內容 |
|---|---|
| **情境** | `save.json` 中 `lastActiveTimestamp = T`，當前時間 `T + 3600`（1 小時離線） |
| **操作** | Bootstrap 完成 |
| **預期** | (a) Phase D 呼叫 `F-02.Initialize(T)`；(b) F-02 計算 `offlineSeconds = 3600`；(c) F-02 發布 `OnOfflineResolved(3600, completedCount)`；(d) FT-04 接手結算 |
| **驗證方式** | EventBus log（觀察 `OnOfflineResolved` payload） |

#### AC-2.7：首次遊玩流程（無存檔）

| 欄位 | 內容 |
|---|---|
| **情境** | `persistentDataPath` 為空（無 save.json / 無 backup） |
| **操作** | 啟動遊戲 |
| **預期** | (a) `HasSaveFile() == false`；(b) Phase A 跳到 Phase E；(c) 各 owner 走 `InitializeAsNewGame()` 預設初始化；(d) `OnLoadCompleted(loadedFromBackupIndex == -1)` 發布 |
| **驗證方式** | API 斷言 + EventBus log |

### 8.3 Backup Rotation 與損毀回退

#### AC-3.1：原子寫入（`File.Replace` 行為）

| 欄位 | 內容 |
|---|---|
| **情境** | `save.json` 已存在（gen-1） |
| **操作** | 觸發 `ExecuteSave`（產生 gen-2） |
| **預期** | (a) 中介過程曾出現 `save.tmp`；(b) 完成後 `save.json` 內容為 gen-2；(c) `save.bak1` 內容為 gen-1；(d) `save.tmp` 被 `File.Replace` 消費（不殘留） |
| **驗證方式** | 磁碟檢視 + 寫入過程截圖（debug 模式可加 sleep 觀察 tmp） |

#### AC-3.2：Rotation 多代正確性

| 欄位 | 內容 |
|---|---|
| **情境** | 連續執行 5 次 `ExecuteSave`（gen-1 ~ gen-5），`SAVE_BACKUP_COUNT = 3` |
| **操作** | 第 5 次完成後檢查磁碟 |
| **預期** | save.json = gen-5；bak1 = gen-4；bak2 = gen-3；bak3 = gen-2；gen-1 已被覆寫消失 |
| **驗證方式** | 磁碟檢視 + 內容比對 |

#### AC-3.3：損毀回退鏈順序

| 欄位 | 內容 |
|---|---|
| **情境** | save.json 不存在；bak1 損毀；bak2 完整；bak3 完整 |
| **操作** | 啟動遊戲 |
| **預期** | (a) 候選 0 跳過（不存在）；(b) 候選 1（bak1）解析失敗 log warning 跳到候選 2；(c) 候選 2（bak2）成功載入；(d) `loadedFromBackupIndex == 2` |
| **驗證方式** | Console（觀察 fallback log 順序） + API 斷言 |

#### AC-3.4：全部失敗走首次遊玩

| 欄位 | 內容 |
|---|---|
| **情境** | save.json + bak1 + bak2 + bak3 全部損毀（JSON 解析失敗） |
| **操作** | 啟動遊戲 |
| **預期** | (a) `OnLoadFailed(ex)` 發布；(b) 走 Phase E 首次遊玩；(c) `loadedFromBackupIndex == -1` |
| **驗證方式** | EventBus log + API 斷言 |

#### AC-3.5：玩家不見對話框

| 欄位 | 內容 |
|---|---|
| **情境** | AC-3.3 / AC-3.4 任一發生 |
| **操作** | 啟動遊戲到主畫面 |
| **預期** | 玩家未看到任何「存檔損毀」/「載入備份」對話框；遊戲直接進入主畫面（含首次遊玩或 backup 還原） |
| **驗證方式** | 人工觀察 / 自動化截圖比對（無 modal dialog） |

### 8.4 Game Over 封存

#### AC-4.1：終末檔生成

| 欄位 | 內容 |
|---|---|
| **情境** | 遊戲進行中觸發 Game Over，UTC 時間戳 `1745677938` |
| **操作** | FT-06 發布 `OnGameOver` |
| **預期** | (a) `save_gameover_1745677938.json` 檔案生成於 persistentDataPath；(b) 內容與當下 `save.json` 完全一致；(c) `OnGameSealed(filePath, 1745677938)` 發布 |
| **驗證方式** | 磁碟檢視（檔名 + 內容比對 byte-by-byte） + EventBus log |

#### AC-4.2：主存檔保留 Over 旗標

| 欄位 | 內容 |
|---|---|
| **情境** | AC-4.1 完成後 |
| **操作** | 檢查 `save.json` |
| **預期** | (a) `save.json` 仍存在；(b) `schemaMeta.gameOverState == "Over"`；(c) 其他欄位未動 |
| **驗證方式** | 磁碟檢視 + JSON 解析斷言 |

#### AC-4.3：Game Over 後重啟

| 欄位 | 內容 |
|---|---|
| **情境** | AC-4.2 完成後關閉遊戲 |
| **操作** | 重新啟動遊戲 |
| **預期** | (a) Bootstrap 走標準流程；(b) FT-06 還原 `gameOverState = "Over"`；(c) `IsGameOver() == true`；(d) F-02 不發 `OnMissionCompleted`、FT-04 不被觸發；(e) P-02 啟動畫面提供「回顧結算 / 開新公會」 |
| **驗證方式** | API 斷言 + EventBus log（觀察無結算事件） + 人工觀察 UI |

### 8.5 ResetToNewGame()

#### AC-5.1：刪檔範圍

| 欄位 | 內容 |
|---|---|
| **情境** | 存在 `save.json` + `save.bak1` + `save.bak2` + `save_gameover_1745677938.json` + `save_gameover_1745700000.json` |
| **操作** | 玩家於 Game Over 畫面點「開新公會」，P-02 呼叫 `ResetToNewGame()` |
| **預期** | (a) `save.json` 已刪除；(b) `save.bak1` / `save.bak2` 已刪除；(c) **兩個** `save_gameover_*.json` 仍存在；(d) `OnGameReset` 發布 |
| **驗證方式** | 磁碟檢視 + EventBus log |

#### AC-5.2：Scene reload 後走首次遊玩

| 欄位 | 內容 |
|---|---|
| **情境** | AC-5.1 完成後 |
| **操作** | P-02 訂閱 `OnGameReset` 執行 `SceneManager.LoadScene(bootstrapScene)` |
| **預期** | (a) 全部 MonoBehaviour 重新走 Awake → Start；(b) FT-10 Bootstrap 偵測 `HasSaveFile() == false`；(c) 走 Phase E 首次遊玩；(d) 各 owner 預設初始化 |
| **驗證方式** | API 斷言 + 各 owner 預設值斷言（如 F-03 `currentGold == GOLD_INITIAL`） |

### 8.6 序列化容器規範

#### AC-6.1：Dictionary 轉 List 來回正確

| 欄位 | 內容 |
|---|---|
| **情境** | FT-09 `_factionScores: Dict<int,int> = { (1, 17), (2, -3) }` |
| **操作** | (a) `Serialize()` 寫入；(b) 重啟遊戲；(c) `RestoreFromSave` 讀回 |
| **預期** | 還原後 `_factionScores` Dict 內容與寫入前完全一致（含 key / value / count） |
| **驗證方式** | API 斷言（FT-09 提供測試專用 `GetFactionScoreSnapshot()`） |

#### AC-6.2：Queue 轉 List 來回正確

| 欄位 | 內容 |
|---|---|
| **情境** | FT-09 `_pendingDialogueStages: Queue<int> = [1003, 1005, 1007]`（FIFO 順序） |
| **操作** | 寫入 + 重啟 + 還原 |
| **預期** | 還原後 Queue 順序與寫入前一致（Dequeue 出 1003 → 1005 → 1007） |
| **驗證方式** | API 斷言 + Dequeue 順序驗證 |

### 8.7 API 與事件契約

#### AC-7.1：MarkDirty / ForceSave 公開 API

| 欄位 | 內容 |
|---|---|
| **情境** | FT-10 已 Awake 完成 |
| **操作** | (a) 呼叫 `MarkDirty()`；(b) 呼叫 `ForceSave()` |
| **預期** | (a) `_isDirty == true` 但磁碟未動；(b) 立即執行 `ExecuteSave`、`OnSaveCompleted` 發布 |
| **驗證方式** | Runtime 狀態斷言 + EventBus log + 磁碟檢視 |

#### AC-7.2：HasSaveFile / IsGameOver / GetLoadedFromBackupIndex 查詢

| 欄位 | 內容 |
|---|---|
| **情境** | 多種 Bootstrap 結果（首次遊玩 / 主存檔載入 / bak1 載入 / Over 態） |
| **操作** | 啟動完成後查詢三個 API |
| **預期** | 回傳值對應 §3.7.1 規格表（`HasSaveFile / IsGameOver / loadedFromBackupIndex`） |
| **驗證方式** | API 斷言（4 種情境矩陣測試） |

#### AC-7.3：事件發布順序契約

| 欄位 | 內容 |
|---|---|
| **情境** | 標準 Bootstrap（無錯誤） |
| **操作** | 啟動到 Bootstrap 完成 |
| **預期** | 事件順序為：`各 owner OnRestored`（若有，由 owner 自定）→ `OnLoadCompleted` |
| **驗證方式** | EventBus log 順序記錄 |

### 8.8 Edge Cases 對應

對應 §5 的 12 條 EC，每條一個 AC。簡略表格化（每條 AC 的「驗證方式」共通：Console + 磁碟檢視 + Runtime 狀態斷言）：

| AC | 對應 EC | 情境 | 預期 |
|---|---|---|---|
| AC-EC-1 | EC-1 寫入閃退 | `ExecuteSave` 執行到 `File.Replace` 前強制 kill 進程 | save.tmp 殘留；save.json 仍為前一版本；下次 Bootstrap 正常載入 |
| AC-EC-2 | EC-2 全 backup 損毀 | save + bak1~3 全損毀 | 走首次遊玩；無對話框 |
| AC-EC-3 | EC-3 Critical 失敗 | F-03 區塊損毀，bak1 完整 | 退至 bak1；`loadedFromBackupIndex == 1` |
| AC-EC-4 | EC-4 Degradable 失敗 | FT-09 區塊損毀 | log warning + FT-09 預設化；其他 owner 正常 |
| AC-EC-5 | EC-5 寫入無權限 | persistentDataPath 設為唯讀 | `OnSaveFailed` 發布；遊戲繼續執行（記憶體模式） |
| AC-EC-6 | EC-6 終末檔複製 IOException | 同秒重複觸發 OnGameOver | 第二次 `File.Copy` 拋例外被 catch；不重複封存 |
| AC-EC-7 | EC-7 Game Over 後重啟 | 與 AC-4.3 等價 | （見 AC-4.3） |
| AC-EC-8 | EC-8 Quit 與節流同 frame | 節流寫入進行中時觸發 Quit | `_isSaving` 互斥保護；後到者 return |
| AC-EC-9 | EC-9 時鐘倒退 | 寫檔後手動把系統時間調回過去再重啟 | F-02 `offlineSeconds` clamp 至 0；FT-10 不偵測 |
| AC-EC-10 | EC-10 Schema 演化 | 模擬未來新增 owner，舊存檔無對應子區塊 | owner 收到 null → `InitializeAsNewGame` 預設 |
| AC-EC-11 | EC-11 手動刪 save.json | 刪 save.json 保留 bak1 | bak1 接手；`loadedFromBackupIndex == 1`；下次寫入重建 save.json |
| AC-EC-12 | EC-12 手動刪終末檔 | 刪除 `save_gameover_<ts>.json` | 不影響核心循環；P-02 歷史介面該條目消失 |

### 8.9 AC 索引與優先序

| 優先級 | AC 範圍 | 說明 |
|---|---|---|
| **P0（MVP 必須通過）** | AC-1.1 ~ AC-1.5、AC-2.1 ~ AC-2.7、AC-3.1 ~ AC-3.4、AC-4.1 ~ AC-4.3、AC-5.1 ~ AC-5.2 | 三軌寫入、Bootstrap 流程、Backup 與回退、Game Over 封存、Reset——核心職責全覆蓋 |
| **P1（Jam 完成版必須通過）** | AC-1.6、AC-3.5、AC-6.1 ~ AC-6.2、AC-7.1 ~ AC-7.3、AC-EC-1 ~ AC-EC-9 | 互斥保護、UI 隱性 fail-safe、容器序列化、API 契約、§3 已內嵌 EC |
| **P2（Post-Jam 強化）** | AC-EC-10 ~ AC-EC-12 | Schema 演化、玩家檔案系統干預——非 Jam 必要場景但須有契約 |


