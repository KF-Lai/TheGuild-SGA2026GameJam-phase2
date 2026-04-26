# Staff System 系統設計文件

_建立時間：2026-04-26_
_狀態：草稿（從 FT-08 拆出，待 design-review）_
_最後更新：2026-04-26_
_系統 ID：FT-12_

**🔖 拆分來源**：本系統由 FT-08（原職員系統）於 2026-04-26 拆分而來。FT-08 改名為「面試系統（Gacha）」聚焦面試 gacha 機制；FT-12 接管職員實例管理、effect 聚合、薪水管線。兩系統透過 `FT-12.HireStaff(candidateCard)` 同步 API 交棒，FT-08 不持有 StaffInstance、FT-12 不感知 gacha 細節。

---

## 設計來源（Design Inputs）

本 GDD 基於以下決策來源撰寫：

- 拆分自 `[FT-08]` 原職員系統 GDD（2026-04-26 拆分批次）
- 依賴 GDD 約束：
  - `[FT-08] gacha-system.md` — 面試 gacha 系統，透過 `OnCandidateHired` / `HireStaff` 交棒候選卡
  - `[FT-07] guild-building-system.md` — `IsStaffSystemUnlocked()` 整體啟停閘（職員休息室 L1）；建築 capacity / slot 數
  - `[FT-06] guild-core.md` — `GetCurrentLevel()` 公會等級（StaffTable.minGuildLevel 過濾）
  - `[F-02] time-system.md` — `OnDailyReset` / `OnHourTick` 驅動薪水管線、自動轉休假檢查
  - `[F-03] resource-management.md` — `AddGoldAllowBankruptcy` 薪水扣款（透過 FT-05 中介）
  - `[FT-05] guild-gold-flow.md` — `OnStaffSalaryDue` 訂閱方（**Phase 2**，Jam 版不發布）
  - `[FT-03] npc-decision-system.md` — `GetStaffWillingnessBonus()` 消費方（委託官 effect）
  - `[F-01] data-manager.md` — `Get<StaffData>` / `GetAll<StaffData>`、`StaffTuning.csv` 系統常數
  - `[FT-09] faction-story-system.md` — `StaffTable.factionID` 欄位 Post-Jam 預留（Jam 版無 runtime 依賴）
- 全局規則：`feedback_time_units`（時間單位僅秒/小時）、`feedback_data_driven`（一切由 CSV 驅動，零硬編碼）

---

## 1. 概要（Overview）

**FT-12 Staff System（職員系統）是公會職員的玩法系統**：管理已錄用職員的名冊狀態、effect 聚合與薪水管線。本系統不執行面試 gacha（屬 FT-08），而是透過 `FT-12.HireStaff(candidateCard)` 同步 API 接收 FT-08 錄用候選卡，建立 `StaffInstance` 入名冊。系統涵蓋三大職責：

### A. 職員名冊管理（Staff Roster）

接收 FT-08 錄用候選 → 建立 StaffInstance（§3.1） → 名冊操作：
- **三態狀態機**（§3.6）：`Working` / `Reallocating` / `OnLeave` 三態互斥
- **Slot 指派流程**（§3.5）：`TryAssignStaff(instanceID, buildingID)` / `TryUnassignStaff(instanceID)`
- **再分配自動轉休假**（§3.7）：Reallocating 超過 12h 自動轉 OnLeave
- **解雇流程**（§3.8）：`TryFireStaff(instanceID)`，扣資遣費、roster 移除（不可逆）
- **休假切換**（§3.6）：`TryGoOnLeave` / `TryReturnFromLeave`

### B. 效果聚合 API（Effect Aggregation）

聚合 `StaffTable.effectIDs` / `effectValues` 形成對外加成查詢 API（§3.4）：
- `GetStaffWillingnessBonus() : float`（FT-03 消費）
- `GetAccountantCommissionBonus() : float` / `GetAccountantPenaltyBonus() : float`（FT-05 消費，Phase 2）
- `GetRecruitRefreshReductionSec() : int`（FT-08 消費，影響面試自動刷新間隔）
- `IsSuccessRatePreviewEnabled() : bool`（FT-02 / P-02 消費）
- 聚合上限見 §4.1

### C. 薪水管線（Salary Pipeline，Phase 2）

訂閱 `F-02.OnDailyReset` 發布 `OnStaffSalaryDue` 事件供 FT-05 統一接管金流：
- 線上發薪 / 離線補發（§3.9.4）
- `lastSalaryTimestamp` 持久化（§3.9.5）
- OnLeave 職員跳過薪水（§3.9.6）

> **Phase 2 註記**：薪水管線完整設計保留於 §3.9 / §4.2 / §5.4 / §8.7，**Jam 版不實作**——對齊 FT-05 既定 Phase 2 立場（FT-05 §3.5 / §6.7）。

### 啟停閘

`FT-07.IsStaffSystemUnlocked()`（職員休息室 L1）為整體啟停閘 — 未解鎖時靜默降級（§3.10）：所有 `Try*` API 回傳 `STAFF_SYSTEM_LOCKED`、`Get*` API 回傳 `0`、`OnStaff*` 事件不發布。UI 層的隱藏 / 禁用由 P-02 訂閱 FT-07 自行處理，FT-12 不規範 UI 行為。

### Jam 範疇

- 職員實例管理（§3.1 ~ §3.8）：全部 Jam 內實作
- effect 聚合（§3.4）：全部 Jam 內實作
- 薪水管線（§3.9）：**Phase 2，Jam 版不實作**（事件未發布，FT-05 訂閱端 no-op）
- ISaveable 持久化：StaffInstance[] / lastSalaryTimestamp（§6.7）

### 不在 Jam 範疇

- 薪水扣款發布與執行（Phase 2，由 FT-05 統一管金流）
- 職員圖鑑 / 圖像介紹（Post-Jam，需新增 D-XX content 資料層）
- FT-09 陣營路線對職員影響（StaffTable.factionID 預留欄位 Post-Jam 啟用）
- 職員特殊技能 / 升級 / 養成（Post-Jam）
- 職員相互關係（Post-Jam，如「同陣營職員加成」）

### FT-08 / FT-12 邊界

**錄用流程交棒**：
1. 玩家在 FT-08 面試介面點「錄用」候選卡
2. FT-08 `TryRecruit` 內部呼叫 `FT-12.HireStaff(CandidateCard candidate) → HireResult`
3. FT-12 驗證 → 建立 `StaffInstance`（new instanceID）→ 加入 roster → 發布 `OnStaffHired` → 回傳 `HireResult.OK + instanceID`
4. FT-08 收到 OK → 從 `currentCandidates` 移除該候選

**單向耦合**：FT-08 → FT-12 透過同步 API；FT-12 不感知 gacha 細節（不知道「保底」「動態歸一化」「保留」）。

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-12 在 Jam 版聚焦兩個核心情緒，對齊 game-concept「敘事劇情」與「有重量的決策」：

**職員是會記得名字的人，不是冷冰冰的數值**

- 玩家錄用一位 5★ 會計，看到她的名字「莉莉雅」、3 個 effect、薪水 500/day。她加進名冊就是個有「臉」的角色
- 把她指派到預備金保險櫃 → 看到 `+2% 傭金` 加成立即生效
- 12 小時後再看名冊，她仍在保險櫃前；玩家覺得「她在工作，這個公會在運轉」

**解雇是真實的決定**

- 1★ 跑腿小弟 effect 0、薪水 50/day 卻佔一個 slot；公會要錢時玩家點「解雇」
- 跳出確認框：「將支付 100 gold 資遣費，此操作不可逆」
- 玩家確認後她從名冊永遠消失。下次看到 1★ 會回想「我曾經為了 50 gold 解雇過一個人」
- 對應 game-concept「有重量的決策」支柱：解雇是 hire & fire 的另一面，玩家承擔的是「辭退人的代價」

### 玩家幻想敘事

> 「我招了三位職員：櫃台艾莉、會計莉莉雅、跑腿小弟肯特。艾莉的 +0.05 willingness 讓冒險者更願意接刁鑽委託；莉莉雅指派到保險櫃後傭金 +2%、罰款 -2%，幾天下來多賺幾千金幣；肯特沒什麼用、薪水卻照付。我猶豫了三天還是按下他的解雇——不是壞人，但這個公會養不起每張臉。下次招到肯特同款的 1★ 跑腿，我大概還是會錄用，再次走過這個流程。」

### 設計原則（FT-12 如何強化幻想）

1. **每個 StaffInstance 是個人**：instanceID 永久唯一、有 `hiredTimestamp` 記錄「他什麼時候加入這個公會」、解雇後 instance 從名冊永久消失（不可逆）
2. **effect 是即時的、可見的**：聚合 API 即時遍歷 roster（§3.4.2 不快取），玩家指派職員的瞬間 effect 變動可被 UI 立即感知
3. **三態語義清楚**：Working = 在崗位上、Reallocating = 換崗中、OnLeave = 暫離；每個轉移都有 cooldown 或時間限制（§3.6）
4. **薪水是 Phase 2 的承諾不是 Jam 包袱**：保留完整設計（§3.9 / §4.2）但 Jam 不實作，Codex 工項書清楚標記「Phase 2 跳過」

