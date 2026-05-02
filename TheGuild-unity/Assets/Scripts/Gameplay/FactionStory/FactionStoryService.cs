using System;
using System.Collections.Generic;
using System.Linq;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Outcome;
using TheGuild.Gameplay.Outcome.Events;
using TheGuild.Gameplay.WorldDanger;
using UnityEngine;
using OutcomeRecord = TheGuild.Gameplay.Outcome.Outcome;

namespace TheGuild.Gameplay.FactionStory
{
    [DefaultExecutionOrder(220)]
    public sealed class FactionStoryService : MonoBehaviour, IFactionStoryService, ISaveable
    {
        private bool _isEnabled;
        private bool _isBootstrapped;
        private bool _isSubscribed;
        private int _factionNeutralID;

        private FactionStoryTableLoader _tableLoader;
        private FactionStoryScoreAccumulator _scoreAccumulator;
        private readonly HashSet<int> _routeCompletedFlags = new HashSet<int>();

        private FactionStorySaveData _pendingRestore;
        private bool _hasPendingRestore;

        internal static Func<int, InjectStaticMissionResult> InjectStaticMissionForTests;
        internal static Func<int, MissionTemplate> MissionTemplateProviderForTests;
        internal static Func<string, int> FactionScoreDeltaProviderForTests;
        internal static Action<int> WorldDangerNotifierForTests;

        // === v3.1 patch P3.1-004：測試鉤子 ===
        /// <summary>測試用：注入 IReadOnlyList&lt;AdventurerInstance&gt; 代替 AdventurerRoster.GetRoster()。</summary>
        internal static Func<IReadOnlyList<AdventurerInstance>> RosterProviderForTests;
        /// <summary>測試用：攔截 DialogueTable.Contains(key) 查詢。</summary>
        internal static Func<string, bool> DialogueTableContainsForTests;

        // === v3.1 patch P3.1-004：runtime 狀態 ===
        private bool _pendingMissingNight;
        private int _totalAdventurerDeaths;
        private readonly HashSet<int> _blockedStages = new HashSet<int>();

        /// <summary>SystemConstants 快取：每次 Bootstrap 或 Restore 後讀取。</summary>
        private int _opheliaTemplateID = 901;
        private int _lightThreshold = 100;
        private int _mixedThreshold = 40;
        private int _ophelia_missing_recovery_hours = 12;

        public static FactionStoryService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<FactionRouteData>("FactionRouteTable");
            DataManager.RegisterTable<StoryStageData>("StoryStageTable");
        }

        // FT-10 stub shape (no : ISaveable yet by design)
        public string OwnerKey => FactionStoryConstants.OWNER_KEY;
        public bool IsCritical => false;

        private void Awake()
        {
            EnsureSingleton();
            EnsureRuntimeObjects();
        }

        private void Start()
        {
            if (!_isBootstrapped)
            {
                Bootstrap();
            }
        }

        private void OnEnable()
        {
            if (!_isBootstrapped)
            {
                Bootstrap();
            }

            SubscribeMissionResolved();
        }

        private void OnDisable()
        {
            UnsubscribeMissionResolved();
        }

