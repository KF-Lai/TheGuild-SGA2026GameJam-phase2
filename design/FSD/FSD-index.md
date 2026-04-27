# FSD 索引（Functional Specification Document Index）

_建立日期：2026-04-26_
_適用對象：實作 The Guild 系統的程式人員（Claude Code、Codex、人類工程師）_
_文件性質：規範 + 索引 + 狀態記錄_

---

## 一、文件目的（Purpose）

本文件同時承擔三個角色：

1. **規範**：定義 FSD（Functional Specification Document，功能規格說明書）的撰寫準則、固定章節格式、衝突處理流程與 review 程序。
2. **索引**：建立 GDD（Game Design Document）與 FSD 的對應關係，作為所有 FSD 的入口。
3. **狀態記錄**：登記每份 FSD 的撰寫進度、Review 結果與「是否遵循 GDD 規則」的自檢紀錄。

**FSD 的角色**：將 GDD 的設計規則轉譯為程式可實作的規格。FSD 是 GDD 與 Script 之間的橋樑，不取代 GDD，也不直接成為 Script。

---

## 二、撰寫規範（Writing Guidelines）

### 2.1 基本原則

| 原則 | 內容 |
| --- | --- |
| GDD 為主 | FSD 以 GDD 為唯一設計來源；對 GDD 的規則拆分、規劃 Script，但不改寫 GDD 內容（添加備註除外）。 |
| 對齊章節 | FSD 必須採用本文件「三、FSD 標準章節格式」定義的固定章節，順序與編號不可調動。 |
| 不揣測 | GDD 第三節「詳細規則」為不可揣測的硬性需求；GDD 第四節「公式」為參考實作，可替代但結果需等價。 |
| 衝突即停 | 發現 GDD 內部或跨 GDD 的規則衝突時，立即停止撰寫並進入「2.5 衝突處理流程」。 |
| 完成即 Review | 寫完 FSD 必須執行「2.6 Review 流程」，三項檢核全部通過才能標記為已完成。 |

### 2.2 寫作風格

- **敘述精確**：使用明確動詞與量詞，避免「應該」「大致」「適當」「合理」等模糊用詞。
- **減少形容詞**：以名詞與動詞為主，形容詞僅在描述使用者體驗時保留。
- **指令明確**：規格條目以可驗證的形式撰寫（例：「呼叫 `IResourceService.TrySpend(cost)`，回傳 `false` 時觸發 `OnInsufficientGold` 事件」）。
- **結構簡潔、邏輯嚴謹**：每章只談本章主題，不重複內容；條目之間需有明確因果或對映。
- **長度適中**：FSD 不需要塞滿資訊量，也不可過於簡略；判斷標準是「程式人員可依 FSD 動工，無須再回頭逐字讀完整份 GDD」。
- **語言規範**：繁體中文撰寫；Unity API、識別符號、專有名詞保留英文；時間單位只用「秒」或「小時」。
- **禁用 emoji**。

### 2.3 與 GDD 的關係

| GDD 章節 | FSD 對應處理方式 |
| --- | --- |
| 1. 概要 | FSD §1 引述範圍與目標，不重抄。 |
| 2. 玩家幻想 | FSD §3 將「玩家幻想」與「系統目的」轉換為實際可行的技術方案。 |
| 3. 詳細規則 | FSD §4、§5、§6、§7 必須完整對齊；不可隨意修改、調整、揣測。 |
| 4. 公式 | FSD §5 視為參考；若有更合適、結果等價的實作方式，可替代並於 §8.2 註記原因。 |
| 5. 邊緣案例 | FSD §7 必須對應，逐項給出程式對策。 |
| 6. 依賴關係 | FSD §2 必須完整列舉上下游與事件契約。 |
| 7. 可調參數 | FSD §6 必須以 CSV / ScriptableObject 表格化，禁止寫死。 |
| 8. 驗收標準 | FSD §1.3「完成目標」需引用並補充程式可驗證的條件。 |

**反向修改 GDD 的限制**：FSD 不可改寫 GDD；僅能在 GDD 章節末尾以引用區塊新增「FSD 回註：…」標記，並在 FSD §8.4 同步登記。

### 2.4 拆分原則（System / Feature Decomposition）

- **是否拆分由 FSD 撰寫者自主判斷**，無須事前詢問使用者；拆分結果在 §4 完整說明，並於本文件「七、狀態記錄」回報。
- **拆分標準**：
  - 單一 Script 預估 > 500 行，或單一類別承擔 > 1 種職責 → 考慮拆分。
  - GDD 內部已有明顯職責分區（例：Pool 管理、抽卡邏輯、保底計算）→ 可拆分。
  - **不可拆解**完整、不可分割的系統／功能（例：原子性結算流程、單次狀態轉移）。
- **拆分粒度**：避免過度切碎導致 Script 數量爆炸；單一 Script 也不可臃腫到難以維護。經驗值：單一系統 FSD 對應 3~8 個 Script。
- **拆分後的命名**：見「五、命名與檔案規則」。

### 2.5 衝突處理流程（Conflict Resolution）

當 FSD 撰寫過程發現規則衝突，依下列順序處理；**未解決前禁止繼續撰寫該 FSD**：

1. **暫停撰寫**，記錄衝突點（涉及的 GDD ID、章節、條目）。
2. **查 GDD §5 邊緣案例**：若已有對應紀錄，依其指引處理。
3. **查 GDD §6 上游依賴**：若衝突源自上游系統，沿用上游規範。
4. **查 GDD §1 概要與 §2 玩家幻想**：基於系統目的提出建議解法。
5. **詢問使用者**：將衝突點、查詢結果、建議解法整理後提交使用者裁決。
6. **登記**：解決後於 FSD §8.5 登記衝突摘要與最終決議；必要時於 GDD 對應章節以「FSD 回註」標記。

### 2.6 Review 流程（Self-Review After Drafting）

FSD 草稿完成後，必須執行下列三項檢核並於「附錄 A — Review 紀錄」登記結果：

| 檢核項 | 通過條件 |
| --- | --- |
| 1. 結構正確 | 章節編號、標題、順序與本文件「三、FSD 標準章節格式」完全一致；每章皆有實質內容。 |
| 2. 邏輯正確 | 拆分理由成立、Script 職責清晰、API/事件/資料流前後一致、無自相矛盾。 |
| 3. 與 GDD 規則相符 | §3 詳細規則逐項對齊；§4 公式有對應實作或等價替代；§5 邊緣案例皆有對策；§6 依賴皆有列舉；§7 可調參數皆已表格化。 |

任一項未通過，回到對應章節修正後再重 review；三項全部通過，於本文件「七、狀態記錄」依 §7.1 狀態流轉規則更新狀態。

### 2.7 既有 Script 偏差檢查（Existing Script Drift Check）

撰寫 FSD 前必須掃一次目標系統對應的既有 Script 目錄（`TheGuild-unity/Assets/Scripts/...`），用 Glob／Grep 確認：

- 既有 Script 是否與 FSD §4.4 預計規劃的職責一致；
- 既有 Script 是否有「位於本系統目錄但職責屬其他系統」的偏差（例：`Core/Time/MissionTimer.cs` 屬 FT-02）；
- 既有 Script 是否已脫離 GDD 規則（git log 顯示的 hotfix 可能未回註 GDD）。

發現偏差不直接修改 Script；於 §8.3「未能實現的規則與修改建議」列出，並於 FT 對應 FSD 撰寫時統一處理。本步驟只需 1~2 次 Glob 即可完成，不允許展開全目錄細讀。

### 2.8 Subagent 撰寫紀律（Subagent Drafting Discipline）

當 FSD 撰寫派發給 subagent 時，呼叫端必須在 prompt 中明列下列紀律，避免 subagent 在前置探索消耗預算未產出檔案：

