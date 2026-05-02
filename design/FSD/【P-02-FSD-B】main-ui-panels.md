# 【P-02-FSD-B】功能規格說明書 — Main UI Panels（10 個 L2 面板實作）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【P-02】main-ui-framework.md`（版本：v0.6 / 2026-05-01） |
| 對應 Data-Specs | 無（本 FSD 不擁有 DS；UIText.csv / SceneObjectStateTable.csv 由 FSD-A owner；其他資料表為消費端引用 owner 系統 DS） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體 |
| 狀態 | 審查中 |
| 最近更新 | 2026-05-02 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

P-02-FSD-B 涵蓋 P-02 Main UI Framework 的 10 個 L2 覆蓋面板（CommissionBoard / AdventurerRoster / GuildBuilding / StaffRoster / StaffGacha / GuildOverview / StoryDialogue / ConfirmPopup / SettingsPanel / LogFloatingWindow host）的具體實作。每個面板負責：取得對應 gameplay 系統的資料、渲染版面元素、處理玩家互動（按鈕、點擊、卡片展開）、訂閱 gameplay 事件以即時更新、呼叫 gameplay API 觸發遊戲動作（派遣、升級、錄用、確認對話等）。所有面板的開啟／關閉、堆疊規則、淡入淡出動畫、輸入路由、文字 lookup、World-to-Screen 計算、SceneObjectController、對話分頁渲染、`OnUIReady` 啟動握手皆委由 P-02-FSD-A 處理。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：
- §3.5.1 委託板（CommissionBoardPanel + 推薦冒險者子面板）
- §3.5.2 冒險者名冊（AdventurerRosterPanel + 卡片展開）
- §3.5.3 公會建設（GuildBuildingPanel）
- §3.5.4 職員名冊（StaffRosterPanel）
- §3.5.5 職員面試（StaffGachaPanel）
- §3.5.6 公會總覽（GuildOverviewPanel）
- §3.5.7 通知 Log（P-02 host UXML container；綁定邏輯在 FSD-A LogFloatingWindowHost；本 FSD 僅提供 UXML 模板與 host 區塊版面）
- §3.5.8 故事面板（StoryDialoguePanel；事件路由與 chain 邏輯在 FSD-A，本 FSD 負責「對話文字 + 確認按鈕」UI 渲染）
- §3.5.9 確認彈窗（ConfirmPopup 視覺實作；API 簽章在 FSD-A）
- §3.5.10 設定彈窗（SettingsPanel）

**Out-of-Scope**：
- 三層 Scene-First 版面、面板狀態機、堆疊規則、ESC 路由 → FSD-A
- 6 個導覽用場景物件路由與 hover outline → FSD-A
- L1 持久 HUD（金幣、劇情指示器、設定按鈕、Log host container）→ FSD-A
- SceneObjectController（v3.1 5 個敘事物件 sprite 切換）→ FSD-A
- DialogueRenderer 分頁公式、SplitDialogue → FSD-A（本 FSD 透過 `DialogueRenderer.RenderToPanel` API 使用）
- World-to-Screen、hover outline 矩形計算 → FSD-A
- `OnUIReady` 啟動序列、UIText 載入、effectiveScale callback → FSD-A
- P-01 / P-03 / FT-10 自身內部邏輯
- Log 浮動視窗的拖曳/縮放/最小化 → P-03
- 透明視窗 / Hit-Test → P-01

### 1.3 完成目標（Definition of Done）

對齊 GDD §8.5（AC-19~AC-26）、§8.6（AC-27~AC-32 故事面板）、§8.10（AC-43~AC-45 整合）。程式可驗證條件：

- **DoD-B1**：EditMode test「CommissionBoardPanel 開啟後 1 frame 內依 `FT02.GetCommissionsBySource(Regular)` + `(Static)` 顯示委託卡；新發布事件後即時新增」通過（對應 AC-19）
- **DoD-B2**：EditMode test「委託卡元素：名稱/難度/類型/報酬/時長/介紹短文/狀態標籤/推薦按鈕齊備；成功率預覽 toggle 依 `IsSuccessRatePreviewEnabled` 顯示/隱藏」通過（對應 AC-20）
- **DoD-B3**：手動驗收「推薦冒險者子面板不顯示 willingness 預估欄位」（對應 AC-20、GDD §3.5.1.1）
- **DoD-B4**：EditMode test「AdventurerRosterPanel 排序：奧菲莉雅 templateID=901 永遠第一格、其餘依 `idleSinceTimestamp` 倒序」通過（對應 AC-21）
- **DoD-B5**：EditMode test「除名/開除按鈕顯示規則：Dead 顯示「除名」/ Idle 僅 `GetBuildingLevel(審查處) > 0` 顯示「開除」/ Wounded/OnMission 不顯示」通過（對應 AC-22）
- **DoD-B6**：EditMode test「GuildBuildingPanel 顯示 6 棟建築當前等級 + 下一級資訊；CanUpgrade==false 時 tooltip 顯示阻擋原因（4 種：金幣/聲望/公會等級/已達上限）」通過（對應 AC-23）
- **DoD-B7**：EditMode test「StaffRosterPanel 空名冊顯示「前往面試」+ 提示文字；非空時列出 `GetActiveRoster()` 結果」通過（對應 AC-24）
- **DoD-B8**：EditMode test「StaffGachaPanel 開啟前先關閉 StaffRoster；TryRecruit SUCCESS 後重開 StaffRoster」通過（對應 AC-25）
- **DoD-B9**：EditMode test「GuildOverviewPanel 顯示等級/稱號/聲望/金幣/破產倒數/陣營分數；訂閱 `OnBankruptcyWarningStateChanged` 進入警告時顯示倒數，每秒遞減」通過（對應 AC-26）
- **DoD-B10**：EditMode test「StoryDialoguePanel 點「確認」按鈕 → 呼叫 `FT09.ConfirmDialogue(stageID)`；`specialEventKey == "ophelia_missing"` 時 publish OnOpheliaMissingNight」通過（對應 AC-27 / AC-28）
- **DoD-B11**：手動驗收「Stage 4 場景說明層字色 #888888 + 不阻擋輸入；Stage 5 黑底 #000001 + 3 行 24 字限制」（對應 AC-29 / AC-30）
- **DoD-B12**：EditMode test「死亡通知（P-03 Critical）+ epilogue 同 frame 觸發時，先呈現死亡通知、確認後再呈現 epilogue」通過（對應 AC-31）
- **DoD-B13**：EditMode test「紙條視覺依 `GetCurrentStyleTagBias()` 切換 light/dark/neutral 三版」通過（對應 AC-32）
- **DoD-B14**：EditMode test「ConfirmPopup `Destructive` 樣式紅底；ESC 等同點「取消」」通過（對應 AC-17、AC-22）
- **DoD-B15**：手動驗收「SettingsPanel：UI 縮放 slider 範圍對齊 P-01 USER_SCALE_*；目標螢幕 dropdown；最小化/離開按鈕」（對應 AC-39）
- **DoD-B16**：EditMode test「面板事件訂閱：CommissionBoard 訂 `OnCommissionPosted/Accepted/Settled/AutoPickup`；AdventurerRoster 訂 `OnAdventurerDied/Recovered/Dismissed`；GuildBuilding 訂 `OnBuildingUpgraded`；StaffRoster 訂 `OnStaffHired/Fired/Assigned/StateChanged`；GuildOverview 訂 `OnGoldChanged/ReputationChanged/BankruptcyWarningStateChanged/GuildLevelChanged/DangerLevelChanged`」通過

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

引用 `【P-02】main-ui-framework.md` 章節：§3.5（10 個面板規格）、§3.5.1.1（推薦冒險者子面板）、§7.5（玩家可調項）、§8.5（面板資料反映 AC-19~AC-26）、§8.6（故事面板與 v3.1 整合 AC-27~AC-32）、§8.10（P-01/P-03 整合 AC-43~AC-45）。架構層基礎（§3.1/§3.4/§3.8/§3.9）由 FSD-A 處理。

### 2.2 Data-Specs 引用

