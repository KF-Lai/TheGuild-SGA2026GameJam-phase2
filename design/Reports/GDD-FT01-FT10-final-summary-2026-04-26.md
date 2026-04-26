# GDD FT-01 ~ FT-10 設計審查最終總結

_完成日期:2026-04-26_
_審查者:Claude Opus 4.7 + max effort thinking + Codex MCP 終審_
_範圍:FT-01 ~ FT-10 + 連動的 F-02 / F-03 / C-02 / C-06 / game-concept / systems-index_

---

## 結論

**所有 GDD 已對齊修補,可進入 Codex 工項書建立階段。**

整個流程經歷:
- 主體 Opus + max effort 對 10 份 FT GDD 的序列審查
- 主體執行 Small 修改(直改)
- 5 個 Sonnet subagent 並行/序列執行 Medium/Large 修改
- 主體二次驗證
- Codex MCP 第一輪終審 → 主體修補 3 Critical + 7 Major + 2 Minor
- Codex MCP 第二輪確認 → 主體修補 3 個新發現的 Minor
- 主體最終驗證

修改範圍:**14 份 GDD + 1 份新規格書**(`VeteranRankWeightTable`),git diff 統計 +1000+ / -200+ 行。

---

## 修改檔案清單

### 設計文件(GDD)

| 檔案 | 主要變更 |
|---|---|
| `design/GDD/[F-02] time-system.md` | C1:移除計時器清單 + RegisterMission/UnregisterMission/OnMissionExpired 機制 |
| `design/GDD/[F-03] resource-management.md` | C4:新增 SetBankruptcyWarningDuration 推送 API + ISaveable 章節;§3.6/§4.5 改用 _currentWarningDuration |
| `design/GDD/[C-02] adventurer-management.md` | C3:補 idleSinceTimestamp/lastAutoPickupTimestamp 欄位 + 規則;M2:CreateRandomInstance + AddAdventurer isUnique 檢查;M10:UpdateStatus overload;補 ISaveable 章節 |
| `design/GDD/[C-06] world-danger-system.md` | 補 ISaveable 章節 |
| `design/GDD/[FT-01] adventurer-recruitment.md` | M1:VeteranRankWeightTable 資料驅動化;M3:改用 GetMaxRecruitableRank;M2:改用 CreateRandomInstance;m1~m6 minor 補完 + ISaveable + OnRecruitSuccess 事件契約 + isUnique=0 處理 |
| `design/GDD/[FT-02] mission-dispatch.md` | C1:作為單一計時/發布點;C2:新增 §3.9 委託板池服務(_regularMissionPool/_staticMissionPool/InjectStaticMission/OnCommissionPosted);C5:InjectStaticMission API;Codex Critical:UpdateStatus overload + PostRegularMission Jam 時序規則;補 ISaveable 章節 |
| `design/GDD/[FT-03] npc-decision-system.md` | M11:§6.1 補 FT-06 + §5.2 對齊 F-02 OnMinuteTick;補 §3.4/3.5 API+事件契約;m2 拒絕原因三階段;Codex Critical:改用 FT02.GetAvailableCommissions + FT06.GetMaxMissionDifficulty;補 ISaveable 薄層 |
| `design/GDD/[FT-04] outcome-resolution.md` | 無修改(Sonnet B 確認 OnMissionCompleted 已正確;C-05 §4.4 簽名核對為兩參數,FT-04 §3.2 step 7 已對齊) |
| `design/GDD/[FT-05] guild-gold-flow.md` | 無修改(設計品質典範,審查通過 APPROVED) |
| `design/GDD/[FT-06] guild-core.md` | M3:GetMaxRecruitableRank/GetMaxMissionDifficulty 拆分;M6:殘留 GetMaxDifficulty 全文清理;補 ISaveable 章節 |
| `design/GDD/[FT-07] guild-building-system.md` | C4:啟動推送 + 升級推送 SetBankruptcyWarningDuration;M5:職員休息室 L2~L5 數值對齊 §7.1;M6:§5.5 邊緣案例邏輯統一;補 ISaveable 章節 |
| `design/GDD/[FT-08] guild-staff-system.md` | M7~M9:§6.1 ID 標錯/補 FT-06/TryDeductGold→AddGold;§3.3.2 step 2 修;§6.6 雙向聲明同步;補 ISaveable 章節 |
| `design/GDD/[FT-09] faction-story-system.md` | C5:InjectStaticMission 對齊 OnCommissionPosted;Codex Major:OnStaticMissionPosted 殘留清理 + Dictionary→List Entry + record struct→readonly struct;補 ISaveable 章節 |
| `design/GDD/[FT-10] save-load-system.md` | C1:§3.3.3 row 6 改寫(FT-02 自重新訂閱 OnSecondTick);Codex Major:root JSON 補 ft03Decision |
| `design/GDD/game-concept.md` | M4:公會等級表 Phase 2 變更註記;Codex Minor:可接難度上限拆分(maxRecruitableRank=S / maxMissionDifficulty=SS) |
| `design/GDD/systems-index.md` | 命名一致(StoryNodeTable→StoryStageTable);FT-09/FT-10 進度狀態 ✅;新增 VeteranRankWeightTable / MissionFactionScoreWeight 條目;FT-08 狀態對齊 |

