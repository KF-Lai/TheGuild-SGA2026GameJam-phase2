# Guild Staff System 系統設計文件

_建立時間：2026-04-24_
_狀態：草稿（逐節設計中 — §1 / §2 / §3.1 / §3.2 / §3.6 完成；§3.3 ~ §3.13 / §4 ~ §8 待設計）_
_系統 ID：FT-08_

**🔖 下次接續入口**：§3 Batch B（§3.3 面試 / gacha、§3.4 保底、§3.5 垃圾物品）。請看檔末「附錄 A — WIP 進度與下次待解問題」。

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

玩家於職員休息室啟用後可透過職員面試錄取新職員入職：

- **單一常駐池**（Jam 版）：只有一個池（`poolID = 1`），不區分陣營、不切檔期
- **池成員依公會等級切換**：`StaffGachaPoolTable` 以 `(poolID, guildLevel)` 為複合主鍵，每個公會等級對應一份 `eligibleStaffIDs: CSV list`——同一池在不同等級抽得到的職員名單不同（Lv1 名單較小、Lv5 名單最完整）
- **依公會等級動態刷新費**：FT-06 `GetCurrentLevel()` 查 `StaffRefreshCostTable` 決定本次面試費用（Lv1→50g / Lv2→100g / Lv3+→300g，實際值見 §7）
- **每次面試刷新多張候選**：張數隨公會等級遞增（§7 調參）
- **稀有度權重**：1★40% / 2★25% / 3★15% / 4★15% / 5★5%，每 10 抽保底 5★（跨卡池保底不跨公會等級；Jam 版單池無此問題）
- **垃圾物品（Trash Items）填充稀有度下層**：兩類—— flavor item（純 flavor、無加成）與平庸職員（低 tag 權重、僅壓稀有度分母）
- **名冊上限**：Jam 版固定 99（無人資辦公桌建築）

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

作為 FT-05 `OnStaffSalaryDue` 事件發布者：

- **每日 UTC 06:00 發薪**（以玩家電腦時區判斷跨日）
- **離線補發 N 次**：若離線跨多個發薪時間點，逐次補發（上限由 `SystemConstants.OFFLINE_MAX_SECONDS` 間接限制）
- **休假態不支薪**：發薪時跳過該職員，不計入 `perStaffSalary`
- **時區對不上則跳過不扣款**（系統時鐘異常、UTC/local 轉換異常）
- FT-08 負責計算每日薪水總額、組裝 `perStaffSalary: Dict<int,int>`、發布事件；FT-05 負責實際扣款（`AddGoldAllowBankruptcy`）

### 核心職責

1. 管理職員實例（`StaffInstance`）的生命週期（招募、指派、切換、解雇、狀態轉移）
2. 提供 tag 聚合後的三個固定名稱加成 API 給 FT-03 / FT-05
3. 驅動每日薪水發薪管線，發布 `OnStaffSalaryDue` 事件
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
| `StaffGachaPoolTable` | gacha 池配置（PK = `(poolID, guildLevel)`；欄位 `eligibleStaffIDs: CSV list` 依公會等級切換池成員） |
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
  - 再分配中仍保留 `assignedBuildingID` 值（代表**目標** slot，§3.8 詳述）
  - 進入休假時清為 `0`
  - 工作中即代表**目前佔用**的 slot
- **保底 counter** 不放 `StaffInstance`（屬於 player-level 狀態），位置與細節見 §3.4。

### 3.2 StaffTable Schema

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

**關鍵語意**：Slot effects 在「再分配」狀態**不**提供加成——再分配是職員正在切換建築的過渡態，不再佔用原 slot 也尚未落位新 slot；Passive effects 在再分配中仍提供（職員仍在公會名冊、提供持續影響力）。

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

---

## 4. 公式（Formulas）

[To be designed]

---

## 5. 邊緣案例（Edge Cases）

[To be designed]

---

## 6. 依賴關係（Dependencies）

[To be designed]

---

## 7. 可調參數（Tuning Knobs）

[To be designed]

---

## 8. 驗收標準（Acceptance Criteria）

[To be designed]

---

## 附錄 A — WIP 進度與下次待解問題

> 此附錄為 `/design-system` 互動設計進度追蹤，GDD 完成時可移除。

### A.1 已完成章節

