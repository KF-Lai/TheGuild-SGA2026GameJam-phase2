# 【FT-08-FSD】功能規格說明書 — Gacha System（面試系統）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-08】gacha-system.md`（版本：2026-04-26 更新；2026-04-26 由原職員系統拆出，聚焦 gacha） |
| 對應 Data-Specs | `【FT-08-DS】staff-gacha-pool-table.md`（_待建_，CSV：`StaffGachaPoolTable.csv`，FT-08 owner）<br>`【FT-08-DS】staff-refresh-cost-table.md`（_待建_，CSV：`StaffRefreshCostTable.csv`，FT-08 owner）<br>`【FT-08-DS】staff-rarity-prob-table.md`（_待建_，CSV：`StaffRarityProbTable.csv`，FT-08 owner）<br>`【FT-08-DS】trash-item-table.md`（_待建_，CSV：`TrashItemTable.csv`，FT-08 owner）<br>`【FT-08-DS】staff-tuning.md`（_待建_，CSV：`StaffTuning.csv`，FT-08 / FT-12 共用 owner；FT-08 註冊 `PITY_THRESHOLD` / `TRASH_ROLL_RATE_AT_RARITY_1` / `MIN_AUTO_REFRESH_INTERVAL_SEC` / `INTERVIEW_AUTO_REFRESH_INTERVAL_L1~L5` / `INTERVIEW_SLOT_COUNT_L1~L5` / `MAX_RESERVE_FALLBACK`）<br>`【F-01-DS】system-constants.md`（消費端：`OFFLINE_MAX_SECONDS`） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體（Opus 4.7 + xhigh） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-28 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-08 Gacha System（面試系統）為公會職員的面試入職介面：透過 gacha roll 出 `CandidateCard`，玩家以「錄用 / 不錄用 / 保留」三擇一決定每張候選命運；錄用後透過 `FT-12.HireStaff(candidate)` 同步 API 交棒至職員系統建立 `StaffInstance`。本系統不管理已錄用職員，僅負責 **roll → 候選卡 → 玩家決策 → 交棒** 的玩家入口；涵蓋多池架構與閘控、四類 refresh 路徑、稀有度動態歸一化、保底機制、垃圾物品（trash items）、系統降級與 ISaveable 持久化。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：

- Jam 版 2 池（A 池 = poolID 1 常駐通用、B 池 = poolID 2 中高等）的多池架構
- `[minGuildLevel, maxGuildLevel]` 主閘 + 5 個 FT-09 預留閘（Jam 版強制空值）的 DataManager 驗證
- 四類 refresh 觸發路徑（Auto / Manual / Switch Pool / Offline Refill）共用 `ExecuteRefresh` 流程
- 稀有度動態歸一化（§4.1.5）+ 層內 staff weighted roll（§4.1.6）+ Trash detection（§3.5.3）
- `pityCounter` 跨池跨 session 累積、命中強制最高稀有度、命中後立即 reset
- `reservedCandidates` 跨池保留區、`reserveTimeLimitSec` 過期釋放、`reserveConsumedFlag` 防刷
- 6 個玩家 API（`TryManualRefresh` / `TrySwitchPool` / `TryRecruit` / `TryRejectCandidate` / `TryReserveCandidate` / `TryReleaseReserve`）
- `OnStaffSystemBoot` 啟動事件（FT-08 唯一對外業務事件）
- ISaveable 序列化 `StaffPlayerState`（critical owner，OwnerKey = `"ft08Gacha"`）
- 系統降級：`FT-07.IsStaffSystemUnlocked() == false` 時所有 `Try*` 回 `STAFF_SYSTEM_LOCKED`、不發布業務事件

**Out-of-Scope**：

- 第 3+ 個 gacha 池（schema 已預留，runtime 留待 Post-Jam）
- FT-09 預留閘 runtime 啟用（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`；Jam 版強制驗證為預設值）
- 線上 auto refresh tick（Jam 版 auto 補刷僅於 `OnStaffSystemBoot` 一次性執行；不訂閱 OnHourTick / OnMinuteTick）
- 職員圖鑑 / 圖像介紹 / 詳細 UI 動畫（屬 P-02 範疇）
- 已錄用職員的管理（屬 FT-12，FT-08 透過 `HireStaff` 單向交棒）
- 薪水 / 解雇 / effect 聚合（屬 FT-12）
- Phase 2 離線薪水補發（GDD §3.3.5 Step D 已標 Jam 版整段跳過）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 AC-1 ~ AC-25。程式可驗證條件如下：

1. **DataManager 驗證**：5 張 owner CSV 全部通過 §3.2 / §3.3.6 / §3.5.2 驗證規則；任一違規拋對應 `*ValidationException` 並 fail-fast（AC-16 / AC-17 / AC-18）。
2. **系統閘**：`FT-07.IsStaffSystemUnlocked() == false` 時 6 個 `Try*` API 一律回 `STAFF_SYSTEM_LOCKED`、不變動 `_playerState`、`OnStaffSystemBoot` payload `isUnlocked = false` 仍發布（AC-1 / AC-20）。
3. **首次解鎖**：解鎖後 `OnStaffSystemBoot` 自動補刷一次，`currentCandidates` 由空填滿 N 張（AC-2）。
4. **Refresh**：手動扣 `cost`、覆蓋 N 張、`pityCounter += |refreshableSlots|`、不更新 `lastAutoRefreshTimestamp`（AC-3）；Auto 補刷後 `lastAutoRefreshTimestamp ← now`（AC-4）；離線多 interval 仍只補 1 次（AC-5）。
5. **池切換**：`currentCandidates` 整個重 roll、`reservedCandidates` 跨池存活、`pityCounter += N`（AC-6）。
6. **保留生命週期**：`TryReserveCandidate` 加入 reservedCandidates（AC-7）；`reserveTimeLimitSec` 到期 `OnStaffSystemBoot` Step B 自動釋放（AC-8）；釋放後 `reserveConsumedFlag = true` 永不可再保留（AC-9）。
7. **稀有度與保底**：層全空時動態歸一化（AC-10）；`pityCounter ≥ PITY_THRESHOLD` 下次 refresh 首張強制 5★（AC-11）；命中後 `pityCounter ← 0`（AC-12）。
8. **錄用交棒**：`TryRecruit` 同步呼叫 `FT-12.HireStaff`，OK 後 slot 置為 placeholder（AC-13）；FT-12 回 `STAFF_SYSTEM_LOCKED` 視為失敗、不變動 candidate、LogError（AC-14）；不錄用清空 slot（AC-15）。
9. **事件契約**：`OnStaffSystemBoot` payload 含 4 欄位（AC-19）；降級時仍發布（AC-20）。
10. **持久化**：Save → reload `StaffPlayerState` 完整還原（AC-21）；`rolledRarity != StaffTable.rarity` 拋 `CandidateCardValidationException`（AC-22）；trash 反查 `TrashItemTable[trashItemID]` 不存在拋例外（AC-23）。
11. **效能**：單次 5 張 refresh < 5ms、Boot 全流程（保留掃描 + 補刷）含 7 保留 + 5 候選 < 10ms（AC-24 / AC-25，PlayMode Profiler 驗證）。
12. **AC 測試覆蓋**：AC-1 ~ AC-23 共 23 項以 EditMode test 驗證；AC-24 / AC-25 走 PlayMode Profiler；總計 25 項 AC 全覆蓋。

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1（系統範圍 / Jam 範疇 / FT-08-FT-12 邊界）→ FSD §1
- §2（玩家幻想 / 設計原則）→ FSD §3
- §3.1（StaffPlayerState / CandidateCard schema + placeholder card 機制 + 驗證規則）→ FSD §5.3 / §6.1
- §3.2（StaffGachaPoolTable schema + 5 預留閘驗證）→ FSD §6.1 / §7
- §3.3（四類 refresh 觸發路徑 + ExecuteRefresh + RollOneSlot + 6 API 合約 + OnStaffSystemBoot + StaffRefreshCostTable schema + 事件發布原則）→ FSD §5.1 / §5.2 / §5.4 / §6.1
- §3.4（pity 機制細節 + 跨 session + Debug Reset 契約）→ FSD §5.4 / §7
- §3.5（Trash items 概念 / TrashItemTable schema / Trash roll 機率 / 計入 pity）→ FSD §6.1 / §7
- §3.6（系統降級行為總表）→ FSD §5.1 / §7
- §3.7（事件契約 + 訂閱者責任 + TryRecruit transaction 邊界 + EventBus 隔離）→ FSD §5.2 / §5.4
- §4.1.1 ~ §4.1.9（9 條公式）→ FSD §5.1 / §5.4
- §5.1 ~ §5.6（6 類邊緣案例）→ FSD §7
- §6.1 ~ §6.7（依賴 / 事件 / 資料表 / ISaveable 持久化契約）→ FSD §2.3 / §2.4 / §2.5 / §6 / §5.3
- §7.1 ~ §7.5（可調參數）→ FSD §6 / §7
- §8 AC-1 ~ AC-25 → FSD §1.3 + §7（每條 AC 在 §7 邊緣案例對策表或本 §1.3 條目中可被驗證）

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-08-DS】staff-gacha-pool-table.md`（_待建_） | `StaffGachaPoolTable.csv` | `poolID` / `poolName` / `minGuildLevel` / `maxGuildLevel` / `eligibleStaffIDs` / `staffWeights` / `reserveTimeLimitSec` / 5 預留閘 | 池架構 / 主閘 + 預留閘 / staffWeights / 保留時限 |
| `【FT-08-DS】staff-refresh-cost-table.md`（_待建_） | `StaffRefreshCostTable.csv` | `guildLevel` / `cost` / `interviewSlotCount` | 手動刷新費 / 面試欄張數 N |
| `【FT-08-DS】staff-rarity-prob-table.md`（_待建_） | `StaffRarityProbTable.csv` | `rarity` / `prob` | 稀有度基礎機率 baseProb |
| `【FT-08-DS】trash-item-table.md`（_待建_） | `TrashItemTable.csv` | `trashItemID` / `name` / `flavorText` / `iconAssetID` | 1★ 層 trash items |
| `【FT-08-DS】staff-tuning.md`（_待建，FT-08 / FT-12 共用） | `StaffTuning.csv` | `PITY_THRESHOLD` / `TRASH_ROLL_RATE_AT_RARITY_1` / `MIN_AUTO_REFRESH_INTERVAL_SEC` / `INTERVIEW_AUTO_REFRESH_INTERVAL_L1~L5` / `INTERVIEW_SLOT_COUNT_L1~L5` / `MAX_RESERVE_FALLBACK` | 系統常數（FT-08 自行註冊；不寫入 SystemConstants） |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `OFFLINE_MAX_SECONDS` | 離線時間上限（補刷判定不超過 7 天） |

