// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class WarmUpTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task WarmUp_ThrowsOnDisposedModel()
        {
            var model = await WhisperModel.LoadAsync("tiny", device: "cpu");
            model.Dispose();
            Assert.Throws<ObjectDisposedException>(() => model.WarmUp());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task WarmUp_RunsSuccessfully()
        {
            using var model = await WhisperModel.LoadAsync("tiny", device: "cpu");
            
            // Measure warm-up time
            var watch = System.Diagnostics.Stopwatch.StartNew();
            model.WarmUp();
            watch.Stop();
            
            // First actual transcription after warm-up
            float[] silence = new float[16000]; // 1 second of silence
            watch.Restart();
            var segments = model.Transcribe(silence);
            watch.Stop();

            Assert.NotNull(segments);
        }
    }
}
