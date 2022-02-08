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
#include "pal/mutex.hpp"
#include "pal/handlemgr.hpp"
#include "pal/cs.hpp"
#include "procprivate.hpp"
#include "pal/process.h"
#include "pal/module.h"
#include "pal/environ.h"
#include "pal/init.h"
#include "pal/utils.h"
#include "pal/virtual.h"

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

void
ThreadCleanupRoutine(
    CPalThread *pThread,
    IPalObject *pObjectToCleanup,
    bool fShutdown,
    bool fCleanupSharedState
    );

PAL_ERROR
ThreadInitializationRoutine(
    CPalThread *pThread,
    CObjectType *pObjectType,
    void *pImmutableData,
    void *pSharedData,
    void *pProcessLocalData
    );

CObjectType CorUnix::otThread(
                otiThread,
                ThreadCleanupRoutine,
                ThreadInitializationRoutine,
                0,      // sizeof(CThreadImmutableData),
                NULL,   // No immutable data copy routine
                NULL,   // No immutable data cleanup routine
                sizeof(CThreadProcessLocalData),
                NULL,   // No process local data cleanup routine
                0,      // sizeof(CThreadSharedData),
                0,      // THREAD_ALL_ACCESS,
                CObjectType::SecuritySupported,
                CObjectType::SecurityInfoNotPersisted,
                CObjectType::UnnamedObject,
                CObjectType::LocalDuplicationOnly,
                CObjectType::WaitableObject,
                CObjectType::SingleTransitionObject,
                CObjectType::ThreadReleaseHasNoSideEffects,
                CObjectType::NoOwner
                );

CAllowedObjectTypes aotThread(otiThread);

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

    InternalEndCurrentThread(pThread);
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
  GetCurrentThreadId

See MSDN doc.
--*/
DWORD
PALAPI
GetCurrentThreadId(
            VOID)
{
    DWORD dwThreadId;

    PERF_ENTRY(GetCurrentThreadId);
    ENTRY("GetCurrentThreadId()\n");

    //
    // TODO: should do perf test to see how this compares
    // with calling InternalGetCurrentThread (i.e., is our lookaside
    // cache faster on average than pthread_self?)
    //

    dwThreadId = (DWORD)THREADSilentGetCurrentThreadId();

    LOGEXIT("GetCurrentThreadId returns DWORD %#x\n", dwThreadId);
    PERF_EXIT(GetCurrentThreadId);

    return dwThreadId;
}

