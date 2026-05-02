# 【P-01-FSD】功能規格說明書 — Desktop Transparent Window

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【P-01】desktop-transparent-window.md`（2026-04-26 已設計） |
| 對應 Data-Specs | 無 CSV 引用；§7 5 個常數收錄於 `P01Tuning.asset`（ScriptableObject，不對應 Data-Specs） |
| 撰寫者 | Claude Code 主體（Opus 4.7 + xhigh，無 subagent） |
| Review 者 | Claude Code 主體（自檢） |
| 狀態 | 審查中 |
| 最近更新 | 2026-05-03 |

---

## 1. 概要（Overview）

### 1.1 系統範圍

P-01 Desktop Transparent Window 為「桌面原生」支柱的技術基礎層。透過 Win32 API（C# `DllImport` P/Invoke）將 Unity 視窗設為透明背景、常駐前置（`HWND_TOPMOST`），並以 `WM_NCHITTEST` 攔截實作細粒度點擊穿透。對外暴露 9 個 API（`GetEffectiveScale` / `SetUserScale` / `ResetUserScaleToDefault` / `GetUserScale` / `EnumerateAvailableMonitors` / `SwitchTargetScreen` / `GetCurrentTargetMonitorID` / `Minimize` + `RegisterEffectiveScaleListener` callback 註冊）給 P-02。Jam 版完整實作純 Windows API，不引入第三方插件。

### 1.2 In-Scope / Out-of-Scope

**In-Scope**：
- Unity 視窗透明化（`WS_EX_LAYERED` + `LWA_COLORKEY` 黑色 color key）
- `HWND_TOPMOST` 常駐前置 + `SW_RESTORE` 後重設置頂
- `WM_NCHITTEST` 攔截實作 panel.Pick Hit-Test
- `WM_DISPLAYCHANGE` 監聽 + 重算 `WorkingArea` / 視窗尺寸 / `baseResolutionScale`
- 多螢幕 enumeration + `SwitchTargetScreen` 切換
- `userScale` 玩家調整 + `effectiveScale` 計算 + callback 推送
- FT-10 持久化 `userScale` + 目標 `monitorID`
- 9 個對外 API + 1 callback 註冊

**Out-of-Scope**：
- 多螢幕延伸 UI（Post-Jam）— GDD §3.1 末段標記
- 可調邊框／使用者切換「取消置頂」（GDD §3.4 明示 Jam 不提供）
- macOS / Linux 平台支援（Jam 僅 Windows Standalone）
- Unity Editor 模式下的視窗行為（GDD §5 EC：所有 Win32 調用以 `UNITY_STANDALONE_WIN && !UNITY_EDITOR` 條件編譯）

### 1.3 完成目標（Definition of Done）

對齊 GDD §8 AC-01~AC-11，補充程式可驗證條件：

- **DoD-01**（GDD AC-01）：Build 後啟動，`Screen.width == WorkingArea.Width`、`Screen.height == WorkingArea.Height × WINDOW_HEIGHT_RATIO`，視窗 Y 等於 `WorkingArea.Bottom - windowHeight`
- **DoD-02**（GDD AC-02）：截圖驗證視窗背景為 `#000000` color key 透明，桌面底圖可見
- **DoD-03**（GDD AC-03）：手動點擊背景區域，桌面圖示被選中（透過 `WM_NCHITTEST` 回傳 `HTTRANSPARENT`）
- **DoD-04**（GDD AC-04）：點擊任意 UI 按鈕觸發 `pointerDown` 事件（`WM_NCHITTEST` 回傳 `HTCLIENT`）
- **DoD-05**（GDD AC-05）：Alt+Tab 切換至其他應用程式後，遊戲視窗仍在最前
- **DoD-06**（GDD AC-06）：Windows 設定變更解析度，`WM_DISPLAYCHANGE` 觸發後 `Screen.height` 自動更新，無重啟
- **DoD-07**（GDD AC-07）：呼叫 `SwitchTargetScreen(monitorID)` 後，視窗 `windowX/Y` 對應新螢幕 `WorkingArea` 並透過 FT-10 持久化
- **DoD-08**（GDD AC-08）：呼叫 `SetUserScale(0.7)`，左/右錨點面板維持邊緣對齊（不向中央漂移），`PanelSettings.scale ≈ baseResolutionScale × 0.7`
- **DoD-09**（GDD AC-09）：呼叫 `ResetUserScaleToDefault()`，`GetUserScale() == 1.0f`，`PanelSettings.scale == baseResolutionScale`
- **DoD-10**（GDD AC-10）：呼叫 `Minimize()`，`IsIconic(hwnd) == true`；點擊工作列圖示後 `IsIconic == false` 且 `GetWindowLongPtr(GWL_EXSTYLE)` 仍含 `WS_EX_TOPMOST`
- **DoD-11**（GDD AC-11）：Unity Editor Play Mode 不執行任何 Win32 調用，無 `LogError` / `Exception`
- **DoD-12**：`RegisterEffectiveScaleListener(callback)` 註冊後立即觸發一次 callback（推送當前 `effectiveScale`）；後續 `WM_DISPLAYCHANGE` / `SetUserScale` / `ResetUserScaleToDefault` / `SwitchTargetScreen` 各觸發一次

---

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

引用 P-01 GDD §1 概覽、§2 玩家幻想、§3.1 ~ §3.8 全章節、§4 公式、§5 邊界情況、§6 依賴關係、§7 調校旋鈕、§8 驗收標準。

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |
| 無 | 無 | 無 | P-01 不引用 CSV；§7 5 個 tuning knob 收錄於 `P01Tuning.asset`（ScriptableObject，不對應 Data-Specs） |

### 2.3 上游依賴系統

