---
name: design-CSV
description: "全自動以 DS 規格書為來源產出 CSV 實檔。輸入一個或多個 GDD 代號（逗號或空白分隔）或 all，依 DS 範例段落與附錄 key 清單生成 column-based 轉置格式 CSV，寫入 Assets/Resources/Data/Tables/，並更新 data-index.md CSV 狀態。"
argument-hint: "<GDD-id>[,<another>...] | all — 例：'/design-CSV C-02' 或 '/design-CSV all'"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit, Bash
---

全自動 skill：不互動、不問問題、不分階段確認；流程跑到底僅輸出產出清單與索引更新摘要。**兩個例外**：(1) 模型不符時立即終止；(2) DS 範例段落缺失時中止並回報。

---

## Pre-flight：模型檢查

確認當前模型為 Sonnet 系列（模型 ID 含 `sonnet`）。

不符合 → 立即終止：

```
[design-CSV 終止] 需要 Sonnet 模型。
當前模型：<current-model-id>
請執行 /model claude-sonnet-4-6 後重新執行 /design-CSV。
```

（本 skill 不要求 extended thinking。）

---

## 1. Parse 輸入

支援格式：
- GDD 系統 ID（regex `^[A-Z]+-\d+$`，如 `C-02`、`FT-08`）→ 處理該 GDD 下所有待產 CSV
- `all` → 處理 data-index.md 中所有待產 CSV（CSV 狀態 = 📐）
- 逗號或空白分隔的多個 GDD ID → 依序處理

空輸入 → 報錯：

```
Usage: /design-CSV <GDD-id>[,<another>...] | all
例：/design-CSV C-02       → 處理 C-02 所有表
    /design-CSV C-03,C-04  → 處理 C-03 + C-04
    /design-CSV all        → 處理所有待產 CSV
```

---

## 2. 讀取共用素材

一次性讀取（不重複讀）：
- `design/Data-Specs/data-index.md`：表格列表、GDD 來源對應、CSV 狀態
- `.claude/rules/data-files.md`：CSV 格式規範（作為生成參考，不輸出到收尾摘要）

---

## 3. 篩選待產表格

從 data-index.md 四張分區表（Foundation / Core / Feature / 文字表）中，找出符合下列**所有**條件的 row：

**包含條件**：
- 「GDD 來源」欄包含目標 GDD ID（如 `【C-02】`）；`all` 時無此過濾
- CSV 狀態欄 = `📐`

**排除條件**（分別記入摘要）：
| 排除原因 | 條件 | 摘要標籤 |
|---|---|---|
| deprecated | row 內任意欄含 ⚠️ 或文字 `deprecated` | `跳過（deprecated）` |
| 歸檔分區 | 屬於「歸檔分區（Archived）」 | `跳過（deprecated）` |
| DS 未完成 | Glob `design/Data-Specs/` 找不到對應 `【*-DS】<table-name>.md` 檔案 | `跳過（DS 未完成）` |
| CSV 已存在 | `TheGuild-unity/Assets/Resources/Data/Tables/<TableName>.csv` 已存在 | `跳過（已存在）` |

DS 檔名對應規則：data-index.md 「表格名稱」欄（PascalCase）→ kebab-case，例：`ProfessionTable.csv` → Glob `【C-03-DS】profession-table.md`。

---

## 4. 為每張表生成 CSV

### 4.1 讀取 DS 檔案

讀取對應 DS 檔案的完整內容。

### 4.2 判斷表格類型

讀 DS「基本資訊」章節「解析方式」欄位：
- 含 `ParseSystemConstants` → **key-value 表**，走 §4.4
- 含 `CsvParser.Parse`（一般） → **一般表**，走 §4.3

### 4.3 一般表 CSV 生成

1. 定位 DS `## 範例` 章節，取出 ` ```csv ... ``` ` code block 的全部內容（含 `#` 註解列與資料列）
2. 若 code block 不存在 → **立即中止整個 skill**，輸出：
   ```
   [design-CSV 中止] DS 範例段落缺失
   表格：<TableName>（DS：<DS 檔名>）
   請確認 DS 文件 ## 範例 章節含有 ```csv 區塊，補充後重新執行 /design-CSV。
   ```
3. **樣本標記判斷**：若 DS 範例 code block 下方的說明文字（括號外的補充說明）包含「樣本」、「見 GDD」、「設計師應補充」、「sample」等字樣，或 code block 僅有 3~5 筆示例資料且 DS 欄位定義明示該表可無限延伸（如 NPC 模板、任務模板等） → 在 CSV 首行加：
   ```
   # [SAMPLE] 本檔為 DS 範例資料，完整內容請依 GDD / DS 規格補充
   ```
4. 輸出 CSV 內容 = 步驟 3 補充說明（若有）+ code block 原始內容

### 4.4 key-value 表 CSV 生成

1. 讀 DS `## 附錄 > 已註冊 key 清單`（一或多個子表格），收集所有 key、型別、預設值、說明
2. **gdd-gap 判斷**：預設值欄為空字串、僅有「（GDD 未指定）」、或不含任何數字/字串字面值 → 記入 gdd-gap 清單；CSV 對應值填佔位值（`0` for int/long、`0.0` for float、`""` for string），並在該 key 所在列上方插入：
   ```
   # [GAP] <KEY> 預設值 GDD 未指定，請人工填寫
   ```
3. 從 DS `## 範例` code block 取出所有 `#` 開頭的說明列，放在 CSV 頭部
4. 生成 column-based / 轉置格式：
   - key 列：所有 key 橫排（以逗號分隔）
   - value 列：對應預設值橫排
   - description 列：對應說明橫排
5. 若 DS 附錄有明顯分組（如「FT-12 owner」/「FT-08 消費端」），在分組間插入 `# --- <分組名稱> ---` 後，繼續同一 key 列延伸（**不可**另起新的 `key,...` 列，符合 `ParseSystemConstants` 僅讀第一組限制）

### 4.5 輸出路徑

```
TheGuild-unity/Assets/Resources/Data/Tables/<TableName>.csv
```

`<TableName>` 直接取自 data-index.md「表格名稱」欄（已為 PascalCase，保持不變）。

若目標目錄 `TheGuild-unity/Assets/Resources/Data/Tables/` 不存在，以 Bash `mkdir -p` 建立後再寫檔。

---

## 5. 更新 data-index.md

對每張成功寫出的 CSV：
- 將 data-index.md 對應 row 的「CSV 狀態」欄從 `📐` 改為 `✅`
- 重算底部「## 統計」表的 CSV ✅ 欄位數值

---

## 6. 收尾輸出

僅輸出以下摘要（無多餘評論）：

```
CSV 產出完成

GDD: <list>
新增 CSV: <count>
  - TheGuild-unity/Assets/Resources/Data/Tables/Xxx.csv  [完整]
  - TheGuild-unity/Assets/Resources/Data/Tables/Yyy.csv  [樣本 — 需設計師補充]
跳過（DS 未完成）: <list>
跳過（已存在）: <list>
跳過（deprecated）: <list>
GDD 缺漏（gdd-gap）: <KEY@TableName>
data-index.md 統計已更新
```

`[完整]` = CSV 含全部設計資料（key-value 表或固定筆數表）；`[樣本]` = 僅含 DS 範例資料，需設計師依 GDD / DS 補全。