        private void OnDestroy()
        {
            UnsubscribeMissionResolved();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool IsFactionStoryEnabled()
        {
            return _isEnabled;
        }

        public int GetCurrentFactionScore(int factionID)
        {
            return _isEnabled && _scoreAccumulator != null
                ? _scoreAccumulator.GetCurrentFactionScore(factionID)
                : 0;
        }

        public int GetMaxFactionScore()
        {
            return _isEnabled && _scoreAccumulator != null
                ? _scoreAccumulator.GetMaxFactionScore()
                : 0;
        }

        public int GetUnlockedStageIndex(int factionID)
        {
            return _isEnabled && _scoreAccumulator != null
                ? _scoreAccumulator.GetUnlockedStageIndex(factionID)
                : -1;
        }

        public int GetPendingDialogueCount()
        {
            return _isEnabled && _scoreAccumulator != null
                ? _scoreAccumulator.GetPendingDialogueCount()
                : 0;
        }

        // v3.1 patch P3.1-006（P-02 design-review）：P-02 OnUIReady step 3 主動查詢用。
        // 從 _scoreAccumulator.PendingDialogueStages 取 FIFO 快照（不可變）。
        public IReadOnlyList<int> GetPendingDialogueStages()
        {
            if (!_isEnabled || _scoreAccumulator == null)
            {
                return Array.Empty<int>();
            }

            return _scoreAccumulator.PendingDialogueStages.ToArray();
        }

        public bool IsRouteCompleted(int factionID)
        {
            return _routeCompletedFlags.Contains(factionID);
        }

        public ConfirmDialogueResult ConfirmDialogue(int stageID)
        {
            if (!_isEnabled || _scoreAccumulator == null)
            {
                return ConfirmDialogueResult.STORY_SYSTEM_DISABLED;
            }

            if (!_scoreAccumulator.TryPeekPendingStage(out int expectedStageID))
            {
                return ConfirmDialogueResult.INVALID_STAGE_ID;
            }

            if (expectedStageID != stageID)
            {
                Debug.LogWarning(
                    $"[FactionStoryService] ConfirmDialogue out-of-order. expected={expectedStageID}, got={stageID}");
                return ConfirmDialogueResult.INVALID_STAGE_ID;
            }

            StoryStageData stage = _tableLoader.GetByStageID(stageID);
            if (stage == null)
            {
                Debug.LogError($"[FactionStoryService] ConfirmDialogue stage not found: stageID={stageID}. Dropped queue head.");
                _scoreAccumulator.ForceDequeuePendingHead();
                return ConfirmDialogueResult.INVALID_STAGE_ID;
            }

            InjectStaticMissionResult injectResult = InjectStaticMission(stage.missionID);
            if (injectResult != InjectStaticMissionResult.OK)
            {
                Debug.LogError($"[FactionStoryService] InjectStaticMission failed. missionID={stage.missionID}, result={injectResult}");
                return ConfirmDialogueResult.INJECT_FAILED;
            }

            _scoreAccumulator.TryDequeuePendingIfMatches(stageID);
            EventBus.Publish(new OnFactionStoryDialogueConfirmedEvent(stage.stageID, stage.factionID, stage.missionID));

            // === v3.1 patch P3.1-004：specialEventKey 處理（Stage 4 Ophelia missing）===
            if (!string.IsNullOrEmpty(stage.specialEventKey) && stage.specialEventKey == "ophelia_missing")
            {
                _pendingMissingNight = true;
                EventBus.Publish(new OnOpheliaMissingNightEvent(stage.stageID));

                // 呼叫 C-02 SetWounded(opheliaInstanceID, customDurationHours: OPHELIA_MISSING_RECOVERY_HOURS)
                int opheliaTemplateID = (int)DataManager.Instance.GetFloat("OPHELIA_TEMPLATE_ID");
                int opheliaMissingHours = (int)DataManager.Instance.GetFloat("OPHELIA_MISSING_RECOVERY_HOURS");
                if (AdventurerRoster.Instance != null)
                {
                    var roster = AdventurerRoster.Instance.GetRoster();
                    AdventurerInstance ophelia = null;
                    for (int i = 0; i < roster.Count; i++)
                    {
                        if (roster[i].templateID == opheliaTemplateID) { ophelia = roster[i]; break; }
                    }
                    if (ophelia != null)
                    {
                        AdventurerRoster.Instance.SetWounded(ophelia.instanceID, opheliaMissingHours);
                    }
                    else
                    {
                        Debug.LogWarning("[FactionStoryService] specialEventKey=ophelia_missing 觸發，但奧菲莉雅不在名冊。");
                    }
                }
            }

            return ConfirmDialogueResult.OK;
        }

        public string Serialize()
        {
            EnsureRuntimeObjects();
            if (!_isBootstrapped && !_hasPendingRestore)
            {
                InitializeAsNewGame();
            }

            FactionStorySaveData saveData = new FactionStorySaveData();
            foreach (KeyValuePair<int, int> pair in _scoreAccumulator.FactionScores)
            {
                saveData.factionScores.Add(new FactionScoreEntry
                {
                    factionID = pair.Key,
                    score = pair.Value
                });
            }

            foreach (KeyValuePair<int, int> pair in _scoreAccumulator.UnlockedStageIndices)
            {
                saveData.unlockedStageIndices.Add(new UnlockedStageEntry
                {
                    factionID = pair.Key,
                    stageIndex = pair.Value
                });
            }

            foreach (int stageID in _scoreAccumulator.PendingDialogueStages)
            {
                saveData.pendingDialogueStages.Add(new PendingDialogueStageEntry
                {
                    stageID = stageID
                });
            }

            foreach (int factionID in _routeCompletedFlags)
            {
                saveData.routeCompletedFlags.Add(new RouteCompletedEntry
                {
                    factionID = factionID
                });
            }

            return JsonUtility.ToJson(saveData);
        }

        public void RestoreFromSave(string ownerJson)
        {
            EnsureRuntimeObjects();

            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            try
            {
                FactionStorySaveData data = JsonUtility.FromJson<FactionStorySaveData>(ownerJson);
                if (data == null)
                {
                    Debug.LogError("[FactionStoryService] RestoreFromSave parsed null saveData, fallback to new game.");
                    InitializeAsNewGame();
                    return;
                }

                _pendingRestore = data;
                _hasPendingRestore = true;

                if (_isBootstrapped && _isEnabled)
                {
                    ApplySaveData(_pendingRestore);
                    NotifyWorldDanger(GetMaxFactionScore());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FactionStoryService] RestoreFromSave failed: {ex.GetType().Name} - {ex.Message}");
                InitializeAsNewGame();
            }
        }

        public void InitializeAsNewGame()
        {
            EnsureRuntimeObjects();
            _scoreAccumulator.InitializeAsNewGame();
            _routeCompletedFlags.Clear();
            _pendingRestore = null;
            _hasPendingRestore = false;
        }

        internal static void ResetForTests()
        {
            InjectStaticMissionForTests = null;
            MissionTemplateProviderForTests = null;
            FactionScoreDeltaProviderForTests = null;
            WorldDangerNotifierForTests = null;
            // === v3.1 patch P3.1-004：清除 v31 測試鉤子 ===
            RosterProviderForTests = null;
            DialogueTableContainsForTests = null;

            if (Instance != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(Instance.gameObject);
#else
                Destroy(Instance.gameObject);
#endif
                Instance = null;
            }
        }

        private void Bootstrap()
        {
            EnsureRuntimeObjects();
            if (_isBootstrapped)
            {
                return;
            }

            if (DataManager.Instance == null)
            {
                Debug.LogError("[FactionStoryService] Bootstrap failed: DataManager.Instance is null.");
                _isEnabled = false;
                _isBootstrapped = true;
                return;
            }

            _factionNeutralID = DataManager.Instance.GetInt(FactionStoryConstants.FACTION_NEUTRAL_ID_KEY);

            // === v3.1 patch P3.1-004：讀取 SystemConstants（找不到 key 時保留預設值）===
            LoadSystemConstants();

            bool tableReady;
            try
            {
                tableReady = _tableLoader.Initialize(_factionNeutralID);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FactionStoryService] Bootstrap table load failed: {ex.GetType().Name} - {ex.Message}");
                tableReady = false;
            }

            bool difficultyReady = HasAnyNonZeroFactionScoreDelta();
            _isEnabled = tableReady && difficultyReady;

            if (!_isEnabled)
            {
                _scoreAccumulator.InitializeAsNewGame();
                _routeCompletedFlags.Clear();
                _isBootstrapped = true;
                return;
            }

            if (_hasPendingRestore && _pendingRestore != null)
            {
                ApplySaveData(_pendingRestore);
            }
            else
            {
                InitializeAsNewGame();
            }

            NotifyWorldDanger(GetMaxFactionScore());
            ReplayPendingUnlockedEvents();

            _isBootstrapped = true;
        }

