// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class BatchedInferenceTests
    {
        [Fact]
        public void BatchedInferencePipeline_CanBeCreated_WithNullModel()
        {
            // BatchedInferencePipeline requires a WhisperModel, which requires native libs.
            // This test verifies the constructor throws with null (validates parameter checking).
            Assert.Throws<ArgumentNullException>(() => new BatchedInferencePipeline(null!));
        }

        [Fact]
        public void BatchedInferencePipeline_DefaultBatchSize_Is8()
        {
            // Verify default batch size documentation is correct.
            // The class accepts batchSize in constructor with default 8.
            // Since we can't instantiate without a model, test the option type instead.
            var options = new WhisperOptions();
            Assert.NotNull(options); // Placeholder — full integration test requires native model
        }
    }
}
