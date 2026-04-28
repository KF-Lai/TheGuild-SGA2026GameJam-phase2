namespace TheGuild.Gameplay.WorldDanger
{
    /// <summary>
    /// WorldDangerTable.csv 對應 DTO（13 欄位）。
    /// </summary>
    public sealed class WorldDangerData
    {
        public string dangerLevel;
        public string name;
        public int timeThreshold;
        public int missionCountReq;
        public string minDifficulty;
        public int factionScoreReq;
        public int weightF_E;
        public int weightD;
        public int weightC;
        public int weightB;
        public int weightA;
        public int weightS_SSS;
        public int maxDebt;
    }
}