| 系統 | 依賴內容 | 服務契約 |
| --- | --- | --- |
| P-02 Main UI Framework | (a) 等待 `OnUIReady` 事件後才啟用 Hit-Test（GDD §3.2 step 10）；(b) `panel.Pick(localPos)` 查詢需要 P-02 panel 實例存在（GDD §3.3）；(c) UI 錨點規則（GDD §3.6 USS `position: absolute; left/right: 0`）由 P-02 UXML/USS 實作；(d) P-02 提供 `OnEffectiveScaleChanged(float)` callback（GDD §3.8.1） | `OnUIReady` 透過 `EventBus` 訂閱（依 FSD-index §2.10 直呼 `EventBus.Subscribe<OnUIReadyEvent>`）；callback 透過 `RegisterEffectiveScaleListener` 註冊 |
| FT-10 Save/Load System | `userScale` 與目標 `monitorID` 持久化；P-01 為 owner，於 `SetUserScale` / `SwitchTargetScreen` 內建寫入 | 實作 `ISaveable`（OwnerKey = `"p01DesktopWindow"`，IsCritical = false） |

> Service 命名遵循 FSD-index §2.10：`P01.X` 敘述對應實作 `DesktopWindowService.Instance.X`。

### 2.4 下游被依賴系統

| 系統 | 說明 |
| --- | --- |
| P-02 Main UI Framework（FSD-B SettingsPanel） | 設定彈窗呼叫 §5.1 列出的 8 個對外 API + `RegisterEffectiveScaleListener`；`OnEffectiveScaleChanged` callback 推送至 P-02 `PanelSettings.scale` |
| P-03 Notification System | Jam 範疇外（已確認不實作）；UI 元素若實作則須在 P-01 Hit-Test 可命中區域內 — Jam 不影響 |
| FT-10 Save/Load System | P-01 實作 `ISaveable`，FT-10 Bootstrap 序列載入時呼叫 `RestoreFromSave(json)` 還原 `userScale` 與目標 `monitorID` |

### 2.5 跨系統事件契約

| 事件名稱 | 方向 | Payload | 發布時機 |
| --- | --- | --- | --- |
| `OnUIReadyEvent` | 訂閱（P-02 → P-01）| 無 payload（marker event）| P-02 `UIBootstrapController` 完成 Bootstrap step 8 後發布；P-01 收到後 `_isHitTestEnabled = true` |
| `Action<float>` callback（非 EventBus）| P-01 → P-02 | `effectiveScale: float` | (1) `RegisterEffectiveScaleListener` 註冊時立即推送一次；(2) `WM_DISPLAYCHANGE` 重算後；(3) `SetUserScale` / `ResetUserScaleToDefault` 後；(4) `SwitchTargetScreen` 後 |

P-01 不發布 `EventBus` 事件；對 P-02 的縮放推送採直接 callback（FSD-A `RegisterEffectiveScaleListener` 註冊機制），避免 EventBus 訂閱者集合不可預測的 callback 順序。

---

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

玩家不需切換至遊戲視窗——公會的透明視窗常駐桌面前景，工作或上課時只需「瞄一眼」即可掌握公會狀態。閒置冒險者在透明視窗中走動，任務計時器靜靜運轉，讓玩家感受到「公會活在自己的桌面上」。

### 3.2 系統目的還原

P-01 是「桌面原生」支柱的技術基礎，透過 Win32 API 將 Unity 視窗轉為透明常駐 widget，並提供細粒度點擊穿透邏輯與多螢幕／縮放適配能力，使玩家能與桌面其他應用程式並行操作而不互相干擾。

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |
| 「公會活在桌面上」常駐感 | 視窗永遠在最前，無視 Alt+Tab，桌面圖示透視可見 | `SetWindowLongPtr WS_EX_LAYERED` + `LWA_COLORKEY RGB(0,0,0)` 黑色透明；`SetWindowPos HWND_TOPMOST` 全程維持；Camera `clearFlags = SolidColor` + `Color(0,0,0,0)` |
| 「不打斷工作流」並行操作 | 點擊背景透明區域穿透到桌面圖示／其他視窗；點擊 UI 元素正常觸發 | 自訂 WndProc 攔截 `WM_NCHITTEST`，透過 `panel.Pick(localPos) != null` 判斷回傳 `HTCLIENT` 或 `HTTRANSPARENT`（不使用 `WS_EX_TRANSPARENT` 全視窗穿透） |
| 「在任何螢幕上適應」 | 玩家換螢幕／改解析度，視窗自動跟隨並等比縮放 | `WM_DISPLAYCHANGE` 監聽 → 重算 `WorkingArea` / `baseResolutionScale`；`SwitchTargetScreen(monitorID)` 玩家手動切換；`baseResolutionScale = WorkingArea.Height / REFERENCE_HEIGHT` 公式 |
| 「玩家自訂大小」 | 設定彈窗 slider 調整 widget 縮放，左右面板維持邊緣對齊 | `SetUserScale(value)` clamp 至 `[USER_SCALE_MIN, USER_SCALE_MAX]`；`effectiveScale = baseResolutionScale × userScale`；callback 推送 P-02 `PanelSettings.scale`；USS 錨點 `left: 0` / `right: 0` 邊緣縮放 |
| 「最小化不破壞置頂」 | 點擊最小化縮至工作列；點擊工作列圖示還原後仍置頂 | `Minimize()` → `ShowWindow(SW_MINIMIZE)`；`SW_RESTORE` 後立即重呼 `SetWindowPos HWND_TOPMOST`（最小化會重置 topmost） |

---

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

否。

### 4.2 拆分理由

P-01 GDD 雖含 §3.1~§3.8 共 8 個子節，但所有規則皆環繞同一個 Win32 視窗實例，職責高度耦合（視窗尺寸／初始化／Hit-Test／置頂／縮放／錨點／最小化／API 全部依賴同一個 HWND）。FSD 級拆分會造成 HWND 跨檔共享耦合與訊息分發鏈複雜化。改採**單 FSD + 7 Script 內部職責分區**，每個 Script 預估 < 500 行（符合 FSD-index §2.4 拆分判斷上限），無需 FSD-A/B 拆分。

### 4.3 拆分結果

