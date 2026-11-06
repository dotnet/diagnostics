// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PROCESS);
#include "pal/palinternal.h"
#include "processmodules.h"
#include "processdescriptor.h"
#include "diagnosticspal.h"
#include <debugmacrosext.h>

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <sys/stat.h>
#include <unistd.h>

#ifdef __APPLE__
#include <sys/posix_sem.h>
#endif

#include "CLREventBase.h"

#include <atomic>
#include <new>
#include <semaphore.h>

#if HAVE_POLL
#include <poll.h>
#else
#include "pal/fakepoll.h"
#endif
#if HAVE_KQUEUE
#include <sys/event.h>
#endif
#ifdef __APPLE__
#define ENABLE_RUNTIME_EVENTS_OVER_PIPES
#endif

using namespace CorUnix;

// The lowest common supported semaphore length, including null character
// NetBSD-7.99.25: 15 characters
// MacOSX 10.11: 31 -- Core 1.0 RC2 compatibility
#if defined(__NetBSD__)
#define CLR_SEM_MAX_NAMELEN 15
#elif defined(__APPLE__)
#define CLR_SEM_MAX_NAMELEN PSEMNAMLEN
#elif defined(NAME_MAX)
#define CLR_SEM_MAX_NAMELEN (NAME_MAX - 4)
#else
// On Solaris, MAXNAMLEN is 512, which is higher than MAX_PATH defined by pal.h
#define CLR_SEM_MAX_NAMELEN MAX_PATH
#endif

static_assert(CLR_SEM_MAX_NAMELEN <= MAX_PATH);


#pragma pack(push,1)
// When creating the semaphore name on Mac running in a sandbox, We reference this structure as a byte array
// in order to encode its data into a string. Its important to make sure there is no padding between the fields
// and also at the end of the buffer. Hence, this structure is defined inside a pack(1)
struct UnambiguousProcessDescriptor
{
    UnambiguousProcessDescriptor()
    {
    }

    UnambiguousProcessDescriptor(DWORD processId, UINT64 disambiguationKey)
    {
        Init(processId, disambiguationKey);
    }

    void Init(DWORD processId, UINT64 disambiguationKey)
    {
        m_processId = processId;
        m_disambiguationKey = disambiguationKey;
    }
    UINT64 m_disambiguationKey;
    DWORD m_processId;
};
#pragma pack(pop)

static
DWORD
StartupHelperThread(
    LPVOID p);

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static
DWORD
StartupHelperRuntimeEventsThread(
    LPVOID p);
#endif



static
void
CreateSemaphoreName(
    char semName[CLR_SEM_MAX_NAMELEN],
    LPCSTR semaphoreName,
    const UnambiguousProcessDescriptor& unambiguousProcessDescriptor,
    LPCSTR applicationGroupId);

// Build the semaphore names using the PID and a value that can be used for distinguishing
// between processes with the same PID (which ran at different times). This is to avoid
// cases where a prior process with the same PID exited abnormally without having a chance
// to clean up its semaphore.
// Note to anyone modifying these names in the future: Semaphore names on OS X are limited
// to SEM_NAME_LEN characters, including null. SEM_NAME_LEN is 31 (at least on OS X 10.11).
// NetBSD limits semaphore names to 15 characters, including null (at least up to 7.99.25).
// Keep 31 length for Core 1.0 RC2 compatibility
#if defined(__NetBSD__)
static const char* RuntimeSemaphoreNameFormat = "/clr%s%08llx";
#else
static const char* RuntimeSemaphoreNameFormat = "/clr%s%08x%016llx";
#endif

static const char* RuntimeStartupSemaphoreName = "st";
static const char* RuntimeContinueSemaphoreName = "co";

#if defined(__NetBSD__)
static uint64_t HashSemaphoreName(uint64_t a, uint64_t b)
{
    return (a ^ b) & 0xffffffff;
}
#else
#define HashSemaphoreName(a,b) a,b
#endif


#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static const char* RuntimeStartupPipeName = "st";
static const char* RuntimeContinuePipeName = "co";
#define PIPE_OPEN_RETRY_DELAY_NS 500000000 // 500 ms
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

class PAL_RuntimeStartupHelper
{
    LONG m_ref;
    std::atomic<bool> m_canceled;
    std::atomic<DWORD> m_error;
    PPAL_STARTUP_CALLBACK m_callback;
    PVOID m_parameter;
    std::atomic<DWORD> m_threadId;
    HANDLE m_threadHandle;
    CLREventBase m_threadCompleted;
    DWORD m_processId;
    char m_startupSemName[CLR_SEM_MAX_NAMELEN];
    char m_continueSemName[CLR_SEM_MAX_NAMELEN];

