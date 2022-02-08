set(CMAKE_INCLUDE_CURRENT_DIR ON)

if(CLR_CMAKE_HOST_WIN32)
    include_directories(${CLR_SHARED_DIR}/inc/llvm)
    #use static crt
    set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
endif(CLR_CMAKE_HOST_WIN32)

add_definitions(-DPAL_STDCPP_COMPAT)

if(CLR_CMAKE_TARGET_ALPINE_LINUX)
    add_definitions(-DTARGET_ALPINE_LINUX)
endif(CLR_CMAKE_TARGET_ALPINE_LINUX)

set(DBGUTIL_SOURCES
    dbgutil.cpp
)

if(NOT DEFINED CLR_CMAKE_HOST_OSX)
    list(APPEND DBGUTIL_SOURCES
        elfreader.cpp
    )
endif(NOT DEFINED CLR_CMAKE_HOST_OSX)

add_library(dbgutil STATIC ${DBGUTIL_SOURCES})
