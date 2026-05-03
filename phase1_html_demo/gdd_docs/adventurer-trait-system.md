# Adventurer Trait System — 冒險者特質（職業）

> **文件狀態**：已設計
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **Jam 版範圍**：MVP（職業特質 / 2 類），1 類與 3 類不實作

## 概覽

**Adventurer Trait System（冒險者特質系統）** 定義冒險者的職業身份，並提供職業對任務成功率與死亡率的修正值。

特質分為三類：

- **1 類（種族特質）**：Jam 版可選
- **2 類（職業特質）**：Jam 版 MVP，每位冒險者擁有一個職業，影響任務結果
- **3 類（動態狀態特質）**：Jam 版可選

本系統是純粹的資料層，不追蹤任何執行中狀態。

## 玩家幻想

當玩家看到公會名冊時，他們不只看到「等級 B 冒險者」——他們看到「等級 B 的盾衛」。職業讓每位冒險者有個性：戰士擅長硬碰硬，遊俠在荒野中如魚得水，斥侯能同時勝任護送與調查任務。選擇派誰去哪個任務，本身就是遊戲中的決策樂趣。

## 詳細設計

### 資料結構

```typescript
type MissionType = '討伐' | '護送' | '採集' | '調查';

interface ProfessionTrait {
  id:                   string;
  name:                 string;
  successRateModifier:  Record<MissionType, number>;  // 成功率加成（百分比）
  deathRateModifier:    Record<MissionType, number>;  // 死亡率修正（百分比）
}
```

> 兩個修正值均為**加法修正**（直接加到基礎值上），非乘法。
> Clamp 由 Mission Dispatch 與 Outcome Resolution 的下游公式處理。

### 職業清單（7 種）


| #   | id          | 職業名稱 | 定位             |
| --- | ----------- | ---- | -------------- |
| 1   | `warrior`   | 戰士   | 討伐專家，前線主力      |
| 2   | `mage`      | 法師   | 調查專家，體能偏弱      |
| 3   | `ranger`    | 遊俠   | 採集與探索，野外生存     |
| 4   | `scout`     | 斥侯   | 護送與調查雙擅，死亡風險較高 |
| 5   | `guardian`  | 盾衛   | 護送與防禦，全面保護     |
| 6   | `healer`    | 治癒師  | 護送支援，降低死亡風險    |
| 7   | `mercenary` | 傭兵   | 全任務中性，無特長無弱點   |


**職業分配規則**：冒險者生成時從 7 種職業中均等隨機（各 1/7），Jam 版固定，不可更換。

### 成功率修正表（`successRateModifier`）

單位：百分比（+20 = +20%）


| 職業  | 討伐  | 護送  | 採集  | 調查  |
| --- | --- | --- | --- | --- |
| 戰士  | +20 | 0   | 0   | -15 |
| 法師  | 0   | 0   | -15 | +20 |
| 遊俠  | 0   | -15 | +20 | 0   |
| 斥侯  | -15 | +20 | 0   | +20 |
| 盾衛  | 0   | +20 | 0   | -15 |
| 治癒師 | -15 | +20 | 0   | 0   |
| 傭兵  | 0   | 0   | 0   | 0   |


### 死亡率修正表（`deathRateModifier`）

單位：百分比（-15 = -15%，降低死亡率）


| 職業  | 討伐  | 護送  | 採集  | 調查  |
| --- | --- | --- | --- | --- |
| 戰士  | -15 | -5  | -5  | 0   |
| 法師  | +10 | 0   | 0   | -10 |
| 遊俠  | -5  | 0   | -15 | -5  |
| 斥侯  | +5  | -10 | -5  | -10 |
| 盾衛  | -10 | -20 | -5  | -5  |
| 治癒師 | +5  | -15 | -5  | -5  |
| 傭兵  | -5  | -5  | 0   | 0   |


### 特質效果類型

**Mixed（C 類）**：每個職業對其擅長任務類型有 **+20% 成功率加成**，對弱點任務有 **-15% 成功率懲罰**，中性任務為 **0%**。傭兵全部為 0（無特長無弱點）。

## 公式

特質修正值為加法修正，直接加入下游公式：

```typescript
// Mission Dispatch 使用
finalSuccessRate = clamp(
  BASE_SUCCESS_RATE[rankDiff] + trait.successRateModifier[missionType],
  0, 1
);

// Outcome Resolution 使用
finalDeathRate = clamp(
  BASE_DEATH_RATE[difficulty]
  + trait.deathRateModifier[missionType]
  + RANK_DIFF_DEATH_MOD[rankDiff],
  0, 1
);
```

## 極端情況

**1. 斥侯同時擁有護送與調查 +20%**

斥侯是唯一有兩個 +20% 成功率加成的職業，但也有討伐 -15% 懲罰。設計意圖：斥侯是「支援型全才」，在正確任務類型下非常有效，但不適合正面戰鬥。

**2. 法師執行討伐任務**

`successRateModifier = 0`（不懲罰成功率），但 `deathRateModifier = +10%`（更容易陣亡）。

**3. 傭兵所有修正值為 0**

傭兵是「安全牌」——在任何任務下表現都是純靠等級差，沒有加分也沒有扣分。

**4. clamp 後成功率或死亡率超出合理範圍**

本系統不做 clamp，由下游（Mission Dispatch、Outcome Resolution）負責 clamp 至 [0, 1]。

## 依賴關係

**上游依賴**：無（純靜態資料）

**下游依賴**


| 系統                           | 說明                                   |
| ---------------------------- | ------------------------------------ |
| **Adventurer Management（2）** | 生成冒險者時隨機指派 `traitId`                 |
| **Mission Dispatch（3）**      | 讀取 `successRateModifier` 計算 NPC 接單意願 |
| **Outcome Resolution（7）**    | 讀取 `deathRateModifier` 計算死亡判定        |


## 調整旋鈕


| 旋鈕    | 位置                         | 目前值     | 安全範圍        | 說明                     |
| ----- | -------------------------- | ------- | ----------- | ---------------------- |
| 職業數量  | `profession-traits.ts`     | 7       | 5 ~ 10      | 增加職業豐富性但增加平衡工作量        |
| 擅長加成值 | `profession-traits.ts`     | +20%    | +10% ~ +30% | 調低讓職業差異更細微；調高讓專精更明顯    |
| 弱點懲罰值 | `profession-traits.ts`     | -15%    | -5% ~ -25%  | 調低讓弱點更可接受；調高強制玩家考慮職業搭配 |
| 生成分配比 | `adventurer-management.ts` | 均等（1/7） | 可加權         | Post-Jam 可讓某些職業更稀有     |


## 驗收標準

**功能驗收**

- 7 種職業均可被讀取，`id` 與 `name` 正確
- 所有職業的 `successRateModifier` 覆蓋全部 4 種任務類型
- 所有職業的 `deathRateModifier` 覆蓋全部 4 種任務類型
- 傭兵所有修正值均為 0
- 斥侯護送與調查 `successRateModifier` 均為 +20%

**整合驗收**

- 冒險者生成時，`traitId` 均等分布於 7 種職業
- Mission Dispatch 計算時正確讀取對應任務類型的修正值
- Outcome Resolution 計算時正確讀取死亡率修正

## 待解問題

[無]