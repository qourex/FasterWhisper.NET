// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Qourex.FasterWhisper.NET;

namespace Qourex.FasterWhisper.NET.Samples.Console.Gpu
{
    class Program
    {
        static async Task Main(string[] args)
        {
            System.Console.WriteLine("=== FasterWhisper.NET GPU Console Sample ===");

            string modelName = "tiny";
            string audioPath = Path.Combine(AppContext.BaseDirectory, "harvard.wav");

            if (!File.Exists(audioPath))
            {
                System.Console.WriteLine($"Error: Audio file not found at {audioPath}");
                return;
            }

            // 1. Download/Resolve the Model from Hugging Face
            System.Console.WriteLine($"Resolving model '{modelName}'...");
            var downloader = new ModelDownloader();
            var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    double percent = (double)p.BytesRead / p.TotalBytes * 100;
                    System.Console.Write($"\rDownloading {p.FileName}: {percent:F1}%");
                }
            });
            string modelPath = await downloader.GetModelPathAsync(modelName, progress);
            System.Console.WriteLine($"\nModel resolved at: {modelPath}");

            // 2. Build the Model with CUDA and default compute type (fallbacks automatically)
            System.Console.WriteLine("Loading model on GPU (CUDA)...");
            var builder = WhisperModelBuilder.Create(modelPath)
                .WithDevice("cuda")
                .WithComputeType("default")
                .WithMemoryMapping()
                .WithNumReplicas(1);

            using var model = builder.Build();
            System.Console.WriteLine("Model loaded successfully.");

            // 3. Load Audio and Transcribe
            System.Console.WriteLine($"Transcribing: {audioPath}...");
            var audioProcessor = new AudioProcessor(model.NMels);
            float[] pcm = audioProcessor.LoadWav(audioPath);

            var options = new WhisperOptions
            {
                BeamSize = 5
            };

            var segments = model.Transcribe(pcm, language: "en", options: options);

            System.Console.WriteLine("\n--- Transcript ---");
            foreach (var segment in segments)
            {
                System.Console.WriteLine($"[{TimeSpan.FromSeconds(segment.Start):hh\\:mm\\:ss\\.fff} -> {TimeSpan.FromSeconds(segment.End):hh\\:mm\\:ss\\.fff}] {segment.Text}");
            }
            System.Console.WriteLine("------------------\n");

            System.Console.WriteLine("Finished successfully.");
        }
    }
}
