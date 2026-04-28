# The Guild — Implementation Guide

_2026-04-28 | 動工前唯一查閱入口_

---

## 1. 規範來源（不重複，僅引用）

| 主題                                                          | 來源                               |
| ------------------------------------------------------------- | ---------------------------------- |
| 全域行為準則、工項分級、審查模型強度、Codex MCP 規則          | `~/.claude/CLAUDE.md`              |
| 專案技術棧、目錄結構、命名、語言／單位、MCP 工具、commit 規範 | `CLAUDE.md`                        |
| Gameplay 程式碼規則                                           | `.claude/rules/gameplay-code.md`   |
| UI 程式碼規則                                                 | `.claude/rules/ui-code.md`         |
| CSV 結構與命名                                                | `.claude/rules/data-files.md`      |
| Unity C# coding standards、測試目錄結構                       | `.claude/docs/coding-standards.md` |
| 設計文件規則                                                  | `.claude/rules/design-docs.md`     |
| GDD 索引、依賴圖、SystemConstants、參數表清單                 | `design/GDD/systems-index.md`      |
| FSD 索引、Service 契約（§2.10）、FSD 模板（§8）               | `design/FSD/FSD-index.md`          |
| Data-Specs 索引                                               | `design/Data-Specs/data-index.md`  |
| 反向依賴索引                                                  | `design/GDD/DIP-index.md`          |

本守則僅補充三項上述未涵蓋的內容：來源層級（§2）、Service 契約速查（§3）、實作順序與進度（§5、§6）。

---

## 2. 來源層級

```
GDD（規則）→ FSD（規格）→ Data-Specs（CSV schema）→ CSV（內容）→ Script
```

- 動工依據 = FSD。
- FSD 不揣測 GDD；衝突依 `FSD-index.md` §2.5 流程仲裁。
- CSV 內容值來自 Google Sheets master（見 memory `reference_google_drive.md`）。

---

## 3. Service 契約速查（節錄自 FSD-index §2.10）

FSD 內的 `IXxxService` 命名僅供敘述。實作直呼 concrete singleton，禁新增 interface 包裝：

| FSD 敘述                                | 實作                                          |
| --------------------------------------- | --------------------------------------------- |
| `IDataManager.X`                        | `DataManager.Instance.X`                      |
| `ITimeService.X` / `ITimeSystem.X`      | `TimeSystem.Instance.X`                       |
| `IResourceService.X`                    | `ResourceManagement.Instance.X`               |
| `IEventBus.Subscribe<T>` / `Publish<T>` | static `EventBus.Subscribe<T>` / `Publish<T>` |

---

## 4. 動工前 Checklist

逐項勾選，未通過禁止動工。

### 4.1 設計層

- [ ] FSD 狀態 = `已完成`（`design/FSD/FSD-index.md` §7.1）
- [ ] FSD §7.1 `[2026-04-28 排查]` blocker 已落地或裁決
- [ ] FSD §2.3 上游系統皆已實作（Glob `Assets/Scripts/` 確認）
- [ ] FSD §6.1 引用的 Data-Specs 全部存在

### 4.2 資料層

- [ ] CSV 已存在於 `Assets/Resources/Data/`，無則先寫
- [ ] CSV 符合 `.claude/rules/data-files.md`
- [ ] 時間欄位以 `_sec` / `_hours` 命名
- [ ] FSD §2.2 / §6.1 / Data-Specs 三處欄位雙向對齊

### 4.3 工項分級與動工路線

- [ ] 依 `~/.claude/CLAUDE.md` 評估 Large / Medium / Small
- [ ] Large / Medium：Codex 實作（read-only）→ Opus + xhigh 審查 → APPROVED → Codex 寫入（workspace-write）
- [ ] Small：subagent（Sonnet + high）閉環 → Opus + xhigh 審查 + 寫入
- [ ] 列出 FSD §4.4 Script 清單作為實作 checklist
- [ ] 列出 FSD §1.3 DoD 作為驗收 checklist

### 4.4 動工後

- [ ] EditMode 測試（首選）／ PlayMode 測試齊備
- [ ] UnityMCP `refresh_unity` + `read_console` 零編譯錯誤
- [ ] UnityMCP `run_tests` 全綠
- [ ] Commit（規範見 `CLAUDE.md` §語言與單位規範）
- [ ] 本守則 §6 進度表登記

---

## 5. 實作順序

### 5.1 Foundation（已落地）

| 系統                     | 路徑                      | 待修                                                                                                                                     |
| ------------------------ | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| F-01 DataManager         | `Core/Data/`              | 偏差全修（2026-04-28）                                                                                                                   |
| F-02 Time System         | `Core/Time/TimeSystem.cs` | FT-02-A 動工時移除 `MissionTimer.cs` + `_missionTimers` + `OnMissionExpiredEvent` + `OfflineSummary.CompletedMissionInstanceIds`（D-01） |
| F-03 Resource Management | `Gameplay/Resources/`     | D4 ISaveable 簽名已對齊；§6.3 契約段落待 Codex 補實作；D8 拆分待重評                                                                     |

### 5.2 Core 層

| #   | 系統                       | 上游                   | 並行條件   |
| --- | -------------------------- | ---------------------- | ---------- |
| 1   | C-01 Mission Database      | F-01                   | 與 #2 並行 |
| 2   | C-03 Profession System     | F-01                   | 與 #1 並行 |
| 3   | C-04 Race System           | F-01、C-03             | 與 #4 並行 |
| 4   | C-05 Trait System          | F-01、C-03             | 與 #3 並行 |
| 5   | C-02 Adventurer Management | F-01、C-03、C-04、C-05 | 與 #6 並行 |
| 6   | C-06 World Danger System   | F-01、F-03             | 與 #5 並行 |

