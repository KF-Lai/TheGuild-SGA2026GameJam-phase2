# Guild Core 系統設計文件

_建立時間：2026-04-22_
_狀態：設計完成（Section 1-8 全部完成，待 /design-review）_
_系統 ID：FT-06_

---

## 1. 概要（Overview）

**Guild Core（FT-06）是公會層級狀態的統一管理系統**，負責維護「公會作為一個組織」的全局資料與生命週期事件。本系統涵蓋三大職責：

### A. 公會等級系統

訂閱 F-03 `OnReputationChanged` 事件，依 `GuildLevelTable` 門檻即時判定升級。支援單次聲望變動跨多級的「連跳」情境——逐級發布 `OnGuildLevelChanged`（Lv1→Lv4 則發 3 次），payload 攜帶 `isMultiJump` / `finalTargetLv` 供下游（P-02 UI、P-03 通知）選擇聚合顯示或逐級動畫。公會等級僅升不降。

對外提供同步查詢 API（`GetMaxRecruitableRank()` / `GetMaxMissionDifficulty()` / `GetCurrentLevel()` / `GetCurrentTitle()` 等;`GetMaxDifficulty()` 為 deprecated alias,見 §3.6）,皆即時讀 `GuildLevelTable` 不維護快取。

> **職責切割（2026-04-23 更新）**：原 `GuildLevelTable.rosterCap` / `maxMissions` 欄位與對應 `GetRosterCap()` / `GetMaxMissions()` API 已**移交 FT-07 Guild Building System**（見 FT-07 §3.7 / §6.3）。FT-06 僅保留：等級判定（聲望門檻）、稱號、可接難度上限（`maxDifficulty`）。容量類查詢由 FT-07 `GetRosterCap()` / `GetMaxConcurrentMissions()` 提供。

### B. Game Over 流程

訂閱 F-03 `OnBankruptcyWarningStateChanged(Bankrupt)` 事件，採兩階段流程：

1. **階段 1**：收到破產通知 → 僅發布 `OnGameOverPending`，P-02 顯示破產訃聞 / 確認畫面
2. **階段 2**：玩家點擊確認 → 呼叫 F-02 暫停 tick、發布 `OnGameOver`，觸發 P-02 結算畫面與 FT-10 封存存檔

### C. 公會基礎狀態

維護公會名稱（玩家初始輸入 + 固定「公會」後綴，如「約翰的公會」；留空則為「公會」）、創立時間（UTC Unix seconds）、總結統計佔位（供結算畫面讀取）。名稱僅顯示用，無邏輯效果。

### 核心職責

1. 訂閱 F-03 聲望變動 → 維護公會等級
2. 提供難度上限查詢 API 給 FT-01（老手邀請可招募最高階級）；任務難度軸資料型別對齊 C-01
3. 監聽破產事件 → 驅動 Game Over 兩階段流程
4. 儲存公會識別資料（名稱、創立時間）

### 不負責

- 聲望數值運算（F-03 負責）
- 破產判定邏輯（F-03 負責）
- 存檔寫入（FT-10 負責）
- 結算畫面內容（P-02 負責）
- 時間 tick 暫停的實作細節（F-02 負責，FT-06 僅觸發）

### 資料表

`GuildLevelTable`（5 級，欄位：`level`、`reputationThreshold`、`title`、`maxDifficulty`（deprecated）、`maxRecruitableRank`、`maxMissionDifficulty`）

> 原 `maxMissions`、`rosterCap` 兩欄位已移至 FT-07 `BuildingTable`（公會櫃臺 → maxConcurrentMissions；公會大廳 → rosterCap），見 FT-07 §3.5。

---

## 2. 玩家幻想（Player Fantasy）

### 目標情緒（MDA Aesthetics）

FT-06 承載兩種對比強烈的情緒：

**成長曲線（Growth / Fellowship / Expression）**
- 升級瞬間的「我做到了」——看著公會從 Lv1 小作坊逐步蛻變為 Lv5 冒險者殿堂
- 稱號的象徵意義：「新手冒險者公會」→「名聲顯赫的冒險者公會」，每次升級都是可炫耀的里程碑
- 容量解鎖的具體好處：多一名冒險者、多接一筆委託、挑戰更高難度——等級不只是數字，是選項變多

**終局敘事（Submission / Narrative）**
- 破產的沉重：公會訃聞、經營總結——這不是遊戲結束畫面，是一段故事的終章
- 兩階段 Game Over 的設計意圖：先給玩家「消化」的時間（階段 1），再進入正式結算（階段 2），避免突兀的「GAME OVER」橫幅
- 公會命名（如「約翰的公會」）讓玩家投射情感——破產時失去的不只是遊戲進度，是「自己的公會」

### 玩家幻想敘事

> 「我是一間公會的創立者。這間公會從最初的小作坊——只有我與兩三個冒險者——慢慢成長為遠近馳名的冒險者殿堂。每一次聲望的累積，都讓公會的名聲更響亮；每一次升級，都讓我能雇用更多夥伴、接下更大的委託。但若哪天帳上見底、無力償還——公會也會留下它自己的故事。」

### 設計原則（FT-06 如何強化幻想）

1. **升級即時回饋**：聲望達標瞬間發事件（不等日結），玩家的努力立刻被系統「看見」
2. **連跳的儀式感**：逐級發事件讓 P-02/P-03 可設計逐級動畫（即使極少發生，單級演出也一致）
3. **稱號勝於等級數字**：payload 攜帶 `fromTitle` / `toTitle`，UI 應強調稱號變化（「新手」→「初階」）
4. **破產不是失敗，是終章**：兩階段 Game Over 給玩家緩衝，配合 P-02 訃聞設計讓結束有敘事重量
5. **公會名稱個人化**：玩家命名權換取情感投射，強化「這是我的公會」

### 關鍵情緒節點（Emotional Beats）

| 節點 | 觸發事件 | 期望情緒 |
|---|---|---|
| 首次升級（Lv1→Lv2） | `OnGuildLevelChanged` | 驚喜、成就感 |
| 中期升級（Lv2→Lv3、Lv3→Lv4） | `OnGuildLevelChanged` | 階段達成、策略擴展 |
| 頂級（Lv4→Lv5） | `OnGuildLevelChanged` (isMax) | 巔峰、可炫耀 |
| 進入 Warning | （由 F-03 發，FT-06 不涉入） | 緊張、壓力 |
| 破產訃聞 | `OnGameOverPending` | 沉重、反思 |
| 結算畫面 | `OnGameOver` | 敘事收尾、回顧旅程 |

