# Guild Building（公會建設）



> **狀態**：設計中
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 3 — 決策有真實後果
> **Jam 版範圍**：次要，公會升級後可解鎖的建設項目

## 概覽

**Guild Building（公會建設）** 是玩家將金幣轉化為公會基礎設施的方式。在 Jam 版中，所有建設為**一次性解鎖**——花費金幣蓋好，功能永久啟用，不需維護費。

建設不會自動解鎖；每個建設有兩個前提條件：**公會等級**（Guild Core）與**金幣**。玩家到達條件後，主動到「公會管理」介面確認建設。

Jam 版規劃 5 個建設項目，分散在 Lv1~Lv4，覆蓋冒險者招募、任務容量、資金周轉三個面向。Post-Jam 可擴充為方案 C（加入被動加成建設）。

## 玩家幻想

那塊空著的牆壁，一直是個遺憾。

你知道如果有一塊「公告欄」，更好的委託就會找上門；如果有一間「訓練場」，那些總是差一點的冒險者也許就能撐過去。建設不是裝飾，是你把公會從「一間漏風的辦公室」變成「真正的公會」的過程。

每一棟建好的設施，都是公會歷史的一部分。

## 詳細設計

### 1. 建設清單


| #   | 建設名稱   | 解鎖效果                                  | 所需公會等級 | 費用   |
| --- | ------ | ------------------------------------- | ------ | ---- |
| 1   | 委託板    | 委託板顯示的委託數量上限 **+3**（6 → 9）            | Lv 1   | 200g |
| 2   | 招募板    | 每 3 小時週期內手動刷新次數 **+2**（5 → 7）         | Lv 1   | 150g |
| 3   | 預備金保險櫃 | 允許 `gold < 0` 時仍接受新委託（解除債務鎖定）         | Lv 2   | 400g |
| 4   | 審查處    | 解鎖「開除會籍」功能，可依 UUID 將冒險者永久移除           | Lv 2   | 250g |
| 5   | 公會櫃台小姐 | 名冊容量上限提升至 **20 人**（覆蓋 Guild Core 基礎值） | Lv 3   | 600g |


> 所有建設費用一次性扣除，完成後永久啟用，不可撤銷。

---

### 2. 建設狀態機

```
未解鎖（條件未達）
  → 可建設（guildLevel 達標 且 gold 足夠）
  → 建設中（僅動畫演出，不阻礙操作）
  → 已完成（效果永久啟用）
```

---

### 3. 各建設效果細節

**委託板**

- 建設前：Mission Dispatch 的 `任務池顯示數量` 預設 6
- 建設後：`任務池顯示數量` 提升為 **9**
- Mission Dispatch 讀取 `buildingUnlocks.noticeboard` 決定顯示上限

**招募板**

- 建設前：候選池手動刷新每 3 小時週期上限 5 次
- 建設後：上限提升為 **7 次**
- Adventurer Management 讀取 `buildingUnlocks.recruitBoard` 決定上限

**預備金保險櫃**

- 建設前：`gold < 0` 時委託板置灰，玩家無法推薦任何委託（客戶不信任債務中的公會）
- 建設後：解除此鎖定，允許在債務狀態下繼續接受委託
- Mission Dispatch 讀取 `buildingUnlocks.reserve` 決定是否啟用債務鎖定

**審查處**

- 建設前：冒險者一旦加入名冊就無法移除（只能等待任務死亡）
- 建設後：冒險者卡片顯示「開除會籍」按鈕，確認後依 `adventurerId`（UUID）永久移除
- 開除的冒險者不返回候選池，直接消失
- 任務中（`status = 'on_mission'`）的冒險者不可開除（按鈕置灰）

**公會櫃台小姐**

- 建設前：名冊上限由 `MAX_ROSTER_BY_GUILD_LEVEL[guildLevel]`（Lv5 最高 **15** 人）決定
- 建設後：硬性覆蓋為上限 **20 人**，無視 Guild Core 基礎值
- 「公會櫃台小姐」屬於 UI 上的人物設定，後期美術版可加入角色插圖

---

### 4. 資料結構

```typescript
interface BuildingUnlocks {
  noticeboard:  boolean   // 委託板
  recruitBoard: boolean   // 招募板
  reserve:      boolean   // 預備金保險櫃
  tribunal:     boolean   // 審查處
  receptionist: boolean   // 公會櫃台小姐
}
```

## 公式

### 建設可用性判斷

```typescript
function canBuild(
  id: keyof BuildingUnlocks,
  guildLevel: number,
  gold: number,
  unlocks: BuildingUnlocks
): boolean {
  if (unlocks[id]) return false                      // 已建設
  const req = BUILDING_REQUIREMENTS[id]
  return guildLevel >= req.minLevel && gold >= req.cost
}
```

---

### 建設需求常數

```typescript
const BUILDING_REQUIREMENTS: Record<keyof BuildingUnlocks, { minLevel: number; cost: number }> = {
  noticeboard:  { minLevel: 1, cost: 200 },
  recruitBoard: { minLevel: 1, cost: 150 },
  reserve:      { minLevel: 2, cost: 400 },
  tribunal:     { minLevel: 2, cost: 250 },
  receptionist: { minLevel: 3, cost: 600 },
}
```

---

### 委託顯示數量

