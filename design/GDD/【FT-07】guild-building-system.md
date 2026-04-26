# Guild Building System 系統設計文件

_建立時間：2026-04-23_
_狀態：設計完成（Section 1-8 全部完成，待 /design-review）_
_系統 ID：FT-07_

---

## 1. 概要（Overview）

FT-07 Guild Building System 是公會建築升級的統一管理系統，負責 6 棟建築（委託板、招募廣告欄、公會大廳、公會櫃臺、預備金保險櫃、職員休息室）的等級狀態維護與效果值授權。每棟建築擁有獨立的升級等級，升級後永久擴充功能：委託板控制同時開放的委託槽數；招募廣告欄控制候選池刷新間隔；公會大廳控制冒險者名冊上限；公會櫃臺控制同時可進行中的任務數；預備金保險櫃控制破產觸發後的倒數緩衝時間；職員休息室為特殊建築，初始為「未建造」（Lv0），建造後才開通 FT-08 職員系統。升級採**雙軌閘門制**：Lv1→Lv2 僅需金幣（金幣軸，早期成長無阻礙），Lv3 及以上同時需要對應的公會聲望等級（聲望閘，確保中後期建築進度與公會整體成長同步）。本系統透過查詢 API 對外暴露建築效果值，下游系統（FT-01 招募、FT-02 派遣、F-03 破產警告、P-02 UI）統一從 FT-07 讀取，不自行維護建築狀態副本。

**不負責**：建築的視覺呈現（P-02 負責）、職員招募邏輯（FT-08 負責）、破產倒數計時實作（F-03 負責，僅讀取 FT-07 的秒數配置）。

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-07 承載兩種核心情緒：

**成長與掌控（Expression / Mastery）**
- 玩家把賺來的金幣花在升級建築，公會從「剛開張的破舊小屋」逐步變成「設施齊全的冒險者殿堂」
- 每次升級都能立即感受到量的改變：委託板從 5 格變 8 格、大廳從 10 人擴到 15 人——效果具體，不是純粹的數字堆疊
- 聲望閘（Lv3+）的存在讓玩家感受到「我的公會真的在長大」——不是只要有錢就能買到一切，聲望代表實力的積累

**資源決策的重量（Challenge / Agency）**
- 金幣是有限的，升委託板還是升公會大廳？先擴容還是先提速？每個選擇都有機會成本
- 聲望閘讓高級建築升級成為里程碑事件，而非單純的金幣消耗——「終於升到 Lv3 了，可以蓋保險櫃 L3 了」

### 玩家幻想敘事

> 「公會剛開始的時候，委託板才貼得下五張任務，大廳也只容得下十個人。但只要讓冒險者們出任務、把傭金攢起來，就能一點一點把這裡擴建——再過一陣子，委託板貼滿了，候選的冒險者也愈來愈多，最後連破產的壓力都能扛得住更久。這間公會，是我一塊磚一塊磚蓋起來的。」

### 關鍵情緒節點

| 時機 | 觸發 | 期望情緒 |
|---|---|---|
| 首次升級任意建築 | 玩家花掉第一筆積蓄 | 投資感、期待感 |
| 委託板 L2（8 格） | 委託選擇變多，策略空間打開 | 掌控感提升 |
| 公會大廳 L3 需 Guild Lv3 | 聲望閘第一次出現 | 目標感（為了升大廳，努力刷聲望） |
| 職員休息室建造 | 職員系統解鎖 | 驚喜、新玩法期待 |
| 保險櫃 L5（48h 倒數） | 破產壓力大幅緩解 | 安全感、後期從容 |

---

## 3. 詳細規則（Detailed Rules）

### 3.1 建築狀態（BuildingState）

```
BuildingState {                   // ↓ 持久化欄位（FT-10 序列化）
    buildingID   : int            // 唯一識別碼（對應 BuildingTable）
    currentLevel : int            // 0（未建造）或 1..maxLevel
}
```

每棟建築以獨立 `BuildingState` 存儲。系統啟動時，除職員休息室外所有建築初始化為 `currentLevel = 1`；職員休息室初始化為 `currentLevel = 0`。

### 3.2 六棟建築定義

| buildingID | 名稱 | 初始等級 | 最高等級 | 效果類型 |
|---|---|---|---|---|
| 1 | 委託板 | 1 | 5 | 委託槽數 |
| 2 | 招募廣告欄 | 1 | 3 | 候選池刷新間隔 |
| 3 | 公會大廳 | 1 | 5 | 名冊上限 |
| 4 | 公會櫃臺 | 1 | 5 | 同時任務上限 |
| 5 | 預備金保險櫃 | 1 | 5 | 破產倒數秒數 |
| 6 | 職員休息室 | 0 | 5 | 職員系統解鎖（L1） + 面試自動刷新間隔階梯（L1~L5） |

### 3.3 升級流程

