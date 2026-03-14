// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// Debug.cpp
//
// Helper code for debugging.
//*****************************************************************************
//


#include "stdafx.h"
#include "utilcode.h"
#include "ex.h"
#include "corexcep.h"

#include "log.h"

#ifdef HOST_WINDOWS
extern "C" _CRTIMP int __cdecl _flushall(void);
#endif

// Global state counter to implement SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE.
Volatile<LONG> g_DbgSuppressAllocationAsserts = 0;

static void GetExecutableFileNameUtf8(SString& value)
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    SString tmp;
    WCHAR * pCharBuf = tmp.OpenUnicodeBuffer(_MAX_PATH);
    DWORD numChars = GetModuleFileNameW(0 /* Get current executable */, pCharBuf, _MAX_PATH);
    tmp.CloseBuffer(numChars);

    tmp.ConvertToUTF8(value);
}

static void DECLSPEC_NORETURN FailFastOnAssert()
{
    WRAPPER_NO_CONTRACT; // If we're calling this, we're well past caring about contract consistency!

    FlushLogging(); // make certain we get the last part of the log
#ifdef HOST_WINDOWS
    _flushall();
#else
    fflush(NULL);
#endif

    ShutdownLogging();
    RaiseFailFastException(NULL, NULL, 0);
}


VOID LogAssert(
    LPCSTR      szFile,
    int         iLine,
    LPCSTR      szExpr
)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_DEBUG_ONLY;

    // Log asserts to the stress log. Note that we can't include the szExpr b/c that
    // may not be a string literal (particularly for formatt-able asserts).
    STRESS_LOG2(LF_ASSERT, LL_ALWAYS, "ASSERT:%s:%d\n", szFile, iLine);

    SYSTEMTIME st;
#ifndef TARGET_UNIX
    GetLocalTime(&st);
#else
    GetSystemTime(&st);
#endif

    SString exename;
    GetExecutableFileNameUtf8(exename);

    LOG((LF_ASSERT,
         LL_FATALERROR,
         "FAILED ASSERT(PID %d [0x%08x], Thread: %d [0x%x]) (%lu/%lu/%lu: %02lu:%02lu:%02lu %s): File: %s, Line %d : %s\n",
         GetCurrentProcessId(),
         GetCurrentProcessId(),
         GetCurrentThreadId(),
         GetCurrentThreadId(),
         (ULONG)st.wMonth,
         (ULONG)st.wDay,
         (ULONG)st.wYear,
         1 + (( (ULONG)st.wHour + 11 ) % 12),
         (ULONG)st.wMinute,
         (ULONG)st.wSecond,
         (st.wHour < 12) ? "am" : "pm",
         szFile,
         iLine,
         szExpr));
    LOG((LF_ASSERT, LL_FATALERROR, "RUNNING EXE: %s\n", exename.GetUTF8()));
}

//*****************************************************************************
// This function is called in order to ultimately return an out of memory
// failed hresult.  But this code will check what environment you are running
// in and give an assert for running in a debug build environment.  Usually
// out of memory on a dev machine is a bogus allocation, and this allows you
// to catch such errors.  But when run in a stress environment where you are
// trying to get out of memory, assert behavior stops the tests.
//*****************************************************************************
HRESULT _OutOfMemory(LPCSTR szFile, int iLine)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_DEBUG_ONLY;

    printf("WARNING: Out of memory condition being issued from: %s, line %d\n", szFile, iLine);
    return (E_OUTOFMEMORY);
}

static const char * szLowMemoryAssertMessage = "Assert failure (unable to format)";

