namespace TheGuild.Gameplay.Save
{
    public interface ISaveLoadService
    {
        void MarkDirty();
        bool ExecuteSave(bool force = false);
        void ForceSave();
        void Bootstrap();
        void OnGameOver();
        void ResetToNewGame();
    }
}
