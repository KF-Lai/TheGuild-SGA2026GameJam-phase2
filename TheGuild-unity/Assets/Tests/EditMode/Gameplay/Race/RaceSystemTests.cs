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
using TheGuild.Gameplay.Race;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Race
{
    public sealed class RaceSystemTests
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
        public void AC_RS_01_GetAllRaces_CountIs4()
        {
            RaceService service = CreateService();
            Assert.AreEqual(4, service.GetAllRaces().Count);
        }

        [Test]
        public void AC_RS_02_GetRace_NameMatches()
        {
            RaceService service = CreateService();
            RaceData row = service.GetRace(1);

            Assert.IsNotNull(row);
            Assert.AreEqual("人類", row.name);
        }

        [Test]
        public void AC_RS_03_To_07_ModifierQueries()
        {
            RaceService service = CreateService();

            Assert.AreEqual(0.08f, service.GetSuccessDelta(1, 3), 0.0001f);
            Assert.AreEqual(-0.08f, service.GetDeathDelta(1, 2), 0.0001f);
            Assert.AreEqual(0f, service.GetSuccessDelta(1, 1), 0.0001f);
            Assert.AreEqual(0.10f, service.GetSuccessDelta(2, 4), 0.0001f);
            Assert.AreEqual(0.05f, service.GetDeathDelta(4, 2), 0.0001f);
        }

        [Test]
        public void AC_RS_08_GetRaceZero_WarnsAndReturnsNull()
        {
            RaceService service = CreateService();

            LogAssert.Expect(LogType.Warning, new Regex("null sentinel", RegexOptions.IgnoreCase));
            Assert.IsNull(service.GetRace(0));
        }

        [Test]
        public void AC_RS_09_InvalidModifierJson_LogsErrorAndFallbackZero()
        {
            string invalidRaceTable = ReplaceRowValue(
                _baseTables["RaceTable"],
                "modifiers",
                1,
                "[{broken]");

            LogAssert.Expect(LogType.Error, new Regex("modifiers JSON parse failed", RegexOptions.IgnoreCase));
            RaceService service = CreateService(new Dictionary<string, string> { { "RaceTable", invalidRaceTable } });

            Assert.AreEqual(0f, service.GetSuccessDelta(1, 3), 0.0001f);
            Assert.AreEqual(0f, service.GetDeathDelta(1, 2), 0.0001f);
        }

        [Test]
        public void Rule05_UnknownTypeID_LogsWarningAndFiltersOut()
        {
            string raceTable = ReplaceRowValue(
                _baseTables["RaceTable"],
                "modifiers",
                1,
                "[{\"typeID\":99,\"successDelta\":0.33,\"deathDelta\":0.44},{\"typeID\":2,\"successDelta\":0.0,\"deathDelta\":-0.08}]");

            LogAssert.Expect(LogType.Warning, new Regex("unknown typeID=99", RegexOptions.IgnoreCase));
            RaceService service = CreateService(new Dictionary<string, string> { { "RaceTable", raceTable } });

            Assert.AreEqual(0f, service.GetSuccessDelta(1, 99), 0.0001f);
            Assert.AreEqual(-0.08f, service.GetDeathDelta(1, 2), 0.0001f);
        }

        [Test]
        public void Rule05_MissionServiceNull_SkipsTypeFiltering()
        {
            string raceTable = ReplaceRowValue(
                _baseTables["RaceTable"],
                "modifiers",
                1,
                "[{\"typeID\":99,\"successDelta\":0.25,\"deathDelta\":0.15}]");

            // ProfessionDatabaseLoader 與 RaceDatabaseLoader 各 fire 一條相同訊息
            LogAssert.Expect(LogType.Error, new Regex(@"MissionDatabaseService\.Instance is null", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Error, new Regex(@"MissionDatabaseService\.Instance is null", RegexOptions.IgnoreCase));
            RaceService service = CreateService(
                new Dictionary<string, string> { { "RaceTable", raceTable } },
                initializeMissionService: false);

            Assert.AreEqual(0.25f, service.GetSuccessDelta(1, 99), 0.0001f);
            Assert.AreEqual(0.15f, service.GetDeathDelta(1, 99), 0.0001f);
        }

        [Test]
        public void AC_RS_11_RollRace_UnknownProfession_ReturnsFallback()
        {
            RaceService service = CreateService();

            LogAssert.Expect(LogType.Error, new Regex("unknown professionID=999", RegexOptions.IgnoreCase));
            Assert.AreEqual(1, service.RollRace(999));
        }

        [Test]
        public void AC_RS_12_RollRace_LengthMismatch_ReturnsFallback()
        {
            RaceService service = CreateService();

            ProfessionData injected = CreateInjectedProfession(701, new[] { 1, 3 }, new[] { 100 });
            InjectProfession(injected);

            LogAssert.Expect(LogType.Error, new Regex(@"raceIDs\.Count", RegexOptions.IgnoreCase));
            Assert.AreEqual(1, service.RollRace(701));
        }

        [Test]
        public void AC_RS_12b_RollRace_NonPositiveWeights_ReturnsFallback()
        {
            RaceService service = CreateService();

            ProfessionData injected = CreateInjectedProfession(702, new[] { 1, 3 }, new[] { 0, -10 });
            InjectProfession(injected);

            LogAssert.Expect(LogType.Warning, new Regex("non-positive weight=0", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Warning, new Regex("non-positive weight=-10", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Error, new Regex("no valid race candidates", RegexOptions.IgnoreCase));
            Assert.AreEqual(1, service.RollRace(702));
        }

        [Test]
        public void RollRace_UnknownRaceID_FilteredWithWarning()
        {
            RaceService service = CreateService();

            ProfessionData injected = CreateInjectedProfession(703, new[] { 999, 1 }, new[] { 80, 20 });
            InjectProfession(injected);

            LogAssert.Expect(LogType.Warning, new Regex("unknown raceID=999", RegexOptions.IgnoreCase));
            Assert.AreEqual(1, service.RollRace(703));
        }

        private RaceService CreateService(
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
            DataManager.RegisterTable<RaceData>("RaceTable");

            GameObject dataManagerGO = new GameObject("DataManager_C04_Test");
            DataManager dataManager = dataManagerGO.AddComponent<DataManager>();
            ReflectionHelper.InvokeInstance(dataManager, "InitializeForTests");

            if (initializeMissionService)
            {
                GameObject missionGO = new GameObject("MissionDatabaseService_C04_Test");
                MissionDatabaseService missionService = missionGO.AddComponent<MissionDatabaseService>();
                ReflectionHelper.InvokeInstance(missionService, "InitializeForTests");
            }

            GameObject professionGO = new GameObject("ProfessionService_C04_Test");
            ProfessionService professionService = professionGO.AddComponent<ProfessionService>();
            ReflectionHelper.InvokeInstance(professionService, "InitializeForTests");

            GameObject raceGO = new GameObject("RaceService_C04_Test");
            RaceService raceService = raceGO.AddComponent<RaceService>();
            raceService.InitializeForTests();
            return raceService;
        }

        private void ResetAll()
        {
            ReflectionHelper.InvokeStatic(typeof(RaceService), "ResetForTests");
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
                "Race",
                "TestResources");
            string path = Path.Combine(root, fileName);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static ProfessionData CreateInjectedProfession(int professionID, int[] raceIDs, int[] raceWeights)
        {
            ProfessionData row = new ProfessionData
            {
                professionID = professionID,
                name = "Injected",
                description = "Injected for test",
                tier = 1,
                baseProfessionID = 0
            };

            ReflectionHelper.SetPrivateField(row, "_raceIDs", raceIDs ?? Array.Empty<int>());
            ReflectionHelper.SetPrivateField(row, "_raceWeights", raceWeights ?? Array.Empty<int>());
            ReflectionHelper.SetPrivateField(row, "_traitGroupIDs", Array.Empty<int>());
            return row;
        }

        private static void InjectProfession(ProfessionData row)
        {
            ProfessionService service = ProfessionService.Instance;
            FieldInfo field = typeof(ProfessionService).GetField("_professionByID", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(ProfessionService).FullName, "_professionByID");
            }

            Dictionary<int, ProfessionData> map = new Dictionary<int, ProfessionData>();
            if (field.GetValue(service) is IReadOnlyDictionary<int, ProfessionData> current)
            {
                foreach (KeyValuePair<int, ProfessionData> pair in current)
                {
                    map[pair.Key] = pair.Value;
                }
            }

            map[row.professionID] = row;
            field.SetValue(service, map);
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

        public static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, InstanceNonPublic);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            field.SetValue(instance, value);
        }
    }
}
