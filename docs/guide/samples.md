# The .NET 10.0 Sample Suite

To help you get started quickly with production-grade architectures, the FasterWhisper.NET repository includes a comprehensive set of **10 sample projects** built on **.NET 10.0**. 

Each application type features separate **CPU** and **GPU** projects to demonstrate targeting differences.

---

## 💻 1. Console Applications
- **Projects**: 
  - [Console.Cpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Console.Cpu)
  - [Console.Gpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Console.Gpu)
- **Core Pattern**: A lightweight command-line tool showing basic usage. It utilizes the `ModelDownloader` API with asynchronous callbacks to print model download progress, instantiates the model runner, loads audio, and prints timestamped transcription segments to the console.

---

## 🌐 2. ASP.NET Core Minimal APIs
- **Projects**:
  - [AspNetCore.Cpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.AspNetCore.Cpu)
  - [AspNetCore.Gpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.AspNetCore.Gpu)
- **Core Pattern**: Exposes a `POST /api/transcribe` endpoint that handles multipart audio uploads.
- **Production Architecture**:
  - **Thread-Safety & Concurrency**: The underlying native C++ engine is not thread-safe for simultaneous re-entrant inference calls. The samples wrap model execution inside a registered `WhisperService` **Singleton** lifecycle.
  - **Thread Pool Delegation**: Audio loading (`AudioProcessor.LoadWav`) and native inference (`WhisperModel.Transcribe`) are CPU-bound and synchronous. To prevent blocking the main web server threads, these methods are executed on the Thread Pool via `Task.Run` inside the async flow:
    ```csharp
    float[] pcm = await Task.Run(() => audioProcessor.LoadWav(wavFilePath));
    var rawSegments = await Task.Run(() => _model.Transcribe(pcm, options: options));
    ```

---

## 🎨 3. Blazor Web Apps
- **Projects**:
  - [Blazor.Cpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Blazor.Cpu)
  - [Blazor.Gpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu)
- **Core Pattern**: A gorgeous, modern dark-mode Blazor Server application.
- **Key Features**:
  - **Interactive Rendering**: Implemented using `@rendermode InteractiveServer` to handle real-time UI state binding.
  - **Live Feedback**: Employs SignalR events to feed model downloading status (percentage complete) and transcription progress directly into the glassmorphic UI.
  - **Interactive Timeline**: Transcripts are presented as an interactive segment timeline that users can scroll and hover over for detailed audio diagnostics.

---

## 🖥️ 4. Windows Forms Desktop Apps
- **Projects**:
  - [WinForms.Cpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu)
  - [WinForms.Gpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.WinForms.Gpu)
- **Core Pattern**: Classic desktop applications updated for modern Windows deployment.
- **Key Features**:
  - **Native Dark Mode**: Uses the official .NET 10.0 WinForms dark mode styling:
    ```csharp
    Application.SetColorMode(SystemColorMode.Dark);
    ```
  - **UI Thread Responsiveness**: CPU-intensive operations (model initialization, transcription) are offloaded from the UI message loop.
  - **Progress Routing**: Employs the `IProgress<T>` pattern to marshal progress events safely back to the UI thread for display updates without throwing cross-thread exceptions.

---

## 📱 5. .NET MAUI Cross-Platform Apps
- **Projects**:
  - [Maui.Cpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Maui.Cpu)
  - [Maui.Gpu](https://github.com/qourex/FasterWhisper.NET/tree/main/samples/Qourex.FasterWhisper.NET.Samples.Maui.Gpu)
- **Core Pattern**: Cross-platform application targeting Android, iOS, macOS, and Windows.
- **Platform targeting differences**:
  - **Cpu Version**: Targets Windows, iOS, Mac Catalyst, and Android.
  - **Gpu Version**: Targets Windows x64 only, since CUDA is only available on desktop configurations. Non-supported environments automatically fall back to warning notifications.
- **Mobile Caching Workaround**: Mobile platforms encapsulate app resources inside zipped asset bundles. Because the native C++ engine requires direct file path handles, raw model resources (such as VAD models) are extracted out of the zipped bundle and written to `FileSystem.CacheDirectory` during application startup.

---

## ⚙️ How to Build and Run

To compile the entire sample suite from the command line, run the following commands:

### Restore and Build
```powershell
# Restore all NuGet dependencies
dotnet restore Qourex.FasterWhisper.slnx

# Build the entire solution in Release configuration
dotnet build Qourex.FasterWhisper.slnx -c Release --no-restore
```

### Running a CPU Console Sample
```powershell
cd samples/Qourex.FasterWhisper.NET.Samples.Console.Cpu
dotnet run -c Release
```

### Running a GPU Console Sample
*(Ensure CUDA 12.x and cuDNN 9.x are installed on your host)*
```powershell
cd samples/Qourex.FasterWhisper.NET.Samples.Console.Gpu
dotnet run -c Release
```
