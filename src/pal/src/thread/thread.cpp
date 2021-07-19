// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    thread.cpp

Abstract:

    Thread object and core APIs



--*/

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(THREAD); // some headers have code with asserts, so do this first

#include "pal/corunix.hpp"
#include "pal/thread.hpp"
#include "pal/handlemgr.hpp"
#include "pal/cs.hpp"
#include "pal/process.h"
#include "pal/module.h"
#include "pal/environ.h"
#include "pal/init.h"
#include "pal/utils.h"

#if defined(__NetBSD__) && !HAVE_PTHREAD_GETCPUCLOCKID
#include <sys/cdefs.h>
#include <sys/param.h>
#include <sys/sysctl.h>
#include <kvm.h>
#elif defined(__sun)
#ifndef _KERNEL
#define _KERNEL
#define UNDEF_KERNEL
#endif
#include <sys/procfs.h>
#ifdef UNDEF_KERNEL
#undef _KERNEL
#endif
#endif

#include <signal.h>
#include <pthread.h>
#if HAVE_PTHREAD_NP_H
#include <pthread_np.h>
#endif
#include <unistd.h>
#include <errno.h>
#include <stddef.h>
#include <sys/stat.h>
#if HAVE_MACH_THREADS
#include <mach/mach.h>
#endif // HAVE_MACH_THREADS
#if HAVE_POLL
#include <poll.h>
#else
#include "pal/fakepoll.h"
#endif  // HAVE_POLL
#include <limits.h>

#if HAVE_SYS_LWP_H
#include <sys/lwp.h>
#endif
#if HAVE_LWP_H
#include <lwp.h>
#endif
// If we don't have sys/lwp.h but do expect to use _lwp_self, declare it to silence compiler warnings
#if HAVE__LWP_SELF && !HAVE_SYS_LWP_H && !HAVE_LWP_H
extern "C" int _lwp_self ();
#endif

using namespace CorUnix;

/*++
Function:
  InternalEndCurrentThreadWrapper

  Destructor for the thread-specific data representing the current PAL thread.
  Called from pthread_exit.  (pthread_exit is not called from the thread on which
  main() was first invoked.  This is not a problem, though, since when main()
  returns, this results in an implicit call to exit().)

  arg: the PAL thread
*/
static void InternalEndCurrentThreadWrapper(void *arg)
{
    CPalThread *pThread = (CPalThread *) arg;

    // When pthread_exit calls us, it has already removed the PAL thread
    // from TLS.  Since InternalEndCurrentThread calls functions that assert
    // that the current thread is known to this PAL, and that pThread
    // actually is the current PAL thread, put it back in TLS temporarily.
    pthread_setspecific(thObjKey, pThread);

    /* Call entry point functions of every attached modules to
       indicate the thread is exiting */
    /* note : no need to enter a critical section for serialization, the loader
       will lock its own critical section */
    LOADCallDllMain(DLL_THREAD_DETACH, NULL);

    pthread_setspecific(thObjKey, NULL);
}

/*++
Function:
  TLSInitialize

  Initialize the TLS subsystem
--*/
BOOL TLSInitialize()
{
    /* Create the pthread key for thread objects, which we use
       for fast access to the current thread object. */
    if (pthread_key_create(&thObjKey, InternalEndCurrentThreadWrapper))
    {
        ERROR("Couldn't create the thread object key\n");
        return FALSE;
    }

    return TRUE;
}

/*++
Function:
    TLSCleanup

    Shutdown the TLS subsystem
--*/
VOID TLSCleanup()
{
    pthread_key_delete(thObjKey);
}

/*++
Function:
    AllocTHREAD

Abstract:
    Allocate CPalThread instance

Return:
    The fresh thread structure, NULL otherwise
--*/
CPalThread* AllocTHREAD()
{
    return InternalNew<CPalThread>();
}

/*++
Function:
    FreeTHREAD

Abstract:
    Free THREAD structure

--*/
static void FreeTHREAD(CPalThread *pThread)
{
    //
    // Run the destructors for this object
    //

    pThread->~CPalThread();

#ifdef _DEBUG
    // Fill value so we can find code re-using threads after they're dead. We
    // check against pThread->dwGuard when getting the current thread's data.
    memset((void*)pThread, 0xcc, sizeof(*pThread));
#endif

    free(pThread);
}
    
