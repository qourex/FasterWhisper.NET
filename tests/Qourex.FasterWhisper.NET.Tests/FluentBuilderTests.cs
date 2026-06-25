// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class FluentBuilderTests
    {
        [Fact]
        public void Create_SetsModel()
        {
            var builder = WhisperModelBuilder.Create("large-v3");
            Assert.NotNull(builder);
        }

        [Fact]
        public void WithDevice_ReturnsSameBuilder()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithDevice("cuda");
            Assert.NotNull(builder);
        }

        [Fact]
        public void WithComputeType_ReturnsSameBuilder()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithComputeType("float16");
            Assert.NotNull(builder);
        }

        [Fact]
        public void ChainedBuilderCalls_DoNotThrow()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithDevice("cpu")
                .WithComputeType("default")
                .WithWordTimestamps()
                .WithDenoising();

            Assert.NotNull(builder);
        }

        [Fact]
        public void WithVad_SetsVadOptions()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithVad(threshold: 0.3f);
            Assert.NotNull(builder);
        }

        [Fact]
        public void WithTextRestoration_ReturnsSameBuilder()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithTextRestoration();
            Assert.NotNull(builder);
        }

        [Fact]
        public void WithMultiPass_ReturnsSameBuilder()
        {
            var builder = WhisperModelBuilder.Create("base")
                .WithMultiPass(confidenceThreshold: 0.5f);
            Assert.NotNull(builder);
        }

        [Fact]
        public void FullChainedBuilder_DoesNotThrow()
        {
            var builder = WhisperModelBuilder.Create("large-v3")
                .WithDevice("cuda")
                .WithComputeType("float16")
                .WithVad(threshold: 0.5f)
                .WithWordTimestamps()
                .WithDenoising()
                .WithTextRestoration()
                .WithMultiPass(confidenceThreshold: 0.6f);

            Assert.NotNull(builder);
        }
    }
}