PAL_ERROR
CorUnix::InternalCreateThread(
    CPalThread *pThread,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    DWORD dwStackSize,
    LPTHREAD_START_ROUTINE lpStartAddress,
    LPVOID lpParameter,
    DWORD dwCreationFlags,
    PalThreadType eThreadType,
    SIZE_T* pThreadId,
    HANDLE *phThread
    )
{
    PAL_ERROR palError;
    CPalThread *pNewThread = NULL;
    CObjectAttributes oa;
    bool fAttributesInitialized = FALSE;
    bool fThreadDataAddedToProcessList = FALSE;
    HANDLE hNewThread = NULL;

    pthread_t pthread;
    pthread_attr_t pthreadAttr;
#if PTHREAD_CREATE_MODIFIES_ERRNO
    int storedErrno;
#endif  // PTHREAD_CREATE_MODIFIES_ERRNO
    BOOL fHoldingProcessLock = FALSE;
    int iError = 0;
    size_t alignedStackSize;

    /* Validate parameters */

    if (lpThreadAttributes != NULL)
    {
        ASSERT("lpThreadAttributes parameter must be NULL (%p)\n",
               lpThreadAttributes);
        palError = ERROR_INVALID_PARAMETER;
        goto EXIT;
    }

    alignedStackSize = dwStackSize;
    if (alignedStackSize != 0)
    {
        // Some systems require the stack size to be aligned to the page size
        if (sizeof(alignedStackSize) <= sizeof(dwStackSize) && alignedStackSize + (GetVirtualPageSize() - 1) < alignedStackSize)
        {
            // When coming here from the public API surface, the incoming value is originally a nonnegative signed int32, so
            // this shouldn't happen
            ASSERT(
                "Couldn't align the requested stack size (%Iu) to the page size because the stack size was too large\n",
                alignedStackSize);
            palError = ERROR_INVALID_PARAMETER;
            goto EXIT;
        }
        alignedStackSize = ALIGN_UP(alignedStackSize, GetVirtualPageSize());
    }

    // Ignore the STACK_SIZE_PARAM_IS_A_RESERVATION flag
    dwCreationFlags &= ~STACK_SIZE_PARAM_IS_A_RESERVATION;

    if ((dwCreationFlags != 0) && (dwCreationFlags != CREATE_SUSPENDED))
    {
        ASSERT("dwCreationFlags parameter is invalid (%#x)\n", dwCreationFlags);
        palError = ERROR_INVALID_PARAMETER;
        goto EXIT;
    }

    //
    // Create the CPalThread for the thread
    //

    pNewThread = AllocTHREAD();
    if (NULL == pNewThread)
    {
        palError = ERROR_OUTOFMEMORY;
        goto EXIT;
    }

    palError = pNewThread->RunPreCreateInitializers();
    if (NO_ERROR != palError)
    {
        goto EXIT;
    }

    pNewThread->m_lpStartAddress = lpStartAddress;
    pNewThread->m_lpStartParameter = lpParameter;
    pNewThread->m_bCreateSuspended = (dwCreationFlags & CREATE_SUSPENDED) == CREATE_SUSPENDED;
    pNewThread->m_eThreadType = eThreadType;

    if (0 != pthread_attr_init(&pthreadAttr))
    {
        ERROR("couldn't initialize pthread attributes\n");
        palError = ERROR_INTERNAL_ERROR;
        goto EXIT;
    }

    fAttributesInitialized = TRUE;

    if (alignedStackSize == 0)
    {
        // The thread is to be created with default stack size. Use the default stack size
        // override that was determined during the PAL initialization.
        alignedStackSize = g_defaultStackSize;
    }

    /* adjust the stack size if necessary */
    if (alignedStackSize != 0)
    {
#ifdef PTHREAD_STACK_MIN
        size_t MinStackSize = ALIGN_UP(PTHREAD_STACK_MIN, GetVirtualPageSize());
#else // !PTHREAD_STACK_MIN
        size_t MinStackSize = 64 * 1024; // this value is typically accepted by pthread_attr_setstacksize()
#endif // PTHREAD_STACK_MIN
        if (alignedStackSize < MinStackSize)
        {
            // Adjust the stack size to a minimum value that is likely to be accepted by pthread_attr_setstacksize(). If this
            // function fails, typically the caller will end up throwing OutOfMemoryException under the assumption that the
            // requested stack size is too large or the system does not have sufficient memory to create a thread. Try to
            // prevent failing just just because the stack size value is too low.
            alignedStackSize = MinStackSize;
        }

        TRACE("setting thread stack size to %Iu\n", alignedStackSize);
        if (0 != pthread_attr_setstacksize(&pthreadAttr, alignedStackSize))
        {
            ERROR("couldn't set pthread stack size to %Iu\n", alignedStackSize);
            palError = ERROR_INTERNAL_ERROR;
            goto EXIT;
        }
    }
    else
    {
        TRACE("using the system default thread stack size\n");
    }

#if HAVE_THREAD_SELF || HAVE__LWP_SELF
    /* Create new threads as "bound", so each pthread is permanently bound
       to an LWP.  Get/SetThreadContext() depend on this 1:1 mapping. */
    pthread_attr_setscope(&pthreadAttr, PTHREAD_SCOPE_SYSTEM);
#endif // HAVE_THREAD_SELF || HAVE__LWP_SELF

    //
    // We never call pthread_join, so create the new thread as detached
    //

    iError = pthread_attr_setdetachstate(&pthreadAttr, PTHREAD_CREATE_DETACHED);
    _ASSERTE(0 == iError);

    //
    // Create the IPalObject for the thread and store it in the object
    //

    palError = CreateThreadObject(
        pThread,
        pNewThread,
        &hNewThread);

    if (NO_ERROR != palError)
    {
        goto EXIT;
    }

    //
    // Add the thread to the process list
    //

    //
    // We use the process lock to ensure that we're not interrupted
    // during the creation process. After adding the CPalThread reference
    // to the process list, we want to make sure the actual thread has been
    // started. Otherwise, there's a window where the thread can be found
    // in the process list but doesn't yet exist in the system.
    //

    PROCProcessLock();
    fHoldingProcessLock = TRUE;

    PROCAddThread(pThread, pNewThread);
    fThreadDataAddedToProcessList = TRUE;

    //
    // Spawn the new pthread
    //

#if PTHREAD_CREATE_MODIFIES_ERRNO
    storedErrno = errno;
#endif  // PTHREAD_CREATE_MODIFIES_ERRNO

    iError = pthread_create(&pthread, &pthreadAttr, CPalThread::ThreadEntry, pNewThread);

#if PTHREAD_CREATE_MODIFIES_ERRNO
    if (iError == 0)
    {
        // Restore errno if pthread_create succeeded.
        errno = storedErrno;
    }
#endif  // PTHREAD_CREATE_MODIFIES_ERRNO

    if (0 != iError)
    {
        ERROR("pthread_create failed, error is %d (%s)\n", iError, strerror(iError));
        palError = ERROR_NOT_ENOUGH_MEMORY;
        goto EXIT;
    }

    //
    // Wait for the new thread to finish its initial startup tasks
    // (i.e., the ones that might fail)
    //
    if (pNewThread->WaitForStartStatus())
    {
        //
        // Everything succeeded. Store the handle for the new thread and
        // the thread's ID in the out params
        //
        *phThread = hNewThread;

        if (NULL != pThreadId)
        {
            *pThreadId = pNewThread->GetThreadId();
        }
    }
    else
    {
        ERROR("error occurred in THREADEntry, thread creation failed.\n");
        palError = ERROR_INTERNAL_ERROR;
        goto EXIT;
    }

    //
    // If we're here, then we've locked the process list and both pthread_create
    // and WaitForStartStatus succeeded. Thus, we can now unlock the process list.
    // Since palError == NO_ERROR, we won't call this again in the exit block.
    //
    PROCProcessUnlock();
    fHoldingProcessLock = FALSE;

EXIT:

    if (fAttributesInitialized)
    {
        if (0 != pthread_attr_destroy(&pthreadAttr))
        {
            WARN("pthread_attr_destroy() failed\n");
        }
    }

    if (NO_ERROR != palError)
    {
        //
        // We either were not able to create the new thread, or a failure
        // occurred in the new thread's entry routine. Free up the associated
        // resources here
        //

        if (fThreadDataAddedToProcessList)
        {
            PROCRemoveThread(pThread, pNewThread);
        }
        //
        // Once we remove the thread from the process list, we can call
        // PROCProcessUnlock.
        //
        if (fHoldingProcessLock)
        {
            PROCProcessUnlock();
        }
        fHoldingProcessLock = FALSE;
    }

    _ASSERT_MSG(!fHoldingProcessLock, "Exiting InternalCreateThread while still holding the process critical section.\n");

    return palError;
}

