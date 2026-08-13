# Security Policy

## Supported Versions

| Version | Supported |
| :--- | :--- |
| **1.0.x** | Supported |

---

## Reporting a Vulnerability

If you discover a security vulnerability in FasterWhisper.NET, please report it responsibly:

1. **Do not** open a public GitHub issue for security vulnerabilities.
2. Email the maintainers at info@qourex.com or use [GitHub Private Vulnerability Reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability).
3. Include the following details in your report:
   - Description of the vulnerability and attack vector
   - Reproduction steps or proof of concept
   - Potential impact on host systems
   - Suggested remediation or patch (if available)

---

## Response Timeline

- **Initial Acknowledgment**: Within 48 hours
- **Triage and Assessment**: Within 7 business days
- **Remediation Release**: Dependent on severity; critical issues are prioritized

---

## Security Scope

The following areas are in scope for security reports:

- Vulnerabilities in the managed C# library code (`src/Qourex.FasterWhisper.NET/`)
- Vulnerabilities in the native C++ wrapper (`src/Qourex.FasterWhisper.Native/`)
- Unsafe deserialization or arbitrary code execution via crafted model files
- Path traversal vulnerabilities in model downloading or file ingestion
- Network security and hash verification defects in `ModelDownloader` or `SileroVad`

The following areas are **out of scope**:

- Vulnerabilities in upstream dependencies (CTranslate2, ONNX Runtime, Silero VAD) — report these directly to upstream maintainers
- Denial of service caused by processing arbitrarily large audio files (expected behavior)
- Attacks requiring physical access to the host machine
