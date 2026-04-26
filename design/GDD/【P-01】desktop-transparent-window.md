# 【P-01】Desktop Transparent Window

_建立時間：2026-04-26_
_狀態：已設計_

---

## §1 概覽（Overview）

P-01 Desktop Transparent Window 是「桌面原生」支柱的技術基礎。透過 Win32 API（C# P/Invoke）將 Unity 應用程式視窗設定為透明背景、常駐前置（Always on Top），並實作細粒度點擊穿透邏輯：背景區域點擊穿透至底層視窗，UI 元素區域正常接收滑鼠輸入。玩家在工作時可透過透明視窗看到公會在桌面角落運作，不打斷工作流程。Jam 版完整實作，技術路線採純 Windows API，不引入第三方插件依賴。

---

## §2 玩家幻想（Player Fantasy）

玩家不需要切換至遊戲視窗——公會的透明視窗常駐於桌面前景，工作或上課時只需「瞄一眼」即可掌握公會狀態。閒置冒險者在透明視窗中走動，任務計時器靜靜運轉，讓玩家感受到「公會活在自己的桌面上」。這個瞬間的存在感，是「桌面原生」支柱的核心情感。

---

## §3 詳細規則（Detailed Rules）

### §3.1 視窗尺寸規則

- 寬度：`WorkingArea.Width`（可用桌面寬度；`WorkingArea` 已排除工作列，等同可用螢幕寬）
- 高度：`WorkingArea.Height × WINDOW_HEIGHT_RATIO`（預設 0.30，見 §7）
- 位置：`x = WorkingArea.Left`，`y = WorkingArea.Bottom - windowHeight`（緊貼工作列上緣；工作列無論在底部、頂部、左側或右側皆正確，因 `WorkingArea` 已排除）
- **解析度自適應**：監聽 `WM_DISPLAYCHANGE` 訊息，收到後重新計算目標螢幕 `WorkingArea`、視窗尺寸與 `baseResolutionScale`，並呼叫 `SetWindowPos` 更新
- **切換螢幕**（Jam 版）：玩家可透過設定選擇目標螢幕（預設主螢幕）；切換後以目標螢幕的 `WorkingArea`（透過 `GetMonitorInfo` 取得）重算視窗尺寸與位置，呼叫 `SetWindowPos` 搬移視窗；**選擇結果透過 FT-10 持久化，下次啟動自動套用；若儲存的螢幕已斷開，fallback 至主螢幕**
- **多螢幕延伸 UI**（Post-Jam）：自動延伸 UI 至左右鄰接螢幕（或上下螢幕），將公會場景橫跨多個螢幕顯示

### §3.2 視窗初始化流程

啟動時序（`DesktopWindowManager.Awake()`）：

1. 讀取已儲存目標螢幕設定（透過 FT-10 持久化）；若無儲存或儲存的螢幕已斷開，以主螢幕為預設（`GetPrimaryMonitor()` 取得 HMONITOR）
2. `GetActiveWindow()` 取得 HWND
3. 計算視窗尺寸（依 §3.1）
4. `SetWindowLongPtr(GWL_EXSTYLE)`：加入 `WS_EX_LAYERED | WS_EX_APPWINDOW`（Extended Style；`WS_EX_APPWINDOW` 確保出現於工作列）
5. `SetWindowLongPtr(GWL_STYLE)`：移除 `WS_CAPTION | WS_THICKFRAME`（Window Style；移除標題列與可調邊框）
6. `SetLayeredWindowAttributes`：color key = `RGB(0, 0, 0)`，模式 `LWA_COLORKEY`
7. Camera：`clearFlags = SolidColor`，`backgroundColor = Color(0, 0, 0, 0)`（純黑對應 color key）
8. `SetWindowPos`：`HWND_TOPMOST`，套用 §3.1 尺寸與位置
9. 替換 `WndProc`（`SetWindowLongPtr GWLP_WNDPROC`），啟用 Hit-Test 邏輯（§3.3）與解析度變更監聽（§3.1）
10. 等待 P-02 `OnUIReady` 事件，確認 UI 載入完成後開放輸入

> 限制：`#000000`（純黑）為透明色鍵保留禁用色，禁止用於任何遊戲元素顏色。

### §3.3 點擊穿透（Hit-Test）邏輯

不使用 `WS_EX_TRANSPARENT`（全視窗穿透，無法區分 UI 區域）。改為攔截 `WM_NCHITTEST`：

- 游標位置有 UI Toolkit `VisualElement` 命中（`panel.Pick(localPos) != null`）→ 回傳 `HTCLIENT`（正常接收輸入）
- 無命中 → 回傳 `HTTRANSPARENT`（穿透至底層視窗）

查詢在自訂 WndProc 每次收到 `WM_NCHITTEST` 時執行，不做 per-frame polling。

