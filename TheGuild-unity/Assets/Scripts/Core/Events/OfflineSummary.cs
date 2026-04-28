namespace TheGuild.Core.Events
{
    /// <summary>
    /// 離線摘要資料。
    /// FSD-A D-01：移除 CompletedCount / CompletedMissionInstanceIds，任務完成清單由 FT-02-A 自行掃描 _activeMissions。
    /// </summary>
    public readonly struct OfflineSummary
    {
        public OfflineSummary(long offlineSeconds, bool crossesDailyReset)
        {
            OfflineSeconds = offlineSeconds;
            CrossesDailyReset = crossesDailyReset;
        }

        public long OfflineSeconds { get; }
        public bool CrossesDailyReset { get; }
    }
}
