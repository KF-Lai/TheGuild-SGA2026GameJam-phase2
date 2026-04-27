---
name: devlog
description: "Summarizes today's development work and appends a new DevLog_YYMMDD.md into the DevLogs/ folder at project root. Uses git log + git diff --stat to discover changes; compares against previous logs to capture only new items."
argument-hint: "[起始 commit hash（可選）] — 若指定，從該 commit 之後開始掃描；否則自動以今日日期過濾"
user-invocable: true
allowed-tools: Read, Glob, Write, Bash
---

When this skill is invoked:

## 1. Determine Today's Date

Get today's date from the system context (`currentDate`). Format as `YYMMDD` for the filename and `YYYY-MM-DD` for content.

## 2. Check for Existing Log

Check if `DevLogs/DevLog_YYMMDD.md` already exists for today.

- If it exists: read it to avoid duplicating entries. Offer to append new items or overwrite.
- If it does not exist: proceed to create.

## 3. Read Previous Log for Comparison

Read the most recent existing log in `DevLogs/` (sorted by filename descending, skip today's if it exists) to understand what was already recorded. This helps identify what is genuinely new today.

## 4. Discover Today's Changes via Git

Use git to determine what changed since the last logged session.

### 4a. Find the Baseline Commit

**If a starting commit hash is provided as an argument:**

```bash
git log <hash>..HEAD --oneline
```

Use that hash as the baseline.

**Otherwise, auto-detect from today's date:**

```bash
git log --after="YYYY-MM-DD 00:00" --oneline
```

If no commits found today, fall back to the last 3 commits:

```bash
git log -3 --oneline
```

### 4b. Scan Changed Files (--stat only, NOT full diff)

```bash
git diff <baseline>..HEAD --stat
```

If no baseline commit (first run ever), use:

```bash
git diff HEAD~3..HEAD --stat
```

**Token budget: keep this scan under 2,000 tokens total.** If the stat output is very long (>60 lines), truncate with `| head -60` and note to the user that output was trimmed.

### 4c. Interpret the Stat Output

From the file paths and insertion/deletion counts alone, infer:

- Which GDD files were created or significantly rewritten (large `+` counts)
- Which Data-Specs were added (new files in `design/Data-Specs/`)
- Which scripts were modified (`Assets/Scripts/`)
- Which CSV data files changed (`Assets/Resources/Data/`)
- Which skill/rule/config files were updated (`.claude/`)
- Which test files changed (`Tests/`)

Do NOT read the full diff of individual files. The stat is sufficient for a devlog summary.

## 5. Summarize Today's Work

Synthesize the git stat findings and compare against the previous devlog to identify what is genuinely new. Follow these principles:

1. **概述（2 sentences max）**: Capture the day's main theme based on the largest change clusters
2. **執行內容**: Bullet list of concrete deliverables grouped by category. One line per item. Do NOT repeat items already in the previous devlog.
3. **建議明日繼續**: 2–4 bullet points of logical next steps based on current progress and systems-index.md status

## 6. Write the Log

Create `DevLogs/DevLog_YYMMDD.md` using this template:

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

## 7. Confirm

After writing the file, report the file path and a one-line summary of what was logged. Do not dump the full file content back to the user.
