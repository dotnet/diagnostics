// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PROCESS);
#include "pal/palinternal.h"
#include "processmodules.h"
#include <debugmacrosext.h>

#include <algorithm>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <sys/stat.h>
#include <unistd.h>

#ifdef __APPLE__
#include <libproc.h>
#include <sys/posix_sem.h>
#endif

using namespace CorUnix;

/*++
Function:
  CreateProcessModules

Abstract
  Returns a process's module list

Return
  ProcessModules * list

--*/
ProcessModules *
CreateProcessModules(
    IN DWORD dwProcessId,
    OUT LPDWORD lpCount)
{
    ProcessModules *listHead = NULL;
    _ASSERTE(lpCount != NULL);

#if defined(__APPLE__)

    // For OS X, the "vmmap" command outputs something similar to the /proc/*/maps file so popen the
    // command and read the relevant lines:
    //
    // ...
    // ==== regions for process 347  (non-writable and writable regions are interleaved)
    // REGION TYPE                      START - END             [ VSIZE] PRT/MAX SHRMOD  REGION DETAIL
    // __TEXT                 000000010446d000-0000000104475000 [   32K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // __DATA                 0000000104475000-0000000104476000 [    4K] rw-/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // __LINKEDIT             0000000104476000-000000010447a000 [   16K] r--/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // Kernel Alloc Once      000000010447a000-000000010447b000 [    4K] rw-/rwx SM=PRV
    // MALLOC (admin)         000000010447b000-000000010447c000 [    4K] r--/rwx SM=ZER
    // ...
    // MALLOC (admin)         00000001044ab000-00000001044ac000 [    4K] r--/rwx SM=PRV
    // __TEXT                 00000001044ac000-0000000104c84000 [ 8032K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 0000000104c84000-0000000104c85000 [    4K] rwx/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 0000000104c85000-000000010513b000 [ 4824K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 000000010513b000-000000010513c000 [    4K] rwx/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 000000010513c000-000000010516f000 [  204K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __DATA                 000000010516f000-00000001051ce000 [  380K] rw-/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __DATA                 00000001051ce000-00000001051fa000 [  176K] rw-/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __LINKEDIT             00000001051fa000-0000000105bac000 [ 9928K] r--/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // VM_ALLOCATE            0000000105bac000-0000000105bad000 [    4K] r--/rw- SM=SHM
    // MALLOC (admin)         0000000105bad000-0000000105bae000 [    4K] r--/rwx SM=ZER
    // MALLOC                 0000000105bae000-0000000105baf000 [    4K] rw-/rwx SM=ZER

    // OS X Sierra (10.12.4 Beta)
    // REGION TYPE                      START - END             [ VSIZE  RSDNT  DIRTY   SWAP] PRT/MAX SHRMOD PURGE    REGION DETAIL
    // Stack                  00007fff5a930000-00007fff5b130000 [ 8192K    32K    32K     0K] rw-/rwx SM=PRV          thread 0
    // __TEXT                 00007fffa4a0b000-00007fffa4a0d000 [    8K     8K     0K     0K] r-x/r-x SM=COW          /usr/lib/libSystem.B.dylib
    // __TEXT                 00007fffa4bbe000-00007fffa4c15000 [  348K   348K     0K     0K] r-x/r-x SM=COW          /usr/lib/libc++.1.dylib

    // NOTE: the module path can have spaces in the name
    // __TEXT                 0000000196220000-00000001965b4000 [ 3664K  2340K     0K     0K] r-x/rwx SM=COW          /Volumes/Builds/builds/devmain/rawproduct/debug/build/out/Applications/Microsoft Excel.app/Contents/SharedSupport/PowerQuery/libcoreclr.dylib

    // NOTE: Sometimes vmmap hides full paths to some process modules (.dylibs in non-system folders), causing debugger not to work.
    // __TEXT                 000000010d8bd000-000000010ddce000 [ 5188K  5188K     0K     0K] r-x/rwx SM=COW          /Users/USER/*/libcoreclr.dylib
    // So now we get modules information by iterating over regions using proc_pidinfo().  See dotnet/runtime#42888.
    int count = 0;

    uint64_t addr = 0;
    while (true)
    {
        struct proc_regionwithpathinfo rwpi;
        int sz = proc_pidinfo(dwProcessId, PROC_PIDREGIONPATHINFO, addr, &rwpi, sizeof rwpi);
        if (sz != sizeof rwpi)
        {
            if (sz == 0 && errno == EINVAL)
                break; // ok

            DestroyProcessModules(listHead);
            listHead = NULL;
            count = 0;
            break; // unknown error
        }

        const char *moduleName = rwpi.prp_vip.vip_path;

        bool dup = false;
        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            if (strcmp(moduleName, entry->_name) == 0)
            {
                dup = true;
                break;
            }
        }

        // Does the offset in the module correspond to a valid MachO header?
        bool mightBeMachOHeader = rwpi.prp_prinfo.pri_offset == 0;

        if (!dup && mightBeMachOHeader)
        {
            int cbModuleName = strlen(moduleName) + 1;
            ProcessModules *entry = (ProcessModules *)malloc(sizeof(ProcessModules) + cbModuleName);
            if (entry == NULL)
            {
                DestroyProcessModules(listHead);
                listHead = NULL;
                count = 0;
                break; // no memory
            }
            memcpy_s(entry->_name, cbModuleName, moduleName, cbModuleName);
            entry->_minimumAddress = 0;
            entry->_baseAddress = (void *)rwpi.prp_prinfo.pri_address;
            entry->_next = listHead;
            listHead = entry;
            count++;
        }

        addr = rwpi.prp_prinfo.pri_address + rwpi.prp_prinfo.pri_size;
    }

    *lpCount = count;

