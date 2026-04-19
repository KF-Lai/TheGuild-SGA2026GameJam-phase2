---
name: bug-report
description: "Creates a structured bug report or analyzes code for potential bugs. Includes severity assessment and reproduction steps."
argument-hint: "[description] or 'analyze [path]'"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write
---

When invoked with a description:

1. **Parse the description** for key information.
2. **Search the codebase** (`TheGuild-unity/Assets/Scripts/`) for related files.
3. **Generate the bug report**:

```markdown
# Bug Report

## Summary
**Title**: [Concise title]
**ID**: BUG-[NNNN]
**Severity**: [S1-Critical / S2-Major / S3-Minor / S4-Trivial]
**Priority**: [P1-Immediate / P2-Next Sprint / P3-Backlog]
**Status**: Open
**Reported**: [Date]

## Classification
- **Category**: [Gameplay / UI / Economy / Quest / Adventurer / Save]
- **System**: [Which game system]
- **Frequency**: [Always / Often / Sometimes / Rare]

## Reproduction Steps
**Preconditions**: [Required state]

1. [Step 1]
2. [Step 2]
3. [Step 3]

**Expected**: [What should happen]
**Actual**: [What happens]

## Technical Context
- **Likely files**: [Based on codebase search]
- **Related systems**: [Cross-system impact]
- **Possible cause**: [If identifiable]

## Related
- **Design doc**: [Link to relevant GDD]
- **Related bugs**: [Links]
```

4. **Save to** `production/bugs/BUG-[NNNN].md` (create dir if needed).

When invoked with `analyze [path]`:

1. **Read the target file(s)**.
2. **Identify potential bugs**: null refs, off-by-one, race conditions, unhandled edge cases, Unity-specific issues (destroyed object access, missing component refs).
3. **For each potential bug**, generate a mini bug report.
