---
name: auto-code-optimize
description: "Run a scoped Unity C# optimization workflow for The Guild. Use when Codex or GPT must plan, implement, review, and verify code improvements for one file, folder, or GDD-linked system while preserving project rules and avoiding unrelated refactors."
---

## Skill Contract

Use this skill when the user invokes $auto-code-optimize or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $auto-code-optimize <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

# Auto Code Optimize

兩個 skill 怎麼選：見 `.claude/docs/auto-code-optimize-usage.md` §兩個 skill 的關係。本 skill 處理單組；多組情境自動轉呼 `auto-code-optimize-batch`。

## 呼叫格式

| 模式 | 範例 |
|---|---|
| 單檔 | `$auto-code-optimize Path/To/File.cs` |
| 多檔（空白／逗號分隔） | `$auto-code-optimize Foo.cs Bar.cs` |
| 資料夾（非遞迴展開該層 .cs） | `$auto-code-optimize Path/To/Folder/` |
| 資料夾遞迴（多子資料夾自動分組） | `$auto-code-optimize Path/ -r` |
| GDD ID（與 `--gdd=` 等價） | `$auto-code-optimize FT-02` |
| 多 GDD（自動轉批次） | `$auto-code-optimize --gdd=FT-02,F-01` |

### Token 解析優先級（每 token，逗號先拆分）

1. `--`/`-` 開頭 → flag
2. `.cs` 結尾 → 單檔
3. 含 `/` `\` 或 `Bash test -d` 為真 → 資料夾
4. 符合 `^[A-Z]+-\d+[a-z]?$` → GDD ID
5. 否則 → 終止「無法解析輸入：`<token>`」

### Flag

| Flag | 說明 |
|---|---|
| `--spec=<path>` | 顯式 spec；省略時依輸入推導（GDD 帶 GDD；其他自動生成 internal spec）|
| `--recursive` / `-r` | 資料夾遞迴展開 |
| `--gdd=<id>[,<id>]` | 從 `design/GDD/[<id>] *.md` 找 spec 與 scripts；多 ID 自動轉批次 |
| `--unattended` | 跳過 Phase -1（給 batch 內部呼叫或預授權離線情境用）|
| `--batch-group-name=<name>` | （內部用）batch wrapper 注入；標記為批次模式 |

## 11 條優化原則（審查標準）

1. **不回退**：先前 commit 已刪除的實作不得加回
2. **風格一致**：命名／縮排／大括號順從既有檔案 + `.claude/rules/gameplay-code.md`
3. **可讀性**：表意命名、扁平流程、適度斷句
4. **重複邏輯函式化**：相似邏輯 ≥ 2 處抽共用
5. **職責清晰**：類別／方法 SRP
6. **簡潔度**：移除不必要抽象、未使用欄位、死代碼
7. **效能**：熱路徑無 alloc、避免 `Find()`／`FindObjectOfType()`、`Awake()` 快取引用
8. **可靠性**：null 檢查、狀態一致、`OnEnable`／`OnDisable` 對稱
9. **參數表格化**：閾值／常數／公式來自 CSV 或 ScriptableObject
10. **冪等性**：重複呼叫結果一致、可安全重試
11. **預測 Try-Catch**：邊界（I/O、解析、外部 API）catch + log；熱路徑禁吞例外

## 流程

```
Phase -1 互動確認（unattended 跳過）
   ↓
Phase 0 解析輸入 + Workspace Check
   ↓ 多組 → 寫 inline manifest → 呼叫 -batch（控制權移交）
   ↓ 單組
