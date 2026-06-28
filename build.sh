#!/usr/bin/env bash
# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# Qourex.FasterWhisper.NET Linux Build Automation Script
# Combines native C++ CMake compilation and .NET NuGet packaging.
# Supports CPU-only and GPU/CUDA-enabled package builds.
#
# Usage:
#   ./build.sh              # Build both CPU and GPU packages (skips GPU if CUDA is missing)
#   ./build.sh --cpu-only   # Build only the CPU package
#   ./build.sh --gpu-only   # Build only the GPU package (fails if CUDA is missing)

set -e

# Parse arguments
cpu_only=false
gpu_only=false

for arg in "$@"; do
  case $arg in
    --cpu-only)
      cpu_only=true
      shift
      ;;
    --gpu-only)
      gpu_only=true
      shift
      ;;
    *)
      echo "Unknown argument: $arg"
      echo "Usage: ./build.sh [--cpu-only | --gpu-only]"
      exit 1
      ;;
  esac
done

if [ "$cpu_only" = true ] && [ "$gpu_only" = true ]; then
  echo "Error: Cannot specify both --cpu-only and --gpu-only"
  exit 1
fi

build_cpu=true
build_gpu=true

if [ "$cpu_only" = true ]; then
  build_gpu=false
elif [ "$gpu_only" = true ]; then
  build_cpu=false
fi

echo -e "\e[36m==================================================\e[0m"
echo -e "\e[36m         Building Qourex.FasterWhisper.NET         \e[0m"
echo -e "\e[36m==================================================\e[0m"

# 1. Paths Setup
ROOT_DIR="$(pwd)"
NATIVE_DIR="$ROOT_DIR/src/Qourex.FasterWhisper.Native"
BUILD_CPU_DIR="$NATIVE_DIR/build_cpu"
BUILD_GPU_DIR="$NATIVE_DIR/build_gpu"
CSHARP_CPU_DIR="$ROOT_DIR/src/Qourex.FasterWhisper.NET"
CSHARP_GPU_DIR="$ROOT_DIR/src/Qourex.FasterWhisper.NET.Gpu"

# CUDA detection helper
check_cuda_available() {
  if [ -n "$CUDA_PATH" ]; then return 0; fi
  if command -v nvcc &> /dev/null; then return 0; fi
  if [ -d "/usr/local/cuda" ]; then return 0; fi
  return 1
}

