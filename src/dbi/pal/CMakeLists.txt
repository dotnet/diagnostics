set(DBI_COREPAL_SOURCE_DIR ${COREPAL_SOURCE_DIR})
project(COREPAL)

include(${CLR_DIR}/clrfeatures.cmake)

include_directories(${RUNTIME_DIR})
include_directories(${DBI_COREPAL_SOURCE_DIR}/inc)
include_directories(${DBI_COREPAL_SOURCE_DIR}/src)
include_directories(${DBI_COREPAL_SOURCE_DIR}/../inc)

set(FEATURE_EVENT_TRACE OFF)
add_subdirectory(${DBI_COREPAL_SOURCE_DIR}/src src)
