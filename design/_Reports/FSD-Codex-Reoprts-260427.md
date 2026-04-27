# FSD Codex Design-Review Reports — 2026-04-27

_Reviewer：Codex（透過 MCP 觸發，read-only sandbox + 寫入本檔）_
_Reviewee：design/FSD/ 下指定 FSD 文件_
_範圍：C-01, C-02, C-03, C-04, C-05, C-06, FT-01, FT-02 (A+B), FT-03, FT-04, FT-05_
_標準：`.claude/skills/design-review/SKILL.md` 8 章節 + FSD-index 撰寫規範_

每份 review 由 Codex 依序 append；順序固定，跨 review 不互相覆蓋。

---

## C-01 mission-database

_審查時間：2026-04-27 11:05:43 +08:00_
_FSD 檔案：design/FSD/【C-01-FSD】mission-database.md_
_對應 GDD：design/GDD/【C-01】mission-database.md_

### 完整性（章節齊備度）
8/8 章節 + 附錄 A 齊備；§0 文件資訊也存在。§1~§8 均有實質內容，附錄 A 有 checklist 與 review 紀錄。缺漏項目：無。

### GDD 對齊
§3.1 四張資料表欄位在 FSD §5.3 / §6.1 全部覆蓋，對齊。
§3.2 結構性約束在 FSD §5.4 / §7 覆蓋，護送限 D/C/B/A、非 regular 不進常規池、neutral factionID、FK 規則皆有處理；但 FSD 未回註 GDD §3.2 rule 3 例句「護送 F~C」與 rule 1「護送限 D~A」的語義不一致。
§3.3 API 清單內容覆蓋，但 GDD 與 FSD 均稱「13 個 API」，實際列出 14 個（含 `GetAllMissionTypes`）。FSD §1.1 / §1.2 / §2.1 / §4.2 / §8.1 / 附錄 A 延續此計數錯誤。
§4.1 護送時長公式與 FSD §5.4 / §8.2 等價；但 GDD §4.1 範例使用 `D baseDuration = 150` 得出 450~750，與 GDD §3.1 表格及 AC-MD-07 的 D=30、90~150 不一致。FSD 採 D=30 / 90~150，未在 §8.2 標明 GDD 範例矛盾。
§4.2 `GetMissionText` 與 §4.3 `IsValidCombination` 對齊。
§5 邊緣案例在 FSD §7 全部覆蓋，且有對應 Script 與驗證方式。

### 內部一致性
1. `13 個 API` 的敘述與 §5.1 實際 14 個方法不一致。
2. §0 狀態仍為「審查中」，但 §8.3 P-01 寫「FSD 狀態可直接轉已完成」，附錄 A 也記錄主體覆核通過，狀態資訊前後不一致。
3. 事件契約與資料流一致：FSD 明確宣告 C-01 不發布、不訂閱事件，後續流程也未引入事件。

### 可實作性
1. FSD §2.3 / §5.4 使用 `IDataManager.GetTable<T>()`，但現有 F-01 FSD 公開 API 列為 `Get<T>` / `GetAll<T>` / `GetWhere<T>` / `GetInt` / `GetFloat` / `PickRandomWhere<T>`，未列 `GetTable<T>()`。Codex 實作時無法直接判定應呼叫哪個 F-01 API。
2. `FactionRouteTable` 用於 `factionID` FK 驗證，但 FSD §2.2 未列對應 Data-Specs 或 owner API；§7 只寫檢查不存在時 fallback，未定義如何取得合法 factionID 集合。
3. `MissionNamePool.csv` 的 D-02 Data-Specs 標為 `_待建_`，`MissionNameData` 欄位名與 `difficulty` 值域尚未被 D-02 文件確認，`GetMissionText` 實作仍有外部規格缺口。

### 跨系統一致性
1. 與 F-01 FSD 衝突：C-01 依賴 `GetTable<T>`，F-01 FSD 未提供此 API；F-01 對 C-01 的下游描述仍寫 `Get<MissionData>` / `GetAll<MissionData>` / `GetWhere<MissionData>`，與 C-01 的四張表設計不一致。
2. 與 FT-02-A 對齊：FT-02-A 已明確將 C-01 回傳 duration 視為分鐘並乘以 60，與 C-01 §8.3 Tech Debt 說明一致。
3. 與 FT-02-B / FT-04 / FT-05 無直接規則衝突；FT-02-B 另有 categoryID magic number 建議，C-01 可提供常數介面降低後續偏差。
4. §2.4 列出的 FT-06、FT-09、FT-10、P-02 對應 FSD 目前不存在，依任務規則略過；但 `FactionRouteTable` 屬 FT-09 / systems-index 資料，C-01 仍需在依賴章節標明 owner 或暫時無法驗證。

### 資料驅動 / 時間單位 / Script Drift
資料驅動：大多數數值已表格化；護送允許難度集合 `{D,C,B,A}` 為硬規則且 FSD §8.3 P-02 已交代，`MissionNamePool` DS 待建則仍是資料驅動缺口。
時間單位：不合格。FSD 多處使用「分鐘」，雖在 §8.3 登記為跨系統 Tech Debt，但本次審查規則要求只能用「秒」或「小時」。
Script Drift：有交代。FSD §8.3 列出 P-01/P-02/P-03，且 P-01 明確說明既有跨系統時間單位 drift；本審查不另掃 Script。

### 建議（依優先序）
1. 將 C-01 對 F-01 的資料讀取 API 改為 F-01 FSD 已定義的 `GetAll<T>` / `GetWhere<T>` / `PickRandomWhere<T>`，或先在 F-01 FSD 補正式 `GetTable<T>` 契約。
2. 修正「13 個 API」為「14 個 API」，或移除 / 另列 `GetAllMissionTypes`；同步更新 §1、§2、§4、§8、附錄 A。
3. 在 §2.2 / §2.3 補 `FactionRouteTable` 的資料來源與 owner，或明確標記 FT-09 Data-Specs 尚未建立時只能跳過 FK 驗證。
4. 在 §8.2 回註 GDD §4.1 D 難度範例 150 分鐘與 §3.1 / AC-MD-07 的 30 分鐘矛盾，避免 Codex 依範例實作錯誤測試。
5. 依專案時間單位規範，建立 C-01 / FT-02 / F-02 的秒制遷移工項；若暫不遷移，將本項保留為明確 waivered Tech Debt，而非標示為完全合規。
6. 建立 D-02 `MissionNamePool` Data-Specs 後回填 §6.1，鎖定 `MissionNameData` 欄位與 fallback 文字來源。

### 結論：NEEDS REVISION
C-01 FSD 章節完整且主體規則可實作，但需先修正 F-01 API 契約、API 計數、FactionRouteTable 依賴與時間單位合規狀態，才能作為無歧義的 Codex implementation spec。

---

## C-02 adventurer-management

_審查時間：2026-04-27 11:08:45 +08:00_
_FSD 檔案：design/FSD/【C-02-FSD】adventurer-management.md_
_對應 GDD：design/GDD/【C-02】adventurer-management.md_

### 完整性（章節齊備度）
8/8 標準章節齊備，並包含 Appendix A review log；§0 文件資訊、§1 scope/DoD、§2 依賴、§3 fantasy mapping、§4 script plan、§5 API/事件/資料流、§6 CSV/參數化、§7 edge cases、§8 GDD self-check 均存在。主要缺口不是章節缺失，而是 §6 未實際展開 GDD §6.4 `ISaveable` 契約：FSD 在 §5.4 Flow D 與 §8.1 引用「§6.4」，但文件只有 §6.1~§6.3，沒有 `OwnerKey`、`IsCritical`、序列化欄位、restore 驗證策略的完整表格。這會直接影響 FT-10 Save/Load 實作。

### GDD 對齊
核心規則大致對齊 GDD：`AdventurerInstance` 欄位、`AdventurerTemplate` 欄位、`RecruitCostTable`、`Idle/Dispatched/Wounded/Dead` 狀態、`WOUNDED_RECOVERY_HOURS × 3600`、`isUnique` 含 Dead 封鎖重招、`CreateRandomInstance` 不自動加入名冊、`BuildTraitList` 固定加隨機再去重，均有落到 FSD。  
需要修正的對齊點：GDD §3.5 `AddAdventurer(instance)` 沒有 `rosterCap` 參數，但 FSD §5.4 Flow A 要 `AddAdventurer` 內部判斷 `GetRosterCount() >= rosterCap`，同時 §5.1 API 又沒有提供 rosterCap 來源。若容量由 FT-06/FT-01 呼叫 `IsRosterFull(rosterCap)` 先檢查，`AddAdventurer` 不應再宣稱可自行判斷上限；若 C-02 要自行防守，則 API 必須改為 `AddAdventurer(instance, rosterCap)` 或新增對 FT-06/IGuildCore 的依賴。

### 內部一致性
主要不一致集中在所有權與介面邊界：
1. `_nextInstanceID` 在 §1.2 與 §5.3 註記由 `AdventurerRoster` 自增管理，但 §5.4 Flow A 由 `AdventurerFactory.CreateFromTemplate` 呼叫 `NextInstanceID()`；`IAdventurerRoster` 未暴露 `AllocateInstanceID` 類 API，Factory 也未宣告持有 `_nextInstanceID`。必須決定 ID allocator 歸屬。
2. `AddAdventurer` 宣稱執行 isUnique 二次驗證，但 `AdventurerRoster` 只收到 `AdventurerInstance`，沒有 `isUnique` 欄位；§4.4 也未列出 `IAdventurerTemplateLoader` 依賴。Roster 若要驗證，需可查模板；否則只能信任 Factory。
3. §5.2 說 `DismissAdventurer` 後發布 `OnAdventurerStatusChanged`，但除名是 roster removal，不是狀態轉換；payload 的 `current` 無法表示「已移除」。建議新增 `OnAdventurerAdded` / `OnAdventurerDismissed`，或明確定義 removal 事件語意。
4. §8.3 宣稱無衝突，但文件內已有 B-01/B-02 建議事項，且存在上述 API 歸屬不一致；不應標「無需回註 GDD」。

### 可實作性
目前可實作主流程，但會迫使 Codex 在關鍵點猜測：
1. `rosterCap` 無來源，會卡住 `AddAdventurer` 測試 AC-AM-07。
2. `_nextInstanceID` allocator 歸屬不明，會卡住 `CreateFromTemplate`、`CreateRandomInstance`、`RestoreFromSave`。
3. `BuildTraitList` 沒有公開 API，但 FT-01 FSD §2.3 明列依賴 `IAdventurerRoster.BuildTraitList`；C-02 FSD §5.1 只把它寫成 Factory 內部流程。隨機候選者生成時到底由 FT-01 傳入 `traitIDs`，還是由 C-02 依 profession 產生 traitIDs，必須統一。
4. `GetRecruitCost` 放在 `IAdventurerRoster`，但 `RecruitCostTable` 載入與快取在 `AdventurerTemplateLoader`；若保留此 API，Roster 需依賴 Loader 或 DataManager，否則實作邊界不清。
5. `RestoreFromSave` 流程 D 說驗證失敗單筆「跳過」，但 §7 對 `templateID` 缺失要求「保留實例、templateID 標 0」。兩者衝突，需分情境規定：templateID 缺失保留；profession/race/trait invalid 是否跳過或降級保留。

### 跨系統一致性
與已存在 FSD 的主要偏差：
1. F-02 FSD 與現有 script 使用 `OnSecondTickEvent` / `CurrentUTCTimestamp`，C-02 FSD 寫 `OnSecondTick { long nowUTC }`。需統一事件型別名稱與 payload 欄位，避免訂閱不到事件。
2. C-04 FSD `RollRace` 失敗 fallback `raceID=1`；C-02 AC-AM-03 僅要求非 0 合法結果，需補充 fallback 也視為合法或由 C-04 保證。
3. C-05 FSD 將 `GetProfessionGroups(professionID)` 視為 C-02 生成特質的重要 API；C-02 FSD 只使用 `randomTraitGroupIDs` 與 `GetTraitGroup`，未說明隨機冒險者如何由 profession 取得 trait group。
4. FT-01 FSD 依賴 `CreateRandomInstance`、`CreateFromTemplate`、`AddAdventurer`、`BuildTraitList`，但 C-02 FSD 沒有對「候選者預生成但尚未入冊」的 ID 分配與 unique 檢查時點做完整規範；刷新池若預生成 instanceID，放棄候選者會消耗 ID，這可以接受但需明文。
5. P-02、FT-09、FT-10 FSD 目前不存在；C-02 列為下游依賴時應標註「待 FSD 驗證」，避免誤認已完成雙向對齊。

