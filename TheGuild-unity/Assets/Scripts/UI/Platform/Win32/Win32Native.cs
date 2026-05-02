// 集中定義所有 Win32 P/Invoke DllImport 與 Win32 常數。
// Win32 常數屬 Windows API 規範值，不入 ScriptableObject。
// 所有 P/Invoke 宣告僅在 UNITY_STANDALONE_WIN && !UNITY_EDITOR 條件下編譯。

using System;
using System.Runtime.InteropServices;

namespace TheGuild.UI.Platform.Win32
{
    // ──────────────────────────────────────────────────────
    //  Win32 常數（GWL / GWLP 旗標）
    // ──────────────────────────────────────────────────────

    /// <summary>Win32 GWL / GWLP 索引常數。</summary>
    internal static class Gwl
    {
        // SetWindowLong / GetWindowLong 索引
        public const int Style   = -16;  // GWL_STYLE
        public const int ExStyle = -20;  // GWL_EXSTYLE

        // SetWindowLongPtr / GetWindowLongPtr 索引（64-bit 相容）
        public const int WndProc = -4;   // GWLP_WNDPROC
    }

    /// <summary>Win32 視窗樣式旗標（WS_*）。</summary>
    internal static class Ws
    {
        public const int Caption    = 0x00C00000; // WS_CAPTION（標題列）
        public const int ThickFrame = 0x00040000; // WS_THICKFRAME（可調邊框）
    }

    /// <summary>Win32 延伸視窗樣式旗標（WS_EX_*）。</summary>
    internal static class WsEx
    {
        public const int Layered  = 0x00080000; // WS_EX_LAYERED（分層視窗，color key 透明必要）
        public const int AppWindow = 0x00040000; // WS_EX_APPWINDOW（顯示於工作列）
    }

    /// <summary>Win32 SetLayeredWindowAttributes 模式旗標。</summary>
    internal static class Lwa
    {
        public const int ColorKey = 0x00000001; // LWA_COLORKEY（以 color key 設透明）
    }

    /// <summary>Win32 SetWindowPos hwndInsertAfter 特殊值。</summary>
    internal static class HwndZOrder
    {
        public static readonly IntPtr TopMost   = new IntPtr(-1); // HWND_TOPMOST
        public static readonly IntPtr NoTopMost = new IntPtr(-2); // HWND_NOTOPMOST
    }

    /// <summary>Win32 SetWindowPos 旗標（SWP_*）。</summary>
    internal static class Swp
    {
        public const uint NoSize     = 0x0001; // SWP_NOSIZE
        public const uint NoMove     = 0x0002; // SWP_NOMOVE
        public const uint NoActivate = 0x0010; // SWP_NOACTIVATE
        public const uint ShowWindow = 0x0040; // SWP_SHOWWINDOW
    }

    /// <summary>Win32 ShowWindow nCmdShow 常數。</summary>
    internal static class Sw
    {
        public const int Minimize = 6; // SW_MINIMIZE
        public const int Restore  = 9; // SW_RESTORE
    }

    /// <summary>Win32 視窗訊息代碼（WM_*）。</summary>
    internal static class Wm
    {
        public const uint NcHitTest     = 0x0084; // WM_NCHITTEST
        public const uint DisplayChange = 0x007E; // WM_DISPLAYCHANGE
        public const uint Activate      = 0x0006; // WM_ACTIVATE
    }

    /// <summary>WM_NCHITTEST 回傳值常數。</summary>
    internal static class HitTestResult
    {
        public const int Client      = 1;  // HTCLIENT（正常接收輸入）
        public const int Transparent = -1; // HTTRANSPARENT（穿透至底層視窗）
    }

    /// <summary>WM_ACTIVATE wParam 低字元值。</summary>
    internal static class WaParam
    {
        public const int Active = 1; // WA_ACTIVE（非最小化還原）
        public const int ClickActive = 2; // WA_CLICKACTIVE（點擊啟動）
    }

    /// <summary>MONITORINFO dwFlags 旗標。</summary>
    internal static class MonitorInfoFlag
    {
        public const uint Primary = 0x00000001; // MONITORINFOF_PRIMARY
    }

    // ──────────────────────────────────────────────────────
    //  Win32 struct 定義
    // ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width  => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    // MONITORINFOEX：含 szDevice（裝置名稱字串，長度 32 WCHAR = 64 bytes）
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public uint   cbSize;     // 必須在呼叫前設為 sizeof(MONITORINFOEX)
        public Rect   rcMonitor;  // 螢幕完整矩形（包含工作列）
        public Rect   rcWork;     // 可用桌面矩形（WorkingArea，已排除工作列）
        public uint   dwFlags;    // MONITORINFOF_PRIMARY 旗標
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;   // 裝置名稱（例 "\\.\DISPLAY1"）
    }

    // ──────────────────────────────────────────────────────
    //  P/Invoke 宣告（僅限 Win32 Standalone Build）
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Win32 API P/Invoke 靜態包裝類別。
    /// 所有呼叫點應套用 #if UNITY_STANDALONE_WIN && !UNITY_EDITOR 條件編譯（FSD §7 EC）。
    /// </summary>
    internal static class Win32Native
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(
            IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsIconic(IntPtr hWnd);

        /// <summary>
        /// 替換 WndProc 回呼指標。
        /// 使用 SetWindowLongPtr(GWLP_WNDPROC) 實作，需傳入 Marshal.GetFunctionPointerForDelegate 產生的 IntPtr。
        /// </summary>
        /// <param name="hWnd">目標視窗句柄</param>
        /// <param name="newWndProcPtr">新 WndProc 函式指標</param>
        /// <returns>舊 WndProc 指標（用於呼叫 DefWindowProc）</returns>
        public static IntPtr ReplaceWndProc(IntPtr hWnd, IntPtr newWndProcPtr)
        {
            return SetWindowLongPtr(hWnd, Gwl.WndProc, newWndProcPtr);
        }

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // EnumDisplayMonitors：回呼型 API，需傳入委託
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);
#endif
    }
}
