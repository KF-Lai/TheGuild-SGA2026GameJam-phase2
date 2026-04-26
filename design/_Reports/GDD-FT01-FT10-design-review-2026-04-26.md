# GDD FT-01 ~ FT-10 設計審查總報告

_審查日期：2026-04-26_
_審查者：Claude Opus 4.7 + max effort thinking_
_範圍：FT-01 Adventurer Recruitment ~ FT-10 Save/Load System_
_前置基礎：F-01 / F-02 / F-03 / C-01 / C-02 / C-06 已讀完作為一致性參考_

---

## 審查總覽

| GDD | 完整性 | Critical | Major | Minor | Verdict |
|---|---|---|---|---|---|
| FT-01 Adventurer Recruitment | 8/8 | 0 | 4 | 6 | NEEDS REVISION |
| FT-02 Mission Dispatch | 8/8 | 1 | 2 | 2 | NEEDS REVISION |
| FT-03 NPC Decision System | 8/8 | 2 | 2 | 3 | NEEDS REVISION |
| FT-04 Outcome Resolution | 8/8 | 0 | 2(連動) | 1 | APPROVED with notes |
| FT-05 Guild Gold Flow | 8/8 | 0 | 0 | 2 | **APPROVED**(品質典範) |
| FT-06 Guild Core | 8/8 | 0 | 3 | 1 | NEEDS REVISION |
| FT-07 Guild Building System | 8/8 | 1 | 1 | 2 | NEEDS REVISION |
| FT-08 Guild Staff System | 8/8 | 0 | 3 | 1 | NEEDS REVISION |
| FT-09 Faction Story System | 8/8 | 1 | 0 | 2 | NEEDS REVISION |
| FT-10 Save/Load System | 8/8 | 0 | 3 | 1 | NEEDS REVISION |

**整體評價**:
- 所有 10 份 GDD 完整性 8/8(8 段標準全到位)
- FT-05 Guild Gold Flow 為設計品質典範,他份 GDD 應參考其 Edge Cases 編號、反向依賴 checklist、Phase 2 範疇分離標註
- 多數 Critical/Major 為**跨系統契約矛盾**而非單一 GDD 設計缺陷
- 所有 GDD 都已寫得很完整,核心循環與架構設計清晰

---

## Critical 問題清單(必須在實作前解決)

### C1: FT-02 ↔ F-02 雙重計時器機制衝突

**位置**: F-02 §3.6 / §6.2 / §6.3、FT-02 §3.7 / §6.2、FT-04 §6.1、FT-10 §3.3.3

**問題**:
- F-02 §3.6 設計 `RegisterMission(missionInstanceId, dispatchTimestamp, durationSeconds)` / `UnregisterMission(missionInstanceId)` API
- F-02 §6.3 定義 `OnMissionExpired(missionInstanceID)` 事件,§6.2 列 FT-04 為訂閱者
- FT-02 §3.7 設計自己訂閱 `OnSecondTick`,維護 `activeMissions` 並發 `OnMissionCompleted(activeMissionID)`
- FT-02 §6.2 列 FT-04 訂閱 `OnMissionCompleted`(不是 OnMissionExpired)
- FT-10 §3.3.3 row 6 又假設 F-02 RegisterMission 機制存在

**影響範圍**: F-02、FT-02、FT-04、FT-10 — 4 份 GDD

**建議解決方向**: **FT-02 為單一計時/發布點**(activeMissions 本就是任務狀態源,維持現狀);F-02 移除以下:
- §3.6 計時器清單 + RegisterMission/UnregisterMission API
- §6.3 OnMissionExpired 事件
- §6.2 中 FT-04 為訂閱者的條目

FT-04 統一訂閱 FT-02 的 `OnMissionCompleted`(已是現況);FT-10 §3.3.3 row 6 改寫為「FT-02 還原 activeMissions 後自行重新訂閱 OnSecondTick」。

**規模**: **Large**(跨 4 份 GDD 統一)→ 派 Sonnet subagent 處理

---

### C2: FT-03 委託板服務歸屬未定

**位置**: FT-03 §3.3、C-01 §1、FT-05 §1

**問題**:
- FT-03 §3.3 自主接單流程引用 `MissionBoard.GetAvailable()`(取已審核、未派遣、難度合格的可用任務)
- C-01 §1 寫「委託板的當前任務清單由 FT-05 Commission Flow 管理」
- FT-05 在 phase 2 改名為 Guild Gold Flow,§1 範疇明確排除「委託池管理」

