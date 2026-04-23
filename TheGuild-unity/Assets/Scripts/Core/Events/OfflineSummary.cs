using System.Collections.Generic;

namespace TheGuild.Core.Events
{
    /// <summary>
    /// 離線摘要資料。
    /// </summary>
    public readonly struct OfflineSummary
    {
        public OfflineSummary(
            long offlineSeconds,
            int completedCount,
            IReadOnlyList<string> completedMissionInstanceIds,
            bool crossesDailyReset)
        {
            OfflineSeconds = offlineSeconds;
            CompletedCount = completedCount;
            CompletedMissionInstanceIds = completedMissionInstanceIds;
            CrossesDailyReset = crossesDailyReset;
        }

        public long OfflineSeconds { get; }
        public int CompletedCount { get; }
        public IReadOnlyList<string> CompletedMissionInstanceIds { get; }
        public bool CrossesDailyReset { get; }
    }
}
