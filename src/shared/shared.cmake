#-------------------------
# Enable C++ EH with SEH
#-------------------------
if (MSVC)
  set_property(DIRECTORY PROPERTY CLR_EH_OPTION /EHa) # enable C++ EH (w/ SEH exceptions)
endif()

include_directories(${CLR_SRC_NATIVE_DIR})
include_directories(${CLR_SHARED_DIR}/pal/prebuilt/inc)

# All of the compiler options are specified in file compileoptions.cmake
# Do not add any new options here. They should be added in compileoptions.cmake
if(CLR_CMAKE_HOST_WIN32)
  add_compile_options($<$<COMPILE_LANGUAGE:C,CXX>:/Zl>) # omit default library name in .OBJ
endif(CLR_CMAKE_HOST_WIN32)

#--------------------------------
# Definition directives
#  - all clr specific compile definitions should be included in this file
#  - all clr specific feature variable should also be added in this file
#----------------------------------
include(${CLR_SHARED_DIR}/clrdefinitions.cmake)

if (CLR_CMAKE_HOST_UNIX)
  include_directories("${CLR_SHARED_DIR}/pal/inc")
  include_directories("${CLR_SHARED_DIR}/pal/inc/rt")
  include_directories("${CLR_SHARED_DIR}/pal/src/safecrt")
endif (CLR_CMAKE_HOST_UNIX)

include_directories(${CLR_SHARED_DIR}/minipal)
include_directories(${CLR_SHARED_DIR}/debug/inc)
include_directories(${CLR_SHARED_DIR}/debug/inc/dump)
include_directories(${CLR_SHARED_DIR}/hosts/inc)
include_directories(${CLR_SHARED_DIR}/inc)
include_directories(${CLR_SHARED_DIR}/gc)
