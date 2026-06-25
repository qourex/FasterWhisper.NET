// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Qourex.FasterWhisper.NET;

namespace Qourex.FasterWhisper.NET.Samples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            PrintBanner();

            if (args.Length > 0 && args[0].Equals("benchmark", StringComparison.OrdinalIgnoreCase))
            {
                string deviceArg = args.Length > 1 ? args[1] : "cpu";
                string modelArg = args.Length > 2 ? args[2] : "tiny";
                string fileArg = args.Length > 3 ? args[3] : "harvard.wav";
                try
                {
                    await RunBenchmarkAsync(deviceArg, modelArg, fileArg);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n✗ Benchmark Error: {ex.GetType().Name}");
                    Console.WriteLine($"  {ex.Message}");
                    Console.ResetColor();
                }
                return;
            }

            string modelName = args.Length > 0 ? args[0] : "tiny";
            string audioPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "harvard.wav");
            string device = args.Length > 2 ? args[2] : "cpu";

            PrintInfo($"Selected Model:  {modelName}");
            PrintInfo($"Selected Audio:  {audioPath}");
            PrintInfo($"Selected Device: {device}");
            Console.WriteLine();

            string outputDir = Path.Combine(AppContext.BaseDirectory, "outputs");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            try
            {
                // Ensure we have an audio file to work with
                if (!File.Exists(audioPath))
                {
                    string fallbackPath = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(audioPath));
                    if (File.Exists(fallbackPath))
                    {
                        audioPath = fallbackPath;
                    }
                    else
                    {
                        PrintWarning($"Audio file '{audioPath}' not found. Generating a test WAV...");
                        audioPath = Path.Combine(AppContext.BaseDirectory, "test_audio.wav");
                        GenerateTestWav(audioPath, durationSeconds: 5, frequency: 440);
                        PrintInfo($"Generated test audio: {audioPath}");
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // SECTION 1: Builder, Logger, Mapped Load & Warm-Up (E-6, E-16, E-19, E-21)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("1. Builder, Logger, Memory-Mapped Load & Cache Warm-Up");

                var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
                {
                    if (p.TotalBytes > 0)
                    {
                        double percent = (double)p.BytesRead / p.TotalBytes * 100;
                        int barWidth = 30;
                        int filled = (int)(percent / 100.0 * barWidth);
                        string bar = new string('█', filled) + new string('░', barWidth - filled);
                        Console.Write($"\r  [Download] [{bar}] {percent,5:F1}%  {p.FileName}");
                    }
                    else
                    {
                        Console.Write($"\r  Downloading {p.FileName}: {p.BytesRead / 1024} KB read...");
                    }
                });

                PrintInfo("Downloading/Resolving model files...");
                var downloader = new ModelDownloader();
                string modelPath = await downloader.GetModelPathAsync(modelName, progress);
                Console.WriteLine();

                PrintInfo("Creating model builder and loading memory-mapped model with diagnostic logger...");
                var builder = WhisperModelBuilder.Create(modelPath)
                    .WithDevice(device)
                    .WithComputeType("default")
                    .WithLogger(new SimpleConsoleLogger("WhisperModel"))
                    .WithMemoryMapping()
                    .WithNumReplicas(1);

                // E-6: Load memory-mapped for zero-copy file loading via builder
                using var model = builder.Build();

                PrintSuccess("Model loaded successfully via MemoryMapped I/O!");
                PrintDetail("Is Multilingual", model.IsMultilingual.ToString());
                PrintDetail("Mel Channels", model.NMels.ToString());

                // E-21: Warm up model caches
                PrintInfo("Warming up model cache...");
                model.WarmUp();
                PrintSuccess("Model cache warm-up completed.");
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 2: Audio Quality Assessment & Spans (E-22, E-24)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("2. Audio Quality Assessment & Spans");

                var audioProcessor = new AudioProcessor(model.NMels);
                float[] pcm = audioProcessor.LoadWav(audioPath);

                PrintInfo("Running Audio Quality Assessment (E-24)...");
                var qualityReport = AudioProcessor.AssessQuality(pcm);

                PrintDetail("Quality Grade", qualityReport.OverallGrade.ToString());
                PrintDetail("Signal-To-Noise Ratio (SNR)", $"{qualityReport.SignalToNoiseRatio:F1} dB");
                PrintDetail("Has DC Offset", qualityReport.HasDcOffset.ToString());
                PrintDetail("Clipping Percentage", $"{qualityReport.ClippingPercent:F1}%");
                PrintDetail("Silence Percentage", $"{qualityReport.SilencePercent:F1}%");

                PrintSubHeader("Actionable suggestions for this audio:");
                foreach (var suggestion in qualityReport.Suggestions)
                {
                    Console.WriteLine($"    ▸ {suggestion}");
                }
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 3: Streaming Mel Feature Extraction (E-23)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("3. Incremental Streaming Mel Feature Extraction");
                PrintInfo("Simulating streaming audio feature extraction...");

                using var melExtractor = new StreamingMelExtractor(model.NMels);
                int feedSize = 1600; // 100ms chunks at 16kHz
                float[] melBuffer = new float[model.NMels * 3000];
                int totalColumnsExtracted = 0;

                // Feed first 3 seconds of audio to simulate streaming Mel computation
                int maxSamplesToFeed = Math.Min(pcm.Length, 16000 * 3);
                for (int offset = 0; offset < maxSamplesToFeed; offset += feedSize)
                {
                    int length = Math.Min(feedSize, maxSamplesToFeed - offset);
                    float[] chunk = new float[length];
                    Array.Copy(pcm, offset, chunk, 0, length);

                    int cols = melExtractor.Feed(chunk, melBuffer);
                    totalColumnsExtracted += cols;
                }

                int flushedCols = melExtractor.Flush(melBuffer);
                totalColumnsExtracted += flushedCols;

                PrintSuccess("Incremental Mel features extracted!");
                PrintDetail("Mel Columns Extracted", totalColumnsExtracted.ToString());
                PrintDetail("Has Full 30s Frame", melExtractor.HasFullFrame.ToString());
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 4: Advanced Transcription Options (Async, Progress, Events)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("4. Advanced Transcription (Async, Progress, Event-driven)");

                var advOptions = new WhisperOptions
                {
                    BeamSize = 10,                      // Broader search space
                    BestOf = 5,                         // FP-1: sample N candidates
                    Patience = 2.0f,                    // Wait longer before pruning search paths
                    PromptResetOnTemperature = 0.5f,    // FP-2: prevent repetition loops
                    NormalizeAudio = true,
                    CutLowFrequencies = true,
                    PreEmphasis = true,                 // E-22: boost high frequencies (essential for 8kHz audio)
                    DenoiseAudio = true,                // E-22: filter background noise
                    RepetitionPenalty = 1.2f,           // FP-2: prevent token repetition loops
                    NoRepeatNgramSize = 3,              // FP-2: prevent repeating n-grams
                    RestoreTextFormatting = true,       // E-8: capitalization and punctuation
                    MultiPassEnabled = true,            // E-11: re-transcribe low confidence
                    MultiPassConfidenceThreshold = 0.6f,
                    MultiPassBeamSize = 10,
                    Temperatures = new float[] { 0.0f, 0.15f, 0.3f, 0.45f, 0.6f, 0.8f }, // Fine-grained fallback temperatures
                    VocabularyBias = new Dictionary<string, float> // E-10: hotword boosting
                    {
                        { "Whisper", 5.0f },
                        { "Qourex", 4.0f },
                        { "CTranslate2", 3.0f }
                    },
                    FilterFillerWords = true,           // Filter out common filler words (uh, um, etc.)
                    PruneStutters = true,               // Prune consecutive duplicate/repeated words
                    HallucinationSilenceThreshold = 2.0f, // Skip silent sections longer than this to avoid hallucinations
                    AdaptiveBeamSize = true             // Fast first-pass decoding (greedy search first)
                };

                var advVadOptions = new VadOptions
                {
                    Enabled = true,                     // Split into natural phrases to avoid long-context drift on tiny
                    Threshold = 0.5f
                };
 
                var transcriptionProgress = new Progress<TranscriptionProgress>(p =>
                {
                    Console.Write($"\r    [Progress] {p.Percent:F1}% ({p.CurrentSeconds:F1}s / {p.TotalSeconds:F1}s)");
                });
 
                // Attach segment decoded callback (E-18)
                model.OnSegmentDecoded += OnSegmentDecodedHandler;

                PrintInfo("Starting async transcription using TranscribeAsync (E-17)...");
                var segmentsList = new List<WhisperSegment>();

                // Run transcription off-thread to gather result and timing diagnostics (E-20)
                var transcribeResult = await Task.Run(() =>
                    model.TranscribeWithInfo(pcm, language: "en", options: advOptions, vadOptions: advVadOptions, progress: transcriptionProgress)
                );

                // Detach callback to avoid affecting later sections
                model.OnSegmentDecoded -= OnSegmentDecodedHandler;
                Console.WriteLine();

                PrintSuccess("Async transcription finished!");
                PrintDetail("Transcription Language", transcribeResult.Info.Language);
                PrintDetail("Language Probability", $"{transcribeResult.Info.LanguageProbability * 100:F1}%");
                PrintDetail("Total Audio Duration", $"{transcribeResult.Info.Duration:F2}s");
                PrintDetail("Duration After VAD", $"{transcribeResult.Info.DurationAfterVad:F2}s");
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 5: Time-Slice & Multilingual Transcription (FP-3, FP-4)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("5. Time-Slice (ClipTimestamps) & Multilingual Detection");

                var timeSliceOptions = new WhisperOptions
                {
                    // FP-3: transcribe specific time ranges
                    ClipTimestamps = new List<(float Start, float End)>
                    {
                        (0.0f, 3.0f),
                        (5.0f, 8.0f)
                    },
                    Multilingual = true, // FP-4: detect language per segment
                    BeamSize = 5
                };

                PrintInfo("Transcribing specific time slices: [0.0s - 3.0s] and [5.0s - 8.0s]...");
                var slicedResult = model.Transcribe(pcm, options: timeSliceOptions);

                PrintSubHeader("Time-slice segments:");
                foreach (var seg in slicedResult)
                {
                    Console.WriteLine($"    ▸ [{seg.Start:F2}s → {seg.End:F2}s] (Lang: {seg.Language}) {seg.Text.Trim()}");
                }
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 6: Output Enrichment & Hallucination Diagnostics (FP-6, FP-7, E-7, E-9)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("6. Output Enrichment & Hallucination Diagnostics");

                PrintSubHeader("Rich segment metadata & word-level confidence:");
                foreach (var seg in transcribeResult.Segments.Take(3))
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"  Segment #{seg.Id} (Seek: {seg.Seek} frames, Temp: {seg.Temperature:F2})");
                    
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"    Hallucination Score: {seg.HallucinationScore:F3} (IsHallucinated={seg.HallucinationScore > 0.7})");

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"    Text: \"{seg.Text.Trim()}\"");
                    Console.ResetColor();

                    if (seg.Words != null && seg.Words.Count > 0)
                    {
                        Console.Write("    Words: ");
                        foreach (var word in seg.Words)
                        {
                            Console.ForegroundColor = word.IsLowConfidence ? ConsoleColor.Red : ConsoleColor.Green;
                            Console.Write($"\"{word.Word}\"({word.Confidence:F2}) ");
                        }
                        Console.ResetColor();
                        Console.WriteLine("\n");
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // SECTION 7: Segment Merging, Splitting & Subtitle Writing (E-12, E-13, E-14, E-15)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("7. Segment Merging, Splitting & Subtitle Exporting");

                var baseSegments = transcribeResult.Segments.ToList();

                // E-13: Paragraph merging
                PrintInfo("Merging short segments into paragraphs...");
                var paragraphs = SegmentMerger.MergeIntoParagraphs(baseSegments, maxPauseSeconds: 1.5f, maxWordsPerParagraph: 40);
                PrintDetail("Paragraph segments count", paragraphs.Count.ToString());

                // E-13: Sentence splitting
                PrintInfo("Splitting segments at sentence boundaries...");
                var sentences = SegmentMerger.SplitIntoSentences(baseSegments);
                PrintDetail("Sentence segments count", sentences.Count.ToString());

                // E-12/E-14/E-15: Export formats
                PrintInfo("Generating export formats (SRT, WebVTT, TSV, JSON, Markdown, HTML, Karaoke SRT)...");

                string srtPath = Path.Combine(outputDir, "output.srt");
                SubtitleExporter.WriteSrt(baseSegments, srtPath);
                PrintDetail("Written SRT Subtitles", srtPath);

                string vttPath = Path.Combine(outputDir, "output.vtt");
                SubtitleExporter.WriteVtt(baseSegments, vttPath);
                PrintDetail("Written WebVTT Subtitles", vttPath);

                string tsvPath = Path.Combine(outputDir, "output.tsv");
                File.WriteAllText(tsvPath, SubtitleExporter.ToTsv(baseSegments));
                PrintDetail("Written TSV Transcript", tsvPath);

                string jsonPath = Path.Combine(outputDir, "output.json");
                File.WriteAllText(jsonPath, SubtitleExporter.ToJson(baseSegments));
                PrintDetail("Written JSON Metadata", jsonPath);

                string mdPath = Path.Combine(outputDir, "output.md");
                File.WriteAllText(mdPath, SubtitleExporter.ToMarkdown(baseSegments));
                PrintDetail("Written Markdown Transcript", mdPath);

                string htmlPath = Path.Combine(outputDir, "output.html");
                SubtitleExporter.WriteHtml(baseSegments, htmlPath);
                PrintDetail("Written Styled HTML Transcript", htmlPath);

                string karaokePath = Path.Combine(outputDir, "output_karaoke.srt");
                SubtitleExporter.WriteWordLevelSrt(baseSegments, karaokePath);
                PrintDetail("Written Word-Level Karaoke SRT", karaokePath);
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 8: Timing Diagnostics & Profiling (E-20)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("8. Timing Diagnostics & Profiling Report");

                var diag = transcribeResult.Diagnostics;
                if (diag != null)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("  ┌────────────────────────────────────────────────────────┐");
                    Console.WriteLine("  │               TRANSCRIBE PERFORMANCE REPORT            │");
                    Console.WriteLine("  ├──────────────────────────────────────────┬─────────────┤");
                    Console.WriteLine($"  │ Audio Load/Decode Time                   │ {diag.AudioLoadMs,8:F1} ms │");
                    Console.WriteLine($"  │ Preprocessing (Denoise/Filter) Time      │ {diag.PreprocessingMs,8:F1} ms │");
                    Console.WriteLine($"  │ Voice Activity Detection (VAD) Time      │ {diag.VadMs,8:F1} ms │");
                    Console.WriteLine($"  │ Mel Spectrogram Computation Time         │ {diag.MelComputeMs,8:F1} ms │");
                    Console.WriteLine($"  │ Neural Network Encoder Time              │ {diag.EncoderMs,8:F1} ms │");
                    Console.WriteLine($"  │ Neural Network Decoder Time              │ {diag.DecoderMs,8:F1} ms │");
                    Console.WriteLine($"  │ Word-Level Timestamp Alignment Time      │ {diag.WordAlignMs,8:F1} ms │");
                    Console.WriteLine($"  │ Text Post-Processing (Restore/Prune) Time│ {diag.PostProcessMs,8:F1} ms │");
                    Console.WriteLine("  ├──────────────────────────────────────────┼─────────────┤");
                    Console.WriteLine($"  │ Total Time Elapsed                       │ {diag.TotalMs,8:F1} ms │");
                    Console.WriteLine($"  │ Real-Time Factor (RTF)                   │ {diag.RealTimeFactor,10:F4} │");
                    Console.WriteLine("  ├──────────────────────────────────────────┼─────────────┤");
                    Console.WriteLine($"  │ Chunks Processed                         │ {diag.ChunksProcessed,11} │");
                    Console.WriteLine($"  │ Temperature Fallback Retries             │ {diag.TemperatureRetries,11} │");
                    Console.WriteLine("  └──────────────────────────────────────────┴─────────────┘");
                    Console.ResetColor();
                }
                else
                {
                    PrintWarning("Diagnostics not available for this run.");
                }
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // SECTION 9: Batched Inference Pipeline (FP-9)
                // ═══════════════════════════════════════════════════════════════
                PrintSection("9. Batched Inference Pipeline (High-Throughput)");

                PrintInfo("Initializing BatchedInferencePipeline (FP-9) with batchSize=4...");
                using var batchPipeline = new BatchedInferencePipeline(model, batchSize: 4);

                var batchOptions = new WhisperOptions
                {
                    NormalizeAudio = true,
                    BeamSize = 1 // Greedy decoding in batch to maximize processing speed
                };

                PrintInfo("Transcribing audio using batch processing...");
                var batchResult = batchPipeline.Transcribe(pcm, language: "en", options: batchOptions);

                PrintSuccess("Batch transcription finished successfully!");
                PrintDetail("Batch Segments count", batchResult.Segments.Count().ToString());
                Console.WriteLine();

                // ═══════════════════════════════════════════════════════════════
                // DONE
                // ═══════════════════════════════════════════════════════════════
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("══════════════════════════════════════════════════");
                Console.WriteLine("  ✓ All feature demonstrations completed!");
                Console.WriteLine("══════════════════════════════════════════════════");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Error: {ex.GetType().Name}");
                Console.WriteLine($"  {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                }
                Console.ResetColor();
            }
        }

        private static void OnSegmentDecodedHandler(object? sender, WhisperSegment seg)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n    [Event Callback - OnSegmentDecoded] [{seg.Start:F2}s → {seg.End:F2}s] \"{seg.Text.Trim()}\"");
            Console.ForegroundColor = color;
        }

        // ─────────────────────────────────────────────────────────────────
        // Console formatting helpers
        // ─────────────────────────────────────────────────────────────────

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════╗");
            Console.WriteLine("  ║    Qourex.FasterWhisper.NET — Feature Demo   ║");
            Console.WriteLine("  ╠══════════════════════════════════════════════╣");
            Console.WriteLine("  ║  Usage: dotnet run [model] [audio] [device]  ║");
            Console.WriteLine("  ║  Defaults: tiny, harvard.wav, cpu            ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void PrintSection(string title)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"══════════════════════════════════════════════════");
            Console.WriteLine($"  {title}");
            Console.WriteLine($"══════════════════════════════════════════════════");
            Console.ResetColor();
        }

        static void PrintSubHeader(string text)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  {text}");
            Console.ResetColor();
        }

        static void PrintInfo(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  ℹ {text}");
            Console.ResetColor();
        }

        static void PrintSuccess(string text)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ {text}");
            Console.ResetColor();
        }

        static void PrintWarning(string text)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠ {text}");
            Console.ResetColor();
        }

        static void PrintDetail(string label, string value)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"    {label}: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(value);
            Console.ResetColor();
        }

        static void PrintResult(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"  ── {text}");
            Console.ResetColor();
        }

        // ─────────────────────────────────────────────────────────────────
        // Test WAV generator (16kHz mono 16-bit PCM sine tone)
        // ─────────────────────────────────────────────────────────────────

        static void GenerateTestWav(string path, int durationSeconds, int frequency)
        {
            const int sampleRate = 16000;
            const short bitsPerSample = 16;
            const short channels = 1;

            int totalSamples = sampleRate * durationSeconds;
            int dataSize = totalSamples * (bitsPerSample / 8) * channels;

            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);

            // RIFF header
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE".ToCharArray());

            // fmt chunk
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);                   // Chunk size
            writer.Write((short)1);             // PCM format
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * (bitsPerSample / 8)); // Byte rate
            writer.Write((short)(channels * (bitsPerSample / 8)));     // Block align
            writer.Write(bitsPerSample);

            // data chunk
            writer.Write("data".ToCharArray());
            writer.Write(dataSize);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double sample = 0.3 * Math.Sin(2.0 * Math.PI * frequency * t);
                short pcmValue = (short)(sample * short.MaxValue);
                writer.Write(pcmValue);
            }
        }

        static async Task RunBenchmarkAsync(string device, string modelName, string inputAudioPath)
        {
            PrintSection("RUNNING SDK PERFORMANCE & RESOURCE BENCHMARK");
            
            // Baseline memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long baseCpuRam = Process.GetCurrentProcess().WorkingSet64;
            long baseGpuVram = GetGpuMemoryUsed();

            PrintInfo($"Baseline CPU RAM: {baseCpuRam / (1024.0 * 1024.0):F1} MB");
            if (baseGpuVram >= 0)
                PrintInfo($"Baseline GPU VRAM: {baseGpuVram / (1024.0 * 1024.0):F1} MB");
            
            PrintInfo($"Downloading/Resolving model '{modelName}'...");
            var downloader = new ModelDownloader();
            string modelPath = await downloader.GetModelPathAsync(modelName);
            
            // Audio data
            string audioPath = inputAudioPath;
            if (!File.Exists(audioPath))
            {
                string fallbackPath = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(audioPath));
                if (File.Exists(fallbackPath))
                {
                    audioPath = fallbackPath;
                }
                else
                {
                    PrintWarning($"Audio file '{audioPath}' not found. Generating a test WAV...");
                    audioPath = Path.Combine(AppContext.BaseDirectory, "test_audio.wav");
                    GenerateTestWav(audioPath, 18, 440); // Generate 18s wav
                }
            }

            // Inspect model to detect correct NMels configuration (e.g. 80 vs 128)
            int nMels = 80;
            try
            {
                using var tempModel = WhisperModelBuilder.Create(modelPath)
                    .WithDevice("cpu")
                    .Build();
                nMels = tempModel.NMels;
            }
            catch { /* fallback to 80 */ }

            var audioProcessor = new AudioProcessor(nMels);
            float[] pcm = audioProcessor.LoadWav(audioPath);
            double audioDuration = pcm.Length / 16000.0;

            PrintInfo($"Selected Model: {modelName} (Mel Channels: {nMels})");
            PrintInfo($"Audio Duration: {audioDuration:F2} seconds");
            PrintInfo($"Target Device:  {device}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 1: Model Startup & Memory Mapping Benchmark
            // ═══════════════════════════════════════════════════════════════
            PrintSubHeader("Phase 1: Startup Time & Model Memory Footprint (Standard vs Memory-Mapped)");
            
            // 1. Standard (Without Memory Mapping)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long preStdCpu = Process.GetCurrentProcess().WorkingSet64;
            long preStdGpu = GetGpuMemoryUsed();
            
            var swLoadStd = System.Diagnostics.Stopwatch.StartNew();
            var modelStd = WhisperModelBuilder.Create(modelPath)
                .WithDevice(device)
                .WithComputeType("default")
                .Build();
            swLoadStd.Stop();
            double loadTimeStdMs = swLoadStd.Elapsed.TotalMilliseconds;
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long postStdCpu = Process.GetCurrentProcess().WorkingSet64;
            long postStdGpu = GetGpuMemoryUsed();
            
            double ramStdMb = (postStdCpu - preStdCpu) / (1024.0 * 1024.0);
            double vramStdMb = preStdGpu >= 0 ? (postStdGpu - preStdGpu) / (1024.0 * 1024.0) : 0;
            
            PrintSuccess($"[Standard] Model loaded in {loadTimeStdMs:F1} ms");
            PrintSuccess($"[Standard] RAM Delta: {ramStdMb:F1} MB");
            if (preStdGpu >= 0)
                PrintSuccess($"[Standard] VRAM Delta: {vramStdMb:F1} MB");
            
            modelStd.Dispose();
            Console.WriteLine();

            // 2. Memory-Mapped (With Memory Mapping)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long preMmapCpu = Process.GetCurrentProcess().WorkingSet64;
            long preMmapGpu = GetGpuMemoryUsed();
            
            var swLoadMmap = System.Diagnostics.Stopwatch.StartNew();
            var modelMmap = WhisperModelBuilder.Create(modelPath)
                .WithDevice(device)
                .WithComputeType("default")
                .WithMemoryMapping()
                .Build();
            swLoadMmap.Stop();
            double loadTimeMmapMs = swLoadMmap.Elapsed.TotalMilliseconds;
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long postMmapCpu = Process.GetCurrentProcess().WorkingSet64;
            long postMmapGpu = GetGpuMemoryUsed();
            
            double ramMmapMb = (postMmapCpu - preMmapCpu) / (1024.0 * 1024.0);
            double vramMmapMb = preMmapGpu >= 0 ? (postMmapGpu - preMmapGpu) / (1024.0 * 1024.0) : 0;
            
            PrintSuccess($"[Memory-Mapped] Model loaded in {loadTimeMmapMs:F1} ms");
            PrintSuccess($"[Memory-Mapped] RAM Delta: {ramMmapMb:F1} MB");
            if (preMmapGpu >= 0)
                PrintSuccess($"[Memory-Mapped] VRAM Delta: {vramMmapMb:F1} MB");
            
            modelMmap.Dispose();
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 2: Replica Scaling & Shared Memory Footprint (1 vs 2 vs 4)
            // ═══════════════════════════════════════════════════════════════
            PrintSubHeader("Phase 2: Multi-Replica VRAM/RAM Scaling (Architectural Shared Weights)");
            
            var replicaMetrics = new List<(int Replicas, double LoadTimeMs, double RamMb, double VramMb)>();
            
            foreach (int reps in new[] { 1, 2, 4 })
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long preLoadCpu = Process.GetCurrentProcess().WorkingSet64;
                long preLoadGpu = GetGpuMemoryUsed();
                
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var modelRep = WhisperModelBuilder.Create(modelPath)
                    .WithDevice(device)
                    .WithNumReplicas(reps)
                    .WithMemoryMapping()
                    .Build();
                sw.Stop();
                
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long postLoadCpu = Process.GetCurrentProcess().WorkingSet64;
                long postLoadGpu = GetGpuMemoryUsed();
                
                double ramUsed = (postLoadCpu - preLoadCpu) / (1024.0 * 1024.0);
                double vramUsed = preLoadGpu >= 0 ? (postLoadGpu - preLoadGpu) / (1024.0 * 1024.0) : 0;
                
                replicaMetrics.Add((reps, sw.Elapsed.TotalMilliseconds, ramUsed, vramUsed));
                PrintInfo($"NumReplicas = {reps}: Load Time = {sw.Elapsed.TotalMilliseconds:F1} ms | CPU RAM = {ramUsed:F1} MB {(preLoadGpu >= 0 ? $"| GPU VRAM = {vramUsed:F1} MB" : "")}");
                
                modelRep.Dispose();
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 3: Execution Performance & Throughput
            // ═══════════════════════════════════════════════════════════════
            PrintSubHeader("Phase 3: Quantization & Speedups (RTF)");
            
            double defaultTime = 0, defaultRtf = 0;
            double int8Time = 0, int8Rtf = 0;
            
            using (var modelDefault = WhisperModelBuilder.Create(modelPath).WithDevice(device).WithComputeType("default").WithMemoryMapping().Build())
            {
                modelDefault.WarmUp();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = modelDefault.Transcribe(pcm).ToList();
                sw.Stop();
                defaultTime = sw.Elapsed.TotalMilliseconds;
                defaultRtf = sw.Elapsed.TotalSeconds / audioDuration;
                PrintInfo($"ComputeType 'default' (float32): Completed in {defaultTime:F1} ms (RTF: {defaultRtf:F4})");
            }

            using (var modelInt8 = WhisperModelBuilder.Create(modelPath).WithDevice(device).WithComputeType("int8").WithMemoryMapping().Build())
            {
                modelInt8.WarmUp();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = modelInt8.Transcribe(pcm).ToList();
                sw.Stop();
                int8Time = sw.Elapsed.TotalMilliseconds;
                int8Rtf = sw.Elapsed.TotalSeconds / audioDuration;
                PrintInfo($"ComputeType 'int8': Completed in {int8Time:F1} ms (RTF: {int8Rtf:F4})");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 4: Concurrency Scaling
            // ═══════════════════════════════════════════════════════════════
            PrintSubHeader("Phase 4: Parallel Concurrency Scaling");
            double timeRep1 = 0, timeRep2 = 0;

            using (var modelRep1 = WhisperModelBuilder.Create(modelPath).WithDevice(device).WithNumReplicas(1).WithMemoryMapping().Build())
            {
                modelRep1.WarmUp();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => modelRep1.Transcribe(pcm).ToList())).ToArray();
                await Task.WhenAll(tasks);
                sw.Stop();
                timeRep1 = sw.Elapsed.TotalMilliseconds;
                PrintInfo($"NumReplicas = 1 (Blocked): 4 concurrent runs in {timeRep1:F1} ms");
            }

            using (var modelRep2 = WhisperModelBuilder.Create(modelPath).WithDevice(device).WithNumReplicas(2).WithMemoryMapping().Build())
            {
                modelRep2.WarmUp();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => modelRep2.Transcribe(pcm).ToList())).ToArray();
                await Task.WhenAll(tasks);
                sw.Stop();
                timeRep2 = sw.Elapsed.TotalMilliseconds;
                PrintInfo($"NumReplicas = 2 (Parallel): 4 concurrent runs in {timeRep2:F1} ms");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 5: Batched Pipeline
            // ═══════════════════════════════════════════════════════════════
            PrintSubHeader("Phase 5: Batched Inference Pipeline");
            double seqTime = 0, batchedTime = 0;
            
            using (var model = WhisperModelBuilder.Create(modelPath).WithDevice(device).WithMemoryMapping().Build())
            {
                model.WarmUp();
                
                var swSeq = System.Diagnostics.Stopwatch.StartNew();
                var result1 = model.Transcribe(pcm).ToList();
                var result2 = model.Transcribe(pcm).ToList();
                swSeq.Stop();
                seqTime = swSeq.Elapsed.TotalMilliseconds;

                float[] longPcm = new float[pcm.Length * 2];
                Array.Copy(pcm, 0, longPcm, 0, pcm.Length);
                Array.Copy(pcm, 0, longPcm, pcm.Length, pcm.Length);

                using var pipeline = new BatchedInferencePipeline(model, batchSize: 2);
                var swBatch = System.Diagnostics.Stopwatch.StartNew();
                var batchResult = pipeline.Transcribe(longPcm);
                int count = batchResult.Segments.Count();
                swBatch.Stop();
                batchedTime = swBatch.Elapsed.TotalMilliseconds;

                PrintInfo($"Sequential (2x single run): {seqTime:F1} ms");
                PrintInfo($"Batched Pipeline (2x audio length): {batchedTime:F1} ms");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════
            // FINAL SUMMARY REPORT
            // ═══════════════════════════════════════════════════════════════
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│                     SDK ARCHITECTURAL & RESOURCE REPORT                      │");
            Console.WriteLine("├──────────────────────────────────────────────┬──────────────┬────────────────┤");
            Console.WriteLine("│ Metric / Configuration                       │ RAM Delta    │ VRAM Delta     │");
            Console.WriteLine("├──────────────────────────────────────────────┼──────────────┼────────────────┤");
            Console.WriteLine($"│ Model Load (Standard / No-Mmap)              │ {ramStdMb,9:F1} MB   │ {vramStdMb,9:F1} MB   │");
            Console.WriteLine($"│ Model Load (Memory-Mapped)                   │ {ramMmapMb,9:F1} MB   │ {vramMmapMb,9:F1} MB   │");
            Console.WriteLine($"│ Replica Scaling (NumReplicas = 1)            │ {replicaMetrics[0].RamMb,9:F1} MB   │ {replicaMetrics[0].VramMb,9:F1} MB   │");
            Console.WriteLine($"│ Replica Scaling (NumReplicas = 2)            │ {replicaMetrics[1].RamMb,9:F1} MB   │ {replicaMetrics[1].VramMb,9:F1} MB   │");
            Console.WriteLine($"│ Replica Scaling (NumReplicas = 4)            │ {replicaMetrics[2].RamMb,9:F1} MB   │ {replicaMetrics[2].VramMb,9:F1} MB   │");
            Console.WriteLine("├──────────────────────────────────────────────┼──────────────┼────────────────┤");
            Console.WriteLine("│ Performance Configuration / Load Type        │ Time (ms)    │ Speedup / RTF  │");
            Console.WriteLine("├──────────────────────────────────────────────┼──────────────┼────────────────┤");
            Console.WriteLine($"│ Model Load Time (Standard / No-Mmap)         │ {loadTimeStdMs,10:F1}   │ Baseline       │");
            Console.WriteLine($"│ Model Load Time (Memory-Mapped)              │ {loadTimeMmapMs,10:F1}   │ {(loadTimeStdMs / loadTimeMmapMs),12:F2}x │");
            Console.WriteLine($"│ Quantization 'default' (float32)             │ {defaultTime,10:F1}   │ RTF: {defaultRtf,7:F4} │");
            Console.WriteLine($"│ Quantization 'int8'                          │ {int8Time,10:F1}   │ RTF: {int8Rtf,7:F4} │");
            double quantSpeed = defaultTime / int8Time;
            Console.WriteLine($"│ Quantization Speedup                         │              │ {quantSpeed,13:F2}x │");
            Console.WriteLine("├──────────────────────────────────────────────┼──────────────┼────────────────┤");
            Console.WriteLine($"│ Concurrency (4 Tasks - Blocked Replica=1)    │ {timeRep1,10:F1}   │                │");
            Console.WriteLine($"│ Concurrency (4 Tasks - Parallel Replica=2)   │ {timeRep2,10:F1}   │                │");
            double scalingSpeed = timeRep1 / timeRep2;
            Console.WriteLine($"│ Concurrency Scaling Speedup                  │              │ {scalingSpeed,13:F2}x │");
            Console.WriteLine("├──────────────────────────────────────────────┼──────────────┼────────────────┤");
            Console.WriteLine($"│ Pipeline Sequential (2x Audio)               │ {seqTime,10:F1}   │                │");
            Console.WriteLine($"│ Pipeline Batched (2x Audio)                  │ {batchedTime,10:F1}   │                │");
            double batchSpeed = seqTime / batchedTime;
            Console.WriteLine($"│ Batched Pipeline Speedup                     │              │ {batchSpeed,13:F2}x │");
            Console.WriteLine("└──────────────────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();
        }

        static long GetGpuMemoryUsed()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=memory.used --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process == null) return -1;
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (long.TryParse(output, out long megabytes))
                {
                    return megabytes * 1024 * 1024; // Convert to bytes
                }
            }
            catch
            {
                // nvidia-smi not available or failed
            }
            return -1;
        }
    }

    class SimpleConsoleLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;
        public SimpleConsoleLogger(string categoryName) => _categoryName = categoryName;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var color = Console.ForegroundColor;
            Console.ForegroundColor = logLevel switch
            {
                Microsoft.Extensions.Logging.LogLevel.Error or Microsoft.Extensions.Logging.LogLevel.Critical => ConsoleColor.Red,
                Microsoft.Extensions.Logging.LogLevel.Warning => ConsoleColor.Yellow,
                Microsoft.Extensions.Logging.LogLevel.Information => ConsoleColor.DarkCyan,
                _ => ConsoleColor.DarkGray
            };
            Console.WriteLine($"    [ILogger - {_categoryName}] {msg}");
            Console.ForegroundColor = color;
        }
    }
}
