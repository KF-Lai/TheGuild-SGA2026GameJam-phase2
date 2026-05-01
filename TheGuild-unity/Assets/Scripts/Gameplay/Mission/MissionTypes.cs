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

        // === v3.1 patch P3.1-001 ===
        /// <summary>1 = 強制必死任務（FT-04 short-circuit 擲骰）；僅允許 categoryID=3，否則載入時重置為 0。</summary>
        public int isScriptedDeath;
        /// <summary>最小世界危險度索引（E=0, D=1, C=2, B=3, A=4）；0 = 無限制。FT-05 SelectMissionFromPool 以此過濾。</summary>
        public int minDangerLevel;
        /// <summary>派遣前置條件：FK → TraitTable.traitID；0 = 無限制。FT-02 / FT-03 消費。</summary>
        public int requiredTraitID;
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
