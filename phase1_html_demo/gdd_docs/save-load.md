# Save / Load（存檔系統）



> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 1 — 離線進行，回來收結果
> **Jam 版範圍**：MVP，完整實作

## 概覽

**Save / Load（存檔系統）** 是遊戲狀態的唯一持久化入口。它將所有有狀態系統的運行資料序列化為一個 JSON 物件，主要透過 `localStorage` 自動儲存；同時提供 **File 匯出/匯入**功能，讓玩家可將存檔備份為本機 `.json` 檔，或從檔案恢復，以對抗瀏覽器清除快取的風險。

本系統處理四件事：（1）**自動存檔**——頁面隱藏或關閉前自動觸發，無需玩家操作；（2）**讀取還原**——遊戲啟動時讀取 `localStorage`，若不存在則初始化新存檔；（3）**File 匯出**——玩家手動觸發，將目前存檔匯出為 `.json` 檔下載；（4）**時間戳保護**——存入目前時間，讀取時驗證是否合理，防止系統時鐘被竄改造成異常離線補算。

沒有這個系統，遊戲每次重開都從零開始，放置型核心完全失去意義。

## 玩家幻想

Save / Load 服務的玩家幻想是：**「你的公會是你的，不是瀏覽器的。」**

最好的存檔系統是讓玩家感覺不到它的存在——關掉遊戲，下次回來一切都在。但當玩家意識到「資料會不會不見」的那一刻，這個系統就必須給出安全感：一個清楚的「備份存檔」按鈕，一個可以下載帶走的 `.json` 檔。

匯出功能不只是技術備份，也是一種**擁有感**的延伸——你建立的公會可以被帶到任何地方、任何裝置，不被某個瀏覽器的資料夾綁架。對一個用了幾天心血培養的公會來說，這份掌控感比任何遊戲功能都更讓人安心。

## 詳細設計

### 核心規則

**四個操作**

```
save()   — 序列化目前狀態 → JSON.stringify → localStorage.setItem
load()   — localStorage.getItem → JSON.parse → 注入各系統
export() — 序列化 → Blob → 觸發瀏覽器下載 .json 檔
import() — 讀取玩家選擇的 .json 檔 → 解析 → 驗證 → 注入各系統（等同 load）
```

**自動存檔觸發點**

```
document.visibilitychange（visible → hidden）→ save()
window.beforeunload → save()（不保證總是執行，雙重保險）
```

**讀取流程**

```
1. 讀取 localStorage['the-guild-save']
2. 若不存在 → initNewGame() → 建立空白狀態後 save()
3. 若存在 → JSON.parse → validateSave(data) → injectState(data)
4. validateSave 驗證：
   a. saveFmt 版本號相符（不符則提示玩家）
   b. savedAt <= Date.now()（時鐘未被調到未來，違規則記錄警告）
   c. 所有必要欄位存在（缺失欄位以預設值補齊，不直接報錯）
5. injectState：
   a. resourceManager.setState(data.resources)
   b. missionDispatch.setActiveMissions(data.activeMissions)
   c. outcomeResolution.setPendingResults(data.pendingResults)
   d. adventurerManager.setState(data.roster, data.candidatePool)
```

### 存檔資料結構

```typescript
interface SaveData {
  saveFmt:        number             // 存檔版本號（目前：1）
  savedAt:        number             // 存檔時的 Unix 毫秒時間戳（反作弊 + 離線計算基準）
  resources:      ResourceState      // { gold: number, reputation: number }
  activeMissions: DispatchRecord[]   // 進行中任務（含 endTimestamp，離線補算必要欄位）
  pendingResults: SettlementRecord[] // 未讀結算清單
  roster:         Adventurer[]       // 冒險者名冊
  candidatePool:  Candidate[]        // 當前候選池
}
```

**localStorage key**：`'the-guild-save'`（單一 key，整個遊戲狀態一次讀寫）

### 存檔時序

**存檔（save）**

```
1. tickSystem.stop()            ← 暫停輪詢（time-tick.md 合約）
2. data = collectState()        ← 從各系統讀取當前狀態
3. data.savedAt = Date.now()    ← 記錄存檔時間
4. localStorage.setItem('the-guild-save', JSON.stringify(data))
5. tickSystem.start()           ← 恢復輪詢
```

**讀取（load）**

```
1. raw = localStorage.getItem('the-guild-save')
2. 若 null → initNewGame() → 結束
3. data = JSON.parse(raw)
4. validateSave(data)           ← 驗證版本 + 時間戳 + 欄位完整性
5. injectState(data)            ← 注入各系統
6. tickSystem.scanNow()         ← cold start 補算離線任務（time-tick.md 合約）
7. tickSystem.start()           ← 啟動定期輪詢
```

**File 匯出（export）**

```
1. data = collectState()
2. data.savedAt = Date.now()
3. blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
4. 建立臨時 <a> 元素，觸發下載，檔名：the-guild-save-[YYYY-MM-DD].json
```

