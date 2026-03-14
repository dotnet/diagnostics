// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#undef _TARGET_AMD64_
#ifndef _TARGET_RISCV64_
#define _TARGET_RISCV64_
#endif

#undef TARGET_AMD64
#ifndef TARGET_RISCV64
#define TARGET_RISCV64
#endif

#include "strike.h"
#include "util.h"
#include <dbghelp.h>

#include "disasm.h"

#include "corhdr.h"
#include "cor.h"
#include "dacprivate.h"

namespace RISCV64GCDump
{
#undef TARGET_X86
#undef LIMITED_METHOD_CONTRACT
#define LIMITED_METHOD_DAC_CONTRACT ((void)0)
#define SUPPORTS_DAC ((void)0)
#define LF_GCROOTS
#define LL_INFO1000
#define LOG(x)
#define LOG_PIPTR(pObjRef, gcFlags, hCallBack)
#define DAC_ARG(x)
#include "gcdumpnonx86.cpp"
}

#if !defined(_TARGET_WIN64_)
#error This file only supports SOS targeting RISCV64 from a 64-bit debugger
#endif

#if !defined(SOS_TARGET_RISCV64)
#error This file should be used to support SOS targeting RISCV64 debuggees
#endif


void RISCV64Machine::IsReturnAddress(TADDR retAddr, TADDR* whereCalled) const
{
    *whereCalled = 0;
    _ASSERTE("RISCV64:NYI");
}

// Determine if a value is MT/MD/Obj
static void HandleValue(TADDR value)
{
    // A MethodTable?
    if (IsMethodTable(value))
    {
        NameForMT_s (value, g_mdName,mdNameLen);
        ExtOut (" (MT: %S)", g_mdName);
        return;
    }
    
    // A Managed Object?
    TADDR dwMTAddr;
    move_xp (dwMTAddr, value);
    if (IsStringObject(value))
    {
        ExtOut (" (\"");
        StringObjectContent (value, TRUE);
        ExtOut ("\")");
        return;
    }
    else if (IsMethodTable(dwMTAddr))
    {
        NameForMT_s (dwMTAddr, g_mdName,mdNameLen);
        ExtOut (" (Object: %S)", g_mdName);
        return;
    }
    
    // A MethodDesc?
    if (IsMethodDesc(value))
    {        
        NameForMD_s (value, g_mdName,mdNameLen);
        ExtOut (" (MD: %S)", g_mdName);
        return;
    }

    // A JitHelper?
    const char* name = HelperFuncName(value);
    if (name) {
        ExtOut (" (JitHelp: %s)", name);
        return;
    }

    // A call to managed code?
    // RISCV64TODO: not (yet) implemented. perhaps we don't need it at all.

    // Random symbol.
    char Symbol[1024];
    if (SUCCEEDED(g_ExtSymbols->GetNameByOffset(TO_CDADDR(value), Symbol, 1024,
                                                NULL, NULL)))
    {
        if (Symbol[0] != '\0')
        {
            ExtOut (" (%s)", Symbol);
            return;
        }
    }
    
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Unassembly a managed code.  Translating managed object,           *  
*    call.                                                             *
*                                                                      *
\**********************************************************************/
void RISCV64Machine::Unassembly (
    TADDR PCBegin, 
    TADDR PCEnd, 
    TADDR PCAskedFor, 
    TADDR GCStressCodeCopy, 
    GCEncodingInfo *pGCEncodingInfo, 
    SOSEHInfo *pEHInfo,
    BOOL bSuppressLines,
    BOOL bDisplayOffsets,
    std::function<void(ULONG*, UINT*, BYTE*)> displayIL) const
{
    _ASSERTE("RISCV64:NYI");
}

BOOL RISCV64Machine::GetExceptionContext (TADDR stack, TADDR PC, TADDR *cxrAddr, CROSS_PLATFORM_CONTEXT * cxr,
                          TADDR * exrAddr, PEXCEPTION_RECORD exr) const
{
    _ASSERTE("RISCV64:NYI");
    return FALSE;
}

///
/// Dump RISCV GCInfo table
///
void RISCV64Machine::DumpGCInfo(GCInfoToken gcInfoToken, unsigned methodSize, printfFtn gcPrintf, bool encBytes, bool bPrintHeader) const
{
    if (bPrintHeader)
    {
        ExtOut("Pointer table:\n");
    }

    RISCV64GCDump::GCDump gcDump(gcInfoToken.Version, encBytes, 5, true);
    gcDump.gcPrintf = gcPrintf;

    gcDump.DumpGCTable(dac_cast<PTR_BYTE>(gcInfoToken.Info), methodSize, 0);
}

