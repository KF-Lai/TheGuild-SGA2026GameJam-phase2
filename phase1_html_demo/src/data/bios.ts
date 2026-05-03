/**
 * Adventurer Bio Generation System
 *
 * Generates unique biographical flavor text for each adventurer.
 * Implements: design/gdd/adventurer-bios.md
 *
 * Each adventurer receives a procedurally generated bio that reflects
 * their profession and rank, creating narrative depth without feeling
 * like database fill. Text is localization-ready: no idioms, reasonable length.
 *
 * Bio templates are structured by profession and rank tier (novice/veteran)
 * to ensure variety and thematic consistency.
 */

import type { ProfessionId, Rank } from '../types'

// ── Bio Template Types ───────────────────────────────────────────────────────

interface BioTemplate {
  /** Short label for debugging. */
  id: string
  /** Template string with {profession} / {rank} / {origin} placeholders. */
  template: string
}

// ── Bio Data Pools ───────────────────────────────────────────────────────────

/** Origins reflect diverse backgrounds (no culturally specific stereotypes). */
const ORIGINS = [
  '小村莊的邊界守衛',           // small village border guard
  '城鎮工匠學徒',                // city artisan apprentice
  '流浪傭兵',                    // wandering mercenary
  '廢墟探險家',                  // ruin explorer
  '商隊衛士',                    // caravan guard
  '神廟侍者',                    // temple servant
  '採礦工人',                    // mine worker
  '森林獵人',                    // forest hunter
  '農莊出身',                    // farm-born
  '無家可歸的流浪者',            // homeless wanderer
  '書籍抄寫員',                  // book scribe
  '市場小販',                    // market vendor
  '老兵',                        // veteran soldier
  '年輕懷夢者',                  // young dreamer
  '被放逐的學徒',                // exiled apprentice
]

/** Motivations for why they join the guild. */
const MOTIVATIONS = [
  '尋求名聲與冒險',              // seeking fame and adventure
  '贖還過去的過錯',              // atoning for past mistakes
  '家族榮譽的追求',              // pursuing family honor
  '逃離過去的生活',              // escaping a previous life
  '證明自己的價值',              // proving self-worth
  '累積財富',                    // accumulating wealth
  '渴望成為傳奇',                // yearning to become a legend
  '保護他人的夢想',              // dream of protecting others
  '尋找失落的力量',              // seeking lost power
  '償還債務',                    // repaying debts
  '為家族尋求改變',              // seeking change for family
  '逃避命運',                    // escaping fate
  '尋求真理',                    // seeking truth
  '響應使命的召喚',              // answering a calling
]

/** Personality quirks that make adventurers feel distinct. */
const QUIRKS = [
  '樂於講述自己的冒險故事',      // loves telling adventure stories
  '沉默寡言但值得信任',          // quiet but trustworthy
  '常咧嘴微笑',                  // grins often
  '總是提前準備',                // always prepared
  '容易被激怒但很快平復',        // quick to anger, quick to calm
  '深思熟慮每個決定',            // thoughtful about decisions
  '充滿幽默感',                  // full of humor
  '專注於細節',                  // focused on details
  '充滿好奇心',                  // perpetually curious
  '帶著古老的符號飾品',          // wears old talismans
  '談話時常引述古籍',            // often quotes old texts
  '保有死對手的紀念品',          // keeps trophies of rivals
  '寫日記記錄所有任務',          // writes logs of all missions
  '熟悉所有酒館的常客',          // knows tavern regulars everywhere
  '從不忘記虧欠他人恩情',        // never forgets a debt
]

// ── Bio Template Collections ─────────────────────────────────────────────────

/**
 * Novice rank templates (F, E) — emphasize uncertainty, hunger, raw potential.
 * These adventurers are new to the profession, full of hope or desperation.
 */
const NOVICE_TEMPLATES: BioTemplate[] = [
  {
    id: 'novice_01_hopeful',
    template: '來自{origin}的{profession}，懷抱成為傑出冒險者的夢想。剛開始接受訓練，{motivation}，同時{quirk}。',
  },
  {
    id: 'novice_02_desperate',
    template: '{origin}出身的{profession}已放棄舒適的生活。現在投身冒險以{motivation}。據說他們{quirk}。',
  },
  {
    id: 'novice_03_uncertain',
    template: '一位年輕的{profession}，來自{origin}，對自己的能力既充滿信心又疑慮。{motivation}成為了他們加入公會的動力，同時{quirk}。',
  },
  {
    id: 'novice_04_restless',
    template: '不甘平凡，這位{origin}來的{profession}渴望名聲和冒險。{motivation}驅使他們踏上危險的征途，{quirk}。',
  },
  {
    id: 'novice_05_marked',
    template: '命運似乎把這位{profession}推向了冒險的道路。{origin}背景下，他們{motivation}，現在已初出茅廬。{quirk}。',
  },
]

