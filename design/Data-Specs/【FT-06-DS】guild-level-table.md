# 【FT-06-DS】GuildLevelTable

依公會聲望查詢等級門檻與對應稱號、招募上限、任務難度上限，為 FT-06 `FindTargetLevel()` 的判定資料源。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/GuildLevelTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-06 GuildCore 的 `RegisterTables()`（FT-06 §6.1 上游依賴：F-01 DataManager `LoadGuildLevelTable()`）
- **資料類別**：`GuildLevelData`（GDD 未指定完整 namespace；FT-06 §3.5）
- **讀取 API**：`DataManager.GetAll<GuildLevelData>()` → 依 `level` 升冪排列供 `FindTargetLevel()` 線性掃描（FT-06 §4.1）；`DataManager.Get<GuildLevelData>(level.ToString())` 供個別等級查詢（FT-06 §3.6 API 內部使用）
- **消費者**：
  - FT-06 GuildCore：`FindTargetLevel(rep)` §4.1 — 從高到低掃描 `reputationThreshold` 判定目標等級
  - FT-06 GuildCore：`GetCurrentTitle()` / `GetMaxRecruitableRank()` / `GetMaxMissionDifficulty()` / `GetMaxDifficulty()` §3.6 — 即時讀 `level[currentLevel]` 對應欄位，**不快取**
  - FT-01 AdventurerRecruitment：`GetMaxRecruitableRank()` §6.1 — 老手邀請可招募的最高冒險者階級（via FT-06 API）
  - FT-03 NPC Decision System：`GetMaxMissionDifficulty()` §3.3 — 自主接單候選任務難度過濾（via FT-06 API）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `level` | `int` | ✓ | 1..5 | PK；公會等級；Lv1 起始，5 筆必須連續（FT-06 §7.1） |
| `reputationThreshold` | `int` | ✓ | 0..INT_MAX | 達到此聲望即判定為本等級；Lv1 固定為 0，後續嚴格升冪（FT-06 §7.1） |
| `title` | `string` | ✓ | 非空 | 公會稱號；UI 顯示用；建議 12 全型字內（FT-06 §7.4） |
| `maxDifficulty` | `string` | ✓ | 依 C-01 難度軸（F/E/D/C/B/A/S/SS/SSS） | **[Deprecated]** legacy 欄位；應保持與 `maxRecruitableRank` 相同值，不再調整（FT-06 §7.1） |
| `maxRecruitableRank` | `string` | ✓ | D/C/B/A/S | 老手招募可邀請的最高冒險者階級；FT-01 `RollVeteranRank()` 使用；值域僅到 S（無 SS/SSS 冒險者，FT-06 §7.1） |
| `maxMissionDifficulty` | `string` | ✓ | 依 C-01 難度軸（D~SS；Jam 預設 Lv5 上限為 SS） | 常規任務可生成的最高難度上限；C-06 池過濾與 P-02 委託板使用（FT-06 §7.1） |

> `maxDifficulty` 為 deprecated legacy 欄位：`GetMaxDifficulty()` 內部呼叫 `GetMaxRecruitableRank()` 並輸出 `[Deprecated] GetMaxDifficulty() — 請改用 GetMaxRecruitableRank()`（FT-06 §3.6）；保留僅為向後相容，新代碼不應讀取此欄。

## 約束 / 不變量

- 必須包含且僅包含 5 筆記錄：`level` 1、2、3、4、5；缺任意一筆 → `Debug.LogError` + `throw`（FT-06 §5.1.3）
- `reputationThreshold[1] == 0`；違反時 `Debug.LogError` + `throw`（FT-06 §5.1.3）
- `reputationThreshold` 嚴格升冪（每級 > 前級，FT-06 §7.4）
- `maxRecruitableRank` 單調不降（D~S，每級 >= 前級，FT-06 §7.4）
- `maxMissionDifficulty` 單調不降（D~SS，每級 >= 前級，FT-06 §7.4）
- `maxDifficulty`（deprecated）應保持與 `maxRecruitableRank` 相同值（FT-06 §7.4）
- `title` 非空（FT-06 §7.4）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `maxRecruitableRank` | C-01 `MissionDifficultyTable.difficulty` 難度字串值域（D~S 子集） | 弱約束（parser 不驗證；值域限 D/C/B/A/S，因冒險者最高階級為 S） |
| `maxMissionDifficulty` | C-01 `MissionDifficultyTable.difficulty` 難度字串值域（D~SS 子集） | 弱約束（parser 不驗證；Jam 版 Lv5 上限 SS） |
| `maxDifficulty` | C-01 `MissionDifficultyTable.difficulty` 難度字串值域 | 弱約束（deprecated；保持與 `maxRecruitableRank` 同值） |

## 變更注意事項

- 修改後 DataManager 重新載入即時生效（FT-06 不快取）
- 調整 `reputationThreshold` 影響玩家達到各等級的節奏，連帶影響 FT-04 `ReputationDeltaTable` 聲望累積效率感（FT-06 §7.1 升級曲線注記）
- 調整 `maxRecruitableRank` 影響 FT-01 老手邀請可招募的冒險者階級上限
- 調整 `maxMissionDifficulty` 影響 C-06 任務池過濾與 P-02 委託板可顯示難度

## 範例

```csv
# === 公會等級表 ===
# PK: level（int 1..5）
# 對齊 FT-06 §3.5 + §7.1 Game Jam 初始資料
# maxDifficulty 為 deprecated legacy，保持與 maxRecruitableRank 同值

level,1,2,3,4,5
reputationThreshold,0,30,80,200,400
title,新手冒險者公會,初階冒險者公會,中階冒險者公會,高階冒險者公會,名聲顯赫的冒險者公會
maxDifficulty,D,C,B,A,S
maxRecruitableRank,D,C,B,A,S
maxMissionDifficulty,D,C,B,A,SS
```

（每個欄位一列；5 筆記錄，數值對齊 FT-06 §3.5 / §7.1 Jam 預設；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-06 §7.1）

| level | reputationThreshold Jam 預設 | 說明 |
|---|---|---|
| 1 | 0 | 固定為 0，不可改（§5.1.3 防禦性驗證） |
| 2 | 30 | （GDD 未指定安全範圍） |
| 3 | 80 | Jam 3h 主流程設計終點；玩家約 3h 可達 |
| 4 | 200 | stretch goal；達成率偏低時可考慮調降 |
| 5 | 400 | stretch goal；可配合外部活動加速聲望累積 |

> 升級曲線差值 = 30, 50, 120, 200（FT-06 §7.1 Jam 版加速進度設計）。`maxDifficulty` 不再調整（deprecated）；若 C-01 難度軸定義改變（如改為數字），`maxRecruitableRank` 與 `maxMissionDifficulty` 需同步調整。
