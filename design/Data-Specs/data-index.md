# CSV 資料表總索引

本文件列出專案所有 CSV 資料表的 Owner GDD、系統分類，以及 DataSpec 規格書與實際 CSV 檔案的實作狀態。

> DS 規格書由 `/design-DS` skill 產出，骨架內嵌在 `.claude/skills/design-DS/SKILL.md`；資料來源以 `design/GDD/systems-index.md` §資料表（line 167-213）與各 GDD §3 / §7 章節為準。

---

## DataSpec 命名規則

```
【<系統ID>-DS】<table-name>.md
```

- `<系統ID>` 對映 GDD owner（與 `design/GDD/【<系統ID>】*.md` 一致），例：`F-01` / `C-02` / `FT-08` / `FT-12`
- `<table-name>` 為 kebab-case，與實際 CSV 檔名（PascalCase）一一對映
- 全形括號 `【】` 與 GDD 命名對齊；後綴一律 `-DS`（避免與舊有 `-Data` / `-DataSpecs` 混用）

| 規格書檔名 | CSV 檔名 | Owner GDD |
|---|---|---|
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `【F-01】data-manager.md` |
| `【F-03-DS】bankruptcy-threshold-table.md` | `BankruptcyThresholdTable.csv` | `【F-03】resource-management.md` |
| `【FT-01-DS】veteran-rank-weight-table.md` | `VeteranRankWeightTable.csv` | `【FT-01】adventurer-recruitment.md` |

---

## CSV 格式與填寫規範

CSV 結構、符號、特殊值、命名與 ID 型別使用原則，統一定義於 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)（規範來源：`design/GDD/【F-01】data-manager.md` §3.2）。新增或修改 CSV 前先檢視該規則檔。

> **2026-04-26 格式變更**：CSV 從 row-based 改為 **column-based / 轉置格式**（第一列為 PK 列，後續每列為一個欄位的定義列）。所有 DS 範例與測試 fixture 已同步轉置；解析邏輯見 `CsvParser.Parse` / `ParseSystemConstants`。

本索引僅維護表格清單、Owner GDD、系統分類、DataSpec 與 CSV 實作狀態。

---

## 狀態圖例

| 圖示 | 意義 |
|---|---|
| ✅ | 已完成 |
| 🔧 | 實作中 |
| 📐 | 規劃中（尚未撰寫） |
| ⚠️ | 已 deprecated（保留為設計參考） |

---

## Foundation 層（F-01 ~ F-03）

| 表格名稱                           | GDD 來源                                                                        | 系統分類                                 | DataSpec 狀態                                 | CSV 狀態 |
| ------------------------------ | ----------------------------------------------------------------------------- | ------------------------------------ | ------------------------------------------- | ------ |
| `SystemConstants.csv`          | 【F-01】data-manager.md（跨系統 key-value 常數，由各消費者註冊）                               | Foundation / F-01 DataManager        | 📐                                          | ✅     |
| `ReputationLabelTable.csv`     | 【F-03】resource-management.md §3.7 / §4.4 / §6.1 | Foundation / F-03 ResourceManagement | 📐                                          | ✅     |

---

## Core 層（C-01 ~ C-06）

