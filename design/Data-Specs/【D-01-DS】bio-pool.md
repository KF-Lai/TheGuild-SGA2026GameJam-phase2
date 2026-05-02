# 【D-01-DS】BioPool

D-01 冒險者背景故事模板池，以 raceID × professionID 組合分組存放 bio 文字模板，供 `GetRandomBio(raceID, professionID, name, gender)` 隨機抽取並替換 `{name}` / `{pronoun}` 後回傳完整 bio 字串。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/BioPool.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）
- **註冊位置**：D-01 CharacterContentService（`DataManager.GetAll<BioEntry>()`，參見 D-01 GDD §6.1）
- **資料類別**：`BioEntry`（FQN 未於 GDD 中指定）
- **讀取 API**：`DataManager.GetAll<BioEntry>()` → 依 raceID + professionID Fallback Chain 過濾後隨機抽取（D-01 GDD §3.5）
- **消費者**：
  - C-02 AdventurerManagement：`CreateRandomInstance` 呼叫 `GetRandomBio(raceID, professionID, name, gender)` 一次性生成並寫入 `AdventurerInstance.bio`（D-01 GDD §4.2 / §4.3 / §6.2）
  - P-02 MainUIFramework：名冊面板讀取 `AdventurerInstance.bio` 顯示（D-01 GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `bioID` | `int` | ✓ | ≥ 1（0 為 null sentinel） | PK；從 1 起連續編號 |
| `raceID` | `int` | ✓ | ≥ 0 | FK → RaceTable；`0` = fallback 通用（任何種族皆適用） |
| `professionID` | `int` | ✓ | ≥ 0 | FK → ProfessionTable；`0` = fallback 通用（任何職業皆適用） |
| `template` | `string` | ✓ | 任意字串（含空字串） | bio 文字模板；支援替換變數 `{name}` 與 `{pronoun}`（D-01 GDD §3.3）；含逗號時以 `"..."` 包裹 |

> `raceID=0` 與 `professionID=0` 為合法 fallback sentinel，不視為 null；只有 PK `bioID=0` 才是 null sentinel。

## 約束 / 不變量

- PK `bioID` 從 1 起，0 為 null sentinel，不得使用 0 作為有效 bioID
- Jam 最小內容量：4 種族 × 7 職業 = 28 筆精確模板（raceID≥1 × professionID≥1），每種組合至少 1 筆（D-01 GDD §3.2）
- Fallback 模板（raceID=0 或 professionID=0）可選；Jam 版非必填（D-01 GDD §3.2 / §7.2）
- `template` 為空字串時視為有效模板，`GetRandomBio` 回傳空字串，不 LogWarning（D-01 GDD §5.1）
- `template` 含未定義 `{variable}` 時，保留原文不替換，不拋例外（D-01 GDD §5.2）
- 同一 raceID × professionID 可有多筆，均勻隨機抽取（D-01 GDD §5.2）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `raceID` | `RaceTable.raceID` | FK 弱約束（parser 不檢查；`raceID=0` 為合法 fallback sentinel，不引用 RaceTable） |
| `professionID` | `ProfessionTable.professionID` | FK 弱約束（parser 不檢查；`professionID=0` 為合法 fallback sentinel） |

## 變更注意事項

- CSV 內容異動（新增 / 修改 template 文字）無需重新編譯，下次 DataManager 載入即生效
- 新增種族或職業後，需補充對應的精確模板或 fallback 模板，否則 `GetRandomBio` 依 Fallback Chain 找不到結果時回傳空字串並 LogWarning
- `template` 文字修改只影響後續新生成的冒險者（bio 於建立時一次性寫入 `AdventurerInstance.bio`，已生成冒險者不受影響）
- 不影響存檔（BioPool 為唯讀內容表；實際 bio 文字存於 `AdventurerInstance.bio`，由 FT-10 序列化）

## 範例

```csv
# === 冒險者背景故事模板池 ===
# raceID=0 或 professionID=0 為 fallback 通用模板
# 模板支援變數：{name}（冒險者名字）、{pronoun}（他/她/其）

bioID,1,2,3,4
raceID,1,1,2,0
professionID,1,2,1,0
template,"{name}出身平凡村莊，{pronoun}握劍時卻從未猶豫。","{name}靠法術謀生，{pronoun}說那只是工具。","{name}十五歲離開森林，{pronoun}眼神裡有什麼東西藏得更深。","{name}行跡飄忽，沒人知道{pronoun}從哪來。"
```

（4 筆示意；實際 Jam 版至少 28 筆精確模板。CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### Fallback Chain（查詢優先序）

依 D-01 GDD §3.5：

```
1. raceID=X  × professionID=Y  → 精確匹配（最優先）
2. raceID=X  × professionID=0  → 同種族通用模板
3. raceID=0  × professionID=Y  → 同職業通用模板
4. raceID=0  × professionID=0  → 完全通用模板
5. 皆無結果  → 回傳 ""；呼叫方自行決定是否顯示佔位文字
```

### 安全範圍與調參指引

| 可調項目 | 預設值（Jam） | 安全範圍 | 影響 |
|---------|-------------|---------|------|
| 每 raceID×professionID 組合的模板數 | 1 筆 | 1 ~ 不限 | 多筆時隨機抽取，增加重複招募同職業/種族冒險者時的文字多樣性 |
| Fallback 模板（raceID=0 或 professionID=0） | 可選，Jam 版非必填 | — | 缺少時若某組合無精確模板，bio 回傳空字串 |

（來源：D-01 GDD §7.2）
