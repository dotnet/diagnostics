set(CLR_SHARED_DIR ${ROOT_DIR}/src/shared)

if (CLR_CMAKE_HOST_UNIX)
  include_directories("${CLR_SHARED_DIR}/pal/inc")
  include_directories("${CLR_SHARED_DIR}/pal/inc/rt")
  include_directories("${CLR_SHARED_DIR}/pal/src/safecrt")
endif (CLR_CMAKE_HOST_UNIX)

include_directories(${CLR_SHARED_DIR})
include_directories(${CLR_SHARED_DIR}/inc)
include_directories(${CLR_SHARED_DIR}/pal/prebuilt/inc)

add_subdirectory(shared)
add_subdirectory(SOS)

if (CLR_CMAKE_HOST_UNIX)
  # This prevents inclusion of standard C compiler headers
  add_compile_options(-nostdinc)
  include_directories(${CLR_SHARED_DIR}/pal/inc/rt/cpp)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(dbgshim)
