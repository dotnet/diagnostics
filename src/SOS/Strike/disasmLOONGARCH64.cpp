// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#undef _TARGET_AMD64_
#ifndef _TARGET_LOONGARCH64_
#define _TARGET_LOONGARCH64_
#endif

#undef TARGET_AMD64
#ifndef TARGET_LOONGARCH64
#define TARGET_LOONGARCH64
#endif

#include "strike.h"
#include "util.h"
#include <dbghelp.h>

#include "disasm.h"

#include "corhdr.h"
#include "cor.h"
#include "dacprivate.h"

namespace LOONGARCH64GCDump
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
#error This file only supports SOS targeting LOONGARCH64 from a 64-bit debugger
#endif

#if !defined(SOS_TARGET_LOONGARCH64)
#error This file should be used to support SOS targeting LOONGARCH64 debuggees
#endif


void LOONGARCH64Machine::IsReturnAddress(TADDR retAddr, TADDR* whereCalled) const
{
    *whereCalled = 0;
    _ASSERTE("LOONGARCH64:NYI");
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
    // LOONGARCH64TODO: not (yet) implemented. perhaps we don't need it at all.

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
void LOONGARCH64Machine::Unassembly (
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
    ULONG_PTR PC = PCBegin;
    char line[1024];
    ULONG lineNum;
    ULONG curLine = -1;
    WCHAR fileName[MAX_LONGPATH];
    char *ptr;
    ULONG ilPosition = 0;
    UINT ilIndentCount = 0;

    while(PC < PCEnd)
    {
        ULONG_PTR currentPC = PC;
        DisasmAndClean (PC, line, ARRAY_SIZE(line));

        if (currentPC != PCBegin)
        {
            ExtOut ("\n");
        }

        // This is the new instruction

        if (IsInterrupt())
            return;
        //
        // Print out line numbers if needed
        //
        if (!bSuppressLines &&
            SUCCEEDED(GetLineByOffset(TO_CDADDR(currentPC), &lineNum, fileName, MAX_LONGPATH)))
        {
            if (lineNum != curLine)
            {
                curLine = lineNum;
                ExtOut("\n%S @ %d:\n", fileName, lineNum);
            }
        }
        displayIL(&ilPosition, &ilIndentCount, (BYTE*)PC);

        //
        // Print out any GC information corresponding to the current instruction offset.
        //
        if (pGCEncodingInfo)
        {
            SIZE_T curOffset = (currentPC - PCBegin) + pGCEncodingInfo->hotSizeToAdd;
            pGCEncodingInfo->DumpGCInfoThrough(curOffset);
        }

        //
        // Print out any EH info corresponding to the current offset
        //
        if (pEHInfo)
        {
            pEHInfo->FormatForDisassembly(currentPC - PCBegin);
        }

        if (currentPC == PCAskedFor)
        {
            ExtOut (">>> ");
        }

        //
        // Print offsets, in addition to actual address.
        //
        if (bDisplayOffsets)
        {
            ExtOut("%04x ", currentPC - PCBegin);
        }

        // look at the disassembled bytes
        ptr = line;
        NextTerm (ptr);

        //
        // If there is gcstress info for this method, and this is a 'hlt'
        // instruction, then gcstress probably put the 'hlt' there.  Look
        // up the original instruction and print it instead.
        //

        if (   GCStressCodeCopy
            && (   !strncmp (ptr, "ffffff0f", 8)
                || !strncmp (ptr, "ffffff0e", 8)
                || !strncmp (ptr, "ffffff0d", 8)
                ))
        {
            ULONG_PTR InstrAddr = currentPC;

            //
            // Compute address into saved copy of the code, and
            // disassemble the original instruction
            //

            ULONG_PTR OrigInstrAddr = GCStressCodeCopy + (InstrAddr - PCBegin);
            ULONG_PTR OrigPC = OrigInstrAddr;

            DisasmAndClean(OrigPC, line, ARRAY_SIZE(line));

            //
            // Increment the real PC based on the size of the unmodifed
            // instruction
            //

            PC = InstrAddr + (OrigPC - OrigInstrAddr);

            //
            // Print out real code address in place of the copy address
            //

            ExtOut("%08x`%08x ", (ULONG)(InstrAddr >> 32), (ULONG)InstrAddr);

            ptr = line;
            NextTerm (ptr);

            //
            // Print out everything after the code address, and skip the
            // instruction bytes
            //

            ExtOut(ptr);

            //
            // Add an indicator that this address has not executed yet
            //

            ExtOut(" (gcstress)");
        }
        else
        {
            ExtOut (line);
        }

        // Now advance to the opcode
        NextTerm (ptr);

        if (!strncmp(ptr, "beq ", 4)
            || !strncmp(ptr, "bne ", 4)
            || !strncmp(ptr, "blt ", 4)
            || !strncmp(ptr, "bge ", 4)
            || !strncmp(ptr, "bltu ", 5)
            || !strncmp(ptr, "bgeu ", 5)
            )
        {
            char *endptr;
            NextTerm (ptr);
            NextTerm (ptr);
            NextTerm (ptr);
            ULONG_PTR value = strtoul(ptr, &endptr, 10);
            ExtOut("(0x%llx)", (value + currentPC));
        }
        else if (!strncmp(ptr, "beqz ", 5)
                || !strncmp(ptr, "bnez ", 5)
                || !strncmp(ptr, "bceqz ", 6)
                || !strncmp(ptr, "bcnez ", 6)
                )
        {
            char *endptr;
            NextTerm (ptr);
            NextTerm (ptr);
            ULONG_PTR value = strtoul(ptr, &endptr, 10);
            ExtOut("(0x%llx)", (value + currentPC));
        }
        else if (!strncmp(ptr, "b ", 2) || !strncmp(ptr, "bl ", 3))
        {
            char *endptr;
            NextTerm (ptr);
            ULONG_PTR value = strtoul(ptr, &endptr, 10);
            ExtOut("(0x%llx)", (value + currentPC));
            HandleValue(value + currentPC);
        }

    }
    ExtOut ("\n");

    //
    // Print out any "end" GC info
    //
    if (pGCEncodingInfo)
    {
        pGCEncodingInfo->DumpGCInfoThrough(PC - PCBegin);
    }

    //
    // Print out any "end" EH info (where the end address is the byte immediately following the last instruction)
    //
    if (pEHInfo)
    {
        pEHInfo->FormatForDisassembly(PC - PCBegin);
    }
}

BOOL LOONGARCH64Machine::GetExceptionContext (TADDR stack, TADDR PC, TADDR *cxrAddr, CROSS_PLATFORM_CONTEXT * cxr,
                          TADDR * exrAddr, PEXCEPTION_RECORD exr) const
{
    _ASSERTE("LOONGARCH64:NYI");
    return FALSE;
}

///
/// Dump LOONGARCH GCInfo table
///
void LOONGARCH64Machine::DumpGCInfo(GCInfoToken gcInfoToken, unsigned methodSize, printfFtn gcPrintf, bool encBytes, bool bPrintHeader) const
{
    if (bPrintHeader)
    {
        ExtOut("Pointer table:\n");
    }

    LOONGARCH64GCDump::GCDump gcDump(gcInfoToken.Version, encBytes, 5, true);
    gcDump.gcPrintf = gcPrintf;

    gcDump.DumpGCTable(dac_cast<PTR_BYTE>(gcInfoToken.Info), methodSize, 0);
}
