set(CMAKE_INCLUDE_CURRENT_DIR ON)

add_definitions(-D_BLD_CLR)

set(UTILCODE_COMMON_SOURCES
  clrhost_nodependencies.cpp
  ex.cpp
  sbuffer.cpp
  sstring_com.cpp
  fstring.cpp
  namespaceutil.cpp
  check.cpp
  sstring.cpp
  safewrap.cpp
  debug.cpp
  pedecoder.cpp
  longfilepathwrappers.cpp
)

# These source file do not yet compile on Linux.
# They should be moved out from here into the declaration
# of UTILCODE_SOURCES above after fixing compiler errors.
if(CLR_CMAKE_TARGET_WIN32)
  list(APPEND UTILCODE_COMMON_SOURCES
    dlwrap.cpp
    securitywrapper.cpp
    securityutil.cpp
  )
endif(CLR_CMAKE_TARGET_WIN32)

set(UTILCODE_STATICNOHOST_SOURCES
  ${UTILCODE_COMMON_SOURCES}
  hostimpl.cpp
)

convert_to_absolute_path(UTILCODE_STATICNOHOST_SOURCES ${UTILCODE_STATICNOHOST_SOURCES})

add_library_clr(utilcodestaticnohost STATIC ${UTILCODE_STATICNOHOST_SOURCES})

if(CLR_CMAKE_HOST_WIN32)
  target_compile_definitions(utilcodestaticnohost PRIVATE _CRTIMP=) # use static version of crt
endif(CLR_CMAKE_HOST_WIN32)

target_compile_definitions(utilcodestaticnohost PRIVATE FEATURE_UTILCODE_NO_DEPENDENCIES)
target_compile_definitions(utilcodestaticnohost PRIVATE SELF_NO_HOST)
target_precompile_headers(utilcodestaticnohost PRIVATE [["stdafx.h"]])
