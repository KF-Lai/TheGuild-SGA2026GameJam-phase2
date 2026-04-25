# Auto Code Optimize — 使用說明

兩輪自動化 AI pair 優化流程：Claude Code 系統審查 + Codex 實作 + Unity MCP 測試閘。

兩個 slash commands：
- `/auto-code-optimize` — 主入口（單組直跑；多組自動轉批次）
- `/auto-code-optimize-batch` — 批次引擎（manifest 模式 / inline flag）

## 啟動前檢查

- 當前 branch 是 main 或你接受的 feature branch（非 main 警告但繼續）
- `git status` clean（暫存目錄已自動排除）
- 想無人值守跑一夜 → `Shift+Tab` 切到 auto-accept-edits

## 兩個 skill 的關係

`auto-code-optimize` 是包裝器，**多組情境會內部組 inline manifest 自動呼叫 `auto-code-optimize-batch`**。設計上故意重疊：

| 模式 | auto-code-optimize | auto-code-optimize-batch |
|---|---|---|
| 單組（單檔／多檔合併／單資料夾／單 GDD） | ✅ 獨佔 | ❌ 不適用（雖可用 `--scripts=` 但無必要）|
| 多組 inline（多獨立資料夾／-r 跨多子資料夾／多 GDD） | ✅ 自動轉呼 batch | ✅ 直接呼叫 |
| Manifest .md 檔 | ❌ | ✅ 獨佔 |
| `--resume` / `--only=` 續跑 | ❌ | ✅ 獨佔 |

多組 inline 兩者結果完全相同（產出路徑、commit、報告皆一致）。

### 怎麼選

| 情境 | 用哪個 |
|---|---|
| 單組 | `/auto-code-optimize` |
| 多組 inline | `/auto-code-optimize`（懶得分辨）或 `-batch`（明確意圖）皆可 |
| Manifest 配方 | `/auto-code-optimize-batch --manifest=<.md>` |
| 續跑／只跑某組 | `/auto-code-optimize-batch --resume` 或 `--only=` |

簡單原則：除非要可版本化配方或續跑，否則 `/auto-code-optimize` 一條龍。

## /auto-code-optimize — 4 種輸入

```bash
# 1. 直接列檔（單／多）
/auto-code-optimize Path/To/File.cs
/auto-code-optimize Foo.cs Bar.cs            # 空白或逗號分隔

# 2. 資料夾（非遞迴）
/auto-code-optimize Path/To/Folder/

# 3. 資料夾遞迴（多子資料夾自動分組轉批次）
/auto-code-optimize Path/ -r

# 4. GDD ID（純 ID 等價於 --gdd=）
/auto-code-optimize FT-02
/auto-code-optimize --gdd=FT-02,F-01,P-02     # 多 GDD 自動批次
```

| Flag | 說明 |
|---|---|
| `--spec=<path>` | 顯式指定 spec（覆寫 GDD 自動帶入或自動生成）|
| `--recursive` / `-r` | 資料夾遞迴展開 |
| `--gdd=<id>[,<id>]` | 從 GDD 自動找 scripts |
| `--unattended` | 跳過 Phase -1 互動確認 |

GDD scripts 抽取規則：詳見 `.claude/skills/auto-code-optimize/SKILL.md` §GDD scripts 抽取。

## /auto-code-optimize-batch — 兩種模式

### A. Manifest 檔（可 commit 版本化、--resume／--only= 續跑）

```bash
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md
/auto-code-optimize-batch --manifest=<path> --resume          # 續跑失敗／未列出
/auto-code-optimize-batch --manifest=<path> --only=time,data  # 只跑指定組
```

Manifest 結構與欄位：見 `.claude/batch-manifests/_template.md`（schema 速查 + 範例）。

### B. Inline flag（無需 .md）

```bash
/auto-code-optimize-batch --auto=<folder>                     # 子資料夾每個一組
/auto-code-optimize-batch --auto=<folder> --recursive
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02
/auto-code-optimize-batch --scripts=A.cs,B.cs                 # 全部視為單組
/auto-code-optimize-batch --auto=<f> --gdd=FT-08 --name=mixed-2026-04-25
```

| Flag | 說明 |
|---|---|
| `--name=<batch>` | 影響 progress／report 檔名；省略 = `inline-<YYYYMMDD-HHMM>` |
| `--resume` ／ `--only=<g>[,<g>]` | 兩模式皆支援 |
| `--unattended` | 跳過 Phase -1 |

完整 flag 與規格化分組規則：見 `.claude/skills/auto-code-optimize-batch/SKILL.md` §Phase A。

## 流程與失敗處理

兩個 skill 各自的 Phase 細節、狀態欄位、失敗路徑：詳見對應 SKILL.md。重點：

- 不自動 push、不自動切 branch、不自動 reset baseline
- Phase -1 ack 後才動 git；workspace 不 clean 直接終止
- 批次某組 TERMINATED → 該組 reset 到 group 起點，續下組（除非 `stop_on_fail=true`）
- 整批回退到啟動前 → Phase 5 / D 選項 [C] 由使用者決定，**不自動執行**

## 中斷與復原

Ctrl-C 後：
1. `git status` 看是否有 uncommitted（中斷時可能在 Codex workspace-write 或 commit 之間）
2. 自行 stash／commit／丟棄；Phase 0/B clean check 會擋下次啟動
3. 批次續跑 → `--resume`

## 無人值守

| 方式 | 說明 |
|---|---|
| auto-accept（推薦） | `Shift+Tab` 切到 auto-accept-edits |
| 預授權 | `.claude/settings.local.json` 已加暫存目錄與 mkdir/rm；Codex MCP 寫入透過 MCP tool 不觸發 prompt → 預期不被打斷 |
| `--dangerously-skip-permissions` | 最不安全，**不建議** |

`--unattended` 跳過 Phase -1 ack（Phase 0/B clean check 仍會檢查）。

## 實戰範例

```bash
# 對 Time 系統做兩輪優化（單組）
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/Time/

# 整個 Core 樹按子系統分組批次
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r

# 依 GDD 對 FT-08 系統優化
/auto-code-optimize FT-08

# 三 GDD 批次，自訂批次名
/auto-code-optimize-batch --gdd=FT-02,F-01,P-02 --name=foundation-opt

# 續跑前次失敗的批次
/auto-code-optimize-batch --manifest=.claude/batch-manifests/2026-04-25-core.md --resume

# 睡前無人值守（先 Shift+Tab 切 auto-accept-edits）
/auto-code-optimize TheGuild-unity/Assets/Scripts/Core/ -r --unattended
```

## 相關檔案

| 路徑 | 用途 |
|---|---|
| `.claude/skills/auto-code-optimize/SKILL.md` | 單組 skill 規範（Phase 細節 + 11 條優化原則 + GDD 抽取）|
| `.claude/skills/auto-code-optimize-batch/SKILL.md` | 批次 skill 規範（Phase A-D + Progress 格式）|
| `.claude/batch-manifests/_template.md` | Manifest schema 範本（欄位速查 + 範例）|
| `.claude/batch-manifests/<name>.md` | 你寫的 manifest（可 commit）|
| `.claude/batch-manifests/.progress/<name>.progress` | 批次進度（gitignored；`--resume` 讀取）|
| `.claude/batch-manifests/.reports/<name>-*.md` | 批次報告（gitignored）|
| `.claude/optimization-work/` | spec / addendum / batch-notes / inline manifest 暫存（gitignored）|
| `.claude/settings.local.json` | 預授權清單 |
