# CSV 資料表總索引

本文件列出專案所有 CSV 資料表的 Owner GDD、系統分類，以及 DataSpec 規格書與實際 CSV 檔案的實作狀態。

> DS 規格書由 `/design-DS` skill 產出，骨架內嵌在 `.claude/skills/design-DS/SKILL.md`；資料來源以 `design/GDD/systems-index.md` §資料表（line 167-213）與各 GDD §3 / §7 章節為準。

---

## DataSpec 命名規則

```
【<系統ID>-DS】<table-name>.md
```

- `<系統ID>` 對映 GDD owner（與 `design/GDD/【<系統ID>】*.md` 一致），例：`F-01` / `C-02` / `FT-08`
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
| `SystemConstants.csv`          | 【F-01】data-manager.md（跨系統 key-value 常數，由各消費者註冊）                               | Foundation / F-01 DataManager        | ✅ `[F-01-DS] system-constants.md`           | 📐     |
| `ReputationLabelTable.csv`     | 【F-03】resource-management.md §3.7 / §4.4 / §6.1 | Foundation / F-03 ResourceManagement | ✅ `[F-03-DS] reputation-label-table.md`   | 📐     |

---

## Core 層（C-01 ~ C-06）

| 表格名稱 | GDD 來源 | 系統分類 | DataSpec 狀態 | CSV 狀態 |
|---|---|---|---|---|
| `MissionTemplate.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | 📐 |
| `MissionTypeTable.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | 📐 |
| `MissionCategoryTable.csv` | 【C-01】mission-database.md §3.1 | Core / C-01 MissionDatabase | 📐 | 📐 |
| `MissionDifficultyTable.csv` | 【C-01】mission-database.md §3.1（合併 baseReward / baseDuration / baseDeathRate / factionScoreDelta；FT-02 §3.2 與 FT-09 §3.2.3 為消費者視角引用） | Core / C-01 MissionDatabase | 📐 | 📐 |
| `AdventurerTemplate.csv` | 【C-02】adventurer-management.md | Core / C-02 AdventurerManagement | 📐 | 📐 |
| `RecruitCostTable.csv` | 【C-02】adventurer-management.md §3.3 + §7.2（FT-01 §7.2 為消費者視角的引用） | Core / C-02 AdventurerManagement | 📐 | 📐 |
| `ProfessionTable.csv` | 【C-03】profession-system.md §3.1 + §7.1（合併 raceIDs / raceWeights / traitGroupIDs；C-04 §3.2 與 C-05 §3.4 為消費者視角引用） | Core / C-03 ProfessionSystem | 📐 | 📐 |
| `RaceTable.csv` | 【C-04】race-system.md §3.1 + §7.1 | Core / C-04 RaceSystem | 📐 | 📐 |
| `TraitTable.csv` | 【C-05】trait-system.md §3.1 + §7.1 | Core / C-05 TraitSystem | 📐 | 📐 |
| `TraitGroupTable.csv` | 【C-05】trait-system.md §3.3 + §7.2 | Core / C-05 TraitSystem | 📐 | 📐 |
| `WorldDangerTable.csv` | 【C-06】world-danger-system.md §3.1 + §7.1~§7.3（單表整合升級閘 / 任務池權重 / 債務上限） | Core / C-06 WorldDangerSystem | 📐 | 📐 |

---

## Feature 層（FT-01 ~ FT-10）

| 表格名稱                            | GDD 來源                                                             | 系統分類                                  | DataSpec 狀態                                 | CSV 狀態 |
| ------------------------------- | ------------------------------------------------------------------ | ------------------------------------- | ------------------------------------------- | ------ |
| `VeteranRankWeightTable.csv`    | 【FT-01】adventurer-recruitment.md §4.4 + §7.3                       | Feature / FT-01 AdventurerRecruitment | ✅ `[FT-01-DS] veteran-rank-weight-table.md` | 📐     |
| `SuccessRateTable.csv`          | 【FT-02】mission-dispatch.md §3.1 + §7.1                             | Feature / FT-02 MissionDispatch       | 📐                                          | 📐     |
| `ReputationDeltaTable.csv`      | 【FT-04】outcome-resolution.md §3.6 + §7.2                           | Feature / FT-04 OutcomeResolution     | 📐                                          | 📐     |
| `GuildLevelTable.csv`           | 【FT-06】guild-core.md §3.5 + §7.1                                   | Feature / FT-06 GuildCore             | 📐                                          | 📐     |
| `BuildingTable.csv`             | 【FT-07】guild-building-system.md §3 + §7.1                          | Feature / FT-07 GuildBuildingSystem   | 📐                                          | 📐     |
| `StaffTable.csv`                | 【FT-08】guild-staff-system.md §3.2.1 + §7.1.1                       | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `StaffGachaPoolTable.csv`       | 【FT-08】guild-staff-system.md §3.2.2 + §7.1.2                       | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `StaffRefreshCostTable.csv`     | 【FT-08】guild-staff-system.md §4.1.2 + §7.1.3                       | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `StaffRarityProbTable.csv`      | 【FT-08】guild-staff-system.md §4.1.5 + §7.1.4                       | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `TrashItemTable.csv`            | 【FT-08】guild-staff-system.md §3.5.2 + §7.1.6                       | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `StaffTuning.csv`               | 【FT-08】guild-staff-system.md §7.2（FT-08 專屬常數，與 SystemConstants 切分） | Feature / FT-08 GuildStaffSystem      | 📐                                          | 📐     |
| `FactionRouteTable.csv`         | 【FT-09】faction-story-system.md §3.2.1 + §7.2                       | Feature / FT-09 FactionStorySystem    | 📐                                          | 📐     |
| `StoryStageTable.csv`           | 【FT-09】faction-story-system.md §3.2.2 + §7.2                       | Feature / FT-09 FactionStorySystem    | 📐                                          | 📐     |

