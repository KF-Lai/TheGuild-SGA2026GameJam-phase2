using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using UnityEngine;

namespace TheGuild.Core.Time
{
    /// <summary>
    /// 遊戲時間核心系統（即時 + 離線）。
    /// </summary>
    public sealed class TimeSystem : MonoBehaviour
    {
        private const int DEFAULT_DAILY_RESET_HOUR = 0;
        private const long DEFAULT_OFFLINE_MAX_SECONDS = 604800L;
        private const float MAX_REALTIME_CATCHUP_SECONDS = 60f;

        private static Func<long> _clockProviderForTests;
        private static Func<float> _deltaProviderForTests;

        private readonly List<MissionTimer> _missionTimers = new List<MissionTimer>(32);
        private readonly HashSet<string> _publishedExpirations = new HashSet<string>(8, StringComparer.Ordinal);
        private readonly List<string> _expiredMissionScratch = new List<string>(8);

        private float _accumulator;
        private int _minuteAccumulator;
        private bool _tickPaused;
        private int _dailyResetHour;
        private long _offlineMaxSeconds;
        private long _lastActiveTimestamp;
        private DateTime _lastResetUtcDate;

        private OfflineState _offlineState = OfflineState.Uninitialized;
        private OfflineSummary _pendingSummary;

        private enum OfflineState
        {
            Uninitialized,
            Pending,
            Resolved
        }

        /// <summary>
        /// 全域唯一實例。
        /// </summary>
        public static TimeSystem Instance { get; private set; }

        /// <summary>
        /// 當前 UTC Unix timestamp（秒）。
        /// </summary>
        public long NowUTC => GetNowUtc();

        /// <summary>
        /// 在 DataManager Awake 前註冊 SystemConstants。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterSystemConstantsTable("SystemConstants");
        }

        /// <summary>
        /// 【離線階段 A】只計算摘要，不做結算發布。
        /// </summary>
        public void Initialize(long lastActiveTimestamp)
        {
            if (_offlineState != OfflineState.Uninitialized)
            {
                return;
            }

            long nowUtc = NowUTC;
            _lastActiveTimestamp = lastActiveTimestamp;

            if (!TryBuildOfflineSummary(lastActiveTimestamp, nowUtc, out OfflineSummary pendingSummary))
            {
                _offlineState = OfflineState.Resolved;
                return;
            }

            _pendingSummary = pendingSummary;
            _offlineState = OfflineState.Pending;
            EventBus.Publish(new OnOfflinePendingEvent(_pendingSummary));
        }

        /// <summary>
        /// 【離線階段 B】依序發布 OnMissionExpired → OnDailyReset(可選) → OnOfflineResolved。
        /// </summary>
        public void ConfirmOfflineResolution()
        {
            if (_offlineState != OfflineState.Pending)
            {
                return;
            }

            IReadOnlyList<string> ids = _pendingSummary.CompletedMissionInstanceIds;
            for (int i = 0; i < ids.Count; i++)
            {
                PublishMissionExpired(ids[i]);
            }

            if (_pendingSummary.CrossesDailyReset)
            {
                _lastResetUtcDate = DateTimeOffset.FromUnixTimeSeconds(NowUTC).UtcDateTime.Date;
                EventBus.Publish(EventNames.OnDailyReset);
            }

            EventBus.Publish(new OnOfflineResolvedEvent(
                _pendingSummary.OfflineSeconds,
                _pendingSummary.CompletedCount));

            _pendingSummary = default;
            _offlineState = OfflineState.Resolved;
        }

        /// <summary>
        /// 註冊任務計時器。durationSeconds 單位為秒（呼叫方負責分鐘→秒轉換）。
        /// </summary>
        public void RegisterMission(string missionInstanceId, long dispatchTimestamp, int durationSeconds)
        {
            if (string.IsNullOrEmpty(missionInstanceId))
            {
                Debug.LogError("[TimeSystem] RegisterMission 失敗：missionInstanceId 不可為空");
                return;
            }

            MissionTimer timer = new MissionTimer(missionInstanceId, dispatchTimestamp, durationSeconds);

            int existingIndex = IndexOfMission(missionInstanceId);
            if (existingIndex >= 0)
            {
                _missionTimers[existingIndex] = timer;
                _publishedExpirations.Remove(missionInstanceId);
                return;
            }

            _missionTimers.Add(timer);
            _publishedExpirations.Remove(missionInstanceId);
        }

