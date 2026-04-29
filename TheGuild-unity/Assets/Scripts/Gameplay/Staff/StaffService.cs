using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Resources;
using UnityEngine;

// ── ISaveable TODO ────────────────────────────────────────────────────────────
// TODO(FT-10)：待 ISaveable 介面建立後，於類別宣告加上 `: ISaveable`。
// 目前保留 ISaveable 需要的 public 成員（OwnerKey / IsCritical / Serialize /
// RestoreFromSave / InitializeAsNewGame）以便 FT-10 直接接線。
// ─────────────────────────────────────────────────────────────────────────────

namespace TheGuild.Gameplay.Staff
{
    /// <summary>
    /// FT-12 職員主服務。
    /// </summary>
    [DefaultExecutionOrder(170)]
    public sealed class StaffService : MonoBehaviour, IStaffService, ISaveable
    {
        private const string STAFF_TABLE_NAME = "StaffTable";
        private const string OWNER_KEY = "ft12Staff";
        private const int SECONDS_PER_DAY = 86400;
        private const int DEFAULT_OFFLINE_MAX_SECONDS = 604800;

        private static readonly IReadOnlyDictionary<int, StaffInstance> s_emptyRoster =
            new Dictionary<int, StaffInstance>(0);

        private readonly Dictionary<int, StaffInstance> _roster = new Dictionary<int, StaffInstance>(128);
        private StaffTableLoader _loader;
        private StaffEffectAggregator _aggregator;

        private bool _hasState;
        private bool _isLoaderReady;
        private int _nextInstanceID = 1;
        private long _lastSalaryTimestamp;
        private long _lastAutoLeaveScanTimestamp;

        public static StaffService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<StaffData>(STAFF_TABLE_NAME);
        }

        /// <summary>FT-10 會使用的 owner key。</summary>
        public string OwnerKey => OWNER_KEY;

        /// <summary>FT-12 屬關鍵存檔。</summary>
        public bool IsCritical => true;

        private void Awake()
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

            _loader = new StaffTableLoader();
            _aggregator = new StaffEffectAggregator(_loader, GetRosterInternal, IsStaffSystemUnlockedInternal);

            if (DataManager.Instance == null)
            {
                Debug.LogError("[StaffService] Awake: DataManager.Instance 為 null，StaffTableLoader 尚未初始化。");
                _isLoaderReady = false;
                return;
            }

            try
            {
                _loader.Initialize();
                _isLoaderReady = true;
            }
            catch (Exception ex)
            {
                _isLoaderReady = false;
                Debug.LogError($"[StaffService] StaffTable 初始化失敗：{ex.GetType().Name} - {ex.Message}");
            }
        }

        private void Start()
        {
            if (!_hasState)
            {
                InitializeAsNewGame();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnMinuteTickEvent>(OnMinuteTickHandler);
            EventBus.Subscribe<BuildingUpgradedEvent>(OnBuildingUpgradedHandler);

#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                EventBus.Subscribe(EventNames.OnDailyReset, OnDailyResetHandler);
            }
#pragma warning restore CS0162
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnMinuteTickEvent>(OnMinuteTickHandler);
            EventBus.Unsubscribe<BuildingUpgradedEvent>(OnBuildingUpgradedHandler);

#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                EventBus.Unsubscribe(EventNames.OnDailyReset, OnDailyResetHandler);
            }
#pragma warning restore CS0162
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<OnMinuteTickEvent>(OnMinuteTickHandler);
            EventBus.Unsubscribe<BuildingUpgradedEvent>(OnBuildingUpgradedHandler);

#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                EventBus.Unsubscribe(EventNames.OnDailyReset, OnDailyResetHandler);
            }
