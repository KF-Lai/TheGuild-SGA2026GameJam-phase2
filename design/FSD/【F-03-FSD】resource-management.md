# 【F-03-FSD】功能規格說明書 — Resource Management

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【F-03】resource-management.md`（版本：2026-04-26 design-review C4 修補後 + 同日 D3/D6 FSD 回註） |
| 對應 Data-Specs | `【F-03-DS】bankruptcy-threshold-table.md`（Phase 2 已 deprecated，保留為設計參考；2026-04-26 D2 落地紀錄已補入 DataSpec）<br>`【F-01-DS】system-constants.md`（共用 `GOLD_INITIAL` / `GOLD_MAX` / `REPUTATION_MIN` / `REPUTATION_MAX`） |
| 撰寫者 | unity-specialist subagent |
| Review 者 | unity-specialist subagent（自檢）；Claude Code 主體（D1+D2+D7 Codex 工項審查 + D3+D6 GDD 回註 patch） |
| 狀態 | 已完成（D1/D2/D7 落地；D3/D6 GDD 回註完成；D4 待 FT-10 FSD；D8 待 D1/D4 落地後再評估） |
| 最近更新 | 2026-04-26（Codex Medium 工項落地 D1+D2+D7：補 `SetBankruptcyWarningDuration` API、移除 deprecated 表查詢、移除 Warning 維持分支重新鎖定；F-03 GDD §3.4 / §4.3 加 D3/D6 FSD 回註；DataSpec 補 Phase 2 落地紀錄；Unity Editor 編譯通過） |

> **逆向 FSD 註記**：本 FSD 由既有實作（`ResourceManagement.cs` 636 行 + 4 個輔助檔）反推契約，並對齊 GDD §3 規則；逆向過程發現 8 項偏差登記於 §8.3，含 4 項屬「實作未跟上 GDD（需補實作）」、4 項屬「實作補強或機制差異（建議 FSD 回註 GDD）」。

---

## 1. 概要（Overview）

### 1.1 系統範圍

Resource Management 是金幣與聲望的唯一管理者，提供原子性的資源增減 API（`AddGold` / `AddGoldAllowBankruptcy` / `AddReputation`）、可調的破產門檻寫入 API（`SetBankruptcyThreshold`）、破產警告狀態機（Normal / Warning / Bankrupt）、以及 Snapshot 還原機制（FT-05 預收回滾用）。系統訂閱 `OnSecondTickEvent` 驅動破產倒數、訂閱 `OnOfflineResolvedEvent` 判定離線破產，並廣播 `OnGoldChangedEvent` / `OnReputationChangedEvent` / `OnBankruptcyStateChangedEvent` 三類事件供下游同步。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：

- 金幣／聲望狀態維護（含 `GOLD_MAX` clamp、聲望 `[REPUTATION_MIN, REPUTATION_MAX]` clamp）
- 兩條金幣寫入路徑：嚴格 `AddGold`（拒絕跌破門檻）與允許破產 `AddGoldAllowBankruptcy`
- 破產警告狀態機：Normal ↔ Warning ↔ Bankrupt 過渡、計時鎖定、剩餘秒數查詢
- 破產門檻被動接收 API：`SetBankruptcyThreshold(int)`（典型呼叫者 C-06）
- `EvaluateWarningState` 統一狀態校正入口（聲望變動、門檻變動、Snapshot 還原皆觸發）
- 重入防護：所有寫入 API 共用 `_isProcessing` 旗標
- Snapshot 機制：`CreateSnapshot` / `RestoreSnapshot`（FT-05 預收回滾用）
- 事件廣播：`OnGoldChangedEvent` / `OnReputationChangedEvent` / `OnBankruptcyStateChangedEvent`
- 訂閱 `OnSecondTickEvent` 與 `OnOfflineResolvedEvent`

**Out-of-Scope**：

- 聲望標籤（label）查詢——屬 P-02 Main UI 範疇，UI 自行查 `ReputationLabelTable`
- Game Over 後處理——屬 FT-06 Guild Core，本系統僅廣播 Bankrupt 事件
- 破產倒數秒數的業務語意——屬 FT-07 Guild Building 範疇（GDD §3.5 規範由 FT-07 透過 `SetBankruptcyWarningDuration` 推送；本實作目前**尚未實作該 API**，沿用舊版表查詢，見 §8.3 條目 D1/D2）
- 存檔序列化的 ISaveable 契約——GDD §6.4 規範實作目前**未實作 ISaveable**（見 §8.3 條目 D4）
- `ResetBankruptcyState` 的呼叫者——Game Jam 階段無上層使用（GDD §3.6 規則 9 已宣告為保留 API）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 驗收標準（AC-RM-01 ~ AC-RM-23），補充程式可驗證條件：

1. **AC-RM-01 拒絕邏輯**：呼叫 `AddGold(-9999)` 使結果 `< _currentBankruptcyThreshold` 時，回傳 `false`、`_currentGold` 不變、不發 `OnGoldChangedEvent`、不變動破產狀態。
2. **AC-RM-02 上限截斷**：`_currentGold == 9_000_000` 時 `AddGold(2_000_000)`，最終 `_currentGold == _goldMax (= 9_999_999)`、事件 `Delta == 999_999`。
3. **AC-RM-03 事件廣播**：合法 `AddGold` 後，`OnGoldChangedEvent(prev, current, delta)` 被廣播恰一次。
4. **AC-RM-04 聲望 clamp**：`_currentReputation == 80` 時 `AddReputation(50)`，最終 `_currentReputation == _reputationMax`、事件 `Delta == 20`。
5. **AC-RM-05 進入 Warning**：`_currentGold == 50` 時 `AddGold(-100)` 後（`_currentGold == -50`），`_warningState == Warning`、發 `OnBankruptcyStateChangedEvent(Normal, Warning)`。
6. **AC-RM-08 Warning 不重置計時器**：Warning 中再 `AddGold(-10)`，`_bankruptcyWarningStartTime` 不變。
7. **AC-RM-09 倒數到期觸發 Bankrupt**：mock `_warningDurationSec` 秒流逝後 `_currentGold < 0`，`HandleSecondTick` 偵測並轉 `Bankrupt`、發 `OnBankruptcyStateChangedEvent(Warning, Bankrupt)`。
8. **AC-RM-11 離線破產判定**：`HandleOfflineResolved` 在 Warning 且金幣負值且 elapsed ≥ duration 時觸發 `Bankrupt`。
9. **AC-RM-17 `AddGoldAllowBankruptcy` 跳過 reject**：`_currentGold == -50` 且 `threshold == -100` 時 `AddGoldAllowBankruptcy(-200)` → `_currentGold == -250`，立即轉 `Bankrupt`。
10. **AC-RM-19 單次至多一次過渡**：任意 `prevGold` × `amount` 組合下，單次 `AddGoldAllowBankruptcy` 廣播 ≤ 1 次 `OnBankruptcyStateChangedEvent`。
11. **AC-RM-20 預設門檻**：`Awake` 後若無推送，`GetCurrentBankruptcyThreshold()` 回傳 `-100`。
12. **AC-RM-21 `SetBankruptcyThreshold` 觸發狀態校正**：`_currentGold == -150`（threshold = -200）時呼叫 `SetBankruptcyThreshold(-100)` → 因 `gold < newThreshold` 立即 `Bankrupt`。
13. **EditMode 測試覆蓋**：CSV 載入零錯誤（`SystemConstants` 4 個常數讀取無例外）；Snapshot 還原後狀態與 `EvaluateWarningState` 校正後一致。

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- 【F-03】§1 概要（系統定位、兩種被動推送 API）
- 【F-03】§3.1 ~ §3.6 詳細規則（資源種類、金幣/聲望規則、事件廣播、API 簽名、破產警告狀態機）
- 【F-03】§4.1 ~ §4.6 公式（金幣增減、聲望增減、`CanAfford`、警告狀態機、門檻寫入）
- 【F-03】§5.1 ~ §5.4 邊緣案例（金幣／聲望／初始化／破產警告）
- 【F-03】§6 依賴關係（上下游、ISaveable 契約）
- 【F-03】§7.1 全域常數、§7.2 BankruptcyThresholdTable（已 deprecated）
- 【F-03】§8 驗收標準 AC-RM-01 ~ AC-RM-23

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `GOLD_INITIAL`（int，預設 100） | `Awake` 載入到 `_goldInitial`；`InitializeInstance` 設為 `_currentGold` 初始值 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `GOLD_MAX`（int，預設 9_999_999） | `Awake` 載入到 `_goldMax`；`AddGold` / `AddGoldAllowBankruptcy` clamp 上限；`RestoreSnapshot` 防腐 clamp 上限 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `REPUTATION_MIN`（int，預設 -100） | `Awake` 載入到 `_reputationMin`；`AddReputation` clamp 下限；`RestoreSnapshot` 防腐 clamp 下限 |
| `【F-01-DS】system-constants.md` | `SystemConstants.csv` | `REPUTATION_MAX`（int，預設 100） | `Awake` 載入到 `_reputationMax`；`AddReputation` clamp 上限；`RestoreSnapshot` 防腐 clamp 上限 |
| `【F-03-DS】bankruptcy-threshold-table.md`（Phase 2 deprecated） | `BankruptcyThresholdTable.csv` | `reputationMin` / `reputationMax` / `warningDurationSec` | **GDD §7.2 規範本表已 deprecated**；但實作 `LookupWarningDuration` 仍主動查詢，見 §8.3 條目 D2。Phase 2 規範改由 FT-07 透過 `SetBankruptcyWarningDuration` 推送（實作未實作該 API，見 §8.3 條目 D1） |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 介面 |
| --- | --- | --- |
| F-01 DataManager | 經 `DataManager.Instance.GetInt(key)` 讀取 4 個 SystemConstants；經 `DataManager.Instance.GetWhere<BankruptcyThresholdData>(predicate)` 查破產警告期表（**已 deprecated 但實作仍使用**）；經 `DataManager.RegisterTable<BankruptcyThresholdData>(BankruptcyThresholdTableName)` 於 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 註冊 | 引用「【F-01-FSD】§5.1 公開 API」之 `Get<T>` / `GetInt` 與 §5.4.1 註冊階段 |
| F-02 Time System | 訂閱 `OnSecondTickEvent` 驅動破產倒數；訂閱 `OnOfflineResolvedEvent` 判定離線破產；備援呼叫 `TimeSystem.Instance.NowUTC` 取 UTC 秒（若 `TimeSystem.Instance == null` 改用 `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`） | 引用「【F-02-FSD】§5.2 事件清單」之 `OnSecondTickEvent` / `OnOfflineResolvedEvent`，後者 payload 僅含 `OfflineSeconds`（採方案 A）|
| Core EventBus | 經 `EventBus.Subscribe<T>` / `Unsubscribe<T>` / `Publish<T>` 進行事件通訊 | 靜態 API |

> **Foundation 層零 Core 依賴**：F-03 在同層只依賴 F-01 / F-02；C-06 World Danger System 與 FT-07 Guild Building System 屬 Core/Feature 層，其推送方向為下游 → F-03（呼叫寫入 API），F-03 **不依賴**這些上層系統（不查詢、不訂閱）。

### 2.4 下游被依賴系統

| 系統 | 依賴內容 | 介面 |
| --- | --- | --- |
| C-06 World Danger System | 啟動時與危險度升級時主動呼叫 `SetBankruptcyThreshold(maxDebt)` | `SetBankruptcyThreshold(int)` |
| FT-02 Mission Dispatch | 預收傭金前檢查 `CanAfford`、預收成功扣款（屬 FT-05 金流範疇） | `CanAfford` / `AddGoldAllowBankruptcy` |
| FT-04 Outcome Resolution | 結算時增減金幣與聲望 | `AddGold` / `AddReputation` |
| FT-05 Guild Gold Flow | 預收／退還／結算入帳／賠償／維護費／薪水扣款；預收回滾使用 Snapshot | `AddGoldAllowBankruptcy` / `CanAfford` / `CreateSnapshot` / `RestoreSnapshot` |
| FT-06 Guild Core | 訂閱 `OnBankruptcyStateChangedEvent`（CurrentState == Bankrupt）進入 Game Over 終態流程；讀聲望判定升等 | `OnBankruptcyStateChangedEvent` / `GetReputation` |
| FT-07 Guild Building | 建設前 `CanAfford`、建設後 `AddGold`；GDD 規範另需主動呼叫 `SetBankruptcyWarningDuration`（**實作未提供此 API**，見 §8.3 條目 D1） | `CanAfford` / `AddGold` |
| FT-08 Gacha System | 面試手動刷新費扣款（gacha 入口） | `CanAfford` / `AddGold` |
| FT-12 Staff System | 解雇資遣費透過 FT-05 中介 `AddGoldAllowBankruptcy`；薪水扣款 Phase 2 | `AddGoldAllowBankruptcy`（FT-05 中介）|
| FT-01 Adventurer Recruitment | 老手邀請費用支出 | `CanAfford` / `AddGold` |
| FT-10 Save/Load | GDD §6.4 規範 ISaveable 序列化 7 欄位（**實作未實作 ISaveable**，目前以 `CreateSnapshot` / `RestoreSnapshot` 替代，見 §8.3 條目 D4） | `CreateSnapshot` / `RestoreSnapshot`（暫代）；GDD 規範 `ISaveable.Serialize` / `RestoreFromSave` / `InitializeAsNewGame` |
| P-02 Main UI | 訂閱 3 個事件即時更新顯示；查詢剩餘秒數顯示倒數 | `OnGoldChangedEvent` / `OnReputationChangedEvent` / `OnBankruptcyStateChangedEvent` / `GetBankruptcyWarningRemainingSeconds` |

### 2.5 跨系統事件契約

| 事件 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnGoldChangedEvent` | F-03 → * | `int PreviousGold` / `int CurrentGold` / `int Delta` | `AddGold` / `AddGoldAllowBankruptcy` 執行成功且 `actualDelta != 0` 時發布；P-02 UI 即時更新；FT-05 預收／結算流程比對前後值 |
| `OnReputationChangedEvent` | F-03 → * | `int PreviousReputation` / `int CurrentReputation` / `int Delta` | `AddReputation` 執行成功且 `actualDelta != 0` 時發布；P-02 UI 即時更新；FT-06 觸發升等檢查 |
| `OnBankruptcyStateChangedEvent` | F-03 → * | `BankruptcyWarningState PreviousState` / `BankruptcyWarningState CurrentState` | 狀態實際過渡時發布（previous != current）；FT-06 訂閱 `Bankrupt` 進入 Game Over；P-02 UI 切換警告 banner |
| `OnSecondTickEvent` | F-02 → F-03 | `long CurrentUTCTimestamp` | `HandleSecondTick`：若 `_warningState == Warning`，檢查 `GetBankruptcyWarningRemainingSeconds() <= 0` 觸發 `Bankrupt` |
| `OnOfflineResolvedEvent` | F-02 → F-03 | `long OfflineSeconds`（payload 採方案 A，僅含 offlineSeconds） | `HandleOfflineResolved`：若 Warning 且金幣負值且 elapsed ≥ duration → `Bankrupt`；否則執行 `EvaluateWarningState` 統一校正 |

