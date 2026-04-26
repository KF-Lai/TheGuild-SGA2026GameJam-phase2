# Gacha System 系統設計文件

_建立時間：2026-04-25_
_狀態：草稿（從原職員系統拆分為純 gacha，待 design-review）_
_最後更新：2026-04-26_
_系統 ID：FT-08_

**🔖 拆分來源**：本系統由原 FT-08「職員系統」於 2026-04-26 拆分而來。職員實例管理、effect 聚合、薪水管線等職責移至 `[FT-12] staff-system.md`；FT-08 改名為「面試系統（Gacha）」聚焦面試 gacha 機制。兩系統透過 `FT-12.HireStaff(candidateCard)` 同步 API 交棒，FT-08 不持有 StaffInstance、FT-12 不感知 gacha 細節。

---

## 設計來源（Design Inputs）

本 GDD 基於以下決策來源撰寫：

- 拆分自原 `[FT-08]` 職員系統 GDD（2026-04-26 拆分批次）
- `DevLogs/DevLog_260423-2.md` — FT-08 Q1~Q6 + D1~D7 決策摘要
- `design/gdd/game-concept.md` §「公會職員系統」— 面試系統原始概念
- 依賴 GDD 約束：
  - `[FT-12] staff-system.md` — 透過 `HireStaff(candidate)` 同步 API 交棒候選卡；查詢 `GetRecruitRefreshReductionSec()` 影響面試自動刷新間隔
  - `[FT-07] guild-building-system.md` — `IsStaffSystemUnlocked()`（職員休息室 L1 解鎖）+ 職員休息室等級驅動 auto refresh interval
  - `[FT-06] guild-core.md` — `GetCurrentLevel()`（StaffGachaPoolTable.minGuildLevel 過濾、StaffRefreshCostTable[guildLevel] 索引）
  - `[F-01] data-manager.md` — `Get<StaffGachaPoolData>` / `Get<StaffRefreshCostData>` / `Get<StaffRarityProbData>` / `Get<TrashItemData>`（4 個 FT-08 owner CSV）；`Get<StaffData>`（驗證 candidate.staffID 用，FT-12 owner）；`StaffTuning.csv` 系統常數（FT-08 / FT-12 共用 owner）
  - `[FT-09] faction-story-system.md` — `StaffGachaPoolTable` 5 個預留閘欄位 Post-Jam 啟用
- 全局規則：`feedback_time_units`（時間單位僅秒/小時）、`feedback_data_driven`（一切由 CSV 驅動）

> **FT-09 runtime 依賴聲明（Jam 版無）**：Jam 版 FT-08 與 FT-09 Faction Story System **無任何 runtime 依賴**。`StaffGachaPoolTable` 的 5 個預留閘欄位（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`）為 Post-Jam 啟用 FT-09 → FT-08 陣營傾向消費而保留的前向相容 schema；Jam 版 DataManager 載入時強制驗證所有預留閘為預設值，違規拋 `StaffGachaPoolTableValidationException`。Post-Jam 啟用路徑：同步解 FT-08 §3.2 驗證邏輯 + FT-09 §6.2 下游登記。

---

## 1. 概要（Overview）

**FT-08 Gacha System（面試系統）是公會職員的面試入職介面**：透過 gacha 機制 roll 出候選卡（`CandidateCard`），玩家以「錄用 / 不錄用 / 保留」三擇一決定每張候選的命運；錄用後透過 `FT-12.HireStaff(candidate)` 同步 API 交棒至職員系統建立 `StaffInstance`。本系統不管理已錄用職員（屬 FT-12），僅負責 **roll → 候選卡 → 玩家決策 → 交棒** 的玩家入口。系統涵蓋三大職責：

### A. 多池架構與閘控（Pool Architecture）

- **Jam 版 2 池**：A 池（常駐通用 Lv1~Lv5）、B 池（中高等 Lv3~Lv5）
- **主閘**：`[minGuildLevel, maxGuildLevel]` 連續區間（DataManager 驗證 `min ≤ max`）
- **預留閘**：5 個 FT-09 預留欄位 Post-Jam 啟用
- **池切換副作用**：`currentCandidates` 全部覆蓋；`reservedCandidates` 跨池存活
- 詳見 §3.2 / §3.3

### B. 刷新模型（Refresh Model）

- **自動刷新**：免費、間隔依**職員休息室等級**（L1=86400s → L5=21600s 階梯）；計時器全域單一，切池不重置；離線補刷 1 次；計入保底 counter
- **手動刷新**：付費（`StaffRefreshCostTable[guildLevel]`）、無 cooldown、計入保底 counter
- 兩者獨立並存；詳見 §3.3

### C. 候選卡決策（Candidate Decision）

- **稀有度 roll**：1★40% / 2★25% / 3★15% / 4★15% / 5★5%（動態歸一化空層，§4.1.5）
- **層內職員 roll**：依 `staffWeights` 加權（§4.1.6）
- **保底機制**：`pityCounter` 跨池跨 session 累積；命中觸發強制最高稀有度
- **垃圾物品**：1★ 層 30% 機率變為 trash（與 filler staff 區別）
- **三擇一**：錄用（呼叫 `FT-12.HireStaff`）/ 不錄用 / 保留（佔 `reservedCandidates` slot 直至時限到）
- 詳見 §3.3 / §3.4 / §3.5

### 啟停閘

`FT-07.IsStaffSystemUnlocked()`（職員休息室 L1）為整體啟停閘 — 未解鎖時靜默降級（§3.6）：所有 `Try*` API 回傳 `STAFF_SYSTEM_LOCKED`、`Get*` API 回傳預設值、`OnStaff*` gacha 事件不發布。

### Jam 範疇

- 多池架構 / 自動 + 手動刷新 / 三擇一 / 稀有度 + 保底 / trash items：全部 Jam 內實作
- 透過 `FT-12.HireStaff` 交棒錄用候選：Jam 內實作
- ISaveable 持久化：StaffPlayerState（pityCounter / candidates / reservedCandidates 等）

### 不在 Jam 範疇

- 第 3+ 池（schema 已預留，Post-Jam）
- FT-09 預留閘 runtime 啟用（Jam 版強制空值，DataManager 驗證）
- 職員圖鑑 / 圖像介紹（Post-Jam）
- 線上 auto refresh tick（Jam 版 auto 補刷僅於 Boot 一次性執行）

### FT-08 / FT-12 邊界

**錄用流程交棒**：
1. 玩家在 FT-08 面試介面點「錄用」候選卡
2. FT-08 `TryRecruit` 內部呼叫 `FT-12.HireStaff(CandidateCard candidate) → HireResult`
3. FT-12 驗證 → 建立 `StaffInstance`（new instanceID）→ 加入 roster → 發布 `OnStaffHired` → 回 `OK + instanceID`
4. FT-08 收到 OK → 從 `currentCandidates` 移除該候選

**單向耦合**：FT-08 → FT-12 透過同步 API；FT-12 不感知 gacha 細節。

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-08 在 Jam 版聚焦兩個核心情緒，對齊 game-concept「探索與發現」與「有重量的決策」：

**翻面試卡的期待感**

- 玩家點「下一個」按鈕 → 看到 5 張候選依序翻面（P-02 動畫）
- 5★ 出現的瞬間 — 機率本來只有 5%，玩家屏住呼吸
- 「保留 vs 錄用 vs 不錄用」三擇一前的猶豫感

**保底是希望、Trash 是現實**

- pityCounter 累積到上限的安心感（「下次刷新一定有 5★」）
- 1★ 層偶爾翻到「咖啡杯」「履歷紙團」等 trash item — 不是職員、佔用 slot — 玩家會笑出來，也提醒「現實沒那麼浪漫」
- 動態歸一化讓「全是 1★」的場面消失，但保底讓「永遠抽不到 5★」也消失

### 玩家幻想敘事

> 「我終於蓋好職員休息室，迫不及待點開面試介面 — 5 張卡片整齊排列。第一張是 1★ 跑腿小弟、第二張是 2★ 櫃台、第三張竟然翻出咖啡杯（Trash！）我笑了一下，第四張是 3★ 會計、第五張是 4★ — 不是 5★ 但已經很滿足。我先保留 4★ 會計，錄用 3★ 會計入職，把其他三張不錄用。pityCounter 已經 +5 了，下次刷新就會觸發保底。我點擊『刷新』，記下這個期待。」

### 設計原則（FT-08 如何強化幻想）

1. **gacha 是入口、不是養成**：FT-08 只負責 roll & 三擇一 & 交棒，不管後續職員怎麼用 — 純粹的「面試體驗」
2. **保底是承諾不是免責**：pityCounter 跨 session、跨池累積，玩家不需擔心「重啟遊戲就重置」
3. **Trash 是世界觀紋理**：1★ 層的 30% trash 機率讓「翻牌瞬間」永遠有不確定性；不是純數值挫折，是世界觀「面試會收到奇怪履歷」的真實感
4. **保留機制不浪費**：有時間限的保留區（`StaffGachaPoolTable.reserveTimeLimitSec`）讓玩家「我想等下個刷新再決定」成為合法策略

---

## 3. 詳細規則（Detailed Rules）

> 本章採 **§3.1 → §3.7** 七子節結構：Player State / CandidateCard schema → Pool schema → 刷新流程 → 保底機制 → 垃圾物品 → 系統降級 → 事件契約。

### 3.1 Player State & Candidate Card Schemas

本節定義 FT-08 的 player-level 狀態容器 `StaffPlayerState` 與面試候選卡資料結構 `CandidateCard`，用於保底 counter / 刷新計時器 / 候選保留等跨 refresh 狀態。`StaffInstance.instanceID` 分配權責歸 FT-12（見下方 `nextInstanceID` 不在本系統 註記）。

**`StaffPlayerState`**（全部持久化，FT-10 序列化）：

```
StaffPlayerState {
    // 保底 counter（跨池累積、公會升級不 reset，見 §3.4）
    pityCounter                  : int                   // 初始 0；每張候選 +1

    // 刷新計時器（全域單一；切池不重置；刷當前選中池）
    lastAutoRefreshTimestamp     : long                  // 最近一次自動刷新的 UTC 時間戳

    // 當前面試畫面（固定 N 大小，N 依 guildLevel）
    currentPoolID                : int                   // 玩家當前切到哪個池（需存檔記憶）
    currentCandidates            : List<CandidateCard>   // 當前池的面試欄內容；空 slot 以 placeholder card 表示（見下方規範）

    // 跨池保留區（空間獨立於 currentCandidates）
    reservedCandidates           : List<CandidateCard>   // 被保留的候選（含 poolID 區分）
}
```

> **`nextInstanceID` 不在本系統**：`StaffInstance.instanceID` 由 FT-12 在 `HireStaff` 內部分配（FT-12 §3.3.2 step 3 + §6.7 序列化 `_nextInstanceID`）。FT-08 不持有此欄位、不存於 SaveData。

> **空 slot 表示法（避免 JsonUtility null 陷阱）**：`currentCandidates` 為**固定 N 大小 List**，**不使用 null**。空 slot（保留期間原 slot 暫空）以 placeholder `CandidateCard` 表示：`staffID = 0`、`trashItemID = 0`、`rolledRarity = 0`、其餘欄位預設值。DataManager 驗證規則 `(staffID > 0) XOR (trashItemID > 0)` **對 placeholder 不適用**（兩者皆 0 為合法空 slot 標記）;runtime 偵測 placeholder 用 helper：`IsEmptySlot(card) = (card.staffID == 0 && card.trashItemID == 0)`。

**設計註記**

- **單一全域 `lastAutoRefreshTimestamp`**：自動刷新計時器不區分池，到點時刷新 `currentPoolID` 指向的池；切池時計時器延續、不重置（玩家無法靠切池偷跑刷新節奏）
- **`currentCandidates` 僅存當前池**：切池時被覆蓋為新池 roll 結果；前池未保留的候選一律丟失（設計規則：保留區是跨池保留的唯一途徑）
- **無 `lastManualRefreshTimestamp`**：手動刷新無 cooldown、不需計時器
- **離線補刷不追蹤**：離線回來時由 `lastAutoRefreshTimestamp` 反推已過幾個間隔，clamp 補 1 次（§3.3 計算時處理）

---

**`CandidateCard`**（全部持久化，FT-10 序列化）：

```
CandidateCard {
    // 卡片識別
    poolID                       : int                   // 所屬池（跨池保留區分用）
    slotIndex                    : int                   // 面試欄位置 0..N-1（N 依 guildLevel）

    // 內容（staffID 與 trashItemID 互斥，恰好一個為 0）
    staffID                      : int                   // 職員 ID；0 = 此卡為 trash item
    trashItemID                  : int                   // trash item ID；0 = 此卡為職員
    rolledRarity                 : int                   // 1~5；trash item 固定 1
    rolledTimestamp              : long                  // 被 roll 出的 UTC 時間戳

    // 保留狀態
    isReserved                   : bool                  // 是否處於保留狀態
    reservedTimestamp            : long                  // 進入保留的 UTC 時間戳（0 = 未保留）
    reserveConsumedFlag          : bool                  // 一旦解除保留即永久 true（防刷機制）
}
```

**DataManager / runtime 驗證規則**

- `(staffID > 0) XOR (trashItemID > 0)` —— 兩者必須恰好一個為 0（互斥）
- `staffID > 0 → rolledRarity == StaffTable[staffID].rarity` —— 職員卡稀有度必須對應模板
- `0 ≤ slotIndex < N` —— N = 當前公會等級對應的面試欄張數（見 §3.3 刷新流程）
- `isReserved == true → reservedTimestamp > 0`
- `reserveConsumedFlag == true → isReserved == false` —— 永久化後不可再次保留

**生命週期時點範例**（以 `staffID=101` 克勞德·會計師為例）：

| 時點 | `isReserved` | `reservedTimestamp` | `reserveConsumedFlag` | 所在 list |
|---|---|---|---|---|
| 剛 roll 出 | `false` | `0` | `false` | `currentCandidates[2]` |
| 玩家點「保留」 | `true` | `1714064400` | `false` | `reservedCandidates` |
| 保留時限到 / 手動取消 | `false` | `0` | `true` | 回到 `currentCandidates[2]` |
| 下次 refresh 刷掉 | — | — | — | 整張卡消失（含 `reserveConsumedFlag`） |

**時點 3（保留解除後）的行為保證**（§5 Edge Cases 會引用）：

- 解除保留後卡回到 `currentCandidates[slotIndex]` 佔回原 slot 位置
- 玩家仍可對此卡選「錄用 / 不錄用」
- 「保留」UI 按鈕必須 disable（因 `reserveConsumedFlag = true`）—— 由 P-02 根據此 flag 判斷
- 下次 refresh 時無論是否已 `reserveConsumedFlag`，該 slot 被新 roll 覆蓋，原卡徹底消失


---

### 3.2 StaffGachaPoolTable Schema

**檔案位置**：`Assets/Resources/Data/Tables/StaffGachaPoolTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `poolID` | int (PK) | — | 唯一池識別碼（Jam 版 1 = 常駐通用、2 = 中高等） |
| `poolName` | string | — | 開發辨識名稱（不直接顯示於 UI） |
| `minGuildLevel` | int | — | 該池開放的最低公會等級 |
| `maxGuildLevel` | int | — | 該池開放的最高公會等級（DataManager 驗證 `≥ minGuildLevel`） |
| `eligibleStaffIDs` | CSV list of int | — | 該池可 roll 的職員 `staffID` 清單 |
| `staffWeights` | CSV list of int | — | 平行於 `eligibleStaffIDs`，每位職員在其稀有度層內的 roll 權重（整數 ≥ 0；0 = 保留不抽出） |
| `reserveTimeLimitSec` | int | — | 本池候選的保留時限（秒；必須 > 0） |
| `storyFlagRequired` | string | `""` | 🔒 FT-09 劇情 flag ID 預留欄；Jam 版強制空值 |
| `factionIDRequired` | int | `0` | 🔒 FT-09 陣營 ID 預留欄；Jam 版強制 `0` |
| `minReputation` | int | `0` | 🔒 聲望門檻預留欄；Jam 版強制 `0` |
| `eventStartTimestamp` | long | `0` | 🔒 限時活動起始 UTC 時間戳預留欄；Jam 版強制 `0` |
| `eventEndTimestamp` | long | `0` | 🔒 限時活動結束 UTC 時間戳預留欄；Jam 版強制 `0` |

