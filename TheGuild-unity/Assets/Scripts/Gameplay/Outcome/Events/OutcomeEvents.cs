namespace TheGuild.Gameplay.Outcome.Events
{
    /// <summary>
    /// FT-04 任務結算完成事件。
    /// 在 OutcomeResolutionService 管線步驟 11 發布，先於 RemoveActiveMission（步驟 12）。
    /// FSD §5.2 / §3.8；下游消費者：FT-05 / FT-09 / P-02 / P-03。
    /// 注意：Outcome 物件為同一引用，訂閱者不得修改欄位（FSD §8.3 B-01）。
    /// </summary>
    public readonly struct OnMissionResolvedEvent
    {
        /// <summary>
        /// 建構子：注入結算結果。
        /// </summary>
        public OnMissionResolvedEvent(Outcome outcome)
        {
            Outcome = outcome;
        }

        /// <summary>本次結算的完整快照資料。</summary>
        public Outcome Outcome { get; }
    }
}