> **跨系統 FK 引用**：`StaffGachaPoolTable.eligibleStaffIDs` / `CandidateCard.staffID` 引用 FT-12 owner 的 `StaffTable.staffID`；`CandidateCard.trashItemID` 引用本 FT-08 owner 的 `TrashItemTable.trashItemID`。

### 2.3 上游依賴系統

依 FSD-index §2.10 規範，敘述採 `IXxx` 形式以利可讀性，實作以 concrete singleton 為準。

| 上游系統 | API / 資料 | 實作契約 | 用途 |
| --- | --- | --- | --- |
| F-01 DataManager | `IDataManager.Get<T>(id)` / `GetAll<T>()` | `DataManager.Instance` | 載入 5 張 owner CSV + 驗證 candidate.staffID 反查 `StaffTable` |
| F-02 Time System | `ITimeSystem.NowUTC` | `TimeSystem.Instance` | refresh / 保留時限 / 補刷判定皆以 UTC timestamp 比對 |
| F-03 Resource Management | `IResourceService.GetCurrentGold()` / `AddGold(delta)` | `ResourceManagement.Instance` | 手動刷新扣費 |
| FT-06 Guild Core | `IGuildCoreService.GetCurrentLevel()` | `GuildCoreService.Instance` | `StaffGachaPoolTable.minGuildLevel` 過濾 + `StaffRefreshCostTable[guildLevel]` 索引 |
| FT-07 Guild Building System | `IBuildingService.IsStaffSystemUnlocked()` / `GetBuildingLevel(6)` | `BuildingService.Instance` | 系統閘 + 職員休息室等級驅動 auto refresh interval |
| FT-12 Staff System | `IStaffService.HireStaff(CandidateCard) → HireResult` / `GetRecruitRefreshReductionSec()` | `StaffService.Instance` | 錄用候選交棒 + auto refresh 間隔加成 |
| Core EventBus | `EventBus.Publish<T>` / `Subscribe<T>` | static `EventBus`（commits 25efd11 / 2c10d17 隔離強化） | 發布 `OnStaffSystemBoot` |
| FT-10 Save/Load System | `ISaveable` 介面 | 由 FT-10 統一管理 | OwnerKey = `"ft08Gacha"`，IsCritical = true |

### 2.4 下游被依賴系統

| 下游系統 | 引用方式 | 用途 |
| --- | --- | --- |
| FT-12 Staff System | 被 FT-08 同步呼叫 `HireStaff(candidate)` | 接收錄用候選並建立 `StaffInstance` |
| P-02 Main UI Framework | 訂閱 `OnStaffSystemBoot` + 查詢 6 個 `Try*` API + `GetCurrentCandidates()` / `GetReservedCandidates()` 等 query API | 面試介面、保留區、候選卡顯示 |
| P-03 Notification System | 訂閱 `OnStaffSystemBoot`（可選） | 補刷完成 toast |
| FT-10 Save/Load System | 透過 `ISaveable.Serialize()` / `RestoreFromSave()` 介面回呼 | 序列化 / 還原 `StaffPlayerState` |

### 2.5 跨系統事件契約

**FT-08 發布事件**（唯一）：

| 事件 | Payload | 發布時機 | 訂閱者 |
| --- | --- | --- | --- |
| `OnStaffSystemBoot` | `{ bool isUnlocked; int appliedRefreshCount; int releasedReserveCount; long bootTimestamp; }` | `OnStaffSystemBoot()` 流程結尾（含 Step A~C 完成；降級時僅 Step A 後即發） | P-02（刷新面試 UI） / P-03（補刷 toast，可選） |

**FT-08 不發布**（GDD §3.3.7 / §3.7.1 明定）：

- refresh 流程本身不發事件（私有 UI 狀態，由 P-02 polling）
- 玩家動作（`TryRecruit` 成功 / `TryReject` / `TryReserve` / `TryRelease` / `TrySwitchPool`）皆不發 FT-08 事件
- `TryRecruit` 成功觸發的 `OnStaffHired` 為 FT-12 內部發布，FT-08 不擁有、不訂閱、不轉發

**FT-08 訂閱事件**：

- 無（FT-07 解鎖狀態採 lazy 檢測；不訂閱 `OnBuildingUpgraded`）

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

GDD §2 描述兩條核心情緒：「翻面試卡的期待感」（5 張候選依序翻面、5★ 的瞬間屏住呼吸、保留 vs 錄用 vs 不錄用的猶豫）與「保底是希望、Trash 是現實」（pityCounter 累積到上限的安心感、1★ 層偶爾翻到咖啡杯與履歷紙團的世界觀紋理）。整體幻想敘事以「玩家點開面試介面 → 看 5 張卡片翻面 → 三擇一決定 → 點刷新 → 等待」為節奏。

### 3.2 系統目的還原

GDD §1 定位 FT-08 為「公會職員的面試入職介面」。系統職責三段：(A) 多池架構與閘控（A/B 池 + 主閘 + 5 個預留閘）、(B) 刷新模型（自動 / 手動雙軌 + 計入保底 + 離線補刷一次）、(C) 候選卡決策（稀有度動態歸一化 + 保底 + 垃圾物品 + 三擇一 + 跨池保留區）。FT-08 純屬入口，不管後續職員怎麼用。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 翻面試卡的期待感 | 點刷新 → N 張候選依序翻面、出現 5★ 時屏住呼吸 | `RollOneSlot` 對 `refreshableSlots` 逐 slot 計算稀有度（`weightedRoll(effectiveProb)`）+ 層內 staff 權重；P-02 收 query 後播翻面動畫；refresh 不發事件 |
| 保底是希望 | pityCounter UI 顯示「再 X 張保底」、達標下次刷新首張保證 5★ | `pityCounter ≥ PITY_THRESHOLD AND poolEligibleByRarity(currentPoolID, 5) ≠ ∅` 觸發強制 `rolledRarity = 5`、命中立即 `pityCounter ← 0`；跨 session 由 `StaffPlayerState.pityCounter` 持久化 |
| Trash 是現實 | 1★ 層偶爾翻到咖啡杯、履歷紙團；玩家會笑 | `ShouldRollTrash()` 在 1★ 命中後以 `TRASH_ROLL_RATE_AT_RARITY_1 = 0.30` 機率轉成 trash item；CandidateCard 以 `staffID = 0`、`trashItemID > 0` 表示；`TryRecruit` 對 trash 回 `CANDIDATE_NOT_HIREABLE` |
| 保留機制不浪費 | 「我想等下個刷新再決定」是合法策略；保留時限到自動釋放 | `TryReserveCandidate` 將卡移入 `reservedCandidates` 並設 `isReserved / reservedTimestamp`；`OnStaffSystemBoot` Step B 與 `ExecuteRefresh` step 2 統一掃描過期；釋放後 `reserveConsumedFlag = true` 防玩家透過再保留循環刷重置 |
| 多池策略選擇 | 中後期玩家切到 B 池追稀有 staff、A 池保留為日常池 | `TrySwitchPool(newPoolID)` 切池並全重 roll；`reservedCandidates` 跨池存活；切池本身不重置 `lastAutoRefreshTimestamp` |
| 公會等級驅動的成長感 | 升等 → 面試欄變多（N: 3→5）、刷新更便宜 | `StaffRefreshCostTable[guildLevel].interviewSlotCount` 索引 N；`refreshCost(guildLevel)` 索引 cost；切池或 refresh 時即時讀取 |
| 系統閘控的解鎖感 | 蓋出職員休息室前面試介面被鎖；蓋好 L1 立刻可用 | `IBuildingService.IsStaffSystemUnlocked()` lazy 檢測；`OnStaffSystemBoot` 在 FT-10 Bootstrap 與解鎖瞬間皆觸發 |
| 離線回來看到新候選 | 玩家隔天上線看到面試欄已被刷新 | `OnStaffSystemBoot` Step C 計算 `missedIntervals`、clamp 至 1 次補刷；不引入線上 tick 訂閱 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（單一 FSD，內含 5 個 Script；對齊 FT-06 / FT-07 / C-06 等先例）。

