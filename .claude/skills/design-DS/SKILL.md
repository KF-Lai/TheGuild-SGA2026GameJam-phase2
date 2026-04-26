---
name: design-DS
description: "全自動以 GDD 為來源產生 CSV 表格規格書（Data-Specs, DS）。輸入一個或多個 GDD 代號／名稱（逗號或空白分隔），對該 GDD 在 design/Data-Specs/data-index.md 名下的所有表格產出 DS 檔案，並更新索引狀態。DS 同時供 Codex 實作參考與 CSV 撰寫規範。"
argument-hint: "<GDD-id-or-name>[,<another>...] — 例：'/design-DS C-01' 或 '/design-DS C-01,C-02 FT-01'"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit
---

全自動 skill：不互動、不問問題、不分階段確認；流程跑到底僅輸出產出清單與索引更新摘要。**兩個例外**：(1) 模型或思考強度不符時立即終止；(2) data-index 列表與 GDD 內容不符時中止並回報。

---

## Pre-flight：模型與思考強度檢查

**在執行任何 skill 步驟前先執行此檢查，兩項全部通過才繼續；任一不符立即終止 skill。**

### 模型檢查

確認當前模型為 Sonnet 系列（模型 ID 包含 `sonnet`）。

- 通過條件：system context 中的模型名稱含 `sonnet`
- 不符合 → 立即終止，輸出：
  ```
  [design-DS 終止] 需要 Sonnet 模型。
  當前模型：<current-model-id>
  請執行 /model claude-sonnet-4-6 後重新執行 /design-DS。
  ```

### 思考強度檢查

確認當前 extended thinking 強度為 high。

- 通過條件：目前以 high 思考強度運行
- 不符合 → 立即終止，輸出：
  ```
  [design-DS 終止] 需要 high 思考強度。
  請執行 /think high 後重新執行 /design-DS。
  ```

---

## 1. Parse 輸入

- 以逗號 `,` 與任意空白切分 args；空輸入直接報錯：
  > `Usage: /design-DS <GDD-id|name>[,<another>...]，例：/design-DS C-01,FT-01`
- 每個 token 解析為 GDD：
  - 看似系統 ID（regex `^[A-Z]+-\d+$`，如 `F-01` / `C-02` / `FT-08`）→ glob `design/GDD/【<id>】*.md`
  - 否則視為 GDD 名稱關鍵字 → glob `design/GDD/*<token>*.md`，例：`mission-database` → `design/GDD/【C-01】mission-database.md`
- 任一 token 解析失敗或命中多筆 → 中止並列出未解析項，**不部分執行**

## 2. 共用素材一次性讀取

僅讀以下檔案，不要逐表重複讀：

- `design/Data-Specs/data-index.md` — **表格索引**：四張分區索引（Foundation／Core／Feature／文字表）、DataSpec 命名規則、狀態圖例、歸檔分區
- `.claude/rules/data-files.md` — **CSV 格式與填寫規範主來源**：CSV 結構規則 / 符號速查 / 特殊值規範 / 命名規範 / ID 型別使用原則（規範來源 = `【F-01】data-manager.md` §3.2）

skill 內**不複述**上述內容；DS 產出時若需引用，以 markdown link 指向原文（表格清單／狀態指 `data-index.md`，CSV 規範條文一律指 `data-files.md`）。

## 3. 篩選待產表格

從 `data-index.md` 的 Foundation／Core／Feature／文字表四張表中，篩選「GDD 來源」欄字串包含當前 GDD ID（如 `【F-03】`）的所有 row：

- **跳過**「DataSpec 狀態」= ✅ 的表格（避免覆寫已完成 DS）
- **跳過** row 內**任一欄位**含 ⚠️ 或字樣 `deprecated` 的表格（deprecated 標記常見於 CSV 狀態欄，不限 DataSpec 狀態欄）
- 將跳過原因（已 ✅ / deprecated）分別記入摘要

## 4. 命名規則

- 檔案路徑：`design/Data-Specs/【<系統ID>-DS】<table-name>.md`（全形括號，與 GDD 命名一致）
- `<系統ID>` 取自 GDD owner（`F-01` / `C-02` / `FT-08` 等）
- `<table-name>` 對映實際 CSV 檔名（PascalCase）→ kebab-case，例：`SystemConstants.csv` → `system-constants`
- 索引內顯示用半形括號 + 反引號（與既有 row 風格一致）：`✅ \`[<系統ID>-DS] <table-name>.md\``

## 5. DS 範本（內嵌，逐字套用）

````markdown
# 【<系統ID>-DS】<TableName>

