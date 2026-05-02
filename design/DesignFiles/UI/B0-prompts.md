# B0 視覺基準 Prompts（純 UI 版）

_建立時間：2026-05-01_
_最後更新：2026-05-02_
_批次：B0（視覺基準）_
_用途：複製送 GPT-4o / Midjourney / Stable Diffusion / DALL·E 產圖_
_來源 brief：`design/UI/UI-art-brief.md` v0.3_
_範圍：UI 視覺基準（4 組）；立繪相關不在本檔範圍_

---

## 共用調性 Token（每組 prompt 共用）

對齊 brief §2「整體氛圍：略帶暗黑與歲月感」：

**Positive 共用**：
```
subtle aged worn quality, lived-in feel, faint dust accumulation, weathered with traces of age, moody warm twilight atmosphere, gentle gothic undertones, somber muted palette, dusk lighting like candlelight or sunset, slightly tarnished metal, parchment with faint yellowing and age stains
```

**Negative 共用**：
```
pristine, brand new, vibrant cheerful, oversaturated, bright daylight, glossy plastic, modern minimalist, sterile clean, full bright noon lighting
```

**註**：每組 prompt 已將上述 token 自然融入，無需重複貼。

---

## 執行順序建議

1. **先跑 B0-01 色板樣本**——確認主色調是否符合「略帶暗黑與歲月感」
2. 色板通過後跑 **B0-02 紋理樣本**——驗證材質方向（含紙條三版差異）
3. 同時跑 **B0-03 字型樣本** 與 **B0-04 樣板按鈕**——確認字型可讀性與按鈕質感

風格目測通過後，將圖檔存入 `Assets/Art/UI/Reference/` 對應檔名，並進入 B1 通用控件批次。

---

## B0-01 色板樣本（v2，加入歲月感）

**【UI-ID】** B0-01
**【名稱】** 色板樣本（Color Palette Sample）
**【尺寸】** 1920×1080 px
**【檔名】** `B0-01_color-palette.png`
**【路徑】** `Assets/Art/UI/Reference/B0-01_color-palette.png`

### Positive Prompt

```
game art bible color palette reference page for a dark medieval fantasy guild management game, somber moody atmosphere with twilight warm tones, organized swatches arranged in labeled groups: primary palette of warm wood tones (deep brown #4A3520, light brown #B68A5C, cream off-white #EFE4D2), slightly tarnished metallic accent (gold #C9A961 with faint patina), dark ink (deep brown #3B2A1A), state colors (muted forest green #6B9B6E, dusty crimson #A04848, weathered amber #D4923A), faction colors (sunrise gold #F4D58A for Aurorae goddess, mystic dark purple #5B4B7A for outer god), each swatch shown as a labeled tile with hex code and usage note, aged parchment background with subtle yellowing and faint age stains, hand-drawn rectangular swatch frames with worn edges, ink-style annotations slightly faded, professional art reference sheet style with lived-in feel, top-down flat composition, illustration, traces of dust in corners, dim warm candlelight ambience
```

### Negative Prompt

```
3d render, photorealistic, gradient swatches, modern flat design ui, neon, cyberpunk, oversaturated, glitchy, anime girl, chaotic arrangement, pixel art swatches, glossy plastic, pristine clean, brand new, vibrant cheerful, bright daylight, sterile minimalist
```

### 設計意圖

建立 The Guild 全體 UI 的色彩語彙基準，強調「歲月感與暮光調」。後續所有 UI prompt 引用此色板，確保跨資產色彩一致性與整體氛圍。

---

## B0-02 紋理樣本拼貼（v2，強化紙條三版差異 + 歲月感）

**【UI-ID】** B0-02
**【名稱】** 紋理樣本拼貼（Texture Sample Sheet）
**【尺寸】** 1920×1080 px
**【檔名】** `B0-02_texture-sheet.png`
**【路徑】** `Assets/Art/UI/Reference/B0-02_texture-sheet.png`

### Positive Prompt