未拆分；§4.4 直接列出 Script 清單。

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |
| `Win32Native.cs` | `Assets/Scripts/UI/Platform/Win32/Win32Native.cs` | 純 P/Invoke `DllImport` 包裝 + Win32 enum / struct 定義 | `System.Runtime.InteropServices` | 100~150 行 |
| `MonitorInfo.cs` | `Assets/Scripts/UI/Platform/Win32/MonitorInfo.cs` | `MonitorInfo` readonly struct（`monitorID` / `displayName` / `isPrimary` / `isConnected`） | 無 | 30~50 行 |
| `MonitorEnumerator.cs` | `Assets/Scripts/UI/Platform/Win32/MonitorEnumerator.cs` | `EnumDisplayMonitors` 列舉 + `GetMonitorInfo` 取得 displayName/WorkingArea + 主螢幕識別 | `Win32Native`、`MonitorInfo` | 80~120 行 |
| `WindowHitTester.cs` | `Assets/Scripts/UI/Platform/Win32/WindowHitTester.cs` | **整流程** `WM_NCHITTEST` Hit-Test（接收 screen 座標 → `ScreenToClient` → 套 `effectiveScale` → `panel.Pick` 查詢 → 回傳 `HTCLIENT` / `HTTRANSPARENT`） | `Win32Native`、`WindowScaleController`、`UnityEngine.UIElements.IPanel`（透過 `Func<IPanel>` provider 注入避免 stale reference） | 100~150 行 |
| `WindowScaleController.cs` | `Assets/Scripts/UI/Platform/Win32/WindowScaleController.cs` | `userScale` clamp + `baseResolutionScale` 計算 + `effectiveScale` 推送 callback dispatch | `MonitorEnumerator`、`P01Tuning` | 100~150 行 |
| `P01Tuning.cs` | `Assets/Scripts/UI/Platform/Win32/P01Tuning.cs` | `ScriptableObject` class 定義 5 個 tuning knob 屬性與 `[CreateAssetMenu]` 標籤；對應 asset 檔位於 `Assets/UI/Tuning/P01Tuning.asset` | `UnityEngine.ScriptableObject` | 30~50 行 |
| `DesktopWindowService.cs` | `Assets/Scripts/UI/Platform/Win32/DesktopWindowService.cs` | 主 controller + Singleton（Awake 初始化 / WndProc 替換 / 9 API surface / `ISaveable` 5 member / 訂閱 `OnUIReadyEvent`） | `Win32Native`、`MonitorEnumerator`、`WindowHitTester`、`WindowScaleController`、`P01Tuning`、`ISaveable`、`EventBus` | 280~380 行 |

**合計**：6 Script + 1 ScriptableObject asset，預估 720~1050 行；最大單 Script `DesktopWindowService.cs` 280~380 行 < 500 行，符合 FSD-index §2.4 不拆分標準。**FSD-index §2.10 規範**：不新增 service interface 抽象層，DesktopWindowService 為 concrete singleton，下游（P-02 SettingsPanel）直接呼叫 `DesktopWindowService.Instance.X`。

### 4.5 類別關係

```
DesktopWindowService (MonoBehaviour, ISaveable, Singleton, [DefaultExecutionOrder(100)])
  ├─ uses: Win32Native (static P/Invoke)
  ├─ uses: MonitorEnumerator
  ├─ uses: WindowHitTester (ctor 注入 hwnd, panelProvider Func<IPanel>, scaleController)
  ├─ uses: WindowScaleController (ctor 注入 P01Tuning SerializeField)
  └─ subscribes: EventBus<OnUIReadyEvent>

WindowScaleController
  ├─ holds: userScale, baseResolutionScale
  ├─ uses: P01Tuning (ScriptableObject)
  └─ dispatches: List<Action<float>> _scaleListeners

WindowHitTester
  ├─ holds: hwnd, panelProvider (Func<IPanel>), scaleController
  ├─ uses: Win32Native (ScreenToClient)
  └─ queries: panelProvider().Pick(localPos)  // 每次 query，避免 UIDocument reload 後 stale reference

MonitorEnumerator
  └─ uses: Win32Native (EnumDisplayMonitors, GetMonitorInfo)

P01Tuning (ScriptableObject)
  └─ exposes: 5 tuning knob 屬性（ReferenceHeight / WindowHeightRatio / UserScaleMin/Max/Step）
```

**[DefaultExecutionOrder(100)] 數值對齊基準**：
- FT-10 SaveLoadService default order = 0；其 `Start()` 內呼叫 `Bootstrap()` 收集 ISaveable 並執行 RestoreFromSave
- P-01 [DefaultExecutionOrder(100)] 確保 `Start()` 晚於 SaveLoadService.Start()，`_saveData` 已被 RestoreFromSave 還原
- P-02 PanelManager / SceneObjectController order = -100 ~ -90（早於 P-01 Awake，但 P-02 OnUIReady 事件由 UIBootstrapController 在 Bootstrap step 8 後發布，與 [DefaultExecutionOrder] 解耦）

---

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

