# 奧菲莉雅 美術規格書（Ophelia Art Spec）

> 本文件為奧菲莉雅單角色的獨立美術規格，用於 GPT 圖像生成的迭代調適。
> 確定風格後，將核心參數回寫至 `【ART-01】art-requirements.md`。

_建立時間：2026-05-01_
_當前版本：v0.3（與 ART-01 v1.7 風格基準對齊）_
_狀態：迭代中_

---

## 1. 風格定位

### 1.1 目標風格

**日式動漫 chibi（Q 版）+ 西式黑暗奇幻舊化質感**（與 ART-01 §0.1 對齊）

視覺基準圖：`design/DesignFiles/Art/角色參考圖.png`

> **重要：基準圖中的人物形象（兜帽收割者）只用來示範繪畫風格，不可直接照用其造型。**
> 奧菲莉雅本人的服裝、髮型、配件、表情、姿勢一律以本文件 §3 的描述為準。
> 基準圖只負責定義「畫風的觀賞感受」，不負責定義「人物本身」。

整體風格以一句話說明：

> 「乍看是 3 頭身上下、輪廓乾淨的日式 chibi 動漫角色；走近一看，會發現布料邊緣有撕裂、繃帶層疊、布料髒舊污漬等手繪感舊化紋理，色調是低飽和的暖棕、焦褐與墨黑——是一個沉默走過長路、依然站得很穩的小角色。」

| 項目 | 規格（參照基準圖） |
|---|---|
| 整體風格 | 日式 chibi 動漫 + 西式黑暗奇幻舊化質感的混合風 |
| 人物結構 | 約 3~3.5 頭身，頭大身小但比例自然；五官清楚但不誇張；軀幹敦實短小；手腳以線稿輪廓 + 平塗呈現 |
| 線條 | 乾淨銳利的黑色描邊；外輪廓粗（2~3px 等效）+ 內部結構線細（1~2px 等效）；不要手繪抖動或西式墨線速寫感 |
| 上色感受 | 平塗 + 1 層硬邊陰影；無漸層、無羽化；左上方光源 |
| 髒舊紋理 | 布料邊緣、繃帶、磨損處可加入手繪感的不規則暗斑、撕裂與污漬，但主體仍由線稿主導，紋理只是裝飾 |
| 細節程度 | 服裝結構（護肩、腰帶、靴子）清楚；簡化手指、繡花、布料褶皺；不做寫實光影 |
| 色板 | 5~8 色，低～中等飽和度；以暖棕、焦褐、墨黑、米白為主；關鍵敘事細節（茶湯、徽記）可用點綴色 |
| 背景 | 透明 PNG（基準圖的綠色純底僅為輸出參考） |

### 1.2 與基準圖的關係（明確區分「學什麼／不學什麼」）

| 學的（風格層面） | 不學的（人物層面） |
|---|---|
| 線稿粗細與乾淨度 | 兜帽斗篷的剪裁 |
| 平塗 + 硬邊陰影的上色感受 | 鐮刀武器 |
| 髒舊紋理的手法（布料污漬、撕裂、繃帶） | 眼罩與單眼露出的設計 |
| 色板與飽和度（暖棕焦褐墨黑） | 護身符項鍊與毛邊配件 |
| 頭身比例與五官比例 | 表情、髮色、髮型 |
| 整體氛圍（壓抑暖調、Q 版親切但有故事感） | 任何具體服飾／配件造型 |

### 1.3 額外排除項目（與本角色定位不符）

| 排除項目 | 原因 |
|---|---|
| 鮮血、外傷特寫 | 預設立繪為日常狀態，受傷狀態走變體 A-1b |
| 暴露衣著 | 實用傭兵裝，完整包覆 |
| 過度黑暗壓抑（純黑色調、邪典感） | 基準圖雖偏暗但仍是溫暖棕調，奧菲莉雅是「沉默而非冷漠」 |
| 低頭、頭髮完全遮臉、閉眼 | 眼神是劇情核心，必須清晰可辨識 |
| 西式漫畫粗墨線速寫感、手繪抖動感 | 與本案 chibi 動漫線條質感不符（v0.3 修正 v0.2 方向） |
| 寫實 6/7 頭身比例 | 與本案 3~3.5 頭身基準不符 |
| 純萌系 chibi（kawaii pastel、moe） | 角色為成熟傭兵，不是萌系吉祥物 |

