# 【FT-01-FSD】功能規格說明書 — Adventurer Recruitment

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-01】adventurer-recruitment.md`（版本：2026-04-27） |
| 對應 Data-Specs | `【FT-01-DS】veteran-rank-weight-table.md`<br>`【C-02-DS】recruit-cost-table.md`（FT-01 §7.2 消費端引用；owner = C-02） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | — |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-01 負責冒險者招募的完整 runtime 流程：維護新手池（F/E 階）與老手池（D~S 階）兩個候選池，執行自動刷新計時（受 FT-07 建築等級與 FT-12 職員效果影響）、手動刷新（免費/付費）、每日免費刷新次數重置、接納新手（免費）、邀請老手（費用+聲望門檻）等操作。系統的職責邊界為：產生候選者、管理候選池狀態、執行接納/邀請交易。冒險者名冊管理（C-02）、成功率計算（FT-02）、金幣數值管理（F-03）不在本系統範圍內。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**
- 新手池與老手池的生成與刷新（自動 + 手動）
- `DAILY_FREE_REFRESH` 次數追蹤與每日重置
- 新手接納流程（免費，無資源消耗）
- 老手邀請流程（金幣扣款 + 聲望門檻驗證）
- 老手階級加權隨機（從 `VeteranRankWeightTable.csv` 讀取權重）
- 候選冒險者生成（優先具名模板、其餘隨機）
- `OnPoolRefreshed` / `OnRecruitSuccess` 事件發布
- `ISaveable` 持久化契約（`_lastRefreshTimestamp`、`_freeRefreshRemaining`、兩個候選池）

**Out-of-Scope**
- 名冊容量管理（C-02 負責）
- 冒險者成功率計算（FT-02 負責）
- 金幣/聲望數值增減的業務邏輯（F-03 負責）
- UI 顯示邏輯（P-02 負責）
- 職員面試/招募（FT-08 Gacha System，與本系統完全獨立）

### 1.3 完成目標（Definition of Done）

| ID | 可驗證條件 |
| --- | --- |
| AC-AR-01 | EditMode 測試：`InitializeAsNewGame()` 後立即呼叫 `CheckAutoRefresh()`，新手池與老手池各有 `RECRUIT_POOL_SIZE`（4）名候選冒險者（stub C-02/C-03/C-04/C-05） |
| AC-AR-02 | EditMode 測試：新手池候選者的 `adventurerInstance.rank ∈ {F, E}`；老手池候選者 `rank ∈ {D, C, B, A, S}` |
| AC-AR-03 | EditMode 測試：以 `maxRecruitableRank = D` stub FT-06，老手池候選者 `rank == D`（全數 D 階） |
| AC-AR-04 | PlayMode 手動步驟：`RecruitRookie(candidateID)` 成功後，`GetRookiePool()` 不含該候選者，`C-02.GetRoster()` 含該冒險者 |
| AC-AR-05 | PlayMode 手動步驟：`RecruitVeteran(candidateID)` 成功後，金幣正確扣除 `cost`，`GetVeteranPool()` 不含該候選者 |
| AC-AR-06 | EditMode 測試：聲望低於 `reputationReq` 時 `RecruitVeteran` 回傳 `false`，金幣不變 |
| AC-AR-07 | EditMode 測試：金幣低於 `cost` 時 `RecruitVeteran` 回傳 `false`，金幣不變 |
| AC-AR-08 | EditMode 測試：`C-02.IsRosterFull()` 回傳 `true` 時，`RecruitRookie` 與 `RecruitVeteran` 均回傳 `false` |
| AC-AR-09 | EditMode 測試：設 `_lastRefreshTimestamp = NowUTC - intervalSec - 1`，呼叫 `CheckAutoRefresh()` 後兩池各重新生成 4 名 |
| AC-AR-10 | EditMode 測試：`ManualRefresh()` 免費成功後 `GetFreeRefreshRemaining()` 減 1，兩池刷新，`GetNextAutoRefreshTimestamp()` 更新 |
| AC-AR-11 | EditMode 測試：免費次數 0，`ManualRefresh()` 付費成功後金幣扣除 `REFRESH_COST` |
| AC-AR-12 | EditMode 測試：免費次數 0 且金幣不足時 `ManualRefresh()` 回傳 `false`，池不變 |
| AC-AR-13 | EditMode 測試：`OnDailyReset()` 呼叫後 `GetFreeRefreshRemaining()` == `DAILY_FREE_REFRESH`（1） |
| AC-AR-14 | EditMode 測試：設 `_lastRefreshTimestamp = NowUTC - 3 * intervalSec`，`RestoreFromSave` 後 `CheckAutoRefresh()` 僅執行一次刷新 |
| AC-AR-15 | EditMode 測試：`isUnique=1` 的模板 `templateID` 在名冊 HashSet 中，刷新後候選池不含該 `templateID` |
| AC-AR-16 | EditMode 測試：同一批候選池 `candidateID / templateID` 無重複 |
| AC-AR-17 | EditMode 測試：`RollVeteranRank` × 1000（全階級開放），D 出現率 40% ± 5%，S 出現率 3% ± 5% |
| AC-AR-18 | EditMode 測試：`FT07.IsStaffSystemUnlocked() == false` 時 `intervalSec == FT07.GetRecruitRefreshInterval().TotalSeconds` |
| AC-AR-19 | EditMode 測試：FT-12 `GetRecruitRefreshReductionSec() == 7200`，`baseSec = 86400`，`intervalSec == 79200` |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

| GDD 章節 | 內容 |
| --- | --- |
| §3.1 | `RecruitCandidate` 資料結構、`RecruitSource` enum |
| §3.2 | 刷新機制（自動 + 手動）、計時起點、每日重置 |
| §3.3 | 新手池生成規則（F/E 50/50、具名模板優先、批次去重） |
| §3.4 | 老手池生成規則（加權隨機、公會等級限制） |
| §3.5 | 接納/邀請流程（`AddAdventurer` 回傳 `false` 的池清理策略、回滾扣款） |
| §3.6 | 查詢 API 簽名 |
| §3.7 | 事件發布契約（`OnPoolRefreshed`、`OnRecruitSuccess`） |
| §4.1 | `CheckAutoRefresh()` 公式（含 FT-12 減量與下限） |
| §4.2 | `ManualRefresh()` 公式 |
| §4.3 | `OnDailyReset()` 公式 |
| §4.4 | `RollVeteranRank()` 公式（資料驅動，從 `VeteranRankWeightTable.csv` 讀取） |
| §4.5 | `GeneratePool()` 公式（Phase 1 具名模板 + Phase 2 隨機生成） |
| §5 | 邊緣案例（§5.1 候選池生成 / §5.2 接納邀請 / §5.3 刷新 / §5.4 存檔） |
| §6 | 依賴關係（上游 9 個系統、下游 2 個系統、循環依賴檢查） |
| §6.4 | ISaveable 持久化契約（`OwnerKey`、`IsCritical`、序列化欄位） |
| §7 | 可調參數（SystemConstants / RecruitCostTable / VeteranRankWeightTable / FT-07 刷新間隔） |
| §8 | 驗收標準（AC-AR-01 ~ AC-AR-19） |

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-01-DS】veteran-rank-weight-table.md` | `VeteranRankWeightTable.csv` | `rank`、`weight` | 老手池候選者階級加權隨機（§4.4 `RollVeteranRank`） |
| `【C-02-DS】recruit-cost-table.md` | `RecruitCostTable.csv` | `rank`、`cost`、`reputationReq` | 老手候選者的招募費用與聲望門檻（`RecruitCandidate.cost` / `.reputationReq`） |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `RECRUIT_POOL_SIZE`、`DAILY_FREE_REFRESH`、`REFRESH_COST`、`MIN_RECRUIT_REFRESH_INTERVAL_SEC` | 池大小、免費刷新次數、付費刷新費用、自動刷新下限秒數 |

