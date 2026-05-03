/**
 * Time / Tick System — The Guild
 * Implements: design/gdd/time-tick.md
 *
 * The single clock for all timed events in the game. Responsibilities:
 *   1. Foreground tick (every 10 s) — detect expired missions and candidate-pool refresh.
 *   2. Offline-progress sweep — on page load, batch-resolve all events that fired while away.
 *
 * This module is intentionally logic-free: it detects "time has passed" and
 * delegates consequences to callbacks. It never imports other src/systems files.
 *
 * @example
 * ```typescript
 * const tickState = createTickState()
 * processOfflineProgress(tickState, guild.activeMissions, callbacks)
 * const stopTick = startTick(tickState, guild.activeMissions, callbacks)
 * // later, on unmount:
 * stopTick()
 * ```
 */

import type { DispatchRecord } from '../types'

// ---------------------------------------------------------------------------
// Constants — tunable values (see GDD §調整旋鈕)
// ---------------------------------------------------------------------------

/** How often the foreground tick runs (milliseconds). */
const TICK_INTERVAL_MS = 1_000

/** Duration of the candidate-pool refresh cycle: 12 hours in milliseconds. */
const CANDIDATE_POOL_REFRESH_INTERVAL_MS = 43_200_000

/**
 * Minimum offline duration (ms) required to trigger the Offline Return Event.
 * Exported so offline-return.ts can cross-check without duplicating the constant.
 * See design/gdd-offline-return.md §1.
 */
export const OFFLINE_THRESHOLD_MS = 600_000

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Metadata returned by processOfflineProgress when offline >= OFFLINE_THRESHOLD_MS.
 * Callers use this to decide whether to build and display an OfflineReturnEvent.
 * The full OfflineReturnEvent (with resolution details) is assembled in the caller
 * after settlement callbacks have run — tick.ts intentionally has no knowledge of
 * outcome data. See design/gdd-offline-return.md §4.
 */
export interface OfflineProgressResult {
  offlineStartTimestamp: number
  offlineEndTimestamp: number
  offlineDurationMs: number
  candidatePoolRefreshed: boolean
}

/**
 * Callbacks invoked by the tick system when timed events fire.
 * Implementations must be idempotent — the same id will not be passed twice
 * per tick, but the caller is responsible for removing resolved records from
 * `activeMissions` to prevent repeat triggers on subsequent ticks.
 */
export interface TickCallbacks {
  /**
   * Called when one or more active missions have reached their `endTimestamp`.
   * Ids are sorted ascending by `endTimestamp` (oldest expiry first).
   * @param expiredIds - `DispatchRecord.id` values for every expired mission.
   */
  onMissionsExpired: (expiredIds: string[]) => void

  /**
   * Called once per refresh cycle when `candidatePoolNextRefreshAt` has been
   * reached. During offline sweeps this fires at most once (last cycle wins).
   */
  onCandidatePoolRefresh: () => void
}

/**
 * Persistent tick state that must be serialized into save data.
 * All fields are wall-clock timestamps in milliseconds (`Date.now()` domain).
 */
export interface TickState {
  /** Timestamp at which the next candidate-pool refresh should fire. */
  candidatePoolNextRefreshAt: number

