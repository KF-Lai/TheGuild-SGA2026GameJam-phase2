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

namespace Tests.EditMode.Gameplay.Adventurer
{
    public sealed class AdventurerManagementTests
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
            _ctx.Roster.InitializeAsNewGame();
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
        public void Test_v31_AC_AM_20_GetRoster_OpheliaFirst()
        {
            // AC-AM-20: GetRoster 排序奧菲莉雅永遠第一
            AdventurerInstance ophelia = null;
            for (int i = 0; i < 5; i++)
            {
                AdventurerInstance dummy = _ctx.Roster.GetFactory().CreateRandomInstance("F", 1, 1, Array.Empty<int>());
                Assert.IsTrue(_ctx.Roster.AddAdventurer(dummy));
            }

            // 檢查名冊中是否有 templateID=1（奧菲莉雅）
            IReadOnlyList<AdventurerInstance> roster = _ctx.Roster.GetRoster();
            if (roster.Count > 0 && roster[0].templateID == 1)
            {
                ophelia = roster[0];
            }
            else
            {
                // 手動建立奧菲莉雅（templateID=1，isUnique=1）
                ophelia = _ctx.Roster.GetFactory().CreateFromTemplate(1);
                if (ophelia != null)
                {
                    _ctx.Roster.AddAdventurer(ophelia);
                }
            }

            // 驗證排序：奧菲莉雅在首位
            roster = _ctx.Roster.GetRoster();
            Assert.Greater(roster.Count, 0);
            if (ophelia != null && ophelia.templateID == 1)
            {
                Assert.AreEqual(ophelia.adventurerID, roster[0].adventurerID, "Ophelia should be first in roster");
            }
        }

        [Test]
        public void Test_v31_AC_AM_21_DismissAdventurer_IdleAllowed()
        {
            // AC-AM-21: DismissAdventurer 對 Idle 冒險者放寬規則
            AdventurerInstance dummy = _ctx.Roster.GetFactory().CreateRandomInstance("F", 1, 1, Array.Empty<int>());
            Assert.IsTrue(_ctx.Roster.AddAdventurer(dummy));

            // 設定冒險者為 Idle（假設無進行中的任務）
            // 驗證可以解僱
            bool ok = _ctx.Roster.DismissAdventurer(dummy.adventurerID);
            Assert.IsTrue(ok, "Should allow dismiss on Idle adventurer");
        }

        [Test]
        public void Test_v31_R2_SetWounded_BasicCall()
        {
            // R2 SetWounded 擴充：既有呼叫（使用預設 customDurationHours）
            AdventurerInstance dummy = _ctx.Roster.GetFactory().CreateRandomInstance("F", 1, 1, Array.Empty<int>());
            Assert.IsTrue(_ctx.Roster.AddAdventurer(dummy));

            // 呼叫 SetWounded 不帶 customDurationHours
            _ctx.Roster.SetWounded(dummy.adventurerID);

            // 驗證狀態變更為受傷
            AdventurerInstance updated = _ctx.Roster.GetAdventurerByID(dummy.adventurerID);
            Assert.IsNotNull(updated);
            Assert.AreEqual("WOUNDED", updated.GetCurrentStatus());
        }

        [Test]
        public void Test_v31_R2_SetWounded_WithCustomDuration()
        {
            // R2 SetWounded 擴充：customDurationHours 變體
            AdventurerInstance dummy = _ctx.Roster.GetFactory().CreateRandomInstance("F", 1, 1, Array.Empty<int>());
            Assert.IsTrue(_ctx.Roster.AddAdventurer(dummy));

            // 呼叫 SetWounded 帶自訂持續時間（例如 5 小時）
            _ctx.Roster.SetWounded(dummy.adventurerID, 5);

            // 驗證狀態變更為受傷
            AdventurerInstance updated = _ctx.Roster.GetAdventurerByID(dummy.adventurerID);
            Assert.IsNotNull(updated);
            Assert.AreEqual("WOUNDED", updated.GetCurrentStatus());
        }

