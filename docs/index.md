---
layout: home

hero:
  name: "FasterWhisper.NET"
  text: "High-Performance Speech Recognition for .NET"
  tagline: "Enterprise-grade C# SDK powered by CTranslate2 for local, offline Whisper inference across desktop, server, and mobile."
  image:
    src: https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/src/Qourex.FasterWhisper.NET/icon.png
    alt: FasterWhisper.NET Logo
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: API Reference
      link: /guide/api-reference
    - theme: alt
      text: GitHub Repository
      link: https://github.com/qourex/FasterWhisper.NET

features:
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z"/></svg>'
    title: CTranslate2 Inference Engine
    details: Leverages native CTranslate2 acceleration with INT8, FP16, and INT16 quantization, achieving up to 4x throughput improvements over standard implementations.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="14" x="2" y="3" rx="2"/><line x1="8" x2="16" y1="21" y2="21"/><line x1="12" x2="12" y1="17" y2="21"/></svg>'
    title: Cross-Platform Native Runtimes
    details: Pre-compiled, self-contained native runtimes for Windows (x64), Linux (x64), macOS (x64, ARM64), Android (ARM64), and iOS (ARM64).
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m7.5 4.27 9 5.15"/><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/></svg>'
    title: Multi-Target .NET Support
    details: Built for modern .NET 8.0, 9.0, and 10.0, including comprehensive sample solutions for ASP.NET Core, Blazor, WinForms, and .NET MAUI.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" x2="12" y1="19" y2="22"/></svg>'
    title: Integrated Silero VAD v5
    details: Built-in Voice Activity Detection via ONNX Runtime to partition audio streams, eliminate silence hallucinations, and optimize compute cycles.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>'
    title: Real-Time Streaming Pipelines
    details: Asynchronous push pipelines yielding live segment updates via IAsyncEnumerable with low latency for interactive microphone and feed processing.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"/><path d="m19 9-5 5-4-4-3 3"/></svg>'
    title: Enterprise Diagnostics & Telemetry
    details: Non-intrusive audio quality grading, signal-to-noise ratio estimation, repetition detection, and memory-mapped model weight initialization.
---
