using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Outcome;
using TheGuild.Gameplay.Outcome.Events;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.FactionStory
{
    public sealed class FactionStoryServiceTests
    {
        private TestContext _ctx;

        private readonly List<OnFactionScoreChangedEvent> _scoreChangedEvents = new List<OnFactionScoreChangedEvent>(16);
        private readonly List<OnFactionStoryStageUnlockedEvent> _stageUnlockedEvents = new List<OnFactionStoryStageUnlockedEvent>(16);
        private readonly List<OnFactionStoryDialogueConfirmedEvent> _dialogueConfirmedEvents = new List<OnFactionStoryDialogueConfirmedEvent>(16);
        private readonly List<OnFactionStoryStageResolvedEvent> _stageResolvedEvents = new List<OnFactionStoryStageResolvedEvent>(16);
        private readonly List<OnFactionRouteCompletedEvent> _routeCompletedEvents = new List<OnFactionRouteCompletedEvent>(8);
        private readonly List<int> _worldDangerMaxScores = new List<int>(8);
        private readonly List<string> _eventOrder = new List<string>(32);

        private readonly Dictionary<string, int> _deltaByDifficulty = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["F"] = 1,
            ["B"] = 5,
            ["SS"] = 20,
            ["X"] = 35
        };

        private readonly Dictionary<int, MissionTemplate> _templateByMissionID = new Dictionary<int, MissionTemplate>
        {
            [1] = new MissionTemplate { missionID = 1, difficulty = "F", typeID = 1, factionID = 1, categoryID = 0 },
            [9001] = new MissionTemplate { missionID = 9001, difficulty = "F", typeID = 1, factionID = 1, categoryID = 3 },
            [9002] = new MissionTemplate { missionID = 9002, difficulty = "B", typeID = 1, factionID = 1, categoryID = 3 },
            [9003] = new MissionTemplate { missionID = 9003, difficulty = "SS", typeID = 1, factionID = 1, categoryID = 3 }
        };

        private InjectStaticMissionResult _injectResult = InjectStaticMissionResult.OK;

        [SetUp]
        public void SetUp()
        {
            ResetStatics();

            _ctx = CreateContext(
                neutralOnlyRoute: false,
                allDifficultyDeltaZero: false,
                includeStoryStages: true);

            EventBus.Subscribe<OnFactionScoreChangedEvent>(HandleScoreChanged);
            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            EventBus.Subscribe<OnFactionStoryDialogueConfirmedEvent>(HandleDialogueConfirmed);
            EventBus.Subscribe<OnFactionStoryStageResolvedEvent>(HandleStageResolved);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<OnFactionScoreChangedEvent>(HandleScoreChanged);
            EventBus.Unsubscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            EventBus.Unsubscribe<OnFactionStoryDialogueConfirmedEvent>(HandleDialogueConfirmed);
            EventBus.Unsubscribe<OnFactionStoryStageResolvedEvent>(HandleStageResolved);
            EventBus.Unsubscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);

            _scoreChangedEvents.Clear();
            _stageUnlockedEvents.Clear();
            _dialogueConfirmedEvents.Clear();
            _stageResolvedEvents.Clear();
            _routeCompletedEvents.Clear();
            _worldDangerMaxScores.Clear();
            _eventOrder.Clear();

            DestroyContext(_ctx);
            _ctx = null;

            ResetStatics();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoD01_BootstrapEnabled_WhenRouteAndStageAndDifficultyAreValid()
        {
            Assert.IsTrue(_ctx.Service.IsFactionStoryEnabled());
            Assert.AreEqual(1, _worldDangerMaxScores.Count);
            Assert.AreEqual(0, _worldDangerMaxScores[0]);
        }

        [Test]
        public void EC01_BootstrapDisabled_WhenAllRoutesAreNeutral()
        {
            DestroyContext(_ctx);

            // 強化：徹底清掉 EventBus 殘留訂閱與 FT-09 / DataManager 靜態狀態
            ResetStatics();

            // SetUp 的訂閱已被 ResetStatics 清除，需在測試內重新掛回
            EventBus.Subscribe<OnFactionScoreChangedEvent>(HandleScoreChanged);
            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            EventBus.Subscribe<OnFactionStoryDialogueConfirmedEvent>(HandleDialogueConfirmed);
            EventBus.Subscribe<OnFactionStoryStageResolvedEvent>(HandleStageResolved);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);

            _worldDangerMaxScores.Clear();
            _scoreChangedEvents.Clear();
            _stageUnlockedEvents.Clear();
            _dialogueConfirmedEvents.Clear();
            _stageResolvedEvents.Clear();
            _routeCompletedEvents.Clear();
            _eventOrder.Clear();

            _ctx = CreateContext(
                neutralOnlyRoute: true,
                allDifficultyDeltaZero: false,
                includeStoryStages: true);

            Assert.IsFalse(_ctx.Service.IsFactionStoryEnabled());

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(0, _scoreChangedEvents.Count, "_scoreChangedEvents 應為 0（degraded 不訂閱 OnMissionResolved）");
            Assert.AreEqual(0, _stageUnlockedEvents.Count, "_stageUnlockedEvents 應為 0（degraded 不發 unlock 事件）");
            Assert.AreEqual(0, _worldDangerMaxScores.Count, "_worldDangerMaxScores 應為 0（degraded 不推 C-06）");
        }

        [Test]
        public void EC01_BootstrapDisabled_WhenAllDifficultyDeltasAreZero()
        {
            DestroyContext(_ctx);

            // 強化：徹底清掉 EventBus 殘留訂閱與 FT-09 / DataManager 靜態狀態
            ResetStatics();

            // SetUp 的訂閱已被 ResetStatics 清除，需在測試內重新掛回
            EventBus.Subscribe<OnFactionScoreChangedEvent>(HandleScoreChanged);
            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            EventBus.Subscribe<OnFactionStoryDialogueConfirmedEvent>(HandleDialogueConfirmed);
            EventBus.Subscribe<OnFactionStoryStageResolvedEvent>(HandleStageResolved);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);

            _worldDangerMaxScores.Clear();
            _scoreChangedEvents.Clear();
            _stageUnlockedEvents.Clear();
            _dialogueConfirmedEvents.Clear();
            _stageResolvedEvents.Clear();
            _routeCompletedEvents.Clear();
            _eventOrder.Clear();

            _ctx = CreateContext(
                neutralOnlyRoute: false,
                allDifficultyDeltaZero: true,
                includeStoryStages: true);

            Assert.IsFalse(_ctx.Service.IsFactionStoryEnabled());
            Assert.AreEqual(0, _worldDangerMaxScores.Count);
        }

        [Test]
        public void DoD02_AccumulateScore_IgnoreNeutralAndUnknownFaction()
        {
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(5, _ctx.Service.GetCurrentFactionScore(1));
            Assert.AreEqual(1, _scoreChangedEvents.Count);

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 0,
                isSuccess = true
            }));
            Assert.AreEqual(5, _ctx.Service.GetCurrentFactionScore(1));
            Assert.AreEqual(1, _scoreChangedEvents.Count);

            LogAssert.Expect(LogType.Warning, "[FactionStoryService] Unknown factionID=99 in OnMissionResolved.");
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 99,
                isSuccess = true
            }));
            Assert.AreEqual(5, _ctx.Service.GetCurrentFactionScore(1));
            Assert.AreEqual(1, _scoreChangedEvents.Count);
        }

        [Test]
        public void DoD03_EC04_MultiStageUnlockInSingleFrame_UsesFIFO()
        {
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "X",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(2, _stageUnlockedEvents.Count);
            Assert.AreEqual(1001, _stageUnlockedEvents[0].StageID);
            Assert.AreEqual(1002, _stageUnlockedEvents[1].StageID);
            Assert.AreEqual(2, _ctx.Service.GetPendingDialogueCount());
            Assert.AreEqual(2, _ctx.Service.GetUnlockedStageIndex(1));
        }

        [Test]
        public void DoD04_ConfirmDialogue_Success_EmitsEventAndDequeues()
        {
            UnlockStage1();

            ConfirmDialogueResult result = _ctx.Service.ConfirmDialogue(1001);

            Assert.AreEqual(ConfirmDialogueResult.OK, result);
            Assert.AreEqual(0, _ctx.Service.GetPendingDialogueCount());
            Assert.AreEqual(1, _dialogueConfirmedEvents.Count);
            Assert.AreEqual(1001, _dialogueConfirmedEvents[0].StageID);
        }

        [Test]
        public void EC07_ConfirmDialogue_InjectFailed_KeepsQueueHead()
        {
            UnlockStage1();
            _injectResult = InjectStaticMissionResult.BOARD_DISABLED;

            LogAssert.Expect(LogType.Error, "[FactionStoryService] InjectStaticMission failed. missionID=9001, result=BOARD_DISABLED");
            ConfirmDialogueResult result = _ctx.Service.ConfirmDialogue(1001);

            Assert.AreEqual(ConfirmDialogueResult.INJECT_FAILED, result);
            Assert.AreEqual(1, _ctx.Service.GetPendingDialogueCount());
            Assert.AreEqual(0, _dialogueConfirmedEvents.Count);
        }

        [Test]
        public void EC08_ConfirmDialogue_QueueHeadMismatch_ReturnsInvalid()
        {
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "X",
                missionFactionID = 1,
                isSuccess = true
            }));

            LogAssert.Expect(LogType.Warning, "[FactionStoryService] ConfirmDialogue out-of-order. expected=1001, got=1002");
            ConfirmDialogueResult result = _ctx.Service.ConfirmDialogue(1002);

            Assert.AreEqual(ConfirmDialogueResult.INVALID_STAGE_ID, result);
            Assert.AreEqual(2, _ctx.Service.GetPendingDialogueCount());
            Assert.AreEqual(0, _dialogueConfirmedEvents.Count);
        }

        [Test]
        public void DoD05_EC05_StoryMissionFailStillResolvesStageWithoutRollingBackUnlock()
        {
            UnlockStage1();
            int unlockedBefore = _ctx.Service.GetUnlockedStageIndex(1);

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 9001,
                missionDifficulty = "F",
                missionFactionID = 1,
                isSuccess = false,
                isDead = true
            }));

            Assert.AreEqual(unlockedBefore, _ctx.Service.GetUnlockedStageIndex(1));
            Assert.AreEqual(1, _stageResolvedEvents.Count);
            Assert.IsFalse(_stageResolvedEvents[0].IsSuccess);
            Assert.IsTrue(_stageResolvedEvents[0].IsDead);
        }

        [Test]
        public void DoD06_EC11_RouteCompletedEmitsOnce_ButScoreStillPushesWorldDanger()
        {
            string saveJson = JsonUtility.ToJson(new FactionStorySaveData
            {
                factionScores = new List<FactionScoreEntry>
                {
                    new FactionScoreEntry { factionID = 1, score = 55 }
                },
                unlockedStageIndices = new List<UnlockedStageEntry>
                {
                    new UnlockedStageEntry { factionID = 1, stageIndex = 2 }
                },
                pendingDialogueStages = new List<PendingDialogueStageEntry>(),
                routeCompletedFlags = new List<RouteCompletedEntry>()
            });
            _ctx.Service.RestoreFromSave(saveJson);

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 9003,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true,
                isDead = false
            }));

            Assert.AreEqual(1, _routeCompletedEvents.Count);
            Assert.AreEqual(1, _routeCompletedEvents[0].FactionID);
            Assert.AreEqual(1003, _routeCompletedEvents[0].FinalStageID);
            Assert.AreEqual(3, _routeCompletedEvents[0].TotalStages);

            int worldDangerCallsAfterFirst = _worldDangerMaxScores.Count;

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 9003,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(1, _routeCompletedEvents.Count);
            Assert.Greater(_worldDangerMaxScores.Count, worldDangerCallsAfterFirst);
        }

        [Test]
        public void DoD07_BootstrapAndScoring_PushesWorldDanger()
        {
            Assert.AreEqual(1, _worldDangerMaxScores.Count);
            Assert.AreEqual(0, _worldDangerMaxScores[0]);

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(2, _worldDangerMaxScores.Count);
            Assert.AreEqual(5, _worldDangerMaxScores[1]);
        }

        [Test]
        public void DoD08_EC03_BootstrapStepF_ReplaysPendingUnlockedEvents_FIFO()
        {
            DestroyContext(_ctx);

            // 強化：徹底清掉 EventBus 殘留訂閱與 FT-09 / DataManager 靜態狀態
            ResetStatics();

            // SetUp 的訂閱已被 ResetStatics 清除，需在測試內重新掛回
            EventBus.Subscribe<OnFactionScoreChangedEvent>(HandleScoreChanged);
            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            EventBus.Subscribe<OnFactionStoryDialogueConfirmedEvent>(HandleDialogueConfirmed);
            EventBus.Subscribe<OnFactionStoryStageResolvedEvent>(HandleStageResolved);
            EventBus.Subscribe<OnFactionRouteCompletedEvent>(HandleRouteCompleted);

            _worldDangerMaxScores.Clear();
            _scoreChangedEvents.Clear();
            _stageUnlockedEvents.Clear();
            _dialogueConfirmedEvents.Clear();
            _stageResolvedEvents.Clear();
            _routeCompletedEvents.Clear();
            _eventOrder.Clear();

            string restoreJson = JsonUtility.ToJson(new FactionStorySaveData
            {
                factionScores = new List<FactionScoreEntry>(),
                unlockedStageIndices = new List<UnlockedStageEntry>(),
                pendingDialogueStages = new List<PendingDialogueStageEntry>
                {
                    new PendingDialogueStageEntry { stageID = 1001 },
                    new PendingDialogueStageEntry { stageID = 1002 }
                },
                routeCompletedFlags = new List<RouteCompletedEntry>()
            });

            _ctx = CreateContext(
                neutralOnlyRoute: false,
                allDifficultyDeltaZero: false,
                includeStoryStages: true,
                restoreJsonBeforeEnable: restoreJson);

            Assert.AreEqual(2, _stageUnlockedEvents.Count);
            Assert.AreEqual(1001, _stageUnlockedEvents[0].StageID);
            Assert.AreEqual(1002, _stageUnlockedEvents[1].StageID);
            Assert.AreEqual(2, _ctx.Service.GetPendingDialogueCount());
        }

        [Test]
        public void DoD09_EC09_EventOrder_IsScoreThenUnlockThenResolveThenRouteComplete()
        {
            string saveJson = JsonUtility.ToJson(new FactionStorySaveData
            {
                factionScores = new List<FactionScoreEntry>
                {
                    new FactionScoreEntry { factionID = 1, score = 55 }
                },
                unlockedStageIndices = new List<UnlockedStageEntry>
                {
                    new UnlockedStageEntry { factionID = 1, stageIndex = 2 }
                },
                pendingDialogueStages = new List<PendingDialogueStageEntry>(),
                routeCompletedFlags = new List<RouteCompletedEntry>()
            });
            _ctx.Service.RestoreFromSave(saveJson);

            _eventOrder.Clear();
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 9003,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            CollectionAssert.AreEqual(
                new[] { "ScoreChanged", "StageUnlocked", "StageResolved", "RouteCompleted" },
                _eventOrder);
        }

        [Test]
        public void DoD10_DoD11_OnDisable_UnsubscribesMissionResolved()
        {
            ReflectionTools.InvokeInstance(_ctx.Service, "OnDisable");
            int beforeScoreEvents = _scoreChangedEvents.Count;

            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "B",
                missionFactionID = 1,
                isSuccess = true
            }));

            Assert.AreEqual(beforeScoreEvents, _scoreChangedEvents.Count);
            Assert.AreEqual(0, _ctx.Service.GetCurrentFactionScore(1));
        }

        [Test]
        public void EC10_RestoreFromSave_InvalidJson_FallbackToNewGameAndNoCrash()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[FactionStoryService\] RestoreFromSave failed: ArgumentException - .*"));
            _ctx.Service.RestoreFromSave("{not valid json");

            Assert.AreEqual(0, _ctx.Service.GetCurrentFactionScore(1));
            Assert.AreEqual(0, _ctx.Service.GetUnlockedStageIndex(1));
            Assert.AreEqual(0, _ctx.Service.GetPendingDialogueCount());
        }

        [Test]
        public void EC10_RestoreFromSave_UnknownFactionAndTooHighIndex_AreSanitized()
        {
            string saveJson = JsonUtility.ToJson(new FactionStorySaveData
            {
                factionScores = new List<FactionScoreEntry>
                {
                    new FactionScoreEntry { factionID = 1, score = 99 },
                    new FactionScoreEntry { factionID = 99, score = 10 }
                },
                unlockedStageIndices = new List<UnlockedStageEntry>
                {
                    new UnlockedStageEntry { factionID = 1, stageIndex = 99 },
                    new UnlockedStageEntry { factionID = 99, stageIndex = 3 }
                },
                pendingDialogueStages = new List<PendingDialogueStageEntry>
                {
                    new PendingDialogueStageEntry { stageID = 9999 },
                    new PendingDialogueStageEntry { stageID = 1001 }
                },
                routeCompletedFlags = new List<RouteCompletedEntry>
                {
                    new RouteCompletedEntry { factionID = 99 },
                    new RouteCompletedEntry { factionID = 1 }
                }
            });

            LogAssert.ignoreFailingMessages = true;
            _ctx.Service.RestoreFromSave(saveJson);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(99, _ctx.Service.GetCurrentFactionScore(1));
            Assert.AreEqual(3, _ctx.Service.GetUnlockedStageIndex(1));
            Assert.AreEqual(1, _ctx.Service.GetPendingDialogueCount());
            Assert.IsTrue(_ctx.Service.IsRouteCompleted(1));
        }

        private void UnlockStage1()
        {
            EventBus.Publish(new OnMissionResolvedEvent(new Outcome
            {
                missionID = 1,
                missionDifficulty = "SS",
                missionFactionID = 1,
                isSuccess = true
            }));
            Assert.AreEqual(1, _ctx.Service.GetPendingDialogueCount());
        }

        private TestContext CreateContext(
            bool neutralOnlyRoute,
            bool allDifficultyDeltaZero,
            bool includeStoryStages,
            string restoreJsonBeforeEnable = null)
        {
            ReflectionTools.InvokeStatic(typeof(DataManager), "ResetForTests");
            FactionStoryService.ResetForTests();

            Dictionary<string, string> tableMap = BuildTableMap(
                neutralOnlyRoute,
                allDifficultyDeltaZero,
                includeStoryStages);

            ReflectionTools.InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(name => tableMap.TryGetValue(name, out string csv) ? csv : null));

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterTable<FactionRouteData>("FactionRouteTable");
            DataManager.RegisterTable<StoryStageData>("StoryStageTable");
            DataManager.RegisterTable<MissionDifficultyData>("MissionDifficultyTable");
            DataManager.RegisterTable<MissionTemplate>("MissionTemplate");

            TestContext ctx = new TestContext();
            ctx.Data = new GameObject("DataManager_FT09_Test").AddComponent<DataManager>();
            ReflectionTools.InvokeInstance(ctx.Data, "InitializeForTests");

            FactionStoryService.FactionScoreDeltaProviderForTests = difficulty =>
            {
                return _deltaByDifficulty.TryGetValue(difficulty ?? string.Empty, out int delta) ? delta : 0;
            };

            FactionStoryService.MissionTemplateProviderForTests = missionID =>
            {
                return _templateByMissionID.TryGetValue(missionID, out MissionTemplate template) ? template : null;
            };

            FactionStoryService.InjectStaticMissionForTests = missionID => _injectResult;
            FactionStoryService.WorldDangerNotifierForTests = score => _worldDangerMaxScores.Add(score);

            ctx.Service = new GameObject("FactionStory_FT09_Test").AddComponent<FactionStoryService>();
            ReflectionTools.SetStaticProperty(typeof(FactionStoryService), "Instance", ctx.Service);
            ReflectionTools.InvokeInstance(ctx.Service, "Awake");

            if (!string.IsNullOrEmpty(restoreJsonBeforeEnable))
            {
                ctx.Service.RestoreFromSave(restoreJsonBeforeEnable);
            }

            // EditMode AddComponent does not guarantee OnEnable execution.
            ReflectionTools.InvokeInstance(ctx.Service, "OnEnable");
            return ctx;
        }

        private Dictionary<string, string> BuildTableMap(
            bool neutralOnlyRoute,
            bool allDifficultyDeltaZero,
            bool includeStoryStages)
        {
            string routeCsv = neutralOnlyRoute
                ? "factionID,0\nname,Neutral\ndescription,Neutral only\n"
                : "factionID,1\nname,Order\ndescription,Order route\n";

            string stageCsv = includeStoryStages
                ? "stageID,1001,1002,1003\n" +
                  "factionID,1,1,1\n" +
                  "stageIndex,1,2,3\n" +
                  "scoreThreshold,10,30,60\n" +
                  "missionID,9001,9002,9003\n" +
                  "dialogueKey,story.order.stage1,story.order.stage2,story.order.stage3\n"
                : "stageID,1001\nfactionID,1\nstageIndex,1\nscoreThreshold,10\nmissionID,9001\ndialogueKey,story.order.stage1\n";

            string difficultyCsv = allDifficultyDeltaZero
                ? "difficulty,F,B,SS\nbaseReward,10,20,30\nbaseDuration,1,1,1\nbaseDeathRate,0.1,0.1,0.1\nfactionScoreDelta,0,0,0\n"
                : "difficulty,F,B,SS,X\nbaseReward,10,20,30,40\nbaseDuration,1,1,1,1\nbaseDeathRate,0.1,0.1,0.1,0.1\nfactionScoreDelta,1,5,20,35\n";

            string missionTemplateCsv =
                "missionID,1,9001,9002,9003\n" +
                "difficulty,F,F,B,SS\n" +
                "typeID,1,1,1,1\n" +
                "factionID,1,1,1,1\n" +
                "categoryID,0,3,3,3\n";

            string systemConstantsCsv =
                "key,FACTION_NEUTRAL_ID\n" +
                "value,0\n" +
                "description,neutral faction id\n";

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FactionRouteTable"] = routeCsv,
                ["StoryStageTable"] = stageCsv,
                ["MissionDifficultyTable"] = difficultyCsv,
                ["MissionTemplate"] = missionTemplateCsv,
                ["SystemConstants"] = systemConstantsCsv
            };
        }

        private void HandleScoreChanged(OnFactionScoreChangedEvent evt)
        {
            _scoreChangedEvents.Add(evt);
            _eventOrder.Add("ScoreChanged");
        }

        private void HandleStageUnlocked(OnFactionStoryStageUnlockedEvent evt)
        {
            _stageUnlockedEvents.Add(evt);
            _eventOrder.Add("StageUnlocked");
        }

        private void HandleDialogueConfirmed(OnFactionStoryDialogueConfirmedEvent evt)
        {
            _dialogueConfirmedEvents.Add(evt);
        }

        private void HandleStageResolved(OnFactionStoryStageResolvedEvent evt)
        {
            _stageResolvedEvents.Add(evt);
            _eventOrder.Add("StageResolved");
        }

        private void HandleRouteCompleted(OnFactionRouteCompletedEvent evt)
        {
            _routeCompletedEvents.Add(evt);
            _eventOrder.Add("RouteCompleted");
        }

        private static void DestroyContext(TestContext ctx)
        {
            if (ctx == null)
            {
                return;
            }

            DestroyIfExists(ctx.Service);
            DestroyIfExists(ctx.Data);
        }

        private static void DestroyIfExists(Component c)
        {
            if (c == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        private static void ResetStatics()
        {
            ReflectionTools.InvokeStatic(typeof(EventBus), "ClearAll");
            ReflectionTools.InvokeStatic(typeof(FactionStoryService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(DataManager), "ResetForTests");
        }

        private sealed class TestContext
        {
            public DataManager Data;
            public FactionStoryService Service;
        }
    }

    internal static class ReflectionTools
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, AnyStatic);
            Assert.IsNotNull(method, $"Missing static method: {type.Name}.{methodName}");
            method.Invoke(null, args);
        }

        public static void InvokeStatic(Type type, string methodName, Type[] parameterTypes, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, AnyStatic, null, parameterTypes, null);
            Assert.IsNotNull(method, $"Missing static method: {type.Name}.{methodName}");
            method.Invoke(null, args);
        }

        public static void InvokeInstance(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, AnyInstance);
            Assert.IsNotNull(method, $"Missing instance method: {target.GetType().Name}.{methodName}");
            method.Invoke(target, args);
        }

        public static void SetStaticProperty(Type type, string propertyName, object value)
        {
            PropertyInfo prop = type.GetProperty(propertyName, AnyStatic);
            Assert.IsNotNull(prop, $"Missing static property: {type.Name}.{propertyName}");
            prop.SetValue(null, value);
        }
    }
}
