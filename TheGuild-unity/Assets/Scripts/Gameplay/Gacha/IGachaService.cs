using System.Collections.Generic;

// 實作依據：gacha-system.md §5.1
// TODO(FT-10)：Save/Load 系統整合後改由 `: ISaveable` 正式繼承，
// 並移除重複宣告（OwnerKey / IsCritical / Serialize / RestoreFromSave / InitializeAsNewGame）。

namespace TheGuild.Gameplay.Gacha
{
    /// <summary>
    /// FT-08 Gacha 對外服務契約。
    /// </summary>
    public interface IGachaService
    {
        // ISaveable 衍生簽名（FT-10 前暫留）

        /// <summary>存檔 owner key。</summary>
        string OwnerKey { get; }

        /// <summary>是否屬於 critical owner。</summary>
        bool IsCritical { get; }

        /// <summary>序列化 Owner JSON。</summary>
        string Serialize();

        /// <summary>由存檔資料還原狀態。</summary>
        void RestoreFromSave(string ownerJson);

        /// <summary>初始化新遊戲狀態。</summary>
        void InitializeAsNewGame();

        // 玩家操作 API

        /// <summary>玩家手動刷新候選卡。</summary>
        RefreshResult TryManualRefresh();

        /// <summary>切換當前卡池並觸發刷新。</summary>
        SwitchPoolResult TrySwitchPool(int newPoolID);

        /// <summary>嘗試錄用指定 slot 候選卡。</summary>
        RecruitResult TryRecruit(int slotIndex);

        /// <summary>拒絕指定 slot 候選卡。</summary>
        RejectResult TryRejectCandidate(int slotIndex);

        /// <summary>保留指定 slot 候選卡。</summary>
        ReserveResult TryReserveCandidate(int slotIndex);

        /// <summary>釋放保留區指定索引候選卡。</summary>
        ReleaseResult TryReleaseReserve(int reserveIndex);

        // Query API

        /// <summary>系統是否解鎖（轉發 FT-07）。</summary>
        bool IsStaffSystemUnlocked();

        /// <summary>取得目前卡池 ID。</summary>
        int GetCurrentPoolID();

        /// <summary>取得目前候選卡清單（含 placeholder）。</summary>
        IReadOnlyList<CandidateCard> GetCurrentCandidates();

        /// <summary>取得保留候選卡清單。</summary>
        IReadOnlyList<CandidateCard> GetReservedCandidates();

        /// <summary>取得當前保底計數。</summary>
        int GetPityCounter();

        /// <summary>取得當前面試槽數 N。</summary>
        int GetInterviewSlotCount();

        /// <summary>取得手動刷新花費。</summary>
        int GetManualRefreshCost();

        /// <summary>取得下次 auto refresh 的 UTC timestamp。</summary>
        long GetNextAutoRefreshUtcTimestamp();
    }
}
