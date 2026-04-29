using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Staff;
using UnityEngine;
using Random = UnityEngine.Random;
using StaffCandidateCard = TheGuild.Gameplay.Staff.CandidateCard;

// 實作依據：【FT-08-FSD】gacha-system.md §1.3 / §5.1 / §5.2 / §5.3 / §5.4 / §6.7

namespace TheGuild.Gameplay.Gacha
{
    [DefaultExecutionOrder(180)]
    public sealed class GachaService : MonoBehaviour, IGachaService, ISaveable
    {
        private const string OWNER_KEY = "ft08Gacha";
        private const int DEFAULT_POOL_ID = 1;
        private const int STAFF_BUILDING_ID = 6;
        private const int MIN_GUILD_LEVEL = 1;
        private const int MAX_GUILD_LEVEL = 5;

        private static readonly IReadOnlyList<CandidateCard> s_emptyCandidates = new List<CandidateCard>(0);

        private StaffPlayerState _playerState;
        private GachaTableLoader _tableLoader;
        private GachaRollEngine _rollEngine;
        private bool _isLoaderReady;
        private bool _hasState;
        private bool _isHiringInFlight;

        public static GachaService Instance { get; private set; }

        public string OwnerKey => OWNER_KEY;
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

            _tableLoader = new GachaTableLoader();
            if (DataManager.Instance == null)
            {
                Debug.LogError("[GachaService] Awake: DataManager.Instance is null.");
                _isLoaderReady = false;
                return;
            }

            try
            {
                _tableLoader.Initialize();
                _isLoaderReady = true;
            }
            catch (Exception ex)
            {
                _isLoaderReady = false;
                Debug.LogError($"[GachaService] GachaTableLoader initialize failed: {ex.GetType().Name} - {ex.Message}");
            }

            if (_isLoaderReady)
            {
                _rollEngine = new GachaRollEngine(_tableLoader, GetNowUtc);
            }
        }

        private void Start()
        {
            if (!_hasState)
            {
                InitializeAsNewGame();
            }

            OnStaffSystemBoot();
        }

        private void OnEnable()
        {
            // Jam: no auto-refresh tick subscription by design (FSD §1.2 Out-of-Scope).
            // Reserved for Phase 2 event subscriptions.
        }

