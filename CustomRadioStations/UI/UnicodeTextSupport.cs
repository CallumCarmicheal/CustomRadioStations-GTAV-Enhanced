using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CustomRadioStations {
    public enum UnicodeTextMode {
        Auto,
        NativeOnly,
        BitmapFallback
    }

    internal enum UnicodeTextAlignment {
        Center,
        Left,
        Right
    }

    internal static class UnicodeTextSupport {
        internal static bool RequiresFallback(string text) {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int index = 0; index < text.Length; index++) {
                char current = text[index];
                if (char.IsSurrogate(current))
                    return true;

                int codePoint = current;
                if (!IsNativeWesternCharacter(codePoint))
                    return true;
            }

            return false;
        }


        internal static List<string> SplitTextElements(string text) {
            var rawElements = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text ?? string.Empty);
            while (enumerator.MoveNext())
                rawElements.Add((string)enumerator.Current);

            // .NET Framework 4.8 predates the newest extended-grapheme rules. Keep its
            // useful handling for combining marks/Hangul, then merge the sequences which
            // are most likely to be split by the older Unicode tables: emoji ZWJ chains,
            // variation selectors, emoji modifiers/tags, regional-indicator flags and
            // half-width Katakana + voiced/semi-voiced marks.
            var result = new List<string>(rawElements.Count);
            for (int index = 0; index < rawElements.Count; index++) {
                string current = rawElements[index];
                while (index + 1 < rawElements.Count) {
                    string next = rawElements[index + 1];
                    if (ShouldMergeTextElements(current, next)) {
                        current += next;
                        index++;
                        continue;
                    }
                    break;
                }
                result.Add(current);
            }
            return result;
        }

        internal static int AdjustWrapBreak(IList<string> elements, int start, int breakAt) {
            if (elements == null || elements.Count == 0)
                return breakAt;

            int adjusted = Math.Max(start + 1, Math.Min(breakAt, elements.Count));
            // Basic kinsoku shori: do not begin a new line with closing punctuation,
            // small kana, iteration/prolongation marks, etc., and do not leave an opening
            // bracket at the end of the previous line. Move one or more graphemes to the
            // next line rather than allowing punctuation to dangle at the boundary.
            while (adjusted > start + 1 && adjusted < elements.Count &&
                (IsProhibitedLineStart(elements[adjusted]) || IsProhibitedLineEnd(elements[adjusted - 1]))) {
                adjusted--;
            }
            return adjusted;
        }

        private static bool ShouldMergeTextElements(string current, string next) {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
                return false;

            int nextCodePoint = GetFirstCodePoint(next);
            if (EndsWithCodePoint(current, 0x200D) || nextCodePoint == 0x200D)
                return true;
            if (IsVariationSelector(nextCodePoint) || IsEmojiModifier(nextCodePoint) ||
                IsEmojiTag(nextCodePoint) || nextCodePoint == 0xFF9E || nextCodePoint == 0xFF9F)
                return true;

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(next, 0);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark ||
                category == UnicodeCategory.EnclosingMark)
                return true;

            // Pair regional indicators into one flag grapheme, but do not merge a third
            // indicator into an already completed pair.
            return IsSingleCodePoint(current, out int currentCodePoint) &&
                IsRegionalIndicator(currentCodePoint) && IsRegionalIndicator(nextCodePoint);
        }

        private static bool IsProhibitedLineStart(string element) {
            int codePoint = GetFirstCodePoint(element);
            if (codePoint < 0)
                return false;

            const string prohibited = "\u3001\u3002\uFF0C\uFF0E\u30FB\uFF1A\uFF1B\uFF1F\uFF01\u203C\u2047\u2048\u2049)]\uFF09\uFF3D\uFF5D\u3015\u3009\u300B\u300D\u300F\u3011\u3019\u3017\u301F\u2019\u201D\uFF60\uFF63\u00BB" +
                "\u3041\u3043\u3045\u3047\u3049\u3063\u3083\u3085\u3087\u308E\u30A1\u30A3\u30A5\u30A7\u30A9\u30C3\u30E3\u30E5\u30E7\u30EE\u30F5\u30F6\u30FC\u3005\u30FD\u30FE\u309D\u309E\u301C\uFF5E\u2026\u2025\u309B\u309C";
            return codePoint <= char.MaxValue && prohibited.IndexOf((char)codePoint) >= 0;
        }

        private static bool IsProhibitedLineEnd(string element) {
            int codePoint = GetFirstCodePoint(element);
            if (codePoint < 0)
                return false;

            const string prohibited = "([\uFF08\uFF3B\uFF5B\u3014\u3008\u300A\u300C\u300E\u3010\u3018\u3016\u301D\u2018\u201C\uFF5F\uFF62\u00AB";
            return codePoint <= char.MaxValue && prohibited.IndexOf((char)codePoint) >= 0;
        }

        private static bool IsSingleCodePoint(string value, out int codePoint) {
            codePoint = GetFirstCodePoint(value);
            if (codePoint < 0)
                return false;
            return value.Length == (codePoint > char.MaxValue ? 2 : 1);
        }

        private static int GetFirstCodePoint(string value) {
            if (string.IsNullOrEmpty(value))
                return -1;
            if (char.IsHighSurrogate(value[0]) && value.Length > 1 && char.IsLowSurrogate(value[1]))
                return char.ConvertToUtf32(value[0], value[1]);
            return value[0];
        }

        private static bool EndsWithCodePoint(string value, int codePoint) {
            if (string.IsNullOrEmpty(value))
                return false;
            int index = value.Length - 1;
            int actual = value[index];
            if (char.IsLowSurrogate(value[index]) && index > 0 && char.IsHighSurrogate(value[index - 1]))
                actual = char.ConvertToUtf32(value[index - 1], value[index]);
            return actual == codePoint;
        }

        private static bool IsVariationSelector(int codePoint) {
            return (codePoint >= 0xFE00 && codePoint <= 0xFE0F) ||
                (codePoint >= 0xE0100 && codePoint <= 0xE01EF);
        }

        private static bool IsEmojiModifier(int codePoint) {
            return codePoint >= 0x1F3FB && codePoint <= 0x1F3FF;
        }

        private static bool IsEmojiTag(int codePoint) {
            return codePoint >= 0xE0020 && codePoint <= 0xE007F;
        }

        private static bool IsRegionalIndicator(int codePoint) {
            return codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;
        }

        internal static string ComputeStableHash(string value) {
            byte[] data = Encoding.UTF8.GetBytes(value ?? string.Empty);
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] hash = sha256.ComputeHash(data);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte valueByte in hash)
                    builder.Append(valueByte.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static bool IsNativeWesternCharacter(int codePoint) {
            // GTA's EFIGS font library reliably covers ASCII, Western Latin and the
            // punctuation normally used by western-language track metadata. Keep that
            // path native so existing English/European titles remain visually identical.
            if (codePoint == '\r' || codePoint == '\n' || codePoint == '\t')
                return true;
            // Keep only the Western ranges we can reasonably expect from GTA's
            // EFIGS fonts on the native fast path. Latin Extended-B/IPA (0180-024F)
            // is much less dependable, so route it through the fallback instead of
            // risking another missing-glyph box.
            if (codePoint >= 0x20 && codePoint <= 0x017F)
                return true;

            switch (codePoint) {
            case 0x2010:
            case 0x2011:
            case 0x2012:
            case 0x2013:
            case 0x2014:
            case 0x2015:
            case 0x2018:
            case 0x2019:
            case 0x201A:
            case 0x201B:
            case 0x201C:
            case 0x201D:
            case 0x201E:
            case 0x201F:
            case 0x2020:
            case 0x2021:
            case 0x2022:
            case 0x2026:
            case 0x2030:
            case 0x2032:
            case 0x2033:
            case 0x2039:
            case 0x203A:
            case 0x2044:
            case 0x20AC:
            case 0x2122:
            case 0x2212:
                return true;
            default:
                return false;
            }
        }
    }
}