**結論**: 委託板的 runtime 任務池**沒有任何系統明確負責管理**

**影響範圍**: FT-03(自主接單)、可能 P-02(展示)、可能新增系統

**建議解決方向**: 兩種選項
- **Option A**: FT-02 新增「委託板池」管理職責(`_commissionBoard: List<CommissionEntry>`、`GetAvailable()` API、`OnCommissionPosted` 事件供 P-02);這也呼應 FT-09 C5 的 `InjectStaticMission` 功能
- **Option B**: 作為 P-02 Main UI 的純 view-side 池,由 P-02 訂閱所有委託生成事件自行維護

**推薦 Option A**(同一個池可同時容納常規生成委託與靜態劇情委託,FT-09 InjectStaticMission 自然落地)

**規模**: **Medium**(FT-02 補章節 + 修整相關 GDD)→ 派 Sonnet subagent

---

### C3: FT-03 idleSinceTimestamp / lastAutoPickupTimestamp 欄位歸屬

**位置**: FT-03 §3.3 + §6.2、C-02 §3.1

**問題**:
- FT-03 §3.3 要求 AdventurerInstance 攜帶 `idleSinceTimestamp` 與 `lastAutoPickupTimestamp` 欄位
- FT-03 §6.2 寫「FT-10 透過 C-02 AdventurerInstance 一併序列化」
- C-02 §3.1 AdventurerInstance 規格**未列**這兩個欄位、§3.4 狀態轉移規則也沒提到設定/清除時機

**影響**: FT-03 自主接單流程無法實作(欄位不存在);FT-10 序列化契約落空

**建議解決方向**: C-02 §3.1 補兩個欄位;§3.4 補規則:
- 進入 `Idle` 狀態時設 `idleSinceTimestamp = NowUTC`
- 離開 `Idle` 狀態(派遣/Wounded/Dead)時清除
- `lastAutoPickupTimestamp` 由 FT-03 主動寫入(透過新增 C-02 setter)

**規模**: **Medium**(C-02 GDD 修改)→ 派 Sonnet subagent

---

### C4: FT-07 GetBankruptcyWarningSeconds vs F-03 BankruptcyThresholdTable 權威衝突

**位置**: FT-07 §3.4 / §3.5 / §6.2 / §7.1 vs F-03 §3.6 / §4.5 / §7.2

**問題**:
- FT-07 §3.5 透過 BuildingTable[5](預備金保險櫃)依等級提供 `GetBankruptcyWarningSeconds()`(L1=3h ~ L5=48h)
- F-03 §3.6 / §4.5 仍用 `BankruptcyThresholdTable.GetWarningDuration(currentReputation)` 依聲望段查詢(惡名 24h ~ 頂級 7d)
- F-03 §7.2 BankruptcyThresholdTable 還在,F-03 GDD 完全沒提到 FT-07 接管

**結論**: 兩個系統都聲稱自己是「破產倒數秒數」的權威來源

**建議解決方向**: 從遊戲幻想看,FT-07 模式更貼合「投資建設可緩解破產壓力」;採類似 SetBankruptcyThreshold 的方案A 推送模式:
- F-03 廢除 BankruptcyThresholdTable 的 warningDurationSec 概念,新增 `SetBankruptcyWarningDuration(int seconds)` 寫入 API
- FT-07 在啟動 + 保險櫃升級時呼叫 `F-03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())`
- F-03 §7.2 移除 BankruptcyThresholdTable(或保留 reputationMin/reputationMax 但移除 warningDurationSec)
- F-03 §3.6 rule 2 / §4.5 改為「進入 Warning 時鎖定的 _warningDurationSec 來自 _currentWarningDuration(由 FT-07 推送的當前值)」

**規模**: **Medium**(F-03 + FT-07 對齊)→ 派 Sonnet subagent

---

### C5: FT-09 InjectStaticMission API 缺失

**位置**: FT-09 §3.5.3 + §6.4 #5、FT-02 §3

**問題**:
- FT-09 §3.5.3 + §6.4 #5 明確要求 FT-02 新增 §3.x 章節:`InjectStaticMission(missionID) → InjectStaticMissionResult` API + `_staticMissionPool: List<int>` runtime 結構 + `OnStaticMissionPosted(missionID)` 事件 + 派遣後從池移除規則
- FT-02 GDD §3 完全沒這些

**影響**: FT-09 §3.5 雙軌呈現流程整段無法實作

