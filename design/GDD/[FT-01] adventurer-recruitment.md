# Adventurer Recruitment 系統設計文件

_建立時間：2026-04-21_
_狀態：設計中_
_系統 ID：FT-01_

---

## 1. 概要（Overview）

FT-01 Adventurer Recruitment 負責冒險者招募的完整流程。系統維護兩個平行的候選池：**新手池**（F/E 階）與**老手池**（D~S 階），分開顯示於 UI，但共用相同的刷新機制。

**刷新規則（兩池共用）**：系統依招募廣告欄建設等級定時自動刷新（L1 24h / L2 16h / L3 8h，數值以 FT-07 `BuildingTable` 為準），每次刷新兩個池子各生成 `RECRUIT_POOL_SIZE`（4）名候選冒險者，未被接納的候選者直接消失。每日 00:00 重置 1 次免費手動刷新機會（兩池共用），使用後額外手動刷新費用為 `REFRESH_COST`（150g/次）。手動刷新時兩個池子同時刷新。

**新手池**（F/E 階）：玩家從候選池中**免費**接納冒險者加入名冊。候選冒險者由 C-02 `CreateFromTemplate` 或隨機生成（職業從 C-03 `GetBaseProfessions` 抽取，種族由 C-04 `RollRace` 決定，特質由 C-05 `RollTraits` 抽取），階級為 F 或 E 隨機。

**老手池**（D~S 階）：顯示可邀請的老手列表及各自的邀請費用（`RecruitCostTable`）。玩家選擇後，系統驗證金幣（F-03 `CanAfford`）與聲望門檻（F-03 `GetReputation` ≥ `reputationReq`），通過後扣款並加入名冊。老手候選的階級由加權隨機決定（D:40% / C:30% / B:18% / A:9% / S:3%），受 FT-06 Guild Core 的 `maxDifficulty` 限制（公會等級決定可招募的最高階級）。邀請次數無每日上限，只要金幣與聲望足夠即可持續邀請。

兩個池子共用名冊容量檢查：C-02 `IsRosterFull(rosterCap)` 為 `true` 時，兩邊均不可加入新成員。FT-01 不管理名冊本身（C-02 負責）、不計算成功率（FT-02 負責）、不直接操作金幣/聲望數值（透過 F-03 API）。

## 2. 玩家幻想（Player Fantasy）

招募是公會長最私人的時刻——你在挑選未來的夥伴。

新手池是期待與驚喜的來源。每次刷新就像打開公會大門，看見一群帶著夢想的年輕人排隊等候。他們階級不高，但每個人的職業、種族、特質組合都是獨一無二的。玩家會開始想像：「這個精靈遊俠看起來不錯，但他有『膽小』特質……算了，還是收下吧，說不定以後能派上用場。」這個「挑人」的過程本身就是樂趣。

老手邀請則是一筆重大投資。花 500 金邀請一個 B 階戰士，那是好幾單委託的傭金。玩家在按下「邀請」之前會猶豫：「我真的需要他嗎？還是把錢留給建設？」這個猶豫感正是經濟系統與招募系統交織產生的張力。而當你終於下定決心邀請一位 A 階冒險者，看到他加入名冊的那一刻，那種「我的公會又強了一分」的滿足感，值得那份投資。

刷新機制的設計意圖是製造「錯過」與「期待」：這批候選者不接就沒了，但下一批可能更好——也可能更差。免費刷新用完後，花 150 金再刷一次的誘惑，是一個微小但有意義的經濟決策。

## 3. 詳細規則（Detailed Rules）

### 3.1 候選池資料結構

`RecruitCandidate` 是 runtime 物件（不對應 CSV），存在於候選池中直到被接納或刷新消失。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `candidateID` | `int` | 候選池內唯一 ID，每次刷新重新分配 |
| `adventurerInstance` | `AdventurerInstance` | 預生成的冒險者實例（由 C-02 建立，尚未加入名冊） |
| `cost` | `int` | 招募費用；新手池固定 `0`，老手池查 `RecruitCostTable` |
| `reputationReq` | `int` | 聲望門檻；新手池固定 `0`，老手池查 `RecruitCostTable` |

---

### 3.2 刷新機制（兩池共用）

