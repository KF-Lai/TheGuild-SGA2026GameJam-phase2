// 整流程 WM_NCHITTEST hit-test 實作（FSD §5.4.2 / §4.4 設計審查後 C2 修正）。
// 職責：接收螢幕座標 → ScreenToClient → 套 effectiveScale → panel.Pick 查詢 → 回傳 HTCLIENT / HTTRANSPARENT。
// 透過 Func<IPanel> panelProvider 避免 UIDocument reload 後的 stale reference（FSD §7 EC / C2 I5 修正）。

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// WM_NCHITTEST 整流程執行器。
    /// <para>
    /// 由 <see cref="DesktopWindowService"/> 在收到 <c>OnUIReadyEvent</c> 後建立，
    /// 每次 WM_NCHITTEST 觸發時執行一次完整 Hit-Test 流程（無 per-frame polling）。
    /// </para>
    /// </summary>
    internal sealed class WindowHitTester
    {
        private readonly IntPtr _hwnd;

        // 每次查詢時透過 provider 取得當前 panel，避免 UIDocument reload 後 stale reference
        private readonly Func<IPanel> _panelProvider;

        private readonly WindowScaleController _scaleController;

        /// <summary>
        /// 建立 WindowHitTester 實例。
        /// </summary>
        /// <param name="hwnd">目標視窗句柄（HWND）</param>
        /// <param name="panelProvider">
        ///     每次 HitTest 呼叫時取得當前 UI Toolkit <see cref="IPanel"/> 的工廠函式。
        ///     回傳 null 時視為 panel 尚未準備好，一律回 HTCLIENT（FSD §7 EC）。
        /// </param>
        /// <param name="scaleController">縮放控制器（取得當前 effectiveScale）</param>
        public WindowHitTester(IntPtr hwnd, Func<IPanel> panelProvider, WindowScaleController scaleController)
        {
            _hwnd            = hwnd;
            _panelProvider   = panelProvider ?? throw new ArgumentNullException(nameof(panelProvider));
            _scaleController = scaleController ?? throw new ArgumentNullException(nameof(scaleController));
        }

        /// <summary>
        /// 執行完整 WM_NCHITTEST 流程並回傳結果碼。
        /// </summary>
        /// <param name="screenPt">由 WM_NCHITTEST lParam 解出的螢幕絕對座標</param>
        /// <returns>
        ///     <see cref="HitTestResult.Client"/> (1)：有 VisualElement 命中，正常接收輸入。<br/>
        ///     <see cref="HitTestResult.Transparent"/> (-1)：無命中，穿透至底層視窗。
        /// </returns>
        public int HitTest(Point screenPt)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Step 1：螢幕座標 → 視窗 client 座標
            Point clientPt = screenPt;
            if (!Win32Native.ScreenToClient(_hwnd, ref clientPt))
            {
                // ScreenToClient 失敗（視窗已銷毀等），安全 fallback
                return HitTestResult.Client;
            }

            // Step 2：取得當前 effectiveScale
            float scale = _scaleController.GetEffectiveScale();
            if (scale <= 0f)
            {
                // 防禦：scale 異常（理論上不發生）
                Debug.LogWarning("[P-01] effectiveScale <= 0，HitTest fallback HTCLIENT");
                return HitTestResult.Client;
            }

            // Step 3：client 座標 → panel-local 座標（除以 effectiveScale）
            var localPt = new Vector2(clientPt.X / scale, clientPt.Y / scale);

            // Step 4：每次 query 取當前 panel（避免 UIDocument reload 後 stale reference）
            IPanel panel = _panelProvider();
            if (panel == null)
            {
                // P-02 panel 尚未準備好（理論上不發生，因 OnUIReady 後才建立 HitTester）
                // 但作為雙重保險保留 HTCLIENT（FSD §7 EC）
                return HitTestResult.Client;
            }

            // Step 5：panel.Pick 查詢 → 有命中 HTCLIENT，無命中 HTTRANSPARENT
            return panel.Pick(localPt) != null ? HitTestResult.Client : HitTestResult.Transparent;
#else
            // 非 Win32 build：Hit-Test 不適用，一律回 HTCLIENT
            return HitTestResult.Client;
#endif
        }
    }
}
