using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Recruitment;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Staff;
using TheGuild.Gameplay.Trait;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Recruitment
{
    public sealed class RecruitmentServiceTests
    {
        private TestContext _ctx;
        private long _mockNowUtc;

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

            _ctx = CreateContext(BuildTableMap());
            _ctx.Recruitment.InitializeAsNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyContext(_ctx);
            _ctx = null;

            ResetStatics();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AC_AR_01_InitializeAsNewGame_PoolsHaveConfiguredSize()
        {
            int expected = _ctx.Data.GetInt("RECRUIT_POOL_SIZE");
            Assert.AreEqual(expected, _ctx.Recruitment.GetRookiePool().Count);
            Assert.AreEqual(expected, _ctx.Recruitment.GetVeteranPool().Count);
        }

        [Test]
        public void AC_AR_02_RookieAndVeteranRanksAreInAllowedSets()
        {
            IReadOnlyList<RecruitCandidate> rookies = _ctx.Recruitment.GetRookiePool();
            for (int i = 0; i < rookies.Count; i++)
            {
                string rank = rookies[i].AdventurerInstance.rank;
                Assert.IsTrue(rank == "F" || rank == "E", $"Unexpected rookie rank: {rank}");
            }

            IReadOnlyList<RecruitCandidate> veterans = _ctx.Recruitment.GetVeteranPool();
            for (int i = 0; i < veterans.Count; i++)
            {
                string rank = veterans[i].AdventurerInstance.rank;
                bool ok = rank == "D" || rank == "C" || rank == "B" || rank == "A" || rank == "S";
                Assert.IsTrue(ok, $"Unexpected veteran rank: {rank}");
            }
        }

        [Test]
        public void AC_AR_03_MaxRecruitableRankD_ProducesOnlyDInVeteranPool()
        {
            _ctx.Guild.InitializeAsNewGame();
            _mockNowUtc += 10;
            _ctx.Recruitment.ManualRefresh();

            IReadOnlyList<RecruitCandidate> veterans = _ctx.Recruitment.GetVeteranPool();
            for (int i = 0; i < veterans.Count; i++)
            {
                Assert.AreEqual("D", veterans[i].AdventurerInstance.rank);
            }
        }

        [Test]
        public void AC_AR_04_RecruitRookie_RemovesCandidateAndAddsRoster()
        {
            RecruitCandidate rookie = _ctx.Recruitment.GetRookiePool()[0];
            int rosterBefore = _ctx.Roster.GetRosterCount();

            bool ok = _ctx.Recruitment.RecruitRookie(rookie.CandidateID);

            Assert.IsTrue(ok);
            Assert.AreEqual(rosterBefore + 1, _ctx.Roster.GetRosterCount());
            Assert.IsFalse(ContainsCandidate(_ctx.Recruitment.GetRookiePool(), rookie.CandidateID));
        }

        [Test]
        public void AC_AR_05_RecruitVeteran_DeductsGoldAndRemovesCandidate()
        {
            RecruitCandidate veteran = _ctx.Recruitment.GetVeteranPool()[0];
            veteran.ReputationReq = 0;
            veteran.Cost = 123;
            int goldBefore = _ctx.Resource.GetGold();

            bool ok = _ctx.Recruitment.RecruitVeteran(veteran.CandidateID);

            Assert.IsTrue(ok);
            Assert.AreEqual(goldBefore - 123, _ctx.Resource.GetGold());
            Assert.IsFalse(ContainsCandidate(_ctx.Recruitment.GetVeteranPool(), veteran.CandidateID));
        }

        [Test]
        public void AC_AR_06_RecruitVeteran_ReputationInsufficient_ReturnsFalse()
        {
            RecruitCandidate veteran = _ctx.Recruitment.GetVeteranPool()[0];
            veteran.ReputationReq = _ctx.Resource.GetReputation() + 1;
            int goldBefore = _ctx.Resource.GetGold();

            bool ok = _ctx.Recruitment.RecruitVeteran(veteran.CandidateID);

            Assert.IsFalse(ok);
            Assert.AreEqual(goldBefore, _ctx.Resource.GetGold());
            Assert.IsTrue(ContainsCandidate(_ctx.Recruitment.GetVeteranPool(), veteran.CandidateID));
        }

        [Test]
        public void AC_AR_07_RecruitVeteran_GoldInsufficient_ReturnsFalse()
        {
            RecruitCandidate veteran = _ctx.Recruitment.GetVeteranPool()[0];
            veteran.ReputationReq = 0;
            veteran.Cost = _ctx.Resource.GetGold() + 1000;
            int goldBefore = _ctx.Resource.GetGold();

            bool ok = _ctx.Recruitment.RecruitVeteran(veteran.CandidateID);

            Assert.IsFalse(ok);
            Assert.AreEqual(goldBefore, _ctx.Resource.GetGold());
        }

        [Test]
        public void AC_AR_08_RosterFull_BothRecruitApisReturnFalse()
        {
            int cap = _ctx.Building.GetRosterCap();
            for (int i = 0; i < cap; i++)
            {
                AdventurerInstance dummy = _ctx.Roster.GetFactory().CreateRandomInstance("F", 1, 1, Array.Empty<int>());
                Assert.IsTrue(_ctx.Roster.AddAdventurer(dummy));
            }

            int rookieId = _ctx.Recruitment.GetRookiePool()[0].CandidateID;
            int veteranId = _ctx.Recruitment.GetVeteranPool()[0].CandidateID;

            Assert.IsFalse(_ctx.Recruitment.RecruitRookie(rookieId));
            Assert.IsFalse(_ctx.Recruitment.RecruitVeteran(veteranId));
        }

        [Test]
        public void AC_AR_09_CheckAutoRefresh_RefreshesWhenIntervalElapsed()
        {
            int interval = _ctx.Recruitment.GetCurrentRefreshIntervalSecondsForTests();
            ReflectionTools.SetPrivateField(_ctx.Recruitment, "_lastRefreshTimestamp", _mockNowUtc - interval - 1L);

            bool refreshed = _ctx.Recruitment.CheckAutoRefresh();

            Assert.IsTrue(refreshed);
            Assert.AreEqual(_ctx.Data.GetInt("RECRUIT_POOL_SIZE"), _ctx.Recruitment.GetRookiePool().Count);
            Assert.AreEqual(_ctx.Data.GetInt("RECRUIT_POOL_SIZE"), _ctx.Recruitment.GetVeteranPool().Count);
        }

        [Test]
        public void AC_AR_10_ManualRefresh_FreePath_DecrementsCounterAndUpdatesTimestamp()
        {
            int freeBefore = _ctx.Recruitment.GetFreeRefreshRemaining();
            long lastBefore = ReflectionTools.GetPrivateField<long>(_ctx.Recruitment, "_lastRefreshTimestamp");
            _mockNowUtc += 3;

            bool ok = _ctx.Recruitment.ManualRefresh();

            Assert.IsTrue(ok);
            Assert.AreEqual(freeBefore - 1, _ctx.Recruitment.GetFreeRefreshRemaining());
            Assert.Greater(ReflectionTools.GetPrivateField<long>(_ctx.Recruitment, "_lastRefreshTimestamp"), lastBefore);
        }

        [Test]
        public void AC_AR_11_ManualRefresh_PaidPath_DeductsRefreshCost()
        {
            ReflectionTools.SetPrivateField(_ctx.Recruitment, "_freeRefreshRemaining", 0);
            int refreshCost = _ctx.Data.GetInt("REFRESH_COST");
            int goldBefore = _ctx.Resource.GetGold();
            _mockNowUtc += 3;

            bool ok = _ctx.Recruitment.ManualRefresh();

            Assert.IsTrue(ok);
            Assert.AreEqual(goldBefore - refreshCost, _ctx.Resource.GetGold());
        }

        [Test]
        public void AC_AR_12_ManualRefresh_NoFreeAndNoGold_ReturnsFalse()
        {
            ReflectionTools.SetPrivateField(_ctx.Recruitment, "_freeRefreshRemaining", 0);
            _ctx.Resource.AddGoldAllowBankruptcy(-1_000_000);
            _mockNowUtc += 3;

            bool ok = _ctx.Recruitment.ManualRefresh();

            Assert.IsFalse(ok);
        }

        [Test]
        public void AC_AR_13_OnDailyReset_ResetsFreeRefresh()
        {
            ReflectionTools.SetPrivateField(_ctx.Recruitment, "_freeRefreshRemaining", 0);

            EventBus.Publish(EventNames.OnDailyReset);

            Assert.AreEqual(_ctx.Data.GetInt("DAILY_FREE_REFRESH"), _ctx.Recruitment.GetFreeRefreshRemaining());
        }

        [Test]
        public void AC_AR_14_RestoreThenCheckAutoRefresh_OnlyOneRefreshExecutes()
        {
            int interval = _ctx.Recruitment.GetCurrentRefreshIntervalSecondsForTests();
            string save = _ctx.Recruitment.Serialize();
            save = ReplaceJsonLong(save, "lastRefreshTimestamp", _mockNowUtc - (3L * interval));

            _ctx.Recruitment.RestoreFromSave(save);

            bool first = _ctx.Recruitment.CheckAutoRefresh();
            bool second = _ctx.Recruitment.CheckAutoRefresh();

            Assert.IsTrue(first);
            Assert.IsFalse(second);
        }

        [Test]
        public void AC_AR_15_UniqueTemplateAlreadyInRoster_IsFilteredOut()
        {
            AdventurerInstance unique = _ctx.Roster.GetFactory().CreateFromTemplate(1);
            if (unique != null)
            {
                _ctx.Roster.AddAdventurer(unique);
            }

            _mockNowUtc += 5;
            _ctx.Recruitment.ManualRefresh();

            Assert.IsFalse(ContainsTemplate(_ctx.Recruitment.GetRookiePool(), 1));
            Assert.IsFalse(ContainsTemplate(_ctx.Recruitment.GetVeteranPool(), 1));
        }

        [Test]
        public void AC_AR_16_CandidateIdAndTemplateId_NoDuplicatesInSamePool()
        {
            AssertUniqueIds(_ctx.Recruitment.GetRookiePool());
            AssertUniqueIds(_ctx.Recruitment.GetVeteranPool());
        }

        [Test]
        public void AC_AR_17_RollVeteranRank_DistributionWithinTolerance()
        {
            RecruitmentPoolGenerator generator = new RecruitmentPoolGenerator(
                _ctx.Roster.GetFactory(),
                _ctx.Roster,
                _ctx.Profession,
                _ctx.Race,
                _ctx.Trait,
                _ctx.Guild,
                _ctx.Data,
                new System.Random(12345));

            int dCount = 0;
            int sCount = 0;
            const int total = 1000;

            for (int i = 0; i < total; i++)
            {
                string rank = generator.RollVeteranRank("S");
                if (rank == "D") dCount++;
                if (rank == "S") sCount++;
            }

            float dRate = dCount / (float)total;
            float sRate = sCount / (float)total;
            Assert.That(dRate, Is.InRange(0.35f, 0.45f));
            Assert.That(sRate, Is.InRange(0.00f, 0.08f));
        }

        [Test]
        public void AC_AR_18_StaffLocked_IntervalEqualsBuildingBase()
        {
            Assert.IsFalse(_ctx.Building.IsStaffSystemUnlocked());
            Assert.AreEqual(86400, _ctx.Recruitment.GetCurrentRefreshIntervalSecondsForTests());
        }

        [Test]
        public void AC_AR_19_StaffReductionApplied_IntervalReducedAndClamped()
        {
            Assert.AreEqual(UpgradeResult.SUCCESS, _ctx.Building.TryUpgradeBuilding(6));
            Assert.IsTrue(_ctx.Building.IsStaffSystemUnlocked());

            HireResult hire = _ctx.Staff.HireStaff(new CandidateCard { staffID = 103 });
            Assert.AreEqual(HireStaffResult.OK, hire.result);
            Assert.AreEqual(AssignResult.SUCCESS, _ctx.Staff.TryAssignStaff(hire.instanceID, 4));

            Assert.AreEqual(79200, _ctx.Recruitment.GetCurrentRefreshIntervalSecondsForTests());
        }

        private static void AssertUniqueIds(IReadOnlyList<RecruitCandidate> pool)
        {
            HashSet<int> candidateIds = new HashSet<int>();
            HashSet<int> positiveTemplateIds = new HashSet<int>();
            for (int i = 0; i < pool.Count; i++)
            {
                RecruitCandidate c = pool[i];
                Assert.IsTrue(candidateIds.Add(c.CandidateID), $"Duplicate candidateID: {c.CandidateID}");

                int templateID = c.AdventurerInstance == null ? 0 : c.AdventurerInstance.templateID;
                if (templateID > 0)
                {
                    Assert.IsTrue(positiveTemplateIds.Add(templateID), $"Duplicate templateID: {templateID}");
                }
            }
        }

        private static bool ContainsCandidate(IReadOnlyList<RecruitCandidate> pool, int candidateId)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].CandidateID == candidateId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTemplate(IReadOnlyList<RecruitCandidate> pool, int templateId)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                AdventurerInstance inst = pool[i].AdventurerInstance;
                if (inst != null && inst.templateID == templateId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceJsonLong(string json, string key, long value)
        {
            string pattern = $"\"{key}\"\\s*:\\s*-?\\d+";
            return Regex.Replace(json, pattern, $"\"{key}\":{value}");
        }

        private Dictionary<string, string> BuildTableMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] names =
            {
                "SystemConstants",
                "StaffTuning",
                "MissionTemplate",
                "MissionDifficultyTable",
                "MissionTypeTable",
                "MissionCategoryTable",
                "ProfessionTable",
                "RaceTable",
                "TraitTable",
                "TraitGroupTable",
                "AdventurerTemplate",
                "RecruitCostTable",
                "GuildLevelTable",
                "StaffTable",
                "VeteranRankWeightTable"
            };

            for (int i = 0; i < names.Length; i++)
            {
                map[names[i]] = LoadCsv(names[i]);
            }

            map["BuildingTable"] =
                "buildingID_level,1_1,2_1,3_1,4_1,5_1,6_0,6_1\n" +
                "buildingID,1,2,3,4,5,6,6\n" +
                "name,MissionBoard,Counter,Hall,Tower,Vault,Lounge,Lounge\n" +
                "maxLevel,1,1,1,1,1,1,1\n" +
                "level,1,1,1,1,1,0,1\n" +
                "effectValue,5,86400,1,1,3600,0,0\n" +
                "upgradeCost,0,0,0,0,0,0,0\n" +
                "guildLevelReq,0,0,0,0,0,0,0\n" +
                "slotCount,0,0,0,1,0,0,0\n" +
                "maintenanceCost,0,0,0,0,0,0,0\n";

            // 測試專用 AdventurerTemplate 子集：production sample 中 templateID=2 引用
            // ProfessionTable 不存在的 professionID=4 會引發 startup LogError，影響其他測試。
            // 此處保留 templateID=1（C 階，isUnique=1，AC_AR_15 需要）與 templateID=3（E 階，非 unique）。
            map["AdventurerTemplate"] =
                "templateID,1,3\n" +
                "name,艾克·鐵拳,傭兵甲\n" +
                "rank,C,E\n" +
                "professionID,1,7\n" +
                "raceID,3,0\n" +
                "fixedTraitIDs,0,0\n" +
                "randomTraitGroupIDs,1|3,2|4\n" +
                "factionID,0,0\n" +
                "isUnique,1,0\n";

            // 測試專用 StaffTable：production sample staffID=101 的 uiFlagIDs("") 與 uiFlagBuildingIDs(0)
            // 解析後長度不一致（0 != 1）會 throw StaffTableValidationException。此處僅保留 staffID=103
            // （AC_AR_19 需要的 RecruitRefreshOnCounter / slot=4 / 7200 秒），並把 uiFlag 兩欄都留空保持長度一致。
            map["StaffTable"] =
                "staffID,103\n" +
                "name,Counter Lady\n" +
                "rarity,2\n" +
                "salary,30\n" +
                "severancePay,80\n" +
                "isFiller,false\n" +
                "factionID,0\n" +
                "minGuildLevel,1\n" +
                "effectIDs,RecruitRefreshOnCounter\n" +
                "effectValues,7200\n" +
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
            // StaffTuning 以 SystemConstants 模式合併，提供 EFFECT_MAX_* / BUILDING_SWITCH_COOLDOWN_SECONDS 等 key 給
            // StaffTableLoader.LoadTuningConstants 查詢。production StaffTuning.csv 已含完整 key/value。
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
            DataManager.RegisterTable<VeteranRankWeightEntry>("VeteranRankWeightTable");

            TestContext ctx = new TestContext();

            ctx.Data = new GameObject("DataManager_FT01_Test").AddComponent<DataManager>();
            ReflectionTools.InvokeInstance(ctx.Data, "InitializeForTests");

            ctx.Time = new GameObject("TimeSystem_FT01_Test").AddComponent<TimeSystem>();
            ReflectionTools.InvokeInstance(ctx.Time, "InitializeForTests");

            ctx.Resource = new GameObject("Resource_FT01_Test").AddComponent<ResourceManagement>();
            ReflectionTools.InvokeInstance(ctx.Resource, "InitializeForTests");

            ctx.Mission = new GameObject("MissionDB_FT01_Test").AddComponent<MissionDatabaseService>();
            ReflectionTools.InvokeInstance(ctx.Mission, "InitializeForTests");

            ctx.Profession = new GameObject("Profession_FT01_Test").AddComponent<ProfessionService>();
            ReflectionTools.InvokeInstance(ctx.Profession, "InitializeForTests");

            ctx.Race = new GameObject("Race_FT01_Test").AddComponent<RaceService>();
            ReflectionTools.InvokeInstance(ctx.Race, "InitializeForTests");

            ctx.Trait = new GameObject("Trait_FT01_Test").AddComponent<TraitService>();
            ReflectionTools.InvokeInstance(ctx.Trait, "InitializeForTests");

            ctx.Roster = new GameObject("Roster_FT01_Test").AddComponent<AdventurerRoster>();
            ReflectionTools.InvokeInstance(ctx.Roster, "InitializeForTests");

            ctx.Guild = new GameObject("Guild_FT01_Test").AddComponent<GuildCoreService>();
            ReflectionTools.InvokeInstance(ctx.Guild, "InitializeForTests");
            ctx.Guild.InitializeAsNewGame();

            ctx.Building = new GameObject("Building_FT01_Test").AddComponent<BuildingService>();
            // BuildingService.InitializeForTests 不顯式賦值 Instance（與 AdventurerRoster/GuildCoreService 不一致）。
            // EditMode AddComponent 在此情境未可靠觸發 Awake，故以 reflection 補設 Instance。
            ReflectionTools.SetStaticProperty(typeof(BuildingService), "Instance", ctx.Building);
            ReflectionTools.InvokeInstance(ctx.Building, "InitializeForTests");
            ctx.Building.InitializeAsNewGame();

            ctx.Staff = new GameObject("Staff_FT01_Test").AddComponent<StaffService>();
            // StaffService 無 InitializeForTests；同樣補設 Instance 並手動執行 Awake 路徑（loader Init）。
            ReflectionTools.SetStaticProperty(typeof(StaffService), "Instance", ctx.Staff);
            ReflectionTools.InvokeInstance(ctx.Staff, "Awake");

            ctx.Recruitment = new GameObject("Recruitment_FT01_Test").AddComponent<RecruitmentService>();
            ReflectionTools.SetStaticProperty(typeof(RecruitmentService), "Instance", ctx.Recruitment);
            ReflectionTools.InvokeInstance(ctx.Recruitment, "InitializeForTests");
            // EditMode AddComponent 不會觸發 OnEnable；手動執行以完成事件訂閱（OnSecondTick / OnDailyReset）。
            ReflectionTools.InvokeInstance(ctx.Recruitment, "OnEnable");

            return ctx;
        }

        private static void DestroyContext(TestContext ctx)
        {
            if (ctx == null)
            {
                return;
            }

            DestroyIfExists(ctx.Recruitment);
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

            ReflectionTools.InvokeStatic(typeof(RecruitmentService), "ResetForTests");
            // StaffService 無 ResetForTests；其 OnDestroy 自動清 Instance，DestroyContext 已處理 GameObject。
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
            public RecruitmentService Recruitment;
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
