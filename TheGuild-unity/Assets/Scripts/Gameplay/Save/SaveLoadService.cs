using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.FactionStory.Events;
using TheGuild.Gameplay.Gacha.Events;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.Outcome.Events;
using TheGuild.Gameplay.Resources.Events;
using TheGuild.Gameplay.Save.Events;
using TheGuild.Gameplay.WorldDanger.Events;
using UnityEngine;

namespace TheGuild.Gameplay.Save
{
    [DefaultExecutionOrder(-100)]
    public sealed class SaveLoadService : MonoBehaviour, ISaveLoadService
    {
        private const float DefaultAutoIntervalSec = 60f;
        private const int DefaultBackupCount = 3;
        private const string DefaultSaveName = "save.json";
        private const string DefaultBackupPrefix = "save.backup";
        private const string DefaultGameOverPrefix = "save.over";

        private static SaveLoadService _instance;

        private List<ISaveable> _saveables = new List<ISaveable>(16);
        private readonly List<Action> _eventUnsubscribers = new List<Action>(16);

        private float _saveIntervalSeconds = DefaultAutoIntervalSec;
        private float _lastSaveRealtime;
        private bool _isDirty;
        private bool _isSaving;

        private int _backupCount = DefaultBackupCount;
        private string _directoryPath;
        private string _saveFileName = DefaultSaveName;
        private string _backupPrefix = DefaultBackupPrefix;
        private string _gameOverPrefix = DefaultGameOverPrefix;

        private void Awake()
        {
            if (!EnsureSingleton())
            {
                return;
            }

            DontDestroyOnLoad(gameObject);
            InitTuning();
            SubscribeAllEvents();
        }

        private void Start()
        {
            if (_instance != this)
            {
                return;
            }

            DiscoverSaveables();
            Bootstrap();
        }

        private void Update()
        {
            if (!_isDirty)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastSaveRealtime < _saveIntervalSeconds)
            {
                return;
            }

            ExecuteSave(false);
        }

        private void OnApplicationQuit()
        {
            ForceSave();
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void ForceSave()
        {
            ExecuteSave(true);
        }

        public bool ExecuteSave(bool force = false)
        {
            if (!force && !_isDirty)
            {
                return false;
            }

            if (_isSaving)
            {
                Debug.LogWarning("[SaveLoadService] ExecuteSave skipped: already saving.");
                return false;
            }

            _isSaving = true;
            try
            {
                SaveDataRoot root = BuildRoot();
                string content = JsonUtility.ToJson(root);
                bool ok = SaveFileIO.WriteSaveWithRotation(
                    _directoryPath,
                    _saveFileName,
                    _backupPrefix,
                    _backupCount,
                    content,
                    out Exception ex);

                if (!ok)
                {
                    EventBus.Publish(new OnSaveFailedEvent(ex != null ? ex.Message : "unknown"));
                    Debug.LogError($"[SaveLoadService] Save failed: {ex?.Message}");
                    return false;
                }

                _isDirty = false;
                _lastSaveRealtime = Time.realtimeSinceStartup;
                EventBus.Publish(new OnSaveCompletedEvent(SaveFileIO.BuildSavePath(_directoryPath, _saveFileName)));
                return true;
            }
            finally
            {
                _isSaving = false;
            }
        }

        public void Bootstrap()
        {
            BootstrapResult result = SaveLoadBootstrap.Execute(_saveables, _directoryPath, _saveFileName, _backupPrefix, _backupCount);

            // === v3.1 patch P3.1-006：奧菲莉雅初始化 Step X ===
            // 位置：Bootstrap 完成後、OnLoadCompleted 發布前
            // 觸發條件：無存檔（首次遊玩）或全 backup 失敗（LoadedFromBackupIndex == -1），代表各 owner 已走 InitializeAsNewGame() 路徑
            // 目的：新遊戲開局即將奧菲莉雅（templateID=OPHELIA_TEMPLATE_ID）放入名冊，繞過容量上限
            // 規範源：GDD FT-10 §3.B Step X；P3.1-006 §3.2.1
            if (result.LoadedFromBackupIndex == -1)
            {
                try
                {
                    int opheliaTemplateID = ReadIntOrDefault("OPHELIA_TEMPLATE_ID", 901);
                    if (AdventurerRoster.Instance != null)
                    {
                        bool registered = AdventurerRoster.Instance.RegisterUniqueAdventurer(opheliaTemplateID);
                        if (!registered)
                        {
                            Debug.LogError($"[FT-10] Bootstrap Step X: RegisterUniqueAdventurer({opheliaTemplateID}) 失敗——無法將奧菲莉雅放入名冊。Bootstrap 繼續。");
                        }
                    }
                    else
                    {
                        Debug.LogError("[FT-10] Bootstrap Step X: AdventurerRoster.Instance 為 null，無法初始化奧菲莉雅。Bootstrap 繼續。");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FT-10] Bootstrap Step X: RegisterUniqueAdventurer 發生例外，Bootstrap 繼續。ex={ex.Message}");
                }
            }

            if (result.Failure != null)
            {
                EventBus.Publish(new OnLoadFailedEvent(result.Failure.Message));
            }

            EventBus.Publish(new OnLoadCompletedEvent(result.LoadedFromBackupIndex));

            if (result.IsGameOver)
            {
                OnGameOver();
            }
        }

        public void OnGameOver()
        {
            ForceSave();

            string terminal = SaveFileIO.BuildSavePath(_directoryPath, _saveFileName);
            string stamped = System.IO.Path.Combine(
                _directoryPath,
                $"{_gameOverPrefix}_{DateTime.UtcNow:yyyyMMddHHmmss}.json");

            if (System.IO.File.Exists(terminal))
            {
                System.IO.File.Copy(terminal, stamped, true);
            }

            EventBus.Publish(new OnGameSealedEvent(stamped));
        }

        public void ResetToNewGame()
        {
            SaveFileIO.DeleteAllSaveFiles(_directoryPath, _saveFileName, _backupPrefix, _backupCount);
            SaveLoadBootstrap.ResetAllSaveables(_saveables);
            _isDirty = false;
            EventBus.Publish(new OnSaveDeletedEvent());
        }

        private bool EnsureSingleton()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return false;
            }

