# Prototype Report: Core Dispatch Loop

> **Date**: 2026-04-07
> **Status**: Built — awaiting playtest

---

## Hypothesis

派遣 → 等待 → 結算的核心循環，在 MUD 風格文字 UI 中，玩家對冒險者的安危和任務成敗會產生
情感投入，決策（選誰、選哪個任務）會感覺有意義。

---

## Approach

**建構內容**：
- 單一 `index.html`（無 build，直接瀏覽器開啟）
- 硬編碼 3 名起始冒險者（F/E 階）、8 個靜態任務模板
- 完整 GDD 公式實作：rankDiff、finalSuccessRate、finalDeathRate、willingness score
- 實時計時（TIME_SCALE = 1 sec/game-min）
- 結算：成功 / 失敗 / 死亡（永久）
- MUD 風格 log、金幣 / 聲望追蹤
- 招募功能（30g）

**刻意省略**：
- Build pipeline（Vite/TypeScript）
- 存檔讀檔
- 世界危險度
- 公會建設
- 動畫效果

**快捷方式**：
- 所有數值硬編碼
- 無錯誤處理
- 單一 HTML 檔案

---

## Result


流暢度1~10分計

建議測試流程：
1. 選冒險者 → 選任務 → 觀察成功/死亡率預覽 => 6分
2. 嘗試派遣高難度任務（觀察 NPC 拒絕機制）=> 9分
3. 等待 2-3 個任務結算 => 7分
4. 讓一名冒險者死亡（觀察情感反應）=>2分，需要更側重於玩家和冒險者建立情感連結
5. 金幣耗盡後的壓力感 => 沒有體驗到

---

## Metrics

- [x] 第一次 NPC 拒絕時玩家反應為何？=> 不知道為什麼會拒絕
- [x] 等待計時器是否製造緊張感？ => 有一點
- [x] 冒險者死亡時感覺損失還是無所謂？ => 比較無所謂
- [x] 決策花費時間（選人 + 選任務）= ? 秒 => 現階段很快
- [x] 10 分鐘遊玩後，玩家是否想繼續？ => 現階段體驗無法支撐10分鐘，但會想要把任務欄給清空

---

## Recommendation: [ PROCEED / PIVOT / KILL ]

1. NPC的背景資訊和人物介紹不夠，無法與玩家建立情感連結。推薦在UI上新增顯示NPC背景的介面，並且要補上NPC背景的說明欄位
2. 委託的代入感不夠，可能要補上更多詞條
3. GUILD LOG的形式很棒，可以保留。並且可以在其中加上更多環境或人物狀態，如描述公會當下環境、某某冒險者說了什麼

---

## If Proceeding

轉入正式實作需要：
- TypeScript + Vite 架構（依 CLAUDE.md）
- 存檔系統（localStorage）
- 任務結算獨立模組（`outcome-resolution.ts`）
- Time/Tick 系統（requestAnimationFrame based）
- 完整任務資料庫（`mission-database.ts`）
- 冒險者特質系統（Type 2 traits only）
- 主介面 shell（MUD log + DOM panel）