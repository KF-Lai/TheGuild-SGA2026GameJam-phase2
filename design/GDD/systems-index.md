# 系統索引（Systems Index）

_建立時間：2026-04-19_
_狀態：草稿_

---

## 系統總覽

本專案採用**資料驅動架構**：所有遊戲內容由 CSV 表格控制，程式碼作為通用引擎讀取資料並執行規則。系統間透過 ID 互相引用，修改內容只需改資料表，不需改程式碼。

**資料來源**：Google Sheets（[Drive 資料夾](https://drive.google.com/drive/u/2/folders/15YHlbfqaza7F4DPXLlIdb30zW0TiGVk-)）→ 匯出 CSV → `Assets/Resources/Data/Tables/`

---

## 系統列舉（25 個系統，FT-11 為佔位）

### Foundation 層（零依賴）

| ID   | 系統                    | 說明                                               | GDD 狀態  | 對應資料表                                    |
| ---- | ----------------------- | -------------------------------------------------- | --------- | --------------------------------------------- |
| F-01 | **DataManager**         | CSV 載入、表格快取、ID 查詢 API、隨機池工具        | ✅ 已設計 | — （基礎設施）                                |
| F-02 | **Time System**         | 即時計時、離線時間差、每日重置（00:00）            | ✅ 已設計 | `SystemConstants`                             |
| F-03 | **Resource Management** | 金幣（可負值）、聲望（-100~100）統一管理、破產警告 | ✅ 已設計 | `SystemConstants`, `BankruptcyThresholdTable` |

### Core 層（依賴 Foundation）

| ID   | 系統                      | 說明                                                        | GDD 狀態  | 對應資料表                                                                                    |
| ---- | ------------------------- | ----------------------------------------------------------- | --------- | --------------------------------------------------------------------------------------------- |
| C-01 | **Mission Database**      | 任務模板、難度（F~SSS）、類型、報酬、時長；D-02 文字 Facade | ✅ 已設計 | `MissionTemplate`, `MissionTypeTable`, `MissionCategoryTable`, `RewardTable`, `DurationTable` |
| C-02 | **Adventurer Management** | 冒險者實例、階級（F~S）、狀態管理、名冊                     | ✅ 已設計 | `AdventurerTemplate`, `RecruitCostTable`                                                      |
| C-03 | **Profession System**     | 7 種職業定義、擅長/弱點、成功率修正                         | ✅ 已設計 | `ProfessionTable`                                                                             |
| C-04 | **Race System**           | 種族定義、屬性修正（如精靈調查+10%）                        | ✅ 已設計 | `RaceTable`, `ProfessionRacePool`                                                             |
| C-05 | **Trait System**          | 個性特質、行為/數值影響、隨機抽取群組                       | ✅ 已設計 | `TraitTable`, `TraitGroupTable`                                                               |
| C-06 | **World Danger System**   | 5 階全局壓力、時間閘+進度閘+陣營閘、任務池偏移              | ✅ 已設計 | `WorldDangerTable`, `MissionPoolWeights`, `DebtLimitTable`                                    |

### Feature 層（依賴 Core）

| ID    | 系統                       | 說明                                                           | GDD 狀態  | 對應資料表                                |
| ----- | -------------------------- | -------------------------------------------------------------- | --------- | ----------------------------------------- |
| FT-01 | **Adventurer Recruitment** | 新手自薦（自動刷新）、老手邀請（費用+聲望）                    | ✅ 已設計 | `RecruitCostTable`                        |
| FT-02 | **Mission Dispatch**       | 推薦機制、rankDiff、成功率/死亡率公式                          | ✅ 已設計 | `SuccessRateTable`, `DeathRateTable`      |
| FT-03 | **NPC Decision System**    | willingness 公式、接受/拒絕、閒置自主接單                      | ✅ 已設計 | `SystemConstants`                         |
| FT-04 | **Outcome Resolution**     | 5 種結算結果、condition 救活、聲望變化、事件發布               | ✅ 已設計 | `ReputationDeltaTable`, `SystemConstants` |
| FT-05 | **Guild Gold Flow**        | 公會全金流統一執行：預收、結算、維護費、薪水；破產狀態轉移快照 | ✅ 已設計 | `SystemConstants`                         |
| FT-06 | **Guild Core**             | 公會等級（Lv1~5）、聲望門檻、可接難度上限、Game Over 流程、公會基礎狀態 | ✅ 已設計 | `GuildLevelTable`                         |
| FT-07 | **Guild Building System**  | 6 棟可升級建築、雙軌閘門（金幣+聲望）、效果值 API（名冊/並行任務/刷新間隔/破產倒數） | ✅ 已設計 | `BuildingTable`                           |
| FT-08 | **Guild Staff System**     | 多池抽卡面試（A/B 池）、保底、保留、3 種職員效果聚合、每日薪水管線 | ✅ 已設計（Jam 版主結構：§1~§2 / §3.1~§3.2 / §3.6 / §4~§8 完成；§3.3~§3.5 + §3.7~§3.13 待補） | `StaffTable`, `StaffGachaPoolTable`, `StaffRefreshCostTable`, `StaffRarityProbTable`, `TrashItemTable`, `StaffTuning`, `StaffPlayerState` |
| FT-09 | **Faction Story System**   | styleTag 累積、陣營路線、劇情節點                              | 待設計    | `FactionRouteTable`, `StoryNodeTable`     |
| FT-10 | **Save/Load System**       | Unity 本地存檔、離線計算                                       | 待設計    | —                                         |
| FT-11 | **Offline Resolver**       | 離線期間 NPC 自主接單追認、結算回補、金流批次補算              | 待設計（Jam 範疇外，佔位） | `SystemConstants`（`OFFLINE_MAX_SECONDS`） |

### Presentation 層（UI）

| ID   | 系統                           | 說明                                   | GDD 狀態 | 對應資料表             |
| ---- | ------------------------------ | -------------------------------------- | -------- | ---------------------- |
| P-01 | **Desktop Transparent Window** | Windows API 透明視窗、點擊穿透、前置   | 待設計   | —                      |
| P-02 | **Main UI Framework**          | 委託板、名冊、公會總覽、招募等畫面管理 | 待設計   | `UIText`               |
| P-03 | **Notification System**        | 結算通知、事件提醒、桌面推播           | 待設計   | `NotificationTemplate` |

### Data 層（內容資料庫）

| ID   | 系統                           | 說明                       | GDD 狀態 | 對應資料表            |
| ---- | ------------------------------ | -------------------------- | -------- | --------------------- |
| D-01 | **Character Content Database** | 冒險者名字池、背景故事模板 | 待設計   | `NamePool`, `BioPool` |
| D-02 | **Mission Content Database**   | 任務名稱池、描述模板       | 待設計   | `MissionNamePool`     |

---

## 依賴圖

```
Foundation 層
┌─────────────┐  ┌─────────────┐  ┌──────────────────┐
│  F-01       │  │  F-02       │  │  F-03            │
│  DataManager│  │  Time System│  │  Resource Mgmt   │
└──────┬──────┘  └──────┬──────┘  └────────┬─────────┘
       │                │                  │
       ▼                ▼                  ▼
Core 層（全部依賴 F-01 DataManager）
┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐
│ C-01       │ │ C-02       │ │ C-03       │ │ C-04       │ │ C-05       │ │ C-06       │
│ Mission DB │ │ Adv Mgmt   │ │ Profession │ │ Race       │ │ Trait      │ │ World      │
│            │ │            │ │            │ │            │ │            │ │ Danger     │
└─────┬──────┘ └─────┬──────┘ └─────┬──────┘ └─────┬──────┘ └─────┬──────┘ └─────┬──────┘
      │              │              │              │              │              │
      ▼              ▼              ▼              ▼              ▼              ▼
Feature 層
┌──────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                      │
│  FT-01 Recruitment ──────► C-02, C-03, C-04, C-05 (生成冒險者)                      │
│  FT-02 Dispatch ─────────► C-01, C-02, C-03, C-04, C-05 (計算成功率)                │
│  FT-03 NPC Decision ────► FT-02 (讀取 willingness 計算結果)                         │
│  FT-04 Outcome ──────────► FT-02, F-03 (結算 + 更新資源)                            │
│  FT-05 Guild Gold Flow ──► FT-02, FT-04, F-03, FT-07, FT-08                         │
│                            (預收 + 結算 + 維護費 + 薪水，走 AddGoldAllowBankruptcy)   │
│  FT-06 Guild Core ──────► F-03 (聲望門檻判定)                                       │
│  FT-07 Guild Building ──► FT-06 (聲望閘), F-03 (金幣扣款)；提供容量 API 給 FT-01/FT-02 │
│  FT-08 Guild Staff ─────► FT-06 (主閘 minGuildLevel), FT-07 (職員休息室 maxLevel=5  │
│                            驅動自動刷新間隔)；發布 OnStaffSalaryDue → FT-05 扣款；   │
│                            提供 GetRecruitRefreshReductionSec → FT-01 招募刷新       │
│                            提供 GetStaffWillingnessBonus → FT-03                     │
│                            提供 GetAccountantCommissionBonus / Penalty → FT-05       │
│                            提供 GetSuccessRatePreviewFlag → P-02 委託板              │
│  FT-09 Faction Story ───► FT-04, C-01 (結算觸發 + styleTag)                        │
│  FT-10 Save/Load ───────► ALL (序列化所有系統狀態)                                   │
│  FT-11 Offline Resolver ► F-02, FT-02, FT-03, FT-04, FT-05（未來 / Jam 範疇外）       │
│                                                                                      │
└──────────────────────────────────────────────────────────────────────────────────────┘
      │
      ▼
Presentation 層
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ P-01         │  │ P-02         │  │ P-03         │
│ Desktop      │  │ Main UI      │  │ Notification │
│ Window       │  │ Framework    │  │ System       │
└──────────────┘  └──────────────┘  └──────────────┘
```

### 關鍵依賴路徑（Critical Path）

```
DataManager → Mission DB ──┐
DataManager → Adv Mgmt ────┤
DataManager → Profession ──┼──► Dispatch → NPC Decision → Outcome → Guild Gold Flow
DataManager → Race ────────┤
DataManager → Trait ───────┘
Time System ──────────────────► Outcome（計時完成觸發結算）、NPC Decision（OnMinuteTick）
Resource Mgmt ────────────────► Guild Gold Flow（AddGoldAllowBankruptcy、GetCurrentGold）
World Danger ─(push)──────────► Resource Mgmt（Start / 升階時 SetBankruptcyThreshold；F-03 不反向依賴 C-06）
```

**無循環依賴** ✅

---

## 資料表架構（CSV Table Schema）

### 系統常數表（SystemConstants.csv）

| key                            | value   | description                                                   |
| ------------------------------ | ------- | ------------------------------------------------------------- |
| GOLD_INITIAL                   | 100     | 玩家起始金幣                                                  |
| GOLD_MAX                       | 9999999 | 金幣上限                                                      |
| COMMISSION_RATE                | 0.20    | 成功傭金比例                                                  |
| PENALTY_RATE                   | 0.10    | 失敗賠償比例                                                  |
| DEATH_AVERSION                 | 0.5     | NPC 死亡迴避係數                                              |
| ACCEPTANCE_THRESHOLD           | 0.25    | NPC 接單意願門檻                                              |
| WILLINGNESS_JITTER             | 0.10    | NPC 意願隨機波動                                              |
| DAILY_FREE_REFRESH             | 1       | 每日免費手動刷新次數                                          |
| REFRESH_COST                   | 150     | 額外手動刷新費用                                              |
| DAILY_RESET_HOUR               | 0       | 每日重置時間（24hr）                                          |
| OFFLINE_MAX_SECONDS            | 604800  | 離線時間上限（秒），超過截斷                                  |
| FACTION_NEUTRAL_ID             | 0       | neutral 陣營的保留 ID，factionID 等於此值時不計入任何陣營分數 |
| ESCORT_TYPE_ID                 | 2       | 護送任務的 typeID 保留值，程式以此判斷護送約束（限 D~A 難度） |
| RECRUIT_POOL_SIZE              | 4       | 候選池每批人數                                                |
| ESCORT_DURATION_MULTIPLIER_MIN | 3.0     | 護送任務時長倍率下限                                          |
| ESCORT_DURATION_MULTIPLIER_MAX | 5.0     | 護送任務時長倍率上限                                          |
| REPUTATION_MIN                 | -100    | 聲望下限                                                      |
| REPUTATION_MAX                 | 100     | 聲望上限                                                      |
| STRONG_TYPE_BONUS              | 0.20    | 擅長任務類型成功率加成                                        |
| WEAK_TYPE_PENALTY              | 0.15    | 弱點任務類型成功率懲罰                                        |
| WOUNDED_RECOVERY_HOURS         | 6       | Wounded 冒險者的恢復等待時間（小時）                          |

> **FT-08 Guild Staff 系統常數獨立分表**：FT-08 的 `EFFECT_MAX_*`（聚合上限：`EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC`）、`PITY_THRESHOLD`、`REALLOCATING_AUTO_LEAVE_SECONDS`、`BUILDING_SWITCH_COOLDOWN_SECONDS`、`ROSTER_CAP`、`TRASH_ROLL_RATE_AT_RARITY_1` 等專屬常數**不在此表**，皆收錄於 `StaffTuning.csv`（見下節 § 參數表）。理由：避免 SystemConstants 因 FT-08 子系統爆量、保持核心常數表精簡可讀，並讓 FT-08 自帶可調表與 GDD §7.2 對齊。

### 參數表（Per-System）

| 表格名稱                   | PK               | 主要欄位                                                                               | 引用                                                      |
| -------------------------- | ---------------- | -------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| `ProfessionTable`          | professionID     | name, description, strongType, weakType                                                | —                                                         |
| `RaceTable`                | raceID           | name, description, statModifiers (JSON)                                                | —                                                         |
| `TraitTable`               | traitID          | name, description, effectType, effectTarget, effectValue                               | —                                                         |
| `TraitGroupTable`          | groupID          | groupName, traitIDs (CSV list), pickCount, pickMode                                    | traitID                                                   |
| `ProfessionRacePool`       | professionID     | raceIDs (CSV list), weights (CSV list)                                                 | professionID, raceID                                      |
| `ProfessionTraitPool`      | professionID     | traitGroupIDs (CSV list)                                                               | professionID, groupID                                     |
| `AdventurerTemplate`       | templateID (int) | name, rank, professionID, raceID, fixedTraits, factionID, isUnique                     | professionID, raceID, traitID, FactionRouteTable          |
| `RecruitCostTable`         | rank             | cost, reputationReq                                                                    | —                                                         |
| `MissionTemplate`          | missionID (int)  | difficulty, typeID, factionID, categoryID                                              | MissionTypeTable, MissionCategoryTable, FactionRouteTable |
| `MissionTypeTable`         | typeID (int)     | typeName                                                                               | —                                                         |
| `MissionCategoryTable`     | categoryID (int) | categoryName                                                                           | —                                                         |
| `RewardTable`              | difficulty       | baseReward                                                                             | —                                                         |
| `DurationTable`            | difficulty       | baseDuration                                                                           | —                                                         |
| `SuccessRateTable`         | rankDiff         | successRate                                                                            | —                                                         |
| `DeathRateTable`           | difficulty       | baseDeathRate                                                                          | —                                                         |
| `ReputationDeltaTable`     | difficulty       | successDelta, failDelta                                                                | —                                                         |
| `GuildLevelTable`          | level            | title, reputationThreshold, maxDifficulty                                              | —                                                         |
| `BuildingTable`            | (buildingID, level) | name, maxLevel, effectValue, upgradeCost, guildLevelReq                             | —                                                         |
| `StaffTable`               | staffID (int)    | name, rarity, salary, severancePay, isFiller, factionID, effectIDs, effectValues, slotBuildingIDs, uiFlagIDs, uiFlagBuildingIDs（見 FT-08 §3.2.1） | buildingID, FactionRouteTable                             |
| `StaffGachaPoolTable`      | poolID (int)     | poolName, eligibleStaffIDs (CSV list), minGuildLevel, maxGuildLevel, reserveTimeLimitSec, 5 個預留閘欄位（見 FT-08 §3.2.2） | staffID, FT-06 guildLevel                                  |
| `StaffRefreshCostTable`    | refreshCount     | manualRefreshCost（見 FT-08 §4.1.2） | —                                                         |
| `StaffRarityProbTable`     | rarity (1~5)     | baseProbability（見 FT-08 §4.1.5） | —                                                         |
| `TrashItemTable`           | trashItemID (int) | name, description, drawWeightTier1~5（見 FT-08 §3.5） | —                                                         |
| `StaffTuning`              | key              | value（FT-08 專屬常數：EFFECT_MAX_*, PITY_THRESHOLD, REALLOCATING_AUTO_LEAVE_SECONDS, BUILDING_SWITCH_COOLDOWN_SECONDS, ROSTER_CAP；見 FT-08 §7.2） | —                                                         |
| `WorldDangerTable`         | dangerLevel      | name, timeThreshold, missionCountReq, minDifficulty, factionScoreReq                   | —                                                         |
| `MissionPoolWeights`       | dangerLevel      | weightF_E, weightD, weightC, weightB, weightA, weightS_SSS                             | —                                                         |
| `BankruptcyThresholdTable` | reputationMin    | reputationMax, warningDurationSec                                                      | —                                                         |
| `FactionRouteTable`        | factionID (int)  | name, description, threshold                                                           | —                                                         |
| `StoryNodeTable`           | nodeID           | factionID, nodeIndex, triggerCondition, missionID, dialogueKey                         | factionID                                                 |
| `ReputationLabelTable`     | labelID          | name, minReputation, maxReputation                                                     | —                                                         |

### 文字表（Standalone Text Tables）

| 表格名稱               | PK         | 主要欄位                                          | 用途           |
| ---------------------- | ---------- | ------------------------------------------------- | -------------- |
| `NamePool`             | nameID     | firstName, lastName, gender, raceID               | 冒險者隨機名字 |
| `BioPool`              | bioID      | raceID, professionID, template (含 {name} 等變數) | 冒險者背景故事 |
| `MissionNamePool`      | nameID     | difficulty, type, missionName, missionDesc        | 任務名稱描述   |
| `UIText`               | key        | zhTW, en                                          | UI 固定文字    |
| `DialogueTable`        | dialogueID | speakerId, context, text                          | 對話內容       |
| `NotificationTemplate` | templateID | eventType, titleTemplate, bodyTemplate            | 通知模板       |

---

## 開發優先序（Game Jam 時程）

### MVP（開發 Day 1-3）— 核心循環可玩

**設計順序**（依賴由上而下）：

| 順序 | 系統                         | 理由                               |
| ---- | ---------------------------- | ---------------------------------- |
| 1    | F-01 DataManager             | 所有系統的基礎，CSV 載入 + ID 查詢 |
| 2    | F-02 Time System             | 計時與離線計算                     |
| 3    | F-03 Resource Management     | 金幣/聲望管理                      |
| 4    | C-01 Mission Database        | 任務模板載入                       |
| 5    | C-03 Profession System       | 職業定義（Race/Trait 可延後）      |
| 6    | C-02 Adventurer Management   | 冒險者實例管理                     |
| 7    | FT-02 Mission Dispatch       | 派遣 + 成功率計算                  |
| 8    | FT-03 NPC Decision System    | 接受/拒絕判定                      |
| 9    | FT-04 Outcome Resolution     | 結算                               |
| 10   | FT-05 Guild Gold Flow        | 金流（委託 + 維護費 + 薪水）       |
| 11   | FT-01 Adventurer Recruitment | 招募（簡化版：僅新手自薦）         |
| 12   | P-02 Main UI Framework       | 基礎 UI（委託板 + 名冊）           |

### 垂直切片（開發 Day 4-6）— 深度系統

| 順序 | 系統                         | 理由                |
| ---- | ---------------------------- | ------------------- |
| 13   | C-04 Race System             | 種族 + 屬性修正     |
| 14   | C-05 Trait System            | 個性特質 + 隨機群組 |
| 15   | C-06 World Danger System     | 全局壓力            |
| 16   | FT-06 Guild Core             | 公會等級            |
| 17   | FT-07 Guild Building System  | 建設升級            |
| 18   | FT-08 Guild Staff System     | 職員面試            |
| 19   | FT-01 Adventurer Recruitment | 完整版（老手邀請）  |
| 20   | D-01 Character Content DB    | 名字/背景故事池     |
| 21   | D-02 Mission Content DB      | 任務名稱/描述池     |

### Game Jam 完成版（開發 Day 7-8）— 完整體驗

| 順序 | 系統                            | 理由         |
| ---- | ------------------------------- | ------------ |
| 22   | FT-09 Faction Story System      | 陣營劇情     |
| 23   | FT-10 Save/Load System          | 存檔         |
| 24   | P-01 Desktop Transparent Window | 桌面透明視窗 |
| 25   | P-03 Notification System        | 通知         |

### 美術置入 + Debug（Day 9-15）

- 角色立繪替換
- UI 素材置入
- 平衡調整（修改 CSV 即可）
- 最終測試

---

## GDD 設計順序

個別系統 GDD 的撰寫順序（使用 `/design-system`）：

| 優先序 | 系統                                 | 設計依賴                  |
| ------ | ------------------------------------ | ------------------------- |
| **1**  | DataManager + 資料表架構             | 無（定義所有表格 schema） |
| **2**  | Mission Database                     | DataManager schema        |
| **3**  | Profession + Race + Trait            | DataManager schema        |
| **4**  | Adventurer Management                | Profession, Race, Trait   |
| **5**  | Mission Dispatch + NPC Decision      | Mission DB, Adventurer    |
| **6**  | Outcome Resolution + Guild Gold Flow | Dispatch                  |
| **7**  | World Danger                         | Mission DB                |
| **8**  | Guild Core + Building + Staff        | Resource Mgmt             |
| **9**  | Recruitment                          | Adventurer, Guild Core    |
| **10** | Faction Story                        | Outcome, Mission DB       |
| **11** | Save/Load                            | ALL                       |
| **12** | UI Framework                         | ALL gameplay systems      |
| **13** | Desktop Window                       | UI Framework              |

---

## 進度追蹤

| 系統                            | 概念 | GDD | 工項需求書 | 實作 | 測試 |
| ------------------------------- | ---- | --- | ---------- | ---- | ---- |
| F-01 DataManager                | ✅   | ✅  | ✅         | ⬜   | ⬜   |
| F-02 Time System                | ✅   | ✅  | ✅         | ⬜   | ⬜   |
| F-03 Resource Management        | ✅   | ✅  | ✅         | ⬜   | ⬜   |
| C-01 Mission Database           | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| C-02 Adventurer Management      | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| C-03 Profession System          | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| C-04 Race System                | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| C-05 Trait System               | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| C-06 World Danger System        | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-01 Adventurer Recruitment    | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-02 Mission Dispatch          | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-03 NPC Decision System       | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-04 Outcome Resolution        | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-05 Guild Gold Flow           | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-06 Guild Core                | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-07 Guild Building System     | ✅   | ✅  | ⬜         | ⬜   | ⬜   |
| FT-08 Guild Staff System        | ✅   | 🟡 主結構完成（§3.3~§3.5 + §3.7~§3.13 待補）  | ⬜         | ⬜   | ⬜   |
| FT-09 Faction Story System      | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| FT-10 Save/Load System          | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| FT-11 Offline Resolver          | ⬜   | ⬜  | ⬜         | ⬜   | ⬜   |
| P-01 Desktop Transparent Window | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| P-02 Main UI Framework          | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| P-03 Notification System        | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| D-01 Character Content DB       | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
| D-02 Mission Content DB         | ✅   | ⬜  | ⬜         | ⬜   | ⬜   |
