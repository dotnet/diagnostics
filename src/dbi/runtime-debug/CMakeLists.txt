add_compile_definitions($<${FEATURE_CORECLR_CACHED_INTERFACE_DISPATCH}:FEATURE_CACHED_INTERFACE_DISPATCH>)
add_compile_definitions($<${FEATURE_CORECLR_VIRTUAL_STUB_DISPATCH}:FEATURE_VIRTUAL_STUB_DISPATCH>)

include_directories(${RUNTIME_DIR})

add_subdirectory(${CLR_DIR}/debug/di di)
if(FEATURE_INPROC_CRASHREPORT AND NOT CLR_CROSS_COMPONENTS_BUILD)
  add_subdirectory(${CLR_DIR}/debug/crashreport crashreport)
endif()
