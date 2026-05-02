using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Resources.Events;
using TheGuild.Gameplay.WorldDanger;
using TheGuild.Gameplay.WorldDanger.Events;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    public sealed class GuildOverviewPanel : MonoBehaviour, IPanel
    {
        private static bool _styleBiasWarningLogged;

        private VisualElement _root;
        private Label _summary;
        private Label _faction;

        public PanelID Id => PanelID.GuildOverview;
        public VisualElement Root => _root;

        private void Awake()
        {
            _root = new VisualElement { name = "guild-overview-panel" };
            _root.style.display = DisplayStyle.None;
            _summary = new Label { name = "guild-overview-summary" };
            _faction = new Label { name = "guild-overview-faction" };
            _root.Add(new Label(Text("ui.panel.overview.title", "Guild Overview")));
            _root.Add(_summary);
            _root.Add(_faction);
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
            EventBus.Subscribe<OnGoldChangedEvent>(HandleGoldChanged);
            EventBus.Subscribe<OnReputationChangedEvent>(HandleReputationChanged);
            EventBus.Subscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyChanged);
            EventBus.Subscribe<OnGuildLevelChangedEvent>(HandleGuildLevelChanged);
            EventBus.Subscribe<OnDangerLevelChangedEvent>(HandleDangerChanged);
            EventBus.Subscribe<OnFactionScoreChangedEvent>(HandleFactionScoreChanged);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleFactionRouteCompleted);

            if (!_styleBiasWarningLogged)
            {
                Debug.LogWarning("[GuildOverviewPanel] FactionStoryService.GetCurrentStyleTagBias() unavailable; fallback to 'neutral'.");
                _styleBiasWarningLogged = true;
            }

            _root.schedule.Execute(Refresh).Every(1000);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnGoldChangedEvent>(HandleGoldChanged);
            EventBus.Unsubscribe<OnReputationChangedEvent>(HandleReputationChanged);
            EventBus.Unsubscribe<OnBankruptcyStateChangedEvent>(HandleBankruptcyChanged);
            EventBus.Unsubscribe<OnGuildLevelChangedEvent>(HandleGuildLevelChanged);
            EventBus.Unsubscribe<OnDangerLevelChangedEvent>(HandleDangerChanged);
            EventBus.Unsubscribe<OnFactionScoreChangedEvent>(HandleFactionScoreChanged);
            EventBus.Unsubscribe<OnFactionRouteCompletedEvent>(HandleFactionRouteCompleted);
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
            int gold = ResourceManagement.Instance == null ? 0 : ResourceManagement.Instance.GetGold();
            int reputation = ResourceManagement.Instance == null ? 0 : ResourceManagement.Instance.GetReputation();
            BankruptcyWarningState bankruptcy = ResourceManagement.Instance == null
                ? BankruptcyWarningState.Normal
                : ResourceManagement.Instance.GetBankruptcyWarningState();
            int level = GuildCoreService.Instance == null ? 0 : GuildCoreService.Instance.GetCurrentLevel();
            string title = GuildCoreService.Instance == null ? string.Empty : GuildCoreService.Instance.GetCurrentTitle();
            string dangerLevel = WorldDangerService.Instance == null ? string.Empty : WorldDangerService.Instance.GetCurrentLevel();
            WorldDangerData danger = WorldDangerService.Instance == null ? null : WorldDangerService.Instance.GetDangerData(dangerLevel);

            _summary.text =
                $"{Text("ui.panel.overview.level", "Level")}: {level} {title}\n" +
                $"{Text("ui.panel.overview.gold", "Gold")}: {gold}\n" +
                $"{Text("ui.panel.overview.reputation", "Reputation")}: {reputation}\n" +
                $"{Text("ui.panel.overview.bankruptcy", "Bankruptcy")}: {bankruptcy}\n" +
                $"{Text("ui.panel.overview.danger", "Danger")}: {(danger == null ? dangerLevel : danger.name)}\n" +
                $"{Text("ui.panel.overview.style", "Style")}: {ResolveStyleBias()}";

            int score = FactionStoryService.Instance == null ? 0 : FactionStoryService.Instance.GetCurrentFactionScore(1);
            int max = FactionStoryService.Instance == null ? 0 : FactionStoryService.Instance.GetMaxFactionScore();
            _faction.text = $"{Text("ui.panel.overview.faction", "Faction")}: {score}/{max}";
        }

        private static string ResolveStyleBias()
        {
            return "neutral";
        }

        private void HandleGoldChanged(OnGoldChangedEvent evt) => Refresh();
        private void HandleReputationChanged(OnReputationChangedEvent evt) => Refresh();
        private void HandleBankruptcyChanged(OnBankruptcyStateChangedEvent evt) => Refresh();
        private void HandleGuildLevelChanged(OnGuildLevelChangedEvent evt) => Refresh();
        private void HandleDangerChanged(OnDangerLevelChangedEvent evt) => Refresh();
        private void HandleFactionScoreChanged(OnFactionScoreChangedEvent evt) => Refresh();
        private void HandleFactionRouteCompleted(OnFactionRouteCompletedEvent evt) => Refresh();

        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null ? fallback : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
