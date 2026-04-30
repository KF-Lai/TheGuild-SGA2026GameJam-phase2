# v3.1 GDD/FSD Patch Summary：奧蘿瑞女神陣營劇本（Aurorae Faction）

_建立時間：2026-04-30_
_狀態：Patch 候選清單（待依優先級寫入各 GDD/FSD 主體）_
_主導：narrative-director（敘事總監）_
_設計來源：與 game-designer 7 次來回討論結論（2026-04-30）_

---

## 0. 文件目的

本檔記錄 v3.1 大綱（`design/DesignFiles/Story/【FT-09-LORE】aurorae-faction-outline.md`）所引發的所有 GDD/FSD Patch 內容。

**處理方式**：依 §1 撰寫順序與優先級，依序寫入各 GDD 主體，並在 `design/GDD/systems-index.md` §GDD Patch 紀錄登記每筆。

---

## 1. Patch 撰寫順序與優先級

依依賴關係排序（上層依賴下層，先寫下層）：

| 順序 | Patch | 優先級 | 修改幅度 | 需要 design-review 重跑？ |
|---|---|---|---|---|
| 1 | C-01 MissionTemplate（3 欄位）| **P0** | 小 patch | 否 |
| 2 | C-05 Trait System（沉默 trait + isScriptedDeath 無效規則 + 安全範圍突破）| **P0** | 中等 patch | 是 |
| 3 | FT-04 Outcome Resolution（isScriptedDeath short-circuit + jitter modifier）| **P0** | 中等 patch | 是 |
| 4 | FT-09 Faction Story System（最大 patch）| **P0** | 大幅修改 | 是 |
| 5 | C-02 Adventurer Management（DismissAdventurer 審查處解鎖 + RegisterUniqueAdventurer）| P1 | 中等 patch | 否 |
| 6 | FT-10 Save/Load（InitializeAsNewGame 補奧菲莉雅初始化）| P1 | 小 patch | 否 |
| 7 | FT-05 Guild Gold Flow（Commission Flow 補 minDangerLevel 篩選）| P1 | 小 patch | 否 |
| 8 | FT-12 Staff System（IsStaffHired API + 5 個 Post-Jam 預埋欄位）| P1 | 小 patch | 否 |
| 9 | FT-07 Guild Building（審查處 Idle 開除確認）| P2 | 小 patch | 否 |
| 10 | P-02 Main UI（SceneObjectController 設計需求登記）| P2 | 小 patch | 否（暫停中不實作）|

---

## 2. P0 Patches

### 2.1 C-01 MissionTemplate — 三個新欄位

**Patch ID**：P3.1-001
**Owner GDD**：`design/GDD/【C-01】mission-database.md`
**Owner DS**：`design/Data-Specs/【C-01-DS】mission-template.md`

**新增欄位**：

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---|---|
| `isScriptedDeath` | int (0/1) | 0 | 1 = 強制必死任務（FT-04 short-circuit 擲骰）；**僅允許於 categoryID=3 模板**，否則 DataManager 載入時 `Debug.LogError` 並重置為 0 |
| `minDangerLevel` | int | 0 | 最小世界危險度索引（E=0, D=1, C=2, B=3, A=4）；FT-05 SelectMissionFromPool 以此過濾，0 = 無限制 |
| `requiredTraitID` | int (FK → C-05 TraitTable) | 0 | 派遣前置條件：冒險者需持有此 traitID；0 = 無限制；FT-02 / FT-03 消費 |

**Validation 規則補充**（C-01 §5.1）：
- `isScriptedDeath == 1 && categoryID != 3` → `LogError`，重置為 0
- `minDangerLevel` 不在 [0,4] → `LogError`，重置為 0
- `requiredTraitID > 0 && TraitTable.Contains(requiredTraitID) == false` → `LogError`，重置為 0

---

### 2.2 C-05 Trait System — 沉默 trait + 規則

**Patch ID**：P3.1-002
**Owner GDD**：`design/GDD/【C-05】trait-system.md`
**Owner DS**：`design/Data-Specs/【C-05-DS】trait-table.md`

**新增 trait（TraitTable.csv）**：

