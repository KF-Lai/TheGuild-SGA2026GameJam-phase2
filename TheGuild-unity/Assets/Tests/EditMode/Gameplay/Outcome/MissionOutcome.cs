// === v3.1 patch P3.1-003 ===
// MissionOutcome：測試用輕量 DTO，供 v31 prefix 測試驗證 isScriptedDeath 哨兵值與概念邏輯。
// 注意：此類別僅供 EditMode 測試使用，不屬於生產代碼路徑。
// 生產結算 DTO 為 TheGuild.Gameplay.Outcome.Outcome（OutcomeData.cs）。

namespace Tests.EditMode.Gameplay.Outcome
{
    /// <summary>
    /// 測試用任務結算快照（v3.1 stub）。
    /// 供 OutcomeResolutionTests v31 prefix 測試驗證 isScriptedDeath 哨兵值與過濾邏輯概念。
    /// </summary>
    internal sealed class MissionOutcome
    {
        /// <summary>任務執行期 ID（對應 ActiveMission.activeMissionID）。</summary>
        public int missionInstanceID;

        /// <summary>世界危險度等級字串（E/D/C/B/A）。</summary>
        public string dangerLevel;

        /// <summary>成功擲骰值；isScriptedDeath=true 時設為 -1.0f 哨兵值（正常範圍 [0,1)）。</summary>
        public float successRoll;

        /// <summary>死亡擲骰值；isScriptedDeath=true 時設為 -1.0f 哨兵值（正常範圍 [0,1)）。</summary>
        public float deathRoll;

        /// <summary>
        /// 是否為劇本強制必死任務（對應 MissionTemplate.isScriptedDeath == 1）。
        /// true 時 FT-04 RollSuccessAndDeath 完全跳過擲骰，successRoll / deathRoll 設哨兵 -1.0f。
        /// </summary>
        public bool isScriptedDeath;
    }
}