### 資料驅動 / 時間單位 / Script Drift
資料驅動方向正確：`AdventurerTemplate.csv`、`RecruitCostTable.csv`、`SystemConstants.csv` 均列出 Data-Specs 與欄位，沒有把 rank cost、reputationReq、wounded hours 寫死。時間單位也正確使用 UTC Unix 秒，`WOUNDED_RECOVERY_HOURS × 3600` 屬單位換算常數，可接受。  
需補：`SystemConstants.csv` 中 `WOUNDED_RECOVERY_HOURS` 由哪個系統呼叫 `RegisterSystemConstantsTable` 已由 F-01/F-02 規範，但 C-02 若直接讀 `GetInt`，應在 script plan 或資料流中寫出載入時機與 fallback 行為。  
Script Drift：目前 `TheGuild-unity/Assets/Scripts` 未看到 C-02 Adventurer/Roster/Factory 既有 script，無直接 C-02 drift。相關既有 drift 是 `TimeSystem.cs` 仍持有 mission timer 與舊離線摘要 payload，但 C-02 只消費 `OnSecondTickEvent`，不是 C-02 阻塞項；事件命名仍需按 F-02 FSD/現有 `GameEvents.cs` 對齊。

### 建議（依優先序）
1. 補上缺失的 §6.4 `ISaveable` 契約，至少包含 `OwnerKey`、`IsCritical`、完整序列化欄位、restore 驗證失敗分流、`_nextInstanceID` 重建規則。
2. 決定 `rosterCap` 防守位置：若 C-02 不依賴 FT-06，移除 `AddAdventurer` 內部容量判斷，AC-AM-07 改測 `IsRosterFull(rosterCap)`；若要雙重防守，改 API。
3. 決定 `_nextInstanceID` allocator 歸屬，並補 `IAdventurerRoster.AllocateInstanceID()` 或把 Factory 與 Roster 合併 ID 管理責任。
4. 統一 C-02 與 FT-01 的 trait generation API：公開 `BuildTraitList` / `CreateRandomInstanceFromProfession`，或修改 FT-01 只傳入完成的 `traitIDs`。
5. 將事件名稱改成 F-02 現行契約 `OnSecondTickEvent`，並補 `OnAdventurerAdded` / `OnAdventurerDismissed` 或明確 removal 事件語意。
6. 調整 `GetRecruitCost` 歸屬至 `IAdventurerTemplateLoader` 或獨立 `IRecruitCostService`；若維持在 Roster，補明 Roster 的資料依賴。
7. 在 §2.4 對 P-02、FT-09、FT-10 標記「FSD 未存在，待後續雙向驗證」。

### 結論：NEEDS REVISION

---

## C-03 profession-system

_審查時間：2026-04-27 11:12:18 +08:00_
_FSD 檔案：design/FSD/【C-03-FSD】profession-system.md_
_對應 GDD：design/GDD/【C-03】profession-system.md_

### 完整性（章節齊備度）
8/8 標準章節齊備，並包含附錄 A review log。§0 文件資訊、§1 scope/DoD、§2 依賴、§3 fantasy mapping、§4 script plan、§5 API/事件/資料流、§6 CSV/參數化、§7 edge cases、§8 GDD self-check 均存在。

缺漏集中在內容覆蓋，不是章節缺失：GDD AC-PS-12「新增合法 tier=2 職業後 `GetUpgradePaths(baseProfessionID)` 回傳該升級職業」未出現在 §1.3 DoD，也未在 §7 測試項中具體化；GDD P-003 新增的 `raceWeights` 與 `raceIDs` 長度不一致案例未出現在 FSD §7 邊緣案例表。

### GDD 對齊
核心職責與 GDD 對齊：`ProfessionTable.csv` 作為單一職業資料來源、`strongTypeIDs` / `weakTypeIDs` 互斥、`tier` / `baseProfessionID` 升級結構、7 個查詢 API、成功率修正交由 FT-02、C-04/C-05 消費 `raceIDs` / `raceWeights` / `traitGroupIDs`，均有落到 FSD。

未對齊點：
1. GDD §3.1 與 §5.1 已明定 `raceWeights` 長度必須等於 `raceIDs`，且載入時由 C-03 Loader `Debug.LogError` 並跳過該職業。FSD §5.4 loader 流程沒有此驗證步驟，§7 也沒有此 edge case；§8.3 宣稱「本 FSD §4.4 `ProfessionDatabaseLoader.Initialize()` 對齊此驗證」，但 §4.4 只列 Script 職責，沒有可實作規則。
2. GDD §6.1 與 C-01 FSD §5.1 的上游 API 是 `GetAllMissionTypes() : IReadOnlyList<MissionTypeData>`；C-03 FSD §2.3 / §5.4 改寫為 `IMissionDatabaseService.GetAllMissionTypeIds() : IReadOnlyList<int>`。此 API 在 C-01 FSD 中不存在，會造成實作時的跨系統契約斷裂。
3. GDD AC-PS-12 未被 DoD 覆蓋。FSD 只測 Game Jam 全 tier=1 時 `GetUpgradePaths` 回空列表，沒有測合法 tier=2 path，因此升級路線 API 的正向案例未鎖定。

### 內部一致性
1. §1.1 宣稱「所有查詢均回傳不可變資料物件」，但 §5.3 `ProfessionData` 使用 `HashSet<int>` 與 `int[]`。若直接回傳同一物件，下游可修改 `StrongTypeIds`、`RaceIds`、`RaceWeights`、`TraitGroupIds`，不符合不可變契約。需改為唯讀集合、複製回傳，或移除「不可變」敘述。
2. §8.3 寫 B-01 已解決且 C-03 Loader 已對齊 `raceWeights` 驗證；§5.4 / §7 實際未列出該驗證。self-check 與規格正文不一致。
3. `GetProfession(professionID).name`、`GetProfession(professionID).Name`、`ProfessionData.RaceIDs`、下游 FSD 中的 `profession.raceIDs` 混用大小寫。若 DataManager 依反射綁定 CSV 欄位，FSD 需明確規定 CSV 欄位名與 C# 屬性名的 mapping，否則 C-04/C-05 消費端會各自猜測。

### 可實作性
1. `baseProfessionID` 驗證流程有順序依賴。§5.4 寫在逐筆載入時用 `dict.ContainsKey(baseProfessionId)` 檢查來源職業；若 CSV 中 tier=2 行排在 base profession 前面，合法資料會被誤判為錯誤。需改為兩階段：先建立所有 `professionID` 集合，再驗證 `baseProfessionID`。
2. `raceWeights` / `raceIDs` 長度驗證缺失會讓 Codex 無法依 GDD P-003 實作正確 Loader，也會讓 C-04 FSD 的 `profession == null → fallback raceID=1` 路徑無法由 C-03 保證觸發。
3. `GetLoadedDictionary() : IReadOnlyDictionary<int, ProfessionData>` 只保護 dictionary 介面，不保護 `ProfessionData` 內部集合。若要維持查詢熱路徑 O(1)，可在 Loader 完成後封存為 `IReadOnlySet<int>` / `IReadOnlyList<int>` 或自訂 immutable DTO。
4. `STRONG_TYPE_BONUS` / `WEAK_TYPE_PENALTY` 屬 FT-02 實際讀取與套用；C-03 DoD-10 要求「C-03 code review 無魔術數字 0.20 / 0.15」可以保留，但不能替代 FT-02 對 SystemConstants 的驗證。

### 跨系統一致性
1. 與 C-01 FSD 不一致：C-03 依賴的 `GetAllMissionTypeIds()` 未在 C-01 公開 API 列表中定義。可改為呼叫 `GetAllMissionTypes()` 後投影 `typeID`，或在 C-01 補正式 API。
2. 與 C-04 FSD P-003 後狀態部分對齊：C-04 已承認 `raceWeights` 驗證責任反轉為 C-03 Loader，並保留 runtime defensive check。C-03 FSD 目前只在 §8.3 宣稱對齊，正文未落地，導致雙向對齊未完成。
3. 與 C-05 FSD 的 `traitGroupIDs` 消費方向一致；C-03 不驗證 `traitGroupIDs` 是否存在於 TraitGroupTable，C-05 在 `GetProfessionGroups` 端過濾並 warning，責任切分清楚。
4. 與 F-01 DataManager 的主鍵重複處理一致：FSD 沿用「重複主鍵後者覆蓋 + warning」標準行為，未新增 C-03 自訂處理。

### 資料驅動 / 時間單位 / Script Drift
資料驅動：職業資料、種族池、特質群組、成功率常數來源皆指向 CSV，方向正確；但 `raceWeights` 長度驗證缺少 FSD 正文規格，資料品質防線不完整。`GetAllProfessions()` 固定 7 筆屬 Game Jam 初始資料驗收，可接受，但需避免實作把 7 寫死。

時間單位：C-03 不持有時間欄位，無時間單位問題。

Script Drift：掃描 `TheGuild-unity/Assets/Scripts` 未發現 `ProfessionData`、`IProfessionService`、`ProfessionDatabaseLoader`、`ProfessionService` 或 `Profession` 目錄；目前無既有 C-03 script drift。唯一命中的相關檔案是 `Core/Time/MissionTimer.cs`，屬既有 FT/F-02 類 drift，與 C-03 無直接關係。

### 建議（依優先序）
1. 在 §5.4 loader 流程與 §7 edge cases 補上 `raceWeights.Length != raceIDs.Length`：`Debug.LogError(professionID, raceIDs.Length, raceWeights.Length)`，跳過該職業，不加入字典；§1.3 增加對應 DoD。
2. 將 C-01 依賴改為現有契約 `GetAllMissionTypes() : IReadOnlyList<MissionTypeData>`，並在 C-03 Loader 內建立 `HashSet<int> validTypeIds`；或先更新 C-01 FSD 正式提供 `GetAllMissionTypeIds()`。
3. 補 AC-PS-12 的正向升級路線測試：注入合法 tier=2 職業，驗證 `GetUpgradePaths(baseProfessionID)` 與 `GetBaseProfession(tier2ID)`。
4. 將 `baseProfessionID` 驗證改為兩階段，避免 CSV 行順序影響合法性。
5. 明確定義 `ProfessionData` 不可變策略：唯讀集合、建構後封存、或服務回傳 defensive copy；同步修正大小寫命名與 CSV mapping。
6. 更新 §8.3 / 附錄 A，避免在正文尚未補齊前宣稱 B-01 已完全解決。

### 結論：NEEDS REVISION

---

## C-04 race-system

_審查時間：2026-04-27 11:15:32 +08:00_
_FSD 檔案：design/FSD/【C-04-FSD】race-system.md_
_對應 GDD：design/GDD/【C-04】race-system.md_

### 完整性（章節齊備度）
8/8 標準章節齊備，並包含附錄 A review log。§0 文件資訊、§1 scope/DoD、§2 依賴、§3 fantasy mapping、§4 script plan、§5 API/事件/資料流、§6 CSV/參數化、§7 edge cases、§8 GDD self-check 均存在。

主要缺口不是章節缺失，而是 §5.3 `RaceData` 資料結構沒有列出 CSV 欄位 `modifiers` 的 raw string 欄位。`RaceDatabaseLoader` 需要讀取該欄位才能解析 JSON；若依 §5.3 直接實作 `RaceData`，DataManager 無法把 `RaceTable.csv.modifiers` 綁定到 C# 物件。

### GDD 對齊
C-04 FSD 已覆蓋 GDD §3.1 `RaceTable`、§3.2 消費 C-03 `ProfessionTable.raceIDs/raceWeights`、§3.3 五支 API、§4.1 `RollRace` 加權抽取、§4.2 modifiers JSON 解析、§4.3 由 FT-02 套用修正值、§5 載入/查詢邊緣案例、§7 可調參數與 §8 驗收標準。GDD P-004 的 `raceID=1` fallback 保留 ID 也已在 FSD §8.3 登記並反映於 fallback 行為。