Phase 1 需求準備 → Phase 2 Review-Fix（≥3 輪 + APPROVED）→ Phase 3 Unity 測試 → 進 Phase 4
Phase 4 第二輪？ → 是回 Phase 1；否進 Phase 5
Phase 5 結束回報（不自動 push）
```

每個 Phase 結束更新並列印狀態表。

### 狀態追蹤欄位

| 欄位 | 預設 | 上限 |
|---|---|---|
| `optimization_round` | 1 | 2 |
| `review_round` | 0 | ≥3 required |
| `test_round` | 0 | 10 max |
| `revert_count` | 0 | 3 max |
| `spec_fix_attempts` | 0 | 3 max |
| `codex_session_id` | null | — |
| `baseline_commit` / `last_stable_commit` | — | — |
| `repo_root` / `target_scripts` / `spec_path` | — | — |

## Phase -1：互動確認

**跳過條件**：`--unattended` 或注入 `--batch-group-name=<name>`。

1. **Dry run 解析**（Read／Glob／`test -d`，不執行 git）
   - 套 §Token 解析優先級
   - 展開資料夾（依 `-r`）
   - 解析 GDD ID（Glob `design/GDD/[<id>]*.md` + Read + 抽 scripts，見 §GDD scripts 抽取）
   - 計算 groups：多 GDD ／ 多獨立資料夾 ／ `-r` 跨多直接子資料夾 → 多組；否則單組

2. **列印確認區塊**
   ```
   ## $auto-code-optimize — 啟動前確認

   解析模式：{單組 | 批次 N 組}
   {單組: target_scripts (M 支) + spec 路徑}
   {批次: groups (N 組) + 每組 preview}

   工具預期會碰到：
     - Codex 寫入：<target scripts parent dirs>
     - 暫存（已預授權）：.claude/optimization-work/  {批次另含 .progress/ .reports/}
     - Git：commit / reset --hard（不 push）
     - Unity MCP：refresh / read_console / run_tests / manage_editor

   無人值守建議：
     [A] Shift+Tab 切 auto-accept-edits（推薦）
     [B] 暫存目錄已預授權；Codex 不觸發 prompt（預期不被打斷）
     [C] --dangerously-skip-permissions（最不安全，不建議）

   準備好輸入空訊息或 "go" 繼續；中止 Ctrl-C。
   ```

3. **等待 ack**：空訊息／"go"／"ok"／"continue" → Phase 0；"abort"／"cancel"／"q" → 終止。

## Phase 0：解析 + Workspace Check

1. **完整解析參數**：collect tokens_files／tokens_folders／tokens_gdd_ids／flag_*
2. **展開資料夾**：`Glob <folder>*.cs` 或遞迴 `<folder>**/*.cs`，排除 `AssemblyInfo.cs`，空 → 終止
3. **解析 GDD ID**：見 §GDD scripts 抽取
4. **構建 groups**：多 GDD（≥2）／多獨立資料夾／`-r` 跨多子資料夾 → 多組；否則單組
5. **多組分派**（若 `len(groups) > 1`）：
   - 寫 inline manifest 到 `<repo_root>/.claude/optimization-work/_inline-<YYYYMMDD-HHMMSS>.md`
     - frontmatter：`manifest: inline-<ts>`、`stop_on_fail: false`、`group_order: [<names>]`
     - 每組 `## <name>` section（`scripts:` ／可選 `spec:` `notes:`）
   - `Skill` 工具呼叫 `auto-code-optimize-batch`，args = `--manifest=<file> --unattended`
   - batch 完成後刪除 inline manifest 檔；本 skill 結束
6. **單組情境**（`len(groups) == 1`）：解構為 `target_scripts` ／ `spec_path` ／ `notes`
7. **讀規範**：Read `CLAUDE.md` ／ `.claude/rules/gameplay-code.md` ／（含 UI 才讀）`.claude/rules/ui-code.md` ／ `.claude/docs/coding-standards.md`
8. **Workspace Check**：
   - `git rev-parse --show-toplevel` → `repo_root`
   - `git status --porcelain -- . ':!.claude/optimization-work' ':!.claude/batch-manifests/.progress' ':!.claude/batch-manifests/.reports'` 非空 → **終止**並列出
   - `git rev-parse --abbrev-ref HEAD` → `current_branch`，非 main 警告但繼續
   - `mkdir -p .claude/optimization-work`
   - `baseline_commit = last_stable_commit = git rev-parse HEAD`
   - 若 `flag_batch_group_name` 非 null → `batch_mode = true`
9. **若有 notes**：寫 `.claude/optimization-work/optimization_addendum_r1.md`
10. **初始化狀態表**並列印 → 進 Phase 1

### GDD scripts 抽取

從 GDD .md 抽 scripts 路徑，依序試：

1. frontmatter `scripts:` 陣列
2. 標題含「實作檔案」／「Scripts」／「Implementation Files」／「目標檔案」的 section 內 ` `<path>.cs` ` inline code
3. 全文搜 `TheGuild-unity/Assets/Scripts/.*\.cs` code reference
4. 去重 + Read 驗證存在
5. 空 → 終止「GDD `<id>` 找不到 scripts 引用，請以 `--spec=<gdd>` 配明確 scripts 路徑呼叫」

## Phase 1：需求準備

1. **CC 寫／補 spec**：
   - `spec_path` 非 null → Read 後比對現狀，缺漏項補 addendum 寫到 `optimization_addendum_r{round}.md`
   - null → 生成 internal spec 寫到 `optimization_spec_r{round}.md`，含 `## 目標檔案`／`## 優化目標`／`## 現狀問題點`／`## 不得回退的先前決策`／`## Acceptance Criteria`
   - `batch_mode` 且 `batch-notes-<group>.md` 存在 → 讀入合併到 `## Batch Group Notes`

2. **發送 spec 給 Codex**（首次 dispatch）
   - `Codex implementation capability when available`：`cd=repo_root`、`sandbox="read-only"`、`SESSION_ID=null`、`return_all_messages=true`
   - PROMPT 要求 Codex (a) 讀 spec 與 target scripts (b) 回需求摘要 + 實作綱要 (c) 列疑點。**先不要實作**
   - 記錄返回 `session_id` 為 `codex_session_id`

3. **驗證 Codex 讀取**：摘要 + 綱要與 spec 一致 → Phase 2；偏離或讀不到 → `spec_fix_attempts++`，依次嘗試（絕對路徑／簡化 markdown／spec 全文嵌入 PROMPT），>3 → 終止

## Phase 2：Review-Fix 迴圈

**進 Phase 3 條件**：`review_round ≥ 3` 且最終 Verdict = APPROVED。

**輪次聚焦**：
- 輪 1：通用結構（職責、風格、可讀性、簡潔度）
- 輪 2：重複邏輯函式化、參數表格化、保護已優化方案
- 輪 3：效能、可靠性、冪等性、預測 Try-Catch

每輪：

1. **Codex 實作／修正**：`sandbox="read-only"`、`SESSION_ID=codex_session_id`
   - PROMPT 要 Codex (a) 實作或依 Required Changes 修正 (b) self-review 11 條 (c) 回 unified diff + self-review 結論 + 仍未處理疑點。**禁止寫入**

2. **驗證回傳格式**：缺 diff／self-review → 同 session 追問 1 次；仍缺視為本輪失敗，下輪補

3. **CC 雙重 review**
   - **Part A 需求／GDD 對齊**：Read `spec_path` 與相關 GDD，對照 diff 檢查偏離／AC／GDD 行為
   - **Part B 程式碼審查**：呼叫 `code-review` skill (Mode 1) + 額外查本輪聚焦項

4. **產出審查結論**（含本輪聚焦／需求對齊／11 原則檢查／Required Changes／Positive Observations／Verdict）

5. **狀態更新**：`review_round++`
   - `review_round ≥ 3` 且 APPROVED → Phase 3
   - APPROVED 但 < 3 → 「本聚焦項通過，進入下一聚焦項目」作下輪 prompt
   - CHANGES REQUIRED → Required Changes 作下輪回饋
   - 列印狀態表

## Phase 3：Unity MCP 測試迴圈

1. **Codex 寫入**：`sandbox="workspace-write"`、`SESSION_ID` 不變
   - PROMPT：「將上輪 APPROVED 的 diff 寫入對應檔案，回報已寫清單。」
   - 驗證 `success=true` 且 `git diff --stat` 有變更

2. **Unity 編譯**：`mcp__UnityMCP__refresh_unity` → 輪詢 `editor_state.isCompiling==false`

3. **編譯錯誤檢查**：`read_console` 過濾 error → 有則跳 6

4. **測試**：`run_tests`（EditMode + PlayMode），解析 pass／fail 與失敗 test 名

5. **runtime 錯誤掃描**：`read_console` 掃 runtime exceptions

6. **錯誤處理**（步驟 3-5 任一失敗）：
   - 直接派 Codex（不走 Phase 2 三輪 review）：`sandbox="workspace-write"`、SESSION_ID 不變
     - PROMPT：「Unity 測試錯誤：{errors}。分析並修正可直接寫入。改非 target_scripts 檔須列出說明。」
   - 改了 target 外檔 → CC 對該檔 `code-review` Mode 2，嚴重問題回送 Codex；不累計 `review_round`
   - `test_round++`，>10 → 跳步驟 8 revert
   - 否則回步驟 2

7. **0 錯誤 → Commit**：
   ```
   git commit -m "opt(round-{round}): <Codex 一句繁中摘要>

   - 目標檔案: {target_scripts}
   - 審查輪數: {review_round}
   - 測試輪數: {test_round}

   Co-Authored-By: Codex <noreply@openai.com>
   Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
   ```
   - 更新 `last_stable_commit`，列印狀態表 → Phase 4

8. **Revert**：`revert_count++`，>3 → 終止；否則 `git reset --hard {last_stable_commit}`、`test_round=0`、`review_round=0`、`codex_session_id=null`、失敗資訊作 addendum，回 Phase 1

## Phase 4：第二輪判斷

- `optimization_round == 1` → 進第二輪：`optimization_round=2`、重置 `review_round`／`test_round`／`spec_fix_attempts`、新 session、`last_stable_commit` 維持為第二輪 baseline，回 Phase 1
- `optimization_round == 2` → Phase 5

## Phase 5：結束回報

**不自動 push／reset**。

**單獨模式**輸出（含 target_scripts／branch／baseline／HEAD／Round 1+2 review/test rounds 與 commits／關鍵改動摘要／使用者下一步 [A push] [B 保留] [C `git reset --hard {baseline_commit}` 捨棄整批]）。

**批次模式**簡化版交回 batch skill：
```
## [Batch Group: {name}] — SUCCESS ✅
Scripts: {target_scripts}
R1: review={r1_review}, test={r1_test}, commits=[{hashes}]
R2: review={r2_review}, test={r2_test}, commits=[{hashes}]
Group baseline: {baseline_commit}
Group HEAD: {last_stable_commit}
```

## 失敗路徑

| # | 條件 | 動作 |
|---|---|---|
| F1 | Phase 0 解析失敗或為空 | 終止 |
| F2 | Phase 0 working tree 不 clean | 終止，要求使用者處理 |
| F3 | Phase 1 spec_fix_attempts > 3 | 終止，回報三次嘗試與最後 Codex 回應 |
| F4 | Phase 3 test_round > 10 | revert 到 last_stable_commit，`revert_count++`，回 Phase 1 |
| F5 | Phase 3 revert_count > 3 | 終止，回報 revert 記錄 |
| F6 | Phase -1 使用者 abort | 終止（無副作用）|

## 執行紀律

- **Codex**：`cd=repo_root` 永遠固定；`sandbox` 預設 `read-only`，僅 Phase 3 step 1/6 切 `workspace-write`；同輪共用 SESSION_ID，新輪／revert 後新 session
- **GDD 對齊不靠記憶**：Phase 2 Part A 必實際 Read 相關 GDD
- **不自動 push／不切 branch**：當前 branch 直接 local commit；禁 `git push` ／ `checkout` ／ `switch` ／ `worktree add`
- **不降級工具**：禁 `--no-verify` ／ `--no-gpg-sign`，hook 失敗追根源
- **狀態表必更**：每 Phase 結束列印
- **語言／時間**：CC 對話審查繁中；Codex prompt 可繁中但識別符號保英文；時間單位「秒／小時」



