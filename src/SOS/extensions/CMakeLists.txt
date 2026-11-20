set(CMAKE_INCLUDE_CURRENT_DIR ON)

include(configure.cmake)

include_directories(${ROOT_DIR}/src/SOS/inc)

set(SOURCES
    hostnativeaot.cpp
    extensions.cpp
)

if(CLR_CMAKE_HOST_UNIX)
    add_definitions(-DHOST_UNIX)
else(CLR_CMAKE_HOST_UNIX)
    add_definitions(-DHOST_WINDOWS)
endif(CLR_CMAKE_HOST_UNIX)

add_library(extensions STATIC ${SOURCES})
