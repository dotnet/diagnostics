project(SOS.NETCore)

if(NOT ${CLR_MANAGED_BINARY_DIR} STREQUAL "")
    set(MANAGED_BINDIR ${CLR_MANAGED_BINARY_DIR}/SOS.NETCore/${CLR_BUILD_TYPE}/netstandard2.0/publish)

    install(FILES ${MANAGED_BINDIR}/SOS.NETCore.dll DESTINATION . )
    install(FILES ${MANAGED_BINDIR}/SOS.NETCore.pdb DESTINATION . )
    install(FILES ${MANAGED_BINDIR}/Microsoft.FileFormats.dll DESTINATION . )
    install(FILES ${MANAGED_BINDIR}/Microsoft.SymbolStore.dll DESTINATION . )
    install(FILES ${MANAGED_BINDIR}/System.Reflection.Metadata.dll DESTINATION . )
    install(FILES ${MANAGED_BINDIR}/System.Collections.Immutable.dll DESTINATION . )
endif()
