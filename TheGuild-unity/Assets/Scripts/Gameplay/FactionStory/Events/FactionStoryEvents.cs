namespace TheGuild.Gameplay.FactionStory.Events
{
    public readonly struct OnFactionScoreChangedEvent
    {
        public OnFactionScoreChangedEvent(int factionID, int oldScore, int newScore, int delta)
        {
            FactionID = factionID;
            OldScore = oldScore;
            NewScore = newScore;
            Delta = delta;
        }

        public int FactionID { get; }
        public int OldScore { get; }
        public int NewScore { get; }
        public int Delta { get; }
    }

    public readonly struct OnFactionStoryStageUnlockedEvent
    {
        public OnFactionStoryStageUnlockedEvent(int stageID, int factionID, int stageIndex, int missionID, string dialogueKey)
        {
            StageID = stageID;
            FactionID = factionID;
            StageIndex = stageIndex;
            MissionID = missionID;
            DialogueKey = dialogueKey;
        }

        public int StageID { get; }
        public int FactionID { get; }
        public int StageIndex { get; }
        public int MissionID { get; }
        public string DialogueKey { get; }
    }

    public readonly struct OnFactionStoryDialogueConfirmedEvent
    {
        public OnFactionStoryDialogueConfirmedEvent(int stageID, int factionID, int missionID)
        {
            StageID = stageID;
            FactionID = factionID;
            MissionID = missionID;
        }

        public int StageID { get; }
        public int FactionID { get; }
        public int MissionID { get; }
    }

    public readonly struct OnFactionStoryStageResolvedEvent
    {
        public OnFactionStoryStageResolvedEvent(int stageID, int factionID, int stageIndex, int missionID, bool isSuccess, bool isDead)
        {
            StageID = stageID;
            FactionID = factionID;
            StageIndex = stageIndex;
            MissionID = missionID;
            IsSuccess = isSuccess;
            IsDead = isDead;
        }

        public int StageID { get; }
        public int FactionID { get; }
        public int StageIndex { get; }
        public int MissionID { get; }
        public bool IsSuccess { get; }
        public bool IsDead { get; }
    }

    public readonly struct OnFactionRouteCompletedEvent
    {
        public OnFactionRouteCompletedEvent(int factionID, int finalStageID, bool finalIsSuccess, int totalStages)
        {
            FactionID = factionID;
            FinalStageID = finalStageID;
            FinalIsSuccess = finalIsSuccess;
            TotalStages = totalStages;
        }

        public int FactionID { get; }
        public int FinalStageID { get; }
        public bool FinalIsSuccess { get; }
        public int TotalStages { get; }
    }
}
