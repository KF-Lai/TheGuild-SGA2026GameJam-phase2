using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02 冒險者狀態列舉。
    /// GDD §3.4 狀態機轉換規則。
    /// </summary>
    public enum AdventurerStatus
    {
        Idle,
        Dispatched,
        Wounded,
        Dead
    }

    /// <summary>
    /// C-02 冒險者 runtime 實例（不對應 CSV，由 FT-10 序列化）。
    /// GDD §3.1 AdventurerInstance 資料結構。
    /// </summary>
    [Serializable]
    public sealed class AdventurerInstance
    {
        /// <summary>Runtime 唯一 ID，由 AdventurerRoster 自增分配；0 = null sentinel。</summary>
        public int instanceID;

        /// <summary>來源模板 ID；0 = 隨機生成（無固定模板）。</summary>
        public int templateID;

        /// <summary>顯示名稱（來自模板或隨機生成）。</summary>
        public string name;

        /// <summary>階級字串：F / E / D / C / B / A / S。</summary>
        public string rank;

        /// <summary>FK → ProfessionTable（C-03）。</summary>
        public int professionID;

        /// <summary>FK → RaceTable（C-04）。</summary>
        public int raceID;

        /// <summary>FK → TraitTable（C-05），固定 + 隨機抽取的合集。</summary>
        public int[] traitIDs;

        /// <summary>陣營歸屬；隨機生成的冒險者填 0（neutral）。</summary>
        public int factionID;

        /// <summary>當前狀態。</summary>
        public AdventurerStatus status;

        /// <summary>派遣中的任務 instanceID；非 Dispatched 時為 0。</summary>
        public int currentMissionID;

        /// <summary>Unix timestamp（秒），Wounded 恢復截止時間；非 Wounded 時為 0。</summary>
        public long woundedUntilTimestamp;

        /// <summary>進入 Idle 狀態時的 UTC timestamp；非 Idle 時為 0。</summary>
        public long idleSinceTimestamp;

        /// <summary>FT-03 寫入；最近一次自主接單的 UTC timestamp；初始為 0。</summary>
        public long lastAutoPickupTimestamp;
    }

    /// <summary>
    /// C-02 冒險者模板（對應 AdventurerTemplate.csv）。
    /// GDD §3.2 AdventurerTemplate 資料表。
    /// </summary>
    public sealed class AdventurerTemplate
    {
        public int templateID;
        public string name;
        public string rank;
        public int professionID;

        /// <summary>0 = 依職業隨機（C-04 RollRace）。</summary>
        public int raceID;

        /// <summary>'|' 分隔；0 為 null sentinel；由 Loader 轉為 int[]。</summary>
        public string[] fixedTraitIDs;

        /// <summary>'|' 分隔；0 為 null sentinel；由 Loader 轉為 int[]。</summary>
        public string[] randomTraitGroupIDs;

        public int factionID;

        /// <summary>1 = 唯一角色（全局只能實例化一次）；0 = 可重複。</summary>
        public int isUnique;
    }

    /// <summary>
    /// C-02 招募費用條目（對應 RecruitCostTable.csv）。
    /// GDD §3.3 RecruitCostTable。
    /// </summary>
    public sealed class RecruitCostEntry
    {
        /// <summary>PK：F / E / D / C / B / A / S。</summary>
        public string rank;

        /// <summary>招募金幣費用。</summary>
        public int cost;

        /// <summary>最低聲望門檻。</summary>
        public int reputationReq;
    }
}