```csharp
// === 縮放查詢與推送（GDD §3.5 / §3.8）===
public float GetEffectiveScale();
// 回傳當前 effectiveScale = baseResolutionScale × userScale；P-02 PanelSettings.scale 套用值來源

public void SetUserScale(float value);
// clamp 至 [USER_SCALE_MIN, USER_SCALE_MAX]，套用後重算 effectiveScale 並推送所有註冊的 callback；
// 透過 ISaveable.MarkDirty 觸發 FT-10 節流寫入

public void ResetUserScaleToDefault();
// 等同 SetUserScale(1.0f)

public float GetUserScale();
// 回傳當前 userScale

// === 螢幕切換（GDD §3.1 / §3.8）===
public IReadOnlyList<MonitorInfo> EnumerateAvailableMonitors();
// 透過 MonitorEnumerator 列舉；MonitorInfo 含 monitorID, displayName, isPrimary, isConnected

public void SwitchTargetScreen(int monitorID);
// 切換目標螢幕，重算 WorkingArea / 視窗尺寸 / baseResolutionScale；
// monitorID 不存在時 fallback 主螢幕 + LogWarning；
// 切換後推送 effectiveScale callback；透過 ISaveable.MarkDirty 觸發持久化

public int GetCurrentTargetMonitorID();
// 回傳當前目標螢幕 monitorID

// === 視窗控制（GDD §3.7 / §3.8）===
public void Minimize();
// ShowWindow(hwnd, SW_MINIMIZE)；無回值

// === Callback 註冊（GDD §3.8.1）===
public void RegisterEffectiveScaleListener(Action<float> callback);
// 註冊後立即觸發一次（推送當前 effectiveScale）；無對應 Unregister API（生命週期與 P-01 同）
```

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |
| `OnUIReadyEvent`（owner: P-02-FSD-A）| 訂閱 | 無 | P-02 完成 Bootstrap step 8 發布；P-01 收到後 `_isHitTestEnabled = true`，啟用 `WM_NCHITTEST` 攔截 |
| `Action<float>` callback（非 EventBus）| 推送（P-01 → P-02）| `newScale: float` | 4 觸發源：(1) `RegisterEffectiveScaleListener` 註冊瞬間；(2) `WM_DISPLAYCHANGE` 重算後；(3) `SetUserScale` / `ResetUserScaleToDefault` 後；(4) `SwitchTargetScreen` 後 |
| Win32 訊息攔截 `WM_NCHITTEST` | 攔截 | screen 座標（lParam）| 每次滑鼠移動／點擊由 Windows 系統送達自訂 WndProc；回傳 `HTCLIENT` / `HTTRANSPARENT` |
| Win32 訊息攔截 `WM_DISPLAYCHANGE` | 攔截 | bpp（wParam）+ resolution（lParam）| 解析度變更時 Windows 廣播；P-01 收到後重算所有 monitor 相關狀態並推送 callback |

### 5.3 資料結構

```csharp
// MonitorInfo.cs（GDD §3.8.2）
public readonly struct MonitorInfo
{
    public readonly int    monitorID;     // HMONITOR.ToInt32() 或自訂穩定 ID（取決於平台 ID 穩定性）
    public readonly string displayName;   // 例 "DELL U2720Q (1)"；透過 GetMonitorInfo + EnumDisplayDevices 組裝
    public readonly bool   isPrimary;     // 透過 GetMonitorInfo MONITORINFOEX.dwFlags & MONITORINFOF_PRIMARY
    public readonly bool   isConnected;   // FT-10 還原時用；當前 enumerate 結果一律 true，斷開螢幕由 fallback 邏輯處理

    public MonitorInfo(int monitorID, string displayName, bool isPrimary, bool isConnected) { ... }
}

// P01Tuning.cs (ScriptableObject)
// 路徑：Assets/Scripts/UI/Platform/Win32/P01Tuning.cs（class）
// asset：Assets/UI/Tuning/P01Tuning.asset（沿用既有 P02UITuning 同目錄）
[CreateAssetMenu(menuName = "TheGuild/UI/P01 Tuning", fileName = "P01Tuning")]
public sealed class P01Tuning : ScriptableObject
{
    [Header("解析度自適應（GDD §7）")]
    [SerializeField] private int _referenceHeight = 1080;
    [Range(0.20f, 0.50f), SerializeField] private float _windowHeightRatio = 0.30f;

    [Header("玩家縮放範圍")]
    [Range(0.3f, 0.8f), SerializeField] private float _userScaleMin = 0.5f;
    [Range(1.5f, 3.0f), SerializeField] private float _userScaleMax = 2.0f;
    [Range(0.05f, 0.25f), SerializeField] private float _userScaleStep = 0.1f;

    public int   ReferenceHeight    => _referenceHeight;
    public float WindowHeightRatio  => _windowHeightRatio;
    public float UserScaleMin       => _userScaleMin;
    public float UserScaleMax       => _userScaleMax;
    public float UserScaleStep      => _userScaleStep;
}

// DesktopWindowSaveData (ISaveable serialize 用，內嵌於 DesktopWindowService.cs)
[Serializable]
private struct DesktopWindowSaveData
{
    public float userScale;
    public int   targetMonitorID;
}

// ISaveable contract（FT-10 既定）
public string OwnerKey   => "p01DesktopWindow";
public bool   IsCritical => false;  // 縮放與螢幕設定 fallback 預設值即可，非 critical
```

### 5.4 內部資料流

#### 5.4.1 啟動初始化（GDD §3.2）

