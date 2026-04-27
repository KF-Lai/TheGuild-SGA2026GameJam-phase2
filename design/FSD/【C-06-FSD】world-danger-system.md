# 【C-06-FSD】功能規格說明書 — World Danger System

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【C-06】world-danger-system.md`（版本：2026-04-20） |
| 對應 Data-Specs | `【C-06-DS】world-danger-table.md`（CSV: `WorldDangerTable.csv`，單表整合升級閘 / 任務池權重 / 債務上限） |
| 撰寫者 | Claude Code 主體（直接撰寫，無 subagent） |
| Review 者 | Claude Code 主體（自檢） |
| 狀態 | 審查中 |
| 最近更新 | 2026-04-27 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

C-06 World Danger System 管理單向遞增的全域世界危險度（5 階：E→D→C→B→A）。每階升級需同時滿足三閘：時間閘（遊戲天數）、進度閘（累積接受任務數）、陣營閘（任意陣營分數）。系統載入 `WorldDangerTable.csv` 並維護 4 個 runtime 狀態欄位；於啟動與升階時主動推送 `maxDebt` 至 F-03 Resource Management 作為金幣硬性下限；提供 `GetPoolWeights()` 給 FT-02 委託生成；訂閱 F-02 每日重置觸發升階檢查；接收 FT-09 陣營分數推送觸發升階重檢。本系統屬 Core 層，依賴 Foundation（F-01 / F-02），對 F-03 採主動推送（Core → Foundation 單向），無循環依賴。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**

- `WorldDangerTable.csv` 載入、欄位驗證、缺漏 fallback
- 升階三閘判定（`elapsedDays` / `_acceptedMissionCount` / `_cachedMaxFactionScore`）
- 遞迴連跳階（`CheckLevelUp` 自我呼叫）
- 啟動 + 升階推送 `F03.SetBankruptcyThreshold(maxDebt)`
- `OnDangerLevelChanged` 事件發布
- 接收 API：`OnMissionAccepted(difficulty)` / `OnFactionScoreUpdated(score)` / `CheckLevelUp()`
- 查詢 API：`GetCurrentLevel` / `GetDangerData` / `GetMaxDebt` / `GetPoolWeights`
- ISaveable 契約（OwnerKey: `c06WorldDanger`，Degradable）

**Out-of-Scope**

- `gameStartTimestamp` 寫入（FT-10 Save/Load `InitializeAsNewGame` 負責）
- 任務池實際生成（FT-02 §3.9.2 消費 `GetPoolWeights()` 自行抽取）
- 陣營分數計算與推送觸發（FT-09 負責；C-06 僅快取 `newMaxScore`）
- 危險度倒退（單向遞增，無此功能）
- F-03 內部破產警告狀態機（C-06 僅推送 `maxDebt`，不參與 Warning / Bankrupt 判定）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 AC-WD-01~16，補充程式可驗證條件：

| DoD ID | 驗收條件 | 驗證方式 |
| --- | --- | --- |
| DoD-01 | 啟動後 `GetCurrentLevel()` 回傳 `"E"`（AC-WD-01） | EditMode test |
| DoD-02 | `GetDangerData("A").name == "末世"`（AC-WD-02） | EditMode test |
| DoD-03 | `GetMaxDebt()` 在 E 階回傳 `-100`、升至 D 後回傳 `-500`（AC-WD-03） | EditMode test |
| DoD-04 | `GetPoolWeights()` 在 E 階 `weightF_E = 40`、`weightS_SSS = 0`（AC-WD-04） | EditMode test |
| DoD-05 | `Start()` 完成後 F-03 `GetCurrentBankruptcyThreshold()` 回傳 `-100`（AC-WD-15） | Integration test：mock IResourceService 攔截 `SetBankruptcyThreshold` |
| DoD-06 | 三閘全達標時 `CheckLevelUp` 升階；任一未達標維持原階（AC-WD-05/06/07） | EditMode test 三組（time-fail / mission-fail / faction-fail） |
| DoD-07 | `OnFactionScoreUpdated(4)` 在 `factionScoreReq=5` 時不升階；`OnFactionScoreUpdated(5)` 立即升階（AC-WD-08） | EditMode test |
| DoD-08 | `OnMissionAccepted("F")` 在下一階 `minDifficulty="D"` 時 count 不增；`OnMissionAccepted("D")` count 增 1（AC-WD-09/10） | EditMode test |
| DoD-09 | 遞迴升階：滿足 D→C 但 C→B 缺進度，`CheckLevelUp` 升至 C 後停止；`acceptedMissionCount` 重置為 0（AC-WD-11） | EditMode test |
| DoD-10 | 離線跨多階：模擬 15 天 + 足夠任務 + 陣營分數，`CheckLevelUp` 補算到 A（AC-WD-12） | EditMode test |
| DoD-11 | A 階呼叫 `OnMissionAccepted` / `CheckLevelUp` no-op 不報錯（AC-WD-13） | EditMode test |
| DoD-12 | `OnDangerLevelChanged` 每次升階觸發 1 次帶正確 `dangerLevel`（AC-WD-14） | EditMode test：訂閱事件計次 |
| DoD-13 | 升階後 F-03 `GetCurrentBankruptcyThreshold()` 回傳新階 `maxDebt`（AC-WD-16） | Integration test |
| DoD-14 | CSV 載入零錯誤；缺欄位 / 全 0 weights / 缺 maxDebt 走 fallback（§5.1） | EditMode test |
| DoD-15 | ISaveable 序列化 4 欄位、RestoreFromSave 還原後立即推送 `SetBankruptcyThreshold(GetMaxDebt())` | Integration test |

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

- §1 概要：分層原則（Core → Foundation 主動推送）、5 階梯起源
- §2 玩家幻想：危險度單向遞增、末世為終極考驗
- §3.1 WorldDangerTable schema（13 欄位 × 5 行）
- §3.2 Runtime 狀態（4 欄位）
- §3.3 升級規則（7 條 rule）
- §3.4 查詢 API（7 個）
- §4.1 GetElapsedDays
- §4.2 CheckLevelUp（含遞迴）
- §4.3 啟動推送
- §4.4 OnMissionAccepted 計數
- §4.5 OnFactionScoreUpdated
- §5.1 資料載入邊緣案例（5 條）
- §5.2 Runtime 操作邊緣案例（5 條）
- §6 依賴關係 + §6.4 ISaveable 契約
- §7 可調參數（三組調整原則）
- §8 驗收標準（16 條 AC-WD-01~16）

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| `【C-06-DS】world-danger-table.md` | `WorldDangerTable.csv` | `dangerLevel`, `name`, `timeThreshold`, `missionCountReq`, `minDifficulty`, `factionScoreReq`, `weightF_E`, `weightD`, `weightC`, `weightB`, `weightA`, `weightS_SSS`, `maxDebt`（13 欄位全部） | 載入靜態危險度資料；升階閘 + 任務池權重 + 債務上限三組欄位整合查詢 |

### 2.3 上游依賴系統

| 系統 | 依賴內容 |
| --- | --- |
| F-01 DataManager | `GetTable<WorldDangerData>()` 載入 `WorldDangerTable.csv` |
| F-02 Time System | `NowUTC` 計算 `elapsedDays`；訂閱 `OnDailyReset` 觸發 `CheckLevelUp` |

### 2.4 下游被依賴系統

| 系統 | 依賴的 API / 事件 |
| --- | --- |
| F-03 Resource Management | 啟動 + 升階時主動呼叫 `F03.SetBankruptcyThreshold(int)`（C-06 → F-03 單向推送，F-03 被動接收） |
| FT-02 Mission Dispatch | 查詢 `GetPoolWeights()`（§3.9.2 委託生成抽 difficulty bucket）；發布事件 `OnMissionAccepted(difficulty)` 推送計數（FT-02 §3.5 step 9） |
| FT-09 Faction Story | 推送 `OnFactionScoreUpdated(int newMaxScore)`（FT-09 在任意陣營分數更新時呼叫，C-06 快取最新最高分並立即 `CheckLevelUp`） |
| P-02 Main UI | 訂閱 `OnDangerLevelChanged` 即時更新 UI；呼叫 `GetCurrentLevel` / `GetDangerData` 顯示當前階別與描述 |
| FT-10 Save/Load | 透過 ISaveable 序列化 4 欄位；`InitializeAsNewGame` 寫入 `_gameStartTimestamp = NowUTC` |

### 2.5 跨系統事件契約

| 事件 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnDangerLevelChanged` | Out（C-06 → *） | `(string newDangerLevel)` | `CheckLevelUp` 升階成功後立即發布；P-02 訂閱更新 UI；FT-02 訂閱觸發 §3.9.2 「危險度升階補池」流程 |
| `OnDailyReset` | In（F-02 → C-06） | `(long todayUtcTimestamp)` | F-02 每日 00:00 發布；C-06 訂閱呼叫 `CheckLevelUp` 嘗試升階 |

