using System;
using UnityEngine;

// FT-07 Guild Building System — 資料型別定義
// 實作依據：【FT-07-FSD】guild-building-system.md §4.4 / §5.3
// 職責（SRP）：BuildingRow（CSV DTO）、BuildingState（runtime 狀態）、
//              UpgradeResult（enum）、BuildingUpgradedEvent（struct payload）

namespace TheGuild.Gameplay.Building
{
    /// <summary>
    /// BuildingTable.csv 的資料列 DTO（唯讀，對應 DataManager 解析後的記錄）。
    /// FSD §5.3 / §6.1 欄位定義。
    /// </summary>
    [Serializable]
    public class BuildingRow
    {
        /// <summary>建築 ID，複合主鍵之一（1~6）。</summary>
        public int buildingID;

        /// <summary>建築名稱（顯示用）。</summary>
        public string name;

        /// <summary>此建築的最高等級（來自 CSV，各建築可不同）。</summary>
        public int maxLevel;

        /// <summary>本列對應的等級，複合主鍵之二（buildingID=6 從 0 起，其他從 1 起）。</summary>
        public int level;

        /// <summary>本等級的效果值（依建築不同，代表槽位數、秒數、容量等）。</summary>
        public int effectValue;

        /// <summary>升至本等級所需金幣費用（L1/L0 初始行為 0）。</summary>
        public int upgradeCost;

        /// <summary>升至本等級所需的公會最低等級（L2 行恆為 0，L3+ 可有門檻）。</summary>
        public int guildLevelReq;

        /// <summary>職員 slot capacity（FT-12 消費；Jam 版不讀，但 schema 驗證時需存在）。</summary>
        public int slotCount;

        /// <summary>Phase 2 維護費（Jam 版不讀，但 schema 驗證時需存在）。</summary>
        public int maintenanceCost;
    }

    /// <summary>
    /// 建築的 runtime 可變狀態（6 棟各一筆）。
    /// FSD §5.3；建議用 class（引用型別，in-place mutation，array 持有引用無需 copy-back）。
    /// </summary>
    [Serializable]
    public class BuildingState
    {
        /// <summary>建築 ID（1~6）。</summary>
        public int buildingID;

        /// <summary>當前等級（buildingID=6 可為 0，其他 1~maxLevel）。</summary>
        public int currentLevel;
    }

    /// <summary>
    /// 升級操作的結果碼。FSD §5.1 / §5.3。
    /// </summary>
    public enum UpgradeResult
    {
        /// <summary>升級成功。</summary>
        SUCCESS = 0,

        /// <summary>已達最高等級，無法繼續升級。</summary>
        ALREADY_MAX = 1,

        /// <summary>公會等級不足以解鎖下一等。</summary>
        GUILD_LEVEL_INSUFFICIENT = 2,

        /// <summary>金幣不足支付升級費用。</summary>
        GOLD_INSUFFICIENT = 3
    }

    /// <summary>
    /// 建築升級成功事件 payload（struct，搭配 EventBus&lt;T&gt; 的泛型 Action&lt;T&gt; 無 boxing）。
    /// FSD §5.2 / §5.3。
    /// </summary>
    public readonly struct BuildingUpgradedEvent
    {
        /// <summary>升級的建築 ID（1~6）。</summary>
        public readonly int BuildingID;

        /// <summary>升級前等級。</summary>
        public readonly int FromLevel;

        /// <summary>升級後等級。</summary>
        public readonly int ToLevel;

        /// <summary>建構子。</summary>
        public BuildingUpgradedEvent(int buildingID, int fromLevel, int toLevel)
        {
            BuildingID = buildingID;
            FromLevel = fromLevel;
            ToLevel = toLevel;
        }
    }
}