### 2.3 上游依賴系統

> **介面命名規範**：本表中以 `IXxxService` / `IXxxSystem` 命名的介面僅為敘述方便（沿用 GDD 用語），實作契約以既有 concrete singleton 為準（`DataManager.Instance` / `TimeSystem.Instance` / `ResourceManagement.Instance` / static `EventBus`）。詳見 `FSD-index.md` §2.10。

| 系統 | 使用介面 / API | 用途 |
| --- | --- | --- |
| F-01 DataManager | `DataManager.GetAll<T>()`、`DataManager.Get<T>(id)` | 載入 `VeteranRankWeightTable`、`RecruitCostTable`、SystemConstants 常數 |
| F-02 Time System | `ITimeSystem.NowUTC`、typed `OnDailyResetEvent`（訂閱）、typed `OnSecondTickEvent`（訂閱） | 自動刷新計時、每日免費刷新次數重置 |
| F-03 Resource Management | `IResourceService.CanAfford(amount)`、`IResourceService.AddGold(-amount)`、`IResourceService.GetReputation()` | 老手邀請扣款、付費手動刷新扣款、聲望門檻查詢 |
| C-02 Adventurer Management | **`IAdventurerFactory.CreateFromTemplate(templateID)` / `IAdventurerFactory.CreateRandomInstance(rank, professionID, raceID, traitIDs)`**（建構職責）；`IAdventurerRoster.AddAdventurer(instance)` / `IAdventurerRoster.IsRosterFull(rosterCap)` / `IAdventurerRoster.GetRoster()`（名冊 CRUD 與容量檢查） | 候選冒險者建立、加入名冊、名冊容量檢查、名冊 templateID 快照。**FT-01 不直接呼叫 `BuildTraitList`**：trait 組裝邏輯封裝於 `IAdventurerFactory.CreateFromTemplate` 內部；FT-01 在隨機冒險者路徑由 C-05 抽完 `traitIDs` 後直接傳入 `CreateRandomInstance` |
| C-03 Profession System | `IProfessionService.GetBaseProfessions()` | 隨機生成時的職業池 |
| C-04 Race System | `IRaceService.RollRace(professionID)` | 隨機生成時的種族抽取 |
| C-05 Trait System | `ITraitService.GetProfessionGroups(professionID)`、`ITraitService.RollTraits(group)` | 隨機生成時的特質抽取 |
| FT-06 Guild Core | `IGuildCoreService.GetMaxRecruitableRank()` | 老手池階級上限 |
| FT-07 Guild Building System | `IGuildBuildingService.GetRosterCap()`、`IGuildBuildingService.GetRecruitRefreshInterval()`、`IGuildBuildingService.IsStaffSystemUnlocked()` | 名冊容量閘門、自動刷新基礎間隔、職員系統解鎖判定 |
| FT-12 Staff System | `IStaffService.GetRecruitRefreshReductionSec() : int` | 公會櫃臺職員 slot effect 折減秒數；未解鎖時固定回 0 |

### 2.4 下游被依賴系統

