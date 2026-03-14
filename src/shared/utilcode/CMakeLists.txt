set(CMAKE_INCLUDE_CURRENT_DIR ON)

set(UTILCODE_COMMON_SOURCES
  clrhost_nodependencies.cpp
  ex.cpp
  sbuffer.cpp
  sstring_com.cpp
  fstring.cpp
  namespaceutil.cpp
  check.cpp
  sstring.cpp
  util_nodependencies.cpp
  safewrap.cpp
  debug.cpp
  pedecoder.cpp
  longfilepathwrappers.cpp
)

set(UTILCODE_STATICNOHOST_SOURCES
  ${UTILCODE_COMMON_SOURCES}
  hostimpl.cpp
)

convert_to_absolute_path(UTILCODE_STATICNOHOST_SOURCES ${UTILCODE_STATICNOHOST_SOURCES})

add_library_clr(utilcodestaticnohost STATIC ${UTILCODE_STATICNOHOST_SOURCES})

target_link_libraries(utilcodestaticnohost PUBLIC coreclrminipal)

if(CLR_CMAKE_HOST_WIN32)
  target_compile_definitions(utilcodestaticnohost PRIVATE _CRTIMP=) # use static version of crt
endif(CLR_CMAKE_HOST_WIN32)

target_compile_definitions(utilcodestaticnohost PRIVATE FEATURE_UTILCODE_NO_DEPENDENCIES)
target_compile_definitions(utilcodestaticnohost PRIVATE SELF_NO_HOST)
target_precompile_headers(utilcodestaticnohost PRIVATE [["stdafx.h"]])
