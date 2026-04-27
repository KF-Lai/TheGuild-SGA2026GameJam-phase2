# 【FT-07-FSD】功能規格說明書 — Guild Building System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【FT-07】guild-building-system.md`（版本：2026-04-27 通過 `/design-review`） |
| 對應 Data-Specs | _待建（`【FT-07-DS】building-table.md`，CSV：`BuildingTable.csv`，owner = FT-07）_ |
| 撰寫者 | Claude Code 主體（直接撰寫，無 subagent） |
| Review 者 | Claude Code 主體（自檢） |
| 狀態 | 審查中（修正後第二輪自檢） |
| 最近更新 | 2026-04-28 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

FT-07 Guild Building System 統一管理 6 棟建築（委託板、招募廣告欄、公會大廳、公會櫃臺、預備金保險櫃、職員休息室）的等級狀態與升級流程，並透過同步查詢 API 對外暴露建築效果值（委託槽數、招募刷新間隔、名冊上限、並行任務上限、破產倒數秒數、職員系統解鎖旗標）。本 FSD 同時涵蓋雙軌閘門（金幣 + 聲望）升級判定、保險櫃倒數秒數啟動／升級主動推送至 F-03、ISaveable 序列化與還原、Phase 2 設施維護費管線封鎖（Jam 版 no-op）。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**

- `BuildingState[6]` 記憶體狀態維護（buildingID 1~6 各一筆 `currentLevel`）。
- `BuildingTable.csv` 載入、複合主鍵 `(buildingID, level)` 校驗。
- 三閘門升級流程（已達上限 / 公會等級 / 金幣）與三種失敗碼回傳。
- 7 個對外查詢 API（§5.1 列）；同步即時讀表，不快取。
- `OnBuildingUpgraded` 事件發布。
- 保險櫃（buildingID=5）升級／啟動時主動呼叫 `ResourceManagement.SetBankruptcyWarningDuration`。
- ISaveable 契約（OwnerKey=`ft07Buildings`、IsCritical=false）：`Serialize` / `RestoreFromSave` / `InitializeAsNewGame`。

**Out-of-Scope**

- 建築視覺呈現、升級動畫、UI 按鈕狀態管理（P-02）。
- 面試 gacha 抽卡邏輯（FT-08）、職員實例運營（FT-12）。
- 破產倒數計時實作（F-03，僅讀取本系統推送的秒數）。
- 維護費 runtime 計算與 `OnGuildMaintenanceDue` 發布（Phase 2，Jam 版封鎖）。
- 玩家提前解鎖／拆除建築（無此規則）。

### 1.3 完成目標（Definition of Done）

| 編號 | 完成條件 | 對應 GDD AC |
| --- | --- | --- |
| DoD-1 | 新遊戲啟動後 `GetBuildingLevel(1..5)==1`、`GetBuildingLevel(6)==0`，6 個效果 API 回傳 §3.5 L1（buildingID=6 為 L0 對應值） | AC-1、AC-2 |
| DoD-2 | 任一建築金幣足、聲望足時 `TryUpgradeBuilding` 回傳 `SUCCESS`，等級 +1、金幣扣除、`OnBuildingUpgraded` 發出一次 | AC-3 |
| DoD-3 | 金幣不足／已滿級時回傳對應失敗碼，狀態不變 | AC-4、AC-5 |
| DoD-4 | 公會 Lv2 嘗試升至 L3 回傳 `GUILD_LEVEL_INSUFFICIENT`；升至 L2 不觸發聲望閘 | AC-6、AC-7、AC-8 |
| DoD-5 | 升級後同 frame 查詢 API 立即回傳新值（如委託板 L3 → `GetMissionSlotCount()==11`、職員休息室 L1 → `IsStaffSystemUnlocked()==true`、保險櫃 L2 → `GetBankruptcyWarningSeconds()==21600`） | AC-9、AC-10、AC-11 |
| DoD-6 | UI 雙擊不重複扣款；CSV 缺行 DataManager 載入失敗；存檔超出 maxLevel 時 clamp + LogWarning | AC-12、AC-13、AC-14 |
| DoD-7 | 存讀檔後所有建築等級與效果 API 還原一致 | AC-15 |
| DoD-8 | `Start()` 完成後 `ResourceManagement.GetBankruptcyWarningDuration()==10800`；保險櫃升級每級觸發推送；非 buildingID=5 升級不推送；存檔載入後依存檔等級推送；**listener 訂閱 `BuildingUpgradedEvent`、buildingID=5 升級時於回呼當下呼叫 `GetBankruptcyWarningSeconds()`，必須已是新等級對應秒數（驗 §5.4.3 步驟 8/9 順序）** | AC-16、AC-17、AC-18、AC-19 |
| DoD-9 | Jam 版 `OnDailyReset` 觸發時 FT-07 不發布 `OnGuildMaintenanceDue`、不執行維護費計算；`maintenanceCost` 欄位僅做存在性驗證 | AC-20、AC-21 |