- **必讀文件清單上限 4 份**：本 FSD-index、目標 GDD、systems-index 相關段落、目標系統的既有 Script 路徑（Glob 一次取檔名即可，不讀內容）；不得追加閱讀無關 GDD 或 coding-standards 全文。
- **xhigh 思考用於推理**：拆分判斷、§3 映射、§7 邊緣案例對策、§8 對齊自檢四階段必須以推理完成，**不**靠多輪工具呼叫。
- **單次 Write 寫完草稿**：階段三必須以單一 Write 完整輸出 FSD 檔案；後續修正才用 Edit。
- **工具呼叫上限參考 30 次**：超過時主動停下盤點剩餘工作，避免被截斷。
- **Review 必填附錄 A**：完成後依 §2.6 自檢，並更新本文件 §6.1 / §7.1 / §7.2（必要時 §7.3）。
- **回報格式 ≤ 400 字**：項次包含檔案路徑、拆分決策、Review 三項結果、衝突與無法實現項、索引更新、下一步建議。

### 2.9 FSD 完成前 Checklist（Pre-Delivery Checklist）

撰寫者在標記「已完成」前，於 §8 末段或附錄 A 備註逐項勾選：

- [ ] §0 文件資訊：對應 GDD 版本、Data-Specs 引用、撰寫者／Review 者／狀態／日期皆填妥
- [ ] §1.3 完成目標：每條皆可被 EditMode／PlayMode 測試或手動步驟驗證
- [ ] §2.1~§2.5：GDD 章節、Data-Specs、上下游、事件契約四向皆列舉
- [ ] §3.3 對映表：每個玩家幻想／系統目的至少一個對應的技術手段
- [ ] §4.1~§4.4：拆分判斷有結論；Script 清單欄位齊全（含路徑、SRP、依賴介面、預估規模）
- [ ] §5.1~§5.4：API 簽名、事件 payload、資料結構、資料流偽碼齊備
- [ ] §6.1~§6.3：引用 CSV 表含對應 Data-Specs；嚴禁寫死清單對齊實作原則第 9 條
- [ ] §7：GDD §5 每條邊緣案例皆有對策（不允許寫「妥善處理」）
- [ ] §8.1：對齊清單覆蓋 GDD §3 每個小節（至 §3.X 二層粒度即可）
- [ ] §8.2~§8.5：公式對齊／無法實現項／GDD 回註／衝突紀錄如實登記，無內容寫「無」
- [ ] 附錄 A：Review 三項結果全填，登記 Review 者與日期
- [ ] FSD-index：§6.1 三方映射、§7.1 撰寫進度、§7.2 自檢紀錄、§7.3 拆分回報（如有）皆同步更新

---

## 三、FSD 標準章節格式（FSD Standard Sections）

每份 FSD 必須採用下列固定章節，編號與標題不可變動。各章節「目的」說明該章要回答什麼問題；「填寫指引」為撰寫者提示。

### 章節總表

| 編號 | 章節標題 | 目的 |
| --- | --- | --- |
| 0 | 文件資訊（Document Info） | 對應 GDD ID/版本、Data-Specs 引用、撰寫者、狀態、最近更新日期。 |
| 1 | 概要（Overview） | FSD 範圍、目標、不在範圍內項目、完成目標（DoD）。 |
| 2 | 設計來源與依賴（Design Sources & Dependencies） | 引用的 GDD 章節、Data-Specs（含對應 CSV）、上下游系統、跨系統事件契約。 |
| 3 | 幻想到實作映射（Fantasy-to-Implementation Mapping） | 將 GDD §1 系統目的與 §2 玩家幻想轉為具體技術方案。 |
| 4 | 功能拆分與 Script 規劃（Feature Decomposition & Script Plan） | 是否拆分、拆分理由、Script 清單（路徑、SRP、依賴介面／服務、預估規模）。 |
| 5 | 公開介面、事件與資料流（Public API, Events & Data Flow） | 對外 API、事件（發布／訂閱）、資料結構（DTO/SO）、內部資料流（含偽碼示範）。 |
| 6 | 資料表使用與參數化（Data Table Usage & Parameterization） | 引用的 CSV（含對應 Data-Specs）／ScriptableObject、欄位用途、嚴禁寫死清單。 |
| 7 | 邊緣案例對策（Edge Case Handling） | 對齊 GDD §5，逐項給出程式處理方式。 |
| 8 | GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log） | 規則對齊勾選、未對齊項目修改建議、給 GDD 的回註紀錄、衝突處理紀錄。 |
| 附錄 A | Review 紀錄（FSD Review Log） | 結構／邏輯／GDD 對齊三項檢核結果與日期。 |

### 章節撰寫指引

#### §0 文件資訊（Document Info）