**File 匯入（import）**

```
1. 觸發 <input type="file"> 選檔對話框
2. 讀取 FileReader → text → JSON.parse
3. validateSave(data)           ← 與 load() 相同驗證
4. localStorage.setItem('the-guild-save', raw)  ← 覆寫 localStorage
5. injectState(data)            ← 注入各系統（等同重新讀取）
6. tickSystem.scanNow() → tickSystem.start()
```

### 與其他系統的互動


| 系統                           | 方向      | 操作                                                                    |
| ---------------------------- | ------- | --------------------------------------------------------------------- |
| **Resource Management（4）**   | 讀取 + 寫入 | `getState()` 取 `ResourceState`；`setState()` 還原                        |
| **Mission Dispatch（3）**      | 讀取 + 寫入 | `getActiveMissions()` 取 `DispatchRecord[]`；`setActiveMissions()` 還原   |
| **Outcome Resolution（7）**    | 讀取 + 寫入 | `getPendingResults()` 取 `SettlementRecord[]`；`setPendingResults()` 還原 |
| **Adventurer Management（2）** | 讀取 + 寫入 | `getState()` 取 `{ roster, candidatePool }`；`setState()` 還原            |
| **Time / Tick System（9）**    | 呼叫      | `stop()` 存檔前；`start()` / `scanNow()` 讀取後（依 time-tick.md 合約）           |


## 公式

### 存檔大小估算

```
ResourceState:       ~50 bytes
DispatchRecord:      ~300 bytes × max 15 筆  =  ~4,500 bytes
SettlementRecord:    ~200 bytes × max 50 筆  = ~10,000 bytes（離線結果積累）
Adventurer:          ~150 bytes × max 15 筆  =  ~2,250 bytes
Candidate:           ~150 bytes × max  4 筆  =    ~600 bytes
版本 / 時間戳 / 其他:                          =    ~100 bytes

總計（最壞情況）：≈ 17,500 bytes ≈ 17 KB
```

> localStorage 上限約 5MB（5,120 KB），17 KB 佔用 **0.3%**，完全不需要壓縮。

### 時間戳驗證

```typescript
function validateTimestamp(savedAt: number): 'ok' | 'future' | 'missing' {
  if (!savedAt || typeof savedAt !== 'number') return 'missing'
  if (savedAt > Date.now())                    return 'future'  // 時鐘被調到未來
  return 'ok'
}
```

### 版本號驗證

```typescript
const CURRENT_SAVE_FMT = 1

function validateVersion(saveFmt: number): 'ok' | 'outdated' | 'unknown' {
  if (saveFmt === CURRENT_SAVE_FMT) return 'ok'
  if (saveFmt < CURRENT_SAVE_FMT)  return 'outdated'   // 舊版，嘗試以預設值補缺欄位
  return 'unknown'                                       // 未來版本，無法讀取
}
```

## 極端情況

**1. localStorage 不存在（首次遊玩）**

`getItem` 回傳 `null`，觸發 `initNewGame()`，建立空白 `SaveData`（全部預設值），立即 `save()` 寫入。玩家看到新遊戲畫面。

**2. localStorage 被瀏覽器清除**

下次開啟時 `getItem` 回傳 `null`，等同首次遊玩，進入 `initNewGame()`。UI 提示「未找到存檔，已開始新遊戲。若有備份檔案請使用【讀取存檔】」。

**3. JSON 格式損毀（`JSON.parse` 拋出異常）**

`try/catch` 包住解析，失敗視同存檔不存在，觸發 `initNewGame()`，並輸出錯誤日誌。

**4. 存檔版本 `outdated`（舊版格式）**

逐欄位嘗試還原，缺少的欄位以預設值補齊。不直接覆蓋清空——放置型玩家的進度非常珍貴。

**5. 存檔版本 `unknown`（來自未來版本）**

顯示提示「此存檔來自較新版本的遊戲，無法讀取」。不覆蓋 localStorage，讓玩家決定是否開新檔。

**6. `savedAt > Date.now()`（時鐘作弊 / 時鐘回撥）**

記錄警告日誌，繼續載入存檔，由 Time / Tick System 的 `isLongOffline` 標記處理後果。Jam 版本不封鎖讀取，只記錄。

**7. File 匯入的 JSON 不是合法存檔**

`validateSave` 失敗 → 顯示提示「無效的存檔檔案」，不執行 `injectState`，不覆寫 localStorage，現有遊戲繼續運行。

**8. localStorage 寫入失敗（容量已滿，QuotaExceededError）**

`try/catch` 捕捉，記錄錯誤日誌，提示玩家「存檔失敗，建議立即使用【備份存檔】匯出」。考慮到本遊戲存檔約 17KB，此情況極少出現。

## 依賴關係

**上游依賴（Save / Load 讀寫的系統）**


