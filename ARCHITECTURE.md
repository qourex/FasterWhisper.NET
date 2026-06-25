# Architecture

> Technical architecture overview of **FasterWhisper.NET** — a high-performance C#/.NET wrapper for OpenAI's Whisper speech recognition model, powered by [CTranslate2](https://github.com/OpenNMT/CTranslate2).

## Layer Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                       │
│              (Your code using the public API)                │
├─────────────────────────────────────────────────────────────┤
│                   C# Managed Layer                           │
│  WhisperModel │ AudioProcessor │ WhisperTokenizer │ SileroVad│
├─────────────────────────────────────────────────────────────┤
│                  Native Interop (P/Invoke)                    │
│               NativeMethods.cs (LibraryImport)               │
├─────────────────────────────────────────────────────────────┤
│              C++ Native Layer (extern "C")                    │
│           qourex_fasterwhisper_native.cpp / .h               │
├─────────────────────────────────────────────────────────────┤
│                     CTranslate2 v4.7.0                       │
│         (Whisper model inference, beam search, CUDA)         │
├─────────────────────────────────────────────────────────────┤
│              Hardware (CPU / NVIDIA GPU)                      │
│              Intel MKL │ cuBLAS │ cuDNN                       │
└─────────────────────────────────────────────────────────────┘
```

## Transcription Pipeline

```
Audio File ──► LoadWav() / FFmpeg ──► Resample(16kHz) ──► Normalize/HPF/PreEmphasis
     │                                                           │
     ▼                                                           ▼
 [Optional]                                              ExtractMelSpectrogram()
 SileroVad ──► Speech segments                                   │
     │                                                           ▼
     ▼                                                  Native whisper_generate()
TranscribeChunk() ◄──── CTranslate2 beam search ◄──── Token prompt assembly
     │
     ▼
WhisperSegment ──► Word timestamps (align()) ──► Text post-processing ──► yield
```

## Memory Ownership Model

| Resource | Allocation | Lifetime | Release |
|:---------|:-----------|:---------|:--------|
| `_modelPtr` | Native `new Whisper(...)` | `WhisperModel` lifetime | `Dispose()` → `FreeWhisperModel()` |
| `NativeWhisperResult*` | Native `new NativeWhisperResult()` | Per-transcription | `free_whisper_result()` after marshalling |
| Mel features | `GCHandle.Alloc(..., Pinned)` | Duration of P/Invoke call | `GCHandle.Free()` in `finally` |
| ONNX session | `new InferenceSession(...)` | `SileroVad` lifetime | `SileroVad.Dispose()` |

## Error Propagation

```
Native C++:  catch (std::exception& e) → *error_msg = strdup(e.what())
     │
     ▼
P/Invoke:    Marshal.PtrToStringUTF8(errorPtr) → FreeString(errorPtr)
     │
     ▼
Managed:     throw new ExternalException($"Generation failed: {errorMsg}")
```

## Concurrency & Thread Safety

`WhisperModel` is thread-safe and supports concurrent transcription calls. Access is serialized via a `SemaphoreSlim` initialized with a capacity equal to `NumReplicas`.

- If `NumReplicas = 1` (default), concurrent transcription calls will queue and execute sequentially without causing native CTranslate2 access violations.
- If `NumReplicas > 1`, the underlying native CTranslate2 replica pool will process up to `NumReplicas` transcriptions concurrently, sharing the same loaded model weights in memory to prevent VRAM/RAM duplication.

All public methods throw `ObjectDisposedException` after `Dispose()` has been called.

## Audio Processing Pipeline

```
Input ──► [DenoiseAudio] ──► NormalizeRMS ──► HighPassFilter ──► PreEmphasis
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

### Supported Audio Formats (WAV)

| Format | `wFormatTag` | Bit Depths |
|:-------|:---:|:---|
| PCM | 1 | 8, 16, 24, 32 |
| IEEE Float | 3 | 32, 64 |
| A-law (G.711) | 6 | 8 |
| μ-law (G.711) | 7 | 8 |

Non-WAV formats (MP3, MP4, Opus, FLAC, etc.) are decoded via FFmpeg subprocess.

## Directory Structure

```
Qourex.FasterWhisper/
├── src/
│   ├── Qourex.FasterWhisper.NET/           # Main managed library
│   │   ├── AudioProcessor.cs               # WAV decoding, resampling, Mel extraction
│   │   ├── AudioQualityReport.cs           # Audio quality assessment report
│   │   ├── BatchedInferencePipeline.cs     # High-throughput batch inference pipeline
│   │   ├── HallucinationDetector.cs        # Repetition and silence hallucination detector
│   │   ├── ModelDownloader.cs              # HuggingFace model downloading
│   │   ├── NativeMethods.cs                # P/Invoke declarations
│   │   ├── SegmentMerger.cs                # Merges VAD-split chunks into segments
│   │   ├── SileroVad.cs                    # ONNX-based voice activity detection
│   │   ├── StreamingMelExtractor.cs        # Real-time streaming Mel spectrogram extraction
│   │   ├── SubtitleExporter.cs             # Subtitle exporting (SRT, VTT, JSON, TSV, Markdown)
│   │   ├── TextRestorer.cs                 # Normalization and formatting text restoration
│   │   ├── TranscriptionDiagnostics.cs     # Performance telemetry and diagnostics
│   │   ├── TranscriptionTypes.cs           # Transcription result data structures
│   │   ├── WhisperModel.cs                 # High-level transcription API
│   │   ├── WhisperModelBuilder.cs          # Fluent builder for model loading
│   │   ├── WhisperOptions.cs               # Configuration options
│   │   ├── WhisperSegment.cs               # Output segment and word structures
│   │   └── WhisperTokenizer.cs             # GPT-2 BPE tokenizer
│   ├── Qourex.FasterWhisper.NET.Gpu/       # GPU variant (Windows & Linux CUDA)
│   └── Qourex.FasterWhisper.Native/        # C++ native wrapper
│       ├── qourex_fasterwhisper_native.cpp  # CTranslate2 C API bridge
│       └── CMakeLists.txt                  # CMake build (fetches CTranslate2)
├── tests/
│   └── Qourex.FasterWhisper.NET.Tests/     # xUnit test suite
├── samples/
│   └── Qourex.FasterWhisper.NET.Samples/   # Console application feature demo
├── build.ps1                               # Windows build automation script
└── build.sh                                # Linux build automation script
```
