# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# See LICENSE file in the project root for full license information.

cmake_minimum_required(VERSION 3.18)

# Prevent CMake 3.30+ deprecation errors for subprojects requiring CMake < 3.5 (e.g. cpu_features)
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)

project(qourex_fasterwhisper_native LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(WITH_CUDA "Build with CUDA support" ON)
option(WITH_CUDNN "Build with cuDNN support" ON)
option(WITH_DNNL "Build with oneDNN support" OFF)
option(WITH_MKL "Build with MKL support" ON)
option(WITH_OPENBLAS "Build with OpenBLAS" OFF)
option(BUILD_CLI "Build CTranslate2 CLI tools" OFF)

# Build CTranslate2 as a shared library so our wrapper can link to it
set(BUILD_SHARED_LIBS ON CACHE BOOL "Build CTranslate2 as a shared library" FORCE)

include(FetchContent)

FetchContent_Declare(
  ctranslate2
  GIT_REPOSITORY https://github.com/OpenNMT/CTranslate2.git
  GIT_TAG        v4.7.0
  GIT_SUBMODULES_RECURSE ON
)

FetchContent_MakeAvailable(ctranslate2)

add_library(qourex_fasterwhisper_native SHARED qourex_fasterwhisper_native.cpp qourex_fasterwhisper_native.h)

target_link_libraries(qourex_fasterwhisper_native PRIVATE ctranslate2)

target_include_directories(qourex_fasterwhisper_native PRIVATE 
    ${ctranslate2_SOURCE_DIR}/include
    ${ctranslate2_BINARY_DIR}/include
)

set_target_properties(qourex_fasterwhisper_native PROPERTIES
    OUTPUT_NAME "qourex_fasterwhisper_native"
    PREFIX ""
)