/*++
Function:
    CreateThreadData

Abstract:
    Create the CPalThread for the startup thread
    or another external thread entering the PAL
    for the first time

Parameters:
    ppThread - on success, receives the CPalThread

Return:
   PAL_ERROR
--*/

PAL_ERROR
CorUnix::CreateThreadData(
    CPalThread **ppThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    CPalThread *pThread = NULL;

    /* Create the thread object */
    pThread = AllocTHREAD();

    if (NULL == pThread)
    {
       palError = ERROR_OUTOFMEMORY;
       goto CreateThreadDataExit;
    }

    palError = pThread->RunPreCreateInitializers();

    if (NO_ERROR != palError)
    {
        goto CreateThreadDataExit;
    }

    pThread->SetLastError(0);

    pThread->m_threadId = THREADSilentGetCurrentThreadId();
    pThread->m_pthreadSelf = pthread_self();
#if HAVE_THREAD_SELF
    pThread->m_dwLwpId = (DWORD) thread_self();
#elif HAVE__LWP_SELF
    pThread->m_dwLwpId = (DWORD) _lwp_self();
#else
    pThread->m_dwLwpId = 0;
#endif

    palError = pThread->RunPostCreateInitializers();
    if (NO_ERROR != palError)
    {
        goto CreateThreadDataExit;
    }

    *ppThread = pThread;

CreateThreadDataExit:

    if (NO_ERROR != palError)
    {
        if (NULL != pThread)
        {
            pThread->ReleaseThreadReference();
        }
    }

    return palError;
}

PAL_ERROR
CPalThread::RunPreCreateInitializers(
    void
    )
{
    PAL_ERROR palError = NO_ERROR;
    int iError;

    //
    // First, perform initialization of CPalThread private members
    //

    InternalInitializeCriticalSection(&m_csLock);
    m_fLockInitialized = TRUE;

    //
    // Call the pre-create initializers for embedded classes
    //

    palError = crtInfo.InitializePreCreate();
    if (NO_ERROR != palError)
    {
        goto RunPreCreateInitializersExit;
    }

RunPreCreateInitializersExit:

    return palError;
}

CPalThread::~CPalThread()
{
    // @UNIXTODO: This is our last chance to unlink our Mach exception handler from the pseudo-chain we're trying
    // to maintain. Unfortunately we don't have enough data or control to do this at all well (and we can't
    // guarantee that another component hasn't chained to us, about which we can do nothing). If the kernel or
    // another component forwards an exception notification to us for this thread things will go badly (we'll
    // terminate the process when trying to look up this CPalThread in order to find forwarding information).
    // On the flip side I don't believe we'll get here currently unless the thread has been terminated (in
    // which case it's not an issue). If we start supporting unload or early disposal of CPalThread objects
    // (say when we return from an outer reverse p/invoke) then we'll need to revisit this. But hopefully by
    // then we'll have an alternative design for handling hardware exceptions.

    if (m_fLockInitialized)
    {
        InternalDeleteCriticalSection(&m_csLock);
    }
}

void
CPalThread::AddThreadReference(
    void
    )
{
    InterlockedIncrement(&m_lRefCount);
}

void
CPalThread::ReleaseThreadReference(
    void
    )
{
    LONG lRefCount = InterlockedDecrement(&m_lRefCount);
    _ASSERT_MSG(lRefCount >= 0, "Released a thread and ended with a negative refcount (%ld)\n", lRefCount);
    if (0 == lRefCount)
    {
        FreeTHREAD(this);
    }

}

PAL_ERROR
CPalThread::RunPostCreateInitializers(
    void
    )
{
    PAL_ERROR palError = NO_ERROR;

    //
    // Call the post-create initializers for embedded classes
    //

    palError = crtInfo.InitializePostCreate(this, m_threadId, m_dwLwpId);
    if (NO_ERROR != palError)
    {
        goto RunPostCreateInitializersExit;
    }

RunPostCreateInitializersExit:

    return palError;
}

