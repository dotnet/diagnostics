// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++

Module Name:

    init/pal.cpp

Abstract:

    Implementation of PAL exported functions not part of the Win32 API.

--*/

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PAL); // some headers have code with asserts, so do this first

#include "pal/thread.hpp"
#include "pal/synchobjects.hpp"
#include "pal/procobj.hpp"
#include "pal/cs.hpp"
#include "pal/file.hpp"
#include "pal/map.hpp"
#include "../objmgr/shmobjectmanager.hpp"
#include "pal/palinternal.h"
#include "pal/sharedmemory.h"
#include "pal/shmemory.h"
#include "pal/process.h"
#include "../thread/procprivate.hpp"
#include "pal/module.h"
#include "pal/virtual.h"
#include "pal/misc.h"
#include "pal/environ.h"
#include "pal/utils.h"
#include "pal/init.h"
#include "pal/stackstring.hpp"

#include <stdlib.h>
#include <unistd.h>
#include <pwd.h>
#include <errno.h>
#include <sys/types.h>
#include <sys/param.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <limits.h>
#include <string.h>
#include <fcntl.h>

#if HAVE_POLL
#include <poll.h>
#else
#include "pal/fakepoll.h"
#endif  // HAVE_POLL

#if defined(__APPLE__)
#include <sys/sysctl.h>
int CacheLineSize;
#endif //__APPLE__

#ifdef __APPLE__
#include <mach-o/dyld.h>
#endif // __APPLE__

#ifdef __NetBSD__
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

#ifdef __FreeBSD__
#include <sys/user.h>
#include <sys/sysctl.h>
#endif

#include <algorithm>

using namespace CorUnix;

//
// $$TODO The C++ compiler doesn't like pal/cruntime.h so duplicate the
// necessary prototype here
//

extern "C" BOOL CRTInitStdStreams( void );

Volatile<INT> init_count = 0;
Volatile<BOOL> shutdown_intent = 0;
static BOOL g_fThreadDataAvailable = FALSE;
static pthread_mutex_t init_critsec_mutex = PTHREAD_MUTEX_INITIALIZER;

// The default minimum stack size
SIZE_T g_defaultStackSize = 0;

// The default value of parameter, whether to mmap images at default base address or not
BOOL g_useDefaultBaseAddr = FALSE;

/* critical section to protect access to init_count. This is allocated on the
   very first PAL_Initialize call, and is freed afterward. */
static PCRITICAL_SECTION init_critsec = NULL;

static int Initialize(int argc, const char *const argv[], DWORD flags);
static BOOL INIT_IncreaseDescriptorLimit(void);

#if defined(__APPLE__)
static bool RunningNatively()
{
    int ret = 0;
    size_t sz = sizeof(ret);
    if (sysctlbyname("sysctl.proc_native", &ret, &sz, NULL, 0) != 0)
    {
        // if the sysctl failed, we'll assume this OS does not support
        // binary translation - so we must be running natively.
        return true;
    }
    return ret != 0;
}
#endif // __APPLE__


/*++
Function:
  PAL_InitializeWithFlags

Abstract:
  This function is the first function of the PAL to be called.
  Internal structure initialization is done here. It could be called
  several time by the same process, a reference count is kept.

Return:
  0 if successful
  -1 if it failed

--*/
int
PALAPI
PAL_InitializeWithFlags(
    int argc,
    const char *const argv[],
    DWORD flags)
{
    return Initialize(argc, argv, flags);
}

/*++
Function:
  PAL_InitializeDLL

Abstract:
    Initializes the non-runtime DLLs/modules like the DAC and SOS.

Return:
  0 if successful
  -1 if it failed

--*/
int
PALAPI
PAL_InitializeDLL()
{
    return Initialize(0, NULL, PAL_INITIALIZE_DLL);
}