**DataManager 驗證規則**：

1. `minGuildLevel ≤ maxGuildLevel` —— 否則拋 `StaffGachaPoolTableValidationException("poolID={id}: minGuildLevel 不得大於 maxGuildLevel")`
2. `eligibleStaffIDs.Count == staffWeights.Count` —— 平行 CSV 長度必須一致
3. 每個 `staffWeights[i] ≥ 0`；若 `sum(staffWeights) == 0` —— 拋錯（池無可抽項）
4. `reserveTimeLimitSec > 0`
5. 五個預留閘欄位（`storyFlagRequired` / `factionIDRequired` / `minReputation` / `eventStartTimestamp` / `eventEndTimestamp`）若任一 ≠ 預設值 —— 拋 `StaffGachaPoolTableValidationException("poolID={id}: Jam 版不支援此閘參數，請保持預設值")`

**設計註記**

- **Category-agnostic 容器**：Schema 刻意不含 `poolType` / `factionTag` / `professionTag` 欄位；池的定性（例如「中高等池」）由 `eligibleStaffIDs` 的實際成員隱式決定，為設計意圖而非資料分類
- **職員與池解耦**：池開放/關閉僅影響未來面試結果；既有已錄取職員的 `StaffInstance` 與池無關、不受池狀態變動影響
- **預留閘的前向相容動機**：Post-Jam 擴充 FT-09 劇情 / 活動系統時，只需改 DataManager 驗證邏輯與 runtime 閘判斷，無需改動 schema
- **`staffWeights` 為稀有度層內權重**：稀有度由 `StaffRarityProbTable`（§3.3）先 roll 確定後，再於該層 `eligibleStaffIDs` 中按 `staffWeights` 比例 roll 具體職員

**Jam 版資料範例**（具體數值 §7 調參）：

| poolID | poolName | minGuildLevel | maxGuildLevel | eligibleStaffIDs | staffWeights | reserveTimeLimitSec |
|---|---|---|---|---|---|---|
| 1 | Common Pool | 1 | 5 | `101,102,103,104,105,...` | `10,10,10,5,1,...` | `604800`（7 天） |
| 2 | Advanced Pool | 3 | 5 | `201,202,203,...` | `10,10,5,...` | `604800`（7 天） |

（上表省略 5 個預留閘欄位；一律保持預設空 / 0）


---

### 3.3 面試刷新流程（Refresh Flow）

本節定義四類 refresh 觸發路徑（自動 / 手動 / 切池 / 離線補刷）的 runtime 流程，並給出共享的 roll 序列、`pityCounter` 累加時點、事件發布順序，以及 6 個面試操作 API 的回傳碼合約。對應公式見 §4.1，邊緣案例見 §5.1 / §5.2，AC 見 §8.2 / §8.3 / §8.5。

#### 3.3.1 觸發路徑分類

| 路徑 | 觸發條件 | 檢查時機 | 扣費 | pityCounter | 覆蓋 currentCandidates | lastAutoRefreshTimestamp |
|---|---|---|---|---|---|---|
| Auto | `now − last ≥ autoRefreshIntervalSec(L)` | OnStaffSystemBoot Step C（一次性，§3.3.5）| 否 | += `|refreshableSlots|` | 全 N 張覆蓋 | ← `now` |
| Manual | `TryManualRefresh()` 且 `GetGold() ≥ cost` | 玩家點擊（API 入口）| 是（cost gold）| += `|refreshableSlots|` | 全 N 張覆蓋 | 不變 |
| Switch Pool | `TrySwitchPool(newPoolID)` | 玩家切池（API 入口）| 否 | += N | B 池整個重 roll | 不變 |
| Offline Refill | 啟動時 `missedIntervals ≥ 1`（即 Auto 路徑於 Boot 觸發的別稱）| OnStaffSystemBoot Step C（同上）| 否 | += N（一次） | 全 N 張覆蓋 | ← `now` |

關鍵差異：
- 僅 Auto / Offline 路徑會更新 `lastAutoRefreshTimestamp`（手動 / 切池**不重置自動刷新節奏**）
- 切池 refresh **不保留** A 池的保留 slot（`refreshableSlots = {0..N-1}` 整個重 roll）
- 手動刷新無 cooldown（防呆由 P-02 UI 0.5s disable 處理）
- **Jam 版無線上 auto refresh tick**：所有 Auto 補刷僅於 OnStaffSystemBoot 一次性執行；玩家持續在線**不會**自動刷新候選（節奏感由「離線回來看到新候選」承擔，避免 OnHourTick 訂閱與 in-game 候選突變）

#### 3.3.2 Refresh 共享流程（執行順序）

不論觸發路徑，refresh 統一執行：

