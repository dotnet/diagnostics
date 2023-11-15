project(sos)

set(CMAKE_INCLUDE_CURRENT_DIR ON)

# Set the RPATH of sos so that it can find dependencies without needing to set LD_LIBRARY_PATH
# For more information: http://www.cmake.org/Wiki/CMake_RPATH_handling.
if (CORECLR_SET_RPATH)
  set(CMAKE_BUILD_WITH_INSTALL_RPATH TRUE)
  if(CLR_CMAKE_HOST_OSX)
    set(CMAKE_INSTALL_RPATH "@loader_path")
  else()
    set(CMAKE_INSTALL_RPATH "\$ORIGIN")
  endif(CLR_CMAKE_HOST_OSX)
endif (CORECLR_SET_RPATH)

if(CLR_CMAKE_TARGET_ARCH_AMD64)
    message(STATUS "CLR_CMAKE_TARGET_ARCH_AMD64")
endif()
if(CLR_CMAKE_TARGET_ARCH_I386)
    message(STATUS "CLR_CMAKE_TARGET_ARCH_I386")
endif()
if(CLR_CMAKE_TARGET_ARCH_ARM)
    message(STATUS "CLR_CMAKE_TARGET_ARCH_ARM")
endif()
if(CLR_CMAKE_TARGET_ARCH_ARM64)
    message(STATUS "CLR_CMAKE_TARGET_ARCH_ARM64")
endif()

if(CLR_CMAKE_HOST_ARCH_AMD64)
  message(STATUS "CLR_CMAKE_HOST_ARCH_AMD64")
  add_definitions(-DSOS_TARGET_AMD64=1)
  add_definitions(-D_TARGET_WIN64_=1)
  add_definitions(-DDBG_TARGET_64BIT)
  add_definitions(-DDBG_TARGET_WIN64=1)
  if (CLR_CMAKE_HOST_WIN32)
    add_definitions(-DSOS_TARGET_ARM64=1)
  endif(CLR_CMAKE_HOST_WIN32)
  remove_definitions(-D_TARGET_ARM64_=1)
  add_definitions(-D_TARGET_AMD64_)
  add_definitions(-DDBG_TARGET_AMD64)
elseif(CLR_CMAKE_HOST_ARCH_I386)
  message(STATUS "CLR_CMAKE_HOST_ARCH_I386")
  add_definitions(-DSOS_TARGET_X86=1)
  add_definitions(-D_TARGET_X86_=1)
  add_definitions(-DTARGET_X86)
  add_definitions(-DDBG_TARGET_32BIT)
  if (CLR_CMAKE_HOST_WIN32)
    add_definitions(-DSOS_TARGET_ARM=1)
  endif(CLR_CMAKE_HOST_WIN32)
elseif(CLR_CMAKE_HOST_ARCH_ARM)
  message(STATUS "CLR_CMAKE_HOST_ARCH_ARM")
  add_definitions(-DSOS_TARGET_ARM=1)
  add_definitions(-D_TARGET_WIN32_=1)
  add_definitions(-D_TARGET_ARM_=1)
  add_definitions(-DDBG_TARGET_32BIT)
elseif(CLR_CMAKE_HOST_ARCH_ARM64)
  message(STATUS "CLR_CMAKE_HOST_ARCH_ARM64")
  add_definitions(-DSOS_TARGET_ARM64=1)
  add_definitions(-D_TARGET_WIN64_=1)
  add_definitions(-DDBG_TARGET_64BIT)
  add_definitions(-DDBG_TARGET_WIN64=1)
elseif(CLR_CMAKE_HOST_ARCH_MIPS64)
  add_definitions(-DSOS_TARGET_MIPS64=1)
  add_definitions(-D_TARGET_WIN64_=1)
  add_definitions(-DDBG_TARGET_64BIT)
  add_definitions(-DDBG_TARGET_WIN64=1)
elseif(CLR_CMAKE_HOST_ARCH_RISCV64)
  add_definitions(-DSOS_TARGET_RISCV64=1)
  add_definitions(-D_TARGET_WIN64_=1)
  add_definitions(-DDBG_TARGET_64BIT)
  add_definitions(-DDBG_TARGET_WIN64=1)
endif()

add_definitions(-DSTRIKE)  

include_directories(${ROOT_DIR}/src/SOS/inc)
include_directories(${ROOT_DIR}/src/SOS/extensions)
include_directories(${CLR_SHARED_DIR}/gcdump)
include_directories(platform)