### 關鍵情緒節點（Emotional Beats）

| 時機 | 觸發 | 期望情緒 |
|---|---|---|
| 第一次錄用職員 | FT-08 → FT-12.HireStaff → `OnStaffHired` 發布 | 收穫感、名冊擴張 |
| 第一次指派 effect 觸發 | TryAssignStaff 成功 → 下次查詢 effect API 看到加成 | 即時生效、價值感 |
| 第一次自動轉休假 | 12h Reallocating 超時 → state = OnLeave | 系統自然後果 |
| 解雇確認 | TryFireStaff 成功 → roster 永久移除 | 沉重、不可逆 |
| 公會壯大時看名冊 | 5+ 職員各司其職、effect 聚合可觀 | 經營成就感 |

---

## 3. 詳細規則（Detailed Rules）

> 本章採 **§3.1 → §3.11** 十一子節結構：StaffInstance schema → StaffTable schema → 入職流程 → effect 聚合 → Slot 指派 → 三態狀態機 → 自動轉休假 → 解雇 → 薪水管線（Phase 2）→ 系統降級 → 事件契約。

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
- **狀態 vs 冷卻正交**：`currentState` 三態互斥（§3.4 詳述），但 `buildingSwitchCooldownEndTimestamp` 可在任一態持續倒數（即使進入休假，冷卻仍照跑）。
- **`assignedBuildingID` 跨狀態語意**：
  - 再分配中 `assignedBuildingID == 0`（A2 案：Reallocating 表示「未指派任一建築、等待玩家選 buildingID」，不再代表目標 slot，§3.4 詳述）
  - 進入休假時清為 `0`
  - 工作中即代表**目前佔用**的 slot（無 slot 能力職員例外，可為 `0`，§3.4.2）
- **保底 counter** 不放 `StaffInstance`（屬於 player-level 狀態），位置與細節見 §3.4。


---

### 3.2 StaffTable Schema

**檔案位置**：`Assets/Resources/Data/Tables/StaffTable.csv`

**Schema**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `staffID` | int (PK) | 唯一識別碼 |
| `name` | string | 職員顯示名稱 |
| `rarity` | int | 稀有度 1~5 |
| `salary` | int | 每日薪水（金幣；發薪時扣款，§3.5） |
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
> `uiFlagIDs` / `uiFlagBuildingIDs` 為本節新增欄位（2026-04-24），解決「純 UI 功能型 slot 加成」無法與 float-typed effect 統一表達的問題——走獨立聚合路徑（§3.4.6），不污染 `effectIDs` / `effectValues` 的數值型別。

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
- 新增 effect 類型時需同步擴充：此白名單 + C# enum + §3.4 聚合演算法 + 若需新 API 還要同步下游 GDD

**`StaffUIFlag` enum（白名單）**：

| uiFlagID | 對應 API | 生效條件 | 消費者 |
|---|---|---|---|
| `SuccessRatePreview` | `IsSuccessRatePreviewEnabled` | 職員狀態 = Working **AND** `assignedBuildingID == uiFlagBuildingIDs[i]`（通常為 `1` 委託板） | P-02 委託審核 UI（顯示成功率預估數字） |

- UI flag 為純布林語意，**不提供數值加成**；任一職員符合條件即 `true`（OR 聚合，非 SUM）
- 未列於白名單的 uiFlagID 於 DataManager 載入時拋錯
- 新增 UI flag 時同步擴充：此白名單 + C# enum + §3.4.6 聚合演算法 + 消費者 GDD（例如 P-02）

**`slotBuildingIDs` 語意**：

- 多選 = **合格 slot 候選清單**；該職員可**二擇一**指派到清單中任一建築（同時只能指派一個 `assignedBuildingID`）
- 空（所有位置為 `0`）= 該職員無 slot 指派能力，僅提供 passive effects
- 實際指派到哪個 slot 由玩家於 §3.5 slot 指派流程決定；若職員帶 slot-type effect（如 `AccountantPenaltyOnVault`），**僅**在指派到對應建築 ID 時才觸發該效果

**資料表範例**（說明用，§7 調參）：

| staffID | name | rarity | salary | severancePay | isFiller | factionID | effectIDs | effectValues | slotBuildingIDs | uiFlagIDs | uiFlagBuildingIDs |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 101 | 克勞德·會計師 | 3 | 40 | 120 | false | 0 | `AccountantCommission,AccountantPenaltyOnVault` | `0.02,-0.02` | `5` | *(empty)* | *(empty)* |
| 102 | 艾蓮·委託官 | 2 | 30 | 80 | false | 0 | `Willingness` | `0.05` | `1` | `SuccessRatePreview` | `1` |
| 103 | 蘿絲·櫃台小姐 | 2 | 30 | 80 | false | 0 | `RecruitRefreshOnCounter` | `7200` | `4` | *(empty)* | *(empty)* |
| 104 | 無名小職員 | 1 | 10 | 10 | true | 0 | `Willingness` | `0.01` | `0` | *(empty)* | *(empty)* |
| 105 | 咖啡杯 | 1 | 0 | 0 | true | 0 | *(empty)* | *(empty)* | `0` | *(empty)* | *(empty)* |


---

### 3.3 入職流程（Hire Flow）

#### 3.3.1 HireStaff 公開 API

FT-12 對 FT-08 提供唯一的「錄用候選卡」入口：

```csharp
public enum HireStaffResult
{
    OK,
    STAFF_SYSTEM_LOCKED,    // FT-07.IsStaffSystemUnlocked() == false
    INVALID_STAFF_ID,        // candidate.staffID 不存在於 StaffTable
    ROSTER_FULL,             // 名冊已達 capacity 上限（Post-Jam 預留；Jam 版不限）
    DUPLICATE_INSTANCE        // 極罕見:nextInstanceID 衝突（Jam 版以正整數遞增不可能發生）
}

public HireResult HireStaff(CandidateCard candidate);

public struct HireResult
{
    public HireStaffResult result;
    public int             instanceID;    // OK 時為新建 instance 的 ID;失敗時為 0
}
```

**呼叫方**：FT-08（在玩家點「錄用」按鈕時於 `TryRecruit` 內部呼叫）；FT-12 不對其他系統暴露此 API。

#### 3.3.2 內部流程

```
HireStaff(CandidateCard candidate) → HireResult:
    Step 1: 啟用檢查
        IF NOT FT07.IsStaffSystemUnlocked():
            return { result: STAFF_SYSTEM_LOCKED, instanceID: 0 }

    Step 2: 驗證 candidate.staffID
        IF candidate.staffID <= 0 OR DataManager.Get<StaffData>(candidate.staffID) == null:
            Debug.LogError("HireStaff: invalid staffID={id}")
            return { result: INVALID_STAFF_ID, instanceID: 0 }

    Step 3: 建立 StaffInstance（§3.1）
        instance = new StaffInstance {
            instanceID:                 _nextInstanceID++,    // 全域單調遞增
            staffID:                    candidate.staffID,
            state:                      Reallocating,           // 入職即可指派(§3.6 預設)
            assignedBuildingID:         0,
            cooldownEndTimestamp:       0,
            reallocatingStartTimestamp: now,                    // §3.7 自動轉休假基準
            hiredTimestamp:             now
        }

    Step 4: 加入 roster
        _roster.Add(instance.instanceID, instance)

    Step 5: 發布事件
        EventBus.Publish(new OnStaffHired(
            instanceID:     instance.instanceID,
            staffID:        instance.staffID,
            hiredTimestamp: instance.hiredTimestamp
        ))

    return { result: OK, instanceID: instance.instanceID }
```

#### 3.3.3 設計原則

- **入職即 Reallocating**：對齊 §3.6 三態定義—— Working 必須有 `assignedBuildingID != 0`，新建 instance 沒指派故為 Reallocating;玩家立即可呼叫 `TryAssignStaff` 指派至建築進入 Working
- **`reallocatingStartTimestamp = now`**：§3.7 自動轉休假倒數從入職瞬間開始;若玩家錄用後 12h 內未指派,職員自動轉 OnLeave
- **不扣費**：招聘費 / 刷新費由 FT-08 在 TryRecruit 階段扣（§3.6.6 / FT-08 §3.3.4）;FT-12 入職階段不再扣費
- **冪等性**：FT-08 端因 `currentCandidates` 索引唯一,不會重複呼叫;FT-12 端 `_nextInstanceID` 單調遞增保證 instanceID 唯一

#### 3.3.4 失敗路徑

| 失敗類型 | FT-12 行為 | FT-08 端動作 |
|---|---|---|
| `STAFF_SYSTEM_LOCKED` | 不變動 roster、不發事件 | TryRecruit 早退、不扣費、不移除候選 |
| `INVALID_STAFF_ID` | LogError、不變動 roster | TryRecruit 視為失敗、不扣費、保留候選（極罕見路徑） |
| `ROSTER_FULL`（Post-Jam） | 不變動 roster | TryRecruit 早退、UI 提示「名冊已滿」 |

