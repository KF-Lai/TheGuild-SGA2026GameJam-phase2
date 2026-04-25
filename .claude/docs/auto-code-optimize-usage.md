# Auto Code Optimize — 使用說明

兩輪自動化 AI pair 優化流程。Claude Code 做系統審查、Codex 做程式實作，
透過 review-fix 迴圈與 Unity MCP 測試閘門驗證後 commit。

涵蓋兩個 slash commands：
- `/auto-code-optimize` — 單組（或自動轉批次）的智能主入口
- `/auto-code-optimize-batch` — 批次引擎（manifest / inline 雙模式）

---

## 啟動前 30 秒檢查

- 當前在 `main`（或你接受的 feature branch；非 main 會警告但繼續）
- `git status` 是 clean（`.claude/optimization-work/` 與 `.claude/batch-manifests/.progress|.reports/` 已被自動排除；其他未提交變更會擋下流程）
- 想無人值守跑一夜 → 按 `Shift+Tab` 切到 auto-accept-edits

---

## /auto-code-optimize — 4 種輸入模式

### 1. 直接列檔

```
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs
/auto-code-optimize Foo.cs Bar.cs
/auto-code-optimize Foo.cs,Bar.cs        # 逗號分隔也行
```

### 2. 資料夾（非遞迴；只展開該層 .cs）

```
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/
```

自動排除 `AssemblyInfo.cs`；展開後 > 4 支會印警告但繼續執行。

### 3. 資料夾遞迴（自動依子資料夾分組轉批次）

```
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ --recursive
```

例：`Core/` 下有 `Time/`、`Data/`、`Events/` 三個子資料夾 → 自動分成 3 組，內部呼叫 batch。

### 4. GDD 導入

```
/auto-code-optimize --gdd=FT-02                   # 單 GDD = 單組
/auto-code-optimize --gdd=FT-02,F-01,P-02         # 多 GDD = 自動批次
/auto-code-optimize FT-02 F-01                    # 純 ID 等價於 --gdd=
```

從 GDD 抽 scripts 的順序（找到就用）：
1. frontmatter 的 `scripts:` 陣列
2. 標題包含「Scripts」「實作檔案」「Implementation Files」「目標檔案」的 section 內 inline code
3. 全文 `TheGuild-unity/Assets/Scripts/.*\.cs` 的 code reference

### Flag

| Flag | 說明 |
|---|---|
| `--spec=<path>` | 顯式指定 spec（覆寫 GDD 自動帶入或自動生成）|
| `--recursive` / `-r` | 資料夾遞迴展開 |
| `--gdd=<id>[,<id>]` | 從 GDD 自動找 scripts |
| `--unattended` | 跳過 Phase -1 互動確認 |

---

## /auto-code-optimize-batch — 何時用

通常 `/auto-code-optimize` 自動處理多組情境就夠。以下情境直接用 batch：

### A. Manifest 檔（可 commit 版本化）

```
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md
/auto-code-optimize-batch --manifest=<path> --resume          # 續跑失敗 / 未列出
/auto-code-optimize-batch --manifest=<path> --only=time,data  # 只跑指定組
```

範本：`.claude/batch-manifests/_template.md`。

Manifest 結構：

```markdown
---
manifest: <name>
stop_on_fail: false
group_order: [a, b, c]
auto_group_from_folders:    # 可選；新組追加到 group_order 尾端
  - <parent_folder>
---

## a
- scripts:
  - <path1.cs>
  - <folder>/             # 非遞迴展開該層 .cs，自動排除 AssemblyInfo.cs
- spec: <path>            # 可選
- notes: <str>            # 可選；作為 Phase 1 addendum
- skip: false             # 可選
```

### B. Inline flag（無需 .md）

```
# 自動以子資料夾分組
/auto-code-optimize-batch --auto=TheGuild-unity/Assets/Scripts/Core/

# 子資料夾下遞迴展開
/auto-code-optimize-batch --auto=TheGuild-unity/Assets/Scripts/Core/ --recursive

# 每個 GDD 一組
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02

# 顯式 scripts（單組大批量）
/auto-code-optimize-batch --scripts=Foo.cs,Bar.cs

# 混合 + 命名
/auto-code-optimize-batch --auto=Core/ --gdd=FT-08 --name=mixed-2026-04-25
```

| Flag | 說明 |
|---|---|
| `--name=<batch>` | 影響 progress / report 檔名；省略 = `inline-<YYYYMMDD-HHMM>` |
| `--resume` / `--only=<g>[,<g>]` | 兩模式都支援 |
| `--unattended` | 跳過 Phase -1 |

---

## 兩個 skill 怎麼選

| 情境 | 用哪個 |
|---|---|
| 一鍵啟動，混合任意輸入 | `/auto-code-optimize`（多組自動轉批次）|
| 明確要批次、無 manifest 檔 | `/auto-code-optimize-batch --auto=` / `--gdd=` / `--scripts=` |
| 寫死配方、可 commit 版本化 | `/auto-code-optimize-batch --manifest=<.md>` |
| 中斷後續跑 / 只跑特定組 | `/auto-code-optimize-batch --resume` 或 `--only=` |

簡單原則：除非需要可版本化的批次配方，否則用 `/auto-code-optimize` 就夠。

---

## 流程概觀

### 單組（auto-code-optimize）

