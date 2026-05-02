# Mission Content Database 系統設計文件

_建立時間：2026-05-02_
_狀態：已設計 v1.0_
_系統 ID：D-02_

---

## 1. 概要（Overview）

D-02 Mission Content Database 是 `MissionNamePool` 的設計規格與內容規範，提供任務名稱（`missionName`）與描述（`missionDesc`）的文字資料池。上層系統透過 C-01 Mission Database 的 Facade API `GetMissionText(difficulty, typeID)` 隨機抽取一筆，不需直接存取此表。D-02 本身無 runtime 邏輯，屬純靜態內容資料庫；其設計工作包含：(1) 定義 `MissionNamePool` 的欄位規格；(2) 制定文字品質門檻（語言風格、長度上限、是否揭露機制資訊）；(3) 規定每個有效 (difficulty × typeID) 組合至少需要 3 筆條目以避免重複感；(4) 提供 Game Jam 初始資料（覆蓋全 31 種有效組合，各組至少 3 筆，共 93 筆以上）。

## 2. 玩家幻想（Player Fantasy）

玩家瀏覽委託板時，每張任務的名稱應在兩秒內傳達「危險程度」與「工作性質」的直覺印象。F/E 難度的任務感覺像日常雜務（安全、瑣碎）；S/SS/SSS 的任務名稱應帶有重量與不祥感。描述不是故事，是公告欄上的一句話——簡短、帶有世界感，偶爾透露一絲神秘。對設計師而言，新增文字等同新增 CSV 行，不需改動任何程式碼。

## 3. 詳細規則（Detailed Rules）

### 3.1 MissionNamePool 欄位規格

| 欄位 | 型別 | 說明 |
|------|------|------|
| `nameID` | `int` (PK) | 唯一識別符，從 1 開始，不重複 |
| `difficulty` | `string` | F / E / D / C / B / A / S / SS / SSS |
| `typeID` | `int` (FK → C-01 MissionTypeTable) | 1=討伐 / 2=護送 / 3=採集 / 4=調查 |
| `missionName` | `string` | 任務名稱，最長 20 字 |
| `missionDesc` | `string` | 任務描述，最長 60 字 |

### 3.2 文字品質門檻

- **名稱**：最長 20 字；不含括號、標點符號堆疊；應傳達任務性質（討伐類帶「斬」「獵」「剿」等動詞，調查類帶「探」「查」「尋」等）
- **描述**：最長 60 字；以第三人稱公告文體撰寫（「委託方：…」或「目標：…」格式，或自由句但語氣正式）；不得包含程式識別符或數值（如 `baseReward`、`0.13`）；F/E 語氣平淡，S+ 語氣沉重或緊迫。範例——F 討伐：「目標：村莊周圍出沒的野狼群，擾民已久，請協助清除。」；SSS 討伐：「委託方不詳。目標以符文封印，接觸者無一生還。公會長親核方可發布。」
- **語言**：繁體中文；奇幻地名、生物名可自創但需一致性（同一生物不同 row 名稱需一致）

### 3.3 覆蓋規則

- 有效組合共 **31 種**（3 非護送類型 × 9 難度 + 護送 × 4 難度）
- 每種有效組合至少 **3 筆**，Game Jam 初始資料共 **≥ 93 筆**
- 無效組合（護送 × F/E/S/SS/SSS）不得填入；若填入，C-01 載入時會 `Debug.LogError` 並跳過

## 4. 公式（Formulas）

D-02 無獨立公式。唯一數量關係：

```
合法最小 row 數 = 有效組合數(31) × 最小筆數(3) = 93
```

文字查詢的隨機抽取邏輯由 C-01 `GetMissionText` 執行，D-02 不擁有任何 runtime 行為。

## 5. 邊緣案例（Edge Cases）