**建議解決方向**: FT-02 補 §3.9 章節定義 `InjectStaticMission` API + `_staticMissionPool` + `OnStaticMissionPosted` 事件;與 C2(委託板歸屬)合併處理為理想

**規模**: **Medium**(與 C2 合併處理)→ 派 Sonnet subagent

---

## Major 問題清單

### M1: FT-01 §4.4 老手階級權重未表格化

- §4.4 hardcoded `{D:40, C:30, B:18, A:9, S:3}`
- 違反 memory `feedback_gates_data_driven` + `feedback_data_driven`
- **建議**: 建立 `VeteranRankWeightTable.csv`(rank PK + weight),§4.4 改用 CSV
- **規模**: Small → 主體直改

### M2: FT-01 候選池實例化機制 + C-02 AddAdventurer isUnique + OnRecruitSuccess 事件三連動

- FT-01 §4.5 Phase 2 直接 `NewAdventurerInstance(...)` 自建 instance,繞過 C-02 instanceID 分配機制
- FT-01 §5.2 邊緣案例假設 `C02.AddAdventurer` 會做 isUnique 攔截,但 C-02 §3.5 規格未含
- FT-10 §3.2.1 訂閱 `OnRecruitSuccess` 事件,但 FT-01 GDD 沒定義
- **建議**:
  1. C-02 提供 `CreateRandomInstance(rank, professionID, raceID, traitIDs)` API,分配 instanceID 但不加入名冊
  2. C-02 §3.5 `AddAdventurer` 補 isUnique 檢查
  3. FT-01 §3.10 補事件契約表,定義 `OnRecruitSuccess(adventurerInstanceID, source)`
- **規模**: Medium(連動 C-02 + FT-01) → 派 Sonnet

### M3: FT-06 GetMaxDifficulty 語義誤導

- FT-01 §3.4/§4.4 用 `FT06.GetMaxDifficulty()` 當「老手招募階級上限」(D~S)
- FT-06 §3.5 Lv5 maxDifficulty = `S`,但 C-06 MissionPoolWeights 在 C/B/A 階都有 `weightS_SSS` > 0,意味 SS/SSS 任務會生成卻無人能接
- API 名稱「GetMaxDifficulty」混淆「任務難度上限」與「冒險者招募階級上限」
- **建議**: FT-06 拆分:
  - `GetMaxRecruitableRank() : string`(回傳 D~S,FT-01 用)
  - `GetMaxMissionDifficulty() : string`(回傳 D~SS,常規任務上限)
  - FT-06 §3.5 GuildLevelTable 補欄位 `maxRecruitableRank`(F~S)、`maxMissionDifficulty`(F~SS)
- **規模**: Medium(FT-06 + FT-01 對齊) → 派 Sonnet

### M4: FT-06 GuildLevelTable 與 game-concept 不一致

- threshold(100/300/700/1500 vs 30/60/100/150)、稱號全改、maxDifficulty(S vs SSS)
- **建議**: 以 FT-06 為當前權威;game-concept §公會等級表 加註「Phase 1 legacy,實際以 FT-06 為準」
- **規模**: Small → 主體直改

### M5: FT-07 §3.5 職員休息室 L2+「TBD」 vs §7.1 完整資料表

- §3.5 寫 L2+「待 FT-08 後補」,§7.1 完整 BuildingTable 已有 L2~L5 具體值
- **建議**: §3.5 更新對齊 §7.1
- **規模**: Small → 主體直改

### M6: FT-07 §5.5 邊緣案例邏輯混亂

- 「effectData[5] 不存在拋例外」與「讀檔 clamp 至 maxLevel」兩個處理規則並存
- **建議**: 統一為「讀檔後 clamp 至 maxLevel + LogWarning」(避免後續查詢失敗)
- **規模**: Small → 主體直改

### M7: FT-08 §6.1 ID 標錯 + 不存在的 API

- 把 F-01 DataManager 寫成「F-03 DataManager」、把 F-03 ResourceManagement 寫成「F-01 ResourceManagement」
- 用 `TryDeductGold(amount): bool`(F-03 不存在的 API);應用 `CanAfford(cost) + AddGold(-cost)` 模式
- **建議**: 直改 §6.1
- **規模**: Small → 主體直改

### M8: FT-08 §6.1 缺 FT-06 上游依賴

- §3.3.4 用 `currentGuildLevel` 過濾池,但 §6.1 沒列 FT-06.GetCurrentLevel
- **規模**: Small → 主體直改

### M9: FT-08 §6.6 雙向聲明同步表狀態過時