---

## 3. 詳細規則（Detailed Rules）

### 3.1 系統狀態（GuildState）

```
GuildState {                       // ↓ 持久化欄位（FT-10 序列化）
    guildName: string              // 玩家輸入的原始字串（不含「公會」後綴）
    displayName: string            // 組合後顯示名稱（「{guildName}公會」或「公會」）
    foundingTimestamp: long        // 公會創立時間（UTC Unix seconds，從 F-02 取）
    currentLevel: int              // 1..5，僅升不降
    gameOverState: enum { Active, Pending, Over }
}

// Runtime-only 狀態（不序列化，見 § 5.2.3）：
//   pendingLevelUpQueue : Queue<LevelUpPayload>
//   _tickPausedRequested : bool（內部旗標，§ 3.8 使用）
```

- `guildName` 最大長度 **8 個全型字（= 16 個半型字元）**；超過截斷
- `currentLevel` 從 `GuildLevelTable` 查其他衍生欄位（title、maxDifficulty），**不快取**；容量類（rosterCap、maxConcurrentMissions）改由 FT-07 提供
- `pendingLevelUpQueue` 為 runtime 狀態，不參與 FT-10 序列化——讀檔後不補發「升到 Lv N」事件（見 § 5.2.3）

### 3.2 初始化流程

**新遊戲**：
1. 玩家於 P-01 輸入公會名稱（可留空）
2. 組合 `displayName`：
   - `guildName` 非空 → `displayName = guildName + "公會"`
   - `guildName` 空 → `displayName = "公會"`
3. `foundingTimestamp = F02.NowUTC`
4. `currentLevel = 1`（聲望 = 0，對應 `GuildLevelTable.level[1]`）
5. `gameOverState = Active`
6. 訂閱 F-03 `OnReputationChanged` 與 `OnBankruptcyWarningStateChanged`
7. 發布 `OnGuildInitialized(displayName, foundingTimestamp)`

**讀檔**（FT-10 驅動）：
1. FT-10 還原 `GuildState` 全部欄位（含 `currentLevel` > 1、`gameOverState` 為任一值的情況）
2. FT-06 重新訂閱 F-03 事件
3. 不重新判定等級（存檔內容為權威）
4. 發布 `OnGuildLoaded(displayName, currentLevel)`
5. **Pending 態讀檔行為**：若還原後 `gameOverState == Pending`，FT-06 **不重發** `OnGameOverPending`；P-02 於 `OnGuildLoaded` 後主動查詢 `IsGameOverPending()` 決定是否重新顯示訃聞畫面。若還原後 `gameOverState == Over`，P-02 查詢 `IsGameOver()` 直接進結算畫面。

### 3.3 公會等級判定演算法

訂閱 F-03 `OnReputationChanged(int newValue, int delta)`（F-03 § 3 規格；FT-06 只讀 `newValue` 作為當下聲望快照，忽略 `delta`）：

```
OnReputationChanged(newValue, delta):
    // delta 供其他訂閱者使用，FT-06 僅以 newValue（= F-03 當下聲望）為判定依據
    targetLevel = 查 GuildLevelTable，找出最高符合 newValue >= reputationThreshold 的 level
    IF targetLevel > currentLevel:
        排程連跳（見 3.4）—— 不在此 frame 內發事件
    // targetLevel <= currentLevel：不降級，忽略
```

**不降級原則**：即使聲望因某種機制回跌，`currentLevel` 維持最高值。

### 3.4 連跳處理（Multi-Jump Queue）

設計意圖：**queue 到下一 frame 再觸發**，避免事件處理器在同一 frame 內遞迴或阻塞。

```
排程連跳（targetLevel）:
    jumpCount = targetLevel - currentLevel
    isMultiJump = (jumpCount > 1)
    將每一級升級排入 pendingLevelUpQueue，payload 預先快照：
        reputationAtUpgrade = newValue（觸發當下的聲望，即 § 3.3 收到的 newValue）
        finalTargetLv = targetLevel
        isMultiJump 旗標

MonoBehaviour.Update()（或 Coroutine 單步）:
    IF pendingLevelUpQueue 非空:
        取出最前一項
        currentLevel = toLv
        發布 OnGuildLevelChanged(payload)
        // 一 frame 發一級，留空間給 P-02/P-03 做動畫
```

**連跳順序**：按等級低→高依序發（Lv1→2→3→4）。

**連跳期間聲望再變動**：若 queue 未清空前又來一次 `OnReputationChanged`：
- 若新 targetLevel > 已排程 finalTargetLv：擴充 queue（補上新級數），更新所有 pending payload 的 `finalTargetLv`
- 若新 targetLevel <= 已排程 finalTargetLv：忽略（不降級）

**事件 Payload 結構**：

```
OnGuildLevelChanged payload:
    fromLv: int
    toLv: int
    fromTitle: string
    toTitle: string
    newMaxDifficulty: string        // [Deprecated] 等同 newMaxRecruitableRank；保留向後相容，下次 review 應移除
    newMaxRecruitableRank: string   // 升級後的老手招募最高階級（D~S）
    newMaxMissionDifficulty: string // 升級後的任務難度上限（D~SS）
    reputationAtUpgrade: int
    upgradeTimestamp: long          // 實際發出事件瞬間的 UTC
    isMultiJump: bool
    finalTargetLv: int
```

> 容量類欄位（newMaxMissions、newRosterCap）已自 payload 移除；下游若需要容量，呼叫 FT-07 `GetRosterCap()` / `GetMaxConcurrentMissions()` 即時查詢。

### 3.5 公會等級表（GuildLevelTable）

| level | reputationThreshold | title | maxDifficulty | maxRecruitableRank | maxMissionDifficulty |
|---|---|---|---|---|---|
| 1 | 0 | 新手冒險者公會 | D | D | D |
| 2 | 30 | 初階冒險者公會 | C | C | C |
| 3 | 80 | 中階冒險者公會 | B | B | B |
| 4 | 200 | 高階冒險者公會 | A | A | A |
| 5 | 400 | 名聲顯赫的冒險者公會 | S | S | SS |

