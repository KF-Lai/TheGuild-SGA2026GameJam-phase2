# Guild Staff System 系統設計文件

_建立時間：2026-04-24_
_狀態：主結構完成 + design-review 已過（Jam 版範疇鎖定，§3.11 / §4.3 / §5.4 / §8.7 為 Phase 2 不實作）_
_系統 ID：FT-08_

**🔖 下次接續入口**：**Codex 工項書建立**（§3.1 ~ §3.10 + §3.12 + §3.13 + §4.1 + §4.2 + §6 + §7 + AC-1 ~ AC-29 + AC-35 ~ AC-47 為 Jam 範疇）。`/design-review FT-08`（2026-04-25）裁示：MUST FIX #1 採 B 案（薪水管線 Phase 2 化），MUST FIX #2 已自動驗證通過。`StaffPlayerState` / `StaffInstance[]` / `CandidateCard[]` 全部已就緒，可進實作。

---

## 設計來源（Design Inputs）

本 GDD 基於以下決策來源撰寫：

- `DevLogs/DevLog_260423-2.md` — FT-08 Q1~Q6 + D1~D7 決策摘要（5 項大類）
- `design/gdd/game-concept.md` §「公會職員系統」— 3 類職員（櫃台 / 委託官 / 會計）原始概念
- 依賴 GDD 約束：
  - `[FT-03] npc-decision-system.md` — `GetStaffWillingnessBonus() : float` 契約（委託官類 +0.05、未招募 0）
  - `[FT-05] guild-gold-flow.md` — `GetAccountantCommissionBonus()` / `GetAccountantPenaltyBonus()` / `OnStaffSalaryDue` 契約（會計類 +0.02 / -0.02，需 slot 指派預備金保險櫃）
  - `[FT-06] guild-core.md` — `GetCurrentLevel()`
  - `[FT-07] guild-building-system.md` — `IsStaffSystemUnlocked()`（職員休息室 L1 解鎖 FT-08）
- 全局規則：`feedback_time_units`（時間單位僅秒/小時）

> ⚠️ **決策粒度警示**：原規劃 `project_ft08_design_decisions.md` 記憶檔遺失，Q1~Q6 / D1~D7 逐題細節已無法取回，本 GDD 以 DevLog 摘要為基底，細節（保底演算法、tag 清單、資遣費公式、狀態轉移細節等）於各節設計時與使用者逐題確認補齊。

---

## 1. 概要（Overview）

**FT-08 Guild Staff System 是公會職員的玩法系統**：透過「職員面試系統」（內部採 gacha 隨機池實作）面試並錄取職員入職、以 `StaffTable.effectIDs` / `effectValues` CSV list（每個職員攜帶的效果清單 + 數值，見 §3.2）聚合形成對外加成查詢 API、並作為 `OnStaffSalaryDue` 事件發布者驅動 FT-05 每日薪水扣款管線。本系統以 FT-07 `IsStaffSystemUnlocked()`（職員休息室 L1 啟用）作為整體啟停閘——職員休息室未建造時，FT-08 **靜默降級**：所有加成 API 回傳 `0`、`OnStaffSalaryDue` 不發布、面試操作 API 回傳 `STAFF_SYSTEM_LOCKED`（符合 FT-05 §3.9.1 / Case 5.4.1 的降級契約）。UI 層的隱藏 / 禁用由 P-02 訂閱 FT-07 `IsStaffSystemUnlocked()` 自行處理，FT-08 不規範 UI 行為。系統涵蓋三大職責：

### A. 職員面試系統（Staff Interview System）

玩家於職員休息室 L1 啟用後可透過職員面試系統錄取新職員。系統採**多池並存 + 依公會等級逐階開放**的架構：

**池架構（Jam 版 2 池）**

- **A 池（常駐通用，Lv1~Lv5）**：公會基礎人才庫，全程開放
- **B 池（中高等，Lv3~Lv5）**：中期疊加開放，提供更高品質職員
- 每池為 **category-agnostic 容器**——schema 不含 `poolType` / `factionTag` / `professionTag` 等分類欄位；池的定性由 `eligibleStaffIDs` 實際成員隱式決定（「中高等池」為設計意圖，非資料標籤）
- **主閘**：`[minGuildLevel, maxGuildLevel]`（連續；DataManager 驗證 `min ≤ max`）
- **預留閘**（FT-09 劇情解鎖 / 陣營 ID / 最低聲望 / 活動起止共 5 欄位）：Schema 前向相容，Jam 版**強制空值**；DataManager 若偵測到任一非預設值即拋 `StaffGachaPoolTableValidationException`

**刷新模型（自動 + 手動並存獨立）**

- **自動刷新**：免費、間隔依**職員休息室（`buildingID=6`）等級**決定（L1=86400s → L5=21600s 階梯，FT-07 §7）；計時器**全域單一**（`StaffPlayerState.lastAutoRefreshTimestamp`），切池**不重置**、延續倒數；離線跨多個觸發點**僅補刷 1 次**；計入保底 counter
- **手動刷新**：付費（`StaffRefreshCostTable[guildLevel]`）、玩家任意觸發、**無 cooldown**（防呆由 P-02 UI disable 按鈕 0.5s 避免誤觸）；計入保底 counter
- 兩者獨立並存，互不重置對方計時器
- **切池副作用**：玩家切至另一池 → 前池未保留的候選**全部被新池 roll 結果覆蓋**（`currentCandidates` 不保留前池狀態）；已保留候選（`reservedCandidates`）跨池存活不受影響——保留區是跨池保留的**唯一**途徑

**面試欄 UX（每張獨立三擇一）**

- 每次刷新展示 N 張候選（N 依公會等級遞增，§7 調參）
- 玩家對每張獨立三擇一：**錄用 / 不錄用 / 保留**
  - **錄用** → 建立 `StaffInstance` 入名冊、支薪、提供 effect；該 slot **空置至下次刷新**
  - **不錄用** → 該 slot **空置至下次刷新**（不立即補位，由玩家決定節奏）
  - **保留** → 候選移入 `reservedCandidates`、不入名冊、不支薪、不提供 effect，下次刷新跳過該 slot
- UX 呈現（簡歷大頭照、上下滑翻動特效、蓋印章 affordance）由 P-02 Main UI 負責；FT-08 僅定義三個 action 的資料語意
- 保底 counter **每張候選 +1**（一次 refresh 使 counter += N，N = 當前面試欄張數）

**保留機制**

- 每池獨立保留時限（`StaffGachaPoolTable.reserveTimeLimitSec`）
- 保留上限公式：`maxReserve = max(1, interviewSlotCount - 1)`（面試欄 = 1 時特例 = 1）
- 時限到自動解除保留 → 下次刷新該候選被新候選取代
- **`reserveConsumedFlag` 防刷機制**：候選一旦解除保留（手動取消 / 時限到）即永久失去再次保留資格，防止「保留 → 取消 → 保留」零成本循環繞過刷新費

**稀有度 roll 與職員權重**

- 稀有度基底權重：1★40% / 2★25% / 3★15% / 4★15% / 5★5%
- **稀有度層空 → 動態歸一化**：`effectiveProb[r] = baseProb[r] / (1 − Σ baseProb[空層])`
- 稀有度確定後 → 該層 `eligibleStaffIDs` 中依 `StaffGachaPoolTable.staffWeights`（平行 CSV、整數、允許 0）按權重 roll 具體職員

**保底機制（10 抽保底 5★）**

- Counter **跨池累積**（切池不失進度）
- 公會升級**不 reset**（跨等級累積）
- 保底觸發時若池中 5★ 空 → counter **不 reset**，本次仍走動態歸一化於一般層 roll，counter 繼續累積到下次池內 5★ 開放觸發
- Counter 存放於 `StaffPlayerState`（§3.4）

**垃圾物品填充**：flavor item（純風味、無加成）+ 平庸職員（`isFiller=true`、effect 低）；具體 roll 演算法與 `TrashItemTable` schema 見 §3.3 / §3.5

**名冊上限**：Jam 版固定 99（無人資辦公桌建築）

### B. 職員運營（Staff Operations — Tag Aggregation & Assignment）

每個職員為獨立角色，職能透過 `StaffTable.effectIDs` / `effectValues`（平行 CSV list，見 §3.2）表達：

- **Effect 聚合加成**：系統以 effectID 分類計算對外加成，暴露 5 個查詢 API：
  - `GetStaffWillingnessBonus() : float` — 委託官類 tag 加總（FT-03 NPC Decision 讀取，被動生效；下游已硬編碼）
  - `GetAccountantCommissionBonus() : float` — 會計傭金類 tag 加總（FT-05 Guild Gold Flow 讀取，被動生效；下游已硬編碼）
  - `GetAccountantPenaltyBonus() : float` — 會計賠償類 tag 加總，**額外要求**該職員已指派至「預備金保險櫃」slot（FT-05 契約；下游已硬編碼）
  - `GetRecruitRefreshReductionSec() : int` — 櫃台類 slot 效果（指派公會櫃臺減少招募刷新秒數；FT-01 消費）
  - `IsSuccessRatePreviewEnabled() : bool` — 委託官類 UI 功能（指派委託板時 P-02 顯示成功率預估）
- **Slot 指派**：`StaffTable.slotBuildingID` 可多選（CSV list，空位填 `0`）；指派至對應建築欄位時觸發指派加成（槽位效果）
- **三態互斥 + 冷卻正交**：**工作中 / 再分配 / 休假** 三態互斥；**切換建築冷卻**（2h = 7200s）為正交機制，可覆蓋在任一態上
- **自動轉休假**：再分配 > 12h（含離線時間，43200s）自動轉休假
- **解雇**：無冷卻、不可逆、需扣資遣費（`StaffTable.severancePay`）

### C. 薪水管線（Salary Pipeline — OnStaffSalaryDue Publisher）

> **⚠️ Phase 2 — Jam 版整節不實作（2026-04-25 使用者裁示 B 案）**
>
> 為對齊 FT-05 GDD（line 31 / 257 / 285 / 754 / 780）既有「`OnStaffSalaryDue` 為 Phase 2、Jam 版不發布」立場，FT-08 §3.11 / §4.3 / §5.4 / §8.7（AC-30 ~ AC-34）整體標為 **Phase 2 範疇**，Jam 版**不實作 OnSalaryTick / ProcessOfflineSalary / OnStaffSalaryDue 發布**。職員「招募即終身免費」（不扣薪）為已知設計妥協；Post-Jam 啟用時須同時拆 FT-05 端的 Phase 2 註記。
>
> 設計內容（觸發時點、組裝邏輯、補發、降級）保留於下方供 Phase 2 直接使用，**不刪除**。Codex 工項書階段，§3.11 系列子節 + AC-30 ~ AC-34 全部跳過實作。

作為 FT-05 `OnStaffSalaryDue` 事件發布者（**Phase 2 規格**）：

- **每日 UTC 06:00 發薪**（以玩家電腦時區判斷跨日）
- **離線補發 N 次**：若離線跨多個發薪時間點，逐次補發（上限由 `SystemConstants.OFFLINE_MAX_SECONDS` 間接限制）
- **休假態不支薪**：發薪時跳過該職員，不計入 `perStaffSalary`
- **時區對不上則跳過不扣款**（系統時鐘異常、UTC/local 轉換異常）
- FT-08 負責計算每日薪水總額、組裝 `perStaffSalary: Dict<int,int>`、發布事件；FT-05 負責實際扣款（`AddGoldAllowBankruptcy`）

### 核心職責

1. 管理職員實例（`StaffInstance`）的生命週期（招募、指派、切換、解雇、狀態轉移）
2. 提供 tag 聚合後的三個固定名稱加成 API 給 FT-03 / FT-05
3. 驅動每日薪水發薪管線，發布 `OnStaffSalaryDue` 事件（**Phase 2 — Jam 版不實作，§3.11 / §4.3 / §8.7 整段為 Phase 2 規格保留**）
4. 職員面試系統實作（gacha 機制 + 稀有度池 + 保底）

### 不負責

- **職員立繪 / flavor text 內容**（D-01 Character Content DB、writer agent）
- **職員陣營 `styleTag` 累積、陣營劇情觸發**（FT-09 Faction Story System 負責，FT-08 僅保留 `StaffTable.factionID` 資料欄）
- **薪水實際扣款**（FT-05 Guild Gold Flow 的 `AddGoldAllowBankruptcy`）
- **職員休息室建築本身**（FT-07 Guild Building System 的 `buildingID=6`）
- **面試 UI 動畫 / 抽卡展示 / 通知 toast / UI 隱藏邏輯**（P-02 Main UI、P-03 Notification）
- **職員狀態存檔序列化**（FT-10 Save/Load，FT-08 僅定義 `StaffInstance` 資料結構）

### 資料表

| 表名 | 用途 |
|---|---|
| `StaffTable` | 職員模板（`staffID`, `name`, `rarity`, `salary`, `severancePay`, `isFiller`, `factionID`, `effectIDs`, `effectValues`, `slotBuildingIDs`, `uiFlagIDs`, `uiFlagBuildingIDs`；見 §3.2） |
| `StaffRefreshCostTable` | 依公會等級的面試刷新費用 + 張數（PK = `guildLevel`） |
| `StaffGachaPoolTable` | gacha 池配置（PK = `poolID`）；含 `[min,max]GuildLevel` 開放區間、`eligibleStaffIDs` + `staffWeights` 平行 CSV、`reserveTimeLimitSec`、5 個預留閘欄位（詳 §3.2.2） |
| `StaffRarityProbTable` | 稀有度 1★~5★ 權重表 |
| `TrashItemTable` | 垃圾物品（flavor item + 平庸職員）定義 |

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-08 於 Jam 版**聚焦兩個核心情緒**（其他面向——收集欲、班表經營、薪水壓力——延後至 Post-Jam「人事系統」擴充，見 `game-concept.md`「公會職員系統」段末 Post-Jam 擴充構想）：

**面試的期待與驚喜（Sensation / Fantasy）**

- 每次面試都是一場未知：下一批應徵者裡會不會有改變公會命運的關鍵人才？
- 稀有度金光、10 抽保底兌現、首次抽到 5★ 的瞬間——這些是公會長最私密的小確幸
- 遇到 flavor item（咖啡杯、破損的鵝毛筆）時的苦笑——面試不總是成功，但每次都值得記錄
- 職員不是抽象的數值包裝，是帶著 flavor 的個體

**間接控制的可見槓桿（Mastery / Expression）**

- 玩家無法直接指揮冒險者，但面試錄取到對的職員時，系統層的槓桿會具體地動
- 委託官在職 → 推薦接受率 +5%（FT-03 willingness 公式直接反映）
- 會計在職 → 傭金多 2%（FT-05 結算明細可見 `+0.02` 的 breakdown）
- 會計指派到預備金保險櫃 → 賠償少 2%（需到 slot 指派才生效的雙層設計）
- 加成不是「一次性 buff」而是「配置」的結果——玩家感受到「我的公會因為我的決策而運作得更好」

### 玩家幻想敘事

> 「公會終於有一間像樣的職員休息室了。這週我貼出面試公告——今天來了幾位應徵者，坐在休息室等我面談。有看起來很專業的會計，也有不知道為什麼拿著咖啡杯來應徵的傢伙。我從桌上抽出幾份履歷，決定要見哪一位。這些人一旦入職，公會的每一筆委託、每一場結算都會因為他們而不同——不是他們親自去打怪，而是因為他們在這裡，一切都變得順手一點。」

### 設計原則（FT-08 如何強化幻想）

1. **面試結果即時兌現**：面試當 frame 生效——錄取的 5★ 職員即可查 tag、指派 slot，不經「培養」「升等」拖慢期待感的回饋
2. **加成路徑可見**：三個對外 API（`GetStaffWillingnessBonus` / `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus`）直接對應下游公式的命名變數——P-02 結算明細顯示 breakdown 時，玩家能看到「會計 +0.02 → 傭金率 0.22」的因果鏈
3. **垃圾物品作為「真實感」錨點**：flavor item 不是 bug，是刻意的設計——讓面試有苦有樂，也讓 5★ 抽到時更珍貴（收集欲 / 重複面試動機留給 Post-Jam 擴充）
4. **指派 slot 的雙層決策**：被動加成（入職即生效）+ 指派加成（需佔位）——玩家思考的是「這位職員該派去哪」而非「要不要錄取」

### 關鍵情緒節點（Emotional Beats）

| 節點 | 觸發事件 | 期望情緒 |
|------|---------|---------|
| 首次開啟面試系統 | 職員休息室 L1 建成，`IsStaffSystemUnlocked()` 首次為 `true` | 新鮮、投資感 |
| 抽到 5★ 職員 / 10 抽保底兌現 | 面試結果稀有度 = 5 | 驚喜、成就 |
| 首次指派會計到預備金保險櫃 | slot 指派完成，`GetAccountantPenaltyBonus()` 首次回傳 `-0.02` | 掌控感（雙層加成生效） |
| 首次查看結算明細看到 bonus breakdown | FT-05 結算畫面顯示 `accountantCommissionBonus = +0.02` | 因果回饋（我的決策改變了結果） |

---

## 3. 詳細規則（Detailed Rules）

> 本節採分批撰寫：**Batch A**（§3.1 / §3.2 / §3.6）奠定資料結構 + 效果聚合規則；**Batch B**（§3.3 / §3.4 / §3.5）面試 / gacha / 保底 / 垃圾物品；**Batch C**（§3.7 ~ §3.13）slot 指派 / 狀態機 / 薪水 / 降級 / 事件契約。目前進度：**Batch A 完成**，B / C 待設計。

### 3.1 職員實例（StaffInstance）

執行期每位職員以 `StaffInstance` 物件表示。當面試錄取一位新職員時建立一個 StaffInstance；解雇時銷毀。

```
StaffInstance {                                      // ↓ 持久化欄位（FT-10 序列化）
    instanceID                         : int         // 唯一識別碼；遞增 int（FT-08 維護 counter）
    staffID                            : int         // 對應 StaffTable 模板
    currentState                       : enum { Working, Reallocating, OnLeave }
    assignedBuildingID                 : int         // 目前指派的建築 ID；0 = 未指派
    reallocatingStartTimestamp         : long        // 進入「再分配」的 UTC 時間戳；0 = 非再分配中
    buildingSwitchCooldownEndTimestamp : long        // 切換建築冷卻結束時間戳；0 = 無冷卻
    hiredTimestamp                     : long        // 入職時的 UTC 時間戳（統計 / 圖鑑用）
}

// Runtime-only（不序列化）：
//   無 —— 所有欄位皆持久化
```

**設計註記**

- **`instanceID`**：FT-08 維護遞增 counter，新遊戲起始 `nextInstanceID = 1`，每次面試錄取後 +1；同存檔內永不重複、永不回收（解雇也不釋出）。
- **重複 `staffID`**：允許同一 `staffID` 對應多個 StaffInstance（玩家兩次面試抽到同一職員模板）。
  > ⚠️ **Game Jam 暫代方案**：重複職員造成數值 / flavor 的奇怪體驗（例如公會裡有兩位同名會計），Jam 版視為已知問題。Post-Jam 規劃以新系統（可能為升星 / 重複補償 / 限定一人等機制）取代，細節留待後續設計。
- **冷卻 / 倒數全用時間戳**（UTC Unix seconds），不用剩餘秒數——抗離線、無需 tick 主動更新，查詢時即時算 `now - timestamp` 差值。
- **狀態 vs 冷卻正交**：`currentState` 三態互斥（§3.8 詳述），但 `buildingSwitchCooldownEndTimestamp` 可在任一態持續倒數（即使進入休假，冷卻仍照跑）。
- **`assignedBuildingID` 跨狀態語意**：
  - 再分配中 `assignedBuildingID == 0`（A2 案：Reallocating 表示「未指派任一建築、等待玩家選 buildingID」，不再代表目標 slot，§3.8 詳述）
  - 進入休假時清為 `0`
  - 工作中即代表**目前佔用**的 slot（無 slot 能力職員例外，可為 `0`，§3.8.2）
- **保底 counter** 不放 `StaffInstance`（屬於 player-level 狀態），位置與細節見 §3.4。

### 3.2 資料表 Schema

本節涵蓋 FT-08 兩大主要資料表：`StaffTable`（§3.2.1，職員模板）與 `StaffGachaPoolTable`（§3.2.2，gacha 池配置）。其餘輔助表（`StaffRefreshCostTable` / `StaffRarityProbTable` / `TrashItemTable`）於 §3.3 ~ §3.5 各自章節定義。

#### 3.2.1 StaffTable Schema

**檔案位置**：`Assets/Resources/Data/StaffTable.csv`

**Schema**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `staffID` | int (PK) | 唯一識別碼 |
| `name` | string | 職員顯示名稱 |
| `rarity` | int | 稀有度 1~5 |
| `salary` | int | 每日薪水（金幣；發薪時扣款，§3.11） |
| `severancePay` | int | 解雇資遣費（金幣） |
| `isFiller` | bool | 垃圾物品標記；平庸職員仍可入職、有薪水、可解雇，僅在 effect 數值上做出區別 |
| `factionID` | int | 陣營 ID（0 = neutral；Jam 版僅保留欄位，由 FT-09 Faction Story 消費，FT-08 不讀此欄） |
| `effectIDs` | CSV list of enum | 職員攜帶的效果清單（enum 值定義於 `StaffEffect` 白名單，見下） |
| `effectValues` | CSV list of float | 平行於 `effectIDs`，每個效果的加成數值（passive 為正值、penalty 類為負值；`RecruitRefreshOnCounter` 單位為秒） |
| `slotBuildingIDs` | CSV list of int | 合格 slot 候選清單（多選 = 二擇一指派；空或全 `0` = 無 slot 指派能力） |
| `uiFlagIDs` | CSV list of enum | UI 功能旗標清單（enum 值定義於 `StaffUIFlag` 白名單；純布林開關，**不走 effect 聚合**） |
| `uiFlagBuildingIDs` | CSV list of int | 平行於 `uiFlagIDs`，該旗標所需的 `assignedBuildingID`（不等於該值則 flag 不啟用） |

> DevLog 中使用的 `tags` / `tagValues` 命名於本節正式改名為 `effectIDs` / `effectValues`，以反映「不只是分類標籤，還攜帶數值」。概念層仍可稱「effect tag」。
>
> `uiFlagIDs` / `uiFlagBuildingIDs` 為本節新增欄位（2026-04-24），解決「純 UI 功能型 slot 加成」無法與 float-typed effect 統一表達的問題——走獨立聚合路徑（§3.6.6），不污染 `effectIDs` / `effectValues` 的數值型別。

**`effectIDs` / `effectValues` 數量上限（依 rarity）**：

| rarity | 最大 effect 數量 |
|---|---|
| 1★ | 1 |
| 2★ | 1 |
| 3★ | 2 |
| 4★ | 2 |
| 5★ | 3 |

- DataManager 載入時驗證：`effectIDs.Count == effectValues.Count ≤ rarity 對應上限`；違反則拋 `StaffTableValidationException`
- 平庸職員（`isFiller = true`）與正式職員共享 schema，差異以欄位數值表達（例如 `rarity=1`、`effectValues` 極小）；flavor 類（純文字風味、無加成）即 `effectIDs` 空集合的極端案例

**`StaffEffect` enum（白名單）**：

| effectID | 類型 | 對應 API | 生效條件 | 值單位 |
|---|---|---|---|---|
| `Willingness` | **Passive** | `GetStaffWillingnessBonus` | 職員狀態 ∈ { Working, Reallocating } | float 比率（如 `+0.05`） |
| `AccountantCommission` | **Passive** | `GetAccountantCommissionBonus` | 職員狀態 ∈ { Working, Reallocating } | float 比率（如 `+0.02`） |
| `AccountantPenaltyOnVault` | **Slot** | `GetAccountantPenaltyBonus` | 職員狀態 = Working **AND** `assignedBuildingID == 5`（預備金保險櫃） | float 比率（負值，如 `-0.02`） |
| `RecruitRefreshOnCounter` | **Slot** | `GetRecruitRefreshReductionSec` | 職員狀態 = Working **AND** `assignedBuildingID == 4`（公會櫃臺） | int 秒數（正值，如 `7200` = 2h） |

- **Effect 類型（Passive / Slot）由 enum 本身屬性決定**，不需要資料表額外欄位；未來擴充新 effect 時於 enum 定義中標註類型
- 未列於白名單的 effectID 於 DataManager 載入時拋錯
- 新增 effect 類型時需同步擴充：此白名單 + C# enum + §3.6 聚合演算法 + 若需新 API 還要同步下游 GDD

**`StaffUIFlag` enum（白名單）**：

| uiFlagID | 對應 API | 生效條件 | 消費者 |
|---|---|---|---|
| `SuccessRatePreview` | `IsSuccessRatePreviewEnabled` | 職員狀態 = Working **AND** `assignedBuildingID == uiFlagBuildingIDs[i]`（通常為 `1` 委託板） | P-02 委託審核 UI（顯示成功率預估數字） |

