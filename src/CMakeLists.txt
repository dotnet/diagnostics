if (CLR_CMAKE_HOST_UNIX)
  include_directories("${ROOT_DIR}/src/pal/inc")
  include_directories("${ROOT_DIR}/src/pal/inc/rt")
  include_directories("${ROOT_DIR}/src/pal/src/safecrt")
endif (CLR_CMAKE_HOST_UNIX)

include_directories(${ROOT_DIR}/src)
include_directories(${ROOT_DIR}/src/pal/prebuilt/inc)
include_directories(inc)

if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(pal)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(inc)
add_subdirectory(SOS)

if (CLR_CMAKE_HOST_UNIX)
  # This prevents inclusion of standard C compiler headers
  add_compile_options(-nostdinc)
  include_directories(${ROOT_DIR}/src/pal/inc/rt/cpp)

  add_subdirectory(palrt)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(utilcode)
add_subdirectory(dbgshim)
