using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Staff;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class StaffRosterPanel : MonoBehaviour, IPanel
    {
        private const int REVIEW_OFFICE_BUILDING_ID = 6;
        private VisualElement _root;
        private VisualElement _list;

        public PanelID Id => PanelID.StaffRoster;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "staff-roster-panel" };
            _root.AddToClassList("p02-panel");
            _root.style.display = DisplayStyle.None;
            _list = new VisualElement { name = "staff-roster-list" };
            _root.Add(new Label(Text("ui.panel.staff.title", "Staff Roster")));
            _root.Add(_list);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<OnStaffHiredEvent>(HandleStaffChanged);
            EventBus.Subscribe<OnStaffFiredEvent>(HandleStaffChanged);
            EventBus.Subscribe<OnStaffAssignedEvent>(HandleStaffChanged);
            EventBus.Subscribe<OnStaffStateChangedEvent>(HandleStaffChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnStaffHiredEvent>(HandleStaffChanged);
            EventBus.Unsubscribe<OnStaffFiredEvent>(HandleStaffChanged);
            EventBus.Unsubscribe<OnStaffAssignedEvent>(HandleStaffChanged);
            EventBus.Unsubscribe<OnStaffStateChangedEvent>(HandleStaffChanged);
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
            _list.Clear();
            IReadOnlyDictionary<int, StaffInstance> roster = StaffService.Instance == null
                ? new Dictionary<int, StaffInstance>(0)
                : StaffService.Instance.GetRoster();

            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null)
                {
                    continue;
                }

                _list.Add(CreateRow(staff));
            }
        }

        private VisualElement CreateRow(StaffInstance staff)
        {
            StaffData data = DataManager.Instance == null ? null : DataManager.Instance.Get<StaffData>(staff.staffID);
            StaffStateView state = StaffService.Instance == null ? default : StaffService.Instance.GetStaffStateView(staff.instanceID);
            VisualElement row = new VisualElement { name = $"staff-row-{staff.instanceID}" };
            row.Add(new Label(data == null ? Text("ui.common.unknown", "Unknown") : data.name));
            row.Add(new Label($"{state.currentState} / {Text("ui.panel.staff.assigned", "Assigned")}: {state.assignedBuildingID}"));

            Button assignReview = new Button(() => Assign(staff.instanceID, REVIEW_OFFICE_BUILDING_ID))
            {
                text = Text("ui.panel.staff.assign.review", "Assign Review Office"),
                name = $"staff-assign-review-{staff.instanceID}"
            };
            assignReview.SetEnabled(REVIEW_OFFICE_BUILDING_ID > 0);
            row.Add(assignReview);

            Button unassign = new Button(() => Unassign(staff.instanceID))
            {
                text = Text("ui.panel.staff.unassign", "Unassign"),
                name = $"staff-unassign-{staff.instanceID}"
            };
            row.Add(unassign);
            return row;
        }

        private void Assign(int instanceID, int buildingID)
        {
            StaffService.Instance?.TryAssignStaff(instanceID, buildingID);
            Refresh();
        }

        private void Unassign(int instanceID)
        {
            StaffService.Instance?.TryUnassignStaff(instanceID);
            Refresh();
        }

        private void HandleStaffChanged(OnStaffHiredEvent evt) => Refresh();
        private void HandleStaffChanged(OnStaffFiredEvent evt) => Refresh();
        private void HandleStaffChanged(OnStaffAssignedEvent evt) => Refresh();
        private void HandleStaffChanged(OnStaffStateChangedEvent evt) => Refresh();

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