### 4.2 拆分理由

不拆分理由（依 FSD-index §2.4 標準）：

- 預估合計 ~1180 行分成 5 個 Script，平均 < 250 行；最大檔（GachaService）預估 ~450 行，未越過 500 行拆分門檻。
- Script 邊界以資料／載入／純函式 roll／服務協調 4 層分隔，職責已清晰，無進一步拆分動機。
- FT-08 的 `_playerState` 為單一 critical state container，所有 6 個 `Try*` API 均需共讀寫，拆分為 FSD-A / FSD-B 反而引入 service 對 service 呼叫的耦合。
- §3.3 / §3.4 / §3.5 / §3.7 雖各自為章，但流程上交織（refresh 內呼叫 RollOneSlot、RollOneSlot 內判 pity 與 trash、TryRecruit 內呼叫 FT-12 並改 _playerState），不適合於 FSD 層拆。

### 4.3 拆分結果

不適用（4.1 已決定不拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `GachaTypes` | `Assets/Scripts/Gameplay/Gacha/GachaTypes.cs` | 定義 FT-08 對外 / 對內所有資料結構與 enum：`StaffPlayerState`、`CandidateCard`、`StaffGachaPoolData`、`StaffRefreshCostData`、`StaffRarityProbData`、`TrashItemData`、`StaffTuningEntry` 對應 row、`RefreshType` enum、6 個 `*Result` enum、`OnStaffSystemBoot` 事件 payload。 | 無（純 POCO + `[Serializable]`） | 200~260 行 |
| `IGachaService` | `Assets/Scripts/Gameplay/Gacha/IGachaService.cs` | 定義 FT-08 對外 6 個玩家 API + 8 個 query API + ISaveable 介面方法簽名，供 P-02 / FT-10 / 測試替身（Phase 2）以 mock 引用。 | `GachaTypes` | 70~100 行 |
| `GachaTableLoader` | `Assets/Scripts/Gameplay/Gacha/GachaTableLoader.cs` | 載入並驗證 5 張 owner CSV：`StaffGachaPoolTable` / `StaffRefreshCostTable` / `StaffRarityProbTable` / `TrashItemTable` / `StaffTuning`（FT-08 認養的 keys）；計算 `poolEligibleByRarity` 索引、`baseProb` lookup table、`tuning` 預載常數；對外提供唯讀 query。 | `IDataManager`、`GachaTypes` | 280~340 行 |
| `GachaRollEngine` | `Assets/Scripts/Gameplay/Gacha/GachaRollEngine.cs` | 純函式 roll 引擎：`RollOneSlot(slot, poolID, isFirst, pityCounter)` → 回傳 `(CandidateCard, bool pityHit)`；內含稀有度動態歸一化（§4.1.5）、層內 staff weighted roll（§4.1.6）、保底判定（§4.1.8）、trash detection（§3.5.3）。所有狀態變更回呼端決定，不直接寫 `_playerState`。 | `IDataManager`、`GachaTableLoader`、`GachaTypes`、`UnityEngine.Random` | 200~260 行 |
| `GachaService` | `Assets/Scripts/Gameplay/Gacha/GachaService.cs` | FT-08 主協調器（singleton + `MonoBehaviour`）：6 個 `Try*` API、`OnStaffSystemBoot()` 啟動序列、`ExecuteRefresh()` 共享流程、`ReleaseReserveInternal()`、ISaveable 實作（Serialize / RestoreFromSave / InitializeAsNewGame）、降級檢測、冪等保護 `_isHiringInFlight`；持有 `_playerState` 單一真實來源。 | `IDataManager`、`ITimeSystem`、`IResourceService`、`IGuildCoreService`、`IBuildingService`、`IStaffService`、`EventBus`、`GachaTableLoader`、`GachaRollEngine`、`GachaTypes` | 420~500 行 |

合計預估 1170~1460 行。

### 4.5 類別關係（可選）

```
                       ┌─────────────────────────┐
                       │     GachaService        │  (singleton; ISaveable)
                       │   (持 _playerState)     │
                       └──────────┬──────────────┘
                                  │ uses
              ┌───────────────────┼────────────────────┐
              │                   │                    │
   ┌──────────▼──────────┐  ┌─────▼──────────┐  ┌──────▼─────┐
   │  GachaTableLoader   │  │ GachaRollEngine│  │   外部上游 │
   │ (5 CSV + tuning)    │  │ (純函式 roll)  │  │ FT-06/7/12/ │
   │  load + validate    │  │ pity / trash   │  │ F-01/2/3   │
   │  poolEligibleByRarity│  │ normalization  │  │  EventBus   │
   └──────────┬──────────┘  └─────┬──────────┘  └─────────────┘
              │                   │
              └─── reads ─────┐   ▼ creates
                              │ ┌───────────────┐
                              └─▶  GachaTypes  │
                                │  (POCO + enum)│
                                └───────────────┘

實作 (concrete) :
   GachaService → IGachaService (對外 API 介面)
                → ISaveable     (FT-10 介面)
```

`GachaService` 為唯一持有可變狀態的類別；`GachaRollEngine` 為純函式（接收 `pityCounter` 等參數，回傳新 `CandidateCard` 與是否命中 pity）；`GachaTableLoader` 為載入後唯讀的資料容器。

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

`GachaService` 實作 `IGachaService`。命名與回傳碼對齊 GDD §3.3.4。

**6 個玩家動作 API**（皆走系統閘檢測，降級時回 `STAFF_SYSTEM_LOCKED`）：

```csharp
RefreshResult     TryManualRefresh();
SwitchPoolResult  TrySwitchPool(int newPoolID);
RecruitResult     TryRecruit(int slotIndex);
RejectResult      TryRejectCandidate(int slotIndex);
ReserveResult     TryReserveCandidate(int slotIndex);
ReleaseResult     TryReleaseReserve(int reserveIndex);
```

**Query API**（P-02 polling 用；降級時回安全預設值）：

```csharp
bool                    IsStaffSystemUnlocked();             // 轉發 FT-07
int                     GetCurrentPoolID();                  // _playerState.currentPoolID
IReadOnlyList<CandidateCard> GetCurrentCandidates();         // 含 placeholder（IsEmptySlot 判斷）
IReadOnlyList<CandidateCard> GetReservedCandidates();
int                     GetPityCounter();
int                     GetInterviewSlotCount();             // N(guildLevel)
int                     GetManualRefreshCost();              // refreshCost(guildLevel)
long                    GetNextAutoRefreshUtcTimestamp();    // last + interval；降級時回 0
```

**Editor-only debug API**（`#if UNITY_EDITOR` 包住整個 method，release 不編譯；GDD §3.4.6）：

```csharp
#if UNITY_EDITOR
internal void DebugResetPityCounter();
#endif
```

**ISaveable 實作**（OwnerKey = `"ft08Gacha"`，IsCritical = true，§5.3 細節）：

```csharp
string OwnerKey { get; }                  // "ft08Gacha"
bool   IsCritical { get; }                // true
string Serialize();                        // JsonUtility(_playerState)
void   RestoreFromSave(string ownerJson); // 反序列化 + candidate.staffID 驗證 + Step B
void   InitializeAsNewGame();             // 預設值（§6.7）
```

**回傳碼語意**（GDD §3.3.4）：

| 回傳碼 | 觸發條件 |
| --- | --- |
| `STAFF_SYSTEM_LOCKED` | `IsStaffSystemUnlocked() == false` |
| `INVALID_SLOT` | `slotIndex < 0` 或 `slotIndex ≥ N` |
| `EMPTY_SLOT` | `IsEmptySlot(currentCandidates[slotIndex]) == true` |
| `INVALID_INDEX` | `reserveIndex < 0` 或 `reserveIndex ≥ reservedCandidates.Count` |
| `POOL_NOT_FOUND` | `StaffGachaPoolTable[newPoolID]` 不存在 |
| `POOL_LEVEL_LOCKED` | `currentGuildLevel ∉ [pool.minGuildLevel, pool.maxGuildLevel]` |
| `ALREADY_IN_POOL` | `newPoolID == currentPoolID` |
| `GOLD_INSUFFICIENT` | `GetCurrentGold() < refreshCost(guildLevel)` |
| `CANDIDATE_NOT_HIREABLE` | `currentCandidates[slotIndex].trashItemID > 0`（trash 不可錄用） |
| `ROSTER_FULL` | FT-12 回 `ROSTER_FULL` 轉發 |
| `RESERVE_FULL` | `reservedCandidates.Count == maxReserve` |
| `RESERVE_CONSUMED` | `currentCandidates[slotIndex].reserveConsumedFlag == true` |
| `SLOT_OCCUPIED_BY_NEW_ROLL` | 解除保留時原 slot 已被新 roll 覆蓋（GDD Case 5.2.4） |
| `INVALID_STAFF_ID` | FT-12 回 `INVALID_STAFF_ID` 轉發 |
| `INTERNAL_ERROR` | FT-12 回非預期 result 轉發 |
| `NO_REFRESHABLE_SLOT` | 全 slot 被保留鎖定（refreshableSlots 為空，Manual / Auto / Switch 共用） |
| `BUSY` | `_isHiringInFlight == true`（同 frame 連點防護） |
| `SUCCESS` | 一切正常 |