<一句話用途說明，與 GDD §3 開頭描述對齊>

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/<TableName>.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）｜ `CsvParser.ParseSystemConstants`（key-value 表，亦為 column-based）
- **註冊位置**：<Owner 系統的 RegisterTables() / RuntimeInitializeOnLoadMethod，未明示則寫 GDD 章節依據>
- **資料類別**：`<C# class FQN，例：TheGuild.Gameplay.Resources.BankruptcyThresholdData>`（key-value 表填「無」）
- **讀取 API**：`DataManager.Get<T>(key)` / `GetAll<T>()` / `GetWhere<T>(predicate)` 等實際 API
- **消費者**：
  - <系統ID 系統名>：<引用方式 + GDD §章節>
  - <可多列>

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| <fieldName> | <int / long / float / string / bool / int[] / string[]> | ✓ / ✗ | <range，無則 — > | <說明> |

> 若 PK 欄位語意特殊（如不綁定資料類別），於表後補一行說明。

## 約束 / 不變量

- <逐條列出 GDD §3 / §7 寫明的 invariants（範圍、唯一性、覆蓋性、單位）>
- <跨欄位約束，例如 `min ≤ max`、加總 > 0>

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| <fieldName> | <Other表.欄位 / SystemConstants.KEY> | <FK 強約束 / 弱約束（parser 不檢查）> |

> 無引用時整段以「無」一行帶過，不留空表。

## 變更注意事項

- <修改後生效時機（即時／重啟／domain reload）>
- <對其他系統的影響（事件、快取、存檔）>
- <breaking change 標示與連動清單>

## 範例

```csv
# === <表用途> ===
# <說明：對齊 GDD §章節 / game-concept 哪段設計>

<pkFieldName,record1ID,record2ID,record3ID>
<field2Name,record1Value,record2Value,record3Value>
<field3Name,record1Value,record2Value,record3Value>
<...>
```
（每個欄位一列；3~5 筆記錄，數值務必對齊 GDD §7 給的調參或 game-concept 的設計意圖，不自行推測；CSV 為 column-based / 轉置格式，規範見 `.claude/rules/data-files.md`）

## 附錄（依需要保留，無內容則整段刪除）

- **已註冊 key 清單**（key-value 表如 `SystemConstants` 必含）
- **安全範圍與調參指引**（含 tuning knob 的數值表，數據須來自 GDD §7）
- **Phase 標記**（如 deprecated、Phase 1 legacy 警示）
````

## 6. 撰寫守則（核心約束，違反即重寫該段）

1. **逐字（verbatim）來源**：欄位名、型別、必填、範圍、約束、Cross-ref 必須能在 GDD §3 / §7 找到對應原文；禁止改寫、重構、推測 GDD 未提及的規則
2. **系統／功能規則同上**：DS 內提到任何系統行為，僅複述 GDD 用語，不引申、不簡化
3. **GDD 缺漏處理**：若 GDD 沒寫某欄位的範圍／約束，於該處填「—」或「（GDD 未指定）」並在收尾摘要列為 `gdd-gap`，**不要自己補**
4. **文字風格**：簡潔、精確、高效；條列式為主，每條一句話；禁用贅述／口頭禪／emoji
5. **CSV 範例必含**：3~5 列，數值對齊 GDD §7 給的 tuning 或 game-concept 推導值；含註解列示範 `#` 用法
6. **不寫 CSV 實檔**：本 skill **不**輸出到 `TheGuild-unity/Assets/Resources/Data/Tables/`；範本內 CSV 範例僅為 DS 文件參考片段，CSV 實檔由後續工項另行產出

## 7. 為每張表生成 DS

針對每張待產表格：

1. 讀對應 GDD 章節（依 data-index.md「GDD 來源」欄指出的 §3 / §7 等章節，未指明則讀 §3 與 §7 全部）
2. **GDD 內容吻合檢查**：確認 GDD 中確實存在 data-index 所列表格的對應設計規則（§3 或 §7 有明確提及該表名稱或相關欄位），若找不到對應內容 → **立即中止整個 skill**，不產出任何檔案，輸出：
   ```
   [design-DS 中止] data-index 與 GDD 內容不符
   表格：<TableName>（來源 GDD：<GDD-ID>）
   data-index 登記：<對應列摘要>
   GDD 未找到：§3 / §7 無對應設計規則或表格定義
   請確認 data-index 登記是否有誤，或補充 GDD 後重新執行 /design-DS。
   ```
2. 套用 §5 範本骨架填寫，遵守 §6 撰寫守則
3. 寫入 `design/Data-Specs/【<系統ID>-DS】<table-name>.md`
4. 寫完後對照 §6 自檢；不符合即改完再進下一張

## 8. 更新 data-index.md

- 將該 row 的「DataSpec 狀態」從 `📐` 改為 `✅ \`[<系統ID>-DS] <table-name>.md\``（半形括號 + 反引號，與既有 row 一致）
- 重算底部「## 統計」表的 DataSpec ✅ 欄位
- CSV 狀態保持原值（DS 不負責產 CSV）

## 9. 收尾輸出

僅輸出以下摘要（無多餘評論）：

```
DS 產出完成

GDD: <list>
新增 DS: <count>
  - design/Data-Specs/【...】....md
  - ...
跳過（已 ✅）: <list>
跳過（deprecated）: <list>
GDD 缺漏（gdd-gap）: <field@table>
data-index.md 統計已更新
```
