---
name: program-ipm
description: "全自動 FSD 實作入口（IPM = Implementation by FSD）。輸入一個或多個 FSD 代號（逗號／空白分隔，支援 ~ 區間），對每份 FSD 依序執行：ImplementationGuide 閘門檢查 → 工項分級 → Codex／subagent 實作 → 自檢 → /code-review 閘門 → 進度登記 → commit。單一 session 嚴格序列，不並行。除 CC 無法解決或 GDD/FSD 邏輯衝突外，全程不需要使用者介入。"
argument-hint: "<FSD-id>[,<another>...] — 例：'/program-ipm FT-01-FSD' 或 '/program-ipm FT-01-FSD, C-01-FSD~C-04-FSD'"
user-invocable: true
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, Skill, Agent, mcp__codex__codex, mcp__UnityMCP__refresh_unity, mcp__UnityMCP__read_console, mcp__UnityMCP__run_tests, mcp__UnityMCP__manage_editor
---

# /program-ipm — FSD 實作入口

全自動 skill：解析 FSD 清單後依序實作。**僅在以下情況中斷並等待使用者**：
- Pre-flight 模型／思考強度／working tree 不符
- ImplementationGuide.md §4 動工前 Checklist 任一項未通過
- GDD／FSD 內部或跨系統規則出現重大邏輯衝突（FSD-index §2.5）
- Codex／subagent 來回上限耗盡仍未通過 /code-review
- 自檢錯誤經分級討論上限後仍無法收斂

其餘情況一律自動執行；不做組間 checkpoint，不做事前 dry-run 互動確認。

---

## 0. 規範來源（不重複，僅引用）

| 主題 | 來源 |
| --- | --- |
| 動工前 Checklist／系統實作順序／進度表 | `design/ImplementationGuide.md` |
| 工項分級（Large／Medium／Small）、Codex MCP 規則 | `~/.claude/CLAUDE.md` §Codex MCP 協作規範 |
| Service 契約速查（concrete singleton） | `design/FSD/FSD-index.md` §2.10／`design/ImplementationGuide.md` §3 |
| 程式實作原則（11 條） | `design/FSD/FSD-index.md` §四 |
| 衝突處理流程 | `design/FSD/FSD-index.md` §2.5 |
| Codex 程式碼／審查標準 | `code-review` skill |
| Codex MCP `/model` 開場指令 | memory `reference_codex_model_commands.md` |

本 skill 僅補充：(A) Pre-flight；(B) FSD 清單解析；(C) 全自動執行流；(D) 問題分級討論上限；(E) MCP 開場規則。

---

## 1. Pre-flight（任一不通過立即終止）

### 1.1 模型檢查

當前模型必須為 Opus（model id 含 `opus`）。

不符 → 立即終止：
```
[program-ipm 終止] 需要 Opus 模型。
當前模型：<current-model-id>
請執行 /model claude-opus-4-7 後重新執行 /program-ipm。
```

### 1.2 思考強度檢查

當前 extended thinking 必須為 **xhigh 或 max**。

不符 → 立即終止：
```
[program-ipm 終止] 需要 xhigh 或 max 思考強度。
請執行 /think xhigh（或 /think max）後重新執行 /program-ipm。
```

### 1.3 Working tree 檢查

```
git status --porcelain -- . ':!.claude/optimization-work' ':!.claude/batch-manifests/.progress' ':!.claude/batch-manifests/.reports'
```

非空 → 終止並列出未提交檔案，要求使用者處理。

### 1.4 分支檢查

`git rev-parse --abbrev-ref HEAD`，非 `main` 印出警告但繼續。**全程不切 branch、不 push、不 worktree**（commit 留在當前 branch）。

### 1.5 必讀規範

讀以下檔案（每份僅讀一次）：

- `design/ImplementationGuide.md`（**必讀**：§4 Checklist／§5 順序／§6 進度表）
- `design/FSD/FSD-index.md` §2.5、§2.10、§四
- `~/.claude/CLAUDE.md`（驗證工項分級規則）
- `.claude/rules/gameplay-code.md`
- 含 UI 才讀 `.claude/rules/ui-code.md`
- `.claude/docs/coding-standards.md`

