# 【FT-01-DataSpecs】VeteranRankWeightTable

老手招募候選池的階級加權隨機表。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/VeteranRankWeightTable.csv`
- **解析方式**：`CsvParser.Parse`（標準 row-based 解析）
- **註冊位置**：F-01 DataManager `Awake` 載入；FT-01 Adventurer Recruitment 透過 `DataManager.Get<VeteranRankWeightData>(rank)` 查詢
- **讀取 API**：`DataManager.GetAll<VeteranRankWeightData>()` 取全表 + `DataManager.Get<VeteranRankWeightData>(rank)` 取單筆
- **消費者**：
  - FT-01 Adventurer Recruitment：§4.4 `RollVeteranRank(maxRecruitableRank)` 從本表取階級權重，過濾後做加權隨機

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| rank | string (PK) | ✓ | D / C / B / A / S | 冒險者階級（必須涵蓋全部 5 階） |
| weight | int | ✓ | ≥ 0 | 加權隨機權重；0 = 該階級實質排除（保留欄位語意） |

## 約束 / 不變量

- `rank` PK 必須涵蓋全部 5 階（D / C / B / A / S）；缺任一階拋 `VeteranRankWeightTableValidationException`
- `weight` ≥ 0；負值視為 0 並 `Debug.LogError`
- 全表 `weight` 加總必須 > 0（至少一階非零）；否則 FT-01 老手池無法生成,拋 validation 異常
- 本表**不含**任務難度（F/E/SS/SSS）：F/E 屬新手池(`DAILY_FREE_REFRESH` 機制);SS/SSS 為任務難度上限,不對應冒險者階級

## Cross-ref（對其他表的引用）

- 無（純權重表，不參照其他表）

## 變更注意事項

- 修改 `weight` 即時生效（重啟遊戲後讀取）
- 各階級權重相對比例決定老手池的階級分佈；過度偏向高階會破壞「S 階稀有感」的設計支柱
- 新增階級（如未來 SS 階冒險者）需同步：(a) C-02 `AdventurerInstance.rank` 值域、(b) RecruitCostTable 補費用 + 聲望門檻、(c) FT-01 §4.4 過濾邏輯、(d) FT-02 SuccessRateTable 與 DeathRateTable、(e) RANK_INDEX 索引

## 範例（對齊 game-concept §冒險者系統 老手邀請加權分布）

```csv
# === 老手招募階級權重表 ===
# FT-01 §4.4 RollVeteranRank 使用；對齊 game-concept Phase 1 legacy 加權分布
# weight 為相對權重，加權隨機由 DataManager 自動正規化

rank,weight
D,40
C,30
B,18
A,9
S,3
```

## 安全範圍與調參指引

| 階級 | Jam 預設 | 安全範圍 | 影響 |
|---|---|---|---|
| D | 40 | 30~50 | 中前期主力；過低讓玩家難以累積 D 階人手 |
| C | 30 | 20~40 | 中期過渡階級 |
| B | 18 | 10~25 | 中後期菁英 |
| A | 9 | 5~15 | 過高會讓 A 階太容易取得，削弱稀缺感 |
| S | 3 | 1~5 | 過高破壞 S 階的傳奇感；建議 ≤ 5 |

> 平衡指標：D + C 應佔總權重 70% 以上(40 + 30 = 70/100 = 70%);A + S 應 ≤ 15%(9 + 3 = 12/100 = 12%);中段 B 為策略階級,18%~25% 為合理區間。
