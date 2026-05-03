/**
 * Save / Load System — The Guild
 * Implements: design/gdd/save-load.md
 *
 * Responsibilities:
 *   1. Auto-save to localStorage (key: 'the-guild-save').
 *   2. Load / validate save data on startup.
 *   3. Export save as a dated .json file download.
 *   4. Import save from a .json file chosen by the player.
 *
 * Note on field naming: the GDD uses `saveFmt` for the version field, but the
 * agreed implementation interface uses `saveVersion`. Both refer to the same
 * concept (format version = 1). `saveVersion` is used throughout this file.
 *
 * All gameplay values that are adjustable are marked with §調整旋鈕.
 */

import type {
  GuildState,
  ResourceState,
  Adventurer,
  Mission,
  DispatchRecord,
  WorldDanger,
} from '../types'
import { getRandomRace } from '../data/races'
import { generateBio } from '../data/bios'
import type { TickState } from '../systems/tick'
import type { SettlementRecord } from '../systems/outcome'

// ---------------------------------------------------------------------------
// Constants — §調整旋鈕
// ---------------------------------------------------------------------------

/** localStorage key for the single save slot. Never change without a migration plan. */
const SAVE_KEY = 'the-guild-save'

/** Current save format version. Increment on every incompatible schema change. */
const CURRENT_SAVE_VERSION = 1

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Serialized representation of all persistent game state.
 * `completedMissionIds` is stored as Array because JSON does not support Set.
 * `candidatePoolNextRefreshAt` and `lastSavedAt` are flattened from TickState
 * to keep the top-level structure flat and easy to validate.
 */
