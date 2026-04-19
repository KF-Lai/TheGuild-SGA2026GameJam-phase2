---
name: map-systems
description: "Decompose the game concept into systems, map dependencies, prioritize design order, and create the systems index."
argument-hint: "[optional: 'next' to pick highest-priority undesigned system]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit
---

When this skill is invoked:

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

After index creation, offer: "Start designing with `/design-system [first-system]`?"

If invoked with `next`: pick highest-priority undesigned system and invoke `/design-system`.