- UI flag 為純布林語意，**不提供數值加成**；任一職員符合條件即 `true`（OR 聚合，非 SUM）
- 未列於白名單的 uiFlagID 於 DataManager 載入時拋錯
- 新增 UI flag 時同步擴充：此白名單 + C# enum + §3.6.6 聚合演算法 + 消費者 GDD（例如 P-02）

**`slotBuildingIDs` 語意**：

- 多選 = **合格 slot 候選清單**；該職員可**二擇一**指派到清單中任一建築（同時只能指派一個 `assignedBuildingID`）
- 空（所有位置為 `0`）= 該職員無 slot 指派能力，僅提供 passive effects
- 實際指派到哪個 slot 由玩家於 §3.7 slot 指派流程決定；若職員帶 slot-type effect（如 `AccountantPenaltyOnVault`），**僅**在指派到對應建築 ID 時才觸發該效果

**資料表範例**（說明用，§7 調參）：

| staffID | name | rarity | salary | severancePay | isFiller | factionID | effectIDs | effectValues | slotBuildingIDs | uiFlagIDs | uiFlagBuildingIDs |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 101 | 克勞德·會計師 | 3 | 40 | 120 | false | 0 | `AccountantCommission,AccountantPenaltyOnVault` | `0.02,-0.02` | `5` | *(empty)* | *(empty)* |
| 102 | 艾蓮·委託官 | 2 | 30 | 80 | false | 0 | `Willingness` | `0.05` | `1` | `SuccessRatePreview` | `1` |
| 103 | 蘿絲·櫃台小姐 | 2 | 30 | 80 | false | 0 | `RecruitRefreshOnCounter` | `7200` | `4` | *(empty)* | *(empty)* |
| 104 | 無名小職員 | 1 | 10 | 10 | true | 0 | `Willingness` | `0.01` | `0` | *(empty)* | *(empty)* |
| 105 | 咖啡杯 | 1 | 0 | 0 | true | 0 | *(empty)* | *(empty)* | `0` | *(empty)* | *(empty)* |

#### 3.2.2 StaffGachaPoolTable Schema

**檔案位置**：`Assets/Resources/Data/StaffGachaPoolTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `poolID` | int (PK) | — | 唯一池識別碼（Jam 版 1 = 常駐通用、2 = 中高等） |
| `poolName` | string | — | 開發辨識名稱（不直接顯示於 UI） |
| `minGuildLevel` | int | — | 該池開放的最低公會等級 |
| `maxGuildLevel` | int | — | 該池開放的最高公會等級（DataManager 驗證 `≥ minGuildLevel`） |
| `eligibleStaffIDs` | CSV list of int | — | 該池可 roll 的職員 `staffID` 清單 |
| `staffWeights` | CSV list of int | — | 平行於 `eligibleStaffIDs`，每位職員在其稀有度層內的 roll 權重（整數 ≥ 0；0 = 保留不抽出） |
| `reserveTimeLimitSec` | int | — | 本池候選的保留時限（秒；必須 > 0） |
| `storyFlagRequired` | string | `""` | 🔒 FT-09 劇情 flag ID 預留欄；Jam 版強制空值 |
| `factionIDRequired` | int | `0` | 🔒 FT-09 陣營 ID 預留欄；Jam 版強制 `0` |
| `minReputation` | int | `0` | 🔒 聲望門檻預留欄；Jam 版強制 `0` |
| `eventStartTimestamp` | long | `0` | 🔒 限時活動起始 UTC 時間戳預留欄；Jam 版強制 `0` |
| `eventEndTimestamp` | long | `0` | 🔒 限時活動結束 UTC 時間戳預留欄；Jam 版強制 `0` |

**DataManager 驗證規則**：

1. `minGuildLevel ≤ maxGuildLevel` —— 否則拋 `StaffGachaPoolTableValidationException("poolID={id}: minGuildLevel 不得大於 maxGuildLevel")`
2. `eligibleStaffIDs.Count == staffWeights.Count` —— 平行 CSV 長度必須一致
3. 每個 `staffWeights[i] ≥ 0`；若 `sum(staffWeights) == 0` —— 拋錯（池無可抽項）
4. `reserveTimeLimitSec > 0`
5. 五個預留閘欄位（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`）若任一 ≠ 預設值 —— 拋 `StaffGachaPoolTableValidationException("poolID={id}: Jam 版不支援此閘參數，請保持預設值")`

**設計註記**

- **Category-agnostic 容器**：Schema 刻意不含 `poolType` / `factionTag` / `professionTag` 欄位；池的定性（例如「中高等池」）由 `eligibleStaffIDs` 的實際成員隱式決定，為設計意圖而非資料分類
- **職員與池解耦**：池開放/關閉僅影響未來面試結果；既有已錄取職員的 `StaffInstance` 與池無關、不受池狀態變動影響
- **預留閘的前向相容動機**：Post-Jam 擴充 FT-09 劇情 / 活動系統時，只需改 DataManager 驗證邏輯與 runtime 閘判斷，無需改動 schema
- **`staffWeights` 為稀有度層內權重**：稀有度由 `StaffRarityProbTable`（§3.3）先 roll 確定後，再於該層 `eligibleStaffIDs` 中按 `staffWeights` 比例 roll 具體職員

**Jam 版資料範例**（具體數值 §7 調參）：

| poolID | poolName | minGuildLevel | maxGuildLevel | eligibleStaffIDs | staffWeights | reserveTimeLimitSec |
|---|---|---|---|---|---|---|
| 1 | Common Pool | 1 | 5 | `101,102,103,104,105,...` | `10,10,10,5,1,...` | `604800`（7 天） |
| 2 | Advanced Pool | 3 | 5 | `201,202,203,...` | `10,10,5,...` | `604800`（7 天） |

（上表省略 5 個預留閘欄位；一律保持預設空 / 0）

#### 3.2.3 Player State & Candidate Card Schemas

本節定義 FT-08 的 player-level 狀態容器 `StaffPlayerState` 與面試候選卡資料結構 `CandidateCard`，用於保底 counter / nextInstanceID / 刷新計時器 / 候選保留等跨 `StaffInstance` 狀態。

**`StaffPlayerState`**（全部持久化，FT-10 序列化）：

```
StaffPlayerState {
    // 招募 counter
    nextInstanceID               : int                   // StaffInstance 分配用；初始 1，遞增永不回收

    // 保底 counter（跨池累積、公會升級不 reset，見 §3.4）
    pityCounter                  : int                   // 初始 0；每張候選 +1

    // 刷新計時器（全域單一；切池不重置；刷當前選中池）
    lastAutoRefreshTimestamp     : long                  // 最近一次自動刷新的 UTC 時間戳

    // 當前面試畫面
    currentPoolID                : int                   // 玩家當前切到哪個池（需存檔記憶）
    currentCandidates            : List<CandidateCard>   // 當前池的面試欄內容（N 張，N 依 guildLevel）

    // 跨池保留區（空間獨立於 currentCandidates）
    reservedCandidates           : List<CandidateCard>   // 被保留的候選（含 poolID 區分）
}
```

**設計註記**

- **單一全域 `lastAutoRefreshTimestamp`**：自動刷新計時器不區分池，到點時刷新 `currentPoolID` 指向的池；切池時計時器延續、不重置（玩家無法靠切池偷跑刷新節奏）
- **`currentCandidates` 僅存當前池**：切池時被覆蓋為新池 roll 結果；前池未保留的候選一律丟失（設計規則：保留區是跨池保留的唯一途徑）
- **無 `lastManualRefreshTimestamp`**：手動刷新無 cooldown、不需計時器
- **離線補刷不追蹤**：離線回來時由 `lastAutoRefreshTimestamp` 反推已過幾個間隔，clamp 補 1 次（§3.3 計算時處理）

---

**`CandidateCard`**（全部持久化，FT-10 序列化）：

```
CandidateCard {
    // 卡片識別
    poolID                       : int                   // 所屬池（跨池保留區分用）
    slotIndex                    : int                   // 面試欄位置 0..N-1（N 依 guildLevel）

    // 內容（staffID 與 trashItemID 互斥，恰好一個為 0）
    staffID                      : int                   // 職員 ID；0 = 此卡為 trash item
    trashItemID                  : int                   // trash item ID；0 = 此卡為職員
    rolledRarity                 : int                   // 1~5；trash item 固定 1
    rolledTimestamp              : long                  // 被 roll 出的 UTC 時間戳

    // 保留狀態
    isReserved                   : bool                  // 是否處於保留狀態
    reservedTimestamp            : long                  // 進入保留的 UTC 時間戳（0 = 未保留）
    reserveConsumedFlag          : bool                  // 一旦解除保留即永久 true（防刷機制）
}
```

**DataManager / runtime 驗證規則**

- `(staffID > 0) XOR (trashItemID > 0)` —— 兩者必須恰好一個為 0（互斥）
- `staffID > 0 → rolledRarity == StaffTable[staffID].rarity` —— 職員卡稀有度必須對應模板
- `0 ≤ slotIndex < N` —— N = 當前公會等級對應的面試欄張數（見 §3.3 刷新流程）
- `isReserved == true → reservedTimestamp > 0`
- `reserveConsumedFlag == true → isReserved == false` —— 永久化後不可再次保留

**生命週期時點範例**（以 `staffID=101` 克勞德·會計師為例）：

| 時點 | `isReserved` | `reservedTimestamp` | `reserveConsumedFlag` | 所在 list |
|---|---|---|---|---|
| 剛 roll 出 | `false` | `0` | `false` | `currentCandidates[2]` |
| 玩家點「保留」 | `true` | `1714064400` | `false` | `reservedCandidates` |
| 保留時限到 / 手動取消 | `false` | `0` | `true` | 回到 `currentCandidates[2]` |
| 下次 refresh 刷掉 | — | — | — | 整張卡消失（含 `reserveConsumedFlag`） |

**時點 3（保留解除後）的行為保證**（§5 Edge Cases 會引用）：

- 解除保留後卡回到 `currentCandidates[slotIndex]` 佔回原 slot 位置
- 玩家仍可對此卡選「錄用 / 不錄用」
- 「保留」UI 按鈕必須 disable（因 `reserveConsumedFlag = true`）—— 由 P-02 根據此 flag 判斷
- 下次 refresh 時無論是否已 `reserveConsumedFlag`，該 slot 被新 roll 覆蓋，原卡徹底消失

### 3.3 面試刷新流程（Refresh Flow）

本節定義四類 refresh 觸發路徑（自動 / 手動 / 切池 / 離線補刷）的 runtime 流程，並給出共享的 roll 序列、`pityCounter` 累加時點、事件發布順序，以及 6 個面試操作 API 的回傳碼合約。對應公式見 §4.1，邊緣案例見 §5.1 / §5.2，AC 見 §8.2 / §8.3 / §8.5。

#### 3.3.1 觸發路徑分類

| 路徑 | 觸發條件 | 檢查時機 | 扣費 | pityCounter | 覆蓋 currentCandidates | lastAutoRefreshTimestamp |
|---|---|---|---|---|---|---|
| Auto | `now − last ≥ autoRefreshIntervalSec(L)` | OnStaffSystemBoot Step C（一次性，§3.3.5）| 否 | += `|refreshableSlots|` | 全 N 張覆蓋 | ← `now` |
| Manual | `TryManualRefresh()` 且 `GetGold() ≥ cost` | 玩家點擊（API 入口）| 是（cost gold）| += `|refreshableSlots|` | 全 N 張覆蓋 | 不變 |
| Switch Pool | `TrySwitchPool(newPoolID)` | 玩家切池（API 入口）| 否 | += N | B 池整個重 roll | 不變 |
| Offline Refill | 啟動時 `missedIntervals ≥ 1`（即 Auto 路徑於 Boot 觸發的別稱）| OnStaffSystemBoot Step C（同上）| 否 | += N（一次） | 全 N 張覆蓋 | ← `now` |

關鍵差異：
- 僅 Auto / Offline 路徑會更新 `lastAutoRefreshTimestamp`（手動 / 切池**不重置自動刷新節奏**）
- 切池 refresh **不保留** A 池的保留 slot（`refreshableSlots = {0..N-1}` 整個重 roll）
- 手動刷新無 cooldown（防呆由 P-02 UI 0.5s disable 處理）
- **Jam 版無線上 auto refresh tick**：所有 Auto 補刷僅於 OnStaffSystemBoot 一次性執行；玩家持續在線**不會**自動刷新候選（節奏感由「離線回來看到新候選」承擔，避免 OnHourTick 訂閱與 in-game 候選突變）

#### 3.3.2 Refresh 共享流程（執行順序）

不論觸發路徑，refresh 統一執行：

```
ExecuteRefresh(refreshType, poolID):
    1. Pre-conditions：
       - IsStaffSystemUnlocked() == true（否則 STAFF_SYSTEM_LOCKED）
       - Manual: GetGold() ≥ refreshCost(guildLevel)
       - Switch: poolID 存在於 StaffGachaPoolTable AND
                 minGuildLevel ≤ currentGuildLevel ≤ maxGuildLevel AND
                 poolID != currentPoolID

    2. 扣費（僅 Manual）：
       FT05.TryDeductGold(refreshCost(guildLevel))

    3. 解除過期保留（無條件，所有路徑）：
       FOR each c in reservedCandidates:
           IF now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID):
               ReleaseReserveInternal(c)              // §3.3.5

    4. 確定 refresh slot 範圍：
       N = StaffRefreshCostTable[guildLevel].interviewSlotCount
       IF refreshType == SwitchPool:
           currentPoolID ← newPoolID
           refreshableSlots = { 0..N-1 }              // B 池整個重 roll
       ELSE:
           reservedSlotIndices = { c.slotIndex | c ∈ reservedCandidates AND c.poolID == currentPoolID }
           refreshableSlots = { 0..N-1 } \ reservedSlotIndices

    5. 對 refreshableSlots 中每個 slot 執行 RollOneSlot（§3.3.3）；
       第一個被刷新的 slot 帶 isFirstSlotInRefresh = true

    6. pityCounter ← pityCounter + |refreshableSlots|
       // 注意：保底命中時 RollOneSlot 內部已將 pityCounter 設為 0；本步驟對殘餘 slot 數累加

    7. 更新時間戳（僅 Auto / Offline）：
       lastAutoRefreshTimestamp ← now

    8. 持久化 StaffPlayerState（FT-10 觸發點）

    9. 不發布事件（refresh 本身為私有狀態，§3.3.7）
```

> **保底計數的精確時序**：保底命中發生在 `RollOneSlot(slotIndex=refreshableSlots[0])` 內部，當下將 `pityCounter ← 0`；其後 step 5 的後續 slot 各自 RollOneSlot 不再帶保底；step 6 對 `pityCounter` 累加 `|refreshableSlots|`，實際數值為 `0 + N`（保底命中當次 refresh 後 counter 為 N）。未命中保底的 refresh 為 `pityCounter += |refreshableSlots|`。

#### 3.3.3 RollOneSlot 流程（單 slot roll）

```
RollOneSlot(slotIndex, poolID, isFirstSlotInRefresh):
    1. 保底判定（僅 isFirstSlotInRefresh = true 時檢查）：
       shouldForce5Star = (pityCounter ≥ PITY_THRESHOLD)
                          AND (poolEligibleByRarity(poolID, 5) ≠ ∅)
                          AND isFirstSlotInRefresh
       IF shouldForce5Star:
           rolledRarity ← 5
           pityCounter ← 0                            // reset 立即生效
           GOTO step 3

    2. 稀有度 roll（§4.1.5 動態歸一化）：
       compute effectiveProb[1..5] using emptyTiers(poolID)
       rolledRarity ← weightedRoll(effectiveProb)

    3. 層內 staff roll（§4.1.6）：
       tierStaff = poolEligibleByRarity(poolID, rolledRarity)
       (rolledStaffID, _) ← weightedRoll(staffWeights restricted to tierStaff)

    4. Trash detection（§3.5.3）：
       IF rolledRarity == 1 AND ShouldRollTrash():
           trashItemID ← rollFromTrashItemTable()
           rolledStaffID ← 0
       ELSE:
           trashItemID ← 0

    5. 建立 CandidateCard 並寫入 currentCandidates[slotIndex]：
       card = CandidateCard {
           poolID = poolID,
           slotIndex = slotIndex,
           staffID = rolledStaffID,
           trashItemID = trashItemID,
           rolledRarity = rolledRarity,
           rolledTimestamp = now,
           isReserved = false,
           reservedTimestamp = 0,
           reserveConsumedFlag = false
       }
       currentCandidates[slotIndex] ← card
```

關鍵約束：
- 保底僅作用於該次 refresh 的**第一張被刷的 slot**（即 `refreshableSlots[0]`，可能不是 `slotIndex == 0`，例如保留鎖定了 slot 0 時）
- `pityCounter ← 0` 在保底命中後立即生效；後續 slot 的判定條件 `isFirstSlotInRefresh = false` 確保不重複觸發
- Trash detection 細節見 §3.5.3（決定 trash 觸發機率）

#### 3.3.4 公開 API 合約

| API | 簽章 | 回傳碼 | 主要副作用 |
|---|---|---|---|
| `TryManualRefresh()` | `() → RefreshResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `GOLD_INSUFFICIENT` | 扣費、覆蓋 candidates、`pityCounter +=` |
| `TrySwitchPool(int newPoolID)` | `(int) → SwitchPoolResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `POOL_NOT_FOUND` / `POOL_LEVEL_LOCKED` / `ALREADY_IN_POOL` | 切池並 refresh、`pityCounter += N` |
| `TryRecruit(int slotIndex)` | `(int) → RecruitResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` / `CANDIDATE_NOT_HIREABLE` / `ROSTER_FULL` | 建 StaffInstance、發 OnStaffHired、清空該 slot |
| `TryRejectCandidate(int slotIndex)` | `(int) → RejectResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` | 清空該 slot |
| `TryReserveCandidate(int slotIndex)` | `(int) → ReserveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` / `RESERVE_FULL` / `RESERVE_CONSUMED` | 移卡至 reservedCandidates、`isReserved = true` |
| `TryReleaseReserve(int reserveIndex)` | `(int) → ReleaseResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_INDEX` / `SLOT_OCCUPIED_BY_NEW_ROLL` | 移卡回 currentCandidates[原 slotIndex]、`reserveConsumedFlag = true` |

回傳碼語意：

| 回傳碼 | 觸發條件 |
|---|---|
| `STAFF_SYSTEM_LOCKED` | `IsStaffSystemUnlocked() == false` |
| `INVALID_SLOT` | `slotIndex < 0 OR slotIndex ≥ N` |
| `EMPTY_SLOT` | `currentCandidates[slotIndex] == null`（被保留鎖定 / refresh 未填入）|
| `INVALID_INDEX` | `reserveIndex < 0 OR reserveIndex ≥ reservedCandidates.Count` |
| `POOL_NOT_FOUND` | `StaffGachaPoolTable[newPoolID]` 不存在 |
| `POOL_LEVEL_LOCKED` | `currentGuildLevel ∉ [pool.minGuildLevel, pool.maxGuildLevel]` |
| `ALREADY_IN_POOL` | `newPoolID == currentPoolID` |
| `CANDIDATE_NOT_HIREABLE` | 該卡為 trash item（`trashItemID > 0`）|
| `ROSTER_FULL` | `roster.Count == ROSTER_CAP` |
| `RESERVE_FULL` | `reservedCandidates.Count == maxReserve`（§4.1.9）|
| `RESERVE_CONSUMED` | 該卡 `reserveConsumedFlag == true` |
| `SLOT_OCCUPIED_BY_NEW_ROLL` | 解除保留時原 slot 已被新 roll 覆蓋（Case 5.2.4）|

#### 3.3.5 離線補刷流程（OnStaffSystemBoot）

FT-08 啟動序列（FT-10 載入後立即執行）：

```
OnStaffSystemBoot():
    IF NOT IsStaffSystemUnlocked():
        RETURN                                        // 系統降級，跳過所有 refresh / pity / 薪水

    // Step A：新存檔 / 異常 timestamp 處理
    IF lastAutoRefreshTimestamp == 0 OR lastAutoRefreshTimestamp > now:
        lastAutoRefreshTimestamp ← now                // Case 5.1.1 / 5.1.3

    // Step B：解除過期保留（先於補刷，避免 race）
    FOR each c in reservedCandidates.ToList():        // 拷貝以支援 in-loop 移除
        IF now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID):
            ReleaseReserveInternal(c)
            // ReleaseReserveInternal 嘗試將卡放回 currentCandidates[c.slotIndex]
            // 若 c.poolID != currentPoolID（玩家保存時在另一池）→ 卡丟失（不放回）

    // Step C：補刷判定（§4.1.4）
    L = FT07.GetBuildingLevel(6)
    intervalSec = autoRefreshIntervalSec(L)
    missedIntervals = floor((now − lastAutoRefreshTimestamp) / intervalSec)
    refillCount = clamp(missedIntervals, 0, 1)

    IF refillCount == 1:
        ExecuteRefresh(refreshType=Auto, poolID=currentPoolID)
        // ExecuteRefresh 內部 lastAutoRefreshTimestamp ← now
    ELSE IF currentCandidates.All(c => c == null):    // 首次解鎖、currentCandidates 為空
        ExecuteRefresh(refreshType=Auto, poolID=currentPoolID)

    // Step D：薪水補發（§3.11.4）—— ⚠️ Phase 2，Jam 版整段跳過（§3.11 為 Phase 2）
    ProcessOfflineSalary()                            // Jam 版不執行（§3.11 / §3.12.3）
```

**保留卡與補刷的競態保證**（Case 5.2.4 回應）：

1. Step B 先掃描過期保留 → ReleaseReserveInternal 嘗試將卡放回 `currentCandidates[c.slotIndex]`
2. 若 Step C 觸發補刷（refillCount == 1）→ 整個 refreshableSlots（含剛放回的 slot）被重 roll → **原卡丟失**（合法行為，符合 Case 5.2.4「離線期間原 slot 已被新 roll 覆蓋」語意）
3. 若 Step C 不補刷（refillCount == 0）→ 卡保留於 slot，玩家登入後可選錄用 / 不錄用

#### 3.3.6 StaffRefreshCostTable Schema

**檔案位置**：`Assets/Resources/Data/StaffRefreshCostTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `guildLevel` | int (PK) | 1~5 | 公會等級索引 |
| `cost` | int | 100~1000 | 手動刷新費（gold；§4.1.2）|
| `interviewSlotCount` | int | 3~5 | 該等級的面試欄張數 N（§4.1.1）|

**DataManager 驗證規則**：

1. `guildLevel` PK 連續 1~5（Jam 版）；缺行拋 `StaffRefreshCostTableValidationException("missing guildLevel={n}")`
2. `cost ≥ 0`
3. `1 ≤ interviewSlotCount ≤ 5`

**Jam 版範例**（與 §7.1.1 一致）：

| guildLevel | cost | interviewSlotCount |
|---|---|---|
| 1 | 100 | 3 |
| 2 | 150 | 3 |
| 3 | 250 | 4 |
| 4 | 400 | 4 |
| 5 | 600 | 5 |

#### 3.3.7 事件發布原則

Refresh 流程本身**不發事件**（私有狀態變化）；由 refresh 結果觸發的玩家動作各自發事件：

| 動作 | 事件 | 發布時機 |
|---|---|---|
| `TryRecruit` 成功 | `OnStaffHired` | refresh 完成後玩家點錄用 → 即發 |
| `TryRejectCandidate` | （無）| 不發事件，純清空 slot |
| `TryReserveCandidate` | （無）| 不發事件，純移動 list |
| `TryReleaseReserve` | （無）| 不發事件 |
| `TrySwitchPool` 成功 | （無）| 不發事件（refresh 為私有）|
| Auto / Offline refresh | （無）| 不發事件 |

**設計理由**：refresh 是「面試欄被刷新」的私有 UI 狀態變動；P-02 由 query API（`GetCurrentCandidates`）/ polling 自行更新顯示；無需事件機制（避免事件洪水與訂閱者管理開銷）。

### 3.4 保底機制細節（Pity Mechanism Details）

公式定義於 §4.1.7 / §4.1.8、邊界案例於 §5.3、AC 於 §8.4；本節展開 runtime 細節。

#### 3.4.1 pityCounter 生命週期

| 階段 | 行為 |
|---|---|
| 新存檔初始化 | `pityCounter ← 0`（FT-08 啟動流程；AC-2）|
| 每次 refresh | `pityCounter += |refreshableSlots|`（§3.3.2 步驟 6）|
| 保底命中 | `pityCounter ← 0`（§3.3.3 步驟 1，立即生效）|
| 池中 5★ 空（pityCounter ≥ 10）| 不 reset、不觸發、繼續累積（Case 5.3.1）|
| 自然 roll 出 5★（非保底）| **不 reset**（§3.4.3）|
| 公會升級 | 不影響 |
| 切池（TrySwitchPool）| 不影響、`pityCounter += N`（同 refresh）|
| 解除保留 / 解雇 / 切態 | 不影響 |
| 系統降級至 L0 | 持久化保留、不 reset（Case 5.3.3）|
| 跨 session 保存 | FT-10 序列化 `StaffPlayerState.pityCounter`（§3.2.3）|

#### 3.4.2 保底命中流程

對應 §3.3.3 RollOneSlot 步驟 1：

```
isFirstSlotInRefresh = (slotIndex == refreshableSlots[0])
shouldForce5Star = (pityCounter ≥ PITY_THRESHOLD)
                   AND (poolEligibleByRarity(currentPoolID, 5) ≠ ∅)
                   AND isFirstSlotInRefresh