1. **自動刷新**：招募廣告欄建設等級決定刷新間隔，透過 FT-07 `GetRecruitRefreshInterval(): TimeSpan` 查詢（以秒數為底）。計時起點為上次刷新的 UTC timestamp（`_lastRefreshTimestamp`），由 F-02 Time System 驅動。到期時自動觸發刷新，兩個池子同時清空並重新生成
2. **手動刷新**：每日 00:00（`DAILY_RESET_HOUR`）重置 `_freeRefreshRemaining = DAILY_FREE_REFRESH`（1 次）。手動刷新時：
   - 若 `_freeRefreshRemaining > 0`：免費，消耗 1 次
   - 若 `_freeRefreshRemaining == 0`：需 `REFRESH_COST`（150g），呼叫 F-03 `CanAfford` → `AddGold(-REFRESH_COST)`
3. 手動刷新**重置自動刷新計時器**（`_lastRefreshTimestamp = now`），避免手動刷新後立刻又自動刷新
4. 離線期間若自動刷新時間已到期，重啟後立即執行一次刷新（不補算離線期間錯過的多次刷新）

---

### 3.3 新手池生成規則

1. 生成 `RECRUIT_POOL_SIZE`（4）名 F/E 階候選冒險者
2. 階級隨機：F 與 E 各 50% 機率
3. 生成流程：
   - 檢查是否有未使用的 `AdventurerTemplate`（`isUnique=1` 且 rank ∈ {F,E}，不在名冊中）→ 優先使用模板（C-02 `CreateFromTemplate`）
   - 其餘位置隨機生成：職業從 C-03 `GetBaseProfessions()` 均勻隨機，種族由 C-04 `RollRace(professionID)` 加權隨機，特質由 C-05 職業群組抽取
4. 同一批次內不重複同一 `templateID`

---

### 3.4 老手池生成規則

1. 生成 `RECRUIT_POOL_SIZE`（4）名 D~S 階候選冒險者
2. 階級由加權隨機決定，基礎權重：

| 階級 | D | C | B | A | S |
|------|---|---|---|---|---|
| 權重 | 40 | 30 | 18 | 9 | 3 |

3. **公會等級限制**：階級超過 FT-06 `GetMaxDifficulty()` 回傳值對應的最高可招募階級時，該階級權重歸零，剩餘權重重新正規化。例如公會 Lv1（`maxDifficulty = D`）→ 老手池只能出 D 階
4. 生成流程與新手池相同（優先使用未出現的具名模板，其餘隨機生成）
5. 每位老手候選者顯示 `cost` 與 `reputationReq`（來自 `RecruitCostTable`）

---

### 3.5 接納/邀請流程

**新手接納：**
1. 玩家選擇候選者 → 檢查 `IsRosterFull` → 呼叫 C-02 `AddAdventurer` → 從新手池移除該候選者

**老手邀請：**
1. 玩家選擇候選者 → 檢查 `IsRosterFull`
2. 檢查聲望：F-03 `GetReputation() >= candidate.reputationReq`
3. 檢查金幣：F-03 `CanAfford(candidate.cost)`
4. 扣款：F-03 `AddGold(-candidate.cost)`
5. 呼叫 C-02 `AddAdventurer` → 從老手池移除該候選者

---

### 3.6 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 取得新手池 | `GetRookiePool() : IReadOnlyList<RecruitCandidate>` | |
| 取得老手池 | `GetVeteranPool() : IReadOnlyList<RecruitCandidate>` | |
| 接納新手 | `RecruitRookie(int candidateID) : bool` | 失敗回傳 `false`（滿員） |
| 邀請老手 | `RecruitVeteran(int candidateID) : bool` | 失敗回傳 `false`（滿員/金幣不足/聲望不足） |
| 手動刷新 | `ManualRefresh() : bool` | 失敗回傳 `false`（金幣不足） |
| 剩餘免費刷新 | `GetFreeRefreshRemaining() : int` | |
| 下次自動刷新時間 | `GetNextAutoRefreshTimestamp() : long` | UTC timestamp |

## 4. 公式（Formulas）

### 4.1 自動刷新判定

