using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Trait;
using UnityEngine;
using UnityEngine.TestTools;
using Random = UnityEngine.Random;

namespace Tests.EditMode.Gameplay.Trait
{
    public sealed class TraitSystemTests
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
        public void AC_TS_01_GetAllTraits_CountMatchesCsv()
        {
            TraitService service = CreateService();
            Assert.AreEqual(5, service.GetAllTraits().Count);
        }

        [Test]
        public void AC_TS_02_GetTraitsByTypeStat_OnlyStat()
        {
            TraitService service = CreateService();
            IReadOnlyList<TraitData> rows = service.GetTraitsByType("stat");

            Assert.Greater(rows.Count, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual("stat", rows[i].effectType);
            }
        }

        [Test]
        public void AC_TS_03_GetTraitsByTypeCondition_OnlyCondition()
        {
            TraitService service = CreateService();
            IReadOnlyList<TraitData> rows = service.GetTraitsByType("condition");

            Assert.Greater(rows.Count, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual("condition", rows[i].effectType);
            }
        }

        [Test]
        public void AC_TS_04_GetTraitZero_WarnsAndReturnsNull()
        {
            TraitService service = CreateService();
            LogAssert.Expect(LogType.Warning, new Regex("null sentinel", RegexOptions.IgnoreCase));
            Assert.IsNull(service.GetTrait(0));
        }

        [Test]
        public void AC_TS_05_InvalidEffectTarget_LogsErrorAndSkips()
        {
            string invalidTraitTable = ReplaceRowValue(
                _baseTables["TraitTable"],
                "effectTarget",
                1,
                "not_a_valid_target");

            LogAssert.Expect(LogType.Error, new Regex("effectTarget=.*invalid", RegexOptions.IgnoreCase));
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitTable", invalidTraitTable } });

            Assert.AreEqual(4, service.GetAllTraits().Count);
            Assert.IsNull(service.GetTrait(1));
        }

        [Test]
        public void Loader_InvalidEffectType_LogsErrorAndSkips()
        {
            string invalidTraitTable = ReplaceRowValue(
                _baseTables["TraitTable"],
                "effectType",
                1,
                "bad_type");

            LogAssert.Expect(LogType.Error, new Regex("effectType=.*invalid", RegexOptions.IgnoreCase));
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitTable", invalidTraitTable } });

            Assert.AreEqual(4, service.GetAllTraits().Count);
            Assert.IsNull(service.GetTrait(1));
        }

        [Test]
        public void Loader_InvalidTraitIDInGroup_LogsWarningAndFilters()
        {
            string invalidGroupTable = ReplaceRowValue(
                _baseTables["TraitGroupTable"],
                "traitIDs",
                1,
                "1|999");

            LogAssert.Expect(LogType.Warning, new Regex("unknown traitID=999", RegexOptions.IgnoreCase));
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitGroupTable", invalidGroupTable } });

            TraitGroupData group = service.GetTraitGroup(1);
            Assert.IsNotNull(group);
            Assert.AreEqual(1, group.TraitIDs.Count);
            Assert.AreEqual(1, group.TraitIDs[0]);
        }

        [Test]
        public void Loader_InvalidPickMode_LogsErrorAndFallbackUniform()
        {
            string invalidGroupTable = ReplaceRowValue(
                _baseTables["TraitGroupTable"],
                "pickMode",
                1,
                "broken_mode");

            LogAssert.Expect(LogType.Error, new Regex("pickMode=.*invalid", RegexOptions.IgnoreCase));
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitGroupTable", invalidGroupTable } });

            TraitGroupData group = service.GetTraitGroup(1);
            Assert.IsNotNull(group);
            Assert.AreEqual("uniform", group.pickMode);
        }

        [Test]
        public void Loader_WeightedPickMode_LogsWarningAndFallbackUniform()
        {
            string invalidGroupTable = ReplaceRowValue(
                _baseTables["TraitGroupTable"],
                "pickMode",
                1,
                "weighted");

            LogAssert.Expect(LogType.Warning, new Regex("pickMode=weighted", RegexOptions.IgnoreCase));
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitGroupTable", invalidGroupTable } });

            TraitGroupData group = service.GetTraitGroup(1);
            Assert.IsNotNull(group);
            Assert.AreEqual("uniform", group.pickMode);
        }

