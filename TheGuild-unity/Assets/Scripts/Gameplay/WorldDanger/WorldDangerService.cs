using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.WorldDanger.Events;
using UnityEngine;

namespace TheGuild.Gameplay.WorldDanger
{
    /// <summary>
    /// C-06 World Danger Service（concrete singleton）。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class WorldDangerService : MonoBehaviour, ISaveable
    {
        private const int SecondsPerDay = 86400;
        private const int FallbackMaxDebt = -100;

        private static readonly string[] DangerLevelOrder = { "E", "D", "C", "B", "A" };
        private static readonly MissionPoolWeights FallbackEWeights = new MissionPoolWeights(40, 30, 20, 8, 2, 0);
        private static readonly IReadOnlyDictionary<string, int> DifficultyIndex = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["F"] = 0,
            ["E"] = 1,
            ["D"] = 2,
            ["C"] = 3,
            ["B"] = 4,
            ["A"] = 5,
            ["S"] = 6,
            ["SS"] = 7,
            ["SSS"] = 8
        };

        private IReadOnlyDictionary<string, WorldDangerData> _dangerByLevel = new Dictionary<string, WorldDangerData>(StringComparer.Ordinal);

        private string _currentDangerLevel;
        private int _acceptedMissionCount;
        private long _gameStartTimestamp;
        private int _cachedMaxFactionScore;

        // FT-10 ISaveable 接線（IsCritical=false / OwnerKey="c06WorldDanger"，per FT-10 FSD §2.3 / §5.4.1.A）
        public string OwnerKey => "c06WorldDanger";
        public bool IsCritical => false;

        [Serializable]
        private sealed class C06SaveData
        {
            public string currentDangerLevel;
            public long gameStartTimestamp;
        }

        public string Serialize()
        {
            return JsonUtility.ToJson(new C06SaveData
            {
                currentDangerLevel = _currentDangerLevel,
                gameStartTimestamp = _gameStartTimestamp
            });
        }

        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            C06SaveData dto = JsonUtility.FromJson<C06SaveData>(ownerJson);
            if (dto == null)
            {
                InitializeAsNewGame();
                return;
            }

