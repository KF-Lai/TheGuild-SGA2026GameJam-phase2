---
name: review-DIP
description: "全自動掃描並補登反向依賴（Dependency Inversion / 雙向引用）。依 design/GDD/DIP-index.md 的 A 區工作清單對指定 GDD 執行反向登記補丁。輸入支援完整檔名、GDD 代號逗號分隔、波浪號範圍。完成後自動更新 DIP-index 狀態。"
argument-hint: "<完整檔名.md> | <GDD-id>[,<GDD-id>...] | <range1~range2>[,...] — 例：'/review-DIP FT-06,FT-07' 或 '/review-DIP FT-01~FT-03,C-01~C-03'"
user-invocable: true
allowed-tools: Read, Glob, Grep, Edit, Write
---

全自動 skill：不互動、不問問題、不分階段確認；按表執行、跑到底僅輸出處理摘要。**禁止**修改目標 GDD 中與反向依賴無關的內文。

---

## Pre-flight：模型與思考強度檢查

**在執行任何 skill 步驟前先執行此檢查，兩項全部通過才繼續；任一不符立即終止 skill。**

### 模型檢查

確認當前模型為 Sonnet 系列（模型 ID 包含 `sonnet`）或更高（含 `opus`）。

- 通過條件：system context 中的模型名稱含 `sonnet` 或 `opus`
- 不符合 → 立即終止，輸出：
  ```
  [review-DIP 終止] 需要 Sonnet 或 Opus 模型。
  當前模型：<current-model-id>
  請執行 /model claude-sonnet-4-6 或 /model claude-opus-4-7 後重新執行 /review-DIP。
  ```

### 思考強度檢查

確認當前 extended thinking 強度為 high 或更高（high / xhigh / max）。

- 通過條件：目前以 high / xhigh / max 思考強度運行
- 不符合 → 立即終止，輸出：
  ```
  [review-DIP 終止] 需要 high 或更高思考強度。
  請執行 /think high（或 xhigh / max）後重新執行 /review-DIP。
  ```

---

## 1. Parse 輸入

支援三種格式（可混用、以逗號分隔）：

1. **完整檔名**：`【FT-06】guild-core.md` → 直接 glob `design/GDD/【FT-06】*.md` 確認存在
2. **GDD 代號**：`FT-06`（regex `^[A-Z]+-\d+$`）→ glob `design/GDD/【<id>】*.md`
3. **波浪號範圍**：`FT-01~FT-03`（regex `^([A-Z]+)-(\d+)~\1-(\d+)$`，前後綴系列代號必須相同）→ 展開為 `FT-01,FT-02,FT-03`

**特殊目標**：`systems-index` 視為合法輸入，對應 `production/systems-index.md`（注意：不在 design/GDD/ 下）。

**錯誤處理**：

- 空輸入 → 報錯：`Usage: /review-DIP <檔名|代號|範圍>[,...]，例：/review-DIP FT-06,FT-07`
- 範圍前後綴不同（如 `FT-01~C-03`）→ 報錯：`範圍前後綴必須相同：<原輸入>`
- 任一 token 解析後在 `design/GDD/` 找不到對應檔案 → 中止並列出未解析項，**不部分執行**
- 任一 token 不在 `DIP-index.md` A 區清單中 → 跳過該 token 並記入摘要「無待處理項」，**不視為錯誤**

---

## 2. 讀取 DIP-index.md

僅讀以下檔案，不要讀全部 GDD：

- `design/GDD/DIP-index.md` — **唯一工作清單來源**：A 區待完成、B 區暫停、C 區已完成

skill 內**不複述** index 內容；解析時聚焦 A 區表格的 `# / 來源 / 工作內容 / 預期落點 / 校驗線索 / 狀態` 六欄。

---

## 3. 篩選工作項

對 §1 解析出的目標 GDD 集合，從 DIP-index.md A 區表格中篩選：

- 匹配條件：A 區某 row 的「目標 GDD」（即子節 A.x 標題系統）= 目標
- **跳過** 狀態 = ✅ 的 row（已完成，避免重做）
- 狀態 = ⏳ 或 🔍 的 row 進入處理佇列
- 跳過原因（已 ✅ / 不在 A 區）分別記入摘要

---

## 4. 對每個 GDD 執行反向登記補丁

針對每個目標 GDD 中的待處理 row，依序：

### 4.1 校驗線索預檢（避免無用功）

1. 讀目標 GDD（完整或 §6 / §6.x 區段，視預期落點）
2. 對該 row 的「校驗線索」（多 token 以 `+` 連接，代表「全部 token 必須同時出現於同一段落 / 同一表格 row」）執行 grep：
   - **全部命中** → 該項已完成，跳過、不修改、標記為 ✅，記入摘要「已存在，無需補登」
   - **未命中或部分命中** → 進入 §4.2 補登

### 4.2 補登反向依賴

依「預期落點」決定寫入位置：