        private void HandleOnMissionResolved(OnMissionResolvedEvent evt)
        {
            if (!_isEnabled)
            {
                return;
            }

            OutcomeRecord outcome = evt.Outcome;
            if (outcome == null)
            {
                Debug.LogError("[FactionStoryService] OnMissionResolved received null outcome.");
                return;
            }

            bool isKnownFaction = _tableLoader.HasFaction(outcome.missionFactionID);
            if (outcome.missionFactionID != _factionNeutralID && !isKnownFaction)
            {
                Debug.LogWarning($"[FactionStoryService] Unknown factionID={outcome.missionFactionID} in OnMissionResolved.");
            }

            if (outcome.isSuccess && isKnownFaction && outcome.missionFactionID != _factionNeutralID)
            {
                int delta = GetFactionScoreDelta(outcome.missionDifficulty);
                if (delta != 0)
                {
                    _scoreAccumulator.AccumulateScoreAndCheckUnlock(outcome.missionFactionID, delta);
                    NotifyWorldDanger(GetMaxFactionScore());
                }
            }

            HandleStoryStageResolve(outcome);
        }

        private void HandleStoryStageResolve(OutcomeRecord outcome)
        {
            MissionTemplate template = GetMissionTemplate(outcome.missionID);
            if (template == null || template.categoryID != FactionStoryConstants.FACTION_STORY_CATEGORY_ID)
            {
                return;
            }

            StoryStageData stage = _tableLoader.GetByMissionID(outcome.missionID);
            if (stage == null)
            {
                Debug.LogWarning($"[FactionStoryService] Story stage missing for missionID={outcome.missionID}.");
                return;
            }

            EventBus.Publish(new OnFactionStoryStageResolvedEvent(
                stage.stageID,
                stage.factionID,
                stage.stageIndex,
                stage.missionID,
                outcome.isSuccess,
                outcome.isDead));

            CheckRouteCompletion(stage, outcome.isSuccess);
        }