DoD-1~DoD-9 皆可由 EditMode／PlayMode test 或 Unity Editor 手動步驟驗證；測試載具於 §7 邊緣案例對策末欄列出。

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要、§2 玩家幻想 → 本 FSD §3.1、§3.2 還原。
- §3.1 建築狀態、§3.2 六棟建築定義 → §5.3、§6.1。
- §3.3 升級流程 → §5.1、§5.4。
- §3.4 效果值查詢 API → §5.1。
- §3.5 建築效果數值表 → §6.1（資料表化）。
- §3.6 事件發布契約 → §5.2。
- §3.7 與 FT-06 的職責切割 → §2.3 上游表備註。
- §3.8 設施維護費管線（Phase 2 封鎖） → §5.4 的 Phase 2 區段、§7 案例對策。
- §4.1~§4.5 公式 → §5.4 偽碼直接採用，無替代（§8.2 登記）。
- §5.1~§5.7 邊緣案例 → §7 逐項對策。
- §6 依賴關係 → §2.3、§2.4、§2.5。
- §6.5 ISaveable 持久化契約 → §5.1、§5.3、§5.4。
- §7.1 BuildingTable.csv schema → §6.1、§6.3。
- §8.1~§8.8 驗收標準 → §1.3 DoD 表逐條對齊。

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【FT-07-DS】building-table.md`（_待建_） | `BuildingTable.csv` | `buildingID`、`name`、`maxLevel`、`level`、`effectValue`、`upgradeCost`、`guildLevelReq`、`slotCount`、`maintenanceCost` | 6 棟建築每等級的效果值、費用、聲望閘、職員 slot capacity、Phase 2 維護費；FT-07 為 owner，FT-08 / FT-12 為消費端 |

### 2.3 上游依賴系統

| 上游系統 | 介面（FSD 敘述） | 實作契約（§2.10 對映） | 用途 |
| --- | --- | --- | --- |
| F-01 DataManager | `IDataManager.GetTable<BuildingRow>()` | `DataManager.Instance.GetTable<BuildingRow>()` | 啟動時取得 `BuildingTable` 全行資料 |
| F-03 Resource Management | `IResourceService.GetGold()` | `ResourceManagement.Instance.GetGold()` | 升級時金幣閘 |
| F-03 Resource Management | `IResourceService.AddGold(int)` | `ResourceManagement.Instance.AddGold(-cost)` | 扣升級費用（嚴格不允許進入負值；GDD §5.2） |
| F-03 Resource Management | `IResourceService.SetBankruptcyWarningDuration(int)` | `ResourceManagement.Instance.SetBankruptcyWarningDuration(seconds)` | 啟動／保險櫃升級時推送倒數秒數（GDD §6.2.1） |
| FT-06 Guild Core | `IGuildCoreService.GetCurrentLevel()` | `GuildCoreService.Instance.GetCurrentLevel()` | 升級時聲望閘 |
| F-02 Time System（**Phase 2，Jam 版不訂閱**） | `EventBus.Subscribe<OnDailyResetEvent>` | static `EventBus.Subscribe<OnDailyResetEvent>` | 維護費管線觸發點（GDD §3.8.1） |

> **Service 介面命名規範對齊**：依 FSD-index §2.10 方案 A，FSD 敘述保留 `IXxxService` 命名，實作 PR 直接呼叫 concrete singleton。本 FSD 的 `IBuildingService.cs`（§4.4）作為 FSD 層介面契約，實作以 `BuildingService.Instance` 暴露。

### 2.4 下游被依賴系統

| 下游系統 | 查詢 API / 事件 | 用途 |
| --- | --- | --- |
| FT-01 Adventurer Recruitment | `GetRosterCap()` / `GetRecruitRefreshInterval()` | 招募名冊上限、候選池刷新計時 |
| FT-02 Mission Dispatch | `GetMaxConcurrentMissions()` | 同時派遣上限閘 |
| FT-08 Gacha System | `IsStaffSystemUnlocked()` / `GetBuildingLevel(6)` | 整體 gacha 啟停閘、面試自動刷新間隔階梯 |
| FT-12 Staff System | `IsStaffSystemUnlocked()` / `GetBuildingLevel(6)` / `BuildingTable[buildingID].slotCount` | 整體職員運營啟停閘、名冊容量、slot 指派 capacity（直接讀 CSV） |
| F-03 Resource Management | `SetBankruptcyWarningDuration(int)`（被推送） | 接收 FT-07 推送的破產倒數秒數，不主動查 FT-07 |
| P-02 Main UI Framework | `GetBuildingLevel(id)` / 全部效果 API / `OnBuildingUpgraded` | 建築管理畫面、升級按鈕 enable／disable、升級完成 UI 更新 |
| P-03 Notification System | `OnBuildingUpgraded`（待 Log API 更新） | 升級完成桌面推播 |
| FT-10 Save/Load System | ISaveable 契約 `Serialize` / `RestoreFromSave` / `InitializeAsNewGame` | 序列化 6 棟 `currentLevel`，OwnerKey=`ft07Buildings` |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布／訂閱時機 |
| --- | --- | --- | --- |
| `OnBuildingUpgraded` | 出 | `int buildingID, int fromLevel, int toLevel` | FT-07 在 `TryUpgradeBuilding` 升等成功後、`SetBankruptcyWarningDuration` 推送之後發布；P-02 / P-03 訂閱 |
| `OnGuildMaintenanceDue` | 出（**Phase 2 不發布**） | `DateTime dueTimestamp, IReadOnlyDictionary<int,int> perBuildingCost, int totalAmount` | Phase 2 啟用後於 `OnDailyResetEvent` 觸發時發布；FT-05 / P-02 / P-03 訂閱；Jam 版 listener 收到 0 次調用（AC-20） |
| `OnDailyResetEvent` | 入（**Phase 2 才訂閱**） | `DateTime resetTimestamp` | 來自 F-02；Jam 版 FT-07 **不訂閱**，Phase 2 啟用維護費時才訂閱（GDD §3.8 開頭封鎖宣告） |

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

公會剛開張時委託板只貼得下 5 張任務、大廳容得下 10 個冒險者；玩家透過任務傭金累積金幣，逐步把每棟建築一級一級擴建，看見委託板貼滿、候選池排滿、破產壓力被保險櫃緩衝撐住——「這間公會是我一塊磚一塊磚蓋起來的」（GDD §2 玩家幻想敘事）。情緒節點：首次升級的投資感、L2 的掌控感擴張、Lv3 聲望閘的目標感、職員休息室建造解鎖新玩法的驚喜、保險櫃 L5 的後期從容。

### 3.2 系統目的還原

FT-07 是公會建築升級的單一管理權威，提供 6 棟建築的等級狀態、雙軌升級閘門（金幣 + 聲望）與 7 個查詢 API。下游（FT-01、FT-02、F-03、FT-08、FT-12、P-02）統一從本系統讀取建築效果值，不自行維護建築狀態副本；保險櫃倒數秒數採「FT-07 主動推送、F-03 被動接收」模式，避免 F-03 反向依賴 FT-07（對齊 systems-index 依賴規則）。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 投資感（首次升級） | 玩家點擊「升級」按鈕 → 金幣立即扣除 → 委託板槽位即刻從 5 變 8 | `TryUpgradeBuilding` 同步流程、`OnBuildingUpgraded` 事件、查詢 API 不快取直接讀表 |
| 量的改變（不是數字堆疊） | 委託板 5→8→11→14→17、大廳 10→15→20→25→30 | `BuildingTable[(id,level)].effectValue` 階梯式跳變，數值寫於 CSV |
| 聲望閘里程碑（公會在長大） | 升 L3 必須先達 Guild Lv3，否則按鈕呈現灰色 | `guildLevelReq`欄位 + `GuildCoreService.GetCurrentLevel()` 比對；`CanUpgrade` 回 false → P-02 disable 按鈕 |
| 資源決策的重量 | 升委託板還是大廳？先擴容還是提速？金幣有限 | 6 棟建築共享同一金幣池（F-03），`AddGold(-cost)` 嚴格扣款不允許債務 |
| 職員休息室的驚喜（解鎖新玩法） | 建造後 `IsStaffSystemUnlocked()` 翻 true → FT-08 / FT-12 開通 | buildingID=6 初始 currentLevel=0 / `GetBuildingLevel(6)>=1` 判定 |
| 後期從容（保險櫃 L5 = 48h） | 玩家負債後仍有 48 小時翻身時間 | 保險櫃升級即時推送 `SetBankruptcyWarningDuration(172800)` 至 F-03 |
| 雙軌閘門（金幣軸 / 聲望軸） | L1→L2 不卡聲望、L3+ 卡聲望 | CSV 設計：L2 `guildLevelReq=0` 恆通過；L3+ `guildLevelReq=3/4/5` |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**。

### 4.2 拆分理由

對齊 FSD-index §2.4 拆分標準：

- **預估規模**：4 Script 合計 600~770 行，單一 Script 最大 `BuildingService.cs` 約 350~450 行，未超過 500 行門檻。
- **職責邊界**：GDD §3.1~§3.8 各小節皆圍繞「建築狀態 + 升級流程 + 效果查詢 + 推送 + 持久化」單一聚合根 `BuildingState[]`，無明顯可分割的職責分區（不同於 FT-02 有 dispatch 與 commission-board 兩個明顯子系統）。
- **原子性**：升級流程（閘門檢查 → 扣金幣 → 升等 → 事件 → 推送）為單次原子操作，拆分會造成跨 Script 狀態同步成本上升。
- **參考既有 FSD**：C-06 World Danger（5 Script、未拆分）、FT-06 Guild Core（5 Script、未拆分）規模相近處理方式一致。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `BuildingTypes.cs` | `Assets/Scripts/Gameplay/Building/BuildingTypes.cs` | 定義 `BuildingRow`（CSV row DTO）、`BuildingState`（runtime 狀態 DTO）、`UpgradeResult`（enum）、`BuildingMaintenanceDuePayload`（Phase 2）、`BuildingUpgradedEvent`（payload struct）等資料結構與常數 | UnityEngine | 80~120 行 |
| `IBuildingService.cs` | `Assets/Scripts/Gameplay/Building/IBuildingService.cs` | 對外服務介面：升級／查詢 API 簽名 + ISaveable 衍生 | UnityEngine、`IDataManager`（型別文件用） | 50~80 行 |
| `BuildingTableLoader.cs` | `Assets/Scripts/Gameplay/Building/BuildingTableLoader.cs` | 從 `DataManager` 取得 `BuildingTable` 全行，建立 `Dictionary<(int,int), BuildingRow>` 雙鍵索引；驗證 6 個 buildingID 與每棟 level=0..maxLevel 完整存在；提供 `GetRow(id, level)` / `GetMaxLevel(id)` / `GetMinLevel(id)` 查詢 | `IDataManager` | 100~150 行 |
| `BuildingService.cs` | `Assets/Scripts/Gameplay/Building/BuildingService.cs` | MonoBehaviour singleton；持有 `BuildingState[6]`；實作 7 個查詢 API、`TryUpgradeBuilding`、ISaveable（`Serialize` / `RestoreFromSave` / `InitializeAsNewGame`）、保險櫃推送、Phase 2 `OnDailyReset` 訂閱（封鎖中） | `IBuildingService`、`IDataManager`、`IResourceService`、`IGuildCoreService`、`EventBus` | 350~450 行 |

- 路徑前綴採 Unity Asset Database 慣例（FSD-index §5.4），不含 `TheGuild-unity/` 專案根。
- `BuildingTypes.cs` 的 `BuildingState` 為 runtime 可變 struct（與 GDD §3.1 對齊）；`BuildingRow` 為 immutable record / class（CSV row）。
- `BuildingService.Instance` 對外為 singleton；ISaveable 註冊由 FT-10 SaveLoadCoordinator 在 Awake 階段透過 `OwnerKey="ft07Buildings"` 收集。

### 4.5 類別關係

```
BuildingService (MonoBehaviour, singleton)
  ├─ IBuildingService（implements）
  ├─ ISaveable（implements）
  ├─ uses BuildingTableLoader（composition；Awake 中建立）
  │     └─ uses IDataManager
  ├─ uses IResourceService（GetGold / AddGold / SetBankruptcyWarningDuration）
  ├─ uses IGuildCoreService（GetCurrentLevel）
  ├─ holds BuildingState[6]（runtime 狀態）
  └─ publishes via EventBus
        └─ BuildingUpgradedEvent
        └─ BuildingMaintenanceDueEvent（Phase 2 only；Jam 不發）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