```

實作要點：

- 「該次 refresh 第一張」用 `isFirstSlotInRefresh` 旗標而非 `pityCounter ≥ 10` 判定——因為命中後 counter 立刻 reset，不能再用 counter 值判斷後續 slot 是否仍處於保底狀態
- `refreshableSlots[0]` **不一定 = `slotIndex 0`**：若玩家保留了 slot 0、該次 refresh 跳過 slot 0，則 `refreshableSlots[0] = 1`，保底作用於 slot 1
- 命中後 RollOneSlot 進入 step 3「層內 staff roll」走 §4.1.6 權重；若 5★ 層含多位職員，按 staffWeights 比例 roll 具體 staffID

#### 3.4.3 保底未觸發路徑

```
shouldForce5Star == false 時：
    rolledRarity ← weightedRoll(effectiveProb)        // §4.1.5

    互動：
    (a) pityCounter < 10：正常 roll；roll 出 5★ 不 reset
    (b) pityCounter ≥ 10 但池 5★ 空：正常 roll；pityCounter 繼續累積
    (c) pityCounter ≥ 10 且 isFirstSlotInRefresh = false（保底已在前 slot 兌現後 reset）：正常 roll
```

**設計決策（§7.4 不可調）**：自然 roll 命中 5★ 不 reset counter——保底進度與自然命中獨立計算，避免「自然抽到 5★ 後保底進度被消除」的玩家不公平感。

#### 3.4.4 與動態歸一化的互動

當 `pityCounter ≥ 10` 但 `emptyTiers` 包含 5★：

```
保底判定 → shouldForce5Star = false（poolEligibleByRarity == ∅）
↓
正常 roll 走 §4.1.5：emptyTiers = {5} → effectiveProb 在 1~4 重新分配
↓
pityCounter += |refreshableSlots|（繼續累積）
↓
玩家下一次 refresh 仍可能在同池被擋（5★ 仍空）→ counter 繼續上升
↓
玩家切到含 5★ 的池 / 升等開放 5★ → 下次 refresh 兌現
```

**counter 上限**：理論無上限；實務 Jam 版玩家不太可能累積到 30+（除非整段 game session 都待在 5★ 空池）。

#### 3.4.5 跨 session 行為

FT-10 載入時驗證：

| 欄位 | 載入時驗證 | 違反處理 |
|---|---|---|
| `pityCounter` | `≥ 0`、整數 | 拋 `StaffPlayerStateValidationException("pityCounter invalid")` |

**5★ 空池 + counter 累積的存檔遷移情境**：

- 保存時 `pityCounter = 15`、`currentPoolID = 2`（B 池 5★ 空）→ 載入後 counter 仍 15
- OnStaffSystemBoot Step C 補刷時若 B 池 5★ 仍空 → 不觸發強制 5★、counter 繼續 +N
- 玩家切到 A 池（含 5★）→ 切池 refresh 立即兌現第一張 5★

#### 3.4.6 Debug Reset 契約

供開發 / QA 使用，**不對外暴露**（不在 P-02 UI、不在玩家 API）：

```csharp
[Conditional("UNITY_EDITOR")]
internal static void DebugResetPityCounter() {
    StaffPlayerState.pityCounter = 0;
    // 不發事件、不持久化（待下次 SaveGame 自然寫入）
}
```

安全範圍：

- 僅於 Editor build 編譯（`UNITY_EDITOR` 條件）
- `internal` scope（同 assembly 訪問）
- 無事件、無扣費、無 refresh 觸發；純改 counter 值

### 3.5 垃圾物品（Trash Items）

#### 3.5.1 概念與職責邊界

Trash item = 抽卡的「空抽」結果，於 CandidateCard 以 `trashItemID > 0` / `staffID == 0` 表示。職責邊界：

| 屬性 | Trash Item | Filler Staff（`isFiller=true`）|
|---|---|---|
| 入職 | ❌ | ✅ |
| 占名冊 | ❌ | ✅（計入 ROSTER_CAP）|
| 領薪水 | ❌ | ✅（薪水可能 0）|
| 提供 effect | ❌ | ✅（極弱 / 空）|
| 解雇 | ❌ | ✅（資遣費可能 0）|
| 在 CandidateCard | `trashItemID > 0`、`staffID = 0` | `staffID > 0`、`trashItemID = 0` |
| 玩家可選操作 | reject / reserve | recruit / reject / reserve |
| 計入 pityCounter | ✅（每張 +1，如同 staff card）| ✅（同左）|

#### 3.5.2 TrashItemTable Schema

**檔案位置**：`Assets/Resources/Data/TrashItemTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `trashItemID` | int (PK) | ≥ 1 | 唯一識別碼 |
| `name` | string | 非空 | 物品名稱（顯示用，例：「咖啡杯」「履歷紙團」）|
| `flavorText` | string | 非空 | 風味文字（writer agent 提供）|
| `iconAssetID` | string | `""` | 視覺資產 ID 預留欄；Jam 版使用通用 trash icon |

**DataManager 驗證規則**：

1. `trashItemID ≥ 1`；不可與任一 `StaffTable.staffID` 衝突 → 拋 `TrashItemTableValidationException("trashItemID={id} collides with StaffTable")`
2. `name` / `flavorText` 非空 / 非 null
3. PK unique（同表內）

**Jam 版範例**：

| trashItemID | name | flavorText |
|---|---|---|
| 9001 | 咖啡杯 | 冷掉的咖啡，杯緣印著陌生的口紅印。 |
| 9002 | 履歷紙團 | 揉爛的履歷，最上方寫著「無經驗無熱情」。 |
| 9003 | 過期身份證 | 拍照時的笑容已經過期五年。 |
| 9004 | 借據 | 不知道借了誰多少錢。 |
| 9005 | 退稿小說 | 第一章寫得很好，第二章開始就崩了。 |

數量建議 5~15 個（§7.1.6）。

#### 3.5.3 Trash Roll 觸發機率

`ShouldRollTrash()` 於 §3.3.3 步驟 4 引用。Jam 版規則：

```
ShouldRollTrash():
    IF rolledRarity != 1: return false                // 僅 1★ 可變 trash
    trashRate = TRASH_ROLL_RATE_AT_RARITY_1           // 預設 0.30（§7.2 可調）
    return Random.value < trashRate
```

**設計依據**：

- 1★ 在 baseProb 為 0.40 → trash 全體機率 ≈ 0.40 × 0.30 = 0.12（與 §7.1.6 「1★ 層內 flavor 占 30%、全體約 12%」一致）
- 不影響 4★ / 5★ 結果（不會搶走玩家高價值 roll）
- 中後期玩家解鎖 B 池（3★~5★ 為主、1★ 層空）→ trash 自動消失（emptyTiers 含 1）

#### 3.5.4 Trash Roll 後續行為

對應 CandidateCard schema（§3.2.3）：

```
roll 結果為 trash → CandidateCard {
    staffID = 0,
    trashItemID = (rollFromTrashItemTable),
    rolledRarity = 1,                                 // trash 強制 1
    ...
}
```

玩家對 trash card 的可選操作：

| 操作 | 行為 |
|---|---|
| `TryRecruit(slotIndex)` | 回 `CANDIDATE_NOT_HIREABLE`（AC-22）；不變動任何狀態 |
| `TryRejectCandidate(slotIndex)` | 與 staff card 同——清空 slot |
| `TryReserveCandidate(slotIndex)` | 允許保留（trash 也佔保留 slot）；UI 可選 disable 但 FT-08 不 enforce |

**設計理由**：保留 trash 在 Jam 版屬合法行為（玩家可能有圖鑑收集慾，雖 Jam 版無圖鑑）；UI 層可額外提示「這張看起來不重要」但不阻擋。

#### 3.5.5 Trash 計入 pityCounter

§4.1.7 已定義 `pityCounter += N`，每張 +1 不分 staff / trash。Trash 計入 pity 的設計理由：

- 保底語意 = 「累積失望張數的補償」；trash 比 1★ 普通職員更失望，理應計入
- 簡化實作：refresh 流程不需在 RollOneSlot 完成後逐張判斷類型再決定 +N
- 不破壞 §3.4.3「自然命中 5★ 不 reset」原則：5★ 不可能是 trash（trash 限 rarity=1）

#### 3.5.6 Filler Staff 與 Trash 的差異化處理

**Filler staff**（`StaffTable.isFiller = true`）：

- 仍是 `StaffInstance`、有 `instanceID`、可指派、可解雇
- effect 數值極弱（如 `Willingness +0.01`）或空集合
- 薪水可低至 0、severancePay 可為 0
- 提供「灰階人物」flavor，避免 1★ 全是強力職員的奇異感

**Trash item**：

- 純 UI flavor、不入名冊、不消耗 ROSTER_CAP
- 無 `instanceID`、無狀態、無事件
- 玩家「處理掉」= 等下次 refresh 覆蓋 / 主動 reject

**池內配比建議**（§7.1.3 / §7.1.6）：1★ 層內 staff 與 trash 的比例約 7:3（trashRate = 0.30）；filler staff 占 1★ staff 的 30~50%（保留少量風味平庸職員）。

### 3.6 效果聚合加成計算（Effect Aggregation）

FT-08 對外提供 **5 個查詢 API**，分兩組：

**A. 三個固定名稱加成 API**（FT-03 / FT-05 已硬編碼調用）：

```csharp
float GetStaffWillingnessBonus();
float GetAccountantCommissionBonus();
float GetAccountantPenaltyBonus();
```

**B. 兩個本 GDD 新增 API**（對應 `RecruitRefreshOnCounter` effect + `SuccessRatePreview` UI flag）：

```csharp
int  GetRecruitRefreshReductionSec();   // 消費者：FT-01 Adventurer Recruitment
bool IsSuccessRatePreviewEnabled();     // 消費者：P-02 Main UI（委託審核畫面）
```

**Effect / UI flag → API 映射（固定寫死，不走資料表）**：

| 來源 | 來源 ID | 聚合至 API | 聚合語意 |
|---|---|---|---|
| `StaffEffect` | `Willingness` | `GetStaffWillingnessBonus` | SUM + 上限 |
| `StaffEffect` | `AccountantCommission` | `GetAccountantCommissionBonus` | SUM + 上限 |
| `StaffEffect` | `AccountantPenaltyOnVault` | `GetAccountantPenaltyBonus` | SUM + 下限（負值） |
| `StaffEffect` | `RecruitRefreshOnCounter` | `GetRecruitRefreshReductionSec` | SUM + 上限 |
| `StaffUIFlag` | `SuccessRatePreview` | `IsSuccessRatePreviewEnabled` | **OR**（任一滿足即 true） |

#### 3.6.1 狀態生效規則

依效果類型決定哪些職員狀態下該效果生效：

| Effect / Flag 類型 | `Working` | `Reallocating` | `OnLeave` |
|---|---|---|---|
| **Passive**（`Willingness`, `AccountantCommission`） | ✅ | ✅ | ❌ |
| **Slot**（`AccountantPenaltyOnVault`, `RecruitRefreshOnCounter`） | ✅（且 `assignedBuildingID` 符合） | ❌ | ❌ |
| **UI Flag**（`SuccessRatePreview`） | ✅（且 `assignedBuildingID == uiFlagBuildingIDs[i]`） | ❌ | ❌ |

**關鍵語意**：Slot effects 在「再分配」狀態**不**提供加成——A2 案下再分配是職員「未指派任一建築、等待玩家選 buildingID」的等待態（`assignedBuildingID == 0` 為其不變式），自然不滿足任一 slot effect 的 `assignedBuildingID == X` 條件；Passive effects 在再分配中仍提供（職員仍在公會名冊、提供持續影響力）。切建築（Working @A → Working @B）**不進 Reallocating**，slot effects 立即切換到新建築。

#### 3.6.2 聚合演算法

```
GetStaffWillingnessBonus():
    IF NOT FT07.IsStaffSystemUnlocked(): return 0f      // 系統降級（§3.12）

    sum = 0
    FOR each staff in activeRoster:
        IF staff.currentState == OnLeave: continue       // Passive 不在休假中生效
        // state ∈ {Working, Reallocating}，對 Passive 皆生效
        idx = indexOf(Willingness, staff.effectIDs)
        IF idx >= 0:
            sum += staff.effectValues[idx]
    sum = min(sum, EFFECT_MAX_WILLINGNESS_BONUS)         // 全域上限封頂（§7）
    return sum

GetAccountantCommissionBonus():
    IF NOT FT07.IsStaffSystemUnlocked(): return 0f
    sum = 0
    FOR each staff in activeRoster:
        IF staff.currentState == OnLeave: continue
        idx = indexOf(AccountantCommission, staff.effectIDs)
        IF idx >= 0:
            sum += staff.effectValues[idx]
    sum = min(sum, EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS)
    return sum

GetAccountantPenaltyBonus():
    IF NOT FT07.IsStaffSystemUnlocked(): return 0f
    sum = 0
    FOR each staff in activeRoster:
        IF staff.currentState != Working: continue        // Slot effect 僅工作中生效
        idx = indexOf(AccountantPenaltyOnVault, staff.effectIDs)
        IF idx >= 0 AND staff.assignedBuildingID == 5:    // 已指派預備金保險櫃
            sum += staff.effectValues[idx]                // effectValue 為負（如 -0.02）
    sum = max(sum, EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS)   // 下限封頂（負值；絕對值不大於上限）
    return sum

GetRecruitRefreshReductionSec():
    IF NOT FT07.IsStaffSystemUnlocked(): return 0
    sum = 0
    FOR each staff in activeRoster:
        IF staff.currentState != Working: continue         // Slot effect 僅工作中生效
        idx = indexOf(RecruitRefreshOnCounter, staff.effectIDs)
        IF idx >= 0 AND staff.assignedBuildingID == 4:     // 已指派公會櫃臺
            sum += (int)staff.effectValues[idx]            // 單位秒（如 7200 = 2h）
    sum = min(sum, EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC)
    return sum
```

#### 3.6.3 疊加上限（Q3A-11 B）

| 常數 | 建議值 | 意義 |
|---|---|---|
| `EFFECT_MAX_WILLINGNESS_BONUS` | `+0.20` | Willingness 總加成上限 |
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | `+0.10` | 會計傭金總加成上限 |
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | `-0.10` | 會計賠償總加成下限（絕對值上限 0.10） |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | `14400`（4h） | 招募刷新減量總和上限（防止刷新近乎即時） |

- 實際值於 §7 調參；Jam 版以上述保守值開始
- 用 `min()` / `max()` 截斷確保疊加不破壞 FT-03 / FT-05 的 balance 假設
- 防止玩家刷滿同 effect 職員讓 rate 溢出合理區間（FT-05 `effectivePenaltyRate` 已有 `max(0, ...)` 下限但上限無防護）

#### 3.6.4 Slot Capacity 自然封頂

`AccountantPenaltyOnVault` 的 slot 條件 `assignedBuildingID == 5` 受 `BuildingTable.slotCount` 約束（Jam 版預備金保險櫃預計 `slotCount = 1`，即同時僅一位會計可啟用該加成）。詳見 §3.7 slot 指派 / capacity 規則。此約束為**實體 slot 數量**的天花板，與 §3.6.3 的**數值**上限正交。

#### 3.6.5 降級行為（系統閘）

當 `FT07.IsStaffSystemUnlocked() == false`（職員休息室未建造）：

- **加成類 API**（`GetStaffWillingnessBonus` / `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` / `GetRecruitRefreshReductionSec`）直接回 `0`，略過 roster 遍歷（早退）
- **布林類 API**（`IsSuccessRatePreviewEnabled`）直接回 `false`
- 符合 FT-03 §5 / FT-05 §3.4「FT-08 未招募 / 系統缺席 → 回 0」的降級契約

#### 3.6.6 UI Flag 聚合（OR 語意）

UI flag 走獨立聚合路徑，**不經 `effectIDs` / `effectValues`**，避免污染 float-typed effect 系統。

```
IsSuccessRatePreviewEnabled() → bool:
    IF NOT FT07.IsStaffSystemUnlocked(): return false
    FOR each staff in activeRoster:
        IF staff.currentState != Working: continue
        idx = indexOf(SuccessRatePreview, staff.uiFlagIDs)
        IF idx >= 0 AND staff.assignedBuildingID == staff.uiFlagBuildingIDs[idx]:
            return true                                    // OR 聚合：一個符合即 true
    return false
```

**設計差異（vs effect 聚合）**：

- **OR 語意 vs SUM 語意**：UI flag 是「功能有無」，任一職員符合即啟用；不像 effect 會累加
- **無疊加上限需求**：bool 無法疊加，所以無 `EFFECT_MAX_*` 類常數
- **DataManager 驗證**：`uiFlagIDs.Count == uiFlagBuildingIDs.Count`，且每個 `uiFlagBuildingIDs[i]` 必須存在於該職員的 `slotBuildingIDs` 中（否則 UI flag 永遠無法啟用，資料表錯誤）

### 3.7 Slot 指派流程（Slot Assignment）

#### 3.7.1 概念與資料對應

Slot = 建築（buildingID）對職員提供的「指派位置」。每個建築的 slot capacity 由 `BuildingTable[buildingID].slotCount`（FT-07 §7.1）決定；職員的 `slotBuildingIDs` CSV list 定義該職員的合格建築清單。

| 概念 | 來源 | 範例 |
|---|---|---|
| 職員可指派的建築清單 | `StaffTable.slotBuildingIDs` | `[4, 5]`（櫃台或保險櫃二擇一）|
| 建築可容納的職員數 | `FT07.BuildingTable[buildingID].slotCount` | 保險櫃 = 1、委託板 = 3、櫃台 = 2（範例值）|
| 職員實際指派位置 | `StaffInstance.assignedBuildingID` | 0 = 未指派、4 = 在櫃台 |
| 切換建築冷卻 | `StaffInstance.buildingSwitchCooldownEndTimestamp` | UTC 秒；切建築 / 取消指派後 `cooldownEnd ← now + 7200s`（§3.8.4）|

#### 3.7.2 公開 API 合約

| API | 簽章 | 回傳碼 |
|---|---|---|
| `TryAssignStaff(int instanceID, int buildingID)` | `(int,int) → AssignResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `BUILDING_NOT_ELIGIBLE` / `BUILDING_FULL` / `STAFF_ON_LEAVE` / `SWITCH_COOLDOWN` |
| `TryUnassignStaff(int instanceID)` | `(int) → UnassignResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `STAFF_NOT_ASSIGNED` / `STAFF_ON_LEAVE` |

#### 3.7.3 TryAssignStaff 完整流程

```
TryAssignStaff(instanceID, buildingID):
    1. 系統閘：IsStaffSystemUnlocked() == false → STAFF_SYSTEM_LOCKED
    1.5 入口轉假掃描：CheckReallocatingAutoLeave()      // §3.9.3；先處理逾時轉假再讀 staff state
    2. 找職員：staff = roster.Find(instanceID)；找不到 → STAFF_NOT_FOUND
    3. 合格性：buildingID ∉ staff.slotBuildingIDs → BUILDING_NOT_ELIGIBLE
    4. 狀態檢查：staff.currentState == OnLeave → STAFF_ON_LEAVE
    5. 冷卻檢查：now < staff.buildingSwitchCooldownEndTimestamp → SWITCH_COOLDOWN
    6. Capacity 檢查：
       currentInBuilding = roster.Count(s => s.assignedBuildingID == buildingID
                                              AND s.currentState == Working)
       capacity = FT07.BuildingTable[buildingID].slotCount
       IF staff.assignedBuildingID != buildingID AND currentInBuilding ≥ capacity:
           return BUILDING_FULL

    7. 執行指派：
       oldBuildingID = staff.assignedBuildingID
       oldState      = staff.currentState

       IF oldBuildingID == buildingID:
           return SUCCESS                              // idempotent；no-op、不發事件

       staff.assignedBuildingID = buildingID

       // A2 案（2026-04-25 使用者裁示）：Reallocating 不再用於切建築過渡
       // TryAssignStaff(X) 成功永遠 → Working；切建築冷卻由 cooldownEnd 獨立約束
       staff.currentState = Working
       staff.reallocatingStartTimestamp = 0              // 落位後重置 12h 倒數

       IF oldBuildingID > 0:
           // 切建築（Working @A → Working @B）→ 設 7200s cooldown 防短期反覆切換
           staff.buildingSwitchCooldownEndTimestamp = now + BUILDING_SWITCH_COOLDOWN_SECONDS
       // ELSE: oldBuildingID == 0（剛錄用 / unassign 後 / OnLeave 回崗後首次落位）
       //       不設冷卻；玩家首次選 buildingID 不應被冷卻擋下

    8. 發布事件（順序見 §3.13.2）：
       Publish OnStaffAssigned { instanceID, oldBuildingID, newBuildingID = buildingID }
       IF oldState != staff.currentState:
           Publish OnStaffStateChanged { instanceID, oldState, newState = staff.currentState }

    9. return SUCCESS
```

> **Reallocating 設計意圖（A2 案 / 2026-04-25 使用者裁示）**：Reallocating **僅**表示「職員已不在任何建築工作、等待玩家選 buildingID 落位」（`assignedBuildingID == 0` 為其不變式）；**不**用於切建築過渡。設計目的是阻止玩家用「Working → Reallocating（unassign）→ Working（重 assign）」鏈條繞過切建築冷卻——故 §3.7.4 TryUnassignStaff 仍設 cooldownEnd = now + 7200s，玩家必須等冷卻才能重 assign 落位。
>
> **Reallocating 三大進入路徑**（皆 `assignedBuildingID = 0`）：
> 1. **錄用後等待**：剛錄用且 `slotBuildingIDs` 含 `> 0` 的 buildingID（§3.8.2）；玩家須 TryAssignStaff(X) 落位 Working
> 2. **取消指派**：Working → TryUnassignStaff → Reallocating（cooldownEnd = now + 7200s）；玩家須等冷卻 + TryAssignStaff(X) 重新落位
> 3. **從休假回崗**：OnLeave → TryReturnFromLeave → Reallocating（cooldown 沿用既有）；玩家須 TryAssignStaff(X) 落位
>
> **切建築不進 Reallocating**（A2 核心）：Working @A → TryAssignStaff(B) 直接 Working @B（assignedBuildingID 改、cooldownEnd = now + 7200s、state 不變）；slot effects 在新建築立即生效（無過渡空窗）；玩家 7200s 內無法再切建築（被 SWITCH_COOLDOWN 擋下）。
>
> **12h auto-leave 適用範圍**（§3.9）：僅對三大 Reallocating 入口的職員計時；切建築職員為 Working 不計時、永不被 auto-leave 影響。

#### 3.7.4 TryUnassignStaff 完整流程

```
TryUnassignStaff(instanceID):
    1. 系統閘 / 入口轉假掃描 / 找職員（同 §3.7.3 步驟 1 / 1.5 / 2）
    2. staff.assignedBuildingID == 0 → STAFF_NOT_ASSIGNED
    3. staff.currentState == OnLeave → STAFF_ON_LEAVE
    4. 執行取消指派：
       oldBuildingID = staff.assignedBuildingID
       oldState      = staff.currentState
       staff.assignedBuildingID = 0
       staff.currentState = Reallocating
       staff.reallocatingStartTimestamp = now
       staff.buildingSwitchCooldownEndTimestamp = now + BUILDING_SWITCH_COOLDOWN_SECONDS

    5. 發布事件：
       Publish OnStaffAssigned { instanceID, oldBuildingID, newBuildingID = 0 }
       IF oldState != Reallocating:
           Publish OnStaffStateChanged { instanceID, oldState, newState = Reallocating }

    6. return SUCCESS
```

#### 3.7.5 多 slot 候選的指派決策

當 `staff.slotBuildingIDs.Count > 1` 時（例：可選櫃台或保險櫃），玩家透過 P-02 UI 選擇實際指派位置；FT-08 不提供「自動最佳建築」邏輯（避免設計黑箱、玩家失控）。P-02 UX 細節見 A.4 對 P-02 的雙向聲明。

#### 3.7.6 Effect 聚合的指派時機影響

| 階段 | Slot effect 生效 | Passive effect 生效 |
|---|---|---|
| Reallocating（`assignedBuildingID = 0`、等待落位）| ❌ | ✅ |
| Working、`assignedBuildingID = 0`（無 slot 能力職員，§3.8.2）| ❌ | ✅ |
| Working、`assignedBuildingID` 符合 effect 條件 | ✅ | ✅ |
| Working、`assignedBuildingID` 不符合 effect 條件 | ❌ | ✅ |
| OnLeave | ❌ | ❌ |

