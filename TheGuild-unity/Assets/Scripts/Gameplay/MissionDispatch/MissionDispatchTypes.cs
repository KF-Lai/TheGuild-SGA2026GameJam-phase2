namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// FT-02 派遣來源列舉。
    /// GDD §3.6a；FSD §5.3。
    /// </summary>
    public enum DispatchSource
    {
        /// <summary>玩家於 P-02 委託板手動派遣。</summary>
        PlayerManual,

        /// <summary>FT-03 自主接單（runtime）。</summary>
        NpcAutoPick,

        /// <summary>FT-11 離線回補（Jam 階段未實作；保留位）。</summary>
        OfflineAutoPick
    }

    /// <summary>
    /// FT-02 成功率查詢表的 CSV 資料列 DTO。
    /// FSD §5.3；Data-Specs 【FT-02-DS】success-rate-table.md。
    /// PK = rankDiff（string，DataManager 以 string 索引）。
    /// </summary>
    public sealed class SuccessRateRow
    {
        /// <summary>PK：rankDiff 字串（"-3" ~ "3"），由 DataManager 用 string 索引。</summary>
        public string rankDiff;

        /// <summary>基礎成功率 [0, 1]；載入時超範圍則 clamp + LogWarning（FSD §7）。</summary>
        public float successRate;
    }
}
