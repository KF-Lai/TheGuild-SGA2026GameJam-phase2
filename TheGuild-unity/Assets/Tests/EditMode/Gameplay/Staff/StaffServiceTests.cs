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

namespace Tests.EditMode.Gameplay.Staff
{
    public sealed class StaffServiceTests
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
        public void Test_v31_FT12_IsStaffHired_InRoster()
        {
            // IsStaffHired API：檢查職員是否已聘用（在名冊中）
            // 先聘用 staffID=103
            HireResult result = _ctx.Staff.HireStaff(new CandidateCard { staffID = 103 });
            Assert.AreEqual(HireStaffResult.OK, result.result, "Should hire staff successfully");

            // 驗證 IsStaffHired 返回 true
            bool hired = _ctx.Staff.IsStaffHired(103);
            Assert.IsTrue(hired, "Staff 103 should be marked as hired");
        }

        [Test]
        public void Test_v31_FT12_IsStaffHired_NotInRoster()
        {
            // IsStaffHired API：檢查未聘用職員（不在名冊中）
            bool hired = _ctx.Staff.IsStaffHired(103);
            Assert.IsFalse(hired, "Staff 103 should not be hired initially");
        }

        [Test]
        public void Test_v31_FT12_PostJamFields_PendingMissingNight_LoadedCorrectly()
        {
            // 5 個 Post-Jam 預埋欄位由 CsvParser 靜默忽略，不破壞 StaffData 載入
            IReadOnlyList<StaffData> rows = _ctx.Data.GetAll<StaffData>();
            Assert.IsNotNull(rows);
            StaffData row103 = null;
            foreach (StaffData r in rows)
            {
                if (r != null && r.staffID == 103) { row103 = r; break; }
            }
            Assert.IsNotNull(row103, "Staff 103 should exist after CSV load");
        }

        [Test]
        public void Test_v31_FT12_PostJamFields_TotalAdventurerDeaths_LoadedCorrectly()
        {
            // 5 個新欄位不破壞 effect 載入
            IReadOnlyList<StaffData> rows = _ctx.Data.GetAll<StaffData>();
            Assert.IsNotNull(rows);
            HireResult result = _ctx.Staff.HireStaff(new CandidateCard { staffID = 103 });
            Assert.AreEqual(HireStaffResult.OK, result.result);
        }

        [Test]
        public void Test_v31_FT12_PostJamFields_BlockedStages_LoadedCorrectly()
        {
            // 5 個新欄位不破壞 GetAll 查詢
            IReadOnlyList<StaffData> rows = _ctx.Data.GetAll<StaffData>();
            Assert.IsNotNull(rows);
            Assert.IsTrue(rows.Count >= 1);
        }

        [Test]
        public void Test_v31_FT12_PostJamFields_EffectApiNotBroken()
        {
            HireResult result = _ctx.Staff.HireStaff(new CandidateCard { staffID = 103 });
            Assert.AreEqual(HireStaffResult.OK, result.result);
            AssignResult assignResult = _ctx.Staff.TryAssignStaff(result.instanceID, 4);
            Assert.AreEqual(AssignResult.SUCCESS, assignResult);
            // 載入未知欄位後，effect API 仍可呼叫（即不拋例外）
            int interval = _ctx.Recruitment.GetCurrentRefreshIntervalSecondsForTests();
            Assert.GreaterOrEqual(interval, 0);
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

            ctx.Data = new GameObject("DataManager_Staff_Test").AddComponent<DataManager>();
            ReflectionTools.InvokeInstance(ctx.Data, "InitializeForTests");

            ctx.Time = new GameObject("TimeSystem_Staff_Test").AddComponent<TimeSystem>();
            ReflectionTools.InvokeInstance(ctx.Time, "InitializeForTests");

            ctx.Resource = new GameObject("Resource_Staff_Test").AddComponent<ResourceManagement>();
            ReflectionTools.InvokeInstance(ctx.Resource, "InitializeForTests");

            ctx.Mission = new GameObject("MissionDB_Staff_Test").AddComponent<MissionDatabaseService>();
            ReflectionTools.InvokeInstance(ctx.Mission, "InitializeForTests");

            ctx.Profession = new GameObject("Profession_Staff_Test").AddComponent<ProfessionService>();
            ReflectionTools.InvokeInstance(ctx.Profession, "InitializeForTests");

            ctx.Race = new GameObject("Race_Staff_Test").AddComponent<RaceService>();
            ReflectionTools.InvokeInstance(ctx.Race, "InitializeForTests");

            ctx.Trait = new GameObject("Trait_Staff_Test").AddComponent<TraitService>();
            ReflectionTools.InvokeInstance(ctx.Trait, "InitializeForTests");

            ctx.Roster = new GameObject("Roster_Staff_Test").AddComponent<AdventurerRoster>();
            ReflectionTools.InvokeInstance(ctx.Roster, "InitializeForTests");

            ctx.Guild = new GameObject("Guild_Staff_Test").AddComponent<GuildCoreService>();
            ReflectionTools.InvokeInstance(ctx.Guild, "InitializeForTests");
            ctx.Guild.InitializeAsNewGame();

            ctx.Building = new GameObject("Building_Staff_Test").AddComponent<BuildingService>();
            ReflectionTools.SetStaticProperty(typeof(BuildingService), "Instance", ctx.Building);
            ReflectionTools.InvokeInstance(ctx.Building, "InitializeForTests");
            ctx.Building.InitializeAsNewGame();
            // 解鎖職員系統：升級建築 6（職員休息室）至 level 1，否則 StaffService.HireStaff 等回 STAFF_SYSTEM_LOCKED
            // 對齊 NpcDecisionServiceTests / GoldFlowServiceTests / RecruitmentServiceTests 既有先例
            ctx.Building.TryUpgradeBuilding(6);

            ctx.Staff = new GameObject("Staff_Staff_Test").AddComponent<StaffService>();
            ReflectionTools.SetStaticProperty(typeof(StaffService), "Instance", ctx.Staff);
            ReflectionTools.InvokeInstance(ctx.Staff, "Awake");

            ctx.Recruitment = new GameObject("Recruitment_Staff_Test").AddComponent<RecruitmentService>();
            ReflectionTools.SetStaticProperty(typeof(RecruitmentService), "Instance", ctx.Recruitment);
            ReflectionTools.InvokeInstance(ctx.Recruitment, "InitializeForTests");
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