採表格化格式，每個欄位獨立一列：

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【系統ID】system-name.md`（版本：commit hash 或日期） |
| 對應 Data-Specs | 多份時逐行列出 `【系統ID-DS】table-name.md`；無則填「無」 |
| 撰寫者 | 人類姓名 / subagent 類型 |
| Review 者 | 同上 |
| 狀態 | `草稿` / `審查中` / `已完成` / `已棄用`（流轉規則見 §7.1） |
| 最近更新 | YYYY-MM-DD |

#### §1 概要（Overview）

- 1.1 系統範圍（一段文字，60~120 字）。
- 1.2 In-Scope / Out-of-Scope 條列。
- 1.3 完成目標（Definition of Done）：對齊 GDD §8 驗收標準，補充程式可驗證的條件（例：通過某 EditMode test、CSV 載入零錯誤、特定事件序列正確）。

#### §2 設計來源與依賴（Design Sources & Dependencies）

- 2.1 GDD 章節引用：明列引用了哪些 GDD 章節（例：§3.2、§4.1、§5）。
- 2.2 Data-Specs 引用：採表格化，欄位包含「Data-Specs 檔名 / 對應 CSV / 引用欄位 / 用途」，與 §6.1 雙向對齊：

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |

- 2.3 上游依賴系統：本系統「需要呼叫／訂閱」的其他系統。
- 2.4 下游被依賴系統：「會呼叫本系統 API／訂閱本系統事件」的其他系統。
- 2.5 跨系統事件契約：列出進出本系統的事件名稱、Payload 結構、發布時機。

#### §3 幻想到實作映射（Fantasy-to-Implementation Mapping）

- 3.1 玩家幻想還原：用一段話複述 GDD §2 的核心幻想。
- 3.2 系統目的還原：用一段話複述 GDD §1 的系統目的。
- 3.3 對映表：

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |

> **§3.3 對映表 vs §8.1 對齊清單**：兩者皆「對映」但目的不同。§3.3 是**設計→實作**（玩家幻想／系統目的 → 玩家現象 → 技術手段），用於說明為何選擇這套技術方案；§8.1 是**規則→章節**（GDD §3 條目 → FSD 章節），用於確認 GDD 規則沒被遺漏。

#### §4 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

- 4.1 是否拆分：`是` / `否`；若否，跳過 4.2、4.3。
- 4.2 拆分理由：對應「2.4 拆分原則」的標準說明。
- 4.3 拆分結果：

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |

- 4.4 Script 清單：

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |

  - **路徑欄位**：以 Unity Asset Database 為準，從 `Assets/...` 起算，省略 `TheGuild-unity/` 專案根前綴。例：`Assets/Scripts/Core/Time/TimeSystem.cs`。
  - **依賴介面／服務欄位**：填介面名（`IDataManager`）、Service Locator 物件（`EventBus`）、UnityEngine 模組（`Time`、`Application`）；不填具體實作類別、不填 Unity GameObject 名稱。
  - **預估規模欄位**：以行數區間表示（例：`200~300 行`、`< 30 行`），> 500 行 → 觸發 §2.4 拆分判斷。

- 4.5 類別關係（可選）：以 ASCII 或 Mermaid 圖描述繼承／組合關係。

#### §5 公開介面、事件與資料流（Public API, Events & Data Flow）

- 5.1 公開 API：列出對外暴露的方法簽名與用途。
- 5.2 事件清單：

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |

- 5.3 資料結構：DTO、ScriptableObject、列舉等定義。
- 5.4 內部資料流：以箭頭式 ASCII 偽碼描述輸入→處理→輸出的流轉。每個觸發點（外部呼叫／事件訂閱／Update tick）獨立一段。格式範例：

  ```
  外部觸發者.方法名稱
    → 本系統.進入點(參數)
        ├─ 步驟 1：從 X 計算 Y
        ├─ 步驟 2：if (條件) → 分支處理
        ├─ 步驟 3：EventBus.Publish(new SomeEvent(payload))
        └─ 步驟 4：更新內部狀態
  ```

  允許用 `├─` `└─` ASCII 樹狀符號或純文字編號；條件分支用 `if (...)` / `else` 縮排；事件發布用 `EventBus.Publish(...)`；不寫真實 C# 語法。

#### §6 資料表使用與參數化（Data Table Usage & Parameterization）

- 6.1 引用的 CSV 表：

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |

  「對應 Data-Specs」欄填 `【系統ID-DS】xxx.md`（與本 FSD §2.2、本文件「§6.2 Data-Specs 索引」雙向對齊）。

- 6.2 引用的 ScriptableObject：同上格式。
- 6.3 嚴禁寫死清單：明列「閾值／常數／公式必須來自表」的項目。每項至少填：

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |

  「違反原則」欄統一填：`對應「四、程式實作原則」第 9 條：參數表格化`。設計上是讓 reviewer 一眼確認所有寫死禁止項都對齊到同一條原則，便於審查。

#### §7 邊緣案例對策（Edge Case Handling）

對齊 GDD §5 邊緣案例，逐項給出對策：

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |

#### §8 GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

- 8.1 規則對齊勾選清單：對 GDD §3 詳細規則逐項勾選。

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |

  - **粒度**：對齊到 GDD §3.X 二層即可（例：§3.3、§3.4），**不必**細到 §3.X.Y.Z 三層；如某 §3.X 整節皆對齊同一 FSD 章節，可用「§3.X 全條目」一列概括。
  - **「是否對齊」欄**：填 `對齊` / `部分對齊` / `未對齊`；後兩者必須在 §8.3 列出原因與修改建議。

- 8.2 公式對齊或替代說明：若採用 GDD §4 公式則勾選；若替代，記錄替代方案與等價證明。
- 8.3 未能實現的規則與修改建議：實作上無法達成 GDD 規則時，提出可行修改方案。
- 8.4 給 GDD 的回註紀錄：明列已在哪些 GDD 章節新增「FSD 回註」標記。
- 8.5 衝突處理紀錄：依「2.5 衝突處理流程」登記每次衝突摘要與最終決議。

#### 附錄 A — Review 紀錄（FSD Review Log）

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |

---

## 四、程式實作原則（Implementation Principles）

下列原則為 FSD 規格之外的程式撰寫指引，所有 Script 須一體遵守：

1. **遵循 FSD 的規格，遵循 GDD 的規則**。
2. **風格一致**：命名／縮排／大括號順從既有檔案 + `.claude/rules/gameplay-code.md`。
3. **可讀性**：表意命名、扁平流程、適度斷句。
4. **重複邏輯函式化**：相似邏輯 ≥ 2 處抽共用。
5. **職責清晰**：類別／方法 SRP（Single Responsibility Principle）。
6. **簡潔度**：移除不必要抽象、未使用欄位、死代碼。
7. **效能**：熱路徑無 alloc、避免 `Find()` / `FindObjectOfType()`、`Awake()` 快取引用。
8. **可靠性**：null 檢查、狀態一致、`OnEnable` / `OnDisable` 對稱。
9. **參數表格化**：閾值／常數／公式來自 CSV 或 ScriptableObject。
10. **冪等性**：重複呼叫結果一致、可安全重試。
11. **預測 Try-Catch**：邊界（I/O、解析、外部 API）catch + log；熱路徑禁吞例外。

---

## 五、命名與檔案規則（Naming & File Conventions）

### 5.1 檔案路徑

- 所有 FSD 放在 `design/FSD/` 目錄下。
- 不建立子目錄，所有 FSD 與 FSD-index.md 同層。

### 5.2 檔名格式

| 情境 | 格式 | 範例 |
| --- | --- | --- |
| 一份 FSD 對應整個 GDD 系統 | `【系統ID-FSD】system-name.md` | `【F-02-FSD】time-system.md` |
| 一份 GDD 拆分成多份 FSD | `【系統ID-FSD-X】sub-name.md`（X = A, B, C...） | `【某系統-FSD-A】sub-A.md`、`【某系統-FSD-B】sub-B.md`（範例為一般性慣例；FT-08 與 FT-12 雖原為同一系統拆分，但拆分發生在 GDD 層而非 FSD 層，各自走獨立 FSD） |

- `系統ID` 對齊 GDD（F-01、C-03、FT-08 等）。
- `sub-name` 為小寫連字號，描述子單元職責。

### 5.3 章節標題格式

- 章節標題採「中文（English）」格式，例：`## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）`。

### 5.4 Script 路徑慣例

