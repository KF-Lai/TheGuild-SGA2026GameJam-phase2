using System.Collections;
using NUnit.Framework;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Core.Time
{
    public sealed class TimeSystemPlayModeTests
    {
        private TimeSystem _timeSystem;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            TimeSystem.ResetTestHooks();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            TimeSystem.ResetTestHooks();

            if (_timeSystem != null)
            {
                Object.DestroyImmediate(_timeSystem.gameObject);
            }
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
