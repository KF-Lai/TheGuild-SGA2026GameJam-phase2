using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Resources.Events;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Resources
{
    public sealed class ResourceManagementTests
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
            TestReflectionHelpers.InvokeStatic(
                typeof(TimeSystem),
                "SetClockProviderForTests",
                new[] { typeof(Func<long>) },
                (Func<long>)(() => _now));
            TestReflectionHelpers.InvokeStatic(
                typeof(TimeSystem),
                "SetDeltaProviderForTests",
                new[] { typeof(Func<float>) },
                (Func<float>)(() => 0f));
            TestReflectionHelpers.InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)GetTableCsv);

            DataManager.RegisterSystemConstantsTable("SystemConstants");

            GameObject dmGo = new GameObject("DM_F03_Test");
            _dm = dmGo.AddComponent<DataManager>();
            _dm.InitializeForTests();

            GameObject tsGo = new GameObject("TS_F03_Test");
            _ts = tsGo.AddComponent<TimeSystem>();
            _ts.InitializeForTests();

            GameObject rmGo = new GameObject("RM_F03_Test");
            _rm = rmGo.AddComponent<ResourceManagement>();
            _rm.InitializeForTests();
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

        [Test] public void AC_RM_01_InitialValuesLoaded() { Assert.AreEqual(100, _rm.GetGold()); Assert.AreEqual(0, _rm.GetReputation()); }

        [Test]
        public void AC_RM_02_AddGoldClampsToMaxAndPublishesDelta()
        {
            int delta = 0;
            EventBus.Subscribe<OnGoldChangedEvent>(e => delta = e.Delta);

            _rm.AddGoldAllowBankruptcy(8_999_900);
            bool ok = _rm.AddGold(2_000_000);

            Assert.IsTrue(ok);
            Assert.AreEqual(9_999_999, _rm.GetGold());
            Assert.AreEqual(999_999, delta);
        }

        [Test]
        public void AC_RM_03_CanAffordBoundary()
        {
            Assert.IsTrue(_rm.CanAfford(0));
            Assert.IsTrue(_rm.CanAfford(-1));
            Assert.IsTrue(_rm.CanAfford(100));
            Assert.IsTrue(_rm.CanAfford(101));
            Assert.IsTrue(_rm.CanAfford(200));
            Assert.IsFalse(_rm.CanAfford(201));
        }
        [Test] public void AC_RM_04_AddGoldRejectBelowThreshold() { Assert.IsFalse(_rm.AddGold(-201)); Assert.AreEqual(100, _rm.GetGold()); }

        [Test]
        public void AC_RM_05_AddGoldAllowBankruptcyCanCrossThreshold()
        {
            bool ok = _rm.AddGoldAllowBankruptcy(-300);
            Assert.IsTrue(ok);
            Assert.AreEqual(-200, _rm.GetGold());
            Assert.AreEqual(BankruptcyWarningState.Bankrupt, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_06_AddReputationClampAndPublish()
        {
            int delta = 0;
            EventBus.Subscribe<OnReputationChangedEvent>(e => delta = e.Delta);
            _rm.AddReputation(200);
            Assert.AreEqual(100, _rm.GetReputation());
            Assert.AreEqual(100, delta);
        }

        [Test] public void AC_RM_07_SetBankruptcyThresholdReevaluate() { _rm.AddGoldAllowBankruptcy(-150); _rm.SetBankruptcyThreshold(-10); Assert.AreEqual(BankruptcyWarningState.Bankrupt, _rm.GetBankruptcyWarningState()); }

        [Test]
        public void AC_RM_08_EnterAndExitWarning()
        {
            _rm.AddGold(-150);
            Assert.AreEqual(BankruptcyWarningState.Warning, _rm.GetBankruptcyWarningState());
            _rm.AddGold(200);
            Assert.AreEqual(BankruptcyWarningState.Normal, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_10_TriggerBankruptcyIdempotent()
        {
            int count = 0;
            EventBus.Subscribe<OnBankruptcyStateChangedEvent>(e => { if (e.CurrentState == BankruptcyWarningState.Bankrupt) count++; });
            _rm.AddGoldAllowBankruptcy(-300);
            _rm.AddGoldAllowBankruptcy(-10);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void AC_RM_12_SetBankruptcyWarningDurationUpdatesCurrentValue()
        {
            Assert.AreEqual(86400, _rm.GetBankruptcyWarningDuration());

            _rm.SetBankruptcyWarningDuration(172800);

            Assert.AreEqual(172800, _rm.GetBankruptcyWarningDuration());
        }

        [Test]
        public void AC_RM_13_ReenterWarningUsesCurrentWarningDuration()
        {
            _rm.SetBankruptcyWarningDuration(172800);
            _rm.AddGold(-150);
            long first = _rm.GetBankruptcyWarningRemainingSeconds();
            Assert.AreEqual(172800, first);

            _rm.AddGold(250);
            Assert.AreEqual(BankruptcyWarningState.Normal, _rm.GetBankruptcyWarningState());

            _rm.SetBankruptcyWarningDuration(10800);
            _rm.AddGold(-250);
            long second = _rm.GetBankruptcyWarningRemainingSeconds();

            Assert.AreEqual(BankruptcyWarningState.Warning, _rm.GetBankruptcyWarningState());
            Assert.AreEqual(10800, second);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void AC_RM_14_CreateAndRestoreSnapshot()
        {
            _rm.AddGold(-150);
            _rm.AddReputation(10);
            ResourceSnapshot snap = _rm.CreateSnapshot();

            _rm.AddGold(200);
            _rm.AddReputation(-10);
            _rm.RestoreSnapshot(snap);

            Assert.AreEqual(-50, _rm.GetGold());
            Assert.AreEqual(10, _rm.GetReputation());
            Assert.AreEqual(BankruptcyWarningState.Warning, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_15_RestoreSnapshotAlwaysReevaluate()
        {
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = -200,
                CurrentReputation = 0,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 86400,
                CurrentBankruptcyThreshold = -100
            };

            _rm.RestoreSnapshot(snap);
            Assert.AreEqual(BankruptcyWarningState.Bankrupt, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_RestoreSnapshot_GoldOverMaxClampedAndWarn()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CurrentGold.*clamp"));
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = 10_000_000,
                CurrentReputation = 0,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 86400,
                CurrentBankruptcyThreshold = -100
            };

            _rm.RestoreSnapshot(snap);
            Assert.AreEqual(9_999_999, _rm.GetGold());
        }

        [Test]
        public void AC_RM_RestoreSnapshot_ReputationOutOfRangeClampedAndWarn()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CurrentReputation.*clamp"));
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = 100,
                CurrentReputation = 999,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 86400,
                CurrentBankruptcyThreshold = -100
            };

            _rm.RestoreSnapshot(snap);
            Assert.AreEqual(100, _rm.GetReputation());
        }

        [Test]
        public void AC_RM_RestoreSnapshot_PositiveBankruptcyThresholdResetAndWarn()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CurrentBankruptcyThreshold.*-100"));
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = 100,
                CurrentReputation = 0,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 86400,
                CurrentBankruptcyThreshold = 50
            };

            _rm.RestoreSnapshot(snap);
            Assert.AreEqual(-100, _rm.GetCurrentBankruptcyThreshold());
        }

        [Test]
        public void AC_RM_16_DeltaZero_NoGoldBroadcast()
        {
            int count = 0;
            EventBus.Subscribe<OnGoldChangedEvent>(_ => count++);
            _rm.AddGold(0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void AC_RM_17_DeltaZero_NoReputationBroadcast()
        {
            int count = 0;
            EventBus.Subscribe<OnReputationChangedEvent>(_ => count++);
            _rm.AddReputation(0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void AC_RM_18_DeltaZero_NoGoldBroadcastAllowBankruptcy()
        {
            int count = 0;
            EventBus.Subscribe<OnGoldChangedEvent>(_ => count++);
            _rm.AddGoldAllowBankruptcy(0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void AC_RM_19_ResetBankruptcyStateKeepsApi()
        {
            _rm.AddGold(-150);
            _rm.ResetBankruptcyState();
            Assert.AreEqual(BankruptcyWarningState.Normal, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_20_GetCurrentThresholdDefault()
        {
            Assert.AreEqual(-100, _rm.GetCurrentBankruptcyThreshold());
        }

        [Test]
        public void AC_RM_21_WarningRemainingNonNegative()
        {
            _rm.AddGold(-150);
            _now += 9999999;
            Assert.AreEqual(0, _rm.GetBankruptcyWarningRemainingSeconds());
        }

        [Test]
        public void AC_RM_22_ReentrancyGuard()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("重入防護觸發|reentrancy detected"));
            EventBus.Subscribe<OnGoldChangedEvent>(_ => _rm.AddGold(1));
            bool ok = _rm.AddGold(1);
            Assert.IsTrue(ok);
            Assert.AreEqual(101, _rm.GetGold());
        }

        [Test]
        public void AC_RM_23_AddGoldAllowBankruptcyOverflowClampsAndWarns()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("溢位|overflow"));
            _rm.AddGoldAllowBankruptcy(int.MaxValue);
            Assert.AreEqual(9_999_999, _rm.GetGold());
        }

        [Test]
        public void AC_RM_24_SetBankruptcyWarningDurationRejectsNonPositive()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SetBankruptcyWarningDuration.*輸入值=0"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SetBankruptcyWarningDuration.*輸入值=-1"));

            _rm.SetBankruptcyWarningDuration(172800);
            _rm.SetBankruptcyWarningDuration(0);
            _rm.SetBankruptcyWarningDuration(-1);

            Assert.AreEqual(172800, _rm.GetBankruptcyWarningDuration());
        }

        [Test]
        public void AC_RM_26_StateEventPublishedOnTransition()
        {
            var seq = new List<BankruptcyWarningState>();
            EventBus.Subscribe<OnBankruptcyStateChangedEvent>(e => seq.Add(e.CurrentState));
            _rm.AddGold(-150);
            _rm.AddGold(200);
            CollectionAssert.AreEqual(new[] { BankruptcyWarningState.Warning, BankruptcyWarningState.Normal }, seq);
        }

        [Test]
        public void AC_RM_27_SetBankruptcyWarningDurationDoesNotAffectLockedWarning()
        {
            _rm.SetBankruptcyWarningDuration(172800);
            _rm.AddGold(-150);
            Assert.AreEqual(172800, _rm.GetBankruptcyWarningRemainingSeconds());

            _rm.SetBankruptcyWarningDuration(10800);
            _rm.AddReputation(80);

            Assert.AreEqual(10800, _rm.GetBankruptcyWarningDuration());
            Assert.AreEqual(172800, _rm.GetBankruptcyWarningRemainingSeconds());
        }

        [Test]
        public void AC_RM_28_HandleOfflineResolvedAlwaysEvaluate()
        {
            _rm.AddGold(-150);
            _rm.AddGold(200);
            EventBus.Publish(new OnOfflineResolvedEvent(100, 0));
            Assert.AreEqual(BankruptcyWarningState.Normal, _rm.GetBankruptcyWarningState());
        }

        [Test]
        public void AC_RM_29_GetBankruptcyWarningRemainingSeconds_OnlyWarningState()
        {
            Assert.AreEqual(0, _rm.GetBankruptcyWarningRemainingSeconds());
            _rm.AddGoldAllowBankruptcy(-300);
            Assert.AreEqual(0, _rm.GetBankruptcyWarningRemainingSeconds());
        }

        [Test]
        public void AC_RM_30_RestoreSnapshotRestoresCurrentWarningDuration()
        {
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = 100,
                CurrentReputation = 0,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 172800,
                CurrentBankruptcyThreshold = -100
            };

            _rm.RestoreSnapshot(snap);

            Assert.AreEqual(172800, _rm.GetBankruptcyWarningDuration());
        }

        [Test]
        public void AC_RM_31_RestoreSnapshotNonPositiveCurrentWarningDurationFallbackAndWarn()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CurrentWarningDuration.*86400"));
            ResourceSnapshot snap = new ResourceSnapshot
            {
                CurrentGold = 100,
                CurrentReputation = 0,
                WarningState = BankruptcyWarningState.Normal,
                BankruptcyWarningStartTime = 0,
                WarningDurationSec = 0,
                CurrentWarningDuration = 0,
                CurrentBankruptcyThreshold = -100
            };

            _rm.RestoreSnapshot(snap);

            Assert.AreEqual(86400, _rm.GetBankruptcyWarningDuration());
        }

        private string GetTableCsv(string tableName)
        {
            if (tableName == "SystemConstants")
            {
                return "key,DAILY_RESET_HOUR,OFFLINE_MAX_SECONDS,GOLD_INITIAL,GOLD_MAX,REPUTATION_MIN,REPUTATION_MAX\n" +
                       "value,0,604800,100,9999999,-100,100\n" +
                       "description,UTC hour,cap,initial,max,min,max\n";
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