| 系統 | 依賴方式 |
| --- | --- |
| FT-10 Save/Load | 呼叫 `GetRookiePool()`、`GetVeteranPool()`、`GetNextAutoRefreshTimestamp()`、`GetFreeRefreshRemaining()`；透過 ISaveable 契約序列化 |
| P-02 Main UI | 呼叫 `GetRookiePool()`、`GetVeteranPool()`、`ManualRefresh()`、`RecruitRookie()`、`RecruitVeteran()`；訂閱 `OnPoolRefreshed` 事件 |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnPoolRefreshed` | FT-01 → EventBus（P-02 訂閱） | `void`（無 payload；訂閱者透過 `GetRookiePool()` / `GetVeteranPool()` 自行取池） | 自動刷新完成後、手動刷新成功後 |
| `OnRecruitSuccess` | FT-01 → EventBus（P-02、FT-10 訂閱） | `(int adventurerInstanceID, RecruitSource source)` | `RecruitRookie` / `RecruitVeteran` 成功完成後（`C-02.AddAdventurer` 已成功） |
| `OnDailyResetEvent` | F-02 → EventBus（FT-01 訂閱） | 空 payload | FT-01 訂閱以重置 `_freeRefreshRemaining` |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

招募是公會長最私人的時刻。新手池提供「打開公會大門，看見排隊年輕人」的期待感——每次刷新都是驚喜，玩家在職業/種族/特質組合中找到值得收留的人選。老手邀請則是有重量的經濟決策：花費數百金邀請一位 B 階冒險者前，玩家會猶豫、比較、最終獲得「公會又強了一分」的滿足。刷新機制製造「錯過感」：候選者不接就沒了，免費刷新用完後每次按下付費刷新都是微小但真實的代價。

### 3.2 系統目的還原

FT-01 作為招募入口，讓玩家透過資源消耗（金幣、聲望）與時間等待（刷新冷卻）來管控名冊成長速度，形成早中後期難度曲線。新手池降低入場門檻；老手池以費用+聲望形成雙重閘門，強化聲望系統的功能性。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 「打開公會大門看見新人」 | 刷新後出現 4 名不同職業/種族/特質組合的 F/E 階候選者 | `GeneratePool(rankPool={F,E}, poolSize=4)` + C-02/C-03/C-04/C-05 的具名模板優先 + 隨機填補 |
| 「每個人都獨一無二」 | 同批次不重複 `templateID`；具名 NPC 只在名冊不存在時出現 | `usedTemplateIDs` HashSet 批次去重 + `rosterTemplateIDs` isUnique 過濾 |
| 「錯過就沒了」 | 刷新後舊候選者消失，新候選者與舊列表完全不同 | 刷新時清空 `_rookiePool` / `_veteranPool` 後重新生成 |
| 「免費刷新用完才需要付費」 | `_freeRefreshRemaining` 計數顯示；耗盡後按鈕顯示金幣費用 | `_freeRefreshRemaining` 計數器 + `ManualRefresh()` 分支判斷 |
| 「老手邀請是重大投資」 | UI 顯示每位老手的 `cost` 與 `reputationReq`；按下後金幣實際扣除 | `RecruitCostTable.csv` 驅動 `cost` / `reputationReq`；`CanAfford` + `AddGold(-cost)` |
| 「老手階級有稀缺感」 | S 階約 3% 機率出現；公會低等時高階冒險者不出現 | `VeteranRankWeightTable.csv` 加權隨機 + FT-06 `GetMaxRecruitableRank()` 權重歸零過濾 |
| 「等待刷新有期待感」 | 倒數計時顯示下次自動刷新時間；建築升級縮短等待 | `_lastRefreshTimestamp` + `intervalSec = max(baseSec - reductionSec, MIN_RECRUIT_REFRESH_INTERVAL_SEC)` |
| 「離線回來池子是新的」 | 離線超過刷新期後重啟，立即看見新候選池 | `RestoreFromSave()` 後呼叫 `CheckAutoRefresh()` |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**是**（拆分為 3 個 Script）

### 4.2 拆分理由

FT-01 涵蓋以下三種不同職責，各自超出「單一職責」邊界：

1. **候選池生成邏輯**：具名模板優先篩選 + 隨機生成兩階段、老手加權隨機；純運算，無狀態，可獨立測試。預估 150~200 行。
2. **招募系統主控**：維護兩個候選池的 runtime 狀態、計時器、免費刷新次數；執行接納/邀請交易（含回滾）；訂閱事件；ISaveable 實作。若合併則單類別超過 500 行。
3. **資料型別（DTO + Enum）**：`RecruitCandidate`、`RecruitSource`；零行為，需明確隔離。

三職責合一預估總行數超過 500 行；GDD §3 已有明顯職責分區（池生成 vs 狀態管理 vs 資料型別），符合 §2.4 拆分標準。

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| FT-01-A | RecruitmentTypes | DTO、Enum 定義（零行為） | §3.1 |
| FT-01-B | RecruitmentPoolGenerator | 候選池生成（具名模板篩選 + 隨機生成 + 老手加權隨機） | §3.3、§3.4、§4.4、§4.5 |
| FT-01-C | RecruitmentService | 招募系統主控（計時器、接納/邀請交易、事件發布、ISaveable） | §3.2、§3.5、§3.6、§3.7、§4.1、§4.2、§4.3、§6.4 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `RecruitmentTypes.cs` | `Assets/Scripts/Gameplay/Recruitment/RecruitmentTypes.cs` | 宣告 `RecruitCandidate` DTO 與 `RecruitSource` enum，零行為 | — | < 40 行 |
| `RecruitmentPoolGenerator.cs` | `Assets/Scripts/Gameplay/Recruitment/RecruitmentPoolGenerator.cs` | 依照職責（新手/老手）生成候選者列表，執行具名模板篩選與加權隨機；具名模板透過 `IAdventurerFactory.CreateFromTemplate` 建立、隨機路徑由 Generator 抽 race/traits 後呼叫 `IAdventurerFactory.CreateRandomInstance` | **`IAdventurerFactory`**、`IAdventurerRoster`、`IProfessionService`、`IRaceService`、`ITraitService`、`IGuildCoreService`、`DataManager` | 200~280 行 |
| `RecruitmentService.cs` | `Assets/Scripts/Gameplay/Recruitment/RecruitmentService.cs` | 維護候選池 runtime 狀態、計時器、接納/邀請交易、事件發布、ISaveable | `ITimeSystem`、`IResourceService`、`IAdventurerRoster`、`IGuildBuildingService`、`IStaffService`、`EventBus`、`RecruitmentPoolGenerator`、`DataManager` | 350~450 行 |
| `RecruitmentEvents.cs` | `Assets/Scripts/Gameplay/Recruitment/Events/RecruitmentEvents.cs` | 集中宣告 typed event struct：`OnPoolRefreshedEvent`（空 struct）、`OnRecruitSuccessEvent { int adventurerInstanceID; RecruitSource source; }` | — | < 50 行 |

### 4.5 類別關係（可選）

```
RecruitmentService
  ├─ 持有 RecruitmentPoolGenerator（組合）
  ├─ 持有 List<RecruitCandidate> _rookiePool
  ├─ 持有 List<RecruitCandidate> _veteranPool
  └─ 實作 ISaveable

