using System.Collections.Generic;
using TheGuild.Core.Events;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Outcome.Events;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class AdventurerRosterPanel : MonoBehaviour, IPanel
    {
        private const int REVIEW_OFFICE_BUILDING_ID = 6;

        [SerializeField] private UIDocument _document;

        private VisualElement _root;
        private VisualElement _list;
        private VisualElement _details;

        public PanelID Id => PanelID.AdventurerRoster;
        public VisualElement Root => _root;

        private void Awake() => Build();

        private void OnEnable()
        {
            EventBus.Subscribe<OnAdventurerRecoveredEvent>(HandleRecovered);
            EventBus.Subscribe<OnAdventurerDismissedEvent>(HandleDismissed);
            EventBus.Subscribe<OnMissionResolvedEvent>(HandleMissionResolved);
            PanelManager.Instance?.RegisterPanel(this);
            if (REVIEW_OFFICE_BUILDING_ID < 0)
            {
                Debug.LogWarning("[AdventurerRosterPanel] REVIEW_OFFICE_BUILDING_ID < 0; dismiss button hidden.");
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnAdventurerRecoveredEvent>(HandleRecovered);
            EventBus.Unsubscribe<OnAdventurerDismissedEvent>(HandleDismissed);
            EventBus.Unsubscribe<OnMissionResolvedEvent>(HandleMissionResolved);
        }

        public void Open(object args)
        {
            Refresh();
            _root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _details.Clear();
            _root.style.display = DisplayStyle.None;
        }

        private void Build()
        {
            _root = new VisualElement { name = "adventurer-roster-panel" };
            _root.AddToClassList("p02-panel");
            _root.Add(new Label(Lookup("ui.panel.adventurer.title")));
            _list = new VisualElement { name = "adventurer-roster-list" };
            _details = new VisualElement { name = "adventurer-roster-details" };
            _root.Add(_list);
            _root.Add(_details);
            _root.style.display = DisplayStyle.None;
            _document?.rootVisualElement?.Add(_root);
        }

        private void Refresh()
        {
            _list.Clear();
            IReadOnlyList<AdventurerInstance> roster = AdventurerRoster.Instance == null
                ? new List<AdventurerInstance>(0)
                : AdventurerRoster.Instance.GetRoster();

            for (int i = 0; i < roster.Count; i++)
            {
                AdventurerInstance adventurer = roster[i];
                if (adventurer == null)
                {
                    continue;
                }

                Button row = new Button(() => ShowDetails(adventurer.instanceID))
                {
                    name = $"adventurer-row-{adventurer.instanceID}",
                    text = BuildCardText(adventurer)
                };
                _list.Add(row);
            }
        }

        private void ShowDetails(int instanceID)
        {
            AdventurerInstance adventurer = AdventurerRoster.Instance?.GetAdventurer(instanceID);
            _details.Clear();
            if (adventurer == null)
            {
                return;
            }

            _details.Add(new Label(adventurer.name));
            _details.Add(new Label(ResolveIntro()));
            _details.Add(new Label(ResolveProfessionName(adventurer.professionID)));
            _details.Add(new Label(ResolveRaceName(adventurer.raceID)));
            int[] traits = adventurer.traitIDs ?? new int[0];
            for (int i = 0; i < traits.Length; i++)
            {
                _details.Add(new Label(ResolveTraitText(traits[i])));
            }
            _details.Add(new Label(Lookup("ui.panel.adventurer.recentResult.placeholder")));

            Button dismiss = new Button(() => Dismiss(instanceID))
            {
                name = "dismiss-adventurer-button",
                text = ResolveDismissText(adventurer.status)
            };
            bool visible = ResolveDismissVisible(adventurer.status);
            dismiss.SetEnabled(ResolveDismissEnabled(adventurer.status));
            dismiss.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _details.Add(dismiss);
        }

        private void Dismiss(int instanceID)
        {
            PanelManager.Instance?.ShowConfirm(new ConfirmArgs(
                "confirm.adventurer.dismiss.body",
                () =>
                {
                    AdventurerRoster.Instance?.DismissAdventurer(instanceID);
                    Refresh();
                },
                null,
                ConfirmStyle.Destructive));
        }

        internal static bool SortsOpheliaFirst(AdventurerInstance a) => a != null && a.templateID == 901;

        private static string BuildCardText(AdventurerInstance adventurer)
        {
            return $"{adventurer.name} / {adventurer.rank} / {ResolveProfessionName(adventurer.professionID)} / {ResolveRaceName(adventurer.raceID)}";
        }

        internal static string ResolveDismissText(AdventurerStatus status)
        {
            return status == AdventurerStatus.Dead ? Lookup("btn.adv.remove") : Lookup("btn.adv.fire");
        }

        internal static bool ResolveDismissVisible(AdventurerStatus status)
        {
            if (status == AdventurerStatus.Dead) return true;
            if (status == AdventurerStatus.Idle) return REVIEW_OFFICE_BUILDING_ID >= 0;
            return false;
        }

        internal static bool ResolveDismissEnabled(AdventurerStatus status)
        {
            if (status == AdventurerStatus.Dead) return true;
            if (status != AdventurerStatus.Idle || REVIEW_OFFICE_BUILDING_ID < 0) return false;
            return BuildingService.Instance != null && BuildingService.Instance.GetBuildingLevel(REVIEW_OFFICE_BUILDING_ID) > 0;
        }

        private static string ResolveIntro()
        {
            return UITextService.Instance == null
                ? "（暫無敘述）"
                : UITextService.Instance.Lookup("text.adv.intro.placeholder", "（暫無敘述）");
        }

        private static string ResolveProfessionName(int professionID) => ProfessionService.Instance?.GetProfession(professionID)?.name ?? professionID.ToString();
        private static string ResolveRaceName(int raceID) => RaceService.Instance?.GetRace(raceID)?.name ?? raceID.ToString();
        private static string ResolveTraitText(int traitID) => TraitService.Instance?.GetTrait(traitID)?.name ?? traitID.ToString();

        private static string Lookup(string key)
        {
            return UITextService.Instance == null ? key : UITextService.Instance.Lookup(key, key);
        }

        private void HandleRecovered(OnAdventurerRecoveredEvent _) => Refresh();
        private void HandleDismissed(OnAdventurerDismissedEvent _) => Refresh();
        private void HandleMissionResolved(OnMissionResolvedEvent _) => Refresh();
    }
}
