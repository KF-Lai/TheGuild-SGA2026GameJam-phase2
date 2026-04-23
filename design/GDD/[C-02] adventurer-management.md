# Adventurer Management 系統設計文件

_建立時間：2026-04-20_
_狀態：設計中_
_系統 ID：C-02_

---

## 1. 概要（Overview）

C-02 Adventurer Management 管理公會名冊中所有冒險者的靜態定義與 runtime 狀態。系統分為兩個層次：靜態層以 `AdventurerTemplate` 定義具名 NPC，`RecruitCostTable` 定義各階級招募費用；runtime 層的 `AdventurerInstance` 持有每位在冊冒險者的完整狀態，包含 rank、professionID、raceID、traitIDs、factionID，以及狀態機的當前狀態（`Idle` / `Dispatched` / `Wounded` / `Dead`）。`Wounded` 狀態代表任務失敗但生還，冒險者需要 `WOUNDED_RECOVERY_HOURS`（預設 6 小時）後才能接受新派遣，計時由 F-02 Time System 驅動。名冊（Roster）為全局冒險者集合，容量上限由 FT-06 Guild Core 的 `rosterCap` 提供；達上限時 FT-01 Recruitment 無法再加入新成員。C-02 不執行招募邏輯（FT-01 負責）、不計算成功率（FT-02 負責）、不處理金幣/聲望（F-03 負責）；其職責嚴格限定於冒險者資料的 CRUD 與狀態轉換。

## 2. 玩家幻想（Player Fantasy）

名冊是玩家最私密的地方。每一位冒險者不只是數字——他有名字、職業、種族、個性，他有自己的故事。當玩家滑過名冊，看到「艾克 · 鐵拳，戰士，已派遣 B 難度討伐，剩 3 小時」，那一刻他不是在看數值面板，他是在等一個他在乎的人回來。

`Wounded` 狀態的設計意圖是製造有重量的惋惜感：任務失敗了，但他活著回來——帶傷、需要休息。玩家看到這個狀態不會覺得「系統懲罰我」，而是「他這次差點沒命」。6 小時的恢復計時讓玩家在下次打開遊戲時，感受到時間的流逝也是這個世界的一部分。

`Wounded` 的來源有兩種：(a) **失敗存活**（正常路徑，擲骰結果為「失敗+存活」）；(b) **成功後被救活**（奇蹟路徑，擲骰結果為「成功+死亡」但 `on_death_survive` 等 condition 特質觸發，將死亡轉為重傷，由 FT-04 Outcome Resolution 依 C-05 § 4.4 規格套用）。兩種來源在系統行為上完全一致（6h 恢復、不可派遣、不可除名），但在玩家感受上形成對比——前者是「差點沒命的失敗」，後者是「撿回一條命的勝利」。

`Dead` 是永久的。沒有復活，沒有例外。正是因為死亡是真實的，活著才有意義。

## 3. 詳細規則（Detailed Rules）

### 3.1 AdventurerInstance 資料結構

`AdventurerInstance` 是 runtime 物件，不對應任何 CSV 表格（由 C-02 在記憶體中管理，並由 FT-10 Save/Load 序列化）。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `instanceID` | `int` | Runtime 唯一 ID，由 C-02 自增分配；`0` 為 null sentinel |
| `templateID` | `int` | 來源模板 ID；`0` = 隨機生成（無固定模板） |
| `name` | `string` | 顯示名稱（來自模板或 D-01 NamePool 隨機） |
| `rank` | `string` | F / E / D / C / B / A / S |
| `professionID` | `int` | FK → ProfessionTable（C-03） |
| `raceID` | `int` | FK → RaceTable（C-04） |
| `traitIDs` | `int[]` | FK → TraitTable（C-05），固定 + 隨機抽取的合集 |
| `factionID` | `int` | 陣營歸屬；隨機生成的冒險者填 `0`（neutral） |
| `status` | `enum` | `Idle` / `Dispatched` / `Wounded` / `Dead` |
| `currentMissionID` | `int` | 派遣中的任務 instanceID；非 `Dispatched` 狀態填 `0`。此 invariant 由 C-02 `UpdateStatus` / `SetWounded` 內部維護，上游呼叫者不需手動清除 |
| `woundedUntilTimestamp` | `long` | Unix timestamp（秒），`Wounded` 恢復截止時間；非 Wounded 填 `0` |