  /**
   * Timestamp of the last successful save. Used to compute offline duration
   * on the next page load, but the tick system itself only writes to it inside
   * `processOfflineProgress`.
   */
  lastSavedAt: number
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Returns a fresh `TickState` suitable for a brand-new game session.
 * `candidatePoolNextRefreshAt` is initialized to 12 hours from now.
 * `lastSavedAt` is set to the current wall-clock time.
 *
 * @example
 * ```typescript
 * const tickState = createTickState()
 * // tickState.candidatePoolNextRefreshAt === Date.now() + 43_200_000
 * ```
 */
export function createTickState(): TickState {
  const now = Date.now()
  return {
    candidatePoolNextRefreshAt: now + CANDIDATE_POOL_REFRESH_INTERVAL_MS,
    lastSavedAt: now,
  }
}

/**
 * Performs a one-shot offline-progress sweep, intended to be called once per
 * page load, before `startTick`.
 *
 * Behaviour:
 * - All `activeMissions` with `endTimestamp <= Date.now()` are collected,
 *   sorted ascending by `endTimestamp`, and passed to `onMissionsExpired` in
 *   a single call.
 * - `candidatePoolNextRefreshAt` is advanced through as many 12-hour windows
 *   as needed. If at least one window elapsed, `onCandidatePoolRefresh` is
 *   called exactly once (intermediate cycles are not replayed — GDD §極端情況 2).
 * - `state.lastSavedAt` is updated to `Date.now()` after processing.
 *
 * @param state          - Mutable tick state (modified in-place).
 * @param activeMissions - Current in-flight missions (read-only by this function).
 * @param callbacks      - Event handlers for downstream systems.
 *
 * @example
 * ```typescript
 * // Called once during game-load, after restoring save data:
 * processOfflineProgress(savedTickState, guild.activeMissions, callbacks)
 * ```
 */
/**
 * Performs a one-shot offline-progress sweep, intended to be called once per
 * page load, before `startTick`.
 *
 * Returns `OfflineProgressResult` when the offline duration is >= OFFLINE_THRESHOLD_MS
 * (10 minutes), enabling the caller to build and display an OfflineReturnEvent.
 * Returns `null` for short offline periods — direct state application only.
 *
 * The returned result does NOT include settlement outcome details; those are
 * populated by the `onMissionsExpired` callback (handled by the caller's
 * outcome system). Callers should build the full OfflineReturnEvent AFTER
 * the callbacks have run. See design/gdd-offline-return.md §4.
 */
export function processOfflineProgress(
  state: TickState,
  activeMissions: DispatchRecord[],
  callbacks: TickCallbacks,
): OfflineProgressResult | null {
  const now = Date.now()
  const offlineStartTimestamp = state.lastSavedAt
  const offlineDurationMs = now - offlineStartTimestamp

  // --- Expired missions -------------------------------------------------------
  const expired = activeMissions
    .filter((r) => r.endTimestamp <= now)
    .sort((a, b) => a.endTimestamp - b.endTimestamp)

  if (expired.length > 0) {
    callbacks.onMissionsExpired(expired.map((r) => r.id))
  }

  // --- Candidate-pool refresh -------------------------------------------------
  // Advance the refresh clock through all elapsed 12-hour windows.
  // Only fire the callback once regardless of how many windows passed.
  let refreshFired = false
  while (state.candidatePoolNextRefreshAt <= now) {
    state.candidatePoolNextRefreshAt += CANDIDATE_POOL_REFRESH_INTERVAL_MS
    refreshFired = true
  }
  if (refreshFired) {
    callbacks.onCandidatePoolRefresh()
  }

  // --- Mark offline sweep complete --------------------------------------------
  state.lastSavedAt = now

  // --- Return offline metadata for Offline Return Event (gdd-offline-return.md §1) ---
  if (offlineDurationMs >= OFFLINE_THRESHOLD_MS) {
    return {
      offlineStartTimestamp,
      offlineEndTimestamp: now,
      offlineDurationMs,
      candidatePoolRefreshed: refreshFired,
    }
  }
  return null
}

/**
 * Starts the foreground tick loop.
 *
 * A `setInterval` fires every 10 seconds and:
 * 1. Collects all `activeMissions` with `endTimestamp <= Date.now()` and calls
 *    `onMissionsExpired`. The caller must remove resolved records from the
 *    array to prevent re-triggering on subsequent ticks.
 * 2. Checks whether `candidatePoolNextRefreshAt` has been reached. If so,
 *    advances the timestamp by one refresh interval and calls
 *    `onCandidatePoolRefresh`.
 *
 * Browser tab throttling: because expiry is determined by comparing
 * `endTimestamp` against `Date.now()` (not by counting ticks), any delays
 * introduced by background-tab throttling are self-correcting — the correct
 * result is produced as soon as the tab is foregrounded again.
 *
 * @param state          - Mutable tick state (modified in-place for refresh tracking).
 * @param activeMissions - Live reference to the active-mission list. Must be
 *                         the same array that the caller mutates as missions resolve.
 * @param callbacks      - Event handlers for downstream systems.
 * @returns A stop function. Call it to clear the interval (e.g., on scene teardown).
 *
 * @example
 * ```typescript
 * const stop = startTick(tickState, guild.activeMissions, callbacks)
 * // on cleanup:
 * stop()
 * ```
 */
export function startTick(
  state: TickState,
  activeMissions: DispatchRecord[],
  callbacks: TickCallbacks,
): () => void {
  const intervalId = setInterval(() => {
    const now = Date.now()

    // --- Expired missions -----------------------------------------------------
    const expiredIds = activeMissions
      .filter((r) => r.endTimestamp <= now)
      .sort((a, b) => a.endTimestamp - b.endTimestamp)
      .map((r) => r.id)

    if (expiredIds.length > 0) {
      callbacks.onMissionsExpired(expiredIds)
    }

    // --- Candidate-pool refresh -----------------------------------------------
    if (state.candidatePoolNextRefreshAt <= now) {
      state.candidatePoolNextRefreshAt = now + CANDIDATE_POOL_REFRESH_INTERVAL_MS
      callbacks.onCandidatePoolRefresh()
    }
  }, TICK_INTERVAL_MS)

  return () => clearInterval(intervalId)
}