P-02-FSD-B 不擁有任何 Data-Specs；以下為消費端引用：

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【P-02-DS】ui-text.md`（已建，owner = P-02-FSD-A） | `UIText.csv` | `key, zhTW` | 全面板標題、按鈕、tooltip、確認彈窗、錯誤提示文字 |
| `【FT-07-DS】building-table.md` | `BuildingTable.csv` | `level, name, upgradeCost, guildLevelReq, effectValue` | GuildBuildingPanel 自查下一級 cost / guildLevelReq / effectValue（消費端） |
| `【FT-08-DS】staff-refresh-cost-table.md`（已建）| `StaffRefreshCostTable.csv` | `refreshCount, manualRefreshCost` | StaffGachaPanel 顯示刷新費用 |
| `【FT-12-DS】staff-table.md`（已建）| `StaffTable.csv` | `staffID, name, rarity, salary, effectIDs, effectValues` | StaffRoster / StaffGacha 顯示職員名稱、稀有度、effect 摘要 |
| `【C-01-DS】mission-template.md` | `MissionTemplate.csv` | `missionID, missionName, difficulty, typeID, factionID` | 委託卡靜態資料（透過 FT-02 取 missionID 後查詢，或透過 D-02 facade）|
| `【C-01-DS】mission-difficulty-table.md` | `MissionDifficultyTable.csv` | `difficulty, baseReward` | 委託卡基準報酬顯示（最終值由 FT-02/FT-05 計算後提供） |
| `【FT-09-DS】faction-route-table.md` | `FactionRouteTable.csv` | `factionID, name` | GuildOverviewPanel 陣營分數標籤、StoryDialoguePanel 紙條樣式選擇 |

### 2.3 上游依賴系統

P-02-FSD-B 需要呼叫／訂閱：

| 系統 | 依賴介面（concrete singleton） | 用途 |
| --- | --- | --- |
| **P-02-FSD-A**（同系統）| `PanelManager.OpenPanel/ClosePanel/ShowConfirm/GetTopPanel`、`UITextService.Lookup/LookupFormat`、`DialogueRenderer.RenderToPanel`、`ScreenAnchorCalculator.WorldToPanelLocal`、`SceneObjectController.ResolveSceneObjectState`、`PersistentHudController` 視覺整合 | 所有面板共用框架基礎；確認彈窗呼叫 `ShowConfirm`；對話文字渲染呼叫 `DialogueRenderer` |
| F-01 DataManager | `Get<MissionTemplate>(missionID)`、`Get<MissionDifficultyTable>(diff)`、`Get<BuildingTable>(buildingID, level)`、`Get<StaffTable>(staffID)`、`Get<StaffRefreshCostTable>(refreshCount)`、`Get<RaceTable>`、`Get<TraitTable>`、`Get<ProfessionTable>`、`Get<FactionRouteTable>` | 各面板靜態資料查詢 |
| F-02 Time System | `TimeSystem.Instance.NowUTC` | 冒險者倒數計時、Wounded 恢復倒數 |
| F-03 Resource Management | `GetGold()`、`GetCurrentReputation()`、`GetBankruptcyWarningRemainingSeconds()`；訂閱 `OnGoldChanged(newValue, delta)`、`OnReputationChanged(newValue, delta)`、`OnBankruptcyWarningStateChanged` | GuildOverviewPanel；CommissionBoardPanel 預收費用顯示參考 |
| C-01 Mission Database | `Get<MissionTemplate>(missionID)` | 委託卡靜態資料 |
| C-02 Adventurer Management | `GetRoster()`（v3.1 排序）、`GetAdventurer(instanceID)`、`DismissAdventurer(instanceID)`；訂閱 `OnAdventurerDied`、`OnAdventurerRecovered`、`OnAdventurerDismissed` | AdventurerRosterPanel 資料源；除名/開除流程 |
| C-03 Profession System | `Get<ProfessionTable>(professionID)` | AdventurerRosterPanel 職業欄位 |
| C-04 Race System | `Get<RaceTable>(raceID)` | AdventurerRosterPanel 種族欄位 |
| C-05 Trait System | `Get<TraitTable>(traitID)` | AdventurerRosterPanel Trait 標籤與描述 |
| C-06 World Danger System | `GetCurrentLevel() : string`、`GetDangerData()`；訂閱 `OnDangerLevelChanged` | GuildOverviewPanel 顯示當前世界危險度 |
| FT-02 Mission Dispatch | `GetCommissionsBySource(CommissionSource source)`、`Dispatch(instanceID, missionID, DispatchSource source) : bool`；訂閱 `OnCommissionPosted(missionID, source)`、`OnCommissionAccepted(missionID, baseReward, source)` | CommissionBoardPanel 資料源；推薦子面板派遣動作 |
| FT-03 NPC Decision | 訂閱 `OnAutoPickup` | CommissionBoardPanel 推薦子面板被搶單時 collapse |
| FT-04 Outcome Resolution | 訂閱 `OnMissionResolved`、`OnAdventurerDied` | CommissionBoardPanel 任務統計 / AdventurerRosterPanel 死亡標記 / GuildOverviewPanel 統計 |
| FT-05 Guild Gold Flow | 訂閱 `OnCommissionSettled` | CommissionBoardPanel 委託卡狀態切「已結算」 |
| FT-06 Guild Core | `GetCurrentLevel()`、`GetCurrentTitle()`、`IsGameOver()`、`IsGameOverPending()`；訂閱 `OnGuildLevelChanged`、`OnGameOverPending` | GuildOverviewPanel；Game Over 流程觸發 |
| FT-07 Guild Building System | `GetBuildingLevel(buildingID)`、具名 effect getter（`GetCommissionBoardSlots / GetMaxConcurrentMissions / GetRecruitRefreshInterval / GetBankruptcyWarningSeconds` 等）、`CanUpgrade(buildingID)`、`TryUpgradeBuilding(buildingID)`、`IsStaffSystemUnlocked()`；訂閱 `OnBuildingUpgraded(buildingID, fromLevel, toLevel)` | GuildBuildingPanel；解鎖閘判定（職員名冊入口、開除按鈕） |
| FT-08 Gacha System | `GetCurrentCandidates()`、`GetCurrentPoolID()`、`TryManualRefresh()`、`TrySwitchPool(poolID)`、`TryRecruit(slotIndex)`、`TryRejectCandidate(slotIndex)`、`TryReserveCandidate(slotIndex)`、`TryReleaseReserve(slotIndex)`、`GetCurrentRefreshCount() : int`（**待 FT-08 §3.3.4 補登**，Jam 版 fallback 由 P-02 自行維護面板開啟以來的本地 counter，見 §5.4.6 + §8.3 B-03）；訂閱 `OnStaffSystemBootEvent` | StaffGachaPanel 資料源與 7 個動作 API；polling 模式刷新候選 |
| FT-09 Faction Story System | `GetCurrentFactionScore(factionID)`、`GetCurrentStyleTagBias()`、`GetUnlockedStageIndex(factionID)`、`ConfirmDialogue(stageID) : ConfirmDialogueResult`；訂閱 `OnFactionStoryStageEpilogue`（透過 FSD-A 路由）、`OnFactionRouteCompleted` | StoryDialoguePanel 確認流程；GuildOverviewPanel 陣營分數；紙條樣式選擇 |
| FT-12 Staff System | `GetActiveRoster()`、`GetRosterCap()`、`GetStaffStateView(instanceID)`、`IsSuccessRatePreviewEnabled()`、`TryAssignStaff(instanceID, slotID)`、`TryFireStaff(instanceID)`、`TryStartLeave(instanceID)`、`IsStaffHired(staffID)`；訂閱 `OnStaffHired`、`OnStaffFired`、`OnStaffAssigned`、`OnStaffStateChanged` | StaffRosterPanel；委託卡成功率預覽旗標 |
| P-01 Desktop Transparent Window | `GetEffectiveScale()`、`SetUserScale(value)`、`ResetUserScaleToDefault()`、`GetUserScale()`、`EnumerateAvailableMonitors()`、`SwitchTargetScreen(monitorID)`、`GetCurrentTargetMonitorID()`、`Minimize()` | SettingsPanel UI 縮放、螢幕切換、最小化按鈕 |
| D-01 Character Content Database | facade `GetAdventurerIntroText(instanceID)` | AdventurerRosterPanel 介紹短文 |
| D-02 Mission Content Database | facade `GetMissionName(missionID)`、`GetMissionIntroText(missionID)` | CommissionBoardPanel 名稱與介紹短文 |

### 2.4 下游被依賴系統

| 系統 | 反向依賴內容 |
| --- | --- |
| FSD-A `PanelManager` | 透過 PanelID enum 路由至本 FSD 各面板的 `Open(args) / Close()` 介面；本 FSD 各面板需實作 `IPanel` 抽象 |
| FT-09 | 期待 StoryDialoguePanel 在玩家確認對話且 `specialEventKey == "ophelia_missing"` 後 publish `OnOpheliaMissingNight` 事件（FSD-A 統籌轉發；本 FSD 提供 confirm callback） |
| FT-08 / FT-12 | 錄用流程依賴 StaffGachaPanel 呼叫 `TryRecruit`；slot 指派 / 開除依賴 StaffRosterPanel 呼叫對應 API |
| FT-07 / FT-02 / C-02 | 升級 / 派遣 / 除名等玩家動作依賴本 FSD 觸發點 |

### 2.5 跨系統事件契約

本 FSD 自身不發布跨系統事件（OnOpheliaMissingNight 由 FSD-A 統籌發布）；訂閱清單見 §2.3 與 §5.2。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家在底部欄帶上點擊建物 → 委託板/名冊/建設等面板從場景中拉出來 → 三秒內掌握公會狀態 → 完成操作後面板收回，場景仍在運轉。當故事節點觸發時，面板退到幕後，奧菲莉雅的對話佔據視覺中心。每一個按鈕、tooltip、確認文字皆由資料表驅動，玩家感知的是「公會的真實面孔」，而非「軟體介面」。

### 3.2 系統目的還原

P-02-FSD-B 為玩家所有「具體操作」的著陸點：審核委託、派遣冒險者、檢視名冊、升級建築、面試職員、總覽公會狀態、回應劇情、確認危險決策、調整視窗。每個面板專注於對應 gameplay 系統的視覺呈現與互動入口，將後端狀態翻譯為玩家可理解的卡片、列表、按鈕、tooltip。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 「即開即看」三秒掌握委託 | 委託板開啟後立即顯示所有待審委託、難度色塊、報酬、時長 | CommissionBoardPanel 訂閱 `OnCommissionPosted/Accepted/Settled` 即時刷新；不做 per-frame polling |
| 「派遣是公會長的核心動作」推薦冒險者一目了然 | 點擊推薦按鈕後展開冒險者列表（職業/階級），點擊冒險者 → 確認彈窗 → 派遣 | RecommendAdventurerSubpanel inline 展開；FT-02 `Dispatch(..., DispatchSource.PlayerManual)`；Jam 版不顯示 willingness 預估（FT-02/FT-03 未提供 API） |
| 「冒險者是有名字的個體」名冊呈現個性 | 名冊卡顯示名字 + Trait 圖示 + 介紹短文；點擊展開完整 Trait 描述與最近任務結果 | AdventurerRosterPanel inline 展開；訂閱 `OnAdventurerDied/Recovered`；奧菲莉雅永遠第一格 |
| 「公會的成長有形可見」建設升級顯示前後差異 | 建設面板每棟建築顯示當前效果值 + 下一級費用/門檻/預期效果；點擊升級 → 確認彈窗 | GuildBuildingPanel 透過 `GetBuildingLevel` + 自查 `BuildingTable[level+1]` |
| 「職員是長期投資」面試與名冊分離 | 點擊沙發 → 職員名冊 → 點「前往面試」按鈕 → 面試面板（堆疊：先關名冊） | StaffRosterPanel + StaffGachaPanel；錄用後自動重開名冊 |
| 「危險決策必須二次確認」破壞性動作紅底 | 除名/開除/解雇/離開遊戲使用 Destructive 樣式紅底按鈕 | ConfirmPopup `ConfirmStyle.Destructive` |
| 「故事面板出現時 UI 退到幕後」對話佔滿畫面 | 故事面板開啟前自動關所有 base panel；ESC 無效；必須走「確認」 | StoryDialoguePanel 由 FSD-A PanelManager 處理堆疊；本 FSD 負責對話文字 + 確認按鈕 callback |
| 「文字佔滿畫面」紙條三版視覺 | Stage 4 灰字場景說明層；Stage 5 黑底白字；其他正常紙條 | StoryDialoguePanel 依 `GetCurrentStyleTagBias()` 切 light/dark/neutral 紙條 sprite |
| 「桌面工具的端莊」設定彈窗對齊 P-01 | UI 縮放 slider、目標螢幕 dropdown、最小化、離開遊戲 | SettingsPanel 透過 P-01 API；UI 縮放範圍對齊 `USER_SCALE_*` |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**是**（P-02 GDD 拆分為 FSD-A / FSD-B；本 FSD-B 為 panel 層，內含 1 個 IPanel interface + 9 個 panel script + 1 個 subpanel script + 1 組 LogFloatingWindow UXML/USS 資產）。

### 4.2 拆分理由

詳見 FSD-A §4.2；本 FSD-B 持有各面板自身視覺狀態（展開卡片狀態、子面板狀態、表單輸入狀態）；不影響跨面板邏輯（堆疊、ESC 路由皆由 FSD-A 處理）。

### 4.3 拆分結果

詳見 FSD-A §4.3 表格。本 FSD-B 對應 GDD §3.5.1~§3.5.10（10 個面板）+ §3.5.1.1（推薦子面板）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| IPanel | `Assets/Scripts/UI/Panels/IPanel.cs` | 抽象 Panel 介面：`Open(object args)` / `Close()` / `PanelID Id { get; }` / `VisualElement Root { get; }`；FSD-A PanelManager 透過此介面驅動 | UnityEngine.UIElements | 30~50 行 |
| CommissionBoardPanel | `Assets/Scripts/UI/Panels/CommissionBoardPanel.cs` | 委託板渲染、訂閱 4 個委託事件、推薦按鈕展開子面板（§3.5.1） | IMissionDispatchService、IDataManager、IFactionStoryService（劇情委託標記）、UITextService、PanelManager | 250~320 行 |
| RecommendAdventurerSubpanel | `Assets/Scripts/UI/Panels/RecommendAdventurerSubpanel.cs` | 委託卡 inline 展開的冒險者推薦清單；點擊冒險者 → 確認彈窗 → 派遣（§3.5.1.1） | IAdventurerRoster、IMissionDispatchService、PanelManager（ShowConfirm）、UITextService | 150~190 行 |
| AdventurerRosterPanel | `Assets/Scripts/UI/Panels/AdventurerRosterPanel.cs` | 名冊渲染（v3.1 排序）、卡片展開、除名/開除流程（§3.5.2） | IAdventurerRoster、IDataManager、IBuildingService、ID01TextFacade、UITextService、PanelManager（ShowConfirm Destructive） | 250~310 行 |
| GuildBuildingPanel | `Assets/Scripts/UI/Panels/GuildBuildingPanel.cs` | 6 棟建築卡片、升級流程、CanUpgrade tooltip 阻擋原因（§3.5.3） | IBuildingService、IDataManager（BuildingTable）、IGuildCoreService、IResourceService、UITextService、PanelManager（ShowConfirm） | 220~280 行 |
| StaffRosterPanel | `Assets/Scripts/UI/Panels/StaffRosterPanel.cs` | 職員名冊渲染、空名冊提示、職員細節展開、slot 指派 dropdown（§3.5.4） | IStaffService、IBuildingService、IDataManager（StaffTable）、UITextService、PanelManager | 220~280 行 |
| StaffGachaPanel | `Assets/Scripts/UI/Panels/StaffGachaPanel.cs` | 職員面試 5 槽位渲染、6 個動作 API 觸發、polling 刷新（§3.5.5） | IGachaService、IStaffService（IsStaffHired）、IDataManager（StaffTable / StaffRefreshCostTable）、UITextService、PanelManager（ShowConfirm） | 280~340 行 |
| GuildOverviewPanel | `Assets/Scripts/UI/Panels/GuildOverviewPanel.cs` | 公會等級/聲望/金幣/破產倒數/陣營分數/styleTag bias（§3.5.6） | IGuildCoreService、IResourceService、IBuildingService、IFactionStoryService、IWorldDangerService、UITextService | 200~260 行 |
| StoryDialoguePanel | `Assets/Scripts/UI/Panels/StoryDialoguePanel.cs` | 故事面板對話文字 + 確認按鈕；styleTag bias 紙條樣式；Stage 4 / Stage 5 特殊呈現；publish OnOpheliaMissingNight 觸發點（§3.5.8） | IFactionStoryService、IDataManager（DialogueTable / FactionRouteTable）、DialogueRenderer（FSD-A）、UITextService、PanelManager、EventBus | 220~280 行 |
| ConfirmPopup | `Assets/Scripts/UI/Panels/ConfirmPopup.cs` | 確認彈窗視覺實作（Normal / Destructive 樣式）；UITextLookup 套入文字；ESC 等同取消（§3.5.9） | UITextService、PanelManager | 150~190 行 |
| SettingsPanel | `Assets/Scripts/UI/Panels/SettingsPanel.cs` | UI 縮放 slider / 目標螢幕 dropdown / 最小化 / 離開遊戲（§3.5.10） | IDesktopWindow（P-01）、UITextService、PanelManager（ShowConfirm Destructive 「離開遊戲」） | 220~280 行 |
| LogFloatingWindow（UXML/USS 資產，非 Script）| `Assets/UI/Panels/LogFloatingWindow.uxml` + `Assets/UI/Panels/LogFloatingWindow.uss` | Log host 區塊版面與預設 layout 資產；UXML container 由 FSD-A LogFloatingWindowHost 在 OnUIReady step 8 instantiate 並呼叫 P-03 BindLogWindow（§3.5.7）| n/a（純資產，無依賴介面）| n/a（資產，無 C# 行數）|

**預估合計：1990~2500 行 / 11 Script（含 IPanel interface + 9 panel + 1 subpanel）+ 1 組 LogFloatingWindow UXML/USS 資產**（原 LogFloatingWindowTemplate.cs 撤銷，職責移轉為 UXML/USS 資產；綁定邏輯統一收於 FSD-A LogFloatingWindowHost）。

### 4.5 類別關係（可選）

```
IPanel (interface)
  ├─ CommissionBoardPanel
  │    └─ has-a RecommendAdventurerSubpanel (inline expand, not _panelStack)
  ├─ AdventurerRosterPanel
  ├─ GuildBuildingPanel
  ├─ StaffRosterPanel
  ├─ StaffGachaPanel
  ├─ GuildOverviewPanel
  ├─ StoryDialoguePanel
  ├─ ConfirmPopup
  └─ SettingsPanel

