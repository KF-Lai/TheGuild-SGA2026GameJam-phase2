---
name: auto-code-optimize-batch
description: "auto-code-optimize 的批次引擎。雙模式：manifest .md 檔（可版本化、--resume／--only=）與 inline flag（--auto=／--gdd=／--scripts=，無需 .md）。對多組依序在當前 branch 執行兩輪流程，組間失敗 reset 隔離，全程不自動 push。"
argument-hint: "(--manifest=<path> | --auto=<folder> [--recursive] | --gdd=<id>[,<id>] | --scripts=<path>[,<path>]) [--name=<batch>] [--resume] [--only=<g>[,<g>]] [--unattended]"
user-invocable: true
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, Skill
---

# Auto Code Optimize Batch

兩個 skill 怎麼選：見 `.claude/docs/auto-code-optimize-usage.md` §兩個 skill 的關係。本 skill 處理多組 + 失敗隔離 + 統一報告，並獨佔 manifest 模式與 `--resume`／`--only=`。

通常由 `auto-code-optimize` 偵測多組時內部呼叫；進階使用者可手動以 manifest 或 inline flag 啟動。整批共用一個 repo、一個 branch；不建 worktree；Phase D 的 push／reset 由使用者統一決定。

## 呼叫格式

### 模式 1：Manifest 檔

```
/auto-code-optimize-batch --manifest=<path> [--resume] [--only=<g>[,<g>]]
```

範本：`.claude/batch-manifests/_template.md`（schema、欄位、範例都在裡面）。

### 模式 2：Inline flag

```
/auto-code-optimize-batch --auto=<folder>           # 子資料夾每個一組
/auto-code-optimize-batch --auto=<folder> --recursive
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02     # 每 GDD 一組
/auto-code-optimize-batch --scripts=A.cs,B.cs,C.cs  # 全部視為一組
/auto-code-optimize-batch --auto=<folder> --gdd=FT-08 --name=mixed-2026-04-25
```

### Flag 總覽

| Flag | 模式 | 說明 |
|---|---|---|
| `--manifest=<path>` | M | manifest 檔（相對 repo root）|
| `--auto=<folder>` | I | 直接子資料夾每個自成一組（同 manifest `auto_group_from_folders`）|
| `--recursive` | I | 與 `--auto=` 配合：每子資料夾遞迴展開 .cs |
| `--gdd=<id>[,<id>]` | I | 每 GDD 一組；scripts 抽取規則同 `auto-code-optimize` §GDD scripts 抽取 |
| `--scripts=<path>[,<path>]` | I | 全部視為單一組 |
| `--name=<batch>` | I | 批次名稱，影響 progress/report 檔名；省略 = `inline-<YYYYMMDD-HHMM>` |
| `--resume` | M+I | 依 progress 跳已 done，重跑 failed/未列出 |
| `--only=<g>[,<g>]` | M+I | 只跑指定組 |
| `--unattended` | M+I | 跳過 Phase -1（auto-code-optimize 內部呼叫必加）|

Inline flag 可混用；命名衝突在 Phase A 終止。

## 流程

```
Phase -1 互動確認（unattended 跳過）
   ↓
Phase A 解析輸入 → 規格化分組（manifest／inline／--resume／--only=）
   ↓
Phase B Workspace Check + 記 batch_baseline + 開 progress/report
   ↓
Phase C 逐組執行：每組呼叫 auto-code-optimize（--unattended）
        SUCCESS：累加 commit；TERMINATED：reset 到 group_start_commit
   ↓
Phase D 彙整回報（不自動 push／reset）
```

### 批次狀態欄位

| 欄位 | 說明 |
|---|---|
| `manifest_name` | 影響 progress/report 檔名；inline 取自 `--name=` 或 `inline-<ts>` |
| `total_groups` / `completed_success` / `completed_terminated` / `skipped` | 計數 |
| `current_group` | 當前執行組，全完 = `done` |
| `batch_baseline_commit` | 整批啟動前 HEAD（Phase D [C] 回退點）|
| `batch_head_commit` | 所有成功組累加後的 HEAD |
| `progress_file` / `report_file` | `.claude/batch-manifests/.progress|.reports/<name>.*` |
| `current_branch` / `repo_root` | — |

## Phase -1：互動確認

**跳過條件**：`--unattended`。

