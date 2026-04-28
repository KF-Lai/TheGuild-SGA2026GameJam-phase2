using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.MissionDispatch
{
    /// <summary>
    /// FT-02-A 成功率/死亡率計算器（純 class，非 MonoBehaviour）。
    /// 職責：輸入冒險者 + 任務 → 輸出 (finalSuccess, finalDeath)。
    /// GDD §3.4 / §4.2；FSD §5.4 流程 A。
    /// </summary>
    public sealed class MissionRateCalculator
    {
        // 階級字串 → 索引映射（FSD §5.4 流程 A Step 1；GDD §3.3）
        private static readonly Dictionary<string, int> RANK_INDEX = new Dictionary<string, int>(System.StringComparer.Ordinal)
        {
            ["F"] = 0,
            ["E"] = 1,
            ["D"] = 2,
            ["C"] = 3,
            ["B"] = 4,
            ["A"] = 5,
            ["S"] = 6,
        };

        // 難度字串 → 索引映射（與 RANK_INDEX 同尺度；FSD §5.4 流程 A Step 1）
        private static readonly Dictionary<string, int> DIFF_INDEX = new Dictionary<string, int>(System.StringComparer.Ordinal)
        {
            ["F"]   = 0,
            ["E"]   = 1,
            ["D"]   = 2,
            ["C"]   = 3,
            ["B"]   = 4,
            ["A"]   = 5,
            ["S"]   = 6,
            ["SS"]  = 7,
            ["SSS"] = 8,
        };

        private const int RANK_DIFF_MIN = -3;
        private const int RANK_DIFF_MAX =  3;

        // 從 DataManager 讀取的常數（Initialize 後有效）
        private float _strongTypeBonus;
        private float _weakTypePenalty;
        private int   _escortTypeID;

        // rankDiff → baseSuccess 查詢字典（Initialize 後有效）
        private readonly Dictionary<int, float> _successRateMap = new Dictionary<int, float>(7);

        private bool _initialized;

        /// <summary>
        /// 從 DataManager 載入 SuccessRateTable 並快取常數。
        /// 須在 DataManager Awake 後呼叫。
        /// FSD §5.4 流程 A。
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            LoadSuccessRateMap();
            LoadSystemConstants();
        }

        /// <summary>
        /// 計算冒險者對任務的最終成功率與死亡率（8 步驟，FSD §5.4 流程 A）。
        /// 純計算，不變更任何狀態。
        /// </summary>
        /// <param name="instanceID">FK → AdventurerInstance（C-02）</param>
        /// <param name="missionID">FK → MissionTemplate（C-01）</param>
        /// <returns>(finalSuccess, finalDeath)；任一 null 時回 (0.0, 0.0) + LogWarning。</returns>
        public (float success, float death) CalculateRates(int instanceID, int missionID)
        {
            // null 防守
            AdventurerInstance adv = AdventurerRoster.Instance != null
                ? AdventurerRoster.Instance.GetAdventurer(instanceID)
                : null;
            if (adv == null)
            {
                Debug.LogWarning($"[MissionRateCalculator] CalculateRates: instanceID={instanceID} 找不到冒險者，回 (0,0)。");
                return (0f, 0f);
            }

            MissionTemplate tmpl = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;
            if (tmpl == null)
            {
                Debug.LogWarning($"[MissionRateCalculator] CalculateRates: missionID={missionID} 找不到任務模板，回 (0,0)。");
                return (0f, 0f);
            }

            // Step 1：rankDiff = clamp(RANK_INDEX[adv.rank] - DIFF_INDEX[tmpl.difficulty], -3, +3)
            if (!RANK_INDEX.TryGetValue(adv.rank ?? string.Empty, out int rankIdx))
            {
                Debug.LogWarning($"[MissionRateCalculator] CalculateRates: instanceID={instanceID} 未知 rank={adv.rank}，回 (0,0)。");
                return (0f, 0f);
            }

            if (!DIFF_INDEX.TryGetValue(tmpl.difficulty ?? string.Empty, out int diffIdx))
            {
                Debug.LogWarning($"[MissionRateCalculator] CalculateRates: missionID={missionID} 未知 difficulty={tmpl.difficulty}，回 (0,0)。");
                return (0f, 0f);
            }

            int rankDiff = Mathf.Clamp(rankIdx - diffIdx, RANK_DIFF_MIN, RANK_DIFF_MAX);

            // Step 2：baseSuccess = _successRateMap[rankDiff]
            if (!_successRateMap.TryGetValue(rankDiff, out float baseSuccess))
            {
                Debug.LogError($"[MissionRateCalculator] CalculateRates: _successRateMap 缺 rankDiff={rankDiff} 行，回 0.0。");
                baseSuccess = 0f;
            }

            // Step 3：baseDeath = IC01.GetBaseDeathRate(tmpl.difficulty)
            float baseDeath = MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetBaseDeathRate(tmpl.difficulty)
                : 0f;

            // Step 4：職業修正（C-03 IsStrongType / IsWeakType）
            if (ProfessionService.Instance != null)
            {
                if (ProfessionService.Instance.IsStrongType(adv.professionID, tmpl.typeID))
                {
                    baseSuccess += _strongTypeBonus;
                }
                else if (ProfessionService.Instance.IsWeakType(adv.professionID, tmpl.typeID))
                {
                    baseSuccess -= _weakTypePenalty;
                }
            }

            // Step 5：種族修正（C-04）
            if (RaceService.Instance != null)
            {
                baseSuccess += RaceService.Instance.GetSuccessDelta(adv.raceID, tmpl.typeID);
                baseDeath   += RaceService.Instance.GetDeathDelta(adv.raceID, tmpl.typeID);
            }

            // Step 6：特質 stat 修正（C-05，逐 traitID）
            if (adv.traitIDs != null && TraitService.Instance != null)
            {
                string successAll  = "success_all";
                string successType = $"success_{tmpl.typeID}";
                string deathAll    = "death_all";
                string deathType   = $"death_{tmpl.typeID}";

                for (int i = 0; i < adv.traitIDs.Length; i++)
                {
                    TraitData trait = TraitService.Instance.GetTrait(adv.traitIDs[i]);
                    if (trait == null)
                    {
                        Debug.LogWarning($"[MissionRateCalculator] CalculateRates: instanceID={instanceID} traitID={adv.traitIDs[i]} 找不到，跳過。");
                        continue;
                    }

                    if (trait.effectType != "stat")
                    {
                        continue;
                    }

                    string target = trait.effectTarget ?? string.Empty;
                    if (string.Equals(target, successAll, System.StringComparison.Ordinal) ||
                        string.Equals(target, successType, System.StringComparison.Ordinal))
                    {
                        baseSuccess += trait.effectValue;
                    }
                    else if (string.Equals(target, deathAll, System.StringComparison.Ordinal) ||
                             string.Equals(target, deathType, System.StringComparison.Ordinal))
                    {
                        baseDeath += trait.effectValue;
                    }
                }
            }

            // Step 7：finalSuccess = clamp(baseSuccess, 0, 1)
            float finalSuccess = Mathf.Clamp01(baseSuccess);

            // Step 8：finalDeath = clamp(baseDeath, 0, 1)
            float finalDeath = Mathf.Clamp01(baseDeath);

            return (finalSuccess, finalDeath);
        }

        // ── 私有方法 ──────────────────────────────────────────────────────────────

        private void LoadSuccessRateMap()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[MissionRateCalculator] Initialize: DataManager.Instance 為 null，_successRateMap 空。");
                return;
            }

            IReadOnlyList<SuccessRateRow> rows = DataManager.Instance.GetAll<SuccessRateRow>();
            _successRateMap.Clear();

            for (int i = 0; i < rows.Count; i++)
            {
                SuccessRateRow row = rows[i];
                if (!int.TryParse(row.rankDiff, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int key))
                {
                    Debug.LogWarning($"[MissionRateCalculator] Initialize: rankDiff={row.rankDiff} 無法 parse 為 int，略過。");
                    continue;
                }

                // 載入時 clamp successRate 至 [0, 1]（FSD §7 邊緣案例）
                float clamped = Mathf.Clamp01(row.successRate);
                if (!Mathf.Approximately(clamped, row.successRate))
                {
                    Debug.LogWarning($"[MissionRateCalculator] Initialize: rankDiff={key} successRate={row.successRate} 超出 [0,1]，已 clamp 至 {clamped}。");
                }

                _successRateMap[key] = clamped;
            }
        }

        private void LoadSystemConstants()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[MissionRateCalculator] Initialize: DataManager.Instance 為 null，常數使用預設值 0。");
                return;
            }

            _strongTypeBonus  = DataManager.Instance.GetFloat("STRONG_TYPE_BONUS");
            _weakTypePenalty  = DataManager.Instance.GetFloat("WEAK_TYPE_PENALTY");
            _escortTypeID     = DataManager.Instance.GetInt("ESCORT_TYPE_ID");
        }

        /// <summary>
        /// 取得 escortTypeID（供 MissionDispatchService 存取）。
        /// </summary>
        internal int EscortTypeID => _escortTypeID;
    }
}