未對齊點：
1. GDD §5.1 已將 `raceIDs` / `raceWeights` 長度不一致的驗證責任歸 C-03 Loader；FSD §8.3 也宣稱 C-04 不再重複驗證。但 FSD §5.4 偽碼仍把 `raceIDs.Length != raceWeights.Length` 寫成 `RollRace` 的 `LogError + return 1` 分支，§1.3 AC-RS-12 也仍測 C-04 的長度不一致路徑。這可保留為 defensive check，但測試所有權與敘述必須改成「C-03 必測、C-04 防守性覆蓋」。
2. GDD / Data-Specs 要求 `modifiers` 為 `RaceTable` 必填欄位；FSD §5.3 `RaceData` 缺該欄位，與 GDD §3.1 欄位定義不一致。
3. GDD §4.2 指出 `JsonUtility` 不能直接解析 top-level array；FSD 有 wrapper DTO，但沒有明確規定把原始 `[...]` 字串包成 `{"modifiers":[...]}` 後再 `FromJson<RaceModifierListWrapper>()`。Codex 仍會在 `JsonUtility.FromJson<RaceModifierListWrapper>(rawJson)` 與手動包裝之間猜測。

### 內部一致性
1. §8.3 說 `raceWeights` 長度驗證已反轉到 C-03 Loader，§5.4 / AC-RS-12 又保留 C-04 主要驗證語意；這是同一責任的雙重歸屬。
2. §6.3 將 fallback raceID 寫成來源為 `RaceTable.csv` 第一筆合法 `raceID`，但 §5.1、§5.4、§7、GDD P-004 均固定為 `1`。若 Game Jam 固定 `1`，§6.3 應改成「常數 `FALLBACK_RACE_ID = 1`，由 GDD 保留 ID 約束支撐」，避免實作者改成動態第一筆。
3. §5.4 在過濾不存在於 RaceTable 的 `raceID` 後只檢查 raceIDs 是否為空，未檢查剩餘 `raceWeights` 是否皆為正值。C-03 Data-Specs 要求 `raceWeights > 0 per element`，但 C-03 FSD 正文尚未落地該驗證；C-04 若保留 defensive validation，需明列 `weight <= 0` 的處理。

### 可實作性
1. `RaceData` 必須包含 `public string modifiers` 或等價 raw 欄位，並定義載入後的 cache 欄位/方法，例如 `IReadOnlyDictionary<int, RaceModifierEntry>` 或 `TryGetModifier(typeID, out entry)`。目前 §5.3 只用註解描述 `_modifierCache`，不足以直接產生可編譯 class。
2. `RollRace` 使用 `Random.Range(0, totalWeight)`，但沒有定義 RNG 注入或測試 seed。AC-RS-10 用 1000 次統計 ±5% 會受全域亂數狀態影響；測試規格需明確 `Random.InitState(seed)`，或把 RNG 抽成可注入介面。
3. `GetAllRaces()` 回傳 `IReadOnlyList<RaceData>`，但 `RaceData` 是 mutable public fields。若 C-04 要保證靜態資料不被下游修改，需回傳 defensive copy、唯讀 DTO，或明確接受下游不得修改的約束。
4. `RaceDatabaseLoader` 依賴 `IMissionDatabaseService.GetAllMissionTypes()`，與 C-01 FSD §5.1 一致；但 FSD 未給 `Initialize(IDataManager, IMissionDatabaseService)` 之類入口簽名，Bootstrap 實作需自行決定建構順序與注入方式。

### 跨系統一致性
1. F-01 FSD §2.4 仍把 C-04 消費寫成 `Get<RaceData>`、`GetAll<ProfessionRacePoolData>`，但 C-04 FSD 與 GDD 已廢棄 `ProfessionRacePool.csv`，且 C-04 實際需要 `GetAll<RaceData>()`。F-01 下游索引需要更新，否則實作人員會誤建廢棄資料類。
2. C-03 FSD 已在 §8.3 宣稱 `raceWeights` 長度驗證由 `ProfessionDatabaseLoader.Initialize()` 完成，但 §5.4 loader 流程與 §7 edge cases 未列該驗證。C-04 依賴 C-03 的錯誤資料防線；在 C-03 修正前，C-04 的「C-03 會跳過違規職業」假設未被 FSD 正文支撐。
3. FT-02-A FSD 使用 `IC04RaceService.GetSuccessDelta()` / `GetDeathDelta()`，C-04 FSD 使用 `IRaceService`。方法語意一致，但介面命名不一致；實作前需統一命名或建立 adapter。
4. FT-01 FSD 已明確消費 `IRaceService.RollRace(professionID)`，與 C-04 §2.4 額外列 FT-01 下游一致；GDD §6.2 未列 FT-01，屬後續 FSD 的合理擴充，不構成衝突。
5. P-02 FSD 目前不存在；C-04 對 P-02 的下游契約只能保留為待驗證項。

### 資料驅動 / 時間單位 / Script Drift
資料驅動：`RaceTable.csv.modifiers`、`ProfessionTable.csv.raceIDs/raceWeights` 均表格化，未把種族修正值寫死；`fallback raceID=1` 是 GDD P-004 保留 ID，不是平衡參數。需補 `weight <= 0` 防線與 `RaceData.modifiers` raw 欄位，資料驅動鏈才完整。

時間單位：C-04 不持有時間欄位，無時間單位違規。

Script Drift：掃描 `TheGuild-unity/Assets/Scripts` 未發現 `RaceData`、`IRaceService`、`RaceDatabaseLoader`、`RaceService` 或 Race 目錄；目前無既有 C-04 script drift。掃描 `TheGuild-unity/Assets/Resources/Data` 未發現 RaceTable 相關資料檔，實作時需建立 `RaceTable.csv` 或確認資料生成工項。跨文件 drift 為 F-01 FSD 仍引用 `ProfessionRacePoolData`，需修正。

### 建議（依優先序）
1. 在 §5.3 補 `RaceData.modifiers : string`，並明確定義 `_modifierCache` 的型別、存取方法與初始化責任。
2. 將 `raceIDs.Length != raceWeights.Length` 的主要驗證測試移到 C-03；C-04 只保留 defensive branch，並把 AC-RS-12 改成「mock 違規 profession data 時防守性 fallback」。
3. 在 §5.4 / §7 補 `raceWeights` 非正值與 `totalWeight <= 0` 的處理：`Debug.LogError(professionID)` 並 fallback `raceID=1`，或明確宣告由 C-03 Loader 保證且 C-04 不防守。
4. 補 JsonUtility wrapper 的精確解析步驟：原始 `[...]` → 字串包裝為 `{"modifiers": ...}` → `FromJson<RaceModifierListWrapper>()`；若選 Newtonsoft.Json，刪除 wrapper DTO 路徑並登記 §8.2。
5. 統一跨系統介面命名：C-04、FT-02-A、DI/Bootstrap 使用同一個 `IRaceService` 或 `IC04RaceService`。
6. 更新 F-01 FSD §2.4 C-04 row，移除 `ProfessionRacePoolData`，改為 `GetAll<RaceData>()`。
7. 讓 AC-RS-10 使用固定 seed 或可注入 RNG，避免統計測試受全域亂數狀態影響。

### 結論：NEEDS REVISION
C-04 FSD 的設計方向與 GDD 主體一致，章節完整，可作為實作基礎；但 `RaceData.modifiers` 欄位缺失、C-03/C-04 長度驗證責任矛盾、F-01 `ProfessionRacePoolData` drift、JsonUtility 解析步驟未明確，會讓 Codex 實作產生分歧。修正上述契約後可進入 implementation spec。

---

## C-05 trait-system

_審查時間：2026-04-27 11:20:51 +08:00_
_FSD 檔案：design/FSD/【C-05-FSD】trait-system.md_
_對應 GDD：design/GDD/【C-05】trait-system.md_

### 完整性（章節齊備度）
C-05 FSD 具備 FSD-index §1-§7 要求的標準章節：文件資訊、概要、設計來源與依賴、幻想到實作映射、Script 規劃、公開 API/事件/資料流、資料表使用、邊緣案例、GDD 對齊自檢與附錄 A。章節形式完整，且 DoD、資料流、CSV 欄位、邊緣案例均有實質內容。

主要缺口不在章節缺失，而在跨文件契約未收斂：C-02 的特質群組來源、FT-04 的 condition 套用責任、F-01 的 group pool 抽樣責任，三處與 C-05 FSD 本文存在明確衝突。

### GDD 對齊
1. C-05 GDD §3.5 的公開 API 清單未包含 `ApplyConditionTraits`，且 §4.4 標明 condition 類套用「由 FT-04 執行」。C-05 FSD 延續此邊界，將 condition 套用列為 Out-of-Scope，這點與 C-05 GDD 主體一致。
2. 但 C-05 GDD §8 AC-TS-06 寫 `pickMode = "unique"`，GDD §3.3 與 FSD 全文使用 `uniform`。FSD §1.3 將 AC-TS-06 改為 `uniform`，但 §8.4/§8.5 未登記「GDD 驗收條件用詞錯誤」或回註，屬靜默修正。
3. GDD/FSD 公式都規定 `pool.Count < pickCount` 才 `LogWarning` 並回傳所有有效特質；但 AC-TS-07 寫 `pickCount=1, pool=1` 時仍需 `LogWarning`。等量池不是邊緣錯誤，驗收條件與公式互斥。
4. FSD §8.1 說 effectTarget 合法清單「不寫死於程式碼，以 CSV effectTarget 為準」，但 GDD §3.2 是固定合法變數空間；FSD §8.3 又承認 `TraitDatabaseLoader` 以 `HashSet<string>` 維護 23 項。此處應改為「合法清單是業務規則常數，由 GDD 維護」，不可宣稱以 CSV 為準。

### 內部一致性
1. §2.4、§5.4 流程 B 將 C-02 流程寫成 `BuildTraitList(professionID)` → `GetProfessionGroups(professionID)`；但 C-02 FSD §2.3、§5.4、§7 明確寫 `BuildTraitList(template.fixedTraitIDs, template.randomTraitGroupIDs)`，並以 `GetTraitGroup(groupID)` + `RollTraits(group)` 消費 C-05。兩份 FSD 對 trait group 來源不一致。
2. §5.1 `ITraitService` 只列 `GetTrait`、`GetAllTraits`、`GetTraitsByType`、`GetTraitGroup`、`RollTraits`、`GetProfessionGroups`。FT-04 FSD §2.3、§5.4、§7、§8.1 卻依賴 `ITraitService.ApplyConditionTraits(Outcome, int[] traitIDs)`。C-05 FSD 沒有此 API，實作會直接卡在介面不匹配。
3. §1.2 Out-of-Scope 說不實作 `stat` / `behavior` / `condition` 套用；§7 又把 `ApplyConditionTraits` runtime 未知 effectTarget 行為列入 C-05 邊界說明。若 C-05 不持有 `ApplyConditionTraits`，該列應移交 FT-04；若 C-05 要持有，§1.2、§5.1、§4.4 需同步改寫。
4. `RollTraits` 的 `weighted` 行為在 §7 對非法 `pickMode` fallback `uniform`，§8.2 對合法但未實作的 `weighted` 也 fallback `uniform`。這兩種情況應有不同 log level 與測試：非法值是 `LogError`，合法未支援是 `LogWarning`。

### 可實作性
1. `TraitData` / `TraitGroupData` 欄位型別可實作，但未明確是否繼承 F-01 的 `GroupPoolData`。F-01 FSD 要求群組表以 `RegisterGroupPoolTable<T>` 註冊，T 為 `GroupPoolData` 或子型別；C-05 FSD 則規劃 `TraitDatabaseLoader` 直接 `GetAll<TraitGroupData>()` 並自建抽樣。需選定一條路徑。
2. `TraitService` 使用 `System.Random` 執行 Fisher-Yates，但未定義 RNG 注入、seed 或測試控制點。AC-TS-06 用 100 次覆蓋 5 個 ID 作為統計驗證，若每次呼叫新建 `System.Random`，快速測試可能因 seed 重複產生偏差。需規定單例 RNG、可注入 RNG，或測試 seed。
3. `GetAllTraits()` / `GetTraitsByType()` 回傳 `IReadOnlyList<TraitData>`，但 `TraitData` 是 mutable DTO。若下游不可修改靜態資料，需定義 defensive copy、唯讀 DTO，或明文宣告資料物件不得被下游改寫。
4. 初始化順序只寫「F-01 DataManager 初始化後」，但 C-05 還依賴 C-03 `IProfessionService`。需要明確 Bootstrap/DI 順序：`TraitDatabaseLoader.Load()` 是否需要 C-03 已載入、`GetProfessionGroups` 是否延遲查 C-03、C-03 未 ready 時如何回應。