> **事件 payload 偏差**：GDD §3.4 規範 `OnBankruptcyWarningStateChanged(BankruptcyWarningState newState)` 僅含新狀態；實作 `OnBankruptcyStateChangedEvent(previousState, currentState)` 含前後雙欄位。實作為 superset，下游可向後兼容；建議 GDD 加 FSD 回註（見 §8.3 條目 D3）。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

金幣是公會存亡的命脈：每個高難度委託背後是「失敗就要賠錢」的真實壓力；負債不是 Game Over，而是必須翻身的絕境感。聲望影響緩衝空間——高聲望帶寬裕的破產緩衝，崩盤時債務撲面而來。資源管理是「有重量的決策」的數值基礎。

### 3.2 系統目的還原

提供金幣/聲望兩項核心資源的唯一管理通路，集中以 `AddGold` / `AddReputation` 控制變動，杜絕散落於各系統的直接修改。同步維護破產警告狀態機（Normal / Warning / Bankrupt），確保從 Warning 進入到 Bankrupt 的判定條件統一、可預期、可從存檔還原。Snapshot 機制讓 FT-05 預收/結算流程具備原子性回滾能力。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 破產風險真實存在 | 金幣轉負時 UI 出現倒數警告 banner，倒數歸零後遊戲確實結束（Game Over） | `EvaluateWarningState` 統一校正入口 + `HandleSecondTick` 每秒檢查剩餘秒數 + `OnBankruptcyStateChangedEvent(Bankrupt)` 廣播至 FT-06 觸發 Game Over |
| 金錢是有限資源 | 想招募高薪冒險者卻被「金幣不足」擋下；建設前需確認預算 | `AddGold` 嚴格路徑：`target < threshold` 直接 reject 回傳 false；`CanAfford(amount)` 提供預先檢查；`GOLD_MAX` clamp 上限 |
| 負債也能繼續玩 | 即使金幣為負仍可接任務；倒數期內所有操作不受限 | `AddGoldAllowBankruptcy` 例外路徑允許跌破門檻；GDD §3.6 rule 3「警告期間玩家可執行所有操作」對應實作未在任何 API 加入 Warning 狀態檢查 |
| 預收回滾不丟金幣 | FT-02 預收後若任務派遣失敗，金幣完整退回 | `CreateSnapshot` 線性快照 + `RestoreSnapshot` 防腐還原 + `EvaluateWarningState` 重算狀態 |
| 聲望影響緩衝空間 | 聲望越高，破產警告期越長 | （**實作偏差**）GDD 規範改由 FT-07 預備金保險櫃推送 `_currentWarningDuration`；實作仍走 `LookupWarningDuration(reputation)` 查表。見 §8.3 條目 D2 |
| 上層動態調整門檻 | C-06 危險度升級時，破產門檻自動放寬 | `SetBankruptcyThreshold(int)` 寫入 API + `EvaluateWarningState` 防禦性校正（門檻緊縮且金幣已破時立即 Bankrupt）|
| 離線時間真實流逝 | 關遊戲一段時間後，原本的破產倒數確實會繼續推進 | `HandleOfflineResolved` 訂閱 `OnOfflineResolvedEvent`（payload `OfflineSeconds`），若 Warning 且金幣負且 elapsed ≥ duration → `Bankrupt` |
| 重入安全 | 訂閱者在事件 callback 中誤呼叫資源 API 不會造成無限遞迴 | `_isProcessing` 共用旗標 + `TryEnterProcessing` / `ExitProcessing` 包圍 try/finally；偵測到重入 `LogError` 拒絕操作 |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

