---
name: scope-check
description: "Analyze a feature or sprint for scope creep by comparing current scope against the original plan. Critical for Game Jam time constraints."
argument-hint: "[feature-name or sprint-N]"
user-invocable: true
allowed-tools: Read, Glob, Grep
---

When this skill is invoked:

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