### 跨系統一致性
1. C-02 與 C-05 的 `BuildTraitList` 契約衝突是阻礙項。C-05/C-03/Data-Specs 支持 `ProfessionTable.traitGroupIDs`；C-02 FSD 支持 `AdventurerTemplate.randomTraitGroupIDs`。需決定隨機冒險者與具名模板各自用哪個來源，並讓 C-02 與 C-05 的 API/資料流一致。
2. FT-04 與 C-05 的 `ApplyConditionTraits` 歸屬衝突是阻礙項。若由 FT-04 實作，FT-04 不應依賴 C-05 API；應只呼叫 `GetTrait` / `GetTraitsByType("condition")`。若由 C-05 實作，C-05 FSD 需新增 API、Outcome DTO 依賴與 Script 職責，並撤銷 Out-of-Scope。
3. F-01 FSD 把 C-05 消費寫成 `PickRandom<TraitData>` / `RegisterGroupPoolTable<TraitGroupData>`；C-05 FSD 自行載入 `TraitGroupTable` 並自行抽樣。這會讓實作者不知道該使用 F-01 隨機池還是 C-05 專屬抽樣器。
4. FT-02-A 與 FT-03 實際只需要 `GetTrait(traitID)` 逐筆處理，C-05 FSD §2.4 另外列 `GetTraitsByType(...)` 作為下游介面。若保留 `GetTraitsByType`，需說明是 debug/查詢用，還是下游公式必須先取得 type cache；目前消費方式與下游 FSD 不完全一致。
5. P-02 FSD 目前不存在；C-05 對 P-02 的契約只能視為待驗證下游，不能標為已完成對齊。

### 資料驅動 / 時間單位 / Script Drift
資料驅動：`TraitTable.csv`、`TraitGroupTable.csv`、`ProfessionTable.csv.traitGroupIDs` 均已表格化；`effectValue`、`pickCount`、`traitIDs` 未寫死。問題在 `effectTarget` 合法清單的定位：它是 GDD 固定 enum-like 規則，不是 CSV 可自由擴充值。FSD §8.1 的「以 CSV effectTarget 為準」需移除。

時間單位：C-05 不持有時間欄位，無「秒 / 小時」違規。

Script Drift：掃描 `TheGuild-unity/Assets/Scripts` 未發現 `TraitData`、`TraitGroupData`、`ITraitService`、`TraitDatabaseLoader`、`TraitService` 或 Trait 目錄，無既有 C-05 script drift。跨文件 drift 明確存在：F-01 的 group pool 抽樣路徑、C-02 的 `randomTraitGroupIDs` 路徑、FT-04 的 `ApplyConditionTraits` 路徑與 C-05 FSD 不一致。

### 建議（依優先序）
1. 先裁決 `ApplyConditionTraits` 歸屬：建議 Game Jam 版本由 FT-04 實作 condition 套用，C-05 只提供 `GetTrait` / `GetTraitsByType("condition")`，並修正 FT-04 FSD 對 `ITraitService.ApplyConditionTraits` 的依賴。
2. 裁決 C-02 trait group 來源：若職業決定預設特質群組，C-02 FSD 的 `CreateRandomInstance` 應呼叫 `GetProfessionGroups(professionID)`；若模板可覆蓋，需明確規則：`randomTraitGroupIDs` 非空優先，否則 fallback profession traitGroupIDs。
3. 統一 F-01 與 C-05 抽樣責任：選擇 `RegisterGroupPoolTable` + `PickRandom`，或選擇 C-05 專屬 `RollTraits`。若選 C-05 專屬抽樣，F-01 FSD C-05 row 需移除 `PickRandom<TraitData>`。
4. 修正 AC-TS-06 `unique`/`uniform` 命名差異，並在 C-05 GDD 或 FSD §8.4 登記回註。
5. 修正 AC-TS-07：`pool.Count == pickCount` 應無 warning；只有 `pool.Count < pickCount` 才 warning。
6. 改寫 §8.1 / §8.3 對 effectTarget 的說法：合法清單是 GDD 規則常數，可用 `HashSet<string>` 實作；不得宣稱以 CSV 為準。
7. 補 RNG 策略：`TraitService` 接受可注入 RNG 或持有單例 RNG，測試以固定 seed 驗證 Fisher-Yates 不重複與覆蓋。

### 結論：MAJOR REVISION NEEDED
C-05 FSD 章節完整，資料表與查詢層設計可作為基礎；但 C-02、FT-04、F-01 三個跨系統契約與 C-05 本文互相衝突，且 GDD 驗收條件中的 `unique`/`uniform` 與 `pool=1` warning 問題未登記。這些不是文字潤飾，會直接導致介面與實作分歧；需先修正契約再進入 implementation。

---

## C-06 world-danger-system

_審查時間：2026-04-27 11:24:22 +08:00_
_FSD 檔案：design/FSD/【C-06-FSD】world-danger-system.md_
_對應 GDD：design/GDD/【C-06】world-danger-system.md_

### 完整性（章節齊備度）
8/8 標準章節齊備，並包含 Appendix A review log。§0 文件資訊、§1 scope/DoD、§2 依賴、§3 fantasy mapping、§4 script plan、§5 API/事件/資料流、§6 CSV/參數化、§7 edge cases、§8 GDD self-check 均存在。章節形式完整，且 DoD 覆蓋 AC-WD-01~16。

主要缺口不是章節缺失，而是實作契約未收斂：F-01 DataManager API、FT-02-B `OnDangerLevelChanged` payload、現有 TimeSystem 每日重置事件型別，三處會直接影響 Codex 產出的可編譯結果。

### GDD 對齊
C-06 FSD 已對齊 GDD P-005 後的主體規則：5 階 `E→D→C→B→A`、三閘 AND 條件、`elapsedDays = floor((NowUTC - gameStartTimestamp) / 86400)`、任務難度索引、`acceptedMissionCount` 升階後歸零、升階時推送 `F03.SetBankruptcyThreshold(GetMaxDebt())`、發布 `OnDangerLevelChanged`、A 階 no-op、`gameStartTimestamp == 0` 回 0 並 LogError，均有落到 FSD。

資料表也對齊 GDD：`WorldDangerTable.csv` 13 欄位與 5 行 Game Jam 初始值完整列出，`maxDebt`、任務池權重、升階閘集中於同一表。邊緣案例對齊 GDD §5，包含缺行、全 0 weight、`maxDebt=0`、非法 `minDifficulty`、大小寫正規化。

需修正的對齊點：FSD 對 F-01 的讀取 API 寫成 `GetTable<WorldDangerData>()`，但 F-01 FSD 與現有 `DataManager.cs` 沒有此 API；GDD 只要求載入表格，不要求 `GetTable`。這是 FSD 層引入的非 GDD 偏差。

### 內部一致性
1. §5.2 寫 `OnDailyReset` 應在 `OnEnable` 訂閱、`OnDisable` 退訂；§5.4 流程 2 又把 `IEventBus.Subscribe<OnDailyResetEvent>(HandleDailyReset)` 放在 `Start()`。事件生命週期不一致，會造成重複訂閱或漏退訂的實作分歧。
2. §5.1 宣稱 `OnMissionAccepted(difficulty)` 對非有效 difficulty 不計數；§5.4 偽碼直接使用 `DifficultyIndex[difficulty]`，未 `TryGetValue`，非法輸入會變成 `KeyNotFoundException`。
3. §1.1 稱 C-06 屬 Core 層，但 §4.4 script path 全部放在 `Assets/Scripts/Gameplay/WorldDanger/`。若專案層級要維持 Core / Gameplay 邊界，需明確說明 C-06 的實際 assembly 與 namespace 歸屬。
4. `OnDangerLevelChanged` 在 C-06 內部一致地使用 `string newDangerLevel`，但未明列事件 payload struct 欄位名稱；§5.4 只寫 `new OnDangerLevelChangedEvent(nextLevel)`，不足以直接產生 `GameEvents.cs` 契約。

### 可實作性
1. FSD 缺少 DataManager 註冊步驟。現有 F-01 模式要求在 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 呼叫 `DataManager.RegisterTable<WorldDangerData>("WorldDangerTable")`，再用 `GetAll<WorldDangerData>()` 讀取；C-06 FSD 只寫 `WorldDangerLoader.LoadAll()` 與 `GetTable<WorldDangerData>()`。
2. `IResourceService` / `IEventBus` 介面未在已存在 FSD 或 script 中定義。現有 F-03 script 是 `ResourceManagement.Instance.SetBankruptcyThreshold(int)`，現有 EventBus 是 static `EventBus`。C-06 需指定要新增 adapter/interface，或直接使用既有 concrete/static 契約。
3. `gameStartTimestamp` 初始化完全委派 FT-10 `InitializeAsNewGame()`，但 FT-10 FSD 目前不存在。若 C-06 先實作，必須補 bootstrap fallback 或把該依賴列為 implementation blocker。
4. 缺行跳階策略可實作，但測試不足。§7 只測「缺 B 行時 D 仍可升 C」，未測「下一階正好缺行時是否發布事件、是否推送 F-03、是否允許 `currentDangerLevel` 暫時成為無資料的 level」。

### 跨系統一致性
1. 與 F-01 FSD 不一致：F-01 §2.4 C-06 row 仍列 `Get<WorldDangerData>`、`GetAll<MissionPoolWeightData>`、`GetAll<DebtLimitData>`，是舊拆表契約；C-06 FSD 已改為單表 `WorldDangerTable.csv`。兩份 FSD 必須同步。
2. 與 FT-02-A 對齊：FT-02-A Dispatch step 9 呼叫 `IC06WorldDanger.OnMissionAccepted(difficulty)`，與 C-06 API 契約一致。
3. 與 FT-02-B 不一致：FT-02-B §2.5 / §5.2 訂閱 `OnDangerLevelChanged` payload 為 `int newDangerLevel`；C-06 發布 `string newDangerLevel`，且危險度值域為 `E/D/C/B/A`。FT-02-B 應改為 string 或明確使用 ordinal enum，但目前沒有轉換規格。
4. 與 F-02 FSD 對齊，但與現有 script 不對齊：F-02 FSD 定義 typed `OnDailyResetEvent`；`TimeSystem.cs` 目前發布 `EventBus.Publish(EventNames.OnDailyReset)` 的 named event。C-06 若訂閱 typed event，現有 script 不會觸發升階檢查。
5. FT-09、P-02、FT-10 FSD 目前不存在。C-06 對 `OnFactionScoreUpdated`、UI 更新、Save/Load restore order 的跨系統契約只能標記為待驗證，不能視為已完成雙向對齊。

### 資料驅動 / 時間單位 / Script Drift
資料驅動：`WorldDangerTable.csv` 已把升階閘、池權重、`maxDebt` 全部表格化，方向正確；`DangerLevelOrder`、`DifficultyIndex`、`86400` 是規則常數，可留在 code。`design/Data-Specs/【C-06-DS】world-danger-table.md` 存在，但 `TheGuild-unity/Assets/Resources/Data/Tables/WorldDangerTable.csv` 目前未存在，實作時需建立資料檔。

時間單位：合格。C-06 使用 UTC Unix 秒作為來源，`timeThreshold` 單位明確為遊戲天數，透過 `86400` 秒換算；未引入分鐘制 duration。需維持所有 API payload 以 `long NowUTC` / 秒為基準。

Script Drift：目前沒有 C-06 專屬 script，無直接 WorldDanger script drift。既有支撐系統 drift 明確存在：`GameEvents.cs` 沒有 `OnDailyResetEvent` 與 `OnDangerLevelChangedEvent`；`TimeSystem.cs` 發布 named `EventNames.OnDailyReset`；F-01 DataManager 無 `GetTable<T>()`；F-03 `SetBankruptcyThreshold(int)` 已存在，可被 C-06 消費。

