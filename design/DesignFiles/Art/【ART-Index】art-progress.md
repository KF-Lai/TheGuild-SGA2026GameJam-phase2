# 美術資產進度索引（Art Progress Index）

> ⚠ **請務必使用 UTF-8 編碼開啟**，否則中文字元會出現亂碼。

_最後更新：2026-05-02_
_規格來源：`【ART-01】art-requirements.md` v1.7.2_

---

## 狀態說明

| 標記 | 意義 |
|---|---|
| `待製作` | 尚未開始 |
| `進行中` | 生成中或待調整 |
| `完成` | 已出圖，待驗收 |
| `已驗收` | 通過 §0.1.8 檢查清單 |
| `待補` | 設計尚未確定，無法製作 |

---

## 整體進度總覽

| 優先度 | 總數 | 已驗收 | 完成 | 進行中 | 待製作 |
|---|---|---|---|---|---|
| P0 必做 | 7 | 0 | 0 | 0 | 7 |
| P1 建議 | 26 | 0 | 0 | 0 | 26 |
| P2 可選 | 17 | 0 | 0 | 0 | 17 |
| P3 餘裕 | 17 | 0 | 0 | 0 | 17 |
| 未分級 | 4 | 0 | 0 | 0 | 4 |
| **合計** | **71** | **0** | **0** | **0** | **71** |

> 總數含 E-4/E-5（待補，不列入有效計數）

---

## 群組 A：主要角色立繪

| ID   | 名稱               | 檔案                                                 | 優先度 | 狀態  | 備註                 |     |
| ---- | ---------------- | -------------------------------------------------- | --- | --- | ------------------ | --- |
| A-1  | 奧菲莉雅 預設          | `Characters/Adventurers/ophelia_default.png`       | P0  | 待製作 | 情感核心；眼神最重要         |     |
| A-1b | 奧菲莉雅 受傷          | `Characters/Adventurers/ophelia_wounded.png`       | P2  | 待製作 | Stage 4 wounded 狀態 |     |
| A-2  | 米拉 預設            | `Characters/Staff/mira_default.png`                | P1  | 待製作 | 聖徽別針不可省略           |     |
| A-2b | 米拉 Stage4 light  | `Characters/Staff/mira_stage4_light_accepted.png`  | P2  | 待製作 | 聖徽清晰可見             |     |
| A-2c | 米拉 Stage4 common | `Characters/Staff/mira_stage4_common_resigned.png` | P2  | 待製作 | 聖徽消失或遮蓋            |     |
| A-3  | 譚恩 預設            | `Characters/Staff/tan_default.png`                 | P1  | 待製作 | 左胸口袋筆末端不可省略        |     |
| A-3b | 譚恩 Stage5 寫字     | `Characters/Staff/tan_stage5_writing.png`          | P2  | 待製作 | 坐姿；需與椅子 D-1 疊圖相容   |     |
| A-4  | 凱拉 預設            | `Characters/Staff/kaira_default.png`               | P1  | 待製作 | 左手腕暗紋不可省略；左手不戴手套   |     |
| A-4b | 凱拉 Stage4 離開     | `Characters/Staff/kaira_stage4_leaving.png`        | P2  | 待製作 | 站姿緊張，目光銳利          |     |
| A-4c | 凱拉 Stage5 等待     | `Characters/Staff/kaira_stage5_waiting.png`        | P2  | 待製作 | 坐姿；需與椅子 D-1 疊圖相容   |     |

---

## 群組 B：具名冒險者立繪

> Jam 版 P3（有餘裕再製作）；可先以 C 群組通用原型代替。

| ID   | templateID | 名稱      | rank | 職業  | 種族  | 檔案                                    | 優先度 | 狀態  |
| ---- | ---------- | ------- | ---- | --- | --- | ------------------------------------- | --- | --- |
| B-01 | 101        | 馬庫斯·柯特  | B    | 傭兵  | 人類  | `Adventurers/adv_101_marcus_kurt.png`   | P3  | 待製作 |
| B-02 | 102        | 艾利亞·維恩  | C    | 法師  | 人類  | `Adventurers/adv_102_elia_vienne.png`   | P3  | 待製作 |
| B-03 | 103        | 卡蒙·葛雷   | B    | 戰士  | 獸人  | `Adventurers/adv_103_kamon_grey.png`    | P3  | 待製作 |
| B-04 | 104        | 露西·費恩   | D    | 遊俠  | 精靈  | `Adventurers/adv_104_lucy_fane.png`     | P3  | 待製作 |
| B-05 | 105        | 馬爾科·賽亞  | A    | 傭兵  | 人類  | `Adventurers/adv_105_marco_seya.png`    | P3  | 待製作 |
| B-06 | 106        | 蘭妮絲·科恩  | C    | 治癒師 | 人類  | `Adventurers/adv_106_lannis_cohen.png`  | P3  | 待製作 |
| B-07 | 107        | 維克托·鄧恩  | B    | 戰士  | 矮人  | `Adventurers/adv_107_victor_dunn.png`   | P3  | 待製作 |
| B-08 | 108        | 莉利安娜·席爾 | S    | 遊俠  | 精靈  | `Adventurers/adv_108_liliana_sill.png`  | P3  | 待製作 |
| B-09 | 109        | 米歇爾·艾許  | S    | 法師  | 人類  | `Adventurers/adv_109_michelle_ash.png`  | P3  | 待製作 |
| B-10 | 110        | 莫德·克萊   | B    | 傭兵  | 人類  | `Adventurers/adv_110_maude_clay.png`    | P3  | 待製作 |
| B-11 | 111        | 艾文·羅斯   | A    | 斥候  | 人類  | `Adventurers/adv_111_evan_ross.png`     | P3  | 待製作 |

