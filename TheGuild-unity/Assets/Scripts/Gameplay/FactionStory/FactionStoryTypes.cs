using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.FactionStory
{
    public static class FactionStoryConstants
    {
        public const int FACTION_STORY_CATEGORY_ID = 3;
        public const string OWNER_KEY = "factionStorySaveData";
        public const string FACTION_NEUTRAL_ID_KEY = "FACTION_NEUTRAL_ID";
    }

    public enum ConfirmDialogueResult
    {
        OK = 0,
        STORY_SYSTEM_DISABLED = 1,
        INVALID_STAGE_ID = 2,
        INJECT_FAILED = 3
    }

    [Serializable]
    public class FactionRouteData
    {
        public int factionID;
        public string name;
        public string description;
    }

    [Serializable]
    public class StoryStageData
    {
        public int stageID;
        public int factionID;
        public int stageIndex;
        public int scoreThreshold;
        public int missionID;
        public string dialogueKey;
    }

    [Serializable]
    public class FactionScoreEntry
    {
        public int factionID;
        public int score;
    }

    [Serializable]
    public class UnlockedStageEntry
    {
        public int factionID;
        public int stageIndex;
    }

    [Serializable]
    public class PendingDialogueStageEntry
    {
        public int stageID;
    }

    [Serializable]
    public class RouteCompletedEntry
    {
        public int factionID;
    }

    [Serializable]
    public class FactionStorySaveData
    {
        public List<FactionScoreEntry> factionScores = new List<FactionScoreEntry>(8);
        public List<UnlockedStageEntry> unlockedStageIndices = new List<UnlockedStageEntry>(8);
        public List<PendingDialogueStageEntry> pendingDialogueStages = new List<PendingDialogueStageEntry>(8);
        public List<RouteCompletedEntry> routeCompletedFlags = new List<RouteCompletedEntry>(8);
    }

    public sealed class FactionStoryTableValidationException : Exception
    {
        public FactionStoryTableValidationException(string message) : base(message) { }
    }
}
