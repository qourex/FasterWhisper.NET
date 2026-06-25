# Changelog

All notable changes to FasterWhisper.NET will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Testing**: Expanded xUnit test suite from 16 to 152 tests, adding comprehensive integration, performance, memory pooling, and edge-case test coverage
- **Security**: Added SHA-256 integrity check for downloaded Silero VAD ONNX models
- **Security**: Expanded ModelDownloader to verify SHA-256 hashes of all official Hugging Face models (tiny through large-v3)

### Changed

- **Audio Processing**: Replaced linear interpolation resampler with Lanczos windowed-sinc resampler for proper anti-aliasing
- **Audio Processing**: Expanded WAV decoder to support 8/16/24/32-bit PCM, 32/64-bit IEEE float, A-law, and μ-law
- **Audio Processing**: Implemented `DenoiseAudio` spectral noise gate for stationary background noise reduction
- **Robustness**: Added `ObjectDisposedException` guards to all public methods after `Dispose()`
- **Robustness**: Fixed `.GetAwaiter().GetResult()` deadlock risk with `Task.Run()` wrapper
- **Robustness**: Fixed WAV reader to handle odd chunk sizes per RIFF spec
- **Code Quality**: Fixed `SileroVad` to use shared static `HttpClient` instance
- **Code Quality**: Fixed `SileroVad.Dispose()` to follow standard `IDisposable` pattern with finalizer
- **Code Quality**: Cached `BuildBytesToUnicodeMap()` as static field
- **Code Quality**: Replaced string concatenation with `StringBuilder`
- **Native**: Removed all `printf`/`fflush` debug statements from production native code
- **Native**: Wrapped `free_*` functions in `try/catch(...)` to prevent exceptions escaping

### Removed

- Removed `debug_log.txt` from repository

## [1.0.0] - 2026-06-22

### Added

- **Core Transcription**: Full Whisper transcription pipeline powered by CTranslate2
  - Beam search decoding with configurable beam size, patience, and penalties
  - Temperature fallback strategy with compression ratio and log probability validation
  - Auto language detection for multilingual models
  - Context window conditioning on previous transcribed text
- **Model Management**
  - Automatic model downloading from Hugging Face Hub (`tiny`, `base`, `small`, `medium`, `large-v1`, `large-v2`, `large-v3`)
  - In-memory model loading from `Dictionary<string, byte[]>` for embedded/serverless scenarios
  - CPU and CUDA device support with configurable compute types (`float32`, `float16`, `int8`, `int8_float16`, `int16`)
  - Flash Attention support for modern NVIDIA GPUs
- **Word-Level Timestamps**
  - Cross-attention alignment via native CTranslate2 `align()` API
  - GPT-2 BPE token-to-word grouping with probability scores
  - Configurable median filter width for alignment smoothing
- **Voice Activity Detection (VAD)**
  - Silero VAD v5 integration via ONNX Runtime
  - Automatic model download and caching
  - Context window management for accurate streaming detection
  - Configurable threshold, minimum speech duration, and minimum silence duration
- **Streaming Transcription**
  - Real-time `IAsyncEnumerable<WhisperSegment>` API via `TranscribeStreamAsync`
  - VAD-based utterance boundary detection
  - Automatic memory management with audio buffer pruning
- **Audio Processing**
  - WAV file loading (8/16/24/32-bit PCM, 32/64-bit IEEE float, A-law, μ-law)
  - Lanczos windowed-sinc resampler (any sample rate → 16kHz)
  - Spectral noise gate for stationary background noise reduction
  - FFmpeg subprocess decoder for MP3, MP4, Opus, and other formats
  - RMS volume normalization (target -20 dBFS)
  - High-pass filter (80Hz cutoff) for DC offset and microphone hum removal
  - Log-Mel spectrogram extraction (80 or 128 channels)
- **Text Post-Processing**
  - Filler word filtering (`uh`, `um`, `ah`, `eh`, `uh-huh`, `mhm`)
  - Stutter/repetition pruning (consecutive duplicate words)
- **Platform Support**
  - Multi-targeting: .NET 8.0, .NET 9.0, .NET 10.0
  - `[LibraryImport]` source-generated P/Invoke for Native AOT compatibility
  - NuGet package with bundled native DLLs (`runtimes/win-x64/native/`)
- **Build & Tooling**
  - `build.ps1` automation script (supports `-CpuOnly` flag)
  - GitHub Actions CI workflow
  - Comprehensive xUnit test suite (16 tests)

[Unreleased]: https://github.com/Qourex/FasterWhisper.NET/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Qourex/FasterWhisper.NET/releases/tag/v1.0.0
