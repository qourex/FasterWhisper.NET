# Advanced Features and Optimization

FasterWhisper.NET includes advanced APIs and optimization pathways engineered for high-throughput server architectures, real-time streaming services, and resource-constrained environments.

---

## Fluent Model Builder

The `WhisperModelBuilder` provides a strongly-typed, discoverable fluent interface for model instantiation, device allocation, replica scaling, and pre-processing pipeline configuration:

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

## Concurrency and Multi-Replica Execution

By default, `WhisperModel` coordinates transcription requests using an internal `SemaphoreSlim` to ensure thread safety. In high-concurrency environments (such as ASP.NET Core web APIs), loading multiple independent model instances leads to unnecessary memory duplication.

### Shared-Weight Replica Pools

FasterWhisper.NET supports multi-replica scaling via the `numReplicas` parameter (or `.WithNumReplicas(N)` in the builder):
- **CTranslate2 Architecture**: Shares base weight tensors and vocabulary embeddings in memory across all worker replicas.
- **Memory Footprint**: Only execution activations and workspace buffers are allocated per replica (~50 MB per worker for `base`), avoiding linear memory growth.

```csharp
// Load model with 2 replicas sharing weights in memory
using var model = await WhisperModel.LoadAsync(
    modelNameOrPath: "base",
    device: "cpu",
    numReplicas: 2
);

// Transcribe multiple files concurrently
var files = new[] { "audio1.wav", "audio2.wav", "audio3.wav" };
var tasks = files.Select(file => Task.Run(() =>
{
    var segments = model.Transcribe(file);
    Console.WriteLine($"Completed transcription: {file}");
}));

await Task.WhenAll(tasks);
```

---

## Batched Inference Pipeline

When transcribing extended audio recordings (e.g., podcasts, depositions, lectures), sequential 30-second window processing underutilizes modern GPU tensor cores. The `BatchedInferencePipeline` optimizes throughput:

1. Partitions audio into distinct speech segments using Silero VAD.
2. Combines segments into parallel batches (up to `batchSize`).
3. Executes batch inference concurrently via CTranslate2.

This yields a **1.5x to 3x throughput improvement** over sequential processing on CUDA devices.

```csharp
using Qourex.FasterWhisper.NET;

using var model = await WhisperModel.LoadAsync("base", device: "cuda");
using var pipeline = new BatchedInferencePipeline(model, batchSize: 8);

var result = pipeline.Transcribe("extended_recording.mp3");
foreach (var segment in result.Segments)
{
    Console.WriteLine($"[{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");
}
```

---

## Voice Activity Detection (VAD)

Silero VAD v5 executes natively via ONNX Runtime to identify speech boundaries. The model binary (`silero_vad.onnx`) is resolved and cached locally on first use.

> [!TIP]
> Enabling VAD is strongly recommended for long audio files. By filtering out unvoiced intervals, VAD prevents the model from generating hallucinated text during background noise and reduces total compute duration.

```csharp
var vadOptions = new VadOptions
{
    Enabled              = true,   // Enable Silero VAD segmentation
    Threshold            = 0.5f,   // Speech probability threshold (0.0 to 1.0)
    MinSpeechDurationMs  = 250,    // Discard speech intervals shorter than 250ms
    MinSilenceDurationMs = 1000    // Split segments on silence exceeding 1000ms
};

var segments = model.Transcribe("recording.mp3", language: null, vadOptions: vadOptions);
```

---

## Word-Level Timestamps

For precise subtitle synchronization, interactive media players, and search indexing, enable cross-attention alignment:

```csharp
var options = new WhisperOptions
{
    WordTimestamps    = true,
    MedianFilterWidth = 7 // Smoothing kernel width for cross-attention matrix
};

var segments = model.Transcribe("interview.wav", language: "en", options: options);

foreach (var segment in segments)
{
    Console.WriteLine($"[{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");

    foreach (var word in segment.Words)
    {
        Console.WriteLine($"  '{word.Word}' [{word.Start:F2}s -> {word.End:F2}s] (Confidence: {word.Probability:P1})");
    }
}
```

