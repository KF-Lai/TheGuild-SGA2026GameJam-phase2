# 【F-03-DS】ReputationLabelTable

依聲望區間查詢對應顯示文字標籤，供 UI 呈現公會聲望段位名稱。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/ReputationLabelTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：F-03 `ResourceManagement`（或 UI 初始化流程）透過 F-01 DataManager 載入；GDD §3.7 / §6.1
- **資料類別**：（GDD 未指定對應 C# 類別名稱；建議 `TheGuild.Gameplay.Resources.ReputationLabelData`）
- **讀取 API**：`DataManager.GetWhere<ReputationLabelData>(r => currentReputation >= r.minReputation && currentReputation <= r.maxReputation)`
- **消費者**：
  - P-02 Main UI：透過 F-01 DataManager 查詢聲望標籤後直接顯示，不經 F-03（F-03 §3.3 rule 3 / §4.4 / §6.1）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| ID | string | ✓ | — | PK，流水號字串（如 `"1"` ~ `"10"`），僅供排序與 debug，不被業務邏輯引用 |
| minReputation | int | ✓ | -100 ~ 100 | 聲望區間下限（含） |
| maxReputation | int | ✓ | -100 ~ 100 | 聲望區間上限（含） |
| label | string | ✓ | — | 對應聲望區間的顯示文字標籤（具體文字由設計師填入，見 gdd-gap） |

> `ID` 為 CSV 第一欄 string PK（依 [data-files.md CSV 結構規則](../../.claude/rules/data-files.md#csv-結構規則)），不對映 `ReputationLabelData` 業務欄位。

## 約束 / 不變量

- 共 **10 筆**，完整覆蓋 `-100 ~ 100`（F-03 §4.4）
- **無缺口**：任意整數聲望值必須落入某一列的 `[minReputation, maxReputation]`
- **不重疊**：各列區間互不交叉；多列命中時 `GetWhere` 取第一個符合列
- **`minReputation ≤ maxReputation`**（單列內）
- `minReputation` / `maxReputation` 值域須落在 `[REPUTATION_MIN, REPUTATION_MAX]`（即 -100 ~ 100），與 `SystemConstants` 常數對齊（弱約束，parser 不檢查）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| minReputation / maxReputation | `SystemConstants.REPUTATION_MIN` / `REPUTATION_MAX` | 數值區間應落在常數定義範圍內（弱約束，parser 不檢查） |

## 變更注意事項

- 修改後重啟遊戲生效；不影響存檔（標籤為純顯示資料，不序列化）
- 增刪列或調整區間邊界前確認 10 筆完整覆蓋不變，避免 `GetWhere` 回傳 null 導致 UI 顯示空白
- `label` 文字變更不影響任何業務邏輯，僅影響 UI 顯示

## 範例

```csv
# === 聲望標籤對照表 ===
# 依 F-03 §3.7：10 筆完整覆蓋 -100~100，無缺口、無重疊
# label 文字為設計師填入（GDD 未指定，此處為佔位示範）

ID,1,2,3,4,5,6,7,8,9,10
minReputation,-100,-80,-60,-40,-20,0,20,40,60,80
maxReputation,-81,-61,-41,-21,-1,19,39,59,79,100
label,臭名昭著,惡名在外,不受信任,默默無聞,初出茅廬,小有名氣,頗具聲望,遠近馳名,聲名赫赫,傳奇公會
```

（10 列示範；`label` 文字為暫定佔位，正式值由設計師確認後填入。）

## 附錄

### gdd-gap

- **`label` 欄位文字值**：F-03 §3.7 / §4.4 均未列出 10 個標籤的具體文字，由設計師填入 CSV；上方範例為佔位示範，非正式設計值
- **資料類別名稱**：GDD 未明示對應 C# 類別名，建議 `ReputationLabelData`（依命名慣例推導）；確認後補回 F-03 §3.7
- **PK 欄位命名**：GDD 未明示，依 [CSV 規範第一欄為 string PK](../../.claude/rules/data-files.md#csv-結構規則) 慣例補足