            _currentDangerLevel = string.IsNullOrEmpty(dto.currentDangerLevel) ? "E" : dto.currentDangerLevel;
            _gameStartTimestamp = dto.gameStartTimestamp;
        }

        public void InitializeAsNewGame()
        {
            _currentDangerLevel = "E";
            _acceptedMissionCount = 0;
            _cachedMaxFactionScore = 0;
            _gameStartTimestamp = TimeSystem.Instance != null ? TimeSystem.Instance.NowUTC : 0;
        }

        public static WorldDangerService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<WorldDangerData>("WorldDangerTable");
        }

        public string GetCurrentLevel()
        {
            return _currentDangerLevel ?? "E";
        }

        public WorldDangerData GetDangerData(string dangerLevel)
        {
            string key = NormalizeDangerLevel(dangerLevel);
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[WorldDangerService] GetDangerData 失敗：dangerLevel 空值。");
                return null;
            }

            if (_dangerByLevel.TryGetValue(key, out WorldDangerData row))
            {
                return row;
            }

            Debug.LogWarning($"[WorldDangerService] GetDangerData 找不到 dangerLevel={key}。");
            return null;
        }

        public int GetMaxDebt()
        {
            if (!TryGetCurrentDangerData(out WorldDangerData row))
            {
                Debug.LogError($"[WorldDangerService] GetMaxDebt fallback：找不到當前 dangerLevel={_currentDangerLevel}。");
                return FallbackMaxDebt;
            }

            if (row.maxDebt == 0)
            {
                Debug.LogError($"[WorldDangerService] GetMaxDebt fallback：dangerLevel={_currentDangerLevel} 的 maxDebt=0。");
                return FallbackMaxDebt;
            }

            return row.maxDebt;
        }

        public MissionPoolWeights GetPoolWeights()
        {
            if (!TryGetCurrentDangerData(out WorldDangerData row))
            {
                Debug.LogError($"[WorldDangerService] GetPoolWeights fallback：找不到當前 dangerLevel={_currentDangerLevel}。");
                return FallbackEWeights;
            }

            MissionPoolWeights weights = new MissionPoolWeights(
                row.weightF_E,
                row.weightD,
                row.weightC,
                row.weightB,
                row.weightA,
                row.weightS_SSS);

            if (weights.IsAllZero)
            {
                Debug.LogError($"[WorldDangerService] GetPoolWeights fallback：dangerLevel={_currentDangerLevel} 權重全為 0。");
                return FallbackEWeights;
            }

            return weights;
        }

        public void OnMissionAccepted(string difficulty)
        {
            if (string.Equals(_currentDangerLevel, "A", StringComparison.Ordinal))
            {
                return;
            }

            string nextLevel = GetNextDangerLevel(_currentDangerLevel);
            if (string.IsNullOrEmpty(nextLevel) || !_dangerByLevel.TryGetValue(nextLevel, out WorldDangerData nextData))
            {
                Debug.LogError($"[WorldDangerService] OnMissionAccepted 找不到下一階資料：nextLevel={nextLevel}。");
                return;
            }

            string normalizedDifficulty = NormalizeDifficulty(difficulty);
            if (!DifficultyIndex.TryGetValue(normalizedDifficulty, out int acceptedIndex))
            {
                Debug.LogWarning($"[WorldDangerService] OnMissionAccepted 收到無效 difficulty={difficulty}。");
                return;
            }

            if (!DifficultyIndex.TryGetValue(nextData.minDifficulty, out int requiredIndex))
            {
                Debug.LogError($"[WorldDangerService] minDifficulty 無效：{nextData.minDifficulty}。");
                return;
            }

            if (acceptedIndex >= requiredIndex)
            {
                _acceptedMissionCount += 1;
                CheckLevelUp();
            }
        }

        public void OnFactionScoreUpdated(int newMaxScore)
        {
            if (string.Equals(_currentDangerLevel, "A", StringComparison.Ordinal))
            {
                return;
            }

            _cachedMaxFactionScore = newMaxScore;
            CheckLevelUp();
        }

        public void CheckLevelUp()
        {
            if (string.Equals(_currentDangerLevel, "A", StringComparison.Ordinal))
            {
                return;
            }

            string nextLevel = GetNextDangerLevel(_currentDangerLevel);
            if (string.IsNullOrEmpty(nextLevel))
            {
                return;
            }

            if (!_dangerByLevel.TryGetValue(nextLevel, out WorldDangerData nextData))
            {
                Debug.LogError($"[WorldDangerService] 缺漏 dangerLevel 階資料：{nextLevel}，強制推進並遞迴檢查。");
                _currentDangerLevel = nextLevel;
                CheckLevelUp();
                return;
            }

            long elapsedDays = GetElapsedDays();
            bool timeOK = elapsedDays >= nextData.timeThreshold;
            bool missionOK = _acceptedMissionCount >= nextData.missionCountReq;
            bool factionOK = nextData.factionScoreReq <= 0 || _cachedMaxFactionScore >= nextData.factionScoreReq;

            if (timeOK && missionOK && factionOK)
            {
                _currentDangerLevel = nextLevel;
                _acceptedMissionCount = 0;
                ApplyBankruptcyThreshold();
                EventBus.Publish(new OnDangerLevelChangedEvent { newDangerLevel = _currentDangerLevel });
                CheckLevelUp();
            }
        }

        internal static void ResetForTests()
        {
            if (Instance != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(Instance.gameObject);
#else
                Destroy(Instance.gameObject);
#endif
                Instance = null;
            }
        }

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void Start()
        {
            ApplyBankruptcyThreshold();
        }

        private void OnEnable()
        {
            EventBus.Subscribe(EventNames.OnDailyReset, HandleDailyReset);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe(EventNames.OnDailyReset, HandleDailyReset);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitializeInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
#if UNITY_EDITOR
                    DestroyImmediate(gameObject);
#endif
                }
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            WorldDangerLoader loader = new WorldDangerLoader();
            _dangerByLevel = loader.LoadAll();

            _currentDangerLevel = "E";
            _acceptedMissionCount = 0;
            _cachedMaxFactionScore = 0;
            _gameStartTimestamp = 0;
        }

        private void HandleDailyReset()
        {
            CheckLevelUp();
        }

        private bool TryGetCurrentDangerData(out WorldDangerData row)
        {
            return _dangerByLevel.TryGetValue(_currentDangerLevel, out row) && row != null;
        }

        private static string NormalizeDangerLevel(string level)
        {
            return string.IsNullOrWhiteSpace(level) ? string.Empty : level.Trim().ToUpperInvariant();
        }

        private static string NormalizeDifficulty(string difficulty)
        {
            return string.IsNullOrWhiteSpace(difficulty) ? string.Empty : difficulty.Trim().ToUpperInvariant();
        }

        private static string GetNextDangerLevel(string currentLevel)
        {
            for (int i = 0; i < DangerLevelOrder.Length; i++)
            {
                if (string.Equals(DangerLevelOrder[i], currentLevel, StringComparison.Ordinal))
                {
                    return i >= DangerLevelOrder.Length - 1 ? null : DangerLevelOrder[i + 1];
                }
            }

            Debug.LogError($"[WorldDangerService] 未知 dangerLevel={currentLevel}。");
            return null;
        }

        private long GetElapsedDays()
        {
            if (_gameStartTimestamp == 0)
            {
                Debug.LogError("[WorldDangerService] gameStartTimestamp=0，elapsedDays 以 0 計算。");
                return 0;
            }

            long nowUtc = TimeSystem.Instance != null
                ? TimeSystem.Instance.NowUTC
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            long elapsed = (nowUtc - _gameStartTimestamp) / SecondsPerDay;
            return elapsed < 0 ? 0 : elapsed;
        }

        private void ApplyBankruptcyThreshold()
        {
            if (ResourceManagement.Instance == null)
            {
                Debug.LogError("[WorldDangerService] ResourceManagement.Instance 為 null，無法推送破產門檻。");
                return;
            }

            ResourceManagement.Instance.SetBankruptcyThreshold(GetMaxDebt());
        }
    }
}
