---
name: design-system
description: "Guided, section-by-section GDD authoring for a single game system. Gathers context, walks through each required section collaboratively, cross-references dependencies, and writes incrementally."
argument-hint: "<system-name> (e.g., 'quest-system', 'adventurer-system', 'economy')"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit
---

When this skill is invoked:

## 1. Parse & Validate

System name argument is **required**. If missing:
> "Usage: `/design-system <system-name>` — e.g., `/design-system quest-system`"

Normalize to kebab-case for filename.

## 2. Gather Context

### Required Reads
- `design/gdd/game-concept.md` — fail if missing: "Run `/brainstorm` or create a game concept first."
- `design/gdd/systems-index.md` — warn if missing: "Consider running `/map-systems` first."

### Dependency Reads
From systems index, read GDDs of upstream and downstream dependencies. Extract interfaces, formulas, edge cases that constrain this system.

### Present Context Summary
> **Designing: [System Name]**
> - Depends on: [list]
> - Depended on by: [list]
> - Constraints from dependencies: [key points]

## 3. Create File Skeleton

Create `design/gdd/[system-name].md` with all 8 section headers + `[To be designed]` placeholders.

Update `production/session-state/active.md` with current task.

## 4. Section-by-Section Design

For **each** of the 8 required sections, follow:

```
Context → Questions → Options → Decision → Draft → Approval → Write to file
```

### Sections
1. **Overview**: One paragraph summary
2. **Player Fantasy**: Target emotion (MDA aesthetics)
3. **Detailed Rules**: Unambiguous mechanics (delegate to `game-designer` for complex design)
4. **Formulas**: All math with variables (delegate to `systems-designer` for complex formulas)
5. **Edge Cases**: Unusual situations
6. **Dependencies**: System connections with interfaces
7. **Tuning Knobs**: Configurable values with safe ranges (for ScriptableObject/JSON)
8. **Acceptance Criteria**: Testable conditions (translatable to Codex work item criteria)

After writing each section, update session state.

## 5. Post-Design

After all sections complete:

1. **Self-check**: Read back from file, verify completeness
2. **Offer `/design-review`**: Validate the GDD
3. **Update systems index**: Mark system as "Designed" or "Approved"
4. **Suggest next**: "Design next system? Create Codex work items? `/sprint-plan`?"

## Key Principle

**Never** auto-generate the full GDD. Walk through section by section with user input.
**Always** write each section to file immediately after approval.
**Always** cross-reference dependency GDDs to prevent contradictions.