```
Phase -1 互動確認（--unattended 跳過）
  ↓
Phase 0 解析 + Workspace check + 記 baseline
  ↓
Phase 1 需求準備（CC 寫 spec / addendum）
  ↓
Phase 2 Review-Fix 迴圈（≥ 3 輪 + APPROVED）
  輪 1：通用結構（職責、風格、可讀性、簡潔度）
  輪 2：重複邏輯函式化、參數表格化、保護已優化方案
  輪 3：效能、可靠性、冪等性、預測 Try-Catch
  ↓
Phase 3 Unity MCP 測試（refresh → console → tests → commit）
  ↓
Phase 4 第一輪完 → 回 Phase 1（第二輪）；第二輪完 → Phase 5
  ↓
Phase 5 結束回報（不自動 push / reset）
```

每輪共執行 11 條優化原則審查（見 `.claude/skills/auto-code-optimize/SKILL.md` §11 條優化原則）。

### 批次（auto-code-optimize-batch）

```
Phase -1 互動確認
  ↓
Phase A 解析輸入（manifest 或 inline）→ 規格化分組
  ↓
Phase B Workspace check + 記 batch_baseline
  ↓
Phase C 逐組執行 → 每組呼叫 auto-code-optimize（--unattended）
       成功：累加 commit；失敗：reset 到該組起點（不污染 HEAD）
  ↓
Phase D 彙整回報（不自動 push / reset）
```

---

## 失敗與終止

| 條件 | 動作 |
|---|---|
| Working tree 不 clean | Phase 0/B 終止；使用者處理後重呼 |
| Script / GDD 解析不到或為空 | 終止 |
| Codex 連續 3 次讀不到 spec | 終止，回報三次嘗試與最後 Codex 回應 |
| Phase 3 `test_round > 10` | revert 到上次 stable，`revert_count++`，回 Phase 1 |
| `revert_count > 3` | 終止 |
| 批次某組 TERMINATED | reset 該組到起點續下組；`stop_on_fail=true` 才停整批 |
| Phase -1 使用者 abort | 終止（無副作用，未啟動任何 git）|

---

## 中斷與復原

Ctrl-C 後：

1. `git status` 看是否有 uncommitted 變更（中斷時可能在 Codex workspace-write 或 git commit 之間）
2. 若有 → 自行 stash / commit / 丟棄；流程不會自動處理（Phase 0/B clean check 會擋下次啟動）
3. 批次續跑 → `--resume`（依 progress 跳過已 done 組，重跑 failed / 未列出）

整批回退到啟動前 → Phase 5 / D 提供的選項 [C]：

```
git reset --hard <baseline_commit>
```

**不自動執行**，由使用者決定。

---

## 無人值守

1. **推薦：auto-accept**（按 `Shift+Tab` 切到 auto-accept-edits）
2. **預授權**：`.claude/settings.local.json` 已加好暫存目錄與 mkdir/rm 命令；Codex MCP 寫入透過 MCP tool（不觸發 Edit/Write 權限）→ 流程中預期不會被打斷
3. **`--dangerously-skip-permissions`**：最不安全，**不建議**

加 `--unattended` flag 可跳過 Phase -1 的人工 ack（Phase 0/B 的 clean check 仍會檢查）。

---

## 實戰範例

### 例 1：對 Time 系統做兩輪優化

```
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/
```

→ 單組 → Phase -1 列預估 + ack → Phase 0-5 → 每輪在 main 上 commit。

### 例 2：對整個 Core 樹分子系統做批次

```
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r
```

→ 偵測到 `Time/`、`Data/`、`Events/` 三個子資料夾 → 自動寫 inline manifest → 呼叫 batch → 三組依序執行 → Phase D 彙整。

### 例 3：依 GDD 對 FT-08 系統優化

```
/auto-code-optimize FT-08
```

→ Glob `design/GDD/[FT-08]*.md` → Read GDD → 抽 scripts → 單組執行（spec 用 GDD）。

### 例 4：批次處理多個工項

```
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02 --name=foundation-opt
```

→ 三組（每 GDD 一組）→ progress 寫到 `.claude/batch-manifests/.progress/foundation-opt.progress`。

### 例 5：續跑前次失敗

```
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md --resume
```

→ 讀 progress → 跳過 `done`、重跑 `failed` 與未列出。

### 例 6：睡前無人值守

```
# 啟動前先 Shift+Tab 切到 auto-accept-edits
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r --unattended
```

→ 跳過 Phase -1 ack，自動進 Phase 0；auto-accept 模式 + 暫存目錄預授權 → 流程預期不會被權限 prompt 打斷。

---

## 相關檔案

| 路徑 | 用途 |
|---|---|
| `.claude/skills/auto-code-optimize/SKILL.md` | 單組 skill 完整規範 |
| `.claude/skills/auto-code-optimize-batch/SKILL.md` | 批次 skill 完整規範 |
| `.claude/batch-manifests/_template.md` | Manifest 範本 |
| `.claude/batch-manifests/<name>.md` | 你寫的 manifest（可 commit）|
| `.claude/batch-manifests/.progress/<name>.progress` | 批次進度（`--resume` 讀取）|
| `.claude/batch-manifests/.reports/<name>-*.md` | 批次報告 |
| `.claude/optimization-work/` | spec / addendum / batch-notes / inline manifest 暫存 |
| `.claude/settings.local.json` | 預授權清單 |

> 建議將 `.claude/optimization-work/`、`.claude/batch-manifests/.progress/`、`.claude/batch-manifests/.reports/` 加入 `.gitignore`，避免 git status 雜訊。
