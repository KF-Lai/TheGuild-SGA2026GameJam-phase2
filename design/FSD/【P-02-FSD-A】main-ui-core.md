# 【P-02-FSD-A】功能規格說明書 — Main UI Framework Core（Framework + Scene + Bootstrap）

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【P-02】main-ui-framework.md`（版本：v0.6 / 2026-05-01） |
| 對應 Data-Specs | `【P-02-DS】ui-text.md`（_待建_，CSV：`UIText.csv`，owner = P-02）<br>`【P-02-DS】scene-object-state-table.md`（_待建_，CSV：`SceneObjectStateTable.csv`，owner = P-02，v3.1 P3.1-010） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh） |
| Review 者 | Claude Code 主體 |
| 狀態 | 已完成 |
| 最近更新 | 2026-05-02 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

P-02-FSD-A 涵蓋 P-02 Main UI Framework 的「框架核心 + 場景整合 + 啟動序列 + 文字資料驅動」四大基礎設施。具體包括：三層 Scene-First 版面（L0/L1/L2）的 sortingOrder 與遮罩規則、L2 面板狀態機與堆疊管理、`OpenPanel/ClosePanel/ShowConfirm` 對內 API、L1 持久 HUD（金幣 / 劇情指示器 / 設定按鈕 / Log host）、6 個導覽用場景物件的點擊路由與 hover outline、SceneObjectController（v3.1 P3.1-010 奧菲莉雅敘事物件）、對話視窗分頁渲染、`OnUIReady` 啟動握手與 FT-10 `OnLoadCompleted` 對接、`UIText.csv` 文字 lookup、P-01 effectiveScale 推送 callback。所有 10 個面板的具體實作交由 P-02-FSD-B 處理。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：
- 三層 Scene-First 版面（L0 場景、L1 持久 HUD、L2 modal overlay）的 UI Toolkit 結構與 sortingOrder 規則（GDD §3.1）
- L2 面板生命週期狀態機（Closed / Opening / Open / Closing）與淡入淡出動畫（GDD §3.4.1、§4.5）
- `_panelStack: List<PanelID>` 堆疊維護與規則（最多兩層、互斥規則）（GDD §3.4.2）
- `OpenPanel(PanelID, args) / ClosePanel(PanelID) / GetTopPanel() / ShowConfirm(args)` 對內 API（GDD §3.4.4、§3.5.9）
- ESC 鍵與 modal 遮罩點擊的輸入路由（GDD §3.4.3）
- 6 個導覽用場景物件的點擊路由、hover outline 渲染、未解鎖 tooltip（GDD §3.2、§4.7）
- L1 持久 HUD 4 元素：金幣顯示、劇情進展指示器、設定按鈕、Log 浮動視窗 host（GDD §3.3、§3.5.7）
- `_pendingStageDialogueQueue: Queue<int>` FIFO 模型與 chain start / continue / manual resume / dedupe 規則（GDD §3.3、§3.5.8）
- SceneObjectController：`SceneObjectStateTable.csv` 載入、`stageCondition` 解析、`ResolveSceneObjectState(objectID)` API、`_activeEvents` 維護（v3.1 P3.1-010；GDD §3.6）
- 對話視窗分頁渲染：`SplitDialogue` 公式、中文逐字計、英文單字邊界斷行、Stage 4「她沒回來」場景說明層、Stage 5 黑底白字（GDD §3.7、§4.4.2）
- World-to-Screen 場景內嵌 UI 錨點計算與 hover outline 矩形計算（GDD §4.3、§4.7）
- `OnUIReady` 啟動序列：訂閱事件、Bootstrap 復原（`GetPendingDialogueStages` 主動查詢）、CSV 載入、L1 元素初始化、P-01 effectiveScale callback 註冊、Log host 綁定、發布 `OnUIReady`（GDD §3.8）
- `UITextLookup(key, fallback)` helper 與 `String.Format` placeholder 套入（GDD §3.9）
- `OnEffectiveScaleChanged(float)` callback 接收 P-01 推送、套用 `PanelSettings.scale`、重算 L1 錨點（GDD §4.2）
- `P02UITuning` ScriptableObject 收錄視覺/動畫常量、字體大小（GDD §7.1、§7.2）

**Out-of-Scope**：
- 10 個 L2 面板的內部資料源、互動細節、版面元素 → P-02-FSD-B
- ConfirmPopup 視覺實作 → P-02-FSD-B（API 簽章在本 FSD-A 定義）
- SettingsPanel 視覺實作 → P-02-FSD-B
- Log 浮動視窗的拖曳/縮放/最小化邏輯 → P-03（P-02 僅為 host caller）
- 透明視窗、Hit-Test、effectiveScale 計算 → P-01
- 通知條目渲染、文字模板 → P-03
- DialogueTable 內容 owner → 待 P-02 啟動 design-review 或 narrative 模組決定（FT-09 為 dialogue 消費端）
- 玩家輸入快捷鍵（1-6 數字鍵切面板）→ Post-Jam

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準（AC-01~AC-12、AC-13~AC-18 框架核心；AC-33~AC-39、AC-40~AC-42、AC-43~AC-45 場景與整合）。程式可驗證條件：

- **DoD-A1**：EditMode test「OpenPanel before OnUIReady → LogWarning + return false」通過（對應 AC-04 / EC-01）
- **DoD-A2**：EditMode test「`_panelStack` 規則：base panel 互斥 / confirm popup 可疊加 / 故事面板開啟前清空 base」通過（對應 AC-14 / AC-15）
- **DoD-A3**：EditMode test「ESC 路由：StoryPanel 忽略 / ConfirmPopup 等同取消 / SettingsPanel 關閉 / 空 stack 開啟設定」通過（對應 AC-12 / AC-16 / AC-17）
- **DoD-A4**：EditMode test「`_pendingStageDialogueQueue` FIFO + dedupe + chain continue + manual resume」通過（對應 AC-10 / AC-11 / EC-08）
- **DoD-A5**：EditMode test「`ResolveSceneObjectState`：priority 由高至低、第一個 stageCondition 成立者生效；全不成立回 Default；syntax 錯誤 LogError 並 skip」通過（對應 AC-33 / AC-36 / EC-21 / EC-22）
- **DoD-A6**：EditMode test「`OnOpheliaMissingNight` 後 `_activeEvents` 含 `"ophelia_missing"`、`OnOpheliaReturned` 後移除」通過（對應 AC-34 / AC-35）
- **DoD-A7**：EditMode test「`SplitDialogue`：中文 24 字斷行、英文單字邊界、混排優先順序」通過（對應 §4.4.2）
- **DoD-A8**：EditMode test「`UITextLookup`：命中回傳對應語言字串 / 不命中 LogError + fallback / fallback 為空回 key 本身」通過（對應 AC-40 / EC-19）
- **DoD-A9**：EditMode test「Bootstrap 復原：`GetPendingDialogueStages` 回傳 [3,5] 時 `_pendingStageDialogueQueue` enqueue 順序為 3→5；既有 stageID 不重複入隊」通過（對應 EC-04）
- **DoD-A10**：EditMode test「`OnUIReady` 發布前所有 `OpenPanel` 被忽略；發布後正常運作」通過（對應 AC-01 / AC-04）
- **DoD-A11**：EditMode test「`UIText.csv` / `SceneObjectStateTable.csv` 載入失敗時降級行為與 `OnUIReady` 仍發布」通過（對應 EC-02 / EC-03 / AC-42）
- **DoD-A12**：EditMode test「`OnEffectiveScaleChanged` callback：套用至 PanelSettings + L1 錨點重算 + LogInfo 新值」通過（對應 AC-37）
- **DoD-A13**：手動驗收「6 個導覽場景物件 hover outline 顯示與點擊路由正確；`nav_staff_lounge` 在 `GetBuildingLevel(6) < 1` 時 tooltip + 不開面板」（對應 AC-05 / AC-06 / AC-07）
- **DoD-A14**：手動驗收「Stage 4「她沒回來」場景說明層字色 `#888888` 不阻擋輸入；Stage 5 黑底 `#000001` 三行限制適用」（對應 AC-29 / AC-30）

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

