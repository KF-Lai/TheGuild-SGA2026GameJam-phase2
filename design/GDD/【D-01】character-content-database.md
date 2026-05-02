# Character Content Database 系統設計文件

_建立時間：2026-05-02_
_狀態：已設計_
_系統 ID：D-01_

---

## 1. 概要（Overview）

D-01 Character Content Database 是 Data 層的純內容系統，為隨機生成的冒險者提供兩種文字內容：**名字**與**背景故事（Bio）**。系統本身不含任何 gameplay 邏輯，不訂閱任何事件，也不主動修改遊戲狀態；它僅作為 F-01 DataManager 的消費方，從 `NamePool.csv` 與 `BioPool.csv` 取得資料並暴露查詢 API。

`NamePool` 以種族（raceID）為分組索引，提供 `PickRandomNameWithGender(raceID) : (string name, int gender)`，回傳單一顯示名稱（無姓氏）與性別。`BioPool` 以種族與職業的組合為分組索引，提供 `GetRandomBio(raceID, professionID, name, gender) : string`，回傳代入 `{name}` / `{pronoun}` 變數後的一段角色小傳文字，供 P-02 名冊面板顯示；bio 於冒險者建立時一次性生成並儲存於 `AdventurerInstance.bio`。

Jam 版實作 Game Jam 完整版所需的最小內容量：每種族至少 6 筆單名（確保候選池 4 人不重名有餘裕）；每種族 × 職業組合至少 1 條 bio 模板（可複用，以 `{name}` / `{pronoun}` 讓每次讀起來不同）。

## 2. 玩家幻想（Player Fantasy）

D-01 是看不見的系統，但玩家感受到的是它最核心的產出——「這個冒險者是誰」。

當玩家在名冊上滑過一位剛自薦入公會的 E 級新人，看到的不是「冒險者 #7」，而是「利恩 · 艾芙，精靈，遊俠」，配上一行「她十五歲離開森林，說是為了錢，但眼神裡有什麼東西藏得更深。」——那一刻，這個 NPC 從數值變成了人。

這種「第一印象」是情感連結的種子。玩家看到的名字與故事越真實、越有個性，就越容易在之後的任務派遣時產生猶豫：「這個人可以去 B 難度討伐嗎？」不是數值計算，是擔心。

MDA 對應：**Narrative（敘事劇情，優先度 1）** 與 **Fellowship（情感連結，優先度 6）**——D-01 是這兩層美學的地基。

## 3. 詳細規則（Detailed Rules）

### 3.1 NamePool 資料表

單名設計（無姓氏），schema：

| 欄位 | 型別 | 說明 |
|------|------|------|
| `nameID` | `int` (PK) | 從 1 起；0 為 null sentinel |
| `name` | `string` | 單一顯示名稱（例：利恩、克拉格、艾芙） |
| `gender` | `int` | `0` = 男（他）、`1` = 女（她）、`2` = 中性（牠/其） |
| `raceID` | `int` | FK → RaceTable；決定此名字屬於哪個種族 |

> `gender` 欄位供 BioPool 模板替換 `{pronoun}` 用；若 bio 模板不含代名詞可忽略。

**Jam 最小內容量**：每個 raceID 至少 6 筆（保證候選池 4 人不重名，有餘裕）。共 4 × 6 = **24 筆**。

---

### 3.2 BioPool 資料表

| 欄位 | 型別 | 說明 |
|------|------|------|
| `bioID` | `int` (PK) | 從 1 起 |
| `raceID` | `int` | FK → RaceTable；`0` = 任何種族皆適用（fallback 用） |
| `professionID` | `int` | FK → ProfessionTable；`0` = 任何職業皆適用（fallback 用） |
| `template` | `string` | bio 文字模板，含替換變數（見 §3.3） |

**Jam 最小內容量**：4 種族 × 7 職業 = 28 筆具體模板，每種組合至少 1 筆。允許以 `professionID=0` 或 `raceID=0` 建立 fallback 通用模板，當特定組合查無結果時使用。

