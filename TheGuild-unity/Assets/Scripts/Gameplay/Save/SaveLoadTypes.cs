using System;

namespace TheGuild.Gameplay.Save
{
    [Serializable]
    public sealed class SchemaMeta
    {
        public string schemaVersion;
        public string savedAtUtc;
        public string gameOverState;
        public long lastActiveTimestamp;
    }

    [Serializable]
    public sealed class SaveDataRoot
    {
        public SchemaMeta schemaMeta;
        public string f03Resources;
        public string c06WorldDanger;
        public string c02AdventurerRoster;
        public string ft06Guild;
        public string ft07Buildings;
        public string ft02Dispatch;
        public string ft08Gacha;
        public string ft12Staff;
        public string ft01Recruitment;
        public string factionStorySaveData;
        public string ft03Decision;
    }

    public readonly struct BootstrapResult
    {
        public BootstrapResult(bool hasExistingSave, int loadedFromBackupIndex, bool isGameOver, Exception failure)
        {
            HasExistingSave = hasExistingSave;
            LoadedFromBackupIndex = loadedFromBackupIndex;
            IsGameOver = isGameOver;
            Failure = failure;
        }

        public bool HasExistingSave { get; }
        public int LoadedFromBackupIndex { get; }
        public bool IsGameOver { get; }
        public Exception Failure { get; }
    }

    public sealed class CriticalRestoreFailedException : Exception
    {
        public CriticalRestoreFailedException(string ownerKey, Exception inner)
            : base($"Critical restore failed. ownerKey={ownerKey}", inner)
        {
            OwnerKey = ownerKey;
        }

        public string OwnerKey { get; }
    }
}