### 建議（依優先序）
1. 修正 C-06 對 F-01 的契約：新增 `RegisterTable<WorldDangerData>("WorldDangerTable")` 註冊流程，將讀取 API 改為 `GetAll<WorldDangerData>()`；同步更新 F-01 FSD C-06 row，移除 `MissionPoolWeightData` / `DebtLimitData` 舊拆表。
2. 統一事件契約：新增 `OnDangerLevelChangedEvent { string NewDangerLevel; }`，並把 FT-02-B payload 從 `int` 改成 `string`；同時裁決 `OnDailyResetEvent` 要採 typed event 還是 named event，並同步 F-02 FSD、C-06 FSD 與現有 `TimeSystem.cs`。
3. 在 `OnMissionAccepted` 補 `DifficultyIndex.TryGetValue(difficulty, out acceptedIndex)` 與 `TryGetValue(nextData.minDifficulty, out requiredIndex)`，非法值 LogWarning/LogError 後不計數。
4. 將 `OnDailyReset` 訂閱流程固定為 `OnEnable` / `OnDisable`，`Start()` 只做初始 `SetBankruptcyThreshold(GetMaxDebt())`。
5. 明確 `IResourceService` / `IEventBus` 的來源。若要新增介面，列出 adapter script 與依賴；若直接使用 `ResourceManagement.Instance` / static `EventBus`，修改 §4.4、§5.4 的依賴描述。
6. 在 C-06 §2.4 / §8.3 標記 FT-09、P-02、FT-10 FSD 未存在，並把 `gameStartTimestamp` 初始化與 restore order 列為待 FT-10 裁決項。
7. 補 `WorldDangerTable.csv` 初始資料與缺「下一階」的 edge test，避免 loader fallback 與升階事件行為被實作者自行解讀。

### 結論：NEEDS REVISION
C-06 FSD 章節完整，GDD 主規則已對齊，資料驅動方向正確；但 F-01 讀表 API、FT-02-B 事件 payload、F-02 現有事件 script drift、未定義的 `IResourceService/IEventBus` 會直接造成實作分歧。修正這些跨系統契約後，可進入 implementation spec。

---

## FT-01 adventurer-recruitment

_審查時間：2026-04-27 11:27:45 +08:00_
_FSD 檔案：design/FSD/【FT-01-FSD】adventurer-recruitment.md_
_對應 GDD：design/GDD/【FT-01】adventurer-recruitment.md_

### 完整性（章節齊備度）
FSD 具備 §0~§8 與附錄 A，涵蓋 Overview、In/Out Scope、DoD、設計來源、上下游依賴、Script plan、API/events、資料流、CSV 參數化、Edge Case、GDD alignment 與 Review log。AC-AR-01~AC-AR-19 可直接轉為測試，章節齊備度合格。

缺口集中在實作契約，不是章節缺漏：§5.2 宣告 `OnPoolRefreshedEvent` / `OnRecruitSuccessEvent`，但 §4.4 Script plan 未指定事件 struct 放在 `RecruitmentTypes.cs`、`GameEvents.cs` 或獨立 `RecruitmentEvents.cs`；§6.1 也未指定 `VeteranRankWeightData` / `RecruitCostEntry` 對 F-01 `DataManager.RegisterTable<T>()` 的註冊位置。

### GDD 對齊
主要玩法規則與 GDD 對齊：新手池 F/E、老手池 D~S、老手權重 D40/C30/B18/A9/S3、`GetMaxRecruitableRank()` 過濾、每日免費刷新、付費刷新、`RecruitCostTable.csv` 的 `cost` / `reputationReq`、離線只補一次刷新、`ISaveable` 欄位皆有覆蓋。

`OnPoolRefreshedEvent` 的發布點偏差已在 §8.4 回註為語義等價：由 `ExecuteRefresh()` 統一發布可避免手動刷新重複發布。但 §5.4 觸發點 B 仍保留 `EventBus.Publish(new OnPoolRefreshedEvent())`，與 §5.4 注意事項、§8.2 的「ManualRefresh 不另行發布」互相矛盾。這會造成實作時手動刷新路徑發布 1 次或 2 次的分歧。

### 內部一致性
1. §5.4 觸發點 B 的 `ManualRefresh()` 在呼叫 `ExecuteRefresh()` 後仍寫 `EventBus.Publish(new OnPoolRefreshedEvent())`；同節 `ExecuteRefresh()` 也發布事件，且注意事項明確說 ManualRefresh 不重複發布。必須刪除觸發點 B 的發布行，保留 `ExecuteRefresh()` 單一發布點。
2. §7 edge case 寫「`CanAfford` 通過但 `AddGold` 失敗時回傳 false，不加入名冊」；§5.4 觸發點 B/D 的偽碼卻直接呼叫 `IResourceService.AddGold(...)`，未檢查 `bool` 回傳值。實作者若照偽碼會在扣款失敗後繼續刷新或加入名冊。
3. C-02 API 歸屬不一致。FT-01 §2.3 / §5.4 把 `CreateFromTemplate`、`CreateRandomInstance`、`BuildTraitList` 掛在 `IAdventurerRoster` 或 `C02` 上；C-02 FSD §5.1 則定義 `CreateFromTemplate` / `CreateRandomInstance` 屬於 `IAdventurerFactory`，`IAdventurerRoster` 只負責 roster CRUD。FT-01 應改依賴 `IAdventurerFactory`，或明確說明要由 C-02 暴露 facade。
4. §2.3 列 `IGuildBuildingService.IsStaffSystemUnlocked()`，AC-AR-18 也測未解鎖狀態；§5.4 自動刷新公式卻只呼叫 `IStaffService.GetRecruitRefreshReductionSec()`。若責任是「FT-12 未解鎖時固定回 0」，需把 `IsStaffSystemUnlocked()` 從 FT-01 公式責任移除，避免雙重判斷。

### 可實作性
1. 事件型別落點未定義。新增 `OnPoolRefreshedEvent` / `OnRecruitSuccessEvent` 時，應在 Script plan 指定檔案與 namespace；否則會與現有 `TheGuild.Core.Events.GameEvents.cs` 的 typed event 集合分裂。
2. `OnDailyResetEvent` 與現有 script 不對齊。F-02 FSD 定義 typed `OnDailyResetEvent`，但現有 `TimeSystem.cs` 發布 `EventBus.Publish(EventNames.OnDailyReset)`，`GameEvents.cs` 目前也沒有 `OnDailyResetEvent` struct。FT-01 若訂閱 typed event，現有 F-02 script 不會觸發免費刷新重置。
3. F-01 DataManager 要求下游於 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 註冊表格。FT-01 FSD 未指定 `DataManager.RegisterTable<VeteranRankWeightData>("VeteranRankWeightTable")`；`RecruitCostTable` 若由 C-02 註冊，也需在 FT-01 依賴中明確聲明「由 C-02 owner 註冊，FT-01 只消費」。
4. §2 列出的 FT-06、FT-07、FT-10、FT-12、P-02 FSD 目前不存在，僅能依 GDD 推定 API。這些依賴需標為 implementation prerequisite，或在 FT-01 中提供 stub/adapter 契約。
5. 現有 scripts 沒有 `IResourceService` 介面宣告，只有 `ResourceManagement` concrete API。FT-01 若要依賴介面，需列出介面新增位置；若直接依賴 `ResourceManagement.Instance`，需修改 §4.4 / §5.4 的依賴描述。

### 跨系統一致性
C-03、C-04、C-05 的核心消費方向成立：FT-01 以 `GetBaseProfessions()`、`RollRace(professionID)`、`GetProfessionGroups()` / `RollTraits()` 生成隨機冒險者，與三份 FSD 的責任邊界大致一致。

C-02 對齊需要修正：FT-01 不應把 factory 行為掛在 `IAdventurerRoster`。建議 FT-01 §2.3 改為同時依賴 `IAdventurerRoster`（Add/IsFull/GetRoster）與 `IAdventurerFactory`（CreateFromTemplate/CreateRandomInstance），並移除 `IAdventurerRoster.BuildTraitList`。

F-03 行為可支援 FT-01，但 `AddGold` 回傳值必須被 FT-01 檢查。現有 `ResourceManagement.AddGold(int)` 在低於破產門檻或重入時會回 `false`，不是必然成功。

P-03 Notification 仍未設計；FSD 已把它列為非阻礙項，判定正確。P-02 / FT-10 FSD 未存在，UI 與 Save/Load 的實際訂閱、dirty 標記與 restore order 還不能視為完成雙向對齊。

### 資料驅動 / 時間單位 / Script Drift
資料驅動方向合格：`RECRUIT_POOL_SIZE`、`DAILY_FREE_REFRESH`、`REFRESH_COST`、`MIN_RECRUIT_REFRESH_INTERVAL_SEC`、`RecruitCostTable.csv`、`VeteranRankWeightTable.csv` 都表格化。實作前必須補 `Assets/Resources/Data/Tables/*.csv`；目前該目錄只有 `.meta`，沒有對應資料檔。

時間單位合格：GDD 使用 24h/16h/8h，FSD 以秒數 `86400/57600/28800` 與 UTC Unix timestamp 表達，`MIN_RECRUIT_REFRESH_INTERVAL_SEC` 也明確為秒。離線多週期只刷新一次與 F-02 日重置「只發一次」規則一致。

Script Drift 明確存在：目前沒有 FT-01 Recruitment script，屬正向實作；但支撐系統 drift 會影響 FT-01：`TimeSystem.cs` 發布 named `EventNames.OnDailyReset` 而非 typed `OnDailyResetEvent`；`GameEvents.cs` 缺 `OnDailyResetEvent`、`OnPoolRefreshedEvent`、`OnRecruitSuccessEvent`；`IResourceService` 介面在 scripts 中未宣告；Required CSV 尚未建立。

### 建議（依優先序）
1. 修正 §5.4 `ManualRefresh()` 偽碼：刪除觸發點 B 的 `EventBus.Publish(new OnPoolRefreshedEvent())`，保留 `ExecuteRefresh()` 唯一發布點。
2. 修正所有 `AddGold(...)` 呼叫：`ManualRefresh()` 與 `RecruitVeteran()` 必須檢查 `bool` 回傳值；扣款失敗時回 `false`、不刷新、不加入名冊、不移除候選者。
3. 對齊 C-02 介面：FT-01 改依賴 `IAdventurerFactory.CreateFromTemplate/CreateRandomInstance`；`IAdventurerRoster` 僅用於 `AddAdventurer`、`IsRosterFull`、`GetRoster`。
4. 指定事件 struct 與 DataManager 註冊落點：新增 `RecruitmentEvents.cs` 或併入 `GameEvents.cs`；新增 `RegisterTables()` 註冊 `VeteranRankWeightTable`，並說明 `RecruitCostTable` owner 由 C-02 註冊。
5. 裁決 daily reset 契約：要嘛把現有 `TimeSystem.cs` 改為發布 typed `OnDailyResetEvent`，要嘛 FT-01 改訂閱 named `EventNames.OnDailyReset`。FSD、F-02 FSD、現有 script 三者必須一致。
6. 在 §2.4 / §8.3 標記 FT-06、FT-07、FT-10、FT-12、P-02 FSD 未存在；實作時以 mock/stub 契約驗證，不宣稱已完成雙向對齊。
7. 建立初始 CSV：`VeteranRankWeightTable.csv`、`RecruitCostTable.csv`、`SystemConstants.csv` 對應 key；否則 AC-AR-01、AC-AR-17 無法落地。

### 結論：NEEDS REVISION

FT-01 FSD 的玩法規則、資料驅動方向與測試覆蓋已足夠，但目前有 4 個會直接造成實作分歧的問題：`OnPoolRefreshedEvent` 重複發布矛盾、`AddGold` 回傳值未在偽碼處理、C-02 factory/roster 介面歸屬錯置、F-02 daily reset typed/named event drift。修正後可進入 implementation spec。

---

## FT-02 mission-dispatch (FSD-A + FSD-B)

_審查時間：2026-04-27 11:30:49 +08:00_
_FSD 檔案：_
- _FSD-A：design/FSD/【FT-02-FSD-A】mission-dispatch-core.md_
- _FSD-B：design/FSD/【FT-02-FSD-B】commission-board.md_

_對應 GDD：design/GDD/【FT-02】mission-dispatch.md_

