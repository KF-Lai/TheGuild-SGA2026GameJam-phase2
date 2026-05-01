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

        // === v3.1 patch P3.1-004：StoryStageTable 新增欄位 ===
        /// <summary>對話 key 解析模式：none / styletag_bias / ophelia_alive_dead。</summary>
        public string dialogueVariantMode = "none";
        /// <summary>Stage 4 = "ophelia_missing"；解鎖確認後發布特殊事件。</summary>
        public string specialEventKey = "";
        /// <summary>Stage 5 = "npc:ophelia:status==Idle"；blocker 解析語法。</summary>
        public string unlockBlockerCondition = "";
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

        // === v3.1 patch P3.1-004：新增持久化欄位 ===
        /// <summary>Stage 4 奧菲莉雅失蹤狀態旗標。</summary>
        public bool factionStoryV31_pendingMissingNight;
        /// <summary>累積冒險者死亡計數（FB-M2）。</summary>
        public int factionStoryV31_totalAdventurerDeaths;
        /// <summary>blocker 攔截的 stageID 清單（Stage 5 等待奧菲莉雅 Idle）。</summary>
        public List<int> factionStoryV31_blockedStages = new List<int>(4);
    }

    public sealed class FactionStoryTableValidationException : Exception
    {
        public FactionStoryTableValidationException(string message) : base(message) { }
    }
}