RecruitmentPoolGenerator
  └─ 回傳 List<RecruitCandidate>（使用 RecruitmentTypes.RecruitCandidate）

RecruitmentTypes（純資料）
  ├─ record/class RecruitCandidate
  └─ enum RecruitSource
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

以下為 `RecruitmentService` 對外暴露的介面（建議以 `IRecruitmentService` 封裝）：

| 方法簽名 | 說明 | 失敗回傳 |
| --- | --- | --- |
| `GetRookiePool() : IReadOnlyList<RecruitCandidate>` | 取得當前新手候選池 | — |
| `GetVeteranPool() : IReadOnlyList<RecruitCandidate>` | 取得當前老手候選池 | — |
| `RecruitRookie(int candidateID) : bool` | 接納新手；滿員回傳 `false` | `false` |
| `RecruitVeteran(int candidateID) : bool` | 邀請老手；滿員/金幣不足/聲望不足回傳 `false` | `false` |
| `ManualRefresh() : bool` | 手動刷新；付費時金幣不足回傳 `false` | `false` |
| `GetFreeRefreshRemaining() : int` | 剩餘免費手動刷新次數 | — |
| `GetNextAutoRefreshTimestamp() : long` | 下次自動刷新的 UTC Unix 秒 | — |

`RecruitmentPoolGenerator` 不對外公開（僅 `RecruitmentService` 持有）。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnPoolRefreshedEvent` | 發布（FT-01 → EventBus） | `struct OnPoolRefreshedEvent { }` | 自動刷新完成後、`ManualRefresh()` 成功後 |
| `OnRecruitSuccessEvent` | 發布（FT-01 → EventBus） | `struct OnRecruitSuccessEvent { int AdventurerInstanceID; RecruitSource Source; }` | `RecruitRookie` / `RecruitVeteran` 成功（`AddAdventurer` 已成功）後 |
| `OnDailyResetEvent` | 訂閱（F-02 → FT-01） | — | FT-01 訂閱以執行 `OnDailyReset()` 重置 `_freeRefreshRemaining` |

### 5.3 資料結構

```csharp
// RecruitmentTypes.cs
public class RecruitCandidate
{
    public int CandidateID;              // 候選池內唯一 ID，每次刷新重新分配
    public AdventurerInstance AdventurerInstance; // 預生成實例（尚未加入名冊）
    public int Cost;                     // 招募費用；新手池固定 0
    public int ReputationReq;            // 聲望門檻；新手池固定 0
}

public enum RecruitSource { Rookie, Veteran }

// VeteranRankWeightData（由 DataManager 自 VeteranRankWeightTable.csv 載入）
public class VeteranRankWeightData
{
    public string Rank;   // "D" / "C" / "B" / "A" / "S"
    public int Weight;    // 加權值（CSV 欄位）
}
```

### 5.4 內部資料流

**觸發點 A：自動刷新計時（F-02 OnSecondTick 訂閱）**

```
F02.OnSecondTick
  → RecruitmentService.OnSecondTick()
      ├─ now = ITimeSystem.NowUTC
      ├─ baseSec = IGuildBuildingService.GetRecruitRefreshInterval().TotalSeconds
      ├─ reductionSec = IStaffService.GetRecruitRefreshReductionSec()
      ├─ intervalSec = max(baseSec - reductionSec, MIN_RECRUIT_REFRESH_INTERVAL_SEC)
      ├─ if (now >= _lastRefreshTimestamp + intervalSec):
      │     ├─ ExecuteRefresh()
      │     └─ _lastRefreshTimestamp = now
      └─ （否則不執行）
