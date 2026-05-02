using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class StoryDialoguePanel : MonoBehaviour, IPanel
    {
        private readonly DialogueRenderer _renderer = new DialogueRenderer();
        private VisualElement _root;
        private VisualElement _content;
        private Button _confirmButton;
        private StoryDialogueOpenArgs _args;

        [SerializeField] private P02UITuning _tuning;

        public PanelID Id => PanelID.StoryDialogue;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "story-dialogue-panel" };
            _root.style.display = DisplayStyle.None;
            _content = new VisualElement { name = "story-dialogue-content" };
            _confirmButton = new Button(Confirm) { name = "story-dialogue-confirm" };
            _confirmButton.text = Text("ui.common.confirm", "Confirm");
            _root.Add(_content);
            _root.Add(_confirmButton);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<OnFactionStoryStageEpilogueEvent>(HandleStageEpilogue);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnFactionStoryStageEpilogueEvent>(HandleStageEpilogue);
            EventBus.Unsubscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);
        }

        public void Open(object args)
        {
            _args = args is StoryDialogueOpenArgs openArgs
                ? openArgs
                : new StoryDialogueOpenArgs(0, string.Empty, string.Empty, string.Empty);

            _root.style.display = DisplayStyle.Flex;
            Render();
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;
            _content.Clear();
            _args = default;
        }

        private void Render()
        {
            string key = _args.DialogueKey;
            if (string.IsNullOrEmpty(key))
            {
                _content.Clear();
                _content.Add(new Label("[對話缺失：]"));
                return;
            }

            _renderer.RenderToPanel(_content, key, _args.StageID, _tuning);
        }

        private void Confirm()
        {
            // FSD-B §5.4.8 Confirm 流程：
            // - SkipConfirmCallback=true → 不呼叫 ConfirmDialogue（場景物件互動入口）
            // - IsEpilogue=true → 不呼叫 ConfirmDialogue（epilogue 不入隊）
            // - 其他路徑：ConfirmDialogue → SUCCESS + SpecialEventKey == "ophelia_missing" → publish OnOpheliaMissingNightEvent
            bool shouldPublishOpheliaMissing = _args.IsOpheliaInteraction;
            if (!_args.SkipConfirmCallback && !_args.IsEpilogue && FactionStoryService.Instance != null && _args.StageID > 0)
            {
                ConfirmDialogueResult result = FactionStoryService.Instance.ConfirmDialogue(_args.StageID);
                if (result == ConfirmDialogueResult.OK
                    && string.Equals(_args.SpecialEventKey, "ophelia_missing", System.StringComparison.Ordinal))
                {
                    shouldPublishOpheliaMissing = true;
                }
                else if (result != ConfirmDialogueResult.OK)
                {
                    Debug.LogWarning($"[StoryDialoguePanel] ConfirmDialogue result={result}");
                }
            }
            else if (FactionStoryService.Instance == null
                     && string.Equals(_args.SpecialEventKey, "ophelia_missing", System.StringComparison.Ordinal))
            {
                shouldPublishOpheliaMissing = true;
            }

            if (shouldPublishOpheliaMissing)
            {
                EventBus.Publish(new OnOpheliaMissingNightEvent(_args.StageID));
            }

            PanelManager.Instance?.ClosePanel(PanelID.StoryDialogue);
        }

        private void HandleStageEpilogue(OnFactionStoryStageEpilogueEvent evt)
        {
            PanelManager.Instance?.OpenPanel(
                PanelID.StoryDialogue,
                new StoryDialogueOpenArgs(evt.StageID, evt.ResolvedEpilogueKey, string.Empty, string.Empty, true, evt.IsOpheliaEpilogue));
        }

        private void HandleRouteCompleted(OnFactionRouteCompletedEvent evt)
        {
            if (PanelManager.Instance?.GetTopPanel() == PanelID.StoryDialogue)
            {
                return;
            }
        }

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
