project(SOS.Extensions)

if(NOT ${NUGET_PACKAGES} STREQUAL "")
    set(DIASYMREADER_ARCH ${CLR_CMAKE_TARGET_ARCH})

    if(NOT(CLR_CMAKE_TARGET_ARCH STREQUAL CLR_CMAKE_HOST_ARCH))
        set(DIASYMREADER_ARCH ${CLR_CMAKE_HOST_ARCH})
    endif()

    if (DIASYMREADER_ARCH STREQUAL x64)
        set(DIASYMREADER_ARCH amd64)
    endif()

    install(FILES ${NUGET_PACKAGES}/microsoft.diasymreader.native/16.11.27-beta1.23180.1/runtimes/win/native/Microsoft.DiaSymReader.Native.${DIASYMREADER_ARCH}.dll DESTINATION . )
endif()

if(NOT ${CLR_MANAGED_BINARY_DIR} STREQUAL "")
    set(MANAGED_BINDIR ${CLR_MANAGED_BINARY_DIR}/SOS.Extensions/${CLR_BUILD_TYPE}/netstandard2.0/publish)
    file(GLOB installfiles ${MANAGED_BINDIR}/*.dll ${MANAGED_BINDIR}/*.pdb)
    install(FILES ${installfiles} DESTINATION . )
endif()
