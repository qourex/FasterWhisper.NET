# Advanced Usage & Features

FasterWhisper.NET includes a suite of advanced APIs, optimization pathways, and diagnostic tools designed for high-throughput server environments, low-latency live streams, and embedded use cases.

---

## 🛠️ Fluent Model Builder

For a cleaner, type-safe, and self-documenting configuration experience, use the `WhisperModelBuilder`. It allows you to specify target devices, compute types, active VAD segmentation, and audio post-processing pipelines inline:

```csharp
using Qourex.FasterWhisper.NET;

using var model = await WhisperModelBuilder.Create("base")
    .WithDevice("cuda")
    .WithComputeType("float16")
    .WithNumReplicas(2)
    .WithVad(threshold: 0.5f)
    .WithWordTimestamps()
    .WithDenoising()
    .BuildAsync();

var segments = model.Transcribe("meeting.wav");
```

---

## 👥 Concurrency & Multi-Replica Execution

By default, the `WhisperModel` class serializes transcription requests using a thread-safe internal semaphore. If your application needs to handle multiple concurrent transcription requests (such as in an ASP.NET Core web server), loading multiple model instances will bloat your RAM/VRAM.

### Shared-Weight Replica Pools
To solve this, FasterWhisper.NET allows you to initialize the model with multiple execution replicas using `numReplicas > 1` (or `.WithNumReplicas(N)` in the builder). 
- **CTranslate2** shares the underlying model weights and vocabulary tensors in memory.
- Only the execution activations are duplicated for each replica, resulting in a minimal memory increase (usually ~50MB per replica for the base model).

```csharp
// Load model with 2 replicas sharing weights in memory but running 2 parallel inferences
using var model = await WhisperModel.LoadAsync(
    modelNameOrPath: "base",
    device: "cpu",
    numReplicas: 2
);

// Transcribe files concurrently using Task.Run
var files = new[] { "audio1.wav", "audio2.wav", "audio3.wav" };
var tasks = files.Select(file => Task.Run(() =>
{
    var segments = model.Transcribe(file);
    Console.WriteLine($"Finished transcribing {file}");
}));

await Task.WhenAll(tasks);
```

---

## ⚡ Batched Inference Pipeline

For high-throughput processing of large audio archives or long podcasts, using standard transcription processes one segment sequentially. The `BatchedInferencePipeline` speeds this up:
1. It uses Silero VAD to segment the long audio file into individual speech chunks.
2. It groups these chunks together into a single batch (up to `batchSize`).
3. It runs inference concurrently on the batch, taking advantage of GPU tensor cores or multi-core CPU SIMD.
This typically yields a **1.5x to 3x** speedup over sequential execution.

```csharp
using Qourex.FasterWhisper.NET;

using var model = await WhisperModel.LoadAsync("base", device: "cuda");
using var pipeline = new BatchedInferencePipeline(model, batchSize: 8);

var result = pipeline.Transcribe("long_podcast.mp3");
foreach (var segment in result.Segments)
{
    Console.WriteLine($"[{segment.Start}s -> {segment.End}s] {segment.Text}");
}
```

---

## 🗣️ Voice Activity Detection (VAD)

**Silero VAD v5** runs natively via ONNX Runtime to identify speech boundaries. The VAD ONNX model weights (`silero_vad.onnx`) are automatically downloaded on first use.

> [!TIP]
> Enabling VAD is highly recommended for long audio files. It filters out silent intervals, preventing the model from hallucinating repetitive loops or blank transcripts. It also speeds up inference by avoiding processing silent audio sections.

```csharp
var vadOptions = new VadOptions
```
```csharp
{
    Enabled              = true,   // Enable Silero VAD segmentation
    Threshold            = 0.5f,   // Speech probability threshold (0.0–1.0)
    MinSpeechDurationMs  = 250,    // Discard speech segments shorter than this
    MinSilenceDurationMs = 100     // Split segments when silence exceeds this
};

var segments = model.Transcribe("meeting.mp3", language: null, vadOptions: vadOptions);
```

---

## ⏱️ Word-Level Timestamps

If you need precise word-by-word timestamps (for karaoke-style captions or alignment tasks), enable CTranslate2's native cross-attention alignment:

```csharp
var options = new WhisperOptions
{
    WordTimestamps   = true,
    MedianFilterWidth = 7     // Smoothing width for the cross-attention matrix
};

var segments = model.Transcribe("interview.wav", language: "en", options: options);

foreach (var segment in segments)
{
    Console.WriteLine($"[{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");

    foreach (var word in segment.Words)
    {
        Console.WriteLine($"  '{word.Word}' [{word.Start:F2}s -> {word.End:F2}s] (Confidence: {word.Probability:P0})");
    }
}
```

