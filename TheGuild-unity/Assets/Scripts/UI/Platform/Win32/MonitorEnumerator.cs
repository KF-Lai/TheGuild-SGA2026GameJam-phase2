// 透過 Win32 EnumDisplayMonitors + GetMonitorInfo 列舉並快取所有連線螢幕資訊。
// 職責：列舉 → 快取 → 提供查詢（FindByID / GetPrimary）。
// 不持有任何 MonoBehaviour 生命週期；由 DesktopWindowService 持有與呼叫。

using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// 螢幕列舉器。封裝 Win32 EnumDisplayMonitors / GetMonitorInfo 的呼叫，
    /// 並維護一份已快取的螢幕清單（<see cref="Refresh"/> 後更新）。
    /// </summary>
    internal sealed class MonitorEnumerator
    {
        // 快取的螢幕清單（readonly view 對外暴露）
        private readonly List<MonitorInfo> _monitors = new List<MonitorInfo>(4);

        // 對外唯讀視圖
        private List<MonitorInfo> _readonlyView;

        /// <summary>初始化時立即執行一次列舉。</summary>
        public MonitorEnumerator()
        {
            Refresh();
        }

        /// <summary>
        /// 重新向 Win32 列舉所有螢幕並更新快取。
        /// 應在 WM_DISPLAYCHANGE 訊息收到後呼叫。
        /// </summary>
        public void Refresh()
        {
            _monitors.Clear();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 委託生命週期需跨越 EnumDisplayMonitors 呼叫範圍，宣告於區域以確保 GC 不提前回收
            Win32Native.MonitorEnumProc enumProc = OnMonitorEnum;
            Win32Native.EnumDisplayMonitors(
                System.IntPtr.Zero, System.IntPtr.Zero,
                enumProc, System.IntPtr.Zero);
#else
            // 非 Win32 Standalone Build 回傳一個預設主螢幕
            AppendFallbackMonitor();
#endif

            if (_monitors.Count == 0)
            {
                // 極端情況：無任何螢幕（CI / headless）—— 插入假 MonitorInfo（FSD §7 EC）
                AppendFallbackMonitor();
            }

            // 刷新唯讀視圖快取
            _readonlyView = _monitors;
        }

        /// <summary>回傳所有已列舉螢幕的唯讀清單（含快取）。</summary>
        public IReadOnlyList<MonitorInfo> GetAll() => _readonlyView ?? _monitors;

        /// <summary>
        /// 以 monitorID 尋找螢幕。
        /// </summary>
        /// <param name="monitorID">目標 monitorID（HMONITOR.ToInt32()）</param>
        /// <returns>找到則回傳對應 MonitorInfo；否則回傳 null。</returns>
        public MonitorInfo? FindByID(int monitorID)
        {
            foreach (MonitorInfo m in _monitors)
            {
                if (m.MonitorID == monitorID)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>
        /// 取得主螢幕（IsPrimary == true）。
        /// 若清單內無主螢幕標記（異常狀況），回傳第一個螢幕。
        /// </summary>
        public MonitorInfo GetPrimary()
        {
            foreach (MonitorInfo m in _monitors)
            {
                if (m.IsPrimary)
                {
                    return m;
                }
            }

            // 安全 fallback：無主螢幕標記時取第一個
            if (_monitors.Count > 0)
            {
                return _monitors[0];
            }

            // 理論上不發生（Refresh 已確保至少一個 fallback monitor）
            return BuildFallbackMonitor();
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>EnumDisplayMonitors 回呼：每個螢幕呼叫一次，將結果加入清單。</summary>
        private bool OnMonitorEnum(
            System.IntPtr hMonitor,
            System.IntPtr hdcMonitor,
            ref Rect lprcMonitor,
            System.IntPtr dwData)
        {
            MonitorInfoEx info = new MonitorInfoEx();
            // cbSize 必須在呼叫前設為結構大小
            info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfoEx));

            if (!Win32Native.GetMonitorInfo(hMonitor, ref info))
            {
                Debug.LogWarning($"[P-01] GetMonitorInfo 失敗：hMonitor={hMonitor}");
                return true; // 繼續列舉其餘螢幕
            }

            bool isPrimary = (info.dwFlags & MonitorInfoFlag.Primary) != 0;
            int  monitorID = hMonitor.ToInt32();

            _monitors.Add(new MonitorInfo(
                monitorID:   monitorID,
                displayName: info.szDevice ?? string.Empty,
                isPrimary:   isPrimary,
                isConnected: true, // EnumDisplayMonitors 結果一律連線
                workLeft:    info.rcWork.Left,
                workTop:     info.rcWork.Top,
                workRight:   info.rcWork.Right,
                workBottom:  info.rcWork.Bottom));

            return true; // 繼續列舉
        }
#endif

        /// <summary>
        /// 插入預設 fallback 螢幕（極端情況 / Editor mode）。
        /// 依 FSD §7 EC：無顯示器環境以 1920×1080 預設值 fallback。
        /// </summary>
        private void AppendFallbackMonitor()
        {
            _monitors.Add(BuildFallbackMonitor());
        }

        private static MonitorInfo BuildFallbackMonitor()
        {
            return new MonitorInfo(
                monitorID:   0,
                displayName: "(no display)",
                isPrimary:   true,
                isConnected: false,
                workLeft:    0,
                workTop:     0,
                workRight:   1920,
                workBottom:  1080);
        }
    }
}