/*++
Function:
  InitializeDefaultStackSize

Abstract:
  Initializes the default stack size.

--*/
void
InitializeDefaultStackSize()
{
#ifdef ENSURE_PRIMARY_STACK_SIZE
    if (g_defaultStackSize == 0)
    {
        // Set the default minimum stack size for MUSL to the same value as we
        // use on Windows.
        g_defaultStackSize = 1536 * 1024;
    }
#endif // ENSURE_PRIMARY_STACK_SIZE
}

/*++
Function:
  Initialize

Abstract:
  Common PAL initialization function.

Return:
  0 if successful
  -1 if it failed

--*/
int
Initialize(
    int argc,
    const char *const argv[],
    DWORD flags)
{
    PAL_ERROR palError = ERROR_GEN_FAILURE;
    CPalThread *pThread = NULL;
    CSharedMemoryObjectManager *pshmom = NULL;
    int retval = -1;
    bool fFirstTimeInit = false;

    /* the first ENTRY within the first call to PAL_Initialize is a special
       case, since debug channels are not initialized yet. So in that case the
       ENTRY will be called after the DBG channels initialization */
    ENTRY_EXTERNAL("PAL_Initialize(argc = %d argv = %p)\n", argc, argv);

    /*Firstly initiate a lastError */
    SetLastError(ERROR_GEN_FAILURE);

#ifdef __APPLE__
    if (!RunningNatively())
    {
        SetLastError(ERROR_BAD_FORMAT);
        goto exit;
    }
#endif // __APPLE__

    CriticalSectionSubSysInitialize();

    if(NULL == init_critsec)
    {
        pthread_mutex_lock(&init_critsec_mutex); // prevents race condition of two threads
                                                 // initializing the critical section.
        if(NULL == init_critsec)
        {
            static CRITICAL_SECTION temp_critsec;

            // Want this critical section to NOT be internal to avoid the use of unsafe region markers.
            InternalInitializeCriticalSectionAndSpinCount(&temp_critsec, 0, false);

            if(NULL != InterlockedCompareExchangePointer(&init_critsec, &temp_critsec, NULL))
            {
                // Another thread got in before us! shouldn't happen, if the PAL
                // isn't initialized there shouldn't be any other threads
                WARN("Another thread initialized the critical section\n");
                InternalDeleteCriticalSection(&temp_critsec);
            }
        }
        pthread_mutex_unlock(&init_critsec_mutex);
    }

    InternalEnterCriticalSection(pThread, init_critsec); // here pThread is always NULL

    if (init_count == 0)
    {
        // Set our pid and sid.
        gPID = getpid();
        gSID = getsid(gPID);

        // Initialize the thread local storage
        if (FALSE == TLSInitialize())
        {
            palError = ERROR_PALINIT_TLS;
            goto done;
        }

        // Initialize debug channel settings before anything else.
        if (FALSE == DBG_init_channels())
        {
            palError = ERROR_PALINIT_DBG_CHANNELS;
            goto CLEANUP0a;
        }

        fFirstTimeInit = true;

        InitializeDefaultStackSize();

        // Initialize the environment.
        if (FALSE == EnvironInitialize())
        {
            palError = ERROR_PALINIT_ENV;
            goto CLEANUP0;
        }


        if (!INIT_IncreaseDescriptorLimit())
        {
            ERROR("Unable to increase the file descriptor limit!\n");
            // We can continue if this fails; we'll just have problems if
            // we use large numbers of threads or have many open files.
        }

        /* initialize the shared memory infrastructure */
        if (!SHMInitialize())
        {
            ERROR("Shared memory initialization failed!\n");
            palError = ERROR_PALINIT_SHM;
            goto CLEANUP0;
        }

        //
        // Initialize global process data
        //

        palError = InitializeProcessData();
        if (NO_ERROR != palError)
        {
            ERROR("Unable to initialize process data\n");
            goto CLEANUP1;
        }

        // Allocate the initial thread data
        //

        palError = CreateThreadData(&pThread);
        if (NO_ERROR != palError)
        {
            ERROR("Unable to create initial thread data\n");
            goto CLEANUP1a;
        }

        PROCAddThread(pThread, pThread);

        //
        // It's now safe to access our thread data
        //

        g_fThreadDataAvailable = TRUE;

        //
        // Initialize module manager
        //
        if (FALSE == LOADInitializeModules())
        {
            ERROR("Unable to initialize module manager\n");
            palError = ERROR_PALINIT_MODULE_MANAGER;
            goto CLEANUP1b;
        }

        //
        // Initialize the object manager
        //

        pshmom = InternalNew<CSharedMemoryObjectManager>();
        if (NULL == pshmom)
        {
            ERROR("Unable to allocate new object manager\n");
            palError = ERROR_OUTOFMEMORY;
            goto CLEANUP1b;
        }

        palError = pshmom->Initialize();
        if (NO_ERROR != palError)
        {
            ERROR("object manager initialization failed!\n");
            InternalDelete(pshmom);
            goto CLEANUP1b;
        }

        g_pObjectManager = pshmom;

        //
        // Initialize the synchronization manager
        //
        g_pSynchronizationManager =
            CPalSynchMgrController::CreatePalSynchronizationManager();

        if (NULL == g_pSynchronizationManager)
        {
            palError = ERROR_NOT_ENOUGH_MEMORY;
            ERROR("Failure creating synchronization manager\n");
            goto CLEANUP1c;
        }
    }
    else
    {
        pThread = InternalGetCurrentThread();
    }

    palError = ERROR_GEN_FAILURE;

    if (init_count == 0)
    {
        //
        // Create the initial process and thread objects
        //
        palError = CreateInitialProcessAndThreadObjects(pThread);
        if (NO_ERROR != palError)
        {
            ERROR("Unable to create initial process and thread objects\n");
            goto CLEANUP2;
        }

        palError = ERROR_GEN_FAILURE;

        /* Initialize the File mapping critical section. */
        if (FALSE == MAPInitialize())
        {
            ERROR("Unable to initialize file mapping support\n");
            palError = ERROR_PALINIT_MAP;
            goto CLEANUP6;
        }

        /* Initialize the Virtual* functions. */
        bool initializeExecutableMemoryAllocator = (flags & PAL_INITIALIZE_EXEC_ALLOCATOR) != 0;
        if (FALSE == VIRTUALInitialize(initializeExecutableMemoryAllocator))
        {
            ERROR("Unable to initialize virtual memory support\n");
            palError = ERROR_PALINIT_VIRTUAL;
            goto CLEANUP10;
        }

        if (flags & PAL_INITIALIZE_SYNC_THREAD)
        {
            //
            // Tell the synchronization manager to start its worker thread
            //
            palError = CPalSynchMgrController::StartWorker(pThread);
            if (NO_ERROR != palError)
            {
                ERROR("Synch manager failed to start worker thread\n");
                goto CLEANUP13;
            }
        }

        if (flags & PAL_INITIALIZE_STD_HANDLES)
        {
            /* create file objects for standard handles */
            if (!FILEInitStdHandles())
            {
                ERROR("Unable to initialize standard file handles\n");
                palError = ERROR_PALINIT_STD_HANDLES;
                goto CLEANUP14;
            }
        }

        if (FALSE == CRTInitStdStreams())
        {
            ERROR("Unable to initialize CRT standard streams\n");
            palError = ERROR_PALINIT_STD_STREAMS;
            goto CLEANUP15;
        }

        TRACE("First-time PAL initialization complete.\n");
        init_count++;

        /* Set LastError to a non-good value - functions within the
           PAL startup may set lasterror to a nonzero value. */
        SetLastError(NO_ERROR);
        retval = 0;
    }
    else
    {
        init_count++;

        TRACE("Initialization count increases to %d\n", init_count.Load());

        SetLastError(NO_ERROR);
        retval = 0;
    }
    goto done;

    /* No cleanup required for CRTInitStdStreams */ 
CLEANUP15:
    FILECleanupStdHandles();
CLEANUP14:
CLEANUP13:
    VIRTUALCleanup();
CLEANUP10:
    MAPCleanup();
CLEANUP6:
    PROCCleanupInitialProcess();
CLEANUP2:
    // Cleanup synchronization manager
CLEANUP1c:
    // Cleanup object manager
CLEANUP1b:
    // Cleanup initial thread data
CLEANUP1a:
    // Cleanup global process data
CLEANUP1:
    SHMCleanup();
CLEANUP0:
CLEANUP0a:
    TLSCleanup();
    ERROR("PAL_Initialize failed\n");
    SetLastError(palError);
done:
#ifdef PAL_PERF
    if( retval == 0)
    {
         PERFEnableProcessProfile();
         PERFEnableThreadProfile(FALSE);
         PERFCalibrate("Overhead of PERF entry/exit");
    }
#endif

    InternalLeaveCriticalSection(pThread, init_critsec);

    if (fFirstTimeInit && 0 == retval)
    {
        _ASSERTE(NULL != pThread);
    }

    if (retval != 0 && GetLastError() == ERROR_SUCCESS)
    {
        ASSERT("returning failure, but last error not set\n");
    }

#ifdef __APPLE__
exit :
#endif // __APPLE__
    LOGEXIT("PAL_Initialize returns int %d\n", retval);
    return retval;
}

