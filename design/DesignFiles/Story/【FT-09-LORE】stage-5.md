# Stage 5 — 最後的椅子

_對應 dangerLevel：A 末世_
_factionScore threshold：200_
_dialogueVariantMode：ophelia_alive_dead_

---

## 1. 解鎖對話視窗（dialogueKey: story.aurorae.stage5）

### 通用版（common，解鎖時刻）

# 文字：公會的天已經黑了。
委託板上只剩最後一張紙條。
上面寫著：「他不會說話，但你會懂。」

（同 Stage 1 開頭紙條，回應 FB-S4）

### 分軌版本（依 styleTag 偏好，解鎖時的詮釋分軌）

#### light 偏好（dialogueKey: story.aurorae.stage5.light）

# 文字：公會最後的光。
委託板釘著同樣的紙條。
「他不會說話，但你會懂。」

#### dark 偏好（dialogueKey: story.aurorae.stage5.dark）

# 文字：全是黑。委託板上。
同樣的話。
「他不會說話，但你會懂。」

#### mixed/neutral（dialogueKey: story.aurorae.stage5.mixed）

# 文字：最後的任務。
紙條與第一次相同。
「他不會說話，但你會懂。」

---

## 2. 奧菲莉雅的台詞（最終金句）

### 奧菲莉雅站起來

# 文字：她站起來。
她看著你。
「這次該我了。」

（共 1 句，全劇本台詞配額最後 1 句）

---

## 3. 紙條視覺與文字內容（FB-S4 呼應）

### 紙條金句

# 文字：他不會說話，但你會懂。

### 紙條視覺規格

# 美術：同 Stage 1 規格，但呈現狀態與世界末世氛圍相符
- light 版：紙張泛黃但仍工整，聖徽完整但微弱
- dark 版：紙張焦黑邊緣，字跡幾乎看不清，扭曲符號已變形
- neutral 版：標準米白，邊角破損明顯

### 紙條呈現動畫

# 動畫：紙條不再「自己冒出」——而是已經在那裡等待。當玩家視線掃過時，紙條才被「發現」；無光暈，進入動畫改為「字跡漸浮現」（2-3 秒），帶著某種「終於」的感覺

---

## 4. 玩家的選擇（dialogueKey 二元分支點）

### 選項呈現

玩家面對兩個選項：
1. **派奧菲莉雅**
2. **派另一個冒險者**

（具體 UI 實現細節由 P-02 主導；文本層在下方呈現）

---

## 5. 劇情委託內容（categoryID=3）

### 委託名稱

# 文字：最後的旅途

### 委託描述

# 文字：有一個地方必須去。那個地方在世界的邊界。派誰去，由你決定。他們不會回來。這不是威脅，只是事實。這次，玩家無法選擇「不派」——誰去，何時去，都由你承擔。

### 委託難度與機制

- difficulty: S（或自訂 SCRIPTED——難度無標準值，但結算時 baseReward=0）
- typeID: 4（調查）
- factionID: 1
- **isScriptedDeath: 1**（強制必死；無論誰去，都會死亡；不可被 condition trait 救活）
- requiredTraitID: 0

---

## 6. 結算流程（雙分支）

### 分支 A：玩家派遣奧菲莉雅

#### 派遣時動畫

# 美術 / 動畫：
- 奧菲莉雅從椅子上站起，走向委託板
- 她用手指尖輕輕觸碰紙條
- 她轉身，看向玩家的位置（相機 / 視點）
- 她沒有說話，但微微點頭
- 她走向門外，身影漸漸模糊、消失於黑暗中
- 她的椅子逐漸靠向鏡頭，空著

#### 結算視窗（dialogueKey: story.aurorae.stage5.epilogue.{styleTag}.dead）

##### light 偏好版（dialogueKey: story.aurorae.stage5.epilogue.light.dead）

# 文字：她去了最後的地方。
她沒有回來。
椅子從此空著。

##### dark 偏好版（dialogueKey: story.aurorae.stage5.epilogue.dark.dead）

# 文字：她走進了黑。
再也沒回來過。
椅子空了。

##### mixed/neutral 版（dialogueKey: story.aurorae.stage5.epilogue.neutral.dead）

# 文字：任務完成。
代價是她的離去。
椅子永遠空著。

---

### 分支 B：玩家派遣他人（拒絕奧菲莉雅）

#### 派遣時動畫

# 美術 / 動畫：
- 玩家選擇「派另一個冒險者」
- 奧菲莉雅坐回椅子上，沒有表情變化
- 她看著玩家做出這個選擇
- 她的眼神深邃但不責備
- 她不勸阻、不催促、不說話
- 所選冒險者走向委託板，走向門外，消失於黑暗中
- 奧菲莉雅仍坐在椅子上，茶杯在手邊（已冷）

#### 結算視窗（dialogueKey: story.aurorae.stage5.epilogue.{styleTag}.alive）

##### light 偏好版（dialogueKey: story.aurorae.stage5.epilogue.light.alive）

# 文字：她沒有去。
冒險者去了，沒有回來。
椅子仍然有人坐著。

##### dark 偏好版（dialogueKey: story.aurorae.stage5.epilogue.dark.alive）

