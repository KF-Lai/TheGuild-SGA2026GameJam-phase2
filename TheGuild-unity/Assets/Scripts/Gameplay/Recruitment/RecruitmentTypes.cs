using System;
using TheGuild.Gameplay.Adventurer;

namespace TheGuild.Gameplay.Recruitment
{
    [Serializable]
    public sealed class RecruitCandidate
    {
        public int CandidateID;
        public AdventurerInstance AdventurerInstance;
        public int Cost;
        public int ReputationReq;
    }

    public enum RecruitSource
    {
        Rookie = 0,
        Veteran = 1
    }

    [Serializable]
    public sealed class VeteranRankWeightEntry
    {
        public string rank;
        public int weight;
    }
}