```
ExecuteRefresh(refreshType, poolID):
    1. Pre-conditions：
       - IsStaffSystemUnlocked() == true（否則 STAFF_SYSTEM_LOCKED）
       - Manual: GetGold() ≥ refreshCost(guildLevel)
       - Switch: poolID 存在於 StaffGachaPoolTable AND
                 minGuildLevel ≤ currentGuildLevel ≤ maxGuildLevel AND
                 poolID != currentPoolID

    2. 解除過期保留（無條件，所有路徑；位於扣費前以避免「扣費後 ReleaseReserveInternal 拋例外」rollback 複雜度）：
       FOR each c in reservedCandidates:
           IF now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID):
               ReleaseReserveInternal(c)              // §3.3.5

    3. 確定 refresh slot 範圍：
       N = StaffRefreshCostTable[guildLevel].interviewSlotCount
       IF refreshType == SwitchPool:
           newCurrentPoolID = newPoolID                // 暫存,step 6 commit
           refreshableSlots = { 0..N-1 }              // B 池整個重 roll
       ELSE:
           reservedSlotIndices = { c.slotIndex | c ∈ reservedCandidates AND c.poolID == currentPoolID }
           refreshableSlots = { 0..N-1 } \ reservedSlotIndices
       IF refreshableSlots.IsEmpty:
           return NO_REFRESHABLE_SLOT                  // 全 slot 被保留;不扣費、不變動狀態

    4. **Pre-compute roll**（commit 前完成所有 RollOneSlot,寫入暫存區 `pendingCards: List<CandidateCard>`）：
       FOR each slotIndex in refreshableSlots（首個帶 isFirstSlotInRefresh = true）：
           card = RollOneSlot(slotIndex, currentPoolID, isFirstSlotInRefresh)   // §3.3.3
           pendingCards.Add(card)
       // RollOneSlot 內部已對 pityCounter 應用保底 reset（命中時 ← 0）;此 step 為 in-memory 計算,任一 throw 不影響 SaveData

    5. 扣費（僅 Manual；step 4 全成功才執行,確保「扣費 → roll 失敗」不會發生）：
       IF refreshType == Manual:
           F03.AddGold(-refreshCost(guildLevel))   // pre-check 已於 step 1 確保 GetGold() ≥ cost

    6. **Commit pendingCards 至 currentCandidates**（atomic：對每個 slotIndex 寫入對應 card；失敗將回滾扣費）：
       FOR card in pendingCards:
           currentCandidates[card.slotIndex] ← card
       IF refreshType == SwitchPool:
           currentPoolID ← newCurrentPoolID

    7. pityCounter ← pityCounter + |refreshableSlots|
       // 注意：保底命中時 RollOneSlot 內部已將 pityCounter 設為 0；本步驟對殘餘 slot 數累加

    8. 更新時間戳（僅 Auto / Offline）：
       lastAutoRefreshTimestamp ← now

    9. 持久化 StaffPlayerState（FT-10 標 dirty,實際寫入由 FT-10 節流策略決定）

   10. 不發布事件（refresh 本身為私有狀態，§3.3.7）
```

> **Transaction 順序設計理由**:Roll 出例外時(極罕見:資料表異動造成 `poolEligibleByRarity` 為空 等)若已扣費將造成「玩家錢沒了但 candidates 沒換」;故採「pre-compute → 扣費 → commit」順序,確保失敗路徑可乾淨 rollback(直接 `return` 即可,因 `_playerState` 尚未變動)。

> **保底計數的精確時序**：保底命中發生在 `RollOneSlot(slotIndex=refreshableSlots[0])` 內部，當下將 `pityCounter ← 0`；其後 step 5 的後續 slot 各自 RollOneSlot 不再帶保底；step 6 對 `pityCounter` 累加 `|refreshableSlots|`，實際數值為 `0 + N`（保底命中當次 refresh 後 counter 為 N）。未命中保底的 refresh 為 `pityCounter += |refreshableSlots|`。

#### 3.3.3 RollOneSlot 流程（單 slot roll）

```
RollOneSlot(slotIndex, poolID, isFirstSlotInRefresh):
    1. 保底判定（僅 isFirstSlotInRefresh = true 時檢查）：
       shouldForce5Star = (pityCounter ≥ PITY_THRESHOLD)
                          AND (poolEligibleByRarity(poolID, 5) ≠ ∅)
                          AND isFirstSlotInRefresh
       IF shouldForce5Star:
           rolledRarity ← 5
           pityCounter ← 0                            // reset 立即生效
           GOTO step 3

    2. 稀有度 roll（§4.1.5 動態歸一化）：
       compute effectiveProb[1..5] using emptyTiers(poolID)
       rolledRarity ← weightedRoll(effectiveProb)

    3. 層內 staff roll（§4.1.6）：
       tierStaff = poolEligibleByRarity(poolID, rolledRarity)
       (rolledStaffID, _) ← weightedRoll(staffWeights restricted to tierStaff)

    4. Trash detection（§3.5.3）：
       IF rolledRarity == 1 AND ShouldRollTrash():
           trashItemID ← rollFromTrashItemTable()
           rolledStaffID ← 0
       ELSE:
           trashItemID ← 0

    5. 建立 CandidateCard 並寫入 currentCandidates[slotIndex]：
       card = CandidateCard {
           poolID = poolID,
           slotIndex = slotIndex,
           staffID = rolledStaffID,
           trashItemID = trashItemID,
           rolledRarity = rolledRarity,
           rolledTimestamp = now,
           isReserved = false,
           reservedTimestamp = 0,
           reserveConsumedFlag = false
       }
       currentCandidates[slotIndex] ← card
```

關鍵約束：
- 保底僅作用於該次 refresh 的**第一張被刷的 slot**（即 `refreshableSlots[0]`，可能不是 `slotIndex == 0`，例如保留鎖定了 slot 0 時）
- `pityCounter ← 0` 在保底命中後立即生效；後續 slot 的判定條件 `isFirstSlotInRefresh = false` 確保不重複觸發
- Trash detection 細節見 §3.5.3（決定 trash 觸發機率）

#### 3.3.4 公開 API 合約

| API | 簽章 | 回傳碼 | 主要副作用 |
|---|---|---|---|
| `TryManualRefresh()` | `() → RefreshResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `GOLD_INSUFFICIENT` | 扣費、覆蓋 candidates、`pityCounter +=` |
| `TrySwitchPool(int newPoolID)` | `(int) → SwitchPoolResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `POOL_NOT_FOUND` / `POOL_LEVEL_LOCKED` / `ALREADY_IN_POOL` | 切池並 refresh、`pityCounter += N` |
| `TryRecruit(int slotIndex)` | `(int) → RecruitResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` / `CANDIDATE_NOT_HIREABLE` / `ROSTER_FULL` | 建 StaffInstance、發 OnStaffHired、清空該 slot |
| `TryRejectCandidate(int slotIndex)` | `(int) → RejectResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` | 清空該 slot |
| `TryReserveCandidate(int slotIndex)` | `(int) → ReserveResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_SLOT` / `EMPTY_SLOT` / `RESERVE_FULL` / `RESERVE_CONSUMED` | 移卡至 reservedCandidates、`isReserved = true` |
| `TryReleaseReserve(int reserveIndex)` | `(int) → ReleaseResult` | `SUCCESS` / `STAFF_SYSTEM_LOCKED` / `INVALID_INDEX` / `SLOT_OCCUPIED_BY_NEW_ROLL` | 移卡回 currentCandidates[原 slotIndex]、`reserveConsumedFlag = true` |

回傳碼語意：

| 回傳碼 | 觸發條件 |
|---|---|
| `STAFF_SYSTEM_LOCKED` | `IsStaffSystemUnlocked() == false` |
| `INVALID_SLOT` | `slotIndex < 0 OR slotIndex ≥ N` |
| `EMPTY_SLOT` | `currentCandidates[slotIndex] == null`（被保留鎖定 / refresh 未填入）|
| `INVALID_INDEX` | `reserveIndex < 0 OR reserveIndex ≥ reservedCandidates.Count` |
| `POOL_NOT_FOUND` | `StaffGachaPoolTable[newPoolID]` 不存在 |
| `POOL_LEVEL_LOCKED` | `currentGuildLevel ∉ [pool.minGuildLevel, pool.maxGuildLevel]` |
| `ALREADY_IN_POOL` | `newPoolID == currentPoolID` |
| `CANDIDATE_NOT_HIREABLE` | 該卡為 trash item（`trashItemID > 0`）|
| `ROSTER_FULL` | `roster.Count == ROSTER_CAP` |
| `RESERVE_FULL` | `reservedCandidates.Count == maxReserve`（§4.1.9）|
| `RESERVE_CONSUMED` | 該卡 `reserveConsumedFlag == true` |
| `SLOT_OCCUPIED_BY_NEW_ROLL` | 解除保留時原 slot 已被新 roll 覆蓋（Case 5.2.4）|

#### 3.3.5 離線補刷流程（OnStaffSystemBoot）

FT-08 啟動序列（FT-10 載入後立即執行）：

```
OnStaffSystemBoot():
    IF NOT IsStaffSystemUnlocked():
        RETURN                                        // 系統降級，跳過所有 refresh / pity / 薪水

    // Step A：新存檔 / 異常 timestamp 處理
    IF lastAutoRefreshTimestamp == 0 OR lastAutoRefreshTimestamp > now:
        lastAutoRefreshTimestamp ← now                // Case 5.1.1 / 5.1.3

    // Step B：解除過期保留（先於補刷，避免 race）
    FOR each c in reservedCandidates.ToList():        // 拷貝以支援 in-loop 移除
        IF now − c.reservedTimestamp ≥ reserveTimeLimitSec(c.poolID):
            ReleaseReserveInternal(c)
            // ReleaseReserveInternal 嘗試將卡放回 currentCandidates[c.slotIndex]
            // 若 c.poolID != currentPoolID（玩家保存時在另一池）→ 卡丟失（不放回）

    // Step C：補刷判定（§4.1.4）
    L = FT07.GetBuildingLevel(6)
    intervalSec = autoRefreshIntervalSec(L)
    missedIntervals = floor((now − lastAutoRefreshTimestamp) / intervalSec)
    refillCount = clamp(missedIntervals, 0, 1)

    IF refillCount == 1:
        ExecuteRefresh(refreshType=Auto, poolID=currentPoolID)
        // ExecuteRefresh 內部 lastAutoRefreshTimestamp ← now
    ELSE IF currentCandidates.All(c => c == null):    // 首次解鎖、currentCandidates 為空
        ExecuteRefresh(refreshType=Auto, poolID=currentPoolID)

    // Step D：薪水補發（FT-12 FT-12 FT-12 §3.5.4）—— ⚠️ Phase 2，Jam 版整段跳過（FT-12 FT-12 FT-12 §3.5 為 Phase 2）
    ProcessOfflineSalary()                            // Jam 版不執行（FT-12 FT-12 FT-12 §3.5 / FT-12 §3.4.3）
```

**保留卡與補刷的競態保證**（Case 5.2.4 回應）：

1. Step B 先掃描過期保留 → ReleaseReserveInternal 嘗試將卡放回 `currentCandidates[c.slotIndex]`
2. 若 Step C 觸發補刷（refillCount == 1）→ 整個 refreshableSlots（含剛放回的 slot）被重 roll → **原卡丟失**（合法行為，符合 Case 5.2.4「離線期間原 slot 已被新 roll 覆蓋」語意）
3. 若 Step C 不補刷（refillCount == 0）→ 卡保留於 slot，玩家登入後可選錄用 / 不錄用

#### 3.3.6 StaffRefreshCostTable Schema

