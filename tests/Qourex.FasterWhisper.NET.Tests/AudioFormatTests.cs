// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Tests for WAV audio format decoding (8/16/24/32-bit PCM, 64-bit float, A-law, μ-law).
    /// These tests exercise AudioProcessor's decoding helpers and noise gate directly.
    /// </summary>
    public class AudioFormatTests
    {
        [Fact]
        public void Decode16BitPcm_KnownValues_ReturnsCorrectFloats()
        {
            // Silence (0), max positive (32767), max negative (-32768)
            byte[] data = new byte[6];
            BitConverter.TryWriteBytes(data.AsSpan(0), (short)0);
            BitConverter.TryWriteBytes(data.AsSpan(2), short.MaxValue);
            BitConverter.TryWriteBytes(data.AsSpan(4), short.MinValue);

            var processor = new AudioProcessor(80);
            // We test via the full WAV pipeline by constructing a minimal WAV in memory
            var wav = BuildMinimalWav(1, 16000, 16, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Equal(3, samples.Length);
                Assert.Equal(0f, samples[0], 4);
                Assert.InRange(samples[1], 0.99f, 1.0f);
                Assert.InRange(samples[2], -1.0f, -0.99f);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Decode8BitPcm_KnownValues_ReturnsCorrectFloats()
        {
            // 8-bit unsigned: 128 = silence, 0 = -1.0, 255 ≈ +1.0
            byte[] data = new byte[] { 128, 0, 255 };
            var wav = BuildMinimalWav(1, 16000, 8, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                var processor = new AudioProcessor(80);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Equal(3, samples.Length);
                Assert.Equal(0f, samples[0], 2);
                Assert.InRange(samples[1], -1.01f, -0.99f);
                Assert.InRange(samples[2], 0.99f, 1.01f);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Decode24BitPcm_SilenceAndMax_ReturnsCorrectFloats()
        {
            // 24-bit: 3 bytes per sample. Silence = 00 00 00, max positive = FF FF 7F
            byte[] data = new byte[] { 0, 0, 0, 0xFF, 0xFF, 0x7F };
            var wav = BuildMinimalWav(1, 16000, 24, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                var processor = new AudioProcessor(80);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Equal(2, samples.Length);
                Assert.Equal(0f, samples[0], 4);
                Assert.InRange(samples[1], 0.99f, 1.0f);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Decode32BitPcm_Silence_ReturnsZero()
        {
            byte[] data = new byte[4]; // all zeros = silence
            var wav = BuildMinimalWav(1, 16000, 32, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                var processor = new AudioProcessor(80);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Single(samples);
                Assert.Equal(0f, samples[0], 4);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Decode32BitFloat_KnownValue_ReturnsCorrectFloat()
        {
            byte[] data = new byte[4];
            BitConverter.TryWriteBytes(data, 0.5f);
            var wav = BuildMinimalWav(3, 16000, 32, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                var processor = new AudioProcessor(80);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Single(samples);
                Assert.Equal(0.5f, samples[0], 4);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Decode64BitFloat_KnownValue_ReturnsCorrectFloat()
        {
            byte[] data = new byte[8];
            BitConverter.TryWriteBytes(data, 0.25);
            var wav = BuildMinimalWav(3, 16000, 64, 1, data);
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempPath, wav);
                var processor = new AudioProcessor(80);
                float[] samples = processor.LoadWav(tempPath);
                Assert.Single(samples);
                Assert.Equal(0.25f, samples[0], 3);
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void SpectralNoiseGate_ReducesNoise()
        {
            // Generate a noisy signal: pure noise at moderate amplitude
            int sampleRate = 16000;
            int length = sampleRate * 2; // 2 seconds
            float[] samples = new float[length];
            var rng = new Random(42);
            for (int i = 0; i < length; i++)
                samples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;

            // Measure RMS energy before gating
            double sumSqBefore = 0;
            foreach (float s in samples) sumSqBefore += s * s;
            float rmsBefore = (float)Math.Sqrt(sumSqBefore / length);

            // Apply aggressive noise gate (-40dB reduction)
            AudioProcessor.ApplySpectralNoiseGate(samples, sampleRate, reductionDb: -40f);

            // Measure RMS energy after gating
            double sumSqAfter = 0;
            foreach (float s in samples) sumSqAfter += s * s;
            float rmsAfter = (float)Math.Sqrt(sumSqAfter / length);

            // RMS should decrease for pure noise (energy is reduced)
            Assert.True(rmsAfter < rmsBefore, $"RMS should decrease: before={rmsBefore:F6}, after={rmsAfter:F6}");
        }

        [Fact]
        public void Resample_48kTo16k_ProducesCorrectLength()
        {
            float[] input = new float[48000]; // 1 second at 48kHz
            for (int i = 0; i < input.Length; i++)
                input[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 48000); // 440Hz tone

            float[] output = AudioProcessor.Resample(input, 48000, 16000);
            // Should produce ~16000 samples (1 second at 16kHz)
            Assert.InRange(output.Length, 15999, 16001);
        }

        private static float CalculateRms(float[] samples)
        {
            double sum = 0;
            foreach (float s in samples) sum += s * s;
            return (float)Math.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// Constructs a minimal valid WAV file header + data for testing.
        /// </summary>
        private static byte[] BuildMinimalWav(ushort formatType, int sampleRate, ushort bitsPerSample, ushort channels, byte[] audioData)
        {
            int bytesPerSample = bitsPerSample / 8;
            int byteRate = sampleRate * channels * bytesPerSample;
            ushort blockAlign = (ushort)(channels * bytesPerSample);
            int fmtChunkSize = 16;
            int dataChunkSize = audioData.Length;
            int fileSize = 4 + (8 + fmtChunkSize) + (8 + dataChunkSize);

            using var ms = new System.IO.MemoryStream();
            using var bw = new System.IO.BinaryWriter(ms);

            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(fileSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(fmtChunkSize);
            bw.Write(formatType);
            bw.Write(channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write(bitsPerSample);

            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataChunkSize);
            bw.Write(audioData);

            return ms.ToArray();
        }
    }
}
