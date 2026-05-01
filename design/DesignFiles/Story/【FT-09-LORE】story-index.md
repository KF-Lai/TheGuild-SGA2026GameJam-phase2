# 劇本內容索引（Story Content Index）

_建立時間：2026-04-30_
_狀態：執行中（v3.1 大綱定稿後啟動）_
_管控者：narrative-director（主體）_

---

## 0. 文件目的

本檔為 **女神陣營（奧蘿瑞）劇本** 與相關內容產出的進度索引與規格標準。所有 haiku subagent 量產任務以本檔為準，內容變更時優先更新此處。

---

## 1. 命名規範（全域）

| 項目 | 名稱 | 別稱 |
|---|---|---|
| 女神 | **奧蘿瑞**（Aurorae 音譯） | 晨曦女神、晨光女神 |
| 聖殿 | **奧蘿瑞晨光聖殿** | 晨曦聖殿 |
| 異神 | （無正式名） | 外來者、那個東西、異神 |
| 信徒 | **契者** | 異神信徒、那群人 |
| 第十一任公會長 | （玩家） | — |
| 情感錨點冒險者 | **奧菲莉雅 Ophelia** | — |
| 女神陣營轉變職員 | **米拉 Mira** | 見習聯絡官 |
| 通用轉變職員（記錄者） | **譚恩 Tan** | 雜役、公會見習 |
| 通用轉變職員（契者倖存者） | **凱拉 Kaira** | 委託官 |

---

## 2. 量產項目進度表

| 項目 | 數量 | 對應檔案 | 狀態 | 完成日 |
|---|---|---|---|---|
| 大綱（v3.1） | 1 份 | `【FT-09-LORE】aurorae-faction-outline.md` | ✅ | 2026-04-30 |
| 本索引 | 1 份 | `【FT-09-LORE】story-index.md` | ✅ | 2026-04-30 |
| GDD/FSD Patch summary | 1 份 | `design/_Reports/GDD-FSD-patch-v3.1-aurorae-faction.md` + `systems-index.md` 登記 | ✅ | 2026-04-30 |
| 陣營劇本（5 Stage） | 5 份 | `【FT-09-LORE】stage-1.md` ~ `stage-5.md` | ✅ | 2026-04-30 |
| 奧菲莉雅文字需求 | 1 份 | `【FT-09-LORE】ophelia-text.md` | ✅ | 2026-04-30 |
| 職員文字需求 | 1 份 | `【FT-09-LORE】staff-text.md` | ✅ | 2026-04-30 |
| 陣營劇情委託 CSV | 5 行（含進階委託 8 行）| `【FT-09-LORE】mission-faction-story.csv` → ✅ 已合併至 `MissionTemplate.csv`（9001-9005 + 8001-8008）| ✅ | 2026-04-30 |
| 高難委託 CSV | SS 5 + SSS 3 = 8 行 | `【FT-09-LORE】mission-high-difficulty.csv` → ✅ 已合併（7001-7008）| ✅ | 2026-04-30 |
| 常規委託 CSV | 100 行（F~A 各 15 + S 10）| `【FT-09-LORE】mission-regular.csv` → ✅ 已合併（1001-1610）| ✅ | 2026-04-30 |
| 冒險者量產 CSV | 71 行（70 常規 + 奧菲莉雅）| `【FT-09-LORE】adventurer-templates.csv` → ✅ 已合併至 `AdventurerTemplate.csv` | ✅ | 2026-04-30 |
| 一般職員量產 CSV | 10 行（含米拉/譚恩/凱拉）| `【FT-09-LORE】staff-templates.csv` → ✅ 已合併至 `StaffTable.csv` | ✅ | 2026-04-30 |
| StoryStageTable CSV | 5 行（Stage 1-5 預設資料）| ✅ 已寫入 `StoryStageTable.csv`（含 dialogueVariantMode / specialEventKey / unlockBlockerCondition）| ✅ | 2026-04-30 |
| 沉默 trait | 1 行 | ✅ 已寫入 `TraitTable.csv`（traitID=999）| ✅ | 2026-04-30 |
| SystemConstants 5 新常數 | 5 行 | ✅ 已寫入 `SystemConstants.csv`（LIGHT_THRESHOLD / MIXED_THRESHOLD / OPHELIA_MISSING_RECOVERY_HOURS / OPHELIA_TEMPLATE_ID / STAGE5_MISSION_ID）| ✅ | 2026-04-30 |
| 對話文本表 | 多行 | 待寫入 `DialogueTable.csv`（dialogueKey 已由各 stage / staff / ophelia 文檔規劃；待 P-02 解除暫停或獨立 dialogue owner GDD 定案後實作）| ⬜ | 待後續實作 |

