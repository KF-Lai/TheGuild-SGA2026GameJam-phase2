using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using TheGuild.Core.Events;
using TheGuild.Core.SaveContract;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02-C 冒險者名冊（concrete singleton MonoBehaviour）。
    /// 管理名冊 List、CRUD API、狀態機轉換、ISaveable stub。
    /// GDD §3.1 / §3.4 / §3.5 / §6.4。
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class AdventurerRoster : MonoBehaviour, ISaveable
    {
        // === v3.1 patch P3.1-005：審查處 buildingID（FT-07 確認後如有變更從 SystemConstants 讀取）===
        // buildingID=4（公會塔/審查處）；InitializeAsNewGame 時 level=1 即解鎖
        private const int ScrutinyOfficeBuildingID = 4;

        private readonly List<AdventurerInstance> _roster = new List<AdventurerInstance>(32);
        private int _nextInstanceID = 1;

        private AdventurerTemplateLoader _loader;
        private AdventurerFactory _factory;

        /// <summary>
        /// ISaveable stub：owner key。
        /// </summary>
        public string OwnerKey => "c02AdventurerRoster";

        /// <summary>
        /// ISaveable stub：是否為 critical（名冊損毀視為存檔不可用）。
        /// </summary>
        public bool IsCritical => true;

        public static AdventurerRoster Instance { get; private set; }

        // ── RuntimeInitializeOnLoadMethod ────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<AdventurerTemplate>("AdventurerTemplate");
            DataManager.RegisterTable<RecruitCostEntry>("RecruitCostTable");
        }

        // ── 查詢 API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 取得名冊全部冒險者（含所有狀態，含 Dead）。
        /// === v3.1 patch P3.1-005：排序規則 —— 奧菲莉雅（OPHELIA_TEMPLATE_ID）永遠第一格；
        ///     其餘依 idleSinceTimestamp 倒序。OPHELIA_TEMPLATE_ID 從 SystemConstants 讀取。===
        /// </summary>
        public IReadOnlyList<AdventurerInstance> GetRoster()
        {
            if (_roster.Count <= 1)
            {
                return _roster;
            }

            int opheliaTemplateID = DataManager.Instance != null
                ? (int)DataManager.Instance.GetFloat("OPHELIA_TEMPLATE_ID")
                : 901;

            List<AdventurerInstance> sorted = new List<AdventurerInstance>(_roster.Count);

            // 奧菲莉雅優先
            AdventurerInstance ophelia = null;
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].templateID == opheliaTemplateID)
                {
                    ophelia = _roster[i];
                    break;
                }
            }

            if (ophelia != null)
            {
                sorted.Add(ophelia);
            }

            // 其餘依 idleSinceTimestamp 倒序（值越大 = 越晚進入 Idle = 越新，排前面）
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i] != ophelia)
                {
                    sorted.Add(_roster[i]);
                }
            }

            // 穩定排序：奧菲莉雅已在 index 0，其餘按 idleSinceTimestamp 倒序
            if (sorted.Count > 1)
            {
                int startIdx = ophelia != null ? 1 : 0;
                sorted.Sort(startIdx, sorted.Count - startIdx,
                    Comparer<AdventurerInstance>.Create((a, b) => b.idleSinceTimestamp.CompareTo(a.idleSinceTimestamp)));
            }

            return sorted;
        }

        /// <summary>
        /// 依狀態篩選（精確篩選；FT-02 / P-02 使用）。
        /// </summary>
        public IReadOnlyList<AdventurerInstance> GetByStatus(AdventurerStatus status)
        {
            List<AdventurerInstance> result = new List<AdventurerInstance>();
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].status == status)
                {
                    result.Add(_roster[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 依 instanceID 查單筆；找不到回傳 null。
        /// </summary>
        public AdventurerInstance GetAdventurer(int instanceID)
        {
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].instanceID == instanceID)
                {
                    return _roster[i];
                }
            }

            return null;
        }

        // === v3.1 patch P3.1-005：GetAdventurerByID 別名 ===
        /// <summary>
        /// GetAdventurer 的 v3.1 別名；依 adventurerID（= instanceID）查單筆；找不到回傳 null。
        /// </summary>
        public AdventurerInstance GetAdventurerByID(int adventurerID) => GetAdventurer(adventurerID);

        /// <summary>
        /// 名冊人數（含 Dead）。
        /// </summary>
        public int GetRosterCount()
        {
            return _roster.Count;
        }

        /// <summary>
        /// 名冊是否已滿。
        /// </summary>
        public bool IsRosterFull(int rosterCap)
        {
            return _roster.Count >= rosterCap;
        }

        // ── 寫入 API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 分配下一個唯一 instanceID（原子操作）。
        /// FSD §5.1 T1-C02 裁決：allocator 歸屬 Roster。
        /// </summary>
        public int AllocateInstanceID()
        {
            return _nextInstanceID++;
        }

        /// <summary>
        /// 加入冒險者至名冊。
        /// rosterCap 防守由呼叫端（FT-01）負責；本方法只做 isUnique 衝突防守。
        /// FSD §5.1 T1-C02 裁決。
        /// </summary>
        public bool AddAdventurer(AdventurerInstance instance)
        {
            if (instance == null)
            {
                Debug.LogWarning("[AdventurerRoster] AddAdventurer: instance 為 null。");
                return false;
            }

            // isUnique 衝突防守（templateID != 0 時檢查；含 Dead 狀態）
            if (instance.templateID != 0)
            {
                bool isUnique = IsTemplateUnique(instance.templateID);
                if (isUnique)
                {
                    for (int i = 0; i < _roster.Count; i++)
                    {
                        if (_roster[i].templateID == instance.templateID)
                        {
                            Debug.LogWarning($"[AdventurerRoster] AddAdventurer: templateID={instance.templateID} isUnique=1，名冊中已存在，回傳 false。");
                            return false;
                        }
                    }
                }
            }

            // 設定 idleSinceTimestamp（若加入時已是 Idle）
            if (instance.status == AdventurerStatus.Idle)
            {
                instance.idleSinceTimestamp = GetNowUTC();
            }

            _roster.Add(instance);
            EventBus.Publish(new OnAdventurerAddedEvent(instance.instanceID));
            return true;
        }

        // === v3.1 patch P3.1-005：RegisterUniqueAdventurer API（§4.3b 偽碼實作）===
        /// <summary>
        /// 將唯一冒險者模板（isUnique=1）加入名冊，**繞過 rosterCap 容量上限**。
        /// 用途：FT-10 InitializeAsNewGame 將奧菲莉雅放入初始名冊。
        /// Guard：模板不存在、isUnique!=1、或名冊中已存在同 templateID → 回傳 false。
        /// GDD §4.3b / v3.1 patch P3.1-005 §3.1.1。
        /// </summary>
        public bool RegisterUniqueAdventurer(int templateID)
        {
            if (_loader == null)
            {
                Debug.LogError("[C-02] RegisterUniqueAdventurer: _loader 為 null，無法驗證模板。");
                return false;
            }

            AdventurerTemplate template = _loader.GetTemplate(templateID);
            if (template == null)
            {
                Debug.LogWarning($"[C-02] RegisterUniqueAdventurer: templateID={templateID} 找不到模板，回傳 false。");
                return false;
            }

            if (template.isUnique != 1)
            {
                Debug.LogWarning($"[C-02] RegisterUniqueAdventurer: templateID={templateID} isUnique={template.isUnique}，非唯一模板，回傳 false。");
                return false;
            }

            // 已存在同 templateID 的實例（含 Dead）→ 拒絕
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].templateID == templateID)
                {
                    Debug.LogWarning($"[C-02] RegisterUniqueAdventurer: templateID={templateID} 名冊中已存在（instanceID={_roster[i].instanceID}），回傳 false。");
                    return false;
                }
            }

            if (_factory == null)
            {
                Debug.LogError("[C-02] RegisterUniqueAdventurer: _factory 為 null，無法建立實例。");
                return false;
            }

            AdventurerInstance instance = _factory.CreateFromTemplate(templateID);
            if (instance == null)
            {
                Debug.LogError($"[C-02] RegisterUniqueAdventurer: CreateFromTemplate({templateID}) 回傳 null。");
                return false;
            }

            // 設定 idleSinceTimestamp（加入時為 Idle）
            instance.idleSinceTimestamp = GetNowUTC();

            // 不檢查 rosterCap，直接加入
            _roster.Add(instance);
            EventBus.Publish(new OnAdventurerAddedEvent(instance.instanceID));
            return true;
        }

        /// <summary>
        /// 更新冒險者狀態，並維護 currentMissionID / idleSinceTimestamp 不變式。
        /// FSD §5.4 流程 B。
        /// </summary>
        public void UpdateStatus(int instanceID, AdventurerStatus newStatus, int missionInstanceID = 0)
        {
            AdventurerInstance instance = GetAdventurer(instanceID);
            if (instance == null)
            {
                Debug.LogWarning($"[AdventurerRoster] UpdateStatus: instanceID={instanceID} 不存在。");
                return;
            }

            AdventurerStatus prev = instance.status;

            // 維護不變式：非 Wounded 時 woundedUntilTimestamp 必須清零（FSD §5.3）
            if (newStatus != AdventurerStatus.Wounded)
            {
                instance.woundedUntilTimestamp = 0;
            }

            switch (newStatus)
            {
                case AdventurerStatus.Dispatched:
                    instance.currentMissionID = missionInstanceID;
                    instance.idleSinceTimestamp = 0;
                    break;
                case AdventurerStatus.Idle:
                    instance.currentMissionID = 0;
                    instance.idleSinceTimestamp = GetNowUTC();
                    break;
                default:
                    // Wounded / Dead
                    instance.currentMissionID = 0;
                    instance.idleSinceTimestamp = 0;
                    break;
            }

            instance.status = newStatus;
            EventBus.Publish(new OnAdventurerStatusChangedEvent(instanceID, prev, newStatus));
        }

        /// <summary>
        /// 將冒險者設為 Wounded（使用 WOUNDED_RECOVERY_HOURS 預設時長）。
        /// === v3.1 patch P3.1-005 R2：向後相容舊呼叫；委派至 SetWounded(int, int?) ===
        /// FSD §5.4 流程 B。
        /// </summary>
        public void SetWounded(int instanceID) => SetWounded(instanceID, null);

        // === v3.1 patch P3.1-005 R2：SetWounded 擴充 customDurationHours ===
        /// <summary>
        /// 將冒險者設為 Wounded（計算並寫入 woundedUntilTimestamp）。
        /// v3.1 放寬：允許 Idle 或 Dispatched 狀態；Dead 狀態拒絕；Wounded 狀態更新時長。
        /// customDurationHours 為 null 時使用 SystemConstants WOUNDED_RECOVERY_HOURS。
        /// FSD §5.4 流程 B / v3.1 patch P3.1-005 R2。
        /// </summary>
        public void SetWounded(int instanceID, int? customDurationHours)
        {
            AdventurerInstance instance = GetAdventurer(instanceID);
            if (instance == null)
            {
                Debug.LogWarning($"[C-02] SetWounded: instanceID={instanceID} 不存在。");
                return;
            }

            // Dead 狀態永遠拒絕
            if (instance.status == AdventurerStatus.Dead)
            {
                Debug.LogWarning($"[C-02] SetWounded: instanceID={instanceID} 狀態為 Dead，不可設為 Wounded。");
                return;
            }

            long now = GetNowUTC();
            if (now == 0)
            {
                Debug.LogError("[C-02] SetWounded: NowUTC=0，無法計算 woundedUntilTimestamp，無操作。");
                return;
            }

            float recoveryHours;
            if (customDurationHours.HasValue)
            {
                recoveryHours = customDurationHours.Value;
            }
            else
            {
                if (DataManager.Instance == null)
                {
                    Debug.LogError("[C-02] SetWounded: DataManager.Instance 為 null，無法讀取 WOUNDED_RECOVERY_HOURS，無操作。");
                    return;
                }

                recoveryHours = DataManager.Instance.GetFloat("WOUNDED_RECOVERY_HOURS");
                if (recoveryHours <= 0f)
                {
                    Debug.LogError($"[C-02] SetWounded: WOUNDED_RECOVERY_HOURS={recoveryHours} 非法，無操作（依 FSD §6.3 禁止 fallback 硬編碼）。");
                    return;
                }
            }

            AdventurerStatus prevStatus = instance.status;
            instance.woundedUntilTimestamp = now + (long)(recoveryHours * 3600f);
            instance.status = AdventurerStatus.Wounded;
            instance.currentMissionID = 0;
            instance.idleSinceTimestamp = 0;

            EventBus.Publish(new OnAdventurerStatusChangedEvent(instanceID, prevStatus, AdventurerStatus.Wounded));
        }

        /// <summary>
        /// 除名冒險者。
        /// GDD §3.4 規則 4 / === v3.1 patch P3.1-005 放寬 ===：
        ///   - Dead 狀態：永遠可除名（既有規則）。
        ///   - Idle 狀態：FT-07 審查處（buildingID=ScrutinyOfficeBuildingID）已解鎖時可除名。
        ///   - Dispatched / Wounded：拒絕。
        /// FSD §5.1 / GDD §3.4 規則 4。
        /// </summary>
        public bool DismissAdventurer(int instanceID)
        {
            AdventurerInstance instance = GetAdventurer(instanceID);
            if (instance == null)
            {
                Debug.LogWarning($"[C-02] DismissAdventurer: instanceID={instanceID} 不存在。");
                return false;
            }

            // Dead 狀態：永遠可除名（既有規則）
            if (instance.status == AdventurerStatus.Dead)
            {
                _roster.Remove(instance);
                EventBus.Publish(new OnAdventurerDismissedEvent(instanceID));
                return true;
            }

            // === v3.1 patch P3.1-005：Idle 放寬規則 ===
            if (instance.status == AdventurerStatus.Idle)
            {
                // 查詢 FT-07 審查處是否解鎖（level >= 1）
                BuildingService buildingService = BuildingService.Instance;
                bool scrutinyUnlocked = false;
                if (buildingService != null)
                {
                    try
                    {
                        scrutinyUnlocked = buildingService.GetBuildingLevel(ScrutinyOfficeBuildingID) >= 1;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[C-02] DismissAdventurer({instanceID}): 查詢審查處等級失敗（{ex.Message}），視為未解鎖。");
                    }
                }

                if (!scrutinyUnlocked)
                {
                    Debug.LogWarning($"[C-02] DismissAdventurer({instanceID}): Idle 冒險者除名失敗——FT-07 審查處（buildingID={ScrutinyOfficeBuildingID}）未解鎖。");
                    return false;
                }

                _roster.Remove(instance);
                EventBus.Publish(new OnAdventurerDismissedEvent(instanceID));
                return true;
            }

            // Dispatched / Wounded → 拒絕
            Debug.LogWarning($"[C-02] DismissAdventurer({instanceID}): 狀態 {instance.status} 不可除名。");
            return false;
        }

        /// <summary>
        /// 每秒 Tick：將到期的 Wounded 冒險者轉為 Idle。
        /// 熱路徑：index loop 避免 alloc。
        /// FSD §5.4 流程 C。
        /// </summary>
        public void TickWoundedRecovery()
        {
            long now = GetNowUTC();
            if (now == 0)
            {
                Debug.LogError("[AdventurerRoster] TickWoundedRecovery: NowUTC=0，跳過本次 Tick。");
                return;
            }

            for (int i = 0; i < _roster.Count; i++)
            {
                AdventurerInstance instance = _roster[i];
                if (instance.status != AdventurerStatus.Wounded)
                {
                    continue;
                }

                if (now >= instance.woundedUntilTimestamp)
                {
                    instance.status = AdventurerStatus.Idle;
                    instance.woundedUntilTimestamp = 0;
                    instance.idleSinceTimestamp = now;

                    EventBus.Publish(new OnAdventurerRecoveredEvent(instance.instanceID));
                    EventBus.Publish(new OnAdventurerStatusChangedEvent(instance.instanceID, AdventurerStatus.Wounded, AdventurerStatus.Idle));
                }
            }
        }

        /// <summary>
        /// FT-03 寫入最近一次自主接單時間戳。
        /// </summary>
        public void SetLastAutoPickupTimestamp(int instanceID, long timestamp)
        {
            AdventurerInstance instance = GetAdventurer(instanceID);
            if (instance == null)
            {
                Debug.LogWarning($"[AdventurerRoster] SetLastAutoPickupTimestamp: instanceID={instanceID} 不存在。");
                return;
            }

            instance.lastAutoPickupTimestamp = timestamp;
        }

        /// <summary>
        /// 查詢 rank 對應的招募費用（委派至 Loader）。
        /// FSD §5.1 GetRecruitCost / §8.3 B-02。
        /// </summary>
        public (int cost, int reputationReq) GetRecruitCost(string rank)
        {
            if (_loader == null)
            {
                Debug.LogError("[AdventurerRoster] GetRecruitCost: _loader 為 null。");
                return (0, 0);
            }

            return _loader.GetRecruitCost(rank);
        }

        /// <summary>
        /// 取得工廠實例（供外部測試或系統呼叫）。
        /// </summary>
        public AdventurerFactory GetFactory()
        {
            return _factory;
        }

        // ── ISaveable Stub ────────────────────────────────────────────────────────

        /// <summary>
        /// 序列化名冊與 _nextInstanceID 為 JSON 字串。
        /// FSD §6.4 Serialize() 流程。
        /// </summary>
        public string Serialize()
        {
            C02SaveDTO dto = new C02SaveDTO
            {
                roster = new List<AdventurerInstance>(_roster),
                nextInstanceID = _nextInstanceID
            };

            return JsonUtility.ToJson(dto);
        }

        /// <summary>
        /// 從 JSON 還原名冊（依 FSD §6.4 驗證分流 7 條規則）。
        /// </summary>
        public void RestoreFromSave(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[AdventurerRoster] RestoreFromSave: json 為空，無操作。");
                return;
            }

            C02SaveDTO dto;
            try
            {
                dto = JsonUtility.FromJson<C02SaveDTO>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AdventurerRoster] RestoreFromSave 反序列化失敗：{ex.Message}");
                throw;
            }

            if (dto == null)
            {
                Debug.LogError("[AdventurerRoster] RestoreFromSave: dto 反序列化為 null。");
                return;
            }

            _roster.Clear();
            List<AdventurerInstance> rawList = dto.roster ?? new List<AdventurerInstance>();
            bool hadData = rawList.Count > 0;
            int skipCount = 0;

            for (int i = 0; i < rawList.Count; i++)
            {
                AdventurerInstance inst = rawList[i];
                if (inst == null)
                {
                    skipCount++;
                    continue;
                }

                // 規則 5：status 為非法 enum 值 → 跳過實例
                if (!Enum.IsDefined(typeof(AdventurerStatus), inst.status))
                {
                    Debug.LogError($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} status={inst.status} 非法，跳過。");
                    skipCount++;
                    continue;
                }

                // 規則 2：professionID 找不到 → 跳過
                if (ProfessionService.Instance != null && ProfessionService.Instance.GetProfession(inst.professionID) == null)
                {
                    Debug.LogError($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} professionID={inst.professionID} 找不到，跳過。");
                    skipCount++;
                    continue;
                }

                // 規則 1：templateID 找不到（模板已從 CSV 移除）→ 保留實例，templateID 改為 0
                if (inst.templateID != 0 && _loader != null && _loader.GetTemplate(inst.templateID) == null)
                {
                    Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} templateID={inst.templateID} 找不到，清零。");
                    inst.templateID = 0;
                }

                // 規則 3：raceID 非 0 但找不到 → 保留實例，改為 fallback raceID=1
                if (inst.raceID != 0 && RaceService.Instance != null && RaceService.Instance.GetRace(inst.raceID) == null)
                {
                    Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} raceID={inst.raceID} 找不到，改為 1。");
                    inst.raceID = 1;
                }

                // 規則 4：過濾不存在的 traitID
                if (inst.traitIDs != null && TraitService.Instance != null)
                {
                    List<int> filteredTraits = new List<int>(inst.traitIDs.Length);
                    for (int t = 0; t < inst.traitIDs.Length; t++)
                    {
                        int traitID = inst.traitIDs[t];
                        if (TraitService.Instance.GetTrait(traitID) == null)
                        {
                            Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} traitID={traitID} 找不到，過濾。");
                        }
                        else
                        {
                            filteredTraits.Add(traitID);
                        }
                    }

                    inst.traitIDs = filteredTraits.ToArray();
                }

                // 規則 6：currentMissionID != 0 但 Dispatched 以外的狀態 → 降回 Idle，清空 currentMissionID
                // （對應 FT-02 ActiveMission 不存在；FT-02 RestoreFromSave 後由 FT-02 自行做深度驗證）
                if (inst.currentMissionID != 0 && inst.status != AdventurerStatus.Dispatched)
                {
                    Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} currentMissionID={inst.currentMissionID} 但 status={inst.status}，降回 Idle。");
                    inst.status = AdventurerStatus.Idle;
                    inst.currentMissionID = 0;
                }

                // 規則 7（補）：非 Wounded 狀態但 woundedUntilTimestamp != 0 → 清零（不變式正規化，FSD §5.3）
                if (inst.status != AdventurerStatus.Wounded && inst.woundedUntilTimestamp != 0)
                {
                    Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: instanceID={inst.instanceID} status={inst.status} 但 woundedUntilTimestamp={inst.woundedUntilTimestamp}，清零。");
                    inst.woundedUntilTimestamp = 0;
                }

                // v1.1 D-01 patch：bio null 正規化（Unity JsonUtility 對舊存檔可能填 null，不影響功能但避免後續呼叫 NRE）
                if (inst.bio == null)
                {
                    inst.bio = string.Empty;
                }

                // gender 範圍正規化（合法 0/1/2，超出範圍轉 0）
                if (inst.gender < 0 || inst.gender > 2)
                {
                    inst.gender = 0;
                }

                _roster.Add(inst);
            }

            // 規則 7：_nextInstanceID 不可小於 max(roster.instanceID) + 1
            int maxID = 0;
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].instanceID > maxID)
                {
                    maxID = _roster[i].instanceID;
                }
            }

            int savedNextID = dto.nextInstanceID;
            int requiredNextID = maxID + 1;
            if (savedNextID < requiredNextID)
            {
                Debug.LogWarning($"[AdventurerRoster] RestoreFromSave: _nextInstanceID={savedNextID} < max+1={requiredNextID}，自動修正。");
                _nextInstanceID = requiredNextID;
            }
            else
            {
                _nextInstanceID = savedNextID;
            }

            // 全部失敗且原始資料非空 → 拋例外
            if (hadData && skipCount == rawList.Count && _roster.Count == 0)
            {
                throw new InvalidOperationException("[AdventurerRoster] RestoreFromSave: 所有實例驗證失敗，名冊完全空白，觸發整檔回退。");
            }
        }

        /// <summary>
        /// 新遊戲初始化（空名冊）。
        /// FSD §6.4 InitializeAsNewGame()。
        /// </summary>
        public void InitializeAsNewGame()
        {
            _roster.Clear();
            _nextInstanceID = 1;
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeInstance();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── Test Support ──────────────────────────────────────────────────────────

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

        // ── 私有方法 ──────────────────────────────────────────────────────────────

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

            _loader = new AdventurerTemplateLoader().Build();
            _factory = new AdventurerFactory(_loader, this);
        }

        private static long GetNowUTC()
        {
            return TimeSystem.Instance != null
                ? TimeSystem.Instance.NowUTC
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private bool IsTemplateUnique(int templateID)
        {
            if (_loader == null)
            {
                return false;
            }

            AdventurerTemplate tmpl = _loader.GetTemplate(templateID);
            return tmpl != null && tmpl.isUnique == 1;
        }

        // ── 序列化 DTO ────────────────────────────────────────────────────────────

        [Serializable]
        private sealed class C02SaveDTO
        {
            public List<AdventurerInstance> roster;
            public int nextInstanceID;
        }
    }
}