> C-06 不訂閱其他事件。`OnMissionAccepted(difficulty)` 與 `OnFactionScoreUpdated(score)` 為**直接 API 呼叫**而非事件（FT-02 / FT-09 直接呼叫 C-06，符合 GDD §3.4 設計）；不走 EventBus，避免不必要的訂閱 / 發布開銷。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

世界危險度是公會長感受到的「時代壓力」。和平年代被時間悄悄推向動盪、暗湧、危局，最終是末世。玩家不直接操控，但會在委託板看到任務難度分布變化、F-03 容許更深的負債（更多錯誤空間），感受世界自身的演進節奏。末世是終極考驗，撐下去的公會即傳奇。

### 3.2 系統目的還原

提供單一真相來源：當前危險度與其決定的 `maxDebt` / 任務池權重 / 三閘進度。資料驅動：所有閾值集中於 `WorldDangerTable.csv`，設計師調表即改節奏。架構分層：Core 層主動推送至 Foundation 層，Foundation 不反向依賴。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 時代壓力悄悄逼近 | 委託板逐漸出現高難度任務 | 升階時發布 `OnDangerLevelChanged`，FT-02 §3.9.2 「危險度升階補池」依新 `GetPoolWeights()` 重抽 |
| 危險度允許更深負債 | 高階公會的金幣硬性下限拉得更深 | 升階時呼叫 `F03.SetBankruptcyThreshold(GetMaxDebt())` 推送 |
| 玩家無法控制世界節奏 | 危險度永不倒退 | 偽碼無 down-grade 分支；不暴露任何「降階」API |
| 末世是終極考驗 | A 階出現後玩家面對最高難度池 | A 階 `weightF_E = 0`（強制高難度）+ `maxDebt = -5000`（最深容錯） |
| 設計師改 CSV 即改節奏 | 重啟後三閘閾值與池權重立刻反映新 CSV | 全部資料來自 `WorldDangerTable.csv`；無寫死值 |
| 跨度多日離線仍正確 | 玩家離線數天後回來，危險度補算到對應階 | `CheckLevelUp` 內遞迴；`elapsedDays` 用絕對 UTC 時間差（非每秒累加） |
| 陣營勢力影響世界 | 累積特定陣營高分後解鎖更深的危險度 | C 階起 `factionScoreReq > 0`；FT-09 推送 `OnFactionScoreUpdated` 觸發 `CheckLevelUp`（觀察者模式，C-06 不查 FT-09） |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。

