# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Verify minimum required version
cmake_minimum_required(VERSION 3.6.2)

cmake_policy(SET CMP0042 NEW) # MACOSX_RPATH is enabled by default.

# Set the project name
project(diagnostics)

# include shared compiler configs
include(eng/native/configurepaths.cmake)
include(${CLR_ENG_NATIVE_DIR}/configurecompiler.cmake)

# override some global configs
if (MSVC)
  add_compile_options(/wd4960 /wd4961 /wd4603 /wd4627 /wd4838 /wd4456 /wd4457 /wd4458 /wd4459 /wd4091 /we4640)
  add_compile_options($<$<COMPILE_LANGUAGE:CXX>:/EHsc>)
  add_compile_options($<$<OR:$<CONFIG:Release>,$<CONFIG:Relwithdebinfo>>:/MT>)
  add_compile_options($<$<OR:$<CONFIG:Debug>,$<CONFIG:Checked>>:/MTd>)
else (MSVC)
  add_compile_options(-Wno-unused-parameter)
  add_compile_options(-Wno-unused-variable)
  add_compile_options(-Wno-implicit-fallthrough)
  add_compile_options(-fvisibility=default)
endif (MSVC)

# Register the default component
set(CMAKE_INSTALL_DEFAULT_COMPONENT_NAME diagnostics)
add_component(diagnostics)

if(CLR_CMAKE_HOST_WIN32)
  message(STATUS "VS_PLATFORM_TOOLSET is ${CMAKE_VS_PLATFORM_TOOLSET}")
  message(STATUS "VS_PLATFORM_NAME is ${CMAKE_VS_PLATFORM_NAME}")
endif(CLR_CMAKE_HOST_WIN32)

set(ROOT_DIR ${CMAKE_CURRENT_SOURCE_DIR})

if(CLR_CMAKE_HOST_UNIX)
  # Drop the static scope for SOS, so it's available outside the
  # compilation unit for external linkage; see extern declaration
  # in SOS sources.
  file(READ "${VERSION_FILE_PATH}" VERSION_FILE_CONTENTS)
  string(REPLACE "static char" "char" VERSION_LINE_WITHOUT_STATIC "${VERSION_FILE_CONTENTS}")
  file(WRITE "${VERSION_FILE_PATH}" "${VERSION_LINE_WITHOUT_STATIC}")
endif(CLR_CMAKE_HOST_UNIX)

# Where _version.h for Windows is generated
if(CLR_CMAKE_HOST_WIN32)
include_directories(${CLR_ARTIFACTS_OBJ_DIR})
endif(CLR_CMAKE_HOST_WIN32)

set(CORECLR_SET_RPATH ON)
if(CORECLR_SET_RPATH)
  # Enable @rpath support for shared libraries.
  set(MACOSX_RPATH ON)
endif(CORECLR_SET_RPATH)

OPTION(CLR_CMAKE_ENABLE_CODE_COVERAGE "Enable code coverage" OFF)
OPTION(CLR_CMAKE_WARNINGS_ARE_ERRORS "Warnings are errors" ON)

#----------------------------------------------------
# Cross target Component build specific configuration
#----------------------------------------------------
if(CLR_CROSS_COMPONENTS_BUILD)
  add_definitions(-DCROSS_COMPILE)

  if(CLR_CMAKE_HOST_ARCH_AMD64 AND (CLR_CMAKE_TARGET_ARCH_ARM OR CLR_CMAKE_TARGET_ARCH_I386))
    set(FEATURE_CROSSBITNESS 1)
  endif(CLR_CMAKE_HOST_ARCH_AMD64 AND (CLR_CMAKE_TARGET_ARCH_ARM OR CLR_CMAKE_TARGET_ARCH_I386))
endif(CLR_CROSS_COMPONENTS_BUILD)

#------------------------------------
# Definitions (for platform)
#-----------------------------------

if (CLR_CMAKE_HOST_ARCH_AMD64)
  add_definitions(-D_AMD64_)
  add_definitions(-D_WIN64)
  add_definitions(-DAMD64)
  add_definitions(-DBIT64=1)
elseif (CLR_CMAKE_HOST_ARCH_I386)
  add_definitions(-D_X86_)
elseif (CLR_CMAKE_HOST_ARCH_ARM)
  add_definitions(-D_ARM_)
  add_definitions(-DARM)
elseif (CLR_CMAKE_HOST_ARCH_ARM64)
  add_definitions(-D_ARM64_)
  add_definitions(-DARM64)
  add_definitions(-D_WIN64)
  add_definitions(-DBIT64=1)
elseif (CLR_CMAKE_HOST_ARCH_MIPS64)
  add_definitions(-D_MIPS64_)
  add_definitions(-DMIPS64)
  add_definitions(-D_WIN64)
  add_definitions(-DBIT64=1)
else ()
  clr_unknown_arch()
endif ()

