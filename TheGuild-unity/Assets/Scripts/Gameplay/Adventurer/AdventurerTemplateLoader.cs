using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using UnityEngine;

namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02-A 冒險者模板載入器：從 DataManager 載入並驗證 AdventurerTemplate 與 RecruitCostTable，快取供查詢。
    /// FSD §4.3 / §6.1 / §7。
    /// </summary>
    public sealed class AdventurerTemplateLoader
    {
        private static readonly HashSet<string> ValidRanks = new HashSet<string>(StringComparer.Ordinal)
        {
            "F", "E", "D", "C", "B", "A", "S"
        };

        private IReadOnlyDictionary<int, AdventurerTemplate> _templateByID = new Dictionary<int, AdventurerTemplate>();
        private IReadOnlyList<AdventurerTemplate> _allTemplates = Array.Empty<AdventurerTemplate>();
        internal IReadOnlyDictionary<string, RecruitCostEntry> _recruitCostDict = new Dictionary<string, RecruitCostEntry>(StringComparer.Ordinal);

        /// <summary>
        /// 建立快取；呼叫後方可使用查詢 API。
        /// </summary>
        public AdventurerTemplateLoader Build()
        {
            LoadTemplates();
            LoadRecruitCostTable();
            return this;
        }

        /// <summary>
        /// 依 templateID 取得模板；找不到回傳 null。
        /// </summary>
        public AdventurerTemplate GetTemplate(int templateID)
        {
            if (templateID <= 0)
            {
                Debug.LogWarning($"[AdventurerTemplateLoader] GetTemplate: templateID={templateID} 非法，回傳 null。");
                return null;
            }

            if (_templateByID.TryGetValue(templateID, out AdventurerTemplate tmpl))
            {
                return tmpl;
            }

            return null;
        }

        /// <summary>
        /// 取得所有合法模板列表（供 FT-01 招募池篩選）。
        /// </summary>
        public IReadOnlyList<AdventurerTemplate> GetAllTemplates()
        {
            return _allTemplates;
        }

        /// <summary>
        /// 依 rank 查詢招募費用；找不到回傳 (0, 0)。
        /// </summary>
        public (int cost, int reputationReq) GetRecruitCost(string rank)
        {
            if (string.IsNullOrEmpty(rank))
            {
                Debug.LogWarning("[AdventurerTemplateLoader] GetRecruitCost: rank 空值。");
                return (0, 0);
            }

            if (_recruitCostDict.TryGetValue(rank, out RecruitCostEntry entry))
            {
                return (entry.cost, entry.reputationReq);
            }

            Debug.LogWarning($"[AdventurerTemplateLoader] GetRecruitCost: 找不到 rank={rank}。");
            return (0, 0);
        }

        // ── 私有方法 ──────────────────────────────────────────────────────────────

        private void LoadTemplates()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[AdventurerTemplateLoader] DataManager.Instance 為 null，無法載入 AdventurerTemplate。");
                return;
            }

            IReadOnlyList<AdventurerTemplate> rawList = DataManager.Instance.GetAll<AdventurerTemplate>();
            Dictionary<int, AdventurerTemplate> dict = new Dictionary<int, AdventurerTemplate>(rawList.Count);
            List<AdventurerTemplate> validList = new List<AdventurerTemplate>(rawList.Count);

            for (int i = 0; i < rawList.Count; i++)
            {
                AdventurerTemplate tmpl = rawList[i];
                if (tmpl == null)
                {
                    continue;
                }

                if (tmpl.templateID <= 0)
                {
                    Debug.LogError($"[AdventurerTemplateLoader] templateID={tmpl.templateID} 非法（必須 ≥ 1），跳過。");
                    continue;
                }

                // rank 驗證（GDD §5.1）
                if (!ValidRanks.Contains(tmpl.rank ?? string.Empty))
                {
                    Debug.LogError($"[AdventurerTemplateLoader] templateID={tmpl.templateID} rank={tmpl.rank} 非法，跳過。");
                    continue;
                }

                // professionID 驗證（GDD §5.1）
                if (ProfessionService.Instance == null || ProfessionService.Instance.GetProfession(tmpl.professionID) == null)
                {
                    Debug.LogError($"[AdventurerTemplateLoader] templateID={tmpl.templateID} professionID={tmpl.professionID} 在 ProfessionTable 找不到，跳過。");
                    continue;
                }

                // raceID 驗證：非 0 時必須存在（GDD §5.1）
                if (tmpl.raceID != 0 && (RaceService.Instance == null || RaceService.Instance.GetRace(tmpl.raceID) == null))
                {
                    Debug.LogError($"[AdventurerTemplateLoader] templateID={tmpl.templateID} raceID={tmpl.raceID} 在 RaceTable 找不到，跳過。");
                    continue;
                }

                // duplicate templateID：後者覆蓋前者（GDD §5.1）
                if (dict.ContainsKey(tmpl.templateID))
                {
                    Debug.LogWarning($"[AdventurerTemplateLoader] templateID={tmpl.templateID} 重複，後者覆蓋前者。");
                }

                dict[tmpl.templateID] = tmpl;
            }

            // 重建 allTemplates（覆蓋後的最終列表）
            foreach (KeyValuePair<int, AdventurerTemplate> pair in dict)
            {
                validList.Add(pair.Value);
            }

            _templateByID = dict;
            _allTemplates = validList.ToArray();
        }

        private void LoadRecruitCostTable()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[AdventurerTemplateLoader] DataManager.Instance 為 null，無法載入 RecruitCostTable。");
                return;
            }

            IReadOnlyList<RecruitCostEntry> rawList = DataManager.Instance.GetAll<RecruitCostEntry>();
            Dictionary<string, RecruitCostEntry> dict = new Dictionary<string, RecruitCostEntry>(StringComparer.Ordinal);

            for (int i = 0; i < rawList.Count; i++)
            {
                RecruitCostEntry entry = rawList[i];
                if (entry == null)
                {
                    continue;
                }

                if (!ValidRanks.Contains(entry.rank ?? string.Empty))
                {
                    Debug.LogWarning($"[AdventurerTemplateLoader] RecruitCostTable rank={entry.rank} 非法，跳過。");
                    continue;
                }

                if (dict.ContainsKey(entry.rank))
                {
                    Debug.LogWarning($"[AdventurerTemplateLoader] RecruitCostTable rank={entry.rank} 重複，後者覆蓋前者。");
                }

                dict[entry.rank] = entry;
            }

            // 驗證 7 個 rank 全部存在（AC-AM-16）
            foreach (string r in ValidRanks)
            {
                if (!dict.ContainsKey(r))
                {
                    Debug.LogError($"[AdventurerTemplateLoader] RecruitCostTable 缺少 rank={r}。");
                }
            }

            _recruitCostDict = dict;
        }
    }
}
