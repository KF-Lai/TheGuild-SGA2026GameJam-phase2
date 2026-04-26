# 【C-05-DS】TraitTable

特質定義表：每筆對應一個冒險者個性特質，持有效果類型（stat / behavior / condition）、效果目標識別符與數值參數。

## 基本資訊

- **檔案路徑**：`TheGuild-unity/Assets/Resources/Data/Tables/TraitTable.csv`
- **解析方式**：`CsvParser.Parse`（column-based）
- **註冊位置**：C-05 TraitSystem（DataManager.GetAll<TraitData>()，GDD §6.1）
- **資料類別**：`TheGuild.Gameplay.Trait.TraitData`
- **讀取 API**：
  - `DataManager.Get<TraitData>(traitID)` — 單筆
  - `DataManager.GetAll<TraitData>()` — 全列表
  - C-05 公開封裝：`GetTrait(int traitID)`、`GetAllTraits()`、`GetTraitsByType(string effectType)`（GDD §3.5）
- **消費者**：
  - C-02 Adventurer Management：`BuildTraitList` 將 fixedTraitIDs 全部加入（間接消費）（GDD §6.2）
  - FT-02 Mission Dispatch：`GetTraitsByType("stat")` 套用成功率與死亡率修正（GDD §4.2 / §6.2）
  - FT-03 NPC Decision：`GetTraitsByType("behavior")` 套用 willingness 修正（GDD §4.3 / §6.2）
  - FT-04 Outcome Resolution：`GetTraitsByType("condition")` 執行結算觸發效果（GDD §4.4 / §6.2）
  - P-02 Main UI：顯示冒險者特質名稱與描述（GDD §6.2）

## 欄位定義

| 欄位 | 型別 | 必填 | 範圍 | 說明 |
|---|---|---|---|---|
| `traitID` | `int` (PK) | ✓ | ≥ 1 | 唯一識別符；`0` 為 null sentinel |
| `name` | `string` | ✓ | — | 特質顯示名稱 |
| `description` | `string` | ✓ | — | UI 顯示描述 |
| `effectType` | `string` | ✓ | `stat` / `behavior` / `condition` | 效果分類；不在此集合時 `Debug.LogError`，跳過（GDD §5.1） |
| `effectTarget` | `string` | ✓ | 見合法清單（GDD §3.2） | 效果目標識別符；不在合法清單時 `Debug.LogError`，跳過（GDD §5.1） |
| `effectValue` | `float` | ✓ | 依 effectType/effectTarget 而定 | 數值參數；詳見下方說明 |

> **effectValue 範圍依 effectType / effectTarget（GDD §3.2 / §7.1）**：
> - `stat` 類（成功率/死亡率 delta）：安全範圍 `-0.15 ~ +0.15`
> - `behavior` 類（willingness delta）：安全範圍 `-0.30 ~ +0.20`
> - `condition` 觸發機率型（`on_success_gold_bonus` / `on_death_survive` / `on_fail_survive`）：`[0, 1]`；安全範圍 `0.05 ~ 0.30`
> - `condition` 固定值型（`on_fail_reputation` 負數 / `on_success_reputation_bonus` 正數）：安全範圍 `-5 ~ +5`

> **effectTarget 完整合法清單（GDD §3.2）**：
> - stat 類：`success_all` / `success_1` / `success_2` / `success_3` / `success_4` / `death_all` / `death_1` / `death_2` / `death_3` / `death_4`
> - behavior 類：`willingness_all` / `willingness_type_1` / `willingness_type_2` / `willingness_type_3` / `willingness_type_4` / `willingness_diff_S` / `willingness_diff_A` / `willingness_diff_low`
> - condition 類：`on_success_gold_bonus` / `on_fail_reputation` / `on_death_survive` / `on_fail_survive` / `on_success_reputation_bonus`

## 約束 / 不變量

- `effectType` 必須在 {`stat`, `behavior`, `condition`}；違規時 `Debug.LogError`，跳過（GDD §5.1）
- `effectTarget` 必須在 GDD §3.2 合法清單內；違規時 `Debug.LogError`，跳過（GDD §5.1）
- `stat` 類疊加規則：多個 stat 特質採**加法疊加**（線性累加所有 delta 後一次 clamp），不乘法、不優先順序（GDD §4.2）
- `condition` 類執行順序：按 `traitIDs` 列表順序依次執行；`on_death_survive` 觸發後 `isDead=false`，後者條件自然不成立（GDD §4.4）
- 同一 `traitID` 重複：後者覆蓋，`Debug.LogWarning`（GDD §5.1）

## Cross-ref

無

## 變更注意事項

- 修改後需 domain reload 才生效
- 新增特質：`effectTarget` 必須使用 GDD §3.2 合法清單；若需新 `effectTarget`，需同步更新 FT-02 / FT-03 / FT-04 套用邏輯（Medium 工項，GDD §7.3）
- 不得移除已存在 `traitID`（AdventurerInstance.traitIDs 存檔引用）

## 範例

```csv
# === TraitTable 特質定義表 ===
# effectTarget 必須使用 GDD §3.2 合法清單；condition 機率型範圍 [0,1]，建議 0.05~0.30

traitID,1,2,3,4,5
name,勇猛,膽小,幸運兒,倒楣鬼,貪財
description,戰鬥表現超出預期,不敢接高難度任務,總在最後關頭化險為夷,失敗時往往有額外損失,成功後偶爾帶回意外收穫
effectType,stat,behavior,condition,condition,condition
effectTarget,success_1,willingness_diff_S,on_death_survive,on_fail_reputation,on_success_gold_bonus
effectValue,0.08,-0.25,0.20,-2.0,0.15
```
（effectValue 對齊 GDD §7.1 安全範圍；traitIDs 供 TraitGroupTable 引用）