        private void CheckRouteCompletion(StoryStageData stage, bool finalIsSuccess)
        {
            if (_routeCompletedFlags.Contains(stage.factionID))
            {
                return;
            }

            StoryStageData finalStage = _tableLoader.GetFinalStage(stage.factionID);
            if (finalStage == null || stage.stageIndex < finalStage.stageIndex)
            {
                return;
            }

            _routeCompletedFlags.Add(stage.factionID);

            EventBus.Publish(new OnFactionRouteCompletedEvent(
                stage.factionID,
                finalStage.stageID,
                finalIsSuccess,
                _tableLoader.GetStageCount(stage.factionID)));
        }

        private void SubscribeMissionResolved()
        {
            if (!_isEnabled || _isSubscribed)
            {
                return;
            }

            EventBus.Subscribe<OnMissionResolvedEvent>(HandleOnMissionResolved);

            // === v3.1 patch P3.1-004：訂閱 C-02 OnAdventurerRecoveredEvent（奧菲莉雅回來偵測）===
            EventBus.Subscribe<OnAdventurerRecoveredEvent>(HandleOnAdventurerRecovered);

            _isSubscribed = true;
        }

        private void UnsubscribeMissionResolved()
        {
            if (!_isSubscribed)
            {
                return;
            }

            EventBus.Unsubscribe<OnMissionResolvedEvent>(HandleOnMissionResolved);

            // === v3.1 patch P3.1-004：取消訂閱 C-02 OnAdventurerRecovered ===
            EventBus.Unsubscribe<OnAdventurerRecoveredEvent>(HandleOnAdventurerRecovered);

            _isSubscribed = false;
        }

        // === v3.1 patch P3.1-004：奧菲莉雅回來事件 handler ===
        /// <summary>
        /// C-02 OnAdventurerRecovered 觸發時識別是否為奧菲莉雅；若是則：
        /// (1) 清除 _pendingMissingNight flag
        /// (2) 發布 OnOpheliaReturnedEvent
        /// (3) 呼叫 TriggerDeferredStageCheck（Stage 5 unlockBlockerCondition 可能解除）
        /// </summary>
        private void HandleOnAdventurerRecovered(OnAdventurerRecoveredEvent evt)
        {
            if (!_isEnabled) return;

            int opheliaTemplateID = (int)DataManager.Instance.GetFloat("OPHELIA_TEMPLATE_ID");
            if (AdventurerRoster.Instance == null) return;

            var ophelia = AdventurerRoster.Instance.GetAdventurer(evt.InstanceID);
            if (ophelia == null || ophelia.templateID != opheliaTemplateID) return;

            // 識別為奧菲莉雅
            _pendingMissingNight = false;
            EventBus.Publish(new OnOpheliaReturnedEvent(evt.InstanceID));

            // 重新檢查 _blockedStages（Stage 5 npc:ophelia:status==Idle 解除）
            TriggerDeferredStageCheck();
        }

