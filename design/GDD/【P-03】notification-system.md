# 【P-03】Notification System

_建立時間：2026-04-26_
_狀態：設計中_

---

## §1 概覽（Overview）

P-03 Notification System 負責將遊戲事件轉化為玩家可見的通知。系統採雙軌設計：Critical 事件（破產警告、公會升級）以 Toast 彈窗強制呈現；其餘事件（任務結算、招募成功、NPC 自主接單等）寫入可上下滾動的訊息 Log，玩家在方便時查閱。所有通知僅在遊戲視窗可見時顯示，不做 OS 層離線推播。通知文字內容由 `NotificationTemplate` CSV 定義，程式碼為通用渲染引擎。

---

## §2 玩家幻想（Player Fantasy）

玩家打開桌面視窗，第一眼掃向訊息 Log：「我的冒險者回來了嗎？有沒有出什麼事？」滾動幾行，看到任務結算、傷亡報告、或 NPC 自己跑去接了一個危險的委託——這個「偷看結果」的瞬間，是遊戲留存感的核心。Critical Toast 則是真正需要玩家立刻回應的時刻：升級的驚喜、或破產倒數的緊張。

---

## §3 詳細規則（Detailed Rules）

### §3.1 通知架構（Jam 版）

Jam 版採單軌設計：所有事件通知進入訊息 Log，依事件優先級套用不同視覺樣式。職員對話氣泡推播為 Post-Jam 功能（見 §3.5），Jam 版不實作。

### §3.2 訊息 Log 規則

- 最多保留 **100 條**；超出上限時自動移除最舊的一條（FIFO）
- 新通知插入 Log 頂部，舊通知往下捲
- Log 呈現為**可浮動的迷你視窗**（類瀏覽器視窗風格）
  - 底部工作列包含：**最大化**、**最小化**按鈕
  - 最小化：收縮至只顯示底部工作列；點擊還原
  - 最大化：展開至預設最大尺寸（見 §7）
- 初始位置：螢幕左側、其他 UI 元件上方
- **Log 視窗專屬互動**（其他 UI 面板不支援）：
  - 拖曳視窗任意區域以移位
  - 滑鼠移至四角出現縮放控制點，拖曳進行等比縮放

### §3.3 事件分級與視覺樣式

| 優先級 | 事件 | 視覺樣式 |
|---|---|---|
| **Critical** | `OnGameOverPending`、`OnGuildLevelChanged` | 紅底 + 粗體文字 |
| **Important** | `OnMissionResolved`、`OnCommissionSettled` | 一般樣式 |
| **Optional** | `OnRecruitSuccess`、`OnAutoPickup`、`OnCommissionPosted` | 一般樣式，字體較小 |

### §3.4 事件訂閱清單

P-03 透過 EventBus 訂閱以下事件，並依 `NotificationTemplate` CSV 的 `templateID` 渲染對應文字：

| 事件 | 來源 | 優先級 |
|---|---|---|
| `OnGameOverPending` | FT-06 | Critical |
| `OnGuildLevelChanged` | FT-06 | Critical |
| `OnMissionResolved` | FT-04 | Important |
| `OnCommissionSettled` | FT-05 | Important |
| `OnRecruitSuccess` | FT-01 | Optional |
| `OnAutoPickup` | FT-03 | Optional |
| `OnCommissionPosted` | FT-02 | Optional |

### §3.5 職員對話氣泡推播（Post-Jam 保留功能）

> **Jam 版不實作，以下為設計概念存檔。**

- 推播與建築 ID 綁定；`NotificationTemplate` CSV 預留 `buildingID` 欄位
- 當建築有職員（FT-12）指派時，推播以該職員角色的對話氣泡形式呈現
- 氣泡文字對應職員文字資料表（待設計）中特定訊息類型 ID 的對話內容
- 同一職位（建築）的不同職員可填入不同對話，實現個性化推播
- Jam 版退回 §3.3 視覺樣式呈現

---

## §4 公式（Formulas）

P-03 不含數值公式；以下定義優先級指派與通知渲染的查詢邏輯。

### §4.1 優先級指派