- FSD §4.4 Script 清單的「路徑」欄一律以 Unity Asset Database 為準，從 `Assets/...` 起算。
- **不**包含 `TheGuild-unity/` 專案根前綴；Unity 內所有資源以 Asset 路徑唯一識別。
- 路徑使用正斜線 `/`，禁用反斜線 `\`（跨平台一致性）。
- 範例：`Assets/Scripts/Core/Time/TimeSystem.cs`、`Assets/Scripts/Gameplay/Mission/MissionDispatch.cs`。

### 5.5 Data-Specs 引用慣例

- Data-Specs 規格書檔名格式：`【系統ID-DS】table-name.md`，存放於 `design/Data-Specs/`。
- FSD 中引用時用相對路徑或單純檔名：`【F-01-DS】system-constants.md`。
- 一份 Data-Specs 對應一個 CSV 表；一個系統可能有多份 Data-Specs（例：FT-02 可能對應 `SuccessRateTable` 與 `DeathRateTable` 兩份）。
- FSD §0、§2.2、§6.1 三處皆需提到對應 Data-Specs，互相對齊。

---

## 六、FSD 索引（FSD Index）

本章建立 **GDD ↔ Data-Specs ↔ FSD** 三方映射，作為實作人員快速定位設計來源、表格規格與功能規格的入口。

### 6.1 GDD-DataSpecs-FSD 三方對應表

下表登記每個 GDD 系統對應的 Data-Specs 與 FSD；當 GDD 拆分為多份 FSD 時，於 FSD 欄條列；多份 Data-Specs 同欄條列。

| GDD ID | GDD 名稱 | 對應 Data-Specs | FSD 檔案 | 拆分情形 |
| --- | --- | --- | --- | --- |
| F-01 | DataManager | [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md) | [【F-01-FSD】data-manager.md](【F-01-FSD】data-manager.md) | 未拆分 |
| F-02 | Time System | [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md)（共用 `DAILY_RESET_HOUR`、`OFFLINE_MAX_SECONDS`） | [【F-02-FSD】time-system.md](【F-02-FSD】time-system.md) | 未拆分 |
| F-03 | Resource Management | [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md)、[【F-03-DS】bankruptcy-threshold-table.md](../Data-Specs/【F-03-DS】bankruptcy-threshold-table.md) | [【F-03-FSD】resource-management.md](【F-03-FSD】resource-management.md) | 未拆分（方向 C：保留 636 行單檔 + §8.3 條目 D8 內部分區建議） |
| C-01 | Mission Database | [【C-01-DS】mission-template.md](../Data-Specs/【C-01-DS】mission-template.md)<br>[【C-01-DS】mission-type-table.md](../Data-Specs/【C-01-DS】mission-type-table.md)<br>[【C-01-DS】mission-category-table.md](../Data-Specs/【C-01-DS】mission-category-table.md)<br>[【C-01-DS】mission-difficulty-table.md](../Data-Specs/【C-01-DS】mission-difficulty-table.md) | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) | 未拆分（單 FSD 對應 4 個 Script：MissionDatabaseLoader / MissionDatabaseService / EscortDurationCalculator / MissionTextFacade） |
| C-02 | Adventurer Management | [`【C-02-DS】adventurer-template.md`](../Data-Specs/【C-02-DS】adventurer-template.md)<br>[`【C-02-DS】recruit-cost-table.md`](../Data-Specs/【C-02-DS】recruit-cost-table.md) | [`【C-02-FSD】adventurer-management.md`](【C-02-FSD】adventurer-management.md) | 未拆分 FSD，內含 4 個 Script（AdventurerTemplateLoader / AdventurerFactory / AdventurerRoster / AdventurerWoundedRecovery） |
| C-03 | Profession System | [【C-03-DS】profession-table.md](../Data-Specs/【C-03-DS】profession-table.md) | [【C-03-FSD】profession-system.md](【C-03-FSD】profession-system.md) | 未拆分（單 FSD 對應 4 個 Script：ProfessionData / IProfessionService / ProfessionDatabaseLoader / ProfessionService） |
| C-04 | Race System | [`【C-04-DS】race-table.md`](../Data-Specs/【C-04-DS】race-table.md)（`RaceTable`；`raceIDs` / `raceWeights` 已合併入 C-03 `ProfessionTable`，owner = C-03） | [`【C-04-FSD】race-system.md`](【C-04-FSD】race-system.md) | 未拆分（單 FSD 含 4 Script：RaceData / IRaceService / RaceDatabaseLoader / RaceService） |
| C-05 | Trait System | [`【C-05-DS】trait-table.md`](../Data-Specs/【C-05-DS】trait-table.md)<br>[`【C-05-DS】trait-group-table.md`](../Data-Specs/【C-05-DS】trait-group-table.md) | [`【C-05-FSD】trait-system.md`](【C-05-FSD】trait-system.md) | 未拆分 FSD，內含 4 個 Script（TraitData / TraitGroupData / TraitDatabaseLoader / TraitService） |
| C-06 | World Danger System | [【C-06-DS】world-danger-table.md](../Data-Specs/【C-06-DS】world-danger-table.md)（單表整合升級閘 / 任務池權重 / 債務上限；原 `MissionPoolWeights.csv` / `DebtLimitTable.csv` 已合併入此表） | [【C-06-FSD】world-danger-system.md](【C-06-FSD】world-danger-system.md) | 未拆分 FSD，內含 5 個 Script（WorldDangerData / MissionPoolWeights / IWorldDangerService / WorldDangerLoader / WorldDangerService） |
| FT-01 | Adventurer Recruitment | [【FT-01-DS】veteran-rank-weight-table.md](../Data-Specs/【FT-01-DS】veteran-rank-weight-table.md)、[`【C-02-DS】recruit-cost-table.md`](../Data-Specs/【C-02-DS】recruit-cost-table.md)（FT-01 §7.2 消費端引用；owner = C-02） | [【FT-01-FSD】adventurer-recruitment.md](【FT-01-FSD】adventurer-recruitment.md) | 未拆分 FSD，內含 3 Script（RecruitmentTypes / RecruitmentPoolGenerator / RecruitmentService，預估合計 600~770 行） |
| FT-02 | Mission Dispatch | `【FT-02-DS】success-rate-table.md`（_待建_；CSV: `SuccessRateTable.csv`，owner = FT-02）<br>`【C-01-DS】mission-difficulty-table.md`（消費端引用；`baseDeathRate` 欄位）<br>[`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（消費端引用；`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY`/`ESCORT_TYPE_ID`） | [【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md)<br>[【FT-02-FSD-B】commission-board.md](【FT-02-FSD-B】commission-board.md) | 拆 A/B（FSD-A：成功率計算+派遣+計時；FSD-B：委託板池管理；共 5 Script） |
| FT-03 | NPC Decision System | [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md)（共用 `DEATH_AVERSION` / `ACCEPTANCE_THRESHOLD` / `WILLINGNESS_JITTER` / `AUTO_PICKUP_IDLE_MINUTES` / `AUTO_PICKUP_INTERVAL_MINUTES`） | [【FT-03-FSD】npc-decision-system.md](【FT-03-FSD】npc-decision-system.md) | 未拆分（3 Script：NpcDecisionTypes / INpcDecisionService / NpcDecisionService） |
| FT-04 | Outcome Resolution | [`【FT-04-DS】reputation-delta-table.md`](../Data-Specs/【FT-04-DS】reputation-delta-table.md)（`ReputationDeltaTable.csv`）<br>[`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（消費端：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`）<br>[`【C-01-DS】mission-difficulty-table.md`](../Data-Specs/【C-01-DS】mission-difficulty-table.md)（消費端：`baseReward`） | [`【FT-04-FSD】outcome-resolution.md`](【FT-04-FSD】outcome-resolution.md) | 未拆分（4 Script：OutcomeData / IOutcomeResolutionService / OutcomeResolutionService / OutcomeReputationCalculator） |
| FT-05 | Guild Gold Flow | [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md)（共用金流常數：`COMMISSION_RATE` / `PENALTY_RATE`） | [【FT-05-FSD】guild-gold-flow.md](【FT-05-FSD】guild-gold-flow.md) | 未拆分（3 Script：GoldFlowTypes / IGoldFlowService / GoldFlowService） |
| FT-06 | Guild Core | _待建（`GuildLevelTable`）_ | _待撰寫_ | — |
| FT-07 | Guild Building System | _待建（`BuildingTable`）_ | _待撰寫_ | — |
| FT-08 | Gacha System（面試系統） | _待建（`StaffGachaPoolTable`、`StaffRefreshCostTable`、`StaffRarityProbTable`、`TrashItemTable`、`StaffPlayerState`、`StaffTuning` 共用）_ | _待撰寫_ | 2026-04-26 從原職員系統拆出，聚焦 gacha 機制 |
| FT-09 | Faction Story System | _待建（`FactionRouteTable`、`StoryStageTable`、`MissionFactionScoreWeight`）_ | _待撰寫_ | — |
| FT-10 | Save/Load System | — | _待撰寫_ | — |
| FT-12 | Staff System（職員系統） | _待建（`StaffTable`、`StaffTuning` 共用）_ | _待撰寫_ | 2026-04-26 從原職員系統拆出，聚焦運營（名冊管理 / effect 聚合 / 薪水管線 Phase 2） |

> **維護指引**：新增 FSD 時將「_待撰寫_」替換為連結；新增 Data-Specs 時將「_待建_」替換為連結並對齊本表；FSD 拆分時於「拆分情形」標 `拆 A/B`。

### 6.2 Data-Specs 索引

對應的反向索引，列出每份 Data-Specs 規格書與被引用的 GDD／FSD：

| Data-Specs 檔案 | 對應 CSV | 被引用的 GDD | 被引用的 FSD |
| --- | --- | --- | --- |
| `【FT-02-DS】success-rate-table.md`（_待建_） | `SuccessRateTable.csv` | FT-02 | [【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md) |
| [`【FT-04-DS】reputation-delta-table.md`](../Data-Specs/【FT-04-DS】reputation-delta-table.md) | `ReputationDeltaTable.csv` | FT-04 | [`【FT-04-FSD】outcome-resolution.md`](【FT-04-FSD】outcome-resolution.md) |
| [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md) | `SystemConstants.csv` | F-01、F-02、F-03、FT-02、FT-03、FT-04、FT-05 | 【F-01-FSD】、【F-02-FSD】、【F-03-FSD】、【C-03-FSD】（`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY` 規格定義）、[【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md)（消費端：`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY`/`ESCORT_TYPE_ID`）、[【FT-03-FSD】npc-decision-system.md](【FT-03-FSD】npc-decision-system.md)（消費端：`DEATH_AVERSION`/`ACCEPTANCE_THRESHOLD`/`WILLINGNESS_JITTER`/`AUTO_PICKUP_IDLE_MINUTES`/`AUTO_PICKUP_INTERVAL_MINUTES`）、[【FT-04-FSD】outcome-resolution.md](【FT-04-FSD】outcome-resolution.md)（消費端：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`）、[【FT-05-FSD】guild-gold-flow.md](【FT-05-FSD】guild-gold-flow.md)（消費端：`COMMISSION_RATE`/`PENALTY_RATE`） |
| [【F-03-DS】bankruptcy-threshold-table.md](../Data-Specs/【F-03-DS】bankruptcy-threshold-table.md) | `BankruptcyThresholdTable.csv` | F-03 | 【F-03-FSD】 |
| [【FT-01-DS】veteran-rank-weight-table.md](../Data-Specs/【FT-01-DS】veteran-rank-weight-table.md) | `VeteranRankWeightTable.csv` | FT-01 | [【FT-01-FSD】adventurer-recruitment.md](【FT-01-FSD】adventurer-recruitment.md) |
| [【C-01-DS】mission-template.md](../Data-Specs/【C-01-DS】mission-template.md) | `MissionTemplate.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) |
| [【C-01-DS】mission-type-table.md](../Data-Specs/【C-01-DS】mission-type-table.md) | `MissionTypeTable.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) |
| [【C-01-DS】mission-category-table.md](../Data-Specs/【C-01-DS】mission-category-table.md) | `MissionCategoryTable.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) |
| [【C-01-DS】mission-difficulty-table.md](../Data-Specs/【C-01-DS】mission-difficulty-table.md) | `MissionDifficultyTable.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md)、[【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md)（消費端：`baseDeathRate`）、[【FT-02-FSD-B】commission-board.md](【FT-02-FSD-B】commission-board.md)（消費端：`categoryID` 驗證）、[【FT-04-FSD】outcome-resolution.md](【FT-04-FSD】outcome-resolution.md)（消費端：`baseReward`） |
| [【C-02-DS】adventurer-template.md](../Data-Specs/【C-02-DS】adventurer-template.md) | `AdventurerTemplate.csv` | C-02 | [【C-02-FSD】adventurer-management.md](【C-02-FSD】adventurer-management.md) |
| [【C-02-DS】recruit-cost-table.md](../Data-Specs/【C-02-DS】recruit-cost-table.md) | `RecruitCostTable.csv` | C-02、FT-01（消費端引用 §7.2） | [【C-02-FSD】adventurer-management.md](【C-02-FSD】adventurer-management.md)、[【FT-01-FSD】adventurer-recruitment.md](【FT-01-FSD】adventurer-recruitment.md)（消費端） |
| [【C-03-DS】profession-table.md](../Data-Specs/【C-03-DS】profession-table.md) | `ProfessionTable.csv` | C-03、C-04（消費 raceIDs/raceWeights）、C-05（消費 traitGroupIDs） | [【C-03-FSD】profession-system.md](【C-03-FSD】profession-system.md)、[【C-04-FSD】race-system.md](【C-04-FSD】race-system.md)（消費端 raceIDs/raceWeights）、[【C-05-FSD】trait-system.md](【C-05-FSD】trait-system.md)（消費端 traitGroupIDs） |
| [【C-04-DS】race-table.md](../Data-Specs/【C-04-DS】race-table.md) | `RaceTable.csv` | C-04 | [【C-04-FSD】race-system.md](【C-04-FSD】race-system.md) |
| [`【C-05-DS】trait-table.md`](../Data-Specs/【C-05-DS】trait-table.md) | `TraitTable.csv` | C-05 | [`【C-05-FSD】trait-system.md`](【C-05-FSD】trait-system.md) |
| [`【C-05-DS】trait-group-table.md`](../Data-Specs/【C-05-DS】trait-group-table.md) | `TraitGroupTable.csv` | C-05 | [`【C-05-FSD】trait-system.md`](【C-05-FSD】trait-system.md) |
| [`【C-06-DS】world-danger-table.md`](../Data-Specs/【C-06-DS】world-danger-table.md) | `WorldDangerTable.csv` | C-06（單表整合升級閘 / 任務池權重 / 債務上限） | [`【C-06-FSD】world-danger-system.md`](【C-06-FSD】world-danger-system.md) |

> **維護指引**：每次有新 FSD 引用某 Data-Specs，於本表「被引用的 FSD」欄追加。每次新增 Data-Specs，於本表新增一列並同步 §6.1。

---

## 七、狀態記錄（Status Log）

### 7.1 撰寫進度

| FSD 檔案 | 對應 GDD | 狀態 | 撰寫者 | 起始日期 | 完成日期 | 備註 |
| --- | --- | --- | --- | --- | --- | --- |
| `【F-02-FSD】time-system.md` | F-02 | 已完成 | unity-specialist subagent | 2026-04-26 | 2026-04-26 | — |
| `【F-01-FSD】data-manager.md` | F-01 | 已完成 | unity-specialist subagent；Claude Code 主體（裁決 patch） | 2026-04-26 | 2026-04-26 | 逆向 FSD：依既有 Script 反推；§8.3 條目 3 已裁決採方案 A（補實作 `GetString` / `GetBool`，GDD §3.3 / §4.3 / §4.4 加 FSD 回註）；其餘 8 項偏差待 F-03 FSD 完成後一次性回註 GDD |
| `【F-03-FSD】resource-management.md` | F-03 | 已完成 | unity-specialist subagent；Claude Code 主體（D1+D2+D7 Codex 工項審查 + D3/D6 GDD 回註 patch） | 2026-04-26 | 2026-04-26 | 逆向 FSD（方向 C 保留 636 行單檔）；§8.3 8 項偏差處理結果：D1+D2+D7 已由 Codex Medium 工項落地（`SetBankruptcyWarningDuration` API 補上、deprecated 表查詢移除、Warning 維持分支重新鎖定移除）；D3/D6 已對 F-03 GDD 加 FSD 回註；D4 待 FT-10 FSD 對齊 ISaveable 介面後處理；D8 待 D1/D4 落地後再評估；D5 對齊 F-02 FSD 無偏差 |
| `【C-01-FSD】mission-database.md` | C-01 | 審查中 | unity-specialist subagent；Claude Code 主體（P-01 覆核 patch） | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分，規劃 4 個 Script；§8.5 P-01「分鐘 Tech Debt」覆核：F-02 GDD §3.1.2 + FT-02 GDD line 112 已標 Tech Debt 共識，非衝突，FSD 沿用合規 |
| `【C-02-FSD】adventurer-management.md` | C-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分（單 FSD 含 4 Script）；無衝突；建議項 B-01/B-02 不阻礙實作 |
| `【C-03-FSD】profession-system.md` | C-03 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分（4 Script）；無衝突；建議項 B-01（raceWeights 長度驗證責任歸屬）不阻礙實作 |
| `【C-04-FSD】race-system.md` | C-04 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分（4 Script）；無衝突；建議項 B-01（raceWeights 驗證責任已確認由 C-04 RaceService.RollRace 負責）；B-02（fallback raceID=1）不阻礙實作 |
| `【C-05-FSD】trait-system.md` | C-05 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分（4 Script）；無衝突；建議項 B-01（effectTarget 合法清單以 HashSet 維護於 TraitDatabaseLoader）；B-02（weighted pickMode fallback uniform）不阻礙實作 |
| `【FT-01-FSD】adventurer-recruitment.md` | FT-01 | 審查中 | unity-specialist subagent；Claude Code 主體（拆分情形誤標 patch） | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；**未拆分** FSD，內含 3 個 Script（RecruitmentTypes / RecruitmentPoolGenerator / RecruitmentService，預估合計 600~770 行）；subagent 初稿誤標「拆 A/B/C」已 patch（§7.3 紀錄已移除，FT-01 僅 1 份 FSD 檔案）；無真實衝突；建議項 B-01（P-03 待設計，不阻礙）；B-02（AdventurerRankUtil 歸屬建議 C-02 FSD 確認）；§4.2 OnPoolRefreshedEvent 發布點統一至 ExecuteRefresh()，GDD §4.2 已回註 |
| `【FT-02-FSD-A】mission-dispatch-core.md` | FT-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Gameplay/Mission Script）；FSD 拆分 A/B（FSD-A = 成功率+派遣+計時，FSD-B = CommissionBoard）；偏差 D-01：MissionTimer.cs 位於 Core/Time，建議遷移至 Gameplay/Mission 或確認可移除；Tech Debt TD-01：duration 單位分鐘為 C-01/F-02/FT-02 共識，非衝突 |
| `【FT-02-FSD-B】commission-board.md` | FT-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | FT-02 拆分子單元 B（CommissionBoard 池管理）；無衝突；建議項 B-01（categoryID 命名常數包裝）/B-02（GetAvailableCommissions 合集去重策略）不阻礙實作 |
| `【FT-03-FSD】npc-decision-system.md` | FT-03 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Script）；FSD 未拆分（3 Script：NpcDecisionTypes / INpcDecisionService / NpcDecisionService）；無衝突；Tech Debt TD-01（AUTO_PICKUP_*_MINUTES 分鐘命名，載入時 ×60 換算秒）；建議項 B-01（MissionDifficultyUtil 歸屬）/B-02（P-03 Log API 待更新）不阻礙實作；待主體複核後轉「已完成」 |
| `【FT-04-FSD】outcome-resolution.md` | FT-04 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 Outcome Script）；FSD 未拆分（4 Script：OutcomeData / IOutcomeResolutionService / OutcomeResolutionService / OutcomeReputationCalculator）；無真實衝突；建議項 B-01（Outcome 物件引用修改風險）/B-02（空介面層取捨）不阻礙實作；待主體複核後轉「已完成」 |
| `【FT-05-FSD】guild-gold-flow.md` | FT-05 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 GoldFlow Script）；FSD 未拆分（3 Script：GoldFlowTypes / IGoldFlowService / GoldFlowService，預估 350~420 行）；無真實衝突；建議項 B-01（F-03 GDD 需補「單次 netDelta 無雙重穿越零線」保證）/B-02（P-02 clamp 提示建議）/B-03（CommissionSource enum 與 FT-02 DispatchSource 重疊確認）皆不阻礙實作；待主體複核後轉「已完成」 |
| `【C-06-FSD】world-danger-system.md` | C-06 | 審查中 | Claude Code 主體（直接撰寫，無 subagent） | 2026-04-27 | 2026-04-27 | 正向 FSD（無既有 WorldDanger Script）；FSD 未拆分（5 Script：WorldDangerData / MissionPoolWeights / IWorldDangerService / WorldDangerLoader / WorldDangerService，預估 410~530 行）；無真實衝突；建議項 B-01（DefaultExecutionOrder 屬性式實現 Script Execution Order）/B-02（OnFactionScoreUpdated A 階早退補強）/B-03（Loader 啟動時做完整 5 階存在性檢查）皆不阻礙實作；§8.4 兩條 GDD 回註意圖待主體覆核後寫入 GDD |

**狀態定義與流轉規則**：

| 狀態 | 定義 | 進入條件 |
| --- | --- | --- |
| `草稿` | FSD 撰寫中或剛寫完，尚未自檢 | 建立 FSD 檔案 |
| `審查中` | 已自檢通過，等待人類複核或跨系統 review | §2.6 三項自檢全通過 + 附錄 A 已登記 |
| `已完成` | 人類複核通過、§7.2 自檢紀錄為「通過」、§6.1 / §7.1 索引同步更新 | 通過人類複核（或撰寫者為人類本身已等同複核） |
| `已棄用` | GDD 大幅變動或 FSD 被取代 | 由變更歷史 §九 登記原因 |

> **撰寫者為 subagent 時**：自檢通過後狀態應為 `審查中`，等待人類確認後才轉 `已完成`；若為單次任務且無後續複核計畫，可在回報時建議使用者直接標記。

### 7.2 GDD 規則自檢紀錄

紀錄每份 FSD 完成 Review 時的 GDD 對齊檢核結果。同一份 FSD 多次 Review 時逐筆追加。

| 日期 | FSD 檔案 | 結構 | 邏輯 | GDD 對齊 | 未對齊摘要 | 後續處理 |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-04-26 | `【F-02-FSD】time-system.md` | 通過 | 通過 | 通過 | `OnOfflineResolved.completedCount` 來源建議由 FT-02 後續事件提供（見 FSD §8.3 第 1 點） | 已裁決採方案 A（2026-04-26）；F-02 GDD §6.3 與 FT-02 GDD §6.1 加 FSD 回註；FSD §2.4/§2.5/§4.4/§5.2/§5.3/§5.4/§7/§8 全章對齊更新 |
| 2026-04-26 | `【F-02-FSD】time-system.md`（方案 A patch 後重 review） | 通過 | 通過 | 通過 | 無 | — |
| 2026-04-26 | `【F-02-FSD】time-system.md`（FSD-index v2 對齊 patch 後重 review） | 通過 | 通過 | 通過 | 無 | §0 / §2.2 / §6.1 / §6.3 結構對齊新規範；無語意異動 |
| 2026-04-26 | `【F-01-FSD】data-manager.md` | 通過 | 通過 | 通過 | 9 項逆向發現偏差列入 §8.3：(1) GDD §3.1.2「硬編碼表格列表」與實作「去中心化 RegisterTable」差異 (2) SystemConstants 註冊位置 GDD 未提 (3) DataSpec 列 `GetString` / `GetBool` 但實作缺失 (4) CsvParser 額外支援 long/double/bool/float[] (5) GroupPool 需另呼叫 `RegisterGroupPoolTable<T>` (6) 「CSV 完全空白」應拆「檔案不存在」與「只有 header」兩情境 (7) `overrideCount` 多一層保護 (8) weights 全 0 fallback 兩階段 (9) Script Execution Order 改用 `[RuntimeInitializeOnLoadMethod]` 機制 | 條目 3 已裁決採方案 A（2026-04-26）：補實作 `GetString` / `GetBool` 至 `DataManager.cs`，F-01 GDD §3.3 / §4.3 / §4.4 加 FSD 回註；其餘 8 項待 F-03 FSD 完成後一次性回註 GDD |
| 2026-04-26 | `【F-01-FSD】data-manager.md`（條目 3 裁決 patch 後重 review） | 通過 | 通過 | 通過 | 無 | §8.1 §3.3 列改「對齊」；§8.3 條目 3 標已裁決並落地；§8.4 補登 3 筆回註；§8.5 補登衝突處理；附錄 A 新增 patch review 列；狀態轉「已完成」 |
| 2026-04-26 | `【F-03-FSD】resource-management.md` | 通過 | 通過 | 通過（含 8 項偏差登記） | 8 項逆向發現偏差列入 §8.3：(D1) 缺 `SetBankruptcyWarningDuration` / `GetBankruptcyWarningDuration` API 與 `_currentWarningDuration` 欄位（GDD §3.5 rule 8/9） (D2) `LookupWarningDuration` 仍主動查 deprecated `BankruptcyThresholdTable`（GDD §7.2 已宣告改 FT-07 推送） (D3) 事件 payload `OnBankruptcyStateChangedEvent(prev, current)` 為 GDD `(newState)` 的 superset (D4) 未實作 ISaveable 介面，目前以 `CreateSnapshot` / `RestoreSnapshot` 替代（GDD §6.4 規範 7 欄位序列化） (D5) 事件名稱對齊 F-02 FSD（無偏差） (D6) `CanAfford` 多 `amount<=0` 短路（語義等價） (D7) `EvaluateWarningState` 在 Warning 維持分支多一條 `_warningDurationSec = LookupWarningDuration(...)`（與 D1/D2 連動） (D8) 主檔 636 行超 500 拆分門檻 | D1/D2/D7 已由 Codex Medium 工項落地（2026-04-26，Unity Editor 編譯通過）；D3/D6 已對 F-03 GDD §3.4/§4.3 加 FSD 回註；D4 待 FT-10 FSD 對齊 ISaveable；D8 待 D1/D4 落地後再評估 |
| 2026-04-26 | `【F-03-FSD】resource-management.md`（D1+D2+D7 落地 + D3/D6 回註 patch 後重 review） | 通過 | 通過 | 通過 | 無 | §0 狀態轉「已完成」；§8.3 D1/D2/D7 標已落地；§8.4 補 3 筆回註（含 DataSpec deprecated 落地紀錄）；§8.6 新增重大實作變更紀錄；附錄 A 補 patch review 列 |
| 2026-04-26 | `【F-01-FSD】data-manager.md`（剩餘 7 項偏差 GDD 回註 patch 後重 review） | 通過 | 通過 | 通過 | 無 | §8.4 補 7 筆 GDD 回註紀錄（D1+D9 / D4 / D2 / D5 / D6 / D8 / D9 共 7 條 FSD 回註已對 F-01 GDD §3.1 / §3.2 / §3.3 / §3.4 / §5.1 / §5.3 / §6.4 落地）|
| 2026-04-27 | `【C-01-FSD】mission-database.md` | 通過 | 通過 | 通過（含 3 項待裁決／建議事項） | (P-01) baseDuration 單位為分鐘，與全域「禁用分鐘」規則衝突，待使用者裁決；(P-02) 護送允許難度集合 {D,C,B,A} 目前為硬規則，無對應 CSV 欄位，建議未來表格化；(P-03) D-02 MissionNamePool DS 尚未建立，MissionTextFacade 實作待確認欄位對齊 | P-01 為阻礙「已完成」的衝突，待裁決；P-02/P-03 為建議項，不阻礙實作啟動 |
| 2026-04-27 | `【C-01-FSD】mission-database.md`（主體覆核 patch） | 通過 | 通過 | 通過 | 無 | P-01「分鐘 Tech Debt」覆核：F-02 GDD §3.1.2 已明定「任務時長分→秒（× 60）」；FT-02 GDD §3.5 line 112 已標「C-01 + FT-02 + F-02 三系統共識 Tech Debt」。subagent 初稿過度敏感，主體覆核判定**非衝突**；C-01 沿用 GDD 既有共識為合規處理。§8.3 P-01 / §8.5 / 附錄 A 已 patch；FSD 仍維「審查中」待後續主體統一最終轉「已完成」 |
| 2026-04-27 | `【C-02-FSD】adventurer-management.md` | 通過 | 通過 | 通過 | 建議項 B-01（`lastAutoPickupTimestamp` 在 GDD §3.4 缺乏顯式的「不受狀態轉移影響」說明，建議 FT-03 FSD 撰寫時回註）；B-02（`GetRecruitCost` 歸屬 `IAdventurerRoster`，未來可考慮移至 `IAdventurerTemplateLoader`）——兩項皆不阻礙實作啟動 | B-01/B-02 登記於 FSD §8.3；無衝突；待主體複核後轉「已完成」 |
| 2026-04-27 | `【C-03-FSD】profession-system.md` | 通過 | 通過 | 通過 | 建議項 B-01（`raceWeights` 長度與 `raceIDs` 長度一致性驗證責任歸屬，建議 C-04 FSD 撰寫時確認；或由 C-03 Loader 統一處理）——不阻礙實作啟動 | B-01 登記於 FSD §8.3；無衝突；待主體複核後轉「已完成」 |
| 2026-04-27 | `【C-04-FSD】race-system.md` | 通過 | 通過 | 通過 | 建議項 B-01（`raceWeights` 長度驗證責任已確認由 `RaceService.RollRace`（C-04）負責；C-03 Loader 不重複驗證）；B-02（fallback raceID=1 在 Game Jam 範圍內安全，建議 CSV 備註保留）——兩項皆不阻礙實作啟動 | B-01/B-02 登記於 FSD §8.3；無衝突；待主體複核後轉「已完成」 |
| 2026-04-27 | `【C-05-FSD】trait-system.md` | 通過 | 通過 | 通過 | 建議項 B-01（effectTarget 合法清單 23 項以 HashSet 維護，非寫死業務邏輯，符合 Game Jam 範疇）；B-02（weighted pickMode Game Jam 預留 fallback uniform）——兩項皆不阻礙實作啟動 | B-01/B-02 登記於 FSD §8.3；無衝突；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-01-FSD】adventurer-recruitment.md` | 通過 | 通過 | 通過 | 建議項 B-01（P-03 Notification 待設計，OnRecruitSuccess 已透過 EventBus 發布，無需修改 FT-01）；B-02（AdventurerRankUtil 歸屬建議 C-02 FSD 撰寫時確認）——兩項皆不阻礙實作啟動；§4.2 OnPoolRefreshedEvent 發布點統一至 ExecuteRefresh()，GDD §4.2 已加回註 | B-01/B-02 登記於 FSD §8.3；無真實衝突；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-02-FSD-A】mission-dispatch-core.md` | 通過 | 通過 | 通過 | 偏差 D-01（MissionTimer.cs 位置偏差）登記 §8.3；Tech Debt TD-01（分鐘單位）確認為 C-01/F-02/FT-02 三系統共識，非衝突；CommissionSource enum 定義移至 FT-02-B，FT-02-A 只需 DispatchSource；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-02-FSD-B】commission-board.md` | 通過 | 通過 | 通過 | 建議項 B-01（categoryID 命名常數）/B-02（合集去重策略）不阻礙實作；ISaveable 共用 OwnerKey 協調方式 §5.3 明確說明；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-03-FSD】npc-decision-system.md` | 通過 | 通過 | 通過 | 無衝突；Tech Debt TD-01（AUTO_PICKUP_*_MINUTES 分鐘命名，載入時 ×60）已標注為三系統共識，非衝突；建議項 B-01（MissionDifficultyUtil 歸屬確認）/B-02（P-03 Log API 待更新）不阻礙實作；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-04-FSD】outcome-resolution.md` | 通過 | 通過 | 通過 | 正向 FSD（無既有 Outcome Script）；FSD 未拆分（4 Script）；無真實衝突；GDD §3.1 ~ §3.9 全部「對齊」；建議項 B-01（Outcome 物件引用修改風險，Game Jam 訂閱者自律）/B-02（空介面層取捨）不阻礙實作；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-05-FSD】guild-gold-flow.md` | 通過 | 通過 | 通過 | 正向 FSD（無既有 GoldFlow Script）；未拆分（3 Script）；GDD §3.1~§3.10 全部「對齊」；GDD §4 公式直接採用；邊緣案例 §5.1~§5.6 全 22 案例皆有對策；無真實衝突；建議項 B-01（F-03 GDD 補「單次 netDelta 無雙重穿越零線」保證）/B-02（P-02 clamp 提示）/B-03（CommissionSource vs DispatchSource enum 重疊確認）不阻礙實作；待主體複核後轉「已完成」 |
| 2026-04-27 | `【FT-05-FSD】guild-gold-flow.md`（GDD P-001 對齊 patch） | 通過 | 通過 | 通過 | B-03 已解決（GDD P-001）：FT-05 GDD §3.2 / §3.9.1 / §3.9.2 / §6.3 source 型別 `CommissionSource` → `DispatchSource`（對齊 FT-02 §3.6a）；本 FSD §2.5 / §4.2 / §4.4 / §5.2 / §5.3 / §8.3 同步；`GoldFlowTypes.cs` 移除自定 enum，從 FT-02 FSD-A 引用 |
| 2026-04-27 | `【C-03-FSD】profession-system.md`（GDD P-003 對齊 patch） | 通過 | 通過 | 通過 | B-01 已解決（GDD P-003）：C-03 GDD §3.1 + §5.1 已將 raceWeights/raceIDs 長度驗證歸屬明文於 C-03 Loader；本 FSD §8.3 標「已解決」；C-04 FSD 同步 |
| 2026-04-27 | `【C-04-FSD】race-system.md`（GDD P-003 + P-004 對齊 patch） | 通過 | 通過 | 通過 | B-01 重新決議：GDD P-003 將驗證歸屬反轉為 C-03 Loader，本 FSD §8.3 B-01 重寫、§8.4 GDD 回註撤銷（不再寫入 GDD）；§5 偽碼 length 檢查保留為 defensive 雙保險。B-02 已解決：GDD P-004 已於 §3.1 新增 `raceID=1` 保留 ID note，本 FSD §3.3 / §5 fallback 對齊。 |
| 2026-04-27 | `【C-06-FSD】world-danger-system.md` | 通過 | 通過 | 通過 | 正向 FSD；GDD §3.1~§3.4 / §4.1~§4.5 全對齊；GDD §5 共 10 條邊緣案例全有對策；無真實衝突；建議項 B-01（DefaultExecutionOrder）/B-02（A 階早退）/B-03（Loader 5 階完整性檢查）皆不阻礙實作；§8.4 兩條 GDD 回註意圖（§3.2/§4.3 + §4.5）待主體覆核後寫入 GDD；待主體複核後轉「已完成」 |
| 2026-04-27 | `【C-06-FSD】world-danger-system.md`（GDD P-005 對齊 patch） | 通過 | 通過 | 通過 | 對應 `/design-review C-06` NEEDS REVISION 全修：GDD P-005（6 子項）已落地。B-01 / B-02 已解決（GDD §4.3 / §4.5 直接補入）；§8.4 兩條 FSD 回註意圖撤銷（GDD 直接落地）；§8.3 補「FSD 受惠項目」記錄 GDD P-005 另 4 項補強（§4.1 防護 / §4.2 缺漏階 / §3.4 API 機制 / §3.2 寫入時機）對 FSD 的正面影響。B-03 Loader 5 階完整性檢查保留為實作期建議。 |

**檢核結果記法**：`通過` / `未通過`（未通過時於「未對齊摘要」欄填入問題與後續處理計畫）。

### 7.3 拆分回報紀錄

撰寫者自主拆分系統／功能後，於此回報拆分結果，使用者可據此追蹤系統解構情形。

| 日期 | GDD ID | 原系統 | 拆分為 | 拆分理由 |
| --- | --- | --- | --- | --- |
| 2026-04-27 | FT-02 | Mission Dispatch | 【FT-02-FSD-A】mission-dispatch-core.md（MissionRateCalculator + MissionDispatchService，成功率計算+派遣+計時）／【FT-02-FSD-B】commission-board.md（CommissionBoardService，委託板池管理），共 5 Script | GDD §3.4（成功率計算）、§3.5（派遣序列）、§3.7（計時）與 §3.9（CommissionBoard 池管理）三大職責分區明顯；預估合計 > 700 行；FSD-A 持有 `_activeMissions` 核心狀態，FSD-B 持有兩池狀態，職責邊界清晰；符合 §2.4 拆分標準 |

---

## 八、FSD 模板（FSD Skeleton Template）

新建 FSD 時，複製下列骨架到新檔案，依「三、FSD 標準章節格式」之「章節撰寫指引」逐節填寫。

```markdown
# 【系統ID-FSD】功能規格說明書 — System Name