# 文字：你選擇了她以外的人。
那個人消失了。
她還在這裡。

##### mixed/neutral 版（dialogueKey: story.aurorae.stage5.epilogue.neutral.alive）

# 文字：任務派遣完成。
死掉的是另一個人。
她活著，坐在椅子上。

---

## 7. 最終場景：公會誌完成

### 公會誌自動更新（無論奧菲莉雅死活）

# 美術 / 動畫：
- 公會誌物件自動翻至最後一頁
- 頁面上原本空白的部分開始出現字跡（漸進浮現，3-5 秒）
- 字跡工整但帶有匆促感

### 最終字跡內容

#### 如果奧菲莉雅派遣出去（死亡路線）

# 文字：「我寫完了。
最後這頁，在她走之前我就想好了。
她去了我沒去的地方。
她會看見我看不見的東西。
那就夠了。
—— 奧菲莉雅」

（字跡從工整到最後幾行變得倉促，最後一行簽名帶著顫抖感）

#### 如果奧菲莉雅活著（未派遣路線）

# 文字：「我寫完了。
不是我去的話，
別人去了。
我在這裡看著。
我會記錄下來。
所有的故事都應該被記錄。
—— 奧菲莉雅」

（字跡工整，最後的簽名帶有某種確定性）

---

## 8. Stage 5 終局場景：譚恩接班

### 場景切換（玩家最後一次離開公會時）

# 美術 / 動畫：
- 玩家點擊「離開公會」或遊戲自然結束時
- 公會場景的光線轉換為晨光（暗示時間流逝）
- 奧菲莉雅的椅子邊，另一張椅子被推了進來
- 譚恩（雜役）坐在那張椅子上，提起筆
- 他開始在一本新的本子上寫字
- 他的表情是沉靜的、專注的
- 鏡頭逐漸拉遠，公會場景變為靜物，譚恩持續寫字的身影在逆光中

# 文字（場景說明，不是對話）：「之後，有人繼續記錄。」

# 音效：筆尖在紙上沙沙的聲音（持續至淡出）

---

## 9. 聖徽紋路石碎裂（FB-A1 終極揭曉）

### 場景物件變化

# 美術 / 動畫：
- Stage 5 結算時，公會角落的「聖徽紋路石」開始崩裂
- 裂紋從石心向外擴散（1-2 秒動畫）
- 石頭碎裂成 3-5 塊，發出輕微碎裂聲
- 碎片仍在原位，散亂堆放
- 光暈完全消散

# 機制說明：FB-A1 顏色變化序列在 Stage 5 結算時完全關閉

---

## 10. 機制觸發 / 系統事件

# 機制：
- FT-09 階段解鎖機制觸發（factionScore >= 200）
- **unlockBlockerCondition 檢查**（§2.4.5 機制）：Stage 5 解鎖需檢查 `npc:ophelia:status==Idle`；若奧菲莉雅為 Wounded / Dead 狀態，Stage 5 進入 `_blockedStages`，不立即解鎖
  - `OnOpheliaReturned` 後自動 `TriggerDeferredStageCheck`，blocker 解除時補觸發解鎖
  - **Jam 版設計**：不制造「玩家永遠卡住」的局面，blocker 機制純粹保證敘事時序
- **dialogueVariantMode = "ophelia_alive_dead"** 啟用：Stage 5 解鎖時刻解析 styleTag 維度（仍不知死活）
  - 結算時透過 `OnFactionStoryStageEpilogue` 事件二次解析，疊加 alive/dead 維度
- C-01 categoryID=3 第五個委託呈現（最終委託，isScriptedDeath=1）
- **isScriptedDeath short-circuit 機制觸發**（FT-04 §3.x.5）：派遣時自動 roll 失敗 + 死亡，不經過擲骰
  - outcome.successRoll = -1.0，outcome.deathRoll = -1.0（標記「未擲骰」）
  - condition 特質（on_death_survive / on_fail_survive）被過濾，無法救活
- **FB-M3 動態文本注入**（結算視窗）：派遣者名字動態引用於 epilogue 文本（需 DialogueTable 支援變數或 FSD 層實現字串插值）
- **OnFactionStoryStageEpilogue 事件發布**（結算後）：
  - resolvedEpilogueKey = `"story.aurorae.stage5.epilogue.{styleTag}.{alive|dead}"`
  - isOpheliaEpilogue = true（識別此為奧菲莉雅相關結算）
  - subjectAlive = （派遣者身份 == 奧菲莉雅 templateID ? false : true）
- 公會誌最後一頁自動完成（FB-A2 最終字跡，無論死活）
- 聖徽紋路石碎裂（FB-A1 最終狀態）
- 場景物件狀態完全鎖定（奧菲莉雅椅子永遠空著 或 永遠有人；無再次變化）
- 背景天空進入「末世色」（全黑或深紫，不再變化）
- **TriggerDeferredStageCheck** 檢查任何其他被 blocker 卡住的 stage（預留機制，Jam 版可能無其他 stage 被阻擋）
- 遊戲不 Game Over：玩家可繼續經營公會、派遣冒險者，但世界已進入 A 階，敘事終局已達成
