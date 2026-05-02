# 【P-02】Main UI Framework

_建立時間：2026-04-26_
_最後更新：2026-05-01_
_狀態：✅ 已設計（v0.6 / 2026-05-01 五輪 design-review APPROVED）_

---

## §1 概覽（Overview）

P-02 Main UI Framework 是所有玩家可見介面的根容器與面板調度中心。系統基於 Unity UI Toolkit（UXML/USS）實作，承載 10 個功能面板：委託板、冒險者名冊、公會建設、職員名冊（FT-12）、職員面試（FT-08）、公會總覽、通知 Log（P-03 浮動視窗）、故事面板、確認彈窗、遊戲設定彈窗。P-02 負責面板的顯示/隱藏調度、初始化順序協調，以及將各 gameplay 系統（FT 層）的狀態變化反映至對應 UI。所有 UI 元素的縮放由 P-01 提供的 `effectiveScale`（`PanelSettings.scale`）統一控制；P-02 在根面板載入完成後發布 `OnUIReady` 事件，通知 P-01 啟用點擊穿透邏輯、通知 P-03 初始化浮動 Log 視窗。通知文字內容由 `UIText.csv` 資料驅動，程式碼不硬編碼任何 UI 字串。

---

## §2 玩家幻想（Player Fantasy）

玩家打開透明桌面視窗，不需要任何選單或載入畫面——委託板和名冊就在眼前，三秒內掌握公會當前狀態：有幾個委託等待審核、哪些冒險者在任務中、金幣是否充裕。這個「即開即看」的瞬間對應支柱 4「桌面原生」——UI 不是需要專注操作的工具，而是一扇隨時可以瞄一眼的窗口。當玩家需要更深入的管理時，切換至建設或職員面板；離開時一切靜靜等待，下次回來的瞬間與離開時一模一樣。故事面板出現時（陣營劇情節點、遊戲結束），UI 退到幕後，文字佔滿畫面——這是玩家與公會命運正面相遇的時刻。

---

## §3 詳細規則（Detailed Rules）

> **版面參考示意圖**：`design/DesignFiles/Art/IconInstructions.png`（透明桌面視窗示意圖：底部欄帶為公會場景，左側為公會建築與委託欄，右側為冒險者區域）
> **v3.1 整合**：奧蘿瑞女神陣營劇本（patch P3.1-010）的 `SceneObjectController` 與對話視窗呈現規範收錄於 §3.6 / §3.7

### §3.1 整體版面架構（Three-Layer Scene-First Model）

P-02 採 **Scene-First** 版面：底部欄帶（高度由 P-01 `WINDOW_HEIGHT_RATIO=0.30` 決定）為唯一視覺空間，所有 UI 皆建構於此欄帶內。版面分為三層，由低至高：

| 層級 | 內容 | UI Document | 是否阻擋輸入 |
|---|---|---|---|
| **L0 場景層** | 公會場景背景（建築物、地面、遠景）、閒置冒險者立繪、奧菲莉雅相關場景物件（v3.1） | `MainSceneDocument`（UXML 根） | 否（穿透至 P-01 Hit-Test） |
| **L1 場景內嵌 UI** | 金幣顯示（嵌保險箱上方）、劇情進展指示器（嵌保險箱旁）、設定按鈕（嵌辦公桌）、Log 浮動視窗（P-03 host） | 與 L0 同一 panel；以 USS `position: absolute` 錨點到對應場景物件螢幕座標 | 元素本身阻擋；元素外不阻擋 |
| **L2 覆蓋面板層** | 10 個面板（§3.5）；同時最多兩層（base panel + confirm popup） | `OverlayPanelDocument`（獨立 UI Document，sortingOrder 高於 L0/L1） | 開啟時整層阻擋（modal overlay 半透明遮罩） |

**設計理由**：
- 場景層持續可見（不被 panel 完全遮蔽），對應 §2「公會持續活在桌面上」
- L1 嵌入式 UI 不獨立常駐——金幣與劇情指示器附著於對應場景物件，玩家視覺焦點跟著場景走
- L2 modal overlay 半透明遮罩讓玩家清楚知道「正在面板裡」；按 ESC 或點空白可關閉（除故事面板與確認彈窗外）

**L2 modal 遮罩**：開啟任一 L2 面板時，於 L0/L1 上方繪製半透明黑色遮罩（alpha=0.4）；點擊遮罩等同 ESC 關閉最頂層 L2 面板（故事面板與確認彈窗例外，必須走「確認/取消」按鈕）。

---

### §3.2 場景物件互動點（Scene-First Navigation）

L0 場景中，以下 6 個物件為玩家點擊的導覽入口；其餘場景物件不可點擊（v3.1 SceneObjectController 管理的奧菲莉雅敘事物件除外，見 §3.6）。

| 場景物件 | objectID | 對應面板 | 觸發條件 | hover 視覺回饋 |
|---|---|---|---|---|
| 委託欄（建築 1）| `nav_commission_board` | §3.5.1 委託板 | 永遠可點 | 物件外緣 outline（白光，alpha=0.6） |
| 冒險者名冊入口（建築 3）| `nav_guild_hall` | §3.5.2 冒險者名冊 | 永遠可點 | 同上 |
| 建設區（場景中央獨立物件，非任一建築）| `nav_construction` | §3.5.3 公會建設 | 永遠可點 | 同上 |
| 預備金保險箱（建築 5）| `nav_safe` | §3.5.6 公會總覽 | 永遠可點 | 同上 |
| 職員休息室沙發（建築 6）| `nav_staff_lounge` | §3.5.4 職員名冊 | `FT07.GetBuildingLevel(6) >= 1` | 同上；未解鎖時顯示半透明灰色 outline + tooltip「需建造職員休息室 L1」 |
| 辦公桌 | `nav_settings_desk` | §3.5.10 設定彈窗 | 永遠可點 | 同上 |

**hover 與點擊處理**：
- hover outline 由 P-02 自行渲染（USS `:hover` outline，依物件 spriteRenderer bounds 反推 UXML 元素矩形）
- 點擊事件由 UXML 元素接收（透過 P-01 Hit-Test 確認命中後路由）
- 未解鎖物件保留 outline 但點擊不開面板，僅顯示 tooltip

**互動點不在 SceneObjectStateTable**：上表 6 行為固定路由表（程式碼常量），不隨 stage 變化；v3.1 `SceneObjectStateTable.csv` 只管理 §3.6 列出的奧菲莉雅敘事物件。

---

### §3.3 持久 UI 元素（Always-Visible HUD）

L1 層常駐 4 個元素，不被 L2 modal 遮罩遮蔽（`sortingOrder` 高於 modal）：

| 元素 | 螢幕錨點 | 內容 | 點擊行為 |
|---|---|---|---|
| 金幣顯示 | 嵌保險箱上方（建築 5 物件 sprite 上方 16px） | `{F03.GetGold()} g`；負值紅字 | 不可點（純資訊） |
| 劇情進展指示器 | 嵌保險箱右側 | 圖示（信封/卷軸）+ 紅點計數（`_pendingStageDialogueQueue.Count`）；計數為 0 時隱藏 | 點擊從隊列 head dequeue 並開啟故事面板（FIFO，與自動觸發一致；用於 chain 中斷後手動 resume，見下方說明） |
| 設定按鈕 | 嵌辦公桌 | 齒輪圖示 | 開啟設定彈窗（§3.5.10） |
| 通知 Log（P-03 host）| 預設左側中段（FT-10 持久化位置） | P-03 浮動視窗（§3.5.7 為 host 規範） | 拖曳/縮放/最小化（行為由 P-03 §3.2 定義） |

**金幣顯示的事件訂閱**：P-02 訂閱 F-03 `OnGoldChanged(int newValue, int delta)` 事件即時更新；不做 per-frame polling。

**劇情對話隊列模型（FIFO）**：P-02 內部維護 `_pendingStageDialogueQueue: Queue<int stageID>`，記錄已收到 `OnFactionStoryStageUnlocked` 但尚未顯示的 stageID（payload 細節透過 FT-09 / DialogueTable 即時查詢，不快取於 queue 內，避免 stage 內容跨版本變動時資料過時）：

| 觸發 | 對 queue 操作 | 後續動作 |
|---|---|---|
| 收到 `OnFactionStoryStageUnlocked(stageID, ...)` | enqueue stageID 到 tail（若 queue 內已含同 stageID 則跳過，避免 §3.8 step 3 主動查詢與 Step F 重發雙重保險的重複入隊）| 若當前無故事面板顯示且無更高優先級 panel（如 Critical 通知）→ 立即 dequeue head 並開啟故事面板（chain start）|
| 玩家在故事面板按「確認」 → P-02 呼叫 `FT09.ConfirmDialogue(stageID)` 回 `SUCCESS` | 該 stageID 已在 dequeue 時移除，不再操作 queue | 故事面板關閉動畫完成後，若 queue 非空 → 自動 dequeue head 並開啟下一個故事面板（chain continue） |
| Chain 中斷情境（P-03 Critical 通知插隊、玩家觸發其他 modal、應用程式失焦等）導致 chain 暫停 | queue 保持當前內容，不變 | 玩家點擊劇情指示器 → 從 head dequeue 並開啟故事面板（手動 chain resume） |
| `OnFactionStoryStageEpilogue` 事件 | **不**入此 queue（直接觸發故事面板，特殊單次播放，見 §3.5.8）| — |
| `OnOpheliaMissingNight` / `OnOpheliaReturned` | **不**入此 queue（前者為場景說明層，後者為單次對話，見 §3.5.8）| — |

**FIFO 設計理由**：
- 對齊 FT-09 §3.4.4「`OnFactionStoryStageUnlocked` 事件逐個發布，依事件抵達順序處理」契約，保證玩家看到的 stage 順序為 1 → 2 → 3
- 指示器點擊與自動觸發採同一 FIFO 邏輯，避免「最近未確認」（LIFO）與「逐一彈出」（FIFO）的設計矛盾
- queue 只記 stageID，payload 由 FT-09 + DialogueTable 即時查詢，跨版本資料表縮減場景由 FT-09 §3.1.2 Step F 處理（FT-09 LogWarning 並從自身隊列移除；P-02 收到對應 OnFactionStoryStageUnlocked 即可，FT-09 已過濾）

---

### §3.4 面板生命週期、堆疊與輸入路由

#### §3.4.1 面板狀態機

每個 L2 面板有 4 個狀態：

```
Closed → Opening → Open → Closing → Closed
         (動畫)            (動畫)
```

- **Opening / Closing**：`PANEL_TRANSITION_SECONDS`（預設 0.15s，見 §7）淡入/淡出動畫；期間禁止輸入
- **Open**：可接收輸入；面板內子元素正常互動
- **Closed**：UXML 元素 `display: none`；不接收輸入、不被 P-01 Hit-Test 命中

#### §3.4.2 堆疊規則（最多兩層）

| 場景 | 允許 | 處理 |
|---|---|---|
| L2 base panel + 確認彈窗（§3.5.9） | 是 | 確認彈窗疊加於 base panel 上方 |
| L2 base panel + 故事面板（§3.5.8） | 否 | 故事面板開啟前自動關閉所有 L2 base panel |
| L2 base panel + 設定彈窗（§3.5.10） | 否 | 同上 |
| 兩個 base panel 同時開（如委託板 + 名冊） | 否 | 後開的關閉先開的 |
| 確認彈窗疊加於故事面板 | 是 | 故事面板內按下高代價選項時 |

**堆疊維護**：P-02 內部維護 `_panelStack: List<PanelID>`，最多 2 個元素；新面板開啟前依規則決定是否清空 stack。