```
entry.priority = PriorityTable[eventType]
```

`PriorityTable` 映射關係即 §3.3 表格（Critical / Important / Optional）。

### §4.2 通知文字渲染

```
entry.text = NotificationTemplate[eventType].templateText.Format(eventPayload)
```

- `eventType` → 查 `NotificationTemplate.csv` → 取 `templateID` 與 `templateText`
- `eventPayload` 為事件攜帶參數（冒險者名稱、任務獎勵等），對應 CSV 中預留的佔位符
- 格式化使用 C# String.Format / 字串插值，佔位符由 CSV 定義

### §4.3 Log 容量管理

```
if (log.Count >= LOG_MAX_ENTRIES)
    log.RemoveAt(log.Count - 1)   // 移除最舊
log.Insert(0, newEntry)            // 插入頂部
```

`LOG_MAX_ENTRIES` 預設 100，見 §7。

### §4.4 時間戳記格式化

```
entry.timestamp = FormatGameTime(F02.CurrentGameTime, TIMESTAMP_FORMAT)
```

Token 替換規則（其餘字元視為字面分隔符）：

| Token | 內容 | 格式 |
|---|---|---|
| `MM` | 遊戲月份 | 零補齊兩位（01~12） |
| `DD` | 遊戲日期 | 零補齊兩位（01~31） |
| `hh` | 遊戲小時 | 零補齊兩位，24H 制（00~23） |
| `mm` | 遊戲分鐘 | 零補齊兩位（00~59） |

`TIMESTAMP_FORMAT` 由全域設定 CSV 讀取（見 §7）；預設值 `MM/DD hh:mm`。

---

## §5 邊界情況（Edge Cases）

| 情況 | 行為 |
|---|---|
| Log 已達 100 條時收到新事件 | 移除最舊一條（index 最大），新通知插入頂部，顯示不中斷 |
| 同一幀收到多個事件（如任務結算 + 委託結算同時觸發） | 依 EventBus 發布順序逐條插入，各自獨立一條 Log |
| `NotificationTemplate.csv` 中找不到對應 `eventType` | 插入一條 fallback 文字：`「[{eventType}] 通知模板缺失」`，優先級降為 Optional |
| Log 視窗被最小化時收到新通知 | 正常寫入 Log，最小化狀態不中斷；工作列不額外閃爍 |
| Log 視窗拖曳至螢幕外（部分超出邊界） | 不做強制限制，允許玩家自由定位；重啟後位置透過 FT-10 還原 |
| Log 視窗縮放後超出最小可讀尺寸 | Clamp 至 `LOG_MIN_WIDTH × LOG_MIN_HEIGHT`（見 §7），不拋例外 |
| 遊戲啟動時大量事件立即觸發（如離線結算） | 同「多事件同幀」規則，逐條插入；若超過 100 條，最舊者被自動移除，只保留最新 100 條 |
| `eventPayload` 欄位缺失或 null | 佔位符以空字串替換，輸出 `Debug.LogError`（含 eventType 與缺失欄位名稱），不拋例外，不影響遊戲運行 |

---

## §6 依賴關係（Dependencies）

### P-03 依賴的系統

| 系統 | 依賴內容 |
|---|---|
| EventBus（Core） | 訂閱所有事件（§3.4 清單）；EventBus 必須在 P-03 初始化前就緒 |
| F-01 Data Manager | 載入 `NotificationTemplate.csv` 及全域設定 CSV（含 `TIMESTAMP_FORMAT`）；載入失敗則退回 fallback |
| F-02 Time System | 讀取當前遊戲時間（月/日/時/分），用於 Log 條目時間戳記格式化（§4.4） |
| FT-10 Save/Load | 持久化 Log 視窗位置與視窗狀態（最大化/最小化）；讀取失敗時使用預設位置 |
| P-01 Desktop Transparent Window | Log 視窗 UI 元素須在 P-01 Hit-Test 可命中範圍內，確保拖曳與點擊可用 |
| P-02 Main UI Framework | Log 視窗為 P-02 場景下的浮動子面板，在 P-02 `OnUIReady` 後初始化 |