/*++
Function:
  InternalEndCurrentThread

Does any necessary memory clean up, signals waiting threads, and then forces
the current thread to exit.
--*/

VOID
CorUnix::InternalEndCurrentThread(
    CPalThread *pThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    ISynchStateController *pSynchStateController = NULL;

#ifdef PAL_PERF
    PERFDisableThreadProfile(UserCreatedThread != pThread->GetThreadType());
#endif

    //
    // Abandon any objects owned by this thread
    //

    palError = g_pSynchronizationManager->AbandonObjectsOwnedByThread(
        pThread,
        pThread
        );

    if (NO_ERROR != palError)
    {
        ERROR("Failure abandoning owned objects");
    }

    //
    // Need to synchronize setting the thread state to TS_DONE since
    // this is checked for in InternalSuspendThreadFromData.
    // TODO: Is this still needed after removing InternalSuspendThreadFromData?
    //

    pThread->suspensionInfo.AcquireSuspensionLock(pThread);
    pThread->synchronizationInfo.SetThreadState(TS_DONE);
    pThread->suspensionInfo.ReleaseSuspensionLock(pThread);

    //
    // Mark the thread object as signaled
    //

    palError = pThread->GetThreadObject()->GetSynchStateController(
        pThread,
        &pSynchStateController
        );

    if (NO_ERROR == palError)
    {
        palError = pSynchStateController->SetSignalCount(1);
        if (NO_ERROR != palError)
        {
            ASSERT("Unable to mark thread object as signaled");
        }

        pSynchStateController->ReleaseController();
    }
    else
    {
        ASSERT("Unable to obtain state controller for thread");
    }

    //
    // Add a reference to the thread data before releasing the
    // thread object, so we can still use it
    //

    pThread->AddThreadReference();

    //
    // Release the reference to the IPalObject for this thread
    //

    pThread->GetThreadObject()->ReleaseReference(pThread);

    /* Remove thread for the thread list of the process
        (don't do if this is the last thread -> gets handled by
        TerminateProcess->PROCCleanupProcess->PROCTerminateOtherThreads) */

    PROCRemoveThread(pThread, pThread);

    //
    // Now release our reference to the thread data. We cannot touch
    // it after this point
    //

    pThread->ReleaseThreadReference();
}

    
void *
CPalThread::ThreadEntry(
    void *pvParam
    )
{
    PAL_ERROR palError;
    CPalThread *pThread;
    PTHREAD_START_ROUTINE pfnStartRoutine;
    LPVOID pvPar;
    DWORD retValue;
#if HAVE_SCHED_GETAFFINITY && HAVE_SCHED_SETAFFINITY
    cpu_set_t cpuSet;
    int st;
#endif

    pThread = reinterpret_cast<CPalThread*>(pvParam);

    if (NULL == pThread)
    {
        ASSERT("THREAD pointer is NULL!\n");
        goto fail;
    }

#if HAVE_SCHED_GETAFFINITY && HAVE_SCHED_SETAFFINITY
    // Threads inherit their parent's affinity mask on Linux. This is not desired, so we reset
    // the current thread's affinity mask to the mask of the current process.
    //
    // Typically, we would use pthread_attr_setaffinity_np() and have pthread_create() create the thread with the specified
    // affinity. At least one implementation of pthread_create() following a pthread_attr_setaffinity_np() calls
    // sched_setaffinity(<newThreadPid>, ...), which is not allowed under Snap's default strict confinement without manually
    // connecting the process-control plug. To work around that, have the thread set the affinity after it starts.
    // sched_setaffinity(<currentThreadPid>, ...) is also currently not allowed, only sched_setaffinity(0, ...).
    // pthread_setaffinity_np(pthread_self(), ...) seems to call sched_setaffinity(<currentThreadPid>, ...) in at least one
    // implementation, and does not work. Use sched_setaffinity(0, ...) instead. See the following for more information:
    // - https://github.com/dotnet/runtime/pull/38795
    // - https://github.com/dotnet/runtime/issues/1634
    // - https://forum.snapcraft.io/t/requesting-autoconnect-for-interfaces-in-pigmeat-process-control-home/17987/13

    CPU_ZERO(&cpuSet);

    st = sched_getaffinity(gPID, sizeof(cpu_set_t), &cpuSet);
    if (st != 0)
    {
        ASSERT("sched_getaffinity failed!\n");
        // The sched_getaffinity should never fail for getting affinity of the current process
        palError = ERROR_INTERNAL_ERROR;
        goto fail;
    }

    st = sched_setaffinity(0, sizeof(cpu_set_t), &cpuSet);
    if (st != 0)
    {
        ASSERT("sched_setaffinity failed!\n");
        // The sched_setaffinity should never fail when passed the mask extracted using sched_getaffinity
        palError = ERROR_INTERNAL_ERROR;
        goto fail;
    }
#endif // HAVE_SCHED_GETAFFINITY && HAVE_SCHED_SETAFFINITY

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
        ASSERT("Error %i initializing thread data (post creation)\n", palError);
        goto fail;
    }

    // Check if the thread should be started suspended.
    if (pThread->GetCreateSuspended())
    {
        palError = pThread->suspensionInfo.InternalSuspendNewThreadFromData(pThread);
        if (NO_ERROR != palError)
        {
            ASSERT("Error %i attempting to suspend new thread\n", palError);
            goto fail;
        }
    }
    else
    {
        //
        // All startup operations that might have failed have succeeded,
        // so thread creation is successful. Let CreateThread return.
        //

        pThread->SetStartStatus(TRUE);
    }

    pThread->synchronizationInfo.SetThreadState(TS_RUNNING);

    if (UserCreatedThread == pThread->GetThreadType())
    {
        /* Inform all loaded modules that a thread has been created */
        /* note : no need to take a critical section to serialize here; the loader
           will take the module critical section */
        LOADCallDllMain(DLL_THREAD_ATTACH, NULL);
    }

