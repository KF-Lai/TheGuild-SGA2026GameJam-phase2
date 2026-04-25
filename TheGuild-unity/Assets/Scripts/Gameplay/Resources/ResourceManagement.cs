using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources.Events;
using UnityEngine;

namespace TheGuild.Gameplay.Resources
{
    /// <summary>
    /// 遊戲資源管理系統，負責金幣、聲望與破產警告狀態。
    /// </summary>
    public sealed class ResourceManagement : MonoBehaviour
    {
        private const string BankruptcyThresholdTableName = "BankruptcyThresholdTable";
        private const string GoldInitialKey = "GOLD_INITIAL";
        private const string GoldMaxKey = "GOLD_MAX";
        private const string ReputationMinKey = "REPUTATION_MIN";
        private const string ReputationMaxKey = "REPUTATION_MAX";

        private const int DefaultGoldInitial = 100;
        private const int DefaultGoldMax = 9999999;
        private const int DefaultReputationMin = -100;
        private const int DefaultReputationMax = 100;
        private const int DefaultBankruptcyThreshold = -100;
        private const long DefaultWarningDurationSec = 86400L;

        private int _goldInitial;
        private int _goldMax;
        private int _reputationMin;
        private int _reputationMax;

        private int _currentGold;
        private int _currentReputation;
        private int _currentBankruptcyThreshold;
        private BankruptcyWarningState _warningState;
        private long _bankruptcyWarningStartTime;
        private long _warningDurationSec;

        private bool _isProcessing;

        /// <summary>
        /// 單例實體。
        /// </summary>
        public static ResourceManagement Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<BankruptcyThresholdData>(BankruptcyThresholdTableName);
        }

        /// <summary>
        /// 取得目前金幣。
        /// </summary>
        public int GetGold()
        {
            return _currentGold;
        }

        /// <summary>
        /// 取得目前聲望。
        /// </summary>
        public int GetReputation()
        {
            return _currentReputation;
        }

        /// <summary>
        /// 判斷是否可負擔指定花費。
        /// </summary>
        public bool CanAfford(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            return _currentGold >= amount;
        }

        /// <summary>
        /// 取得目前破產門檻。
        /// </summary>
        public int GetCurrentBankruptcyThreshold()
        {
            return _currentBankruptcyThreshold;
        }

        /// <summary>
        /// 取得目前破產警告狀態。
        /// </summary>
        public BankruptcyWarningState GetBankruptcyWarningState()
        {
            return _warningState;
        }

