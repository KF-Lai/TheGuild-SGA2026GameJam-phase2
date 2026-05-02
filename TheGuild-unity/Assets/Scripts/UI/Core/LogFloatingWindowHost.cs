using TheGuild.UI.Scene;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Core
{
    [DefaultExecutionOrder(-80)]
    public sealed class LogFloatingWindowHost : MonoBehaviour
    {
        private const string DEFAULT_UXML_PATH = "UI/UXML/LogFloatingWindow";

        [SerializeField] private VisualTreeAsset _windowTemplate;

        private VisualElement _windowRoot;
        private VisualElement _pendingBindContainer;
        private bool _bound;
        private bool _logHostFailed;

        public static LogFloatingWindowHost Instance { get; private set; }
        public VisualElement PendingBindContainer => _pendingBindContainer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public BindResult Bind()
        {
            if (_bound)
            {
                Debug.LogWarning("[LogFloatingWindowHost] Log window already bound.");
                return BindResult.AlreadyBound;
            }

            try
            {
                VisualElement host = PersistentHudController.Instance == null
                    ? null
                    : PersistentHudController.Instance.GetLogHostContainer();
                if (host == null)
                {
                    Debug.LogWarning("[LogFloatingWindowHost] Invalid log host container.");
                    return BindResult.InvalidContainer;
                }

                VisualTreeAsset template = _windowTemplate != null
                    ? _windowTemplate
                    : Resources.Load<VisualTreeAsset>(DEFAULT_UXML_PATH);
                if (template == null)
                {
                    Debug.LogWarning("[LogFloatingWindowHost] LogFloatingWindow UXML unavailable.");
                    return BindResult.Unavailable;
                }

                _windowRoot = template.Instantiate();
                _pendingBindContainer = _windowRoot.Q<VisualElement>("log-body");
                host.Add(_windowRoot);
                _bound = true;

                // TODO P-03：NotificationService 補齊後，由此將 _pendingBindContainer 交給 BindLogWindow。
                Debug.Log("[LogFloatingWindowHost] P-03 unavailable, log container prepared.");
                return BindResult.Unavailable;
            }
            catch (System.Exception ex)
            {
                _logHostFailed = true;
                Debug.LogError($"[LogFloatingWindowHost] Bind failed: {ex.GetType().Name} - {ex.Message}");
                return BindResult.Unavailable;
            }
        }
    }
}
