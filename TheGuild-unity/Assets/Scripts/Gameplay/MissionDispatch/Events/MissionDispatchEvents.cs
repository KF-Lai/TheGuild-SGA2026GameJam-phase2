namespace TheGuild.Gameplay.MissionDispatch.Events
{
    /// <summary>
    /// FT-02-A 派遣接受委託事件。
    /// GDD §3.10；FSD §5.2。
    /// Dispatch step 6 發布，先於 C-02 UpdateStatus。
    /// </summary>
    public readonly struct OnCommissionAcceptedEvent
    {
        public OnCommissionAcceptedEvent(int missionID, int baseReward, DispatchSource source)
        {
            MissionID = missionID;
            BaseReward = baseReward;
            Source = source;
        }

        /// <summary>FK → MissionTemplate（C-01）。</summary>
        public int MissionID { get; }

        /// <summary>派遣時快照的基礎獎勵（來自 C-01.GetBaseReward）。</summary>
        public int BaseReward { get; }

        /// <summary>派遣來源（玩家手動 / NPC 自主 / 離線）。</summary>
        public DispatchSource Source { get; }
    }

    /// <summary>
    /// FT-02-A 冒險者已派遣事件。
    /// GDD §3.10；FSD §5.2。
    /// Dispatch step 8 發布。
    /// </summary>
    public readonly struct OnAdventurerDispatchedEvent
    {
        public OnAdventurerDispatchedEvent(int instanceID, int activeMissionID)
        {
            InstanceID = instanceID;
            ActiveMissionID = activeMissionID;
        }

        /// <summary>FK → AdventurerInstance（C-02）。</summary>
        public int InstanceID { get; }

        /// <summary>新建立的 ActiveMission ID。</summary>
        public int ActiveMissionID { get; }
    }

    /// <summary>
    /// FT-02-A 任務完成事件。
    /// GDD §3.10；FSD §5.2。
    /// TickCompletionCheck 首次偵測到期時發布；每筆任務僅發一次（去重）。
    /// </summary>
    public readonly struct OnMissionCompletedEvent
    {
        public OnMissionCompletedEvent(int activeMissionID)
        {
            ActiveMissionID = activeMissionID;
        }

        /// <summary>已完成的 ActiveMission ID。</summary>
        public int ActiveMissionID { get; }
    }

    /// <summary>
    /// FT-02-A 任務取消事件。
    /// GDD §3.10；FSD §5.2。
    /// RestoreFromSave 驗證 adventurerInstanceID 找不到時發布。
    /// </summary>
    public readonly struct OnMissionCancelledEvent
    {
        public OnMissionCancelledEvent(int activeMissionID)
        {
            ActiveMissionID = activeMissionID;
        }

        /// <summary>被取消的 ActiveMission ID。</summary>
        public int ActiveMissionID { get; }
    }

    /// <summary>
    /// FT-02-A 離線任務批次處理完成事件。
    /// GDD §3.10；FSD §5.2。
    /// HandleOfflineResolved 計算完成後發布，供離線摘要 UI 訂閱。
    /// </summary>
    public readonly struct OnOfflineMissionsResolvedEvent
    {
        public OnOfflineMissionsResolvedEvent(long offlineSeconds, int completedCount, int[] completedActiveMissionIDs)
        {
            OfflineSeconds = offlineSeconds;
            CompletedCount = completedCount;
            CompletedActiveMissionIDs = completedActiveMissionIDs;
        }

        /// <summary>離線秒數。</summary>
        public long OfflineSeconds { get; }

        /// <summary>離線期間完成的任務數量。</summary>
        public int CompletedCount { get; }

        /// <summary>離線期間完成的 ActiveMission ID 陣列。</summary>
        public int[] CompletedActiveMissionIDs { get; }
    }
}
