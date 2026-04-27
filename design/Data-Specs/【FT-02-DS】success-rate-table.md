# 【FT-02-DS】SuccessRateTable

依冒險者與任務的階級差（rankDiff）查詢基礎成功率，為 FT-02 `CalculateRates()` 的起算值。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/SuccessRateTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-02 MissionDispatch 的 `RegisterTables()`（FT-02 §6.1）
- **資料類別**：`SuccessRateData`（GDD 未指定完整 namespace；FT-02 §3.1）
- **讀取 API**：`DataManager.Get<SuccessRateData>(rankDiff.ToString())`（FT-02 §5.1 edge case 暗示 per-key 查詢）
- **消費者**：
  - FT-02 MissionDispatch：`CalculateRates(instanceID, missionID)` §3.4 step 2 — `baseSuccess = SuccessRateTable[rankDiff].successRate`（FT-02 §4.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rankDiff` | `int` | ✓ | `-3 ~ +3`（共 7 個整數值） | PK；`clamp(冒險者階級索引 − 任務難度索引, -3, +3)`（FT-02 §3.3） |
| `successRate` | `float` | ✓ | `[0.0, 1.0]` | 基礎成功率；載入時若超出範圍則 `LogWarning` 並 clamp 至 `[0.0, 1.0]`（FT-02 §5.1） |

## 約束 / 不變量

- 必須包含 `rankDiff` 值 -3、-2、-1、0、1、2、3 共 7 筆記錄；缺任意一筆則 `Debug.LogError`，該 rankDiff 查詢回傳 `0.0`（FT-02 §5.1）
- `successRate` 應在 `[0.0, 1.0]`；超出範圍在載入時 clamp，上線前應於 CSV 修正（FT-02 §5.1）

## Cross-ref

無（`rankDiff` 為 runtime 計算值，非 FK 引用）

## 變更注意事項

- 修改 `successRate` 後影響 FT-02 `CalculateRates()` 的基礎值，進而影響：
  - FT-03 NPC Decision `willingnessScore`（`finalSuccessRate - finalDeathRate × DEATH_AVERSION`，FT-02 §7.4）
  - FT-04 Outcome Resolution 結算骰（使用 `ActiveMission.finalSuccessRate` 快照）
- 調整 `rankDiff = -1` 時，須驗算「低一階 + 職業擅長 +0.20」後的有效成功率是否接近 0.55（FT-02 §7.4 原則）
- DataManager 重新載入後即時生效

## 範例

```csv
# === 成功率查詢表 ===
# PK: rankDiff = clamp(冒險者階級索引 - 任務難度索引, -3, +3)
# 對齊 FT-02 §3.1 Game Jam 初始資料（沿用 game-concept）

rankDiff,-3,-2,-1,0,1,2,3
successRate,0.10,0.20,0.35,0.55,0.70,0.85,0.95
```

（每個欄位一列；7 筆記錄，數值對齊 FT-02 §3.1 Jam 預設；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-02 §7.1）

| rankDiff | Jam 預設 | 安全範圍 | 影響 |
|---|---|---|---|
| +3 | 0.95 | 0.90 ~ 0.99 | 碾壓任務的安全感；不設為 1.0 保留微小失敗可能性 |
| +2 | 0.85 | （GDD 未指定） | — |
| +1 | 0.70 | （GDD 未指定） | — |
| 0 | 0.55 | 0.40 ~ 0.65 | 同階匹配的基準線；過高讓派遣無風險，過低讓同階任務令人卻步 |
| -1 | 0.35 | 0.25 ~ 0.45 | 低一階的挑戰感；搭配職業擅長 +0.20 後應可拉回 0.55 附近 |
| -2 | 0.20 | （GDD 未指定） | — |
| -3 | 0.10 | 0.05 ~ 0.15 | 極端錯配的懲罰；過低接近必敗，過高讓賭博心態增加 |

> gdd-gap：rankDiff +2、+1、-2 的安全範圍 GDD §7.1 未指定，需設計師補充後更新本表。
