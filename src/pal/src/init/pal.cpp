// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

/*++



Module Name:

    init/pal.cpp

Abstract:

    Implementation of PAL exported functions not part of the Win32 API.



--*/

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PAL); // some headers have code with asserts, so do this first

#include "pal/thread.hpp"
#include "pal/file.hpp"
#include "pal/map.hpp"
#include "../objmgr/shmobjectmanager.hpp"
#include "pal/palinternal.h"
#include "pal/process.h"
#include "pal/module.h"
#include "pal/virtual.h"
#include "pal/misc.h"
#include "pal/environ.h"
#include "pal/utils.h"
#include "pal/locale.h"
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
#endif

#include <algorithm>

using namespace CorUnix;

//
// $$TODO The C++ compiler doesn't like pal/cruntime.h so duplicate the
// necessary prototype here
//

extern "C" BOOL CRTInitStdStreams( void );

Volatile<INT> init_count = 0;
static BOOL g_fThreadDataAvailable = FALSE;
static pthread_mutex_t init_critsec_mutex = PTHREAD_MUTEX_INITIALIZER;

// The default minimum stack size
SIZE_T g_defaultStackSize = 0;

/* critical section to protect access to init_count. This is allocated on the
   very first PAL_Initialize call, and is freed afterward. */
static PCRITICAL_SECTION init_critsec = NULL;

static int Initialize(int argc, const char *const argv[], DWORD flags);
static BOOL INIT_IncreaseDescriptorLimit(void);

// Process and session ID of this process.
DWORD gPID = (DWORD) -1;
DWORD gSID = (DWORD) -1;

//
// Key used for associating CPalThread's with the underlying pthread
// (through pthread_setspecific)
//
pthread_key_t CorUnix::thObjKey;

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

#ifdef ENSURE_PRIMARY_STACK_SIZE
/*++
Function:
  EnsureStackSize

Abstract:
  This fixes a problem on MUSL where the initial stack size reported by the
  pthread_attr_getstack is about 128kB, but this limit is not fixed and
  the stack can grow dynamically. The problem is that it makes the 
  functions ReflectionInvocation::[Try]EnsureSufficientExecutionStack 
  to fail for real life scenarios like e.g. compilation of corefx.
  Since there is no real fixed limit for the stack, the code below
  ensures moving the stack limit to a value that makes reasonable
  real life scenarios work.

--*/
__attribute__((noinline,optnone))
void
EnsureStackSize(SIZE_T stackSize)
{
    volatile uint8_t *s = (uint8_t *)_alloca(stackSize);
    *s = 0;
}
#endif // ENSURE_PRIMARY_STACK_SIZE

