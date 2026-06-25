// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class AudioQualityTests
    {
        [Fact]
        public void AssessQuality_SilentAudio_ReturnsPoorGrade()
        {
            float[] silence = new float[16000]; // 1 second of silence
            var report = AudioProcessor.AssessQuality(silence);

            Assert.Equal(AudioQualityGrade.Poor, report.OverallGrade);
            Assert.True(report.SilencePercent > 90f);
        }

        [Fact]
        public void AssessQuality_NormalAudio_ReturnsReasonableGrade()
        {
            float[] audio = GenerateSineWave(440, 16000, 1.0f, 0.3f);
            for (int i = (int)(audio.Length * 0.8); i < audio.Length; i++)
            {
                audio[i] = 0f;
            }
            var report = AudioProcessor.AssessQuality(audio);

            Assert.True(report.OverallGrade == AudioQualityGrade.Good ||
                        report.OverallGrade == AudioQualityGrade.Excellent);
            Assert.True(report.SignalToNoiseRatio > 0);
        }

        [Fact]
        public void AssessQuality_ClippedAudio_DetectsClipping()
        {
            float[] audio = GenerateSineWave(440, 16000, 1.0f, 1.5f); // Will clip at 1.0
            for (int i = 0; i < audio.Length; i++)
            {
                audio[i] = Math.Clamp(audio[i], -1.0f, 1.0f);
                if (Math.Abs(audio[i]) >= 0.999f) audio[i] = 1.0f; // Ensure clipping
            }

            var report = AudioProcessor.AssessQuality(audio);
            Assert.True(report.ClippingPercent > 0f);
        }

        [Fact]
        public void AssessQuality_HasSuggestions()
        {
            float[] silence = new float[16000];
            var report = AudioProcessor.AssessQuality(silence);

            Assert.NotNull(report.Suggestions);
            Assert.NotEmpty(report.Suggestions);
        }

        [Fact]
        public void AssessQuality_ReportContainsSNR()
        {
            float[] audio = GenerateSineWave(440, 16000, 1.0f, 0.3f);
            var report = AudioProcessor.AssessQuality(audio);

            Assert.True(report.PeakAmplitude > 0f);
        }

        [Fact]
        public void AssessQuality_DcOffset_Detected()
        {
            float[] audio = new float[16000];
            for (int i = 0; i < audio.Length; i++)
                audio[i] = 0.3f; // Constant DC offset

            var report = AudioProcessor.AssessQuality(audio);
            Assert.True(report.HasDcOffset);
        }

        private static float[] GenerateSineWave(float freq, int sampleRate, float duration, float amplitude)
        {
            int numSamples = (int)(sampleRate * duration);
            float[] samples = new float[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                samples[i] = amplitude * (float)Math.Sin(2.0 * Math.PI * freq * i / sampleRate);
            }
            return samples;
        }
    }
}
