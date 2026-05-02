using System;
using System.Collections.Generic;
using TheGuild.Gameplay.Building;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheGuild.UI.Scene
{
    [DefaultExecutionOrder(-70)]
    public sealed class SceneNavigationController : MonoBehaviour
    {
        private const int STAFF_LOUNGE_BUILDING_ID = 6;
        private const int STAFF_LOUNGE_UNLOCK_LEVEL = 1;

        [SerializeField] private Camera _camera;
        [SerializeField] private UIDocument _sceneDocument;
        [SerializeField] private P02UITuning _tuning;
        [SerializeField] private List<NavigationBinding> _bindings = new List<NavigationBinding>();

        private readonly Dictionary<Collider2D, NavigationBinding> _bindingByCollider =
            new Dictionary<Collider2D, NavigationBinding>();
        private VisualElement _outline;
        private Label _tooltip;
        private NavigationBinding _hovered;
        private float _effectiveScale = 1f;

        public static SceneNavigationController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            RebuildBindingMap();
            EnsureVisuals();
        }

        private void Update()
        {
            if (_camera == null)
            {
                return;
            }

            NavigationBinding binding = PickNavigationBinding();
            if (binding != _hovered)
            {
                _hovered = binding;
                UpdateHoverVisual(binding);
            }

            if (binding != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                HandleObjectClicked(binding.ObjectID);
            }
        }

        private void OnDisable()
        {
            HideHoverVisual();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OnEffectiveScaleChanged(float effectiveScale)
        {
            _effectiveScale = effectiveScale > 0f ? effectiveScale : 1f;
            UpdateHoverVisual(_hovered);
        }

        public bool HandleObjectClicked(string objectID)
        {
            if (!NavObjectMap.TryGetPanelID(objectID, out PanelID panelID))
            {
                return false;
            }

            if (string.Equals(objectID, "nav_staff_lounge", StringComparison.Ordinal)
                && !IsStaffLoungeUnlocked())
            {
                ShowTooltip(objectID);
                return false;
            }

            return PanelManager.Instance != null && PanelManager.Instance.OpenPanel(panelID);
        }

        private NavigationBinding PickNavigationBinding()
        {
            if (Mouse.current == null)
            {
                return null;
            }

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(mousePos);
            Collider2D hit = Physics2D.OverlapPoint(world);
            if (hit != null && _bindingByCollider.TryGetValue(hit, out NavigationBinding binding))
            {
                return binding;
            }

            return null;
        }

        private void UpdateHoverVisual(NavigationBinding binding)
        {
            if (binding == null || binding.SpriteRenderer == null)
            {
                HideHoverVisual();
                return;
            }

            EnsureVisuals();
            Rect rect = ScreenAnchorCalculator.WorldBoundsToPanelRect(
                binding.SpriteRenderer.bounds,
                _camera,
                new Vector2(Screen.width, Screen.height),
                _effectiveScale);

            _outline.style.display = DisplayStyle.Flex;
            _outline.style.left = rect.xMin;
            _outline.style.top = rect.yMin;
            _outline.style.width = rect.width;
            _outline.style.height = rect.height;
            _outline.style.borderTopWidth = GetHoverThickness();
            _outline.style.borderRightWidth = GetHoverThickness();
            _outline.style.borderBottomWidth = GetHoverThickness();
            _outline.style.borderLeftWidth = GetHoverThickness();
            _outline.style.borderTopColor = GetHoverColor(binding.ObjectID);
            _outline.style.borderRightColor = GetHoverColor(binding.ObjectID);
            _outline.style.borderBottomColor = GetHoverColor(binding.ObjectID);
            _outline.style.borderLeftColor = GetHoverColor(binding.ObjectID);

            if (string.Equals(binding.ObjectID, "nav_staff_lounge", StringComparison.Ordinal)
                && !IsStaffLoungeUnlocked())
            {
                ShowTooltip(binding.ObjectID);
            }
            else
            {
                _tooltip.style.display = DisplayStyle.None;
            }
        }

        private void HideHoverVisual()
        {
            if (_outline != null)
            {
                _outline.style.display = DisplayStyle.None;
            }

            if (_tooltip != null)
            {
                _tooltip.style.display = DisplayStyle.None;
            }
        }

        private void ShowTooltip(string objectID)
        {
            EnsureVisuals();
            _tooltip.text = UITextService.Instance == null
                ? objectID
                : UITextService.Instance.Lookup("scene.nav.staff_lounge.locked", objectID);
            _tooltip.style.display = DisplayStyle.Flex;
        }

        private bool IsStaffLoungeUnlocked()
        {
            return BuildingService.Instance != null
                && BuildingService.Instance.GetBuildingLevel(STAFF_LOUNGE_BUILDING_ID) >= STAFF_LOUNGE_UNLOCK_LEVEL;
        }

        private Color GetHoverColor(string objectID)
        {
            float alpha = _tuning == null ? 0.6f : _tuning.HoverOutlineAlpha;
            bool lockedStaff = string.Equals(objectID, "nav_staff_lounge", StringComparison.Ordinal)
                && !IsStaffLoungeUnlocked();
            return lockedStaff ? new Color(0.5f, 0.5f, 0.5f, alpha) : new Color(1f, 1f, 1f, alpha);
        }

        private float GetHoverThickness()
        {
            return _tuning == null ? 2f : _tuning.HoverOutlineThickness;
        }

        private void EnsureVisuals()
        {
            if (_sceneDocument == null || _sceneDocument.rootVisualElement == null || _outline != null)
            {
                return;
            }

            VisualElement root = _sceneDocument.rootVisualElement;
            _outline = new VisualElement { name = "scene-hover-outline" };
            _outline.pickingMode = PickingMode.Ignore;
            _outline.style.position = Position.Absolute;
            _outline.style.display = DisplayStyle.None;
            root.Add(_outline);

            _tooltip = new Label { name = "scene-nav-tooltip" };
            _tooltip.pickingMode = PickingMode.Ignore;
            _tooltip.style.position = Position.Absolute;
            _tooltip.style.display = DisplayStyle.None;
            _tooltip.style.left = 12;
            _tooltip.style.top = 12;
            root.Add(_tooltip);
        }

        private void RebuildBindingMap()
        {
            _bindingByCollider.Clear();
            for (int i = 0; i < _bindings.Count; i++)
            {
                NavigationBinding binding = _bindings[i];
                if (binding != null && binding.Collider != null)
                {
                    _bindingByCollider[binding.Collider] = binding;
                }
            }
        }

        [Serializable]
        private sealed class NavigationBinding
        {
            public string ObjectID;
            public SpriteRenderer SpriteRenderer;
            public Collider2D Collider;
        }
    }
}