### 4.2 拆分理由

不拆分 FSD。預估 5 個 Script 合計約 410~530 行，逼近但未顯著超出 500 行門檻；單一 Script 中 `WorldDangerService` 預估 200~270 行，遠低於拆分臨界。Service 持有 runtime 狀態（4 欄位）+ 升階遞迴邏輯 + F-03 推送 + 事件發布 + ISaveable，緊密耦合於「升階為單一原子操作」的設計（升階成功須**同時**重置 `acceptedMissionCount`、推送新 `maxDebt`、發布 `OnDangerLevelChanged`、遞迴重檢——任一步驟單獨拆出會破壞原子性保證）。GDD §3 / §4 內部無明顯職責分區（升階三閘共用 `CheckLevelUp` 流程）。

### 4.3 拆分結果

不適用（未拆分）。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `WorldDangerData` | `Assets/Scripts/Gameplay/WorldDanger/WorldDangerData.cs` | 資料類別：對應 `WorldDangerTable.csv` 一行的 13 欄位 | 無（純資料） | `< 40 行` |
| `MissionPoolWeights` | `Assets/Scripts/Gameplay/WorldDanger/MissionPoolWeights.cs` | DTO：6 個難度 bucket 權重的不可變值類型，由 `GetPoolWeights()` 回傳 | 無（純資料） | `< 30 行` |
| `IWorldDangerService` | `Assets/Scripts/Gameplay/WorldDanger/IWorldDangerService.cs` | 介面：定義對外 7 個 API + 事件契約，供 mock 測試與 DI 使用 | 無 | `< 50 行` |
| `WorldDangerLoader` | `Assets/Scripts/Gameplay/WorldDanger/WorldDangerLoader.cs` | 載入 CSV、執行 §5.1 欄位驗證與大小寫正規化、產出 `Dictionary<string, WorldDangerData>` | `IDataManager` | `100~140 行` |
| `WorldDangerService` | `Assets/Scripts/Gameplay/WorldDanger/WorldDangerService.cs` | MonoBehaviour：持有 4 個 runtime 狀態、實作 7 個 API、執行 `CheckLevelUp` 遞迴、Awake/Start 推送、訂閱 `OnDailyReset`、實作 `ISaveable` | `WorldDangerLoader`（組合）、`ITimeService`、`IResourceService`、`EventBus` | `200~270 行` |

> **路徑慣例**：以 Unity Asset Database 為準，從 `Assets/...` 起算（FSD-index §5.4）。

### 4.5 類別關係

```
WorldDangerService（MonoBehaviour）
  ├─ 組合 WorldDangerLoader（Awake 期完成載入，Service 持有結果字典）
  ├─ → ITimeService（NowUTC、訂閱 OnDailyReset）
  ├─ → IResourceService（呼叫 SetBankruptcyThreshold；C-06 → F-03 單向推送）
  ├─ → EventBus（發布 OnDangerLevelChanged）
  └─ 實作 IWorldDangerService 與 ISaveable

WorldDangerLoader → IDataManager
GetPoolWeights() 回傳 MissionPoolWeights（值類型 / 不可變 DTO）

外部系統只依賴 IWorldDangerService 介面
```

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

定義於 `IWorldDangerService`（對外唯一入口）。

