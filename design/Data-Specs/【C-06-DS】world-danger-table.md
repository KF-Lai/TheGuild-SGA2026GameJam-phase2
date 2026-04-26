# 【C-06-DS】WorldDangerTable

世界危險度資料表：集中所有「按危險度查找」的數值（升級三閘條件 / 任務池難度權重 / 破產債務上限），5 行覆蓋 E / D / C / B / A 全階，方便設計師單檔調整全部跨欄位平衡。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/WorldDangerTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-06 WorldDangerSystem（DataManager.GetAll<WorldDangerData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.World.WorldDangerData`
- **讀取 API**：
  - `DataManager.Get<WorldDangerData>(dangerLevel)` — 單筆（dangerLevel 為 string PK）
  - C-06 公開封裝：`GetDangerData(string dangerLevel)`、`GetMaxDebt()`、`GetPoolWeights()`（GDD §3.4）
- **消費者**：
  - C-06 WorldDangerSystem：升級檢查（timeThreshold / missionCountReq / minDifficulty / factionScoreReq）、推送 maxDebt 至 F-03、發布 OnDangerLevelChanged（GDD §3.3 / §4.2）
  - F-03 Resource Management：被動接收 `SetBankruptcyThreshold(maxDebt)`（C-06 主動推送，F-03 不主動查詢此表）（GDD §6.2）
  - FT-02 Mission Dispatch：`GetPoolWeights()` 決定任務池難度分布偏移（GDD §6.2）
  - P-02 Main UI：顯示當前危險度名稱與說明（GDD §6.2）

## 欄位定義

| 欄位群 | 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|---|
| 主鍵 | `dangerLevel` | `string` (PK) | ✓ | E/D/C/B/A | 危險度識別符；DataManager 載入時統一轉為大寫（GDD §5.1） |
| 顯示 | `name` | `string` | ✓ | — | 顯示名稱 |
| 升級閘 | `timeThreshold` | `int` | ✓ | ≥ 0 | 升至此階所需最低遊戲天數；`0` = 不設時間閘 |
| 升級閘 | `missionCountReq` | `int` | ✓ | ≥ 0 | 進度閘：累積接受任務數門檻；`0` = 不設 |
| 升級閘 | `minDifficulty` | `string` | ✓ | F/E/D/C/B/A/S/SS/SSS | 進度閘：計入 `acceptedMissionCount` 的最低任務難度；不合法值視為 `"F"`（GDD §5.1） |
| 升級閘 | `factionScoreReq` | `int` | ✓ | ≥ 0 | 陣營閘：任意陣營累積分數需達到此值；`0` = 不設此閘（GDD §3.3）|
| 任務池權重 | `weightF_E` | `int` | ✓ | ≥ 0 | F~E 難度合計權重 |
| 任務池權重 | `weightD` | `int` | ✓ | ≥ 0 | D 難度權重 |
| 任務池權重 | `weightC` | `int` | ✓ | ≥ 0 | C 難度權重 |
| 任務池權重 | `weightB` | `int` | ✓ | ≥ 0 | B 難度權重 |
| 任務池權重 | `weightA` | `int` | ✓ | ≥ 0 | A 難度權重 |
| 任務池權重 | `weightS_SSS` | `int` | ✓ | ≥ 0 | S~SSS 難度合計權重 |
| 債務上限 | `maxDebt` | `int` | ✓ | < 0 | 金幣不可低於此值，由 F-03 強制執行；缺失或為 0 時 `Debug.LogError` 並 fallback `-100`（GDD §5.1） |

## 約束 / 不變量

- 5 行必須全部覆蓋 E / D / C / B / A；缺某階時 `Debug.LogError`，升級跳過該階直接嘗試下一階（GDD §5.1）
- E 階三閘均為 0（起始狀態，不需升級條件）；E / D 階 `factionScoreReq = 0`（不設陣營閘）（GDD §3.1）
- 任務池權重任一全為 0 時：`Debug.LogError`，`GetPoolWeights()` 回傳 E 階權重作為 fallback（GDD §5.1）
- `maxDebt` 絕對值應隨危險度遞增（GDD §7.3）
- `weightS_SSS` 在 E / D 階維持 `0`，避免開局出現 S 難度（GDD §7.2）
- `weightF_E` 在 A 末世設為 `0`，強制面對高難度（GDD §7.2）
- 世界危險度只升不降，runtime 不讀取本表以決定降級邏輯（GDD §3.3）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `minDifficulty` | 難度固定索引（F=0…SSS=8） | 弱約束（不合法值視為 "F"，GDD §5.1） |

## 變更注意事項

- 修改後需 domain reload 才生效
- 修改 `maxDebt` 後 C-06 在下次 Start / 升階時重新推送至 F-03；存檔還原後也會推送（GDD §4.3 / §6.4）
- 修改 `factionScoreReq`（C 暗湧）需搭配 FT-09 陣營分數累積速度校準（GDD §7.1）
- 修改 `timeThreshold`（A 末世）決定 Game Jam 體驗節奏上限（GDD §7.1）

## 範例

```csv
# === WorldDangerTable 世界危險度（Game Jam 5 階）===
# timeThreshold 單位為遊戲天數；maxDebt 為負值（金幣下限）

dangerLevel,E,D,C,B,A
name,和平,動盪,暗湧,危局,末世
timeThreshold,0,1,3,7,14
missionCountReq,0,10,15,20,25
minDifficulty,F,F,D,C,B
factionScoreReq,0,0,5,12,20
weightF_E,40,20,10,5,0
weightD,30,35,20,10,5
weightC,20,25,35,25,15
weightB,8,15,25,35,30
weightA,2,5,8,20,35
weightS_SSS,0,0,2,5,15
maxDebt,-100,-500,-1000,-2500,-5000
```
（數值直接取自 GDD §3.1 Game Jam 初始資料表）

## 附錄

### 安全範圍與調參指引（來源 GDD §7.1 ~ §7.3）

| 參數 | 預設值 | 安全範圍 | 影響 |
|---|---|---|---|
| `timeThreshold`（D 動盪） | 1 天 | 0 ~ 3 天 | 過短讓玩家開局即進入壓力；過長缺乏緊張感 |
| `timeThreshold`（A 末世） | 14 天 | 10 ~ 21 天 | 決定 Game Jam 體驗節奏上限 |
| `missionCountReq`（C 暗湧） | 15 | 10 ~ 25 | 進度閘太寬讓玩家被動升危險度，太嚴讓危險度停滯 |
| `factionScoreReq`（C 暗湧） | 5 | 3 ~ 10 | 過低幾乎無意義；過高讓陣營系統成為瓶頸 |
| `factionScoreReq`（A 末世） | 20 | 15 ~ 30 | 應搭配 FT-09 陣營分數累積速度校準 |
