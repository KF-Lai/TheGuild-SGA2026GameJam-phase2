---
manifest: example-batch-YYYY-MM-DD
stop_on_fail: false
group_order: [time-system, data-layer, events]
# auto_group_from_folders:
#   - TheGuild-unity/Assets/Scripts/Core/
---

<!--
  本檔為範本，使用時：
  1. 複製到同資料夾，改名為 <YYYY-MM-DD>-<theme>.md（例：2026-04-25-core-opt.md）
  2. 刪掉此 HTML 註解區塊與下方「Schema 速查」+「使用模式」+「最小／完整／混合範例」三段 section
  3. 依需求保留／修改下方 ## 範例 group，或全部清掉重寫

  本範本僅用於 /auto-code-optimize-batch --manifest=<.md> 模式。
  Inline 多組（--auto= / --gdd= / --scripts=）由 skill 自動產生 inline manifest，不需要手寫此檔。
  完整對照見 .claude/docs/auto-code-optimize-usage.md §兩個 skill 的關係。
-->

# Schema 速查

## Frontmatter（檔頂 `---` 區塊內）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `manifest` | string | ✅ | — | 批次名稱；決定 `.progress/<name>.progress`、`.reports/<name>-*.md` 檔名 |
| `stop_on_fail` | bool | ✅ | — | `false`=某組失敗繼續下一組；`true`=遇失敗整批終止 |
| `group_order` | list[string] | ✅ | — | 執行順序；每個名稱必須對應下方一個 `## <name>` section |
| `auto_group_from_folders` | list[path] | 可選 | — | 掃這些資料夾的**直接子資料夾**自動加組（名稱=子資料夾名）；追加到 `group_order` 尾端 |

## Group section（每個 `## <name>` 之下）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `scripts` | list[path] | ✅ | — | 可混 `.cs` 單檔與**資料夾路徑**；資料夾**非遞迴**展開該層 `*.cs`，自動排除 `AssemblyInfo.cs` |
| `spec` | path | 可選 | 自動生成 | `.md` 檔（GDD 或自寫 spec）；省略時 `auto-code-optimize` 依 11 條原則自動生成 internal spec |
| `notes` | string | 可選 | — | Phase 1 addendum 文字（該組給 Codex 的聚焦提示）|
| `skip` | bool | 可選 | `false` | `true`=臨時跳過此組，不必改 `group_order` |

## 路徑與容量規則

- 所有路徑相對 repo root（如 `TheGuild-unity/Assets/Scripts/...`）
- 路徑分隔符用 `/`（跨平台）
- 每組建議 **2–4 個相關 scripts**（共享系統邊界）；超過會印警告但仍執行

---

# 使用模式

## 模式 A：純手寫 group（最常見）

frontmatter 只填 `group_order`，每組手寫 `## <name>` section。下方「範例 1/2/3」即此模式。

## 模式 B：純 auto-discover

frontmatter 開啟 `auto_group_from_folders`，**不寫** `group_order`（或留空 `[]`），**不寫**任何 `## <name>` section。skill 自動掃子資料夾、每個生一組、用子資料夾名當 group name。

```yaml
---
manifest: core-auto-2026-04-25
stop_on_fail: false
group_order: []
auto_group_from_folders:
  - TheGuild-unity/Assets/Scripts/Core/
---
```

## 模式 C：混合

`group_order` 列手寫組，`auto_group_from_folders` 額外加 auto 組（auto 組追加到尾端）。同名衝突時手寫優先。

---

# 最小範例（只填必要欄位）

## data-layer
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Data/

# 完整範例（每個可選欄位都示範）

## time-system
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Time/TimeSystem.cs
  - TheGuild-unity/Assets/Scripts/Core/Time/MissionTimer.cs
- spec: design/GDD/[FT-xx] time-system.md
- notes: 聚焦於 tick 派發效能與 OnMinuteTick 的冪等性
- skip: false

# 混合範例（資料夾 + 單檔）

## events
- scripts:
  - TheGuild-unity/Assets/Scripts/Core/Events/                                  # 資料夾：展開該層 *.cs
  - TheGuild-unity/Assets/Scripts/Gameplay/Resources/Events/ResourceEvents.cs   # 單檔：直接加入
- notes: 對齊 EventBus subscribe idempotency 規範
