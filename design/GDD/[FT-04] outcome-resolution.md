# Outcome Resolution 系統設計文件

_建立時間：2026-04-22_
_狀態：骨架（Skeleton）_
_系統 ID：FT-04_

---

## 設計決策備忘（Design Decisions Memo）

> 本節記錄骨架建立前已與設計者對齊的關鍵決策，供後續逐節撰寫時參照。撰寫完成後可移除。

1. **職責切分**（Q1）：FT-04 只決定「結算結果」並發布事件；FT-05 訂閱事件處理金流（傭金/賠償/預收對帳）；聲望由 FT-04 直接呼叫 `F-03.AddReputation`
2. **擲骰模型**（Q2 / 融合後）：維持兩次獨立擲骰——`successRoll` vs `finalSuccessRate`、`deathRoll` vs `adjustedDeathRate`
3. **冒險者結果映射**（Q2 / 融合後）：
   - 成功 + 存活 → 生存（Idle）
   - 成功 + 死亡 → 死亡（Dead）
   - 失敗 + 存活 → 重傷（Wounded，休息 6h）
   - 失敗 + 死亡 → 死亡（Dead）
4. **condition 特質套用時機**（Q3）：擲骰後 → 套用 condition 特質 → 決定最終結果
5. **金流計算職責**（Q4 / Q5）：全部由 FT-05 處理（含傭金、賠償、會計修正、condition gold bonus）
6. **on_death_survive 在「成功+死亡」時的結果**（Q7）：救活後走「成功+存活」路徑 → 生存（Idle）
7. **成功時死亡率打折**（Q8）：`adjustedDeathRate = finalDeathRate × DEATH_RATE_ON_SUCCESS_MULTIPLIER(=0.5)`，僅在 `missionResult == 成功` 時套用；失敗時使用原 `finalDeathRate`
8. **C-02 Wounded 語意**：沿用現行定義（「任務失敗但生還」），不需修改 C-02

---

## 1. 概要（Overview）

[待設計]

---

## 2. 玩家幻想（Player Fantasy）

[待設計]

---

## 3. 詳細規則（Detailed Rules）

[待設計]

---

## 4. 公式（Formulas）

[待設計]

---

## 5. 邊緣案例（Edge Cases）

[待設計]

---

## 6. 依賴關係（Dependencies）

[待設計]

---

## 7. 可調參數（Tuning Knobs）

[待設計]

---

## 8. 驗收標準（Acceptance Criteria）

[待設計]