#### §3.4.3 輸入路由

- **ESC 鍵**：
  - `_panelStack.Top == ConfirmPopup` → 等同點「取消」
  - `_panelStack.Top == StoryPanel` → **忽略**（必須走「確認」按鈕，避免錯過劇情觸發）
  - `_panelStack.Top == SettingsPanel` → 關閉
  - 其餘 base panel → 關閉最頂層
  - `_panelStack` 為空時 → 開啟設定彈窗（§3.5.10）

- **modal 遮罩點擊**：行為同 ESC（故事面板/確認彈窗例外，遮罩點擊不關閉）

- **其他鍵**：Jam 版不實作快捷鍵（如 1-6 數字鍵切面板），Post-Jam 預留

#### §3.4.4 面板開啟 API（P-02 對內）

```csharp
public bool OpenPanel(PanelID id, object args = null)
public bool ClosePanel(PanelID id)
public PanelID GetTopPanel()  // 回傳 _panelStack.Top；empty 時回 PanelID.None
```

`OpenPanel` 在違反 §3.4.2 堆疊規則時自動關閉衝突面板再開新面板，回傳 `true`；若被 `OnUIReady` 之前呼叫則 `LogWarning` 並回 `false`。

---

### §3.5 面板規格（Panel Specifications）

#### §3.5.1 委託板（CommissionBoardPanel）

- **入口**：點擊 `nav_commission_board` 場景物件
- **資料來源**：FT-02 `GetCommissionsBySource(CommissionSource.Regular)` + `GetCommissionsBySource(CommissionSource.Static)`（FT-02 §3.8）；訂閱 `OnCommissionPosted(missionID, source)`、`OnCommissionAccepted(missionID, baseReward, source)` 事件即時刷新
- **版面**：垂直滾動列表，每一行為一張委託卡
- **委託卡元素**：
  - 名稱（`MissionTemplate.missionName`，via D-02 文字 facade）
  - 難度（F~SSS，色塊）
  - 類型（討伐/護送/採集/調查，圖示 + 文字）
  - 報酬（FT-02 / FT-05 計算後的最終值）
  - 時長（小時/分鐘格式化）
  - 介紹短文（20~40 字，via D-02 facade）
  - 狀態標籤（待審核 / 進行中 / 已結算）
  - 推薦按鈕（待審核狀態才顯示，點擊展開推薦冒險者子面板，§3.5.1.1）
  - 成功率預覽（僅當 `FT12.IsSuccessRatePreviewEnabled() == true` 時顯示）

##### §3.5.1.1 推薦冒險者子面板

- 開啟方式：點擊推薦按鈕，inline 展開於委託卡下方（不視為新 L2 面板）
- 列出符合 FT-02 前置條件的冒險者（status=Idle 且通過 `requiredTraitID` 過濾，v3.1）
- 每筆顯示：名字、職業、階級（**Jam 版不顯示 willingness 預估**——FT-02 / FT-03 未對 P-02 提供 willingness 預覽 API；最終接受/拒絕由 NPC 派遣後即時判定，玩家透過 FT-03 `OnAutoPickup` 事件得知結果。Post-Jam 若新增 `FT03.PreviewWillingness` API 可加回此欄位）
- 點擊冒險者：開啟確認彈窗（文字「派遣 {name} 接下 {missionName}？」）
- 確認後呼叫 FT-02 `Dispatch(int instanceID, int missionID, DispatchSource source) : bool`（FT-02 §3.8），`source = DispatchSource.PlayerManual`；回傳 `false` 時 P-02 顯示 toast「派遣失敗」並重整委託板資料；子面板關閉

#### §3.5.2 冒險者名冊（AdventurerRosterPanel）

- **入口**：點擊 `nav_guild_hall` 場景物件
- **資料來源**：C-02 `GetRoster()`（依 v3.1 P3.1-005 §3.1.3 排序：奧菲莉雅 `templateID=901` 永遠第一格，其餘依 `idleSinceTimestamp` 倒序）
- **版面**：垂直滾動列表，每一行為一張冒險者卡
- **冒險者卡元素**：
  - 名字（直列 + Trait 標籤，最多 3 個 Trait 圖示）
  - 階級（F~S，色塊）/ 職業（圖示）/ 種族（圖示）
  - 狀態（Idle / OnMission / Wounded / Dead）+ 倒數（OnMission/Wounded 顯示剩餘秒數，via F-02）
  - 介紹短文（20~40 字，via D-01 facade）
  - 點擊展開詳細資訊（inline，不視為新面板）
- **詳細資訊內容**：完整 Trait 列表 + 描述、能力修正值、最近 5 次任務結果摘要
- **除名/開除按鈕**（v3.1 P3.1-005 §3.1.2）：
  - Dead 狀態：永遠顯示「除名」按鈕
  - Idle 狀態：僅當 FT-07 審查處已解鎖（判定方式：`FT07.GetBuildingLevel(REVIEW_OFFICE_BUILDING_ID) > 0`，buildingID 待 FT-07 P3.1-009 補登；若 FT-07 後續新增 `IsBuildingUnlocked(buildingID)` API，可改用該 API）時顯示「開除」按鈕
  - Wounded / OnMission：不顯示
  - 點擊開啟 Destructive 確認彈窗，確認後呼叫 C-02 `DismissAdventurer(instanceID)`

#### §3.5.3 公會建設（GuildBuildingPanel）

- **入口**：點擊 `nav_construction` 場景物件
- **資料來源**：對 6 棟建築逐棟呼叫 FT-07 `GetBuildingLevel(buildingID) : int`（FT-07 §3.6）；下一級資訊由 P-02 自查 `BuildingTable.csv`（透過 F-01 DataManager）取得 `upgradeData[level+1]` 的 cost / guildLevelReq / effectValue
- **版面**：6 行卡片（每棟一行）
- **建築卡元素**：
  - 建築名稱、圖示、當前等級
  - 當前效果值（依 buildingID 呼叫對應 FT-07 具名 getter：`GetCommissionBoardSlots()` / `GetMaxConcurrentMissions()` / `GetRecruitRefreshInterval()` / `GetBankruptcyWarningSeconds()` 等，FT-07 §3.6；通用 `GetEffectValue` API 不存在）
  - 下一級升級資訊：費用、公會等級門檻、預期效果值（自 `BuildingTable.csv` 直查）
  - 升級按鈕（`FT07.CanUpgrade(buildingID) == true` 可點，否則灰色 + tooltip 顯示阻擋原因）
- **升級流程**：點擊升級按鈕 → 確認彈窗（顯示費用） → 確認後呼叫 FT-07 `TryUpgradeBuilding(buildingID)`
- **事件訂閱**：`OnBuildingUpgraded(buildingID, fromLevel, toLevel)` → 重整面板資料

#### §3.5.4 職員名冊（StaffRosterPanel，FT-12）

- **入口**：點擊 `nav_staff_lounge` 場景物件（前置條件：`FT07.GetBuildingLevel(6) >= 1`）
- **資料來源**：FT-12 `GetActiveRoster()`（§3.6.7）、`GetRosterCap()`（§3.6.7）；個別職員狀態透過 `GetStaffStateView(instanceID)`（§3.6.6）取得
- **版面**：
  - 上方：「前往面試」按鈕（開啟 §3.5.5）
  - 下方：當前職員列表，每行：職員名、稀有度、effect 摘要（透過 `StaffTable[staffID]` 查 effectIDs / effectValues）、薪水（Phase 2 Jam 不發但顯示）、目前指派 slot（建築名稱）、三態狀態（Working / Reallocating / OnLeave）
  - 點擊職員：展開細節，含「指派至 slot」下拉選單（呼叫 FT-12 `TryAssignStaff` 等 API，依 FT-12 §3.6.4 切換 cooldown 規則）
- **空名冊**：僅顯示「前往面試」按鈕 + 提示「目前沒有職員，前往面試招募」
- **事件訂閱**：FT-12 `OnStaffHired` / `OnStaffFired` / `OnStaffAssigned` / `OnStaffStateChanged`（FT-12 §6.3）→ 重整面板資料

#### §3.5.5 職員面試（StaffGachaPanel，FT-08）

- **入口**：職員名冊面板「前往面試」按鈕（**不**直接從場景物件開啟，避免玩家略過名冊上下文）
- **資料來源**：FT-08 `GetCurrentCandidates()` polling（FT-08 §3.3.4）、`GetCurrentPoolID()`（FT-08 §3.3.4）；刷新費用由 P-02 自查 `StaffRefreshCostTable[refreshCount]`（透過 F-01）；保留區資料透過 `StaffPlayerState.reservedCandidates`（FT-08 SaveData，由 FT-10 載入後 P-02 透過適當 query 取得）
- **版面**：
  - 池選擇 tab（A 池 / B 池，當前 poolID 由 `FT08.GetCurrentPoolID()` 取得；依 FT-08 §3.2.2 解鎖閘決定可見）
  - 當前候選列表（5 格槽位，由 `GetCurrentCandidates()` 取得）
  - 「面試」按鈕（呼叫 `FT08.TryManualRefresh()` 消耗刷新費用）
  - 「切池」按鈕（呼叫 `FT08.TrySwitchPool(poolID)`）
  - 「保留」按鈕（呼叫 `FT08.TryReserveCandidate(slotIndex)`）
  - 「不錄用」按鈕（呼叫 `FT08.TryRejectCandidate(slotIndex)`）
  - 候選結果揭示動畫（Jam 版簡化為直接顯示）
- **錄用流程**：
  - 玩家點擊職員候選 → 「錄用」確認彈窗（顯示薪水、effect 摘要）
  - 確認後 P-02 呼叫 FT-08 `TryRecruit(int slotIndex)`（FT-08 §3.3.4），FT-08 內部呼叫 FT-12 `HireStaff(candidate)`
  - 成功後關閉面試面板，返回職員名冊（堆疊行為：面試面板開啟前已關閉名冊，錄用後 P-02 重新開啟名冊）
- **事件訂閱**：FT-08 不發布 gacha 業務事件（FT-08 §3.7「設計理由」）；P-02 透過 polling `GetCurrentCandidates` 即時刷新；訂閱 FT-12 `OnStaffHired` 確認錄用結果

> 註：§3.5.5 細節以 FT-08 GDD 為主；本節僅定義 P-02 視角的版面與堆疊行為。

#### §3.5.6 公會總覽（GuildOverviewPanel）

- **入口**：點擊 `nav_safe` 場景物件
- **資料來源**：FT-06、F-03、FT-07、FT-09
- **版面**：
  - 公會等級 + 稱號 + 聲望（F-03） + 至下一級進度條（FT-06）
  - 金幣 + 破產倒數（訂閱 F-03 `OnBankruptcyWarningStateChanged` 事件進入/離開警告狀態；倒數值透過 `F03.GetBankruptcyWarningRemainingSeconds()` 取得；倒數總長設定值為 `FT07.GetBankruptcyWarningSeconds()`，由 FT-07 推送至 F-03，P-02 不直接讀）
  - 已解鎖建築摘要
  - 任務統計：總接受/成功/失敗/死亡件數（FT-04 累積值）
  - 陣營分數（`FT09.GetCurrentFactionScore` × 各 factionID）+ styleTag bias（v3.1 `FT09.GetCurrentStyleTagBias()`）

#### §3.5.7 通知 Log（P-03 浮動視窗 host）