## 0. 文件資訊（Document Info）

| 欄位 | 內容 |
| --- | --- |
| 對應 GDD | `【系統ID】system-name.md`（版本：YYYY-MM-DD 或 commit hash） |
| 對應 Data-Specs | `【系統ID-DS】table-name.md`（多份逐行列；無則填「無」） |
| 撰寫者 | |
| Review 者 | |
| 狀態 | 草稿 |
| 最近更新 | YYYY-MM-DD |

## 1. 概要（Overview）

### 1.1 系統範圍

### 1.2 In-Scope / Out-of-Scope

### 1.3 完成目標（Definition of Done）

## 2. 設計來源與依賴（Design Sources & Dependencies）

### 2.1 GDD 章節引用

### 2.2 Data-Specs 引用

| Data-Specs | 對應 CSV | 引用欄位 | 用途 |
| --- | --- | --- | --- |

### 2.3 上游依賴系統

### 2.4 下游被依賴系統

### 2.5 跨系統事件契約

## 3. 幻想到實作映射（Fantasy-to-Implementation Mapping）

### 3.1 玩家幻想還原

### 3.2 系統目的還原

### 3.3 對映表

| 幻想／目的 | 玩家可感知的具體現象 | 對應的技術手段 |
| --- | --- | --- |

## 4. 功能拆分與 Script 規劃（Feature Decomposition & Script Plan）

