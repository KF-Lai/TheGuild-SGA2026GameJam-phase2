# Project Directory Structure

```
TheGuild-SGA2026GameJam-phase2/
├── CLAUDE.md                          # Master project config
├── README.md                          # Game concept and scope
├── .claude/
│   ├── settings.json                  # Permissions + hooks
│   ├── agents/                        # 8 agent definitions
│   ├── skills/                        # 7 slash command definitions
│   ├── hooks/                         # 4 hook scripts
│   ├── rules/                         # 4 path-specific rule files
│   └── docs/                          # Standards and reference docs
├── design/
│   └── gdd/                           # Game design documents (GDDs)
├── production/
│   ├── sprints/                       # Sprint plans
│   ├── milestones/                    # Milestone definitions
│   ├── session-state/                 # Active session state (auto-managed)
│   └── session-logs/                  # Session and agent audit logs
└── TheGuild-unity/                    # Unity project root
    ├── Assets/
    │   ├── Scripts/
    │   │   ├── Core/                  # Core systems: event bus, save, time
    │   │   ├── Gameplay/              # Quest, adventurer, economy, guild
    │   │   ├── UI/                    # UI Toolkit screens and components
    │   │   └── Data/                  # ScriptableObject definitions
    │   ├── Resources/
    │   │   └── Data/                  # JSON config files (runtime)
    │   ├── Scenes/                    # Unity scenes
    │   ├── Prefabs/                   # Prefab assets
    │   ├── Art/                       # Sprites, UI art
    │   └── Audio/                     # Sound effects, music
    ├── Packages/
    └── ProjectSettings/
```

## Key Conventions

| Directory | Purpose | Who writes here |
|-----------|---------|----------------|
| `design/gdd/` | Game design specs | game-designer, systems-designer, economy-designer |
| `Scripts/Core/` | Shared infrastructure | unity-specialist |
| `Scripts/Gameplay/` | Game systems | gameplay-programmer (via Codex) |
| `Scripts/UI/` | UI implementation | unity-ui-specialist (via Codex) |
| `Scripts/Data/` | ScriptableObject defs | gameplay-programmer |
| `Resources/Data/` | JSON configs | systems-designer, economy-designer |
| `production/` | Sprint plans, logs | Claude Code (producer role) |
