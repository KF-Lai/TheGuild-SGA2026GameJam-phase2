---
name: map-systems
description: "Decompose a game concept into systems, dependencies, priorities, design order, and a systems index. Use when mapping a project from concept into GDD/FSD planning structure or choosing the next system to design."
---

## Skill Contract

Use this skill when the user invokes $map-systems or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $map-systems <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

When using this skill:


## 1. Read Context

**Required**: `design/gdd/game-concept.md` (or `README.md` if no concept doc yet)
**Optional**: `design/gdd/game-pillars.md`, existing `design/gdd/systems-index.md`, any existing GDDs

If systems index exists, present status and ask: update, design next, or revise priorities?

## 2. Systems Enumeration

Extract systems from the game concept. For The Guild, expected systems include:

**Core**:
- Quest System (posting, assignment, resolution)
- Adventurer System (recruitment, stats, personality, death)
- Economy System (gold, costs, rewards)
- Time System (real-time timers, offline progress)

**Feature**:
- Guild Management (reputation, facilities, upgrades)
- Guild Staff System (staff with specialties)
- Adventurer Decision System (simple version: random acceptance)
- Main Storyline (single faction)

**Presentation**:
- Quest Board UI
- Adventurer Roster UI
- Guild Overview UI

**Data**:
- Adventurer Character Database
- Quest Template Database

Present enumeration, ask user to confirm, add, remove, or combine.

## 3. Dependency Mapping

Map dependencies between systems. Sort into layers:
1. **Foundation**: Zero dependencies (Time, core data structures)
2. **Core**: Depends on Foundation only
3. **Feature**: Depends on Core
4. **Presentation**: UI wrapping gameplay systems
5. **Polish**: Tutorial, accessibility, etc.

Detect circular dependencies and propose resolutions.

## 4. Priority Assignment

For Game Jam, use these tiers:
- **MVP**: Core loop systems (quest + adventurer + economy + time)
- **Game Jam Target**: Guild management, staff, simple decisions, storyline
- **If Time Permits**: Polish, extended content, advanced features

Determine design order: MVP Foundation first, then Core, then Features.

## 5. Write Systems Index

Create `design/gdd/systems-index.md` with:
- Enumeration table
- Dependency map
- Design order
- Progress tracker

## 6. Handoff

After index creation, offer: "Start designing with `$design-system [first-system]`?"

If invoked with `next`: pick highest-priority undesigned system and invoke `$design-system`.



