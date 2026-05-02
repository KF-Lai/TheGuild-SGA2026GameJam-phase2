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
            // TODO P-01：正式接入 RegisterEffectiveScaleListener(callback)。
            Debug.Log("[UIBootstrapController] P-01 unavailable, default scale 1.0");
            if (PersistentHudController.Instance != null)
            {
                PersistentHudController.Instance.OnEffectiveScaleChanged(1.0f);
            }
        }
    }
}
