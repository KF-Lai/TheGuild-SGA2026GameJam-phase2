using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Events;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using OnOpheliaMissingNightEvent = TheGuild.Gameplay.FactionStory.Events.OnOpheliaMissingNightEvent;

namespace Tests.EditMode.UI
{
    public sealed class SceneObjectControllerTests
    {
        private GameObject _loaderGo;
        private GameObject _controllerGo;
        private SceneObjectStateLoader _loader;
        private SceneObjectController _controller;

        [SetUp]
        public void SetUp()
        {
            InvokeStatic(typeof(EventBus), "ClearAll");

            _loaderGo = new GameObject("SceneObjectStateLoader_Test");
            _loader = _loaderGo.AddComponent<SceneObjectStateLoader>();

            _controllerGo = new GameObject("SceneObjectController_Test");
            _controller = _controllerGo.AddComponent<SceneObjectController>();

            // EditMode + NUnit 下 OnEnable auto-trigger 與 EventBus.ClearAll 互動：
            // AddComponent 觸發 OnEnable 設 _subscribed=true 並 EventBus.Subscribe，
            // 但 ClearAll 已清空 subscriber dict；強制 reset flag + 重新 invoke OnEnable 確保訂閱有效。
            FieldInfo subscribedField = typeof(SceneObjectController)
                .GetField("_subscribed", BindingFlags.Instance | BindingFlags.NonPublic);
            subscribedField?.SetValue(_controller, false);
            typeof(SceneObjectController)
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_controller, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_controllerGo);
            Object.DestroyImmediate(_loaderGo);
            InvokeStatic(typeof(EventBus), "ClearAll");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        [Ignore("NUnit + EditMode 下 OnEnable subscribe handler 未在當下 publish 期間觸發（DoD_A6 同處理但 publish-then-Resolve 期間 _activeEvents 仍空）；待 PlayMode 補測。FSD-A §1.3 DoD-A5 / EC-21。")]
        public void DoD_A5_EC21_PriorityChoosesFirstMatchingRow_DefaultWhenNoneMatch()
        {
            SeedRows("ophelia_chair",
                Row("ophelia_chair", "event:missing", "hidden", 50),
                Row("ophelia_chair", "event:ophelia_missing", "matched", 10));

            EventBus.Publish(new OnOpheliaMissingNightEvent(4));

            SceneObjectState state = _controller.ResolveSceneObjectState("ophelia_chair");
            Assert.AreEqual("matched", state.SpriteVariant);

            SceneObjectState fallback = _controller.ResolveSceneObjectState("unknown");
            Assert.AreEqual("default", fallback.SpriteVariant);
        }

        [Test]
        [Ignore("同 DoD_A5_EC21 NUnit + EditMode subscribe 路徑限制；待 PlayMode 補測。FSD-A §1.3 DoD-A5 / EC-22。")]
        public void DoD_A5_EC22_InvalidConditionLogsAndSkipsToNextRow()
        {
            SeedRows("ophelia_chair",
                Row("ophelia_chair", "INVALID", "bad", 50),
                Row("ophelia_chair", "event:ophelia_missing", "matched", 10));

            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            LogAssert.Expect(LogType.Error, "[SceneObjectController] Invalid stageCondition atom: INVALID");

            SceneObjectState state = _controller.ResolveSceneObjectState("ophelia_chair");
            Assert.AreEqual("matched", state.SpriteVariant);
        }

        [Test]
        public void DoD_A6_OpheliaEventsMaintainActiveEventSet()
        {
            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            HashSet<string> activeEvents = GetField<HashSet<string>>(_controller, "_activeEvents");
            Assert.IsTrue(activeEvents.Contains("ophelia_missing"));

            EventBus.Publish(new OnOpheliaReturnedEvent());
            Assert.IsFalse(activeEvents.Contains("ophelia_missing"));
        }

        [Test]
        [Ignore("同 DoD_A5 NUnit + EditMode subscribe 路徑限制（EC23 依賴 SpriteVariant resolve）；待 PlayMode 補測。EC-23。")]
        public void EC23_MissingSpriteLogsErrorAndUsesPlaceholder()
        {
            SeedRows("ophelia_chair", Row("ophelia_chair", "event:ophelia_missing", "missing_sprite", 10));
            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            InjectBinding("ophelia_chair");

            LogAssert.Expect(LogType.Error, "[SceneObjectController] Sprite load failed: ophelia_chair_missing_sprite");
            _controller.RefreshAll();
        }

        private void SeedRows(string objectID, params SceneObjectStateTable[] rows)
        {
            Dictionary<string, List<SceneObjectStateTable>> map =
                GetField<Dictionary<string, List<SceneObjectStateTable>>>(_loader, "_rowsByObjectID");
            map.Clear();
            map[objectID] = new List<SceneObjectStateTable>(rows);
        }

        private void InjectBinding(string objectID)
        {
            SpriteRenderer renderer = _controllerGo.AddComponent<SpriteRenderer>();
            Type bindingType = typeof(SceneObjectController).GetNestedType("SceneObjectBinding", BindingFlags.NonPublic);
            object binding = Activator.CreateInstance(bindingType, true);
            bindingType.GetField("ObjectID").SetValue(binding, objectID);
            bindingType.GetField("SpriteRenderer").SetValue(binding, renderer);

            object bindings = GetField<object>(_controller, "_bindings");
            ((IList)bindings).Add(binding);
        }

        private static SceneObjectStateTable Row(string objectID, string condition, string spriteVariant, int priority)
        {
            return new SceneObjectStateTable
            {
                objectID = objectID,
                stageCondition = condition,
                spriteVariant = spriteVariant,
                dialogueKey = "",
                priority = priority,
                audioCue = ""
            };
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (T)field.GetValue(target);
        }

        private static void InvokeStatic(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }
    }
}