> 錄用失敗時 FT-08 不應消耗 candidate（保留供下次嘗試或玩家保留）；對齊 FT-08 §3.3 設計原則「失敗不扣費」。

---

### 3.4 效果聚合加成計算（Effect Aggregation）

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

#### 3.4.1 狀態生效規則

依效果類型決定哪些職員狀態下該效果生效：

| Effect / Flag 類型 | `Working` | `Reallocating` | `OnLeave` |
|---|---|---|---|
| **Passive**（`Willingness`, `AccountantCommission`） | ✅ | ✅ | ❌ |
| **Slot**（`AccountantPenaltyOnVault`, `RecruitRefreshOnCounter`） | ✅（且 `assignedBuildingID` 符合） | ❌ | ❌ |
| **UI Flag**（`SuccessRatePreview`） | ✅（且 `assignedBuildingID == uiFlagBuildingIDs[i]`） | ❌ | ❌ |

**關鍵語意**：Slot effects 在「再分配」狀態**不**提供加成——A2 案下再分配是職員「未指派任一建築、等待玩家選 buildingID」的等待態（`assignedBuildingID == 0` 為其不變式），自然不滿足任一 slot effect 的 `assignedBuildingID == X` 條件；Passive effects 在再分配中仍提供（職員仍在公會名冊、提供持續影響力）。切建築（Working @A → Working @B）**不進 Reallocating**，slot effects 立即切換到新建築。

#### 3.4.2 聚合演算法

```
GetStaffWillingnessBonus():
    IF NOT FT07.IsStaffSystemUnlocked(): return 0f      // 系統降級（§3.10）

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

#### 3.4.3 疊加上限（Q3A-11 B）

| 常數 | 建議值 | 意義 |
|---|---|---|
| `EFFECT_MAX_WILLINGNESS_BONUS` | `+0.20` | Willingness 總加成上限 |
| `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` | `+0.10` | 會計傭金總加成上限 |
| `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` | `-0.10` | 會計賠償總加成下限（絕對值上限 0.10） |
| `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` | `14400`（4h） | 招募刷新減量總和上限（防止刷新近乎即時） |

- 實際值於 §7 調參；Jam 版以上述保守值開始
- 用 `min()` / `max()` 截斷確保疊加不破壞 FT-03 / FT-05 的 balance 假設
- 防止玩家刷滿同 effect 職員讓 rate 溢出合理區間（FT-05 `effectivePenaltyRate` 已有 `max(0, ...)` 下限但上限無防護）

#### 3.4.4 Slot Capacity 自然封頂

`AccountantPenaltyOnVault` 的 slot 條件 `assignedBuildingID == 5` 受 `BuildingTable.slotCount` 約束（Jam 版預備金保險櫃預計 `slotCount = 1`，即同時僅一位會計可啟用該加成）。詳見 §3.5 slot 指派 / capacity 規則。此約束為**實體 slot 數量**的天花板，與 §3.4.3 的**數值**上限正交。

#### 3.4.5 降級行為（系統閘）

當 `FT07.IsStaffSystemUnlocked() == false`（職員休息室未建造）：

- **加成類 API**（`GetStaffWillingnessBonus` / `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` / `GetRecruitRefreshReductionSec`）直接回 `0`，略過 roster 遍歷（早退）
- **布林類 API**（`IsSuccessRatePreviewEnabled`）直接回 `false`
- 符合 FT-03 §5 / FT-05 §3.4「FT-08 未招募 / 系統缺席 → 回 0」的降級契約

#### 3.4.6 UI Flag 聚合（OR 語意）

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

### 3.5 Slot 指派流程（Slot Assignment）

#### 3.5.1 概念與資料對應

Slot = 建築（buildingID）對職員提供的「指派位置」。每個建築的 slot capacity 由 `BuildingTable[buildingID].slotCount`（FT-07 §7.1）決定；職員的 `slotBuildingIDs` CSV list 定義該職員的合格建築清單。

| 概念 | 來源 | 範例 |
|---|---|---|
| 職員可指派的建築清單 | `StaffTable.slotBuildingIDs` | `[4, 5]`（櫃台或保險櫃二擇一）|
| 建築可容納的職員數 | `FT07.BuildingTable[buildingID].slotCount` | 保險櫃 = 1、委託板 = 3、櫃台 = 2（範例值）|
| 職員實際指派位置 | `StaffInstance.assignedBuildingID` | 0 = 未指派、4 = 在櫃台 |
| 切換建築冷卻 | `StaffInstance.buildingSwitchCooldownEndTimestamp` | UTC 秒；切建築 / 取消指派後 `cooldownEnd ← now + 7200s`（§3.4.4）|

#### 3.5.2 公開 API 合約

| API | 簽章 | 回傳碼 |
|---|---|---|
| `TryAssignStaff(int instanceID, int buildingID)` | `(int,int) → AssignResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `BUILDING_NOT_ELIGIBLE` / `BUILDING_FULL` / `STAFF_ON_LEAVE` / `SWITCH_COOLDOWN` |
| `TryUnassignStaff(int instanceID)` | `(int) → UnassignResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `STAFF_NOT_ASSIGNED` / `STAFF_ON_LEAVE` |

#### 3.5.3 TryAssignStaff 完整流程

```
TryAssignStaff(instanceID, buildingID):
    1. 系統閘：IsStaffSystemUnlocked() == false → STAFF_SYSTEM_LOCKED
    1.5 入口轉假掃描：CheckReallocatingAutoLeave()      // §3.5.3；先處理逾時轉假再讀 staff state
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

    8. 發布事件（順序見 §3.11.2）：
       Publish OnStaffAssigned { instanceID, oldBuildingID, newBuildingID = buildingID }
       IF oldState != staff.currentState:
           Publish OnStaffStateChanged { instanceID, oldState, newState = staff.currentState }

    9. return SUCCESS
```

> **Reallocating 設計意圖（A2 案 / 2026-04-25 使用者裁示）**：Reallocating **僅**表示「職員已不在任何建築工作、等待玩家選 buildingID 落位」（`assignedBuildingID == 0` 為其不變式）；**不**用於切建築過渡。設計目的是阻止玩家用「Working → Reallocating（unassign）→ Working（重 assign）」鏈條繞過切建築冷卻——故 §3.5.4 TryUnassignStaff 仍設 cooldownEnd = now + 7200s，玩家必須等冷卻才能重 assign 落位。
>
> **Reallocating 三大進入路徑**（皆 `assignedBuildingID = 0`）：
> 1. **錄用後等待**：剛錄用且 `slotBuildingIDs` 含 `> 0` 的 buildingID（§3.4.2）；玩家須 TryAssignStaff(X) 落位 Working
> 2. **取消指派**：Working → TryUnassignStaff → Reallocating（cooldownEnd = now + 7200s）；玩家須等冷卻 + TryAssignStaff(X) 重新落位
> 3. **從休假回崗**：OnLeave → TryReturnFromLeave → Reallocating（cooldown 沿用既有）；玩家須 TryAssignStaff(X) 落位
>
> **切建築不進 Reallocating**（A2 核心）：Working @A → TryAssignStaff(B) 直接 Working @B（assignedBuildingID 改、cooldownEnd = now + 7200s、state 不變）；slot effects 在新建築立即生效（無過渡空窗）；玩家 7200s 內無法再切建築（被 SWITCH_COOLDOWN 擋下）。
>
> **12h auto-leave 適用範圍**（§3.5）：僅對三大 Reallocating 入口的職員計時；切建築職員為 Working 不計時、永不被 auto-leave 影響。

#### 3.5.4 TryUnassignStaff 完整流程

```
TryUnassignStaff(instanceID):
    1. 系統閘 / 入口轉假掃描 / 找職員（同 §3.5.3 步驟 1 / 1.5 / 2）
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

#### 3.5.5 多 slot 候選的指派決策

當 `staff.slotBuildingIDs.Count > 1` 時（例：可選櫃台或保險櫃），玩家透過 P-02 UI 選擇實際指派位置；FT-08 不提供「自動最佳建築」邏輯（避免設計黑箱、玩家失控）。P-02 UX 細節見 A.4 對 P-02 的雙向聲明。

#### 3.5.6 Effect 聚合的指派時機影響

| 階段 | Slot effect 生效 | Passive effect 生效 |
|---|---|---|
| Reallocating（`assignedBuildingID = 0`、等待落位）| ❌ | ✅ |
| Working、`assignedBuildingID = 0`（無 slot 能力職員，§3.4.2）| ❌ | ✅ |
| Working、`assignedBuildingID` 符合 effect 條件 | ✅ | ✅ |
| Working、`assignedBuildingID` 不符合 effect 條件 | ❌ | ✅ |
| OnLeave | ❌ | ❌ |

**重新聚合時機**：FT-08 不快取聚合結果（§3.4 演算法每次調用即時遍歷 roster），因此 `OnStaffAssigned` / `OnStaffStateChanged` 事件不需要主動觸發 cache invalidation；下游（FT-01 / FT-03 / FT-05）按需 query 即可拿到最新值。