`IBuildingService`（FSD 敘述；實作為 `BuildingService.Instance`）：

| API | 簽名 | 用途 |
| --- | --- | --- |
| `GetBuildingLevel` | `int GetBuildingLevel(int buildingID)` | 回傳指定建築當前等級；buildingID 不在 1~6 → 拋 `ArgumentOutOfRangeException` |
| `GetMissionSlotCount` | `int GetMissionSlotCount()` | 委託板 effectValue（讀 buildingID=1 + currentLevel） |
| `GetRosterCap` | `int GetRosterCap()` | 公會大廳 effectValue（buildingID=3） |
| `GetMaxConcurrentMissions` | `int GetMaxConcurrentMissions()` | 公會櫃臺 effectValue（buildingID=4） |
| `GetRecruitRefreshInterval` | `TimeSpan GetRecruitRefreshInterval()` | 招募廣告欄 effectValue（秒）→ `TimeSpan.FromSeconds` |
| `GetBankruptcyWarningSeconds` | `int GetBankruptcyWarningSeconds()` | 預備金保險櫃 effectValue（秒）（buildingID=5） |
| `IsStaffSystemUnlocked` | `bool IsStaffSystemUnlocked()` | `GetBuildingLevel(6) >= 1` |
| `CanUpgrade` | `bool CanUpgrade(int buildingID)` | 對齊 GDD §4.1，三閘檢查回 bool（不附原因），P-02 用於按鈕 enable 判定 |
| `TryUpgradeBuilding` | `UpgradeResult TryUpgradeBuilding(int buildingID)` | 主升級入口；回傳 `SUCCESS` / `ALREADY_MAX` / `GUILD_LEVEL_INSUFFICIENT` / `GOLD_INSUFFICIENT` |
| `Serialize` | `string Serialize()`（ISaveable） | 序列化 `BuildingState[6]` 為 JSON |
| `RestoreFromSave` | `void RestoreFromSave(string ownerJson)`（ISaveable） | 反序列化 + clamp 驗證 + 推送破產倒數秒數 |
| `InitializeAsNewGame` | `void InitializeAsNewGame()`（ISaveable） | 重置為 GDD §6.5 預設值（buildingID=1..5 → currentLevel=1；buildingID=6 → currentLevel=0） |

