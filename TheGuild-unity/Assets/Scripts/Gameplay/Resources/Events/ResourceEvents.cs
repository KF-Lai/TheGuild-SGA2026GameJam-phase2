using TheGuild.Gameplay.Resources;

namespace TheGuild.Gameplay.Resources.Events
{
    /// <summary>
    /// 金幣變動事件。
    /// </summary>
    public readonly struct OnGoldChangedEvent
    {
        public OnGoldChangedEvent(int previousGold, int currentGold, int delta)
        {
            PreviousGold = previousGold;
            CurrentGold = currentGold;
            Delta = delta;
        }

        public int PreviousGold { get; }
        public int CurrentGold { get; }
        public int Delta { get; }
    }

    /// <summary>
    /// 聲望變動事件。
    /// </summary>
    public readonly struct OnReputationChangedEvent
    {
        public OnReputationChangedEvent(int previousReputation, int currentReputation, int delta)
        {
            PreviousReputation = previousReputation;
            CurrentReputation = currentReputation;
            Delta = delta;
        }

        public int PreviousReputation { get; }
        public int CurrentReputation { get; }
        public int Delta { get; }
    }

    /// <summary>
    /// 破產狀態變更事件。
    /// </summary>
    public readonly struct OnBankruptcyStateChangedEvent
    {
        public OnBankruptcyStateChangedEvent(BankruptcyWarningState previousState, BankruptcyWarningState currentState)
        {
            PreviousState = previousState;
            CurrentState = currentState;
        }

        public BankruptcyWarningState PreviousState { get; }
        public BankruptcyWarningState CurrentState { get; }
    }
}