1. **Dry run 解析**
   - manifest 模式：Read manifest，parse frontmatter + sections（不展開資料夾）
   - inline 模式：套 §A3 規則組 group map（不執行 git）
   - 列出預估 groups：names + 每組 scripts 數預估

2. **列印確認區塊**（同 `auto-code-optimize` Phase -1 結構，差異：暫存目錄含 `.claude/batch-manifests/.progress|.reports/`；Git 操作含「reset --hard 至各 group_start_commit」）

3. **等待 ack**：空訊息／"go"／"ok"／"continue" → Phase A；"abort"／"cancel"／"q" → 終止。

## Phase A：解析輸入並規格化分組

### A1. 模式判斷

- `--manifest=<path>` → §A2
- 否則檢 inline flag（`--auto=` ／ `--gdd=` ／ `--scripts=`）→ §A3
- 都沒有 → 終止「請至少提供 --manifest 或一個 inline flag」

### A2. Manifest 模式

1. **Read manifest**：parse frontmatter（`manifest`／`stop_on_fail`／`group_order`／可選 `auto_group_from_folders`） + 每個 `## <name>` section（`scripts`／`spec`／`notes`／`skip`）
2. **`auto_group_from_folders`**（若存在）：對每 `parent_folder` Glob 直接子資料夾（**非遞迴**）；每子資料夾 `name = 子資料夾名`、`scripts = Glob <subfolder>/*.cs` 排除 `AssemblyInfo.cs`、空 → 跳過、同名衝突 → 終止；`group_order` 已有 → 追加 auto names（字母序），無 → 全用 auto names
3. **資料夾展開**（所有組的 `scripts`）：`.cs` 結尾保留；資料夾 normalize + Glob `<p>*.cs` 非遞迴 + 排除 `AssemblyInfo.cs`，空 → 終止；去重；>4 → 警告（仍執行）
4. **交叉驗證**：`group_order` 每 name 在 group map 存在；每組 spec（若有）存在；每組 scripts 展開後非空

### A3. Inline 模式

1. **批次名稱**：`--name=<x>` 或 `inline-<YYYYMMDD-HHMM>` → `manifest_name`
2. **`--auto=<folder>`**：子資料夾 scripts，含 `--recursive` 用 `<sub>/**/*.cs`，否則 `<sub>/*.cs`，排除 `AssemblyInfo.cs`；空 → 跳過該子資料夾；group map：`{name: <subfolder>, scripts, spec: null, notes: null}`
3. **`--gdd=<id>[,<id>]`**：對每 id Glob `design/GDD/[<id>]*.md` → `gdd_path`（找不到終止）；Read + 抽 scripts（規則見 `auto-code-optimize` §GDD scripts 抽取）；group map：`{name: <id>, scripts, spec: <gdd_path>, notes: null}`
4. **`--scripts=<path>[,<path>]`**：套 §A2 step 3 展開；建單組 `{name: "scripts", scripts, spec: null, notes: null}`
5. **名稱衝突檢查**：auto／gdd／scripts 三類來源任兩組同名 → 終止
6. **`group_order`** = group map keys（處理順序：auto → gdd → scripts）
7. **資料夾展開 + 警告**：同 §A2 step 3-4

### A4. `--only` ／ `--resume`

- `--only` → 過濾 `group_order` 只保留指定 names
- `--resume` → 找 `.claude/batch-manifests/.progress/<manifest_name>.progress`，移除已 `done` 組（progress 不存在視同無 resume）

進 Phase B。

## Phase B：Workspace Check + baseline

1. `git rev-parse --show-toplevel` → `repo_root`
2. `git status --porcelain -- . ':!.claude/optimization-work' ':!.claude/batch-manifests/.progress' ':!.claude/batch-manifests/.reports'` 非空 → **終止**並列出
3. `git rev-parse --abbrev-ref HEAD` → `current_branch`，非 main 警告但繼續
4. `mkdir -p .claude/optimization-work`
5. `batch_baseline_commit = batch_head_commit = git rev-parse HEAD`
6. **準備 progress／report**
   - `progress_file = .claude/batch-manifests/.progress/<manifest_name>.progress`
   - `report_file = .claude/batch-manifests/.reports/<manifest_name>-<YYYYMMDD-HHMM>.md`
   - `mkdir -p` 兩目錄；`--resume` + progress 存在 → 載入；否則新建
   - 寫 report header（manifest／啟動時間／group_order／branch／baseline）