```
DesktopWindowService.Awake()
  ├─ EnsureSingleton:
  │    if (Instance != null && Instance != this) {
  │        Destroy(gameObject); return;
  │    }
  │    Instance = this;
  │    DontDestroyOnLoad(gameObject);
  ├─ if (Application.platform != RuntimePlatform.WindowsPlayer)
  │    → _isPlatformDisabled = true; LogWarning("[P-01] non-Windows platform, disabled."); return
  ├─ _scaleController = new WindowScaleController(_tuning)
  ├─ _monitorEnumerator = new MonitorEnumerator()
  ├─ _hitTester = null （待 OnUIReady）
  └─ EventBus.Subscribe<OnUIReadyEvent>(HandleOnUIReady)

DesktopWindowService.Start()  // [DefaultExecutionOrder(100)] 晚於 SaveLoadService default 0
  ├─ Step 0：if (_isPlatformDisabled) return
  ├─ Step 1：讀取 _saveData
  │    │   FT-10 SaveLoadService.Start (order=0) 已執行 Bootstrap 並對所有 ISaveable 呼叫
  │    │   RestoreFromSave / InitializeAsNewGame；P-01 [DefaultExecutionOrder(100)] 保證
  │    │   本 Start 執行時 _saveData 已被填入。
  │    └─ if (_saveData 仍為 default) → InitializeAsNewGame()  // 安全 fallback（理論不發生）
  ├─ Step 2：解析目標螢幕
  │    ├─ if (_saveData.targetMonitorID != 0) → 從 _monitorEnumerator.FindByID 找對應
  │    └─ else 或找不到 / isConnected=false → fallback _monitorEnumerator.GetPrimary() + LogWarning
  ├─ Step 3：取得 HWND（Win32Native.GetActiveWindow）並計算視窗尺寸
  │    └─ windowHeight = WorkingArea.Height × _tuning.WindowHeightRatio
  │    └─ windowX = WorkingArea.Left, windowY = WorkingArea.Bottom - windowHeight
  ├─ Step 4：套用 ExtendedStyle（SetWindowLongPtr GWL_EXSTYLE，加 WS_EX_LAYERED | WS_EX_APPWINDOW）
  ├─ Step 5：移除 Style（SetWindowLongPtr GWL_STYLE，移除 WS_CAPTION | WS_THICKFRAME）
  ├─ Step 6：SetLayeredWindowAttributes（color key = RGB(0,0,0), LWA_COLORKEY）
  ├─ Step 7：套用 Camera clearFlags=SolidColor, backgroundColor=Color(0,0,0,0)
  ├─ Step 8：Win32Native.SetWindowPos（hwnd, HWND_TOPMOST, windowX, windowY, width, windowHeight）
  ├─ Step 9：替換 WndProc（Win32Native.SetWindowLongPtr GWLP_WNDPROC）啟用 WM_NCHITTEST +
  │         WM_DISPLAYCHANGE + WM_ACTIVATE 攔截
  ├─ Step 10：_scaleController.UpdateBaseResolutionScale(workingArea.Height)
  │         （此時 _scaleListeners 為空，無 callback dispatch；P-02 在 Bootstrap step 7 之前
  │          呼叫 RegisterEffectiveScaleListener，註冊瞬間立即推送一次當前值）
  └─ Step 11：等待 OnUIReadyEvent（已在 Awake 訂閱）

DesktopWindowService.HandleOnUIReady(OnUIReadyEvent evt)
  ├─ Step 1：定義 panelProvider lambda
  │         () => PanelManager.Instance != null
  │             ? PanelManager.Instance.GetMainSceneRootPanel()  // 對應 MainSceneCanvas UIDocument
  │             : null
  │         （每次 WM_NCHITTEST 觸發時呼叫，避免 UIDocument reload 後 stale reference）
  ├─ Step 2：_hitTester = new WindowHitTester(_hwnd, panelProvider, _scaleController)
  └─ Step 3：_isHitTestEnabled = true
```

#### 5.4.2 WM_NCHITTEST 攔截（GDD §3.3）

```
Windows.SendMessage(WM_NCHITTEST, lParam = MAKELPARAM(screenX, screenY))
  → DesktopWindowService.WndProc(hwnd, msg, wParam, lParam)
      ├─ if (msg == WM_NCHITTEST)
      │    ├─ if (!_isHitTestEnabled || _hitTester == null)
      │    │    → return HTCLIENT (1)  // Bootstrap 階段保留接收輸入，避免全穿透 race
      │    └─ else
      │         → screenPt = (LOWORD(lParam), HIWORD(lParam))
      │         → return _hitTester.HitTest(screenPt)
      ├─ if (msg == WM_DISPLAYCHANGE) → 委派 §5.4.3
      ├─ if (msg == WM_ACTIVATE && wParam == WA_ACTIVE) → 委派 §5.4.7
      └─ else → return DefWindowProc(hwnd, msg, wParam, lParam)

WindowHitTester.HitTest(POINT screenPt) : int
  ├─ Step 1：clientPt = screenPt（複製為 mutable）
  │         Win32Native.ScreenToClient(_hwnd, ref clientPt)
  ├─ Step 2：scale = _scaleController.GetEffectiveScale()
  │         if (scale <= 0) → return HTCLIENT  // 安全 fallback（理論不發生）
  ├─ Step 3：localPt = new Vector2(clientPt.x / scale, clientPt.y / scale)
  ├─ Step 4：panel = _panelProvider()  // 每次 query 當前 panel，避免 UIDocument reload 後 stale
  │         if (panel == null) → return HTCLIENT  // P-02 panel 尚未準備好，保留輸入
  └─ Step 5：return panel.Pick(localPt) != null ? HTCLIENT (1) : HTTRANSPARENT (-1)
```

#### 5.4.3 WM_DISPLAYCHANGE 攔截（GDD §3.1）

```
Windows.SendMessage(WM_DISPLAYCHANGE, wParam = bpp, lParam = MAKELPARAM(width, height))
  → DesktopWindowService.WndProc
      └─ if (msg == WM_DISPLAYCHANGE)
          ├─ Step 1：_monitorEnumerator.Refresh()
          ├─ Step 2：解析當前 _targetMonitorID（若已斷開 fallback 主螢幕 + LogWarning）
          ├─ Step 3：重算 windowHeight / windowX / windowY
          ├─ Step 4：_scaleController.UpdateBaseResolutionScale(workingArea.Height)
          │      └─ 內部：baseResolutionScale = workingArea.Height / _tuning.ReferenceHeight
          │           effectiveScale = baseResolutionScale × userScale
          │           DispatchScaleListeners(effectiveScale)
          └─ Step 5：Win32Native.SetWindowPos(hwnd, HWND_TOPMOST, windowX, windowY, ...)
```

#### 5.4.4 SetUserScale（GDD §3.5）

```
P-02.SettingsPanel slider OnValueChanged
  → DesktopWindowService.SetUserScale(value)
      ├─ Step 1：clamp = Mathf.Clamp(value, _tuning.UserScaleMin, _tuning.UserScaleMax)
      ├─ Step 2：if (clamp ≈ _scaleController.UserScale) return  // 冪等
      ├─ Step 3：_scaleController.SetUserScale(clamp)
      │      └─ 內部：userScale = clamp
      │           effectiveScale = baseResolutionScale × clamp
      │           DispatchScaleListeners(effectiveScale)
      └─ Step 4：MarkSaveDataDirty()
              └─ FT-10 節流寫入（依 SAVE_AUTO_INTERVAL_SEC）
```