```csv
# === Trait 999：沉默（奧菲莉雅專屬，不在隨機抽取群組內）===
traitID,999
name,沉默
description,她不主動接受委託，需要玩家明確指派
effectType,behavior
effectTarget,willingness_all
effectValue,-0.40
```

**新規則（C-05 §4.4 補規則）**：
> **`on_death_survive` 與 `on_fail_survive` 在 `ActiveMission.isScriptedDeath == 1` 時永遠不觸發**。FT-04 在傳入 `traitIDs` 前已執行過濾（FT-04 §3.4 守衛邏輯）。設計理由：劇本必死的設計重量不應被 condition trait 救活。

**新規則（C-05 §7.1 安全範圍突破）**：
> behavior 類 effectValue 安全範圍 `-0.30 ~ +0.20`，**具名特殊角色**（isUnique=1 且為敘事核心）可例外突破至 `-0.40 ~ +0.30`。當前已知例外：奧菲莉雅「沉默」（-0.40 willingness_all）。

---

### 2.3 FT-04 Outcome Resolution — 兩項機制

**Patch ID**：P3.1-003
**Owner GDD**：`design/GDD/【FT-04】outcome-resolution.md`
**Owner FSD**：`design/FSD/【FT-04-FSD】outcome-resolution.md`

#### 2.3.1 isScriptedDeath short-circuit（§3.x.5 RollSuccessAndDeath 補規則）

```
RollSuccessAndDeath(outcome, activeMission, template):
    // === isScriptedDeath 插入點 ===
    IF template.isScriptedDeath == 1:
        outcome.isSuccess  = false
        outcome.isDead     = true
        outcome.isWounded  = false
        outcome.adjustedDeathRate = 1.0
        outcome.successRoll = -1.0   // 標記「未擲骰」（Debug 識別）
        outcome.deathRoll   = -1.0
        return  // 完全跳過擲骰

    // 原始擲骰邏輯（不變）
    outcome.successRoll = Random.Range(0.0, 1.0)
    ...
```

**§3.4 補規則（condition 過濾）**：
```
ApplyConditionTraits(outcome, adventurer.traitIDs):
    IF activeMission.isScriptedDeath == 1:
        filteredTraitIDs = adventurer.traitIDs
            .Where(t => t.effectTarget != "on_death_survive"
                     && t.effectTarget != "on_fail_survive")
        C05.ApplyConditionTraits(outcome, filteredTraitIDs)
    ELSE:
        C05.ApplyConditionTraits(outcome, adventurer.traitIDs)
```

#### 2.3.2 styleTag jitter modifier（FB-M1）

**§4.x 新增**：
```
CalcAdjustedJitter(baseJitter, mission, currentDangerLevelIndex):
    bias = FT09.GetCurrentStyleTagBias()
    IF bias == StyleTag.Light AND currentDangerLevelIndex >= 2 (C 暗湧)
       AND mission.factionID == 1:
        return baseJitter - 0.04  // light 任務 jitter 偏負 4%
    return baseJitter
```

**設計用途**：FB-M1 玩家「最近運氣特別差」的機制感受來源。

#### 2.3.3 死亡結算路徑（Stage 5 必死）

- `isScriptedDeath=1` 結算走「失敗+死亡」路徑（`isSuccess=false, isDead=true, finalStatus=Dead`）
- 設計師可將 Stage 5 任務 `baseReward=0` 讓 FT-05 自然零金流
- 不扣聲望可選：將該任務 `difficulty="SCRIPTED"` 利用 §5.5 fallback 機制（缺失難度 → baseDelta=0）

---

### 2.4 FT-09 Faction Story System — 最大 patch

**Patch ID**：P3.1-004
**Owner GDD**：`design/GDD/【FT-09】faction-story-system.md`
**Owner FSD**：`design/FSD/【FT-09-FSD】faction-story-system.md`

#### 2.4.1 StoryStageTable 新增欄位

| 欄位 | 型別 | 預設 | 說明 |
|---|---|---|---|
| `dialogueVariantMode` | string | `"none"` | 三值：`none` / `ophelia_alive_dead` / `styletag_bias` |
| `specialEventKey` | string | `""` | Stage 4 = `"ophelia_missing"`；P-02 在對話確認後依此發布特殊事件 |
| `unlockBlockerCondition` | string | `""` | Stage 5 = `"npc:ophelia:status==Idle"`；blocker 解析語法見 §3.4.3 |