### 4.1 是否拆分

### 4.2 拆分理由

### 4.3 拆分結果

| 子單元 ID | 名稱 | 職責 | 對應 GDD 章節 |
| --- | --- | --- | --- |

### 4.4 Script 清單

| Script | 路徑 | 職責（SRP 一句話） | 依賴介面／服務 | 預估規模 |
| --- | --- | --- | --- | --- |

### 4.5 類別關係（可選）

## 5. 公開介面、事件與資料流（Public API, Events & Data Flow）

### 5.1 公開 API

### 5.2 事件清單

| 事件名稱 | 方向 | Payload | 發布時機 / 訂閱目的 |
| --- | --- | --- | --- |

### 5.3 資料結構

### 5.4 內部資料流

```
外部觸發者.方法名稱
  → 本系統.進入點(參數)
      ├─ 步驟 1：從 X 計算 Y
      ├─ 步驟 2：if (條件) → 分支處理
      ├─ 步驟 3：EventBus.Publish(new SomeEvent(payload))
      └─ 步驟 4：更新內部狀態
```

## 6. 資料表使用與參數化（Data Table Usage & Parameterization）

### 6.1 引用的 CSV 表

| 表名 | 欄位 | 對應 Data-Specs | 用途 | 載入時機 |
| --- | --- | --- | --- | --- |

