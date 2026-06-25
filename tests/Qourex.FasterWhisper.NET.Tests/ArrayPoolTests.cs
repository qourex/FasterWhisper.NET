// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System.Buffers;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class ArrayPoolTests
    {
        [Fact]
        public void ExtractMelSpectrogramPooled_ProducesSameResult()
        {
            var processor = new AudioProcessor(80);
            float[] audio = GenerateSineWave(440, 16000, 1.0f);

            float[] standard = processor.ExtractMelSpectrogram(audio, 80);
            float[] pooled = processor.ExtractMelSpectrogramPooled(audio, 80);

            Assert.True(pooled.Length >= standard.Length);

            try
            {
                // Verify values are approximately equal (pooled may have slight differences from buffer reuse)
                for (int i = 0; i < standard.Length; i++)
                {
                    Assert.True(Math.Abs(standard[i] - pooled[i]) < 1e-4f,
                        $"Mismatch at index {i}: standard={standard[i]}, pooled={pooled[i]}");
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(pooled);
            }
        }

        [Fact]
        public void ArrayPool_RentAndReturn_DoesNotLeak()
        {
            // Verify that ArrayPool can be rented and returned without issues
            var pool = ArrayPool<float>.Shared;
            float[] buffer = pool.Rent(1024);
            Assert.NotNull(buffer);
            Assert.True(buffer.Length >= 1024);
            pool.Return(buffer, clearArray: true);
        }

        [Fact]
        public void ExtractMelSpectrogramsParallel_MultipleChunks()
        {
            float[] chunk1 = GenerateSineWave(440, 16000, 0.5f);
            float[] chunk2 = GenerateSineWave(880, 16000, 0.5f);

            var processor = new AudioProcessor(80);
            var chunks = new[] { chunk1, chunk2 };
            var results = processor.ExtractMelSpectrogramsParallel(chunks, 80);

            Assert.Equal(2, results.Length);
            Assert.True(results[0].Length > 0);
            Assert.True(results[1].Length > 0);
        }

        [Fact]
        public async System.Threading.Tasks.Task ExtractMelSpectrogramPooled_MultipleThreads_Safe()
        {
            var processor = new AudioProcessor(80);
            var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

            for (int t = 0; t < 10; t++)
            {
                int frequency = 200 + t * 100;
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    float[] audio = GenerateSineWave(frequency, 16000, 0.5f);
                    float[] pooled = processor.ExtractMelSpectrogramPooled(audio, 80);
                    Assert.NotNull(pooled);
                    Assert.True(pooled.Length > 0);
                    ArrayPool<float>.Shared.Return(pooled);
                }));
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        private static float[] GenerateSineWave(float freq, int sampleRate, float duration)
        {
            int numSamples = (int)(sampleRate * duration);
            float[] samples = new float[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                samples[i] = 0.3f * (float)Math.Sin(2.0 * Math.PI * freq * i / sampleRate);
            }
            return samples;
        }
    }
}
