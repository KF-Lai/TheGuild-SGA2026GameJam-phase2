# Adventurer Management — 隊員管理

> **文件狀態**：已設計
> **作者**：使用者 + Claude Code Game Studios
> **最後更新**：2026-04-06
> **支柱對應**：支柱 1 — 離線 / 長期經營感；支柱 3 — 人員生死與取捨
> **Jam 版範圍**：MVP

## 概覽

**Adventurer Management（隊員管理）** 管理公會冒險者的來源、個體資料與生命週期。

冒險者進入公會有兩種途徑：**新手自薦**與**老手邀請**。新手冒險者（F/E 階）會定期自行出現在公會，玩家決定是否接納；老手冒險者（D 階以上，階級隨機）需要玩家主動花費金幣邀請，一旦邀請即自動加入，無拒絕機會。

每位冒險者持有獨立的個體資料：階級、職業特質、名字、當前狀態（空閒 / 執行任務 / 死亡）。任務派遣、任務結算等系統透過此系統讀取冒險者資料並更新其狀態。冒險者死亡時從名冊中永久移除。

本系統不計算成功率或死亡率，僅負責「人事管理」：記錄誰在公會、他們的狀態如何、以及進出公會的規則。

## 玩家幻想

每半天刷新一批冒險者是公會最期待的時刻——「今天有沒有好苗子？」新手免費但弱，老手強但要錢：這個選擇本身就是一種張力。看到一個 A 階戰士出現，心裡盤算「現在金幣夠嗎？不請他，他會消失嗎？」，這種窗口感讓每次刷新都有重量。

15 人名冊是緊繃的資源——不是每個人都值得留，低階冒險者死了心疼但也釋放了名額。玩家漸漸形成自己的用人哲學：重視職業搭配？還是賭高階稀有冒險者？

## 詳細設計

### 資料結構

```typescript
type AdventurerRank   = 'F' | 'E' | 'D' | 'C' | 'B' | 'A' | 'S';
type AdventurerStatus = 'idle' | 'on_mission' | 'dead';

interface Adventurer {
  id:               string;            // 唯一識別碼（UUID）
  name:             string;            // 顯示名稱（隨機生成）
  rank:             AdventurerRank;    // 冒險者階級
  traitId:          string;            // 職業特質 ID（來自 Adventurer Trait System）
  status:           AdventurerStatus;  // 當前狀態
  currentMissionId: string | null;     // 執行中任務 ID（idle 時為 null）
}
```

### 刷新邏輯（Candidate Pool）

每 **12 小時**產生一批候選冒險者（Candidate Pool），顯示於公會「招募欄」。

**批次規模**：固定 4 位

**新手 / 老手比率**：各批次中新手佔比 40%~60%（隨機，均值 50%）

| 類型 | 階級範圍 | 進入條件 | 費用 |
|------|---------|---------|------|
| 新手 | F 或 E（各 50%） | 玩家選擇接納或拒絕 | 免費 |
| 老手 | D~S（加權，見下） | 玩家花費金幣邀請 | 依階級計費 |

**老手階級加權分布**

| 階級 | 權重 |
|------|------|
| D | 40% |
| C | 30% |
| B | 18% |
| A | 9% |
| S | 3% |

**老手邀請費用**

| 階級 | 費用範圍 | 中位值 |
|------|---------|--------|
| D | 50 ~ 70 | 60 |
| C | 130 ~ 170 | 150 |
| B | 250 ~ 350 | 300 |
| A | 420 ~ 580 | 500 |
| S | 850 ~ 1150 | 1000 |

**候選池有效期**：下次刷新時自動清除，未接納 / 未邀請的候選消失。

### 狀態機

```
              接受（新手）/ 邀請成功（老手）
[候選池] ──────────────────────────────→ [idle]
                                           │
                              Mission Dispatch 派遣
                                           ↓
                                      [on_mission]
                                     /           \
                         Outcome Resolution       Outcome Resolution
                              成功                    失敗（存活）
                               ↓                        ↓
                            [idle]                   [idle]
                                         \
                                  Outcome Resolution
                                       失敗（死亡）
                                          ↓
                                       [dead] → 從名冊移除
```

### 名冊容量

- Jam 版固定上限：**15 名**
- 超過上限時，新手接納與老手邀請均被拒絕（UI 提示名冊已滿）
- 冒險者死亡後立即釋放名額

### 與其他系統的互動

| 系統 | 方向 | 操作 |
|------|------|------|
| **Adventurer Trait System（14）** | 讀取 | 生成冒險者時，從職業清單隨機指派 `traitId` |
| **Resource Management（4）** | 讀取 + 寫入 | 邀請老手時確認金幣餘額，並扣除費用 |
| **Mission Dispatch（3）** | 讀取 + 寫入 | 讀取 idle 冒險者清單；派遣時將狀態改為 `on_mission` |
| **Outcome Resolution（7）** | 寫入 | 任務結算後將冒險者改回 `idle` 或移除（死亡） |
| **Save / Load（10）** | 讀取 + 寫入 | 序列化完整名冊（`Adventurer[]`）與候選池 |

## 公式

### 冒險者生成

