using System;
using System.Collections.Generic;

namespace TheGuild.UI.Core
{
    public enum PanelID
    {
        None = 0,
        Invalid = -1,
        CommissionBoard = 10,
        AdventurerRoster = 20,
        GuildBuilding = 30,
        StaffRoster = 40,
        StaffGacha = 50,
        GuildOverview = 60,
        StoryDialogue = 70,
        ConfirmPopup = 80,
        SettingsPanel = 90
    }

    public enum ConfirmStyle
    {
        Normal = 0,
        Destructive = 1
    }

    public readonly struct ConfirmArgs
    {
        public ConfirmArgs(string messageKey, Action onConfirm, Action onCancel = null, ConfirmStyle style = ConfirmStyle.Normal)
        {
            MessageKey = messageKey;
            OnConfirm = onConfirm;
            OnCancel = onCancel;
            Style = style;
        }

        public string MessageKey { get; }
        public Action OnConfirm { get; }
        public Action OnCancel { get; }
        public ConfirmStyle Style { get; }
    }

    public enum BindResult
    {
        Success = 0,
        AlreadyBound = 1,
        InvalidContainer = 2,
        Unavailable = 3
    }

    public readonly struct SceneObjectState
    {
        public SceneObjectState(string objectID, string spriteVariant, string dialogueKey, int priority, string audioCue)
        {
            ObjectID = objectID;
            SpriteVariant = spriteVariant;
            DialogueKey = dialogueKey;
            Priority = priority;
            AudioCue = audioCue;
        }

        public string ObjectID { get; }
        public string SpriteVariant { get; }
        public string DialogueKey { get; }
        public int Priority { get; }
        public string AudioCue { get; }

        public static SceneObjectState Default(string objectID)
        {
            return new SceneObjectState(objectID, "default", string.Empty, 0, string.Empty);
        }
    }

    public enum TextLanguage
    {
        zhTW = 0,
        en = 1
    }

    public enum PanelState
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3
    }

    public static class SortingOrder
    {
        public const int L0Scene = 0;
        public const int L1HUD = 100;
        public const int L2Modal = 200;
        public const int L2Panel = 210;
        public const int ConfirmPopup = 220;
        public const int StoryPanel = 230;
        public const int LogFloatingWindow = 250;
    }

    public static class NavObjectMap
    {
        public static readonly IReadOnlyDictionary<string, PanelID> ObjectToPanel =
            new Dictionary<string, PanelID>(StringComparer.Ordinal)
            {
                { "nav_commission_board", PanelID.CommissionBoard },
                { "nav_guild_hall", PanelID.AdventurerRoster },
                { "nav_construction", PanelID.GuildBuilding },
                { "nav_safe", PanelID.GuildOverview },
                { "nav_staff_lounge", PanelID.StaffRoster },
                { "nav_settings_desk", PanelID.SettingsPanel }
            };

        public static bool TryGetPanelID(string objectID, out PanelID panelID)
        {
            if (string.IsNullOrEmpty(objectID))
            {
                panelID = PanelID.Invalid;
                return false;
            }

            return ObjectToPanel.TryGetValue(objectID, out panelID);
        }
    }

    public readonly struct StoryDialogueOpenArgs
    {
        public StoryDialogueOpenArgs(
            int stageID,
            string dialogueKey,
            string specialEventKey,
            string sourceObjectID,
            bool isEpilogue = false,
            bool isOpheliaInteraction = false,
            bool skipConfirmCallback = false)
        {
            StageID = stageID;
            DialogueKey = dialogueKey;
            SpecialEventKey = specialEventKey;
            SourceObjectID = sourceObjectID;
            IsEpilogue = isEpilogue;
            IsOpheliaInteraction = isOpheliaInteraction;
            SkipConfirmCallback = skipConfirmCallback;
        }

        public int StageID { get; }
        public string DialogueKey { get; }
        public string SpecialEventKey { get; }
        public string SourceObjectID { get; }
        public bool IsEpilogue { get; }
        public bool IsOpheliaInteraction { get; }
        public bool SkipConfirmCallback { get; }
    }

    public readonly struct OnUIReadyEvent { }
    public readonly struct OnOpheliaReturnedEvent { }
}
