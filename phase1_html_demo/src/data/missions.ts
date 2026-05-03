/**
 * Mission Database — 任務資料庫
 *
 * Implements: design/gdd/mission-database.md
 * Pool generation logic: design/gdd/mission-dispatch.md (Section: 核心規則 §1)
 *
 * This file is pure data + generation logic. It has no runtime side-effects and
 * does not import from any other src/systems file.
 */

import type { CommissionTier, Difficulty, Mission, MissionType, WorldDanger } from '../types'

// ---------------------------------------------------------------------------
// Config tables  (調整旋鈕 — see mission-database.md §調整旋鈕)
// ---------------------------------------------------------------------------

/** BASE_DURATION per difficulty (minutes) */
const BASE_DURATION: Record<Difficulty, number> = {
  F: 20,
  E: 45,
  D: 150,
  C: 270,
  B: 480,
  A: 840,
  S: 1200,
  SS: 1800,
  SSS: 3240,
}

/** BASE_REWARD per difficulty (gold) */
const BASE_REWARD: Record<Difficulty, number> = {
  F: 20,
  E: 50,
  D: 120,
  C: 300,
  B: 600,
  A: 1000,
  S: 2000,
  SS: 5000,
  SSS: 10000,
}

/** TYPE_MOD range [min, max] per MissionType */
const TYPE_MOD_RANGE: Record<MissionType, [number, number]> = {
  調查: [1.00, 1.10],
  採集: [1.15, 1.30],
  討伐: [1.10, 1.30],
  護送: [3.00, 5.00],
}

// ---------------------------------------------------------------------------
// Difficulty ordering helpers
// ---------------------------------------------------------------------------

const DIFFICULTY_ORDER: Difficulty[] = ['F', 'E', 'D', 'C', 'B', 'A', 'S', 'SS', 'SSS']

function difficultyIndex(d: Difficulty): number {
  return DIFFICULTY_ORDER.indexOf(d)
}

/** Maximum difficulty allowed to appear in the mission pool per WorldDanger level.
 *  Derived from world-danger-system.md §4 pool weight table:
 *  - E/D: S~SSS weight = 0%  → cap at A
 *  - C/B/A: S~SSS weight > 0% → cap at SSS
 */
const WORLD_DANGER_MAX_DIFFICULTY: Record<WorldDanger, Difficulty> = {
  E: 'A',
  D: 'A',
  C: 'SSS',
  B: 'SSS',
  A: 'SSS',
}

// ---------------------------------------------------------------------------
// WorldDanger pool weight table  (world-danger-system.md §4)
// Groups: 'FE', 'D', 'C', 'B', 'A', 'SSSS'
// ---------------------------------------------------------------------------

type DifficultyGroup = 'FE' | 'D' | 'C' | 'B' | 'A' | 'SSSS'

const POOL_WEIGHTS: Record<WorldDanger, Record<DifficultyGroup, number>> = {
  E:  { FE: 40, D: 30, C: 20, B: 8,  A: 2,  SSSS: 0  },
  D:  { FE: 20, D: 35, C: 25, B: 15, A: 5,  SSSS: 0  },
  C:  { FE: 10, D: 20, C: 35, B: 25, A: 8,  SSSS: 2  },
  B:  { FE: 5,  D: 10, C: 25, B: 35, A: 20, SSSS: 5  },
  A:  { FE: 0,  D: 5,  C: 15, B: 30, A: 35, SSSS: 15 },
}

function difficultyGroup(d: Difficulty): DifficultyGroup {
  if (d === 'F' || d === 'E') return 'FE'
  if (d === 'D') return 'D'
  if (d === 'C') return 'C'
  if (d === 'B') return 'B'
  if (d === 'A') return 'A'
  return 'SSSS' // S, SS, SSS
}

// ---------------------------------------------------------------------------
// Duration + reward calculation helpers
// ---------------------------------------------------------------------------

/** Seeded-style pseudo-random that avoids Math.random side-effects in tests.
 *  Returns a value in [min, max] using the provided random source.
 */
function randBetween(min: number, max: number, rng: () => number): number {
  return min + rng() * (max - min)
}