        /// <summary>
        /// 取得破產警告剩餘秒數。
        /// </summary>
        public long GetBankruptcyWarningRemainingSeconds()
        {
            if (_warningState != BankruptcyWarningState.Warning)
            {
                return 0;
            }

            long now = GetNowUtc();
            long elapsed = now - _bankruptcyWarningStartTime;
            if (elapsed <= 0)
            {
                return _warningDurationSec;
            }

            long remaining = _warningDurationSec - elapsed;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>
        /// 變更金幣（嚴格模式）。
        /// </summary>
        public bool AddGold(int delta)
        {
            if (!TryEnterProcessing("AddGold"))
            {
                return false;
            }

            try
            {
                long target = (long)_currentGold + delta;
                if (target < _currentBankruptcyThreshold)
                {
                    return false;
                }

                if (target > _goldMax)
                {
                    target = _goldMax;
                }

                int previousGold = _currentGold;
                int newGold = (int)target;
                int actualDelta = newGold - previousGold;
                if (actualDelta == 0)
                {
                    return true;
                }

                _currentGold = newGold;
                EventBus.Publish(new OnGoldChangedEvent(previousGold, _currentGold, actualDelta));
                UpdateBankruptcyWarning(previousGold, _currentGold);
                return true;
            }
            finally
            {
                ExitProcessing();
            }
        }

        /// <summary>
        /// 變更金幣（允許進入破產判定）。
        /// </summary>
        public bool AddGoldAllowBankruptcy(int delta)
        {
            if (!TryEnterProcessing("AddGoldAllowBankruptcy"))
            {
                return false;
            }

            try
            {
                long target = (long)_currentGold + delta;
                if (target > int.MaxValue)
                {
                    Debug.LogWarning("[ResourceManagement] AddGoldAllowBankruptcy 發生上溢位，已截斷為 int.MaxValue。");
                    target = int.MaxValue;
                }
                else if (target < int.MinValue)
                {
                    Debug.LogWarning("[ResourceManagement] AddGoldAllowBankruptcy 發生下溢位，已截斷為 int.MinValue。");
                    target = int.MinValue;
                }

                if (target > _goldMax)
                {
                    target = _goldMax;
                }

                int previousGold = _currentGold;
                int newGold = (int)target;
                int actualDelta = newGold - previousGold;
                if (actualDelta == 0)
                {
                    return true;
                }

                _currentGold = newGold;
                EventBus.Publish(new OnGoldChangedEvent(previousGold, _currentGold, actualDelta));
                EvaluateWarningState();
                return true;
            }
            finally
            {
                ExitProcessing();
            }
        }

        /// <summary>
        /// 變更聲望。
        /// </summary>
        public void AddReputation(int delta)
        {
            if (!TryEnterProcessing("AddReputation"))
            {
                return;
            }

            try
            {
                int previous = _currentReputation;
                long target = (long)_currentReputation + delta;
                if (target < _reputationMin)
                {
                    target = _reputationMin;
                }
                else if (target > _reputationMax)
                {
                    target = _reputationMax;
                }

                _currentReputation = (int)target;
                int actualDelta = _currentReputation - previous;
                if (actualDelta != 0)
                {
                    EventBus.Publish(new OnReputationChangedEvent(previous, _currentReputation, actualDelta));
                }

                EvaluateWarningState();
            }
            finally
            {
                ExitProcessing();
            }
        }

        /// <summary>
        /// 設定目前破產門檻。
        /// </summary>
        public void SetBankruptcyThreshold(int threshold)
        {
            _currentBankruptcyThreshold = threshold;
            EvaluateWarningState();
        }

        /// <summary>
        /// 重置破產狀態（保留 API）。
        /// </summary>
        public void ResetBankruptcyState()
        {
            ExitWarning();
        }

        /// <summary>
        /// 建立目前資源快照。
        /// </summary>
        public ResourceSnapshot CreateSnapshot()
        {
            return new ResourceSnapshot
            {
                CurrentGold = _currentGold,
                CurrentReputation = _currentReputation,
                WarningState = _warningState,
                BankruptcyWarningStartTime = _bankruptcyWarningStartTime,
                WarningDurationSec = _warningDurationSec,
                CurrentBankruptcyThreshold = _currentBankruptcyThreshold
            };
        }

        /// <summary>
        /// 還原資源快照並重新評估破產狀態。
        /// </summary>
        public void RestoreSnapshot(ResourceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                Debug.LogError("[ResourceManagement] RestoreSnapshot 失敗：snapshot 為 null。");
                return;
            }

            _currentGold = snapshot.CurrentGold;
            _currentReputation = snapshot.CurrentReputation;
            _warningState = snapshot.WarningState;
            _bankruptcyWarningStartTime = snapshot.BankruptcyWarningStartTime;
            _warningDurationSec = snapshot.WarningDurationSec;
            _currentBankruptcyThreshold = snapshot.CurrentBankruptcyThreshold;

            EvaluateWarningState();
        }

        internal static void ResetForTests()
        {
            if (Instance != null)
            {
                Instance.UnsubscribeEvents();
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
            SubscribeEvents();
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void InitializeInstance()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            LoadConfig();
            _currentGold = _goldInitial;
            _currentReputation = 0;
            _currentBankruptcyThreshold = DefaultBankruptcyThreshold;
            _warningState = BankruptcyWarningState.Normal;
            _bankruptcyWarningStartTime = 0;
            _warningDurationSec = 0;
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void SubscribeEvents()
        {
            EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Subscribe<OnOfflineResolvedEvent>(HandleOfflineResolved);
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void UnsubscribeEvents()
        {
            EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick);
            EventBus.Unsubscribe<OnOfflineResolvedEvent>(HandleOfflineResolved);
        }

        private bool TryEnterProcessing(string apiName)
        {
            if (_isProcessing)
            {
                Debug.LogError($"[ResourceManagement] reentrancy detected in {apiName}");
                return false;
            }

            _isProcessing = true;
            return true;
        }

        private void ExitProcessing()
        {
            _isProcessing = false;
        }

        private void LoadConfig()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[ResourceManagement] DataManager.Instance 為 null，改用預設常數。");
                _goldInitial = DefaultGoldInitial;
                _goldMax = DefaultGoldMax;
                _reputationMin = DefaultReputationMin;
                _reputationMax = DefaultReputationMax;
                return;
            }

            _goldInitial = DataManager.Instance.GetInt(GoldInitialKey);
            _goldMax = DataManager.Instance.GetInt(GoldMaxKey);
            _reputationMin = DataManager.Instance.GetInt(ReputationMinKey);
            _reputationMax = DataManager.Instance.GetInt(ReputationMaxKey);

            if (_goldMax < _goldInitial)
            {
                _goldMax = _goldInitial;
            }

            if (_reputationMin > _reputationMax)
            {
                int swap = _reputationMin;
                _reputationMin = _reputationMax;
                _reputationMax = swap;
            }
        }