if (CLR_CMAKE_TARGET_ARCH_AMD64)
  if (CLR_CMAKE_HOST_UNIX)
    add_definitions(-DDBG_TARGET_AMD64_UNIX)
  endif()
  add_definitions(-D_TARGET_64BIT_=1)
  add_definitions(-D_TARGET_AMD64_=1)
  add_definitions(-DDBG_TARGET_64BIT=1)
  add_definitions(-DDBG_TARGET_AMD64=1)
  add_definitions(-DDBG_TARGET_WIN64=1)
elseif (CLR_CMAKE_TARGET_ARCH_ARM64)
  if (CLR_CMAKE_HOST_UNIX)
    add_definitions(-DDBG_TARGET_ARM64_UNIX)
  endif()
  add_definitions(-D_TARGET_ARM64_=1)
  add_definitions(-D_TARGET_64BIT_=1)
  add_definitions(-DDBG_TARGET_64BIT=1)
  add_definitions(-DDBG_TARGET_ARM64=1)
  add_definitions(-DDBG_TARGET_WIN64=1)
  add_definitions(-DFEATURE_MULTIREG_RETURN)
elseif (CLR_CMAKE_TARGET_ARCH_ARM)
  if (CLR_CMAKE_HOST_UNIX)
    add_definitions(-DDBG_TARGET_ARM_UNIX)
  elseif (WIN32 AND NOT DEFINED CLR_CROSS_COMPONENTS_BUILD)
    # Set this to ensure we can use Arm SDK for Desktop binary linkage when doing native (Arm32) build
    add_definitions(-D_ARM_WINAPI_PARTITION_DESKTOP_SDK_AVAILABLE=1)
    add_definitions(-D_ARM_WORKAROUND_)
  endif (CLR_CMAKE_HOST_UNIX)
  add_definitions(-D_TARGET_ARM_=1)
  add_definitions(-DDBG_TARGET_32BIT=1)
  add_definitions(-DDBG_TARGET_32BIT=1)
  add_definitions(-DDBG_TARGET_ARM=1)
elseif (CLR_CMAKE_TARGET_ARCH_I386)
  add_definitions(-D_TARGET_X86_=1)
  add_definitions(-DDBG_TARGET_32BIT=1)
  add_definitions(-DDBG_TARGET_32BIT=1)
  add_definitions(-DDBG_TARGET_X86=1)
elseif (CLR_CMAKE_TARGET_ARCH_MIPS64)
  add_definitions(-DDBG_TARGET_MIPS64_UNIX)
  add_definitions(-D_TARGET_MIPS64_=1)
  add_definitions(-D_TARGET_64BIT_=1)
  add_definitions(-DDBG_TARGET_MIPS64=1)
  add_definitions(-DDBG_TARGET_64BIT=1)
  add_definitions(-DDBG_TARGET_WIN64=1)
  add_definitions(-DFEATURE_MULTIREG_RETURN)
else ()
  clr_unknown_arch()
endif (CLR_CMAKE_TARGET_ARCH_AMD64)

if(CLR_CMAKE_HOST_WIN32)
  add_definitions(-DWIN32)
  add_definitions(-D_WIN32)
  add_definitions(-DWINVER=0x0602)
  add_definitions(-D_WIN32_WINNT=0x0602)
  add_definitions(-DWIN32_LEAN_AND_MEAN=1)
  add_definitions(-D_CRT_SECURE_NO_WARNINGS)
endif(CLR_CMAKE_HOST_WIN32)

add_definitions(-DUNICODE)
add_definitions(-D_UNICODE)

#--------------------------------------
# FEATURE Defines
#--------------------------------------

add_definitions(-DFEATURE_CORESYSTEM)

if(CLR_CMAKE_HOST_UNIX)
    add_definitions(-DPLATFORM_UNIX=1)
    add_definitions(-DFEATURE_PAL)
    add_definitions(-DFEATURE_PAL_ANSI)
endif(CLR_CMAKE_HOST_UNIX)

if(CLR_CMAKE_HOST_WIN32)
    add_definitions(-DFEATURE_COMINTEROP)
endif(CLR_CMAKE_HOST_WIN32)

if(NOT CMAKE_SYSTEM_NAME STREQUAL NetBSD)
    add_definitions(-DFEATURE_HIJACK)
endif(NOT CMAKE_SYSTEM_NAME STREQUAL NetBSD)

if(FEATURE_EVENT_TRACE)
    add_definitions(-DFEATURE_EVENT_TRACE=1)
    add_definitions(-DFEATURE_PERFTRACING=1)
endif(FEATURE_EVENT_TRACE)

if(CLR_CMAKE_HOST_UNIX_AMD64)
    add_definitions(-DFEATURE_MULTIREG_RETURN)
endif (CLR_CMAKE_HOST_UNIX_AMD64)

if(CLR_CMAKE_HOST_UNIX AND CLR_CMAKE_TARGET_ARCH_AMD64)
    add_definitions(-DUNIX_AMD64_ABI)
endif(CLR_CMAKE_HOST_UNIX AND CLR_CMAKE_TARGET_ARCH_AMD64)

#-----------------------------------------
# Native Projects
#-----------------------------------------
add_subdirectory(src)