```typescript
const COMMISSION_DISPLAY_LIMIT = (unlocks: BuildingUnlocks): number =>
  unlocks.noticeboard ? 9 : 6
```

---

### 名冊容量上限

```typescript
const EFFECTIVE_MAX_ROSTER = (guildLevel: number, unlocks: BuildingUnlocks): number =>
  unlocks.receptionist ? 20 : MAX_ROSTER_BY_GUILD_LEVEL[guildLevel]
```

---

## 極端情況


| #   | 情況                    | 說明                      | 處理方式                             |
| --- | --------------------- | ----------------------- | -------------------------------- |
| 1   | **建設時金幣剛好歸零**         | 費用正好等於當前金幣              | 允許建設，`gold = 0`；不觸發債務狀態          |
| 2   | **建設時金幣不足**           | `gold < cost`           | 按鈕置灰，顯示「金幣不足（差 Xg）」              |
| 3   | **已建設時再次觸發**          | 存檔損毀或 UI bug            | `unlocks[id] = true` 時直接忽略，不扣金幣  |
| 4   | **審查處：開除任務中的冒險者**     | `status = 'on_mission'` | 按鈕置灰，提示「任務進行中，無法開除」              |
| 5   | **審查處：名冊只剩 1 人**      | 開除後名冊空置                 | 允許開除至 0 人；委託板全部置灰（無可推薦者）         |
| 6   | **公會櫃台小姐：已滿 15 人後建設** | 建設前已達等級名冊上限     | 建設後立即開放至 20 人，不需重新開局             |
| 7   | **公會降回低等級**（不降級設計）    | Guild Core 等級只升不降       | 建設需求的 `minLevel` 僅用於解鎖，不會因等級變化失效 |


---

## 依賴關係


| 系統                       | 方向          | 類型  | 說明                                                                           |
| ------------------------ | ----------- | --- | ---------------------------------------------------------------------------- |
| Guild Core（1）            | 上游          | 強   | 讀取 `guildLevel` 作為建設解鎖前提                                                     |
| Resource Management（4）   | 上游讀取 + 下游寫入 | 強   | 讀取 `gold` 確認金幣；建設時執行 `gold -= cost`                                          |
| Mission Dispatch（3）      | 下游讀取        | 強   | 讀取 `noticeboard` 決定委託顯示上限；讀取 `reserve` 決定債務鎖定                                |
| Adventurer Management（2） | 下游讀取        | 強   | 讀取 `recruitBoard` 決定刷新次數上限；讀取 `receptionist` 決定名冊容量；讀取 `tribunal` 決定是否顯示開除按鈕 |
| Save / Load（10）          | 持久化         | 強   | `BuildingUnlocks` 納入存檔                                                       |


### 依賴方向

```
Guild Core (1) ────────────┐
Resource Management (4) ───┤
                           ▼
                    Guild Building (5) ──▶ Mission Dispatch (3)
                                      ──▶ Adventurer Management (2)
```

---

## 調整旋鈕


| 旋鈕                          | 位置                  | 目前值                 | 安全範圍      | 說明                  |
| --------------------------- | ------------------- | ------------------- | --------- | ------------------- |
| 各建設費用                       | `guild-building.ts` | 200/150/400/250/600 | ×0.5 ~ ×2 | 調低讓玩家更早建設；調高增加資金壓力  |
| 各建設等級需求                     | `guild-building.ts` | 1/1/2/2/3           | ±1        | 調整解鎖節奏，控制玩家何時取得各項能力 |
| `COMMISSION_DISPLAY_LIMIT`  | `guild-building.ts` | 6 / 9（建設後）          | 4 ~ 12    | 影響委託板資訊密度與選擇壓力      |
| `EFFECTIVE_MAX_ROSTER`（建設後） | `guild-building.ts` | 20                  | 15 ~ 30   | 建設後的名冊容量天花板         |
| 手動刷新次數（建設後）                 | `guild-building.ts` | 7（+2）               | 6 ~ 10    | 建設後的刷新次數上限          |


---

## 驗收標準

### 建設解鎖

- Lv1 公會、足夠金幣時，委託板與招募板可建設（按鈕可用）
- Lv1 公會時，預備金保險櫃與審查處按鈕置灰（等級不足）
- 金幣不足時，任何建設按鈕均置灰並顯示差額提示
- 建設成功後 `gold` 正確扣除，`buildingUnlocks[id]` 設為 `true`

### 各建設效果

- **委託板建設後**：Mission Dispatch 顯示上限從 6 增至 9
- **招募板建設後**：Adventurer Management 的手動刷新上限從 5 增至 7
- **預備金保險櫃建設前**：`gold < 0` 時委託板全部置灰，無法推薦
- **預備金保險櫃建設後**：`gold < 0` 時委託板正常顯示，可推薦
- **審查處建設後**：冒險者卡片出現「開除會籍」按鈕；任務中冒險者的按鈕置灰
- **審查處開除確認後**：冒險者從名冊消失，不出現在候選池
- **公會櫃台小姐建設後**：名冊容量上限提升為 20，不受 guildLevel 影響

### 存檔整合

- 建設狀態在重新載入後正確還原（`BuildingUnlocks` 讀取正確）
- 已建設項目在重新載入後效果立即生效

