project(sosplugin)

set(CMAKE_INCLUDE_CURRENT_DIR ON)

# Set the RPATH of the plugin so that it can find dependencies without needing to set LD_LIBRARY_PATH
# For more information: http://www.cmake.org/Wiki/CMake_RPATH_handling.
if (CORECLR_SET_RPATH)
  set(CMAKE_BUILD_WITH_INSTALL_RPATH TRUE)
  if(CLR_CMAKE_HOST_OSX)
    set(CMAKE_INSTALL_RPATH "@loader_path")
  else()
    set(CMAKE_INSTALL_RPATH "\$ORIGIN")
  endif(CLR_CMAKE_HOST_OSX)
endif (CORECLR_SET_RPATH)

set(ENABLE_LLDBPLUGIN ${CLR_CMAKE_HOST_UNIX} CACHE BOOL "Enable building the SOS plugin for LLDB.")
set(REQUIRE_LLDBPLUGIN ${CLR_CMAKE_HOST_LINUX} CACHE BOOL "Require building the SOS plugin for LLDB.")

if(SKIP_LLDBPLUGIN)
    set(REQUIRE_LLDBPLUGIN false)
endif()

if(NOT $ENV{LLVM_HOME} STREQUAL "")
    set(LLDB_INCLUDE_DIR "$ENV{LLVM_HOME}/include")
    set(LLDB_LIB_DIR "$ENV{LLVM_HOME}/lib")
else()
    if(NOT $ENV{LLDB_INCLUDE_DIR} STREQUAL "")
       set(LLDB_INCLUDE_DIR "$ENV{LLDB_INCLUDE_DIR}")
    endif()
    if(NOT $ENV{LLDB_LIB_DIR} STREQUAL "")
       set(LLDB_LIB_DIR "$ENV{LLDB_LIB_DIR}")
    endif()
endif()

set(WITH_LLDB_LIBS "${LLDB_LIB_DIR}" CACHE PATH "Path to LLDB libraries")
set(WITH_LLDB_INCLUDES "${LLDB_INCLUDE_DIR}" CACHE PATH "Path to LLDB headers")

message(STATUS "WITH_LLDB_LIBS: ${WITH_LLDB_LIBS}")
message(STATUS "WITH_LLDB_INCLUDES: ${WITH_LLDB_INCLUDES}")

if(NOT ENABLE_LLDBPLUGIN)
    return()
endif()

if(NOT "$ENV{LLDB_H}" STREQUAL "")
    set(LLDB_H "$ENV{LLDB_H}")
else()
    # Glob matching LLDB headers in standard locations
    file(GLOB LLDB_H_PATHS
        "$ENV{ROOTFS_DIR}/usr/lib/llvm-*/include/lldb/API/LLDB.h"
        "$ENV{ROOTFS_DIR}/usr/local/llvm*/include/lldb/API/LLDB.h"
        "$ENV{ROOTFS_DIR}/usr/include/lldb/API/LLDB.h"
    )

    # Add explicitly specified path if provided
    if(WITH_LLDB_INCLUDES)
        file(GLOB EXTRA_LLDB_H_PATHS "${WITH_LLDB_INCLUDES}/lldb/API/LLDB.h")
        list(APPEND LLDB_H_PATHS ${EXTRA_LLDB_H_PATHS})
    endif()

    # Sort the list to get the highest version last
    list(SORT LLDB_H_PATHS COMPARE NATURAL)

    list(LENGTH LLDB_H_PATHS LLDB_H_PATHS_LEN)
    if(LLDB_H_PATHS_LEN GREATER 0)
        list(GET LLDB_H_PATHS -1 LATEST_LLDB_H_PATH)
        # Go up 3 levels from lldb/API/LLDB.h -> include
        get_filename_component(LLDB_H "${LATEST_LLDB_H_PATH}" DIRECTORY)  # .../API
        get_filename_component(LLDB_H "${LLDB_H}" DIRECTORY)              # .../lldb
        get_filename_component(LLDB_H "${LLDB_H}" DIRECTORY)              # .../include

        # Extract LLVM version from LLDB_H path: match llvm-XX or llvmXX
        string(REGEX MATCH "llvm[-]?([0-9]+)" LLVM_VERSION_MATCH "${LLDB_H}")
        if(LLVM_VERSION_MATCH)
            string(REGEX REPLACE "llvm[-]?([0-9]+)" "\\1" LLDB_LLVM_VERSION "${LLVM_VERSION_MATCH}")
            message(STATUS "Detected LLVM version: ${LLDB_LLVM_VERSION}")
        endif()
    else()
        if(REQUIRE_LLDBPLUGIN)
            set(MESSAGE_MODE FATAL_ERROR)
        else()
            set(MESSAGE_MODE WARNING)
        endif()
        message(${MESSAGE_MODE} "Cannot find LLDB.h. Try installing lldb-dev. You may need to set LLVM_HOME or LLDB_INCLUDE_DIR.")
        return()
    endif()
endif()

message(STATUS "LLDB_H: ${LLDB_H}")

if(NOT $ENV{LLDB_LIB} STREQUAL "")
    set(LLDB_LIB "$ENV{LLDB_LIB}")
else()
    # Check for LLDB library
    if(CLR_CMAKE_HOST_OSX)
        find_library(LLDB_LIB NAMES LLDB lldb lldb-${LLDB_LLVM_VERSION} lldb-6.0 lldb-5.0 lldb-4.0 lldb-3.9 lldb-3.8 lldb-3.7 lldb-3.6 lldb-3.5 PATHS "${WITH_LLDB_LIBS}" PATH_SUFFIXES llvm NO_DEFAULT_PATH)
        find_library(LLDB_LIB NAMES LLDB lldb lldb-${LLDB_LLVM_VERSION} lldb-6.0 lldb-5.0 lldb-4.0 lldb-3.9 lldb-3.8 lldb-3.7 lldb-3.6 lldb-3.5 PATH_SUFFIXES llvm)
        if(LLDB_LIB STREQUAL LLDB_LIB-NOTFOUND)
            if(REQUIRE_LLDBPLUGIN)
                set(MESSAGE_MODE FATAL_ERROR)
            else()
                set(MESSAGE_MODE WARNING)
            endif()
            message(${MESSAGE_MODE} "Cannot find lldb library. Try installing Xcode. You may need to set LLVM_HOME, LLDB_LIB_DIR or LLDB_LIB if the build still can't find it.")
            return()
        endif()
    endif()
endif()

message(STATUS "LLDB_LIB: ${LLDB_LIB}")

add_compile_options(-Wno-delete-non-virtual-dtor)

include_directories(${ROOT_DIR}/src/SOS/inc)
include_directories(${ROOT_DIR}/src/SOS/extensions)
include_directories("${LLDB_H}")

set(SOURCES
    sosplugin.cpp
    soscommand.cpp
    sethostruntimecommand.cpp
    setsostidcommand.cpp
    services.cpp
)

set(LIBRARIES
    extensions
)

if(NOT ${LLDB_LIB} STREQUAL "")
    list(APPEND LIBRARIES ${LLDB_LIB})
endif()

add_library_clr(sosplugin SHARED ${SOURCES})

target_link_libraries(sosplugin ${LIBRARIES})

# add the install targets
install_clr(TARGETS sosplugin DESTINATIONS .)
