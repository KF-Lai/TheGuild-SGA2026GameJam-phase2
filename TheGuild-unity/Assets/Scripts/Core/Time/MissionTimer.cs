namespace TheGuild.Core.Time
{
    /// <summary>
    /// 任務計時器資料。
    /// </summary>
    public readonly struct MissionTimer
    {
        public MissionTimer(string missionInstanceId, long dispatchTimestamp, int durationSeconds)
        {
            MissionInstanceId = missionInstanceId;
            DispatchTimestamp = dispatchTimestamp;
            DurationSeconds = durationSeconds;
        }

        public string MissionInstanceId { get; }
        public long DispatchTimestamp { get; }
        public int DurationSeconds { get; }
    }
}