LogFloatingWindow.uxml + LogFloatingWindow.uss (UXML/USS 資產；由 FSD-A LogFloatingWindowHost instantiate 並呼叫 P-03 BindLogWindow)

每個 Panel 在 OnEnable 訂閱事件、OnDisable 反訂閱（對齊 .claude/rules/ui-code.md）
所有面板透過 FSD-A PanelManager.OpenPanel(PanelID, args) 驅動，IPanel.Open(args) 接收 args 後初始化資料
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// IPanel 抽象（FSD-A PanelManager 透過此 interface 驅動）
public interface IPanel {
    PanelID Id { get; }
    VisualElement Root { get; }
    void Open(object args);
    void Close();
}

// 各 Panel 的 Open args 型別契約（透過 object 傳遞，內部 cast）
public sealed class CommissionBoardOpenArgs { /* 預留 */ }
public sealed class AdventurerRosterOpenArgs { public int? SelectedInstanceID; }
public sealed class GuildBuildingOpenArgs { public int? FocusBuildingID; }
public sealed class StaffRosterOpenArgs { /* 預留 */ }
public sealed class StaffGachaOpenArgs { /* 預留 */ }
public sealed class GuildOverviewOpenArgs { /* 預留 */ }
public sealed class StoryDialogueOpenArgs {
    public int StageID;
    public string ResolvedDialogueKey;
    public string SpecialEventKey;
    public bool IsEpilogue;
    public bool IsOpheliaInteraction;
    public bool SkipConfirmCallback;   // SceneObjectController 互動入口時 true
}
public sealed class ConfirmPopupOpenArgs {
    public string TitleKey;
    public string BodyKey;
    public string ConfirmKey;
    public string CancelKey;
    public Action OnConfirm;
    public Action OnCancel;
    public ConfirmStyle Style;
}
public sealed class SettingsPanelOpenArgs { /* 預留 */ }

