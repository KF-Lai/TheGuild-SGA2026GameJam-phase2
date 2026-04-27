# DIP-index：反向依賴（Dependency Inversion / 雙向引用）登記索引

_最後更新：2026-04-27（A 區全部完成）_
_用途：彙整各已通過 design-review 的 GDD 在 §6.4 / §6.6 / §6.3 中列出的「對端 GDD 須補的反向依賴」，作為 `/review-DIP` skill 的工作清單來源_

---

## 圖例與術語

- **來源 GDD**：在自己的 §6.4 / §6.6 / §6.3 列出此項反向依賴需求的 GDD
- **目標 GDD**：應補上反向登記的對端 GDD
- **工作內容**：應補登的具體章節、API、事件、契約描述
- **預期落點**：目標 GDD 中應寫入的章節
- **校驗線索**：可用於 grep 判斷是否已完成的關鍵字（skill 用來自動跳過已完成項）
- **狀態**：
  - ⏳ 待完成
  - ✅ 已完成（含校驗結果）
  - 🔍 待校驗（來源 GDD 標記為已存在，需 grep 對端確認）
  - 🚫 暫停（對端 GDD 尚未設計，待對方撰寫時自行登記）

---

## A. 工作清單（按目標 GDD 分組）

### A.1 F-01 Data Manager

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A1-1 | FT-10 §6.4 #1 | 補登 FT-10 為間接消費者（透過各 owner `RestoreFromSave` 內部呼叫 `Get<T>` / `GetAll<T>` 驗證 ID 合法性） | F-01 §6 反向依賴表 | `FT-10` + `RestoreFromSave` | ✅（2026-04-27 完成）|
| A1-2 | FT-08 §6.6 | 補登 FT-08 為 4 個 owner CSV（`StaffGachaPoolData` / `StaffRefreshCostData` / `StaffRarityProbData` / `TrashItemData`）的查詢者 | F-01 §6.2 下游 | `FT-08` + `StaffGachaPoolData` | ✅（2026-04-27 完成）|

### A.2 F-02 Time System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A2-1 | FT-10 §6.4 #2 | 校驗 §6 已登記 FT-10 為 `Initialize(lastActiveTimestamp)` 唯一呼叫者；若描述不夠明確則補強 | F-02 §6 | `FT-10` + `Initialize` + `lastActiveTimestamp` | ✅（2026-04-27 完成）|

### A.3 F-03 Resource Management

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A3-1 | FT-10 §6.4 #3 | 補登 FT-10 透過 `ISaveable` 還原 6 個欄位（`currentGold` / `currentReputation` / `_warningState` / `_bankruptcyWarningStartTime` / `_warningDurationSec` / `_currentBankruptcyThreshold`）；補 `ISaveable` 實作要求 | F-03 §3 / §6 | `FT-10` + `ISaveable` | ✅（2026-04-27 完成）|

### A.4 C-02 Adventurer Management

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A4-1 | FT-10 §6.4 #4 | 補登 FT-10 透過 `ISaveable` 序列化 `AdventurerInstance[]`（含 FT-03 idle 時間戳）；補 `ISaveable` 實作要求 | C-02 §3 / §6 | `FT-10` + `ISaveable` + `AdventurerInstance` | ✅（2026-04-27 完成）|

### A.5 C-06 World Danger System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A5-1 | FT-10 §6.4 #5 | 補登 FT-10 透過 `ISaveable` 還原 `currentDangerLevel` / `gameStartTimestamp`；明示 Awake 設預設、FT-10 在 Start 之前還原的時序 | C-06 §3 / §6 | `FT-10` + `ISaveable` + `gameStartTimestamp` | ✅（2026-04-27 完成）|

### A.6 FT-01 Adventurer Recruitment

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A6-1 | FT-10 §6.4 #6 | 補登 FT-10 透過 `ISaveable` 序列化候選池 / 刷新時間戳 / 免費刷新次數；補 `ISaveable` 實作要求 | FT-01 §5.4 / §6 | `FT-10` + `ISaveable` | ✅（2026-04-27 完成）|

### A.7 FT-02 Mission Dispatch

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A7-1 | FT-10 §6.4 #7 | 補登 FT-10 還原後 FT-02 自行重新訂閱 F-02 `OnSecondTick` 觸發 `TickCompletionCheck`；補 `ISaveable` 實作要求（序列化 `activeMissions[]` / `_nextActiveMissionID`）| FT-02 §5.5 / §6 | `FT-10` + `ISaveable` + `activeMissions` | ✅（2026-04-27 完成）|

