using System;

namespace TheGuild.Gameplay.Outcome
{
    // === v3.1 patch P3.1-003 ===
    /// <summary>
    /// FT-09 GetCurrentStyleTagBias() 回傳型別（v3.1 stub）。
    /// FT-09 實作完成後可移至 TheGuild.Gameplay.FactionStory namespace 或共用 namespace。
    /// 依賴方（FT-04 CalcAdjustedJitter）透過 FactionStoryService.Instance null-safe 存取，
    /// FT-09 缺席時 fallback 回 StyleTag.Dark（jitter modifier 不生效）。
    /// </summary>
    public enum StyleTag
    {
        Dark,
        Mixed,
        Light
    }

    /// <summary>
    /// FT-04 任務結算最終狀態列舉。
    /// GDD §3.1 / §3.5 五種最終結果之冒險者狀態面。
    /// </summary>
    public enum OutcomeStatus
    {
        Idle,
        Wounded,
        Dead
    }

    /// <summary>
    /// FT-04 任務結算資料傳輸物件（DTO）。
    /// 全 15 個欄位對應 FSD §5.3；此物件在 OnMissionResolved 事件中傳遞給下游消費者。
    /// 注意：下游訂閱者不應修改此物件欄位（GDD §5.6 / FSD §8.3 B-01 約定）。
    /// </summary>
    public sealed class Outcome
    {
        /// <summary>對應 FT-02 ActiveMission 的執行期 ID；結算後供 RemoveActiveMission 清理用。</summary>
        public int activeMissionID;

        /// <summary>FK → C-01 MissionTemplate。</summary>
        public int missionID;

        /// <summary>FK → C-02 AdventurerInstance。</summary>
        public int adventurerInstanceID;

        /// <summary>快照自 MissionTemplate.difficulty（F～SSS）。</summary>
        public string missionDifficulty;

        /// <summary>快照自 MissionTemplate.typeID，供下游分類（FT-09 等）。</summary>
        public int missionTypeID;

        /// <summary>快照自 MissionTemplate.factionID，供 FT-09 Faction Story 消費。</summary>
        public int missionFactionID;

        /// <summary>快照自 C01.GetBaseReward(difficulty)；供 condition gold bonus 計算與 FT-05 消費。</summary>
        public int baseReward;

        /// <summary>成功擲骰值 [0, 1)，供 UI / debug 顯示。</summary>
        public float successRoll;

        /// <summary>死亡擲骰值 [0, 1)，供 UI / debug 顯示。</summary>
        public float deathRoll;

        /// <summary>實際用於 deathRoll 比較的死亡率（成功時已套用 DEATH_RATE_ON_SUCCESS_MULTIPLIER 折扣）。</summary>
        public float adjustedDeathRate;

        /// <summary>由 successRoll &lt; finalSuccessRate 判定。</summary>
        public bool isSuccess;

        /// <summary>由 deathRoll &lt; adjustedDeathRate 判定；condition 特質 on_death_survive 可將此值改為 false。</summary>
        public bool isDead;

        /// <summary>失敗+存活時為 true；condition 救活後亦設為 true。</summary>
        public bool isWounded;

        /// <summary>MapFinalStatus 由 isDead / isWounded 映射出的最終狀態。</summary>
        public OutcomeStatus finalStatus;

        /// <summary>基礎聲望（ReputationDeltaTable 查表）+ condition on_*_reputation* 疊加。</summary>
        public int reputationDelta;

        /// <summary>
        /// on_success_gold_bonus condition 觸發時累積的 bonus 金幣（倍率 × baseReward）。
        /// FT-04 不呼叫 AddGold；此值由 FT-05 從 OnMissionResolved 消費。
        /// </summary>
        public int conditionGoldBonus;

        /// <summary>
        /// 機率判定通過的 condition traitID 清單。
        /// 初始為 Array.Empty&lt;int&gt;()；ApplyConditionTraits 執行後填入觸發的 ID 列表。
        /// </summary>
        public int[] triggeredConditionTraits;

        /// <summary>
        /// 無參建構子：將 triggeredConditionTraits 初始化為空陣列。
        /// </summary>
        public Outcome()
        {
            triggeredConditionTraits = Array.Empty<int>();
        }
    }
}
