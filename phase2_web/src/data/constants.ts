// SystemConstants — The Guild Phase 2
// 來源：design/GDD/systems-index.md §資料表架構 SystemConstants.csv
// 所有系統常數集中於此；修改只需改這個檔案。

// ── 金幣 / 聲望 ─────────────────────────────────────────────────────────────
export const GOLD_INITIAL            = 200
export const GOLD_MAX                = 9_999_999
export const REPUTATION_MIN          = -100
export const REPUTATION_MAX          = 100

// ── 傭金 / 賠償 ─────────────────────────────────────────────────────────────
export const COMMISSION_RATE         = 0.20   // 成功傭金比例
export const PENALTY_RATE            = 0.10   // 失敗賠償比例

// ── NPC 決策 ─────────────────────────────────────────────────────────────────
export const DEATH_AVERSION          = 0.5    // NPC 死亡迴避係數
export const ACCEPTANCE_THRESHOLD   = 0.25   // NPC 接單意願門檻
export const WILLINGNESS_JITTER      = 0.10   // NPC 意願隨機波動

// ── 任務成功率修正 ────────────────────────────────────────────────────────────
export const STRONG_TYPE_BONUS       = 0.20   // 職業擅長加成
export const WEAK_TYPE_PENALTY       = 0.15   // 職業弱點懲罰

// ── 招募 ────────────────────────────────────────────────────────────────────
export const RECRUIT_POOL_SIZE       = 4      // 候選池每批人數
export const DAILY_FREE_REFRESH      = 1      // 每日免費手動刷新次數
export const REFRESH_COST            = 150    // 額外手動刷新費用

// ── 任務類型 ─────────────────────────────────────────────────────────────────
export const ESCORT_TYPE_ID          = 2      // 護送任務保留 typeID
export const ESCORT_DURATION_MULT_MIN = 3.0   // 護送時長倍率下限
export const ESCORT_DURATION_MULT_MAX = 5.0   // 護送時長倍率上限

// ── 時間 / 離線 ──────────────────────────────────────────────────────────────
export const DAILY_RESET_HOUR        = 0      // 每日重置時間（24hr）
export const OFFLINE_MAX_SECONDS     = 604800 // 離線上限（7天，超過截斷）
export const WOUNDED_RECOVERY_HOURS  = 6      // Wounded 恢復等待時間（小時）

// ── 陣營 ────────────────────────────────────────────────────────────────────
export const FACTION_NEUTRAL_ID      = 0      // neutral 陣營保留 ID
