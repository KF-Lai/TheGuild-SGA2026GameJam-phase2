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
            // 4 元素改絕對定位，避免 default flex layout 堆疊出橫條（截圖 2026-05-03 灰條問題）。
            // GDD §3.3 原規格寫「嵌保險箱 / 嵌辦公桌」，但 nav row 為等寬 flex 排版無固定建築座標，
            // Jam 版簡化為四角錨點（Jam scope 縮減原則：USS styling 採極簡）。

            // 金幣：右上角
            _goldLabel = new Label { name = "hud-gold-label" };
            _goldLabel.style.fontSize = GetHudFontSize();
            _goldLabel.style.position = Position.Absolute;
            _goldLabel.style.top = 8;
            _goldLabel.style.right = 12;
            _goldLabel.style.color = new StyleColor(new Color(1f, 0.92f, 0.78f, 1f));
            _root.Add(_goldLabel);

            // 劇情指示器：右上角金幣下方（NARRATIVE_ENABLED=0 時 _storyIndicator.style.display = None，本次 hidden）
            _storyIndicator = new Button(OnStoryIndicatorClicked) { name = "hud-story-indicator" };
            _storyIndicator.style.fontSize = GetHudFontSize();
            _storyIndicator.style.position = Position.Absolute;
            _storyIndicator.style.top = 36;
            _storyIndicator.style.right = 12;
            _storyIndicator.style.display = DisplayStyle.None; // RefreshStoryIndicator 會依 queue 計數動態顯隱
            _root.Add(_storyIndicator);

            // 設定按鈕：與 nav-settings-desk 重複，預設 hidden（避免兩個入口）。
            // 保留欄位給 GDD §3.3 完整性，後續若要快捷 HUD 設定鈕可重新 enable。
            _settingsButton = new Button(OnSettingsClicked) { name = "hud-settings-button", text = "⚙" };
            _settingsButton.style.fontSize = GetHudFontSize();
            _settingsButton.style.position = Position.Absolute;
            _settingsButton.style.bottom = 8;
            _settingsButton.style.right = 12;
            _settingsButton.style.display = DisplayStyle.None; // nav-settings-desk 已是入口
            _root.Add(_settingsButton);

            // Log host：左下角，待 P-03 接入。Jam scope 縮減原則 P-03 不做 → 容器空但保留位置。
            _logHostContainer = new VisualElement { name = "hud-log-host" };
            _logHostContainer.style.position = Position.Absolute;
            _logHostContainer.style.bottom = 8;
            _logHostContainer.style.left = 12;
            _logHostContainer.style.minWidth = 200;
            _logHostContainer.style.minHeight = 80;
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