**否**（採方向 C：保留現況單檔，內部以職責分區方式組織）。

### 4.2 拆分理由

主檔 `ResourceManagement.cs` 為 **636 行**，已超過 FSD-index §2.4 拆分門檻 500 行。經拆分判斷：

1. **職責緊耦合，B 方向（拆 A/B）不可行**：金幣寫入（`AddGold`）、聲望寫入（`AddReputation`）、破產狀態機（`EvaluateWarningState` / `EnterWarning` / `ExitWarning` / `TriggerBankruptcy`）、Snapshot 流程（`CreateSnapshot` / `RestoreSnapshot`）四者共用同一份內部狀態（`_currentGold` / `_currentReputation` / `_warningState` / `_bankruptcyWarningStartTime` / `_warningDurationSec` / `_currentBankruptcyThreshold`）與同一份 `_isProcessing` 重入旗標。若拆「資源持有」vs「破產狀態機」兩層，需把 6 個欄位的寫權限暴露給狀態機類別，破壞封裝且引入跨類同步成本。
2. **原子性無法切割**：`AddGold` → `UpdateBankruptcyWarning`（同步進入/離開 Warning）、`AddGoldAllowBankruptcy` → `EvaluateWarningState`（涵蓋 Bankrupt 分支）、`AddReputation` → `EvaluateWarningState`（聲望變動觸發狀態校正）、`SetBankruptcyThreshold` → `EvaluateWarningState`（門檻變動觸發校正）。每一條寫入路徑都必須在 try/finally 內完成「資源變動 + 事件廣播 + 狀態校正」三步，跨類拆分會讓 `_isProcessing` 跨越邊界，增加死鎖風險。
3. **GDD §3 內部無清晰職責分區**：規則本身把資源管理與破產警告綁為一個「核心循環」（§3.4 事件廣播明列「`AddGold` / `AddGoldAllowBankruptcy` / `AddReputation` 共用同一 `_isProcessing`」），未提示可拆。
4. **採方向 C 的具體建議（§8.3 條目 D8 登記）**：保留單檔，內部以 `#region` 分 5 區：「公開查詢 API」（`GetGold` / `GetReputation` / `CanAfford` / `Get*`）、「公開寫入 API」（`AddGold` / `AddGoldAllowBankruptcy` / `AddReputation` / `SetBankruptcyThreshold` / `ResetBankruptcyState`）、「Snapshot」（`CreateSnapshot` / `RestoreSnapshot`）、「破產狀態機」（`EvaluateWarningState` / `UpdateBankruptcyWarning` / `EnterWarning` / `ExitWarning` / `TriggerBankruptcy` / `LookupWarningDuration`）、「生命週期與事件處理」（`Awake` / `OnEnable` / `OnDisable` / `HandleSecondTick` / `HandleOfflineResolved` / `LoadConfig` / `TryEnterProcessing` / `ExitProcessing` / `GetNowUtc`）。職責分區後檔案行數不變，但可讀性與審查效率提升。

### 4.3 拆分結果

不適用（未拆分為多檔）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `ResourceManagement` | `Assets/Scripts/Gameplay/Resources/ResourceManagement.cs` | 維護金幣/聲望狀態、破產警告狀態機、Snapshot 機制與資源相關事件廣播 | `MonoBehaviour`、`DataManager`（讀常數與表）、`EventBus`、`TimeSystem`（備援取 NowUTC） | 600~650 行（**已超 500 行門檻**，職責分區建議見 §4.2 / §8.3 條目 D8） |
| `OnGoldChangedEvent`<br>`OnReputationChangedEvent`<br>`OnBankruptcyStateChangedEvent` | `Assets/Scripts/Gameplay/Resources/Events/ResourceEvents.cs` | 三類資源變動事件的 `readonly struct` payload 集合（避免裝箱） | — | 50~80 行 |
| `ResourceSnapshot` | `Assets/Scripts/Gameplay/Resources/ResourceSnapshot.cs` | 資源狀態快照 DTO（含金幣/聲望/破產三組欄位）；FT-05 預收回滾用 | `[Serializable]` | < 50 行 |
| `BankruptcyWarningState` | `Assets/Scripts/Gameplay/Resources/BankruptcyWarningState.cs` | 破產警告狀態列舉（`Normal=0` / `Warning=1` / `Bankrupt=2`）| — | < 30 行 |
| `BankruptcyThresholdData` | `Assets/Scripts/Gameplay/Resources/BankruptcyThresholdData.cs` | `BankruptcyThresholdTable.csv` 對應的 DTO（Phase 2 deprecated 但實作仍使用，見 §8.3 條目 D2） | CsvParser 反射綁定（欄位名 case-sensitive） | < 30 行 |

### 4.5 類別關係（可選）

