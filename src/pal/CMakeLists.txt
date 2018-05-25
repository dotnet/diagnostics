cmake_minimum_required(VERSION 2.8.12.2)

project(COREPAL)

include_directories(${COREPAL_SOURCE_DIR}/inc)
include_directories(${COREPAL_SOURCE_DIR}/src)

add_compile_options(-fexceptions)
add_definitions(-DUSE_STL)

add_subdirectory(src)
