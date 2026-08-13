# System Architecture

> Technical architecture and interop specification for **FasterWhisper.NET** — a high-performance C# / .NET SDK for OpenAI Whisper speech recognition, powered by [CTranslate2](https://github.com/OpenNMT/CTranslate2).

---

## Architectural Layering

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                       │
│        (ASP.NET Core, Blazor, WinForms, MAUI, Console)       │
├─────────────────────────────────────────────────────────────┤
│                   C# Managed Layer                           │
│  WhisperModel │ AudioProcessor │ WhisperTokenizer │ SileroVad│
├─────────────────────────────────────────────────────────────┤
│                  Native Interop (P/Invoke)                   │
│               NativeMethods.cs (LibraryImport)               │
├─────────────────────────────────────────────────────────────┤
│              C++ Native Layer (extern "C")                   │
│           qourex_fasterwhisper_native.cpp / .h               │
├─────────────────────────────────────────────────────────────┤
│                     CTranslate2 v4.7.0                       │
│         (Whisper model inference, beam search, CUDA)         │
├─────────────────────────────────────────────────────────────┤
│              Hardware Acceleration Runtimes                  │
│        Intel oneMKL │ OpenBLAS │ NVIDIA cuBLAS │ cuDNN       │
└─────────────────────────────────────────────────────────────┘
```

---

## Transcription Execution Pipeline

```
Audio Input ──► LoadWav() / FFmpeg ──► Resample(16kHz) ──► Normalize / HighPass / PreEmphasis
     │                                                               │
     ▼                                                               ▼
 [Optional]                                               ExtractMelSpectrogram()
 SileroVad ──► Active speech chunks                                  │
     │                                                               ▼
     ▼                                                    Native whisper_generate()
TranscribeChunk() ◄──── CTranslate2 beam search ◄──── Token prompt context assembly
     │
     ▼
WhisperSegment ──► Cross-attention align() ──► Text post-processing ──► Output Stream
```

---

## Memory Management and Ownership Model

| Resource | Allocation Strategy | Lifetime | Release Mechanism |
| :--- | :--- | :--- | :--- |
| `_modelPtr` | Native `new Whisper(...)` | `WhisperModel` instance | `Dispose()` → `FreeWhisperModel()` |
| `NativeWhisperResult*` | Native `new NativeWhisperResult()` | Per-transcription call | `free_whisper_result()` post-marshaling |
| Mel Features Buffer | `GCHandle.Alloc(..., Pinned)` | Duration of P/Invoke call | `GCHandle.Free()` in managed `finally` block |
| ONNX Inference Session | `new InferenceSession(...)` | `SileroVad` instance | `SileroVad.Dispose()` |

---

## Error Propagation and Exception Safety

```
Native C++:  catch (const std::exception& e) ──► *error_msg = strdup(e.what())
     │
     ▼
P/Invoke:    Marshal.PtrToStringUTF8(errorPtr) ──► FreeString(errorPtr)
     │
     ▼
Managed:     throw new ExternalException($"Inference failed: {errorMsg}")
```

---

## Concurrency and Thread Safety

`WhisperModel` coordinates concurrent transcription requests using an internal `SemaphoreSlim` initialized to the model's `NumReplicas` capacity:

- **Single Replica (`NumReplicas = 1`)**: Concurrent transcription calls are queued safely and executed sequentially, preventing native memory access collisions without throwing exceptions.
- **Multiple Replicas (`NumReplicas > 1`)**: The native CTranslate2 replica pool processes up to `NumReplicas` transcription requests concurrently in parallel. Replicas share the same base model weight tensors in memory, preventing RAM and VRAM duplication.

All public APIs implement `ObjectDisposedException` guards once `Dispose()` has been called.

---

## Digital Signal Processing (DSP) Pipeline

```
Input Audio ──► [DenoiseAudio] ──► NormalizeRMS ──► HighPassFilter ──► PreEmphasis
                                                                         │
                                                                         ▼
                                                    Hann Window ──► FFT ──► |FFT|²
                                                                         │
                                                                         ▼
                                                   Mel Filter Bank ──► log10 ──► clamp
                                                                         │
                                                                         ▼
                                                                Log-Mel Spectrogram
                                                               (nMels × 3000 frames)
```

### Supported WAV Container Formats

| Format Name | `wFormatTag` | Bit Depths |
| :--- | :---: | :--- |
| **PCM** | 1 | 8, 16, 24, 32 |
| **IEEE Float** | 3 | 32, 64 |
| **A-law (G.711)** | 6 | 8 |
| **μ-law (G.711)** | 7 | 8 |

*Non-WAV formats (MP3, MP4, Opus, FLAC, AAC) are decoded via a local FFmpeg subprocess if present on the system `PATH`.*

---

## Repository Structure

```
Qourex.FasterWhisper/
├── src/
│   ├── Qourex.FasterWhisper.NET/           # Core managed library (NuGet package)
│   │   ├── AudioProcessor.cs               # WAV decoding, resampling, Mel extraction
│   │   ├── AudioQualityReport.cs           # Audio quality grading and SNR estimation
│   │   ├── BatchedInferencePipeline.cs     # High-throughput batch inference pipeline
│   │   ├── HallucinationDetector.cs        # Repetition and silence hallucination mitigation
│   │   ├── ModelDownloader.cs              # Hugging Face model resolution and download
│   │   ├── NativeMethods.cs                # Source-generated P/Invoke declarations
│   │   ├── SegmentMerger.cs                # Merges VAD chunks into continuous segments
│   │   ├── SileroVad.cs                    # Silero VAD v5 ONNX integration
│   │   ├── StreamingMelExtractor.cs        # Real-time streaming Mel extractor
│   │   ├── SubtitleExporter.cs             # SRT, WebVTT, TSV, JSON, Markdown export
│   │   ├── TextRestorer.cs                 # Output capitalization and punctuation formatting
│   │   ├── TranscriptionDiagnostics.cs     # RTF and telemetry diagnostics
│   │   ├── TranscriptionTypes.cs           # Public result models
│   │   ├── WhisperModel.cs                 # Primary transcription API
│   │   ├── WhisperModelBuilder.cs          # Fluent configuration builder
│   │   ├── WhisperOptions.cs               # Generation and DSP options
│   │   ├── WhisperSegment.cs               # Segment and word data structures
│   │   └── WhisperTokenizer.cs             # GPT-2 BPE tokenizer implementation
│   ├── Qourex.FasterWhisper.NET.Gpu/       # GPU package project and runtime targets
│   └── Qourex.FasterWhisper.Native/        # C++ native interop wrapper
│       ├── qourex_fasterwhisper_native.cpp  # CTranslate2 C API bridge
│       └── CMakeLists.txt                  # CMake build definition
├── tests/
│   └── Qourex.FasterWhisper.NET.Tests/     # xUnit test suite (152 tests)
├── samples/                                # 10 sample applications for .NET 10.0
├── docs/                                   # VitePress documentation portal
├── build.ps1                               # PowerShell build script
├── build.sh                                # Bash build script
└── Qourex.FasterWhisper.slnx              # Solution file
```
