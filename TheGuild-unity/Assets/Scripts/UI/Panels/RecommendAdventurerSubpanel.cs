using System;
using System.Collections.Generic;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.UI.Core;
using UnityEngine.UIElements;

namespace TheGuild.UI.Panels
{
    public sealed class RecommendAdventurerSubpanel
    {
        private readonly VisualElement _root;
        private readonly VisualElement _list;
        private int _missionID;

        public RecommendAdventurerSubpanel()
        {
            _root = new VisualElement { name = "recommend-adventurer-subpanel" };
            _list = new VisualElement { name = "recommend-adventurer-list" };
            _root.Add(new Label(Text("ui.panel.commission.recommend.title", "Recommended Adventurers")));
            _root.Add(_list);
            _root.style.display = DisplayStyle.None;
        }

        public VisualElement Root => _root;

        public void Show(int missionID)
        {
            _missionID = missionID;
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            _list.Clear();
        }

        private void Refresh()
        {
            _list.Clear();
            IReadOnlyList<AdventurerInstance> roster = AdventurerRoster.Instance == null
                ? Array.Empty<AdventurerInstance>()
                : AdventurerRoster.Instance.GetRoster();

            for (int i = 0; i < roster.Count; i++)
            {
                AdventurerInstance adventurer = roster[i];
                if (adventurer == null || adventurer.status != AdventurerStatus.Idle)
                {
                    continue;
                }

                Button button = new Button(() => Dispatch(adventurer.instanceID))
                {
                    text = adventurer.name,
                    name = $"recommend-adventurer-{adventurer.instanceID}"
                };
                _list.Add(button);
            }

            if (_list.childCount == 0)
            {
                _list.Add(new Label(Text("ui.panel.commission.recommend.empty", "No idle adventurer")));
            }
        }

        private void Dispatch(int instanceID)
        {
            bool dispatched = MissionDispatchService.Instance != null
                && MissionDispatchService.Instance.Dispatch(instanceID, _missionID, DispatchSource.PlayerManual);

            if (!dispatched)
            {
                return;
            }

            Hide();
        }

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
