cmake_minimum_required(VERSION 3.26)

project(mscordbi_universal_standalone)

get_filename_component(DEFAULT_RUNTIME_REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/runtime" ABSOLUTE)
set(RUNTIME_REPO_ROOT "${DEFAULT_RUNTIME_REPO_ROOT}" CACHE PATH "Path to the dotnet/runtime source snapshot")

include(${RUNTIME_REPO_ROOT}/eng/native/configurepaths.cmake)

set(RUNTIME_GENERATED_INCLUDE_DIR "" CACHE PATH "Directory containing runtime_version.h and _version.h")
if(RUNTIME_GENERATED_INCLUDE_DIR STREQUAL "")
  message(FATAL_ERROR "RUNTIME_GENERATED_INCLUDE_DIR must identify the generated runtime headers.")
endif()
get_filename_component(RUNTIME_GENERATED_INCLUDE_DIR "${RUNTIME_GENERATED_INCLUDE_DIR}" ABSOLUTE)
set(CLR_ARTIFACTS_OBJ_DIR "${CMAKE_CURRENT_BINARY_DIR}/artifacts")
set(CLR_SOURCELINK_FILE_PATH "${RUNTIME_GENERATED_INCLUDE_DIR}/native.sourcelink.json")
set(VERSION_HEADER_PATH "${RUNTIME_GENERATED_INCLUDE_DIR}/_version.h")
set(VERSION_FILE_PATH "${RUNTIME_GENERATED_INCLUDE_DIR}/_version.c")
set(VERSION_FILE_RC_PATH "${RUNTIME_GENERATED_INCLUDE_DIR}/NativeVersion.rc")
set(RUNTIME_VERSION_HEADER_PATH "${RUNTIME_GENERATED_INCLUDE_DIR}/runtime_version.h")

include(${CLR_ENG_NATIVE_DIR}/configurecompiler.cmake)

if(MSVC)
  set_property(DIRECTORY PROPERTY CLR_EH_OPTION /EHa)
endif()

set(CLR_DIR ${CLR_REPO_ROOT_DIR}/src/coreclr)
set(RUNTIME_DIR ${CLR_DIR}/runtime)
set(VM_DIR ${CLR_DIR}/vm)
set(COREPAL_SOURCE_DIR ${CLR_DIR}/pal)
set(GENERATED_INCLUDE_DIR ${CMAKE_CURRENT_BINARY_DIR}/inc)
set(GENERATED_EVENTING_DIR ${CMAKE_CURRENT_BINARY_DIR}/Eventing)
set(CORECLR_SET_RPATH ON)

if(NOT EXISTS ${RUNTIME_VERSION_HEADER_PATH})
  message(FATAL_ERROR
    "${RUNTIME_VERSION_HEADER_PATH} must be generated before configuring the standalone universal DBI build.")
endif()
if(CLR_CMAKE_HOST_WIN32 AND NOT EXISTS ${VERSION_HEADER_PATH})
  message(FATAL_ERROR
    "${VERSION_HEADER_PATH} must be generated before configuring the standalone universal DBI build on Windows.")
endif()

if(NOT EXISTS ${VERSION_FILE_PATH})
  set(VERSION_FILE_PATH ${CLR_ENG_NATIVE_DIR}/version/_version.c)
endif()

if(MSVC)
  set(CMAKE_CXX_STANDARD_LIBRARIES "")
endif()

set(CMAKE_INSTALL_MESSAGE LAZY)

if(NOT DEFINED FEATURE_DYNAMIC_CODE_COMPILED)
  set(FEATURE_DYNAMIC_CODE_COMPILED 1)
endif()
if(NOT DEFINED FEATURE_MULTITHREADING AND NOT CLR_CMAKE_TARGET_ARCH_WASM)
  set(FEATURE_MULTITHREADING 1)
endif()
if(FEATURE_MULTITHREADING)
  add_compile_definitions(FEATURE_MULTITHREADING)
endif()

include_directories(${CLR_SRC_NATIVE_DIR})
include_directories(${CLR_SRC_NATIVE_DIR}/inc)

add_component(Unspecified)
add_component(runtime)
add_component(paltests paltests_install)

set(EP_GENERATED_HEADER_PATH ${GENERATED_INCLUDE_DIR})
include(${CLR_SRC_NATIVE_DIR}/eventpipe/configure.cmake)
add_subdirectory(${CLR_SRC_NATIVE_DIR}/containers containers)
add_subdirectory(${CLR_SRC_NATIVE_DIR}/eventpipe eventpipe)
add_subdirectory(${CLR_SRC_NATIVE_DIR}/minipal shared_minipal)

if(CLR_CMAKE_TARGET_LINUX)
  include(CheckCSourceCompiles)
  check_c_source_compiles(
    "
    #include <linux/membarrier.h>
    int main(void)
    {
      return MEMBARRIER_CMD_PRIVATE_EXPEDITED | MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED;
    }
    "
    HAVE_PRIVATE_EXPEDITED_MEMBARRIER_COMMANDS)
  if(NOT HAVE_PRIVATE_EXPEDITED_MEMBARRIER_COMMANDS)
    # These values are part of the Linux UAPI but are absent from older kernel headers.
    foreach(minipal_target IN ITEMS minipal_objects aotminipal)
      target_compile_definitions(${minipal_target} PRIVATE
        MEMBARRIER_CMD_PRIVATE_EXPEDITED=8
        MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED=16)
    endforeach()
  endif()
endif()

if(CLR_CMAKE_HOST_UNIX)
  add_subdirectory(pal)
endif()

add_subdirectory(${CLR_DIR}/debug/debug-pal debug/debug-pal)
add_subdirectory(${CLR_DIR}/minipal minipal)

if(CLR_CMAKE_HOST_WIN32)
  add_compile_options($<$<COMPILE_LANGUAGE:C,CXX>:/Zl>)
endif()

include_directories(${CLR_DIR}/pal/prebuilt/inc)
include_directories(${RUNTIME_GENERATED_INCLUDE_DIR})

include(${CLR_DIR}/clrdefinitions.cmake)
if(NOT TARGET eventing_headers)
  add_custom_target(eventing_headers)
endif()
if(FEATURE_STANDALONE_GC)
  add_compile_definitions(FEATURE_STANDALONE_GC)
endif()

if(CLR_CMAKE_HOST_UNIX)
  include_directories(${CLR_DIR}/pal/inc)
  include_directories(${CLR_DIR}/pal/inc/rt)
  include_directories(${CLR_DIR}/pal/src/safecrt)
endif()

include_directories(${CLR_DIR}/inc)
include_directories(${CLR_DIR}/debug/inc)
include_directories(${CLR_DIR}/debug/inc/${ARCH_SOURCES_DIR})
include_directories(${CLR_DIR}/debug/inc/dump)
include_directories(${CLR_DIR}/md/inc)
include_directories(${GENERATED_INCLUDE_DIR})
include_directories(${CLR_DIR}/hosts/inc)
include_directories(${CLR_DIR}/interpreter/inc)
include_directories(${CLR_DIR}/minipal)

if(CLR_CMAKE_TARGET_WIN32 AND FEATURE_EVENT_TRACE)
  include_directories(${GENERATED_INCLUDE_DIR}/etw)
endif()
add_component(debug)

add_subdirectory(${CLR_DIR}/debug/dbgutil debug/dbgutil)

if(CLR_CMAKE_HOST_UNIX)
  add_compile_options($<$<COMPILE_LANG_AND_ID:CXX,Clang,AppleClang>:-Wno-null-arithmetic>)
  add_compile_options($<$<COMPILE_LANG_AND_ID:CXX,Clang>:-Wno-sync-alignment>)
  add_compile_options($<$<COMPILE_LANG_AND_ID:CXX,GNU>:-Wno-conversion-null>)
  add_compile_options($<$<COMPILE_LANG_AND_ID:CXX,GNU>:-Wno-pointer-arith>)

  set(NATIVE_RESOURCE_DIR ${CLR_DIR}/nativeresources)
  include_directories(${NATIVE_RESOURCE_DIR})
  set(PROCESS_RC_SCRIPT ${NATIVE_RESOURCE_DIR}/processrc.sh)
  set(RESOURCE_STRING_HEADER_DIR ${NATIVE_RESOURCE_DIR})

  function(build_resources SOURCE TARGET_NAME TARGET_FILE)
    set_property(SOURCE ${SOURCE} APPEND PROPERTY COMPILE_DEFINITIONS "RC_INVOKED")
    set(PREPROCESSED_SOURCE ${CMAKE_CURRENT_BINARY_DIR}/${TARGET_NAME}.rc.i)
    preprocess_file(${SOURCE} ${PREPROCESSED_SOURCE})
    set(RESOURCE_ENTRY_ARRAY_CPP ${CMAKE_CURRENT_BINARY_DIR}/${TARGET_NAME}.cpp)
    add_custom_command(
      OUTPUT ${RESOURCE_ENTRY_ARRAY_CPP}
      COMMAND ${PROCESS_RC_SCRIPT} ${PREPROCESSED_SOURCE} ${TARGET_NAME} >${RESOURCE_ENTRY_ARRAY_CPP}
      DEPENDS ${PREPROCESSED_SOURCE} ${PROCESS_RC_SCRIPT}
    )
    include_directories(${RESOURCE_STRING_HEADER_DIR})
    set(${TARGET_FILE} ${RESOURCE_ENTRY_ARRAY_CPP} PARENT_SCOPE)
  endfunction()

  add_subdirectory(${CLR_DIR}/nativeresources nativeresources)
endif()

add_subdirectory(${CLR_DIR}/utilcode utilcode)
add_subdirectory(${CLR_DIR}/inc inc)
add_subdirectory(${CLR_DIR}/md md)
add_subdirectory(runtime-debug debug)
add_subdirectory(dlls)
