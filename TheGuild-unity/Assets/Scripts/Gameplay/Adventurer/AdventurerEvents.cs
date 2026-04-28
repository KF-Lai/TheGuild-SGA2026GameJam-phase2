namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02 冒險者加入名冊事件。
    /// FSD §5.2：AddAdventurer 成功後發布。
    /// </summary>
    public readonly struct OnAdventurerAddedEvent
    {
        public OnAdventurerAddedEvent(int instanceID)
        {
            InstanceID = instanceID;
        }

        public int InstanceID { get; }
    }

    /// <summary>
    /// C-02 冒險者除名事件（Dead → 從名冊移除）。
    /// FSD §5.2：DismissAdventurer 成功後發布；不重複發 OnAdventurerStatusChangedEvent。
    /// </summary>
    public readonly struct OnAdventurerDismissedEvent
    {
        public OnAdventurerDismissedEvent(int instanceID)
        {
            InstanceID = instanceID;
        }

        public int InstanceID { get; }
    }

    /// <summary>
    /// C-02 冒險者傷勢恢復事件（Wounded → Idle）。
    /// FSD §5.2：TickWoundedRecovery 中狀態轉換完成時發布。
    /// </summary>
    public readonly struct OnAdventurerRecoveredEvent
    {
        public OnAdventurerRecoveredEvent(int instanceID)
        {
            InstanceID = instanceID;
        }

        public int InstanceID { get; }
    }

    /// <summary>
    /// C-02 冒險者狀態變更事件（任意轉換）。
    /// FSD §5.2：UpdateStatus / SetWounded / TickWoundedRecovery 後發布。
    /// </summary>
    public readonly struct OnAdventurerStatusChangedEvent
    {
        public OnAdventurerStatusChangedEvent(int instanceID, AdventurerStatus prev, AdventurerStatus current)
        {
            InstanceID = instanceID;
            Prev = prev;
            Current = current;
        }

        public int InstanceID { get; }
        public AdventurerStatus Prev { get; }
        public AdventurerStatus Current { get; }
    }
}