---

## 2. 解析輸入（FSD 清單）

### 2.1 切分

以逗號 `,` 與任意空白切分 args；空輸入 → 終止：

```
Usage: /program-ipm <FSD-id>[,<another>...]
範例：
  /program-ipm FT-01-FSD
  /program-ipm FT-01-FSD, C-01-FSD~C-04-FSD
  /program-ipm F-01-FSD F-02-FSD F-03-FSD
```

### 2.2 Token 規則

| Token 形式 | 意義 |
| --- | --- |
| `^[A-Z]+-\d+-FSD$` | 單一未拆分 FSD（例：`F-01-FSD`、`FT-01-FSD`） |
| `^[A-Z]+-\d+-FSD-[A-Z]$` | 拆分後子 FSD（例：`FT-02-FSD-A`） |
| `<id>~<id>` | 區間：兩端必須同前綴（`F-`／`C-`／`FT-`），編號連續展開（例：`C-01-FSD~C-04-FSD` → `C-01-FSD,C-02-FSD,C-03-FSD,C-04-FSD`） |
| 其他 | 終止「無法解析輸入：`<token>`」 |

### 2.3 FSD 解析

對每個 ID 執行 `Glob design/FSD/【<id>】*.md`：

- 命中 0 筆 → 終止「FSD 不存在：`<id>`」
- 命中 1 筆 → 加入清單
- 命中多筆（拆分系統使用基底 ID 時，例 `FT-02-FSD` 會命中 A／B）→ **依字母順序**全部加入清單（A 先於 B）

### 2.4 順序保留

依使用者輸入順序處理；同一 token 展開後保留展開順序。**禁止重排**、**禁止並行**。

### 2.5 系統實作順序檢查（軟警告）

對照 `design/ImplementationGuide.md` §5（特別是 §5.2／§5.3 上游列），若清單順序與依賴順序衝突：

- 列出衝突點與建議順序
- **不自動重排**；保留使用者順序但記入摘要備註

例：使用者輸入 `FT-02-FSD-A, C-01-FSD` → 警告「C-01 為 FT-02-A 上游，建議先 C-01 再 FT-02-A」，但仍依使用者順序執行。

---

## 3. 主流程（每份 FSD 依序執行 §3.1～§3.8）

完成一份 FSD 全部步驟後才處理下一份。前一份失敗時依 §6 終止條件決定是否中斷整體 skill。

### 3.1 ImplementationGuide §4 Checklist 閘門

逐項驗證，未通過 → 終止此 FSD 並記入摘要，**不嘗試自動修補設計層**：

**§4.1 設計層**
- FSD `§0 狀態` = `已完成`
- FSD `§7.1` 無 `[YYYY-MM-DD 排查]` blocker（或已落地）
- FSD §2.3 上游系統皆已實作（對 `Assets/Scripts/` Glob 驗證；交叉比對 ImplementationGuide §6 進度表狀態 = `已完成`）
- FSD §6.1 引用的 Data-Specs 全部存在於 `design/Data-Specs/`

**§4.2 資料層**
- FSD §6.1 列出的 CSV 全部存在於 `Assets/Resources/Data/Tables/`
- CSV 時間欄位以 `_sec` / `_hours` 命名（Grep header 驗證）
- FSD §2.2 / §6.1 / Data-Specs 三處欄位雙向對齊（抽查 Data-Specs 第一份做欄位 diff）

任一未通過 → 列出未通過項與檢查路徑後終止此 FSD：
```
[program-ipm 中止 <FSD-id>] §4 Checklist 未通過：
  - <未通過項 1>：<證據>
  - <未通過項 2>：<證據>
請補完後重新執行 /program-ipm <FSD-id>。
```

### 3.2 工項分級

依 `~/.claude/CLAUDE.md` §Codex MCP 協作規範 §何時用 Codex（工項分級）判定：