**Stage 1-5 預設資料**：

| stageID | factionID | stageIndex | scoreThreshold | missionID | dialogueKey | dialogueVariantMode | specialEventKey | unlockBlockerCondition |
|---|---|---|---|---|---|---|---|---|
| 1001 | 1 | 1 | 8 | [stage1 missionID] | story.aurorae.stage1 | none | "" | "" |
| 1002 | 1 | 2 | 22 | [stage2 missionID] | story.aurorae.stage2 | none | "" | "" |
| 1003 | 1 | 3 | 50 | [stage3 missionID] | story.aurorae.stage3 | styletag_bias | "" | "" |
| 1004 | 1 | 4 | 120 | [stage4 missionID] | story.aurorae.stage4 | styletag_bias | ophelia_missing | "" |
| 1005 | 1 | 5 | 200 | [stage5 missionID] | story.aurorae.stage5 | ophelia_alive_dead | "" | npc:ophelia:status==Idle |

#### 2.4.2 dialogueKey 解析邏輯

```
ResolveDialogueKey(string baseKey, string mode):
    SWITCH mode:
        CASE "none":
            return baseKey
        CASE "styletag_bias":
            bias = GetCurrentStyleTagBias()
            candidate = $"{baseKey}.{bias.ToString().ToLower()}"  // e.g. story.aurorae.stage3.light
            return DialogueTable.Contains(candidate) ? candidate : baseKey
        CASE "ophelia_alive_dead":
            // Stage 5 解鎖時刻只能解析 styletag 維度（奧菲莉雅死活未定）
            bias = GetCurrentStyleTagBias()
            candidate = $"{baseKey}.{bias.ToString().ToLower()}"
            return DialogueTable.Contains(candidate) ? candidate : baseKey
            // 二次解析（OnFactionStoryStageEpilogue）見 §2.4.4
```

#### 2.4.3 GetCurrentStyleTagBias() API

```csharp
public enum StyleTag { Dark, Mixed, Light }

public StyleTag GetCurrentStyleTagBias()
{
    int score = GetCurrentFactionScore(1);  // factionID=1 女神陣營
    if (score >= LIGHT_THRESHOLD) return StyleTag.Light;
    if (score >= MIXED_THRESHOLD) return StyleTag.Mixed;
    return StyleTag.Dark;
}
```

**閾值（SystemConstants.csv 新增）**：
- `LIGHT_THRESHOLD = 100`（safe range [80, 200]，不得低於 MIXED_THRESHOLD + 30）
- `MIXED_THRESHOLD = 40`（safe range [20, 60]，不得高於 Stage 3 scoreThreshold=50）

**性質宣告**（必須明文寫入 GDD）：
> 「styleTag 區間判定依賴 `currentScore`，由 §3.1 單向加分保證永不倒退。Jam 版範疇內，玩家進入 Light 區間後永遠不會退回 Mixed/Dark。本系統刻意不實作 hysteresis——任何「補救降分」需求應重新評估 §1 設計原則。」

#### 2.4.4 OnFactionStoryStageEpilogue 新事件

**事件 schema**：
```csharp
public readonly struct OnFactionStoryStageEpilogue
{
    public readonly int    stageID;
    public readonly int    factionID;
    public readonly string resolvedEpilogueKey;
    public readonly bool   isOpheliaEpilogue;
    public readonly bool   subjectAlive;
}
```

**觸發時機**：FT-09 §3.6.2 `HandleOnMissionResolved` Step 9 後（識別 Stage 5 categoryID=3 任務結算後），同 frame 發布。

**resolvedEpilogueKey 解析**：
```
"story.aurorae.stage5.epilogue.{styletag}.{alive|dead}"
e.g. "story.aurorae.stage5.epilogue.light.dead"
```

#### 2.4.5 unlockBlockerCondition 機制（§3.4.3 新章節）

