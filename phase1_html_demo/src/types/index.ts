// Core type definitions — The Guild
// Agents: DO NOT modify this file without updating all dependent systems

export type Rank = 'F' | 'E' | 'D' | 'C' | 'B' | 'A' | 'S'

// Implements: design/gdd-commission-tiers.md
// Commission board tier — controls availability (by guildRep) and reward multiplier.
export type CommissionTier = 'common' | 'silver' | 'gold'
export type Difficulty = 'F' | 'E' | 'D' | 'C' | 'B' | 'A' | 'S' | 'SS' | 'SSS'
export type MissionType = '討伐' | '護送' | '採集' | '調查'
export type AdventurerStatus = 'idle' | 'on_mission' | 'dead'
export type WorldDanger = 'E' | 'D' | 'C' | 'B' | 'A'

// ProfessionId is defined in src/data/traits.ts — imported by consumers that need it
export type ProfessionId = 'warrior' | 'mage' | 'ranger' | 'scout' | 'guardian' | 'healer' | 'mercenary'

// RaceId represents the adventurer's racial heritage — affects success/death rate modifiers
// Implements: design/gdd/racial-modifiers.md (混合模式)
export type RaceId = 'human' | 'elf' | 'dwarf' | 'orc' | 'halfling' | 'lizardfolk'

// ---------------------------------------------------------------------------
// Growth Trait System — Adventurer XP & Trait Progression
// Implements: design/gdd/growth-traits.md
// ---------------------------------------------------------------------------

/**
 * Effect type for a growth trait.
 * 'success_rate' and 'death_rate' are applied in calcRates().
 * 'passive' traits are stored but only used by future combat systems.
 */
export type TraitEffectType = 'success_rate' | 'death_rate' | 'passive'

/** A single growth trait definition. */
export interface TraitEffect {
  /** Unique identifier, e.g. 'iron_will'. */
  id: string
  /** Display name shown in UI. */
  name: string
  /** Brief description for tooltip. */
  description: string
  /** What stat this trait modifies. */
  effectType: TraitEffectType
  /**
   * Additive modifier as a decimal (e.g. 0.05 = +5%).
   * For 'death_rate' effects, negative values reduce death chance.
   * Ignored when effectType is 'passive'.
   */
  value: number
}

/**
 * A trait unlocked by an individual adventurer through XP milestones.
 * Stored on Adventurer.growthTraits[].
 */
export interface AdventurerGrowthTrait {
  traitId: string
  /** Timestamp (ms) when the trait was first unlocked. */
  unlockedAt: number
}

export interface Adventurer {
  id: string
  name: string
  rank: Rank
  professionId: ProfessionId
  /** Racial heritage — affects success/death rate modifiers via calcRates(). */
  raceId: RaceId
  status: AdventurerStatus
  currentMissionId: string | null
  /** Experience points accumulated through mission completions. */
  xp: number
  /** Traits unlocked by reaching XP milestones. */
  growthTraits: AdventurerGrowthTrait[]
  /** Generated biographical flavor text. Unique per adventurer. */
  bio: string
  /**
   * Fixed invitation cost snapshotted at candidate-pool generation time.
   * When present, overrides the live recruitCost(rank) calculation so the
   * cost displayed in the UI and deducted on recruit are always identical.
   * Implements: adventurer-management.md § 招募費用一致性
   */
  fixedRecruitCost?: number
}

export interface Mission {
  id: string
  name: string
  type: MissionType
  difficulty: Difficulty
  baseReward: number
  duration: number  // minutes
  description: string
  isTemplate: boolean
  /** Commission board tier — set at generation time, reward multiplier already applied.
   *  Implements: design/gdd-commission-tiers.md */
  tier: CommissionTier
  /**
   * Timestamp (ms) when the guild master accepted and posted this commission to the board.
   * Set in onReviewAccept (main.ts). Undefined while the mission is still in pendingReview.
   * Used by the commission card to display a "新" badge for recently posted missions.
   */
  postedAt?: number
}