引用 `【P-02】main-ui-framework.md` 章節：§1（系統範圍）、§2（玩家幻想）、§3.1（三層架構）、§3.2（場景物件互動點）、§3.3（持久 UI 元素 + queue 模型）、§3.4（面板生命週期/堆疊/輸入路由/OpenPanel API）、§3.5.7（Log host）、§3.5.9（ConfirmPopup API 簽章）、§3.6（SceneObjectController 子模組）、§3.7（對話視窗呈現）、§3.8（OnUIReady 啟動握手）、§3.9（UIText 路由）、§4.1（變數定義）、§4.2（PanelSettings 縮放）、§4.3（World-to-Screen）、§4.4（文字截斷）、§4.5（淡入淡出動畫）、§4.6（Log 視窗位置 clamp）、§4.7（hover outline 矩形）、§4.8（範例計算）、§5（EC-01~EC-26 全部）、§6（依賴關係）、§7.1（視覺/動畫常量）、§7.2（字體大小）、§7.4（z-order 常量）、§7.5（玩家可調項）、§8.1~§8.10（AC 全條目）。

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【P-02-DS】ui-text.md`（_待建_） | `UIText.csv` | `key, zhTW, en` | 全 UI 玩家可見字串資料源；`UITextLookup(key)` 由本系統取得對應語言字串 |
| `【P-02-DS】scene-object-state-table.md`（_待建_） | `SceneObjectStateTable.csv` | `objectID, stageCondition, spriteVariant, dialogueKey, priority, audioCue` | SceneObjectController 解析資料源；v3.1 P3.1-010 5 個 objectID × 10 row |

### 2.3 上游依賴系統

P-02-FSD-A 需要呼叫／訂閱：

| 系統 | 依賴介面（concrete singleton） | 用途 |
| --- | --- | --- |
| F-01 DataManager | `DataManager.Instance.Get<UITextRow>()`、`Get<SceneObjectStateRow>()`、`Get<DialogueRow>()` | UIText / SceneObjectStateTable / DialogueTable 載入後查詢 |
| F-02 Time System | `TimeSystem.Instance.NowUTC` | 倒數計時輔助、log 條目時戳（FSD-B 的面板使用，本 FSD-A 透過 PersistentHudController 在金幣 HUD 不直接使用） |
| F-03 Resource Management | `ResourceManagement.Instance.GetGold()`、訂閱 `OnGoldChangedEvent` | L1 金幣顯示與即時更新 |
| FT-07 Guild Building System | `BuildingService.Instance.GetBuildingLevel(int buildingID) : int` | `nav_staff_lounge` 解鎖閘判定（SceneNavigationController）；UIBootstrapController step 7 SceneObjectController 初始化亦可能查詢 |
| FT-09 Faction Story System | `FactionStoryService.Instance.GetPendingDialogueStages() : IReadOnlyList<int>`、`GetUnlockedStageIndex(int factionID) : int`、`ConfirmDialogue(int stageID) : ConfirmDialogueResult`；訂閱 `OnFactionStoryStageUnlockedEvent` / `OnFactionStoryStageEpilogueEvent` / `OnFactionStoryStageResolvedEvent` / `OnOpheliaMissingNightEvent` / `OnOpheliaReturnedEvent`（OnFactionRouteCompletedEvent 為可選）| 故事面板觸發、queue 復原、SceneObjectController stage 來源、`_activeEvents` 維護；StageEpilogue 路由給 FSD-B、StageResolved 路由給 FSD-B 委託板 |
| FT-10 Save/Load System | 訂閱 `OnLoadCompletedEvent` 事件 | OnUIReady 啟動序列觸發點 |
| P-01 Desktop Transparent Window | `P01.GetEffectiveScale() / SetUserScale / ResetUserScaleToDefault / GetUserScale / EnumerateAvailableMonitors / SwitchTargetScreen / GetCurrentTargetMonitorID / Minimize`；註冊 `RegisterEffectiveScaleListener(callback)` | UI 縮放、設定彈窗螢幕切換（本 FSD-A 提供 `OnEffectiveScaleChanged` callback；設定彈窗 UI 在 FSD-B） |
| P-03 Notification System | `NotificationService.Instance.BindLogWindow(VisualElement container) : BindResult` | Log 浮動視窗 host 綁定（OnUIReady step 8） |
| EventBus | `EventBus.Subscribe<T>` / `Publish<T>` | 全部跨系統事件訂閱與 `OnUIReady` 發布 |

> 註：FSD 敘述中保留 `IXxxService` 命名供可讀性；實作 PR 直接呼叫上述 concrete singleton（依 FSD-index §2.10 裁決）。

### 2.4 下游被依賴系統

| 系統 | 反向依賴內容 |
| --- | --- |
| P-02-FSD-B（同系統面板）| 本 FSD-A 提供 `PanelManager.OpenPanel/ClosePanel/ShowConfirm`、`PanelStateMachine` 動畫管線、`UITextService.Lookup`、`PersistentHudController` HUD 元素（FSD-B 面板僅渲染面板內容，HUD 由 FSD-A 持有）、`DialogueRenderer.Render`、`SceneObjectController.ResolveSceneObjectState`、`ScreenAnchorCalculator.WorldToPanelLocal` |
| P-01 | 訂閱本系統 `OnUIReady` 事件後啟用 Hit-Test；接收 `panel.Pick(localPos)` 查詢需 P-02 panel 實例存在 |
| P-03 | 訂閱本系統 `OnUIReady` 後初始化 Log 視窗；本系統 OnUIReady step 8 呼叫 `BindLogWindow(container)` |
| FT-09 | 期待本系統訂閱 `OnFactionStoryStageUnlocked` 並對 `specialEventKey == "ophelia_missing"` 在玩家確認對話後 publish `OnOpheliaMissingNight`；ConfirmDialogue 必須在玩家點「確認」後呼叫 |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnUIReadyEvent` | P-02-FSD-A 發布 | （無 payload struct） | UIBootstrapController OnUIReady step 9 發布；P-01 / P-03 訂閱以啟用 Hit-Test 與 Log 視窗 |
| `OnOpheliaMissingNightEvent` | FSD-B StoryDialoguePanel 發布；FSD-A SceneObjectController 訂閱 | （無 payload struct） | 玩家在 Stage 4 故事面板確認對話且 `specialEventKey == "ophelia_missing"` 時，由 FSD-B StoryDialoguePanel 直接 publish；FT-09 §3.6.9 + FSD-A SceneObjectController 同為 subscriber（`_activeEvents.Add("ophelia_missing")` + RefreshAll）|
| `OnLoadCompletedEvent` | P-02-FSD-A 訂閱 | FT-10 payload | OnUIReady 啟動序列觸發點 |
| `OnGoldChangedEvent` | P-02-FSD-A 訂閱 | `{ int newValue, int delta }` | L1 金幣顯示更新 |
| `OnFactionStoryStageUnlockedEvent` | P-02-FSD-A 訂閱 | `{ int stageID, string resolvedDialogueKey, string specialEventKey }` | enqueue stageID 至 `_pendingStageDialogueQueue` tail；若條件成立 chain start |
| `OnFactionStoryStageEpilogueEvent` | P-02-FSD-A 訂閱（路由給 FSD-B）| `{ int stageID, string resolvedEpilogueKey, bool isOpheliaEpilogue, bool subjectAlive }` | epilogue 對話呈現（不入 queue） |
| `OnFactionStoryStageResolvedEvent` | P-02-FSD-A 訂閱（路由給 FSD-B 委託板）| FT-09 payload | 委託板狀態更新 |
| `OnOpheliaReturnedEvent` | P-02-FSD-A 訂閱 | （無 payload struct） | 從 `_activeEvents` 移除，重整場景物件 |

> 本 FSD-A 不發布 `OnPanelOpened` / `OnPanelClosed` 業務事件（GDD 未要求；FSD-B 面板需要時透過直接 callback 回頭處理）。
> `OnOpheliaMissingNightEvent` 的 publisher 為 FSD-B StoryDialoguePanel（玩家「確認」按鈕 callback 內 publish）；FSD-A 為 subscriber。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家打開透明桌面視窗，「即開即看」公會狀態：金幣、委託、冒險者三秒內掌握；不需要任何選單或載入畫面。離開時一切靜靜等待，下次回來的瞬間與離開時一模一樣（GDD §2）。

### 3.2 系統目的還原