/** Calculate duration (minutes) for a given difficulty + type, using rng source. */
function calcDuration(difficulty: Difficulty, type: MissionType, rng: () => number): number {
  const base = BASE_DURATION[difficulty]
  const timeMod = randBetween(0.80, 1.20, rng)
  const [typeMin, typeMax] = TYPE_MOD_RANGE[type]
  const typeMod = randBetween(typeMin, typeMax, rng)
  return base * timeMod * typeMod
}

/** Round actualDuration up to nearest 30 minutes (estimatedDuration per GDD). */
function estimateDuration(actualMinutes: number): number {
  return Math.ceil(actualMinutes / 30) * 30
}

/** Calculate baseReward for a given difficulty, using rng source. */
function calcReward(difficulty: Difficulty, rng: () => number): number {
  return Math.round(BASE_REWARD[difficulty] * randBetween(0.85, 1.15, rng))
}

// ---------------------------------------------------------------------------
// ID counter for generated template missions
// ---------------------------------------------------------------------------

let _templateCounter = 0

function nextTemplateId(): string {
  _templateCounter += 1
  return `tmpl_${_templateCounter.toString().padStart(4, '0')}`
}

// ---------------------------------------------------------------------------
// Static mission data
// Implements: mission-database.md §任務生成類型 (generationType: 'static')
// All static missions: isTemplate = false, fixed id, Chinese name + description.
// Difficulty spread: F × 3, E × 3, D × 3, C × 3, B × 2, A × 2, S × 1
// Types: 討伐, 護送(D~A only), 採集, 調查
// ---------------------------------------------------------------------------