```
CheckStageUnlock(int factionID, int oldScore, int newScore):
    // ... 既有解鎖判定 ...
    IF stage.scoreThreshold met:
        IF stage.unlockBlockerCondition != "":
            blocked = EvaluateBlocker(stage.unlockBlockerCondition)
            IF blocked:
                _blockedStages.Add(stage.stageID)
                return  // 不解鎖、不入隊、不發事件
        // 正常解鎖流程
```

**EvaluateBlocker 解析語法**（Jam 版只實作 1 種）：
- `"npc:ophelia:status==Idle"` → 查 C-02 奧菲莉雅 instance status，false 即 blocked

**TriggerDeferredStageCheck API**（新增）：
- 由 `OnOpheliaReturned` handler 呼叫，重新檢查 `_blockedStages`，blocker 解除時補觸發解鎖

#### 2.4.6 訂閱 FT-04 死亡事件（FB-M2）

**新增 runtime 狀態**：
- `_totalAdventurerDeaths: int`（持久化於 SaveData）

**訂閱**：FT-04 `OnAdventurerDied(adventurerID)` → `_totalAdventurerDeaths++`

**消費**：Stage 4 解鎖時若 `_totalAdventurerDeaths >= 5`，公會誌新字跡多一句「**我數過了，你失去了五個**」（透過 `AppendGuildLogEntry` API，見 §2.10.2）

#### 2.4.7 OnOpheliaMissingNight / OnOpheliaReturned 事件

**OnOpheliaMissingNight**：
- 觸發點：Stage 4 解鎖對話視窗確認後（P-02 收到 `OnFactionStoryStageUnlocked` 且 `specialEventKey == "ophelia_missing"`，於 ConfirmDialogue 後發布）
- 處理者：FT-09（內部記錄 flag）+ P-02（場景物件 sprite 更新）+ 呼叫 `C02.SetWounded(opheliaInstanceID)`，但 `woundedUntilTimestamp` 設為 `NowUTC + OPHELIA_MISSING_RECOVERY_HOURS × 3600`

**OnOpheliaReturned**：
- 觸發點：奧菲莉雅 woundedUntilTimestamp 到期，C-02 `OnAdventurerRecovered` 發布且 instanceID 為奧菲莉雅
- 處理者：FT-09（清除 flag、TriggerDeferredStageCheck）+ P-02（場景物件 sprite 還原 + 顯示「她回來了」對話）+ 再次呼叫 `C02.SetWounded` 設 6h 標準 Wounded（`WOUNDED_RECOVERY_HOURS=6`）

**新增 SystemConstants**：
- `OPHELIA_MISSING_RECOVERY_HOURS = 12`（safe range [6, 24]）
- `OPHELIA_TEMPLATE_ID = 901`

---

## 3. P1 Patches

### 3.1 C-02 Adventurer Management

**Patch ID**：P3.1-005
**Owner GDD**：`design/GDD/【C-02】adventurer-management.md`

#### 3.1.1 RegisterUniqueAdventurer API（§3 新增）

```csharp
public bool RegisterUniqueAdventurer(int templateID)
{
    var template = DataManager.Get<AdventurerTemplate>(templateID);
    if (template == null || template.isUnique != 1) return false;
    if (roster.Any(a => a.templateID == templateID)) return false;
    var instance = CreateFromTemplate(templateID);
    roster.Add(instance);  // 不檢查 rosterCap
    return true;
}
```

**用途**：FT-10 `InitializeAsNewGame` 呼叫此 API 將奧菲莉雅放入名冊，**繞過容量上限與費用檢查**。

#### 3.1.2 DismissAdventurer 規則放寬（§3.4 Rule 4 patch）

**舊規則**：僅 `Dead` 狀態可除名
**新規則**：
- `Dead` 狀態：永遠可除名（既有規則）
- `Idle` 狀態：**僅當 FT-07 審查處（buildingID=待定）已解鎖時可除名**

**新增事件**：
- `OnAdventurerDismissed(instanceID)`：除名時發布，FT-09 訂閱以識別奧菲莉雅特殊處理

#### 3.1.3 名冊排序

`GetRoster()` 排序規則：
1. `templateID == OPHELIA_TEMPLATE_ID` 優先（永遠第一格）
2. 其餘依 `idleSinceTimestamp` 倒序

---

### 3.2 FT-10 Save/Load System