        [Test]
        public void AC_TS_06_RollTraits_SeededUniformCoverage()
        {
            TraitService service = CreateService();
            TraitGroupData group = service.GetTraitGroup(1);

            Random.InitState(260506);
            HashSet<int> hit = new HashSet<int>();
            for (int i = 0; i < 100; i++)
            {
                int[] result = service.RollTraits(group);
                Assert.AreEqual(1, result.Length);
                hit.Add(result[0]);
            }

            Assert.AreEqual(5, hit.Count);
        }

        [Test]
        public void AC_TS_07_RollTraits_EqualCount_NoWarning()
        {
            string groupTable = ReplaceRowValue(_baseTables["TraitGroupTable"], "traitIDs", 1, "1");
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitGroupTable", groupTable } });

            TraitGroupData group = service.GetTraitGroup(1);
            int[] result = service.RollTraits(group);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0]);
        }

        [Test]
        public void AC_TS_07b_RollTraits_PickCountGreater_WarnsAndReturnsAll()
        {
            string groupTable = ReplaceRowValue(_baseTables["TraitGroupTable"], "traitIDs", 1, "1");
            groupTable = ReplaceRowValue(groupTable, "pickCount", 1, "2");
            TraitService service = CreateService(new Dictionary<string, string> { { "TraitGroupTable", groupTable } });

            TraitGroupData group = service.GetTraitGroup(1);
            LogAssert.Expect(LogType.Warning, new Regex("pickCount=.*pool\\.Count", RegexOptions.IgnoreCase));
            int[] result = service.RollTraits(group);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0]);
        }

        [Test]
        public void AC_TS_08_GetProfessionGroups_Returns_1_3_4()
        {
            TraitService service = CreateService();
            IReadOnlyList<TraitGroupData> groups = service.GetProfessionGroups(1);

            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(1, groups[0].groupID);
            Assert.AreEqual(3, groups[1].groupID);
            Assert.AreEqual(4, groups[2].groupID);
        }

        [Test]
        public void Test_v31_AC_TS_16_SilentTrait_999_EffectValuesCorrect()
        {
            // AC-TS-16：traitID=999 載入後 effectValue == -0.40、effectTarget == "willingness_all"、effectType == "behavior"
            // 新增沉默 trait 到 TraitTable
            string traitTableWith999 = _baseTables["TraitTable"] + "\n999,沉默,她不主動接受委託，需要玩家明確指派,behavior,willingness_all,-0.40";

            TraitService service = CreateService(new Dictionary<string, string> { { "TraitTable", traitTableWith999 } });

            TraitData trait = service.GetTrait(999);
            Assert.IsNotNull(trait);
            Assert.AreEqual(999, trait.traitID);
            Assert.AreEqual("沉默", trait.name);
            Assert.AreEqual("behavior", trait.effectType);
            Assert.AreEqual("willingness_all", trait.effectTarget);
            Assert.AreEqual(-0.40f, trait.effectValue, 0.0001f);
        }

        [Test]
        public void Test_v31_AC_TS_17_SilentTrait_999_NotInGroups()
        {
            // AC-TS-17：traitID=999 不出現在任何 TraitGroupTable.traitIDs 中
            string traitTableWith999 = _baseTables["TraitTable"] + "\n999,沉默,她不主動接受委託，需要玩家明確指派,behavior,willingness_all,-0.40";

            TraitService service = CreateService(new Dictionary<string, string> { { "TraitTable", traitTableWith999 } });

            // 檢查所有 trait group，確保 999 不在任何一個中
            TraitGroupData group1 = service.GetTraitGroup(1);
            TraitGroupData group2 = service.GetTraitGroup(2);
            TraitGroupData group3 = service.GetTraitGroup(3);
            TraitGroupData group4 = service.GetTraitGroup(4);

            Assert.IsNotNull(group1);
            Assert.IsFalse(group1.TraitIDs.Contains(999));

            if (group2 != null)
            {
                Assert.IsFalse(group2.TraitIDs.Contains(999));
            }
            if (group3 != null)
            {
                Assert.IsFalse(group3.TraitIDs.Contains(999));
            }
            if (group4 != null)
            {
                Assert.IsFalse(group4.TraitIDs.Contains(999));
            }
        }

        [Test]
        public void Test_v31_AC_TS_18_isScriptedDeath_FiltersSurviveTraits()
        {
            // AC-TS-18：C-05 驗證過濾邏輯（該測試屬 FT-04 範疇，C-05 只驗證 trait 資料正確）
            // 本測試確認 effectTarget == "on_death_survive" 與 "on_fail_survive" 的 trait 在 TraitTable 中定義正確
            string traitTableWithSurvive = _baseTables["TraitTable"] + "\n10,死亡倖存,死後能活著,condition,on_death_survive,1.0\n11,失敗倖存,失敗後活著,condition,on_fail_survive,1.0";

            TraitService service = CreateService(new Dictionary<string, string> { { "TraitTable", traitTableWithSurvive } });

            TraitData surviveOnDeath = service.GetTrait(10);
            TraitData surviveOnFail = service.GetTrait(11);

            Assert.IsNotNull(surviveOnDeath);
            Assert.AreEqual("on_death_survive", surviveOnDeath.effectTarget);
            Assert.AreEqual("condition", surviveOnDeath.effectType);

            Assert.IsNotNull(surviveOnFail);
            Assert.AreEqual("on_fail_survive", surviveOnFail.effectTarget);
            Assert.AreEqual("condition", surviveOnFail.effectType);
        }

        private TraitService CreateService(Dictionary<string, string> tableOverrides = null)
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
            DataManager.RegisterTable<TraitData>("TraitTable");
            DataManager.RegisterTable<TraitGroupData>("TraitGroupTable");

            GameObject dataManagerGO = new GameObject("DataManager_C05_Test");
            DataManager dataManager = dataManagerGO.AddComponent<DataManager>();
            ReflectionHelper.InvokeInstance(dataManager, "InitializeForTests");

            GameObject missionGO = new GameObject("MissionDatabaseService_C05_Test");
            MissionDatabaseService missionService = missionGO.AddComponent<MissionDatabaseService>();
            ReflectionHelper.InvokeInstance(missionService, "InitializeForTests");

            GameObject professionGO = new GameObject("ProfessionService_C05_Test");
            ProfessionService professionService = professionGO.AddComponent<ProfessionService>();
            ReflectionHelper.InvokeInstance(professionService, "InitializeForTests");

            GameObject traitGO = new GameObject("TraitService_C05_Test");
            TraitService traitService = traitGO.AddComponent<TraitService>();
            ReflectionHelper.InvokeInstance(traitService, "InitializeForTests");
            return traitService;
        }

        private void ResetAll()
        {
            ReflectionHelper.InvokeStatic(typeof(TraitService), "ResetForTests");
            ReflectionHelper.InvokeStatic(typeof(ProfessionService), "ResetForTests");
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
                { "ProfessionTable", LoadTestCsv("ProfessionTable.csv") },
                { "TraitTable", LoadTestCsv("TraitTable.csv") },
                { "TraitGroupTable", LoadTestCsv("TraitGroupTable.csv") },
                { "RaceTable", LoadTestCsv("RaceTable.csv") }
            };
        }

        private static string LoadTestCsv(string fileName)
        {
            string root = Path.Combine(
                Application.dataPath,
                "Tests",
                "EditMode",
                "Gameplay",
                "Trait",
                "TestResources");
            string path = Path.Combine(root, fileName);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string ReplaceRowValue(string csv, string rowName, int columnIndex, string newValue)
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
                    if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        List<string> cols = ParseCsvLine(line);
                        if (cols.Count > 0 && string.Equals(cols[0], rowName, StringComparison.Ordinal))
                        {
                            while (cols.Count <= columnIndex)
                            {
                                cols.Add(string.Empty);
                            }

                            cols[columnIndex] = newValue;
                            output = JoinCsvLine(cols);
                        }
                    }

                    sb.AppendLine(output);
                }
            }

            return sb.ToString();
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> columns = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    columns.Add(cell.ToString());
                    cell.Length = 0;
                    continue;
                }

                cell.Append(c);
            }

            columns.Add(cell.ToString());
            return columns;
        }

        private static string JoinCsvLine(List<string> columns)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(EscapeCsv(columns[i]));
            }
            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            bool needsQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0;
            if (!needsQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
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
