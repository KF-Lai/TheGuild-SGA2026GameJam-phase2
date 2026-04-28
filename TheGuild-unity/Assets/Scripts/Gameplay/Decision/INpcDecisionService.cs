namespace TheGuild.Gameplay.Decision
{
    public interface INpcDecisionService
    {
        DecisionResult MakeDecision(int instanceID, int missionID);
        float PreviewEffectiveScore(int instanceID, int missionID);
    }
}
