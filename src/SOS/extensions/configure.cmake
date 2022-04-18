include(CheckStructHasMember)
include(CheckCXXSymbolExists)

check_struct_has_member ("struct dirent" d_type dirent.h HAVE_DIRENT_D_TYPE)
check_include_files("sys/auxv.h;asm/hwcap.h" HAVE_AUXV_HWCAP_H)

configure_file( ${CMAKE_CURRENT_SOURCE_DIR}/config.h.in ${CMAKE_CURRENT_BINARY_DIR}/config.h)
