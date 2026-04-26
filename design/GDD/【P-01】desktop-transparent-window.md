# [P-01] Desktop Transparent Window

_建立時間：2026-04-26_
_狀態：設計中_

---

## §1 概覽（Overview）

P-01 Desktop Transparent Window 是「桌面原生」支柱的技術基礎。透過 Win32 API（C# P/Invoke）將 Unity 應用程式視窗設定為透明背景、常駐前置（Always on Top），並實作細粒度點擊穿透邏輯：背景區域點擊穿透至底層視窗，UI 元素區域正常接收滑鼠輸入。玩家在工作時可透過透明視窗看到公會在桌面角落運作，不打斷工作流程。Jam 版完整實作，技術路線採純 Windows API，不引入第三方插件依賴。

---

## §2 玩家幻想（Player Fantasy）

玩家不需要切換至遊戲視窗——公會的透明視窗常駐於桌面前景，工作或上課時只需「瞄一眼」即可掌握公會狀態。閒置冒險者在透明視窗中走動，任務計時器靜靜運轉，讓玩家感受到「公會活在自己的桌面上」。這個瞬間的存在感，是「桌面原生」支柱的核心情感。

---

## §3 詳細規則（Detailed Rules）

[待設計]

---

## §4 公式（Formulas）

[待設計]

---

## §5 邊界情況（Edge Cases）

[待設計]

---

## §6 依賴關係（Dependencies）

[待設計]

---

## §7 調校旋鈕（Tuning Knobs）

[待設計]

---

## §8 驗收標準（Acceptance Criteria）

[待設計]