```
CheckAutoRefresh():
    now = F-02 TimeSystem.NowUTC
    baseSec = (long)FT07.GetRecruitRefreshInterval().TotalSeconds  // 招募廣告欄建設等級決定
    reductionSec = FT08.GetRecruitRefreshReductionSec()             // 公會櫃臺職員 slot effect；FT-08 缺席時為 0
    intervalSec = max(baseSec - reductionSec, MIN_RECRUIT_REFRESH_INTERVAL_SEC)  // 強制下限避免刷新近乎即時
    if now >= _lastRefreshTimestamp + intervalSec:
        ExecuteRefresh()
        _lastRefreshTimestamp = now
```

> 離線回補：重啟時呼叫一次 `CheckAutoRefresh()`，若已到期則刷新一次。不補算中間錯過的多次刷新。
>
> **FT-08 加成**：`FT08.GetRecruitRefreshReductionSec()` 為公會櫃臺指派職員的 `RecruitRefreshOnCounter` slot effect 加總（已套用 §3.6.3 cap，預設上限 14400 秒 / 4h）。FT-08 系統未解鎖（`IsStaffSystemUnlocked() == false`）時固定回 0，公式照常運作。`MIN_RECRUIT_REFRESH_INTERVAL_SEC` 建議 `3600`（1h），避免極端配置使刷新近乎即時失去等待感。

---

### 4.2 手動刷新

```
ManualRefresh():
    if _freeRefreshRemaining > 0:
        _freeRefreshRemaining -= 1
    else:
        if !F03.CanAfford(REFRESH_COST): return false
        F03.AddGold(-REFRESH_COST)

    ExecuteRefresh()
    _lastRefreshTimestamp = F02.NowUTC   // 重置自動刷新計時器
    broadcast OnPoolRefreshed()
    return true
```

---

### 4.3 每日免費刷新重置

```
OnDailyReset():    // 訂閱 F-02 每日 00:00 事件
    _freeRefreshRemaining = DAILY_FREE_REFRESH    // 1
```

---

### 4.4 老手階級加權隨機

```
RollVeteranRank(maxRecruitableRank):
    baseWeights = { D:40, C:30, B:18, A:9, S:3 }

    // 過濾超過公會等級限制的階級
    filtered = baseWeights.Where(rank => RANK_INDEX(rank) <= RANK_INDEX(maxRecruitableRank))

    if filtered.IsEmpty() → Debug.LogError, return D    // 防禦性 fallback

    totalWeight = filtered.Values.Sum()
    roll = Random.Range(0, totalWeight)
    cumulative = 0
    foreach (rank, weight) in filtered:
        cumulative += weight
        if roll < cumulative:
            return rank

    return filtered.Last().Key    // 防禦性 fallback
```

範例（公會 Lv2，`maxDifficulty = C`）：
- 過濾後：D:40, C:30，total = 70
- roll 0~39 → D（57%），roll 40~69 → C（43%）

---

### 4.5 候選冒險者生成

```
GeneratePool(rankPool, poolSize):
    candidates = []
    usedTemplateIDs = []

    // Phase 1: 優先使用具名模板
    availableTemplates = AdventurerTemplate
        .Where(t => t.isUnique == 1
                  && t.rank ∈ rankPool
                  && !C02.RosterContains(t.templateID))
        .Shuffle()

    foreach template in availableTemplates:
        if candidates.Count >= poolSize: break
        if usedTemplateIDs.Contains(template.templateID): continue
        instance = C02.CreateFromTemplate(template.templateID)
        if instance == null: continue    // isUnique 衝突
        usedTemplateIDs.Add(template.templateID)
        candidates.Add(NewCandidate(instance, rankPool))

    // Phase 2: 隨機生成填滿剩餘位置
    while candidates.Count < poolSize:
        rank = PickRank(rankPool)    // 新手池: 50/50 F/E；老手池: RollVeteranRank
        professionID = RandomFrom(C03.GetBaseProfessions()).professionID
        raceID = C04.RollRace(professionID)
        traitIDs = C02.BuildTraitList([], C05.GetProfessionGroups(professionID))
        instance = NewAdventurerInstance(rank, professionID, raceID, traitIDs)
        candidates.Add(NewCandidate(instance, rankPool))

    return candidates
```