#ifdef PAL_PERF
    PERFAllocThreadInfo();
    PERFEnableThreadProfile(UserCreatedThread != pThread->GetThreadType());
#endif

    /* call the startup routine */
    pfnStartRoutine = pThread->GetStartAddress();
    pvPar = pThread->GetStartParameter();

    retValue = (*pfnStartRoutine)(pvPar);

    TRACE("Thread exited (%u)\n", retValue);
    pThread->SetExitCode(retValue);

    return NULL;

fail:

    //
    // Notify InternalCreateThread that a failure occurred
    //

    if (NULL != pThread)
    {
        pThread->synchronizationInfo.SetThreadState(TS_FAILED);
        pThread->SetStartStatus(FALSE);
    }

    /* do not call ExitThread : we don't want to call DllMain(), and the thread
       isn't in a clean state (e.g. lpThread isn't in TLS). the cleanup work
       above should release all resources */
    return NULL;
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

/*++
Function:
    CreateThreadData

Abstract:
    Creates the IPalObject for a thread, storing
    the reference in the CPalThread

Parameters:
    pThread - the thread data for the creating thread
    pNewThread - the thread data for the thread being initialized

Return:
   PAL_ERROR
--*/

PAL_ERROR
CorUnix::CreateThreadObject(
    CPalThread *pThread,
    CPalThread *pNewThread,
    HANDLE *phThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    IPalObject *pobjThread = NULL;
    IDataLock *pDataLock;
    HANDLE hThread = NULL;
    CThreadProcessLocalData *pLocalData = NULL;
    CObjectAttributes oa;
    BOOL fThreadDataStoredInObject = FALSE;
    IPalObject *pobjRegisteredThread = NULL;

    //
    // Create the IPalObject for the thread
    //

    palError = g_pObjectManager->AllocateObject(
        pThread,
        &otThread,
        &oa,
        &pobjThread
        );

    if (NO_ERROR != palError)
    {
        goto CreateThreadObjectExit;
    }

    //
    // Store the CPalThread inside of the IPalObject
    //

    palError = pobjThread->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        goto CreateThreadObjectExit;
    }

    pLocalData->pThread = pNewThread;
    pDataLock->ReleaseLock(pThread, TRUE);
    fThreadDataStoredInObject = TRUE;

    //
    // Register the IPalObject (obtaining a handle)
    //

    palError = g_pObjectManager->RegisterObject(
        pThread,
        pobjThread,
        &aotThread,
        &hThread,
        &pobjRegisteredThread
        );

    //
    // pobjThread is invalidated by the call to RegisterObject, so NULL
    // it out here to prevent it from being released
    //

    pobjThread = NULL;

    if (NO_ERROR != palError)
    {
        goto CreateThreadObjectExit;
    }

    //
    // Store the registered object inside of the thread object,
    // adding a reference for the thread itself
    //

    pNewThread->m_pThreadObject = pobjRegisteredThread;
    pNewThread->m_pThreadObject->AddReference();

    *phThread = hThread;