- FT-01/FT-05 標 ⬜,但 A.4 寫入紀錄已完成
- **規模**: Small → 主體直改

### M10: FT-02 §3.5 step 5 缺 SetDispatched API

- FT-02 §3.5 step 5 直接 `adventurer.currentMissionID = activeMissionID`,違反 C-02 封裝
- C-02 §3.5 沒有對應 setter
- **建議**: C-02 提供 `UpdateStatus(instanceID, Dispatched, missionInstanceID = 0)` overload 或新增 `SetDispatched(instanceID, missionInstanceID)`
- **規模**: Small(連動 C-02 + FT-02 微調) → 主體直改

### M11: FT-03 §6.1 缺 FT-06 依賴 + §5.2 與 F-02 OnMinuteTick 規範不一致

- §3.3 過濾「難度 ≤ 公會等級上限」缺 FT-06 引用,§6.1 未列
- §5.2「重啟後第一次 OnMinuteTick 補算」與 F-02 §3.2 rule 6「離線不補發 OnMinuteTick」直接矛盾
- **建議**: §6.1 補 FT-06.GetMaxDifficulty;§5.2 改為「重啟後依正常節奏接收 OnMinuteTick;Jam 階段離線期間 NPC 自主接單由 FT-11 接管暫停(對應 F-02 §3.2 rule 6 與 FT-11 規範)」
- **規模**: Small → 主體直改

### M12: FT-04 §3.2 step 7 與 C-05 簽名(連動)

- §3.2 step 7 `C05.ApplyConditionTraits(outcome, adventurer.traitIDs)` 兩參數
- FT-05 跨系統審查 Minor 紀錄 C-05 §4.4 是三參數(含 missionReward)
- 需到 C-05 確認當前簽名;若 C-05 已對齊兩參數,則此 M12 解除
- **規模**: Small(讀 C-05 確認) → 主體直查

### M13: FT-10 ISaveable 介面各 owner 缺實作(9 個 GDD)

- FT-10 §3.6.2 要求 F-03 / C-02 / C-06 / FT-01 / FT-02 / FT-06 / FT-07 / FT-08 / FT-09 都實作 ISaveable 介面(`OwnerKey`/`IsCritical`/`Serialize`/`RestoreFromSave`/`InitializeAsNewGame`)
- 各 owner GDD §3 / §6 都沒此章節
- §6.4 已列為待補 15 個 GDD(批次處理計畫)
- **規模**: Large(9 個 GDD 補同一模式章節) → 派 Sonnet subagent

---

## Minor 問題清單(主體可直改的小修)

### FT-01
- m1 §3.6 缺 `OnPoolRefreshed` 事件契約表
- m2 §3.2 應補一句 FT-08 加成影響的描述
- m3 §3.5 流程缺 `rosterCap = FT07.GetRosterCap()` 呼叫
- m4 §7 缺 `MIN_RECRUIT_REFRESH_INTERVAL_SEC` Tuning Knob
- m5 AC-AR-18 用 `GetBuildingLevel(6)` 判 FT-08 解鎖,與 §4.1 註解 `IsStaffSystemUnlocked()` 不一致
- m6 §3.3/§4.5 對 `isUnique=0` 具名模板的處理不明確

### FT-02
- m1 §4.2 用 `trait.effectType == "stat"` magic string
- m2 §5.3 缺 step 3 `OnCommissionAccepted` 發布後失敗的 rollback 說明

### FT-03
- m1 §3 缺正式查詢 API 表(`MakeDecision` 簽名)
- m2 §3.1 拒絕原因判定的三階段 willingnessScore 中間值區分不夠明確
- m3 §4.2 effectTarget 用 magic string

### FT-04
- m1 §3.2 step 2 後應 explicit 加「驗證 adventurer.status == Dispatched」

### FT-05
- m1 §5.4.4 `effectiveCommissionRate > 1.0` 無上限保護(設計選擇,Jam 不擋)
- m2 §3.4 conditionGoldBonus 取整策略未明確(連動 FT-04 M12)

### FT-06
- m1 §5.4.3 Rich Text 標籤對白名單處理可能 over-engineering(設計選擇)

### FT-09
- m1 §6.4 9 個 GDD 雙向依賴登記(批次處理計畫)
- m2 命名一致性:systems-index 仍寫 `StoryNodeTable`,FT-09 改名為 `StoryStageTable`

### FT-10
- m1 §6.4 反向依賴 15 個 GDD(批次處理計畫)

---

## 修改執行計畫

### 第 1 批: Small 修改(主體直改)