---

### 3.2 AdventurerTemplate 資料表（靜態 CSV）

具名 NPC 的定義表，PK = `templateID`。

| 欄位 | 型別 | 說明 |
|------|------|------|
| `templateID` | `int` (PK) | 從 1 起；`0` 為 null sentinel |
| `name` | `string` | 具名 NPC 的固定顯示名稱 |
| `rank` | `string` | F / E / D / C / B / A / S |
| `professionID` | `int` | FK → ProfessionTable |
| `raceID` | `int` | FK → RaceTable；`0` = 依職業隨機（C-04 ProfessionRacePool） |
| `fixedTraitIDs` | `int[]`（`\|` 分隔） | 每次實例化都必定擁有的特質 ID 列表；無固定特質填 `0` |
| `randomTraitGroupIDs` | `int[]`（`\|` 分隔） | 隨機抽取用的特質群組 ID 列表（FK → TraitGroupTable，C-05）；不需要隨機特質填 `0` |
| `factionID` | `int` | 陣營歸屬；`0` = neutral |
| `isUnique` | `int` | `1` = 唯一角色（全局只能實例化一次）；`0` = 可重複招募 |

> **特質生成規則**：實例化時，先將 `fixedTraitIDs` 全部加入；再對每個 `randomTraitGroupIDs` 中的群組，依 C-05 TraitGroup 的 `pickCount` 與 `pickMode` 隨機抽取後加入。最終 `traitIDs` = 固定特質 ∪ 隨機抽取特質，去重。

---

### 3.3 RecruitCostTable（靜態 CSV）

| `rank` | `cost`（金幣） | `reputationReq`（聲望門檻） |
|--------|-------------|------------------------|
| F | 0 | 0 |
| E | 0 | 0 |
| D | 100 | 0 |
| C | 250 | 20 |
| B | 500 | 50 |
| A | 1000 | 80 |
| S | 2000 | 100 |

> F/E 為新手自薦，免費且無聲望門檻；D~S 為老手邀請，費用由 F-03 Resource Management 扣除，聲望門檻由 FT-01 Recruitment 驗證。

---

### 3.4 狀態機轉換規則

```
Idle ──────────────────────► Dispatched   （FT-02 派遣）

Dispatched ─[成功，存活]──────────► Idle
Dispatched ─[慘勝 or 失敗，存活]──► Wounded
Dispatched ─[慘勝 or 失敗，死亡]──► Dead

Wounded ─[woundedUntilTimestamp 到期]──► Idle

Dead ─── 終態
        保留於名冊，status = Dead
        玩家在名冊手動按「除名」→ C-02.DismissAdventurer(instanceID) → 從名冊移除
```

**補充規則：**

1. `Dispatched` 狀態的冒險者**不可**再次派遣，也**不可**被除名
2. `Wounded` 狀態的冒險者**不可**被派遣；FT-03 NPC Decision 在 willingness 計算前直接跳過 `Wounded` 冒險者；恢復計時由 F-02 Time System 的 timestamp 比較驅動（含離線時間）
3. `Dead` 狀態的冒險者**占用名冊容量**直到被手動除名；FT-01 Recruitment 在判斷名冊是否滿員時，`Dead` 狀態的冒險者仍計入人數
4. `DismissAdventurer` 僅允許對 `Dead` 狀態的冒險者呼叫；對其他狀態呼叫時回傳 `false` 並 `Debug.LogWarning`
5. **招募池刷新規則**：在刷新招募池時，若某具名 NPC 的 `templateID` 在名冊中存在（含 `Dead` 狀態），則該模板永不刷入招募池（由 FT-01 Recruitment 呼叫 `CreateFromTemplate` 時由 C-02 isUnique 檢查攔截）

---

