# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# See LICENSE file in the project root for full license information.

<#
.SYNOPSIS
    Executes pre-release GPU validation for FasterWhisper.NET.Gpu on NVIDIA hardware.
.DESCRIPTION
    Validates CUDA driver detection, native GPU library loading, model loading, and speech transcription.
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Model = "tiny",
    [string]$Precision = "float16"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " FasterWhisper.NET GPU Hardware Pre-Release Validation" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Check NVIDIA Driver & GPU hardware presence
Write-Host "`n[1/4] Checking NVIDIA Driver & Hardware..." -ForegroundColor Yellow
$nvidiaSmi = Get-Command "nvidia-smi" -ErrorAction SilentlyContinue
if (-not $nvidiaSmi) {
    Write-Error "nvidia-smi not found in PATH. An NVIDIA GPU with installed drivers is required."
    exit 1
}

$gpuInfo = & nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader
Write-Host "Detected GPU: $gpuInfo" -ForegroundColor Green

# 2. Verify GPU native runtime binaries
Write-Host "`n[2/4] Verifying GPU Runtime Binaries in src/Qourex.FasterWhisper.NET.Gpu/runtimes..." -ForegroundColor Yellow
$gpuNativeDir = Join-Path $repoRoot "src/Qourex.FasterWhisper.NET.Gpu/runtimes/win-x64/native"
$requiredDlls = @("ctranslate2.dll", "qourex_fasterwhisper_native.dll")

foreach ($dll in $requiredDlls) {
    $dllPath = Join-Path $gpuNativeDir $dll
    if (Test-Path $dllPath) {
        $sizeMB = [math]::Round((Get-Item $dllPath).Length / 1MB, 2)
        Write-Host "  Found $dll ($sizeMB MB)" -ForegroundColor Gray
    } else {
        Write-Error "Required native GPU binary missing: $dllPath"
        exit 1
    }
}

# 3. Build test suite with GPU targets enabled
Write-Host "`n[3/4] Building test suite with GPU project references (-p:UseGpuTest=true)..." -ForegroundColor Yellow
$testProject = Join-Path $repoRoot "tests/Qourex.FasterWhisper.NET.Tests/Qourex.FasterWhisper.NET.Tests.csproj"
& dotnet build $testProject -c $Configuration -p:UseGpuTest=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile GPU test suite."
    exit $LASTEXITCODE
}

# 4. Execute GPU Integration & Native Tests
Write-Host "`n[4/4] Executing CUDA & cuDNN Integration Tests (Model: $Model, Precision: $Precision)..." -ForegroundColor Yellow
$env:FASTER_WHISPER_DEVICE = "cuda"
$env:FASTER_WHISPER_COMPUTE_TYPE = $Precision

& dotnet test $testProject -c $Configuration --no-build -p:UseGpuTest=true --filter "Category=Integration" --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) {
    Write-Error "GPU Integration tests failed."
    exit $LASTEXITCODE
}

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host " ALL GPU VALIDATION CHECKS PASSED SUCCESSFULLY" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
