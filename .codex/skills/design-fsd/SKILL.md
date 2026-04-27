---
name: design-fsd
description: "Generate Functional Specification Documents from one or more GDD documents for The Guild, including implementable rules, dependencies, APIs, edge cases, acceptance criteria, and FSD index updates. Use for FSD authoring from GDD sources."
---

## Skill Contract

Use this skill when the user invokes $design-fsd or asks for work matching the description. This skill is compatible with both Codex and GPT contexts.

- In Codex, inspect repository files first, edit only the requested artifacts, and verify with available local tools when changes are made.
- In GPT or chat-only contexts, provide structured reasoning, specs, review notes, and implementation-ready guidance without assuming direct file access.
- Treat legacy slash-command examples as invocation examples; prefer $design-fsd <args> when referring to this skill.
- If a referenced tool, MCP server, or runtime integration is unavailable, use the closest available local workflow and state the limitation.

全自動 skill：不互動、不問問題、不分階段確認。**兩個例外**：(1) 模型能力不足時先降級為規劃或審查模式；(2) 偵測到 GDD 規則衝突時（FSD-index §2.5）暫停並等待使用者裁決。

---

## 0. 規範來源

本 skill 的一切撰寫規則來自 `design/FSD/FSD-index.md`。遇到 skill 指令與 FSD-index 規範不一致時，**以 FSD-index 為準**。

---

## 1. Parse 輸入

- 以逗號 `,` 與任意空白切分 args；空輸入直接報錯：
  > `Usage: $design-fsd <GDD-id|name>[,<another>...]，例：$design-fsd C-01,FT-01`
- 每個 token 解析為 GDD：
  - 看似系統 ID（regex `^[A-Z]+-\d+$`，如 `F-01` / `C-02` / `FT-08`）→ Glob `design/GDD/【<id>】*.md`
  - 否則視為 GDD 名稱關鍵字 → Glob `design/GDD/*<token>*.md`
- 任一 token 解析失敗或命中多筆 → 中止並列出未解析項，**不部分執行**
- 解析結果轉成有序清單，**依序**（非並行）處理

---

## 2. 共用素材一次性讀取

讀取下列檔案（每份僅讀一次，不重複讀）：

- `design/FSD/FSD-index.md` — **主規範**：撰寫準則、章節格式、命名規則、衝突流程、Review 流程、模板
- `design/GDD/systems-index.md` — 系統依賴關係概覽（§2.3、§2.4 依賴列舉用）

---

## 3. 逐 GDD 依序執行（不並行）

對清單中每個 GDD 依序執行以下全部步驟，前一個 GDD 完成後才處理下一個。

### 3.1 前置確認

讀取 `design/FSD/FSD-index.md` 的 §6.1 三方對應表 與 §7.1 撰寫進度，確認：

- 若 §7.1 已有對應 FSD 且狀態為 `已完成` → **跳過**，記入摘要（跳過原因：已完成）
- 若狀態為 `草稿` 或 `審查中` → **跳過**，記入摘要（跳過原因：撰寫中，請先處理現有草稿）
- 若 §6.1 標記 `_待撰寫_` 或無對應列 → 繼續

### 3.2 讀取 GDD

讀取目標 GDD 完整內容。**不讀取**其他無關 GDD 全文（跨系統依賴只讀 systems-index.md 的摘要行）。

### 3.3 既有 Script 偏差檢查（§2.7）

依 GDD ID 對應的 Script 目錄執行 **1~2 次 Glob**，取得檔名清單：

- `TheGuild-unity/Assets/Scripts/Core/<對應子目錄>/` 或 `TheGuild-unity/Assets/Scripts/Gameplay/<對應子目錄>/`
- **只讀檔名，不讀內容**；偏差記入 FSD §8.3，不直接修改 Script

目錄對應原則：

| GDD 前綴 | Script 目錄 |
| --- | --- |
| F-0x | `Assets/Scripts/Core/` 對應子目錄 |
| C-0x | `Assets/Scripts/Gameplay/` 相關子目錄 |
| FT-0x | `Assets/Scripts/Gameplay/` 相關子目錄 |

### 3.4 FSD 撰寫

依 `design/FSD/FSD-index.md` §三（標準章節格式）與 §八（模板）撰寫 FSD。