            _instance = this;
            return true;
        }

        private void InitTuning()
        {
            _directoryPath = Application.persistentDataPath;
            _saveIntervalSeconds = ReadFloatOrDefault("SAVE_AUTO_INTERVAL_SEC", DefaultAutoIntervalSec);
            _backupCount = Mathf.Max(0, ReadIntOrDefault("SAVE_BACKUP_COUNT", DefaultBackupCount));
            _saveFileName = ReadStringOrDefault("SAVE_FILE_NAME", DefaultSaveName);
            _backupPrefix = ReadStringOrDefault("SAVE_BAK_PREFIX", DefaultBackupPrefix);
            _gameOverPrefix = ReadStringOrDefault("SAVE_GAMEOVER_PREFIX", DefaultGameOverPrefix);
        }

        private void DiscoverSaveables()
        {
            _saveables.Clear();

            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is ISaveable saveable)
                {
                    _saveables.Add(saveable);
                }
            }
        }

        private SaveDataRoot BuildRoot()
        {
            SaveDataRoot root = new SaveDataRoot
            {
                schemaMeta = new SchemaMeta
                {
                    schemaVersion = "1",
                    savedAtUtc = DateTime.UtcNow.ToString("o"),
                    gameOverState = "Active",
                    lastActiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            };

            for (int i = 0; i < _saveables.Count; i++)
            {
                ISaveable saveable = _saveables[i];
                string json = saveable.Serialize();
                AssignOwnerJson(root, saveable.OwnerKey, json);
            }

            return root;
        }

        private static void AssignOwnerJson(SaveDataRoot root, string ownerKey, string json)
        {
            switch (ownerKey)
            {
                case "f03Resources": root.f03Resources = json; break;
                case "c06WorldDanger": root.c06WorldDanger = json; break;
                case "c02AdventurerRoster": root.c02AdventurerRoster = json; break;
                case "ft06Guild": root.ft06Guild = json; break;
                case "ft07Buildings": root.ft07Buildings = json; break;
                case "ft02Dispatch": root.ft02Dispatch = json; break;
                case "ft08Gacha": root.ft08Gacha = json; break;
                case "ft12Staff": root.ft12Staff = json; break;
                case "ft01Recruitment": root.ft01Recruitment = json; break;
                case "factionStorySaveData": root.factionStorySaveData = json; break;
                case "ft03Decision": root.ft03Decision = json; break;
            }
        }

        private float ReadFloatOrDefault(string key, float fallback)
        {
            try
            {
                return DataManager.Instance != null ? DataManager.Instance.GetFloat(key) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private int ReadIntOrDefault(string key, int fallback)
        {
            try
            {
                return DataManager.Instance != null ? DataManager.Instance.GetInt(key) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private string ReadStringOrDefault(string key, string fallback)
        {
            try
            {
                string value = DataManager.Instance != null ? DataManager.Instance.GetString(key) : null;
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private void SubscribeAllEvents()
        {
            Hook<OnGoldChangedEvent>(_ => MarkDirty());
            Hook<OnReputationChangedEvent>(_ => MarkDirty());
            Hook<OnBankruptcyStateChangedEvent>(_ => MarkDirty());
            Hook<OnDangerLevelChangedEvent>(_ => MarkDirty());
            Hook<OnAdventurerAddedEvent>(_ => MarkDirty());
            Hook<OnAdventurerDismissedEvent>(_ => MarkDirty());
            Hook<OnAdventurerStatusChangedEvent>(_ => MarkDirty());
            Hook<OnCommissionAcceptedEvent>(_ => MarkDirty());
            Hook<OnMissionResolvedEvent>(_ => MarkDirty());
            Hook<BuildingUpgradedEvent>(_ => MarkDirty());
            Hook<OnFactionScoreChangedEvent>(_ => MarkDirty());
            Hook<OnGachaStateDirtyEvent>(_ => MarkDirty());
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _eventUnsubscribers.Count; i++)
            {
                _eventUnsubscribers[i].Invoke();
            }

            _eventUnsubscribers.Clear();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Hook<T>(Action<T> action)
        {
            EventBus.Subscribe(action);
            _eventUnsubscribers.Add(() => EventBus.Unsubscribe(action));
        }
    }
}