/*++
Function:
PAL_IsDebuggerPresent

Abstract:
This function should be used to determine if a debugger is attached to the process.
--*/
PALIMPORT
BOOL
PALAPI
PAL_IsDebuggerPresent()
{
#if defined(__linux__)
    BOOL debugger_present = FALSE;
    char buf[2048];

    int status_fd = open("/proc/self/status", O_RDONLY);
    if (status_fd == -1)
    {
        return FALSE;
    }
    ssize_t num_read = read(status_fd, buf, sizeof(buf) - 1);

    if (num_read > 0)
    {
        static const char TracerPid[] = "TracerPid:";
        char *tracer_pid;

        buf[num_read] = '\0';
        tracer_pid = strstr(buf, TracerPid);
        if (tracer_pid)
        {
            debugger_present = !!atoi(tracer_pid + sizeof(TracerPid) - 1);
        }
    }

    close(status_fd);

    return debugger_present;
#elif defined(__APPLE__) || defined(__FreeBSD__)
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid() };
    int ret = sysctl(mib, sizeof(mib)/sizeof(*mib), &info, &size, NULL, 0);

    if (ret == 0)
#if defined(__APPLE__)
        return ((info.kp_proc.p_flag & P_TRACED) != 0);
#else // __FreeBSD__
        return ((info.ki_flag & P_TRACED) != 0);