export const STATIC_MISSIONS: Omit<Mission, 'tier'>[] = [
  // ── F 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_001',
    name: '驅除村外老鼠窩',
    type: '討伐',
    difficulty: 'F',
    baseReward: 20,
    duration: estimateDuration(calcDuration('F', '討伐', Math.random)),
    description: '農民報告村外倉庫附近出現大批老鼠，請公會派人驅除，避免糧食損失。',
    isTemplate: false,
  },
  {
    id: 'static_002',
    name: '採集藥草：月見草',
    type: '採集',
    difficulty: 'F',
    baseReward: 18,
    duration: estimateDuration(calcDuration('F', '採集', Math.random)),
    description: '城鎮藥劑師委託採集郊外濕地的月見草，數量不多，適合初出茅廬的冒險者。',
    isTemplate: false,
  },
  {
    id: 'static_003',
    name: '調查廢棄磨坊的噪音',
    type: '調查',
    difficulty: 'F',
    baseReward: 22,
    duration: estimateDuration(calcDuration('F', '調查', Math.random)),
    description: '鄰近磨坊夜間傳出怪聲，居民不敢靠近。委託人推測只是流浪貓或野狗，請查明原因。',
    isTemplate: false,
  },

  // ── E 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_004',
    name: '清剿森林邊緣哥布林哨兵',
    type: '討伐',
    difficulty: 'E',
    baseReward: 50,
    duration: estimateDuration(calcDuration('E', '討伐', Math.random)),
    description: '一支小型哥布林巡邏隊在森林邊緣騷擾過路商人，城鎮守衛無暇應付，委託公會解決。',
    isTemplate: false,
  },
  {
    id: 'static_005',
    name: '採集礦山碎鐵礦石',
    type: '採集',
    difficulty: 'E',
    baseReward: 48,
    duration: estimateDuration(calcDuration('E', '採集', Math.random)),
    description: '鐵匠鋪需要大量碎鐵礦石作為輔料，礦山工人不足，委託公會冒險者協助採集。',
    isTemplate: false,
  },
  {
    id: 'static_006',
    name: '調查失蹤牧羊人的下落',
    type: '調查',
    difficulty: 'E',
    baseReward: 55,
    duration: estimateDuration(calcDuration('E', '調查', Math.random)),
    description: '一名牧羊人三日前出門放牧後失蹤，家人焦急尋找。委託人希望公會查明他的下落。',
    isTemplate: false,
  },

  // ── D 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_007',
    name: '剿滅石窟食人魔巢穴',
    type: '討伐',
    difficulty: 'D',
    baseReward: 120,
    duration: estimateDuration(calcDuration('D', '討伐', Math.random)),
    description: '東方山丘的石窟中棲息一群食人魔，近日頻繁出沒，已有旅人遭襲。請徹底清除巢穴。',
    isTemplate: false,
  },
  {
    id: 'static_008',
    name: '護送醫療補給至南方邊哨',
    type: '護送',
    difficulty: 'D',
    baseReward: 115,
    duration: estimateDuration(calcDuration('D', '護送', Math.random)),
    description: '南方邊境哨所爆發疫病，急需藥品補給。道路並不安全，需要武裝護送確保物資送達。',
    isTemplate: false,
  },
  {
    id: 'static_009',
    name: '調查古遺跡的異常魔力反應',
    type: '調查',
    difficulty: 'D',
    baseReward: 125,
    duration: estimateDuration(calcDuration('D', '調查', Math.random)),
    description: '魔法師公會偵測到城外古遺跡有異常魔力波動，委託調查源頭，並帶回詳細報告。',
    isTemplate: false,
  },

  // ── C 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_010',
    name: '圍剿盤據山道的土匪頭目',
    type: '討伐',
    difficulty: 'C',
    baseReward: 300,
    duration: estimateDuration(calcDuration('C', '討伐', Math.random)),
    description: '一幫土匪長期盤踞主要山道，劫奪商隊，頭目武功不弱。商會懸賞，要求剿滅並帶回印鑑為證。',
    isTemplate: false,
  },
  {
    id: 'static_011',
    name: '護送城主緊急密函至王都',
    type: '護送',
    difficulty: 'C',
    baseReward: 290,
    duration: estimateDuration(calcDuration('C', '護送', Math.random)),
    description: '城主有機密文件須緊急送達王都，路途遙遠且已有刺客行動的跡象，需要可靠的護衛同行。',
    isTemplate: false,
  },
  {
    id: 'static_012',
    name: '採集深山靈芝與珍稀草藥',
    type: '採集',
    difficulty: 'C',
    baseReward: 310,
    duration: estimateDuration(calcDuration('C', '採集', Math.random)),
    description: '宮廷御醫需要特定品種的深山靈芝，生長地點偏遠且有野獸出沒。委託者願意支付豐厚報酬。',
    isTemplate: false,
  },

  // ── B 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_013',
    name: '剿滅幻影龍的領地巡邏隊',
    type: '討伐',
    difficulty: 'B',
    baseReward: 600,
    duration: estimateDuration(calcDuration('B', '討伐', Math.random)),
    description: '一頭幻影龍在北方山脈建立領地，其巡邏範圍已威脅到礦山作業。委託消滅外圍巡邏個體以縮減其勢力範圍。',
    isTemplate: false,
  },
  {
    id: 'static_014',
    name: '護送亡命煉金術士離境',
    type: '護送',
    difficulty: 'B',
    baseReward: 590,
    duration: estimateDuration(calcDuration('B', '護送', Math.random)),
    description: '一名掌握機密配方的煉金術士被秘密組織追殺，必須在多個勢力的追捕下安全護送出境。',
    isTemplate: false,
  },

  // ── A 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_015',
    name: '討伐古龍：焰髯者薩雷克斯',
    type: '討伐',
    difficulty: 'A',
    baseReward: 1000,
    duration: estimateDuration(calcDuration('A', '討伐', Math.random)),
    description: '沉睡百年的古龍薩雷克斯已於火山口甦醒，開始焚燒周邊村落。此乃舉國委託，需最頂尖的冒險者應戰。',
    isTemplate: false,
  },
  {
    id: 'static_016',
    name: '調查消失的王國邊境哨站',
    type: '調查',
    difficulty: 'A',
    baseReward: 990,
    duration: estimateDuration(calcDuration('A', '調查', Math.random)),
    description: '三個邊境哨站相繼無聲無息地消失，連同駐守士兵。王室要求秘密調查，不得驚動鄰國。',
    isTemplate: false,
  },

  // ── F 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_018',
    name: '趕走闖入農舍的野豬',
    type: '討伐',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(calcDuration('F', '討伐', Math.random)),
    description: '一頭野豬闖入農舍，踩壞圍欄並驚嚇牲畜。農夫無力驅趕，懇請公會派人處理。',
    isTemplate: false,
  },
  {
    id: 'static_019',
    name: '採集溪邊的釣魚草',
    type: '採集',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(calcDuration('F', '採集', Math.random)),
    description: '漁夫需要一批釣魚草作為誘餌添加物，溪邊生長茂盛，只需注意腳下濕滑。',
    isTemplate: false,
  },
  {
    id: 'static_020',
    name: '查探神廟地窖的異臭',
    type: '調查',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(calcDuration('F', '調查', Math.random)),
    description: '鎮上神廟地窖近日散發異臭，祭司懷疑有動物在此死亡腐爛，委託公會查明並清理。',
    isTemplate: false,
  },
  {
    id: 'static_021',
    name: '幫孩子找回走失的寵物貓',
    type: '調查',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(calcDuration('F', '調查', Math.random)),
    description: '領主家的孩子走失了心愛的虎斑貓，據說跑向了西邊的倉庫區，請協助尋回。',
    isTemplate: false,
  },
  {
    id: 'static_022',
    name: '清除路邊巨型蜘蛛網',
    type: '討伐',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(calcDuration('F', '討伐', Math.random)),
    description: '通往市集的小路旁，一隻巨型蜘蛛在灌木叢中結網攔路，已嚇跑數名行商，請予以驅除。',
    isTemplate: false,
  },

  // ── E 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_023',
    name: '搗毀河岸哥布林的漁網陷阱',
    type: '討伐',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(calcDuration('E', '討伐', Math.random)),
    description: '哥布林在河岸設置陷阱竊取漁獲，漁民苦不堪言。委託清剿這批哥布林並破壞其設施。',
    isTemplate: false,
  },
  {
    id: 'static_024',
    name: '採集夜光蘑菇',
    type: '採集',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(calcDuration('E', '採集', Math.random)),
    description: '煉金師需要一批夜光蘑菇作為發光藥劑的原料，生長於黑暗林地，採集時需提防林中野獸。',
    isTemplate: false,
  },
  {
    id: 'static_025',
    name: '追查頻繁失竊的市集攤位',
    type: '調查',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(calcDuration('E', '調查', Math.random)),
    description: '市集連續多日發生竊案，守衛人手不足，攤商聯合委託公會暗中調查並找出竊賊。',
    isTemplate: false,
  },
  {
    id: 'static_026',
    name: '清除廢礦坑的毒蛇巢',
    type: '討伐',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(calcDuration('E', '討伐', Math.random)),
    description: '廢棄礦坑被一群毒蛇盤踞，礦主有意重新開採，委託公會事先清除以保障工人安全。',
    isTemplate: false,
  },
  {
    id: 'static_027',
    name: '調查村莊水井的污染源',
    type: '調查',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(calcDuration('E', '調查', Math.random)),
    description: '村民飲用水井後接連感到不適，懷疑水源遭到污染。委託公會溯源調查，找出原因並提交報告。',
    isTemplate: false,
  },

  // ── D 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_028',
    name: '剿滅窟居食屍鬼群',
    type: '討伐',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(calcDuration('D', '討伐', Math.random)),
    description: '舊墓地下的地道中出現食屍鬼群落，近來開始向地面蠶食。神廟祭司請求公會徹底清剿。',
    isTemplate: false,
  },
  {
    id: 'static_029',
    name: '護送流亡學者至北方修道院',
    type: '護送',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(calcDuration('D', '護送', Math.random)),
    description: '一名研究禁術的學者遭宗教裁判所通緝，需在未被發現的情況下護送至北方修道院尋求庇護。',
    isTemplate: false,
  },
  {
    id: 'static_030',
    name: '採集深林中的古樹樹脂',
    type: '採集',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(calcDuration('D', '採集', Math.random)),
    description: '製弓師傅需要稀有古樹的樹脂作為弓身防水塗料，古樹位於深林禁區，周圍有肉食野獸出沒。',
    isTemplate: false,
  },
  {
    id: 'static_031',
    name: '調查詭異的夜間鐘聲',
    type: '調查',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(calcDuration('D', '調查', Math.random)),
    description: '半個月來，荒野教堂每到子夜便傳出鐘聲，但教堂早已廢棄多年。領主懷疑有人在此秘密集會。',
    isTemplate: false,
  },
  {
    id: 'static_032',
    name: '護送受傷騎士返回駐地',
    type: '護送',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(calcDuration('D', '護送', Math.random)),
    description: '一名王國騎士在巡邏途中遭遇伏擊，身負重傷無法獨行。委託公會派人護送其安全返回城堡。',
    isTemplate: false,
  },

  // ── C 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_033',
    name: '討伐詛咒沼澤的巫妖',
    type: '討伐',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(calcDuration('C', '討伐', Math.random)),
    description: '東方沼澤近年詛咒蔓延，傳聞源頭是一名不死的巫妖。周邊村落農作物枯死，請公會出手剿滅。',
    isTemplate: false,
  },
  {
    id: 'static_034',
    name: '護送珠寶商穿越盜賊出沒的峽谷',
    type: '護送',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(calcDuration('C', '護送', Math.random)),
    description: '珠寶商攜帶大批貴重貨物需穿越峽谷，而該峽谷最近盜賊橫行，已有三支商隊遭劫。',
    isTemplate: false,
  },
  {
    id: 'static_035',
    name: '採集火山地帶的熔岩礦石',
    type: '採集',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(calcDuration('C', '採集', Math.random)),
    description: '鍛造師需要火山岩漿附近的特殊礦石打造耐高溫器具，採集地點危機四伏，非精銳不可。',
    isTemplate: false,
  },
  {
    id: 'static_036',
    name: '調查失蹤探險隊的最後位置',
    type: '調查',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(calcDuration('C', '調查', Math.random)),
    description: '三個月前一支精英探險隊進入禁忌森林後杳無音訊，家屬籌資委託公會查明其下落。',
    isTemplate: false,
  },
  {
    id: 'static_037',
    name: '剿滅盤踞古堡的血族伯爵',
    type: '討伐',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(calcDuration('C', '討伐', Math.random)),
    description: '廢棄古堡中居住著一名血族伯爵，近月頻繁於夜間外出吸血，鄰近村莊人心惶惶，請予以清除。',
    isTemplate: false,
  },

  // ── B 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_038',
    name: '討伐遠古樹靈：荒木之怒',
    type: '討伐',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(calcDuration('B', '討伐', Math.random)),
    description: '原始森林深處一株千年樹靈覺醒，對附近伐木業者發動攻擊。林業公會懸以重賞，請求剿滅。',
    isTemplate: false,
  },
  {
    id: 'static_039',
    name: '護送王室特使前往敵對領地談判',
    type: '護送',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(calcDuration('B', '護送', Math.random)),
    description: '王室特使須深入敵對領主的領地進行秘密談判，沿途遍佈間諜與刺客，護衛任務極度危險。',
    isTemplate: false,
  },
  {
    id: 'static_040',
    name: '採集深淵礦脈的星鐵隕石',
    type: '採集',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(calcDuration('B', '採集', Math.random)),
    description: '傳說中的星鐵隕石沉積於深淵礦脈，是打造神器的必要材料。礦脈充斥異界生物，九死一生。',
    isTemplate: false,
  },
  {
    id: 'static_041',
    name: '調查消失的聖騎士團第七分隊',
    type: '調查',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(calcDuration('B', '調查', Math.random)),
    description: '聖騎士團精英分隊奉命調查邪教總壇後失聯，教廷求助公會秘密調查，不得驚動邪教組織。',
    isTemplate: false,
  },
  {
    id: 'static_042',
    name: '剿滅佔領橋樑的巨型石偶',
    type: '討伐',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(calcDuration('B', '討伐', Math.random)),
    description: '王國最重要的貿易橋樑被一隻覺醒石偶佔領，任何試圖通行者皆遭攻擊，貿易已中斷數週。',
    isTemplate: false,
  },

  // ── A 級（追加）─────────────────────────────────────────────────────────────
  {
    id: 'static_043',
    name: '封印冰原地下城的混沌之眼',
    type: '調查',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(calcDuration('A', '調查', Math.random)),
    description: '冰原深處的地下城中出現一隻混沌之眼，其凝視足以摧毀心智。魔法師公會懇請協助調查並封印。',
    isTemplate: false,
  },
  {
    id: 'static_044',
    name: '護送最後的龍族蛋至聖地',
    type: '護送',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(calcDuration('A', '護送', Math.random)),
    description: '現存最後一枚龍族蛋必須護送至遠古聖地方可孵化，各方勢力皆對此蛋虎視眈眈，護衛任務生死攸關。',
    isTemplate: false,
  },
  {
    id: 'static_045',
    name: '討伐王都地下蔓延的屍王',
    type: '討伐',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(calcDuration('A', '討伐', Math.random)),
    description: '王都下水道深處隱藏著一名古老屍王，其死亡之氣正緩緩滲透地基，若不剿滅，王都將淪為不死城。',
    isTemplate: false,
  },
  {
    id: 'static_046',
    name: '採集絕境之塔頂層的天界晶石',
    type: '採集',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(calcDuration('A', '採集', Math.random)),
    description: '傳說中懸浮於雲端的「絕境之塔」頂層生長著天界晶石，製造神聖法器的唯一材料，取回者可獲重金。',
    isTemplate: false,
  },
  {
    id: 'static_047',
    name: '調查神殿祭司集體消失事件',
    type: '調查',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(calcDuration('A', '調查', Math.random)),
    description: '光明神殿的百名祭司於一夜之間無聲無息地消失，神像上留有不明血符。王室下令嚴查，懸賞最高等級。',
    isTemplate: false,
  },

  // ── S 級 ──────────────────────────────────────────────────────────────────
  {
    id: 'static_017',
    name: '封印裂縫：混沌之門',
    type: '調查',
    difficulty: 'S',
    baseReward: 2000,
    duration: estimateDuration(calcDuration('S', '調查', Math.random)),
    description: '世界裂縫已在荒原深處開啟，異界生物不斷湧出。任務要求深入裂縫核心，完成古老封印儀式——回來的人，將名留史冊。',
    isTemplate: false,
  },
]