#### 5.4.5 SwitchTargetScreen（GDD §3.1）

```
P-02.SettingsPanel dropdown OnSelected(monitorID)
  → DesktopWindowService.SwitchTargetScreen(monitorID)
      ├─ Step 1：var monitor = _monitorEnumerator.FindByID(monitorID)
      │   └─ if (monitor == null || !monitor.isConnected)
      │      → LogWarning + fallback _monitorEnumerator.GetPrimary()
      ├─ Step 2：_targetMonitorID = monitor.monitorID
      ├─ Step 3：取得 monitor 的 WorkingArea，重算視窗尺寸
      ├─ Step 4：Win32Native.SetWindowPos(hwnd, HWND_TOPMOST, windowX, windowY, width, windowHeight)
      ├─ Step 5：_scaleController.UpdateBaseResolutionScale(workingArea.Height)（觸發 callback）
      └─ Step 6：MarkSaveDataDirty()
```

#### 5.4.6 RegisterEffectiveScaleListener（GDD §3.8.1）

```
P-02.UIBootstrapController（Bootstrap step 7 之前）
  → DesktopWindowService.RegisterEffectiveScaleListener(callback)
      ├─ Step 1：_scaleController.AddListener(callback)
      └─ Step 2：callback.Invoke(_scaleController.GetEffectiveScale())   // 立即推送一次
```

#### 5.4.7 Minimize / Restore（GDD §3.7）

```
P-02.SettingsPanel onMinimizeClicked
  → DesktopWindowService.Minimize()
      └─ Win32Native.ShowWindow(hwnd, SW_MINIMIZE)
          // 不主動處理還原；還原由 Windows 工作列點擊觸發

[Windows 系統] 玩家點擊工作列圖示
  → SW_RESTORE 自動執行
  → DesktopWindowService.WndProc 收到 WM_ACTIVATE（wParam == WA_ACTIVE）
      └─ Win32Native.SetWindowPos(hwnd, HWND_TOPMOST, ...)  // 重設 topmost
```

#### 5.4.8 ISaveable Serialize / RestoreFromSave（FT-10 契約）

```
FT-10 SaveLoadService.ExecuteSave
  → DesktopWindowService.Serialize() : string
      └─ return JsonUtility.ToJson(new DesktopWindowSaveData {
              userScale = _scaleController.UserScale,
              targetMonitorID = _targetMonitorID
          });

FT-10 SaveLoadBootstrap retry
  → DesktopWindowService.RestoreFromSave(json)
      ├─ if (string.IsNullOrEmpty(json)) → InitializeAsNewGame() return
      ├─ var data = JsonUtility.FromJson<DesktopWindowSaveData>(json)
      ├─ if (data == null) → LogError + InitializeAsNewGame() return
      ├─ _saveData = data
      └─ Start() 階段套用（Step 2 解析 targetMonitorID + Step 3+ 套用 userScale）

DesktopWindowService.InitializeAsNewGame()
  ├─ _saveData.userScale = 1.0f
  └─ _saveData.targetMonitorID = 0   // sentinel：Start 解析時 fallback 主螢幕
```

---

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |
| 無 | — | — | P-01 不引用任何 CSV | — |

### 6.2 引用的 ScriptableObject

| SO | 欄位 | 用途 | 載入時機 |
| --- | --- | --- | --- |
| `P01Tuning.asset` | `_referenceHeight`、`_windowHeightRatio`、`_userScaleMin`、`_userScaleMax`、`_userScaleStep` | 解析度自適應基準與玩家縮放範圍／步進 | `DesktopWindowService.Awake()` SerializeField 注入 |

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |
| `REFERENCE_HEIGHT`（int 1080） | `P01Tuning._referenceHeight` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `WINDOW_HEIGHT_RATIO`（float 0.30） | `P01Tuning._windowHeightRatio` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `USER_SCALE_MIN`（float 0.5） | `P01Tuning._userScaleMin` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `USER_SCALE_MAX`（float 2.0） | `P01Tuning._userScaleMax` | 對應「四、程式實作原則」第 9 條：參數表格化 |
| `USER_SCALE_STEP`（float 0.1） | `P01Tuning._userScaleStep` | 對應「四、程式實作原則」第 9 條：參數表格化 |

> Win32 常數（如 `WS_EX_LAYERED = 0x00080000`、`HWND_TOPMOST = -1`、`HTCLIENT = 1`、`HTTRANSPARENT = -1`）為 Windows API 規範值，集中於 `Win32Native.cs` 作為 `const`，不視為 game-design tuning，不入 ScriptableObject。

---

## 7. 邊緣案例對策（Edge Case Handling）

