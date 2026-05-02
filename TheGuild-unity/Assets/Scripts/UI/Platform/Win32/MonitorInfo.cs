// MonitorInfo readonly struct — 表示單一螢幕資訊快照（GDD §3.8.2 / FSD §5.3）。
// 純資料型別，無行為；由 MonitorEnumerator 建立，由 DesktopWindowService 對外暴露。

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// 單一螢幕資訊（不可變快照）。
    /// <para>欄位定義依 GDD §3.8.2 / FSD §5.3。</para>
    /// </summary>
    public readonly struct MonitorInfo
    {
        /// <summary>
        /// 螢幕唯一識別碼（HMONITOR.ToInt32()）。
        /// 警告：HMONITOR 在解析度變更後可能改變；FT-10 還原時以此 ID 比對，找不到則 fallback 主螢幕。
        /// </summary>
        public readonly int MonitorID;

        /// <summary>
        /// 螢幕顯示名稱（例 "\\.\DISPLAY1"）。
        /// 由 GetMonitorInfo MONITORINFOEX.szDevice 取得。
        /// </summary>
        public readonly string DisplayName;

        /// <summary>是否為系統主螢幕（MONITORINFOF_PRIMARY）。</summary>
        public readonly bool IsPrimary;

        /// <summary>
        /// 是否仍連線中。
        /// EnumDisplayMonitors 結果一律為 true；FT-10 還原時已斷線螢幕的 fallback 以此判斷。
        /// </summary>
        public readonly bool IsConnected;

        /// <summary>螢幕可用桌面矩形（已排除工作列）。</summary>
        public readonly int WorkLeft;
        public readonly int WorkTop;
        public readonly int WorkRight;
        public readonly int WorkBottom;

        /// <summary>可用桌面寬度（WorkRight - WorkLeft）。</summary>
        public int WorkWidth  => WorkRight - WorkLeft;

        /// <summary>可用桌面高度（WorkBottom - WorkTop）。</summary>
        public int WorkHeight => WorkBottom - WorkTop;

        /// <summary>建立 MonitorInfo 實例。</summary>
        public MonitorInfo(
            int monitorID,
            string displayName,
            bool isPrimary,
            bool isConnected,
            int workLeft,
            int workTop,
            int workRight,
            int workBottom)
        {
            MonitorID    = monitorID;
            DisplayName  = displayName;
            IsPrimary    = isPrimary;
            IsConnected  = isConnected;
            WorkLeft     = workLeft;
            WorkTop      = workTop;
            WorkRight    = workRight;
            WorkBottom   = workBottom;
        }

        /// <summary>回傳供 Debug 用的摘要字串。</summary>
        public override string ToString()
        {
            return $"Monitor[{MonitorID}] \"{DisplayName}\" primary={IsPrimary} connected={IsConnected} " +
                   $"workArea=({WorkLeft},{WorkTop})-({WorkRight},{WorkBottom})";
        }
    }
}
