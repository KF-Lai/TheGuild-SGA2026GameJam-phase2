using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class ConfirmPopup : MonoBehaviour, IPanel
    {
        private VisualElement _root;
        private Label _message;
        private Button _confirmButton;
        private Button _cancelButton;
        private ConfirmArgs _args;

        public PanelID Id => PanelID.ConfirmPopup;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "confirm-popup" };
            _root.style.display = DisplayStyle.None;

            _message = new Label { name = "confirm-message" };
            _confirmButton = new Button(HandleConfirm) { name = "confirm-ok" };
            _cancelButton = new Button(HandleCancel) { name = "confirm-cancel" };
            _confirmButton.text = Text("ui.common.confirm", "Confirm");
            _cancelButton.text = Text("ui.common.cancel", "Cancel");

            _root.Add(_message);
            _root.Add(_confirmButton);
            _root.Add(_cancelButton);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
        }

        public void Open(object args)
        {
            _args = args is ConfirmArgs confirmArgs ? confirmArgs : default;
            _message.text = Text(_args.MessageKey, string.Empty);
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
