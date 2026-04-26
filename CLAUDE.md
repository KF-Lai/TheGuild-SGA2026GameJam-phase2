# The Guild — Game Jam Phase 2

> 2D 放置型經營遊戲 | Unity | SGA 2026 Game Jam

---

## 專案概要

玩家身為新上任的公會長，招募冒險者、接受委託、派遣任務、管理資源，經營與發展冒險者公會。
核心循環：審核委託 → 招募冒險者 → 派遣任務 → 等待與解決 → 任務結算。
設計重點：離線掛機、NPC 自主性、真實後果（死亡/破產）。
範疇控制：聚焦核心循環，功能蔓延時執行 `/scope-check`，詳見 `README.md`。

---

## 技術棧

- **引擎**：Unity 2D (URP) + C# (.NET)
- **UI**：UI Toolkit (UXML/USS) 為主，UGUI 為輔（世界空間 UI）
- **資料驅動**：CSV 表格放在 `Assets/Resources/Data/`，欄位規格見 `design/data-specs/`
- **命名慣例**：`PascalCase`（public）、`_camelCase`（private field）、`camelCase`（local）

---

## 目錄結構

```
design/
  GDD/                      ← 遊戲設計文件（[系統ID] system-name.md）
  data-specs/               ← CSV 表格規格書（[系統ID-Data] table-name.md）
  Reports/
production/                 ← Sprint、里程碑、session state
TheGuild-unity/Assets/
  Scripts/Core/             ← 事件匯流排、存檔、時間、資料管理
  Scripts/Gameplay/         ← 委託、冒險者、經濟、公會
  Scripts/UI/
  Resources/Data/           ← CSV 資料檔
  Tests/EditMode|PlayMode/  ← Unity Test Framework
```

---

## 協作模式（本專案專屬）

工項分級制度與 Codex 互動規範詳見 `~/.claude/CLAUDE.md`（全域自動載入）。本專案專屬對映：

- **設計來源**：Large/Medium 工項先寫 `design/GDD/` + `design/data-specs/`，再轉工項需求發 Codex
- **Gemini Narrative**：writer agent 可呼叫 `gemini_generate_narrative` 產生文本初稿
- **Gemini Web Search**：全域 PreToolUse hook 自動觸發，無需手動呼叫
- **Unity 操作**：透過 UnityMCP（refresh、tests、scene、asset、console）
- **CSV 生成**：Claude Code 直接 `Write` 到 `Resources/Data/`，Unity refresh 後 .meta 自動生成

---

## MCP 工具

| MCP | 用途 | 工具名稱 |
|-----|------|---------|
| Codex | 程式碼實作與寫入 | `mcp__codex__codex` |
| Gemini | Web Search / 文件查詢 / 文本初稿 | `mcp__gemini__*` |
| UnityMCP | Unity Editor 控制、測試、建置 | `mcp__UnityMCP__*` |

## 語言與單位規範

- **設計文件 / 程式碼註釋**：繁體中文（Unity API、識別符號、專有名詞保持英文）
- **識別符號**：英文（PascalCase / _camelCase / camelCase）
- **時間單位**：全專案只用「秒」或「小時」，禁用分/天/週
- **Git commit 訊息**：繁體中文（subject 與 description；技術 prefix 如 `opt(round-N):` / `feat:` / `fix:` 與 `Co-Authored-By` trailer 保持英文標準格式）

---

## 規則檔索引

| 主題 | 檔案 |
|---|---|
| CSV 資料格式與欄位規範 | `.claude/rules/data-files.md` |
| CSV 表格規格書索引 | `design/Data-Specs/data-index.md` |
| 玩法程式碼 | `.claude/rules/gameplay-code.md` |
| UI 程式碼 | `.claude/rules/ui-code.md` |
| 設計文件 | `.claude/rules/design-docs.md` |
| 程式碼標準 | `.claude/docs/coding-standards.md` |
| 情境管理 | `.claude/docs/context-management.md` |
| 程式碼行為準則 | `~/.claude/CLAUDE.md`（全域自動載入） |
