using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.UI
{
    /// <summary>
    /// P-02-FSD-A DoD-A2 / A3 / A10 PlayMode 補測：
    /// PlayMode 環境下 Application.isPlaying=true，OnEnable 自然觸發、SetUIReadyForTests 可靠生效。
    /// 對應 EditMode 同名 [Ignore] tests（NUnit + EditMode reflection setter 不穩定限制）。
    /// </summary>
    public sealed class PanelManagerPlayModeTests
    {
        private GameObject _bootstrapGo;
        private GameObject _panelGo;
        private UIBootstrapController _bootstrap;
        private PanelManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _bootstrapGo = new GameObject("UIBootstrap_PM_PlayMode");
            _bootstrap = _bootstrapGo.AddComponent<UIBootstrapController>();

            _panelGo = new GameObject("PanelManager_PlayMode");
            _manager = _panelGo.AddComponent<PanelManager>();

            // 等一 frame 讓 OnEnable 完整觸發。
            yield return null;

            _bootstrap.SetUIReadyForTests(true);
            RegisterRoots(_manager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_panelGo != null) Object.DestroyImmediate(_panelGo);
            if (_bootstrapGo != null) Object.DestroyImmediate(_bootstrapGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DoD_A2_BasePanelsAreMutuallyExclusive_ConfirmStacksOnBase()
        {
            Assert.IsTrue(_manager.OpenPanel(PanelID.CommissionBoard));
            AdvanceAnimations(_manager);
            yield return null;

            Assert.IsTrue(_manager.OpenPanel(PanelID.AdventurerRoster));
            AdvanceAnimations(_manager);
            yield return null;

            Assert.AreEqual(PanelID.AdventurerRoster, _manager.GetTopPanel());

            Assert.IsTrue(_manager.ShowConfirm(new ConfirmArgs("confirm", null)));
            AdvanceAnimations(_manager);
            yield return null;

            Assert.AreEqual(PanelID.ConfirmPopup, _manager.GetTopPanel());
        }

        [UnityTest]
        public IEnumerator DoD_A3_EscapeRouting_StoryIgnoredConfirmCancelsSettingsClosesEmptyOpensSettings()
        {
            bool cancelled = false;

            _manager.OpenPanel(PanelID.StoryDialogue, new StoryDialogueOpenArgs(1, "dialogue", "", ""));
            AdvanceAnimations(_manager);
            yield return null;
            _manager.OnEscape();
            yield return null;
            Assert.AreEqual(PanelID.StoryDialogue, _manager.GetTopPanel());

            _manager.ShowConfirm(new ConfirmArgs("confirm", null, () => cancelled = true));
            AdvanceAnimations(_manager);
            yield return null;
            _manager.OnEscape();
            AdvanceAnimations(_manager);
            yield return null;
            Assert.IsTrue(cancelled);
            Assert.AreEqual(PanelID.StoryDialogue, _manager.GetTopPanel());

            _manager.ClosePanel(PanelID.StoryDialogue);
            AdvanceAnimations(_manager);
            yield return null;
            _manager.OpenPanel(PanelID.SettingsPanel);
            AdvanceAnimations(_manager);
            yield return null;
            _manager.OnEscape();
            AdvanceAnimations(_manager);
            yield return null;
            Assert.AreEqual(PanelID.None, _manager.GetTopPanel());

            _manager.OnEscape();
            AdvanceAnimations(_manager);
            yield return null;
            Assert.AreEqual(PanelID.SettingsPanel, _manager.GetTopPanel());
        }

        [UnityTest]
        public IEnumerator DoD_A10_OpenPanelFailsBeforeReadyAndSucceedsAfterReady()
        {
            _bootstrap.SetUIReadyForTests(false);
            LogAssert.Expect(LogType.Warning, "OpenPanel called before OnUIReady: CommissionBoard");
            Assert.IsFalse(_manager.OpenPanel(PanelID.CommissionBoard));
            yield return null;

            _bootstrap.SetUIReadyForTests(true);
            yield return null;
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
    }
}
