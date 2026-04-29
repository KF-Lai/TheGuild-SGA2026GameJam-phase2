namespace TheGuild.Core.SaveContract
{
    public interface ISaveable
    {
        string OwnerKey { get; }
        bool IsCritical { get; }
        string Serialize();
        void RestoreFromSave(string ownerJson);
        void InitializeAsNewGame();
    }
}
