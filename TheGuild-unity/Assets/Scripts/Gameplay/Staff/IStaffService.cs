using System.Collections.Generic;

namespace TheGuild.Gameplay.Staff
{
    /// <summary>
    /// FT-12 職員服務介面。
    /// </summary>
    public interface IStaffService
    {
        // ── ISaveable 衍生簽章（FT-10 待整合）────────────────────────────────

        /// <summary>存檔 owner key。</summary>
        string OwnerKey { get; }

        /// <summary>是否關鍵存檔。</summary>
        bool IsCritical { get; }

        /// <summary>
        /// 序列化職員狀態。
        /// </summary>
        string Serialize();

        /// <summary>
        /// 還原職員狀態。
        /// </summary>
        void RestoreFromSave(string ownerJson);

        /// <summary>
        /// 新遊戲初始化。
        /// </summary>
        void InitializeAsNewGame();

        // ── Query API（5）──────────────────────────────────────────────────────

        /// <summary>
        /// 取得意願加成。
        /// </summary>
        float GetStaffWillingnessBonus();

        /// <summary>
        /// 取得會計傭金加成。
        /// </summary>
        float GetAccountantCommissionBonus();

        /// <summary>
        /// 取得會計罰款修正（負值）。
        /// </summary>
        float GetAccountantPenaltyBonus();

        /// <summary>
        /// 取得招募刷新減秒。
        /// </summary>
        int GetRecruitRefreshReductionSec();

        /// <summary>
        /// 是否啟用成功率預覽。
        /// </summary>
        bool IsSuccessRatePreviewEnabled();

        // ── Mutator API（6）────────────────────────────────────────────────────

        /// <summary>
        /// 錄用職員。
        /// </summary>
        HireResult HireStaff(CandidateCard candidate);

        /// <summary>
        /// 指派職員到建築。
        /// </summary>
        AssignResult TryAssignStaff(int instanceID, int buildingID);

        /// <summary>
        /// 取消職員指派。
        /// </summary>
        UnassignResult TryUnassignStaff(int instanceID);

        /// <summary>
        /// 職員進入休假。
        /// </summary>
        GoOnLeaveResult TryGoOnLeave(int instanceID);

        /// <summary>
        /// 職員從休假返回。
        /// </summary>
        ReturnFromLeaveResult TryReturnFromLeave(int instanceID);

        /// <summary>
        /// 解雇職員。
        /// </summary>
        TryFireStaffResult TryFireStaff(int instanceID);

        // ── 讀取 API ───────────────────────────────────────────────────────────

        /// <summary>
        /// 取得指定職員實例（找不到回 null）。
        /// </summary>
        StaffInstance GetInstance(int instanceID);

        /// <summary>
        /// 取得目前 roster。
        /// </summary>
        IReadOnlyDictionary<int, StaffInstance> GetRoster();

        /// <summary>
        /// 取得職員狀態檢視。
        /// </summary>
        StaffStateView GetStaffStateView(int instanceID);
    }
}
