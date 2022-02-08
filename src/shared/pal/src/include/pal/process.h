// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    include/pal/process.h

Abstract:

    Miscellaneous process related functions.

Revision History:



--*/

#ifndef _PAL_PROCESS_H_
#define _PAL_PROCESS_H_

#include "pal/palinternal.h"
#include "pal/stackstring.hpp"

#ifdef __cplusplus
extern "C"
{
#endif // __cplusplus

/* thread ID of thread that has initiated an ExitProcess (or TerminateProcess).
   this is to make sure only one thread cleans up the PAL, and also to prevent
   calls to CreateThread from succeeding once shutdown has started
   [defined in process.c]
*/
extern Volatile<LONG> terminator;

// The process and session ID of this process, so we can avoid excessive calls to getpid() and getsid().
extern DWORD gPID;
extern DWORD gSID;


/*++
Function:
  PROCGetProcessIDFromHandle

Abstract
  Return the process ID from a process handle
--*/
DWORD PROCGetProcessIDFromHandle(HANDLE hProcess);

/*++
Function:
  PROCCleanupInitialProcess

Abstract
  Cleanup all the structures for the initial process.

Parameter
  VOID

Return
  VOID

--*/
VOID PROCCleanupInitialProcess(VOID);


/*++
Function:
  PROCProcessLock

Abstract
  Enter the critical section associated to the current process
--*/
VOID PROCProcessLock(VOID);


/*++
Function:
  PROCProcessUnlock

Abstract
  Leave the critical section associated to the current process
--*/
VOID PROCProcessUnlock(VOID);

/*++
Function:
  PROCAbort()

  Aborts the process after calling the shutdown cleanup handler. This function
  should be called instead of calling abort() directly.

  Does not return
--*/
PAL_NORETURN
VOID PROCAbort();

#ifdef __cplusplus
}
#endif // __cplusplus

#endif //PAL_PROCESS_H_