### 6.2 引用的 ScriptableObject

### 6.3 嚴禁寫死清單

| 項目（變數/常數名） | 來源欄位（CSV 或 SO） | 違反原則 |
| --- | --- | --- |

## 7. 邊緣案例對策（Edge Case Handling）

| GDD §5 案例 | 程式處理方式 | 涉及 Script | 驗證方式 |
| --- | --- | --- | --- |

## 8. GDD 對齊自檢與變更紀錄（GDD Alignment Self-Check & Change Log）

### 8.1 規則對齊勾選清單

| GDD §3 條目 | 對應 FSD 章節 | 是否對齊 | 備註 |
| --- | --- | --- | --- |

### 8.2 公式對齊或替代說明

### 8.3 未能實現的規則與修改建議

### 8.4 給 GDD 的回註紀錄

| 日期 | GDD 檔案 | 章節 | 回註摘要 |
| --- | --- | --- | --- |

### 8.5 衝突處理紀錄

| 日期 | 衝突摘要 | 涉及 GDD/FSD | 最終決議 |
| --- | --- | --- | --- |

## 附錄 A — Review 紀錄（FSD Review Log）

### 完成前 Checklist（複製自 FSD-index §2.9）

- [ ] §0 文件資訊填妥
- [ ] §1.3 完成目標可被測試驗證
- [ ] §2.1~§2.5 四向皆列舉
- [ ] §3.3 對映表覆蓋所有幻想／目的
- [ ] §4 Script 清單欄位齊全
- [ ] §5 API/事件/資料結構/資料流齊備
- [ ] §6 CSV 引用含對應 Data-Specs；§6.3 嚴禁寫死清單對齊原則第 9 條
- [ ] §7 邊緣案例皆有對策
- [ ] §8.1 對齊清單覆蓋 GDD §3 二層粒度
- [ ] §8.2~§8.5 如實登記
- [ ] FSD-index §6.1 / §7.1 / §7.2 已同步更新

