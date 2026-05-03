# Phase 2 Web Migration — 執行索引

_建立：2026-05-03 | 來源：phase1_html_demo → phase2_web/_

---

## 執行規則

- 實作：Sonnet subagent（最多 3 個並行）
- 審查：Claude Code 主體（Opus + xhigh），Medium 以上必審
- 禁用：Codex MCP / GPT MCP（本任務期間）
- 提交：當前 branch 直接 commit，不自動 push
- 各工項狀態：`未開始` / `進行中` / `驗證` / `完成`

---

## 一、不動檔案清單

> 這些檔案從 phase1_html_demo 直接複製後**非必要勿修改**。
> 若需改動，需在此標注原因。

| 檔案（phase2_web/ 路徑） | 來源 | 備注 |
|---|---|---|
| `index.html` | phase1_html_demo/index.html | 入口不變 |
| `tsconfig.json` | phase1_html_demo/tsconfig.json | TS 設定不變 |
| `netlify.toml` | phase1_html_demo/netlify.toml | Deploy 設定不變 |
| `package.json` | phase1_html_demo/package.json | 僅改 name 欄位 |
| `src/styles/main.css` | phase1_html_demo/src/styles/main.css | Stage 6 美術才動 |
| `src/data/bios.ts` | phase1_html_demo/src/data/bios.ts | Phase 2 D-01 延伸，不刪舊資料 |
| `src/data/missions.ts` | phase1_html_demo/src/data/missions.ts | 保持硬編碼，Phase 2 C-01 擴充 |
| `src/data/races.ts` | phase1_html_demo/src/data/races.ts | C-04 延伸，不刪舊資料 |
| `src/data/growth-traits.ts` | phase1_html_demo/src/data/growth-traits.ts | 直接沿用 |
| `src/ui/main-menu.ts` | phase1_html_demo/src/ui/main-menu.ts | 直接沿用 |
| `src/ui/cheat-menu.ts` | phase1_html_demo/src/ui/cheat-menu.ts | 直接沿用 |
| `src/systems/resource.ts` | phase1_html_demo/src/systems/resource.ts | 直接沿用（F-03 基礎） |
| `src/systems/tick.ts` | phase1_html_demo/src/systems/tick.ts | 直接沿用（F-02 基礎） |
| `src/systems/offline-return.ts` | phase1_html_demo/src/systems/offline-return.ts | Jam 簡化版離線推進，FT-11 等同 |

---

## 二、執行計畫

### Stage 0 — 基礎設施（序列）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 0.1 | 建立 `phase2_web/`、複製不動檔案 + retrofit 基底（11 直接複製 + 7 待改基底） | Small | 主體直接做 | 完成 |
| 0.2 | 建 `src/data/constants.ts`（25 個 SystemConstants，從 Phase 2 GDD 彙整） | Small | 主體直接做 | 完成 |
| 0.3 | 建 `src/core/events.ts`（輕量 EventBus，~50 行） | Small | Sonnet subagent | 完成 |
| 0.4 | retrofit `src/types/index.ts`（加 gender/bio/StaffInstance/DangerLevel/GuildLevel 等 Phase 2 新型別） | Small | Sonnet subagent | 完成 |

> 0.3 / 0.4 可並行（2 subagent）。0.1 / 0.2 先完成才能派出。

---

### Stage 1 — Phase 1 Retrofit（最多 3 並行）

#### Wave 1A（並行 3）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 1.1 | `dispatch.ts` 對齊 FT-02（rankDiff clamp、professionBonus、isScriptedDeath 短路） | Medium | Sonnet subagent | 完成 |
| 1.2 | `outcome.ts` 對齊 FT-04（5 結算分歧、condition trait 救活；styleTag jitter 跳過） | Medium | Sonnet subagent | 完成 |
| 1.3 | `adventurer.ts` 對齊 C-02（gender/bio 欄位、unique 角色排序） | Small | Sonnet subagent | 完成 |

#### Wave 1B（並行 2）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 1.4 | `save-load.ts` 升級 IndexedDB（取代 localStorage；單軌寫入；Backup rotation 跳過） | Medium | Sonnet subagent | 完成 |
| 1.5 | `guild.ts` → FT-06 Guild Core（Lv1~5 聲望門檻、可接難度上限、Game Over 流程） | Medium | Sonnet subagent | 完成 |

---

### Stage 2 — 新作 Service Wave 1（並行 3）

> 零相互依賴，全並行。依賴：Stage 0 完成。

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 2.1 | `src/systems/world-danger.ts`：C-06 World Danger System（~400 行：5 階全局壓力 + 池權重 + 債務上限推送） | Medium | Sonnet subagent | 完成 |
| 2.2 | `src/systems/recruitment.ts`：FT-01 Adventurer Recruitment（~500 行：自薦池 + 邀請 + 刷新邏輯） | Medium | Sonnet subagent | 完成 |
| 2.3 | `src/systems/npc-decision.ts`：FT-03 NPC Decision System（~400 行：willingness 公式獨立 service） | Medium | Sonnet subagent | 完成 |

---

### Stage 3 — 新作 Service Wave 2（並行 2）