**Patch ID**：P3.1-006
**Owner GDD**：`design/GDD/【FT-10】save-load-system.md`

#### 3.2.1 InitializeAsNewGame 補奧菲莉雅初始化

**§3.B Bootstrap Orchestration 新遊戲分支補一步**：
```
Step X (位置：C-02 名冊初始化後、FT-01 候選池初始化前):
    C02.RegisterUniqueAdventurer(SystemConstants.OPHELIA_TEMPLATE_ID)
```

#### 3.2.2 新增持久化欄位

- `factionStoryV31_pendingMissingNight: bool`（FT-09 模式）
- `factionStoryV31_totalAdventurerDeaths: int`（FT-09 累積死亡計數）
- `factionStoryV31_blockedStages: List<int>`（FT-09 blocker 狀態）

---

### 3.3 FT-05 Guild Gold Flow

**Patch ID**：P3.1-007
**Owner GDD**：`design/GDD/【FT-05】guild-gold-flow.md`

#### 3.3.1 SelectMissionFromPool 補 minDangerLevel 篩選

```
SelectMissionFromPool():
    // 1. weights 採樣難度 tier（既有邏輯）
    difficulty = SampleDifficultyByWeights(C06.GetPoolWeights())

    // 2. 取該 tier 模板候選
    candidates = MissionTemplateTable.WhereDifficulty(difficulty)

    // 3. minDangerLevel 過濾（v3.1 新增）
    currentDangerIndex = DangerLevelToIndex(C06.GetCurrentLevel())
    candidates = candidates.Where(t => t.minDangerLevel <= currentDangerIndex)

    // 4. 若候選為空，fallback 到下一 tier（最多 3 次重採）
    IF candidates.IsEmpty:
        // fallback logic
```

---

### 3.4 FT-12 Staff System

**Patch ID**：P3.1-008
**Owner GDD**：`design/GDD/【FT-12】staff-system.md`

#### 3.4.1 IsStaffHired API 新增

```csharp
public bool IsStaffHired(int staffID)
{
    return _activeRoster.Any(s => s.staffID == staffID);
}
```

#### 3.4.2 StaffTable 新增 5 個 Post-Jam 預埋欄位

| 欄位 | 型別 | 預設 | 說明 |
|---|---|---|---|
| `personalityDesc` | string | 必填 | 人格描述（Jam 版填寫但不被消費，Post-Jam 親密度系統參考） |
| `intimacyLevel` | int | 0 | Post-Jam 親密度系統 |
| `isLeavePossible` | int (0/1) | 0 | Post-Jam 自願離職可能性 |
| `mood` | int | 0 | Post-Jam 心情系統，safe range [0, 4] |
| `personalEventIDs` | int[] | `0` | Post-Jam 個人事件 FK 列表（→ PersonalEventTable.csv，Post-Jam 新建） |

#### 3.4.3 三位敘事核心職員 CSV（StaffTable.csv 新增）

```csv
staffID,501,502,503
name,米拉,譚恩,凱拉
rarity,4,3,3
salary,80,50,50
severancePay,300,200,200
isFiller,false,false,false
factionID,1,0,0
minGuildLevel,2,1,1
effectIDs,Willingness,RecruitRefreshOnCounter,""
effectValues,0.15,1800,""
slotBuildingIDs,0,4,1
uiFlagIDs,"","",SuccessRatePreview
uiFlagBuildingIDs,0,0,1
personalityDesc,對女神的信仰與對現實的懷疑在她心中拉鋸，表面溫柔卻有自己的底線,說話簡短直接不繞彎子，對公會長有戒心但尊重實力,樂觀隨性、好奇心強，對委託結果比任何人都認真
intimacyLevel,0,0,0
isLeavePossible,0,0,0
mood,0,0,0
personalEventIDs,0,0,0
```

---

## 4. P2 Patches

### 4.1 FT-07 Guild Building System

**Patch ID**：P3.1-009
**Owner GDD**：`design/GDD/【FT-07】guild-building-system.md`

**確認項目**（不一定需要實際 patch）：
- 確認審查處建築（buildingID 待查）的解鎖機制與 C-02 `DismissAdventurer` Idle 開除整合
- 提供 `IsBuildingUnlocked(buildingID)` API 給 C-02 查詢

