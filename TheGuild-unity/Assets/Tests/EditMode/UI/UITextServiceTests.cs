using System;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Data;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode.UI
{
    public sealed class UITextServiceTests
    {
        private GameObject _dataGo;
        private GameObject _serviceGo;
        private DataManager _data;
        private UITextService _service;

        [SetUp]
        public void SetUp()
        {
            InvokeStatic(typeof(DataManager), "ResetForTests");
            InvokeStatic(
                typeof(DataManager),
                "SetTableTextProviderForTests",
                new[] { typeof(Func<string, string>) },
                (Func<string, string>)(name => name == "UIText"
                    ? "key,hello.key\nzhTW,你好\nen,Hello\n"
                    : null));

            DataManager.RegisterTable<UITextService.UITextRow>("UIText");
            _dataGo = new GameObject("DataManager_UI_Test");
            _data = _dataGo.AddComponent<DataManager>();
            InvokeInstance(_data, "InitializeForTests");

            _serviceGo = new GameObject("UITextService_Test");
            _service = _serviceGo.AddComponent<UITextService>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_serviceGo);
            Object.DestroyImmediate(_dataGo);
            InvokeStatic(typeof(DataManager), "ResetForTests");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoD_A8_LookupHitReturnsZhTW()
        {
            _service.Initialize();

            Assert.AreEqual("你好", _service.Lookup("hello.key"));
        }

        [Test]
        public void DoD_A8_EC19_MissingKeyLogsAndReturnsFallbackOrKey()
        {
            _service.Initialize();

            LogAssert.Expect(LogType.Error, "[UITextService] Missing UIText key: missing.key");
            Assert.AreEqual("missing.key", _service.Lookup("missing.key"));

            LogAssert.Expect(LogType.Error, "[UITextService] Missing UIText key: missing.key");
            Assert.AreEqual("fallback", _service.Lookup("missing.key", "fallback"));
        }

        [Test]
        public void EC02_DataManagerMissing_InitializeLogsErrorAndLookupFallsBack()
        {
            Object.DestroyImmediate(_dataGo);
            _dataGo = null;
            InvokeStatic(typeof(DataManager), "ResetForTests");

            LogAssert.Expect(LogType.Error, "[UITextService] DataManager.Instance is null.");
            _service.Initialize();
            LogAssert.Expect(LogType.Warning, "[UITextService] Lookup called before Initialize.");

            Assert.AreEqual("fallback", _service.Lookup("any.key", "fallback"));
        }

        private static void InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, args);
        }

        private static void InvokeStatic(Type type, string methodName, Type[] parameterTypes, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
            Assert.IsNotNull(method);
            method.Invoke(null, args);
        }

        private static void InvokeInstance(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(target, args);
        }
    }
}