P-02 是所有玩家可見介面的根容器與面板調度中心，承載 10 個功能面板，協調初始化順序、面板生命週期、文字資料驅動，並把 gameplay 系統狀態變化反映至 UI（GDD §1）。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 「即開即看」三秒掌握公會狀態 | 啟動後底部欄帶立即顯示金幣、場景、6 個導覽物件；無載入過場 | `OnUIReady` 啟動序列在 FT-10 `OnLoadCompleted` 後 ≤200ms 完成；L1 持久 HUD 在 step 6 初始化；`OnUIReady` 發布後立即可互動 |
| 「公會持續活在桌面上」場景持續可見 | L2 面板開啟時場景透過半透明遮罩仍可見；金幣與 Log 視窗永遠可見 | sortingOrder 分層（L0=0 / L1=100 / L2 modal=200 / L2 panel=210 / Log=250）；modal 遮罩 alpha=0.40；L1 元素 sortingOrder 高於 modal |
| 「文字內容由資料驅動」非硬編碼 | 修改 `UIText.csv` zhTW 欄即可改動所有玩家可見字串 | `UITextService.Lookup(key)` 透過 F-01 DataManager 取 row；不命中 LogError + fallback；FSD-A 與 FSD-B 全面套用 |
| 「故事面板出現時 UI 退到幕後」文字佔滿畫面 | Stage 4 場景說明層字色灰；Stage 5 黑底白字 | `DialogueRenderer` 依 GDD §3.7 切換樣式；`STORY_NOTE_GRAY_HEX` / `STORY_NOTE_BLACK_HEX` 由 `P02UITuning` 提供 |
| 「離開時一切靜靜等待」FT-10 三軌寫入 | 重啟後 pending stage 不遺漏 | OnUIReady step 3 主動查詢 `GetPendingDialogueStages` + FT-09 §3.1.2 Step F 重發雙重保險；`_pendingStageDialogueQueue` enqueue 前 dedupe |
| 「奧菲莉雅敘事物件」場景隨劇情變化 | Stage 升級後場景物件 sprite 切換、可互動 | SceneObjectController 訂閱 FT-09 stage 變動 + `_activeEvents` 維護；`ResolveSceneObjectState(objectID)` 依 priority + stageCondition 解析 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**是**（P-02 GDD 拆分為 FSD-A / FSD-B 兩份）。

### 4.2 拆分理由

對齊 FSD-index §2.4 拆分標準：

1. **單一 FSD 內 Script 數預估超出 §2.4 經驗值上限（3~8 個）**：P-02 GDD §3.1~§3.9 + §3.5（10 個面板）合計預估 24~30 個 Script，總行數 4000~5000 行；單一 FSD 篇幅將超出可維護範圍
2. **GDD 內職責分區明顯**：§3.5「面板規格」（10 個面板各自獨立的資料來源、互動邏輯、版面）與 §3.1~§3.4 + §3.6~§3.9（框架核心、場景整合、啟動握手、文字驅動）為兩個自然職責邊界
3. **狀態歸屬清晰**：FSD-A 持有 `_panelStack`（panel 堆疊狀態）、`_pendingStageDialogueQueue`（劇情隊列）、`_activeEvents`（v3.1 事件集合）；FSD-B 各面板僅持有自身視覺狀態與展開卡片狀態，不影響跨面板邏輯
4. **依賴方向單向**：FSD-A 為 framework + service 層，FSD-B 為 panel 層，FSD-B 依賴 FSD-A 但 FSD-A 不依賴 FSD-B（PanelManager 透過 PanelID enum 路由給 FSD-B 面板，無類型反向依賴）

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |
| FSD-A | Main UI Core | 三層版面、面板狀態機/堆疊、L1 持久 HUD、6 個導覽物件路由、SceneObjectController（v3.1）、對話分頁渲染、`OnUIReady` 啟動握手、`UIText` 文字 lookup、`P02UITuning` 常量、effectiveScale callback | §3.1、§3.2、§3.3、§3.4、§3.5.7、§3.5.9（API only）、§3.6、§3.7、§3.8、§3.9、§4 全節、§7.1/§7.2/§7.4/§7.5 |
| FSD-B | Main UI Panels | 10 個 L2 面板實作（CommissionBoard / AdventurerRoster / GuildBuilding / StaffRoster / StaffGacha / GuildOverview / StoryDialogue / ConfirmPopup / SettingsPanel / LogFloatingWindowHost）；推薦冒險者子面板 | §3.5.1~§3.5.10 |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| P02UITuning | `Assets/Scripts/UI/Core/P02UITuning.cs` | ScriptableObject 收錄視覺/動畫常量與字體大小（§7.1/§7.2） | UnityEngine.ScriptableObject | 60~80 行 |
| PanelTypes | `Assets/Scripts/UI/Core/PanelTypes.cs` | PanelID enum、ConfirmStyle、ConfirmArgs、BindResult、SceneObjectState struct、TextLanguage enum 等型別定義 | （無外部依賴） | 100~140 行 |
| PanelStateMachine | `Assets/Scripts/UI/Core/PanelStateMachine.cs` | 單一面板 Closed/Opening/Open/Closing 狀態機與淡入淡出動畫驅動（§3.4.1、§4.5） | UnityEngine.UIElements、P02UITuning | 140~180 行 |
| PanelManager | `Assets/Scripts/UI/Core/PanelManager.cs` | `_panelStack` 維護、`OpenPanel/ClosePanel/GetTopPanel/ShowConfirm` 對內 API、ESC/modal 遮罩輸入路由（§3.4.2/§3.4.3/§3.4.4） | UIDocument、UIBootstrapController（讀 IsUIReady）、PanelStateMachine、UITextService | 250~320 行 |
| UIBootstrapController | `Assets/Scripts/UI/Core/UIBootstrapController.cs` | OnUIReady 啟動序列 9 步驟、FT-10 OnLoadCompleted 訂閱、Log host 綁定、effectiveScale callback 註冊（§3.8） | EventBus、IDataManager、IFactionStoryService、IBuildingService、ITimeSystem、INotificationService、IDesktopWindow、PanelManager、PersistentHudController、SceneObjectController | 280~360 行 |
| UITextService | `Assets/Scripts/UI/Core/UITextService.cs` | `UIText.csv` 載入快取、`Lookup(key, fallback)` helper、語言切換（Jam 版鎖 zhTW；Post-Jam 預留 en）（§3.9） | IDataManager、UIText row schema | 100~140 行 |
| SceneNavigationController | `Assets/Scripts/UI/Scene/SceneNavigationController.cs` | 6 個導覽用場景物件的點擊路由、hover outline 渲染、`nav_staff_lounge` 解鎖閘 tooltip（§3.2、§4.7） | IBuildingService、PanelManager、ScreenAnchorCalculator、UITextService | 220~260 行 |
| PersistentHudController | `Assets/Scripts/UI/Scene/PersistentHudController.cs` | L1 4 元素：金幣顯示、劇情指示器（含 queue 點擊 resume）、設定按鈕、Log host container（§3.3） | IResourceService、StoryDialogueQueue、PanelManager、ScreenAnchorCalculator | 250~310 行 |
| StoryDialogueQueue | `Assets/Scripts/UI/Scene/StoryDialogueQueue.cs` | `Queue<int> _pending` FIFO 模型；enqueue dedupe；chain start / continue / manual resume；event subscription（§3.3、§3.5.8） | IFactionStoryService、PanelManager、EventBus | 140~180 行 |
| SceneObjectController | `Assets/Scripts/UI/Scene/SceneObjectController.cs` | v3.1 5 個敘事物件 sprite/dialogueKey 切換；`_activeEvents` 維護；`ResolveSceneObjectState(objectID)`；點擊互動觸發故事面板（§3.6） | IDataManager、IFactionStoryService、EventBus、PanelManager、DialogueRenderer | 220~280 行 |
| SceneObjectStateLoader | `Assets/Scripts/UI/Scene/SceneObjectStateLoader.cs` | `SceneObjectStateTable.csv` 載入、依 objectID 分組、依 priority 排序快取 | IDataManager、SceneObjectStateRow schema | 80~110 行 |
| DialogueRenderer | `Assets/Scripts/UI/Scene/DialogueRenderer.cs` | `SplitDialogue(rawText)` 中文逐字計 + 英文單字邊界混排斷行；分頁渲染；Stage 4「她沒回來」灰字；Stage 5 黑底白字（§3.7、§4.4.2） | IDataManager（DialogueTable）、UIDocument、P02UITuning | 160~200 行 |
| ScreenAnchorCalculator | `Assets/Scripts/UI/Scene/ScreenAnchorCalculator.cs` | World-to-Screen 場景內嵌 UI 錨點計算（§4.3）；hover outline 矩形計算（§4.7）；Log 視窗 clamp（§4.6） | UnityEngine.Camera、IDesktopWindow（windowClient）、P02UITuning | 130~170 行 |
| LogFloatingWindowHost | `Assets/Scripts/UI/Core/LogFloatingWindowHost.cs` | UXML container instantiate、呼叫 `INotificationService.BindLogWindow`、處理 BindResult 三回傳碼（§3.5.7） | INotificationService、UIDocument | 80~110 行 |