### A.8 FT-03 NPC Decision System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A8-1 | FT-10 §6.4 #8 | 補登 FT-10 透過 C-02 `AdventurerInstance` 一併序列化 idle 時間戳；FT-03 自身 `RestoreFromSave` 為薄層（僅做訂閱重建） | FT-03 §6 | `FT-10` + `薄層` 或 `idleSinceTimestamp` | ✅（2026-04-27 完成）|

### A.9 FT-04 Outcome Resolution

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A9-1 | FT-10 §6.4 #9 | 在 §6 補一行明示「**無持久狀態，不參與序列化**」聲明 | FT-04 §6 | `無持久狀態` 或 `不參與序列化` | ✅（2026-04-27 完成）|

### A.10 FT-05 Guild Gold Flow

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A10-1 | FT-10 §6.4 #10 | 在 §6 補一行明示「Jam 版不持久化結算歷史；FT-10 §1 既定範疇外」（Post-Jam 擴充收支日誌時再修訂）| FT-05 §6 | `FT-10` + `不持久化` 或 `Post-Jam` | ✅（2026-04-27 完成）|

### A.11 FT-06 Guild Core

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A11-1 | FT-10 §6.4 #11 | 補登 FT-10 訂閱 `OnGuildInitialized` / `OnGuildLevelChanged` / `OnGameOver`；透過 `ISaveable` 序列化 `GuildState`；提供 `IsGameOver()` 查詢給 FT-10 / P-02；明示 `pendingLevelUpQueue` runtime-only 不序列化 | FT-06 §3.1 / §3.2 / §3.5 / §6 | `FT-10` + `OnGameOver` + `ISaveable` | ✅（2026-04-27 完成）|
| A11-2 | FT-08 §6.6 | 補登 FT-08 為 `GetCurrentLevel` 消費者（StaffGachaPoolTable.minGuildLevel 過濾、StaffRefreshCostTable[guildLevel] 索引）| FT-06 §6.2 下游 | `FT-08` + `GetCurrentLevel` | ✅（2026-04-27 完成）|

### A.12 FT-07 Guild Building System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A12-1 | FT-10 §6.4 #12 | 補登 FT-10 透過 `ISaveable` 序列化 `BuildingState[]`（6 棟 `currentLevel`）；補 `ISaveable` 實作要求 | FT-07 §3.1 / §6 | `FT-10` + `ISaveable` + `BuildingState` | ✅（2026-04-27 完成）|
| A12-2 | FT-05 §6.4 #1 | 補登 FT-05 為 `OnGuildMaintenanceDue` 訂閱者；補 `GetBuildingPenaltyBonus()` API（Jam 預設 0）| FT-07 §3 / §6 | `FT-05` + `OnGuildMaintenanceDue` | ✅（2026-04-27 完成）|
| A12-3 | FT-08 §6.6 | 補登 FT-08 為 `IsStaffSystemUnlocked` / `GetBuildingLevel(6)`（職員休息室等級驅動 auto refresh interval）消費者 | FT-07 §6.2 下游 | `FT-08` + `IsStaffSystemUnlocked` | ✅（2026-04-27 完成）|

### A.13 FT-08 Gacha System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A13-1 | FT-10 §6.4 #13 | 補登 FT-10 透過 `ISaveable` 序列化 `StaffPlayerState` + `CandidateCard[]`；明示 `CandidateCardValidationException` 為 FT-10 critical 路徑捕獲源 | FT-08 §3.1 / §3.2.3 / §6.7 | `FT-10` + `CandidateCardValidationException` | ✅（2026-04-27 完成）|

### A.14 FT-09 Faction Story System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A14-1 | FT-10 §6.4 #14 | 補一行明示「FT-10 §3.3.4 將 FT-09 列為 degradable，反序列化異常不阻擋 Bootstrap」雙向確認 | FT-09 §6 / §5.10 | `FT-10` + `degradable` 或 `不阻擋 Bootstrap` | ✅（2026-04-27 完成）|

### A.15 FT-12 Staff System

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A15-1 | FT-10 §6.4 #13 | 補登 FT-10 透過 `ISaveable` 序列化 `StaffInstance[]` + `_nextInstanceID` + `_lastSalaryTimestamp`；明示 `CriticalRestoreFailedException` 由 staffID 驗證觸發 | FT-12 §3.1 / §6.7 | `FT-10` + `StaffInstance` + `CriticalRestoreFailedException` | ✅（2026-04-27 完成）|
| A15-2 | FT-08 §6.6 | 校驗 §6 上游列出 FT-08 為 `HireStaff` 呼叫者；下游列出 FT-08 為 `GetRecruitRefreshReductionSec` 消費者 | FT-12 §6 | `FT-08` + `HireStaff` + `GetRecruitRefreshReductionSec` | ✅（2026-04-27 完成）|

