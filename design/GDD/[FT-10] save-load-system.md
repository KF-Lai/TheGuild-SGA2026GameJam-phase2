# Save/Load System 系統設計文件

_建立時間：2026-04-26_
_狀態：草稿（撰寫中）_
_系統 ID：FT-10_

**🔖 撰寫進度**：§1 進行中（依 `/design-system` 互動流程逐節撰寫）。

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
  - `[FT-02] mission-dispatch.md` §5.5 / §6 — 序列化 `activeMissions` 列表、`_nextActiveMissionID`；載入後重建計數器並由 Mission Dispatch 重新呼叫 `RegisterMission` 還原 F-02 計時器
  - `[FT-03] npc-decision-system.md` §6 — 透過 C-02 `AdventurerInstance` 一併序列化 `idleSinceTimestamp` / `lastAutoPickupTimestamp`
  - `[FT-04] outcome-resolution.md` §6 — FT-04 **無持久狀態**，不參與序列化
  - `[FT-05] guild-gold-flow.md` — Jam 範疇外，未來擴充收支日誌時訂閱（FT-10 Jam 版不持久化結算歷史）
  - `[FT-06] guild-core.md` §3.1 / §3.2 / §3.5 — `GuildState` 全量序列化（`guildName` / `displayName` / `foundingTimestamp` / `currentLevel` / `gameOverState`）；訂閱 `OnGuildInitialized` 觸發首次存檔、訂閱 `OnGameOver` 封存存檔；`pendingLevelUpQueue` runtime-only 不序列化
  - `[FT-07] guild-building-system.md` §3.1 / §6 — `BuildingState[]` 全量序列化（6 棟建築 `currentLevel`）
  - `[FT-08] guild-staff-system.md` §3.1 / §3.2.3 / §6 — `StaffPlayerState` + `StaffInstance[]` + `CandidateCard[]` 全量序列化；`reserveConsumedFlag` 永久化、`pityCounter` 跨會話累積、`lastAutoRefreshTimestamp` 全域單一計時器；驗證規則：`staffID > 0 → rolledRarity == StaffTable[staffID].rarity` / `staffID XOR trashItemID`，違規拋 `CandidateCardValidationException`
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
3. 各 owner 依**依賴拓撲順序**執行還原（F-03 → C-06 `currentDangerLevel` → C-02 名冊 → FT-06 → FT-07 → FT-02 `activeMissions` → FT-08 → FT-01 → FT-03 idle 時間戳 → FT-09），每階驗證 ID 合法性（透過 F-01 `GetAll<T>`）；驗證失敗的單筆資料依各 owner GDD 既定 EC 策略處理（FT-09 EC-10 靜默降級、FT-08 §3.2.3 fail-fast 拋 `CandidateCardValidationException` 等）
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

[To be designed]

## 3. 詳細規則（Detailed Rules）

[To be designed]

## 4. 公式（Formulas）

[To be designed]

## 5. 邊緣案例（Edge Cases）

[To be designed]

## 6. 依賴關係（Dependencies）

[To be designed]

## 7. 可調參數（Tuning Knobs）

[To be designed]

## 8. 驗收標準（Acceptance Criteria）

[To be designed]
