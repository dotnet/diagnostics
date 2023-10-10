include(${CLR_SHARED_DIR}/shared.cmake)

if(CLR_CMAKE_HOST_UNIX)
  add_subdirectory(lldbplugin)
endif(CLR_CMAKE_HOST_UNIX)

# lldbplugin doesn't build with these options
if(CLR_CMAKE_HOST_WIN32)
  message(STATUS "CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION: ${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}")

  message(STATUS "VSInstallDir: $ENV{VSInstallDir}")
  include_directories("$ENV{VSInstallDir}/DIA SDK/include")

  add_compile_options(/Zl) # omit default library name in .OBJ

  add_subdirectory(runcommand)
  if(NOT CLR_CMAKE_TARGET_ARCH_ARM64 AND NOT CLR_CMAKE_TARGET_ARCH_ARM)
    add_subdirectory(SOS.UnitTests/Debuggees/DesktopClrHost)
  endif(NOT CLR_CMAKE_TARGET_ARCH_ARM64 AND NOT CLR_CMAKE_TARGET_ARCH_ARM)
endif(CLR_CMAKE_HOST_WIN32)

add_compile_definitions(STRESS_LOG_ANALYZER)
add_compile_definitions(DACCESS_COMPILE)
add_compile_definitions(SOS_INCLUDE)
add_compile_definitions(DISABLE_CONTRACTS)

add_subdirectory(extensions)
add_subdirectory(SOS.Extensions)
add_subdirectory(Strike)
