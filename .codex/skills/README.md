# Codex/GPT Skill Library

This folder stores repository-local skill prompts in standard Codex skill format:

`<skill-name>/SKILL.md`

Each skill uses lowercase hyphen-case names, Codex-compatible YAML frontmatter, and an optional `agents/openai.yaml` metadata file.

Use skills by naming them explicitly in Codex or GPT prompts:

```text
Use $design-review to review design/GDD/<target>.md
Use $design-fsd to generate an FSD for F-01
Use $code-review to review this patch
```

Available skills:
- `auto-code-optimize`
- `auto-code-optimize-batch`
- `brainstorm`
- `bug-report`
- `code-review`
- `design-ds`
- `design-fsd`
- `design-review`
- `design-system`
- `devlog`
- `map-systems`
- `project-stage-detect`
- `scope-check`
- `sprint-plan`

Source: migrated and normalized from `skills_for_codex/*/SKILL.md`.
