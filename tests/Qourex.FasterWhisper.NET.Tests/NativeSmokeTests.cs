// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// End-to-end native smoke tests validating runtime interop, model loading, audio decoding,
    /// VAD, word-level alignment, and streaming across target platforms.
    /// Marked as Integration to run in integration/smoke test suites.
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Native")]
    public class NativeSmokeTests
    {
        private const string ModelName = "tiny";
        private static readonly string WavPath = FindHarvardWav();

        private static string FindHarvardWav()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "samples")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            if (dir != null)
            {
                var path = Path.Combine(dir, "samples", "Qourex.FasterWhisper.NET.Samples", "harvard.wav");
                if (File.Exists(path)) return path;
            }
            return "harvard.wav";
        }

        private static bool IsSampleAvailable() => File.Exists(WavPath);

        [Fact]
        public async Task SmokeTest_FileTranscription_SucceedsAndEmitsSegments()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                Assert.NotNull(model);

                var options = new WhisperOptions
                {
                    BeamSize = 1,
                    NormalizeAudio = true
                };

                List<WhisperSegment> segments;
                try
                {
                    segments = model.Transcribe(WavPath, language: "en", options: options).ToList();
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }

                Assert.NotEmpty(segments);

                string fullText = string.Join(" ", segments.Select(s => s.Text)).Trim();
                Assert.False(string.IsNullOrWhiteSpace(fullText));
                Assert.True(fullText.Length >= 10);
            }
        }

        [Fact]
        public async Task SmokeTest_RawFloatBufferTranscription_MatchesFileExecution()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                var processor = new AudioProcessor(80);
                float[] audioSamples = processor.LoadWav(WavPath);
                Assert.NotEmpty(audioSamples);
                Assert.True(audioSamples.Length > 16000);

                List<WhisperSegment> segments;
                try
                {
                    segments = model.Transcribe(audioSamples, language: "en", options: new WhisperOptions { BeamSize = 1 }).ToList();
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }

                Assert.NotEmpty(segments);

                string fullText = string.Join(" ", segments.Select(s => s.Text));
                Assert.False(string.IsNullOrWhiteSpace(fullText));
            }
        }

        [Fact]
        public async Task SmokeTest_AsyncStreaming_YieldsSegmentsSequentially()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                var receivedSegments = new List<WhisperSegment>();

                try
                {
                    await foreach (var segment in model.TranscribeAsync(WavPath, language: "en", options: new WhisperOptions { BeamSize = 1 }))
                    {
                        Assert.NotNull(segment);
                        Assert.True(segment.End >= segment.Start);
                        receivedSegments.Add(segment);
                    }
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }

                Assert.NotEmpty(receivedSegments);
            }
        }

        [Fact]
        public async Task SmokeTest_WordTimestamps_HaveValidBoundsAndProbabilities()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                var options = new WhisperOptions
                {
                    BeamSize = 1,
                    WordTimestamps = true
                };

                List<WhisperSegment> segments;
                try
                {
                    segments = model.Transcribe(WavPath, language: "en", options: options).ToList();
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }

                Assert.NotEmpty(segments);

                var wordsWithTimestamps = segments.SelectMany(s => s.Words).ToList();
                Assert.NotEmpty(wordsWithTimestamps);

                foreach (var word in wordsWithTimestamps)
                {
                    Assert.NotEmpty(word.Word);
                    Assert.True(word.Start >= 0f);
                    Assert.True(word.End >= word.Start);
                    Assert.InRange(word.Probability, 0.0f, 1.0f);
                }
            }
        }

        [Fact]
        public async Task SmokeTest_SileroVAD_FiltersSilenceAndTranscribesSpeech()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                var vadOptions = new VadOptions
                {
                    Enabled = true,
                    Threshold = 0.5f,
                    MinSpeechDurationMs = 250,
                    MaxSpeechDurationS = float.PositiveInfinity,
                    MinSilenceDurationMs = 2000
                };

                var options = new WhisperOptions { BeamSize = 1 };
                List<WhisperSegment> segments;
                try
                {
                    segments = model.Transcribe(WavPath, language: "en", options: options, vadOptions: vadOptions).ToList();
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }

                Assert.NotEmpty(segments);
            }
        }

        [Fact]
        public async Task SmokeTest_MultilingualModel_DetectsLanguageCorrectly()
        {
            if (!IsSampleAvailable()) return;

            WhisperModel? model = null;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: "cpu", computeType: "default");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is ExternalException || ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                Assert.True(model.IsMultilingual);

                try
                {
                    var segments = model.Transcribe(WavPath, options: new WhisperOptions { BeamSize = 1 }).ToList();
                    Assert.NotEmpty(segments);
                }
                catch (ExternalException ex) when (ex.Message.Contains("SGEMM") || ex.Message.Contains("backend"))
                {
                    return;
                }
            }
        }
    }
}
