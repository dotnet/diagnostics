set(DIAGNOSTICSPAL_SOURCES
    bstr.cpp
    processdescriptor.cpp
    processmodules.cpp
    runtimestartup.cpp
    safecrt/mbusafecrt_compat.cpp
    safecrt/safecrt_output_l.cpp
    safecrt/safecrt_output_s.cpp
    safecrt/safecrt_woutput_s.cpp
    safecrt/swprintf.cpp
    safecrt/vswprint.cpp
    safecrt/xtow_s.cpp
)

set_source_files_properties(
    safecrt/safecrt_output_l.cpp
    safecrt/safecrt_output_s.cpp
    safecrt/xtow_s.cpp
    PROPERTIES COMPILE_OPTIONS -U_UNICODE
)

add_library(diagnosticspal STATIC ${DIAGNOSTICSPAL_SOURCES})

target_include_directories(diagnosticspal
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/inc
        ${CMAKE_CURRENT_SOURCE_DIR}/prebuilt/inc
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/safecrt
        ${COREPAL_SOURCE_DIR}/src/include
        ${RUNTIME_DIR}
        ${CMAKE_CURRENT_BINARY_DIR}/../pal/src
)

target_compile_definitions(diagnosticspal PRIVATE CORECLR LP64COMPATIBLE)
if(CLR_CMAKE_TARGET_LINUX)
    target_compile_definitions(diagnosticspal PRIVATE HAVE_PROCFS_MAPS=1)
endif()
target_link_libraries(diagnosticspal PUBLIC coreclrpal coreclrminipal minipal)
