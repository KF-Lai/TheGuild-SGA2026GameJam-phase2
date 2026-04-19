---
paths:
  - "TheGuild-unity/Assets/Scripts/Gameplay/**"
---

# Gameplay Code Rules

- ALL gameplay values MUST come from external config/data files (ScriptableObject or JSON), NEVER hardcoded
- Use `Time.deltaTime` for ALL time-dependent calculations (frame-rate independence)
- NO direct references to UI code — use events/UnityEvents for cross-system communication
- Every gameplay system must implement a clear interface (e.g., `IQuestSystem`, `IAdventurerManager`)
- State machines must have explicit transition tables with documented states
- Write unit tests for all gameplay logic — separate logic from MonoBehaviour
- Document which design doc each feature implements in code comments
- No static singletons for game state — use dependency injection or ScriptableObject-based service locators
- Use `[SerializeField] private` instead of `public` for inspector fields

## Examples

**Correct** (data-driven):

```csharp
[SerializeField] private QuestConfig _questConfig;

float successRate = _questConfig.BaseSuccessRate * adventurer.SkillModifier;
float elapsed = Time.deltaTime * _questConfig.TimeScale;
```

**Incorrect** (hardcoded):

```csharp
public float successRate = 0.75f;  // VIOLATION: hardcoded gameplay value
float elapsed = 1.0f;              // VIOLATION: not using deltaTime, not from config
```
