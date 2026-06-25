// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class AudioProcessorTests
    {
        [Fact]
        public void TestMelScaleConversion()
        {
            // Mel conversion should be invertible
            double originalHz = 440.0;
            double mel = HzToMel(originalHz);
            double reconstructedHz = MelToHz(mel);

            Assert.True(Math.Abs(originalHz - reconstructedHz) < 1e-4, 
                $"Mel conversions were not invertible. Expected {originalHz}, got {reconstructedHz}");
        }

        [Fact]
        public void TestMelScaleMonotonicity()
        {
            // Higher frequencies must have higher mel values
            Assert.True(HzToMel(100.0) < HzToMel(200.0));
            Assert.True(HzToMel(1000.0) < HzToMel(2000.0));
        }

        [Fact]
        public void TestMelSpectrogramExtractionBoundaries()
        {
            var processor80 = new AudioProcessor(80);
            var processor128 = new AudioProcessor(128);
            float[] silence = new float[16000 * 2]; // 2 seconds of silence
            
            // Extracting 80 mel channels
            float[] mel80 = processor80.ExtractMelSpectrogram(silence, 80);
            Assert.Equal(80 * 3000, mel80.Length);
 
            // Extracting 128 mel channels
            float[] mel128 = processor128.ExtractMelSpectrogram(silence, 128);
            Assert.Equal(128 * 3000, mel128.Length);
 
            // Output should be normalized
            foreach (float val in mel80)
            {
                Assert.True(val >= -2.0f && val <= 2.0f, $"Mel spectrogram value {val} out of reasonable normalized boundaries.");
            }
        }

        [Fact]
        public void TestNormalizeRmsBoostsQuietAudio()
        {
            // Generate a 1-second quiet sine wave (~0.01 amplitude)
            float[] samples = new float[16000];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.01f * (float)Math.Sin(2 * Math.PI * 440 * i / 16000.0);
            }

            // Calculate original RMS
            float originalRms = CalculateRms(samples);
            Assert.True(originalRms < 0.01f, "Original RMS should be very small");

            // Normalize to -20 dBFS
            AudioProcessor.NormalizeRms(samples);

            // Target RMS at -20 dBFS = 10^(-20/20) = 0.1
            float normalizedRms = CalculateRms(samples);
            Assert.True(normalizedRms > 0.05f && normalizedRms < 0.2f, 
                $"Normalized RMS {normalizedRms:F4} should be near 0.1 (target -20 dBFS)");
        }

        [Fact]
        public void TestNormalizeRmsDoesNotAmplifyNearSilence()
        {
            // Near-silent input (practically zero)
            float[] samples = new float[16000];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 1e-7f;
            }

            AudioProcessor.NormalizeRms(samples);

            // Should NOT be amplified significantly (RMS < 1e-5f guard)
            float rms = CalculateRms(samples);
            Assert.True(rms < 0.01f, $"Near-silence RMS {rms} should not be amplified");
        }

        [Fact]
        public void TestHighPassFilterRemovesDcOffset()
        {
            // Create signal with DC offset (constant + sine)
            float[] samples = new float[16000];
            float dcOffset = 0.5f;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = dcOffset + 0.3f * (float)Math.Sin(2 * Math.PI * 1000 * i / 16000.0);
            }

            AudioProcessor.ApplyHighPassFilter(samples);

            // After high-pass filtering, DC component should be mostly removed
            float mean = 0f;
            for (int i = samples.Length / 2; i < samples.Length; i++) // Skip transient at start
            {
                mean += samples[i];
            }
            mean /= (samples.Length / 2);

            Assert.True(Math.Abs(mean) < 0.05f, 
                $"DC offset should be removed by high-pass filter, but mean is {mean:F4}");
        }

        [Fact]
        public void TestHighPassFilterPreservesHighFrequencies()
        {
            // Create a 1kHz sine wave (well above 80Hz cutoff)
            float[] samples = new float[16000];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.5f * (float)Math.Sin(2 * Math.PI * 1000 * i / 16000.0);
            }

            float originalRms = CalculateRms(samples);
            AudioProcessor.ApplyHighPassFilter(samples);
            float filteredRms = CalculateRms(samples);

            // A 1kHz signal should pass through with minimal attenuation
            float ratio = filteredRms / originalRms;
            Assert.True(ratio > 0.9f, 
                $"1kHz signal should be preserved (ratio={ratio:F4}), but was significantly attenuated");
        }

        // Helper mirrors for test assertions
        private static double HzToMel(double hz)
        {
            if (hz < 1000.0)
            {
                return hz / (200.0 / 3.0);
            }
            return 15.0 + Math.Log(hz / 1000.0) / Math.Log(6.4 / 3.0) * 10.0;
        }

        private static double MelToHz(double mel)
        {
            if (mel < 15.0)
            {
                return mel * (200.0 / 3.0);
            }
            return 1000.0 * Math.Pow(6.4 / 3.0, (mel - 15.0) / 10.0);
        }

        private static float CalculateRms(float[] samples)
        {
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            return (float)Math.Sqrt(sum / samples.Length);
        }
    }
}

