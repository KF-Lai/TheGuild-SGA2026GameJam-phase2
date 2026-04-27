---
name: sprint-plan
description: "Create or update sprint plans and sprint status reports from milestones, design docs, work items, bugs, and prior sprint artifacts. Use for planning The Guild work across design, implementation, review, and verification."
---

## Skill Contract

Use this skill when the user invokes $sprint-plan or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $sprint-plan <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:

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
- **Design**: the design workflow produce GDD
- **Codex**: Work item sent to Codex for implementation
- **Review**: Code review of Codex output
- **Direct**: the current agent implements directly (small fixes, configs)

## Carryover from Previous Sprint
| Work Item | Reason | New Estimate |
|-----------|--------|-------------|

## Risks
| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|

## Definition of Done
- [ ] All Must Have items completed and reviewed
- [ ] All Codex output passed $code-review
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