**重新聚合時機**：FT-08 不快取聚合結果（§3.6 演算法每次調用即時遍歷 roster），因此 `OnStaffAssigned` / `OnStaffStateChanged` 事件不需要主動觸發 cache invalidation；下游（FT-01 / FT-03 / FT-05）按需 query 即可拿到最新值。

#### 3.7.7 Capacity 衝突的特殊情境

| 情境 | 行為 |
|---|---|
| 兩位都帶 `AccountantPenaltyOnVault`、保險櫃 capacity = 1 | 第二位 TryAssignStaff(5) 回 `BUILDING_FULL`；玩家須先取消第一位指派或解雇 |
| Reallocating 是否占 capacity？ | **不占**（A2 案下 Reallocating 必然 `assignedBuildingID == 0`，自然不對應任何 buildingID 的 capacity）|
| OnLeave 是否占 capacity？ | **不占**（同上）|
| 同 staff 重複 TryAssignStaff 同 buildingID | idempotent；no-op、不發事件、回 SUCCESS |

---

### 3.8 三態狀態機（Staff State Machine）

#### 3.8.1 狀態定義

| State | 語意 | Passive Effect | Slot Effect | 領薪水 |
|---|---|---|---|---|
| `Working` | 在指派的建築工作中 | ✅ | ✅（assignedBuildingID 符合）| ✅ |
| `Reallocating` | 未指派任一建築、等待玩家選 buildingID（A2 案；入口：錄用後 / 取消指派 / 從休假回崗）| ✅ | ❌ | ✅ |
| `OnLeave` | 休假中 | ❌ | ❌ | ❌（§4.3.1 排除）|

#### 3.8.2 狀態轉移總表

| 起始狀態 | 終止狀態 | 觸發 | 副作用 |
|---|---|---|---|
| (錄用) | `Working` | `TryRecruit` 成功且 `slotBuildingIDs` 為空或全 `0`（無 slot 能力，§3.2.1）| state = Working、assignedBuildingID = 0 |
| (錄用) | `Reallocating` | `TryRecruit` 成功且 `slotBuildingIDs` 含至少一個 `> 0` 的 buildingID | state = Reallocating、assignedBuildingID = 0、reallocatingStart = now |
| `Reallocating` | `Working` | `TryAssignStaff(buildingID)` 落位（oldBuildingID = 0）| assignedBuildingID = buildingID、reallocatingStart = 0；**cooldown 不重置**（首次落位無冷卻 / unassign 後沿用既有 cooldown）|
| `Working` | `Working` | `TryAssignStaff(otherBuildingID)` 切建築（oldBuildingID > 0、A2 案）| assignedBuildingID = otherBuildingID、`cooldownEnd ← now + 7200s`、reallocatingStart 保持 0 |
| `Working` | `Reallocating` | `TryUnassignStaff` | assignedBuildingID = 0、reallocatingStart = now、`cooldownEnd ← now + 7200s` |
| `Reallocating` | `OnLeave` | `now − reallocatingStart ≥ REALLOCATING_AUTO_LEAVE_SECONDS`（§3.9）| assignedBuildingID = 0、reallocatingStart = 0 |
| `Working` | `OnLeave` | `TryGoOnLeave(instanceID)` | assignedBuildingID = 0、reallocatingStart = 0 |
| `Reallocating` | `OnLeave` | `TryGoOnLeave(instanceID)` | 同上 |
| `OnLeave` | `Reallocating` | `TryReturnFromLeave(instanceID)` | reallocatingStart = now（玩家須在 12h 內 TryAssignStaff，否則自動轉假）|
| 任何 | (銷毀) | `TryFireStaff(instanceID)` | StaffInstance 從 roster 移除（§3.10）|

> **Reallocating 入口語意統一（A2 案，2026-04-25 裁示）**：Reallocating 永遠表示「`assignedBuildingID == 0`、等待玩家選 buildingID」，**不再用於切換建築過渡**。三大進入路徑——錄用後（含 slot 能力）/ 取消指派 / 從休假回崗。切建築走 `Working → Working`（§3.8.2，`cooldownEnd ← now + 7200s`），不經 Reallocating；同時 `Working → Reallocating（unassign）` 也設冷卻，避免「Working → Reallocating → Working」鏈條繞過 7200s 切建築冷卻。Reallocating 帶 `reallocatingStartTimestamp` 計時器；該計時器於進入新指派（Working）或進入休假（OnLeave）時 reset 為 0。

#### 3.8.3 公開 API 合約（狀態切換）

| API | 簽章 | 回傳碼 |
|---|---|---|
| `TryGoOnLeave(int instanceID)` | `(int) → GoOnLeaveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `ALREADY_ON_LEAVE` |
| `TryReturnFromLeave(int instanceID)` | `(int) → ReturnFromLeaveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `NOT_ON_LEAVE` |
| `TryAssignStaff` / `TryUnassignStaff` | 見 §3.7.2 | 見 §3.7.2 |

#### 3.8.4 冷卻欄位語意

`buildingSwitchCooldownEndTimestamp` 與 `currentState` 正交（§3.1 已述）：

| 情境 | 冷卻處理 |
|---|---|
| Working → Working（切建築，A2 案）| `cooldownEnd ← now + 7200s` |
| Working → Reallocating（取消指派）| `cooldownEnd ← now + 7200s` |
| Working / Reallocating → OnLeave（休假）| 不重置（持續倒數）|
| OnLeave → Reallocating（回崗）| 不重置（沿用既有冷卻）|
| Reallocating → OnLeave（自動轉假，§3.9）| 不重置 |
| Reallocating → Working（落位）| 不重置（首次落位無冷卻 / unassign 後沿用既有冷卻）|
| 跨 session 載入 | 冷卻照舊（時間戳持久化）|

**設計理由**：冷卻針對「玩家短時間反覆切建築操弄聚合結果」；切建築與取消指派均設冷卻，避免「Working → Reallocating（unassign）→ Working（重 assign）」鏈條繞過切換限制；休假 / 落位本身不重置冷卻，避免「切建築 → 休假 → 馬上回崗 → 切到新建築」的繞過。

#### 3.8.5 持久化欄位驗證

| 欄位 | 持久化 | 載入時驗證 |
|---|---|---|
| `currentState` | ✅ | enum 值合法 |
| `assignedBuildingID` | ✅ | OnLeave → 0；Reallocating → 0（A2 不變式）；Working → ≥ 0（≥ 1 = 佔用 slot；= 0 僅當 staff 無 slot 能力，§3.8.2 row 1）|
| `reallocatingStartTimestamp` | ✅ | state == Reallocating → > 0；其他 → 0 |
| `buildingSwitchCooldownEndTimestamp` | ✅ | ≥ 0 |

驗證違反 → 拋 `StaffInstanceValidationException`（FT-10 載入時）。

#### 3.8.6 狀態查詢 API（內部）

供 P-02 UI 讀取顯示用，不在玩家 API 範圍：

```csharp
internal static StaffStateView GetStaffStateView(int instanceID);
// 回傳 { currentState, assignedBuildingID, reallocatingRemainingSec, switchCooldownRemainingSec }
//   - reallocatingRemainingSec = max(0, reallocatingStart + REALLOCATING_AUTO_LEAVE_SECONDS − now)；
//                                state ≠ Reallocating 時為 0
//   - switchCooldownRemainingSec = max(0, switchCooldownEnd − now)
```

---

### 3.9 再分配自動轉休假（Auto-Leave on Long Reallocation）

#### 3.9.1 設計動機

避免玩家「錄用後 / unassign 後 / 從休假回崗後忘記選 buildingID」造成 Reallocating 狀態無限延伸；強制 12h 後自動轉 OnLeave，使玩家必須主動回崗（TryReturnFromLeave + TryAssignStaff）才能繼續使用該職員的 Slot effect。

> A2 案（2026-04-25 使用者裁示）下，**切建築直接 Working、不進 Reallocating**，因此本節規則只對三大入口（剛錄用 / unassign 後 / OnLeave 回崗後）的 Reallocating 職員適用。

#### 3.9.2 觸發條件

```
shouldAutoLeave(staff) =
    (staff.currentState == Reallocating)
    AND (staff.reallocatingStartTimestamp > 0)
    AND (now − staff.reallocatingStartTimestamp ≥ REALLOCATING_AUTO_LEAVE_SECONDS)
```

`REALLOCATING_AUTO_LEAVE_SECONDS = 43200`（12h；§7.2）。

#### 3.9.3 檢查時機

```
CheckReallocatingAutoLeave():
    IF NOT IsStaffSystemUnlocked(): return
    FOR each staff in roster:
        IF shouldAutoLeave(staff):
            oldState = staff.currentState
            staff.currentState = OnLeave
            staff.assignedBuildingID = 0
            staff.reallocatingStartTimestamp = 0
            Publish OnStaffStateChanged { instanceID = staff.instanceID, oldState, newState = OnLeave }
```

呼叫點：

- **OnStaffSystemBoot**：處理離線期間累積的轉假
- **OnHourTick**（FT-02 訂閱）：線上每小時掃描一次
- **TryAssignStaff / TryUnassignStaff / TryRecruit / TryFireStaff 入口**：避免 race condition（玩家剛要操作就觸發轉假，造成回傳碼不一致）

#### 3.9.4 與冷卻的互動

`buildingSwitchCooldownEndTimestamp` **不**因自動轉假重置（§3.8.4）。玩家被自動轉假後 TryReturnFromLeave 回 Reallocating → 仍受原冷卻約束（直到 cooldown 結束才能 TryAssignStaff）。

#### 3.9.5 邊緣情境

| 情境 | 行為 |
|---|---|
| 玩家 unassign → 12h 內未指派 | 自動轉 OnLeave |
| 玩家 assign 切建築（A2 案）| **不進 Reallocating** → 直接 Working @ 新建築；不受 12h auto-leave 影響（state 已為 Working）|
| 玩家在 11h59m 時 TryAssignStaff 落位 | Reallocating → Working、不轉假、reallocatingStart = 0 |
| 玩家離線 24h | OnStaffSystemBoot 時批次轉假所有 reallocating > 12h 的職員 |
| 系統降級期間 | CheckReallocatingAutoLeave 不執行；reallocatingStartTimestamp 凍結；解鎖後 OnStaffSystemBoot 處理 |
| 系統降級恰好跨 12h | 解鎖後立即批次轉假所有跨閾值職員 |

---

### 3.10 解雇流程（Fire Flow）

#### 3.10.1 公開 API

```csharp
TryFireStaffResult TryFireStaff(int instanceID);
// 回傳碼：SUCCESS / STAFF_SYSTEM_LOCKED / STAFF_NOT_FOUND
```

#### 3.10.2 完整流程

```
TryFireStaff(instanceID):
    1. 系統閘：IsStaffSystemUnlocked() == false → STAFF_SYSTEM_LOCKED
    1.5 入口轉假掃描：CheckReallocatingAutoLeave()      // §3.9.3；解雇 OnLeave 職員不影響流程，但維持 §3.9.3 入口契約一致
    2. 找職員：staff = roster.Find(instanceID)；找不到 → STAFF_NOT_FOUND
    3. 計算資遣費：
       severancePay = StaffTable[staff.staffID].severancePay
    4. 扣款（不檢查餘額）：
       FT05.AddGoldAllowBankruptcy(-severancePay)
       // 允許進入負債（FT-05 §3.9 既有契約）；解雇是強制動作不可阻擋
    5. 發布事件（先於 roster.Remove，§3.10.4）：
       Publish OnStaffFired {
           instanceID = staff.instanceID,
           staffID = staff.staffID,
           firedTimestamp = now,
           severancePaid = severancePay
       }
    6. 從 roster 移除：
       roster.Remove(staff)
    7. 不 reset pityCounter / nextInstanceID
    8. return SUCCESS
```

#### 3.10.3 不可逆性

- 解雇後 `instanceID` 不再回收（§3.1）；同存檔內永不重複
- 任一狀態（Working / Reallocating / OnLeave）皆可解雇
- 解雇者的 cooldown / reallocatingStart / assignedBuildingID 等欄位隨 StaffInstance 一同銷毀
- 不可撤銷；玩家「再次抽到同 staffID」會建立新的 StaffInstance（新 instanceID）

#### 3.10.4 事件發布 vs roster.Remove 順序

**設計決策（§7.4 不可調）**：發布 `OnStaffFired` **先於** `roster.Remove(staff)`。

理由：

- 訂閱者可能需要在 staff 仍存在時取得最後狀態（例：通知 UI「您解雇了 [name]」需 query staff.staffID 對應 StaffTable.name）
- 訂閱者若關心「解雇後的聚合值」，應於 callback 內 query 時自行 filter `instanceID != firedInstanceID`，或訂閱時使用 deferred callback
- §3.6 演算法為即時遍歷無快取——`roster.Remove` 後任何後續 query 立即反映新值，無 cache 失效需求

#### 3.10.5 解雇條件與失敗情境

| 情境 | 行為 |
|---|---|
| 玩家金幣不足 severancePay | 仍允許解雇；FT-05 進入負債（AddGoldAllowBankruptcy）；玩家若 < 破產閾值觸發破產（FT-05 §3.4）|
| 系統降級期間 | 回 `STAFF_SYSTEM_LOCKED`；解雇被擋（避免在系統停擺期 roster 異動）|
| 解雇 OnLeave 職員 | 允許；流程相同；不影響薪水（OnLeave 本就不領薪）|
| 解雇 Reallocating 職員 | 允許；該職員 reallocatingStart 隨 instance 銷毀；不發 OnStaffStateChanged（state 不需改、instance 直接銷毀）|

#### 3.10.6 與保留候選的關係

解雇某 `instanceID` **不影響** `reservedCandidates` 或 `currentCandidates` 中同 `staffID` 的候選卡（候選卡與 StaffInstance 是不同物件）。玩家可解雇 instance、再從候選卡錄用同 staffID 建新 instance（新 instanceID）。

---

### 3.11 薪水管線（Salary Pipeline）

> **⚠️ Phase 2 — Jam 版整節不實作（B 案，2026-04-25 使用者裁示）**
>
> 對齊 FT-05 既有 Phase 2 立場（line 31 / 257 / 285）。Jam 版 FT-08 **不訂閱 OnHourTick**、**不執行 OnSalaryTick / ProcessOfflineSalary**、**永不發布 `OnStaffSalaryDue`**。職員「招募即終身免費」為已知 Jam 範圍妥協；OnStaffSystemBoot Step D 直接 skip。
>
> 下列 §3.11.1 ~ §3.11.7 內容**保留為 Phase 2 規格**，Codex 工項書階段全部跳過實作；Phase 2 啟用時直接套用本節，並同步移除 FT-05 的 Phase 2 註記（FT-05 §3.7 / §6 / §7）。

#### 3.11.1 觸發時點（Phase 2）

- **線上 tick**：每日 UTC 06:00（`SALARY_UTC_HOUR = 6`、§7.2）；FT-08 訂閱 FT-02 TimeSystem 的 `OnHourTick` 事件，於 hour == 6 時觸發 OnSalaryTick
- **離線啟動**：OnStaffSystemBoot Step D 補發跨日累積薪水

#### 3.11.2 perStaffSalary 組裝（與 §4.3.1 對齊）

```
AssemblePerStaffSalary():
    perStaffSalary = {}
    rosterSnapshot = roster.ToArray()                 // §5.4.2 原子 snapshot

    FOR each s in rosterSnapshot:
        IF s.currentState == OnLeave: continue
        salary = StaffTable[s.staffID].salary
        perStaffSalary[s.instanceID] = salary

    return perStaffSalary
```

**snapshot 語意**：發薪當下取 roster 拷貝；後續發布事件 / FT-05 扣款期間若 roster 變動（玩家解雇 / 切態），不影響本次發薪結果。

#### 3.11.3 線上發薪流程

```
OnSalaryTick(dueTimestamp):
    1. 系統閘：IsStaffSystemUnlocked() == false → return（§5.4.3）
    2. 時鐘檢查：now < lastSalaryTimestamp → return（§5.1.1 / Case 5.4 / §4.3.3）
    3. 重複守護：dueTimestamp ≤ lastSalaryTimestamp → return（避免同日重複發薪）
    4. 組裝：perStaffSalary = AssemblePerStaffSalary()
    5. 計算總額：totalAmount = Σ perStaffSalary.Values
    6. 發布事件：
       Publish OnStaffSalaryDue {
           dueTimestamp,
           perStaffSalary,
           totalAmount
       }
       // FT-05 訂閱：AddGoldAllowBankruptcy(-totalAmount)
    7. lastSalaryTimestamp ← dueTimestamp
```

#### 3.11.4 離線補發迴圈

```
ProcessOfflineSalary():
    IF NOT IsStaffSystemUnlocked(): return
    IF lastSalaryTimestamp == 0:
        lastSalaryTimestamp ← now                     // 新存檔首次解鎖；不補發歷史
        return
    IF now < lastSalaryTimestamp: return              // 時鐘倒退（§5.1.1）

    secondsSinceLast = now − lastSalaryTimestamp
    cappedSeconds = min(secondsSinceLast, OFFLINE_MAX_SECONDS)
    missedSalaryCycles = floor(cappedSeconds / 86400)

    FOR i = 1 to missedSalaryCycles:
        dueTimestamp = lastSalaryTimestamp + 86400    // 累進
        OnSalaryTick(dueTimestamp)                    // 內部更新 lastSalaryTimestamp
```

**離線設計決策**：

- 逐次發布 = FT-05 逐次扣款 = 訂閱者可逐次處理（例：P-03 顯示「3 天前發薪 X gold」）
- ProcessOfflineSalary 跨 cycle 之間 roster 不會異動（無玩家輸入）→ perStaffSalary 每次組裝結果穩定
- `dueTimestamp` 為**理論發薪時點**（lastSalaryTimestamp + 86400 累進），不是 `now`；下游可依此顯示歷史結算明細

#### 3.11.5 lastSalaryTimestamp 持久化

| 階段 | 行為 |
|---|---|
| 新存檔首次解鎖 | `lastSalaryTimestamp ← now`（OnStaffSystemUnlocked callback）|
| 線上每次 OnSalaryTick | `lastSalaryTimestamp ← dueTimestamp`（步驟 7）|
| 離線補發每 cycle | 同上（OnSalaryTick 內部）|
| 系統降級期間 | 不更新（OnSalaryTick 早退）|
| 重新解鎖（L0 → L1） | `lastSalaryTimestamp ← now`（防補發降級期間薪水，§3.11.7）|
| 時鐘倒退 | 不更新 |

#### 3.11.6 OnLeave 職員薪水跳過

§4.3.1 / §5.4.1 已定義：OnLeave 職員不入 perStaffSalary。**特殊情境**：

- 發薪當 frame 玩家 `TryGoOnLeave(instanceID)` → 視 snapshot 時點：snapshot 在 TryGoOnLeave 之前 → 計薪；之後 → 不計薪
- **設計上不保證**這微秒級的順序；玩家不應依賴此行為（§5.4.2 已述用 atomic snapshot 防 dict 污染）

#### 3.11.7 系統降級時的薪水管線

```
OnStaffSystemUnlocked():    // 由 §3.12.5 訂閱 OnBuildingUpgraded 觸發
    IF lastSalaryTimestamp != 0:
        lastSalaryTimestamp ← now                     // 防補發降級期間薪水
```

降級期間：

- OnSalaryTick 不執行（系統閘擋下）
- ProcessOfflineSalary 不執行
- lastSalaryTimestamp 凍結
- 重新解鎖時 reset 為 now → 不補發降級期間每天的薪水（簡化決策、不公平給玩家但避免破產風暴）

#### 3.11.8 事件 payload 完整契約

```
OnStaffSalaryDue payload {
    dueTimestamp        : long                  // 該次發薪對應的 UTC 時間戳（理論時點）
    perStaffSalary      : Dict<int, int>        // key = instanceID, value = 該職員薪水
    totalAmount         : int                   // Σ perStaffSalary.Values
}
```

訂閱者責任：

- **FT-05**（硬契約）：收到事件 → `AddGoldAllowBankruptcy(-totalAmount)`；不關心 perStaffSalary key 細節
- **P-02**（可選）：UI 顯示發薪明細
- **P-03**（可選）：toast「支付了 X gold 薪水給 N 位職員」
- 任一訂閱者 throw → 不影響其他訂閱者（EventBus 容錯，commits 25efd11 / 2c10d17 已強化）

---

### 3.12 系統降級行為總表（Degradation Contract）

當 `FT07.IsStaffSystemUnlocked() == false` 時，FT-08 進入降級模式。本表彙整所有 API / 事件 / 內部流程的降級行為，作為 §3.6.5 / §5.4.3 / AC-1 / AC-3 / AC-29 / AC-45 的單點對照表。

#### 3.12.1 對外 API 行為總表

| API | 降級時行為 | AC 對應 |
|---|---|---|
| `IsStaffSystemUnlocked()` | 直接 return false（轉發 FT-07）| AC-1 |
| `IsSuccessRatePreviewEnabled()` | return false（早退、不遍歷 roster）| AC-29 |
| `GetStaffWillingnessBonus()` | return 0f（早退）| AC-29 |
| `GetAccountantCommissionBonus()` | return 0f（早退）| AC-29 |
| `GetAccountantPenaltyBonus()` | return 0f（早退）| AC-29 |
| `GetRecruitRefreshReductionSec()` | return 0（早退）| AC-29 |
| `TryManualRefresh()` | return STAFF_SYSTEM_LOCKED；不扣費、不變動 candidates | AC-1 |
| `TrySwitchPool(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryRecruit(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryRejectCandidate(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryReserveCandidate(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryReleaseReserve(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryAssignStaff(int, int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryUnassignStaff(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryGoOnLeave(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryReturnFromLeave(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |
| `TryFireStaff(int)` | return STAFF_SYSTEM_LOCKED | AC-1 |

#### 3.12.2 事件發布總表

| 事件 | 降級時 | AC 對應 |
|---|---|---|
| `OnStaffHired` | 不發布（無 TryRecruit 入口可成功）| AC-45 |
| `OnStaffFired` | 不發布（無 TryFireStaff 入口可成功）| AC-45 |
| `OnStaffAssigned` | 不發布 | AC-45 |
| `OnStaffStateChanged` | 不發布 | AC-45 |
| `OnStaffSalaryDue` | 不發布（OnSalaryTick 早退）；**Jam 範疇下因 §3.11 為 Phase 2，無視系統解鎖狀態，永不發布** | AC-45 / AC-29（Jam 版自動通過，因事件源頭未實作）|

#### 3.12.3 內部流程降級

| 流程 | 降級時 |
|---|---|
| OnStaffSystemBoot Step C（補刷判定，即 Auto / Offline 路徑，§3.3.1）| 跳過；**Jam 版本就無線上 auto refresh tick**——所有 auto 補刷僅於 Boot 一次性執行，降級時整段不執行 |
| OnStaffSystemBoot Step D（薪水補發）| 跳過；**Jam 範疇下整段 §3.11 為 Phase 2，無條件跳過，無視系統解鎖狀態** |
| CheckReallocatingAutoLeave | 跳過（reallocatingStartTimestamp 凍結、解鎖後沿用）|
| OnSalaryTick（OnHourTick 訂閱觸發）| 早退；**Jam 範疇下未訂閱 OnHourTick（§3.11 Phase 2），不存在觸發路徑** |
| 過期保留掃描 | 跳過（reservedCandidates 凍結；解鎖後 OnStaffSystemBoot Step B 處理）|

#### 3.12.4 持久化資料保留

降級期間，下列資料**全部保留**（§5.4.3 已述）：

- `StaffPlayerState`：pityCounter、nextInstanceID、lastAutoRefreshTimestamp、currentPoolID、currentCandidates、reservedCandidates
- `StaffInstance[]`：所有職員的 state / assignedBuildingID / cooldown / reallocatingStart / hiredTimestamp
- `lastSalaryTimestamp`：FT-08 內部狀態（不在 StaffPlayerState 中、與發薪管線分離）；解鎖時 reset 為 now（§3.11.7）

#### 3.12.5 降級進入 / 離開的事件 callback

FT-08 不主動訂閱 FT-07 `OnBuildingUpgraded` / `OnBuildingDestroyed` 用於 API 降級——所有 API 入口處 query `IsStaffSystemUnlocked()`（惰性檢測）。但 §3.11.7 「解鎖時 reset lastSalaryTimestamp」需要訂閱：

```csharp
// FT-08 啟動時訂閱：
EventBus.Subscribe<OnBuildingUpgraded>(evt => {
    IF evt.buildingID == 6 AND evt.fromLevel == 0 AND evt.toLevel == 1:
        OnStaffSystemUnlocked();   // §3.11.7：lastSalaryTimestamp ← now
});
```

降級進入（L1+ → L0）目前 Jam 版**不訂閱**——降級為 lazy 檢測；玩家若在線降級會看到下次 query 即返回 0 / locked，不需要主動 callback。

---

### 3.13 事件契約（Event Contracts）

§6.3 已概覽事件清單；本節展開每個事件的精確 payload、發布時序、訂閱者責任、降級行為。

#### 3.13.1 事件清單與 payload

```
OnStaffHired {
    int  instanceID
    int  staffID
    long hiredTimestamp
}