- 資料源：`Assets/Resources/Data/Tables/GuildLevelTable.csv`
- 閾值、稱號將交由 haiku 精修；難度需與 C-01 難度軸對齊
- 本表欄位為 FT-06 對外唯一等級資料源
- 原 `maxMissions`、`rosterCap` 兩欄位改由 FT-07 `BuildingTable` 提供（公會櫃臺、公會大廳），見 FT-07 §3.5
- `maxDifficulty` 欄位保留（向後相容，legacy）；`maxRecruitableRank`（冒險者招募上限）與 `maxMissionDifficulty`（任務難度上限）為新拆分欄位，語意更明確

### 3.6 容量查詢 API

全部同步、即時讀 `GuildLevelTable.level[currentLevel]`，**不快取**：

| API | 回傳 | 用途 |
|---|---|---|
| `GetCurrentLevel()` | `int` | 目前等級（1..5） |
| `GetCurrentTitle()` | `string` | 目前稱號 |
| `GetMaxRecruitableRank()` | `string` | 老手招募可邀請的最高冒險者階級（D~S）；讀 `GuildLevelTable.maxRecruitableRank`；FT-01 老手邀請上限使用 |
| `GetMaxMissionDifficulty()` | `string` | 常規任務可生成的最高難度上限（D~SS）；讀 `GuildLevelTable.maxMissionDifficulty`；P-02 委託板與 C-06 池過濾使用（待相關系統採用） |
| `GetMaxDifficulty()` | `string` | **[Deprecated]** 向後相容 alias；內部呼叫 `GetMaxRecruitableRank()` 並於 Log 輸出 `[Deprecated] GetMaxDifficulty() — 請改用 GetMaxRecruitableRank()`；下次 review 應移除 |
| `GetGuildDisplayName()` | `string` | 顯示名稱（含「公會」後綴） |
| `GetFoundingTimestamp()` | `long` | 創立時間 UTC |

> 原 `GetRosterCap()` / `GetMaxMissions()` 已移除，改由 FT-07 `GetRosterCap()` / `GetMaxConcurrentMissions()` 提供（FT-07 §3.4）。

> 等級讀檔後連續呼叫這些 API 應與等級未變的情況表現一致（不需重新訂閱即可正確回傳新等級的容量）。

### 3.7 Game Over 流程 — 階段 1（Pending）

訂閱 F-03 `OnBankruptcyWarningStateChanged(newState)`：

```
OnBankruptcyWarningStateChanged(newState):
    IF newState == Bankrupt AND gameOverState == Active:
        gameOverState = Pending
        發布 OnGameOverPending(snapshot)
    // newState != Bankrupt 或已 Pending/Over：忽略
```

**冪等性**：收到第二次 `Bankrupt` 不再發事件（`gameOverState` 已是 Pending）。

**階段 1 期間系統行為**：
- F-02 tick **不暫停**（UI 動畫需要時間流逝）
- 其他系統事件照常（但 P-02 應攔截輸入，避免玩家在訃聞期間繼續操作）
- 不接受新任務、不結算現有任務的流程由 P-02 UI 阻塞處理（FT-06 不介入）

**`OnGameOverPending` payload**：

```
pendingTimestamp: long          // UTC
finalGoldBeforeGameOver: int    // F-03 快照
finalReputation: int            // F-03 快照
finalLevel: int                 // currentLevel 快照
finalTitle: string
guildDisplayName: string
foundingTimestamp: long
```

### 3.8 Game Over 流程 — 階段 2（Over）

由 P-02 於玩家確認後呼叫 `FT06.ConfirmGameOver()`：

```
ConfirmGameOver():
    IF gameOverState != Pending: return（冪等）
    呼叫 F02.PauseTick()
    gameOverState = Over
    發布 OnGameOver(payload)
    // FT-10 監聽 OnGameOver 進行存檔終止寫入（見下方註記）
    // P-02 監聽 OnGameOver 進入結算畫面
```

**FT-06 與 FT-10 的 Game Over 存檔介面合約**：
- FT-06 僅負責發布 `OnGameOver(payload)`，**不規定** FT-10 的「封存」策略（read-only flag / slot 重新命名 / 另存終末存檔皆由 FT-10 自決）
- FT-06 保證的契約僅有一項：`OnGameOver` 發出後 `gameOverState` 永久為 `Over`、`F-02` tick 已暫停，其餘狀態不再變動；FT-10 可依此快照 `GuildState` 與其他系統狀態
- 若 FT-10 選擇允許玩家「從 Game Over 存檔載入」，載入後 FT-06 依 § 3.2 讀檔流程還原 `gameOverState = Over`，P-02 查詢 `IsGameOver()` 直接進結算畫面

**`OnGameOver` payload**：沿用階段 1 payload + 確認時間戳 `confirmTimestamp`。

**階段 2 之後**：`gameOverState = Over`，FT-06 API 仍可讀取（供結算畫面查詢最終狀態），但不再處理任何 F-03 事件。

### 3.9 公會名稱處理

**輸入驗證**（P-01 輸入階段由 UI 負責，FT-06 作為防禦性檢查）：
- 最大長度：**8 個全型字（16 個半型字元）**，超過截斷
- 允許空字串
- 允許純半型字元（如 "Adventurer"）
- 不允許換行、控制字元（`\r`、`\n`、`\t` 等） → strip

**組合規則**：
```
ComposeDisplayName(guildName):
    trimmed = guildName.Trim()
    IF string.IsNullOrEmpty(trimmed):
        return "公會"
    ELSE:
        return trimmed + "公會"
```

**示例**：
- 輸入「約翰的」 → `displayName = "約翰的公會"`
- 輸入「」 → `displayName = "公會"`
- 輸入「諾爾村」 → `displayName = "諾爾村公會"`

### 3.10 事件發布契約

| 事件 | 觸發時機 | 訂閱者 |
|---|---|---|
| `OnGuildInitialized` | 新遊戲初始化完成 | P-02（顯示公會名稱）、FT-10（觸發首次存檔） |
| `OnGuildLoaded` | 讀檔完成 | P-02 |
| `OnGuildLevelChanged` | 連跳 queue 每 frame 取出一級 | P-02（UI 動畫）、P-03（通知）、FT-01/FT-02（容量變更通知） |
| `OnGameOverPending` | 首次收到 F-03 Bankrupt 事件 | P-02（訃聞畫面） |
| `OnGameOver` | 玩家於階段 1 確認 | P-02（結算畫面）、FT-10（封存存檔） |

### 3.11 查詢 API 總表

見 3.6 所列容量查詢 API，加上：

