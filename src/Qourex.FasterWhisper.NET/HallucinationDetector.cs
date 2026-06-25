// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Detects and suppresses hallucinated text using multiple signals:
    /// compression ratio, repeated n-grams, token probability entropy,
    /// and audio energy correlation.
    /// </summary>
    public static class HallucinationDetector
    {
        /// <summary>
        /// Analyzes a segment for hallucination indicators.
        /// Returns a score 0.0 (definitely real) to 1.0 (definitely hallucinated).
        /// </summary>
        /// <param name="segment">The segment to analyze.</param>
        /// <param name="audioSlice">The corresponding audio samples for energy analysis. Can be null.</param>
        /// <returns>Hallucination score between 0.0 and 1.0.</returns>
        public static float ComputeHallucinationScore(WhisperSegment segment, float[]? audioSlice = null)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Text))
                return 0f;

            float score = 0f;

            // Signal 1: High compression ratio indicates repetitive text
            score += ComputeCompressionSignal(segment.CompressionRatio) * 0.25f;

            // Signal 2: Repeated n-grams within the segment
            score += DetectRepeatedNgrams(segment.Text) * 0.25f;

            // Signal 3: Low average log probability
            score += ComputeLogProbSignal(segment.AvgLogProb) * 0.20f;

            // Signal 4: High no-speech probability
            score += ComputeNoSpeechSignal(segment.NoSpeechProb) * 0.15f;

            // Signal 5: Text length vs audio energy mismatch
            if (audioSlice != null && audioSlice.Length > 0)
            {
                score += DetectEnergyMismatch(segment, audioSlice) * 0.15f;
            }

            return Math.Clamp(score, 0f, 1f);
        }

        /// <summary>
        /// Checks if a segment is likely hallucinated based on a configurable threshold.
        /// </summary>
        /// <param name="segment">The segment to check.</param>
        /// <param name="threshold">Score threshold above which the segment is considered hallucinated. Default is 0.7.</param>
        /// <param name="audioSlice">Optional audio samples for energy analysis.</param>
        /// <returns>True if hallucination score exceeds threshold.</returns>
        public static bool IsHallucinated(WhisperSegment segment, float threshold = 0.7f, float[]? audioSlice = null)
        {
            return ComputeHallucinationScore(segment, audioSlice) >= threshold;
        }

        /// <summary>
        /// Checks for known hallucination patterns in text (music notes, repeated phrases, etc.).
        /// </summary>
        /// <param name="text">Text to analyze.</param>
        /// <returns>True if a known hallucination pattern is detected.</returns>
        public static bool HasKnownHallucinationPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();

            // Pattern 1: Music notes (♪, ♫) are common hallucinations in silent audio
            if (trimmed.Contains('♪') || trimmed.Contains('♫'))
                return true;

            // Pattern 2: "Thank you for watching" / "Thanks for watching" - common YouTube hallucination
            if (Regex.IsMatch(trimmed, @"(?i)thanks?\s+(for|you\s+for)\s+watching", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                return true;

            // Pattern 3: "Please subscribe" - common YouTube hallucination
            if (Regex.IsMatch(trimmed, @"(?i)please\s+subscribe", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                return true;

            // Pattern 4: Single repeated character or word filling the entire segment
            if (trimmed.Length > 10)
            {
                string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 3)
                {
                    var unique = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
                    if (unique.Count == 1)
                        return true; // All the same word repeated
                }
            }

            // Pattern 5: Only punctuation or whitespace
            if (Regex.IsMatch(trimmed, @"^[\s\p{P}]+$", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                return true;

            return false;
        }

        private static float ComputeCompressionSignal(float compressionRatio)
        {
            // Normal speech: 1.0–2.4, Hallucinated: > 2.4
            if (compressionRatio <= 1.8f) return 0f;
            if (compressionRatio >= 3.0f) return 1f;
            return (compressionRatio - 1.8f) / 1.2f;
        }

        private static float DetectRepeatedNgrams(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0f;

            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 4) return 0f;

            int totalNgrams = 0;
            int repeatedNgrams = 0;

            // Check bigrams and trigrams
            for (int n = 2; n <= 3; n++)
            {
                var ngramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i <= words.Length - n; i++)
                {
                    string ngram = string.Join(" ", words, i, n);
                    if (!ngramCounts.TryGetValue(ngram, out int count))
                        count = 0;
                    ngramCounts[ngram] = count + 1;
                    totalNgrams++;
                }

                foreach (var kvp in ngramCounts)
                {
                    if (kvp.Value > 1)
                        repeatedNgrams += kvp.Value - 1;
                }
            }

            if (totalNgrams == 0) return 0f;
            return Math.Clamp((float)repeatedNgrams / totalNgrams * 2f, 0f, 1f);
        }

        private static float ComputeLogProbSignal(float avgLogProb)
        {
            // Good quality: > -0.5, Suspicious: -0.5 to -1.0, Likely hallucinated: < -1.0
            if (avgLogProb >= -0.5f) return 0f;
            if (avgLogProb <= -1.5f) return 1f;
            return (-avgLogProb - 0.5f) / 1.0f;
        }

        private static float ComputeNoSpeechSignal(float noSpeechProb)
        {
            // High no_speech_prob with text output = likely hallucination
            if (noSpeechProb <= 0.3f) return 0f;
            if (noSpeechProb >= 0.9f) return 1f;
            return (noSpeechProb - 0.3f) / 0.6f;
        }

        private static float DetectEnergyMismatch(WhisperSegment segment, float[] audioSlice)
        {
            // Compute RMS energy of the audio
            double sumSquares = 0;
            for (int i = 0; i < audioSlice.Length; i++)
            {
                sumSquares += audioSlice[i] * (double)audioSlice[i];
            }
            float rms = (float)Math.Sqrt(sumSquares / audioSlice.Length);

            // Very low energy with substantial text = mismatch
            int wordCount = segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            float audioDuration = (float)audioSlice.Length / 16000f;

            if (rms < 0.01f && wordCount > 3)
                return 1f; // Near-silent audio with many words

            // Words per second check: normal speech is 2-4 wps
            if (audioDuration > 0)
            {
                float wps = wordCount / audioDuration;
                if (wps > 8f) return 0.8f; // Unrealistically fast
                if (wps < 0.3f && wordCount > 2) return 0.5f; // Unrealistically slow with content
            }

            return 0f;
        }
    }
}
