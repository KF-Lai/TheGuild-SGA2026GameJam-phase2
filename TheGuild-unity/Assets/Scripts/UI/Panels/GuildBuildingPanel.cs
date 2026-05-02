using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Building;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class GuildBuildingPanel : MonoBehaviour, IPanel
    {
        private VisualElement _root;
        private VisualElement _list;

        public PanelID Id => PanelID.GuildBuilding;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "guild-building-panel" };
            _root.style.display = DisplayStyle.None;
            _list = new VisualElement { name = "guild-building-list" };
            _root.Add(new Label(Text("ui.panel.building.title", "Guild Building")));
            _root.Add(_list);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<BuildingUpgradedEvent>(HandleBuildingUpgraded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BuildingUpgradedEvent>(HandleBuildingUpgraded);
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
            IReadOnlyList<BuildingRow> rows = DataManager.Instance == null
                ? new List<BuildingRow>(0)
                : DataManager.Instance.GetAll<BuildingRow>();
            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < rows.Count; i++)
            {
                BuildingRow row = rows[i];
                if (row == null || !seen.Add(row.buildingID))
                {
                    continue;
                }

                _list.Add(CreateRow(row.buildingID));
            }
        }

        private VisualElement CreateRow(int buildingID)
        {
            int level = BuildingService.Instance == null ? 0 : BuildingService.Instance.GetBuildingLevel(buildingID);
            BuildingRow next = DataManager.Instance == null ? null : DataManager.Instance.Get<BuildingRow>($"{buildingID}_{level + 1}");
            VisualElement row = new VisualElement { name = $"building-row-{buildingID}" };
            row.Add(new Label(next == null ? Text("ui.panel.building.unknown", $"Building {buildingID}") : next.name));
            row.Add(new Label(Text("ui.panel.building.level", "Level") + $": {level}"));

            Button upgrade = new Button(() => TryUpgrade(buildingID))
            {
                text = Text("ui.panel.building.upgrade", "Upgrade"),
                name = $"building-upgrade-{buildingID}"
            };
            upgrade.SetEnabled(BuildingService.Instance != null && BuildingService.Instance.CanUpgrade(buildingID));
            if (next != null)
            {
                upgrade.tooltip = Text("ui.panel.building.upgrade.cost", "Cost") + $": {next.upgradeCost}";
            }
            row.Add(upgrade);
            return row;
        }

        private void TryUpgrade(int buildingID)
        {
            if (BuildingService.Instance != null)
            {
                BuildingService.Instance.TryUpgradeBuilding(buildingID);
            }
            Refresh();
        }

        private void HandleBuildingUpgraded(BuildingUpgradedEvent evt) => Refresh();

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