## 5. 邊緣案例（Edge Cases）

### 5.1 候選池生成

| 情況 | 處理方式 |
|------|---------|
| 符合條件的具名模板數量超過 `poolSize` | 隨機取 `poolSize` 個，其餘留待下次刷新 |
| 符合條件的具名模板為 0 個 | 全部隨機生成，不報錯（正常情況） |
| 老手池所有階級權重歸零（公會等級過低導致無合法階級） | `Debug.LogError`，fallback 生成 D 階。實務上不會發生：公會 Lv1 `maxDifficulty = D`，D 階權重 40 不會歸零 |
| 隨機生成時 C-03 `GetBaseProfessions()` 為空 | `Debug.LogError`，跳過該候選位置，池可能少於 `poolSize` |
| 隨機生成時 C-04 `RollRace` 回傳 fallback（raceID=1） | 正常接受，不報錯（C-04 內部已報錯） |

---

### 5.2 接納/邀請

| 情況 | 處理方式 |
|------|---------|
| 名冊已滿時嘗試接納/邀請 | 回傳 `false`，UI 顯示「名冊已滿」提示 |
| 老手邀請時金幣不足 | 回傳 `false`，UI 顯示「金幣不足」；不扣款、不移除候選者 |
| 老手邀請時聲望不足 | 回傳 `false`，UI 顯示「聲望不足」；不扣款、不移除候選者 |
| 老手邀請時 `CanAfford` 通過但 `AddGold` 失敗（重入防護等極端情況） | `Debug.LogError`，回傳 `false`，不加入名冊 |
| 老手邀請扣款會跨過破產閾值 | 走 F-03 `AddGold(-cost)`（**非** `AddGoldAllowBankruptcy`），F-03 規則 2 會 reject；回傳 `false`，不扣款、不加入名冊。招募系統刻意不允許靠邀請把公會推入破產——與 FT-05 金流（維護費 / 薪水 / 委託結算）的語義區別。 |
| 候選者的 `templateID` 在接納前已被其他途徑加入名冊（理論上不會，但防禦性） | C-02 `AddAdventurer` 內部的 isUnique 檢查攔截，回傳 `false`；FT-01 從池中移除該候選者，`Debug.LogWarning` |
| 接納/邀請後池中剩餘候選者不足 | 不自動補充，等待下次刷新 |

---

### 5.3 刷新

| 情況 | 處理方式 |
|------|---------|
| 付費手動刷新時金幣不足 | 回傳 `false`，不刷新，不消耗免費次數 |
| 離線超過多個刷新週期（如離線 72h，刷新間隔 24h） | 僅執行一次刷新，不補算中間錯過的週期 |
| 離線跨越每日重置時間（00:00） | 重啟時由 F-02 觸發每日重置事件，`_freeRefreshRemaining` 重置為 1 |
| 手動刷新與自動刷新幾乎同時觸發 | 手動刷新重置 `_lastRefreshTimestamp`，自動刷新的 `CheckAutoRefresh` 判定未到期，不會重複刷新 |
| FT-08 未解鎖（職員系統未啟用）| `FT08.GetRecruitRefreshReductionSec()` 回 0；公式 `intervalSec = baseSec - 0 = baseSec`，與 FT-08 缺席時行為一致 |
| FT-08 加成超過 `baseSec - MIN_RECRUIT_REFRESH_INTERVAL_SEC`（極端配置）| `intervalSec` 截斷為 `MIN_RECRUIT_REFRESH_INTERVAL_SEC`（1h 下限），不會出現負值或 0 間隔 |

---

### 5.4 存檔相關

