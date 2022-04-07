if(CLR_CMAKE_HOST_UNIX)
  add_subdirectory(lldbplugin)
endif(CLR_CMAKE_HOST_UNIX)

# lldbplugin doesn't build with these options
if(CLR_CMAKE_HOST_WIN32)
  message(STATUS "CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION: ${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}")

  message(STATUS "VSInstallDir: $ENV{VSInstallDir}")
  include_directories("$ENV{VSInstallDir}/DIA SDK/include")

  add_compile_options(/FIWarningControl.h) # force include of WarningControl.h
  add_compile_options(/Zl) # omit default library name in .OBJ

  add_subdirectory(runcommand)
  if(NOT CLR_CMAKE_TARGET_ARCH_ARM64)
    add_subdirectory(SOS.UnitTests/Debuggees/DesktopClrHost)
  endif()
endif(CLR_CMAKE_HOST_WIN32)

add_definitions(-D_SECURE_SCL=0)

add_compile_definitions(STRESS_LOG_ANALYZER)
add_compile_definitions(DACCESS_COMPILE)

if (NOT CLR_CMAKE_TARGET_ARCH_I386 OR NOT CLR_CMAKE_TARGET_WIN32)
  add_compile_definitions($<$<NOT:$<BOOL:$<TARGET_PROPERTY:IGNORE_DEFAULT_TARGET_ARCH>>>:FEATURE_EH_FUNCLETS>)
endif (NOT CLR_CMAKE_TARGET_ARCH_I386 OR NOT CLR_CMAKE_TARGET_WIN32)

add_subdirectory(extensions)
add_subdirectory(SOS.Extensions)
add_subdirectory(Strike)
