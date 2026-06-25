# Contributing to Qourex.FasterWhisper.NET

Thank you for your interest in contributing to Qourex.FasterWhisper.NET! This document
provides guidelines for contributing to the project.

## How to Contribute

### Reporting Issues

- Use the [GitHub Issues](https://github.com/qourex/fasterwhisper.net/issues)
  tracker
- Check existing issues before creating a new one
- Include a clear title and description
- Provide reproduction steps, expected behavior, and actual behavior
- Include your OS, .NET version, and GPU (if applicable)

### Suggesting Features

- Open a [GitHub Discussion](https://github.com/qourex/fasterwhisper.net/discussions)
  or issue with the `enhancement` label
- Describe the use case and proposed solution
- Consider backward compatibility implications

### Submitting Pull Requests

1. **Fork** the repository and create a branch from `main`
2. **Install prerequisites**:
   - **.NET 8.0+ SDK**
   - **CMake 3.18+**
   - **Visual Studio 2022 / Build Tools (MSVC)** for C++ compilation
   - **Intel oneAPI Math Kernel Library (oneMKL)** — optional but highly recommended for CPU BLAS performance (pass `-DWITH_MKL=ON` in CMake)
   - **Intel oneAPI DPC++/C++ Compiler** — optional (for highly optimized SIMD/AVX code generation)
   - **CUDA Toolkit 12.x & cuDNN 9.x** (optional, required for GPU acceleration builds)
3. **Build the native library**:
   > [!IMPORTANT]
   > When compiling the C++ wrapper manually, you must run all `cmake` and build commands from a **Visual Studio Developer Command Prompt** (or a terminal with `vcvars64.bat` loaded) to ensure MSVC, CMake, and oneAPI compiler environment variables are correctly configured.

   Using the automation script:
   ```powershell
   # Build with CPU optimizations (MKL/OpenMP if installed, or fallback OpenBLAS)
   .\build.ps1 -CpuOnly

   # Build with CUDA + GPU support
   .\build.ps1
   ```
4. **Make your changes** with clear, descriptive commits
5. **Add tests** for new features or bug fixes
6. **Run the test suite**:
   ```powershell
    dotnet test tests\Qourex.FasterWhisper.NET.Tests -c Release
   ```
7. **Ensure the build passes**:
   ```powershell
    dotnet build src\Qourex.FasterWhisper.NET\Qourex.FasterWhisper.NET.csproj -c Release
   ```
8. **Submit a Pull Request** with a clear description of the changes

## Development Guidelines

### Code Style

- Follow standard C# coding conventions
- Use `///` XML documentation comments on all public APIs
- Keep methods focused and under ~50 lines where practical
- Use `Span<T>` and `Memory<T>` for performance-critical paths

### Project Structure

```
src/
├── Qourex.FasterWhisper.Native/ # C++ interop layer (CMake)
└── Qourex.FasterWhisper.NET/    # C# class library
tests/
└── Qourex.FasterWhisper.NET.Tests/ # xUnit test suite
samples/
└── Qourex.FasterWhisper.NET.Samples/ # Console app feature demo
```

### Testing

- **Unit tests**: Fast, no external dependencies (tokenizer, audio processing, etc.)
- **Integration tests**: Require native DLLs and may download models. Marked by
  longer execution times.
- All PRs must pass the existing test suite
- New features should include corresponding tests

### Native (C++) Changes

- Changes to `qourex_fasterwhisper_native.h` / `.cpp` require rebuilding via `build.ps1`
- Follow the existing export pattern: `EXPORT return_type function_name(...)`
- Always provide an error output parameter (`char** error_msg`) for native functions
- Free functions must be paired with allocation functions (e.g., `whisper_align` / `free_alignment_result`)

### Documentation

- Update `README.md` for user-facing changes
- Update `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/)
- Update XML doc comments for any modified public APIs

## Code of Conduct

By participating in this project, you agree to maintain a respectful and
inclusive environment. Please:

- Be welcoming and inclusive
- Be respectful of differing viewpoints and experiences
- Accept constructive criticism gracefully
- Focus on what is best for the community

## License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE).