**座標轉換流程**（`WM_NCHITTEST` lParam → panel-local）：

1. 從 lParam 解出螢幕座標 `(screenX, screenY)`
2. `ScreenToClient(hwnd, &pt)` → 視窗 client 座標 `(clientX, clientY)`
3. 換算至 panel-local：`localX = clientX / effectiveScale`，`localY = clientY / effectiveScale`
4. `panel.Pick(new Vector2(localX, localY))`

### §3.4 Always on Top

- 初始化時設定 `HWND_TOPMOST`，全程維持
- Jam 版不提供使用者切換「取消置頂」功能
- 若其他全螢幕應用程式（遊戲/影片播放器）啟動，Windows 會自然覆蓋本視窗，無需特殊處理

### §3.5 縮放規則

```
baseResolutionScale = WorkingArea.Height / REFERENCE_HEIGHT   // 解析度自適應
effectiveScale      = baseResolutionScale × userScale         // userScale 玩家調整
```

- `effectiveScale` 套用至 UI Toolkit `PanelSettings.scale`
- 縮放只改變元素大小，視窗尺寸（全寬 × 比例高）不變
- 玩家可透過遊戲內 UI 調整 `userScale`（預設 1.0，範圍與步進見 §7）；**`userScale` 設定透過 FT-10 持久化**
- **還原預設**：提供「還原預設尺寸」按鈕，將 `userScale` 重設為 `1.0`，`effectiveScale` 回到純解析度自適應值

### §3.6 UI 錨點規則

- 左側面板（公會建築、委託板）：USS `position: absolute; left: 0`
- 右側面板（冒險者區域）：USS `position: absolute; right: 0`
- 縮放不影響錨點，元素從各自邊緣就地縮放，不往中央漂移

### §3.7 最小化 / 還原

- 視窗無標準邊框，提供遊戲內「最小化」按鈕
- 最小化：`ShowWindow(hwnd, SW_MINIMIZE)`，視窗縮至工作列
- 還原：點擊工作列圖示，`ShowWindow(hwnd, SW_RESTORE)` + 重設 `HWND_TOPMOST`（最小化後 topmost 可能被重置）

---

## §4 公式（Formulas）

### 變數定義

| 變數 | 說明 | 預設值 |
|---|---|---|
| `REFERENCE_HEIGHT` | 參考解析度高度（像素） | 1080 |
| `WINDOW_HEIGHT_RATIO` | 視窗高度佔可用桌面高度比例 | 0.30 |
| `userScale` | 玩家調整倍率 | 1.0 |
| `WorkingArea.Height` | 目標螢幕可用桌面高度（已排除工作列，透過 `GetMonitorInfo` 取得） | 依系統 |
| `WorkingArea.Bottom` | 目標螢幕可用桌面底部 Y 座標 | 依系統 |

> 切換螢幕後，`WorkingArea` 改為新目標螢幕的可用區域，其餘公式不變。

### 縮放公式

```
baseResolutionScale = WorkingArea.Height / REFERENCE_HEIGHT
effectiveScale      = baseResolutionScale × userScale
```

### 視窗高度與位置公式

```
windowX      = WorkingArea.Left
windowHeight = WorkingArea.Height × WINDOW_HEIGHT_RATIO
windowY      = WorkingArea.Bottom - windowHeight
```

### 範例計算

**1920×1080，工作列高 40px**
- `WorkingArea.Height` = 1040
- `windowHeight` = 1040 × 0.30 = 312px
- `windowY` = 1040 − 312 = 728
- `baseResolutionScale` = 1040 / 1080 ≈ 0.963
- `effectiveScale`（userScale=1.0）≈ 0.963

**2560×1440，工作列高 40px**
- `WorkingArea.Height` = 1400
- `windowHeight` = 1400 × 0.30 = 420px
- `baseResolutionScale` = 1400 / 1080 ≈ 1.296
- `effectiveScale`（userScale=1.0）≈ 1.296

---

## §5 邊界情況（Edge Cases）

| 情況 | 行為 |
|---|---|
| 工作列在頂部、左側或右側（非預設底部） | 以目標螢幕 `WorkingArea` 實際矩形計算，自動適應；頂/左/右皆正確 |
| 多螢幕環境（Jam 版） | 玩家透過設定切換目標螢幕；視窗跟隨目標螢幕 `WorkingArea` 重新定位 |
| 多螢幕延伸 UI（Post-Jam） | 自動延伸 UI 至鄰接螢幕，Jam 版不實作 |
| `userScale` 超出範圍 | Clamp 至 `[USER_SCALE_MIN, USER_SCALE_MAX]`，不拋例外 |
| 解析度變更（`WM_DISPLAYCHANGE`）時遊戲正在運行 | 重算目標螢幕 `WorkingArea`、視窗尺寸與 `baseResolutionScale`；重設位置；不重啟遊戲 |
| 禁用色 `#000000` 被遊戲元素使用 | 顯示為透明破洞；由美術規範（禁用色碼 `#000000`）防範，程式不做修補 |
| 視窗還原後 `HWND_TOPMOST` 被重置 | `SW_RESTORE` 後立即重呼 `SetWindowPos HWND_TOPMOST` |
| Unity Editor 模式（非 Build） | 所有 Win32 調用僅在 `UNITY_STANDALONE_WIN && !UNITY_EDITOR` 條件下編譯，Editor 模式下跳過，視窗行為回退至標準 Unity 視窗 |