**預估合計：2110~2660 行 / 14 Script**。

### 4.5 類別關係（可選）

```
UIBootstrapController (Awake → 訂閱 OnLoadCompleted)
    │
    ├─ 啟動 step 1：UIDocument 載入
    ├─ 啟動 step 2：訂閱事件（F-03 / FT-04 / FT-06 / FT-07 / FT-09 / FT-12 ...）
    ├─ 啟動 step 3：StoryDialogueQueue.RestorePendingFromService()
    ├─ 啟動 step 4：UITextService.Initialize() / SceneObjectStateLoader.Initialize()
    ├─ 啟動 step 5：P-01 RegisterEffectiveScaleListener(OnEffectiveScaleChanged)
    ├─ 啟動 step 6：PersistentHudController.Initialize()
    ├─ 啟動 step 7：SceneObjectController.RefreshAll()
    ├─ 啟動 step 8：LogFloatingWindowHost.Bind()
    └─ 啟動 step 9：EventBus.Publish<OnUIReadyEvent>()

PanelManager
    │
    ├─ has-a List<PanelID> _panelStack
    ├─ uses PanelStateMachine （per-panel 動畫 driver）
    ├─ uses UITextService （ConfirmPopup 文字）
    └─ routes to FSD-B panels via PanelID

SceneObjectController
    │
    ├─ uses SceneObjectStateLoader （CSV row 取得）
    ├─ uses IFactionStoryService.GetUnlockedStageIndex
    ├─ has-a HashSet<string> _activeEvents
    └─ uses DialogueRenderer （點擊互動開啟對話）

PersistentHudController
    │
    ├─ has-a StoryDialogueQueue
    ├─ uses IResourceService.OnGoldChanged
    └─ uses ScreenAnchorCalculator （錨點計算）
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// PanelManager（FSD-B 與 FSD-A 內部共用）
public bool OpenPanel(PanelID id, object args = null);
public bool ClosePanel(PanelID id);
public PanelID GetTopPanel();   // _panelStack.Top；空時回 PanelID.None
public void ShowConfirm(string titleKey, string bodyKey,
                        string confirmKey, string cancelKey,
                        Action onConfirm, Action onCancel = null,
                        ConfirmStyle style = ConfirmStyle.Normal);

// UITextService
public string Lookup(string key, string fallback = null);
public string LookupFormat(string key, params object[] args);   // String.Format placeholder
public void SetLanguage(TextLanguage lang);                     // Jam 版鎖 zhTW；Post-Jam 預留

// SceneObjectController
public SceneObjectState ResolveSceneObjectState(string objectID);
public void RefreshAll();   // OnUIReady step 7 呼叫；stage / event 變更時內部自動呼叫

// StoryDialogueQueue
public int Count { get; }
public bool TryDequeueHead(out int stageID);
public void EnqueueIfAbsent(int stageID);   // dedupe enqueue
public void RestorePendingFromService();    // OnUIReady step 3

// PersistentHudController
public VisualElement GetLogHostContainer();   // OnUIReady step 8 由 LogFloatingWindowHost 取得
public void OnEffectiveScaleChanged(float newScale);   // P-01 callback；重算 L1 錨點

// DialogueRenderer
public IReadOnlyList<DialoguePage> SplitDialogue(string rawText);
public void RenderToPanel(VisualElement panel, IReadOnlyList<DialoguePage> pages, int pageIndex);

// ScreenAnchorCalculator
public Vector2 WorldToPanelLocal(Vector3 worldPos, float effectiveScale);
public Rect WorldBoundsToPanelRect(Bounds worldBounds, float effectiveScale);

// UIBootstrapController
public bool IsUIReady { get; }   // OnUIReady 已發布為 true；發布前 OpenPanel 用此判斷
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnUIReadyEvent` | publish | （無欄位 struct） | UIBootstrapController OnUIReady step 9；P-01 / P-03 訂閱 |
| `OnOpheliaMissingNightEvent` | publish | （無欄位 struct） | 玩家確認 Stage 4 對話且 `specialEventKey == "ophelia_missing"` 時由 PanelManager（轉 FSD-B StoryDialoguePanel）publish；FT-09 §3.6.9 + 本系統 SceneObjectController 同為 subscriber |
| `OnLoadCompletedEvent` | subscribe | FT-10 payload | UIBootstrapController.OnLoadCompletedHandler；觸發 OnUIReady 啟動序列 |
| `OnGoldChangedEvent` | subscribe | `{ int newValue, int delta }` | PersistentHudController 更新金幣顯示；負值套紅字 USS class |
| `OnFactionStoryStageUnlockedEvent` | subscribe | `{ int stageID, string resolvedDialogueKey, string specialEventKey }` | StoryDialogueQueue.EnqueueIfAbsent(stageID)；若 PanelManager `_panelStack` 無 StoryPanel 且無 Critical 通知 → chain start |
| `OnFactionStoryStageEpilogueEvent` | subscribe | `{ int stageID, string resolvedEpilogueKey, bool isOpheliaEpilogue, bool subjectAlive }` | 直接觸發故事面板（不入 queue），路由給 FSD-B StoryDialoguePanel |
| `OnFactionStoryStageResolvedEvent` | subscribe | FT-09 payload | 路由給 FSD-B（委託板狀態更新） |
| `OnOpheliaMissingNightEvent` | subscribe | （無 payload） | SceneObjectController._activeEvents.Add("ophelia_missing")；RefreshAll() |
| `OnOpheliaReturnedEvent` | subscribe | （無 payload） | SceneObjectController._activeEvents.Remove("ophelia_missing")；RefreshAll() |

### 5.3 資料結構

```csharp
// PanelTypes.cs
public enum PanelID {
    None = 0,
    CommissionBoard = 1,
    AdventurerRoster = 2,
    GuildBuilding = 3,
    StaffRoster = 4,
    StaffGacha = 5,
    GuildOverview = 6,
    StoryDialogue = 7,
    ConfirmPopup = 8,
    SettingsPanel = 9
    // LogFloatingWindow 不入 _panelStack（L1 元素）
}

public enum PanelState { Closed, Opening, Open, Closing }

public enum ConfirmStyle { Normal, Destructive }

public struct ConfirmArgs {
    public string TitleKey;
    public string BodyKey;
    public string ConfirmKey;
    public string CancelKey;
    public Action OnConfirm;
    public Action OnCancel;
    public ConfirmStyle Style;
}

public enum BindResult { Success, AlreadyBound, InvalidContainer }

public enum TextLanguage { ZhTW, En }

public readonly struct SceneObjectState {
    public readonly string SpriteVariant;
    public readonly string DialogueKey;
    public readonly string AudioCue;
    public static readonly SceneObjectState Default = new("default", string.Empty, string.Empty);
}

public sealed class SceneObjectStateRow {
    public string ObjectID;
    public string StageCondition;
    public string SpriteVariant;
    public string DialogueKey;
    public int Priority;
    public string AudioCue;
}

public sealed class UITextRow {
    public string Key;
    public string ZhTW;
    public string En;
}

public sealed class DialoguePage {
    public IReadOnlyList<string> Lines;   // ≤ DIALOGUE_LINES_PER_PAGE
}
```