> B-12（templateID=112）已移除，待資料確認後補入。

---

## 群組 C：通用冒險者原型

> ⚠ ART-01 文字標注「共 7 張」但表格僅列 6 職業（professionID 5 缺漏），待確認後補入。

| ID | 職業 | professionID | 檔案 | 優先度 | 狀態 |
|---|---|---|---|---|---|
| C-01 | 戰士 | 1 | `Adventurers/adv_generic_warrior.png` | P3 | 待製作 |
| C-02 | 法師 | 6 | `Adventurers/adv_generic_mage.png` | P3 | 待製作 |
| C-03 | 遊俠 | 2 | `Adventurers/adv_generic_ranger.png` | P3 | 待製作 |
| C-04 | 斥候 | 3 | `Adventurers/adv_generic_scout.png` | P3 | 待製作 |
| C-05 | 治癒師 | 4 | `Adventurers/adv_generic_healer.png` | P3 | 待製作 |
| C-06 | 傭兵 | 7 | `Adventurers/adv_generic_mercenary.png` | P3 | 待製作 |

---

## 群組 D：公會室內道具

| ID | 名稱 | 檔案 | 畫布 | 優先度 | 狀態 | 備註 |
|---|---|---|---|---|---|---|
| D-1 | 奧菲莉雅的椅子 | `Props/chair_ophelia.png` | 512×512 | P1 | 待製作 | 需相容 A-3b / A-4c 坐姿疊圖 |
| D-2a | 木酒杯（滿） | `Props/mug_full.png` | 512×512 | P0 | 待製作 | 木製鐵箍，深琥珀液體飽滿，杯口 1~2 個酒沫 |
| D-2b | 木酒杯（殘留） | `Props/mug_residue.png` | 512×512 | P0 | 待製作 | 液面已落，杯內壁可見酒漬／液痕 |
| D-2c | 木酒杯（空） | `Props/mug_empty.png` | 512×512 | P0 | 待製作 | 完全空，舊酒漬乾痕，覆薄塵 |
| D-3 | 委託板 | `Props/bulletin_board.png` | **1024×768** | P0 | 待製作 | 尺寸為例外規格 |
| D-4 | 公會誌 | `Props/guild_ledger.png` | 512×512 | 未分級 | 待製作 | 皮革封面手寫日誌 |
| D-5a | 聖徽紋路石 Stage1 | `Props/SacredStone/sacred_stone_stage1.png` | 512×512 | P2 | 待製作 | 純白，淡金聖徽，有光暈 |
| D-5b | 聖徽紋路石 Stage2 | `Props/SacredStone/sacred_stone_stage2.png` | 512×512 | P2 | 待製作 | 灰白，光暈減弱 |
| D-5c | 聖徽紋路石 Stage3 | `Props/SacredStone/sacred_stone_stage3.png` | 512×512 | P2 | 待製作 | 灰色，暗灰-紫灰紋路 |
| D-5d | 聖徽紋路石 Stage4 | `Props/SacredStone/sacred_stone_stage4.png` | 512×512 | P2 | 待製作 | 深灰底，紫灰紋路，無光暈 |
| D-5e | 聖徽紋路石 Stage5 碎裂 | `Props/SacredStone/sacred_stone_stage5_shattered.png` | 512×512 | P2 | 待製作 | 碎 3~5 塊，邊緣鋒利 |
| D-6 | 米拉角落信件堆 | `Props/mira_letters_pile.png` | 512×512 | 未分級 | 待製作 | Stage 1-4，米拉加入後 |

---

## 群組 E：陣營紙條

| ID | Stage | 版本 | 檔案 | 優先度 | 狀態 |
|---|---|---|---|---|---|
| E-1a | Stage 1 | light | `Props/Notes/note_stage1_light.png` | P1 | 待製作 |
| E-1b | Stage 1 | dark | `Props/Notes/note_stage1_dark.png` | P1 | 待製作 |
| E-1c | Stage 1 | neutral | `Props/Notes/note_stage1_neutral.png` | P1 | 待製作 |
| E-2a | Stage 3 | light | `Props/Notes/note_stage3_light.png` | P2 | 待製作 |
| E-2b | Stage 3 | dark | `Props/Notes/note_stage3_dark.png` | P2 | 待製作 |
| E-2c | Stage 3 | neutral | `Props/Notes/note_stage3_neutral.png` | P2 | 待製作 |
| E-3a | Stage 3 門口 | light/neutral | `Props/Notes/note_door_stage3_common.png` | P1 | 待製作 |
| E-3b | Stage 3 門口 | dark | `Props/Notes/note_door_stage3_dark.png` | P1 | 待製作 |
| E-4 | Stage 4 | — | — | — | 待補 |
| E-5 | Stage 5 | — | — | — | 待補 |

