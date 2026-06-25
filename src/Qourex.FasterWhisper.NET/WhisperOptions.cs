// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Configuration options for Whisper model generation (decoding parameters).
    /// </summary>
    public class WhisperOptions
    {
        /// <summary>
        /// Beam size to use for beam search (set 1 to run greedy search). Default is 5.
        /// </summary>
        public int BeamSize { get; set; } = 5;

        /// <summary>
        /// Beam search patience factor. The decoding will continue until beam_size*patience hypotheses are finished. Default is 1.0.
        /// </summary>
        public float Patience { get; set; } = 1.0f;

        /// <summary>
        /// Exponential penalty applied to the length during beam search. Default is 1.0.
        /// </summary>
        public float LengthPenalty { get; set; } = 1.0f;

        /// <summary>
        /// Penalty applied to the score of previously generated tokens (set > 1 to penalize). Default is 1.0.
        /// </summary>
        public float RepetitionPenalty { get; set; } = 1.0f;

        /// <summary>
        /// Prevent repetitions of ngrams with this size (set 0 to disable). Default is 0.
        /// </summary>
        public int NoRepeatNgramSize { get; set; } = 0;

        /// <summary>
        /// Maximum generation length. Default is 448.
        /// </summary>
        public int MaxLength { get; set; } = 448;

        /// <summary>
        /// Randomly sample from the top K candidates (set 0 to sample from the full distribution). Default is 1 (greedy).
        /// </summary>
        public int SamplingTopK { get; set; } = 1;

        /// <summary>
        /// High temperatures increase randomness. Default is 1.0.
        /// </summary>
        public float SamplingTemperature { get; set; } = 1.0f;

        /// <summary>
        /// Number of hypotheses to include in the result. Default is 1.
        /// </summary>
        public int NumHypotheses { get; set; } = 1;

        /// <summary>
        /// Include scores in the result. Default is true.
        /// </summary>
        public bool ReturnScores { get; set; } = true;

        /// <summary>
        /// Include the probability of the no speech token in the result. Default is true.
        /// </summary>
        public bool ReturnNoSpeechProb { get; set; } = true;

        /// <summary>
        /// Maximum index of the first predicted timestamp. Default is 50.
        /// </summary>
        public int MaxInitialTimestampIndex { get; set; } = 50;

        /// <summary>
        /// Suppress blank outputs at the beginning of the sampling. Default is true.
        /// </summary>
        public bool SuppressBlank { get; set; } = true;

        /// <summary>
        /// List of token IDs to suppress during generation. If null, uses the model default config.
        /// </summary>
        public int[]? SuppressTokens { get; set; } = new int[] { -1 };

        /// <summary>
        /// Extract word-level timestamps using cross-attention alignment. Default is false.
        /// </summary>
        public bool WordTimestamps { get; set; } = false;

        /// <summary>
        /// Width of the median filter applied to the cross-attention matrix during word alignment. Default is 7.
        /// </summary>
        public int MedianFilterWidth { get; set; } = 7;

        /// <summary>
        /// Temperatures sequence to fall back to if the decoding results fail thresholds. Default is [0.0, 0.2, 0.4, 0.6, 0.8, 1.0].
        /// </summary>
        public float[] Temperatures { get; set; } = new float[] { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

        /// <summary>
        /// If the average log probability of the segment is below this threshold, it fails validation. Default is -1.0.
        /// </summary>
        public float LogProbThreshold { get; set; } = -1.0f;

        /// <summary>
        /// If the probability of the no-speech token is above this threshold, the segment is considered silence. Default is 0.6.
        /// </summary>
        public float NoSpeechThreshold { get; set; } = 0.6f;

        /// <summary>
        /// If the gzip text compression ratio of the segment is above this threshold, it fails validation due to repetitive text. Default is 2.4.
        /// </summary>
        public float CompressionRatioThreshold { get; set; } = 2.4f;

        /// <summary>
        /// Optional string prefix to guide the model's transcription of the first chunk.
        /// </summary>
        public string? Prefix { get; set; }

        /// <summary>
        /// Suppress timestamp tokens during generation. Default is false.
        /// </summary>
        public bool WithoutTimestamps { get; set; } = false;

        /// <summary>
        /// Enable RMS volume normalization. Default is true.
        /// </summary>
        public bool NormalizeAudio { get; set; } = true;

        /// <summary>
        /// Apply high-pass filter cutting off frequencies below 80Hz (DC offset and microphone hum). Default is true.
        /// </summary>
        public bool CutLowFrequencies { get; set; } = true;

        /// <summary>
        /// Condition the model's next chunk decoding on the previous chunk's transcribed text tokens. Default is true.
        /// </summary>
        public bool ConditionOnPreviousText { get; set; } = true;

        /// <summary>
        /// Filters out common filler words (like 'uh', 'um', 'ah') from word timestamps and segment text. Default is false.
        /// </summary>
        public bool FilterFillerWords { get; set; } = false;

        /// <summary>
        /// Prunes consecutive duplicate/repeated words (stuttering) from word timestamps and segment text. Default is false.
        /// </summary>
        public bool PruneStutters { get; set; } = false;

        /// <summary>
        /// Optional initial prompt to condition the model's first chunk. Useful for domain-specific vocabulary,
        /// spelling guidance, or style (e.g., "Meeting transcript about quantum computing.").
        /// Tokens are prepended after &lt;|startofprev|&gt; as context.
        /// </summary>
        public string? InitialPrompt { get; set; }

        /// <summary>
        /// Comma-separated words to boost in transcription. Prepended as context to the prompt.
        /// Example: "TensorFlow, CUDA, Qourex"
        /// </summary>
        public string? Hotwords { get; set; }

        /// <summary>
        /// If greater than 0, skip silent sections longer than this threshold (in seconds)
        /// when hallucination is detected via word timestamp gaps.
        /// Set to 0 to disable. Recommended value: 2.0.
        /// </summary>
        public float HallucinationSilenceThreshold { get; set; } = 0f;

        /// <summary>
        /// Punctuation marks that should be prepended to the following word during word timestamp merging.
        /// Default matches faster-whisper Python.
        /// </summary>
        public string PrependPunctuations { get; set; } = "\"'\u201C\u00BF([{-";

        /// <summary>
        /// Punctuation marks that should be appended to the preceding word during word timestamp merging.
        /// Default matches faster-whisper Python.
        /// </summary>
        public string AppendPunctuations { get; set; } = "\"'.\u3002,\uFF0C!\uFF01?\uFF1F:\uFF1A\u201D)]}\u3001";

        /// <summary>
        /// Maximum number of new tokens to generate per chunk. If 0, uses MaxLength. Default is 0.
        /// </summary>
        public int MaxNewTokens { get; set; } = 0;

        /// <summary>
        /// Apply pre-emphasis filter (y[n] = x[n] - 0.97*x[n-1]) to boost high-frequency consonant clarity.
        /// Default is false.
        /// </summary>
        public bool PreEmphasis { get; set; } = false;

        /// <summary>
        /// Apply spectral noise gate to reduce background noise before transcription.
        /// Default is false.
        /// </summary>
        public bool DenoiseAudio { get; set; } = false;

        /// <summary>
        /// Number of candidates to sample when using non-zero temperature.
        /// The model generates this many hypotheses and returns the best one by score.
        /// Only used when temperature &gt; 0. Default is 5 (matching Python faster-whisper).
        /// </summary>
        public int BestOf { get; set; } = 5;

        /// <summary>
        /// When temperature fallback is triggered (validation failed), reset the prompt context
        /// if the current temperature exceeds this threshold. Prevents the model from getting
        /// stuck in repetition loops by clearing previous-text conditioning.
        /// Set to 0 to disable. Default is 0.5 (matching Python faster-whisper).
        /// </summary>
        public float PromptResetOnTemperature { get; set; } = 0.5f;

        /// <summary>
        /// Optional list of timestamp pairs (start, end) in seconds to restrict transcription
        /// to specific time ranges within the audio. Each pair defines a clip to transcribe.
        /// If set, VAD is bypassed and only the specified clips are processed.
        /// </summary>
        public List<(float Start, float End)>? ClipTimestamps { get; set; }

        /// <summary>
        /// When true, perform language detection on each 30-second chunk independently.
        /// Useful for audio containing multiple languages (e.g., multilingual meetings).
        /// The detected language is stored in each segment's Language property.
        /// Default is false (detect once from first chunk).
        /// </summary>
        public bool Multilingual { get; set; } = false;

        /// <summary>
        /// When true (default), use greedy decoding (beam=1) at temperature 0 and full beam
        /// only on retry at higher temperatures. Provides 3-5x speedup for first-pass decoding.
        /// Set to false to always use BeamSize even at temp=0.
        /// </summary>
        public bool AdaptiveBeamSize { get; set; } = true;

        /// <summary>
        /// Apply punctuation and capitalization restoration to transcribed text.
        /// Uses rule-based restoration: capitalize after sentence-ending punctuation,
        /// fix standalone "i" → "I", add missing periods.
        /// Default is false.
        /// </summary>
        public bool RestoreTextFormatting { get; set; } = false;

        /// <summary>
        /// Dictionary of words/phrases to boost during decoding, with bias strength.
        /// Implemented via enhanced prompt injection for domain-specific terminology.
        /// Example: { ["CUDA"] = 5.0f, ["TensorFlow"] = 3.0f }
        /// </summary>
        public Dictionary<string, float>? VocabularyBias { get; set; }

        /// <summary>
        /// Enable second-pass re-transcription of low-confidence segments.
        /// Low-confidence segments are retranscribed with higher beam size.
        /// Default is false.
        /// </summary>
        public bool MultiPassEnabled { get; set; } = false;

        /// <summary>
        /// Confidence threshold below which segments are re-transcribed in multi-pass mode.
        /// Default is 0.6.
        /// </summary>
        public float MultiPassConfidenceThreshold { get; set; } = 0.6f;

        /// <summary>
        /// Beam size used for multi-pass re-transcription of low-confidence segments.
        /// Default is 10.
        /// </summary>
        public int MultiPassBeamSize { get; set; } = 10;
    }

    /// <summary>
    /// VAD (Voice Activity Detection) configuration options.
    /// </summary>
    public class VadOptions
    {
        /// <summary>
        /// Enable or disable Voice Activity Detection. Default is false (VAD is bypassed).
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Probability threshold above which a frame is considered speech. Default is 0.5.
        /// </summary>
        public float Threshold { get; set; } = 0.5f;

        /// <summary>
        /// Minimum duration of active speech to keep a segment (in milliseconds). Default is 250.
        /// </summary>
        public int MinSpeechDurationMs { get; set; } = 250;

        /// <summary>
        /// Minimum duration of silence to split speech segments (in milliseconds). Default is 2000.
        /// </summary>
        public int MinSilenceDurationMs { get; set; } = 2000;

        /// <summary>
        /// Maximum duration of a single speech segment in seconds.
        /// Segments longer than this are force-split at the nearest silence.
        /// Default is 0 (disabled — no maximum). Recommended: 30.
        /// </summary>
        public float MaxSpeechDurationS { get; set; } = 0f;

        /// <summary>
        /// Padding in milliseconds added to the start and end of each detected speech segment.
        /// Prevents clipping of speech onset/offset. Default is 400 (matching Python faster-whisper).
        /// </summary>
        public int SpeechPadMs { get; set; } = 400;
    }
}