```csharp
// P02UITuning.cs（ScriptableObject）
[CreateAssetMenu(fileName = "P02UITuning", menuName = "TheGuild/UI/P02UITuning")]
public sealed class P02UITuning : ScriptableObject {
    public float PanelTransitionSeconds = 0.15f;
    public float ModalOverlayAlpha = 0.40f;
    public int HoverOutlineThickness = 2;
    public float HoverOutlineAlpha = 0.60f;
    public int LogVisibleMargin = 32;
    public int IntroTextMaxDisplayChars = 40;
    public int DialogueLineMaxChars = 24;
    public int DialogueLinesPerPage = 3;
    public string StoryNoteGrayHex = "#888888";
    public string StoryNoteBlackHex = "#000001";
    public int FontSizePanelTitle = 16;
    public int FontSizeCardHeader = 14;
    public int FontSizeCardBody = 11;
    public int FontSizeDialogue = 14;
    public int FontSizeHud = 13;
    public int FontSizeButton = 12;
}
```

### 5.4 內部資料流

#### §5.4.1 OnUIReady 啟動序列（UIBootstrapController）

```
FT-10.SaveLoadService
  → 發布 OnLoadCompletedEvent
  → UIBootstrapController.OnLoadCompletedHandler()
      ├─ step 1：UIDocument 載入 MainSceneDocument + OverlayPanelDocument（若已 Awake 階段載入則 skip）
      ├─ step 2：訂閱事件（F-03 / FT-04 / FT-06 / FT-07 / FT-09 / FT-12 / 其他）
      ├─ step 3：StoryDialogueQueue.RestorePendingFromService()
      │           ├─ 取得 IFactionStoryService.GetPendingDialogueStages()
      │           ├─ foreach stageID in snapshot：EnqueueIfAbsent(stageID)
      │           └─ （與 FT-09 Step F 重發雙重保險；dedupe 避免重複入隊）
      ├─ step 4：UITextService.Initialize()（CSV 載入）
      │         + SceneObjectStateLoader.Initialize()
      ├─ step 5：P01.RegisterEffectiveScaleListener(OnEffectiveScaleChanged)
      │         （P-01 立即推送一次當前 effectiveScale → 套用至 PanelSettings.scale）
      ├─ step 6：PersistentHudController.Initialize()
      │         （建立 4 個 L1 元素 + 套用 World-to-Screen 錨點）
      ├─ step 7：SceneObjectController.RefreshAll()
      │         （依當前 stage + activeEvents 套用初始 sprite）
      ├─ step 8：LogFloatingWindowHost.Bind()
      │         ├─ instantiate LogFloatingWindow UXML container
      │         ├─ container 加入 PersistentHudController.GetLogHostContainer()
      │         ├─ 呼叫 INotificationService.BindLogWindow(container)
      │         └─ switch BindResult：
      │             ├─ Success → no-op
      │             ├─ AlreadyBound → LogWarning「Log 視窗已綁定」no-op
      │             └─ InvalidContainer → LogError + 進入 EC-25 降級
      └─ step 9：EventBus.Publish(new OnUIReadyEvent())
                ├─ IsUIReady = true
                └─ if StoryDialogueQueue.Count > 0：chain start
                    └─ TryDequeueHead → PanelManager.OpenPanel(StoryDialogue, stageID)
```

#### §5.4.2 OpenPanel 流程（PanelManager）

```
FSD-B Panel.OpenSelf(args) 或 SceneNavigationController.OnNavObjectClicked(panelID)
  → PanelManager.OpenPanel(id, args)
      ├─ 檢查 IsUIReady：false → LogWarning + return false
      ├─ 檢查 _panelStack 規則（GDD §3.4.2）：
      │   ├─ id == StoryDialogue → 關閉 _panelStack 中所有 base panel
      │   ├─ id == SettingsPanel → 關閉 _panelStack 中所有 base panel
      │   ├─ id == ConfirmPopup → 不清空（疊加於 base 或 story 之上）
      │   └─ id 為 base panel：
      │       └─ if _panelStack.Last 為 base panel → ClosePanel(_panelStack.Last)
      ├─ 取對應 PanelStateMachine（依 PanelID dict 路由 FSD-B 面板）
      ├─ 呼叫 panel.Open(args) → 進入 Opening 狀態（淡入動畫 PANEL_TRANSITION_SECONDS）
      ├─ _panelStack.Add(id)
      └─ return true
```

#### §5.4.3 ClosePanel 流程

```
任意觸發者
  → PanelManager.ClosePanel(id)
      ├─ if id 不在 _panelStack：LogWarning + return false
      ├─ panel.Close() → 進入 Closing 狀態（淡出動畫）
      ├─ Closing 結束後：display=none → Closed
      └─ _panelStack.Remove(id)
```

#### §5.4.4 ESC 輸入路由

```
UIDocument.OnKeyDown(KeyCode.Escape)
  → PanelManager.OnEscape()
      ├─ if _panelStack 為空 → OpenPanel(SettingsPanel)
      ├─ else top = _panelStack.Last：
      │   ├─ top == ConfirmPopup → ConfirmPopup.OnCancel.Invoke() + ClosePanel(ConfirmPopup)
      │   ├─ top == StoryDialogue → 忽略（必須走「確認」按鈕）
      │   ├─ top == SettingsPanel → ClosePanel(SettingsPanel)
      │   └─ else（其他 base panel）→ ClosePanel(top)
```

#### §5.4.5 modal 遮罩點擊路由

```
modal overlay element.OnClick
  → PanelManager.OnModalOverlayClicked()
      ├─ 等同 OnEscape，但故事面板/確認彈窗 case 提前過濾：
      │   ├─ top == StoryDialogue → 忽略（明示遮罩點擊不關閉）
      │   ├─ top == ConfirmPopup → 忽略（同上；必須走按鈕）
      │   └─ 其餘 → 等同 OnEscape
```

#### §5.4.6 Story Dialogue chain（StoryDialogueQueue + PanelManager）

```
FT-09 publish OnFactionStoryStageUnlockedEvent(stageID, dialogueKey, specialEventKey)
  → StoryDialogueQueue.OnFactionStoryStageUnlockedHandler(payload)
      ├─ EnqueueIfAbsent(stageID)（dedupe）
      └─ if PanelManager.GetTopPanel() != StoryDialogue
         AND IsP03CriticalActive() == false：
            └─ TryDequeueHead → PanelManager.OpenPanel(StoryDialogue, BuildOpenArgsFromUnlockEvent(payload))

IsP03CriticalActive() fallback 邏輯（對齊 §8.3 A-03）：
  ├─ 若 P-03 提供 `INotificationService.IsCriticalNotificationActive() : bool` API → 直接呼叫
  └─ 若 P-03 尚未提供（API 待 P-03 §3 / §5 後續 patch）→ return false（暫不阻塞 chain start；EC-26 採此 fallback）

BuildOpenArgsFromUnlockEvent(payload) → StoryDialogueOpenArgs：
  ├─ StageID = payload.stageID
  ├─ ResolvedDialogueKey = payload.resolvedDialogueKey
  ├─ SpecialEventKey = payload.specialEventKey
  ├─ IsEpilogue = false
  ├─ IsOpheliaInteraction = false
  └─ SkipConfirmCallback = false

FSD-B StoryDialoguePanel 玩家點「確認」按鈕
  → 呼叫 IFactionStoryService.ConfirmDialogue(stageID) → 回 SUCCESS
  → if specialEventKey == "ophelia_missing"
        → EventBus.Publish(new OnOpheliaMissingNightEvent())   // FSD-B 為 publisher
  → PanelManager.ClosePanel(StoryDialogue)
  → Closing 動畫結束後：
      ├─ if StoryDialogueQueue.Count > 0 AND IsP03CriticalActive() == false：
      │     └─ TryDequeueHead → PanelManager.OpenPanel(StoryDialogue, BuildOpenArgs(stageID))（chain continue）
      └─ else：no-op

PersistentHudController 玩家點劇情指示器
  → if StoryDialogueQueue.Count > 0：
      └─ TryDequeueHead → PanelManager.OpenPanel(StoryDialogue, BuildOpenArgs(stageID))（manual resume）
```

#### §5.4.7 SceneObjectController.ResolveSceneObjectState

