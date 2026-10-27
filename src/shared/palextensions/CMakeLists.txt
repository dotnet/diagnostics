set(PALEXTENSIONS_SOURCES
    processdescriptor.cpp
    processmodules.cpp
    runtimestartup.cpp
)

add_library(diagnosticspal STATIC ${PALEXTENSIONS_SOURCES})

target_include_directories(diagnosticspal
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/inc
        ${CMAKE_CURRENT_SOURCE_DIR}/prebuilt/inc
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CLR_SHARED_DIR}/pal/src/include
        ${CLR_SHARED_DIR}/runtime
        ${CMAKE_CURRENT_BINARY_DIR}/../pal/src
)

target_compile_definitions(diagnosticspal PRIVATE CORECLR LP64COMPATIBLE)
if(CLR_CMAKE_TARGET_LINUX)
    target_compile_definitions(diagnosticspal PRIVATE HAVE_PROCFS_MAPS=1)
endif()
target_link_libraries(diagnosticspal PUBLIC coreclrpal coreclrminipal minipal)