/**
 * Veteran rank templates (D, C, B, A, S) — emphasize experience, scars, wisdom.
 * These are seasoned adventurers with accomplishments and hardened by the road.
 */
const VETERAN_TEMPLATES: BioTemplate[] = [
  {
    id: 'veteran_01_hardened',
    template: '經歷無數場任務後，這位{origin}出身的{profession}已磨練成精銳。{motivation}依然是他們的驅動力。{quirk}。',
  },
  {
    id: 'veteran_02_legendary',
    template: '他們的名字在許多酒館流傳。曾經的{origin}，如今是令人畏懼的{profession}，{motivation}讓他們持續接受挑戰。{quirk}。',
  },
  {
    id: 'veteran_03_weary',
    template: '多年的冒險生涯留下了痕跡，但這位{profession}的決心未曾動搖。{motivation}在這位{origin}出身的老手身上依然閃耀。{quirk}。',
  },
  {
    id: 'veteran_04_mentor',
    template: '這位資深的{profession}曾{motivation}，如今他們用經驗指引後來者。{origin}背景下成長，{quirk}。',
  },
  {
    id: 'veteran_05_mysterious',
    template: '沒有人完全了解這位{profession}的過去。{origin}是故事的開端，但他們{motivation}卻指向更深的真相。據說{quirk}。',
  },
]

// ── Profession-specific descriptors ──────────────────────────────────────────

/**
 * Profession-specific flavor phrases to replace {profession} in templates.
 * Adds thematic variety without changing template structure.
 */
const PROFESSION_DESCRIPTORS: Record<ProfessionId, string[]> = {
  warrior: ['戰士', '劍手', '防線中堅', '武裝戰士'],
  mage: ['法師', '秘術使用者', '魔法學者', '奧術修行者'],
  ranger: ['遊俠', '獵人', '弓手', '野外巡者'],
  scout: ['斥侯', '偵查手', '匿蹤者', '前線眼睛'],
  guardian: ['守衛者', '護盾使用者', '堡壘', '保護者'],
  healer: ['療癒師', '聖職者', '醫療術士', '生命使者'],
  mercenary: ['傭兵', '雇傭戰士', '職業戰鬥家', '無情傭者'],
}

// ── Template Selection & Randomization ───────────────────────────────────────

/**
 * Selects a random item from an array.
 */
function pickRandom<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)]
}

/**
 * Determines if a rank is novice (F, E) or veteran (D+).
 */
function isNoviceRank(rank: Rank): boolean {
  return rank === 'F' || rank === 'E'
}

/**
 * Generates a unique biography for an adventurer.
 *
 * @param professionId - The adventurer's profession (warrior, mage, etc).
 * @param rank - The adventurer's rank (affects template selection).
 * @returns A complete biographical sentence in Traditional Chinese.
 *
 * Implementation:
 *   1. Select template based on rank (novice or veteran).
 *   2. Pick random origin, motivation, quirk, and profession variant.
 *   3. Substitute placeholders: {origin}, {motivation}, {quirk}, {profession}.
 *   4. Return final bio string.
 */
export function generateBio(professionId: ProfessionId, rank: Rank): string {
  // Select appropriate template pool by rank tier
  const templates = isNoviceRank(rank) ? NOVICE_TEMPLATES : VETERAN_TEMPLATES

  // Pick random entries from each pool
  const template = pickRandom(templates)
  const origin = pickRandom(ORIGINS)
  const motivation = pickRandom(MOTIVATIONS)
  const quirk = pickRandom(QUIRKS)
  const profession = pickRandom(PROFESSION_DESCRIPTORS[professionId])

  // Substitute all placeholders
  const bio = template.template
    .replace(/{origin}/g, origin)
    .replace(/{motivation}/g, motivation)
    .replace(/{quirk}/g, quirk)
    .replace(/{profession}/g, profession)

  return bio
}
