using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.Time;
using TheGuild.Gameplay.GoldFlow.Events;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.MissionDispatch.Events;
using TheGuild.Gameplay.Outcome.Events;
// Outcome class 與 namespace 同名，需用 alias 消除歧義（CS0118）
using OutcomeRecord = TheGuild.Gameplay.Outcome.Outcome;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Staff;
using UnityEngine;

namespace TheGuild.Gameplay.GoldFlow
{
    [DefaultExecutionOrder(220)]
    public sealed class GoldFlowService : MonoBehaviour, IGoldFlowService
    {
        private const string KeyCommissionRate = "COMMISSION_RATE";
        private const string KeyPenaltyRate = "PENALTY_RATE";

        private float _commissionRate;
        private float _penaltyRate;

        private DataManager _dataManager;
        private TimeSystem _timeSystem;
        private ResourceManagement _resourceManagement;

        public static GoldFlowService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterSystemConstantsTable("SystemConstants");
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnCommissionAcceptedEvent>(HandleCommissionAccepted);
            EventBus.Subscribe<OnMissionResolvedEvent>(HandleMissionResolved);
            EventBus.Subscribe<OnGuildMaintenanceDueEvent>(HandleMaintenanceDue);
            EventBus.Subscribe<OnStaffSalaryDueEvent>(HandleSalaryDue);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCommissionAcceptedEvent>(HandleCommissionAccepted);
            EventBus.Unsubscribe<OnMissionResolvedEvent>(HandleMissionResolved);
            EventBus.Unsubscribe<OnGuildMaintenanceDueEvent>(HandleMaintenanceDue);
            EventBus.Unsubscribe<OnStaffSalaryDueEvent>(HandleSalaryDue);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<OnCommissionAcceptedEvent>(HandleCommissionAccepted);
            EventBus.Unsubscribe<OnMissionResolvedEvent>(HandleMissionResolved);
            EventBus.Unsubscribe<OnGuildMaintenanceDueEvent>(HandleMaintenanceDue);
            EventBus.Unsubscribe<OnStaffSalaryDueEvent>(HandleSalaryDue);

            if (Instance == this)
            {
                Instance = null;
            }
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

            CacheDependencies();
            LoadConstants();
        }

