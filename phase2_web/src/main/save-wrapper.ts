/**
 * main/save-wrapper.ts — AppState 存檔 / 載入 wrapper
 *
 * 由於 save-load.ts 的 saveGame / loadGame 只接受 GuildState，
 * 此處用 wrapper 將完整 AppState 序列化後塞進一個兼容的 GuildState 結構，
 * 或透過獨立的 extended save key 儲存其餘 system 狀態。
 *
 * Jam 最簡化策略：
 *   - GuildState 欄位原樣儲存（save-load.ts 既有機制）
 *   - 其餘 system 狀態（outcomeState / worldDanger / recruitment / buildings / staffRoster / gacha）
 *     另以獨立 IndexedDB key 存在同一 slot 的擴充欄位中
 *   - 實作為：以 JSON 序列化整個 ExtendedSaveState，透過 IndexedDB 直接讀寫
 *
 * 注意：不修改 save-load.ts（已 commit）；本模組另起 IndexedDB store 'saves_ext'。
 */

import * as worldDanger  from '../systems/world-danger'
import * as recruitment  from '../systems/recruitment'
import * as building     from '../systems/building'
import * as staff        from '../systems/staff'
import * as gacha        from '../systems/gacha'
import { createOutcomeState } from '../systems/outcome'
import { createAutoPickupTracker } from '../systems/npc-decision'
import * as saveLoad from '../systems/save-load'

import type { AppState } from './types'
import type { GuildState } from '../types'

// ---------------------------------------------------------------------------
// 序列化型別
// ---------------------------------------------------------------------------

interface ExtendedSaveState {
  version: number
  savedAt: number
  /** GuildState 欄位（completedMissionIds 序列化為 string[]） */
  guild: Omit<GuildState, 'completedMissionIds'> & { completedMissionIds: string[] }
  outcomeState: {
    pendingResults: ReturnType<typeof import('../systems/outcome').createOutcomeState>['pendingResults']
  }
  worldDanger: ReturnType<typeof worldDanger.serialize>
  recruitment: ReturnType<typeof recruitment.serialize>
  buildings: ReturnType<typeof building.serialize>
  staffRoster: ReturnType<typeof staff.serialize>
  gacha: ReturnType<typeof gacha.serialize>
  meta: {
    totalAdventurersHired: number
    totalAdventurersDead: number
    totalMissionsCompleted: number
    gameStartMs: number
    bankruptcyWarningStart: number | null
  }
}

const CURRENT_VERSION = 1
const DB_NAME    = 'the-guild-ext'
const DB_VERSION = 1
const STORE_NAME = 'saves'

// ---------------------------------------------------------------------------
// IndexedDB helper
// ---------------------------------------------------------------------------

function openExtDB(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION)
    req.onupgradeneeded = (e) => {
      const db = (e.target as IDBOpenDBRequest).result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'slot' })
      }
    }
    req.onsuccess = (e) => resolve((e.target as IDBOpenDBRequest).result)
    req.onerror   = (e) => reject((e.target as IDBOpenDBRequest).error)
  })
}

// ---------------------------------------------------------------------------
// 公開 API
// ---------------------------------------------------------------------------

/**
 * 將整個 AppState 儲存到指定 slot。
 */
export async function saveAppState(state: AppState, slot: string): Promise<void> {
  const ext: ExtendedSaveState = {
    version: CURRENT_VERSION,
    savedAt: Date.now(),
    guild: {
      ...state.guild,
      completedMissionIds: Array.from(state.guild.completedMissionIds),
    },
    outcomeState: { pendingResults: [...state.outcomeState.pendingResults] },
    worldDanger:  worldDanger.serialize(state.worldDanger),
    recruitment:  recruitment.serialize(state.recruitment),
    buildings:    building.serialize(state.buildings),
    staffRoster:  staff.serialize(state.staffRoster),
    gacha:        gacha.serialize(state.gacha),
    meta: {
      totalAdventurersHired:   state.totalAdventurersHired,
      totalAdventurersDead:    state.totalAdventurersDead,
      totalMissionsCompleted:  state.totalMissionsCompleted,
      gameStartMs:             state.gameStartMs,
      bankruptcyWarningStart:  state.bankruptcyWarningStart,
    },
  }

  const db = await openExtDB()
  return new Promise((resolve, reject) => {
    const tx  = db.transaction(STORE_NAME, 'readwrite')
    const req = tx.objectStore(STORE_NAME).put({ slot, ...ext })
    req.onsuccess = () => resolve()
    req.onerror   = (e) => reject((e.target as IDBRequest).error)
  })
}