```
TryUpgradeBuilding(buildingID):
    entry = BuildingTable[buildingID]
    current = GetBuildingLevel(buildingID)

    // 閘門 1：已達上限
    IF current >= entry.maxLevel → 回傳 ALREADY_MAX

    nextLevel = current + 1
    upgrade = entry.upgradeData[nextLevel]

    // 閘門 2：公會等級（以 upgrade.guildLevelReq 為準；L1/L2 的 guildLevelReq 固定為 0，恆通過）
    IF FT06.GetCurrentLevel() < upgrade.guildLevelReq → 回傳 GUILD_LEVEL_INSUFFICIENT

    // 閘門 3：金幣（使用 AddGold，嚴格不允許進入負值；見 §5.2）
    IF F03.GetGold() < upgrade.cost → 回傳 GOLD_INSUFFICIENT

    // 通過：扣金幣、升等
    F03.AddGold(-upgrade.cost)   // 非 AddGoldAllowBankruptcy：建築升級屬主動消費，無債務豁免
    currentLevel = nextLevel
    發布 OnBuildingUpgraded(buildingID, fromLevel, toLevel)

    // 保險櫃升級後推送新破產倒數秒數
    IF buildingID == 5:
        F03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())

    回傳 SUCCESS
```

### 3.4 效果值查詢 API

升級後效果立即生效，下游系統每次需要數值時主動呼叫以下 API：

| API | 回傳 | 消費者 |
|---|---|---|
| `GetBuildingLevel(id)` | `int` | P-02 UI |
| `GetMissionSlotCount()` | `int` | P-02（委託板顯示槽） |
| `GetRecruitRefreshInterval()` | `TimeSpan` | FT-01（刷新計時） |
| `GetRosterCap()` | `int` | FT-01（招募上限閘） |
| `GetMaxConcurrentMissions()` | `int` | FT-02（派遣上限閘） |
| `GetBankruptcyWarningSeconds()` | `int` | F-03（破產倒數設定）；同時透過啟動推送（`Start()`）與升級推送（`buildingID=5` 升級時）主動寫入 F-03 `_currentWarningDuration` |
| `IsStaffSystemUnlocked()` | `bool` | FT-08、P-02 |

所有 API 皆同步、即時讀 `BuildingTable` 對應等級行，**不快取**。

### 3.5 建築效果數值表

#### 委託板

| 等級 | 委託槽數 | 升級費用 | 公會等級需求 |
|---|---|---|---|
| 1 | 5 | — | — |
| 2 | 8 | 150g | — |
| 3 | 11 | 350g | Lv3 |
| 4 | 14 | 700g | Lv4 |
| 5 | 17 | 1,200g | Lv5 |

#### 招募廣告欄

| 等級 | 刷新間隔 | 升級費用 | 公會等級需求 |
|---|---|---|---|
| 1 | 24h | — | — |
| 2 | 16h | 250g | — |
| 3 | 8h | 600g | Lv3 |

#### 公會大廳

| 等級 | 名冊上限 | 升級費用 | 公會等級需求 |
|---|---|---|---|
| 1 | 10 | — | — |
| 2 | 15 | 400g | — |
| 3 | 20 | 900g | Lv3 |
| 4 | 25 | 1,600g | Lv4 |
| 5 | 30 | 2,500g | Lv5 |

#### 公會櫃臺

| 等級 | 同時任務上限 | 升級費用 | 公會等級需求 |
|---|---|---|---|
| 1 | 5 | — | — |
| 2 | 8 | 250g | — |
| 3 | 11 | 500g | Lv3 |
| 4 | 14 | 900g | Lv4 |
| 5 | 18 | 1,400g | Lv5 |

#### 預備金保險櫃

| 等級 | 破產倒數 | 升級費用 | 公會等級需求 |
|---|---|---|---|
| 1 | 3h（10,800s） | — | — |
| 2 | 6h（21,600s） | 200g | — |
| 3 | 12h（43,200s） | 450g | Lv3 |
| 4 | 24h（86,400s） | 800g | Lv4 |
| 5 | 48h（172,800s） | 1,500g | Lv5 |

#### 職員休息室

| 等級 | 效果（FT-08 面試自動刷新間隔，秒） | 費用 | 公會等級需求 |
|---|---|---|---|
| 0 | 未建造（職員系統鎖定，`IsStaffSystemUnlocked()` 回 `false`） | — | — |
| 1 | 24h（86,400s）+ `IsStaffSystemUnlocked()` 回 `true` | 500g | — |
| 2 | 18h（64,800s） | 1,500g | Lv2 |
| 3 | 12h（43,200s） | 3,000g | Lv3 |
| 4 | 8h（28,800s） | 7,000g | Lv4 |
| 5 | 6h（21,600s） | 15,000g | Lv5 |

> 職員休息室 L2~L5 透過縮短 FT-08 面試自動刷新間隔提供加速。完整 `BuildingTable[6]` 數值與設計理由見 §7.1 + §7.3；FT-08 §4.1.3 / §7.3 對齊。

### 3.6 事件發布契約

