# 【C-02-DS】RecruitCostTable

冒險者招募費用表：定義各階級冒險者的招募金幣費用與聲望門檻，7 行覆蓋 F~S 全階級。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/RecruitCostTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-02 AdventurerManagement（DataManager.GetAll<RecruitCostData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Adventurer.RecruitCostData`
- **讀取 API**：
  - `DataManager.Get<RecruitCostData>(rank)` — 單筆（rank 為 string PK）
- **消費者**：
  - FT-01 Recruitment：招募前驗證 `reputationReq`（當前聲望 ≥ 門檻才允許招募），呼叫 F-03 扣除 `cost`（GDD §3.3 / §6.2 / data-index.md §已知歸屬注意事項 2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rank` | `string` (PK) | ✓ | F/E/D/C/B/A/S | 冒險者階級識別符 |
| `cost` | `int` | ✓ | ≥ 0 | 招募費用（金幣）；F/E 為 0（新手自薦，免費）（GDD §3.3） |
| `reputationReq` | `int` | ✓ | ≥ 0 | 招募所需最低聲望；F/E/D 為 0（無聲望門檻）（GDD §3.3） |

## 約束 / 不變量

- 7 行必須全部覆蓋 F / E / D / C / B / A / S（GDD §3.3）
- F / E 階 `cost = 0`、`reputationReq = 0`（新手自薦，免費且無聲望門檻）（GDD §3.3）
- D~S 階費用由 F-03 Resource Management 扣除；聲望門檻由 FT-01 Recruitment 驗證（GDD §3.3）
- `cost` 與 `reputationReq` 均不得為負（GDD 未指定錯誤處理行為，視設計意圖不應出現）

## Cross-ref

無

## 變更注意事項

- 修改後需 domain reload 才生效
- 調整 `cost` 影響玩家整體金幣支出節奏，需搭配名冊容量與任務報酬評估（GDD §7.2）
- 調整 `reputationReq` 影響聲望系統的意義感（GDD §7.2）

## 範例

```csv
# === RecruitCostTable 招募費用 ===
# F/E 免費；D~S 費用由 F-03 扣除，聲望門檻由 FT-01 驗證（GDD §3.3）

rank,F,E,D,C,B,A,S
cost,0,0,100,250,500,1000,2000
reputationReq,0,0,0,20,50,80,100
```
（數值直接取自 GDD §3.3 設計資料）

## 附錄

### 安全範圍與調參指引（來源 GDD §7.2）

| 參數 | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|
| D 階 `cost` | 100 | 50 ~ 200 | 中階冒險者入場費；過低讓玩家跳過新手期，過高阻礙公會發展 |
| S 階 `cost` | 2000 | 1000 ~ 3500 | 頂階稀缺感；應高於玩家達 S 難度前的存款預期 |
| C 階 `reputationReq` | 20 | 10 ~ 40 | 中階冒險者聲望門檻；過高讓玩家卡關，過低聲望系統失去意義 |
