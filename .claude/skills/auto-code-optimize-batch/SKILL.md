---
name: auto-code-optimize-batch
description: "批次優化引擎。雙模式：manifest 檔（複雜批次、可版本化）與 inline flag（--auto=<folder> / --gdd=<id>,<id> / --scripts=<path>,<path>，無需編寫 .md）。對多組 scripts 依序在當前 branch 執行 auto-code-optimize 兩輪流程，組間失敗不中斷整批。啟動前 Phase -1 互動式權限預覽 + clean working tree check，全程不自動 push。"
argument-hint: "(--manifest=<path> | --auto=<folder> [--recursive] | --gdd=<id>[,<id>] | --scripts=<path>[,<path>]) [--name=<batch>] [--resume] [--only=<g>[,<g>]] [--unattended]"
user-invocable: true
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, Skill
---

# Auto Code Optimize Batch — 批次優化引擎

## 定位

`auto-code-optimize` 的批次包裝。本 skill 不重複實作優化流程，只做：

1. 讀 manifest 或解析 inline flag → 規格化分組
2. Workspace Safety Check + baseline 記錄
3. 對每組依序呼叫 `auto-code-optimize`（傳 `--batch-group-name=<name>` + `--unattended`）
4. 組間失敗隔離（reset 到該組起點）+ 彙整報告

通常由 `auto-code-optimize` 在偵測到多組時內部呼叫；進階使用者可手動以 manifest 或 inline flag 啟動。

整批共用一個 repo、一個 branch；不建 worktree；Phase D 的 push / reset 決策由使用者統一決定。

---

## 呼叫格式

### 模式 1：Manifest 檔（複雜批次、可版本化）

```
/auto-code-optimize-batch --manifest=<path> [--resume] [--only=...]
```

**範例**：
```
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md --resume
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md --only=time-system,events
```

### 模式 2：Inline（一鍵啟動，無需 .md）

```
# 自動以子資料夾分組
/auto-code-optimize-batch --auto=TheGuild-unity/Assets/Scripts/Core/

# 子資料夾下遞迴展開 .cs
/auto-code-optimize-batch --auto=TheGuild-unity/Assets/Scripts/Core/ --recursive

# 每個 GDD 一組
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02

# 顯式 scripts（單組大批量）
/auto-code-optimize-batch --scripts=PathA.cs,PathB.cs,PathC.cs

# 混合 + 命名
/auto-code-optimize-batch --auto=TheGuild-unity/Assets/Scripts/Core/ --gdd=FT-08 --name=mixed-2026-04-25
```

### 參數總覽

| Flag | 模式 | 說明 |
|---|---|---|
| `--manifest=<path>` | M | manifest 檔（相對 repo root）|
| `--auto=<folder>` | I | 該資料夾的「直接子資料夾」每個自成一組（同 manifest `auto_group_from_folders`）|
| `--recursive` | I | 與 `--auto=` 配合，每子資料夾遞迴展開 `.cs` |
| `--gdd=<id>[,<id>]` | I | 每個 GDD ID 一組；scripts 從 GDD 內抽取（規則同 auto-code-optimize §GDD scripts 抽取）|
| `--scripts=<path>[,<path>]` | I | 將所有 scripts 視為單一組（適合大批量但不需分組）|
| `--name=<batch-name>` | I | 批次名稱（影響 progress / report 檔名）；省略 = `inline-<YYYYMMDD-HHMM>` |
| `--resume` | M+I | 依 progress 跳過已完成組，只跑 pending/failed |
| `--only=<g>[,<g>]` | M+I | 只跑指定組 |
| `--unattended` | M+I | 跳過 Phase -1 互動（auto-code-optimize 內部呼叫時必加）|

Inline 多 flag 可混用（`--auto=` + `--gdd=` 同時使用）；命名衝突會在 Phase A 終止。

---

## Manifest 格式

範本見 `.claude/batch-manifests/_template.md`。簡述：

```markdown
---
manifest: <name>
stop_on_fail: false
group_order: [a, b, c]
auto_group_from_folders:    # 可選；新增的 group 追加到 group_order 尾端
  - <parent_folder>
---

## a
- scripts:
  - <path1.cs>
  - <folder>/    # 非遞迴展開該層 .cs，自動排除 AssemblyInfo.cs
- spec: <path>   # 可選
- notes: <str>   # 可選
- skip: false    # 可選
```

每組建議 2–4 支 .cs；超過 4 印警告但仍執行。`scripts` 可混合單檔與資料夾。

---

## 批次狀態表

