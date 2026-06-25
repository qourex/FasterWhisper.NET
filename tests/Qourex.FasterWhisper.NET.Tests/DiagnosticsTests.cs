// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class DiagnosticsTests
    {
        [Fact]
        public void TranscriptionDiagnostics_RealTimeFactor_Computed()
        {
            var diag = new TranscriptionDiagnostics
            {
                TotalMs = 500,
                AudioDurationMs = 1000,
                ChunksProcessed = 1,
                SegmentsProduced = 3
            };

            Assert.Equal(0.5, diag.RealTimeFactor, 2);
        }

        [Fact]
        public void TranscriptionDiagnostics_ZeroAudio_RTFZero()
        {
            var diag = new TranscriptionDiagnostics
            {
                TotalMs = 100,
                AudioDurationMs = 0
            };

            Assert.Equal(0, diag.RealTimeFactor);
        }

        [Fact]
        public void TranscriptionDiagnostics_AllTimings_Set()
        {
            var diag = new TranscriptionDiagnostics
            {
                AudioLoadMs = 10,
                PreprocessingMs = 20,
                VadMs = 30,
                MelComputeMs = 40,
                EncoderMs = 100,
                DecoderMs = 200,
                WordAlignMs = 50,
                PostProcessMs = 10,
                TotalMs = 460,
                AudioDurationMs = 5000,
                ChunksProcessed = 1,
                TemperatureRetries = 0,
                SegmentsProduced = 5,
                PeakMemoryBytes = 1024 * 1024
            };

            Assert.Equal(10, diag.AudioLoadMs);
            Assert.Equal(200, diag.DecoderMs);
            Assert.Equal(1024 * 1024, diag.PeakMemoryBytes);
        }

        [Fact]
        public void TranscriptionDiagnostics_ToString_ContainsKey()
        {
            var diag = new TranscriptionDiagnostics
            {
                TotalMs = 1000,
                AudioDurationMs = 5000,
                ChunksProcessed = 1,
                SegmentsProduced = 3
            };

            string str = diag.ToString();
            Assert.Contains("Total:", str);
            Assert.Contains("RTF:", str);
            Assert.Contains("Segments:", str);
        }

        [Fact]
        public void TranscriptionResult_Diagnostics_CanBeNull()
        {
            var result = new TranscriptionResult
            {
                Segments = new List<WhisperSegment>(),
                Info = new TranscriptionInfo
                {
                    Language = "en",
                    LanguageProbability = 1.0f,
                    Duration = 5.0f,
                    DurationAfterVad = 4.0f,
                    TranscriptionOptions = new WhisperOptions(),
                    VadOptions = new VadOptions()
                }
            };

            Assert.Null(result.Diagnostics);
        }
    }
}
