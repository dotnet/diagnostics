include_directories(inc)

if (CLR_CMAKE_HOST_UNIX)
  include_directories("inc/rt")
endif (CLR_CMAKE_HOST_UNIX)

add_subdirectory(shared)
add_subdirectory(SOS)
add_subdirectory(dbgshim)
