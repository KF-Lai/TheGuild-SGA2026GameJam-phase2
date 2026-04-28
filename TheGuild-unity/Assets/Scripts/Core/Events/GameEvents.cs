namespace TheGuild.Core.Events
{
    /// <summary>
    /// 每秒 Tick 事件。
    /// </summary>
    public readonly struct OnSecondTickEvent
    {
        public OnSecondTickEvent(long nowUtc)
        {
            NowUTC = nowUtc;
        }

        public long NowUTC { get; }
    }

    /// <summary>
    /// 每分鐘 Tick 事件。
    /// </summary>
    public readonly struct OnMinuteTickEvent
    {
        public OnMinuteTickEvent(long nowUtc)
        {
            NowUTC = nowUtc;
        }

        public long NowUTC { get; }
    }

    /// <summary>
    /// 離線摘要已準備完成，等待玩家確認。
    /// </summary>
    public readonly struct OnOfflinePendingEvent
    {
        public OnOfflinePendingEvent(OfflineSummary summary)
        {
            Summary = summary;
        }

        public OfflineSummary Summary { get; }
    }

    /// <summary>
    /// 玩家確認離線摘要後，離線結算完成。
    /// FSD-A D-02：payload 簡化為僅 OfflineSeconds，任務完成數由 FT-02-A 訂閱後計算並發 OnOfflineMissionsResolvedEvent。
    /// </summary>
    public readonly struct OnOfflineResolvedEvent
    {
        public OnOfflineResolvedEvent(long offlineSeconds)
        {
            OfflineSeconds = offlineSeconds;
        }

        public long OfflineSeconds { get; }
    }

    /// <summary>
    /// 無 payload 事件名稱常數。
    /// </summary>
    public static class EventNames
    {
        public const string OnDailyReset = "OnDailyReset";
    }
}