| 事件 | Payload | 發布時機 | 訂閱者 |
|---|---|---|---|
| `OnBuildingUpgraded` | `buildingID, fromLevel, toLevel` | 升級成功 | P-02（UI 更新）、P-03（通知） |
| `OnGuildMaintenanceDue` | `dueTimestamp, perBuildingCost, totalAmount` | 每日 UTC 00:00（`OnDailyReset`）**Phase 2，Jam 版不發布** | FT-05 Guild Gold Flow（扣款）、P-02（顯示明細）、P-03（通知） |

### 3.7 與 FT-06 的職責切割

FT-06 `GuildLevelTable` 的 `rosterCap` 與 `maxMissions` 欄位**不再使用**；FT-01、FT-02 改從 FT-07 讀取對應 API。FT-06 僅保留：等級判定（聲望門檻）、稱號、可接難度上限（`maxDifficulty`）。

---

### 3.8 設施維護費管線（Maintenance Pipeline）

> **Phase 2 規格 — Jam 版不實作**：FT-07 Jam 版不訂閱 `OnDailyReset`，不計算維護費，不發布 `OnGuildMaintenanceDue`。FT-05 訂閱端（§3.6）已就緒，Phase 2 啟用時解除 FT-07 此節的 Jam 封鎖即可。

#### 3.8.1 觸發時機

FT-07 訂閱 F-02 `OnDailyReset` 事件，於每日 UTC 00:00 觸發維護費計算與發布。

```
OnDailyReset(resetTimestamp):
    perBuildingCost = CalculateMaintenanceCosts()
    totalAmount = Σ perBuildingCost.Values
    IF totalAmount == 0: return  // 所有建築均在 L1，免維護費
    Publish OnGuildMaintenanceDue {
        dueTimestamp      = resetTimestamp,
        perBuildingCost   = perBuildingCost,  // defensive copy
        totalAmount       = totalAmount
    }
```

#### 3.8.2 維護費計算

```
CalculateMaintenanceCosts() → Dictionary<int, int>:
    result = {}
    FOR buildingID in 1..6:
        level = buildingStates[buildingID].currentLevel
        cost  = BuildingTable[(buildingID, level)].maintenanceCost
        IF cost > 0:
            result[buildingID] = cost
    return result
```

- 職員休息室 L0（未建造）與所有建築 L1 的 `maintenanceCost = 0`，不進 `perBuildingCost` dict
- FT-07 不判斷金幣是否足夠（由 FT-05 `AddGoldAllowBankruptcy` 處理，可觸發破產警告）

#### 3.8.3 維護費數值設計

各建築每日維護費（金幣），L1 = 0（新手免費），升級後開始計費：

| 建築 | L0/L1 | L2 | L3 | L4 | L5 |
|---|---|---|---|---|---|
| 委託板（ID=1） | 0 | 15 | 30 | 55 | 80 |
| 招募廣告欄（ID=2） | 0 | 20 | 40 | — | — |
| 公會大廳（ID=3） | 0 | 20 | 45 | 80 | 120 |
| 公會櫃臺（ID=4） | 0 | 20 | 40 | 70 | 100 |
| 預備金保險櫃（ID=5） | 0 | 15 | 30 | 50 | 75 |
| 職員休息室（ID=6） | 0 | 0 | 40 | 90 | 160 | 240 |

**設計依據**：
- 所有建築 L1 免維護費，降低新玩家壓力
- 維護費約為當日預期傭金收入的 10~15%（Lv2 全 L2：90g/天；Lv3 全 L3：185g/天）
- 職員休息室 L2+ 費用較高，反映職員系統帶來的持續加成價值
- Phase 2 啟用前需確認玩家儲備金至少為 3 倍當日預計維護費（避免載入存檔即觸發破產）

---

## 4. 公式（Formulas）

### 4.1 升級資格判定公式

給定 `buildingID`、目前建築等級 `currentLevel`，判定能否升級至 `nextLevel = currentLevel + 1`：

```
CanUpgrade(buildingID) → bool:
    entry    = BuildingTable[buildingID]
    nextLv   = BuildingState[buildingID].currentLevel + 1

    IF nextLv > entry.maxLevel         → false（已滿級）
    IF FT06.GetCurrentLevel() <
       entry.upgradeData[nextLv].guildLevelReq → false（聲望不足；L1/L2 guildLevelReq=0 恆通過）
    IF F03.GetGold() <
       entry.upgradeData[nextLv].cost  → false（金幣不足）
    → true
```

### 4.2 效果值查詢公式（查表型）

所有效果值直接查 `BuildingTable`（複合主鍵 `(buildingID, level)`），無額外計算：

```
GetMissionSlotCount():
    return BuildingTable[1].upgradeData[GetBuildingLevel(1)].effectValue
    // 範例：等級 2 → 8；等級 3 → 11

GetRosterCap():
    return BuildingTable[3].upgradeData[GetBuildingLevel(3)].effectValue
    // 範例：等級 1 → 10；等級 5 → 30

GetMaxConcurrentMissions():
    return BuildingTable[4].upgradeData[GetBuildingLevel(4)].effectValue

GetRecruitRefreshInterval():
    seconds = BuildingTable[2].upgradeData[GetBuildingLevel(2)].effectValue
    return TimeSpan.FromSeconds(seconds)
    // 等級 1 → 86400s（24h）；等級 3 → 28800s（8h）

GetBankruptcyWarningSeconds():
    return BuildingTable[5].upgradeData[GetBuildingLevel(5)].effectValue
    // 等級 1 → 10800（3h）；等級 5 → 172800（48h）

IsStaffSystemUnlocked():
    return GetBuildingLevel(6) >= 1
```