#### 3.5.7 Capacity 衝突的特殊情境

| 情境 | 行為 |
|---|---|
| 兩位都帶 `AccountantPenaltyOnVault`、保險櫃 capacity = 1 | 第二位 TryAssignStaff(5) 回 `BUILDING_FULL`；玩家須先取消第一位指派或解雇 |
| Reallocating 是否占 capacity？ | **不占**（A2 案下 Reallocating 必然 `assignedBuildingID == 0`，自然不對應任何 buildingID 的 capacity）|
| OnLeave 是否占 capacity？ | **不占**（同上）|
| 同 staff 重複 TryAssignStaff 同 buildingID | idempotent；no-op、不發事件、回 SUCCESS |

---


---

### 3.6 三態狀態機（Staff State Machine）

#### 3.6.1 狀態定義

| State | 語意 | Passive Effect | Slot Effect | 領薪水 |
|---|---|---|---|---|
| `Working` | 在指派的建築工作中 | ✅ | ✅（assignedBuildingID 符合）| ✅ |
| `Reallocating` | 未指派任一建築、等待玩家選 buildingID（A2 案；入口：錄用後 / 取消指派 / 從休假回崗）| ✅ | ❌ | ✅ |
| `OnLeave` | 休假中 | ❌ | ❌ | ❌（§4.1.1 排除）|

#### 3.6.2 狀態轉移總表

| 起始狀態 | 終止狀態 | 觸發 | 副作用 |
|---|---|---|---|
| (錄用) | `Working` | `TryRecruit` 成功且 `slotBuildingIDs` 為空或全 `0`（無 slot 能力，§3.2.1）| state = Working、assignedBuildingID = 0 |
| (錄用) | `Reallocating` | `TryRecruit` 成功且 `slotBuildingIDs` 含至少一個 `> 0` 的 buildingID | state = Reallocating、assignedBuildingID = 0、reallocatingStart = now |
| `Reallocating` | `Working` | `TryAssignStaff(buildingID)` 落位（oldBuildingID = 0）| assignedBuildingID = buildingID、reallocatingStart = 0；**cooldown 不重置**（首次落位無冷卻 / unassign 後沿用既有 cooldown）|
| `Working` | `Working` | `TryAssignStaff(otherBuildingID)` 切建築（oldBuildingID > 0、A2 案）| assignedBuildingID = otherBuildingID、`cooldownEnd ← now + 7200s`、reallocatingStart 保持 0 |
| `Working` | `Reallocating` | `TryUnassignStaff` | assignedBuildingID = 0、reallocatingStart = now、`cooldownEnd ← now + 7200s` |
| `Reallocating` | `OnLeave` | `now − reallocatingStart ≥ REALLOCATING_AUTO_LEAVE_SECONDS`（§3.5）| assignedBuildingID = 0、reallocatingStart = 0 |
| `Working` | `OnLeave` | `TryGoOnLeave(instanceID)` | assignedBuildingID = 0、reallocatingStart = 0 |
| `Reallocating` | `OnLeave` | `TryGoOnLeave(instanceID)` | 同上 |
| `OnLeave` | `Reallocating` | `TryReturnFromLeave(instanceID)` | reallocatingStart = now（玩家須在 12h 內 TryAssignStaff，否則自動轉假）|
| 任何 | (銷毀) | `TryFireStaff(instanceID)` | StaffInstance 從 roster 移除（§3.4）|

> **Reallocating 入口語意統一（A2 案，2026-04-25 裁示）**：Reallocating 永遠表示「`assignedBuildingID == 0`、等待玩家選 buildingID」，**不再用於切換建築過渡**。三大進入路徑——錄用後（含 slot 能力）/ 取消指派 / 從休假回崗。切建築走 `Working → Working`（§3.4.2，`cooldownEnd ← now + 7200s`），不經 Reallocating；同時 `Working → Reallocating（unassign）` 也設冷卻，避免「Working → Reallocating → Working」鏈條繞過 7200s 切建築冷卻。Reallocating 帶 `reallocatingStartTimestamp` 計時器；該計時器於進入新指派（Working）或進入休假（OnLeave）時 reset 為 0。

#### 3.6.3 公開 API 合約（狀態切換）

| API | 簽章 | 回傳碼 |
|---|---|---|
| `TryGoOnLeave(int instanceID)` | `(int) → GoOnLeaveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `ALREADY_ON_LEAVE` |
| `TryReturnFromLeave(int instanceID)` | `(int) → ReturnFromLeaveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `STAFF_NOT_FOUND` / `NOT_ON_LEAVE` |
| `TryAssignStaff` / `TryUnassignStaff` | 見 §3.5.2 | 見 §3.5.2 |

#### 3.6.4 冷卻欄位語意

`buildingSwitchCooldownEndTimestamp` 與 `currentState` 正交（§3.1 已述）：

| 情境 | 冷卻處理 |
|---|---|
| Working → Working（切建築，A2 案）| `cooldownEnd ← now + 7200s` |
| Working → Reallocating（取消指派）| `cooldownEnd ← now + 7200s` |
| Working / Reallocating → OnLeave（休假）| 不重置（持續倒數）|
| OnLeave → Reallocating（回崗）| 不重置（沿用既有冷卻）|
| Reallocating → OnLeave（自動轉假，§3.5）| 不重置 |
| Reallocating → Working（落位）| 不重置（首次落位無冷卻 / unassign 後沿用既有冷卻）|
| 跨 session 載入 | 冷卻照舊（時間戳持久化）|

**設計理由**：冷卻針對「玩家短時間反覆切建築操弄聚合結果」；切建築與取消指派均設冷卻，避免「Working → Reallocating（unassign）→ Working（重 assign）」鏈條繞過切換限制；休假 / 落位本身不重置冷卻，避免「切建築 → 休假 → 馬上回崗 → 切到新建築」的繞過。

#### 3.6.5 持久化欄位驗證

| 欄位 | 持久化 | 載入時驗證 |
|---|---|---|
| `currentState` | ✅ | enum 值合法 |
| `assignedBuildingID` | ✅ | OnLeave → 0；Reallocating → 0（A2 不變式）；Working → ≥ 0（≥ 1 = 佔用 slot；= 0 僅當 staff 無 slot 能力，§3.4.2 row 1）|
| `reallocatingStartTimestamp` | ✅ | state == Reallocating → > 0；其他 → 0 |
| `buildingSwitchCooldownEndTimestamp` | ✅ | ≥ 0 |

驗證違反 → 拋 `StaffInstanceValidationException`（FT-10 載入時）。

#### 3.6.6 狀態查詢 API（內部）

供 P-02 UI 讀取顯示用，不在玩家 API 範圍：

```csharp
internal static StaffStateView GetStaffStateView(int instanceID);
// 回傳 { currentState, assignedBuildingID, reallocatingRemainingSec, switchCooldownRemainingSec }
//   - reallocatingRemainingSec = max(0, reallocatingStart + REALLOCATING_AUTO_LEAVE_SECONDS − now)；
//                                state ≠ Reallocating 時為 0
//   - switchCooldownRemainingSec = max(0, switchCooldownEnd − now)
```

---


---

### 3.7 再分配自動轉休假（Auto-Leave on Long Reallocation）

#### 3.7.1 設計動機

避免玩家「錄用後 / unassign 後 / 從休假回崗後忘記選 buildingID」造成 Reallocating 狀態無限延伸；強制 12h 後自動轉 OnLeave，使玩家必須主動回崗（TryReturnFromLeave + TryAssignStaff）才能繼續使用該職員的 Slot effect。

> A2 案（2026-04-25 使用者裁示）下，**切建築直接 Working、不進 Reallocating**，因此本節規則只對三大入口（剛錄用 / unassign 後 / OnLeave 回崗後）的 Reallocating 職員適用。

#### 3.7.2 觸發條件

```
shouldAutoLeave(staff) =
    (staff.currentState == Reallocating)
    AND (staff.reallocatingStartTimestamp > 0)
    AND (now − staff.reallocatingStartTimestamp ≥ REALLOCATING_AUTO_LEAVE_SECONDS)
```

`REALLOCATING_AUTO_LEAVE_SECONDS = 43200`（12h；§7.2）。

#### 3.7.3 檢查時機

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

#### 3.7.4 與冷卻的互動

`buildingSwitchCooldownEndTimestamp` **不**因自動轉假重置（§3.4.4）。玩家被自動轉假後 TryReturnFromLeave 回 Reallocating → 仍受原冷卻約束（直到 cooldown 結束才能 TryAssignStaff）。

#### 3.7.5 邊緣情境

| 情境 | 行為 |
|---|---|
| 玩家 unassign → 12h 內未指派 | 自動轉 OnLeave |
| 玩家 assign 切建築（A2 案）| **不進 Reallocating** → 直接 Working @ 新建築；不受 12h auto-leave 影響（state 已為 Working）|
| 玩家在 11h59m 時 TryAssignStaff 落位 | Reallocating → Working、不轉假、reallocatingStart = 0 |
| 玩家離線 24h | OnStaffSystemBoot 時批次轉假所有 reallocating > 12h 的職員 |
| 系統降級期間 | CheckReallocatingAutoLeave 不執行；reallocatingStartTimestamp 凍結；解鎖後 OnStaffSystemBoot 處理 |
| 系統降級恰好跨 12h | 解鎖後立即批次轉假所有跨閾值職員 |

