// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class HallucinationDetectorTests
    {
        private static WhisperSegment Seg(string text, float compressionRatio, float avgLogProb,
            float noSpeechProb, float start, float end) =>
            new(text, Array.Empty<int>(), 0f, noSpeechProb, start, end)
            {
                CompressionRatio = compressionRatio,
                AvgLogProb = avgLogProb
            };

        [Fact]
        public void ComputeHallucinationScore_NormalText_LowScore()
        {
            var segment = Seg("The quick brown fox jumps over the lazy dog.", 1.2f, -0.3f, 0.01f, 0f, 5f);
            float score = HallucinationDetector.ComputeHallucinationScore(segment, new float[16000 * 5]);
            Assert.True(score < 0.5f, $"Expected low hallucination score, got {score}");
        }

        [Fact]
        public void ComputeHallucinationScore_HighCompressionRatio_HigherScore()
        {
            var segment = Seg("test test test test test test test test", 3.5f, -0.5f, 0.1f, 0f, 5f);
            float score = HallucinationDetector.ComputeHallucinationScore(segment, new float[16000 * 5]);
            Assert.True(score > 0.2f, $"Expected elevated score for repeated text, got {score}");
        }

        [Fact]
        public void ComputeHallucinationScore_LowLogProb_IncreasesScore()
        {
            var segment = Seg("Some text here.", 1.0f, -2.0f, 0.01f, 0f, 3f);
            float score = HallucinationDetector.ComputeHallucinationScore(segment, new float[16000 * 3]);
            Assert.True(score > 0.1f, $"Expected increased score for low log prob, got {score}");
        }

        [Fact]
        public void IsHallucinated_DefaultThreshold_DetectsHighScore()
        {
            var segment = Seg("Thanks for watching Thanks for watching", 4.0f, -1.5f, 0.8f, 0f, 2f);
            segment.HallucinationScore = 0.85f;
            Assert.True(segment.HallucinationScore > 0.7f);
        }

        [Fact]
        public void ComputeHallucinationScore_EmptyText_HandledGracefully()
        {
            var segment = Seg("", 0f, 0f, 0f, 0f, 1f);
            float score = HallucinationDetector.ComputeHallucinationScore(segment, new float[16000]);
            Assert.True(score >= 0f && score <= 1f);
        }
    }
}
