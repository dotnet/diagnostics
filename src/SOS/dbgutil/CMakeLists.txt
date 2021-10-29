set(CMAKE_INCLUDE_CURRENT_DIR ON)

if(WIN32)
    include_directories(${ROOT_DIR}/src/inc/llvm)
    #use static crt
    add_definitions(-MT)
endif(WIN32)

add_definitions(-DPAL_STDCPP_COMPAT)

set(DBGUTIL_SOURCES
    dbgutil.cpp
)

if(NOT DEFINED CLR_CMAKE_HOST_OSX)
    list(APPEND DBGUTIL_SOURCES
        elfreader.cpp
    )
endif(NOT DEFINED CLR_CMAKE_HOST_OSX)

if(CLR_CMAKE_HOST_ALPINE_LINUX)
    add_definitions(-DTARGET_ALPINE_LINUX)
endif(CLR_CMAKE_HOST_ALPINE_LINUX)

if(CLR_CMAKE_HOST_UNIX)
    add_definitions(-DHOST_UNIX)
    add_compile_options(-fPIC)
else(CLR_CMAKE_HOST_UNIX)
    add_definitions(-DHOST_WINDOWS)
endif(CLR_CMAKE_HOST_UNIX)

add_library(dbgutil STATIC ${DBGUTIL_SOURCES})