```
[C-06]                  [FT-07]              [FT-04 / FT-05 / FT-08 / ...]
  │ SetBankruptcyThreshold │ (規範: SetBankruptcyWarningDuration  │ AddGold / AddGoldAllowBankruptcy
  │                        │  -- 實作未提供此 API)                 │ AddReputation / CanAfford
  ▼                        ▼                                       ▼
┌────────────────────────────────────────────────────────────────────┐
│                   ResourceManagement (MB)                          │
│  Fields: _currentGold / _currentReputation /                       │
│          _warningState / _bankruptcyWarningStartTime /             │
│          _warningDurationSec / _currentBankruptcyThreshold /       │
│          _isProcessing                                             │
│                                                                    │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐        │
│  │ Public Query   │  │ Public Write   │  │ Snapshot       │        │
│  └────────────────┘  └────────┬───────┘  └────────┬───────┘        │
│                               │                   │                │
│                               ▼                   │                │
│                      ┌────────────────┐           │                │
│                      │ Bankruptcy SM  │◄──────────┘                │
│                      │ EvaluateWarning│                            │
│                      │ Enter/Exit/Trig│                            │
│                      └────────┬───────┘                            │
│                               │ EventBus.Publish                   │
│                               ▼                                    │
│                      OnGoldChangedEvent /                          │
│                      OnReputationChangedEvent /                    │
│                      OnBankruptcyStateChangedEvent                 │
└──────────────────────────┬─────────────────────────────────────────┘
                           ▲
       OnSecondTickEvent / OnOfflineResolvedEvent
                           │
                       [F-02 TimeSystem]
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

**單例**：`static ResourceManagement Instance { get; private set; }`（`Awake` 初始化；多實例後到者自我銷毀）。

**查詢 API**（無副作用，可任何時候呼叫）：

| 簽名 | 行為 |
| --- | --- |
| `int GetGold()` | 回傳 `_currentGold` |
| `int GetReputation()` | 回傳 `_currentReputation` |
| `bool CanAfford(int amount)` | `amount <= 0` 直接回 `true`（短路；GDD §4.3 公式無此短路，行為等價，見 §8.3 條目 D6）；否則回 `(long)_currentGold - amount >= _currentBankruptcyThreshold` |
| `int GetCurrentBankruptcyThreshold()` | 回傳 `_currentBankruptcyThreshold`（預設 `-100`）|
| `BankruptcyWarningState GetBankruptcyWarningState()` | 回傳 `_warningState` |
| `long GetBankruptcyWarningRemainingSeconds()` | 非 Warning 回 `0`；Warning 時回 `max(0, _warningDurationSec - (NowUTC - _bankruptcyWarningStartTime))`；elapsed ≤ 0 時回 `_warningDurationSec`（保護時鐘倒退）|

**寫入 API**（共用 `_isProcessing` 重入旗標）：

| 簽名 | 回傳 / 行為 |
| --- | --- |
| `bool AddGold(int delta)` | 嚴格路徑；`target < threshold` 回 `false` 不變動；clamp 上限 `_goldMax`；`actualDelta != 0` 才發 `OnGoldChangedEvent` 並呼叫 `UpdateBankruptcyWarning(prev, new)`；重入回 `false` |
| `bool AddGoldAllowBankruptcy(int delta)` | 允許跌破門檻路徑；對 `target` 做 `int.MinValue` / `int.MaxValue` 截斷防溢位；clamp 上限 `_goldMax`；`actualDelta != 0` 才發 `OnGoldChangedEvent` 並呼叫 `EvaluateWarningState`；重入回 `false` |
| `void AddReputation(int delta)` | clamp 至 `[_reputationMin, _reputationMax]`；`actualDelta != 0` 才發 `OnReputationChangedEvent`；無論 delta 是否為 0 皆呼叫 `EvaluateWarningState`；重入靜默 return |
| `void SetBankruptcyThreshold(int threshold)` | 直接寫入 `_currentBankruptcyThreshold`，呼叫 `EvaluateWarningState` 校正狀態（門檻緊縮且金幣已破時立即 `Bankrupt`）|
| `void ResetBankruptcyState()` | 呼叫 `ExitWarning()` 將狀態重設為 `Normal` 並廣播事件；GDD §3.6 rule 9 註明 Game Jam 階段無呼叫者，保留供未來擴充 |

**Snapshot API**（FT-05 預收回滾用）：

| 簽名 | 行為 |
| --- | --- |
| `ResourceSnapshot CreateSnapshot()` | 線性複製 6 個欄位至新 `ResourceSnapshot` 實例 |
| `void RestoreSnapshot(ResourceSnapshot snapshot)` | snapshot 為 null → `LogError` 返回；防腐 sanitize：無效列舉值 → `Normal`、負時間戳 → 0、負持續秒 → 0、金幣超 `_goldMax` → clamp、聲望超 `[_reputationMin, _reputationMax]` → clamp、正值門檻 → `DefaultBankruptcyThreshold (-100)`；最後呼叫 `EvaluateWarningState` 重算狀態 |

**測試專用 internal API**：`internal static void ResetForTests()`、`internal void InitializeForTests()`。

> **缺失 API（GDD §3.5 rule 8/9 規範但未實作）**：`SetBankruptcyWarningDuration(int)` 與 `GetBankruptcyWarningDuration()`；對應欄位 `_currentWarningDuration` 亦不存在。詳見 §8.3 條目 D1。

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnGoldChangedEvent` | F-03 → * | `int PreviousGold` / `int CurrentGold` / `int Delta` | `AddGold` / `AddGoldAllowBankruptcy` 成功且 `actualDelta != 0` 時發；P-02 UI 即時更新；FT-05 比對前後值 |
| `OnReputationChangedEvent` | F-03 → * | `int PreviousReputation` / `int CurrentReputation` / `int Delta` | `AddReputation` 成功且 `actualDelta != 0` 時發；P-02 UI 即時更新 |
| `OnBankruptcyStateChangedEvent` | F-03 → * | `BankruptcyWarningState PreviousState` / `BankruptcyWarningState CurrentState` | 狀態實際過渡時發（previous != current）；FT-06 訂閱 Bankrupt 進入 Game Over；P-02 UI 切換 banner |
| `OnSecondTickEvent` | F-02 → F-03（訂閱） | `long CurrentUTCTimestamp` | `HandleSecondTick`：`_warningState != Warning` 早退；否則檢查 `GetBankruptcyWarningRemainingSeconds() <= 0` 觸發 `TriggerBankruptcy` |
| `OnOfflineResolvedEvent` | F-02 → F-03（訂閱） | `long OfflineSeconds`（採方案 A） | `HandleOfflineResolved`：若 Warning 且金幣負且 `NowUTC - _bankruptcyWarningStartTime >= _warningDurationSec` → `TriggerBankruptcy`；否則呼叫 `EvaluateWarningState` 統一校正 |

### 5.3 資料結構

**`BankruptcyWarningState`**（`Assets/Scripts/Gameplay/Resources/BankruptcyWarningState.cs`）：

| 值 | 名稱 | 條件 |
| --- | --- | --- |
| 0 | `Normal` | `_currentGold >= 0` |
| 1 | `Warning` | `_currentBankruptcyThreshold <= _currentGold < 0` 且倒數未到期 |
| 2 | `Bankrupt` | 倒數到期時 `_currentGold < 0`，或 `_currentGold < _currentBankruptcyThreshold` |

**`ResourceSnapshot`**（`Assets/Scripts/Gameplay/Resources/ResourceSnapshot.cs`，`[Serializable]` `sealed class`）：

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `CurrentGold` | `int` | 快照建立時的金幣 |
| `CurrentReputation` | `int` | 快照建立時的聲望 |
| `WarningState` | `BankruptcyWarningState` | 快照建立時的破產警告狀態 |
| `BankruptcyWarningStartTime` | `long` | Warning 開始的 UTC Unix 秒；非 Warning 時為 0 |
| `WarningDurationSec` | `long` | Warning 鎖定的倒數秒數 |
| `CurrentBankruptcyThreshold` | `int` | 快照建立時的破產門檻 |

**`BankruptcyThresholdData`**（`Assets/Scripts/Gameplay/Resources/BankruptcyThresholdData.cs`，CsvParser 反射綁定）：

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `reputationMin` | `int` | 區間下限（含） |
| `reputationMax` | `int` | 區間上限（含） |
| `warningDurationSec` | `long` | 該聲望區間對應的警告期秒數 |

**事件 payload struct**：見 §5.2；皆為 `readonly struct`。

**內部欄位**（不對外）：

```csharp
private const string BankruptcyThresholdTableName = "BankruptcyThresholdTable";
private const string GoldInitialKey = "GOLD_INITIAL";
private const string GoldMaxKey = "GOLD_MAX";
private const string ReputationMinKey = "REPUTATION_MIN";
private const string ReputationMaxKey = "REPUTATION_MAX";

private const int  DefaultGoldInitial = 100;
private const int  DefaultGoldMax = 9999999;
private const int  DefaultReputationMin = -100;
private const int  DefaultReputationMax = 100;
private const int  DefaultBankruptcyThreshold = -100;
private const long DefaultWarningDurationSec = 86400L;

private int _goldInitial;       // Awake LoadConfig
private int _goldMax;           // Awake LoadConfig
private int _reputationMin;     // Awake LoadConfig
private int _reputationMax;     // Awake LoadConfig

private int _currentGold;
private int _currentReputation;
private int _currentBankruptcyThreshold;
private BankruptcyWarningState _warningState;
private long _bankruptcyWarningStartTime;
private long _warningDurationSec;

private bool _isProcessing;     // 重入旗標
```

> **GDD 規範但實作缺失欄位**：`_currentWarningDuration`（GDD §3.5 規則 8/9）。實作以 `LookupWarningDuration(reputation)` 即時查表替代，見 §8.3 條目 D1/D2。

### 5.4 內部資料流

#### 5.4.1 註冊與啟動流程

```
Unity.RuntimeInitialize (BeforeSceneLoad)
  → ResourceManagement.RegisterTables
      └─ DataManager.RegisterTable<BankruptcyThresholdData>("BankruptcyThresholdTable")

Unity.SceneLoad
  → ResourceManagement.Awake → InitializeInstance
      ├─ if (Instance != null && Instance != this) → Destroy(gameObject) 返回
      ├─ Instance = this; if (Application.isPlaying) DontDestroyOnLoad
      ├─ LoadConfig
      │     ├─ if (DataManager.Instance == null) → 用 Default* 常數，LogError
      │     ├─ _goldInitial = ReadConfigIntOrDefault("GOLD_INITIAL", 100)
      │     ├─ _goldMax     = ReadConfigIntOrDefault("GOLD_MAX", 9999999)
      │     ├─ _reputationMin = ReadConfigIntOrDefault("REPUTATION_MIN", -100)
      │     ├─ _reputationMax = ReadConfigIntOrDefault("REPUTATION_MAX", 100)
      │     ├─ if (_goldMax < _goldInitial) → _goldMax = _goldInitial（防呆）
      │     └─ if (_reputationMin > _reputationMax) → swap（防呆）
      ├─ _currentGold = _goldInitial
      ├─ _currentReputation = 0
      ├─ _currentBankruptcyThreshold = DefaultBankruptcyThreshold (-100)
      ├─ _warningState = Normal
      ├─ _bankruptcyWarningStartTime = 0
      └─ _warningDurationSec = 0

Unity.OnEnable
  → ResourceManagement.OnEnable → SubscribeEvents
      ├─ EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick)
      └─ EventBus.Subscribe<OnOfflineResolvedEvent>(HandleOfflineResolved)
```

#### 5.4.2 `AddGold` 嚴格路徑（同步流程）