`OwnerKey` const = `"ft07Buildings"`；`IsCritical` const = `false`。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `BuildingUpgradedEvent` | 出 | `{ int buildingID; int fromLevel; int toLevel }` | `TryUpgradeBuilding` 升等成功、保險櫃推送（若 buildingID=5）之後發布；P-02 訂閱以更新 UI、P-03 訂閱以推播通知 |
| `BuildingMaintenanceDueEvent` | 出（**Phase 2 only**） | `{ DateTime dueTimestamp; IReadOnlyDictionary<int,int> perBuildingCost; int totalAmount }` | Phase 2 啟用後於 `OnDailyResetEvent` 計算後發布；Jam 版不發布（AC-20） |
| `OnDailyResetEvent` | 入（**Phase 2 only**） | `{ DateTime resetTimestamp }` | Phase 2 啟用維護費後 `BuildingService.OnEnable` 訂閱；Jam 版不訂閱 |

### 5.3 資料結構

```
BuildingRow (CSV row, immutable)
    int    buildingID         // 複合 PK 1
    string name
    int    maxLevel
    int    level              // 複合 PK 2
    int    effectValue
    int    upgradeCost
    int    guildLevelReq
    int    slotCount
    int    maintenanceCost    // Phase 2 用，Jam 版不讀

BuildingState (runtime, mutable)
    int buildingID            // 1..6
    int currentLevel          // 0（職員休息室）或 1..maxLevel

UpgradeResult (enum)
    SUCCESS                   = 0
    ALREADY_MAX               = 1
    GUILD_LEVEL_INSUFFICIENT  = 2
    GOLD_INSUFFICIENT         = 3

BuildingUpgradedEvent (struct)
    int buildingID
    int fromLevel
    int toLevel
    // 註：EventBus<T> 對 struct payload 不應 boxing；若 EventBus 實作為 Action<object>
    //     需於實作 PR 評估是否改泛型 Action<T> 或保留 class payload。FT-07 暫採 struct，
    //     對齊既有 FSD（C-06 / FT-06）event payload 慣例。

BuildingMaintenanceDuePayload (Phase 2)
    DateTime                       dueTimestamp
    IReadOnlyDictionary<int,int>   perBuildingCost   // defensive copy
    int                            totalAmount

ISaveable.Serialize() JSON shape
    {
      "states": [
        { "buildingID": 1, "currentLevel": 1 },
        ...
        { "buildingID": 6, "currentLevel": 0 }
      ]
    }
```

### 5.4 內部資料流

#### 5.4.1 啟動序列（New Game）