OnStaffFired {
    int  instanceID
    int  staffID
    long firedTimestamp
    int  severancePaid
}

OnStaffAssigned {
    int  instanceID
    int  oldBuildingID    // 0 = 從未指派
    int  newBuildingID    // 0 = TryUnassignStaff
}

OnStaffStateChanged {
    int        instanceID
    StaffState oldState
    StaffState newState
}

OnStaffSalaryDue {                  // ⚠️ Phase 2，Jam 版不發布（§3.11 整節為 Phase 2）
    long           dueTimestamp
    Dict<int,int>  perStaffSalary    // key = instanceID
    int            totalAmount
}
```

#### 3.13.2 發布時序保證

對於同一個動作觸發多個事件的情境，FT-08 保證以下順序：

| 動作 | 順序 |
|---|---|
| TryRecruit 成功 | 1. roster.Add(staff) → 2. Publish OnStaffHired |
| TryFireStaff 成功 | 1. AddGoldAllowBankruptcy(-severancePay) → 2. Publish OnStaffFired → 3. roster.Remove |
| TryAssignStaff（state 改變：Reallocating → Working，oldBuildingID = 0）| 1. assignedBuildingID 改 → 2. state 改 → 3. Publish OnStaffAssigned → 4. Publish OnStaffStateChanged |
| TryAssignStaff（state 不變、僅改 buildingID：切建築 Working → Working，A2 案）| 1. assignedBuildingID 改 + cooldownEnd 重設 → 2. Publish OnStaffAssigned |
| TryAssignStaff（idempotent，buildingID 同舊）| 無事件 |
| TryUnassignStaff | 1. assignedBuildingID = 0 → 2. state = Reallocating → 3. Publish OnStaffAssigned → 4. Publish OnStaffStateChanged |
| TryGoOnLeave | 1. state = OnLeave、assignedBuildingID = 0 → 2. Publish OnStaffStateChanged |
| TryReturnFromLeave | 1. state = Reallocating、reallocatingStart = now → 2. Publish OnStaffStateChanged |
| Reallocating > 12h 自動轉假 | 1. state = OnLeave、assignedBuildingID = 0 → 2. Publish OnStaffStateChanged |
| OnSalaryTick | **Phase 2**：1. perStaffSalary 組裝（atomic snapshot）→ 2. Publish OnStaffSalaryDue → 3. lastSalaryTimestamp 更新；Jam 版整段不執行 |

#### 3.13.3 訂閱者責任表

| 事件 | 訂閱者 | 處理動作 |
|---|---|---|
| OnStaffHired | P-02 名冊 UI | 加入名冊顯示 |
| OnStaffHired | P-03 通知 | toast「招募了 [name]」 |
| OnStaffHired | （Post-Jam）圖鑑 | 解鎖該 staffID 的圖鑑條目 |
| OnStaffFired | P-02 | 從名冊移除 |
| OnStaffFired | P-03 | toast「解雇了 [name]，支付 X gold 資遣費」 |
| OnStaffAssigned | P-02 | 名冊 UI 更新指派狀態圖示 |
| OnStaffAssigned | FT-05（保險櫃 slot 變動感知）| 不需主動處理（§3.6 即時遍歷無快取）；可選 log 記錄 |
| OnStaffStateChanged | P-02 | 顯示 Working / Reallocating / OnLeave 狀態圖示 |
| OnStaffStateChanged | P-03（轉假）| toast「[name] 進入休假」（OnLeave 觸發時）|
| OnStaffSalaryDue | FT-05（**硬契約 / Phase 2**）| `AddGoldAllowBankruptcy(-totalAmount)`；Jam 版 FT-08 不發布、FT-05 訂閱端為 no-op |
| OnStaffSalaryDue | P-02（可選 / Phase 2）| 發薪明細列表 |
| OnStaffSalaryDue | P-03（可選 / Phase 2）| toast「支付薪水 X gold」 |

#### 3.13.4 訂閱者錯誤隔離

EventBus 已強化（commits 25efd11 / 2c10d17）：

- 任一訂閱者 throw → 隔離、其他訂閱者繼續執行
- 訂閱者於 callback 內 unsubscribe 自身：合法（subscribe 容器使用 idempotency guard）
- 訂閱者於 callback 內 publish 同一事件：應避免（可能造成 reentrancy）；FT-08 自身不在訂閱中重發任何事件

#### 3.13.5 降級時不發布

§3.12.2 已詳列；本節僅補一條工程提示：

**降級期間訂閱者狀態**：訂閱者保持訂閱（不取消）；FT-08 不對訂閱者發送「系統解鎖」/「系統降級」事件。下游若需感知降級狀態，自行 poll `IsStaffSystemUnlocked()` 或訂閱 FT-07 `OnBuildingUpgraded(buildingID=6)` 自行判斷。

#### 3.13.6 Payload 永續性

事件 payload 為 **immutable**（C# `readonly struct` 或 `class` with init-only properties）；訂閱者不應修改 payload。FT-08 publish 後不保留 payload reference（GC 可回收）。

#### 3.13.7 事件 vs API 查詢的選擇

| 場景 | 推薦使用 |
|---|---|
| 一次性反應（職員入職時 toast）| 訂閱事件 |
| 即時值查詢（UI 顯示當前 willingness 加成）| 查詢 API（GetStaffWillingnessBonus）|
| 持續監看狀態（每 frame 看名冊）| 查詢 API + 內部 dirty flag |
| 累積統計（總共解雇了幾位）| 訂閱事件 + 自行累計 |

---

## 4. 公式（Formulas）

> 本節將 §1 / §3 已口述之設計規則正式化為公式；所有公式之 runtime 引用見 §3 詳述（面試流程 §3.3 待寫、效果聚合 §3.6、薪水 §3.11 待寫）。符號慣例：`floor(·)` = 向下取整；`clamp(x, lo, hi) = min(max(x, lo), hi)`；`Σ` = 對 roster 或 effect list 求和；`[guildLevel]` 下標 = 以公會等級作 key 的 table 查詢。
>
> **降級前提**：以下所有公式於 `FT07.IsStaffSystemUnlocked() == false` 時不執行——面試 API 回 `STAFF_SYSTEM_LOCKED`、加成 API 回 `0` / `false`、薪水不發布（見 §3.6.5 / §3.12）。

### 4.1 面試系統公式（Interview System Formulas）

#### 4.1.1 面試欄張數 N（interview slot count）

```
N(guildLevel) = StaffRefreshCostTable[guildLevel].interviewSlotCount
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `guildLevel` | 玩家當前公會等級 | `1 ≤ guildLevel ≤ 5`（Jam 版）|
| `N` | 本次 refresh 呈現的候選張數 | `1 ≤ N ≤ 5`（Jam 版建議上限 5，避免 UI 擁擠）|

**範例**：`guildLevel = 3`、`StaffRefreshCostTable[3].interviewSlotCount = 4` → `N = 4`。該次 refresh 填滿 `currentCandidates[0..3]`。

---

#### 4.1.2 手動刷新費（manual refresh cost）

```
refreshCost(guildLevel) = StaffRefreshCostTable[guildLevel].cost
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `refreshCost` | 玩家主動付費刷新一次的金幣成本 | `refreshCost ≥ 0`（Jam 版建議 100 ~ 500 gold）|

- 表查詢，不依賴 pool（刷新費與當前選中池無關）
- 付款流程：扣款在 `TryManualRefresh()` 內部依序檢查 `GetGold() ≥ refreshCost` → 扣款 → 觸發 roll；不足回 `GOLD_INSUFFICIENT`

**範例**：`guildLevel = 2`、`StaffRefreshCostTable[2].cost = 150` → 玩家點「刷新」時扣 150 gold，roll 出新的 N 張候選。

---

#### 4.1.3 自動刷新間隔（auto-refresh interval）

```
autoRefreshIntervalSec(L) = BuildingTable[6].upgradeData[L].effectValue
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `L` | 當前職員休息室等級；`L = FT07.GetBuildingLevel(6)` | `1 ≤ L ≤ 5`（`L = 0` 時系統鎖，不走此公式）|
| `autoRefreshIntervalSec` | 自動刷新的秒數間隔 | `21600 ≤ interval ≤ 86400`（FT-07 §7.1 鎖定階梯）|

**FT-07 階梯**（引用自 FT-07 §7.1）：L1=86400（24h）/ L2=64800（18h）/ L3=43200（12h）/ L4=28800（8h）/ L5=21600（6h）。

**範例**：玩家升職員休息室至 L3 → `autoRefreshIntervalSec = 43200`（12 小時自動刷新一次）。

---

#### 4.1.4 離線補刷次數（offline refresh refill count）

```
missedIntervals  = floor((now - lastAutoRefreshTimestamp) / autoRefreshIntervalSec(L))
refillCount      = clamp(missedIntervals, 0, 1)
```

補刷後更新：

```
IF refillCount == 1:
    lastAutoRefreshTimestamp ← now     // 重置為 now，下個 interval 從 now 起算
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `now` | 登入時的 UTC 時間戳 | 大於 `lastAutoRefreshTimestamp`（時鐘異常見 §5）|
| `lastAutoRefreshTimestamp` | `StaffPlayerState.lastAutoRefreshTimestamp` | 全域單一計時器（§3.2.3）|
| `missedIntervals` | 離線期間理論應觸發的次數 | `≥ 0`；可能很大（例：離線 7 天 L1 = 7）|
| `refillCount` | 實際補刷次數 | `clamp → {0, 1}` |

**設計決策**：clamp 上限 1 而非累積補刷——防止長期離線回來看到爆炸性歷史候選堆積、UI 展示混亂；同時保底 counter 也只 +N 一次（不被離線放大）。補刷後 `lastAutoRefreshTimestamp ← now`（非 `+= intervalSec`）以讓玩家重置 cadence。

**範例**：
- `now = 1714000000`、`lastAutoRefreshTimestamp = 1713900000`、`L = 2`（`intervalSec = 64800`） → `missedIntervals = floor(100000 / 64800) = 1` → `refillCount = 1` → 補 1 次 refresh、更新 `lastAutoRefreshTimestamp = 1714000000`。
- 離線 7 天後登入（`now - last = 604800`）、`L = 5`（`intervalSec = 21600`） → `missedIntervals = 28` → `refillCount = 1`（clamp）→ 玩家僅見一次新 refresh。

---

#### 4.1.5 稀有度動態歸一化（rarity dynamic normalization）

**基底機率**（來自 `StaffRarityProbTable`）：

```
baseProb[1] = 0.40, baseProb[2] = 0.25, baseProb[3] = 0.15, baseProb[4] = 0.15, baseProb[5] = 0.05
Σ baseProb = 1.00
```

**動態歸一化**（池中該層無職員時重新分配）：

```
emptyTiers       = { r | poolEligibleByRarity(poolID, r) == ∅ }
normalizationDen = 1 − Σ_{r ∈ emptyTiers} baseProb[r]
effectiveProb[r] = baseProb[r] / normalizationDen     (r ∉ emptyTiers)
effectiveProb[r] = 0                                  (r ∈ emptyTiers)
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `poolEligibleByRarity(poolID, r)` | 池內稀有度 `r` 且 `staffWeights[i] > 0` 的職員子集 | 子集 |
| `emptyTiers` | 該池空的稀有度集合 | `⊆ {1, 2, 3, 4, 5}` |
| `normalizationDen` | 歸一化分母 | `(0, 1]`（至少一層非空時 > 0）|
| `effectiveProb[r]` | 實際 roll 機率 | `[0, 1]`；`Σ effectiveProb = 1` |

**設計約束**：`|emptyTiers| < 5`——池中**至少一層非空**（DataManager 驗證；全空池拋 `StaffGachaPoolTableValidationException`）。

**範例**（池 B 中高等，假設 1★ / 2★ 層空、含 3★~5★）：
- `emptyTiers = {1, 2}`
- `normalizationDen = 1 − (0.40 + 0.25) = 0.35`
- `effectiveProb[3] = 0.15 / 0.35 ≈ 0.4286`
- `effectiveProb[4] = 0.15 / 0.35 ≈ 0.4286`
- `effectiveProb[5] = 0.05 / 0.35 ≈ 0.1429`
- 和 = 1.000 ✓

---

#### 4.1.6 稀有度層內職員權重（per-staff weight within rarity tier）

稀有度 roll 確定 `rolledRarity = r*` 後，於該層內依權重 roll 具體 `staffID`：

```
tierStaff[r*]    = { (sID, w) | sID ∈ eligibleStaffIDs, StaffTable[sID].rarity == r*, w = staffWeights[indexOf(sID)] }
tierWeightSum[r*] = Σ_{(sID, w) ∈ tierStaff[r*]} w

P(staffID = sID | rolledRarity = r*) = w / tierWeightSum[r*]       (for (sID, w) ∈ tierStaff[r*])
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `r*` | 上一步 roll 出的稀有度 | `{1, 2, 3, 4, 5} \ emptyTiers` |
| `w` | `StaffGachaPoolTable.staffWeights` 對應值 | 整數 `≥ 0`；`0 = 保留不抽出` |
| `tierWeightSum[r*]` | 該層總權重 | `> 0`（動態歸一化已保證層非空）|

**範例**（池 A 的 3★ 層：`eligibleStaffIDs = [101, 201, 301]`、`staffWeights = [10, 5, 0]`）：
- `tierStaff[3] = { (101, 10), (201, 5), (301, 0) }`；301 權重 0 → 不抽出
- `tierWeightSum[3] = 15`
- `P(101 | rolledRarity = 3) = 10 / 15 ≈ 0.667`
- `P(201 | rolledRarity = 3) = 5 / 15 ≈ 0.333`
- `P(301 | rolledRarity = 3) = 0`

---

#### 4.1.7 保底 counter 增量（pity counter increment）

每次 refresh（自動 / 手動 / 離線補刷）：

```
pityCounter ← pityCounter + N       // N = 該次展示張數（§4.1.1）
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `pityCounter` | `StaffPlayerState.pityCounter` 跨池累積 | `≥ 0`；觸發時 reset 為 `0`（§4.1.8）|
| `N` | 該次 refresh 張數 | 見 §4.1.1 |

**設計註記**：切池不 reset、公會升級不 reset、解除保留 / 解雇不影響。每張候選 +1（非每次 refresh +1）——避免 N=5 時保底速度與 N=3 時差太多導致體感不一致。

**範例**：玩家在 `guildLevel = 2`（N = 3）連刷 3 次 → `pityCounter: 0 → 3 → 6 → 9`；第 4 次刷新使 `pityCounter = 12`，觸發保底（§4.1.8）。

---

#### 4.1.8 保底觸發條件（pity trigger condition）

```
shouldForce5Star = (pityCounter ≥ 10) AND (poolEligibleByRarity(currentPoolID, 5) ≠ ∅)
```

**觸發後行為**（僅影響該次 refresh 內**第一張**命中保底的 slot）：

```
IF shouldForce5Star:
    該 slot 的 rolledRarity ← 5（跳過 §4.1.5 動態歸一化）
    後續依 §4.1.6 在 5★ 層內 roll 具體 staffID
    pityCounter ← 0                                     // reset
ELIF (pityCounter ≥ 10) AND (poolEligibleByRarity(currentPoolID, 5) == ∅):
    // 池中 5★ 空：本次不強制觸發，counter 繼續累積、走動態歸一化
    pityCounter 不變
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `pityCounter` | 累積計數器 | 通常 `[0, ~15]`；5★ 空池情境可更高 |
| `currentPoolID` | 玩家當前池 | `StaffPlayerState.currentPoolID` |

**設計註記**：保底**不保證連抽到**——僅該次 refresh 的第一張保證 5★，其他 slot 仍走正常 roll（可能再出 5★、也可能出 1★）。池中 5★ 空時 counter 不 reset 也不觸發，等玩家切到含 5★ 的池（或升公會等級開放 5★）才兌現——避免「5★ 空池刷新也算保底消耗」對玩家不公平。

**範例**：
- `pityCounter = 10`、`currentPoolID = 1`（A 池含 5★） → 下次 refresh 第一張強制 5★、`pityCounter ← 0`
- `pityCounter = 10`、`currentPoolID = 2`（B 池本 case 5★ 空） → 不觸發、`pityCounter` 下次 refresh 繼續 `+= N`

---

#### 4.1.9 保留上限（max reserve）

```
maxReserve = max(1, interviewSlotCount - 1)
           = max(1, N - 1)
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `N` | 當前面試欄張數（§4.1.1） | `1 ≤ N ≤ 5` |
| `maxReserve` | 跨池保留區 `reservedCandidates` 的容量上限 | `1 ≤ maxReserve ≤ 4` |

**設計約束**：特例 `N = 1 → maxReserve = 1`（避免玩家無法保留任何東西）；`N ≥ 2 → maxReserve = N - 1`（強制至少留一張「現在決定」的壓力）。

**溢出處理**：嘗試保留第 `maxReserve + 1` 張時，`TryReserveCandidate()` 回 `RESERVE_FULL`；玩家必須先從保留區移除一張（錄用 / 解雇對應 `StaffInstance` / 手動取消）。

**範例**：`guildLevel = 3`、`N = 4` → `maxReserve = 3`；玩家可同時保留 3 張候選（跨 A / B 池），第 4 張嘗試保留時失敗。

---

### 4.2 效果聚合上限（Effect Aggregation Caps）

§3.6.2 演算法的最後一步皆為 cap 截斷；以下是 §3.6.3 表格的公式形式化。

```
GetStaffWillingnessBonus()          = min(Σ_i WillingnessEffectValues[i],          EFFECT_MAX_WILLINGNESS_BONUS)
GetAccountantCommissionBonus()      = min(Σ_i AccountantCommissionEffectValues[i], EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS)
GetAccountantPenaltyBonus()         = max(Σ_i AccountantPenaltyEffectValues[i],    EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS)     // 負值、max 為「下限截斷」
GetRecruitRefreshReductionSec()     = min(Σ_i RecruitRefreshEffectValues[i],       EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC)
```

| 常數 | 值 | 型別 | 意義 |
|---|---|---|---|
| `EFFECT_MAX_WILLINGNESS_BONUS` | `+0.20` | float | Willingness 總加成上限（進 FT-03 公式前的 cap）|
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | `+0.10` | float | 會計傭金加總上限（進 FT-05 公式前的 cap）|
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | `-0.10` | float | 會計賠償加總**下限**（絕對值 ≤ 0.10；負值的 `max` = 向零方向截斷）|
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | `14400`（4h）| int | 招募刷新減量上限（避免刷新近乎即時失去等待感）|

**變數範圍**：

| 變數 | 定義 | 範圍 |
|---|---|---|
| `Σ_i WillingnessEffectValues[i]` | 全 roster（含 Reallocating，排除 OnLeave）帶 `Willingness` effect 的 `effectValues[i]` 加總 | `[0, +∞)`；實務 Jam 版 roster ≤ 99 且每職員 ≤ 0.05 → 實際上界遠低於 cap |
| `Σ_i AccountantCommissionEffectValues[i]` | 同上、`AccountantCommission` | 同上 |
| `Σ_i AccountantPenaltyEffectValues[i]` | **僅** `currentState == Working AND assignedBuildingID == 5` 的職員 | `(−∞, 0]`；slot capacity = 1 → 實務 `{−0.02} or {0}` |
| `Σ_i RecruitRefreshEffectValues[i]` | **僅** `currentState == Working AND assignedBuildingID == 4` 的職員 | `[0, +∞)`；slot capacity 受 FT-07 §7.1 `buildingID=4` slotCount 限制 |

**範例**：3 位委託官職員在職（Willingness `+0.05` 各 → Σ = 0.15）→ `GetStaffWillingnessBonus() = min(0.15, 0.20) = 0.15`。第 4 位加入（Σ = 0.20）仍為 `0.20`；第 5 位加入（Σ = 0.25）被 cap 為 `0.20`。

---

### 4.3 薪水公式（Salary Formulas）

> **⚠️ Phase 2 — Jam 版整段公式不執行（B 案，2026-04-25 使用者裁示）**
>
> 對齊 §3.11 / FT-05 既有 Phase 2 立場。下列公式（每日薪水 dict 組裝、總額、離線補發）保留為 Phase 2 規格、Jam 版 Codex 不需實作；其引用之 `lastSalaryTimestamp`、`SALARY_UTC_HOUR`、`OFFLINE_MAX_SECONDS` 皆無 runtime 觸發路徑。Phase 2 啟用時直接套用本節，並同步移除 FT-05 §3.7 的 Phase 2 註記。

#### 4.3.1 每日薪水 dict 組裝（Phase 2）

每日 UTC 06:00 結算時，組裝 `perStaffSalary: Dict<int, int>`（key = `instanceID`、value = 薪水）：

```
perStaffSalary = { s.instanceID → StaffTable[s.staffID].salary
                   | s ∈ activeRoster, s.currentState ≠ OnLeave }
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `activeRoster` | FT-08 名冊中所有 `StaffInstance` | `≤ 99`（Jam 版名冊上限）|
| `s.currentState` | 三態 | `{ Working, Reallocating, OnLeave }` |
| `StaffTable[s.staffID].salary` | 模板每日薪水 | `≥ 0`（Jam 版建議 0 ~ 100 gold/day）|

**休假者排除**：`OnLeave` 不計入 `perStaffSalary`（FT-08 §1.C 已定義）。

#### 4.3.2 薪水總額（total amount）

事件 payload 附帶總額供 FT-05 快速檢查；欄位名稱對齊 FT-05 既有契約（§6.3）：

```
totalAmount = Σ_{k ∈ perStaffSalary.Keys} perStaffSalary[k]
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `totalAmount` | 本次發薪總扣款（事件 payload 欄位）| `≥ 0`；實務 Jam 版 `[0, ~3000]` gold/day |

#### 4.3.3 離線補發次數

```
missedSalaryCycles = floor((now − lastSalaryTimestamp) / 86400)
                     clamped by SystemConstants.OFFLINE_MAX_SECONDS / 86400
```

每一個補發週期獨立組裝 `perStaffSalary` 一次、發布事件一次（逐次觸發 FT-05 扣款；具體離線迴圈邏輯於 §3.11 定義）。

| 變數 | 定義 | 範圍 |
|---|---|---|
| `lastSalaryTimestamp` | 最近一次成功發薪的 UTC 時間戳 | `≤ now` |
| `missedSalaryCycles` | 離線期間應補發的發薪週期數 | `0 ≤ n ≤ OFFLINE_MAX_SECONDS/86400` |
| `86400` | 1 天 = 86400 秒 | 常數 |

**範例**（假設 `OFFLINE_MAX_SECONDS = 604800`，即 7 天）：
- 離線 3 天登入 → `missedSalaryCycles = 3` → 發布 `OnStaffSalaryDue` 三次，每次 payload 以當時 roster 狀態組裝
- 離線 30 天登入 → `missedSalaryCycles = clamp(30, 0, 7) = 7` → 僅補 7 次（FT-05 `OFFLINE_MAX_SECONDS` 既有契約）

**時區異常**：若 `now < lastSalaryTimestamp`（系統時鐘被調回），本次啟動**跳過發薪**（不扣款、不發事件），見 §5 邊緣案例。

---

## 5. 邊緣案例（Edge Cases）

> 本節覆蓋 §4 公式未直接處理的異常 / 邊界情境。每個 case 明確指出「系統做什麼」而非「妥善處理」。

### 5.1 時間異常（Time Anomalies）

**Case 5.1.1 — 系統時鐘倒退（`now < lastAutoRefreshTimestamp`）**
- **觸發**：玩家手動調整電腦時鐘、時區切換、跨日光節約時間邊界導致 `now` 回退
- **自動刷新行為**：`missedIntervals = floor(負數 / interval)` → 負值 → `clamp(…, 0, 1) = 0` → 不補刷、`lastAutoRefreshTimestamp` 不變動
- **薪水行為**：本次啟動**跳過發薪**（§4.3.3 契約），`lastSalaryTimestamp` 不更新、不發 `OnStaffSalaryDue`
- **效果聚合**：不受影響（不依賴時間戳）
- **玩家可見**：UI 顯示「等待下次自動刷新」直到 `now` 重新超過 `last`

**Case 5.1.2 — 離線超過 `OFFLINE_MAX_SECONDS`**
- **觸發**：玩家離線 > 7 天（假設 `OFFLINE_MAX_SECONDS = 604800`）
- **自動刷新**：`refillCount = clamp(floor(…), 0, 1) = 1`，僅補一次（§4.1.4），不累積 7 次
- **薪水補發**：`missedSalaryCycles = clamp(floor((now - last) / 86400), 0, 7)`，最多補 7 次發薪（§4.3.3）
- **保底**：僅累積一次 `+N`（跟隨 refresh 次數）；不因「理論應該觸發 7 次」被放大

