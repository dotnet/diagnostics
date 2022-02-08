// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

#include <pthread.h>
#include <sys/syscall.h>

#include "threadsusp.hpp"
#include "threadinfo.hpp"
#include "synchobjects.hpp"
#include <errno.h>

namespace CorUnix
{
    enum PalThreadType
    {
        UserCreatedThread,
        PalWorkerThread,
        SignalHandlerThread
    };
    
    PAL_ERROR
    InternalCreateThread(
        CPalThread *pThread,
        LPSECURITY_ATTRIBUTES lpThreadAttributes,
        DWORD dwStackSize,
        LPTHREAD_START_ROUTINE lpStartAddress,
        LPVOID lpParameter,
        DWORD dwCreationFlags,
        PalThreadType eThreadType,
        SIZE_T* pThreadId,
        HANDLE *phThread
        );

    PAL_ERROR
    InternalGetThreadDataFromHandle(
        CPalThread *pThread,
        HANDLE hThread,
        CPalThread **ppTargetThread,
        IPalObject **ppobjThread
        );

    VOID
    InternalEndCurrentThread(
        CPalThread *pThread
        );

    PAL_ERROR
    InternalCreateDummyThread(
        CPalThread *pThread,
        LPSECURITY_ATTRIBUTES lpThreadAttributes,
        CPalThread **ppDummyThread,
        HANDLE *phThread
        );

 
    PAL_ERROR
    CreateThreadData(
        CPalThread **ppThread
        );

    PAL_ERROR
    CreateThreadObject(
        CPalThread *pThread,
        CPalThread *pNewThread,
        HANDLE *phThread
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

        CThreadCRTInfo() :
            strtokContext(NULL),
            wcstokContext(NULL)
        {
        };
    };

    class CPalThread
    {
        friend
            PAL_ERROR
            InternalCreateThread(
                CPalThread *,
                LPSECURITY_ATTRIBUTES,
                DWORD,
                LPTHREAD_START_ROUTINE,
                LPVOID,
                DWORD,
                PalThreadType,
                SIZE_T*,
                HANDLE*
                );

        friend
            PAL_ERROR
            InternalCreateDummyThread(
                CPalThread *pThread,
                LPSECURITY_ATTRIBUTES lpThreadAttributes,
                CPalThread **ppDummyThread,
                HANDLE *phThread
                );

        friend
            PAL_ERROR
            CreateThreadData(
                CPalThread **ppThread
                );

        friend
            PAL_ERROR
            CreateThreadObject(
                CPalThread *pThread,
                CPalThread *pNewThread,
                HANDLE *phThread
                );

    private:

        CPalThread *m_pNext;
        DWORD m_dwExitCode;
        BOOL m_fExitCodeSet;
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
        // The IPalObject for this thread. The thread will release its reference
        // to this object when it exits.
        //

        IPalObject *m_pThreadObject;

        //
        // Thread ID info
        //

        SIZE_T m_threadId;
        DWORD m_dwLwpId;
        pthread_t m_pthreadSelf;

        //
        // Start info
        //

        LPTHREAD_START_ROUTINE m_lpStartAddress;
        LPVOID m_lpStartParameter;
        BOOL m_bCreateSuspended;
        PalThreadType m_eThreadType;

        //
        // pthread mutex / condition variable for gating thread startup.
        // InternalCreateThread waits on the condition variable to determine
        // when the new thread has reached passed all failure points in
        // the entry routine
        //

        pthread_mutex_t m_startMutex;
        pthread_cond_t m_startCond;
        bool m_fStartItemsInitialized;
        bool m_fStartStatus;
        bool m_fStartStatusSet;

        // Base address of the stack of this thread
        void* m_stackBase;
        // Limit address of the stack of this thread
        void* m_stackLimit;
        // Signal handler's alternate stack to help with stack overflow
        void* m_alternateStack;

        //
        // The thread entry routine (called from InternalCreateThread)
        //

        static void* ThreadEntry(void * pvParam);

        //
        // Data for PAL side-by-side support
        //

    public:

        //
        // Embedded information for areas owned by other subsystems
        //

        CThreadSynchronizationInfo synchronizationInfo;
        CThreadSuspensionInfo suspensionInfo;
        CThreadCRTInfo crtInfo;

