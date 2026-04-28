using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Gameplay.Race
{
    public sealed class RaceSystemPlayModeTests
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
        }

        [UnityTest]
        public IEnumerator AC_RS_10_RollRaceDistribution_Profession7_IsWithinTolerance()
        {
            RaceService service = CreateService();

            UnityEngine.Random.InitState(260427);
            const int sampleCount = 1000;
            int race1 = 0;
            int race3 = 0;
            int race4 = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                int raceID = service.RollRace(7);
                if (raceID == 1)
                {
                    race1++;
                }
                else if (raceID == 3)
                {
                    race3++;
                }
                else if (raceID == 4)
                {
                    race4++;
                }
            }

            Assert.AreEqual(sampleCount, race1 + race3 + race4);
            Assert.That(race1, Is.InRange(450, 550)); // 50% ±5%
            Assert.That(race3, Is.InRange(250, 350)); // 30% ±5%
            Assert.That(race4, Is.InRange(150, 250)); // 20% ±5%

            yield return null;
        }

        private RaceService CreateService()
        {
            ReflectionHelper.InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(tableName => _baseTables.TryGetValue(tableName, out string csv) ? csv : null));

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterTable<MissionTemplate>("MissionTemplate");
            DataManager.RegisterTable<MissionDifficultyData>("MissionDifficultyTable");
            DataManager.RegisterTable<MissionTypeData>("MissionTypeTable");
            DataManager.RegisterTable<MissionCategoryData>("MissionCategoryTable");
            DataManager.RegisterTable<ProfessionData>("ProfessionTable");
            DataManager.RegisterTable<RaceData>("RaceTable");

            GameObject dataManagerGO = new GameObject("DataManager_C04_PlayMode");
            DataManager dataManager = dataManagerGO.AddComponent<DataManager>();
            ReflectionHelper.InvokeInstance(dataManager, "InitializeForTests");

            GameObject missionGO = new GameObject("MissionDatabaseService_C04_PlayMode");
            MissionDatabaseService missionService = missionGO.AddComponent<MissionDatabaseService>();
            ReflectionHelper.InvokeInstance(missionService, "InitializeForTests");

            GameObject professionGO = new GameObject("ProfessionService_C04_PlayMode");
            ProfessionService professionService = professionGO.AddComponent<ProfessionService>();
            ReflectionHelper.InvokeInstance(professionService, "InitializeForTests");

            GameObject raceGO = new GameObject("RaceService_C04_PlayMode");
            RaceService raceService = raceGO.AddComponent<RaceService>();
            ReflectionHelper.InvokeInstance(raceService, "InitializeForTests");
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
