# v3.1 GDD/FSD Patch 執行計畫（Aurorae Faction）

_建立時間：2026-04-30_
_用途：將 `_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` 內容分解為可執行任務清單_
_狀態：執行計畫（尚未開始實際 patch）_

---

## 0. 任務概覽

| 維度 | 數量 |
|---|---|
| 影響 GDD | 10 份 |
| 影響 FSD | 8 份 |
| 影響 Data-Specs | 5 份 |
| 影響 CSV（非 design 檔，含 Resources/Data/Tables） | 4 份 |
| 影響其他文件（systems-index / FSD-index）| 2 份 |
| **預估總工時（人工執行）** | **12-20 小時** |
| **預估總工時（Codex 輔助）** | **6-10 小時** |
| design-review 重跑需求 | 3 項（C-05 / FT-04 / FT-09） |

---

## 1. 完整修改清單（依檔案分類）

### 1.1 GDD 主體（10 份）

| 檔案 | 章節 | 修改類型 | 詳細內容 | 工時估 |
|---|---|---|---|---|
| `【C-01】mission-database.md` | §3.x schema | 新增 3 欄位 | `isScriptedDeath` / `minDangerLevel` / `requiredTraitID`（含 Validation 規則）| 30-60 分 |
| `【C-05】trait-system.md` | §3.x（TraitTable）| 新增 1 行 | traitID=999「沉默」 | 15-30 分 |
| `【C-05】trait-system.md` | §4.4 | 補規則 | `on_death_survive` / `on_fail_survive` 對 isScriptedDeath=1 無效 | 30-60 分 |
| `【C-05】trait-system.md` | §7.1 | 修改 | behavior 安全範圍突破至 ±0.40（具名特殊角色例外）| 15 分 |
| `【FT-04】outcome-resolution.md` | §3.x.5 RollSuccessAndDeath | 修改偽碼 | isScriptedDeath short-circuit | 30-60 分 |
| `【FT-04】outcome-resolution.md` | §3.4 ApplyConditionTraits | 補過濾邏輯 | isScriptedDeath=1 時過濾 survive 類 trait | 30 分 |
| `【FT-04】outcome-resolution.md` | §4.x 新章節 | 新增公式 | styleTag jitter modifier（FB-M1） | 30-60 分 |
| `【FT-04】outcome-resolution.md` | §3.5 Death 結算路徑 | 補說明 | isScriptedDeath 走「失敗+死亡」路徑 | 15-30 分 |
| `【FT-09】faction-story-system.md` | §3.2.2 StoryStageTable | 新增 3 欄位 | `dialogueVariantMode` / `specialEventKey` / `unlockBlockerCondition` + 5 行預設資料 | 60-90 分 |
| `【FT-09】faction-story-system.md` | §3.x dialogueKey 解析 | 新章節 | ResolveDialogueKey 偽碼 + 三模式說明 | 60 分 |
| `【FT-09】faction-story-system.md` | §3.x styleTag API | 新章節 | `GetCurrentStyleTagBias()` + 閾值 + 性質宣告 | 60 分 |
| `【FT-09】faction-story-system.md` | §3.7.2 事件清單 | 新增事件 | `OnFactionStoryStageEpilogue`（含 schema） | 30-60 分 |
| `【FT-09】faction-story-system.md` | §3.4.3 unlockBlockerCondition | 新章節 | `_blockedStages` + EvaluateBlocker + TriggerDeferredStageCheck | 60-90 分 |
| `【FT-09】faction-story-system.md` | §3.6.2 訂閱 FT-04 死亡 | 補規則 | `_totalAdventurerDeaths` 累積（FB-M2） | 30-60 分 |
| `【FT-09】faction-story-system.md` | §3.x Ophelia 事件 | 新章節 | `OnOpheliaMissingNight` / `OnOpheliaReturned` + 事件流程 | 60-90 分 |
| `【FT-09】faction-story-system.md` | §6.1 / §6.2 | 補依賴 | C-02、FT-04、FT-12 新增依賴點 | 30 分 |
| `【FT-09】faction-story-system.md` | §7 | 新增參數 | LIGHT_THRESHOLD / MIXED_THRESHOLD / OPHELIA_MISSING_RECOVERY_HOURS | 30 分 |
| `【C-02】adventurer-management.md` | §3.x 新 API | 新增 | `RegisterUniqueAdventurer(templateID)` | 30-60 分 |
| `【C-02】adventurer-management.md` | §3.4 Rule 4 | 修改 | DismissAdventurer 規則放寬（審查處解鎖後 Idle 可開除） | 30-60 分 |
| `【C-02】adventurer-management.md` | §3.x 新事件 | 新增 | `OnAdventurerDismissed` 事件 | 15-30 分 |
| `【C-02】adventurer-management.md` | §3.x GetRoster | 修改排序 | 奧菲莉雅永遠第一格 | 15 分 |
| `【FT-10】save-load-system.md` | §3.B Bootstrap 新遊戲分支 | 補步驟 | 呼叫 `C02.RegisterUniqueAdventurer(901)` | 15-30 分 |
| `【FT-10】save-load-system.md` | §3.x 持久化欄位 | 新增 3 欄位 | `factionStoryV31_pendingMissingNight` / `_totalAdventurerDeaths` / `_blockedStages` | 30 分 |
| `【FT-05】guild-gold-flow.md` | §3.x SelectMissionFromPool | 修改偽碼 | minDangerLevel 篩選 + fallback 重採規則 | 30-60 分 |
| `【FT-12】staff-system.md` | §3.4 新 API | 新增 | `IsStaffHired(staffID) : bool` | 15-30 分 |
| `【FT-12】staff-system.md` | §3.2 StaffTable schema | 新增 5 欄位 | `personalityDesc` / `intimacyLevel` / `isLeavePossible` / `mood` / `personalEventIDs` | 30 分 |
| `【FT-07】guild-building-system.md` | §3.x | 確認 | 審查處（buildingID 待確認）的解鎖機制 + IsBuildingUnlocked API | 15-30 分（可能不需動）|
| `【P-02】main-ui-framework.md` | §3.x（暫停中）| 登記需求 | SceneObjectController + SceneObjectStateTable + 對話視窗呈現規範 | 30-60 分（待解除暫停）|