---

## 💿 In-Memory Model Loading

For environments with restricted file access, encrypted storage, or embedded databases, you can load Whisper models directly from byte arrays:

> [!WARNING]
> The loaded files dictionary **must** contain either `vocabulary.txt` or `vocabulary.json` to properly initialize the Whisper tokenizer. If the vocabulary file is missing, the initializer will throw a `KeyNotFoundException`.

```csharp
var modelFiles = new Dictionary<string, byte[]>
{
    ["model.bin"]       = File.ReadAllBytes("path/to/model.bin"),
    ["config.json"]     = File.ReadAllBytes("path/to/config.json"),
    ["vocabulary.txt"]  = File.ReadAllBytes("path/to/vocabulary.txt")
};

using var model = new WhisperModel(
    modelFiles,
    device: "cpu",
    computeType: "int8",
    cpuThreads: 4
);

var segments = model.Transcribe("audio.wav", language: "en");
```

---

## 🌍 Language Detection

You can let the library automatically detect the spoken language during transcription, or explicitly invoke the detector beforehand:

```csharp
float[] pcm = LoadAudioAsPcm("unknown_speech.wav"); // 16 kHz mono float32
var languages = model.DetectLanguage(pcm);

// Print the top 3 detected languages
foreach (var (language, probability) in languages.Take(3))
{
    Console.WriteLine($"Language: {language} (Probability: {probability:P1})");
}

// Auto-detect during transcription
var segments = model.Transcribe("unknown_speech.wav", language: null);
```

---

## 🎵 Audio Preprocessing & Formats

FasterWhisper.NET provides built-in DSP operations to clean up audio signals before sending them to the neural network:

```csharp
var options = new WhisperOptions
{
    NormalizeAudio    = true,   // RMS volume normalization (Default: true)
    CutLowFrequencies = true,   // High-pass filter at 80 Hz to remove DC offset/hum (Default: true)
    PreEmphasis       = false,  // Boost high-frequency clarity
    DenoiseAudio      = false   // Spectral noise gate for background static noise removal
};
```

### Supported Formats
| Format | Decoding Pathway |
| :--- | :--- |
| **WAV (PCM)** | Direct, fast managed decoding (8/16/24/32-bit PCM, IEEE floats, A-law, μ-law). |
| **MP3, MP4, FLAC, Opus** | Decoded using a local **FFmpeg** subprocess. (FFmpeg must be installed and in the system `PATH`). |
| **Raw Arrays** | Pass custom `float[]` PCM data directly (requires 16kHz, single-channel mono). |

---

## 📝 Text Post-Processing Filters

Whisper transcripts occasionally contain filler words or stutters. You can enable automatic post-inference cleanup filters:

```csharp
var options = new WhisperOptions
{
    FilterFillerWords       = true,  // Removes "uh", "um", "ah", "eh", "mhm", etc.
    PruneStutters           = true,  // Removes consecutive duplicate words ("the the" -> "the")
    ConditionOnPreviousText = true   // Feeds previous segment text back to maintain context (Default: true)
};
```

---

## 📈 Audio Quality Assessment

Before committing computing resources to transcription, you can run the built-in non-intrusive analyzer to assess the audio signal quality. It provides a quality grade and actionable suggestions:

```csharp
float[] samples = WhisperModel.LoadAudio("input.wav");
var report = AudioQualityReport.Assess(samples);

Console.WriteLine($"Quality Grade: {report.OverallGrade}"); // Excellent, Good, Fair, Poor
Console.WriteLine($"Signal-to-Noise Ratio (SNR): {report.SignalToNoiseRatio:F1} dB");

foreach (var suggestion in report.Suggestions)
{
    Console.WriteLine($"Diagnostic: {suggestion}");
}
```

---

## 📝 Subtitle Exporting

Easily convert transcription results into standard subtitle formats for video players and media indexing pipelines:

```csharp
var segments = model.Transcribe("movie.wav");

// 1. Export as SRT string
string srtContent = SubtitleExporter.ToSrt(segments);
File.WriteAllText("movie.srt", srtContent);

// 2. Export as WebVTT directly to a file path
SubtitleExporter.WriteVtt(segments, "movie.vtt");

// 3. Other formats: ToTsv(segments) or ToJson(segments) are also available
```
