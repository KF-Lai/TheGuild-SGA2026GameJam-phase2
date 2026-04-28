using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Staff
{
    /// <summary>
    /// 職員狀態。
    /// </summary>
    public enum StaffState
    {
        Working = 0,
        Reallocating = 1,
        OnLeave = 2
    }

    /// <summary>
    /// 職員效果 ID（白名單）。
    /// </summary>
    public enum StaffEffect
    {
        Willingness = 0,
        AccountantCommission = 1,
        AccountantPenaltyOnVault = 2,
        RecruitRefreshOnCounter = 3
    }

    /// <summary>
    /// 職員 UI 旗標（白名單）。
    /// </summary>
    public enum StaffUIFlag
    {
        SuccessRatePreview = 0
    }

    /// <summary>
    /// HireStaff 結果碼。
    /// </summary>
    public enum HireStaffResult
    {
        OK = 0,
        STAFF_SYSTEM_LOCKED = 1,
        INVALID_STAFF_ID = 2,
        ROSTER_FULL = 3,
        DUPLICATE_INSTANCE = 4
    }

    /// <summary>
    /// TryAssignStaff 結果碼。
    /// </summary>
    public enum AssignResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        STAFF_NOT_FOUND = 2,
        BUILDING_NOT_ELIGIBLE = 3,
        BUILDING_FULL = 4,
        STAFF_ON_LEAVE = 5,
        SWITCH_COOLDOWN = 6
    }

    /// <summary>
    /// TryUnassignStaff 結果碼。
    /// </summary>
    public enum UnassignResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        STAFF_NOT_FOUND = 2,
        STAFF_NOT_ASSIGNED = 3,
        STAFF_ON_LEAVE = 4
    }

    /// <summary>
    /// TryGoOnLeave 結果碼。
    /// </summary>
    public enum GoOnLeaveResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        STAFF_NOT_FOUND = 2,
        ALREADY_ON_LEAVE = 3
    }

    /// <summary>
    /// TryReturnFromLeave 結果碼。
    /// </summary>
    public enum ReturnFromLeaveResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        STAFF_NOT_FOUND = 2,
        NOT_ON_LEAVE = 3
    }

    /// <summary>
    /// TryFireStaff 結果碼。
    /// </summary>
    public enum TryFireStaffResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        STAFF_NOT_FOUND = 2
    }

    /// <summary>
    /// 職員錄用事件。
    /// </summary>
    public readonly struct OnStaffHiredEvent
    {
        public readonly int InstanceID;
        public readonly int StaffID;
        public readonly long HiredTimestamp;

        public OnStaffHiredEvent(int instanceID, int staffID, long hiredTimestamp)
        {
            InstanceID = instanceID;
            StaffID = staffID;
            HiredTimestamp = hiredTimestamp;
        }
    }

    /// <summary>
    /// 職員解雇事件。
    /// </summary>
    public readonly struct OnStaffFiredEvent
    {
        public readonly int InstanceID;
        public readonly int StaffID;
        public readonly long FiredTimestamp;
        public readonly int SeverancePaid;

        public OnStaffFiredEvent(int instanceID, int staffID, long firedTimestamp, int severancePaid)
        {
            InstanceID = instanceID;
            StaffID = staffID;
            FiredTimestamp = firedTimestamp;
            SeverancePaid = severancePaid;
        }
    }

    /// <summary>
    /// 職員指派事件。
    /// </summary>
    public readonly struct OnStaffAssignedEvent
    {
        public readonly int InstanceID;
        public readonly int OldBuildingID;
        public readonly int NewBuildingID;

        public OnStaffAssignedEvent(int instanceID, int oldBuildingID, int newBuildingID)
        {
            InstanceID = instanceID;
            OldBuildingID = oldBuildingID;
            NewBuildingID = newBuildingID;
        }
    }

    /// <summary>
    /// 職員狀態變更事件。
    /// </summary>
    public readonly struct OnStaffStateChangedEvent
    {
        public readonly int InstanceID;
        public readonly StaffState OldState;
        public readonly StaffState NewState;

        public OnStaffStateChangedEvent(int instanceID, StaffState oldState, StaffState newState)
        {
            InstanceID = instanceID;
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// 薪水到期事件（Phase 2）。
    /// </summary>
    public readonly struct OnStaffSalaryDueEvent
    {
        public readonly long DueTimestamp;
        public readonly Dictionary<int, int> PerStaffSalary;
        public readonly int TotalAmount;

        public OnStaffSalaryDueEvent(long dueTimestamp, Dictionary<int, int> perStaffSalary, int totalAmount)
        {
            DueTimestamp = dueTimestamp;
            PerStaffSalary = perStaffSalary;
            TotalAmount = totalAmount;
        }
    }

    /// <summary>
    /// StaffTable 原始資料列。
    /// </summary>
    [Serializable]
    public class StaffData
    {
        public int staffID;
        public string name;
        public int rarity;
        public int salary;
        public int severancePay;
        public bool isFiller;
        public int factionID;
        public int minGuildLevel;
        public string[] effectIDs;
        public float[] effectValues;
        public string[] slotBuildingIDs;
        public string[] uiFlagIDs;
        public string[] uiFlagBuildingIDs;

        [NonSerialized] public List<StaffEffect> effectIDsParsed = new List<StaffEffect>(4);
        [NonSerialized] public List<float> effectValuesParsed = new List<float>(4);
        [NonSerialized] public List<int> slotBuildingIDsParsed = new List<int>(4);
        [NonSerialized] public List<StaffUIFlag> uiFlagIDsParsed = new List<StaffUIFlag>(2);
        [NonSerialized] public List<int> uiFlagBuildingIDsParsed = new List<int>(2);
    }

    /// <summary>
    /// 職員執行期實例。
    /// </summary>
    [Serializable]
    public class StaffInstance
    {
        public int instanceID;
        public int staffID;
        public StaffState currentState;
        public int assignedBuildingID;
        public long reallocatingStartTimestamp;
        public long buildingSwitchCooldownEndTimestamp;
        public long hiredTimestamp;
    }

    /// <summary>
    /// 職員狀態檢視模型。
    /// </summary>
    public struct StaffStateView
    {
        public StaffState currentState;
        public int assignedBuildingID;
        public int reallocatingRemainingSec;
        public int switchCooldownRemainingSec;
    }

    /// <summary>
    /// 候選卡片（FT-08 -> FT-12）。
    /// </summary>
    public struct CandidateCard
    {
        public int staffID;
    }

    /// <summary>
    /// Hire 回傳值。
    /// </summary>
    public struct HireResult
    {
        public HireStaffResult result;
        public int instanceID;
    }

    /// <summary>
    /// Phase 2 開關（Jam 版固定 false）。
    /// </summary>
    public static class StaffPhase2
    {
        public const bool SalaryEnabled = false;
    }

    public class StaffTableValidationException : Exception
    {
        public StaffTableValidationException(string message) : base(message) { }
    }

    public class CriticalRestoreFailedException : Exception
    {
        public CriticalRestoreFailedException(string message) : base(message) { }
    }
}
