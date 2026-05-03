# Systems Index — The Guild

> **文件狀態**：已設計
> **最後更新**：2026-04-07

## 系統清單

| #   | 系統名稱 | 類別 | Jam 優先級 | 設計狀態 | GDD 連結 | 上游依賴 |
|-----|---------|------|-----------|---------|---------|---------|
| 1   | Guild Core 公會核心 | 核心框架 | MVP | 未開始 | — | 4 |
| 2   | Adventurer Management 隊員管理 | 遊戲玩法 | MVP | 已設計 | [design/gdd/adventurer-management.md](design/gdd/adventurer-management.md) | 4、14 |
| 3   | Mission Dispatch 任務派遣 | 遊戲玩法 | MVP | 已設計 | [design/gdd/mission-dispatch.md](design/gdd/mission-dispatch.md) | 2、6、9、14 |
| 4   | Resource Management 資源管理 | 核心框架 | MVP | 已設計 | [design/gdd/resource-management.md](02_Project/SGA_2026_gamejam/TheGuild-SGA2026GameJam-phase2/design/GDD/resource-management.md) | 無 |
| 5   | Commission Flow 傭金流程 | 遊戲玩法 | MVP | 未開始 | — | 4、7 |
| 6   | Mission Database 任務資料庫 | 核心框架 | MVP | 已設計 | [design/gdd/mission-database.md](02_Project/SGA_2026_gamejam/TheGuild-SGA2026GameJam-phase2/design/GDD/mission-database.md) | 無 |
| 7   | Outcome Resolution 任務結算 | 遊戲玩法 | MVP | 未開始 | — | 2、3、4、6 |
| 8   | World Danger System 世界危險度 | 核心框架 | 次要 | 未開始 | — | 無 |
| 9   | Time / Tick System 時間系統 | 核心框架 | MVP | 未開始 | [design/gdd/time-tick.md](design/gdd/time-tick.md) | 無 |
| 10  | Save / Load 存檔讀檔 | 核心框架 | MVP | 未開始 | — | 全部 |
| 11  | Main UI Shell 主介面 | 介面 | MVP | Approved | [design/gdd/guild-hall-scene.md](design/gdd/guild-hall-scene.md) | 全部 |
| 12  | Guild Building System 公會建設 | 進階玩法 | 次要 | 未開始 | — | 1、4 |
| 13  | NPC Logic / AI NPC 邏輯 | 進階玩法 | 次要 | 未開始 | — | 2、3 |
| 14  | Adventurer Trait System 冒險者特質 | 遊戲玩法 | MVP（2類）/ 砍掉（1、3類） | 已設計 | [design/gdd/adventurer-trait-system.md](design/gdd/adventurer-trait-system.md) | 無 |
| 15  | Rank Progression 階級成長 | 進階玩法 | 次要 | 未開始 | — | 2、7 |
| 16  | Notification / Feedback 通知回饋 | 介面 | 次要 | 未開始 | — | 無 |

**已排除（不納入 Jam 版）**：迷宮任務系統、種族特質玩法、動態狀態特質

## 依賴層次（設計順序）

```
第 1 層（無依賴，先設計）：
  Resource Management（4）
  Mission Database（6）
  Adventurer Trait System（14）
  Time / Tick System（9）

第 2 層（依賴第 1 層）：
  Guild Core（1）← 依賴 4
  Adventurer Management（2）← 依賴 4、14
  World Danger System（8）

第 3 層（依賴第 1-2 層）：
  Mission Dispatch（3）← 依賴 2、6、9、14
  Outcome Resolution（7）← 依賴 2、3、4、6

第 4 層（依賴第 1-3 層）：
  Commission Flow（5）← 依賴 4、7
  Save / Load（10）← 依賴全部
  Main UI Shell（11）← 依賴全部
```

## 進度統計

| 狀態 | 數量 |
|------|------|
| 設計文件已完成 | 6 |
| 設計文件未開始 | 10 |
| 已排除 | 3 |
