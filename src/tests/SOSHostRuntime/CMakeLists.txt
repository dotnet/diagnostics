project(SOSHostRuntime)

add_library_clr(SOSHostRuntime SHARED hostruntime.cpp)
install_clr(TARGETS SOSHostRuntime DESTINATIONS .)
