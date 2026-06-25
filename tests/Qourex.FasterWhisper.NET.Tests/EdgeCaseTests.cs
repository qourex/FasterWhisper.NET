// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Edge case tests for audio processing (TEST-3).
    /// </summary>
    public class EdgeCaseTests
    {
        [Fact]
        public void NormalizeRms_EmptyArray_DoesNotThrow()
        {
            float[] samples = Array.Empty<float>();
            AudioProcessor.NormalizeRms(samples);
            Assert.Empty(samples);
        }

        [Fact]
        public void NormalizeRms_AllZeros_StaysZero()
        {
            float[] samples = new float[100];
            AudioProcessor.NormalizeRms(samples);
            foreach (float s in samples)
                Assert.Equal(0f, s);
        }

        [Fact]
        public void ApplyHighPassFilter_EmptyArray_DoesNotThrow()
        {
            float[] samples = Array.Empty<float>();
            AudioProcessor.ApplyHighPassFilter(samples);
            Assert.Empty(samples);
        }

        [Fact]
        public void ApplyPreEmphasis_SingleSample_DoesNotThrow()
        {
            float[] samples = new float[] { 0.5f };
            AudioProcessor.ApplyPreEmphasis(samples);
            Assert.Single(samples);
        }

        [Fact]
        public void Resample_SameRate_ReturnsSameArray()
        {
            float[] input = new float[] { 0.1f, 0.2f, 0.3f };
            float[] output = AudioProcessor.Resample(input, 16000, 16000);
            Assert.Same(input, output);
        }

        [Fact]
        public void Resample_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AudioProcessor.Resample(null!, 44100, 16000));
        }

        [Fact]
        public void Resample_ZeroRate_ThrowsArgumentException()
        {
            float[] input = new float[] { 0.1f };
            Assert.Throws<ArgumentException>(() => AudioProcessor.Resample(input, 0, 16000));
        }

        [Fact]
        public void Resample_8kTo16k_ProducesCorrectLength()
        {
            // 1 second at 8kHz → should produce ~16000 samples
            float[] input = new float[8000];
            float[] output = AudioProcessor.Resample(input, 8000, 16000);
            Assert.InRange(output.Length, 15999, 16001);
        }

        [Fact]
        public void SpectralNoiseGate_ShortAudio_DoesNotThrow()
        {
            // Audio shorter than FFT size should be a no-op
            float[] samples = new float[100];
            AudioProcessor.ApplySpectralNoiseGate(samples);
            // Should not throw or corrupt data
        }

        [Fact]
        public void SpectralNoiseGate_NullInput_DoesNotThrow()
        {
            // Null should be handled gracefully
            AudioProcessor.ApplySpectralNoiseGate(null!);
        }

        [Fact]
        public void ExtractMelSpectrogram_VeryShortAudio_ReturnsValidArray()
        {
            var processor = new AudioProcessor(80);
            float[] shortAudio = new float[160]; // 0.01 seconds
            float[] mel = processor.ExtractMelSpectrogram(shortAudio, 80);
            Assert.NotNull(mel);
            Assert.Equal(80 * 3000, mel.Length); // Always returns nMels * 3000
        }
    }
}