// ---------------------------------------------------------------------------
// Mission templates
// Implements: mission-database.md §任務生成類型 (generationType: 'template')
// isTemplate = true; no fixed id (id assigned at generation time).
// Fields represent "typical" values — actual duration/reward computed at generation.
// ---------------------------------------------------------------------------

export const MISSION_TEMPLATES: Omit<Mission, 'id' | 'tier'>[] = [
  // ── F 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '驅除野外害蟲群',
    type: '討伐',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(BASE_DURATION['F'] * 1.10),
    description: '周邊田野出現成群害蟲，農戶無力應對，需派冒險者前往驅除。',
    isTemplate: true,
  },
  // ── F 採集 ──────────────────────────────────────────────────────────────
  {
    name: '採集河邊野花',
    type: '採集',
    difficulty: 'F',
    baseReward: BASE_REWARD['F'],
    duration: estimateDuration(BASE_DURATION['F'] * 1.20),
    description: '染坊需要大量野花作為染料原料，委託冒險者在附近河岸採集。',
    isTemplate: true,
  },
  // ── E 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '剿滅哥布林小隊',
    type: '討伐',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(BASE_DURATION['E'] * 1.15),
    description: '一支哥布林小隊在近郊活動，已造成數起財物損失，需盡快清除。',
    isTemplate: true,
  },
  // ── E 採集 ──────────────────────────────────────────────────────────────
  {
    name: '採集山泉附近礦石',
    type: '採集',
    difficulty: 'E',
    baseReward: BASE_REWARD['E'],
    duration: estimateDuration(BASE_DURATION['E'] * 1.25),
    description: '工匠需要特定礦石製作工具，指定採集點在山泉附近，有少量野獸出沒。',
    isTemplate: true,
  },
  // ── D 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '清剿山林狼群',
    type: '討伐',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(BASE_DURATION['D'] * 1.15),
    description: '山林中的狼群近日行動異常激進，已多次攻擊旅人，需要有組織的小隊前往清剿。',
    isTemplate: true,
  },
  // ── D 調查 ──────────────────────────────────────────────────────────────
  {
    name: '調查異常失蹤事件',
    type: '調查',
    difficulty: 'D',
    baseReward: BASE_REWARD['D'],
    duration: estimateDuration(BASE_DURATION['D'] * 1.05),
    description: '某區域接連發生失蹤事件，官方無暇詳查，委託公會派人調查原因並提交報告。',
    isTemplate: true,
  },
  // ── C 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '討伐地下城精英魔物',
    type: '討伐',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(BASE_DURATION['C'] * 1.20),
    description: '地下城深層出現精英魔物，對進入的探索隊造成重大損失，需要實力強勁的隊伍清剿。',
    isTemplate: true,
  },
  // ── C 採集 ──────────────────────────────────────────────────────────────
  {
    name: '採集危險地帶特殊材料',
    type: '採集',
    difficulty: 'C',
    baseReward: BASE_REWARD['C'],
    duration: estimateDuration(BASE_DURATION['C'] * 1.25),
    description: '煉金師需要的特殊材料只生長於危險地帶，採集過程中需對抗強力魔物的干擾。',
    isTemplate: true,
  },
  // ── B 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '討伐覺醒魔物領袖',
    type: '討伐',
    difficulty: 'B',
    baseReward: BASE_REWARD['B'],
    duration: estimateDuration(BASE_DURATION['B'] * 1.20),
    description: '一隻異常覺醒的魔物開始統率周邊同類，形成威脅，需在其勢力進一步壯大前擊殺。',
    isTemplate: true,
  },
  // ── A 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '討伐傳說級野獸',
    type: '討伐',
    difficulty: 'A',
    baseReward: BASE_REWARD['A'],
    duration: estimateDuration(BASE_DURATION['A'] * 1.15),
    description: '一頭傳說中的古代野獸現身於人跡罕至的荒野，威脅到整個地區的平衡，此乃頂級委託。',
    isTemplate: true,
  },
  // ── S 討伐 ──────────────────────────────────────────────────────────────
  {
    name: '討伐異界入侵先鋒',
    type: '討伐',
    difficulty: 'S',
    baseReward: BASE_REWARD['S'],
    duration: estimateDuration(BASE_DURATION['S'] * 1.20),
    description: '來自異界的先鋒部隊已建立橋頭堡，若不立刻摧毀，後果將無法估量。僅接受最頂尖冒險者。',
    isTemplate: true,
  },
  // ── SS 討伐 ─────────────────────────────────────────────────────────────
  {
    name: '討伐古代神格魔物',
    type: '討伐',
    difficulty: 'SS',
    baseReward: BASE_REWARD['SS'],
    duration: estimateDuration(BASE_DURATION['SS'] * 1.20),
    description: '一個擁有神格的古代存在從封印中甦醒，普通的武裝力量已無法抗衡，需要英雄中的英雄。',
    isTemplate: true,
  },
  // ── SSS 討伐 ────────────────────────────────────────────────────────────
  {
    name: '終結末世災厄：黑暗意志',
    type: '討伐',
    difficulty: 'SSS',
    baseReward: BASE_REWARD['SSS'],
    duration: estimateDuration(BASE_DURATION['SSS'] * 1.20),
    description: '世界末日正在降臨。一個自稱「黑暗意志」的存在宣告終結一切秩序。這或許是公會接下的最後一份委託。',
    isTemplate: true,
  },
]