| 分級 | 條件（簡述） | 實作角色 | 審查角色 | 來回上限 |
| --- | --- | --- | --- | --- |
| Large | 跨系統、核心架構、新系統實作 | Codex | Opus + xhigh／max | 4 |
| Medium | 單一類別／系統組件 | Codex | Opus + xhigh／max | 3 |
| Small | 改 config／修 typo／加欄位 | Sonnet + high subagent 閉環 | Opus + xhigh／max | 3 |

判斷依據：FSD §4.4 Script 清單規模／預估行數／是否觸及多個系統目錄。

分級結果記入本次 FSD 狀態並印出。

### 3.3 實作（依分級分流）

#### 3.3.1 Large／Medium 流程

```
Codex 實作（read-only）
  → CC 審查（/code-review Mode 1）
  → APPROVED → Codex 寫入（workspace-write）
  → CHANGES → 同 session 修正 → 再審查
  → 上限耗盡仍未 APPROVED → 終止此 FSD（見 §6）
```

**Codex 開場規則（首次 dispatch）**：

- 工具：`mcp__codex__codex`
- 必傳：`cd=<repo_root>`、`sandbox="read-only"`、`SESSION_ID=null`、`return_all_messages=true`
- **PROMPT 首行必為**：`/model gpt-5.3-codex high`（依 memory `reference_codex_model_commands.md`）
- PROMPT 內容（首行之後）：
  - (a) 必讀清單：本 FSD 全文、ImplementationGuide §3 Service 契約、相關 GDD（FSD §2.1 引用章節）、相關 CSV 表頭
  - (b) 任務：依 FSD §4.4 Script 清單實作；遵循 FSD-index §四 11 條原則；service 一律 concrete singleton（見 ImplementationGuide §3）
  - (c) 回報：unified diff + self-review（11 條對照） + 仍待裁決疑點
  - (d) **禁止寫入**（read-only）

**同 session 後續輪次**：傳入相同 `SESSION_ID`、`sandbox` 維持 `read-only`、PROMPT **不再重複** `/model` 指令；只傳本輪 Required Changes。

**CC 審查**：呼叫 `Skill: code-review`（Mode 1：Codex Diff Patch）。本 skill 主體必須是 Opus + xhigh／max；若派 subagent 審查，Agent tool 傳 `model: "opus"` 並於 prompt 首句聲明「以 xhigh 思考強度進行審查」。

**APPROVED 寫入**：同 session、`sandbox="workspace-write"`、PROMPT 為「將上輪 APPROVED 的 diff 寫入；回報已寫清單」。驗證 `success=true` 且 `git diff --stat` 非空。

#### 3.3.2 Small 流程

派 subagent 全權閉環（Sonnet + high）：

- Agent tool：`subagent_type="gameplay-programmer"`（純 C#）／`unity-ui-specialist`（含 UI）／`unity-specialist`（純 Unity 設定）
- 傳 `model: "sonnet"`
- prompt 首句：「以 high effort 進行實作；完成後自行 self-review，再交主體審查。」
- prompt 內容：FSD 路徑、Script 清單、實作原則 11 條、寫入授權

完成後本 skill 主體（Opus + xhigh／max）執行 `/code-review` Mode 2 審查實際檔案。CHANGES → 同 subagent 修；上限 3 次。

#### 3.3.3 替代 MCP（gpt-mcp）

若 codex-mcp 不可用或使用者指定 `gpt-mcp`，於首次 dispatch 之 PROMPT 首行改為：

```
/model gpt-5.5 high
```

其餘流程相同。預設使用 codex-mcp。

### 3.4 自檢（Unity MCP 測試閘）

寫入完成後執行：

1. `mcp__UnityMCP__refresh_unity` → 輪詢 `editor_state.isCompiling == false`
2. `mcp__UnityMCP__read_console` 過濾 error
3. `mcp__UnityMCP__run_tests`（先 EditMode 後 PlayMode），解析失敗清單
4. `mcp__UnityMCP__read_console` 二次掃描 runtime exception