```
SaveLoadCoordinator.Bootstrap (FT-10)
  → BuildingService.InitializeAsNewGame()
      ├─ 步驟 1：buildingStates[0..4] = (id=1..5, currentLevel=1)
      ├─ 步驟 2：buildingStates[5]   = (id=6, currentLevel=0)
      └─ 步驟 3：（不推送，等 Start 統一推送）
SaveLoadCoordinator.Bootstrap → BuildingService.Start()
  ├─ 步驟 1：seconds = GetBankruptcyWarningSeconds() // 讀 BuildingTable[(5, 1)].effectValue = 10800
  ├─ 步驟 2：ResourceManagement.Instance.SetBankruptcyWarningDuration(seconds)
  └─ 步驟 3：（Jam 版止於此；Phase 2 加：EventBus.Subscribe<OnDailyResetEvent>(OnDailyReset)）
```

#### 5.4.2 啟動序列（Load Save）

```
SaveLoadCoordinator.Bootstrap → BuildingService.RestoreFromSave(ownerJson)
  ├─ 步驟 1：JsonUtility.FromJson<BuildingStateList>(ownerJson) → 6 筆
  ├─ 步驟 2：for each state:
  │      maxLv = BuildingTableLoader.GetMaxLevel(state.buildingID)
  │      if state.currentLevel > maxLv:
  │          Debug.LogWarning("buildingID={id}: clamped from {saved} to {maxLv}")
  │          state.currentLevel = maxLv
  │      buildingStates[index] = state
  └─ 步驟 3：（不於本方法推送；Start 階段統一推送，與 New Game 同流程；GDD §6.5 RestoreFromSave 步驟 3 的推送已搬移至 §5.4.1 Start，等價變體登記於 §8.4）
```

#### 5.4.3 升級主流程

```
P-02 UpgradeButton.OnClick(buildingID)
  → BuildingService.TryUpgradeBuilding(buildingID)
      ├─ 步驟 1：state    = GetState(buildingID)
      │         row      = BuildingTableLoader.GetRow(buildingID, state.currentLevel)
      ├─ 步驟 2：if state.currentLevel >= row.maxLevel → return ALREADY_MAX
      ├─ 步驟 3：nextLv   = state.currentLevel + 1
      │         nextRow  = BuildingTableLoader.GetRow(buildingID, nextLv)
      ├─ 步驟 4：if GuildCoreService.Instance.GetCurrentLevel() < nextRow.guildLevelReq
      │             → return GUILD_LEVEL_INSUFFICIENT
      ├─ 步驟 5：if ResourceManagement.Instance.GetGold() < nextRow.upgradeCost
      │             → return GOLD_INSUFFICIENT
      ├─ 步驟 6：ResourceManagement.Instance.AddGold(-nextRow.upgradeCost)  // 嚴格不允許負值；GDD §5.2
      ├─ 步驟 7：fromLv = state.currentLevel; state.currentLevel = nextLv
      ├─ 步驟 8：if buildingID == 5
      │             → ResourceManagement.Instance.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())
      │             // 推送排在事件發布「之前」（與 GDD §3.3 末兩步順序相反，登記於 §8.2）
      │             // 目的：listener 在 BuildingUpgradedEvent 回呼當下查 GetBankruptcyWarningSeconds() 即得新值
      ├─ 步驟 9：EventBus.Publish(new BuildingUpgradedEvent(buildingID, fromLv, nextLv))
      └─ 步驟 10：return SUCCESS
```

#### 5.4.4 查詢 API（典型）

```
P-02.RefreshBoardUI / FT-01.CheckRoster / FT-02.GateMaxConcurrent
  → BuildingService.Get*()    // 任一查詢 API
      ├─ 步驟 1：state   = buildingStates[buildingID - 1]
      ├─ 步驟 2：row     = BuildingTableLoader.GetRow(buildingID, state.currentLevel)
      └─ 步驟 3：return row.effectValue   // 或 TimeSpan.FromSeconds(row.effectValue) 等型別包裝
```

#### 5.4.5 維護費管線（**Phase 2，Jam 版整段封鎖**）

```
F-02.OnDailyResetEvent (Phase 2 only)
  → BuildingService.OnDailyReset(resetTimestamp)
      ├─ 步驟 1：perBuildingCost = {}
      ├─ 步驟 2：for buildingID in 1..6:
      │      level = buildingStates[buildingID-1].currentLevel
      │      cost  = BuildingTableLoader.GetRow(buildingID, level).maintenanceCost
      │      if cost > 0: perBuildingCost[buildingID] = cost
      ├─ 步驟 3：totalAmount = Σ perBuildingCost.Values
      ├─ 步驟 4：if totalAmount == 0 → return  // 全 L1 / L0 免費
      └─ 步驟 5：EventBus.Publish(new BuildingMaintenanceDueEvent(
                  resetTimestamp, perBuildingCost, totalAmount))
```

> **Jam 版實作要求**：`BuildingService.OnEnable` **不**訂閱 `OnDailyResetEvent`；`OnDailyReset` 方法**不實作**（class 內不存在）。Phase 2 啟用時新增該方法、於 `OnEnable` 訂閱 `OnDailyResetEvent`、於 `OnDisable` 取消訂閱即可解除封鎖。測試 AC-20 透過模擬發布 `OnDailyResetEvent`、驗證 `BuildingMaintenanceDueEvent` listener 收到 0 次調用。

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `BuildingTable.csv` | `buildingID, name, maxLevel, level, effectValue, upgradeCost, guildLevelReq, slotCount, maintenanceCost` | `【FT-07-DS】building-table.md`（_待建_） | 6 棟建築每等級的效果值、升級費、聲望閘、職員 slot capacity、Phase 2 維護費 | DataManager Awake（F-01 §3.1.2 註冊清單）；BuildingService Awake 透過 `BuildingTableLoader` 拉取並建索引 |

**載入驗證**（`BuildingTableLoader.Initialize`）：

