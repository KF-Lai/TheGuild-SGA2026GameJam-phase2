# 【FT-01-DS】VeteranRankWeightTable

老手池階級加權隨機設定表，供 FT-01 `RollVeteranRank` 抽取老手候選冒險者的階級分布。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/VeteranRankWeightTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-01 AdventurerRecruitment 初始化流程（GDD 未明示 RegisterTables 具體位置；§6.1 依賴 F-01 DataManager 載入）
- **資料類別**：`TheGuild.Gameplay.Recruitment.VeteranRankWeightData`
- **讀取 API**：`DataManager.GetAll<VeteranRankWeightData>()`
- **消費者**：
  - FT-01 AdventurerRecruitment：`RollVeteranRank(maxRecruitableRank)` 中讀取全部記錄，過濾超過 `FT06.GetMaxRecruitableRank()` 上限的階級後加權隨機（§4.4）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rank` | `string` (PK) | ✓ | D/C/B/A/S | 老手池可出現的冒險者階級符號；F/E 屬新手池，不納入此表 |
| `weight` | `int` | ✓ | ≥ 1 | 加權隨機用整數權重；值越大該階級出現機率越高（§7.3） |

> `rank` 為 string 型 PK，符合全專案「具顯示語義的等級符號維持 string」原則（[`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）。

## 約束 / 不變量

- `rank` 取值限定於 D/C/B/A/S（GDD §3.4：老手池不生成 F/E 階）
- 每個合法階級僅出現一次（PK 唯一性）
- `weight ≥ 1`（GDD §7.3 安全範圍下限均 ≥ 1）
- 所有 `weight` 加總 > 0（確保加權隨機可正規化；GDD §4.4 隱含）
- 公會等級過濾後若剩餘記錄為空，FT-01 §4.4 以 D 階 fallback（`Debug.LogError`）；實務上公會 Lv1 `maxRecruitableRank = D`，D 權重 40 不會歸零

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `rank` | `AdventurerRankUtil.RankIndex`（共用 helper，F=0…S=6） | 弱約束（parser 不檢查；FT-01 §4.4 以 `RANK_INDEX(kvp.Key)` 做數值比較） |

## 變更注意事項

- DataManager 於啟動時載入 CSV；修改後需重啟或 domain reload 才生效
- 調整 weight 分布時，需搭配 FT-06 `GetMaxRecruitableRank()` 上限評估玩家實際可見的階級比例（公會 Lv1 僅能出 D，Lv2 出 D/C，依此類推）
- 新增階級（如 SS）：需確認 `AdventurerRankUtil.RankIndex` 支援該值，且 FT-01 §3.4 老手池規則需同步更新
- 移除現有階級：確保 FT-01 §4.4 `fallback` 邏輯不依賴被移除的 rank 值；Data-Specs 與 GDD §3.4 需同步

## 範例

```csv
# === 老手池階級加權隨機設定 ===
# 對齊 FT-01 §7.3 Jam 預設值；rank 為 string PK（階級符號）

rank,D,C,B,A,S
weight,40,30,18,9,3
```

（欄位一列；5 筆記錄對應 D/C/B/A/S 五個老手階級；數值來自 GDD §7.3 Jam 預設；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引

| 階級（rank） | 預設 weight | 安全範圍 | 設計意圖（來源：GDD §7.3） |
|---|---|---|---|
| D | 40 | 30 ~ 50 | D 階為老手池基礎盤，權重最高確保早期玩家有穩定供給 |
| C | 30 | 20 ~ 40 | 中階主力，與 D 階比例決定過渡期體感 |
| B | 18 | 10 ~ 25 | 高階起點，開始有稀缺感 |
| A | 9 | 5 ~ 15 | 過高會讓 A 階太容易取得，削弱高階冒險者的稀缺感 |
| S | 3 | 1 ~ 5 | 過高破壞 S 階的傳奇感；建議保持個位數 |

> 調整時驗算：`D/(D+C+B+A+S)` 為 D 階名義機率（公會開放全階級時）；加入公會等級過濾後有效比例會重新正規化。
