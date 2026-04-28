namespace TheGuild.Gameplay.Recruitment.Events
{
    public readonly struct OnPoolRefreshedEvent
    {
    }

    public readonly struct OnRecruitSuccessEvent
    {
        public OnRecruitSuccessEvent(int adventurerInstanceID, RecruitSource source)
        {
            AdventurerInstanceID = adventurerInstanceID;
            Source = source;
        }

        public int AdventurerInstanceID { get; }
        public RecruitSource Source { get; }
    }
}
