using TheGuild.Core.Events;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Resources.Events;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Scene
{
    [DefaultExecutionOrder(-110)]
    public sealed class PersistentHudController : MonoBehaviour
    {
        [SerializeField] private UIDocument _hudDocument;
        [SerializeField] private P02UITuning _tuning;

        private VisualElement _root;
        private Label _goldLabel;
        private Button _storyIndicator;
        private Button _settingsButton;
        private VisualElement _logHostContainer;
        private bool _initialized;
        private bool _subscribed;

        public static PersistentHudController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                SubscribeEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_hudDocument == null || _hudDocument.rootVisualElement == null)
            {
                Debug.LogWarning("[PersistentHudController] HUD document is not assigned.");
                return;
            }

            _root = _hudDocument.rootVisualElement;
            BuildHudElements();
            SubscribeEvents();
            UpdateGold(ResourceManagement.Instance == null ? 0 : ResourceManagement.Instance.GetGold());
            RefreshStoryIndicator();
            _initialized = true;
        }

        public void OnEffectiveScaleChanged(float newScale)
        {
            float scale = newScale > 0f ? newScale : 1f;
            if (_hudDocument != null && _hudDocument.panelSettings != null)
            {
                _hudDocument.panelSettings.scale = scale;
            }

            if (SceneNavigationController.Instance != null)
            {
                SceneNavigationController.Instance.OnEffectiveScaleChanged(scale);
            }
        }

        public VisualElement GetLogHostContainer()
        {
            return _logHostContainer;
        }

        public void RefreshStoryIndicator()
        {
            if (_storyIndicator == null)
            {
                return;
            }

            int count = StoryDialogueQueue.Instance == null ? 0 : StoryDialogueQueue.Instance.Count;
            _storyIndicator.text = count.ToString();
            _storyIndicator.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildHudElements()
        {
            _goldLabel = new Label { name = "hud-gold-label" };
            _goldLabel.style.fontSize = GetHudFontSize();
            _root.Add(_goldLabel);

            _storyIndicator = new Button(OnStoryIndicatorClicked) { name = "hud-story-indicator" };
            _storyIndicator.style.fontSize = GetHudFontSize();
            _root.Add(_storyIndicator);

            _settingsButton = new Button(OnSettingsClicked) { name = "hud-settings-button", text = "⚙" };
            _settingsButton.style.fontSize = GetHudFontSize();
            _root.Add(_settingsButton);

            _logHostContainer = new VisualElement { name = "hud-log-host" };
            _root.Add(_logHostContainer);
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            EventBus.Subscribe<OnGoldChangedEvent>(HandleGoldChanged);
            _subscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed)
            {
                return;
            }

            EventBus.Unsubscribe<OnGoldChangedEvent>(HandleGoldChanged);
            _subscribed = false;
        }

        private void HandleGoldChanged(OnGoldChangedEvent evt)
        {
            UpdateGold(evt.CurrentGold);
        }

        private void UpdateGold(int gold)
        {
            if (_goldLabel != null)
            {
                _goldLabel.text = $"{gold} g";
            }
        }

        private void OnStoryIndicatorClicked()
        {
            if (StoryDialogueQueue.Instance != null)
            {
                StoryDialogueQueue.Instance.ManualResume();
                RefreshStoryIndicator();
            }
        }

        private void OnSettingsClicked()
        {
            if (PanelManager.Instance != null)
            {
                PanelManager.Instance.OpenPanel(PanelID.SettingsPanel);
            }
        }

        private int GetHudFontSize()
        {
            return _tuning == null ? 13 : _tuning.FontSizeHud;
        }
    }
}