| API | 回傳 | 用途 |
|---|---|---|
| `IsGameOverPending()` | `bool` | P-02 判斷是否顯示訃聞 |
| `IsGameOver()` | `bool` | 全系統判斷是否進入結算狀態 |
| `ConfirmGameOver()` | `void` | P-02 於玩家確認後呼叫 |

---

## 4. 公式（Formulas）

### 4.1 等級判定公式

給定當前聲望 `rep`，求 `targetLevel`：

```
targetLevel = max { L ∈ {1, 2, 3, 4, 5}  |  GuildLevelTable[L].reputationThreshold <= rep }
```

**說明**：
- 表必須按 `reputationThreshold` 升冪排列（Lv1=0, Lv2=100, ...）
- 若 `rep >= GuildLevelTable[5].reputationThreshold`，則 `targetLevel = 5`（頂級）
- `rep < 0` 為正常情境（F-03 聲望下限 `-100`），`targetLevel = 1`，因不降級原則忽略

**實作演算法**（從高到低線性掃描，O(5)）：

```
FindTargetLevel(rep):
    FOR L = 5 DOWN TO 1:
        IF GuildLevelTable[L].reputationThreshold <= rep:
            return L
    return 1  // 理論不該到達
```

### 4.2 升級判定公式

```
shouldUpgrade = (targetLevel > currentLevel)
jumpCount = max(0, targetLevel - currentLevel)
isMultiJump = (jumpCount > 1)
```

### 4.3 連跳事件序列生成

給定 `currentLevel = C`、`targetLevel = T`（T > C），生成事件序列：

```
FOR k = 1 TO (T - C):
    event[k] = OnGuildLevelChanged {
        fromLv: C + k - 1
        toLv:   C + k
        fromTitle: GuildLevelTable[C + k - 1].title
        toTitle:   GuildLevelTable[C + k].title
        newMaxDifficulty: GuildLevelTable[C + k].maxDifficulty             // [Deprecated] 向後相容
        newMaxRecruitableRank: GuildLevelTable[C + k].maxRecruitableRank
        newMaxMissionDifficulty: GuildLevelTable[C + k].maxMissionDifficulty
        reputationAtUpgrade: rep  // 觸發瞬間聲望
        upgradeTimestamp: F02.NowUTC           // 實際發出瞬間
        isMultiJump: (T - C > 1)
        finalTargetLv: T
    }
```

**範例**（C=1, T=4, rep=700）：

| k | fromLv | toLv | fromTitle | toTitle | isMultiJump | finalTargetLv |
|---|---|---|---|---|---|---|
| 1 | 1 | 2 | 新手冒險者公會 | 初階冒險者公會 | true | 4 |
| 2 | 2 | 3 | 初階冒險者公會 | 中階冒險者公會 | true | 4 |
| 3 | 3 | 4 | 中階冒險者公會 | 高階冒險者公會 | true | 4 |

**注意**：每一級的 `upgradeTimestamp` **在取出 queue 發送時**才 stamp（跨 frame 時間會略有不同，這是刻意設計——P-03 通知記錄真實發送時間）。

### 4.4 公會名稱長度驗證

```
IsValidGuildName(input):
    trimmed = input.Trim()
    charCount = CountDisplayChars(trimmed)   // 全型字記 1，半型字記 0.5，向上取整
    return charCount <= 8
```

**`CountDisplayChars` 定義**：

```
CountDisplayChars(s):
    count = 0.0
    FOR each char c in s:
        IF IsFullWidth(c): count += 1.0
        ELSE: count += 0.5
    return Ceil(count)
```

**`IsFullWidth` 判定**：Unicode 範圍 `U+4E00..U+9FFF`（CJK 統一表意）、`U+3040..U+30FF`（假名）、`U+AC00..U+D7AF`（諺文）等；ASCII 與半型字元為半型。

**示例**：
- 「約翰的公會老闆」= 7 個全型字 = 7.0 → 通過
- 「AdventurerGuild」= 15 個半型字 = 7.5 → Ceil = 8 → 通過
- 「我的冒險者公會管理員」= 10 個全型字 = 10.0 → 拒絕

> 若「8 全型 = 16 半型」邊界算法細節由 UI 層實作有偏好，可在 UI 側統一，但 FT-06 防禦性檢查使用上述公式。

### 4.5 Game Over 狀態轉移

有限狀態機：

```
States: { Active, Pending, Over }

Transitions:
    Active → Pending:  F-03 OnBankruptcyWarningStateChanged(Bankrupt)
    Pending → Over:    P-02 呼叫 ConfirmGameOver()
    Over → *:          無（終態）
    Pending → Active:  無（不可回復）
    Active → Over:     無（必須經過 Pending）

Invariants:
    IF gameOverState == Over:
        F-02 tick 已暫停
    IF gameOverState != Active:
        不再處理 OnReputationChanged（等級凍結）
```

> 「Over 態不再處理 `OnReputationChanged`」為新增語意，防止結算期間聲望變動導致延遲升級事件。

---

## 5. 邊緣案例（Edge Cases）

### 5.1 等級判定異常

**Case 5.1.1 — 聲望回跌（F-03 以 AddReputation(負值) 扣除）**
- 情境：`currentLevel = 3`（對應 rep ≥ 300），F-03 扣聲望至 rep = 250
- 行為：FT-06 收到 `OnReputationChanged(250, delta)`，計算 `targetLevel = 2`，因 `targetLevel < currentLevel`，**忽略**，`currentLevel` 維持 3
- 文件標註：與 Section 3.3「不降級原則」一致

**Case 5.1.2 — 聲望為負數**
- 情境：`rep < 0` 為正常情境（F-03 聲望下限為 `-100`）。若 FT-06 收到 `OnReputationChanged(-10, delta)` 等負值
- 行為：`targetLevel = 1`（低於 Lv1 門檻對應的 `rep >= 0`），因 `targetLevel <= currentLevel`（不降級原則）忽略，不發 `OnGuildLevelChanged`
- 文件標註：非異常情境，不輸出 Warning；與 §5.1.1 聲望回跌同一邏輯分支

**Case 5.1.3 — `GuildLevelTable` 空表 / Lv1 threshold 非 0**
- 情境：資料表設定錯誤，例如 Lv1 threshold = 100
- 行為：初始聲望 0 時 `FindTargetLevel(0)` 將 return 1（fallback），但後續 rep 達 100 時會判定為「升級到 Lv1」→ 誤判
- 防禦：初始化階段檢查 `GuildLevelTable[1].reputationThreshold == 0`，否則 `Debug.LogError` + `throw`
- 文件標註：F-01 DataManager 載入時驗證

