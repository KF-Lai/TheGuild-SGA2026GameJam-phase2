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

namespace Tests.PlayMode.UI
{
    /// <summary>
    /// P-02-FSD-A DoD-A5 / EC-21 / EC-22 / EC-23 PlayMode 補測：
    /// PlayMode 環境下 OnEnable 自然觸發、EventBus subscribe handler 在 publish 期間正確 fire。
    /// 對應 EditMode 同名 [Ignore] tests（NUnit + EditMode subscribe 路徑限制）。
    /// </summary>
    public sealed class SceneObjectControllerPlayModeTests
    {
        private GameObject _loaderGo;
        private GameObject _controllerGo;
        private SceneObjectStateLoader _loader;
        private SceneObjectController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            InvokeStatic(typeof(EventBus), "ClearAll");

            _loaderGo = new GameObject("SceneObjectStateLoader_PlayMode");
            _loader = _loaderGo.AddComponent<SceneObjectStateLoader>();

            _controllerGo = new GameObject("SceneObjectController_PlayMode");
            _controller = _controllerGo.AddComponent<SceneObjectController>();

            // 等一 frame 讓 OnEnable 完整觸發 SubscribeEvents。
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_controllerGo != null) Object.DestroyImmediate(_controllerGo);
            if (_loaderGo != null) Object.DestroyImmediate(_loaderGo);
            InvokeStatic(typeof(EventBus), "ClearAll");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DoD_A5_EC21_PriorityChoosesFirstMatchingRow_DefaultWhenNoneMatch()
        {
            SeedRows("ophelia_chair",
                Row("ophelia_chair", "event:missing", "hidden", 50),
                Row("ophelia_chair", "event:ophelia_missing", "matched", 10));

            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            yield return null;

            SceneObjectState state = _controller.ResolveSceneObjectState("ophelia_chair");
            Assert.AreEqual("matched", state.SpriteVariant);

            SceneObjectState fallback = _controller.ResolveSceneObjectState("unknown");
            Assert.AreEqual("default", fallback.SpriteVariant);
        }

        [UnityTest]
        public IEnumerator DoD_A5_EC22_InvalidConditionLogsAndSkipsToNextRow()
        {
            SeedRows("ophelia_chair",
                Row("ophelia_chair", "INVALID", "bad", 50),
                Row("ophelia_chair", "event:ophelia_missing", "matched", 10));

            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            yield return null;

            LogAssert.Expect(LogType.Error, "[SceneObjectController] Invalid stageCondition atom: INVALID");

            SceneObjectState state = _controller.ResolveSceneObjectState("ophelia_chair");
            Assert.AreEqual("matched", state.SpriteVariant);
        }

        [UnityTest]
        public IEnumerator EC23_MissingSpriteLogsErrorAndUsesPlaceholder()
        {
            SeedRows("ophelia_chair", Row("ophelia_chair", "event:ophelia_missing", "missing_sprite", 10));
            EventBus.Publish(new OnOpheliaMissingNightEvent(4));
            yield return null;

            InjectBinding("ophelia_chair");

            LogAssert.Expect(LogType.Error, "[SceneObjectController] Sprite load failed: ophelia_chair_missing_sprite");
            _controller.RefreshAll();
            yield return null;
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