        private void OnDisable()
        {
            // Reserved for Phase 2 unsubscriptions.
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void InitializeAsNewGame()
        {
            _playerState = new StaffPlayerState
            {
                pityCounter = 0,
                lastAutoRefreshTimestamp = 0L,
                currentPoolID = DEFAULT_POOL_ID,
                currentCandidates = new List<CandidateCard>(5),
                reservedCandidates = new List<CandidateCard>(5)
            };

            _hasState = true;
        }

        public string Serialize()
        {
            if (!_hasState || _playerState == null)
            {
                InitializeAsNewGame();
            }

            StaffPlayerStateWrapper wrapper = new StaffPlayerStateWrapper
            {
                state = _playerState
            };

            return JsonUtility.ToJson(wrapper);
        }

        public void RestoreFromSave(string ownerJson)
        {
            if (string.IsNullOrEmpty(ownerJson))
            {
                InitializeAsNewGame();
                return;
            }

            if (!_isLoaderReady)
            {
                throw new StaffPlayerStateValidationException("RestoreFromSave failed: loader is not ready.");
            }

            StaffPlayerStateWrapper wrapper = JsonUtility.FromJson<StaffPlayerStateWrapper>(ownerJson);
            if (wrapper == null || wrapper.state == null)
            {
                throw new StaffPlayerStateValidationException("RestoreFromSave failed: state wrapper is null.");
            }

            _playerState = wrapper.state;
            if (_playerState.currentCandidates == null)
            {
                _playerState.currentCandidates = new List<CandidateCard>(0);
            }

            if (_playerState.reservedCandidates == null)
            {
                _playerState.reservedCandidates = new List<CandidateCard>(0);
            }

            ValidateAndSanitizePlayerState();
            _hasState = true;
        }

        public RefreshResult TryManualRefresh()
        {
            if (!IsStaffSystemUnlocked())
            {
                return RefreshResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return RefreshResult.STAFF_SYSTEM_LOCKED;
            }

            return ExecuteRefresh(RefreshType.Manual, _playerState.currentPoolID);
        }

        public SwitchPoolResult TrySwitchPool(int newPoolID)
        {
            if (!IsStaffSystemUnlocked())
            {
                return SwitchPoolResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return SwitchPoolResult.STAFF_SYSTEM_LOCKED;
            }

            if (!TryGetPoolData(newPoolID, out StaffGachaPoolData newPool))
            {
                return SwitchPoolResult.POOL_NOT_FOUND;
            }

            int currentLevel = GetCurrentGuildLevel();
            if (currentLevel < newPool.minGuildLevel || currentLevel > newPool.maxGuildLevel)
            {
                return SwitchPoolResult.POOL_LEVEL_LOCKED;
            }

            if (newPoolID == _playerState.currentPoolID)
            {
                return SwitchPoolResult.ALREADY_IN_POOL;
            }

            RefreshResult refreshResult = ExecuteRefresh(RefreshType.SwitchPool, newPoolID);
            if (refreshResult == RefreshResult.SUCCESS)
            {
                return SwitchPoolResult.SUCCESS;
            }

            if (refreshResult == RefreshResult.STAFF_SYSTEM_LOCKED)
            {
                return SwitchPoolResult.STAFF_SYSTEM_LOCKED;
            }

            return SwitchPoolResult.NO_REFRESHABLE_SLOT;
        }

        public RecruitResult TryRecruit(int slotIndex)
        {
            if (!IsStaffSystemUnlocked())
            {
                return RecruitResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return RecruitResult.STAFF_SYSTEM_LOCKED;
            }

            int slotCount = GetInterviewSlotCountInternal();
            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                return RecruitResult.INVALID_SLOT;
            }

            EnsureCurrentCandidatesSizedTo(slotCount);
            CandidateCard candidate = _playerState.currentCandidates[slotIndex];
            if (candidate.IsEmptySlot())
            {
                return RecruitResult.EMPTY_SLOT;
            }

            if (candidate.trashItemID > 0)
            {
                return RecruitResult.CANDIDATE_NOT_HIREABLE;
            }

            if (_isHiringInFlight)
            {
                return RecruitResult.BUSY;
            }

            IStaffService staffService = StaffService.Instance;
            if (staffService == null)
            {
                Debug.LogError("[GachaService] TryRecruit failed: StaffService.Instance is null.");
                return RecruitResult.INTERNAL_ERROR;
            }

            _isHiringInFlight = true;
            try
            {
                HireResult hireResult = staffService.HireStaff(new StaffCandidateCard
                {
                    staffID = candidate.staffID
                });

                switch (hireResult.result)
                {
                    case HireStaffResult.OK:
                        _playerState.currentCandidates[slotIndex] = CandidateCardExtensions.MakeEmptySlot(_playerState.currentPoolID, slotIndex);
                        // TODO(FT-10): mark dirty
                        return RecruitResult.SUCCESS;

                    case HireStaffResult.STAFF_SYSTEM_LOCKED:
                        Debug.LogError("[GachaService] FT-08/FT-12 unlock state out-of-sync during TryRecruit.");
                        return RecruitResult.STAFF_SYSTEM_LOCKED;

                    case HireStaffResult.INVALID_STAFF_ID:
                        Debug.LogError($"[GachaService] TryRecruit invalid staffID from candidate: {candidate.staffID}");
                        return RecruitResult.INVALID_STAFF_ID;

                    case HireStaffResult.ROSTER_FULL:
                        return RecruitResult.ROSTER_FULL;

                    case HireStaffResult.DUPLICATE_INSTANCE:
                        Debug.LogError("[GachaService] FT-12 returned DUPLICATE_INSTANCE; possible StaffService instance ID corruption.");
                        return RecruitResult.INTERNAL_ERROR;

                    default:
                        return RecruitResult.INTERNAL_ERROR;
                }
            }
            finally
            {
                _isHiringInFlight = false;
            }
        }

