using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Core
{
    /// <summary>
    /// 單一面板淡入淡出狀態機。
    /// </summary>
    public sealed class PanelStateMachine
    {
        private readonly VisualElement _panel;
        private readonly float _transitionSeconds;
        private float _elapsedSeconds;
        private float _fromAlpha;
        private float _toAlpha;
        private Action _onCompleted;
        private bool _cancelScheduled;

        public PanelStateMachine(VisualElement panel, float transitionSeconds)
        {
            _panel = panel;
            _transitionSeconds = Mathf.Max(0.01f, transitionSeconds);
            State = PanelState.Closed;
            ApplyClosed();
        }

        public PanelState State { get; private set; }

        public float RemainingSeconds
        {
            get { return Mathf.Max(0f, _transitionSeconds - _elapsedSeconds); }
        }

        public bool IsAnimating
        {
            get { return State == PanelState.Opening || State == PanelState.Closing; }
        }

        public void Open(Action onOpened = null)
        {
            if (_panel == null || State == PanelState.Open || State == PanelState.Opening)
            {
                return;
            }

            StartTransition(PanelState.Opening, 0f, 1f, onOpened);
            _panel.style.display = DisplayStyle.Flex;
        }

        public void Close(Action onClosed = null)
        {
            if (_panel == null || State == PanelState.Closed || State == PanelState.Closing)
            {
                return;
            }

            StartTransition(PanelState.Closing, CurrentOpacity(), 0f, onClosed);
        }

        public void UpdateAnimation(float deltaSeconds)
        {
            if (_panel == null || !IsAnimating)
            {
                return;
            }

            _elapsedSeconds += Mathf.Max(0f, deltaSeconds);
            float t = Mathf.Clamp01(_elapsedSeconds / _transitionSeconds);
            _panel.style.opacity = Mathf.Lerp(_fromAlpha, _toAlpha, t);

            if (t < 1f)
            {
                return;
            }

            CompleteTransition();
        }

        public void ScheduleCancelAfterOpening(Action onCancel)
        {
            if (_panel == null || onCancel == null)
            {
                return;
            }

            if (State == PanelState.Open)
            {
                onCancel.Invoke();
                return;
            }

            if (State != PanelState.Opening || _cancelScheduled)
            {
                return;
            }

            _cancelScheduled = true;
            long remainingMs = Mathf.CeilToInt(RemainingSeconds * 1000f);
            _panel.schedule.Execute(() =>
            {
                _cancelScheduled = false;
                onCancel.Invoke();
            }).ExecuteLater(remainingMs);
        }

        private void StartTransition(PanelState nextState, float fromAlpha, float toAlpha, Action onCompleted)
        {
            State = nextState;
            _elapsedSeconds = 0f;
            _fromAlpha = Mathf.Clamp01(fromAlpha);
            _toAlpha = Mathf.Clamp01(toAlpha);
            _onCompleted = onCompleted;
            _panel.pickingMode = PickingMode.Ignore;
            _panel.style.opacity = _fromAlpha;
        }

        private void CompleteTransition()
        {
            _panel.style.opacity = _toAlpha;

            if (State == PanelState.Opening)
            {
                State = PanelState.Open;
                _panel.pickingMode = PickingMode.Position;
            }
            else
            {
                ApplyClosed();
            }

            Action callback = _onCompleted;
            _onCompleted = null;
            callback?.Invoke();
        }

        private void ApplyClosed()
        {
            State = PanelState.Closed;
            if (_panel == null)
            {
                return;
            }

            _panel.style.opacity = 0f;
            _panel.style.display = DisplayStyle.None;
            _panel.pickingMode = PickingMode.Ignore;
        }

        private float CurrentOpacity()
        {
            float opacity = _panel.resolvedStyle.opacity;
            return Mathf.Clamp01(opacity);
        }
    }
}
