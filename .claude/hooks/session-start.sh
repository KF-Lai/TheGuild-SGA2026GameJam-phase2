#!/bin/bash
# SessionStart hook: Load project context at session start
# Adapted for Unity project (TheGuild)

echo "=== The Guild — Session Context ==="

# Current branch
BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)
if [ -n "$BRANCH" ]; then
    echo "Branch: $BRANCH"
    echo ""
    echo "Recent commits:"
    git log --oneline -5 2>/dev/null | while read -r line; do
        echo "  $line"
    done
fi

# Current sprint
LATEST_SPRINT=$(ls -t production/sprints/sprint-*.md 2>/dev/null | head -1)
if [ -n "$LATEST_SPRINT" ]; then
    echo ""
    echo "Active sprint: $(basename "$LATEST_SPRINT" .md)"
fi

# Current milestone
LATEST_MILESTONE=$(ls -t production/milestones/*.md 2>/dev/null | head -1)
if [ -n "$LATEST_MILESTONE" ]; then
    echo "Active milestone: $(basename "$LATEST_MILESTONE" .md)"
fi

# Code health (Unity C# source)
SCRIPTS_DIR="TheGuild-unity/Assets/Scripts"
if [ -d "$SCRIPTS_DIR" ]; then
    TODO_COUNT=$(grep -r "TODO" "$SCRIPTS_DIR/" 2>/dev/null | wc -l)
    FIXME_COUNT=$(grep -r "FIXME" "$SCRIPTS_DIR/" 2>/dev/null | wc -l)
    CS_FILES=$(find "$SCRIPTS_DIR" -name "*.cs" 2>/dev/null | wc -l)
    TODO_COUNT=$(echo "$TODO_COUNT" | tr -d ' ')
    FIXME_COUNT=$(echo "$FIXME_COUNT" | tr -d ' ')
    CS_FILES=$(echo "$CS_FILES" | tr -d ' ')
    if [ "$CS_FILES" -gt 0 ]; then
        echo ""
        echo "C# files: $CS_FILES | TODOs: $TODO_COUNT | FIXMEs: $FIXME_COUNT"
    fi
fi

# Active session state recovery
STATE_FILE="production/session-state/active.md"
if [ -f "$STATE_FILE" ]; then
    echo ""
    echo "=== ACTIVE SESSION STATE DETECTED ==="
    echo "Previous session left state at: $STATE_FILE"
    echo "Read this file to recover context."
    echo ""
    head -20 "$STATE_FILE" 2>/dev/null
    TOTAL_LINES=$(wc -l < "$STATE_FILE" 2>/dev/null)
    if [ "$TOTAL_LINES" -gt 20 ]; then
        echo "  ... ($TOTAL_LINES total lines)"
    fi
    echo "=== END SESSION STATE PREVIEW ==="
fi

echo "==================================="
exit 0