/*++
Function:
  InitializeDefaultStackSize

Abstract:
  Initializes the default stack size. 

--*/
void
InitializeDefaultStackSize()
{
    char* defaultStackSizeStr = getenv("COMPlus_DefaultStackSize");
    if (defaultStackSizeStr != NULL)
    {
        errno = 0;
        // Like all numeric values specific by the COMPlus_xxx variables, it is a 
        // hexadecimal string without any prefix.
        long int size = strtol(defaultStackSizeStr, NULL, 16);

        if (errno == 0)
        {
            g_defaultStackSize = std::max(size, (long int)PTHREAD_STACK_MIN);
        }
    }

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
    bool fFirstTimeInit = false;
    int retval = -1;

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

        fFirstTimeInit = true;

        InitializeDefaultStackSize();

#ifdef ENSURE_PRIMARY_STACK_SIZE
        if (flags & PAL_INITIALIZE_ENSURE_STACK_SIZE)
        {
            EnsureStackSize(g_defaultStackSize);
        }
#endif // ENSURE_PRIMARY_STACK_SIZE

        // Initialize the TLS lookaside cache
        if (FALSE == TLSInitialize())
        {
            goto done;
        }

        // Initialize the environment.
        if (FALSE == EnvironInitialize())
        {
            goto CLEANUP0;
        }

        // Initialize debug channel settings before anything else.
        // This depends on the environment, so it must come after
        // EnvironInitialize.
        if (FALSE == DBG_init_channels())
        {
            goto CLEANUP0;
        }

        if (!INIT_IncreaseDescriptorLimit())
        {
            ERROR("Unable to increase the file descriptor limit!\n");
            // We can continue if this fails; we'll just have problems if
            // we use large numbers of threads or have many open files.
        }

        //
        // Allocate the initial thread data
        //

        palError = CreateThreadData(&pThread);
        if (NO_ERROR != palError)
        {
            ERROR("Unable to create initial thread data\n");
            goto CLEANUP2;
        }

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
            palError = ERROR_INTERNAL_ERROR;
            goto CLEANUP2;
        }

        //
        // Initialize the object manager
        //

        pshmom = InternalNew<CSharedMemoryObjectManager>();
        if (NULL == pshmom)
        {
            ERROR("Unable to allocate new object manager\n");
            palError = ERROR_OUTOFMEMORY;
            goto CLEANUP2;
        }

        palError = pshmom->Initialize();
        if (NO_ERROR != palError)
        {
            ERROR("object manager initialization failed!\n");
            InternalDelete(pshmom);
            goto CLEANUP2;
        }

        g_pObjectManager = pshmom;
    }
    else
    {
        pThread = InternalGetCurrentThread();
    }

    palError = ERROR_GEN_FAILURE;

    if (init_count == 0)
    {
        palError = ERROR_GEN_FAILURE;

        /* Initialize the File mapping critical section. */
        if (FALSE == MAPInitialize())
        {
            ERROR("Unable to initialize file mapping support\n");
            goto CLEANUP2;
        }

        /* Initialize the Virtual* functions. */
        bool initializeExecutableMemoryAllocator = (flags & PAL_INITIALIZE_EXEC_ALLOCATOR) != 0;
        if (FALSE == VIRTUALInitialize(initializeExecutableMemoryAllocator))
        {
            ERROR("Unable to initialize virtual memory support\n");
            goto CLEANUP10;
        }

        if (flags & PAL_INITIALIZE_STD_HANDLES)
        {
            /* create file objects for standard handles */
            if (!FILEInitStdHandles())
            {
                ERROR("Unable to initialize standard file handles\n");
                goto CLEANUP14;
            }
        }

        if (FALSE == CRTInitStdStreams())
        {
            ERROR("Unable to initialize CRT standard streams\n");
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
    VIRTUALCleanup();
CLEANUP10:
    MAPCleanup();
CLEANUP2:
    SHMCleanup();
CLEANUP0:
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
#elif defined(__APPLE__)
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid() };
    int ret = sysctl(mib, sizeof(mib)/sizeof(*mib), &info, &size, NULL, 0);

    if (ret == 0)
        return ((info.kp_proc.p_flag & P_TRACED) != 0);

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

/*++
Function:
  PROCAbort()

  Aborts the process after calling the shutdown cleanup handler. This function
  should be called instead of calling abort() directly.
  
  Does not return
--*/
PAL_NORETURN
VOID
PROCAbort()
{
    // Abort the process after waiting for the core dump to complete
    abort();
}

/*++
Function:
  GetCurrentProcessId

See MSDN doc.
--*/
DWORD
PALAPI
GetCurrentProcessId(
            VOID)
{
    PERF_ENTRY(GetCurrentProcessId);
    ENTRY("GetCurrentProcessId()\n" );

    LOGEXIT("GetCurrentProcessId returns DWORD %#x\n", gPID);
    PERF_EXIT(GetCurrentProcessId);
    return gPID;
}


/*++
Function:
  GetCurrentSessionId

See MSDN doc.
--*/
DWORD
PALAPI
GetCurrentSessionId(
            VOID)
{
    PERF_ENTRY(GetCurrentSessionId);
    ENTRY("GetCurrentSessionId()\n" );

    LOGEXIT("GetCurrentSessionId returns DWORD %#x\n", gSID);
    PERF_EXIT(GetCurrentSessionId);
    return gSID;
}

