using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Gacha;
using TheGuild.Gameplay.Gacha.Events;
using TheGuild.Gameplay.Staff;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;
using CandidateCard = TheGuild.Gameplay.Gacha.CandidateCard;

namespace TheGuild.UI.Panels
{
    public sealed class StaffGachaPanel : MonoBehaviour, IPanel
    {
        private VisualElement _root;
        private VisualElement _currentList;
        private VisualElement _reserveList;

        public PanelID Id => PanelID.StaffGacha;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "staff-gacha-panel" };
            _root.style.display = DisplayStyle.None;
            _currentList = new VisualElement { name = "staff-gacha-current" };
            _reserveList = new VisualElement { name = "staff-gacha-reserve" };
            Button refresh = new Button(RefreshCandidates)
            {
                text = Text("ui.panel.gacha.refresh", "Refresh"),
                name = "staff-gacha-refresh"
            };
            _root.Add(new Label(Text("ui.panel.gacha.title", "Staff Interview")));
            _root.Add(refresh);
            _root.Add(_currentList);
            _root.Add(_reserveList);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<OnGachaStateDirtyEvent>(HandleGachaDirty);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnGachaStateDirtyEvent>(HandleGachaDirty);
        }

        public void Open(object args)
        {
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            _currentList.Clear();
            _reserveList.Clear();

            IReadOnlyList<CandidateCard> current = GachaService.Instance == null
                ? new List<CandidateCard>(0)
                : GachaService.Instance.GetCurrentCandidates();
            for (int i = 0; i < current.Count; i++)
            {
                _currentList.Add(CreateCandidate(current[i], false, i));
            }

            IReadOnlyList<CandidateCard> reserved = GachaService.Instance == null
                ? new List<CandidateCard>(0)
                : GachaService.Instance.GetReservedCandidates();
            for (int i = 0; i < reserved.Count; i++)
            {
                _reserveList.Add(CreateCandidate(reserved[i], true, i));
            }
        }

        private VisualElement CreateCandidate(CandidateCard card, bool reserved, int index)
        {
            VisualElement row = new VisualElement { name = reserved ? $"staff-reserve-{index}" : $"staff-candidate-{index}" };
            StaffData data = card == null || DataManager.Instance == null ? null : DataManager.Instance.Get<StaffData>(card.staffID);
            row.Add(new Label(data == null ? Text("ui.panel.gacha.empty", "Empty") : data.name));

            if (reserved)
            {
                row.Add(new Button(() => ReleaseReserve(index)) { text = Text("ui.panel.gacha.release", "Release") });
                return row;
            }

            int slot = card == null ? index : card.slotIndex;
            row.Add(new Button(() => Recruit(slot)) { text = Text("ui.panel.gacha.recruit", "Recruit") });
            row.Add(new Button(() => Reserve(slot)) { text = Text("ui.panel.gacha.reserve", "Reserve") });
            row.Add(new Button(() => Reject(slot)) { text = Text("ui.panel.gacha.reject", "Reject") });
            return row;
        }

        private void RefreshCandidates()
        {
            GachaService.Instance?.TryManualRefresh();
            Refresh();
        }

        private void Recruit(int slotIndex)
        {
            GachaService.Instance?.TryRecruit(slotIndex);
            PanelManager.Instance?.ClosePanel(PanelID.StaffGacha, () => PanelManager.Instance.OpenPanel(PanelID.StaffRoster));
        }

        private void Reserve(int slotIndex)
        {
            GachaService.Instance?.TryReserveCandidate(slotIndex);
            Refresh();
        }

        private void Reject(int slotIndex)
        {
            GachaService.Instance?.TryRejectCandidate(slotIndex);
            Refresh();
        }

        private void ReleaseReserve(int index)
        {
            GachaService.Instance?.TryReleaseReserve(index);
            Refresh();
        }

        private void HandleGachaDirty(OnGachaStateDirtyEvent evt) => Refresh();

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
