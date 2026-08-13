# Sample Applications Catalog

The FasterWhisper.NET repository includes a comprehensive suite of **10 sample applications** targeting **.NET 10.0**. These samples illustrate production-ready integration patterns across console, web, desktop, and mobile frameworks.

Separate projects are provided for standard CPU execution and GPU-accelerated execution.

---

## 1. Console Applications

- **Projects**: 
  - `Qourex.FasterWhisper.NET.Samples.Console.Cpu`
  - `Qourex.FasterWhisper.NET.Samples.Console.Gpu`
- **Architecture**: Lightweight CLI demonstrating automated model resolution, Hugging Face downloading with progress reporting, audio file ingestion, and segment printing with precise timestamps.

---

## 2. ASP.NET Core Minimal APIs

- **Projects**:
  - `Qourex.FasterWhisper.NET.Samples.AspNetCore.Cpu`
  - `Qourex.FasterWhisper.NET.Samples.AspNetCore.Gpu`
- **Endpoint**: `POST /api/transcribe` (accepts multipart WAV form uploads and returns structured JSON transcription segments).
- **Architectural Patterns**:
  - **Singleton Service Registration**: Heavy model instances and replica pools are registered as a **Singleton** service in the DI container, ensuring model weights are loaded only once during application startup.
  - **Thread Pool Offloading**: Audio decoding and CTranslate2 native inference are CPU/GPU-bound synchronous operations. The controller offloads execution to the .NET Thread Pool via `Task.Run` to prevent thread pool starvation:
    ```csharp
    float[] pcm = await Task.Run(() => audioProcessor.LoadWav(wavFilePath));
    var rawSegments = await Task.Run(() => _model.Transcribe(pcm, options: options));
    ```

---

## 3. Blazor Web Apps (Server)

- **Projects**:
  - `Qourex.FasterWhisper.NET.Samples.Blazor.Cpu`
  - `Qourex.FasterWhisper.NET.Samples.Blazor.Gpu`
- **Architecture**: Interactive Blazor Server web application featuring real-time model downloading status, audio playback coordination, and interactive timeline rendering.
- **Key Features**:
  - **InteractiveServer Rendering**: Utilizes SignalR circuits for immediate server-to-client UI synchronization.
  - **Segment Timeline**: Renders transcription output as an interactive visual timeline with per-segment audio positioning.

---

## 4. Windows Forms Applications

- **Projects**:
  - `Qourex.FasterWhisper.NET.Samples.WinForms.Cpu`
  - `Qourex.FasterWhisper.NET.Samples.WinForms.Gpu`
- **Architecture**: Desktop UI demonstrating native **.NET 10.0 Dark Mode** support (`Application.SetColorMode(SystemColorMode.Dark)`).
- **Key Features**:
  - **Non-Blocking Background Workers**: Offloads heavy model initialization and audio inference to background tasks.
  - **Safe UI Marshaling**: Uses `IProgress<T>` to marshal downloading and transcription status safely back to the UI thread.

---

## 5. .NET MAUI Cross-Platform Apps

- **Projects**:
  - `Qourex.FasterWhisper.NET.Samples.Maui.Cpu` (Targets Windows, macOS, iOS, Android)
  - `Qourex.FasterWhisper.NET.Samples.Maui.Gpu` (Targets Windows x64 with CUDA)
- **Key Features**:
  - **Resource Extraction**: Automatically extracts bundled test audio files from packaged app assets to `FileSystem.CacheDirectory` to provide standard filesystem paths for native interop.
  - **Cross-Platform File Picker**: Includes platform-specific MIME type and UTI filters for `.wav` file selection across mobile and desktop platforms.

---

## Building and Running the Samples

### Build Entire Solution
From the repository root:
```powershell
dotnet build Qourex.FasterWhisper.slnx -c Release
```

### Run a Specific Project
```powershell
# Run Console CPU Sample
dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Console.Cpu/Qourex.FasterWhisper.NET.Samples.Console.Cpu.csproj -c Release

# Run Blazor GPU Sample
dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu/Qourex.FasterWhisper.NET.Samples.Blazor.Gpu.csproj -c Release

# Run WinForms CPU Sample
dotnet run --project samples/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu/Qourex.FasterWhisper.NET.Samples.WinForms.Cpu.csproj -c Release
```
