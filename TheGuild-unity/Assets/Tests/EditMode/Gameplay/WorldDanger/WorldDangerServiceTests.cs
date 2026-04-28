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
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.WorldDanger;
using TheGuild.Gameplay.WorldDanger.Events;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.WorldDanger
{
    public sealed class WorldDangerServiceTests
    {
        private const long Day = 86400L;

        private Dictionary<string, string> _baseTables;
        private long _nowUtc;

        private DataManager _dataManager;
        private TimeSystem _timeSystem;
        private ResourceManagement _resourceManagement;
        private WorldDangerService _worldDangerService;

        [SetUp]
        public void SetUp()
        {
            ResetAll();
            _baseTables = LoadBaseTables();
            _nowUtc = 1_700_000_000L;
        }

        [TearDown]
        public void TearDown()
        {
            ResetAll();
            DestroyAllObjects();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoD_01_DefaultCurrentLevel_IsE()
        {
            WorldDangerService service = CreateService();
            Assert.AreEqual("E", service.GetCurrentLevel());
        }

        [Test]
        public void DoD_02_GetDangerData_A_LevelNameMatches()
        {
            WorldDangerService service = CreateService();
            WorldDangerData row = service.GetDangerData("A");

            Assert.IsNotNull(row);
            Assert.AreEqual("Catastrophe", row.name);
        }

        [Test]
        public void DoD_03_GetMaxDebt_EAndD()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "E");
            Assert.AreEqual(-100, service.GetMaxDebt());

            SetPrivateField(service, "_currentDangerLevel", "D");
            Assert.AreEqual(-500, service.GetMaxDebt());
        }

        [Test]
        public void DoD_04_GetPoolWeights_EWeightsMatch()
        {
            WorldDangerService service = CreateService();
            SetPrivateField(service, "_currentDangerLevel", "E");

            MissionPoolWeights weights = service.GetPoolWeights();
            Assert.AreEqual(40, weights.weightF_E);
            Assert.AreEqual(0, weights.weightS_SSS);
        }

        [Test]
        public void DoD_05_StartPushesEThresholdToResourceManagement()
        {
            WorldDangerService service = CreateService();
            SetPrivateField(service, "_currentDangerLevel", "E");
            InvokeInstance(service, "Start");

            Assert.AreEqual(-100, ResourceManagement.Instance.GetCurrentBankruptcyThreshold());
        }

        [Test]
        public void DoD_06_CheckLevelUp_ThreeGateFailCases()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 10);
            SetPrivateField(service, "_cachedMaxFactionScore", 99);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc);
            service.CheckLevelUp();
            Assert.AreEqual("E", service.GetCurrentLevel());

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 9);
            SetPrivateField(service, "_cachedMaxFactionScore", 99);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 2 * Day);
            service.CheckLevelUp();
            Assert.AreEqual("E", service.GetCurrentLevel());

            SetPrivateField(service, "_currentDangerLevel", "D");
            SetPrivateField(service, "_acceptedMissionCount", 15);
            SetPrivateField(service, "_cachedMaxFactionScore", 4);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 10 * Day);
            service.CheckLevelUp();
            Assert.AreEqual("D", service.GetCurrentLevel());
        }

        [Test]
        public void DoD_07_OnFactionScoreUpdated_TriggersOnlyWhenThresholdReached()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "D");
            SetPrivateField(service, "_acceptedMissionCount", 15);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 10 * Day);

            service.OnFactionScoreUpdated(4);
            Assert.AreEqual("D", service.GetCurrentLevel());

            service.OnFactionScoreUpdated(5);
            Assert.AreEqual("C", service.GetCurrentLevel());
        }

        [Test]
        public void DoD_08_OnMissionAccepted_CountsOnlyWhenDifficultyPasses()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "C");
            SetPrivateField(service, "_acceptedMissionCount", 0);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc);

            service.OnMissionAccepted("F");
            Assert.AreEqual(0, GetPrivateField<int>(service, "_acceptedMissionCount"));

            service.OnMissionAccepted("C");
            Assert.AreEqual(1, GetPrivateField<int>(service, "_acceptedMissionCount"));
        }

        [Test]
        public void DoD_09_CheckLevelUp_RecursiveAndResetAcceptedCount()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "C");
            SetPrivateField(service, "_acceptedMissionCount", 20);
            SetPrivateField(service, "_cachedMaxFactionScore", 12);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 10 * Day);

            service.CheckLevelUp();

            Assert.AreEqual("B", service.GetCurrentLevel());
            Assert.AreEqual(0, GetPrivateField<int>(service, "_acceptedMissionCount"));
        }

        [Test]
        public void DoD_10_OfflineCrossLevel_ToA_WhenMissionReqConfiguredZero()
        {
            string csv = _baseTables["WorldDangerTable"];
            csv = ReplaceRowValue(csv, "missionCountReq", 2, "0");
            csv = ReplaceRowValue(csv, "missionCountReq", 3, "0");
            csv = ReplaceRowValue(csv, "missionCountReq", 4, "0");
            csv = ReplaceRowValue(csv, "missionCountReq", 5, "0");

            WorldDangerService service = CreateService(new Dictionary<string, string> { { "WorldDangerTable", csv } });

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 99);
            SetPrivateField(service, "_cachedMaxFactionScore", 99);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 30 * Day);

            service.CheckLevelUp();
            Assert.AreEqual("A", service.GetCurrentLevel());
        }

        [Test]
        public void DoD_11_A_Level_NoOpForMissionFactionAndCheck()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "A");
            SetPrivateField(service, "_acceptedMissionCount", 7);
            SetPrivateField(service, "_cachedMaxFactionScore", 1);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 30 * Day);

            service.OnMissionAccepted("SSS");
            service.OnFactionScoreUpdated(999);
            service.CheckLevelUp();

            Assert.AreEqual("A", service.GetCurrentLevel());
            Assert.AreEqual(7, GetPrivateField<int>(service, "_acceptedMissionCount"));
            Assert.AreEqual(1, GetPrivateField<int>(service, "_cachedMaxFactionScore"));
        }

        [Test]
        public void DoD_12_OnDangerLevelChangedEvent_PublishedExactlyOnce()
        {
            WorldDangerService service = CreateService();

            int publishCount = 0;
            string payload = null;
            EventBus.Subscribe<OnDangerLevelChangedEvent>(evt =>
            {
                publishCount += 1;
                payload = evt.newDangerLevel;
            });

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 10);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 2 * Day);

            service.CheckLevelUp();

            Assert.AreEqual(1, publishCount);
            Assert.AreEqual("D", payload);
        }

        [Test]
        public void DoD_13_StartPushesCurrentLevelThresholdToResourceManagement()
        {
            WorldDangerService service = CreateService();
            SetPrivateField(service, "_currentDangerLevel", "C");
            InvokeInstance(service, "Start");

            Assert.AreEqual(-1000, ResourceManagement.Instance.GetCurrentBankruptcyThreshold());
        }

        [Test]
        public void DoD_14_CsvValidationAndFallback_BehavesAsSpecified()
        {
            string csv = _baseTables["WorldDangerTable"];
            csv = ReplaceRowValue(csv, "dangerLevel", 1, "e");
            csv = ReplaceRowValue(csv, "minDifficulty", 3, "X");
            csv = ReplaceRowValue(csv, "weightF_E", 2, "0");
            csv = ReplaceRowValue(csv, "weightD", 2, "0");
            csv = ReplaceRowValue(csv, "weightC", 2, "0");
            csv = ReplaceRowValue(csv, "weightB", 2, "0");
            csv = ReplaceRowValue(csv, "weightA", 2, "0");
            csv = ReplaceRowValue(csv, "weightS_SSS", 2, "0");
            csv = ReplaceRowValue(csv, "maxDebt", 2, "0");

            // Loader 按 CSV 行序處理：E(case warning) → D(weights+maxDebt errors) → C(minDifficulty error)
            LogAssert.Expect(LogType.Warning, new Regex("dangerLevel 大小寫不一致", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Error, new Regex("權重全為 0", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Error, new Regex("maxDebt 為 0", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Error, new Regex("minDifficulty 非法", RegexOptions.IgnoreCase));

            WorldDangerService service = CreateService(new Dictionary<string, string> { { "WorldDangerTable", csv } });

            SetPrivateField(service, "_currentDangerLevel", "D");
            // GetMaxDebt 在 D 階 maxDebt=0 觸發 fallback LogError
            LogAssert.Expect(LogType.Error, new Regex("GetMaxDebt fallback", RegexOptions.IgnoreCase));
            Assert.AreEqual(-100, service.GetMaxDebt());

            // GetPoolWeights 在 D 階 weights 全為 0 觸發 fallback LogError
            LogAssert.Expect(LogType.Error, new Regex("GetPoolWeights fallback", RegexOptions.IgnoreCase));
            MissionPoolWeights fallbackWeights = service.GetPoolWeights();
            Assert.AreEqual(40, fallbackWeights.weightF_E);
            Assert.AreEqual(30, fallbackWeights.weightD);
            Assert.AreEqual(20, fallbackWeights.weightC);
            Assert.AreEqual(8, fallbackWeights.weightB);
            Assert.AreEqual(2, fallbackWeights.weightA);
            Assert.AreEqual(0, fallbackWeights.weightS_SSS);

            WorldDangerData cRow = service.GetDangerData("C");
            Assert.IsNotNull(cRow);
            Assert.AreEqual("F", cRow.minDifficulty);
        }

        [Test]
        public void OnDailyReset_StringEventSubscription_TriggersCheckLevelUp()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 10);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 2 * Day);

            EventBus.Publish(EventNames.OnDailyReset);
            Assert.AreEqual("D", service.GetCurrentLevel());
        }

        [Test]
        public void OnMissionAccepted_InvalidDifficulty_LogsWarningAndReturns()
        {
            WorldDangerService service = CreateService();

            SetPrivateField(service, "_currentDangerLevel", "E");
            SetPrivateField(service, "_acceptedMissionCount", 0);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 2 * Day);

            LogAssert.Expect(LogType.Warning, new Regex("無效 difficulty", RegexOptions.IgnoreCase));
            service.OnMissionAccepted("??");
            Assert.AreEqual(0, GetPrivateField<int>(service, "_acceptedMissionCount"));
        }

        [Test]
        public void CheckLevelUp_MissingLevelRow_LogsErrorAndMovesToNextLevel()
        {
            string csv = _baseTables["WorldDangerTable"];
            csv = ReplaceRowValue(csv, "dangerLevel", 3, "X");

            LogAssert.Expect(LogType.Error, new Regex("dangerLevel 非法", RegexOptions.IgnoreCase));
            WorldDangerService service = CreateService(new Dictionary<string, string> { { "WorldDangerTable", csv } });

            SetPrivateField(service, "_currentDangerLevel", "D");
            SetPrivateField(service, "_acceptedMissionCount", 0);
            SetPrivateField(service, "_cachedMaxFactionScore", 0);
            SetPrivateField(service, "_gameStartTimestamp", _nowUtc - 30 * Day);

            LogAssert.Expect(LogType.Error, new Regex("缺漏 dangerLevel 階資料", RegexOptions.IgnoreCase));
            service.CheckLevelUp();

            Assert.AreEqual("C", service.GetCurrentLevel());
        }

        private WorldDangerService CreateService(Dictionary<string, string> tableOverrides = null)
        {
            Dictionary<string, string> tables = new Dictionary<string, string>(_baseTables, StringComparer.Ordinal);
            if (tableOverrides != null)
            {
                foreach (KeyValuePair<string, string> pair in tableOverrides)
                {
                    tables[pair.Key] = pair.Value;
                }
            }

            InvokeStatic(
                typeof(TimeSystem),
                "SetClockProviderForTests",
                new[] { typeof(Func<long>) },
                (Func<long>)(() => _nowUtc));

            InvokeStatic(
                typeof(TimeSystem),
                "SetDeltaProviderForTests",
                new[] { typeof(Func<float>) },
                (Func<float>)(() => 0f));

            InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(tableName => tables.TryGetValue(tableName, out string csv) ? csv : null));

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterTable<WorldDangerData>("WorldDangerTable");

            GameObject dataManagerGO = new GameObject("DM_C06_Test");
            _dataManager = dataManagerGO.AddComponent<DataManager>();
            InvokeInstance(_dataManager, "InitializeForTests");

            GameObject timeSystemGO = new GameObject("TS_C06_Test");
            _timeSystem = timeSystemGO.AddComponent<TimeSystem>();
            InvokeInstance(_timeSystem, "InitializeForTests");

            GameObject resourceGO = new GameObject("RM_C06_Test");
            _resourceManagement = resourceGO.AddComponent<ResourceManagement>();
            InvokeInstance(_resourceManagement, "InitializeForTests");

            GameObject worldDangerGO = new GameObject("WD_C06_Test");
            _worldDangerService = worldDangerGO.AddComponent<WorldDangerService>();
            _worldDangerService.InitializeForTests();
            // EditMode 下 AddComponent 不保證觸發 OnEnable，手動呼叫以建立 OnDailyReset 訂閱
            InvokeInstance(_worldDangerService, "OnEnable");

            InvokeInstance(_worldDangerService, "Start");
            return _worldDangerService;
        }

        private static Dictionary<string, string> LoadBaseTables()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "SystemConstants", LoadCsv("SystemConstants.csv") },
                { "WorldDangerTable", LoadCsv("WorldDangerTable.csv") }
            };
        }

        private static string LoadCsv(string fileName)
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "EditMode",
                "Gameplay",
                "WorldDanger",
                "TestResources",
                fileName);

            return File.ReadAllText(path, Encoding.UTF8);
        }

        private void ResetAll()
        {
            InvokeStatic(typeof(EventBus), "ClearAll");
            InvokeStatic(typeof(WorldDangerService), "ResetForTests");
            InvokeStatic(typeof(ResourceManagement), "ResetForTests");
            InvokeStatic(typeof(TimeSystem), "ResetTestHooks");
            InvokeStatic(typeof(DataManager), "ResetForTests");
        }

        private void DestroyAllObjects()
        {
            if (_worldDangerService != null)
            {
                UnityEngine.Object.DestroyImmediate(_worldDangerService.gameObject);
                _worldDangerService = null;
            }

            if (_resourceManagement != null)
            {
                UnityEngine.Object.DestroyImmediate(_resourceManagement.gameObject);
                _resourceManagement = null;
            }

            if (_timeSystem != null)
            {
                UnityEngine.Object.DestroyImmediate(_timeSystem.gameObject);
                _timeSystem = null;
            }

            if (_dataManager != null)
            {
                UnityEngine.Object.DestroyImmediate(_dataManager.gameObject);
                _dataManager = null;
            }
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
                        List<string> columns = ParseCsvLine(line);
                        if (columns.Count > 0 && string.Equals(columns[0], rowName, StringComparison.Ordinal))
                        {
                            while (columns.Count <= columnIndex)
                            {
                                columns.Add(string.Empty);
                            }

                            columns[columnIndex] = newValue;
                            output = JoinCsvLine(columns);
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

        private static void InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            method.Invoke(null, args);
        }

        private static void InvokeStatic(Type type, string methodName, Type[] parameterTypes, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic, null, parameterTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            method.Invoke(null, args);
        }

        private static void InvokeInstance(object instance, string methodName, params object[] args)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            method.Invoke(instance, args);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            field.SetValue(instance, value);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            return (T)field.GetValue(instance);
        }
    }
}
