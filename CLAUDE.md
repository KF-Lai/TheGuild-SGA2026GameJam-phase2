# The Guild — Game Jam Phase 2

> 2D 放置型經營遊戲 | Unity | SGA 2026 Game Jam

## 專案概要

玩家身為新上任的公會長，招募冒險者、接受委託、派遣任務、管理資源，經營與發展冒險者公會。
核心循環：審核委託 → 招募冒險者 → 派遣任務 → 等待與解決 → 任務結算。
設計重點：離線掛機、NPC 自主性、真實後果（死亡/破產）。

## 引擎與技術棧

- **引擎**：Unity 2D (URP)
- **語言**：C# (.NET)
- **UI**：UI Toolkit (UXML/USS) 為主，UGUI 為輔（世界空間 UI）
- **資料驅動**：ScriptableObject + JSON 設定檔（`Assets/Resources/Data/`）
- **命名慣例**：`PascalCase`（public）、`_camelCase`（private field）、`camelCase`（local）

## 協作模式

### Claude Code — 專案主導與審查（Opus）
- 設計、規劃、討論專案內容
- 依工項規模分級，決定交付方式
- 審查 Large/Medium 工項中 Codex 的產出
- Small 工項委派 subagent 閉環處理

### Codex — 實作人員（MCP）
- 接收 Claude Code 發送的工項需求，進行實作
- 實作完成後回報 Claude Code 進行審查
- 審查通過後負責寫入檔案（`sandbox="workspace-write"`）

### Subagents — 輕量實作
- Small 工項：subagent 全權負責實作、審查、寫入

### Gemini — Web Search + 文本初稿（CLI）
- Web Search：全域 PreToolUse hook 自動觸發
- 文本初稿：writer agent 可呼叫 `gemini_generate_narrative` 產出初稿，再由 writer 擴充修飾

## 工項交付流程（分級制）

### Large（跨系統、核心架構、新系統實作）
```
1. Claude Code 完成設計文件 (design/gdd/)
2. 將設計轉為工項需求（目標、範圍、技術限制、驗收條件）
3. Claude Code 發送需求 → Codex 實作（read-only）
4. Codex 回報完成 → Claude Code 審查
   ├─ APPROVED → 通知 Codex 寫入檔案（workspace-write）→ 完成
   └─ CHANGES REQUIRED → 發回 Codex 修改（最多 2 次來回）
5. 2 次來回後仍未解決 → 通知使用者介入
```

### Medium（單一類別/系統組件）
```
1. Claude Code 發送需求 → Codex 實作（read-only）
2. Codex 回報完成 → Claude Code 審查
   ├─ APPROVED → 通知 Codex 寫入檔案（workspace-write）→ 完成
   └─ CHANGES REQUIRED → 發回 Codex 修改（最多 1 次來回）
3. 1 次來回後仍未解決 → 通知使用者介入
```

### Small（改 config、修 typo、加欄位、簡單調整）
```
1. 指派合適的 subagent 自行實作、審查、寫入檔案
```

### 分級判斷原則
- 影響 3+ 個系統或檔案 → Large
- 影響單一系統內 1-2 個檔案 → Medium
- 單點修改、無邏輯變更 → Small

## 專案目錄

- `design/gdd/` — 遊戲設計文件
- `production/` — Sprint、里程碑、session state
- `TheGuild-unity/Assets/Scripts/` — C# 原始碼
  - `Core/` — 核心系統（事件匯流排、存檔、時間管理）
  - `Gameplay/` — 玩法系統（委託、冒險者、經濟、公會）
  - `UI/` — UI 程式碼
  - `Data/` — ScriptableObject 定義
- `TheGuild-unity/Assets/Resources/Data/` — JSON 設定檔

## MCP 工具

| MCP | 用途 | 工具名稱 |
|-----|------|---------|
| Codex | 程式碼實作與寫入 | `mcp__codex__codex` |
| Gemini Query | Web Search / 文件查詢 | `mcp__gemini__gemini_query` |
| Gemini Search | 文件搜尋 | `mcp__gemini__gemini_search_docs` |
| Gemini Narrative | 文本初稿生成 | `mcp__gemini__gemini_generate_narrative` |
| UnityMCP | Unity Editor 控制、測試、建置 | `mcp__UnityMCP__*` |

## 開發範圍（Game Jam）

見 `README.md`。聚焦核心循環，嚴控範疇。功能蔓延時執行 `/scope-check`。

## 語言規範

- **設計文件**：以繁體中文撰寫，專有名詞／Unity 工具名稱／程式碼識別符號保持英文
- **程式碼註釋**：以繁體中文撰寫（`//`、`/* */`、`/// <summary>`）
- **識別符號**：英文（PascalCase / _camelCase / camelCase）

## 標準與規則

- 設計文件：`.claude/rules/design-docs.md`
- 玩法程式碼：`.claude/rules/gameplay-code.md`
- 資料檔案：`.claude/rules/data-files.md`
- UI 程式碼：`.claude/rules/ui-code.md`
- 程式碼標準：`.claude/docs/coding-standards.md`
- 情境管理：`.claude/docs/context-management.md`
