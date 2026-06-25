// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class BestOfTests
    {
        [Fact]
        public void BestOf_Default_Is5()
        {
            var options = new WhisperOptions();
            Assert.Equal(5, options.BestOf);
        }

        [Fact]
        public void BestOf_CanBeSet()
        {
            var options = new WhisperOptions { BestOf = 10 };
            Assert.Equal(10, options.BestOf);
        }

        [Fact]
        public void BestOf_MinValue1_IsValid()
        {
            var options = new WhisperOptions { BestOf = 1 };
            Assert.Equal(1, options.BestOf);
        }
    }
}
