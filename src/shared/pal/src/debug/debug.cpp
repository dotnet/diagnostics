// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    debug.c

Abstract:

    Implementation of Win32 debugging API functions.

Revision History:



--*/


#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(DEBUG); // some headers have code with asserts, so do this first

#include "pal/thread.hpp"
#include "pal/file.hpp"
#include "pal/palinternal.h"
#include "pal/process.h"
#include "pal/debug.h"
#include "pal/environ.h"
#include "pal/malloc.hpp"
#include "pal/module.h"
#include "pal/stackstring.hpp"
#include "pal/virtual.h"
#include "pal/utils.h"

#include <signal.h>
#include <unistd.h>
#include <errno.h>
#include <sys/types.h>
#include <sys/wait.h>

using namespace CorUnix;

static const char PAL_OUTPUTDEBUGSTRING[]    = "PAL_OUTPUTDEBUGSTRING";

extern "C" {

/*++
Function:
  OutputDebugStringA

See MSDN doc.
--*/
VOID
PALAPI
OutputDebugStringA(
        IN LPCSTR lpOutputString)
{
    PERF_ENTRY(OutputDebugStringA);
    ENTRY("OutputDebugStringA (lpOutputString=%p (%s))\n",
          lpOutputString ? lpOutputString : "NULL",
          lpOutputString ? lpOutputString : "NULL");

    // As we don't support debug events, we are going to output the debug string
    // to stderr instead of generating OUT_DEBUG_STRING_EVENT. It's safe to tell
    // EnvironGetenv not to make a copy of the value here since we only want to
    // check whether it exists, not actually use it.
    if ((lpOutputString != NULL) &&
        (NULL != EnvironGetenv(PAL_OUTPUTDEBUGSTRING, /* copyValue */ FALSE)))
    {
        fprintf(stderr, "%s", lpOutputString);
    }

    LOGEXIT("OutputDebugStringA returns\n");
    PERF_EXIT(OutputDebugStringA);
}

/*++
Function:
  OutputDebugStringW

See MSDN doc.
--*/
VOID
PALAPI
OutputDebugStringW(
        IN LPCWSTR lpOutputString)
{
    CHAR *lpOutputStringA;
    int strLen;

    PERF_ENTRY(OutputDebugStringW);
    ENTRY("OutputDebugStringW (lpOutputString=%p (%S))\n",
          lpOutputString ? lpOutputString: W16_NULLSTRING,
          lpOutputString ? lpOutputString: W16_NULLSTRING);

    if (lpOutputString == NULL)
    {
        OutputDebugStringA("");
        goto EXIT;
    }

    if ((strLen = WideCharToMultiByte(CP_ACP, 0, lpOutputString, -1, NULL, 0,
                                      NULL, NULL))
        == 0)
    {
        ASSERT("failed to get wide chars length\n");
        SetLastError(ERROR_INTERNAL_ERROR);
        goto EXIT;
    }

    /* strLen includes the null terminator */
    if ((lpOutputStringA = (LPSTR) InternalMalloc((strLen * sizeof(CHAR)))) == NULL)
    {
        ERROR("Insufficient memory available !\n");
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        goto EXIT;
    }

    if(! WideCharToMultiByte(CP_ACP, 0, lpOutputString, -1,
                             lpOutputStringA, strLen, NULL, NULL))
    {
        ASSERT("failed to convert wide chars to multibytes\n");
        SetLastError(ERROR_INTERNAL_ERROR);
        free(lpOutputStringA);
        goto EXIT;
    }

    OutputDebugStringA(lpOutputStringA);
    free(lpOutputStringA);

EXIT:
    LOGEXIT("OutputDebugStringW returns\n");
    PERF_EXIT(OutputDebugStringW);
}

/*++
Function:
  DebugBreak

See MSDN doc.
--*/
VOID
PALAPI
DebugBreak(
       VOID)
{
    PERF_ENTRY(DebugBreak);
    ENTRY("DebugBreak()\n");

    DBG_DebugBreak();
    
    LOGEXIT("DebugBreak returns\n");
    PERF_EXIT(DebugBreak);
}

/*++
Function:
  PAL_ProbeMemory

Abstract

Parameter
  pBuffer : address of memory to validate
  cbBuffer : size of memory region to validate
  fWriteAccess : if true, validate writable access, else just readable.

Return
  true if memory is valid, false if not.
--*/
BOOL
PALAPI
PAL_ProbeMemory(
    PVOID pBuffer,
    DWORD cbBuffer,
    BOOL fWriteAccess)
{
    int fds[2];
    int flags;

    if (pipe(fds) != 0)
    {
        ASSERT("pipe failed: errno is %d (%s)\n", errno, strerror(errno));
        return FALSE;
    }

    flags = fcntl(fds[0], F_GETFL, 0);
    fcntl(fds[0], F_SETFL, flags | O_NONBLOCK);
    
    flags = fcntl(fds[1], F_GETFL, 0);
    fcntl(fds[1], F_SETFL, flags | O_NONBLOCK);

    PVOID pEnd = (PBYTE)pBuffer + cbBuffer;
    BOOL result = TRUE;

    // Validate the first byte in the buffer, then validate the first byte on each page after that.
    while (pBuffer < pEnd)
    {
        int written = write(fds[1], pBuffer, 1);
        if (written == -1)
        {
            if (errno != EFAULT)
            {
                ASSERT("write failed: errno is %d (%s)\n", errno, strerror(errno));
            }
            result = FALSE;
            break;
        }
        else
        {
            if (fWriteAccess)
            {
                int rd = read(fds[0], pBuffer, 1);
                if (rd == -1)
                {
                    if (errno != EFAULT)
                    {
                        ASSERT("read failed: errno is %d (%s)\n", errno, strerror(errno));
                    }
                    result = FALSE;
                    break;
                }
            }
        }

        // Round to the beginning of the next page
        pBuffer = PVOID(ALIGN_DOWN((SIZE_T)pBuffer, GetVirtualPageSize()) + GetVirtualPageSize());
    }

    close(fds[0]);
    close(fds[1]);

    return result;
}

} // extern "C"