| 情況 | 處理方式 |
|------|---------|
| `difficulty` 填入非法值（如 `"X"`） | C-01 `GetMissionText` 篩選結果為空 → 回傳 fallback `("未知委託", "（無描述）")` 並 `Debug.LogWarning`；D-02 本身不驗證 |
| `typeID` 填入非法值（如 `99`） | 同上，篩選結果為空 → fallback |
| 護送（typeID=2）填入 F/E/S/SS/SSS 難度 | C-01 API 不會呼叫此組合；`MissionNamePool` 中的對應行屬無效資料但不 crash |
| 某有效組合筆數不足 3 筆 | 系統可運作，重複感上升；屬內容品質問題，不觸發 runtime 錯誤 |
| `missionName` 超過 20 字 | 不觸發 runtime 錯誤；由 UI 截斷或顯示不完整，屬資料填寫規範問題 |
| `missionDesc` 超過 60 字 | 同上 |
| `nameID` 重複 | DataManager 標準行為：後者覆蓋前者，`Debug.LogWarning` |

## 6. 依賴關係（Dependencies）

### 6.1 D-02 的依賴（上游）

| 系統 | 用途 |
|------|------|
| F-01 DataManager | 載入 `MissionNamePool.csv`，提供 `PickRandomWhere<MissionNameData>` 查詢能力 |
| C-01 Mission Database | 定義合法的 `difficulty`（F~SSS）與 `typeID`（1~4）範圍；D-02 內容填寫需對齊 C-01 的約束（護送限 D~A） |

### 6.2 依賴 D-02 的系統（下游）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| C-01 Mission Database | 透過 F-01 DataManager 的 `PickRandomWhere<MissionNameData>` 查詢 `MissionNamePool`；C-01 的 `GetMissionText(difficulty, typeID)` 為對上層系統的 Facade，D-02 無獨立 runtime class | C-01 `GetMissionText(difficulty, typeID) : (string name, string desc)` |

### 6.3 循環依賴

D-02 不依賴任何 runtime 系統，無循環依賴。C-01 作為 D-02 的唯一消費者，關係單向。

## 7. 可調參數（Tuning Knobs）

D-02 無數值參數，可調整的只有內容本身。

| 可調項目 | 說明 | 注意事項 |
|---------|------|---------|
| 各組筆數 | 每個 (difficulty × typeID) 組合的條目數量 | 增加筆數降低重複感；最低不得低於 1 筆（否則 C-01 觸發 fallback） |
| 名稱長度上限 | 目前規範 20 字 | 超長名稱由 UI 決定截斷行為，不影響 runtime |
| 描述長度上限 | 目前規範 60 字 | 同上 |
| 文字風格 | 語氣、用詞、公告文體格式 | 修改需重新審閱全部 93+ 筆確保一致性 |

## 8. 驗收標準（Acceptance Criteria）

| ID | 測試項目 | 通過條件 |
|----|---------|---------|
| AC-D02-01 | 覆蓋完整性 | 31 種有效 (difficulty × typeID) 組合，各組至少 3 筆；可用腳本或手動 group-by 驗證 |
| AC-D02-02 | 無效護送組合 | `MissionNamePool` 中不存在 typeID=2 且 difficulty 為 F/E/S/SS/SSS 的行 |
| AC-D02-03 | nameID 唯一 | 所有 row 的 `nameID` 不重複 |
| AC-D02-04 | 名稱長度 | 所有 `missionName` ≤ 20 字 |
| AC-D02-05 | 描述長度 | 所有 `missionDesc` ≤ 60 字 |
| AC-D02-06 | C-01 Facade 整合 | 執行 `GetMissionText("C", 1)` 100 次，結果不全相同（隨機抽取有效） |
| AC-D02-07 | Fallback 不觸發 | 遊戲正常啟動後 Console 不出現任何 `MissionNamePool` 相關的 `LogWarning`（代表所有合法組合均有資料） |
| AC-D02-08 | 文字無裸數值 | 所有 `missionDesc` 不含小數點數字（`0.xx`）或程式識別符（以人工審查確認） |

## 九、變更歷史

| 日期 | 版本 | 變更摘要 |
|------|------|---------|
| 2026-05-02 | v0.1 | 骨架建立 |
| 2026-05-02 | v1.0 | 8 節全部完成（欄位規格、31 組覆蓋規則、文字品質門檻、8 條驗收標準） |
| 2026-05-02 | v1.1 | design-review 修正：§ 6.2 依賴描述對齊 C-01 § 6.1（D-02 無獨立 runtime class，透過 F-01 DataManager 消費）；§ 3.2 補充描述文體範例（F 討伐 / SSS 討伐各一句） |
