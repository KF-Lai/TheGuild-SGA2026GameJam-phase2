---
name: design-review
description: "Reviews a game design document for completeness, consistency, implementability, and adherence to the 8-section standard. Run before handing a design to Codex for implementation."
argument-hint: "[path-to-design-doc]"
user-invocable: true
allowed-tools: Read, Glob, Grep
---

When this skill is invoked:

1. **Read the target design document** in full.

2. **Read CLAUDE.md** for project context and standards.

3. **Read related design documents** in `design/gdd/` for cross-system consistency.

4. **Evaluate against the 8-section standard**:
   - [ ] Has Overview section (one-paragraph summary)
   - [ ] Has Player Fantasy section (intended feeling)
   - [ ] Has Detailed Rules section (unambiguous mechanics)
   - [ ] Has Formulas section (all math defined with variables)
   - [ ] Has Edge Cases section (unusual situations handled)
   - [ ] Has Dependencies section (other systems listed)
   - [ ] Has Tuning Knobs section (configurable values identified)
   - [ ] Has Acceptance Criteria section (testable success conditions)

5. **Check internal consistency**:
   - Do formulas produce values matching described behavior?
   - Do edge cases contradict main rules?
   - Are dependencies bidirectional?

6. **Check implementability** (critical for Codex work items):
   - Are rules precise enough for Codex to implement without guessing?
   - Are there hand-wave sections where details are missing?
   - Can acceptance criteria be directly translated to test cases?
   - Are all data values specified (for ScriptableObject/JSON configs)?

7. **Check cross-system consistency**:
   - Conflicts with existing mechanics?
   - Unintended interactions with other systems?
   - Consistent with game pillars and tone?

8. **Output the review**:

```
## Design Review: [Document Title]

### Completeness: [X/8 sections present]
[List missing sections]

### Consistency Issues
[Internal or cross-system contradictions]

### Implementability Concerns
[Vague or unimplementable sections — critical for Codex work items]

### Balance Concerns
[Obvious balance risks]

### Recommendations
[Prioritized improvements]

### Verdict: [APPROVED / NEEDS REVISION / MAJOR REVISION NEEDED]
```

9. **Next step recommendations**:
   - If APPROVED: "Ready for work item creation. Convert to Codex work items."
   - If NEEDS REVISION: "Fix the listed issues, then re-run `/design-review`."
   - If MAJOR REVISION NEEDED: "Consider re-running `/design-system` for this system."
