using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Gameplay.Save;
using TheGuild.Gameplay.Save.Events;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.Save
{
    public sealed class SaveLoadServiceTests
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private GameObject _go;
        private SaveLoadService _service;
        private string _tempDir;
        private TestEventCollector _events;

        [SetUp]
        public void SetUp()
        {
            typeof(EventBus).GetMethod("ClearAll", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(null, null);
            _tempDir = Path.Combine(Path.GetTempPath(), "ft10-save-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _events = new TestEventCollector();
            _events.Subscribe();

            _go = new GameObject("SaveLoadServiceTests");
            _service = _go.AddComponent<SaveLoadService>();
            SetField(_service, "_directoryPath", _tempDir);
            SetField(_service, "_saveFileName", "save.json");
            SetField(_service, "_backupPrefix", "save.backup");
            SetField(_service, "_backupCount", 3);
            SetField(_service, "_saveIntervalSeconds", 60f);
            SetField(_service, "_lastSaveRealtime", 0f);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Unsubscribe();
            typeof(EventBus).GetMethod("ClearAll", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(null, null);

            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }

            if (Directory.Exists(_tempDir))
            {
                foreach (string file in Directory.GetFiles(_tempDir))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_tempDir, true);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoD01_MarkDirty_DoesNotWriteImmediately()
        {
            InjectSaveables(new FakeSaveable("f03Resources", true));
            _service.MarkDirty();

            InvokePrivate(_service, "Update");

            Assert.AreEqual(0, _events.SaveCompletedCount);
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "save.json")));
        }

        [Test]
        public void DoD02_NodeIntervalElapsed_WritesWhenDirty()
        {
            InjectSaveables(new FakeSaveable("f03Resources", true));
            _service.MarkDirty();
            SetField(_service, "_lastSaveRealtime", -1000f);

            InvokePrivate(_service, "Update");

            Assert.AreEqual(1, _events.SaveCompletedCount);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "save.json")));
        }

        [Test]
        public void DoD03_ForceSave_BypassesDirtyCheck()
        {
            InjectSaveables(new FakeSaveable("f03Resources", true));
            _service.ForceSave();
            Assert.AreEqual(1, _events.SaveCompletedCount);
        }

        [Test]
        public void DoD04_ConcurrentExecuteSave_BlockedByMutex()
        {
            InjectSaveables(new FakeSaveable("f03Resources", true));
            SetField(_service, "_isSaving", true);
            LogAssert.Expect(LogType.Warning, "[SaveLoadService] ExecuteSave skipped: already saving.");

            _service.ForceSave();

            Assert.AreEqual(0, _events.SaveCompletedCount);
        }

        [Test]
        public void DoD05_Critical_RestoreFailure_RetriesNextBackup()
        {
            FakeSaveable critical = new FakeSaveable("f03Resources", true)
            {
                ThrowOnRestoreWhenJsonContains = "candidate0"
            };

            FakeSaveable staff = new FakeSaveable("ft12Staff", false);

            File.WriteAllText(Path.Combine(_tempDir, "save.json"), BuildRootJson("{\"token\":\"candidate0\"}", "{}"));
            File.WriteAllText(Path.Combine(_tempDir, "save.backup1.json"), BuildRootJson("{\"token\":\"candidate1\"}", "{}"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Critical failure on candidate 0"));
            BootstrapResult result = SaveLoadBootstrap.Execute(
                new List<ISaveable> { critical, staff },
                _tempDir,
                "save.json",
                "save.backup",
                3);

            Assert.AreEqual(1, result.LoadedFromBackupIndex);
        }

        [Test]
        public void DoD06_Degradable_RestoreFailure_FallsBack()
        {
            FakeSaveable critical = new FakeSaveable("f03Resources", true);
            FakeSaveable degradable = new FakeSaveable("factionStorySaveData", false)
            {
                ThrowAlwaysOnRestore = true
            };

            File.WriteAllText(Path.Combine(_tempDir, "save.json"), BuildRootJson("{}", "{}"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Degradable fallback owner=factionStorySaveData"));
            BootstrapResult result = SaveLoadBootstrap.Execute(
                new List<ISaveable> { critical, degradable },
                _tempDir,
                "save.json",
                "save.backup",
                3);

            Assert.AreEqual(0, result.LoadedFromBackupIndex);
            Assert.AreEqual(1, degradable.InitializeCount);
        }

        [Test]
        public void DoD07_AtomicWrite_OnFailure_PreservesOriginal()
        {
            string savePath = Path.Combine(_tempDir, "save.json");
            File.WriteAllText(savePath, "ORIGINAL");
            File.SetAttributes(savePath, FileAttributes.ReadOnly);

            InjectSaveables(new FakeSaveable("f03Resources", true));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[SaveFileIO\] Write failed"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[SaveLoadService\] Save failed"));

            _service.ForceSave();

            Assert.AreEqual("ORIGINAL", File.ReadAllText(savePath));
        }

        [Test]
        public void DoD08_AllBackupsFailed_PublishesLoadFailedThenLoadCompletedNeg1()
        {
            File.WriteAllText(Path.Combine(_tempDir, "save.json"), "{broken");
            File.WriteAllText(Path.Combine(_tempDir, "save.backup1.json"), "{broken");
            InjectSaveables(new FakeSaveable("f03Resources", true));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Parse failed"));
            // Bootstrap Step X 在無 AdventurerRoster.Instance 時 LogError「奧菲莉雅初始化失敗」並繼續；測試環境未注入 C-02，必然觸發。
            LogAssert.Expect(LogType.Error, "[FT-10] Bootstrap Step X: AdventurerRoster.Instance 為 null，無法初始化奧菲莉雅。Bootstrap 繼續。");
            _service.Bootstrap();

            Assert.AreEqual("LoadFailed", _events.EventOrder[0]);
            Assert.AreEqual("LoadCompleted", _events.EventOrder[1]);
            Assert.AreEqual(-1, _events.LastLoadedFromBackupIndex);
        }

        [Test]
        public void DoD09_OnGameOver_TriggersSealAndOnGameSealed()
        {
            InjectSaveables(new FakeSaveable("f03Resources", true));
            _service.OnGameOver();
            Assert.AreEqual(1, _events.GameSealedCount);
        }

        [Test]
        public void DoD10_ResetToNewGame_DeletesSaveAndPublishesEvent()
        {
            File.WriteAllText(Path.Combine(_tempDir, "save.json"), "{}");
            _service.ResetToNewGame();
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "save.json")));
            Assert.AreEqual(1, _events.SaveDeletedCount);
        }

        [Test]
        public void EC03_CriticalRetryChain_ResetsOwnersBetweenCandidates()
        {
            FakeSaveable critical = new FakeSaveable("f03Resources", true)
            {
                ThrowAlwaysOnRestore = true
            };
            FakeSaveable other = new FakeSaveable("ft01Recruitment", false);

            File.WriteAllText(Path.Combine(_tempDir, "save.json"), BuildRootJson("{}", "{}"));
            File.WriteAllText(Path.Combine(_tempDir, "save.backup1.json"), BuildRootJson("{}", "{}"));

            SaveLoadBootstrap.Execute(
                new List<ISaveable> { critical, other },
                _tempDir,
                "save.json",
                "save.backup",
                3);

            Assert.GreaterOrEqual(critical.InitializeCount, 1);
            Assert.GreaterOrEqual(other.InitializeCount, 1);
        }

        [Test]
        public void EC10_OwnerJsonNull_TriggersInitializeAsNewGame()
        {
            FakeSaveable degradable = new FakeSaveable("factionStorySaveData", false)
            {
                InitializeWhenOwnerJsonNull = true
            };

            File.WriteAllText(
                Path.Combine(_tempDir, "save.json"),
                "{\"schemaMeta\":{\"schemaVersion\":\"1\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"gameOverState\":\"Active\",\"lastActiveTimestamp\":0}}"
            );

            SaveLoadBootstrap.Execute(new List<ISaveable> { degradable }, _tempDir, "save.json", "save.backup", 3);
            Assert.AreEqual(1, degradable.InitializeCount);
        }

        [Test]
        public void Test_v31_AC_FT10_RegisterOphelia_OnNewGame()
        {
            FakeSaveable f03Resources = new FakeSaveable("f03Resources", true);
            FakeSaveable c02Roster = new FakeSaveable("c02Roster", false)
            {
                InitializeWhenOwnerJsonNull = true
            };

            File.WriteAllText(
                Path.Combine(_tempDir, "save.json"),
                "{\"schemaMeta\":{\"schemaVersion\":\"1\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"gameOverState\":\"Active\",\"lastActiveTimestamp\":0}}"
            );

            SaveLoadBootstrap.Execute(
                new List<ISaveable> { f03Resources, c02Roster },
                _tempDir,
                "save.json",
                "save.backup",
                3);

            Assert.AreEqual(1, c02Roster.InitializeCount, "C-02 roster should be initialized on new game");
        }

        [Test]
        public void Test_v31_AC_FT10_PersistFactionStoryFields()
        {
            FakeSaveable factionStory = new FakeSaveable("factionStorySaveData", false);
            FakeSaveable f03Resources = new FakeSaveable("f03Resources", true);

            string jsonWithFields = BuildRootJson(
                "{}",
                "{\"factionStoryV31_pendingMissingNight\":true,\"_totalAdventurerDeaths\":5,\"_blockedStages\":\"2,3\"}"
            );
            File.WriteAllText(Path.Combine(_tempDir, "save.json"), jsonWithFields);

            BootstrapResult result = SaveLoadBootstrap.Execute(
                new List<ISaveable> { f03Resources, factionStory },
                _tempDir,
                "save.json",
                "save.backup",
                3);

            Assert.AreEqual(0, result.LoadedFromBackupIndex, "Should load from primary save");
        }

        private void InjectSaveables(params ISaveable[] saveables)
        {
            SetField(_service, "_saveables", new List<ISaveable>(saveables));
        }

        private static string BuildRootJson(string f03Json, string staffJson)
        {
            return "{"
                + "\"schemaMeta\":{\"schemaVersion\":\"1\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"gameOverState\":\"Active\",\"lastActiveTimestamp\":0},"
                + "\"f03Resources\":\"" + Escape(f03Json) + "\","
                + "\"ft12Staff\":\"" + Escape(staffJson) + "\""
                + "}";
        }

        private static string Escape(string raw)
        {
            return raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, AnyInstance);
            Assert.IsNotNull(method, $"Method not found: {methodName}");
            method.Invoke(target, null);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, AnyInstance);
            Assert.IsNotNull(field, $"Field not found: {fieldName}");
            field.SetValue(target, value);
        }

        private sealed class FakeSaveable : ISaveable
        {
            public FakeSaveable(string ownerKey, bool isCritical)
            {
                OwnerKey = ownerKey;
                IsCritical = isCritical;
            }

            public string OwnerKey { get; }
            public bool IsCritical { get; }
            public bool ThrowAlwaysOnRestore { get; set; }
            public string ThrowOnRestoreWhenJsonContains { get; set; }
            public bool InitializeWhenOwnerJsonNull { get; set; }
            public int InitializeCount { get; private set; }

            public string Serialize()
            {
                return "{}";
            }

            public void RestoreFromSave(string ownerJson)
            {
                if (InitializeWhenOwnerJsonNull && string.IsNullOrEmpty(ownerJson))
                {
                    InitializeAsNewGame();
                    return;
                }

                if (ThrowAlwaysOnRestore)
                {
                    throw new InvalidOperationException($"restore fail: {OwnerKey}");
                }

                if (!string.IsNullOrEmpty(ThrowOnRestoreWhenJsonContains)
                    && ownerJson != null
                    && ownerJson.Contains(ThrowOnRestoreWhenJsonContains, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"restore fail marker: {OwnerKey}");
                }
            }

            public void InitializeAsNewGame()
            {
                InitializeCount++;
            }
        }

        private sealed class TestEventCollector
        {
            private Action<OnSaveCompletedEvent> _onSaveCompleted;
            private Action<OnLoadFailedEvent> _onLoadFailed;
            private Action<OnLoadCompletedEvent> _onLoadCompleted;
            private Action<OnGameSealedEvent> _onGameSealed;
            private Action<OnSaveDeletedEvent> _onSaveDeleted;

            public int SaveCompletedCount { get; private set; }
            public int GameSealedCount { get; private set; }
            public int SaveDeletedCount { get; private set; }
            public int LastLoadedFromBackupIndex { get; private set; } = int.MinValue;
            public List<string> EventOrder { get; } = new List<string>();

            public void Subscribe()
            {
                _onSaveCompleted = _ =>
                {
                    SaveCompletedCount++;
                    EventOrder.Add("SaveCompleted");
                };
                _onLoadFailed = _ => EventOrder.Add("LoadFailed");
                _onLoadCompleted = evt =>
                {
                    LastLoadedFromBackupIndex = evt.LoadedFromBackupIndex;
                    EventOrder.Add("LoadCompleted");
                };
                _onGameSealed = _ =>
                {
                    GameSealedCount++;
                    EventOrder.Add("GameSealed");
                };
                _onSaveDeleted = _ =>
                {
                    SaveDeletedCount++;
                    EventOrder.Add("SaveDeleted");
                };

                EventBus.Subscribe(_onSaveCompleted);
                EventBus.Subscribe(_onLoadFailed);
                EventBus.Subscribe(_onLoadCompleted);
                EventBus.Subscribe(_onGameSealed);
                EventBus.Subscribe(_onSaveDeleted);
            }

            public void Unsubscribe()
            {
                if (_onSaveCompleted != null) EventBus.Unsubscribe(_onSaveCompleted);
                if (_onLoadFailed != null) EventBus.Unsubscribe(_onLoadFailed);
                if (_onLoadCompleted != null) EventBus.Unsubscribe(_onLoadCompleted);
                if (_onGameSealed != null) EventBus.Unsubscribe(_onGameSealed);
                if (_onSaveDeleted != null) EventBus.Unsubscribe(_onSaveDeleted);
            }
        }
    }
}
