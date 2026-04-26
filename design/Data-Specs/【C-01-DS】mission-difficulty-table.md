# 【C-01-DS】MissionDifficultyTable

難度數值查找表：集中所有「按難度查找」的數值（基礎報酬 / 基礎時長 / 基礎死亡率 / 陣營加分），9 行覆蓋 F~SSS 全難度，跨 C-01 / FT-02 / FT-09 共用消費。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/MissionDifficultyTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-01 MissionDatabase.RegisterTables()（GDD §3.1 / §6.1）
- **資料類別**：`TheGuild.Gameplay.Mission.MissionDifficultyData`
- **讀取 API**：
  - `DataManager.Get<MissionDifficultyData>(difficulty)` — 單筆（difficulty 為 string PK）
  - C-01 公開封裝 API：`GetBaseReward(string difficulty)`、`GetBaseDuration(string difficulty)`、`GetBaseDeathRate(string difficulty)`、`GetFactionScoreDelta(string difficulty)`（GDD §3.3）
- **消費者**：
  - C-01 MissionDatabase：`baseReward` / `baseDuration` 供 P-02 / FT-04 / FT-05 消費（GDD §3.1）
  - FT-02 Mission Dispatch：`baseDeathRate` 套用死亡率公式（GDD §3.1 / FT-02 §3.4）
  - FT-04 Outcome Resolution：`baseReward` 供 C-05 `on_success_gold_bonus` 計算（GDD §6.2）
  - FT-09 Faction Story：`factionScoreDelta` 計算陣營加分（GDD §3.1 / FT-09 §3.3.2）
  - P-02 Main UI：顯示任務時長與報酬（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `difficulty` | `string` (PK) | ✓ | F/E/D/C/B/A/S/SS/SSS | 難度識別符（9 種，全覆蓋） |
| `baseReward` | `int` | ✓ | > 0 | 基礎報酬（金幣） |
| `baseDuration` | `int` | ✓ | > 0 | 基礎時長（分鐘）；護送時長 = `baseDuration × random(ESCORT_DURATION_MULTIPLIER_MIN, ESCORT_DURATION_MULTIPLIER_MAX)`（GDD §4.1） |
| `baseDeathRate` | `float` | ✓ | [0.0, 1.0] | 基礎死亡率；FT-02 §3.4 套用；超出範圍時 clamp 並 `Debug.LogError`（GDD §5.1） |
| `factionScoreDelta` | `int` | ✓ | ≥ 0 | 任務成功且 `factionID != 0` 時對應陣營的加分值；`< 0` 時 `Debug.LogError` 並視為 `0`（GDD §3.1 / §5.1） |

## 約束 / 不變量

- 9 行必須全部覆蓋 F / E / D / C / B / A / S / SS / SSS；缺任一行時 `Debug.LogError`，缺失難度的 API 回傳 `0` / `0.0`（GDD §5.1）
- `baseDeathRate` 必須在 `[0.0, 1.0]`；違規時 `Debug.LogError` 並 clamp（GDD §5.1）
- `factionScoreDelta` 必須 ≥ 0；違規時 `Debug.LogError` 並視為 `0`（GDD §3.1 / §5.1）
- 同一 `difficulty` 在 CSV 重複：後者覆蓋前者，`Debug.LogWarning`（GDD §5.3）

## Cross-ref

無

## 變更注意事項

- 修改 `baseDeathRate` 需搭配 FT-02 §3.4 / §7 同步校準（GDD §7.2）
- 修改 `factionScoreDelta` 需搭配 FT-09 §7 / `StoryStageTable.scoreThreshold` 同時校準（GDD §7.2）
- 修改 `baseReward` 影響整體金幣流入速度，需搭配 Commission Flow 傭金率評估（GDD §7.2）
- 修改 `baseDuration` 時注意護送時長為 `baseDuration × 3~5 倍`（GDD §7.2 / §4.1）
- 本表由 C-01 owner；跨系統（FT-02 / FT-09）調整任何欄位需同步 C-01 §3.1 / FT-02 §3.2 / FT-09 §3.2.3 三處消費端說明（data-index.md §已知歸屬注意事項 6）

## 範例

```csv
# === MissionDifficultyTable 任務難度數值 ===
# 資料來源：GDD §3.1 Game Jam 初始資料；baseDuration 單位為分鐘

difficulty,F,E,D,C,B,A,S,SS,SSS
baseReward,30,80,200,400,600,800,1200,2000,3000
baseDuration,5,12,30,60,480,840,1200,1800,3240
baseDeathRate,0.02,0.06,0.10,0.13,0.18,0.40,0.50,0.60,0.70
factionScoreDelta,1,1,2,3,5,8,13,20,30
```
（數值直接取自 GDD §3.1 Game Jam 初始資料表）

## 附錄

### 安全範圍與調參指引

| 欄位 | 安全範圍 | 備注（來源 GDD §7.2） |
|---|---|---|
| `baseDeathRate` | [0.0, 1.0]（強制）；A/SS 為設計高尖峰 | 與 FT-02 §3.4 / §7 同步校準 |
| `factionScoreDelta` | ≥ 0；A 以上設計為指數遞增 | 與 FT-09 §7 / `StoryStageTable.scoreThreshold` 校準 |