**檔案位置**：`Assets/Resources/Data/Tables/StaffRefreshCostTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `guildLevel` | int (PK) | 1~5 | 公會等級索引 |
| `cost` | int | 100~1000 | 手動刷新費（gold；§4.1.2）|
| `interviewSlotCount` | int | 3~5 | 該等級的面試欄張數 N（§4.1.1）|

**DataManager 驗證規則**：

1. `guildLevel` PK 連續 1~5（Jam 版）；缺行拋 `StaffRefreshCostTableValidationException("missing guildLevel={n}")`
2. `cost ≥ 0`
3. `1 ≤ interviewSlotCount ≤ 5`

**Jam 版範例**（與 §7.1.1 一致）：

| guildLevel | cost | interviewSlotCount |
|---|---|---|
| 1 | 100 | 3 |
| 2 | 150 | 3 |
| 3 | 250 | 4 |
| 4 | 400 | 4 |
| 5 | 600 | 5 |

#### 3.3.7 事件發布原則

Refresh 流程本身**不發事件**（私有狀態變化）；由 refresh 結果觸發的玩家動作各自發事件：

| 動作 | 事件 | 發布時機 |
|---|---|---|
| `TryRecruit` 成功 | `OnStaffHired` | refresh 完成後玩家點錄用 → 即發 |
| `TryRejectCandidate` | （無）| 不發事件，純清空 slot |
| `TryReserveCandidate` | （無）| 不發事件，純移動 list |
| `TryReleaseReserve` | （無）| 不發事件 |
| `TrySwitchPool` 成功 | （無）| 不發事件（refresh 為私有）|
| Auto / Offline refresh | （無）| 不發事件 |

**設計理由**：refresh 是「面試欄被刷新」的私有 UI 狀態變動；P-02 由 query API（`GetCurrentCandidates`）/ polling 自行更新顯示；無需事件機制（避免事件洪水與訂閱者管理開銷）。


---

### 3.4 保底機制細節（Pity Mechanism Details）

公式定義於 §4.1.7 / §4.1.8、邊界案例於 §5.3、AC 於 §8.4；本節展開 runtime 細節。

#### 3.4.1 pityCounter 生命週期

| 階段 | 行為 |
|---|---|
| 新存檔初始化 | `pityCounter ← 0`（FT-08 啟動流程；AC-2）|
| 每次 refresh | `pityCounter += |refreshableSlots|`（§3.3.2 步驟 6）|
| 保底命中 | `pityCounter ← 0`（§3.3.3 步驟 1，立即生效）|
| 池中 5★ 空（pityCounter ≥ PITY_THRESHOLD）| 不 reset、不觸發、繼續累積（Case 5.3.1）|
| 自然 roll 出 5★（非保底）| **不 reset**（§3.4.3）|
| 公會升級 | 不影響 |
| 切池（TrySwitchPool）| 不影響、`pityCounter += N`（同 refresh）|
| 解除保留 / 解雇 / 切態 | 不影響 |
| 系統降級至 L0 | 持久化保留、不 reset（Case 5.3.3）|
| 跨 session 保存 | FT-10 序列化 `StaffPlayerState.pityCounter`（§3.2.3）|

#### 3.4.2 保底命中流程

對應 §3.3.3 RollOneSlot 步驟 1：

```
isFirstSlotInRefresh = (slotIndex == refreshableSlots[0])
shouldForce5Star = (pityCounter ≥ PITY_THRESHOLD)
                   AND (poolEligibleByRarity(currentPoolID, 5) ≠ ∅)
                   AND isFirstSlotInRefresh
```

實作要點：

- 「該次 refresh 第一張」用 `isFirstSlotInRefresh` 旗標而非 `pityCounter ≥ PITY_THRESHOLD` 判定——因為命中後 counter 立刻 reset，不能再用 counter 值判斷後續 slot 是否仍處於保底狀態
- `refreshableSlots[0]` **不一定 = `slotIndex 0`**：若玩家保留了 slot 0、該次 refresh 跳過 slot 0，則 `refreshableSlots[0] = 1`，保底作用於 slot 1
- 命中後 RollOneSlot 進入 step 3「層內 staff roll」走 §4.1.6 權重；若 5★ 層含多位職員，按 staffWeights 比例 roll 具體 staffID

#### 3.4.3 保底未觸發路徑

```
shouldForce5Star == false 時：
    rolledRarity ← weightedRoll(effectiveProb)        // §4.1.5

    互動：
    (a) pityCounter < 10：正常 roll；roll 出 5★ 不 reset
    (b) pityCounter ≥ PITY_THRESHOLD 但池 5★ 空：正常 roll；pityCounter 繼續累積
    (c) pityCounter ≥ PITY_THRESHOLD 且 isFirstSlotInRefresh = false（保底已在前 slot 兌現後 reset）：正常 roll
```

**設計決策（§7.4 不可調）**：自然 roll 命中 5★ 不 reset counter——保底進度與自然命中獨立計算，避免「自然抽到 5★ 後保底進度被消除」的玩家不公平感。

#### 3.4.4 與動態歸一化的互動

當 `pityCounter ≥ PITY_THRESHOLD` 但 `emptyTiers` 包含 5★：

```
保底判定 → shouldForce5Star = false（poolEligibleByRarity == ∅）
↓
正常 roll 走 §4.1.5：emptyTiers = {5} → effectiveProb 在 1~4 重新分配
↓
pityCounter += |refreshableSlots|（繼續累積）
↓
玩家下一次 refresh 仍可能在同池被擋（5★ 仍空）→ counter 繼續上升
↓
玩家切到含 5★ 的池 / 升等開放 5★ → 下次 refresh 兌現
```

**counter 上限**：理論無上限；實務 Jam 版玩家不太可能累積到 30+（除非整段 game session 都待在 5★ 空池）。

#### 3.4.5 跨 session 行為

FT-10 載入時驗證：

| 欄位 | 載入時驗證 | 違反處理 |
|---|---|---|
| `pityCounter` | `≥ 0`、整數 | 拋 `StaffPlayerStateValidationException("pityCounter invalid")` |

**5★ 空池 + counter 累積的存檔遷移情境**：

- 保存時 `pityCounter = 65`、`currentPoolID = 2`（B 池 5★ 空，已超過 PITY_THRESHOLD = 60）→ 載入後 counter 仍 65
- OnStaffSystemBoot Step C 補刷時若 B 池 5★ 仍空 → 不觸發強制 5★、counter 繼續 +N
- 玩家切到 A 池（含 5★）→ 切池 refresh 立即兌現第一張 5★

#### 3.4.6 Debug Reset 契約

供開發 / QA 使用，**不對外暴露**（不在 P-02 UI、不在玩家 API）：

```csharp
#if UNITY_EDITOR
internal void DebugResetPityCounter() {
    _playerState.pityCounter = 0;
    // 不發事件、不持久化（待下次 SaveGame 自然寫入）
}
#endif
```

安全範圍：

- **`#if UNITY_EDITOR` 包住整個 method**(不用 `[Conditional]` attribute,因為後者僅移除 call site,method 仍編譯進 release build → 仍可被 reflection 呼叫);Release build 完全不編譯此 method
- `internal` scope（同 assembly 訪問）+ instance method(不用 static,確保由 FT-08 service 實例呼叫)
- 無事件、無扣費、無 refresh 觸發；純改 counter 值
- 暴露途徑:Editor 環境的 debug menu / inspector button,**不在 P-02 UI 暴露**(FT-08 對外無此 public API)


---

### 3.5 垃圾物品（Trash Items）

#### 3.5.1 概念與職責邊界

Trash item = 抽卡的「空抽」結果，於 CandidateCard 以 `trashItemID > 0` / `staffID == 0` 表示。職責邊界：

| 屬性 | Trash Item | Filler Staff（`isFiller=true`）|
|---|---|---|
| 入職 | ❌ | ✅ |
| 占名冊 | ❌ | ✅（計入 ROSTER_CAP）|
| 領薪水 | ❌ | ✅（薪水可能 0）|
| 提供 effect | ❌ | ✅（極弱 / 空）|
| 解雇 | ❌ | ✅（資遣費可能 0）|
| 在 CandidateCard | `trashItemID > 0`、`staffID = 0` | `staffID > 0`、`trashItemID = 0` |
| 玩家可選操作 | reject / reserve | recruit / reject / reserve |
| 計入 pityCounter | ✅（每張 +1，如同 staff card）| ✅（同左）|

#### 3.5.2 TrashItemTable Schema

**檔案位置**：`Assets/Resources/Data/Tables/TrashItemTable.csv`

**Schema**：

| 欄位 | 型別 | Jam 預設 | 說明 |
|---|---|---|---|
| `trashItemID` | int (PK) | ≥ 1 | 唯一識別碼 |
| `name` | string | 非空 | 物品名稱（顯示用，例：「咖啡杯」「履歷紙團」）|
| `flavorText` | string | 非空 | 風味文字（writer agent 提供）|
| `iconAssetID` | string | `""` | 視覺資產 ID 預留欄；Jam 版使用通用 trash icon |

**DataManager 驗證規則**：

1. `trashItemID ≥ 1`；不可與任一 `StaffTable.staffID` 衝突 → 拋 `TrashItemTableValidationException("trashItemID={id} collides with StaffTable")`
2. `name` / `flavorText` 非空 / 非 null
3. PK unique（同表內）

**Jam 版範例**：

| trashItemID | name | flavorText |
|---|---|---|
| 9001 | 咖啡杯 | 冷掉的咖啡，杯緣印著陌生的口紅印。 |
| 9002 | 履歷紙團 | 揉爛的履歷，最上方寫著「無經驗無熱情」。 |
| 9003 | 過期身份證 | 拍照時的笑容已經過期五年。 |
| 9004 | 借據 | 不知道借了誰多少錢。 |
| 9005 | 退稿小說 | 第一章寫得很好，第二章開始就崩了。 |

數量建議 5~15 個（§7.1.6）。

#### 3.5.3 Trash Roll 觸發機率

`ShouldRollTrash()` 於 §3.3.3 步驟 4 引用。Jam 版規則：

```
ShouldRollTrash():
    IF rolledRarity != 1: return false                // 僅 1★ 可變 trash
    trashRate = TRASH_ROLL_RATE_AT_RARITY_1           // 預設 0.30（§7.2 可調）
    return Random.value < trashRate
```

**設計依據**：

- 1★ 在 baseProb 為 0.40 → trash 全體機率 ≈ 0.40 × 0.30 = 0.12（與 §7.1.6 「1★ 層內 flavor 占 30%、全體約 12%」一致）
- 不影響 4★ / 5★ 結果（不會搶走玩家高價值 roll）
- 中後期玩家解鎖 B 池（3★~5★ 為主、1★ 層空）→ trash 自動消失（emptyTiers 含 1）

#### 3.5.4 Trash Roll 後續行為

對應 CandidateCard schema（§3.2.3）：

```
roll 結果為 trash → CandidateCard {
    staffID = 0,
    trashItemID = (rollFromTrashItemTable),
    rolledRarity = 1,                                 // trash 強制 1
    ...
}
```

玩家對 trash card 的可選操作：