7. 初始化狀態欄位（`completed_*=0`）並列印 → Phase C

## Phase C：逐組執行

對每 `group_name` in `group_order`：

1. **跳過判斷**：`skip:true`／`--resume` progress 標 `done`／`--only` 不在清單（A4 已過濾，保險） → 標 `skipped`
2. **記起點**：`group_start_commit = git rev-parse HEAD`，更新 `current_group`，列印狀態
3. **batch-notes**（若有 notes）：寫 `.claude/optimization-work/batch-notes-<group_name>.md`
4. **呼叫 auto-code-optimize**：`Skill` 工具，`skill="auto-code-optimize"`、`args="<scripts space-separated> --batch-group-name=<group_name> --unattended" + （若有 spec）"--spec=<path>"`
5. **解析結果**
   - **SUCCESS**：`git rev-parse HEAD` → `batch_head_commit`；progress 增 `<g>|done|<start>..<head>|<ts>`；report 附 SUCCESS 摘要；`completed_success++`
   - **TERMINATED**：讀終止原因；`git reset --hard <group_start_commit>`（清本組變更，不影響先前成功組）；progress 增 `<g>|failed|<start>|<ts>|<reason>`；report 附 FAILED；`completed_terminated++`；`stop_on_fail=true` → 跳出 → Phase D
6. **清 batch-notes 檔**（若 step 3 建過）
7. 更新狀態，列印；下一組

## Phase D：彙整回報

1. **寫 report footer**：完成時間、總計、最終 `batch_head_commit`、成功 commit 範圍、失敗原因清單
2. **列印摘要**（含 manifest／branch／baseline／HEAD／Success/Terminated/Skipped 計數／成功組 commit ranges／失敗組 reset 後的原因／Report 與 Progress 路徑／使用者選項 [A push][B 保留][C `git reset --hard {batch_baseline_commit}` 捨棄整批]）
3. **若 inline manifest**（`manifest_name` 以 `inline-` 開頭）：caller skill（`auto-code-optimize`）負責清 `.claude/optimization-work/_inline-*.md`；本 skill 不主動清（避免 caller 還沒讀完報告）
4. **不自動 push／reset**

## Progress 檔格式

純文字，每行一個 group，`|` 分隔：

```
<group_name>|<status>|<commit_range_or_commit>|<timestamp>[|<reason>]
```

範例：
```
time-system|done|abc123..def456|2026-04-25T14:30:00
data-layer|failed|def456|2026-04-25T15:10:00|test_round > 10 after 3 reverts
events|done|def456..789xyz|2026-04-25T15:45:00
```

`--resume` 只跑 `failed` 或未列出組；`done` 跳過。

## 失敗路徑

| # | 條件 | 動作 |
|---|---|---|
| BF1 | Manifest 不存在／格式錯／inline 參數無效 | 終止 |
| BF2 | Phase B clean tree 不通過 | 終止，整批不啟動 |
| BF3 | Group 中 script／folder／GDD 解析失敗或為空 | 終止 |
| BF4 | `group_order` 中 name 不在 group map | 終止 |
| BF5 | 某組 TERMINATED 且 `stop_on_fail=true` | 本組 reset，進 Phase D 出部分結果 |
| BF6 | 某組 TERMINATED 且 `stop_on_fail=false` | 本組 reset，續下組 |
| BF7 | Phase -1 使用者 abort | 終止（無副作用）|

**關鍵語意**：Phase A/B 失敗 → 整批不啟動；Phase C 某組失敗 → 視 `stop_on_fail`；本 skill **不**自動 `git reset --hard <batch_baseline_commit>`，回退整批是 Phase D 選項 [C]。

## 執行紀律

- **單一 repo 單一 branch**：當前 branch 直接 commit；禁建 worktree／temp branch、禁 `checkout`／`switch`
- **禁自動 push／禁自動 reset baseline**：`git push` 與 `git reset --hard <batch_baseline_commit>` 由使用者在 Phase D 決定；組內失敗 reset 只到 `group_start_commit`
- **Progress 持久化**：每組結束立即寫；`--resume` 只重跑 failed／未列出
- **嚴格序列**：禁並行（避免 Unity instance／Codex session 衝突）
- **中斷處理**：Ctrl-C 後當前組可能 uncommitted；下次 `--resume` 前自行清 working tree（Phase B clean check 會擋）
- **不降級工具**：禁 `--no-verify`／`--no-gpg-sign`