        private void HandleSecondTick(OnSecondTickEvent _)
        {
            if (_warningState != BankruptcyWarningState.Warning)
            {
                return;
            }

            if (GetBankruptcyWarningRemainingSeconds() <= 0)
            {
                TriggerBankruptcy();
            }
        }

        private void HandleOfflineResolved(OnOfflineResolvedEvent evt)
        {
            if (_warningState == BankruptcyWarningState.Warning)
            {
                long remaining = GetBankruptcyWarningRemainingSeconds();
                if (remaining > 0 && evt.OfflineSeconds >= remaining)
                {
                    TriggerBankruptcy();
                    return;
                }
            }

            EvaluateWarningState();
        }

        private void EvaluateWarningState()
        {
            if (_currentGold >= 0)
            {
                ExitWarning();
                return;
            }

            if (_currentGold < _currentBankruptcyThreshold)
            {
                TriggerBankruptcy();
                return;
            }

            if (_warningState != BankruptcyWarningState.Warning)
            {
                EnterWarning();
            }
            else
            {
                _warningDurationSec = LookupWarningDuration(_currentReputation);
            }
        }

        private void UpdateBankruptcyWarning(int previousGold, int newGold)
        {
            if (_warningState == BankruptcyWarningState.Warning && newGold >= 0)
            {
                ExitWarning();
                return;
            }

            if (_warningState == BankruptcyWarningState.Normal && previousGold >= 0 && newGold < 0)
            {
                EnterWarning();
            }
        }

        private void ExitWarning()
        {
            if (_warningState == BankruptcyWarningState.Normal)
            {
                return;
            }

            BankruptcyWarningState previous = _warningState;
            _warningState = BankruptcyWarningState.Normal;
            _bankruptcyWarningStartTime = 0;
            _warningDurationSec = 0;
            EventBus.Publish(new OnBankruptcyStateChangedEvent(previous, _warningState));
        }

        private void EnterWarning()
        {
            BankruptcyWarningState previous = _warningState;
            _warningState = BankruptcyWarningState.Warning;
            _bankruptcyWarningStartTime = GetNowUtc();
            _warningDurationSec = LookupWarningDuration(_currentReputation);

            if (previous != _warningState)
            {
                EventBus.Publish(new OnBankruptcyStateChangedEvent(previous, _warningState));
            }
        }

        private void TriggerBankruptcy()
        {
            if (_warningState == BankruptcyWarningState.Bankrupt)
            {
                return;
            }

            BankruptcyWarningState previous = _warningState;
            _warningState = BankruptcyWarningState.Bankrupt;
            _bankruptcyWarningStartTime = 0;
            _warningDurationSec = 0;
            EventBus.Publish(new OnBankruptcyStateChangedEvent(previous, _warningState));
        }

        private long LookupWarningDuration(int reputation)
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[ResourceManagement] LookupWarningDuration 失敗：DataManager.Instance 為 null。");
                return DefaultWarningDurationSec;
            }

            IReadOnlyList<BankruptcyThresholdData> matched = DataManager.Instance.GetWhere<BankruptcyThresholdData>(
                x => x.reputationMin <= reputation && reputation <= x.reputationMax);

            if (matched.Count > 0)
            {
                return matched[0].warningDurationSec;
            }

            Debug.LogError($"[ResourceManagement] 找不到對應聲望區間設定，reputation={reputation}");
            return DefaultWarningDurationSec;
        }

        private static long GetNowUtc()
        {
            if (TimeSystem.Instance != null)
            {
                return TimeSystem.Instance.NowUTC;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