// ---------------------------------------------------------------------------
// Weighted random selection helper
// ---------------------------------------------------------------------------

function weightedRandom<T>(items: T[], weights: number[], rng: () => number): T {
  const total = weights.reduce((a, b) => a + b, 0)
  let r = rng() * total
  for (let i = 0; i < items.length; i++) {
    r -= weights[i]
    if (r <= 0) return items[i]
  }
  return items[items.length - 1]
}

// ---------------------------------------------------------------------------
// Commission Tier config
// Implements: design/gdd-commission-tiers.md
// ---------------------------------------------------------------------------

/** Minimum guild reputation required to unlock each tier. */
const TIER_REP_THRESHOLD: Record<CommissionTier, number> = {
  common: 0,
  silver: 50,
  gold: 120,
}

/** Reward multiplier applied to baseReward at generation time per tier. */
const TIER_REWARD_MULTIPLIER: Record<CommissionTier, number> = {
  common: 1.0,
  silver: 1.5,
  gold: 2.0,
}

/** Number of slots generated per tier per refresh. */
const TIER_SLOT_COUNT: Record<CommissionTier, number> = {
  common: 4,
  silver: 2,
  gold: 1,
}

// ---------------------------------------------------------------------------
// generateMissionPool
// Implements: mission-dispatch.md §核心規則 §1
//             design/gdd-commission-tiers.md
//
// Steps (per tier):
//  1. Collect available static missions (not completed, not active, difficulty <= WorldDanger cap)
//  2. If still below slot count, draw from templates weighted by WorldDanger pool weights
//  3. If pool is still empty after both steps, fallback to one F 討伐 template (common only)
// Tier unlock: silver requires guildRep >= 50, gold requires guildRep >= 120.
// ---------------------------------------------------------------------------