---

## §6 依賴關係（Dependencies）

### P-01 依賴的系統

| 系統 | 依賴內容 |
|---|---|
| P-02 Main UI Framework | 等待 `OnUIReady` 事件後才啟用 Hit-Test；`panel.Pick()` 查詢依賴 P-02 的 UI Panel 實例；UI 錨點規則（§3.6）由 P-02 實作 |

### 依賴 P-01 的系統

| 系統 | 說明 |
|---|---|
| P-02 Main UI Framework | P-02 的 USS 錨點規則（置左/置右）與 `PanelSettings.scale` 設定須與 P-01 §3.5/§3.6 規格對齊 |
| P-03 Notification System | P-03 的通知 UI 元素須在 P-01 Hit-Test 可命中區域內，確保玩家可點擊通知 |

> P-01 為純技術層，不讀取任何遊戲資料（Foundation / Core / Feature 層），無相關依賴。

---

## §7 調校旋鈕（Tuning Knobs）

| 參數 | 預設值 | 安全範圍 | 影響面向 |
|---|---|---|---|
| `REFERENCE_HEIGHT` | 1080 | 720 ~ 2160 | 解析度自適應基準；調高則高解析度下元素變小，調低則反之 |
| `WINDOW_HEIGHT_RATIO` | 0.30 | 0.20 ~ 0.50 | 底部欄帶高度佔可用桌面比例；過低角色顯示不完整，過高遮蔽工作桌面 |
| `USER_SCALE_MIN` | 0.5 | 0.3 ~ 0.8 | 玩家縮放下限；過低導致 UI 文字不可讀 |
| `USER_SCALE_MAX` | 2.0 | 1.5 ~ 3.0 | 玩家縮放上限；過高導致元素超出視窗範圍 |
| `USER_SCALE_STEP` | 0.1 | 0.05 ~ 0.25 | 玩家每次調整的步進幅度 |

---

## §8 驗收標準（Acceptance Criteria）

| # | 條件 | 驗證方式 |
|---|---|---|
| AC-01 | 遊戲啟動後，視窗寬度等於目標螢幕可用寬度，高度等於可用高度 × `WINDOW_HEIGHT_RATIO`，緊貼工作列上緣 | 目測 + 以截圖量測像素 |
| AC-02 | 視窗背景完全透明，可清楚看到桌面底圖與其他視窗 | 目測：將瀏覽器置於遊戲後方，確認透視 |
| AC-03 | 點擊透明背景區域，點擊事件穿透至底層視窗（如桌面圖示可被選取） | 操作：點擊背景區域，確認桌面圖示被選中 |
| AC-04 | 點擊 UI 元素（按鈕、面板），正常觸發互動，不穿透 | 操作：點擊任意遊戲按鈕，確認有反應 |
| AC-05 | 視窗常駐前景（Always on Top），其他視窗無法覆蓋 | 操作：切換至其他應用程式視窗，確認遊戲視窗仍在最前 |
| AC-06 | 解析度變更後，視窗自動重新計算尺寸與位置，不需重啟遊戲 | 操作：在 Windows 設定中變更解析度，確認視窗自動調整 |
| AC-07 | 多螢幕環境下，玩家切換目標螢幕後，視窗正確搬移至目標螢幕 | 操作：設定中切換螢幕，確認視窗移動 |
| AC-08 | 玩家調整 `userScale`，遊戲元素等比例縮放，左側面板維持左錨、右側面板維持右錨 | 目測：縮放後確認 UI 元素不往中央漂移 |
| AC-09 | 點擊「還原預設尺寸」，`userScale` 回到 1.0，元素尺寸恢復 | 操作：先縮放後還原，確認尺寸回復 |
| AC-10 | 點擊「最小化」，視窗縮至工作列；點擊工作列圖示，視窗還原且 Always on Top 仍有效 | 操作：最小化後還原，確認置頂行為 |
| AC-11 | Unity Editor 模式下，所有 Win32 調用不執行，不拋例外 | 在 Editor 中 Play Mode，確認無錯誤 log |