---


---

### 3.8 解雇流程（Fire Flow）

#### 3.8.1 公開 API

```csharp
TryFireStaffResult TryFireStaff(int instanceID);
// 回傳碼：SUCCESS / STAFF_SYSTEM_LOCKED / STAFF_NOT_FOUND
```

#### 3.8.2 完整流程

```
TryFireStaff(instanceID):
    1. 系統閘：IsStaffSystemUnlocked() == false → STAFF_SYSTEM_LOCKED
    1.5 入口轉假掃描：CheckReallocatingAutoLeave()      // §3.5.3；解雇 OnLeave 職員不影響流程，但維持 §3.5.3 入口契約一致
    2. 找職員：staff = roster.Find(instanceID)；找不到 → STAFF_NOT_FOUND
    3. 計算資遣費：
       severancePay = StaffTable[staff.staffID].severancePay
    4. 扣款（不檢查餘額）：
       FT05.AddGoldAllowBankruptcy(-severancePay)
       // 允許進入負債（FT-05 §3.5 既有契約）；解雇是強制動作不可阻擋
    5. 發布事件（先於 roster.Remove，§3.4.4）：
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

#### 3.8.3 不可逆性

- 解雇後 `instanceID` 不再回收（§3.1）；同存檔內永不重複
- 任一狀態（Working / Reallocating / OnLeave）皆可解雇
- 解雇者的 cooldown / reallocatingStart / assignedBuildingID 等欄位隨 StaffInstance 一同銷毀
- 不可撤銷；玩家「再次抽到同 staffID」會建立新的 StaffInstance（新 instanceID）

#### 3.8.4 事件發布 vs roster.Remove 順序

**設計決策（§7.4 不可調）**：發布 `OnStaffFired` **先於** `roster.Remove(staff)`。

理由：

- 訂閱者可能需要在 staff 仍存在時取得最後狀態（例：通知 UI「您解雇了 [name]」需 query staff.staffID 對應 StaffTable.name）
- 訂閱者若關心「解雇後的聚合值」，應於 callback 內 query 時自行 filter `instanceID != firedInstanceID`，或訂閱時使用 deferred callback
- §3.4 演算法為即時遍歷無快取——`roster.Remove` 後任何後續 query 立即反映新值，無 cache 失效需求

#### 3.8.5 解雇條件與失敗情境

| 情境 | 行為 |
|---|---|
| 玩家金幣不足 severancePay | 仍允許解雇；FT-05 進入負債（AddGoldAllowBankruptcy）；玩家若 < 破產閾值觸發破產（FT-05 §3.4）|
| 系統降級期間 | 回 `STAFF_SYSTEM_LOCKED`；解雇被擋（避免在系統停擺期 roster 異動）|
| 解雇 OnLeave 職員 | 允許；流程相同；不影響薪水（OnLeave 本就不領薪）|
| 解雇 Reallocating 職員 | 允許；該職員 reallocatingStart 隨 instance 銷毀；不發 OnStaffStateChanged（state 不需改、instance 直接銷毀）|

#### 3.8.6 與保留候選的關係

解雇某 `instanceID` **不影響** `reservedCandidates` 或 `currentCandidates` 中同 `staffID` 的候選卡（候選卡與 StaffInstance 是不同物件）。玩家可解雇 instance、再從候選卡錄用同 staffID 建新 instance（新 instanceID）。

---


---

### 3.9 薪水管線（Salary Pipeline）

> **⚠️ Phase 2 — Jam 版整節不實作（B 案，2026-04-25 使用者裁示）**
>
> 對齊 FT-05 既有 Phase 2 立場（line 31 / 257 / 285）。Jam 版 FT-08 **不訂閱 OnHourTick**、**不執行 OnSalaryTick / ProcessOfflineSalary**、**永不發布 `OnStaffSalaryDue`**。職員「招募即終身免費」為已知 Jam 範圍妥協；OnStaffSystemBoot Step D 直接 skip。
>
> 下列 §3.5.1 ~ §3.5.7 內容**保留為 Phase 2 規格**，Codex 工項書階段全部跳過實作；Phase 2 啟用時直接套用本節，並同步移除 FT-05 的 Phase 2 註記（FT-05 §3.5 / §6 / §7）。

#### 3.9.1 觸發時點（Phase 2）

- **線上 tick**：每日 UTC 06:00（`SALARY_UTC_HOUR = 6`、§7.2）；FT-08 訂閱 FT-02 TimeSystem 的 `OnHourTick` 事件，於 hour == 6 時觸發 OnSalaryTick
- **離線啟動**：OnStaffSystemBoot Step D 補發跨日累積薪水

#### 3.9.2 perStaffSalary 組裝（與 §4.1.1 對齊）

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

#### 3.9.3 線上發薪流程

```
OnSalaryTick(dueTimestamp):
    1. 系統閘：IsStaffSystemUnlocked() == false → return（§5.4.3）
    2. 時鐘檢查：now < lastSalaryTimestamp → return（§5.1.1 / Case 5.4 / §4.1.3）
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

#### 3.9.4 離線補發迴圈

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

#### 3.9.5 lastSalaryTimestamp 持久化

| 階段 | 行為 |
|---|---|
| 新存檔首次解鎖 | `lastSalaryTimestamp ← now`（OnStaffSystemUnlocked callback）|
| 線上每次 OnSalaryTick | `lastSalaryTimestamp ← dueTimestamp`（步驟 7）|
| 離線補發每 cycle | 同上（OnSalaryTick 內部）|
| 系統降級期間 | 不更新（OnSalaryTick 早退）|
| 重新解鎖（L0 → L1） | `lastSalaryTimestamp ← now`（防補發降級期間薪水，§3.5.7）|
| 時鐘倒退 | 不更新 |

#### 3.9.6 OnLeave 職員薪水跳過

§4.1.1 / §5.4.1 已定義：OnLeave 職員不入 perStaffSalary。**特殊情境**：

- 發薪當 frame 玩家 `TryGoOnLeave(instanceID)` → 視 snapshot 時點：snapshot 在 TryGoOnLeave 之前 → 計薪；之後 → 不計薪
- **設計上不保證**這微秒級的順序；玩家不應依賴此行為（§5.4.2 已述用 atomic snapshot 防 dict 污染）

#### 3.9.7 系統降級時的薪水管線

```
OnStaffSystemUnlocked():    // 由 §3.10.5 訂閱 OnBuildingUpgraded 觸發
    IF lastSalaryTimestamp != 0:
        lastSalaryTimestamp ← now                     // 防補發降級期間薪水
```

降級期間：

- OnSalaryTick 不執行（系統閘擋下）
- ProcessOfflineSalary 不執行
- lastSalaryTimestamp 凍結
- 重新解鎖時 reset 為 now → 不補發降級期間每天的薪水（簡化決策、不公平給玩家但避免破產風暴）

#### 3.9.8 事件 payload 完整契約

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
- **P-03**（可選）：toast「支付了 X gold 薪水給 N 位職員」 **【→Log API待更新】**
- 任一訂閱者 throw → 不影響其他訂閱者（EventBus 容錯，commits 25efd11 / 2c10d17 已強化）

---


---

### 3.10 系統降級行為總表（Degradation Contract）

當 `FT07.IsStaffSystemUnlocked() == false` 時，FT-12 進入降級模式。

#### 3.10.1 對外 API 行為總表

| API | 降級時行為 |
|---|---|
| `HireStaff(CandidateCard)` | return `STAFF_SYSTEM_LOCKED`、不變動 roster |
| `IsSuccessRatePreviewEnabled()` | return false（早退、不遍歷 roster）|
| `GetStaffWillingnessBonus()` | return 0f（早退）|
| `GetAccountantCommissionBonus()` | return 0f（早退）|
| `GetAccountantPenaltyBonus()` | return 0f（早退）|
| `GetRecruitRefreshReductionSec()` | return 0（早退）|
| `TryAssignStaff(int, int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryUnassignStaff(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryGoOnLeave(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryReturnFromLeave(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryFireStaff(int)` | return `STAFF_SYSTEM_LOCKED` |

#### 3.10.2 事件發布總表

| 事件 | 降級時 |
|---|---|
| `OnStaffHired` | 不發布（HireStaff 早退）|
| `OnStaffFired` | 不發布（TryFireStaff 早退）|
| `OnStaffAssigned` | 不發布 |
| `OnStaffStateChanged` | 不發布 |
| `OnStaffSalaryDue` | 不發布；**Jam 範疇下因 §3.9 為 Phase 2，無視系統解鎖狀態，永不發布** |

#### 3.10.3 內部流程降級

