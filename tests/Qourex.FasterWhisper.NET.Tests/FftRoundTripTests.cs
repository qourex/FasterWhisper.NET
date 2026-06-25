// Copyright (c) 2026 Qourex. Licensed under the MIT License.
using System;
using Xunit;
using Xunit.Abstractions;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class FftRoundTripTests
    {
        private readonly ITestOutputHelper _output;
        public FftRoundTripTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void FFT_IFFT_RoundTrip_RecoversOriginalSignal()
        {
            int n = 512;
            float[] real = new float[n];
            float[] imag = new float[n];

            var rng = new Random(42);
            float[] original = new float[n];
            for (int i = 0; i < n; i++)
            {
                original[i] = (float)(rng.NextDouble() * 2 - 1);
                real[i] = original[i];
            }

            // Forward FFT
            AudioProcessor.FFTInPlace(real, imag, n);

            _output.WriteLine($"After FFT: real[0]={real[0]:F4}, imag[0]={imag[0]:F4}");

            // IFFT: conjugate → FFT → scale
            for (int i = 0; i < n; i++)
                imag[i] = -imag[i];

            AudioProcessor.FFTInPlace(real, imag, n);

            float maxError = 0;
            float invN = 1.0f / n;
            for (int i = 0; i < n; i++)
            {
                float recovered = real[i] * invN;
                float error = Math.Abs(recovered - original[i]);
                if (error > maxError) maxError = error;
            }

            _output.WriteLine($"Max round-trip error: {maxError:E4}");
            Assert.True(maxError < 1e-3f, $"FFT round-trip error too large: {maxError}");
        }

        [Fact]
        public void NoiseGate_NoChange_Identity()
        {
            // If we DON'T attenuate any bins, the output should match input
            int n = 512;
            float[] real = new float[n];
            float[] imag = new float[n];
            float[] window = new float[n];

            var rng = new Random(42);
            float[] original = new float[n];
            for (int i = 0; i < n; i++)
            {
                original[i] = (float)(rng.NextDouble() * 2 - 1);
                real[i] = original[i];
                window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / n));
            }

            // Window → FFT → (no changes) → IFFT → unwindow
            for (int i = 0; i < n; i++)
                real[i] *= window[i];
            
            AudioProcessor.FFTInPlace(real, imag, n);
            
            // IFFT
            for (int i = 0; i < n; i++)
                imag[i] = -imag[i];
            AudioProcessor.FFTInPlace(real, imag, n);
            
            float invN = 1.0f / n;
            float maxError = 0;
            for (int i = 0; i < n; i++)
            {
                float recovered = real[i] * invN;
                // The recovered signal = original * window (since we windowed before FFT)
                float expected = original[i] * window[i];
                float error = Math.Abs(recovered - expected);
                if (error > maxError) maxError = error;
            }

            _output.WriteLine($"Identity round-trip max error: {maxError:E4}");
            Assert.True(maxError < 1e-3f, $"Identity round-trip error: {maxError}");
        }
    }
}