        CPalThread()
            :
            m_pNext(NULL),
            m_dwExitCode(STILL_ACTIVE),
            m_fExitCodeSet(FALSE),
            m_fLockInitialized(FALSE),
            m_fIsDummy(FALSE),
            m_lRefCount(1),
            m_pThreadObject(NULL),
            m_threadId(0),
            m_dwLwpId(0),
            m_pthreadSelf(0),
            m_lpStartAddress(NULL),
            m_lpStartParameter(NULL),
            m_bCreateSuspended(FALSE),
            m_eThreadType(UserCreatedThread),
            m_fStartItemsInitialized(FALSE),
            m_fStartStatus(FALSE),
            m_fStartStatusSet(FALSE),
            m_stackBase(NULL),
            m_stackLimit(NULL)
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

        //
        // SetStartStatus is called by THREADEntry or InternalSuspendNewThread
        // to inform InternalCreateThread of the results of the thread's
        // initialization. InternalCreateThread calls WaitForStartStatus to
        // obtain this information (and will not return to its caller until
        // the info is available).
        //

        void
        SetStartStatus(
            bool fStartSucceeded
            );

        bool
        WaitForStartStatus(
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

        //
        // The following three methods provide access to the
        // native lock used to protect thread native wait data.
        //

        void
        AcquireNativeWaitLock(
            void
            )
        {
            synchronizationInfo.AcquireNativeWaitLock();
        }

        void
        ReleaseNativeWaitLock(
            void
            )
        {
            synchronizationInfo.ReleaseNativeWaitLock();
        }

        bool
        TryAcquireNativeWaitLock(
            void
            )
        {
            return synchronizationInfo.TryAcquireNativeWaitLock();
        }

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

        void
        SetExitCode(
            DWORD dwExitCode
            )
        {
            m_dwExitCode = dwExitCode;
            m_fExitCodeSet = TRUE;
        };

        BOOL
        GetExitCode(
            DWORD *pdwExitCode
            )
        {
            *pdwExitCode = m_dwExitCode;
            return m_fExitCodeSet;
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

        LPTHREAD_START_ROUTINE
        GetStartAddress(
            void
            )
        {
            return m_lpStartAddress;
        };

        LPVOID
        GetStartParameter(
            void
            )
        {
            return m_lpStartParameter;
        };

        BOOL
        GetCreateSuspended(
            void
            )
        {
            return m_bCreateSuspended;
        };

        PalThreadType
        GetThreadType(
            void
            )
        {
            return m_eThreadType;
        };

        IPalObject *
        GetThreadObject(
            void
            )
        {
            return m_pThreadObject;
        }
        BOOL
        IsDummy(
            void
            )
        {
            return m_fIsDummy;
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

/***

    $$TODO: These are needed only to support cross-process thread duplication

    class CThreadImmutableData
    {
    public:
        DWORD dwProcessId;
    };

    class CThreadSharedData
    {
    public:
        DWORD dwThreadId;
        DWORD dwExitCode;
    };
***/

    //
    // The process local information for a thread is just a pointer
    // to the underlying CPalThread object.
    //

    class CThreadProcessLocalData
    {
    public:
        CPalThread *pThread;
    };

    extern CObjectType otThread;
}

BOOL
TLSInitialize(
    void
    );

VOID
TLSCleanup(
    void
    );

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
#define PlatformGetCurrentThreadId() (SIZE_T)syscall(SYS_gettid)
#elif defined(__APPLE__)
inline SIZE_T PlatformGetCurrentThreadId() {
    uint64_t tid;
    pthread_threadid_np(pthread_self(), &tid);
    return (SIZE_T)tid;
}
#elif defined(__FreeBSD__)
#include <pthread_np.h>
#define PlatformGetCurrentThreadId() (SIZE_T)pthread_getthreadid_np()
#elif defined(__NetBSD__)
#include <lwp.h>
#define PlatformGetCurrentThreadId() (SIZE_T)_lwp_self()
#else
#define PlatformGetCurrentThreadId() (SIZE_T)pthread_self()
#endif

inline SIZE_T THREADSilentGetCurrentThreadId() {
    static __thread SIZE_T tid;
    if (!tid)
        tid = PlatformGetCurrentThreadId();
    return tid;
}

#endif // _PAL_THREAD_HPP_