### A.16 systems-index.md

| # | 來源 | 工作內容 | 預期落點 | 校驗線索 | 狀態 |
|---|---|---|---|---|---|
| A16-1 | FT-10 §6.4 #15 | 將 FT-10 GDD 狀態從「待設計」改為「已設計」；進度追蹤表將 GDD 欄位由 ⬜ 改為 ✅ | systems-index.md | `FT-10` 行的狀態欄 | ✅（2026-04-27 完成）|

---

## B. 暫停項（待對端 GDD 設計時自行登記）

| # | 來源 | 目標 | 暫停原因 |
|---|---|---|---|
| B-1 | FT-05 §6.4, FT-09 §6.4, FT-10 §6.4 | P-02 Main UI Framework | P-02 GDD 尚未撰寫，待對方設計時統一登記訂閱契約 |
| B-2 | FT-05 §6.4, FT-09 §6.4, FT-10 §6.4 | P-03 Notification System | P-03 GDD 尚未撰寫 |
| B-3 | FT-05 §6.4 | FT-11 Offline Resolver | FT-11 GDD 尚未撰寫，待 Post-Jam 設計時登記為 `OnCommissionAccepted(source = OfflineAutoPick)` 發布者 |

---

## C. 已完成項（歷史紀錄）

| # | 來源 | 目標 | 工作內容 | 完成日 |
|---|---|---|---|---|
| C-1 | FT-05 §6.4 | F-03 | `AddGoldAllowBankruptcy(int delta)` API 新增；§3.2 rule 7、§4.1a 偽代碼、§8 AC-RM-16~19；§6.2 已列 FT-05 | 2026-04-22 |
| C-2 | FT-05 §6.4 | FT-04 | §6.2 已列 FT-05（`OnMissionResolved` 訂閱）| 2026-04-22 |
| C-3 | FT-05 §6.4 | FT-02 | §3.5 發布 `OnCommissionAccepted(missionID, baseReward, source)`；§6.2 已列 FT-05 | 2026-04-22 |
| C-4 | FT-05 §6.4 | FT-03 | §6.3 註明 FT-03 → FT-02.Dispatch(NpcAutoPick) 間接發布；單一發布點原則 | 2026-04-22 |
| C-5 | FT-05 §6.4 | FT-12 | FT-12 §6.3 登記為 `OnStaffSalaryDue` 發布者；§3.6 / §6.2 提供 `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` API | — |
| C-6 | FT-07 §6.3 | FT-06 | `GuildLevelTable` 移除 `rosterCap`/`maxMissions`；移除 `GetRosterCap` / `GetMaxMissions` API；§6.2 補登 FT-07 | — |
| C-7 | FT-07 §6.3 | FT-01 | §6.1 上游：`GetRosterCap()` 來源改 FT-07；新增 FT-07 `GetRecruitRefreshInterval()` | — |
| C-8 | FT-07 §6.3 | FT-02 | §6.1 上游：`GetMaxMissions()` 來源改 FT-07 `GetMaxConcurrentMissions()` | — |
| C-9 | FT-09 §6.4 | FT-04 / C-01 / C-06 / F-01 / FT-02 / FT-08 / FT-12 / systems-index | 7/9 對端登記完成（見 FT-09 §6.4 表）| — |

---

## D. 統計

| 區塊 | 條目數 |
|---|---|
| A 待完成（含 🔍 待校驗）| 0（2026-04-27 本輪全部完成）|
| B 暫停（待對端 GDD 設計）| 3 |
| C 已完成 | 9 |

**A 區待處理目標 GDD 數**：12（F-01 / F-02 / F-03 / C-02 / C-06 / FT-01 / FT-02 / FT-03 / FT-04 / FT-05 / FT-06 / FT-07 / FT-08 / FT-09 / FT-12 / systems-index）

---

## E. 使用方式

執行 `/review-DIP <輸入>` 由 skill 自動掃描並修改：

- **完整檔名**：`/review-DIP 【FT-06】guild-core.md`
- **GDD 代號（逗號分隔）**：`/review-DIP FT-06,FT-07,FT-08`
- **波浪號範圍**：`/review-DIP FT-01~FT-03,C-01~C-03`

skill 將：

1. 解析輸入 → 對照本 index A 區 → 鎖定該 GDD 的所有待處理條目
2. 讀目標 GDD → 用「校驗線索」grep → 已存在則跳過、標 ✅
3. 對未存在條目按「工作內容」逐條補登 → 寫入「預期落點」章節
4. 完成後更新本 index 對應 row 狀態為 ✅，搬到 C 區