```
[Caller].AddGold(delta)
  → TryEnterProcessing("AddGold")
      ├─ if (_isProcessing) → LogError, return false
      └─ _isProcessing = true
  → try
      ├─ target = (long)_currentGold + delta
      ├─ if (target < _currentBankruptcyThreshold) → return false（reject；金幣不變）
      ├─ if (target > _goldMax) → target = _goldMax（clamp 上限）
      ├─ previousGold = _currentGold
      ├─ newGold = (int)target
      ├─ actualDelta = newGold - previousGold
      ├─ if (actualDelta == 0) → return true（不發事件）
      ├─ _currentGold = newGold
      ├─ EventBus.Publish(new OnGoldChangedEvent(previousGold, _currentGold, actualDelta))
      ├─ UpdateBankruptcyWarning(previousGold, _currentGold)
      │     ├─ if (Warning && newGold >= 0) → ExitWarning
      │     │       ├─ _warningState = Normal
      │     │       ├─ _bankruptcyWarningStartTime = 0
      │     │       ├─ _warningDurationSec = 0
      │     │       └─ EventBus.Publish(new OnBankruptcyStateChangedEvent(Warning, Normal))
      │     └─ if (Normal && previousGold >= 0 && newGold < 0) → EnterWarning
      │             ├─ _warningState = Warning
      │             ├─ _bankruptcyWarningStartTime = GetNowUtc()
      │             ├─ _warningDurationSec = LookupWarningDuration(_currentReputation)  ← 仍走表查；GDD 規範應用 _currentWarningDuration
      │             └─ EventBus.Publish(new OnBankruptcyStateChangedEvent(Normal, Warning))
      └─ return true
  → finally
      └─ ExitProcessing → _isProcessing = false
```

#### 5.4.3 `AddGoldAllowBankruptcy` 例外路徑（FT-05 用）

```
[FT-05].AddGoldAllowBankruptcy(delta)
  → TryEnterProcessing("AddGoldAllowBankruptcy") → 重入回 false
  → try
      ├─ target = (long)_currentGold + delta
      ├─ if (target > int.MaxValue) → LogWarning, target = int.MaxValue（防溢位）
      ├─ if (target < int.MinValue) → LogWarning, target = int.MinValue（防下溢）
      ├─ if (target > _goldMax)     → target = _goldMax（clamp 上限；下限不截）
      ├─ previousGold = _currentGold
      ├─ newGold = (int)target
      ├─ actualDelta = newGold - previousGold
      ├─ if (actualDelta == 0) → return true
      ├─ _currentGold = newGold
      ├─ EventBus.Publish(new OnGoldChangedEvent(previousGold, _currentGold, actualDelta))
      └─ EvaluateWarningState  ← 走統一入口（含 Bankrupt 分支）
            ├─ if (_currentGold >= 0) → ExitWarning, return
            ├─ if (_currentGold < _currentBankruptcyThreshold) → TriggerBankruptcy, return
            ├─ if (_warningState != Warning) → EnterWarning
            └─ else → _warningDurationSec = LookupWarningDuration(_currentReputation)  ← 既有實作行為（見 §8.3 條目 D7）
  → finally
      └─ ExitProcessing
```

#### 5.4.4 `AddReputation`（聲望變動 + 統一狀態校正）

```
[Caller].AddReputation(delta)
  → TryEnterProcessing("AddReputation") → 重入靜默 return
  → try
      ├─ previous = _currentReputation
      ├─ target = clamp((long)_currentReputation + delta, _reputationMin, _reputationMax)
      ├─ _currentReputation = (int)target
      ├─ actualDelta = _currentReputation - previous
      ├─ if (actualDelta != 0) → EventBus.Publish(new OnReputationChangedEvent(previous, _currentReputation, actualDelta))
      └─ EvaluateWarningState   ← 即使 delta=0 也呼叫（為配合 GDD §5.2「delta=0 仍重算」）
  → finally
      └─ ExitProcessing
```

#### 5.4.5 每秒 Tick 與離線結算（事件訂閱端）

```
[F-02].EventBus.Publish(OnSecondTickEvent)
  → ResourceManagement.HandleSecondTick(evt)
      ├─ if (_warningState != Warning) → return（早退）
      └─ if (GetBankruptcyWarningRemainingSeconds() <= 0)
            └─ TriggerBankruptcy
                  ├─ if (_warningState == Bankrupt) → return（冪等保護）
                  ├─ previous = _warningState
                  ├─ _warningState = Bankrupt
                  ├─ _bankruptcyWarningStartTime = 0
                  ├─ _warningDurationSec = 0
                  └─ EventBus.Publish(new OnBankruptcyStateChangedEvent(previous, Bankrupt))

[F-02].EventBus.Publish(OnOfflineResolvedEvent { OfflineSeconds })
  → ResourceManagement.HandleOfflineResolved(evt)
      ├─ if (Warning && _currentGold < 0)
      │     ├─ elapsed = GetNowUtc() - _bankruptcyWarningStartTime
      │     └─ if (elapsed >= _warningDurationSec) → TriggerBankruptcy, return
      └─ EvaluateWarningState   ← 兜底校正（涵蓋金幣回正應 ExitWarning、金幣破門檻應 Bankrupt）
```

#### 5.4.6 Snapshot 還原流程（FT-05 預收回滾）

```
[FT-05].snapshot = ResourceManagement.Instance.CreateSnapshot()    ← 預收前線性複製 6 欄位
[FT-05].AddGoldAllowBankruptcy(-cost)                              ← 預收
... 其他派遣流程 ...
[FT-05] 派遣失敗 → ResourceManagement.Instance.RestoreSnapshot(snapshot)
  → if (snapshot == null) → LogError, return
  → sanitize 6 欄位（無效列舉/負時間/負秒/超 GOLD_MAX/超聲望範圍/正值門檻）
  → 寫回 _currentGold / _currentReputation / _warningState / _bankruptcyWarningStartTime / _warningDurationSec / _currentBankruptcyThreshold
  → EvaluateWarningState   ← 還原後重算狀態，避免 sanitize 後的不一致
```

> **注意**：`RestoreSnapshot` 不發 `OnGoldChangedEvent` / `OnReputationChangedEvent`（只有 `EvaluateWarningState` 內部可能發 `OnBankruptcyStateChangedEvent`）；UI 應在 FT-05 完成回滾後主動查詢刷新，或 FT-05 自行決定是否補發事件。

#### 5.4.7 `SetBankruptcyThreshold`（C-06 推送）

```
[C-06].SetBankruptcyThreshold(newThreshold)
  → _currentBankruptcyThreshold = newThreshold
  → EvaluateWarningState
        ├─ if (_currentGold >= 0) → ExitWarning, return
        ├─ if (_currentGold < newThreshold) → TriggerBankruptcy, return（防禦性，正常情境 C-06 只放寬不會觸發）
        └─ if (_warningState != Warning) → EnterWarning
            else → _warningDurationSec = LookupWarningDuration(_currentReputation)
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `SystemConstants.csv` | `GOLD_INITIAL`、`GOLD_MAX`、`REPUTATION_MIN`、`REPUTATION_MAX` | `【F-01-DS】system-constants.md` | `Awake.LoadConfig` 透過 `DataManager.Instance.GetInt(key)` 載入；對應內部 `_goldInitial` / `_goldMax` / `_reputationMin` / `_reputationMax` | F-03 `InitializeInstance` 內呼叫 `LoadConfig` |
| `BankruptcyThresholdTable.csv`（**Phase 2 deprecated**） | `reputationMin` / `reputationMax` / `warningDurationSec` | `【F-03-DS】bankruptcy-threshold-table.md` | 實作 `LookupWarningDuration(reputation)` 仍主動 `GetWhere<BankruptcyThresholdData>` 查詢，作為 `EnterWarning` 鎖定 `_warningDurationSec` 來源；GDD §3.5/§7.2 規範應改由 FT-07 推送 `_currentWarningDuration` | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 註冊；DataManager.Awake 載入；runtime 由 `EnterWarning` / `EvaluateWarningState` 觸發查詢 |

### 6.2 引用的 ScriptableObject

無。本系統完全以 CSV + `[Serializable] class` DTO 為資料載體。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `_goldInitial`（金幣初始值） | `SystemConstants.csv` → `GOLD_INITIAL` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_goldMax`（金幣上限） | `SystemConstants.csv` → `GOLD_MAX` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_reputationMin`（聲望下限） | `SystemConstants.csv` → `REPUTATION_MIN` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_reputationMax`（聲望上限） | `SystemConstants.csv` → `REPUTATION_MAX` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `_warningDurationSec`（破產警告期秒數）| **規範**：FT-07 透過 `SetBankruptcyWarningDuration` 推送（見 GDD §3.5）。**實作現況**：仍走 `BankruptcyThresholdTable.csv` → `warningDurationSec`，見 §8.3 條目 D1/D2 | 對應「四、程式實作原則」第 9 條：參數表格化（規範與實作均符合表格化要求，差別在來源 CSV）|

**允許的程式碼常數**（Fallback 值或語言/單位定義，非可調參數）：

| 常數 | 出現位置 | 性質 |
| --- | --- | --- |
| `DefaultGoldInitial = 100` | `LoadConfig` 失敗 fallback | DataManager 不可用時的安全網 |
| `DefaultGoldMax = 9_999_999` | 同上 | 同上 |
| `DefaultReputationMin = -100` | 同上 | 同上 |
| `DefaultReputationMax = 100` | 同上 | 同上 |
| `DefaultBankruptcyThreshold = -100` | `InitializeInstance` 設初值；`RestoreSnapshot` 偵測正值時 fallback | GDD §3.2 rule 6 明定「`Awake` 階段初始化為預設值 `-100`」 |
| `DefaultWarningDurationSec = 86400L` | `LookupWarningDuration` 各失敗分支 fallback | GDD §3.5 rule 9 明定預設 `86400` |

