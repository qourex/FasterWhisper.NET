// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class PromptResetTests
    {
        [Fact]
        public void PromptResetOnTemperature_Default_Is05()
        {
            var options = new WhisperOptions();
            Assert.Equal(0.5f, options.PromptResetOnTemperature);
        }

        [Fact]
        public void PromptResetOnTemperature_CanBeDisabled()
        {
            var options = new WhisperOptions { PromptResetOnTemperature = 0f };
            Assert.Equal(0f, options.PromptResetOnTemperature);
        }

        [Fact]
        public void PromptResetOnTemperature_CanBeSetHigher()
        {
            var options = new WhisperOptions { PromptResetOnTemperature = 0.8f };
            Assert.Equal(0.8f, options.PromptResetOnTemperature);
        }
    }
}