| 章節 | 狀態 | 關鍵決策來源 |
|------|------|-------------|
| §1 Overview | ✅ | Q1-1(B) / Q1-2(C) / Q1-3(A) / Q1-4(A) / Q1-5(A) |
| §2 Player Fantasy | ✅ | Q2-1（E1 + E4 Jam 聚焦，E2/E5/E7 延後至 Post-Jam 人事系統）/ Q2-2(A) / Q2-3(A+C) / Q2-4(A) |
| §3.1 StaffInstance | ✅ | Q3A-1(A) / Q3A-2(A 備註 Jam 暫代) / Q3A-3(A) |
| §3.2 StaffTable Schema | ✅ | Q3A-4(A) / Q3A-5(B) / Q3A-6(B 效果 id+數值 欄位) / Q3A-7(A) / Q3A-8(C 僅用 isFiller) |
| §3.6 Effect Aggregation | ✅ | Q3A-9(A) / Q3A-10(B 並細分 Passive vs Slot) / Q3A-11(B) / Q3A-12(A) |
| **Batch A 附加決策** | ✅ | 新增 `RecruitRefreshOnCounter` effect + `SuccessRatePreview` UI flag（Q 組後確認，候選 1 + 候選 2Q）|
| 跨系統副作用 | 📋 待整批處理 | game-concept.md 已補 Post-Jam 人事系統構想；FT-01 §6.1 / P-02 §6.1 反向依賴延後 |

### A.2 下次接續點：§3 Batch B — 面試 / Gacha / 保底 / 垃圾物品

#### A.2.1 §3.3 面試流程（gacha 演算法）

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

#### A.2.2 §3.4 保底機制

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

#### A.2.3 §3.5 垃圾物品填充

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

#### A.2.4 通用

**Q3B-15：面試費資金不足時的行為**

- 金幣 < `StaffRefreshCostTable[Lv]` 時：
  - **A.** UI 擋（按鈕 disabled），FT-08 API 回 `GOLD_INSUFFICIENT`
  - **B.** 允許玩家進入負債（類似 FT-05 `AddGoldAllowBankruptcy`）
  - **C.** 允許但觸發破產警告
- 依 FT-07 建築升級的先例（§5.2）：**主動消費不允許負債** → 預設 A

---

### A.3 Batch B 完成後接續：Batch C 範圍預覽

Batch C 涵蓋 §3.7 ~ §3.13：

- §3.7 Slot 指派流程（assign / switch / 冷卻 / slot capacity）
- §3.8 三態狀態機（Working ↔ Reallocating ↔ OnLeave）
- §3.9 自動轉休假判定（再分配 > 12h）
- §3.10 解雇流程（資遣費扣款 + tag 移出聚合時序）
- §3.11 薪水發薪流程（UTC 06:00 + 離線補發 + 休假跳過 + 時區異常）
- §3.12 降級行為契約（API 層面的 no-op 細則）
- §3.13 事件發布契約（`OnStaffHired` / `OnStaffAssigned` / `OnStaffFired` / `OnStaffSalaryDue` / `OnStaffStateChanged`）

預估 Batch C 約 18~22 個問題。

---

### A.4 尚待處理的副作用 / 跨系統修正清單

> FT-08 整體通過（§1~§8 全部完成）後一次處理，避免中途重工。

- [ ] **FT-01 §6.1**：補上游「FT-08 `GetRecruitRefreshReductionSec()`」；刷新間隔公式改寫 `actualInterval = FT07.GetRecruitRefreshInterval() - FT08.GetRecruitRefreshReductionSec()`
- [ ] **FT-03 §5 / §6**：若 §3.6 聚合上限影響現有 AC-ND3-04 行為，補反向一致性檢查
- [ ] **FT-05 §6 / §7**：補登記 FT-08 為 `OnStaffSalaryDue` 發布者（現已預留契約但 §6 未列）；確認 `GetAccountantPenaltyBonus` 的 slot 條件（保險櫃）與 FT-08 §3.6.2 實作對齊
- [ ] **FT-07 §3.7 / §7**：`BuildingTable.slotCount` 欄位（DevLog 決定「全 6 棟加 slotCount」）目前尚未加入 FT-07 §7 schema，需補
- [ ] **P-02 §6.1**（若已建）：補「FT-08 `IsStaffSystemUnlocked()` + `IsSuccessRatePreviewEnabled()`」
- [ ] **FT-10 §6.1**（若已建）：補 `StaffInstance` + `StaffPlayerState` 序列化
- [ ] **systems-index.md `SystemConstants` 欄位**：補 `EFFECT_MAX_*` 系列常數
- [ ] **systems-index.md 資料表清單**：補 `uiFlagIDs` / `uiFlagBuildingIDs` 欄位於 `StaffTable` 描述
- [ ] **`/design-review` FT-08**：Batch C 完成後執行

### A.5 Session 恢復 checklist

下次開啟 `/design-system 繼續FT-08` 時：

1. 讀本檔 §1 ~ §3.6 以恢復上下文
2. 讀本附錄 A 了解進度與待解問題
3. 從 A.2.1 Q3B-1 開始逐題推進
4. 完成 Batch B 後寫入 §3.3 / §3.4 / §3.5、更新 A.1 表、清理 A.2 已答題
5. 進入 Batch C 前先重新檢視 A.3 範圍
