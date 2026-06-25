# Getting Started with FasterWhisper.NET

**FasterWhisper.NET** is an offline, high-performance speech-to-text library for modern C# and .NET applications. By wrapping the **CTranslate2** inference engine and integrating **Silero VAD v5 ONNX**, it provides developers with state-of-the-art C# speech recognition capabilities without the need for active cloud dependencies.

---

## 🛠️ System Prerequisites

FasterWhisper.NET runs on native interop libraries. Depending on your operating system, make sure the following native dependencies are met:

### Windows
- **CPU & GPU**: [Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe) must be installed.
- **GPU (CUDA)**: Requires the CUDA Toolkit 12.x and cuDNN 9.x libraries to be installed and available in the system path (`PATH`).

### Linux
- **CPU**: Install OpenBLAS or compatible BLAS libraries:
  ```bash
  sudo apt-get update && sudo apt-get install -y libopenblas-dev
  ```
- **GPU (CUDA)**: CUDA 12.x driver/toolkit and cuDNN 9.x libraries must be installed on the host.

### macOS
- Uses Apple's built-in **Apple Accelerate** framework. No external BLAS installation is necessary.

### Android / iOS
- Fully supported on 64-bit architectures (`arm64-v8a` for Android, `arm64` for iOS). 32-bit platforms/simulators are not supported.

---

## 📦 Installation

FasterWhisper.NET is distributed as two primary NuGet packages. Choose the one that fits your hardware target:

### 1. CPU-Only Package
For standard CPU execution on Windows, Linux, macOS, Android, and iOS:
```bash
dotnet add package FasterWhisper.NET --version 1.0.2
```

### 2. GPU-Accelerated Package
For CUDA-enabled graphics cards on Windows and Linux (this package includes cuDNN and cublas dependencies inside the RID folders, but still requires the system to have a CUDA-compatible driver installed):
```bash
dotnet add package FasterWhisper.NET.Gpu --version 1.0.2
```

---

## 🚀 Quick Start (Transcribing a WAV File)

Here is a complete, copy-pasteable console application that downloads the `tiny` model, loads it into memory, processes a WAV file, and outputs the transcription.

> [!NOTE]
> FasterWhisper.NET requires **16kHz, single-channel (mono), 16-bit PCM** WAV files. Use the built-in `AudioProcessor` helper or tools like FFmpeg to transcode input files before processing.

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Qourex.FasterWhisper.NET;

class Program
{
    static async Task Main()
    {
        string modelName = "tiny";
        string audioPath = "input.wav";

        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Please provide a valid wav file at: {audioPath}");
            return;
        }

        // 1. Resolve and download the model from Hugging Face
        Console.WriteLine($"Resolving model '{modelName}'...");
        var downloader = new ModelDownloader();
        var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
        {
            if (p.TotalBytes > 0)
            {
                double percent = (double)p.BytesRead / p.TotalBytes * 100;
                Console.Write($"\rDownloading {p.FileName}: {percent:F1}%");
            }
        });
        
        string modelPath = await downloader.GetModelPathAsync(modelName, progress);
        Console.WriteLine($"\nModel resolved at: {modelPath}");

        // 2. Initialize and configure the Whisper model builder
        var builder = WhisperModelBuilder.Create(modelPath)
            .WithDevice("cpu")             // Use "cuda" if using FasterWhisper.NET.Gpu
            .WithComputeType("default")     // Set "float16" for GPU to maximize speed
            .WithMemoryMapping()            // Memory-map weights for instant load
            .WithNumReplicas(1);

        using var model = builder.Build();
        Console.WriteLine("Model loaded successfully.");

        // 3. Load and transcode the audio using AudioProcessor
        var audioProcessor = new AudioProcessor(model.NMels);
        float[] pcm = audioProcessor.LoadWav(audioPath);

        // 4. Set transcription parameters
        var options = new WhisperOptions
        {
            BeamSize = 5,
            Temperature = 0.0f
        };

        // 5. Transcribe
        var segments = model.Transcribe(pcm, language: "en", options: options);

        Console.WriteLine("\n--- Transcript ---");
        foreach (var segment in segments)
        {
            Console.WriteLine($"[{TimeSpan.FromSeconds(segment.Start):hh\\:mm\\:ss} -> {TimeSpan.FromSeconds(segment.End):hh\\:mm\\:ss}] {segment.Text}");
        }
    }
}
```

---

## 🔄 Real-time Streaming Transcription

FasterWhisper.NET supports real-time streaming using an asynchronous push pipeline. This is ideal for microphone inputs or audio feeds.

```csharp
using Qourex.FasterWhisper.NET;

// Initialize the model
using var model = WhisperModelBuilder.Create(modelPath)
    .WithDevice("cpu")
    .Build();

// Create the stream context
using var stream = model.CreateStream();

// Start the transcription task in the background
var transcriptionTask = Task.Run(async () =>
{
    await foreach (var segment in stream.GetSegmentsAsync())
    {
        Console.WriteLine($"Streamed Segment: [{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");
    }
});

// Push audio chunks (16kHz PCM float array) periodically
float[] chunk = new float[16000]; // 1 second of audio
stream.Push(chunk);

// Tell the stream that no more audio is coming
stream.Finish();

// Wait for the final segments to process
await transcriptionTask;
```

---

## 🗃️ Available Models

FasterWhisper.NET supports all standard CTranslate2-compatible Whisper models. The `ModelDownloader` resolves model names from the official repositories on Hugging Face (such as `Systran/faster-whisper-*`).

| Model Name | Parameter Count | VRAM Requirement | Recommended Use Case |
| :--- | :--- | :--- | :--- |
| **tiny** | 39 M | ~150 MB | Ultra-fast CPU execution, mobile devices |
| **base** | 74 M | ~250 MB | Low-resource desktop, quick prototyping |
| **small** | 244 M | ~600 MB | Good balance of speed and accuracy |
| **medium** | 769 M | ~1.5 GB | Server-side transcribing, high accuracy |
| **large-v3** | 1.5 B | ~3.0 GB | Maximum accuracy, multilingual support |

### Compute Types and Quantization
CTranslate2 allows you to run models at different precisions (compute types) to reduce size and speed up inference:
- **`default`**: Automatically picks the best type based on hardware capabilities.
- **`float16`**: Recommended for GPU (`cuda`) to take advantage of Tensor Cores.
- **`int8`**: Quantizes model weights to 8-bit integers. Reduces memory consumption by half and speeds up CPU inference on modern instruction sets (AVX-512 / AVX2).