/** Helper: fill `slotCount` missions into `out` for a given tier.
 *  Mutates `usedStaticIds` to prevent the same static mission appearing in multiple tiers. */
function fillTierSlots(
  tier: CommissionTier,
  slotCount: number,
  completedIds: Set<string>,
  activeMissionIds: Set<string>,
  usedStaticIds: Set<string>,
  worldDanger: WorldDanger,
  rng: () => number,
  out: Mission[],
): void {
  const maxDiff = WORLD_DANGER_MAX_DIFFICULTY[worldDanger]
  const maxDiffIndex = difficultyIndex(maxDiff)
  const multiplier = TIER_REWARD_MULTIPLIER[tier]

  let filled = 0

  // Step 1: available static missions
  for (const m of STATIC_MISSIONS) {
    if (filled >= slotCount) break
    if (completedIds.has(m.id)) continue
    if (activeMissionIds.has(m.id)) continue
    if (usedStaticIds.has(m.id)) continue
    if (difficultyIndex(m.difficulty) > maxDiffIndex) continue
    usedStaticIds.add(m.id)
    out.push({ ...m, tier, baseReward: Math.round(m.baseReward * multiplier) })
    filled++
  }

  // Step 2: fill remainder with template missions weighted by WorldDanger
  if (filled < slotCount) {
    const weights = POOL_WEIGHTS[worldDanger]

    const eligibleTemplates = MISSION_TEMPLATES.filter(t => {
      if (difficultyIndex(t.difficulty) > maxDiffIndex) return false
      if (t.type === '護送') {
        const di = difficultyIndex(t.difficulty)
        if (di < difficultyIndex('D') || di > difficultyIndex('A')) return false
      }
      return true
    })

    const templateWeights = eligibleTemplates.map(t => weights[difficultyGroup(t.difficulty)])
    const needed = slotCount - filled

    for (let i = 0; i < needed; i++) {
      if (eligibleTemplates.length === 0) break
      const template = weightedRandom(eligibleTemplates, templateWeights, rng)
      const baseReward = Math.round(calcReward(template.difficulty, rng) * multiplier)
      const generated: Mission = {
        ...template,
        id: nextTemplateId(),
        tier,
        duration: estimateDuration(calcDuration(template.difficulty, template.type, rng)),
        baseReward,
      }
      out.push(generated)
      filled++
    }
  }
}