---

## 7. 邊緣案例對策（Edge Case Handling）

對齊 GDD §5.1 ~ §5.4：

### 7.1 金幣（GDD §5.1）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `AddGold(-500)` 使金幣低於破產門檻 | `target < _currentBankruptcyThreshold` → `return false`、金幣不變、不發事件、不變動狀態 | `ResourceManagement.AddGold` | EditMode：`_currentGold=100, threshold=-100`，`AddGold(-300)` 斷言回 false 且 `_currentGold==100` 且事件未發布 |
| `AddGold(0)` | `target == _currentGold` → `actualDelta == 0` → 直接 return true，不發事件 | `ResourceManagement.AddGold` | EditMode：呼叫 `AddGold(0)` 斷言事件廣播 0 次、回 true |
| `AddGold(9999999)` 超 `GOLD_MAX` | `target > _goldMax` → `target = _goldMax`；`actualDelta` 為實際增量 | `ResourceManagement.AddGold` | EditMode：AC-RM-02 |
| 傭金收入使負債金幣回正 | `UpdateBankruptcyWarning(prev<0, new>=0)` → `ExitWarning` 清計時器並廣播 | `ResourceManagement.UpdateBankruptcyWarning` / `ExitWarning` | EditMode：Warning 中 `AddGold(+大額)` 斷言狀態回 Normal、計時器歸 0 |

### 7.2 聲望（GDD §5.2）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `AddReputation(50)` 在 80 → 超 100 | clamp 至 `_reputationMax`，actualDelta=20，發 `OnReputationChangedEvent` | `ResourceManagement.AddReputation` | EditMode：AC-RM-04 |
| `AddReputation(0)` | `actualDelta == 0` → 不發事件；仍呼叫 `EvaluateWarningState`（防禦性重算）| `ResourceManagement.AddReputation` | EditMode：Warning 中 `AddReputation(0)` 斷言事件未發、狀態維持 Warning |
| 聲望已達 -100 再扣減 | clamp 至 `_reputationMin`，delta=0，不發事件 | `ResourceManagement.AddReputation` | EditMode：`_currentReputation=-100`，`AddReputation(-50)` 斷言事件未發 |
| 上游推送更寬鬆門檻（更負） | `_currentBankruptcyThreshold = newThreshold` → `EvaluateWarningState`：金幣仍在新門檻上方，維持原狀態（Warning 或 Normal）| `ResourceManagement.SetBankruptcyThreshold` / `EvaluateWarningState` | EditMode：AC-RM-10 |

### 7.3 初始化（GDD §5.3）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 首次啟動（無存檔） | `InitializeInstance` 設 `_currentGold=_goldInitial`、`_currentReputation=0`、`_warningState=Normal`、其他歸 0 | `ResourceManagement.InitializeInstance` | EditMode：呼叫 `InitializeForTests` 後斷言初值 |
| 載入存檔後金幣為負且 `_bankruptcyWarningStartTime` 存在 | `RestoreSnapshot` 寫回 6 欄位 + `EvaluateWarningState`：因金幣 < 0 → `EnterWarning`（若已 Warning 則維持並重新查 duration），符合「以存檔時間戳恢復 Warning」 | `ResourceManagement.RestoreSnapshot` | EditMode：snapshot 含 Warning 狀態，還原後斷言 `GetBankruptcyWarningState()==Warning` 且剩餘秒正確 |
| 載入存檔後金幣為正但保有 `_bankruptcyWarningStartTime` | `RestoreSnapshot` 寫回 → `EvaluateWarningState`：`_currentGold >= 0` → `ExitWarning`（清時間戳）| `ResourceManagement.RestoreSnapshot` / `EvaluateWarningState` | EditMode：snapshot 金幣為正但帶非零 startTime，還原後斷言 startTime 被清為 0 |

### 7.4 破產警告（GDD §5.4）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 金幣從正值降為負值 | `UpdateBankruptcyWarning(prev>=0, new<0)` → `EnterWarning`：鎖定 `_bankruptcyWarningStartTime = GetNowUtc()`、`_warningDurationSec = LookupWarningDuration(_currentReputation)` | `ResourceManagement.EnterWarning` | EditMode：AC-RM-05 |
| Warning 期間再 `AddGold(-10)`（仍負）| `UpdateBankruptcyWarning(prev<0, new<0)`：兩個條件分支皆 false → 不變動狀態、不重置計時器 | `ResourceManagement.UpdateBankruptcyWarning` | EditMode：AC-RM-08 |
| Warning 期間 `AddGold` 回正 | `UpdateBankruptcyWarning(Warning, new>=0)` → `ExitWarning` | `ResourceManagement.ExitWarning` | EditMode：AC-RM-07 |
| 多次正負振盪 | 每次 `EnterWarning` 重新 `GetNowUtc()` 並重新 `LookupWarningDuration`；`AC-RM-13` 涵蓋 | `ResourceManagement.EnterWarning` | EditMode：AC-RM-13 |
| Warning 期間聲望上升 | `AddReputation` → `EvaluateWarningState`：`_currentGold < 0` 且 Warning 仍存在 → `_warningDurationSec = LookupWarningDuration(_currentReputation)`（更新但不重置 startTime；剩餘秒會自動以新 duration 重算）| `ResourceManagement.EvaluateWarningState` | EditMode：Warning 中 `AddReputation(+50)`，斷言 `_warningDurationSec` 更新、`_bankruptcyWarningStartTime` 不變 |
| Warning 期間 C-06 推送更寬鬆門檻 | `SetBankruptcyThreshold` → `EvaluateWarningState`：金幣仍 < 新門檻則 Bankrupt、否則維持 Warning | `ResourceManagement.SetBankruptcyThreshold` | EditMode：AC-RM-10 |
| 離線期間倒數已到期且金幣負值 | `HandleOfflineResolved`：Warning 且 `_currentGold < 0` 且 `elapsed >= _warningDurationSec` → `TriggerBankruptcy` | `ResourceManagement.HandleOfflineResolved` | EditMode：AC-RM-11 |
| 離線期間金幣回正但倒數本已到期 | `HandleOfflineResolved`：第一個 if 塊不成立（`_currentGold >= 0`）→ `EvaluateWarningState`：`_currentGold >= 0` → `ExitWarning`（不觸發 Bankrupt）| `ResourceManagement.HandleOfflineResolved` / `EvaluateWarningState` | EditMode：AC-RM-12 |

### 7.5 額外覆蓋（既有實作補強）

