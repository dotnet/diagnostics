project(COREPAL)

include_directories(${COREPAL_SOURCE_DIR}/inc)
include_directories(${COREPAL_SOURCE_DIR}/src)

add_compile_options(-fexceptions)
add_definitions(-DUSE_STL)

remove_definitions(-DUNICODE)
remove_definitions(-D_UNICODE)

add_subdirectory(src)
