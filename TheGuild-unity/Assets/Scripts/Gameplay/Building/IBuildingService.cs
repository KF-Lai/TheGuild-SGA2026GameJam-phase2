using System;

// FT-07 Guild Building System — 服務介面
// 實作依據：【FT-07-FSD】guild-building-system.md §5.1
// 職責（SRP）：對外 API 簽名定義，含升級、查詢與 ISaveable 衍生方法

namespace TheGuild.Gameplay.Building
{
    /// <summary>
    /// FT-07 建築服務介面。
    /// 下游（FT-01、FT-02、FT-08、FT-12、P-02）統一透過此介面查詢建築效果值與升級。
    /// 實作為 <see cref="BuildingService"/>（concrete singleton MonoBehaviour）。
    /// FSD §5.1。
    /// </summary>
    public interface IBuildingService
    {
        // ── ISaveable 衍生（待 FT-10 拆出為獨立介面）──────────────────────────

        /// <summary>FT-10 ISaveable owner 識別鍵（值：<c>"ft07Buildings"</c>）。</summary>
        string OwnerKey { get; }

        /// <summary>FT-10 ISaveable 是否關鍵（值：<c>false</c>）。</summary>
        bool IsCritical { get; }

        /// <summary>
        /// 序列化 6 棟建築的 <see cref="BuildingState"/> 為 JSON 字串。
        /// FT-10 SaveLoadCoordinator 呼叫。
        /// </summary>
        string Serialize();

        /// <summary>
        /// 從存檔 JSON 還原建築狀態；超出 maxLevel 的等級 clamp + LogWarning。
        /// 不於本方法內推送保險櫃倒數秒數（統一由 Start 推送）。
        /// FT-10 Bootstrap 呼叫。FSD §5.4.2。
        /// </summary>
        void RestoreFromSave(string ownerJson);

        /// <summary>
        /// 新遊戲初始化：buildingID 1~5 currentLevel=1，buildingID 6 currentLevel=0。
        /// FT-10 Bootstrap 呼叫。FSD §5.4.1。
        /// </summary>
        void InitializeAsNewGame();

        // ── 查詢 API ─────────────────────────────────────────────────────────

        /// <summary>
        /// 回傳指定建築的當前等級。
        /// buildingID 不在 1~6 → 拋 <see cref="ArgumentOutOfRangeException"/>。
        /// FSD §5.1。
        /// </summary>
        /// <param name="buildingID">建築 ID（1~6）。</param>
        int GetBuildingLevel(int buildingID);

        /// <summary>
        /// 回傳委託板（buildingID=1）的委託槽位數（effectValue）。
        /// FT-02 消費。FSD §5.1。
        /// </summary>
        int GetMissionSlotCount();

        /// <summary>
        /// 回傳公會大廳（buildingID=3）的冒險者名冊上限（effectValue）。
        /// FT-01 消費。FSD §5.1。
        /// </summary>
        int GetRosterCap();

        /// <summary>
        /// 回傳公會櫃臺（buildingID=4）的並行任務上限（effectValue）。
        /// FT-02 消費。FSD §5.1。
        /// </summary>
        int GetMaxConcurrentMissions();

        /// <summary>
        /// 回傳招募廣告欄（buildingID=2）的候選池刷新間隔（effectValue 秒 → TimeSpan）。
        /// FT-01 消費。FSD §5.1。
        /// </summary>
        TimeSpan GetRecruitRefreshInterval();

        /// <summary>
        /// 回傳預備金保險櫃（buildingID=5）的破產警告持續秒數（effectValue）。
        /// 啟動與升級時推送至 F-03。FSD §5.1。
        /// </summary>
        int GetBankruptcyWarningSeconds();

        /// <summary>
        /// 職員休息室（buildingID=6）currentLevel &gt;= 1 時回傳 true，解鎖 FT-08 / FT-12。
        /// FSD §5.1。
        /// </summary>
        bool IsStaffSystemUnlocked();

        // ── 升級 API ──────────────────────────────────────────────────────────

        /// <summary>
        /// 快速檢查指定建築是否可升級（三閘：已滿級 / 公會等級 / 金幣）。
        /// 不修改任何狀態，P-02 用於按鈕 enable 判定。FSD §5.1。
        /// </summary>
        /// <param name="buildingID">建築 ID（1~6）。</param>
        bool CanUpgrade(int buildingID);

        /// <summary>
        /// 嘗試升級指定建築，回傳升級結果碼。
        /// 成功時扣除金幣、更新等級、發布 <see cref="BuildingUpgradedEvent"/>；
        /// buildingID=5 成功時額外推送 <see cref="global::TheGuild.Gameplay.Resources.ResourceManagement.SetBankruptcyWarningDuration"/>。
        /// FSD §5.4.3。
        /// </summary>
        /// <param name="buildingID">建築 ID（1~6）。</param>
        UpgradeResult TryUpgradeBuilding(int buildingID);
    }
}
