using TheGuild.Core.Events;
using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Core
{
    [DefaultExecutionOrder(-150)]
    public sealed class UIBootstrapController : MonoBehaviour
    {
        [SerializeField] private UIDocument _mainSceneDocument;
        [SerializeField] private UIDocument _overlayPanelDocument;

        private bool _bootstrapped;

        public static UIBootstrapController Instance { get; private set; }
        public bool IsUIReady { get; private set; }

        /// <summary>主場景 UIDocument（P-01 WindowHitTester 取 IPanel 使用）。</summary>
        public UIDocument MainSceneDocument => _mainSceneDocument;

        // 測試專用：直接設定 IsUIReady 避免 reflection 在 NUnit + EditMode 下偶發未生效。
        internal void SetUIReadyForTests(bool value)
        {
            IsUIReady = value;
        }

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
        }

        private void Start()
        {
            Bootstrap();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Bootstrap()
        {
            if (_bootstrapped)
            {
                return;
            }

            _bootstrapped = true;
            IsUIReady = false;

            ValidateDocuments();

            if (StoryDialogueQueue.Instance != null)
            {
                StoryDialogueQueue.Instance.SubscribeEvents();
            }

            if (StoryDialogueQueue.Instance != null)
            {
                StoryDialogueQueue.Instance.RestorePendingFromService();
            }

            if (UITextService.Instance != null)
            {
                UITextService.Instance.Initialize();
            }

            if (SceneObjectStateLoader.Instance != null)
            {
                SceneObjectStateLoader.Instance.Initialize();
            }

            RegisterEffectiveScaleFallback();

            if (PersistentHudController.Instance != null)
            {
                PersistentHudController.Instance.Initialize();
            }

            if (SceneObjectController.Instance != null)
            {
                SceneObjectController.Instance.RefreshAll();
            }

            if (LogFloatingWindowHost.Instance != null)
            {
                LogFloatingWindowHost.Instance.Bind();
            }

            IsUIReady = true;
            EventBus.Publish(new OnUIReadyEvent());

            if (StoryDialogueQueue.Instance != null)
            {
                StoryDialogueQueue.Instance.TryStartNext();
            }
        }

        private void ValidateDocuments()
        {
            if (_mainSceneDocument == null)
            {
                Debug.LogWarning("[UIBootstrapController] MainSceneDocument is not assigned.");
            }

            if (_overlayPanelDocument == null)
            {
                Debug.LogWarning("[UIBootstrapController] OverlayPanelDocument is not assigned.");
            }
        }

        private void RegisterEffectiveScaleFallback()
        {
            // P-01 接入策略：避免 cyclic asmdef ref（TheGuild.UI.Platform.Win32 已 ref TheGuild.UI），
            // 改由 P-01 DesktopWindowService.HandleOnUIReady 主動 push（呼叫 OnEffectiveScaleChanged 並
            // 透過 RegisterEffectiveScaleListener 註冊長期 callback）。本方法保留 fallback log，給 P-01
            // 不可用 / OnUIReady 早於 P-01 載入的極端情境兜底。
            Debug.Log("[UIBootstrapController] effectiveScale fallback 1.0（待 P-01 主動 push）");
            OnEffectiveScaleChanged(1.0f);
        }

        /// <summary>
        /// effectiveScale 變更回呼。public 給 P-01 DesktopWindowService 主動 push 用，
        /// 也給 fallback 路徑於 P-01 不可用時直接呼叫。
        /// </summary>
        public void OnEffectiveScaleChanged(float newScale)
        {
            // 推送至 PanelSettings.scale（P-01 §3.5 / §3.8.1）
            if (_mainSceneDocument != null && _mainSceneDocument.panelSettings != null)
            {
                _mainSceneDocument.panelSettings.scale = newScale;
            }

            if (_overlayPanelDocument != null && _overlayPanelDocument.panelSettings != null)
            {
                _overlayPanelDocument.panelSettings.scale = newScale;
            }

            // 通知下游元件
            if (PersistentHudController.Instance != null)
            {
                PersistentHudController.Instance.OnEffectiveScaleChanged(newScale);
            }

            if (SceneNavigationController.Instance != null)
            {
                SceneNavigationController.Instance.OnEffectiveScaleChanged(newScale);
            }
        }
    }
}