**Case 5.1.3 — 新遊戲初始化（`lastAutoRefreshTimestamp == 0`）**
- **觸發**：新存檔首次解鎖職員系統（FT-07 職員休息室剛建到 L1）
- **初始化規則**：`IsStaffSystemUnlocked()` **首次**回 `true` 時，由 FT-08 啟動流程設 `lastAutoRefreshTimestamp ← now`；避免 `floor((now - 0) / interval)` 被巨大差值觸發虛假補刷
- **等價情境**：存檔載入時若 `lastAutoRefreshTimestamp == 0`（舊存檔遷移 / 異常）→ 同樣 reset 為 `now`

---

### 5.2 面試流程（Interview Flow）

**Case 5.2.1 — 手動刷新金幣不足**
- **觸發**：`GetGold() < refreshCost(guildLevel)`
- **行為**：`TryManualRefresh()` 回 `GOLD_INSUFFICIENT`；`currentCandidates` / `pityCounter` / `lastAutoRefreshTimestamp` **皆不變動**；不扣款、不發任何事件

**Case 5.2.2 — 保留區已滿**
- **觸發**：`reservedCandidates.Count == maxReserve`，玩家嘗試保留第 `maxReserve + 1` 張
- **行為**：`TryReserveCandidate()` 回 `RESERVE_FULL`；該候選 `isReserved` 不變（仍在 `currentCandidates`）；玩家需先錄用 / 不錄用 / 手動取消保留區某一張以騰出空間

**Case 5.2.3 — 候選已被 `reserveConsumedFlag = true`，玩家點保留**
- **觸發**：候選曾被保留後解除（時點 3），玩家看到卡回原 slot 再點保留
- **行為**：P-02 Main UI 依 `reserveConsumedFlag` disable 按鈕；若 UI 層遺漏，FT-08 `TryReserveCandidate()` 拒絕回 `RESERVE_CONSUMED`——**雙層防護**

**Case 5.2.4 — 保留時限到、玩家離線期間**
- **觸發**：玩家保留候選 A、離線超過 `reserveTimeLimitSec`、未再登入
- **行為**：下次登入時，FT-08 啟動流程掃描 `reservedCandidates`，凡 `now − reservedTimestamp ≥ reserveTimeLimitSec(poolID)` 的卡一律解除：`isReserved = false`、`reservedTimestamp = 0`、`reserveConsumedFlag = true`、從 `reservedCandidates` 移除
- **卡的去向**：嘗試放回 `currentCandidates[slotIndex]`（該 slot 在保留期間被鎖定為空，§1 line 57「下次刷新跳過該 slot」）；若玩家離線期間原池又刷了新 batch（離線自動補刷 1 次），則原 slot 已被新 roll 覆蓋——此時保留卡**丟失**（不再回到任何 list，`reservedCandidates` 中直接移除）
- **實作註記**：解除保留的順序 = 先掃保留區、後跑離線補刷；避免「卡剛解除就被新 roll 覆蓋」的競態

**Case 5.2.5 — 切池導致未保留候選丟失**
- **觸發**：玩家從 A 池切到 B 池
- **行為**：`currentCandidates`（A 池殘留）**整個覆蓋為 B 池新 roll 結果**；`reservedCandidates` 跨池存活不受影響（依 `poolID` 識別歸屬）；已錄用職員（`StaffInstance`）與池無關、不受影響
- **觸發時機**：切池動作本身 = 立即觸發一次 B 池 refresh（以填滿 `currentCandidates`）；計入保底 counter（`pityCounter += N`）、不扣刷新費（切池免費）
- **設計副作用**：保留區是跨池保留的**唯一**途徑；玩家若不想丟 A 池的 3★ 好卡，切池前必須先保留

---

### 5.3 保底（Pity）邊界

**Case 5.3.1 — 保底達成但池中 5★ 空**
- **觸發**：`pityCounter ≥ 10`、玩家當前 `currentPoolID` 對應池 `poolEligibleByRarity(poolID, 5) == ∅`
- **行為**：**不觸發強制 5★**；本次 refresh 走正常動態歸一化（§4.1.5，5★ ∈ `emptyTiers`）；`pityCounter` **不 reset、繼續累積**（每張 +N）；下次 refresh 若池中 5★ 仍空同樣不觸發
- **兌現時機**：玩家切到含 5★ 的池、或升公會等級開放新 5★、或 DataManager 更新 `staffWeights` 使 5★ 層非空 → 下次 refresh 第一張強制 5★、counter reset

**Case 5.3.2 — 保底觸發當次多張同時命中**
- **觸發**：`pityCounter = 10`、`N = 5`
- **行為**：僅該次 refresh 的**第一張（slotIndex = 0）**強制 5★；其餘 4 張走正常 roll（可能再出 5★、也可能全 1★）；`pityCounter ← 0`（不分張數；下次 refresh 從 `+N` 重新起算）

**Case 5.3.3 — 保底 counter 累積期間職員休息室降級至 L0**
- **觸發**：玩家拆除職員休息室（`buildingID=6` → `L0`）導致 `IsStaffSystemUnlocked() == false`
- **行為**：`pityCounter` 持久化、不 reset；系統降級期間無 refresh 發生；重新升回 L1 時，counter 原值恢復有效——玩家不會因為臨時降級而失去保底進度

---

### 5.4 薪水（Salary）邊界

> **⚠️ Phase 2 — Jam 版整段邊界不會觸發（B 案，2026-04-25 使用者裁示）**
>
> 對齊 §3.11 / §4.3 / FT-05 Phase 2 立場。Jam 版 §3.11 整段不實作，下列邊界不存在運行時情境，但保留為 Phase 2 啟用時的契約。

**Case 5.4.1 — OnLeave 職員在發薪時點**
- **觸發**：職員 A 處於 `OnLeave`、發薪 tick 觸發
- **行為**：A 不入 `perStaffSalary` dict、不扣款（§4.3.1）；事件 payload 不含 A；若 A 在發薪**後**立即切為 `Working`，下次發薪週期才計薪

**Case 5.4.2 — 發薪當 frame 解雇 / 切態**
- **觸發**：發薪流程正在組裝 `perStaffSalary`、同 frame 另一條路徑嘗試解雇某職員
- **行為**：FT-08 以**原子 roster snapshot** 為準——組裝 `perStaffSalary` 時對 roster 拷貝一份快照；發布事件後再處理解雇；避免 dict 內含已解雇 `instanceID`、或漏發在職職員薪水

**Case 5.4.3 — 系統降級（`IsStaffSystemUnlocked() == false`）時 roster 非空**
- **觸發**：特殊存檔（例：debug 操作、FT-10 遷移）或玩家升休息室 → 錄取職員 → 後續拆除休息室
- **行為**：
  - 加成 API 全部回 `0` / `false`（§3.6.5）
  - `OnStaffSalaryDue` **不發布**、`lastSalaryTimestamp` 不更新（不扣薪、不補發）
  - `StaffInstance` 資料仍保留在名冊（解雇仍可用）
  - 重新建回休息室 → 系統恢復時**不補發降級期間的薪水**（設計簡化：降級 = 暫停薪水管線）

---

### 5.5 資料驗證邊界（DataManager）

**Case 5.5.1 — 池全稀有度空（`|emptyTiers| == 5`）**
- **觸發**：`StaffGachaPoolTable.eligibleStaffIDs` 空 / 所有 `staffWeights[i] == 0`
- **行為**：DataManager 載入時拋 `StaffGachaPoolTableValidationException("poolID={id}: 池無可抽項（所有層為空）")`；遊戲不啟動（fail-fast）

**Case 5.5.2 — `poolEligibleByRarity(poolID, r)` 因單層 `staffWeights` 全 0**
- **觸發**：層內有職員但 `staffWeights[i]` 全為 0（例如暫時禁用整層）
- **行為**：該層加入 `emptyTiers`（§4.1.5 定義已 filter `w > 0`）；歸一化分母自動重算；不觸發 validation 異常（屬於**合法降級配置**，與「全池空」區別）

**Case 5.5.3 — `StaffTable[staffID].rarity` 與 CandidateCard `rolledRarity` 不一致**
- **觸發**：載入舊存檔、`StaffTable` 中該 `staffID` 的 rarity 欄位被調整
- **行為**：FT-10 存檔載入時驗證 `staffID > 0 → rolledRarity == StaffTable[staffID].rarity`（§3.2.3 驗證規則）；不一致拋 `CandidateCardValidationException`；FT-10 決定 fail-fast 或遷移策略（FT-08 僅提供驗證契約）

**Case 5.5.4 — `staffID > 0 AND trashItemID > 0`（互斥違反）**
- **觸發**：資料表編輯錯誤、存檔損毀
- **行為**：DataManager / FT-10 載入時拋 `CandidateCardValidationException("XOR violation")`（§3.2.3 驗證規則）

---

### 5.6 系統邊界（System Gate）

**Case 5.6.1 — 職員休息室升降級途中 refresh 計時器行為**
- **觸發**：自動刷新倒數中（例：L1 = 24h，已過 20h）、玩家升級至 L3（`interval` 改為 12h）
- **行為**：`lastAutoRefreshTimestamp` 不因升級重置；立即以**新 interval** 計算 `now − last`：若 `now − last ≥ intervalSec(新等級)` → 立刻觸發補刷一次、`lastAutoRefreshTimestamp ← now`；否則照舊倒數（用新 interval）
- **極端例**：L1 已過 20h、升 L3（12h）→ 已超過新 interval → 立即補刷（玩家可感知「升級後立刻多一次面試」）

**Case 5.6.2 — 職員休息室從 L1+ 降至 L0**
- **觸發**：（Jam 版無此流程，但定義契約以防未來新增）設計上不允許降級至 L0；若發生（debug）：`IsStaffSystemUnlocked() == false` → 系統全面降級（§3.6.5 / §5.4.3）；`pityCounter` / roster / `lastAutoRefreshTimestamp` 資料保留、重新升回 L1 時恢復使用
- **`currentCandidates` 處理**：降級瞬間清空 `currentCandidates`（不可面試）；`reservedCandidates` 保留（資料不丟，視為「保留時限暫停倒數」——但 Jam 版時限倒數仍照走，解除後清為丟失視同 Case 5.2.4）

---

## 6. 依賴關係（Dependencies）

> 本節採「上游 / 下游 / 事件 / 資料 / 常數」五類列示 FT-08 的所有依賴介面。**雙向對齊清單**（需寫回對方 GDD 的依賴聲明）見 A.4 跨系統副作用清單。

### 6.1 上游依賴（FT-08 消費）

| 依賴系統 | API / 契約 | 消費時機 | 降級行為（對方缺席時）|
|---|---|---|---|
| **FT-07 Guild Building System** | `IsStaffSystemUnlocked() : bool` | 所有公開 API 入口檢查 | FT-08 全面降級（§3.6.5 / §3.12 / §5.4.3）|
| FT-07 | `GetBuildingLevel(6) : int` | §4.1.3 自動刷新間隔 | 回 0 視為 L0，系統閘關閉 |
| FT-07 | `GetBuildingLevel(1/4/5) : int` | §3.6 slot 指派驗證（委託板 / 櫃台 / 保險櫃）| slot capacity = 0，對應 effect 回 0 |
| FT-07 | `BuildingTable[6].upgradeData[L].effectValue` | §4.1.3 間隔查表（經 FT-07 讀 CSV）| 同上 |
| **F-02 TimeSystem** | `GetUtcNow() : long` | 所有時間戳運算（`now`）| FT-08 拋異常（時間系統為硬依賴）|
| **F-03 DataManager** | CSV 載入 + schema 驗證 API | 啟動期載入 5 張 FT-08 表 | 驗證失敗 → fail-fast，不啟動遊戲 |
| **F-01 ResourceManagement** | `GetGold() : long` / `TryDeductGold(amount) : bool` | §4.1.2 手動刷新扣款 | FT-08 手動刷新回 `GOLD_INSUFFICIENT` |

### 6.2 下游消費者（FT-08 被消費）

| 消費者 | 消費介面 | 語意 |
|---|---|---|
| **FT-03 NPC Decision** | `GetStaffWillingnessBonus() : float` | Passive 加成；進 willingness 計算前作為 addend |
| **FT-05 Guild Gold Flow** | `GetAccountantCommissionBonus() : float` | Passive 加成；進 `effectiveCommissionRate` 公式 |
| FT-05 | `GetAccountantPenaltyBonus() : float` | Slot 加成（需指派保險櫃）；進 `effectivePenaltyRate` |
| FT-05 | `OnStaffSalaryDue` 事件訂閱（**Phase 2，Jam 版不發布**）| 收到 payload → `AddGoldAllowBankruptcy(-totalAmount)`；Jam 版 FT-08 §3.11 不實作 → FT-05 訂閱 callback 永不觸發、為 no-op（與 FT-05 §3.7 立場一致）|
| **FT-01 Adventurer Recruitment** | `GetRecruitRefreshReductionSec() : int` | Slot 加成（需指派櫃台）；自招募刷新間隔扣除 |
| **P-02 Main UI** | `IsSuccessRatePreviewEnabled() : bool` | 委託審核畫面顯示成功率預估數字 |
| P-02 | 面試 UI 操作 API（`TryRecruit` / `TryRejectCandidate` / `TryReserveCandidate` / `TryReleaseReserve` / `TryManualRefresh` / `TrySwitchPool`）| 玩家面試交互的 UX 行為（簡歷卡、上下滑翻動、蓋印章）|
| **FT-10 Save/Load** | `StaffPlayerState` + `StaffInstance[]` + `CandidateCard[]` 序列化 / 反序列化 | 跨會話持久化；§3.2.3 schema 契約 |
| **FT-09 Faction Story** *(Post-Jam)* | `StaffTable.factionID` 欄位、`StaffGachaPoolTable` 5 個預留閘欄位 | Jam 版保留 schema 不消費 |

### 6.3 事件契約

**FT-08 發布**：

| 事件名 | Payload | 訂閱者 | 發布時機 |
|---|---|---|---|
| `OnStaffSalaryDue`（**Phase 2 / Jam 版不發布**）| `(long dueTimestamp, Dict<int,int> perStaffSalary, int totalAmount)` | **FT-05**（硬契約 §3.9.1，Jam 版 no-op）| 每日 UTC 06:00；離線補發逐次；§4.3 詳述。Jam 版 §3.11 整段為 Phase 2 不實作，本事件無 publisher；Phase 2 啟用條件：FT-08 §3.11 + FT-05 §3.7 同步解 Phase 2 註記。Dict key 為 `instanceID`（FT-08 內部語意）；FT-05 僅 iterate 不關心 key 語意 |
| `OnStaffHired` | `{ instanceID: int, staffID: int, hiredTimestamp: long }` | P-02 / P-03 Notification / 圖鑑 | 錄用流程成功後 |
| `OnStaffFired` | `{ instanceID: int, staffID: int, firedTimestamp: long, severancePaid: int }` | P-02 / P-03 | 解雇流程成功後 |
| `OnStaffAssigned` | `{ instanceID: int, oldBuildingID: int, newBuildingID: int }` | FT-05（保險櫃 slot 重算）/ P-02 | `TryAssignStaff` 成功後（首次落位 / 切建築，A2 案）/ `TryUnassignStaff` 成功後（newBuildingID = 0）|
| `OnStaffStateChanged` | `{ instanceID: int, oldState: enum, newState: enum }` | P-02（名冊 UI 更新）| 狀態機切換時 |

**FT-08 訂閱**：無硬依賴的訂閱——所有上游狀態皆**拉取式**（按需查詢 `GetBuildingLevel` / `GetUtcNow` / `GetGold`）。可選優化：訂閱 `OnBuildingUpgraded(buildingID=6)` 主動觸發 Case 5.6.1 間隔重算（Jam 版不做，於刷新觸發時即時 query 即可）。

### 6.4 資料表依賴

| 表名 | 擁有者 | FT-08 用途 |
|---|---|---|
| `StaffTable.csv` | FT-08 | 職員模板（`rarity` / `salary` / `effectIDs` / `effectValues` / `slotBuildingIDs` …）|
| `StaffGachaPoolTable.csv` | FT-08 | 池配置（`eligibleStaffIDs` / `staffWeights` / `reserveTimeLimitSec`）|
| `StaffRefreshCostTable.csv` | FT-08 | 手動刷新費 + 面試欄張數（PK = `guildLevel`）|
| `StaffRarityProbTable.csv` | FT-08 | 1★~5★ 基底權重（§4.1.5）|
| `TrashItemTable.csv` | FT-08 | flavor item 定義（`trashItemID` → name / flavor text）|
| `BuildingTable.csv` | **FT-07** | `buildingID=6` 的 `upgradeData[L].effectValue` = 自動刷新間隔（§4.1.3）|
| `SystemConstants.csv` | **F-03** | `OFFLINE_MAX_SECONDS` / `SALARY_UTC_HOUR`（=6）|

### 6.5 共享常數

| 常數 | 來源 | FT-08 用途 |
|---|---|---|
| `OFFLINE_MAX_SECONDS` | `SystemConstants.csv` | §4.3.3 薪水補發上限 |
| `SALARY_UTC_HOUR` | `SystemConstants.csv` | 每日發薪時點（固定 UTC 06:00）|
| `EFFECT_MAX_WILLINGNESS_BONUS` | FT-08 `StaffTuning.csv`（§7）| §4.2 cap |
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | 同上 | §4.2 cap |
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | 同上 | §4.2 cap |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | 同上 | §4.2 cap |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | 同上 | 再分配自動轉休假閾值（43200s；§3.8 詳述）|
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | 同上 | 切換建築冷卻（7200s；§3.8 詳述）|
| `ROSTER_CAP` | 同上 | Jam 版名冊上限（99）|

### 6.6 雙向聲明同步表

下列為 FT-08 Jam 版 GDD 通過後需於對方 GDD 補登記的雙向依賴（詳 A.4）：

| 對方 GDD | 需補登記內容 | 狀態 |
|---|---|---|
| FT-07 §3.2 / §5.7 / §7.1 / §7.3 | 職員休息室 `maxLevel = 5` + 自動刷新間隔階梯 | ✅ 已寫入（2026-04-25 C1 對齊）|
| FT-01 §6 | `GetRecruitRefreshReductionSec()` 消費、slot effect 來源為 FT-08 | ⬜ 待處理 |
| FT-03 §6 | `GetStaffWillingnessBonus()` 消費已登記、Jam 版無需新增 | ✅ 已存在 |
| FT-05 §6 / §7 | `OnStaffSalaryDue` 發布者為 FT-08；`GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` 來源 | ⬜ 待處理 |
| FT-10 §3 | 序列化契約：`StaffPlayerState` + `StaffInstance[]` + `CandidateCard[]` | ⬜ 待處理 |
| P-02 §6 | 面試 UI UX 責任：簡歷卡 / 上下滑翻動 / 蓋印章 / 保留按鈕 disable 依 `reserveConsumedFlag` | ⬜ 待處理 |
| `systems-index.md` | FT-08 升級為「Designed（Jam 版完成）」 | ⬜ 待處理 |

---

## 7. 可調參數（Tuning Knobs）

> 本節列出 FT-08 所有可由設計師於資料表 / 常數調整的參數。每項標註安全範圍、影響面、與 § 公式 / 規則的對應錨點。**不可調參數**（設計決策硬寫）見 §7.4。

### 7.1 資料表參數（CSV）

#### 7.1.1 `StaffRefreshCostTable.csv`

PK = `guildLevel`；對應 §4.1.1 / §4.1.2

| 欄位 | 類型 | Jam 版建議範圍 | 影響 |
|---|---|---|---|
| `guildLevel` | int (PK) | `1 ~ 5` | 必含 1~5 五行 |
| `cost` | int | `100 ~ 1000` gold | 手動刷新費；過低 → 玩家狂刷破壞稀有度體感、過高 → 玩家不刷只等自動 |
| `interviewSlotCount` | int | `3 ~ 5` | 面試欄張數 N；過低 → 保底速度太慢、過高 → UI 擁擠 + 保底速度太快 |

**Jam 版範例配置**：

| guildLevel | cost | interviewSlotCount |
|---|---|---|
| 1 | 100 | 3 |
| 2 | 150 | 3 |
| 3 | 250 | 4 |
| 4 | 400 | 4 |
| 5 | 600 | 5 |

#### 7.1.2 `StaffRarityProbTable.csv`

PK = `rarity`；對應 §4.1.5

| 欄位 | 類型 | Jam 版鎖定 | 影響 |
|---|---|---|---|
| `rarity` | int (PK) | `1 ~ 5` | 必含 5 行 |
| `baseProb` | float | `Σ = 1.00`（強制驗證）| 稀有度基底機率；改動會劇烈影響 5★ 期望抽數與保底兌現節奏 |

**Jam 版鎖定值**：1★=0.40 / 2★=0.25 / 3★=0.15 / 4★=0.15 / 5★=0.05；DataManager 驗證 `Σ baseProb == 1.00 ± 0.001`，否則拋 `StaffRarityProbTableValidationException`

#### 7.1.3 `StaffGachaPoolTable.csv`

詳 §3.2.2；對應 §4.1.5 / §4.1.6 / §4.1.9

| 欄位 | Jam 版安全範圍 | 影響 |
|---|---|---|
| `[min,max]GuildLevel` | `1 ≤ min ≤ max ≤ 5` | 池開放時機；Jam 版 A 池 = `[1,5]`、B 池 = `[3,5]` |
| `eligibleStaffIDs` | 至少含 1 個 staffID 且至少 1 層 `staffWeights[i] > 0` | 池內職員集合；過少 → 抽到重複頻繁、過多 → 5★ 機率被稀釋 |
| `staffWeights` | 整數 ≥ 0；Σ > 0 | 層內機率權重；同層用 1~10 區間即可表達相對稀有 |
| `reserveTimeLimitSec` | `86400 ~ 604800`（1~7 天）| 保留時限；過短 → 玩家剛保留就過期、過長 → 跨池保留變成永久免費櫃位 |

**Jam 版範例**（各池含 1~5★ 全層、約 10 個職員 / 池）：

| poolID | poolName | minGL | maxGL | reserveTimeLimitSec | 設計意圖 |
|---|---|---|---|---|---|
| 1 | Common | 1 | 5 | 604800（7 天）| 全程開放、含 1~5★ 全層 |
| 2 | Advanced | 3 | 5 | 604800（7 天）| 中後期開放、3~5★ 為主、品質較高 |

#### 7.1.4 `StaffTable.csv` 數值欄位

詳 §3.2.1；單職員可調

| 欄位 | Jam 版安全範圍 | 影響 |
|---|---|---|
| `salary` | `0 ~ 100` gold/day | 每日扣款；總和影響金幣壓力（FT-05）|
| `severancePay` | `0 ~ 500` gold | 解雇成本；建議 `≈ salary × 5`（5 日薪）|
| `effectValues[i]` | 依 effect 類型不同 | 見 §7.2 effect 數值表 |
| `isFiller` | `true / false` | 標記 trash 類職員（仍可入職、薪水低、effect 弱）|

**Jam 版總薪水壓力建議**：完整 roster 5~10 位常駐 + 0~10 位偶發 → 每日總薪水 `≤ 500 gold/day`，與 FT-05 委託利潤對齊（不超過單日預期收入 30%）

#### 7.1.5 effect 數值欄位（`StaffTable.effectValues`）

每個 effect 的單一數值範圍：

| effectID | 類型 | Jam 版單值範圍 | 設計意圖 |
|---|---|---|---|
| `Willingness` | float | `+0.01 ~ +0.05` | 委託官加成；3 位疊加 ≈ +0.15 接近 cap |
| `AccountantCommission` | float | `+0.01 ~ +0.03` | 會計傭金；2 位疊加 ≈ +0.06 接近 cap |
| `AccountantPenaltyOnVault` | float | `−0.05 ~ −0.01` | 會計賠償減免（負值）；slot capacity = 1 自然封頂 |
| `RecruitRefreshOnCounter` | int (秒) | `1800 ~ 7200`（30min ~ 2h）| 招募刷新減量；2 位疊加 ≈ 4h 接近 cap |

**單職員 effect 數量受 rarity 限制**（§3.2.1）：1★=1 / 2★=1 / 3★=2 / 4★=2 / 5★=3

#### 7.1.6 `TrashItemTable.csv`

詳 §3.5（待寫）；Jam 版安全範圍：

| 欄位 | 安全範圍 | 影響 |
|---|---|---|
| `trashItemID` | int ≥ 1 | PK |
| `name` / `flavorText` | string | 風味文字（writer agent 提供）|
| 數量 | `5 ~ 15` 個 | 過少重複頻繁、過多稀釋有意義抽卡 |

### 7.2 系統常數（`StaffTuning.csv` / `SystemConstants.csv`）

> 建議獨立一張 `StaffTuning.csv` 集中放 FT-08 系統常數；通用常數（離線上限 / 發薪時點）放 `SystemConstants.csv`

