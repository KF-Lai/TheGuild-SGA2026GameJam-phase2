using System;

// FT-06 Guild Core — 型別定義檔
// 實作依據：【FT-06-FSD】guild-core.md §5.3
// 包含：GameOverState enum、GuildState POCO、GuildLevelEntry DTO、
//       LevelUpPayload 內部 queue 元素、5 個事件 struct

namespace TheGuild.Gameplay.Guild
{
    // ────────────────────────────────────────────────────────────────────────────
    // runtime 用 enum（所有狀態比較與轉移皆使用此 enum；序列化前由 SetGameOverState
    // helper 同步寫入 GuildState.gameOverState 字串）
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 公會 Game Over 狀態（runtime cache，不直接序列化）。
    /// </summary>
    public enum GameOverState
    {
        Active = 0,
        Pending = 1,
        Over = 2
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 公會核心狀態 POCO（FT-10 ISaveable 序列化對象；JsonUtility 直接序列化）
    // 5 個欄位為 FSD §5.3 規範的完整 schema，不可增減（ISaveable owner = ft06Guild）
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 公會持久化狀態（序列化 POCO）。
    /// </summary>
    [Serializable]
    public class GuildState
    {
        /// <summary>玩家輸入名稱（不含「公會」後綴）。</summary>
        public string guildName;

        /// <summary>完整顯示名稱（含「公會」後綴）。</summary>
        public string displayName;

        /// <summary>公會創立時間（UTC Unix seconds）。</summary>
        public long foundingTimestamp;

        /// <summary>當前等級（1..5，僅升不降）。</summary>
        public int currentLevel;

        /// <summary>
        /// Game Over 狀態字串（"Active" / "Pending" / "Over"）。
        /// 序列化用；runtime 比較請使用 GuildCoreService._gameOverState enum。
        /// </summary>
        public string gameOverState;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CSV row DTO（DataManager 自動反射填充欄位；欄位名稱須與 GuildLevelTable.csv 完全一致）
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GuildLevelTable.csv 的單列資料傳輸物件。
    /// 欄位命名須與 CSV 欄位名稱一致（DataManager CsvParser 依欄位名反射填充）。
    /// </summary>
    [Serializable]
    public class GuildLevelEntry
    {
        public int level;
        public int reputationThreshold;
        public string title;

        /// <summary>[Deprecated] 與 maxRecruitableRank 同值，保留供舊資料相容。</summary>
        public string maxDifficulty;

        public string maxRecruitableRank;
        public string maxMissionDifficulty;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 內部 queue 元素（runtime-only，不序列化；List<LevelUpPayload> 最多 4 個元素）
    // 設計選擇：改用 List<LevelUpPayload> 取代 Queue<T>，以便 §5.4-A 步驟 5 in-place 回填
    // (疑點 2 決策：List.RemoveAt(0) 小規模無效能影響；最多 4 個元素)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 連跳 queue 的單一升級任務（僅 runtime 使用）。
    /// </summary>
    internal struct LevelUpPayload
    {
        public int fromLv;
        public int toLv;

        /// <summary>本批次升級的最終目標等級（§5.4-A 步驟 5 統一回填）。</summary>
        public int finalTargetLv;

        /// <summary>總跳數 > 1 時為 true（同批次所有 payload 一致）。</summary>
        public bool isMultiJump;

        /// <summary>觸發本次升級時的聲望值（最後一次觸發更新）。</summary>
        public int reputationAtUpgrade;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 事件 struct（readonly struct；透過 EventBus 發布）
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 新遊戲初始化完成事件（由 InitializeAsNewGame 末段發布）。
    /// </summary>
    public readonly struct OnGuildInitializedEvent
    {
        public OnGuildInitializedEvent(string displayName, long foundingTimestamp)
        {
            DisplayName = displayName;
            FoundingTimestamp = foundingTimestamp;
        }

        public string DisplayName { get; }
        public long FoundingTimestamp { get; }
    }

    /// <summary>
    /// 存檔讀取完成事件（由 RestoreFromSave 末段發布）。
    /// </summary>
    public readonly struct OnGuildLoadedEvent
    {
        public OnGuildLoadedEvent(string displayName, int currentLevel)
        {
            DisplayName = displayName;
            CurrentLevel = currentLevel;
        }

        public string DisplayName { get; }
        public int CurrentLevel { get; }
    }

