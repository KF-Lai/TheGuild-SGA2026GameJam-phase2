namespace TheGuild.Core.Time
{
    /// <summary>
    /// 任務計時器資料。
    /// </summary>
    public readonly struct MissionTimer
    {
        /// <summary>
        /// 建立任務計時器資料。
        /// </summary>
        public MissionTimer(string missionInstanceId, long dispatchTimestamp, int durationSeconds)
        {
            MissionInstanceId = missionInstanceId;
            DispatchTimestamp = dispatchTimestamp;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// 任務實例識別碼。
        /// </summary>
        public string MissionInstanceId { get; }

        /// <summary>
        /// 任務派遣 UTC Unix 時間戳（秒）。
        /// </summary>
        public long DispatchTimestamp { get; }

        /// <summary>
        /// 任務持續秒數。
        /// </summary>
        public int DurationSeconds { get; }
    }
}