對齊 GDD §5 邊界情況：

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |
| 工作列在頂部、左側或右側 | `WorkingArea` 透過 `GetMonitorInfo MONITORINFOEX.rcWork` 取得，已自動排除工作列；`windowY = WorkingArea.Bottom - windowHeight` 對任何工作列方向皆正確 | `MonitorEnumerator`、`DesktopWindowService` Step 3 | EditMode 測試：mock `WorkingArea` 為 `(0, 40, 1920, 1080)`（頂部工作列）/ `(40, 0, 1920, 1080)`（左側工作列），驗證 windowY 計算結果 |
| 多螢幕（Jam 版）切換 | 玩家透過 `SwitchTargetScreen(monitorID)` 切換；fallback 主螢幕 + LogWarning（GDD §3.1） | `MonitorEnumerator.FindByID` + `DesktopWindowService.SwitchTargetScreen` | PlayMode 手動測試：插拔副螢幕，呼叫 `SwitchTargetScreen` 驗證視窗搬移 |
| 多螢幕延伸 UI（Post-Jam） | Jam 不實作；FSD §1.2 標 Out-of-Scope | — | — |
| `userScale` 超出範圍 | `SetUserScale` 內部 `Mathf.Clamp(value, UserScaleMin, UserScaleMax)`，不拋例外 | `DesktopWindowService.SetUserScale` | EditMode 測試：呼叫 `SetUserScale(10.0f)` 驗證 `GetUserScale() == UserScaleMax` |
| 解析度變更（`WM_DISPLAYCHANGE`） | WndProc 收到後重算 `WorkingArea` / `baseResolutionScale` / 視窗尺寸；推送 callback；不重啟遊戲 | `DesktopWindowService.WndProc` `WM_DISPLAYCHANGE` 分支 | PlayMode 手動測試：在 Windows 設定變更解析度，驗證視窗自動調整 |
| 禁用色 `#000000` 被遊戲元素使用 | 程式不修補；由美術規範禁用 `#000000`（GDD §3.2 註記）；如違反則該元素顯示為透明破洞 | — | 美術 review 確認 art 與 USS 無 `#000000` 純黑色碼 |
| `HWND_TOPMOST` 被重置（最小化還原後） | WndProc 收到 `WM_ACTIVATE`（`wParam == WA_ACTIVE`）後立即重呼 `SetWindowPos HWND_TOPMOST` | `DesktopWindowService.WndProc` `WM_ACTIVATE` 分支 | PlayMode 手動測試：最小化後從工作列還原，驗證視窗仍置頂 |
| Unity Editor 模式 | 所有 Win32 調用以 `#if UNITY_STANDALONE_WIN && !UNITY_EDITOR` 條件編譯；Editor Play Mode 跳過初始化（`_isPlatformDisabled = true`），所有 API 早退並 LogWarning（首次呼叫一次） | `Win32Native`（整個 class 條件編譯）、`DesktopWindowService.Awake` 平台檢查 | EditMode 測試：在 Editor 跑 Play Mode，驗證 console 0 error |

額外實作期 EC（GDD 未列但實作必須處理）：

| EC | 處理方式 | 涉及 Script |
| --- | --- | --- |
| `panel.Pick()` 在 P-02 OnUIReady 前被呼叫 | `_isHitTestEnabled = false` 或 `_hitTester == null` 時 `WM_NCHITTEST` 一律回 `HTCLIENT`（保留接收輸入；不可一律 transparent，否則 Bootstrap 階段點擊全穿透）；`WindowHitTester.HitTest` 內 `panelProvider()` 回 null 時也回 `HTCLIENT`（雙重保險）| `DesktopWindowService.WndProc`、`WindowHitTester.HitTest` |
| `RegisterEffectiveScaleListener` 重複註冊同一 callback | `WindowScaleController.AddListener` 用 `List<Action<float>>` 不去重；P-02 端應確保只註冊一次（生命週期與 P-01 同） | `WindowScaleController.AddListener` |
| `EnumerateAvailableMonitors` 在無顯示器環境（極端） | 回傳含一個假 `MonitorInfo`（`monitorID = 0`、`displayName = "(no display)"`、`isPrimary = true`、`isConnected = false`）；視窗以 1920×1080 預設值 fallback | `MonitorEnumerator.Enumerate` |
| `UIDocument` reload uxml 導致 `IPanel` reference 失效 | `WindowHitTester` 透過 `Func<IPanel> panelProvider` lambda 注入，每次 `HitTest` 呼叫時 query 當前 panel；不持有 stale reference | `WindowHitTester.HitTest`、`DesktopWindowService.HandleOnUIReady` Step 1 |
| FT-10 RestoreFromSave 順序倒置（P-01 Start 早於 SaveLoadService Bootstrap） | `[DefaultExecutionOrder(100)]` 保證 P-01 Start 晚於 SaveLoadService.Start (default 0)；安全 fallback：P-01 Start Step 1 偵測 `_saveData == default` 時呼叫 `InitializeAsNewGame()`（理論不發生）| `DesktopWindowService.Start` Step 1 |

---

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |
| §3.1 視窗尺寸規則 | §5.4.1 Step 3、§5.4.3、§5.4.5 | 對齊 | 含工作列方向自適應、解析度自適應、切換螢幕、Post-Jam 多螢幕延伸標 Out-of-Scope |
| §3.2 視窗初始化流程（10 步） | §5.4.1 | 對齊 | Step 1~10 一一對應，含 OnUIReady 等待 |
| §3.3 點擊穿透（Hit-Test） | §5.4.2 | 對齊 | 座標轉換 4 步 + `panel.Pick` 查詢；不使用 `WS_EX_TRANSPARENT` |
| §3.4 Always on Top | §5.4.1 Step 8、§5.4.7 | 對齊 | 初始化設 `HWND_TOPMOST`；最小化還原後重設 |
| §3.5 縮放規則 | §5.4.4、§5.4.6 | 對齊 | clamp + callback dispatch + FT-10 持久化 |
| §3.6 UI 錨點規則 | §3.3（對映表）、§2.3 上游依賴 | 對齊 | 由 P-02 USS 實作；P-01 提供 `effectiveScale` 給 PanelSettings.scale |
| §3.7 最小化 / 還原 | §5.4.7 | 對齊 | `Minimize()` API + `WM_ACTIVATE` 重設 topmost |
| §3.8 對外 API surface | §5.1 | 對齊 | 8 API + `RegisterEffectiveScaleListener` + `MonitorInfo` schema 全收錄 |

### 8.2 公式對齊或替代說明

GDD §4 列出的 3 條公式（`baseResolutionScale` / `effectiveScale` / `windowHeight`）皆**直接採用偽碼**，無替代。實作於 `WindowScaleController.UpdateBaseResolutionScale` 與 `DesktopWindowService.Start` Step 3。範例計算（GDD §4 末段：1920×1080 工作列 40px → windowHeight = 312、baseResolutionScale ≈ 0.963；2560×1440 → windowHeight = 420、baseResolutionScale ≈ 1.296）作為單元測試樣本。

### 8.3 未能實現的規則與修改建議

