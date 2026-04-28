// FT-06 Guild Core — 程式碼常數
// 實作依據：【FT-06-FSD】guild-core.md §4.4 / GDD §7.2
// 這些常數屬於設計決策層（GDD §7.2），不入 SystemConstants.csv；
// 但不得在其他 Script 直接 magic number，一律引用此類別。

namespace TheGuild.Gameplay.Guild
{
    /// <summary>
    /// FT-06 Guild Core 系統的程式碼常數。
    /// 所有值皆有對應的 GDD §7.2 來源，不可在其他地方寫死相同數值。
    /// </summary>
    public static class GuildCoreConstants
    {
        /// <summary>
        /// 公會名稱輸入的全型字上限（8 全型字 = 16 半型字等效）。
        /// 來源：GDD §3.9 / §4.4 名稱長度公式。
        /// </summary>
        public const int GUILD_NAME_MAX_FULLWIDTH = 8;

        /// <summary>
        /// 公會名稱後綴（自動附加於玩家輸入後）。
        /// 來源：GDD §3.9。
        /// </summary>
        public const string GUILD_NAME_SUFFIX = "公會";

        /// <summary>
        /// 當玩家輸入為空白或控制字元時的預設顯示名稱。
        /// 來源：GDD §3.9 邊緣案例（§5.4.1）。
        /// </summary>
        public const string DEFAULT_GUILD_NAME = "公會";

        /// <summary>
        /// 連跳 queue 每次發送一個升級事件的間隔 frame 數。
        /// 60FPS 時約 0.1 秒一級，確保下游 UI 有足夠時間播放動畫。
        /// 來源：GDD §3.4 / FSD §3.3 「連跳的儀式感」。
        /// </summary>
        public const int LEVEL_UP_QUEUE_INTERVAL_FRAMES = 6;

        /// <summary>
        /// ISaveable OwnerKey，對應存檔系統（FT-10）中的 owner 識別鍵。
        /// 來源：FSD §5.1 / GDD §6.4。
        /// </summary>
        public const string OWNER_KEY = "ft06Guild";

        /// <summary>
        /// DataManager 中 GuildLevelTable.csv 的表名。
        /// 來源：FSD §6.1。
        /// </summary>
        public const string GUILD_LEVEL_TABLE_NAME = "GuildLevelTable";
    }
}