| 方法簽名 | 說明 | 錯誤行為 |
| --- | --- | --- |
| `string GetCurrentLevel()` | 回傳當前 `dangerLevel`（`E` / `D` / `C` / `B` / `A`） | 永不回 null（初始為 `"E"`） |
| `WorldDangerData GetDangerData(string dangerLevel)` | 取指定階資料 | 找不到回 `null`，`Debug.LogWarning` |
| `int GetMaxDebt()` | 當前階 `maxDebt`（負整數） | 當前階字典缺漏或 `maxDebt = 0` 時 fallback `-100`，`Debug.LogError`（§5.1 對策） |
| `MissionPoolWeights GetPoolWeights()` | 當前階任務池權重 6 欄位 DTO | 當前階字典缺漏或 weights 全 0 時 fallback E 階權重，`Debug.LogError` |
| `void OnMissionAccepted(string difficulty)` | FT-02 §3.5 step 9 呼叫；難度達下一階 `minDifficulty` 則 `_acceptedMissionCount++` 後觸發 `CheckLevelUp` | A 階直接 return；難度未達 minDifficulty 不計數，不報錯 |
| `void OnFactionScoreUpdated(int newMaxScore)` | FT-09 呼叫；更新 `_cachedMaxFactionScore` 後立即觸發 `CheckLevelUp` | A 階直接 return |
| `void CheckLevelUp()` | F-02 `OnDailyReset` 訂閱 callback；亦由 `OnMissionAccepted` / `OnFactionScoreUpdated` 內部觸發；A 階 no-op；遞迴自我呼叫直到無法升階 | 無錯誤路徑；缺漏階透過 fallback 跳過 |

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnDangerLevelChanged` | Out（C-06 → *） | `(string newDangerLevel)` | `CheckLevelUp` 升階成功後立即發布；P-02 訂閱更新 UI；FT-02 訂閱觸發 §3.9.2 危險度升階補池 |
| `OnDailyReset` | In（F-02 → C-06） | `(long todayUtcTimestamp)` | F-02 每日 00:00 發布；C-06 在 `OnEnable` 訂閱、`OnDisable` 取消訂閱；訂閱 callback 直接呼叫 `CheckLevelUp` |

> `OnMissionAccepted` 與 `OnFactionScoreUpdated` 為直接 API 呼叫（非 EventBus 事件），不在此表登記。

### 5.3 資料結構

```csharp
// WorldDangerData — 對應 WorldDangerTable.csv 一行
public class WorldDangerData
{
    public string dangerLevel;       // E / D / C / B / A
    public string name;              // 顯示名稱（和平 / 動盪 / 暗湧 / 危局 / 末世）
    public int    timeThreshold;     // 時間閘：升至此階所需最低遊戲天數
    public int    missionCountReq;   // 進度閘：累積接受任務數門檻
    public string minDifficulty;     // 進度閘：計入累計的最低任務難度
    public int    factionScoreReq;   // 陣營閘：任意陣營分數需達到此值；0 = 不設此閘
    public int    weightF_E;         // 任務池 F~E 合計權重
    public int    weightD;
    public int    weightC;
    public int    weightB;
    public int    weightA;
    public int    weightS_SSS;       // S~SSS 合計權重
    public int    maxDebt;           // 負值；金幣硬性下限
}

// MissionPoolWeights — 由 GetPoolWeights() 回傳（值類型）
public readonly struct MissionPoolWeights
{
    public readonly int weightF_E;
    public readonly int weightD;
    public readonly int weightC;
    public readonly int weightB;
    public readonly int weightA;
    public readonly int weightS_SSS;
    // 建構子省略
}

// 內部 runtime 狀態（WorldDangerService 私有欄位）
private string _currentDangerLevel;       // Awake 設 "E"；ISaveable RestoreFromSave 還原
private int    _acceptedMissionCount;     // 升階時重置為 0
private long   _gameStartTimestamp;       // FT-10 InitializeAsNewGame 寫入；ISaveable 還原
private int    _cachedMaxFactionScore;    // FT-09 推送

// 內部固定難度索引（GDD §3.3 rule 4）
private static readonly Dictionary<string, int> DifficultyIndex = new()
{
    ["F"] = 0, ["E"] = 1, ["D"] = 2, ["C"] = 3, ["B"] = 4,
    ["A"] = 5, ["S"] = 6, ["SS"] = 7, ["SSS"] = 8,
};

// 階梯順序（GDD §3.1）
private static readonly string[] DangerLevelOrder = { "E", "D", "C", "B", "A" };
```

### 5.4 內部資料流

**觸發點 1：Awake — 載入與初始化**

```
WorldDangerService.Awake()
  → WorldDangerLoader.LoadAll()
      ├─ IDataManager.GetTable<WorldDangerData>() → entries
      ├─ foreach entry:
      │     ├─ entry.dangerLevel = entry.dangerLevel.ToUpperInvariant()  // §5.1 row 5：大小寫正規化
      │     ├─ if entry.dangerLevel ∉ {E,D,C,B,A} → LogError + skip
      │     ├─ if entry.minDifficulty ∉ {F,E,D,C,B,A,S,SS,SSS} → LogError + 寫入 "F"（§5.1 row 4）
      │     ├─ if (weightF_E + weightD + ... + weightS_SSS == 0) → LogError（§5.1 row 2 標記）
      │     └─ if entry.maxDebt == 0 → LogError（§5.1 row 3 標記）
      └─ 產出 _dangerDict: Dictionary<string, WorldDangerData>
  → _currentDangerLevel = "E"
  → _acceptedMissionCount = 0
  → _cachedMaxFactionScore = 0
  // _gameStartTimestamp 由 FT-10 在 Bootstrap 期透過 InitializeAsNewGame 或 RestoreFromSave 設定