    // A value that, used in conjunction with the process ID, uniquely identifies a process.
    // See the format we use for debugger semaphore names for why this is necessary.
    UINT64 m_processIdDisambiguationKey;

    // Debugger waits on this semaphore and the runtime signals it on startup.
    sem_t *m_startupSem;

    // Debuggee waits on this semaphore and the debugger signals it after the startup callback
    // registered (m_callback) returns.
    sem_t *m_continueSem;

#ifdef __APPLE__
    char m_applicationGroupId[MAX_APPLICATION_GROUP_ID_LENGTH+1] = {};
#endif // __APPLE__

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
    char m_startupPipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH] = {};
    char m_continuePipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH] = {};
    std::atomic<DWORD> m_runtimeEventsThreadId;
    HANDLE m_runtimeEventsThreadHandle;
    CLREventBase m_runtimeEventsThreadCompleted;
    bool m_ownsStartupPipe = false;
    bool m_ownsContinuePipe = false;

    typedef enum
    {
        RuntimeEvent_Unknown = 0,
        RuntimeEvent_Started = 1,
        RuntimeEvent_Continue = 2,
    } RuntimeEvent;

    static void CloseFd(int fd)
    {
        if (fd != -1)
        {
            while (close(fd) < 0 && errno == EINTR);
        }
    }

    static ssize_t ReadIOFunc(int fd, void *buf, size_t count)
    {
        return read(fd, buf, count);
    }

    static ssize_t WriteIOFunc(int fd, void *buf, size_t count)
    {
        return write(fd, buf, count);
    }

    static BOOL CreatePipe(const char* name)
    {
        int result = -1;
        while ((result = mkfifo(name, S_IRWXU)) < 0 && errno == EINTR);
        if (result == -1)
        {
            if (errno == EEXIST)
            {
                unlink(name);
                while ((result = mkfifo(name, S_IRWXU)) < 0 && errno == EINTR);
            }
        }

        if (result == -1)
        {
            TRACE("mkfifo failed: errno is %d (%s)\n", errno, strerror(errno));
            return FALSE;
        }

        return TRUE;
    }

    static int OpenNonBlockingPipe(int kq, const char* name, int mode, const std::atomic<bool> *canceled)
    {
        int fd = -1;
        int retries = 0;
        int flags = mode | O_NONBLOCK;

#if defined(FD_CLOEXEC)
        flags |= O_CLOEXEC;
#endif

        while (!*canceled && fd == -1)
        {
            fd = open(name, flags);
            if (fd == -1)
            {
                if (mode == O_WRONLY && errno == ENXIO)
                {
                    PAL_nanosleep(PIPE_OPEN_RETRY_DELAY_NS);
                    continue;
                }
                else if (errno == EINTR)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }
        }

        if (fd == -1)
        {
            if (!*canceled)
            {
                TRACE("open failed: errno is %d (%s)\n", errno, strerror(errno));
            }
            else
            {
                TRACE("open canceled\n");
            }
            return -1;
        }

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
        struct kevent change;
        EV_SET(&change, fd, (mode & O_ACCMODE) == O_RDONLY ? EVFILT_READ : EVFILT_WRITE, EV_ADD | EV_DISABLE, 0, 0, NULL);
        if (kevent(kq, &change, 1, NULL, 0, NULL) == -1)
        {
            TRACE("kevent failed: errno is %d (%s)\n", errno, strerror(errno));
            CloseFd(fd);
            return -1;
        }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT

        return fd;
    }

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
    static int DoNonBlockingPipeIO(int kq, int fd, void *buf, size_t count, int timeout, ssize_t (*io_func)(int fd, void *buf, size_t count), short filter)
    {
        int result = -1;
        struct timespec timeout_spec;
        struct timespec *timeout_ptr = NULL;
        if (timeout > 0)
        {
            timeout_spec.tv_sec = timeout / 1000;
            timeout_spec.tv_nsec = (timeout % 1000) * 1000000L;
            timeout_ptr = &timeout_spec;
        }

        struct kevent change;
        EV_SET(&change, fd, filter, EV_ENABLE, 0, 0, NULL);
        if (kevent(kq, &change, 1, NULL, 0, NULL) == -1)
        {
            return -1;
        }

        while (1)
        {
            struct kevent event;
            int nev = kevent(kq, NULL, 0, &event, 1, timeout_ptr);
            if (nev == -1 && errno == EINTR)
            {
                continue;
            }
            else if (nev == 0)
            {
                // Check for timeout or EOF.
                int n = io_func(fd, buf, count);
                if (n > 0)
                {
                    result = n;
                    break;
                }
                else if (n == 0)
                {
                    // EOF - pipe closed
                    result = -2;
                    break;
                }
                else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                {
                    // Timeout.
                    result = 0;
                    break;
                }
                else
                {
                    break;
                }
            }
            else if (nev > 0)
            {
                if (event.filter == filter && event.ident == fd)
                {
                    if (event.flags & EV_EOF)
                    {
                        result = -2;
                        break;
                    }

                    int n = io_func(fd, buf, count);
                    if (n > 0)
                    {
                        result = n;
                        break;
                    }
                    else if (n == 0)
                    {
                        // EOF - pipe closed
                        result = -2;
                        break;
                    }
                    else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else
            {
                break;
            }
        }

        EV_SET(&change, fd, filter, EV_DISABLE, 0, 0, NULL);
        kevent(kq, &change, 1, NULL, 0, NULL);

        return result;
    }

    static int ReadNonBlockingPipe(int kq, int fd, void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(kq, fd, buf, count, timeout, ReadIOFunc, EVFILT_READ);
    }

    static int WriteNonBlockingPipe(int kq, int fd, const void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(kq, fd, (void *)buf, count, timeout, WriteIOFunc, EVFILT_WRITE);
    }
#else
    static int DoNonBlockingPipeIO(int fd, void *buf, size_t count, int timeout, ssize_t (*io_func)(int fd, void *buf, size_t count), short filter)
    {
        struct pollfd pfd;
        pfd.fd = fd;
        pfd.events = filter;

        while (1)
        {
            int poll_ret = poll(&pfd, 1, timeout);
            if (poll_ret > 0)
            {
                if (pfd.revents & filter)
                {
                    int n = io_func(fd, buf, count);
                    if (n > 0)
                    {
                        return n;
                    }
                    else if (n == 0)
                    {
                        // EOF - pipe closed
                        return -2;
                    }
                    else if (errno == EAGAIN || errno == EWOULDBLOCK)
                    {
                        continue;
                    }
                    else if (errno == EINTR)
                    {
                        continue;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else if (pfd.revents & (POLLERR | POLLHUP | POLLNVAL))
                {
                    return -1;
                }
            }
            else if (poll_ret == 0)
            {
                // Check for timeout or EOF.
                int n = io_func(fd, buf, count);
                if (n > 0)
                {
                    return n;
                }
                else if (n == 0)
                {
                    // EOF - pipe closed
                    return -2;
                }
                else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                {
                    // Timeout.
                    return 0;
                }
                else
                {
                    return -1;
                }
            }
            else if (errno == EINTR)
            {
                continue;
            }
            else
            {
                return -1;
            }
        }

        return -1;
    }

    static int ReadNonBlockingPipe(int kq, int fd, void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(fd, buf, count, timeout, ReadIOFunc, POLLIN);
    }

    static int
    WriteNonBlockingPipe(int kq, int fd, const void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(fd, (void *)buf, count, timeout, WriteIOFunc, POLLOUT);
    }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

#ifdef __APPLE__
    LPCSTR GetApplicationGroupId() const
    {
        return m_applicationGroupId[0] == '\0' ? nullptr : m_applicationGroupId;
    }
#else
    LPCSTR GetApplicationGroupId() const
    {
        return nullptr;
    }
#endif // __APPLE__

public:
    PAL_RuntimeStartupHelper(DWORD dwProcessId, PPAL_STARTUP_CALLBACK pfnCallback, PVOID parameter) :
        m_ref(1),
        m_canceled(false),
        m_error(NO_ERROR),
        m_callback(pfnCallback),
        m_parameter(parameter),
        m_threadId(0),
        m_threadHandle(NULL),
        m_processId(dwProcessId),
        m_startupSem(SEM_FAILED),
        m_continueSem(SEM_FAILED)
#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        , m_runtimeEventsThreadId(0)
        , m_runtimeEventsThreadHandle(NULL)
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    {
    }

    ~PAL_RuntimeStartupHelper()
    {
        CleanupProtocol();
        if (m_threadHandle != NULL)
        {
            CloseHandle(m_threadHandle);
        }
        m_threadCompleted.CloseEvent();

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        if (m_runtimeEventsThreadHandle != NULL)
        {
            CloseHandle(m_runtimeEventsThreadHandle);
        }
        m_runtimeEventsThreadCompleted.CloseEvent();
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    }

    void CleanupProtocol()
    {
        if (m_startupSem != SEM_FAILED)
        {
            sem_close(m_startupSem);
            sem_unlink(m_startupSemName);
            m_startupSem = SEM_FAILED;
        }

        if (m_continueSem != SEM_FAILED)
        {
            sem_close(m_continueSem);
            sem_unlink(m_continueSemName);
            m_continueSem = SEM_FAILED;
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        if (m_ownsStartupPipe)
        {
            unlink(m_startupPipeName);
            m_ownsStartupPipe = false;
        }
        if (m_ownsContinuePipe)
        {
            unlink(m_continuePipeName);
            m_ownsContinuePipe = false;
        }
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    }

    LONG AddRef()
    {
        LONG ref = InterlockedIncrement(&m_ref);
        return ref;
    }

    LONG Release()
    {
        LONG ref = InterlockedDecrement(&m_ref);
        if (ref == 0)
        {
            delete this;
        }
        return ref;
    }

    DWORD GetSemError()
    {
        DWORD pe;
        switch (errno)
        {
            case ENOENT:
                pe = ERROR_NOT_FOUND;
                break;
            case EACCES:
                pe = ERROR_INVALID_ACCESS;
                break;
            case EINVAL:
            case ENAMETOOLONG:
                pe = ERROR_INVALID_NAME;
                break;
            case ENOMEM:
                pe = ERROR_OUTOFMEMORY;
                break;
            case EEXIST:
                pe = ERROR_ALREADY_EXISTS;
                break;
            case ENOSPC:
                pe = ERROR_TOO_MANY_SEMAPHORES;
                break;
            default:
                pe = ERROR_INVALID_PARAMETER;
                break;
        }
        return pe;
    }

    DWORD Register(LPCWSTR lpApplicationGroupId)
    {
        DWORD pe = NO_ERROR;
        BOOL ret;
        UnambiguousProcessDescriptor unambiguousProcessDescriptor;

#ifdef __APPLE__
        if (lpApplicationGroupId != NULL)
        {
            /* Convert to ASCII */
            int applicationGroupIdLength = WideCharToMultiByte(CP_ACP, 0, lpApplicationGroupId, -1, m_applicationGroupId, sizeof(m_applicationGroupId), NULL, NULL);
            if (applicationGroupIdLength == 0)
            {
                pe = GetLastError();
                TRACE("applicationGroupId: Failed to convert to multibyte string (%u)\n", pe);
                if (pe == ERROR_INSUFFICIENT_BUFFER)
                {
                    pe = ERROR_BAD_LENGTH;
                }
                goto exit;
            }
        }
        else
        {
            // Indicate that group ID is not being used
            m_applicationGroupId[0] = '\0';
        }
#endif // __APPLE__

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        PAL_GetTransportPipeName(m_startupPipeName, m_processId, GetApplicationGroupId(), RuntimeStartupPipeName);
        PAL_GetTransportPipeName(m_continuePipeName, m_processId, GetApplicationGroupId(), RuntimeContinuePipeName);

        TRACE("PAL_RuntimeStartupHelper.Register creating startup '%s' continue '%s' pipes\n", m_startupPipeName, m_continuePipeName);

#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

        // See semaphore name format for details about this value. We store it so that
        // it can be used by the cleanup code that removes the semaphore with sem_unlink.
        ret = GetProcessIdDisambiguationKey(m_processId, &m_processIdDisambiguationKey);

        // If GetProcessIdDisambiguationKey failed for some reason, it should set the value
        // to 0. We expect that anyone else opening the semaphore name will also fail and thus
        // will also try to use 0 as the value.
        _ASSERTE(ret == TRUE || m_processIdDisambiguationKey == 0);

        unambiguousProcessDescriptor.Init(m_processId, m_processIdDisambiguationKey);
        CreateSemaphoreName(m_startupSemName, RuntimeStartupSemaphoreName, unambiguousProcessDescriptor, GetApplicationGroupId());
        CreateSemaphoreName(m_continueSemName, RuntimeContinueSemaphoreName, unambiguousProcessDescriptor, GetApplicationGroupId());

        TRACE("PAL_RuntimeStartupHelper.Register creating startup '%s' continue '%s' semaphores\n", m_startupSemName, m_continueSemName);

        // Create the continue semaphore first so we don't race with PAL_NotifyRuntimeStarted. This open will fail if another
        // debugger is trying to attach to this process because the name will already exist.
        m_continueSem = sem_open(m_continueSemName, O_CREAT | O_EXCL, S_IRWXU, 0);
        if (m_continueSem == SEM_FAILED)
        {
            TRACE("sem_open(continue) failed: errno is %d (%s)\n", errno, strerror(errno));
            pe = GetSemError();
            goto exit;
        }

        // Create the debuggee startup semaphore so the runtime (debuggee) knows to wait for a debugger connection.
        m_startupSem = sem_open(m_startupSemName, O_CREAT | O_EXCL, S_IRWXU, 0);
        if (m_startupSem == SEM_FAILED)
        {
            TRACE("sem_open(startup) failed: errno is %d (%s)\n", errno, strerror(errno));
            pe = GetSemError();
            goto exit;
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        m_ownsContinuePipe = CreatePipe(m_continuePipeName);
        if (!m_ownsContinuePipe)
        {
            pe = GetSemError();
            goto exit;
        }
        m_ownsStartupPipe = CreatePipe(m_startupPipeName);
        if (!m_ownsStartupPipe)
        {
            pe = GetSemError();
            goto exit;
        }

        if (!m_runtimeEventsThreadCompleted.CreateManualEventNoThrow(false))
        {
            pe = ERROR_OUTOFMEMORY;
            goto exit;
        }

        // Add a reference for the thread handler
        AddRef();
        m_runtimeEventsThreadHandle = CreateThread(
            NULL,
            0,
            ::StartupHelperRuntimeEventsThread,
            this,
            0,
            NULL);

        if (m_runtimeEventsThreadHandle == NULL)
        {
            pe = GetLastError();
            m_runtimeEventsThreadCompleted.Set();
            TRACE("CreateThread failed %d\n", pe);
            Release();
            goto exit;
        }
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

        if (!m_threadCompleted.CreateManualEventNoThrow(false))
        {
            pe = ERROR_OUTOFMEMORY;
            goto exit;
        }

        // Add a reference for the thread handler
        AddRef();
        m_threadHandle = CreateThread(
            NULL,
            0,
            ::StartupHelperThread,
            this,
            0,
            NULL);

        if (m_threadHandle == NULL)
        {
            pe = GetLastError();
            m_threadCompleted.Set();
            TRACE("CreateThread failed %d\n", pe);
            Release();
            goto exit;
        }
    exit:
        return pe;
    }

    static DWORD WaitForWorker(CLREventBase& completion)
    {
        DWORD result = completion.Wait(60 * 1000);
        if (result != WAIT_OBJECT_0)
        {
            ERROR("Waiting for startup worker completion failed: %u\n", result);
            return result == WAIT_TIMEOUT ? ERROR_TIMEOUT : ERROR_GEN_FAILURE;
        }
        return NO_ERROR;
    }

    DWORD Unregister()
    {
        m_canceled = true;
        DWORD error = NO_ERROR;

        // Tell the runtime to continue
        if (m_continueSem != SEM_FAILED && sem_post(m_continueSem) != 0)
        {
            error = GetSemError();
            ERROR("sem_post(continueSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        // Tell the worker thread to continue
        if (m_startupSem != SEM_FAILED && sem_post(m_startupSem) != 0)
        {
            error = GetSemError();
            ERROR("sem_post(startupSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        // Don't need to wait for the worker threads if unregister called on it
        if (m_threadCompleted.IsValid() && m_threadId != GetCurrentThreadId())
        {
            DWORD result = WaitForWorker(m_threadCompleted);
            if (result != NO_ERROR)
            {
                error = result;
            }
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        if (m_runtimeEventsThreadCompleted.IsValid() && m_runtimeEventsThreadId != GetCurrentThreadId())
        {
            DWORD result = WaitForWorker(m_runtimeEventsThreadCompleted);
            if (result != NO_ERROR)
            {
                error = result;
            }
        }
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
        // Completion precedes the worker's final Release. Retire the protocol
        // here so a new registration need not wait for that final reference.
        bool onWorker = m_threadId == GetCurrentThreadId();
#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        onWorker = onWorker || m_runtimeEventsThreadId == GetCurrentThreadId();
#endif
        if (error == NO_ERROR && !onWorker)
        {
            CleanupProtocol();
        }
        return error;
    }

    //
    // There are a couple race conditions that need to be considered here:
    //
    // * On launch, between the fork and execv in the PAL's CreateProcess where the target process
    //   may contain a coreclr module image if the debugger process is running managed code. This
    //   makes just checking if the coreclr module exists not enough.
    //
    // * On launch (after the execv) or attach when the coreclr is loaded but before the DAC globals
    //   table is initialized where it is too soon to use/initialize the DAC on the debugger side.
    //
    // They are both fixed by check if the one of transport pipe files has been created.
    //
    bool IsCoreClrProcessReady()
    {
        char pipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH];

        PAL_GetTransportPipeName(pipeName, m_processId, GetApplicationGroupId(), "in");

        struct stat buf;
        if (stat(pipeName, &buf) == 0)
        {
            TRACE("IsCoreClrProcessReady: stat(%s) SUCCEEDED\n", pipeName);
            return true;
        }
        TRACE("IsCoreClrProcessReady: stat(%s) FAILED: errno is %d (%s)\n", pipeName, errno, strerror(errno));
        return false;
    }

    DWORD InvokeStartupCallback()
    {
        ProcessModules *listHead = NULL;
        DWORD pe = NO_ERROR;
        DWORD count;

        if (m_canceled)
        {
            goto exit;
        }

        // Enumerate all the modules in the process and invoke the callback
        // for the coreclr module if found.
        listHead = CreateProcessModules(m_processId, &count);
        if (listHead == NULL)
        {
            ERROR("CreateProcessModules failed for pid %d\n", m_processId);
            pe = ERROR_INVALID_PARAMETER;
            goto exit;
        }

        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            bool found = false;

            PAL_CPP_TRY
            {
                found = m_callback(entry->GetName(), entry->GetBaseAddress(), m_parameter);
            }
            PAL_CPP_CATCH_ALL
            {
                ERROR("InvokeStartupCallback exception in callback %p %s\n", entry->GetBaseAddress(), entry->GetName());
                pe = ERROR_ACCESS_DENIED;
                goto exit;
            }
            PAL_CPP_ENDTRY

            if (found)
            {
                TRACE("InvokeStartupCallback found module %p %s\n", entry->GetBaseAddress(), entry->GetName());
                break;
            }
        }

    exit:
        // Wake up the runtime
        if (sem_post(m_continueSem) != 0)
        {
            ASSERT("sem_post(continueSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }
        if (listHead != NULL)
        {
            DestroyProcessModules(listHead);
        }
        return pe;
    }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
    void StartupHelperRuntimeEventsThread()
    {
        m_runtimeEventsThreadId = GetCurrentThreadId();
        DWORD pe = NO_ERROR;
        int offset = 0;
        int kq = -1;
        int continuePipeFd = -1;
        int startupPipeFd = -1;

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
        kq = kqueue();
        if (kq == -1)
        {
            TRACE("StartupHelperRuntimeEventsThread: kqueue() failed: %d (%s)\n", errno, strerror(errno));
            goto exit;
        }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT

        TRACE("StartupHelperRuntimeEventsThread: opening continue '%s' pipe\n", m_continuePipeName);

        continuePipeFd = OpenNonBlockingPipe(kq, m_continuePipeName, O_WRONLY, &m_canceled);
        if (continuePipeFd == -1)
        {
            if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled opening continue pipe\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: failed opening continue pipe\n");
            }
            goto exit;
        }

        TRACE("StartupHelperRuntimeEventsThread: opening startup '%s' pipe\n", m_startupPipeName);

        startupPipeFd = OpenNonBlockingPipe(kq, m_startupPipeName, O_RDONLY, &m_canceled);
        if (startupPipeFd == -1)
        {
            if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled opening startup pipe\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: failed opening startup pipe\n");
            }
            goto exit;
        }

        TRACE("StartupHelperRuntimeEventsThread: waiting on started event\n");

        {
            unsigned char event = (unsigned char)RuntimeEvent_Unknown;
            unsigned char *buffer = &event;
            int bytesToRead = sizeof(event);
            int bytesRead = 0;

            do
            {
                bytesRead = ReadNonBlockingPipe(kq, startupPipeFd, buffer + offset, bytesToRead - offset, 1000);
                if (bytesRead > 0)
                {
                    offset += bytesRead;
                }
                else if (bytesRead == 0)
                {
                    // Timeout.
                    continue;
                }
                else if (bytesRead == -2 && offset == 0)
                {
                    // Not connected yet, retry.
                    continue;
                }
                else
                {
                    // Error or EOF
                    break;
                }
            }
            while (!m_canceled && offset < bytesToRead);

            if (offset == bytesToRead && event == (unsigned char)RuntimeEvent_Started)
            {
                TRACE("StartupHelperRuntimeEventsThread: received started event\n");
            }
            else if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled waiting for started event\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: received invalid event\n");
                m_error = ERROR_INVALID_PARAMETER;
            }

            sem_post(m_startupSem);
        }

        TRACE("StartupHelperRuntimeEventsThread: waiting on debugger\n");

        while (sem_wait(m_continueSem) != 0)
        {
            if (EINTR == errno)
            {
                TRACE("StartupHelperRuntimeEventsThread: sem_wait() failed with EINTR; re-waiting\n");
                continue;
            }
            TRACE("StartupHelperRuntimeEventsThread: sem_wait(contniue) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        TRACE("StartupHelperRuntimeEventsThread: sending continue event\n");

        {
            unsigned char event = (unsigned char)RuntimeEvent_Continue;
            unsigned char *buffer = &event;
            int bytesToWrite = sizeof(event);
            int bytesWritten = 0;

            offset = 0;
            do
            {
                bytesWritten = WriteNonBlockingPipe(kq, continuePipeFd, buffer + offset, bytesToWrite - offset, 1000);
                if (bytesWritten > 0)
                {
                    offset += bytesWritten;
                }
            }
            while (bytesWritten > 0 && offset < bytesToWrite);

            if (offset != bytesToWrite)
            {
                TRACE("StartupHelperRuntimeEventsThread: failed sending continue event\n");
            }
        }

    exit:

        if (startupPipeFd != -1)
        {
            CloseFd(startupPipeFd);
        }

        if (continuePipeFd != -1)
        {
            CloseFd(continuePipeFd);
        }

        if (kq != -1)
        {
            CloseFd(kq);
        }
        if (!m_runtimeEventsThreadCompleted.Set())
        {
            ERROR("Failed to signal runtime events worker completion\n");
        }
    }

#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

    void StartupHelperThread()
    {
        m_threadId = GetCurrentThreadId();
        DWORD pe = NO_ERROR;

        if (IsCoreClrProcessReady())
        {
            pe = InvokeStartupCallback();
        }
        else {
            TRACE("sem_wait(startup)\n");

            // Wait until the coreclr runtime (debuggee) starts up
            while (sem_wait(m_startupSem) != 0)
            {
                if (EINTR == errno)
                {
                    TRACE("sem_wait() failed with EINTR; re-waiting");
                    continue;
                }
                TRACE("sem_wait(startup) failed: errno is %d (%s)\n", errno, strerror(errno));
                pe = GetSemError();
            }

            if (pe == NO_ERROR && m_error != NO_ERROR)
            {
                pe = m_error;
            }

            if (pe == NO_ERROR)
            {
                pe = InvokeStartupCallback();
            }
        }

        // Invoke the callback on errors
        if (pe != NO_ERROR && !m_canceled)
        {
            SetLastError(pe);
            m_callback(NULL, NULL, m_parameter);
        }
        if (!m_threadCompleted.Set())
        {
            ERROR("Failed to signal startup worker completion\n");
        }
    }
};

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static
DWORD
StartupHelperRuntimeEventsThread(LPVOID p)
{
    TRACE("StartupHelperRuntimeEventsThread: starting\n");

    PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)p;
    helper->StartupHelperRuntimeEventsThread();
    helper->Release();

    TRACE("StartupHelperRuntimeEventsThread: finished\n");

    return 0;
}
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

static
DWORD
StartupHelperThread(LPVOID p)
{
    TRACE("PAL's StartupHelperThread starting\n");

    PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)p;
    helper->StartupHelperThread();
    helper->Release();
    return 0;
}

/*++
    PAL_RegisterForRuntimeStartup

Parameters:
    dwProcessId - process id of runtime process
    lpApplicationGroupId - A string representing the application group ID of a sandboxed
                           process running in Mac. Pass NULL if the process is not
                           running in a sandbox and other platforms.
    pfnCallback - function to callback for coreclr module found
    parameter - data to pass to callback
    ppUnregisterToken - pointer to put PAL_UnregisterForRuntimeStartup token.

Return value:
    DWORD

Note:
    If the modulePath or hModule is NULL when the callback is invoked, an error occured
    and GetLastError() will return the Win32 error code.

    The callback is always invoked on a separate thread and this API returns immediately.

    Only the first coreclr module is currently supported.

--*/
DWORD
PALAPI
PAL_RegisterForRuntimeStartup(
    IN DWORD dwProcessId,
    IN LPCWSTR lpApplicationGroupId,
    IN PPAL_STARTUP_CALLBACK pfnCallback,
    IN PVOID parameter,
    OUT PVOID *ppUnregisterToken)
{
    if (ppUnregisterToken == NULL)
    {
        return ERROR_INVALID_PARAMETER;
    }
    *ppUnregisterToken = NULL;
    if (pfnCallback == NULL)
    {
        return ERROR_INVALID_PARAMETER;
    }

    PAL_RuntimeStartupHelper *helper = new (std::nothrow) PAL_RuntimeStartupHelper(dwProcessId, pfnCallback, parameter);
    if (helper == NULL)
    {
        return ERROR_OUTOFMEMORY;
    }

    // Create the debuggee startup semaphore so the runtime (debuggee) knows to wait for
    // a debugger connection.
    DWORD pe = helper->Register(lpApplicationGroupId);
    if (NO_ERROR != pe)
    {
        helper->Unregister();
        helper->Release();
        helper = NULL;
    }

    *ppUnregisterToken = helper;
    return pe;
}

/*++
    PAL_UnregisterForRuntimeStartup

    Stops/cancels startup notification. This API can be called in the startup callback. Otherwise,
    it will block until the callback thread finishes and no more callbacks will be initiated after
    this API returns.

Parameters:
    dwUnregisterToken - token from PAL_RegisterForRuntimeStartup or NULL.

Return value:
    DWORD
--*/
DWORD
PALAPI
PAL_UnregisterForRuntimeStartup(
    IN PVOID pUnregisterToken)
{
    if (pUnregisterToken != NULL)
    {
        PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)pUnregisterToken;
        DWORD error = helper->Unregister();
        helper->Release();
        return error;
    }
    return NO_ERROR;
}

#ifdef __APPLE__

// We use 7bits from each byte, so this computes the extra size we need to encode a given byte count
constexpr int GetExtraEncodedAreaSize(UINT rawByteCount)
{
    return (rawByteCount+6)/7;
}
const int SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH = GetExtraEncodedAreaSize(sizeof(UnambiguousProcessDescriptor));
const int SEMAPHORE_ENCODED_NAME_LENGTH =
    sizeof(UnambiguousProcessDescriptor) + /* For process ID + disambiguationKey */
    SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH; /* For base 255 extra encoding space */

static_assert_no_msg(MAX_APPLICATION_GROUP_ID_LENGTH
    + 1 /* For / */
    + 2 /* For ST/CO name prefix */
    + SEMAPHORE_ENCODED_NAME_LENGTH /* For encoded name string */
    + 1 /* For null terminator */
    <= CLR_SEM_MAX_NAMELEN);

// In Apple we are limited by the length of the semaphore name. However, the characters which can be used in the
// name can be anything between 1 and 255 (since 0 will terminate the string). Thus, we encode each byte b in
// unambiguousProcessDescriptor as b ? b : 1, and mark an additional bit indicating if b is 0 or not. We use 7 bits
// out of each extra byte so 1 bit will always be '1'. This will ensure that our extra bytes are never 0 which are
// invalid characters. Thus we need an extra byte for each 7 input bytes. Hence, only extra 2 bytes for the name string.
static void EncodeSemaphoreName(char *encodedSemName, const UnambiguousProcessDescriptor& unambiguousProcessDescriptor)
{
    const unsigned char *buffer = (const unsigned char *)&unambiguousProcessDescriptor;
    char *extraEncodingBits = encodedSemName + sizeof(UnambiguousProcessDescriptor);

    // Reset the extra encoding bit area
    for (int i=0; i<SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH; i++)
    {
        extraEncodingBits[i] = 0x80;
    }

    // Encode each byte in unambiguousProcessDescriptor
    for (int i=0; i<sizeof(UnambiguousProcessDescriptor); i++)
    {
        unsigned char b = buffer[i];
        encodedSemName[i] = b ? b : 1;
        extraEncodingBits[i/7] |= (b ? 0 : 1) << (i%7);
    }
}
#endif

void CreateSemaphoreName(char semName[CLR_SEM_MAX_NAMELEN], LPCSTR semaphoreName, const UnambiguousProcessDescriptor& unambiguousProcessDescriptor, LPCSTR applicationGroupId)
{
    int length = 0;

#ifdef __APPLE__
    if (applicationGroupId != nullptr)
    {
        // We assume here that applicationGroupId has been already tested for length and is less than MAX_APPLICATION_GROUP_ID_LENGTH
        length = sprintf_s(semName, CLR_SEM_MAX_NAMELEN, "%s/%s", applicationGroupId, semaphoreName);
        _ASSERTE(length > 0 && length < CLR_SEM_MAX_NAMELEN);

        EncodeSemaphoreName(semName+length, unambiguousProcessDescriptor);
        length += SEMAPHORE_ENCODED_NAME_LENGTH;
        semName[length] = 0;
    }
    else
#endif // __APPLE__
    {
        length = sprintf_s(
            semName,
            CLR_SEM_MAX_NAMELEN,
            RuntimeSemaphoreNameFormat,
            semaphoreName,
            HashSemaphoreName(unambiguousProcessDescriptor.m_processId, unambiguousProcessDescriptor.m_disambiguationKey));
    }

    _ASSERTE(length > 0 && length < CLR_SEM_MAX_NAMELEN );
}
