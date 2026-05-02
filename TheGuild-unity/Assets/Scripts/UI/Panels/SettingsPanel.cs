// SettingsPanel — P-02-FSD-B §5.4.10 設定彈窗。
// 提供 UI 縮放 / 目標螢幕 / 最小化 / 離開遊戲 等設定操作，
// 所有狀態變更透過 P-01 DesktopWindowService API 執行，UI 不自行持有狀態。
//
// 設計文件：design/FSD/【P-02-FSD-B】main-ui-panels.md §5.4.10
//           design/GDD/【P-02】main-ui-framework.md §3.5.10 / §3.4.3

using System.Collections.Generic;
using TheGuild.UI.Core;
using TheGuild.UI.Platform.Win32;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = TheGuild.UI.Core.IPanel;

namespace TheGuild.UI.Panels
{
    /// <summary>
    /// 設定彈窗面板。
    /// <para>
    /// 規格對應：FSD §5.4.10 / EC-17（已斷開螢幕）/ EC-18（slider clamp）。
    /// ESC 路由由 PanelManager.OnEscape 處理（GDD §3.4.3），本面板不自行處理。
    /// </para>
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour, IPanel
    {
        [SerializeField] private StyleSheet _styleSheet;

        // ── Cached VisualElement 引用 ──
        private VisualElement _root;
        private Slider        _scaleSlider;
        private Button        _resetScaleButton;
        private DropdownField _monitorDropdown;
        private Slider        _volumeSlider;
        private DropdownField _langDropdown;
        private Button        _minimizeButton;
        private Button        _exitButton;
        private Button        _closeButton;

        // 螢幕列表快照（index → MonitorID 對映）
        private readonly List<int> _monitorIDByIndex = new List<int>();

        // IPanel 契約
        public PanelID        Id   => PanelID.SettingsPanel;
        public VisualElement  Root => _root;

        // ──────────────────────────────────────────────────────
        //  MonoBehaviour 生命週期
        // ──────────────────────────────────────────────────────

        private void Awake()
        {
            BuildVisualTree();
        }

        private void OnEnable()
        {
            PanelManager.Instance?.RegisterPanel(this);
        }

        // ──────────────────────────────────────────────────────
        //  IPanel 實作
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 開啟設定面板：從 P-01 讀取當前值填入各控制項。
        /// args 目前未使用（SettingsPanel 無需外部參數），保留 IPanel 簽章。
        /// </summary>
        public void Open(object args)
        {
            RefreshScaleSlider();
            RefreshMonitorDropdown();
            _root.style.display = DisplayStyle.Flex;
        }

        /// <summary>關閉設定面板。</summary>
        public void Close()
        {
            _root.style.display = DisplayStyle.None;
        }

        // ──────────────────────────────────────────────────────
        //  視覺樹建構（Awake 內執行，僅建構一次）
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 在 Awake 建構所有 VisualElement 並快取引用。
        /// 樣式採 UI Toolkit 內建元件（Jam scope 縮減，USS 極簡）。
        /// </summary>
        private void BuildVisualTree()
        {
            // 根節點：初始隱藏
            _root = new VisualElement { name = "settings-panel" };
            _root.AddToClassList("settings-panel");
            _root.style.display = DisplayStyle.None;

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // 內容容器
            VisualElement container = new VisualElement { name = "settings-container" };
            container.AddToClassList("settings-container");

            // 標題列
            VisualElement header = new VisualElement { name = "settings-header" };
            header.AddToClassList("settings-header");

            Label title = new Label { name = "settings-title" };
            title.AddToClassList("settings-title");
            title.text = Text("settings.title", "設定");

            _closeButton = new Button(HandleClose) { name = "settings-close" };
            _closeButton.AddToClassList("settings-close");
            _closeButton.text = Text("settings.close", "關閉");

            header.Add(title);
            header.Add(_closeButton);

            // UI 縮放 Slider
            VisualElement scaleRow = new VisualElement { name = "settings-row-scale" };
            scaleRow.AddToClassList("settings-row");

            Label scaleLabel = new Label { name = "settings-scale-label" };
            scaleLabel.AddToClassList("settings-row__label");
            scaleLabel.text = Text("settings.scale.label", "UI 縮放");

            // Slider range 從 P-01 GetUserScaleRange() 取得（FSD §5.4.10 / DoD-B15）
            (float scaleMin, float scaleMax, float scaleStep) = GetScaleRange();

            _scaleSlider = new Slider(scaleMin, scaleMax) { name = "settings-scale-slider" };
            _scaleSlider.AddToClassList("settings-scale-slider");
            _scaleSlider.pageSize = scaleStep;   // pageSize 控制鍵盤步進
            _scaleSlider.RegisterValueChangedCallback(OnScaleChanged);

            _resetScaleButton = new Button(HandleResetScale) { name = "settings-scale-reset" };
            _resetScaleButton.AddToClassList("settings-scale-reset");
            _resetScaleButton.text = Text("settings.scale.reset", "還原預設");

            scaleRow.Add(scaleLabel);
            scaleRow.Add(_scaleSlider);
            scaleRow.Add(_resetScaleButton);

            // 目標螢幕 DropdownField
            VisualElement monitorRow = new VisualElement { name = "settings-row-monitor" };
            monitorRow.AddToClassList("settings-row");

            Label monitorLabel = new Label { name = "settings-monitor-label" };
            monitorLabel.AddToClassList("settings-row__label");
            monitorLabel.text = Text("settings.monitor.label", "目標螢幕");

            _monitorDropdown = new DropdownField { name = "settings-monitor-dropdown" };
            _monitorDropdown.AddToClassList("settings-monitor-dropdown");
            _monitorDropdown.RegisterValueChangedCallback(OnMonitorChanged);

            monitorRow.Add(monitorLabel);
            monitorRow.Add(_monitorDropdown);

            // 音量 Slider（Post-Jam 預留，唯讀）
            VisualElement volumeRow = new VisualElement { name = "settings-row-volume" };
            volumeRow.AddToClassList("settings-row");

            Label volumeLabel = new Label { name = "settings-volume-label" };
            volumeLabel.AddToClassList("settings-row__label");
            volumeLabel.text = Text("settings.volume.label", "音量（Post-Jam）");

            _volumeSlider = new Slider(0f, 1f) { name = "settings-volume-slider", value = 1f };
            _volumeSlider.AddToClassList("settings-volume-slider");
            _volumeSlider.SetEnabled(false); // Post-Jam 預留，Jam 版唯讀

            volumeRow.Add(volumeLabel);
            volumeRow.Add(_volumeSlider);

            // 語言 DropdownField（Jam 版鎖 zhTW，唯讀）
            VisualElement langRow = new VisualElement { name = "settings-row-lang" };
            langRow.AddToClassList("settings-row");

            Label langLabel = new Label { name = "settings-lang-label" };
            langLabel.AddToClassList("settings-row__label");
            langLabel.text = Text("settings.lang.label", "語言");

            _langDropdown = new DropdownField(new List<string> { "zhTW" }, 0)
            {
                name = "settings-lang-dropdown"
            };
            _langDropdown.AddToClassList("settings-lang-dropdown");
            _langDropdown.SetEnabled(false); // Jam 版鎖定，Post-Jam 解鎖

            langRow.Add(langLabel);
            langRow.Add(_langDropdown);

            // 底部動作列
            VisualElement footer = new VisualElement { name = "settings-footer" };
            footer.AddToClassList("settings-footer");

            _minimizeButton = new Button(HandleMinimize) { name = "settings-minimize" };
            _minimizeButton.AddToClassList("settings-minimize");
            _minimizeButton.text = Text("settings.minimize", "最小化");

            _exitButton = new Button(HandleExit) { name = "settings-exit" };
            _exitButton.AddToClassList("settings-exit");
            _exitButton.text = Text("settings.exit", "離開遊戲");

            footer.Add(_minimizeButton);
            footer.Add(_exitButton);

            // 組合 DOM
            container.Add(header);
            container.Add(scaleRow);
            container.Add(monitorRow);
            container.Add(volumeRow);
            container.Add(langRow);
            container.Add(footer);
            _root.Add(container);
        }

        // ──────────────────────────────────────────────────────
        //  Refresh 輔助（Open 時呼叫）
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 從 P-01 讀取當前 userScale 填入 slider。
        /// </summary>
        private void RefreshScaleSlider()
        {
            float current = DesktopWindowService.Instance != null
                ? DesktopWindowService.Instance.GetUserScale()
                : 1.0f;

            // 不透過 SetValueWithoutNotify 時會觸發 callback，暫時反註冊避免循環
            _scaleSlider.UnregisterValueChangedCallback(OnScaleChanged);
            _scaleSlider.value = current;
            _scaleSlider.RegisterValueChangedCallback(OnScaleChanged);
        }

        /// <summary>
        /// 從 P-01 列舉螢幕並填入 dropdown；記錄 index → MonitorID 對映。
        /// EC-17：已斷開螢幕加「(已斷開) 」前綴（FSD §5.4.10）。
        /// </summary>
        private void RefreshMonitorDropdown()
        {
            _monitorIDByIndex.Clear();

            IReadOnlyList<MonitorInfo> monitors = DesktopWindowService.Instance != null
                ? DesktopWindowService.Instance.EnumerateAvailableMonitors()
                : System.Array.Empty<MonitorInfo>();

            List<string> choices = new List<string>(monitors.Count);
            int currentMonitorID = DesktopWindowService.Instance != null
                ? DesktopWindowService.Instance.GetCurrentTargetMonitorID()
                : 0;

            int selectedIndex = 0;
            string disconnectedPrefix = Text("settings.monitor.disconnected_prefix", "(已斷開) ");

            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo monitor = monitors[i];
                _monitorIDByIndex.Add(monitor.MonitorID);

                // EC-17：已斷開項目加前綴文字
                string displayName = monitor.IsConnected
                    ? monitor.DisplayName
                    : disconnectedPrefix + monitor.DisplayName;

                choices.Add(displayName);

                if (monitor.MonitorID == currentMonitorID)
                {
                    selectedIndex = i;
                }
            }

            // 不透過 value 設定（避免觸發 callback），先更新 choices 再設定 index
            _monitorDropdown.UnregisterValueChangedCallback(OnMonitorChanged);
            _monitorDropdown.choices = choices;

            if (choices.Count > 0)
            {
                _monitorDropdown.index = selectedIndex;
            }

            _monitorDropdown.RegisterValueChangedCallback(OnMonitorChanged);
        }