1. 取得 `BuildingTable` 全行，按 `(buildingID, level)` 建 `Dictionary` 索引。
2. 驗證 buildingID 集合恰為 `{1,2,3,4,5,6}`；缺任一 → throw `MissingBuildingDataException`（對齊 GDD §5 Case 5.4 / AC-13）。
3. 對每個 buildingID：
   - 取出該建築所有 row 的 `maxLevel`，必須全部一致；不一致 → throw。
   - 期望 level 集合：buildingID=6 為 `{0,1,2,3,4,5}`，其他為 `{1,2,3,4,5}`（依 maxLevel）；缺任一 → throw（AC-13）。
   - 最小 level 行 `upgradeCost==0`、`guildLevelReq==0`（GDD §7.1）；違反 → throw。
   - level=2 行 `guildLevelReq==0`（雙軌閘設計，§7.2）；違反 → LogError。
4. 索引建立後常駐記憶體；`BuildingService` 透過 `GetRow(id, level)` 同步查詢。

### 6.2 引用的 ScriptableObject

無。本系統所有可調參數皆來自 `BuildingTable.csv`。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 委託板 / 招募廣告欄 / 公會大廳 / 公會櫃臺 / 預備金保險櫃 / 職員休息室的 `effectValue` | `BuildingTable.csv` 各 row 的 `effectValue` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 各建築 `maxLevel` | `BuildingTable.csv` 對應 row 的 `maxLevel` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 升級費用 `upgradeCost` | `BuildingTable.csv` 對應 row 的 `upgradeCost` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 聲望閘門等級 `guildLevelReq` | `BuildingTable.csv` 對應 row 的 `guildLevelReq` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 職員 slot capacity（FT-12 消費） | `BuildingTable.csv` 對應 row 的 `slotCount` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| Phase 2 維護費 `maintenanceCost` | `BuildingTable.csv` 對應 row 的 `maintenanceCost` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 「nextLevel ≥ 3 才檢查聲望閘」邏輯 | 由 `guildLevelReq` 欄位資料隱含（L2 row 為 0、L3+ row 為 3/4/5），**程式不寫** `if nextLevel >= 3` 特例 | 對應「四、程式實作原則」第 9 條：參數表格化（GDD §7.3 明文不可硬編碼） |
| ISaveable `OwnerKey` 字串 | `BuildingService.OwnerKey` const `"ft07Buildings"` | 唯一字串常數，不視為「寫死規則」（FSD-index §6.3 範圍排除標識符） |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| **5.1** 金幣恰等於升級費用 | `GetGold() < cost` 用 `<` 比較（`>=` 視為足夠），扣除後金幣可為 0；F-03 自行判定是否進入破產警告，FT-07 不預判 | `BuildingService.TryUpgradeBuilding` | EditMode test：**前置**：將委託板 currentLevel 設為 2、`GuildCoreService.currentLevel` 設為 3（避開聲望閘）、起始金幣=350；**驗證**：升委託板 L2→L3（費用 350g）回 SUCCESS、升等後金幣==0 |
| **5.2** 升級後金幣變負數（債務） | 金幣閘 `if GetGold() < cost → return GOLD_INSUFFICIENT`；走 `AddGold(-cost)` 而非 `AddGoldAllowBankruptcy`，確保扣款後不進入負值 | `BuildingService.TryUpgradeBuilding` | EditMode test：起始金幣=80，升委託板 L1→L2（費用 150）→ 回 GOLD_INSUFFICIENT、金幣==80 不變 |
| **5.3** 同 frame 雙擊 | FT-07 不主動防連點；第一次升級成功後 currentLevel 已更新，第二次呼叫依當前狀態重新走 §5.4.3 流程；P-02 應在 `BuildingUpgradedEvent` 後才重新 enable 按鈕（屬 P-02 範疇） | `BuildingService.TryUpgradeBuilding`；P-02（防護） | EditMode test：連續呼叫兩次，第二次依金幣／已滿級回對應結果（AC-12） |
| **5.4** BuildingTable 缺行 | `BuildingTableLoader.Initialize` 驗證階段拋 `MissingBuildingDataException`；DataManager Awake 階段已驗證 → 整個 Bootstrap 失敗，Unity 不進入 Play 模式 | `BuildingTableLoader.Initialize` | EditMode test：建假 CSV 缺 buildingID=3 → Initialize 拋例外（AC-13） |
| **5.5** 存檔等級超出 maxLevel | `RestoreFromSave` 偵測 `currentLevel > maxLevel` → clamp 至 maxLevel + `Debug.LogWarning("buildingID={id}: clamped from {saved} to {maxLevel}")`；不拋例外，遊戲繼續 | `BuildingService.RestoreFromSave` | EditMode test：注入 `currentLevel=99` → 還原後 == maxLevel、Console 有 LogWarning（AC-14） |
| **5.6** FT-06 尚未初始化（聲望閘判定時） | 依 FT-06 §3.2 既定保證——`Awake` 階段已寫入 `currentLevel=1`；FT-07 在 `Awake` 後呼叫 `GetCurrentLevel` 必合法（≥1）。若實作層真的早於 FT-06 Awake 取值（C# field default `0`），聲望閘必然不通過（L3+ 需 `guildLevelReq≥3`，`0<3` → reject），為安全 fallback；不額外 try-catch | `BuildingService.TryUpgradeBuilding` | Script Execution Order 設定（gameplay-programmer 實作期）；EditMode test：人為將 FT-06 設為未初始化（currentLevel=0）→ 嘗試升 L3 必回 GUILD_LEVEL_INSUFFICIENT |
| **5.7** 職員休息室升級階梯（FT-08 擴張後） | FT-08 透過 `GetBuildingLevel(6)` 取等級後直接讀 `BuildingTable[(6, level)].effectValue` 作為自動刷新間隔秒數；L0 時 `IsStaffSystemUnlocked()` 回 false，FT-08 降級不讀 effectValue | `BuildingService.GetBuildingLevel` / FT-08（消費端） | EditMode test：buildingStates[5].currentLevel=0 → IsStaffSystemUnlocked()==false；升至 L1 → 立即==true、effectValue=86400 |
| **8.8 衍生（AC-20）** Jam 版 OnDailyReset 不觸發維護費 | `BuildingService.OnEnable` 不訂閱 `OnDailyResetEvent`；`BuildingMaintenanceDueEvent` listener 收到 0 次（即使其他系統發布 `OnDailyResetEvent`） | `BuildingService.OnEnable`（不含 Subscribe） | PlayMode test：模擬 F-02 發 `OnDailyResetEvent` → 訂閱 BuildingMaintenanceDueEvent 的 spy 0 次調用（AC-20） |
| **8.8 衍生（AC-21）** Jam 版 maintenanceCost 不參與 runtime | `BuildingTableLoader` 載入時驗證欄位存在（schema 完整性），但 `BuildingService` Jam 版任何方法皆不讀 `row.maintenanceCost` | `BuildingTableLoader.Initialize`；`BuildingService` 任意方法 | EditMode test：將 maintenanceCost 改為極端值 → Jam 行為（升級、查詢、推送）皆不變（AC-21） |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 建築狀態（BuildingState） | §5.3 資料結構 / §5.4.1 啟動序列 | 對齊 | runtime 欄位完整對應 |
| §3.2 六棟建築定義（含初始等級） | §5.4.1 / §6.1 | 對齊 | 初始 buildingID=6 → currentLevel=0；其他 → 1 |
| §3.3 升級流程（三閘 + 推送） | §5.4.3 / §5.1 | 對齊（含等價變體） | 三閘判定與推送條件對齊 GDD pseudo；末段「事件發布 / 保險櫃推送」順序對換為等價變體 V-01（登記於 §8.2） |
| §3.4 效果值查詢 API（7 個） | §5.1 / §5.4.4 | 對齊 | 同步即時讀表，不快取 |
| §3.5 建築效果數值表 | §6.1 / §6.3 | 對齊 | 全數據移至 BuildingTable.csv |
| §3.6 事件發布契約 | §2.5 / §5.2 | 對齊 | `OnBuildingUpgraded` Jam 版啟用；`OnGuildMaintenanceDue` Phase 2 |
| §3.7 與 FT-06 的職責切割 | §2.3 上游表 / §2.4 下游表 | 對齊 | rosterCap / maxMissions 由 FT-07 提供，FT-06 補丁清單已勾完 |
| §3.8 設施維護費管線（Phase 2） | §5.4.5 / §7（AC-20、AC-21） | 對齊 | Jam 版整段封鎖：不訂閱、不發布、不計算 |