| 情況 | 處理方式 |
|------|---------|
| 需序列化的狀態 | `_lastRefreshTimestamp`、`_freeRefreshRemaining`、兩個候選池的完整資料 |
| 載入存檔後候選者的 `templateID` 在當前 CSV 找不到 | 保留候選者資料（name/rank 等已快照），`templateID` 標記為 `0`（與 C-02 存檔策略一致） |
| 載入存檔後立即執行 `CheckAutoRefresh()`，可能覆蓋存檔中的候選池 | 若自動刷新已到期則刷新（符合預期：離線期間池已過期） |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-01 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `RecruitCostTable`、`AdventurerTemplate`、SystemConstants 常數 | `DataManager.GetAll<T>()` |
| F-02 Time System | 取得當前 timestamp 驅動自動刷新、訂閱每日重置事件、訂閱離線結算事件 | `NowUTC`、`OnDailyReset`、`OnSecondTick` |
| F-03 Resource Management | 老手邀請費用扣款、付費手動刷新扣款、聲望門檻查詢 | `CanAfford(amount)`、`AddGold(-amount)`、`GetReputation()` |
| C-02 Adventurer Management | 建立冒險者實例、加入名冊、名冊容量檢查 | `CreateFromTemplate(templateID)`、`AddAdventurer(instance)`、`IsRosterFull(rosterCap)`、`GetRoster()` |
| C-03 Profession System | 取得基礎職業列表作為隨機生成池 | `GetBaseProfessions()` |
| C-04 Race System | 隨機生成冒險者時抽種族 | `RollRace(professionID)` |
| C-05 Trait System | 隨機生成冒險者時抽特質 | `GetProfessionGroups(professionID)`、`RollTraits(group)` |
| FT-06 Guild Core | 可招募最高階級（老手邀請階級上限） | `GetMaxDifficulty()` |
| FT-07 Guild Building System | 名冊容量上限、招募刷新間隔 | `GetRosterCap()`、`GetRecruitRefreshInterval()` |
| FT-08 Guild Staff System | 公會櫃臺職員 slot effect：減少招募刷新秒數 | `GetRecruitRefreshReductionSec() : int`（系統未解鎖時回 0）|

---

### 6.2 下游依賴（依賴 FT-01 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| FT-10 Save/Load | 序列化候選池、刷新時間戳、免費刷新次數 | `GetRookiePool()`、`GetVeteranPool()`、`GetNextAutoRefreshTimestamp()`、`GetFreeRefreshRemaining()` |
| P-02 Main UI | 顯示兩個候選池、刷新按鈕、倒數計時 | `GetRookiePool()`、`GetVeteranPool()`、`ManualRefresh()`、`RecruitRookie()`、`RecruitVeteran()`、`OnPoolRefreshed` 事件 |

---

### 6.3 循環依賴注意事項

- FT-01 依賴 C-02 建立實例與加入名冊，C-02 不依賴 FT-01——**無循環依賴**
- FT-01 依賴 FT-06 查詢老手邀請階級上限，FT-06 不依賴 FT-01——**無循環依賴**
- FT-01 依賴 FT-07 查詢名冊容量與刷新間隔，FT-07 不依賴 FT-01——**無循環依賴**
- FT-01 依賴 FT-08 查詢櫃臺職員的招募刷新減量，FT-08 不依賴 FT-01——**無循環依賴**
- FT-01 依賴 F-03 扣款，F-03 不依賴 FT-01——**無循環依賴**

## 7. 可調參數（Tuning Knobs）

### 7.1 SystemConstants.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `RECRUIT_POOL_SIZE` | `4` | `3 ~ 6` | 每批候選池人數；過少選擇太窄，過多造成選擇困難且稀釋具名 NPC 出現率 |
| `DAILY_FREE_REFRESH` | `1` | `1 ~ 3` | 每日免費手動刷新次數；過多削弱付費刷新的經濟壓力 |
| `REFRESH_COST` | `150` | `50 ~ 300` | 額外手動刷新費用；應高於 F 級任務傭金（20g×20%=4g）但低於 D 級任務傭金（150g×20%=30g）的數倍，讓早期覺得昂貴、中期可承受 |

---

### 7.2 RecruitCostTable.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| D 階 `cost` | `100` | `50 ~ 200` | 中階冒險者入場費；過低讓玩家跳過新手期 |
| S 階 `cost` | `2000` | `1000 ~ 3500` | 頂階冒險者稀缺感；應高於玩家達到 S 難度前的存款預期 |
| C 階 `reputationReq` | `20` | `10 ~ 40` | 聲望門檻；過高卡關，過低聲望系統失去意義 |

---

