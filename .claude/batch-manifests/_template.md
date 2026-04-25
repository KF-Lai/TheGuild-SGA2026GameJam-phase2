---
manifest: example-batch-YYYY-MM-DD
stop_on_fail: false
group_order: [time-system, data-layer, events]
# auto_group_from_folders:   # 可選。有此欄位時，skill 會掃描指定資料夾的直接子資料夾，
#   - TheGuild-unity/Assets/Scripts/Core/   # 每個子資料夾自動生成一組（名稱=子資料夾名）。
# 若同時有 group_order 和 auto_group_from_folders，auto 組會追加到 group_order 尾端。
---

# 範本說明（使用時請刪除此 section）

- 檔名建議：`<YYYY-MM-DD>-<theme>.md`，例：`2026-04-25-core-opt.md`
- 複製此檔到同資料夾後改名，刪掉此「範本說明」section
- `group_order` 內每個名稱必須對應下方一個 `##` section（auto-generated 組不需要手寫 section）
- 每組建議 **2–4 個相關 scripts**（共享系統邊界）；超過會印警告但仍執行
- `scripts` 清單可混合 **單檔（.cs 結尾）** 與 **資料夾路徑**，資料夾會自動 Glob 展開該層的 `*.cs`（**非遞迴**）
- 資料夾展開會自動排除 `AssemblyInfo.cs`
- `spec` 欄省略時，`auto-code-optimize` 會依 11 條優化原則自動生成 internal spec
- `notes` 欄是給本組的聚焦提示（Phase 1 作為 addendum 讀入）
- 任一 group 設 `skip: true` 即可臨時跳過，不必改 `group_order`

---

## time-system
# 範例 1：手寫單檔清單
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs
  - TheGuild-unity/Assets/Scripts/Core/Time/MissionTimer.cs
- spec: design/GDD/[FT-xx]time-system.md
- notes: 聚焦於 tick 派發效能與 OnMinuteTick 的冪等性

## data-layer
# 範例 2：資料夾展開（非遞迴，自動排除 AssemblyInfo.cs）
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Data/
- spec: design/GDD/[F-01]data-manager.md
- notes: 解析錯誤處理與參數表格化

## events
# 範例 3：資料夾 + 單檔混合
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Events/       # 展開整個 Events 資料夾
  - TheGuild-unity/Assets/Scripts/Gameplay/Resources/Events/ResourceEvents.cs  # 追加一個外部事件檔
- skip: false
