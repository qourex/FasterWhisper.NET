// Copyright (c) 2026 Qourex. Licensed under the MIT License.
using System;
using Xunit;
using Xunit.Abstractions;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class NoiseGateDebugTests
    {
        private readonly ITestOutputHelper _output;
        public NoiseGateDebugTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void NoiseGate_Debug()
        {
            int sampleRate = 16000;
            int length = sampleRate; // 1 second
            float[] samples = new float[length];
            var rng = new Random(42);
            for (int i = 0; i < length; i++)
                samples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;

            float maxBefore = 0, rmsBefore = 0;
            foreach (float s in samples)
            {
                if (Math.Abs(s) > maxBefore) maxBefore = Math.Abs(s);
                rmsBefore += s * s;
            }
            rmsBefore = (float)Math.Sqrt(rmsBefore / length);

            _output.WriteLine($"Before: max={maxBefore:F4}, rms={rmsBefore:F4}");

            // Single frame test: manually do FFT→gate→IFFT for one frame
            int fftSize = 512;
            float[] window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / fftSize));

            float[] r = new float[fftSize];
            float[] im = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                r[i] = samples[i] * window[i];

            AudioProcessor.FFTInPlace(r, im, fftSize);

            // Check magnitudes
            float maxMag = 0;
            for (int k = 0; k < fftSize / 2 + 1; k++)
            {
                float mag = (float)Math.Sqrt(r[k] * r[k] + im[k] * im[k]);
                if (mag > maxMag) maxMag = mag;
            }
            _output.WriteLine($"Max FFT magnitude: {maxMag:F4}");

            // Attenuate all bins by factor 0.01
            float factor = 0.01f;
            int numBins = fftSize / 2 + 1;
            for (int k = 0; k < numBins; k++)
            {
                r[k] *= factor;
                im[k] *= factor;
                if (k > 0 && k < fftSize / 2)
                {
                    r[fftSize - k] *= factor;
                    im[fftSize - k] *= factor;
                }
            }

            // IFFT
            for (int i = 0; i < fftSize; i++)
                im[i] = -im[i];
            AudioProcessor.FFTInPlace(r, im, fftSize);

            float maxRecovered = 0;
            float invN = 1.0f / fftSize;
            for (int i = 0; i < fftSize; i++)
            {
                float val = Math.Abs(r[i] * invN);
                if (val > maxRecovered) maxRecovered = val;
            }
            _output.WriteLine($"Max recovered (single frame, factor={factor}): {maxRecovered:F6}");
            _output.WriteLine($"Expected max ~{maxBefore * factor:F6}");

            // Now test full noise gate
            float[] testSamples = new float[length];
            rng = new Random(42);
            for (int i = 0; i < length; i++)
                testSamples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;

            AudioProcessor.ApplySpectralNoiseGate(testSamples, sampleRate, reductionDb: -40f);

            float maxAfter = 0, rmsAfter = 0;
            foreach (float s in testSamples)
            {
                if (Math.Abs(s) > maxAfter) maxAfter = Math.Abs(s);
                rmsAfter += s * s;
            }
            rmsAfter = (float)Math.Sqrt(rmsAfter / length);

            _output.WriteLine($"After gate: max={maxAfter:F4}, rms={rmsAfter:F4}");

            Assert.True(maxAfter <= maxBefore * 2, $"Output too large: {maxAfter} vs {maxBefore}");
        }
    }
}