        private bool HasAnyNonZeroFactionScoreDelta()
        {
            IReadOnlyList<MissionDifficultyData> rows = DataManager.Instance.GetAll<MissionDifficultyData>();
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                MissionDifficultyData row = rows[i];
                if (row != null && row.factionScoreDelta != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetFactionScoreDelta(string difficulty)
        {
            if (FactionScoreDeltaProviderForTests != null)
            {
                return FactionScoreDeltaProviderForTests.Invoke(difficulty);
            }

            return MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetFactionScoreDelta(difficulty)
                : 0;
        }

        private MissionTemplate GetMissionTemplate(int missionID)
        {
            if (MissionTemplateProviderForTests != null)
            {
                return MissionTemplateProviderForTests.Invoke(missionID);
            }

            return MissionDatabaseService.Instance != null
                ? MissionDatabaseService.Instance.GetTemplate(missionID)
                : null;
        }

        private InjectStaticMissionResult InjectStaticMission(int missionID)
        {
            if (InjectStaticMissionForTests != null)
            {
                return InjectStaticMissionForTests.Invoke(missionID);
            }

            return CommissionBoardService.Instance != null
                ? CommissionBoardService.Instance.InjectStaticMission(missionID)
                : InjectStaticMissionResult.BOARD_DISABLED;
        }

        private void NotifyWorldDanger(int newMaxScore)
        {
            if (WorldDangerNotifierForTests != null)
            {
                WorldDangerNotifierForTests.Invoke(newMaxScore);
                return;
            }

            if (WorldDangerService.Instance != null)
            {
                WorldDangerService.Instance.OnFactionScoreUpdated(newMaxScore);
            }
        }

        private void ApplySaveData(FactionStorySaveData data)
        {
            Dictionary<int, int> factionScores = new Dictionary<int, int>(8);
            Dictionary<int, int> unlocked = new Dictionary<int, int>(8);
            List<int> pending = new List<int>(16);
            _routeCompletedFlags.Clear();

            if (data.factionScores != null)
            {
                for (int i = 0; i < data.factionScores.Count; i++)
                {
                    FactionScoreEntry entry = data.factionScores[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    factionScores[entry.factionID] = entry.score;
                }
            }

            if (data.unlockedStageIndices != null)
            {
                for (int i = 0; i < data.unlockedStageIndices.Count; i++)
                {
                    UnlockedStageEntry entry = data.unlockedStageIndices[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    unlocked[entry.factionID] = entry.stageIndex;
                }
            }

            if (data.pendingDialogueStages != null)
            {
                for (int i = 0; i < data.pendingDialogueStages.Count; i++)
                {
                    PendingDialogueStageEntry entry = data.pendingDialogueStages[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    pending.Add(entry.stageID);
                }
            }

            if (data.routeCompletedFlags != null)
            {
                for (int i = 0; i < data.routeCompletedFlags.Count; i++)
                {
                    RouteCompletedEntry entry = data.routeCompletedFlags[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    if (_tableLoader.HasFaction(entry.factionID))
                    {
                        _routeCompletedFlags.Add(entry.factionID);
                    }
                    else
                    {
                        Debug.LogWarning($"[FactionStoryService] Discard stale routeCompleted flag factionID={entry.factionID}.");
                    }
                }
            }

            _scoreAccumulator.RestoreRuntimeState(factionScores, unlocked, pending);
            _pendingRestore = null;
            _hasPendingRestore = false;
        }

        private void ReplayPendingUnlockedEvents()
        {
            Queue<int> replay = new Queue<int>(_scoreAccumulator.PendingDialogueStages);
            _scoreAccumulator.PendingDialogueStages.Clear();

            while (replay.Count > 0)
            {
                int stageID = replay.Dequeue();
                StoryStageData stage = _tableLoader.GetByStageID(stageID);
                if (stage == null)
                {
                    Debug.LogWarning($"[FactionStoryService] Bootstrap replay dropped stale stageID={stageID}.");
                    continue;
                }

                _scoreAccumulator.PendingDialogueStages.Enqueue(stageID);
                EventBus.Publish(new OnFactionStoryStageUnlockedEvent(
                    stage.stageID,
                    stage.factionID,
                    stage.stageIndex,
                    stage.missionID,
                    stage.dialogueKey));
            }
        }

        private void EnsureRuntimeObjects()
        {
            if (_tableLoader == null)
            {
                _tableLoader = new FactionStoryTableLoader();
            }

            if (_scoreAccumulator == null)
            {
                _scoreAccumulator = new FactionStoryScoreAccumulator(_tableLoader);
            }
        }

        private void EnsureSingleton()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
#if UNITY_EDITOR
                    DestroyImmediate(gameObject);
#endif
                }

                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        // v3.1 patch P3.1-004：讀 SystemConstants 5 個 v3.1 常數至 service 快取。
        // 依 FT-09 GDD §3.4.9 / §3.6.9 規格，於 Bootstrap 期間呼叫；找不到 key 時保留 default 值（silent，不 LogError）。
        private void LoadSystemConstants()
        {
            if (DataManager.Instance == null)
            {
                return;
            }

            if (DataManager.Instance.TryGetInt("LIGHT_THRESHOLD", out int light) && light > 0)
            {
                _lightThreshold = light;
            }

            if (DataManager.Instance.TryGetInt("MIXED_THRESHOLD", out int mixed) && mixed > 0)
            {
                _mixedThreshold = mixed;
            }

            if (DataManager.Instance.TryGetInt("OPHELIA_MISSING_RECOVERY_HOURS", out int recovery) && recovery > 0)
            {
                _ophelia_missing_recovery_hours = recovery;
            }

            if (DataManager.Instance.TryGetInt("OPHELIA_TEMPLATE_ID", out int templateID) && templateID > 0)
            {
                _opheliaTemplateID = templateID;
            }
        }

        // v3.1 patch P3.1-004：OnAdventurerRecovered（奧菲莉雅 Wounded → Idle）後重檢 _blockedStages，
        // 依 GDD §3.4.8 / §3.6.9 規格：blocker 解除者補觸發解鎖（mutate _scoreAccumulator + 重發 OnFactionStoryStageUnlockedEvent）。
        private void TriggerDeferredStageCheck()
        {
            if (!_isEnabled || _scoreAccumulator == null || _blockedStages.Count == 0)
            {
                return;
            }

            int[] snapshot = new int[_blockedStages.Count];
            _blockedStages.CopyTo(snapshot);

            for (int i = 0; i < snapshot.Length; i++)
            {
                int stageID = snapshot[i];
                StoryStageData stage = _tableLoader.GetByStageID(stageID);
                if (stage == null)
                {
                    _blockedStages.Remove(stageID);
                    continue;
                }

                if (EvaluateBlocker(stage.unlockBlockerCondition))
                {
                    continue; // 仍 blocked
                }

                _blockedStages.Remove(stageID);
                _scoreAccumulator.UnblockStage(stage.factionID, stage.stageIndex, stage.stageID);
                EventBus.Publish(new OnFactionStoryStageUnlockedEvent(
                    stage.stageID,
                    stage.factionID,
                    stage.stageIndex,
                    stage.missionID,
                    stage.dialogueKey));
            }
        }

        // v3.1 patch P3.1-004：解析 unlockBlockerCondition 語法。
        // Jam 版僅支援 "npc:ophelia:status==Idle"；不識別語法 → LogWarning + 視為無 blocker。
        // GDD §3.4.8 EvaluateBlocker 表格：查 C-02 roster 以 templateID==OPHELIA_TEMPLATE_ID 找實例。
        private bool EvaluateBlocker(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return false;
            }

            if (condition == "npc:ophelia:status==Idle")
            {
                IReadOnlyList<AdventurerInstance> roster = RosterProviderForTests != null
                    ? RosterProviderForTests.Invoke()
                    : (AdventurerRoster.Instance != null ? AdventurerRoster.Instance.GetRoster() : null);

                if (roster == null)
                {
                    return true; // 無 roster 視為 blocked
                }

                for (int i = 0; i < roster.Count; i++)
                {
                    if (roster[i] != null && roster[i].templateID == _opheliaTemplateID)
                    {
                        return roster[i].status != AdventurerStatus.Idle;
                    }
                }

                return true; // 找不到奧菲莉雅 → blocked
            }

            Debug.LogWarning($"[FactionStoryService] Unknown blocker syntax: {condition}");
            return false;
        }
    }
}