/**
 * 從指定 slot 載入並還原 AppState。
 * 找不到存檔或版本不符時回傳 null。
 */
export async function loadAppState(slot: string): Promise<AppState | null> {
  const db = await openExtDB()

  return new Promise((resolve, reject) => {
    const tx  = db.transaction(STORE_NAME, 'readonly')
    const req = tx.objectStore(STORE_NAME).get(slot)

    req.onsuccess = (e) => {
      const record = (e.target as IDBRequest<(ExtendedSaveState & { slot: string }) | undefined>).result
      if (!record) { resolve(null); return }
      if (record.version > CURRENT_VERSION) {
        console.warn('[save-wrapper] saveVersion 來自較新版本，拒絕載入', record.version)
        resolve(null)
        return
      }

      try {
        const guildState: GuildState = {
          ...record.guild,
          completedMissionIds: new Set(record.guild.completedMissionIds),
          buildings: Array.isArray(record.guild.buildings) ? record.guild.buildings : [],
          staffRoster: Array.isArray(record.guild.staffRoster) ? record.guild.staffRoster : [],
          dangerLevel: typeof record.guild.dangerLevel === 'number' ? record.guild.dangerLevel : 0,
        }
        // 舊存檔 activeMissions 缺 difficulty 欄位時補 fallback
        for (const r of guildState.activeMissions) {
          if (!(r as any).difficulty) (r as any).difficulty = 'F'
        }

        const restoredState: AppState = {
          guild:        guildState,
          outcomeState: record.outcomeState ?? createOutcomeState(),
          worldDanger:  worldDanger.deserialize(record.worldDanger),
          recruitment:  recruitment.deserialize(record.recruitment),
          buildings:    building.deserialize(record.buildings),
          staffRoster:  staff.deserialize(record.staffRoster),
          gacha:        gacha.deserialize(record.gacha),
          autoPickup:   createAutoPickupTracker(),
          bankruptcyWarningStart: record.meta?.bankruptcyWarningStart ?? null,
          totalAdventurersHired:  record.meta?.totalAdventurersHired ?? 0,
          totalAdventurersDead:   record.meta?.totalAdventurersDead ?? 0,
          totalMissionsCompleted: record.meta?.totalMissionsCompleted ?? 0,
          gameStartMs:            record.meta?.gameStartMs ?? Date.now(),
        }
        resolve(restoredState)
      } catch (err) {
        console.error('[save-wrapper] 還原失敗', err)
        resolve(null)
      }
    }

    req.onerror = (e) => reject((e.target as IDBRequest).error)
  })
}

/**
 * 刪除指定 slot 的擴充存檔。
 */
export async function deleteAppSave(slot: string): Promise<void> {
  // 同時刪除 save-load.ts 的原始 GuildState 存檔
  await saveLoad.deleteSave(slot)

  const db = await openExtDB()
  return new Promise((resolve, reject) => {
    const tx  = db.transaction(STORE_NAME, 'readwrite')
    const req = tx.objectStore(STORE_NAME).delete(slot)
    req.onsuccess = () => resolve()
    req.onerror   = (e) => reject((e.target as IDBRequest).error)
  })
}

/**
 * 列出有擴充存檔的 slot 清單。
 */
export async function listAppSaves(): Promise<string[]> {
  const db = await openExtDB()
  return new Promise((resolve, reject) => {
    const req = db.transaction(STORE_NAME, 'readonly').objectStore(STORE_NAME).getAllKeys()
    req.onsuccess = (e) =>
      resolve((e.target as IDBRequest<IDBValidKey[]>).result as string[])
    req.onerror = (e) => reject((e.target as IDBRequest).error)
  })
}