### 4.3 計算範例

**範例 A — 玩家嘗試將委託板從 L2 升至 L3（公會 Lv2，金幣充足）**

| 條件 | 值 | 結果 |
|---|---|---|
| nextLevel | 3 | — |
| 聲望閘觸發？（nextLevel >= 3） | 是 | 檢查 FT-06 等級 |
| FT-06 當前等級 | 2 | Lv3 需求 → **不足** |
| 結果 | — | `GUILD_LEVEL_INSUFFICIENT` |

**範例 B — 公會 Lv3，金幣 500g，嘗試升委託板 L2→L3（費用 350g）**

| 條件 | 值 | 結果 |
|---|---|---|
| nextLevel | 3 | — |
| 聲望閘 | Lv3 需求，當前 Lv3 | 通過 |
| 金幣閘 | 需 350g，有 500g | 通過 |
| 扣除金幣後 | 500 − 350 = 150g | — |
| 委託板等級 | 2 → 3 | `GetMissionSlotCount()` 回傳 11 |

### 4.4 啟動推送（破產倒數秒數）

系統啟動時，主動將當前保險櫃等級對應的破產倒數秒數推送至 F-03。對齊 C-06 `SetBankruptcyThreshold` 啟動推送模式。

```
Start():
    // buildingID=5（預備金保險櫃）初始必為 Lv1（新遊戲初始已建造），推送 L1 = 10800s
    // 存檔載入時以載入後等級為準，GetBankruptcyWarningSeconds() 自動查 BuildingTable[5]
    F03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())
```

### 4.5 升級推送（保險櫃升級時）

保險櫃升級成功後，立即推送新等級對應的秒數。其他建築升級不執行此推送。

```
// 在 TryUpgradeBuilding() 升等後執行（§3.3 流程末尾）
AfterUpgrade(buildingID, newLevel):
    IF buildingID == 5:
        F03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())
        // 此時 GetBankruptcyWarningSeconds() 已查新等級
        // 範例：L4→L5 升級後推送 172800（48h）
```

---

## 5. 邊緣案例（Edge Cases）

**Case 5.1 — 玩家在金幣恰好等於升級費用時升級**
- 情境：金幣 = 350g，委託板 L2→L3 費用 = 350g
- 行為：閘門通過（`>=`，等於視為足夠），扣除後金幣 = 0g（可能進入警告區間）
- 文件標註：F-03 的破產警告由 F-03 自行判定，FT-07 只負責扣金幣，不預判破產

**Case 5.2 — 升級後金幣變負數（債務狀態）**
- 情境：金幣 80g，但玩家強行呼叫（或 UI bug 讓玩家在金幣不足時送出升級請求）
- 行為：`CanUpgrade()` 在金幣閘已回傳 `false`，`TryUpgradeBuilding()` 不執行扣款；若直接繞過 `CanUpgrade()` 呼叫 `TryUpgradeBuilding()`，內部閘門同樣攔截，回傳 `GOLD_INSUFFICIENT`
- 文件標註：FT-07 **不允許**因升級建築而讓金幣進入負值；建築升級與任務失敗賠償不同，屬於主動消費，無債務豁免

**Case 5.3 — 同一 frame 內連續呼叫 TryUpgradeBuilding（UI 雙擊）**
- 情境：P-02 按鈕沒有防連點，玩家快速雙擊升級
- 行為：第一次呼叫成功升等；第二次呼叫時 `currentLevel` 已更新，若仍可升（金幣足夠且閘門通過）則再次升等；若不可升（已達上限或金幣不足）則回傳對應錯誤
- 防禦：P-02 升級按鈕應在 `OnBuildingUpgraded` 事件發出後才重新 Enable，避免非預期的連續升級

**Case 5.4 — BuildingTable 缺少某棟建築的資料**
- 情境：CSV 缺 buildingID = 3（公會大廳）的行
- 行為：DataManager 載入時拋出 `MissingBuildingDataException`，遊戲無法啟動
- 文件標註：F-01 DataManager 載入階段驗證所有 6 個 buildingID 存在

**Case 5.5 — 存讀檔時建築等級超出 maxLevel（資料表被修改為較低上限）**
- 情境：存檔記錄委託板 `currentLevel = 5`，但新版 `BuildingTable` 將 `maxLevel` 改為 3
- 行為：讀檔時 FT-07 `RestoreFromSave` 內部驗證 `currentLevel <= BuildingTable[id].maxLevel`；超出則 **clamp 至 `maxLevel`** 並輸出 `Debug.LogWarning("buildingID={id}: clamped from {saved} to {maxLevel}")`；clamp 後續查 `effectData[maxLevel]` 必有效，不會拋例外
- 設計理由：採取 clamp 而非 fail-fast，避免 GDD/CSV 縮減 maxLevel 時舊存檔失效；玩家仍能繼續遊玩，僅該建築降回新上限（罕見路徑，正常開發中不會發生）