**Case 5.1.4 — 聲望達到頂級門檻後繼續累加**
- 情境：`currentLevel = 5`（已頂級），rep 從 1500 增至 3000
- 行為：`targetLevel = 5`（不變），不發事件
- 文件標註：若未來新增 Lv6 需對應調整

### 5.2 連跳佇列異常

**Case 5.2.1 — 連跳佇列未清空前遊戲被暫停**
- 情境：玩家手動暫停（F-02 PauseTick）在連跳發送中途
- 行為：MonoBehaviour `Update()` 不受 F-02 tick 影響（Unity 引擎層），事件繼續發送
- 設計意圖：升級事件不應被玩家暫停阻擋（UI 動畫仍應播放）

**Case 5.2.2 — 連跳中途觸發 Game Over**
- 情境：連跳佇列內還剩 2 級待發，此時 F-03 觸發破產（例如破產與聲望大幅增加同一 tick 內發生的極端情境）
- 行為：`gameOverState = Pending` 後，連跳佇列**停止發送**，清空 queue
- 設計意圖：Game Over 優先級高於升級動畫，避免「破產畫面彈出又彈出升級通知」
- 實作：`Update()` 取出 queue 前檢查 `gameOverState != Active`

**Case 5.2.3 — 存檔時連跳佇列非空**
- 情境：連跳 Lv1→Lv4 中途，玩家觸發存檔（自動存檔或手動）
- 行為：FT-10 存檔時 `currentLevel` 可能為 Lv2 或 Lv3（queue 中的中間態）
- 決策：**存檔存「已發送到的最新等級」**（`currentLevel` 本值），讀檔後**不補發**剩餘連跳事件
- 理由：連跳事件是 UI/通知的一次性驅動，讀檔後玩家不應看到「升到 Lv4」的重播

### 5.3 Game Over 異常

**Case 5.3.1 — 玩家永不確認訃聞（長期卡在 Pending）**
- 情境：`OnGameOverPending` 發出後玩家離開遊戲、關閉視窗
- 行為：`gameOverState = Pending`，F-02 tick 未暫停，時間繼續流逝
- 處理：FT-10 自動存檔應正常運作（`gameOverState = Pending` 寫入存檔）；下次開遊戲讀檔後，P-02 偵測 `IsGameOverPending() == true` 重新顯示訃聞
- 文件標註：「訃聞畫面」應為模態，但即使非模態也不會破壞狀態機

**Case 5.3.2 — 重複收到 Bankrupt 事件**
- 情境：F-03 內部瑕疵導致連續發兩次 `OnBankruptcyWarningStateChanged(Bankrupt)`
- 行為：第二次進入 handler 時 `gameOverState == Pending`，條件不成立，忽略
- 冪等性由 Section 3.7 保證

**Case 5.3.3 — `ConfirmGameOver()` 在非 Pending 狀態被呼叫**
- 情境：P-02 bug 或 UI 邏輯錯誤，在 `Active` 或 `Over` 狀態呼叫 `ConfirmGameOver()`
- 行為：直接 return（冪等），不改變狀態、不發事件
- 防禦性 log：`Debug.LogWarning("ConfirmGameOver called in state {gameOverState}")`

**Case 5.3.4 — Pending 期間收到聲望變化（可能升級）**
- 情境：破產後、確認前，F-03 發 `OnReputationChanged`（例如某個舊流程未停止）
- 行為：handler 首行檢查 `gameOverState != Active` → 直接 return（見 4.5 invariant）
- 文件標註：等級凍結於破產瞬間

### 5.4 公會名稱異常

**Case 5.4.1 — 玩家輸入純空白字元（"   "）**
- 行為：`Trim()` 後為空 → `displayName = "公會"`
- 文件標註：與留空等效

**Case 5.4.2 — 玩家輸入超長字串**
- 情境：複製貼上「一段很長很長的公會介紹文字」
- 行為：
  - UI 層應在輸入時即時截斷至 8 全型字
  - FT-06 防禦性檢查：`IsValidGuildName() == false` 時截斷至前 8 全型字
- 文件標註：不拋錯，靜默截斷

**Case 5.4.3 — 玩家輸入特殊字元（emoji、控制字元、Unity Rich Text）**

| 字元類型 | 處理 | 計入字數？ |
|---|---|---|
| Emoji（全型字範圍） | 允許 | 是（1.0 / 字元） |
| 控制字元（`\n`、`\r`、`\t`、`\0` 等 Unicode `Cc` 類別） | **拒絕**（UI 輸入層 strip，FT-06 防禦性檢查時亦 strip） | — |
| Unity Rich Text **完整標籤對**（`<b>...</b>`、`<color=#RRGGBB>...</color>`、`<size=N>...</size>`、`<i>`、`<material=N>`、`<sprite=N>` 等） | 允許，視為文字效果 | **否**（標籤本身不計；內部文字照常計） |
| Unity Rich Text **不完整 / 落單標籤**（如只有 `<b>` 無 `</b>`） | 視為一般字元 | 是 |
| HTML/XML 一般標籤（非 Unity Rich Text 支援者） | 視為一般字元 | 是 |

**完整標籤判定規則**：
- 開始標籤（`<tag>` 或 `<tag=value>`）與對應關閉標籤（`</tag>`）必須在字串內成對出現
- 支援的標籤白名單遵循 Unity TextMeshPro Rich Text 官方列表（`b`、`i`、`u`、`s`、`color`、`size`、`material`、`sprite`、`line-height`、`align`、`cspace`、`mspace`、`indent`、`margin`、`nobr`、`link`、`lowercase`、`uppercase`、`smallcaps`、`style`、`pos`、`space`、`voffset`、`page` 等）
- 不在白名單內的標籤視為一般字元

**範例**：
- 輸入「`<b>約翰的</b>`」→ 實際字元計算：「約翰的」= 3 全型字 → 通過
- 輸入「`<color=#ff0000>血紅</color>傳說`」→ 計算：「血紅傳說」= 4 全型字 → 通過
- 輸入「`<b>約翰的`」（缺關閉）→ 視為一般字元 `<b>約翰的` = 3 全型 + 3 半型 = 4.5 → Ceil = 5 → 通過（但顯示會是字面量）

