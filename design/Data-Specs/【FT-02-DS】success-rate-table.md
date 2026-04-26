# 【FT-02-DS】SuccessRateTable

依 rankDiff（冒險者階級索引減任務難度索引，clamp 至 -3 ~ +3）查表取得基礎成功率，作為 FT-02 `CalculateRates` 的起點輸入。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/SuccessRateTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-02 MissionDispatch 初始化流程（GDD §6.1 依賴 F-01 DataManager 載入；RegisterTables 具體位置 GDD 未明示）
- **資料類別**：`TheGuild.Gameplay.Dispatch.SuccessRateData`
- **讀取 API**：`DataManager.Get<SuccessRateData>(rankDiff.ToString())`（§3.4 step 2 / §4.2 Step 1）
- **消費者**：
  - FT-02 MissionDispatch：`CalculateRates(instanceID, missionID)` 中以 `rankDiff.ToString()` 查詢 `baseSuccess`（§3.4 step 2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `rankDiff` | `int` (PK) | ✓ | -3 ~ +3（7 個整數值） | 冒險者階級索引 − 任務難度索引；由呼叫端 clamp 後查表（§3.3 / §4.1） |
| `successRate` | `float` | ✓ | [0, 1] | 基礎成功率；載入時若超出範圍 `Debug.LogWarning` 並 clamp 至 `[0, 1]`（§5.1） |

## 約束 / 不變量

- `rankDiff` 取值涵蓋 -3 / -2 / -1 / 0 / +1 / +2 / +3 共七筆（GDD §3.1；§5.1：缺少任一值 → `Debug.LogError`，該 rankDiff 查詢回傳 `0.0`）
- `rankDiff` PK 唯一性：每個整數值恰一筆記錄
- `successRate ∈ [0, 1]`（GDD §5.1：載入時超出範圍 → `Debug.LogWarning` + clamp）

## Cross-ref

無（`rankDiff` 為 FT-02 `CalculateRates` 派生的整數差值，不直接引用其他表欄位）

## 變更注意事項

- DataManager 於啟動時載入 CSV；修改後需重啟或 domain reload 才生效
- 調整 `successRate` 時，需搭配 FT-03 NPC Decision `ACCEPTANCE_THRESHOLD`（0.25）評估配對接受率（§7.4 調整原則）
- 尤其注意 rankDiff=-1 + 職業擅長 `STRONG_TYPE_BONUS`（+0.20）後的有效成功率，GDD §7.4 預期約 0.55（§7.1 rankDiff=-1 安全範圍說明）
- 調整任一值均影響 AC-MD2-01 ~ AC-MD2-03 驗收標準，需同步更新測試期望值（GDD §8）
- 為唯讀查找表，不影響存檔結構

## 範例

```csv
# === 成功率查表 ===
# 對齊 GDD FT-02 §3.1 Game Jam 初始資料；rankDiff 為 int PK，-3 ~ +3 共 7 筆

rankDiff,-3,-2,-1,0,1,2,3
successRate,0.10,0.20,0.35,0.55,0.70,0.85,0.95
```

（每個欄位一列；7 筆記錄對應 rankDiff -3 ~ +3，PK 含負數整數；數值來自 GDD §3.1 Game Jam 初始資料；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引

| rankDiff | 預設 successRate | 安全範圍 | 設計意圖（來源：GDD §7.1） |
|---|---|---|---|
| +3 | 0.95 | 0.90 ~ 0.99 | 碾壓任務的安全感；不設 1.0 保留微小失敗可能性 |
| +2 | 0.85 | （GDD 未指定） | — |
| +1 | 0.70 | （GDD 未指定） | — |
| 0 | 0.55 | 0.40 ~ 0.65 | 同階匹配基準線；過高讓派遣無風險，過低讓同階任務也令人卻步 |
| -1 | 0.35 | 0.25 ~ 0.45 | 低一階挑戰感；搭配職業擅長 +0.20 後應可拉回 0.55 附近，讓「對的人接對的任務」有意義 |
| -2 | 0.20 | （GDD 未指定） | — |
| -3 | 0.10 | 0.05 ~ 0.15 | 極端錯配懲罰；過低接近必敗，過高讓玩家產生賭博心態 |

> 調整時驗算：successRate(-1) + `STRONG_TYPE_BONUS`(0.20) 預期約 0.55（GDD §7.4 調整原則）。
