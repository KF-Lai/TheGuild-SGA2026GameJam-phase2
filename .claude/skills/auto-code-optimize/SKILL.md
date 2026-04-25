---
name: auto-code-optimize
description: "智能單組優化主入口。支援 .cs 檔（單/多）、資料夾（非遞迴）、--recursive 遞迴、--gdd=<ID>[,<ID>] 從 GDD 自動找 scripts 等多種輸入。解析後若為單組直接執行兩輪 Phase 1-5 流程，多組自動內部組 inline manifest 呼叫 auto-code-optimize-batch（無需 .md）。Claude Code 系統審查 + Codex 實作協作於當前 branch（預設 main），啟動前 Phase -1 互動式權限預覽 + clean working tree check，全程不自動 push。"
argument-hint: "<input>... [--spec=<path>] [--recursive|-r] [--gdd=<id>[,<id>]] [--unattended]"
user-invocable: true
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, Skill, mcp__codex__codex, mcp__UnityMCP__refresh_unity, mcp__UnityMCP__read_console, mcp__UnityMCP__run_tests, mcp__UnityMCP__manage_editor
---

# Auto Code Optimize — AI Pair 優化主入口

## 角色

- **Claude Code**：資深遊戲工程師，系統/架構視角；審查需求、code-review Codex 產出、判斷階段轉移。
- **Codex**：資深遊戲工程師，程式人員視角；實作、self-review、寫入。

**原則**：除非觸發終止條件，使用者不介入；最大化 AI 自動化。

---

## 呼叫格式

智能解析任意組合輸入。

| 模式 | 範例 |
|---|---|
| 單檔 | `/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs` |
| 多檔（空白或逗號） | `/auto-code-optimize Foo.cs Bar.cs` 或 `Foo.cs,Bar.cs` |
| 資料夾（非遞迴） | `/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/` |
| 資料夾（遞迴） | `/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r` |
| 單一 GDD | `/auto-code-optimize --gdd=FT-02` |
| 多 GDD（自動轉批次） | `/auto-code-optimize --gdd=FT-02,F-01,P-02` |
| 純 ID 等價於 --gdd | `/auto-code-optimize FT-02 F-01` |
| 帶 spec 覆寫 | `/auto-code-optimize Core/Time/ --spec=design/GDD/[FT-xx].md` |

### 解析優先級（每個 token，逗號自動拆分後處理）

1. 以 `--` 或 `-` 開頭 → flag
2. 以 `.cs` 結尾 → 單檔
3. 含路徑分隔符（`/` 或 `\`）或可被 `Bash test -d` 確認為目錄 → 資料夾
4. 符合 `^[A-Z]+-\d+[a-z]?$`（FT-02 / F-01 / P-02 / FT-08a 等）→ GDD ID（與 `--gdd=` 等價）
5. 否則 → 終止「無法解析輸入：`<token>`」

### Flag

| Flag | 說明 |
|---|---|
| `--spec=<path>` | 顯式 spec；省略時依輸入推導（GDD 模式自動帶 GDD；其他自動生成 internal spec）|
| `--recursive` / `-r` | 資料夾遞迴展開 |
| `--gdd=<id>[,<id>]` | 從 `design/GDD/[<id>] *.md` 找 spec 與 scripts；多 ID 自動轉批次 |
| `--unattended` | 跳過 Phase -1 互動確認（給 batch wrapper 內部呼叫使用，或你已預先預授權所有權限的離線情境）|
| `--batch-group-name=<name>` | 由 batch wrapper 注入；標記為批次模式（內部用）|

---

## 11 條優化原則（貫穿審查標準）

1. **已優化掉的作法不加回**：檢查是否回退了先前 commit 已刪除的實作
2. **風格一致性**：命名、縮排、大括號順從既有檔案 + `.claude/rules/gameplay-code.md`
3. **可讀性**：表意命名、扁平流程、適度斷句
4. **重複邏輯函式化**：相似邏輯 ≥ 2 處考慮抽共用
5. **職責清晰度**：類別/方法 SRP
6. **簡潔度**：移除不必要抽象、未使用欄位、死代碼
7. **效能**：熱路徑無 alloc、避免 `Find()`/`FindObjectOfType()`、`Awake()` 快取引用
8. **可靠性**：null 檢查、狀態一致性、`OnEnable`/`OnDisable` 對稱
9. **參數表格化**：閾值/常數/公式來自 CSV 或 ScriptableObject
10. **冪等性**：重複呼叫結果一致、可安全重試
11. **預測 Try-Catch**：邊界（I/O、解析、外部 API）加 catch + log；熱路徑禁止吞例外

---

## 流程概觀

```
Phase -1 互動確認（unattended 跳過）
   ↓
