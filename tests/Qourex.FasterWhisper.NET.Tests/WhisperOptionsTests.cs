// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Tests for WhisperOptions and VadOptions default values.
    /// </summary>
    public class WhisperOptionsTests
    {
        [Fact]
        public void WhisperOptions_DefaultValues_AreCorrect()
        {
            var options = new WhisperOptions();

            Assert.Equal(5, options.BeamSize);
            Assert.Equal(1.0f, options.Patience);
            Assert.Equal(1.0f, options.LengthPenalty);
            Assert.True(options.NormalizeAudio);
            Assert.True(options.CutLowFrequencies);
            Assert.False(options.PreEmphasis);
            Assert.False(options.DenoiseAudio);
            Assert.False(options.FilterFillerWords);
            Assert.False(options.PruneStutters);
        }

        [Fact]
        public void VadOptions_DefaultValues_AreCorrect()
        {
            var options = new VadOptions();

            Assert.False(options.Enabled);
            Assert.Equal(0.5f, options.Threshold);
        }
    }
}
