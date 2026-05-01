using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Mission
{
    public sealed class MissionDatabaseTests
    {
        private Dictionary<string, string> _baseTables;

        [SetUp]
        public void SetUp()
        {
            ResetAll();
            _baseTables = LoadBaseTables();
        }

        [TearDown]
        public void TearDown()
        {
            ResetAll();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissionDatabaseLoader_LoadsAllTables_NoError()
        {
            MissionDatabaseService service = CreateService();
            Assert.IsNotNull(service);
            Assert.AreEqual(4, service.GetAllMissionTypes().Count);
            Assert.Greater(service.GetRegularTemplates("D").Count, 0);
        }

        [Test]
        public void GetRegularTemplates_D_ReturnsOnlyRegularD()
        {
            MissionDatabaseService service = CreateService();
            IReadOnlyList<MissionTemplate> rows = service.GetRegularTemplates("D");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(0, rows[0].categoryID);
            Assert.AreEqual(0, rows[1].categoryID);
            Assert.AreEqual("D", rows[0].difficulty);
            Assert.AreEqual("D", rows[1].difficulty);
        }

        [Test]
        public void EscortConstraint_FDifficulty_LogsErrorAndSkips()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("difficulty,D,D,C,SSS,A,B", "difficulty,D,F,C,SSS,A,B") }
            };

            LogAssert.Expect(LogType.Error, new Regex("護送任務但 difficulty=F 不在 D/C/B/A"));
            MissionDatabaseService service = CreateService(overrides);
            IReadOnlyList<MissionTemplate> escortRows = service.GetRegularTemplates("F", 2);
            Assert.AreEqual(0, escortRows.Count);
        }

        [Test]
        public void UnknownTypeID_LogsErrorAndSkips()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("typeID,1,2,3,1,2,4", "typeID,1,99,3,1,2,4") }
            };

            LogAssert.Expect(LogType.Error, new Regex("typeID=99 無對應 MissionTypeTable"));
            MissionDatabaseService service = CreateService(overrides);
            MissionTemplate mission = service.GetTemplate(1002);
            Assert.IsNull(mission);
        }

        [Test]
        public void UnknownFactionID_FallsBackToNeutral()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("factionID,0,1,0,0,2,0", "factionID,0,99,0,0,2,0") }
            };

            LogAssert.Expect(LogType.Warning, new Regex("factionID=99 不存在，已回退為 FACTION_NEUTRAL_ID=0"));
            MissionDatabaseService service = CreateService(overrides, id => id == 1 || id == 2);
            MissionTemplate mission = service.GetTemplate(1002);

            Assert.IsNotNull(mission);
            Assert.AreEqual(0, mission.factionID);
        }

        [Test]
        public void DifficultyTable_AllValues_MatchGDDDefaults()
        {
            MissionDatabaseService service = CreateService();
            Assert.AreEqual(3000, service.GetBaseReward("SSS"));
            Assert.AreEqual(480, service.GetBaseDuration("B"));
            Assert.AreEqual(0.02f, service.GetBaseDeathRate("F"), 0.0001f);
            Assert.AreEqual(30, service.GetFactionScoreDelta("SSS"));

            ResetAll();
            _baseTables = LoadBaseTables();
            LogAssert.Expect(LogType.Error, new Regex("MissionDifficultyTable 缺少難度：B"));
            MissionDatabaseService missingBService = CreateService(
                new Dictionary<string, string> { { "MissionDifficultyTable", _baseTables["MissionDifficultyTable_MissingB"] } });

            Assert.AreEqual(0, missingBService.GetBaseReward("B"));
            Assert.AreEqual(0, missingBService.GetBaseDuration("B"));
            Assert.AreEqual(0f, missingBService.GetBaseDeathRate("B"));
            Assert.AreEqual(0, missingBService.GetFactionScoreDelta("B"));
        }

        [Test]
        public void GetEscortDuration_D_InRangeAndRandom()
        {
            MissionDatabaseService service = CreateService();
            HashSet<int> unique = new HashSet<int>();

            for (int i = 0; i < 100; i++)
            {
                int value = service.GetEscortDuration("D");
                Assert.That(value, Is.InRange(90, 150));
                unique.Add(value);
            }

            Assert.Greater(unique.Count, 1);
        }

        [Test]
        public void GetMissionText_NormalAndFallback()
        {
            MissionDatabaseService service = CreateService();
            (string name, string desc) normal = service.GetMissionText("D", 1);
            Assert.IsFalse(string.IsNullOrWhiteSpace(normal.name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(normal.desc));
            Assert.AreNotEqual("未知委託", normal.name);

            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionNamePool", _baseTables["MissionNamePool"].Replace("difficulty,D,D,C,B", "difficulty,F,D,C,B") }
            };

            ResetAll();
            _baseTables = LoadBaseTables();
            LogAssert.Expect(LogType.Warning, new Regex("GetMissionText 查無資料 difficulty=D, typeID=1"));
            MissionDatabaseService fallbackService = CreateService(overrides);
            (string name, string desc) fallback = fallbackService.GetMissionText("D", 1);

            Assert.AreEqual("未知委託", fallback.name);
            Assert.AreEqual("（無描述）", fallback.desc);
        }

        [Test]
        public void IsValidCombination_EscortRules()
        {
            MissionDatabaseService service = CreateService();
            Assert.IsFalse(service.IsValidCombination("F", 2));
            Assert.IsTrue(service.IsValidCombination("D", 2));
            Assert.IsTrue(service.IsValidCombination("SSS", 1));
        }

        [Test]
        public void GetTemplatesByCategory_And_TypeName()
        {
            MissionDatabaseService service = CreateService();
            IReadOnlyList<MissionTemplate> categoryRows = service.GetTemplatesByCategory(3);

            Assert.AreEqual(1, categoryRows.Count);
            Assert.AreEqual(1005, categoryRows[0].missionID);
            Assert.AreEqual("討伐", service.GetTypeName(1));
            Assert.AreEqual("faction_story", service.GetCategoryName(3));

            LogAssert.Expect(LogType.Warning, new Regex("找不到 typeID=99 的名稱"));
            Assert.IsNull(service.GetTypeName(99));
        }

        [Test]
        public void Test_v31_AC_MD_17_isScriptedDeath_InvalidCategoryID_LogsErrorAndResets()
        {
            // AC-MD-17：CSV 中 isScriptedDeath=1, categoryID=0（非 3）→ LogError，欄位重置為 0
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("isScriptedDeath,0,0,0,0,0,0", "isScriptedDeath,0,1,0,0,0,0") }
            };

            LogAssert.Expect(LogType.Error, new Regex("isScriptedDeath.*categoryID.*重置為 0|isScriptedDeath.*必須.*categoryID=3"));
            MissionDatabaseService service = CreateService(overrides);

            MissionTemplate invalidTemplate = service.GetTemplate(1002);
            Assert.IsNotNull(invalidTemplate);
            Assert.AreEqual(0, invalidTemplate.isScriptedDeath);
            Assert.AreEqual(0, invalidTemplate.categoryID);
        }

        [Test]
        public void Test_v31_AC_MD_18_minDangerLevel_OutOfRange_LogsErrorAndResets()
        {
            // AC-MD-18：CSV 中 minDangerLevel=5（範圍外 [0,4]）→ LogError，重置為 0
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("minDangerLevel,0,0,0,0,0,0", "minDangerLevel,0,5,0,0,0,0") }
            };

            LogAssert.Expect(LogType.Error, new Regex("minDangerLevel.*範圍.*重置為 0|minDangerLevel.*\\[0,4\\]"));
            MissionDatabaseService service = CreateService(overrides);

            MissionTemplate invalidTemplate = service.GetTemplate(1002);
            Assert.IsNotNull(invalidTemplate);
            Assert.AreEqual(0, invalidTemplate.minDangerLevel);
        }

        [Test]
        public void Test_v31_AC_MD_19_requiredTraitID_UnknownID_LogsErrorAndResets()
        {
            // AC-MD-19：CSV 中 requiredTraitID=9999（不存在於 TraitTable）→ LogError，重置為 0
            // traitTableValidator 回傳 false（模擬 TraitTable 中找不到 9999）
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "MissionTemplate", _baseTables["MissionTemplate"].Replace("requiredTraitID,0,0,0,0,0,0", "requiredTraitID,0,9999,0,0,0,0") }
            };

            LogAssert.Expect(LogType.Error, new Regex("requiredTraitID.*TraitTable|requiredTraitID.*不存在"));
            MissionDatabaseService service = CreateService(overrides, traitTableValidator: id => false);

            MissionTemplate invalidTemplate = service.GetTemplate(1002);
            Assert.IsNotNull(invalidTemplate);
            Assert.AreEqual(0, invalidTemplate.requiredTraitID);
        }

        private MissionDatabaseService CreateService(
            Dictionary<string, string> tableOverrides = null,
            Func<int, bool> factionRouteValidator = null,
            Func<int, bool> traitTableValidator = null)
        {
            Dictionary<string, string> tableMap = new Dictionary<string, string>(_baseTables, StringComparer.Ordinal);
            if (tableOverrides != null)
            {
                foreach (KeyValuePair<string, string> pair in tableOverrides)
                {
                    tableMap[pair.Key] = pair.Value;
                }
            }

            ReflectionHelper.InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(tableName => tableMap.TryGetValue(tableName, out string csv) ? csv : null));

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterTable<MissionTemplate>("MissionTemplate");
            DataManager.RegisterTable<MissionDifficultyData>("MissionDifficultyTable");
            DataManager.RegisterTable<MissionTypeData>("MissionTypeTable");
            DataManager.RegisterTable<MissionCategoryData>("MissionCategoryTable");
            DataManager.RegisterTable<MissionNameData>("MissionNamePool");

            GameObject dmGo = new GameObject("DataManager_C01_Test");
            DataManager dm = dmGo.AddComponent<DataManager>();
            ReflectionHelper.InvokeInstance(dm, "InitializeForTests");

            MissionDatabaseService.SetFactionRouteValidatorForTests(factionRouteValidator);
            MissionDatabaseService.SetTraitTableValidatorForTests(traitTableValidator); // === v3.1 patch P3.1-001 ===
            GameObject serviceGo = new GameObject("MissionDatabaseService_C01_Test");
            MissionDatabaseService service = serviceGo.AddComponent<MissionDatabaseService>();
            service.InitializeForTests();
            return service;
        }

        private void ResetAll()
        {
            MissionDatabaseService.ResetForTests();
            ReflectionHelper.InvokeStatic(typeof(DataManager), "ResetForTests");
        }

        private static Dictionary<string, string> LoadBaseTables()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "SystemConstants", LoadTestCsv("SystemConstants.csv") },
                { "MissionTemplate", LoadTestCsv("MissionTemplate.csv") },
                { "MissionDifficultyTable", LoadTestCsv("MissionDifficultyTable.csv") },
                { "MissionDifficultyTable_MissingB", LoadTestCsv("MissionDifficultyTable_MissingB.csv") },
                { "MissionTypeTable", LoadTestCsv("MissionTypeTable.csv") },
                { "MissionCategoryTable", LoadTestCsv("MissionCategoryTable.csv") },
                { "MissionNamePool", LoadTestCsv("MissionNamePool.csv") }
            };
        }

        private static string LoadTestCsv(string fileName)
        {
            string root = Path.Combine(
                Application.dataPath,
                "Tests",
                "EditMode",
                "Gameplay",
                "Mission",
                "TestResources");
            string path = Path.Combine(root, fileName);
            return File.ReadAllText(path, Encoding.UTF8);
        }
    }

    internal static class ReflectionHelper
    {
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, StaticNonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            method.Invoke(null, args);
        }

        public static void InvokeStatic(Type type, string methodName, Type[] paramTypes, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, StaticNonPublic, null, paramTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            method.Invoke(null, args);
        }

        public static void InvokeInstance(object instance, string methodName, params object[] args)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, InstanceNonPublic);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            method.Invoke(instance, args);
        }
    }
}