---

## 文字表（Standalone Text Tables）

> 來源：`systems-index.md` §文字表（line 204-213）。文字表用於 i18n 與隨機文本生成，schema 通常較簡單，但仍須補規格書避免欄位飄移。

| 表格名稱 | GDD 來源 | 系統分類 | DataSpec 狀態 | CSV 狀態 |
|---|---|---|---|---|
| `NamePool.csv` | systems-index.md（隨機冒險者名字） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `BioPool.csv` | systems-index.md（冒險者背景故事模板） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `MissionNamePool.csv` | systems-index.md（任務名稱描述池） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `UIText.csv` | systems-index.md（UI 固定文字 i18n） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `DialogueTable.csv` | systems-index.md（對話內容） | Cross-cutting / 文字資料 | 📐 | 📐 |
| `NotificationTemplate.csv` | systems-index.md（通知模板） | Cross-cutting / 文字資料 | 📐 | 📐 |

---

## 歸檔分區（Archived — 已 deprecated 或被合併）

> 本分區紀錄歷史 CSV 表的去處，避免新人誤建。Runtime 不載入、不查詢；保留 DataSpec / GDD 章節僅為設計參考。

| 表格名稱 | 歸檔原因 | 取代來源 | DataSpec 狀態 |
|---|---|---|---|
| `BankruptcyThresholdTable.csv` | Phase 2 已 deprecated（runtime 不查詢） | FT-07 預備金保險櫃透過 `SetBankruptcyWarningDuration` 推送 `_currentWarningDuration` 至 F-03 | ✅ `[F-03-DS] bankruptcy-threshold-table.md`（保留為設計參考） |
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
| Foundation | 2 | 2 | 0 |
| Core | 11 | 0 | 0 |
| Feature | 14 | 1 | 0 |
| 文字表 | 6 | 0 | 0 |
| **總計（active）** | **33** | **3** | **0** |
| 歸檔 | 9 | 1 | — |

> 2026-04-26 合併批次：移除 7 張表（A1 + A2 + B1 + B2），BankruptcyThresholdTable 移到歸檔分區（原計入 Foundation）；總表數 40 → 33。
> 實際 `Assets/Resources/Data/Tables/` 目錄內無任何正式 CSV；現有 `SystemConstants.csv` / `BankruptcyThresholdTable.csv` 僅存在於 `Assets/Tests/EditMode/.../TestResources/` 作為測試 fixture。

---

## 已知歸屬注意事項

1. **SystemConstants.csv** 為跨系統 key-value 表，schema 由 F-01 DataManager 定義 parser，但 key 由各消費者系統註冊；新增 key 時必須同步更新 `[F-01-DS] system-constants.md` §「已註冊 key 清單」。
2. **RecruitCostTable.csv** 在 C-02 §3.3 定義 schema，FT-01 §7.2 從消費者角度引用；DataSpec 規格書應掛在 C-02 owner。
3. **BankruptcyThresholdTable.csv** Phase 2 已 deprecated（移到歸檔分區），runtime 不查詢（`warningDurationSec` 改由 FT-07 預備金保險櫃推送）；保留 DataSpec 為設計參考。
4. **StaffTuning.csv** 是 FT-08 專屬參數表（避免 SystemConstants 因子系統爆量），與 SystemConstants 同為 key-value 結構但載入流程獨立。
5. **ReputationLabelTable.csv** schema 定義於 F-03 §3.7（2026-04-26 補入）；DataSpec 見 `[F-03-DS] reputation-label-table.md`。`label` 欄位文字值 GDD 未指定（gdd-gap），由設計師填入 CSV。
6. **MissionDifficultyTable.csv**（C-01 owner）為 2026-04-26 合併產出：跨 owner（C-01 / FT-02 / FT-09）共用，新增 / 調整欄位時須同步 C-01 §3.1 / FT-02 §3.2 / FT-09 §3.2.3 三處消費端說明。
7. **ProfessionTable.csv**（C-03 owner）為 2026-04-26 擴充：跨 owner（C-03 / C-04 / C-05）共用，調整 `raceIDs` / `raceWeights` / `traitGroupIDs` 欄位時須與 C-04 / C-05 owner 同步意圖。
8. **WorldDangerTable.csv**（C-06 owner）為 2026-04-26 合併產出：單系統內三組欄位整合（升級閘 / 任務池權重 / 債務上限），無跨 owner 協調成本。