**GDD 小計**：約 11-17 小時

### 1.2 FSD（8 份，需與 GDD 同步）

| 檔案 | 修改類型 | 對應 GDD 變更 | 工時估 |
|---|---|---|---|
| `【C-01-FSD】mission-database.md` | 同步新欄位 | 3 欄位 | 30 分 |
| `【C-05-FSD】trait-system.md` | 同步新 trait + 規則 | 3 項 | 60 分 |
| `【FT-04-FSD】outcome-resolution.md` | 同步 short-circuit + jitter modifier | 4 項 | 60-90 分 |
| `【FT-09-FSD】faction-story-system.md` | 同步全部 patch | 7+ 項 | 120-180 分 |
| `【C-02-FSD】adventurer-management.md` | 同步新 API + 規則放寬 | 4 項 | 60 分 |
| `【FT-10-FSD】save-load-system.md` | 同步 Bootstrap + 持久化 | 2 項 | 30 分 |
| `【FT-05-FSD】guild-gold-flow.md` | 同步 SelectMission 篩選 | 1 項 | 30 分 |
| `【FT-12-FSD】staff-system.md` | 同步 API + schema | 2 項 | 30 分 |

**FSD 小計**：約 7-10 小時

### 1.3 Data-Specs（5 份）

| 檔案 | 修改類型 | 內容 | 工時估 |
|---|---|---|---|
| `【C-01-DS】mission-template.md` | 新增欄位定義 | 3 欄位（含型別、預設值、Validation） | 30 分 |
| `【C-05-DS】trait-table.md` | 新增 trait + 規則 | traitID=999 + safe range 突破 | 30 分 |
| `【FT-09-DS】story-stage-table.md` | 新增欄位定義 | 3 欄位 + 5 行預設資料 | 60 分 |
| `【FT-12-DS】staff-table.md` | 新增 5 欄位定義 | 全部 Post-Jam 預埋 | 30 分 |
| `【F-01-DS】system-constants.md` | 新增 5 常數 | LIGHT_THRESHOLD / MIXED_THRESHOLD / OPHELIA_MISSING_RECOVERY_HOURS / OPHELIA_TEMPLATE_ID / STAGE5_MISSION_ID | 15-30 分 |

**Data-Specs 小計**：約 3-4 小時

### 1.4 實際 CSV 檔（Resources/Data/Tables/）

| 檔案 | 修改類型 | 內容來源 | 工時估 |
|---|---|---|---|
| `MissionTemplate.csv` | 合併新欄位 + 121 個委託 | 從 `mission-faction-story.csv` + `mission-high-difficulty.csv` + `mission-regular.csv` 合併 | 60-90 分 |
| `AdventurerTemplate.csv` | 替換為量產版 | 從 `adventurer-templates.csv` 取代既有 SAMPLE | 30 分 |
| `StaffTable.csv` | 替換為量產版 | 從 `staff-templates.csv` 取代既有 SAMPLE | 30 分 |
| `TraitTable.csv` | 新增 1 行 | traitID=999 沉默 | 15 分 |
| `SystemConstants.csv` | 新增 5 行 | 5 常數 | 15 分 |
| `StoryStageTable.csv` | 新增 5 行 + 新欄位 | Stage 1-5 預設資料 | 30 分 |

