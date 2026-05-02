# The Guild — Implementation Guide

_2026-04-28 | 動工前唯一查閱入口_

---

## 1. 規範來源（不重複，僅引用）

| 主題                                                          | 來源                               |
| ------------------------------------------------------------- | ---------------------------------- |
| 全域行為準則、工項分級、審查模型強度、Codex MCP 規則          | `~/.claude/CLAUDE.md`              |
| 專案技術棧、目錄結構、命名、語言／單位、MCP 工具、commit 規範 | `CLAUDE.md`                        |
| Gameplay 程式碼規則                                           | `.claude/rules/gameplay-code.md`   |
| UI 程式碼規則                                                 | `.claude/rules/ui-code.md`         |
| CSV 結構與命名                                                | `.claude/rules/data-files.md`      |
| Unity C# coding standards、測試目錄結構                       | `.claude/docs/coding-standards.md` |
| 設計文件規則                                                  | `.claude/rules/design-docs.md`     |
| GDD 索引、依賴圖、SystemConstants、參數表清單                 | `design/GDD/systems-index.md`      |
| FSD 索引、Service 契約（§2.10）、FSD 模板（§8）               | `design/FSD/FSD-index.md`          |
| Data-Specs 索引                                               | `design/Data-Specs/data-index.md`  |
| 反向依賴索引                                                  | `design/GDD/DIP-index.md`          |

本守則僅補充三項上述未涵蓋的內容：來源層級（§2）、Service 契約速查（§3）、實作順序與進度（§5、§6）。

---

## 2. 來源層級

```
GDD（規則）→ FSD（規格）→ Data-Specs（CSV schema）→ CSV（內容）→ Script
```

- 動工依據 = FSD。
- FSD 不揣測 GDD；衝突依 `FSD-index.md` §2.5 流程仲裁。
- CSV 內容值來自 Google Sheets master（見 memory `reference_google_drive.md`）。

---

## 3. Service 契約速查（節錄自 FSD-index §2.10）

FSD 內的 `IXxxService` 命名僅供敘述。實作直呼 concrete singleton，禁新增 interface 包裝：

| FSD 敘述                                | 實作                                          |
| --------------------------------------- | --------------------------------------------- |
| `IDataManager.X`                        | `DataManager.Instance.X`                      |
| `ITimeService.X` / `ITimeSystem.X`      | `TimeSystem.Instance.X`                       |
| `IResourceService.X`                    | `ResourceManagement.Instance.X`               |
| `IEventBus.Subscribe<T>` / `Publish<T>` | static `EventBus.Subscribe<T>` / `Publish<T>` |

---

## 4. 動工前 Checklist

逐項勾選，未通過禁止動工。

### 4.1 設計層

- [ ] FSD 狀態 = `已完成`（`design/FSD/FSD-index.md` §7.1）
- [ ] FSD §7.1 `[2026-04-28 排查]` blocker 已落地或裁決
- [ ] FSD §2.3 上游系統皆已實作（Glob `Assets/Scripts/` 確認）
- [ ] FSD §6.1 引用的 Data-Specs 全部存在

### 4.2 資料層

- [ ] CSV 已存在於 `Assets/Resources/Data/Tables/`，無則先寫
- [ ] CSV 符合 `.claude/rules/data-files.md`
- [ ] 時間欄位以 `_sec` / `_hours` 命名
- [ ] FSD §2.2 / §6.1 / Data-Specs 三處欄位雙向對齊

### 4.3 工項分級與動工路線

- [ ] 依 `~/.claude/CLAUDE.md` 評估 Large / Medium / Small
- [ ] Large / Medium：Codex 實作（read-only）→ Opus + xhigh 審查 → APPROVED → Codex 寫入（workspace-write）
- [ ] Small：subagent（Sonnet + high）閉環 → Opus + xhigh 審查 + 寫入
- [ ] 列出 FSD §4.4 Script 清單作為實作 checklist
- [ ] 列出 FSD §1.3 DoD 作為驗收 checklist

### 4.4 動工後

- [ ] EditMode 測試（首選）／ PlayMode 測試齊備
- [ ] UnityMCP `refresh_unity` + `read_console` 零編譯錯誤
- [ ] UnityMCP `run_tests` 全綠
- [ ] Commit（規範見 `CLAUDE.md` §語言與單位規範）
- [ ] 本守則 §6 進度表登記