| 流程 | 降級時 |
|---|---|
| Bootstrap 薪水補發（§3.9.4）| 跳過；**Jam 範疇下整段 §3.9 為 Phase 2，無條件跳過** |
| `CheckReallocatingAutoLeave`（§3.7） | 跳過（reallocatingStartTimestamp 凍結、解鎖後沿用）|
| `OnSalaryTick`（OnHourTick 訂閱）| 早退；**Jam 範疇下未訂閱 OnHourTick（§3.9 Phase 2），不存在觸發路徑** |

#### 3.10.4 持久化資料保留

降級期間，下列資料**全部保留**：

- `StaffInstance[]`：所有職員的 state / assignedBuildingID / cooldown / reallocatingStart / hiredTimestamp
- `_lastSalaryTimestamp`：解鎖時 reset 為 now（§3.9.7）

#### 3.10.5 降級進入 / 離開的事件 callback

FT-12 不主動訂閱 FT-07 `OnBuildingUpgraded` 用於 API 降級——所有 API 入口處 query `IsStaffSystemUnlocked()`（惰性檢測）。但 §3.9.7「解鎖時 reset lastSalaryTimestamp」需要訂閱：

```csharp
EventBus.Subscribe<OnBuildingUpgraded>(evt => {
    IF evt.buildingID == 6 AND evt.fromLevel == 0 AND evt.toLevel == 1:
        OnStaffSystemUnlocked();   // §3.9.7：lastSalaryTimestamp ← now
});
```

降級進入（L1+ → L0）Jam 版**不訂閱**——降級為 lazy 檢測；玩家若在線降級會看到下次 query 即返回 0 / locked。

---

### 3.11 事件契約（Event Contracts）

§6.3 已概覽事件清單；本節展開每個事件的精確 payload、發布時序、訂閱者責任、降級行為。

#### 3.11.1 事件清單與 payload

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

OnStaffSalaryDue {                  // ⚠️ Phase 2，Jam 版不發布（§3.9 整節為 Phase 2）
    long           dueTimestamp
    Dict<int,int>  perStaffSalary    // key = instanceID
    int            totalAmount
}
```

#### 3.11.2 發布時序保證

| 動作 | 順序 |
|---|---|
| HireStaff 成功（§3.3.2）| 1. roster.Add(instance) → 2. Publish `OnStaffHired` |
| TryFireStaff 成功（§3.8.2）| 1. AddGoldAllowBankruptcy(-severancePay) → 2. Publish `OnStaffFired` → 3. roster.Remove |
| TryAssignStaff（state 改變：Reallocating → Working，oldBuildingID = 0）| 1. assignedBuildingID 改 → 2. state 改 → 3. Publish `OnStaffAssigned` → 4. Publish `OnStaffStateChanged` |
| TryAssignStaff（state 不變、僅改 buildingID）| 1. assignedBuildingID 改 + cooldownEnd 重設 → 2. Publish `OnStaffAssigned` |
| TryAssignStaff（idempotent，buildingID 同舊）| 無事件 |
| TryUnassignStaff | 1. assignedBuildingID = 0 → 2. state = Reallocating → 3. Publish `OnStaffAssigned` → 4. Publish `OnStaffStateChanged` |
| TryGoOnLeave | 1. state = OnLeave、assignedBuildingID = 0 → 2. Publish `OnStaffStateChanged` |
| TryReturnFromLeave | 1. state = Reallocating、reallocatingStart = now → 2. Publish `OnStaffStateChanged` |
| Reallocating > 12h 自動轉假（§3.7）| 1. state = OnLeave、assignedBuildingID = 0 → 2. Publish `OnStaffStateChanged` |
| OnSalaryTick（Phase 2）| 1. perStaffSalary 組裝（atomic snapshot）→ 2. Publish `OnStaffSalaryDue` → 3. lastSalaryTimestamp 更新；Jam 版整段不執行 |

#### 3.11.3 訂閱者責任表

| 事件 | 訂閱者 | 處理動作 |
|---|---|---|
| OnStaffHired | P-02 名冊 UI | 加入名冊顯示 |
| OnStaffHired | P-03 通知 **【→Log API待更新】** | toast「招募了 [name]」 |
| OnStaffFired | P-02 | 從名冊移除 |
| OnStaffFired | P-03 **【→Log API待更新】** | toast「解雇了 [name]，支付 X gold 資遣費」 |
| OnStaffAssigned | P-02 | 名冊 UI 更新指派狀態圖示 |
| OnStaffAssigned | FT-05（保險櫃 slot 變動感知）| 不需主動處理（§3.4 即時遍歷無快取）|
| OnStaffStateChanged | P-02 | 顯示 Working / Reallocating / OnLeave 狀態圖示 |
| OnStaffStateChanged | P-03（轉假）| toast「[name] 進入休假」（OnLeave 觸發時）|
| OnStaffSalaryDue | FT-05（**硬契約 / Phase 2**）| `AddGoldAllowBankruptcy(-totalAmount)`；Jam 版 FT-12 不發布、FT-05 訂閱端 no-op |

#### 3.11.4 訂閱者錯誤隔離

EventBus 已強化（commits 25efd11 / 2c10d17）：任一訂閱者 throw → 隔離、其他訂閱者繼續執行；FT-12 自身不在訂閱中重發任何事件，避免 reentrancy。

#### 3.11.5 降級時不發布

§3.10.2 已詳列；下游若需感知降級狀態，自行 poll `IsStaffSystemUnlocked()` 或訂閱 FT-07 `OnBuildingUpgraded(buildingID=6)` 自行判斷。

---

## 4. 公式（Formulas）

> 本章彙整 FT-12 的核心公式。§4.1 effect 聚合上限對齊 §3.4.3 疊加規則；§4.2 薪水公式為 Phase 2 規格（Jam 版不實作但完整保留）。

### 4.1 效果聚合上限（Effect Aggregation Caps）

§3.4.2 演算法的最後一步皆為 cap 截斷；以下是 §3.4.3 表格的公式形式化。

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


---

### 4.2 薪水公式（Salary Formulas）

> **⚠️ Phase 2 — Jam 版整段公式不執行（B 案，2026-04-25 使用者裁示）**
>
> 對齊 §3.5 / FT-05 既有 Phase 2 立場。下列公式（每日薪水 dict 組裝、總額、離線補發）保留為 Phase 2 規格、Jam 版 Codex 不需實作；其引用之 `lastSalaryTimestamp`、`SALARY_UTC_HOUR`、`OFFLINE_MAX_SECONDS` 皆無 runtime 觸發路徑。Phase 2 啟用時直接套用本節，並同步移除 FT-05 §3.5 的 Phase 2 註記。

#### 4.2.1 每日薪水 dict 組裝（Phase 2）

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

#### 4.2.2 薪水總額（total amount）

事件 payload 附帶總額供 FT-05 快速檢查；欄位名稱對齊 FT-05 既有契約（§6.3）：

```
totalAmount = Σ_{k ∈ perStaffSalary.Keys} perStaffSalary[k]
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `totalAmount` | 本次發薪總扣款（事件 payload 欄位）| `≥ 0`；實務 Jam 版 `[0, ~3000]` gold/day |

#### 4.2.3 離線補發次數

```
missedSalaryCycles = floor((now − lastSalaryTimestamp) / 86400)
                     clamped by SystemConstants.OFFLINE_MAX_SECONDS / 86400
```

每一個補發週期獨立組裝 `perStaffSalary` 一次、發布事件一次（逐次觸發 FT-05 扣款；具體離線迴圈邏輯於 §3.5 定義）。

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


---


## 5. 邊緣案例（Edge Cases）

### 5.1 時間異常（Time Anomalies）

| 情境 | 處理方式 |
|---|---|
| 系統時鐘倒退（now < hiredTimestamp）| `reallocatingStartTimestamp` / `cooldownEndTimestamp` 比較使用 `Mathf.Max(0, now - reallocatingStart)`；負數 clamp 至 0；不拋例外 |
| 離線跨日（Phase 2 薪水補發）| §3.9.4：`offlineDays = floor((now - lastSalaryTimestamp) / 86400)`；上限由 F-02 `OFFLINE_MAX_SECONDS = 604800`（7 天）clamp |
| `reallocatingStartTimestamp == 0`（舊存檔）| `CheckReallocatingAutoLeave` 跳過該 instance；下次 `TryAssignStaff` / `TryUnassignStaff` 重設為 `now` |
| `lastSalaryTimestamp == 0`（首次解鎖）| §3.9.7 reset 為 `now`；不補發歷史薪水 |

### 5.2 名冊操作（Roster Operations）

| 情境 | 處理方式 |
|---|---|
| `TryAssignStaff` 指派建築 capacity 已滿 | return `BUILDING_AT_CAPACITY`；不變動 instance state |
| `TryAssignStaff` 同建築重複指派（idempotent） | return `OK`、不發 `OnStaffAssigned` 事件、不重設 cooldown |
| `TryUnassignStaff` 對 OnLeave 職員 | return `INVALID_STATE`；OnLeave 職員需先 `TryReturnFromLeave` |
| `TryFireStaff` 對 Working 職員 | 流程允許；step 1 自動 unassign（§3.8.2）；資遣費照付 |
| `HireStaff` 後 12h 內未指派 | `CheckReallocatingAutoLeave` 自動轉 OnLeave（§3.7） |