---

### 3.3 BioPool 模板替換規則

模板字串中支援以下替換變數，runtime 由 `GetRandomBio` 執行：

| 變數 | 替換為 | 範例 |
|------|--------|------|
| `{name}` | `AdventurerInstance.name` | 利恩 |
| `{pronoun}` | gender=0→「他」、gender=1→「她」、gender=2→「其」 | 她 |

模板範例：`「{name} 十五歲離開森林，說是為了錢，但{pronoun}眼神裡有什麼東西藏得更深。」`

---

### 3.4 查詢 API

| API | 簽名 | 說明 |
|-----|------|------|
| 隨機名字 | `PickRandomName(int raceID) : string` | 從 `NamePool` 依 raceID 過濾後加權均勻抽取，回傳 `name` 字串；raceID 無結果時 fallback 至 raceID=1（人類） |
| 背景故事 | `GetRandomBio(int raceID, int professionID, string name, int gender) : string` | 依 raceID + professionID 精確匹配；無結果時依序 fallback（見 §3.5）；回傳替換 `{name}` / `{pronoun}` 後的完整 bio 字串 |

---

### 3.5 BioPool 查詢優先序（Fallback Chain）

```
1. raceID=X  × professionID=Y  → 精確匹配（最優先）
2. raceID=X  × professionID=0  → 同種族通用模板
3. raceID=0  × professionID=Y  → 同職業通用模板
4. raceID=0  × professionID=0  → 完全通用模板
5. 皆無結果  → 回傳空字串（""）；呼叫方自行決定是否顯示佔位文字
```

## 4. 公式（Formulas）

### 4.1 PickRandomNameWithGender(raceID)

```
PickRandomNameWithGender(raceID):
    pool = NamePool.Where(n => n.raceID == raceID)

    if pool.IsEmpty:
        pool = NamePool.Where(n => n.raceID == 1)   // fallback 人類
        Debug.LogWarning("D01: no names for raceID=" + raceID + ", fallback to human")

    if pool.IsEmpty:
        Debug.LogError("D01: NamePool is empty")
        return ("???", 0)

    entry = pool.PickRandom()
    return (entry.name, entry.gender)
```

> C-02 `CreateRandomInstance` 改呼叫此 API（取代原 `PickRandomName`）以取得 name 與 gender。  
> `PickRandomName(raceID) : string` 保留為轉接 wrapper（回傳 tuple.name），供其他不需要 gender 的呼叫者使用。

---

### 4.2 GetRandomBio(raceID, professionID, name, gender)

```
GetRandomBio(raceID, professionID, name, gender):
    // Fallback Chain（§3.5）
    candidate = BioPool.Where(b => b.raceID==raceID && b.professionID==professionID).PickRandom()
    if candidate == null:
        candidate = BioPool.Where(b => b.raceID==raceID && b.professionID==0).PickRandom()
    if candidate == null:
        candidate = BioPool.Where(b => b.raceID==0 && b.professionID==professionID).PickRandom()
    if candidate == null:
        candidate = BioPool.Where(b => b.raceID==0 && b.professionID==0).PickRandom()
    if candidate == null:
        Debug.LogWarning("D01: no bio for race=" + raceID + " prof=" + professionID)
        return ""

    // 變數替換
    pronounMap = { 0: "他", 1: "她", 2: "其" }
    text = candidate.template
              .Replace("{name}", name)
              .Replace("{pronoun}", pronounMap[gender] ?? "其")
    return text
```

---

### 4.3 C-02 CreateRandomInstance 修訂（cross-system patch 預告）

```
CreateRandomInstance(rank, professionID, raceID, traitIDs):
    (name, gender) = D01.PickRandomNameWithGender(raceID)      // 改為 tuple API
    bio            = D01.GetRandomBio(raceID, professionID, name, gender)  // 新增
    instance.name   = name
    instance.gender = gender                                    // C-02 新欄位
    instance.bio    = bio                                       // C-02 新欄位
    // 其餘欄位不變
```

