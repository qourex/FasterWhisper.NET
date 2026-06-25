# Mobile Deployment (Android & iOS)

FasterWhisper.NET features first-class native support for running speech-to-text directly on mobile devices (Android and iOS). Because it bypasses external API endpoints, transcription is fast, secure, and functions completely offline.

---

## 📋 Architectural Overview

To run high-performance AI models on mobile devices, FasterWhisper.NET uses native compilation techniques:

| Platform | Architecture | Mathematical Backend | Native Library Target |
| :--- | :--- | :--- | :--- |
| **Android** | `arm64-v8a` (64-bit) | Eigen / Ruy | `qourex_fasterwhisper_native.so`, `libctranslate2.so` |
| **iOS** | `arm64` (64-bit) | Apple Accelerate | `qourex_fasterwhisper_native.dylib`, `libctranslate2.dylib` (Embedded Framework) |

> [!WARNING]
> Only 64-bit mobile devices and simulators are supported. Attempting to build or run on 32-bit simulators or legacy devices will result in a `DllNotFoundException` or runtime execution failures.

---

## 🤖 Android Integration

To configure your Android application for FasterWhisper.NET:

### 1. Workload Installation
Ensure that the Android development workload is installed:
```bash
dotnet workload install android
```

### 2. Permissions Configuration
If you plan to pick or record audio files from external storage, you must request permissions in your `AndroidManifest.xml` (located under `Platforms/Android/`):
```xml
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />
<!-- If you plan to record live audio using a microphone -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

---

## 🍎 iOS Integration

To configure your iOS application for FasterWhisper.NET:

### 1. Workload Installation
Ensure that the iOS development workload is installed:
```bash
dotnet workload install ios
```

### 2. Battery & Math Backend
FasterWhisper.NET compiles against Apple's native **Apple Accelerate** framework on iOS. This offers hardware-level optimization for the Apple Neural Engine and CPU vector extensions (NEON), resulting in extremely low battery consumption and fast transcription speeds.

### 3. Code-Signing Requirements
Physical iOS devices require all binaries and native `.dylib` libraries to be code-signed. The FasterWhisper.NET NuGet package includes MSBuild targets that automatically extract, sign, and embed the CTranslate2 and native interop dynamic libraries into your `.app` bundle.
- Ensure you have a valid Apple Developer Profile configured in Visual Studio or Rider.
- For physical device debugging, configure your `Entitlements.plist` and provisioning profiles as you would for a standard iOS app.

---

## 💾 Resource Management & The Mobile Caching Workaround

### The Problem
On Android and iOS, raw files (such as pre-packaged audio clips or VAD ONNX models) embedded in the application package are zipped inside the `.apk` or `.ipa` bundle. They do **not** exist as separate, physical files on the disk.
Because the native C++ Whisper engine requires direct filesystem path strings (`char*`) to load assets, it cannot read files directly from inside the compiled bundle.

### The Solution: Extracting to Cache
On application startup, you must extract your packaged assets out of the application package and write them to the local device cache directory (`FileSystem.CacheDirectory` or `FileSystem.AppDataDirectory`). Once extracted, you can pass the physical path of the cached file to the SDK.

Here is the exact helper method used in our .NET MAUI sample:

```csharp
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

public async Task<string> PrepareTempFileFromAssetAsync(string assetName)
{
    // Define the destination target in the local CacheDirectory
    string targetPath = Path.Combine(FileSystem.CacheDirectory, assetName);
    
    // Check if we need to extract it (or optionally overwrite it)
    if (File.Exists(targetPath))
    {
        File.Delete(targetPath);
    }

    // Open the read stream from the packaged app bundle
    using var stream = await FileSystem.OpenAppPackageFileAsync(assetName);
    
    // Write it to a physical file on disk
    using var outStream = File.OpenWrite(targetPath);
    await stream.CopyToAsync(outStream);
    
    // Return the physical file path that native interop can read
    return targetPath;
}
```

---

## 📦 Managing Large Model Assets

Whisper model files are large (even the `tiny` model is approximately ~75MB). Packaging models directly inside the mobile app bundle will dramatically bloat your store download package.

### Recommended Flow
1. **Dynamic Download**: Exclude model files from your app bundle.
2. **On-Demand Loading**: When the app starts (or when the user first triggers transcription), check if the model folder exists in `FileSystem.AppDataDirectory`.
3. **ModelDownloader**: If it does not exist, use the `ModelDownloader` class to download it dynamically from Hugging Face:

```csharp
using Qourex.FasterWhisper.NET;
using Microsoft.Maui.Storage;

var downloader = new ModelDownloader();
var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
{
    if (p.TotalBytes > 0)
    {
        double percentage = (double)p.BytesRead / p.TotalBytes;
        // Update your UI ProgressBar on the main thread
        MainThread.BeginInvokeOnMainThread(() => {
            MyProgressBar.Progress = percentage;
        });
    }
});

// Download model files directly to the mobile app's local storage path
string modelPath = await downloader.GetModelPathAsync("tiny", progress);
```

---

## ⚡ Performance Optimization Tips

- **Use the `tiny` or `base` Models**: These models are highly optimized for CPU-only inference on mobile, offering a runtime speed that is faster than real-time (RTF < 1.0) while keeping memory usage under 300MB.
- **Compute Type**: Leave compute type set to `default` or use `int8` (8-bit quantization) on Android/iOS to reduce memory footprint and speed up operations.
- **Avoid UI Thread Blocking**: Always run Whisper initialization (`builder.Build()`) and transcription (`model.Transcribe()`) on a background thread using `Task.Run` to prevent freezing the mobile UI.
