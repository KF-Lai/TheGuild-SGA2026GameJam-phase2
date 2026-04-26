using System;
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
        private long _currentWarningDuration = DefaultWarningDurationSec;

        private bool _isProcessing;

        /// <summary>
        /// 單例實體。
        /// </summary>
        public static ResourceManagement Instance { get; private set; }

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

            long target = (long)_currentGold - amount;
            return target >= _currentBankruptcyThreshold;
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
                CurrentWarningDuration = _currentWarningDuration,
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

            BankruptcyWarningState sanitizedWarningState = snapshot.WarningState;
            if (!Enum.IsDefined(typeof(BankruptcyWarningState), sanitizedWarningState))
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到無效 WarningState={snapshot.WarningState}，已改為 {BankruptcyWarningState.Normal}。");
                sanitizedWarningState = BankruptcyWarningState.Normal;
            }

            long sanitizedWarningDurationSec = snapshot.WarningDurationSec;
            if (sanitizedWarningDurationSec < 0)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到負值 WarningDurationSec={snapshot.WarningDurationSec}，已 clamp 為 0。");
                sanitizedWarningDurationSec = 0;
            }

            long sanitizedCurrentWarningDuration = snapshot.CurrentWarningDuration;
            if (sanitizedCurrentWarningDuration <= 0)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到非正值 CurrentWarningDuration={snapshot.CurrentWarningDuration}，已改為 {DefaultWarningDurationSec}。");
                sanitizedCurrentWarningDuration = DefaultWarningDurationSec;
            }

            long sanitizedBankruptcyWarningStartTime = snapshot.BankruptcyWarningStartTime;
            if (sanitizedBankruptcyWarningStartTime < 0)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到負值 BankruptcyWarningStartTime={snapshot.BankruptcyWarningStartTime}，已 clamp 為 0。");
                sanitizedBankruptcyWarningStartTime = 0;
            }

            int sanitizedCurrentGold = snapshot.CurrentGold;
            if (sanitizedCurrentGold > _goldMax)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到 CurrentGold={snapshot.CurrentGold} 超過上限 {_goldMax}，已 clamp 為 {_goldMax}。");
                sanitizedCurrentGold = _goldMax;
            }

            int sanitizedCurrentReputation = snapshot.CurrentReputation;
            if (sanitizedCurrentReputation < _reputationMin)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到 CurrentReputation={snapshot.CurrentReputation} 低於下限 {_reputationMin}，已 clamp 為 {_reputationMin}。");
                sanitizedCurrentReputation = _reputationMin;
            }
            else if (sanitizedCurrentReputation > _reputationMax)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到 CurrentReputation={snapshot.CurrentReputation} 超過上限 {_reputationMax}，已 clamp 為 {_reputationMax}。");
                sanitizedCurrentReputation = _reputationMax;
            }

            int sanitizedCurrentBankruptcyThreshold = snapshot.CurrentBankruptcyThreshold;
            if (sanitizedCurrentBankruptcyThreshold > 0)
            {
                Debug.LogWarning($"[ResourceManagement] RestoreSnapshot 偵測到正值 CurrentBankruptcyThreshold={snapshot.CurrentBankruptcyThreshold}，已改為 {DefaultBankruptcyThreshold}。");
                sanitizedCurrentBankruptcyThreshold = DefaultBankruptcyThreshold;
            }

            _currentGold = sanitizedCurrentGold;
            _currentReputation = sanitizedCurrentReputation;
            _warningState = sanitizedWarningState;
            _bankruptcyWarningStartTime = sanitizedBankruptcyWarningStartTime;
            _warningDurationSec = sanitizedWarningDurationSec;
            _currentWarningDuration = sanitizedCurrentWarningDuration;
            _currentBankruptcyThreshold = sanitizedCurrentBankruptcyThreshold;

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
            _currentWarningDuration = DefaultWarningDurationSec;
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

            _goldInitial = ReadConfigIntOrDefault(GoldInitialKey, DefaultGoldInitial);
            _goldMax = ReadConfigIntOrDefault(GoldMaxKey, DefaultGoldMax);
            _reputationMin = ReadConfigIntOrDefault(ReputationMinKey, DefaultReputationMin);
            _reputationMax = ReadConfigIntOrDefault(ReputationMaxKey, DefaultReputationMax);

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

        private int ReadConfigIntOrDefault(string key, int fallback)
        {
            try
            {
                return DataManager.Instance.GetInt(key);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResourceManagement] LoadConfig 讀取 {key} 失敗：{ex.Message}");
                return fallback;
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
            if (_warningState == BankruptcyWarningState.Warning && _currentGold < 0)
            {
                long elapsed = GetNowUtc() - _bankruptcyWarningStartTime;
                if (elapsed >= _warningDurationSec)
                {
                    TriggerBankruptcy();
                    return;
                }
            }

            EvaluateWarningState();
        }

        /// <summary>
        /// 取得目前破產警告持續秒數設定。
        /// </summary>
        public long GetBankruptcyWarningDuration()
        {
            return _currentWarningDuration;
        }

        /// <summary>
        /// 設定下次進入破產警告時鎖定的持續秒數。
        /// </summary>
        public void SetBankruptcyWarningDuration(int newDurationSec)
        {
            if (newDurationSec <= 0)
            {
                Debug.LogError($"[ResourceManagement] SetBankruptcyWarningDuration 失敗：newDurationSec 必須大於 0，輸入值={newDurationSec}。");
                return;
            }

            _currentWarningDuration = newDurationSec;
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
            if (_warningState == BankruptcyWarningState.Warning)
            {
                return;
            }

            BankruptcyWarningState previous = _warningState;
            _warningState = BankruptcyWarningState.Warning;
            _bankruptcyWarningStartTime = GetNowUtc();
            _warningDurationSec = _currentWarningDuration;

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