        // ──────────────────────────────────────────────────────
        //  事件處理（UI 只發命令，不持有狀態）
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// Slider 值變更：轉發給 P-01 SetUserScale。
        /// EC-18：Slider lowValue/highValue 已 clamp，P-01 SetUserScale 內部再次 clamp（雙重保險）。
        /// </summary>
        private void OnScaleChanged(ChangeEvent<float> evt)
        {
            DesktopWindowService.Instance?.SetUserScale(evt.newValue);
        }

        /// <summary>
        /// 還原縮放預設值：呼叫 P-01 ResetUserScaleToDefault，並更新 slider 顯示。
        /// </summary>
        private void HandleResetScale()
        {
            DesktopWindowService.Instance?.ResetUserScaleToDefault();

            // 還原後從 P-01 讀取實際值更新顯示（預設 1.0f，但以 P-01 回傳為準）
            RefreshScaleSlider();
        }

        /// <summary>
        /// 螢幕 dropdown 選擇變更：轉換 index → MonitorID 後呼叫 P-01 SwitchTargetScreen。
        /// EC-17：已斷開螢幕由 P-01 SwitchTargetScreen 內部 fallback 主螢幕 + LogWarning。
        /// </summary>
        private void OnMonitorChanged(ChangeEvent<string> evt)
        {
            int selectedIndex = _monitorDropdown.index;
            if (selectedIndex < 0 || selectedIndex >= _monitorIDByIndex.Count)
            {
                return;
            }

            int monitorID = _monitorIDByIndex[selectedIndex];

            // EC-17：已斷開螢幕的 UI 側 log（P-01 SwitchTargetScreen 內部已有 LogWarning）
            if (DesktopWindowService.Instance != null)
            {
                IReadOnlyList<MonitorInfo> monitors = DesktopWindowService.Instance.EnumerateAvailableMonitors();
                if (selectedIndex < monitors.Count && !monitors[selectedIndex].IsConnected)
                {
                    Debug.Log("[Settings] 目標螢幕已斷開，已切回主螢幕");
                }
            }

            DesktopWindowService.Instance?.SwitchTargetScreen(monitorID);
        }