**Case 5.6 — 聲望閘判定時 FT-06 尚未初始化**
- 情境：場景載入順序異常，FT-07 在 FT-06 就緒前收到升級請求
- 行為：`FT06.GetCurrentLevel()` 回傳預設值（0 或拋出 null ref）→ 若回傳 0，聲望閘必然不通過（安全 fallback）；若拋例外則由呼叫端 catch
- 文件標註：依賴 Unity 場景初始化順序保證 FT-06 先於 FT-07 可用（由 gameplay-programmer 實作時以 Script Execution Order 保障）

**Case 5.7 — 職員休息室升級（FT-08 擴張後）**
- 情境：玩家建造職員休息室後逐級升級 L1~L5
- 行為：`BuildingTable[6].maxLevel = 5`，每級縮短 FT-08 面試自動刷新間隔（L1=86400s → L2=64800s → L3=43200s → L4=28800s → L5=21600s；詳 §7）
- FT-08 透過 `GetBuildingLevel(6)` 取得等級後讀 `BuildingTable[6].upgradeData[level].effectValue` 作為自動刷新間隔秒數；L0 未建造時 `IsStaffSystemUnlocked()` 回 `false`，FT-08 降級，不會讀取 `effectValue`

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（FT-07 讀取 / 呼叫的系統）

| 上游系統 | 介面 | 用途 | 類型 |
|---|---|---|---|
| **F-01 DataManager** | `LoadBuildingTable()` | 載入 BuildingTable CSV | 同步查詢 |
| **F-03 Resource Management** | `GetGold(): int` | 升級前金幣閘門判定 | 同步查詢 |
| **F-03 Resource Management** | `AddGold(int amount)` | 扣除升級費用；走 `AddGold`（非 `AddGoldAllowBankruptcy`），因建築升級為主動消費，不允許讓金幣進入負值（§5.2） | 控制 API |
| **FT-06 Guild Core** | `GetCurrentLevel(): int` | 聲望閘門判定（nextLevel >= 3 時） | 同步查詢 |

**硬依賴**：F-01、F-03、FT-06 皆為必要依賴，任一缺失 FT-07 無法運作。

### 6.2 下游系統（讀取 FT-07 的系統）

| 下游系統 | 查詢 API | 用途 |
|---|---|---|
| **FT-01 Adventurer Recruitment** | `GetRosterCap()` | 招募名冊上限閘門 |
| **FT-01 Adventurer Recruitment** | `GetRecruitRefreshInterval()` | 候選池刷新計時 |
| **FT-02 Mission Dispatch** | `GetMaxConcurrentMissions()` | 同時派遣上限閘門 |
| **F-03 Resource Management** | `SetBankruptcyWarningDuration(int)` | FT-07 主動推送：啟動時（`Start()`）與保險櫃升級時，呼叫 `F03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())` 寫入破產倒數秒數；F-03 被動接收，不查詢 FT-07 |
| **FT-08 Guild Staff** | `IsStaffSystemUnlocked()` | 判斷職員系統是否可用 |
| **FT-08 Guild Staff** | `GetBuildingLevel(6)` | 自動刷新間隔階梯（讀 `BuildingTable[6, level].effectValue`）|
| **FT-08 Guild Staff** | `BuildingTable[buildingID].slotCount` | slot 指派 capacity（FT-08 §3.7 直接讀資料表，無需經 FT-07 API）|
| **P-02 Main UI** | `GetBuildingLevel(id)` / 所有效果 API | 建築管理 UI 呈現、升級按鈕狀態 |
| **P-02 Main UI** | `GetMissionSlotCount()` | 委託板顯示槽數 |
| **P-03 Notification** | `OnBuildingUpgraded` 事件 | 升級完成通知 |
| **FT-10 Save/Load** | `BuildingState[]` 全量序列化 / 還原 | 存讀檔內容 |

### 6.3 與 FT-06 的職責切割（補丁登記）

FT-06 GDD 需補丁：

- [x] `GuildLevelTable` 移除 `rosterCap`、`maxMissions` 欄位（改由 FT-07 提供）
- [x] `GetRosterCap()` API 從 FT-06 移除（改由 FT-07 提供）
- [x] `GetMaxMissions()` API 從 FT-06 移除（改由 FT-07 提供）
- [x] FT-06 §6.2 下游補登 FT-07（FT-07 查詢 `GetCurrentLevel()` 做聲望閘）

FT-01 GDD 需補丁：
- [x] §6.1 上游：`GetRosterCap()` 來源改為 FT-07（原登記為 FT-06）
- [x] §6.1 上游：新增 FT-07 `GetRecruitRefreshInterval()`

FT-02 GDD 需補丁：
- [x] §6.1 上游：`GetMaxMissions()` 來源改為 FT-07 `GetMaxConcurrentMissions()`（原登記為 FT-06）

