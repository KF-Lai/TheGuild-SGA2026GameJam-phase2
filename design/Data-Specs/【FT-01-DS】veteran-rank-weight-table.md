# 【FT-01-DS】VeteranRankWeightTable

老手招募池的階級抽取加權隨機表，驅動 FT-01 `RollVeteranRank()` 的機率分布。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/VeteranRankWeightTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-01 AdventurerRecruitment 的 `RegisterTables()`（FT-01 §6.1）
- **資料類別**：`VeteranRankWeightData`（GDD 未指定完整 namespace；FT-01 §4.4）
- **讀取 API**：`DataManager.GetAll<VeteranRankWeightData>()` → `.ToDictionary(w => w.rank, w => w.weight)`（FT-01 §4.4）
- **消費者**：
  - FT-01 AdventurerRecruitment：`RollVeteranRank(maxRecruitableRank)` 加權隨機抽取老手候選者階級（FT-01 §4.4）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rank` | `string` | ✓ | `D` / `C` / `B` / `A` / `S` | PK；老手池可出現的冒險者階級符號（F/E/SS/SSS 不收錄，FT-01 §3.4 rule 1） |
| `weight` | `int` | ✓ | ≥ 1 | 加權隨機的相對權重；`0` 為 int null sentinel，不得作為合法權重（FT-01 §4.4 防禦性 fallback 回 D） |

> `rank` 欄值為等級符號字串（`D`/`C`/`B`/`A`/`S`），符合全專案 string PK 例外原則（[`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）。

## 約束 / 不變量

- 必須包含且僅包含 `D`、`C`、`B`、`A`、`S` 五筆記錄（FT-01 §3.4 rule 1）
- 所有 `weight` ≥ 1；`0` 為 int null sentinel，等同缺值（[`.claude/rules/data-files.md`](../../.claude/rules/data-files.md) §特殊值規範）
- 所有 `weight` 加總 > 0，保證正規化後機率分布合法（FT-01 §4.4）
- `rank` 不得包含 `F`、`E`、`SS`、`SSS`（老手池階級範圍為 D~S，FT-01 §3.4 rule 1）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `rank` | `AdventurerRankUtil.RankIndex(string)` | 弱約束（parser 不驗證；`RollVeteranRank` 於 runtime 做 `<=` 比較過濾超過 `maxRecruitableRank` 的行，FT-01 §4.4） |

## 變更注意事項

- 修改 `weight` 後於 DataManager 重新載入後即時生效（影響 `RollVeteranRank` 機率分布）
- 增刪 `rank` 行時須確認 `AdventurerRankUtil.RankIndex(string)` 能解析新值
- 唯一消費者為 FT-01，不影響其他系統

## 範例

```csv
# === 老手池階級抽取權重表 ===
# 對齊 FT-01 §4.4 RollVeteranRank；runtime 過濾超過 maxRecruitableRank 的行後重新正規化

rank,D,C,B,A,S
weight,40,30,18,9,3
```

（每個欄位一列；5 筆記錄，數值對齊 FT-01 §7.3 Jam 預設；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-01 §7.3）

| rank | Jam 預設 | 安全範圍 | 影響 |
|---|---|---|---|
| D | 40 | 30 ~ 50 | D 階出現率；過高讓 D 階太難稀釋，過低讓老手池難以填入低階候選者 |
| C | 30 | 20 ~ 40 | — |
| B | 18 | 10 ~ 25 | — |
| A | 9 | 5 ~ 15 | 過高削弱 A 階稀缺感 |
| S | 3 | 1 ~ 5 | 過高破壞 S 階傳奇感 |

> 調整時以相對比例為主（如 D:C:B:A:S ≈ 4:3:2:1:0.3），無需加總至固定值；FT-01 §4.4 會自動正規化。