| 表格名稱 | GDD 來源 | 系統分類 | DataSpec 狀態 | CSV 狀態 |
|---|---|---|---|---|
| `MissionTemplate.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | ✅ |
| `MissionTypeTable.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | ✅ |
| `MissionCategoryTable.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | ✅ |
| `MissionDifficultyTable.csv` | 【C-01】mission-database.md §3.1（合併 baseReward / baseDuration / baseDeathRate / factionScoreDelta；FT-02 §3.2 與 FT-09 §3.2.3 為消費者視角引用） | Core / C-01 MissionDatabase | 📐 | ✅ |
| `AdventurerTemplate.csv` | 【C-02】adventurer-management.md | Core / C-02 AdventurerManagement | 📐 | ✅ |
| `RecruitCostTable.csv` | 【C-02】adventurer-management.md §3.3 + §7.2（FT-01 §7.2 為消費者視角的引用） | Core / C-02 AdventurerManagement | 📐 | ✅ |
| `ProfessionTable.csv` | 【C-03】profession-system.md §3.1 + §7.1（合併 raceIDs / raceWeights / traitGroupIDs；C-04 §3.2 與 C-05 §3.4 為消費者視角引用） | Core / C-03 ProfessionSystem | 📐 | ✅ |
| `RaceTable.csv` | 【C-04】race-system.md §3.1 + §7.1 | Core / C-04 RaceSystem | 📐 | ✅ |
| `TraitTable.csv` | 【C-05】trait-system.md §3.1 + §7.1 | Core / C-05 TraitSystem | 📐 | ✅ |
| `TraitGroupTable.csv` | 【C-05】trait-system.md §3.3 + §7.2 | Core / C-05 TraitSystem | 📐 | ✅ |
| `WorldDangerTable.csv` | 【C-06】world-danger-system.md §3.1 + §7.1~§7.3（單表整合升級閘 / 任務池權重 / 債務上限） | Core / C-06 WorldDangerSystem | 📐 | ✅ |

---

## Feature 層（FT-01 ~ FT-10）

| 表格名稱                            | GDD 來源                                                             | 系統分類                                  | DataSpec 狀態                                 | CSV 狀態 |
| ------------------------------- | ------------------------------------------------------------------ | ------------------------------------- | ------------------------------------------- | ------ |
| `VeteranRankWeightTable.csv`    | 【FT-01】adventurer-recruitment.md §4.4 + §7.3                       | Feature / FT-01 AdventurerRecruitment | ✅ `[FT-01-DS] veteran-rank-weight-table.md` | ✅     |
| `SuccessRateTable.csv`          | 【FT-02】mission-dispatch.md §3.1 + §7.1                             | Feature / FT-02 MissionDispatch       | ✅ `[FT-02-DS] success-rate-table.md`        | ✅     |
| `ReputationDeltaTable.csv`      | 【FT-04】outcome-resolution.md §3.6 + §7.2                           | Feature / FT-04 OutcomeResolution     | ✅ `[FT-04-DS] reputation-delta-table.md`   | ✅     |
| `GuildLevelTable.csv`           | 【FT-06】guild-core.md §3.5 + §7.1                                   | Feature / FT-06 GuildCore             | ✅ `[FT-06-DS] guild-level-table.md`        | ✅     |
| `BuildingTable.csv`             | 【FT-07】guild-building-system.md §3 + §7.1                          | Feature / FT-07 GuildBuildingSystem   | ✅ `[FT-07-DS] building-table.md`           | ✅     |
| `StaffTable.csv`                | 【FT-12】staff-system.md §3.2 + §7.1.1（2026-04-26 從原 FT-08 拆出，owner 移交 FT-12）| Feature / FT-12 StaffSystem           | ✅ `[FT-12-DS] staff-table.md`              | ✅     |
| `StaffGachaPoolTable.csv`       | 【FT-08】gacha-system.md §3.2 + §7.1.3                              | Feature / FT-08 GachaSystem           | ✅ `[FT-08-DS] staff-gacha-pool-table.md`   | ✅     |
| `StaffRefreshCostTable.csv`     | 【FT-08】gacha-system.md §3.3.6 + §7.1.1                            | Feature / FT-08 GachaSystem           | ✅ `[FT-08-DS] staff-refresh-cost-table.md` | ✅     |
| `StaffRarityProbTable.csv`      | 【FT-08】gacha-system.md §4.1.5 + §7.1.2                            | Feature / FT-08 GachaSystem           | ✅ `[FT-08-DS] staff-rarity-prob-table.md`  | ✅     |
| `TrashItemTable.csv`            | 【FT-08】gacha-system.md §3.5.2 + §7.1.4                            | Feature / FT-08 GachaSystem           | ✅ `[FT-08-DS] trash-item-table.md`         | ✅     |
| `StaffTuning.csv`               | 【FT-12】staff-system.md §7.2（owner）+ 【FT-08】gacha-system.md §7.2（消費端）；T5 裁決後 owner 統一為 FT-12，FT-08 改標消費端引用 | Feature / FT-12（owner，FT-08 消費端） | ✅ `[FT-12-DS] staff-tuning.md`             | ✅     |
| `FactionRouteTable.csv`         | 【FT-09】faction-story-system.md §3.2.1 + §7.2                       | Feature / FT-09 FactionStorySystem    | ✅ `[FT-09-DS] faction-route-table.md`      | ✅     |
| `StoryStageTable.csv`           | 【FT-09】faction-story-system.md §3.2.2 + §7.2                       | Feature / FT-09 FactionStorySystem    | ✅ `[FT-09-DS] story-stage-table.md`        | ✅     |

---

## Platform 層（P-01 ~ P-03）

| 表格名稱 | GDD 來源 | 系統分類 | DataSpec 狀態 | CSV 狀態 |
|---|---|---|---|---|
| `SceneObjectStateTable.csv` | 【P-02】main-ui-framework.md §3.6.2（v3.1 P3.1-010） | Platform / P-02 MainUIFramework | ✅ `[P-02-DS] scene-object-state-table.md` | ✅ |

---

## 文字表（Standalone Text Tables）

> 來源：`systems-index.md` §文字表（line 204-213）。文字表用於 i18n 與隨機文本生成，schema 通常較簡單，但仍須補規格書避免欄位飄移。

| 表格名稱 | GDD 來源 | 系統分類 | DataSpec 狀態 | CSV 狀態 |
|---|---|---|---|---|
| `NamePool.csv` | 【D-01】character-content-database.md §3.1 | Cross-cutting / D-01 CharacterContentDatabase | ✅ `[D-01-DS] name-pool.md` | 📐 |
| `BioPool.csv` | 【D-01】character-content-database.md §3.2 | Cross-cutting / D-01 CharacterContentDatabase | ✅ `[D-01-DS] bio-pool.md` | 📐 |
| `MissionNamePool.csv` | 【D-02】mission-content-database.md §3.1 | Cross-cutting / D-02 MissionContentDatabase | ✅ `[D-02-DS] mission-name-pool.md` | ✅ |
| `UIText.csv` | 【P-02】main-ui-framework.md §7.3 / §3.9 | Platform / P-02 MainUIFramework（文字表） | ✅ `[P-02-DS] ui-text.md` | ✅ |
| `DialogueTable.csv` | systems-index.md（對話內容） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `NotificationTemplate.csv` | systems-index.md（通知模板） | Cross-cutting / 文字資料 | 📐 | 📐 |

---

## 歸檔分區（Archived — 已 deprecated 或被合併）

> 本分區紀錄歷史 CSV 表的去處，避免新人誤建。Runtime 不載入、不查詢；保留 DataSpec / GDD 章節僅為設計參考。

| 表格名稱 | 歸檔原因 | 取代來源 | DataSpec 狀態 |
|---|---|---|---|
| `BankruptcyThresholdTable.csv` | Phase 2 已 deprecated（runtime 不查詢） | FT-07 預備金保險櫃透過 `SetBankruptcyWarningDuration` 推送 `_currentWarningDuration` 至 F-03 | 📐（保留為設計參考） |
| `RewardTable.csv` | 2026-04-26 合併 | C-01 `MissionDifficultyTable.baseReward` 欄位 | — |
| `DurationTable.csv` | 2026-04-26 合併 | C-01 `MissionDifficultyTable.baseDuration` 欄位 | — |
| `DeathRateTable.csv` | 2026-04-26 合併（owner 由 FT-02 移交 C-01） | C-01 `MissionDifficultyTable.baseDeathRate` 欄位 | — |
| `MissionFactionScoreWeight.csv` | 2026-04-26 合併（owner 由 FT-09 移交 C-01） | C-01 `MissionDifficultyTable.factionScoreDelta` 欄位 | — |
| `MissionPoolWeights.csv` | 2026-04-26 合併 | C-06 `WorldDangerTable.weightF_E` ~ `weightS_SSS` 欄位群 | — |
| `DebtLimitTable.csv` | 2026-04-26 合併 | C-06 `WorldDangerTable.maxDebt` 欄位 | — |
| `ProfessionRacePool.csv` | 2026-04-26 合併（owner C-04） | C-03 `ProfessionTable.raceIDs` / `raceWeights` 欄位（C-04 §3.2 為消費端） | — |
| `ProfessionTraitPool.csv` | 2026-04-26 合併（owner C-05） | C-03 `ProfessionTable.traitGroupIDs` 欄位（C-05 §3.4 為消費端） | — |

---

## 統計

| 分類 | 表格數 | DataSpec ✅ | CSV ✅ |
|---|---|---|---|
| Foundation | 2 | 0 | 2 |
| Core | 11 | 0 | 11 |
| Feature | 13 | 13 | 13 |
| Platform | 1 | 1 | 1 |
| 文字表 | 6 | 4 | 2 |
| **總計（active）** | **33** | **18** | **29** |
| 歸檔 | 9 | 0 | — |

> 2026-04-26 合併批次：移除 7 張表（A1 + A2 + B1 + B2），BankruptcyThresholdTable 移到歸檔分區（原計入 Foundation）；總表數 40 → 33。
> 實際 `Assets/Resources/Data/Tables/` 目錄內無任何正式 CSV；現有 `SystemConstants.csv` / `BankruptcyThresholdTable.csv` 僅存在於 `Assets/Tests/EditMode/.../TestResources/` 作為測試 fixture。

---

## 已知歸屬注意事項

1. **SystemConstants.csv** 為跨系統 key-value 表，schema 由 F-01 DataManager 定義 parser，但 key 由各消費者系統註冊；新增 key 時必須同步更新 `[F-01-DS] system-constants.md` §「已註冊 key 清單」。
2. **RecruitCostTable.csv** 在 C-02 §3.3 定義 schema，FT-01 §7.2 從消費者角度引用；DataSpec 規格書應掛在 C-02 owner。
3. **BankruptcyThresholdTable.csv** Phase 2 已 deprecated（移到歸檔分區），runtime 不查詢（`warningDurationSec` 改由 FT-07 預備金保險櫃推送）；保留 DataSpec 為設計參考。
4. **StaffTuning.csv** 為 FT-12 owner、FT-08 消費端的 key-value 表（2026-04-28 T5 裁決前為 FT-08 / FT-12 共用 owner，現統一為 FT-12 owner）：FT-12 owner key 含 `EFFECT_MAX_*` / `BUILDING_SWITCH_COOLDOWN_SECONDS` / `REALLOCATING_AUTO_LEAVE_SECONDS` / `AUTO_LEAVE_SCAN_INTERVAL_SECONDS`（T18 補登）/ `ROSTER_CAP`；FT-08 消費端 key 含 `PITY_THRESHOLD` / `TRASH_ROLL_RATE_AT_RARITY_1` / `MIN_AUTO_REFRESH_INTERVAL_SEC` / `MAX_RESERVE_FALLBACK` / `INTERVIEW_*` 等 gacha 常數。避免 SystemConstants 因子系統爆量；新增 key 須註明 owner / 消費端歸屬。
9. **StaffTable.csv**（FT-12 owner，2026-04-26 從原 FT-08 拆出）：schema 定義於 FT-12 §3.2；`minGuildLevel` 欄位由 FT-08 gacha 池於 candidate 過濾時消費，FT-12 自身不消費。
10. **FT-08 gacha 專屬表組**（StaffGachaPoolTable / StaffRefreshCostTable / StaffRarityProbTable / TrashItemTable）owner 仍為 FT-08；2026-04-26 拆分後 FT-12 不消費這 4 張表。
5. **ReputationLabelTable.csv** schema 定義於 F-03 §3.7（2026-04-26 補入）；DataSpec 見 `[F-03-DS] reputation-label-table.md`。`label` 欄位文字值 GDD 未指定（gdd-gap），由設計師填入 CSV。
6. **MissionDifficultyTable.csv**（C-01 owner）為 2026-04-26 合併產出：跨 owner（C-01 / FT-02 / FT-09）共用，新增 / 調整欄位時須同步 C-01 §3.1 / FT-02 §3.2 / FT-09 §3.2.3 三處消費端說明。
7. **ProfessionTable.csv**（C-03 owner）為 2026-04-26 擴充：跨 owner（C-03 / C-04 / C-05）共用，調整 `raceIDs` / `raceWeights` / `traitGroupIDs` 欄位時須與 C-04 / C-05 owner 同步意圖。
8. **WorldDangerTable.csv**（C-06 owner）為 2026-04-26 合併產出：單系統內三組欄位整合（升級閘 / 任務池權重 / 債務上限），無跨 owner 協調成本。

---

## v3.1 Patch 影響紀錄（2026-04-30）

奧蘿瑞女神陣營劇本（v3.1）對 CSV 表格影響彙整：

| CSV | 影響類型 | 影響內容 | DataSpec 同步狀態 | CSV 同步狀態 |
|---|---|---|---|---|
| `MissionTemplate.csv` | 新增欄位 + 量產 | v3.1 三新欄位（isScriptedDeath / minDangerLevel / requiredTraitID）；missionID 從 26+3 擴充為 147（既有 1-26 + 9001-9005 陣營劇情 + 8001-8008 進階 + 7001-7008 高難 + 1001-1610 常規 100）| ✅ C-01-DS（P3.1-001） | ✅ 量產合併 |
| `TraitTable.csv` | 新增 trait | traitID=999「沉默」（奧菲莉雅專屬，effectValue=-0.40 突破安全範圍） | ✅ C-05-DS（P3.1-002） | ✅ |
| `SystemConstants.csv` | 新增 5 常數 | LIGHT_THRESHOLD=100 / MIXED_THRESHOLD=40 / OPHELIA_MISSING_RECOVERY_HOURS=12 / OPHELIA_TEMPLATE_ID=901 / STAGE5_MISSION_ID=9005 | ✅ F-01-DS（P3.1-A3） | ✅ |
| `AdventurerTemplate.csv` | 量產替換 | 71 位（含奧菲莉雅 templateID=901）| 📐（DS 未專為 v3.1 patch） | ✅ 量產合併 |
| `StaffTable.csv` | 新增欄位 + 量產 | v3.1 五個 Post-Jam 預埋欄位（personalityDesc / intimacyLevel / isLeavePossible / mood / personalEventIDs）；10 位（含米拉/譚恩/凱拉 + 7 充數） | ✅ FT-12-DS（P3.1-008） | ✅ 量產合併 |
| `StoryStageTable.csv` | 新增欄位 + 預設資料 | v3.1 三新欄位（dialogueVariantMode / specialEventKey / unlockBlockerCondition）；5 行 Stage 1-5 預設資料 | ✅ FT-09-DS（P3.1-004） | ✅ 量產合併 |

**完整 patch summary**：`design/_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md`
**執行紀錄**：`design/_Reports/GDD-FSD-patch-execution-plan-v3.1.md`
**systems-index 登記**：P3.1-aurorae（含 P3.1-001 ~ P3.1-010）

> 待後續實作：DialogueTable.csv（dialogueKey 對應文本，由各 stage / staff / ophelia 文檔規劃）。
