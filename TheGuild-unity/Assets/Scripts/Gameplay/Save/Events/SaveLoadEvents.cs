namespace TheGuild.Gameplay.Save.Events
{
    public readonly struct OnSaveCompletedEvent
    {
        public OnSaveCompletedEvent(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }

    public readonly struct OnSaveFailedEvent
    {
        public OnSaveFailedEvent(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public readonly struct OnLoadCompletedEvent
    {
        public OnLoadCompletedEvent(int loadedFromBackupIndex)
        {
            LoadedFromBackupIndex = loadedFromBackupIndex;
        }

        public int LoadedFromBackupIndex { get; }
    }

    public readonly struct OnLoadFailedEvent
    {
        public OnLoadFailedEvent(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public readonly struct OnGameSealedEvent
    {
        public OnGameSealedEvent(string sealedPath)
        {
            SealedPath = sealedPath;
        }

        public string SealedPath { get; }
    }

    public readonly struct OnSaveDeletedEvent
    {
    }
}
