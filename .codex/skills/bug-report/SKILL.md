---
name: bug-report
description: "Create structured bug reports or analyze target code for likely defects, reproduction steps, severity, affected systems, likely files, and technical context. Use for gameplay, UI, economy, quest, save, and Unity bug triage."
---

## Skill Contract

Use this skill when the user invokes $bug-report or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $bug-report <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.
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