```

**觸發點 B：手動刷新（UI 呼叫 ManualRefresh()）**

```
UI.ManualRefresh()
  → RecruitmentService.ManualRefresh() : bool
      ├─ if (_freeRefreshRemaining > 0):
      │     _freeRefreshRemaining -= 1
      │ else:
      │     if (!IResourceService.CanAfford(REFRESH_COST)): return false
      │     // FSD-Codex-Reoprts-260427 T1-FT01：檢查 AddGold 回傳值
      │     if (!IResourceService.AddGold(-REFRESH_COST)): return false  // 扣款失敗，不刷新
      ├─ ExecuteRefresh()  // 唯一 OnPoolRefreshedEvent 發布點
      ├─ _lastRefreshTimestamp = ITimeSystem.NowUTC
      // ※刪除原 EventBus.Publish — ExecuteRefresh 已發布，避免重複（T1-FT01 修正）
      └─ return true
```

**觸發點 C：接納新手（UI 呼叫 RecruitRookie）**

```
UI.RecruitRookie(candidateID)
  → RecruitmentService.RecruitRookie(candidateID) : bool
      ├─ candidate = _rookiePool.Find(candidateID)
      ├─ if (candidate == null): return false
      ├─ rosterCap = IGuildBuildingService.GetRosterCap()
      ├─ if (IAdventurerRoster.IsRosterFull(rosterCap)): return false（滿員）
      ├─ result = IAdventurerRoster.AddAdventurer(candidate.AdventurerInstance)
      ├─ if (result == true):
      │     _rookiePool.Remove(candidate)
      │     EventBus.Publish(new OnRecruitSuccessEvent { ..., Source=Rookie })
      │     return true
      └─ if (result == false):
            ├─ if (isUnique 衝突): _rookiePool.Remove(candidate); Debug.LogWarning
            └─ return false
```

**觸發點 D：邀請老手（UI 呼叫 RecruitVeteran）**

```
UI.RecruitVeteran(candidateID)
  → RecruitmentService.RecruitVeteran(candidateID) : bool
      ├─ candidate = _veteranPool.Find(candidateID)
      ├─ if (candidate == null): return false
      ├─ rosterCap = IGuildBuildingService.GetRosterCap()
      ├─ if (IAdventurerRoster.IsRosterFull(rosterCap)): return false（滿員）
      ├─ if (IResourceService.GetReputation() < candidate.ReputationReq): return false（聲望不足）
      ├─ if (!IResourceService.CanAfford(candidate.Cost)): return false（金幣不足）
      // FSD-Codex-Reoprts-260427 T1-FT01：檢查 AddGold 回傳值
      ├─ if (!IResourceService.AddGold(-candidate.Cost)): return false（扣款失敗，候選池不變）
      ├─ result = IAdventurerRoster.AddAdventurer(candidate.AdventurerInstance)
      ├─ if (result == true):
      │     _veteranPool.Remove(candidate)
      │     EventBus.Publish(new OnRecruitSuccessEvent { ..., Source=Veteran })
      │     return true
      └─ if (result == false):
            ├─ IResourceService.AddGold(+candidate.Cost)    // 回滾扣款（成功路徑保證）
            ├─ if (isUnique 衝突): _veteranPool.Remove(candidate); Debug.LogWarning
            └─ return false
```

**觸發點 E：每日重置（F-02 OnDailyResetEvent 訂閱）**

```
F02.OnDailyResetEvent
  → RecruitmentService.OnDailyReset()
      └─ _freeRefreshRemaining = DAILY_FREE_REFRESH    // 重置為 1
```

**ExecuteRefresh()（內部）**

```
ExecuteRefresh()
  ├─ rosterTemplateIDs = IAdventurerRoster.GetRoster().Select(a => a.TemplateID).ToHashSet()
  ├─ _rookiePool = RecruitmentPoolGenerator.GeneratePool(rankPool={F,E}, poolSize, rosterTemplateIDs)
  ├─ _veteranPool = RecruitmentPoolGenerator.GeneratePool(rankPool={D~S}, poolSize, rosterTemplateIDs)
  └─ EventBus.Publish(new OnPoolRefreshedEvent())  // 僅在 ExecuteRefresh 內部發布（手動刷新 B 另外發布→重複；需確認：移至 ExecuteRefresh 統一發布，ManualRefresh 不重複發布）
