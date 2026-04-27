---
name: design-system
description: "Guide section-by-section GDD authoring for a single game system, gathering context, dependencies, mechanics, formulas, edge cases, UI feedback, acceptance criteria, and implementation notes. Use for new or revised system design."
---

## Skill Contract

Use this skill when the user invokes $design-system or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $design-system <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:


## 1. Parse & Validate

System name argument is **required**. If missing:
> "Usage: `$design-system <system-name>` — e.g., `$design-system quest-system`"

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
3. **Detailed Rules**: Unambiguous mechanics; if the design is still fuzzy, run an extra focused rules pass before writing
4. **Formulas**: All math with variables; if the math is still unstable, resolve it before writing
5. **Edge Cases**: Unusual situations
6. **Dependencies**: System connections with interfaces
7. **Tuning Knobs**: Configurable values with safe ranges (for ScriptableObject/JSON)
8. **Acceptance Criteria**: Testable conditions (translatable to downstream implementation work item criteria)

After writing each section, update session state.

## 5. Post-Design

After all sections complete:

1. **Self-check**: Read back from file, verify completeness
2. **Offer `$design-review`**: Validate the GDD
3. **Update systems index**: Mark system as "Designed" or "Approved"
4. **Suggest next**: "Design next system? Create implementation work items? `/sprint-plan`?"

## Key Principle

**Never** auto-generate the full GDD. Walk through section by section with user input.
**Always** write each section to file immediately after approval.
**Always** cross-reference dependency GDDs to prevent contradictions.