---

## F. FSD 反向依賴待處理（備忘錄，目前不啟動）

> **本節用途**：記錄已知 FSD 之間反向依賴缺口，作為未來啟動的備忘錄。**目前不啟動**——多數下游 FSD（FT-06 / FT-07 / FT-08 / FT-09 / FT-10 / FT-12）尚未撰寫，現在補登只是 placeholder。
>
> **啟動時機**：FT-10 FSD 完成後（屆時會帶動最大量缺口），同時或稍後處理 FT-06 / FT-07 / FT-08 / FT-09 / FT-12 FSD §2.4 的同類補登。
>
> **與 GDD DIP 的關係**：FSD 自帶四層反向同步機制（§2.4 下游表 + FSD-index §6.1 / §6.2 / §8.4 GDD 回註），不需要另建獨立 DIP-index，沿用既有結構即可；本節僅作為「待處理缺口清單」備忘。

### F.1 已知缺口清單（FT-10 FSD 觸發）

當 FT-10 FSD 撰寫完成時，下列上游 FSD 的 §2.4「下游被依賴系統」需追加 FT-10 為下游：

| # | 上游 FSD | 應補入 §2.4 內容 | 來源依據 |
|---|---|---|---|
| F-1 | `【F-02-FSD】time-system.md` | FT-10 — `Initialize(lastActiveTimestamp)` 唯一呼叫者 | FT-10 GDD §6.4 #2 |
| F-2 | `【F-03-FSD】resource-management.md` | FT-10 — `ISaveable` 序列化 6 欄位（currentGold / currentReputation / _warningState 等） | FT-10 GDD §6.4 #3 |
| F-3 | `【C-02-FSD】adventurer-management.md` | FT-10 — `ISaveable` 序列化 `AdventurerInstance[]` | FT-10 GDD §6.4 #4 |
| F-4 | `【C-06-FSD】world-danger-system.md` | FT-10 — `ISaveable` 還原 currentDangerLevel / gameStartTimestamp | FT-10 GDD §6.4 #5 |
| F-5 | `【FT-01-FSD】adventurer-recruitment.md` | FT-10 — `ISaveable` 序列化候選池 / 刷新時間戳 / 免費刷新次數 | FT-10 GDD §6.4 #6 |
| F-6 | `【FT-02-FSD-A】mission-dispatch-core.md` | FT-10 — `ISaveable` 序列化 `activeMissions[]` / `_nextActiveMissionID`；還原後重新訂閱 OnSecondTick | FT-10 GDD §6.4 #7 |
| F-7 | `【FT-03-FSD】npc-decision-system.md` | FT-10 — 薄層 ISaveable，idle 時間戳由 C-02 一併序列化 | FT-10 GDD §6.4 #8 |
| F-8 | `【FT-04-FSD】outcome-resolution.md` | FT-10 — 「無持久狀態，不參與序列化」對端聲明 | FT-10 GDD §6.4 #9 |
| F-9 | `【FT-05-FSD】guild-gold-flow.md` | FT-10 — 「Jam 版不持久化結算歷史」對端聲明 | FT-10 GDD §6.4 #10 |

### F.2 啟動時的處理建議

啟動時建議走以下流程：

1. **擴充本 DIP-index 結構**：將 F 區從備忘錄升級為正式工作清單（A 區風格的表格 + 校驗線索 + 狀態欄）
2. **擴充 `/review-DIP` skill**：
   - 加 `--target=fsd|gdd|both` 旗標（預設 `gdd` 維持向下相容）
   - skill 解析輸入時根據旗標查 A 區（GDD）或 F 區（FSD）
   - 校驗線索 grep 路徑改為 `design/FSD/【<id>-FSD】*.md` 而非 `design/GDD/`
   - 預期落點為 FSD §2.4 表格（追加 row 而非新增章節）
3. **同時掃 FT-06 / FT-07 / FT-08 / FT-09 / FT-12 FSD**：這幾份 FSD 撰寫時自然會在 §2.3 列上游、§2.4 列下游；若有遺漏的雙向缺口一併補

### F.3 不在範疇

- **不處理 FSD → GDD 回註**：已有 §8.3 偏差列表 + §8.4 GDD 回註紀錄機制（每份 FSD 內建），運作正常
- **不處理 FSD → Data-Specs 反向索引**：已由 FSD-index §6.2 「被引用的 FSD」欄機制覆蓋
- **不處理尚未撰寫的下游 FSD（P-01 / P-02 / P-03 等）**：對齊 B 區暫停慣例，待對方撰寫時自行登記