> **回傳碼分布**：每個 API 回傳自己的 enum（`RefreshResult` / `SwitchPoolResult` / `RecruitResult` / `RejectResult` / `ReserveResult` / `ReleaseResult`），各 enum 只包含與該 API 語意相符的子集（例：`RecruitResult` 含 `SUCCESS / STAFF_SYSTEM_LOCKED / INVALID_SLOT / EMPTY_SLOT / CANDIDATE_NOT_HIREABLE / ROSTER_FULL / INVALID_STAFF_ID / INTERNAL_ERROR / BUSY`，不含 `GOLD_INSUFFICIENT`）。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnStaffSystemBootEvent` | 出 | `{ bool isUnlocked; int appliedRefreshCount; int releasedReserveCount; long bootTimestamp; }` | `OnStaffSystemBoot()` 完成 Step A~C 後立即發布；P-02 重整面試 UI；P-03（可選）顯示「補刷已就緒」toast |
| `OnStaffHired`（FT-12 owner） | — | — | FT-08 不發、不訂閱、不轉發；TryRecruit OK 後由 FT-12 內部發布 |
| `OnBuildingUpgraded`（FT-07 owner） | — | — | FT-08 不訂閱；解鎖狀態採 lazy 檢測（GDD §3.6.5） |

### 5.3 資料結構

**`StaffPlayerState`**（`[Serializable]` class，FT-10 序列化單一容器）：

```csharp
[Serializable]
public class StaffPlayerState {
    public int                    pityCounter;                  // 跨池累積
    public long                   lastAutoRefreshTimestamp;     // 全域單一
    public int                    currentPoolID;                // 1 = A 池（預設）
    public List<CandidateCard>    currentCandidates;            // 固定 N 大小，空 slot 用 placeholder
    public List<CandidateCard>    reservedCandidates;           // 跨池保留區
}
```

**`CandidateCard`**（`[Serializable]` class）：

```csharp
[Serializable]
public class CandidateCard {
    public int   poolID;
    public int   slotIndex;
    public int   staffID;                // 0 = trash 或 placeholder
    public int   trashItemID;            // 0 = staff 或 placeholder
    public int   rolledRarity;           // 1~5；placeholder = 0；trash 固定 1
    public long  rolledTimestamp;
    public bool  isReserved;
    public long  reservedTimestamp;
    public bool  reserveConsumedFlag;
}
```

**`OnStaffSystemBootEvent`** payload：

```csharp
public readonly struct OnStaffSystemBootEvent {
    public readonly bool isUnlocked;
    public readonly int  appliedRefreshCount;
    public readonly int  releasedReserveCount;
    public readonly long bootTimestamp;
    public OnStaffSystemBootEvent(bool a, int b, int c, long d) { ... }
}
```

**`HireResult`**（FT-12 回傳，FT-08 引用 FT-12 命名空間 / 公開 enum；不重新定義）。

**`RefreshType`** enum：

```csharp
internal enum RefreshType { Auto, Manual, SwitchPool, OfflineRefill }
```

> `OfflineRefill` 為 `Auto` 的別名（行為相同：`lastAutoRefreshTimestamp ← now`、不扣費、覆蓋 N 張），分開命名僅供 log / debug 區分。

**6 個 `*Result` enum**（簡化記法；每個 enum 列舉表中對應該 API 可能回傳的子集；參見 §5.1 回傳碼分布）。

**Helper**：

```csharp
internal static class CandidateCardExtensions {
    public static bool IsEmptySlot(this CandidateCard c) => c.staffID == 0 && c.trashItemID == 0;
    public static CandidateCard MakeEmptySlot(int poolID, int slotIndex) => new() {
        poolID = poolID, slotIndex = slotIndex,
        staffID = 0, trashItemID = 0, rolledRarity = 0,
        rolledTimestamp = 0, isReserved = false, reservedTimestamp = 0, reserveConsumedFlag = false
    };
}
```

**ISaveable 序列化策略**：以 Unity `JsonUtility` 序列化單一 `_playerState`，外層包成 wrapper（避免 List 直接 root）：

```csharp
[Serializable] private class StaffPlayerStateWrapper { public StaffPlayerState state; }
```

### 5.4 內部資料流

#### 5.4.1 Bootstrap 流程（FT-10 載入後立即執行）

```
FT-10.LoadComplete
  → GachaService.OnStaffSystemBoot()
      ├─ if (!_buildingService.IsStaffSystemUnlocked())：
      │     ├─ EventBus.Publish(new OnStaffSystemBootEvent(false, 0, 0, now))
      │     └─ return                            // 系統降級，跳過所有 refresh / pity / 保留
      ├─ Step A：if (lastAutoRefreshTimestamp == 0 || lastAutoRefreshTimestamp > now)
      │     └─ lastAutoRefreshTimestamp ← now    // GDD Case 5.1.1 / 5.1.3
      ├─ Step B：releasedReserveCount = 0
      │     for each c in reservedCandidates.ToList():
      │         if (now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID)):
      │             ReleaseReserveInternal(c)    // 嘗試放回 currentCandidates[c.slotIndex]，丟失保護見 §5.4.5
      │             releasedReserveCount += 1
      ├─ Step C：補刷判定
      │     L = _buildingService.GetBuildingLevel(6)
      │     intervalSec = autoRefreshIntervalSec(L)            // §4.1.3
      │     missedIntervals = floor((now − lastAutoRefreshTimestamp) / intervalSec)
      │     refillCount = clamp(missedIntervals, 0, 1)
      │     appliedRefreshCount = 0
      │     if (refillCount == 1):
      │         ExecuteRefresh(RefreshType.OfflineRefill, currentPoolID)
      │         appliedRefreshCount = 1
      │     else if (currentCandidates.All(IsEmptySlot)):       // 首次解鎖
      │         ExecuteRefresh(RefreshType.OfflineRefill, currentPoolID)
      │         appliedRefreshCount = 1
      ├─ Step D：Phase 2 薪水補發 → Jam 版整段跳過（GDD §3.3.5 / FT-12 §3.5）
      └─ EventBus.Publish(new OnStaffSystemBootEvent(true, appliedRefreshCount, releasedReserveCount, now))
```

#### 5.4.2 ExecuteRefresh 共享流程（GDD §3.3.2 完整對齊）

```
ExecuteRefresh(refreshType, poolID)
  ├─ Step 1：Pre-conditions
  │     ├─ if (!IsStaffSystemUnlocked()) → return STAFF_SYSTEM_LOCKED
  │     ├─ if (refreshType == Manual && _resource.GetCurrentGold() < cost) → return GOLD_INSUFFICIENT
  │     └─ if (refreshType == SwitchPool):
  │           ├─ pool = _tableLoader.GetPool(poolID); if (pool == null) → return POOL_NOT_FOUND
  │           ├─ if (!(pool.minGuildLevel ≤ level ≤ pool.maxGuildLevel)) → return POOL_LEVEL_LOCKED
  │           └─ if (poolID == _playerState.currentPoolID) → return ALREADY_IN_POOL
  ├─ Step 2：解除過期保留（無條件，所有路徑；先於扣費）
  │     for each c in _playerState.reservedCandidates.ToList():
  │         if (now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID)):
  │             ReleaseReserveInternal(c)
  ├─ Step 3：refreshableSlots 計算
  │     N = _tableLoader.GetSlotCount(level)
  │     EnsureCurrentCandidatesSizedTo(N)          // truncate 或 pad placeholder（§7 GDD §5.6）
  │     if (refreshType == SwitchPool):
  │         refreshableSlots = {0..N-1}            // 切池整個重 roll
  │     else:
  │         reservedSlotIndices = { c.slotIndex | c ∈ reservedCandidates AND c.poolID == currentPoolID }
  │         refreshableSlots = {0..N-1} \ reservedSlotIndices
  │     if (refreshableSlots.IsEmpty) → return NO_REFRESHABLE_SLOT  // 不扣費、不變動狀態
  ├─ Step 4：Pre-compute roll（in-memory，任一 throw 不影響 _playerState）
  │     pendingCards = []
  │     pityHitInThisRefresh = false
  │     isFirst = true
  │     for slotIndex in refreshableSlots：
  │         (card, pityHit) = _rollEngine.RollOneSlot(
  │             slotIndex, currentPoolID, isFirstSlotInRefresh = isFirst,
  │             pityCounter = _playerState.pityCounter
  │         )
  │         if (pityHit): _playerState.pityCounter ← 0; pityHitInThisRefresh = true   // §3.3.3 Step 1 內語意
  │         pendingCards.Add(card)
  │         isFirst = false
  ├─ Step 5：扣費（僅 Manual；Step 4 全成功才執行）
  │     if (refreshType == Manual): _resource.AddGold(-cost)
  ├─ Step 6：Commit pendingCards 至 currentCandidates
  │     for card in pendingCards: currentCandidates[card.slotIndex] ← card
  │     if (refreshType == SwitchPool): _playerState.currentPoolID ← poolID
  ├─ Step 7：pityCounter 累加
  │     _playerState.pityCounter += refreshableSlots.Count
  │     // 註：Step 4 命中時已將 pityCounter ← 0；累加後實際值 = 0 + refreshableSlots.Count
  ├─ Step 8：時間戳（僅 Auto / OfflineRefill）
  │     if (refreshType == Auto || refreshType == OfflineRefill): lastAutoRefreshTimestamp ← now
  ├─ Step 9：FT-10 標 dirty（不立即寫入；節流由 FT-10 決定）
  └─ Step 10：refresh 不發事件（GDD §3.3.7）
  return SUCCESS
