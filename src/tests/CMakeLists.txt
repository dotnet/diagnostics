include(${CLR_SHARED_DIR}/shared.cmake)

if(CLR_CMAKE_HOST_WIN32)
  # test helpers used by SOS tests
  add_subdirectory(runcommand)
  if(NOT CLR_CMAKE_TARGET_ARCH_ARM)
    add_subdirectory(DesktopClrHost)
  endif(NOT CLR_CMAKE_TARGET_ARCH_ARM)
endif(CLR_CMAKE_HOST_WIN32)