### 依賴 P-03 的系統

| 系統 | 說明 |
|---|---|
| FT-01 冒險者招募 | `OnRecruitSuccess` → P-03 Log |
| FT-02 任務派遣 | `OnCommissionPosted` → P-03 Log |
| FT-03 NPC 決策 | `OnAutoPickup` → P-03 Log |
| FT-04 結果解算 | `OnMissionResolved` → P-03 Log |
| FT-05 公會金流 | `OnCommissionSettled` → P-03 Log |
| FT-06 公會核心 | `OnGameOverPending`、`OnGuildLevelChanged` → P-03 Log（Critical） |

---

## §7 調校旋鈕（Tuning Knobs）

| 參數 | 預設值 | 安全範圍 | 影響面向 |
|---|---|---|---|
| `LOG_MAX_ENTRIES` | 100 | 50 ~ 500 | Log 最大保留條數；過低玩家易失去歷史記錄，過高記憶體佔用上升 |
| `LOG_MIN_WIDTH` | 280 | 200 ~ 400 | Log 視窗縮放下限寬度（px，UI 單位） |
| `LOG_MIN_HEIGHT` | 120 | 80 ~ 200 | Log 視窗縮放下限高度（px，UI 單位） |
| `LOG_DEFAULT_WIDTH` | 400 | 300 ~ 600 | Log 視窗初始寬度 |
| `LOG_DEFAULT_HEIGHT` | 300 | 200 ~ 500 | Log 視窗初始高度 |
| `TIMESTAMP_FORMAT` | `MM/DD hh:mm` | 任意合法 Token 組合 | Log 條目時間戳記顯示格式；Token 見 §4.4 |
| `CRITICAL_FONT_SIZE` | 14 | 10 ~ 18 | Critical 優先級條目字體大小（pt） |
| `OPTIONAL_FONT_SIZE` | 11 | 8 ~ 14 | Optional 優先級條目字體大小（pt） |

---

## §8 驗收標準（Acceptance Criteria）

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-01 | 訂閱的 7 個事件觸發後，Log 各出現對應條目，文字與 `NotificationTemplate.csv` 相符 | 逐一觸發各事件，比對 Log 文字 |
| AC-02 | Critical 事件條目顯示紅底 + 粗體；Optional 條目字體小於 Important | 目測各優先級視覺樣式 |
| AC-03 | Log 條目包含時間戳記，格式與 `TIMESTAMP_FORMAT` 設定完全吻合，月/日/時/分皆零補齊 | 觸發事件，比對 Log 時間戳記格式 |
| AC-04 | Log 超過 100 條時，最舊條目自動移除，新條目插入頂部 | 連續觸發 101 個事件，確認第 1 條已消失 |
| AC-05 | Log 視窗可拖曳至任意位置 | 操作：拖曳視窗，確認跟隨移動 |
| AC-06 | 滑鼠移至四角出現縮放控制點，拖曳後視窗等比縮放，不小於 `LOG_MIN_WIDTH × LOG_MIN_HEIGHT` | 操作：縮放至極小，確認 Clamp 生效 |
| AC-07 | 點擊最小化，Log 收縮至工作列；點擊還原，Log 回到原尺寸與位置 | 操作：最小化 → 還原 |
| AC-08 | 點擊最大化，Log 展開至預設最大尺寸（`LOG_DEFAULT_WIDTH × LOG_DEFAULT_HEIGHT`） | 操作：最大化，目測尺寸 |
| AC-09 | 重啟遊戲後，Log 視窗位置與狀態與關閉前一致（FT-10 持久化） | 操作：移動視窗 → 關閉 → 重啟，確認位置還原 |
| AC-10 | `NotificationTemplate.csv` 找不到 eventType 時，Log 顯示 fallback 文字，不崩潰 | 刪除某一 templateID 後觸發對應事件 |
| AC-11 | `eventPayload` 為 null 時，Log 條目佔位符顯示為空字串，Unity Console 出現 `LogError`，遊戲正常運行 | 手動傳入 null payload，確認 Console 錯誤與遊戲不中斷 |
