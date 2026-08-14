# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# See LICENSE file in the project root for full license information.

<#
.SYNOPSIS
    Generates SHA-256 checksums for native binaries and NuGet release packages.
.DESCRIPTION
    Scans runtime directories and artifacts to produce a standardized SHA256SUMS.txt manifest.
.PARAMETER OutputPath
    Target path for the generated checksum file. Default: SHA256SUMS.txt
#>

[CmdletBinding()]
param (
    [string]$OutputPath = "SHA256SUMS.txt"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Generating SHA-256 checksums for FasterWhisper.NET artifacts..." -ForegroundColor Cyan

$targets = @()

# 1. Native runtime binaries
$nativeExtensions = @("*.dll", "*.so", "*.dylib")
$runtimeDirs = @(
    (Join-Path $repoRoot "src/Qourex.FasterWhisper.NET/runtimes"),
    (Join-Path $repoRoot "src/Qourex.FasterWhisper.NET.Gpu/runtimes")
)

foreach ($dir in $runtimeDirs) {
    if (Test-Path $dir) {
        foreach ($ext in $nativeExtensions) {
            $files = Get-ChildItem -Path $dir -Filter $ext -Recurse -File
            $targets += $files
        }
    }
}

# 2. Packaged NuGet and Symbol artifacts
$artifactsDir = Join-Path $repoRoot "artifacts"
if (Test-Path $artifactsDir) {
    $packages = Get-ChildItem -Path $artifactsDir -Include @("*.nupkg", "*.snupkg") -Recurse -File
    $targets += $packages
}

if ($targets.Count -eq 0) {
    Write-Warning "No target binaries or packages found for checksum generation."
    exit 0
}

$checksumEntries = [System.Collections.Generic.List[string]]::new()

foreach ($file in $targets) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relPath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
    $entry = "$hash  $relPath"
    $checksumEntries.Add($entry)
    Write-Host "  $entry" -ForegroundColor Gray
}

$destinationFile = Join-Path $repoRoot $OutputPath
$checksumEntries | Out-File -FilePath $destinationFile -Encoding utf8

Write-Host "`nSuccessfully wrote $($checksumEntries.Count) checksums to $destinationFile" -ForegroundColor Green
