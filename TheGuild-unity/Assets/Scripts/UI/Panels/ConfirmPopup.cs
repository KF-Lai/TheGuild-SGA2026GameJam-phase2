using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class ConfirmPopup : MonoBehaviour, IPanel
    {
        [SerializeField] private StyleSheet _styleSheet;

        private VisualElement _root;
        private VisualElement _panel;
        private Label _title;
        private Label _message;
        private VisualElement _buttons;
        private Button _confirmButton;
        private Button _cancelButton;
        private ConfirmArgs _args;

        public PanelID Id => PanelID.ConfirmPopup;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "confirm-popup" };
            _root.AddToClassList("confirm-popup");
            _root.style.display = DisplayStyle.None;

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            _panel = new VisualElement { name = "confirm-panel" };
            _panel.AddToClassList("confirm-panel");

            _title = new Label { name = "confirm-title" };
            _title.AddToClassList("confirm-title");
            _title.style.display = DisplayStyle.None;

            _message = new Label { name = "confirm-message" };
            _message.AddToClassList("confirm-message");

            _buttons = new VisualElement { name = "confirm-buttons" };
            _buttons.AddToClassList("confirm-buttons");

            _cancelButton = new Button(HandleCancel) { name = "confirm-cancel" };
            _cancelButton.AddToClassList("confirm-cancel");
            _cancelButton.text = Text("ui.common.cancel", "Cancel");

            _confirmButton = new Button(HandleConfirm) { name = "confirm-ok" };
            _confirmButton.AddToClassList("confirm-ok");
            _confirmButton.text = Text("ui.common.confirm", "Confirm");

            _buttons.Add(_cancelButton);
            _buttons.Add(_confirmButton);
            _panel.Add(_title);
            _panel.Add(_message);
            _panel.Add(_buttons);
            _root.Add(_panel);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
        }

        public void Open(object args)
        {
            _args = args is ConfirmArgs confirmArgs ? confirmArgs : default;

            bool hasTitle = !string.IsNullOrEmpty(_args.TitleKey);
            _title.text = hasTitle ? Text(_args.TitleKey, string.Empty) : string.Empty;
            _title.style.display = hasTitle ? DisplayStyle.Flex : DisplayStyle.None;

            _message.text = Text(_args.MessageKey, string.Empty);

            if (_args.Style == ConfirmStyle.Destructive)
            {
                _confirmButton.AddToClassList("confirm-ok--danger");
            }
            else
            {
                _confirmButton.RemoveFromClassList("confirm-ok--danger");
            }

            _root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;
            _args = default;
        }

        private void HandleConfirm()
        {
            _args.OnConfirm?.Invoke();
            PanelManager.Instance?.ClosePanel(PanelID.ConfirmPopup);
        }

        private void HandleCancel()
        {
            _args.OnCancel?.Invoke();
            PanelManager.Instance?.ClosePanel(PanelID.ConfirmPopup);
        }

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