### 6.4 開發順序

```
F-01 → F-03 → FT-06 → FT-07 → FT-01 / FT-02 / F-03（破產秒數）/ FT-08 / P-02
```

FT-07 自身 API 在 FT-06 就緒後即可實作；FT-01、FT-02 容量閘需等 FT-07 的 API 就緒。

### 6.5 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft07Buildings"` |
| `IsCritical` | `false`（Degradable；還原失敗時所有建築重置為初始等級，玩家失去已投入的升級費用，但核心循環仍可繼續） |

> **決策記錄**：FT-10 §3.3.4 將 FT-07 列為 Degradable。BuildingState 重置意味玩家失去所有已花費金幣的升級進度（最多 6 棟建築的升級費用），此為不可逆損失。此 `IsCritical = false` 決策遵循 FT-10 既定分類，如認為後果過重可提升為 Critical（請於 design-review 階段確認）。

**`Serialize()` 序列化欄位**：

序列化 `BuildingState[]`（6 棟建築），每棟僅保留 `currentLevel`：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `buildingID` | `int` | 建築 ID（1~6） |
| `currentLevel` | `int` | 當前等級（buildingID=6 可為 0；其他為 1~maxLevel） |

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化 6 筆 `BuildingState`。
2. 驗證每筆 `currentLevel ≤ BuildingTable[buildingID].maxLevel`；超出時 clamp 至 `maxLevel` 並輸出 `Debug.LogWarning`（對齊 §5 Case 5.5 / AC-14，不拋例外）。
3. 還原完成後，呼叫 `F-03.SetBankruptcyWarningDuration(GetBankruptcyWarningSeconds())` 推送預備金保險櫃等級對應的破產倒數秒數（對齊 §3.3 / §4.4-4.5 / AC-19）。

**`InitializeAsNewGame()` 預設值**：

| buildingID | name | `currentLevel` |
|---|---|---|
| 1 | 委託板 | `1` |
| 2 | 招募廣告欄 | `1` |
| 3 | 公會大廳 | `1` |
| 4 | 公會櫃臺 | `1` |
| 5 | 預備金保險櫃 | `1` |
| 6 | 職員休息室 | `0`（未建造） |

對應 FT-10 §3.3.3 拓撲順序 row 5、§3.3.4 Degradable 分類、§6.1 #15（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 BuildingTable.csv

**檔案位置**：`Assets/Resources/Data/Tables/BuildingTable.csv`

所有建築效果值、費用、等級上限皆在此表調整，不需改程式碼。

**Schema**：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `buildingID` | int | 複合主鍵欄 1（1–6） |
| `name` | string | 建築顯示名稱 |
| `maxLevel` | int | 最高等級上限（每棟建築各行內容需一致） |
| `level` | int | 複合主鍵欄 2（0..maxLevel；職員休息室含 level=0 行，其他建築從 level=1 起） |
| `effectValue` | int | 該等級的效果值（槽數 / 秒數 / 人數 / 布林語意） |
| `upgradeCost` | int | **升至本等級**所需金幣（每棟建築最小 level 行為初始態，固定為 0） |
| `guildLevelReq` | int | **升至本等級**所需公會等級（最小 level 行與 level=2 固定為 0；level>=3 依設計填 3/4/5） |
| `slotCount` | int | 該建築可同時容納的職員數量（FT-08 §3.7 讀取為 `staff.assignedBuildingID == buildingID` 的 capacity 上限）。Jam 版採「逐建築固定值、不隨 level 增長」原則：未提供職員位置的建築固定 0；提供職員位置的建築（委託板 / 公會櫃臺 / 預備金保險櫃）所有 level 皆為相同值。L0 行（職員休息室）固定 0 |
| `maintenanceCost` | int | **Phase 2**：每日維護費（金幣）；L1 與 L0 填 0（免費）；FT-07 `OnDailyReset` 讀取並計算 `perBuildingCost`，Jam 版不讀此欄 |

> **複合主鍵**：`(buildingID, level)`，每棟建築的每個等級各佔一行。
> **初始等級**：由 `BuildingTable` 中該 `buildingID` **最小 level 行**決定——職員休息室的最小 level 為 `0`（初始未建造），其他建築的最小 level 為 `1`（初始已建造）。

**完整預設值**（可直接作為 CSV 初稿）：

