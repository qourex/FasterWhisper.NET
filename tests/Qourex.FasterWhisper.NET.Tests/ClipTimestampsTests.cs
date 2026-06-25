// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class ClipTimestampsTests
    {
        [Fact]
        public void ClipTimestamps_DefaultNull()
        {
            var options = new WhisperOptions();
            Assert.Null(options.ClipTimestamps);
        }

        [Fact]
        public void ClipTimestamps_CanBeSet()
        {
            var options = new WhisperOptions
            {
                ClipTimestamps = new List<(float Start, float End)>
                {
                    (0.0f, 10.0f),
                    (30.0f, 60.0f)
                }
            };

            Assert.NotNull(options.ClipTimestamps);
            Assert.Equal(2, options.ClipTimestamps.Count);
            Assert.Equal(0.0f, options.ClipTimestamps[0].Start);
            Assert.Equal(10.0f, options.ClipTimestamps[0].End);
        }

        [Fact]
        public void ClipTimestamps_EmptyList()
        {
            var options = new WhisperOptions
            {
                ClipTimestamps = new List<(float Start, float End)>()
            };

            Assert.NotNull(options.ClipTimestamps);
            Assert.Empty(options.ClipTimestamps);
        }
    }
}
