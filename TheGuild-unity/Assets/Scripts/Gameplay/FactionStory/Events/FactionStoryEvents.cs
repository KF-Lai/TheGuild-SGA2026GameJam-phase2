namespace TheGuild.Gameplay.FactionStory.Events
{
    // === v3.1 patch P3.1-004：新增事件 ===

    /// <summary>
    /// Stage 5 任務結算後發布，帶入二次解析的 epilogue dialogue key。
    /// FT-09 §3.6.2 Step 10 / §3.6.10。
    /// </summary>
    public readonly struct OnFactionStoryStageEpilogueEvent
    {
        public OnFactionStoryStageEpilogueEvent(
            int stageID,
            int factionID,
            string resolvedEpilogueKey,
            bool isOpheliaEpilogue,
            bool subjectAlive)
        {
            StageID = stageID;
            FactionID = factionID;
            ResolvedEpilogueKey = resolvedEpilogueKey;
            IsOpheliaEpilogue = isOpheliaEpilogue;
            SubjectAlive = subjectAlive;
        }

        public int StageID { get; }
        public int FactionID { get; }
        public string ResolvedEpilogueKey { get; }
        public bool IsOpheliaEpilogue { get; }
        public bool SubjectAlive { get; }
    }

    /// <summary>
    /// Stage 4 確認後，奧菲莉雅失蹤事件。FT-09 §3.6.9。
    /// </summary>
    public readonly struct OnOpheliaMissingNightEvent
    {
        public OnOpheliaMissingNightEvent(int stageID)
        {
            StageID = stageID;
        }

        public int StageID { get; }
    }

    /// <summary>
    /// 奧菲莉雅傷勢恢復後，回歸事件。FT-09 §3.6.9。
    /// </summary>
    public readonly struct OnOpheliaReturnedEvent
    {
        public OnOpheliaReturnedEvent(int adventurerInstanceID)
        {
            AdventurerInstanceID = adventurerInstanceID;
        }

        public int AdventurerInstanceID { get; }
    }


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