```
texture reference sheet for a dark medieval fantasy guild ui with somber timeworn atmosphere, organized in a clean labeled grid (2 rows by 3 columns), six samples each 480x320 px tile clearly separated with thin line dividers and labels underneath, samples must be VISUALLY DISTINCT from each other:
tile 1 dark walnut wood plank with subtle vertical grain, deep brown base #4A3520 with visible knots and fine wear scratches, traces of dust;
tile 2 polished but slightly tarnished gold metal trim with engraved decorative border, faint patina and age oxidation in recessed details, bright but not pristine (#C9A961);
tile 3 aged cream parchment standard ground with faint paper grain, slight yellowing, subtle age stains spread across surface (warm off-white #EFE4D2);
tile 4 Letter V1 Aurorae note: distinctly cream-yellow paper, smooth texture, faintly luminous warm undertone, mostly clean uncreased surface but with one corner slightly worn (light cream #F2EBD8);
tile 5 Letter V2 outer god note: clearly grayish-brown stained paper with prominent grain noise speckles, dramatic torn edge along left side, scorched burn mark in bottom-right corner with charred edges, dirt smudges (grayish #D8C8A0 ground, distinctly darker and dirtier than V1);
tile 6 Letter V3 folded note: clearly bright white-cream cleanly different from cream-yellow V1, two prominent crease lines forming a quartered fold pattern, slight brown discoloration along folds (neutral white-cream #EAE3D0);
each sample with bold label below, hand-drawn label tags with worn edges, art reference style, top-down flat layout, painterly texture, lived-in feel, dim warm twilight ambience, samples should look like they belong to an old guild archive
```

### Negative Prompt

```
3d render, photorealistic, blurry, glossy plastic, modern flat, neon, cyberpunk, chaotic, color noise overlay, oversaturated, anime characters, ui frames, identical paper variants, three nearly identical letter samples, paper variants too similar, weak distinction between tiles, pristine clean surfaces, brand new materials, vibrant cheerful, bright daylight
```

### 設計意圖

固定四種主紋理（深棕木質／微銹金邊／泛黃羊皮紙／紙條三版）的視覺基準，並對齊 FT-09 v3.1 patch §5 紙條三版規格的核心要求（三版必須一眼可辨）。所有後續主面板（D 層）、邊框、紙條氣泡（F-B-08~10）引用此樣本。

---

## B0-03 字型樣本展示（v2，加入歲月感）

**【UI-ID】** B0-03
**【名稱】** 字型樣本展示（Typography Sample）
**【尺寸】** 1920×1080 px
**【檔名】** `B0-03_typography.png`
**【路徑】** `Assets/Art/UI/Reference/B0-03_typography.png`

### Positive Prompt

```
typography reference card for a dark medieval fantasy game ui with timeworn atmosphere, three labeled sections from top to bottom on aged parchment background with subtle yellowing,
section 1 traditional chinese serif body font in deep brown ink showing sample text "公會委託　冒險者名冊　審核新任務　派遣艾倫前往討伐",
section 2 monospaced western numerals and english labels for currency display in dark brown showing "1234567890　+250 GOLD　Lv.3",
section 3 hand-drawn cursive script for letter notes in faded brown ink with slight ink bleed showing flowing handwritten phrases,
all samples on warm cream parchment with faint age stains and dust, each section has small label tag identifying its usage with worn edges, hand-drawn label boxes, reference card style, clean readable layout but with lived-in feel, illustration, dim warm candlelight ambience
```

### Negative Prompt

```
3d render, photorealistic, modern flat design, sans-serif body font, glitchy text, japanese kana, korean hangul, neon, cyberpunk, oversaturated, anime characters, pristine new paper, vibrant cheerful, bright daylight, sterile clean
```

### 設計意圖

固定中文主字（serif，正文用）、英／數等寬字（金額用）、紙條手寫字三組字型基準，避免跨 UI 字型飄移。氛圍對齊 brief §2 暮光暗黑調。

> **註**：此圖為**視覺方向參考**，最終實際字型由 art-director 直接選用既有字體（Noto Serif TC / 思源宋體 + 等寬數字字型 + Klee One 系列），不從圖檔切版。

---

## B0-04 樣板按鈕展示（v2，強化態差異 + 歲月感）

**【UI-ID】** B0-04
**【名稱】** 樣板按鈕展示（Reference Button Set）
**【整圖尺寸】** 1920×1080 px（reference sheet）
**【單顆按鈕展示尺寸】** 240×64 px（reference 用放大展示，便於目視四態差異）
**【實作切版目標】** 180×48 px（依 `UI-art-brief.md` §3.3 F-A 按鈕規格）
**【檔名】** `B0-04_button-reference.png`
**【路徑】** `Assets/Art/UI/Reference/B0-04_button-reference.png`

### Positive Prompt