- **不為 L2 面板**（不入 `_panelStack`）；為 L1 持久浮動視窗，由 P-03 自行管理拖曳/縮放/最小化（P-03 §3.2）
- **P-02 host 職責**：在 `OnUIReady` 後 instantiate `LogFloatingWindow` UXML 容器，呼叫 P-03 `BindLogWindow(VisualElement container) : BindResult`（P-03 §3.5）完成綁定。回傳碼處理：
  - `Success`：綁定完成，P-03 開始渲染 Log 條目
  - `AlreadyBound`：P-02 LogWarning「Log 視窗已綁定，跳過重複綁定」並 no-op（不視為錯誤）
  - `InvalidContainer`：P-02 LogError 並進入 §EC-25 降級（P-02 OnUIReady 仍照常完成）
- **位置與狀態**：由 FT-10 持久化（依 P-03 §6 依賴）
- **與 L2 面板互動**：L2 面板開啟時，Log 視窗保持可見可拖曳（不被 modal 遮罩遮蔽，`sortingOrder` 高於 modal）

#### §3.5.8 故事面板（StoryDialoguePanel，v3.1 整合點）

- **入口**：FT-09 事件自動觸發 + 持久指示器手動點擊（§3.3）
- **觸發事件**：

| 事件 | payload | 對話呈現 |
|---|---|---|
| `OnFactionStoryStageUnlocked` | `stageID, resolvedDialogueKey, specialEventKey` | 解鎖對話；確認後呼叫 FT-09 `ConfirmDialogue(stageID)` |
| `OnFactionStoryStageEpilogue` | `stageID, resolvedEpilogueKey, isOpheliaEpilogue, subjectAlive` | epilogue 對話；確認後關閉（不需 ConfirmDialogue） |
| `OnOpheliaMissingNight` | — | Stage 4「她沒回來」場景說明層（§3.7） |
| `OnOpheliaReturned` | — | 「她回來了」對話 |

- **堆疊行為**：
  - 開啟前自動關閉所有 L2 base panel
  - 期間 ESC 無效，必須點「確認」按鈕
  - 多 stage unlock 同時觸發：依 §3.3 `_pendingStageDialogueQueue` FIFO 處理；ConfirmDialogue 後自動 chain continue（dequeue 下一個 head）
  - 死亡通知 + epilogue 同 frame 觸發（如 Stage 5 奧菲莉雅死亡）：先呈現 P-03 Critical 死亡通知，玩家確認後再彈出 `OnFactionStoryStageEpilogue` 對話（依 v3.1 §4.2.3）；epilogue 不入 stage queue（單次播放）
- **對話視窗呈現**：見 §3.7
- **`specialEventKey == "ophelia_missing"`**：玩家點「確認」呼叫 `FT09.ConfirmDialogue(stageID)` 回 `SUCCESS` 後，P-02 publish `OnOpheliaMissingNight` 至 EventBus（FT-09 §3.6.9 與 P-02 自身 §3.6.3 SceneObjectController 同為 subscriber）

#### §3.5.9 確認彈窗（ConfirmPopup）

- **入口**：任何需要二次確認的動作呼叫 P-02 `ShowConfirm(args)`
- **API**：
  ```csharp
  public void ShowConfirm(string titleKey, string bodyKey,
                          string confirmKey, string cancelKey,
                          Action onConfirm, Action onCancel = null,
                          ConfirmStyle style = ConfirmStyle.Normal)
  ```
- **版面**：標題 + 內文 + 「確認」/「取消」雙按鈕；半透明遮罩
- **樣式**（`ConfirmStyle`）：
  - `Normal`：藍底確認鈕
  - `Destructive`：紅底確認鈕（用於除名、開除、解雇、賣建築等）
- **ESC**：等同「取消」

#### §3.5.10 設定彈窗（SettingsPanel）

- **入口**：點擊 `nav_settings_desk` 或 ESC 在 `_panelStack` 為空時觸發
- **內容**：
  - UI 縮放（`P01.SetUserScale(value)`，slider [USER_SCALE_MIN, USER_SCALE_MAX] step `USER_SCALE_STEP`；按鈕「還原預設」呼叫 P-01 還原）
  - 目標螢幕（dropdown，列出可用螢幕；選擇後呼叫 P-01 切換螢幕 API）
  - 音量（Post-Jam 預留 slider）
  - 語言（Jam 版只 zhTW，預留 en）
  - 「最小化」按鈕（呼叫 P-01 §3.8 對外 API surface 中的 `Minimize()`）
  - 「離開遊戲」按鈕 → Destructive 確認彈窗
- **持久化**：所有設定變更立即寫入 FT-10（透過 P-01 `SetUserScale` / 螢幕切換 API 內建持久化）

---

### §3.6 SceneObjectController 子模組（v3.1 Patch P3.1-010）

#### §3.6.1 子模組職責

`SceneObjectController` 為 P-02 的場景物件渲染管線：依 FT-09 階段狀態與事件，切換場景物件 sprite / 對話鍵 / 音效。**只處理 v3.1 列出的奧菲莉雅敘事物件**（5 個 objectID，10 個 stageCondition row）；§3.2 的 6 個導覽用建物**不**走此模組。

#### §3.6.2 SceneObjectStateTable.csv schema

| 欄位 | 型別 | 必要 | 說明 |
|---|---|---|---|
| `objectID` | string | 是 | 場景物件唯一識別碼 |
| `stageCondition` | string | 是 | 觸發條件語法：`stageID >= N` / `stageID == N` / `stageID < N` / `event:KEY` |
| `spriteVariant` | string | 是 | sprite 變體名稱（命名規則：`Assets/Art/Scene/Ophelia/{objectID}_{spriteVariant}.png`） |
| `dialogueKey` | string | 否 | 玩家點擊互動時顯示的對話 key（透過 DialogueTable 查詢；可為空字串表不可互動） |
| `priority` | int | 是 | 同 objectID 多 row 時優先順序（高值優先） |
| `audioCue` | string | 否 | 觸發時音效（Post-Jam 預留，Jam 版欄位填空） |

> 多 row 共用 objectID：依 priority 由高至低評估 stageCondition，第一個成立的 row 生效。

#### §3.6.3 stageCondition 解析

```
EvaluateStageCondition(condition, currentStage, activeEvents):
    IF condition starts with "stageID >=":  return currentStage >= ParseInt(after ">=")
    IF condition starts with "stageID ==":  return currentStage == ParseInt(after "==")
    IF condition starts with "stageID <":   return currentStage <  ParseInt(after "<")
    IF condition starts with "event:":      return activeEvents.Contains(after ":")
    LogError("Unknown condition syntax"); return false
```

`activeEvents` 由 P-02 維護：訂閱 EventBus 事件 `OnOpheliaMissingNight`（publisher 為 P-02 自身於 §3.5.8 ConfirmDialogue 後發布；FT-09 §3.6.9 與 P-02 SceneObjectController 同為 subscriber）→ 加入 `"ophelia_missing"`；訂閱 `OnOpheliaReturned`（publisher 為 FT-09 §3.6.9，於奧菲莉雅 Wounded 復原時觸發）→ 移除 `"ophelia_missing"`。`GetUnlockedStageIndex(int factionID) : int` 為 FT-09 §3.7.1 公開 API，無 unlocked stage 時回 `-1`。

#### §3.6.4 ResolveSceneObjectState API

```csharp
public SceneObjectState ResolveSceneObjectState(string objectID)
{
    var rows = DataManager.Get<SceneObjectStateTable>()
                          .WhereObjectID(objectID)
                          .OrderByDescending(r => r.priority);
    foreach (var row in rows)
        if (EvaluateStageCondition(row.stageCondition, FT09.GetUnlockedStageIndex(1), _activeEvents))
            return new SceneObjectState(row.spriteVariant, row.dialogueKey, row.audioCue);
    return SceneObjectState.Default;
}
```

#### §3.6.5 場景物件點擊互動

- 玩家點擊 v3.1 場景物件（如 `ophelia_chair`、`ophelia_teacup`）→ 依 `ResolveSceneObjectState().dialogueKey` 開啟故事面板，呈現該對話
- `dialogueKey` 為空字串時不可點擊（hover outline 不顯示）
- 互動視覺回饋同 §3.2 hover outline 規則

#### §3.6.6 SceneObjectStateTable 初始資料

依 v3.1 patch P3.1-010 §4.2.1 列出的 10 行（涵蓋 5 個 objectID：`ophelia_chair`、`ophelia_teacup`、`ophelia_guildbook`、`ophelia_crest`、`ophelia_door_note`）；DS 與 CSV 由後續資料表設計階段填入。

---

### §3.7 對話視窗呈現規範（v3.1 Patch P3.1-010 §4.2.3）

- **主對話視窗**：≤3 行，每行 ≤24 字（中文計算）；超出時自動換頁，「下一頁」按鈕
- **Stage 4「她沒回來」**：場景說明層（疊加在公會場景內，半透明文字框），字色偏灰（`#888888`）；不走標準 modal，不阻擋輸入
- **Stage 5 奧菲莉雅台詞**：黑底白字最簡形式（背景色 `#000001`，避開 P-01 透明色鍵 `#000000`）；3 行限制仍適用
- **死亡通知 → epilogue 視窗順序**：佇列化處理；FT-04 `OnAdventurerDied` 觸發死亡通知（走 P-03 Critical），確認後再呈現 FT-09 `OnFactionStoryStageEpilogue` 對話
- **紙條三版視覺規格**：light / dark / neutral 三版（v3.1 §5），由 art-director 提供素材；P-02 依 `FT09.GetCurrentStyleTagBias()` 選擇紙條樣式 sprite

---

### §3.8 啟動握手 OnUIReady（對接 P-01）

P-02 啟動序列（觸發點：訂閱 FT-10 `OnLoadCompleted` 事件後執行）：

1. `MainSceneDocument` 與 `OverlayPanelDocument` UXML 載入完成
2. 訂閱所有事件（F-03、FT-04、FT-06、FT-07、FT-09、FT-12 等）
3. **Bootstrap 復原**（補捉 FT-10 Phase C 期間發布的事件）：
   - 呼叫 `FT09.GetPendingDialogueStages()`（FT-09 §3.7.1）取得 read-only snapshot；對清單中每個 stageID 依 head→tail 順序 enqueue 至 `_pendingStageDialogueQueue`
   - **目的**：FT-09 §3.1.2 Step F 在 FT-10 Phase C 期間重發 `OnFactionStoryStageUnlocked` 事件，但 P-02 此時尚未訂閱（P-02 啟動序列在 `OnLoadCompleted` 之後才開始，FT-10 §3.3.2 line 349），因此這些事件在事件流上會丟失。透過主動查詢 + Step F 雙重保險，無論 P-02 訂閱時序如何，pending stages 都不會遺漏
4. 載入 `UIText.csv`、`SceneObjectStateTable.csv`（透過 F-01）
5. 註冊 P-01 effectiveScale callback：呼叫 `P01.RegisterEffectiveScaleListener(OnEffectiveScaleChanged)`（P-01 §3.8.1）；P-01 立即推送一次當前 effectiveScale → P-02 套用至 PanelSettings
6. 初始化所有 L1 持久 UI（金幣、劇情指示器、設定按鈕）；使用 §4.3 World-to-Screen 公式套用錨點
7. 場景物件 sprite 套用初始 `ResolveSceneObjectState`（依當前 stage 與 activeEvents）
8. P-02 instantiate `LogFloatingWindow` UXML 容器並呼叫 `P03.BindLogWindow(container)` 完成 Log 視窗綁定（P-02 為 caller，P-03 為 callee；§3.5.7 + P-03 §3.5）；依 BindResult 處理三種回傳碼（§3.5.7）
9. P-02 發布 `OnUIReady` 事件（P-01 訂閱後啟用 Hit-Test）；若 step 3 復原的 queue 非空 → 立即依 §3.3 chain start 規則 dequeue head 並開啟故事面板

