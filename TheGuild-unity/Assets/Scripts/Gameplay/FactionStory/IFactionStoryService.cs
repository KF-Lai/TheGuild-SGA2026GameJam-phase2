using System.Collections.Generic;

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

        // v3.1 patch P3.1-006（P-02 design-review）：P-02 OnUIReady step 3 Bootstrap 復原專用 query。
        // 回傳尚未 ConfirmDialogue 處理完的 stage 隊列快照（FIFO 順序）。
        IReadOnlyList<int> GetPendingDialogueStages();
    }
}