**衝突即停（§2.5）**：撰寫過程中若發現 GDD 內部或跨 GDD 規則衝突：
1. 停止撰寫該 FSD
2. 查 GDD §5 邊緣案例、§6 上游依賴、§1 概要是否已有指引
3. 整理衝突點、查詢結果、建議解法，輸出格式：
   ```
   [design-FSD 衝突暫停] GDD: <ID>
   衝突點：<涉及章節與條目>
   查詢結果：<§5/§6/§1 有無指引>
   建議解法：<建議>
   請裁決後回覆以繼續。
   ```
4. 等待使用者裁決；裁決後於 FSD §8.5 登記並繼續

**命名規則**（FSD-index §5.2）：
- 未拆分：`design/FSD/【<系統ID>-FSD】<system-name>.md`
- 拆分後：`design/FSD/【<系統ID>-FSD-A】<sub-name>.md`（A、B、C...）

**拆分判斷**（FSD-index §2.4）：
- 預估單一 Script > 500 行 → 考慮拆分
- GDD 內有明顯職責分區 → 可拆分
- 不可拆解原子性操作

**工具呼叫上限參考 30 次**：超過時主動停下盤點剩餘工作。

### 3.5 Self-Review（§2.6）

草稿完成後執行三項檢核：

| 檢核項 | 通過條件 |
| --- | --- |
| 結構正確 | 章節編號、標題、順序與 FSD-index §三完全一致；每章皆有實質內容 |
| 邏輯正確 | 拆分理由成立、Script 職責清晰、API/事件/資料流前後一致、無自相矛盾 |
| 與 GDD 規則相符 | §3 詳細規則逐項對齊；§4 公式有對應實作；§5 邊緣案例皆有對策；§6 依賴皆列舉；§7 可調參數皆已表格化 |

任一項未通過 → 修正後再 review，直到三項全通過。

Review 結果填入 FSD 附錄 A。

### 3.6 完成前 Checklist（§2.9）

標記完成前逐項對照 FSD-index §2.9 checklist，確認全部勾選。

### 3.7 更新 FSD-index.md

Write/Edit `design/FSD/FSD-index.md`：

- **§6.1 三方對應表**：將 `_待撰寫_` 替換為 FSD 連結；若有拆分，更新「拆分情形」欄
- **§7.1 撰寫進度**：新增一列（FSD 檔案、對應 GDD、狀態 `審查中`、撰寫者 `gpt-5.5+` 或實際執行代理名稱、起始日期、完成日期、備註）
- **§7.2 GDD 規則自檢紀錄**：新增 review 記錄列
- **§7.3 拆分回報紀錄**：若有拆分，新增一列

---

## 4. 派發給子代理的紀律（§2.8）

若將 FSD 撰寫派發給其他代理，建議使用 **a capable reasoning model**，且 prompt 必須明列下列紀律：

1. **必讀文件清單上限 4 份**：FSD-index.md、目標 GDD、systems-index.md 摘要行、目標系統 Script 路徑（Glob 一次取檔名）；不追加讀取其他 GDD 或 coding-standards 全文
2. **xhigh 思考用於推理**：拆分判斷、§3 映射、§7 邊緣案例對策、§8 對齊自檢四階段必須以推理完成，不靠多輪工具呼叫
3. **單次 Write 寫完草稿**：必須以單一 Write 完整輸出 FSD 檔案；後續修正才用 Edit
4. **工具呼叫上限參考 30 次**：超過時主動停下盤點剩餘工作
5. **Review 必填附錄 A**：完成後依 §2.6 自檢，並更新 FSD-index §6.1 / §7.1 / §7.2（必要時 §7.3）
6. **回報格式 ≤ 400 字**：包含檔案路徑、拆分決策、Review 三項結果、衝突與無法實現項、索引更新、下一步建議

---

## 5. 收尾輸出

所有 GDD 處理完畢後，僅輸出以下摘要（無多餘評論）：

```
FSD 產出完成

GDD: <list>
新增 FSD: <count>
  - design/FSD/【...】....md
  - ...（若拆分則逐一列出）
跳過（已完成）: <list>
跳過（撰寫中）: <list>
衝突暫停: <list>（若有）
FSD-index.md 已更新：§6.1 / §7.1 / §7.2（/ §7.3 若有拆分）
```



