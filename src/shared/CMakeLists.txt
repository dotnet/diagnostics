if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(pal)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(inc)
add_subdirectory(dbgutil)

if (CLR_CMAKE_HOST_UNIX)
  # This prevents inclusion of standard C compiler headers
  add_compile_options(-nostdinc)
  include_directories(${CLR_SHARED_DIR}/pal/inc/rt/cpp)
  add_subdirectory(palrt)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(utilcode)