```

> 注意：`ExecuteRefresh()` 統一負責發布 `OnPoolRefreshedEvent`；`ManualRefresh()` 呼叫 `ExecuteRefresh()` 後不再重複發布，§4.2 偽碼已修正。

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `VeteranRankWeightTable.csv` | `rank`、`weight` | `【FT-01-DS】veteran-rank-weight-table.md` | 老手候選者階級加權隨機（`RollVeteranRank`） | `DataManager` 啟動時統一載入（F-01 初始化階段） |
| `RecruitCostTable.csv` | `rank`、`cost`、`reputationReq` | `【C-02-DS】recruit-cost-table.md` | 老手候選者的費用與聲望門檻填入 `RecruitCandidate` | `DataManager` 啟動時統一載入 |
| `SystemConstants.csv` | `RECRUIT_POOL_SIZE`、`DAILY_FREE_REFRESH`、`REFRESH_COST`、`MIN_RECRUIT_REFRESH_INTERVAL_SEC` | `【F-01-DS】system-constants.md` | 池大小、免費刷新次數、付費刷新費用、刷新間隔下限 | `DataManager` 啟動時統一載入 |

### 6.2 引用的 ScriptableObject

無。本系統全部資料來自 CSV，不使用 ScriptableObject。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `RECRUIT_POOL_SIZE`（每批候選池大小） | `SystemConstants.csv`：`RECRUIT_POOL_SIZE` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `DAILY_FREE_REFRESH`（每日免費刷新次數） | `SystemConstants.csv`：`DAILY_FREE_REFRESH` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `REFRESH_COST`（付費手動刷新費用） | `SystemConstants.csv`：`REFRESH_COST` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `MIN_RECRUIT_REFRESH_INTERVAL_SEC`（自動刷新間隔下限秒數） | `SystemConstants.csv`：`MIN_RECRUIT_REFRESH_INTERVAL_SEC` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 老手各階級招募費用（D~S `cost`） | `RecruitCostTable.csv`：`cost` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 老手各階級聲望門檻（D~S `reputationReq`） | `RecruitCostTable.csv`：`reputationReq` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 老手各階級出現權重（D:40 / C:30 / B:18 / A:9 / S:3） | `VeteranRankWeightTable.csv`：`weight` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 招募廣告欄各等級刷新間隔（L1=86400s / L2=57600s / L3=28800s） | FT-07 `BuildingTable.csv`（buildingID=2） | 對應「四、程式實作原則」第 9 條：參數表格化 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| §5.1：符合條件的具名模板超過 `poolSize` | `availableTemplates.Shuffle()` 後取前 `poolSize` 個 | `RecruitmentPoolGenerator` | EditMode 測試：模板數 > poolSize 時，候選池恰好 4 人，無重複 templateID |
| §5.1：符合條件的具名模板為 0 個 | 進入 Phase 2 全數隨機生成，不報錯 | `RecruitmentPoolGenerator` | EditMode 測試：空模板 CSV 時，候選池 4 人全為隨機生成（`templateID == 0`） |
| §5.1：老手池所有階級權重歸零（`maxRecruitableRank` 低於 D，實務不發生） | `Debug.LogError`，fallback 生成 D 階 | `RecruitmentPoolGenerator` | EditMode 測試：stub `GetMaxRecruitableRank()` 回 "X"（不合法值），候選池全為 D 階 |
| §5.1：隨機生成時 `GetBaseProfessions()` 為空 | `Debug.LogError`，早退，池可能少於 `poolSize` | `RecruitmentPoolGenerator` | EditMode 測試：stub `GetBaseProfessions()` 回空列表，候選池 0 人，無例外丟出 |
| §5.1：`RollRace` 回傳 fallback（raceID=1） | 正常接受，不報錯（C-04 內部已報錯） | `RecruitmentPoolGenerator` | EditMode 測試：stub `RollRace()` 回 1，候選者 raceID == 1，流程無例外 |
| §5.2：名冊已滿時嘗試接納/邀請 | `IsRosterFull()` 為 `true` 時直接回傳 `false`，不執行後續步驟 | `RecruitmentService` | EditMode 測試：stub `IsRosterFull()` 回 `true`，兩個方法均回傳 `false` |
| §5.2：老手邀請金幣不足 | `CanAfford(cost)` 為 `false` 時回傳 `false`，不扣款、不移除候選者 | `RecruitmentService` | EditMode 測試：金幣 0，`RecruitVeteran` 回傳 `false`，候選池大小不變 |
| §5.2：老手邀請聲望不足 | `GetReputation() < reputationReq` 時回傳 `false`，不扣款 | `RecruitmentService` | EditMode 測試：聲望 0，`reputationReq = 20`，回傳 `false` |
| §5.2：`CanAfford` 通過但 `AddGold` 失敗（極端情況） | `Debug.LogError`，回傳 `false`，不加入名冊，**不回滾**（尚未呼叫 `AddAdventurer`） | `RecruitmentService` | EditMode 測試：stub `AddGold` 丟例外，候選者保留，金幣由 F-03 內部處理一致性 |
| §5.2：`AddAdventurer` 失敗後老手邀請需回滾扣款 | 呼叫 `IResourceService.AddGold(+candidate.Cost)` 回滾，依 isUnique 衝突決定是否移除候選者，`Debug.LogWarning` | `RecruitmentService` | EditMode 測試：stub `AddAdventurer()` 回 `false`，金幣回到扣款前值 |
| §5.2：扣款會跨過破產閾值 | 使用 `AddGold`（非 `AddGoldAllowBankruptcy`），F-03 規則 2 拒絕；回傳 `false`，不扣款 | `RecruitmentService` | EditMode 測試：金幣 50，cost=100，`RecruitVeteran` 回傳 `false`（CanAfford 已攔截）；若 CanAfford 與 bankruptcy 門檻不同步，F-03 AddGold 回 `false`，FT-01 視為失敗 |
| §5.2：接納/邀請後池中剩餘候選者不足 | 不自動補充，等待下次刷新 | `RecruitmentService` | EditMode 測試：接納後候選池 3 人，無自動補充行為 |
| §5.3：付費手動刷新金幣不足 | `CanAfford(REFRESH_COST)` 失敗，回傳 `false`，不刷新，不消耗免費次數 | `RecruitmentService` | EditMode 測試：`_freeRefreshRemaining = 0`，金幣 0，`ManualRefresh()` 回傳 `false` |
| §5.3：離線超過多個刷新週期（如離線 72h，週期 24h） | `RestoreFromSave()` 後呼叫一次 `CheckAutoRefresh()`；僅執行一次刷新，不補算 | `RecruitmentService` | EditMode 測試：`_lastRefreshTimestamp = NowUTC - 3 * intervalSec`，`CheckAutoRefresh()` 一次執行後 `_lastRefreshTimestamp` 更新為 now |
| §5.3：離線跨越每日重置時間 | F-02 重啟時發布 `OnDailyResetEvent`，FT-01 訂閱後重置 `_freeRefreshRemaining` | `RecruitmentService` | EditMode 測試：stub F-02 發布 `OnDailyResetEvent`，`GetFreeRefreshRemaining()` == 1 |
| §5.3：手動刷新與自動刷新幾乎同時觸發 | 手動刷新重置 `_lastRefreshTimestamp = now`；下一個 `OnSecondTick` 的 `CheckAutoRefresh()` 判定未到期，不重複刷新 | `RecruitmentService` | EditMode 測試：手動刷新後立即呼叫 `CheckAutoRefresh()`，不觸發第二次刷新 |
| §5.3：職員系統未解鎖（`IsStaffSystemUnlocked() == false`） | `GetRecruitRefreshReductionSec()` 固定回 0；`intervalSec = baseSec` | `RecruitmentService` | EditMode 測試：stub `IsStaffSystemUnlocked()` 回 `false`，stub `GetRecruitRefreshReductionSec()` 回 0，`intervalSec == baseSec` |
| §5.3：FT-12 加成超過 `baseSec - MIN_RECRUIT_REFRESH_INTERVAL_SEC` | `intervalSec = max(baseSec - reductionSec, MIN_RECRUIT_REFRESH_INTERVAL_SEC)` 截斷 | `RecruitmentService` | EditMode 測試：`baseSec = 3600`，`reductionSec = 3000`，`intervalSec == 3600`（下限） |
| §5.4：需序列化的狀態 | `_lastRefreshTimestamp`、`_freeRefreshRemaining`、`_rookiePool`、`_veteranPool` 實作 `ISaveable.Serialize()` | `RecruitmentService` | EditMode 測試：序列化後反序列化，四個欄位值與原始一致 |
| §5.4：載入後 `templateID` 在 CSV 找不到 | 保留候選者資料，`templateID` 標記為 0，`AdventurerInstance` 已快照完整欄位 | `RecruitmentService` | EditMode 測試：修改 CSV 移除某 templateID，載入後該候選者 `templateID == 0`，其他欄位完整 |
| §5.4：載入後立即 `CheckAutoRefresh()` 可能覆蓋存檔池 | 若離線超過刷新期則覆蓋（符合設計預期）；若未超過則保留存檔池 | `RecruitmentService` | EditMode 測試：`_lastRefreshTimestamp = NowUTC - intervalSec / 2`，`RestoreFromSave()` 後候選池為存檔池 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 `RecruitCandidate` 資料結構 | §5.3 | 對齊 | |
| §3.1 `RecruitSource` enum | §5.3 | 對齊 | |
| §3.2 自動刷新計時（建築等級驅動、FT-12 減量、MIN 下限） | §5.4 觸發點 A | 對齊 | |
| §3.2 手動刷新（免費次數消耗 → 付費） | §5.4 觸發點 B | 對齊 | |
| §3.2 手動刷新重置自動刷新計時器 | §5.4 觸發點 B | 對齊 | |
| §3.2 每日 00:00 重置免費刷新次數 | §5.4 觸發點 E | 對齊 | |
| §3.2 離線期間不補算中間錯過週期 | §7 §5.3 邊緣案例 | 對齊 | |
| §3.3 新手池生成（F/E 50/50、poolSize=4） | §5.4 ExecuteRefresh | 對齊 | |
| §3.3 具名模板優先（isUnique=1 過濾名冊、isUnique=0 批次去重） | §5.4 ExecuteRefresh；§7 §5.1 | 對齊 | |
| §3.4 老手池生成（加權隨機、公會等級限制） | §5.4 ExecuteRefresh；§8.2 公式對齊 | 對齊 | |
| §3.4 `RecruitCostTable` 填入 `cost` / `reputationReq` | §6.1 | 對齊 | |
| §3.5 新手接納流程（`AddAdventurer` 成功才移除候選者） | §5.4 觸發點 C | 對齊 | |
| §3.5 老手邀請流程（聲望 + 金幣雙重驗證，先扣款再 AddAdventurer） | §5.4 觸發點 D | 對齊 | |
| §3.5 老手邀請 `AddAdventurer` 失敗時回滾扣款 | §5.4 觸發點 D；§7 §5.2 | 對齊 | |
| §3.6 查詢 API 簽名 | §5.1 | 對齊 | |
| §3.7 `OnPoolRefreshed` / `OnRecruitSuccess` 事件契約 | §5.2 | 對齊 | |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 對應 | 是否等價 |
| --- | --- | --- |
| §4.1 `CheckAutoRefresh()` | §5.4 觸發點 A | 等價。`intervalSec = max(baseSec - reductionSec, MIN_RECRUIT_REFRESH_INTERVAL_SEC)` 計算方式與 GDD 完全一致，觸發條件 `now >= _lastRefreshTimestamp + intervalSec` 一致 |
| §4.2 `ManualRefresh()` | §5.4 觸發點 B | 等價。分支（免費次數 > 0 / == 0 + 付費）完全對應；`ExecuteRefresh()` 統一發布 `OnPoolRefreshedEvent`，ManualRefresh 不另行發布（詳見 §5.4 注意事項） |
| §4.3 `OnDailyReset()` | §5.4 觸發點 E | 等價 |
| §4.4 `RollVeteranRank()` | §5.4 ExecuteRefresh；`RecruitmentPoolGenerator` | 等價。從 CSV 讀取 `weight`，`RANK_INDEX` helper 過濾超出 `maxRecruitableRank` 的階級，權重歸零後重新正規化，加權線性掃描 |
| §4.5 `GeneratePool()` | §5.4 ExecuteRefresh；`RecruitmentPoolGenerator` | 等價。Phase 1 具名模板篩選（Shuffle → 批次去重）+ Phase 2 隨機填補；`GetBaseProfessions()` 空時早退 |

**`OnPoolRefreshedEvent` 發布點說明**：GDD §4.2 偽碼在 `ManualRefresh()` 結尾發布，GDD §4.1（自動刷新）在 `ExecuteRefresh()` 內發布。FSD 改為在 `ExecuteRefresh()` 統一發布，以避免手動刷新路徑重複發布。兩條路徑最終發布次數均為 1，語義等價。此偏差已登記 §8.4 回註。

### 8.3 未能實現的規則與修改建議

**B-01（建議項，不阻礙實作）**：GDD §3.7 訂閱者清單包含「P-03 Notification（可選 toast）」，但 P-03 尚未設計（GDD 狀態為「待設計」）。`OnRecruitSuccess` 事件已透過 EventBus 發布，P-03 未來直接訂閱即可，無需修改 FT-01 程式碼。

**B-02（建議項，不阻礙實作）**：`AdventurerRankUtil.RankIndex(string)` 為 §4.4 `RollVeteranRank` 所需的階級序數 helper，GDD 僅說明「專案共用 helper」，未指定實作位置。建議由 C-02 FSD 撰寫時確認 `AdventurerRankUtil` 的歸屬 Script 路徑，FT-01 直接引用。若 C-02 未提供，FT-01 `RecruitmentPoolGenerator` 可自行內部宣告靜態方法。

**B-03（CT-05 裁決，已修正）**：FSD-Codex-Reoprts-260427 CT-05 裁決：`CreateFromTemplate` / `CreateRandomInstance` 屬 `IAdventurerFactory`，不屬 `IAdventurerRoster`；`BuildTraitList` 為 Factory 內部 helper，不對外公開。FT-01 已於 §2.3 / §4.4 改依賴 `IAdventurerFactory`，移除原先誤掛在 `IAdventurerRoster.BuildTraitList` 的依賴。trait group 來源規則：(a) 具名模板路徑由 `IAdventurerFactory.CreateFromTemplate(templateID)` 內部依 `template.fixedTraitIDs` + `template.randomTraitGroupIDs` 組合；(b) 隨機路徑由 `RecruitmentPoolGenerator` 呼叫 `ITraitService.GetProfessionGroups(professionID)` 取群組、`ITraitService.RollTraits(group)` 抽特質後傳入 `IAdventurerFactory.CreateRandomInstance`。

**Prerequisite — 依賴未完成 FSD（FSD-Codex-Reoprts-260427 CT-10）**

本 FSD 對以下系統的 API / 事件契約引用，目前**尚未經對方 FSD 雙向驗證**，視為 stub 契約：

- FT-06 Guild Core（FSD 未存在）
- FT-07 Guild Building（FSD 未存在）
- FT-10 Save/Load（FSD 未存在）
- FT-12 Staff System（FSD 未存在）
- P-02 Main UI（FSD 未存在）
- P-03 Notification System（FSD 未存在）

實作時須以 mock / stub 介面驗證；對方 FSD 完成後須回頭做雙向對齊複查。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-27 | `【FT-01】adventurer-recruitment.md` | §4.2 `ManualRefresh()` | FSD 回註：`OnPoolRefreshedEvent` 改由 `ExecuteRefresh()` 統一發布，`ManualRefresh()` 不另行發布；語義等價，避免重複發布。 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 2026-04-27 | 無真實衝突。時間單位：GDD 使用「h」（小時）表示刷新間隔，符合全域規範（秒/小時），非衝突。FT-12 `GetRecruitRefreshReductionSec()` 回傳秒數與 systems-index 依賴圖標記為「FT-01 依賴 FT-12」而非 FT-08，無衝突（FT-08 = Gacha，FT-12 = Staff）。 | — | 無須裁決 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：每條皆可被 EditMode／PlayMode 測試或手動步驟驗證（AC-AR-01 ~ AC-AR-19）
- [x] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [x] §3.3 對映表：8 組幻想／目的各有對應技術手段
- [x] §4.1~§4.4：拆分判斷有結論（是，3 Script）；Script 清單欄位齊全（路徑、SRP、依賴介面、預估規模）
- [x] §5.1~§5.4：API 簽名、事件 payload、資料結構、資料流偽碼（5 個觸發點 + ExecuteRefresh）齊備
- [x] §6.1~§6.3：引用 CSV 含對應 Data-Specs（3 表）；嚴禁寫死清單 8 項對齊原則第 9 條
- [x] §7：GDD §5.1~§5.4 全部 19 條邊緣案例皆有對策（含涉及 Script 與驗證方式）
- [x] §8.1：對齊清單覆蓋 GDD §3.1~§3.7 全條目（二層粒度）
- [x] §8.2~§8.5：公式對齊 5 條／建議項 2 條／GDD 回註 1 條／衝突紀錄 1 條（無真實衝突）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（見本次更新）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | unity-specialist subagent（自檢） | 通過 | 通過 | 通過 | 無真實衝突；B-01（P-03 待設計，不阻礙）；B-02（`AdventurerRankUtil` 歸屬建議 C-02 FSD 確認）；§4.2 發布點統一至 `ExecuteRefresh()` 已回註 GDD |