```
medieval fantasy game ui button reference sheet with dark somber timeworn atmosphere, two button styles each presented in four states arranged in a clean grid (2 rows of 4), each button rendered LARGE and CLEARLY at 240x64 px size in the reference image,
top row primary button: dark walnut wood base with bright lustrous gold metal trim showing faint patina and age oxidation in recessed details (NOT antique bronze, must be bright gold #C9A961 but slightly weathered) and cream label "確定" with deep brown ink;
default state regular flat appearance with subtle wear marks and dust;
hover state with PROMINENT outer warm golden halo glow extending 8-12 pixels around button, brightness increased 20%, subtle inner highlight like ember glow;
pressed state visibly SUNKEN inward 3 pixels with deep inset shadow at top-left edges, slightly darker overall tone, button appears physically depressed;
disabled state 50% desaturated grayish brown with heavier dust and grime, no glow, label faded gray;
bottom row danger button: deep crimson red wood base (#A04848 with darker grain) with same slightly tarnished gold trim and cream label "解雇" with same four states (default with wear / glowing red ember hover / sunken pressed / heavily faded grimy disabled);
clear bold state labels under each button, painterly hand-drawn texture, aged parchment background with subtle yellowing, well-spaced organized reference layout with breathing room, illustration, art reference sheet style, lived-in feel, dim warm twilight ambience, button states must be obviously distinguishable at a glance
```

### Negative Prompt

```
3d render, photorealistic, modern flat design, gradient fill, neon glow, cyberpunk, glitchy, glossy plastic, oversaturated, css button preview, web ui, anime characters, antique bronze trim, dull gold, button states too similar, hover indistinguishable from default, pressed flat without depth, small cramped layout, pristine clean, brand new, vibrant cheerful, bright daylight, sterile minimalist
```

### 設計意圖

建立 F-A-07 主要按鈕（Primary）與 F-A-09 危險按鈕（Danger）的視覺基準，含 default/hover/pressed/disabled 四態。氛圍對齊 brief §2 暮光暗黑調，按鈕看起來像「已被多代公會長使用過」。後續所有按鈕（Secondary、Icon-only、Toggle、Tab）引用此套質感、光源、磨損規則。

---

## 工具執行小技巧

### Midjourney
- 加 `--ar 16:9`（B0-01/02/03/04）
- 風格穩定：加 `--style raw --s 250`
- 暗黑感：可加 `--chaos 5` 微擾動避免太規整
- 同一 prompt 跑 4 張取最佳

### Stable Diffusion（Web UI / ComfyUI）
- 採樣器：DPM++ 2M Karras
- 步數：30~40
- CFG：7~9
- 模型建議：sd-xl-base-1.0 + 中世紀奇幻風 LoRA；可疊 Darkest Dungeon style LoRA 強化暗黑感

### GPT-4o / DALL·E 3
- 直接貼 positive prompt，DALL·E 3 不支援 negative prompt（在 prompt 內以「avoid X」自然語言表達）
- 解析度選擇 1024×1024 或 1792×1024（橫式）

### 通用建議
- B0-01 色板若 hex 色顯示不準，可手動在 Figma / Photoshop 重畫並貼 hex（GPT 不一定精準還原 hex）
- B0-04 按鈕產出後，截取單顆按鈕做 9-slice 處理
- 暗黑感過頭可降回：刪「somber」「gentle gothic」保留「subtle aged worn quality, lived-in feel」即可

---

## 產出後操作

1. 圖檔存入：`Assets/Art/UI/Reference/B0-XX_*.png`（覆蓋原檔）
2. 風格通過後在 `UI-art-brief.md` §9 加註「v0.3 B0 visual baseline 已建立」
3. 進入 B1 通用控件批次（F-A 全套 18 項）

---

## 變更歷史

| 日期 | 版本 | 變更摘要 |
|---|---|---|
| 2026-05-01 | v0.1 | 初版建立。共 5 組 prompts（色板 / 紋理 / 字型 / 樣板按鈕 / 立繪 placeholder），對齊 `UI-art-brief.md` v0.2 |
| 2026-05-02 | v0.2 | 第一輪審查：B0-01/03/05 通過、B0-02/04 重產。補入 B0-02 v2（強化紙條三版差異）與 B0-04 v2（放大尺寸 + 強化態差異 + 亮金 trim）|
| 2026-05-02 | v0.3 | 重構：(1) 移除 B0-05 立繪相關內容，本檔僅聚焦 UI；(2) 全部 4 組 UI prompts 加入「暗黑陳舊」視覺 token（aged worn / lived-in / dust / patina / twilight ambience）；(3) §2 補入共用調性 token 區塊；(4) 對齊 `UI-art-brief.md` v0.3 |
| 2026-05-02 | v0.3.1 | 跨檔對齊 review：B0-04 標頭補入「整圖尺寸 / 單顆按鈕展示尺寸 / 實作切版目標」三段尺寸，明示 240×64（reference 展示）vs 180×48（實作切版）的關係，對齊 brief §3.3 F-A 按鈕規格 |