- **§6 反向依賴表 / §6.2 下游表**：在既有表格末追加 row（不重排既有 row、不修改其他欄位）
- **§3.x ISaveable 實作要求**：在該章節末追加段落（標題如「### 3.x ISaveable 實作」，內容含 OwnerKey / IsCritical / Serialize / RestoreFromSave / InitializeAsNewGame 五個介面方法的填寫範本）
- **§3 / §6 末尾單行明示宣告**（如 FT-04「無持久狀態」）：在指定章節末追加一行 blockquote 或 bullet
- **systems-index.md 進度同步**：找到 FT-10 row 改狀態欄；找到進度追蹤表將 GDD 欄位由 ⬜ 改為 ✅

### 4.3 撰寫守則（核心約束，違反即重寫該段）

1. **最小變動**：只寫入「工作內容」明確指定的內容；禁止改寫、重構鄰近文字
2. **不動其他內文**：嚴禁修改與反向依賴無關的章節、表格、列表
3. **格式對齊既有風格**：表格欄位數對齊既有 row、bullet 縮排對齊兄弟條目、標題層級對齊周圍章節
4. **語氣中性**：寫入內容用既有 GDD 的語氣；禁用贅述、口頭禪、emoji
5. **可追溯**：每筆補登的內容須能被 §4.1 的「校驗線索」全部命中，作為下次執行的冪等保證
6. **GDD 結構不符處理**：若預期落點章節不存在（如目標 GDD 沒有 §6.2 下游表）→ **跳過該 row 並記入「結構不符」**，不擅自新增章節

### 4.4 寫入後自檢

對剛補登的內容，重新 grep 校驗線索：

- 全部命中 → 標記該 row 為 ✅
- 仍未全部命中 → 視為失敗、Edit 回退（若 Edit 不支援回退，記入「補登失敗」並繼續下一 row，不阻擋整體執行）

---

## 5. 更新 DIP-index.md

對所有完成（✅）的 row：

1. **狀態欄改為 ✅**：在 A 區該 row 末欄改為 `✅`，並補一段標註完成日期 `（2026-MM-DD 完成）`
2. **不搬移到 C 區**：保留在 A 區並維持 ✅ 狀態，避免破壞表格結構；C 區僅手動歷史歸檔用
3. **D 區統計更新**：A 區待完成數扣除本次完成數

**冪等性**：再次執行同一輸入時，§4.1 預檢應全部命中、§4.2 不執行任何 Edit、§5 無新增 ✅，僅輸出「全部已完成」摘要。

---

## 6. 收尾輸出

僅輸出以下摘要（無多餘評論）：

```
DIP 反向依賴補登完成

輸入: <user args>
解析目標 GDD: <list>

處理結果:
  ✅ 新補登: <count>
    - <目標GDD> A<n>-<k>: <工作內容摘要>（寫入 §<chapter>）
    - ...
  🔍 校驗通過（已存在）: <count>
    - <目標GDD> A<n>-<k>: <校驗線索> 命中
    - ...
  ⚠️ 跳過（結構不符）: <count>
    - <目標GDD> A<n>-<k>: <章節> 不存在
    - ...
  ❌ 補登失敗: <count>
    - <目標GDD> A<n>-<k>: <原因>

DIP-index.md 已更新: A 區 ✅ +<count>，待完成餘 <remaining>
```

---

## 7. 邊界規範

### 7.1 安全範圍

- **只讀** 目標 GDD 的內容並 Edit 既有 row 末追加新 row 或新段落
- **不創建** 新的 GDD 檔案、不刪除任何既有檔案
- **不執行** Bash / 外部命令；僅使用 Read / Glob / Grep / Edit / Write

### 7.2 失敗熔斷

- 若 Pre-flight 模型 / 思考強度檢查不通過 → 立即終止
- 若 §1 解析任一 token 失敗 → 中止整個 skill，不部分執行
- 若 §4.4 自檢連續 3 個 row 失敗 → 中止整個 skill，輸出已完成項與失敗摘要，避免雪崩寫壞多個 GDD

### 7.3 不在範疇

- **不修改** 目標 GDD 中與反向依賴無關的內文（包含但不限於：規則描述、公式、AC、tuning knobs、玩家幻想、§1 概要等）
- **不重組** 章節順序或表格 row 順序
- **不更名** 既有 API / 事件 / 欄位
- **不新增** 章節（若預期落點不存在則記為「結構不符」並跳過）
- **不處理** B 區（暫停項，對端 GDD 尚未設計）
- **不重做** C 區歷史已完成項

### 7.4 與其他 skill 的關係

- `/design-review` 完成後，若該 GDD 的 §6.4 / §6.6 / §6.3 列出新的反向依賴需求 → **由使用者手動更新 DIP-index.md A 區**，再執行本 skill；本 skill **不**自動掃描各 GDD §6.4 增量
- 本 skill 完成後，若有新增 ✅ → systems-index.md 統計可能需同步（A16 條目自動處理）