// 各 Panel 對外查詢／互動 API（多數面板無對外 API；以下列出 P-02 內部跨面板 / FSD-A 呼叫的少數 API）
public sealed class CommissionBoardPanel : IPanel {
    public void RefreshAll();   // FSD-A 收到關鍵事件時可呼叫；面板自身亦在事件 handler 內呼叫
}
public sealed class StaffGachaPanel : IPanel {
    public void RefreshCandidatesFromService();   // polling 模式 + OnStaffHired event 觸發
}
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnCommissionPostedEvent` | subscribe | `{ int missionID, CommissionSource source }` | CommissionBoardPanel 新增委託卡 |
| `OnCommissionAcceptedEvent` | subscribe | `{ int missionID, int baseReward, CommissionSource source }` | CommissionBoardPanel 委託卡狀態切「進行中」 |
| `OnCommissionSettledEvent` | subscribe | FT-05 payload | CommissionBoardPanel 切「已結算」 |
| `OnAutoPickupEvent` | subscribe | FT-03 payload | CommissionBoardPanel 推薦子面板 collapse + toast「已自主接下」（EC-10） |
| `OnAdventurerDiedEvent` | subscribe | `{ int instanceID, ... }` | AdventurerRosterPanel 重整 + 死亡標記；GuildOverviewPanel 統計累積 |
| `OnAdventurerRecoveredEvent` | subscribe | `{ int instanceID }` | AdventurerRosterPanel 重整 |
| `OnAdventurerDismissedEvent` | subscribe | `{ int instanceID }` | AdventurerRosterPanel 重整 |
| `OnMissionResolvedEvent` | subscribe | FT-04 payload | GuildOverviewPanel 任務統計累積 |
| `OnBuildingUpgradedEvent` | subscribe | `{ int buildingID, int fromLevel, int toLevel }` | GuildBuildingPanel 重整 |
| `OnGuildLevelChangedEvent` | subscribe | FT-06 payload | GuildOverviewPanel 等級/稱號更新 |
| `OnGameOverPendingEvent` | subscribe | FT-06 payload | GuildOverviewPanel UI Pending 顯示（Jam 版 P-02 GDD §6 注：FT-06 §3 尚有對齊事項；本 FSD 僅顯示提示，不阻塞流程） |
| `OnGoldChangedEvent` | subscribe | `{ int newValue, int delta }` | GuildOverviewPanel 金幣顯示（與 FSD-A HUD 共用同一事件，但 FSD-A 是 L1 持久 HUD，本面板是總覽內金幣明細） |
| `OnReputationChangedEvent` | subscribe | `{ int newValue, int delta }` | GuildOverviewPanel 聲望顯示與進度條 |
| `OnBankruptcyWarningStateChangedEvent` | subscribe | F-03 payload | GuildOverviewPanel 進入/離開警告狀態 → 顯示/隱藏破產倒數 |
| `OnDangerLevelChangedEvent` | subscribe | C-06 payload | GuildOverviewPanel 危險度名稱更新 |
| `OnStaffHiredEvent` | subscribe | FT-12 payload | StaffRosterPanel 重整；StaffGachaPanel 確認錄用結果 |
| `OnStaffFiredEvent` | subscribe | FT-12 payload | StaffRosterPanel 重整 |
| `OnStaffAssignedEvent` | subscribe | FT-12 payload | StaffRosterPanel 重整 |
| `OnStaffStateChangedEvent` | subscribe | FT-12 payload | StaffRosterPanel 重整 |
| `OnStaffSystemBootEvent` | subscribe | FT-08 payload | StaffGachaPanel 初次顯示候選刷新 |
| `OnFactionStoryStageEpilogueEvent` | subscribe（FSD-A 路由） | FT-09 payload | StoryDialoguePanel epilogue 路徑 |
| `OnFactionRouteCompletedEvent` | subscribe（可選） | FT-09 payload | StoryDialoguePanel 路線完結 UI |
| `OnOpheliaMissingNightEvent` | publish（StoryDialoguePanel 為唯一 publisher） | （無欄位 struct） | StoryDialoguePanel 在玩家「確認」按鈕 callback 內，當 `args.SpecialEventKey == "ophelia_missing"` 時直接呼叫 `EventBus.Publish<OnOpheliaMissingNightEvent>(...)`；FSD-A SceneObjectController 與 FT-09 §3.6.9 同為 subscriber |

> 各面板於 `OnEnable` 訂閱、`OnDisable` 反訂閱（對齊 `.claude/rules/ui-code.md`）。

### 5.3 資料結構

```csharp
// CommissionBoardPanel 卡片視圖模型
private sealed class CommissionCardView {
    public int MissionID;
    public CommissionSource Source;
    public string Name;
    public string Difficulty;
    public int TypeID;
    public int Reward;
    public float DurationHours;
    public string IntroText;
    public CommissionStatus Status;   // Pending / OnGoing / Settled
    public bool ShowSuccessRatePreview;
}

private enum CommissionStatus { Pending, OnGoing, Settled }

// AdventurerRosterPanel 卡片視圖模型
private sealed class AdventurerCardView {
    public int InstanceID;
    public string Name;
    public AdventurerRank Rank;
    public int ProfessionID;
    public int RaceID;
    public IReadOnlyList<int> TraitIDs;
    public AdventurerStatus Status;
    public long IdleSinceTimestamp;
    public string IntroText;
    public bool IsExpanded;
    public bool ShowDismissButton;   // §3.5.2 / AC-22 規則計算結果
    public string DismissButtonKey;  // "btn.adv.dismiss" / "btn.adv.fire"
}

// GuildBuildingPanel 卡片視圖模型
private sealed class BuildingCardView {
    public int BuildingID;
    public string Name;
    public int CurrentLevel;
    public string CurrentEffectText;
    public BuildingUpgradeInfo NextLevel;   // null 表已達上限
    public bool CanUpgrade;
    public string UpgradeBlockReasonKey;    // "tooltip.upgrade.gold" / ".reputation" / ".guildLevel" / ".maxLevel"
}

private sealed class BuildingUpgradeInfo {
    public int Level;
    public int Cost;
    public int GuildLevelReq;
    public string ExpectedEffectText;
}

// StaffRosterPanel
private sealed class StaffRosterCardView {
    public int InstanceID;
    public int StaffID;
    public string Name;
    public int Rarity;
    public int Salary;
    public IReadOnlyList<int> EffectIDs;
    public IReadOnlyList<float> EffectValues;
    public int? AssignedSlotBuildingID;
    public StaffStateView State;   // Working / Reallocating / OnLeave
}

// StaffGachaPanel
private sealed class GachaSlotView {
    public int SlotIndex;
    public CandidateCard Candidate;   // FT-08 提供
    public bool IsReserved;
}

// StoryDialoguePanel
private enum NoteVariant { Light, Dark, Neutral, Stage4Inline, Stage5Black }
private sealed class StoryDialoguePresentationConfig {
    public NoteVariant Variant;
    public string PrimaryHex;       // 文字色
    public string BackgroundHex;    // 紙條色
    public bool BlockInput;         // Stage4 inline = false；其他 = true
}

