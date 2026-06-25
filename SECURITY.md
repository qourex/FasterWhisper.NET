# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | ✅        |

## Reporting a Vulnerability

If you discover a security vulnerability in Qourex.FasterWhisper.NET, please report it
responsibly:

1. **Do NOT** open a public GitHub issue for security vulnerabilities
2. Email the maintainers at the address listed in the repository profile, or use
   [GitHub Private Vulnerability Reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

## Response Timeline

- **Acknowledgment**: Within 48 hours
- **Initial Assessment**: Within 7 days
- **Fix Release**: Dependent on severity; critical issues prioritized

## Scope

The following are in scope for security reports:

- Vulnerabilities in the C# library code (`src/Qourex.FasterWhisper.NET/`)
- Vulnerabilities in the native C++ wrapper (`src/Qourex.FasterWhisper.Native/`)
- Unsafe deserialization or code execution via model files
- Path traversal in model downloading or file loading
- Network security issues in `ModelDownloader` or `SileroVad` download logic

The following are **out of scope**:

- Vulnerabilities in upstream dependencies (CTranslate2, ONNX Runtime, Silero VAD)
  — report these to the respective upstream projects
- Denial of service via large audio files (expected behavior)
- Issues requiring physical access to the host machine