```
┌─────────────────────────────┬──────────────────────────┐
│ manifest                    │ <name>                   │
│ total_groups                │ N                        │
│ completed_success           │ K                        │
│ completed_terminated        │ M                        │
│ skipped                     │ S                        │
│ current_group               │ <name or "done">         │
│ batch_baseline_commit       │ <hash>                   │
│ batch_head_commit           │ <hash>                   │
│ current_branch              │ <branch>                 │
│ repo_root                   │ <path>                   │
│ progress_file               │ <path>                   │
│ report_file                 │ <path>                   │
└─────────────────────────────┴──────────────────────────┘
```

---

## Phase -1：啟動前互動確認（unattended 模式跳過）

**目標**：在開始任何 git 操作前，把本次將碰到的 groups、路徑、權限類別攤開給使用者，確認可無人值守再進 Phase A。

當 `--unattended=true` 時跳過。

### Steps

1. **Dry run 解析輸入**
   - manifest 模式：Read manifest，parse frontmatter + sections（不展開資料夾）
   - inline 模式：套用 §A3 規則組 group map（不執行 git）
   - 列出預估 `groups`：names + 每組 scripts 數預估

2. **列印確認區塊**

   ```
   ## /auto-code-optimize-batch — 啟動前確認

   模式：{manifest=<path> | inline}
   名稱：<batch-name>

   groups ({N} 組)：
     - <a> ({M} 支): <preview path>...
     - <b> ({M} 支): ...
     ...

   工具預期會碰到：
     - Codex MCP 寫入（不觸發 Edit/Write 權限）：
       <所有 target scripts 的 parent dirs，去重>
     - 暫存（已預授權）：
       .claude/optimization-work/
       .claude/batch-manifests/.progress/<name>.progress
       .claude/batch-manifests/.reports/<name>-*.md
     - Git 操作（commit / reset --hard 至各 group_start_commit；不 push）
     - Unity MCP（refresh / read_console / run_tests / manage_editor）

   無人值守建議三選一：
     [A] auto-accept：按 Shift+Tab 切到 auto-accept-edits（最簡單）
     [B] 暫存目錄已預授權；Codex MCP 寫入不觸發 prompt → 本次預期不會被打斷
     [C] 啟動 Claude Code 時用 --dangerously-skip-permissions（最不安全；不建議）

   準備好後輸入空訊息或 "go" 繼續；要中止請 Ctrl-C。
   ```

3. **等待 ack**
   - 任意 ack（空訊息 / "go" / "ok" / "continue"）→ 進 Phase A
   - "abort" / "cancel" / "q" → 終止

---

## Phase A：解析輸入並規格化分組

### A1. 模式判斷

- 有 `--manifest=<path>` → 走 §A2
- 否則檢查 inline flag（`--auto=` / `--gdd=` / `--scripts=`）→ 走 §A3
- 都沒有 → 終止「請至少提供 --manifest 或一個 inline flag」

### A2. Manifest mode

1. **Read manifest**，parse frontmatter（`manifest`、`stop_on_fail`、`group_order`、`auto_group_from_folders` 可選）+ 每個 `## <name>` section（建 group map：scripts/spec/notes/skip）

2. **處理 `auto_group_from_folders`**（若存在）
   - 對每個 `parent_folder`：
     - Normalize（補尾 `/`）
     - `Glob <parent>*/` 取直接子資料夾（**非遞迴**）
     - 對每子資料夾：
       - `group_name` = 子資料夾名（保原 PascalCase）
       - `scripts = Glob <subfolder>/*.cs`（非遞迴）排除 `AssemblyInfo.cs`
       - 空 → 跳過（不生成空組）
       - 名稱衝突（與手寫 group 同名）→ 終止
       - 加入 group map：`spec=null`、`notes=null`、`skip=false`
   - 更新 `group_order`：原本有 → 追加 auto-generated names（按字母序）；原本無 → 全用 auto names

3. **資料夾展開**（對所有手寫與 auto-generated 組的 `scripts` 清單）
   - 每個路徑 `p`：
     - `.cs` 結尾 → 單檔保留
     - 否則資料夾：normalize、`Glob <p>*.cs` **非遞迴**、排除 `AssemblyInfo.cs`、空 → 終止
   - 去重；> 4 → 警告（仍執行）

4. **交叉驗證**
   - `group_order` 每個 name 在 group map 存在 → 否則終止「缺少 group：`<name>`」
   - 每組 spec（若有）存在
   - 每組 scripts 展開後非空

### A3. Inline mode

1. **取批次名稱**：`--name=<x>` 或自動 `inline-<YYYYMMDD-HHMM>`；保留為 `manifest_name`