---

## 3. 全域寫作規格（haiku 必讀）

### 3.1 CSV 格式規範（依 `.claude/rules/data-files.md`）

- **轉置格式**：第一列為 PK 列，後續每列為一個欄位定義
- **多值欄位**：用 `|` 分隔（如 `traitIDs = 1|3|5`）
- **註解**：第 0 欄首字元為 `#` → 整列跳過
- **null sentinel**：int 用 `0`、string 用 `""`、多值用單一 `0`
- **PK 從 1 起**，`0` 永遠不與合法 ID 衝突
- **欄位 ID 縮寫一律大寫** `ID` / `IDs`

### 3.2 標籤系統（防止被 Data System 誤讀）

所有非 CSV 欄位的「**敘事標註**」一律以 `#` 前綴：

| 用途                       | 標籤      |
| ------------------------ | ------- |
| 純文字呈現（玩家直接讀的對話/描述）       | `# 文字:` |
| 美術靜態素材需求（紙條視覺、立繪、場景物件）   | `# 美術:` |
| 動態演出需求（茶杯冷卻過渡、字跡浮現、相框變化） | `# 動畫:` |
| 音效需求（紙條飄落聲、椅子拖動、敲門）      | `# 音效:` |
| 待後續補完的內容                 | `# 待補:` |
| 機制觸發條件說明（不出現於玩家面前）       | `# 機制:` |

### 3.3 文字長度上限

| 內容類型 | 上限 |
|---|---|
| Stage 主對話視窗 | 3 行 / 每行 ≤24 字 |
| 委託描述 | ≤120 字 |
| 冒險者 bio | ≤80 字 |
| 職員自我介紹 | ≤60 字 |
| 任務結算 flavor text | ≤30 字 |
| 紙條金句 | ≤2 行 |

### 3.4 命名規則

- **冒險者名字**：西洋風（拉丁語系 / 北歐 / 凱爾特），使用「**名 + 姓**」格式（如「**艾倫·萊德**」）。**不採用稱號**（如「鐵拳」「月影」）
- **職員名字**：同冒險者，西洋風，「名 + 姓」
- **委託名稱**：簡潔，4~12 字，動詞開頭優先（如「護送商隊穿越荒野」）
- **陣營劇情委託名稱**：可較詩意（如「**沉默旅人的南行**」）

---

## 4. Stage 與系統耦合摘要

| Stage | dangerLevel | factionScore threshold | 需要的系統機制 |
|---|---|---|---|
| Onboarding | — | — | C-02 名冊、AdventurerTemplate isUnique=1、FT-10 InitializeAsNewGame |
| Stage 1 | E 和平 | 8 | FT-09 階段解鎖、C-01 categoryID=3、場景擺飾物 |
| Stage 2 | D 動盪 | 22 | FT-09 階段解鎖、輔軌啟動 |
| Stage 3 | C 暗湧 | 50 | FT-09 主軌變體（dialogueKey 多軌）、styleTag 累積、FT-04 jitter modifier、公會門口紙條物件 |
| Stage 4 | B 危局 | 120 | 公會誌動態文本、FT-09 訂閱 FT-04 死亡事件、茶杯場景物件 |
| Stage 5 | A 末世 | 200 | FB-M3 動態變數注入、奧菲莉雅死/活二元分支、譚恩接班場景 |

