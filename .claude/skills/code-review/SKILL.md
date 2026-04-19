---
name: code-review
description: "Reviews code or Codex diff patches for architecture, quality, Unity C# standards, and SOLID compliance. Supports the review-feedback-resubmit loop with Codex."
argument-hint: "[path-to-file-or-directory] or 'codex-patch' to review Codex output"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash
---

When this skill is invoked:

## Mode 1: Review Codex Diff Patch

When reviewing Codex output (the most common mode in this project):

1. **Read the diff patch** provided by Claude Code from Codex MCP output.

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

**If CHANGES REQUIRED**: Claude Code sends the Required Changes list back to Codex for revision.
**If APPROVED**: Claude Code proceeds to rewrite the patch to production quality and write files.

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

Recommendation: Claude Code should intervene directly rather than sending back to Codex.
```
