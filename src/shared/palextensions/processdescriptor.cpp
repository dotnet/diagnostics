// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PROCESS);
#include "pal/palinternal.h"
#include "processdescriptor.h"
#include <debugmacrosext.h>

#include <errno.h>
#include <stdio.h>
#include <sys/time.h>
#if defined(__APPLE__) || defined(__FreeBSD__) || defined(__NetBSD__)
#include <sys/sysctl.h>
#endif
#ifdef __NetBSD__
#include <sys/param.h>
#include <kvm.h>
#endif
#ifdef __FreeBSD__
#include <sys/user.h>
#endif

/*++
 Function:
  GetProcessIdDisambiguationKey

  Get a numeric value that can be used to disambiguate between processes with the same PID,
  provided that one of them is still running. The numeric value can mean different things
  on different platforms, so it should not be used for any other purpose. Under the hood,
  it is implemented based on the creation time of the process.
--*/
BOOL
GetProcessIdDisambiguationKey(DWORD processId, UINT64 *disambiguationKey)
{
    if (disambiguationKey == nullptr)
    {
        _ASSERTE(!"disambiguationKey argument cannot be null!");
        return FALSE;
    }

    *disambiguationKey = 0;

#if defined(__APPLE__) || defined(__FreeBSD__)

    // On OS X, we return the process start time expressed in Unix time (the number of seconds
    // since the start of the Unix epoch).
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, processId };
    int ret = ::sysctl(mib, sizeof(mib)/sizeof(*mib), &info, &size, nullptr, 0);

    if (ret == 0)
    {
#if defined(__APPLE__)
        timeval procStartTime = info.kp_proc.p_starttime;
#else // __FreeBSD__
        timeval procStartTime = info.ki_start;
#endif
        long secondsSinceEpoch = procStartTime.tv_sec;

        *disambiguationKey = secondsSinceEpoch;
        return TRUE;
    }
    else
    {
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

#elif defined(__NetBSD__)

    // On NetBSD, we return the process start time expressed in Unix time (the number of seconds
    // since the start of the Unix epoch).
    kvm_t *kd;
    int cnt;
    struct kinfo_proc2 *info;

    kd = kvm_open(nullptr, nullptr, nullptr, KVM_NO_FILES, "kvm_open");
    if (kd == nullptr)
    {
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

    info = kvm_getproc2(kd, KERN_PROC_PID, processId, sizeof(struct kinfo_proc2), &cnt);
    if (info == nullptr || cnt < 1)
    {
        kvm_close(kd);
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

    kvm_close(kd);

    long secondsSinceEpoch = info->p_ustart_sec;
    *disambiguationKey = secondsSinceEpoch;

    return TRUE;

#elif HAVE_PROCFS_STAT

    // Here we read /proc/<pid>/stat file to get the start time for the process.
    // We return this value (which is expressed in jiffies since boot time).

    // Making something like: /proc/123/stat
    char statFileName[64];

    INDEBUG(int chars = )
    snprintf(statFileName, sizeof(statFileName), "/proc/%d/stat", processId);
    _ASSERTE(chars > 0 && chars <= (int)sizeof(statFileName));

    FILE *statFile = fopen(statFileName, "r");
    if (statFile == nullptr)
    {
        TRACE("GetProcessIdDisambiguationKey: fopen() FAILED");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    char *line = nullptr;
    size_t lineLen = 0;
    if (getline(&line, &lineLen, statFile) == -1)
    {
        TRACE("GetProcessIdDisambiguationKey: getline() FAILED");
        SetLastError(ERROR_INVALID_HANDLE);
        free(line);
        fclose(statFile);
        return FALSE;
    }

    unsigned long long starttime;

    // According to `man proc`, the second field in the stat file is the filename of the executable,
    // in parentheses. Tokenizing the stat file using spaces as separators breaks when that name
    // has spaces in it, so we start using sscanf_s after skipping everything up to and including the
    // last closing paren and the space after it.
    char *scanStartPosition = strrchr(line, ')') + 2;

    // All the format specifiers for the fields in the stat file are provided by 'man proc'.
    int sscanfRet = sscanf_s(scanStartPosition,
        "%*c %*d %*d %*d %*d %*d %*u %*u %*u %*u %*u %*u %*u %*d %*d %*d %*d %*d %*d %llu \n",
         &starttime);

    free(line);
    fclose(statFile);

    if (sscanfRet != 1)
    {
        _ASSERTE(!"Failed to parse stat file contents with sscanf_s.");
        return FALSE;
    }

    *disambiguationKey = starttime;
    return TRUE;

#else
    // If this is not OS X and we don't have /proc, we just return FALSE.
    WARN("GetProcessIdDisambiguationKey was called but is not implemented on this platform!");
    return FALSE;
#endif
}
