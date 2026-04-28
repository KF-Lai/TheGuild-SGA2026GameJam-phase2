using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.Gameplay.Outcome
{
    /// <summary>
    /// ReputationDeltaTable 中的單筆 CSV 資料對應類別。
    /// DataManager.GetAll&lt;ReputationDeltaData&gt;() 用此型別反序列化；
    /// PK = difficulty（string：F/E/D/C/B/A/S/SS/SSS）。
    /// FSD §5.3 / §6.1；Data-Specs【FT-04-DS】reputation-delta-table.md。
    /// </summary>
    public sealed class ReputationDeltaData
    {
        /// <summary>PK：任務難度字串（F～SSS）。</summary>
        public string difficulty;

        /// <summary>成功時的基礎聲望變化量（正值）。</summary>
        public int successDelta;

        /// <summary>失敗時的基礎聲望變化量（負值）。</summary>
        public int failDelta;
    }

    /// <summary>
    /// OutcomeReputationCalculator 內部使用的快取結構。
    /// 從 ReputationDeltaData 轉換而來，以 difficulty 字串為 key。
    /// </summary>
    internal readonly struct ReputationDeltaEntry
    {
        public ReputationDeltaEntry(int successDelta, int failDelta)
        {
            SuccessDelta = successDelta;
            FailDelta = failDelta;
        }

        public int SuccessDelta { get; }
        public int FailDelta { get; }
    }

    /// <summary>
    /// FT-04 聲望計算器：依任務難度查詢 ReputationDeltaTable 回傳基礎聲望變化量。
    /// 從 OutcomeResolutionService 分離以保持 Service 職責單一（SRP）。
    /// FSD §5.3 OutcomeReputationCalculator / §4.4 Script 清單。
    /// </summary>
    public sealed class OutcomeReputationCalculator
    {
        private Dictionary<string, ReputationDeltaEntry> _reputationTable;

        /// <summary>
        /// 以 ReputationDeltaData 列表初始化內部查詢表。
        /// 應在 OutcomeResolutionService.Awake() 時呼叫，傳入 DataManager.GetAll&lt;ReputationDeltaData&gt;()。
        /// </summary>
        public void Initialize(IReadOnlyList<ReputationDeltaData> entries)
        {
            // 預估容量：ReputationDeltaTable 固定 9 行（F~SSS）
            _reputationTable = new Dictionary<string, ReputationDeltaEntry>(
                entries != null ? entries.Count : 9,
                System.StringComparer.Ordinal);

            if (entries == null)
            {
                Debug.LogError("[OutcomeReputationCalculator] Initialize: entries 為 null，聲望表為空。");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                ReputationDeltaData data = entries[i];
                if (data == null || string.IsNullOrEmpty(data.difficulty))
                {
                    Debug.LogWarning("[OutcomeReputationCalculator] Initialize: 跳過 difficulty 為空的資料列。");
                    continue;
                }

                _reputationTable[data.difficulty] = new ReputationDeltaEntry(data.successDelta, data.failDelta);
            }
        }

        /// <summary>
        /// 依難度字串與結果查詢基礎聲望變化量。
        /// 查無對應 difficulty → LogError 並回傳 0（不改變聲望，流程繼續）。
        /// successDelta &lt; 0 或 failDelta &gt; 0 → LogWarning（符號異常），但仍套用 CSV 值。
        /// FSD §5.4 步驟 6 / §7 §5.1。
        /// </summary>
        /// <param name="difficulty">任務難度字串（F～SSS）。</param>
        /// <param name="isSuccess">本次結算是否成功。</param>
        /// <returns>基礎聲望變化量。</returns>
        public int GetBaseDelta(string difficulty, bool isSuccess)
        {
            if (_reputationTable == null)
            {
                Debug.LogError("[OutcomeReputationCalculator] GetBaseDelta: 聲望表尚未初始化，回傳 0。");
                return 0;
            }

            if (!_reputationTable.TryGetValue(difficulty, out ReputationDeltaEntry entry))
            {
                Debug.LogError(
                    $"[OutcomeReputationCalculator] GetBaseDelta: 找不到 difficulty={difficulty} 的聲望設定，回傳 0。");
                return 0;
            }

            // 符號異常警告（仍套用 CSV 值，不修正）
            if (entry.SuccessDelta < 0)
            {
                Debug.LogWarning(
                    $"[OutcomeReputationCalculator] GetBaseDelta: difficulty={difficulty} successDelta={entry.SuccessDelta} 為負值（設計異常），仍套用。");
            }

            if (entry.FailDelta > 0)
            {
                Debug.LogWarning(
                    $"[OutcomeReputationCalculator] GetBaseDelta: difficulty={difficulty} failDelta={entry.FailDelta} 為正值（設計異常），仍套用。");
            }

            return isSuccess ? entry.SuccessDelta : entry.FailDelta;
        }
    }
}