if (CLR_CMAKE_HOST_WIN32)
  add_definitions(-DUSE_STL)

  set(SOS_SOURCES
    disasm.cpp
    dllsext.cpp
    eeheap.cpp
    ExpressionNode.cpp
    dbgengservices.cpp
    exts.cpp
    gchist.cpp
    gcroot.cpp
    symbols.cpp
    managedcommands.cpp
    metadata.cpp
    sigparser.cpp
    sildasm.cpp
    sos.cpp
    stressLogDump.cpp
    strike.cpp
    util.cpp
    vm.cpp
    WatchCmd.cpp
    Native.rc
    platform/datatarget.cpp
    platform/hostimpl.cpp
    platform/targetimpl.cpp
    platform/runtimeimpl.cpp
  )
  
  add_definitions(-DFX_VER_INTERNALNAME_STR=SOS.dll)

  #Preprocess exports definition file
  preprocess_file(${CMAKE_CURRENT_SOURCE_DIR}/sos.def ${CMAKE_CURRENT_BINARY_DIR}/sos.def)
  list(APPEND SOS_SOURCES ${CMAKE_CURRENT_BINARY_DIR}/sos.def)

  set(SOS_LIBRARY
    extensions
    corguids
    dbgutil
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

  if(NOT CLR_CMAKE_HOST_ARCH_ARM64 AND NOT CLR_CMAKE_HOST_ARCH_ARM)
    list(APPEND 
      SOS_LIBRARY 
        mscoree.lib)
  endif(NOT CLR_CMAKE_HOST_ARCH_ARM64 AND NOT CLR_CMAKE_HOST_ARCH_ARM)

else(CLR_CMAKE_HOST_WIN32)
  add_definitions(-DFEATURE_ENABLE_HARDWARE_EXCEPTIONS)
  add_definitions(-DPAL_STDCPP_COMPAT=1)
  add_compile_options(-Wno-null-arithmetic)
  add_compile_options(-Wno-format)

  include_directories(BEFORE xplat)

  set(SOS_SOURCES
    disasm.cpp
    eeheap.cpp
    exts.cpp
    gchist.cpp
    gcroot.cpp
    symbols.cpp
    metadata.cpp
    sigparser.cpp
    sildasm.cpp
    stressLogDump.cpp
    strike.cpp
    sos.cpp
    util.cpp
    platform/datatarget.cpp
    platform/hostimpl.cpp
    platform/targetimpl.cpp
    platform/runtimeimpl.cpp
  )

  set(DEF_SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/sos_unixexports.src)
  set(EXPORTS_FILE ${CMAKE_CURRENT_BINARY_DIR}/sos.exports)
  generate_exports_file(${DEF_SOURCES} ${EXPORTS_FILE})

  if(CMAKE_SYSTEM_NAME STREQUAL Linux OR CMAKE_SYSTEM_NAME STREQUAL FreeBSD OR CMAKE_SYSTEM_NAME STREQUAL NetBSD)
      set(CMAKE_SHARED_LINKER_FLAGS "${CMAKE_SHARED_LINKER_FLAGS} -Xlinker -Bsymbolic -Xlinker -Bsymbolic-functions")
      set(EXPORTS_LINKER_OPTION -Wl,--version-script=${EXPORTS_FILE})
  endif(CMAKE_SYSTEM_NAME STREQUAL Linux OR CMAKE_SYSTEM_NAME STREQUAL FreeBSD OR CMAKE_SYSTEM_NAME STREQUAL NetBSD)

  if(CMAKE_SYSTEM_NAME STREQUAL Darwin)
      set(EXPORTS_LINKER_OPTION -Wl,-exported_symbols_list,${EXPORTS_FILE})
  endif(CMAKE_SYSTEM_NAME STREQUAL Darwin)

  set(SOS_LIBRARY
    extensions
    corguids
    dbgutil
    palrt
    coreclrpal
  )

endif(CLR_CMAKE_HOST_WIN32)

if(CLR_CMAKE_HOST_ARCH_AMD64)
  set(SOS_SOURCES_ARCH
    disasmX86.cpp
  )
  if (CLR_CMAKE_HOST_WIN32)
    list(APPEND 
      SOS_SOURCES_ARCH  
      disasmARM64.cpp
    )
  endif(CLR_CMAKE_HOST_WIN32)
elseif(CLR_CMAKE_HOST_ARCH_I386)
  set(SOS_SOURCES_ARCH 
    disasmX86.cpp
  )
  if (CLR_CMAKE_HOST_WIN32)
    list(APPEND
      SOS_SOURCES_ARCH
      disasmARM.cpp
    )
  endif(CLR_CMAKE_HOST_WIN32)
elseif(CLR_CMAKE_HOST_ARCH_ARM)
  set(SOS_SOURCES_ARCH
    disasmARM.cpp
  )
elseif(CLR_CMAKE_HOST_ARCH_ARM64)
  set(SOS_SOURCES_ARCH
    disasmARM64.cpp
  )
elseif(CLR_CMAKE_HOST_ARCH_RISCV64)
  set(SOS_SOURCES_ARCH
    disasmRISCV64.cpp
  )
endif()

list(APPEND SOS_SOURCES ${SOS_SOURCES_ARCH})

if(CLR_CMAKE_HOST_LINUX OR CLR_CMAKE_HOST_FREEBSD OR CLR_CMAKE_HOST_NETBSD OR CLR_CMAKE_HOST_SUNOS)
    # Add linker exports file option
    set(EXPORTS_LINKER_OPTION -Wl,--version-script=${EXPORTS_FILE})
endif(CLR_CMAKE_HOST_LINUX OR CLR_CMAKE_HOST_FREEBSD OR CLR_CMAKE_HOST_NETBSD OR CLR_CMAKE_HOST_SUNOS)

if(CLR_CMAKE_HOST_OSX)
    # Add linker exports file option
    set(EXPORTS_LINKER_OPTION -Wl,-exported_symbols_list,${EXPORTS_FILE})
endif(CLR_CMAKE_HOST_OSX)

add_library_clr(sos SHARED ${SOS_SOURCES})

if(CLR_CMAKE_HOST_UNIX)
    add_custom_target(sos_exports DEPENDS ${EXPORTS_FILE})
    add_dependencies(sos sos_exports)

    set_property(TARGET sos APPEND_STRING PROPERTY LINK_FLAGS ${EXPORTS_LINKER_OPTION})
    set_property(TARGET sos APPEND_STRING PROPERTY LINK_DEPENDS ${EXPORTS_FILE})
endif(CLR_CMAKE_HOST_UNIX)

target_link_libraries(sos ${SOS_LIBRARY})

# add the install targets
install_clr(TARGETS sos DESTINATIONS .)

if(NOT CLR_CMAKE_HOST_WIN32)
  install(FILES sosdocsunix.txt DESTINATION .)
endif(NOT CLR_CMAKE_HOST_WIN32)
