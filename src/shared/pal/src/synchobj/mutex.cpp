// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++

Module Name:

    mutex.cpp

Abstract:

    Implementation of mutex synchroniztion object as described in
    the WIN32 API

Revision History:

--*/

#include "pal/mutex.hpp"

/* Basic spinlock implementation */
void SPINLOCKAcquire (LONG * lock, unsigned int flags)
{
    size_t loop_seed = 1, loop_count = 0;

    if (flags & SYNCSPINLOCK_F_ASYMMETRIC)
    {
        loop_seed = ((size_t)pthread_self() % 10) + 1;
    }
    while (InterlockedCompareExchange(lock, 1, 0))
    {
        if (!(flags & SYNCSPINLOCK_F_ASYMMETRIC) || (++loop_count % loop_seed))
        {
#if PAL_IGNORE_NORMAL_THREAD_PRIORITY
            struct timespec tsSleepTime;
            tsSleepTime.tv_sec = 0;
            tsSleepTime.tv_nsec = 1;
            nanosleep(&tsSleepTime, NULL);
#else
            sched_yield();
#endif
        }
    }

}

void SPINLOCKRelease (LONG * lock)
{
    VolatileStore(lock, 0);
}

DWORD SPINLOCKTryAcquire (LONG * lock)
{
    return InterlockedCompareExchange(lock, 1, 0);
    // only returns 0 or 1.
}
