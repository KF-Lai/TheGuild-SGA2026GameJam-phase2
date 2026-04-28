using System;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// 任務模板資料。
    /// </summary>
    [Serializable]
    public class MissionTemplate
    {
        public int missionID;
        public string difficulty;
        public int typeID;
        public int factionID;
        public int categoryID;
    }

    /// <summary>
    /// 任務難度資料。
    /// </summary>
    [Serializable]
    public class MissionDifficultyData
    {
        public string difficulty;
        public int baseReward;
        public int baseDuration;
        public float baseDeathRate;
        public int factionScoreDelta;
    }

    /// <summary>
    /// 任務類型資料。
    /// </summary>
    [Serializable]
    public class MissionTypeData
    {
        public int typeID;
        public string typeName;
    }

    /// <summary>
    /// 任務類別資料。
    /// </summary>
    [Serializable]
    public class MissionCategoryData
    {
        public int categoryID;
        public string categoryName;
    }

    /// <summary>
    /// 任務名稱池資料（D-02）。
    /// </summary>
    [Serializable]
    public class MissionNameData
    {
        public string difficulty;
        public int typeID;
        public string missionName;
        public string missionDesc;
    }
}