2. **處理 `--auto=<folder>`**（同 §A2 step 2 邏輯）
   - 子資料夾 scripts：含 `--recursive` 用 `<subfolder>/**/*.cs`，否則 `<subfolder>/*.cs`，排除 `AssemblyInfo.cs`
   - 空 → 跳過該子資料夾
   - 加入 group map：`{name: <subfolder>, scripts: ..., spec: null, notes: null}`

3. **處理 `--gdd=<id>[,<id>]`**：對每 id：
   - `Glob design/GDD/[<id>]*.md` → `gdd_path`，找不到 → 終止
   - Read gdd_path，抽 scripts（規則：見 auto-code-optimize §GDD scripts 抽取）
   - 加入 group map：`{name: <id>, scripts: ..., spec: <gdd_path>, notes: null}`

4. **處理 `--scripts=<path>[,<path>]`**：
   - 對每路徑套 §A2 step 3 展開規則（單檔 / 資料夾）
   - 建一組：`{name: "scripts", scripts: ..., spec: null, notes: null}`

5. **名稱衝突檢查**：auto / gdd / scripts 三類來源任兩組同名 → 終止

6. **`group_order`** = group map keys（按處理順序：auto → gdd → scripts）

7. **資料夾展開 + 警告**：同 §A2 step 3-4

### A4. 套用 `--only` / `--resume`

- `--only` → 過濾 `group_order` 只保留指定 names
- `--resume` → 找 `.claude/batch-manifests/.progress/<manifest_name>.progress`，移除已 `done` 的 groups（progress 不存在則視同無 resume，全跑）

進 Phase B。

---

## Phase B：Workspace Safety Check + baseline 記錄

1. `git rev-parse --show-toplevel` → `repo_root`

2. `git status --porcelain -- . ':!.claude/optimization-work' ':!.claude/batch-manifests/.progress' ':!.claude/batch-manifests/.reports'`
   - 非空 → **終止**：
     ```
     [TERMINATED] Working tree 不 clean，無法直接在當前 branch 上執行批次優化。
     請先處理以下變更（stash / commit / 丟棄）後再呼叫：
     {git status --porcelain 輸出}
     ```

3. `git rev-parse --abbrev-ref HEAD` → `current_branch`
   - != "main" → 警告但繼續

4. `mkdir -p <repo_root>/.claude/optimization-work`

5. `batch_baseline_commit = git rev-parse HEAD`、`batch_head_commit = batch_baseline_commit`

6. **準備 progress / report**
   - `progress_file = .claude/batch-manifests/.progress/<manifest_name>.progress`
   - `report_file = .claude/batch-manifests/.reports/<manifest_name>-<YYYYMMDD-HHMM>.md`
   - `mkdir -p` 兩目錄；`--resume` + progress 存在則載入、否則新建
   - 寫 report header（manifest name / 啟動時間 / group_order / branch / baseline）

7. **初始化狀態表**：`total_groups`、`completed_success=0`、`completed_terminated=0`、`skipped=0`；列印

8. 進 Phase C

---

## Phase C：逐組執行

For each `group_name` in `group_order`：

1. **跳過判斷**
   - `skip:true` → 標記 `skipped`
   - `--resume` 且 progress 標記 `done` → 跳過
   - `--only` 但本組不在清單 → 跳過（A4 已過濾，此處保險）

2. **記本組起點**：`group_start_commit = git rev-parse HEAD`，更新 `current_group`，列印狀態表

3. **準備 batch-notes**（若有 notes）：寫 `<repo_root>/.claude/optimization-work/batch-notes-<group_name>.md`

4. **組裝參數並呼叫 auto-code-optimize**
   - `Skill` 工具，`skill = "auto-code-optimize"`、`args = "<scripts space-separated> --batch-group-name=<group_name> --unattended"` + （若有 spec）`--spec=<path>`
   - 等待完成

5. **解析結果**
   - **SUCCESS**（auto-code-optimize Phase 5 batch 模式輸出）：
     - `git rev-parse HEAD` → 更新 `batch_head_commit`
     - progress 增 `<group>|done|<start>..<head>|<timestamp>`
     - report 附加 SUCCESS 摘要
     - `completed_success += 1`
   - **TERMINATED**：
     - 讀取終止原因
     - `git reset --hard <group_start_commit>`（清本組失敗變更，**不影響先前成功組**）
     - progress 增 `<group>|failed|<start>|<timestamp>|<reason>`
     - report 附加 FAILED（含原因）
     - `completed_terminated += 1`
     - 若 `stop_on_fail = true` → 跳出 for 進 Phase D

6. **清理 batch-notes 檔**（若 step 3 建過）

7. 更新狀態表，列印；繼續下一組

---

## Phase D：彙整回報

1. **寫 report footer**：完成時間、總計、最終 `batch_head_commit`、成功 commit 範圍、失敗原因清單

