using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Core.Time
{
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
        public void AC_TS_01_Realtime_60Seconds_ExpireOnce()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);

            _timeSystem.RegisterMission("m1", _now, 60);

            for (int i = 0; i < 60; i++)
            {
                _now += 1;
                _timeSystem.TickForTests(1f);
            }

            Assert.AreEqual(1, expired);
        }

        [Test]
        public void AC_TS_02_Realtime_SameFrameMultiExpire_AllPublished()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);

            _timeSystem.RegisterMission("m1", _now, 10);
            _timeSystem.RegisterMission("m2", _now, 10);

            _now += 10;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(2, expired);
        }

        [Test]
        public void AC_TS_03_RemainingZero_TriggersNextTick()
        {
            string got = null;
            EventBus.Subscribe<OnMissionExpiredEvent>(e => got = e.MissionInstanceId);

            _timeSystem.RegisterMission("m1", _now, 1);
            _now += 1;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual("m1", got);
        }

        [Test]
        public void AC_TS_04_InitializeOnlyPublishesPending_WhenHasExpiredMission()
        {
            int pending = 0;
            int expired = 0;
            int resolved = 0;

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => pending++);
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            _timeSystem.RegisterMission("m1", _now - 600, 60);
            _timeSystem.Initialize(_now - 300);

            Assert.AreEqual(1, pending);
            Assert.AreEqual(0, expired);
            Assert.AreEqual(0, resolved);
        }

        [Test]
        public void AC_TS_05_ConfirmOfflineResolution_OrderAndResolved()
        {
            List<string> seq = new List<string>(4);

            EventBus.Subscribe<OnOfflinePendingEvent>(_ => seq.Add("Pending"));
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => seq.Add("Expired"));
            EventBus.Subscribe(EventNames.OnDailyReset, () => seq.Add("Daily"));
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => seq.Add("Resolved"));

            _timeSystem.RegisterMission("m1", _now - 600, 60);
            _timeSystem.Initialize(_now - 300);
            _timeSystem.ConfirmOfflineResolution();

            CollectionAssert.AreEqual(new[] { "Pending", "Expired", "Resolved" }, seq);
        }

        [Test]
        public void AC_TS_06_NoMissionNoCrossDay_DirectResolved()
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
        public void AC_TS_06b_NoMissionButCrossDay_PendingThenDailyAndResolved()
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
        public void AC_TS_11_ConfirmOrder_ExpiredThenDailyThenResolved()
        {
            List<string> seq = new List<string>(4);
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => seq.Add("Expired"));
            EventBus.Subscribe(EventNames.OnDailyReset, () => seq.Add("Daily"));
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => seq.Add("Resolved"));

            _timeSystem.RegisterMission("m1", _now - 3600, 60);
            _timeSystem.Initialize(_now - 86_400);
            _timeSystem.ConfirmOfflineResolution();

            CollectionAssert.AreEqual(new[] { "Expired", "Daily", "Resolved" }, seq);
        }

        [Test]
        public void AC_TS_12_ConfirmIdempotent()
        {
            int resolved = 0;
            EventBus.Subscribe<OnOfflineResolvedEvent>(_ => resolved++);

            _timeSystem.RegisterMission("m1", _now - 600, 60);
            _timeSystem.Initialize(_now - 300);

            _timeSystem.ConfirmOfflineResolution();
            _timeSystem.ConfirmOfflineResolution();
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void AC_TS_13_RealtimeAndOfflineConsistency_WithinOneSecond()
        {
            int realCount = 0;
            int offlineCount = 0;

            EventBus.Subscribe<OnMissionExpiredEvent>(_ => realCount++);
            _timeSystem.RegisterMission("m1", _now, 10);
            _now += 10;
            _timeSystem.TickForTests(1f);
            _timeSystem.UnregisterMission("m1");

            EventBus.ClearAll();
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => offlineCount++);
            _timeSystem.RegisterMission("m2", _now - 10, 10);
            _timeSystem.Initialize(_now - 10);
            _timeSystem.ConfirmOfflineResolution();

            Assert.AreEqual(1, realCount);
            Assert.AreEqual(1, offlineCount);
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
            _timeSystem.RegisterMission("m1", _now - 600, 60);
            _timeSystem.Initialize(_now - 300);
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
        public void AC_TS_21_MissionExpired_Deduplicated()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);

            _timeSystem.RegisterMission("m1", _now, 1);

            for (int i = 0; i < 3; i++)
            {
                _now += 1;
                _timeSystem.TickForTests(1f);
            }

            Assert.AreEqual(1, expired);
        }

        [Test]
        public void AC_TS_22_OfflineThenRealtime_NoDuplicateMissionExpired()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);

            _timeSystem.RegisterMission("m1", _now - 120, 60);
            _timeSystem.Initialize(_now - 120);
            _timeSystem.ConfirmOfflineResolution();

            _now += 5;
            _timeSystem.TickForTests(5f);

            Assert.AreEqual(1, expired);
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
        public void AC_TS_24_UnregisterClearsDedupSet_AllowsReuseId()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(_ => expired++);

            _timeSystem.RegisterMission("m1", _now, 1);
            _now += 1;
            _timeSystem.TickForTests(1f);

            _timeSystem.UnregisterMission("m1");
            _timeSystem.RegisterMission("m1", _now, 1);
            _now += 1;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(2, expired);
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

        [Test]
        public void AC_TS_28_CheckMissionTimers_HandlerUnregisterDoesNotSkipOthers()
        {
            int expired = 0;
            EventBus.Subscribe<OnMissionExpiredEvent>(e =>
            {
                expired++;
                _timeSystem.UnregisterMission(e.MissionInstanceId);
            });

            _timeSystem.RegisterMission("m1", _now, 10);
            _timeSystem.RegisterMission("m2", _now, 10);
            _timeSystem.RegisterMission("m3", _now, 10);

            _now += 10;
            _timeSystem.TickForTests(1f);

            Assert.AreEqual(3, expired);
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

                return "key,value,description\n" +
                       "DAILY_RESET_HOUR,0,utc hour\n" +
                       "OFFLINE_MAX_SECONDS,604800,max offline sec\n";
            };

            setProvider?.Invoke(null, new object[] { provider });

            MethodInfo register = typeof(TimeSystem).GetMethod(
                "RegisterTables",
                BindingFlags.Static | BindingFlags.NonPublic);

            register?.Invoke(null, null);
        }
    }
}
