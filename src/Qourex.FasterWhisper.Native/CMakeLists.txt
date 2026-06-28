# Copyright (c) 2026 Qourex. Licensed under the MIT License.
# See LICENSE file in the project root for full license information.

cmake_minimum_required(VERSION 3.18)

# Prevent CMake 3.30+ deprecation errors for subprojects requiring CMake < 3.5 (e.g. cpu_features)
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)

project(qourex_fasterwhisper_native LANGUAGES CXX C)

option(WITH_CUDA "Build with CUDA support" ON)

if(WITH_CUDA)
  # Set CUDA architectures up to Hopper (9.0) which all CMake versions recognize
  set(CUDA_ARCH_LIST "5.3;6.0;6.1;7.0;7.5;8.0;8.6;8.9;9.0" CACHE STRING "CUDA architectures list for FindCUDA" FORCE)
  # Manually append compute_100 (Blackwell) nvcc flags to bypass older CMake database limitations
  list(APPEND CUDA_NVCC_FLAGS "-gencode;arch=compute_100,code=sm_100;-gencode;arch=compute_100,code=compute_100")
  set(CUDA_NVCC_FLAGS "${CUDA_NVCC_FLAGS}" CACHE STRING "Custom NVCC flags" FORCE)
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
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

FetchContent_GetProperties(ctranslate2)
if(NOT ctranslate2_POPULATED)
  FetchContent_Populate(ctranslate2)

  message(STATUS "Patching CTranslate2 thread_pool.cc for Android support...")
  set(THREAD_POOL_SRC "${ctranslate2_SOURCE_DIR}/src/thread_pool.cc")
  if(EXISTS "${THREAD_POOL_SRC}")
    file(READ "${THREAD_POOL_SRC}" THREAD_POOL_CONTENT)
    string(REPLACE 
      "const int status = pthread_setaffinity_np(thread.native_handle(), sizeof (cpu_set_t), &cpuset);"
      "#ifndef __ANDROID__\n    const int status = pthread_setaffinity_np(thread.native_handle(), sizeof (cpu_set_t), &cpuset);\n#else\n    const int status = 0;\n#endif"
      THREAD_POOL_CONTENT_PATCHED
      "${THREAD_POOL_CONTENT}"
    )
    file(WRITE "${THREAD_POOL_SRC}" "${THREAD_POOL_CONTENT_PATCHED}")
    message(STATUS "CTranslate2 thread_pool.cc successfully patched.")
  else()
    message(WARNING "Could not find CTranslate2 thread_pool.cc at: ${THREAD_POOL_SRC}")
  endif()

  add_subdirectory(${ctranslate2_SOURCE_DIR} ${ctranslate2_BINARY_DIR})
endif()

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
