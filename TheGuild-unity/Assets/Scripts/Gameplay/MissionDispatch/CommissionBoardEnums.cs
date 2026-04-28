namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// 委託板來源（GDD §3.6a）。
    /// </summary>
    public enum CommissionSource
    {
        /// <summary>由 PostRegularMission 注入（常規生成）。</summary>
        Regular,

        /// <summary>由 InjectStaticMission 注入（FT-09 劇情靜態委託）。</summary>
        Static
    }

    /// <summary>
    /// PostRegularMission 回傳碼（FSD §5.1 / §5.4 流程 B）。
    /// </summary>
    public enum PostResult
    {
        OK,
        UNKNOWN_MISSION_ID,
        WRONG_CATEGORY,
        ALREADY_ON_BOARD
    }

    /// <summary>
    /// InjectStaticMission 回傳碼（FSD §5.1 / §5.4 流程 A）。
    /// </summary>
    public enum InjectStaticMissionResult
    {
        OK,
        UNKNOWN_MISSION_ID,
        WRONG_CATEGORY,
        ALREADY_ON_BOARD,

        /// <summary>Jam 階段不會發生；預留供未來擴展。</summary>
        BOARD_DISABLED
    }

    /// <summary>
    /// FSD §8.3 B-01 硬要求：categoryID 命名常數，禁止以 magic number 0 / 3 出現於流程碼。
    /// </summary>
    public static class CommissionCategory
    {
        public const int RegularCategoryID = 0;
        public const int StaticCategoryID = 3;
    }
}
