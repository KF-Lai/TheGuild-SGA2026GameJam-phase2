using UnityEngine.UIElements;

namespace TheGuild.UI.Core
{
    public interface IPanel
    {
        PanelID Id { get; }
        VisualElement Root { get; }
        void Open(object args);
        void Close();
    }
}
