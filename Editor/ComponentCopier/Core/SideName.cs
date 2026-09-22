using System;
using System.Text.RegularExpressions;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum Side
    {
        None,
        Left,
        Right,
    }

    /// <summary>
    /// Flips the side marker of an object name: "Hand_L" ↔ "Hand_R", "LeftArm" ↔ "RightArm",
    /// "Skirt_L_01" ↔ "Skirt_R_01", "左手" ↔ "右手". The rules follow Blender's BLI_string_flip_side_name,
    /// extended by camelCase words, the Japanese 左 / 右 and a marker token in the middle of the name. Exactly
    /// one marker is flipped; the rules are tried in order and the first that applies wins. A rename suffix
    /// (".001", " (1)") is kept out of the way.
    /// </summary>
    internal static class SideName
    {
        private const char LeftKanji = '左';
        private const char RightKanji = '右';

        private static readonly Regex RenameSuffix = new Regex(@"(\.\d+|\s\(\d+\))$", RegexOptions.Compiled);
        private static readonly string[] Words = { "left", "Left", "LEFT", "right", "Right", "RIGHT" };

        public static Side GetSide(string name) => TryFlip(name, out _, out var side) ? side : Side.None;

        public static bool TryFlip(string name, out string flipped) => TryFlip(name, out flipped, out _);

        /// <summary>
        /// Returns the name with its side marker flipped, or false (and the name unchanged) when it has none.
        /// </summary>
        public static bool TryFlip(string name, out string flipped, out Side side)
        {
            flipped = name;
            side = Side.None;
            if (string.IsNullOrEmpty(name)) return false;

            string suffix = "";
            var match = RenameSuffix.Match(name);
            if (match.Success && match.Index > 0)
            {
                suffix = match.Value;
                name = name.Substring(0, match.Index);
            }

            if (!TryFlipEnd(name, out var result, out side) &&
                !TryFlipStart(name, out result, out side) &&
                !TryFlipWord(name, out result, out side) &&
                !TryFlipKanji(name, out result, out side) &&
                !TryFlipToken(name, out result, out side))
            {
                return false;
            }

            flipped = result + suffix;
            return true;
        }

        private static bool IsSeparator(char c) => c == '.' || c == '_' || c == '-' || c == ' ';

        private static bool TryFlipLetter(char letter, out char flipped, out Side side)
        {
            switch (letter)
            {
                case 'L': flipped = 'R'; side = Side.Left; return true;
                case 'l': flipped = 'r'; side = Side.Left; return true;
                case 'R': flipped = 'L'; side = Side.Right; return true;
                case 'r': flipped = 'l'; side = Side.Right; return true;
                default: flipped = letter; side = Side.None; return false;
            }
        }

        /// <summary>"Hand_L", "Hand.r"</summary>
        private static bool TryFlipEnd(string name, out string result, out Side side)
        {
            result = name;
            side = Side.None;

            int last = name.Length - 1;
            if (last < 1 || !IsSeparator(name[last - 1])) return false;
            if (!TryFlipLetter(name[last], out var letter, out side)) return false;

            result = name.Substring(0, last) + letter;
            return true;
        }

        /// <summary>"L_Hand", "r.hand"</summary>
        private static bool TryFlipStart(string name, out string result, out Side side)
        {
            result = name;
            side = Side.None;

            if (name.Length < 2 || !IsSeparator(name[1])) return false;
            if (!TryFlipLetter(name[0], out var letter, out side)) return false;

            result = letter + name.Substring(1);
            return true;
        }

        /// <summary>
        /// "左手", "手首右": 左 / 右 at the start or the end. Japanese needs no separator, as the character is a
        /// word of its own. A name with both ("左右対称") has no side.
        /// </summary>
        private static bool TryFlipKanji(string name, out string result, out Side side)
        {
            result = name;
            side = Side.None;

            if (name.IndexOf(LeftKanji) >= 0 && name.IndexOf(RightKanji) >= 0) return false;

            int last = name.Length - 1;
            int index = TryFlipKanjiChar(name[0], out _, out _) ? 0
                : TryFlipKanjiChar(name[last], out _, out _) ? last
                : -1;
            if (index < 0) return false;

            TryFlipKanjiChar(name[index], out var flipped, out side);
            result = name.Substring(0, index) + flipped + name.Substring(index + 1);
            return true;
        }

        private static bool TryFlipKanjiChar(char c, out char flipped, out Side side)
        {
            switch (c)
            {
                case LeftKanji: flipped = RightKanji; side = Side.Left; return true;
                case RightKanji: flipped = LeftKanji; side = Side.Right; return true;
                default: flipped = c; side = Side.None; return false;
            }
        }

        /// <summary>
        /// "LeftArm", "leftArm", "ArmLeft", "left_arm", "Left arm". The word keeps its form (left / Left / LEFT).
        /// </summary>
        private static bool TryFlipWord(string name, out string result, out Side side)
        {
            result = name;
            side = Side.None;

            foreach (var word in Words)
            {
                if (name.Length < word.Length) continue;

                if (name.StartsWith(word, StringComparison.Ordinal) && IsBoundaryAfter(word, name, word.Length))
                {
                    result = FlipWord(word, out side) + name.Substring(word.Length);
                    return true;
                }

                int start = name.Length - word.Length;
                if (name.EndsWith(word, StringComparison.Ordinal) && IsBoundaryBefore(word, name, start))
                {
                    result = name.Substring(0, start) + FlipWord(word, out side);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// "Skirt_L_01", "Skirt_Left_01": a marker between separators. Two markers are ambiguous and left alone.
        /// </summary>
        private static bool TryFlipToken(string name, out string result, out Side side)
        {
            result = name;
            side = Side.None;

            int found = -1;
            int foundLength = 0;
            string replacement = null;
            var foundSide = Side.None;

            int start = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                if (i < name.Length && !IsSeparator(name[i])) continue;

                int length = i - start;
                if (length > 0 && TryFlipWholeToken(name.Substring(start, length), out var flippedToken, out var tokenSide))
                {
                    if (found >= 0) return false;
                    found = start;
                    foundLength = length;
                    replacement = flippedToken;
                    foundSide = tokenSide;
                }

                start = i + 1;
            }

            if (found < 0) return false;

            result = name.Substring(0, found) + replacement + name.Substring(found + foundLength);
            side = foundSide;
            return true;
        }

        private static bool TryFlipWholeToken(string token, out string flipped, out Side side)
        {
            if (token.Length == 1 &&
                (TryFlipLetter(token[0], out var letter, out side) || TryFlipKanjiChar(token[0], out letter, out side)))
            {
                flipped = letter.ToString();
                return true;
            }

            foreach (var word in Words)
            {
                if (token != word) continue;
                flipped = FlipWord(word, out side);
                return true;
            }

            flipped = token;
            side = Side.None;
            return false;
        }

        private static string FlipWord(string word, out Side side)
        {
            bool left = word[0] == 'l' || word[0] == 'L';
            side = left ? Side.Left : Side.Right;

            string other = left ? "right" : "left";
            if (word == word.ToUpperInvariant()) return other.ToUpperInvariant();
            if (word == word.ToLowerInvariant()) return other;
            return char.ToUpperInvariant(other[0]) + other.Substring(1);
        }

        /// <summary>
        /// A word at the start must not run into more lowercase letters: "LeftArm", "leftArm" and "Left_Arm"
        /// carry a marker, "Leftarm" does not. An all-caps word needs a separator, as "LEFTARM" cannot be split.
        /// </summary>
        private static bool IsBoundaryAfter(string word, string name, int index)
        {
            if (index >= name.Length) return true;

            char next = name[index];
            if (!char.IsLetter(next)) return true;
            return char.IsUpper(next) && word != word.ToUpperInvariant();
        }

        /// <summary>
        /// A word at the end must begin a new word: "ArmLeft" and "Arm_Left" carry a marker, "Cleft" does not.
        /// </summary>
        private static bool IsBoundaryBefore(string word, string name, int index)
        {
            if (index == 0) return true;

            char previous = name[index - 1];
            if (!char.IsLetter(previous)) return true;
            return IsCapitalized(word) && char.IsLower(previous);
        }

        private static bool IsCapitalized(string word) =>
            char.IsUpper(word[0]) && word.Substring(1) == word.Substring(1).ToLowerInvariant();
    }
}