CreateThreadObjectExit:

    if (NO_ERROR != palError)
    {
        if (NULL != hThread)
        {
            g_pObjectManager->RevokeHandle(pThread, hThread);
        }

        if (NULL != pNewThread->m_pThreadObject)
        {
            //
            // Release the new thread's reference on the underlying thread
            // object
            //

            pNewThread->m_pThreadObject->ReleaseReference(pThread);
        }

        if (!fThreadDataStoredInObject)
        {
            //
            // The CPalThread for the new thread was never stored in
            // an IPalObject instance, so we need to release the initial
            // reference here. (If it has been stored it will get freed in
            // the owning object's cleanup routine)
            //

            pNewThread->ReleaseThreadReference();
        }
    }

    if (NULL != pobjThread)
    {
        pobjThread->ReleaseReference(pThread);
    }

    if (NULL != pobjRegisteredThread)
    {
        pobjRegisteredThread->ReleaseReference(pThread);
    }

    return palError;
}

PAL_ERROR
CorUnix::InternalCreateDummyThread(
    CPalThread *pThread,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    CPalThread **ppDummyThread,
    HANDLE *phThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    CPalThread *pDummyThread = NULL;
    IPalObject *pobjThread = NULL;
    IPalObject *pobjThreadRegistered = NULL;
    IDataLock *pDataLock;
    CThreadProcessLocalData *pLocalData;
    CObjectAttributes oa(NULL, lpThreadAttributes);
    bool fThreadDataStoredInObject = FALSE;

    pDummyThread = AllocTHREAD();
    if (NULL == pDummyThread)
    {
        palError = ERROR_OUTOFMEMORY;
        goto InternalCreateDummyThreadExit;
    }

    pDummyThread->m_fIsDummy = TRUE;

    palError = g_pObjectManager->AllocateObject(
        pThread,
        &otThread,
        &oa,
        &pobjThread
        );

    if (NO_ERROR != palError)
    {
        goto InternalCreateDummyThreadExit;
    }

    palError = pobjThread->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        goto InternalCreateDummyThreadExit;
    }

    pLocalData->pThread = pDummyThread;
    pDataLock->ReleaseLock(pThread, TRUE);
    fThreadDataStoredInObject = TRUE;

    palError = g_pObjectManager->RegisterObject(
        pThread,
        pobjThread,
        &aotThread,
        phThread,
        &pobjThreadRegistered
        );

    //
    // pobjThread is invalidated by the above call, so NULL
    // it out here
    //

    pobjThread = NULL;

    if (NO_ERROR != palError)
    {
        goto InternalCreateDummyThreadExit;
    }

    //
    // Note the we do NOT store the registered object for the
    // thread w/in pDummyThread. Since this thread is not actually
    // executing that reference would never be released (and thus
    // the thread object would never be cleaned up...)
    //

    *ppDummyThread = pDummyThread;

