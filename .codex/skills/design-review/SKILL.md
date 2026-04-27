---
name: design-review
description: "Review a game design document for completeness, consistency, implementability, dependency alignment, acceptance criteria, and adherence to the project eight-section GDD standard. Use before converting design into implementation work."
---

## Skill Contract

Use this skill when the user invokes $design-review or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $design-review <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:


## Design Review: [Document Title]

### Completeness: [X/8 sections present]
[List missing sections]

### Consistency Issues
[Internal or cross-system contradictions]

### Implementability Concerns
[Vague or unimplementable sections — critical for implementation work]

### Balance Concerns
[Obvious balance risks]

### Recommendations
[Prioritized improvements]

### Verdict: [APPROVED / NEEDS REVISION / MAJOR REVISION NEEDED]
```

9. **Next step recommendations**:
   - If APPROVED: "Ready for work item creation. Convert to implementation work items."
   - If NEEDS REVISION: "Fix the listed issues, then re-run `$design-review`."
   - If MAJOR REVISION NEEDED: "Consider re-running `$design-system` for this system."