```
caller.ResolveSceneObjectState(objectID)
  → rows = SceneObjectStateLoader.GetByObjectID(objectID)（依 priority 由高至低排序快取）
  → currentStage = IFactionStoryService.GetUnlockedStageIndex(factionID = 1)
  → foreach row in rows：
        if EvaluateStageCondition(row.StageCondition, currentStage, _activeEvents)：
            return new SceneObjectState(row.SpriteVariant, row.DialogueKey, row.AudioCue)
  → return SceneObjectState.Default

EvaluateStageCondition(condition, currentStage, activeEvents):
  ├─ condition starts with "stageID >="：return currentStage >= ParseInt
  ├─ condition starts with "stageID =="：return currentStage == ParseInt
  ├─ condition starts with "stageID <" ：return currentStage <  ParseInt
  ├─ condition starts with "event:"   ：return activeEvents.Contains(name)
  └─ else：LogError; return false
```

#### §5.4.8 SceneObjectController 物件點擊互動

```
玩家點擊 ophelia_chair / ophelia_teacup 等 v3.1 場景物件
  → SceneObjectController.OnSceneObjectClicked(objectID)
      ├─ state = ResolveSceneObjectState(objectID)
      ├─ if state.DialogueKey 為空 → 不可點，no-op
      └─ PanelManager.OpenPanel(StoryDialogue, new StoryDialogueOpenArgs {
              StageID = 0,                                  // 場景物件互動非 stage 流程，stageID 占位
              ResolvedDialogueKey = state.DialogueKey,
              SpecialEventKey = string.Empty,
              IsEpilogue = false,
              IsOpheliaInteraction = true,
              SkipConfirmCallback = true                    // 場景物件互動不呼叫 ConfirmDialogue
          })
         // FSD-B StoryDialoguePanel 收到後依 ResolvedDialogueKey 自行查 DialogueTable + SplitDialogue（與 FT-09 事件路徑統一邏輯路徑，避免兩條觸發路徑型別 mismatch）
```

#### §5.4.9 EffectiveScale 推送處理

```
P-01 解析度變更 / SetUserScale / SwitchTargetScreen
  → P01 呼叫 P-02 註冊的 callback
  → PersistentHudController.OnEffectiveScaleChanged(newScale)
      ├─ 套用所有 PanelSettings.scale = newScale
      ├─ ScreenAnchorCalculator.RecalculateAll()（重算 4 個 L1 元素錨點）
      ├─ SceneNavigationController.RecalculateHoverRects()（hover outline 矩形）
      └─ LogInfo("EffectiveScale changed: {newScale}")
```

#### §5.4.10 UITextLookup（UITextService）