**0 錯誤** → 進 §3.5。
**有錯誤** → 進 §3.5.1 自檢錯誤分級處理。

### 3.5 /code-review 閘門

通過 §3.4 自檢後，再次呼叫 `Skill: code-review` Mode 2，對實際寫入的檔案做最終審查（不同於 §3.3 的 diff 審查）。

- APPROVED → 進 §3.6
- CHANGES REQUIRED → 回 §3.3 派 Codex／subagent 修正；不重置自檢輪次計數，但累計於該 FSD 來回上限

#### 3.5.1 自檢錯誤分級處理（§3.4 失敗時）

依 §4 問題分級規則決斷。修正完成後：

1. 派 Codex／subagent 寫入修正
2. 回 §3.4 重跑自檢
3. **不論修正幅度，CC 必須對修正內容再 review 一次**（§3.5 流程不可省略）

### 3.6 進度登記

通過 §3.5 後，更新 `design/ImplementationGuide.md` §6 進度表對應列：

- `狀態`：`已完成`
- `開始`：本次 FSD 進入 §3.3 的日期（YYYY-MM-DD）
- `完成`：今日（YYYY-MM-DD）
- `備註`：保留原備註，必要時追加「<FSD-id> commit `<hash>`」

### 3.7 Commit

```
git add <實際變更檔案>
git commit -m "$(cat <<'EOF'
feat(<系統ID>): 實作 <FSD-id> <一句繁體中文摘要>

- FSD: design/FSD/【<FSD-id>】<name>.md
- 工項分級: <Large/Medium/Small>
- 審查輪數: <n>
- 自檢輪數: <n>
- 進度表: 已登記

Co-Authored-By: Codex <noreply@openai.com>
Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

注意：
- subject／description 一律繁中（CLAUDE.md §語言與單位規範）
- 技術 prefix（`feat:`／`fix:`）與 `Co-Authored-By` trailer 保持英文
- Small 工項由 subagent 主導時，Co-Authored-By 改填 subagent 角色名（例：`gameplay-programmer subagent`）
- **不 push**（在當前 branch 留 local commit；使用者結束後自行決定推送）

### 3.8 印出單份 FSD 狀態表

```
[program-ipm <FSD-id>] 完成 ✅
  - 工項分級: <Large/Medium/Small>
  - 來回輪數: review=<n>, self-check=<n>
  - 變更檔案: <list>
  - Commit: <hash>
  - 進度表: §6 已登記
