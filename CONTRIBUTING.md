# Contributing to FasterWhisper.NET

Thank you for your interest in contributing to FasterWhisper.NET. This document provides technical guidelines and development workflows for contributing to the project.

---

## Contribution Workflow

### Reporting Issues

- Use the [GitHub Issues](https://github.com/qourex/fasterwhisper.net/issues) tracker.
- Search existing open and closed issues before submitting a new issue.
- Provide a clear, descriptive title and detailed reproduction steps.
- Include environment information: Operating System, .NET SDK version, GPU hardware, and driver/CUDA versions if applicable.

### Feature Suggestions

- Open a [GitHub Discussion](https://github.com/qourex/fasterwhisper.net/discussions) or issue with the `enhancement` label.
- Detail the specific enterprise use case and proposed technical design.
- Consider backward compatibility and native interop impacts.

### Submitting Pull Requests

1. **Fork** the repository and create a feature branch from `main`.
2. **Verify Prerequisites**:
   - **.NET 8.0+ SDK**
   - **CMake 3.18+**
   - **Visual Studio 2022 / MSVC Build Tools** for native C++ compilation
   - **Intel oneAPI Math Kernel Library (oneMKL)** (optional for CPU BLAS performance)
   - **CUDA Toolkit 12.x and cuDNN 8.9.x** (optional for GPU builds)
3. **Build the Native Library**:
   > [!IMPORTANT]
   > When compiling the C++ wrapper manually on Windows, run `cmake` and build commands from a **Visual Studio Developer Command Prompt** (or a terminal with `vcvars64.bat` loaded).

   Using the automation script:
   ```powershell
   # Build CPU optimizations
   .\build.ps1 -CpuOnly

   # Build with CUDA and GPU support
   .\build.ps1
   ```
4. **Implement Changes** with concise, descriptive commit messages.
5. **Add Automated Tests** for any new features or bug fixes.
6. **Execute the Test Suite**:
   ```powershell
   dotnet test tests\Qourex.FasterWhisper.NET.Tests -c Release
   ```
7. **Ensure Solution Builds Cleanly**:
   ```powershell
   dotnet build Qourex.FasterWhisper.slnx -c Release
   ```
8. **Submit a Pull Request** linking to relevant issues with an architectural summary.

---

## Development and Engineering Standards

### C# Managed Layer

- Adhere to standard .NET naming and design guidelines.
- Provide XML documentation comments (`///`) on all public types and members.
- Keep methods modular and focused.
- Utilize `Span<T>`, `ReadOnlyMemory<T>`, and `[LibraryImport]` source generation for performance-critical interop routines.

### Native C++ Layer

- Modifications to `qourex_fasterwhisper_native.h` or `.cpp` require rebuilding native binaries via `build.ps1`.
- Maintain the standardized C ABI export pattern: `EXPORT return_type function_name(...)`.
- Always provide an output error pointer (`char** error_msg`) for exception forwarding.
- All heap allocations returned across the interop boundary must have corresponding explicit free functions (e.g., `whisper_align` and `free_alignment_result`).
- Production native code must not contain unmanaged `printf` or debugging output.

### Documentation Requirements

- Update `README.md` and `docs/` for user-facing API changes.
- Update `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/).
- Keep documentation completely free of emojis, maintaining an enterprise technical standard.

---

## Code of Conduct

All contributors are expected to uphold a welcoming, professional, and respectful environment in accordance with our [Code of Conduct](CODE_OF_CONDUCT.md).

---

## License

By contributing to FasterWhisper.NET, you agree that your contributions will be licensed under the [MIT License](LICENSE).