#endif

    return FALSE;
#elif defined(__NetBSD__)
    int traced;
    kvm_t *kd;
    int cnt;

    struct kinfo_proc *info;

    kd = kvm_open(NULL, NULL, NULL, KVM_NO_FILES, "kvm_open");
    if (kd == NULL)
        return FALSE;

    info = kvm_getprocs(kd, KERN_PROC_PID, getpid(), &cnt);
    if (info == NULL || cnt < 1)
    {
        kvm_close(kd);
        return FALSE;
    }

    traced = info->kp_proc.p_slflag & PSL_TRACED;
    kvm_close(kd);

    if (traced != 0)
        return TRUE;
    else
        return FALSE;
#elif defined(__sun)
    int readResult;
    char statusFilename[64];
    snprintf(statusFilename, sizeof(statusFilename), "/proc/%d/status", getpid());
    int fd = open(statusFilename, O_RDONLY);
    if (fd == -1)
    {
        return FALSE;
    }

    pstatus_t status;
    do
    {
        readResult = read(fd, &status, sizeof(status));
    }
    while ((readResult == -1) && (errno == EINTR));

    close(fd);
    return status.pr_flttrace.word[0] != 0;
#else
    return FALSE;
#endif
}

/*++
Function:
  PALIsThreadDataInitialized

Returns TRUE if startup has reached a point where thread data is available
--*/
BOOL PALIsThreadDataInitialized()
{
    return g_fThreadDataAvailable;
}