**實作責任**：
- UI 層（P-01）：輸入時即時驗證 + 顯示預覽
- FT-06：防禦性檢查時執行同一演算法（工具方法統一由 Core 提供 `RichTextLengthCalculator`）

**Case 5.4.4 — 玩家輸入「公會」作為名稱**
- 輸入：「公會」
- 組合後：`displayName = "公會公會"`
- 行為：允許（玩家選擇，不做語意去重）
- 文件標註：非 bug，為玩家表達自由

### 5.5 訂閱/生命週期異常

**Case 5.5.1 — FT-06 初始化早於 F-03**
- 情境：場景載入順序導致 FT-06 訂閱時 F-03 尚未準備好
- 行為：`EventBus.Subscribe` 應為 lazy（事件未發布即訂閱無妨）
- 依賴：EventBus 設計需支援「先訂閱後發布」語意（由 Core 層保證）

**Case 5.5.2 — FT-06 銷毀但仍有訂閱未解除**
- 情境：場景切換或 GameObject Destroy
- 行為：`OnDisable` / `OnDestroy` 解除所有訂閱，避免 null ref
- 實作責任：gameplay-programmer 實作時需在 MonoBehaviour 生命週期鉤子解除

---

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（Upstream — FT-06 訂閱 / 讀取的系統）

| 上游系統 | 介面 | 用途 | 類型 |
|---|---|---|---|
| **F-02 Time System** | `NowUTC : long`（property） | 取創立時間、升級時間戳 | 同步查詢 |
| **F-02 Time System** | `PauseTick(): void` | 階段 2 Game Over 後暫停時間 | 控制 API |
| **F-03 Resource Management** | Event `OnReputationChanged(int newValue, int delta)` | 觸發等級判定（FT-06 僅讀 `newValue`，忽略 `delta`） | 事件訂閱 |
| **F-03 Resource Management** | Event `OnBankruptcyWarningStateChanged(newState: enum)` | 觸發 Game Over 流程 | 事件訂閱 |
| **F-03 Resource Management** | `GetGold(): int` / `GetReputation(): int` | 取 Game Over 快照 | 同步查詢 |
| **F-01 DataManager** | `LoadGuildLevelTable(): List<GuildLevelEntry>` | 載入等級表 CSV | 同步查詢 |

**硬依賴**：F-02、F-03、F-01 為必要依賴，任一缺失 FT-06 無法運作。

### 6.2 下游系統（Downstream — 訂閱 / 讀取 FT-06 的系統）

| 下游系統 | 訂閱事件 / 查詢 API | 用途 |
|---|---|---|
| **FT-01 Adventurer Recruitment** | `GetMaxRecruitableRank()` | 老手邀請可招募最高階級（冒險者階級上限）；名冊上限改由 FT-07 `GetRosterCap()` 提供 |
| **P-02 / FT-02 / C-06**（待採用） | `GetMaxMissionDifficulty()` | 常規任務難度上限，用於 P-02 委託板與 C-06 任務生成過濾（待相關系統正式採用） |
| **FT-07 Guild Building System** | `GetCurrentLevel()` | 升級聲望閘門判定 |
| **P-01 Intro / Main Menu** | `OnGuildInitialized` / 公會名稱輸入 API | 新遊戲流程 |
| **P-02 HUD & Screens** | `OnGuildLevelChanged` / `OnGameOverPending` / `OnGameOver` | UI 更新、訃聞、結算畫面 |
| **P-02 HUD & Screens** | `GetGuildDisplayName()` / `GetCurrentTitle()` | HUD 公會名 / 稱號顯示 |
| **P-02 HUD & Screens** | `ConfirmGameOver()` | 玩家確認訃聞後呼叫 |
| **P-03 Notification** | `OnGuildLevelChanged` | 升級通知 toast |
| **FT-10 Save/Load** | `OnGuildInitialized` / `OnGameOver` | 首次存檔 / 封存存檔 |
| **FT-10 Save/Load** | `GuildState` 全量序列化 / 還原 | 存讀檔內容 |

### 6.3 事件契約矩陣

**FT-06 發布的事件**：

| 事件 | Payload 要點 | 發布時機 | 訂閱者 |
|---|---|---|---|
| `OnGuildInitialized` | `displayName`, `foundingTimestamp` | 新遊戲初始化完成 | P-02, FT-10 |
| `OnGuildLoaded` | `displayName`, `currentLevel` | FT-10 讀檔完成回呼 | P-02 |
| `OnGuildLevelChanged` | 見 3.4 完整欄位 | 連跳 queue 每 frame 取出一級 | P-02, P-03, FT-01, FT-02 |
| `OnGameOverPending` | 見 3.7 完整欄位 | 首次收到 F-03 Bankrupt | P-02 |
| `OnGameOver` | 階段 1 payload + `confirmTimestamp` | `ConfirmGameOver()` 被呼叫 | P-02, FT-10 |

**FT-06 訂閱的事件**：

| 事件源 | 事件 | 處理邏輯 |
|---|---|---|
| F-03 | `OnReputationChanged` | 呼叫 3.3 判定 → 3.4 排程連跳 |
| F-03 | `OnBankruptcyWarningStateChanged(Bankrupt)` | 觸發 3.7 階段 1 |

### 6.4 反向依賴登記（Reverse-Dependency Checklist）

FT-06 GDD 完成後，以下 GDD 的 §6.2（下游）需補登 FT-06 為下游：

- [x] `[F-02] time-system.md` §6.2：已補 FT-06（讀 `NowUTC`、呼叫 `PauseTick()`）
- [x] `[F-03] resource-management.md` §6.2：已登記 FT-06（訂閱 `OnBankruptcyWarningStateChanged`）；`OnReputationChanged` 由 F-03 直接發布、FT-06 於 §6.1 登記為上游訂閱
- [x] `[F-01] data-manager.md` §6.2：已登記 FT-06（讀 `GuildLevelTable`）

以下 GDD 的 §6.1（上游）需補登 FT-06 為上游：