### 1.4 目標氛圍

`silent, composed, watchful, slightly melancholic`
沉默但不冷漠；靜謐而有存在感；眼神深邃可被辨識；外表小巧但不可愛幼齒。

---

## 2. 畫布規格

| 項目 | 規格 |
|---|---|
| 畫布尺寸 | 512 × 768 px |
| 格式 | PNG，透明背景 |
| 角色位置 | 居中，腳底距畫布底部 ≥ 32px，頭頂距頂部 ≥ 16px |
| 視角 | 正面或 ¾ 側面（偏左約 15°），全身站姿 |

---

## 3. 角色外觀規格

### 3.1 基本資料

| 項目 | 規格 |
|---|---|
| 性別 | 女性 |
| 種族 | 人類 |
| 年齡感 | 20 代中後期，成熟但不老 |
| 體型 | 不高但姿態沉穩，中等身形 |

### 3.2 服裝

| 部位 | 描述 |
|---|---|
| 上衣 | 深藍或暗灰色實用傭兵裝，長袖，無華麗裝飾 |
| 肩甲 | 皮質護肩（單肩或雙肩皆可），有使用痕跡 |
| 腰帶 | 寬皮腰帶，有扣環，偶有小配件 |
| 下身 | 深色長褲或裙褲，實用型 |
| 靴子 | 中筒皮靴，有磨損感 |

### 3.3 頭部

| 項目 | 描述 |
|---|---|
| 髮色 | 深棕或黑髮 |
| 髮型 | 中短長度，無刻意整理，髮絲自然垂落臉側 |
| 表情 | **中性、靜默**，眼神深邃直視，不笑不皺眉 |
| 眼睛 | 深棕色，稍大（漫畫風），清晰可見，是識別核心 |

### 3.4 道具（必要）

- 手持或靠近身旁有陶瓷茶杯（簡樸款，小杯）
- 茶杯狀態：有熱茶（可見液體，可不畫水蒸氣）

---

## 4. 不可省略的劇情符號

| 符號 | 要求 |
|---|---|
| 眼神 | 中性直視，必須清晰可辨識（不可被頭髮遮擋超過 50%） |
| 茶杯 | 必須出現在畫面中 |
| 椅子相容性 | 姿態與構圖需預留「坐姿版本」的銜接可能（身體比例需一致） |

---

## 5. GPT 生成提示詞（v0.3，自然語言版本）

> **生成方式建議：** GPT-4o 等多模態模型對自然語言敘述的響應比關鍵字堆疊更精確。本版本以中文自然語言敘述為主、英文關鍵字為輔。建議同時上傳基準圖 `design/DesignFiles/Art/角色參考圖.png` 作為 style reference。

### 【中文自然語言指示（建議直接貼給 GPT）】

```
請參考我提供的風格基準圖（design/DesignFiles/Art/角色參考圖.png）的繪畫風格繪製一張角色立繪。

【關於風格基準圖的使用】
基準圖中的兜帽收割者造型「不要照搬」。我只取它的：
- 日式 chibi 動漫 + 西式黑暗奇幻舊化質感的混合風
- 約 3 至 3.5 頭身、頭大身小但比例自然的人物結構
- 乾淨銳利的黑色線稿（外輪廓粗、內部結構線細，無手繪抖動感）
- 平塗加一層硬邊陰影的上色感受（沒有漸層、沒有羽化，光源來自左上）
- 布料邊緣的污漬、撕裂、繃帶層疊等手繪感舊化紋理（局部裝飾，不蓋過主體輪廓）
- 5 到 8 色的低飽和暖棕、焦褐、墨黑、米白色板

【明確不要照搬的東西】
基準圖中的兜帽、鐮刀、眼罩、護身符項鍊、毛邊斗篷、繃帶位置等，
都是「示範繪畫風格用」，不要套到角色身上。

【要繪製的角色：奧菲莉雅（Ophelia）】
- 性別：女性，人類，20 代中後期
- 體型：身形不高但姿態沉穩，敦實但仍能看出女性輪廓
- 服裝：深藍或暗灰色實用傭兵裝（長袖外套），單肩或雙肩有皮質護肩（有使用痕跡）；
       寬皮腰帶配金屬扣環；深色長褲；中筒磨損皮靴
- 髮型：深棕或近黑的中短髮，無刻意整理，髮絲自然垂落臉側，但不可遮過眼睛
- 表情：中性、靜默，雙眼深棕色、深邃直視前方，不笑也不皺眉，嘴部閉合放鬆
- 道具：一手持小型陶瓷茶杯（樸素款），杯內可見深棕茶湯
- 姿勢：全身站姿，正面或微微偏左 15 度的 ¾ 視角，雙腳落地，重心略偏一側
- 氛圍關鍵字：沉默、沉穩、警覺、略帶哀愁；外表小巧但不可愛幼齒

【畫布與輸出】
- 畫布 512 × 768 px，透明背景 PNG
- 角色置中，全身可見，腳底距畫布底部約 32 至 64 px
- 不要文字、不要浮水印、不要漸層、不要光暈、不要寫實 3D 質感
```

