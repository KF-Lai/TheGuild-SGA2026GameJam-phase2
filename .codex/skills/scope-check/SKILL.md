---
name: scope-check
description: "Analyze a feature, sprint, or work item for scope creep against the original plan, game jam constraints, core pillars, dependencies, and delivery risk. Use when deciding what to keep, defer, or cut."
---

## Skill Contract

Use this skill when the user invokes $scope-check or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $scope-check <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:

1. **Read the original plan**:
   - Feature name → read design doc from `design/gdd/`
   - Sprint number → read sprint plan from `production/sprints/`
   - No argument → compare entire README.md scope against current state

2. **Read current state**:
   - Scan codebase for implemented files
   - Read git log for related commits
   - Check for TODO comments indicating scope additions

3. **Compare and output**:

```markdown
## Scope Check: [Feature/Sprint Name]
Generated: [Date]

### Original Scope
[Items from the original plan]

### Current Scope
[Items implemented or in progress]

### Scope Additions (not in original plan)
| Addition | Source | Justified? | Effort |
|----------|--------|-----------|--------|

### Scope Removals (in original but dropped)
| Removed Item | Reason | Impact |
|-------------|--------|--------|

### Bloat Score
- Original items: [N]
- Current items: [N]
- Net scope change: [+/-N] ([X]%)

### Game Jam Risk Assessment
- **Schedule Risk**: [Low/Medium/High]
- **Quality Risk**: [Low/Medium/High]
- **Submission Risk**: [Low/Medium/High] — can we submit on time?

### Recommendations
1. **Cut**: [Items to remove to stay on schedule]
2. **Defer**: [Items for post-Game Jam]
3. **Keep**: [Justified additions]

### Verdict
- **On Track**: Within 10% of original scope
- **Minor Creep**: 10-25% — manageable with adjustments
- **Significant Creep**: 25-50% — need to cut or extend
- **Out of Control**: >50% — stop and re-plan
```

### Game Jam Priority Rule
When recommending cuts, always preserve the **core loop** (quest → assign → wait → result) over any secondary feature. A working core loop with placeholder art ships; a beautiful half-finished game does not.