---

### 4.2 P-02 Main UI Framework

**Patch ID**：P3.1-010
**Owner GDD**：`design/GDD/【P-02】main-ui-framework.md`（設計暫停中，僅登記需求）

#### 4.2.1 SceneObjectController 子模組規格

**SceneObjectStateTable.csv schema**：

| 欄位 | 型別 | 必要 | 說明 |
|---|---|---|---|
| `objectID` | string (PK) | 是 | 場景物件唯一識別碼 |
| `stageCondition` | string | 是 | 觸發條件語法（`stageID >= N` / `stageID == N` / `event:KEY`）|
| `spriteVariant` | string | 是 | 對應 sprite 變體名稱 |
| `dialogueKey` | string | 否 | 玩家點擊互動時顯示的對話 key |
| `priority` | int | 是 | 同 objectID 多行時優先順序（高值優先）|
| `audioCue` | string | 否 | 觸發時音效（Post-Jam 預留）|

**SceneObjectStateTable.csv 完整初始資料**（轉置格式）：

```csv
objectID,ophelia_chair,ophelia_chair,ophelia_chair,ophelia_teacup,ophelia_teacup,ophelia_guildbook,ophelia_guildbook,ophelia_crest,ophelia_crest,ophelia_door_note
stageCondition,stageID >= 1,event:ophelia_missing,stageID >= 5,stageID >= 1,event:ophelia_missing,stageID == 1,stageID >= 2,stageID < 3,stageID >= 3,event:ophelia_missing
spriteVariant,idle,empty,empty,present,cold,unopened,annotated,hidden,visible,posted
dialogueKey,scene.chair.ophelia_present,scene.chair.ophelia_missing,scene.chair.ophelia_dead,scene.teacup.normal,scene.teacup.cold,scene.guildbook.new,scene.guildbook.read,"",scene.crest.revealed,scene.door_note.missing
priority,0,10,20,0,10,0,5,0,10,10
audioCue,"",sfx.wind_gust,"","","","","","",sfx.magical_hum,""
```

#### 4.2.2 美術資產命名規則

路徑：`Assets/Art/Scene/Ophelia/{objectID}_{spriteVariant}.png`

#### 4.2.3 對話視窗呈現規範

- 主對話視窗 ≤3 行 / 每行 ≤24 字
- Stage 4「她沒回來」採場景說明層（疊加在公會場景內），字色偏灰
- Stage 5 奧菲莉雅台詞採黑底白字最簡形式
- 死亡通知 → epilogue 視窗順序：佇列化處理，先死亡通知再 epilogue

---

## 5. 紙條三版視覺規格（給 art-director）

| 元素 | light 版 | dark 版 | neutral 版 |
|---|---|---|---|
| 紙質 | 米白偏奶油色，無明顯紋理；輕微羊皮感 | 灰黃色，紙漿雜質感（顆粒噪點 3-5%）| 米白接近標準白，輕微泛黃 |
| 字色（hex）| `#3B2A1A` ~ `#2C1A0A`（深棕墨水）| `#1A1A1A` ~ `#0D0D0D`（近純黑，顫抖 ±1-2px）| `#2E2E2E` ~ `#222222`（深灰）|
| 行距 / 字距 | 1.6× 寬鬆，靠左對齊 | 1.2× 壓縮，基線傾斜 1-2° | 1.4× 正常 |
| 紙緣 | 整齊，四角輕微缺損 1-2px | 不規則撕裂（上緣或左緣）+ 右下焦痕（Stage 3+）| 輕微折痕，對稱磨損 |
| 落款 | 極小晨曦聖徽（4×4px，亮度低，Stage 3+）| 扭曲線條符號（不規則橢圓加十字）| 無落款或單一 `—` 破折號 |

**核心一致元素**（不可變）：紙條長寬比 1:2.5、字型大小 14pt、核心文字內容相同、進入動畫相同。

---

## 6. 實作紅線（給 Codex / gameplay-programmer）

### 6.1 v3.1 新設計專屬紅線