| buildingID | name | maxLevel | level | effectValue | upgradeCost | guildLevelReq | slotCount | maintenanceCost |
|---|---|---|---|---|---|---|---|---|
| 1 | 委託板 | 5 | 1 | 5 | 0 | 0 | 3 | 0 |
| 1 | 委託板 | 5 | 2 | 8 | 150 | 0 | 3 | 15 |
| 1 | 委託板 | 5 | 3 | 11 | 350 | 3 | 3 | 30 |
| 1 | 委託板 | 5 | 4 | 14 | 700 | 4 | 3 | 55 |
| 1 | 委託板 | 5 | 5 | 17 | 1200 | 5 | 3 | 80 |
| 2 | 招募廣告欄 | 3 | 1 | 86400 | 0 | 0 | 0 | 0 |
| 2 | 招募廣告欄 | 3 | 2 | 57600 | 250 | 0 | 0 | 20 |
| 2 | 招募廣告欄 | 3 | 3 | 28800 | 600 | 3 | 0 | 40 |
| 3 | 公會大廳 | 5 | 1 | 10 | 0 | 0 | 0 | 0 |
| 3 | 公會大廳 | 5 | 2 | 15 | 400 | 0 | 0 | 20 |
| 3 | 公會大廳 | 5 | 3 | 20 | 900 | 3 | 0 | 45 |
| 3 | 公會大廳 | 5 | 4 | 25 | 1600 | 4 | 0 | 80 |
| 3 | 公會大廳 | 5 | 5 | 30 | 2500 | 5 | 0 | 120 |
| 4 | 公會櫃臺 | 5 | 1 | 5 | 0 | 0 | 2 | 0 |
| 4 | 公會櫃臺 | 5 | 2 | 8 | 250 | 0 | 2 | 20 |
| 4 | 公會櫃臺 | 5 | 3 | 11 | 500 | 3 | 2 | 40 |
| 4 | 公會櫃臺 | 5 | 4 | 14 | 900 | 4 | 2 | 70 |
| 4 | 公會櫃臺 | 5 | 5 | 18 | 1400 | 5 | 2 | 100 |
| 5 | 預備金保險櫃 | 5 | 1 | 10800 | 0 | 0 | 1 | 0 |
| 5 | 預備金保險櫃 | 5 | 2 | 21600 | 200 | 0 | 1 | 15 |
| 5 | 預備金保險櫃 | 5 | 3 | 43200 | 450 | 3 | 1 | 30 |
| 5 | 預備金保險櫃 | 5 | 4 | 86400 | 800 | 4 | 1 | 50 |
| 5 | 預備金保險櫃 | 5 | 5 | 172800 | 1500 | 5 | 1 | 75 |
| 6 | 職員休息室 | 5 | 0 | 0 | 0 | 0 | 0 | 0 |
| 6 | 職員休息室 | 5 | 1 | 86400 | 500 | 0 | 0 | 0 |
| 6 | 職員休息室 | 5 | 2 | 64800 | 1500 | 2 | 0 | 40 |
| 6 | 職員休息室 | 5 | 3 | 43200 | 3000 | 3 | 0 | 90 |
| 6 | 職員休息室 | 5 | 4 | 28800 | 7000 | 4 | 0 | 160 |
| 6 | 職員休息室 | 5 | 5 | 21600 | 15000 | 5 | 0 | 240 |

> 招募廣告欄 `effectValue` 單位為秒（86400 = 24h，57600 = 16h，28800 = 8h）。預備金保險櫃同。職員休息室 `effectValue` 亦為秒，代表 FT-08 面試自動刷新間隔（L1=24h → L5=6h 階梯）；L0 時 `GetBuildingLevel(6) == 0`，`IsStaffSystemUnlocked()` 回 `false`，FT-08 降級不讀此欄。
> `slotCount` 對應 FT-08 §3.7 slot 指派 capacity；Jam 版三個有 slot 的建築值為「委託板=3 / 公會櫃臺=2 / 預備金保險櫃=1」（FT-08 §3.6.3 `AccountantPenaltyOnVault` slot capacity = 1 自然封頂、§4.2 `RecruitRefreshOnCounter` 受 buildingID=4 slotCount 限制）。其他三棟（招募廣告欄 / 公會大廳 / 職員休息室）為 `0`，FT-08 `TryAssignStaff` 對 capacity=0 的 buildingID 一律拒絕指派。後續若調整槽數，FT-08 需重新 grep slotCount 引用點同步。

### 7.2 調整指南

| 調整目標 | 修改欄位 | 安全範圍 | 注意事項 |
|---|---|---|---|
| 升級費用整體調高 / 降低 | `upgradeCost` | 各建築總費用建議不超過同期預期累積收入的 50% | 與 FT-05 金流模型對齊 |
| 縮短招募刷新時間 | `effectValue`（buildingID=2） | 最短建議 ≥ 3600s（1h），避免刷新過快失去等待感 | 單位為秒 |
| 調整破產倒數緩衝 | `effectValue`（buildingID=5） | 最短 1800s（30min），最長 604800s（7 天） | 若低於 3600s，玩家幾乎沒有反應時間；**L1 = 3h（10,800s）設定需配合 D 難度任務 2.5h 時長留有餘量**——破產倒數啟動後，玩家至少可完成一張 D 難度任務（baseDuration=150 分鐘=9,000s）回收傭金再解除警告，3h 緩衝（10,800s）>D 難度時長（9,000s），餘量 30 分鐘。若未來調整 D 難度時長，需同步確認 L1 緩衝仍大於 D 難度時長 |
| 調整名冊或並行上限 | `effectValue`（buildingID=3/4） | 名冊上限建議不超過 50；並行任務上限建議不超過名冊上限的 70% | 並行過高會讓閒置冒險者消失，損害視覺設計 |
| 提高聲望閘門等級 | `guildLevelReq` | 僅允許 0、3、4、5（對應 FT-06 等級） | 不可設為 1 或 2（設計規則：前兩級只用金幣軸） |

