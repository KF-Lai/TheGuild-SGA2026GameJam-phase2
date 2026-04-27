---
name: code-review
description: "Review code, diffs, or Codex output for Unity C# quality, architecture, SOLID compliance, project standards, implementation risk, and missing tests. Use when asked to review existing files, patches, or implementation results."
---

## Skill Contract

Use this skill when the user invokes $code-review or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $code-review <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:


## Mode 1: Review Codex Diff Patch

When reviewing Codex output (the most common mode in this project):

1. **Read the diff patch** provided in the current task context or Codex output.

2. **Read the corresponding work item** to understand requirements and acceptance criteria.

3. **Read the CLAUDE.md** and relevant rules for project standards.

4. **Evaluate against coding standards**:
   - [ ] `[SerializeField] private` instead of `public` for inspector fields
   - [ ] No hardcoded gameplay values (all from ScriptableObject/JSON config)
   - [ ] Cyclomatic complexity under 10 per method
   - [ ] No method exceeds 40 lines
   - [ ] Dependencies injected (no static singletons for game state)
   - [ ] Interfaces defined for system contracts

5. **Check Unity-specific quality**:
   - [ ] Component references cached in `Awake()`
   - [ ] No `Find()`, `FindObjectOfType()`, `SendMessage()` in production
   - [ ] No allocations in `Update()` or hot paths
   - [ ] `Time.deltaTime` for time-dependent calculations
   - [ ] Events subscribed in `OnEnable()`, unsubscribed in `OnDisable()`
   - [ ] Proper null checks using `== null` (not `is null`) for Unity objects

6. **Check architectural compliance**:
   - [ ] UI code does not own or modify game state
   - [ ] Events/signals for cross-system communication
   - [ ] Correct dependency direction
   - [ ] No circular dependencies
   - [ ] Consistent with existing codebase patterns

7. **Output the review**:

```
## Code Review: [File/System Name]

### Source: Codex Diff Patch | Work Item: [reference]

### Standards Compliance: [X/6 passing]
[List failures with specific line references in the diff]

### Unity Quality: [X/6 passing]
[List Unity-specific issues]

### Architecture: [CLEAN / MINOR ISSUES / VIOLATIONS FOUND]
[List specific concerns]

### Positive Observations
[What is done well]

### Required Changes
[Must-fix items — these go back to Codex]

### Suggestions
[Nice-to-have improvements]

### Verdict: [APPROVED / CHANGES REQUIRED]
```

**If CHANGES REQUIRED**: send the Required Changes list back to Codex for revision.
**If APPROVED**: proceed with final code integration and file updates.

## Mode 2: Review Existing File

When reviewing already-written code files:

1. **Read the target file(s)** in full.
2. **Follow steps 3-7 above** but evaluate the actual file content instead of a diff patch.

## Escalation Rule

If this review is the **3rd iteration** (Codex has been asked to fix twice already), include an additional section:

```
### Escalation Notice
This is review iteration 3+. Recurring issues:
[List issues that keep appearing]

Recommendation: stop the review loop and fix the issues directly in Codex.
```