### 3.5 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 取得全名冊 | `GetRoster() : IReadOnlyList<AdventurerInstance>` | 含所有狀態（含 Dead） |
| 依狀態篩選 | `GetByStatus(AdventurerStatus status) : IReadOnlyList<AdventurerInstance>` | 供 FT-02、P-02 使用 |
| 單筆查詢 | `GetAdventurer(int instanceID) : AdventurerInstance` | 找不到回傳 `null` |
| 名冊人數 | `GetRosterCount() : int` | 含 Dead；供 FT-01 判斷滿員 |
| 名冊是否滿員 | `IsRosterFull(int rosterCap) : bool` | `GetRosterCount() >= rosterCap` |
| 加入名冊 | `AddAdventurer(AdventurerInstance instance) : bool` | 滿員回傳 `false`；FT-01 呼叫 |
| 更新狀態 | `UpdateStatus(int instanceID, AdventurerStatus newStatus) : void` | FT-04 Outcome 呼叫。若 `newStatus ≠ Dispatched`，自動清除 `currentMissionID = 0` |
| 設定 Wounded | `SetWounded(int instanceID) : void` | 狀態轉為 `Wounded`；計算並寫入 `woundedUntilTimestamp`（當前時間 + `WOUNDED_RECOVERY_HOURS × 3600`）；自動清除 `currentMissionID = 0` |
| 除名 | `DismissAdventurer(int instanceID) : bool` | 僅允許 Dead 狀態；成功移除回傳 `true` |
| 從模板建立 | `CreateFromTemplate(int templateID) : AdventurerInstance` | 由 FT-01 呼叫；isUnique 驗證在此執行 |
| Tick 更新 | `TickWoundedRecovery() : void` | C-02 訂閱 F-02 `OnSecondTick`，每秒自行呼叫；將到期的 Wounded → Idle |

## 4. 公式（Formulas）

### 4.1 Wounded 恢復截止時間

```
SetWounded(instanceID):
    now = F-02 TimeSystem.NowUTC   // Unix 秒
    instance.woundedUntilTimestamp = now + WOUNDED_RECOVERY_HOURS × 3600
    instance.status = Wounded
```

- `WOUNDED_RECOVERY_HOURS = 6`（SystemConstants，預設值）
- 範例：當前 timestamp = 1745000000 → `woundedUntilTimestamp = 1745000000 + 21600 = 1745021600`

---

### 4.2 Wounded 恢復 Tick

```
TickWoundedRecovery():
    now = F-02 TimeSystem.NowUTC
    foreach instance in roster where status == Wounded:
        if now >= instance.woundedUntilTimestamp:
            instance.status = Idle
            instance.woundedUntilTimestamp = 0
            EventBus.Publish(OnAdventurerRecovered, instanceID)
```

> 離線時間自動計入，因為 timestamp 是絕對時間點，不依賴 frame 計算。`TickWoundedRecovery` 由 F-02 Time System 在每次 Tick 時呼叫（含離線補算）。

---

### 4.3 isUnique 驗證（CreateFromTemplate 內）

```
CreateFromTemplate(templateID):
    template = DataManager.Get<AdventurerTemplate>(templateID)
    if template == null → return null, Debug.LogError

    if template.isUnique == 1:
        if roster.Any(a => a.templateID == templateID):
            Debug.LogWarning("unique adventurer already in roster")
            return null

    instance = new AdventurerInstance()
    instance.instanceID    = NextInstanceID()
    instance.templateID    = templateID
    instance.name          = template.name
    instance.rank          = template.rank
    instance.professionID  = template.professionID
    instance.raceID        = (template.raceID != 0)
                               ? template.raceID
                               : C04.RollRace(template.professionID)
    instance.traitIDs      = BuildTraitList(template.fixedTraitIDs,
                                            template.randomTraitGroupIDs)
    instance.factionID     = template.factionID
    instance.status        = Idle
    instance.currentMissionID       = 0
    instance.woundedUntilTimestamp  = 0
    return instance
```

> `isUnique` 檢查對名冊中所有狀態（含 `Dead`）的實例生效——唯一角色一旦出現過（無論是否已死亡），便永不再刷出招募池。

---

### 4.4 特質合集建立（BuildTraitList）

