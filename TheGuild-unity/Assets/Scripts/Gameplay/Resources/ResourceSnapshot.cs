using System;

namespace TheGuild.Gameplay.Resources
{
    /// <summary>
    /// 資源系統快照資料。
    /// </summary>
    [Serializable]
    public sealed class ResourceSnapshot
    {
        public int CurrentGold;
        public int CurrentReputation;
        public BankruptcyWarningState WarningState;
        public long BankruptcyWarningStartTime;
        public long WarningDurationSec;
        public int CurrentBankruptcyThreshold;
    }
}
