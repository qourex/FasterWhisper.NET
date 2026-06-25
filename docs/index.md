---
layout: home

hero:
  name: "FasterWhisper.NET"
  text: "Offline Speech-to-Text for .NET"
  tagline: "A production-ready C# SDK wrapping CTranslate2 for ultra-fast, local Whisper audio transcription."
  image:
    src: https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/src/Qourex.FasterWhisper.NET/icon.png
    alt: FasterWhisper.NET Logo
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/qourex/FasterWhisper.NET

features:
  - icon: ⚡
    title: High-Performance Engine
    details: Powered by CTranslate2, achieving up to 4x speedup over standard Whisper implementations.
  - icon: 📱
    title: Cross-Platform Native
    details: First-class support for Windows, Linux, macOS, Android, and iOS using native RIDs.
  - icon: 📦
    title: .NET 10.0 Ready
    details: Includes 10 CPU/GPU sample projects covering Blazor, WinForms, Minimal APIs, and MAUI.
  - icon: 🗣️
    title: Integrated Silero VAD
    details: Native Silero VAD v5 ONNX integration for precise audio segmentation and noise filtering.
  - icon: 🔄
    title: Real-time Streaming
    details: Asynchronous push pipelines for live streaming transcription with IAsyncEnumerable support.
  - icon: 📊
    title: Enterprise Diagnostics
    details: Built-in audio quality metrics, hallucination filters, and memory-mapped model loading.
---

<style>
:root {
  --vp-home-hero-name-color: transparent;
  --vp-home-hero-name-background: linear-gradient(135deg, #a78bfa 0%, #3b82f6 100%);
}
</style>
