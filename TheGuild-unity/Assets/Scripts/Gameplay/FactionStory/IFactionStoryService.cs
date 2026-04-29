namespace TheGuild.Gameplay.FactionStory
{
    public interface IFactionStoryService
    {
        bool IsFactionStoryEnabled();
        int GetCurrentFactionScore(int factionID);
        int GetMaxFactionScore();
        int GetUnlockedStageIndex(int factionID);
        ConfirmDialogueResult ConfirmDialogue(int stageID);
        int GetPendingDialogueCount();
        bool IsRouteCompleted(int factionID);
    }
}
