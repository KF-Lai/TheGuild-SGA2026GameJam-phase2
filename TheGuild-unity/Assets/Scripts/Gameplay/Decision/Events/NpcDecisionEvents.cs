namespace TheGuild.Gameplay.Decision.Events
{
    public readonly struct OnAutoPickupEvent
    {
        public OnAutoPickupEvent(int adventurerInstanceID, int missionID)
        {
            AdventurerInstanceID = adventurerInstanceID;
            MissionID = missionID;
        }

        public int AdventurerInstanceID { get; }
        public int MissionID { get; }
    }
}