---

## In-Memory Model Loading

For environments with encrypted storage, database-backed model binaries, or restricted disk access, models can be loaded directly from memory:

> [!WARNING]
> The dictionary **must** include either `vocabulary.txt` or `vocabulary.json` alongside `model.bin` and `config.json`. If vocabulary data is omitted, initialization will throw a `KeyNotFoundException`.

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

## Language Detection and Multi-Lingual Processing

The SDK supports automated language identification across 99+ supported languages, with confidence scoring:

```csharp
float[] pcm = audioProcessor.LoadWav("multilingual_sample.wav");
var detectedLanguages = model.DetectLanguage(pcm);

// Inspect top candidate languages
foreach (var (language, probability) in detectedLanguages.Take(3))
{
    Console.WriteLine($"Language: {language} (Confidence: {probability:P1})");
}

// Automatically detect during transcription
var segments = model.Transcribe("multilingual_sample.wav", language: null);
```

---

## Audio Preprocessing and Format Support

FasterWhisper.NET includes built-in Digital Signal Processing (DSP) routines executed prior to spectrogram extraction:

```csharp
var options = new WhisperOptions
{
    NormalizeAudio    = true,   // Standardize signal amplitude (-20 dBFS target)
    CutLowFrequencies = true,   // 80 Hz high-pass filter (removes DC offset and hum)
    PreEmphasis       = false,  // First-order high-frequency boost
    DenoiseAudio      = false   // Spectral subtraction for stationary background noise
};
```

### Supported Media Formats

| Format | Ingestion Pipeline |
| :--- | :--- |
| **WAV (PCM / Float / Log-PCM)** | Direct managed decoding for 8/16/24/32-bit PCM, 32/64-bit IEEE float, A-law, and μ-law. |
| **MP3, MP4, FLAC, Opus, AAC** | Decoded automatically via local FFmpeg subprocess when present on system `PATH`. |
| **Raw Buffer** | Direct ingestion of 16 kHz mono `float[]` PCM arrays. |

---

## Text Post-Processing Filters

Output streams can be cleaned up using integrated text filtering rules:

```csharp
var options = new WhisperOptions
{
    FilterFillerWords       = true,  // Removes verbal hesitations ("uh", "um", "ah", "eh", "mhm")
    PruneStutters           = true,  // Removes consecutive duplicate words ("the the" -> "the")
    ConditionOnPreviousText = true   // Retains preceding context for window continuity
};
```

---

## Audio Quality Assessment

Evaluate signal quality before invoking compute-heavy transcription models using non-intrusive SNR and clipping analysis:

```csharp
float[] samples = WhisperModel.LoadAudio("input.wav");
var report = AudioQualityReport.Assess(samples);

Console.WriteLine($"Signal Quality Grade: {report.OverallGrade}"); // Excellent, Good, Fair, Poor
Console.WriteLine($"Signal-to-Noise Ratio: {report.SignalToNoiseRatio:F1} dB");

foreach (var suggestion in report.Suggestions)
{
    Console.WriteLine($"Recommendation: {suggestion}");
}
```

---

## Subtitle and Transcript Exporting

Format transcription segments directly into standardized subtitle and data interchange formats:

```csharp
var segments = model.Transcribe("presentation.wav");

// 1. Export as SubRip Subtitle (SRT) format string
string srtData = SubtitleExporter.ToSrt(segments);
File.WriteAllText("presentation.srt", srtData);

// 2. Export directly to WebVTT file
SubtitleExporter.WriteVtt(segments, "presentation.vtt");

// 3. Export as TSV or JSON data structures
string tsvData = SubtitleExporter.ToTsv(segments);
string jsonData = SubtitleExporter.ToJson(segments);
```