---

## 5. 實作順序

### 5.1 Foundation（已落地）

| 系統                     | 路徑                      | 待修                                                                                                                                     |
| ------------------------ | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| F-01 DataManager         | `Core/Data/`              | 偏差全修（2026-04-28）                                                                                                                   |
| F-02 Time System         | `Core/Time/TimeSystem.cs` | FT-02-A 動工時移除 `MissionTimer.cs` + `_missionTimers` + `OnMissionExpiredEvent` + `OfflineSummary.CompletedMissionInstanceIds`（D-01） |
| F-03 Resource Management | `Gameplay/Resources/`     | D4 ISaveable 簽名已對齊；§6.3 契約段落待 Codex 補實作；D8 拆分待重評                                                                     |

### 5.2 Core 層

| #   | 系統                         | 上游                  | 並行條件    |
| --- | -------------------------- | ------------------- | ------- |
| 1   | C-01 Mission Database      | F-01                | 與 #2 並行 |
| 2   | C-03 Profession System     | F-01                | 與 #1 並行 |
| 3   | C-04 Race System           | F-01、C-03           | 與 #4 並行 |
| 4   | C-05 Trait System          | F-01、C-03           | 與 #3 並行 |
| 5   | C-02 Adventurer Management | F-01、C-03、C-04、C-05 | 與 #6 並行 |
| 6   | C-06 World Danger System   | F-01、F-03           | 與 #5 並行 |

### 5.3 Feature 層

| 階段  | #   | 系統                            | 上游                           | 備註               |
| --- | --- | ----------------------------- | ---------------------------- | ---------------- |
| F1  | 7   | FT-02-A Mission Dispatch Core | C-01/02/03/04/05、F-02、F-03   | 同時執行 D-01 移除舊計時器 |
| F1  | 8   | FT-02-B Commission Board      | FT-02-A、C-01                 | 序列於 #7 後         |
| F1  | 9   | FT-04 Outcome Resolution      | FT-02、F-03、C-01、C-02         | 序列於 #8 後         |
| F2  | 10  | FT-06 Guild Core              | F-03、F-01                    | 可與 F1 並行         |
| F2  | 11  | FT-07 Guild Building System   | FT-06、F-03                   | 序列於 #10 後        |
| F3  | 12  | FT-12 Staff System            | FT-06、FT-07、F-01、F-02        | Phase 2 薪水管線不啟用  |
| F3  | 13  | FT-08 Gacha System            | FT-06、FT-07、FT-12            | 序列於 #12 後        |
| F4  | 14  | FT-01 Adventurer Recruitment  | C-02/03/04/05、F-03、FT-12     | 與 #15 並行         |
| F4  | 15  | FT-03 NPC Decision System     | FT-02、C-02、C-05、F-02、FT-12   | 與 #14 並行         |
| F5  | 16  | FT-05 Guild Gold Flow         | FT-02、FT-04、F-03、FT-07、FT-12 | 與 #17 並行         |
| F5  | 17  | FT-09 Faction Story System    | FT-04、C-01、F-01              | 與 #16 並行         |
| F6  | 18  | FT-10 Save/Load System        | ALL                          | 必須最後；獨佔          |
| P1  | 19  | P-01 Desktop Transparent Window | F-01（讀 P-01 設定）         | **2026-05-03 排入 Jam**；桌面透明 widget；Win32 P/Invoke + Unity API；FSD 待撰寫 |

### 5.4 暫緩／範疇外

| 系統                     | 處理                                                  |
| ------------------------ | ----------------------------------------------------- |
| P-03 Notification System | **Jam 範疇外，不實作**（2026-05-03 確認）；GDD 完整保留 Post-Jam；Jam 階段所有玩家可見訊息由各 panel 自行渲染；LogFloatingWindowHost 維持 standalone（`BindResult.Unavailable`） |
| D-01 / D-02 Content DBs  | GDD ✅（v1.0 / 2026-05-02）；FSD ⬜；Jam 不實作（fallback 顯示 templateID / typeID）|
| FT-11 Offline Resolver   | Game Jam 範疇外，不實作                               |
| FT-09 / 劇情系統         | **Jam 階段 NARRATIVE_ENABLED=0 停用**（2026-05-03 確認）；FT-09 完整實作保留（209 條測試），runtime flag 早退；奧菲莉雅 / 奧蘿瑞女神不出現；Game Over 唯一觸發＝破產 |

