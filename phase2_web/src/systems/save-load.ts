/**
 * Save / Load System — The Guild (Phase 2)
 * 存儲後端：IndexedDB（DB: 'the-guild-db'，store: 'saves'）
 * 支援 3 個 slot：'slot0'（主要自動存檔）、'slot1'、'slot2'
 */

import type { GuildState, StaffInstance, BuildingState } from '../types'

// ---------------------------------------------------------------------------
// 常數
// ---------------------------------------------------------------------------

const DB_NAME    = 'the-guild-db'
const DB_VERSION = 1
const STORE_NAME = 'saves'
const DEFAULT_SLOT = 'slot0'

const CURRENT_SAVE_VERSION = 1

// ---------------------------------------------------------------------------
// 序列化型別
// ---------------------------------------------------------------------------

/**
 * IndexedDB 中實際儲存的結構。
 * completedMissionIds 以 string[] 儲存（JSON 無法序列化 Set）。
 */
interface SaveRecord {
  slot: string
  saveVersion: number
  savedAt: number
  state: Omit<GuildState, 'completedMissionIds'> & { completedMissionIds: string[] }
}

// ---------------------------------------------------------------------------
// IndexedDB 初始化
// ---------------------------------------------------------------------------

function openDB(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)

    request.onupgradeneeded = (event) => {
      const db = (event.target as IDBOpenDBRequest).result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'slot' })
      }
    }

    request.onsuccess = (event) => {
      resolve((event.target as IDBOpenDBRequest).result)
    }

    request.onerror = (event) => {
      console.error('[save-load] openDB 失敗', (event.target as IDBOpenDBRequest).error)
      reject((event.target as IDBOpenDBRequest).error)
    }
  })
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * 將 GuildState 序列化並寫入指定 slot。
 * completedMissionIds (Set) 轉為 Array 後儲存。
 */
export async function saveGame(state: GuildState, slot: string = DEFAULT_SLOT): Promise<void> {
  const db = await openDB()

  const record: SaveRecord = {
    slot,
    saveVersion: CURRENT_SAVE_VERSION,
    savedAt: Date.now(),
    state: {
      ...state,
      completedMissionIds: Array.from(state.completedMissionIds),
    },
  }

  return new Promise((resolve, reject) => {
    const tx      = db.transaction(STORE_NAME, 'readwrite')
    const store   = tx.objectStore(STORE_NAME)
    const request = store.put(record)

    request.onsuccess = () => resolve()
    request.onerror   = (event) => {
      console.error('[save-load] saveGame 寫入失敗', (event.target as IDBRequest).error)
      reject((event.target as IDBRequest).error)
    }
  })
}

/**
 * 從指定 slot 讀取並還原 GuildState。
 * completedMissionIds 從 Array 還原為 Set。
 * 找不到存檔時回傳 null。
 */
export async function loadGame(slot: string = DEFAULT_SLOT): Promise<GuildState | null> {
  const db = await openDB()

  return new Promise((resolve, reject) => {
    const tx      = db.transaction(STORE_NAME, 'readonly')
    const store   = tx.objectStore(STORE_NAME)
    const request = store.get(slot)

    request.onsuccess = (event) => {
      const record = (event.target as IDBRequest<SaveRecord | undefined>).result
      if (!record) {
        resolve(null)
        return
      }

      if (record.saveVersion > CURRENT_SAVE_VERSION) {
        console.warn('[save-load] loadGame: saveVersion 來自較新版本，拒絕載入', record.saveVersion)
        resolve(null)
        return
      }

      const rawState = record.state

      const guildState: GuildState = {
        ...rawState,
        completedMissionIds: new Set(rawState.completedMissionIds),
        staffRoster:  Array.isArray(rawState.staffRoster)  ? rawState.staffRoster  as StaffInstance[]  : [],
        buildings:    Array.isArray(rawState.buildings)    ? rawState.buildings    as BuildingState[]  : [],
        dangerLevel:  typeof rawState.dangerLevel === 'number' ? rawState.dangerLevel : 0,
      }

      resolve(guildState)
    }

    request.onerror = (event) => {
      console.error('[save-load] loadGame 讀取失敗', (event.target as IDBRequest).error)
      reject((event.target as IDBRequest).error)
    }
  })
}

/**
 * 刪除指定 slot 的存檔。
 */
export async function deleteSave(slot: string = DEFAULT_SLOT): Promise<void> {
  const db = await openDB()

  return new Promise((resolve, reject) => {
    const tx      = db.transaction(STORE_NAME, 'readwrite')
    const store   = tx.objectStore(STORE_NAME)
    const request = store.delete(slot)

    request.onsuccess = () => resolve()
    request.onerror   = (event) => {
      console.error('[save-load] deleteSave 失敗', (event.target as IDBRequest).error)
      reject((event.target as IDBRequest).error)
    }
  })
}

/**
 * 回傳所有有存檔的 slot 名稱列表。
 */
export async function listSaves(): Promise<string[]> {
  const db = await openDB()

  return new Promise((resolve, reject) => {
    const tx      = db.transaction(STORE_NAME, 'readonly')
    const store   = tx.objectStore(STORE_NAME)
    const request = store.getAllKeys()

    request.onsuccess = (event) => {
      const keys = (event.target as IDBRequest<IDBValidKey[]>).result
      resolve(keys as string[])
    }

    request.onerror = (event) => {
      console.error('[save-load] listSaves 失敗', (event.target as IDBRequest).error)
      reject((event.target as IDBRequest).error)
    }
  })
}
