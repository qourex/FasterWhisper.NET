// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Detailed timing breakdown of a transcription run.
    /// Provides per-stage profiling to identify bottlenecks.
    /// </summary>
    public class TranscriptionDiagnostics
    {
        /// <summary>Time spent loading and decoding audio file (ms).</summary>
        public double AudioLoadMs { get; init; }

        /// <summary>Time spent on audio preprocessing: normalize, filter, denoise (ms).</summary>
        public double PreprocessingMs { get; init; }

        /// <summary>Time spent on VAD segmentation (ms). 0 if VAD disabled.</summary>
        public double VadMs { get; init; }

        /// <summary>Time spent computing Mel spectrograms (ms).</summary>
        public double MelComputeMs { get; init; }

        /// <summary>Time spent in the encoder (ms).</summary>
        public double EncoderMs { get; init; }

        /// <summary>Time spent in the decoder, including temperature fallback (ms).</summary>
        public double DecoderMs { get; init; }

        /// <summary>Time spent on word alignment / cross-attention (ms).</summary>
        public double WordAlignMs { get; init; }

        /// <summary>Time spent on post-processing: filler words, stutters, text restoration (ms).</summary>
        public double PostProcessMs { get; init; }

        /// <summary>Total wall-clock time for the entire transcription (ms).</summary>
        public double TotalMs { get; init; }

        /// <summary>Duration of the input audio (ms).</summary>
        public double AudioDurationMs { get; init; }

        /// <summary>
        /// Real-time factor: ratio of processing time to audio duration.
        /// Values &lt; 1.0 mean faster than real-time.
        /// </summary>
        public double RealTimeFactor => AudioDurationMs > 0 ? TotalMs / AudioDurationMs : 0;

        /// <summary>Number of audio chunks processed.</summary>
        public int ChunksProcessed { get; init; }

        /// <summary>Number of temperature retries across all chunks.</summary>
        public int TemperatureRetries { get; init; }

        /// <summary>Total segments produced.</summary>
        public int SegmentsProduced { get; init; }

        /// <summary>Peak memory usage during transcription (bytes). 0 if not measured.</summary>
        public long PeakMemoryBytes { get; init; }

        /// <summary>Returns a human-readable summary of the diagnostics.</summary>
        public override string ToString()
        {
            return $"Total: {TotalMs:F1}ms | RTF: {RealTimeFactor:F2}x | " +
                   $"Audio: {AudioLoadMs:F1}ms | Preproc: {PreprocessingMs:F1}ms | " +
                   $"VAD: {VadMs:F1}ms | Mel: {MelComputeMs:F1}ms | " +
                   $"Decode: {DecoderMs:F1}ms | Align: {WordAlignMs:F1}ms | " +
                   $"PostProc: {PostProcessMs:F1}ms | " +
                   $"Chunks: {ChunksProcessed} | Retries: {TemperatureRetries} | Segments: {SegmentsProduced}";
        }
    }
}
