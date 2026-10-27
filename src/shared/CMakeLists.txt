include(runtime.cmake)
include_directories(${CLR_SRC_NATIVE_DIR})
include_directories(${CLR_SRC_NATIVE_DIR}/inc)

add_subdirectory(${CLR_SRC_NATIVE_DIR}/minipal native/minipal)

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

include(shared.cmake)

add_subdirectory(inc)
add_subdirectory(debug)

if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(palrt)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(utilcode)