```
BuildTraitList(fixedTraitIDs, randomTraitGroupIDs):
    result = fixedTraitIDs.Filter(id => id != 0)   // 去除 null sentinel

    foreach groupID in randomTraitGroupIDs.Filter(id => id != 0):
        group = C05.GetTraitGroup(groupID)
        picked = C05.RollTraits(group)              // 依 pickCount + pickMode 抽取
        result.AddRange(picked)

    return result.Distinct()                        // 去重，避免固定與隨機重複
```

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `AdventurerTemplate` 的 `professionID` 在 ProfessionTable 找不到 | `Debug.LogError`，跳過該模板 |
| `raceID != 0` 但在 RaceTable 找不到 | `Debug.LogError`，跳過該模板 |
| `fixedTraitIDs` 包含不存在於 TraitTable 的 ID（非 0） | `Debug.LogWarning`，過濾該 traitID，其餘正常載入 |
| `randomTraitGroupIDs` 包含不存在於 TraitGroupTable 的 groupID（非 0） | `Debug.LogWarning`，跳過該 groupID，其餘正常抽取 |
| `rank` 不在合法值 {F, E, D, C, B, A, S} | `Debug.LogError`，跳過該模板 |
| 同一 `templateID` 在 CSV 中出現兩次 | 後者覆蓋前者，`Debug.LogWarning`（DataManager 標準行為）|

---

### 5.2 Runtime 操作

| 情況 | 處理方式 |
|------|---------|
| `AddAdventurer` 時名冊已滿 | 回傳 `false`，`Debug.LogWarning`；不加入 |
| `UpdateStatus` 傳入不存在的 `instanceID` | `Debug.LogWarning`，無操作 |
| `DismissAdventurer` 對 `Idle` / `Dispatched` / `Wounded` 狀態呼叫 | 回傳 `false`，`Debug.LogWarning`；不移除 |
| `SetWounded` 對非 `Dispatched` 狀態呼叫 | `Debug.LogWarning`，無操作（狀態轉換只允許從 Dispatched 進入 Wounded） |
| `CreateFromTemplate` 但 `isUnique=1` 且同 `templateID` 已在名冊中（含 Dead） | 回傳 `null`，`Debug.LogWarning` |
| `TickWoundedRecovery` 呼叫時 F-02 timestamp 取得失敗（回傳 0） | `Debug.LogError`，跳過本次 Tick，不修改任何狀態 |
| 名冊中所有冒險者均為 `Dead` 或 `Dispatched`，無 `Idle` 可派 | C-02 不處理此情況；FT-02 / FT-03 在取得空的 Idle 列表後自行處理 |

---

### 5.3 存檔相關

| 情況 | 處理方式 |
|------|---------|
| 載入存檔時 `instanceID` 自增計數器重建 | 取名冊中最大 `instanceID + 1`，確保不重複 |
| 存檔中的 `templateID` 在當前 CSV 找不到 | `Debug.LogWarning`，保留該實例資料（name / rank 等已快照），僅將 `templateID` 標記為 `0` |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（C-02 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `AdventurerTemplate`、`RecruitCostTable` | `DataManager.GetAll<AdventurerTemplate>()` |
| F-02 Time System | 取得當前 Unix timestamp，驅動 Wounded 恢復 Tick | `TimeSystem.NowUTC`、Tick 回呼 |
| C-03 Profession System | 驗證 `professionID` 合法性；UI 顯示職業名稱 | `GetProfession(professionID)` |
| C-04 Race System | `raceID = 0` 時依職業隨機抽種族 | `RollRace(professionID)` |
| C-05 Trait System | 依 `randomTraitGroupIDs` 抽取隨機特質 | `GetTraitGroup(groupID)`、`RollTraits(group)` |

---

### 6.2 下游依賴（依賴 C-02 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| FT-01 Recruitment | 加入新冒險者、建立模板實例、檢查名冊容量 | `AddAdventurer`、`CreateFromTemplate`、`IsRosterFull` |
| FT-02 Mission Dispatch | 取得可派遣（Idle）冒險者列表 | `GetByStatus(Idle)` |
| FT-03 NPC Decision | 取得 Idle 冒險者清單計算 willingness | `GetByStatus(Idle)` |
| FT-04 Outcome Resolution | 結算後更新冒險者狀態（Idle / Wounded / Dead） | `SetWounded`、`UpdateStatus` |
| FT-10 Save/Load | 序列化／反序列化整個名冊 | `GetRoster()`、`AddAdventurer` |
| P-02 Main UI | 顯示名冊列表、狀態、除名按鈕 | `GetRoster()`、`GetAdventurer`、`DismissAdventurer` |