### 完整性（兩份各自 + 合計章節齊備度）
FSD-A 具備 §0~§8 與附錄 A，範圍覆蓋成功率、死亡率、派遣 10 步序列、ActiveMission、計時、離線摘要、事件與存檔。FSD-B 具備 §0~§8 與附錄 A，範圍覆蓋 `_regularMissionPool` / `_staticMissionPool`、`PostRegularMission`、`InjectStaticMission`、`RemoveMissionFromBoard`、補池流程、`OnCommissionPosted` 與委託板存檔。合計後可覆蓋 GDD §3.1~§3.10 的主要規則拆分。

章節齊備度合格，但 B 的 §4.2 / §4.3 以「不另拆子模組」收束，實作上可接受；需要補強的是契約細節，而不是章節缺漏。A/B 共用 `OwnerKey="ft02Dispatch"` 的存檔聚合方式只寫在 §5.3 註記，尚未進入 `ICommissionBoardService` 或明確 save DTO 介面。

### GDD 對齊
A 對齊 GDD §3.1~§3.8：`rankDiff`、`SuccessRateTable`、`baseDeathRate` 由 C-01 提供、職業/種族/特質加法修正、clamp、Dispatch 10 步、ActiveMission、`OnMissionCompleted` 與查詢 API 均有落點。B 對齊 GDD §3.9：兩個 pool、Regular/Static source、注入 API、補池流程、派遣後移除與 `OnCommissionPosted` 均有落點。

需修正 2 個對齊缺口。第一，GDD §4.2 的完整示例包含 trait 後為 `0.63 / 0.18`，A 的 AC-MD2-01 只驗到 `0.55 / 0.18`；若 AC-MD2-01 是刻意排除 trait，需明寫，並另補一個完整疊加測試。第二，C-06 FSD 已定義 `OnDangerLevelChanged` payload 為 `(string newDangerLevel)`，B §2.5 / §5.2 卻寫 `int newDangerLevel`，與危險度階級 `E/D/C/B/A` 的字串模型不一致。

### 內部一致性（含 A↔B 契約一致性）
A/B 的核心分工正確：A 發布 `OnCommissionAccepted(missionID, baseReward, DispatchSource)`；B 發布 `OnCommissionPosted(missionID, CommissionSource)`；`DispatchSource` 與 `CommissionSource` 語義未混用；A 在 Dispatch step 10 透過 `ICommissionBoardService.RemoveMissionFromBoard(missionID)` 委託 B 移除 board entry。

主要不一致在存檔契約。GDD §6.4 把 `activeMissions`、`_nextActiveMissionID`、`_regularMissionPool`、`_staticMissionPool` 放在同一 `OwnerKey="ft02Dispatch"` 下；A/B 也同意共用 owner key，但 B §5.1 的公開 API 沒有列出 `GetSerializableState()` / `RestoreState(dto)`，A §5.3 只以註記說會呼叫 B 的狀態方法。這會讓 FT-10 或實作者無法判斷誰是唯一 `ISaveable` owner、restore order 由誰保證、B 的 pool DTO 型別放在哪個檔案。

B 的去重策略也需裁決。`IsCommissionOnBoard` 防止正常路徑重複，但 `GetAvailableCommissions()` 若直接 concat 兩個 pool，在 corrupted save 或雙池同 ID 時會把同一 missionID 暴露給 FT-03/P-02。B §8.3 已列 B-02，但應明確裁決 `GetAvailableCommissions()` 是否回傳 distinct list。

### 可實作性
多數流程可直接轉為 EditMode tests；`CalculateRates`、`Dispatch`、`TickCompletionCheck`、`InjectStaticMission`、`PostRegularMission`、`RefillPool` 的偽碼足夠實作。

阻礙在事件與存檔介面落點。現有 `GameEvents.cs` 沒有 `OnCommissionAccepted`、`OnAdventurerDispatched`、`OnMissionCompleted`、`OnMissionCancelled`、`OnOfflineMissionsResolved`、`OnCommissionPosted` 的 typed event struct；FSD 也未指定新增到 `GameEvents.cs`、`MissionEvents.cs` 或各自 types 檔。若繼續用 typed `EventBus`，FT-02-A/B 應列出事件 struct 檔案、namespace 與 payload 欄位名稱。

此外，B 依賴的 FT-07 / FT-09 / FT-10 / P-02 FSD 目前未存在；A 依賴的 FT-07 也未存在。FSD 可先保留 stub 契約，但 §2 / §8.3 應標明這些是 implementation prerequisite，避免宣稱已完成雙向依賴對齊。

### 跨系統一致性
C-01、C-02、C-03、C-04、C-05、FT-04、FT-05 的已存在 FSD 大致可承接 A 的 API：`GetBaseDuration` / `GetEscortDuration` 回傳分鐘、C-02 `UpdateStatus(instanceID, Dispatched, activeMissionID)`、C-03 `IsStrongType` / `IsWeakType`、C-04 delta、C-05 stat trait、FT-05 引用 `DispatchSource` 都一致。

跨系統問題集中在 F-02 與 C-06。F-02 FSD 定義 `OnDailyResetEvent` 與 `OnOfflineResolvedEvent(long offlineSeconds)`，但現有 `TimeSystem.cs` 發布 named `EventNames.OnDailyReset`，且 `GameEvents.cs` 的 `OnOfflineResolvedEvent` 仍是 `(long OfflineSeconds, int CompletedCount)`。FT-02-A/B 若照 FSD 訂閱 typed daily reset 或只讀單欄 offline payload，現有 script 不會完全對齊。

C-06 目前把 `OnDangerLevelChanged` 定義為 string payload，B 定義為 int payload。這是直接契約衝突，需由 B 改為 `(string newDangerLevel)` 或 C-06 改模型；依 C-06 危險度階級 `E/D/C/B/A`，建議 B 改為 string。

### 資料驅動 / 時間單位 / Script Drift
資料驅動方向合格：成功率表、死亡率、職業修正、護送 type、委託板容量、危險度權重、categoryID 來源均指向 CSV 或上游 API。B 的 `categoryID=0/3` 仍是 magic number 風險；若不新增 SystemConstants，至少應在 `CommissionBoardEnums.cs` 定義 `RegularCategoryID` / `StaticCategoryID` 常數，避免散落在流程碼。

時間單位有已登記 Tech Debt：A 沿用 C-01 `duration` 分鐘，轉成 `completionTimestamp = dispatchTimestamp + duration × 60`。這與 C-01/F-02/FT-02 目前共識一致，不再列為阻礙；實作時不可把 `GetBaseDuration` / `GetEscortDuration` 誤當秒。

Script Drift 明確存在且需要升級處理。`TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs` 目前持有 `_missionTimers`、`RegisterMission`、`UnregisterMission`、`CheckMissionTimers`，並發布 `OnMissionExpiredEvent(string missionInstanceId)`；FT-02-A 則設計由 `MissionDispatchService` 持有 `_activeMissions` 並發布 `OnMissionCompleted(int activeMissionID)`。這不是單純檔案位置偏差，而是計時責任重疊。實作前必須裁決：移除/停用 F-02 的 mission timer 路徑，或讓 FT-02-A 改接 F-02 `OnMissionExpiredEvent`。兩者並存會造成重複完成或離線完成摘要來源分裂。

### 建議（依優先序）
1. 裁決 F-02 / FT-02 計時責任：建議採 FT-02-A 作為唯一任務計時 owner，移除或停用 `TimeSystem.RegisterMission` / `OnMissionExpiredEvent` 任務路徑；同步更新 F-02 FSD、現有 `TimeSystem.cs` 與 FT-02-A §8.3。
2. 修正事件契約：`OnDailyReset` 改為與 F-02 FSD 一致的 typed `OnDailyResetEvent`，或所有 FSD 改訂閱 named `EventNames.OnDailyReset`；`OnOfflineResolvedEvent` payload 統一為只含 `offlineSeconds`，由 FT-02-A 發 `OnOfflineMissionsResolved`。
3. 修正 B 的 C-06 契約：`OnDangerLevelChanged` payload 改為 `(string newDangerLevel)`，並在 B §2.5 / §5.2 / tests 同步。
4. 明確 A/B 共用存檔介面：在 `ICommissionBoardService` 或獨立 save adapter 補 `GetSerializableState()` / `RestoreState(dto)`，列出 DTO 欄位與 restore order，明定只有 `MissionDispatchService` 對 FT-10 暴露 `ISaveable OwnerKey="ft02Dispatch"`。
5. 指定 FT-02 events 的 struct 落點與欄位名稱，至少包含 `OnCommissionAcceptedEvent`、`OnAdventurerDispatchedEvent`、`OnMissionCompletedEvent`、`OnMissionCancelledEvent`、`OnOfflineMissionsResolvedEvent`、`OnCommissionPostedEvent`。
6. 補 GDD §4.2 完整疊加測試：保留 AC-MD2-01 作「無 trait」示例時，新增一條 `0.63 / 0.18` 的 profession + trait full-stack case。
7. 裁決 `GetAvailableCommissions()` 去重策略，建議回傳 distinct list，並在 corrupted restore 後以 LogWarning + 去重修復。
8. 將 FT-07 / FT-09 / FT-10 / P-02 未存在 FSD 標記為 prerequisite；目前只能依 stub 契約驗證，不能宣稱完成雙向對齊。

### 結論：NEEDS REVISION

FT-02 A/B 的功能拆分合理，GDD §3 規則大多已無遺漏且無重疊衝突；`DispatchSource` / `CommissionSource` 已正確分離。仍需修正 F-02 計時與離線事件 drift、C-06 `OnDangerLevelChanged` payload、A/B 共用 `OwnerKey` 的存檔聚合介面，以及 FT-02 events struct 落點。這些修正完成後可進入 implementation spec。

---

## FT-03 npc-decision-system

_審查時間：2026-04-27 11:34:32 +08:00_
_FSD 檔案：design/FSD/【FT-03-FSD】npc-decision-system.md_
_對應 GDD：design/GDD/【FT-03】npc-decision-system.md_

### 完整性（章節齊備度）
章節齊備。FSD 具備 §0 文件資訊、§1 Overview/DoD、§2 來源與依賴、§3 幻想到實作映射、§4 Script 規劃、§5 API/事件/資料流、§6 資料表與參數化、§7 Edge Cases、§8 GDD 對齊與附錄 A Review Log，符合 FSD-index §1-§7 的固定結構。

完成目標覆蓋 GDD §8 的 13 條驗收標準，且多數可直接轉成 EditMode 測試。缺口在 FT-03 宣稱 `ISaveable OwnerKey="ft03Decision"`，但 §4.4 Script 清單未列 `ISaveable`，§5.1 公開 API 也未列 `Serialize()` / `RestoreFromSave()` / `InitializeAsNewGame()`。若 FT-03 真的要作為 FT-10 owner，即使只回傳 `{}`，仍需列入 Script 職責與 API；若不需要 owner，§2.4 應改為「由 C-02 完全持久化，FT-03 僅在 MonoBehaviour 生命週期訂閱事件」。

### GDD 對齊
核心規則對齊。`effectiveScore = finalSuccessRate - finalDeathRate × DEATH_AVERSION + behavior deltas + staff bonus + jitter`、不 clamp、`RejectionReason` 三分支、推薦路徑不直接 dispatch、自主接單路徑呼叫 `FT02.Dispatch(..., NpcAutoPick)`，均與 GDD §3-§5 一致。

需修正兩個文字層級不精確點。第一，§1.1 寫「不直接執行派遣」，同句又說自主接單直接呼叫 FT-02 API；應改為「推薦路徑不 dispatch，自主接單成功時由 FT-03 呼叫 FT-02 Dispatch」。第二，§7 `lastAutoPickupTimestamp = 0` 的描述同時說「差值很大會通過守衛」與「等待 `AUTO_PICKUP_IDLE_SECONDS` 後觸發」；實際觸發條件由 `idleSinceTimestamp` 決定，若存檔中的 Idle 已超過門檻，重啟後第一個 `OnMinuteTick` 就會觸發，而不是重新等待 10 分鐘。

### 內部一致性
公式與拒絕原因判定一致，`PreviewEffectiveScore` 明確回傳 Step 3 後分數且不含 jitter，和 UI 預覽用途相符。

