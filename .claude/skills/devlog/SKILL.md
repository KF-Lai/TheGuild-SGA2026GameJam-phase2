---
name: devlog
description: "Summarizes today's development work and appends a new DevelopLog_YYMMDD.md into the devlogs/ folder at project root. Compares against previous logs to capture only new items."
argument-hint: "(no arguments needed)"
user-invocable: true
allowed-tools: Read, Glob, Write, Bash
---

When this skill is invoked:

## 1. Determine Today's Date

Get today's date from the system context (`currentDate`). Format as `YYMMDD` for the filename and `YYYY-MM-DD` for content.

## 2. Check for Existing Log

Check if `devlogs/DevelopLog_YYMMDD.md` already exists for today.

- If it exists: read it to avoid duplicating entries. Offer to append new items or overwrite.
- If it does not exist: proceed to create.

## 3. Read Previous Log for Comparison

Read the most recent existing log in `devlogs/` (sorted by filename descending) to understand what was already recorded. This helps identify what is genuinely new today.

## 4. Summarize Today's Work

Summarize the current session's development activities following these principles:

1. **概述（2 sentences max）**: One or two sentences capturing the day's main theme
2. **執行內容**: Bullet list of concrete deliverables — GDD files created/updated, systems designed, conventions established, tools configured. Group by category if needed. Do NOT describe in detail; one line per item.
3. **建議明日繼續**: 2–4 bullet points of logical next steps based on current progress and systems-index.md status

## 5. Write the Log

Create `devlogs/DevelopLog_YYMMDD.md` using this template:

```markdown
# DevLog — YYYY-MM-DD

## 概述

[1-2 sentence summary]

---

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