### 5.3 薪水（Phase 2，Jam 版不驗收）

| 情境 | 處理方式 |
|---|---|
| 離線跨日 N 天 | §3.9.4 補發迴圈逐日發 `OnStaffSalaryDue`；FT-05 逐筆扣款（Phase 2） |
| OnLeave 職員 | §3.9.6 不入 perStaffSalary（薪水 = 0） |
| StaffTable.salary 為 0 / 負值 | DataManager 載入時 `LogError` + clamp 至 0；Jam 版預期僅 0 為合法 |
| 同 frame 內多個 `OnStaffSalaryDue` 觸發 | EventBus 序列化發布；FT-05 訂閱者依序處理 |

### 5.4 資料驗證（DataManager）

| 情境 | 處理方式 |
|---|---|
| `StaffTable.staffID == 0` | DataManager 載入時 `StaffTableValidationException`，跳過該行 |
| `StaffTable.effectIDs` / `effectValues` 長度不一致 | DataManager 載入時 `LogError` + 視為 effect 全空 |
| `StaffTable.minGuildLevel < 1` | `LogWarning` + clamp 至 1 |
| `StaffInstance.staffID` 在還原時找不到對應 StaffTable 行 | `RestoreFromSave` 階段拋 `CriticalRestoreFailedException`（FT-10 critical 路徑） |

### 5.5 系統邊界（System Gate）

| 情境 | 處理方式 |
|---|---|
| 未解鎖時呼叫 API | §3.10.1 全部走降級路徑、`STAFF_SYSTEM_LOCKED` |
| 解鎖瞬間（OnBuildingUpgraded buildingID=6 fromLevel=0 toLevel=1）| §3.10.5 訂閱 callback：`lastSalaryTimestamp ← now`（Phase 2 才生效） |
| 降級後再解鎖 | StaffInstance[] 保留、effect 立即恢復查詢；Jam 版不重發歷史事件 |

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-12 消費）

| # | 上游系統 | 消費 API / 資料 | 用途 |
|---|---|---|---|
| 1 | F-01 DataManager | `Get<StaffData>`, `GetAll<StaffData>`, `GetInt`（StaffTuning） | StaffTable 查詢、effect 上限 / cooldown 等系統常數 |
| 2 | F-02 Time System | `OnDailyReset`（Phase 2 薪水）、`OnHourTick`（自動轉休假檢查） | 驅動 §3.7 / §3.9 |
| 3 | F-03 Resource Mgmt | `AddGoldAllowBankruptcy`（透過 FT-05 中介） | 解雇資遣費（§3.8.2）、薪水扣款（Phase 2 §3.9） |
| 4 | FT-06 Guild Core | `GetCurrentLevel()` | StaffTable.minGuildLevel 過濾 |
| 5 | FT-07 Guild Building | `IsStaffSystemUnlocked()`、`GetBuildingState(buildingID).currentLevel` | 系統閘 + 各建築 capacity 取得 |
| 6 | FT-08 Gacha System | （單向被動接收）`HireStaff(candidate)` 由 FT-08 呼叫 | 接收錄用候選 |

### 6.2 下游消費者（FT-12 被消費）

| # | 下游系統 | 消費介面 | 用途 |
|---|---|---|---|
| 1 | FT-08 Gacha | 呼叫 `HireStaff(candidate)`、查詢 `GetRecruitRefreshReductionSec()` | 錄用流程交棒、面試 auto refresh 加成 |
| 2 | FT-03 NPC Decision | 查詢 `GetStaffWillingnessBonus()` | NPC 接受意願加成 |
| 3 | FT-05 Guild Gold Flow | 訂閱 `OnStaffSalaryDue`（Phase 2）、查詢 `GetAccountantCommissionBonus` / `GetAccountantPenaltyBonus` | 薪水扣款（Phase 2）+ 委託金流會計加成 |
| 4 | FT-02 Mission Dispatch | 查詢 `IsSuccessRatePreviewEnabled()` | UI 顯示成功率預覽 flag |
| 5 | P-02 Main UI | 訂閱 5 事件 + 名冊 UI / 指派 UI / 解雇確認 | 玩家可見的職員管理介面 |
| 6 | P-03 Notification | 訂閱 OnStaffHired / OnStaffFired / OnStaffStateChanged（轉假時） | 桌面通知 |
| 7 | FT-10 Save/Load | `ISaveable` 實作（§6.7） | 序列化 StaffInstance[] / lastSalaryTimestamp |

### 6.3 事件契約

詳見 §3.11；本表為摘要：

| 事件 | 方向 | 來源 / 訂閱者 |
|---|---|---|
| `OnStaffHired` | 出 | FT-12 → P-02 / P-03 |
| `OnStaffFired` | 出 | FT-12 → P-02 / P-03 |
| `OnStaffAssigned` | 出 | FT-12 → P-02 / FT-05（被動感知） |
| `OnStaffStateChanged` | 出 | FT-12 → P-02 / P-03 |
| `OnStaffSalaryDue` | 出（**Phase 2**）| FT-12 → FT-05（硬契約） |
| `OnBuildingUpgraded` | 入 | FT-07 → FT-12（§3.10.5 解鎖 callback） |

### 6.4 資料表依賴

| 表格 | Owner | FT-12 用途 |
|---|---|---|
| `StaffTable.csv` | FT-12 owner | 職員資料定義（§3.2） |
| `StaffTuning.csv` | FT-12 owner | effect 上限 / cooldown 等系統常數（§7.2） |

> `StaffGachaPoolTable` / `StaffRefreshCostTable` / `StaffRarityProbTable` / `TrashItemTable` 為 FT-08 owner，FT-12 不消費。

### 6.5 共享常數（SystemConstants.csv）

| 常數 | 預設值 | 用途 |
|---|---|---|
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | `21600`（6h）| §3.5 切換建築冷卻 |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | `43200`（12h）| §3.7 自動轉休假觸發 |
| `STAFF_BASE_SEVERANCE_GOLD` | `100` | §3.8 解雇基礎資遣費 |

### 6.6 雙向聲明同步表

| 對端 GDD | FT-12 對端應有的反向登記 |
|---|---|
| `[FT-08]` | §6 下游列出 FT-12 為 `HireStaff` 呼叫對象、`GetRecruitRefreshReductionSec` 消費者 |
| `[FT-07]` | §6 下游列出 FT-12 為 `IsStaffSystemUnlocked` / `GetBuildingState` 消費者 |
| `[FT-06]` | §6 下游列出 FT-12 為 `GetCurrentLevel` 消費者 |
| `[F-02]` | §6 下游列出 FT-12 為 `OnDailyReset` / `OnHourTick` 訂閱者（Phase 2 / Jam） |
| `[FT-05]` | §6 上游列出 FT-12 為 `OnStaffSalaryDue` 發布者（Phase 2） |
| `[FT-03]` | §6 上游列出 FT-12 為 `GetStaffWillingnessBonus` 提供方 |
| `[FT-09]` | §設計來源聲明：`StaffTable.factionID` Post-Jam 才消費 |
| `[FT-10]` | §6.4 反向依賴清單列出 FT-12 為 `ISaveable` owner（Critical） |

### 6.7 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft12StaffSystem"` |
| `IsCritical` | `true`（StaffInstance 缺失導致 effect 聚合 / 薪水管線無法運作，整檔回退） |

**`Serialize()` 序列化欄位**：

| 欄位 | 型別 | Unity 序列化中介 | 說明 |
|---|---|---|---|
| `_roster` | `Dictionary<int, StaffInstance>` | `List<StaffInstance>` | 職員名冊（key 為 instanceID） |
| `_nextInstanceID` | `int` | `int` | 全域單調遞增 ID |
| `_lastSalaryTimestamp` | `long` | `long` | Phase 2 薪水補發基準 |

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化上述 3 個欄位。
2. 對每筆 `StaffInstance.staffID` 透過 `F-01.Get<StaffData>(staffID)` 驗證合法性；找不到時拋 `CriticalRestoreFailedException`（FT-10 critical 路徑，§3.3.4）。
3. 對每筆 `StaffInstance.assignedBuildingID != 0` 驗證 buildingID 合法性（透過 FT-07）；不合法時 reset assignedBuildingID = 0、state = Reallocating、reallocatingStartTimestamp = now、`LogWarning`。
4. 還原完成不補發任何事件（UI 由 Bootstrap 後查詢刷新）。

**`InitializeAsNewGame()` 預設值**：

| 欄位 | 初始值 |
|---|---|
| `_roster` | 空字典 |
| `_nextInstanceID` | `1` |
| `_lastSalaryTimestamp` | `0` |

對應 FT-10 §3.3.3 拓撲順序 row（FT-12）、§3.3.4 Critical 分類、§6.1（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 資料表參數（CSV）

#### 7.1.1 `StaffTable.csv` 數值欄位