### 7.3 老手階級權重

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| D 權重 | `40` | `30 ~ 50` | D 階出現率；權重相對比例決定分布 |
| C 權重 | `30` | `20 ~ 40` | |
| B 權重 | `18` | `10 ~ 25` | |
| A 權重 | `9` | `5 ~ 15` | 過高會讓 A 階太容易取得，削弱高階冒險者的稀缺感 |
| S 權重 | `3` | `1 ~ 5` | 過高破壞 S 階的傳奇感 |

> 權重目前定義於 game-concept，實作時建議移入 CSV 表格（如 `VeteranRankWeightTable`）以保持資料驅動原則。或作為 SystemConstants 的多筆 key（`VETERAN_WEIGHT_D`、`VETERAN_WEIGHT_C`…）。

---

### 7.4 刷新間隔（由 FT-07 Guild Building 控制）

| 招募廣告欄等級 | 刷新間隔 | 對應秒數 |
|-----------|---------|---------|
| L1（初始） | 24h | 86400s |
| L2 | 16h | 57600s |
| L3 | 8h | 28800s |

> 數值定義於 FT-07 `BuildingTable`（buildingID=2）。FT-01 透過 `FT07.GetRecruitRefreshInterval(): TimeSpan` 查詢，不快取。招募廣告欄初始已建造（L1），無「未建造」狀態。

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-AR-01 | 首次啟動後，新手池與老手池各有 `RECRUIT_POOL_SIZE`（4）名候選冒險者 |
| AC-AR-02 | 新手池候選者階級均為 F 或 E，老手池候選者階級均在 D~S 範圍內 |
| AC-AR-03 | 老手池候選者階級不超過 FT-06 `maxDifficulty` 對應的最高階級 |
| AC-AR-04 | `RecruitRookie(candidateID)` 成功後，候選者從新手池移除且出現在 C-02 名冊中 |
| AC-AR-05 | `RecruitVeteran(candidateID)` 成功後，金幣扣除 `cost`、候選者從老手池移除且出現在名冊中 |
| AC-AR-06 | 老手邀請時聲望不足（`GetReputation() < reputationReq`），回傳 `false`，金幣不變、候選者保留 |
| AC-AR-07 | 老手邀請時金幣不足（`CanAfford` 失敗），回傳 `false`，金幣不變、候選者保留 |
| AC-AR-08 | 名冊已滿時，`RecruitRookie` 與 `RecruitVeteran` 均回傳 `false` |
| AC-AR-09 | 自動刷新到期後，兩個池子同時清空並重新生成新候選者 |
| AC-AR-10 | 手動刷新（免費）成功後，`_freeRefreshRemaining` 減 1，兩個池子同時刷新，自動刷新計時器重置 |
| AC-AR-11 | 手動刷新（付費）成功後，金幣扣除 `REFRESH_COST`（150g），兩個池子同時刷新 |
| AC-AR-12 | 免費刷新次數用完後，付費刷新金幣不足時回傳 `false`，池子不變 |
| AC-AR-13 | 每日 00:00 重置後，`GetFreeRefreshRemaining()` 回傳 `DAILY_FREE_REFRESH`（1） |
| AC-AR-14 | 離線超過刷新間隔後重啟，僅執行一次刷新（非多次） |
| AC-AR-15 | `isUnique=1` 的模板已在名冊中（含 Dead），刷新時該模板不出現在候選池中 |
| AC-AR-16 | 同一批次候選池內不出現重複的 `templateID` |
| AC-AR-17 | `RollVeteranRank` 呼叫 1000 次（公會 Lv5，全階級開放），D 出現率約 40%、S 出現率約 3%（允許 ±5% 誤差） |
| AC-AR-18 | FT-08 未解鎖（`FT07.GetBuildingLevel(6) == 0`）時，`FT08.GetRecruitRefreshReductionSec()` 回 0，`intervalSec == FT07.GetRecruitRefreshInterval().TotalSeconds`（無減量套用） |
| AC-AR-19 | FT-08 解鎖且 1 位櫃臺職員帶 `RecruitRefreshOnCounter = 7200`（2h）並指派至公會櫃臺，`baseSec = 86400` 時 `intervalSec = 86400 − 7200 = 79200`；移除指派或職員轉 OnLeave 後 `intervalSec` 回到 `86400` |