```

#### 5.4.3 RollOneSlot（純函式，GDD §3.3.3）

```
GachaRollEngine.RollOneSlot(slotIndex, poolID, isFirstSlotInRefresh, pityCounter)
  ├─ Step 1：保底判定
  │     shouldForce5Star = isFirstSlotInRefresh
  │                      AND (pityCounter ≥ tuning.PITY_THRESHOLD)
  │                      AND (poolEligibleByRarity(poolID, 5).Count > 0)
  │     if (shouldForce5Star):
  │         rolledRarity = 5
  │         pityHit = true                       // 由呼叫端 reset
  │         GOTO Step 3
  ├─ Step 2：稀有度 roll（§4.1.5 動態歸一化）
  │     emptyTiers = { r | poolEligibleByRarity(poolID, r).Count == 0 }
  │     normalizationDen = 1 − Σ_{r ∈ emptyTiers} baseProb[r]
  │     for r in {1..5}: effectiveProb[r] = (r ∈ emptyTiers) ? 0 : baseProb[r] / normalizationDen
  │     rolledRarity = WeightedRoll(effectiveProb)
  ├─ Step 3：層內 staff weighted roll（§4.1.6）
  │     tierStaff = { (sID, w) | sID ∈ pool.eligibleStaffIDs AND staffTable[sID].rarity == rolledRarity AND w > 0 }
  │     rolledStaffID = WeightedRoll(tierStaff)
  ├─ Step 4：Trash detection（§3.5.3，僅 1★）
  │     if (rolledRarity == 1 AND Random.value < tuning.TRASH_ROLL_RATE_AT_RARITY_1):
  │         trashItemID = WeightedRoll(_tableLoader.GetTrashItems())  // 等權或 drawWeight 待 DS 確認
  │         rolledStaffID = 0
  │     else:
  │         trashItemID = 0
  └─ Step 5：建立 CandidateCard
        return (CandidateCard {
            poolID, slotIndex, staffID = rolledStaffID, trashItemID,
            rolledRarity, rolledTimestamp = now,
            isReserved = false, reservedTimestamp = 0, reserveConsumedFlag = false
        }, pityHit)
```

#### 5.4.4 TryRecruit（GDD §3.7.3 transaction 邊界）

```
P-02.OnRecruitClick
  → GachaService.TryRecruit(slotIndex)
      ├─ Step 1：Pre-conditions
      │     ├─ if (!IsStaffSystemUnlocked()) → return STAFF_SYSTEM_LOCKED
      │     ├─ if (slotIndex < 0 || slotIndex ≥ currentCandidates.Count) → return INVALID_SLOT
      │     ├─ candidate = currentCandidates[slotIndex]
      │     ├─ if (candidate.IsEmptySlot()) → return EMPTY_SLOT
      │     └─ if (candidate.trashItemID > 0) → return CANDIDATE_NOT_HIREABLE
      ├─ Step 2：冪等保護
      │     if (_isHiringInFlight) → return BUSY
      │     _isHiringInFlight = true
      │     try:
      ├─ Step 3：呼叫 FT-12（同步）
      │         hireResult = _staffService.HireStaff(candidate)
      ├─ Step 4：處理回應
      │         switch (hireResult.result):
      │             case OK:
      │                 currentCandidates[slotIndex] ← MakeEmptySlot(currentPoolID, slotIndex)
      │                 // FT-10 標 dirty；refresh 不發事件，OnStaffHired 由 FT-12 發
      │                 return SUCCESS
      │             case STAFF_SYSTEM_LOCKED:
      │                 LogError("FT-08/FT-12 unlock state out-of-sync")
      │                 return STAFF_SYSTEM_LOCKED          // candidate 不變
      │             case INVALID_STAFF_ID:
      │                 LogError($"CandidateCard.staffID={candidate.staffID} not in StaffTable")
      │                 return INVALID_STAFF_ID
      │             case ROSTER_FULL:
      │                 return ROSTER_FULL
      │             case DUPLICATE_INSTANCE:
      │                 LogError("FT-12 returned DUPLICATE_INSTANCE; _nextInstanceID counter corruption suspected")
      │                 return INTERNAL_ERROR                  // 對齊 FT-12 GDD line 268 enum 5 值
      │             default:
      │                 return INTERNAL_ERROR                   // 防衛性：未知 enum 值
      └─ finally:
              _isHiringInFlight = false
```

#### 5.4.5 ReleaseReserveInternal（GDD §3.3.5 + §5.6）

```
GachaService.ReleaseReserveInternal(c)
  ├─ Step 1：從 reservedCandidates 移除 c
  ├─ Step 2：c.isReserved = false; c.reservedTimestamp = 0; c.reserveConsumedFlag = true
  ├─ Step 3：嘗試放回 currentCandidates[c.slotIndex]
  │     N = _tableLoader.GetSlotCount(level)
  │     if (c.poolID != _playerState.currentPoolID):
  │         // 玩家保存時在另一池 → 卡丟失（合法行為，GDD §3.3.5 Step B 規範）
  │         return
  │     if (c.slotIndex >= N):                              // GDD §5.6 降等情境
  │         // fallback 至「找第一個 empty slot 放回」
  │         emptyIdx = currentCandidates.FindIndex(IsEmptySlot)
  │         if (emptyIdx < 0):
  │             LogWarning("FT-08: ReleaseReserveInternal slot 已滿且 slotIndex 越界，強制丟棄該卡")
  │             return                                       // reserveConsumedFlag 已設 true 防防刷
  │         c.slotIndex = emptyIdx
  │     if (!IsEmptySlot(currentCandidates[c.slotIndex])):
  │         // 原 slot 已被新 roll 覆蓋 → 玩家走 TryReleaseReserve 入口時應回 SLOT_OCCUPIED_BY_NEW_ROLL；
  │         // 內部 ReleaseReserveInternal 由過期掃描呼叫，覆蓋情境直接丟棄
  │         return
  │     currentCandidates[c.slotIndex] = c
```

> `TryReleaseReserve(reserveIndex)` 為玩家主動入口；先檢查 `SLOT_OCCUPIED_BY_NEW_ROLL`（原 slot 非 empty），通過後呼叫 `ReleaseReserveInternal`。

#### 5.4.6 TryReserveCandidate / TryRejectCandidate / TrySwitchPool（簡化）

```
TryReserveCandidate(slotIndex)
  ├─ pre-conditions（lock / INVALID_SLOT / EMPTY_SLOT）
  ├─ if (currentCandidates[slotIndex].reserveConsumedFlag) → RESERVE_CONSUMED
  ├─ if (reservedCandidates.Count ≥ MaxReserve(N)) → RESERVE_FULL
  ├─ c = currentCandidates[slotIndex]
  ├─ c.isReserved = true; c.reservedTimestamp = now
  ├─ reservedCandidates.Add(c)
  ├─ currentCandidates[slotIndex] = MakeEmptySlot(currentPoolID, slotIndex)
  └─ FT-10 標 dirty；不發事件；return SUCCESS

TryRejectCandidate(slotIndex)
  ├─ pre-conditions（lock / INVALID_SLOT / EMPTY_SLOT）
  ├─ currentCandidates[slotIndex] = MakeEmptySlot(currentPoolID, slotIndex)
  └─ FT-10 標 dirty；不發事件；return SUCCESS

