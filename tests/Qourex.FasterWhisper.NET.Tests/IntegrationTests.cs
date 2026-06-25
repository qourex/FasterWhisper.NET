// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    [Trait("Category", "Integration")]
    public class IntegrationTests
    {
        private const string ModelName = "tiny";
        private static readonly string WavPath = GetWavPath();

        private static string GetWavPath()
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

        [Theory]
        [InlineData("cpu")]
        [InlineData("cuda")]
        public async Task TestTranscriptionAndLanguageDetection(string device)
        {
            // 1. Load model (will download to ~/.cache/qourex-fasterwhisper/ if not present)
            WhisperModel model;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: device);
            }
            catch (System.Runtime.InteropServices.ExternalException ex) when (ex.Message.Contains("not compiled with CUDA support"))
            {
                // Gracefully skip CUDA tests if the loaded library doesn't support CUDA (e.g. CPU-only build)
                return;
            }

            using (model)
            {
                Assert.NotNull(model);

                // 2. Transcribe WAV file with auto-detected language and word timestamps
                var options = new WhisperOptions
                {
                    WordTimestamps = true,
                    NormalizeAudio = true,
                    CutLowFrequencies = true
                };

                var segments = model.Transcribe(WavPath, options: options).ToList();
                Assert.NotEmpty(segments);

                // Assert language detection (multilingual model auto-detects English)
                Assert.True(model.IsMultilingual);

                // Assert transcription text contains expected words (Harvard sentences)
                string fullText = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
                Assert.Contains("beer", fullText);

                // Assert word-level timestamps are populated
                foreach (var segment in segments)
                {
                    Assert.NotEmpty(segment.Words);
                    foreach (var word in segment.Words)
                    {
                        Assert.NotEmpty(word.Word);
                        Assert.True(word.Start >= 0f);
                        Assert.True(word.End >= word.Start);
                        Assert.True(word.Probability >= 0f && word.Probability <= 1f);
                    }
                }
            }
        }

        [Fact]
        public void TestFillerAndStutterFilters()
        {
            var options = new WhisperOptions
            {
                WordTimestamps = true,
                FilterFillerWords = true,
                PruneStutters = true
            };

            var segment = new WhisperSegment("uh hello hello world um", new int[0], 0f, 0f, 0f, 5f)
            {
                Words = new List<WhisperWord>
                {
                    new() { Word = "uh", Start = 0f, End = 0.5f, Probability = 0.9f },
                    new() { Word = "hello", Start = 0.5f, End = 1.0f, Probability = 0.95f },
                    new() { Word = "hello", Start = 1.0f, End = 1.5f, Probability = 0.95f },
                    new() { Word = "world", Start = 1.5f, End = 2.0f, Probability = 0.99f },
                    new() { Word = "um", Start = 2.0f, End = 2.5f, Probability = 0.9f }
                }
            };

            WhisperModel.CleanSegmentTextAndWords(segment, options);

            Assert.Equal("hello world", segment.Text);
            Assert.Equal(2, segment.Words.Count);
            Assert.Equal("hello", segment.Words[0].Word);
            Assert.Equal("world", segment.Words[1].Word);
        }

        [Theory]
        [InlineData("cpu")]
        [InlineData("cuda")]
        public async Task TestInMemoryModelLoading(string device)
        {
            var downloader = new ModelDownloader();
            string modelPath = await downloader.GetModelPathAsync(ModelName);

            var modelFiles = new Dictionary<string, byte[]>();
            var filesToLoad = new[] { "model.bin", "config.json", "vocabulary.txt" };
            foreach (var file in filesToLoad)
            {
                string path = Path.Combine(modelPath, file);
                if (File.Exists(path))
                {
                    modelFiles[file] = await File.ReadAllBytesAsync(path);
                }
            }

            WhisperModel model;
            try
            {
                model = new WhisperModel(modelFiles, device: device);
            }
            catch (System.Runtime.InteropServices.ExternalException ex) when (ex.Message.Contains("not compiled with CUDA support"))
            {
                // Gracefully skip CUDA tests if the loaded library doesn't support CUDA (e.g. CPU-only build)
                return;
            }

            using (model)
            {
                Assert.NotNull(model);

                var segments = model.Transcribe(WavPath).ToList();
                Assert.NotEmpty(segments);

                string fullText = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
                Assert.Contains("beer", fullText);
            }
        }

        [Theory]
        [InlineData("cpu")]
        [InlineData("cuda")]
        public async Task TestStreamingTranscription(string device)
        {
            WhisperModel model;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: device);
            }
            catch (System.Runtime.InteropServices.ExternalException ex) when (ex.Message.Contains("not compiled with CUDA support"))
            {
                // Gracefully skip CUDA tests if the loaded library doesn't support CUDA (e.g. CPU-only build)
                return;
            }

            using (model)
            {
                var audioProcessor = new AudioProcessor(model.NMels);
                float[] pcm = audioProcessor.LoadWav(WavPath);

                async IAsyncEnumerable<float[]> ProduceAudioChunks()
                {
                    int chunkSize = 8000;
                    for (int i = 0; i < pcm.Length; i += chunkSize)
                    {
                        int len = Math.Min(chunkSize, pcm.Length - i);
                        float[] chunk = new float[len];
                        Array.Copy(pcm, i, chunk, 0, len);
                        yield return chunk;
                        await Task.Delay(10);
                    }
                }

                var streamingResult = model.TranscribeStreamAsync(ProduceAudioChunks());
                var segments = new List<WhisperSegment>();
                await foreach (var seg in streamingResult)
                {
                    segments.Add(seg);
                }

                Assert.NotEmpty(segments);
                string fullText = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
                Assert.Contains("beer", fullText);
            }
        }

        [Fact]
        public void Test8kHzUpsampling()
        {
            int rate8k = 8000;
            float[] input = new float[rate8k];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate8k);
            }

            float[] output = AudioProcessor.Resample(input, rate8k, 16000);

            Assert.Equal(16000, output.Length);

            for (int i = 0; i < output.Length; i++)
            {
                Assert.True(output[i] >= -1.01f && output[i] <= 1.01f);
            }
        }

        [Theory]
        [InlineData("cpu")]
        [InlineData("cuda")]
        public async Task TestMultiReplicaConcurrency(string device)
        {
            WhisperModel model;
            try
            {
                model = await WhisperModel.LoadAsync(ModelName, device: device, numReplicas: 2);
            }
            catch (System.Runtime.InteropServices.ExternalException ex) when (ex.Message.Contains("not compiled with CUDA support"))
            {
                return;
            }

            using (model)
            {
                Assert.NotNull(model);

                var task1 = Task.Run(() => model.Transcribe(WavPath).ToList());
                var task2 = Task.Run(() => model.Transcribe(WavPath).ToList());

                await Task.WhenAll(task1, task2);

                var segments1 = task1.Result;
                var segments2 = task2.Result;

                Assert.NotEmpty(segments1);
                Assert.NotEmpty(segments2);

                string text1 = string.Join(" ", segments1.Select(s => s.Text)).ToLowerInvariant();
                string text2 = string.Join(" ", segments2.Select(s => s.Text)).ToLowerInvariant();

                Assert.Contains("beer", text1);
                Assert.Contains("beer", text2);
            }
        }
    }
}