```

**觸發點 2：Start — 推送初始 maxDebt 至 F-03**

```
WorldDangerService.Start()
  → IResourceService.SetBankruptcyThreshold(GetMaxDebt())
      // F-03 已在自身 Awake 設 -100 預設值；C-06 在 Start 推送覆寫為 _currentDangerLevel 對應 maxDebt
      // 若 FT-10 已在 Awake 後 Start 前還原 _currentDangerLevel 非 "E"，此推送會帶入正確的階別 maxDebt
  → IEventBus.Subscribe<OnDailyResetEvent>(HandleDailyReset)
```

> **Script Execution Order 要求**：F-03.Start < C-06.Start。實作方式：`WorldDangerService` 標註 `[DefaultExecutionOrder(100)]`，`ResourceManagement` 標註 `[DefaultExecutionOrder(50)]`（程式級保證，無需 Unity Editor 設定，符合 §8.3 B-01）。

**觸發點 3：FT-02 呼叫 `OnMissionAccepted(difficulty)`**

```
WorldDangerService.OnMissionAccepted(difficulty)
  → if _currentDangerLevel == "A" → return    // §5.2 row 5：A 階 no-op
  → nextLevel = GetNextDangerLevel(_currentDangerLevel)
  → nextData = _dangerDict.GetValueOrDefault(nextLevel)
  → if nextData == null → LogError, return    // §5.1 row 1：缺階交由 CheckLevelUp 處理，本 API 提早退出
  → if DifficultyIndex[difficulty] >= DifficultyIndex[nextData.minDifficulty]:
       ├─ _acceptedMissionCount++
       └─ CheckLevelUp()
```

**觸發點 4：F-02 OnDailyReset 訂閱 callback**

```
F02.OnDailyReset → WorldDangerService.HandleDailyReset(todayUtcTimestamp)
  → CheckLevelUp()
```

**觸發點 5：FT-09 呼叫 `OnFactionScoreUpdated(newMaxScore)`**

```
WorldDangerService.OnFactionScoreUpdated(newMaxScore)
  → if _currentDangerLevel == "A" → return    // FSD 主動加入 A 階早退（§8.3 B-02 / §8.4 GDD 回註）
  → _cachedMaxFactionScore = newMaxScore
  → CheckLevelUp()
```

**觸發點 6：CheckLevelUp 內部流程（核心）**

```
WorldDangerService.CheckLevelUp()
  → nextLevel = GetNextDangerLevel(_currentDangerLevel)
  → if nextLevel == null → return                    // 已是 A 階
  → nextData = _dangerDict.GetValueOrDefault(nextLevel)
  → if nextData == null:                             // §5.1 row 1：缺階跳過
       ├─ Debug.LogError($"missing dangerLevel row: {nextLevel}")
       ├─ _currentDangerLevel = nextLevel             // 直接視為已升至缺漏階
       └─ return CheckLevelUp()                       // 遞迴嘗試下一階
  → elapsedDays = (ITimeService.NowUTC - _gameStartTimestamp) / 86400
       // C# 整數除法 ≡ floor，前提兩數均非負（_gameStartTimestamp = 0 由 §5.2 row 2 處理）
  → timeOK    = elapsedDays >= nextData.timeThreshold
  → missionOK = _acceptedMissionCount >= nextData.missionCountReq
  → factionOK = (nextData.factionScoreReq == 0)
                OR (_cachedMaxFactionScore >= nextData.factionScoreReq)
  → if timeOK AND missionOK AND factionOK:
       ├─ _currentDangerLevel = nextLevel
       ├─ _acceptedMissionCount = 0                  // GDD §3.3 rule 6：升階重置
       ├─ IResourceService.SetBankruptcyThreshold(GetMaxDebt())
       ├─ EventBus.Publish(new OnDangerLevelChangedEvent(nextLevel))
       └─ CheckLevelUp()                              // 遞迴連跳階
  // 任一閘未滿足：直接結束
```

**觸發點 7：ISaveable RestoreFromSave 後重新推送**

```
WorldDangerService.RestoreFromSave(json)
  → 反序列化 4 欄位（_currentDangerLevel / _acceptedMissionCount / _gameStartTimestamp / _cachedMaxFactionScore）
  → if _currentDangerLevel ∉ {"E", "D", "C", "B", "A"} → throw InvalidStateException
       // Degradable 策略：FT-10 接到例外後觸發 InitializeAsNewGame
  → IResourceService.SetBankruptcyThreshold(GetMaxDebt())
       // 還原當前階對應的 maxDebt（覆寫 F-03 預設 -100）
  → 不立即觸發 CheckLevelUp（保留存檔當下狀態；下一次 OnDailyReset / API 呼叫會自然觸發）
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| `WorldDangerTable.csv` | `dangerLevel`, `name`, `timeThreshold`, `missionCountReq`, `minDifficulty`, `factionScoreReq`, `weightF_E`, `weightD`, `weightC`, `weightB`, `weightA`, `weightS_SSS`, `maxDebt`（13 欄位） | `【C-06-DS】world-danger-table.md` | 升階閘 + 任務池權重 + 債務上限三組欄位整合查詢 | `WorldDangerService.Awake()` |

### 6.2 引用的 ScriptableObject

