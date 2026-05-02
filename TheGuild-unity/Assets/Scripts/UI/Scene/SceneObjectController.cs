using System;
using System.Collections.Generic;
using TheGuild.Core.Events;
using TheGuild.Gameplay.FactionStory;
using TheGuild.UI.Core;
using UnityEngine;
using OnFactionStoryStageUnlockedEvent = TheGuild.Gameplay.FactionStory.Events.OnFactionStoryStageUnlockedEvent;

namespace TheGuild.UI.Scene
{
    [DefaultExecutionOrder(-90)]
    public sealed class SceneObjectController : MonoBehaviour
    {
        private const int DEFAULT_OPHELIA_FACTION_ID = 1;
        private const string OPHELIA_MISSING_EVENT = "ophelia_missing";

        [SerializeField] private List<SceneObjectBinding> _bindings = new List<SceneObjectBinding>();

        private readonly Dictionary<string, SceneObjectBinding> _bindingByObjectID =
            new Dictionary<string, SceneObjectBinding>(StringComparer.Ordinal);
        private readonly HashSet<string> _activeEvents = new HashSet<string>(StringComparer.Ordinal);
        private Sprite _placeholderSprite;
        private bool _subscribed;

        public static SceneObjectController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            RebuildBindingMap();
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public SceneObjectState ResolveSceneObjectState(string objectID)
        {
            if (string.IsNullOrEmpty(objectID) || SceneObjectStateLoader.Instance == null)
            {
                return SceneObjectState.Default(objectID);
            }

            IReadOnlyList<SceneObjectStateTable> rows = SceneObjectStateLoader.Instance.GetRows(objectID);
            for (int i = 0; i < rows.Count; i++)
            {
                SceneObjectStateTable row = rows[i];
                if (EvaluateStageCondition(row))
                {
                    return new SceneObjectState(row.objectID, row.spriteVariant, row.dialogueKey, row.priority, row.audioCue);
                }
            }

            return SceneObjectState.Default(objectID);
        }