| 操作 | 行為 |
|---|---|
| `TryRecruit(slotIndex)` | 回 `CANDIDATE_NOT_HIREABLE`（AC-22）；不變動任何狀態 |
| `TryRejectCandidate(slotIndex)` | 與 staff card 同——清空 slot |
| `TryReserveCandidate(slotIndex)` | 允許保留（trash 也佔保留 slot）；UI 可選 disable 但 FT-08 不 enforce |

**設計理由**：保留 trash 在 Jam 版屬合法行為（玩家可能有圖鑑收集慾，雖 Jam 版無圖鑑）；UI 層可額外提示「這張看起來不重要」但不阻擋。

#### 3.5.5 Trash 計入 pityCounter

§4.1.7 已定義 `pityCounter += N`，每張 +1 不分 staff / trash。Trash 計入 pity 的設計理由：

- 保底語意 = 「累積失望張數的補償」；trash 比 1★ 普通職員更失望，理應計入
- 簡化實作：refresh 流程不需在 RollOneSlot 完成後逐張判斷類型再決定 +N
- 不破壞 §3.4.3「自然命中 5★ 不 reset」原則：5★ 不可能是 trash（trash 限 rarity=1）

#### 3.5.6 Filler Staff 與 Trash 的差異化處理

**Filler staff**（`StaffTable.isFiller = true`）：

- 仍是 `StaffInstance`、有 `instanceID`、可指派、可解雇
- effect 數值極弱（如 `Willingness +0.01`）或空集合
- 薪水可低至 0、severancePay 可為 0
- 提供「灰階人物」flavor，避免 1★ 全是強力職員的奇異感

**Trash item**：

- 純 UI flavor、不入名冊、不消耗 ROSTER_CAP
- 無 `instanceID`、無狀態、無事件
- 玩家「處理掉」= 等下次 refresh 覆蓋 / 主動 reject

**池內配比建議**（§7.1.3 / §7.1.6）：1★ 層內 staff 與 trash 的比例約 7:3（trashRate = 0.30）；filler staff 占 1★ staff 的 30~50%（保留少量風味平庸職員）。


---


### 3.6 系統降級行為總表（Degradation Contract）

當 `FT07.IsStaffSystemUnlocked() == false` 時，FT-08 進入降級模式。

#### 3.6.1 對外 API 行為總表

| API | 降級時行為 |
|---|---|
| `IsStaffSystemUnlocked()` | 直接 return false（轉發 FT-07）|
| `TryManualRefresh()` | return `STAFF_SYSTEM_LOCKED`；不扣費、不變動 candidates |
| `TrySwitchPool(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryRecruit(int)` | return `STAFF_SYSTEM_LOCKED`；不呼叫 FT-12 |
| `TryRejectCandidate(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryReserveCandidate(int)` | return `STAFF_SYSTEM_LOCKED` |
| `TryReleaseReserve(int)` | return `STAFF_SYSTEM_LOCKED` |

#### 3.6.2 事件發布總表

| 事件 | 降級時 |
|---|---|
| `OnStaffSystemBoot` | 仍會發布（Bootstrap 階段；payload 內 `isUnlocked = false`）|
| `OnCandidateRolled`（如有，內部）| 不發布 |
| `OnPoolSwitched`（如有，內部）| 不發布 |

> **gacha 未發布業務事件**：FT-08 為玩家輸入導向系統，gacha 內部狀態變動由 P-02 透過 API 查詢取得，無需事件廣播。

#### 3.6.3 內部流程降級

| 流程 | 降級時 |
|---|---|
| `OnStaffSystemBoot` Step C（Auto / Offline 補刷判定） | 跳過；**Jam 版本就無線上 auto refresh tick**——所有 auto 補刷僅於 Boot 一次性執行，降級時整段不執行 |
| 過期保留掃描（`reservedCandidates` 時限到） | 跳過（reservedCandidates 凍結；解鎖後 OnStaffSystemBoot Step B 處理）|

#### 3.6.4 持久化資料保留

降級期間，`StaffPlayerState` 全部欄位保留（pityCounter、lastAutoRefreshTimestamp、currentPoolID、currentCandidates、reservedCandidates）；解鎖後接續使用。

#### 3.6.5 降級進入 / 離開的事件 callback

FT-08 不主動訂閱 FT-07 `OnBuildingUpgraded` 用於 API 降級——所有 API 入口處 query `IsStaffSystemUnlocked()`（惰性檢測）。

降級進入（L1+ → L0）Jam 版**不訂閱**——降級為 lazy 檢測；玩家若在線降級會看到下次 query 即返回 locked。

---

### 3.7 事件契約（Event Contracts）

§6 已概覽事件清單；本節展開 FT-08 唯一發布的事件 payload 與時序。

#### 3.7.1 事件清單與 payload

```
OnStaffSystemBoot {
    bool isUnlocked          // FT-07.IsStaffSystemUnlocked() 結果
    int  appliedRefreshCount // Step C 補刷次數
    int  releasedReserveCount // Step B 過期保留釋放數
    long bootTimestamp        // 此次 Bootstrap 執行時點
}
```

> Jam 版 FT-08 的所有玩家可見變動（roll、保留、池切換）由 P-02 透過 API 即時查詢顯示，不需業務事件廣播。

#### 3.7.2 訂閱者責任表

| 事件 | 訂閱者 | 處理動作 |
|---|---|---|
| OnStaffSystemBoot | P-02 名冊 / 面試 UI | Bootstrap 後刷新 UI 狀態（保留區、候選區） |
| OnStaffSystemBoot | P-03（可選）| 補刷有發生時 toast「面試已就緒」 |

#### 3.7.3 玩家錄用觸發 FT-12 OnStaffHired

玩家於 FT-08 點「錄用」 → FT-08 呼叫 `FT-12.HireStaff(candidate)` → FT-12 內部發 `OnStaffHired`（payload 含 instanceID / staffID / hiredTimestamp）。FT-08 不訂閱此事件，也不轉發。

**Transaction 邊界**：

```
TryRecruit(slotIndex):
    1. Pre-conditions:
       - IsStaffSystemUnlocked() == true（否則 STAFF_SYSTEM_LOCKED）
       - IsValidSlotIndex(slotIndex) AND NOT IsEmptySlot(currentCandidates[slotIndex])（否則 INVALID_CANDIDATE_INDEX）
       - currentCandidates[slotIndex].staffID > 0（否則 CANDIDATE_NOT_HIREABLE,trash 不可錄用）

    2. **冪等保護**: IF _isHiringInFlight: return BUSY    // 防 P-02 連點兩次同 frame 重複呼叫
       _isHiringInFlight = true                              // 進入 critical section
       try:
           candidate = currentCandidates[slotIndex]            // snapshot (不立即移除,等 FT-12 OK)

    3. 呼叫 FT-12（FT-12 內部 transaction:`_roster.Add` → `EventBus.Publish(OnStaffHired)` → return）：
           hireResult = FT12.HireStaff(candidate)

    4. 處理 FT-12 回應:
           IF hireResult.result == OK:
               currentCandidates[slotIndex] ← MakeEmptySlot()    // §3.1 placeholder card
               return RECRUIT_OK
           ELIF hireResult.result == STAFF_SYSTEM_LOCKED:
               LogError("FT-08/FT-12 unlock state out-of-sync, retry next frame")
               return STAFF_SYSTEM_LOCKED                          // 不變動 currentCandidates,玩家可重試
           ELIF hireResult.result == INVALID_STAFF_ID:
               LogError("CandidateCard.staffID={id} not in StaffTable; FT-12 rejected")
               return INVALID_STAFF_ID                             // 不變動,玩家可 reject 該卡
           ELSE:
               return INTERNAL_ERROR                                // 罕見,保留候選由玩家重試
       finally:
           _isHiringInFlight = false                              // 離開 critical section
```

**Transaction 安全保證**：

- FT-08 在 FT-12 回 OK 前**不移除** candidate(`currentCandidates[slotIndex]` 不變),確保 FT-12 失敗時 FT-08 狀態回退乾淨
- FT-12 內部已用 `EventBus` 強化(commits 25efd11 / 2c10d17)隔離訂閱者例外:任一訂閱者 throw 不影響 publish 端,不破壞 FT-12 `_roster.Add` 已提交的 instance
- FT-12 在 `EventBus.Publish` 前已 `_roster.Add`,publish 拋例外為極罕見路徑(OOM 等);**Jam 版接受此風險**,實作層由 EventBus 隔離保障
- `_isHiringInFlight` 旗標防 P-02 在 FT-12 同步呼叫進行中再次觸發 TryRecruit(同 frame 連點);因 FT-12 同步回傳,實際 critical section 跨 frame 機率極低

#### 3.7.4 訂閱者錯誤隔離 / 降級時不發布

EventBus 已強化（commits 25efd11 / 2c10d17）：任一訂閱者 throw → 隔離、其他訂閱者繼續執行；FT-08 自身不在訂閱中重發任何事件。降級時 §3.6.2 已詳列發布行為。

---


## 4. 公式（Formulas）

> 本章彙整 FT-08 面試系統的核心公式（F-1.1 ~ F-1.9）。effect 聚合 / 薪水公式已移至 FT-12 staff-system.md §4.1 / §4.2。

### 4.1 面試系統公式（Interview System Formulas）

#### 4.1.1 面試欄張數 N（interview slot count）

```
N(guildLevel) = StaffRefreshCostTable[guildLevel].interviewSlotCount
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `guildLevel` | 玩家當前公會等級 | `1 ≤ guildLevel ≤ 5`（Jam 版）|
| `N` | 本次 refresh 呈現的候選張數 | `1 ≤ N ≤ 5`（Jam 版建議上限 5，避免 UI 擁擠）|

**範例**：`guildLevel = 3`、`StaffRefreshCostTable[3].interviewSlotCount = 4` → `N = 4`。該次 refresh 填滿 `currentCandidates[0..3]`。

---

#### 4.1.2 手動刷新費（manual refresh cost）

```
refreshCost(guildLevel) = StaffRefreshCostTable[guildLevel].cost
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `refreshCost` | 玩家主動付費刷新一次的金幣成本 | `refreshCost ≥ 0`（Jam 版建議 100 ~ 500 gold）|

- 表查詢，不依賴 pool（刷新費與當前選中池無關）
- 付款流程：扣款在 `TryManualRefresh()` 內部依序檢查 `GetGold() ≥ refreshCost` → 扣款 → 觸發 roll；不足回 `GOLD_INSUFFICIENT`

**範例**：`guildLevel = 2`、`StaffRefreshCostTable[2].cost = 150` → 玩家點「刷新」時扣 150 gold，roll 出新的 N 張候選。

---

#### 4.1.3 自動刷新間隔（auto-refresh interval）

