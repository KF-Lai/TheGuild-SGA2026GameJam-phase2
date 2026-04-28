using System.Globalization;

// FT-06 Guild Core — 公會名稱字串處理工具
// 實作依據：【FT-06-FSD】guild-core.md §5.4-E / GDD §3.9 / §4.4 / §5.4.1~5.4.4
// 職責（SRP）：純靜態函式，不依賴 MonoBehaviour 或任何 Unity runtime 物件

namespace TheGuild.Gameplay.Guild
{
    /// <summary>
    /// 公會名稱字串處理工具（純靜態，無副作用）。
    /// 處理控制字元過濾、全型字截斷、後綴組合。
    /// </summary>
    public static class GuildNameUtility
    {
        // ────────────────────────────────────────────────────────────────────────
        // 全形字 Unicode 範圍定義（疑點 5 決策：Unicode block 範圍逐字判斷）
        // 對齊 FSD §3.9 / GDD §5.4.3 Rich Text「逐字計入，不解析」原則
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 過濾字串中的 Unicode 控制字元（UnicodeCategory.Control）。
        /// Rich Text 標籤內字符逐字計入，不解析標籤（FSD §5.4.3）。
        /// </summary>
        public static string StripControlChars(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input ?? string.Empty;
            }

            // 先掃描是否有控制字元；無則直接回原字串，避免不必要的 alloc
            bool hasControl = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(input[i]) == UnicodeCategory.Control)
                {
                    hasControl = true;
                    break;
                }
            }

            if (!hasControl)
            {
                return input;
            }

            // 有控制字元時才建 char[]（熱路徑避免不必要 alloc）
            char[] buffer = new char[input.Length];
            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(input[i]) != UnicodeCategory.Control)
                {
                    buffer[count++] = input[i];
                }
            }

            return new string(buffer, 0, count);
        }

        /// <summary>
        /// 判斷字符是否為全形字（全形字計 1，半形字計 0）。
        /// 依 Unicode block 判斷，覆蓋常見 CJK 及全形 Latin/Punct 範圍。
        /// </summary>
        public static bool IsFullWidth(char c)
        {
            // CJK Unified Ideographs（常用漢字）：U+4E00 ~ U+9FFF
            if (c >= 0x4E00 && c <= 0x9FFF) return true;

            // CJK Extension A：U+3400 ~ U+4DBF
            if (c >= 0x3400 && c <= 0x4DBF) return true;

            // CJK Extension B 以上為 Surrogate pair，char 單一無法表示，略過

            // CJK Compatibility Ideographs：U+F900 ~ U+FAFF
            if (c >= 0xF900 && c <= 0xFAFF) return true;

            // Hiragana：U+3040 ~ U+309F
            if (c >= 0x3040 && c <= 0x309F) return true;

            // Katakana：U+30A0 ~ U+30FF
            if (c >= 0x30A0 && c <= 0x30FF) return true;

            // Hangul Syllables：U+AC00 ~ U+D7AF
            if (c >= 0xAC00 && c <= 0xD7AF) return true;

            // Hangul Jamo：U+1100 ~ U+11FF
            if (c >= 0x1100 && c <= 0x11FF) return true;

            // Fullwidth Latin / Punct：U+FF00 ~ U+FFEF（全形英數與全形標點）
            if (c >= 0xFF00 && c <= 0xFFEF) return true;

            // CJK Radicals Supplement / Kangxi Radicals / Punctuation：U+2E80 ~ U+303F
            if (c >= 0x2E80 && c <= 0x303F) return true;

            // CJK Strokes / Enclosed CJK letters：U+31C0 ~ U+31FF / U+3200 ~ U+33FF
            if (c >= 0x31C0 && c <= 0x33FF) return true;

            // Enclosed CJK Letters and Months：U+3200 ~ U+32FF（已含在上方範圍）

            // Bopomofo / Bopomofo Extended：U+02EA ~ U+02EB / U+3100 ~ U+312F
            if (c >= 0x3100 && c <= 0x312F) return true;

            return false;
        }

        /// <summary>
        /// 計算字串的顯示字數（全型字計 1，半型字計 0.5，向上取整）。
        /// 內部模型：全型 +2，半型 +1，最終除以 2 向上取整（對齊 FSD §3.9 §4.4 公式）。
        /// </summary>
        public static int CountDisplayChars(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }

            int units = 0;  // 全型 +2，半型 +1
            for (int i = 0; i < input.Length; i++)
            {
                units += IsFullWidth(input[i]) ? 2 : 1;
            }

            // 向上取整除以 2
            return (units + 1) / 2;
        }

        /// <summary>
        /// 截斷字串，使顯示字數不超過 maxFullwidth 個全型字。
        /// 全型字計 1，半型字計 0.5；達到上限即停止。
        /// 對齊 FSD §3.9 / GDD §4.4 公式（內部使用整數模型：全型 +2，半型 +1，上限 = maxFullwidth * 2）。
        /// </summary>
        public static string TruncateToFullwidthLimit(string input, int maxFullwidth)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input ?? string.Empty;
            }

            // 上限換算為 units（全型 +2，半型 +1）
            int maxUnits = maxFullwidth * 2;
            int accumulated = 0;
            int cutIndex = input.Length;   // 預設不截斷

            for (int i = 0; i < input.Length; i++)
            {
                int cost = IsFullWidth(input[i]) ? 2 : 1;

                // 加入此字後是否超過上限
                if (accumulated + cost > maxUnits)
                {
                    // 達到上限，此字不納入
                    cutIndex = i;
                    break;
                }

                accumulated += cost;
            }

            return cutIndex >= input.Length ? input : input.Substring(0, cutIndex);
        }

        /// <summary>
        /// 組合完整公會顯示名稱。
        /// 步驟：StripControlChars → Trim → TruncateToFullwidthLimit → 加後綴。
        /// 若截斷後結果為空，回傳 DEFAULT_GUILD_NAME（"公會"）。
        /// 對齊 FSD §5.4-E。
        /// </summary>
        public static string ComposeDisplayName(string rawInput)
        {
            // 步驟 1：過濾控制字元
            string stripped = StripControlChars(rawInput ?? string.Empty);

            // 步驟 2：去除前後空白
            string trimmed = stripped.Trim();

            // 步驟 3：截斷至全型字上限
            string truncated = TruncateToFullwidthLimit(trimmed, GuildCoreConstants.GUILD_NAME_MAX_FULLWIDTH);

            // 步驟 4：若截斷後為空，回傳預設名稱
            if (string.IsNullOrEmpty(truncated))
            {
                return GuildCoreConstants.DEFAULT_GUILD_NAME;
            }

            // 步驟 5：加上後綴
            return truncated + GuildCoreConstants.GUILD_NAME_SUFFIX;
        }

        /// <summary>
        /// 驗證公會名稱是否合法（非空且顯示字數在上限內）。
        /// </summary>
        public static bool IsValidGuildName(string rawInput)
        {
            if (string.IsNullOrEmpty(rawInput))
            {
                return false;
            }

            string stripped = StripControlChars(rawInput);
            string trimmed = stripped.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                return false;
            }

            // 顯示字數在上限內即合法
            return CountDisplayChars(trimmed) <= GuildCoreConstants.GUILD_NAME_MAX_FULLWIDTH;
        }
    }
}