### 8.2 公式對齊或替代說明

GDD §4.1~§4.5 公式偽碼大部分**直接採用**：

- §4.1 `CanUpgrade` → §5.1 公開 API + §5.4.3 步驟 2~5。
- §4.2 效果值查表 → §5.4.4。
- §4.3 計算範例 → 用於 §1.3 DoD 與 §7 測試案例設計。
- §4.4 啟動推送 → §5.4.1 步驟 1~2。
- §4.5 升級推送（保險櫃升級時）→ §5.4.3 步驟 8。

#### 等價變體 V-01：升級流程末段順序對換

| 項目 | 內容 |
| --- | --- |
| GDD 原偽碼 | §3.3 末段：「升等 → 發布 `OnBuildingUpgraded` → IF buildingID==5 推送 `SetBankruptcyWarningDuration`」 |
| FSD 實作順序 | §5.4.3 步驟 7~9：「升等 → IF buildingID==5 推送 `SetBankruptcyWarningDuration` → 發布 `BuildingUpgradedEvent`」 |
| 變更內容 | 將「保險櫃推送」與「事件發布」對調，使推送發生在事件之前 |
| 等價性證明 | 兩者皆於同一 `TryUpgradeBuilding` 同步呼叫內完成、無中斷點；對外可觀察狀態（建築等級、金幣值、`GetBankruptcyWarningSeconds()` 回傳值、F-03 內 `_currentWarningDuration`、事件是否發出且僅一次）在方法返回時完全一致 |
| 採用理由 | 訂閱 `BuildingUpgradedEvent` 的 listener（P-02 / P-03）在事件回呼當下查 `GetBankruptcyWarningSeconds()` 或讀 F-03 倒數秒數時，必為新等級對應值；避免 listener 觀察到「事件已發布但 F-03 仍持有舊秒數」的中介狀態 |
| 對應驗證 | DoD-8 已補 listener 順序驗證子項；AC-17 / AC-18 不受影響（仍以方法返回後 `GetBankruptcyWarningDuration()` 為準） |

§8.2 結論：除等價變體 V-01 外，無其他公式替代。

### 8.3 未能實現的規則與修改建議

