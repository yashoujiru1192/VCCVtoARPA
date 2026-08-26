using System;
using System.Collections.Generic;
using System.Linq;

namespace VccvToArpa {
    /// <summary>
    /// Parses the alias notation used by Cz-style English VCCV USTs and
    /// expands it into the ARPAbet phoneme inventory used by ARPAsing banks.
    /// Alias boundaries are preserved separately so the phonemizer can emit
    /// ARPAsing start, transition, and release aliases without consulting G2P.
    /// </summary>
    internal static class VccvEnglishAliasParser {
        internal sealed class ParsedAlias {
            public string[] Phonemes { get; init; } = Array.Empty<string>();
            public bool HasStart { get; init; }
            public bool HasEnd { get; init; }
            public bool IsUnderscoreContinuation { get; init; }
            public bool StrongSignature { get; init; }
        }

        private sealed class CzToken {
            public string Text { get; init; } = string.Empty;
            public string[] Arpabet { get; init; } = Array.Empty<string>();
        }

        // Longest spellings are matched first. Composite vowels used by the
        // OpenUtau EN VCCV implementation expand back into their underlying
        // vowel/coda sequence for ARPAsing.
        private static readonly CzToken[] Tokens = new[] {
            Token("Ang", "ae", "ng"),
            Token("1ng", "ih", "ng"),
            Token("8n", "aw", "n"),
            Token("9l", "ao", "l"),
            Token("hhy", "hh", "y"),
            Token("ch", "ch"),
            Token("dh", "dh"),
            Token("sh", "sh"),
            Token("th", "th"),
            Token("zh", "zh"),
            Token("ng", "ng"),
            Token("nk", "ng"),
            Token("dd", "dx"),
            Token("hh", "hh"),
            Token("hy", "hh", "y"),
            Token("sp", "s", "p"),
            Token("st", "s", "t"),

            Token("@", "ae"),
            Token("a", "aa"),
            Token("u", "ah"),
            Token("x", "ah"),
            Token("0", "ow"),
            Token("8", "aw"),
            Token("I", "ay"),
            Token("e", "eh"),
            Token("3", "er"),
            Token("A", "ey"),
            Token("i", "ih"),
            Token("E", "iy"),
            Token("O", "ow"),
            Token("Q", "oy"),
            Token("6", "uh"),
            Token("o", "uw"),
            Token("9", "ao"),
            Token("&", "ae"),
            Token("1", "ih"),

            Token("b", "b"),
            Token("d", "d"),
            Token("f", "f"),
            Token("g", "g"),
            Token("h", "hh"),
            Token("j", "jh"),
            Token("k", "k"),
            Token("l", "l"),
            Token("m", "m"),
            Token("n", "n"),
            Token("p", "p"),
            Token("r", "r"),
            Token("s", "s"),
            Token("t", "t"),
            Token("v", "v"),
            Token("w", "w"),
            Token("y", "y"),
            Token("z", "z"),
        };

        private static readonly HashSet<string> ArpaVowels = new(
            new[] {
                "aa", "ae", "ah", "ao", "aw", "ay", "eh", "er", "ey",
                "ih", "iy", "ow", "oy", "uh", "uw",
            },
            StringComparer.Ordinal);

        private static CzToken Token(string text, params string[] arpabet) =>
            new() { Text = text, Arpabet = arpabet };

        internal static bool IsVowel(string phoneme) =>
            ArpaVowels.Contains(phoneme);

        internal static bool TryParse(string lyric, out ParsedAlias parsed) {
            parsed = null;
            var source = (lyric ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(source)
                    || source.StartsWith(".", StringComparison.Ordinal)
                    || source == "+" || source == "+~" || source == "+*"
                    || source == "+!" || source.Equals("br", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var hasStart = source.StartsWith("-", StringComparison.Ordinal);
            var underscore = source.StartsWith("_", StringComparison.Ordinal);
            var hasEnd = source.EndsWith("-", StringComparison.Ordinal)
                && source.Length > 1;

            if (hasStart || underscore) {
                source = source.Substring(1).TrimStart();
            }
            if (hasEnd && source.Length > 0) {
                source = source.Substring(0, source.Length - 1).TrimEnd();
            }
            if (string.IsNullOrEmpty(source)) {
                return false;
            }

            var rawParts = source.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var expandedParts = new List<string[]>();
            var allPhonemes = new List<string>();
            foreach (var rawPart in rawParts) {
                if (!TryTokenize(rawPart, out var partPhonemes)) {
                    return false;
                }
                expandedParts.Add(partPhonemes);
                allPhonemes.AddRange(partPhonemes);
            }
            if (allPhonemes.Count == 0 || allPhonemes.Count > 7) {
                return false;
            }

            parsed = new ParsedAlias {
                Phonemes = allPhonemes.ToArray(),
                HasStart = hasStart,
                HasEnd = hasEnd,
                IsUnderscoreContinuation = underscore,
                StrongSignature = hasStart || hasEnd || underscore
                    || HasDistinctiveCzSymbol(source)
                    || HasVccvSpacePattern(expandedParts),
            };
            return true;
        }

        private static bool TryTokenize(string source, out string[] phonemes) {
            var result = new List<string>();
            var offset = 0;
            while (offset < source.Length) {
                CzToken match = null;
                foreach (var token in Tokens) {
                    if (source.AsSpan(offset).StartsWith(
                            token.Text.AsSpan(), StringComparison.Ordinal)) {
                        match = token;
                        break;
                    }
                }
                if (match == null) {
                    phonemes = Array.Empty<string>();
                    return false;
                }
                result.AddRange(match.Arpabet);
                offset += match.Text.Length;
            }
            phonemes = result.ToArray();
            return phonemes.Length > 0;
        }

        private static bool HasDistinctiveCzSymbol(string source) {
            if (source.IndexOfAny(new[] { '@', '&', '0', '1', '3', '6', '8', '9' }) >= 0) {
                return true;
            }
            return source.Length > 1
                && source.Any(character => character is 'A' or 'E' or 'I' or 'O' or 'Q');
        }

        private static bool HasVccvSpacePattern(IReadOnlyList<string[]> parts) {
            if (parts.Count != 2 || parts.Any(part => part.Length == 0)) {
                return false;
            }
            var left = parts[0];
            var right = parts[1];
            var leftAllVowels = left.All(IsVowel);
            var leftAllConsonants = left.All(symbol => !IsVowel(symbol));
            var rightAllVowels = right.All(IsVowel);
            var rightAllConsonants = right.All(symbol => !IsVowel(symbol));

            // Standard VCCV spacing covers V C, V CC, CC, and VV aliases.
            // Requiring homogeneous sides avoids classifying ordinary phrases
            // such as "a cat" as phonetic input.
            return (leftAllVowels && rightAllConsonants)
                || (leftAllConsonants && rightAllConsonants)
                || (leftAllVowels && rightAllVowels)
                || (leftAllConsonants && rightAllVowels);
        }
    }
}
