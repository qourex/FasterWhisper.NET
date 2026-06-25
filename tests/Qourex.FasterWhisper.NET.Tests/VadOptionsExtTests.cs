// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class VadOptionsExtTests
    {
        [Fact]
        public void MaxSpeechDurationS_Default_IsZero()
        {
            var options = new VadOptions();
            Assert.Equal(0f, options.MaxSpeechDurationS);
        }

        [Fact]
        public void MaxSpeechDurationS_CanBeSet()
        {
            var options = new VadOptions { MaxSpeechDurationS = 30.0f };
            Assert.Equal(30.0f, options.MaxSpeechDurationS);
        }

        [Fact]
        public void SpeechPadMs_Default_Is400()
        {
            var options = new VadOptions();
            Assert.Equal(400, options.SpeechPadMs);
        }

        [Fact]
        public void SpeechPadMs_CanBeSet()
        {
            var options = new VadOptions { SpeechPadMs = 100 };
            Assert.Equal(100, options.SpeechPadMs);
        }

        [Fact]
        public void VadOptions_Enabled_DefaultFalse()
        {
            var options = new VadOptions();
            Assert.False(options.Enabled);
        }
    }
}
