# Mobile Deployment: Android and iOS

FasterWhisper.NET includes native binaries and deployment targets for running offline speech recognition on mobile devices (Android and iOS). Because processing occurs on-device, transcription operates with zero cloud latency and maintains complete data privacy.

---

## Architectural Overview

To deliver high-performance inference on mobile hardware, FasterWhisper.NET links to platform-native mathematical libraries:

| Platform | Architecture | Mathematical Engine | Native Runtime Libraries |
| :--- | :--- | :--- | :--- |
| **Android** | `arm64-v8a` (64-bit) | Eigen / Ruy | `qourex_fasterwhisper_native.so`, `libctranslate2.so` |
| **iOS** | `arm64` (64-bit) | Apple Accelerate | `qourex_fasterwhisper_native.dylib`, `libctranslate2.dylib` (Framework) |

> [!WARNING]
> Only 64-bit physical devices and 64-bit simulators are supported. 32-bit platforms and legacy x86 emulators are unsupported and will fail during native library loading.

---

## Android Configuration

Configure your .NET Android or .NET MAUI project as follows:

### 1. Workload Installation
Verify that the Android workload is installed in your development environment:
```bash
dotnet workload install android
```

### 2. Permissions Configuration
If your application captures microphone input or accesses external audio storage, declare the appropriate permissions in `AndroidManifest.xml` (located under `Platforms/Android/`):

```xml
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

---

## iOS Configuration

Configure your .NET iOS or .NET MAUI project as follows:

### 1. Workload Installation
Verify that the iOS workload is installed in your development environment:
```bash
dotnet workload install ios
```

### 2. Apple Accelerate Framework
On iOS, FasterWhisper.NET links directly to Apple's **Apple Accelerate** framework. This utilizes Apple Silicon Neural Engine and CPU NEON vector registers, maximizing battery efficiency and throughput.

### 3. Code-Signing Requirements
Physical iOS deployment requires all dynamic libraries to be signed. The FasterWhisper.NET package includes MSBuild targets that automatically embed and sign the native dynamic libraries into the application `.app` bundle.
- Ensure an active Apple Developer Provisioning Profile is selected in your IDE.
- If recording live audio, include `NSMicrophoneUsageDescription` in your `Info.plist`.

---

## Packaged Asset Management and Local Caching

### Packaged Resource Access
On mobile platforms, bundled assets (such as sample WAV files or pre-packaged VAD ONNX models) are stored inside compressed application archive packages (`.apk` / `.ipa`). They do not reside as standalone filesystem paths.

Because native C++ runtimes require direct filesystem paths (`const char*`), assets stored in app packages must be extracted to the device's local application cache directory before initialization.

### Asset Extraction Helper

The following pattern extracts a bundled app asset to the local cache directory:

```csharp
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

public static async Task<string> PrepareTempFileFromAssetAsync(string assetName)
{
    string targetPath = Path.Combine(FileSystem.CacheDirectory, assetName);
    
    // Verify whether extraction is needed
    if (!File.Exists(targetPath))
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var outStream = File.Create(targetPath);
        await stream.CopyToAsync(outStream);
    }
    
    return targetPath;
}
```

---

## Managing Model Assets

Whisper model binaries range from ~75 MB (`tiny`) upwards. Bundling model weights directly into the store distribution bundle can significantly inflate application package size.

### Recommended On-Demand Loading Pattern

1. **Exclude Model Weights from App Store Bundle**: Keep the base application download small.
2. **First-Launch Check**: Verify whether model weights exist in `FileSystem.AppDataDirectory`.
3. **Download with Progress Reporting**: Use `ModelDownloader` to retrieve weights from Hugging Face on demand:

```csharp
using System;
using Qourex.FasterWhisper.NET;
using Microsoft.Maui.ApplicationModel;

var downloader = new ModelDownloader();
var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
{
    if (p.TotalBytes > 0)
    {
        double percentage = (double)p.BytesRead / p.TotalBytes;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            downloadProgressBar.Progress = percentage;
        });
    }
});

// Download model assets directly to device application storage
string modelPath = await downloader.GetModelPathAsync("tiny", progress);
```

---

## Mobile Performance Optimization Guidelines

- **Model Selection**: Deploy the `tiny` or `base` models for mobile inference. These provide real-time factor (RTF) performance below 1.0 while maintaining memory consumption under 300 MB.
- **Quantization**: Use `computeType: "default"` or `"int8"` to reduce runtime memory footprint and maximize SIMD efficiency on ARM64.
- **UI Thread Decoupling**: Always offload model initialization and transcription tasks to worker threads via `Task.Run` to prevent freezing the main UI thread.
