// Profession data — C-03 Profession System（Phase 2 內嵌版）
// 來源：design/GDD/【C-03】profession-system.md §3.1
// Phase 2 規則：職業僅影響成功率，不影響死亡率（死亡率由 C-04 種族 / C-05 特質決定）

import type { MissionType, ProfessionId } from '../types'
import { STRONG_TYPE_BONUS, WEAK_TYPE_PENALTY } from './constants'

export type { ProfessionId }

export interface ProfessionTrait {
  id: ProfessionId
  /** Phase 2 整數 professionID（C-03 §3.1 PK，1~7） */
  professionID: number
  name: string
  /** 擅長任務類型列表 */
  strongTypes: MissionType[]
  /** 弱點任務類型列表 */
  weakTypes: MissionType[]
  /** 各任務類型的成功率修正（小數，e.g. 0.20 = +20%）
   *  Phase 2：STRONG_TYPE_BONUS = +0.20、WEAK_TYPE_PENALTY = -0.15，其餘為 0 */
  successRateModifier: Record<MissionType, number>
}

export const PROFESSION_TRAITS: Record<ProfessionId, ProfessionTrait> = {
  warrior: {
    id: 'warrior', professionID: 1, name: '戰士',
    strongTypes: ['討伐'], weakTypes: ['調查'],
    successRateModifier: { 討伐: STRONG_TYPE_BONUS, 護送: 0, 採集: 0, 調查: -WEAK_TYPE_PENALTY },
  },
  mage: {
    id: 'mage', professionID: 2, name: '法師',
    strongTypes: ['調查'], weakTypes: ['採集'],
    successRateModifier: { 討伐: 0, 護送: 0, 採集: -WEAK_TYPE_PENALTY, 調查: STRONG_TYPE_BONUS },
  },
  ranger: {
    id: 'ranger', professionID: 3, name: '遊俠',
    strongTypes: ['採集'], weakTypes: ['護送'],
    successRateModifier: { 討伐: 0, 護送: -WEAK_TYPE_PENALTY, 採集: STRONG_TYPE_BONUS, 調查: 0 },
  },
  scout: {
    id: 'scout', professionID: 4, name: '斥侯',
    strongTypes: ['護送', '調查'], weakTypes: ['討伐'],
    successRateModifier: { 討伐: -WEAK_TYPE_PENALTY, 護送: STRONG_TYPE_BONUS, 採集: 0, 調查: STRONG_TYPE_BONUS },
  },
  guardian: {
    id: 'guardian', professionID: 5, name: '盾衛',
    strongTypes: ['護送'], weakTypes: ['調查'],
    successRateModifier: { 討伐: 0, 護送: STRONG_TYPE_BONUS, 採集: 0, 調查: -WEAK_TYPE_PENALTY },
  },
  healer: {
    id: 'healer', professionID: 6, name: '治癒師',
    strongTypes: ['護送'], weakTypes: ['討伐'],
    successRateModifier: { 討伐: -WEAK_TYPE_PENALTY, 護送: STRONG_TYPE_BONUS, 採集: 0, 調查: 0 },
  },
  mercenary: {
    id: 'mercenary', professionID: 7, name: '傭兵',
    strongTypes: [], weakTypes: [],
    successRateModifier: { 討伐: 0, 護送: 0, 採集: 0, 調查: 0 },
  },
}

export const PROFESSION_IDS: ProfessionId[] = Object.keys(PROFESSION_TRAITS) as ProfessionId[]

export function getRandomProfessionId(): ProfessionId {
  return PROFESSION_IDS[Math.floor(Math.random() * PROFESSION_IDS.length)]
}

export function getProfession(id: ProfessionId): ProfessionTrait {
  return PROFESSION_TRAITS[id]
}

export function isStrongType(id: ProfessionId, type: MissionType): boolean {
  return PROFESSION_TRAITS[id].strongTypes.includes(type)
}

export function isWeakType(id: ProfessionId, type: MissionType): boolean {
  return PROFESSION_TRAITS[id].weakTypes.includes(type)
}
