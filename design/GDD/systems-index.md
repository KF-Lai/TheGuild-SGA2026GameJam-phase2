# 系統索引（Systems Index）

_建立時間：2026-04-19_
_狀態：草稿_

---

## 系統總覽

本專案採用**資料驅動架構**：所有遊戲內容由 CSV 表格控制，程式碼作為通用引擎讀取資料並執行規則。系統間透過 ID 互相引用，修改內容只需改資料表，不需改程式碼。

**資料來源**：Google Sheets（[Drive 資料夾](https://drive.google.com/drive/u/2/folders/15YHlbfqaza7F4DPXLlIdb30zW0TiGVk-)）→ 匯出 CSV → `Assets/Resources/Data/Tables/`

---

## 系統列舉（26 個系統，FT-11 為佔位；FT-08 / FT-12 為原職員系統 2026-04-26 拆分）

### Foundation 層（零依賴）

| ID   | 系統                    | 說明                                               | GDD 狀態  | 對應資料表                                    |
| ---- | ----------------------- | -------------------------------------------------- | --------- | --------------------------------------------- |
| F-01 | **DataManager**         | CSV 載入、表格快取、ID 查詢 API、隨機池工具        | ✅ 已設計 | — （基礎設施）                                |
| F-02 | **Time System**         | 即時計時、離線時間差、每日重置（00:00）            | ✅ 已設計 | `SystemConstants`                             |
| F-03 | **Resource Management** | 金幣（可負值）、聲望（-100~100）統一管理、破產警告 | ✅ 已設計 | `SystemConstants`, `BankruptcyThresholdTable` |

### Core 層（依賴 Foundation）

| ID   | 系統                      | 說明                                                        | GDD 狀態  | 對應資料表                                                                                    |
| ---- | ------------------------- | ----------------------------------------------------------- | --------- | --------------------------------------------------------------------------------------------- |
| C-01 | **Mission Database**      | 任務模板、難度（F~SSS）、類型、報酬、時長、死亡率、陣營加分；D-02 文字 Facade | ✅ 已設計 | `MissionTemplate`, `MissionTypeTable`, `MissionCategoryTable`, `MissionDifficultyTable`（合併 baseReward / baseDuration / baseDeathRate / factionScoreDelta，由 FT-02 / FT-09 共用消費） |
| C-02 | **Adventurer Management** | 冒險者實例、階級（F~S）、狀態管理、名冊                     | ✅ 已設計 | `AdventurerTemplate`, `RecruitCostTable`                                                      |
| C-03 | **Profession System**     | 7 種職業定義、擅長/弱點、成功率修正                         | ✅ 已設計 | `ProfessionTable`                                                                             |
| C-04 | **Race System**           | 種族定義、屬性修正（如精靈調查+10%）                        | ✅ 已設計 | `RaceTable`（owner）；職業 → 種族池欄位 `raceIDs` / `raceWeights` 已合併入 C-03 `ProfessionTable` |
| C-05 | **Trait System**          | 個性特質、行為/數值影響、隨機抽取群組                       | ✅ 已設計 | `TraitTable`, `TraitGroupTable`                                                               |
| C-06 | **World Danger System**   | 5 階全局壓力、時間閘+進度閘+陣營閘、任務池偏移              | ✅ 已設計 | `WorldDangerTable`（單表整合升級閘 / 池權重 / 債務上限）                                       |

### Feature 層（依賴 Core）

| ID    | 系統                       | 說明                                                           | GDD 狀態  | 對應資料表                                |
| ----- | -------------------------- | -------------------------------------------------------------- | --------- | ----------------------------------------- |
| FT-01 | **Adventurer Recruitment** | 新手自薦（自動刷新）、老手邀請（費用+聲望）                    | ✅ 已設計 | `RecruitCostTable`                        |
| FT-02 | **Mission Dispatch**       | 推薦機制、rankDiff、成功率/死亡率公式                          | ✅ 已設計 | `SuccessRateTable`（owner）；`baseDeathRate` 消費 C-01 `MissionDifficultyTable` |
| FT-03 | **NPC Decision System**    | willingness 公式、接受/拒絕、閒置自主接單                      | ✅ 已設計 | `SystemConstants`                         |
| FT-04 | **Outcome Resolution**     | 5 種結算結果、condition 救活、聲望變化、事件發布               | ✅ 已設計 | `ReputationDeltaTable`, `SystemConstants` |
| FT-05 | **Guild Gold Flow**        | 公會全金流統一執行：預收、結算、維護費、薪水；破產狀態轉移快照 | ✅ 已設計 | `SystemConstants`                         |
| FT-06 | **Guild Core**             | 公會等級（Lv1~5）、聲望門檻、可接難度上限、Game Over 流程、公會基礎狀態 | ✅ 已設計 | `GuildLevelTable`                         |
| FT-07 | **Guild Building System**  | 6 棟可升級建築、雙軌閘門（金幣+聲望）、效果值 API（名冊/並行任務/刷新間隔/破產倒數） | ✅ 已設計 | `BuildingTable`                           |
| FT-08 | **Gacha System（面試系統）** | 多池抽卡面試（A/B 池）、保底、保留、垃圾物品；錄用後透過 `FT-12.HireStaff` 交棒 | ✅ 已設計（2026-04-26 從原 FT-08 拆分；聚焦 gacha） | `StaffGachaPoolTable`, `StaffRefreshCostTable`, `StaffRarityProbTable`, `TrashItemTable`, `StaffPlayerState`（owner） |
| FT-09 | **Faction Story System**   | styleTag 累積、陣營路線、劇情階段、按難度加權累分     | ✅ 已設計 | `FactionRouteTable`, `StoryStageTable`（owner）；`factionScoreDelta` 消費 C-01 `MissionDifficultyTable` |
| FT-10 | **Save/Load System**       | Unity 本地存檔(三軌寫入策略)、Bootstrap 順序協調、Backup rotation、Game Over 封存、離線計算交棒 F-02         | ✅ 已設計 | —                                         |
| FT-11 | **Offline Resolver**       | 離線期間 NPC 自主接單追認、結算回補、金流批次補算              | 待設計（Jam 範疇外，佔位） | `SystemConstants`（`OFFLINE_MAX_SECONDS`） |
| FT-12 | **Staff System（職員系統）** | StaffInstance 名冊管理、三態狀態機（Working/Reallocating/OnLeave）、Slot 指派、effect 聚合 API、薪水管線（Phase 2 Jam 不發）；接收 FT-08 錄用候選 | ✅ 已設計（2026-04-26 從原 FT-08 拆分；聚焦運營；§3.9 / §4.2 / §5.3 / §8.6 標 Phase 2 由 FT-05 統一接管薪水）| `StaffTable`（owner）, `StaffTuning` |

### Presentation 層（UI）

| ID   | 系統                           | 說明                                   | GDD 狀態 | 對應資料表             |
| ---- | ------------------------------ | -------------------------------------- | -------- | ---------------------- |
| P-01 | **Desktop Transparent Window** | Windows API 透明視窗、點擊穿透、前置；切換螢幕；解析度自適應 + 玩家縮放 | ✅ 已設計 | —                      |
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
│  FT-05 Guild Gold Flow ──► FT-02, FT-04, F-03, FT-07, FT-12                         │
│                            (預收 + 結算 + 維護費 + 薪水，走 AddGoldAllowBankruptcy)   │
│  FT-06 Guild Core ──────► F-03 (聲望門檻判定)                                       │
│  FT-07 Guild Building ──► FT-06 (聲望閘), F-03 (金幣扣款)；提供容量 API 給 FT-01/FT-02 │
│  FT-08 Gacha System ────► FT-06 (主閘 minGuildLevel), FT-07 (職員休息室 L1 解鎖     │
│                            ＋驅動自動刷新間隔), FT-12 (錄用呼叫 HireStaff)            │
│  FT-09 Faction Story ───► FT-04, C-01 (結算觸發 + styleTag)                        │
│  FT-10 Save/Load ───────► ALL (序列化所有系統狀態)                                   │
│  FT-11 Offline Resolver ► F-02, FT-02, FT-03, FT-04, FT-05（未來 / Jam 範疇外）       │
│  FT-12 Staff System ────► FT-06 (公會等級), FT-07 (IsStaffSystemUnlocked)；          │
│                            發布 OnStaffSalaryDue → FT-05 扣款 (Phase 2)；           │
│                            提供 GetStaffWillingnessBonus → FT-03                     │
│                            提供 GetAccountantCommissionBonus / Penalty → FT-05       │
│                            提供 GetRecruitRefreshReductionSec → FT-08 面試刷新加成   │
│                            提供 IsSuccessRatePreviewEnabled → P-02 委託板            │
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
| GOLD_INITIAL                   | 200     | 玩家起始金幣                                                  |
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

> **FT-08 / FT-12 系統常數獨立分表**（2026-04-26 拆分後共用）：
> - FT-12 接管：`EFFECT_MAX_*`（聚合上限：`EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC`）、`REALLOCATING_AUTO_LEAVE_SECONDS`、`BUILDING_SWITCH_COOLDOWN_SECONDS`、`ROSTER_CAP`（FT-12 §6.5 / §7.2）
> - FT-08 接管：`PITY_THRESHOLD`、`TRASH_ROLL_RATE_AT_RARITY_1` 等 gacha 專屬常數
>
> 兩系統常數**不在此表**，皆收錄於 `StaffTuning.csv`（見下節 § 參數表）。理由：避免 SystemConstants 因子系統爆量、保持核心常數表精簡可讀，並讓 FT-08 / FT-12 自帶可調表與各自 GDD §7 對齊。

### 參數表（Per-System）

| 表格名稱                   | PK               | 主要欄位                                                                               | 引用                                                      |
| -------------------------- | ---------------- | -------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| `ProfessionTable`          | professionID     | name, description, strongTypeIDs, weakTypeIDs, tier, baseProfessionID, raceIDs, raceWeights, traitGroupIDs（C-03 §3.1，整合 C-04 / C-05 消費欄位） | typeID, raceID, groupID                                  |
| `RaceTable`                | raceID           | name, description, statModifiers (JSON)                                                | —                                                         |
| `TraitTable`               | traitID          | name, description, effectType, effectTarget, effectValue                               | —                                                         |
| `TraitGroupTable`          | groupID          | groupName, traitIDs (CSV list), pickCount, pickMode                                    | traitID                                                   |
| `AdventurerTemplate`       | templateID (int) | name, rank, professionID, raceID, fixedTraits, factionID, isUnique                     | professionID, raceID, traitID, FactionRouteTable          |
| `RecruitCostTable`         | rank             | cost, reputationReq                                                                    | —                                                         |
| `MissionTemplate`          | missionID (int)  | difficulty, typeID, factionID, categoryID                                              | MissionTypeTable, MissionCategoryTable, FactionRouteTable |
| `MissionTypeTable`         | typeID (int)     | typeName                                                                               | —                                                         |
| `MissionCategoryTable`     | categoryID (int) | categoryName                                                                           | —                                                         |
| `MissionDifficultyTable`   | difficulty       | baseReward, baseDuration, baseDeathRate, factionScoreDelta（C-01 §3.1，9 行覆蓋 F~SSS；FT-02 / FT-09 共用消費）| —                                                         |
| `SuccessRateTable`         | rankDiff         | successRate                                                                            | —                                                         |
| `ReputationDeltaTable`     | difficulty       | successDelta, failDelta                                                                | —                                                         |
| `GuildLevelTable`          | level            | title, reputationThreshold, maxDifficulty                                              | —                                                         |
| `BuildingTable`            | (buildingID, level) | name, maxLevel, effectValue, upgradeCost, guildLevelReq                             | —                                                         |
| `StaffTable`               | staffID (int)    | name, rarity, salary, severancePay, isFiller, factionID, effectIDs, effectValues, slotBuildingIDs, uiFlagIDs, uiFlagBuildingIDs（見 FT-12 §3.2） | buildingID, FactionRouteTable                             |
| `StaffGachaPoolTable`      | poolID (int)     | poolName, eligibleStaffIDs (CSV list), minGuildLevel, maxGuildLevel, reserveTimeLimitSec, 5 個預留閘欄位（見 FT-08 §3.2.2） | staffID, FT-06 guildLevel                                  |
| `StaffRefreshCostTable`    | refreshCount     | manualRefreshCost（見 FT-08 §4.1.2） | —                                                         |
| `StaffRarityProbTable`     | rarity (1~5)     | baseProbability（見 FT-08 §4.1.5） | —                                                         |
| `TrashItemTable`           | trashItemID (int) | name, description, drawWeightTier1~5（見 FT-08 §3.5） | —                                                         |
| `StaffTuning`              | key              | value（FT-08 / FT-12 共用常數：EFFECT_MAX_*（FT-12 §4.1）、PITY_THRESHOLD（FT-08）、REALLOCATING_AUTO_LEAVE_SECONDS / BUILDING_SWITCH_COOLDOWN_SECONDS（FT-12 §6.5 / §7.2）、ROSTER_CAP（FT-12）） | —                                                         |
| `WorldDangerTable`         | dangerLevel      | name, timeThreshold, missionCountReq, minDifficulty, factionScoreReq, weightF_E, weightD, weightC, weightB, weightA, weightS_SSS, maxDebt（C-06 §3.1，整合升級閘 / 池權重 / 債務上限） | —                                                         |
| `BankruptcyThresholdTable` | reputationMin    | reputationMax, warningDurationSec                                                      | —                                                         |
| `FactionRouteTable`        | factionID (int)  | name, description                                                                      | —                                                         |
| `StoryStageTable`          | stageID (int)    | factionID, stageIndex, scoreThreshold, missionID, dialogueKey（FT-09 §3.2.2）          | factionID, MissionTemplate                                |
| `VeteranRankWeightTable`   | rank             | weight（FT-01 §4.4 / 7.3,5 階加權隨機表）                                              | —                                                         |
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
| 18   | FT-08 Gacha System + FT-12 Staff System | 職員面試（gacha + 名冊管理；2026-04-26 拆分）|
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
| **8**  | Guild Core + Building + Gacha (FT-08) + Staff (FT-12) | Resource Mgmt（FT-08 / FT-12 為原職員系統拆分）|
| **9**  | Recruitment                          | Adventurer, Guild Core    |
| **10** | Faction Story                        | Outcome, Mission DB       |
| **11** | Save/Load                            | ALL                       |
| **12** | UI Framework                         | ALL gameplay systems      |
| **13** | Desktop Window                       | UI Framework              |

---

1. **Sprint 1 開工前 grep audit**:批次處理 FT-09/FT-10 反向依賴 + F-02 §5.2 舊 EC 清理
2. **Codex 工項書建立順序**:對齊 systems-index Day 1-3 開發優先序
   - F-01 → F-02 → F-03 → C-01 → C-03 → C-02 → FT-02 → FT-03 → FT-04 → FT-05 → FT-01 → P-02
3. **資料表 CSV 生成**:依各 GDD §7 與對應 data-spec 規格書,於 Sprint 1 一次性生成
4. **二次設計爭議若浮現**:本報告「設計層面決策點」4 項可作為複審入口

---

## 進度追蹤

| 系統                              | 概念  | GDD                                     | 工項需求書 | 實作  | 測試  |
| ------------------------------- | --- | --------------------------------------- | ----- | --- | --- |
| F-01 DataManager                | ✅   | ✅                                       | ✅     | ⬜   | ⬜   |
| F-02 Time System                | ✅   | ✅                                       | ✅     | ⬜   | ⬜   |
| F-03 Resource Management        | ✅   | ✅                                       | ✅     | ⬜   | ⬜   |
| C-01 Mission Database           | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| C-02 Adventurer Management      | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| C-03 Profession System          | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| C-04 Race System                | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| C-05 Trait System               | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| C-06 World Danger System        | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-01 Adventurer Recruitment    | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-02 Mission Dispatch          | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-03 NPC Decision System       | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-04 Outcome Resolution        | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-05 Guild Gold Flow           | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-06 Guild Core                | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-07 Guild Building System     | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-08 Gacha System              | ✅   | ✅（2026-04-26 從原職員系統拆出，聚焦 gacha）| ⬜     | ⬜   | ⬜   |
| FT-09 Faction Story System      | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-10 Save/Load System          | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| FT-11 Offline Resolver          | ⬜   | ⬜                                       | ⬜     | ⬜   | ⬜   |
| FT-12 Staff System              | ✅   | ✅（2026-04-26 從原職員系統拆出；§3.9 / §4.2 / §5.3 / §8.6 + AC-2 / AC-36 標 Phase 2）| ⬜     | ⬜   | ⬜   |
| P-01 Desktop Transparent Window | ✅   | ✅                                       | ⬜     | ⬜   | ⬜   |
| P-02 Main UI Framework          | ✅   | ⬜                                       | ⬜     | ⬜   | ⬜   |
| P-03 Notification System        | ✅   | ⬜                                       | ⬜     | ⬜   | ⬜   |
| D-01 Character Content DB       | ✅   | ⬜                                       | ⬜     | ⬜   | ⬜   |
| D-02 Mission Content DB         | ✅   | ⬜                                       | ⬜     | ⬜   | ⬜   |

---

## GDD Patch 紀錄

本章記錄 GDD 通過 `/design-review` 後主體執行的修正 patch（不含 GDD 內 §九 變更歷史的常規迭代）。每筆 patch 標明日期、影響 GDD、章節、修改摘要與來源。

| 日期 | Patch ID | 影響 GDD | 章節 | 修改摘要 | 來源 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | P-001 | FT-05 Guild Gold Flow | §3.2、§3.9.1、§3.9.2、§6.3 | `OnCommissionAccepted` / `OnCommissionPrepaid` 事件 source 型別由 `CommissionSource` 改為 `DispatchSource`（修正與 FT-02 §3.6a enum 命名衝突）；§3.9.1 表格補防錯註記「不要與 `CommissionSource = {Regular, Static}` 混淆」 | `/design-review`（C-01~C-05、FT-01~FT-05 跨系統一致性） |
| 2026-04-27 | P-002 | C-05 Trait System | §4.4 | `ApplyConditionTraits` 偽碼補上 `outcome.triggeredConditionTraits.Add(traitID)` 邏輯；表格後新增追加規則說明（機率類型僅在判定通過時追加；固定值類型命中即追加）。對齊 FT-04 §3.4「觸發成功的 traitID 由 C-05 追加」契約 | `/design-review`（FT-04 ↔ C-05 跨系統 inconsistency） |
| 2026-04-27 | P-003 | C-03 Profession System、C-04 Race System | C-03 §3.1 / §5.1；C-04 §5.1 | C-03 §3.1 表格後新增「跨欄位長度驗證」note：`raceWeights` 與 `raceIDs` 長度一致驗證**歸 C-03 Loader 載入時負責**，違規跳過該職業；§5.1 邊緣案例補一行「raceWeights 長度不一致 → LogError + 跳過該職業」。C-04 §5.1 對應行改寫為「驗證歸 C-03 Loader 處理；C-04 RollRace 走 `profession == null` fallback 路徑，不重複驗證」 | `/design-review`（C-03 ↔ C-04 驗證歸屬不明） |
| 2026-04-27 | P-004 | C-04 Race System | §3.1 | Game Jam 初始資料表後新增 note：「`raceID = 1`（人類）為 fallback 保留 ID」。`§4.1 RollRace` 多處錯誤路徑均 fallback 至 `raceID=1`，禁止新增種族時重排或覆寫此 ID | `/design-review`（C-04 fallback ID 假設未明文保護） |
| 2026-04-27 | P-005 | C-06 World Danger System | §3.2 / §3.4 / §4.1 / §4.2 / §4.3 / §4.5 | `/design-review C-06` NEEDS REVISION 修正（6 子項）：(1) §4.1 `GetElapsedDays` 偽碼補 `gameStartTimestamp == 0` 防護分支（必修，原偽碼會讓時間閘恆滿足，違反 §5.2 row 2「視為未滿足」設計意圖）；(2) §4.2 `CheckLevelUp` 偽碼補缺漏階跳過分支（讓 §5.1 row 1 可被 Codex 直接照偽碼實作）；(3) §4.5 `OnFactionScoreUpdated` 偽碼加 A 階早退（對齊 §5.2 row 5）；(4) §4.3 Script Execution Order 補「實作方式」說明：用 `[DefaultExecutionOrder]` 屬性宣告、不依賴 Unity Editor 設定；(5) §3.4 表格末段補「API 呼叫機制」note：`OnMissionAccepted` / `OnFactionScoreUpdated` 為直接 API 呼叫不走 EventBus；(6) §3.2 `gameStartTimestamp` 寫入時機明確化：FT-10 `InitializeAsNewGame` 寫入 `NowUTC`、`RestoreFromSave` 還原；寫入時機在 C-06 `Awake` 之後、`Start` 之前 | `/design-review C-06`（NEEDS REVISION） |

> **維護指引**：每次因 `/design-review` / `/scope-check` 等跨 GDD 審查產生的修正 patch 應在此登記。GDD 內 §九「變更歷史」僅記錄該 GDD 本身的版本演進；本表記錄跨 GDD 一致性修正，便於未來 audit 與回溯。
>
> **與 FSD-index §九 的區別**：FSD-index §九記錄 FSD-index 規範本身的變更；本表記錄 GDD 內容的跨系統 patch。FSD 因 GDD patch 需重新對齊時（如 FT-05 FSD 因 P-001 需更新 §5.3 enum 描述），於 FSD-index §7.2 自檢紀錄中追加 patch review 列。
