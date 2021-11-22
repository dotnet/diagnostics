project(runcommand)

set(CMAKE_INCLUDE_CURRENT_DIR ON)

include_directories(inc)
include_directories("$ENV{VSInstallDir}/DIA SDK/include")

add_definitions(-DUSE_STL)

#use static crt
add_definitions(-MT) 

set(RUNCOMMAND_SOURCES
    runcommand.cpp
)
  
set(RUNCOMMAND_LIBRARY
    ${STATIC_MT_CRT_LIB}
    ${STATIC_MT_CPP_LIB}
    ${STATIC_MT_VCRT_LIB}
    kernel32.lib
    user32.lib
    ole32.lib
    oleaut32.lib
    dbghelp.lib
    uuid.lib
    version.lib
    dbgeng.lib
    advapi32.lib
    psapi.lib
    ntdll.lib
)

add_library_clr(runcommand SHARED ${RUNCOMMAND_SOURCES})

target_link_libraries(runcommand ${RUNCOMMAND_LIBRARY})

# add the install targets
install_clr(TARGETS runcommand DESTINATIONS .)
