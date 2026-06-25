// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class AdaptiveBeamTests
    {
        [Fact]
        public void AdaptiveBeamSize_DefaultTrue()
        {
            var options = new WhisperOptions();
            Assert.True(options.AdaptiveBeamSize);
        }

        [Fact]
        public void AdaptiveBeamSize_CanBeDisabled()
        {
            var options = new WhisperOptions { AdaptiveBeamSize = false };
            Assert.False(options.AdaptiveBeamSize);
        }

        [Fact]
        public void BeamSize_DefaultIs5()
        {
            var options = new WhisperOptions();
            Assert.Equal(5, options.BeamSize);
        }
    }
}