        /// <summary>
        /// 移除任務計時器。
        /// </summary>
        public void UnregisterMission(string missionInstanceId)
        {
            if (string.IsNullOrEmpty(missionInstanceId))
            {
                return;
            }

            for (int i = _missionTimers.Count - 1; i >= 0; i--)
            {
                if (_missionTimers[i].MissionInstanceId == missionInstanceId)
                {
                    _missionTimers.RemoveAt(i);
                }
            }

            _publishedExpirations.Remove(missionInstanceId);
        }

        /// <summary>
        /// 暫停即時 Tick（冪等，且不影響離線流程）。
        /// </summary>
        public void PauseTick()
        {
            _tickPaused = true;
        }

        /// <summary>
        /// 取得目前進行中任務計時器快照。
        /// </summary>
        public IReadOnlyList<MissionTimer> GetActiveMissionTimers()
        {
            return new List<MissionTimer>(_missionTimers);
        }

        /// <summary>
        /// 取得最後活動時間戳。
        /// </summary>
        public long GetLastActiveTimestamp()
        {
            return _lastActiveTimestamp;
        }

        internal static void SetClockProviderForTests(Func<long> provider)
        {
            _clockProviderForTests = provider;
        }

        internal static void SetDeltaProviderForTests(Func<float> provider)
        {
            _deltaProviderForTests = provider;
        }

        internal static void ResetTestHooks()
        {
            _clockProviderForTests = null;
            _deltaProviderForTests = null;
        }

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        internal void TickForTests(float deltaSeconds)
        {
            if (_tickPaused)
            {
                return;
            }

            ProcessRealtime(deltaSeconds);
        }

        internal string GetOfflineStateNameForTests()
        {
            return _offlineState.ToString();
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

            LoadSystemConstants();
            long now = NowUTC;
            _lastResetUtcDate = DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime.Date;
            _lastActiveTimestamp = now;
        }

        private void Update()
        {
            if (_tickPaused)
            {
                return;
            }

            float delta = _deltaProviderForTests != null
                ? _deltaProviderForTests()
                : UnityEngine.Time.unscaledDeltaTime;

            ProcessRealtime(delta);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ProcessRealtime(float deltaSeconds)
        {
            if (deltaSeconds > MAX_REALTIME_CATCHUP_SECONDS)
            {
                Debug.LogWarning(
                    $"[TimeSystem] catastrophic delta {deltaSeconds:F1}s exceeds cap {MAX_REALTIME_CATCHUP_SECONDS}s; " +
                    "truncated to prevent frame freeze. Consider Initialize() for offline-style catch-up.");
                deltaSeconds = MAX_REALTIME_CATCHUP_SECONDS;
            }

            _accumulator += deltaSeconds;

            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;

                long nowUtc = NowUTC;
                TickSeconds(nowUtc);
            }
        }

        private void TickSeconds(long nowUtc)
        {
            _lastActiveTimestamp = nowUtc;

            EventBus.Publish(new OnSecondTickEvent(nowUtc));
            CheckMissionTimers(nowUtc);
            CheckDailyResetCrossing(nowUtc);
            TickMinute(nowUtc);
        }

        private void TickMinute(long nowUtc)
        {
            _minuteAccumulator += 1;
            if (_minuteAccumulator < 60)
            {
                return;
            }

            _minuteAccumulator = 0;
            EventBus.Publish(new OnMinuteTickEvent(nowUtc));
        }

