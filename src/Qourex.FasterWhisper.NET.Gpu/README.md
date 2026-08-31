![FasterWhisper.NET Banner](https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/social-card.png)

# FasterWhisper.NET.Gpu

**by [Qourex](https://qourex.com)** — GPU-Accelerated Speech Recognition for .NET

[![Build & Test](https://github.com/qourex/fasterwhisper.net/actions/workflows/build.yml/badge.svg)](https://github.com/qourex/fasterwhisper.net/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/FasterWhisper.NET.Gpu.svg?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/FasterWhisper.NET.Gpu)
[![Downloads](https://img.shields.io/nuget/dt/FasterWhisper.NET.Gpu.svg?style=flat-square&logo=nuget&label=Downloads)](https://www.nuget.org/packages/FasterWhisper.NET.Gpu)
[![Documentation](https://img.shields.io/badge/docs-VitePress-brightgreen.svg?style=flat-square)](https://qourex.github.io/FasterWhisper.NET/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)

**[Documentation Portal](https://qourex.github.io/FasterWhisper.NET/)** — Guides, API references, .NET 10.0 samples, and mobile deployment walkthroughs.

---

**FasterWhisper.NET.Gpu** is the GPU-accelerated distribution of FasterWhisper.NET. It bundles pre-compiled native binaries built with **NVIDIA CUDA** and **cuDNN** enabled for CTranslate2, delivering high-throughput speech recognition on NVIDIA graphics cards.

For CPU-only environments without CUDA dependencies, use the base [FasterWhisper.NET](https://www.nuget.org/packages/FasterWhisper.NET) package.

---

## Key GPU Advantages

- **CUDA and cuDNN Acceleration** — Native GPU execution for all Whisper model variants.
- **Flash Attention Support** — Significant throughput improvements on Ampere (RTX 30-series), Ada Lovelace (RTX 40-series), and Blackwell (RTX 50-series) architectures.
- **Mixed Precision Compute** — Native support for `"float16"` and `"int8_float16"` compute precisions to reduce VRAM footprint while maximizing tensor core utilization.
- **Parallel Mel Feature Extraction** — Multi-threaded DSP audio preprocessing pipeline executing on CPU threads before GPU batch scheduling.

---

## Installation

Install the GPU-enabled package via the .NET CLI:

```bash
dotnet add package FasterWhisper.NET.Gpu
```

---

## CUDA Prerequisites

To run this package with GPU acceleration (`device: "cuda"`), verify that the host system has compatible NVIDIA runtime libraries installed:

### Windows Requirements
1. **NVIDIA CUDA Toolkit 12.x** — [CUDA Downloads](https://developer.nvidia.com/cuda-downloads)
2. **NVIDIA cuDNN 8.9.x** (Required: `cudnn64_8.dll`) — [cuDNN 8.x Archive Downloads](https://developer.nvidia.com/cudnn-archive)
   > **Note:** cuDNN 8.9.x is strictly required. cuDNN 9 (`cudnn64_9.dll`) is currently not supported.

Ensure the following dynamic libraries are present in your system `PATH`:
- `cudart64_12.dll` (or active CUDA 12 runtime)
- `cublas64_12.dll`
- `cublasLt64_12.dll`
- `cudnn64_8.dll` (and `cudnn_*.dll` helper libraries)

### Linux and WSL2 Requirements
1. **NVIDIA CUDA Toolkit 12.x** — [Linux CUDA Downloads](https://developer.nvidia.com/cuda-downloads)
2. **NVIDIA cuDNN 8.9.x** (`libcudnn.so.8`) — [Linux cuDNN 8.x Downloads](https://developer.nvidia.com/cudnn-archive)

Ensure the following shared libraries are accessible in `LD_LIBRARY_PATH` or standard library paths (`/usr/local/cuda/lib64`):
- `libcudart.so.12`
- `libcublas.so.12`
- `libcublasLt.so.12`
- `libcudnn.so.8`

### Supported GPU Architectures

The bundled native GPU binaries are pre-compiled with native SASS code and forward-compatible PTX for all modern NVIDIA GPU generations:

| Architecture | Compute Capability | Example GPUs |
| :--- | :--- | :--- |
| **Maxwell** | `sm_53` | GTX 900M, Jetson Nano / TX1 |
| **Pascal** | `sm_60`, `sm_61` | GTX 1060 / 1070 / 1080 / 1080 Ti, Tesla P40 / P100 |
| **Volta** | `sm_70` | Titan V, Tesla V100 |
| **Turing** | `sm_75` | RTX 2060 / 2070 / 2080, GTX 1660, Tesla T4 |
| **Ampere** | `sm_80`, `sm_86` | RTX 3060 / 3070 / 3080 / 3090, A100, A10 |
| **Ada Lovelace** | `sm_89` | RTX 4060 / 4070 / 4080 / 4090, RTX 5000 Ada, L4, L40 |
| **Hopper** | `sm_90` | H100, H800 |
| **Blackwell (RTX 50-Series)** | `sm_100`, `sm_120` | RTX 5070 / 5080 / 5090, B100, B200 |
| **Future NVIDIA GPUs** | `PTX (compute_90/100/120)` | Automatic JIT compilation by the NVIDIA driver via embedded PTX |

---

## Docker Compilation for Linux GPU Binaries

For Linux and WSL2 environments, native CUDA binaries can be compiled cleanly using an isolated Docker container without altering host build tools:

```bash
docker run --rm --gpus all -v "$(pwd)":/workspace -w /workspace nvcr.io/nvidia/cuda:12.8.0-devel-ubuntu22.04 bash -c "
  apt-get update && \
  apt-get install -y ca-certificates gpg wget && \
  wget -O - https://apt.kitware.com/keys/kitware-archive-latest.asc 2>/dev/null | gpg --dearmor - | tee /usr/share/keyrings/kitware-archive-keyring.gpg >/dev/null && \
  echo 'deb [signed-by=/usr/share/keyrings/kitware-archive-keyring.gpg] https://apt.kitware.com/ubuntu/ jammy main' | tee /etc/apt/sources.list.d/kitware.list >/dev/null && \
  apt-get update && \
  apt-get install -y cmake build-essential libopenblas-dev ninja-build libcudnn8-dev git && \
  ./build.sh --gpu-only
"
```

This compiles the native wrapper and automatically stages `qourex_fasterwhisper_native.so` and `libctranslate2.so` under `src/Qourex.FasterWhisper.NET.Gpu/runtimes/linux-x64/native/`.

---

## Quick Start: GPU Transcription

```csharp
using System;
using System.Threading.Tasks;
using Qourex.FasterWhisper.NET;

// 1. Download and load the model on CUDA
using var model = await WhisperModel.LoadAsync(
    modelNameOrPath: "large-v3",
    device:          "cuda",       // Target GPU
    computeType:     "float16",    // Half-precision for optimal GPU performance
    flashAttention:  true          // Flash Attention (requires compute capability >= 8.0)
);

// 2. Configure transcription parameters
var options = new WhisperOptions
{
    BeamSize       = 5,
    WordTimestamps = true
};

// 3. Execute transcription
var segments = model.Transcribe(
    mediaPath: "audio.wav",
    language:  "en",
    options:   options
);

// 4. Output results
foreach (var segment in segments)
{
    Console.WriteLine($"[{segment.Start:F2}s -> {segment.End:F2}s] {segment.Text}");
}
```

---

## GPU Configuration Options

### Compute Precision Types

| Compute Type | Description |
| :--- | :--- |
| `"default"` | Selects `float16` if supported by the GPU, with automatic fallback |
| `"float16"` | Recommended. Fast FP16 execution with lowest VRAM footprint |
| `"float32"` | Standard 32-bit single-precision floating point |
| `"int8_float16"` | INT8 quantized compute with FP16 activation storage |

### Flash Attention

Enable Flash Attention for Ampere and newer architectures:
```csharp
flashAttention: true
```
*Note: Flash Attention requires an NVIDIA GPU with compute capability ≥ 8.0 (RTX 30-series, RTX 40-series, RTX 50-series, A100, H100).*

---

## License

This package is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

```
MIT License · Copyright (c) 2026 Qourex
```
