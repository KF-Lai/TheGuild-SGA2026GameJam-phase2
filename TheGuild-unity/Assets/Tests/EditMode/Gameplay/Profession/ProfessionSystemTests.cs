using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Profession;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Profession
{
    public sealed class ProfessionSystemTests
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
        public void DoD01_DoD02_GetAllAndBaseProfessions()
        {
            ProfessionService service = CreateService();

            IReadOnlyList<ProfessionData> all = service.GetAllProfessions();
            IReadOnlyList<ProfessionData> baseRows = service.GetBaseProfessions();

            Assert.AreEqual(7, all.Count);
            Assert.AreEqual(7, baseRows.Count);
            for (int i = 0; i < baseRows.Count; i++)
            {
                Assert.AreEqual(1, baseRows[i].Tier);
            }
        }

        [Test]
        public void DoD03_DoD04_DoD05_StrongWeakMatrix()
        {
            ProfessionService service = CreateService();

            Assert.IsTrue(service.IsStrongType(1, 1));
            Assert.IsTrue(service.IsWeakType(1, 4));
            Assert.IsTrue(service.IsStrongType(4, 2));
            Assert.IsTrue(service.IsStrongType(4, 4));

            Assert.IsFalse(service.IsStrongType(7, 1));
            Assert.IsFalse(service.IsStrongType(7, 2));
            Assert.IsFalse(service.IsStrongType(7, 3));
            Assert.IsFalse(service.IsStrongType(7, 4));
            Assert.IsFalse(service.IsWeakType(7, 1));
            Assert.IsFalse(service.IsWeakType(7, 2));
            Assert.IsFalse(service.IsWeakType(7, 3));
            Assert.IsFalse(service.IsWeakType(7, 4));
        }

        [Test]
        public void DoD06_OverlapStrongWeak_LogsErrorAndSkips()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                {
                    "ProfessionTable",
                    _baseTables["ProfessionTable"].Replace(
                        "weakTypeIDs,4,1,2,3,4,1,0",
                        "weakTypeIDs,1|4,1,2,3,4,1,0")
                }
            };

            LogAssert.Expect(LogType.Error, new Regex("overlap", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(overrides);

            Assert.IsNull(service.GetProfession(1));
            Assert.AreEqual(6, service.GetAllProfessions().Count);
        }

        [Test]
        public void DoD07_UnknownTypeID_LogsWarningAndFiltersOut()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                {
                    "ProfessionTable",
                    _baseTables["ProfessionTable"].Replace(
                        "strongTypeIDs,1|2,3,4,2|4,1|3,2,0",
                        "strongTypeIDs,1|99,3,4,2|4,1|3,2,0")
                }
            };

            LogAssert.Expect(LogType.Warning, new Regex("unknown typeID=99", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(overrides);

            Assert.IsTrue(service.IsStrongType(1, 1));
            Assert.IsFalse(service.IsStrongType(1, 99));
        }

        [Test]
        public void DoD08_GetProfessionZeroWarns_UnknownSilent()
        {
            ProfessionService service = CreateService();

            LogAssert.Expect(LogType.Warning, new Regex("null sentinel", RegexOptions.IgnoreCase));
            Assert.IsNull(service.GetProfession(0));
            Assert.IsNull(service.GetProfession(999));
        }

        [Test]
        public void DoD09_BaseDatasetHasNoUpgradePath()
        {
            ProfessionService service = CreateService();
            Assert.AreEqual(0, service.GetUpgradePaths(1).Count);
        }

        [Test]
        public void DoD09b_Tier2UpgradePathAndReverseLookup()
        {
            string tier2Table = AppendColumn(
                _baseTables["ProfessionTable"],
                new Dictionary<string, string>
                {
                    { "professionID", "8" },
                    { "name", "Knight" },
                    { "description", "Tier2 test row" },
                    { "strongTypeIDs", "1" },
                    { "weakTypeIDs", "3" },
                    { "tier", "2" },
                    { "baseProfessionID", "1" },
                    { "raceIDs", "1|2" },
                    { "raceWeights", "50|50" },
                    { "traitGroupIDs", "1|4" }
                });

            ProfessionService service = CreateService(
                new Dictionary<string, string> { { "ProfessionTable", tier2Table } });

            IReadOnlyList<ProfessionData> upgrades = service.GetUpgradePaths(1);
            Assert.AreEqual(1, upgrades.Count);
            Assert.AreEqual(8, upgrades[0].ProfessionID);

            ProfessionData baseProfession = service.GetBaseProfession(8);
            Assert.IsNotNull(baseProfession);
            Assert.AreEqual(1, baseProfession.ProfessionID);
        }

        [Test]
        public void DoD09c_Tier2MissingBaseProfession_LogsErrorAndSkips()
        {
            string invalidTier2 = AppendColumn(
                _baseTables["ProfessionTable"],
                new Dictionary<string, string>
                {
                    { "professionID", "8" },
                    { "name", "BrokenTier2" },
                    { "description", "Invalid baseProfessionID" },
                    { "strongTypeIDs", "1" },
                    { "weakTypeIDs", "3" },
                    { "tier", "2" },
                    { "baseProfessionID", "999" },
                    { "raceIDs", "1|2" },
                    { "raceWeights", "50|50" },
                    { "traitGroupIDs", "1|4" }
                });

            LogAssert.Expect(LogType.Error, new Regex("baseProfessionID=999", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(
                new Dictionary<string, string> { { "ProfessionTable", invalidTier2 } });

            Assert.IsNull(service.GetProfession(8));
            Assert.AreEqual(0, service.GetUpgradePaths(1).Count);
        }

        [Test]
        public void DoD09d_RaceWeightsLengthMismatch_LogsErrorAndSkips()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                {
                    "ProfessionTable",
                    _baseTables["ProfessionTable"].Replace(
                        "raceWeights,60|40,55|45,50|50,70|30,65|35,40|60,40|30|30",
                        "raceWeights,60,55|45,50|50,70|30,65|35,40|60,40|30|30")
                }
            };

            LogAssert.Expect(LogType.Error, new Regex("raceWeights\\.Length", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(overrides);
            Assert.IsNull(service.GetProfession(1));
        }

        [Test]
        public void RaceWeightsNonPositive_LogsErrorAndSkips()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                {
                    "ProfessionTable",
                    _baseTables["ProfessionTable"].Replace(
                        "raceWeights,60|40,55|45,50|50,70|30,65|35,40|60,40|30|30",
                        "raceWeights,60|40,55|0,50|50,70|30,65|35,40|60,40|30|30")
                }
            };

            LogAssert.Expect(LogType.Error, new Regex("non-positive", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(overrides);
            Assert.IsNull(service.GetProfession(2));
        }

        [Test]
        public void Rule07_MissionServiceNull_SkipsTypeFiltering()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                {
                    "ProfessionTable",
                    _baseTables["ProfessionTable"].Replace(
                        "strongTypeIDs,1|2,3,4,2|4,1|3,2,0",
                        "strongTypeIDs,1|99,3,4,2|4,1|3,2,0")
                }
            };

            LogAssert.Expect(LogType.Error, new Regex("MissionDatabaseService\\.Instance is null", RegexOptions.IgnoreCase));
            ProfessionService service = CreateService(overrides, initializeMissionService: false);

            Assert.IsTrue(service.IsStrongType(1, 99));
        }

        [Test]
        public void Rule09_IsStrongWeakUnknownProfession_WarnsAndReturnsFalse()
        {
            ProfessionService service = CreateService();

            LogAssert.Expect(LogType.Warning, new Regex("unknown professionID=999", RegexOptions.IgnoreCase));
            Assert.IsFalse(service.IsStrongType(999, 1));

            LogAssert.Expect(LogType.Warning, new Regex("unknown professionID=999", RegexOptions.IgnoreCase));
            Assert.IsFalse(service.IsWeakType(999, 1));
        }

        [Test]
        public void DoD10_ServiceDoesNotDependOnStrongWeakConstants()
        {
            ProfessionService service = CreateService();
            Assert.IsNotNull(service.GetProfession(1));
        }

        [Test]
        public void DoD11_ExposedCollectionsAreReadOnlyContracts()
        {
            ProfessionService service = CreateService();
            ProfessionData row = service.GetProfession(1);

            Assert.IsNotNull(row);
            Assert.That(row.StrongTypeIds, Is.AssignableTo<IReadOnlyIntSet>());
            Assert.That(row.WeakTypeIds, Is.AssignableTo<IReadOnlyIntSet>());
            Assert.That(row.RaceIDs, Is.AssignableTo<IReadOnlyList<int>>());
            Assert.That(row.RaceWeights, Is.AssignableTo<IReadOnlyList<int>>());
            Assert.That(row.TraitGroupIDs, Is.AssignableTo<IReadOnlyList<int>>());
            Assert.IsFalse(row.StrongTypeIds is ISet<int>);
            Assert.IsFalse(row.WeakTypeIds is ISet<int>);
            Assert.Throws<NotSupportedException>(() => ((ICollection<int>)row.RaceIDs).Add(99));
            Assert.Throws<NotSupportedException>(() => ((ICollection<int>)row.RaceWeights).Add(99));
            Assert.Throws<NotSupportedException>(() => ((ICollection<int>)row.TraitGroupIDs).Add(99));
        }

        private ProfessionService CreateService(
            Dictionary<string, string> tableOverrides = null,
            bool initializeMissionService = true)
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
            DataManager.RegisterTable<ProfessionData>("ProfessionTable");

            GameObject dataManagerGO = new GameObject("DataManager_C03_Test");
            DataManager dataManager = dataManagerGO.AddComponent<DataManager>();
            ReflectionHelper.InvokeInstance(dataManager, "InitializeForTests");

            if (initializeMissionService)
            {
                GameObject missionGO = new GameObject("MissionDatabaseService_C03_Test");
                MissionDatabaseService missionService = missionGO.AddComponent<MissionDatabaseService>();
                ReflectionHelper.InvokeInstance(missionService, "InitializeForTests");
            }

            GameObject professionGO = new GameObject("ProfessionService_C03_Test");
            ProfessionService professionService = professionGO.AddComponent<ProfessionService>();
            professionService.InitializeForTests();
            return professionService;
        }

        private void ResetAll()
        {
            ProfessionService.ResetForTests();
            ReflectionHelper.InvokeStatic(typeof(MissionDatabaseService), "ResetForTests");
            ReflectionHelper.InvokeStatic(typeof(DataManager), "ResetForTests");
        }

        private static Dictionary<string, string> LoadBaseTables()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "SystemConstants", LoadTestCsv("SystemConstants.csv") },
                { "MissionTemplate", LoadTestCsv("MissionTemplate.csv") },
                { "MissionDifficultyTable", LoadTestCsv("MissionDifficultyTable.csv") },
                { "MissionTypeTable", LoadTestCsv("MissionTypeTable.csv") },
                { "MissionCategoryTable", LoadTestCsv("MissionCategoryTable.csv") },
                { "ProfessionTable", LoadTestCsv("ProfessionTable.csv") }
            };
        }

        private static string LoadTestCsv(string fileName)
        {
            string root = Path.Combine(
                Application.dataPath,
                "Tests",
                "EditMode",
                "Gameplay",
                "Profession",
                "TestResources");
            string path = Path.Combine(root, fileName);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string AppendColumn(string csv, IReadOnlyDictionary<string, string> valuesByRowName)
        {
            StringBuilder sb = new StringBuilder();
            using (StringReader reader = new StringReader(csv))
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    string output = line;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string trimmed = line.TrimStart();
                        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
                        {
                            int commaIndex = line.IndexOf(',');
                            if (commaIndex > 0)
                            {
                                string rowName = line.Substring(0, commaIndex).Trim();
                                if (valuesByRowName.TryGetValue(rowName, out string value))
                                {
                                    output = line + "," + value;
                                }
                            }
                        }
                    }

                    sb.AppendLine(output);
                }
            }

            return sb.ToString();
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
