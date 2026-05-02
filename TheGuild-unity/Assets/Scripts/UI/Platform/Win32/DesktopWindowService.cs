// P-01 Desktop Transparent Window 主 Controller。
// Singleton MonoBehaviour，實作 ISaveable（FT-10 持久化 userScale + targetMonitorID）。
// 執行順序 [DefaultExecutionOrder(100)] 確保晚於 SaveLoadService (default=0) 執行 Start。
//
// 設計文件：design/GDD/【P-01】desktop-transparent-window.md
//           design/FSD/【P-01-FSD】desktop-transparent-window.md

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Gameplay.Save;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using UIPanel = UnityEngine.UIElements.IPanel;

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// Win32 透明視窗 widget 主控制器。
    /// <para>
    /// 暴露 9 個對外 API（FSD §5.1）給 P-02 SettingsPanel：
    /// GetEffectiveScale / SetUserScale / ResetUserScaleToDefault / GetUserScale /
    /// EnumerateAvailableMonitors / SwitchTargetScreen / GetCurrentTargetMonitorID /
    /// Minimize / RegisterEffectiveScaleListener
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DesktopWindowService : MonoBehaviour, ISaveable
    {
        // ──────────────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────────────

        [Header("P-01 Tuning（必指派 P01Tuning.asset）")]
        [SerializeField] private P01Tuning _tuning;

        // ──────────────────────────────────────────────────────
        //  Singleton
        // ──────────────────────────────────────────────────────

        /// <summary>DesktopWindowService Singleton 實例。</summary>
        public static DesktopWindowService Instance { get; private set; }

        // ──────────────────────────────────────────────────────
        //  內部狀態
        // ──────────────────────────────────────────────────────

        // 平台 flag：非 WindowsPlayer 或 Editor 模式時為 true，所有 API 一律早退
        private bool _isPlatformDisabled;
        // 已印過一次 platform disabled warning（避免重複 log）
        private bool _platformWarnLogged;

        // Win32 視窗句柄
        private IntPtr _hwnd;

        // 子系統（非 MonoBehaviour）
        private WindowScaleController _scaleController;
        private MonitorEnumerator     _monitorEnumerator;
        private WindowHitTester       _hitTester;

        // Hit-Test 啟用旗標（OnUIReady 後才 true）
        private bool _isHitTestEnabled;

        // 目標螢幕 ID（0 = sentinel，代表使用主螢幕）
        private int _targetMonitorID;

        // 儲存資料（FT-10 Bootstrap 時由 RestoreFromSave 填入）
        private DesktopWindowSaveData _saveData;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // WndProc 委託需持有強引用避免 GC 回收
        private WndProcDelegate _wndProcDelegate;
        // 舊 WndProc 指標（呼叫 DefWindowProc 用）
        private IntPtr _oldWndProc;

        // Win32 WndProc 函式簽章
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
#endif

        // ──────────────────────────────────────────────────────
        //  ISaveable 契約（FSD §5.3 / FT-10）
        // ──────────────────────────────────────────────────────

        /// <summary>FT-10 儲存鍵（固定值：p01DesktopWindow）。</summary>
        public string OwnerKey => "p01DesktopWindow";

        /// <summary>非 critical：縮放與螢幕設定 fallback 預設值即可，不阻擋讀取。</summary>
        public bool IsCritical => false;

        // ──────────────────────────────────────────────────────
        //  MonoBehaviour 生命週期
        // ──────────────────────────────────────────────────────

        private void Awake()
        {
            // EnsureSingleton（FSD §5.4.1 Awake step 1）
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 平台檢查：非 WindowsPlayer 時停用所有 Win32 功能（FSD §7 EC）
            if (Application.platform != RuntimePlatform.WindowsPlayer)
            {
                _isPlatformDisabled = true;
                Debug.LogWarning("[P-01] 非 Windows Standalone 平台，DesktopWindowService 已停用。");
                return;
            }

            // 初始化子系統（Awake 時不需 UI 就緒）
            _scaleController   = new WindowScaleController(_tuning);
            _monitorEnumerator = new MonitorEnumerator();
            _hitTester         = null; // 待 OnUIReady

            // 訂閱 OnUIReadyEvent（FSD §5.4.1 Awake 末步）
            EventBus.Subscribe<OnUIReadyEvent>(HandleOnUIReady);
        }

        private void Start()
        {
            // 平台停用時早退（DoD-11）
            if (_isPlatformDisabled)
            {
                return;
            }

            // Step 1：確認 _saveData 已由 FT-10 RestoreFromSave 填入
            // [DefaultExecutionOrder(100)] 保證本 Start 晚於 SaveLoadService.Start (default 0)
            // 安全 fallback：若 _saveData 仍為 default（理論不發生）則初始化
            if (_saveData.userScale <= 0f)
            {
                InitializeAsNewGame();
            }

            // Step 2：解析目標螢幕
            MonitorInfo targetMonitor = ResolveTargetMonitor(_saveData.targetMonitorID);
            _targetMonitorID = targetMonitor.MonitorID;

            // Step 3：取得 HWND 並計算視窗尺寸
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _hwnd = Win32Native.GetActiveWindow();
#endif
            CalculateAndApplyWindow(targetMonitor);
        }

        private void OnDestroy()
        {
            // 清理 singleton 引用
            if (Instance == this)
            {
                Instance = null;
            }

            // 退訂 EventBus
            EventBus.Unsubscribe<OnUIReadyEvent>(HandleOnUIReady);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 還原原始 WndProc，避免程式關閉後懸置指標
            if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            {
                Win32Native.SetWindowLongPtr(_hwnd, Gwl.WndProc, _oldWndProc);
            }
#endif
        }

        // ──────────────────────────────────────────────────────
        //  ISaveable 實作
        // ──────────────────────────────────────────────────────

        /// <summary>序列化當前 userScale 與 targetMonitorID 為 JSON。</summary>
        public string Serialize()
        {
            return JsonUtility.ToJson(new DesktopWindowSaveData
            {
                userScale       = _scaleController != null ? _scaleController.UserScale : 1.0f,
                targetMonitorID = _targetMonitorID
            });
        }

        /// <summary>由 FT-10 Bootstrap 呼叫，還原儲存值（在 Start 前執行）。</summary>
        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            DesktopWindowSaveData data;
            try
            {
                data = JsonUtility.FromJson<DesktopWindowSaveData>(ownerJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[P-01] RestoreFromSave 解析失敗：{ex.Message}，使用預設值。");
                InitializeAsNewGame();
                return;
            }

            _saveData = data;
        }

        /// <summary>初始化為新遊戲預設值。</summary>
        public void InitializeAsNewGame()
        {
            _saveData = new DesktopWindowSaveData
            {
                userScale       = 1.0f,
                targetMonitorID = 0  // sentinel：Start 解析時 fallback 主螢幕
            };
        }

        // ──────────────────────────────────────────────────────
        //  公開 API（FSD §5.1 — 9 個對外方法）
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 回傳當前 effectiveScale = baseResolutionScale × userScale。
        /// P-02 PanelSettings.scale 套用值來源。
        /// </summary>
        public float GetEffectiveScale()
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return 1.0f;
            }

            return _scaleController.GetEffectiveScale();
        }

        /// <summary>
        /// 設定玩家縮放。內部 clamp 至 [UserScaleMin, UserScaleMax]。
        /// 設定後重算 effectiveScale 並推送所有已註冊 callback；透過 ISaveable 持久化。
        /// </summary>
        public void SetUserScale(float value)
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return;
            }

            // Step 1：clamp（FSD §5.4.4 Step 1）
            float clamped = Mathf.Clamp(value, _tuning.UserScaleMin, _tuning.UserScaleMax);

            // Step 2：冪等檢查（FSD §5.4.4 Step 2）
            if (Mathf.Approximately(clamped, _scaleController.UserScale))
            {
                return;
            }

            // Step 3：套用（FSD §5.4.4 Step 3，內部 DispatchScaleListeners）
            _scaleController.SetUserScale(clamped);

            // Step 4：持久化（FSD §5.4.4 Step 4）
            MarkSaveDataDirty();
        }

        /// <summary>
        /// 將 userScale 還原為預設值 1.0（等同 SetUserScale(1.0f)）。
        /// </summary>
        public void ResetUserScaleToDefault()
        {
            SetUserScale(1.0f);
        }

        /// <summary>回傳當前 userScale（給 P-02 SettingsPanel slider 顯示）。</summary>
        public float GetUserScale()
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return 1.0f;
            }

            return _scaleController.UserScale;
        }

        /// <summary>
        /// 列舉所有可用螢幕。
        /// MonitorInfo 含 MonitorID / DisplayName / IsPrimary / IsConnected。
        /// </summary>
        public IReadOnlyList<MonitorInfo> EnumerateAvailableMonitors()
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return Array.Empty<MonitorInfo>();
            }

            return _monitorEnumerator.GetAll();
        }

        /// <summary>
        /// 切換目標螢幕，重算 WorkingArea / 視窗尺寸 / baseResolutionScale 並推送 callback。
        /// monitorID 不存在時 fallback 主螢幕 + LogWarning；切換後透過 ISaveable 持久化。
        /// </summary>
        public void SwitchTargetScreen(int monitorID)
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return;
            }

            // Step 1：查找目標螢幕（FSD §5.4.5）
            MonitorInfo? found = _monitorEnumerator.FindByID(monitorID);
            MonitorInfo target;
            if (found == null || !found.Value.IsConnected)
            {
                Debug.LogWarning($"[P-01] SwitchTargetScreen：monitorID={monitorID} 不存在或已斷線，fallback 主螢幕。");
                target = _monitorEnumerator.GetPrimary();
            }
            else
            {
                target = found.Value;
            }

            // Step 2：更新 targetMonitorID
            _targetMonitorID = target.MonitorID;

            // Step 3~4：重算視窗尺寸並套用（同時觸發 callback，Step 5）
            CalculateAndApplyWindow(target);

            // Step 6：持久化
            MarkSaveDataDirty();
        }

        /// <summary>回傳當前目標螢幕 monitorID（給 P-02 SettingsPanel dropdown 高亮）。</summary>
        public int GetCurrentTargetMonitorID()
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return 0;
            }

            return _targetMonitorID;
        }

        /// <summary>
        /// 最小化視窗至工作列。
        /// 還原由 Windows 工作列點擊觸發，WndProc 收 WM_ACTIVATE 後重設 HWND_TOPMOST。
        /// </summary>
        public void Minimize()
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                return;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Win32Native.ShowWindow(_hwnd, Sw.Minimize);
#endif
        }

        /// <summary>
        /// 回傳玩家縮放的合法範圍與步進幅度（來自 P01Tuning.asset）。
        /// 供 P-02 SettingsPanel Slider 設定 lowValue / highValue / pageSize。
        /// </summary>
        public (float min, float max, float step) GetUserScaleRange()
        {
            if (_tuning == null)
            {
                // _tuning 未指派時回傳與 P01Tuning 預設值對齊的安全值
                return (0.5f, 2.0f, 0.1f);
            }

            return (_tuning.UserScaleMin, _tuning.UserScaleMax, _tuning.UserScaleStep);
        }

        /// <summary>
        /// 註冊 effectiveScale 變更 callback。
        /// 註冊後立即 invoke 一次當前值（FSD §5.4.6 / DoD-12）。
        /// </summary>
        public void RegisterEffectiveScaleListener(Action<float> callback)
        {
            if (_isPlatformDisabled)
            {
                LogPlatformDisabledOnce();
                // 即使平台停用，仍立即 invoke 一次預設值 1.0，使 P-02 不必特判
                callback?.Invoke(1.0f);
                return;
            }

            if (callback == null)
            {
                return;
            }

            // Step 1：加入監聽清單（FSD §5.4.6 Step 1）
            _scaleController.AddListener(callback);

            // Step 2：立即 invoke 一次（FSD §5.4.6 Step 2 / DoD-12）
            callback.Invoke(_scaleController.GetEffectiveScale());
        }

        // ──────────────────────────────────────────────────────
        //  事件處理
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 收到 P-02 OnUIReadyEvent 後：建立 WindowHitTester 並啟用 Hit-Test（FSD §5.4.1）。
        /// </summary>
        private void HandleOnUIReady(OnUIReadyEvent evt)
        {
            // Step 1：定義 panelProvider lambda（每次呼叫時取當前 panel，避免 stale reference）
            Func<UIPanel> panelProvider = () =>
            {
                if (PanelManager.Instance == null)
                {
                    return null;
                }

                return PanelManager.Instance.GetMainSceneRootPanel();
            };

            // Step 2：建立 WindowHitTester
            _hitTester = new WindowHitTester(_hwnd, panelProvider, _scaleController);

            // Step 3：啟用 Hit-Test
            _isHitTestEnabled = true;
        }

        // ──────────────────────────────────────────────────────
        //  Win32 WndProc
        // ──────────────────────────────────────────────────────

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// 自訂 WndProc：攔截 WM_NCHITTEST / WM_DISPLAYCHANGE / WM_ACTIVATE（FSD §5.4.1 Step 9）。
        /// </summary>
        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Wm.NcHitTest)
            {
                return HandleNcHitTest(lParam);
            }

            if (msg == Wm.DisplayChange)
            {
                HandleDisplayChange();
                return IntPtr.Zero;
            }

            if (msg == Wm.Activate)
            {
                // WA_ACTIVE 或 WA_CLICKACTIVE：最小化還原後重設 HWND_TOPMOST（FSD §5.4.7）
                int wParamLow = (int)(wParam.ToInt64() & 0xFFFF);
                if (wParamLow == WaParam.Active || wParamLow == WaParam.ClickActive)
                {
                    // 重設置頂（最小化可能重置 topmost）
                    Win32Native.SetWindowPos(
                        _hwnd, HwndZOrder.TopMost,
                        0, 0, 0, 0,
                        Swp.NoSize | Swp.NoMove | Swp.NoActivate);
                }

                // 讓預設 WndProc 繼續處理 WM_ACTIVATE
            }

            // 其餘訊息交給原始 WndProc
            return Win32Native.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>處理 WM_NCHITTEST（FSD §5.4.2）。</summary>
        private IntPtr HandleNcHitTest(IntPtr lParam)
        {
            // Bootstrap 階段或 HitTester 未初始化：一律 HTCLIENT（不全穿透）
            if (!_isHitTestEnabled || _hitTester == null)
            {
                return new IntPtr(HitTestResult.Client);
            }

            // 從 lParam 解出螢幕座標（LOWORD / HIWORD）
            long raw    = lParam.ToInt64();
            short screenX = unchecked((short)(raw & 0xFFFF));
            short screenY = unchecked((short)((raw >> 16) & 0xFFFF));

            Point screenPt = new Point { X = screenX, Y = screenY };
            int result = _hitTester.HitTest(screenPt);
            return new IntPtr(result);
        }

        /// <summary>處理 WM_DISPLAYCHANGE（FSD §5.4.3）。</summary>
        private void HandleDisplayChange()
        {
            // Step 1：重新列舉螢幕
            _monitorEnumerator.Refresh();

            // Step 2：解析當前 targetMonitorID（若已斷開 fallback 主螢幕）
            MonitorInfo target = ResolveTargetMonitor(_targetMonitorID);
            _targetMonitorID = target.MonitorID;

            // Step 3~5：重算視窗尺寸、更新 scale 並 SetWindowPos（含 callback dispatch）
            CalculateAndApplyWindow(target);
        }