- [x] `[FT-01] adventurer-recruitment.md` §6.1：已登記 FT-06（讀 `GetMaxRecruitableRank()`,M3 拆分後）;`GetRosterCap()` 來源改為 FT-07
- [x] `[FT-02] mission-dispatch.md` §6.1：來源改為 FT-07 `GetMaxConcurrentMissions()`（原 FT-06 `GetMaxMissions()` 已移除）
- [x] `[FT-07] guild-building-system.md` §6.2：FT-06 已登記為 FT-07 上游（聲望閘門判定）
- ~~`[C-01] mission-database.md` §6.1：補 FT-06~~ — **不適用**：C-01 為純資料系統（Mission Database），不主動呼叫任何 API；`GetMaxRecruitableRank()` / `GetMaxMissionDifficulty()` 的字串值域對齊 C-01 難度軸,但消費者為 FT-01 / 未來 P-02 / FT-02 非 C-01
- [ ] `[FT-10] save-load.md` §6.1：補「FT-06（訂閱 `OnGuildInitialized` / `OnGameOver`、序列化 `GuildState`）」
- [ ] `[P-01] intro-menu.md` §6.1（若已建）：補「FT-06（呼叫公會名稱設定 API）」
- [ ] `[P-02] hud-screens.md` §6.1（若已建）：補「FT-06（多事件訂閱 + 呼叫 `ConfirmGameOver()`）」
- [ ] `[P-03] notification.md` §6.1（若已建）：補「FT-06（訂閱 `OnGuildLevelChanged`）」

> 依專案規則：等 FT-06 整體通過後，與 FT-05 的反向依賴更新批次一起處理。

### 6.5 開發順序（Dev Order）

**依賴前置**：F-01 → F-02 → F-03 → **FT-06** → FT-07 → FT-01 / FT-02 / P-01 / P-02 / P-03 / FT-10

- FT-06 自身 API 僅依賴 F-02 / F-03 / F-01，可在 FT-07 之前實作
- FT-07 需等 FT-06 `GetCurrentLevel()` 就緒（聲望閘門判定）
- FT-01、FT-02 容量閘需等 FT-07 `GetRosterCap()` / `GetMaxConcurrentMissions()` 就緒;FT-01 老手邀請階級上限需等 FT-06 `GetMaxRecruitableRank()` 就緒
- P-02 HUD 顯示公會名稱、稱號、等級需等 FT-06 就緒
- FT-10 存讀檔序列化 `GuildState` 需等 FT-06 資料結構確定

### 6.6 ISaveable 持久化契約

| 欄位 | 值 |
|---|---|
| `OwnerKey` | `"ft06Guild"` |
| `IsCritical` | `true`（公會等級/聲望缺失導致 FT-07 聲望閘、FT-01 階級上限等無法驗算，整檔回退） |

**`Serialize()` 序列化欄位**（對應 §3.1 `GuildState` 持久欄位）：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `guildName` | `string` | 玩家輸入的公會名稱前綴（不含後綴「公會」） |
| `displayName` | `string` | 完整顯示名稱（= `guildName + GUILD_NAME_SUFFIX`） |
| `foundingTimestamp` | `long` | 公會建立時的 UTC Unix 秒 |
| `currentLevel` | `int` | 當前公會等級（1~5） |
| `gameOverState` | `string` | `"Active"` / `"Pending"` / `"Over"` |

不序列化：`pendingLevelUpQueue`（§3.1 明確標注為 runtime-only，Bootstrap 後由 reputation 重新驅動判定）。

**`RestoreFromSave(string ownerJson)` 行為**：

1. 反序列化上述 5 個欄位。
2. 驗證 `currentLevel` ∈ `[1, 5]`、`gameOverState` ∈ `{"Active", "Pending", "Over"}`；違規拋例外（觸發整檔回退）。
3. 還原完成後發布 `OnGuildLoaded(displayName, currentLevel)` 事件（對齊 §3.2 / AC-13；不補發 `OnGuildLevelChanged` 連跳事件）。

**`InitializeAsNewGame()` 預設值**：

| 欄位 | 初始值 |
|---|---|
| `guildName` | `""` |
| `displayName` | `DEFAULT_GUILD_NAME`（預設「公會」） |
| `foundingTimestamp` | `NowUTC` |
| `currentLevel` | `1` |
| `gameOverState` | `"Active"` |

對應 FT-10 §3.3.3 拓撲順序 row 4、§3.3.4 Critical 分類、§6.1 #14（FT-10 設計來源清單）。

---

## 7. 可調參數（Tuning Knobs）

### 7.1 `GuildLevelTable.csv`

**檔案位置**：`Assets/Resources/Data/Tables/GuildLevelTable.csv`

| 欄位 | 型別 | 允許範圍 | 說明 |
|---|---|---|---|
| `level` | int | 1..5 | 等級（主鍵）；必須連續、Lv1 起始 |
| `reputationThreshold` | int | 0..INT_MAX | 達到此聲望即升此級；Lv1 必須為 0、後續嚴格升冪 |
| `title` | string | 非空 | 公會稱號；UI 顯示用 |
| `maxDifficulty` | string | 依 C-01 難度軸（F/E/D/C/B/A/S/SS/SSS） | **[Deprecated]** legacy 欄位，保留向後相容；新代碼應讀 `maxRecruitableRank` |
| `maxRecruitableRank` | string | D/C/B/A/S | 老手招募可邀請的最高冒險者階級；FT-01 `RollVeteranRank` 使用；值域僅到 S（無 SS/SSS 冒險者） |
| `maxMissionDifficulty` | string | 依 C-01 難度軸（D~SS，Jam 預設 Lv5 上限為 SS） | 常規任務可生成的最高難度；P-02 委託板與 C-06 池過濾使用；值域可達 SS/SSS（任務存在但冒險者招募上限不同） |

> `maxMissions`、`rosterCap` 欄位已移至 FT-07 `BuildingTable`（公會櫃臺、公會大廳），本表不再維護。

**預設值**（延續 Section 3.5）：

| level | reputationThreshold | title | maxDifficulty | maxRecruitableRank | maxMissionDifficulty |
|---|---|---|---|---|---|
| 1 | 0 | 新手冒險者公會 | D | D | D |
| 2 | 30 | 初階冒險者公會 | C | C | C |
| 3 | 80 | 中階冒險者公會 | B | B | B |
| 4 | 200 | 高階冒險者公會 | A | A | A |
| 5 | 400 | 名聲顯赫的冒險者公會 | S | S | SS |

**調整指南**：
- 升級曲線：threshold 差值 = 30, 50, 120, 200（2026-04-26 調整為 Jam 版加速進度設計；Lv3=80 為 Jam 版設計終點，玩家約 3h 可達）
- 難度軸：`maxRecruitableRank` 值域限制為 D~S（冒險者最高階級 S）；`maxMissionDifficulty` 值域可到 SS/SSS；若 C-01 難度軸改變（如改為數字），本表需同步調整
- `maxDifficulty`（legacy）：不再調整；僅保留向後相容，應保持與 `maxRecruitableRank` 相同值（避免混淆）
- `maxRecruitableRank` 與 `maxMissionDifficulty` 必須單調不降（每級 >= 前級）