`OnUIReady` 發布前所有 `OpenPanel` 呼叫被忽略（LogWarning + 回 false）；發布後 P-02 進入正常運作狀態。

---

### §3.9 文字資料驅動（UIText.csv 路由）

- 所有玩家可見字串（按鈕文字、tooltip、面板標題、確認彈窗文字、錯誤提示）皆透過 `UIText.csv` 提供 `key → zhTW / en` 映射
- P-02 內提供 `UITextLookup(key, fallback)` helper：
  - key 命中 → 回傳當前語言對應字串
  - key 不命中 → `LogError`，回傳 fallback；fallback 為空時回傳 key 本身以利除錯
- 動態字串（如「派遣 {name} 接下 {missionName}？」）以 `String.Format` 套入 placeholder
- 對話文字（DialogueTable）**不**走此路由——FT-09 / SceneObjectController 直接查 DialogueTable

---

## §4 公式（Formulas）

P-02 不含 gameplay 公式；以下定義 UI 渲染所需的座標、縮放、z-order、文字處理與動畫公式。

### §4.1 變數定義

| 變數 | 說明 | 預設值 / 來源 |
|---|---|---|
| `effectiveScale` | UI Toolkit `PanelSettings.scale` 套用值 | 由 P-01 §3.5 計算後推送 |
| `windowClient` | Unity 視窗 client 區域矩形（pixel） | P-01 取得 |
| `INTRO_TEXT_MIN_CHARS` | 介紹短文最小字元數（中文計）| 20 |
| `INTRO_TEXT_MAX_CHARS` | 介紹短文最大字元數 | 40 |
| `DIALOGUE_LINE_MAX_CHARS` | 對話視窗每行最大字元數 | 24 |
| `DIALOGUE_LINES_PER_PAGE` | 對話視窗每頁最多行數 | 3 |
| `PANEL_TRANSITION_SECONDS` | 面板淡入淡出動畫時長 | 0.15 |
| `MODAL_OVERLAY_ALPHA` | L2 modal 半透明遮罩 alpha | 0.40 |
| `HOVER_OUTLINE_THICKNESS` | hover outline 描邊像素 | 2 |
| `HOVER_OUTLINE_ALPHA` | hover outline alpha | 0.60 |
| `SortingOrder.L0Scene` | 場景層 sortingOrder | 0 |
| `SortingOrder.L1HUD` | L1 持久 UI sortingOrder | 100 |
| `SortingOrder.L2Modal` | L2 modal 遮罩 sortingOrder | 200 |
| `SortingOrder.L2Panel` | L2 base panel sortingOrder | 210 |
| `SortingOrder.ConfirmPopup` | 確認彈窗 sortingOrder | 220 |
| `SortingOrder.StoryPanel` | 故事面板 sortingOrder | 230 |
| `SortingOrder.LogFloatingWindow` | Log 浮動視窗 sortingOrder | 250（高於所有 L2，永遠可拖曳） |

> 所有 `SortingOrder.*` 為 P-02 內部常量；UI Toolkit 透過獨立 UIDocument + 不同 sortingOrder 實現；同一 UIDocument 內透過 USS `z-index` 處理。

### §4.2 PanelSettings 縮放

P-02 不自行計算縮放，沿用 P-01 推送：

```
PanelSettings.scale = P01.GetEffectiveScale()
```

P-01 在以下時機推送 effectiveScale 至 P-02（推送機制：P-01 §3.8.1 `RegisterEffectiveScaleListener(callback)`）：
- 註冊當下立即推送一次（保證初值就緒）
- 解析度變更（`WM_DISPLAYCHANGE`）
- `SetUserScale` / `ResetUserScaleToDefault` 呼叫後
- `SwitchTargetScreen` 呼叫後

P-02 實作 `OnEffectiveScaleChanged(float newScale)` callback（P-01 §3.8.1 規範）；callback 內套用至所有 PanelSettings 並 LogInfo 新值。註冊由 §3.8 step 4 完成。

### §4.3 場景內嵌 UI 螢幕錨點（World-to-Screen）

L1 場景內嵌 UI（金幣、劇情指示器、設定按鈕、hover outline）以場景物件世界座標反推 UXML 元素位置：

```
worldPos    = sceneObject.transform.position             // 場景物件世界座標（Unity）
screenPos   = Camera.main.WorldToScreenPoint(worldPos)   // 螢幕像素座標（左下原點）
panelLocalX = screenPos.x / effectiveScale
panelLocalY = (windowClient.height - screenPos.y) / effectiveScale  // UI Toolkit 左上原點翻轉

uiElement.style.left = panelLocalX + offsetX
uiElement.style.top  = panelLocalY + offsetY
```

`offsetX / offsetY` 為各元素相對於物件中心的偏移（例：金幣顯示 `offsetY = -16`，貼於保險箱上方）。

更新時機：每次場景物件位置變更或 effectiveScale 變更時重算；不做 per-frame polling。

### §4.4 文字截斷規則

#### §4.4.1 介紹短文（委託 / 冒險者卡）

由 D-01 / D-02 文字 facade 已保證輸出字串長度落在 `[INTRO_TEXT_MIN_CHARS, INTRO_TEXT_MAX_CHARS]`；P-02 不再截斷，僅以 USS `text-overflow: ellipsis` 處理 UI 元素寬度不足的視覺溢出。

#### §4.4.2 對話視窗（故事面板 / SceneObjectController）

```
SplitDialogue(rawText) → List<string> pages:
    pages = []
    currentPage = []
    foreach segment in rawText.Split('\n'):
        wrapped = WrapByLineLength(segment, DIALOGUE_LINE_MAX_CHARS)
        foreach line in wrapped:
            currentPage.Add(line)
            if currentPage.Count == DIALOGUE_LINES_PER_PAGE:
                pages.Add(currentPage); currentPage = []
    if currentPage.NotEmpty: pages.Add(currentPage)
    return pages
```

`WrapByLineLength` 中文逐字計（每字寬度 1）；英文以單字邊界（空白/標點）斷行；混排時兩規則並用，斷行優先點：標點 > 空白 > 強制中字斷行。

「下一頁」按鈕在 `pages.Count > 1` 時顯示；最後一頁按鈕文字切為「確認」。

### §4.5 面板淡入淡出動畫

```
opacity(t) = Lerp(fromAlpha, toAlpha, t / PANEL_TRANSITION_SECONDS)   // t in [0, PANEL_TRANSITION_SECONDS]
```

- Opening：fromAlpha=0, toAlpha=1
- Closing：fromAlpha=1, toAlpha=0
- 期間 `pickingMode = Ignore`，元素不接收輸入
- 動畫結束後，Opening → Open（pickingMode=Position）；Closing → Closed（display=none）

### §4.6 Log 視窗位置 clamp

依 P-03 §5「Log 視窗拖曳至螢幕外（部分超出邊界）」規則，P-02 host 不強制 clamp。但提供「最小可見錨點」保證：

```
visibleAnchor = (logRect.x + LOG_VISIBLE_MARGIN, logRect.y + LOG_VISIBLE_MARGIN)
if visibleAnchor outside windowClient:
    logRect = ClampToWindow(logRect, windowClient, LOG_VISIBLE_MARGIN)
```

`LOG_VISIBLE_MARGIN = 32px`（見 §7）；只在啟動時與 effectiveScale 變更時觸發 clamp，不在每次拖曳時觸發。

### §4.7 hover outline 矩形計算

針對 §3.2 場景物件互動點，hover outline 為依 sprite bounds 反推的 UXML 矩形：

```
spriteBounds = sceneObject.spriteRenderer.bounds   // world AABB
worldCorners = [bounds.min, bounds.max]
screenCorners = worldCorners.Select(WorldToScreenPoint)
uiRect = {
    left   : min(screenCorners.x) / effectiveScale,
    top    : (windowClient.height - max(screenCorners.y)) / effectiveScale,
    width  : (max(screenCorners.x) - min(screenCorners.x)) / effectiveScale,
    height : (max(screenCorners.y) - min(screenCorners.y)) / effectiveScale
}
outline.style.borderWidth = HOVER_OUTLINE_THICKNESS
outline.style.borderColor = (1, 1, 1, HOVER_OUTLINE_ALPHA)
```

未解鎖物件改用 `borderColor = (0.5, 0.5, 0.5, HOVER_OUTLINE_ALPHA)` 與 tooltip 提示。

### §4.8 範例計算

**1920×1080，effectiveScale ≈ 0.963（P-01 §4 範例）**

範例 A — 金幣顯示錨點：
- 保險箱物件世界座標 `(8.5, 1.2, 0)`
- `WorldToScreenPoint` → `(1670, 280)`（pixel）
- `panelLocalX = 1670 / 0.963 ≈ 1734`
- `panelLocalY = (312 − 280) / 0.963 ≈ 33`（windowClient.height = 312）
- 加 `offsetY = -16` → `top = 33 − 16 = 17`

範例 B — 對話分頁：
- 原文「她沒有回來。我們等了一整夜，桌子前的位子始終是空的，茶冷了，連晨星也黯了。」（35 字含標點）
- 每行 24 字 → 第 1 行「她沒有回來。我們等了一整夜，桌子前的位子始終是空的，」（剛好 24 字）
- 第 2 行「茶冷了，連晨星也黯了。」（11 字）
- 合計 2 行，1 頁完成；按鈕顯示「確認」

範例 C — 面板淡入：
- t=0  → opacity=0
- t=0.075 → opacity=0.5
- t=0.15 → opacity=1（進入 Open）

---

## §5 邊界情況（Edge Cases）

### §5.1 啟動與初始化

| # | 情況 | 行為 |
|---|---|---|
| EC-01 | `OpenPanel` 在 `OnUIReady` 發布前被呼叫 | `LogWarning("OpenPanel called before OnUIReady: {id}")`，回傳 false，不開面板 |
| EC-02 | `UIText.csv` 載入失敗（F-01 回傳 null） | P-02 進入 fallback 模式：所有 `UITextLookup` 回傳 key 本身；`OnUIReady` 仍照常發布；UI 可運作但顯示為 raw key 字串（利於除錯定位） |
| EC-03 | `SceneObjectStateTable.csv` 載入失敗 | P-02 進入降級：奧菲莉雅敘事物件全部使用 `SceneObjectState.Default`（隱藏 spriteVariant，dialogueKey 為空）；`OnUIReady` 仍發布；場景中 5 個物件不可互動，但其他 6 個導覽點不受影響 |
| EC-04 | P-02 的 OnUIReady 發布前，FT-09 已發布 `OnFactionStoryStageUnlocked` 事件（載入存檔含 pending stages） | **雙重保險機制**：(1) **被動接收**：FT-09 §3.1.2 Step F 在 RestoreFromSave 後重發事件，若 P-02 已訂閱即接收；(2) **主動查詢**：P-02 §3.8 step 3 在訂閱完成後立即呼叫 `FT09.GetPendingDialogueStages()` 補捉 Phase C 期間發布但 P-02 未及訂閱的事件。兩機制重疊不重複（P-02 enqueue 前 dedupe `_pendingStageDialogueQueue` 既有 stageID）；任一機制單獨失效仍有另一機制兜底 |

### §5.2 面板生命週期與堆疊