| ID | GDD 規則 | 實作狀況 | 建議 |
| --- | --- | --- | --- |
| B-01 | GDD §3.1 末段 Post-Jam「多螢幕延伸 UI」 | Jam 不實作 | 已標 §1.2 Out-of-Scope；Post-Jam 啟用時新增 `IExtendedUiHost` 介面與 `MonitorEnumerator.GetAdjacent` 邏輯 |
| B-02 | GDD §3.5「玩家可透過遊戲內 UI 調整 userScale」 | UI 由 P-02-FSD-B2 SettingsPanel 實作；P-01 僅提供 `SetUserScale` API | 不阻擋實作；P-01 完成後 SettingsPanel 即可動工 |
| B-03 | `MonitorInfo.isConnected` 動態追蹤 | 當前 enumerate 結果一律 `true`；斷開螢幕僅在 FT-10 還原時 fallback；不做 Hot-plug 即時偵測 | Jam 範圍接受；如需 hot-plug 偵測可新增 `WM_DEVICECHANGE` 攔截，建議 Post-Jam |
| B-04 | `EnumerateAvailableMonitors` 在無顯示器環境（CI / headless build）| §7 額外 EC：回傳假 `MonitorInfo`；實際 build target 為 Windows Standalone GUI，無此情境 | 接受；測試環境若需 mock，可在 `MonitorEnumerator` 注入 `IMonitorProvider` 介面（Post-Jam 重構） |
| ~~B-05~~ | ~~`panel.Pick` 在 OnUIReady 前被呼叫的 fallback~~ | **已內化於 §7 EC 與 §5.4.2 雙重保險**（`_isHitTestEnabled = false` 或 `_hitTester == null` 或 `panelProvider() == null` 任一觸發回 HTCLIENT）；無修改建議 | — |

#### 8.3.1 design-review 修正紀錄（2026-05-03）

`/design-review P-01-FSD` 第一輪 NEEDS REVISION 7 條 issue 全修：

| issue | 嚴重度 | 修正內容 |
| --- | --- | --- |
| C1 | P0 | §4.4 移除 `IDesktopWindowService.cs`（違反 FSD-index §2.10 不新增 service interface 抽象層）；§4.4 末段加入 §2.10 規範對齊宣告；§4.5 類別關係圖移除 `exposes: IDesktopWindowService`；DesktopWindowService 為 concrete singleton，下游直接 `DesktopWindowService.Instance.X` |
| C2 | P1 | §5.4.2 改 WndProc 委派 `_hitTester.HitTest(screenPt)`，WindowHitTester 內部執行整流程（ScreenToClient + scale 換算 + panel.Pick + 回傳 HTCLIENT/HTTRANSPARENT）；§4.4 WindowHitTester 職責文字加「**整流程**」標示 |
| I1 | P2 | §4.5 類別關係圖加 `[DefaultExecutionOrder(100)]` 數值對齊基準說明（FT-10 SaveLoadService default 0、P-02 PanelManager / SceneObjectController -100~-90） |
| I2 | P2 | §5.4.1 Awake EnsureSingleton 展開為完整偽碼（5 行 if/Destroy/Instance/DontDestroyOnLoad）|
| I3 | P2 | §5.4.1 Start Step 1 加 `[DefaultExecutionOrder(100)]` 對齊機制說明（保證 P-01 Start 晚於 SaveLoadService.Start = 0）；安全 fallback：`_saveData == default` 時呼叫 `InitializeAsNewGame()`；§7 EC 加新列「FT-10 RestoreFromSave 順序倒置」 |
| I4 | P3 | §4.4 P01Tuning 列改為獨立 Script（`Assets/Scripts/UI/Platform/Win32/P01Tuning.cs`），移除原「同目錄或獨立」模糊表述；§5.3 P01Tuning 註解明定 class 路徑 + asset 路徑 |
| I5 | P3 | §4.4 WindowHitTester 依賴改為 `Func<IPanel> provider`；§4.5 類別關係 `panelProvider().Pick` 註明每次 query；§5.4.1 HandleOnUIReady Step 1 加 panelProvider lambda 定義；§5.4.2 WindowHitTester.HitTest Step 4 註明每次 query；§7 EC 加新列「UIDocument reload 導致 IPanel stale」 |

### 8.4 給 GDD 的回註紀錄

無。本 FSD 完全對齊 GDD §3.1~§3.8、§4、§5、§7、§8，無需 GDD 回註。

### 8.5 衝突處理紀錄

無衝突。GDD 內部規則一致，跨系統依賴（P-02 / FT-10）契約清晰。

---

## 附錄 A — Review 紀錄（FSD Review Log）

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
| 2026-05-03 | Claude Code 主體（Opus 4.7 + xhigh） | 通過 | 通過 | 通過 | 章節編號／標題／順序與 FSD-index §三完全一致；7 Script 拆分理由成立、SRP 清晰、API/事件/資料流前後一致；GDD §3.1~§3.8 逐項對齊（§8.1）、§4 公式直接採用（§8.2）、§5 邊緣案例皆有對策（§7）、§7 5 個 tuning knob 全表格化於 P01Tuning ScriptableObject（§6.2 / §6.3）；§2.9 完成前 checklist 全勾選 |
| 2026-05-03 | Claude Code 主體（Opus 4.7 + xhigh，design-review patch 後）| 通過 | 通過 | 通過 | `/design-review P-01-FSD` 第一輪 NEEDS REVISION 7 條 issue 全修（C1 P0 / C2 P1 / I1~I5 P2~P3）；§4.4 6 Script + 1 SO（從 7+1 移除 IDesktopWindowService 違反 §2.10）；§5.4.1 EnsureSingleton 偽碼展開、Start Step 1 補 `[DefaultExecutionOrder(100)]` 對齊機制；§5.4.2 WndProc 委派 `_hitTester.HitTest(screenPt)` + WindowHitTester.HitTest 5 步偽碼；§4.5 類別關係圖補 [DefaultExecutionOrder] 數值對齊基準；§7 EC 新增 2 條（UIDocument reload IPanel stale / FT-10 順序倒置 fallback）；§8.3 B-05 標已內化、新增 §8.3.1 修正紀錄表 |
