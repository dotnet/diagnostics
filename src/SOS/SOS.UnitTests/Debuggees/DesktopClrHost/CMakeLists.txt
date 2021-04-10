project(DesktopClrHost)

set(CMAKE_INCLUDE_CURRENT_DIR ON)

include_directories(inc)
include_directories("$ENV{VSInstallDir}/DIA SDK/include")

add_definitions(-DUSE_STL)
add_definitions(-MT) 

set(DESKTOPCLRHOST_SOURCES
    DesktopClrHost.cpp
)
  
set(DESKTOPCLRHOST_LIBRARY
    ${STATIC_MT_CRT_LIB}
    ${STATIC_MT_CPP_LIB}
    ${STATIC_MT_VCRT_LIB}
    kernel32.lib
    user32.lib
    ole32.lib
    oleaut32.lib
    uuid.lib
    version.lib
    advapi32.lib
    psapi.lib
    ntdll.lib
    mscoree.lib
)

add_library_clr(DesktopClrHost SHARED ${DESKTOPCLRHOST_SOURCES})

target_link_libraries(DesktopClrHost ${DESKTOPCLRHOST_LIBRARY})

install(TARGETS DesktopClrHost DESTINATION ${CLR_MANAGED_BINARY_DIR}/WebApp3/${CLR_BUILD_TYPE}/netcoreapp3.1)
install(TARGETS DesktopClrHost DESTINATION ${CLR_MANAGED_BINARY_DIR}/WebApp3/${CLR_BUILD_TYPE}/net5.0)
install(TARGETS DesktopClrHost DESTINATION ${CLR_MANAGED_BINARY_DIR}/WebApp3/${CLR_BUILD_TYPE}/net6.0)
