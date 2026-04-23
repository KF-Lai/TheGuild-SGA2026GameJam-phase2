using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Core.Data
{
    public sealed class DataManagerTests
    {
        private const string TestTableCsv =
            "id,name,cost,weight,tags,isUnique\n" +
            "warrior,勇者,100,0.5,melee|physical,true\n" +
            "mage,法師,150,0.3,ranged|magical,false\n" +
            "# 這是註解\n" +
            "rogue,盜賊,120,0.2,\"melee|stealth,unique\",1\n";

        private const string TestMissionCsv =
            "id,title\n" +
            "123,測試任務\n";

        private const string TestSystemConstantsCsv =
            "key,value,description\n" +
            "TEST_RATE,0.2,測試用比例\n" +
            "TEST_COUNT,5,測試用整數\n";

        private const string GroupPoolUniformCsv =
            "groupID,groupName,memberIDs,pickCount,pickMode,weights\n" +
            "warrior_traits,戰士池,warrior|mage|rogue,2,uniform,1|1|1\n";

        private const string GroupPoolWeightedCsv =
            "groupID,groupName,memberIDs,pickCount,pickMode,weights\n" +
            "weighted_traits,權重池,warrior|mage,1,weighted,7|3\n";

        private const string GroupPoolInsufficientCsv =
            "groupID,groupName,memberIDs,pickCount,pickMode,weights\n" +
            "small_pool,小池,warrior|mage|rogue,5,uniform,1|1|1\n";

        [SetUp]
        public void SetUp()
        {
            DataManager.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DataManager.ResetForTests();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AC_DM_01_LoadAllTables_NoError()
        {
            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestTable", TestTableCsv },
                    { "TestSystemConstants", TestSystemConstantsCsv }
                },
                () =>
                {
                    DataManager.RegisterTable<TestTableData>("TestTable");
                    DataManager.RegisterSystemConstantsTable("TestSystemConstants");
                });

            Assert.IsNotNull(manager);
            Assert.AreEqual(3, manager.GetAll<TestTableData>().Count);
        }

        [Test]
        public void AC_DM_02_MissingTable_LogError_ButOtherTablesWork()
        {
            LogAssert.Expect(LogType.Error, new Regex("表格 MissingSystemConstants 未找到"));

            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestTable", TestTableCsv }
                },
                () =>
                {
                    DataManager.RegisterTable<TestTableData>("TestTable");
                    DataManager.RegisterSystemConstantsTable("MissingSystemConstants");
                });

            Assert.IsNotNull(manager);
            Assert.IsNotNull(manager.Get<TestTableData>("warrior"));
        }

        [Test]
        public void AC_DM_03_GetMissingId_ReturnsNullAndWarning()
        {
            DataManager manager = CreateTableOnlyManager();

            LogAssert.Expect(LogType.Warning, new Regex("找不到資料：ID=不存在"));
            TestTableData result = manager.Get<TestTableData>("不存在");

            Assert.IsNull(result);
        }

        [Test]
        public void AC_DM_04_GetAll_CountMatchesDataRows()
        {
            DataManager manager = CreateTableOnlyManager();
            IReadOnlyList<TestTableData> all = manager.GetAll<TestTableData>();

            Assert.AreEqual(3, all.Count);
        }

        [Test]
        public void AC_DM_05_GetFloat_ReturnsExpectedValue()
        {
            DataManager manager = CreateConstantsOnlyManager();

            float value = manager.GetFloat("TEST_RATE");

            Assert.AreEqual(0.2f, value, 0.0001f);
        }

        [Test]
        public void AC_DM_06_GetFloat_UnknownKey_ReturnsZeroAndError()
        {
            DataManager manager = CreateConstantsOnlyManager();

            LogAssert.Expect(LogType.Error, new Regex("SystemConstants 查無 key：UNKNOWN_KEY"));
            float value = manager.GetFloat("UNKNOWN_KEY");

            Assert.AreEqual(0f, value);
        }

        [Test]
        public void AC_DM_07_GetWhere_FilterWorks()
        {
            DataManager manager = CreateTableOnlyManager();

            IReadOnlyList<TestTableData> filtered = manager.GetWhere<TestTableData>(x => x.cost > 100);

            Assert.AreEqual(2, filtered.Count);
            Assert.AreEqual("mage", filtered[0].id);
            Assert.AreEqual("rogue", filtered[1].id);
        }

        [Test]
        public void AC_DM_08_PickRandom_NoDuplicate()
        {
            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestTable", TestTableCsv },
                    { "TestGroupPool", GroupPoolUniformCsv }
                },
                () =>
                {
                    DataManager.RegisterTable<TestTableData>("TestTable");
                    DataManager.RegisterGroupPoolTable<GroupPoolData>("TestGroupPool");
                });

            List<TestTableData> picked = manager.PickRandom<TestTableData>("warrior_traits");

            Assert.AreEqual(2, picked.Count);
            Assert.AreNotEqual(picked[0].id, picked[1].id);
        }

        [Test]
        public void AC_DM_09_PickRandom_WeightedDistributionWithinTolerance()
        {
            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestTable", TestTableCsv },
                    { "TestGroupPool", GroupPoolWeightedCsv }
                },
                () =>
                {
                    DataManager.RegisterTable<TestTableData>("TestTable");
                    DataManager.RegisterGroupPoolTable<GroupPoolData>("TestGroupPool");
                });

            manager.SetRandomForTests(new System.Random(12345));

            int warriorCount = 0;
            int mageCount = 0;

            for (int i = 0; i < 100; i++)
            {
                List<TestTableData> picked = manager.PickRandom<TestTableData>("weighted_traits");
                Assert.AreEqual(1, picked.Count);

                if (picked[0].id == "warrior")
                {
                    warriorCount++;
                }
                else if (picked[0].id == "mage")
                {
                    mageCount++;
                }
            }

            Assert.That(warriorCount, Is.InRange(60, 80));
            Assert.That(mageCount, Is.InRange(20, 40));
        }

        [Test]
        public void AC_DM_10_PickRandom_PoolSmallerThanPickCount_ReturnAll()
        {
            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestTable", TestTableCsv },
                    { "TestGroupPool", GroupPoolInsufficientCsv }
                },
                () =>
                {
                    DataManager.RegisterTable<TestTableData>("TestTable");
                    DataManager.RegisterGroupPoolTable<GroupPoolData>("TestGroupPool");
                });

            List<TestTableData> picked = manager.PickRandom<TestTableData>("small_pool");

            Assert.AreEqual(3, picked.Count);
        }

        [UnityTest]
        public IEnumerator AC_DM_11_DuplicateDataManager_DestroySecond()
        {
            DataManager.SetTableTextProviderForTests(_ => null);

            GameObject go1 = new GameObject("DataManager_1");
            go1.AddComponent<DataManager>();

            GameObject go2 = new GameObject("DataManager_2");
            go2.AddComponent<DataManager>();

            yield return null;

            DataManager[] managers = UnityEngine.Object.FindObjectsOfType<DataManager>();
            Assert.AreEqual(1, managers.Length);
        }

        [Test]
        public void AC_DM_12_GetIntOverload_UsesStringPath()
        {
            DataManager manager = CreateManager(
                new Dictionary<string, string>
                {
                    { "TestMissionTable", TestMissionCsv }
                },
                () => { DataManager.RegisterTable<TestMissionData>("TestMissionTable"); });

            TestMissionData fromInt = manager.Get<TestMissionData>(123);
            TestMissionData fromString = manager.Get<TestMissionData>("123");

            Assert.AreSame(fromString, fromInt);
        }

        [Test]
        public void AC_DM_17_UnregisteredTypeQuery_ReturnsNullAndError()
        {
            DataManager manager = CreateTableOnlyManager();

            LogAssert.Expect(LogType.Error, new Regex("型別 UnregisteredType 未註冊"));
            UnregisteredType result = manager.Get<UnregisteredType>("x");

            Assert.IsNull(result);
        }

        [Test]
        public void AC_DM_18_RegisterAfterLoad_FailsAndNotLoaded()
        {
            DataManager manager = CreateTableOnlyManager();

            LogAssert.Expect(LogType.Error, new Regex("DataManager 已載入，表格 LateTable 註冊失敗"));
            DataManager.RegisterTable<LateTableData>("LateTable");

            LogAssert.Expect(LogType.Error, new Regex("型別 LateTableData 未註冊"));
            LateTableData result = manager.Get<LateTableData>("late");

            Assert.IsNull(result);
        }

        [Test]
        public void AC_DM_19_DuplicateRegistration_WarnAndUseLaterType()
        {
            DataManager.SetTableTextProviderForTests(name =>
            {
                if (name == "DuplicateTable")
                {
                    return "id,title\nrow1,替換成功\n";
                }

                return null;
            });

            DataManager.RegisterTable<TestTableData>("DuplicateTable");
            LogAssert.Expect(LogType.Warning, new Regex("表格 DuplicateTable 已由 TestTableData 註冊，後續註冊以 ReplacementTableData 覆蓋"));
            DataManager.RegisterTable<ReplacementTableData>("DuplicateTable");

            GameObject go = new GameObject("DataManager_DuplicateRegister");
            DataManager manager = go.AddComponent<DataManager>();

            ReplacementTableData row = manager.Get<ReplacementTableData>("row1");
            Assert.IsNotNull(row);
            Assert.AreEqual("替換成功", row.title);
        }

        [Test]
        public void AC_DM_20_RegisterSystemConstantsTable_GetFloatWorks()
        {
            DataManager manager = CreateConstantsOnlyManager();

            float value = manager.GetFloat("TEST_RATE");

            Assert.AreEqual(0.2f, value, 0.0001f);
        }

        private static DataManager CreateTableOnlyManager()
        {
            return CreateManager(
                new Dictionary<string, string> { { "TestTable", TestTableCsv } },
                () => { DataManager.RegisterTable<TestTableData>("TestTable"); });
        }

        private static DataManager CreateConstantsOnlyManager()
        {
            return CreateManager(
                new Dictionary<string, string> { { "TestSystemConstants", TestSystemConstantsCsv } },
                () => { DataManager.RegisterSystemConstantsTable("TestSystemConstants"); });
        }

        private static DataManager CreateManager(Dictionary<string, string> tableMap, Action registerAction)
        {
            DataManager.SetTableTextProviderForTests(name =>
            {
                return tableMap.TryGetValue(name, out string csv) ? csv : null;
            });

            registerAction?.Invoke();

            GameObject go = new GameObject("DataManager_Test");
            return go.AddComponent<DataManager>();
        }

        [Serializable]
        private sealed class TestTableData
        {
            public string id;
            public string name;
            public int cost;
            public float weight;
            public string[] tags;
            public bool isUnique;
        }

        [Serializable]
        private sealed class TestMissionData
        {
            public string id;
            public string title;
        }

        [Serializable]
        private sealed class ReplacementTableData
        {
            public string id;
            public string title;
        }

        [Serializable]
        private sealed class UnregisteredType
        {
            public string id;
        }

        [Serializable]
        private sealed class LateTableData
        {
            public string id;
        }
    }
}