```
buildingInterval     = BuildingTable[6].upgradeData[L].effectValue
staffReduction       = FT12.GetRecruitRefreshReductionSec()       // 公會櫃臺職員的 RecruitRefreshOnCounter slot effect 加總(已套用 FT-12 §4.1 cap);系統未解鎖時為 0
autoRefreshIntervalSec(L) = max(MIN_AUTO_REFRESH_INTERVAL_SEC, buildingInterval - staffReduction)
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `L` | 當前職員休息室等級；`L = FT07.GetBuildingLevel(6)` | `1 ≤ L ≤ 5`（`L = 0` 時系統鎖，不走此公式）|
| `buildingInterval` | 職員休息室等級對應的基礎自動刷新間隔 | `21600 ≤ buildingInterval ≤ 86400`（FT-07 §7.1 鎖定階梯）|
| `staffReduction` | FT-12 公會櫃臺職員 slot effect 加總 | `0 ≤ staffReduction ≤ 14400`（FT-12 §4.1 cap,預設上限 4h）|
| `MIN_AUTO_REFRESH_INTERVAL_SEC` | `StaffTuning.csv` 系統常數,預設 `3600`(1h) | `[1800, 7200]`（§7.2） |
| `autoRefreshIntervalSec` | 套用 FT-12 加成後的最終秒數間隔 | `≥ MIN_AUTO_REFRESH_INTERVAL_SEC` |

**FT-07 階梯**（引用自 FT-07 §7.1）：L1=86400（24h）/ L2=64800（18h）/ L3=43200（12h）/ L4=28800（8h）/ L5=21600（6h）。

**範例**：
- 玩家升職員休息室至 L3、無公會櫃臺職員 → `buildingInterval = 43200`、`staffReduction = 0` → `autoRefreshIntervalSec = 43200`（12 小時）
- 玩家升職員休息室至 L5、有 1 位櫃臺職員帶 `RecruitRefreshOnCounter = 7200`（2h）並指派至公會櫃臺 → `buildingInterval = 21600`、`staffReduction = 7200` → `autoRefreshIntervalSec = max(3600, 21600 - 7200) = 14400`（4 小時）
- 極端情境:`staffReduction` 達 cap 14400 + 職員休息室 L5(`buildingInterval = 21600`) → `autoRefreshIntervalSec = max(3600, 21600 - 14400) = 7200`（2 小時）;若 `staffReduction > buildingInterval - MIN_AUTO_REFRESH_INTERVAL_SEC` 則被 floor 截斷至 `MIN_AUTO_REFRESH_INTERVAL_SEC`

---

#### 4.1.4 離線補刷次數（offline refresh refill count）

```
missedIntervals  = floor((now - lastAutoRefreshTimestamp) / autoRefreshIntervalSec(L))
refillCount      = clamp(missedIntervals, 0, 1)
```

補刷後更新：

```
IF refillCount == 1:
    lastAutoRefreshTimestamp ← now     // 重置為 now，下個 interval 從 now 起算
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `now` | 登入時的 UTC 時間戳 | 大於 `lastAutoRefreshTimestamp`（時鐘異常見 §5）|
| `lastAutoRefreshTimestamp` | `StaffPlayerState.lastAutoRefreshTimestamp` | 全域單一計時器（§3.2.3）|
| `missedIntervals` | 離線期間理論應觸發的次數 | `≥ 0`；可能很大（例：離線 7 天 L1 = 7）|
| `refillCount` | 實際補刷次數 | `clamp → {0, 1}` |

**設計決策**：clamp 上限 1 而非累積補刷——防止長期離線回來看到爆炸性歷史候選堆積、UI 展示混亂；同時保底 counter 也只 +N 一次（不被離線放大）。補刷後 `lastAutoRefreshTimestamp ← now`（非 `+= intervalSec`）以讓玩家重置 cadence。

**範例**：
- `now = 1714000000`、`lastAutoRefreshTimestamp = 1713900000`、`L = 2`（`intervalSec = 64800`） → `missedIntervals = floor(100000 / 64800) = 1` → `refillCount = 1` → 補 1 次 refresh、更新 `lastAutoRefreshTimestamp = 1714000000`。
- 離線 7 天後登入（`now - last = 604800`）、`L = 5`（`intervalSec = 21600`） → `missedIntervals = 28` → `refillCount = 1`（clamp）→ 玩家僅見一次新 refresh。

---

#### 4.1.5 稀有度動態歸一化（rarity dynamic normalization）

**基底機率**（來自 `StaffRarityProbTable`）：

```
baseProb[1] = 0.40, baseProb[2] = 0.25, baseProb[3] = 0.15, baseProb[4] = 0.15, baseProb[5] = 0.05
Σ baseProb = 1.00
```

**動態歸一化**（池中該層無職員時重新分配）：

```
emptyTiers       = { r | poolEligibleByRarity(poolID, r) == ∅ }
normalizationDen = 1 − Σ_{r ∈ emptyTiers} baseProb[r]
effectiveProb[r] = baseProb[r] / normalizationDen     (r ∉ emptyTiers)
effectiveProb[r] = 0                                  (r ∈ emptyTiers)
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `poolEligibleByRarity(poolID, r)` | 池內稀有度 `r` 且 `staffWeights[i] > 0` 的職員子集 | 子集 |
| `emptyTiers` | 該池空的稀有度集合 | `⊆ {1, 2, 3, 4, 5}` |
| `normalizationDen` | 歸一化分母 | `(0, 1]`（至少一層非空時 > 0）|
| `effectiveProb[r]` | 實際 roll 機率 | `[0, 1]`；`Σ effectiveProb = 1` |

**設計約束**：`|emptyTiers| < 5`——池中**至少一層非空**（DataManager 驗證；全空池拋 `StaffGachaPoolTableValidationException`）。

**範例**（池 B 中高等，假設 1★ / 2★ 層空、含 3★~5★）：
- `emptyTiers = {1, 2}`
- `normalizationDen = 1 − (0.40 + 0.25) = 0.35`
- `effectiveProb[3] = 0.15 / 0.35 ≈ 0.4286`
- `effectiveProb[4] = 0.15 / 0.35 ≈ 0.4286`
- `effectiveProb[5] = 0.05 / 0.35 ≈ 0.1429`
- 和 = 1.000 ✓

---

#### 4.1.6 稀有度層內職員權重（per-staff weight within rarity tier）

稀有度 roll 確定 `rolledRarity = r*` 後，於該層內依權重 roll 具體 `staffID`：

```
tierStaff[r*]    = { (sID, w) | sID ∈ eligibleStaffIDs, StaffTable[sID].rarity == r*, w = staffWeights[indexOf(sID)] }
tierWeightSum[r*] = Σ_{(sID, w) ∈ tierStaff[r*]} w

P(staffID = sID | rolledRarity = r*) = w / tierWeightSum[r*]       (for (sID, w) ∈ tierStaff[r*])
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `r*` | 上一步 roll 出的稀有度 | `{1, 2, 3, 4, 5} \ emptyTiers` |
| `w` | `StaffGachaPoolTable.staffWeights` 對應值 | 整數 `≥ 0`；`0 = 保留不抽出` |
| `tierWeightSum[r*]` | 該層總權重 | `> 0`（動態歸一化已保證層非空）|

**範例**（池 A 的 3★ 層：`eligibleStaffIDs = [101, 201, 301]`、`staffWeights = [10, 5, 0]`）：
- `tierStaff[3] = { (101, 10), (201, 5), (301, 0) }`；301 權重 0 → 不抽出
- `tierWeightSum[3] = 15`
- `P(101 | rolledRarity = 3) = 10 / 15 ≈ 0.667`
- `P(201 | rolledRarity = 3) = 5 / 15 ≈ 0.333`
- `P(301 | rolledRarity = 3) = 0`

---

#### 4.1.7 保底 counter 增量（pity counter increment）

每次 refresh（自動 / 手動 / 離線補刷）：

```
pityCounter ← pityCounter + N       // N = 該次展示張數（§4.1.1）
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `pityCounter` | `StaffPlayerState.pityCounter` 跨池累積 | `≥ 0`；觸發時 reset 為 `0`（§4.1.8）|
| `N` | 該次 refresh 張數 | 見 §4.1.1 |

**設計註記**：切池不 reset、公會升級不 reset、解除保留 / 解雇不影響。每張候選 +1（非每次 refresh +1）——避免 N=5 時保底速度與 N=3 時差太多導致體感不一致。

**範例**（`PITY_THRESHOLD = 60`）：玩家在 `guildLevel = 2`（N = 3）連刷 20 次 → `pityCounter: 0 → 3 → 6 → ... → 60`（第 20 次 refresh **後**累加達 60）。**第 21 次** refresh 開始時 §3.3.3 step 1 判定 `pityCounter ≥ 60`，首張觸發保底、命中後 `pityCounter ← 0`、之後本次 refresh 結束 step 6 累加為 `0 + N = 3`。

> **時序註記**：保底判定在 §3.3.3 step 1（roll **前**），pityCounter 累加在 §3.3.2 step 6（refresh **後**），故「第 N 次 refresh 累加達標 → **第 N+1 次** refresh 觸發」是實作的精確語意。設計意圖是「累積感知 → 下次兌現」而非「達標即時兌現」。

---

#### 4.1.8 保底觸發條件（pity trigger condition）

```
shouldForce5Star = (pityCounter ≥ PITY_THRESHOLD) AND (poolEligibleByRarity(currentPoolID, 5) ≠ ∅)
```

**觸發後行為**（僅影響該次 refresh 內**第一張**命中保底的 slot）：

```
IF shouldForce5Star:
    該 slot 的 rolledRarity ← 5（跳過 §4.1.5 動態歸一化）
    後續依 §4.1.6 在 5★ 層內 roll 具體 staffID
    pityCounter ← 0                                     // reset
ELIF (pityCounter ≥ PITY_THRESHOLD) AND (poolEligibleByRarity(currentPoolID, 5) == ∅):
    // 池中 5★ 空：本次不強制觸發，counter 繼續累積、走動態歸一化
    pityCounter 不變
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `pityCounter` | 累積計數器 | 通常 `[0, ~PITY_THRESHOLD + 5]`；5★ 空池情境可更高 |
| `currentPoolID` | 玩家當前池 | `StaffPlayerState.currentPoolID` |
| `PITY_THRESHOLD` | `StaffTuning.csv` 系統常數，預設 `60` | `[40, 100]`（§7.2） |

**設計註記**：保底**不保證連抽到**——僅該次 refresh 的第一張保證 5★，其他 slot 仍走正常 roll（可能再出 5★、也可能出 1★）。池中 5★ 空時 counter 不 reset 也不觸發，等玩家切到含 5★ 的池（或升公會等級開放 5★）才兌現——避免「5★ 空池刷新也算保底消耗」對玩家不公平。

**範例**（`PITY_THRESHOLD = 60`,假設前一次 refresh 後 pityCounter 達 60）：
- `pityCounter = 60`(refresh 結束時的累加值)、`currentPoolID = 1`（A 池含 5★） → **下次** refresh 開始時 step 1 判定觸發、第一張強制 5★、`pityCounter ← 0`
- `pityCounter = 60`、`currentPoolID = 2`（B 池本 case 5★ 空） → 不觸發、`pityCounter` 下次 refresh 繼續 `+= N`

---

#### 4.1.9 保留上限（max reserve）

```
maxReserve = max(1, interviewSlotCount - 1)
           = max(1, N - 1)
```