| # | 情況 | 行為 |
|---|---|---|
| EC-05 | 同 frame 兩個面板開啟請求（如同時點擊兩個場景物件） | `OpenPanel` 序列化處理：第一個請求進入 Opening 狀態，第二個請求依 §3.4.2 規則關閉第一個再開；中間 0.15s 內完成（淡入動畫疊加） |
| EC-06 | 面板處於 Opening / Closing 動畫期間，玩家點擊互動 | `pickingMode = Ignore` 期間元素不接收輸入；玩家點擊穿透至 P-01 Hit-Test，可能命中底層場景物件——此時不視為 panel 內互動，不阻擋場景物件點擊 |
| EC-07 | 確認彈窗 Opening 中 ESC 鍵被按下 | 等待 Opening 結束（最多 0.15s）後立即套用「取消」邏輯；不打斷動畫 |
| EC-08 | 故事面板開啟期間 FT-09 再次發布 `OnFactionStoryStageUnlocked` | 第二事件 enqueue 至 P-02 `_pendingStageDialogueQueue` tail（同時 FT-09 自身 `_pendingDialogueStages` 維護自己的隊列，雙端同步無 desync 風險，因 P-02 enqueue 來自事件接收）；不插入當前對話；當前對話「確認」（`ConfirmDialogue` 回 `SUCCESS`）後，§3.5.8 chain continue 規則自動 dequeue P-02 queue head 並開啟下一個故事面板 |
| EC-09 | 確認彈窗疊加於故事面板上方時 ESC 被按下 | 等同點「取消」（關閉確認彈窗）；故事面板維持開啟（仍須走「確認」按鈕） |

### §5.3 資料同步與事件競態

| # | 情況 | 行為 |
|---|---|---|
| EC-10 | 委託板開啟期間，玩家推薦中的委託被 NPC 自主接走（FT-03 `OnAutoPickup`） | P-02 訂閱 `OnAutoPickup` → 立即重整委託板；若推薦冒險者子面板正開於該委託，子面板自動 collapse + 顯示 toast「{name} 已自主接下此委託」（P-03 Optional 通知） |
| EC-11 | 冒險者名冊開啟期間，當前展開卡片的冒險者死亡（FT-04 `OnAdventurerDied`） | 訂閱 `OnAdventurerDied` → 重整名冊；若該冒險者卡片展開中，自動 collapse 並顯示淡灰底「已死亡」標記，不主動關閉名冊面板 |
| EC-12 | 公會建設面板開啟期間，玩家觸發升級（呼叫 `TryUpgradeBuilding`），但金幣同時被 FT-05 維護費扣款導致升級失敗 | FT-07 回傳 `GOLD_INSUFFICIENT`；P-02 顯示確認彈窗 toast「金幣不足」並重整面板資料；不重複扣款 |
| EC-13 | 職員名冊開啟期間，FT-12 觸發 `OnStaffSalaryDue` 但 FT-05 因破產拒付薪水（Phase 2 Jam 不發但持久顯示） | Jam 版不發薪水（FT-12 §3.9 標 Phase 2）→ 此情況不會發生於 Jam；UI 行為以 Post-Jam 處理為準（暫不實作） |
| EC-14 | 故事面板呈現 Stage 4「她沒回來」期間，玩家對奧菲莉雅發出推薦（理論上不可能因她已 Wounded） | C-02 與 FT-02 前置檢查阻擋（status != Idle）；若仍走到 P-02 推薦子面板，UI 已過濾掉 Wounded 冒險者，玩家不會看到她 |

### §5.4 螢幕與縮放

| # | 情況 | 行為 |
|---|---|---|
| EC-15 | 面板 Open 狀態時解析度變更（`WM_DISPLAYCHANGE`） | P-01 推送新 effectiveScale → P-02 套用新 `PanelSettings.scale`；面板尺寸自動跟隨；場景內嵌 UI 重新呼叫 §4.3 World-to-Screen 重算錨點；不關閉面板 |
| EC-16 | 面板 Opening 動畫期間 effectiveScale 變更 | 動畫繼續進行（opacity lerp 不受縮放影響）；新 scale 在動畫結束前已套用，不額外打斷 |
| EC-17 | 設定彈窗螢幕 dropdown 列出的螢幕已斷開（重啟前選擇後拔掉螢幕） | dropdown 顯示「(已斷開) {螢幕名}」項目並標灰；玩家選擇時 P-01 fallback 至主螢幕（依 P-01 §3.1）；P-02 顯示 toast「目標螢幕已斷開，已切回主螢幕」 |
| EC-18 | userScale 滑桿被拖至範圍外 | P-01 內部 clamp 至 `[USER_SCALE_MIN, USER_SCALE_MAX]`；UI 滑桿 visual 跟隨 clamp 後值（不顯示超出值） |

### §5.5 資料缺失與 fallback

| # | 情況 | 行為 |
|---|---|---|
| EC-19 | `UITextLookup(key)` 命中失敗 | `LogError("UIText key missing: {key}")`，回傳 fallback 字串（若呼叫端未提供 fallback，回傳 key 本身） |
| EC-20 | DialogueTable 找不到 `dialogueKey`（故事面板觸發） | 顯示「[對話缺失：{dialogueKey}]」於對話視窗，「確認」按鈕仍可點；不阻擋 stage 流程（仍呼叫 FT-09 `ConfirmDialogue`） |
| EC-21 | `SceneObjectStateTable` 中 `objectID` 對應 row 全部 stageCondition 不成立 | `ResolveSceneObjectState` 回傳 `SceneObjectState.Default`；場景物件以預設 sprite 呈現、不可互動 |
| EC-22 | `SceneObjectStateTable` row 的 `stageCondition` 語法錯誤 | `EvaluateStageCondition` `LogError`，該 row 視為不成立；繼續評估下一 row（priority 排序） |
| EC-23 | sprite 資產 `{objectID}_{spriteVariant}.png` 載入失敗 | `LogError`，顯示空白佔位 sprite（`Sprite.Empty` 或 1×1 透明）；不影響 dialogueKey 互動 |

### §5.6 P-01 / P-03 整合

| # | 情況 | 行為 |
|---|---|---|
| EC-24 | P-01 在 P-02 OnUIReady 之前已嘗試 Hit-Test（早期玩家輸入） | P-01 §3.2 規定「等待 OnUIReady 後才開放輸入」；若仍發生，P-01 自身的 panel.Pick 會回 null（P-02 panel 尚未準備）→ 全視窗穿透；不影響 P-02 |
| EC-25 | P-02 host instantiate `LogFloatingWindow` 失敗（P-03 binding 異常） | P-02 `LogError`，繼續完成 OnUIReady；遊戲可玩但 Log 視窗不顯示；P-03 自身 fallback 邏輯處理（P-03 §5） |
| EC-26 | P-03 Critical 通知（如破產警告）與故事面板同 frame 觸發 | 依 §3.7「死亡通知 → epilogue 視窗順序」延伸規則：Critical 通知優先呈現（P-03 自身 Toast），故事面板等待 Critical 確認後再開啟 |

---

## §6 依賴關係（Dependencies）

### §6.1 P-02 依賴的系統（Upstream）

#### Foundation 層

| 系統 | 依賴介面 | 用途 |
|---|---|---|
| F-01 DataManager | `Get<UIText>()`、`Get<SceneObjectStateTable>()`、`Get<DialogueTable>()` | UI 文字、場景物件狀態、對話內容查詢；F-01 必須在 P-02 OnUIReady 之前完成 CSV 載入 |
| F-02 Time System | `NowUTC` property（F-02 §3）；倒數秒數由 P-02 自行計算 `targetTimestamp - NowUTC` | 冒險者倒數計時（OnMission/Wounded 剩餘秒數）、Log 條目時間戳記輔助 |
| F-03 Resource Management | `GetGold()`、`GetCurrentReputation()`、`GetBankruptcyWarningRemainingSeconds()`（F-03 §6）；訂閱 `OnGoldChanged(int newValue, int delta)`、`OnReputationChanged(int newValue, int delta)`、`OnBankruptcyWarningStateChanged` | 金幣即時顯示、聲望顯示、破產倒數進入/離開警告狀態 |

#### Core 層

| 系統 | 依賴介面 | 用途 |
|---|---|---|
| C-01 Mission Database | `Get<MissionTemplate>(missionID)`、`Get<MissionDifficultyTable>(difficulty)` | 委託卡靜態資料（難度色塊、報酬基準值） |
| C-02 Adventurer Management | `GetRoster()`、`GetAdventurer(instanceID)`；訂閱 `OnAdventurerDied`、`OnAdventurerRecovered`、`OnAdventurerDismissed`；呼叫 `DismissAdventurer(instanceID)` | 名冊面板資料源；除名/開除流程 |
| C-03 Profession System | 透過 C-02 取得 professionID → `Get<ProfessionTable>(id)` 顯示職業圖示與名稱 | 名冊卡職業欄位 |
| C-04 Race System | 同 C-03 模式（透過 C-02 取得 raceID） | 名冊卡種族欄位 |
| C-05 Trait System | 透過 C-02 取得 traitIDs → `Get<TraitTable>(id)` 顯示 trait 名稱與描述 | 名冊卡 Trait 標籤、詳細展開的 trait 描述 |
| C-06 World Danger System | `GetCurrentLevel() : string`（dangerLevel 字元，C-06 §6）、`GetDangerData()`（取 name 與其他 metadata）；訂閱 `OnDangerLevelChanged` | 公會總覽顯示當前世界危險度名稱與描述 |

#### Feature 層

