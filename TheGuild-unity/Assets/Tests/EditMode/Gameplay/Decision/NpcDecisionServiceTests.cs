using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Decision;
using TheGuild.Gameplay.Decision.Events;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Staff;
using TheGuild.Gameplay.Trait;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Decision
{
    public sealed class NpcDecisionServiceTests
    {
        private TestContext _ctx;
        private long _mockNowUtc;
        private readonly List<OnAutoPickupEvent> _autoPickupEvents = new List<OnAutoPickupEvent>(8);

        [SetUp]
        public void SetUp()
        {
            ResetStatics();
            _mockNowUtc = 1_700_000_000L;

            ReflectionTools.InvokeStatic(
                typeof(TimeSystem),
                "SetClockProviderForTests",
                new[] { typeof(Func<long>) },
                (Func<long>)(() => _mockNowUtc));

            // mock TraitTable 的 traitID=8 effectTarget=willingness_unknown_tag 不在 TraitDatabaseLoader 允許清單，
            // loader 會在啟動期 LogError 並跳過該列。AC_ND3_13 仰賴此 trait 不入庫的行為驗證 service 無破壞性。
            LogAssert.Expect(LogType.Error, "[TraitDatabaseLoader] traitID=8 effectTarget=willingness_unknown_tag is invalid, skip row.");

            _ctx = CreateContext(BuildTableMap());
            EventBus.Subscribe<OnAutoPickupEvent>(HandleAutoPickupEvent);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<OnAutoPickupEvent>(HandleAutoPickupEvent);
            _autoPickupEvents.Clear();

            DestroyContext(_ctx);
            _ctx = null;

            ResetStatics();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AC_ND3_01_MakeDecision_EffectiveScoreWithinJitterRange_AndAccepted()
        {
            AdventurerInstance adv = AddAdventurer("S");
            _ctx.Decision.InitializeForTests(seed: 1001);

            float preview = _ctx.Decision.PreviewEffectiveScore(adv.instanceID, 1);
            DecisionResult result = _ctx.Decision.MakeDecision(adv.instanceID, 1);
            float jitter = _ctx.Data.GetFloat("WILLINGNESS_JITTER");

            Assert.That(result.EffectiveScore, Is.InRange(preview - jitter, preview + jitter));
            Assert.IsTrue(result.Accepted);
            Assert.IsNull(result.RejectionReason);
        }

        [Test]
        public void AC_ND3_02_WillingnessDiffA_AppliesToAOnly_NotC()
        {
            AdventurerInstance withTrait = AddAdventurer("C", new[] { 2 });
            AdventurerInstance noTrait = AddAdventurer("C");
            ReflectionTools.SetPrivateField(_ctx.Decision, "_willingnessJitter", 0f);

            float withTraitA = _ctx.Decision.PreviewEffectiveScore(withTrait.instanceID, 4); // A
            float noTraitA = _ctx.Decision.PreviewEffectiveScore(noTrait.instanceID, 4);
            float withTraitC = _ctx.Decision.PreviewEffectiveScore(withTrait.instanceID, 3); // C
            float noTraitC = _ctx.Decision.PreviewEffectiveScore(noTrait.instanceID, 3);

            Assert.That(withTraitA - noTraitA, Is.EqualTo(-0.30f).Within(0.0001f));
            Assert.That(withTraitC - noTraitC, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void AC_ND3_03_PositiveAndNegativeJitterCanFlipAcceptance()
        {
            AdventurerInstance adv = AddAdventurer("D");
            float preview = _ctx.Decision.PreviewEffectiveScore(adv.instanceID, 2);

            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", preview);
            ReflectionTools.SetPrivateField(_ctx.Decision, "_rng", new FixedRandom(1.0));
            DecisionResult positive = _ctx.Decision.MakeDecision(adv.instanceID, 2);

            ReflectionTools.SetPrivateField(_ctx.Decision, "_rng", new FixedRandom(0.0));
            DecisionResult negative = _ctx.Decision.MakeDecision(adv.instanceID, 2);

            Assert.IsTrue(positive.Accepted);
            Assert.IsFalse(negative.Accepted);
        }

        [Test]
        public void AC_ND3_04_StaffBonusAddsExactly005()
        {
            AdventurerInstance adv = AddAdventurer("D");
            ReflectionTools.SetPrivateField(_ctx.Decision, "_willingnessJitter", 0f);
            float baseline = _ctx.Decision.PreviewEffectiveScore(adv.instanceID, 2);

            // 解鎖職員系統（升級 Lounge）以允許 HireStaff／TryAssignStaff。
            Assert.AreEqual(UpgradeResult.SUCCESS, _ctx.Building.TryUpgradeBuilding(6));
            HireResult hire = _ctx.Staff.HireStaff(new CandidateCard { staffID = 103 });
            Assert.AreEqual(HireStaffResult.OK, hire.result);
            Assert.AreEqual(AssignResult.SUCCESS, _ctx.Staff.TryAssignStaff(hire.instanceID, 4));

            float boosted = _ctx.Decision.PreviewEffectiveScore(adv.instanceID, 2);
            Assert.That(boosted - baseline, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void AC_ND3_05_RejectionReason_BranchesAreCorrect()
        {
            AdventurerInstance noTrait = AddAdventurer("D");
            AdventurerInstance withTrait = AddAdventurer("D", new[] { 2 });
            ReflectionTools.SetPrivateField(_ctx.Decision, "_willingnessJitter", 0f);

            // TooRisky
            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", 2f);
            DecisionResult tooRisky = _ctx.Decision.MakeDecision(noTrait.instanceID, 2);
            Assert.IsFalse(tooRisky.Accepted);
            Assert.AreEqual(TheGuild.Gameplay.Decision.RejectionReason.TooRisky, tooRisky.RejectionReason);

            // NotWilling: threshold between base and after-traits
            float baseScore = _ctx.Decision.PreviewEffectiveScore(noTrait.instanceID, 4);
            float afterTrait = _ctx.Decision.PreviewEffectiveScore(withTrait.instanceID, 4);
            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", (baseScore + afterTrait) * 0.5f);
            DecisionResult notWilling = _ctx.Decision.MakeDecision(withTrait.instanceID, 4);
            Assert.IsFalse(notWilling.Accepted);
            Assert.AreEqual(TheGuild.Gameplay.Decision.RejectionReason.NotWilling, notWilling.RejectionReason);

            // NotInterested: base/traits pass, jitter makes final fail
            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", baseScore - 0.02f);
            ReflectionTools.SetPrivateField(_ctx.Decision, "_willingnessJitter", 0.1f);
            ReflectionTools.SetPrivateField(_ctx.Decision, "_rng", new FixedRandom(0.0)); // -jitter
            DecisionResult notInterested = _ctx.Decision.MakeDecision(noTrait.instanceID, 4);
            Assert.IsFalse(notInterested.Accepted);
            Assert.AreEqual(TheGuild.Gameplay.Decision.RejectionReason.NotInterested, notInterested.RejectionReason);
        }

        [Test]
        public void AC_ND3_06_NonIdleMakeDecision_ReturnsRejectedWithNullReason_AndNoDispatch()
        {
            AdventurerInstance adv = AddAdventurer("D");
            _ctx.Roster.UpdateStatus(adv.instanceID, AdventurerStatus.Dispatched, 999);
            int before = _ctx.Dispatch.GetActiveMissionCount();

            DecisionResult result = _ctx.Decision.MakeDecision(adv.instanceID, 1);

            Assert.IsFalse(result.Accepted);
            Assert.IsNull(result.RejectionReason);
            Assert.AreEqual(before, _ctx.Dispatch.GetActiveMissionCount());
        }

        [Test]
        public void AC_ND3_07_AutoPickupTick_BeforeIdleThreshold_NoAttempt()
        {
            AdventurerInstance adv = AddAdventurer("S");
            PostRegularMission(1);

            float idleSeconds = ReflectionTools.GetPrivateField<float>(_ctx.Decision, "_autoPickupIdleSeconds");
            adv.idleSinceTimestamp = _mockNowUtc - (long)idleSeconds + 1;
            adv.lastAutoPickupTimestamp = 0;

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(0L, adv.lastAutoPickupTimestamp);
            Assert.AreEqual(0, _ctx.Dispatch.GetActiveMissionCount());
        }

        [Test]
        public void AC_ND3_08_AutoPickupAfterIdle_SelectsBestMission()
        {
            AdventurerInstance adv = AddAdventurer("S");
            adv.idleSinceTimestamp = _mockNowUtc - 900; // 15m

            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", -1f);
            PostRegularMission(1); // F
            PostRegularMission(2); // D

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(1, _autoPickupEvents.Count);
            Assert.AreEqual(1, _autoPickupEvents[0].MissionID);
        }

        [Test]
        public void AC_ND3_09_AutoPickupIntervalGuard_BlocksSecondAttempt()
        {
            AdventurerInstance adv = AddAdventurer("F");
            adv.idleSinceTimestamp = _mockNowUtc - 900; // 15m
            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", 1f); // force no dispatch
            PostRegularMission(2);

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));
            long first = adv.lastAutoPickupTimestamp;

            _mockNowUtc += 60; // 1m < 30m interval
            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(first, adv.lastAutoPickupTimestamp);
            Assert.AreEqual(0, _ctx.Dispatch.GetActiveMissionCount());
        }

        [Test]
        public void AC_ND3_10_AllScoresBelowThreshold_NoDispatch_ButTimestampUpdated()
        {
            AdventurerInstance adv = AddAdventurer("F");
            adv.idleSinceTimestamp = _mockNowUtc - 900; // 15m

            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", 1f);
            PostRegularMission(2);

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(0, _ctx.Dispatch.GetActiveMissionCount());
            Assert.AreEqual(_mockNowUtc, adv.lastAutoPickupTimestamp);
        }

        [Test]
        public void AC_ND3_11_AutoPickupSuccess_PublishesEventPayload()
        {
            AdventurerInstance adv = AddAdventurer("S");
            adv.idleSinceTimestamp = _mockNowUtc - 900;

            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", -1f);
            PostRegularMission(1);
            _autoPickupEvents.Clear();

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(1, _autoPickupEvents.Count);
            Assert.AreEqual(adv.instanceID, _autoPickupEvents[0].AdventurerInstanceID);
            Assert.AreEqual(1, _autoPickupEvents[0].MissionID);
        }

        [Test]
        public void AC_ND3_12_DispatchFalse_NoRetry_NoException_NoEvent()
        {
            // MissionDispatchService FALLBACK_MAX_CONCURRENT_MISSIONS=5（FT-07 整合前的硬常數）。
            // 先填滿 5 個 active missions 讓後續 Dispatch 必失敗。
            int[] fillerMissionIDs = new[] { 1, 2, 3, 4, 5 };
            for (int i = 0; i < fillerMissionIDs.Length; i++)
            {
                AdventurerInstance filler = AddAdventurer("S");
                Assert.IsTrue(
                    _ctx.Dispatch.Dispatch(filler.instanceID, fillerMissionIDs[i], DispatchSource.PlayerManual),
                    $"setup failed: expected dispatch {fillerMissionIDs[i]} to occupy slot");
            }

            AdventurerInstance target = AddAdventurer("S");
            target.idleSinceTimestamp = _mockNowUtc - 900;

            ReflectionTools.SetPrivateField(_ctx.Decision, "_acceptanceThreshold", -1f);
            PostRegularMission(6);
            _autoPickupEvents.Clear();

            EventBus.Publish(new OnMinuteTickEvent(_mockNowUtc));

            Assert.AreEqual(_mockNowUtc, target.lastAutoPickupTimestamp);
            Assert.AreEqual(0, _autoPickupEvents.Count);
        }

        [Test]
        public void AC_ND3_13_UnknownEffectTarget_Warns_AndDoesNotAffectScore()
        {
            // TraitDatabaseLoader 已於啟動期過濾 traitID=8（unknown effectTarget），
            // 故 service 取得 GetTrait(8)=null，跳過該特質、不影響其餘分數。
            AdventurerInstance withUnknown = AddAdventurer("D", new[] { 8 });
            AdventurerInstance noTrait = AddAdventurer("D");
            ReflectionTools.SetPrivateField(_ctx.Decision, "_willingnessJitter", 0f);

            float scoreWithUnknown = _ctx.Decision.PreviewEffectiveScore(withUnknown.instanceID, 2);
            float scoreWithout = _ctx.Decision.PreviewEffectiveScore(noTrait.instanceID, 2);

            Assert.That(scoreWithUnknown, Is.EqualTo(scoreWithout).Within(0.0001f));
        }

        private void HandleAutoPickupEvent(OnAutoPickupEvent evt)
        {
            _autoPickupEvents.Add(evt);
        }

        private AdventurerInstance AddAdventurer(string rank, int[] traitIDs = null)
        {
            AdventurerInstance adv = new AdventurerInstance
            {
                instanceID = _ctx.Roster.AllocateInstanceID(),
                templateID = 0,
                name = "Test",
                rank = rank,
                professionID = 1,
                raceID = 1,
                traitIDs = traitIDs ?? Array.Empty<int>(),
                factionID = 0,
                status = AdventurerStatus.Idle,
                currentMissionID = 0,
                woundedUntilTimestamp = 0,
                idleSinceTimestamp = 0,
                lastAutoPickupTimestamp = 0
            };

            Assert.IsTrue(_ctx.Roster.AddAdventurer(adv));
            return _ctx.Roster.GetAdventurer(adv.instanceID);
        }

        private void PostRegularMission(int missionID)
        {
            Assert.AreEqual(PostResult.OK, _ctx.CommissionBoard.PostRegularMission(missionID));
        }

        private Dictionary<string, string> BuildTableMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] names =
            {
                "SystemConstants",
                "StaffTuning",
                "MissionDifficultyTable",
                "MissionTypeTable",
                "MissionCategoryTable",
                "ProfessionTable",
                "RaceTable",
                "TraitGroupTable",
                "AdventurerTemplate",
                "RecruitCostTable",
                "GuildLevelTable",
                "SuccessRateTable"
            };

            for (int i = 0; i < names.Length; i++)
            {
                map[names[i]] = LoadCsv(names[i]);
            }

            map["MissionTemplate"] =
                "missionID,1,2,3,4,5,6\n" +
                "difficulty,F,D,C,A,B,D\n" +
                "typeID,1,2,3,1,4,1\n" +
                "factionID,0,1,0,0,2,0\n" +
                "categoryID,0,0,0,0,1,0\n";

            map["TraitTable"] =
                "traitID,2,8\n" +
                "name,TraitA,TraitUnknown\n" +
                "description,For test,For test\n" +
                "effectType,behavior,behavior\n" +
                "effectTarget,willingness_diff_A,willingness_unknown_tag\n" +
                "effectValue,-0.30,-0.40\n";

            map["AdventurerTemplate"] =
                "templateID,1,3\n" +
                "name,UniqueA,CommonB\n" +
                "rank,C,E\n" +
                "professionID,1,7\n" +
                "raceID,3,0\n" +
                "fixedTraitIDs,0,0\n" +
                "randomTraitGroupIDs,1|3,2|4\n" +
                "factionID,0,0\n" +
                "isUnique,1,0\n";

            // BuildingTableLoader 對 buildingID=6 要求 levels {0..maxLevel}，必須同時提供 6_0 與 6_1。
            // building 4 (Tower / 公會櫃臺) effectValue=5：對齊 AC_ND3_12 填滿 5 個 active missions 的 setup 假設。
            map["BuildingTable"] =
                "buildingID_level,1_1,2_1,3_1,4_1,5_1,6_0,6_1\n" +
                "buildingID,1,2,3,4,5,6,6\n" +
                "name,MissionBoard,Counter,Hall,Tower,Vault,Lounge,Lounge\n" +
                "maxLevel,1,1,1,1,1,1,1\n" +
                "level,1,1,1,1,1,0,1\n" +
                "effectValue,5,86400,10,5,3600,0,0\n" +
                "upgradeCost,0,0,0,0,0,0,0\n" +
                "guildLevelReq,0,0,0,0,0,0,0\n" +
                "slotCount,0,0,0,1,0,0,0\n" +
                "maintenanceCost,0,0,0,0,0,0,0\n";

            map["StaffTable"] =
                "staffID,103\n" +
                "name,WillingnessStaff\n" +
                "rarity,2\n" +
                "salary,30\n" +
                "severancePay,80\n" +
                "isFiller,false\n" +
                "factionID,0\n" +
                "minGuildLevel,1\n" +
                "effectIDs,Willingness\n" +
                "effectValues,0.05\n" +
                "slotBuildingIDs,4\n" +
                "uiFlagIDs,\n" +
                "uiFlagBuildingIDs,\n";

            return map;
        }

        private static string LoadCsv(string tableName)
        {
            string path = Path.Combine(Application.dataPath, "Resources", "Data", "Tables", tableName + ".csv");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private TestContext CreateContext(Dictionary<string, string> tableMap)
        {
            ReflectionTools.InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(name => tableMap.TryGetValue(name, out string csv) ? csv : null));

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterSystemConstantsTable("StaffTuning");

            DataManager.RegisterTable<MissionTemplate>("MissionTemplate");
            DataManager.RegisterTable<MissionDifficultyData>("MissionDifficultyTable");
            DataManager.RegisterTable<MissionTypeData>("MissionTypeTable");
            DataManager.RegisterTable<MissionCategoryData>("MissionCategoryTable");
            DataManager.RegisterTable<ProfessionData>("ProfessionTable");
            DataManager.RegisterTable<RaceData>("RaceTable");
            DataManager.RegisterTable<TraitData>("TraitTable");
            DataManager.RegisterTable<TraitGroupData>("TraitGroupTable");
            DataManager.RegisterTable<AdventurerTemplate>("AdventurerTemplate");
            DataManager.RegisterTable<RecruitCostEntry>("RecruitCostTable");
            DataManager.RegisterTable<GuildLevelEntry>("GuildLevelTable");
            DataManager.RegisterTable<BuildingRow>("BuildingTable");
            DataManager.RegisterTable<StaffData>("StaffTable");
            DataManager.RegisterTable<SuccessRateRow>("SuccessRateTable");

            TestContext ctx = new TestContext();

            ctx.Data = new GameObject("DataManager_FT03_Test").AddComponent<DataManager>();
            ReflectionTools.InvokeInstance(ctx.Data, "InitializeForTests");

            ctx.Time = new GameObject("TimeSystem_FT03_Test").AddComponent<TimeSystem>();
            ReflectionTools.InvokeInstance(ctx.Time, "InitializeForTests");

            ctx.Resource = new GameObject("Resource_FT03_Test").AddComponent<ResourceManagement>();
            ReflectionTools.InvokeInstance(ctx.Resource, "InitializeForTests");

            ctx.Mission = new GameObject("MissionDB_FT03_Test").AddComponent<MissionDatabaseService>();
            ReflectionTools.InvokeInstance(ctx.Mission, "InitializeForTests");

            ctx.Profession = new GameObject("Profession_FT03_Test").AddComponent<ProfessionService>();
            ReflectionTools.InvokeInstance(ctx.Profession, "InitializeForTests");

            ctx.Race = new GameObject("Race_FT03_Test").AddComponent<RaceService>();
            ReflectionTools.InvokeInstance(ctx.Race, "InitializeForTests");

            ctx.Trait = new GameObject("Trait_FT03_Test").AddComponent<TraitService>();
            ReflectionTools.InvokeInstance(ctx.Trait, "InitializeForTests");

            ctx.Roster = new GameObject("Roster_FT03_Test").AddComponent<AdventurerRoster>();
            ReflectionTools.InvokeInstance(ctx.Roster, "InitializeForTests");

            ctx.Guild = new GameObject("Guild_FT03_Test").AddComponent<GuildCoreService>();
            ReflectionTools.InvokeInstance(ctx.Guild, "InitializeForTests");
            ctx.Guild.InitializeAsNewGame();

            ctx.Building = new GameObject("Building_FT03_Test").AddComponent<BuildingService>();
            ReflectionTools.SetStaticProperty(typeof(BuildingService), "Instance", ctx.Building);
            ReflectionTools.InvokeInstance(ctx.Building, "InitializeForTests");
            ctx.Building.InitializeAsNewGame();

            ctx.Staff = new GameObject("Staff_FT03_Test").AddComponent<StaffService>();
            ReflectionTools.SetStaticProperty(typeof(StaffService), "Instance", ctx.Staff);
            ReflectionTools.InvokeInstance(ctx.Staff, "Awake");

            ctx.CommissionBoard = new GameObject("Board_FT03_Test").AddComponent<CommissionBoardService>();
            ReflectionTools.SetStaticProperty(typeof(CommissionBoardService), "Instance", ctx.CommissionBoard);
            ReflectionTools.InvokeInstance(ctx.CommissionBoard, "InitializeForTests");

            ctx.Dispatch = new GameObject("Dispatch_FT03_Test").AddComponent<MissionDispatchService>();
            ReflectionTools.SetStaticProperty(typeof(MissionDispatchService), "Instance", ctx.Dispatch);
            ReflectionTools.InvokeInstance(ctx.Dispatch, "InitializeForTests");

            ctx.Decision = new GameObject("Decision_FT03_Test").AddComponent<NpcDecisionService>();
            ReflectionTools.SetStaticProperty(typeof(NpcDecisionService), "Instance", ctx.Decision);
            ctx.Decision.InitializeForTests(seed: 12345);
            ReflectionTools.InvokeInstance(ctx.Decision, "OnEnable");

            return ctx;
        }

        private static void DestroyContext(TestContext ctx)
        {
            if (ctx == null)
            {
                return;
            }

            DestroyIfExists(ctx.Decision);
            DestroyIfExists(ctx.Dispatch);
            DestroyIfExists(ctx.CommissionBoard);
            DestroyIfExists(ctx.Staff);
            DestroyIfExists(ctx.Building);
            DestroyIfExists(ctx.Guild);
            DestroyIfExists(ctx.Roster);
            DestroyIfExists(ctx.Trait);
            DestroyIfExists(ctx.Race);
            DestroyIfExists(ctx.Profession);
            DestroyIfExists(ctx.Mission);
            DestroyIfExists(ctx.Resource);
            DestroyIfExists(ctx.Time);
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

            ReflectionTools.InvokeStatic(typeof(NpcDecisionService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(MissionDispatchService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(CommissionBoardService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(BuildingService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(GuildCoreService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(AdventurerRoster), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(TraitService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(RaceService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(ProfessionService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(MissionDatabaseService), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(ResourceManagement), "ResetForTests");
            ReflectionTools.InvokeStatic(typeof(TimeSystem), "ResetTestHooks");
            ReflectionTools.InvokeStatic(typeof(DataManager), "ResetForTests");

            ReflectionTools.SetStaticProperty(typeof(StaffService), "Instance", null);
        }

        private sealed class TestContext
        {
            public DataManager Data;
            public TimeSystem Time;
            public ResourceManagement Resource;
            public MissionDatabaseService Mission;
            public ProfessionService Profession;
            public RaceService Race;
            public TraitService Trait;
            public AdventurerRoster Roster;
            public GuildCoreService Guild;
            public BuildingService Building;
            public StaffService Staff;
            public CommissionBoardService CommissionBoard;
            public MissionDispatchService Dispatch;
            public NpcDecisionService Decision;
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double _value;

            public FixedRandom(double value)
            {
                _value = Mathf.Clamp01((float)value);
            }

            protected override double Sample()
            {
                return _value;
            }
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

        public static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, AnyInstance);
            Assert.IsNotNull(field, $"Missing field: {target.GetType().Name}.{fieldName}");
            return (T)field.GetValue(target);
        }

        public static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, AnyInstance);
            Assert.IsNotNull(field, $"Missing field: {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }

        public static void SetStaticProperty(Type type, string propertyName, object value)
        {
            PropertyInfo prop = type.GetProperty(propertyName, AnyStatic);
            Assert.IsNotNull(prop, $"Missing static property: {type.Name}.{propertyName}");
            prop.SetValue(null, value);
        }
    }
}
