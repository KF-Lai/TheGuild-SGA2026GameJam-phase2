---
name: sprint-plan
description: "Generates a new sprint plan or status update. Pulls context from milestones, design docs, and previous sprints. Adapted for Claude Code + Codex workflow."
argument-hint: "[new|update|status]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit
---

When this skill is invoked:

1. **Read current milestone** from `production/milestones/`.
2. **Read previous sprint** (if any) from `production/sprints/`.
3. **Scan design documents** in `design/gdd/` for features ready for implementation.

For `new`:

4. **Generate a sprint plan**:

```markdown
# Sprint [N] — [Start Date] to [End Date]

## Sprint Goal
[One sentence: what this sprint achieves toward the milestone]

## Capacity
- Total days: [X]
- Buffer (20%): [Y days for unplanned work]
- Available: [Z days]

## Work Items

### Must Have (Critical Path)
| ID | Work Item | Type | Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|-----------|------|-------|-----------|-------------|-------------------|
| | | Design/Codex/Review | | | | |

### Should Have
| ID | Work Item | Type | Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|-----------|------|-------|-----------|-------------|-------------------|

### Nice to Have
| ID | Work Item | Type | Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|-----------|------|-------|-----------|-------------|-------------------|

## Work Item Types
- **Design**: Claude Code + design agents produce GDD
- **Codex**: Work item sent to Codex MCP for implementation
- **Review**: Code review of Codex output
- **Direct**: Claude Code implements directly (small fixes, configs)

## Carryover from Previous Sprint
| Work Item | Reason | New Estimate |
|-----------|--------|-------------|

## Risks
| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|

## Definition of Done
- [ ] All Must Have items completed and reviewed
- [ ] All Codex output passed /code-review
- [ ] Design documents updated for deviations
- [ ] No S1 or S2 bugs
```

For `status`:

4. **Generate status report**:

```markdown
# Sprint [N] Status — [Date]

## Progress: [X/Y items complete] ([Z%])

### Completed
| Item | Completed By | Notes |
|------|-------------|-------|

### In Progress
| Item | Owner | % Done | Blockers |
|------|-------|--------|----------|

### Not Started
| Item | Owner | At Risk? |
|------|-------|----------|

## Burndown Assessment
[On track / Behind / Ahead]
```