### 7.3 不可調參數（設計決策）

- 「雙軌閘門」語意：Lv1→Lv2 僅受金幣軸限制，Lv3+ 同時受聲望軸限制——此規則以 CSV 資料呈現（L2 `guildLevelReq = 0`、L3+ `guildLevelReq >= 3`），程式不再硬編碼 `nextLevel >= 3` 特例
- 升級費用不可使金幣進入負值（設計規則，非配置）
- 職員休息室 `maxLevel = 5`（FT-08 決定的自動刷新間隔階梯 L1=24h → L5=6h；升級費與等級門檻見 §7.1 表）
- `slotCount` 不隨 level 變動（Jam 範圍）：每棟建築所有 level 行的 `slotCount` 相同——避免「升級即新增 slot」造成 FT-08 capacity 跳變、聚合上限重算、UI 動態重排等連動問題；Post-Jam 若需 level-scaled slot，需同步調整 FT-08 §3.7 capacity 重算流程 + §3.6 effect 聚合上限觀察 + P-02 slot UI 重排規則 |

---

## 8. 驗收標準（Acceptance Criteria）

### 8.1 初始化

- **AC-1**：新遊戲啟動後，`GetBuildingLevel(1..5)` 皆回傳 `1`；`GetBuildingLevel(6)` 回傳 `0`
- **AC-2**：`GetMissionSlotCount() == 5`、`GetRosterCap() == 10`、`GetMaxConcurrentMissions() == 5`、`GetRecruitRefreshInterval() == 86400s`、`GetBankruptcyWarningSeconds() == 10800`、`IsStaffSystemUnlocked() == false`

### 8.2 升級流程

- **AC-3**：金幣 ≥ 升級費用、聲望閘通過時，`TryUpgradeBuilding()` 回傳 `SUCCESS`，建築等級 +1，金幣正確扣除，`OnBuildingUpgraded` 事件發出
- **AC-4**：金幣不足時，`TryUpgradeBuilding()` 回傳 `GOLD_INSUFFICIENT`，建築等級不變，金幣不變，事件不發出
- **AC-5**：建築已滿級時，`TryUpgradeBuilding()` 回傳 `ALREADY_MAX`，狀態不變

### 8.3 聲望閘門

- **AC-6**：公會 Lv2，嘗試升任意建築至 L3 → 回傳 `GUILD_LEVEL_INSUFFICIENT`，等級不變
- **AC-7**：升級至 L2（nextLevel = 2）時，即使公會 Lv1 也**不觸發**聲望閘，只需金幣足夠即可通過
- **AC-8**：公會 Lv3，嘗試升委託板 L2→L3（費用 350g，金幣足夠）→ 回傳 `SUCCESS`，`GetMissionSlotCount()` 立即回傳 `11`

### 8.4 效果值即時生效

- **AC-9**：委託板升至 L3 後，`GetMissionSlotCount()` 同一 frame 回傳 `11`（不需重啟或重載）
- **AC-10**：職員休息室從 L0 升至 L1 後，`IsStaffSystemUnlocked()` 立即回傳 `true`
- **AC-11**：預備金保險櫃升至 L2 後，`GetBankruptcyWarningSeconds()` 回傳 `21600`（6h）

### 8.5 防呆

- **AC-12**：UI 快速雙擊升級按鈕，若第一次已成功且金幣不足，第二次回傳 `GOLD_INSUFFICIENT`，不重複扣款
- **AC-13**：`BuildingTable` CSV 缺少任一 buildingID（1–6）的任一等級行，DataManager 載入失敗並輸出錯誤，遊戲不進入 Play 狀態
- **AC-14**：讀取存檔後，若某建築 `currentLevel` 超出 `BuildingTable.maxLevel`，系統 clamp 至 `maxLevel` 並輸出 `Debug.LogWarning`，不拋例外

### 8.6 存讀檔

- **AC-15**：存檔後重新載入，所有建築等級與升級前一致；`GetMissionSlotCount()` 等效果 API 回傳值與存檔前相同

### 8.7 破產倒數推送

- **AC-16**：新遊戲 `Start()` 完成後，`F03.GetBankruptcyWarningDuration()` == `10800`（預備金保險櫃 L1 對應 3h）
- **AC-17**：預備金保險櫃從 L1 升至 L5 後，`F03.GetBankruptcyWarningDuration()` == `172800`（48h）；每次升級（L1→L2、L2→L3 等）後值即時更新
- **AC-18**：升級其他建築（委託板、公會大廳等，`buildingID != 5`）後，`F03.GetBankruptcyWarningDuration()` 不變
- **AC-19**：存檔載入後 `Start()` 以存檔等級推送正確秒數——例如保險櫃存檔等級為 L3，`Start()` 後 `F03.GetBankruptcyWarningDuration()` == `43200`