| 常數名 | Jam 版值 | 安全範圍 | 影響 |
|---|---|---|---|
| `EFFECT_MAX_WILLINGNESS_BONUS` | `+0.20` | `+0.10 ~ +0.40` | Willingness 總加成 cap；建議 ≤ FT-03 willingness 公式分母的合理上界 |
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | `+0.10` | `+0.05 ~ +0.20` | 傭金總 cap；建議與 FT-05 委託基礎利潤對齊 |
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | `−0.10` | `−0.20 ~ −0.05` | 賠償減免下限；負值，絕對值越大玩家受益越多 |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | `14400`（4h）| `3600 ~ 28800`（1h~8h）| 招募刷新減量上限；建議不超過 FT-01 基礎刷新間隔 50% |
| `PITY_THRESHOLD` | `10` | `5 ~ 20` | 保底觸發閾值（單位：累積張數）；過低 → 保底太頻繁失去隨機性、過高 → 玩家挫折 |
| `SALARY_UTC_HOUR` | `6` | `0 ~ 23` | 每日發薪時點；UTC 06:00 ≈ 大多數時區的非 prime time |
| `OFFLINE_MAX_SECONDS` | `604800`（7 天）| `86400 ~ 2592000`（1~30 天）| 離線最大補發；F-02 通用常數 |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | `43200`（12h）| `21600 ~ 86400`（6~24h）| 再分配自動轉休假閾值 |
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | `7200`（2h）| `3600 ~ 14400`（1~4h）| 切換建築冷卻 |
| `ROSTER_CAP` | `99` | `20 ~ 200` | 名冊上限；UI 與序列化效能限制 |

### 7.3 FT-07 擁有的 FT-08 相關參數

> 雖屬 FT-07 `BuildingTable.csv`，但語意由 FT-08 決定，調整時兩 GDD 同步

| 行（buildingID=6）| 欄位 | Jam 版值 | 安全範圍 | 影響 |
|---|---|---|---|---|
| L1 | `effectValue`（自動刷新間隔秒）| `86400`（24h）| `≥ 21600`（6h）| 起手節奏 |
| L2 | 同上 | `64800`（18h）| L1 > L2 | 階梯遞減 |
| L3 | 同上 | `43200`（12h）| L2 > L3 | |
| L4 | 同上 | `28800`（8h）| L3 > L4 | |
| L5 | 同上 | `21600`（6h）| L4 > L5；建議 ≥ 3600（1h）| 滿級節奏；過低破壞「等待感」設計 |
| L1~L5 | `upgradeCost` | `500 / 1500 / 4500 / 13500 / 40000` | ×3 遞增；總 ≤ FT-05 同期累積收入 50% | 升級成本壓力 |
| L2~L5 | `guildLevelReq` | `2 / 3 / 4 / 5` | 對齊公會等級開放 | 升級門檻 |

### 7.4 不可調參數（設計決策硬寫）

下列參數**不應透過資料表暴露**，改動需 GDD 變更：

| 參數 / 規則 | 值 | 理由 |
|---|---|---|
| 三態互斥 `{Working, Reallocating, OnLeave}` | enum | 狀態機完整性；改動需重設計 §3.8 |
| `staffID` / `trashItemID` 互斥（XOR）| 規則 | CandidateCard schema 保證；違反 = 資料異常 |
| 保底計數單位（每張 +1） | 規則 | 與 N 解耦的均勻體感；改為「每次 refresh +1」會使 N=5 與 N=3 保底速度差太大 |
| 保底反 reset（5★ 空池不 reset）| 規則 | 公平性；玩家不應因池暫時 5★ 空而失保底 |
| `maxReserve = max(1, N-1)` 公式 | 規則 | 保留容量恆 < 面試欄；防玩家全保留繞過決策壓力 |
| `reserveConsumedFlag` 永久化 | 規則 | 防「保留→取消→保留」零成本繞刷 |
| 切池副作用（前池未保留候選丟失）| 規則 | 保留區是跨池保留的唯一途徑；維持池切換的 stake |
| 動態歸一化公式（`baseProb / (1 − Σ 空層)`） | 公式 | 機率質量守恆；保證 Σ effectiveProb = 1 |
| `lastAutoRefreshTimestamp` 全域單一 | 結構 | 防玩家切池偷跑刷新節奏；§3.2.3 設計約束 |
| 離線補刷 `clamp 0~1` | 規則 | 防離線爆量候選；§4.1.4 |
| `IsStaffSystemUnlocked() == false` 行為 | 降級契約 | 加成回 0、薪水不發、面試回 LOCKED；§3.6.5 / §5.4.3 |

### 7.5 平衡建議（Designer Notes）

- **5★ 期望抽數**：基底 5%、保底 10 抽 → 期望命中 ≈ 第 10 抽；1 次手動刷新（N=4）≈ 4 張 → 約 2.5 次手動刷新觸發保底；若以 cost=400/refresh、`guildLevel=4`計，玩家累積 1000 gold 即可保底 1 次 5★
- **薪水 vs 委託利潤**：完整名冊（10 位職員 × 平均 50 gold = 500 gold/day）應 ≤ 同期委託每日預期淨利的 30%；超過會讓玩家陷入「養不起職員」的負迴圈
- **加成 cap 兌現門檻**：每個 cap 的設計目標是「3~5 位職員疊滿」，避免 1 位 5★ 就頂滿（壓低 5★ 價值）也避免 10+ 位才頂滿（玩家感受不到增量）
- **保留時限 7 天**：覆蓋週末玩家斷線情境；過短會讓「保留」變成「短期暫存」失去策略意義

---

## 8. 驗收標準（Acceptance Criteria）

> 每條 AC 應可由 QA 直接驗證 pass / fail；測試流程不依賴 UI（直接呼叫 API）。AC 編號跨章節連續（AC-1 ~ AC-N）以利 Codex 工項追蹤。

### 8.1 系統初始化與閘控（System Init / Gate）

- **AC-1**：新遊戲啟動且 `FT07.GetBuildingLevel(6) == 0` → `IsStaffSystemUnlocked() == false` → 所有面試 API（`TryRecruit` / `TryManualRefresh` / `TryReserveCandidate` / `TrySwitchPool`）回 `STAFF_SYSTEM_LOCKED`；所有加成 API 回 `0`；`IsSuccessRatePreviewEnabled() == false`；`OnStaffSalaryDue` 不發布
- **AC-2**：升級職員休息室至 L1 → `IsStaffSystemUnlocked() == true` → 首次解鎖時 FT-08 啟動流程設 `lastAutoRefreshTimestamp ← now`、`pityCounter` 初始 `0`、`nextInstanceID` 初始 `1`、`currentPoolID` 初始 `1`（A 池）
- **AC-3**：拆除職員休息室回 L0（debug）→ 系統降級：`StaffPlayerState` 與 `StaffInstance[]` 資料保留、加成 API 回 `0`、薪水不發；重新升回 L1 → 資料恢復可用、`pityCounter` 不 reset

### 8.2 面試刷新（Refresh Flow）

- **AC-4**：手動刷新 `guildLevel = 2` 且 `GetGold() ≥ 150` → `TryManualRefresh()` 回 `SUCCESS`；扣款 150；`currentCandidates` 填滿 N=3 張；`pityCounter += 3`；`lastAutoRefreshTimestamp` **不變**
- **AC-5**：手動刷新 `GetGold() < cost` → 回 `GOLD_INSUFFICIENT`；金幣不變、候選不變、`pityCounter` 不變
- **AC-6**：自動刷新間隔到（`now − lastAutoRefreshTimestamp ≥ autoRefreshIntervalSec(L)`）→ 觸發 1 次刷新、覆蓋 `currentCandidates`、`pityCounter += N`、`lastAutoRefreshTimestamp ← now`
- **AC-7**：離線跨多個自動刷新觸發點（例：L1 = 24h、離線 72h）→ 登入時補刷次數 `clamp(missedIntervals, 0, 1) = 1`；`pityCounter` 僅 +N 一次；`lastAutoRefreshTimestamp ← now`
- **AC-8**：`now < lastAutoRefreshTimestamp`（時鐘倒退）→ 不觸發補刷、`lastAutoRefreshTimestamp` 不變
- **AC-9**：升級職員休息室從 L1 至 L3 且 `now − last ≥ intervalSec(L3)` → 立即補刷一次（Case 5.6.1）

### 8.3 池切換與保留（Pool Switch & Reserve）

- **AC-10**：玩家從 A 池切到 B 池 → `currentPoolID ← 2`、A 池 `currentCandidates` **整個被覆蓋為 B 池新 roll 結果**、`reservedCandidates`（兩池共用）內容不變、`pityCounter += N`、不扣金幣
- **AC-11**：保留候選 `TryReserveCandidate(slotIndex)` → 該候選 `isReserved = true`、`reservedTimestamp = now`、加入 `reservedCandidates`；`currentCandidates[slotIndex]` 為空（鎖定）；下次 refresh 不覆蓋該 slot
- **AC-12**：`reservedCandidates.Count == maxReserve` 時嘗試保留第 `maxReserve + 1` 張 → `TryReserveCandidate()` 回 `RESERVE_FULL`；該候選 `isReserved` 不變
- **AC-13**：保留候選 `TryReleaseReserve(reserveIndex)`（手動取消）→ 卡回到 `currentCandidates[原 slotIndex]`、`isReserved = false`、`reservedTimestamp = 0`、`reserveConsumedFlag = true`；嘗試再次保留同卡 → 回 `RESERVE_CONSUMED`
- **AC-14**：保留時限到（`now − reservedTimestamp ≥ reserveTimeLimitSec(poolID)`）→ 自動解除保留（同 AC-13 行為）；若離線期間自動補刷已覆蓋原 slot → 卡丟失（不回 `currentCandidates`、不回 `reservedCandidates`，Case 5.2.4）

### 8.4 稀有度 / 保底（Rarity / Pity）

- **AC-15**：池中所有稀有度層皆有可抽（`emptyTiers == ∅`）→ `effectiveProb` 即 `baseProb`；統計 10000 次 roll → 5★ 比例 ≈ 5% ± 1%
- **AC-16**：池中 1★ / 2★ 層空（`staffWeights` 全 0）→ `emptyTiers = {1, 2}`、`normalizationDen = 0.35`；`effectiveProb[3] ≈ 0.4286`、`effectiveProb[5] ≈ 0.1429`；`Σ effectiveProb == 1.000 ± 0.001`
- **AC-17**：池內全層空（`eligibleStaffIDs` 空 / `staffWeights` 全 0）→ DataManager 載入時拋 `StaffGachaPoolTableValidationException`；遊戲不啟動
- **AC-18**：`pityCounter ≥ 10` 且 `poolEligibleByRarity(currentPoolID, 5) ≠ ∅` 時下次 refresh → 第一張 `rolledRarity == 5`、後續張數走正常 roll、`pityCounter ← 0`
- **AC-19**：`pityCounter ≥ 10` 但池中 5★ 空 → 不強制 5★；`pityCounter` 繼續累積（不 reset）；玩家切到含 5★ 的池後下次 refresh 兌現
- **AC-20**：tier 內 `staffWeights = [10, 5, 0]` → 統計 10000 次該層 roll → 第一個 staffID 比例 ≈ 0.667、第二個 ≈ 0.333、第三個 = 0

### 8.5 錄用 / 不錄用（Hire / Reject）

- **AC-21**：`TryRecruit(slotIndex)` 對 `staffID > 0` 候選 → 建立 `StaffInstance`（`instanceID = nextInstanceID`、`++nextInstanceID`）、加入 roster、發 `OnStaffHired`；`currentCandidates[slotIndex]` 空置至下次 refresh
- **AC-22**：`TryRecruit(slotIndex)` 對 `trashItemID > 0` 候選 → 回 `CANDIDATE_NOT_HIREABLE`（trash item 不可入職）；候選 / 名冊 / 事件不變
- **AC-23**：`TryRejectCandidate(slotIndex)` → `currentCandidates[slotIndex]` 空置至下次 refresh、不入名冊、不發事件
- **AC-24**：`TryRecruit` 時名冊已達 `ROSTER_CAP` → 回 `ROSTER_FULL`；候選不變

### 8.6 效果聚合（Effect Aggregation）

- **AC-25**：3 位委託官（每位 `Willingness +0.05`、狀態 = Working）→ `GetStaffWillingnessBonus() == 0.15`；增至 5 位（Σ = 0.25）→ 回 `0.20`（cap 截斷）
- **AC-26**：1 位委託官切態為 OnLeave → 該位不計入 SUM；Reallocating 仍計入（Passive 規則）
- **AC-27**：1 位會計帶 `AccountantPenaltyOnVault = -0.02`、未指派保險櫃 → `GetAccountantPenaltyBonus() == 0`；指派保險櫃（`assignedBuildingID = 5`、狀態 = Working）→ 回 `-0.02`
- **AC-28**：2 位職員都帶 `SuccessRatePreview` UI flag、僅 1 位指派委託板 → `IsSuccessRatePreviewEnabled() == true`（OR 聚合）
- **AC-29**：`IsStaffSystemUnlocked() == false` → 5 個 API 全部直接回 `0` / `false`，**不遍歷 roster**（早退驗證；可用 mock log 觀察）

### 8.7 薪水（Salary）

> **⚠️ Phase 2 — AC-30 ~ AC-34 整段 Jam 版不驗證（B 案，2026-04-25 使用者裁示）**
>
> §3.11 為 Phase 2 不實作，下列 AC 無觸發路徑可測；**Jam QA 驗收時自動視為 PASS（vacuously true，無事件 publisher）**。Phase 2 啟用時（FT-08 §3.11 + FT-05 §3.7 同步解 Phase 2 註記）才完整跑下列 AC。

- **AC-30**（**Phase 2**）：UTC 06:00 觸發發薪 → `OnStaffSalaryDue` 發布、payload `perStaffSalary` 含所有 `currentState ≠ OnLeave` 職員（key = `instanceID`、value = `StaffTable[s.staffID].salary`）；`totalAmount == Σ values`
- **AC-31**（**Phase 2**）：1 位職員 `currentState == OnLeave` → 該 `instanceID` **不在** `perStaffSalary` 中、`totalAmount` 不含其薪水
- **AC-32**（**Phase 2**）：離線跨 3 天登入 → `OnStaffSalaryDue` 連續發布 3 次（各以對應 `dueTimestamp` 組裝）；`lastSalaryTimestamp` 更新至最新一次（`lastSalaryTimestamp` 為 FT-08 內部狀態，不在事件 payload 中）
- **AC-33**（**Phase 2**）：離線 30 天登入、`OFFLINE_MAX_SECONDS = 604800` → `OnStaffSalaryDue` 發布次數 = `clamp(30, 0, 7) = 7` 次
- **AC-34**（**Phase 2**）：發薪時 `now < lastSalaryTimestamp`（時鐘倒退）→ 跳過本次發薪、`lastSalaryTimestamp` 不更新、不發事件、不扣金幣
- **AC-30J ~ AC-34J（Jam 版替代驗證，整段合一）**：FT-08 啟動到結束，**從未發布 `OnStaffSalaryDue`**、**金幣不因職員存在而減少**、`lastSalaryTimestamp` 永遠 `0`、未訂閱 `OnHourTick` 或 OnSalaryTick callback；可用 EventBus mock log 驗證「無 OnStaffSalaryDue 入列」

### 8.8 資料驗證（DataManager）

- **AC-35**：`StaffTable` 中某行 `effectIDs.Count != effectValues.Count` → 載入時拋 `StaffTableValidationException`、遊戲不啟動
- **AC-36**：`StaffTable` 中某 5★ 職員 `effectIDs.Count > 3`（超過 rarity 上限）→ 拋 `StaffTableValidationException`
- **AC-37**：`StaffGachaPoolTable` 中 `eligibleStaffIDs.Count != staffWeights.Count` → 拋 `StaffGachaPoolTableValidationException`
- **AC-38**：`StaffRarityProbTable.baseProb` 加總 ≠ 1.00 ± 0.001 → 拋 `StaffRarityProbTableValidationException`
- **AC-39**：CandidateCard 載入時 `staffID > 0 AND trashItemID > 0`（XOR 違反）→ 拋 `CandidateCardValidationException`
- **AC-40**：CandidateCard 載入時 `staffID > 0 AND rolledRarity != StaffTable[staffID].rarity` → 拋 `CandidateCardValidationException`

### 8.9 事件契約（Event Contracts）

- **AC-41**：錄用成功 → `OnStaffHired` 發布且 payload 含 `{ instanceID, staffID, hiredTimestamp == now }`
- **AC-42**：解雇成功 → `OnStaffFired` 發布、`severancePay` 從金幣扣除（透過 FT-05 `AddGoldAllowBankruptcy`）；`StaffInstance` 從 roster 移除
- **AC-43**：slot 指派 / 切換完成 → `OnStaffAssigned` 發布、payload 含 `{ instanceID, oldBuildingID, newBuildingID }`
- **AC-44**：狀態機切換 → `OnStaffStateChanged` 發布、payload 含 `{ instanceID, oldState, newState }`
- **AC-45**：所有事件於系統降級期間（`IsStaffSystemUnlocked() == false`）**不發布**

### 8.10 效能與規模（Performance / Scale）

- **AC-46**：roster `Count == ROSTER_CAP`（99）時，5 個加成 API 回應時間 < 1 ms（內部 O(N) 遍歷）
- **AC-47**：`currentCandidates.Count == N` + `reservedCandidates.Count == maxReserve` 同時持久化、FT-10 序列化 / 反序列化 round-trip 後資料完整一致

---

## 附錄 A — WIP 進度與下次待解問題

> 此附錄為 `/design-system` 互動設計進度追蹤，GDD 完成時可移除。

### A.1 已完成章節

| 章節 | 狀態 | 備註 |
|------|------|-------------|
| §1 Overview | ✅ | Batch B v2 M1~M5 宏觀對齊完成後回填（2 池、自動+手動刷新、保留機制、跨池保底） |
| §2 Player Fantasy | ✅ | Q2-1（E1 + E4 Jam 聚焦，E2/E5/E7 延後至 Post-Jam 人事系統）/ Q2-2(A) / Q2-3(A+C) / Q2-4(A) |
| §3.1 StaffInstance | ✅ | Q3A-1(A) / Q3A-2(A 備註 Jam 暫代) / Q3A-3(A) |
| §3.2.1 StaffTable Schema | ✅ | Schema 主體不動（Batch A 結論）+ `uiFlagIDs` / `uiFlagBuildingIDs` 欄位 |
| §3.2.2 StaffGachaPoolTable Schema | ✅ | Batch B v2 新增：`poolID` PK + `[min,max]GuildLevel` + `staffWeights` + `reserveTimeLimitSec` + 5 個預留閘欄位 |
| §3.2.3 StaffPlayerState + CandidateCard Schema | ✅ | C1/C2/C3 對齊後新增：player-level 狀態容器 + 面試候選卡資料結構（Round 2 M2-1~3 / Round 3 M3-1~3） |
| §3.3 面試刷新流程 | ✅ | ExecuteRefresh / RollOneSlot pseudocode、6 個 action API 契約、`OnStaffSystemBoot` 離線補刷、`StaffRefreshCostTable` schema、事件發布原則（2026-04-25） |
| §3.4 保底機制細節 | ✅ | `pityCounter` 生命週期表、命中流程（`isFirstSlotInRefresh` flag）、未命中三互動、與動態歸一化交互、跨 session 行為、Editor-only `DebugResetPityCounter`（2026-04-25） |
| §3.5 垃圾物品 | ✅ | trash vs filler staff 對照、`TrashItemTable` schema 5 範例（咖啡杯 / 履歷紙團 / 過期身份證 / 借據 / 退稿小說）、`TRASH_ROLL_RATE_AT_RARITY_1=0.30`、保底計入規則、與 filler 區別（2026-04-25） |
| §3.6 Effect Aggregation | ✅ | Q3A-9(A) / Q3A-10(B 並細分 Passive vs Slot) / Q3A-11(B) / Q3A-12(A) |
| §3.7 Slot 指派流程 | ✅ | `TryAssignStaff` / `TryUnassignStaff` 完整 pseudocode、capacity 規則、多 slot 候選、`BUILDING_SWITCH_COOLDOWN_SECONDS` 冷卻、idempotency（2026-04-25） |
| §3.8 三態狀態機 | ✅ | 10 列 transition table（Working / Reallocating / OnLeave 三態互斥）、API 契約、cooldown 語意、persistence validation（2026-04-25） |
| §3.9 再分配自動轉休假 | ✅ | `shouldAutoLeave` 條件（`REALLOCATING_AUTO_LEAVE_SECONDS=43200`）、`CheckReallocatingAutoLeave` checkpoints（Boot / OnHourTick / API 入口）、edge cases（2026-04-25） |
| §3.10 解雇流程 | ✅ | `TryFireStaff` 完整流程、不可逆性、event-vs-roster.Remove 順序決策（事件先發）、失敗情境（2026-04-25） |
| §3.11 薪水管線 | 🔵 Phase 2（Jam 不實作）| `AssemblePerStaffSalary` / `OnSalaryTick` / `ProcessOfflineSalary`、`lastSalaryTimestamp` 生命週期、`OnStaffSystemUnlocked` callback 重設邏輯。設計完整保留（2026-04-25），但對齊 FT-05 既有 Phase 2 立場（B 案裁示），Jam 版整段不實作；§3.11 / §4.3 / §5.4 / §8.7（AC-30~AC-34）同步標記 Phase 2 |
| §3.12 系統降級行為總表 | ✅ | 3 表（17 APIs / 5 events / 6 internal flows）涵蓋降級行為、AC mapping（2026-04-25） |
| §3.13 事件契約 | ✅ | 5 事件 payload、10 列發布順序表、subscriber 責任表、error isolation、payload immutability（2026-04-25） |
| **Batch A 附加決策** | ✅ | 新增 `RecruitRefreshOnCounter` effect + `SuccessRatePreview` UI flag |
| **Batch B v2 宏觀對齊** | ✅ | M1 池架構 / M2 閘機制 / M3 刷新模型 / M4 保留機制 / M5 保底範圍（詳 A.2.1）|
| **Pre-§4 阻斷項對齊** | ✅ | C1 FT-07 職員休息室 maxLevel=5 擴張 / C2 `StaffPlayerState` schema / C3 `CandidateCard` schema（詳 A.2.1b） |
| **Batch C 全部子節** | ✅ | §3.7 ~ §3.13 七子節同批寫入（2026-04-25 本 session）|
| §4 公式（Formulas） | ✅ / 🔵 §4.3 Phase 2 | §4.1 面試系統（9 公式）/ §4.2 效果聚合上限 / **§4.3 薪水公式為 Phase 2（Jam 不實作，B 案裁示）**|
| §5 邊緣案例（Edge Cases） | ✅ / 🔵 §5.4 Phase 2 | 6 大類：時間異常 / 面試流程 / 保底邊界 / **§5.4 薪水邊界為 Phase 2** / 資料驗證 / 系統閘 |
| §6 依賴關係（Dependencies） | ✅ | 6.1~6.6：上游 / 下游 / 事件 / 資料表 / 共享常數 / 雙向聲明同步表 |
| §7 可調參數（Tuning Knobs） | ✅ | 7.1~7.5：CSV 表 / 系統常數 / FT-07 同步參數 / 不可調 / 平衡建議 |
| §8 驗收標準（Acceptance Criteria） | ✅ / 🔵 §8.7 Phase 2 | AC-1 ~ AC-47 跨 10 子節（系統閘 / 刷新 / 池切換 / 保底 / 錄用 / 聚合 / **§8.7 AC-30~AC-34 薪水改 Phase 2 跳測，Jam 改用 AC-30J~AC-34J 替代驗證「無 OnStaffSalaryDue 入列」**/ 驗證 / 事件 / 效能） |
| 跨系統副作用（A.4） | ✅ Jam 範疇完成 | 8/8 反向依賴已寫入（FT-01 / FT-03 / FT-05 / FT-07 maxLevel 擴張 / FT-07 slotCount 補登 / systems-index FT-08 條目 / systems-index SystemConstants 註記 / `/design-review FT-08` + B 案 Phase 2 標記）；FT-10 / P-02 / P-03 反向依賴登記延後至對方 GDD 創建時補（A.4 末端列表，Phase 2 啟用時須同步解 FT-05 §3.7 Phase 2 註記）|

### A.2 Batch B v2 — 重啟對齊計畫

**重啟原因**（2026-04-24 第二次 session 重建）：
- 第一輪 Batch B 一次提問 10+ 題、混合宏觀與細節，導致決策疲勞
- DevLog 摘要本身有誤（「單一常駐池」被使用者糾正為多池）
- §1 / §3.2 因「單池」假設失效需回填修正
- 為避免第一輪對話成果遺失，已對齊事實與新暴露事實保留於 A.2.0

#### A.2.0 第一輪已對齊與新暴露事實

**✅ 已對齊（Batch B v2 不需重問）**：

- **面試 UX 模型 = 逐張操作**：刷新 → 展示 N 張 → 玩家對每張獨立決定「錄取 / 保留 / 丟棄」（原 Q3B-1 = A）
- **稀有度層為空 = 動態歸一化**：當前池某稀有度 R 無可抽職員時，該 R 的權重按比例分配到其他非空稀有度層，不走純降級；公式 `effectiveProb[r] = baseProb[r] / (1 − sum(baseProb[空層]))`（原 Q3B-5 = A）