| 變數 | 定義 | 範圍 |
|---|---|---|
| `N` | 當前面試欄張數（§4.1.1） | `1 ≤ N ≤ 5` |
| `maxReserve` | 跨池保留區 `reservedCandidates` 的容量上限 | `1 ≤ maxReserve ≤ 4` |

**設計約束**：特例 `N = 1 → maxReserve = 1`（避免玩家無法保留任何東西）；`N ≥ 2 → maxReserve = N - 1`（強制至少留一張「現在決定」的壓力）。

**溢出處理**：嘗試保留第 `maxReserve + 1` 張時，`TryReserveCandidate()` 回 `RESERVE_FULL`；玩家必須先從保留區移除一張（錄用 / 解雇對應 `StaffInstance` / 手動取消）。

**範例**：`guildLevel = 3`、`N = 4` → `maxReserve = 3`；玩家可同時保留 3 張候選（跨 A / B 池），第 4 張嘗試保留時失敗。

---


---

## 5. 邊緣案例（Edge Cases）

### 5.1 時間異常（Time Anomalies）

| 情境 | 處理方式 |
|---|---|
| 系統時鐘倒退（now < lastAutoRefreshTimestamp）| 補刷判定使用 `Mathf.Max(0, now - last)`；負數 clamp 至 0 |
| `lastAutoRefreshTimestamp == 0`（首次 Boot）| 直接補刷一次（§3.3.5 預設行為） |
| 離線時間超過 OFFLINE_MAX_SECONDS（7 天）| 補刷次數仍只觸發 1 次（Jam 版設計，§4.1.4） |

### 5.2 面試流程（Interview Flow）

| 情境 | 處理方式 |
|---|---|
| 玩家在 candidate 還沒 roll 完時點 TryRecruit | UI 應防呆禁用；API 收到無效 candidateIndex 回 `INVALID_CANDIDATE_INDEX` |
| TryRecruit 時 FT-12 回 `STAFF_SYSTEM_LOCKED` | FT-08 視為失敗、不扣費、保留候選；LogError 記錄不一致 |
| TryRecruit 時 FT-12 回 `INVALID_STAFF_ID` | 視為失敗、保留候選、LogError；玩家可重試或 reject |
| 切池時手動刷新 cooldown 還在（如有未來新增） | 切池不重置任何計時（§3.3.1） |

### 5.3 保底（Pity）邊界

| 情境 | 處理方式 |
|---|---|
| pityCounter 超過閾值但 candidates 已被保留 | 保底命中下一次 refresh 的首個 refreshableSlot（§3.4.2） |
| 跨 session 後 pityCounter 從 SaveData 還原 | 還原即用，無需 reset；§3.4.5 規範 |
| Debug Reset Pity Counter（Editor-only） | `DebugResetPityCounter()` API 僅在 Editor 環境可呼叫；§3.4.6 規範 |

### 5.4 資料驗證邊界（DataManager）

| 情境 | 處理方式 |
|---|---|
| `StaffGachaPoolTable.minGuildLevel > maxGuildLevel` | DataManager 載入時 `LogError`、跳過該行 |
| `StaffGachaPoolTable` 5 個預留閘任一非預設值（Jam 版） | 拋 `StaffGachaPoolTableValidationException`；Jam 版強制空值 |
| `StaffRefreshCostTable[guildLevel]` 缺行 | LogError + fallback 至 `cost = 0`（玩家可免費刷新，臨時降級） |
| `StaffRarityProbTable.csv` 機率總和 ≠ 1.0 | LogWarning + 動態歸一化（§4.1.5） |
| `TrashItemTable.csv` 為空 | trash roll 跳過；1★ 層全部 staff（§3.5.3） |

### 5.5 系統邊界（System Gate）

| 情境 | 處理方式 |
|---|---|
| 未解鎖時呼叫 API | §3.6.1 全部走降級路徑、`STAFF_SYSTEM_LOCKED` |
| 解鎖瞬間 | OnStaffSystemBoot 重新觸發（含補刷判定）|
| 降級後再解鎖 | candidates / reserved 保留；下次 Boot 走過期保留掃描 |

### 5.6 公會等級下降導致 N 縮小（保留 / 候選 slotIndex 超範圍）

> Jam 版假設 `FT06.GetCurrentLevel()` **單調不減**（公會等級僅升不降，對應 FT-06 §3 設計）；但若 Post-Jam 引入降等機制,以下處理規範作為前置防禦。

| 情境 | 處理方式 |
|---|---|
| `currentCandidates[slotIndex]` 中 `slotIndex >= N`（公會降級後 N 縮小） | OnStaffSystemBoot Step C 補刷判定執行前,先 truncate `currentCandidates` 至 N 大小,溢出 slot 的候選**直接丟棄**（不轉至保留區,避免複雜的 slot 重分配邏輯）;`LogWarning("FT-08: truncated currentCandidates from M to N due to guildLevel降等")` |
| `reservedCandidates[*].slotIndex >= N`（保留卡的 slotIndex 在當前 N 範圍外） | 保留卡**仍存於 reservedCandidates**,但解除保留時若 `slotIndex >= N` 則 fallback 至「找第一個空 slot 放回」;若無空 slot 則 `LogWarning + 強制丟棄該卡 + 設 reserveConsumedFlag = true`（防玩家透過降等保留多卡） |
| 玩家 ReleaseReserveInternal 時 slotIndex >= N | 同上 fallback 邏輯;§3.3.5 ReleaseReserveInternal 內部處理 |

**規範細節**：

- **不可逆語義**:被 truncate / 強制丟棄的候選**永久消失**（含 reserveConsumedFlag = true 防止透過降等再升等繞過保留 cap）
- **與 reservedCandidates 容量上限互動**:`maxReserve = max(1, N - 1)` 在 N 縮小後也跟著縮;若降等後 `reservedCandidates.Count > maxReserve`,**保留現有卡不主動清除**（玩家未來可手動取消或時限到自然清）;但 `TryReserveCandidate` 在已超過 maxReserve 時返回 `RESERVE_FULL`

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-08 消費）

| # | 上游系統 | 消費 API / 資料 | 用途 |
|---|---|---|---|
| 1 | F-01 DataManager | `Get<StaffData>`, `GetAll<StaffData>`; `StaffTuning.csv` | StaffGachaPoolTable / StaffRefreshCostTable / StaffRarityProbTable / TrashItemTable / StaffTable（驗證 candidate.staffID） |
| 2 | FT-06 Guild Core | `GetCurrentLevel()` | StaffGachaPoolTable.minGuildLevel 過濾、StaffRefreshCostTable[guildLevel] 索引 |
| 3 | FT-07 Guild Building | `IsStaffSystemUnlocked()`, `GetBuildingState(buildingID=6).currentLevel` | 系統閘 + 職員休息室等級驅動 auto refresh interval |
| 4 | FT-12 Staff System | `HireStaff(CandidateCard) → HireResult`, `GetRecruitRefreshReductionSec()` | 錄用流程交棒 + auto refresh 加成查詢 |

### 6.2 下游消費者（FT-08 被消費）

| # | 下游系統 | 消費介面 | 用途 |
|---|---|---|---|
| 1 | FT-12 Staff System | （單向被動）FT-08 呼叫 `FT-12.HireStaff` | 接收錄用候選 |
| 2 | P-02 Main UI | 訂閱 `OnStaffSystemBoot`、查詢 6 個 `Try*` API + 候選查詢 | 面試介面、保留區、候選卡顯示 |
| 3 | P-03 Notification | 訂閱 `OnStaffSystemBoot`（可選） | 補刷完成 toast |
| 4 | FT-10 Save/Load | `ISaveable` 實作（§6.7） | 序列化 StaffPlayerState |

### 6.3 事件契約

詳見 §3.7；本表為摘要：

| 事件 | 方向 | 來源 / 訂閱者 |
|---|---|---|
| `OnStaffSystemBoot` | 出 | FT-08 → P-02 / P-03 |
| `OnBuildingUpgraded` | 入 | FT-07 → FT-08（§3.6.5 解鎖 lazy 檢測，未訂閱） |

> FT-08 透過 `FT-12.HireStaff` 同步 API 觸發 FT-12 的 `OnStaffHired` 事件，但 FT-08 不擁有該事件、不訂閱、不轉發。

### 6.4 資料表依賴

| 表格 | Owner | FT-08 用途 |
|---|---|---|
| `StaffGachaPoolTable.csv` | FT-08 owner | 池架構 / 閘 / staffWeights / reserveTimeLimitSec |
| `StaffRefreshCostTable.csv` | FT-08 owner | 手動刷新費（per guildLevel） |
| `StaffRarityProbTable.csv` | FT-08 owner | 稀有度機率（動態歸一化前的 base） |
| `TrashItemTable.csv` | FT-08 owner | 1★ 層 trash items |
| `StaffTuning.csv` | **FT-08 / FT-12 共用 owner** | FT-08 註冊 `PITY_THRESHOLD` / `TRASH_ROLL_RATE_AT_RARITY_1` / `INTERVIEW_AUTO_REFRESH_INTERVAL_*` / `INTERVIEW_SLOT_COUNT_*`；FT-12 註冊 effect 聚合上限 / cooldown 等職員運營常數 |
| `StaffTable.csv` | FT-12 owner | FT-08 透過 staffWeights / eligibleStaffIDs FK 引用驗證 candidate.staffID |

### 6.5 共享常數（SystemConstants.csv / StaffTuning.csv）

| 常數 | 預設值 | 用途 |
|---|---|---|
| `TRASH_ROLL_RATE_AT_RARITY_1` | `0.30` | 1★ 層 trash 觸發機率 |
| `MAX_RESERVE_FALLBACK` | `1` | 面試欄 = 1 時的特例 maxReserve |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1` ~ `L5` | `86400` ~ `21600` | 自動刷新間隔（職員休息室等級驅動） |

### 6.6 雙向聲明同步表

| 對端 GDD | FT-08 對端應有的反向登記 |
|---|---|
| `[FT-12]` | §6 上游列出 FT-08 為 `HireStaff` 呼叫者；下游列出 FT-08 為 `GetRecruitRefreshReductionSec` 消費者 |
| `[FT-07]` | §6 下游列出 FT-08 為 `IsStaffSystemUnlocked` / 職員休息室等級消費者 |
| `[FT-06]` | §6 下游列出 FT-08 為 `GetCurrentLevel` 消費者 |
| `[F-01]` | §6.2 下游列出 FT-08 為 4 個 owner CSV 的查詢者 |
| `[FT-09]` | §設計來源聲明：5 個預留閘 Post-Jam 才消費（已存在於 FT-09 §6.4） |
| `[FT-10]` | §6.4 反向依賴清單列出 FT-08 為 `ISaveable` owner（Critical） |

### 6.7 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft08Gacha"` |
| `IsCritical` | `true`（StaffPlayerState 缺失導致 candidate / pity / 保留全部遺失，整檔回退；CandidateCardValidationException fail-fast 由本系統觸發） |

**`Serialize()` 序列化欄位**：

