// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    [Trait("Category", "Integration")]
    public class VADTests
    {
        [Fact]
        public async Task TestVadOnSilence()
        {
            // SileroVad.CreateAsync will automatically download the ONNX file
            // to the local cache if not present, verifying network/io/onnx loading
            using var vad = await SileroVad.CreateAsync();

            Assert.NotNull(vad);

            // 1. Process a single chunk of zeros (silence)
            float[] silentChunk = new float[512];
            float prob = vad.ProcessChunk(silentChunk);

            // Silence should have very low speech probability (usually close to 0)
            Assert.True(prob < 0.3f, $"Expected silence to have low probability, but got {prob}");

            // 2. Run speech segmenter on a larger silence block (e.g. 1 second)
            float[] silence = new float[16000];
            var segments = vad.GetSpeechTimestamps(silence, threshold: 0.5f);

            // There should be no speech segments detected in pure silence
            Assert.Empty(segments);
        }
    }
}
