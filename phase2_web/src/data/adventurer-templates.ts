/**
 * 冒險者立繪 Template（jam 簡化版 AdventurerTemplate）
 *
 * 11 個具名冒險者模板，與 public/images/characters/adventurers/ 立繪一一對應。
 * createAdventurer 30% 機率走 template path 生成具名冒險者，70% 純隨機。
 *
 * 設計來源：使用者於 2026-05-03 提供的立繪 mapping（基於 PNG 視覺判斷）。
 */

import type { Rank, ProfessionId } from '../types'

/**
 * 冒險者 Template — 對應一張立繪。
 * 生成時隨機從 professionOptions 與 rankOptions 各抽一個。
 */
export interface AdventurerTemplate {
  /** 模板 ID（'adv_101' ~ 'adv_111'）*/
  templateId: string
  /** 顯示名稱（中文音譯）*/
  name: string
  /** 性別：0 男 / 1 女 */
  gender: 0 | 1
  /** 可生成的職業選項（依立繪視覺判斷，生成時隨機選一）*/
  professionOptions: ProfessionId[]
  /** 可生成的階級選項（依立繪推測角色實力，生成時隨機選一）*/
  rankOptions: Rank[]
  /** 立繪檔名前綴（不含 .png；對應 public/images/characters/adventurers/<portrait>.png）*/
  portrait: string
}

/**
 * 11 個具名冒險者模板。
 * 對應 public/images/characters/adventurers/adv_NNN_*.png 立繪。
 */
export const ADVENTURER_TEMPLATES: ReadonlyArray<AdventurerTemplate> = [
  {
    templateId: 'adv_101',
    name: '馬庫斯·柯爾',
    gender: 0,
    professionOptions: ['mercenary', 'warrior'],
    rankOptions: ['F', 'E', 'D'],
    portrait: 'adv_101_marcus_kurt',
  },
  {
    templateId: 'adv_102',
    name: '艾莉雅·維恩',
    gender: 1,
    professionOptions: ['mage'],
    rankOptions: ['F', 'E', 'D', 'C'],
    portrait: 'adv_102_elia_vienne',
  },
  {
    templateId: 'adv_103',
    name: '卡蒙·格雷',
    gender: 0,
    professionOptions: ['warrior', 'guardian'],
    rankOptions: ['F', 'E', 'D', 'C', 'B'],
    portrait: 'adv_103_kamon_grey',
  },
  {
    templateId: 'adv_104',
    name: '露西·費恩',
    gender: 1,
    professionOptions: ['ranger', 'scout'],
    rankOptions: ['F', 'E', 'D', 'C'],
    portrait: 'adv_104_lucy_fane',
  },
  {
    templateId: 'adv_105',
    name: '馬可·賽雅',
    gender: 0,
    professionOptions: ['mercenary', 'warrior'],
    rankOptions: ['C', 'B', 'A'],
    portrait: 'adv_105_marco_seya',
  },
  {
    templateId: 'adv_106',
    name: '蘭妮絲·柯恩',
    gender: 1,
    professionOptions: ['healer'],
    rankOptions: ['F', 'E', 'D', 'C', 'B', 'A', 'S'],
    portrait: 'adv_106_lannis_cohen',
  },
  {
    templateId: 'adv_107',
    name: '維克多·鄧恩',
    gender: 0,
    professionOptions: ['guardian'],
    rankOptions: ['F', 'E', 'D', 'C', 'B'],
    portrait: 'adv_107_victor_dunn',
  },
  {
    templateId: 'adv_108',
    name: '莉莉安娜·席爾',
    gender: 1,
    professionOptions: ['ranger', 'scout'],
    rankOptions: ['B', 'A', 'S'],
    portrait: 'adv_108_liliana_sill',
  },
  {
    templateId: 'adv_109',
    name: '米歇爾·艾許',
    gender: 1,
    professionOptions: ['mage'],
    rankOptions: ['B', 'A', 'S'],
    portrait: 'adv_109_michelle_ash',
  },
  {
    templateId: 'adv_110',
    name: '茉德·克雷',
    gender: 1,
    professionOptions: ['warrior'],
    rankOptions: ['B', 'A', 'S'],
    portrait: 'adv_110_maude_clay',
  },
  {
    templateId: 'adv_111',
    name: '伊凡·羅斯',
    gender: 0,
    professionOptions: ['mercenary'],
    rankOptions: ['C', 'B', 'A'],
    portrait: 'adv_111_evan_ross',
  },
]

// ---------------------------------------------------------------------------
// 輔助函式
// ---------------------------------------------------------------------------

/**
 * 隨機挑選一個模板。
 * 若指定 targetRank，僅從 rankOptions 包含該 rank 的模板中挑選；找不到回 null。
 */
export function pickRandomTemplate(targetRank?: Rank): AdventurerTemplate | null {
  const candidates = targetRank
    ? ADVENTURER_TEMPLATES.filter((t) => t.rankOptions.includes(targetRank))
    : ADVENTURER_TEMPLATES
  if (candidates.length === 0) return null
  return candidates[Math.floor(Math.random() * candidates.length)]
}

/** 從模板的 professionOptions 隨機挑一個職業。*/
export function pickTemplateProfession(tpl: AdventurerTemplate): ProfessionId {
  return tpl.professionOptions[Math.floor(Math.random() * tpl.professionOptions.length)]
}

/** 從模板的 rankOptions 隨機挑一個階級。*/
export function pickTemplateRank(tpl: AdventurerTemplate): Rank {
  return tpl.rankOptions[Math.floor(Math.random() * tpl.rankOptions.length)]
}