| 編號 | 項目 | 描述 | 建議處理 |
| --- | --- | --- | --- |
| B-01 | `【FT-07-DS】building-table.md` 待建 | FSD §2.2、§6.1 引用「待建」DS；FT-07 為 BuildingTable owner，無既有 DS | 後續執行 `/design-DS FT-07` 建立；DS 未到位不阻礙 FT-07 實作（schema 已於本 FSD §6.1 列出，可作 DS 草稿基底） |
| B-02 | FT-08 / FT-12 對 `BuildingTable[6].effectValue` 與 `slotCount` 的消費路徑 | GDD §6.2 列 FT-12 `BuildingTable[buildingID].slotCount` 由 FT-12 直接讀資料表（不經 FT-07 API）；FT-08 透過 `GetBuildingLevel(6)` 後自行讀表 | FT-12 / FT-08 FSD 撰寫時確認消費端 API；本 FSD 提供 `GetBuildingLevel(id)` 已足夠，無需新增 `GetSlotCount(id)` API（避免增加表面積） |
| B-03 | P-03 Notification Log API 待更新 | GDD §3.6、§6.2 多處標「【→Log API待更新】」 | P-03 FSD 撰寫時確認 BuildingUpgradedEvent payload 是否需擴充；目前不影響 FT-07 實作 |
| B-04 | ISaveable 介面實際簽名 | FT-10 FSD 尚未撰寫；`Serialize` 回傳 string 還是 byte[] / `RestoreFromSave` 簽名待 FT-10 定案 | 本 FSD 暫採 `string Serialize() / void RestoreFromSave(string)`，與 F-03 FSD 既定模式對齊；FT-10 FSD 完成後若簽名變更，本 FSD §5.1 同步 patch |
| B-05 | Phase 2 啟用判定旗標 | GDD §3.8 開頭宣告 Jam 版封鎖，未明示「啟用 Phase 2 維護費」的旗標位置（CSV 旗標？版本號？） | 本 FSD 採「Jam 版直接不訂閱 / 不發布」，Phase 2 啟用以 code patch 方式（解除 §5.4.5 封鎖），不引入 runtime feature flag；待 FT-07 進入 Phase 2 階段時於 GDD §3.8 補旗標規格 |

B-01~B-05 皆**不阻礙** FT-07 實作啟動。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-28 | `【FT-07】guild-building-system.md` | §6.5 RestoreFromSave 行為步驟 3 | RestoreFromSave 內的 `F-03.SetBankruptcyWarningDuration(...)` 推送已於 FSD §5.4.2 步驟 3 搬移至 `Start()` 統一執行（New Game / Load 同流程，等價且不重複推送）。GDD §6.5 文字保留即可，實作以 FSD 為準；後續若 FT-10 SaveLoadCoordinator 改為「不在 Bootstrap 內呼叫 BuildingService.Start()」則需重新評估推送位置 |

回註背景：FSD §5.4.1（New Game）與 §5.4.2（Load Save）共享 `Start()` 推送步驟以避免重複實作；GDD §6.5 RestoreFromSave 步驟 3 在 FSD 層面被 `Start()` 取代，屬實作層去重，不影響 AC-19（存檔載入後 `Start()` 完成時 `GetBankruptcyWarningDuration()` 等於存檔等級對應秒數）。

GDD 其他章節（§3.1~§3.8、§4.1~§4.5、§5、§6.1~§6.4、§7、§8）規則完整、雙向依賴已標記、驗收標準已映射至 §1.3 DoD，本 FSD 不另提回註。

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| —（撰寫過程無 §2.5 衝突暫停）|

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥（對應 GDD、Data-Specs 標待建、撰寫者／Review 者／狀態／日期皆有）
- [x] §1.3 完成目標可被測試驗證（DoD-1~DoD-9 對齊 AC-1~AC-21）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節、Data-Specs、上下游、事件契約）
- [x] §3.3 對映表覆蓋所有幻想／目的（7 條映射）
- [x] §4 Script 清單欄位齊全（4 Script、含路徑、SRP、依賴介面、預估規模）
- [x] §5 API/事件/資料結構/資料流齊備（12 API、3 事件、5 資料結構、5 資料流偽碼段）
- [x] §6 CSV 引用含對應 Data-Specs（標待建）；§6.3 嚴禁寫死清單對齊原則第 9 條（8 項）
- [x] §7 邊緣案例皆有對策（GDD §5.1~§5.7 + AC-20/21 衍生案例共 9 條）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（§3.1~§3.8 全列）
- [x] §8.2~§8.5 如實登記（§8.2 含 1 項等價變體 V-01；§8.3 5 項建議事項；§8.4 1 筆 GDD 回註；§8.5 無衝突）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（本次 patch）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | Claude Code 主體（自檢） | 通過 | 通過 | 通過 | 正向 FSD（無既有 Script）；FSD 未拆分（4 Script：BuildingTypes / IBuildingService / BuildingTableLoader / BuildingService，預估合計 600~800 行）；GDD §3.1~§3.8 全部「對齊」；GDD §4.1~§4.5 公式直接採用；GDD §5.1~§5.7 共 7 條邊緣案例皆有對策、涉及 Script、驗證方式；無真實衝突；建議項 B-01（FT-07-DS 待建）/B-02（FT-08/FT-12 消費 BuildingTable 路徑）/B-03（P-03 Log API 待更新）/B-04（ISaveable 簽名待 FT-10 定案）/B-05（Phase 2 啟用旗標）皆不阻礙實作；§8.4 無 GDD 回註；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| 2026-04-28 | Claude Code 主體（修正後自檢） | 通過 | 通過 | 通過 | 依 2026-04-27 `/design-review` 建議完成 6 項修正：(1) §5.4.5 Phase 2 封鎖收斂為「Jam 版不實作 OnDailyReset 方法」單一指示；(2) §5.4.3 步驟 8/9 偽碼補事件 / 推送順序註解，登記為等價變體 V-01；(3) §5.4.2 步驟 3 補搬移說明指向 §8.4；(4) §7 Case 5.1 測試敘述補前置條件（Guild Lv3 + 委託板 currentLevel=2）；(5) DoD-8 補 listener 順序驗證子項；(6) §5.3 BuildingUpgradedEvent 補 EventBus boxing 註記；§8.2 新增等價變體 V-01 證明、§8.4 新增 1 筆 GDD 回註（RestoreFromSave 推送搬移）；無新增公式 / 無新增 Script / AC 映射不變（21/21）；待主體複核後轉「已完成」 |
