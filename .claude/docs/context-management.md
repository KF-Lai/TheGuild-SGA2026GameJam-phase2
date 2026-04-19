# Context Management

Context is the most critical resource in a Claude Code session. Manage it actively.

## File-Backed State (Primary Strategy)

**The file is the memory, not the conversation.** Files persist across compactions and crashes.

### Session State File

Maintain `production/session-state/active.md` as a living checkpoint. Update after each milestone:

- Design section approved and written to file
- Architecture decision made
- Implementation milestone reached (Codex work item completed)
- Test results obtained

Contents: current task, progress checklist, key decisions, files being worked on, open questions.

### Incremental File Writing

When creating multi-section documents (GDDs, architecture docs):

1. Create the file immediately with a skeleton (all section headers)
2. Discuss and draft one section at a time
3. Write each section as soon as it's approved
4. Update the session state file after each section
5. Previous discussion can be safely compacted — decisions are in the file

## Proactive Compaction

- Compact at ~60-70% context usage, not reactively at the limit
- Use `/clear` between unrelated tasks
- Natural compaction points: after writing a section, after committing, after completing a Codex work item

## Subagent Delegation

Use subagents to keep the main session clean:

- **Use subagents** for code review, multi-file investigation, research (>5k tokens)
- **Use direct reads** when you know exactly which 1-2 files to check
- Provide full context in subagent prompts — they don't inherit conversation history

## Compaction Preservation

When compacted, preserve:

- Reference to `production/session-state/active.md`
- List of files modified and their purpose
- Architectural decisions and rationale
- Active sprint tasks and status
- Codex session IDs for ongoing work items
- Unresolved blockers or questions
- Current task and step

**After compaction:** Read `production/session-state/active.md` first to recover.