InternalCreateDummyThreadExit:

    if (NULL != pobjThreadRegistered)
    {
        pobjThreadRegistered->ReleaseReference(pThread);
    }

    if (NULL != pobjThread)
    {
        pobjThread->ReleaseReference(pThread);
    }

    if (NO_ERROR != palError
        && NULL != pDummyThread
        && !fThreadDataStoredInObject)
    {
        pDummyThread->ReleaseThreadReference();
    }

    return palError;
}

PAL_ERROR
CorUnix::InternalGetThreadDataFromHandle(
    CPalThread *pThread,
    HANDLE hThread,
    CPalThread **ppTargetThread,
    IPalObject **ppobjThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    IPalObject *pobj;
    IDataLock *pLock;
    CThreadProcessLocalData *pData;

    *ppobjThread = NULL;

    if (hPseudoCurrentThread == hThread)
    {
        *ppTargetThread = pThread;
    }
    else
    {
        palError = g_pObjectManager->ReferenceObjectByHandle(
            pThread,
            hThread,
            &aotThread,
            &pobj
            );

        if (NO_ERROR == palError)
        {
            palError = pobj->GetProcessLocalData(
                pThread,
                ReadLock,
                &pLock,
                reinterpret_cast<void**>(&pData)
                );

            if (NO_ERROR == palError)
            {
                *ppTargetThread = pData->pThread;
                pLock->ReleaseLock(pThread, FALSE);

                //
                // Transfer object reference to out param
                //

                *ppobjThread = pobj;
            }
            else
            {
                pobj->ReleaseReference(pThread);
            }
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

    iError = pthread_mutex_init(&m_startMutex, NULL);
    if (0 != iError)
    {
        goto RunPreCreateInitializersExit;
    }

    iError = pthread_cond_init(&m_startCond, NULL);
    if (0 != iError)
    {
        pthread_mutex_destroy(&m_startMutex);
        goto RunPreCreateInitializersExit;
    }

    m_fStartItemsInitialized = TRUE;

    //
    // Call the pre-create initializers for embedded classes
    //

    palError = synchronizationInfo.InitializePreCreate();
    if (NO_ERROR != palError)
    {
        goto RunPreCreateInitializersExit;
    }

    palError = suspensionInfo.InitializePreCreate();
    if (NO_ERROR != palError)
    {
        goto RunPreCreateInitializersExit;
    }

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

    if (m_fStartItemsInitialized)
    {
        int iError;

        iError = pthread_cond_destroy(&m_startCond);
        _ASSERTE(0 == iError);

        iError = pthread_mutex_destroy(&m_startMutex);
        _ASSERTE(0 == iError);
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

    if (pthread_setspecific(thObjKey, reinterpret_cast<void*>(this)))
    {
        ASSERT("Unable to set the thread object key's value\n");
        palError = ERROR_INTERNAL_ERROR;
        goto RunPostCreateInitializersExit;
    }

    palError = synchronizationInfo.InitializePostCreate(this, m_threadId, m_dwLwpId);
    if (NO_ERROR != palError)
    {
        goto RunPostCreateInitializersExit;
    }

    palError = suspensionInfo.InitializePostCreate(this, m_threadId, m_dwLwpId);
    if (NO_ERROR != palError)
    {
        goto RunPostCreateInitializersExit;
    }

    palError = crtInfo.InitializePostCreate(this, m_threadId, m_dwLwpId);
    if (NO_ERROR != palError)
    {
        goto RunPostCreateInitializersExit;
    }

RunPostCreateInitializersExit:

    return palError;
}

void
CPalThread::SetStartStatus(
    bool fStartSucceeded
    )
{
    int iError;

#if _DEBUG
    if (m_fStartStatusSet)
    {
        ASSERT("Multiple calls to CPalThread::SetStartStatus\n");
    }
#endif

    //
    // This routine may get called from CPalThread::ThreadEntry
    //
    // If we've reached this point there are no further thread
    // suspensions that happen at creation time, so reset
    // m_bCreateSuspended
    //

    m_bCreateSuspended = FALSE;

    iError = pthread_mutex_lock(&m_startMutex);
    if (0 != iError)
    {
        ASSERT("pthread primitive failure\n");
        // bugcheck?
    }

    m_fStartStatus = fStartSucceeded;
    m_fStartStatusSet = TRUE;

    iError = pthread_cond_signal(&m_startCond);
    if (0 != iError)
    {
        ASSERT("pthread primitive failure\n");
        // bugcheck?
    }

    iError = pthread_mutex_unlock(&m_startMutex);
    if (0 != iError)
    {
        ASSERT("pthread primitive failure\n");
        // bugcheck?
    }
}

bool
CPalThread::WaitForStartStatus(
    void
    )
{
    int iError;

    iError = pthread_mutex_lock(&m_startMutex);
    if (0 != iError)
    {
        ASSERT("pthread primitive failure\n");
        // bugcheck?
    }

    while (!m_fStartStatusSet)
    {
        iError = pthread_cond_wait(&m_startCond, &m_startMutex);
        if (0 != iError)
        {
            ASSERT("pthread primitive failure\n");
            // bugcheck?
        }
    }

    iError = pthread_mutex_unlock(&m_startMutex);
    if (0 != iError)
    {
        ASSERT("pthread primitive failure\n");
        // bugcheck?
    }

    return m_fStartStatus;
}

void
ThreadCleanupRoutine(
    CPalThread *pThread,
    IPalObject *pObjectToCleanup,
    bool fShutdown,
    bool fCleanupSharedState
    )
{
    CThreadProcessLocalData *pThreadData = NULL;
    CPalThread *pThreadToCleanup = NULL;
    IDataLock *pDataLock = NULL;
    PAL_ERROR palError = NO_ERROR;

    //
    // Free the CPalThread data for the passed in thread
    //

    palError = pObjectToCleanup->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void**>(&pThreadData)
        );

    if (NO_ERROR == palError)
    {
        //
        // Note that we may be cleaning up the data for the calling
        // thread (i.e., pThread == pThreadToCleanup), so the release
        // of the thread reference needs to be the last thing that
        // we do (though in that case it's very likely that the person
        // calling us will be holding an extra reference to allow
        // for the thread data to be available while the rest of the
        // object cleanup takes place).
        //

        pThreadToCleanup = pThreadData->pThread;
        pThreadData->pThread = NULL;
        pDataLock->ReleaseLock(pThread, TRUE);
        pThreadToCleanup->ReleaseThreadReference();
    }
    else
    {
        ASSERT("Unable to obtain thread data");
    }

}

PAL_ERROR
ThreadInitializationRoutine(
    CPalThread *pThread,
    CObjectType *pObjectType,
    void *pImmutableData,
    void *pSharedData,
    void *pProcessLocalData
    )
{
    return NO_ERROR;
}

