# Mission Dispatch（任務派遣）

> **狀態**：已還原（對話 + 程式對齊）  
> **作者**：使用者 + Claude Code Game Studios  
> **最後更新**：2026-04-06  
> **支柱對應**：支柱 3 — 決策有真實後果  
> **Jam 版範圍**：MVP  

## 概覽

Mission Dispatch 是玩家與「委託循環」之間的主要操作層：產生委託板任務池、計算 NPC 接單意願、在成功接單時建立 `DispatchRecord` 並將冒險者標記為 `on_mission`。進行中任務以 `activeMissions: DispatchRecord[]` 保存（與 Outcome Resolution、Time/Tick、Save/Load 共用生命週期）。

`BASE_MISSION_DEATH_RATE` 的**設計真相**在對話紀錄中與 Outcome Resolution 連動；本檔與程式表一致：**F 2% → SSS 50%**（見下表）。

## 玩家幻想

玩家體驗是「推薦」而非強制：你挑出任務與人選，冒險者依風險與報酬**自己決定接或不接**。接單門檻與死亡率讓每次派遣都有賭注感，而非按鈕即成功。

## 詳細設計

### 核心規則

1. **委託板**：任務池筆數為 **`getCommissionDisplayLimit(buildingUnlocks)`** — 未建設「委託板」建築時 **6** 筆，建設後 **9** 筆（`main.ts` 傳入 `generateMissionPool` 之 `poolSize`）。先取可用靜態任務（受公會可承接難度上限、世界危險度篩選），不足則依 `World Danger` 權重抽模板任務補滿。若仍為空，fallback 生成一筆 F 討伐模板，避免白板。
2. **同時任務上限**：`activeMissions.length` 不得超過 `MAX_MISSIONS[guildLevel]`（定義於 Guild Core）。
3. **接單決策**（`tryDispatch`）：
  - 計算 `finalSuccessRate`、`finalDeathRate`（見公式節）。
  - `willingnessScore = finalSuccessRate − finalDeathRate × DEATH_AVERSION`。
  - `effectiveScore = willingnessScore + jitter`，`jitter ∈ [−ACCEPTANCE_JITTER, +ACCEPTANCE_JITTER]`（均勻隨機）。
  - 若 `effectiveScore < ACCEPTANCE_THRESHOLD` → 拒絕接單。
4. **接單成功**：建立 `DispatchRecord`（含 `preCollectedAmount = baseReward`）、`startTimestamp` / `endTimestamp`（`duration` 為**分鐘**，轉毫秒）。

### 狀態與轉換

- 冒險者：`idle` →（接單）→ `on_mission`，並設定 `currentMissionId`。
- `DispatchRecord` 留在 `activeMissions` 直至 Time/Tick 到期 → Outcome Resolution 結算後移除。

### 與其他系統的互動


| 系統                 | 關係                                                   |
| ------------------ | ---------------------------------------------------- |
| Mission Database   | 靜態任務、模板生成                                            |
| Adventurer Trait   | 成功率／死亡率職業修正                                          |
| Guild Core         | 可承接難度上限、同時任務數上限                                      |
| World Danger       | 任務池難度權重、靜態任務解鎖門檻                                     |
| Resource           | 讀取聲望（預留任務池接口）                                        |
| Outcome Resolution | 消耗 `DispatchRecord`、結算                               |
| Commission Flow    | 讀取 `activeMissions` 計算 `pendingGold`（於 `main.ts` 整合） |


## 公式

### 等級差

`rankDiff = clamp( RANK_INDEX(冒險者) − DIFFICULTY_INDEX(任務), −4, +3 )`

### 基礎成功率 `BASE_SUCCESS_RATE`（依 rankDiff）


| rankDiff | +3  | +2  | +1  | 0   | −1  | −2  | −3  |
| -------- | --- | --- | --- | --- | --- | --- | --- |
| 值        | 95% | 85% | 70% | 55% | 35% | 20% | 10% |