/*++
Function:
  PALCommonCleanup

  Utility function to prepare for shutdown.

--*/
void
PALCommonCleanup()
{
    static bool cleanupDone = false;

    // Declare the beginning of shutdown
    PALSetShutdownIntent();

    if (!cleanupDone)
    {
        cleanupDone = true;

        //
        // Let the synchronization manager know we're about to shutdown
        //
        CPalSynchMgrController::PrepareForShutdown();
    }
}

BOOL PALIsShuttingDown()
{
    /* TODO: This function may be used to provide a reader/writer-like
       mechanism (or a ref counting one) to prevent PAL APIs that need to access
       PAL runtime data, from working when PAL is shutting down. Each of those API
       should acquire a read access while executing. The shutting down code would
       acquire a write lock, i.e. suspending any new incoming reader, and waiting
       for the current readers to be done. That would allow us to get rid of the
       dangerous suspend-all-other-threads at shutdown time */
    return shutdown_intent;
}

void PALSetShutdownIntent()
{
    /* TODO: See comment in PALIsShuttingDown */
    shutdown_intent = TRUE;
}

/*++
Function:
  PALInitLock

Take the initializaiton critical section (init_critsec). necessary to serialize
TerminateProcess along with PAL_Terminate and PAL_Initialize

(no parameters)

Return value :
    TRUE if critical section existed (and was acquired)
    FALSE if critical section doens't exist yet
--*/
BOOL PALInitLock(void)
{
    if(!init_critsec)
    {
        return FALSE;
    }

    CPalThread * pThread =
        (PALIsThreadDataInitialized() ? InternalGetCurrentThread() : NULL);

    InternalEnterCriticalSection(pThread, init_critsec);
    return TRUE;
}

/*++
Function:
  PALInitUnlock

Release the initialization critical section (init_critsec).

(no parameters, no return value)
--*/
void PALInitUnlock(void)
{
    if(!init_critsec)
    {
        return;
    }

    CPalThread * pThread =
        (PALIsThreadDataInitialized() ? InternalGetCurrentThread() : NULL);

    InternalLeaveCriticalSection(pThread, init_critsec);
}

/* Internal functions *********************************************************/

/*++
Function:
    INIT_IncreaseDescriptorLimit [internal]

Abstract:
    Calls setrlimit(2) to increase the maximum number of file descriptors
    this process can open.

Return value:
    TRUE if the call to setrlimit succeeded; FALSE otherwise.
--*/
static BOOL INIT_IncreaseDescriptorLimit(void)
{
#ifndef DONT_SET_RLIMIT_NOFILE
    struct rlimit rlp;
    int result;

    result = getrlimit(RLIMIT_NOFILE, &rlp);
    if (result != 0)
    {
        return FALSE;
    }
    // Set our soft limit for file descriptors to be the same
    // as the max limit.
    rlp.rlim_cur = rlp.rlim_max;
#ifdef __APPLE__
    // Based on compatibility note in setrlimit(2) manpage for OSX,
    // trim the limit to OPEN_MAX.
    if (rlp.rlim_cur > OPEN_MAX)
    {
        rlp.rlim_cur = OPEN_MAX;
    }
#endif
    result = setrlimit(RLIMIT_NOFILE, &rlp);
    if (result != 0)
    {
        return FALSE;
    }
#endif // !DONT_SET_RLIMIT_NOFILE
    return TRUE;
}