Phase 0 解析輸入 + Workspace Safety Check
   ↓ 多組
   組 inline manifest → 呼叫 auto-code-optimize-batch（控制權移交）
   ↓ 單組
Phase 1 需求準備 → Phase 2 Review-Fix（≥3 輪 + APPROVED）
   → Phase 3 Unity MCP 測試迴圈 → Phase 4 第二輪？→ Phase 5 結束回報
```

每個 Phase 結束時更新並列印狀態表。

---

## 狀態追蹤表

```
┌─────────────────────────────┬──────────────────┐
│ optimization_round          │ 1 of 2           │
│ review_round (cumulative)   │ 0 / ≥3 required  │
│ test_round                  │ 0 / 10 max       │
│ revert_count                │ 0 / 3 max        │
│ spec_fix_attempts           │ 0 / 3 max        │
│ codex_session_id            │ <uuid or none>   │
│ baseline_commit             │ <hash>           │
│ last_stable_commit          │ <hash>           │
│ repo_root                   │ <path>           │
│ target_scripts              │ [list]           │
│ spec_path                   │ <path>           │
└─────────────────────────────┴──────────────────┘
```

---

## Phase -1：啟動前互動確認（unattended 模式跳過）

**目標**：在開始任何 git 操作前，把本次將碰到的路徑、模式、權限類別攤開給使用者，確認可無人值守再進 Phase 0。

當 `--unattended=true` 或被 `auto-code-optimize-batch` 以 `--batch-group-name=` 注入時，跳過此 Phase。

### Steps

1. **Dry run 解析輸入**（不執行任何 git，只用 Read / Glob / `Bash test`）
   - 套用 §解析優先級
   - 展開資料夾（依 `-r` 遞迴或非遞迴）
   - 解析 GDD ID（Glob `design/GDD/[<id>]*.md`，Read 後抽 scripts；§GDD scripts 抽取規則）
   - 計算預估 `groups`：
     - 多 GDD → 每 GDD 一組
     - 多個獨立資料夾 / `-r` 跨多個直接子資料夾 → 每子資料夾一組
     - 否則 → 單組（合併所有 .cs / 單資料夾 / 單 GDD）

2. **列印確認區塊**

   ```
   ## /auto-code-optimize — 啟動前確認

   解析模式：{單組 | 批次 N 組}

   {若單組:}
     target_scripts ({M} 支)：
       - <path1>
       - <path2>
       ...
     spec： <path or "Phase 1 自動生成 internal spec">

   {若批次:}
     groups ({N} 組)：
       - <group_a> ({M} 支): <preview path>...
       - <group_b> ({M} 支): ...
     將呼叫 auto-code-optimize-batch（inline manifest，無需編寫 .md）

   工具預期會碰到：
     - Codex MCP 寫入（不觸發 Edit/Write 權限）：
       <target scripts 的 parent dirs，去重>
     - 暫存（已預授權於 settings.local.json）：
       .claude/optimization-work/
       {若批次:} .claude/batch-manifests/.progress/、.reports/
     - Git 操作（commit / reset --hard，不 push）
     - Unity MCP（refresh / read_console / run_tests / manage_editor）

   無人值守建議三選一：
     [A] auto-accept：按 Shift+Tab 切到 auto-accept-edits（最簡單）
     [B] 暫存目錄已預授權；Codex MCP 寫入不觸發 prompt → 本次預期不會被打斷
     [C] 啟動 Claude Code 時用 --dangerously-skip-permissions（最不安全；不建議）

   準備好後輸入空訊息或 "go" 繼續；要中止請 Ctrl-C。
   ```

3. **等待 ack**
   - 任意 ack（空訊息 / "go" / "ok" / "continue"）→ 進 Phase 0
   - "abort" / "cancel" / "q" → 終止

---

## Phase 0：解析輸入並分派

**目標**：完整解析參數、Workspace Safety Check、依 group 數分派單組或批次流程。

### Steps

1. **完整解析參數**（同 Phase -1 dry run，這次正式版）
   - 收集 `tokens_files` / `tokens_folders` / `tokens_gdd_ids` / `flag_spec` / `flag_recursive` / `flag_unattended` / `flag_batch_group_name`

2. **展開資料夾**
   - 對每個 folder：`Glob` 非遞迴 `<folder>*.cs` 或遞迴 `<folder>**/*.cs`
   - 排除 `AssemblyInfo.cs`；空清單 → 終止

3. **解析 GDD ID**
   - 對每個 id：`Glob design/GDD/[<id>]*.md`、`Read` GDD、抽 scripts（§GDD scripts 抽取）
   - 找不到 / 抽取空 → 終止

4. **構建 groups（單組 vs 批次判斷）**
   - 多 GDD（≥ 2）或多個獨立資料夾或 `-r` 跨多個直接子資料夾 → 多組
   - 否則 → 單組（合併所有 .cs / 單資料夾 / 單 GDD）

5. **多組 → 分派 batch**（若 `len(groups) > 1`）
   - 寫 inline manifest 到 `<repo_root>/.claude/optimization-work/_inline-<YYYYMMDD-HHMMSS>.md`
     - frontmatter：`manifest: inline-<timestamp>`、`stop_on_fail: false`、`group_order: [<names>]`
     - 每組一個 `## <name>` section，含 `scripts:` / `spec:`（若有） / `notes:`（若有）
   - 用 `Skill` 工具呼叫 `auto-code-optimize-batch`，args = `--manifest=<inline file> --unattended`（Phase -1 已確認，不再彈第二次）
   - batch skill 完成後刪除 inline manifest 檔
   - 結束本 skill（控制權已移交給 batch）

6. **單組情境**（若 `len(groups) == 1`）：解構 `groups[0]` 為 `target_scripts` / `spec_path` / `notes`

7. **讀取專案規範**
   - Read `CLAUDE.md` / `.claude/rules/gameplay-code.md` / `.claude/rules/ui-code.md`（若 target 含 UI） / `.claude/docs/coding-standards.md`

8. **Workspace Safety Check**
   - `git rev-parse --show-toplevel` → `repo_root`
   - `git status --porcelain -- . ':!.claude/optimization-work' ':!.claude/batch-manifests/.progress' ':!.claude/batch-manifests/.reports'`
     - 非空 → **終止**：
       ```
       [TERMINATED] Working tree 不 clean，無法直接在當前 branch 上執行優化。
       請先處理以下變更（stash / commit / 丟棄）後再呼叫：
       {git status --porcelain 輸出}
       ```
   - `git rev-parse --abbrev-ref HEAD` → `current_branch`
     - != "main" → 警告但繼續（「目前在 `<current_branch>`，將直接 commit 到該 branch」）
   - `mkdir -p <repo_root>/.claude/optimization-work`
   - `baseline_commit = git rev-parse HEAD`、`last_stable_commit = baseline_commit`
   - 若 `flag_batch_group_name` 非 null → `batch_mode = true`、記錄 `batch_group_name`

9. **若有 notes**：寫 `<repo_root>/.claude/optimization-work/optimization_addendum_r1.md`（內容為 notes，Phase 1 階段使用）

10. **初始化狀態表**
    - `optimization_round = 1`、`review_round = 0`、`test_round = 0`、`revert_count = 0`、`spec_fix_attempts = 0`、`codex_session_id = null`
    - 列印狀態表

11. **進 Phase 1**

---

## GDD scripts 抽取規則

從 GDD .md 檔抽出對應 scripts 路徑：
1. 若 frontmatter 有 `scripts:` 陣列 → 直接用
2. 找標題包含「實作檔案」/「Scripts」/「Implementation Files」/「目標檔案」的 section，抽其中所有 ` `<path>.cs` ` inline code
3. 在文件全文搜 `TheGuild-unity/Assets/Scripts/.*\.cs` 的 code reference
4. 結果去重 + Read 驗證存在
5. 若空 → 終止「GDD `<id>` 中找不到 scripts 引用，請以 `--spec=<gdd>` 配合明確 scripts 路徑呼叫」

---

## Phase 1：需求準備（規則 2-4）

**目標**：CC 系統視角確認需求，產出 Codex 可讀的 .md。

### Steps

1. **CC 需求審查**（規則 2）
   - 若 `spec_path` 非 null → Read，比對 target_scripts 現狀，缺漏項補 addendum 寫到 `<repo_root>/.claude/optimization-work/optimization_addendum_r{round}.md`
   - 若 null → CC 生成 internal spec 寫到 `<repo_root>/.claude/optimization-work/optimization_spec_r{round}.md`，含：
     - `## 目標檔案`、`## 優化目標`（11 條原則中本輪聚焦）、`## 現狀問題點`、`## 不得回退的先前決策`、`## Acceptance Criteria`
   - 設 `spec_path = <生成的 spec>`
   - **Batch 模式補充**：若 `batch_mode=true` 且 `<repo_root>/.claude/optimization-work/batch-notes-<group>.md` 存在 → 讀入合併到 addendum 或 spec 的 `## Batch Group Notes` section

2. **發送 spec 給 Codex**（規則 3）
   - `mcp__codex__codex` 呼叫：
     - `cd = repo_root`、`sandbox = "read-only"`、`SESSION_ID = null`（首次）、`return_all_messages = true`
   - **Prompt 範本（首次 dispatch）**：
     ```
     你是資深遊戲工程師，負責程式實作。請閱讀工項需求書並確認理解。

     需求書：{spec_path}
     目標 Scripts：{target_scripts}
     優化輪次：{optimization_round} / 2

     任務：
     1. 完整讀取需求書
     2. 讀取所有 target scripts
     3. 回覆：(a) 需求摘要 (b) 你打算如何實作（綱要）(c) 有無讀不到或不清楚的地方

     **先不要實作**。等 Claude Code 確認後才動手。
     ```
   - 記錄返回的 `session_id` 到 `codex_session_id`

3. **驗證 Codex 讀取成功**（規則 4）
   - 成功訊號：摘要 + 實作綱要與 spec 一致
   - 失敗訊號：「找不到檔案」/「unclear path」/ 摘要明顯偏離 spec
   - 失敗時 `spec_fix_attempts += 1`：
     - `> 3` → **終止**，回報三次嘗試與最後 Codex 回應
     - 否則依次嘗試：(1) 改絕對路徑 (2) 簡化 spec markdown (3) spec 全文嵌入 PROMPT
   - 修正後重試 step 2

4. **進 Phase 2**

---

## Phase 2：Review-Fix 迴圈（規則 5-7）

**目標**：≥ 3 完整輪 + 最終 APPROVED 才進 Phase 3。

**輪次定義**：一輪 = 「Codex 實作/修正 → CC review」完整來回。

**輪次聚焦**：
- 輪 1：通用結構（職責、風格、可讀性、簡潔度）
- 輪 2：重複邏輯函式化、參數表格化、已優化方案保護
- 輪 3：效能、可靠性、冪等性、預測 Try-Catch

### Steps（每一輪）

1. **Codex 實作或修正**（規則 5）
   - `mcp__codex__codex` 呼叫：
     - `cd = repo_root`、`sandbox = "read-only"`（Phase 2 全程唯讀）、`SESSION_ID = codex_session_id`、`return_all_messages = true`
   - **Prompt 範本（第 N 輪）**：
     ```
     【本輪聚焦】{第 N 輪聚焦重點}
     【Claude Code 回饋】{上輪 Required Changes；第 1 輪為 "初始實作"}

     任務：
     1. 實作（或修正）target scripts
     2. 完成後自行 code review，依 11 條優化原則檢查
     3. self-review 發現問題先修正再回報
     4. 回報格式：
        a. 修改後完整 diff（unified format）
        b. self-review 結論（哪些原則檢查 / 通過 / 修正）
        c. 仍未處理的疑點

     **sandbox 為 read-only，不要寫入檔案**，只回傳 diff 和 review。
     ```

2. **驗證 Codex 回傳格式**：缺 diff 或 self-review → 同 session 追問補齊（最多 1 次）；仍缺 → 視為本輪失敗，註記要求下輪補齊

3. **CC 雙重 review**（規則 6）
   - **Part A — 需求/GDD 對齊**：Read `spec_path` 與相關 GDD（`design/GDD/`），對照 diff 檢查偏離 / Acceptance Criteria / GDD 行為一致
   - **Part B — 程式碼審查**：呼叫 `code-review` skill (Mode 1)；額外查本輪聚焦項

4. **產出審查結論**
   ```
   ## Phase 2 Review Round {N} / {optimization_round}

   ### 本輪聚焦
   {第 N 輪聚焦重點}

   ### 需求/GDD 對齊
   [PASS / ISSUES: ...]

   ### 11 原則檢查（本輪項）
   - [ ] 原則 X：...
   - [ ] 原則 Y：...

   ### Required Changes
   [列出具體修改點，含 line reference]

   ### Positive Observations
   [做得好的部分，避免 Codex 下輪誤改回]

   ### Verdict: [APPROVED / CHANGES REQUIRED]
   ```

5. **狀態更新**：`review_round += 1`
   - 進 Phase 3 條件：`review_round ≥ 3` 且本輪 = APPROVED
   - 否則回 step 1：
     - `Verdict = CHANGES REQUIRED` → 將 Required Changes 作為下輪 Codex 回饋
     - `Verdict = APPROVED` 但 `review_round < 3` → 「本聚焦項通過，進入下一聚焦項目」作為下輪 prompt 輸入
   - 列印狀態表

6. **進 Phase 3**（條件滿足）

---

## Phase 3：Unity MCP 測試迴圈（規則 8-12）

**目標**：Codex 寫入 → Unity 編譯 + 測試 → 0 錯誤即 commit。錯誤直接 Codex 修，不走 Phase 2。

### Steps

1. **Codex 寫入**：`sandbox = "workspace-write"`、SESSION_ID 不變
   - PROMPT：「請將上一輪 APPROVED 的 diff 實際寫入對應檔案。完成後回報已寫入清單。」
   - 驗證 `success=true` 且 `git diff --stat` 有變更

2. **Unity 編譯**：`mcp__UnityMCP__refresh_unity` → 輪詢 `editor_state.isCompiling==false`

3. **編譯錯誤檢查**：`mcp__UnityMCP__read_console` 過濾 error → 有則跳 step 6

4. **測試**：`mcp__UnityMCP__run_tests`（EditMode + PlayMode 依設定）；解析 pass / fail 數與失敗 test 名

5. **runtime / test 錯誤**：`read_console` 再掃 runtime exceptions

6. **錯誤處理**（規則 8-9）
   - step 3-5 任一有 error/failure → 不走 Phase 2 三輪 review，直接派 Codex：
     - `sandbox = "workspace-write"`、SESSION_ID 不變
     - PROMPT：「Unity 測試錯誤：{errors}。請分析並修正，可直接寫入。若改非 target_scripts 檔（規則 9），列出並說明原因。」
   - 若改了 target_scripts 外的檔 → CC 對該檔 `code-review` Mode 2；嚴重問題回送 Codex；不累計 review_round
   - `test_round += 1`
   - `> 10` → step 8（revert）
   - 否則回 step 2

7. **0 錯誤 → Commit**（規則 10）
   ```
   git add <changed files>
   git commit -m "opt(round-{optimization_round}): <Codex 給的一句摘要>

   - Scripts: {target_scripts}
   - Review rounds: {review_round}
   - Test rounds: {test_round}

   Co-Authored-By: Codex <noreply@openai.com>
   Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
   ```
   - 取新 commit hash → 更新 `last_stable_commit`
   - 列印狀態表，進 Phase 4

8. **Revert 流程**（規則 11）
   - `revert_count += 1`
   - `> 3` → **終止**，回報 revert 記錄與最後錯誤
   - 否則：`git reset --hard {last_stable_commit}`、`test_round = 0`、`review_round = 0`、新 Codex session（`codex_session_id = null`），失敗資訊作 Phase 1 addendum，回 Phase 1

---

## Phase 4：第二輪判斷（規則 13-14）

- `optimization_round == 1` → `optimization_round = 2`、重置 `review_round`/`test_round`/`spec_fix_attempts`、新 session（`codex_session_id = null`）；`last_stable_commit` 維持為第二輪 baseline；回 Phase 1
- `optimization_round == 2` → 進 Phase 5

---

## Phase 5：結束回報（規則 15）

**單獨模式**（`batch_mode = false`）：
```
## Auto Code Optimize — 完成 ✅

Target Scripts:
{target_scripts}

Branch:              {current_branch}
Baseline (啟動前):   {baseline_commit}
HEAD (目前):         {last_stable_commit}

### Round 1
- Review rounds: {r1_review}
- Test rounds: {r1_test}
- Commits: {hashes + subjects}

### Round 2
- Review rounds: {r2_review}
- Test rounds: {r2_test}
- Commits: {hashes + subjects}

### 關鍵改動摘要（跨兩輪）
- {bullet 1}
- ...

### 下一步
所有 commit 都在 local，**未 push**。由使用者決定：
  [A] `git push` 推到 remote
  [B] 保留不 push，繼續手動驗證
  [C] 捨棄：`git reset --hard {baseline_commit}`（回啟動前狀態，**新增 commits 全數消失**）
```

**批次模式**（`batch_mode = true`）：簡化版交回 batch skill：
```
## [Batch Group: {batch_group_name}] — SUCCESS ✅

Scripts: {target_scripts}
R1: review={r1_review}, test={r1_test}, commits=[{hashes}]
R2: review={r2_review}, test={r2_test}, commits=[{hashes}]
Group baseline: {baseline_commit}
Group HEAD: {last_stable_commit}
```

**不自動 push / reset**：單獨模式由使用者；批次模式由 batch skill Phase D 統一處理。

---

## 失敗路徑

| # | 條件 | 動作 |
|---|---|---|
| F1 | Phase 0 script/folder/GDD 解析失敗或為空 | 終止 |
| F2 | Phase 0 Workspace 不 clean | 終止，要求使用者處理 |
| F3 | Phase 1 Codex 連續 3 次無法讀 spec | 終止，回報三次嘗試與最後 Codex 回應 |
| F4 | Phase 3 `test_round > 10` | revert 到 `last_stable_commit`，`revert_count++`，回 Phase 1 |
| F5 | Phase 3 `revert_count > 3` | 終止，回報 revert 記錄 |
| F6 | Phase -1 使用者 abort | 終止（無副作用，未啟動任何 git） |

---

## 執行紀律（核心 6 條）

1. **狀態表必更**：每個 Phase 結束更新並列印；使用者可隨時中斷檢視
2. **Codex 紀律**：`cd = repo_root` 永遠固定；`sandbox` 預設 `read-only`，僅 Phase 3 step 1/6 切 `workspace-write`；同輪共用 SESSION_ID，新輪 / revert 後新 session
3. **GDD 對齊不靠記憶**：Phase 2 Part A 必實際 `Read` 相關 GDD
4. **不自動 push / 不自動 branch 切換**：當前 branch 直接 local commit；禁止 `git push` / `git checkout` / `git switch` / `git worktree add`
5. **不降級工具**：禁 `--no-verify` / `--no-gpg-sign`；hook 失敗追根源
6. **語言與時間規範**：CC 對話審查用繁體中文；Codex prompt 可繁中但識別符號保持英文；時間常數遵循全域規則「只用秒/小時」

---

## 術語

| 術語 | 含義 |
|---|---|
| `target_scripts` | 本次優化的 .cs 集合 |
| `spec / 工項需求書` | 描述優化需求的 .md（使用者提供 / 自動生成 / GDD）|
| `baseline_commit` | Phase 0 起始 commit；Phase 5 [C] 回退點 |
| `last_stable_commit` | 最近一次通過 Unity 測試的 commit；Phase 3 revert 回退點 |
| `review_round` | Phase 2 Codex↔CC 完整來回次數 |
| `test_round` | Phase 3 Unity 測試次數 |
| `optimization_round` | 1 或 2，規則 14 強制兩輪 |
