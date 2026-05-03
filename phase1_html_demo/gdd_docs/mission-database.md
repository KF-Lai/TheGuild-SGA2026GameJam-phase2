# Mission Database — 任務資料庫

> **文件狀態**：已設計
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **Jam 版範圍**：MVP

## 概覽

**Mission Database（任務資料庫）** 是遊戲所有任務的靜態資料來源與生成規則。它定義任務的類型、難度、時長、報酬，以及任務在任務池中出現的條件。

任務分為兩種生成方式：**靜態任務**（手工設計，固定內容）與**模板任務**（程序生成，從模板參數化產出）。任務資料庫本身不追蹤任何執行中狀態——它只是資料。

## 玩家幻想

任務資料庫服務的玩家幻想是：**每次開啟委託板都有新鮮感**。靜態任務帶來有意義的故事性委託（「為城主護送緊急信件」），模板任務確保任務池永遠不空（「討伐山林中的哥布林群」）。難度分佈讓玩家在早期看到可接受的任務，在後期接觸到報酬豐厚但風險極高的委託。

## 詳細設計

### 任務生成類型

| 類型 | 說明 | 適用場景 |
|------|------|---------|
| `static` | 手工設計，有固定 id、名稱、描述 | 重要劇情委託、特殊事件 |
| `template` | 從模板參數化生成，名稱由詞組合成 | 日常委託，補充任務池 |

### 資料結構

**共用欄位（Static + Template 都有）**

```typescript
interface MissionBase {
  id:              string;          // 唯一識別碼
  generationType:  'static' | 'template';
  missionType:     MissionType;     // 討伐 | 護送 | 採集 | 調查
  difficulty:      Difficulty;      // F | E | D | C | B | A | S | SS | SSS
  minimumRank:     AdventurerRank;  // 最低建議冒險者等級（advisory only）
  recommendedRank: AdventurerRank;  // 推薦冒險者等級（advisory only）
  actualDuration:  number;          // 實際時長（分鐘，生成時決定）
  estimatedDuration: number;        // 顯示時長（分鐘，取整至 30 分鐘倍數）
  baseReward:      number;          // 基礎報酬（金幣）
  styleTag:        StyleTag;        // 'light' | 'mixed' | 'dark'
}
```

**靜態任務額外欄位**

```typescript
interface StaticMission extends MissionBase {
  name:           string;   // 任務名稱
  description:    string;   // 任務描述
  staticSubtype:  string;   // 子分類標籤
  worldEventTrigger?: string; // 觸發條件（可選）
}
```

**模板任務額外欄位**

```typescript
interface TemplateMission extends MissionBase {
  difficultyRange: [Difficulty, Difficulty]; // 可生成的難度範圍
  namePattern:     string;                   // 名稱生成模板
}
```

### 最低等級（minimumRank）與推薦等級（recommendedRank）

> 這兩個欄位均為**建議性**，不限制玩家向 NPC 推薦任務。最終是否接單由 NPC 自主決定。

**minimumRank 對照表**（任務難度 → 冒險者最低建議階級）

| 任務難度 | F | E | D | C | B | A | S | SS | SSS |
|---------|---|---|---|---|---|---|---|----|-----|
| minimumRank | F | F | E | D | C | B | A | S | S |

**recommendedRank 對照表**（任務難度 → 推薦冒險者階級）

| 任務難度 | F | E | D | C | B | A | S | SS | SSS |
|---------|---|---|---|---|---|---|---|----|-----|
| recommendedRank | F | E | D | C | B | A | S | S | S |

### styleTag 規則

| 難度範圍 | styleTag |
|---------|---------|
| F ~ C | `'light'` |
| B ~ A | `'mixed'` |
| S ~ SSS | `'dark'` |

### 任務類型限制

| 任務類型 | 可用難度範圍 | 備註 |
|---------|-----------|------|
| 討伐 | F ~ SSS | 無限制 |
| 採集 | F ~ SSS | 無限制 |
| 調查 | F ~ SSS | 無限制 |
| 護送 | D ~ A | 過短無趣，過長設計矛盾 |

## 公式

### 實際時長（actualDuration）

```
actualDuration = BASE_DURATION[difficulty] × U(0.80, 1.20) × TYPE_MOD[missionType]
```

> `U(0.80, 1.20)` 為均勻分布隨機係數，`TYPE_MOD` 為任務類型時長修正。

**BASE_DURATION（分鐘）**

| 難度 | F | E | D | C | B | A | S | SS | SSS |
|-----|---|---|---|---|---|---|---|----|-----|
| 基礎時長（分鐘） | 20 | 45 | 150 | 270 | 480 | 840 | 1200 | 1800 | 3240 |