```
caller.Lookup(key, fallback = null)
  → if !_loaded：LogWarning "UITextService not initialized" + return fallback ?? key
  → if _table.TryGetValue(key, out row)：
        return _currentLanguage == ZhTW ? row.ZhTW : row.En
  → LogError "UIText key missing: {key}"
  → return fallback ?? key
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `UIText.csv` | `key, zhTW, en` | `【P-02-DS】ui-text.md`（_待建_） | 全 UI 玩家可見字串資料源；UITextService.Initialize() 一次性載入後快取 | OnUIReady step 4 |
| `SceneObjectStateTable.csv` | `objectID, stageCondition, spriteVariant, dialogueKey, priority, audioCue` | `【P-02-DS】scene-object-state-table.md`（_待建_） | SceneObjectController 解析來源；SceneObjectStateLoader.Initialize() 載入後依 objectID 分組、依 priority 由高至低排序快取 | OnUIReady step 4 |
| `DialogueTable`（消費端，owner 待 P-02 啟動 design-review 後決定）| `dialogueID, speakerId, context, text` | （消費端，本 FSD 不擁有 DS） | 對話文字內容；DialogueRenderer / SceneObjectController 點擊互動時透過 DataManager 查詢 | OnUIReady step 4（懶載入也可） |

### 6.2 引用的 ScriptableObject

| ScriptableObject | 路徑 | 用途 | 載入時機 |
| --- | --- | --- | --- |
| `P02UITuning` | `Assets/Resources/Data/Tuning/P02UITuning.asset` | 視覺/動畫常量、字體大小、顏色 hex；§7.1/§7.2 全部欄位 | Awake 時 `Resources.Load<P02UITuning>` |

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `PANEL_TRANSITION_SECONDS` | P02UITuning.PanelTransitionSeconds | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `MODAL_OVERLAY_ALPHA` | P02UITuning.ModalOverlayAlpha | 同上 |
| `HOVER_OUTLINE_THICKNESS` | P02UITuning.HoverOutlineThickness | 同上 |
| `HOVER_OUTLINE_ALPHA` | P02UITuning.HoverOutlineAlpha | 同上 |
| `LOG_VISIBLE_MARGIN` | P02UITuning.LogVisibleMargin | 同上 |
| `INTRO_TEXT_MAX_DISPLAY_CHARS` | P02UITuning.IntroTextMaxDisplayChars | 同上 |
| `DIALOGUE_LINE_MAX_CHARS` | P02UITuning.DialogueLineMaxChars | 同上 |
| `DIALOGUE_LINES_PER_PAGE` | P02UITuning.DialogueLinesPerPage | 同上 |
| `STORY_NOTE_GRAY_HEX` | P02UITuning.StoryNoteGrayHex | 同上 |
| `STORY_NOTE_BLACK_HEX` | P02UITuning.StoryNoteBlackHex | 同上 |
| 字體大小 6 個 (`FONT_SIZE_*`) | P02UITuning.FontSize* | 同上 |
| 全 UI 玩家可見字串 | UIText.csv | 同上（亦對應 `.claude/rules/ui-code.md`「All UI text must go through the localization system」） |
| 場景物件 sprite 變體 / dialogueKey 對映 | SceneObjectStateTable.csv | 同上 |
| `nav_staff_lounge` 解鎖閘 building level 閾值（=1） | GDD §3.2 表格固定值，作為程式碼常量 `STAFF_LOUNGE_BUILDING_ID = 6`、`STAFF_LOUNGE_UNLOCK_LEVEL = 1` | 不違反——導覽路由為固定路由表（GDD §3.2「互動點不在 SceneObjectStateTable」），程式碼常量合規；buildingID 6 對齊 FT-07 BuildingTable owner 編號 |
| 6 個 nav 物件 → PanelID 對映 | GDD §3.2 表格固定路由，程式碼 `Dictionary<string, PanelID> NavObjectMap` 常量 | 不違反——同上理由；不隨 stage 變化 |
| `SortingOrder.*` 7 個常量 | GDD §7.4 標明「不對外調整」（程式碼常量） | 不違反——固定 z-order 為架構決策，不視為 tuning |
| 審查處 buildingID（用於開除按鈕判定）| Jam 版採程式碼常量 `REVIEW_OFFICE_BUILDING_ID`（值待 FT-07 P3.1-009 補登）+ `IBuildingService.GetBuildingLevel(REVIEW_OFFICE_BUILDING_ID) > 0` 判斷；常量值未確認時（≤ 0）由 AdventurerRosterPanel 在初始化檢查時 LogWarning 並隱藏所有 Idle 冒險者的「開除」按鈕（不阻擋名冊面板載入）| 不違反——導覽路由為固定常量；FT-07 補 P3.1-009 後改用 `IsBuildingUnlocked(buildingID)` 統一接口 |

---

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| EC-01：OpenPanel 在 OnUIReady 之前被呼叫 | `PanelManager.OpenPanel` 第一行檢查 `UIBootstrapController.IsUIReady`；false → `LogWarning("OpenPanel called before OnUIReady: {id}")` + return false | PanelManager、UIBootstrapController | EditMode test：建立 PanelManager 不發布 OnUIReady，呼叫 OpenPanel(CommissionBoard)，斷言 LogWarning + return false |
| EC-02：UIText.csv 載入失敗 | `UITextService.Initialize` catch + LogError；`_loaded = false`；`Lookup` fallback 為「回傳 fallback ?? key」；OnUIReady step 9 仍照常發布 | UITextService、UIBootstrapController | EditMode test：DataManager 回 null，呼叫 Initialize；Lookup("any.key") 回 "any.key" |
| EC-03：SceneObjectStateTable.csv 載入失敗 | `SceneObjectStateLoader.Initialize` catch + LogError；`_table = empty`；`SceneObjectController.ResolveSceneObjectState` 全回 Default；OnUIReady 仍發布 | SceneObjectStateLoader、SceneObjectController | EditMode test：CSV 缺失，斷言 ResolveSceneObjectState 任意 objectID 回 Default |
| EC-04：OnUIReady 前 FT-09 已發布 OnFactionStoryStageUnlocked | 雙重保險：(1) OnUIReady step 2 訂閱事件（事件抵達自動 enqueue）；(2) step 3 主動 `RestorePendingFromService` 補捉 Phase C 期間發布；StoryDialogueQueue.EnqueueIfAbsent dedupe 防止重複 | UIBootstrapController、StoryDialogueQueue | EditMode test：模擬 FT-09 service 在 step 2 之前已 publish stage 3，service.GetPendingDialogueStages 回 [3]；OnUIReady 完成後斷言 queue 含 stage 3 一次（非兩次）|
| EC-05：同 frame 兩個面板開啟請求 | `PanelManager.OpenPanel` 為同步函式；第一個請求進入 Opening 後立即返回；第二個請求依 §3.4.2 規則處理（base panel 互斥則先 ClosePanel 第一個）；序列化處理無 race | PanelManager | EditMode test：同 frame 連呼 OpenPanel(CommissionBoard) + OpenPanel(AdventurerRoster)，斷言最終 _panelStack 只含 AdventurerRoster |
| EC-06：Opening / Closing 動畫期間玩家點擊 | `PanelStateMachine` 進入 Opening / Closing 時將 panel root `pickingMode = Ignore`；UIToolkit picking 自動穿透至底層；P-01 Hit-Test 收到 panel.Pick 為 null → 視為 transparent | PanelStateMachine | EditMode test：注入 panel 在 Opening 時 panel.pickingMode == Ignore |
| EC-07：ConfirmPopup Opening 中 ESC 被按下 | `PanelManager.OnEscape` 檢查 panel state；若 == Opening → 排程「動畫結束後執行 OnCancel」（用 UI Toolkit `schedule.Execute(...).After(remainingMs)`）；不打斷動畫 | PanelManager、PanelStateMachine | EditMode test：Opening 中按 ESC，斷言 OnCancel 在動畫結束後被呼叫 |
| EC-08：故事面板開啟期間 FT-09 再次發布 OnFactionStoryStageUnlocked | StoryDialogueQueue 訂閱事件 → EnqueueIfAbsent 至 tail；不打斷當前對話；當前 ConfirmDialogue SUCCESS 後 chain continue 自動 dequeue head | StoryDialogueQueue、PanelManager | EditMode test：故事面板開 stage 3 中，publish stage 4；確認 stage 3 後斷言 stage 4 自動開啟 |
| EC-09：ConfirmPopup 疊加於 StoryDialogue 上 ESC 被按下 | `PanelManager.OnEscape` top == ConfirmPopup → 等同 OnCancel + ClosePanel(ConfirmPopup)；不影響底層 StoryDialogue | PanelManager | EditMode test：開故事面板 + 觸發 ShowConfirm；按 ESC 斷言 ConfirmPopup 關 + StoryDialogue 仍開 |
| EC-15：面板 Open 期間解析度變更 | P-01 推送新 effectiveScale → `OnEffectiveScaleChanged` callback；套用所有 PanelSettings.scale；`ScreenAnchorCalculator.RecalculateAll`；不關閉面板 | PersistentHudController、ScreenAnchorCalculator | 手動驗收：開委託板 → 變更解析度，確認面板仍開 + 尺寸跟隨 |
| EC-16：面板 Opening 中 effectiveScale 變更 | PanelStateMachine opacity lerp 不依賴 scale；新 scale 在動畫 mid 套用無視覺中斷 | PersistentHudController、PanelStateMachine | 手動驗收 |
| EC-19：UITextLookup(key) 命中失敗 | `UITextService.Lookup` LogError + return fallback ?? key；不拋例外 | UITextService | EditMode test：`UITextLookup("missing.key")` 回 "missing.key"；Console 含 LogError |
| EC-20：DialogueTable 找不到 dialogueKey | `DialogueRenderer` 檢查 row 為 null → 顯示 `[對話缺失：{dialogueKey}]`；「確認」按鈕仍可點；故事面板 ConfirmDialogue 流程不變 | DialogueRenderer、StoryDialoguePanel（FSD-B 訂閱） | EditMode test：注入未知 key，斷言 RenderToPanel 顯示佔位文字 |
| EC-21：SceneObjectStateTable row 全部 stageCondition 不成立 | `ResolveSceneObjectState` foreach 走完所有 row 仍未命中 → return Default | SceneObjectController | EditMode test：注入 stageID=99 高於所有 row 條件，斷言回 Default |
| EC-22：stageCondition 語法錯誤 | `EvaluateStageCondition` LogError；該 row 視為不成立；繼續評估下一 row | SceneObjectController | EditMode test：注入 `condition = "INVALID"` row，斷言 LogError + 繼續評估 |
| EC-23：sprite 資產載入失敗 | DialogueRenderer / SceneObjectController 在 `Resources.Load<Sprite>` 回 null 時 LogError + 套用 `Sprite.Empty` placeholder（1×1 透明）；不影響 dialogueKey 互動 | SceneObjectController | EditMode test：模擬資源缺失，斷言不拋例外 + UI 顯示空白 |
| EC-24：P-01 在 OnUIReady 之前 Hit-Test | P-01 自身規範等待 OnUIReady 才啟用 Hit-Test；本 FSD 不需特殊處理；若仍發生，P-01 panel.Pick 回 null → 全視窗穿透；本 FSD 無錯誤路徑 | （無，由 P-01 處理） | 觀察行為（依 P-01 §3.2 規範） |
| EC-25：LogFloatingWindowHost instantiate 失敗 | `LogFloatingWindowHost.Bind` try-catch 包覆 `INotificationService.BindLogWindow`；任一階段例外 → LogError + 設 `_logHostFailed = true`；OnUIReady step 9 仍發布；遊戲可玩但 Log 視窗不顯示 | LogFloatingWindowHost、UIBootstrapController | EditMode test：mock NotificationService 拋例外，斷言 OnUIReady 仍發布 |
| EC-26：P-03 Critical 通知與故事面板同 frame 觸發 | StoryDialogueQueue 訂閱 P-03 Critical 通知事件（如有）；Critical 顯示中時暫不 chain start；P-03 通知關閉後再 dequeue head（chain resume） | StoryDialogueQueue | 手動驗收：模擬 Stage unlock 與破產警告同時，確認破產警告先顯示 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 整體版面架構 | §4.4（Script 清單）、§5.3（資料結構）、§6.3（z-order 常量）、§7（EC-15/EC-16） | 對齊 | sortingOrder 常量收於 PanelTypes（程式碼）；7 個值依 §7.4 視為架構常量不對外調整 |
| §3.2 場景物件互動點 | §4.4 SceneNavigationController、§5.4.4（OnEscape 不路由）、§5.4.7~§5.4.8 排除（v3.1 物件走 SceneObjectController） | 對齊 | 6 個導覽物件為固定路由（GDD §3.2 明示「互動點不在 SceneObjectStateTable」）；買家解鎖閘 `nav_staff_lounge` 走 `IBuildingService.GetBuildingLevel(6) >= 1` |
| §3.3 持久 UI 元素 | §4.4 PersistentHudController、§5.4.6 chain start/continue/manual resume、§5.4.9 effectiveScale | 對齊 | queue model FIFO + dedupe；金幣訂閱 `OnGoldChanged(newValue, delta)` 事件 |
| §3.4 面板生命週期/堆疊/輸入路由/OpenPanel API | §4.4 PanelManager + PanelStateMachine、§5.1 公開 API、§5.4.2~§5.4.5 | 對齊 | 動畫 0.15s 對齊 §4.5；ESC 與 modal 遮罩路由完整 |
| §3.5.7 Log 浮動視窗 host | §4.4 LogFloatingWindowHost、§5.4.1 step 8、EC-25 | 對齊 | BindResult 三回傳碼處理（Success / AlreadyBound / InvalidContainer）；P-02 為 caller，P-03 為 callee |
| §3.5.9 ConfirmPopup API 簽章 | §5.1 `ShowConfirm` 簽章、§5.3 ConfirmArgs / ConfirmStyle | 對齊 | UI 視覺實作在 FSD-B；本 FSD 提供 API 入口 |
| §3.6 SceneObjectController | §4.4 SceneObjectController + SceneObjectStateLoader、§5.4.7~§5.4.8、EC-21~EC-23、EC-03 | 對齊 | 5 個 v3.1 objectID × 10 row；priority 排序；activeEvents 維護 |
| §3.7 對話視窗呈現 | §4.4 DialogueRenderer、§5.4.7（SceneObjectController 互動）、§6.2 P02UITuning、EC-20 | 對齊 | Stage 4 灰字 #888888、Stage 5 黑底 #000001；3 行 24 字限制 |
| §3.8 OnUIReady 啟動握手 | §5.4.1（9 步驟）、EC-01 / EC-02 / EC-03 / EC-04 | 對齊 | step 3 主動查詢 + Step F 重發雙重保險；step 5 effectiveScale callback 註冊 |
| §3.9 UIText 路由 | §4.4 UITextService、§5.1 Lookup/LookupFormat、EC-19 | 對齊 | dynamic 字串走 String.Format placeholder；DialogueTable 不走此路由 |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | 對應 FSD 實作 | 對齊／替代 |
| --- | --- | --- |
| §4.2 PanelSettings.scale = P01.GetEffectiveScale() | `PersistentHudController.OnEffectiveScaleChanged(newScale)` 套用 `PanelSettings.scale = newScale` | 對齊 |
| §4.3 World-to-Screen | `ScreenAnchorCalculator.WorldToPanelLocal`：依公式 `panelLocalY = (windowClient.height - screenPos.y) / effectiveScale` 加 offsetY | 對齊 |
| §4.4.2 SplitDialogue 偽碼 | `DialogueRenderer.SplitDialogue` 直接照偽碼實作；中文逐字計、英文單字邊界、混排優先（標點 > 空白 > 強制中字斷行）| 對齊 |
| §4.5 opacity Lerp | `PanelStateMachine.UpdateAnimation(deltaTime)` 依 `opacity(t) = Lerp(fromAlpha, toAlpha, t / PANEL_TRANSITION_SECONDS)` | 對齊 |
| §4.6 Log 視窗 clamp | `ScreenAnchorCalculator.ClampLogWindow`：依 visibleAnchor + LOG_VISIBLE_MARGIN 規則 | 對齊 |
| §4.7 hover outline 矩形 | `ScreenAnchorCalculator.WorldBoundsToPanelRect` 依 spriteRenderer.bounds 反推 UXML 矩形 | 對齊 |
| §4.8 範例計算 | （單純範例，無實作對應；用於驗證 §4.3 / §4.4.2 / §4.5 結果）| n/a |

### 8.3 未能實現的規則與修改建議

| 編號 | 條目 | 性質 | 建議 |
| --- | --- | --- | --- |
| A-01 | 開除按鈕「審查處 buildingID」未在 GDD 與 FT-07 表格化 | 暫時阻塞 | Jam 版採程式碼常量 `REVIEW_OFFICE_BUILDING_ID`（值待 FT-07 P3.1-009 補登）+ `IBuildingService.GetBuildingLevel(REVIEW_OFFICE_BUILDING_ID) > 0` 判斷；常量值未確認（≤ 0）時 AdventurerRosterPanel.OnEnable LogWarning + 隱藏所有 Idle 冒險者「開除」按鈕；FT-07 P3.1-009 補登後改用 `IsBuildingUnlocked(buildingID)` 統一接口（§6.3 已對齊此決議）|
| A-02 | DialogueTable owner 待定 | 跨 FSD | 短期 P-02 從 F-01 DataManager 直查 DialogueTable；長期 owner 由 P-02 啟動 design-review 或 narrative 模組決定（依 FSD-self-check-log T6 暫緩）|
| A-03 | P-03 Critical 通知插隊 chain | 建議性 | EC-26 需 P-03 提供 `INotificationService.IsCriticalNotificationActive() : bool` 查詢或 `OnCriticalNotificationStateChangedEvent` 事件；P-03 §3 / §5 待 patch；本 FSD §5.4.6 `IsP03CriticalActive()` helper 已明示 fallback 邏輯：API 未提供時 return false（暫不阻塞 chain start，EC-26 採此 fallback；P-03 補 API 後 helper 內改呼叫真實 API）|
| A-04 | P-02-DS UIText / SceneObjectStateTable 待建 | 文件 | DS-designer 後續以 `/design-DS P-02` 補；本 FSD §6.1 / §2.2 標 _待建_ |
| A-05 | userScale slider step `USER_SCALE_STEP` 引用 | 跨 FSD | 設定彈窗實作於 FSD-B；FSD-A 不直接持有；FSD-B 透過 P-01 §7 取常量 |
| A-06 | ISaveable 實作（P-02 「最後關閉的 base panel ID」可選持久化） | 建議性 | GDD §6.1 FT-10 row 提到 P-02 僅持久化「最後關閉 base panel ID（可選）」；Jam 版可選擇不實作；若實作則 OwnerKey = `"P-02-LastPanel"`，Serialize/RestoreFromSave 直接寫 PanelID int；本 FSD 預留介面待人類覆核決定 |

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| — | — | — | （本 FSD 無 GDD 回註；GDD 規則皆對齊或以 §8.3 條目處理）|

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | — | — | （本 FSD 撰寫期間無真實衝突；GDD v0.6 已通過五輪 design-review，跨系統 API 對齊已完成）|

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥
- [x] §1.3 完成目標可被測試驗證（DoD-A1~A14）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節、Data-Specs、上游、下游、事件契約）
- [x] §3.3 對映表覆蓋所有幻想／目的（6 條）
- [x] §4 Script 清單欄位齊全（14 個 Script）
- [x] §5 API/事件/資料結構/資料流齊備（5.1~5.4）
- [x] §6 CSV 引用含對應 Data-Specs；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（EC-01~EC-26 中與 FSD-A 相關 17 條皆列）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（10 條）
- [x] §8.2~§8.5 如實登記
- [x] FSD-index §6.1 / §7.1 / §7.2 / §7.3 已同步更新（隨後補丁）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-05-02 | Claude Code 主體（Opus 4.7 + xhigh）| 通過 | 通過 | 通過 | 結構：8 節 + 附錄 A 全填、編號順序對齊 FSD-index §三；邏輯：Script 職責 SRP 明確、API/事件/資料流前後一致、雙重保險（OnUIReady step 3 + FT-09 Step F）對 EC-04 妥善處理；GDD 對齊：§3.1~§3.9 / §4 全節 / §5 EC-01~EC-26 中 17 條與 FSD-A 相關全有對策、§7.1/§7.2/§7.4/§7.5 全納入 P02UITuning + 程式碼常量、§8 AC 對應 DoD 14 條；建議項 A-01~A-06 皆不阻擋實作 |
| 2026-05-02 | Claude Code 主體（Opus 4.7 + xhigh）—— `/design-review P-02-FSD` NEEDS REVISION patch | 通過 | 通過 | 通過 | design-review 14 條 issue 全修。**FSD-A 修補項**：(C2) §5.2 OnOpheliaMissingNightEvent publisher 明示為 FSD-B StoryDialoguePanel；表格末加 publisher / subscriber 註解。(C3) §2.5 / §5.2 全部事件名稱統一加 `Event` 後綴對齊 FT-12 既定慣例。(C4) §2.3 補 FT-07 IBuildingService row、FT-09 訂閱事件清單補完（5 個事件）。(C5) §4.4 PanelManager 依賴介面 `IBootstrapState` → `UIBootstrapController`（讀 IsUIReady）。(C1) §5.4.8 SceneObjectController 點擊互動改用 typed `StoryDialogueOpenArgs`（移除匿名物件 dialoguePages 欄位），統一兩條觸發路徑（FT-09 事件 / 場景物件互動）由 FSD-B 自行 SplitDialogue。(I4) §6.3 + §8.3 A-01 審查處 buildingID fallback 二擇一決議：Jam 版採 `REVIEW_OFFICE_BUILDING_ID` 程式碼常量 + `GetBuildingLevel(...) > 0` 判斷；常量值 ≤ 0 時 LogWarning + 隱藏開除按鈕。(I5) §5.4.6 chain start 補 `IsP03CriticalActive()` helper 與 fallback 邏輯（API 未提供時 return false）；§8.3 A-03 同步說明。本次 patch 不變動章節結構、不影響 §1.3 DoD / §8.1 / §8.2 / §8.5 既有判定 |
