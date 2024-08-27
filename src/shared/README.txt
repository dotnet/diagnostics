The "shared" directory contains the common code between the runtime and diagnostics repros.

It is also shared by the dbgshim and SOS components.

Updated last on 8/14/2024 from the runtime repo commit hash: 96bcf7150deba280a070c6a4d85ca0640e405278

runtime/src/coreclr/inc                                 -> diagnostics/src/shared/inc
runtime/src/coreclr/debug/dbgutil                       -> diagnostics/src/shared/debug/dbgutil
runtime/src/coreclr/debug/inc/dump/dumpcommon.h         -> diagnostics/src/shared/debug/inc/dump/dumpcommon.h
runtime/src/coreclr/debug/inc/dbgutil.h                 -> diagnostics/src/shared/debug/inc/dbgutil.h
runtime/src/coreclr/debug/inc/dbgtargetcontext.h        -> diagnostics/src/shared/debug/inc/dbgtargetcontext.h
runtime/src/coreclr/debug/inc/runtimeinfo.h             -> diagnostics/src/shared/debug/inc/runtimeinfo.h
runtime/src/coreclr/gcdump                              -> diagnostics/src/shared/gcdump
runtime/src/coreclr/gcinfo/gcinfodumper.cpp             -> diagnostics/src/shared/gcinfo/gcinfodumper.cpp
runtime/src/coreclr/vm/gcinfodecoder.cpp                -> diagnostics/src/shared/vm/gcinfodecoder.cpp
runtime/src/coreclr/gc/gcdesc.h                         -> diagnostics/src/shared/gc/gcdesc.h
runtime/src/coreclr/dlls/mscorrc/resource.h             -> diagnostics/src/shared/dlls/mscorrc/resource.h
runtime/src/coreclr/hosts/inc/coreclrhost.h             -> diagnostics/src/shared/hosts/inc/coreclrhost.h
runtime/src/coreclr/minipal                             -> diagnostics/src/shared/minipal
runtime/src/coreclr/pal                                 -> diagnostics/src/shared/pal
runtime/src/coreclr/palrt                               -> diagnostics/src/shared/palrt
runtime/src/coreclr/utilcode                            -> diagnostics/src/shared/utilcode

runtime/src/native/minipal                              -> diagnostics/src/shared/native/minipal

runtime/src/coreclr/utilcode/sigparser.cpp              -> diagnostics/src/SOS/Strike/sigparser.cpp                - contracts removed
runtime/src/coreclr/gcdump                              -> diagnostics/src/shared/gcdump/gcdump.cpp                - SOS can't include utilcode.h
runtime/src/coreclr/gcdump                              -> diagnostics/src/shared/gcdump/i386/gcdumpx86.cpp        - SOS can't include utilcode.h
runtime/src/coreclr/gcdump                              -> diagnostics/src/shared/palrt/bstr.cpp                   - needed by dbgshim

HAVE_PROCFS_MAPS is needed by diagnostics/src/shared/pal/src/thread/process.cpp:

diagnostics/src/shared/pal/src/configure.cmake
diagnostics/src/shared/pal/src/config.h.in
diagnostics/eng/native/tryrun.cmake

There are a lot of include and source files that need to be carefully merged from the runtime because 
there is functions that the diagnostics repo doesn't need (i.e. Mutexes) and functions that were removed
from the runtime PAL that the diagnostics repo needs (i.e. RemoveDirectoryA).

diagnostics\src\shared\inc\arrayholder.h                - needs #pragma once
diagnostics\src\shared\inc\daccess.h                    - mostly stripped down except for macros the other shared/inc files need, TADDR needs to be ULONG_PTR for x86
diagnostics\src\shared\inc\ex.h                         - stripped some parts of the EX_* macros out
diagnostics\src\shared\inc\holder.h                     - needs ReleaseHolder #ifndef SOS_INCLUDE out
diagnostics\src\shared\inc\predeftlsslot.h              - SOS needs all the old enums for older runtimes

diagnostics\src\shared\utilcode\clrhost_nodependencies.cpp - remove DbgIsExecutable()
diagnostics\src\shared\utilcode\debug.cpp
diagnostics\src\shared\utilcode\ex.cpp
diagnostics\src\shared\utilcode\util_nodependencies.cpp - gone except OutputDebugStringUtf8()
diagnostics\src\shared\utilcode\hostimpl.cpp            - remove ClrSleepEx()
diagnostics\src\shared\utilcode\pedecoder.cpp           - remove ForceRelocForDLL()

diagnostics\src\shared\pal\src\debug\debug.cpp          - careful merging
diagnostics\src\shared\pal\src\file\directory.cpp       - add RemoveDirectoryA for SOS
diagnostics\src\shared\pal\src\handlemgr\handleapi.cpp

diagnostics\src\shared\pal\src\safecrt\vsprintf.cpp     - vsprintf/vsnprintf needed for SOS %S
diagnostics\src\shared\pal\src\safecrt\mbusafecrt.cpp   - need _safecrt_cfltcvt for vsprintf/vsnprint support
diagnostics\src\shared\pal\src\safecrt\mbusafecrt_internal.h

For swprintf/vswprintf support needed by SOS (files not part of the runtime anymore):

diagnostics/src/shared/pal/src/safecrt/output.inl
diagnostics/src/shared/pal/src/safecrt/safecrt_output_l.cpp
diagnostics/src/shared/pal/src/safecrt/safecrt_output_s.cpp
diagnostics/src/shared/pal/src/safecrt/safecrt_woutput_s.cpp
diagnostics/src/shared/pal/src/safecrt/swprintf.cpp
diagnostics/src/shared/pal/src/safecrt/vswprint.cpp
diagnostics/src/shared/pal/src/safecrt/xtoa_s.cpp
diagnostics/src/shared/pal/src/safecrt/xtow_s.cpp
diagnostics/src/shared/pal/src/safecrt/xtox_s.inl
