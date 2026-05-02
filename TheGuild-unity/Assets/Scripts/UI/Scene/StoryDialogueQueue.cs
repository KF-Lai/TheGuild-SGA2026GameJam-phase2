using System.Collections.Generic;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.UI.Core;
using UnityEngine;

namespace TheGuild.UI.Scene
{
    [DefaultExecutionOrder(-120)]
    public sealed class StoryDialogueQueue : MonoBehaviour
    {
        private readonly Queue<int> _pendingStageDialogueQueue = new Queue<int>();
        private readonly HashSet<int> _pendingStageSet = new HashSet<int>();
        private readonly Dictionary<int, string> _dialogueKeyByStageID = new Dictionary<int, string>();
        private bool _subscribed;
        private int _currentStageID;
        private IFactionStoryService _serviceOverride;

        public static StoryDialogueQueue Instance { get; private set; }
        public int Count => _pendingStageDialogueQueue.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            _subscribed = true;
        }

        public void RestorePendingFromService()
        {
            // FSD-A §3.8 OnUIReady step 3 / EC-04 雙重保險（main 路徑）：主動 query FT-09 取目前 pending stage queue。
            IFactionStoryService service = GetService();
            if (service == null)
            {
                return;
            }

            IReadOnlyList<int> stages = service.GetPendingDialogueStages();
            if (stages == null || stages.Count == 0)
            {
                return;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                EnqueueIfAbsent(stages[i], string.Empty);
            }
        }

        public bool EnqueueIfAbsent(int stageID, string dialogueKey)
        {
            if (stageID <= 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(dialogueKey))
            {
                _dialogueKeyByStageID[stageID] = dialogueKey;
            }

            if (_pendingStageSet.Contains(stageID))
            {
                return false;
            }

            _pendingStageSet.Add(stageID);
            _pendingStageDialogueQueue.Enqueue(stageID);
            TryStartNext();
            return true;
        }

        public bool TryStartNext()
        {
            if (_pendingStageDialogueQueue.Count == 0 || PanelManager.Instance == null)
            {
                return false;
            }

            if (PanelManager.Instance.GetTopPanel() != PanelID.None || IsP03CriticalActive())
            {
                return false;
            }

            int stageID = _pendingStageDialogueQueue.Dequeue();
            _pendingStageSet.Remove(stageID);
            _currentStageID = stageID;

            _dialogueKeyByStageID.TryGetValue(stageID, out string dialogueKey);
            StoryDialogueOpenArgs args = new StoryDialogueOpenArgs(stageID, dialogueKey, string.Empty, string.Empty);
            return PanelManager.Instance.OpenPanel(PanelID.StoryDialogue, args);
        }

        public void ConfirmCurrentDialogue()
        {
            IFactionStoryService service = GetService();
            if (_currentStageID <= 0 || service == null)
            {
                return;
            }

            ConfirmDialogueResult result = service.ConfirmDialogue(_currentStageID);
            if (result != ConfirmDialogueResult.OK)
            {
                Debug.LogWarning($"[StoryDialogueQueue] ConfirmDialogue failed. stageID={_currentStageID}, result={result}");
                return;
            }

            _currentStageID = 0;
            if (PanelManager.Instance != null)
            {
                // FSD-A §5.4.6 chain continue：StoryDialogue Closing 動畫完成後再 TryStartNext，避免 GetTopPanel 仍回 StoryDialogue。
                PanelManager.Instance.ClosePanel(PanelID.StoryDialogue, TryStartNextOnClose);
            }
        }

        private void TryStartNextOnClose()
        {
            TryStartNext();
        }

        public void ManualResume()
        {
            TryStartNext();
        }

        private void HandleStageUnlocked(OnFactionStoryStageUnlockedEvent evt)
        {
            EnqueueIfAbsent(evt.StageID, evt.DialogueKey);
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed)
            {
                return;
            }

            EventBus.Unsubscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            _subscribed = false;
        }

        private bool IsP03CriticalActive()
        {
            // TODO Batch 3/P-03：若 P-03 提供 critical 查詢 API，於此接入。
            return false;
        }

        private IFactionStoryService GetService()
        {
            return _serviceOverride ?? (IFactionStoryService)FactionStoryService.Instance;
        }

        internal void SetServiceForTests(IFactionStoryService service)
        {
            _serviceOverride = service;
        }
    }
}
