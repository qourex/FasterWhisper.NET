param (
    [string]$NupkgPath
)

if (-not (Test-Path $NupkgPath)) {
    Write-Error "File not found: $NupkgPath"
    exit 1
}

$absolutePath = [System.IO.Path]::GetFullPath($NupkgPath)
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

Write-Host "Extracting $absolutePath to $tempDir"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($absolutePath, $tempDir)

# 1. Rename directories under lib/
$libPath = Join-Path $tempDir "lib"
if (Test-Path $libPath) {
    $dirs = Get-ChildItem -Path $libPath -Directory
    foreach ($dir in $dirs) {
        $newName = [System.Text.RegularExpressions.Regex]::Replace($dir.Name, '(?<=android|ios|maccatalyst|macos|windows)\d+(\.\d+)?', '')
        if ($dir.Name -ne $newName) {
            $destPath = Join-Path $libPath $newName
            Write-Host "Renaming directory $($dir.FullName) to $destPath"
            Move-Item -Path $dir.FullName -Destination $destPath -Force
        }
    }
}

# 2. Update .nuspec file
$nuspecFiles = Get-ChildItem -Path $tempDir -Filter "*.nuspec"
foreach ($nuspec in $nuspecFiles) {
    Write-Host "Updating nuspec: $($nuspec.FullName)"
    $content = Get-Content -Path $nuspec.FullName -Raw
    # Replace targetFramework version suffixes in nuspec (e.g., targetFramework="net9.0-android35.0" -> targetFramework="net9.0-android")
    $updatedContent = [System.Text.RegularExpressions.Regex]::Replace($content, '(?<=targetFramework="net\d+\.\d+-(android|ios|maccatalyst|macos|windows))\d+(\.\d+)?', '')
    $updatedContent | Set-Content -Path $nuspec.FullName -Encoding UTF8
}

# Remove original nupkg
Remove-Item -Path $absolutePath -Force

# Zip it back up
Write-Host "Re-packing package to $absolutePath"
[System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, $absolutePath)

# Clean up temp files
Remove-Item -Path $tempDir -Recurse -Force | Out-Null

Write-Host "Successfully cleaned up $NupkgPath"