| 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 重入：訂閱者在 callback 中再次呼叫資源 API | `TryEnterProcessing` 偵測 `_isProcessing == true` → `LogError` 並 reject（`AddGold` / `AddGoldAllowBankruptcy` 回 false；`AddReputation` 靜默 return）| `ResourceManagement.TryEnterProcessing` | EditMode：mock 訂閱者在 `OnGoldChangedEvent` callback 中呼叫 `AddGold`，斷言內層回 false 且 LogError |
| `RestoreSnapshot(null)` | `LogError("RestoreSnapshot 失敗：snapshot 為 null")` 並返回，狀態不變 | `ResourceManagement.RestoreSnapshot` | EditMode：傳 null 斷言 LogError、狀態不變 |
| Snapshot 含無效列舉值 | `Enum.IsDefined` 檢查失敗 → `LogWarning` 並 fallback `Normal` | `ResourceManagement.RestoreSnapshot` | EditMode：snapshot.WarningState 設為 (BankruptcyWarningState)99，還原後斷言為 Normal + LogWarning |
| Snapshot 含負時間戳 / 負持續秒 / 超 `_goldMax` 金幣 / 超範圍聲望 / 正值門檻 | 各自 `LogWarning` + clamp / fallback | `ResourceManagement.RestoreSnapshot` | EditMode：注入各項異常值，斷言正確 sanitize |
| `LookupWarningDuration` 查無對應聲望區間 | `LogError("找不到對應聲望區間設定")` → 回 `DefaultWarningDurationSec (86400)` | `ResourceManagement.LookupWarningDuration` | EditMode：CSV 無覆蓋當前聲望，斷言 `_warningDurationSec` 為 86400 + LogError |
| `LookupWarningDuration` 多區間重疊 | `LogWarning` 並採第一筆 | `ResourceManagement.LookupWarningDuration` | EditMode：CSV 注入 2 列覆蓋同聲望，斷言採第一列 + LogWarning |
| `DataManager.Instance == null` 時 `LookupWarningDuration` | `LogError` → 回 `DefaultWarningDurationSec` | `ResourceManagement.LookupWarningDuration` | EditMode：未初始化 DataManager 直接呼叫，斷言回 86400 |
| `AddGoldAllowBankruptcy` 大額負值溢位 | `target < int.MinValue` → `LogWarning` + 截斷至 `int.MinValue`；`target > int.MaxValue` → 截斷至 `int.MaxValue` | `ResourceManagement.AddGoldAllowBankruptcy` | EditMode：`_currentGold=int.MinValue+10` 時 `AddGoldAllowBankruptcy(-100)` 斷言不拋例外 + LogWarning |
| `TimeSystem.Instance == null` 取 NowUTC | `GetNowUtc` fallback `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` | `ResourceManagement.GetNowUtc` | EditMode：未初始化 TimeSystem，斷言 `GetNowUtc` 不拋例外、回值為合理 UTC 秒 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 資源種類（金幣/聲望初始值與範圍） | §5.1（API）、§5.3（欄位）、§5.4.1（初始化） | 對齊 | `_currentGold` 從 `_goldInitial` 初始化；`_currentReputation` 從 0 初始化；範圍以常數驅動 |
| §3.2 金幣規則 rule 1~4 | §5.1（`AddGold` / `AddGoldAllowBankruptcy`）、§5.4.2 | 對齊 | reject 邏輯、上限 clamp、負值不影響任務、`AddGold(0)` 不發事件皆對應 |
| §3.2 金幣規則 rule 5（`SetBankruptcyThreshold` 被動推送） | §5.1、§5.4.7 | 對齊 | F-03 不主動查詢 C-06；以寫入 API 接收推送 |
| §3.2 金幣規則 rule 6（預設 -100） | §5.4.1、§6.3 | 對齊 | `InitializeInstance` 內設 `DefaultBankruptcyThreshold = -100` |
| §3.2 金幣規則 rule 7（`AddGoldAllowBankruptcy` 語義 + 單次不穿越零線兩次） | §5.1、§5.4.3 | 對齊 | 公式照走 EvaluateWarningState；單次線性變化保證一次過渡 |
| §3.3 聲望規則 rule 1~3 | §5.1（`AddReputation`）、§5.4.4 | 對齊 | clamp、`AddReputation` 後執行 `EvaluateWarningState` |
| §3.3 聲望規則 rule 4（聲望變動觸發狀態校正） | §5.4.4 | 對齊 | 即使 `actualDelta == 0` 仍呼叫 `EvaluateWarningState` |
| §3.4 事件廣播 rule 1~4 | §5.2、§5.4 各分支 | 對齊 | 三個事件齊全；任何系統不得跳過 API（封裝）|
| §3.4 事件廣播 rule 5（`_isProcessing` 重入防護） | §5.4.2 / §5.4.3 / §5.4.4、§7.5 | 對齊 | `TryEnterProcessing` / `ExitProcessing` 統一管理 |
| §3.4 事件廣播 rule 6（`OnEnable` / `OnDisable` 訂閱） | §5.4.1 | 對齊 | `SubscribeEvents` / `UnsubscribeEvents` 對稱 |
| §3.4 事件廣播 rule 7（Script Execution Order） | §5.4.1（註冊） | 對齊 | 採 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 註冊機制 + DataManager.Awake 早於 F-03 Awake；無需 Project Settings 設定（與 F-01 FSD §8.3 條目 9 同源）|
| §3.5 查詢與寫入 API rule 1~7 | §5.1 公開 API 表 | 對齊 | `GetGold` / `GetReputation` / `CanAfford` / `GetCurrentBankruptcyThreshold` / `GetBankruptcyWarningState` / `GetBankruptcyWarningRemainingSeconds` / `SetBankruptcyThreshold` 完整實作 |
| §3.5 查詢與寫入 API rule 8~9（`SetBankruptcyWarningDuration` / `GetBankruptcyWarningDuration`） | §5.1（缺失註）、§8.3 條目 D1 | **未對齊** | 實作完全缺失此兩 API 與 `_currentWarningDuration` 欄位 |
| §3.6 破產警告狀態定義 | §5.3 | 對齊 | 三個狀態定義、條件對應 |
| §3.6 規則 1~5（進出 Warning、Tick 倒數）| §5.4.2、§5.4.5 | 對齊 | 含 Warning 不重置計時器、Tick 觸發 Bankrupt |
| §3.6 規則 6（`SetBankruptcyThreshold` / `SetBankruptcyWarningDuration` 各自處理）| §5.4.7、§8.3 條目 D1 | **部分對齊** | `SetBankruptcyThreshold` 部分對齊；`SetBankruptcyWarningDuration` 缺失 |
| §3.6 規則 7（離線判定）| §5.4.5 | 對齊 | `HandleOfflineResolved` 涵蓋 |
| §3.6 規則 8（Bankrupt 不自行懲罰）| §2.4（FT-06 訂閱） | 對齊 | F-03 僅廣播事件 |
| §3.6 規則 9（`ResetBankruptcyState` 保留 API）| §5.1 | 對齊 | 保留 `ExitWarning` 入口 |
| §3.6 規則 10（`EvaluateWarningState` 統一校正入口）| §5.4.3 / §5.4.4 / §5.4.5 / §5.4.6 / §5.4.7 | 對齊 | 4 個觸發點皆走 `EvaluateWarningState` |

### 8.2 公式對齊或替代說明

- **§4.1 `AddGold`**：實作對齊偽碼；`actualDelta == 0` 跳過廣播一致；用 `(long)` 中間值防溢位一致。
- **§4.1a `AddGoldAllowBankruptcy`**：實作額外加入 `int.MinValue` / `int.MaxValue` 雙向截斷與 `LogWarning`，為實作補強，行為等價（GDD 假設 `int` 加減不溢位，實作為更穩健）。
- **§4.2 `AddReputation`**：實作對齊偽碼；clamp + `EvaluateWarningState` 一致。
- **§4.3 `CanAfford`**：實作多一條 `amount <= 0` 短路返回 `true`，行為等價（負/零支出永遠可承受）；GDD 公式 `currentGold - amount >= threshold` 在 `amount == 0` 時亦回 true、在 `amount < 0` 時 `currentGold + |amount| >= threshold` 恒成立（threshold ≤ 0），與短路結果一致。見 §8.3 條目 D6。
- **§4.4 聲望標籤查詢**：GDD 已宣告為「參考，非 RM 內部邏輯」；實作未實作（屬 P-02 UI 範疇）。
- **§4.5 破產警告狀態機**：實作以 `EnterWarning` / `ExitWarning` / `TriggerBankruptcy` / `EvaluateWarningState` / `UpdateBankruptcyWarning` 5 個方法分散偽碼中的單一 `UpdateBankruptcyWarning` 函式，行為與 GDD 偽碼等價且更具防呆（`EnterWarning` / `ExitWarning` / `TriggerBankruptcy` 各自冪等保護）。
- **§4.6 `SetBankruptcyThreshold` / `SetBankruptcyWarningDuration` / `GetBankruptcyWarningDuration`**：`SetBankruptcyThreshold` 對齊；後兩者實作缺失（見 §8.3 條目 D1）。

### 8.3 未能實現的規則與修改建議（含逆向發現的偏差）