### 7.2 程式碼常數（Constants）

| 常數名 | 型別 | 預設值 | 允許範圍 | 說明 |
|---|---|---|---|---|
| `GUILD_NAME_MAX_FULLWIDTH` | int | **8** | 4..16 | 公會名稱最大全型字數（不含「公會」後綴） |
| `GUILD_NAME_SUFFIX` | string | **"公會"** | 不建議改 | 公會名稱固定後綴 |
| `DEFAULT_GUILD_NAME` | string | **"公會"** | 不建議改 | 玩家未輸入時的預設顯示名 |
| `LEVEL_UP_QUEUE_INTERVAL_FRAMES` | int | **1** | 1..30 | 連跳事件發送間隔（frame 數）；1 = 每 frame 發一級 |

**位置**：`Assets/Scripts/Gameplay/Guild/GuildCoreConstants.cs`

### 7.3 不可調參數（Hard-Coded / 設計決策）

下列為設計決策，不應透過設定檔變更：

- 等級不可降（`currentLevel` 僅升不降）
- Game Over 兩階段（Pending → Over）順序不可改
- `F-02 PauseTick()` 在階段 2 之前不呼叫
- 連跳期間收到 Bankrupt 事件清空 queue（Game Over 優先級高於升級）

### 7.4 調整檢查清單（Tuning Safety）

改 `GuildLevelTable.csv` 時必須確認：

- [ ] `level` 連續且從 1 起始
- [ ] `reputationThreshold[1] == 0`
- [ ] `reputationThreshold` 嚴格升冪（每級 > 前級）
- [ ] `maxRecruitableRank` 單調不降（難度軸定義內，D~S）
- [ ] `maxMissionDifficulty` 單調不降（難度軸定義內，D~SS）
- [ ] `maxDifficulty`（legacy）保持與 `maxRecruitableRank` 相同值（避免混淆）
- [ ] `title` 非空、UI 字串長度不溢出（建議 12 全型字內）

改 `GUILD_NAME_MAX_FULLWIDTH` 時必須確認：
- [ ] P-01 UI 輸入框對應限制同步調整
- [ ] HUD / 通知顯示區域容得下新長度（最壞情況 N 全型字 + 「公會」後綴）

---

## 8. 驗收標準（Acceptance Criteria）

### 8.1 初始化（新遊戲）

- **AC-1**：新遊戲啟動後，`GetCurrentLevel() == 1`、`GetCurrentTitle() == "新手冒險者公會"`
- **AC-2**：玩家輸入「約翰的」→ `GetGuildDisplayName() == "約翰的公會"`；留空 → `GetGuildDisplayName() == "公會"`
- **AC-3**：`GetFoundingTimestamp()` 回傳值等於 `F02.NowUTC` 的初始化瞬間值

### 8.2 等級系統

- **AC-4**：F-03 發 `OnReputationChanged(newValue=100, delta=100)`（從 rep=0）後，**下一 frame** 發出 `OnGuildLevelChanged(fromLv=1, toLv=2)`
- **AC-5**：F-03 發 `OnReputationChanged(newValue=1500, delta=1500)`（從 rep=0）後，連發 4 次 `OnGuildLevelChanged`（Lv1→2、2→3、3→4、4→5），每次間隔 1 frame，所有 payload 的 `isMultiJump == true` 且 `finalTargetLv == 5`
- **AC-6**：聲望回跌（rep 從 700 降到 250）後，`OnGuildLevelChanged` **不發送**、`GetCurrentLevel()` 維持升過最高值
- **AC-7**：已在 Lv5 時再收到 rep 增加事件，不發 `OnGuildLevelChanged`

### 8.3 難度上限 API

- **AC-8**：`GetMaxRecruitableRank()` 回傳值與 `GuildLevelTable.maxRecruitableRank` 當前等級列完全一致；`GetMaxMissionDifficulty()` 回傳值與 `GuildLevelTable.maxMissionDifficulty` 完全一致；`GetMaxDifficulty()` deprecated alias 行為等同 `GetMaxRecruitableRank()`（每次升級後同步反映）；容量類 API（`GetRosterCap` / `GetMaxConcurrentMissions`）已移至 FT-07 驗收

### 8.4 Game Over 流程

- **AC-9**：F-03 發 `OnBankruptcyWarningStateChanged(Bankrupt)` 後，FT-06 發 `OnGameOverPending`，此時 `IsGameOverPending() == true`、`IsGameOver() == false`、F-02 tick **未暫停**
- **AC-10**：P-02 呼叫 `ConfirmGameOver()` 後，FT-06 發 `OnGameOver`、F-02 tick **已暫停**、`IsGameOver() == true`
- **AC-11**：`ConfirmGameOver()` 在非 Pending 狀態被呼叫時，不改變狀態、不發事件（冪等）
- **AC-12**：Pending 或 Over 狀態下再收到 `OnReputationChanged`，**不發** `OnGuildLevelChanged`

### 8.5 存讀檔

- **AC-13**：新遊戲初始化完成後發 `OnGuildInitialized`；FT-10 讀檔完成後發 `OnGuildLoaded`，payload 內 `currentLevel` 與存檔一致（不重新判定、不補發連跳事件）

### 8.6 公會名稱驗證

- **AC-14**：輸入超過 8 全型字的字串（如「我的冒險者公會管理員」= 10 全型字），FT-06 截斷至前 8 全型字後組合 `displayName`
- **AC-15**：輸入含控制字元（`\n`、`\t`）的字串，`displayName` 中不含這些字元；輸入完整 Rich Text 標籤對（如 `<b>約翰的</b>`）時，標籤保留、長度僅計內部文字 3 字

---

### 備註

- **MonoBehaviour 驗證**：上述 AC 假設 FT-06 實作為 `GuildCore` MonoBehaviour，連跳由 `Update()` 或 Coroutine 驅動；實作確認後 gameplay-programmer 撰寫 PlayMode 測試
- **Grep audit**：deferred — 等跨系統修正批次處理時一併驗證（參見 FT-05 相同策略）
- **UI 驗證**：P-02 動畫、P-03 toast 的 AC 屬於 P-02/P-03 GDD 範圍，FT-06 只保證事件正確發送