```
traitId           = random pick from PROFESSION_TRAITS（7 種，各 1/7）
name              = random pick from NAME_POOL（預定義名字庫）
status            = 'idle'
currentMissionId  = null
```

**新手階級**
```
rank = random( ['F', 'E'] )  // 各 50%
```

**老手階級**（加權隨機）
```
rank = weightedRandom({ D: 0.40, C: 0.30, B: 0.18, A: 0.09, S: 0.03 })
```

### 老手邀請費用

```
invitationCost = round( BASE_REWARD[rank] × 0.5 × U(0.85, 1.15) )
```

### 候選池刷新

```
batchSize    = 4
noviceRatio  = U(0.40, 0.60)
noviceCount  = round( batchSize × noviceRatio )
veteranCount = batchSize − noviceCount
```

## 極端情況

**1. 名冊已滿時有候選者出現**

候選池正常刷新，但接納 / 邀請操作被拒絕，提示「名冊已滿（15/15）」。

**2. 金幣不足以邀請老手**

邀請按鈕灰化，提示「金幣不足」。候選者留在池中直到刷新。

**3. 候選池刷新時舊批次未被處理**

直接清空。未接納的新手與未邀請的老手消失，不保留、不補償（設計意圖：製造決策壓力）。

**4. `noviceCount` 計算後為 0 或等於 `batchSize`**

屬於正常隨機結果，Jam 版不做保底處理。

**5. 冒險者死亡後名額釋放時機**

死亡在 Outcome Resolution 結算時立即觸發，同一 tick 從名冊移除，名額即時釋放。

**6. S 階老手出現時玩家無力邀請**

費用 850~1150 金幣，早期幾乎無法負擔。候選消失屬正常遊戲壓力，不提供折扣或保留機制。

**7. 名字庫耗盡**

允許重名（Jam 版），由 `id`（UUID）作為唯一識別。

## 依賴關係

**上游依賴**

| 系統 | 依賴性質 | 說明 |
|------|---------|------|
| **Resource Management（4）** | 強依賴 | 邀請老手時讀取金幣餘額、扣除費用 |
| **Adventurer Trait System（14）** | 強依賴 | 生成冒險者時隨機指派職業 `traitId` |

**下游依賴**

| 系統 | 依賴性質 | 說明 |
|------|---------|------|
| **Mission Dispatch（3）** | 強依賴 | 讀取 idle 冒險者；派遣後寫入 `status` |
| **Outcome Resolution（7）** | 強依賴 | 結算後更新冒險者狀態或移除 |
| **Save / Load（10）** | 強依賴 | 序列化名冊與候選池 |
| **Guild Hall Scene（11）** | UI 入口 | 提供招募與開除的玩家操作介面（點擊公會長人物觸發）。 > ⚠️ 參見 guild-hall-scene.md §公會長管理面板 |

## 調整旋鈕

| 旋鈕 | 位置 | 目前值 | 安全範圍 | 說明 |
|------|------|--------|---------|------|
| 名冊上限 | `adventurer-management.ts` | 15 | 10 ~ 30 | Jam 版固定 |
| 候選批次大小 | `adventurer-management.ts` | 4 | 2 ~ 6 | 調高加快流動；調低增加稀缺感 |
| 刷新間隔 | `adventurer-management.ts` | 43200 秒（12 小時） | 14400 ~ 86400 秒 | — |
| 新手佔比範圍 | `adventurer-management.ts` | 40% ~ 60% | 20% ~ 80% | — |
| 老手階級加權 | `adventurer-management.ts` | D:40/C:30/B:18/A:9/S:3 | 各項 ±15% | — |
| 邀請費用係數 | `adventurer-management.ts` | 50%（BASE_REWARD × 0.5）| 30% ~ 80% | — |
| 邀請費用隨機幅度 | `adventurer-management.ts` | ±15%（U(0.85, 1.15)）| ±5% ~ ±25% | — |

## 驗收標準

**功能驗收**

- [ ] 冒險者生成時，`traitId` 必為 7 種職業之一（均等分布）
- [ ] 冒險者生成時，`status = idle`，`currentMissionId = null`
- [ ] 新手候選階級僅出現 F 或 E
- [ ] 老手候選階級僅出現 D ~ S，D 最多，S 最少
- [ ] 邀請老手時，費用落在對應階級的費用範圍內
- [ ] 名冊達 15 人時，接納與邀請均被拒絕
- [ ] 候選池刷新後，舊批次完全清空

**公式驗收**

- [ ] `invitationCost` 為整數，落在 `BASE_REWARD[rank] × 0.5 × [0.85, 1.15]` 範圍內
- [ ] `noviceCount + veteranCount = batchSize`
- [ ] `noviceRatio` 每批次落在 40% ~ 60% 之間

**狀態機驗收**

- [ ] 派遣後冒險者 `status = on_mission`，`currentMissionId` 非 null
- [ ] 結算存活後，`status = idle`，`currentMissionId = null`
- [ ] 結算死亡後，冒險者從名冊移除，名額即時釋放

**整合驗收**

- [ ] 邀請老手後，`gold` 正確扣減
- [ ] 金幣不足時邀請不觸發扣款
- [ ] Save / Load 序列化後還原，名冊與候選池完全一致

## 待解問題

[無]
