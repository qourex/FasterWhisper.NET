// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class SegmentMergerTests
    {
        private static WhisperSegment Seg(float start, float end, string text) =>
            new(text, Array.Empty<int>(), 0f, 0f, start, end);

        [Fact]
        public void MergeIntoParagraphs_MergesCloseSegments()
        {
            var segments = new List<WhisperSegment>
            {
                Seg(0.0f, 2.0f, "Hello world."),
                Seg(2.3f, 4.0f, "How are you?"),
                Seg(4.2f, 6.0f, "I'm fine."),
                Seg(10.0f, 12.0f, "New paragraph.")
            };

            var merged = SegmentMerger.MergeIntoParagraphs(segments, maxPauseSeconds: 1.0f);

            Assert.Equal(2, merged.Count);
            Assert.Contains("Hello world.", merged[0].Text);
            Assert.Contains("How are you?", merged[0].Text);
            Assert.Contains("I'm fine.", merged[0].Text);
            Assert.Equal("New paragraph.", merged[1].Text.Trim());
        }

        [Fact]
        public void MergeIntoParagraphs_RespectsMaxWords()
        {
            var segments = new List<WhisperSegment>();
            for (int i = 0; i < 20; i++)
            {
                segments.Add(Seg(i * 1.0f, i * 1.0f + 0.9f, $"Word{i} another more."));
            }

            var merged = SegmentMerger.MergeIntoParagraphs(segments, maxPauseSeconds: 1.0f, maxWordsPerParagraph: 10);
            Assert.True(merged.Count > 1);
        }

        [Fact]
        public void SplitIntoSentences_SplitsAtPeriods()
        {
            var segments = new List<WhisperSegment>
            {
                Seg(0.0f, 5.0f, "First sentence. Second sentence. Third sentence.")
            };

            var split = SegmentMerger.SplitIntoSentences(segments);
            Assert.True(split.Count >= 2);
        }

        [Fact]
        public void MergeIntoParagraphs_EmptyInput_ReturnsEmpty()
        {
            var merged = SegmentMerger.MergeIntoParagraphs(new List<WhisperSegment>());
            Assert.Empty(merged);
        }

        [Fact]
        public void MergeIntoParagraphs_SingleSegment_ReturnsSame()
        {
            var segments = new List<WhisperSegment>
            {
                Seg(0.0f, 2.0f, "Only segment.")
            };

            var merged = SegmentMerger.MergeIntoParagraphs(segments);
            Assert.Single(merged);
            Assert.Equal("Only segment.", merged[0].Text.Trim());
        }

        [Fact]
        public void SplitIntoSentences_DistributesWordTimestamps()
        {
            var parent = Seg(0.0f, 5.0f, "Hello world. How are you?");
            parent.Words = new List<WhisperWord>
            {
                new() { Word = "Hello", Start = 0.1f, End = 0.5f, Probability = 0.9f },
                new() { Word = "world.", Start = 0.6f, End = 1.0f, Probability = 0.95f },
                new() { Word = "How", Start = 1.5f, End = 2.0f, Probability = 0.88f },
                new() { Word = "are", Start = 2.1f, End = 2.5f, Probability = 0.92f },
                new() { Word = "you?", Start = 2.6f, End = 3.0f, Probability = 0.99f }
            };

            var split = SegmentMerger.SplitIntoSentences(new[] { parent });

            Assert.Equal(2, split.Count);

            // First sentence: "Hello world."
            Assert.Equal("Hello world.", split[0].Text);
            Assert.NotNull(split[0].Words);
            Assert.Equal(2, split[0].Words.Count);
            Assert.Equal("Hello", split[0].Words[0].Word);
            Assert.Equal("world.", split[0].Words[1].Word);

            // Second sentence: "How are you?"
            Assert.Equal("How are you?", split[1].Text);
            Assert.NotNull(split[1].Words);
            Assert.Equal(3, split[1].Words.Count);
            Assert.Equal("How", split[1].Words[0].Word);
            Assert.Equal("are", split[1].Words[1].Word);
            Assert.Equal("you?", split[1].Words[2].Word);
        }
    }
}