---

## 群組 F：公會室內背景

| ID | 名稱 | 檔案 | 優先度 | 狀態 | 天空描述 |
|---|---|---|---|---|---|
| F-1 | Onboarding | `Backgrounds/bg_guild_onboarding.png` | P0 | 待製作 | 晴朗藍天，清晨光 |
| F-2a | Stage 1（和平） | `Backgrounds/bg_guild_stage1.png` | P0 | 待製作 | 晴朗藍天，日間暖光 |
| F-2b | Stage 2（動盪初期） | `Backgrounds/bg_guild_stage2.png` | P1 | 待製作 | 灰色雲層，散射冷光 |
| F-3 | Stage 3（暗湧） | `Backgrounds/bg_guild_stage3.png` | P2 | 待製作 | 黃昏橙色固定天空 |
| F-4 | Stage 4（危局） | `Backgrounds/bg_guild_stage4.png` | P2 | 待製作 | 深紫色，近夜 |
| F-5 | Stage 5（末世） | `Backgrounds/bg_guild_stage5.png` | P2 | 待製作 | 全黑或極深紫，無星無月 |

---

## 群組 G：UI 圖示

| ID | 名稱 | 檔案 | 優先度 | 狀態 |
|---|---|---|---|---|
| G-1 | 晨曦聖徽 | `UI/Icons/icon_dawn_emblem.png` | 未分級 | 待製作 |
| G-2a | 職業圖示：戰士 | `UI/Icons/icon_profession_warrior.png` | P1 | 待製作 |
| G-2b | 職業圖示：遊俠 | `UI/Icons/icon_profession_ranger.png` | P1 | 待製作 |
| G-2c | 職業圖示：斥候 | `UI/Icons/icon_profession_scout.png` | P1 | 待製作 |
| G-2d | 職業圖示：治癒師 | `UI/Icons/icon_profession_healer.png` | P1 | 待製作 |
| G-2e | 職業圖示：法師 | `UI/Icons/icon_profession_mage.png` | P1 | 待製作 |
| G-2f | 職業圖示：傭兵 | `UI/Icons/icon_profession_mercenary.png` | P1 | 待製作 |
| G-3a | 等階徽章：F | `UI/Icons/icon_rank_F.png` | P1 | 待製作 |
| G-3b | 等階徽章：E | `UI/Icons/icon_rank_E.png` | P1 | 待製作 |
| G-3c | 等階徽章：D | `UI/Icons/icon_rank_D.png` | P1 | 待製作 |
| G-3d | 等階徽章：C | `UI/Icons/icon_rank_C.png` | P1 | 待製作 |
| G-3e | 等階徽章：B | `UI/Icons/icon_rank_B.png` | P1 | 待製作 |
| G-3f | 等階徽章：A | `UI/Icons/icon_rank_A.png` | P1 | 待製作 |
| G-3g | 等階徽章：S | `UI/Icons/icon_rank_S.png` | P1 | 待製作 |

---

## 待確認事項

| # | 問題 | 來源 | 狀態 |
|---|---|---|---|
| Q-1 | Group C 文字標注「共 7 張」但表格僅 6 職業，professionID 5 可能缺漏 | §1 Group C | 待確認 |
| Q-2 | B-12（templateID=112）bio 描述與奧菲莉雅重複，暫不製作 | §1 Group B | 待確認 |
| Q-3 | D-4 / D-6 / E-2 / G-1 未列入優先度表，已暫標未分級 | §6 製作優先度 | 待確認 |
| Q-4 | E-4 / E-5 設計尚未確定，待 FT-09 確認後補充 | §3 Group E | 待設計 |

---

## 變更歷史

| 日期 | 版本 | 內容 |
|---|---|---|
| 2026-05-01 | v1.0 | 初版建立，依 ART-01 v1.5 完整列出所有資產（71 項），標記初始狀態為「待製作」；記錄 4 項待確認問題 |
| 2026-05-02 | v1.1 | 同步至 ART-01 v1.7.2：規格來源版本字段更新；狀態說明檢查清單引用 §0.1.5 → §0.1.8（v1.7 後 §0.1.5 已改名「細節程度」）；D 群組 D-2a/b/c 由「茶杯（熱／冷／空）」更名為「木酒杯（滿／殘留／空）」，檔名 `teacup_*.png` → `mug_full/residue/empty.png`，備註同步描述木製鐵箍舊質感（隨 ART-01 v1.7.2 A-1 道具更換決策連動） |
