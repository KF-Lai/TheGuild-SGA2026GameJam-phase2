using System;

namespace TheGuild.Gameplay.Resources
{
    /// <summary>
    /// 資源系統快照資料。
    /// </summary>
    [Serializable]
    public sealed class ResourceSnapshot
    {
        /// <summary>
        /// 快照建立時的目前金幣。
        /// </summary>
        public int CurrentGold;
        /// <summary>
        /// 快照建立時的目前聲望。
        /// </summary>
        public int CurrentReputation;
        /// <summary>
        /// 快照建立時的破產警告狀態。
        /// </summary>
        public BankruptcyWarningState WarningState;
        /// <summary>
        /// 進入警告狀態的 UTC 秒級時間戳。
        /// </summary>
        public long BankruptcyWarningStartTime;
        /// <summary>
        /// 目前警告狀態的持續秒數設定。
        /// </summary>
        public long WarningDurationSec;
        /// <summary>
        /// 快照建立時的目前破產門檻。
        /// </summary>
        public int CurrentBankruptcyThreshold;
    }
}