```

完成後進入下一份 FSD。

---

## 4. 問題分級規則（自動執行 vs 討論輪數）

下列規則同時適用於 §3.3（實作疑點）與 §3.5.1（自檢錯誤）。**僅 LLM 對 LLM 的內部討論**，不打斷使用者。

| 問題等級 | 判斷條件 | 處理 |
| --- | --- | --- |
| **一般** | 命名、樣式、單一檔案邏輯小錯、明確規範可循 | CC 或 codex／gpt 自行決斷，直接修正不討論 |
| **中等** | 跨檔案影響、需在兩種合理方案間取捨、行為改變但範圍可控 | CC ↔ Codex／gpt 討論 **1～3 輪**達成共識後決斷 |
| **大型／嚴重** | 影響系統契約、上下游事件、Service 行為、效能熱路徑、資料結構變更 | CC ↔ Codex／gpt 討論 **2～5 輪**後決斷 |

**討論一輪定義**：CC 提出一個替代／質疑 → Codex 回應一次。

**達成共識條件**：
- CC 與 Codex 對方案敘述無新異議；或
- CC（Opus + xhigh／max）做最終裁決並紀錄理由

**討論上限耗盡仍未收斂**：
- 中等問題上限 3 輪未收斂 → 升級為大型問題重啟（最多再 5 輪）
- 大型問題 5 輪未收斂 → 進 §6.2 暫停並請使用者裁決

**修正後 review**：依使用者規則，所有自檢錯誤的修正內容必須由 CC review 一次（§3.5），即使分級為「一般」也不可省略。

---

## 5. 多 FSD 順序紀律

- **單一 session 嚴格序列**：禁止為了加速而並行派多個 Codex／subagent
- **未通過 /code-review 不進下一份**：當前 FSD 未過 §3.5 → 整體 skill 暫停或終止；**禁止**跳過繼續下一份
- **狀態隔離**：每份 FSD 重置 `review_round`／`self_check_round`／`codex_session_id`／`baseline_commit`；上一份失敗不污染下一份
- **失敗策略**：當前 FSD 失敗 → 依 §6 規則決定整體 skill 是否終止；預設「失敗即停」，不繼續後續 FSD

---

## 6. 終止條件與使用者介入點

僅以下情境呼叫使用者；其餘自動處理。

### 6.1 Pre-flight 失敗
模型／思考強度／working tree 不符 → §1 終止訊息。

### 6.2 GDD／FSD 邏輯衝突
FSD-index §2.5 流程：暫停撰寫實作，整理衝突點、查詢結果、建議解法後輸出：

```
[program-ipm 衝突暫停] FSD: <FSD-id>
衝突點: <涉及章節與條目>
查詢結果: <GDD §5/§6/§1 是否有指引>
建議解法: <方案 A／B>
請裁決後回覆以繼續。
```

裁決後回 §3.3 對應步驟；於 FSD §8.5 登記。

### 6.3 來回上限耗盡

- Large Codex 4 輪未 APPROVED
- Medium Codex 3 輪未 APPROVED
- Small subagent 3 輪未 APPROVED
- 大型問題討論 5 輪未收斂

→ 終止當前 FSD：

```
[program-ipm 上限耗盡 <FSD-id>] 工項=<Large/Medium/Small>
最後一輪 Required Changes：
  <list>
最後一輪 Codex/subagent 回應摘要：
  <summary>
請使用者裁決後續處理（介入修正／調整 FSD／放棄此項）。
```

### 6.4 Unity MCP 不可用
任一 Unity MCP 工具連續失敗 3 次 → 終止當前 FSD 並回報。

### 6.5 整體 skill 終止
任一 FSD 進入 6.1～6.4，整體 skill 停在當前 FSD，**不**繼續後續 FSD。已完成的 FSD（含 commit 與 §6 進度表登記）保留。

---

## 7. 收尾輸出

所有 FSD 處理完畢後（或在 §6.5 提前終止後），輸出：

```
program-ipm 結束

輸入清單: <原始 args>
解析後: <展開的 FSD 清單>

完成: <count>
  - <FSD-id>：commit <hash>，分級 <L/M/S>，來回 review=<n> self-check=<n>
  - ...

跳過（§3.1 閘門未過）: <list（含原因）>
衝突暫停: <list>
上限耗盡: <list>
未啟動: <list>（§6.5 後續未執行的 FSD）

ImplementationGuide.md §6：已更新 <count> 列
分支: <branch> | HEAD: <hash> | 未 push（請使用者自行決定）
```

---

## 8. 執行紀律

- **Codex `cd` 永遠固定為 repo root**，否則靜默失敗
- **Codex sandbox 預設 read-only**，僅在 §3.3 寫入步驟與 §3.5.1 修正寫入步驟切 workspace-write；用完不回退（同輪用完即丟棄 session）
- **同 FSD 同 session**；新 FSD／revert 必新 session
- **不降級工具**：禁 `--no-verify`／`--no-gpg-sign`，hook 失敗追根源
- **不切 branch、不 push、不 worktree**
- **GDD 對齊不靠記憶**：§3.3 / §3.5 必實際 Read FSD 與相關 GDD
- **狀態表必更**：每份 FSD 結束（§3.8）必印；整體結束（§7）必印
- **語言／時間單位**：CC 對話與 commit 訊息繁中；Codex prompt 可繁中但識別符號保英文；CSV 時間欄位限「秒／小時」
- **memory 約束**：本 skill 不寫 memory；使用者要求時才寫