**CSV 合併小計**：約 3-4 小時

### 1.5 其他文件

| 檔案 | 修改類型 | 內容 | 工時估 |
|---|---|---|---|
| `systems-index.md` | 已完成 | P3.1-aurorae 已登記 | ✅ |
| `FSD-index.md §7.2` | 新增 patch review 紀錄 | 為 8 個 FSD 各追加一行 patch 對應紀錄 | 30 分 |
| `data-index.md` | 更新 CSV 狀態 | MissionTemplate 從 SAMPLE → 完整版；StaffTable / AdventurerTemplate / StoryStageTable 同 | 15 分 |

---

## 2. 修改順序建議（依依賴關係 + 優先級）

### Stage A：底層 Schema（**P0 並行**）

> **預估時間：2-3 小時**

並行執行（無相互依賴）：

- **A1**：`C-01` GDD + DS + `MissionTemplate.csv` 新欄位
- **A2**：`C-05` GDD + DS + `TraitTable.csv` 新增 traitID=999
- **A3**：`F-01-DS` system-constants + `SystemConstants.csv` 新增 5 常數

### Stage B：核心結算（**P0 順序**）

> **預估時間：2-3 小時**
> 依賴：A1（C-01 isScriptedDeath 欄位）、A2（C-05 規則）

- **B1**：`FT-04` GDD + FSD（isScriptedDeath short-circuit + jitter modifier + condition 過濾）
- **B2**：design-review 重跑 FT-04 ↔ C-05 一致性

### Stage C：陣營劇情系統（**P0 大幅修改**）

> **預估時間：4-6 小時**
> 依賴：A1、A2、B1

- **C1**：`FT-09` GDD（最大 patch，分 7 個子章節）
- **C2**：`FT-09-DS` story-stage-table（新欄位 + 5 行預設資料）
- **C3**：`StoryStageTable.csv` 寫入 5 行 Stage 預設資料
- **C4**：`FT-09-FSD`（同步 GDD 變更）
- **C5**：design-review 重跑 FT-09 ↔ C-01 / FT-04 / C-02 / FT-12 一致性

### Stage D：依賴系統（**P1 並行**）

> **預估時間：3-4 小時**
> 依賴：A1、B1、C1

並行執行：

- **D1**：`C-02` GDD + FSD（RegisterUniqueAdventurer + DismissAdventurer 放寬 + 名冊排序）
- **D2**：`FT-10` GDD + FSD（Bootstrap 補奧菲莉雅 + 持久化欄位）
- **D3**：`FT-05` GDD + FSD（SelectMissionFromPool minDangerLevel 篩選）
- **D4**：`FT-12` GDD + DS + FSD（IsStaffHired API + 5 欄位預埋）+ `StaffTable.csv` 量產 10 位

### Stage E：UI 與建築（**P2，可延後**）

> **預估時間：1-2 小時**

- **E1**：`FT-07` GDD（確認審查處解鎖機制，可能不需動）
- **E2**：`P-02` GDD（解除暫停後處理；當前僅在 patch summary 中登記需求）

### Stage F：CSV 量產合併（可與 Stage D 並行）

> **預估時間：2-3 小時**

- **F1**：將 `mission-faction-story.csv` + `mission-high-difficulty.csv` + `mission-regular.csv` 合併進 `MissionTemplate.csv`（121 行新增 + 3 個新欄位）
- **F2**：`AdventurerTemplate.csv` 替換為量產 71 位
- **F3**：`StoryStageTable.csv` 寫入 5 行（已含於 C3）

### Stage G：跨文件一致性收尾

> **預估時間：30-60 分鐘**

- **G1**：`FSD-index.md §7.2` 追加 8 個 FSD 的 patch review 紀錄
- **G2**：`Data-Specs/data-index.md` 更新 CSV 狀態
- **G3**：`STORY-INDEX.md` 更新進度狀態為「全部完成」

---

## 3. 並行性分析（最大化效率）

```
Stage A（並行 3 子任務）─┐
                          ├─→ Stage B ─→ Stage C ─→ Stage D（並行 4 子任務）─┐
                          │                                                  ├─→ Stage G
                          └─────────────────────→ Stage F（並行）─────────────┘
                                                                Stage E（可延後）
```

**最快執行路徑**（並行最大化）：
- 並行：A1 + A2 + A3
- 串行：B1
- 串行：C1（最大耗時）
- 並行：D1 + D2 + D3 + D4 + F1 + F2
- 串行：G1 + G2 + G3
- 延後：E1 + E2

**並行最大化的工時**：約 **8-12 小時**（如果並行能完美執行）
**串行執行的工時**：約 **15-20 小時**

---

## 4. 風險與注意事項

### 4.1 高風險項目（容易踩雷）

