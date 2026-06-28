# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# Qourex.FasterWhisper.NET Build Automation Script
# Combines native C++ CMake compilation and .NET NuGet packaging.
# Supports CPU-only and GPU/CUDA-enabled package builds.
#
# Usage:
#   .\build.ps1              # Build both CPU and GPU packages (default, skips GPU if CUDA is missing)
#   .\build.ps1 -CpuOnly     # Build only the CPU package
#   .\build.ps1 -GpuOnly     # Build only the GPU package (fails if CUDA is missing)

param(
    [switch]$CpuOnly,
    [switch]$GpuOnly
)

$ErrorActionPreference = "Stop"

# Determine what to build
$buildCpu = $true
$buildGpu = $true

if ($CpuOnly -and $GpuOnly) {
    throw "Cannot specify both -CpuOnly and -GpuOnly"
} elseif ($CpuOnly) {
    $buildGpu = $false
} elseif ($GpuOnly) {
    $buildCpu = $false
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "         Building Qourex.FasterWhisper.NET         " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Paths Setup
$RootDir = Get-Location
$NativeDir = Join-Path $RootDir "src\Qourex.FasterWhisper.Native"
$BuildCpuDir = Join-Path $NativeDir "build_cpu"
$BuildGpuDir = Join-Path $NativeDir "build_gpu"
$CSharpCpuDir = Join-Path $RootDir "src\Qourex.FasterWhisper.NET"
$CSharpGpuDir = Join-Path $RootDir "src\Qourex.FasterWhisper.NET.Gpu"

# Ensure CMake from VS 2026 Enterprise is in the PATH so it is always available
$VsCMakeDir = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin"
if (Test-Path $VsCMakeDir) {
    $env:PATH = "$VsCMakeDir;$env:PATH"
}

# Prefer VS 2022 Build Tools for CUDA compatibility, falling back to VS 2026 Enterprise
$VsDevCmd = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
if (-not (Test-Path $VsDevCmd)) {
    $VsDevCmd = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat"
}

# CUDA detection helper
function Check-Cuda-Available {
    if ($null -ne $env:CUDA_PATH) { return $true }
    if (Get-Command nvcc -ErrorAction SilentlyContinue) { return $true }
    if (Test-Path "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA") { return $true }
    return $false
}

# Native build and DLL staging helper
function Build-Native-And-Stage($buildDir, $cudaEnabled, $csharpProjectDir) {
    if ($cudaEnabled) {
        Write-Host "`nConfiguring and building native C++ wrapper (CUDA + MKL)..." -ForegroundColor Green
    } else {
        Write-Host "`nConfiguring and building native C++ wrapper (CPU only + MKL)..." -ForegroundColor Green
    }

    # Clean existing CMake cache and build files recursively to prevent path/platform mismatches
    if (Test-Path $buildDir) {
        Write-Host "Cleaning existing CMake cache files recursively from: $buildDir"
        Get-ChildItem -Path $buildDir -Filter "CMakeCache.txt" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
        Get-ChildItem -Path $buildDir -Filter "CMakeFiles" -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem -Path $buildDir -Filter "Makefile" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
        Get-ChildItem -Path $buildDir -Filter "cmake_install.cmake" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $buildDir)) {
        New-Item -ItemType Directory -Path $buildDir | Out-Null
    }

    Push-Location $buildDir
    try {
        $cudaFlags = ""
        if ($cudaEnabled) {
            $cudaFlags = "-DWITH_CUDA=ON -DWITH_CUDNN=ON -DCMAKE_CUDA_ARCHITECTURES=`"53;60;61;70;75;80;86;89;90;100;100+PTX`""
        } else {
            $cudaFlags = "-DWITH_CUDA=OFF -DWITH_CUDNN=OFF"
        }

        # Configure and build CMake using Ninja and Visual Studio tools
        Write-Host "Using VS Dev Cmd: $VsDevCmd"
        Write-Host "Running CMake configuration and building DLL in: $buildDir"
        
        $ninjaPath = "ninja"
        $vsNinjaPaths = @(
            "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
            "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
            "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
            "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
            "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
        )
        foreach ($p in $vsNinjaPaths) {
            if (Test-Path $p) {
                $ninjaPath = $p
                break
            }
        }
        Write-Host "Using Ninja path: $ninjaPath"

        $buildCmd = "call `"$VsDevCmd`" -arch=x64 && cmake .. -G `"Ninja`" -DCMAKE_MAKE_PROGRAM=`"$ninjaPath`" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DOPENMP_RUNTIME=INTEL -DWITH_MKL=ON -DMKL_ROOT=`"C:\Program Files (x86)\Intel\oneAPI\mkl\latest`" -DWITH_DNNL=OFF $cudaFlags && cmake --build ."
        cmd.exe /c $buildCmd
        
        if ($LASTEXITCODE -ne 0) {
            throw "CMake build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    # Stage native DLLs
    Write-Host "Staging native DLLs into C# project structure..." -ForegroundColor Green
    $runtimeNativeDir = Join-Path $csharpProjectDir "runtimes\win-x64\native"
    if (-not (Test-Path $runtimeNativeDir)) {
        New-Item -ItemType Directory -Path $runtimeNativeDir | Out-Null
    }

    # Copy wrapper DLL
    $compiledDll = Join-Path $buildDir "qourex_fasterwhisper_native.dll"
    if (-not (Test-Path $compiledDll)) {
        throw "Compiled native wrapper DLL not found at: $compiledDll"
    }
    Copy-Item -Path $compiledDll -Destination $runtimeNativeDir -Force
    Write-Host "  Copied qourex_fasterwhisper_native.dll"

    # Copy CTranslate2 shared library
    $ct2Dll = Join-Path $buildDir "_deps\ctranslate2-build\ctranslate2.dll"
    if (Test-Path $ct2Dll) {
        Copy-Item -Path $ct2Dll -Destination $runtimeNativeDir -Force
        Write-Host "  Copied ctranslate2.dll"
    }

    # Copy MKL runtime DLLs
    $mklDir = "C:\Program Files (x86)\Intel\oneAPI\mkl\latest\bin"
    if (Test-Path $mklDir) {
        $mklDlls = @(
            "mkl_core.3.dll", "mkl_intel_thread.3.dll", "mkl_def.3.dll", "mkl_mc3.3.dll", "mkl_avx2.3.dll", "mkl_avx512.3.dll",
            "mkl_core.2.dll", "mkl_intel_thread.2.dll", "mkl_def.2.dll", "mkl_mc3.2.dll", "mkl_avx2.2.dll", "mkl_avx512.2.dll"
        )
        foreach ($dll in $mklDlls) {
            $dllPath = Join-Path $mklDir $dll
            if (Test-Path $dllPath) {
                Copy-Item -Path $dllPath -Destination $runtimeNativeDir -Force
                Write-Host "  Copied $dll"
            }
        }
    }

    # Copy Intel OpenMP and Compiler runtime DLLs
    $iompDir = "C:\Program Files (x86)\Intel\oneAPI\compiler\latest\bin"
    if (Test-Path $iompDir) {
        $compilerDlls = @("libiomp5md.dll", "libmmd.dll", "libircmd.dll", "svml_dispmd.dll")
        foreach ($dll in $compilerDlls) {
            $dllPath = Join-Path $iompDir $dll
            if (Test-Path $dllPath) {
                Copy-Item -Path $dllPath -Destination $runtimeNativeDir -Force
                Write-Host "  Copied $dll"
            }
        }
    }

    Write-Host "  All native DLLs staged at: $runtimeNativeDir"
}

# 2. Execute Native Builds

# CPU Build
if ($buildCpu) {
    Build-Native-And-Stage -buildDir $BuildCpuDir -cudaEnabled $false -csharpProjectDir $CSharpCpuDir
}

# GPU Build
if ($buildGpu) {
    $cudaAvailable = Check-Cuda-Available
    if (-not $cudaAvailable) {
        if ($GpuOnly) {
            throw "CUDA Toolkit not detected on this machine. Cannot build GPU version."
        } else {
            Write-Host "`nCUDA Toolkit not detected. Skipping GPU package build." -ForegroundColor Yellow
            $buildGpu = $false
        }
    } else {
        Build-Native-And-Stage -buildDir $BuildGpuDir -cudaEnabled $true -csharpProjectDir $CSharpGpuDir
    }
}

# 3. Build Solution and Pack NuGet Packages
Write-Host "`nBuilding solution and packing NuGet packages..." -ForegroundColor Green

dotnet build (Join-Path $RootDir "Qourex.FasterWhisper.slnx") --configuration Release

if ($buildCpu) {
    dotnet pack $CSharpCpuDir --configuration Release --output (Join-Path $RootDir "artifacts")
}

if ($buildGpu) {
    # Ensure local copies of readme etc. exist in the GPU folder for packaging
    Copy-Item -Path (Join-Path $RootDir "README_GPU.md") -Destination (Join-Path $CSharpGpuDir "README.md") -Force
    Copy-Item -Path (Join-Path $RootDir "THIRD_PARTY_NOTICES.md") -Destination $CSharpGpuDir -Force
    Copy-Item -Path (Join-Path $RootDir "CHANGELOG.md") -Destination $CSharpGpuDir -Force
    dotnet pack $CSharpGpuDir --configuration Release --output (Join-Path $RootDir "artifacts")
}

Write-Host "`n==================================================" -ForegroundColor Cyan
Write-Host " Build Completed! NuGet packages are in: ./artifacts" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