> 依賴：Stage 1.5（FT-06）完成。

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 3.1 | `src/systems/building.ts`：FT-07 Guild Building System（~400 行：6 棟升級、雙軌閘門、effect API） | Medium | Sonnet subagent | 完成 |
| 3.2 | `src/systems/gold-flow.ts`：FT-05 Guild Gold Flow（~450 行：預收 / 結算 / 維護費 / 薪水觸發） | Medium | Sonnet subagent | 完成 |

---

### Stage 4 — FT-12 → FT-08 簡化版（序列）

> FT-12 先做（提供 StaffInstance 結構）；FT-08 依賴 FT-12.HireStaff。
> 簡化：三角色（米拉 501 / 譚恩 502 / 凱拉 503），無保底 / 無垃圾物品 / 無 OnLeave 三態。

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 4.1 | `src/systems/staff.ts`：FT-12 Staff System 簡化版（~350 行：單 Working 狀態、slot 指派、effect 聚合、薪水管線） | Medium | Sonnet subagent | 完成 |
| 4.2 | `src/systems/gacha.ts`：FT-08 Gacha System 簡化版（~300 行：單池抽卡、錄用呼叫 FT-12.HireStaff） | Medium | Sonnet subagent | 完成 |

---

### Stage 5 — UI Panels（最多 3 並行）

> 基底：沿用 `shell.ts` + `guild-hall-scene.ts`（Phase 1）。
> 依賴：Stages 1~4 對應 service 完成。

#### Wave 5A（並行 3）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 5.1 | 委託板 panel 升級（沿用 Phase 1 dispatch UI + successRate preview，對齊 P-02） | Medium | Sonnet subagent | 完成 |
| 5.2 | 名冊 panel 升級（沿用 + bio 顯示、unique 角色第一格，對齊 P-02） | Medium | Sonnet subagent | 完成 |
| 5.3 | 公會總覽 panel（公會等級、聲望、World Danger 顯示） | Medium | Sonnet subagent | 完成 |

#### Wave 5B（並行 3）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 5.4 | 招募 panel（FT-01：自薦池 + 邀請費用顯示） | Medium | Sonnet subagent | 完成 |
| 5.5 | 公會建設 panel（FT-07：6 棟建築升級，雙軌閘門顯示） | Medium | Sonnet subagent | 完成 |
| 5.6 | 面試 + 職員名冊 panel（FT-08 簡化抽卡 + FT-12 三角色管理） | Medium | Sonnet subagent | 完成 |

#### Wave 5C（並行 2）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 5.7 | 設定 panel + 結算彈窗 + 確認彈窗 | Medium | Sonnet subagent | 未開始 |
| 5.8 | 主選單 + 通知區 + Game Over 畫面 | Small | Sonnet subagent | 未開始 |

---

### Stage 6 — 整合 + 美術 + 除錯（序列）

| # | 工項 | 分級 | 執行者 | 狀態 |
|---|---|---|---|---|
| 6.1 | `main.ts` 整合所有 service + Bootstrap 順序協調 | Medium | Sonnet subagent | 未開始 |
| 6.2 | 美術 PNG 置入 `public/images/`（characters / ui / scene）+ CSS background-image | Small | 主體直接做 | 未開始 |
| 6.3 | 字型 + UI styling（USS → CSS 轉寫，極簡風）| Small | Sonnet subagent | 未開始 |
| 6.4 | 整合除錯（黃金路徑 + 邊緣案例）| Medium | 主體直接做 | 未開始 |
| 6.5 | Vite build & netlify deploy 確認 | Small | 主體直接做 | 未開始 |

---

## 三、里程碑摘要

| 里程碑      | 完成條件                                      | 預估完成      |
| -------- | ----------------------------------------- | --------- |
| M0 基礎就緒  | Stage 0 全完成，`phase2_web/` 可 `npm run dev` | Stage 0 後 |
| M1 核心可玩  | Stages 1~3 全完成，委託 → 派遣 → 結算 → 金流閉環        | Stage 3 後 |
| M2 完整功能  | Stage 4 完成，FT-08/12 職員系統可操作               | Stage 4 後 |
| M3 UI 完整 | Stage 5 全完成，所有 panel 可操作                  | Stage 5 後 |
| M4 可交付   | Stage 6 全完成，build 通過，美術置入，netlify 可訪問     | Stage 6 後 |

---

## 四、新增系統檔案對照

> 本節列出 phase2_web 新增的系統檔（phase1_html_demo 完全沒有的），方便追蹤。

| 檔案 | 對應系統 | 預計建立於 |
|---|---|---|
| `src/core/events.ts` | EventBus | Stage 0.3 |
| `src/data/constants.ts` | SystemConstants | Stage 0.2 |
| `src/data/traits.ts`（重寫）| C-03 Profession（取代 Phase 1）| Stage 0.2 |
| `src/systems/world-danger.ts` | C-06 World Danger | Stage 2.1 |
| `src/systems/recruitment.ts` | FT-01 Recruitment | Stage 2.2 |
| `src/systems/npc-decision.ts` | FT-03 NPC Decision | Stage 2.3 |
| `src/systems/building.ts` | FT-07 Guild Building | Stage 3.1 |
| `src/systems/gold-flow.ts` | FT-05 Guild Gold Flow | Stage 3.2 |
| `src/systems/staff.ts` | FT-12 Staff（簡化）| Stage 4.1 |
| `src/systems/gacha.ts` | FT-08 Gacha（簡化）| Stage 4.2 |