| 欄位 | 型別 | Unity 序列化中介 | 說明 |
|---|---|---|---|
| `_playerState` | `StaffPlayerState` | `[Serializable] class` | 單一序列化容器，內含 §3.1 定義的 5 欄位:`pityCounter` / `lastAutoRefreshTimestamp` / `currentPoolID` / `currentCandidates: List<CandidateCard>` / `reservedCandidates: List<CandidateCard>` |

> **單一來源原則**:`StaffPlayerState` 結構（§3.1）已包含 `currentCandidates` / `reservedCandidates`,FT-08 持有單一 `_playerState` 私有欄位作為所有 gacha runtime state 的真實來源;不另外維護 `_currentCandidates` / `_reservedCandidates` 並列欄位（避免雙重資料 / 序列化重複）。

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化 `_playerState`(內含 6 欄位)。
2. 對 `_playerState.currentCandidates` 與 `_playerState.reservedCandidates` 中每筆 candidate.staffID 透過 `F-01.Get<StaffData>(staffID)` 驗證；若 `staffID > 0` 但 `rolledRarity != StaffTable[staffID].rarity` 則拋 `CandidateCardValidationException`（FT-10 critical 路徑）。
3. trash candidate（`staffID == 0` 且 `trashItemID > 0`）驗證 `TrashItemTable[trashItemID]` 存在；不存在拋例外。
4. 還原完成後執行 OnStaffSystemBoot Step B（過期保留掃描,§3.3.5）。

**`InitializeAsNewGame()` 預設值**（建立空 `StaffPlayerState`）：

| `_playerState` 欄位 | 初始值 |
|---|---|
| `pityCounter` | `0` |
| `lastAutoRefreshTimestamp` | `0` |
| `currentPoolID` | `1`（A 池） |
| `currentCandidates` | 空列表（首次 `OnStaffSystemBoot` 補刷後填滿 N 張） |
| `reservedCandidates` | 空列表 |

對應 FT-10 §3.3.3 拓撲順序 row（FT-08 / FT-12 兩個 critical owner）、§3.3.4 Critical 分類、§6.1（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 資料表參數（CSV）

#### 7.1.1 `StaffRefreshCostTable.csv`

| 欄位 | 型別 | 用途 | 安全範圍 |
|---|---|---|---|
| `guildLevel` | int (PK) | 公會等級 | `[1, 5]` |
| `cost` | int | 手動刷新費 | `[0, 5000]`；過低失去金幣壓力、過高玩家不刷 |

#### 7.1.2 `StaffRarityProbTable.csv`

| 欄位 | 型別 | 用途 | 安全範圍 |
|---|---|---|---|
| `rarity` | int (PK) | 1~5 | 固定 |
| `prob` | float | 稀有度基礎機率 | `[0.0, 1.0]`；總和 ≠ 1.0 時動態歸一化 |

預設：1★=0.40, 2★=0.25, 3★=0.15, 4★=0.15, 5★=0.05

#### 7.1.3 `StaffGachaPoolTable.csv`

| 欄位 | 型別 | 用途 | 安全範圍 |
|---|---|---|---|
| `poolID` | int (PK) | 池識別 | `[1, ∞)` |
| `minGuildLevel` / `maxGuildLevel` | int | 主閘 | `[1, 5]`，`min ≤ max` |
| `eligibleStaffIDs` | int 多值 | 該池可 roll 的 staffID 集合 | 至少 1 個 |
| `staffWeights` | int 多值 | 平行 weight（允許 0） | `[0, 1000]` 每位 |
| `reserveTimeLimitSec` | int | 保留時限 | `[3600, 604800]`（1h~7d） |
| 5 個預留閘欄位 | - | Post-Jam 啟用 | Jam 版強制空值 |

#### 7.1.4 `TrashItemTable.csv`

| 欄位 | 型別 | 用途 | 安全範圍 |
|---|---|---|---|
| `trashItemID` | int (PK) | trash 識別 | `[1, ∞)` |
| `name` / `description` | string | UI 顯示 | 含義要與 trash 主題一致（咖啡杯、履歷紙團等） |

### 7.2 系統常數（StaffTuning.csv / SystemConstants.csv）

| key | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|
| `PITY_THRESHOLD` | `60` | `[40, 100]` | 保底觸發閾值（§4.1.8 / §3.4）；過低（< 40）讓 5★ 太常見失去稀有性、過高（> 100）失去保底意義 |
| `TRASH_ROLL_RATE_AT_RARITY_1` | `0.30` | `[0.0, 1.0]` | 1★ 層 trash 機率；過低（=0）失去 trash 紋理、過高（>0.5）惹人厭 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L1` | `86400`（24h）| `[3600, 86400]` | L1 自動刷新間隔 |
| `INTERVIEW_AUTO_REFRESH_INTERVAL_L5` | `21600`（6h）| `[3600, 86400]` | L5 自動刷新間隔（最短） |
| `INTERVIEW_SLOT_COUNT_L1` ~ `L5` | `3` ~ `7` | `[1, 10]` | 面試欄張數（職員休息室等級遞增） |

### 7.3 FT-07 擁有的 FT-08 相關參數

| 參數（位於 BuildingTable）| 用途 |
|---|---|
| `BuildingTable[buildingID=6].levelEffects[L].slotCount`（職員休息室） | 面試欄張數（透過 §4.1.1 公式映射） |
| `BuildingTable[buildingID=6].levelEffects[L].refreshInterval`（職員休息室） | 自動刷新間隔（驅動 §4.1.3） |

### 7.4 不可調參數（設計決策硬寫）

| 項目 | 值 | 理由 |
|---|---|---|
| 池切換副作用（覆蓋 currentCandidates） | 固定 | 跨池保留通道為單一（reservedCandidates） |
| OwnerKey `"ft08Gacha"` | 固定 | SaveData root JSON 子區塊 key |
| trash 計入 pityCounter | 固定 | 設計動機:trash 不該被當「保底逃逸路徑」 |

### 7.5 平衡建議（Designer Notes）

- `TRASH_ROLL_RATE_AT_RARITY_1 = 0.30` 是「世界觀紋理」與「玩家挫折」的平衡點;low (< 0.20) 失去趣味、high (> 0.50) 變成挫折來源
- `INTERVIEW_AUTO_REFRESH_INTERVAL_L1 = 86400`（每天 1 次）對應「玩家每天上線一次」的節奏；L5 = 21600 對應「上線多次」的核心玩家
- 保底機制 `PITY_THRESHOLD = 60` 對應「累積 60 張候選後觸發」；玩家在 N=5 面試欄下平均每 12 次 refresh 觸發一次保底,符合「玩家上線多次」的長期累積節奏。安全範圍見 §7.2
- **Trash 疊乘機率觀察**：1★ baseProb = 0.40 × `TRASH_ROLL_RATE_AT_RARITY_1` = 0.30 = trash 全體機率約 0.12（每 refresh N=5 平均 0.6 張）。連續 2~3 次 refresh 都遇 ≥1 張 trash 的機率約 35~50%；若 playtest 反映玩家挫折,優先調 `TRASH_ROLL_RATE_AT_RARITY_1` 至 0.20 而非加防呆機制（防呆會破壞「世界觀紋理」設計意圖）

---

## 8. 驗收標準（Acceptance Criteria）

### 8.1 系統初始化與閘控

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-1 | 職員休息室未建造 | 全 `Try*` API 回 `STAFF_SYSTEM_LOCKED`；OnStaffSystemBoot payload `isUnlocked = false` | EditMode |
| AC-2 | 職員休息室升至 L1 | OnStaffSystemBoot 重新觸發；候選欄就緒（`currentCandidates` 從空到 N 張） | PlayMode |

### 8.2 面試刷新（Refresh Flow）

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-3 | 玩家點手動刷新 | 扣 `StaffRefreshCostTable[guildLevel].cost`、N 張候選全覆蓋、pityCounter += N | EditMode |
| AC-4 | auto refresh 間隔到（OnStaffSystemBoot Step C） | 補刷一次、`lastAutoRefreshTimestamp ← now` | EditMode |
| AC-5 | 離線多個 auto interval | Boot 階段僅補刷 1 次（不疊加） | EditMode |

### 8.3 池切換與保留（Pool Switch & Reserve）

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-6 | 玩家切池 | currentCandidates 全部覆蓋；reservedCandidates 跨池存活 | EditMode |
| AC-7 | 保留候選 | 加入 reservedCandidates、設 reserveExpireTimestamp | EditMode |
| AC-8 | reserveTimeLimit 到期 | OnStaffSystemBoot Step B 自動釋放 | EditMode |
| AC-9 | reserveConsumedFlag 防刷 | 已釋放的 candidate 永久不可再保留 | EditMode |

### 8.4 稀有度 / 保底（Rarity / Pity）

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-10 | 稀有度層全空 | 動態歸一化重新分配機率（§4.1.5） | EditMode |
| AC-11 | pityCounter 達閾值 | 下次 refresh 首張強制 5★ | EditMode |
| AC-12 | 保底命中後 | pityCounter 重置為 0、後續 roll 回正常機率 | EditMode |

### 8.5 錄用 / 不錄用（Hire / Reject）

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-13 | 玩家點錄用 | FT-08 呼叫 `FT-12.HireStaff(candidate)`；FT-12 回 OK 後從 currentCandidates 移除 | EditMode |
| AC-14 | FT-12 回 `STAFF_SYSTEM_LOCKED` | FT-08 視為失敗、保留候選、LogError | EditMode |
| AC-15 | 玩家點不錄用 | candidate 從 currentCandidates 移除（slot 空置至下次刷新） | EditMode |

### 8.6 資料驗證（DataManager）

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-16 | StaffGachaPoolTable 5 預留閘任一非預設 | 拋 StaffGachaPoolTableValidationException、跳過該行 | EditMode |
| AC-17 | StaffGachaPoolTable.minGuildLevel > maxGuildLevel | LogError、跳過該行 | EditMode |
| AC-18 | StaffRarityProbTable 機率總和 ≠ 1.0 | LogWarning + 動態歸一化 | EditMode |

### 8.7 事件契約

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-19 | OnStaffSystemBoot payload 完整 | 含 isUnlocked / appliedRefreshCount / releasedReserveCount / bootTimestamp | EditMode |
| AC-20 | 降級時 OnStaffSystemBoot 仍發布 | payload `isUnlocked = false` | EditMode |

### 8.8 持久化

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-21 | Save → reload | StaffPlayerState 完整還原（含 pityCounter / candidates / reserved） | EditMode |
| AC-22 | candidate.staffID 還原時 rolledRarity != StaffTable.rarity | 拋 CandidateCardValidationException | EditMode |
| AC-23 | trash candidate（staffID=0、trashItemID>0）還原時 trashItemID 不存在 | 拋例外 | EditMode |

### 8.9 效能與規模

| AC | 條件 | 預期 | 驗證 |
|---|---|---|---|
| AC-24 | 單次 refresh（5 張候選）| < 5ms | Profiler |
| AC-25 | 保留區 7 張 + 候選區 5 張 | OnStaffSystemBoot 全流程 < 10ms | Profiler |

---
