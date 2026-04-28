using System;
using System.Collections.Generic;
using UnityEngine;

// FT-06 Guild Core — GuildLevelTable 載入與驗證
// 實作依據：【FT-06-FSD】guild-core.md §5.4-F Loader.Load 驗證項 / §4.4
// 職責（SRP）：GuildLevelTable.csv 欄位驗證、索引建立、提供 GetEntry / FindTargetLevel 查詢

namespace TheGuild.Gameplay.Guild
{
    /// <summary>
    /// GuildLevelTable.csv 載入器。
    /// 驗證資料合法性後自建 <see cref="Dictionary{TKey,TValue}"/>，
    /// 提供 <see cref="GetEntry"/> 與 <see cref="FindTargetLevel"/> 查詢。
    /// </summary>
    internal class GuildLevelDatabaseLoader
    {
        // ────────────────────────────────────────────────────────────────────────
        // 難度軸序映射（疑點 4 決策：用 Dictionary<string,int> 覆蓋全 9 階）
        // F<E<D<C<B<A<S<SS<SSS，對應 0~8
        // 同時服務 maxRecruitableRank（F~S，7 階）與 maxMissionDifficulty（F~SSS，9 階）
        // ────────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, int> DifficultyOrder =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "F",   0 },
                { "E",   1 },
                { "D",   2 },
                { "C",   3 },
                { "B",   4 },
                { "A",   5 },
                { "S",   6 },
                { "SS",  7 },
                { "SSS", 8 }
            };

        private Dictionary<int, GuildLevelEntry> _byLevel;

