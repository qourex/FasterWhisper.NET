// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Represents a transcribed audio segment with text, timestamps, tokens, and model scores.
    /// </summary>
    public class WhisperSegment
    {
        /// <summary>
        /// Gets the transcribed text of the segment.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets the raw token IDs generated for this segment.
        /// </summary>
        public int[] Tokens { get; }

        /// <summary>
        /// Gets the model generation score (average log probability).
        /// </summary>
        public float Score { get; }

        /// <summary>
        /// Gets the probability of the no-speech token for this segment.
        /// </summary>
        public float NoSpeechProb { get; }

        /// <summary>
        /// Gets the start timestamp in seconds relative to the audio chunk.
        /// </summary>
        public float Start { get; set; }

        /// <summary>
        /// Gets the end timestamp in seconds relative to the audio chunk.
        /// </summary>
        public float End { get; set; }

        /// <summary>
        /// Gets the list of aligned words in this segment.
        /// </summary>
        public List<WhisperWord> Words { get; set; } = new();

        /// <summary>
        /// Average log probability of the segment's tokens. Used for quality validation.
        /// </summary>
        public float AvgLogProb { get; set; }

        /// <summary>
        /// Gzip compression ratio of the segment text. High values indicate repetitive/hallucinated text.
        /// </summary>
        public float CompressionRatio { get; set; }

        /// <summary>
        /// Confidence score (0.0–1.0) derived from avg_logprob, no_speech_prob, and compression ratio.
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// Sequential segment index (0-based) within the transcription.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Seek position (in frames) within the audio where this segment was decoded.
        /// </summary>
        public int Seek { get; set; }

        /// <summary>
        /// The sampling temperature used to generate this segment.
        /// </summary>
        public float Temperature { get; set; }

        /// <summary>
        /// Detected language for this segment (populated when Multilingual=true).
        /// Null when language was explicitly specified or single-language detection was used.
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// Hallucination likelihood score (0.0 = definitely real, 1.0 = definitely hallucinated).
        /// Computed by <see cref="HallucinationDetector.ComputeHallucinationScore"/>.
        /// </summary>
        public float HallucinationScore { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WhisperSegment"/> class.
        /// </summary>
        public WhisperSegment(string text, int[] tokens, float score, float noSpeechProb, float start, float end)
        {
            Text = text;
            Tokens = tokens;
            Score = score;
            NoSpeechProb = noSpeechProb;
            Start = start;
            End = end;
        }

        /// <summary>
        /// Creates a WhisperSegment from raw token IDs and extracts timestamps.
        /// </summary>
        internal static WhisperSegment FromTokens(
            int[] tokens,
            float score,
            float noSpeechProb,
            WhisperTokenizer tokenizer,
            float baseOffsetSeconds = 0f)
        {
            string text = tokenizer.Decode(tokens, skipSpecialTokens: true).Trim();

            // Extract start and end timestamps from tokens
            float start = baseOffsetSeconds;
            float end = baseOffsetSeconds;
            bool foundStart = false;

            foreach (int id in tokens)
            {
                string tokenStr = tokenizer.GetTokenString(id);
                if (tokenStr.StartsWith("<|") && tokenStr.EndsWith("|>"))
                {
                    string timeStr = tokenStr.Substring(2, tokenStr.Length - 4);
                    // Ignore non-numeric special tokens like <|startoftranscript|>, <|en|>, etc.
                    if (float.TryParse(timeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                    {
                        if (!foundStart)
                        {
                            start = baseOffsetSeconds + seconds;
                            foundStart = true;
                        }
                        end = baseOffsetSeconds + seconds;
                    }
                }
            }

            return new WhisperSegment(text, tokens, score, noSpeechProb, start, end);
        }
    }

    /// <summary>
    /// Represents an individual aligned word with its timing and probability.
    /// </summary>
    public class WhisperWord
    {
        /// <summary>
        /// The word text.
        /// </summary>
        public string Word { get; set; } = "";

        /// <summary>
        /// Start time in seconds.
        /// </summary>
        public float Start { get; set; }

        /// <summary>
        /// End time in seconds.
        /// </summary>
        public float End { get; set; }

        /// <summary>
        /// The alignment/token probability.
        /// </summary>
        public float Probability { get; set; }

        /// <summary>
        /// Per-word confidence score (0.0–1.0) derived from alignment probability.
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// Flagged if confidence is below threshold (default 0.5).
        /// </summary>
        public bool IsLowConfidence { get; set; }
    }
}
