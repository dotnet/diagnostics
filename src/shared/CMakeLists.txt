include_directories(${CLR_SRC_NATIVE_DIR})

if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(pal)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(minipal)

include(shared.cmake)

add_subdirectory(inc)
add_subdirectory(debug)

if (CLR_CMAKE_HOST_UNIX)
  add_subdirectory(palrt)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(utilcode)