### 【英文關鍵字補充（如生成器偏好英文 prompt）】

```
chibi anime character illustration with western dark fantasy worn texture,
around 3 to 3.5 head body proportion, head large relative to body but natural proportion,
clean crisp black lineart, thicker outer contour, thinner inner structure lines,
no hand-drawn jitter, no rough ink-sketch feel,
flat color blocks with single hard-edged shadow layer,
zero gradients, zero airbrush, upper-left soft lighting,
localized hand-painted grime, torn cloth edges, bandage accents on clothing,

young adult human woman, late 20s, mercenary, calm stoic presence,
dark navy or charcoal long-sleeve practical mercenary tunic,
worn leather shoulder pad on one or both shoulders, wide leather belt with metal buckle,
dark trousers, mid-calf scuffed leather boots,
dark brown to near-black jaw-length hair, naturally falling, not covering eyes,
neutral closed-mouth expression, deep brown eyes looking directly forward,
holding a small plain ceramic cup with dark tea visible,
full body standing pose, three-quarter view turned slightly left,

muted warm earthy palette (worn brown, burnt sienna, ink black, dusty off-white),
5 to 8 colors total, low to mid saturation,
clean transparent background, character centered head to toe
```

### 【Negative Prompt】

```
hooded figure, scythe, eye patch, grim reaper costume, exact replica of style reference character,
blood, gore, wounds, exposed skin, cleavage, revealing outfit,
6-head proportion, 7-head proportion, realistic adult proportions, photorealistic, 3D render,
chibi cute mascot, kawaii pastel, pure japanese moe style,
western comic ink sketch, rough hand-drawn ink lines, brush stroke jitter, watercolor, oil painting, painterly,
smooth gradients, airbrush shading, soft glow, bloom, lens flare,
smiling, laughing, crying, dramatic expression, closed eyes, hair fully covering face,
text, watermarks, logo, multiple characters, environment background
```

### 【建議參數】

- AR：2:3（對應 512×768）
- Style reference：上傳 `design/DesignFiles/Art/角色參考圖.png`，權重建議 **0.4~0.6**（既擷取風格、又避免角色形象被同化為兜帽收割者；若發現生成結果開始長出兜帽或鐮刀，再往下降權重）
- Seed strategy：找到滿意版本後固定 seed，後續所有角色（米拉、譚恩、凱拉、具名冒險者）共用同 seed 以維持風格一致

### 【v0.3 修改說明（相對 v0.2）】

| 面向 | v0.2 寫法 | v0.3 修正 |
|---|---|---|
| 風格定位 | 西式漫畫墨線插圖（pen and ink, western tabletop RPG art） | 改為日式 chibi 動漫 + 西式黑暗奇幻舊化質感的混合風 |
| 基準圖 | `風格&頭身參考.jpg` | 改為 `design/DesignFiles/Art/角色參考圖.png` |
| 頭身比例 | 4 頭身（head is one-quarter of total height） | 改為 3~3.5 頭身（與 ART-01 §0.1 對齊） |
| 線條風格 | 粗獷墨線、手繪不完美感、brushstroke jitter | 改為乾淨銳利線稿、無手繪抖動 |
| 陰影 | crosshatch 紋理 | 改為平塗 + 硬邊陰影 + 局部舊化暗斑紋理 |
| 寫法 | 英文關鍵字堆疊為主 | 改為中文自然語言為主、英文關鍵字為補充 |
| Style ref 權重 | 0.6~0.8 | 降至 0.4~0.6（避免角色形象被基準圖同化） |
| 「不照搬基準圖人物」明確指示 | 無 | prompt 開頭與 negative 雙重強調 |