// ConfirmPopup（透過 ConfirmPopupOpenArgs 接收）
```

### 5.4 內部資料流

#### §5.4.1 CommissionBoardPanel.Open

```
PanelManager.OpenPanel(PanelID.CommissionBoard, args)
  → CommissionBoardPanel.Open(args)
      ├─ regular = FT02.GetCommissionsBySource(CommissionSource.Regular)
      ├─ static  = FT02.GetCommissionsBySource(CommissionSource.Static)
      ├─ foreach missionID in regular ∪ static：
      │    ├─ template = DataManager.Get<MissionTemplate>(missionID)
      │    ├─ name     = D02Facade.GetMissionName(missionID)
      │    ├─ intro    = D02Facade.GetMissionIntroText(missionID)
      │    ├─ diff     = DataManager.Get<MissionDifficultyTable>(template.difficulty)
      │    ├─ reward   = baseReward 與 FT-02 / FT-05 計算後最終值（FT-02 §3.8 提供 active mission reward；委託板採 baseReward 顯示，最終值在派遣後）
      │    └─ 建立 CommissionCardView 並渲染卡片
      ├─ ShowSuccessRatePreview = FT12.IsSuccessRatePreviewEnabled()
      └─ 套用 USS class（推薦按鈕僅 Pending 狀態）

OnEnable：訂閱 OnCommissionPosted / OnCommissionAccepted / OnCommissionSettled / OnAutoPickup
OnDisable：反訂閱
```

#### §5.4.2 RecommendAdventurerSubpanel 流程

```
玩家點推薦按鈕（CommissionBoardPanel 卡片內）
  → CommissionBoardPanel.OnRecommendButtonClicked(missionID)
      ├─ template = DataManager.Get<MissionTemplate>(missionID)
      ├─ subpanel = new RecommendAdventurerSubpanel(missionID)
      ├─ candidates = filter C02.GetRoster() by:
      │     ├─ adventurer.status == Idle
      │     └─ MeetsTraitRequirement(adventurer, template.requiredTraitID)
      │           ├─ if template.requiredTraitID == 0 → return true（無要求；對齊 v3.1 P3.1-001 設計意圖）
      │           └─ else → return adventurer.traitIDs.Contains(template.requiredTraitID)
      ├─ subpanel.Render(candidates)
      └─ inline 展開於委託卡下方（不入 _panelStack）

玩家點擊冒險者卡片
  → subpanel.OnAdventurerClicked(instanceID)
      → PanelManager.ShowConfirm(
            titleKey: "confirm.dispatch.title",
            bodyKey: "confirm.dispatch.body",   // "派遣 {0} 接下 {1}？" → LookupFormat 套入
            confirmKey: "btn.confirm",
            cancelKey: "btn.cancel",
            onConfirm: () => {
                bool ok = FT02.Dispatch(instanceID, missionID, DispatchSource.PlayerManual);
                if (!ok) {
                    UIToast.Show(UITextService.Lookup("toast.dispatch.failed"));
                    panel.RefreshAll();
                }
                subpanel.Collapse();
            }
        )

訂閱 OnAutoPickup：若推薦展開的 missionID 被搶單 → subpanel.Collapse() + toast「{name} 已自主接下此委託」
```

#### §5.4.3 AdventurerRosterPanel.Open

```
AdventurerRosterPanel.Open(args)
  ├─ roster = C02.GetRoster()  // 已照 v3.1 排序（奧菲莉雅 901 第一格 + idleSinceTimestamp 倒序）
  ├─ foreach adventurer in roster：
  │    ├─ profession = DataManager.Get<ProfessionTable>(adventurer.professionID)
  │    ├─ race       = DataManager.Get<RaceTable>(adventurer.raceID)
  │    ├─ traits     = adventurer.traitIDs.Select(id => DataManager.Get<TraitTable>(id))
  │    ├─ intro      = D01Facade.GetAdventurerIntroText(adventurer.instanceID)
  │    ├─ ShowDismissButton = ResolveDismissButton(adventurer.status)
  │    └─ 建立 AdventurerCardView
  ├─ args.SelectedInstanceID 非空 → 自動展開該卡片
  └─ 渲染列表

ResolveDismissButton(status):
  ├─ status == Dead       → return ("btn.adv.remove", true)
  ├─ status == Idle       →
  │     ├─ if REVIEW_OFFICE_BUILDING_ID 常量值 ≤ 0（待 FT-07 P3.1-009 補登）
  │     │     └─ LogWarning「審查處 buildingID 未確認，開除按鈕暫不顯示」+ return (null, false)
  │     └─ else → return ("btn.adv.fire", FT07.GetBuildingLevel(REVIEW_OFFICE_BUILDING_ID) > 0)
  ├─ status in [Wounded, OnMission] → return (null, false)
  └─ default → return (null, false)

// AdventurerRosterPanel.OnEnable 時執行一次 LogWarning 檢查；不在每張卡片重複輸出
// FT-07 補 P3.1-009 後，全條件改用 IBuildingService.IsBuildingUnlocked(REVIEW_OFFICE_BUILDING_ID)

玩家點除名/開除按鈕
  → PanelManager.ShowConfirm(
        titleKey: ...,
        bodyKey: "confirm.dismiss.body",   // "確定要開除 {0}？此操作不可逆。"
        confirmKey: ...,
        cancelKey: ...,
        onConfirm: () => C02.DismissAdventurer(instanceID),
        style: ConfirmStyle.Destructive
    )
```

#### §5.4.4 GuildBuildingPanel.Open

```
GuildBuildingPanel.Open(args)
  ├─ foreach buildingID in [1..6]：
  │    ├─ currentLevel = FT07.GetBuildingLevel(buildingID)
  │    ├─ currentEffect = ResolveEffectText(buildingID, currentLevel)  // 依 buildingID 呼叫對應 FT-07 具名 getter
  │    ├─ nextLevelData = DataManager.Get<BuildingTable>(buildingID, currentLevel + 1)
  │    │    └─ null 表已達上限
  │    ├─ canUpgrade = FT07.CanUpgrade(buildingID)
  │    ├─ blockReasonKey = canUpgrade ? null : ResolveBlockReason(buildingID, nextLevelData)
  │    │    // 優先順序由設計師暫定（GDD §3.5.3 / AC-23 未明示）；本 FSD 採：已達上限 > 公會等級 > 聲望 > 金幣
  │    │    // 理由：上限為硬封頂，公會等級為長期成長路徑，聲望為中期門檻，金幣為短期可恢復；
  │    │    //       玩家更需先看到不可恢復的阻擋；同時違反多項時 tooltip 顯示首個成立者
  │    │    ├─ if nextLevelData == null → return "tooltip.upgrade.maxLevel"
  │    │    ├─ if FT06.GetCurrentLevel() < nextLevelData.guildLevelReq → return "tooltip.upgrade.guildLevel"
  │    │    ├─ if ResourceManagement.GetCurrentReputation() < repReq → return "tooltip.upgrade.reputation"
  │    │    └─ if ResourceManagement.GetGold() < nextLevelData.upgradeCost → return "tooltip.upgrade.gold"
  │    └─ 建立 BuildingCardView

ResolveEffectText(buildingID, level):
  switch buildingID：
    case 1: return UITextLookup("text.building.commissionSlots", FT07.GetCommissionBoardSlots())
    case 2: return UITextLookup("text.building.maxConcurrent", FT07.GetMaxConcurrentMissions())
    case 3: return UITextLookup("text.building.recruitInterval", FT07.GetRecruitRefreshInterval())
    case 5: return UITextLookup("text.building.bankruptcyWarning", FT07.GetBankruptcyWarningSeconds())
    case 6: return UITextLookup("text.building.staffLounge", FT07.GetStaffLoungeCapacity())
    default: ...

玩家點升級按鈕
  → PanelManager.ShowConfirm(
        titleKey: "confirm.upgrade.title",
        bodyKey: "confirm.upgrade.body",   // "升級 {0} 至 Lv{1}（費用 {2}g）？"
        ...,
        onConfirm: () => {
            UpgradeResult result = FT07.TryUpgradeBuilding(buildingID);
            if (result == GOLD_INSUFFICIENT) {
                UIToast.Show(UITextService.Lookup("toast.upgrade.goldInsufficient"));
            }
            panel.RefreshAll();
        }
    )

OnEnable：訂閱 OnBuildingUpgraded → RefreshAll
```

#### §5.4.5 StaffRosterPanel.Open

```
StaffRosterPanel.Open(args)
  ├─ roster = FT12.GetActiveRoster()
  ├─ rosterCap = FT12.GetRosterCap()
  ├─ if roster.Count == 0：
  │    └─ 顯示「前往面試」按鈕 + 提示文字（"text.staff.empty"）
  ├─ else：
  │    └─ foreach staffInstance in roster：
  │         ├─ staffData = DataManager.Get<StaffTable>(staffInstance.staffID)
  │         ├─ stateView = FT12.GetStaffStateView(staffInstance.instanceID)
  │         ├─ effectSummary = ResolveEffectSummary(staffData.effectIDs, staffData.effectValues)
  │         └─ 建立 StaffRosterCardView
  └─ 「前往面試」按鈕：
        → PanelManager.OpenPanel(PanelID.StaffGacha)   // FSD-A 自動關閉 StaffRoster

玩家點 slot 指派 dropdown
  → FT12.TryAssignStaff(instanceID, slotID)
  → 若回 false：UIToast.Show("toast.staff.cooldown") 等