| # | 任務 | GDD | 預估動作 |
|---|---|---|---|
| 1 | M1 建立 VeteranRankWeightTable.csv + FT-01 §4.4 改寫 | FT-01 + 新表 | 建表 + 改章節 |
| 2 | M4 game-concept 公會等級表加註 legacy | game-concept | 加註 |
| 3 | M5 FT-07 §3.5 職員休息室 L2~L5 數值對齊 §7.1 | FT-07 | 改章節 |
| 4 | M6 FT-07 §5.5 邊緣案例邏輯統一 | FT-07 | 改章節 |
| 5 | M7 FT-08 §6.1 ID 標錯與 TryDeductGold | FT-08 | 改章節 |
| 6 | M8 FT-08 §6.1 補 FT-06 上游 | FT-08 | 改章節 |
| 7 | M9 FT-08 §6.6 雙向聲明同步 | FT-08 | 改章節 |
| 8 | M11 FT-03 §6.1 + §5.2 對齊 | FT-03 | 改章節 |
| 9 | M12 確認 C-05 §4.4 簽名(可能已對齊) | C-05 讀取 | 讀取確認 |
| 10 | Minor 文字修正(各 GDD) | 全部 | 微改 |

### 第 2 批: Medium/Large 修改(派 Sonnet subagent)

| # | 任務 | 影響 GDD | 規模 |
|---|---|---|---|
| A | C1 雙重計時器:F-02 移除 RegisterMission/OnMissionExpired,FT-02 為單一發布點 | F-02、FT-02、FT-04、FT-10 | Large |
| B | C2 + C5 委託板服務歸屬 + InjectStaticMission API | FT-02 (補章節)、FT-09(對齊) | Medium |
| C | C3 idleSinceTimestamp 欄位歸屬 | C-02(補欄位 + 規則) | Medium |
| D | C4 破產倒數秒數權威衝突 | F-03、FT-07 | Medium |
| E | M2 候選池實例化 + AddAdventurer isUnique + OnRecruitSuccess 事件 | C-02、FT-01、FT-10 | Medium |
| F | M3 GetMaxDifficulty 語義拆分 | FT-06、FT-01 | Medium |
| G | M10 SetDispatched API(C-02 + FT-02 微調) | C-02、FT-02 | Small/Medium |
| H | M13 FT-10 ISaveable 介面 9 個 owner 補章節 | F-03、C-02、C-06、FT-01、FT-02、FT-06、FT-07、FT-08、FT-09 | Large |

### 重大爭議(寫進本報告供使用者複審)

審查過程中沒有「無法決定」的重大爭議。所有 Critical/Major 問題我都做了具體建議:
- C1 → FT-02 為單一發布點(建議移除 F-02 計時器機制)
- C2 → Option A:FT-02 新增委託板池
- C3 → C-02 補欄位
- C4 → FT-07 模式為主,F-03 改被動接收
- C5 → 與 C2 合併

如果使用者醒來複審後不同意上述任一方向,可在 Reports 中追加意見並要求調整。

---

## 與 FT-05 跨系統審查報告對照

FT-05 跨系統審查(2026-04-22)發現的 C1/C2/C3 已全部修補:
- C1 F-03 `AddGoldAllowBankruptcy` ✅(F-03 §3.2 rule 7 + §4.1a)
- C2 `OnCommissionAccepted` 發布者 ✅(FT-02 §3.5 step 3)
- C3 `OnMinuteTick` 事件 ✅(F-02 §3.2 rule 5-6)

FT-05 跨系統審查 M1(反向依賴登記)已部分完成:
- F-03 / FT-02 / FT-03 / FT-04 / FT-08 已落地 ✅
- FT-07 / P-02 / P-03 / FT-11 待設計

---

## 整體建議

1. **第 1 批 Small 修改**主體立即執行(預估 ~30 個 Edit)
2. **第 2 批 Medium/Large** 任務 A~H 派 Sonnet subagent 並行/序列執行
3. **二次主體 review**:所有修改後我親自重審
4. **Codex 終審**:傳給 Codex 以資深遊戲工程師 + 程式實作角度審視
5. **Codex 回饋處理**:依規模分級,Small 主體直改、Medium/Large 派 Sonnet,改完主體再 review

---

_本報告已彙整 10 份 FT GDD 的所有審查發現。所有 Critical 與 Major 問題的解決建議均經 Opus + max effort thinking 推導,符合既有 GDD 架構與設計原則。如使用者醒來複審後對任一方向有異議,可調整對應任務並重新派發。_