| 序 | 規則 / 偏差 | 實作現況 | 修改建議 |
| --- | --- | --- | --- |
| D1 | GDD §3.5 rule 8/9 規範 `SetBankruptcyWarningDuration(int)` 與 `GetBankruptcyWarningDuration()`，並要求新增 `_currentWarningDuration` 欄位（預設 86400）；FT-07 應於啟動／預備金保險櫃升級時主動推送 | **已落地（2026-04-26 Codex Medium 工項）**：補上 `SetBankruptcyWarningDuration(int newDurationSec)`（≤ 0 LogError 不更新）/ `GetBankruptcyWarningDuration() : long` API；新增 `_currentWarningDuration` 欄位（field initializer = `DefaultWarningDurationSec` = 86400L）；`EnterWarning` 改 `_warningDurationSec = _currentWarningDuration`；`InitializeInstance` 重設為預設值；`ResourceSnapshot` 新增 `CurrentWarningDuration` 欄位；`CreateSnapshot` / `RestoreSnapshot` 同步序列化／還原（含 `<= 0` sanitize fallback）。EditMode 測試已補上 6 個對應案例 | 已完成；無後續行動 |
| D2 | GDD §3.5/§3.6/§7.2 規範 `BankruptcyThresholdTable.csv` 已 deprecated，F-03 不應主動查詢；DataSpec 標註該表 runtime 已移除 | **已落地（2026-04-26 Codex Medium 工項）**：移除 `LookupWarningDuration` 方法整個刪除；移除 `RegisterTable<BankruptcyThresholdData>` 註冊（整個 `RegisterTables` 方法刪除）；刪除 `BankruptcyThresholdData.cs` 與 `.meta`；DataSpec `【F-03-DS】bankruptcy-threshold-table.md` 補上 Phase 2 落地紀錄；測試檔同步移除舊表註冊與聲望區間查詢測試 | 已完成；無後續行動 |
| D3 | GDD §3.4 規範事件 `OnBankruptcyWarningStateChanged(BankruptcyWarningState newState)` 僅含新狀態 | 實作 `OnBankruptcyStateChangedEvent(BankruptcyWarningState previousState, BankruptcyWarningState currentState)` 含前後雙欄位 | **回註 GDD**：實作為 superset（含 GDD 規範的「新狀態」+ 額外的「前狀態」）；建議於 GDD §3.4 加 FSD 回註：「實作 payload 含 `PreviousState` 與 `CurrentState` 雙欄位，便於下游區分『何種轉移』（如 Warning→Normal 自然恢復 vs Bankrupt→Normal 重置）。」 |
| D4 | GDD §6.4 規範 ISaveable 契約：`OwnerKey="f03Resources"` / `IsCritical=true` / `Serialize()` / `RestoreFromSave(string ownerJson)` / `InitializeAsNewGame()` 序列化 7 欄位 | **未實作 ISaveable**：以 `CreateSnapshot` / `RestoreSnapshot` (`ResourceSnapshot` DTO 含 6 欄位) 替代；缺失 `_currentWarningDuration` 欄位（與 D1 同源）；`InitializeAsNewGame` 對應 `InitializeInstance`，但未走 ISaveable 介面 | **必修**：實作 `ISaveable` 介面（待 FT-10 FSD 撰寫時對齊介面定義）；目前 `CreateSnapshot` / `RestoreSnapshot` 暫供 FT-05 預收回滾用，可保留為內部 API。FT-10 FSD 應規範 `Serialize() : string` 內部呼叫 `CreateSnapshot` 並序列化為 JSON、`RestoreFromSave(json)` 反序列化為 `ResourceSnapshot` 並呼叫 `RestoreSnapshot`，使兩條路徑共用同一份 sanitize 邏輯 |
| D5 | GDD §3.4 rule 6 規範訂閱 `TimeSystem.OnSecondTick` / `TimeSystem.OnOfflineResolved` | 實作訂閱 `OnSecondTickEvent` / `OnOfflineResolvedEvent`（透過 `EventBus.Subscribe`），對齊 F-02 FSD §5.2 事件清單 | 對齊，無修改建議 |
| D6 | GDD §4.3 公式：`return currentGold - amount >= GetCurrentBankruptcyThreshold()` | 實作多一條 `if (amount <= 0) return true` 短路 | **回註 GDD**：建議於 §4.3 補一句「實作可加 `amount <= 0` 短路返回 true（語義等價）」，或修改實作移除短路以對齊公式（建議前者，行為等價且短路效率較高）|
| D7 | GDD §4.5 偽碼：`EvaluateWarningState` 在「`currentGold < 0` 且 `_warningState != Normal`」分支只設 `EnterWarning`（不再操作 `_warningDurationSec`） | **已落地（2026-04-26 Codex Medium 工項）**：`EvaluateWarningState` 的 `else { _warningDurationSec = LookupWarningDuration(...); }` 分支整個移除；Warning 維持狀態下 `_warningDurationSec` 鎖定不變，符合 GDD §3.6 rule 6 精神 | 已完成；無後續行動 |
| D8 | `ResourceManagement.cs` 636 行已超 FSD-index §2.4 拆分門檻 500 行 | 採方向 C（保留現況單檔；§4.2 已說明拆分理由） | **建議**：在類別內以 `#region` 分 5 區（公開查詢 API / 公開寫入 API / Snapshot / 破產狀態機 / 生命週期與事件處理），檔案行數不變但改善審查效率；若未來新增 D1/D2/D4 對應 API（`SetBankruptcyWarningDuration` + ISaveable 實作 ≈ +60~80 行），再次評估是否需拆 |

> **總結**：
> - **必修 4 項**：D1（補 `SetBankruptcyWarningDuration` API）、D2（移除 deprecated 表查詢）、D4（實作 ISaveable）、D7（與 D1 連動移除 `LookupWarningDuration` 重新鎖定分支）。建議派 Codex 一個工項一次處理 D1+D2+D7（強耦合，必須同步），D4 可獨立工項並待 FT-10 FSD 對齊介面後處理。
> - **建議 FSD 回註 GDD 2 項**：D3（事件 payload superset）、D6（`CanAfford` 短路）。
> - **內部分區建議 1 項**：D8（5 region 切分）。
> - **無偏差但需註記 1 項**：D5（事件名稱對齊 F-02 FSD）。

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| 2026-04-26 | `【F-03】resource-management.md` | §3.4 末尾（rule 3 對應） | 對應 §8.3 條目 D3：實作 `OnBankruptcyStateChangedEvent` 含 `PreviousState` / `CurrentState` 雙欄位（GDD 規範僅 `newState`），屬向後兼容 superset；下游可區分轉移類型 |
| 2026-04-26 | `【F-03】resource-management.md` | §4.3 末尾（`CanAfford` 公式對應） | 對應 §8.3 條目 D6：實作於本公式之前加 `if (amount <= 0) return true` 短路，語義等價且提升熱路徑效率與防溢位 |
| 2026-04-26 | `【F-03-DS】bankruptcy-threshold-table.md` | 頂部 deprecated 區塊末段 | 補上 Phase 2 落地紀錄：`RegisterTables` / `LookupWarningDuration` / `BankruptcyThresholdData.cs` 已移除；DataSpec 保留為設計歷史參考 |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| 無（GDD 內部與跨 GDD 皆無衝突）| — | — | — |

> 本次逆向 FSD 撰寫過程未發現「GDD 內部矛盾」或「跨 GDD 衝突」需使用者裁決。§8.3 條目 D1/D2/D4 屬「實作未跟上 GDD 規範」，非 GDD 衝突。

### 8.6 重大實作變更紀錄（補充章節）

| 日期 | 變更摘要 | 涉及檔案 | 執行者 |
| --- | --- | --- | --- |
| 2026-04-26 | Codex Medium 工項落地 D1 + D2 + D7（強耦合一次處理）：補 `SetBankruptcyWarningDuration` API、移除 deprecated `BankruptcyThresholdTable` 查詢、移除 `EvaluateWarningState` Warning 維持分支重新鎖定。Unity Editor 編譯通過 | `ResourceManagement.cs` (+34/-47)、`ResourceSnapshot.cs` (+4/-0)、`BankruptcyThresholdData.cs` 與 `.meta` 刪除、`ResourceManagementTests.cs` (+62/-42)、`ResourceManagementPlayModeTests.cs` (+1/-13) | Codex 實作；Claude Code 主體（Opus + xhigh）審查 |

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊填妥（含對應 GDD 版本、雙份 Data-Specs、撰寫者／Review 者／狀態／日期）
- [x] §1.3 完成目標可被測試驗證（13 條 EditMode/PlayMode 可驗證）
- [x] §2.1~§2.5 四向皆列舉（GDD 章節 / Data-Specs / 上游 / 下游 / 事件契約）
- [x] §3.3 對映表覆蓋所有幻想／目的（8 列）
- [x] §4 拆分判斷有結論（方向 C 保留現況 + §8.3 條目 D8 內部分區建議）；Script 清單欄位齊全（5 個 Script）
- [x] §5 API/事件/資料結構/資料流齊備（API 14 條、事件 5 條、7 個資料結構、7 個資料流分支）
- [x] §6 CSV 引用含對應 Data-Specs（雙份）；§6.3 嚴禁寫死清單對齊原則第 9 條
- [x] §7 邊緣案例皆有對策（GDD §5.1~§5.4 全 11 條 + 8 條額外覆蓋）
- [x] §8.1 對齊清單覆蓋 GDD §3 二層粒度（§3.1~§3.6 全列；標出 D1 未對齊與 D6 部分對齊）
- [x] §8.2~§8.5 如實登記（§8.5 衝突無；§8.4 為待執行回註清單）
- [x] FSD-index §6.1 / §7.1 / §7.2 已同步更新（撰寫者於本 FSD 寫入後執行）

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-26 | unity-specialist subagent | 通過 | 通過 | 通過（含 8 項偏差登記） | 逆向 FSD：8 項偏差列入 §8.3，含 4 項必修（D1/D2/D4/D7）、2 項建議 FSD 回註（D3/D6）、1 項內部分區建議（D8）、1 項對齊註記（D5）；建議派 Codex 一個工項處理 D1+D2+D7（強耦合），D4 獨立工項並待 FT-10 FSD 對齊介面後處理 |
| 2026-04-26 | Claude Code 主體（D1+D2+D7 落地 patch + D3/D6 GDD 回註 patch） | 通過 | 通過 | 通過 | Codex Medium 工項落地（read-only 規劃 → workspace-write 實作 → Opus + xhigh 審查通過）；F-03 GDD §3.4 / §4.3 加 D3/D6 FSD 回註；DataSpec 補 Phase 2 落地紀錄；§8.3 D1/D2/D7 標已落地；§8.4 補 3 筆回註紀錄；§8.6 新增重大實作變更紀錄；狀態轉「已完成」（D4 待 FT-10 FSD；D8 視 D1/D4 落地後再評估） |
