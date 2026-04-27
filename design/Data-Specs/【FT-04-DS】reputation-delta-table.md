# 【FT-04-DS】ReputationDeltaTable

任務難度對應的聲望變化基礎值表，供 FT-04 Outcome Resolution §3.6 `ApplyBaseReputationDelta` 結算時查詢。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/ReputationDeltaTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-04 Outcome Resolution 初始化階段（依 F-01 DataManager bootstrap 規範，FT-04 §6.1 `GetTable<ReputationDeltaTable>()`）
- **資料類別**：`（GDD 未指定）`
- **讀取 API**：`DataManager.GetTable<ReputationDeltaData>()` 取整張表，再以 `difficulty` 字串（如 `"B"`）索引單筆（FT-04 §6.1 `GetTable<ReputationDeltaTable>()`、§3.6 `ReputationDeltaTable[outcome.missionDifficulty]`）
- **消費者**：
  - FT-04 Outcome Resolution：`ApplyBaseReputationDelta(outcome)` 中以 `outcome.missionDifficulty` 為 key 查詢（FT-04 §3.2 step 6、§3.6）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `difficulty` | `string` (PK) | ✓ | F / E / D / C / B / A / S / SS / SSS | 任務難度等級；語義標籤非 FK，string 型別原則見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md) |
| `successDelta` | `int` | ✓ | `[+1, +30]`（安全範圍） | 任務成功時聲望變化量；設計意圖為正整數；符號異常時 FT-04 發 `LogWarning` 但仍執行（FT-04 §5.1） |
| `failDelta` | `int` | ✓ | `[-30, -1]`（安全範圍） | 任務失敗時聲望變化量；設計意圖為負整數；符號異常時 FT-04 發 `LogWarning` 但仍執行（FT-04 §5.1） |

> `difficulty` 為語義標籤（如 `"B"`），不綁定 int ID；與 `MissionDifficultyTable.difficulty` 值域對齊，但 parser 不強制檢查（弱約束）。

## 約束 / 不變量

- 必須覆蓋全部 9 個難度等級（F, E, D, C, B, A, S, SS, SSS）；缺少任何一行時 FT-04 `Debug.LogError` 並 fallback 回傳 `(successDelta=0, failDelta=0)`（FT-04 §5.1）
- `successDelta` 設計意圖為正整數，`failDelta` 為負整數；符號異常僅警告不修正
- `successDelta + failDelta` 算術平均建議維持**接近 0 或略負**（Game Jam 初始值約 `-1.2`）（FT-04 §7.2）
- 相鄰難度 `successDelta` 差距建議 `1~2`，`failDelta` 差距建議 `2~3`（FT-04 §7.2）
- `difficulty` 為非空語義標籤，PK 值永遠不等於 `""`（string null sentinel，見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `difficulty` | `MissionDifficultyTable.difficulty`（C-01）| 弱約束（parser 不檢查；值域須人工對齊） |

## 變更注意事項

- 修改後需在 Unity Editor 重新匯入 CSV 或重啟遊戲才生效；FT-04 不額外快取（FT-04 §7.4）
- 調整本表時須連動確認 F-03 `AddReputation` 破產警告門檻與 FT-06 `GuildLevelTable` 公會升級門檻節奏仍合理（FT-04 §7.2 調整準則）
- 新增難度等級（如新增 `SSSS`）須同步更新 `MissionDifficultyTable` 與 `MissionDifficultyUtil.DifficultyIndex`

## 範例

```csv
# === ReputationDeltaTable ===
# 任務難度聲望變化表，對齊 FT-04 §3.6 Game Jam 初始值
# A 難度 successDelta（+6）低於 B 難度（+8）為刻意設計，見 FT-04 §7.2 設計意圖註記

difficulty,F,E,D,C,B,A,S,SS,SSS
successDelta,3,4,5,6,8,6,8,10,12
failDelta,-1,-2,-3,-4,-6,-8,-10,-12,-15
```

（每個欄位一列；9 筆難度記錄，數值對齊 FT-04 §3.6 Game Jam 初始資料；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

**安全範圍與調參指引**（FT-04 §7.2）

| 欄位 | Game Jam 初始值 | 安全範圍 |
|------|--------------|---------|
| `successDelta` | F=3, E=4, D=5, C=6, B=8, A=6, S=8, SS=10, SSS=12 | `[+1, +30]` |
| `failDelta` | F=-1, E=-2, D=-3, C=-4, B=-6, A=-8, S=-10, SS=-12, SSS=-15 | `[-30, -1]` |

A 難度 `successDelta = +6 < B 難度 +8` 為刻意設計（非 typo）：A 階任務對高等公會為常規，若給爆量聲望會破壞中後期節奏；B 階對中等公會是「踮腳挑戰」，略高回報鼓勵跨階接單。若 playtest 失衡可調整為 A `successDelta = +9~+10`。詳見 FT-04 §7.2。