#pragma warning restore CS0162

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── Query API ──────────────────────────────────────────────────────────

        public float GetStaffWillingnessBonus()
        {
            if (!_isLoaderReady)
            {
                return 0f;
            }

            return _aggregator.GetStaffWillingnessBonus();
        }

        public float GetAccountantCommissionBonus()
        {
            if (!_isLoaderReady)
            {
                return 0f;
            }

            return _aggregator.GetAccountantCommissionBonus();
        }

        public float GetAccountantPenaltyBonus()
        {
            if (!_isLoaderReady)
            {
                return 0f;
            }

            return _aggregator.GetAccountantPenaltyBonus();
        }

        public int GetRecruitRefreshReductionSec()
        {
            if (!_isLoaderReady)
            {
                return 0;
            }

            return _aggregator.GetRecruitRefreshReductionSec();
        }

        public bool IsSuccessRatePreviewEnabled()
        {
            if (!_isLoaderReady)
            {
                return false;
            }

            return _aggregator.IsSuccessRatePreviewEnabled();
        }

        public StaffInstance GetInstance(int instanceID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return null;
            }

            return _roster.TryGetValue(instanceID, out StaffInstance staff) ? staff : null;
        }

        public IReadOnlyDictionary<int, StaffInstance> GetRoster()
        {
            return IsStaffSystemUnlockedInternal() ? _roster : s_emptyRoster;
        }

        public StaffStateView GetStaffStateView(int instanceID)
        {
            StaffStateView view = default;
            if (!IsStaffSystemUnlockedInternal())
            {
                return view;
            }

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return view;
            }

            long now = GetNowUtc();
            int reallocRemaining = 0;
            if (staff.currentState == StaffState.Reallocating &&
                staff.reallocatingStartTimestamp > 0 &&
                _loader.ReallocatingAutoLeaveSec > 0)
            {
                long elapsed = now - staff.reallocatingStartTimestamp;
                long remain = _loader.ReallocatingAutoLeaveSec - elapsed;
                reallocRemaining = remain > 0 ? (int)remain : 0;
            }

            int cooldownRemaining = 0;
            if (staff.buildingSwitchCooldownEndTimestamp > now)
            {
                cooldownRemaining = (int)(staff.buildingSwitchCooldownEndTimestamp - now);
            }

            view.currentState = staff.currentState;
            view.assignedBuildingID = staff.assignedBuildingID;
            view.reallocatingRemainingSec = reallocRemaining;
            view.switchCooldownRemainingSec = cooldownRemaining;
            return view;
        }

        // ── Mutator API ────────────────────────────────────────────────────────

        public HireResult HireStaff(CandidateCard candidate)
        {
            HireResult output = default;
            output.result = HireStaffResult.STAFF_SYSTEM_LOCKED;
            output.instanceID = 0;

            if (!IsStaffSystemUnlockedInternal())
            {
                return output;
            }

            if (!_isLoaderReady)
            {
                output.result = HireStaffResult.STAFF_SYSTEM_LOCKED;
                return output;
            }

            StaffData data = _loader.Get(candidate.staffID);
            if (data == null)
            {
                output.result = HireStaffResult.INVALID_STAFF_ID;
                return output;
            }

            if (_loader.RosterCap > 0 && _roster.Count >= _loader.RosterCap)
            {
                output.result = HireStaffResult.ROSTER_FULL;
                return output;
            }

            int instanceID = _nextInstanceID;
            _nextInstanceID += 1;

            if (_roster.ContainsKey(instanceID))
            {
                output.result = HireStaffResult.DUPLICATE_INSTANCE;
                return output;
            }

            long now = GetNowUtc();
            bool hasSlot = HasSlotCapability(data);

            StaffInstance instance = new StaffInstance
            {
                instanceID = instanceID,
                staffID = candidate.staffID,
                currentState = StaffState.Reallocating,
                assignedBuildingID = 0,
                reallocatingStartTimestamp = hasSlot ? now : 0L,
                buildingSwitchCooldownEndTimestamp = 0L,
                hiredTimestamp = now
            };

            _roster.Add(instanceID, instance);
            EventBus.Publish(new OnStaffHiredEvent(instance.instanceID, instance.staffID, instance.hiredTimestamp));

            output.result = HireStaffResult.OK;
            output.instanceID = instanceID;
            return output;
        }

        public AssignResult TryAssignStaff(int instanceID, int buildingID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return AssignResult.STAFF_SYSTEM_LOCKED;
            }

            CheckReallocatingAutoLeave();

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return AssignResult.STAFF_NOT_FOUND;
            }

            StaffData data = _loader.Get(staff.staffID);
            if (data == null || !CanAssignBuilding(data, buildingID))
            {
                return AssignResult.BUILDING_NOT_ELIGIBLE;
            }

            if (staff.currentState == StaffState.Working && staff.assignedBuildingID == buildingID)
            {
                return AssignResult.SUCCESS;
            }

            if (staff.currentState == StaffState.OnLeave)
            {
                return AssignResult.STAFF_ON_LEAVE;
            }

            long now = GetNowUtc();
            if (now < staff.buildingSwitchCooldownEndTimestamp)
            {
                return AssignResult.SWITCH_COOLDOWN;
            }

            int currentInBuilding = CountWorkingInBuilding(buildingID);
            int capacity = GetBuildingCapacity(buildingID);
            if (currentInBuilding >= capacity)
            {
                return AssignResult.BUILDING_FULL;
            }

            int oldBuildingID = staff.assignedBuildingID;
            StaffState oldState = staff.currentState;

            staff.assignedBuildingID = buildingID;
            staff.currentState = StaffState.Working;
            staff.reallocatingStartTimestamp = 0L;
            staff.buildingSwitchCooldownEndTimestamp = now + _loader.BuildingSwitchCooldownSec;

            EventBus.Publish(new OnStaffAssignedEvent(instanceID, oldBuildingID, buildingID));
            if (oldState != StaffState.Working)
            {
                EventBus.Publish(new OnStaffStateChangedEvent(instanceID, oldState, StaffState.Working));
            }

            return AssignResult.SUCCESS;
        }

        public UnassignResult TryUnassignStaff(int instanceID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return UnassignResult.STAFF_SYSTEM_LOCKED;
            }

            CheckReallocatingAutoLeave();

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return UnassignResult.STAFF_NOT_FOUND;
            }

            if (staff.assignedBuildingID == 0)
            {
                return UnassignResult.STAFF_NOT_ASSIGNED;
            }

            if (staff.currentState == StaffState.OnLeave)
            {
                return UnassignResult.STAFF_ON_LEAVE;
            }

            long now = GetNowUtc();
            int oldBuildingID = staff.assignedBuildingID;
            StaffState oldState = staff.currentState;

            staff.assignedBuildingID = 0;
            staff.currentState = StaffState.Reallocating;
            staff.reallocatingStartTimestamp = now;

            EventBus.Publish(new OnStaffAssignedEvent(instanceID, oldBuildingID, 0));
            EventBus.Publish(new OnStaffStateChangedEvent(instanceID, oldState, StaffState.Reallocating));
            return UnassignResult.SUCCESS;
        }

        public GoOnLeaveResult TryGoOnLeave(int instanceID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return GoOnLeaveResult.STAFF_SYSTEM_LOCKED;
            }

            CheckReallocatingAutoLeave();

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return GoOnLeaveResult.STAFF_NOT_FOUND;
            }

            if (staff.currentState == StaffState.OnLeave)
            {
                return GoOnLeaveResult.ALREADY_ON_LEAVE;
            }

            int oldBuildingID = staff.assignedBuildingID;
            StaffState oldState = staff.currentState;

            staff.currentState = StaffState.OnLeave;
            staff.assignedBuildingID = 0;
            staff.reallocatingStartTimestamp = 0L;

            if (oldBuildingID > 0)
            {
                EventBus.Publish(new OnStaffAssignedEvent(instanceID, oldBuildingID, 0));
            }

            EventBus.Publish(new OnStaffStateChangedEvent(instanceID, oldState, StaffState.OnLeave));
            return GoOnLeaveResult.SUCCESS;
        }

        public ReturnFromLeaveResult TryReturnFromLeave(int instanceID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return ReturnFromLeaveResult.STAFF_SYSTEM_LOCKED;
            }

            CheckReallocatingAutoLeave();

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return ReturnFromLeaveResult.STAFF_NOT_FOUND;
            }

            if (staff.currentState != StaffState.OnLeave)
            {
                return ReturnFromLeaveResult.NOT_ON_LEAVE;
            }

            StaffState oldState = staff.currentState;
            staff.currentState = StaffState.Reallocating;
            staff.reallocatingStartTimestamp = GetNowUtc();

            EventBus.Publish(new OnStaffStateChangedEvent(instanceID, oldState, StaffState.Reallocating));
            return ReturnFromLeaveResult.SUCCESS;
        }

        public TryFireStaffResult TryFireStaff(int instanceID)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return TryFireStaffResult.STAFF_SYSTEM_LOCKED;
            }

            CheckReallocatingAutoLeave();

            if (!_roster.TryGetValue(instanceID, out StaffInstance staff) || staff == null)
            {
                return TryFireStaffResult.STAFF_NOT_FOUND;
            }

            StaffData data = _loader.Get(staff.staffID);
            if (data == null)
            {
                return TryFireStaffResult.STAFF_NOT_FOUND;
            }

            int severance = data.severancePay < 0 ? 0 : data.severancePay;
            ResourceManagement resource = ResourceManagement.Instance;
            if (resource != null)
            {
                resource.AddGoldAllowBankruptcy(-severance);
            }

            long now = GetNowUtc();
            EventBus.Publish(new OnStaffFiredEvent(staff.instanceID, staff.staffID, now, severance));
            _roster.Remove(instanceID);
            return TryFireStaffResult.SUCCESS;
        }

        // ── ISaveable ──────────────────────────────────────────────────────────

        public string Serialize()
        {
            StaffSaveData save = new StaffSaveData
            {
                staffInstances = new List<StaffInstance>(_roster.Count),
                nextInstanceID = _nextInstanceID,
                lastSalaryTimestamp = _lastSalaryTimestamp
            };

            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                save.staffInstances.Add(pair.Value);
            }

            save.staffInstances.Sort(CompareByInstanceIDAsc);
            return JsonUtility.ToJson(save);
        }

        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            StaffSaveData save = JsonUtility.FromJson<StaffSaveData>(ownerJson);
            if (save == null)
            {
                throw new CriticalRestoreFailedException("Staff 存檔反序列化結果為 null。");
            }

            _roster.Clear();
            long now = GetNowUtc();
            int maxInstanceID = 0;
            List<StaffInstance> source = save.staffInstances ?? new List<StaffInstance>(0);

            for (int i = 0; i < source.Count; i++)
            {
                StaffInstance staff = source[i];
                if (staff == null)
                {
                    continue;
                }

                if (staff.instanceID <= 0)
                {
                    throw new CriticalRestoreFailedException($"RestoreFromSave 偵測到非法 instanceID={staff.instanceID}。");
                }

                if (_roster.ContainsKey(staff.instanceID))
                {
                    throw new CriticalRestoreFailedException($"RestoreFromSave 偵測到重複 instanceID={staff.instanceID}。");
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    throw new CriticalRestoreFailedException($"RestoreFromSave 找不到 staffID={staff.staffID} 的 StaffTable 資料。");
                }

                if (!Enum.IsDefined(typeof(StaffState), staff.currentState))
                {
                    throw new CriticalRestoreFailedException(
                        $"RestoreFromSave 偵測到非法 StaffState={staff.currentState}（instanceID={staff.instanceID}）。");
                }

                if (staff.hiredTimestamp < 0)
                {
                    staff.hiredTimestamp = 0;
                }

                if (staff.buildingSwitchCooldownEndTimestamp < 0)
                {
                    staff.buildingSwitchCooldownEndTimestamp = 0;
                }

                switch (staff.currentState)
                {
                    case StaffState.OnLeave:
                        staff.assignedBuildingID = 0;
                        staff.reallocatingStartTimestamp = 0;
                        break;

                    case StaffState.Reallocating:
                        staff.assignedBuildingID = 0;
                        if (staff.reallocatingStartTimestamp < 0)
                        {
                            staff.reallocatingStartTimestamp = 0;
                        }
                        break;

                    case StaffState.Working:
                        if (staff.assignedBuildingID <= 0 || !CanAssignBuilding(data, staff.assignedBuildingID))
                        {
                            ResetToReallocating(staff, now);
                        }
                        else
                        {
                            staff.reallocatingStartTimestamp = 0;
                        }
                        break;
                }

                _roster.Add(staff.instanceID, staff);
                if (staff.instanceID > maxInstanceID)
                {
                    maxInstanceID = staff.instanceID;
                }
            }

            FixWorkingCapacityOverflow(now);

            _nextInstanceID = save.nextInstanceID > 0 ? save.nextInstanceID : 1;
            int requiredNext = maxInstanceID + 1;
            if (_nextInstanceID < requiredNext)
            {
                _nextInstanceID = requiredNext;
            }

            _lastSalaryTimestamp = save.lastSalaryTimestamp;
            _lastAutoLeaveScanTimestamp = 0;
            _hasState = true;
        }

        public void InitializeAsNewGame()
        {
            _roster.Clear();
            _nextInstanceID = 1;
            _lastSalaryTimestamp = 0;
            _lastAutoLeaveScanTimestamp = 0;
            _hasState = true;
        }

        // ── 內部流程 ──────────────────────────────────────────────────────────

        internal void CheckReallocatingAutoLeave()
        {
            if (!_isLoaderReady)
            {
                return;
            }

            if (!IsStaffSystemUnlockedInternal())
            {
                return;
            }

            long now = GetNowUtc();
            if (_lastAutoLeaveScanTimestamp > 0 &&
                now - _lastAutoLeaveScanTimestamp < _loader.AutoLeaveScanIntervalSec)
            {
                return;
            }

            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Reallocating)
                {
                    continue;
                }

                if (staff.reallocatingStartTimestamp == 0)
                {
                    continue;
                }

                if (now - staff.reallocatingStartTimestamp < _loader.ReallocatingAutoLeaveSec)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null || !HasSlotCapability(data))
                {
                    continue;
                }

                StaffState oldState = staff.currentState;
                staff.currentState = StaffState.OnLeave;
                staff.assignedBuildingID = 0;
                staff.reallocatingStartTimestamp = 0;
                EventBus.Publish(new OnStaffStateChangedEvent(staff.instanceID, oldState, StaffState.OnLeave));
            }

            _lastAutoLeaveScanTimestamp = now;
        }

        internal void OnStaffSystemBoot()
        {
            CheckReallocatingAutoLeave();

#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                ProcessOfflineSalary();
            }
