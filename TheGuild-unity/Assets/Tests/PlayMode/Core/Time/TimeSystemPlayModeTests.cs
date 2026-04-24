using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Core.Time
{
    public sealed class TimeSystemPlayModeTests
    {
        private DataManager _dm;
        private GameObject _dmGo;
        private TimeSystem _timeSystem;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            TimeSystem.ResetTestHooks();

            typeof(DataManager).GetMethod("ResetForTests", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            typeof(DataManager).GetMethod("SetTableTextProviderForTests", BindingFlags.Static | BindingFlags.NonPublic).Invoke(
                null,
                new object[]
                {
                    (Func<string, string>)(name =>
                        name == "SystemConstants"
                            ? "key,value,description\nDAILY_RESET_HOUR,0,h\nOFFLINE_MAX_SECONDS,604800,c\n"
                            : null)
                });
            DataManager.RegisterSystemConstantsTable("SystemConstants");
            _dmGo = new GameObject("DM_TS_Play");
            _dm = _dmGo.AddComponent<DataManager>();
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

            if (_dmGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_dmGo);
            }

            _dm = null;
            _dmGo = null;
            typeof(DataManager).GetMethod("ResetForTests", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }

        [UnityTest]
        public IEnumerator PlayMode_OnSecondTick_IsDrivenByUpdate()
        {
            int second = 0;
            EventBus.Subscribe<OnSecondTickEvent>(_ => second++);

            GameObject go = new GameObject("TimeSystem_Play_Second");
            _timeSystem = go.AddComponent<TimeSystem>();

            yield return new WaitForSecondsRealtime(1.2f);

            Assert.GreaterOrEqual(second, 1);
        }

        [UnityTest]
        public IEnumerator PlayMode_OnMinuteTick_Rhythm()
        {
            int minute = 0;
            EventBus.Subscribe<OnMinuteTickEvent>(_ => minute++);

            TimeSystem.SetDeltaProviderForTests(() => 1f);
            GameObject go = new GameObject("TimeSystem_Play_Minute");
            _timeSystem = go.AddComponent<TimeSystem>();

            for (int i = 0; i < 61; i++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(minute, 1);
        }

        [UnityTest]
        public IEnumerator PlayMode_PauseTick_StopsTicks()
        {
            int second = 0;
            EventBus.Subscribe<OnSecondTickEvent>(_ => second++);

            GameObject go = new GameObject("TimeSystem_Play_Pause");
            _timeSystem = go.AddComponent<TimeSystem>();

            yield return new WaitForSecondsRealtime(1.1f);
            int beforePause = second;

            _timeSystem.PauseTick();
            yield return new WaitForSecondsRealtime(1.1f);

            Assert.AreEqual(beforePause, second);
        }
    }
}
