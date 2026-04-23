# FT-05 跨系統審查報告

_日期：2026-04-22_
_審查者：Opus subagent（xhigh thinking）_
_範圍：截至 FT-05 為止所有已完成 GDD_
_焦點：系統衝突 + API 完整性（不審數值平衡）_

---

## 🔴 Critical（阻擋 FT-05 進入實作）

### C1. F-03 `AddGoldAllowBankruptcy(int)` API 尚未寫入 F-03 GDD
- **位置**：`[F-03] resource-management.md` §3.5 查詢 API、§4 公式
- **問題**：FT-05 結算 / 維護費 / 薪水扣款全走此 API；F-03 現有 `AddGold` 的 reject 規則在 `newGold < threshold` 時直接 return false，無法觸發 Warning/Bankrupt。缺失即無法實作 FT-05 核心金流
- **處理**：F-03 §3.2、§3.5、§4.1 新增 `AddGoldAllowBankruptcy(int amount)` — 語義：略過 `threshold` 下限、仍走 `UpdateBankruptcyWarning`、仍 clamp `GOLD_MAX`、仍廣播 `OnGoldChanged`；新增 AC；§6.2 下游列出 FT-05；寫入「單次 netDelta 不穿零線兩次」保證（FT-05 §5.5.3 依賴）

### C2. `OnCommissionAccepted` 事件無登記發布者
- **位置**：FT-05 §3.9.1 指定 FT-02/FT-03 發布；但 `[FT-02] mission-dispatch.md` §3.5、`[FT-03] npc-decision-system.md` §3.1/§3.3 都未提及
- **問題**：預收管線沒有實際發布者 → 結算時玩家金幣從未被「押金」過，FT-05 §4.3 範例 A/B/C 全部無法跑通
- **處理**：FT-02 §3.5 Dispatch 執行**前**發布 `OnCommissionAccepted(missionID, baseReward, source)`；挑 FT-02 為單一發布點，避免 FT-03 + FT-02 雙重發布；對應 §6.2 登記 FT-05 為下游

### C3. `OnMinuteTick` 事件在 F-02 未定義
- **位置**：`[FT-03] npc-decision-system.md` §3.3、§6.1 訂閱 `F-02 OnMinuteTick`；但 `[F-02] time-system.md` 事件表（行 182-185）僅有 `OnSecondTick` / `OnDailyReset` / `OnOfflineResolved`
- **問題**：FT-03 自主接單驅動源不存在，整個 `AutoPickupTick` 管線無法觸發（非 FT-05 直屬，但屬跨系統事件契約破損）
- **處理**：(a) F-02 新增 `OnMinuteTick`；或 (b) FT-03 改訂 `OnSecondTick` 內部以 `currentUTC % 60 == 0` 節流

---

## 🟡 Major（Sprint 0 前應修正）

### M1. FT-05 反向依賴登記尚未在對應 GDD 落地
- 位置：FT-05 §6.4 列 9 條 TODO；F-03 / FT-02 / FT-03 / FT-04 / FT-07 / FT-08 的 §6.2 至今都沒有列 FT-05 為下游
- 違反 `design-docs.md`「雙向依賴」規則
- 處理：Sprint 0 前至少補 F-03 / FT-02 / FT-03 / FT-04 四個已設計 GDD 的 §6.2

### M2. FT-04 聲望扣款與 FT-05 金流時序
- 位置：FT-04 §3.2 步驟 10 先呼叫 `F03.AddReputation`，步驟 11 才發 `OnMissionResolved`（FT-05 消費）。而 `AddReputation` 在 `currentGold < 0` 時立即 `TriggerBankruptcy`（F-03 §4.2）
- 問題：若結算前已在 Warning，FT-04 聲望變動提前觸發 `Bankrupt`，FT-05 結算快照 `bankruptcyStateBefore = Bankrupt`，P-03 通知會「破產→結算入帳」反序
- 處理：FT-04 §3.2 加註調整空間；或 FT-05 §3.8 狀態轉移對照表補 `Bankrupt → Bankrupt` 列

### M3. `C01.GetBaseReward` 簽名與 FT-02 流程不接
- 位置：C-01 §3（行 96）`GetBaseReward(string difficulty) : int`；FT-02 §3.5 Dispatch 流程未取 `difficulty`
- 處理：FT-02 §3.5 明列「發布 `OnCommissionAccepted` 前以 `C01.GetTemplate(...).difficulty` 取得 `baseReward`」；或 C-01 新增 `GetBaseRewardByMissionID(missionID)` 便利 API

### M4. FT-05 §5.3.4 與 FT-02 快照行為矛盾
- 位置：FT-05 §5.3.4「結算時 `outcome.baseReward` 與預收 `baseReward` 不一致」暗示 FT-02/FT-11 會中途改 `baseReward`；FT-02 §3.6 保證「ActiveMission 快照不變」
- 處理：FT-05 §5.3.4 改為「僅當上游設計允許動態調整 baseReward 時存在」，標記 Jam 階段不會發生

### M5. 預收 clamp 後的失敗路徑 UX 後果
- 位置：FT-05 §5.3.2 未點出「金幣爆表→預收被 clamp→任務失敗時純虧 penalty 金」的後果
- 處理：§5.3.2 補一句 UX 警告，或 P-02/P-03 層顯示

---

## 🟢 Minor（彙總）

- `systems-index.md` 依賴圖（行 93）FT-05 仍寫舊消費關係
- F-03 §6.2 行 307 仍寫「FT-05 Commission Flow」未更名
- C-05 §4.4 `ApplyConditionTraits(outcome, traitIDs, missionReward)` 三參數 vs FT-04 §3.2/§3.4 用兩參數 — 需對齊
- FT-01 §3.5 老手邀請用 `F03.AddGold(-cost)` 符合設計，可在 Edge Case 註明

---

## 行動順序（建議）

1. **進入實作前必修**：C1、C2（若要跑通 FT-05 任何範例）
2. **Sprint 0 前修**：C3、M1、M2、M3、M4
3. **有空時修**：M5 + 所有 Minor

關鍵檔案：
- `design/GDD/[F-03] resource-management.md`（C1）
- `design/GDD/[FT-02] mission-dispatch.md`（C2）
- `design/GDD/[F-02] time-system.md`（C3）
- `design/GDD/[FT-05] guild-gold-flow.md` §6.4（M1 TODO 清單）