2. **列印摘要**
   ```
   ## Auto Code Optimize Batch — 完成

   Manifest:  {manifest_name}
   Branch:    {current_branch}
   Baseline:  {batch_baseline_commit}   ← 整批啟動前 HEAD
   HEAD:      {batch_head_commit}       ← 成功組全部 commit 後 HEAD

   Results:
   ✅ Success   : {completed_success} / {total_groups}
   ❌ Terminated: {completed_terminated}
   ⏭  Skipped   : {skipped}

   Success 組：
   - {group_a}: commits {hash1..hash2}
   - {group_b}: commits {hash3..hash4}

   Terminated 組（變更已 reset 回該組起點，不污染 HEAD）：
   - {group_c}: {reason 摘要}

   Report:   {report_file}
   Progress: {progress_file}  ← 下次可用 --resume 續跑失敗組

   下一步由使用者決定（所有 commit 都在 local，**未 push**）：
     [A] `git push` 推到 remote（包含所有成功組的 commit）
     [B] 保留不 push，繼續手動驗證
     [C] 捨棄整批：`git reset --hard {batch_baseline_commit}`
         （回啟動前狀態，**所有成功組的 commit 全數消失**）
   ```

3. **若本批是 auto-code-optimize 內部呼叫的 inline manifest**（manifest_name 以 `inline-` 開頭）：
   - 由 caller skill 在控制權回去後刪除 `<repo_root>/.claude/optimization-work/_inline-*.md`
   - 本 skill 不主動刪除（避免 caller 還沒讀完報告就被清掉）

4. **不自動 push / reset**

---

## Progress 檔格式

純文字，每行一個 group 狀態，`|` 分隔：

```
<group_name>|<status>|<commit_range_or_commit>|<timestamp>[|<reason>]
```

範例：
```
time-system|done|abc123..def456|2026-04-25T14:30:00
data-layer|failed|def456|2026-04-25T15:10:00|test_round > 10 after 3 reverts
events|done|def456..789xyz|2026-04-25T15:45:00
```

`--resume` 只跑 `failed` 或未列出的 group；`done` 跳過。

---

## 失敗路徑

| # | 條件 | 動作 |
|---|---|---|
| BF1 | Manifest 檔不存在 / 格式錯誤 / inline 參數無效 | 終止 |
| BF2 | Phase B clean tree 不通過 | 終止，整批不啟動 |
| BF3 | Group 中 script/folder/GDD 解析失敗或為空 | 終止 |
| BF4 | `group_order` 中 name 不存在於 group map | 終止 |
| BF5 | 某組 TERMINATED 且 `stop_on_fail=true` | 本組 reset，進 Phase D 出部分結果 |
| BF6 | 某組 TERMINATED 且 `stop_on_fail=false` | 本組 reset，續下組 |
| BF7 | Phase -1 使用者 abort | 終止（無副作用）|

**關鍵語意**：Phase A/B 失敗 → 整批不啟動；Phase C 某組失敗 → 視 `stop_on_fail`；本 skill 不自動 `git reset --hard <batch_baseline_commit>`，回退整批是使用者在 Phase D 的選項 [C]。

---

## 執行紀律（核心 6 條）

1. **單一 repo 單一 branch**：當前 branch 直接 commit；禁建 worktree / temp branch、禁 `git checkout` / `switch`
2. **禁自動 push / 禁自動 reset baseline**：`git push` 與 `git reset --hard <batch_baseline_commit>` 由使用者在 Phase D 決定；組內失敗 reset 只到 `group_start_commit`
3. **Progress 持久化**：每組結束立即寫 progress；`--resume` 只重跑 failed / 未列出
4. **嚴格序列**：禁並行（避免 Unity instance / Codex session 衝突）
5. **中斷處理**：Ctrl-C 後當前組可能有 uncommitted 變更；下次 `--resume` 前需自行清理 working tree（Phase B clean check 會擋）
6. **不降級工具**：禁 `--no-verify` / `--no-gpg-sign`

---

## 術語對應

| batch 術語 | 對應 |
|---|---|
| `batch_baseline_commit` | 整批啟動前 HEAD（Phase D [C] 回退點）|
| `batch_head_commit` | 所有成功組累加後的 HEAD |
| `group_start_commit` | 單組起點（auto-code-optimize 的 `baseline_commit`）|
| `manifest_name` | 影響 progress/report 檔名；inline 模式取自 `--name=` 或 `inline-<YYYYMMDD-HHMM>` |
| `group` | 單次 `auto-code-optimize` 的 target_scripts 集合 |
| `current_branch` | 呼叫時當前 branch（預設 main，警告但允許其他 branch）|
