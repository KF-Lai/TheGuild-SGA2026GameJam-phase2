namespace TheGuild.Gameplay.Decision
{
    public enum RejectionReason
    {
        TooRisky,
        NotWilling,
        NotInterested
    }

    public readonly struct DecisionResult
    {
        public readonly bool Accepted;
        public readonly float EffectiveScore;
        public readonly RejectionReason? RejectionReason;

        public DecisionResult(bool accepted, float effectiveScore, RejectionReason? rejectionReason)
        {
            Accepted = accepted;
            EffectiveScore = effectiveScore;
            RejectionReason = rejectionReason;
        }
    }
}
