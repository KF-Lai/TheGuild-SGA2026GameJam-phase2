namespace TheGuild.Gameplay.Gacha.Events
{
    /// <summary>
    /// FT-08 候選池 / Reserved 池 / pity 狀態變更通知。
    /// FT-10 SaveLoadService 訂閱此事件以標記 dirty；GachaService 在
    /// TryRecruit OK、TryRejectCandidate、TryReserveCandidate、
    /// TryReleaseReserve、InternalRefresh Step 9 等修改 _playerState 的位置發布。
    /// </summary>
    public readonly struct OnGachaStateDirtyEvent
    {
    }
}
