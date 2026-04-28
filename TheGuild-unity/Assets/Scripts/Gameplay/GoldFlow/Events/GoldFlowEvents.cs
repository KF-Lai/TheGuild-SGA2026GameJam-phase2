using System.Collections.Generic;
using TheGuild.Gameplay.MissionDispatch;

namespace TheGuild.Gameplay.GoldFlow.Events
{
    public readonly struct OnCommissionPrepaidEvent
    {
        public OnCommissionPrepaidEvent(int missionID, int prepaidAmount, DispatchSource source)
        {
            MissionID = missionID;
            PrepaidAmount = prepaidAmount;
            Source = source;
        }

        public int MissionID { get; }
        public int PrepaidAmount { get; }
        public DispatchSource Source { get; }
    }

    public readonly struct OnCommissionSettledEvent
    {
        public OnCommissionSettledEvent(CommissionBreakdown breakdown)
        {
            Breakdown = breakdown;
        }

        public CommissionBreakdown Breakdown { get; }
    }

    public readonly struct OnMaintenanceChargedEvent
    {
        public OnMaintenanceChargedEvent(MaintenanceBreakdown breakdown)
        {
            Breakdown = breakdown;
        }

        public MaintenanceBreakdown Breakdown { get; }
    }

    public readonly struct OnSalaryChargedEvent
    {
        public OnSalaryChargedEvent(SalaryBreakdown breakdown)
        {
            Breakdown = breakdown;
        }

        public SalaryBreakdown Breakdown { get; }
    }

    public readonly struct OnGuildMaintenanceDueEvent
    {
        public OnGuildMaintenanceDueEvent(long dueTimestamp, Dictionary<int, int> perBuildingCost, int totalAmount)
        {
            DueTimestamp = dueTimestamp;
            PerBuildingCost = perBuildingCost;
            TotalAmount = totalAmount;
        }

        public long DueTimestamp { get; }
        public Dictionary<int, int> PerBuildingCost { get; }
        public int TotalAmount { get; }
    }
}