> `gender` 與 `bio` 需納入 FT-10 序列化（`ISaveable` 欄位列表）。

## 5. 邊緣案例（Edge Cases）

### 5.1 資料載入

| 情況 | 處理方式 |
|------|---------|
| `NamePool` 完全為空 | `Debug.LogError`；`PickRandomNameWithGender` 回傳 `("???", 0)` |
| 特定 raceID 無任何名字 | `Debug.LogWarning`；fallback 至 raceID=1（人類）；人類也無結果才 LogError |
| `BioPool` 完全為空 | `Debug.LogWarning`；`GetRandomBio` 回傳 `""`，UI 顯示佔位文字 |
| `NamePool.raceID` FK 在 RaceTable 找不到 | `Debug.LogWarning`，仍保留該筆資料供查詢（D-01 不驗證 FK，載入時不過濾） |
| `BioPool.professionID` FK 在 ProfessionTable 找不到 | 同上，仍保留；Fallback Chain 仍可執行 |
| `template` 為空字串 | 視為有效模板；變數替換後回傳空字串，不 LogWarning |

---

### 5.2 Runtime 查詢

| 情況 | 處理方式 |
|------|---------|
| `PickRandomNameWithGender` 傳入 raceID=0 | fallback 至 raceID=1（0 為 null sentinel，不應作為查詢條件） |
| `gender` 傳入合法範圍外的值（如 -1、99） | `pronounMap` 查無，fallback 至「其」，不 LogWarning |
| `template` 含未定義的 `{variable}` | 保留原文（不替換），不 LogError（未來擴充變數時向後相容） |
| `name` 為空字串或 null | 替換 `{name}` 後為空或報例外；呼叫方（C-02）負責確保傳入非空名字 |
| BioPool 同一 raceID×professionID 有多筆 | 均勻隨機抽取其中一筆（不加權） |

## 6. 依賴關係（Dependencies）

### 6.1 上游依賴（D-01 依賴的系統）

| 系統 | 依賴內容 | 介面 |
|------|---------|------|
| F-01 DataManager | 載入 `NamePool`、`BioPool` | `DataManager.GetAll<NameEntry>()`、`DataManager.GetAll<BioEntry>()` |
| C-04 Race System | `NamePool.raceID` FK 語意（raceID=1 為 fallback 保留值） | 僅 schema 約定，無 runtime API 呼叫 |
| C-03 Profession System | `BioPool.professionID` FK 語意 | 僅 schema 約定，無 runtime API 呼叫 |

---

### 6.2 下游依賴（依賴 D-01 的系統）

| 系統 | 依賴內容 | 使用介面 |
|------|---------|---------|
| C-02 Adventurer Management | 隨機生成冒險者時取得名字、gender、bio | `PickRandomNameWithGender(raceID)`、`GetRandomBio(raceID, professionID, name, gender)` |
| P-02 Main UI | 名冊顯示冒險者 bio | 讀取 `AdventurerInstance.bio`（C-02 欄位，D-01 在建立時已填入） |

---

### 6.3 Cross-System Patch（C-02 必須同步更新）

D-01 設計確定後，C-02 `AdventurerInstance` 需補以下欄位：

| 欄位 | 型別 | 說明 |
|------|------|------|
| `gender` | `int` | 0=男、1=女、2=中性；由 `PickRandomNameWithGender` 取得 |
| `bio` | `string` | 背景故事文字；由 `GetRandomBio` 生成，一次性寫入，顯示時直接讀取 |

`CreateRandomInstance` 偽碼對應修訂見 §4.3。`gender` 與 `bio` 均須納入 FT-10 `ISaveable` 序列化欄位列表。