### 資料規格書

| 檔案 | 變更 |
|---|---|
| `design/data-specs/[FT-01-Data] veteran-rank-weight-table.md` | **新增**:5 階加權隨機表規格(對齊 FT-01 §4.4 資料驅動修整) |
| `design/data-specs/[F-03-Data] bankruptcy-threshold-table.md` | M1:標 Phase 2 deprecated;LookupWarningDuration 標 Phase 1 舊版行為 |

---

## 跨系統契約矛盾解決方向(已落地)

### C1: F-02 vs FT-02 雙重計時器機制 ✅
- **結論**:FT-02 為單一計時/發布點(activeMissions 為任務狀態源)
- **動作**:F-02 移除 RegisterMission/UnregisterMission API + OnMissionExpired 事件;FT-04 統一訂閱 FT-02.OnMissionCompleted;FT-10 §3.3.3 row 6 改寫為「FT-02 自重新訂閱 OnSecondTick」

### C2: 委託板池服務歸屬 ✅
- **結論**:FT-02 新增 §3.9 CommissionBoard 池服務,持有 `_regularMissionPool` + `_staticMissionPool`
- **動作**:FT-02 §3.9.x 完整定義池結構/InjectStaticMission API/OnCommissionPosted 事件/PostRegularMission Jam 時序(啟動填池 + 每日重置補池 + 危險度升階補池);FT-03 自主接單改用 GetAvailableCommissions

### C3: AdventurerInstance idleSinceTimestamp / lastAutoPickupTimestamp 欄位 ✅
- **結論**:C-02 §3.1 補欄位 + §3.4 補維護規則(進入 Idle 自動設、轉出時清);FT-03 透過 SetLastAutoPickupTimestamp 寫入

### C4: 破產倒數秒數權威 ✅
- **結論**:FT-07 預備金保險櫃等級為權威,F-03 改為被動接收
- **動作**:F-03 新增 SetBankruptcyWarningDuration 寫入 API,§4.5 改用 _currentWarningDuration;FT-07 §3.3 升級流程 + Start 啟動推送;BankruptcyThresholdTable.warningDurationSec 標 deprecated

### C5: FT-09 InjectStaticMission API ✅
- **結論**:與 C2 合併處理,FT-02 §3.9.2 提供 InjectStaticMission API + OnCommissionPosted(source=Static) 事件
- **動作**:FT-09 §3.5.3 對齊事件名稱

---

## 設計層面決策點(供使用者複審)

### 1. FT-07 IsCritical=false(M13 ISaveable)

依 FT-10 §3.3.4 既定分類,FT-07 為 Degradable,失敗時呼叫 `InitializeAsNewGame()` 將 6 棟建築重置為初始等級。**潛在問題**:玩家失去所有升級進度(可能達數千金幣不可逆損失)。

**Sonnet E 已在 FT-07 §6.5 標記決策記錄**;若認為應改 IsCritical=true,Sprint 時可調整。

### 2. isUnique=0 同批次去重

Sonnet D 採「同批次 templateID 去重」(避免兩個同模板的傢伙同時出現),但允許跨次刷新重複出現。

若設計意圖是「同一個普通模板可在同批次重複出現多次」,需修整 FT-01 §4.5 Phase 1 的 usedTemplateIDs 邏輯。建議**保留現行去重策略**(更貼合「招募是挑選不同夥伴」的玩家幻想)。

### 3. FT-06 GuildLevelTable.maxDifficulty deprecated

Sonnet D 拆分為 maxRecruitableRank + maxMissionDifficulty,保留 maxDifficulty 為 deprecated alias。

**Codex 第二輪確認**:FT-06 全文殘留 GetMaxDifficulty 已清理;FT-01 已切換到 GetMaxRecruitableRank。

CSV 實作時建議**只生成新欄位**,不寫入 maxDifficulty(對齊 deprecated 語意);FT-06 程式內以新欄位為準。

### 4. FT-02 §3.9.2 PostRegularMission 設計

Codex 第一輪指出原本「待補」會讓新遊戲委託板為空。已補完整 Jam 階段時序:

