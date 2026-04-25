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

[To be designed]

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