> **具名 NPC（templateID > 0）不從 D-01 取名字或 bio**：`AdventurerTemplate.csv` 已有固定 `name` 欄位；bio 可在 template 中新增 `bio` 欄位（靜態文字，無需模板替換），或留空（P-02 顯示時以佔位文字代替）。此為 Jam 版設計意圖，具名 NPC 的 bio 不走 D-01 隨機系統。

## 7. 可調參數（Tuning Knobs）

### 7.1 NamePool.csv（內容可調）

| 可調項目 | 預設值（Jam） | 安全範圍 | 影響 |
|---------|-------------|---------|------|
| 每 raceID 的名字筆數 | 6 筆 | 4 ~ 不限 | 低於 4 時，候選池 4 人可能出現重名（FT-01 不去重，重名屬設計接受範圍） |
| gender 分布 | 各種族自由設定 | 無強制比例 | 影響 bio 中 {pronoun} 的出現頻率；若想讓代名詞均衡，建議 0/1 各半 |

---

### 7.2 BioPool.csv（內容可調）

| 可調項目 | 預設值（Jam） | 安全範圍 | 影響 |
|---------|-------------|---------|------|
| 每 raceID×professionID 組合的模板數 | 1 筆 | 1 ~ 不限 | 多筆時隨機抽取，增加重複招募同職業/種族冒險者時的文字多樣性 |
| Fallback 模板（raceID=0 或 professionID=0） | 可選，Jam 版非必填 | — | 缺少時若某組合無精確模板，bio 回傳空字串 |

---

### 7.3 AdventurerTemplate.csv 擴充（跨系統）

具名 NPC 可選擇性新增 `bio` 欄位（靜態文字）。Jam 版可留空，P-02 以佔位文字（如「——」）代替。新增 bio 不需改程式碼。

## 8. 驗收標準（Acceptance Criteria）

| ID | 驗收條件 |
|----|---------|
| AC-D01-01 | DataManager 初始化後，`PickRandomNameWithGender(1)` 回傳非空的 name 字串與合法 gender（0/1/2） |
| AC-D01-02 | `PickRandomNameWithGender` 傳入 raceID=99（不存在）時，回傳 raceID=1（人類）的名字，且出現 `LogWarning` |
| AC-D01-03 | `PickRandomNameWithGender` 傳入 raceID=0 時，回傳 raceID=1 的名字（0 為 null sentinel，不應作為查詢條件） |
| AC-D01-04 | `PickRandomName(raceID)` 回傳值與 `PickRandomNameWithGender(raceID).name` 語意一致（wrapper 正確轉接） |
| AC-D01-05 | `GetRandomBio(2, 3, "利恩", 1)` 回傳非空字串（raceID=2、professionID=3 有對應模板時） |
| AC-D01-06 | bio 回傳字串中 `{name}` 已被替換為傳入的 name 參數，`{pronoun}` 已被替換為對應代名詞（gender=1 → 她） |
| AC-D01-07 | 傳入無對應精確模板的組合（如 raceID=99、professionID=99）時，依 Fallback Chain 嘗試通用模板；全部查無則回傳 `""` 並出現 `LogWarning` |
| AC-D01-08 | `template` 含未知變數 `{unknown}` 時，該變數保留原文，其他已知變數正常替換，不拋例外 |
| AC-D01-09 | C-02 `CreateRandomInstance` 建立的 `AdventurerInstance`，`gender` 與 `bio` 欄位均非預設空值（確認 D-01 API 已被正確呼叫） |
| AC-D01-10 | FT-10 存檔再載入後，`AdventurerInstance.bio` 與 `gender` 還原值與儲存前一致 |

---

## 9. 變更歷史

| 日期 | 版本 | 變更摘要 |
|------|------|---------|
| 2026-05-02 | v0.1 | 骨架建立 |
| 2026-05-02 | v1.0 | 8 節全寫完成；單名設計、BioPool 種族×職業分組、bio 一次性生成存入 C-02、C-02 cross-system patch（gender / bio 欄位）確立 |
