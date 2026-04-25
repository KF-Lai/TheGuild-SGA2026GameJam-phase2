using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Gameplay.Resources
{
    public sealed class ResourceManagementPlayModeTests
    {
        private DataManager _dm;
        private TimeSystem _ts;
        private ResourceManagement _rm;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            TestReflectionHelpers.InvokeStatic(typeof(DataManager), "ResetForTests");
            TestReflectionHelpers.InvokeStatic(typeof(TimeSystem), "ResetTestHooks");
            TestReflectionHelpers.InvokeStatic(typeof(ResourceManagement), "ResetForTests");
            TestReflectionHelpers.InvokeStatic(typeof(EventBus), "ClearAll");

            _now = 1_700_000_000L;
            TestReflectionHelpers.InvokeStatic(typeof(TimeSystem), "SetClockProviderForTests", new[] { typeof(Func<long>) }, (Func<long>)(() => _now));
            TestReflectionHelpers.InvokeStatic(typeof(TimeSystem), "SetDeltaProviderForTests", new[] { typeof(Func<float>) }, (Func<float>)(() => 0f));
            TestReflectionHelpers.InvokeStatic(typeof(DataManager), "SetTableTextProviderForTests", new[] { typeof(Func<string, string>) }, (Func<string, string>)GetTableCsv);

            DataManager.RegisterSystemConstantsTable("SystemConstants");
            DataManager.RegisterTable<BankruptcyThresholdData>("BankruptcyThresholdTable");

            GameObject dmGo = new GameObject("DM_F03_Play");
            _dm = dmGo.AddComponent<DataManager>();

            GameObject tsGo = new GameObject("TS_F03_Play");
            _ts = tsGo.AddComponent<TimeSystem>();

            GameObject rmGo = new GameObject("RM_F03_Play");
            _rm = rmGo.AddComponent<ResourceManagement>();
        }

        [TearDown]
        public void TearDown()
        {
            TestReflectionHelpers.InvokeStatic(typeof(EventBus), "ClearAll");
            TestReflectionHelpers.InvokeStatic(typeof(TimeSystem), "ResetTestHooks");
            TestReflectionHelpers.InvokeStatic(typeof(DataManager), "ResetForTests");
            TestReflectionHelpers.InvokeStatic(typeof(ResourceManagement), "ResetForTests");

            if (_rm != null)
            {
                UnityEngine.Object.DestroyImmediate(_rm.gameObject);
            }

            if (_ts != null)
            {
                UnityEngine.Object.DestroyImmediate(_ts.gameObject);
            }

            if (_dm != null)
            {
                UnityEngine.Object.DestroyImmediate(_dm.gameObject);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AC_RM_09_WarningCountdownExpires_TriggersBankrupt()
        {
            // 不加 reputation：rep=0 -> band 2 (0~29) -> duration=43200
            _rm.AddGold(-150);
            Assert.AreEqual(BankruptcyWarningState.Warning, _rm.GetBankruptcyWarningState());

            _now += 43201;
            TestReflectionHelpers.InvokeInstance(_ts, "TickForTests", new[] { typeof(float) }, 1f);

            yield return null;
            Assert.AreEqual(BankruptcyWarningState.Bankrupt, _rm.GetBankruptcyWarningState());
        }

        [UnityTest]
        public IEnumerator AC_RM_11_OfflineResolvedCanTriggerBankrupt()
        {
            _rm.AddGold(-150);
            Assert.AreEqual(BankruptcyWarningState.Warning, _rm.GetBankruptcyWarningState());

            _now += 90000;
            EventBus.Publish(new OnOfflineResolvedEvent(90000, 0));

            yield return null;
            Assert.AreEqual(BankruptcyWarningState.Bankrupt, _rm.GetBankruptcyWarningState());
        }

        private string GetTableCsv(string tableName)
        {
            if (tableName == "SystemConstants")
            {
                return "key,value,description\n" +
                       "DAILY_RESET_HOUR,0,UTC hour\n" +
                       "OFFLINE_MAX_SECONDS,604800,cap\n" +
                       "GOLD_INITIAL,100,initial\n" +
                       "GOLD_MAX,9999999,max\n" +
                       "REPUTATION_MIN,-100,min\n" +
                       "REPUTATION_MAX,100,max\n";
            }

            if (tableName == "BankruptcyThresholdTable")
            {
                return "id,reputationMin,reputationMax,warningDurationSec\n" +
                       "1,-100,-1,86400\n" +
                       "2,0,29,43200\n" +
                       "3,30,59,172800\n" +
                       "4,60,79,259200\n" +
                       "5,80,100,604800\n" +
                       "6,101,200,999\n";
            }

            return null;
        }
    }

    internal static class TestReflectionHelpers
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

        public static void InvokeInstance(object instance, string methodName, Type[] paramTypes, params object[] args)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, InstanceNonPublic, null, paramTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            method.Invoke(instance, args);
        }
    }
}