#pragma warning restore CS0162
        }

        private void OnMinuteTickHandler(OnMinuteTickEvent _)
        {
            CheckReallocatingAutoLeave();
        }

        private void OnBuildingUpgradedHandler(BuildingUpgradedEvent evt)
        {
            if (evt.BuildingID == 6 && evt.FromLevel == 0 && evt.ToLevel >= 1)
            {
                OnStaffSystemUnlocked();
            }
        }

        private void OnStaffSystemUnlocked()
        {
            long now = GetNowUtc();
            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Reallocating)
                {
                    continue;
                }

                if (staff.reallocatingStartTimestamp == 0)
                {
                    continue;
                }

                if (now - staff.reallocatingStartTimestamp < _loader.ReallocatingAutoLeaveSec)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null || !HasSlotCapability(data))
                {
                    continue;
                }

                staff.reallocatingStartTimestamp = now;
            }

#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                _lastSalaryTimestamp = now;
            }
#pragma warning restore CS0162
        }

        private void OnDailyResetHandler()
        {
#pragma warning disable CS0162
            if (StaffPhase2.SalaryEnabled)
            {
                OnSalaryTick(GetNowUtc());
            }
#pragma warning restore CS0162
        }

        private void OnSalaryTick(long dueTimestamp)
        {
            if (!IsStaffSystemUnlockedInternal())
            {
                return;
            }

            if (dueTimestamp <= _lastSalaryTimestamp)
            {
                return;   // 時鐘倒退或重複守護
            }

            Dictionary<int, int> perStaffSalary = AssemblePerStaffSalary();
            int totalAmount = 0;
            foreach (KeyValuePair<int, int> pair in perStaffSalary)
            {
                totalAmount += pair.Value;
            }

            EventBus.Publish(new OnStaffSalaryDueEvent(dueTimestamp, perStaffSalary, totalAmount));
            _lastSalaryTimestamp = dueTimestamp;
        }

        private void ProcessOfflineSalary()
        {
            long now = GetNowUtc();
            if (_lastSalaryTimestamp <= 0)
            {
                _lastSalaryTimestamp = now;
                return;
            }

            if (now <= _lastSalaryTimestamp)
            {
                return;
            }

            int offlineMax = DataManager.Instance != null
                ? DataManager.Instance.GetInt("OFFLINE_MAX_SECONDS")
                : DEFAULT_OFFLINE_MAX_SECONDS;

            if (offlineMax <= 0)
            {
                offlineMax = DEFAULT_OFFLINE_MAX_SECONDS;
            }

            long rawDays = (now - _lastSalaryTimestamp) / SECONDS_PER_DAY;
            long maxDays = offlineMax / SECONDS_PER_DAY;
            long salaryCycles = rawDays > maxDays ? maxDays : rawDays;

            for (long i = 0; i < salaryCycles; i++)
            {
                long due = _lastSalaryTimestamp + SECONDS_PER_DAY;
                OnSalaryTick(due);
            }
        }

        private Dictionary<int, int> AssemblePerStaffSalary()
        {
            Dictionary<int, int> output = new Dictionary<int, int>(_roster.Count);
            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState == StaffState.OnLeave)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null || data.salary <= 0)
                {
                    continue;
                }

                output[staff.instanceID] = data.salary;
            }

            return output;
        }

        private void FixWorkingCapacityOverflow(long now)
        {
            Dictionary<int, List<StaffInstance>> workingByBuilding = new Dictionary<int, List<StaffInstance>>(8);

            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Working || staff.assignedBuildingID <= 0)
                {
                    continue;
                }

                if (!workingByBuilding.TryGetValue(staff.assignedBuildingID, out List<StaffInstance> list))
                {
                    list = new List<StaffInstance>(8);
                    workingByBuilding.Add(staff.assignedBuildingID, list);
                }

                list.Add(staff);
            }

            foreach (KeyValuePair<int, List<StaffInstance>> pair in workingByBuilding)
            {
                int buildingID = pair.Key;
                List<StaffInstance> list = pair.Value;
                int capacity = GetBuildingCapacity(buildingID);

                if (list.Count <= capacity)
                {
                    continue;
                }

                list.Sort(CompareByHireThenInstanceIDAsc);
                for (int i = capacity; i < list.Count; i++)
                {
                    ResetToReallocating(list[i], now);
                }
            }
        }

        private static void ResetToReallocating(StaffInstance staff, long now)
        {
            staff.currentState = StaffState.Reallocating;
            staff.assignedBuildingID = 0;
            staff.reallocatingStartTimestamp = now;
        }

        private int CountWorkingInBuilding(int buildingID)
        {
            int count = 0;
            foreach (KeyValuePair<int, StaffInstance> pair in _roster)
            {
                StaffInstance staff = pair.Value;
                if (staff != null &&
                    staff.currentState == StaffState.Working &&
                    staff.assignedBuildingID == buildingID)
                {
                    count += 1;
                }
            }

            return count;
        }

        private static bool HasSlotCapability(StaffData data)
        {
            for (int i = 0; i < data.slotBuildingIDsParsed.Count; i++)
            {
                if (data.slotBuildingIDsParsed[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanAssignBuilding(StaffData data, int buildingID)
        {
            for (int i = 0; i < data.slotBuildingIDsParsed.Count; i++)
            {
                if (data.slotBuildingIDsParsed[i] == buildingID)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetBuildingCapacity(int buildingID)
        {
            if (buildingID <= 0 || DataManager.Instance == null || BuildingService.Instance == null)
            {
                return 0;
            }

            int level = BuildingService.Instance.GetBuildingLevel(buildingID);
            string pk = buildingID + "_" + level;
            BuildingRow row = DataManager.Instance.Get<BuildingRow>(pk);
            return row != null ? row.slotCount : 0;
        }

        private bool IsStaffSystemUnlockedInternal()
        {
            return BuildingService.Instance != null && BuildingService.Instance.IsStaffSystemUnlocked();
        }

        private IReadOnlyDictionary<int, StaffInstance> GetRosterInternal()
        {
            return _roster;
        }

        private static long GetNowUtc()
        {
            if (TimeSystem.Instance != null)
            {
                return TimeSystem.Instance.NowUTC;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static int CompareByInstanceIDAsc(StaffInstance a, StaffInstance b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            return a.instanceID.CompareTo(b.instanceID);
        }

        private static int CompareByHireThenInstanceIDAsc(StaffInstance a, StaffInstance b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            int hireCmp = a.hiredTimestamp.CompareTo(b.hiredTimestamp);
            if (hireCmp != 0)
            {
                return hireCmp;
            }

            return a.instanceID.CompareTo(b.instanceID);
        }

        [Serializable]
        private sealed class StaffSaveData
        {
            public List<StaffInstance> staffInstances;
            public int nextInstanceID;
            public long lastSalaryTimestamp;
        }
    }
}