### 5.3 Feature 層

| 階段 | #   | 系統                          | 上游                             | 備註                       |
| ---- | --- | ----------------------------- | -------------------------------- | -------------------------- |
| F1   | 7   | FT-02-A Mission Dispatch Core | C-01/02/03/04/05、F-02、F-03     | 同時執行 D-01 移除舊計時器 |
| F1   | 8   | FT-02-B Commission Board      | FT-02-A、C-01                    | 序列於 #7 後               |
| F1   | 9   | FT-04 Outcome Resolution      | FT-02、F-03、C-01、C-02          | 序列於 #8 後               |
| F2   | 10  | FT-06 Guild Core              | F-03、F-01                       | 可與 F1 並行               |
| F2   | 11  | FT-07 Guild Building System   | FT-06、F-03                      | 序列於 #10 後              |
| F3   | 12  | FT-12 Staff System            | FT-06、FT-07、F-01、F-02         | Phase 2 薪水管線不啟用     |
| F3   | 13  | FT-08 Gacha System            | FT-06、FT-07、FT-12              | 序列於 #12 後              |
| F4   | 14  | FT-01 Adventurer Recruitment  | C-02/03/04/05、F-03、FT-12       | 與 #15 並行                |
| F4   | 15  | FT-03 NPC Decision System     | FT-02、C-02、C-05、F-02、FT-12   | 與 #14 並行                |
| F5   | 16  | FT-05 Guild Gold Flow         | FT-02、FT-04、F-03、FT-07、FT-12 | 與 #17 並行                |
| F5   | 17  | FT-09 Faction Story System    | FT-04、C-01、F-01                | 與 #16 並行                |
| F6   | 18  | FT-10 Save/Load System        | ALL                              | 必須最後；獨佔             |

### 5.4 暫緩／範疇外

| 系統                     | 處理                                                  |
| ------------------------ | ----------------------------------------------------- |
| P-01 Desktop Window      | FSD 未啟動；Game Jam 用 Unity 預設視窗                |
| P-02 Main UI Framework   | 設計暫停（memory `project_p02_blocked.md`），等待通知 |
| P-03 Notification System | FSD 未啟動；LogWarning 改呼叫為建議事項               |
| D-01 / D-02 Content DBs  | GDD 待設計；需 narrative 配合                         |
| FT-11 Offline Resolver   | Game Jam 範疇外，不實作                               |

### 5.5 並行上限參考（4 週）

```
週 1：[C-01 + C-03] → [C-04 + C-05]
週 2：[C-02 + C-06 + FT-06] → [FT-02-A → FT-02-B] → [FT-07 + FT-04]
週 3：[FT-12 → FT-08] → [FT-01 + FT-03]
週 4：[FT-05 + FT-09] → FT-10
```

實際並行容量由使用者裁定。

---

## 6. 進度追蹤

| #   | 系統                          | 狀態   | 開始 | 完成          | 備註                         |
| --- | ----------------------------- | ------ | ---- | ------------- | ---------------------------- |
| —   | F-01 DataManager              | 已完成 | —    | 2026-04-26 前 | 偏差全修                     |
| —   | F-02 Time System              | 已完成 | —    | 2026-04-26 前 | MissionTimer 待 FT-02-A 移除 |
| —   | F-03 Resource Management      | 已完成 | —    | 2026-04-26 前 | §6.3 契約段落待補            |
| 1   | C-01 Mission Database         | 已完成 | 2026-04-28 | 2026-04-28 | C-01-FSD Codex Medium 落地；附帶修正 SystemConstants.ESCORT_TYPE_ID 4→2 |
| 2   | C-03 Profession System        | 待     |      |               |                              |
| 3   | C-04 Race System              | 待     |      |               |                              |
| 4   | C-05 Trait System             | 待     |      |               |                              |
| 5   | C-02 Adventurer Management    | 待     |      |               |                              |
| 6   | C-06 World Danger System      | 待     |      |               |                              |
| 7   | FT-02-A Mission Dispatch Core | 待     |      |               | 同步執行 D-01                |
| 8   | FT-02-B Commission Board      | 待     |      |               |                              |
| 9   | FT-04 Outcome Resolution      | 待     |      |               |                              |
| 10  | FT-06 Guild Core              | 待     |      |               |                              |
| 11  | FT-07 Guild Building System   | 待     |      |               | T21 Phase 2 旗標待裁         |
| 12  | FT-12 Staff System            | 待     |      |               | Phase 2 薪水不啟用           |
| 13  | FT-08 Gacha System            | 待     |      |               |                              |
| 14  | FT-01 Adventurer Recruitment  | 待     |      |               |                              |
| 15  | FT-03 NPC Decision System     | 待     |      |               |                              |
| 16  | FT-05 Guild Gold Flow         | 待     |      |               |                              |
| 17  | FT-09 Faction Story System    | 待     |      |               |                              |
| 18  | FT-10 Save/Load System        | 待     |      |               | 必須最後                     |

狀態值：`待` / `進行中` / `審查中` / `已完成` / `阻塞`。阻塞時於備註欄填阻塞原因與處理計畫。

---

## 7. 變更歷史

| 日期       | 內容                                               |
| ---------- | -------------------------------------------------- |
| 2026-04-28 | 建立守則                                           |
| 2026-04-28 | 對齊 CLAUDE.md，重複內容改為連結引用；改寫為指令式 |