    /// <summary>
    /// 公會等級變更事件（由 Update() 從 _pendingLevelUpQueue 取出一項時發布）。
    /// 11 個欄位對齊 FSD §5.3 / GDD §3.4 / §4.3。
    /// </summary>
    public readonly struct OnGuildLevelChangedEvent
    {
        public OnGuildLevelChangedEvent(
            int fromLv, int toLv,
            string fromTitle, string toTitle,
            string newMaxDifficulty,
            string newMaxRecruitableRank,
            string newMaxMissionDifficulty,
            int reputationAtUpgrade,
            long upgradeTimestamp,
            bool isMultiJump,
            int finalTargetLv)
        {
            FromLv = fromLv;
            ToLv = toLv;
            FromTitle = fromTitle;
            ToTitle = toTitle;
            NewMaxDifficulty = newMaxDifficulty;
            NewMaxRecruitableRank = newMaxRecruitableRank;
            NewMaxMissionDifficulty = newMaxMissionDifficulty;
            ReputationAtUpgrade = reputationAtUpgrade;
            UpgradeTimestamp = upgradeTimestamp;
            IsMultiJump = isMultiJump;
            FinalTargetLv = finalTargetLv;
        }

        public int FromLv { get; }
        public int ToLv { get; }
        public string FromTitle { get; }
        public string ToTitle { get; }

        /// <summary>[Deprecated] 等同 NewMaxRecruitableRank，保留供下游相容。</summary>
        public string NewMaxDifficulty { get; }

        public string NewMaxRecruitableRank { get; }
        public string NewMaxMissionDifficulty { get; }
        public int ReputationAtUpgrade { get; }

        /// <summary>從 queue 取出並發布時的 UTC 時間戳。</summary>
        public long UpgradeTimestamp { get; }

        public bool IsMultiJump { get; }

        /// <summary>本批次的最終目標等級。</summary>
        public int FinalTargetLv { get; }
    }

    /// <summary>
    /// Game Over 階段 1 事件：玩家破產後立即發布（訃聞顯示時機）。
    /// 對齊 FSD §5.3 / GDD §3.7；7 個欄位。
    /// </summary>
    public readonly struct OnGameOverPendingEvent
    {
        public OnGameOverPendingEvent(
            long pendingTimestamp,
            int finalGoldBeforeGameOver,
            int finalReputation,
            int finalLevel,
            string finalTitle,
            string guildDisplayName,
            long foundingTimestamp)
        {
            PendingTimestamp = pendingTimestamp;
            FinalGoldBeforeGameOver = finalGoldBeforeGameOver;
            FinalReputation = finalReputation;
            FinalLevel = finalLevel;
            FinalTitle = finalTitle;
            GuildDisplayName = guildDisplayName;
            FoundingTimestamp = foundingTimestamp;
        }

        public long PendingTimestamp { get; }
        public int FinalGoldBeforeGameOver { get; }
        public int FinalReputation { get; }
        public int FinalLevel { get; }
        public string FinalTitle { get; }
        public string GuildDisplayName { get; }
        public long FoundingTimestamp { get; }
    }

    /// <summary>
    /// Game Over 階段 2 事件：玩家手動確認訃聞後發布（結算時機）。
    /// 對齊 FSD §5.3 / GDD §3.8；= Pending payload 7 欄位 + confirmTimestamp。
    /// </summary>
    public readonly struct OnGameOverEvent
    {
        public OnGameOverEvent(
            long pendingTimestamp,
            int finalGoldBeforeGameOver,
            int finalReputation,
            int finalLevel,
            string finalTitle,
            string guildDisplayName,
            long foundingTimestamp,
            long confirmTimestamp)
        {
            PendingTimestamp = pendingTimestamp;
            FinalGoldBeforeGameOver = finalGoldBeforeGameOver;
            FinalReputation = finalReputation;
            FinalLevel = finalLevel;
            FinalTitle = finalTitle;
            GuildDisplayName = guildDisplayName;
            FoundingTimestamp = foundingTimestamp;
            ConfirmTimestamp = confirmTimestamp;
        }

        public long PendingTimestamp { get; }
        public int FinalGoldBeforeGameOver { get; }
        public int FinalReputation { get; }
        public int FinalLevel { get; }
        public string FinalTitle { get; }
        public string GuildDisplayName { get; }
        public long FoundingTimestamp { get; }

        /// <summary>玩家確認訃聞的時間戳（PauseTick 後取得）。</summary>
        public long ConfirmTimestamp { get; }
    }
}