        internal void InitializeForTests()
        {
            InitializeInstance();
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

        private void CacheDependencies()
        {
            _dataManager = DataManager.Instance;
            _timeSystem = TimeSystem.Instance;
            _resourceManagement = ResourceManagement.Instance;
        }

        private void LoadConstants()
        {
            if (_dataManager == null)
            {
                _commissionRate = 0f;
                _penaltyRate = 0f;
                Debug.LogError("[GoldFlowService] LoadConstants: DataManager.Instance is null.");
                return;
            }

            _commissionRate = _dataManager.GetFloat(KeyCommissionRate);
            _penaltyRate = _dataManager.GetFloat(KeyPenaltyRate);
        }

        private void HandleCommissionAccepted(OnCommissionAcceptedEvent evt)
        {
            if (evt.BaseReward <= 0)
            {
                Debug.LogError("[GoldFlowService] HandleCommissionAccepted: baseReward must be > 0.");
                return;
            }

            if (!TryResolveResource(out ResourceManagement resource))
            {
                return;
            }

            int goldBefore = resource.GetGold();
            bool ok = resource.AddGold(evt.BaseReward);
            if (!ok)
            {
                Debug.LogError($"[GoldFlowService] HandleCommissionAccepted: AddGold failed for missionID={evt.MissionID}.");
                return;
            }

            int prepaid = resource.GetGold() - goldBefore;
            EventBus.Publish(new OnCommissionPrepaidEvent(evt.MissionID, prepaid, evt.Source));
        }

        private void HandleMissionResolved(OnMissionResolvedEvent evt)
        {
            OutcomeRecord outcome = evt.Outcome;
            if (outcome == null)
            {
                Debug.LogError("[GoldFlowService] HandleMissionResolved: outcome is null.");
                return;
            }

            float accountantCommissionBonus = StaffService.Instance != null
                ? StaffService.Instance.GetAccountantCommissionBonus()
                : 0f;
            float accountantPenaltyBonus = StaffService.Instance != null
                ? StaffService.Instance.GetAccountantPenaltyBonus()
                : 0f;

            // FT-07 建築層賠償率加成預留鉤子（FT-05 GDD §3.4）：Jam 階段固定 0，
            // 目前無建築種類提供賠償加成；Phase 2 若新增此類建築再於 BuildingService
            // 補 GetBuildingPenaltyBonus() API 並改呼叫之。
            float buildingPenaltyBonus = 0f;

            float effectiveCommissionRate = _commissionRate + accountantCommissionBonus;
            float effectivePenaltyRate = Mathf.Max(0f, _penaltyRate + accountantPenaltyBonus + buildingPenaltyBonus);

            int commissionAmount = 0;
            int penaltyAmount = 0;
            int netDelta;

            if (outcome.isSuccess)
            {
                commissionAmount = Mathf.RoundToInt(outcome.baseReward * effectiveCommissionRate);
                netDelta = commissionAmount + outcome.conditionGoldBonus;
            }
            else
            {
                penaltyAmount = Mathf.RoundToInt(outcome.baseReward * effectivePenaltyRate);
                netDelta = -penaltyAmount + outcome.conditionGoldBonus;
            }

            if (!TryExecuteGoldFlow(
                    netDelta,
                    out int goldBefore,
                    out int goldAfter,
                    out BankruptcyWarningState bankruptcyBefore,
                    out BankruptcyWarningState bankruptcyAfter))
            {
                Debug.LogError($"[GoldFlowService] HandleMissionResolved: ExecuteGoldFlow failed for missionID={outcome.missionID}.");
                return;
            }

            CommissionBreakdown breakdown = new CommissionBreakdown(
                outcome.activeMissionID,
                outcome.missionID,
                outcome.adventurerInstanceID,
                outcome.missionDifficulty,
                ResolveDispatchSource(outcome),
                outcome.isSuccess,
                outcome.baseReward,
                outcome.conditionGoldBonus,
                _commissionRate,
                _penaltyRate,
                accountantCommissionBonus,
                accountantPenaltyBonus,
                buildingPenaltyBonus,
                effectiveCommissionRate,
                effectivePenaltyRate,
                commissionAmount,
                penaltyAmount,
                netDelta,
                goldBefore,
                goldAfter,
                bankruptcyBefore,
                bankruptcyAfter,
                GetNowUtc());

            EventBus.Publish(new OnCommissionSettledEvent(breakdown));
        }

        private void HandleMaintenanceDue(OnGuildMaintenanceDueEvent evt)
        {
            if (evt.PerBuildingCost == null)
            {
                Debug.LogError("[GoldFlowService] HandleMaintenanceDue: perBuildingCost is null.");
                return;
            }

            if (evt.TotalAmount <= 0)
            {
                Debug.LogError("[GoldFlowService] HandleMaintenanceDue: totalAmount must be > 0.");
                return;
            }

            int netDelta = -evt.TotalAmount;
            if (!TryExecuteGoldFlow(
                    netDelta,
                    out int goldBefore,
                    out int goldAfter,
                    out BankruptcyWarningState bankruptcyBefore,
                    out BankruptcyWarningState bankruptcyAfter))
            {
                Debug.LogError($"[GoldFlowService] HandleMaintenanceDue: ExecuteGoldFlow failed with totalAmount={evt.TotalAmount}.");
                return;
            }

            Dictionary<int, int> defensiveCopy = new Dictionary<int, int>(evt.PerBuildingCost);
            MaintenanceBreakdown breakdown = new MaintenanceBreakdown(
                evt.DueTimestamp,
                defensiveCopy,
                evt.TotalAmount,
                netDelta,
                goldBefore,
                goldAfter,
                bankruptcyBefore,
                bankruptcyAfter,
                GetNowUtc());

            EventBus.Publish(new OnMaintenanceChargedEvent(breakdown));
        }

        private void HandleSalaryDue(OnStaffSalaryDueEvent evt)
        {
            if (evt.PerStaffSalary == null)
            {
                Debug.LogError("[GoldFlowService] HandleSalaryDue: perStaffSalary is null.");
                return;
            }

            if (evt.TotalAmount <= 0)
            {
                Debug.LogError("[GoldFlowService] HandleSalaryDue: totalAmount must be > 0.");
                return;
            }

            int netDelta = -evt.TotalAmount;
            if (!TryExecuteGoldFlow(
                    netDelta,
                    out int goldBefore,
                    out int goldAfter,
                    out BankruptcyWarningState bankruptcyBefore,
                    out BankruptcyWarningState bankruptcyAfter))
            {
                Debug.LogError($"[GoldFlowService] HandleSalaryDue: ExecuteGoldFlow failed with totalAmount={evt.TotalAmount}.");
                return;
            }

            Dictionary<int, int> defensiveCopy = new Dictionary<int, int>(evt.PerStaffSalary);
            SalaryBreakdown breakdown = new SalaryBreakdown(
                evt.DueTimestamp,
                defensiveCopy,
                evt.TotalAmount,
                netDelta,
                goldBefore,
                goldAfter,
                bankruptcyBefore,
                bankruptcyAfter,
                GetNowUtc());

            EventBus.Publish(new OnSalaryChargedEvent(breakdown));
        }

        private bool TryResolveResource(out ResourceManagement resource)
        {
            resource = _resourceManagement;
            if (resource != null)
            {
                return true;
            }

            resource = ResourceManagement.Instance;
            _resourceManagement = resource;

            if (resource == null)
            {
                Debug.LogError("[GoldFlowService] ResourceManagement.Instance is null.");
                return false;
            }

            return true;
        }

        private bool TryExecuteGoldFlow(
            int netDelta,
            out int goldBefore,
            out int goldAfter,
            out BankruptcyWarningState bankruptcyBefore,
            out BankruptcyWarningState bankruptcyAfter)
        {
            goldBefore = 0;
            goldAfter = 0;
            bankruptcyBefore = BankruptcyWarningState.Normal;
            bankruptcyAfter = BankruptcyWarningState.Normal;

            if (!TryResolveResource(out ResourceManagement resource))
            {
                return false;
            }

            goldBefore = resource.GetGold();
            bankruptcyBefore = resource.GetBankruptcyWarningState();

            bool applied = resource.AddGoldAllowBankruptcy(netDelta);

            goldAfter = resource.GetGold();
            bankruptcyAfter = resource.GetBankruptcyWarningState();

            return applied;
        }

        private DispatchSource ResolveDispatchSource(OutcomeRecord outcome)
        {
            if (outcome == null)
            {
                return DispatchSource.PlayerManual;
            }

            return DispatchSource.PlayerManual;
        }

        private long GetNowUtc()
        {
            TimeSystem time = _timeSystem != null ? _timeSystem : TimeSystem.Instance;
            if (time != null)
            {
                _timeSystem = time;
                return time.NowUTC;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
