using System.Collections.Generic;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Resources;

namespace TheGuild.Gameplay.GoldFlow
{
    public readonly struct CommissionBreakdown
    {
        public CommissionBreakdown(
            int activeMissionID,
            int missionID,
            int adventurerInstanceID,
            string missionDifficulty,
            DispatchSource source,
            bool isSuccess,
            int baseReward,
            int conditionGoldBonus,
            float commissionRateBase,
            float penaltyRateBase,
            float accountantCommissionBonus,
            float accountantPenaltyBonus,
            float buildingPenaltyBonus,
            float effectiveCommissionRate,
            float effectivePenaltyRate,
            int commissionAmount,
            int penaltyAmount,
            int netDelta,
            int goldBefore,
            int goldAfter,
            BankruptcyWarningState bankruptcyStateBefore,
            BankruptcyWarningState bankruptcyStateAfter,
            long settleTimestamp)
        {
            ActiveMissionID = activeMissionID;
            MissionID = missionID;
            AdventurerInstanceID = adventurerInstanceID;
            MissionDifficulty = missionDifficulty;
            Source = source;
            IsSuccess = isSuccess;
            BaseReward = baseReward;
            ConditionGoldBonus = conditionGoldBonus;
            CommissionRateBase = commissionRateBase;
            PenaltyRateBase = penaltyRateBase;
            AccountantCommissionBonus = accountantCommissionBonus;
            AccountantPenaltyBonus = accountantPenaltyBonus;
            BuildingPenaltyBonus = buildingPenaltyBonus;
            EffectiveCommissionRate = effectiveCommissionRate;
            EffectivePenaltyRate = effectivePenaltyRate;
            CommissionAmount = commissionAmount;
            PenaltyAmount = penaltyAmount;
            NetDelta = netDelta;
            GoldBefore = goldBefore;
            GoldAfter = goldAfter;
            BankruptcyStateBefore = bankruptcyStateBefore;
            BankruptcyStateAfter = bankruptcyStateAfter;
            SettleTimestamp = settleTimestamp;
        }

        public int ActiveMissionID { get; }
        public int MissionID { get; }
        public int AdventurerInstanceID { get; }
        public string MissionDifficulty { get; }
        public DispatchSource Source { get; }
        public bool IsSuccess { get; }
        public int BaseReward { get; }
        public int ConditionGoldBonus { get; }
        public float CommissionRateBase { get; }
        public float PenaltyRateBase { get; }
        public float AccountantCommissionBonus { get; }
        public float AccountantPenaltyBonus { get; }
        public float BuildingPenaltyBonus { get; }
        public float EffectiveCommissionRate { get; }
        public float EffectivePenaltyRate { get; }
        public int CommissionAmount { get; }
        public int PenaltyAmount { get; }
        public int NetDelta { get; }
        public int GoldBefore { get; }
        public int GoldAfter { get; }
        public BankruptcyWarningState BankruptcyStateBefore { get; }
        public BankruptcyWarningState BankruptcyStateAfter { get; }
        public long SettleTimestamp { get; }
    }

    public readonly struct MaintenanceBreakdown
    {
        public MaintenanceBreakdown(
            long dueTimestamp,
            Dictionary<int, int> perBuildingCost,
            int totalAmount,
            int netDelta,
            int goldBefore,
            int goldAfter,
            BankruptcyWarningState bankruptcyStateBefore,
            BankruptcyWarningState bankruptcyStateAfter,
            long chargedTimestamp)
        {
            DueTimestamp = dueTimestamp;
            PerBuildingCost = perBuildingCost;
            TotalAmount = totalAmount;
            NetDelta = netDelta;
            GoldBefore = goldBefore;
            GoldAfter = goldAfter;
            BankruptcyStateBefore = bankruptcyStateBefore;
            BankruptcyStateAfter = bankruptcyStateAfter;
            ChargedTimestamp = chargedTimestamp;
        }

        public long DueTimestamp { get; }
        public Dictionary<int, int> PerBuildingCost { get; }
        public int TotalAmount { get; }
        public int NetDelta { get; }
        public int GoldBefore { get; }
        public int GoldAfter { get; }
        public BankruptcyWarningState BankruptcyStateBefore { get; }
        public BankruptcyWarningState BankruptcyStateAfter { get; }
        public long ChargedTimestamp { get; }
    }

    public readonly struct SalaryBreakdown
    {
        public SalaryBreakdown(
            long dueTimestamp,
            Dictionary<int, int> perStaffSalary,
            int totalAmount,
            int netDelta,
            int goldBefore,
            int goldAfter,
            BankruptcyWarningState bankruptcyStateBefore,
            BankruptcyWarningState bankruptcyStateAfter,
            long chargedTimestamp)
        {
            DueTimestamp = dueTimestamp;
            PerStaffSalary = perStaffSalary;
            TotalAmount = totalAmount;
            NetDelta = netDelta;
            GoldBefore = goldBefore;
            GoldAfter = goldAfter;
            BankruptcyStateBefore = bankruptcyStateBefore;
            BankruptcyStateAfter = bankruptcyStateAfter;
            ChargedTimestamp = chargedTimestamp;
        }

        public long DueTimestamp { get; }
        public Dictionary<int, int> PerStaffSalary { get; }
        public int TotalAmount { get; }
        public int NetDelta { get; }
        public int GoldBefore { get; }
        public int GoldAfter { get; }
        public BankruptcyWarningState BankruptcyStateBefore { get; }
        public BankruptcyWarningState BankruptcyStateAfter { get; }
        public long ChargedTimestamp { get; }
    }
}