**❗ 第一輪暴露的新事實 / 推翻的假設**（待 Batch B v2 對齊）：

- **多池並存 + 依等級逐階開放**：DevLog「單一常駐池」是誤記；實際每個池由 `[minGuildLevel, maxGuildLevel]` 控制開放區間，可多池並存（使用者舉例：A 池 Lv1~3 / B 池 Lv3~5 / C 池 Lv1~5 全開）
- **自動刷新機制存在**：先前 §1 只考慮付費手動刷新；使用者提到「不論自動刷新或手動刷新都可以保留」——自動刷新的頻率、費用、與等級的關係全待設計
- **候選保留機制**：玩家可跨刷新保留候選；推測「保留」= 候選暫停區（未入冊、未支薪、未生效），上限待定
- **個別職員權重**：稀有度 → 具體職員的 roll 不採均勻隨機，而是**每職員一個整數權重**（`StaffGachaPoolTable.staffWeights: CSV list` 平行於 `eligibleStaffIDs`；使用者口述為「第二段權重」）；整數、允許 0 待確認
- **歸一化 edge cases 未對齊**：多層同時空時的處理、保底觸發但池中該稀有度為空時的行為

#### A.2.1 宏觀對齊問題（Macro Alignment — Round 1）

> **原則**：每題只問大方向、不問細節數值；對齊完 M1~M5 才回填 §1 / §3.2，再進入細節 Q&A 寫入 §3.3 ~ §3.5。每輪最多 3 題，避免再次失焦。

**M1：池架構與分類（Pool Architecture）**
- Jam 版預計幾個池？（1 / 2 / 3+？）
- 多池如何分類？（按職能 tag / 按陣營 factionID / 按劇情階段 / 通用 vs 專業）
- 是否需要「常駐通用池」+「特定條件池」的混合？

**M2：池的開放/關閉機制（Pool Gating）**
- 純依公會等級 `[minGuildLevel, maxGuildLevel]` 區間（連續）？
- 是否需要其他閘（聲望門檻、FT-09 劇情解鎖、限時活動）？
- 是否允許不連續開放（例如 Lv1 + Lv3 + Lv5 開放，Lv2 / Lv4 關閉）？

**M3：刷新模型（Refresh Model）**
- 付費手動刷新（已確定）+ 自動刷新（新發現）並存？還是二擇一？
- 自動刷新的觸發（每日 UTC 06:00 / 等級決定間隔 / 其他）
- 自動刷新是否免費？是否依等級縮短間隔？
- 自動刷新是否覆蓋「未保留」候選、保留「已保留」候選？
- 自動刷新是否消耗保底 counter？

**M4：候選保留機制（Candidate Reserve）**
- 「保留」= 候選暫停區（未入冊、未支薪、不生效）這個語意正確嗎？
- 保留上限？（無上限 / 固定槽位 N / 與名冊上限共用）
- 保留動作是否需要付費？是否有時限？
- 「錄取」與「保留」是否互斥動作（一張只能其一），還是可同時選「保留 + 錄取」？

**M5：保底範圍與計算（Pity Scope）**
- 保底 counter 是「跨池累積」（原 DevLog）還是「per-pool 獨立」？
- 公會升級時是否 reset counter？
- 保底觸發但池中該稀有度為空時，counter 是否累積到下次有 5★？還是視為兌現後 reset？

#### A.2.1b Pre-§4 阻斷項（C1/C2/C3）對齊結果

2026-04-25 `/design-review` 標出 3 項 §4 公式阻斷項，Round 1~3 對齊後已全部解決：

**C1 — FT-07 職員休息室 maxLevel 擴張**（Round 1 M1-1/M1-2/M1-3）
- M1-1 = B：近等比遞減階梯 L1=86400s / L2=64800s / L3=43200s / L4=28800s / L5=21600s
- M1-2 = A：升級費保守 ×3 遞增 L2=1500 / L3=4500 / L4=13500 / L5=40000
- M1-3 = A：guildLevelReq 對齊等級 L2→Lv2 / L3→Lv3 / L4→Lv4 / L5→Lv5
- 寫入：FT-07 §3.2 / §5.7 / §7.1 `BuildingTable[6]` / §7.3

**C2 — `StaffPlayerState` schema**（Round 2 M2-1/M2-2/M2-3）
- M2-1 = A：自動刷新計時器全域單一（`lastAutoRefreshTimestamp`），到點刷當前選中池、切池不重置
- M2-2 = A：手動刷新無 cooldown（防呆 by UI）
- M2-3 = approve：6 欄位 schema（`nextInstanceID` / `pityCounter` / `lastAutoRefreshTimestamp` / `currentPoolID` / `currentCandidates` / `reservedCandidates`）
- 寫入：FT-08 §3.2.3 + §1 刷新模型補註

**C3 — `CandidateCard` schema**（Round 3 M3-1/M3-2/M3-3）
- M3-1 = A：Staff vs Trash 用互斥雙 ref 欄位（`staffID` / `trashItemID`，恰好一個 > 0）
- M3-2 = B：錄用 / 不錄用後 slot 空置至下次刷新；UX（簡歷卡、上下滑翻動、蓋印章）由 P-02 Main UI 負責
- M3-3 = approve：9 欄位 schema + 時點 3（保留解除後回到 slot、仍可錄用 / 不錄用、`reserveConsumedFlag=true` 禁用保留按鈕）
- 寫入：FT-08 §3.2.3 + §1 面試欄 UX 補註；P-02 UX 意圖登記於 A.4

**連帶解決的 A.2.2 細節題**：保留候選資料結構 ✅ / 保底計數單位（每張 +1）✅ / 保底 counter 存放位置（`StaffPlayerState.pityCounter`）✅

#### A.2.2 宏觀對齊後再展開的細節題（Detail Round — 先不展開）

M1~M5 + C1~C3 對齊後仍待展開：

- 池內職員清單具體組成（Jam 版各池的 eligibleStaffIDs）
- 刷新費與候選張數的 per-pool 配置（`StaffRefreshCostTable` schema）
- 自動刷新 tick 與離線補刷邏輯（接 FT-02 Time System）
- ~~保留候選的資料結構~~ ✅ 已於 §3.2.3 定義 `CandidateCard`
- ~~DataManager 驗證規則（`staffWeights.Count == eligibleStaffIDs.Count`、空層識別、全 0 權重處理）~~ ✅ 已於 §3.2.2 定義
- flavor item 與 `isFiller` 平庸職員的最終切分（原 Q3B-11 ~ Q3B-14）
- 面試費資金不足行為（原 Q3B-15，預設 A = UI 擋）
- ~~保底計數單位（每張 +1 vs 每次刷新 +1，原 Q3B-7）~~ ✅ 每張 +1（§1 M3-2=B 推論）
- ~~保底 counter 存放位置（`StaffPlayerState`，原 Q3B-10）~~ ✅ `StaffPlayerState.pityCounter`（§3.2.3）

#### A.2.3 Batch B v2 寫入順序

1. **M1~M5 宏觀對齊完成** → 回填 §1（A 段面試系統 + 末尾資料表）+ §3.2（若需新增 `StaffGachaPoolTable` schema 段落於本節）
2. 進入 A.2.2 細節題 → 逐題對齊後寫入 §3.3 面試流程
3. §3.3 寫入後 → §3.4 保底機制
4. §3.4 寫入後 → §3.5 垃圾物品填充
5. Batch B v2 結案（§1 / §3.2 / §3.3~§3.5 全部完成） → 進 Batch C（§3.7 ~ §3.13）

<!-- ARCHIVED: 以下舊 Batch B 第一輪問題清單保留於此僅供追溯，Batch B v2 不再參考 -->

#### A.2.OLD 舊 Batch B 問題清單（ARCHIVED — 僅追溯用，不再參考）

<details>
<summary>展開查看 Q3B-1 ~ Q3B-15 歷史問題</summary>

##### §3.3 面試流程（gacha 演算法）

**Q3B-1：面試 UX 模型**

- **A.** 刷新 → 展示 N 張 → 玩家**逐張決定**錄取或放棄（每張獨立操作）
- **B.** 刷新 → **自動全部錄取** N 張（無選擇，全入名冊）
- **C.** 刷新 → 展示 N 張 → 玩家**從 N 張選 1 張**錄取，其他丟棄
- **D.** 其他

**Q3B-2：候選清單持續時間與保留**

- 下次刷新是否自動覆蓋前次未錄取的候選？
- 玩家是否能「保留」候選到下次？
- 候選是否有時限（例如 1h 自動消失）？

**Q3B-3：候選張數 per 公會等級**

候選張數隨公會等級遞增（DevLog 決定）。建議值：

| Lv | 候選張數（建議初值） |
|----|----|
| 1 | 2 |
| 2 | 3 |
| 3 | 4 |
| 4 | 5 |
| 5 | 6 |

是否採此初值？（將進入 §7 調參）

**Q3B-4：從稀有度 roll 到具體職員的演算法**

給定本次 roll 出的稀有度 R：

- 候選職員 = `StaffGachaPoolTable[1, currentGuildLevel].eligibleStaffIDs` 中 `StaffTable.rarity == R` 的職員
- 在候選中**均勻隨機**選 1 個？還是用權重（例如 `isFiller=true` 的職員權重較低 / 較高）？
- 選項：
  - **A.** 均勻隨機
  - **B.** Filler vs 非 Filler 兩段權重（Filler 在 1★ 層中佔高比例，5★ 層中幾乎 0）
  - **C.** 每個職員在 `StaffGachaPoolTable` 另有個別權重欄位

**Q3B-5：稀有度層為空時的 fallback**

若 `eligibleStaffIDs` 在該等級下沒有任何 `rarity == R` 的職員（例如 Lv1 時 5★ 層為空）：

- **A.** 降級到次低有內容的稀有度（Lv1 抽到 5★ 但無 5★ → 給 4★，無 4★ → 3★...）
- **B.** DataManager 載入時驗證禁止空層，一出錯直接拋
- **C.** 補 filler（給 flavor item 或平庸職員頂上）

##### §3.4 保底機制

**Q3B-6：保底觸發的精細語意**

DevLog 「每 10 抽保底 5★」的具體規則：

- **A.** **第 10 抽必中**（counter 0→9 皆可任意 roll；counter == 10 時強制 rarity = 5，之後 reset）
- **B.** **連 10 抽無 5★ 則下次保底**（counter 每抽 +1 且若 roll 出 5★ reset 為 0；counter > 10 時強制 rarity = 5）
- **C.** 其他

**Q3B-7：Counter 計數單位**

- **A.** **每張候選 +1**（Lv5 每次刷新展示 6 張，counter +6）
- **B.** **每次刷新動作 +1**（不論候選張數，一次刷新 +1；Lv5 = 6 張同時 roll 但 counter 只 +1）

**Q3B-8：Counter reset 條件**

- **A.** **僅保底觸發時 reset**（roll 出 5★ 也 reset，符合 B 語意；或僅保底觸發 reset，符合 A 語意）
- **B.** 公會升級時 reset（DevLog「跨卡池不跨等級」的詮釋之一）
- **C.** 公會升級時**不** reset（counter 跨等級累積）
- **D.** A + B 組合 / A + C 組合

**Q3B-9：保底範圍**

- **A.** 僅 5★ 保底（每 10 抽保底 5★）
- **B.** 4★ 和 5★ 分別有不同保底周期（例如每 5 抽保 4★、每 10 抽保 5★）
- DevLog 僅提 5★ → 預設 A

**Q3B-10：Counter 存放位置**

- 存在哪裡？（FT-08 的 player-level state，不屬於 `StaffInstance`）
- 是否需要專門的 `StaffPlayerState` 物件（含 counter + `nextInstanceID` + 其他 player 層欄位）？

##### §3.5 垃圾物品填充

**Q3B-11：`TrashItemTable` schema 與 `StaffTable.isFiller` 的切分**

承 Q3A-8 決策：平庸職員放 `StaffTable`（`isFiller=true`）；flavor item 是否仍需要獨立的 `TrashItemTable`？

- **A.** `TrashItemTable` 只放 **flavor item**（無 staffID、無 StaffInstance、純文字訊息）
- **B.** 完全廢除 `TrashItemTable`，flavor item 也放 `StaffTable`（`isFiller=true` 且 `effectIDs` / `salary` / `slotBuildingIDs` 全空）
- DevLog 提到 `TrashItemTable` 存在，預設 A（切分清楚）

**Q3B-12：Flavor item 被抽到時的行為**

若選 Q3B-11 A（`TrashItemTable` 獨立）：

- 抽到 flavor item 後：
  - **A.** UI 顯示訊息「你抽到：咖啡杯！」→ 自動丟棄，不占名冊上限
  - **B.** 玩家可選「收藏」到獨立倉庫（Jam 版不實作，但 Post-Jam 的圖鑑會用）
  - **C.** 直接丟棄不顯示（flavor item 視為「空抽」）

**Q3B-13：Flavor item 如何進入 roll**

- **A.** flavor item 獨立 roll：先 roll 稀有度 → 若稀有度 = 1 → 再 roll「flavor vs 正式 1★ 職員」比例
- **B.** flavor item 視為 rarity=1 的特殊項，與其他 1★ 職員同池 roll（`StaffGachaPoolTable` 需加 flavor 項）
- **C.** flavor item 另有觸發條件（例如 counter 相關、或低稀有度加權）

**Q3B-14：Flavor item 的填充比例**

- Jam 版每次 roll 到 1★（機率 40%）時，其中多少比例是 flavor？
- 建議值：1★ 層內 flavor 占 30%（即全體約 12% 抽到 flavor item）
- 是否採用？

##### 通用

**Q3B-15：面試費資金不足時的行為**

- 金幣 < `StaffRefreshCostTable[Lv]` 時：
  - **A.** UI 擋（按鈕 disabled），FT-08 API 回 `GOLD_INSUFFICIENT`
  - **B.** 允許玩家進入負債（類似 FT-05 `AddGoldAllowBankruptcy`）
  - **C.** 允許但觸發破產警告
- 依 FT-07 建築升級的先例（§5.2）：**主動消費不允許負債** → 預設 A

</details>

---

### A.3 Batch C 結案紀錄（§3.7 ~ §3.13）

**狀態**：✅ 全部完成（2026-04-25 本 session 一次性寫入）

| 子節 | 主要產出 |
|------|---------|
| §3.7 Slot 指派流程 | `TryAssignStaff` / `TryUnassignStaff` 完整 pseudocode、capacity 規則、多 slot 候選 UI 流程、`BUILDING_SWITCH_COOLDOWN_SECONDS=7200` 冷卻、idempotency 條件 |
| §3.8 三態狀態機 | 10 列 transition table（Working / Reallocating / OnLeave 三態互斥）、6 個對外 API 契約、cooldown 語意、persistence validation 規則 |
| §3.9 自動轉休假判定 | `shouldAutoLeave` 條件（`REALLOCATING_AUTO_LEAVE_SECONDS=43200`）、`CheckReallocatingAutoLeave` 三 checkpoint（Boot / OnHourTick / API 入口）、邊緣案例 |
| §3.10 解雇流程 | `TryFireStaff` 完整流程、不可逆性、event-vs-roster.Remove 順序決策（事件先發 → 訂閱者讀取 staff 資料 → roster.Remove）、失敗情境 |
| §3.11 薪水發薪流程 | `AssemblePerStaffSalary` / `OnSalaryTick` / `ProcessOfflineSalary`、`lastSalaryTimestamp` 生命週期、`OnStaffSystemUnlocked` callback 重設邏輯（防止解鎖回溯薪水爆炸）|
| §3.12 降級行為契約 | 3 表覆蓋 17 APIs / 5 events / 6 internal flows 的 no-op 細則與 AC mapping |
| §3.13 事件發布契約 | 5 事件 payload schema、10 列發布順序表、subscriber 責任表、error isolation、payload immutability |

實際撰寫量：§3.7 ~ §3.13 一次性產出 ~990 行，整 GDD 由 1668 行擴張至 2658 行。

---

### A.4 尚待處理的副作用 / 跨系統修正清單

> FT-08 整體通過（§1~§8 全部完成）後一次處理，避免中途重工。

**已完成（2026-04-25 本 session 寫入）：**

- [x] ✅ **FT-01 §6.1 / §4.1 / §5.3 / §8**：補上游 FT-08 `GetRecruitRefreshReductionSec()`；§4.1 `CheckAutoRefresh` 改寫納入減量；§5.3 補 2 條 edge case（FT-08 解鎖 / 極端減量 clamp 至 MIN）；§8 補 AC-AR-18 / AC-AR-19（2026-04-25 本 session）
- [x] ✅ **FT-03 §5 / §6**：FT-03 §6 已存在 `GetStaffWillingnessBonus` 並於 §3 / §6.1 / §6.5 / §6.6 多處引用 FT-08；§3.6 聚合上限不影響現有 AC-ND3-04 行為（2026-04-25 grep 驗證）
- [x] ✅ **FT-05 §6.6 反向依賴**：`[ ] FT-08 Guild Staff` 改 `[x]`，註明 FT-08 §6.3 / §3.6 / §4.2 對應條目；§3.1.3 / §3.9.1 `perStaffSalary` key 由 `staffID` 改寫為 `instanceID`，與 FT-08 §4.3.1 對齊（2026-04-25 本 session）
- [x] ✅ **FT-07 §3.2 / §5.7 / §7.1 / §7.3**：職員休息室 `BuildingTable[6]` `maxLevel` 由 `1` 擴張為 `5`，補 L2~L5 `upgradeData`（`effectValue` = 自動刷新間隔秒數、L1=86400 → L5=21600）（2026-04-25 本 session 寫入）
- [x] ~~**FT-07 §3.x / §7**：新增 `GetBuildingLevel(int buildingID) : int` API~~ —— 更正：此 API 已於 FT-07 §3.4 / §6.1 存在（line 104），無需新增，僅需確認 `buildingID=6` 的查詢契約；已於 C1 擴張中確認覆蓋
- [x] ✅ **systems-index.md FT-08 條目升級**：FT-08 列表狀態 `待設計` → `🟡 主結構完成`；依賴圖節點補完整 5 個對外 API + 上下游箭頭；資料表清單補 `StaffGachaPoolTable` / `StaffRefreshCostTable` / `StaffRarityProbTable` / `TrashItemTable` / `StaffTuning`；進度表 GDD 欄改 🟡（2026-04-25 本 session）
- [x] ✅ **FT-07 §7.1 / §7.3 / §6.2**：`BuildingTable` schema 補 `slotCount` 欄；§7.1 預設值表 6 棟全展開（委託板=3 / 公會櫃臺=2 / 預備金保險櫃=1，其他 3 棟=0）；§7.3 不可調參數新增「`slotCount` 不隨 level 變動」設計決策；§6.2 下游補登 FT-08 讀取 `BuildingTable.slotCount` 與 `GetBuildingLevel(6)` 兩條（2026-04-25 本 session）
- [x] ✅ **systems-index.md SystemConstants 表頭註記**：補 FT-08 系統常數獨立分表說明（`EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` / `PITY_THRESHOLD` / `REALLOCATING_AUTO_LEAVE_SECONDS` / `BUILDING_SWITCH_COOLDOWN_SECONDS` / `ROSTER_CAP` / `TRASH_ROLL_RATE_AT_RARITY_1` 全部歸入 `StaffTuning.csv`，理由：避免子系統爆量、與 FT-08 §7.2 對齊）（2026-04-25 本 session）

**已完成（2026-04-25 本 session 後續寫入）：**

- [x] ✅ **`/design-review` FT-08**：2026-04-25 透過 Explore agent 跑完，判定 NEEDS REVISION（1 條 MUST FIX：FT-05 vs FT-08 薪水管線 Phase 2 範疇衝突；MUST FIX #2 FT-07 簽章驗證 grep 後自動通過、無 action）；SHOULD FIX 3 條（poolEligibleByRarity 形式化、ShouldRollTrash C# 慣用法、ProcessOfflineSalary 迴圈註釋）由於 §3.11 整段 Phase 2 化後已無 runtime path，緩處理至 Phase 2 啟用前再補
- [x] ✅ **MUST FIX #1 採 B 案（薪水管線 Phase 2 化）**：使用者裁示對齊 FT-05 既有立場，FT-08 §1.C / §3.11 / §3.12（OnStaffSalaryDue 條目+Step D+OnSalaryTick）/ §3.13（payload+發布順序+訂閱者）/ §4.3 / §5.4 / §6.2 / §6.3 / §8.7（AC-30~AC-34）全部加 Phase 2 標記；§8.7 補 AC-30J~AC-34J Jam 版替代驗證（驗證「無 OnStaffSalaryDue 入列」）；標頭與 §A.1 進度表同步（2026-04-25 本 session）
- [x] ✅ **§3.7.3 / §3.8 設計矛盾消解採 A2 案（切建築直接 `Working → Working`）**：使用者裁示 Reallocating 入口語意收斂為三大路徑（錄用後 / 取消指派 / 從休假回崗），不再用於切換建築過渡；§3.7.3 step 7 ELSE 分支重寫（`TryAssignStaff` 成功永遠 `→ Working`、`reallocatingStartTimestamp = 0`、僅 `oldBuildingID > 0` 時設 `cooldownEnd ← now + 7200s`）；§3.6.1 / §3.7.1 / §3.7.6 / §3.7.7 / §3.8.1 / §3.8.2 / §3.8.4 / §3.8.5 / §3.9.1 / §3.9.5 / §3.12.3 / §3.13.2 同步更新；§3.8.2 transition table 新增 `Working → Working` row（A2 案）並標記、移除舊「Working → Reallocating（切換）」row；保留 §3.7.4 `TryUnassignStaff` 設 cooldownEnd 機制（防 `Working → Reallocating → Working` 繞過 7200s 冷卻，使用者明確意圖）；標頭：`assignedBuildingID == 0` 為 Reallocating 不變式（2026-04-25 本 session）

**待處理：**

無（Jam 範疇 GDD 鎖定，可進 Codex 工項書階段）。

**延後至對方 GDD 創建時補（FT-10 / P-02 GDD 截至 2026-04-25 尚未建立）：**

- [ ] **FT-10 §6.1（GDD 創建時）**：補 `StaffPlayerState` + `StaffInstance[]` + `CandidateCard[]` 序列化契約（schema 詳見 FT-08 §3.1 / §3.2.3 / §3.2.3 內 `CandidateCard`）；含 `reserveConsumedFlag` 永久化、`pityCounter` 跨會話累積、`lastAutoRefreshTimestamp` 全域單一計時器；驗證規則：`staffID > 0 → rolledRarity == StaffTable[staffID].rarity` / `staffID XOR trashItemID`，違規拋 `CandidateCardValidationException`
- [ ] **P-02 §6.1（GDD 創建時）面試 UI UX**：登記面試欄 UX 意圖——簡歷卡大頭照呈現、上下滑翻動特效（上滑 = 錄用 + 蓋紅印章、下滑 = 不錄用 + 蓋印章）、保留按鈕 disable 條件（`reserveConsumedFlag == true`）、手動刷新按鈕 0.5s disable 防呆；6 個面試 action API 對應（`TryRecruit` / `TryRejectCandidate` / `TryReserveCandidate` / `TryReleaseReserve` / `TryManualRefresh` / `TrySwitchPool`）
- [ ] **P-02 §6.1（GDD 創建時）委託板 UI**：補「FT-08 `IsStaffSystemUnlocked()` 閘控 + `IsSuccessRatePreviewEnabled()` 顯示成功率預估數字」；委託板顯示成功率時呼叫此 API
- [ ] **P-02 §6.1（GDD 創建時）名冊 UI**：訂閱 `OnStaffHired` / `OnStaffFired` / `OnStaffAssigned` / `OnStaffStateChanged` 更新名冊顯示
- [ ] **P-03 §6.1（GDD 創建時）**：訂閱 `OnStaffHired` / `OnStaffFired` / `OnStaffSalaryDue` 等事件，依模板渲染通知 toast

### A.5 Session 恢復 checklist

下次開啟 `/design-system 繼續FT-08` 時（Batch B v2 流程）：

1. 讀本檔 §1（含頂部 ⚠️ 待回填提示）~ §3.6 以恢復上下文
2. 讀本附錄 A.1（進度表）+ A.2.0（已對齊事實 + 新暴露事實）+ A.2.1（M1~M5 宏觀對齊問題）
3. 從 **M1** 開始逐題對齊（每輪最多 3 題，避免決策疲勞）
4. M1~M5 全部對齊後：
   - 回填 §1 A 段（面試系統）與末尾資料表（`StaffGachaPoolTable` schema）
   - 修正 §3.2（若新增 `StaffGachaPoolTable` schema 欄位表）
   - 更新 A.1 進度表（移除 §1 / §3.2 的 ⚠️ 標記）
5. 進入 A.2.2 細節題 → 逐段對齊後寫入 §3.3 → §3.4 → §3.5
6. Batch B v2 結案後清理 A.2.OLD 折疊區塊，進入 Batch C（見 A.3）