//*****************************************************************************
// This function will handle ignore codes and tell the user what is happening.
//*****************************************************************************
bool _DbgBreakCheck(
    LPCSTR      szFile,
    int         iLine,
    LPCUTF8     szExpr,
    BOOL        fConstrained)
{
    STATIC_CONTRACT_THROWS;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_FORBID_FAULT;
    STATIC_CONTRACT_DEBUG_ONLY;

    CONTRACT_VIOLATION(FaultNotFatal | GCViolation | TakesLockViolation);

    char formatBuffer[4096];

    SString modulePath;
    BOOL formattedMessages = FALSE;

    // If we are low on memory we cannot even format a message. If this happens we want to
    // contain the exception here but display as much information as we can about the exception.
    if (!fConstrained)
    {
        EX_TRY
        {
            GetExecutableFileNameUtf8(modulePath);

            sprintf_s(formatBuffer, sizeof(formatBuffer),
                "\nAssert failure(PID %d [0x%08x], Thread: %d [0x%04x]): %s\n"
                "    File: %s:%d\n"
                "    Image: %s\n\n",
                GetCurrentProcessId(), GetCurrentProcessId(),
                GetCurrentThreadId(), GetCurrentThreadId(),
                szExpr, szFile, iLine, modulePath.GetUTF8());

            formattedMessages = TRUE;
        }
        EX_CATCH
        {
        }
        EX_END_CATCH(SwallowAllExceptions);
    }

    // Emit assert in debug output and console for easy access.
    if (formattedMessages)
    {
        OutputDebugStringUtf8(formatBuffer);
        fprintf(stderr, "%s", formatBuffer);
    }
    else
    {
        // Note: we cannot convert to unicode or concatenate in this situation.
        OutputDebugStringUtf8(szLowMemoryAssertMessage);
        OutputDebugStringUtf8("\n");
        OutputDebugStringUtf8(szFile);
        OutputDebugStringUtf8("\n");
        OutputDebugStringUtf8(szExpr);
        OutputDebugStringUtf8("\n");
        printf("%s", szLowMemoryAssertMessage);
        printf("\n");
        printf("%s", szFile);
        printf("\n");
        printf("%s", szExpr);
        printf("\n");
    }

    LogAssert(szFile, iLine, szExpr);


    if (IsDebuggerPresent())
    {
        return true;       // like a retry
    }

    FailFastOnAssert();
    UNREACHABLE();
}

bool _DbgBreakCheckNoThrow(
    LPCSTR      szFile,
    int         iLine,
    LPCSTR      szExpr,
    BOOL        fConstrained)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_FORBID_FAULT;
    STATIC_CONTRACT_DEBUG_ONLY;

    bool failed = false;
    bool result = false;
    EX_TRY
    {
        result = _DbgBreakCheck(szFile, iLine, szExpr, fConstrained);
    }
    EX_CATCH
    {
        failed = true;
    }
    EX_END_CATCH(SwallowAllExceptions);

    if (failed)
    {
        return true;
    }
    return result;
}

VOID DebBreakHr(HRESULT hr)
{
  STATIC_CONTRACT_LEAF;
  static int i = 0;  // add some code here so that we'll be able to set a BP
  _ASSERTE(hr != (HRESULT) 0xcccccccc);
  i++;
}
void *dbgForceToMemory;     // dummy pointer that pessimises enregistration

int g_BufferLock = -1;

VOID DbgAssertDialog(const char *szFile, int iLine, const char *szExpr)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_FORBID_FAULT;
    STATIC_CONTRACT_SUPPORTS_DAC_HOST_ONLY;

    //DEBUG_ONLY_FUNCTION;

#ifdef DACCESS_COMPILE
    // In the DAC case, asserts can mean one of two things.
    // Either there is a bug in the DAC infrastructure itself (a real assert), or just
    // that the target is corrupt or being accessed at an inconsistent state (a "target
    // consistency failure").  For target consistency failures, we need a mechanism to disable them
    // (without affecting other asserts) so that we can test corrupt / inconsistent targets.

    // @dbgtodo  DAC: For now we're treating all asserts as if they are target consistency checks.
    // In the future we should differentiate the two so that real asserts continue to fire, even when
    // we expect the target to be inconsistent.  See DevDiv Bugs 31674.
    if( !DacTargetConsistencyAssertsEnabled() )
    {
        return;
    }
#endif // #ifndef DACCESS_COMPILE

    // We increment this every time we use the SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE
    // macro below.  If it is a big number it means either a lot of threads are asserting
    // or we have a recursion in the Assert logic (usually the latter).  At least with this
    // code in place, we don't get stack overflow (and the process torn down).
    // the correct fix is to avoid calling asserting when allocating memory with an assert.
    if (g_DbgSuppressAllocationAsserts > 16)
        DebugBreak();

    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;

    dbgForceToMemory = &szFile;     //make certain these args are available in the debugger
    dbgForceToMemory = &iLine;
    dbgForceToMemory = &szExpr;

    LONG lAlreadyOwned = InterlockedExchange((LPLONG)&g_BufferLock, 1);
    if (lAlreadyOwned == 1)
    {
        if (_DbgBreakCheckNoThrow(szFile, iLine, szExpr, FALSE))
        {
            _DbgBreak();
        }
    }
    else
    {
        char *szExprToDisplay = (char*)szExpr;
        if (_DbgBreakCheckNoThrow(szFile, iLine, szExprToDisplay, FALSE))
        {
            _DbgBreak();
        }

        g_BufferLock = 0;
    }
} // DbgAssertDialog
