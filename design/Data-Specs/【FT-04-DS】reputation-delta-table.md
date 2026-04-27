# 【FT-04-DS】ReputationDeltaTable

依任務難度查詢成功/失敗的基礎聲望變化量，為 FT-04 結算管線的聲望 delta 起算值。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/ReputationDeltaTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：FT-04 OutcomeResolution 的 `RegisterTables()`（FT-04 §6.1）
- **資料類別**：`ReputationDeltaData`（GDD 未指定完整 namespace；FT-04 §7.4）
- **讀取 API**：`DataManager.Get<ReputationDeltaData>(difficulty)`（FT-04 §5.1 edge case 暗示 per-key 查詢；缺失行 → LogError + 回傳 `(0, 0)`）
- **消費者**：
  - FT-04 OutcomeResolution：`ApplyBaseReputationDelta(outcome)` §3.6 — 依 `outcome.missionDifficulty` 查詢 `successDelta` / `failDelta`，condition 特質以 `+=` 疊加後傳入 `F03.AddReputation`

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `difficulty` | `string` | ✓ | `F` / `E` / `D` / `C` / `B` / `A` / `S` / `SS` / `SSS` | PK；任務難度符號（等級符號字串，同 C-01 難度軸） |
| `successDelta` | `int` | ✓ | 整數（成功通常為正；建議 `[+1, +30]`，符號異常 LogWarning 不阻擋、依 CSV 值執行，§5.1） | 任務成功時的基礎聲望增量；FT-04 §3.6 套用，condition 特質可疊加（FT-04 §3.2 步驟 6/7）。GDD §3.6 table A 難度（+6）< B 難度（+8）屬設計刻意，非 typo（見 §7.2） |
| `failDelta` | `int` | ✓ | 整數（失敗通常為負；建議 `[-30, -1]`，符號異常 LogWarning 不阻擋、依 CSV 值執行，§5.1） | 任務失敗時的基礎聲望扣量 |

> `difficulty` 欄值為等級符號字串（`F`~`SSS`），符合全專案 string PK 例外原則（[`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）。

## 約束 / 不變量

- 必須包含 9 筆記錄：`F`、`E`、`D`、`C`、`B`、`A`、`S`、`SS`、`SSS`；缺任意一筆 → `Debug.LogError`，查詢回傳 `(0, 0)`（FT-04 §5.1）
- `successDelta` 通常為正、`failDelta` 通常為負；符號異常時 `Debug.LogWarning`（FT-04 §5.1），但不修正、依 CSV 值執行
- FT-04 不 clamp `reputationDelta`；最終 clamp 由 F-03 `AddReputation` 處理（FT-04 §3.6 / §4.2）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| `difficulty` | C-01 `MissionDifficultyTable.difficulty` 難度字串值域 | 弱約束（parser 不驗證；`outcome.missionDifficulty` 快照自 MissionTemplate，兩處應對齊難度軸定義） |

## 變更注意事項

- 修改後 DataManager 重新載入即時生效（FT-04 不快取）
- 調整 `successDelta` / `failDelta` 影響 FT-06 GuildLevelTable `reputationThreshold` 達成節奏，以及 F-03 破產警告觸發頻率（FT-04 §7.2 調整準則）
- A 難度 `successDelta (+6)` < B 難度 (+8)：GDD §7.2 明確設計意圖，調整前須確認設計師同意（非 bug）

## 範例

```csv
# === 任務結算聲望變化表 ===
# 對齊 FT-04 §3.6 / §7.2；condition 特質以 += 疊加於基礎 delta 之上

difficulty,F,E,D,C,B,A,S,SS,SSS
successDelta,3,4,5,6,8,6,8,10,12
failDelta,-1,-2,-3,-4,-6,-8,-10,-12,-15
```

（每個欄位一列；9 筆記錄，數值對齊 FT-04 §3.6 Game Jam 初始資料；CSV 為 column-based / 轉置格式，規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### 安全範圍與調參指引（FT-04 §7.2）

| 欄位 | Jam 預設範圍 | 安全範圍 | 影響 |
|---|---|---|---|
| `successDelta` | +3 ~ +12 | [+1, +30] | 超過 +30 會讓聲望上升過快，破壞 GuildLevelTable 門檻節奏 |
| `failDelta` | -1 ~ -15 | [-30, -1] | 超過 -30，單次失敗易觸發破產警告，過度懲罰離線結算 |

> 平衡指標：`successDelta + failDelta` 算術平均應維持**接近 0 或略負**（Jam 版算術平均 ≈ +0.11，接近中性）。調整準則見 FT-04 §7.2。
