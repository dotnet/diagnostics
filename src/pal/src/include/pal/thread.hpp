// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

/*++



Module Name:

    include/pal/thread.hpp

Abstract:
    Header file for thread structures



--*/

#ifndef _PAL_THREAD_HPP_
#define _PAL_THREAD_HPP_

#include "corunix.hpp"
#include "shm.hpp"
#include "cs.hpp"
#include "pal/threadinfo.hpp"

#include <pthread.h>    
#include <sys/syscall.h>
#include <errno.h>

namespace CorUnix
{
    extern pthread_key_t thObjKey;

    enum PalThreadType
    {
        UserCreatedThread,
        PalWorkerThread,
        SignalHandlerThread
    };
    
    PAL_ERROR
    CreateThreadData(
        CPalThread **ppThread
        );

    /* In the windows CRT there is a constant defined for the max width
    of a _ecvt conversion. That constant is 348. 348 for the value, plus
    the exponent value, decimal, and sign if required. */
#define ECVT_MAX_COUNT_SIZE 348
#define ECVT_MAX_BUFFER_SIZE 357

    /*STR_TIME_SIZE is defined as 26 the size of the
      return val by ctime_r*/
#define STR_TIME_SIZE 26

    class CThreadCRTInfo : public CThreadInfoInitializer
    {
    public:
        CHAR *       strtokContext; // Context for strtok function
        WCHAR *      wcstokContext; // Context for wcstok function
        struct PAL_tm localtimeBuffer; // Buffer for localtime function
        CHAR         ctimeBuffer[ STR_TIME_SIZE ]; // Buffer for ctime function
        CHAR         ECVTBuffer[ ECVT_MAX_BUFFER_SIZE ]; // Buffer for _ecvt function.

        CThreadCRTInfo() :
            strtokContext(NULL),
            wcstokContext(NULL)
        {
            ZeroMemory(&localtimeBuffer, sizeof(localtimeBuffer));
            ZeroMemory(ctimeBuffer, sizeof(ctimeBuffer));
            ZeroMemory(ECVTBuffer, sizeof(ECVTBuffer));
        };
    };

    class CPalThread
    {
        friend
            PAL_ERROR
            CreateThreadData(
                CPalThread **ppThread
                );

    private:

        CPalThread *m_pNext;
        CRITICAL_SECTION m_csLock;
        bool m_fLockInitialized;
        bool m_fIsDummy;

        //
        // Minimal reference count, used primarily for cleanup purposes. A
        // new thread object has an initial refcount of 1. This initial
        // reference is removed by CorUnix::InternalEndCurrentThread.
        //
        // The only other spot the refcount is touched is from within
        // CPalObjectBase::ReleaseReference -- incremented before the
        // destructors for an ojbect are called, and decremented afterwords.
        // This permits the freeing of the thread structure to happen after
        // the freeing of the enclosing thread object has completed.
        //

        LONG m_lRefCount;

        //
        // Thread ID info
        //

        SIZE_T m_threadId;
        DWORD m_dwLwpId;
        pthread_t m_pthreadSelf;        

    public:

        //
        // Embedded information for areas owned by other subsystems
        //

        CThreadCRTInfo crtInfo;

        CPalThread()
            :
            m_pNext(NULL),
            m_fLockInitialized(FALSE),
            m_lRefCount(1),
            m_threadId(0),
            m_dwLwpId(0),
            m_pthreadSelf(0)
        {
        };

        virtual ~CPalThread();

        PAL_ERROR
        RunPreCreateInitializers(
            void
            );

        //
        // m_threadId and m_dwLwpId must be set before calling
        // RunPostCreateInitializers
        //

        PAL_ERROR
        RunPostCreateInitializers(
            void
            );

        void
        Lock(
            CPalThread *pThread
            )
        {
            InternalEnterCriticalSection(pThread, &m_csLock);
        };

        void
        Unlock(
            CPalThread *pThread
            )
        {
            InternalLeaveCriticalSection(pThread, &m_csLock);
        };

        static void
        SetLastError(
            DWORD dwLastError
            )
        {
            // Reuse errno to store last error
            errno = dwLastError;
        };

        static DWORD
        GetLastError(
            void
            )
        {
            // Reuse errno to store last error
            return errno;
        };

        SIZE_T
        GetThreadId(
            void
            )
        {
            return m_threadId;
        };
        
        DWORD
        GetLwpId(
            void
            )
        {
            return m_dwLwpId;
        };

        pthread_t
        GetPThreadSelf(
            void
            )
        {
            return m_pthreadSelf;
        };

        CPalThread*
        GetNext(
            void
            )
        {
            return m_pNext;
        };

        void
        SetNext(
            CPalThread *pNext
            )
        {
            m_pNext = pNext;
        };

        void
        AddThreadReference(
            void
            );

        void
        ReleaseThreadReference(
            void
            );
    };

    extern "C" CPalThread *CreateCurrentThreadData();

    inline CPalThread *GetCurrentPalThread()
    {
        return reinterpret_cast<CPalThread*>(pthread_getspecific(thObjKey));
    }

    inline CPalThread *InternalGetCurrentThread()
    {
        CPalThread *pThread = GetCurrentPalThread();
        if (pThread == nullptr)
            pThread = CreateCurrentThreadData();
        return pThread;
    }
}

BOOL
TLSInitialize(
    void
    );

VOID
TLSCleanup(
    void
    );

extern int free_threads_spinlock;

#define SYNCSPINLOCK_F_ASYMMETRIC  1

#define SPINLOCKInit(lock) (*(lock) = 0)
#define SPINLOCKDestroy SPINLOCKInit

void SPINLOCKAcquire (LONG * lock, unsigned int flags);
void SPINLOCKRelease (LONG * lock);
DWORD SPINLOCKTryAcquire (LONG * lock);

/*++
Macro:
  THREADSilentGetCurrentThreadId

Abstract:
  Same as GetCurrentThreadId, but it doesn't output any traces.
  It is useful for tracing functions to display the thread ID
  without generating any new traces.

  TODO: how does the perf of pthread_self compare to
  InternalGetCurrentThread when we find the thread in the
  cache?

  If the perf of pthread_self is comparable to that of the stack
  bounds based lookaside system, why aren't we using it in the
  cache?

  In order to match the thread ids that debuggers use at least for
  linux we need to use gettid(). 

--*/
#if defined(__linux__)
#define THREADSilentGetCurrentThreadId() (SIZE_T)syscall(SYS_gettid)
#elif defined(__APPLE__)
inline SIZE_T THREADSilentGetCurrentThreadId() {
    uint64_t tid;
    pthread_threadid_np(pthread_self(), &tid);
    return (SIZE_T)tid;
}
#elif defined(__FreeBSD__)
#include <sys/thr.h>
inline SIZE_T THREADSilentGetCurrentThreadId() {
    long tid;
    thr_self(&tid);
    return (SIZE_T)tid;
}
#elif defined(__NetBSD__)
#include <lwp.h>
#define THREADSilentGetCurrentThreadId() (SIZE_T)_lwp_self()
#else
#define THREADSilentGetCurrentThreadId() (SIZE_T)pthread_self()
#endif

#endif // _PAL_THREAD_HPP_