無。所有資料來自 `WorldDangerTable.csv`，不使用 SO。

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| 各階 `timeThreshold`（升階時間閘） | `WorldDangerTable.csv` → `timeThreshold` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| 各階 `missionCountReq`（升階進度閘） | `WorldDangerTable.csv` → `missionCountReq` | 第 9 條 |
| 各階 `minDifficulty`（計入計數的最低難度） | `WorldDangerTable.csv` → `minDifficulty` | 第 9 條 |
| 各階 `factionScoreReq`（陣營閘） | `WorldDangerTable.csv` → `factionScoreReq` | 第 9 條 |
| 各階 6 個 `weight*`（任務池權重） | `WorldDangerTable.csv` → `weightF_E` ~ `weightS_SSS` | 第 9 條 |
| 各階 `maxDebt`（債務上限） | `WorldDangerTable.csv` → `maxDebt` | 第 9 條 |

**業務規則常數（非 tuning knob，硬編碼合規）**：

| 項目 | 位置 | 說明 |
| --- | --- | --- |
| `DangerLevelOrder = {"E","D","C","B","A"}` | `WorldDangerService.cs` 靜態常數 | GDD §3.1 5 階固定枚舉；新增階需同步 GDD + DS + 此常數 |
| `DifficultyIndex` 字典（F=0~SSS=8） | `WorldDangerService.cs` 靜態常數 | GDD §3.3 rule 4 固定映射；與 FT-02 §3.3 / FT-03 §4.2 共用語義 |
| `86400`（1 天秒數） | `CheckLevelUp` 偽碼內 | 時間單位定義常數 |
| Fallback `_currentDangerLevel = "E"` | `Awake` 初始化 | GDD §3.2 規定初始階 |
| Fallback `maxDebt = -100`（缺欄位時） | `GetMaxDebt` fallback 分支 | E 階 GDD 預設值；保護 CSV 異常時核心循環 |

---

## 7. 邊緣案例對策（Edge Case Handling）

### GDD §5.1 資料載入

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `WorldDangerTable` 缺某 dangerLevel 行（如缺 B） | `WorldDangerLoader` 載入時 `Debug.LogError` 列舉缺漏階；`CheckLevelUp` 內若 `_dangerDict.GetValueOrDefault(nextLevel) == null` → LogError + 直接設 `_currentDangerLevel = nextLevel`（跳過該階）+ 遞迴 `CheckLevelUp` 嘗試下一階 | `WorldDangerLoader` / `WorldDangerService` | EditMode test：mock CSV 缺 B 行；驗證升階序列從 D 直接跳至 C（不停在 B） |
| 該行 `weight*` 任一缺失或全為 0 | Loader 載入時 `Debug.LogError`；`GetPoolWeights()` 在當前階 weights 全 0 時 fallback E 階權重 | `WorldDangerLoader` / `WorldDangerService` | EditMode test：注入全 0 weights 行；驗證 fallback 為 E 階 `(40,30,20,8,2,0)` |
| 該行 `maxDebt` 缺失 / 為 0 | Loader 載入時 `Debug.LogError`；`GetMaxDebt()` 在當前階為 0 時 fallback `-100` | `WorldDangerService` | EditMode test：注入 `maxDebt=0` 行 |
| `minDifficulty` 不在合法難度值 {F,E,D,C,B,A,S,SS,SSS} | Loader 載入時 `Debug.LogError`；該行 `minDifficulty` 寫入為 `"F"`（最寬鬆門檻） | `WorldDangerLoader` | EditMode test：注入 `minDifficulty="X"` |
| `dangerLevel` 大小寫不一致（如 `e` vs `E`） | Loader 載入時統一 `ToUpperInvariant()`，`Debug.LogWarning` | `WorldDangerLoader` | EditMode test：注入小寫 `dangerLevel="d"`，驗證字典 key 為 `"D"` 且有 LogWarning |