### 【與風格目標的對應】

- 中文自然語言段落 → 直接傳達結構、氛圍、舊化質感的混合需求；GPT-4o 對中文敘述場景理解高
- 英文 `chibi anime ... with western dark fantasy worn texture` → 兩個風格錨點，避免落入純萌系或純黑暗單邊
- `clean crisp black lineart, no hand-drawn jitter, no rough ink-sketch feel` → 明確排除 v0.2 方向的副作用
- `localized hand-painted grime, torn cloth edges, bandage accents` → 確保舊化質感從基準圖傳遞下來
- `hooded figure, scythe, eye patch ... exact replica of style reference character`（negative） → 防止 GPT 把基準圖人物形象當成「目標人物」

---

## 6. 一致性驗收清單

出圖後，依下列項目逐一確認再交給迭代判斷：

- [ ] 頭身比例約 3~3.5 頭（與 ART-01 §0.1 與基準圖一致）
- [ ] 線稿乾淨銳利，外輪廓明顯粗於內部線條，無手繪抖動或墨線速寫感
- [ ] 上色為平塗 + 硬邊陰影，完全無漸層
- [ ] 服裝邊緣可見適度髒舊紋理（污漬、磨損、繃帶等），但不蓋過主體輪廓
- [ ] 表情中性，雙眼清晰可見（不被頭髮遮蓋）
- [ ] 茶杯出現在畫面中
- [ ] **未照搬基準圖中的兜帽、鐮刀、眼罩、護身符等收割者特徵**
- [ ] 服裝為深藍／暗灰實用傭兵裝，非亮色或奇異裝扮
- [ ] 無血腥、傷口、暴露衣著、誇張表情
- [ ] 透明背景，角色清晰與背景分離
- [ ] 整體色板 ≤8 色，暖棕／焦褐／墨黑為基調

---

## 7. 迭代記錄

| 版本 | 日期 | 提示詞變更 | 問題 / 結果 | 決策 |
|---|---|---|---|---|
| v0.1 | 2026-05-01 | 初版 | 頭身 5.5~6（過長）、線稿太乾淨精緻、陰影仍有漸層/cel-shading 感 | 全部三項需修正 |
| v0.2 | 2026-05-01 | 明確頭身比例（1/4 頭高）、加強線稿不完美感、強制排除漸層陰影、Style ref 權重調至 0.6~0.8 | 待出圖驗收 | — |
| v0.3 | 2026-05-01 | **全面換風格基準**：從西式漫畫墨線改為日式 chibi 動漫 + 西式黑暗奇幻舊化質感；改用 `design/DesignFiles/Art/角色參考圖.png`；頭身降至 3~3.5（與 ART-01 §0.1 對齊）；prompt 改為中文自然語言為主、英文關鍵字為輔；明確要求不照搬基準圖人物形象；Style ref 權重降至 0.4~0.6 | 待出圖驗收 | — |

---

## 8. 待確認事項

| # | 問題 | 狀態 |
|---|---|---|
| W-1 | 頭身確定為 4~4.5？還是想試試 3.5 折中版本？ | **v0.3 已採用 3~3.5 頭身，與 ART-01 §0.1 對齊** |
| W-2 | 線條粗糙質感強度——完全對齊參考圖，還是稍微乾淨一點？ | **v0.3 已改為乾淨銳利線稿；舊化感由布料污漬、撕裂、繃帶紋理提供，而非由線條提供** |
| W-3 | 確認風格後，是否要將新風格基準回寫至 ART-01，取代原本的日系賽璐璐定義？ | **v0.3 已同步寫入 ART-01 §0.1（v1.7）** |
