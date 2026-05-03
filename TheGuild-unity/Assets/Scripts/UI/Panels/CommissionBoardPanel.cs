using System.Collections.Generic;
using TheGuild.Core.Events;
using TheGuild.Gameplay.GoldFlow.Events;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.Decision.Events;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class CommissionBoardPanel : MonoBehaviour, IPanel
    {
        private readonly Dictionary<int, string> _missionStates = new Dictionary<int, string>();
        private VisualElement _root;
        private VisualElement _regularList;
        private VisualElement _staticList;
        private RecommendAdventurerSubpanel _recommendSubpanel;

        public PanelID Id => PanelID.CommissionBoard;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "commission-board-panel" };
            _root.AddToClassList("p02-panel");
            _root.style.display = DisplayStyle.None;
            _regularList = new VisualElement { name = "commission-regular-list" };
            _staticList = new VisualElement { name = "commission-static-list" };
            _recommendSubpanel = new RecommendAdventurerSubpanel();

            _root.Add(new Label(Text("ui.panel.commission.title", "Commission Board")));
            _root.Add(_regularList);
            _root.Add(_staticList);
            _root.Add(_recommendSubpanel.Root);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<OnCommissionPostedEvent>(HandleCommissionPosted);
            EventBus.Subscribe<OnCommissionAcceptedEvent>(HandleCommissionAccepted);
            EventBus.Subscribe<OnCommissionSettledEvent>(HandleCommissionSettled);
            EventBus.Subscribe<OnAutoPickupEvent>(HandleAutoPickup);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCommissionPostedEvent>(HandleCommissionPosted);
            EventBus.Unsubscribe<OnCommissionAcceptedEvent>(HandleCommissionAccepted);
            EventBus.Unsubscribe<OnCommissionSettledEvent>(HandleCommissionSettled);
            EventBus.Unsubscribe<OnAutoPickupEvent>(HandleAutoPickup);
        }

        public void Open(object args)
        {
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Close()
        {
            _recommendSubpanel.Hide();
            _root.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            _regularList.Clear();
            _staticList.Clear();
            AddSection(_regularList, CommissionSource.Regular);
            AddSection(_staticList, CommissionSource.Static);
        }

        private void AddSection(VisualElement container, CommissionSource source)
        {
            container.Add(new Label(Text(source == CommissionSource.Static ? "ui.panel.commission.static" : "ui.panel.commission.regular", source.ToString())));
            IReadOnlyList<int> ids = CommissionBoardService.Instance == null
                ? new List<int>(0)
                : CommissionBoardService.Instance.GetCommissionsBySource(source);

            for (int i = 0; i < ids.Count; i++)
            {
                container.Add(CreateMissionCard(ids[i]));
            }
        }

        private VisualElement CreateMissionCard(int missionID)
        {
            VisualElement card = new VisualElement { name = $"commission-card-{missionID}" };
            MissionTemplate template = MissionDatabaseService.Instance == null ? null : MissionDatabaseService.Instance.GetTemplate(missionID);
            (string name, string desc) text = template == null || MissionDatabaseService.Instance == null
                ? (Text("text.mission.name.placeholder", $"Mission {missionID}"), Text("text.adv.intro.placeholder", "（暫無敘述）"))
                : MissionDatabaseService.Instance.GetMissionText(template.difficulty, template.typeID);

            card.Add(new Label(string.IsNullOrEmpty(text.name) ? Text("text.mission.name.placeholder", $"Mission {missionID}") : text.name));
            card.Add(new Label(string.IsNullOrEmpty(text.desc) ? Text("text.adv.intro.placeholder", "（暫無敘述）") : text.desc));
            if (_missionStates.TryGetValue(missionID, out string stateKey))
            {
                card.Add(new Label(Text(stateKey, stateKey)));
            }

            Button dispatch = new Button(() => _recommendSubpanel.Show(missionID))
            {
                text = Text("ui.panel.commission.dispatch", "Dispatch"),
                name = $"commission-dispatch-{missionID}"
            };
            card.Add(dispatch);
            return card;
        }

        private void HandleCommissionPosted(OnCommissionPostedEvent evt)
        {
            _missionStates[evt.MissionID] = "ui.panel.commission.state.posted";
            Refresh();
        }

        private void HandleCommissionAccepted(OnCommissionAcceptedEvent evt)
        {
            _missionStates[evt.MissionID] = "ui.panel.commission.state.accepted";
            Refresh();
        }

        private void HandleCommissionSettled(OnCommissionSettledEvent evt)
        {
            _missionStates[evt.Breakdown.MissionID] = "ui.panel.commission.state.settled";
            Refresh();
        }

        private void HandleAutoPickup(OnAutoPickupEvent evt)
        {
            _missionStates[evt.MissionID] = "ui.panel.commission.state.auto";
            Refresh();
        }

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