        public void RefreshAll()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                RefreshBinding(_bindings[i]);
            }
        }

        public bool HandleObjectClicked(string objectID)
        {
            SceneObjectState state = ResolveSceneObjectState(objectID);
            if (string.IsNullOrEmpty(state.DialogueKey) || PanelManager.Instance == null)
            {
                return false;
            }

            StoryDialogueOpenArgs args = new StoryDialogueOpenArgs(0, state.DialogueKey, string.Empty, objectID);
            return PanelManager.Instance.OpenPanel(PanelID.StoryDialogue, args);
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            EventBus.Subscribe<OnOpheliaMissingNightEvent>(HandleOpheliaMissingNight);
            EventBus.Subscribe<OnOpheliaReturnedEvent>(HandleOpheliaReturned);
            EventBus.Subscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            _subscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed)
            {
                return;
            }

            EventBus.Unsubscribe<OnOpheliaMissingNightEvent>(HandleOpheliaMissingNight);
            EventBus.Unsubscribe<OnOpheliaReturnedEvent>(HandleOpheliaReturned);
            EventBus.Unsubscribe<OnFactionStoryStageUnlockedEvent>(HandleStageUnlocked);
            _subscribed = false;
        }

        private void HandleOpheliaMissingNight(OnOpheliaMissingNightEvent evt)
        {
            _activeEvents.Add(OPHELIA_MISSING_EVENT);
            RefreshAll();
        }

        private void HandleOpheliaReturned(OnOpheliaReturnedEvent evt)
        {
            _activeEvents.Remove(OPHELIA_MISSING_EVENT);
            RefreshAll();
        }

        private void HandleStageUnlocked(OnFactionStoryStageUnlockedEvent evt)
        {
            RefreshAll();
        }

        private void RefreshBinding(SceneObjectBinding binding)
        {
            if (binding == null || binding.SpriteRenderer == null || string.IsNullOrEmpty(binding.ObjectID))
            {
                return;
            }

            SceneObjectState state = ResolveSceneObjectState(binding.ObjectID);
            binding.SpriteRenderer.sprite = LoadSpriteOrPlaceholder(binding.ObjectID, state.SpriteVariant);
        }

        private Sprite LoadSpriteOrPlaceholder(string objectID, string spriteVariant)
        {
            string variant = string.IsNullOrEmpty(spriteVariant) ? "default" : spriteVariant;
            Sprite sprite = Resources.Load<Sprite>($"Art/Scene/Ophelia/{objectID}_{variant}");
            if (sprite != null)
            {
                return sprite;
            }

            Debug.LogError($"[SceneObjectController] Sprite load failed: {objectID}_{variant}");
            return GetPlaceholderSprite();
        }

        private Sprite GetPlaceholderSprite()
        {
            if (_placeholderSprite != null)
            {
                return _placeholderSprite;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.clear);
            texture.Apply();
            _placeholderSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            return _placeholderSprite;
        }

        private bool EvaluateStageCondition(SceneObjectStateTable row)
        {
            if (row == null || string.IsNullOrEmpty(row.stageCondition))
            {
                return false;
            }

            return EvaluateOrExpression(row.stageCondition, row.factionID <= 0 ? DEFAULT_OPHELIA_FACTION_ID : row.factionID);
        }

        private bool EvaluateOrExpression(string expression, int factionID)
        {
            string[] parts = expression.Split(new[] { "OR" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (EvaluateAndExpression(parts[i], factionID))
                {
                    return true;
                }
            }

            return false;
        }

        private bool EvaluateAndExpression(string expression, int factionID)
        {
            string[] parts = expression.Split(new[] { "AND" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!EvaluateAtom(parts[i].Trim(), factionID))
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateAtom(string atom, int factionID)
        {
            if (atom.StartsWith("event:", StringComparison.Ordinal))
            {
                return _activeEvents.Contains(atom.Substring("event:".Length).Trim());
            }

            int stage = FactionStoryService.Instance == null
                ? -1
                : FactionStoryService.Instance.GetUnlockedStageIndex(factionID);

            if (TryCompare(atom, "stageID >= ", stage, (a, b) => a >= b)) return true;
            if (TryCompare(atom, "stage >= ", stage, (a, b) => a >= b)) return true;
            if (TryCompare(atom, "stageID == ", stage, (a, b) => a == b)) return true;
            if (TryCompare(atom, "stage == ", stage, (a, b) => a == b)) return true;
            if (TryCompare(atom, "stageID <= ", stage, (a, b) => a <= b)) return true;
            if (TryCompare(atom, "stage <= ", stage, (a, b) => a <= b)) return true;
            if (TryCompare(atom, "stageID > ", stage, (a, b) => a > b)) return true;
            if (TryCompare(atom, "stage > ", stage, (a, b) => a > b)) return true;
            if (TryCompare(atom, "stageID < ", stage, (a, b) => a < b)) return true;
            if (TryCompare(atom, "stage < ", stage, (a, b) => a < b)) return true;

            Debug.LogError($"[SceneObjectController] Invalid stageCondition atom: {atom}");
            return false;
        }

        private bool TryCompare(string atom, string prefix, int currentStage, Func<int, int, bool> compare)
        {
            if (!atom.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (int.TryParse(atom.Substring(prefix.Length).Trim(), out int value))
            {
                return compare(currentStage, value);
            }

            Debug.LogError($"[SceneObjectController] Invalid stageCondition number: {atom}");
            return false;
        }

        private void RebuildBindingMap()
        {
            _bindingByObjectID.Clear();
            for (int i = 0; i < _bindings.Count; i++)
            {
                SceneObjectBinding binding = _bindings[i];
                if (binding != null && !string.IsNullOrEmpty(binding.ObjectID))
                {
                    _bindingByObjectID[binding.ObjectID] = binding;
                }
            }
        }

        [Serializable]
        private sealed class SceneObjectBinding
        {
            public string ObjectID;
            public SpriteRenderer SpriteRenderer;
        }
    }
}