TrySwitchPool(newPoolID)
  ├─ pre-conditions（lock / POOL_NOT_FOUND / POOL_LEVEL_LOCKED / ALREADY_IN_POOL）
  └─ ExecuteRefresh(SwitchPool, newPoolID)
        // ExecuteRefresh 內已處理 currentPoolID commit、refreshableSlots = {0..N-1}、pityCounter += N
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `StaffGachaPoolTable.csv` | `poolID`, `poolName`, `minGuildLevel`, `maxGuildLevel`, `eligibleStaffIDs`, `staffWeights`, `reserveTimeLimitSec`, `storyFlagRequired`, `factionIDRequired`, `minReputation`, `eventStartTimestamp`, `eventEndTimestamp` | `【FT-08-DS】staff-gacha-pool-table.md`（_待建_） | 池架構 / 主閘 / staffWeights / 預留閘驗證 | F-01 DataManager 啟動時（Bootstrap 前） |
| `StaffRefreshCostTable.csv` | `guildLevel` (PK), `cost`, `interviewSlotCount` | `【FT-08-DS】staff-refresh-cost-table.md`（_待建_） | 手動刷新費 + 面試欄張數 N | 同上 |
| `StaffRarityProbTable.csv` | `rarity` (PK 1~5), `prob` (= `baseProbability`) | `【FT-08-DS】staff-rarity-prob-table.md`（_待建_） | 稀有度基礎機率 baseProb | 同上 |
| `TrashItemTable.csv` | `trashItemID` (PK), `name`, `flavorText`, `iconAssetID` | `【FT-08-DS】trash-item-table.md`（_待建_） | 1★ 層 trash items 池 | 同上 |
| `StaffTuning.csv` | FT-08 認養 keys：`PITY_THRESHOLD`, `TRASH_ROLL_RATE_AT_RARITY_1`, `MIN_AUTO_REFRESH_INTERVAL_SEC`, `INTERVIEW_AUTO_REFRESH_INTERVAL_L1`~`L5`, `INTERVIEW_SLOT_COUNT_L1`~`L5`, `MAX_RESERVE_FALLBACK` | `【FT-08-DS】staff-tuning.md`（_待建，FT-08 / FT-12 共用） | 系統常數（不寫入 SystemConstants.csv） | 同上 |
| `StaffTable.csv` | `staffID`, `rarity`, ... | `【FT-12-DS】staff-table.md`（_待建_，FT-12 owner） | FT-08 反查 `candidate.staffID → rarity` 驗證、roll 後比對；不寫入 | 同上 |
| `SystemConstants.csv` | `OFFLINE_MAX_SECONDS` | `【F-01-DS】system-constants.md` | 邊緣：離線 > 7 天時補刷仍 clamp 至 1 次（GDD §5.1） | 同上 |

> **GDD §3.3.6 對齊**：`StaffRefreshCostTable.guildLevel` PK 連續 1~5，缺行 fail-fast；`cost ≥ 0`；`1 ≤ interviewSlotCount ≤ 5`。
> **GDD §3.5.2 對齊**：`TrashItemTable.trashItemID` 不可與任一 `StaffTable.staffID` 衝突（DataManager 跨表驗證）。

### 6.2 引用的 ScriptableObject

無。FT-08 全資料來自 CSV；不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `PITY_THRESHOLD`（保底閾值，預設 60） | `StaffTuning.csv` key=`PITY_THRESHOLD` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `TRASH_ROLL_RATE_AT_RARITY_1`（1★ trash 機率，預設 0.30） | `StaffTuning.csv` key=`TRASH_ROLL_RATE_AT_RARITY_1` | 同上 |
| `MIN_AUTO_REFRESH_INTERVAL_SEC`（最短間隔下限，預設 3600） | `StaffTuning.csv` key=`MIN_AUTO_REFRESH_INTERVAL_SEC` | 同上 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1`~`L5`（自動刷新間隔，預設 86400~21600） | `StaffTuning.csv` keys=`INTERVIEW_AUTO_REFRESH_INTERVAL_L1`..`L5` | 同上 |
| `INTERVIEW_SLOT_COUNT_L1`~`L5`（面試欄張數參考） | `StaffRefreshCostTable.csv.interviewSlotCount`（正規來源）；`StaffTuning.csv` 為向下兼容備份 | 同上 |
| `MAX_RESERVE_FALLBACK`（N=1 時 maxReserve fallback，預設 1） | `StaffTuning.csv` key=`MAX_RESERVE_FALLBACK` | 同上 |
| `cost`（手動刷新費，per guildLevel） | `StaffRefreshCostTable.csv.cost` | 同上 |
| `N`（interviewSlotCount，per guildLevel） | `StaffRefreshCostTable.csv.interviewSlotCount` | 同上 |
| `baseProb[1..5]`（稀有度基礎機率） | `StaffRarityProbTable.csv.prob` | 同上 |
| `eligibleStaffIDs` / `staffWeights`（每池可抽 staff + 權重） | `StaffGachaPoolTable.csv` | 同上 |
| `reserveTimeLimitSec`（保留時限，per pool） | `StaffGachaPoolTable.csv.reserveTimeLimitSec` | 同上 |
| `[minGuildLevel, maxGuildLevel]`（池主閘） | `StaffGachaPoolTable.csv` | 同上 |
| `auto refresh 階梯 buildingInterval(L)` | `BuildingTable[(6, L)].effectValue`（FT-07 owner；複合主鍵 `(buildingID, level)`，欄位 `effectValue` 單位為秒） | 同上（消費端引用，owner = FT-07） |
| `staffReduction`（職員 effect 加成） | `FT-12.GetRecruitRefreshReductionSec()`（runtime API；其上限由 FT-12 §4.1 管控） | 同上（消費端引用，owner = FT-12） |
| `OFFLINE_MAX_SECONDS`（離線時間上限） | `SystemConstants.csv` key=`OFFLINE_MAX_SECONDS` | 同上 |
| `OwnerKey = "ft08Gacha"`（不可調，§7.4） | 程式碼常數；列入「不可調參數硬寫」清單，非寫死違規 | 不適用（見 GDD §7.4） |

> **不可調參數**（程式碼常數，GDD §7.4 明定）：池切換副作用（覆蓋 currentCandidates）、OwnerKey、trash 計入 pityCounter。這三項非寫死違規，列入此處備案。

---

## 7. 邊緣案例對策（Edge Case Handling）

對齊 GDD §5.1 ~ §5.6 共 18 條邊緣案例，逐項給出對策。

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1 系統時鐘倒退（now < lastAutoRefreshTimestamp） | `OnStaffSystemBoot` Step A 與 §4.1.4 補刷判定皆使用 `Mathf.Max(0L, now - lastAutoRefreshTimestamp)`；負數 clamp 至 0 → `missedIntervals = 0`、refillCount = 0；同時 Step A 將 `lastAutoRefreshTimestamp ← now` 修正後續基準 | `GachaService` | EditMode：mock TimeSystem 回傳 `lastAutoRefreshTimestamp + 1` 然後再回 `now − 1000`，驗證不爆 |
| §5.1 `lastAutoRefreshTimestamp == 0`（首次 Boot） | Step A 將其設為 `now`；Step C 因 `currentCandidates.All(IsEmptySlot)` 觸發補刷一次 | `GachaService` | EditMode（AC-2） |
| §5.1 離線 > `OFFLINE_MAX_SECONDS`（7 天） | `missedIntervals` 即使再大，`refillCount = clamp(missedIntervals, 0, 1)` 仍只 1 次（GDD §4.1.4） | `GachaService` | EditMode（AC-5） |
| §5.2 玩家在 candidate 還沒 roll 完時點 TryRecruit | UI 防呆為 P-02 責任；API 收 `INVALID_SLOT` 或 `EMPTY_SLOT` 拒絕 | `GachaService` | EditMode |
| §5.2 TryRecruit 時 FT-12 回 `STAFF_SYSTEM_LOCKED` | `TryRecruit` Step 4 不變動 currentCandidates、LogError、回 `STAFF_SYSTEM_LOCKED` 給 P-02；玩家可重試 | `GachaService` | EditMode（AC-14） |
| §5.2 TryRecruit 時 FT-12 回 `INVALID_STAFF_ID` | 不變動 currentCandidates、LogError、回 `INVALID_STAFF_ID`；玩家可手動 `TryRejectCandidate` 清掉壞卡 | `GachaService` | EditMode |
| §5.2 切池時手動刷新 cooldown 還在（如有未來新增） | Jam 版手動刷新無 cooldown；切池本身不影響任何計時器（GDD §3.3.1） | `GachaService` | 文件對齊；無 cooldown 故不需測試 |
| §5.3 pityCounter 超過閾值但 candidates 已被保留 | `RollOneSlot` 對 `refreshableSlots[0]` 套保底（`isFirstSlotInRefresh = true`）；`refreshableSlots[0]` 不一定是 slotIndex 0 | `GachaRollEngine`、`GachaService` | EditMode：保留 slot 0 後手動刷，驗證 5★ 落在 slot 1 |
| §5.3 跨 session 後 pityCounter 從 SaveData 還原 | `RestoreFromSave` 反序列化 `_playerState.pityCounter`；驗證 `≥ 0`，否則拋 `StaffPlayerStateValidationException` | `GachaService` | EditMode（AC-21） |
| §5.3 Debug Reset Pity Counter | `#if UNITY_EDITOR internal void DebugResetPityCounter() { _playerState.pityCounter = 0; } #endif`；release build 完全不編譯 | `GachaService` | Editor menu 手動 |
| §5.4 `StaffGachaPoolTable.minGuildLevel > maxGuildLevel` | `GachaTableLoader` 載入時 LogError + 跳過該行；不阻斷其他池載入 | `GachaTableLoader` | EditMode（AC-17） |
| §5.4 `StaffGachaPoolTable` 5 預留閘任一非預設值（Jam 版） | `GachaTableLoader` 拋 `StaffGachaPoolTableValidationException`；DataManager fail-fast | `GachaTableLoader` | EditMode（AC-16） |
| §5.4 `StaffRefreshCostTable[guildLevel]` 缺行 | `GachaTableLoader` 載入時拋 `StaffRefreshCostTableValidationException("missing guildLevel={n}")`；整檔回退（critical） | `GachaTableLoader` | EditMode |
| §5.4 `StaffRarityProbTable.csv` 機率總和 ≠ 1.0 | `GachaTableLoader` LogWarning；runtime `RollOneSlot` 走動態歸一化（baseProb 重新分配，§4.1.5 仍有效） | `GachaTableLoader`、`GachaRollEngine` | EditMode（AC-18） |
| §5.4 `TrashItemTable.csv` 為空 | `RollOneSlot` Step 4 在 `_tableLoader.GetTrashItems().Count == 0` 時直接 fallthrough（trash 不發生）；1★ 層全部 staff（GDD §3.5.3） | `GachaRollEngine` | EditMode |
| §5.5 未解鎖時呼叫 API | 全部 `Try*` API 入口檢測 `IsStaffSystemUnlocked() == false` → 回 `STAFF_SYSTEM_LOCKED`（GDD §3.6.1） | `GachaService` | EditMode（AC-1） |
| §5.5 解鎖瞬間 | `OnStaffSystemBoot` 重新觸發；補刷一次填滿 N 張 | `GachaService` | PlayMode（AC-2） |
| §5.5 降級後再解鎖 | `_playerState` 全保留（GDD §3.6.4）；下次 Boot 走 Step B 過期保留掃描 + Step C 補刷 | `GachaService` | EditMode |
| §5.6 `currentCandidates[slotIndex]` 中 `slotIndex >= N`（公會降級後 N 縮小） | `OnStaffSystemBoot` Step C 補刷判定前先 `EnsureCurrentCandidatesSizedTo(N)`：truncate 至 N、溢出 slot 直接丟棄、LogWarning | `GachaService` | EditMode（手動模擬降等） |
| §5.6 `reservedCandidates[*].slotIndex >= N` | `ReleaseReserveInternal` 內 fallback 至「找第一個 empty slot 放回」；無 empty slot 則 LogWarning + 強制丟棄 + `reserveConsumedFlag = true` | `GachaService` | EditMode |
| §5.6 玩家主動 `TryReleaseReserve` 時 slotIndex >= N | 同上 fallback；若全滿則回 `SLOT_OCCUPIED_BY_NEW_ROLL` 給玩家 | `GachaService` | EditMode |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 Player State & CandidateCard schemas（含 placeholder card / 驗證規則 / 生命週期表） | §5.3、§6.1 | 對齊 | placeholder card 機制以 `IsEmptySlot` / `MakeEmptySlot` helper 實現；`(staffID > 0) XOR (trashItemID > 0)` 驗證在 `RestoreFromSave` 與 `RollOneSlot` 雙路 |
| §3.2 StaffGachaPoolTable Schema（含 5 預留閘驗證 / Category-agnostic） | §6.1、§7 | 對齊 | 5 預留閘驗證實作於 `GachaTableLoader`，runtime 不消費 |
| §3.3.1 觸發路徑分類（Auto / Manual / SwitchPool / OfflineRefill） | §5.4.2 ExecuteRefresh | 對齊 | `RefreshType` enum 4 值；ExecuteRefresh 集中處理差異 |
| §3.3.2 ExecuteRefresh 共享流程（10 step） | §5.4.2 | 對齊 | Step 1~10 偽碼逐步映射（Step 4 pre-compute → Step 5 扣費 → Step 6 commit） |
| §3.3.3 RollOneSlot 流程 | §5.4.3 | 對齊 | 純函式抽到 `GachaRollEngine`；保底命中由呼叫端 reset |
| §3.3.4 公開 API 合約（6 API + 回傳碼） | §5.1 | 對齊 | 每 API 對應 enum；回傳碼語意表完整 |
| §3.3.5 OnStaffSystemBoot 離線補刷流程 | §5.4.1 | 對齊 | Step A~D 對齊；Step D 整段 Jam 版跳過 |
| §3.3.6 StaffRefreshCostTable Schema | §6.1 | 對齊 | 缺行 fail-fast 於 `GachaTableLoader` |
| §3.3.7 事件發布原則 | §2.5、§5.2 | 對齊 | refresh / 玩家動作不發事件；唯一事件 `OnStaffSystemBoot` |
| §3.4 保底機制細節（生命週期 / 命中流程 / 未觸發路徑 / 動態歸一化互動 / 跨 session / Debug Reset） | §5.4.3、§7（pity 三案例） | 對齊 | Debug Reset 以 `#if UNITY_EDITOR` 包整 method |
| §3.5 垃圾物品（職責邊界 / TrashItemTable Schema / Trash roll 機率 / Filler vs Trash 差異） | §5.4.3、§6.1、§7 | 對齊 | trash 計入 pity 與 §3.5.5 一致；Filler staff 不在 FT-08 持有（屬 FT-12） |
| §3.6 系統降級行為總表 | §5.1、§5.2、§7（§5.5 三案例） | 對齊 | 全部 `Try*` 走 `STAFF_SYSTEM_LOCKED`；`OnStaffSystemBoot` 仍發布；不訂閱 `OnBuildingUpgraded`（lazy 檢測） |
| §3.7 事件契約（payload / 訂閱者 / TryRecruit transaction / EventBus 隔離） | §5.2、§5.3、§5.4.4 | 對齊 | `_isHiringInFlight` 冪等保護；FT-12 回 STAFF_SYSTEM_LOCKED / INVALID_STAFF_ID 路徑 LogError + 不變動 candidate |

