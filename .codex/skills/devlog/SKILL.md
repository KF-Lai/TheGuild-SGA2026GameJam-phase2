---
name: devlog
description: "Summarize current development work into a dated project devlog entry by comparing recent changes, design artifacts, implementation work, and previous logs. Use when asked to create or update a development log."
---

## Skill Contract

Use this skill when the user invokes $devlog or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $devlog <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:

## 執行內容

### [Category, e.g. GDD 設計完成]

- **[Item]** — [one-line description]

### [Category, e.g. 規範建立 / 環境配置 / 程式實作]

- **[Item]** — [one-line description]

---

## 建議明日繼續

- **[Next step]** — [why / what to do]
```

## 6. Confirm

After writing the file, report the file path and a one-line summary of what was logged. Do not dump the full file content back to the user.