1. **isScriptedDeath 必須在 RollSuccessAndDeath 開頭 short-circuit**
   - 不可擲骰後再 override `isDead`（會消耗亂數序列、影響 seed-based 測試）
   - `successRoll = -1.0` 標記未擲骰，Debug UI 可識別

2. **on_death_survive 對 isScriptedDeath=1 任務無效**
   - C-05 `ApplyConditionTraits` 必須先過濾再傳入
   - **不可在 condition trait 計算後再 override `isDead = true`**——這會讓「條件特質救活」與「劇本必死」的執行順序產生語意混亂

3. **dialogueVariantMode 必須在事件發布前完成解析**
   - FT-09 `OnFactionStoryStageUnlocked` payload 帶 `resolvedDialogueKey`
   - P-02 不持有 styleTag 判斷邏輯，只查 DialogueTable

4. **styleTag 即時計算，不快取**
   - `GetCurrentStyleTagBias()` 每次呼叫即時讀 `_factionScores[1]`
   - 不要為了「優化」而引入快取——快取會製造「Stage 3 解鎖時計算、Stage 4 呈現時用舊快照」的語意漏洞

5. **奧菲莉雅初始化時序：FT-10 Bootstrap > C-02 名冊初始化 > RegisterUniqueAdventurer > FT-01 候選池**
   - 順序錯誤會導致 FT-01 候選池可能誤生成 templateID=901 的候選（雖然 isUnique 攔截器會阻止，但會 LogError）

6. **unlockBlockerCondition 的 blocker 解析必須先於入隊**
   - `_blockedStages` 在分數達標但 blocker 未解時填入
   - `OnOpheliaReturned` 後 `TriggerDeferredStageCheck` 重新檢查 `_blockedStages`

7. **Stage 5 兩段式 dialogueKey 解析**
   - 第一次解析：Stage 5 解鎖時刻（styletag 維度）
   - 第二次解析：FT-04 結算後（疊加 alive/dead 維度），透過 `OnFactionStoryStageEpilogue` 事件
   - **不可在 Stage 5 解鎖時就解析死活**（此時奧菲莉雅未派遣）

### 6.2 既有系統繼承紅線（game-designer 提示）

8. **condition 特質改寫 isDead 的時序必須先於 MapFinalStatus**（FT-04 §3.2 12 步驟順序嚴守）
9. **conditionGoldBonus 只填欄位，FT-04 不可直接呼叫 AddGold**（FT-05 是金流唯一入口）
10. **isUnique 模板的二次檢查不能取代 FT-01 的前置快照過濾**（避免 UI 顯示無效候選）

---

## 7. SystemConstants.csv 新增項目

```csv
key,value,description
LIGHT_THRESHOLD,100,FT-09 styleTag bias 進入 Light 區間的最小分數
MIXED_THRESHOLD,40,FT-09 styleTag bias 進入 Mixed 區間的最小分數
OPHELIA_MISSING_RECOVERY_HOURS,12,Stage 4 奧菲莉雅失蹤至回來的現實小時數
OPHELIA_TEMPLATE_ID,901,奧菲莉雅 AdventurerTemplate ID
STAGE5_MISSION_ID,9005,Stage 5 劇情委託 missionID（量產時填入正確值）
```

---

## 8. 後續工作

| 階段 | 內容 | 負責 |
|---|---|---|
| Phase A | 本檔案在 `systems-index.md` GDD Patch 紀錄登記 | narrative-director（已完成）|
| Phase B | P0 patches（FT-09 / C-01 / FT-04 / C-05）寫入主體 GDD | 後續 Codex 或 gameplay-programmer 介入時處理 |
| Phase C | P1 patches（C-02 / FT-10 / FT-05 / FT-12）寫入主體 GDD | 同上 |
| Phase D | P2 patches（FT-07 / P-02）等對應系統解除暫停或設計時整合 | 同上 |
| Phase E | design-review 重跑（C-05 / FT-04 / FT-09 三項）| 跨 GDD 審查 |

---

## 9. 變更歷史

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-04-30 | v1 | 初版 patch summary 建立。內容來自與 game-designer 7 次來回討論（議題 1-5 + 邊緣情境）。涵蓋 10 個 GDD/FSD 的 patch 候選，依 P0/P1/P2 優先級排序。 |