### GDD §5.2 Runtime 操作

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| `OnFactionScoreUpdated` 尚未被 FT-09 呼叫過（初始狀態） | `_cachedMaxFactionScore = 0`（Awake 初始化）；`factionScoreReq = 0` 的階自然通過；有門檻的階暫無法升階，不報錯 | `WorldDangerService` | EditMode test：不呼叫 OnFactionScoreUpdated 時 D 階仍可升（factionScoreReq=0），C 階不可升（factionScoreReq=5） |
| `_gameStartTimestamp = 0`（存檔損毀） | `Debug.LogError`；`elapsedDays = (NowUTC - 0) / 86400` 會回傳極大值（NowUTC ≈ 17 億）→ 時間閘恆滿足；但 §5.2 row 2 GDD 明文「視為時間閘未滿足」——FSD 對策：在 `CheckLevelUp` 計算 elapsedDays 前檢查 `if _gameStartTimestamp == 0 → LogError + elapsedDays = 0`（時間閘未滿足） | `WorldDangerService` | EditMode test：mock `_gameStartTimestamp = 0`，驗證 D 階升階失敗（timeOK = false） |
| 玩家離線跨多階 | `CheckLevelUp` 遞迴處理；一次補算到符合條件的最高階 | `WorldDangerService` | DoD-10 / AC-WD-12 |
| `_acceptedMissionCount` 在存檔還原後繼續累積 | ISaveable 序列化此值，還原直接延續，不重置 | `WorldDangerService` | EditMode test：序列化 → 還原 → 驗證 count 一致 |
| 當前危險度為 A，呼叫 `OnMissionAccepted` / `CheckLevelUp` | 直接 return，不計數、不升階、不報錯（§5.4 觸發點 3 / 6 早退分支） | `WorldDangerService` | DoD-11 / AC-WD-13 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 WorldDangerTable schema（13 欄位 × 5 行） | §5.3 `WorldDangerData` / §6.1 | 對齊 | DTO 完整覆蓋 |
| §3.2 Runtime 狀態（4 欄位） | §5.3 內部狀態 / §6.4 ISaveable Serialize | 對齊 | 4 欄位皆序列化 |
| §3.3 升級規則 rule 1（單向遞增） | §5.1 API 表 + §5.4 觸發點 6 | 對齊 | 無 down-grade 分支 |
| §3.3 升級規則 rule 2（三閘 AND 條件） | §5.4 觸發點 6 `timeOK / missionOK / factionOK` | 對齊 | 邏輯 AND |
| §3.3 升級規則 rule 3（elapsedDays 計算） | §5.4 觸發點 6 公式 | 對齊 | `(NowUTC - gameStart) / 86400` |
| §3.3 升級規則 rule 4（難度索引固定 F=0~SSS=8） | §5.3 `DifficultyIndex` 字典 | 對齊 | hardcoded helper（GDD 固定） |
| §3.3 升級規則 rule 5（陣營分數推送） | §5.1 `OnFactionScoreUpdated` API + §5.4 觸發點 5 | 對齊 | C-06 不查詢 FT-09，被動接收推送 |
| §3.3 升級規則 rule 6（升階重置 acceptedMissionCount） | §5.4 觸發點 6 `_acceptedMissionCount = 0` | 對齊 | 緊跟階別變更 |
| §3.3 升級規則 rule 7（推送 + 發布事件） | §5.4 觸發點 6 `SetBankruptcyThreshold` + `OnDangerLevelChanged` | 對齊 | 升階原子操作三步驟 |
| §3.4 查詢 API 全 7 個 | §5.1 公開 API 表 | 對齊 | 簽名一致 |

### 8.2 公式對齊或替代說明

| GDD §4 公式 | FSD 採用方式 | 備註 |
| --- | --- | --- |
| §4.1 `GetElapsedDays = floor((NowUTC - gameStart) / 86400)` | 直接採用；C# 整數除法等價 floor（雙方均為非負 long） | — |
| §4.2 `CheckLevelUp` 遞迴連跳 | 直接採用；§5.4 觸發點 6 完整偽碼 | — |
| §4.3 啟動推送（C-06.Start > F-03.Start） | 直接採用；§5.4 觸發點 2 | Script Execution Order 用 `[DefaultExecutionOrder]` 屬性實現（FSD 補強：相比 GDD 的「依賴 Unity Editor 設定」，屬性式更穩定） |
| §4.4 `OnMissionAccepted` 計數規則 | 直接採用；§5.4 觸發點 3 偽碼 | A 階 short-circuit |
| §4.5 `OnFactionScoreUpdated` 即時觸發 | 直接採用；§5.4 觸發點 5 偽碼 | A 階 short-circuit（FSD 補強：GDD 偽碼未明示 A 階早退，本 FSD 主動加入早退邏輯，避免無謂遞迴；§8.4 已記錄 GDD 回註意圖） |

### 8.3 未能實現的規則與修改建議

無重大未能實現項目。建議項：

- **B-01（已解決，2026-04-27 GDD P-005）**：原建議 GDD §4.3 補「`[DefaultExecutionOrder]` 屬性式實現」說明。**已由 GDD P-005 解決**：C-06 GDD §4.3 已補「實作方式」段落，明文「兩者皆以 `[DefaultExecutionOrder(int)]` 屬性宣告、不依賴 Unity Editor 設定」。本 FSD §5.4 觸發點 2 與 §8.2 公式對齊表相關描述對齊。
- **B-02（已解決，2026-04-27 GDD P-005）**：原建議 GDD §4.5 補 A 階早退邏輯。**已由 GDD P-005 解決**：C-06 GDD §4.5 偽碼已補 `if currentDangerLevel == "A": return`。本 FSD §5.4 觸發點 5 早已主動加入此早退，對齊。
- **B-03（建議項，不阻礙實作）**：`WorldDangerLoader` 啟動時可考慮做完整 5 階存在性檢查並 LogError 列舉所有缺漏階，方便調試（目前缺漏階由 `CheckLevelUp` 運行時偵測）。屬實作細節優化，未在 GDD P-005 範圍內，保留為實作期建議。