**TYPE_MOD 範圍**

| 任務類型 | TYPE_MOD 範圍 |
|---------|-------------|
| 調查 | ×1.00 ~ ×1.10 |
| 採集 | ×1.15 ~ ×1.30 |
| 討伐 | ×1.10 ~ ×1.30 |
| 護送 | ×3.00 ~ ×5.00 |

> **護送任務說明**：護送（×3~5）為刻意設計的「長途遠征」類型。SSS 護送理論上可達 13~14 天，屬於極端情況（見極端情況節）。

### 顯示時長（estimatedDuration）

```
estimatedDuration = ⌈actualDuration / 30⌉ × 30
```

> 上取整至最近的 30 分鐘倍數，用於 UI 顯示（不顯示精確值）。

**參考顯示值（討伐作為基準）**

| 難度 | 顯示估值（近似） |
|-----|--------------|
| F | 30 分鐘 |
| E | 60 分鐘 |
| D | 2.5 小時 |
| C | 5 小時 |
| B | 8 小時 |
| A | 14 小時 |
| S | 20 小時 |
| SS | 30 小時 |
| SSS | 48 ~ 72 小時（最大 ≈ 3.5 天） |

### 基礎報酬（baseReward）

```
baseReward = round(BASE_REWARD[difficulty] × U(0.85, 1.15))
```

**BASE_REWARD（金幣）**

| 難度 | F | E | D | C | B | A | S | SS | SSS |
|-----|---|---|---|---|---|---|---|----|-----|
| 基礎報酬 | 20 | 50 | 120 | 300 | 600 | 1000 | 2000 | 5000 | 10000 |

## 極端情況

**1. 護送任務在高難度時長超過 3.5 天**

護送 TYPE_MOD 為 ×3~5，不受 3.5 天上限約束。SSS 護送可達 13~14 天，屬於設計意圖的「史詩長征」，Jam 版不特別限制。

**2. actualDuration 計算後小於 30 分鐘**

F 難度最短：`20 × 0.80 × 1.00 = 16 分鐘`。`estimatedDuration` 會取整為 30 分鐘顯示，不影響實際計時。

**3. 靜態任務被完成後**

靜態任務完成後不再出現於任務池（由 `completedMissionIds` 追蹤）。

**4. 模板任務生成時難度範圍超出護送限制**

模板任務的難度生成需由生成邏輯保證護送類型只生成 D~A 難度，不需 MissionBase 額外驗證。

## 依賴關係

**上游依賴**：無（純靜態資料，不依賴其他系統）

**下游依賴**

| 系統 | 說明 |
|------|------|
| **Mission Dispatch（3）** | 讀取任務清單、`missionType`、`difficulty`、`actualDuration` |
| **Outcome Resolution（7）** | 讀取 `baseReward` 計算傭金 |
| **Adventurer Management（2）** | 讀取 `BASE_REWARD` 計算老手邀請費用 |

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| `BASE_DURATION` 各難度值 | `mission-database.ts` | 見上表 | ±20% | 調整遊戲節奏；影響玩家等待時間 |
| `TYPE_MOD` 範圍 | `mission-database.ts` | 見上表 | ±0.2 | 調整各類型任務相對時長 |
| `BASE_REWARD` 各難度值 | `mission-database.ts` | 見上表 | ±30% | 影響金幣通膨速度與玩家收益感 |
| 報酬隨機幅度 | `mission-database.ts` | ±15%（U(0.85, 1.15)） | ±5% ~ ±25% | 控制報酬波動感 |
| 時長隨機幅度 | `mission-database.ts` | ±20%（U(0.80, 1.20)） | ±10% ~ ±30% | 控制任務時長的不確定性 |

## 驗收標準

- [ ] `estimatedDuration` 永遠是 30 的整數倍
- [ ] 護送任務難度只出現 D~A
- [ ] `minimumRank` 與 `recommendedRank` 依難度索引正確查表
- [ ] `baseReward` 計算後為整數，落在 `BASE_REWARD[difficulty] × [0.85, 1.15]` 範圍內
- [ ] F 難度任務顯示時長為 30 分鐘
- [ ] SSS 討伐任務顯示時長落在 48~72 小時範圍內

## 待解問題

- guild-hall-scene.md 引入了委託「待審核（pending）/ 已核可（approved）」的兩階段狀態概念。`MissionBase` 目前無 `status` 欄位，未來需評估是否在任務資料庫層或 Mission Dispatch 層追蹤此狀態，並對齊資料結構。
  > ⚠️ 參見 guild-hall-scene.md §委託流程