| 系統 | 依賴介面 | 用途 |
|---|---|---|
| FT-02 Mission Dispatch | `GetCommissionsBySource(CommissionSource source) : IReadOnlyList<int>`（FT-02 §3.8）、`Dispatch(int instanceID, int missionID, DispatchSource source) : bool`（FT-02 §3.8）；訂閱 `OnCommissionPosted(missionID, source)`、`OnCommissionAccepted(missionID, baseReward, source)` | 委託板資料源、推薦子面板派遣動作（`source = DispatchSource.PlayerManual`） |
| FT-03 NPC Decision | 訂閱 `OnAutoPickup` | 推薦子面板被搶單時自動 collapse（EC-10） |
| FT-04 Outcome Resolution | 訂閱 `OnMissionResolved`、`OnAdventurerDied` | 任務統計累積、名冊死亡標記 |
| FT-05 Guild Gold Flow | 訂閱 `OnCommissionSettled` | 委託卡狀態切為「已結算」 |
| FT-06 Guild Core | `GetCurrentLevel() : int`、`GetCurrentTitle() : string`（FT-06 §3.6）、`IsGameOver()`、`IsGameOverPending()`；訂閱 `OnGuildLevelChanged(payload)`、`OnGameOverPending` | 公會總覽顯示等級/稱號；Game Over 流程（FT-06 §3 注：聲望進度條由 P-02 自查 `GuildLevelTable` + F-03 `GetCurrentReputation` 計算） |
| FT-07 Guild Building System | `GetBuildingLevel(buildingID) : int`、具名 effect getter（`GetCommissionBoardSlots` / `GetMaxConcurrentMissions` / `GetRecruitRefreshInterval` / `GetBankruptcyWarningSeconds` 等，FT-07 §3.6）、`CanUpgrade(buildingID)`、`TryUpgradeBuilding(buildingID)`、`IsStaffSystemUnlocked()`；訂閱 `OnBuildingUpgraded(buildingID, fromLevel, toLevel)` | 建設面板資料源、升級流程；判定 `nav_staff_lounge` 可點性與開除按鈕顯示（後者以 `GetBuildingLevel(審查處 buildingID) > 0` 判定，待 FT-07 P3.1-009 補登審查處 buildingID） |
| FT-08 Gacha System | `GetCurrentCandidates() : IReadOnlyList<CandidateCard>`、`GetCurrentPoolID() : int`（兩者於 FT-08 §3.3.4）、6 個 `Try*` 動作 API（`TryManualRefresh` / `TrySwitchPool` / `TryRecruit` / `TryRejectCandidate` / `TryReserveCandidate` / `TryReleaseReserve`）；訂閱 `OnStaffSystemBoot`（FT-08 §3.7）；其餘狀態變動透過 polling `GetCurrentCandidates` 自行刷新（FT-08 不發布 gacha 業務事件） | 職員面試面板資料源與互動；錄用透過 `TryRecruit(slotIndex)` |
| FT-09 Faction Story System | `GetCurrentFactionScore(int factionID) : int`、`GetCurrentStyleTagBias() : StyleTag`、`GetUnlockedStageIndex(int factionID) : int`、`ConfirmDialogue(int stageID) : ConfirmDialogueResult`、`GetPendingDialogueStages() : IReadOnlyList<int>`（皆 FT-09 §3.7.1，最後者為 P-02 §3.8 step 3 bootstrap 復原專用）；訂閱 `OnFactionStoryStageUnlocked`、`OnFactionStoryStageResolved`、`OnFactionStoryStageEpilogue`、`OnOpheliaMissingNight`、`OnOpheliaReturned`、`OnFactionRouteCompleted`（可選，路線完結 UI）（FT-09 §3.6 / §3.7.2） | 故事面板觸發、紙條樣式選擇、SceneObjectController stageID 來源；P-02 維護自身 `_pendingStageDialogueQueue`（FIFO snapshot from FT-09 + 即時事件追加）|
| FT-10 Save/Load System | `ISaveable` 介面實作（OwnerKey 待定）；個別持久化項由 P-01 / P-03 自行 owner（Log 視窗位置 → P-03；userScale / 目標螢幕 → P-01） | P-02 本身僅持久化「最後關閉的 base panel ID（可選，恢復上次 UI 狀態）」 |
| FT-12 Staff System | `GetActiveRoster() : IReadOnlyList<StaffInstance>`、`GetRosterCap() : int`（兩者於 FT-12 §3.6.7）、`GetStaffStateView(int instanceID)`（FT-12 §3.6.6）、`IsSuccessRatePreviewEnabled()`（FT-12 §3.4）；訂閱 `OnStaffHired`、`OnStaffFired`、`OnStaffAssigned`、`OnStaffStateChanged`（FT-12 §6.3） | 職員名冊資料源、委託卡成功率預覽旗標 |

#### Presentation 層

| 系統 | 依賴介面 | 用途 |
|---|---|---|
| P-01 Desktop Transparent Window | P-01 §3.8 對外 API surface 8 個方法：`GetEffectiveScale()`、`SetUserScale(value)`、`ResetUserScaleToDefault()`、`GetUserScale()`、`EnumerateAvailableMonitors()`、`SwitchTargetScreen(monitorID)`、`GetCurrentTargetMonitorID()`、`Minimize()`；註冊 `RegisterEffectiveScaleListener(callback)` 接收 effectiveScale 推送；P-02 須實作 `OnEffectiveScaleChanged(float newScale)` callback（P-01 §3.8.1） | UI 縮放、設定彈窗螢幕切換、最小化按鈕 |
| P-03 Notification System | `BindLogWindow(VisualElement container) : BindResult`（P-03 §3.5） | Log 浮動視窗 host 綁定 |

#### Data 層

| 系統 | 依賴介面 | 用途 |
|---|---|---|
| D-01 Character Content Database | 透過 facade `GetAdventurerIntroText(instanceID)` 取得 20~40 字介紹短文 | 名冊卡介紹短文 |
| D-02 Mission Content Database | 透過 facade `GetMissionName(missionID)`、`GetMissionIntroText(missionID)` 取得任務名稱與短文 | 委託卡名稱與介紹短文 |

### §6.2 依賴 P-02 的系統（Downstream）

| 系統 | 反向依賴內容 |
|---|---|
| P-01 Desktop Transparent Window | 訂閱 P-02 `OnUIReady` 事件後啟用 Hit-Test 邏輯（P-01 §3.2 step 10）；`panel.Pick(localPos)` 查詢命中需 P-02 panel 實例存在；P-01 §3.6 USS 錨點規則（左/右）由 P-02 實作；P-01 推送 effectiveScale 變更時呼叫 P-02 `OnEffectiveScaleChanged` callback |
| P-03 Notification System | Log 浮動視窗為 P-02 場景下的子面板（P-03 §6 列）；P-03 訂閱 P-02 `OnUIReady` 後初始化 Log 視窗；通知 UI 元素須在 P-02 提供的 Hit-Test 可命中區域內 |
| FT-09 Faction Story System | 期待 P-02 訂閱 `OnFactionStoryStageUnlocked` 並對 `specialEventKey == "ophelia_missing"` 在玩家確認對話後發布 `OnOpheliaMissingNight`（FT-09 §2.4.7 / v3.1 P3.1-004）；ConfirmDialogue 必須在玩家點「確認」後呼叫，FT-09 才能維護 `_pendingDialogueStages` 隊列 |
| FT-08 Gacha System | 錄用流程依賴 P-02 呼叫 `TryRecruit(slotIndex)`（FT-08 §3.3.4）；其餘 5 個 `Try*` 動作 API（refresh / switchPool / reject / reserve / release）皆由 P-02 觸發；FT-08 依賴 P-02 提供面試 UI 才能讓玩家面試/錄用；無 P-02 時 FT-08 為純後端，無對外觸發點 |
| FT-12 Staff System | slot 指派、開除流程依賴 P-02 職員名冊面板呼叫對應 API；無 UI 時 FT-12 名冊管理為純後端 |
| FT-07 Guild Building System | 升級流程依賴 P-02 建設面板呼叫 `TryUpgradeBuilding`；無 UI 時建築升級無觸發點（除作弊指令） |
| FT-02 Mission Dispatch / C-02 Adventurer Management | 推薦、除名等玩家動作依賴 P-02 觸發 |

### §6.3 雙向依賴標註備忘

依 `.claude/rules/design-docs.md` 規範，下列雙向依賴需於對方 GDD §6 註記反向引用。**狀態欄記錄 2026-05-01 design-review 後已完成的補強項：**

| 對方 GDD | 反向引用內容 | 狀態 |
|---|---|---|
| P-01 §6 | P-02 §3.8 對外 API surface 表已補完；P-02 實作 `OnEffectiveScaleChanged` callback；§6 反向引用 P-02 已更新 | ✅ 2026-05-01 已 patch（Pkg-3）|
| P-03 §6 | `BindLogWindow(VisualElement container)` API 簽章與生命週期於 P-03 §3.5 補完；§6「依賴 P-03 的系統」補 P-02 列 | ✅ 2026-05-01 已 patch（Pkg-4）|
| FT-07 §6 | 「P-02 訂閱 `OnBuildingUpgraded` 重整建設面板」、具名 effect getter / CanUpgrade / TryUpgradeBuilding 引用、IsStaffSystemUnlocked 引用 | ✅ 2026-05-01 已 patch（FT-07 §6.2 line 415-417 升級為 v0.6 對齊版本；審查處 buildingID 待 FT-07 P3.1-009 後續補登）|
| FT-08 §6 | 「P-02 訂閱 `OnStaffSystemBoot`、polling `GetCurrentCandidates`、呼叫 6 個 `Try*` API + `GetCurrentPoolID`」 | ✅ 2026-05-01 已 patch（Pkg-6）|
| FT-09 §6.2.2 / §3.7.1 | (1) 補 `GetPendingDialogueStages() : IReadOnlyList<int>` API（§3.7.1）+ 對應 §6.2.2 P-02 呼叫 API 表；(2) §6.2.2 已列 P-02 訂閱 5 + 1 個 FT-09 事件、呼叫 ConfirmDialogue；(3) 對 `specialEventKey == "ophelia_missing"` 確認對話後 P-02 發布 `OnOpheliaMissingNight` 給 FT-09 / 自身 SceneObjectController | ✅ 2026-05-01 已 patch（v0.6 同步補登）|
| FT-12 §6 | 「P-02 呼叫 `GetActiveRoster` / `GetRosterCap` / `GetStaffStateView` / `IsSuccessRatePreviewEnabled`；訂閱 `OnStaffHired` / `OnStaffFired` / `OnStaffAssigned` / `OnStaffStateChanged`」 | ✅ 2026-05-01 已 patch（Pkg-5）|
| C-02 §6 | 「P-02 依 v3.1 P3.1-005 §3.1.3 排序規則呈現名冊（奧菲莉雅永遠第一格）；除名/開除流程透過 P-02 確認彈窗觸發；訂閱 OnAdventurerDied / OnAdventurerRecovered / OnAdventurerDismissed」 | ✅ 2026-05-01 已 patch（C-02 §6.2 line 328 升級為 v0.6 對齊版本）|
| FT-04 §6 | 「P-02 訂閱 `OnAdventurerDied`、`OnMissionResolved` 用於名冊死亡標記與任務統計累積」 | ✅ 2026-05-01 已 patch（FT-04 §6 line 620 ⏳ → ✅ for P-02；P-03 仍 ⏳ 待設計）|
| F-03 §6 | 「P-02 訂閱 `OnGoldChanged(newValue, delta)`、`OnReputationChanged(newValue, delta)`、`OnBankruptcyWarningStateChanged` 即時更新 L1 持久 HUD」 | ✅ F-03 §6.2 line 409 已涵蓋全部 v0.6 事件 + API，無需修改 |
| C-06 §6 | 「P-02 呼叫 `GetCurrentLevel` / `GetDangerData`；訂閱 `OnDangerLevelChanged`」 | ✅ C-06 §6 已存在此條目（C-06 §6 P-02 列）|

> ⏳ 標記項目於 P-02 GDD 通過 design-review 後，由主體一次性 patch 至各 GDD（依 `design/GDD/DIP-index.md` 流程登記）。

---

## §7 調校旋鈕（Tuning Knobs）

P-02 的調校面向分三層：(1) 視覺/動畫常量（P-02 內部 ScriptableObject 設定）；(2) 資料表（CSV 內容）；(3) 固定 z-order 常量（不對外調整）。

### §7.1 視覺與動畫常量

收錄於 `P02UITuning` ScriptableObject（路徑：`Assets/Resources/Data/Tuning/P02UITuning.asset`）：

| 參數 | 預設值 | 安全範圍 | 影響面向 |
|---|---|---|---|
| `PANEL_TRANSITION_SECONDS` | 0.15 | 0.05 ~ 0.40 | 面板淡入淡出時長；過低跳變突兀，過高玩家覺得遲鈍 |
| `MODAL_OVERLAY_ALPHA` | 0.40 | 0.20 ~ 0.70 | L2 modal 半透明遮罩深度；過低背景干擾閱讀，過高失去「場景仍在跑」的視覺暗示 |
| `HOVER_OUTLINE_THICKNESS` | 2 | 1 ~ 4 | 場景物件 hover outline 像素粗細（受 `effectiveScale` 縮放）|
| `HOVER_OUTLINE_ALPHA` | 0.60 | 0.30 ~ 1.00 | hover outline 不透明度；過低玩家不易察覺可互動，過高遮蔽 sprite 細節 |
| `LOG_VISIBLE_MARGIN` | 32 | 16 ~ 64 | Log 浮動視窗最小可見錨點邊距（pixel）；保證至少有一個角落留在視窗內可被拖回 |
| `INTRO_TEXT_MAX_DISPLAY_CHARS` | 40 | 30 ~ 60 | UI 元素寬度不足時 ellipsis 截斷字元數；與 D-01/D-02 facade 輸出長度上限對齊 |
| `DIALOGUE_LINE_MAX_CHARS` | 24 | 18 ~ 32 | 對話視窗每行最大字元（中文計）；過低分頁過多影響節奏，過高溢出視窗邊界 |
| `DIALOGUE_LINES_PER_PAGE` | 3 | 2 ~ 5 | 對話視窗每頁行數；上下限受 P-01 視窗高度約束 |
| `STORY_NOTE_GRAY_HEX` | `#888888` | 任意有效 hex（避開 `#000000`） | Stage 4「她沒回來」場景說明層字色（v3.1 §3.7）|
| `STORY_NOTE_BLACK_HEX` | `#000001` | 任意非 `#000000` 的近黑色 | Stage 5 黑底背景色（避開 P-01 透明色鍵 `#000000`）|

