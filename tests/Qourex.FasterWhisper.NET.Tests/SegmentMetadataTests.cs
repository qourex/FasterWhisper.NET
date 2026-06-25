// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class SegmentMetadataTests
    {
        private static WhisperSegment Seg() =>
            new("test", Array.Empty<int>(), 0f, 0f, 0f, 1f);

        [Fact]
        public void WhisperSegment_Id_DefaultZero()
        {
            var seg = Seg();
            Assert.Equal(0, seg.Id);
        }

        [Fact]
        public void WhisperSegment_Id_CanBeSet()
        {
            var seg = Seg();
            seg.Id = 42;
            Assert.Equal(42, seg.Id);
        }

        [Fact]
        public void WhisperSegment_Seek_CanBeSet()
        {
            var seg = Seg();
            seg.Seek = 1000;
            Assert.Equal(1000, seg.Seek);
        }

        [Fact]
        public void WhisperSegment_Temperature_CanBeSet()
        {
            var seg = Seg();
            seg.Temperature = 0.4f;
            Assert.Equal(0.4f, seg.Temperature);
        }

        [Fact]
        public void WhisperSegment_Language_CanBeSet()
        {
            var seg = Seg();
            seg.Language = "fr";
            Assert.Equal("fr", seg.Language);
        }

        [Fact]
        public void WhisperSegment_HallucinationScore_Range()
        {
            var seg = Seg();
            seg.HallucinationScore = 0.5f;
            Assert.True(seg.HallucinationScore >= 0f && seg.HallucinationScore <= 1f);
        }

        [Fact]
        public void WhisperWord_Confidence_CanBeSet()
        {
            var word = new WhisperWord { Word = "test", Confidence = 0.95f };
            Assert.Equal(0.95f, word.Confidence);
            Assert.False(word.IsLowConfidence);
        }

        [Fact]
        public void WhisperWord_IsLowConfidence_WhenBelow05()
        {
            var word = new WhisperWord { Word = "test", Confidence = 0.3f, IsLowConfidence = true };
            Assert.True(word.IsLowConfidence);
        }
    }
}
