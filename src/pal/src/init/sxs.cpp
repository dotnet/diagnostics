// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++





--*/

#include "pal/dbgmsg.h"
#include "pal/thread.hpp"
#include "pal/init.h"
#include "pal/module.h"
#include "pal/process.h"

using namespace CorUnix;

SET_DEFAULT_DEBUG_CHANNEL(SXS);

PAL_ERROR AllocatePalThread(CPalThread **ppThread);

/*++
Function:
  CreateCurrentThreadData

Abstract:
  This function is called by the InternalGetOrCreateCurrentThread inlined
  function to create the thread data when it is null meaning the thread has
  never been in this PAL.

Warning:
  If the allocation fails, this function asserts and exits the process.
--*/
extern "C" CPalThread *
CreateCurrentThreadData()
{
    CPalThread *pThread = NULL;

    if (PALIsThreadDataInitialized()) {
        PAL_ERROR palError = AllocatePalThread(&pThread);
        if (NO_ERROR != palError)
        {
            ASSERT("Unable to allocate pal thread: error %d - aborting\n", palError);
            PROCAbort();
        }
    }

    return pThread;
}

PAL_ERROR
AllocatePalThread(CPalThread **ppThread)
{
    CPalThread *pThread = NULL;
    PAL_ERROR palError;

    palError = CreateThreadData(&pThread);
    if (NO_ERROR != palError)
    {
        goto exit;
    }

exit:
    *ppThread = pThread;
    return palError;
}