主要不一致在事件與依賴命名。§2.5 / §5.2 將 `OnMinuteTick` payload 寫成 `float nowUTC`，但 F-02 FSD 與現有 `GameEvents.cs` 均為 `OnMinuteTickEvent(long nowUTC)` / `NowUTC`。§4.5 又寫 `EventBus<OnMinuteTick>`，與實際 typed event 名稱不一致。此處會直接影響編譯與訂閱。

資料存取介面也不一致。FT-03 §2.3 / §6.1 使用 `IDataManager.GetConstant<float>(key)`，但 F-01 FSD 與現有 DataManager 契約是 `GetFloat(key)` / `GetInt(key)`，沒有 `GetConstant<T>`。FT-03 應改用 `GetFloat` 讀 5 個常數，或先在 F-01 FSD/實作補出泛型 API，不能在 FT-03 單方引用。

### 可實作性
`MakeDecision`、`PreviewEffectiveScore`、`AutoPickupTick`、`TryAutoPickup` 的流程足夠實作，且對 FT-02 / C-02 / C-05 的主要呼叫點清楚。

阻礙點有三項。第一，事件契約需改為 `EventBus.Subscribe<OnMinuteTickEvent>` 並讀 `evt.NowUTC : long`。第二，`Random.Range` 測試可控性未指定；AC-ND3-01 / AC-ND3-03 要固定 jitter，FSD 應指定以 `IRandomProvider`、可注入 delegate，或在測試中保存/還原 `UnityEngine.Random.state`，否則 EditMode test 不穩定。第三，FT-03 `ISaveable` 的生命週期未定義清楚；`RestoreFromSave` 重新訂閱 `OnMinuteTick` 可能與 `OnEnable` 重複訂閱，應指定訂閱只在 `OnEnable`，`OnDisable` 解除，Restore 不做 event subscribe。

### 跨系統一致性
已存在的 C-02、C-05、FT-02-A/B 可承接大部分需求。C-02 已定義 `idleSinceTimestamp` / `lastAutoPickupTimestamp`、`GetByStatus(Idle)`、`SetLastAutoPickupTimestamp`；C-05 提供 behavior trait 查詢；FT-02-A 提供 `CalculateRates` / `Dispatch` 與 `DispatchSource.NpcAutoPick`；FT-02-B 提供 `GetAvailableCommissions()`。

仍有未完成依賴。FSD-index 顯示 FT-06、FT-12、FT-10 尚未有 FSD，P-02 / P-03 也未在 FSD 目錄中出現；因此 `IGuildCoreService.GetMaxMissionDifficulty()`、`IStaffService.GetStaffWillingnessBonus()`、`OnAutoPickupEvent` 的 UI/Notification 消費與 FT-10 存檔契約只能視為 stub 契約，不能視為已完成雙向對齊。FT-03 §8.3 應把這些列為 implementation prerequisites，而不是只列 P-03 Log API。

現有 Script 方面，`GameEvents.cs` 尚無 `OnAutoPickupEvent`，FT-03 新增 `NpcDecisionTypes.cs` 時可自行定義；但 `GameEvents.cs` 中既有 F-02 事件命名與 payload 已固定，FT-03 必須跟隨現有 `OnMinuteTickEvent(long NowUTC)`。此外，TimeSystem 目前仍有 `OnMissionExpiredEvent` / mission timer drift，屬 FT-02 已登記問題；FT-03 自主接單只需依賴 FT-02 Dispatch 成敗，不應接觸 F-02 mission timer 路徑。

### 資料驅動 / 時間單位 / Script Drift
資料驅動方向正確：`DEATH_AVERSION`、`ACCEPTANCE_THRESHOLD`、`WILLINGNESS_JITTER`、`AUTO_PICKUP_IDLE_MINUTES`、`AUTO_PICKUP_INTERVAL_MINUTES` 均來自 `SystemConstants.csv`；behavior delta 來自 `TraitTable.csv`。但 repo 搜尋只看到這 5 個 key 在 FSD/GDD/index 中出現，尚未確認 `SystemConstants.csv` 實體資料已存在；實作前需補資料或以 Data-Spec 驗收擋住缺 key。

時間單位處理可接受但需精確：GDD 與 FSD 已登記 `AUTO_PICKUP_*_MINUTES` 為 Tech Debt，載入後轉秒快取。事件 payload、C-02 timestamp、F-02 `NowUTC` 應全程使用 `long` 秒，不使用 `float`。

Script Drift 明確存在：現有 `GameEvents.cs` 的 `OnOfflineResolvedEvent` 仍含 `CompletedCount`，`OnDailyReset` 仍是 named event，這是 F-02 drift；對 FT-03 的直接 drift 是 `OnMinuteTickEvent` 名稱與型別已存在但 FSD 寫錯。FT-03 實作前需先修 FSD 契約，否則會與現有事件系統不相容。

### 建議（依優先序）
1. 將 FT-03 的 F-02 tick 契約全部改為 `OnMinuteTickEvent(long NowUTC)`；§2.5、§4.5、§5.2、§5.4 同步改名，禁止使用 `float nowUTC`。
2. 將 DataManager 依賴改為 `IDataManager.GetFloat(key)`，或先在 F-01 補正式 `GetConstant<T>` 契約；不可在 FT-03 單方使用不存在 API。
3. 裁決 FT-03 是否真的實作空 `ISaveable`。若是，§4.4 / §5.1 補 `ISaveable` 方法與訂閱生命週期；若否，刪除 `OwnerKey="ft03Decision"`，改由 C-02 與 MonoBehaviour `OnEnable` / `OnDisable` 負責。
4. 為 jitter 測試指定可控 RNG 策略，確保 AC-ND3-01 / AC-ND3-03 可重現。
5. 修正 `lastAutoPickupTimestamp = 0` 的邊緣案例描述：首次載入是否立即於下一個 minute tick 觸發，取決於 persisted `idleSinceTimestamp` 是否已超過 `_autoPickupIdleSeconds`。
6. 在 §8.3 登記缺 FSD prerequisite：FT-06、FT-12、FT-10、P-02、P-03 尚未完成 FSD，相關介面暫以 GDD stub 驗證。
7. 新增 `OnAutoPickupEvent` struct 的落點與 namespace，建議維持在 `NpcDecisionTypes.cs`，並明列 P-03 只訂閱該 typed event。

### 結論：NEEDS REVISION

FT-03 的玩法規則、公式、資料驅動與 GDD §3-§5 對齊良好，沒有需要重寫設計的問題。但目前存在會阻斷實作的介面不一致：`OnMinuteTick` 型別/名稱、DataManager API、空 `ISaveable` 契約，以及缺 FSD 依賴未標 prerequisite。修正上述契約後可進入 implementation spec。

---

## FT-04 outcome-resolution

_審查時間：2026-04-27 11:38:38 +08:00_
_FSD 檔案：design/FSD/【FT-04-FSD】outcome-resolution.md_
_對應 GDD：design/GDD/【FT-04】outcome-resolution.md_

### 完整性（章節齊備度）
章節齊備。FSD 具備 §0 文件資訊、§1 概要、§2 設計來源與依賴、§3 幻想到實作映射、§4 Script 規劃、§5 API/事件/資料流、§6 資料表使用、§7 邊緣案例、§8 GDD 對齊自檢與附錄 A Review 紀錄，符合 FSD-index §3 固定章節格式。

完成目標可測試，但覆蓋不完整。§1.3 有成功率 0/1、步驟 2 null、`on_death_survive`、B 難度聲望、事件順序與 CSV 載入測試；缺少 GDD AC-OR-10 機率分佈、AC-OR-12 兩次擲骰獨立性、AC-OR-17 gold bonus、AC-OR-23 F-03 clamp、AC-OR-27/28 離線批次等代表性驗收。這不阻擋規格理解，但會降低實作驗收完整度。

### GDD 對齊
核心流程對齊 GDD §3.1~§3.9：Outcome 欄位、12 步結算管線、成功/死亡兩次獨立擲骰、condition 套用在擲骰後且 MapFinalStatus 前、聲望先 baseDelta 再 condition 疊加、C-02 狀態更新、`OnMissionResolved` 先於 `RemoveActiveMission`。

存在一項未登記的 GDD 內部衝突：GDD §5.1 規定缺少 `DEATH_RATE_ON_SUCCESS_MULTIPLIER` 時 fallback `0.5`，FSD §5.4/§7 也採 `0.5`；但 GDD AC-OR-02 寫「缺失時預設 `1.0`」。FSD §8.5 未記錄此衝突。實作若依驗收標準會得到與 FSD 不同的死亡率。

FT-09 對齊不足。FT-09 GDD 明確消費 `missionFactionID`、`isSuccess`、`missionDifficulty`、`missionID`、`isDead`；FT-04 FSD §2.4 只列 `missionFactionID`、`isSuccess`、`missionTypeID`。雖然 Outcome DTO 已含 `missionID`、`missionDifficulty`、`isDead`，下游依賴表未完整反映 FT-09 契約。

### 內部一致性
結算順序、公式與最終狀態映射一致。`(T,F,F)`、`(T,F,T)`、`(T,T,F)`、`(F,F,T)`、`(F,T,F)` 五種結果可由資料流推導，`(F,F,F)` 與 `(T,T,T)` 的不可達說明也與步驟 5/7/8 相符。

主要不一致在介面抽象。§5.1 宣稱 FT-04 無對外公開 API，但 §4.4 又規劃空的 `IOutcomeResolutionService` 作為 Service Locator 佔位。這不會破壞規則，但會讓實作產生無方法介面與註冊行為的歧義。若保留，需明確寫出是否註冊與註冊目的；若不保留，應刪除該 Script。

### 可實作性
目前不可直接交付實作，原因是三個契約會造成編譯或資料載入失敗。

第一，F-01 DataManager API 不相容。FT-04 FSD §2.3 / §5.3 / §5.4 使用 `IDataManager.GetTable<ReputationDeltaTable>()` 與 `GetTable<SystemConstants>()`，但 F-01 FSD 與既有 `DataManager.cs` 公開契約是 `Get<T>(id)`、`GetAll<T>()`、`GetWhere<T>()`、`GetFloat(key)`、`GetInt(key)`，沒有 `GetTable<T>()` 或回傳 dictionary 的 API。FT-04 應改為 `GetAll<ReputationDeltaData>()` 建快取，並用 `GetFloat("DEATH_RATE_ON_SUCCESS_MULTIPLIER")` 讀常數，或先在 F-01 補正式 API。

第二，C-05 契約不相容。FT-04 FSD 將 `ITraitService.ApplyConditionTraits(Outcome, int[] traitIDs)` 視為上游 API；但 C-05 FSD §5.1 的 `ITraitService` API 清單沒有此方法，且 C-05 FSD §7/§8 明確說 `ApplyConditionTraits` 的套用邏輯由 FT-04 負責，C-05 只提供 `GetTraitsByType("condition")` / `GetTrait(traitID)`。需二選一：補 C-05 FSD/API 讓 C-05 實作 `ApplyConditionTraits`，或改 FT-04 FSD 由 `OutcomeResolutionService` 讀取 condition traits 並自行套用。

第三，Data-Spec 未支援必要常數。`design/Data-Specs/【F-01-DS】system-constants.md` 的已註冊 key 清單與範例 CSV 不含 `DEATH_RATE_ON_SUCCESS_MULTIPLIER`。FT-04 FSD 把它列為資料驅動來源，但實作階段會讀不到 key 並永遠走 fallback。需先補 Data-Spec 與實體 CSV 規格。

### 跨系統一致性
FT-02-A 的 `OnMissionCompleted(int activeMissionID)`、`GetActiveMission`、`RemoveActiveMission` 與 FT-04 需求一致；`RemoveActiveMission` 不存在時 warning 的邊界也對齊。C-01 的 `GetTemplate` / `GetBaseReward`、C-02 的 `GetAdventurer` / `UpdateStatus` / `SetWounded`、F-03 的 `AddReputation` 可承接主要流程。

FT-05 對 `OnMissionResolved` 的消費欄位與 FT-04 §2.4 一致，且金流邊界清楚：FT-04 只填 `conditionGoldBonus`，不呼叫 `AddGold`。F-03 FSD §2.4 中仍寫 FT-04 可能「增減金幣與聲望」，但現行 FT-04/F-05 分工已把金幣移到 FT-05，建議後續回註 F-03 文件以免誤導。

