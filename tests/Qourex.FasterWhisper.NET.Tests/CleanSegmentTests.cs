// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Tests for CleanSegmentTextAndWords edge cases (TEST-4).
    /// </summary>
    public class CleanSegmentTests
    {
        private static WhisperSegment CreateSegment(string text, List<WhisperWord>? words = null)
        {
            var segment = new WhisperSegment(text, System.Array.Empty<int>(), 0f, 0f, 0f, 1f);
            if (words != null)
                segment.Words = words;
            return segment;
        }

        [Fact]
        public void CleanSegment_EmptyWordsList_DoesNotThrow()
        {
            var segment = CreateSegment("", new List<WhisperWord>());
            var options = new WhisperOptions { FilterFillerWords = true, PruneStutters = true };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            Assert.Empty(segment.Text);
            Assert.Empty(segment.Words);
        }

        [Fact]
        public void CleanSegment_AllFillerWords_CleansToEmpty()
        {
            var segment = CreateSegment("uh um ah", new List<WhisperWord>
            {
                new() { Word = "uh", Start = 0, End = 0.5f, Probability = 0.9f },
                new() { Word = "um", Start = 0.5f, End = 1.0f, Probability = 0.9f },
                new() { Word = "ah", Start = 1.0f, End = 1.5f, Probability = 0.9f },
            });
            var options = new WhisperOptions { FilterFillerWords = true };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            Assert.Empty(segment.Words);
            Assert.Equal("", segment.Text.Trim());
        }

        [Fact]
        public void CleanSegment_SingleWord_NoFillers_Unchanged()
        {
            var segment = CreateSegment("hello", new List<WhisperWord>
            {
                new() { Word = "hello", Start = 0, End = 0.5f, Probability = 0.9f }
            });
            var options = new WhisperOptions { FilterFillerWords = true, PruneStutters = true };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            Assert.Single(segment.Words);
            Assert.Contains("hello", segment.Text);
        }

        [Fact]
        public void CleanSegment_NoDuplicates_NoPruning()
        {
            var segment = CreateSegment("the quick brown fox", new List<WhisperWord>
            {
                new() { Word = "the", Start = 0, End = 0.2f, Probability = 0.9f },
                new() { Word = "quick", Start = 0.2f, End = 0.5f, Probability = 0.9f },
                new() { Word = "brown", Start = 0.5f, End = 0.8f, Probability = 0.9f },
                new() { Word = "fox", Start = 0.8f, End = 1.0f, Probability = 0.9f },
            });
            var options = new WhisperOptions { PruneStutters = true };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            Assert.Equal(4, segment.Words.Count);
        }

        [Fact]
        public void CleanSegment_ConsecutiveDuplicates_PrunesStutters()
        {
            var segment = CreateSegment("the the the fox", new List<WhisperWord>
            {
                new() { Word = "the", Start = 0, End = 0.2f, Probability = 0.9f },
                new() { Word = "the", Start = 0.2f, End = 0.4f, Probability = 0.9f },
                new() { Word = "the", Start = 0.4f, End = 0.6f, Probability = 0.9f },
                new() { Word = "fox", Start = 0.6f, End = 1.0f, Probability = 0.9f },
            });
            var options = new WhisperOptions { PruneStutters = true };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            // Should keep only one "the" + "fox"
            Assert.Equal(2, segment.Words.Count);
            Assert.Equal("the", segment.Words[0].Word);
            Assert.Equal("fox", segment.Words[1].Word);
        }

        [Fact]
        public void CleanSegment_NullWords_DoesNotThrow()
        {
            var segment = CreateSegment("hello");
            segment.Words = null!;
            var options = new WhisperOptions { FilterFillerWords = true, PruneStutters = true };

            // Should not throw even with null Words
            WhisperModel.CleanSegmentTextAndWords(segment, options);
        }
    }
}
