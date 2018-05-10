# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

function(clr_unknown_arch)
    if (WIN32)
        message(FATAL_ERROR "Only AMD64, ARM64, ARM and I386 are supported")
    elseif(CLR_CROSS_COMPONENTS_BUILD)
        message(FATAL_ERROR "Only AMD64, I386 host are supported for linux cross-architecture component")
    else()
        message(FATAL_ERROR "Only AMD64, ARM64 and ARM are supported")
    endif()
endfunction()

function(strip_symbols targetName outputFilename)
  if (CLR_CMAKE_PLATFORM_UNIX)
    if (STRIP_SYMBOLS)

      # On the older version of cmake (2.8.12) used on Ubuntu 14.04 the TARGET_FILE
      # generator expression doesn't work correctly returning the wrong path and on
      # the newer cmake versions the LOCATION property isn't supported anymore.
      if (CMAKE_VERSION VERSION_EQUAL 3.0 OR CMAKE_VERSION VERSION_GREATER 3.0)
          set(strip_source_file $<TARGET_FILE:${targetName}>)
      else()
          get_property(strip_source_file TARGET ${targetName} PROPERTY LOCATION)
      endif()

      if (CMAKE_SYSTEM_NAME STREQUAL Darwin)
        set(strip_destination_file ${strip_source_file}.dwarf)

        add_custom_command(
          TARGET ${targetName}
          POST_BUILD
          VERBATIM
          COMMAND ${DSYMUTIL} --flat --minimize ${strip_source_file}
          COMMAND ${STRIP} -S ${strip_source_file}
          COMMENT Stripping symbols from ${strip_source_file} into file ${strip_destination_file}
        )
      else (CMAKE_SYSTEM_NAME STREQUAL Darwin)
        set(strip_destination_file ${strip_source_file}.dbg)

        add_custom_command(
          TARGET ${targetName}
          POST_BUILD
          VERBATIM
          COMMAND ${OBJCOPY} --only-keep-debug ${strip_source_file} ${strip_destination_file}
          COMMAND ${OBJCOPY} --strip-debug ${strip_source_file}
          COMMAND ${OBJCOPY} --add-gnu-debuglink=${strip_destination_file} ${strip_source_file}
          COMMENT Stripping symbols from ${strip_source_file} into file ${strip_destination_file}
        )
      endif (CMAKE_SYSTEM_NAME STREQUAL Darwin)

      set(${outputFilename} ${strip_destination_file} PARENT_SCOPE)
    endif (STRIP_SYMBOLS)
  endif(CLR_CMAKE_PLATFORM_UNIX)
endfunction()

function(install_clr targetName)
  list(FIND CLR_CROSS_COMPONENTS_LIST ${targetName} INDEX)
  if (NOT DEFINED CLR_CROSS_COMPONENTS_LIST OR NOT ${INDEX} EQUAL -1)
    strip_symbols(${targetName} strip_destination_file)
    # On the older version of cmake (2.8.12) used on Ubuntu 14.04 the TARGET_FILE
    # generator expression doesn't work correctly returning the wrong path and on
    # the newer cmake versions the LOCATION property isn't supported anymore.
    if(CMAKE_VERSION VERSION_EQUAL 3.0 OR CMAKE_VERSION VERSION_GREATER 3.0)
       set(install_source_file $<TARGET_FILE:${targetName}>)
    else()
        get_property(install_source_file TARGET ${targetName} PROPERTY LOCATION)
    endif()

    install(PROGRAMS ${install_source_file} DESTINATION .)
    if(WIN32)
        install(FILES ${CMAKE_CURRENT_BINARY_DIR}/$<CONFIG>/${targetName}.pdb DESTINATION PDB)
    else()
        install(FILES ${strip_destination_file} DESTINATION .)
    endif()
    if(CLR_CMAKE_PGO_INSTRUMENT)
        if(WIN32)
            install(FILES ${CMAKE_CURRENT_BINARY_DIR}/$<CONFIG>/${targetName}.pgd DESTINATION PGD OPTIONAL)
        endif()
    endif()
  endif()
endfunction()

function(_add_library)
    if(NOT WIN32)
      add_library(${ARGV} ${VERSION_FILE_PATH})
    else()
      add_library(${ARGV})
    endif(NOT WIN32)
    list(FIND CLR_CROSS_COMPONENTS_LIST ${ARGV0} INDEX)
    if (DEFINED CLR_CROSS_COMPONENTS_LIST AND ${INDEX} EQUAL -1)
     set_target_properties(${ARGV0} PROPERTIES EXCLUDE_FROM_ALL 1)
    endif()
endfunction()

function(add_library_clr)
    _add_library(${ARGV})
endfunction()
