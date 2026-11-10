project(soscohost)

add_definitions(-DUSE_STL)
include_directories(${CLR_SHARED_DIR}/hosts/inc)

add_library_clr(soscohost SHARED soscohost.cpp soscohost.def)
target_link_libraries(soscohost ${STATIC_MT_CRT_LIB} ${STATIC_MT_CPP_LIB} ${STATIC_MT_VCRT_LIB} dbgeng.lib)
install_clr(TARGETS soscohost DESTINATIONS .)
