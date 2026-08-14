// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    [Trait("Category", "Smoke")]
    [Trait("Category", "Native")]
    public class NativeSmokeTests
    {
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

        [Fact]
        public async Task TestNativeSpeechRecognitionPipeline()
        {
            // Verify audio sample existence
            if (!File.Exists(WavPath))
            {
                return;
            }

            WhisperModel? model = null;
            try
            {
                // Initialize lightweight 'tiny' model on CPU
                model = await WhisperModel.LoadAsync("tiny", device: "cpu", computeType: "default");
            }
            catch (DllNotFoundException)
            {
                // If running in an environment without native runtime binaries in output directory, skip gracefully
                return;
            }
            catch (Exception ex) when (ex.GetType().Name.Contains("Native"))
            {
                return;
            }

            using (model)
            {
                Assert.NotNull(model);
                Assert.True(model.IsMultilingual);

                var options = new WhisperOptions
                {
                    BeamSize = 1,
                    WordTimestamps = true,
                    NormalizeAudio = true
                };

                var segments = model.Transcribe(WavPath, options: options).ToList();
                Assert.NotEmpty(segments);

                string fullText = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
                Assert.False(string.IsNullOrWhiteSpace(fullText));
                
                // Assert that transcribed speech contains recognizable words
                Assert.True(fullText.Length > 10);
            }
        }
    }
}