**解雇 / 休假按鈕（Jam 版範疇外決議）**：
  ├─ FT-12 §3.5 提供 `TryFireStaff(instanceID)` / `TryStartLeave(instanceID)` API（§2.3 已列）
  ├─ Jam 版 StaffRosterPanel **不暴露**解雇 / 休假 UI 入口（GDD §3.5.4 line 213 僅明示 slot 指派 dropdown，未提解雇/休假；避免 scope creep）
  ├─ ConfirmPopup `Destructive` 樣式 §3.5.9 提到「解雇」用例為一般性說明，Jam 版實際走的 Destructive 入口為 AdventurerRoster 的「除名/開除」與 SettingsPanel 的「離開遊戲」
  └─ Phase 2 補：StaffRosterPanel 卡片右上角加「解雇」與「休假」按鈕，呼叫 ConfirmPopup `Destructive` 樣式 → TryFireStaff / TryStartLeave；登記於 §8.3 B-10

OnEnable：訂閱 OnStaffHired / OnStaffFired / OnStaffAssigned / OnStaffStateChanged → RefreshAll
```

#### §5.4.6 StaffGachaPanel.Open

```
StaffGachaPanel.Open(args)
  ├─ candidates = FT08.GetCurrentCandidates()
  ├─ poolID = FT08.GetCurrentPoolID()
  ├─ refreshCount = ResolveRefreshCount()
  │     ├─ if FT08 提供 GetCurrentRefreshCount() API → 直接呼叫（待 FT-08 §3.3.4 補登後採此路徑）
  │     └─ else fallback：使用 P-02 自行維護的 _localRefreshCounter
  │           ├─ 面板 Open 時從 0 起算（每次 Open 重置；對應 FT-08 §4.1.2 RefreshCostTable 第一筆通常為免費）
  │           ├─ 每次 TryManualRefresh 回 SUCCESS 後 _localRefreshCounter += 1
  │           └─ 面板 Close 時不持久化（Jam 版近似行為；長期建議移至 FT-08 SaveData）
  ├─ refreshCost = DataManager.Get<StaffRefreshCostTable>(refreshCount).manualRefreshCost
  ├─ 渲染 5 槽位（依 candidates 對應 GachaSlotView，含保留旗標）
  └─ 池選擇 tab 顯示 A/B（依 FT-08 §3.2.2 解鎖閘）

玩家點「面試」按鈕（每張卡片下方 / 全槽位刷新）
  → result = FT08.TryManualRefresh()
  → if result == SUCCESS：_localRefreshCounter += 1（fallback 路徑使用）
  → RefreshCandidatesFromService()

玩家點「切池」按鈕
  → FT08.TrySwitchPool(targetPoolID)
  → RefreshCandidatesFromService()

玩家點槽位卡片 → 「錄用」確認彈窗
  → PanelManager.ShowConfirm(
        titleKey: "confirm.recruit.title",
        bodyKey: "confirm.recruit.body",   // "錄用 {0}？薪水 {1}g，效果 {2}。"
        ...,
        onConfirm: () => {
            RecruitResult result = FT08.TryRecruit(slotIndex);
            if (result == SUCCESS) {
                PanelManager.ClosePanel(PanelID.StaffGacha);
                PanelManager.OpenPanel(PanelID.StaffRoster);
            } else {
                UIToast.Show("toast.recruit.failed.{result}");
            }
        }
    )

「保留」按鈕（候選卡片角落）→ FT08.TryReserveCandidate(slotIndex) → RefreshCandidatesFromService
「不錄用」按鈕 → FT08.TryRejectCandidate(slotIndex) → RefreshCandidatesFromService
「釋放保留」按鈕（已保留卡片右上角小 ✕ 圖示，僅 IsReserved == true 時顯示）
  → FT08.TryReleaseReserve(slotIndex) → RefreshCandidatesFromService

RefreshCandidatesFromService（polling 模式）：
  ├─ candidates = FT08.GetCurrentCandidates()
  ├─ 重新渲染 5 槽位（含保留旗標 / ✕ 圖示顯示判定）
  └─ 不額外訂閱 gacha 業務事件（FT-08 §3.7 設計理由）

OnEnable：訂閱 OnStaffSystemBoot（初次顯示）+ OnStaffHired（確認錄用）
```

#### §5.4.7 GuildOverviewPanel.Open

```
GuildOverviewPanel.Open(args)
  ├─ level   = FT06.GetCurrentLevel()
  ├─ title   = FT06.GetCurrentTitle()
  ├─ rep     = ResourceManagement.GetCurrentReputation()
  ├─ currentLevelRep = DataManager.Get<GuildLevelTable>(level).reputationThreshold
  ├─ nextLevelData = DataManager.Get<GuildLevelTable>(level + 1)   // 已達上限時為 null
  ├─ nextLevelRep = nextLevelData?.reputationThreshold ?? currentLevelRep
  ├─ progressPercent = (nextLevelData == null) ? 1.0f
  │                   : Mathf.Clamp01((float)(rep - currentLevelRep) / (nextLevelRep - currentLevelRep))
  ├─ gold = ResourceManagement.GetGold()
  ├─ if F03.IsBankruptcyWarningActive()：
  │    └─ remaining = ResourceManagement.GetBankruptcyWarningRemainingSeconds()
  │       渲染倒數
  ├─ buildingsSummary = [1..6].Where(b => FT07.GetBuildingLevel(b) > 0)
  ├─ missionStats = （訂閱 OnMissionResolved / OnAdventurerDied 累積；本面板自身 cache）
  ├─ factionScores = [1..N].Select(f => (f, FT09.GetCurrentFactionScore(f), FactionRouteTable[f].name))
  ├─ styleBias = FT09.GetCurrentStyleTagBias()
  ├─ dangerName = WorldDanger.GetDangerData().name
  └─ 渲染版面

OnEnable：訂閱 OnGoldChanged / OnReputationChanged / OnBankruptcyWarningStateChanged
            / OnGuildLevelChanged / OnDangerLevelChanged / OnMissionResolved / OnAdventurerDied
            / OnGameOverPending → RefreshAll（或部分區塊增量更新）

破產倒數 per-second 更新：
  ├─ Update() 中以 1Hz 節流：if (Time.unscaledTime - _lastTickAt > 1f) { 重算 remaining; _lastTickAt = ...; }
  └─ 或使用 IVisualElementScheduledItem.Every(1000ms)
