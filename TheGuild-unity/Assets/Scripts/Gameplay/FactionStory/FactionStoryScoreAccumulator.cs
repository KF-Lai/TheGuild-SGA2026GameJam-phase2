using System.Collections.Generic;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory.Events;
using UnityEngine;

namespace TheGuild.Gameplay.FactionStory
{
    internal sealed class FactionStoryScoreAccumulator
    {
        private readonly FactionStoryTableLoader _tableLoader;

        // Required by FT-09 spec.
        private readonly Dictionary<int, int> _factionScores = new Dictionary<int, int>(8);
        private readonly Dictionary<int, int> _unlockedStageIndices = new Dictionary<int, int>(8);
        private readonly Queue<int> _pendingDialogueStages = new Queue<int>(16);

        public IReadOnlyDictionary<int, int> FactionScores => _factionScores;
        public IReadOnlyDictionary<int, int> UnlockedStageIndices => _unlockedStageIndices;
        public Queue<int> PendingDialogueStages => _pendingDialogueStages;

        public FactionStoryScoreAccumulator(FactionStoryTableLoader tableLoader)
        {
            _tableLoader = tableLoader;
        }

        public void InitializeAsNewGame()
        {
            _factionScores.Clear();
            _unlockedStageIndices.Clear();
            _pendingDialogueStages.Clear();
        }

        public void RestoreRuntimeState(
            IReadOnlyDictionary<int, int> factionScores,
            IReadOnlyDictionary<int, int> unlockedStageIndices,
            IReadOnlyList<int> pendingStageIDs)
        {
            InitializeAsNewGame();

            if (factionScores != null)
            {
                foreach (KeyValuePair<int, int> pair in factionScores)
                {
                    if (!_tableLoader.HasFaction(pair.Key))
                    {
                        Debug.LogWarning($"[FactionStoryScoreAccumulator] Discard stale faction score: factionID={pair.Key}");
                        continue;
                    }

                    _factionScores[pair.Key] = pair.Value < 0 ? 0 : pair.Value;
                }
            }

            if (unlockedStageIndices != null)
            {
                foreach (KeyValuePair<int, int> pair in unlockedStageIndices)
                {
                    if (!_tableLoader.HasFaction(pair.Key))
                    {
                        Debug.LogWarning($"[FactionStoryScoreAccumulator] Discard stale unlock state: factionID={pair.Key}");
                        continue;
                    }

                    int clamped = pair.Value;
                    int maxStage = _tableLoader.GetMaxStageIndex(pair.Key);
                    if (clamped > maxStage)
                    {
                        Debug.LogWarning($"[FactionStoryScoreAccumulator] Clamp unlock index: factionID={pair.Key}, from={clamped}, to={maxStage}");
                        clamped = maxStage;
                    }

                    if (clamped < 0)
                    {
                        clamped = 0;
                    }

                    _unlockedStageIndices[pair.Key] = clamped;
                }
            }

            if (pendingStageIDs != null)
            {
                for (int i = 0; i < pendingStageIDs.Count; i++)
                {
                    int stageID = pendingStageIDs[i];
                    if (_tableLoader.GetByStageID(stageID) == null)
                    {
                        Debug.LogWarning($"[FactionStoryScoreAccumulator] Discard stale pending stageID={stageID}");
                        continue;
                    }

                    _pendingDialogueStages.Enqueue(stageID);
                }
            }
        }

        public int GetCurrentFactionScore(int factionID)
        {
            return _factionScores.TryGetValue(factionID, out int score) ? score : 0;
        }

        public int GetUnlockedStageIndex(int factionID)
        {
            return _unlockedStageIndices.TryGetValue(factionID, out int stageIndex) ? stageIndex : 0;
        }

        public int GetPendingDialogueCount()
        {
            return _pendingDialogueStages.Count;
        }

        public bool TryPeekPendingStage(out int stageID)
        {
            if (_pendingDialogueStages.Count == 0)
            {
                stageID = 0;
                return false;
            }

            stageID = _pendingDialogueStages.Peek();
            return true;
        }

        public bool TryDequeuePendingIfMatches(int expectedStageID)
        {
            if (_pendingDialogueStages.Count == 0)
            {
                return false;
            }

            if (_pendingDialogueStages.Peek() != expectedStageID)
            {
                return false;
            }

            _pendingDialogueStages.Dequeue();
            return true;
        }

        public void ForceDequeuePendingHead()
        {
            if (_pendingDialogueStages.Count > 0)
            {
                _pendingDialogueStages.Dequeue();
            }
        }

        // v3.1 patch P3.1-004：unlockBlockerCondition 解除後由 FactionStoryService.TriggerDeferredStageCheck 呼叫，
        // 補觸發已 block 但 blocker 已解除的 stage 的解鎖流程（mutate 內部 dict + queue）。
        internal void UnblockStage(int factionID, int stageIndex, int stageID)
        {
            if (!_tableLoader.HasFaction(factionID))
            {
                return;
            }

            int currentMax = _unlockedStageIndices.TryGetValue(factionID, out int existing) ? existing : 0;
            if (stageIndex > currentMax)
            {
                _unlockedStageIndices[factionID] = stageIndex;
            }

            if (!_pendingDialogueStages.Contains(stageID))
            {
                _pendingDialogueStages.Enqueue(stageID);
            }
        }

        public int GetMaxFactionScore()
        {
            int max = 0;
            foreach (KeyValuePair<int, int> pair in _factionScores)
            {
                if (pair.Value > max)
                {
                    max = pair.Value;
                }
            }

            return max;
        }

        public void AccumulateScoreAndCheckUnlock(int factionID, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            int oldScore = GetCurrentFactionScore(factionID);
            int newScore = oldScore + delta;
            if (newScore < 0)
            {
                newScore = 0;
            }

            _factionScores[factionID] = newScore;

            EventBus.Publish(new OnFactionScoreChangedEvent(
                factionID,
                oldScore,
                newScore,
                delta));

            CheckStageUnlock(factionID, newScore);
        }

        private void CheckStageUnlock(int factionID, int newScore)
        {
            if (!_tableLoader.HasFaction(factionID))
            {
                return;
            }

            IReadOnlyList<StoryStageData> stages = _tableLoader.GetByFactionID(factionID);
            if (stages.Count == 0)
            {
                return;
            }

            int currentMaxIndex = GetUnlockedStageIndex(factionID);

            for (int i = 0; i < stages.Count; i++)
            {
                StoryStageData stage = stages[i];
                if (stage.stageIndex <= currentMaxIndex)
                {
                    continue;
                }

                if (newScore < stage.scoreThreshold)
                {
                    // Stages are validated as threshold-increasing per faction.
                    break;
                }

                _unlockedStageIndices[factionID] = stage.stageIndex;
                currentMaxIndex = stage.stageIndex;
                _pendingDialogueStages.Enqueue(stage.stageID);

                EventBus.Publish(new OnFactionStoryStageUnlockedEvent(
                    stage.stageID,
                    stage.factionID,
                    stage.stageIndex,
                    stage.missionID,
                    stage.dialogueKey));
            }
        }
    }
}
