// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Audio quality grade classification.
    /// </summary>
    public enum AudioQualityGrade
    {
        /// <summary>SNR &gt; 30dB, no clipping, good level.</summary>
        Excellent,
        /// <summary>SNR 20–30dB, minimal issues.</summary>
        Good,
        /// <summary>SNR 10–20dB, some quality concerns.</summary>
        Fair,
        /// <summary>SNR &lt; 10dB, significant quality issues.</summary>
        Poor
    }

    /// <summary>
    /// Assessment of input audio quality with actionable suggestions.
    /// </summary>
    public class AudioQualityReport
    {
        /// <summary>Estimated signal-to-noise ratio in dB.</summary>
        public float SignalToNoiseRatio { get; init; }

        /// <summary>Percentage of samples at or near maximum amplitude (clipping).</summary>
        public float ClippingPercent { get; init; }

        /// <summary>Peak amplitude (0.0–1.0).</summary>
        public float PeakAmplitude { get; init; }

        /// <summary>RMS level in dBFS (decibels relative to full scale).</summary>
        public float RmsLevelDbfs { get; init; }

        /// <summary>Whether the audio has a significant DC offset.</summary>
        public bool HasDcOffset { get; init; }

        /// <summary>Percentage of audio that is silence (below -40dBFS).</summary>
        public float SilencePercent { get; init; }

        /// <summary>Overall quality grade.</summary>
        public AudioQualityGrade OverallGrade { get; init; }

        /// <summary>Actionable suggestions for improving transcription quality.</summary>
        public List<string> Suggestions { get; init; } = new();

        /// <summary>Returns a human-readable summary.</summary>
        public override string ToString()
        {
            return $"Grade: {OverallGrade} | SNR: {SignalToNoiseRatio:F1}dB | " +
                   $"Peak: {PeakAmplitude:F3} | RMS: {RmsLevelDbfs:F1}dBFS | " +
                   $"Clipping: {ClippingPercent:F1}% | Silence: {SilencePercent:F1}% | " +
                   $"DC Offset: {HasDcOffset}";
        }

        /// <summary>
        /// Analyzes audio quality and returns a report with actionable recommendations.
        /// </summary>
        /// <param name="samples">Audio samples (mono float32, typically 16kHz).</param>
        /// <param name="sampleRate">Sample rate in Hz. Default 16000.</param>
        /// <returns>Audio quality report with grade and suggestions.</returns>
        public static AudioQualityReport Assess(float[] samples, int sampleRate = 16000)
        {
            if (samples == null || samples.Length == 0)
            {
                return new AudioQualityReport
                {
                    OverallGrade = AudioQualityGrade.Poor,
                    Suggestions = new List<string> { "No audio data provided." }
                };
            }

            // Compute statistics
            float peak = 0f;
            double sumSquares = 0;
            double sum = 0;
            int clippedSamples = 0;
            int silentSamples = 0;
            float silenceThreshold = 0.001f; // ~-60dBFS

            for (int i = 0; i < samples.Length; i++)
            {
                float abs = Math.Abs(samples[i]);
                if (abs > peak) peak = abs;
                sumSquares += samples[i] * (double)samples[i];
                sum += samples[i];

                if (abs >= 0.99f) clippedSamples++;
                if (abs < silenceThreshold) silentSamples++;
            }

            float rms = (float)Math.Sqrt(sumSquares / samples.Length);
            float dcOffset = (float)(sum / samples.Length);
            float clippingPercent = (float)clippedSamples / samples.Length * 100f;
            float silencePercent = (float)silentSamples / samples.Length * 100f;

            // RMS in dBFS
            float rmsDbfs = rms > 0 ? 20f * MathF.Log10(rms) : -96f;

            // Estimate SNR: ratio of RMS signal to estimated noise floor
            // Noise floor estimated from quietest 10% of frames
            float snr = EstimateSnr(samples, sampleRate, rms);

            // Determine grade
            AudioQualityGrade grade;
            if (snr > 30 && clippingPercent < 0.1f && rmsDbfs > -30)
                grade = AudioQualityGrade.Excellent;
            else if (snr > 20 && clippingPercent < 1f && rmsDbfs > -40)
                grade = AudioQualityGrade.Good;
            else if (snr > 10 && clippingPercent < 5f)
                grade = AudioQualityGrade.Fair;
            else
                grade = AudioQualityGrade.Poor;

            // Generate suggestions
            var suggestions = new List<string>();

            if (clippingPercent > 1f)
                suggestions.Add($"Audio is clipping ({clippingPercent:F1}% of samples). Reduce recording volume.");

            if (rmsDbfs < -35)
                suggestions.Add($"Audio level is very low ({rmsDbfs:F1} dBFS). Consider enabling NormalizeAudio=true.");

            if (Math.Abs(dcOffset) > 0.01f)
                suggestions.Add($"DC offset detected ({dcOffset:F4}). Enable CutLowFrequencies=true to remove.");

            if (snr < 15)
                suggestions.Add($"Low SNR ({snr:F1} dB). Enable DenoiseAudio=true for noise reduction.");

            if (silencePercent > 80)
                suggestions.Add($"Audio is mostly silence ({silencePercent:F0}%). Enable VAD to skip silent regions.");

            if (suggestions.Count == 0)
                suggestions.Add("Audio quality is good. No changes recommended.");

            return new AudioQualityReport
            {
                SignalToNoiseRatio = snr,
                ClippingPercent = clippingPercent,
                PeakAmplitude = peak,
                RmsLevelDbfs = rmsDbfs,
                HasDcOffset = Math.Abs(dcOffset) > 0.01f,
                SilencePercent = silencePercent,
                OverallGrade = grade,
                Suggestions = suggestions
            };
        }

        private static float EstimateSnr(float[] samples, int sampleRate, float overallRms)
        {
            // Frame-based SNR estimation
            int frameSize = sampleRate / 10; // 100ms frames
            if (samples.Length < frameSize * 2) return 20f; // Default for very short audio

            var frameEnergies = new List<float>();
            for (int i = 0; i <= samples.Length - frameSize; i += frameSize)
            {
                double frameSum = 0;
                for (int j = 0; j < frameSize; j++)
                {
                    frameSum += samples[i + j] * (double)samples[i + j];
                }
                frameEnergies.Add((float)Math.Sqrt(frameSum / frameSize));
            }

            frameEnergies.Sort();

            // Noise floor = mean of bottom 10% of frames
            int noiseFrameCount = Math.Max(1, frameEnergies.Count / 10);
            float noiseFloor = 0;
            for (int i = 0; i < noiseFrameCount; i++)
            {
                noiseFloor += frameEnergies[i];
            }
            noiseFloor /= noiseFrameCount;

            if (noiseFloor <= 0) noiseFloor = 1e-7f;

            float snr = 20f * MathF.Log10(overallRms / noiseFloor);
            return Math.Clamp(snr, 0f, 80f);
        }
    }
}