### Review 紀錄表

| 日期 | Review 者 | 結構 | 邏輯 | GDD 對齊 | 備註 |
| --- | --- | --- | --- | --- | --- |
```

---

## 九、變更歷史（Change History）

| 日期 | 變更摘要 | 變更者 |
| --- | --- | --- |
| 2026-04-26 | 建立 FSD-index.md 初版：規範、索引、狀態記錄、模板 | Claude Code |
| 2026-04-26 | F-02 FSD 撰寫測試後升級：(1) §六 升級為 GDD-DataSpecs-FSD 三方映射並新增 §6.2 Data-Specs 索引；(2) 新增 §2.7 既有 Script 偏差檢查、§2.8 Subagent 撰寫紀律、§2.9 完成前 Checklist；(3) §7.1 補狀態流轉規則；(4) §5 新增 §5.4 Script 路徑慣例、§5.5 Data-Specs 引用慣例；(5) §0 / §2.2 / §4.4 / §5.4 / §6.1 / §6.3 / §8.1 章節指引補強；(6) §八 模板同步更新含完成前 Checklist | Claude Code |
| 2026-04-27 | 同步原職員系統拆分（FT-08 + FT-12，2026-04-26 GDD 層完成）：(1) §5.2 拆分範例註記改為一般性慣例；(2) §6.1 FT-08 row 改為 Gacha System、新增 FT-12 row（Staff System）；(3) StaffTable owner 移交 FT-12，StaffTuning 標記 FT-08 / FT-12 共用 | Claude Code |
| 2026-04-27 | FT-02 FSD 撰寫完成（拆分 A/B）：(1) §6.1 FT-02 row 更新 Data-Specs + FSD 連結 + 拆分情形；(2) §6.2 C-01-DS mission-difficulty-table 追加 FT-02-FSD-A/B 消費端；F-01-DS system-constants 追加 FT-02-FSD-A；新增 FT-02-DS success-rate-table 待建登記；(3) §7.1 新增 FT-02-FSD-A/B 兩列；(4) §7.2 新增 FT-02-FSD-A/B review 紀錄；(5) §7.3 新增 FT-02 拆分回報 | unity-specialist subagent |
