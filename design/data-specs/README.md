# Data Specs — 資料表規格書

本資料夾收錄所有 CSV 資料表的**填表規格**，供 Claude Code 與設計師依規格生成資料內容。

---

## 命名規則

```
[<系統ID>-Data] <table-name>.md
```

- `系統ID` 對應到 `design/GDD/[系統ID] *.md` 的 owner 系統
- 加上 `-Data` 後綴與 GDD 區分（避免在檔案搜尋時混淆）
- `table-name` 使用 kebab-case，與實際 CSV 檔名（PascalCase）對映

範例：
| 規格書 | CSV 檔案 | Owner GDD |
|---|---|---|
| `[F-01-Data] system-constants.md` | `SystemConstants.csv` | `[F-01] data-manager.md` |
| `[F-03-Data] bankruptcy-threshold-table.md` | `BankruptcyThresholdTable.csv` | `[F-03] resource-management.md` |

---

## 規格書範本

每份規格書固定包含以下章節：

```markdown
# [<系統ID>-Data] <表名>

## 基本資訊
- 檔案路徑：Assets/Resources/Data/<檔名>.csv
- 解析方式：Parse / ParseSystemConstants
- 註冊位置：哪個系統的 RuntimeInitializeOnLoadMethod
- 消費者：哪些系統會讀這張表（用哪些欄位）

## 欄位定義
| 欄位 | 型別 | 必填 | 範圍 | 說明 |

## 約束 / 不變量
- 條列式

## Cross-ref（對其他表的引用）
- 條列式（無則寫「無」）

## 變更注意事項
- 條列式（影響哪些系統、需要重啟、需要清存檔等）

## 範例（3~5 列）
```csv
[實際 CSV 片段]
```
```

---

## CSV 格式與註解規則（共用）

詳見 `.claude/rules/data-files.md` 與 `TheGuild-unity/Assets/Scripts/Core/Data/CsvParser.cs`。重點摘要：

| 規則 | 說明 |
|---|---|
| 註解列 | 第一欄首字元為 `#` → 整列跳過 |
| 空白列 | 整列所有欄位皆空 → 跳過 |
| Header 起點 | 從上往下找「第一個非註解、非空白」的列當 header |
| 引號包裹 | `"..."` 內可含逗號、換行；`""` 表示一個引號字元 |
| 列陣列 | `\|` 分隔（如 `melee\|starter`） |
| bool 寫法 | `true` / `false` / `TRUE` / `FALSE` / `1` / `0` 均可 |
| 主鍵 | 第一欄；空白或重複會發 warning |
| Null sentinel | `int` 用 `0`、`string` 用 `""`、多值欄位用單一 `0` |
| 欄位命名 | 「ID」縮寫一律大寫（`professionID`、`typeIDs`） |

---

## 表格索引

> 狀態：📐 規劃中 / 🔧 實作中 / ✅ 已上線

### Foundation 層（F-01 ~ F-03）

| 規格書 | CSV | 狀態 | Owner |
|---|---|---|---|
| [F-01-Data] system-constants.md | `SystemConstants.csv` | ✅ | F-01 DataManager |
| [F-03-Data] bankruptcy-threshold-table.md | `BankruptcyThresholdTable.csv` | ✅ | F-03 ResourceManagement |

### Core 層（C-01 ~ C-06）

待補。

### Feature 層（FT-01 ~ FT-11）

待補。

---

## 工作流程（給 Claude Code）

當需要生成或修改 CSV 內容時：

1. **先讀規格書**（本資料夾）確認欄位、型別、約束
2. **若同時需修改 schema** → 先更新規格書，再生 CSV
3. **寫 CSV** 到 `TheGuild-unity/Assets/Resources/Data/<檔名>.csv`
4. **觸發 Unity refresh**（`mcp__UnityMCP__refresh_unity`）讓 .meta 自動生成
5. **檢查 console** 是否有 `[CsvParser]` 警告 / 錯誤

新增表格時：
1. 先在 GDD 寫清楚表格用途與消費者
2. 在本資料夾建規格書 `[<系統ID>-Data] <table-name>.md`
3. 在本 README 的索引表登記
4. 才產 CSV 檔案
