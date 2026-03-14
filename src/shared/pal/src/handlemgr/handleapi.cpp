// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    handleapi.cpp

Abstract:

    Implementation of the handle management APIs



--*/

#include "pal/handleapi.hpp"
#include "pal/handlemgr.hpp"
#include "pal/thread.hpp"
#include "pal/dbgmsg.h"
#include "pal/process.h"

using namespace CorUnix;

SET_DEFAULT_DEBUG_CHANNEL(HANDLE);

PAL_ERROR
CloseSpecialHandle(
    HANDLE hObject
    );

/*++
Function:
  CloseHandle

See MSDN doc.

Note : according to MSDN, FALSE is returned in case of error. But also
according to MSDN, closing an invalid handle raises an exception when running a
debugger [or, alternately, if a special registry key is set]. This behavior is
not required in the PAL, so we'll always return FALSE.
--*/
BOOL
PALAPI
CloseHandle(
        IN OUT HANDLE hObject)
{
    CPalThread *pThread;
    PAL_ERROR palError;

    PERF_ENTRY(CloseHandle);
    ENTRY("CloseHandle (hObject=%p) \n", hObject);

    pThread = InternalGetCurrentThread();

    palError = InternalCloseHandle(
        pThread,
        hObject
        );

    if (NO_ERROR != palError)
    {
        pThread->SetLastError(palError);
    }

    LOGEXIT("CloseHandle returns BOOL %d\n", (NO_ERROR == palError));
    PERF_EXIT(CloseHandle);
    return (NO_ERROR == palError);
}

PAL_ERROR
CorUnix::InternalCloseHandle(
    CPalThread * pThread,
    HANDLE hObject
    )
{
    PAL_ERROR palError = NO_ERROR;

    if (!HandleIsSpecial(hObject))
    {
        palError = g_pObjectManager->RevokeHandle(
            pThread,
            hObject
            );
    }
    else
    {
        palError = CloseSpecialHandle(hObject);
    }

    return palError;
}

PAL_ERROR
CloseSpecialHandle(
    HANDLE hObject
    )
{
    if ((hObject == hPseudoCurrentThread) ||
        (hObject == hPseudoCurrentProcess))
    {
        return NO_ERROR;
    }

    return ERROR_INVALID_HANDLE;
}

