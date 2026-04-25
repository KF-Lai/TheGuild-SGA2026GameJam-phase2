using NUnit.Framework;
using TheGuild.Core.Events;

namespace Tests.EditMode.Core.Events
{
    public sealed class EventBusTests
    {
        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
        }

        [Test]
        public void AC_EB_01_GenericSubscribePublish_Works()
        {
            int count = 0;
            EventBus.Subscribe<OnSecondTickEvent>(_ => count++);
            EventBus.Publish(new OnSecondTickEvent(123));

            Assert.AreEqual(1, count);
        }

        [Test]
        public void AC_EB_02_Unsubscribe_Works()
        {
            int count = 0;
            System.Action<OnSecondTickEvent> handler = _ => count++;

            EventBus.Subscribe(handler);
            EventBus.Unsubscribe(handler);
            EventBus.Publish(new OnSecondTickEvent(1));

            Assert.AreEqual(0, count);
        }

        [Test]
        public void AC_EB_03_StringEvent_Works()
        {
            int count = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => count++);
            EventBus.Publish(EventNames.OnDailyReset);

            Assert.AreEqual(1, count);
        }

        [Test]
        public void AC_EB_04_MultiSubscriber_OrderIsFIFO()
        {
            string order = string.Empty;
            EventBus.Subscribe(EventNames.OnDailyReset, () => order += "A");
            EventBus.Subscribe(EventNames.OnDailyReset, () => order += "B");
            EventBus.Publish(EventNames.OnDailyReset);

            Assert.AreEqual("AB", order);
        }

        [Test]
        public void AC_EB_05_PublisherDoesNotClearOtherSubscribers()
        {
            int count = 0;
            EventBus.Subscribe(EventNames.OnDailyReset, () => count++);

            EventBus.Publish(EventNames.OnDailyReset);
            EventBus.Publish(EventNames.OnDailyReset);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void AC_EB_PublishReentrant_DoesNotCorruptOuterIteration()
        {
            int handlerACount = 0;
            int handlerBCount = 0;
            bool reentered = false;

            System.Action<TestEvent> handlerA = _ =>
            {
                handlerACount++;
                if (!reentered)
                {
                    reentered = true;
                    EventBus.Publish(new TestEvent());
                }
            };

            System.Action<TestEvent> handlerB = _ => handlerBCount++;

            EventBus.Subscribe(handlerA);
            EventBus.Subscribe(handlerB);

            EventBus.Publish(new TestEvent());

            Assert.AreEqual(2, handlerACount);
            Assert.AreEqual(2, handlerBCount);
        }

        [Test]
        public void AC_EB_PublishHandlerSelfUnsubscribe_DoesNotSkipOthers()
        {
            int aCount = 0;
            int bCount = 0;
            int cCount = 0;
            System.Action<TestEvent> handlerA = _ => aCount++;
            System.Action<TestEvent> handlerC = _ => cCount++;
            System.Action<TestEvent> handlerB = null;
            handlerB = _ =>
            {
                bCount++;
                EventBus.Unsubscribe(handlerB);
            };

            EventBus.Subscribe(handlerA);
            EventBus.Subscribe(handlerB);
            EventBus.Subscribe(handlerC);

            EventBus.Publish(new TestEvent());

            Assert.AreEqual(1, aCount);
            Assert.AreEqual(1, bCount);
            Assert.AreEqual(1, cCount);
        }

        private readonly struct TestEvent
        {
        }
    }
}