> Jam 版單一語系（zhTW），字寬計算採「中文逐字計、英文以單字邊界」混排規則；Post-Jam 加入 en 後 `DIALOGUE_LINE_MAX_CHARS` 需重新校準（英文字數通常為中文 1.8~2.0 倍）。

### §7.2 字體大小（依優先級分級）

收錄於同一 `P02UITuning`：

| 參數 | 預設值（pt） | 安全範圍 | 用途 |
|---|---|---|---|
| `FONT_SIZE_PANEL_TITLE` | 16 | 12 ~ 22 | 面板標題 |
| `FONT_SIZE_CARD_HEADER` | 14 | 10 ~ 18 | 委託卡 / 名冊卡標題（名稱、難度等）|
| `FONT_SIZE_CARD_BODY` | 11 | 9 ~ 14 | 介紹短文、tooltip |
| `FONT_SIZE_DIALOGUE` | 14 | 10 ~ 20 | 故事面板對話本文 |
| `FONT_SIZE_HUD` | 13 | 10 ~ 16 | 金幣顯示、劇情指示器計數 |
| `FONT_SIZE_BUTTON` | 12 | 10 ~ 16 | 按鈕文字 |

> 字體大小受 `effectiveScale` 縮放影響；上述為 PanelSettings.scale=1.0 基準值。

### §7.3 資料表 tuning 面向

| 資料表 | tuning 範疇 | 設計責任歸屬 |
|---|---|---|
| `UIText.csv` | 所有玩家可見字串（按鈕、tooltip、面板標題、確認彈窗、錯誤提示）；新增/修改 key 不需改程式碼 | P-02 owner |
| `SceneObjectStateTable.csv` | 奧菲莉雅敘事物件的 stage → sprite 對映；新增 stage 變體只需加 row | P-02 owner（v3.1 P3.1-010）|
| `DialogueTable` | 故事面板對話內容；P-02 不擁有，僅消費 | FT-09 / 敘事資料表（非 P-02）|
| `NotificationTemplate` | Log 通知文字模板；P-02 不擁有，由 P-03 消費 | P-03 owner |

### §7.4 固定 z-order 常量（不對外調整）

§4.1 列出的 `SortingOrder.*` 常量為 P-02 內部固定值，調整需修改程式碼並通過 design-review；不視為 tuning：

| 常量 | 值 | 不可調整理由 |
|---|---|---|
| `SortingOrder.L0Scene` | 0 | UI Toolkit 慣例：場景層為基底 |
| `SortingOrder.L1HUD` | 100 | 高於場景，低於任何 modal |
| `SortingOrder.L2Modal` | 200 | 介於 L1 與面板之間，承載半透明遮罩 |
| `SortingOrder.L2Panel` | 210 | 高於 modal 遮罩 |
| `SortingOrder.ConfirmPopup` | 220 | 高於 base panel |
| `SortingOrder.StoryPanel` | 230 | 高於 base panel；與 ConfirmPopup 之間留 10 緩衝 |
| `SortingOrder.LogFloatingWindow` | 250 | 高於所有 L2，永遠可拖曳（§3.5.7）|

> 各值間隔 10 為 Post-Jam 預留（如插入新層級）；任何新增層級需在 design-review 中明示理由與向上/向下相容性。

### §7.5 玩家可調項

設定彈窗（§3.5.10）暴露給玩家的調整：

| 項目 | 範圍 / 步進 | 來源 | 持久化 |
|---|---|---|---|
| UI 縮放（userScale） | [P-01 USER_SCALE_MIN, USER_SCALE_MAX] step `USER_SCALE_STEP` | P-01 §7 | FT-10（P-01 內建）|
| 目標螢幕 | dropdown 列出可用螢幕 | P-01 `EnumerateAvailableMonitors` | FT-10（P-01 內建）|
| 還原預設尺寸 | 按鈕（將 userScale 重設為 1.0）| P-01 提供 | — |
| 音量 | Post-Jam 預留 | — | — |
| 語言 | Jam 版鎖定 zhTW | UIText.csv 欄位 | FT-10（Post-Jam）|

---

## §8 驗收標準（Acceptance Criteria）

### §8.1 啟動與整體架構

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-01 | 遊戲啟動後 P-02 在所有 CSV 載入完成、所有事件訂閱完成、L1 HUD 初始化完成後才發布 `OnUIReady` 事件 | 在 P-02 註入測試 hook，確認 `OnUIReady` 發布時：`_pendingStageDialogueQueue` 已初始化（empty 或還原狀態）、`UIText` 已載入、4 個 L1 元素已存在、P-01 effectiveScale callback 已註冊 |
| AC-02 | 啟動後場景層持續可見，閒置冒險者立繪正常顯示，未被任何面板完全遮蔽 | 目測：開啟任一 L2 面板，確認場景層仍透過 modal 遮罩可見（半透明遮罩 alpha=0.40） |
| AC-03 | L1 持久 HUD（金幣、劇情指示器、設定按鈕、Log）開啟任一 L2 面板時仍可見且可互動 | 目測 + 操作：開啟委託板，確認金幣顯示與設定按鈕可點 |
| AC-04 | `OpenPanel` 在 `OnUIReady` 之前被呼叫時 `LogWarning` 並回傳 false，不開面板 | 單元測試：注入 panel 開啟請求於 OnUIReady 之前，確認 LogWarning 與 false 回傳 |

### §8.2 場景物件導覽與 hover

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-05 | 6 個導覽用場景物件（委託欄/大廳/建設區/保險箱/沙發/辦公桌）滑鼠 hover 時顯示白色 outline，alpha=0.60 | 目測：hover 各物件，確認 outline 出現 |
| AC-06 | 點擊各場景物件開啟對應面板（§3.2 表格 6 對映正確） | 操作：分別點擊 6 個物件，確認開啟正確面板 |
| AC-07 | `nav_staff_lounge` 在 `FT07.GetBuildingLevel(6) < 1` 時點擊不開職員名冊；hover 顯示灰色 outline + tooltip「需建造職員休息室 L1」 | 操作：以建築 6 等級=0 啟動，hover 沙發確認 tooltip；點擊確認無面板開啟 |
| AC-08 | v3.1 SceneObjectController 管理的 5 個奧菲莉雅敘事物件（`ophelia_chair` 等）依當前 stage 顯示正確 spriteVariant，dialogueKey 為空者不可點 | 操作：模擬 `stageID=3`，確認 `ophelia_crest` 切為 `visible`，hover 顯示 outline；模擬 `stageID=1` 時 `ophelia_crest` 為 `hidden`，hover 不顯示 outline |

### §8.3 持久 HUD

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-09 | 金幣顯示訂閱 `OnGoldChanged(int newValue, int delta)` 事件，金幣變動後 1 frame 內反映於 UI；負值顯示為紅字 | 操作：呼叫 F-03 `AddGold(-1000)` 直至金幣為負，確認紅字 |
| AC-10 | 劇情進展指示器在 `_pendingStageDialogueQueue.Count == 0` 時隱藏；計數 > 0 時顯示紅點 + 數字；收到 `OnFactionStoryStageUnlocked` 事件後 enqueue 至 tail；故事面板顯示時 dequeue head；ConfirmDialogue `SUCCESS` 後若 queue 非空自動 dequeue head 顯示下一個（chain continue）| 操作：模擬同 frame 觸發 stage A + stage B 兩事件，確認紅點數字為 2；確認 stage A 後紅點數字為 1，且故事面板自動顯示 stage B；確認 stage B 後紅點隱藏 |
| AC-11 | 點擊劇情指示器在 chain 中斷（如 P-03 Critical 通知插隊後）時觸發手動 resume，從 queue head dequeue 並開啟故事面板（FIFO，與自動觸發一致）| 操作：模擬 stage A + stage B 入隊後 chain 因 Critical 通知中斷；點擊指示器，確認彈出 stage A（head）對話而非 stage B（FIFO 行為） |
| AC-12 | 點擊辦公桌或在 `_panelStack` 為空時按 ESC，皆開啟設定彈窗 | 操作：兩種觸發方式各驗證一次 |

### §8.4 面板生命週期與堆疊

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-13 | 面板淡入動畫於 `PANEL_TRANSITION_SECONDS`（0.15s）內完成，opacity 從 0 平滑到 1；期間元素不接收輸入 | 操作：開啟面板並嘗試在 0.05s 內點擊面板按鈕，確認無回應 |
| AC-14 | 兩個 base panel 同時開啟時，後開的關閉先開的（如委託板開啟中點擊大廳，名冊取代委託板） | 操作：依序開委託板 → 名冊，確認 `_panelStack` 只有名冊 |
| AC-15 | 故事面板開啟前自動關閉所有 L2 base panel；確認彈窗可疊加於故事面板上 | 操作：開啟委託板 → 觸發 FT-09 stage unlock，確認委託板已關、故事面板開啟 |
| AC-16 | ESC 在 `_panelStack.Top == StoryPanel` 時被忽略；必須點「確認」按鈕關閉 | 操作：開啟故事面板，連續按 ESC 5 次，確認面板仍開；點「確認」後關閉 |
| AC-17 | ESC 在 `_panelStack.Top == ConfirmPopup` 時等同點「取消」 | 操作：觸發確認彈窗，按 ESC，確認 `onCancel` 被呼叫且彈窗關閉 |
| AC-18 | modal 遮罩點擊行為等同 ESC（故事面板/確認彈窗例外，遮罩點擊不關閉） | 操作：開啟委託板，點擊遮罩，確認委託板關閉；開啟故事面板，點擊遮罩，確認面板仍開 |