#endif

        // ──────────────────────────────────────────────────────
        //  內部輔助方法
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 解析目標螢幕：先找 monitorID，找不到或已斷線則 fallback 主螢幕。
        /// </summary>
        private MonitorInfo ResolveTargetMonitor(int monitorID)
        {
            if (monitorID != 0)
            {
                MonitorInfo? found = _monitorEnumerator.FindByID(monitorID);
                if (found != null && found.Value.IsConnected)
                {
                    return found.Value;
                }

                Debug.LogWarning($"[P-01] 目標螢幕 monitorID={monitorID} 不存在或已斷線，fallback 主螢幕。");
            }

            return _monitorEnumerator.GetPrimary();
        }

        /// <summary>
        /// 依目標螢幕計算視窗尺寸 / 位置，套用 Win32 樣式設定，並更新 scale。
        /// Start / SwitchTargetScreen / WM_DISPLAYCHANGE 共用此方法。
        /// </summary>
        private void CalculateAndApplyWindow(MonitorInfo monitor)
        {
            // Step 3：計算視窗尺寸（FSD §5.4.1 Step 3，GDD §4 公式）
            int windowWidth  = monitor.WorkWidth;
            int windowHeight = Mathf.RoundToInt(monitor.WorkHeight * _tuning.WindowHeightRatio);
            int windowX      = monitor.WorkLeft;
            int windowY      = monitor.WorkBottom - windowHeight;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Step 4：套用 ExtendedStyle（加 WS_EX_LAYERED | WS_EX_APPWINDOW）
            IntPtr currentEx = Win32Native.GetWindowLongPtr(_hwnd, Gwl.ExStyle);
            IntPtr newEx     = new IntPtr(currentEx.ToInt64() | WsEx.Layered | WsEx.AppWindow);
            Win32Native.SetWindowLongPtr(_hwnd, Gwl.ExStyle, newEx);

            // Step 5：移除 Style（移除 WS_CAPTION | WS_THICKFRAME）
            IntPtr currentStyle = Win32Native.GetWindowLongPtr(_hwnd, Gwl.Style);
            IntPtr newStyle     = new IntPtr(currentStyle.ToInt64() & ~(long)(Ws.Caption | Ws.ThickFrame));
            Win32Native.SetWindowLongPtr(_hwnd, Gwl.Style, newStyle);

            // Step 6：SetLayeredWindowAttributes（color key = RGB(0,0,0)，LWA_COLORKEY）
            Win32Native.SetLayeredWindowAttributes(_hwnd, 0x000000, 0, Lwa.ColorKey);

            // Step 7：Camera clearFlags = SolidColor，backgroundColor = Color(0,0,0,0)
            ApplyCameraSettings();

            // Step 8：SetWindowPos HWND_TOPMOST
            Win32Native.SetWindowPos(
                _hwnd, HwndZOrder.TopMost,
                windowX, windowY, windowWidth, windowHeight,
                Swp.ShowWindow);

            // Step 9：替換 WndProc（僅首次執行時安裝，避免重複替換）
            if (_wndProcDelegate == null)
            {
                _wndProcDelegate = WndProc;
                IntPtr newWndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                _oldWndProc = Win32Native.ReplaceWndProc(_hwnd, newWndProcPtr);
            }
#endif

            // Step 10：更新 scale 並推送 callback（含 WM_DISPLAYCHANGE / SwitchTargetScreen 的 dispatch）
            _scaleController.UpdateBaseResolutionScale(monitor.WorkHeight);

            // Step 10 補：還原 userScale（Start 初次執行時套用 _saveData.userScale）
            // 注意：UpdateBaseResolutionScale 會 dispatch，不需再次 dispatch
            if (!Mathf.Approximately(_scaleController.UserScale, _saveData.userScale))
            {
                // 直接 SetUserScale，clamp + dispatch 在 SetUserScale 內執行
                float clamped = Mathf.Clamp(_saveData.userScale, _tuning.UserScaleMin, _tuning.UserScaleMax);
                _scaleController.SetUserScale(clamped);
            }
        }

        /// <summary>套用 Camera 透明設定（Step 7）。</summary>
        private static void ApplyCameraSettings()
        {
            Camera main = Camera.main;
            if (main == null)
            {
                Debug.LogWarning("[P-01] 找不到 Camera.main，跳過 camera 透明設定。");
                return;
            }

            main.clearFlags       = CameraClearFlags.SolidColor;
            main.backgroundColor  = new Color(0f, 0f, 0f, 0f); // 純黑對應 color key
        }

        /// <summary>通知 FT-10 執行節流持久化。</summary>
        private void MarkSaveDataDirty()
        {
            // FSD §5.4.4 Step 4 / §5.4.5 Step 6：通知 FT-10 節流寫入
            SaveLoadService.Instance?.MarkDirty();
        }

        /// <summary>非 Win32 平台首次呼叫時印一次 warning。</summary>
        private void LogPlatformDisabledOnce()
        {
            if (_platformWarnLogged)
            {
                return;
            }

            _platformWarnLogged = true;
            Debug.LogWarning("[P-01] DesktopWindowService 已停用（非 Windows Standalone 平台）。");
        }

        // ──────────────────────────────────────────────────────
        //  ISaveable 序列化資料結構（內嵌）
        // ──────────────────────────────────────────────────────

        [Serializable]
        private struct DesktopWindowSaveData
        {
            public float userScale;
            public int   targetMonitorID;
        }
    }
}