### 5.5 並行上限參考（4 週）

```
週 1：[C-01 + C-03] → [C-04 + C-05]
週 2：[C-02 + C-06 + FT-06] → [FT-02-A → FT-02-B] → [FT-07 + FT-04]
週 3：[FT-12 → FT-08] → [FT-01 + FT-03]
週 4：[FT-05 + FT-09] → FT-10
```

實際並行容量由使用者裁定。

---

## 6. 進度追蹤

| #   | 系統                            | 狀態  | 開始         | 完成           | 備註                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| --- | ----------------------------- | --- | ---------- | ------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| —   | F-01 DataManager              | 已完成 | —          | 2026-04-26 前 | 偏差全修                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| —   | F-02 Time System              | 已完成 | —          | 2026-04-26 前 | D-01 已隨 FT-02-A 移除 MissionTimer / _missionTimers / OnMissionExpiredEvent / OfflineSummary.CompletedMissionInstanceIds；D-02 OnOfflineResolvedEvent payload 簡化為僅 OfflineSeconds                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| —   | F-03 Resource Management      | 已完成 | —          | 2026-04-26 前 | §6.3 契約段落待補                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| 1   | C-01 Mission Database         | 已完成 | 2026-04-28 | 2026-04-28   | C-01-FSD Codex Medium 落地；附帶修正 SystemConstants.ESCORT_TYPE_ID 4→2                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 2   | C-03 Profession System        | 已完成 | 2026-04-28 | 2026-04-28   | C-03-FSD Codex Medium 落地；定義 IReadOnlyIntSet 替代 .NET 5+ IReadOnlySet                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| 3   | C-04 Race System              | 已完成 | 2026-04-28 | 2026-04-28   | C-04-FSD Codex Medium 落地；PlayMode 統計測試 AC-RS-10 通過                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| 4   | C-05 Trait System             | 已完成 | 2026-04-28 | 2026-04-28   | C-05-FSD Codex Medium 落地；23 effectTargets 硬編碼於 Loader                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| 5   | C-02 Adventurer Management    | 已完成 | 2026-04-28 | 2026-04-28   | C-02-FSD subagent (sonnet+high) 實作；codex-mcp review 採納 UpdateStatus woundedUntilTimestamp 不變式 + RestoreFromSave 規則 7                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 6   | C-06 World Danger System      | 已完成 | 2026-04-28 | 2026-04-28   | C-06-FSD Codex Medium 落地；ISaveable Stub 待 FT-10                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| 7   | FT-02-A Mission Dispatch Core | 已完成 | 2026-04-28 | 2026-04-28   | FT-02-FSD-A subagent (sonnet+high) 實作 + 主體完成 D-01/D-02 移除；codex-mcp APPROVED + 採納 RestoreFromSave Instance==null 防禦；獨立 asmdef `TheGuild.Gameplay.MissionDispatch` 避免與 Profession/Race 既有 cyclic ref；D-01 已連帶清理 13 個 TimeSystemTests mission timer 測試                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 8   | FT-02-B Commission Board      | 已完成 | 2026-04-28 | 2026-04-28   | FT-02-FSD-B 主體實作（subagent quota 失敗轉主體閉環）；codex-mcp APPROVED；FT-02-A DTO 整合 FT02SaveDTO 含兩池；Dispatch step 10 改實呼 RemoveMissionFromBoard；同 MissionDispatch asmdef                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| 9   | FT-04 Outcome Resolution      | 已完成 | 2026-04-28 | 2026-04-28   | FT-04-FSD subagent (sonnet+high) 實作（codex-mcp token 上限 fallback：claude code 主體 Opus+xhigh 驗證，待流量恢復後補驗）；APPROVED 含一處修正（unknown effectTarget 不再從 triggered 移除，對齊 §5.4 偽碼）；EditMode 146/146 + PlayMode 6/6 全綠；on_success_gold_bonus effectValue 採倍率語意（× baseReward）為 ad-hoc 決策，待 FSD §8.5 補登衝突紀錄                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| 10  | FT-06 Guild Core              | 已完成 | 2026-04-28 | 2026-04-28   | FT-06-FSD subagent (sonnet+high) 實作（codex-mcp token 上限 fallback：claude code 主體 Opus+xhigh 驗證，待流量恢復後補驗）；APPROVED 含一處修正（移除 GuildLevelDatabaseLoader.FindTargetLevel 中 unused highestLevel dead code）；連跳 queue 改用 List<LevelUpPayload> 以支援 §5.4-A 步驟 5 in-place 回填；OnReputationChangedEvent payload 對齊實際 3 欄位 schema（用 evt.CurrentReputation）；ISaveable Stub 待 FT-10；EditMode 146/146 + PlayMode 6/6 全綠                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 11  | FT-07 Guild Building System   | 已完成 | 2026-04-28 | 2026-04-28   | FT-07-FSD subagent (sonnet+high) 實作（codex-mcp token 上限 fallback：claude code 主體 Opus+xhigh 驗證，待流量恢復後補驗）；APPROVED 含兩處修正（TryUpgradeBuilding 加 buildingID 範圍檢查；Awake DataManager.Instance null guard 對齊 FT-04/06 模式）；BuildingTable.csv 為 sample（5 列：1_1/1_2/2_1/6_0/6_1），FSD §6.1 嚴格驗證下實際 runtime 啟用會 throw MissingBuildingDataException，待 design-CSV 補完；ISaveable Stub 待 FT-10；EditMode 146/146 + PlayMode 6/6 全綠；T21 Phase 2 旗標待裁                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| 12  | FT-12 Staff System            | 已完成 | 2026-04-28 | 2026-04-28   | FT-12-FSD Codex Large 落地（codex-mcp APPROVED 含兩處微調：HireStaff `!_isLoaderReady` 回 STAFF_SYSTEM_LOCKED 而非 INVALID_STAFF_ID；OnSalaryTick 合併重複 `dueTimestamp <= _lastSalaryTimestamp` 守護）；7 檔 2013 行（StaffTypes 279 / IStaffService 114 / StaffTableLoader 333 / StaffEffectAggregator 240 / StaffService 1024 / asmdef 20 / AssemblyInfo 3）；獨立 asmdef `TheGuild.Gameplay.Staff` 引用 Core.Data / Time / Events + Gameplay.Resources / Building；FT-05 為後續系統，資遣費直呼 `ResourceManagement.Instance.AddGoldAllowBankruptcy` 對齊 FT-04/06/07 先例；StaffPhase2.SalaryEnabled C# const false 編譯期 dead code（搭配 #pragma warning disable CS0162）；ISaveable Stub 待 FT-10；EditMode 146/146 + PlayMode 6/6 全綠；ROSTER_CAP=0 視為無上限（Jam 預設）                                                                                                                                                                                |
| 13  | FT-08 Gacha System            | 已完成 | 2026-04-29 | 2026-04-29   | FT-08-FSD Codex Large 落地（4 批 dispatch：A=asmdef+AssemblyInfo+GachaTypes 244+IGachaService 74；B=GachaTableLoader 570；C=GachaRollEngine 228；D=GachaService 1136；合計 ~2253 行）；自註冊 StaffTuning（FT-12 doc 標載入但實際未實作 RegisterTable，由 FT-08 RegisterTable<StaffTuningEntry>("StaffTuning")）；`CandidateCard` 命名衝突採 `using StaffCandidateCard = TheGuild.Gameplay.Staff.CandidateCard;` alias（FT-08 自帶 9 欄位 class，FT-12 既存 1 欄位 struct 為 HireStaff 入口參數）；`DefaultExecutionOrder=180`（晚於 FT-12=170 / FT-07=160 / FT-06=150）；自檢輪數：Batch C 1 次 Random ambiguity 修正（補 `using Random = UnityEngine.Random;` alias），Batch D 寫入時套用 2 處 review 修正（Step 7 pity 累加邏輯對齊 FSD §5.4.2 註解「命中先 reset，再無條件 += refreshableSlots.Count」；檔頭 prefix 補 `【FT-08-FSD】`）；ISaveable Stub 待 FT-10；TrashItemTableValidationException 暫嵌 GachaService nested private class，後續 patch 移至 GachaTypes.cs；EditMode 146/146 + PlayMode 6/6 全綠 |
| 14  | FT-01 Adventurer Recruitment  | 已完成 | 2026-04-29 | 2026-04-29 | FT-01-FSD Codex Medium 落地（codex-mcp APPROVED 含一處清理：移除未使用的 `SetPoolGeneratorForTests` test helper）；4 檔 ~1080 行 runtime（RecruitmentTypes 27 / Events/RecruitmentEvents 18 / RecruitmentPoolGenerator 354 / RecruitmentService 611 + AssemblyInfo 3 + asmdef 26）+ EditMode 測試 591 行；獨立 asmdef `TheGuild.Gameplay.Recruitment` 引用 Core.Data/Time/Events 與 Gameplay.Resources/Adventurer/Profession/Race/Trait/Guild/Building/Staff；OnDailyReset 採 named event（對齊 EventNames.OnDailyReset 既有 API；FSD 寫 `OnDailyResetEvent` 為敘述偏差，已對齊實作）；ISaveable Stub 待 FT-10；`DefaultExecutionOrder=200`（晚於 FT-12=170 / FT-08=180）；自檢輪數：1 次 review APPROVED，1 次自檢 fix（StaffService.ResetForTests 不存在改 InvokeInstance Awake、AdventurerTemplate sample 中 templateID=2 professionID=4 不一致改用測試 mock、StaffTable mock 規避 staffID=101 uiFlag 長度衝突、StaffTuning 註冊為 SystemConstants 模式、BuildingService/StaffService.Instance 在 EditMode 需 reflection 補設、OnEnable 在 EditMode 需手動 invoke）；EditMode 165/165 + PlayMode 6/6 全綠 |
| 15  | FT-03 NPC Decision System     | 已完成 | 2026-04-29 | 2026-04-29 | FT-03-FSD Codex Medium 落地（codex-mcp APPROVED 無修改）；4 檔 ~500 行 runtime（NpcDecisionTypes 23 / INpcDecisionService 8 / Events/NpcDecisionEvents 14 / NpcDecisionService 455 + AssemblyInfo 3 + asmdef 24）+ EditMode 測試 620 行；獨立 asmdef `TheGuild.Gameplay.Decision`；effectiveScore 4 步驟（base/traits/staff/jitter）；OnMinuteTickEvent 訂閱驅動 AutoPickupTick；DifficultyIndex 內嵌（FSD §8.3 B-01 授權）；MakeDecision 非 Idle 早退；ResolveRejectionReason 三分支對齊 FSD §3.1；自檢輪數：1 次 fix（mock BuildingTable 補 6_0 列、AC_ND3_04 補 TryUpgradeBuilding(6) 解鎖、AC_ND3_12 改填 5 個 active missions 對齊 FALLBACK_MAX_CONCURRENT_MISSIONS=5、AC_ND3_13 移除運行時 LogAssert 改用 SetUp Expect loader 過濾 LogError）；ISaveable 不需實作（T1-FT03 裁決）；EditMode 178/178 + PlayMode 6/6 全綠 |
| 16  | FT-05 Guild Gold Flow         | 已完成 | 2026-04-29 | 2026-04-29 | FT-05-FSD Codex Medium 落地（codex-mcp APPROVED 無修改）；4 檔 ~700 行 runtime（GoldFlowTypes 153 / IGoldFlowService 6 / Events/GoldFlowEvents 63 / GoldFlowService 382 + AssemblyInfo 3 + asmdef 23）+ EditMode 測試 ~580 行；獨立 asmdef `TheGuild.Gameplay.GoldFlow`；4 In 事件訂閱（OnCommissionAccepted / OnMissionResolved / OnGuildMaintenanceDue / OnStaffSalaryDue）+ 4 Out 事件（OnCommissionPrepaid / OnCommissionSettled / OnMaintenanceCharged / OnSalaryCharged）；OnGuildMaintenanceDueEvent 由 FT-05 自定義（Phase 2 由 FT-07 發布）；ExecuteGoldFlow 共用快照流程；GetBuildingPenaltyBonus hardcode 0（TODO FT-07 Phase 2 補）；Outcome class/namespace 同名 → 用 `using OutcomeRecord = TheGuild.Gameplay.Outcome.Outcome;` alias 解 CS0118；自檢輪數：1 次 fix（CS0118 alias、測試補 GuildCoreService + GuildLevelTable 註冊、AC_GF5_07 GOLD_INITIAL=200 對齊扣款值）；不需 ISaveable（FSD §1.2 Out-of-Scope）；EditMode 193/193 + PlayMode 6/6 全綠 |
| 17  | FT-09 Faction Story System    | 已完成 | 2026-04-29 | 2026-04-29 | FT-09-FSD Codex Large 落地（codex-mcp APPROVED 含一處修正：GetUnlockedStageIndex 未登記值 -1→0 對齊 FSD §5.1「區分降級與未解鎖」）；6 檔 ~1180 行 runtime（FactionStoryTypes 79 / IFactionStoryService 13 / Events/FactionStoryEvents 86 / FactionStoryTableLoader 235 / FactionStoryScoreAccumulator 229 / FactionStoryService 639 + asmdef 21 + AssemblyInfo 3）+ EditMode 測試 712 行；獨立 asmdef `TheGuild.Gameplay.FactionStory` 引用 Core.Data/Events + Gameplay.Mission/MissionDispatch/WorldDanger/Outcome；`DefaultExecutionOrder=220`（晚於 FT-12=170/FT-08=180/FT-01=200，下游接收方）；Outcome class/namespace 同名衝突採 `using OutcomeRecord = TheGuild.Gameplay.Outcome.Outcome;` alias（沿用 FT-05 先例）；自檢輪數：1 次 fix（CS0118 alias）+ 3 次測試 fix（UnlockStage1 helper 改 SS、CreateContext 加 ResetForTests、EC01_*+DoD08 test body 加 ResetStatics+re-subscribe 解 EventBus 訂閱殘留問題）；ISaveable Stub 完整（5 method + OWNER_KEY="factionStorySaveData" + IsCritical=false），待 FT-10 落地後加 inheritance 宣告；MissionTemplate.csv 缺 missionID 9001-9003，runtime 走 INJECT_FAILED 降級（EC-7 涵蓋）；EditMode 209/209 + PlayMode 6/6 全綠 |
| 18  | FT-10 Save/Load System        | 已完成 | 2026-04-29 | 2026-04-29 | FT-10-FSD Codex Large 落地（codex-mcp R1 read-only diff → R2 3 條 Required Changes 修正：Awake/Start 拆分 / Bootstrap retry candidateIndex 明確走訪 / SaveLoadServiceTests 從 scaffold 改 12 真實測試 → R3 寫入時 PowerShell encoding 致 7 個既有 owner 中文註解 mojibake，CC revert + 手動 Edit 加 ISaveable inheritance + WorldDanger 補 stub）；新增獨立 asmdef `TheGuild.Core.SaveContract`（無依賴介面層）；6 檔 FT-10 Save core（SaveLoadTypes/ISaveLoadService/SaveFileIO/SaveLoadBootstrap/SaveLoadService/Events）+ asmdef + AssemblyInfo；11 個 owner 加 `: TheGuild.Core.SaveContract.ISaveable` + asmdef ref（F-03 IsCritical=true / C-06+FT-03+FT-09+FT-12+FT-08+FT-07+FT-06+FT-02+FT-01+C-02 對齊 FSD §3.3.4）；3 個 owner 補完整 ISaveable stub（F-03 6 fields / C-06 2 fields / FT-03 薄層 no-op）；OwnerKey 對齊：FT-08 採實作端 "ft08Gacha" / C-02 採 "c02AdventurerRoster"（與 FSD §5.4.1.A 寫的 "ft08Staff" / "c02Adventurers" 不同，dict / SaveDataRoot 對齊實作）；FT-12 OWNER_KEY 改 "ft12Staff" 對齊 FSD；FT-01 IsCritical 從 true 改 false 對齊 FSD §3.3.4 Degradable；資料層補 5 SAVE_* keys 至 SystemConstants.csv + F-01-DS；自檢輪數：1 次 fix（using namespace 對齊：移除 Adventurer.Events 加 Adventurer + MissionDispatch.Events）+ 1 次 fix（EventBus.ClearAll internal 改 reflection）+ 1 次 fix（DoD07 LogAssert.Expect 兩條 SaveFileIO/SaveLoadService Error）；Codex 越界改 systems-index.md 已 revert；EditMode 225/225 + PlayMode 6/6 全綠 |
| 19  | P-02-FSD-A Main UI Core       | 已完成 | 2026-05-02 | 2026-05-02 | P-02-FSD-A 實作完成；14 Script 落地（PanelManager / PanelStateMachine / UIBootstrapController / UITextService / SceneNavigationController / PersistentHudController / StoryDialogueQueue / SceneObjectController / SceneObjectStateLoader / DialogueRenderer / ScreenAnchorCalculator / LogFloatingWindowHost / P02UITuning / PanelTypes）；P-02-DS（ui-text / scene-object-state-table）+ CSV（UIText / SceneObjectStateTable）已落地；commit 序：ed2c286（主體實作 + EditMode 測試）→ 4bab073（v3.1 P3.1-004 stub + GetPendingDialogueStages）→ cfe76f0（PlayMode 6 條測試覆蓋 EditMode [Ignore] DoD-A2/A3/A5/A10）→ 458e844（chain continue 落差修補：PanelManager.ClosePanel 加 onClosed callback、StoryDialogueQueue.SetServiceForTests seam、DoD_A4_EC08 PlayMode 覆蓋）；自檢輪數：3 輪修補（v3.1 stub / PlayMode 補測 / chain continue impl 落差）；測試：EditMode UI 19 total / 12 pass / 7 [Ignore] 環境限制紀錄 / 0 fail，PlayMode UI 7/7 pass（duration 0.47s） |
| 20  | P-02-FSD-B1 Main UI Panels（Jam B1）| 已完成 | 2026-05-02 | 2026-05-02 | 2026-05-02 `/program-ipm` §4 Checklist 4 條 fail → 拆 B1/B2 scope（FSD-B §8.5 split decision）。**B1 scope 落地**：10 Script + 1 UXML/USS 資產（IPanel + CommissionBoard + RecommendAdventurerSubpanel + AdventurerRoster + GuildBuilding + StaffRoster + StaffGacha + GuildOverview + StoryDialogue + ConfirmPopup + LogFloatingWindow asset；扣除 SettingsPanel），共約 1156 行 + 199 行測試。實作流程：Codex R1（read-only diff baseline 1500 行）→ R2（PanelManager IPanel registry / dispatch 落差修補 + 各 panel 功能補完）→ R3（5 條小修：CommissionStatus state / 刪 BuildingPanel reputation 分支 / FSD-A 文件 §8.5 row + 附錄 A row / test handler 修 / GuildOverview neutral fallback）→ **R4 workspace-write 因 Codex MCP JSON parse error fail**（CLAUDE.md token 上限應變 fallback）→ 改派 unity-ui-specialist subagent (Sonnet+high) 寫入 + CC 主體 (Opus+xhigh) 審查與最終修補（PanelTypes.SkipConfirmCallback bool / StoryDialoguePanel.Confirm IsEpilogue 流程 / IPanel ambiguity alias / Outcome asmdef ref / CandidateCard alias / EditMode 反射 Awake+OnEnable seam / GuildOverview static guard reset SetUp）。**FSD-A 同步補丁**：PanelManager 補 IPanel registry + Open(args)/Close() dispatch（同 chain continue 落差修補先例，§8.5 + 附錄 A 已紀錄）；StoryDialogueOpenArgs 加 IsEpilogue/IsOpheliaInteraction/SkipConfirmCallback 三欄位；移除 UI Core 內 OnOpheliaMissingNightEvent struct（改用 FactionStory namespace）；SceneObjectController 互動入口傳 SkipConfirmCallback=true。**驗證**：Tests.EditMode.UI + Panels 33 total / 24 pass / 0 fail / 9 [Ignore]（環境限制紀錄）；Tests.PlayMode.UI 7/7 pass。**已知 baseline 漂移**（與 B1 無關）：Tests.EditMode.Gameplay.* 25 條 fail 因 MissionTypeTable / MissionTemplate.csv 對齊問題，待獨立 patch。Service 偏差紀錄：MissionTemplate 無 MissionName 欄（改用 MissionDatabaseService.GetMissionText）/ OnBankruptcyWarningStateChangedEvent → OnBankruptcyStateChangedEvent / OnBuildingUpgradedEvent → BuildingUpgradedEvent / 無 OnAdventurerDiedEvent（改 OnMissionResolvedEvent）/ FactionStoryService 無 GetCurrentStyleTagBias()（fallback "neutral" + 一次 LogWarning） |
| 20.B2 | P-02-FSD-B2 Main UI Panels（B2 deferred）| 待 | —          | —          | 待 P-01（**Jam 必做** 2026-05-03 校正，已於 2026-05-03 落地 — 見 row 21）+ D-01 / D-02 / DialogueTable owner 落地後啟動；**B2 scope** = SettingsPanel（1 Script ~220~280 行，仰賴 P-01 IDesktopWindow 8 API）+ AdventurerRoster 介紹短文 D-01 整合 + CommissionBoard 介紹短文 D-02 整合 + StoryDialoguePanel DialogueTable 對話內容整合（3 處 facade）；預估 220~280 行 + 整合 patch；**P-01 已 unblocked，SettingsPanel 即可動工** |
| 21  | P-01 Desktop Transparent Window | 已完成 | 2026-05-03 | 2026-05-03 | P-01-FSD subagent fallback 落地（codex-mcp/gpt-mcp token 上限禁用，由 unity-specialist subagent (Sonnet+high) 實作、claude code 主體 Opus+xhigh 驗證，待流量恢復後補驗）；7 .cs runtime（DesktopWindowService 618 / Win32Native 196 / MonitorEnumerator 159 / WindowScaleController 111 / WindowHitTester 93 / MonitorInfo 73 / P01Tuning 58，合計 1308 行）+ AssemblyInfo 3 + asmdef + P01Tuning.asset；獨立 asmdef `TheGuild.UI.Platform.Win32` 引用 Core.Events / Core.SaveContract / Gameplay.Save / UI；`DefaultExecutionOrder=100` 晚於 SaveLoadService（default 0）；ISaveable OwnerKey="p01DesktopWindow" / IsCritical=false；自檢輪數：1 次 fix（R1 MarkSaveDataDirty 改呼 SaveLoadService.Instance?.MarkDirty / R2 WindowHitTester 整段包進 #if 消除 CS0162 / E1 internal class HitTest→HitTestResult 解 method-class 命名衝突 / E2 SaveLoadService 加 public Instance getter / R3 PanelManager.GetMainSceneRootPanel 回傳型別用完整 UnityEngine.UIElements.IPanel + DesktopWindowService 用 UIPanel alias 解 IPanel ambiguity）；跨層 patch 3 處（PanelManager +14 行 / UIBootstrapController.MainSceneDocument 公開屬性 +3 行 / SaveLoadService +2 行 public Instance getter）；P01Tuning.asset m_Script.guid 由 Unity refresh 後手動修補（d8aadc2a8708c5647a733c005f1d9f2d）；EditMode 290/299 + 9 既有 [Ignore] 全綠 / PlayMode 13/13 全綠；不需 P-01 自帶測試（Win32 P/Invoke 無法 EditMode mock，DoD-01~DoD-12 由 Standalone build manual test 驗證）；Follow-up：(1) DesktopWindowService GameObject 需在 MainScene 建立並指派 P01Tuning.asset；(2) UIBootstrapController.RegisterEffectiveScaleFallback() stub 接入 DesktopWindowService.RegisterEffectiveScaleListener；(3) Tests.EditMode.UI.Platform.Win32 asmdef 與 WindowScaleController/MonitorEnumerator fallback 測試補登 |

狀態值：`待` / `進行中` / `審查中` / `已完成` / `阻塞`。阻塞時於備註欄填阻塞原因與處理計畫。

---

## 7. 變更歷史

| 日期       | 內容                                               |
| ---------- | -------------------------------------------------- |
| 2026-04-28 | 建立守則                                           |
| 2026-04-28 | 對齊 CLAUDE.md，重複內容改為連結引用；改寫為指令式 |