| 系統                           | 依賴性質 | 介面                                                                                 |
| ---------------------------- | ---- | ---------------------------------------------------------------------------------- |
| **Resource Management（4）**   | 強依賴  | `getState() → ResourceState`；`setState(ResourceState)`                             |
| **Mission Dispatch（3）**      | 強依賴  | `getActiveMissions() → DispatchRecord[]`；`setActiveMissions(DispatchRecord[])`     |
| **Outcome Resolution（7）**    | 強依賴  | `getPendingResults() → SettlementRecord[]`；`setPendingResults(SettlementRecord[])` |
| **Adventurer Management（2）** | 強依賴  | `getState() → { roster, candidatePool }`；`setState({ roster, candidatePool })`     |
| **Time / Tick System（9）**    | 強依賴  | `stop()` / `start()` / `scanNow()`                                                 |


**下游依賴（依賴 Save / Load 的系統）**

無。本系統為持久化終點，不被任何遊戲系統反向依賴。

**依賴方向**

```
Resource Management（4）
Mission Dispatch（3）
Outcome Resolution（7）   ──→  Save / Load（10）  ──→  （持久化終點）
Adventurer Management（2）
Time / Tick System（9）
```

## 調整旋鈕


| 旋鈕                 | 位置             | 目前值                                | 安全範圍 | 說明                                     |
| ------------------ | -------------- | ---------------------------------- | ---- | -------------------------------------- |
| `SAVE_KEY`         | `save-load.ts` | `'the-guild-save'`                 | —    | localStorage 的 key 名稱；更改後舊存檔需遷移，不要輕易修改 |
| `CURRENT_SAVE_FMT` | `save-load.ts` | `1`                                | 只能遞增 | 存檔版本號；每次新增不相容欄位時遞增，永遠不要減少              |
| 匯出檔名格式             | `save-load.ts` | `the-guild-save-[YYYY-MM-DD].json` | —    | 可依需求自訂，不影響功能                           |


> 本系統幾乎沒有需要調整的數值——存檔格式一旦定下就應保持穩定。`CURRENT_SAVE_FMT` 是唯一需要在新版開發時主動管理的旋鈕。

## 視覺／音效需求

[待設計]

## UI 需求

- 主介面（或設定選單）提供兩個按鈕：**【備份存檔】**（匯出 `.json`）、**【讀取存檔】**（匯入 `.json`）
- 存檔失敗時顯示紅色警告提示（非阻擋式 toast 通知）
- 匯入成功後顯示確認訊息：「存檔已還原，遊戲重新載入中」
- 首次遊玩 / 存檔遺失時顯示說明提示：「未找到存檔，已開始新遊戲。若有備份請使用【讀取存檔】」

## 驗收標準

**自動存檔驗收**

- 頁面切換至背景（`visibilitychange`）後，localStorage 存在 `'the-guild-save'` key
- 存入的 `savedAt` 與觸發存檔時的 `Date.now()` 誤差 < 1 秒

**讀取還原驗收**

- 存入 `gold=500, reputation=30`，重新整理頁面後，兩值完全還原
- 存入 2 筆 `activeMissions`，重整後長度為 2，`endTimestamp` 精確還原（誤差為 0）
- 存入 3 筆 `pendingResults`，重整後待讀清單長度為 3
- 存入 5 名冒險者，重整後名冊完全還原，`id`、`rank`、`traitId`、`status` 均一致

**首次遊玩 / 損毀存檔驗收**

- 清空 localStorage 後啟動遊戲，進入新遊戲初始狀態，不崩潰
- 寫入格式錯誤的 JSON 後啟動，進入新遊戲，不崩潰，console 有錯誤日誌

**File 匯出/匯入驗收**

- 點擊匯出後瀏覽器觸發 `.json` 檔下載，內容為有效 JSON，包含所有必要欄位
- 匯出後清空 localStorage，再匯入同一檔案，`gold`、`roster`、`activeMissions` 完全還原
- 匯入非存檔的 JSON 檔，顯示錯誤提示，不崩潰，localStorage 不受影響

**版本驗收**

- `saveFmt` 低於 `CURRENT_SAVE_FMT` 時，讀取成功（缺欄位以預設值補齊），不崩潰
- `saveFmt` 高於 `CURRENT_SAVE_FMT` 時，顯示版本警告，不覆寫存檔

## 待解問題


| 問題                         | 狀態  | 備註                                                                                                          |
| -------------------------- | --- | ----------------------------------------------------------------------------------------------------------- |
| `candidatePool` 的刷新計時如何持久化 | 待確認 | Adventurer Management 有 `REFRESH_INTERVAL`（12 小時），候選池刷新時間是否也需存入 `SaveData`？待 Adventurer Management GDD 最終確認 |
| 存檔遷移策略（`outdated` 版本）      | 暫定  | 目前以「預設值補缺欄位」處理，Jam 版本只有 v1，正式版本發布前需明確遷移函數設計                                                                 |