---

## 5. 待解決問題清單

| 編號 | 議題 | 狀態 | 對應系統 |
|---|---|---|---|
| Q1 | dialogueKey 多軌變體機制（FT-09 schema 補充） | 🟡 game-designer 討論中 | FT-09 |
| Q2 | 沉默 trait 的 effectType / effectTarget | 🟡 同上 | C-05 |
| Q3 | 奧菲莉雅初始放入名冊機制 | 🟡 同上 | C-02 / FT-10 |
| Q4 | FT-04 styleTag 影響 jitter | 🟡 同上 | FT-04 |
| Q5 | FT-09 訂閱 FT-04 死亡事件 | 🟡 同上 | FT-09 / FT-04 |
| Q6 | 公會場景可互動物件系統 | 🟡 同上（P-02 暫停中，僅記錄需求） | P-02 |
| Q7 | 高難委託 SS/SSS 觸發機制 | 🟡 同上 | C-01 / FT-02 |
| Q8 | 米拉/譚恩/凱拉 加入 StaffTable + factionID 預留 | 🟡 同上 | FT-12 |

---

## 6. 變更歷史

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-04-30 | v0.1 | 初版索引建立。命名規範定案（奧蘿瑞 / 米拉 / 譚恩 / 凱拉）；量產數量表確認；CSV 規格與標籤系統定案 |
| 2026-04-30 | v0.2 | 完成全部量產任務（陣營劇本 5 Stage / 奧菲莉雅文字 / 職員文字 / 委託 CSV 121 個 / 冒險者 CSV 71 位 / 一般職員 CSV 10 位）。GDD/FSD Patch summary 已登記至 systems-index P3.1-aurorae。haiku 量產 + 2 次審稿循環完成。待後續實作：(1) 沉默 trait 寫入 TraitTable.csv；(2) DialogueTable.csv 文本對應；(3) Patch summary 各項依優先級寫入主體 GDD（FT-09 / C-01 / FT-04 / C-05 / 等 10 個）|
| 2026-04-30 | v0.3 | **Stage A~G 執行完成**：(A) 底層 schema patch（C-01 / C-05 / SystemConstants）；(B) FT-04 結算 patch（isScriptedDeath short-circuit + jitter modifier）；(C) FT-09 大幅修改（StoryStageTable 3 新欄位 + ResolveDialogueKey + GetCurrentStyleTagBias + Epilogue + Blocker 機制 + Ophelia 事件）；(D) C-02 / FT-10 / FT-05 / FT-12 P1 patch；(F) **CSV 量產合併**（DS-designer sonnet 執行 + 自行 review 全項通過）—— MissionTemplate 147 個 / AdventurerTemplate 71 位 / StaffTable 10 位 / StoryStageTable 5 個 / TraitTable + 999 / SystemConstants + 5 常數；(G) 跨文件一致性收尾（FSD-index §九 / data-index v3.1 patch 紀錄 / STORY-INDEX 進度更新）。**GDD review** 發現並修正 R1（FT-09 訂閱不存在事件）/ R2（C-02 SetWounded API 擴充）/ R3（FT-09 EvaluateBlocker 改用 GetRoster）。**FSD review** 發現並修正 F1（C-01 FSD 缺 v3.1 同步）/ F4（C-02 FSD 缺 R2 同步）+ FT-09 FSD 補 R1/R3 修正同步。剩餘工作：(1) DialogueTable.csv 文本（待 P-02 / dialogue owner 定案）；(2) FT-09 / FT-10 / FT-12 FSD 完整 Script 設計（待 Codex）；(3) design-review 重跑 4 項 P0（C-05 / FT-04 / FT-09 / C-02 含 R2）；(4) FT-07 / P-02 待後續確認 |
