---
paths:
  - "TheGuild-unity/Assets/Scripts/UI/**"
---

# UI Code Rules

- UI must NEVER own or directly modify game state — display only, use commands/events to request changes
- All UI text must go through the localization system — no hardcoded user-facing strings
- Support both keyboard/mouse AND gamepad input for all interactive elements
- All animations must be skippable and respect user accessibility preferences
- UI sounds trigger through the audio event system, not directly
- UI must never block the game thread
- Use UI Toolkit (UXML/USS) for screen-space UI; UGUI only for world-space elements
- Cache VisualElement references — never query the visual tree every frame
- Register events in `OnEnable`, unregister in `OnDisable`
