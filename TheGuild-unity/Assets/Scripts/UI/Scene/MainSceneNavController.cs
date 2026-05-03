// MainSceneNavController — 主場景 7 個場景元素點擊路由（design/IconInstructions_game_elements_cutout.png）。
// 訂閱 OnUIReadyEvent 後，從 MainSceneDocument 的 rootVisualElement 取出 6 個 Button，
// 註冊 click callback 路由到 PanelManager.OpenPanel。
//
// 7 個場景元素中冒險者立繪為純裝飾（不可點），故 controller 管理 6 個 button。
// nav-master-desk / nav-bookshelf 為多入口物件，Jam 版暫採單入口（最常用功能），
// sub-menu 拆分待 Post-Jam 補。

using TheGuild.Core.Events;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Scene
{
    /// <summary>
    /// 主場景 6 個 nav button 點擊路由器（含 1 個裝飾立繪不可點）。
    /// <para>
    /// 規格對應：design/IconInstructions_game_elements_cutout.png 7 元素布局。
    /// 訂閱 OnUIReadyEvent 後 cache button 與 register click callback。
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class MainSceneNavController : MonoBehaviour
    {
        public static MainSceneNavController Instance { get; private set; }

        // Cached button references（OnUIReady 後填入）
        private Button _navLogBoard;
        private Button _navCommissionBoard;
        private Button _navCounter;
        private Button _navSafe;
        private Button _navMasterDesk;
        private Button _navBookshelf;

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

            // Cache 6 個 button（冒險者立繪為純裝飾，無 button）
            _navLogBoard        = root.Q<Button>("nav-log-board");
            _navCommissionBoard = root.Q<Button>("nav-commission-board");
            _navCounter         = root.Q<Button>("nav-counter");
            _navSafe            = root.Q<Button>("nav-safe");
            _navMasterDesk      = root.Q<Button>("nav-master-desk");
            _navBookshelf       = root.Q<Button>("nav-bookshelf");

            RegisterClickCallbacks();
            _initialized = true;
        }

        private void RegisterClickCallbacks()
        {
            if (_navLogBoard != null)        _navLogBoard.clicked        += OnLogBoardClicked;
            if (_navCommissionBoard != null) _navCommissionBoard.clicked += OnCommissionBoardClicked;
            if (_navCounter != null)         _navCounter.clicked         += OnCounterClicked;
            if (_navSafe != null)            _navSafe.clicked            += OnSafeClicked;
            if (_navMasterDesk != null)      _navMasterDesk.clicked      += OnMasterDeskClicked;
            if (_navBookshelf != null)       _navBookshelf.clicked       += OnBookshelfClicked;
        }

        private void UnregisterClickCallbacks()
        {
            if (_navLogBoard != null)        _navLogBoard.clicked        -= OnLogBoardClicked;
            if (_navCommissionBoard != null) _navCommissionBoard.clicked -= OnCommissionBoardClicked;
            if (_navCounter != null)         _navCounter.clicked         -= OnCounterClicked;
            if (_navSafe != null)            _navSafe.clicked            -= OnSafeClicked;
            if (_navMasterDesk != null)      _navMasterDesk.clicked      -= OnMasterDeskClicked;
            if (_navBookshelf != null)       _navBookshelf.clicked       -= OnBookshelfClicked;
        }

        // ──────────────────────────────────────────────────────
        //  Click handlers — 路由到 PanelManager.OpenPanel
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// Log 板 → P-03 LogFloatingWindow（Jam scope 縮減 P-03 不做，暫顯 placeholder log）。
        /// </summary>
        private void OnLogBoardClicked()
        {
            // TODO: P-03 LogFloatingWindow 接入後改為 toggle Log 視窗顯示
            Debug.Log("[MainSceneNav] 公會 Log 板（P-03 LogFloatingWindow，Jam scope 縮減暫不開面板）");
        }

        /// <summary>委託版 → CommissionBoardPanel（管理委託）。</summary>
        private void OnCommissionBoardClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.CommissionBoard);
        }

        /// <summary>
        /// 公會櫃臺 → CommissionBoardPanel（推薦委託面板）。
        /// 推薦子面板（§3.5.1.1）為 inline 子面板，由 CommissionBoardPanel 內展開；
        /// 暫直接開父面板，玩家手動點推薦按鈕進子面板。
        /// </summary>
        private void OnCounterClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.CommissionBoard);
        }

        /// <summary>保險箱 → GuildOverviewPanel（顯示金幣 / 公會狀態）。</summary>
        private void OnSafeClicked()
        {
            PanelManager.Instance?.OpenPanel(PanelID.GuildOverview);
        }

        /// <summary>
        /// 公會會長辦公桌 → 多入口（建設 / 職員管理 / 面試 / 冒險者管理）。
        /// Jam 版暫採單入口 AdventurerRoster（最常用），sub-menu 拆分待 Post-Jam 補。
        /// </summary>
        private void OnMasterDeskClicked()
        {
            // TODO: 實作辦公桌 sub-menu popup（含 GuildBuilding / StaffRoster / StaffGacha / AdventurerRoster 4 button）
            PanelManager.Instance?.OpenPanel(PanelID.AdventurerRoster);
        }

        /// <summary>
        /// 書櫃 → 多入口（公會歷史書 / 設定）。
        /// Jam 版暫採單入口 SettingsPanel；公會歷史書面板待 Post-Jam 補。
        /// </summary>
        private void OnBookshelfClicked()
        {
            // TODO: 實作書櫃 sub-menu popup（含 GuildHistory / Settings 2 button）；
            //       GuildHistory panel 設計待 narrative-director 補
            PanelManager.Instance?.OpenPanel(PanelID.SettingsPanel);
        }
    }
}