### 8.2 公式對齊或替代說明

對齊 GDD §4.1.1 ~ §4.1.9 共 9 條公式，全部直接採用偽碼實作，**未替代**：

| 公式 | 實作位置 | 對齊摘要 |
| --- | --- | --- |
| §4.1.1 N(guildLevel) = `StaffRefreshCostTable[guildLevel].interviewSlotCount` | `GachaTableLoader.GetSlotCount(level)` | 直接表查 |
| §4.1.2 refreshCost(guildLevel) | `GachaTableLoader.GetRefreshCost(level)` | 直接表查 |
| §4.1.3 autoRefreshIntervalSec(L) = `max(MIN_..., buildingInterval - staffReduction)` | `GachaService.AutoRefreshIntervalSec()` | runtime 組合 FT-07 / FT-12 / tuning |
| §4.1.4 missedIntervals = `floor((now - last) / intervalSec)`；refillCount = clamp(0,1) | `GachaService.OnStaffSystemBoot` Step C | 直接偽碼 |
| §4.1.5 動態歸一化 effectiveProb | `GachaRollEngine.RollOneSlot` Step 2 | 預載 baseProb；emptyTiers 由 `poolEligibleByRarity` 計算 |
| §4.1.6 層內 staff weighted roll | `GachaRollEngine.RollOneSlot` Step 3 | 直接偽碼；`staffWeights[i] = 0` 跳過 |
| §4.1.7 pityCounter += refreshableSlots.Count | `ExecuteRefresh` Step 7 | 對齊（每張 +1） |
| §4.1.8 shouldForce5Star = `pity ≥ TH AND poolEligibleByRarity(p, 5) ≠ ∅ AND isFirst` | `GachaRollEngine.RollOneSlot` Step 1 | 對齊；pool 5★ 空時不 reset、不觸發 |
| §4.1.9 maxReserve = `max(1, N - 1)` | `GachaService.MaxReserve()` | 對齊；N=1 時用 `MAX_RESERVE_FALLBACK = 1` |

### 8.3 未能實現的規則與修改建議

正向 FSD（無既有 Script），無實作偏差。以下為 review 過程中辨識的**建議事項**：

