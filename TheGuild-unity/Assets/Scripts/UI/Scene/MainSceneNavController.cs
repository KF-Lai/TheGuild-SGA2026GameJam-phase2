// MainSceneNavController — 主場景 6 個 nav button 點擊路由（GDD §3.2）。
// 訂閱 OnUIReadyEvent 後，從 MainSceneDocument 的 rootVisualElement 取出 6 個 Button，
// 註冊 click callback 路由到 PanelManager.OpenPanel。
//
// nav_staff_lounge 受 FT-07 BuildingService 解鎖閘控制（GetBuildingLevel(6) >= 1），
// 未解鎖時 button 顯示 disabled 樣式並 LogWarning（Jam scope 縮減：不顯 P-03 toast）。

using TheGuild.Core.Events;
using TheGuild.Gameplay.Building;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Scene
{
    /// <summary>
    /// 主場景 6 個 nav button 點擊路由器。
    /// <para>
    /// 規格對應：GDD §3.2 場景物件互動點（6 行固定路由表）。
    /// 訂閱 OnUIReadyEvent 後 cache button 與 register click callback。
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class MainSceneNavController : MonoBehaviour
    {
        // 對齊 GDD §3.5.4 / FSD-B B1 解鎖閘 buildingID
        private const int STAFF_LOUNGE_BUILDING_ID = 6;

        public static MainSceneNavController Instance { get; private set; }

        // Cached button references（OnUIReady 後填入）
        private Button _navCommissionBoard;
        private Button _navGuildHall;
        private Button _navConstruction;
        private Button _navSafe;
        private Button _navStaffLounge;
        private Button _navSettingsDesk;

        private bool _initialized;
        private bool _eventSubscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            SubscribeOnUIReady();
        }

        private void OnDisable()
        {
            UnsubscribeOnUIReady();
            UnregisterClickCallbacks();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void SubscribeOnUIReady()
        {
            if (_eventSubscribed)
            {
                return;
            }

            EventBus.Subscribe<OnUIReadyEvent>(HandleOnUIReady);
            _eventSubscribed = true;
        }

        private void UnsubscribeOnUIReady()
        {
            if (!_eventSubscribed)
            {
                return;
            }

            EventBus.Unsubscribe<OnUIReadyEvent>(HandleOnUIReady);
            _eventSubscribed = false;
        }

        private void HandleOnUIReady(OnUIReadyEvent evt)
        {
            Initialize();
        }

        /// <summary>
        /// Cache 6 個 nav button 並註冊 click callback。
        /// 重複呼叫安全（_initialized guard）。
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            if (UIBootstrapController.Instance == null
                || UIBootstrapController.Instance.MainSceneDocument == null)
            {
                Debug.LogWarning("[MainSceneNavController] UIBootstrapController.MainSceneDocument 不可用，跳過初始化。");
                return;
            }

            VisualElement root = UIBootstrapController.Instance.MainSceneDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[MainSceneNavController] MainSceneDocument.rootVisualElement 為 null，跳過初始化。");
                return;
            }

            // Cache 6 個 button
            _navCommissionBoard = root.Q<Button>("nav-commission-board");
            _navGuildHall       = root.Q<Button>("nav-guild-hall");
            _navConstruction    = root.Q<Button>("nav-construction");
            _navSafe            = root.Q<Button>("nav-safe");
            _navStaffLounge     = root.Q<Button>("nav-staff-lounge");
            _navSettingsDesk    = root.Q<Button>("nav-settings-desk");

            RegisterClickCallbacks();
            RefreshStaffLoungeLockState();

            _initialized = true;
        }

        private void RegisterClickCallbacks()
        {
            if (_navCommissionBoard != null) _navCommissionBoard.clicked += OnCommissionBoardClicked;
            if (_navGuildHall != null)       _navGuildHall.clicked       += OnGuildHallClicked;
            if (_navConstruction != null)    _navConstruction.clicked    += OnConstructionClicked;
            if (_navSafe != null)            _navSafe.clicked            += OnSafeClicked;
            if (_navStaffLounge != null)     _navStaffLounge.clicked     += OnStaffLoungeClicked;
            if (_navSettingsDesk != null)    _navSettingsDesk.clicked    += OnSettingsDeskClicked;
        }

        private void UnregisterClickCallbacks()
        {
            if (_navCommissionBoard != null) _navCommissionBoard.clicked -= OnCommissionBoardClicked;
            if (_navGuildHall != null)       _navGuildHall.clicked       -= OnGuildHallClicked;
            if (_navConstruction != null)    _navConstruction.clicked    -= OnConstructionClicked;
            if (_navSafe != null)            _navSafe.clicked            -= OnSafeClicked;
            if (_navStaffLounge != null)     _navStaffLounge.clicked     -= OnStaffLoungeClicked;
            if (_navSettingsDesk != null)    _navSettingsDesk.clicked    -= OnSettingsDeskClicked;
        }

        // ──────────────────────────────────────────────────────
        //  Click handlers — 路由到 PanelManager.OpenPanel
        // ──────────────────────────────────────────────────────

        private void OnCommissionBoardClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.CommissionBoard);
        }

        private void OnGuildHallClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.AdventurerRoster);
        }

        private void OnConstructionClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.GuildBuilding);
        }

        private void OnSafeClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.GuildOverview);
        }

        private void OnStaffLoungeClicked()
        {
            // FT-07 解鎖閘（GDD §3.2 nav_staff_lounge：FT07.GetBuildingLevel(6) >= 1）
            if (!IsStaffLoungeUnlocked())
            {
                Debug.Log("[MainSceneNav] 職員休息室未解鎖（需建造 L1）");
                return;
            }

            PanelManager.Instance?.OpenPanel(PanelID.StaffRoster);
        }

        private void OnSettingsDeskClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.SettingsPanel);
        }

        // ──────────────────────────────────────────────────────
        //  解鎖閘 / 視覺刷新
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 重新刷新 nav_staff_lounge 的解鎖視覺狀態（依 FT-07 BuildingService）。
        /// 應在 Initialize 與 BuildingUpgraded 事件後呼叫。
        /// </summary>
        public void RefreshStaffLoungeLockState()
        {
            if (_navStaffLounge == null)
            {
                return;
            }

            bool unlocked = IsStaffLoungeUnlocked();
            _navStaffLounge.SetEnabled(unlocked);
        }

        private static bool IsStaffLoungeUnlocked()
        {
            if (BuildingService.Instance == null)
            {
                return false;
            }

            return BuildingService.Instance.GetBuildingLevel(STAFF_LOUNGE_BUILDING_ID) >= 1;
        }
    }
}
