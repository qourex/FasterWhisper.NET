# FasterWhisper.NET Sample Applications

This directory contains a suite of sample applications demonstrating the integration of **FasterWhisper.NET** across multiple application frameworks and hosting patterns. All projects target **.NET 10.0** and reference official NuGet packages.

Separate projects are provided for standard CPU execution (`FasterWhisper.NET`) and GPU-accelerated execution (`FasterWhisper.NET.Gpu`).

---

## Project Catalog

The following projects are included in the solution file `Qourex.FasterWhisper.slnx`:

### 1. Console Applications
Lightweight Command Line Interface (CLI) tools showcasing automated model downloading, progress reporting, parameter configuration, and audio transcription.
- **CPU Project**: `Qourex.FasterWhisper.NET.Samples.Console.Cpu`
  - *Package Reference*: `FasterWhisper.NET` (v1.0.2)
- **GPU Project**: `Qourex.FasterWhisper.NET.Samples.Console.Gpu`
  - *Package Reference*: `FasterWhisper.NET.Gpu` (v1.0.2)
  - *Configuration*: `device: "cuda"`, `computeType: "default"`

### 2. ASP.NET Core Minimal APIs
Production-grade web service integrations exposing HTTP REST endpoints for transcription. Demonstrates thread-safe request handling and thread pool delegation.
- **CPU Project**: `Qourex.FasterWhisper.NET.Samples.AspNetCore.Cpu`
- **GPU Project**: `Qourex.FasterWhisper.NET.Samples.AspNetCore.Gpu`
- **Endpoints**:
  - `POST /api/transcribe` — Accepts multipart WAV audio file uploads and returns structured JSON transcription segments (start, end, text).

### 3. Blazor Web Apps (Server)
Interactive server-side web applications illustrating real-time downloading status feedback and graphical segment timeline visualization.
- **CPU Project**: `Qourex.FasterWhisper.NET.Samples.Blazor.Cpu`
- **GPU Project**: `Qourex.FasterWhisper.NET.Samples.Blazor.Gpu`
- **Architecture**: Employs `InteractiveServer` render mode and SignalR communication to synchronize downloading and transcription states between client browsers and back-end Whisper models seamlessly.

### 4. Windows Forms Applications
Desktop applications demonstrating classic Windows Form architectures updated with native **.NET 10.0 Dark Mode** support (`Application.SetColorMode(SystemColorMode.Dark)`).
- **CPU Project**: `Qourex.FasterWhisper.NET.Samples.WinForms.Cpu`
- **GPU Project**: `Qourex.FasterWhisper.NET.Samples.WinForms.Gpu`
- **Architecture**: Offloads heavy model loading and transcription pipelines to background worker threads via `Task.Run` and marshals progress updates to the UI thread using `Progress<T>` to maintain UI responsiveness.

### 5. .NET MAUI Applications
Cross-platform mobile and desktop application templates demonstrating raw resource bundling and cross-platform native file picking.
- **CPU Project**: `Qourex.FasterWhisper.NET.Samples.Maui.Cpu`
  - *Target Platforms*: Windows (WinUI 3), macOS (Mac Catalyst), iOS, Android.
- **GPU Project**: `Qourex.FasterWhisper.NET.Samples.Maui.Gpu`
  - *Target Platforms*: Windows (WinUI 3) only (CUDA dependency).
- **Asset Handling**: Packs test audio files directly into packaged `Resources/Raw` assets. At runtime, the application extracts these files to local cache directories to guarantee unpackaged file accessibility under WinUI 3 and mobile environments.

---

## Building and Running the Samples

### Prerequisites
- **.NET 10.0 SDK** (or later).
- For GPU-accelerated projects:
  - NVIDIA GPU with CUDA support.
  - **CUDA Toolkit 12.x** and **cuDNN 8.9.x** (`cudnn64_8.dll`) runtime dynamic libraries in the system `PATH`.

### 1. Build the Entire Solution
To build all projects in Release configuration:
```powershell
dotnet build Qourex.FasterWhisper.slnx -c Release
```

### 2. Run a Sample Project
Execute any sample directly using `dotnet run`:

- **Run Console CPU App**:
  ```powershell
  dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Console.Cpu/Qourex.FasterWhisper.NET.Samples.Console.Cpu.csproj -c Release
  ```

- **Run Blazor GPU App**:
  ```powershell
  dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu.csproj -c Release
  ```

- **Run WinForms CPU App**:
  ```powershell
  dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu.csproj -c Release
  ```

---

## Authors and Copyright

- **Author**: Qourex
- **Copyright**: Copyright (c) 2026 Qourex
- All sample projects are licensed under the MIT License.
