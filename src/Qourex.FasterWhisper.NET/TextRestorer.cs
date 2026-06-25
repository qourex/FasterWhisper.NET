// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Restores proper punctuation and capitalization in transcribed text.
    /// Whisper often outputs lowercase text without proper sentence-ending punctuation.
    /// </summary>
    public static class TextRestorer
    {
        private static readonly Regex SentenceEndRegex = new(@"([.!?])\s+(\w)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
        private static readonly Regex StandaloneIRegex = new(@"\bi\b", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
        private static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
        private static readonly Regex EllipsisRegex = new(@"\.{2,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

        /// <summary>
        /// Capitalizes after sentence-ending punctuation (.!?) and at start of text.
        /// Also fixes standalone "i" → "I".
        /// </summary>
        /// <param name="text">The text to restore capitalization on.</param>
        /// <returns>Text with restored capitalization.</returns>
        public static string RestoreCapitalization(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var sb = new StringBuilder(text.Length);
            bool capitalizeNext = true;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (capitalizeNext && char.IsLetter(c))
                {
                    sb.Append(char.ToUpper(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }

                if (c == '.' || c == '!' || c == '?')
                {
                    capitalizeNext = true;
                }
            }

            // Fix standalone "i" → "I"
            string result = StandaloneIRegex.Replace(sb.ToString(), "I");

            return result;
        }

        /// <summary>
        /// Adds missing periods at the end of segments that don't end with punctuation.
        /// </summary>
        /// <param name="text">The text to add punctuation to.</param>
        /// <returns>Text with restored end punctuation.</returns>
        public static string RestorePunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string trimmed = text.TrimEnd();

            // If already ends with sentence-ending punctuation, return as-is
            if (trimmed.Length > 0)
            {
                char last = trimmed[^1];
                if (last == '.' || last == '!' || last == '?' || last == '…' ||
                    last == ':' || last == ';' || last == '"' || last == '\'')
                {
                    return trimmed;
                }
            }

            // Add period at end
            return trimmed + ".";
        }

        /// <summary>
        /// Normalizes formatting: quotation marks, dashes, ellipsis, multiple spaces.
        /// </summary>
        /// <param name="text">The text to normalize.</param>
        /// <returns>Normalized text.</returns>
        public static string NormalizeFormatting(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string result = text;

            // Normalize curly quotes to straight quotes
            result = result.Replace('\u201C', '"').Replace('\u201D', '"'); // " "
            result = result.Replace('\u2018', '\'').Replace('\u2019', '\''); // ' '

            // Normalize em-dash and en-dash
            result = result.Replace('\u2014', '-').Replace('\u2013', '-'); // — –

            // Normalize ellipsis
            result = EllipsisRegex.Replace(result, "…");
            result = result.Replace("...", "…");

            // Collapse multiple spaces
            result = MultipleSpacesRegex.Replace(result, " ");

            // Trim leading/trailing whitespace
            result = result.Trim();

            return result;
        }

        /// <summary>
        /// Full restore pipeline: normalize → capitalize → punctuate.
        /// </summary>
        /// <param name="text">The text to fully restore.</param>
        /// <returns>Fully restored text.</returns>
        public static string Restore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string result = NormalizeFormatting(text);
            result = RestoreCapitalization(result);
            result = RestorePunctuation(result);

            return result;
        }

        /// <summary>
        /// Applies text restoration to a segment's text and optionally its words.
        /// </summary>
        /// <param name="segment">The segment to restore.</param>
        public static void RestoreSegment(WhisperSegment segment)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Text)) return;
            segment.Text = Restore(segment.Text);
        }
    }
}
