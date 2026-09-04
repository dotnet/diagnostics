project(sos-lldb)

add_executable_clr(sos-lldb driver.cpp)
target_link_libraries(sos-lldb sos_lldb_dependencies)

install_clr(TARGETS sos-lldb DESTINATIONS .)