# Native build and library staging helper
build_native_and_stage() {
  local build_dir="$1"
  local cuda_enabled="$2"
  local csharp_project_dir="$3"

  if [ "$cuda_enabled" = true ]; then
    echo -e "\n\e[32mConfiguring and building native C++ wrapper (CUDA)...\e[0m"
  else
    echo -e "\n\e[32mConfiguring and building native C++ wrapper (CPU + OpenBLAS)...\e[0m"
  fi

  # Clean existing CMake cache and build files recursively to prevent path/platform mismatches
  if [ -d "$build_dir" ]; then
    echo "Cleaning existing CMake cache files recursively from: $build_dir"
    find "$build_dir" -name "CMakeCache.txt" -delete
    find "$build_dir" -name "CMakeFiles" -type d -exec rm -rf {} +
    find "$build_dir" -name "Makefile" -delete
    find "$build_dir" -name "cmake_install.cmake" -delete
  fi

  mkdir -p "$build_dir"
  pushd "$build_dir" > /dev/null

  cmake_flags=()
  if [ "$cuda_enabled" = true ]; then
    cmake_flags=(
      "-DWITH_CUDA=ON"
      "-DWITH_CUDNN=ON"
      "-DWITH_MKL=OFF"
      "-DOPENMP_RUNTIME=COMP"
    )
  else
    cmake_flags=(
      "-DWITH_CUDA=OFF"
      "-DWITH_CUDNN=OFF"
      "-DWITH_MKL=OFF"
      "-DWITH_OPENBLAS=ON"
      "-DOPENMP_RUNTIME=COMP"
    )
  fi

  # Check if ninja is available to compile fast in parallel
  generator=""
  if command -v ninja >/dev/null 2>&1; then
    generator="-G Ninja"
    echo "Using Ninja build generator for parallel compilation."
  fi

  # Run CMake configuration and build
  echo "Running CMake configuration in: $build_dir"
  cmake "$NATIVE_DIR" -DCMAKE_BUILD_TYPE=Release $generator "${cmake_flags[@]}"
  cmake --build . --config Release

  popd > /dev/null

  # Stage native libraries
  echo -e "\e[32mStaging native libraries into C# project structure...\e[0m"
  local runtime_native_dir="$csharp_project_dir/runtimes/linux-x64/native"
  mkdir -p "$runtime_native_dir"

  # Copy wrapper library
  local compiled_so="$build_dir/qourex_fasterwhisper_native.so"
  if [ ! -f "$compiled_so" ]; then
    echo "Error: Compiled native wrapper not found at: $compiled_so"
    exit 1
  fi
  cp "$compiled_so" "$runtime_native_dir/"
  echo "  Copied qourex_fasterwhisper_native.so"

  # Copy CTranslate2 library
  local ct2_so="$build_dir/_deps/ctranslate2-build/libctranslate2.so"
  if [ -f "$ct2_so" ]; then
    cp "$ct2_so" "$runtime_native_dir/libctranslate2.so"
    cp "$ct2_so" "$runtime_native_dir/libctranslate2.so.4"
    cp "$ct2_so" "$runtime_native_dir/libctranslate2.so.4.7.0"
    echo "  Copied libctranslate2.so, libctranslate2.so.4, and libctranslate2.so.4.7.0"
  else
    echo "Error: libctranslate2.so not found at: $ct2_so"
    exit 1
  fi

  echo -e "  All native libraries staged at: $runtime_native_dir"
}

# 2. Execute Native Builds

# CPU Build
if [ "$build_cpu" = true ]; then
  build_native_and_stage "$BUILD_CPU_DIR" false "$CSHARP_CPU_DIR"
fi

# GPU Build
if [ "$build_gpu" = true ]; then
  if check_cuda_available; then
    build_native_and_stage "$BUILD_GPU_DIR" true "$CSHARP_GPU_DIR"
  else
    if [ "$gpu_only" = true ]; then
      echo "Error: CUDA Toolkit / nvcc not detected on this machine. Cannot build GPU version."
      exit 1
    else
      echo -e "\n\e[33mCUDA Toolkit not detected. Skipping GPU package build.\e[0m"
      build_gpu=false
    fi
  fi
fi

# 3. Build Solution and Pack NuGet Packages
if command -v dotnet &> /dev/null; then
  echo -e "\n\e[32mBuilding solution and packing NuGet packages...\e[0m"

  dotnet build "$ROOT_DIR/Qourex.FasterWhisper.slnx" --configuration Release

  if [ "$build_cpu" = true ]; then
    dotnet pack "$CSHARP_CPU_DIR" --configuration Release --output "$ROOT_DIR/artifacts"
  fi

  if [ "$build_gpu" = true ]; then
    # Ensure local copies of readme etc. exist in the GPU folder for packaging
    cp "$ROOT_DIR/README_GPU.md" "$CSHARP_GPU_DIR/README.md"
    cp "$ROOT_DIR/THIRD_PARTY_NOTICES.md" "$CSHARP_GPU_DIR/"
    cp "$ROOT_DIR/CHANGELOG.md" "$CSHARP_GPU_DIR/"
    dotnet pack "$CSHARP_GPU_DIR" --configuration Release --output "$ROOT_DIR/artifacts"
  fi

  echo -e "\n\e[36m==================================================\e[0m"
  echo -e "\e[36m Build Completed! NuGet packages are in: ./artifacts\e[0m"
  echo -e "\e[36m==================================================\e[0m"
else
  echo -e "\n\e[33mDotnet SDK not found in PATH. Skipping .NET build and pack steps.\e[0m"
  echo -e "You can commit/push the staged '.so' files, and GitHub Actions will package them.\e[0m"
fi
