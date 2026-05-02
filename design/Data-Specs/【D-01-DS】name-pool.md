# 【D-01-DS】NamePool

D-01 冒險者名字池資料表，以 raceID 分組存放單名記錄，供 `PickRandomNameWithGender(raceID)` API 隨機抽取冒險者顯示名稱與性別。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/NamePool.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）
- **註冊位置**：D-01 CharacterContentService（`DataManager.GetAll<NameEntry>()`，參見 D-01 GDD §6.1）
- **資料類別**：`NameEntry`（FQN 未於 GDD 中指定）
- **讀取 API**：`DataManager.GetAll<NameEntry>()` → 依 raceID 過濾後均勻隨機抽取
- **消費者**：
  - C-02 AdventurerManagement：`CreateRandomInstance` 呼叫 `PickRandomNameWithGender(raceID)` 取得 name 與 gender（D-01 GDD §4.1 / §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `nameID` | `int` | ✓ | ≥ 1（0 為 null sentinel） | PK；從 1 起連續編號 |
| `name` | `string` | ✓ | 非空字串 | 單一顯示名稱（無姓氏），如「利恩」「克拉格」 |
| `gender` | `int` | ✓ | 0 / 1 / 2 | 0=男（他）、1=女（她）、2=中性（牠/其）；供 BioPool 模板替換 `{pronoun}` 用（runtime 替換結果見 D-01 GDD §3.3 / §4.1） |
| `raceID` | `int` | ✓ | ≥ 1 | FK → RaceTable；決定此名字屬於哪個種族（raceID=1 為人類 fallback 保留值） |

## 約束 / 不變量

- PK `nameID` 從 1 起，0 為 null sentinel，不得使用 0 作為有效 nameID
- 每個 `raceID` 至少 6 筆（Jam 最小量：4 種族 × 6 = 24 筆），確保候選池 4 人不重名有餘裕（D-01 GDD §3.1）
- `raceID=1`（人類）必須存在有效名字，作為其他種族查無結果時的 fallback（D-01 GDD §4.1）
- `gender` 只允許 0 / 1 / 2；其他值為非法輸入（D-01 GDD §3.1 / §5.2）
- `name` 不得為空字串（空字串視為無效記錄）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `raceID` | `RaceTable.raceID` | FK 弱約束（parser 不檢查；D-01 GDD §5.1：查無時保留資料，不過濾） |

## 變更注意事項

- CSV 內容異動（新增 / 刪除名字）無需重新編譯，下次啟動 DataManager 重新載入即生效
- 刪除特定 `raceID` 的所有名字記錄時，須確認 `raceID=1`（人類）仍有有效記錄，否則 fallback 鏈斷裂
- `raceID` 的有效集合由 `RaceTable.csv` 定義；增減種族時需同步補充 NamePool 記錄
- 不影響存檔（NamePool 為唯讀內容表）

## 範例

```csv
# === 冒險者名字池 ===
# 依種族（raceID）分組；每個種族至少 6 筆（Jam 最小量）
# gender: 0=男 1=女 2=中性

nameID,1,2,3,4,5
name,艾登,里昂,卡莉,利恩,克拉格
gender,0,0,1,1,0
raceID,1,1,1,2,2
```

（5 筆示意；實際 Jam 版每種族至少 6 筆、共 4 種族 24 筆以上。CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引

| 可調項目 | 預設值（Jam） | 安全範圍 | 影響 |
|---------|-------------|---------|------|
| 每 raceID 的名字筆數 | 6 筆 | 4 ~ 不限 | 低於 4 時，候選池 4 人可能出現重名（FT-01 不去重，重名屬設計接受範圍） |
| gender 分布 | 各種族自由設定 | 無強制比例 | 影響 bio 中 `{pronoun}` 的出現頻率；若想讓代名詞均衡，建議 0/1 各半 |

（來源：D-01 GDD §7.1）
