# 【F-03-Specs】BankruptcyThresholdTable

> **⚠️ Phase 2 已 deprecated(2026-04-26 design-review C4 修補)**:破產警告期長度改由 FT-07 預備金保險櫃等級透過 `F-03.SetBankruptcyWarningDuration()` 主動推送(見 F-03 §3.5 / §3.6 / §4.6 + FT-07 §3.3 / §4.4-4.5)。本表 `warningDurationSec` 欄位**不再 runtime 查詢**;F-03 不主動載入本表。
>
> 本規格保留為**設計參考**,記錄 Phase 1 的「依聲望段查倒數」設計意圖。Phase 2 玩法以 FT-07 保險櫃投資緩解破產壓力為主軸。
>
> **DataManager 載入清單**:Phase 2 應從 F-03 `ResourceManagement.RegisterTables()` 移除本表;若需保留聲望段資訊,改用 `ReputationLabelTable`(僅 label 文字,無 warningDurationSec)。
>
> **Phase 2 落地紀錄（2026-04-26）**：F-03 FSD §8.3 D2 落地完成。`ResourceManagement.RegisterTables()` 整個 method 已移除（無其他註冊需要保留）；`BankruptcyThresholdData.cs` 與 `.meta` 已刪除；`LookupWarningDuration` method 已刪除。本表保留為設計歷史參考，不再對應任何 runtime 程式碼。下方「基本資訊」欄位中關於註冊位置、資料類別、讀取 API 的描述為 Phase 1 歷史，僅作參考。

依當前聲望決定破產警告期長度的查找表(Phase 1 legacy)。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/BankruptcyThresholdTable.csv`
- **解析方式**：`CsvParser.Parse`（一般資料表，第一欄為 PK）
- **註冊位置**：F-03 `ResourceManagement.RegisterTables()`（`RuntimeInitializeOnLoadMethod`）
- **資料類別**：`TheGuild.Gameplay.Resources.BankruptcyThresholdData`
- **讀取 API**：`DataManager.Instance.GetWhere<BankruptcyThresholdData>(predicate)`
- **消費者**(已 deprecated):
  - ~~F-03 ResourceManagement:`LookupWarningDuration(reputation)` 依 `reputation ∈ [reputationMin, reputationMax]` 取對應 `warningDurationSec`~~ — **Phase 2 已移除**:F-03 改為被動接收 `SetBankruptcyWarningDuration(int)` 推送(由 FT-07 預備金保險櫃等級觸發,F-03 §3.5 / §3.6 / §4.6 + FT-07 §3.3 / §4.4-4.5);本 runtime 用語為 Phase 1 舊版行為

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| ID | int | ✓ | ≥1 | 帶段流水號（PK），僅供識別與排序 |
| reputationMin | int | ✓ | -100~100 | 區間下限（含） |
| reputationMax | int | ✓ | -100~100 | 區間上限（含） |
| warningDurationSec | long | ✓ | >0 | 該區間對應的破產警告期秒數 |

> 說明：`ID` 為 PK 但不綁定到 `BankruptcyThresholdData`（該類別只有 `reputationMin` / `reputationMax` / `warningDurationSec` 三個欄位）。`ID` 純供 CSV 內部索引、debug log、設計師排序用。

## 約束 / 不變量

- **`reputationMin ≤ reputationMax`**（單列內）
- **區間不應重疊**：若多列覆蓋同一個 `reputation`，`GetWhere` 回傳第一個符合的，後者被忽略
- **建議完整覆蓋 `[REPUTATION_MIN, REPUTATION_MAX]`**（即 -100~100）：若有缺口，落入缺口的 `reputation` 會 fallback 到 `DefaultWarningDurationSec = 86400` 並產生 error log
- **`reputationMin` / `reputationMax` 應落在 `[REPUTATION_MIN, REPUTATION_MAX]` 區間內**（與 SystemConstants 的聲望上下限對齊）
- `warningDurationSec` 單位**只能是秒**（見 memory `feedback_time_units.md`）

## Cross-ref

| 欄位 | 引用 | 引用方式 |
|---|---|---|
| reputationMin / reputationMax | `SystemConstants.REPUTATION_MIN` / `REPUTATION_MAX` | 數值區間應落在常數定義範圍內（弱約束，parser 不檢查） |

## 變更注意事項

- 修改後重啟遊戲生效；不影響存檔
- 增刪區間會立即影響所有玩家的破產警告期長度（線上熱更新時要小心）
- 改 PK（`ID`）不影響邏輯，但會擾亂 debug log 的對映關係

## 範例

```csv
# === 破產警告期查找表 ===
# 依當前聲望決定金幣低於門檻時的警告期長度（秒）
# 區間 inclusive，須完整覆蓋 -100~100，避免落入 fallback (86400 秒)

ID,reputationMin,reputationMax,warningDurationSec
# --- 負聲望段：警告期短，玩家壓力大 ---
1,-100,-1,86400
# --- 正聲望段：警告期遞增，給玩家更多翻身空間 ---
2,0,29,43200
3,30,59,172800
4,60,79,259200
5,80,100,604800

# 下面這條是測試 fallback 行為用（超出聲望上限），實機請勿開啟
#6,101,200,999
```
