# Coding Standards — Unity C#

## Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Public field/property | PascalCase | `public int MaxHealth` |
| Private field | _camelCase | `private int _currentHealth` |
| Local variable | camelCase | `var damageAmount = 10f;` |
| Method | PascalCase | `public void TakeDamage()` |
| Interface | IPascalCase | `public interface IQuestSystem` |
| Constant | PascalCase | `public const int MaxLevel = 50;` |
| Enum | PascalCase | `public enum QuestStatus { Pending, Active }` |
| ScriptableObject | PascalCase + SO suffix | `QuestConfigSO` |

## Unity-Specific Standards

### Inspector Fields
```csharp
[Header("Quest Settings")]
[SerializeField] private QuestConfigSO _questConfig;

[Tooltip("Time in seconds before quest expires")]
[SerializeField] private float _expirationTime = 300f;
```

- Use `[SerializeField] private` instead of `public` for inspector-exposed fields
- Use `[Header]` and `[Tooltip]` for inspector organization
- Use `readonly` and `const` where applicable

### MonoBehaviour Lifecycle
- Cache component references in `Awake()`, never in `Update()`
- Subscribe to events in `OnEnable()`, unsubscribe in `OnDisable()`
- Avoid `Update()` where possible — use events, coroutines, or timers
- Never use `Find()`, `FindObjectOfType()`, or `SendMessage()` in production

### ScriptableObject Architecture
- Use ScriptableObjects for all data-driven content: quest templates, adventurer stats, economy configs
- Separate data from behavior — ScriptableObjects hold data, MonoBehaviours read it
- Use ScriptableObject-based events for decoupled cross-system communication

### Memory and Performance
- No allocations in hot paths (`Update`, physics callbacks)
- Use `StringBuilder` instead of string concatenation in loops
- Pool frequently instantiated objects with `ObjectPool<T>`
- Use `== null` (not `is null`) for Unity object null checks

## Architecture Rules

- Public APIs must have XML doc comments
- Cyclomatic complexity under 10 per method
- No method exceeds 40 lines (excluding data declarations)
- Dependencies are injected, not statically resolved
- Configuration values from ScriptableObjects or JSON, never hardcoded
- Systems expose interfaces (not concrete class dependencies)
- Events/signals for cross-system communication
- Frame-rate independent logic (`Time.deltaTime` everywhere)

## 語言規範

- 程式碼註釋（`//` 和 `/* */`）必須以**繁體中文**撰寫
- XML 文件註釋（`/// <summary>` 等）必須以**繁體中文**撰寫
- 識別符號（變數名、函式名、類別名、命名空間）維持英文
- Unity 內建 API 名稱不翻譯

## Test Directory Structure

測試程式碼與測試用資產統一放於 `Assets/Tests/` 下，**不**置於 `Assets/Scripts/Tests/`。

| 類型 | 路徑 | 說明 |
|------|------|------|
| EditMode 測試 | `Assets/Tests/EditMode/{SystemGroup}/{SystemName}/` | 不需 Unity runtime，執行快；預設首選 |
| PlayMode 測試 | `Assets/Tests/PlayMode/{SystemGroup}/{SystemName}/` | 需 MonoBehaviour 生命週期 / `Time.deltaTime` 累積 |
| 測試用資產 | `Assets/Tests/{EditMode,PlayMode}/{SystemGroup}/{SystemName}/TestResources/` | 測試用 CSV、ScriptableObject 等 |
| Assembly Definition | 與測試檔同目錄，命名 `Tests.{EditMode,PlayMode}.{SystemGroup}.{SystemName}.asmdef` | 引用 `UnityEngine.TestRunner`、`UnityEditor.TestRunner`、`nunit.framework` 與對應生產組件 |

`{SystemGroup}` 對應生產程式碼的第一層命名空間（如 `Core`、`Gameplay`）；`{SystemName}` 對應系統資料夾名（如 `Data`、`Time`、`Resources`）。

範例：F-01 DataManager 的 EditMode 測試放於 `Assets/Tests/EditMode/Core/Data/`，測試用 CSV 放於 `Assets/Tests/EditMode/Core/Data/TestResources/`。

## Design Document Compliance

- All gameplay values from config files with sensible defaults
- State machines with explicit transition tables
- No direct UI references from gameplay code
- Document which GDD each feature implements