export interface SaveData {
  saveVersion: number
  savedAt: number
  resources: ResourceState
  adventurers: Adventurer[]
  missionPool: Mission[]
  pendingReview: Mission[]        // 待審核委託池
  activeMissions: DispatchRecord[]
  completedMissionIds: string[]   // Set<string> serialised as Array
  guildLevel: number
  worldDanger: WorldDanger
  candidatePoolNextRefreshAt: number
  lastSavedAt: number
  pendingResults: SettlementRecord[]
  messageLog: string[]            // 訊息欄記錄
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

type TimestampStatus = 'ok' | 'future' | 'missing'
type VersionStatus   = 'ok' | 'outdated' | 'unknown'

function validateTimestamp(savedAt: unknown): TimestampStatus {
  if (!savedAt || typeof savedAt !== 'number') return 'missing'
  if (savedAt > Date.now())                    return 'future'
  return 'ok'
}

function validateVersion(saveVersion: unknown): VersionStatus {
  if (typeof saveVersion !== 'number') return 'unknown'
  if (saveVersion === CURRENT_SAVE_VERSION) return 'ok'
  if (saveVersion < CURRENT_SAVE_VERSION)  return 'outdated'
  return 'unknown'
}

/**
 * Validates raw parsed JSON as a SaveData object.
 * Returns the (possibly patched) SaveData on success, or null on fatal failure.
 *
 * Validation rules (GDD §詳細設計 讀取流程):
 *   - saveVersion unknown (from a future build) → return null, do not overwrite.
 *   - saveVersion outdated → patch missing fields with defaults, continue.
 *   - savedAt in the future → log warning, continue (GDD §極端情況 6).
 *   - Missing required fields → fill with defaults, continue (GDD §極端情況 4).
 */
function validateSave(raw: unknown): SaveData | null {
  if (raw === null || typeof raw !== 'object') {
    console.error('[save-load] validateSave: not an object')
    return null
  }

  const data = raw as Record<string, unknown>

  // --- Version check ----------------------------------------------------------
  const versionStatus = validateVersion(data['saveVersion'])
  if (versionStatus === 'unknown') {
    console.warn(
      '[save-load] validateSave: saveVersion is from a newer build — refusing to load.',
      data['saveVersion'],
    )
    return null
  }
  if (versionStatus === 'outdated') {
    console.warn(
      '[save-load] validateSave: outdated saveVersion, patching missing fields.',
      data['saveVersion'],
    )
  }

  // --- Timestamp check --------------------------------------------------------
  const tsStatus = validateTimestamp(data['savedAt'])
  if (tsStatus === 'future') {
    console.warn(
      '[save-load] validateSave: savedAt is in the future (clock skew?). Continuing.',
      data['savedAt'],
    )
  }
  if (tsStatus === 'missing') {
    console.warn('[save-load] validateSave: savedAt missing, defaulting to 0.')
  }

  // --- Patch missing fields with defaults -------------------------------------
  const now = Date.now()

  const patched: SaveData = {
    saveVersion:                  CURRENT_SAVE_VERSION,
    savedAt:                      typeof data['savedAt'] === 'number'  ? data['savedAt']  : 0,
    resources:                    isResourceState(data['resources'])    ? data['resources'] : { gold: 0, reputation: 0 },
    adventurers:                  Array.isArray(data['adventurers'])    ? patchAdventurers(data['adventurers'] as Adventurer[]) : [],
    missionPool:                  Array.isArray(data['missionPool'])    ? data['missionPool'] as Mission[]    : [],
    pendingReview:                Array.isArray(data['pendingReview'])  ? data['pendingReview'] as Mission[]  : [],
    activeMissions:               Array.isArray(data['activeMissions']) ? data['activeMissions'] as DispatchRecord[] : [],
    completedMissionIds:          Array.isArray(data['completedMissionIds']) ? data['completedMissionIds'] as string[] : [],
    guildLevel:                   typeof data['guildLevel'] === 'number'   ? data['guildLevel']   : 1,
    worldDanger:                  isWorldDanger(data['worldDanger'])        ? data['worldDanger']  : 'E',
    candidatePoolNextRefreshAt:   typeof data['candidatePoolNextRefreshAt'] === 'number'
                                    ? data['candidatePoolNextRefreshAt']
                                    : now + 43_200_000,
    lastSavedAt:                  typeof data['lastSavedAt'] === 'number'  ? data['lastSavedAt']  : now,
    pendingResults:               Array.isArray(data['pendingResults'])     ? data['pendingResults'] as SettlementRecord[] : [],
    messageLog:                   Array.isArray(data['messageLog'])         ? data['messageLog'] as string[]              : [],
  }

  return patched
}

/**
 * Patches missing XP / growthTraits fields on adventurers from old saves.
 * Safe to call on current-version adventurers — existing values are preserved.
 */
function patchAdventurers(advs: Adventurer[]): Adventurer[] {
  return advs.map(adv => ({
    ...adv,
    xp:           typeof (adv as any).xp === 'number'         ? (adv as any).xp           : 0,
    growthTraits: Array.isArray((adv as any).growthTraits)    ? (adv as any).growthTraits : [],
    raceId:       (adv as any).raceId                         ?? getRandomRace(),
    bio:          typeof (adv as any).bio === 'string'         ? (adv as any).bio          : generateBio(adv.professionId, adv.rank),
  }))
}

function isResourceState(v: unknown): v is ResourceState {
  if (!v || typeof v !== 'object') return false
  const r = v as Record<string, unknown>
  return typeof r['gold'] === 'number' && typeof r['reputation'] === 'number'
}

const WORLD_DANGER_VALUES: WorldDanger[] = ['E', 'D', 'C', 'B', 'A']
function isWorldDanger(v: unknown): v is WorldDanger {
  return typeof v === 'string' && (WORLD_DANGER_VALUES as string[]).includes(v)
}

/** Assembles a SaveData snapshot from live game state. Does NOT set savedAt. */
function collectState(
  state: GuildState,
  pendingResults: SettlementRecord[],
  tickState: TickState,
  messageLog: string[] = [],
): Omit<SaveData, 'savedAt'> {
  return {
    saveVersion:                CURRENT_SAVE_VERSION,
    resources:                  state.resources,
    adventurers:                state.adventurers,
    missionPool:                state.missionPool,
    pendingReview:              state.pendingReview,
    activeMissions:             state.activeMissions,
    completedMissionIds:        Array.from(state.completedMissionIds),
    guildLevel:                 state.guildLevel,
    worldDanger:                state.worldDanger,
    candidatePoolNextRefreshAt: tickState.candidatePoolNextRefreshAt,
    lastSavedAt:                tickState.lastSavedAt,
    pendingResults,
    messageLog,
  }
}

/** Formats a Date as YYYY-MM-DD for use in export filenames. */
function formatDateYMD(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Serialises current game state and writes it to localStorage.
 * Catches QuotaExceededError and logs a warning without throwing
 * (GDD §極端情況 8).
 */
export function saveGame(
  state: GuildState,
  pendingResults: SettlementRecord[],
  tickState: TickState,
  messageLog: string[] = [],
): void {
  const data: SaveData = {
    ...collectState(state, pendingResults, tickState, messageLog),
    savedAt: Date.now(),
  }
  try {
    localStorage.setItem(SAVE_KEY, JSON.stringify(data))
  } catch (err) {
    console.error(
      '[save-load] saveGame: localStorage write failed (quota exceeded?). ' +
      'Recommend immediate export.',
      err,
    )
  }
}

/**
 * Reads and validates save data from localStorage.
 * Returns the validated SaveData on success, or null if:
 *   - No save exists.
 *   - JSON is malformed (GDD §極端情況 3).
 *   - Save version is from a future build (GDD §極端情況 5).
 */
export function loadGame(): SaveData | null {
  const raw = localStorage.getItem(SAVE_KEY)
  if (raw === null) return null

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch (err) {
    console.error('[save-load] loadGame: JSON.parse failed — treating as no save.', err)
    return null
  }

  return validateSave(parsed)
}

/**
 * Exports current game state as a dated .json file download.
 * File name format: the-guild-save-YYYY-MM-DD.json  (§調整旋鈕)
 */
export function exportSave(
  state: GuildState,
  pendingResults: SettlementRecord[],
  tickState: TickState,
  messageLog: string[] = [],
): void {
  const data: SaveData = {
    ...collectState(state, pendingResults, tickState, messageLog),
    savedAt: Date.now(),
  }

  const json  = JSON.stringify(data, null, 2)
  const blob  = new Blob([json], { type: 'application/json' })
  const url   = URL.createObjectURL(blob)
  const date  = formatDateYMD(new Date())

  const anchor      = document.createElement('a')
  anchor.href       = url
  anchor.download   = `the-guild-save-${date}.json`
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}

/**
 * Reads a File selected by the player, validates it as SaveData, and—if
 * valid—writes it to localStorage (overwriting any existing save).
 *
 * Returns the validated SaveData on success, or null on any error.
 * Callers should inspect the return value to decide whether to reload state
 * and show a success / error message to the player.
 *
 * Invalid files do not touch localStorage (GDD §極端情況 7).
 */
export function importSave(file: File): Promise<SaveData | null> {
  return new Promise((resolve) => {
    const reader = new FileReader()

    reader.onload = (event) => {
      const text = event.target?.result
      if (typeof text !== 'string') {
        console.error('[save-load] importSave: FileReader result is not a string.')
        resolve(null)
        return
      }

      let parsed: unknown
      try {
        parsed = JSON.parse(text)
      } catch (err) {
        console.error('[save-load] importSave: JSON.parse failed.', err)
        resolve(null)
        return
      }

      const validated = validateSave(parsed)
      if (validated === null) {
        console.warn('[save-load] importSave: validation failed — localStorage unchanged.')
        resolve(null)
        return
      }

      // Overwrite localStorage with the imported data (GDD §File 匯入 step 4).
      try {
        localStorage.setItem(SAVE_KEY, text)
      } catch (err) {
        console.error('[save-load] importSave: localStorage write failed.', err)
        resolve(null)
        return
      }

      resolve(validated)
    }

    reader.onerror = (event) => {
      console.error('[save-load] importSave: FileReader error.', event)
      resolve(null)
    }

    reader.readAsText(file)
  })
}

/**
 * Returns true if a save exists in localStorage.
 * Does not validate the save data — use loadGame() for that.
 */
export function hasSave(): boolean {
  return localStorage.getItem(SAVE_KEY) !== null
}

/**
 * Removes the save from localStorage.
 * Intended for "New Game" flows; does not affect in-memory state.
 */
export function deleteSave(): void {
  localStorage.removeItem(SAVE_KEY)
}