```

#### §5.4.8 StoryDialoguePanel.Open

```
StoryDialoguePanel.Open(args : StoryDialogueOpenArgs)
  ├─ dialogueText = DataManager.Get<DialogueRow>().Lookup(args.ResolvedDialogueKey)?.text
  │                 ?? "[對話缺失：{args.ResolvedDialogueKey}]"
  ├─ pages = DialogueRenderer.SplitDialogue(dialogueText)
  ├─ presentation = ResolvePresentation(args)
  │    ├─ args.SpecialEventKey == "ophelia_missing" → NoteVariant.Stage4Inline (灰字 #888888)
  │    ├─ args.StageID == 5 對應的 stage → NoteVariant.Stage5Black (背景 #000001)
  │    ├─ FT09.GetCurrentStyleTagBias() → Light / Dark / Neutral
  │    └─ default → Neutral
  ├─ 套用 presentation 並渲染第一頁
  └─ 「下一頁」按鈕（pages.Count > 1）；最後一頁按鈕為「確認」

玩家點「確認」按鈕：
  ├─ if !args.SkipConfirmCallback：
  │    ├─ if args.IsEpilogue：
  │    │    └─ no-op（epilogue 不呼叫 ConfirmDialogue）
  │    └─ else：
  │         result = FT09.ConfirmDialogue(args.StageID)
  │         if result == SUCCESS：
  │             if args.SpecialEventKey == "ophelia_missing"：
  │                 EventBus.Publish(new OnOpheliaMissingNightEvent())
  │         else if result == INJECT_FAILED / others：
  │             UIToast.Show("toast.story.confirmFailed.{result}")
  └─ PanelManager.ClosePanel(PanelID.StoryDialogue)
     // FSD-A 收到 close → chain continue（依 §5.4.6 FSD-A 規範）

OnEnable：訂閱 OnFactionStoryStageEpilogue / OnFactionRouteCompleted（可選）
```

#### §5.4.9 ConfirmPopup.Open

```
ConfirmPopup.Open(args : ConfirmPopupOpenArgs)
  ├─ title = UITextService.Lookup(args.TitleKey)
  ├─ body  = UITextService.Lookup(args.BodyKey)
  ├─ confirmText = UITextService.Lookup(args.ConfirmKey, "確認")
  ├─ cancelText  = UITextService.Lookup(args.CancelKey, "取消")
  ├─ if args.Style == ConfirmStyle.Destructive：套用紅底 USS class
  └─ 渲染並等待按鈕點擊

玩家點「確認」：args.OnConfirm?.Invoke() + PanelManager.ClosePanel(ConfirmPopup)
玩家點「取消」：args.OnCancel?.Invoke() + PanelManager.ClosePanel(ConfirmPopup)
ESC：等同點「取消」（由 FSD-A PanelManager.OnEscape 處理）
modal 遮罩點擊：忽略（依 GDD §3.4.2 表格，ConfirmPopup 必須走按鈕；FSD-A PanelManager.OnModalOverlayClicked 過濾）
```

#### §5.4.10 SettingsPanel.Open

```
SettingsPanel.Open(args)
  ├─ slider userScale = P01.GetUserScale()
  │    range = [P01.USER_SCALE_MIN, P01.USER_SCALE_MAX] step P01.USER_SCALE_STEP
  │    onChanged: value => P01.SetUserScale(value)
  ├─ button「還原預設」: () => P01.ResetUserScaleToDefault()
  ├─ dropdown 螢幕：
  │    items = P01.EnumerateAvailableMonitors()
  │    selected = P01.GetCurrentTargetMonitorID()
  │    onChanged: id => P01.SwitchTargetScreen(id)
  ├─ slider 音量（Post-Jam 預留，唯讀顯示）
  ├─ dropdown 語言（Jam 版鎖 zhTW，唯讀）
  ├─ button「最小化」: () => P01.Minimize()
  └─ button「離開遊戲」:
        → PanelManager.ShowConfirm(
              titleKey: "confirm.exit.title",
              bodyKey: "confirm.exit.body",
              ...,
              onConfirm: () => Application.Quit(),
              style: ConfirmStyle.Destructive
          )

EC-17（dropdown 列出已斷開螢幕）：
  ├─ EnumerateAvailableMonitors 結果含 isConnected 旗標
  ├─ 已斷開項顯示「(已斷開) {name}」並標灰
  └─ 玩家選擇已斷開項 → P01.SwitchTargetScreen 內部 fallback 至主螢幕 → toast「目標螢幕已斷開，已切回主螢幕」
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `UIText.csv` | `key, zhTW` | `【P-02-DS】ui-text.md`（已建，owner = P-02-FSD-A） | 全面板文字 | 透過 FSD-A UITextService.Lookup |
| `MissionTemplate.csv` | `missionID, missionName, difficulty, typeID, factionID, categoryID` | `【C-01-DS】mission-template.md` | CommissionBoardPanel 委託卡靜態資料（消費端） | F-01 啟動時載入 |
| `MissionDifficultyTable.csv` | `difficulty, baseReward, baseDuration` | `【C-01-DS】mission-difficulty-table.md` | 委託卡基準數據（消費端） | 同上 |
| `BuildingTable.csv` | `buildingID, level, name, upgradeCost, guildLevelReq, effectValue` | `【FT-07-DS】building-table.md` | GuildBuildingPanel 下一級資訊（消費端） | 同上 |
| `StaffTable.csv` | `staffID, name, rarity, salary, effectIDs, effectValues` | `【FT-12-DS】staff-table.md`（已建） | StaffRoster / StaffGacha 職員資料（消費端） | 同上 |
| `StaffRefreshCostTable.csv` | `refreshCount, manualRefreshCost` | `【FT-08-DS】staff-refresh-cost-table.md`（已建） | StaffGachaPanel 刷新費用（消費端） | 同上 |
| `GuildLevelTable.csv` | `level, title, reputationThreshold, maxDifficulty` | `【FT-06-DS】guild-level-table.md` | GuildOverviewPanel 等級進度條 | 同上 |
| `FactionRouteTable.csv` | `factionID, name` | `【FT-09-DS】faction-route-table.md` | GuildOverviewPanel 陣營分數標籤；StoryDialoguePanel 紙條樣式 | 同上 |
| `RaceTable.csv` / `ProfessionTable.csv` / `TraitTable.csv` | 各自 PK + name | `【C-04-DS】` / `【C-03-DS】` / `【C-05-DS】` | AdventurerRosterPanel 職業/種族/Trait 顯示 | 同上 |
| `DialogueTable` | `dialogueID, text` | （owner 待定，§8.3 條目 B-02） | StoryDialoguePanel 對話文字 | F-01 啟動時載入或懶載入 |

### 6.2 引用的 ScriptableObject

無（本 FSD 透過 FSD-A `P02UITuning` 取常量；不額外建立 SO）。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 各 Panel 標題、按鈕、tooltip 文字 | UIText.csv | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 委託卡 baseReward / baseDuration / 難度色塊基準 | MissionDifficultyTable.csv | 同上 |
| 建築升級 cost / guildLevelReq / effectValue 預期值 | BuildingTable.csv | 同上 |
| 職員稀有度顯示閾值、薪水、effect 摘要 | StaffTable.csv | 同上 |
| 面試刷新費用 | StaffRefreshCostTable.csv | 同上 |
| 公會等級稱號、聲望門檻 | GuildLevelTable.csv | 同上 |
| 陣營名稱（GuildOverview 標籤、紙條樣式來源） | FactionRouteTable.csv | 同上 |
| 職業 / 種族 / Trait 名稱與描述 | ProfessionTable / RaceTable / TraitTable | 同上 |
| Stage 4 / Stage 5 視覺色 hex | P02UITuning.StoryNoteGrayHex / StoryNoteBlackHex（FSD-A 持有 SO） | 同上（透過 FSD-A 取常量） |
| 動畫 0.15s、modal alpha 0.40、hover outline 屬性 | P02UITuning（FSD-A） | 同上 |
| `nav_staff_lounge` 解鎖閘 buildingID = 6 | GDD §3.2 固定路由表（程式碼常量） | 不違反——導覽路由為固定路由表（GDD §3.2 明示） |
| 審查處 buildingID（開除按鈕判定） | 待 FT-07 P3.1-009 補登；本 FSD 取 `IBuildingService.IsBuildingUnlocked(REVIEW_OFFICE_BUILDING_ID)` 接口或常量 + LogWarning | 不違反——常量為過渡 |
| 6 棟建築 ID 範圍 [1..6] | GDD §3.5.3 表格固定 | 不違反——建築清單為固定設計 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| EC-10：委託板開啟期間玩家推薦中的委託被 NPC 自主接走 | CommissionBoardPanel 訂閱 `OnAutoPickup` → if RecommendAdventurerSubpanel 開於該 missionID → subpanel.Collapse() + UIToast「{name} 已自主接下此委託」（P-03 Optional） | CommissionBoardPanel + RecommendAdventurerSubpanel | EditMode test：開推薦子面板，模擬 OnAutoPickup 同 missionID，斷言 subpanel 自動關 |
| EC-11：冒險者名冊開啟期間當前展開卡片冒險者死亡 | AdventurerRosterPanel 訂閱 `OnAdventurerDied` → 若展開中卡片 == instanceID → 自動 collapse + 套用「已死亡」灰底 USS class；不主動關閉名冊面板 | AdventurerRosterPanel | EditMode test：展開卡片 A，模擬 A 死亡，斷言卡片 collapse + 灰底 |
| EC-12：建設面板升級失敗（金幣同時被扣款）| GuildBuildingPanel 接 `TryUpgradeBuilding` 回傳值；非 SUCCESS → UIToast 顯示對應錯誤鍵 + RefreshAll；不重複扣款（FT-07 內部冪等保證） | GuildBuildingPanel | EditMode test：模擬 TryUpgradeBuilding 回 GOLD_INSUFFICIENT，斷言 toast + 面板重整 |
| EC-13：職員名冊開啟期間 OnStaffSalaryDue 但 FT-05 拒付（Phase 2）| Jam 版不發薪水（FT-12 §3.9 標 Phase 2）→ 此情境 Jam 版不會發生；UI 不實作 | （無，Phase 2）| Phase 2 補測 |
| EC-14：故事面板呈現 Stage 4 期間玩家對奧菲莉雅推薦 | C-02 與 FT-02 前置檢查阻擋（status != Idle）；推薦子面板已過濾 Wounded 冒險者，玩家看不到她；本 FSD 不額外處理 | RecommendAdventurerSubpanel | 觀察：推薦子面板列表自然不含 Wounded |
| EC-17：設定彈窗螢幕 dropdown 列出已斷開螢幕 | SettingsPanel dropdown 解析 `isConnected` 旗標 → 已斷開項顯示灰；玩家選擇已斷開 → P01.SwitchTargetScreen 內部 fallback 至主螢幕 → toast | SettingsPanel | 多螢幕手動測試（拔線 → 開設定彈窗 → dropdown 顯示「(已斷開)」）|
| EC-18：userScale 滑桿被拖至範圍外 | SettingsPanel slider `lowValue / highValue` 對齊 `[USER_SCALE_MIN, USER_SCALE_MAX]`；P01.SetUserScale 內部 clamp；UI slider visual 跟隨 clamp 後值 | SettingsPanel | EditMode test：模擬 SetUserScale(超出值) → P01 回 clamp 後值 → slider visual 對齊 |
| EC-26：P-03 Critical 通知與故事面板同 frame 觸發 | StoryDialoguePanel 不直接處理；FSD-A StoryDialogueQueue 統籌（依 P-02-FSD-A §7 EC-26）；本 FSD 為 chain end consumer | （由 FSD-A 處理）| 同上 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.5.1 委託板 + §3.5.1.1 推薦子面板 | §4.4 CommissionBoardPanel + RecommendAdventurerSubpanel、§5.4.1~§5.4.2、§7 EC-10 | 對齊 | Jam 版不顯示 willingness 預估（FT-02/FT-03 未提供 API）；成功率預覽依 FT-12 旗標 |
| §3.5.2 冒險者名冊 | §4.4 AdventurerRosterPanel、§5.4.3、§7 EC-11、§8.1 AC-21/AC-22 | 對齊 | v3.1 排序透過 C-02 GetRoster 直接提供；除名/開除依規則計算 |
| §3.5.3 公會建設 | §4.4 GuildBuildingPanel、§5.4.4、§7 EC-12 | 對齊 | 自查 BuildingTable + 具名 effect getter；CanUpgrade tooltip 4 種阻擋原因 |
| §3.5.4 職員名冊 | §4.4 StaffRosterPanel、§5.4.5 | 對齊 | 入口前置條件 `GetBuildingLevel(6) >= 1` 由 FSD-A SceneNavigationController 過濾；本 FSD 假設已通過 |
| §3.5.5 職員面試 | §4.4 StaffGachaPanel、§5.4.6 | 對齊 | polling 模式（不訂閱 gacha 業務事件）；6 個 Try* API 全列；錄用後重開名冊 |
| §3.5.6 公會總覽 | §4.4 GuildOverviewPanel、§5.4.7 | 對齊 | 破產倒數透過 `OnBankruptcyWarningStateChanged` + `GetBankruptcyWarningRemainingSeconds`；陣營分數 + styleTag bias |
| §3.5.7 通知 Log（host）| §4.4 LogFloatingWindowTemplate（UXML 模板）；綁定邏輯 FSD-A | 對齊 | 本 FSD 僅提供 UXML/USS 模板資源 |
| §3.5.8 故事面板 | §4.4 StoryDialoguePanel、§5.4.8、§7 EC-20（透過 DialogueRenderer 處理） | 對齊 | confirm 流程 + Stage 4/5 特殊呈現 + OnOpheliaMissingNight publish |
| §3.5.9 確認彈窗 | §4.4 ConfirmPopup、§5.4.9 | 對齊 | Normal / Destructive 兩樣式；ESC 等同取消（FSD-A 路由）|
| §3.5.10 設定彈窗 | §4.4 SettingsPanel、§5.4.10、§7 EC-17/EC-18 | 對齊 | 透過 P-01 §3.8 對外 API surface 8 個方法 |

### 8.2 公式對齊或替代說明

本 FSD 不含 gameplay 公式；依賴 FSD-A 的 §4.4.2（SplitDialogue）、§4.5（淡入淡出）、§4.3（World-to-Screen）。各面板中的派生計算（如進度條百分比 `(rep - currentLevelRep) / (nextLevelRep - currentLevelRep)`）為標準線性映射，無需替代說明。

### 8.3 未能實現的規則與修改建議

| 編號 | 條目 | 性質 | 建議 |
| --- | --- | --- | --- |
| B-01 | 開除按鈕「審查處 buildingID」未在 GDD / FT-07 表格化 | 暫時阻塞 | 同 FSD-A §8.3 A-01；待 FT-07 P3.1-009 補登 |
| B-02 | DialogueTable owner 待定 | 跨 FSD | StoryDialoguePanel 從 F-01 直查 DialogueTable；長期 owner 待定 |
| B-03 | StaffGachaPanel 取 `refreshCount` API 待 FT-08 提供 | 跨 FSD | FT-08 §3.3.4 未明列 `GetCurrentRefreshCount() : int`；建議 FT-08 補登。本 FSD §5.4.6 已明示 fallback：FT-08 補 API 前由 P-02 自行維護 `_localRefreshCounter`（面板 Open 時 reset 0、TryManualRefresh SUCCESS 後 +1、面板 Close 不持久化）；功能等價，長期建議移至 FT-08 SaveData |
| B-04 | GuildOverviewPanel 任務統計累積值持久化策略 | 建議性 | Jam 版可選擇不持久化，重啟後從 0 開始；或由 FT-04 提供累積查詢 API |
| B-05 | EC-26 P-03 Critical 通知判定 | 跨 FSD | 同 FSD-A §8.3 A-03；P-03 補 query API 後對齊 |
| B-06 | EC-13 Phase 2 薪水拒付 UI | Phase 2 | Jam 版不實作（FT-12 §3.9 共識）|
| B-07 | StaffRosterPanel slot 指派 dropdown 顯示 cooldown 倒數 | 建議性 | 顯示已具備但倒數可選；FT-12 提供 cooldown remaining 查詢即可加 |
| B-08 | StoryDialoguePanel epilogue 與正常 stage 不同處理（不呼叫 ConfirmDialogue）| 建議性 | 已於 §5.4.8 流程明示；StoryDialogueOpenArgs.IsEpilogue 旗標路由 |
| B-09 | 8 個面板各自的事件訂閱可能在 FT-09 / FT-12 / FT-08 後續 patch 後需要對齊 | 建議性 | 後續批次處理；不阻塞 Jam 版實作 |
| B-10 | StaffRosterPanel 解雇 / 休假 UI 入口（Phase 2 補）| 範疇外（Jam 版） | Jam 版不暴露解雇 / 休假按鈕（GDD §3.5.4 line 213 僅明示 slot 指派 dropdown）；FT-12 §3.5 提供的 `TryFireStaff / TryStartLeave` API 在 Jam 版僅後端可用、UI 不可達；Phase 2 補卡片右上角按鈕並走 ConfirmPopup `Destructive` 樣式 |

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | （本 FSD 無 GDD 回註）|

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | — | — | （無真實衝突；GDD v0.6 已通過五輪 design-review）|

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥
- [x] §1.3 完成目標可被測試驗證（DoD-B1~B16）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節、Data-Specs、上游、下游、事件契約）
- [x] §3.3 對映表覆蓋所有幻想／目的（9 條）
- [x] §4 Script 清單欄位齊全（11 個 Script 含 IPanel）
- [x] §5 API/事件/資料結構/資料流齊備（5.1~5.4）
- [x] §6 CSV 引用含對應 Data-Specs；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（EC-10~EC-14、EC-17、EC-18、EC-26 共 7 條與 FSD-B 相關）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（10 條 §3.5.1~§3.5.10）
- [x] §8.2~§8.5 如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 / §7.3 已同步更新（隨後補丁）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-05-02 | Claude Code 主體（Opus 4.7 + xhigh）| 通過 | 通過 | 通過 | 結構：8 節 + 附錄 A 全填、編號順序對齊 FSD-index §三；邏輯：10 個面板 SRP 明確、API/事件/資料流前後一致、ConfirmPopup callback chain 清晰、StoryDialoguePanel epilogue 路徑透過 IsEpilogue 旗標分流；GDD 對齊：§3.5.1~§3.5.10 全對齊、AC-19~AC-32 / AC-43~AC-45 對應 DoD 16 條、§7 EC 7 條（FSD-B 相關）皆有對策；建議項 B-01~B-09 皆不阻擋實作 |
| 2026-05-02 | Claude Code 主體（Opus 4.7 + xhigh）—— `/design-review P-02-FSD` NEEDS REVISION patch | 通過 | 通過 | 通過 | design-review 14 條 issue 全修。**FSD-B 修補項**：(C2) §5.2 OnOpheliaMissingNightEvent 標明 StoryDialoguePanel 為唯一 publisher，與 FSD-A subscriber 對映清晰。(I1) §2.3 FT-08 列補 `GetCurrentRefreshCount`（待 FT-08 補登）+ §5.4.6 明示 ResolveRefreshCount fallback（API 未提供時 P-02 自行維護 `_localRefreshCounter`）；§8.3 B-03 同步澄清。(I2) §5.4.5 補「解雇 / 休假按鈕（Jam 版範疇外決議）」block：Jam 版不暴露 UI；§8.3 新增 B-10 標 Phase 2 補。(I3) §5.4.2 RecommendAdventurerSubpanel 補 `MeetsTraitRequirement` helper：requiredTraitID == 0 視為無要求、否則 `traitIDs.Contains` 判斷。(I4) §5.4.3 ResolveDismissButton 補 fallback：常量值 ≤ 0 時 LogWarning + 隱藏開除按鈕（對齊 FSD-A 同步修正）。(I6) §5.4.6 補「釋放保留」按鈕（已保留卡片右上角小 ✕ 圖示）→ TryReleaseReserve；§2.3 FT-08 列補 7 個 Try* API 完整。(I7) §5.4.7 補 `currentLevelRep = DataManager.Get<GuildLevelTable>(level).reputationThreshold`；補 `nextLevelData == null` 已達上限分支 + Mathf.Clamp01 防護。(I8) §5.4.4 ResolveBlockReason 優先順序明示：已達上限 > 公會等級 > 聲望 > 金幣（理由：硬封頂 > 長期成長 > 中期門檻 > 短期可恢復）。(I9) §4.4 LogFloatingWindowTemplate.cs 撤銷，改為 `LogFloatingWindow.uxml + .uss` UXML/USS 資產；§4.5 類別關係同步；預估合計改為「11 Script + 1 組 UXML/USS 資產」。本次 patch 不變動章節結構、不影響 §1.3 DoD / §8.1 / §8.2 / §8.5 既有判定 |