/** A single member in a party dispatch. */
export interface PartyMember {
  id: string
  name: string
  professionId: ProfessionId
}

export interface DispatchRecord {
  id: string
  missionId: string
  missionName: string
  /** Lead / solo adventurer (first party member). */
  adventurerId: string
  adventurerName: string
  /** All party members including lead. Length >= 1. */
  partyMembers: PartyMember[]
  /** Active synergy bonus names (e.g. '戰士+治療師'). */
  activeSynergies: string[]
  startTimestamp: number
  endTimestamp: number
  finalSuccessRate: number
  finalDeathRate: number
  preCollectedAmount: number
}

export type OutcomeType = 'SUCCESS' | 'FAILURE' | 'DEATH' | 'PYRRHIC'

export interface OutcomeResult {
  type: OutcomeType
  record: DispatchRecord
  goldDelta: number
  reputationDelta: number
  message: string
}

export interface ResourceState {
  gold: number
  reputation: number  // -100 to 100 raw; shown as label
}

export interface GuildState {
  resources: ResourceState
  adventurers: Adventurer[]
  missionPool: Mission[]
  pendingReview: Mission[]       // 待審核委託池（尚未公示）
  activeMissions: DispatchRecord[]
  completedMissionIds: Set<string>
  guildLevel: number
  worldDanger: WorldDanger
}

// ---------------------------------------------------------------------------
// Offline Return Event System — 「信使歸來」
// Implements: design/gdd-offline-return.md
// ---------------------------------------------------------------------------

/** Single mission settlement result included in an OfflineReturnEvent. */
export interface OfflineResolution {
  missionId: string
  missionName: string
  adventurerName: string
  outcome: OutcomeType
  /** Net gold change applied to the guild (positive = income, negative = loss). */
  goldDelta: number
  /** Additional gold from return bonus; non-zero only for SUCCESS/PYRRHIC when bonus is active. */
  bonusGoldFromBonus?: number
}

// ---------------------------------------------------------------------------
// NPC Autonomous Mission System — 「自主接委託」
// Implements: design/gdd-npc-autonomous-mission.md
// ---------------------------------------------------------------------------

/**
 * Result for an NPC mission that completed while the player was offline (State 2).
 * Gold is applied immediately on offline return.
 */
export interface NpcMissionResolution {
  adventurerName: string
  missionName: string
  outcome: 'SUCCESS' | 'FAILURE'
  /** Gold the guild receives. Non-zero only on SUCCESS (baseReward * 0.2). */
  goldDelta: number
}

/**
 * An NPC mission that is still running when the player returns (State 1).
 * The corresponding DispatchRecord has already been pushed to activeMissions.
 */
export interface NpcActiveMission {
  adventurerName: string
  missionName: string
  /** Milliseconds remaining until the mission ends. */
  remainingMs: number
}

/**
 * Assembled event data for the offline return summary overlay.
 * Produced by buildOfflineReturnEvent() in systems/offline-return.ts.
 */
export interface OfflineReturnEvent {
  /** UUID for dedup / logging. */
  eventId: string
  offlineStartTimestamp: number
  offlineEndTimestamp: number
  offlineDurationMs: number
  resolutions: OfflineResolution[]
  candidatePoolRefreshed: boolean
  /** True when at least 1 SUCCESS or PYRRHIC occurred AND offline >= 10 min. */
  bonusCommissionEligible: boolean
  /** True when eligible AND offline <= 30 min (player returned in time). */
  bonusCommissionActive: boolean
  /** Total extra gold awarded. 0 when bonusCommissionActive is false. */
  bonusCommissionAmount: number
  /** NPC missions that finished during offline (State 2 — already resolved). */
  npcResolutions: NpcMissionResolution[]
  /** NPC missions still running when player returns (State 1 — added to activeMissions). */
  npcActiveMissions: NpcActiveMission[]
  /** DispatchRecords for State 1 NPC missions — caller pushes these to activeMissions. */
  npcDispatchRecords: DispatchRecord[]
}
