using System;
using NUnit.Framework;
using System.Reflection;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Core.Time
{
    /// <summary>
    /// F-02 TimeSystem 測試（FSD-A D-01 後：mission timer 相關測試已移除，由 FT-02-A 自帶測試覆蓋）。
    /// </summary>
    public sealed class TimeSystemTests
    {
        private long _now;
        private DataManager _dataManager;
        private TimeSystem _timeSystem;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            TimeSystem.ResetTestHooks();
            ResetDataManagerForTestsByReflection();

            _now = 1_700_000_000;
            TimeSystem.SetClockProviderForTests(() => _now);
            TimeSystem.SetDeltaProviderForTests(() => 0f);

            RegisterSystemConstantsIntoDataManagerForTests();

            GameObject dmGo = new GameObject("DataManager_Test");
            _dataManager = dmGo.AddComponent<DataManager>();
            _dataManager.InitializeForTests();

            GameObject tsGo = new GameObject("TimeSystem_Test");
            _timeSystem = tsGo.AddComponent<TimeSystem>();
            _timeSystem.InitializeForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            TimeSystem.ResetTestHooks();

            if (_timeSystem != null)
            {
                UnityEngine.Object.DestroyImmediate(_timeSystem.gameObject);
            }

            if (_dataManager != null)
            {
                UnityEngine.Object.DestroyImmediate(_dataManager.gameObject);
            }

            ResetDataManagerForTestsByReflection();
        }

        [Test]
        public void AC_TS_06_NoCrossDay_DirectResolved()
        {
            int pending = 0;
            int resolved = 0;

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => pending++);
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            _timeSystem.Initialize(_now - 300);

            Assert.AreEqual(0, pending);
            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void AC_TS_06b_CrossDay_PendingThenDailyAndResolved()
        {
            int pending = 0;
            int daily = 0;
            int resolved = 0;

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => pending++);
            EventBus.Subscribe(EventNames.OnDailyReset, () => daily++);
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            long last = _now - 86_400 - 60;
            _timeSystem.Initialize(last);
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, pending);
            Assert.AreEqual(1, daily);
            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void AC_TS_07_TimeRollback_OfflineLessOrEqualZero_NoEvent()
        {
            int pending = 0;
            int resolved = 0;

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => pending++);
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            _timeSystem.Initialize(_now + 100);

            Assert.AreEqual(0, pending);
            Assert.AreEqual(0, resolved);
            Assert.AreEqual("Resolved", _timeSystem.GetOfflineStateNameForTests());
        }

        [Test]
        public void AC_TS_08_OfflineCap_Applied()
        {
            long got = -1;
            EventBus.Subscribe<OnOfflineResolvedEvent>(e => got = e.OfflineSeconds);

            _timeSystem.Initialize(_now - (8L * 24L * 3600L));
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(604800L, got);
        }

        [Test]
        public void AC_TS_09_RealtimeDailyReset_PublishedOnce()
        {
            int daily = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => daily++);

            _now += 86_400;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(1, daily);
        }

        [Test]
        public void AC_TS_10_OfflineCrossMultipleDays_DailyResetOnlyOnce()
        {
            int daily = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => daily++);

            _timeSystem.Initialize(_now - 3L * 86_400L);
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, daily);
        }

        [Test]
        public void AC_TS_12_ConfirmIdempotent()
        {
            int resolved = 0;
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            // 跨日才會進入 Pending 狀態，使用者得呼叫 ConfirmOfflineResolution。
            _timeSystem.Initialize(_now - 86_400 - 60);

            _timeSystem.ConfirmOfflineResolution();
            _timeSystem.ConfirmOfflineResolution();
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void AC_TS_14_PauseTick_StopsSecondTick()
        {
            int second = 0;
            EventBus.Subscribe<OnSecondTickEvent>(_ => second++);

            _timeSystem.PauseTick();
            _now += 10;
            _timeSystem.TickForTests(10f);

            Assert.AreEqual(0, second);
        }

        [Test]
        public void AC_TS_15_PauseTick_BlocksRealtimeDailyReset()
        {
            int daily = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => daily++);

            _timeSystem.PauseTick();
            _now += 86_400;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(0, daily);
        }

        [Test]
        public void AC_TS_16_PauseTick_DoesNotBlockOfflineFlow()
        {
            int pending = 0;
            int resolved = 0;

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => pending++);
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            _timeSystem.PauseTick();
            // 跨日後進入 Pending 狀態（FSD-A D-01 後不再以任務驅動 Pending）。
            _timeSystem.Initialize(_now - 86_400 - 60);
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, pending);
            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void AC_TS_17_PauseTick_Idempotent()
        {
            _timeSystem.PauseTick();
            _timeSystem.PauseTick();
            _timeSystem.PauseTick();
            Assert.Pass();
        }

        [Test]
        public void AC_TS_18_PauseTick_NowUTCStillWorks()
        {
            _timeSystem.PauseTick();
            long value = _timeSystem.NowUTC;
            Assert.AreEqual(_now, value);
        }

        [Test]
        public void AC_TS_19_OnMinuteTick_PublishesEvery60Seconds()
        {
            int minute = 0;
            EventBus.Subscribe<OnMinuteTickEvent>(_ => minute++);

            for (int i = 0; i < 120; i++)
            {
                _now += 1;
                _timeSystem.TickForTests(1f);
            }

            Assert.AreEqual(2, minute);
        }

        [Test]
        public void AC_TS_20_OfflineDoesNotBackfillMinuteTick()
        {
            int minute = 0;
            EventBus.Subscribe<OnMinuteTickEvent>(_ => minute++);

            _timeSystem.Initialize(_now - 600);
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(0, minute);
        }

        [Test]
        public void AC_TS_23_CrossYearDailyReset_Works()
        {
            int daily = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => daily++);

            DateTimeOffset dec31 = new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.Zero);
            _now = dec31.ToUnixTimeSeconds();
            _timeSystem.Initialize(_now - 10);

            _now = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(1, daily);
        }

        [Test]
        public void AC_TS_25_SystemConstants_AvailableAtAwake()
        {
            Assert.AreEqual(_now, _timeSystem.NowUTC);
        }

        [Test]
        public void AC_TS_26_SystemConstants_IdempotentRegister_NoErrorPath()
        {
            MethodInfo register = typeof(TimeSystem).GetMethod("RegisterTables", BindingFlags.Static | BindingFlags.NonPublic);
            register.Invoke(null, null);
            register.Invoke(null, null);

            Assert.Pass();
        }

        [Test]
        public void AC_TS_27_ProcessRealtime_CapsCatastrophicDelta()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("catastrophic delta"));

            int second = 0;
            EventBus.Subscribe<OnSecondTickEvent>(_ => second++);

            _now += 600;
            _timeSystem.TickForTests(600f);

            Assert.AreEqual(60, second);
        }

        private static void ResetDataManagerForTestsByReflection()
        {
            MethodInfo method = typeof(DataManager).GetMethod(
                "ResetForTests",
                BindingFlags.Static | BindingFlags.NonPublic);

            method?.Invoke(null, null);
        }

        private static void RegisterSystemConstantsIntoDataManagerForTests()
        {
            MethodInfo setProvider = typeof(DataManager).GetMethod(
                "SetTableTextProviderForTests",
                BindingFlags.Static | BindingFlags.NonPublic);

            Func<string, string> provider = name =>
            {
                if (name != "SystemConstants")
                {
                    return null;
                }

                return "key,DAILY_RESET_HOUR,OFFLINE_MAX_SECONDS\n" +
                       "value,0,604800\n" +
                       "description,utc hour,max offline sec\n";
            };

            setProvider?.Invoke(null, new object[] { provider });

            MethodInfo register = typeof(TimeSystem).GetMethod(
                "RegisterTables",
                BindingFlags.Static | BindingFlags.NonPublic);

            register?.Invoke(null, null);
        }
    }
}