        private void LoadSystemConstants()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[TimeSystem] DataManager.Instance 為 null，使用預設常數值");
                _dailyResetHour = DEFAULT_DAILY_RESET_HOUR;
                _offlineMaxSeconds = DEFAULT_OFFLINE_MAX_SECONDS;
                return;
            }

            _dailyResetHour = TryLoadIntConstant("DAILY_RESET_HOUR", DEFAULT_DAILY_RESET_HOUR);
            _offlineMaxSeconds = TryLoadIntConstant("OFFLINE_MAX_SECONDS", (int)DEFAULT_OFFLINE_MAX_SECONDS);

            if (_dailyResetHour < 0 || _dailyResetHour > 23)
            {
                _dailyResetHour = DEFAULT_DAILY_RESET_HOUR;
            }

            if (_offlineMaxSeconds <= 0)
            {
                _offlineMaxSeconds = DEFAULT_OFFLINE_MAX_SECONDS;
            }
        }

        private static int TryLoadIntConstant(string key, int defaultValue)
        {
            try
            {
                return DataManager.Instance.GetInt(key);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TimeSystem] 讀取 {key} 失敗：{ex.Message}，使用預設值 {defaultValue}");
                return defaultValue;
            }
        }

        private static long GetNowUtc()
        {
            if (_clockProviderForTests != null)
            {
                return _clockProviderForTests();
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void CheckMissionTimers(long nowUtc)
        {
            _expiredMissionScratch.Clear();

            for (int i = 0; i < _missionTimers.Count; i++)
            {
                MissionTimer timer = _missionTimers[i];
                long remainingSeconds = GetRemainingSeconds(in timer, nowUtc);
                if (remainingSeconds <= 0)
                {
                    _expiredMissionScratch.Add(timer.MissionInstanceId);
                }
            }

            for (int i = 0; i < _expiredMissionScratch.Count; i++)
            {
                PublishMissionExpired(_expiredMissionScratch[i]);
            }
        }

        private void PublishMissionExpired(string missionInstanceId)
        {
            if (string.IsNullOrEmpty(missionInstanceId))
            {
                return;
            }

            if (_publishedExpirations.Contains(missionInstanceId))
            {
                return;
            }

            _publishedExpirations.Add(missionInstanceId);
            EventBus.Publish(new OnMissionExpiredEvent(missionInstanceId));
        }

        private void CheckDailyResetCrossing(long currentUtcSeconds)
        {
            DateTime currentUtc = DateTimeOffset.FromUnixTimeSeconds(currentUtcSeconds).UtcDateTime;
            DateTime currentDate = currentUtc.Date;

            if (currentDate <= _lastResetUtcDate)
            {
                return;
            }

            if (currentUtc.Hour < _dailyResetHour)
            {
                return;
            }

            _lastResetUtcDate = currentDate;
            EventBus.Publish(EventNames.OnDailyReset);
        }

        private bool ComputeDailyResetCrossing(long lastActiveUtc, long nowUtc)
        {
            DateTime last = DateTimeOffset.FromUnixTimeSeconds(lastActiveUtc).UtcDateTime;
            DateTime now = DateTimeOffset.FromUnixTimeSeconds(nowUtc).UtcDateTime;

            DateTime boundary = last.Date.AddHours(_dailyResetHour);
            if (last >= boundary)
            {
                boundary = boundary.AddDays(1);
            }

            return boundary <= now;
        }

        /// <summary>
        /// 建立離線摘要資料並回傳是否需要進入 Pending。
        /// 回傳 false 可能代表兩種狀況：
        /// 1) 無離線時間，未發送任何事件。
        /// 2) 離線有時間但 0 任務且無跨日，已立即發送 OnOfflineResolvedEvent。
        /// </summary>
        private bool TryBuildOfflineSummary(long lastActiveTimestamp, long nowUtc, out OfflineSummary summary)
        {
            summary = default;

            long rawOfflineSeconds = nowUtc - lastActiveTimestamp;
            if (rawOfflineSeconds <= 0)
            {
                return false;
            }

            long offlineSeconds = rawOfflineSeconds > _offlineMaxSeconds
                ? _offlineMaxSeconds
                : rawOfflineSeconds;

            List<string> completedMissionIds = CollectCompletedMissionIds(nowUtc);
            bool crossesDailyReset = ComputeDailyResetCrossing(lastActiveTimestamp, nowUtc);

            if (completedMissionIds.Count == 0 && !crossesDailyReset)
            {
                EventBus.Publish(new OnOfflineResolvedEvent(offlineSeconds, 0));
                return false;
            }

            summary = new OfflineSummary(
                offlineSeconds,
                completedMissionIds.Count,
                completedMissionIds,
                crossesDailyReset);

            return true;
        }

        private List<string> CollectCompletedMissionIds(long nowUtc)
        {
            List<string> completed = new List<string>(8);
            for (int i = 0; i < _missionTimers.Count; i++)
            {
                MissionTimer timer = _missionTimers[i];
                long remainingSeconds = GetRemainingSeconds(in timer, nowUtc);
                if (remainingSeconds <= 0)
                {
                    completed.Add(timer.MissionInstanceId);
                }
            }

            return completed;
        }

        private static long GetRemainingSeconds(in MissionTimer timer, long nowUtc)
        {
            return timer.DispatchTimestamp + timer.DurationSeconds - nowUtc;
        }

        private int IndexOfMission(string missionInstanceId)
        {
            for (int i = 0; i < _missionTimers.Count; i++)
            {
                if (_missionTimers[i].MissionInstanceId == missionInstanceId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