---

### 6.3 循環依賴注意事項

- C-02 依賴 C-04、C-05，C-04 / C-05 不依賴 C-02——**無循環依賴**
- C-02 依賴 F-02 的 timestamp，F-02 不依賴 C-02——**無循環依賴**
- FT-04 呼叫 C-02 `UpdateStatus`，C-02 不依賴 FT-04——**無循環依賴**

## 7. 可調參數（Tuning Knobs）

### 7.1 SystemConstants.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| `WOUNDED_RECOVERY_HOURS` | `6` | `2 ~ 24` | Wounded 冒險者的恢復等待時間；過短失去懲罰感，過長讓玩家名冊長期空缺，建議搭配名冊容量一起評估 |

---

### 7.2 RecruitCostTable.csv

| 參數 | 預設值 | 安全範圍 | 影響 |
|------|--------|---------|------|
| D 階 `cost` | 100 | 50 ~ 200 | 中階冒險者的入場費；過低讓玩家跳過新手期，過高阻礙公會發展節奏 |
| S 階 `cost` | 2000 | 1000 ~ 3500 | 頂階冒險者的稀缺感；應高於玩家達到 S 難度前的存款預期 |
| C 階 `reputationReq` | 20 | 10 ~ 40 | 中階冒險者的聲望門檻；過高讓玩家卡關，過低聲望系統失去意義 |

---

### 7.3 AdventurerTemplate.csv

新增具名 NPC：在 CSV 新增一行，分配唯一 `templateID`，程式碼無需改動。`isUnique = 1` 適合用於有劇情意義的角色；`isUnique = 0` 適合可多次出現的特色冒險者原型。

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-AM-01 | DataManager 初始化後，`GetRoster()` 回傳空列表（初始無任何冒險者） |
| AC-AM-02 | `CreateFromTemplate(templateID)` 建立的實例，`professionID`、`rank`、`name` 與模板一致 |
| AC-AM-03 | 模板 `raceID = 0` 時，建立的實例 `raceID` 為 C-04 RollRace 的合法結果（非 0） |
| AC-AM-04 | 模板有 `fixedTraitIDs`，建立的實例 `traitIDs` 必定包含所有非 0 的 `fixedTraitIDs` |
| AC-AM-05 | `isUnique = 1` 的模板，名冊中已有任意狀態（含非 Dead）實例時，再次 `CreateFromTemplate` 回傳 `null` 並出現 `LogWarning` |
| AC-AM-06 | `isUnique = 1` 的模板，名冊中存在該 `templateID` 的 `Dead` 實例時，`CreateFromTemplate` 回傳 `null` 並出現 `LogWarning`（Dead 實例仍封鎖重招） |
| AC-AM-07 | `AddAdventurer` 在名冊未滿時回傳 `true`，名冊達上限時回傳 `false` |
| AC-AM-08 | `IsRosterFull(4)` 在名冊有 4 人（含 Dead）時回傳 `true` |
| AC-AM-09 | `SetWounded(instanceID)` 後，`woundedUntilTimestamp = 當前 timestamp + 21600`，status = `Wounded` |
| AC-AM-10 | `TickWoundedRecovery()` 呼叫時，`woundedUntilTimestamp` 已到期的冒險者 status 轉為 `Idle`，`woundedUntilTimestamp` 歸零 |
| AC-AM-11 | 模擬離線 7 小時後呼叫 `TickWoundedRecovery()`，`WOUNDED_RECOVERY_HOURS = 6` 的冒險者狀態轉為 `Idle` |
| AC-AM-12 | `DismissAdventurer` 對 `Dead` 冒險者回傳 `true`，從名冊移除；對 `Idle` / `Dispatched` / `Wounded` 回傳 `false` |
| AC-AM-13 | `GetByStatus(Idle)` 只回傳 status = `Idle` 的冒険者 |
| AC-AM-14 | `UpdateStatus` 傳入不存在的 `instanceID` 時，出現 `LogWarning`，名冊無任何變動 |
| AC-AM-15 | `AdventurerTemplate` 中 `professionID` 在 ProfessionTable 找不到時，啟動出現 `LogError`，該模板不可被 `CreateFromTemplate` 使用 |