export function generateMissionPool(
  poolSize: number,
  completedIds: Set<string>,
  activeMissionIds: Set<string>,
  worldDanger: WorldDanger,
  guildRep: number = 0,
): Mission[] {
  const pool: Mission[] = []
  const rng = Math.random
  const usedStaticIds = new Set<string>()

  // Determine which tiers are unlocked
  const tiersToGenerate: CommissionTier[] = ['common']
  if (guildRep >= TIER_REP_THRESHOLD.silver) tiersToGenerate.push('silver')
  if (guildRep >= TIER_REP_THRESHOLD.gold) tiersToGenerate.push('gold')

  // Fill slots per unlocked tier
  for (const tier of tiersToGenerate) {
    fillTierSlots(
      tier,
      TIER_SLOT_COUNT[tier],
      completedIds,
      activeMissionIds,
      usedStaticIds,
      worldDanger,
      rng,
      pool,
    )
  }

  // Note: poolSize parameter is kept for API compatibility but tier counts now
  // determine actual pool size (4 common + 2 silver + 1 gold max = 7 missions).
  // Legacy callers passing poolSize=6 are unaffected — generated count may differ slightly.
  void poolSize

  // Fallback — ensure pool is never empty (mission-dispatch.md §極端情況 §2)
  if (pool.length === 0) {
    const fallbackTemplate = MISSION_TEMPLATES.find(t => t.difficulty === 'F' && t.type === '討伐')
      ?? MISSION_TEMPLATES[0]
    pool.push({
      ...fallbackTemplate,
      id: nextTemplateId(),
      tier: 'common',
      duration: estimateDuration(calcDuration('F', '討伐', rng)),
      baseReward: calcReward('F', rng),
    })
  }

  return pool
}
