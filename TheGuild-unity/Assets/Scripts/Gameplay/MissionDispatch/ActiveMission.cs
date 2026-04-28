using System;

namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// FT-02-A 進行中任務的執行期資料快照（純資料類別，無行為）。
    /// GDD §3.6 ActiveMission 資料結構；FSD §5.3。
    /// </summary>
    [Serializable]
    public sealed class ActiveMission
    {
        /// <summary>Runtime 唯一 ID，由 MissionDispatchService 自增分配。</summary>
        public int activeMissionID;

        /// <summary>FK → MissionTemplate（C-01）。</summary>
        public int missionID;

        /// <summary>FK → AdventurerInstance（C-02）。</summary>
        public int adventurerInstanceID;

        /// <summary>派遣時快照的最終成功率（[0, 1]）；不隨後續修正值變動。</summary>
        public float finalSuccessRate;

        /// <summary>派遣時快照的最終死亡率（[0, 1]）；不隨後續修正值變動。</summary>
        public float finalDeathRate;

        /// <summary>派遣時間（Unix 秒）。</summary>
        public long dispatchTimestamp;

        /// <summary>預計完成時間（Unix 秒）= dispatchTimestamp + duration × 60（Tech Debt TD-01：duration 為分鐘）。</summary>
        public long completionTimestamp;
    }
}
