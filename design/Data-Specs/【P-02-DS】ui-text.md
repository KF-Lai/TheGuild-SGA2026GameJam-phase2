# 【P-02-DS】UIText

所有玩家可見 UI 字串的 key → zhTW / en 多語系映射表；P-02 透過 `UITextLookup(key, fallback)` helper 查詢，新增或修改文字 key 不需改程式碼（§3.9 / §7.3）。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/UIText.csv`
- **解析方式**：`CsvParser.Parse`（column-based / 轉置格式）
- **註冊位置**：P-02 啟動序列 §3.8 step 4（透過 F-01 DataManager 載入）
- **資料類別**：`TheGuild.UI.UITextEntry`（GDD 未指定 FQN；依命名慣例推導）
- **讀取 API**：`DataManager.Get<UITextEntry>(key)` 取得單筆；P-02 封裝為 `UITextLookup(key, fallback)` helper（§3.9）
- **消費者**：
  - P-02 UITextLookup：所有面板字串（按鈕文字、tooltip、面板標題、確認彈窗文字、錯誤提示）§3.9 / §7.3

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `key` | string | ✓ | — | 文字唯一識別 key（PK）；全域唯一，命名慣例 GDD 未指定（gdd-gap） |
| `zhTW` | string | ✓ | — | 繁體中文字串；Jam 版唯一生效語系（§7.5） |
| `en` | string | ✗ | — | 英文字串；Jam 版鎖定 zhTW，此欄 Post-Jam 啟用（§7.5） |

> 動態字串以 placeholder 格式儲存，由 P-02 呼叫端帶入實際值（§3.9）。**gdd-gap**：GDD §3.9 範例使用命名式 `{name}` / `{missionName}`，與 C# `String.Format` 位置式 `{0}` / `{1}` 不符；Jam 版建議統一採位置式並更正 GDD 範例，否則需自訂 template parser。待設計師裁決後對齊 CSV 實檔與呼叫端實作。

## 約束 / 不變量

- `key` 全域唯一；重複 key 在 DataManager 載入時建議發出 LogError（GDD 未明示行為；靜默覆蓋難以維護，應在 F-01 DataManager 載入層實作重複 key 偵測）
- `zhTW` 必須非空；Jam 版所有 key 的 zhTW 欄均需填值（§3.9）
- `en` Jam 版可留空；Post-Jam 多語系啟用後補全（§7.5）
- `UITextLookup` key 不命中時 LogError 並回傳 fallback；fallback 為空時回傳 key 本身（§3.9 / EC-02）
- 全部玩家可見字串必須走此表；程式碼不硬編碼任何 UI 字串（§1）

## Cross-ref

無（key 為全域語義標籤，不引用其他資料表 ID）。

## 變更注意事項

- 修改後即時生效於下次 DataManager 載入（重啟或 domain reload）；runtime 不快取單筆值，每次 UITextLookup 均查詢 DataManager
- 新增 / 修改 key 不需改程式碼（§7.3）；刪除 key 需先全域 grep UITextLookup 呼叫端確認無殘留引用
- 多語系擴充（Post-Jam）需同步 `UITextLookup` 語系選擇邏輯（程式碼異動）
- `en` 欄啟用後需重新校準 `DIALOGUE_LINE_MAX_CHARS`（英文字數約為中文 1.8~2.0 倍，§7.1 注）

## 範例

```csv
# === UIText：UI 固定字串多語系對映表 ===
# Jam 版鎖定 zhTW；en 欄 Post-Jam 補全；key 命名慣例由 P-02 設計師定義（gdd-gap）

key,BTN_CONFIRM,BTN_CANCEL,PANEL_COMMISSION_BOARD,HUD_GOLD_FORMAT,EC_UITEXT_FALLBACK
zhTW,確認,取消,委託板,{0} g,（文字載入失敗）
en,Confirm,Cancel,Commission Board,{0} g,(text load failed)
```
（column-based 轉置格式；每欄一筆 key 記錄；key 名稱與 zhTW / en 值均為格式示範，具體 key 清單由 P-02 設計師填入；規範見 [`.claude/rules/data-files.md`](../../.claude/rules/data-files.md)）

## 附錄

### Phase 標記

- Jam 版鎖定 zhTW；en 欄為 Post-Jam 預留欄位，Jam 版 CSV 可留空但欄位需存在（避免 schema 異動）。
- key 命名慣例為 gdd-gap；GDD §3.9 未定義命名格式，建議補充後統一對齊 CSV 實檔。