> **GDD P-005 同步補強（FSD 受惠項目）**：除 B-01 / B-02 外，GDD P-005 另含 4 項補強對 FSD 為正面影響：
> - GDD §4.1 `GetElapsedDays` 偽碼補 `gameStartTimestamp == 0` 防護分支（FSD §7 邊緣案例 §5.2 row 2 對策已對齊）
> - GDD §4.2 `CheckLevelUp` 偽碼補缺漏階跳過分支（FSD §5.4 觸發點 6 早已主動實作對齊）
> - GDD §3.4 表格補「API 呼叫機制」note：API 直接呼叫 vs EventBus 區分（FSD §2.5 / §5.2 已對齊）
> - GDD §3.2 `gameStartTimestamp` 寫入時機明確化：FT-10 `InitializeAsNewGame` 寫入、`RestoreFromSave` 還原（FSD §5.4 觸發點 7 / §6.4 ISaveable 已對齊）

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |
| ~~2026-04-27~~ | ~~`【C-06】world-danger-system.md`~~ | ~~§3.2 / §4.3~~ | ~~FSD 回註：Script Execution Order 「F-03 < C-06」由 `[DefaultExecutionOrder]` 屬性實現（FSD §8.3 B-01），不依賴 Unity Editor 設定。~~ **已撤銷**：2026-04-27 GDD P-005 已直接補入此實作方式說明（§4.3），不再需要 FSD 回註。 |
| ~~2026-04-27~~ | ~~`【C-06】world-danger-system.md`~~ | ~~§4.5~~ | ~~FSD 回註：`OnFactionScoreUpdated` 內部建議在 `_currentDangerLevel == "A"` 時直接 return，避免冗餘 `CheckLevelUp` 呼叫；對齊 §5.2 row 5 「A 階不報錯」精神（FSD §8.3 B-02）。~~ **已撤銷**：2026-04-27 GDD P-005 已直接補 A 階早退至 §4.5 偽碼，不再需要 FSD 回註。 |

> 兩條 GDD 回註意圖均因 GDD P-005 直接落地而撤銷，不再寫入 GDD。

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |
| — | 無真實衝突 | — | — |

C-06 GDD 與全域規則無時間單位、enum、跨系統一致性衝突。`elapsedDays` 用「天」為時間閘單位（`timeThreshold` 為整數天數）屬規格自洽：時間運算層仍以 `NowUTC` 秒為基準計算，「天」僅為 UI / 規格層的表達單位（86400 秒 = 1 天），不違反「全專案時間單位只能用秒或小時」規則（後者規範時間變數命名與比較單位，不阻擋以「天」為設計層概念表達閾值）。

---

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [x] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [x] §1.3 完成目標：15 條 DoD 對齊 GDD 16 條 AC，每條可被 EditMode / Integration test 驗證
- [x] §2.1~§2.5：GDD 章節（10 條）、Data-Specs（1 份）、上游（2 系統）、下游（5 系統）、事件契約（OnDangerLevelChanged out + OnDailyReset in）皆列舉
- [x] §3.3 對映表：7 條，覆蓋 GDD §1 / §2 所有面向
- [x] §4.1~§4.4：拆分判斷（否，預估 410~530 行）；Script 清單 5 個 Script 路徑、SRP、依賴介面、預估規模全填
- [x] §5.1~§5.4：7 個 API 簽名、2 個事件 payload、4 個資料結構（含內部狀態 + DifficultyIndex helper）、7 個觸發點偽碼齊備
- [x] §6.1~§6.3：1 張 CSV 表 + Data-Specs 連結；§6.3 嚴禁寫死清單 6 項對齊原則第 9 條，5 項業務規則常數已說明
- [x] §7：GDD §5.1 5 條 + §5.2 5 條共 10 條邊緣案例皆有具體對策（無「妥善處理」）
- [x] §8.1：對齊清單覆蓋 GDD §3.1 / §3.2 / §3.3（7 rules） / §3.4，二層粒度
- [x] §8.2~§8.5：5 條公式對齊（含 FSD 主動加 A 階早退與 DefaultExecutionOrder 兩處補強）；§8.3 三條建議項；§8.4 兩條 GDD 回註意圖；§8.5 無衝突
- [x] FSD-index：§6.1 / §7.1 / §7.2 將同步更新；§6.2 新增 C-06-DS 列

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-04-27 | Claude Code 主體（直接撰寫，無 subagent） | 通過 | 通過 | 通過 | 無真實衝突；建議項 B-01（DefaultExecutionOrder 屬性式實現）/ B-02（OnFactionScoreUpdated A 階早退）/ B-03（Loader 啟動時做完整 5 階存在性檢查）皆不阻礙實作；§8.4 兩條 GDD 回註意圖待主體覆核後寫入 GDD |
| 2026-04-27 | Claude Code 主體（GDD P-005 對齊 patch） | 通過 | 通過 | 通過 | 對應 `/design-review C-06` NEEDS REVISION → GDD P-005（6 子項）：B-01（DefaultExecutionOrder）/ B-02（A 階早退）已由 GDD P-005 §4.3 / §4.5 直接落地，FSD §8.3 標「已解決」；§8.4 兩條 GDD 回註意圖撤銷（GDD 已直接補入，不需 FSD 回註）。GDD P-005 另 4 項補強（§4.1 防護、§4.2 缺漏階、§3.4 API 機制、§3.2 寫入時機）對 FSD 為正面影響，§8.3 已記錄 FSD 受惠項目。B-03 保留為實作期建議。 |
