include_directories(${ROOT_DIR}/src/pal/prebuilt/inc)

include_directories(inc)

if (CLR_CMAKE_HOST_UNIX)
    add_subdirectory(palrt)
    add_subdirectory(pal)
endif(CLR_CMAKE_HOST_UNIX)

add_subdirectory(inc)
add_subdirectory(SOS)