`finalSuccessRate = clamp(baseSuccess + successRateModifier(trait, missionType), 0, 1)`

### 死亡率修正 `RANK_DIFF_DEATH_MOD`


| rankDiff | +3   | +2   | +1  | 0   | −1  | −2   | −3   |
| -------- | ---- | ---- | --- | --- | --- | ---- | ---- |
| 值        | −20% | −10% | −5% | 0   | +5% | +15% | +25% |


### 任務基礎死亡率 `BASE_MISSION_DEATH_RATE`（對話紀錄最終定案 = 程式表）


| 難度    | F   | E   | D   | C   | B   | A   | S   | SS  | SSS |
| ----- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 基礎死亡率 | 2%  | 6%  | 10% | 13% | 18% | 25% | 30% | 38% | 50% |


`finalDeathRate = clamp(baseDeathRate + deathModifier(trait, missionType) + rankDiffDeathMod, 0, 1)`

### NPC 意願常數（程式）

- `ACCEPTANCE_THRESHOLD = 0.25`
- `DEATH_AVERSION = 0.5`
- `ACCEPTANCE_JITTER = 0.10`（加法）

### 範例演算（對話紀錄曾用於驗證）

B 難度基礎死亡率 18%、採集職業死亡率 −15%、rankDiff −1 修正 +5%：

`finalDeathRate = clamp(18% − 15% + 5%, 0, 1) = 8%`

`willingnessScore = 55% − 8% × 0.5 = 51%`（若 `finalSuccessRate=55%`）

## 極端情況

1. `**finalSuccessRate = 0`**：仍可能因死亡擲骰出現 DEATH／PYRRHIC（由 Outcome Resolution 定義）；Mission Dispatch 只負責傳遞率值。
2. **任務池為空**：強制生成一筆 F 討伐模板，避免無法遊玩。
3. **同時達任務上限**：`tryDispatch` 回傳拒絕（即使意願分數足夠）。
4. **靜態任務已完成**：不再出現於池中；由 `completedMissionIds` 控制。

## 依賴關係

- **上游**：Mission Database、Adventurer Trait、Guild Core、World Danger、（Resource 預留）
- **下游**：Outcome Resolution、Commission Flow（讀取進行中列表）、Save/Load

## 調整旋鈕


| 旋鈕                                                                      | 位置                    | 說明          |
| ----------------------------------------------------------------------- | --------------------- | ----------- |
| `poolSize`（`generateMissionPool` 參數）                                    | 由 `main` 傳入           | 6 或 9（`getCommissionDisplayLimit`） |
| `ACCEPTANCE_THRESHOLD`                                                  | 同上                    | NPC 接單底線    |
| `DEATH_AVERSION`                                                        | 同上                    | 死亡風險在意願中的權重 |
| `ACCEPTANCE_JITTER`                                                     | 同上                    | 意願隨機幅       |
| `BASE_SUCCESS_RATE` / `RANK_DIFF_DEATH_MOD` / `BASE_MISSION_DEATH_RATE` | 同上                    | 核心難度曲線      |


## 驗收標準

- 拒絕接單時不建立 `DispatchRecord`、不變更 `activeMissions`。
- 接單時 `preCollectedAmount === mission.baseReward`，且 `endTimestamp − startTimestamp` 符合任務 `duration`（分鐘）。
- 死亡率表與 Outcome Resolution GDD／對話紀錄一致（F2…SSS50）。
- 任務池在任意危險度與進度下至少有一筆可顯示任務（fallback）。

## 待解問題

- 聲望影響任務池（程式保留 `_reputation` 參數）尚未寫入完整規則，可於平衡迭代補齊。
- 委託來源已引入兩階段流程：系統產生的委託先進入「待審核池」，由玩家於辦公桌審核批准後才進入委託欄（`availableCommissions`）。本 GDD 的委託板生成邏輯需確認是否對齊此流程。
  > ⚠️ 參見 guild-hall-scene.md §委託流程