        [Test]
        public void Test_v31_RegisterUniqueAdventurer_Success()
        {
            // RegisterUniqueAdventurer 成功路徑（templateID=1 不在名冊中）
            bool ok = _ctx.Roster.RegisterUniqueAdventurer(1);
            Assert.IsTrue(ok, "Should successfully register unique adventurer template 1");

            // 驗證名冊中存在此冒險者
            IReadOnlyList<AdventurerInstance> roster = _ctx.Roster.GetRoster();
            bool found = false;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].templateID == 1)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "Template 1 should be in roster");
        }

        [Test]
        public void Test_v31_RegisterUniqueAdventurer_AlreadyExists()
        {
            // RegisterUniqueAdventurer 已存在路徑
            // 先註冊一次
            bool first = _ctx.Roster.RegisterUniqueAdventurer(1);
            Assert.IsTrue(first);

            // 再試一次，應該返回 false
            bool second = _ctx.Roster.RegisterUniqueAdventurer(1);
            Assert.IsFalse(second, "Should not register duplicate unique adventurer");
        }

        [Test]
        public void Test_v31_RegisterUniqueAdventurer_NonUnique()
        {
            // RegisterUniqueAdventurer isUnique=0 情境
            // templateID=3 是 E 階非 unique 模板（見 BuildTableMap）
            bool ok = _ctx.Roster.RegisterUniqueAdventurer(3);
            // 預期：若 isUnique=0 應拒絕（返回 false）
            // 或當作普通招募失敗（端視實作）
            Assert.IsFalse(ok, "Should not register non-unique adventurer as unique");
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

            ctx.Data = new GameObject("DataManager_AM_Test").AddComponent<DataManager>();
            ReflectionTools.InvokeInstance(ctx.Data, "InitializeForTests");

            ctx.Time = new GameObject("TimeSystem_AM_Test").AddComponent<TimeSystem>();
            ReflectionTools.InvokeInstance(ctx.Time, "InitializeForTests");

            ctx.Resource = new GameObject("Resource_AM_Test").AddComponent<ResourceManagement>();
            ReflectionTools.InvokeInstance(ctx.Resource, "InitializeForTests");

            ctx.Mission = new GameObject("MissionDB_AM_Test").AddComponent<MissionDatabaseService>();
            ReflectionTools.InvokeInstance(ctx.Mission, "InitializeForTests");

            ctx.Profession = new GameObject("Profession_AM_Test").AddComponent<ProfessionService>();
            ReflectionTools.InvokeInstance(ctx.Profession, "InitializeForTests");

            ctx.Race = new GameObject("Race_AM_Test").AddComponent<RaceService>();
            ReflectionTools.InvokeInstance(ctx.Race, "InitializeForTests");

            ctx.Trait = new GameObject("Trait_AM_Test").AddComponent<TraitService>();
            ReflectionTools.InvokeInstance(ctx.Trait, "InitializeForTests");

            ctx.Roster = new GameObject("Roster_AM_Test").AddComponent<AdventurerRoster>();
            ReflectionTools.InvokeInstance(ctx.Roster, "InitializeForTests");

            ctx.Guild = new GameObject("Guild_AM_Test").AddComponent<GuildCoreService>();
            ReflectionTools.InvokeInstance(ctx.Guild, "InitializeForTests");
            ctx.Guild.InitializeAsNewGame();

            ctx.Building = new GameObject("Building_AM_Test").AddComponent<BuildingService>();
            ReflectionTools.SetStaticProperty(typeof(BuildingService), "Instance", ctx.Building);
            ReflectionTools.InvokeInstance(ctx.Building, "InitializeForTests");
            ctx.Building.InitializeAsNewGame();

            ctx.Staff = new GameObject("Staff_AM_Test").AddComponent<StaffService>();
            ReflectionTools.SetStaticProperty(typeof(StaffService), "Instance", ctx.Staff);
            ReflectionTools.InvokeInstance(ctx.Staff, "Awake");

            ctx.Recruitment = new GameObject("Recruitment_AM_Test").AddComponent<RecruitmentService>();
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