        /// <summary>最小化：呼叫 P-01 Minimize。</summary>
        private void HandleMinimize()
        {
            DesktopWindowService.Instance?.Minimize();
        }

        /// <summary>
        /// 離開遊戲：顯示 Destructive 確認彈窗，確認後 Application.Quit。
        /// </summary>
        private void HandleExit()
        {
            if (PanelManager.Instance == null)
            {
                return;
            }

            PanelManager.Instance.ShowConfirm(new ConfirmArgs(
                messageKey: "confirm.exit.body",
                onConfirm:  () => Application.Quit(),
                onCancel:   null,
                style:      ConfirmStyle.Destructive,
                titleKey:   "confirm.exit.title"
            ));
        }

        /// <summary>關閉設定面板。</summary>
        private void HandleClose()
        {
            PanelManager.Instance?.ClosePanel(PanelID.SettingsPanel);
        }

        // ──────────────────────────────────────────────────────
        //  輔助方法
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 取得縮放範圍。優先從 P-01 GetUserScaleRange() 取得；
        /// P-01 不可用時回傳與 P01Tuning.asset 預設值對齊的安全值（FSD §5.4.10 / EC-18）。
        /// </summary>
        private static (float min, float max, float step) GetScaleRange()
        {
            if (DesktopWindowService.Instance != null)
            {
                return DesktopWindowService.Instance.GetUserScaleRange();
            }

            // P-01 不可用時的 fallback（對齊 P01Tuning.asset 預設值）
            return (0.5f, 2.0f, 0.1f);
        }

        /// <summary>透過 UITextService 查文字；Service 不可用時回傳 fallback。</summary>
        private static string Text(string key, string fallback)
        {
            return UITextService.Instance == null
                ? fallback
                : UITextService.Instance.Lookup(key, fallback);
        }
    }
}
