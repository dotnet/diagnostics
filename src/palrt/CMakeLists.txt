set(CMAKE_INCLUDE_CURRENT_DIR ON)

include_directories(${ROOT_DIR}/src/pal/inc)
include_directories(${ROOT_DIR}/src/pal/inc/rt)

# Include the dummy c++ include files
include_directories(${ROOT_DIR}/src/pal/inc/rt/cpp)

# This prevents inclusion of standard C compiler headers
add_compile_options(-nostdinc)

add_compile_options(-fPIC)
add_definitions(-DUNICODE)
add_definitions(-D_UNICODE)

set(PALRT_SOURCES
    bstr.cpp
    coguid.cpp
    comem.cpp
    guid.cpp
    memorystream.cpp
    path.cpp
    urlpars.cpp
    unicode.cpp
    variant.cpp
)

add_library(palrt
    STATIC
    ${PALRT_SOURCES}
)