- **啟動填池**:Bootstrap 完成後填滿至 `FT07.GetMissionSlotCount()`
- **每日重置補池**:F-02 OnDailyReset → 計算缺額補滿
- **危險度升階補池**:C-06 OnDangerLevelChanged → 依新權重補滿
- **抽取規則**:依 C-06.GetPoolWeights 加權抽 difficulty → 用 C-01.GetRegularTemplates(difficulty) 取模板清單 → 均勻隨機;同 missionID 跳過(冪等);連續 N 次重複放棄(收斂保護)

若設計師覺得「啟動 + 每日 + 升階」三軌不夠頻繁,Post-Jam 可擴充為時間流逝連續生成。

---

## Codex 最終建議

> **Codex 第二輪終審結論**:10 個 Critical/Major 全部 ✅;3 個第二輪新發現 Minor(都已修補);**可進入 Codex 工項書建立階段**。

### 已知未處理事項(可選改進,不阻擋實作)

1. **FT-09 §6.4 反向依賴 9 個 GDD 批次處理**:除 #5(FT-02 InjectStaticMission)、#9(systems-index)已落地外,#1~#4/#6/#7/#8 待批次處理,可在 Sprint 1 開工前的 grep audit 一次完成
2. **FT-10 §6.4 反向依賴 15 個 GDD 批次處理**:ISaveable 章節已落地(M13 修補),其餘 §6.x 反向依賴登記可一次掃完
3. **F-02 §3.2 / §5.2 編號間隙與舊 EC 條目**:Sonnet B 修改時保留了 §3.2 rule 編號不重新整理(避免大改動);§5.2 邊緣案例的「同一幀內多個任務同時到期」/「任務派遣後立即關閉遊戲再開啟」嚴格說屬於 FT-02 §5.4 範疇,未來可清理

---

## 處理流程紀錄

### 階段 1:序列審查(Opus + max,主體親自)

10 份 FT GDD 一份一份審完,記錄每份的 Critical/Major/Minor 問題清單。同時讀取 F-01/F-02/F-03/C-01/C-02/C-06 作為跨系統一致性的 ground truth。

審查報告:`design/Reports/GDD-FT01-FT10-design-review-2026-04-26.md`

### 階段 2:Small 修改主體直改

8 個 Small 修改主體完成(VeteranRankWeightTable 建立、game-concept Phase 2 註記、systems-index 命名對齊、各 GDD minor 補完等)。

### 階段 3:Medium/Large 修改派 5 個 Sonnet

| Sonnet | 任務 | 範圍 |
|---|---|---|
| A | C4 破產倒數秒數權威 | F-03 + FT-07 |
| B | C1 雙重計時器機制 | F-02 + FT-02 + FT-04 + FT-10 |
| C | C2 委託板池 + C5 InjectStaticMission | FT-02 + FT-09 |
| D | C3 idleSinceTimestamp + M2 候選池實例化 + M10 SetDispatched + M3 GetMaxDifficulty | C-02 + FT-01 + FT-06 |
| E | M13 ISaveable 介面 | 9 個 owner GDD |

### 階段 4:主體二次驗證

grep 抽樣確認所有 Sonnet 修改正確落地,無 race condition。

### 階段 5:Codex MCP 第一輪終審

以資深 Unity C# 遊戲工程師 + 程式實作角度審查;發現 3 個 Critical(C1/C2/C3:FT-03 殘留 MissionBoard.GetAvailable + FT-02 step 4-5 未走 overload + PostRegularMission 待補)+ 7 個 Major(殘留文字/格式/JsonUtility 限制等)+ 2 個 Minor。

### 階段 6:主體修補 Codex 第一輪回饋

3 Critical + 7 Major + 2 Minor 全部 Small 規模,主體一次直改完成。

### 階段 7:Codex MCP 第二輪確認

10 個原問題全部 ✅;發現 3 個修補時引入的新 Minor(都已修補)。

### 階段 8:最終報告(本檔)

---

## 後續建議

1. **Sprint 1 開工前 grep audit**:批次處理 FT-09/FT-10 反向依賴 + F-02 §5.2 舊 EC 清理
2. **Codex 工項書建立順序**:對齊 systems-index Day 1-3 開發優先序
   - F-01 → F-02 → F-03 → C-01 → C-03 → C-02 → FT-02 → FT-03 → FT-04 → FT-05 → FT-01 → P-02
3. **資料表 CSV 生成**:依各 GDD §7 與對應 data-spec 規格書,於 Sprint 1 一次性生成
4. **二次設計爭議若浮現**:本報告「設計層面決策點」4 項可作為複審入口

---

_整份審查由 Claude Opus 4.7 序列執行,5 個 Sonnet subagent 平行/序列協作,Codex MCP 兩輪終審,符合 CLAUDE.md「審查 Codex 產出一律 Opus + xhigh」規範與「中大型派 Sonnet、小型主體直改」工作流原則。_
