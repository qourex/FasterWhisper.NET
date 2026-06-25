# FasterWhisper.NET Samples Directory

This directory contains a suite of professional sample applications designed to demonstrate the integration and execution of **FasterWhisper.NET** within various application patterns and framework environments. All projects target **.NET 10.0** and reference the official packages directly from NuGet.org at version `1.0.2`.

Separate projects are provided for CPU-based execution (`FasterWhisper.NET`) and GPU/CUDA-accelerated execution (`FasterWhisper.NET.Gpu`).

---

## 📂 Project Catalog

The following projects are registered in the solution file `Qourex.FasterWhisper.slnx`:

### 1. Console Applications
Minimal Command Line Interface (CLI) showcases demonstrating automatic model resolution, downloading progress tracking, and single-audio-file transcription.
*   **CPU Version**: `Qourex.FasterWhisper.NET.Samples.Console.Cpu`
    *   *Dependencies*: `FasterWhisper.NET` (v1.0.2)
*   **GPU Version**: `Qourex.FasterWhisper.NET.Samples.Console.Gpu`
    *   *Dependencies*: `FasterWhisper.NET.Gpu` (v1.0.2)
    *   *Configuration*: Device: `"cuda"`, Compute Type: `"default"` (provides automated float16/float32 fallbacks based on GPU capabilities).

### 2. ASP.NET Core Minimal APIs
Web service integrations exposing HTTP REST endpoints for transcription. Includes thread-safe request serialization using a singleton service container to prevent native engine reentrancy conflicts.
*   **CPU Version**: `Qourex.FasterWhisper.NET.Samples.AspNetCore.Cpu`
*   **GPU Version**: `Qourex.FasterWhisper.NET.Samples.AspNetCore.Gpu`
*   **Endpoints**:
    *   `POST /api/transcribe` — Accepts multipart WAV audio file uploads and returns segmented transcription JSON metadata (start, end, text).

### 3. Blazor Web Apps (Server)
Premium, dark-themed interactive server-side web applications illustrating real-time downloading status feedback and graphical segment timeline visualization.
*   **CPU Version**: `Qourex.FasterWhisper.NET.Samples.Blazor.Cpu`
*   **GPU Version**: `Qourex.FasterWhisper.NET.Samples.Blazor.Gpu`
*   **Key Features**: Outfit typography, glassmorphic layout, live model downloading progress bars, and reactive audio playbacks.
*   **Interactivity**: Utilizes `InteractiveServer` render mode to coordinate signal status between client browsers and back-end Whisper models seamlessly.

### 4. Windows Forms Applications
Desktop applications highlighting classic Windows Form layouts upgraded to target the native **.NET 10.0 Dark Mode** support (`Application.SetColorMode(SystemColorMode.Dark)`).
*   **CPU Version**: `Qourex.FasterWhisper.NET.Samples.WinForms.Cpu`
*   **GPU Version**: `Qourex.FasterWhisper.NET.Samples.WinForms.Gpu`
*   **Architecture**: Offloads heavy model loading and transcription pipelines to background worker threads using `Task.Run` and marshals progress updates to the UI thread via `Progress<T>` to maintain system responsiveness.

### 5. .NET MAUI Applications
Cross-platform mobile and desktop application templates demonstrating raw resource bundling and cross-platform native file picking.
*   **CPU Version**: `Qourex.FasterWhisper.NET.Samples.Maui.Cpu`
    *   *Targets*: Windows (WinUI 3), macOS (Mac Catalyst), iOS, Android.
*   **GPU Version**: `Qourex.FasterWhisper.NET.Samples.Maui.Gpu`
    *   *Targets*: Windows (WinUI 3) only (CUDA dependency).
*   **Assets**: Packs `harvard.wav` and `harvard2.wav` directly into packaged `Resources/Raw` assets. At runtime, the application extracts these files to local cache directories to guarantee unpackaged file accessibility under WinUI 3 environments.
*   **File Selection**: Includes cross-platform `FilePickerFileType` filters specifically configured to allow `.wav` selection on Windows, iOS, Android, and macOS.

---

## 🚀 How to Build and Run

### Prerequisites
*   **.NET 10.0 SDK** (installed on host machine).
*   For GPU-accelerated samples:
    *   NVIDIA GPU with CUDA support.
    *   **CUDA Toolkit 12.x** and **cuDNN 9.x** libraries available in the system PATH.

### 1. Compile the Entire Solution
To build all sample projects in Release configuration, execute the following command from the repository root:
```powershell
dotnet build Qourex.FasterWhisper.slnx -c Release
```

### 2. Run a Specific Project
You can run any sample project using the `dotnet run` command.

*   **Run Console CPU App**:
    ```powershell
    dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Console.Cpu/Qourex.FasterWhisper.NET.Samples.Console.Cpu.csproj -c Release
    ```
*   **Run Blazor GPU App**:
    ```powershell
    dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu.csproj -c Release
    ```
*   **Run WinForms CPU App**:
    ```powershell
    dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu.csproj -c Release
    ```

---

## ⚖️ Authors & Copyright
*   **Author**: Qourex
*   **Copyright**: Copyright (c) 2026 Qourex
*   All sample projects are licensed under the MIT License.