| 風險 | 對應 patch 點 | 緩解 |
|---|---|---|
| FT-09 大幅修改可能與既有 §1 ~ §8 結構衝突 | C1 | 建議優先讀完整 FT-09 GDD 後再分章節 patch；不要跳章節 |
| C-05 安全範圍突破（沉默 -0.40）影響既有 trait 平衡 | A2 | 需 design-review 確認此例外不會被未來新增 trait 濫用 |
| FT-04 isScriptedDeath short-circuit 改主路徑 | B1 | 需 EditMode 測試確認既有結算測試不受影響 |
| FT-09 訂閱 FT-04 死亡事件 | C1 | FT-04 必須先有 OnAdventurerDied 事件（驗證是否已存在）|
| P-02 暫停中卻有大量 patch 需求 | E2 | 僅在 patch summary 中登記，待 P-02 解除暫停時才實際 patch |

### 4.2 待 verify 的假設

| 假設 | 需 verify 的位置 |
|---|---|
| FT-04 是否已有 `OnAdventurerDied` 事件（FB-M2 訂閱依賴） | FT-04 GDD §3.x 事件清單 |
| FT-07 審查處 buildingID 是多少 | FT-07 BuildingTable.csv |
| FT-12 既有 StaffTable.csv schema 是否已支援 v3.1 patch 新欄位 | FT-12 GDD §3.2 + StaffTable.csv |
| FT-05 SelectMissionFromPool 的具體切入點 | FT-05 FSD（已實作） |
| C-02 OnAdventurerDismissed 事件是否已存在 | C-02 GDD §3.x |

### 4.3 需 design-review 重跑的 3 項

依 game-designer 建議：

1. **C-05**（isScriptedDeath 無效規則 + safe range 突破）
2. **FT-04**（isScriptedDeath short-circuit 改主路徑）
3. **FT-09**（最大幅修改，多項新機制）

每項 design-review 重跑：60-120 分鐘。

---

## 5. 執行模式建議

### 模式 A：一次性手動執行（不建議）
- **優點**：完全可控
- **缺點**：12-20 小時連續工作，易疲勞與出錯
- **適用**：僅 P0 緊急時

### 模式 B：分階段手動執行（建議用於 Stage A、B、E、G）
- **優點**：可分批完成，每階段做 verify
- **缺點**：跨多日
- **適用**：底層 schema、UI、收尾

### 模式 C：Codex 輔助執行（建議用於 Stage C、D、F）
- **優點**：大幅縮短時間（約一半），文件量大時尤其顯著
- **缺點**：需要 Codex MCP 可用 + 嚴格 review
- **流程**：Claude Code 主體出工項書 → Codex 實作（read-only）→ Claude Code Opus + xhigh 審查 → APPROVED → Codex 寫入

### 模式 D：Subagent 並行（建議用於 Stage D 並行子任務）
- **優點**：D1-D4 並行可在 1 小時內完成 4 個 GDD/FSD patch
- **缺點**：需要對每個子任務寫獨立 brief
- **適用**：依賴關係明確的並行 patch

### 推薦混合策略

```
Stage A：Subagent 並行（3 個子任務）→ 30-60 分鐘
Stage B：手動或 Codex 輔助 → 1-2 小時
Stage C：Codex 輔助（最大 patch 用 Codex 提升效率）→ 2-3 小時
Stage D：Subagent 並行（4 個子任務）→ 1-2 小時
Stage F：手動執行（CSV 合併）→ 1-2 小時
Stage E：延後（待 P-02 解除暫停）
Stage G：手動執行（小工作）→ 30 分鐘
design-review：手動執行 3 項 → 3-6 小時
```

**總計（混合策略）**：約 **9-15 小時**

---

## 6. 第一步該做什麼？

### 立即可做（不需任何前置）
1. **Stage A1**：C-01 GDD + DS 新增 3 欄位（最簡單、無依賴、blockers 解除最多）
2. **Stage A2**：C-05 GDD + DS 新增 traitID=999（無依賴）
3. **Stage A3**：F-01-DS 新增 5 常數（最簡單）

### 第一週可完成的合理目標
- Stage A 全部（2-3 小時）
- Stage B FT-04 patch（2-3 小時）
- 開始 Stage C FT-09 GDD 的部分子章節

### 完成優先序
若時間極有限，建議完成順序：
1. **必做**（P0 全部）：A1 + A2 + B1 + C1（核心，缺一不可）
2. **重要**（P1 全部）：D1 + D2 + D3 + D4
3. **可延後**（P2）：E1 + E2

---

## 7. 變更歷史

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-04-30 | v1 | 初版執行計畫。基於 `_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` 整理。包含 23 個 GDD/FSD/DS 檔案的修改清單、依賴關係、並行性分析、風險評估、執行模式建議。 |
