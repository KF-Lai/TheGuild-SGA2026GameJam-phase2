namespace TheGuild.Gameplay.MissionDispatch.Events
{
    /// <summary>
    /// 委託板新增任務事件（FSD §5.2 / GDD §3.9.5）。
    /// PostRegularMission 或 InjectStaticMission 成功後發布。
    /// </summary>
    public readonly struct OnCommissionPostedEvent
    {
        public OnCommissionPostedEvent(int missionID, CommissionSource source)
        {
            MissionID = missionID;
            Source = source;
        }

        public int MissionID { get; }
        public CommissionSource Source { get; }
    }
}
