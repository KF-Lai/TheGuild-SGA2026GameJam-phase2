using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Core
{
    [DefaultExecutionOrder(-100)]
    public sealed class PanelManager : MonoBehaviour
    {
        private readonly Dictionary<PanelID, VisualElement> _panelRoots = new Dictionary<PanelID, VisualElement>();
        private readonly Dictionary<PanelID, PanelStateMachine> _machines = new Dictionary<PanelID, PanelStateMachine>();
        private readonly Dictionary<PanelID, IPanel> _panels = new Dictionary<PanelID, IPanel>();
        private readonly Dictionary<PanelID, ConfirmArgs> _confirmArgs = new Dictionary<PanelID, ConfirmArgs>();
        private readonly List<PanelID> _panelStack = new List<PanelID>(2);

        [SerializeField] private P02UITuning _tuning;

        public static PanelManager Instance { get; private set; }

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

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            foreach (PanelStateMachine machine in _machines.Values)
            {
                machine.UpdateAnimation(delta);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RegisterPanelRoot(PanelID id, VisualElement root)
        {
            if (id == PanelID.None || id == PanelID.Invalid || root == null)
            {
                Debug.LogError($"[PanelManager] RegisterPanelRoot invalid. id={id}");
                return;
            }

            _panelRoots[id] = root;
            _machines[id] = new PanelStateMachine(root, GetTransitionSeconds());
        }

        public void RegisterPanel(IPanel panel)
        {
            if (panel == null || panel.Id == PanelID.None || panel.Id == PanelID.Invalid || panel.Root == null)
            {
                Debug.LogError("[PanelManager] RegisterPanel invalid.");
                return;
            }

            _panels[panel.Id] = panel;
            RegisterPanelRoot(panel.Id, panel.Root);
        }

        public bool OpenPanel(PanelID id, object args = null)
        {
            if (UIBootstrapController.Instance == null || !UIBootstrapController.Instance.IsUIReady)
            {
                Debug.LogWarning($"OpenPanel called before OnUIReady: {id}");
                return false;
            }

            if (!EnsureRegistered(id))
            {
                return false;
            }

            if (id == PanelID.ConfirmPopup)
            {
                if (args is ConfirmArgs confirmArgs)
                {
                    _confirmArgs[id] = confirmArgs;
                }
                PushOrMoveTop(id);
                _machines[id].Open();
                DispatchPanelOpen(id, args);
                return true;
            }

            if (id == PanelID.StoryDialogue)
            {
                CloseAllBasePanels();
                PushOrMoveTop(id);
                _machines[id].Open();
                DispatchPanelOpen(id, args);
                return true;
            }

            if (IsBasePanel(id))
            {
                CloseAllBasePanels();
                PushOrMoveTop(id);
                _machines[id].Open();
                DispatchPanelOpen(id, args);
                return true;
            }

            Debug.LogWarning($"[PanelManager] Unsupported panel id: {id}");
            return false;
        }

        public bool ClosePanel(PanelID id, Action onClosed = null)
        {
            if (!_machines.TryGetValue(id, out PanelStateMachine machine))
            {
                return false;
            }

            machine.Close(() =>
            {
                RemoveFromStack(id);
                DispatchPanelClose(id);
                onClosed?.Invoke();
            });
            if (id == PanelID.ConfirmPopup)
            {
                _confirmArgs.Remove(id);
            }

            return true;
        }

        public PanelID GetTopPanel()
        {
            return _panelStack.Count == 0 ? PanelID.None : _panelStack[_panelStack.Count - 1];
        }

        public bool ShowConfirm(ConfirmArgs args)
        {
            return OpenPanel(PanelID.ConfirmPopup, args);
        }

        public void OnEscape()
        {
            PanelID top = GetTopPanel();
            if (top == PanelID.None)
            {
                OpenPanel(PanelID.SettingsPanel);
                return;
            }

            if (top == PanelID.StoryDialogue)
            {
                return;
            }

            if (top == PanelID.ConfirmPopup)
            {
                InvokeConfirmCancel();
                return;
            }

            if (top == PanelID.SettingsPanel || IsBasePanel(top))
            {
                ClosePanel(top);
            }
        }

        public void OnModalOverlayClicked()
        {
            PanelID top = GetTopPanel();
            if (top == PanelID.StoryDialogue || top == PanelID.ConfirmPopup)
            {
                return;
            }

            OnEscape();
        }

        private void InvokeConfirmCancel()
        {
            if (!_machines.TryGetValue(PanelID.ConfirmPopup, out PanelStateMachine machine))
            {
                return;
            }

            _confirmArgs.TryGetValue(PanelID.ConfirmPopup, out ConfirmArgs args);
            if (machine.State == PanelState.Opening)
            {
                machine.ScheduleCancelAfterOpening(() =>
                {
                    args.OnCancel?.Invoke();
                    ClosePanel(PanelID.ConfirmPopup);
                });
                return;
            }

            args.OnCancel?.Invoke();
            ClosePanel(PanelID.ConfirmPopup);
        }

        private void CloseAllBasePanels()
        {
            for (int i = _panelStack.Count - 1; i >= 0; i--)
            {
                PanelID id = _panelStack[i];
                if (IsBasePanel(id))
                {
                    ClosePanel(id);
                }
            }
        }

        private void PushOrMoveTop(PanelID id)
        {
            RemoveFromStack(id);
            if (_panelStack.Count >= 2)
            {
                ClosePanel(_panelStack[0]);
            }
            _panelStack.Add(id);
        }

        private void RemoveFromStack(PanelID id)
        {
            for (int i = _panelStack.Count - 1; i >= 0; i--)
            {
                if (_panelStack[i] == id)
                {
                    _panelStack.RemoveAt(i);
                }
            }
        }

        private bool EnsureRegistered(PanelID id)
        {
            if (_machines.ContainsKey(id))
            {
                return true;
            }

            Debug.LogError($"[PanelManager] Panel root is not registered: {id}");
            return false;
        }

        private void DispatchPanelOpen(PanelID id, object args)
        {
            if (_panels.TryGetValue(id, out IPanel panel))
            {
                panel.Open(args);
            }
        }

        private void DispatchPanelClose(PanelID id)
        {
            if (_panels.TryGetValue(id, out IPanel panel))
            {
                panel.Close();
            }
        }

        private bool IsBasePanel(PanelID id)
        {
            return id == PanelID.CommissionBoard
                || id == PanelID.AdventurerRoster
                || id == PanelID.GuildBuilding
                || id == PanelID.StaffRoster
                || id == PanelID.StaffGacha
                || id == PanelID.GuildOverview
                || id == PanelID.SettingsPanel;
        }

        private float GetTransitionSeconds()
        {
            return _tuning == null ? 0.15f : _tuning.PanelTransitionSeconds;
        }
    }
}