        // ────────────────────────────────────────────────────────────────────────
        // 公開 API
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 載入並驗證 GuildLevelEntry 清單。
        /// 執行 7 項驗證（FSD §5.4-F）；任一違規 <see cref="Debug.LogError"/> + 拋
        /// <see cref="InvalidOperationException"/>。
        /// </summary>
        /// <param name="entries">DataManager.GetAll&lt;GuildLevelEntry&gt;() 的結果。</param>
        public void Load(IReadOnlyList<GuildLevelEntry> entries)
        {
            // 驗證 1：非空
            if (entries == null || entries.Count < 1)
            {
                Debug.LogError("[GuildLevelDatabaseLoader] GuildLevelTable 載入失敗：entries 為空（需至少 1 筆）");
                throw new InvalidOperationException("GuildLevelTable entries must not be empty.");
            }

            // 驗證 2：第一筆必須是 level=1、reputationThreshold=0
            if (entries[0].level != 1 || entries[0].reputationThreshold != 0)
            {
                Debug.LogError(
                    $"[GuildLevelDatabaseLoader] 驗證失敗：entries[0] 必須是 level=1、reputationThreshold=0，" +
                    $"實際 level={entries[0].level}、threshold={entries[0].reputationThreshold}");
                throw new InvalidOperationException("GuildLevelTable first entry must be level=1 with reputationThreshold=0.");
            }

            // 驗證 3：level 嚴格升冪且連續（1,2,3,...）
            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].level != entries[i - 1].level + 1)
                {
                    Debug.LogError(
                        $"[GuildLevelDatabaseLoader] 驗證失敗：level 不連續或非升冪，" +
                        $"entries[{i - 1}].level={entries[i - 1].level}，entries[{i}].level={entries[i].level}");
                    throw new InvalidOperationException("GuildLevelTable level must be strictly consecutive ascending (1,2,3,...).");
                }
            }

            // 驗證 4：reputationThreshold 嚴格升冪
            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].reputationThreshold <= entries[i - 1].reputationThreshold)
                {
                    Debug.LogError(
                        $"[GuildLevelDatabaseLoader] 驗證失敗：reputationThreshold 非嚴格升冪，" +
                        $"entries[{i - 1}]={entries[i - 1].reputationThreshold}，entries[{i}]={entries[i].reputationThreshold}");
                    throw new InvalidOperationException("GuildLevelTable reputationThreshold must be strictly ascending.");
                }
            }

            // 驗證 5：maxRecruitableRank 單調不降（F<E<D<C<B<A<S）
            for (int i = 1; i < entries.Count; i++)
            {
                if (CompareDifficultyRank(entries[i].maxRecruitableRank, entries[i - 1].maxRecruitableRank) < 0)
                {
                    Debug.LogError(
                        $"[GuildLevelDatabaseLoader] 驗證失敗：maxRecruitableRank 不單調不降，" +
                        $"entries[{i - 1}]={entries[i - 1].maxRecruitableRank}，entries[{i}]={entries[i].maxRecruitableRank}");
                    throw new InvalidOperationException("GuildLevelTable maxRecruitableRank must be non-decreasing by difficulty order.");
                }
            }

            // 驗證 6：maxMissionDifficulty 單調不降（F<E<D<C<B<A<S<SS<SSS）
            for (int i = 1; i < entries.Count; i++)
            {
                if (CompareDifficultyRank(entries[i].maxMissionDifficulty, entries[i - 1].maxMissionDifficulty) < 0)
                {
                    Debug.LogError(
                        $"[GuildLevelDatabaseLoader] 驗證失敗：maxMissionDifficulty 不單調不降，" +
                        $"entries[{i - 1}]={entries[i - 1].maxMissionDifficulty}，entries[{i}]={entries[i].maxMissionDifficulty}");
                    throw new InvalidOperationException("GuildLevelTable maxMissionDifficulty must be non-decreasing by difficulty order.");
                }
            }

            // 驗證 7：title 非空字串
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.IsNullOrEmpty(entries[i].title))
                {
                    Debug.LogError(
                        $"[GuildLevelDatabaseLoader] 驗證失敗：entries[{i}].title 為空（level={entries[i].level}）");
                    throw new InvalidOperationException($"GuildLevelTable entry at index {i} has empty title.");
                }
            }

            // 驗證通過，建立主鍵索引
            _byLevel = new Dictionary<int, GuildLevelEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                _byLevel[entries[i].level] = entries[i];
            }
        }

        /// <summary>
        /// 依等級查詢單筆資料。找不到時回傳 null + LogError。
        /// </summary>
        public GuildLevelEntry GetEntry(int level)
        {
            if (_byLevel == null)
            {
                Debug.LogError("[GuildLevelDatabaseLoader] GetEntry 呼叫前必須先執行 Load()");
                return null;
            }

            if (_byLevel.TryGetValue(level, out GuildLevelEntry entry))
            {
                return entry;
            }

            Debug.LogError($"[GuildLevelDatabaseLoader] 找不到等級 {level} 的資料");
            return null;
        }

        /// <summary>
        /// 依聲望值計算對應的目標等級。
        /// 從高到低線性掃描（O(N)，N 最多 5），回傳 reputation >= entry.reputationThreshold 的最高 level。
        /// 若全部 threshold 皆 > reputation（理論上不可能，因 Lv1 threshold=0），回傳 1。
        /// </summary>
        public int FindTargetLevel(int reputation)
        {
            if (_byLevel == null)
            {
                Debug.LogError("[GuildLevelDatabaseLoader] FindTargetLevel 呼叫前必須先執行 Load()");
                return 1;
            }

            // 掃描所有 entry，找到 reputation >= threshold 的最高等級
            // 預設回 Lv1（FSD §5.1.2：reputation 為負時 targetLevel = 1）
            int highestMatchedLevel = 1;

            foreach (KeyValuePair<int, GuildLevelEntry> kv in _byLevel)
            {
                if (reputation >= kv.Value.reputationThreshold && kv.Key > highestMatchedLevel)
                {
                    highestMatchedLevel = kv.Key;
                }
            }

            return highestMatchedLevel;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 內部輔助方法
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 依難度軸序比較兩個難度字串。
        /// 回傳 &lt;0 表示 a &lt; b；0 表示相等；&gt;0 表示 a &gt; b。
        /// 未知難度字串視為最低（0），並記錄 LogWarning。
        /// </summary>
        private static int CompareDifficultyRank(string a, string b)
        {
            int orderA = GetDifficultyOrder(a);
            int orderB = GetDifficultyOrder(b);
            return orderA - orderB;
        }

        /// <summary>
        /// 取得難度字串在軸序中的序號（F=0 ~ SSS=8）。
        /// 未知字串回傳 -1 + LogWarning。
        /// </summary>
        private static int GetDifficultyOrder(string rank)
        {
            if (DifficultyOrder.TryGetValue(rank ?? string.Empty, out int order))
            {
                return order;
            }

            Debug.LogWarning($"[GuildLevelDatabaseLoader] 未知難度等級字串：\"{rank}\"，視為最低序（-1）");
            return -1;
        }
    }
}