| 欄位 | 型別 | 用途 | 安全範圍 |
|---|---|---|---|
| `salary` | int | 每日薪水（Phase 2）| `[0, 10000]`；Jam 預設全 0（薪水未實作） |
| `severancePay` | int | 解雇資遣費 | `[0, 5000]`；Jam 預設範圍 50~500 視稀有度 |
| `minGuildLevel` | int | 可錄用最低公會等級 | `[1, 5]` |

#### 7.1.2 `StaffTable.effectIDs` / `effectValues`

每位職員攜帶 effect 清單（CSV 多值欄位以 `|` 分隔）：

| effectID | effectValue 安全範圍 | 用途 |
|---|---|---|
| `WillingnessBonus` | `[0.0, 0.20]` | FT-03 NPC 接受意願加成（櫃台類） |
| `AccountantCommissionBonus` | `[0.0, 0.10]` | FT-05 委託傭金加成（會計類，需指派至預備金保險櫃 buildingID=5） |
| `AccountantPenaltyBonus` | `[-0.10, 0.0]` | FT-05 委託失敗罰款減免（會計類） |
| `RecruitRefreshReduction` | `[0, 21600]`（秒） | FT-08 面試 auto refresh 間隔減免 |
| `SuccessRatePreview` | `bool` | UI flag，啟用任務成功率預覽 |

聚合上限見 §4.1（疊加封頂規則）。

### 7.2 系統常數（StaffTuning.csv / SystemConstants.csv）

| key | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|
| `BUILDING_SWITCH_COOLDOWN_SECONDS` | `21600`（6h）| `[0, 86400]` | §3.5 切換建築冷卻；過短會讓玩家頻繁切換 effect、過長挫折感 |
| `REALLOCATING_AUTO_LEAVE_SECONDS` | `43200`（12h）| `[3600, 86400]` | §3.7 自動轉休假觸發；過短不友善（玩家剛雇就被轉假）、過長無效 |
| `STAFF_BASE_SEVERANCE_GOLD` | `100` | `[0, 1000]` | 解雇基礎資遣費（疊加 StaffTable.severancePay） |

### 7.3 FT-07 擁有的 FT-12 相關參數

| 參數（位於 BuildingTable）| 用途 |
|---|---|
| `BuildingTable[buildingID=6].levelEffects[L].slotCount`（職員休息室） | 名冊容量上限（roster slot 數） |
| `BuildingTable[buildingID=5].levelEffects[L].slotCount`（預備金保險櫃） | 會計指派 slot 數（影響 §3.4 effect 聚合） |

### 7.4 不可調參數（設計決策硬寫）

| 項目 | 值 | 理由 |
|---|---|---|
| 三態狀態名稱（`Working` / `Reallocating` / `OnLeave`）| 固定 | 改名需同步 SaveData / 事件 payload / UI |
| HireResult enum 值 | 固定 | 跨系統契約（FT-08 呼叫端） |
| OwnerKey `"ft12StaffSystem"` | 固定 | SaveData root JSON 子區塊 key |

### 7.5 平衡建議（Designer Notes）

- effect 數值校準從 `WillingnessBonus = 0.05`、`AccountantCommissionBonus = 0.02` 開始；先驗證 §3.4 聚合上限不被輕易觸發
- `severancePay` 應反映稀有度差異：1★ ≈ 50、5★ ≈ 500；玩家解雇 5★ 應感受到沉重
- `REALLOCATING_AUTO_LEAVE_SECONDS = 12h` 對應「離線 8h 醒來指派」的玩家節奏；若 playtest 顯示太多人轉假，可調至 24h

---

## 8. 驗收標準（Acceptance Criteria）

### 8.1 系統初始化與閘控

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-1 | 職員休息室未建造 | 全 `Try*` API 回傳 `STAFF_SYSTEM_LOCKED`、全 `Get*` API 回傳 0 | EditMode test |
| AC-2 | 解鎖瞬間（OnBuildingUpgraded buildingID=6 0→1） | `_lastSalaryTimestamp` 設為 now | EditMode test |

### 8.2 入職流程

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-3 | FT-08 呼叫 `HireStaff(valid candidate)` | 回 `OK + instanceID`、roster 增 1 筆、發 `OnStaffHired` | EditMode test |
| AC-4 | 系統未解鎖時呼叫 HireStaff | 回 `STAFF_SYSTEM_LOCKED`、roster 不變 | EditMode test |
| AC-5 | 入職後新 instance state = `Reallocating`、reallocatingStartTimestamp = now | 用 `GetInstance(id)` 驗證欄位 | EditMode test |

### 8.3 Slot 指派與三態狀態機

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-6 | `TryAssignStaff(id, validBuildingID)`（state=Reallocating）| 回 `OK`、state→Working、發 `OnStaffAssigned + OnStaffStateChanged` | EditMode test |
| AC-7 | TryAssignStaff 同建築重複指派（idempotent） | 回 `OK`、不發事件、cooldown 不重設 | EditMode test |
| AC-8 | TryAssignStaff 違反 capacity | 回 `BUILDING_AT_CAPACITY`、roster 不變 | EditMode test |
| AC-9 | TryUnassignStaff（state=Working）| state→Reallocating、發兩事件 | EditMode test |
| AC-10 | TryGoOnLeave（state=Working）| 自動 unassign、state→OnLeave、發 `OnStaffStateChanged` | EditMode test |
| AC-11 | TryReturnFromLeave（state=OnLeave）| state→Reallocating、reallocatingStart=now | EditMode test |
| AC-12 | Reallocating 超過 12h | OnHourTick 觸發 `CheckReallocatingAutoLeave` 自動轉 OnLeave | PlayMode test（時間快進） |

### 8.4 解雇流程

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-13 | TryFireStaff（合法 instanceID）| 扣 severancePay、發 `OnStaffFired`、roster 移除 | EditMode test |
| AC-14 | 解雇 Working 職員 | 自動 unassign（state 先 Working→Reallocating 再 fire）、effect 立即停用 | EditMode test |
| AC-15 | 不可逆性 | TryFireStaff 完成後 `GetInstance(id)` 回 null；無「復原」API | EditMode test |

### 8.5 Effect 聚合

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-16 | 名冊空 | 全 `Get*` API 回傳 0 | EditMode test |
| AC-17 | 1 位 Working 會計（指派至 buildingID=5）| `GetAccountantCommissionBonus()` 回 `effectValue` | EditMode test |
| AC-18 | 2 位 Working 會計疊加 | 聚合至 §4.1 上限 | EditMode test |
| AC-19 | OnLeave 職員 | 不貢獻 effect（聚合算法跳過）| EditMode test |
| AC-20 | Reallocating 職員 | 視 effect 類型（passive vs slot-bound）決定貢獻；§3.4.1 規範 | EditMode test |

### 8.6 薪水（Phase 2，Jam 不驗收）

> §3.9 / §4.2 / 本節 AC 標記 Phase 2，Jam 版整段跳過。Codex 工項書應將此節 AC（AC-21~AC-25）標為 `[Phase 2]`：

- AC-21：OnDailyReset 觸發 → `OnStaffSalaryDue` 發布、payload 含 perStaffSalary + totalAmount
- AC-22：離線跨 N 天 → 補發 N 次 OnStaffSalaryDue
- AC-23：OnLeave 職員 → 不入 perStaffSalary（薪水 = 0）
- AC-24：薪水管線降級 → 系統解鎖時 lastSalaryTimestamp = now，不補發歷史
- AC-25：FT-05 訂閱端 → AddGoldAllowBankruptcy(-totalAmount) 完成扣款

### 8.7 資料驗證

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-26 | StaffTable 缺 staffID | DataManager 載入時 LogError、跳過該行 | EditMode test |
| AC-27 | StaffInstance.staffID 還原時找不到對應 StaffTable 行 | RestoreFromSave 拋 CriticalRestoreFailedException | EditMode test |
| AC-28 | StaffInstance.assignedBuildingID 還原時 buildingID 不合法 | reset assignedBuildingID = 0、state = Reallocating、LogWarning | EditMode test |

### 8.8 事件契約

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-29 | OnStaffHired payload 完整 | 含 instanceID / staffID / hiredTimestamp 三欄位 | EditMode test |
| AC-30 | 訂閱者 throw 時 EventBus 隔離 | 其他訂閱者仍執行 | EditMode test |
| AC-31 | 降級時不發布 5 事件 | 系統未解鎖時不論觸發點為何不發任何 OnStaff* | EditMode test |

### 8.9 持久化

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-32 | Save → reload | StaffInstance[] 完整還原（含 state / cooldown / hiredTimestamp） | EditMode test |
| AC-33 | _nextInstanceID 還原後仍單調遞增（不重複） | reload 後 HireStaff 取得新 instanceID > 任何已存在 | EditMode test |
| AC-34 | reallocatingStartTimestamp == 0（舊存檔）| CheckReallocatingAutoLeave 跳過、TryAssignStaff/Unassign 重設為 now | EditMode test |

### 8.10 效能與規模

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-35 | 名冊 50 位職員時 effect 聚合 | 單次查詢 < 1ms（即時遍歷） | Profiler |
| AC-36 | OnDailyReset 觸發薪水（Phase 2）| 50 位職員 perStaffSalary 組裝 < 5ms | Profiler |

---
