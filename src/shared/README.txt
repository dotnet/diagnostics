The "shared" directory contains the common code between the runtime and diagnostics repros.

It is also shared by the dbgshim and SOS components.

Updated last on 2/7/2022 from the runtime repo commit hash: 4c662b16e3e6fe688f9bbff2a9423fdee799ecfe

runtime/src/coreclr/inc -> diagnostics/src/shared/inc
runtime/src/coreclr/debug/dbgutil -> diagnostics/src/shared/dbgutil
runtime/src/coreclr/debug/inc/dbgutil.h -> diagnostics/src/shared/inc/dbgutil.h
runtime/src/coreclr/debug/inc/dbgtargetcontext.h -> diagnostics/src/shared/inc/dbgtargetcontext.h
runtime/src/coreclr/debug/inc/runtimeinfo.h -> diagnostics/src/shared/inc/runtimeinfo.h
runtime/src/coreclr/gcdump -> diagnostics/src/shared/gcdump
runtime/src/coreclr/gcinfo/gcinfodumper.cpp -> diagnostics/src/shared/gcdump/gcinfodumper.cpp
runtime/src/coreclr/vm/gcinfodecoder.cpp -> diagnostics/src/shared/gcdump/gcinfodecoder.cpp
runtime/src/coreclr/gc/gcdesc.h -> diagnostics/src/shared/inc/gcdesc.h
runtime/src/coreclr/vm/hillclimbing.h -> diagnostics/src/shared/inc/hillclimbing.h
runtime/src/coreclr/dlls/mscorrc/resource.h -> diagnostics/src/shared/inc/resource.h
runtime/src/coreclr/hosts/inc/coreclrhost.h -> diagnostics/src/shared/inc/coreclrhost.h
runtime/src/native/minipal -> diagnostics/src/shared/minipal
runtime/src/coreclr/pal -> diagnostics/src/shared/pal
runtime/src/coreclr/palrt -> diagnostics/src/shared/palrt
runtime/src/coreclr/utilcode -> diagnostics/src/shared/utilcode
