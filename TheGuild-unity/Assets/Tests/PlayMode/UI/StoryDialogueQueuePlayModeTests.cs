using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Gameplay.FactionStory;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.UI
{
    /// <summary>
    /// P-02-FSD-A DoD-A4 / EC-08 PlayMode 補測：故事面板 chain continue。
    /// FSD-A §5.4.6：ConfirmDialogue OK → ClosePanel → Closing 動畫結束後若 queue 非空則開下一條。
    /// 對應 EditMode 同名 [Ignore] test（FT-09 ConfirmDialogue concrete singleton 缺輕量 mock seam 限制）。
    /// </summary>
    public sealed class StoryDialogueQueuePlayModeTests
    {
        private GameObject _bootstrapGo;
        private GameObject _panelGo;
        private GameObject _queueGo;
        private UIBootstrapController _bootstrap;
        private PanelManager _panelManager;
        private StoryDialogueQueue _queue;
        private FakeFactionStoryService _fakeService;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _bootstrapGo = new GameObject("UIBootstrap_StoryQueue_PlayMode");
            _bootstrap = _bootstrapGo.AddComponent<UIBootstrapController>();

            _panelGo = new GameObject("PanelManager_StoryQueue_PlayMode");
            _panelManager = _panelGo.AddComponent<PanelManager>();

            _queueGo = new GameObject("StoryDialogueQueue_PlayMode");
            _queue = _queueGo.AddComponent<StoryDialogueQueue>();

            // 等一 frame 讓 OnEnable 完整觸發 SubscribeEvents。
            yield return null;

            _bootstrap.SetUIReadyForTests(true);
            _panelManager.RegisterPanelRoot(PanelID.StoryDialogue, new VisualElement());
            _panelManager.RegisterPanelRoot(PanelID.SettingsPanel, new VisualElement());

            _fakeService = new FakeFactionStoryService();
            _queue.SetServiceForTests(_fakeService);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_queueGo != null) Object.DestroyImmediate(_queueGo);
            if (_panelGo != null) Object.DestroyImmediate(_panelGo);
            if (_bootstrapGo != null) Object.DestroyImmediate(_bootstrapGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DoD_A4_EC08_ConfirmCurrentDialogue_ChainsNextStage()
        {
            // EC-08：故事面板開 stage 3 中，publish stage 4；確認 stage 3 後斷言 stage 4 自動開啟。
            Assert.IsTrue(_queue.EnqueueIfAbsent(3, "dialogue.stage3"));
            yield return null;
            Assert.AreEqual(PanelID.StoryDialogue, _panelManager.GetTopPanel());
            Assert.AreEqual(3, GetField<int>(_queue, "_currentStageID"));
            Assert.AreEqual(0, _queue.Count);

            // Stage 4 排入 tail：不打斷當前對話。
            Assert.IsTrue(_queue.EnqueueIfAbsent(4, "dialogue.stage4"));
            Assert.AreEqual(1, _queue.Count);
            Assert.AreEqual(3, GetField<int>(_queue, "_currentStageID"));

            // 確認 stage 3：ClosePanel 啟動 Closing；TryStartNext 透過 onClosed callback 在動畫結束後才執行。
            _queue.ConfirmCurrentDialogue();
            Assert.AreEqual(1, _fakeService.ConfirmCalls.Count);
            Assert.AreEqual(3, _fakeService.ConfirmCalls[0]);

            // 推進 Closing 動畫至完成 → onClosed 串接 TryStartNext → OpenPanel(StoryDialogue, stage 4)。
            AdvanceAnimations();
            yield return null;

            Assert.AreEqual(PanelID.StoryDialogue, _panelManager.GetTopPanel());
            Assert.AreEqual(4, GetField<int>(_queue, "_currentStageID"));
            Assert.AreEqual(0, _queue.Count);
        }

        private void AdvanceAnimations()
        {
            Dictionary<PanelID, PanelStateMachine> machines =
                GetField<Dictionary<PanelID, PanelStateMachine>>(_panelManager, "_machines");
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

        private sealed class FakeFactionStoryService : IFactionStoryService
        {
            public List<int> ConfirmCalls { get; } = new List<int>();

            public bool IsFactionStoryEnabled() => true;
            public int GetCurrentFactionScore(int factionID) => 0;
            public int GetMaxFactionScore() => 100;
            public int GetUnlockedStageIndex(int factionID) => 0;
            public int GetPendingDialogueCount() => 0;
            public bool IsRouteCompleted(int factionID) => false;
            public IReadOnlyList<int> GetPendingDialogueStages() => Array.Empty<int>();

            public ConfirmDialogueResult ConfirmDialogue(int stageID)
            {
                ConfirmCalls.Add(stageID);
                return ConfirmDialogueResult.OK;
            }
        }
    }
}
