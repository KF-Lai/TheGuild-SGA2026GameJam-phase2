using NUnit.Framework;
using System.Reflection;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.UI.Core;
using TheGuild.UI.Panels;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.UI.Panels
{
    public sealed class PanelConstructionTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("panel-test");
            // GuildOverviewPanel._styleBiasWarningLogged 為靜態 guard，跨 session 持久；測試前重置以保證 LogAssert.Expect 一致觸發。
            FieldInfo guard = typeof(GuildOverviewPanel)
                .GetField("_styleBiasWarningLogged", BindingFlags.Static | BindingFlags.NonPublic);
            guard?.SetValue(null, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void DoD_B1_CommissionBoard_ConstructsRoot()
        {
            CommissionBoardPanel panel = Create<CommissionBoardPanel>();
            AssertPanel(panel, PanelID.CommissionBoard);
        }

        [Test]
        public void DoD_B2_AdventurerRoster_ConstructsRoot()
        {
            AdventurerRosterPanel panel = Create<AdventurerRosterPanel>();
            AssertPanel(panel, PanelID.AdventurerRoster);
        }

        [Test]
        public void DoD_B3_GuildBuilding_ConstructsRoot()
        {
            GuildBuildingPanel panel = Create<GuildBuildingPanel>();
            AssertPanel(panel, PanelID.GuildBuilding);
        }

        [Test]
        public void DoD_B4_StaffRoster_ConstructsRoot()
        {
            StaffRosterPanel panel = Create<StaffRosterPanel>();
            AssertPanel(panel, PanelID.StaffRoster);
        }

        [Test]
        public void DoD_B5_StaffGacha_ConstructsRoot()
        {
            StaffGachaPanel panel = Create<StaffGachaPanel>();
            AssertPanel(panel, PanelID.StaffGacha);
        }

        [Test]
        public void DoD_B9_GuildOverview_ConstructsRoot()
        {
            LogAssert.Expect(LogType.Warning, "[GuildOverviewPanel] FactionStoryService.GetCurrentStyleTagBias() unavailable; fallback to 'neutral'.");
            GuildOverviewPanel panel = Create<GuildOverviewPanel>();
            AssertPanel(panel, PanelID.GuildOverview);
        }

        [Test]
        public void DoD_B10_StoryDialogue_ConstructsRoot()
        {
            StoryDialoguePanel panel = Create<StoryDialoguePanel>();
            AssertPanel(panel, PanelID.StoryDialogue);
        }

        [Test]
        public void DoD_B11_ConfirmPopup_ConstructsRoot()
        {
            ConfirmPopup panel = Create<ConfirmPopup>();
            AssertPanel(panel, PanelID.ConfirmPopup);
        }

        [Test]
        public void DoD_B12_RecommendSubpanel_ConstructsHiddenRoot()
        {
            RecommendAdventurerSubpanel subpanel = new RecommendAdventurerSubpanel();
            Assert.NotNull(subpanel.Root);
            Assert.AreEqual("recommend-adventurer-subpanel", subpanel.Root.name);
        }

        [Test]
        public void DoD_B13_StoryDialoguePublishesGameplayOpheliaEvent()
        {
            StoryDialoguePanel panel = Create<StoryDialoguePanel>();
            int stage = 0;
            void Handler(OnOpheliaMissingNightEvent evt) => stage = evt.StageID;

            EventBus.Subscribe<OnOpheliaMissingNightEvent>(Handler);
            try
            {
                panel.Open(new StoryDialogueOpenArgs(4, "missing-key", "ophelia_missing", string.Empty));
                typeof(StoryDialoguePanel)
                    .GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(panel, null);
                Assert.AreEqual(4, stage);
            }
            finally
            {
                EventBus.Unsubscribe<OnOpheliaMissingNightEvent>(Handler);
            }
        }

        [Test]
        public void DoD_B14_SettingsPanel_IsCreated()
        {
            // B2 scope（2026-05-03 落地）：SettingsPanel 已實作，原 B1 斷言「不存在」反轉為「存在」。
            Assert.IsNotNull(System.Type.GetType("TheGuild.UI.Panels.SettingsPanel, TheGuild.UI.Panels"));
        }

        [Test]
        public void DoD_B16_IPanel_LivesInCoreNamespace()
        {
            Assert.AreEqual("TheGuild.UI.Core", typeof(IPanel).Namespace);
        }

        private T Create<T>() where T : Component
        {
            T component = _go.AddComponent<T>();
            // EditMode 不自動觸發 Awake/OnEnable；用 reflection 手動呼叫以對齊 PlayMode 行為（FSD-A 既有測試先例：UIBootstrapController.SetUIReadyForTests 內部反射模式）。
            typeof(T)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, null);
            typeof(T)
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, null);
            return component;
        }

        private static void AssertPanel(IPanel panel, PanelID id)
        {
            Assert.NotNull(panel);
            Assert.NotNull(panel.Root);
            Assert.AreEqual(id, panel.Id);
        }
    }
}
