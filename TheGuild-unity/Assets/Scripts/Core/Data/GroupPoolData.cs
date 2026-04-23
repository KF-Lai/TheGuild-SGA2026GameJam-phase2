using System;

namespace TheGuild.Core.Data
{
    /// <summary>
    /// 群組隨機池資料結構。
    /// </summary>
    [Serializable]
    public class GroupPoolData
    {
        public string groupID;
        public string groupName;
        public string[] memberIDs = Array.Empty<string>();
        public int pickCount = 1;
        public string pickMode = "uniform";
        public float[] weights = Array.Empty<float>();
    }
}
