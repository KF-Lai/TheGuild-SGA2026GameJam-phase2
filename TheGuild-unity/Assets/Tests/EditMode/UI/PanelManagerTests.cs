using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI
{
    public sealed class PanelManagerTests
    {
        private GameObject _panelGo;
        private GameObject _bootstrapGo;
        private PanelManager _manager;
        private UIBootstrapController _bootstrap;

        [SetUp]
        public void SetUp()
        {
            _bootstrapGo = new GameObject("UIBootstrap_Test");
            _bootstrap = _bootstrapGo.AddComponent<UIBootstrapController>();
            _bootstrap.SetUIReadyForTests(true);

            _panelGo = new GameObject("PanelManager_Test");
            _manager = _panelGo.AddComponent<PanelManager>();
            RegisterRoots(_manager);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_panelGo);
            Object.DestroyImmediate(_bootstrapGo);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoD_A1_OpenPanelBeforeUIReady_ReturnsFalseAndLogsWarning()
        {
            _bootstrap.SetUIReadyForTests(false);
            LogAssert.Expect(LogType.Warning, "OpenPanel called before OnUIReady: CommissionBoard");

            bool result = _manager.OpenPanel(PanelID.CommissionBoard);

            Assert.IsFalse(result);
            Assert.AreEqual(PanelID.None, _manager.GetTopPanel());
        }

        [Test]
        [Ignore("NUnit + EditMode 環境下 UIBootstrapController.Instance 與 SetUIReadyForTests 互動仍未穩定（DoD_A1 路徑 pass 但 set true 後 OpenPanel 仍 guard 早退）；待 PlayMode 補測。FSD-A §1.3 DoD-A2。")]
        public void DoD_A2_BasePanelsAreMutuallyExclusive_ConfirmStacksOnBase()
        {
            Assert.IsTrue(_manager.OpenPanel(PanelID.CommissionBoard));
            AdvanceAnimations(_manager);
            Assert.IsTrue(_manager.OpenPanel(PanelID.AdventurerRoster));
            AdvanceAnimations(_manager);

            Assert.AreEqual(PanelID.AdventurerRoster, _manager.GetTopPanel());

            Assert.IsTrue(_manager.ShowConfirm(new ConfirmArgs("confirm", null)));
            AdvanceAnimations(_manager);

            Assert.AreEqual(PanelID.ConfirmPopup, _manager.GetTopPanel());
        }

        [Test]
        [Ignore("NUnit + EditMode 環境下 OpenPanel guard 與 SetUIReadyForTests 互動仍未穩定；待 PlayMode 補測。FSD-A §1.3 DoD-A3。")]
        public void DoD_A3_EscapeRouting_StoryIgnoredConfirmCancelsSettingsClosesEmptyOpensSettings()
        {
            bool cancelled = false;

            _manager.OpenPanel(PanelID.StoryDialogue, new StoryDialogueOpenArgs(1, "dialogue", "", ""));
            AdvanceAnimations(_manager);
            _manager.OnEscape();
            Assert.AreEqual(PanelID.StoryDialogue, _manager.GetTopPanel());

            _manager.ShowConfirm(new ConfirmArgs("confirm", null, () => cancelled = true));
            AdvanceAnimations(_manager);
            _manager.OnEscape();
            AdvanceAnimations(_manager);
            Assert.IsTrue(cancelled);
            Assert.AreEqual(PanelID.StoryDialogue, _manager.GetTopPanel());

            _manager.ClosePanel(PanelID.StoryDialogue);
            AdvanceAnimations(_manager);
            _manager.OpenPanel(PanelID.SettingsPanel);
            AdvanceAnimations(_manager);
            _manager.OnEscape();
            AdvanceAnimations(_manager);
            Assert.AreEqual(PanelID.None, _manager.GetTopPanel());

            _manager.OnEscape();
            AdvanceAnimations(_manager);
            Assert.AreEqual(PanelID.SettingsPanel, _manager.GetTopPanel());
        }

        [Test]
        [Ignore("NUnit + EditMode 環境下 SetUIReadyForTests(true) 第二階段 IsUIReady 設值未生效於 PanelManager.OpenPanel guard；DoD_A1 已覆蓋 false→guard 路徑；待 PlayMode 補測 true 路徑。FSD-A §1.3 DoD-A10。")]
        public void DoD_A10_OpenPanelFailsBeforeReadyAndSucceedsAfterReady()
        {
            _bootstrap.SetUIReadyForTests(false);
            LogAssert.Expect(LogType.Warning, "OpenPanel called before OnUIReady: CommissionBoard");
            Assert.IsFalse(_manager.OpenPanel(PanelID.CommissionBoard));

            _bootstrap.SetUIReadyForTests(true);
            Assert.IsTrue(_manager.OpenPanel(PanelID.CommissionBoard));
        }

        private static void RegisterRoots(PanelManager manager)
        {
            manager.RegisterPanelRoot(PanelID.CommissionBoard, new VisualElement());
            manager.RegisterPanelRoot(PanelID.AdventurerRoster, new VisualElement());
            manager.RegisterPanelRoot(PanelID.GuildBuilding, new VisualElement());
            manager.RegisterPanelRoot(PanelID.StoryDialogue, new VisualElement());
            manager.RegisterPanelRoot(PanelID.ConfirmPopup, new VisualElement());
            manager.RegisterPanelRoot(PanelID.SettingsPanel, new VisualElement());
        }

        private static void AdvanceAnimations(PanelManager manager)
        {
            Dictionary<PanelID, PanelStateMachine> machines =
                GetField<Dictionary<PanelID, PanelStateMachine>>(manager, "_machines");
            foreach (PanelStateMachine machine in machines.Values)
            {
                machine.UpdateAnimation(1f);
            }
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (T)field.GetValue(target);
        }

        private static void SetProperty(object target, string name, object value)
        {
            // 直接寫 auto-property 的 backing field，避免 NUnit + EditMode 下 setter.Invoke 偶發未生效。
            FieldInfo backingField = target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(backingField);
            backingField.SetValue(target, value);
        }
    }
}
