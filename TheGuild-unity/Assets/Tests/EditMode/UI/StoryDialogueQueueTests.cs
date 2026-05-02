using System;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.UI
{
    public sealed class StoryDialogueQueueTests
    {
        private GameObject _queueGo;
        private GameObject _panelGo;
        private GameObject _bootstrapGo;
        private StoryDialogueQueue _queue;
        private PanelManager _panelManager;

        [SetUp]
        public void SetUp()
        {
            InvokeStatic(typeof(EventBus), "ClearAll");

            _bootstrapGo = new GameObject("UIBootstrap_Queue_Test");
            UIBootstrapController bootstrap = _bootstrapGo.AddComponent<UIBootstrapController>();
            bootstrap.SetUIReadyForTests(true);

            _panelGo = new GameObject("PanelManager_Queue_Test");
            _panelManager = _panelGo.AddComponent<PanelManager>();
            _panelManager.RegisterPanelRoot(PanelID.SettingsPanel, new VisualElement());
            _panelManager.RegisterPanelRoot(PanelID.StoryDialogue, new VisualElement());

            _queueGo = new GameObject("StoryDialogueQueue_Test");
            _queue = _queueGo.AddComponent<StoryDialogueQueue>();
            _queue.SubscribeEvents();

            _panelManager.OpenPanel(PanelID.SettingsPanel);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_queueGo);
            Object.DestroyImmediate(_panelGo);
            Object.DestroyImmediate(_bootstrapGo);
            InvokeStatic(typeof(EventBus), "ClearAll");
        }

        [Test]
        public void DoD_A4_EnqueueIfAbsent_DedupesSameStageID()
        {
            Assert.IsTrue(_queue.EnqueueIfAbsent(3, "dialogue.stage3"));
            Assert.IsFalse(_queue.EnqueueIfAbsent(3, "dialogue.stage3"));

            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void DoD_A4_TryStartNext_DoesNotStartWhenTopPanelExists()
        {
            _queue.EnqueueIfAbsent(3, "dialogue.stage3");

            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void DoD_A9_EC04_SubscribedStageUnlockedEventUsesDedupe()
        {
            EventBus.Publish(new OnFactionStoryStageUnlockedEvent(3, 1, 1, 101, "dialogue.stage3"));
            EventBus.Publish(new OnFactionStoryStageUnlockedEvent(3, 1, 1, 101, "dialogue.stage3"));

            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        [Ignore("Chain continue 倚賴 PanelStateMachine Closing 動畫完成 callback；EditMode 環境動畫驅動不穩定，覆蓋責任移至 StoryDialogueQueuePlayModeTests.DoD_A4_EC08_ConfirmCurrentDialogue_ChainsNextStage。")]
        public void DoD_A4_EC08_ConfirmCurrentDialogue_ChainsNextStage()
        {
        }

        private static void InvokeStatic(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }

        private static void SetProperty(object target, string name, object value)
        {
            // 直接寫 auto-property 的 backing field。
            FieldInfo backingField = target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(backingField);
            backingField.SetValue(target, value);
        }
    }
}