        public RejectResult TryRejectCandidate(int slotIndex)
        {
            if (!IsStaffSystemUnlocked())
            {
                return RejectResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return RejectResult.STAFF_SYSTEM_LOCKED;
            }

            int slotCount = GetInterviewSlotCountInternal();
            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                return RejectResult.INVALID_SLOT;
            }

            EnsureCurrentCandidatesSizedTo(slotCount);
            if (_playerState.currentCandidates[slotIndex].IsEmptySlot())
            {
                return RejectResult.EMPTY_SLOT;
            }

            _playerState.currentCandidates[slotIndex] = CandidateCardExtensions.MakeEmptySlot(_playerState.currentPoolID, slotIndex);
            // TODO(FT-10): mark dirty
            return RejectResult.SUCCESS;
        }

        public ReserveResult TryReserveCandidate(int slotIndex)
        {
            if (!IsStaffSystemUnlocked())
            {
                return ReserveResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return ReserveResult.STAFF_SYSTEM_LOCKED;
            }

            int slotCount = GetInterviewSlotCountInternal();
            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                return ReserveResult.INVALID_SLOT;
            }

            EnsureCurrentCandidatesSizedTo(slotCount);
            CandidateCard candidate = _playerState.currentCandidates[slotIndex];
            if (candidate.IsEmptySlot())
            {
                return ReserveResult.EMPTY_SLOT;
            }

            if (candidate.reserveConsumedFlag)
            {
                return ReserveResult.RESERVE_CONSUMED;
            }

            int maxReserve = MaxReserve(slotCount);
            if (_playerState.reservedCandidates.Count >= maxReserve)
            {
                return ReserveResult.RESERVE_FULL;
            }

            long now = GetNowUtc();
            candidate.isReserved = true;
            candidate.reservedTimestamp = now;
            _playerState.reservedCandidates.Add(candidate);

            _playerState.currentCandidates[slotIndex] = CandidateCardExtensions.MakeEmptySlot(_playerState.currentPoolID, slotIndex);
            // TODO(FT-10): mark dirty
            return ReserveResult.SUCCESS;
        }

        public ReleaseResult TryReleaseReserve(int reserveIndex)
        {
            if (!IsStaffSystemUnlocked())
            {
                return ReleaseResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return ReleaseResult.STAFF_SYSTEM_LOCKED;
            }

            if (reserveIndex < 0 || reserveIndex >= _playerState.reservedCandidates.Count)
            {
                return ReleaseResult.INVALID_INDEX;
            }

            CandidateCard reserved = _playerState.reservedCandidates[reserveIndex];
            if (reserved == null)
            {
                _playerState.reservedCandidates.RemoveAt(reserveIndex);
                // TODO(FT-10): mark dirty
                return ReleaseResult.SUCCESS;
            }

            int slotCount = GetInterviewSlotCountInternal();
            EnsureCurrentCandidatesSizedTo(slotCount);

            if (reserved.poolID == _playerState.currentPoolID)
            {
                int targetSlot = reserved.slotIndex;
                if (targetSlot >= 0 && targetSlot < slotCount)
                {
                    if (!_playerState.currentCandidates[targetSlot].IsEmptySlot())
                    {
                        return ReleaseResult.SLOT_OCCUPIED_BY_NEW_ROLL;
                    }
                }
                else
                {
                    int fallback = FindFirstEmptySlotIndex(_playerState.currentCandidates, slotCount);
                    if (fallback < 0)
                    {
                        return ReleaseResult.SLOT_OCCUPIED_BY_NEW_ROLL;
                    }
                }
            }

            ReleaseReserveInternal(reserved);
            // TODO(FT-10): mark dirty
            return ReleaseResult.SUCCESS;
        }

        public bool IsStaffSystemUnlocked()
        {
            return BuildingService.Instance != null && BuildingService.Instance.IsStaffSystemUnlocked();
        }

        public int GetCurrentPoolID()
        {
            if (!IsStaffSystemUnlocked() || !_hasState || _playerState == null)
            {
                return 0;
            }

            return _playerState.currentPoolID;
        }

        public IReadOnlyList<CandidateCard> GetCurrentCandidates()
        {
            if (!IsStaffSystemUnlocked() || !_hasState || _playerState == null || _playerState.currentCandidates == null)
            {
                return s_emptyCandidates;
            }

            return _playerState.currentCandidates;
        }

        public IReadOnlyList<CandidateCard> GetReservedCandidates()
        {
            if (!IsStaffSystemUnlocked() || !_hasState || _playerState == null || _playerState.reservedCandidates == null)
            {
                return s_emptyCandidates;
            }

            return _playerState.reservedCandidates;
        }

        public int GetPityCounter()
        {
            if (!IsStaffSystemUnlocked() || !_hasState || _playerState == null)
            {
                return 0;
            }

            return _playerState.pityCounter;
        }

        public int GetInterviewSlotCount()
        {
            if (!IsStaffSystemUnlocked() || !_isLoaderReady || !_hasState)
            {
                return 0;
            }

            return GetInterviewSlotCountInternal();
        }

        public int GetManualRefreshCost()
        {
            if (!IsStaffSystemUnlocked() || !_isLoaderReady || !_hasState)
            {
                return 0;
            }

            return _tableLoader.GetRefreshCost(GetCurrentGuildLevel());
        }

        public long GetNextAutoRefreshUtcTimestamp()
        {
            if (!IsStaffSystemUnlocked() || !_isLoaderReady || !_hasState || _playerState == null)
            {
                return 0L;
            }

            if (_playerState.lastAutoRefreshTimestamp <= 0L)
            {
                return 0L;
            }

            return _playerState.lastAutoRefreshTimestamp + GetAutoRefreshIntervalSec();
        }

        public void OnStaffSystemBoot()
        {
            long now = GetNowUtc();
            if (!IsStaffSystemUnlocked())
            {
                EventBus.Publish(new OnStaffSystemBootEvent(false, 0, 0, now));
                return;
            }

            if (!IsOperational())
            {
                EventBus.Publish(new OnStaffSystemBootEvent(true, 0, 0, now));
                return;
            }

            if (_playerState.lastAutoRefreshTimestamp == 0L || _playerState.lastAutoRefreshTimestamp > now)
            {
                _playerState.lastAutoRefreshTimestamp = now;
            }

            int releasedReserveCount = SweepExpiredReserves(now);

            int intervalSec = GetAutoRefreshIntervalSec();
            long elapsed = now - _playerState.lastAutoRefreshTimestamp;
            if (elapsed < 0L)
            {
                elapsed = 0L;
            }

            long missedIntervals = intervalSec > 0 ? elapsed / intervalSec : 0L;
            int refillCount = missedIntervals > 0 ? 1 : 0;

            int slotCount = GetInterviewSlotCountInternal();
            EnsureCurrentCandidatesSizedTo(slotCount);
            bool allCurrentEmpty = AreAllCurrentCandidatesEmpty(slotCount);

            int appliedRefreshCount = 0;
            if (refillCount == 1 || allCurrentEmpty)
            {
                RefreshResult refreshResult = ExecuteRefresh(RefreshType.OfflineRefill, _playerState.currentPoolID);
                if (refreshResult == RefreshResult.SUCCESS)
                {
                    appliedRefreshCount = 1;
                }
            }

            // Step D (salary/offline payroll) is intentionally skipped in Jam phase.
            EventBus.Publish(new OnStaffSystemBootEvent(true, appliedRefreshCount, releasedReserveCount, now));
        }

        private RefreshResult ExecuteRefresh(RefreshType type, int poolID)
        {
            if (!IsStaffSystemUnlocked())
            {
                return RefreshResult.STAFF_SYSTEM_LOCKED;
            }

            if (!IsOperational())
            {
                return RefreshResult.STAFF_SYSTEM_LOCKED;
            }

            int currentLevel = GetCurrentGuildLevel();
            long now = GetNowUtc();

            // Step 2: always release expired reserves first.
            SweepExpiredReserves(now);

            // Step 3: calculate refreshable slots.
            int slotCount = _tableLoader.GetSlotCount(currentLevel);
            EnsureCurrentCandidatesSizedTo(slotCount);

            List<int> refreshableSlots = new List<int>(slotCount);
            if (type == RefreshType.SwitchPool)
            {
                for (int i = 0; i < slotCount; i++)
                {
                    refreshableSlots.Add(i);
                }
            }
            else
            {
                bool[] reservedSlotFlags = new bool[slotCount];
                for (int i = 0; i < _playerState.reservedCandidates.Count; i++)
                {
                    CandidateCard reserved = _playerState.reservedCandidates[i];
                    if (reserved == null)
                    {
                        continue;
                    }

                    if (reserved.poolID != _playerState.currentPoolID)
                    {
                        continue;
                    }

                    if (reserved.slotIndex >= 0 && reserved.slotIndex < slotCount)
                    {
                        reservedSlotFlags[reserved.slotIndex] = true;
                    }
                }

                for (int i = 0; i < slotCount; i++)
                {
                    if (!reservedSlotFlags[i])
                    {
                        refreshableSlots.Add(i);
                    }
                }
            }

            if (refreshableSlots.Count == 0)
            {
                return RefreshResult.NO_REFRESHABLE_SLOT;
            }

            // Step 4: precompute all rolls before mutating state or charging gold.
            List<CandidateCard> pendingCards = new List<CandidateCard>(refreshableSlots.Count);
            bool pityHitInThisRefresh = false;
            for (int i = 0; i < refreshableSlots.Count; i++)
            {
                int slotIndex = refreshableSlots[i];
                bool isFirstSlotInRefresh = i == 0;
                int pityInput = pityHitInThisRefresh ? 0 : _playerState.pityCounter;

                (CandidateCard card, bool pityHit) rolled = _rollEngine.RollOneSlot(
                    slotIndex,
                    poolID,
                    isFirstSlotInRefresh,
                    pityInput);

                pendingCards.Add(rolled.card);
                if (rolled.pityHit)
                {
                    pityHitInThisRefresh = true;
                }
            }

            // Step 5: deduct gold after Step 4 succeeds (manual only).
            if (type == RefreshType.Manual)
            {
                int refreshCost = _tableLoader.GetRefreshCost(currentLevel);
                ResourceManagement resource = ResourceManagement.Instance;
                if (resource == null)
                {
                    return RefreshResult.GOLD_INSUFFICIENT;
                }

                if (resource.GetGold() < refreshCost)
                {
                    return RefreshResult.GOLD_INSUFFICIENT;
                }

                if (!resource.AddGold(-refreshCost))
                {
                    return RefreshResult.GOLD_INSUFFICIENT;
                }
            }

            // Step 6: commit cards.
            if (type == RefreshType.SwitchPool)
            {
                _playerState.currentPoolID = poolID;
            }

            for (int i = 0; i < pendingCards.Count; i++)
            {
                CandidateCard card = pendingCards[i];
                _playerState.currentCandidates[card.slotIndex] = card;
            }

            // Step 7: pity update (FSD §5.4.2 Step 7 — 命中時先 reset，再無條件累加 N).
            if (pityHitInThisRefresh)
            {
                _playerState.pityCounter = 0;
            }
            _playerState.pityCounter += refreshableSlots.Count;

            // Step 8: auto/offline timestamp update.
            if (type == RefreshType.Auto || type == RefreshType.OfflineRefill)
            {
                _playerState.lastAutoRefreshTimestamp = now;
            }

            // Step 9: mark dirty.
            // TODO(FT-10): mark dirty

            // Step 10: no refresh event publish (FSD §3.3.7).
            return RefreshResult.SUCCESS;
        }

        private void ReleaseReserveInternal(CandidateCard candidate)
        {
            if (candidate == null || _playerState == null || _playerState.reservedCandidates == null)
            {
                return;
            }

            RemoveReservedCandidate(candidate);

            candidate.isReserved = false;
            candidate.reservedTimestamp = 0L;
            candidate.reserveConsumedFlag = true;

            if (candidate.poolID != _playerState.currentPoolID)
            {
                return;
            }

            int slotCount = GetInterviewSlotCountInternal();
            EnsureCurrentCandidatesSizedTo(slotCount);

            int targetSlot = candidate.slotIndex;
            if (targetSlot < 0 || targetSlot >= slotCount)
            {
                int fallbackSlot = FindFirstEmptySlotIndex(_playerState.currentCandidates, slotCount);
                if (fallbackSlot < 0)
                {
                    Debug.LogWarning("[GachaService] ReleaseReserveInternal fallback failed: all current slots are occupied; candidate discarded.");
                    return;
                }

                candidate.slotIndex = fallbackSlot;
                targetSlot = fallbackSlot;
            }

            if (!_playerState.currentCandidates[targetSlot].IsEmptySlot())
            {
                return;
            }

            _playerState.currentCandidates[targetSlot] = candidate;
        }

        private void ValidateAndSanitizePlayerState()
        {
            if (_playerState.pityCounter < 0)
            {
                throw new StaffPlayerStateValidationException($"Invalid pityCounter={_playerState.pityCounter}.");
            }

            if (_playerState.lastAutoRefreshTimestamp < 0L)
            {
                _playerState.lastAutoRefreshTimestamp = 0L;
            }

            if (_playerState.currentPoolID <= 0)
            {
                _playerState.currentPoolID = DEFAULT_POOL_ID;
            }

            if (!TryGetPoolData(_playerState.currentPoolID, out _))
            {
                throw new StaffPlayerStateValidationException($"Invalid currentPoolID={_playerState.currentPoolID}; pool not found.");
            }

            Dictionary<int, int> staffRarityByID = BuildStaffRarityByID();
            for (int i = 0; i < _playerState.currentCandidates.Count; i++)
            {
                CandidateCard card = _playerState.currentCandidates[i];
                if (card == null)
                {
                    _playerState.currentCandidates[i] = CandidateCardExtensions.MakeEmptySlot(_playerState.currentPoolID, i);
                    continue;
                }

                ValidateCandidateCard(card, staffRarityByID);
            }

            for (int i = _playerState.reservedCandidates.Count - 1; i >= 0; i--)
            {
                CandidateCard card = _playerState.reservedCandidates[i];
                if (card == null)
                {
                    _playerState.reservedCandidates.RemoveAt(i);
                    continue;
                }

                ValidateCandidateCard(card, staffRarityByID);
                card.isReserved = true;
                if (card.reservedTimestamp <= 0L)
                {
                    throw new CandidateCardValidationException("Reserved candidate has invalid reservedTimestamp <= 0.");
                }
            }
        }

        private void ValidateCandidateCard(CandidateCard card, IReadOnlyDictionary<int, int> staffRarityByID)
        {
            if (card.staffID < 0 || card.trashItemID < 0)
            {
                throw new CandidateCardValidationException("CandidateCard has negative staffID/trashItemID.");
            }

            bool hasStaff = card.staffID > 0;
            bool hasTrash = card.trashItemID > 0;
            if (hasStaff == hasTrash)
            {
                if (hasStaff || card.rolledRarity != 0)
                {
                    throw new CandidateCardValidationException("CandidateCard must be exactly one of staff/trash, or a valid empty placeholder.");
                }
            }

            if (hasStaff)
            {
                if (!staffRarityByID.TryGetValue(card.staffID, out int rarity))
                {
                    throw new CandidateCardValidationException($"CandidateCard.staffID={card.staffID} not found in StaffTable.");
                }

                if (card.rolledRarity != rarity)
                {
                    throw new CandidateCardValidationException(
                        $"CandidateCard rarity mismatch: staffID={card.staffID}, rolledRarity={card.rolledRarity}, tableRarity={rarity}");
                }
            }

            if (hasTrash)
            {
                if (card.rolledRarity != 1)
                {
                    throw new CandidateCardValidationException(
                        $"Trash candidate must have rolledRarity=1, got {card.rolledRarity}.");
                }

                if (!TrashItemExists(card.trashItemID))
                {
                    throw new TrashItemTableValidationException(
                        $"CandidateCard.trashItemID={card.trashItemID} not found in TrashItemTable.");
                }
            }
        }

        private int SweepExpiredReserves(long nowUtc)
        {
            int released = 0;
            for (int i = _playerState.reservedCandidates.Count - 1; i >= 0; i--)
            {
                CandidateCard candidate = _playerState.reservedCandidates[i];
                if (candidate == null)
                {
                    _playerState.reservedCandidates.RemoveAt(i);
                    continue;
                }

                if (IsReserveExpired(candidate, nowUtc))
                {
                    ReleaseReserveInternal(candidate);
                    released += 1;
                }
            }

            return released;
        }

        private bool IsReserveExpired(CandidateCard candidate, long nowUtc)
        {
            if (candidate.reservedTimestamp <= 0L)
            {
                return false;
            }

            if (!TryGetPoolData(candidate.poolID, out StaffGachaPoolData pool))
            {
                return true;
            }

            long elapsed = nowUtc - candidate.reservedTimestamp;
            return elapsed >= pool.reserveTimeLimitSec;
        }

        private void EnsureCurrentCandidatesSizedTo(int slotCount)
        {
            if (slotCount < 0)
            {
                slotCount = 0;
            }

            if (_playerState.currentCandidates == null)
            {
                _playerState.currentCandidates = new List<CandidateCard>(slotCount);
            }

            while (_playerState.currentCandidates.Count > slotCount)
            {
                _playerState.currentCandidates.RemoveAt(_playerState.currentCandidates.Count - 1);
            }

            while (_playerState.currentCandidates.Count < slotCount)
            {
                int slotIndex = _playerState.currentCandidates.Count;
                _playerState.currentCandidates.Add(
                    CandidateCardExtensions.MakeEmptySlot(_playerState.currentPoolID, slotIndex));
            }
        }

        private bool RemoveReservedCandidate(CandidateCard target)
        {
            for (int i = _playerState.reservedCandidates.Count - 1; i >= 0; i--)
            {
                CandidateCard item = _playerState.reservedCandidates[i];
                if (ReferenceEquals(item, target))
                {
                    _playerState.reservedCandidates.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private int FindFirstEmptySlotIndex(IReadOnlyList<CandidateCard> list, int slotCount)
        {
            for (int i = 0; i < slotCount; i++)
            {
                CandidateCard card = list[i];
                if (card.IsEmptySlot())
                {
                    return i;
                }
            }

            return -1;
        }

        private int GetInterviewSlotCountInternal()
        {
            return _tableLoader.GetSlotCount(GetCurrentGuildLevel());
        }

        private int GetCurrentGuildLevel()
        {
            int level = MIN_GUILD_LEVEL;
            if (GuildCoreService.Instance != null)
            {
                level = GuildCoreService.Instance.GetCurrentLevel();
            }

            if (level < MIN_GUILD_LEVEL)
            {
                level = MIN_GUILD_LEVEL;
            }
            else if (level > MAX_GUILD_LEVEL)
            {
                level = MAX_GUILD_LEVEL;
            }

            return level;
        }

        private int GetAutoRefreshIntervalSec()
        {
            int roomLevel = MIN_GUILD_LEVEL;
            if (BuildingService.Instance != null)
            {
                roomLevel = BuildingService.Instance.GetBuildingLevel(STAFF_BUILDING_ID);
            }

            if (roomLevel < MIN_GUILD_LEVEL)
            {
                roomLevel = MIN_GUILD_LEVEL;
            }
            else if (roomLevel > MAX_GUILD_LEVEL)
            {
                roomLevel = MAX_GUILD_LEVEL;
            }

            int baseInterval = _tableLoader.GetAutoRefreshIntervalSec(roomLevel);
            int reduction = 0;
            if (StaffService.Instance != null)
            {
                reduction = StaffService.Instance.GetRecruitRefreshReductionSec();
            }

            if (reduction < 0)
            {
                reduction = 0;
            }

            int adjusted = baseInterval - reduction;
            if (adjusted < _tableLoader.MinAutoRefreshIntervalSec)
            {
                adjusted = _tableLoader.MinAutoRefreshIntervalSec;
            }

            if (adjusted < 1)
            {
                adjusted = 1;
            }

            return adjusted;
        }

        private int MaxReserve(int slotCount)
        {
            int computed = slotCount - 1;
            if (computed < 1)
            {
                int fallback = _tableLoader != null ? _tableLoader.MaxReserveFallback : 1;
                if (fallback < 1)
                {
                    fallback = 1;
                }

                computed = fallback;
            }

            return computed;
        }

        private bool AreAllCurrentCandidatesEmpty(int slotCount)
        {
            for (int i = 0; i < slotCount; i++)
            {
                if (!_playerState.currentCandidates[i].IsEmptySlot())
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetPoolData(int poolID, out StaffGachaPoolData pool)
        {
            pool = null;
            if (!_isLoaderReady || _tableLoader == null)
            {
                return false;
            }

            IReadOnlyList<StaffGachaPoolData> allPools = _tableLoader.GetAllPools();
            for (int i = 0; i < allPools.Count; i++)
            {
                StaffGachaPoolData row = allPools[i];
                if (row != null && row.poolID == poolID)
                {
                    pool = row;
                    return true;
                }
            }

            return false;
        }

        private bool TrashItemExists(int trashItemID)
        {
            IReadOnlyList<TrashItemData> items = _tableLoader.GetTrashItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].trashItemID == trashItemID)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<int, int> BuildStaffRarityByID()
        {
            if (DataManager.Instance == null)
            {
                throw new CandidateCardValidationException("DataManager.Instance is null during staff rarity validation.");
            }

            IReadOnlyList<StaffData> rows = DataManager.Instance.GetAll<StaffData>();
            if (rows == null)
            {
                throw new CandidateCardValidationException("StaffTable rows are null.");
            }

            Dictionary<int, int> output = new Dictionary<int, int>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                StaffData row = rows[i];
                if (row == null || row.staffID <= 0)
                {
                    continue;
                }

                if (!output.ContainsKey(row.staffID))
                {
                    output.Add(row.staffID, row.rarity);
                }
            }

            return output;
        }

        private bool IsOperational()
        {
            return _isLoaderReady && _hasState && _playerState != null && _rollEngine != null;
        }

        private static long GetNowUtc()
        {
            if (TimeSystem.Instance != null)
            {
                return TimeSystem.Instance.NowUTC;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        [Serializable]
        private sealed class StaffPlayerStateWrapper
        {
            public StaffPlayerState state;
        }

        private sealed class TrashItemTableValidationException : Exception
        {
            public TrashItemTableValidationException(string message) : base(message) { }
        }
    }
}