#elif HAVE_PROCFS_MAPS

    // Here we read /proc/<pid>/maps file in order to parse it and figure out what it says
    // about a library we are looking for. This file looks something like this:
    //
    // [address]      [perms] [offset] [dev] [inode]     [pathname] - HEADER is not preset in an actual file
    //
    // 35b1800000-35b1820000 r-xp 00000000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a1f000-35b1a20000 r--p 0001f000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a20000-35b1a21000 rw-p 00020000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a21000-35b1a22000 rw-p 00000000 00:00 0       [heap]
    // 35b1c00000-35b1dac000 r-xp 00000000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1dac000-35b1fac000 ---p 001ac000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1fac000-35b1fb0000 r--p 001ac000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1fb0000-35b1fb2000 rw-p 001b0000 08:02 135870  /usr/lib64/libc-2.15.so

    // Making something like: /proc/123/maps
    char mapFileName[100];
    char *line = NULL;
    size_t lineLen = 0;
    int count = 0;
    ssize_t read;

    INDEBUG(int chars = )
    snprintf(mapFileName, sizeof(mapFileName), "/proc/%d/maps", dwProcessId);
    _ASSERTE(chars > 0 && chars <= (int)sizeof(mapFileName));

    FILE *mapsFile = fopen(mapFileName, "r");
    if (mapsFile == NULL)
    {
        goto exit;
    }

    // Reading maps file line by line
    while ((read = getline(&line, &lineLen, mapsFile)) != -1)
    {
        void *startAddress, *endAddress, *offset;
        int devHi, devLo, inode;
        char moduleName[PATH_MAX];

        if (sscanf_s(line, "%p-%p %*[-rwxsp] %p %x:%x %d %[^\n]\n", &startAddress, &endAddress, &offset, &devHi, &devLo, &inode, moduleName, ARRAY_SIZE(moduleName)) == 7)
        {
            if (inode != 0)
            {
                bool dup = false;
                for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
                {
                    if (strcmp(moduleName, entry->GetName()) == 0)
                    {
                        if (entry->_baseAddress == 0 && offset == 0)
                        {
                            entry->_baseAddress = startAddress;
                        }
                        entry->_minimumAddress = std::min(startAddress, entry->_minimumAddress);
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                {
                    int cbModuleName = strlen(moduleName) + 1;
                    ProcessModules *entry = (ProcessModules *)malloc(sizeof(ProcessModules) + cbModuleName);
                    if (entry == NULL)
                    {
                        DestroyProcessModules(listHead);
                        listHead = NULL;
                        count = 0;
                        break;
                    }
                    strcpy_s(entry->_name, cbModuleName, moduleName);
                    entry->_baseAddress = 0;
                    entry->_minimumAddress = startAddress;
                    if (offset == 0)
                    {
                        entry->_baseAddress = startAddress;
                    }
                    entry->_next = listHead;
                    listHead = entry;
                    count++;
                }
            }
        }
    }

    *lpCount = count;

    free(line); // We didn't allocate line, but as per contract of getline we should free it
    fclose(mapsFile);
exit:

#else
    _ASSERTE(!"Not implemented on this platform");
#endif
    return listHead;
}

/*++
Function:
    DestroyProcessModules

Abstract
  Cleans up the process module table.

Return
  None

--*/
VOID
DestroyProcessModules(IN ProcessModules *listHead)
{
    for (ProcessModules *entry = listHead; entry != NULL; )
    {
        ProcessModules *next = entry->GetNext();
        free(entry);
        entry = next;
    }
}