### §8.5 面板資料反映

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-19 | 委託板資料源為 `FT02.GetCommissionsBySource(Regular)` + `GetCommissionsBySource(Static)`，新委託發布（`OnCommissionPosted(missionID, source)`）後 1 frame 內出現於列表 | 操作：模擬 FT-02 發布新委託（兩個 source 各一），確認委託板列表新增該卡 |
| AC-20 | 委託卡顯示元素完整：名稱、難度、類型、報酬、時長、介紹短文、狀態標籤、推薦按鈕（待審核時）；成功率預覽僅在 `FT12.IsSuccessRatePreviewEnabled() == true` 顯示；推薦冒險者子面板**不顯示** willingness 預估（Jam 版限制） | 目測 + 切換 FT-12 旗標確認預覽欄位顯示/隱藏；目測子面板無 willingness 欄位 |
| AC-21 | 冒險者名冊依 v3.1 排序（奧菲莉雅 templateID=901 永遠第一格，其餘依 idleSinceTimestamp 倒序） | 操作：載入含奧菲莉雅 + 5 名其他冒險者的存檔，確認名冊第一格為奧菲莉雅 |
| AC-22 | 冒險者除名/開除按鈕顯示規則：Dead 永遠顯示「除名」；Idle 僅 `FT07.GetBuildingLevel(審查處 buildingID) > 0` 時顯示「開除」；Wounded/OnMission 不顯示 | 操作：四種狀態各驗證一次，含審查處等級 = 0 vs > 0 對 Idle 狀態的差異 |
| AC-23 | 公會建設面板顯示 6 棟建築當前等級與下一級資訊；升級按鈕在 `CanUpgrade == false` 時灰色 + tooltip 顯示阻擋原因（金幣不足/聲望不足/公會等級不足/已達上限） | 操作：構造各阻擋情境，確認 tooltip 文字正確 |
| AC-24 | 職員名冊面板列出 FT-12 `GetActiveRoster()` 結果；空名冊時僅顯示「前往面試」按鈕 + 提示文字 | 操作：以空名冊啟動面板確認；招募 1 名職員後重整確認列表 |
| AC-25 | 職員面試面板開啟前先關閉職員名冊（堆疊處理）；錄用呼叫 `FT08.TryRecruit(slotIndex)` 回傳 `SUCCESS` 後 P-02 重開職員名冊 | 操作：點「前往面試」確認名冊關閉；錄用後確認名冊重開且新職員出現 |
| AC-26 | 公會總覽顯示等級/稱號/聲望/金幣/破產倒數/陣營分數/styleTag bias；訂閱 F-03 `OnBankruptcyWarningStateChanged` 事件進入警告狀態時顯示倒數，倒數值取自 `F03.GetBankruptcyWarningRemainingSeconds()` | 操作：構造破產情境（金幣 < bankruptcyThreshold），確認破產倒數顯示與每秒遞減 |

### §8.6 故事面板與 v3.1 整合

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-27 | `OnFactionStoryStageUnlocked` 事件觸發後故事面板自動開啟，呈現 `resolvedDialogueKey` 對應對話；玩家點「確認」後呼叫 FT-09 `ConfirmDialogue(stageID)` | 模擬事件觸發，確認對話內容與 ConfirmDialogue 呼叫 |
| AC-28 | `OnFactionStoryStageUnlocked.specialEventKey == "ophelia_missing"` 時，玩家確認對話後 P-02 發布 `OnOpheliaMissingNight` 事件 | 模擬 Stage 4 觸發，確認確認後 EventBus 收到 `OnOpheliaMissingNight` |
| AC-29 | Stage 4「她沒回來」場景說明層字色 `#888888`，疊加在公會場景內，不阻擋輸入（玩家可正常點擊其他物件） | 觸發 Stage 4，目測字色與互動 |
| AC-30 | Stage 5 對話採黑底白字（背景色 `#000001`，避開透明色鍵），3 行 24 字限制仍適用 | 觸發 Stage 5，目測背景色 hex 值 |
| AC-31 | FT-04 `OnAdventurerDied` 與 FT-09 `OnFactionStoryStageEpilogue` 同 frame 觸發時，先呈現死亡通知（P-03 Critical），確認後再呈現 epilogue 對話 | 模擬奧菲莉雅在 Stage 5 任務死亡，確認 UI 順序 |
| AC-32 | 紙條視覺依 `FT09.GetCurrentStyleTagBias()` 切換 light / dark / neutral 三版 | 三種 styleTag 各觸發一次故事面板，目測紙條樣式 |

### §8.7 SceneObjectController（v3.1 P3.1-010）

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-33 | `ResolveSceneObjectState(objectID)` 依 priority 由高至低評估 stageCondition，第一個成立者生效 | 單元測試：構造同 objectID 多 row 不同 priority，驗證選擇結果 |
| AC-34 | `OnOpheliaMissingNight` 事件後 `_activeEvents` 含 `"ophelia_missing"`；場景物件 `ophelia_chair` 切為 `empty` 變體 | 模擬事件，確認 sprite 切換 |
| AC-35 | `OnOpheliaReturned` 事件後 `_activeEvents` 移除 `"ophelia_missing"`；場景物件依 stageID 重新解析 | 模擬事件，確認 sprite 還原 |
| AC-36 | `stageCondition` 語法錯誤時 `LogError`，該 row 視為不成立，繼續評估下一 row | 注入錯誤 stageCondition row，確認 LogError + 後續 row 正常生效 |

### §8.8 縮放與螢幕適應

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-37 | P-01 推送新 effectiveScale 後，所有 PanelSettings.scale 更新，L1 場景內嵌 UI 重新計算錨點 | 操作：調整 P-01 userScale，確認金幣顯示位置跟隨保險箱物件 |
| AC-38 | 解析度變更（`WM_DISPLAYCHANGE`）期間若有面板開啟，面板尺寸自動跟隨新 scale 不關閉 | 操作：開啟委託板，於 Windows 設定變更解析度，確認面板仍開且尺寸正確 |
| AC-39 | 設定彈窗螢幕 dropdown 列出可用螢幕；切換後 P-01 移動視窗；已斷開螢幕標灰並 fallback 至主螢幕 | 多螢幕環境操作；拔線測試 fallback |

### §8.9 資料缺失與 fallback

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-40 | `UITextLookup(key)` 命中失敗時 `LogError` 並回傳 fallback；fallback 為空時回傳 key 本身 | 注入未知 key，確認 Console 錯誤與 UI 顯示 |
| AC-41 | DialogueTable 找不到 `dialogueKey` 時對話視窗顯示「[對話缺失：{dialogueKey}]」，「確認」按鈕仍可點，FT-09 `ConfirmDialogue` 仍被呼叫 | 注入未知 dialogueKey，確認顯示與後續流程 |
| AC-42 | `SceneObjectStateTable.csv` 載入失敗時降級：奧菲莉雅敘事物件全為 `SceneObjectState.Default`；其他面板與導覽點不受影響 | 移除 CSV 啟動，確認降級行為 |

### §8.10 P-01 / P-03 整合

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-43 | P-01 在 P-02 `OnUIReady` 後啟用 Hit-Test，`panel.Pick(localPos)` 對 P-02 UI 元素正確命中（命中回 HTCLIENT，未命中回 HTTRANSPARENT） | 操作：點擊 UI 按鈕（命中）vs 點擊背景（穿透至桌面），驗證行為差異 |
| AC-44 | P-03 在 P-02 `OnUIReady` 後初始化 Log 浮動視窗；P-02 呼叫 `BindLogWindow` 完成綁定 | 啟動後確認 Log 視窗出現於預設位置 |
| AC-45 | Log 浮動視窗 sortingOrder=250 高於所有 L2 面板（含故事面板 230），任何 L2 面板開啟時 Log 視窗仍可拖曳 | 操作：開啟故事面板，嘗試拖曳 Log 視窗確認可移動 |

---

## §九 變更歷史（Change History）

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-04-26 | v0.1 | GDD 骨架建立（§1 §2）；§3–§8 留待後續設計 |
| 2026-04-27 | v0.2 | §1 §2 完成；§3–§8 設計暫停（待 UI 示意圖） |
| 2026-05-01 | v0.3 | §3–§8 全節完成；新增 SceneObjectController（v3.1 P3.1-010 整合） |
| 2026-05-01 | v0.4 | **二次 design-review NEEDS REVISION 修正**：跨系統 API 對齊（C1–C9 共 9 類 issue）。涉及變更：(1) §3.3 劇情指示器改 P-02 內部 `_pendingDialogueCount` 記帳模型；(2) §3.5.1 委託板資料源改 `GetCommissionsBySource`；(3) §3.5.1.1 推薦子面板移除 willingness 預估（FT-02/FT-03 未提供 API），派遣 API 改 `Dispatch(instanceID, missionID, DispatchSource.PlayerManual)`；(4) §3.5.2 開除按鈕判定改 `GetBuildingLevel(審查處 buildingID) > 0`；(5) §3.5.3 建設面板資料源改 `GetBuildingLevel` + 自查 BuildingTable + 具名 effect getter；(6) §3.5.5 職員面試 API 全面對齊 FT-08 §3.3.4（`TryRecruit` / `GetCurrentCandidates` / polling 模式）；(7) §3.5.6 公會總覽破產倒數改 `OnBankruptcyWarningStateChanged` + `GetBankruptcyWarningRemainingSeconds`；(8) §3.6.4 SceneObjectController 改 `GetUnlockedStageIndex`；(9) §3.8 啟動序列補 P-01 effectiveScale callback 註冊；(10) §5.2 EC-04 改設計（依 FT-09 Bootstrap Step F 重發機制）；(11) §6.1 全層級 API 簽章對齊；(12) §6.3 雙向依賴備忘表加狀態欄；(13) §8 對應 AC 修正。並同步觸發其他 GDD patch：FT-12 §3.6.7 補 GetActiveRoster + GetRosterCap（Pkg-5）、FT-08 §3.3.4 補 GetCurrentPoolID（Pkg-6）、P-01 §3.8 補對外 API surface（Pkg-3）、P-03 §3.5 補 BindLogWindow（Pkg-4）|
| 2026-05-01 | v0.5 | **三次 design-review NEEDS REVISION 修正**（minor）：(1) **P0-1**：§3.3 劇情對話模型由純 `_pendingDialogueCount` 升級為 `_pendingStageDialogueQueue: Queue<int>`，明示 FIFO 自動 chain + 指示器點擊手動 resume，解決「最近 vs 逐一」設計矛盾；§3.5.8 多事件處理表述對齊；§5.2 EC-08 雙端 queue 同步說明；AC-10 / AC-11 重寫驗收場景；(2) **P0-2**：§6.2 row 4 過時 `HireSelected` → `TryRecruit`；(3) **P0-3**：AC-01 `_pendingDialogueStages` → `_pendingStageDialogueQueue` 並補 P-01 callback 已註冊驗證；(4) **P1-4**：§3.5.7 補 `BindLogWindow(VisualElement) : BindResult` 簽章與 3 個回傳碼處理；(5) **P1-5**：§3.5.10 / §4.2 引用 P-01 §3.8 / §3.8.1 而非 §3.7 / §3.5；(6) **P1-6**：§3.6.3 修正 `OnOpheliaMissingNight` 訂閱表述（澄清 P-02 自身 publish，FT-09 與 P-02 SceneObjectController 同為 subscriber）；(7) **P2-7**：§6.1 FT-12 row 移除多餘 `IsStaffSystemUnlocked()` 引用（P-02 統一用 `GetBuildingLevel(6) >= 1` 形式）|
| 2026-05-01 | v0.6 | **四次 design-review NEEDS REVISION 修正**（minor）：(1) **P0-α**：修正 BindLogWindow caller 方向矛盾——P-02 §3.8 step 7（原 step 7 拆為 step 8 + 9）改為「P-02 instantiate UXML container 並呼叫 P03.BindLogWindow(container)」；P-03 §3.5 lifecycle step 1-2 同步為「P-02 caller，P-03 callee」；(2) **P1-γ Bootstrap 時序問題修正**：FT-09 §3.7.1 補 `GetPendingDialogueStages() : IReadOnlyList<int>` 公開 API（FT-09 v0.6 同步 patch）；P-02 §3.8 新增 step 3「Bootstrap 復原」主動查詢，與 FT-09 Step F 重發機制構成雙重保險；§3.3 queue model 補 dedupe 規則；EC-04 改寫為「雙重保險」說明；(3) **§6.1 FT-09 row** 補 `GetPendingDialogueStages` API + `OnFactionRouteCompleted` 可選訂閱；(4) **§6.3 雙向依賴狀態欄** FT-09 條目改 ✅ 已 patch |
