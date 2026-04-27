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

### 2.10 Service 介面命名規範（Service Naming Convention，FSD-Codex-Reoprts-260427 CT-07 裁決）

**裁決**：採方案 A — Game Jam 階段不新增 service interface 抽象層；下游 FSD 引用上游服務時，以既有 concrete singleton 為實作契約。

**對映表（FSD 敘述形式 → 實作契約）**：

| FSD 敘述（為文義方便保留） | 實作契約 | 來源 Script |
| --- | --- | --- |
| `IDataManager.X` | `DataManager.Instance.X` | `Assets/Scripts/Core/Data/DataManager.cs` |
| `ITimeService.NowUTC` / `ITimeSystem.NowUTC` | `TimeSystem.Instance.NowUTC` | `Assets/Scripts/Core/Time/TimeSystem.cs` |
| `IResourceService.X` | `ResourceManagement.Instance.X` | `Assets/Scripts/Gameplay/Resources/ResourceManagement.cs` |
| `IEventBus.Subscribe<T>` / `Publish<T>` | static `EventBus.Subscribe<T>` / `EventBus.Publish<T>` | `Assets/Scripts/Core/Events/EventBus.cs` |

**規範**：
1. 撰寫者可在 FSD 敘述中保留 `IXxxService` 命名以維持可讀性；實作 PR 必須直接呼叫上述 concrete singleton，不新增 interface 包裝。
2. 既有 FSD 不需大量改寫；本條規範作為共識基準，下游 FSD 在 §2.3「上游依賴系統」表內以一行註記引用本條即可。
3. 未來若需要可測試性而引入 interface（例如 EditMode test mock），由 implementation PR 單獨評估，並回註本規範修訂。

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
| FT-06 | Guild Core | _待建（`【FT-06-DS】guild-level-table.md`，CSV：`GuildLevelTable.csv`）_ | [【FT-06-FSD】guild-core.md](【FT-06-FSD】guild-core.md) | 未拆分 FSD，內含 5 個 Script（GuildCoreTypes / GuildCoreConstants / GuildLevelDatabaseLoader / GuildNameUtility / GuildCoreService） |
| FT-07 | Guild Building System | _待建（`【FT-07-DS】building-table.md`，CSV：`BuildingTable.csv`，owner = FT-07）_ | [【FT-07-FSD】guild-building-system.md](【FT-07-FSD】guild-building-system.md) | 未拆分（單 FSD 對應 4 個 Script：BuildingTypes / IBuildingService / BuildingTableLoader / BuildingService，預估合計 600~800 行） |
| FT-08 | Gacha System（面試系統） | _待建（`【FT-08-DS】staff-gacha-pool-table.md`、`【FT-08-DS】staff-refresh-cost-table.md`、`【FT-08-DS】staff-rarity-prob-table.md`、`【FT-08-DS】trash-item-table.md`、`【FT-08-DS】staff-tuning.md`（FT-08 / FT-12 共用））_；消費端：[`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（`OFFLINE_MAX_SECONDS`） | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md) | 未拆分 FSD，內含 5 個 Script（GachaTypes / IGachaService / GachaTableLoader / GachaRollEngine / GachaService，預估 1170~1460 行）；2026-04-26 GDD 從原職員系統拆出，聚焦 gacha 機制 |
| FT-09 | Faction Story System | [`【FT-09-DS】faction-route-table.md`](../Data-Specs/【FT-09-DS】faction-route-table.md)<br>[`【FT-09-DS】story-stage-table.md`](../Data-Specs/【FT-09-DS】story-stage-table.md)<br>消費端：[`【C-01-DS】mission-difficulty-table.md`](../Data-Specs/【C-01-DS】mission-difficulty-table.md)（`factionScoreDelta`，owner = C-01）<br>消費端：[`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（`FACTION_NEUTRAL_ID`） | [【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md) | 未拆分 FSD，內含 5 個 Script（FactionStoryTypes / IFactionStoryService / FactionStoryTableLoader / FactionStoryScoreAccumulator / FactionStoryService，預估 1000~1200 行）；`MissionFactionScoreWeight` 已合併入 C-01 `MissionDifficultyTable.factionScoreDelta`（owner 移交 C-01） |
| FT-10 | Save/Load System | [`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（消費端：`SAVE_AUTO_INTERVAL_SEC` / `SAVE_BACKUP_COUNT` / `SAVE_FILE_NAME` / `SAVE_BAK_PREFIX` / `SAVE_GAMEOVER_PREFIX`） | [【FT-10-FSD】save-load-system.md](【FT-10-FSD】save-load-system.md) | 未拆分 FSD，內含 6 個 Script（SaveLoadTypes / ISaveable / ISaveLoadService / SaveFileIO / SaveLoadBootstrap / SaveLoadService，預估 1000~1310 行） |
| FT-12 | Staff System（職員系統） | `【FT-12-DS】staff-table.md`（_待建_，CSV：`StaffTable.csv`，owner = FT-12）<br>`【FT-08-DS】staff-tuning.md`（_待建_，CSV：`StaffTuning.csv`，FT-08 / FT-12 共用 owner）<br>消費端：[`【F-01-DS】system-constants.md`](../Data-Specs/【F-01-DS】system-constants.md)（`OFFLINE_MAX_SECONDS`，Phase 2 薪水補發 cap） | [【FT-12-FSD】staff-system.md](【FT-12-FSD】staff-system.md) | 未拆分 FSD，內含 5 個 Script（StaffTypes / IStaffService / StaffTableLoader / StaffEffectAggregator / StaffService，預估 1280~1640 行）；2026-04-26 GDD 從原職員系統拆出，聚焦運營（名冊管理 / effect 聚合 / 薪水管線 Phase 2） |

> **維護指引**：新增 FSD 時將「_待撰寫_」替換為連結；新增 Data-Specs 時將「_待建_」替換為連結並對齊本表；FSD 拆分時於「拆分情形」標 `拆 A/B`。

### 6.2 Data-Specs 索引

對應的反向索引，列出每份 Data-Specs 規格書與被引用的 GDD／FSD：

| Data-Specs 檔案 | 對應 CSV | 被引用的 GDD | 被引用的 FSD |
| --- | --- | --- | --- |
| `【FT-02-DS】success-rate-table.md`（_待建_） | `SuccessRateTable.csv` | FT-02 | [【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md) |
| [`【FT-04-DS】reputation-delta-table.md`](../Data-Specs/【FT-04-DS】reputation-delta-table.md) | `ReputationDeltaTable.csv` | FT-04 | [`【FT-04-FSD】outcome-resolution.md`](【FT-04-FSD】outcome-resolution.md) |
| [【F-01-DS】system-constants.md](../Data-Specs/【F-01-DS】system-constants.md) | `SystemConstants.csv` | F-01、F-02、F-03、FT-02、FT-03、FT-04、FT-05、FT-09、FT-10、FT-12（消費端：`OFFLINE_MAX_SECONDS`，Phase 2） | 【F-01-FSD】、【F-02-FSD】、【F-03-FSD】、【C-03-FSD】（`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY` 規格定義）、[【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md)（消費端：`STRONG_TYPE_BONUS`/`WEAK_TYPE_PENALTY`/`ESCORT_TYPE_ID`）、[【FT-03-FSD】npc-decision-system.md](【FT-03-FSD】npc-decision-system.md)（消費端：`DEATH_AVERSION`/`ACCEPTANCE_THRESHOLD`/`WILLINGNESS_JITTER`/`AUTO_PICKUP_IDLE_MINUTES`/`AUTO_PICKUP_INTERVAL_MINUTES`）、[【FT-04-FSD】outcome-resolution.md](【FT-04-FSD】outcome-resolution.md)（消費端：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`）、[【FT-05-FSD】guild-gold-flow.md](【FT-05-FSD】guild-gold-flow.md)（消費端：`COMMISSION_RATE`/`PENALTY_RATE`）、[【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md)（消費端：`FACTION_NEUTRAL_ID`）、[【FT-10-FSD】save-load-system.md](【FT-10-FSD】save-load-system.md)（消費端：`SAVE_AUTO_INTERVAL_SEC`/`SAVE_BACKUP_COUNT`/`SAVE_FILE_NAME`/`SAVE_BAK_PREFIX`/`SAVE_GAMEOVER_PREFIX`）、[【FT-12-FSD】staff-system.md](【FT-12-FSD】staff-system.md)（消費端：`OFFLINE_MAX_SECONDS`，Phase 2 薪水補發） |
| [【F-03-DS】bankruptcy-threshold-table.md](../Data-Specs/【F-03-DS】bankruptcy-threshold-table.md) | `BankruptcyThresholdTable.csv` | F-03 | 【F-03-FSD】 |
| [【FT-01-DS】veteran-rank-weight-table.md](../Data-Specs/【FT-01-DS】veteran-rank-weight-table.md) | `VeteranRankWeightTable.csv` | FT-01 | [【FT-01-FSD】adventurer-recruitment.md](【FT-01-FSD】adventurer-recruitment.md) |
| [【C-01-DS】mission-template.md](../Data-Specs/【C-01-DS】mission-template.md) | `MissionTemplate.csv` | C-01、FT-09（消費 `categoryID` / `factionID`） | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md)、[【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md)（消費端：`GetTemplate` 取 `categoryID` 識別劇情委託） |
| [【C-01-DS】mission-type-table.md](../Data-Specs/【C-01-DS】mission-type-table.md) | `MissionTypeTable.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) |
| [【C-01-DS】mission-category-table.md](../Data-Specs/【C-01-DS】mission-category-table.md) | `MissionCategoryTable.csv` | C-01 | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md) |
| [【C-01-DS】mission-difficulty-table.md](../Data-Specs/【C-01-DS】mission-difficulty-table.md) | `MissionDifficultyTable.csv` | C-01、FT-02（消費 `baseDeathRate`）、FT-04（消費 `baseReward`）、FT-09（消費 `factionScoreDelta`） | [【C-01-FSD】mission-database.md](【C-01-FSD】mission-database.md)、[【FT-02-FSD-A】mission-dispatch-core.md](【FT-02-FSD-A】mission-dispatch-core.md)（消費端：`baseDeathRate`）、[【FT-02-FSD-B】commission-board.md](【FT-02-FSD-B】commission-board.md)（消費端：`categoryID` 驗證）、[【FT-04-FSD】outcome-resolution.md](【FT-04-FSD】outcome-resolution.md)（消費端：`baseReward`）、[【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md)（消費端：`factionScoreDelta`） |
| [【C-02-DS】adventurer-template.md](../Data-Specs/【C-02-DS】adventurer-template.md) | `AdventurerTemplate.csv` | C-02 | [【C-02-FSD】adventurer-management.md](【C-02-FSD】adventurer-management.md) |
| [【C-02-DS】recruit-cost-table.md](../Data-Specs/【C-02-DS】recruit-cost-table.md) | `RecruitCostTable.csv` | C-02、FT-01（消費端引用 §7.2） | [【C-02-FSD】adventurer-management.md](【C-02-FSD】adventurer-management.md)、[【FT-01-FSD】adventurer-recruitment.md](【FT-01-FSD】adventurer-recruitment.md)（消費端） |
| [【C-03-DS】profession-table.md](../Data-Specs/【C-03-DS】profession-table.md) | `ProfessionTable.csv` | C-03、C-04（消費 raceIDs/raceWeights）、C-05（消費 traitGroupIDs） | [【C-03-FSD】profession-system.md](【C-03-FSD】profession-system.md)、[【C-04-FSD】race-system.md](【C-04-FSD】race-system.md)（消費端 raceIDs/raceWeights）、[【C-05-FSD】trait-system.md](【C-05-FSD】trait-system.md)（消費端 traitGroupIDs） |
| [【C-04-DS】race-table.md](../Data-Specs/【C-04-DS】race-table.md) | `RaceTable.csv` | C-04 | [【C-04-FSD】race-system.md](【C-04-FSD】race-system.md) |
| [`【C-05-DS】trait-table.md`](../Data-Specs/【C-05-DS】trait-table.md) | `TraitTable.csv` | C-05 | [`【C-05-FSD】trait-system.md`](【C-05-FSD】trait-system.md) |
| [`【C-05-DS】trait-group-table.md`](../Data-Specs/【C-05-DS】trait-group-table.md) | `TraitGroupTable.csv` | C-05 | [`【C-05-FSD】trait-system.md`](【C-05-FSD】trait-system.md) |
| [`【C-06-DS】world-danger-table.md`](../Data-Specs/【C-06-DS】world-danger-table.md) | `WorldDangerTable.csv` | C-06（單表整合升級閘 / 任務池權重 / 債務上限） | [`【C-06-FSD】world-danger-system.md`](【C-06-FSD】world-danger-system.md) |
| `【FT-08-DS】staff-gacha-pool-table.md`（_待建_） | `StaffGachaPoolTable.csv` | FT-08 | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md) |
| `【FT-08-DS】staff-refresh-cost-table.md`（_待建_） | `StaffRefreshCostTable.csv` | FT-08 | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md) |
| `【FT-08-DS】staff-rarity-prob-table.md`（_待建_） | `StaffRarityProbTable.csv` | FT-08 | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md) |
| `【FT-08-DS】trash-item-table.md`（_待建_） | `TrashItemTable.csv` | FT-08 | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md) |
| `【FT-08-DS】staff-tuning.md`（_待建，FT-08 / FT-12 共用 owner_） | `StaffTuning.csv` | FT-08（`PITY_THRESHOLD` / `TRASH_ROLL_RATE_AT_RARITY_1` / `MIN_AUTO_REFRESH_INTERVAL_SEC` / `INTERVIEW_AUTO_REFRESH_INTERVAL_L1~L5` / `INTERVIEW_SLOT_COUNT_L1~L5` / `MAX_RESERVE_FALLBACK`）、FT-12（`EFFECT_MAX_WILLINGNESS_BONUS` / `EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS` / `EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS` / `EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC` / `BUILDING_SWITCH_COOLDOWN_SECONDS` / `REALLOCATING_AUTO_LEAVE_SECONDS` / `ROSTER_CAP`） | [【FT-08-FSD】gacha-system.md](【FT-08-FSD】gacha-system.md)、[【FT-12-FSD】staff-system.md](【FT-12-FSD】staff-system.md)（消費端：聚合上限 4 + 進入 Working 冷卻 + 自動轉假閾值 + roster cap） |
| [`【FT-09-DS】faction-route-table.md`](../Data-Specs/【FT-09-DS】faction-route-table.md) | `FactionRouteTable.csv` | FT-09 | [【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md) |
| [`【FT-09-DS】story-stage-table.md`](../Data-Specs/【FT-09-DS】story-stage-table.md) | `StoryStageTable.csv` | FT-09 | [【FT-09-FSD】faction-story-system.md](【FT-09-FSD】faction-story-system.md) |

> **維護指引**：每次有新 FSD 引用某 Data-Specs，於本表「被引用的 FSD」欄追加。每次新增 Data-Specs，於本表新增一列並同步 §6.1。
>
> **§6.2 §F-01-DS 消費端追加**：FT-08 FSD 消費 `OFFLINE_MAX_SECONDS`；列入 `【F-01-DS】system-constants.md` row「被引用的 FSD」欄末追加 `【FT-08-FSD】gacha-system.md（消費端：OFFLINE_MAX_SECONDS）`（待後續 patch）。

---

## 七、狀態記錄（Status Log）

### 7.1 撰寫進度

| FSD 檔案 | 對應 GDD | 狀態 | 撰寫者 | 起始日期 | 完成日期 | 備註 |
| --- | --- | --- | --- | --- | --- | --- |
| `【F-02-FSD】time-system.md` | F-02 | 已完成 | unity-specialist subagent | 2026-04-26 | 2026-04-26 | — |
| `【F-01-FSD】data-manager.md` | F-01 | 已完成 | unity-specialist subagent；Claude Code 主體（裁決 patch） | 2026-04-26 | 2026-04-26 | 逆向 FSD：依既有 Script 反推；§8.3 條目 3 已採方案 A 補實作 `GetString` / `GetBool`（GDD §3.3 / §4.3 / §4.4 已回註）；其餘 8 項偏差於 2026-04-26 patch 全數回註 GDD（§7.2 補登 7 筆）。**[2026-04-28 排查] 全部已修復** |
| `【F-03-FSD】resource-management.md` | F-03 | 已完成 | unity-specialist subagent；Claude Code 主體（D1+D2+D7 Codex 工項審查 + D3/D6 GDD 回註 patch） | 2026-04-26 | 2026-04-26 | 逆向 FSD（方向 C 保留 636 行單檔）；§8.3 8 項偏差。**[2026-04-28 排查]** ✓ D1/D2/D7（Codex Medium 落地：`SetBankruptcyWarningDuration` API、deprecated 表查詢移除、Warning 維持分支重新鎖定移除）；✓ D3/D6（F-03 GDD 已回註）；✓ D5（對齊 F-02 無偏差）；**未修復**：D4（待 FT-10 FSD 對齊 ISaveable 介面）、D8（636 行拆分待 D1/D4 落地後評估，目前卡 D4） |
| `【C-01-FSD】mission-database.md` | C-01 | 審查中 | unity-specialist subagent；Claude Code 主體（P-01 覆核 patch） | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script）；§8.5 P-01「分鐘 Tech Debt」主體覆核非衝突（F-02/FT-02/C-01 三系統共識，FSD 沿用合規）。**[2026-04-28 排查]** ✓ P-01；**未修復**：P-02（護送難度集合 {D,C,B,A} 表格化建議）、P-03（MissionNamePool DS 待建）—皆建議項；FSD 仍標審查中，待主體最終轉「已完成」 |
| `【C-02-FSD】adventurer-management.md` | C-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script）；無衝突。**[2026-04-28 排查] 未修復**：B-01（lastAutoPickupTimestamp「不受狀態轉移影響」回註，建議 FT-03 FSD 確認）、B-02（GetRecruitCost 歸屬 IAdventurerRoster vs IAdventurerTemplateLoader 未來考慮）—皆建議項 |
| `【C-03-FSD】profession-system.md` | C-03 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script）；無衝突。**[2026-04-28 排查]** ✓ B-01（GDD P-003 已將 raceWeights/raceIDs 長度驗證歸屬明文於 C-03 Loader）；**全部已修復**；FSD 仍標審查中，待主體最終轉「已完成」 |
| `【C-04-FSD】race-system.md` | C-04 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script）；無衝突。**[2026-04-28 排查]** ✓ B-01（GDD P-003 反轉至 C-03 Loader，C-04 §5 偽碼保留 defensive 雙保險）；✓ B-02（GDD P-004 §3.1 已加 raceID=1 保留 ID note）；**全部已修復** |
| `【C-05-FSD】trait-system.md` | C-05 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script）；無衝突。**[2026-04-28 排查] 未修復**：B-01（effectTarget 23 項 HashSet 維護於 TraitDatabaseLoader）、B-02（weighted pickMode fallback uniform 預留）—皆建議項 |
| `【FT-01-FSD】adventurer-recruitment.md` | FT-01 | 審查中 | unity-specialist subagent；Claude Code 主體（拆分情形誤標 patch） | 2026-04-27 | 2026-04-27 | 正向 FSD；**未拆分**（3 Script：RecruitmentTypes / RecruitmentPoolGenerator / RecruitmentService，預估 600~770 行）；subagent 拆分誤標已 patch；無真實衝突；§4.2 OnPoolRefreshedEvent 統一至 ExecuteRefresh()（GDD §4.2 已回註）。**[2026-04-28 排查]** ✓ subagent patch、§4.2 GDD 回註；**未修復**：B-01（P-03 Notification 待設計）、B-02（AdventurerRankUtil 歸屬待 C-02 FSD 確認） |
| `【FT-02-FSD-A】mission-dispatch-core.md` | FT-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；FSD 拆分 A/B（A=成功率+派遣+計時，B=CommissionBoard）。**[2026-04-28 排查] 未修復**：D-01（MissionTimer.cs 位於 Core/Time，待決策遷移至 Gameplay/Mission 或移除）；已知 Tech Debt 保留：TD-01（duration 分鐘，C-01/F-02/FT-02 三系統共識） |
| `【FT-02-FSD-B】commission-board.md` | FT-02 | 審查中 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | FT-02 拆分子單元 B（CommissionBoard 池管理）；無衝突；ISaveable 共用 OwnerKey 協調已於 §5.3 說明。**[2026-04-28 排查] 未修復**：B-01（categoryID 命名常數包裝）、B-02（GetAvailableCommissions 合集去重策略）—皆建議項 |
| `【FT-03-FSD】npc-decision-system.md` | FT-03 | 已完成 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（3 Script：NpcDecisionTypes / INpcDecisionService / NpcDecisionService）；無衝突。**[2026-04-28 排查] 未修復**：B-01（MissionDifficultyUtil 歸屬待確認）、B-02（P-03 Log API 待更新）；已知 Tech Debt 保留：TD-01（AUTO_PICKUP_*_MINUTES 分鐘命名，載入時 ×60 換算秒） |
| `【FT-04-FSD】outcome-resolution.md` | FT-04 | 已完成 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script：OutcomeData / IOutcomeResolutionService / OutcomeResolutionService / OutcomeReputationCalculator）；無真實衝突。**[2026-04-28 排查] 未修復**：B-01（Outcome 物件引用修改風險，Game Jam 訂閱者自律）、B-02（空介面層取捨）—皆建議項 |
| `【FT-05-FSD】guild-gold-flow.md` | FT-05 | 已完成 | unity-specialist subagent | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（3 Script：GoldFlowTypes / IGoldFlowService / GoldFlowService，預估 350~420 行）；無真實衝突。**[2026-04-28 排查]** ✓ B-03（GDD P-001 已落地，§3.2/§3.9.1/§3.9.2/§6.3 source 型別 CommissionSource→DispatchSource，GoldFlowTypes 移除自定 enum 改引用 FT-02-A）；**未修復**：B-01（F-03 GDD 補「單次 netDelta 無雙重穿越零線」保證）、B-02（P-02 clamp 提示，待 P-02 設計） |
| `【C-06-FSD】world-danger-system.md` | C-06 | 審查中 | Claude Code 主體（直接撰寫，無 subagent） | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（5 Script：WorldDangerData / MissionPoolWeights / IWorldDangerService / WorldDangerLoader / WorldDangerService，預估 410~530 行）；無真實衝突。**[2026-04-28 排查]** ✓ B-01/B-02（GDD P-005 直接落地 §4.3/§4.5）；✓ §8.4 兩條 GDD 回註意圖（GDD 直接落地，意圖已撤銷）；**未修復**：B-03（Loader 啟動時做完整 5 階存在性檢查—實作期建議） |
| `【FT-06-FSD】guild-core.md` | FT-06 | 已完成 | Claude Code 主體（直接撰寫，無 subagent） | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（5 Script：GuildCoreTypes / GuildCoreConstants / GuildLevelDatabaseLoader / GuildNameUtility / GuildCoreService，預估 650~870 行）；無真實衝突。**[2026-04-28 排查] 未修復**：B-01（FT-06-DS guild-level-table.md 待建）、B-02（BankruptcyWarningState enum 來源 F-03 待確認）、B-03（DataManager.GetTable<T> 簽名待確認）、B-04（payload deprecated 欄位下次 review 移除）、B-05（Pending 期間 UI 阻塞屬 P-02 範疇） |
| `【FT-07-FSD】guild-building-system.md` | FT-07 | 已完成 | Claude Code 主體（直接撰寫，無 subagent） | 2026-04-27 | 2026-04-27 | 正向 FSD；未拆分（4 Script：BuildingTypes / IBuildingService / BuildingTableLoader / BuildingService，預估 600~800 行）；無真實衝突。**[2026-04-28 排查] 未修復**：B-01（FT-07-DS building-table.md 待建）、B-02（FT-08/FT-12 消費 BuildingTable 路徑待確認）、B-03（P-03 Log API 待更新）、B-04（ISaveable 簽名待 FT-10 定案）、B-05（Phase 2 啟用旗標） |
| `【FT-08-FSD】gacha-system.md` | FT-08 | 審查中 | Claude Code 主體（直接撰寫，無 subagent；Opus 4.7 + xhigh） | 2026-04-28 | 2026-04-28 | 正向 FSD（無既有 Gacha / Staff Script）；FSD 未拆分（5 Script：GachaTypes / IGachaService / GachaTableLoader / GachaRollEngine / GachaService，預估 1170~1460 行）；GDD §3.1~§3.7 全部「對齊」；GDD §4.1.1~§4.1.9 共 9 條公式直接採用偽碼（未替代）；GDD §5.1~§5.6 共 21 條邊緣案例皆有對策；無真實衝突；建議項 B-01（5 份 FT-08-DS 待建）/B-02（FT-12-DS 待建影響反查 schema）/B-03（TrashItemTable 是否帶 drawWeightTier1~5 欄位待 DS-designer 確認）/B-04（GDD §6.5 補登 `MIN_AUTO_REFRESH_INTERVAL_SEC`）/B-05（`MAX_RESERVE_FALLBACK` 在 N=1 fallback 用法）/B-06（事件 struct 命名 `OnStaffSystemBootEvent`）/B-07（reserveConsumedFlag 生命週期細節）/B-08（GDD §3.3.5 `FT-12 FT-12 FT-12` 筆誤）皆不阻礙實作；§8.4 無 GDD 回註；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| `【FT-09-FSD】faction-story-system.md` | FT-09 | 審查中 | Claude Code 主體（直接撰寫，無 subagent；Opus 4.7 + xhigh） | 2026-04-28 | 2026-04-28 | 正向 FSD（無既有 Faction / Story Script）；FSD 未拆分（5 Script：FactionStoryTypes / IFactionStoryService / FactionStoryTableLoader / FactionStoryScoreAccumulator / FactionStoryService，預估 1000~1200 行）；GDD §3.1~§3.7 共 7 子節全部「對齊」；GDD §4 公式 F-1~F-3 直接採用偽碼，F-4 / F-5 為 offline 工具不在 runtime；GDD §5 共 12 條 EC 皆有對策、涉及 Script、驗證方式；GDD §8 AC-F-1~F-8 / AC-EC-1~12 / AC-D-1~9 全對齊 §1.3 / §7；無真實衝突；建議項 B-01（FT-02 InjectStaticMission enum 確認）/B-02（DialogueTable owner 待 P-02 GDD 定案）/B-03（P-02 / P-03 待設計）/B-04（ISaveable 簽名待 FT-10 定案）/B-05（GDD §3.1.2 Step F stage 不存在分支補洞）/B-06（GDD §3.3.2 Step 1 categoryID=3 跨節依賴補註）皆不阻礙實作；§8.4 兩條 GDD 回註意圖（§3.1.2 / §3.3.2）待主體覆核後寫入 GDD；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| `【FT-10-FSD】save-load-system.md` | FT-10 | 審查中 | Claude Code 主體（直接撰寫，無 subagent；Opus 4.7 + xhigh） | 2026-04-28 | 2026-04-28 | 正向 FSD（無既有 Save/Load Script）；FSD 未拆分（6 Script：SaveLoadTypes / ISaveable / ISaveLoadService / SaveFileIO / SaveLoadBootstrap / SaveLoadService，預估 1000~1310 行）；GDD §3.1~§3.7 共 22 子節全部「對齊」；GDD §4.1~§4.5 公式 5 條直接採用偽碼（未替代）；GDD §5 共 12 條 EC（EC-1~EC-12）皆有對策、涉及 Script、驗證方式；GDD §8 AC-1.1~AC-1.6 / AC-2.1~AC-2.7 / AC-3.1~AC-3.5 / AC-4.1~AC-4.3 / AC-5.1~AC-5.2 / AC-6.1~AC-6.2 / AC-7.1~AC-7.3 / AC-EC-1~AC-EC-12 共 38 條全對齊 §1.3 DoD-01~DoD-14；GDD §6.1~§6.4 雙向依賴 15 系統 + 反向依賴清單全部覆蓋；無真實衝突；建議項 B-01（SaveLoadService.cs 行數監控）/B-02（P-02 P-03 待設計）/B-03（ISaveable null 契約傳遞至各 owner FSD）/B-04（GetSystemConstant<string> 確認）/B-05（Bootstrap retry 冪等性）/B-06（OnApplicationQuit Editor 模擬）/B-07（F-02 Initialize 時序 invariant）/B-08（終末檔列舉 wrapper）皆不阻礙實作；§8.4 無 GDD 回註；§8.5 無衝突紀錄；待主體複核後轉「已完成」 |
| `【FT-12-FSD】staff-system.md` | FT-12 | 審查中 | Claude Code 主體（直接撰寫，無 subagent；Opus 4.7 + xhigh） | 2026-04-28 | 2026-04-28 | 正向 FSD（無既有 Staff Script）；FSD 未拆分（5 Script：StaffTypes / IStaffService / StaffTableLoader / StaffEffectAggregator / StaffService，預估 1280~1640 行）；GDD §3.1~§3.11 共 11 子節全部「對齊」；GDD §4.1 聚合上限公式直接採用偽碼，§4.2 薪水公式 Phase 2 整段不執行；GDD §5.1~§5.5 共 17 條 EC 皆有對策、涉及 Script、驗證方式；GDD §8 AC-1 / AC-3~AC-35 全對齊 §1.3（AC-2 / AC-21~AC-25 / AC-36 屬 Phase 2 不驗收）；無真實衝突。**[2026-04-28 design-review patch]** ✓ B-11（FT-07 API 對齊：`GetBuildingState`+`GetSlotCount` → `GetBuildingLevel` + `IDataManager.Get<BuildingData>` 直讀，§2.3 / §5.4.3 step 7 / §6.1 / §6.3 / §4.4 五處同步；§8.4 加 GDD 回註意圖待 FT-12 GDD §6.1 row 4 同步）；§5.2 補事件 struct `OnXxxEvent` 後綴 note；§5.4.1 step 5 採 `StaffPhase2.SalaryEnabled = false` C# const；新增 §5.4.12 Subscribe / Unsubscribe 對稱生命週期；§5.4.10 dueTimestamp 來源澄清；§5.4.11 step 3 排序加 instanceID 次序鍵。**未修復**：B-01 / B-02 / B-04（DS / FSD 待建）、B-03（DS owner 標註）、B-05（F-02 OnHourTick 缺位 ⇒ 已採 OnMinuteTick + 自節流，無需修正）、B-06（StaffService Phase 2 拆分預留）、B-07（CandidateCard 跨型別 reference）、B-08（M-3 Jam vs Phase 2 分支）、B-09（守護已涵蓋）、B-10（EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC 單位混型）、D-01（Auto-leave 自節流間隔表格化建議）皆為實作期 / 跨 FSD 同步建議 |

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

> **紀錄載體已外移**：完整自檢紀錄表請見 [`FSD-self-check-log.md`](./FSD-self-check-log.md)。本節僅保留章節錨點與規範說明，方便其他文件以「§7.2」引用；新增列一律寫入 `FSD-self-check-log.md`。

**檢核結果記法**：`通過` / `未通過`（未通過時於「未對齊摘要」欄填入問題與後續處理計畫）。

<!-- 原始紀錄表格已搬至 FSD-self-check-log.md，新增列請寫入該檔 -->

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
| 2026-04-28 | FT-08 FSD 撰寫完成（未拆分，5 Script）：(1) §6.1 FT-08 row 更新 Data-Specs（5 份 _待建_ DS）+ FSD 連結 + 拆分情形「未拆分」+ Script 預估規模；(2) §6.2 新增 5 份 FT-08-DS 待建登記列；維護指引追加 F-01-DS system-constants 消費端 FT-08 待 patch 提示；(3) §7.1 新增 FT-08-FSD 列（狀態：審查中）；(4) §7.2 新增 FT-08-FSD review 紀錄（GDD §3.1~§3.7 全對齊、§4.1.1~§4.1.9 公式直接採用、§5.1~§5.6 共 21 條 EC 對策、AC-1~AC-25 全對齊、8 條建議項皆不阻礙實作）；(5) §7.3 不更新（未拆分） | Claude Code 主體（Opus 4.7 + xhigh） |
| 2026-04-28 | FT-09 FSD 撰寫完成（未拆分，5 Script）：(1) §6.1 FT-09 row 由 _待撰寫_ 改為 FSD 連結 + Data-Specs 4 份引用（FT-09-DS 2 份 + C-01-DS / F-01-DS 消費端）+ 拆分情形「未拆分」+ Script 預估 1000~1200 行；(2) §6.2 新增 2 份 FT-09-DS row（faction-route-table / story-stage-table）；F-01-DS system-constants「被引用 GDD」追加 FT-09、「被引用 FSD」追加 FT-09-FSD（消費端：FACTION_NEUTRAL_ID）；C-01-DS mission-difficulty-table「被引用」雙欄追加 FT-09 消費 factionScoreDelta；C-01-DS mission-template「被引用」雙欄追加 FT-09 消費 categoryID/factionID；(3) §7.1 新增 FT-09-FSD 列（狀態：審查中）；(4) FSD-self-check-log.md（§7.2 已外移）新增 FT-09-FSD review 紀錄（GDD §3.1~§3.7 全對齊、F-1~F-3 直接採用 / F-4~F-5 不在 runtime、12 條 EC 全對策、AC-F/EC/D/T 全對齊、6 條建議項皆不阻擋實作、2 條 GDD 回註意圖）；(5) §7.3 不更新（未拆分） | Claude Code 主體（Opus 4.7 + xhigh） |
| 2026-04-28 | FT-12 FSD 撰寫完成（未拆分，5 Script）：(1) §6.1 FT-12 row 由 _待撰寫_ 改為 FSD 連結 + Data-Specs 3 份引用（FT-12-DS staff-table / FT-08-DS staff-tuning 共用 / F-01-DS 消費端 OFFLINE_MAX_SECONDS Phase 2）+ 拆分情形「未拆分」+ Script 預估 1280~1640 行；(2) §6.2 FT-08-DS staff-tuning「被引用 GDD」FT-12 由「待 FT-12 FSD 定案」改為實際 7 個 key 列舉（4 個 EFFECT_MAX_* + BUILDING_SWITCH_COOLDOWN_SECONDS + REALLOCATING_AUTO_LEAVE_SECONDS + ROSTER_CAP）、「被引用 FSD」追加 FT-12-FSD（消費端：聚合上限 4 + 進入 Working 冷卻 + 自動轉假閾值 + roster cap）；F-01-DS system-constants「被引用 GDD」追加 FT-12、「被引用 FSD」追加 FT-12-FSD（消費端：OFFLINE_MAX_SECONDS Phase 2）；(3) §7.1 新增 FT-12-FSD 列（狀態：審查中）；(4) FSD-self-check-log.md 新增 FT-12-FSD review 紀錄（GDD §3.1~§3.11 共 11 子節全對齊、§4.1 公式直接採用 / §4.2 Phase 2 整段不執行、§5.1~§5.5 共 17 條 EC 全對策、AC-1 / AC-3~AC-35 全對齊、AC-2 / AC-21~AC-25 / AC-36 屬 Phase 2 不驗收、10 條建議項 + 1 條 D 級建議皆不阻擋實作、無 GDD 回註、無衝突紀錄）；(5) §7.3 不更新（未拆分） | Claude Code 主體（Opus 4.7 + xhigh） |
| 2026-04-28 | FT-10 FSD 撰寫完成（未拆分，6 Script）：(1) §6.1 FT-10 row 由 _待撰寫_ 改為 FSD 連結 + Data-Specs 引用 F-01-DS system-constants 消費端（5 keys）+ 拆分情形「未拆分（6 Script，預估 1000~1310 行）」；(2) §6.2 F-01-DS system-constants「被引用 GDD」追加 FT-10、「被引用 FSD」追加 FT-10-FSD（消費端：SAVE_AUTO_INTERVAL_SEC / SAVE_BACKUP_COUNT / SAVE_FILE_NAME / SAVE_BAK_PREFIX / SAVE_GAMEOVER_PREFIX）；(3) §7.1 新增 FT-10-FSD 列（狀態：審查中）；(4) FSD-self-check-log.md（§7.2 已外移）新增 FT-10-FSD review 紀錄（GDD §3.1~§3.7 全對齊、§4.1~§4.5 公式 5 條直接採用、§5 12 條 EC 全對策、AC 38 條全對齊 DoD-01~14、§6.1~§6.4 雙向依賴 15 系統覆蓋、8 條建議項皆不阻擋實作）；(5) §7.3 不更新（未拆分） | Claude Code 主體（Opus 4.7 + xhigh） |
