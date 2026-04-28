using System;
using System.Collections.Generic;

// 實作依據：gacha-system.md §5.3 / §6.1

namespace TheGuild.Gameplay.Gacha
{
    /// <summary>
    /// 刷新來源分類。
    /// </summary>
    internal enum RefreshType
    {
        Auto = 0,
        Manual = 1,
        SwitchPool = 2,
        OfflineRefill = 3
    }

    /// <summary>
    /// TryManualRefresh 回傳碼。
    /// </summary>
    public enum RefreshResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        GOLD_INSUFFICIENT = 2,
        NO_REFRESHABLE_SLOT = 3
    }

    /// <summary>
    /// TrySwitchPool 回傳碼。
    /// </summary>
    public enum SwitchPoolResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        POOL_NOT_FOUND = 2,
        POOL_LEVEL_LOCKED = 3,
        ALREADY_IN_POOL = 4,
        NO_REFRESHABLE_SLOT = 5
    }

    /// <summary>
    /// TryRecruit 回傳碼。
    /// </summary>
    public enum RecruitResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        INVALID_SLOT = 2,
        EMPTY_SLOT = 3,
        CANDIDATE_NOT_HIREABLE = 4,
        ROSTER_FULL = 5,
        INVALID_STAFF_ID = 6,
        INTERNAL_ERROR = 7,
        BUSY = 8
    }

    /// <summary>
    /// TryRejectCandidate 回傳碼。
    /// </summary>
    public enum RejectResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        INVALID_SLOT = 2,
        EMPTY_SLOT = 3
    }

    /// <summary>
    /// TryReserveCandidate 回傳碼。
    /// </summary>
    public enum ReserveResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        INVALID_SLOT = 2,
        EMPTY_SLOT = 3,
        RESERVE_FULL = 4,
        RESERVE_CONSUMED = 5
    }

    /// <summary>
    /// TryReleaseReserve 回傳碼。
    /// </summary>
    public enum ReleaseResult
    {
        SUCCESS = 0,
        STAFF_SYSTEM_LOCKED = 1,
        INVALID_INDEX = 2,
        SLOT_OCCUPIED_BY_NEW_ROLL = 3
    }

    /// <summary>
    /// FT-08 玩家持久化狀態容器（FT-10 Owner JSON root）。
    /// </summary>
    [Serializable]
    public class StaffPlayerState
    {
        public int pityCounter;
        public long lastAutoRefreshTimestamp;
        public int currentPoolID;
        public List<CandidateCard> currentCandidates;
        public List<CandidateCard> reservedCandidates;
    }

    /// <summary>
    /// 候選卡資料（含 placeholder / staff / trash）。
    /// </summary>
    [Serializable]
    public class CandidateCard
    {
        public int poolID;
        public int slotIndex;
        public int staffID;
        public int trashItemID;
        public int rolledRarity;
        public long rolledTimestamp;
        public bool isReserved;
        public long reservedTimestamp;
        public bool reserveConsumedFlag;
    }

    /// <summary>
    /// StaffGachaPoolTable 對應 row。
    /// </summary>
    [Serializable]
    public class StaffGachaPoolData
    {
        public int poolID;
        public string poolName;
        public int minGuildLevel;
        public int maxGuildLevel;
        public int[] eligibleStaffIDs;
        public int[] staffWeights;
        public int reserveTimeLimitSec;
        public string storyFlagRequired;
        public int factionIDRequired;
        public int minReputation;
        public long eventStartTimestamp;
        public long eventEndTimestamp;
    }

    /// <summary>
    /// StaffRefreshCostTable 對應 row。
    /// </summary>
    [Serializable]
    public class StaffRefreshCostData
    {
        public int guildLevel;
        public int cost;
        public int interviewSlotCount;
    }

    /// <summary>
    /// StaffRarityProbTable 對應 row。
    /// </summary>
    [Serializable]
    public class StaffRarityProbData
    {
        public int rarity;
        public float prob;
    }

    /// <summary>
    /// TrashItemTable 對應 row。
    /// </summary>
    [Serializable]
    public class TrashItemData
    {
        public int trashItemID;
        public string name;
        public string flavorText;
    }

    /// <summary>
    /// StaffTuning.csv 對應 row（FT-08 消費端引用）。
    /// </summary>
    [Serializable]
    public class StaffTuningEntry
    {
        public string key;
        public string value;
        public string description;
    }

    /// <summary>
    /// 系統開機後通知 UI 的 boot payload。
    /// </summary>
    public readonly struct OnStaffSystemBootEvent
    {
        public readonly bool IsUnlocked;
        public readonly int AppliedRefreshCount;
        public readonly int ReleasedReserveCount;
        public readonly long BootTimestamp;

        public OnStaffSystemBootEvent(
            bool isUnlocked,
            int appliedRefreshCount,
            int releasedReserveCount,
            long bootTimestamp)
        {
            IsUnlocked = isUnlocked;
            AppliedRefreshCount = appliedRefreshCount;
            ReleasedReserveCount = releasedReserveCount;
            BootTimestamp = bootTimestamp;
        }
    }

    /// <summary>
    /// CandidateCard 共用判斷與 placeholder 建構工具。
    /// </summary>
    public static class CandidateCardExtensions
    {
        /// <summary>
        /// 判斷是否為空 slot（含 null）。
        /// </summary>
        public static bool IsEmptySlot(this CandidateCard card)
        {
            return card == null || (card.staffID == 0 && card.trashItemID == 0);
        }

        /// <summary>
        /// 建立 placeholder 卡片。
        /// </summary>
        public static CandidateCard MakeEmptySlot(int poolID, int slotIndex)
        {
            return new CandidateCard
            {
                poolID = poolID,
                slotIndex = slotIndex,
                staffID = 0,
                trashItemID = 0,
                rolledRarity = 0,
                rolledTimestamp = 0L,
                isReserved = false,
                reservedTimestamp = 0L,
                reserveConsumedFlag = false
            };
        }
    }

    public class StaffGachaPoolTableValidationException : Exception
    {
        public StaffGachaPoolTableValidationException(string message) : base(message) { }
    }

    public class StaffRefreshCostTableValidationException : Exception
    {
        public StaffRefreshCostTableValidationException(string message) : base(message) { }
    }

    public class CandidateCardValidationException : Exception
    {
        public CandidateCardValidationException(string message) : base(message) { }
    }

    public class StaffPlayerStateValidationException : Exception
    {
        public StaffPlayerStateValidationException(string message) : base(message) { }
    }
}
