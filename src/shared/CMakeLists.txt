include(runtime.cmake)
include_directories(${CLR_SRC_NATIVE_DIR})
include_directories(${CLR_SRC_NATIVE_DIR}/inc)

add_subdirectory(${CLR_SRC_NATIVE_DIR}/minipal native/minipal)

if (CLR_CMAKE_TARGET_LINUX)
  include(CheckCSourceCompiles)
  check_c_source_compiles("
    #include <linux/membarrier.h>
    int main(void)
    {
      return MEMBARRIER_CMD_PRIVATE_EXPEDITED | MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED;
    }"
    HAVE_PRIVATE_EXPEDITED_MEMBARRIER_COMMANDS)
  if (NOT HAVE_PRIVATE_EXPEDITED_MEMBARRIER_COMMANDS)
    # Older Linux SDK headers predate these Linux UAPI enum values.
    target_compile_definitions(minipal_objects PRIVATE
      MEMBARRIER_CMD_PRIVATE_EXPEDITED=8
      MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED=16)
    target_compile_definitions(aotminipal PRIVATE
      MEMBARRIER_CMD_PRIVATE_EXPEDITED=8
      MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED=16)
  endif()
endif()

if (CLR_CMAKE_HOST_UNIX)
  # Runtime enables event tracing; diagnostics does not mirror its generated providers.
  set(FEATURE_EVENT_TRACE 0)
  include_directories(${RUNTIME_DIR})
  include_directories(${COREPAL_SOURCE_DIR}/inc)
  include_directories(${COREPAL_SOURCE_DIR}/src)
  include_directories(${CMAKE_CURRENT_SOURCE_DIR}/inc)
  add_subdirectory(${COREPAL_SOURCE_DIR}/src pal/src)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(${CORECLR_MINIPAL_SOURCE_DIR} minipal)

if (CLR_CMAKE_TARGET_WIN32 AND CLR_CMAKE_TARGET_ARCH_I386)
  target_compile_options(coreclrminipal PRIVATE /FIlimits.h)
endif()

include(shared.cmake)

add_subdirectory(inc)
add_subdirectory(debug)

if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(diagnosticspal)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(utilcode)
