// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Metadata about a completed transcription, returned alongside segments.
    /// Matches Python faster-whisper's TranscriptionInfo dataclass.
    /// </summary>
    public class TranscriptionInfo
    {
        /// <summary>Detected or specified language code (e.g. "en").</summary>
        public string Language { get; init; } = "";

        /// <summary>Language detection probability (0.0–1.0).</summary>
        public float LanguageProbability { get; init; }

        /// <summary>Total audio duration in seconds.</summary>
        public float Duration { get; init; }

        /// <summary>Audio duration after VAD filtering (seconds). Equals Duration if VAD disabled.</summary>
        public float DurationAfterVad { get; init; }

        /// <summary>All detected languages with their probabilities (top-N).</summary>
        public List<(string Language, float Probability)>? AllLanguageProbs { get; init; }

        /// <summary>The transcription options used.</summary>
        public WhisperOptions TranscriptionOptions { get; init; } = new();

        /// <summary>The VAD options used.</summary>
        public VadOptions VadOptions { get; init; } = new();
    }

    /// <summary>
    /// Contains both the transcription segments and metadata info,
    /// matching Python's return type: (Iterable[Segment], TranscriptionInfo).
    /// </summary>
    public class TranscriptionResult
    {
        /// <summary>The transcribed segments.</summary>
        public IEnumerable<WhisperSegment> Segments { get; init; } = [];

        /// <summary>Transcription metadata (language, duration, options used).</summary>
        public TranscriptionInfo Info { get; init; } = new();

        /// <summary>Detailed timing breakdown. Populated when diagnostics are enabled.</summary>
        public TranscriptionDiagnostics? Diagnostics { get; init; }
    }

    /// <summary>
    /// Progress information reported during transcription.
    /// </summary>
    public class TranscriptionProgress
    {
        /// <summary>Current position in seconds.</summary>
        public float CurrentSeconds { get; init; }

        /// <summary>Total audio duration in seconds.</summary>
        public float TotalSeconds { get; init; }

        /// <summary>Progress percentage (0–100).</summary>
        public float Percent => TotalSeconds > 0 ? (CurrentSeconds / TotalSeconds) * 100f : 0f;
    }
}