P-02 / P-03 仍無 FSD；FT-04 對 UI/Notification 的消費欄位只能視為 stub 契約。FT-04 不需等待它們才能實作，但 §8.3 應把 P-02/P-03 未設計列為 implementation prerequisite。

### 資料驅動 / 時間單位 / Script Drift
`ReputationDeltaTable` Data-Spec 完整，9 個難度、`successDelta`、`failDelta` 與 A 階非單調設計意圖都已記錄。`MissionDifficultyTable.baseReward` 透過 C-01 讀取，owner 邊界正確。

資料驅動缺口在 `SystemConstants`。`DEATH_RATE_ON_SUCCESS_MULTIPLIER` 已在 FSD-index 與 FT-04 FSD 被引用，但 F-01 Data-Spec 沒有 key；且 GDD AC-OR-02 fallback `1.0` 與 FSD fallback `0.5` 衝突。實作前必須補資料規格與驗收值。

時間單位合規。FT-04 本身不使用「分鐘」，`WOUNDED_RECOVERY_HOURS` 由 C-02 擁有且以小時計，符合 CLAUDE.md 與 FSD-index 的時間單位規範。

既有 Script drift：repo 中沒有 `Assets/Scripts/Gameplay/Outcome` 或 Outcome 相關 Script，屬正向 FSD；現有 `EventBus.cs` 已對 typed event handler 做 try/catch 隔離，支援 FSD §7 對訂閱者例外的假設。現有 `DataManager.cs` 沒有 `GetTable<T>()`，這是本 FSD 的直接 Script drift。

### 建議（依優先序）
1. 修正 F-01 存取契約：將 `GetTable<ReputationDeltaTable>()` / `GetTable<SystemConstants>()` 改為既有 `GetAll<T>()` + `GetFloat(key)`，並明列 `ReputationDeltaData` DTO 註冊方式。
2. 裁決 condition 套用責任。若由 C-05 提供 `ApplyConditionTraits`，同步更新 C-05 FSD §5.1；若由 FT-04 實作，移除 `ITraitService.ApplyConditionTraits` 依賴並補 FT-04 的 condition 迴圈偽碼。
3. 補 `SystemConstants` Data-Spec 與範例 key：`DEATH_RATE_ON_SUCCESS_MULTIPLIER`，型別 `float`，範圍 `[0.0, 1.0]`，預設 `0.5`。
4. 裁決 GDD AC-OR-02 的 fallback `1.0` vs `0.5`，並於 FSD §8.5 登記衝突處理紀錄。
5. 補完整 FT-09 下游欄位：`missionDifficulty`、`missionID`、`isDead`；保留 `missionTypeID` 時需說明其用途是否仍由 FT-09 消費。
6. 將 P-02 / P-03 未有 FSD、FT-09 尚無 FSD 的依賴狀態列入 §8.3 prerequisites。
7. 擴充 §1.3 DoD，至少補 `conditionGoldBonus`、F-03 clamp、兩次擲骰獨立性、離線多任務結算四類測試。

### 結論：NEEDS REVISION

FT-04 的核心設計方向正確，結算管線與 GDD 主要規則一致，不需要重寫系統。但目前存在會阻斷實作的跨系統契約錯誤：F-01 API 不存在、C-05 condition 套用責任互斥、SystemConstants 缺 key，另有 GDD fallback 值衝突未登記。修正上述契約後可重新審查。

---

## FT-05 guild-gold-flow

_審查時間：2026-04-27 11:43:31 +08:00_
_FSD 檔案：design/FSD/【FT-05-FSD】guild-gold-flow.md_
_對應 GDD：design/GDD/【FT-05】guild-gold-flow.md_

### 完整性（章節齊備度）
章節齊備。FSD 具備 §0 文件資訊、§1 概要、§2 設計來源與依賴、§3 幻想到實作映射、§4 Script 規劃、§5 API/事件/資料流、§6 資料表使用、§7 邊緣案例、§8 GDD 對齊自檢與附錄 A/B，符合 FSD-index §3 的標準章節格式。

DoD 覆蓋委託預收、結算、clamp、破產狀態、生命週期、CSV 參數化與降級測試，驗收面向足夠。需修正一個測試描述：AC-8 要求 `CommissionBreakdown` 20 欄「皆非預設空值」過嚴，因 `conditionGoldBonus=0`、未招募會計 bonus=0、成功時 `penaltyGoldAmount=0`、失敗時 `commissionGoldAmount=0` 都是合法值。應改為「必填欄位已填入；分支合法為 0 的欄位依路徑驗證」。

### GDD 對齊
核心規則對齊 GDD §3.1~§3.10：預收使用 `AddGold`，結算使用 `AddGoldAllowBankruptcy`，成功/失敗淨額公式、`conditionGoldBonus` 成敗皆加、`bankruptcyStateBefore/After` 快照、`DispatchSource` 透傳、無對外查詢 API 皆一致。GDD P-001 後，`CommissionSource` 與 `DispatchSource` 命名衝突已在 FSD 中同步修正。

固定支出範圍需標記更精準。GDD Scope Note 說 Jam 階段不驗證維護費/薪水實際扣款，只驗證「無上游事件時無金流、無輸出」；FSD AC-9~AC-11 則要求可手動發布 `OnGuildMaintenanceDue` / `OnStaffSalaryDue` 並驗證扣款。這可作為 Phase 2 handler 單元測試保留，但應在 AC 表中明確標為 Phase 2/deferred，避免 Jam 驗收誤以為 FT-07/FT-12 已需發布事件。

### 內部一致性
金流管線順序一致：加成查詢先於有效率計算，淨額計算先於 `ExecuteGoldFlow`，timestamp 寫入後發布 settled event。`netDelta` 保留公式值、不回寫 clamp 後實際差額，與 §5.3.1 的 P-02 顯示責任一致。

主要不一致是 §8.3 B-01 的狀態。FT-05 FSD 仍說 F-03 GDD/FSD 需補「單次 `netDelta` 無雙重穿越零線」保證；但 F-03 FSD 已列 AC-RM-19「單次至多一次過渡」，並於 §8.1 對齊 `AddGoldAllowBankruptcy` 單次線性變化保證。FT-05 應更新 B-01 為「已由 F-03 FSD 覆蓋；若需 GDD 回註再登記」，避免留下過期 blocker。

### 可實作性
目前不可直接交付實作，存在三個會阻斷編譯或 runtime 行為的契約缺口。

第一，FSD 使用不存在的 service interface。§2.3、§4.4、§4.5、§6.1 寫 `IDataManager`、`ITimeService`、`IResourceService`；但既有 Script 與對應 FSD 實際公開的是 `DataManager.Instance.GetFloat/GetInt`、`TimeSystem.NowUTC`、`ResourceManagement.GetGold/AddGold/AddGoldAllowBankruptcy/GetBankruptcyWarningState`，repo 內沒有 `IDataManager`、`ITimeService`、`IResourceService` interface。FT-05 必須二選一：改依現有 concrete singleton/MonoBehaviour 契約，或先在 F-01/F-02/F-03 補正式 interface 與註冊方式。

第二，事件型別落點不足。現有 `EventBus` 是 `Subscribe<T>` / `Publish<T>` 的強型別事件系統；FT-05 FSD 只列 tuple payload，例如 `(int missionID, int baseReward, DispatchSource source)`，但未規劃 `OnCommissionAcceptedEvent`、`OnCommissionPrepaidEvent`、`OnCommissionSettledEvent`、`OnMaintenanceChargedEvent`、`OnSalaryChargedEvent` 等具體 struct/class 與 namespace。若直接用 tuple 會造成事件型別不明確且不符合現有 `ResourceEvents.cs` 模式。建議在 `GoldFlowTypes.cs` 或新增 `GoldFlowEvents.cs` 明列事件資料型別。

第三，`IGoldFlowService` 被定義為空介面。若只是 Service Locator 佔位，需明確說明是否註冊、誰會解析、測試如何 mock；若沒有使用者，應移除該 Script，避免產生無方法 interface 的維護成本。

### 跨系統一致性
FT-02-A 的 `DispatchSource` 與 `OnCommissionAccepted` 契約已對齊；FT-04 的 `Outcome.conditionGoldBonus`、`baseReward`、`isSuccess` 可支援結算；F-03 既有 `ResourceManagement.cs` 已有 `AddGoldAllowBankruptcy` 與 `GetBankruptcyWarningState`，金流核心可承接。

FT-07、FT-12、FT-06、FT-10、P-02、P-03 尚無 FSD；FT-05 目前對這些系統的事件/API 只能視為 GDD stub。這不阻擋委託金流實作，但應在 §8.3 列為 implementation prerequisite，特別是 `IStaffService`、`IGuildBuildingService`、`OnGuildMaintenanceDue`、`OnStaffSalaryDue` 的最終簽名需待對應 FSD 確認。

現有 EventBus 會在 handler 內 catch exception 並 log，不會把訂閱者例外向外傳播。FT-05 FSD 的「不 try-catch，例外向上至 EventBus 層」與現有 EventBus 隔離行為相容，但 §7 的描述應避免暗示例外會中斷整個 publish 流程。

### 資料驅動 / 時間單位 / Script Drift
資料驅動目前未通過。`design/Data-Specs/【F-01-DS】system-constants.md` 與 `TheGuild-unity/Assets/Resources/Data/Tables/` 現況未包含 `COMMISSION_RATE` / `PENALTY_RATE`；實體 `SystemConstants.csv` 也不存在。FT-05 若照 FSD 呼叫 `GetFloat("COMMISSION_RATE")` / `GetFloat("PENALTY_RATE")`，會讀不到 key，預設 0.20/0.10 不會成立。實作前必須補 Data-Spec key、預設值、範圍與實體 CSV。

時間單位合規。FT-05 使用 `long` UTC Unix seconds 作為 `settleTimestamp` / `chargeTimestamp`，未引入分鐘單位；GDD 提到的分鐘 Tech Debt 與 FT-05 無直接關係。

既有 Script drift：沒有 `Assets/Scripts/Gameplay/GoldFlow/` 既有實作，屬正向 FSD；F-03 ResourceManagement 已支持必要金流 API。直接 drift 是 FSD 宣稱的 `IResourceService` / `IDataManager` / `ITimeService` interface 不存在，以及事件型別未對齊現有 typed EventBus 模式。

### 建議（依優先序）
1. 補 `SystemConstants` Data-Spec 與實體 CSV：`COMMISSION_RATE=float, default 0.20, safe [0.05,0.50]`；`PENALTY_RATE=float, default 0.10, safe [0.05,0.30]`，並確認由現有 `DataManager.RegisterSystemConstantsTable("SystemConstants")` 載入。
2. 裁決依賴注入策略：若不新增 interface，將 FT-05 全文 `IDataManager` / `ITimeService` / `IResourceService` 改為現有 `DataManager.Instance` / `TimeSystem` / `ResourceManagement` 契約；若要 interface，先回補 F-01/F-02/F-03 FSD 與 Script。
3. 在 Script 規劃中新增事件資料型別落點，至少包含 `OnCommissionAcceptedEvent`、`OnCommissionPrepaidEvent`、`OnCommissionSettledEvent`、`OnGuildMaintenanceDueEvent`、`OnMaintenanceChargedEvent`、`OnStaffSalaryDueEvent`、`OnSalaryChargedEvent`。
4. 將 AC-8 改為欄位初始化與分支值驗證，不要求所有欄位非 0。
5. 將 AC-9~AC-11 標記為 Phase 2 handler 測試或 deferred；Jam 驗收另列「無上游事件時不產生固定支出金流」。
6. 更新 §8.3 B-01 狀態，註明 F-03 FSD AC-RM-19 已覆蓋單次過渡保證；若仍需 GDD 回註，明確列為文件同步項而非 FT-05 blocker。
7. 將 FT-07/FT-12/P-02/P-03/FT-10 尚無 FSD 的依賴狀態列入 §8.3 prerequisite，避免實作期誤判契約已定案。

### 結論：NEEDS REVISION

FT-05 的核心金流設計、GDD 規則、公式與跨系統方向正確，不需要重寫。但 Data-Spec 缺 `COMMISSION_RATE/PENALTY_RATE`、上游 service interface 與既有 Script 不一致、typed EventBus 事件型別未落點，會直接阻斷實作或造成 runtime 讀值錯誤。修正上述契約後可重新審查並進入實作。

---
