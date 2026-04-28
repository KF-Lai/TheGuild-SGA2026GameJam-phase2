using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Recruitment
{
    public sealed class RecruitmentPoolGenerator
    {
        private static readonly string[] VeteranRanks = { "D", "C", "B", "A", "S" };
        private static readonly HashSet<string> RookieRanks = new HashSet<string>(StringComparer.Ordinal)
        {
            "F", "E"
        };

        private readonly AdventurerFactory _factory;
        private readonly AdventurerRoster _roster;
        private readonly ProfessionService _professionService;
        private readonly RaceService _raceService;
        private readonly TraitService _traitService;
        private readonly GuildCoreService _guildCoreService;
        private readonly System.Random _rng;

        private readonly IReadOnlyList<AdventurerTemplate> _templates;
        private readonly Dictionary<string, int> _veteranWeightByRank = new Dictionary<string, int>(StringComparer.Ordinal);

        public RecruitmentPoolGenerator(
            AdventurerFactory factory,
            AdventurerRoster roster,
            ProfessionService professionService,
            RaceService raceService,
            TraitService traitService,
            GuildCoreService guildCoreService,
            DataManager dataManager,
            System.Random rng = null)
        {
            _factory = factory;
            _roster = roster;
            _professionService = professionService;
            _raceService = raceService;
            _traitService = traitService;
            _guildCoreService = guildCoreService;
            _rng = rng ?? new System.Random();

            _templates = dataManager != null
                ? dataManager.GetAll<AdventurerTemplate>()
                : Array.Empty<AdventurerTemplate>();

            IReadOnlyList<VeteranRankWeightEntry> weightRows = dataManager != null
                ? dataManager.GetAll<VeteranRankWeightEntry>()
                : Array.Empty<VeteranRankWeightEntry>();

            for (int i = 0; i < weightRows.Count; i++)
            {
                VeteranRankWeightEntry row = weightRows[i];
                if (row == null || string.IsNullOrEmpty(row.rank))
                {
                    continue;
                }

                _veteranWeightByRank[row.rank] = Mathf.Max(0, row.weight);
            }
        }

        public List<RecruitCandidate> GenerateRookiePool(int poolSize, HashSet<int> usedUniqueTemplateIds, int startCandidateId)
        {
            List<RecruitCandidate> pool = new List<RecruitCandidate>(poolSize);
            HashSet<int> usedNonUniqueTemplateIds = new HashSet<int>();
            int nextCandidateId = startCandidateId;

            TryFillFromTemplates(
                isRookiePool: true,
                maxVeteranRankIndex: -1,
                poolSize: poolSize,
                usedUniqueTemplateIds: usedUniqueTemplateIds,
                usedNonUniqueTemplateIds: usedNonUniqueTemplateIds,
                output: pool,
                nextCandidateId: ref nextCandidateId);

            int guard = 0;
            int maxAttempts = poolSize * 30;
            while (pool.Count < poolSize && guard < maxAttempts)
            {
                RecruitCandidate candidate = CreateRandomCandidate(isRookiePool: true, maxVeteranRankIndex: -1, ref nextCandidateId);
                guard += 1;
                if (candidate != null)
                {
                    pool.Add(candidate);
                }
            }

            return pool;
        }

        public List<RecruitCandidate> GenerateVeteranPool(int poolSize, HashSet<int> usedUniqueTemplateIds, string maxRecruitableRank, int startCandidateId)
        {
            int maxIndex = Mathf.Max(0, RankIndex(maxRecruitableRank));
            List<RecruitCandidate> pool = new List<RecruitCandidate>(poolSize);
            HashSet<int> usedNonUniqueTemplateIds = new HashSet<int>();
            int nextCandidateId = startCandidateId;

            TryFillFromTemplates(
                isRookiePool: false,
                maxVeteranRankIndex: maxIndex,
                poolSize: poolSize,
                usedUniqueTemplateIds: usedUniqueTemplateIds,
                usedNonUniqueTemplateIds: usedNonUniqueTemplateIds,
                output: pool,
                nextCandidateId: ref nextCandidateId);

            int guard = 0;
            int maxAttempts = poolSize * 30;
            while (pool.Count < poolSize && guard < maxAttempts)
            {
                RecruitCandidate candidate = CreateRandomCandidate(isRookiePool: false, maxVeteranRankIndex: maxIndex, ref nextCandidateId);
                guard += 1;
                if (candidate != null)
                {
                    pool.Add(candidate);
                }
            }

            return pool;
        }

        public string RollVeteranRank(string maxRecruitableRank)
        {
            int maxIndex = RankIndex(maxRecruitableRank);
            if (maxIndex < 0)
            {
                maxIndex = 0;
            }

            return RollVeteranRankByIndex(maxIndex);
        }

        internal static int RankIndex(string rank)
        {
            switch (rank)
            {
                case "D": return 0;
                case "C": return 1;
                case "B": return 2;
                case "A": return 3;
                case "S": return 4;
                default: return -1;
            }
        }

        private void TryFillFromTemplates(
            bool isRookiePool,
            int maxVeteranRankIndex,
            int poolSize,
            HashSet<int> usedUniqueTemplateIds,
            HashSet<int> usedNonUniqueTemplateIds,
            List<RecruitCandidate> output,
            ref int nextCandidateId)
        {
            if (_factory == null || _templates == null || _templates.Count == 0)
            {
                return;
            }

            List<AdventurerTemplate> shuffled = new List<AdventurerTemplate>(_templates.Count);
            for (int i = 0; i < _templates.Count; i++)
            {
                AdventurerTemplate template = _templates[i];
                if (template != null)
                {
                    shuffled.Add(template);
                }
            }

            Shuffle(shuffled);

            for (int i = 0; i < shuffled.Count && output.Count < poolSize; i++)
            {
                AdventurerTemplate template = shuffled[i];
                if (!IsTemplateEligible(template, isRookiePool, maxVeteranRankIndex))
                {
                    continue;
                }

                if (template.isUnique == 1)
                {
                    if (!usedUniqueTemplateIds.Add(template.templateID))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!usedNonUniqueTemplateIds.Add(template.templateID))
                    {
                        continue;
                    }
                }

                AdventurerInstance instance = _factory.CreateFromTemplate(template.templateID);
                if (instance == null)
                {
                    continue;
                }

                (int cost, int reputationReq) = isRookiePool ? (0, 0) : _roster.GetRecruitCost(instance.rank);
                output.Add(new RecruitCandidate
                {
                    CandidateID = nextCandidateId++,
                    AdventurerInstance = instance,
                    Cost = cost,
                    ReputationReq = reputationReq
                });
            }
        }

        private RecruitCandidate CreateRandomCandidate(bool isRookiePool, int maxVeteranRankIndex, ref int nextCandidateId)
        {
            if (_factory == null || _roster == null || _professionService == null || _raceService == null || _traitService == null)
            {
                return null;
            }

            IReadOnlyList<ProfessionData> baseProfessions = _professionService.GetBaseProfessions();
            if (baseProfessions == null || baseProfessions.Count == 0)
            {
                return null;
            }

            ProfessionData profession = baseProfessions[_rng.Next(baseProfessions.Count)];
            string rank = isRookiePool
                ? (_rng.NextDouble() < 0.5 ? "F" : "E")
                : RollVeteranRankByIndex(maxVeteranRankIndex);

            int raceID = _raceService.RollRace(profession.professionID);
            int[] traitIDs = CollectTraits(profession.professionID);
            AdventurerInstance instance = _factory.CreateRandomInstance(rank, profession.professionID, raceID, traitIDs);
            if (instance == null)
            {
                return null;
            }

            (int cost, int reputationReq) = isRookiePool ? (0, 0) : _roster.GetRecruitCost(rank);
            return new RecruitCandidate
            {
                CandidateID = nextCandidateId++,
                AdventurerInstance = instance,
                Cost = cost,
                ReputationReq = reputationReq
            };
        }

        private int[] CollectTraits(int professionID)
        {
            IReadOnlyList<TraitGroupData> groups = _traitService.GetProfessionGroups(professionID);
            if (groups == null || groups.Count == 0)
            {
                return Array.Empty<int>();
            }

            HashSet<int> seen = new HashSet<int>();
            List<int> result = new List<int>(8);
            for (int i = 0; i < groups.Count; i++)
            {
                TraitGroupData group = groups[i];
                if (group == null)
                {
                    continue;
                }

                int[] rolled = _traitService.RollTraits(group);
                if (rolled == null)
                {
                    continue;
                }

                for (int j = 0; j < rolled.Length; j++)
                {
                    int traitID = rolled[j];
                    if (traitID > 0 && seen.Add(traitID))
                    {
                        result.Add(traitID);
                    }
                }
            }

            return result.ToArray();
        }

        private string RollVeteranRankByIndex(int maxIndex)
        {
            int clampedMax = Mathf.Clamp(maxIndex, 0, VeteranRanks.Length - 1);
            int totalWeight = 0;
            for (int i = 0; i <= clampedMax; i++)
            {
                totalWeight += GetWeight(VeteranRanks[i]);
            }

            if (totalWeight <= 0)
            {
                return "D";
            }

            int roll = _rng.Next(totalWeight);
            int accum = 0;
            for (int i = 0; i <= clampedMax; i++)
            {
                accum += GetWeight(VeteranRanks[i]);
                if (roll < accum)
                {
                    return VeteranRanks[i];
                }
            }

            return VeteranRanks[clampedMax];
        }

        private int GetWeight(string rank)
        {
            return _veteranWeightByRank.TryGetValue(rank, out int weight) ? Mathf.Max(0, weight) : 0;
        }

        private static bool IsTemplateEligible(AdventurerTemplate template, bool isRookiePool, int maxVeteranRankIndex)
        {
            if (template == null || string.IsNullOrEmpty(template.rank))
            {
                return false;
            }

            if (isRookiePool)
            {
                return RookieRanks.Contains(template.rank);
            }

            int idx = RankIndex(template.rank);
            return idx >= 0 && idx <= maxVeteranRankIndex;
        }

        private void Shuffle(List<AdventurerTemplate> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                AdventurerTemplate tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