| ID | 內容 | 影響 | 處理狀態 |
| --- | --- | --- | --- |
| B-01 | FT-08-DS 5 份 Data-Specs 尚未建立（`StaffGachaPoolTable` / `StaffRefreshCostTable` / `StaffRarityProbTable` / `TrashItemTable` / `StaffTuning`） | 不阻礙 FSD 通過；阻礙 CSV 實際撰寫 | 待處理：Sprint 1 開工前由 DS-designer 統一補齊；FSD §6.1 / §2.2 已標 _待建_ |
| B-02 | FT-12-DS `staff-table.md` 尚未建立 | 不阻礙 FT-08 FSD；阻礙 `candidate.staffID` 反查驗證的 schema 對齊 | 待處理：由 FT-12 FSD 撰寫者帶入 |
| B-03 | `TrashItemTable` 是否帶 `drawWeightTier1~5` 欄位（GDD §3.5 systems-index 註記提及，但 §3.5.2 schema 未列） | 影響 `RollOneSlot` Step 4 的 trash weighted roll 實作（等權 vs 加權） | 待處理：寫 FT-08-DS 時與 game-designer 確認；Jam 版 fallback 為等權 roll |
| B-04 | `MIN_AUTO_REFRESH_INTERVAL_SEC` 在 GDD §4.1.3 變數表存在但 §6.5 / §7.2 共享常數表缺登記 | 文件不一致；不影響實作（FSD 已將其登記在 `StaffTuning.csv`） | 待處理：建議 FT-08 GDD §6.5 補一行；本 FSD §6.1 / §6.3 已主動登記 |
| B-05 | `MAX_RESERVE_FALLBACK` 在 GDD §6.5 / §7.2 共享常數表登記為 `MAX_RESERVE_FALLBACK = 1`，但實作只在 `N == 1` 特例下使用 | 邏輯等價（`max(1, N - 1)` 在 `N=1` 時 = 1，不依賴 `MAX_RESERVE_FALLBACK`） | 已說明：視為「N=1 時 fallback 值的明示常數」，仍由 `StaffTuning.csv` 載入；無實作分歧 |
| B-06 | `OnStaffSystemBoot` 事件名稱與 payload struct 命名（`OnStaffSystemBootEvent`）的最終命名應與 EventBus 既有事件命名一致（`On{Subject}{Verb}Event`） | 命名約定 | 已決定：採 `OnStaffSystemBootEvent` 作為 struct，事件透過 `EventBus.Publish<OnStaffSystemBootEvent>` 發 |
| B-07 | `CandidateCard.reserveConsumedFlag` 在 placeholder card 為 false（GDD §3.1 placeholder 規範），但生命週期表「下次 refresh 刷掉」一行寫「整張卡消失（含 reserveConsumedFlag）」 | 兩者不衝突：reserveConsumedFlag 隨 card 消失（被新 roll 覆蓋）；但若該卡未被新 roll 覆蓋而僅是手動釋放，flag 保留 | 已對齊：FSD `ReleaseReserveInternal` 手動釋放後 flag = true 並嘗試放回 slot |
| B-08 | GDD §3.3.5 註解「Step D：薪水補發 ⚠️ Phase 2，Jam 版整段跳過」連續寫了兩次 `FT-12 FT-12 FT-12 §3.5.4` / `§3.4.3`（疑似筆誤） | 文件可讀性，非衝突 | 待處理：FSD §5.4.1 統一寫一次；建議 FT-08 GDD 下次 patch 修正筆誤 |
| **C-1** | `/design-review FT-08-FSD`（2026-04-28）發現 FSD §2.3 / §5.4.1 引用 `_buildingService.GetBuildingState(6).currentLevel`，但 FT-07 實際提供 `GetBuildingLevel(int buildingID) → int`（FT-07 GDD §3.4 / FT-07 FSD §5.1）；FT-08 GDD §6.1 自身亦誤標 | 阻礙實作（Codex 找不到 `GetBuildingState` API） | **已修**：FSD §2.3 / §5.4.1 全文替換為 `_buildingService.GetBuildingLevel(6)`；§8.4 對 FT-08 GDD §6.1 加回註建議統一命名 |
| **C-2** | 同上 review C-2：FSD §6.3 嚴禁寫死清單寫 `BuildingTable[6].levelEffects[L].refreshInterval`，但 FT-07 真實 schema 為 `BuildingTable[(buildingID, level)].effectValue`（複合主鍵單表，欄位名 `effectValue`，FT-07 GDD §3.5 / FT-07 FSD §6.1） | 阻礙實作（Codex 找不到 `levelEffects[L]` 結構） | **已修**：FSD §6.3 改為 `BuildingTable[(6, L)].effectValue` |
| **C-3** | `/design-review` 發現 FSD §5.4.4 TryRecruit switch 列 4 個 case + default，未顯式列 FT-12 GDD line 268 的 `DUPLICATE_INSTANCE` enum 值 | 行為已 cover（落 default → INTERNAL_ERROR），但 FSD 應顯式對齊 FT-12 5 個 enum 值 | **已修**：FSD §5.4.4 補 `case DUPLICATE_INSTANCE → LogError + return INTERNAL_ERROR` |
| **C-4** | `/design-review` 發現 FSD §4.4 描述「7 個 query API」與 §5.1 實列 8 個不一致 | 純筆誤 | **已修**：FSD §4.4 改為「8 個 query API」 |
| **C-5** | `/design-review` 發現 FSD §1.3 條目 12「以上 23 項 AC」表述模糊（GDD §8 共 25 項 AC） | 文件可讀性 | **已修**：FSD §1.3 改為「AC-1 ~ AC-23 共 23 項以 EditMode test 驗證；AC-24 / AC-25 走 PlayMode Profiler；總計 25 項 AC 全覆蓋」 |

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-28 | `【FT-08】gacha-system.md` | §6.1（上游依賴表 FT-07 row） | FSD 回註建議：FT-07 row 寫 `GetBuildingState(buildingID=6).currentLevel`，但 FT-07 實際提供的是 `GetBuildingLevel(int) → int`（FT-07 GDD §3.4 / FT-07 FSD §5.1）。GDD §3.3.5 line 430 已用正確的 `FT07.GetBuildingLevel(6)`；建議 §6.1 同步改為 `GetBuildingLevel(6)` 以與 §3.3.5 統一，避免實作端誤導。本回註僅建議寫入 GDD，不阻礙 FSD 通過。 |

> 上述回註屬建議性，由主體覆核後決定是否實際寫入 FT-08 GDD。B-04（GDD §6.5 補登 `MIN_AUTO_REFRESH_INTERVAL_SEC`）/ B-08（GDD §3.3.5 `FT-12 FT-12 FT-12` 筆誤）兩條為次要文件可讀性問題，留待下次 GDD patch 一併處理。

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |

無真實衝突。

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥（對應 GDD / Data-Specs / 撰寫者 / Review 者 / 狀態 / 最近更新）
- [x] §1.3 完成目標 12 條皆可被 EditMode / PlayMode test 或 Profiler 驗證
- [x] §2.1~§2.5 GDD 章節 / Data-Specs / 上游（8 項） / 下游（4 項） / 事件契約四向皆列舉
- [x] §3.3 對映表覆蓋 8 項幻想／目的（含 GDD §2 兩條核心情緒、§1 三段系統職責、§3 多池 / 等級 / 解鎖 / 離線 4 個延伸感知）
- [x] §4 Script 清單 5 個 Script 欄位齊全（路徑 / SRP / 依賴 / 預估規模），未拆分理由列出
- [x] §5 API（13 + Editor + ISaveable）/ 事件（1 出 / 不訂閱）/ 資料結構（3 class + 1 struct + enum）/ 資料流（5 段偽碼）齊備
- [x] §6 CSV 引用 7 表含對應 Data-Specs；§6.3 嚴禁寫死清單 16 條對齊原則第 9 條
- [x] §7 邊緣案例 21 條覆蓋 GDD §5.1~§5.6 全部
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度共 13 個小節
- [x] §8.2 公式對齊 9 條全部「直接採用」；§8.3 8 條建議項；§8.4 / §8.5 如實登記「無」
- [x] FSD-index §6.1 / §6.2 / §7.1 / §7.2 已同步更新（見對應更新紀錄）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh） | 通過 | 通過 | 通過 | 正向 FSD（無既有 Script）；FSD 未拆分（5 Script：GachaTypes / IGachaService / GachaTableLoader / GachaRollEngine / GachaService，預估 1170~1460 行）；GDD §3.1~§3.7 全部「對齊」；GDD §4.1.1~§4.1.9 公式直接採用偽碼（未替代）；GDD §5.1~§5.6 共 21 條邊緣案例皆有對策、涉及 Script、驗證方式；無真實衝突；建議項 B-01（5 份 FT-08-DS 待建）/B-02（FT-12-DS 待建）/B-03（TrashItemTable.drawWeightTier* 欄位確認）/B-04（GDD §6.5 補登 `MIN_AUTO_REFRESH_INTERVAL_SEC`）/B-05（`MAX_RESERVE_FALLBACK` 只在 N=1 fallback）/B-06（事件 struct 命名）/B-07（reserveConsumedFlag 生命週期細節）/B-08（GDD §3.3.5 筆誤）皆不阻礙實作；待主體複核後轉「已完成」 |
| 2026-04-28 | Claude Code 主體（Opus 4.7 + xhigh，`/design-review` patch 後重 review） | 通過 | 通過 | 通過 | 對應 `/design-review FT-08-FSD` NEEDS REVISION 全修：C-1（§2.3 / §5.4.1 `GetBuildingState(6).currentLevel` → `GetBuildingLevel(6)`；對齊 FT-07 真實 API）/ C-2（§6.3 `BuildingTable[6].levelEffects[L].refreshInterval` → `BuildingTable[(6, L)].effectValue`；對齊 FT-07 真實欄位）/ C-3（§5.4.4 TryRecruit switch 補 `DUPLICATE_INSTANCE` case；對齊 FT-12 GDD line 268 的 5 個 enum）/ C-4（§4.4 「7 個 query API」→「8 個 query API」）/ C-5（§1.3 條目 12 wording 改清晰）已落地。§8.3 補 5 條 patch 紀錄並標「已修」；§8.4 新增對 FT-08 GDD §6.1 的回註建議（建議 GDD §6.1 將 `GetBuildingState` 同步改為 `GetBuildingLevel`，與 §3.3.5 統一）。本次 patch 不影響 §3.3 對映表 / §4 Script 規劃 / §5.3 資料結構 / §7 邊緣案例對策；僅修正引用上游 API 的命名與 schema 欄位名。 |
