# Getting Started with FasterWhisper.NET

FasterWhisper.NET is an offline, high-performance speech-to-text SDK for modern .NET applications. Powered by the CTranslate2 inference engine and integrated with Silero VAD v5 ONNX, the library provides state-of-the-art speech recognition with zero cloud dependencies.

---

## System Prerequisites

FasterWhisper.NET links to optimized native binaries. Ensure the host environment meets the platform-specific requirements:

### Windows
- **CPU and GPU**: Install the [Microsoft Visual C++ Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe).
- **GPU (CUDA)**: Requires NVIDIA CUDA Toolkit 12.x and cuDNN 8.9.x (`cudnn64_8.dll`) runtime libraries to be accessible in the system `PATH`.

### Linux
- **CPU**: Install OpenBLAS or an equivalent BLAS library:
  ```bash
  sudo apt-get update && sudo apt-get install -y libopenblas-dev
  ```
- **GPU (CUDA)**: Requires NVIDIA CUDA 12.x driver/toolkit and cuDNN 8.9.x (`libcudnn.so.8`) libraries on the host system.

### macOS
- Uses the built-in Apple Accelerate framework for hardware-accelerated BLAS operations on both Apple Silicon (ARM64) and Intel (x64) architectures. No external BLAS dependencies are required.

### Android and iOS
- Native support is compiled for 64-bit architectures (`arm64-v8a` for Android, `arm64` for iOS). 32-bit platforms and x86 mobile emulators are not supported.

---

## Package Installation

FasterWhisper.NET is distributed via NuGet. Choose the package appropriate for your target environment:

### Standard Package (CPU Execution)
Recommended for cross-platform deployments across Windows, Linux, macOS, Android, and iOS:

```bash
dotnet add package FasterWhisper.NET --version 1.0.2
```

### GPU-Accelerated Package (NVIDIA CUDA)
Recommended for high-throughput CUDA acceleration on Windows and Linux x64 hosts:

```bash
dotnet add package FasterWhisper.NET.Gpu --version 1.0.2
```

---

## Quick Start: Audio Transcription

The following example demonstrates resolving and downloading a model from the Hugging Face Hub, configuring inference parameters, loading an audio file, and printing segmented transcription results:

> [!NOTE]
> FasterWhisper.NET operates on **16 kHz, single-channel (mono), 16-bit PCM** or 32-bit float audio. The built-in `AudioProcessor` automatically handles decoding, channel mixing, and Lanczos windowed-sinc resampling for standard WAV files.

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
            Console.WriteLine($"Audio file not found: {audioPath}");
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
            .WithDevice("cpu")             // Use "cuda" for GPU acceleration
            .WithComputeType("default")     // Use "float16" for GPU to maximize throughput
            .WithMemoryMapping()            // Memory-map weights for rapid initialization
            .WithNumReplicas(1);

        using var model = builder.Build();
        Console.WriteLine("Model loaded successfully.");

        // 3. Load and preprocess the audio file
        var audioProcessor = new AudioProcessor(model.NMels);
        float[] pcm = audioProcessor.LoadWav(audioPath);

        // 4. Configure decoding parameters
        var options = new WhisperOptions
        {
            BeamSize = 5,
            SamplingTemperature = 0.0f
        };

        // 5. Execute transcription
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

## Real-Time Streaming Transcription

FasterWhisper.NET supports real-time streaming using an asynchronous push pipeline. This architecture is designed for microphone feeds, network audio streams, and live captioning workflows:

```csharp
using System;
using System.Threading.Tasks;
using Qourex.FasterWhisper.NET;

// 1. Initialize the model
using var model = WhisperModelBuilder.Create(modelPath)
    .WithDevice("cpu")
    .Build();

// 2. Create the streaming context
using var stream = model.CreateStream();

// 3. Process transcription events concurrently in the background
var transcriptionTask = Task.Run(async () =>
{
    await foreach (var segment in stream.GetSegmentsAsync())
    {
        Console.WriteLine($"[{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");
    }
});

// 4. Ingest audio buffers (16 kHz mono float32 PCM)
float[] audioBuffer = new float[16000]; // 1 second of audio
stream.Push(audioBuffer);

// 5. Signal stream completion
stream.Finish();

// 6. Await remaining segment processing
await transcriptionTask;
```

---

## Available Models

FasterWhisper.NET supports all standard CTranslate2-compatible Whisper models. The `ModelDownloader` resolves model assets from Hugging Face repositories (such as `Systran/faster-whisper-*`):

| Model | Parameters | Disk Footprint | Approx. VRAM (FP16) | Recommended Target |
| :--- | :--- | :--- | :--- | :--- |
| **tiny** | 39 M | ~75 MB | ~150 MB | Low-latency edge, mobile, unit testing |
| **base** | 74 M | ~142 MB | ~250 MB | General desktop, rapid prototyping |
| **small** | 244 M | ~466 MB | ~600 MB | Balanced accuracy and throughput |
| **medium** | 769 M | ~1.5 GB | ~1.5 GB | High-accuracy transcription |
| **large-v3** | 1.55 B | ~3.1 GB | ~3.0 GB | Maximum accuracy, multilingual pipelines |
| **large-v3-turbo** | 809 M | ~1.6 GB | ~2.0 GB | High accuracy with optimized decoder layers |

### Precision and Compute Types

CTranslate2 supports multiple compute precisions to balance memory usage and inference throughput:

- **`default`**: Automatically selects the optimal compute precision for the host device.
- **`float16`**: Standard half-precision floating point. Recommended for NVIDIA GPUs to utilize Tensor Cores.
- **`int8`**: 8-bit integer quantization. Reduces memory consumption by approximately 50% and accelerates CPU execution via modern SIMD extensions (AVX-512 / AVX2 / NEON).
- **`int8_float16`**: Combines INT8 quantized weights with FP16 activation storage for reduced VRAM footprint.
- **`float32`**: Standard 32-bit single-precision floating point.
