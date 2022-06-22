// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// ==++==
//

//
// ==--==
#include "sos.h"
#include "disasm.h"
#include <dbghelp.h>

#include "corhdr.h"
#include "cor.h"
#include "dacprivate.h"
#include "sospriv.h"
#include "corerror.h"
#include "safemath.h"

#include <psapi.h>
#include <cordebug.h>
#include <xcordebug.h>
#include <mscoree.h>
#include <tchar.h>
#include "gcinfo.h"

#ifndef STRESS_LOG
#define STRESS_LOG
#endif // STRESS_LOG
#define STRESS_LOG_READONLY
#include "stresslog.h"

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dlfcn.h>
#endif // !FEATURE_PAL

#include "coreclrhost.h"
#include <set>
#include <string>

#if defined(__APPLE__)
#include <mach-o/dyld.h>
#endif

const char * const CorElementTypeName[ELEMENT_TYPE_MAX]=
{
#define TYPEINFO(e,ns,c,s,g,ia,ip,if,im,gv)    c,
#include "cortypeinfo.h"
#undef TYPEINFO
};

const char * const CorElementTypeNamespace[ELEMENT_TYPE_MAX]=
{
#define TYPEINFO(e,ns,c,s,g,ia,ip,if,im,gv)    ns,
#include "cortypeinfo.h"
#undef TYPEINFO
};

IXCLRDataProcess *g_clrData = NULL;
ISOSDacInterface *g_sos = NULL;

#ifndef IfFailRet
#define IfFailRet(EXPR) do { Status = (EXPR); if(FAILED(Status)) { return (Status); } } while (0)
#endif

// Max number of reverted rejit versions that !dumpmd and !ip2md will print
const UINT kcMaxRevertedRejitData   = 10;
const UINT kcMaxTieredVersions      = 10;

#ifndef FEATURE_PAL

// ensure we always allocate on the process heap
void* __cdecl operator new(size_t size) throw()
{ return HeapAlloc(GetProcessHeap(), 0, size); }
void __cdecl operator delete(void* pObj) throw()
{ HeapFree(GetProcessHeap(), 0, pObj); }

void* __cdecl operator new[](size_t size) throw()
{ return HeapAlloc(GetProcessHeap(), 0, size); }
void __cdecl operator delete[](void* pObj) throw()
{ HeapFree(GetProcessHeap(), 0, pObj); }

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to get the memory address given a symbol  *
*    name.  It handles difference in symbol name between ntsd and      *
*    windbg.                                                           *
*                                                                      *
\**********************************************************************/
DWORD_PTR GetValueFromExpression(___in __in_z const char *const instr)
{
    _ASSERTE(g_pRuntime != nullptr);
    LoadRuntimeSymbols();

    std::string symbol;
    symbol.append(GetRuntimeModuleName());
    symbol.append("!");
    symbol.append(instr);

    ULONG64 dwAddr;
    const char* str = symbol.c_str();
    char name[256];

    dwAddr = 0;
    HRESULT hr = g_ExtSymbols->GetOffsetByName (str, &dwAddr);
    if (SUCCEEDED(hr))
        return (DWORD_PTR)dwAddr;
    else if (hr == S_FALSE && dwAddr)
        return (DWORD_PTR)dwAddr;

    strcpy_s (name, ARRAY_SIZE(name), str);
    char *ptr;
    if ((ptr = strstr (name, "__")) != NULL)
    {
        ptr[0] = ':';
        ptr[1] = ':';
        ptr += 2;
        while ((ptr = strstr(ptr, "__")) != NULL)
        {
            ptr[0] = ':';
            ptr[1] = ':';
            ptr += 2;
        }
        dwAddr = 0;
        hr = g_ExtSymbols->GetOffsetByName (name, &dwAddr);
        if (SUCCEEDED(hr))
            return (DWORD_PTR)dwAddr;
        else if (hr == S_FALSE && dwAddr)
            return (DWORD_PTR)dwAddr;
    }
    else if ((ptr = strstr (name, "::")) != NULL)
    {
        ptr[0] = '_';
        ptr[1] = '_';
        ptr += 2;
        while ((ptr = strstr(ptr, "::")) != NULL)
        {
            ptr[0] = '_';
            ptr[1] = '_';
            ptr += 2;
        }
        dwAddr = 0;
        hr = g_ExtSymbols->GetOffsetByName (name, &dwAddr);
        if (SUCCEEDED(hr))
            return (DWORD_PTR)dwAddr;
        else if (hr == S_FALSE && dwAddr)
            return (DWORD_PTR)dwAddr;
    }
    return 0;
}

#endif // FEATURE_PAL

void ReportOOM()
{
    ExtOut("SOS Error: Out of memory\n");
}

BOOL IsDumpFile()
{
    static int g_fDumpFile = -1;
    if (g_fDumpFile == -1) {
        ULONG Class;
        ULONG Qualifier;
        g_ExtControl->GetDebuggeeType(&Class,&Qualifier);
        if (Qualifier >= DEBUG_DUMP_SMALL)
            g_fDumpFile = 1;
        else
            g_fDumpFile = 0;
    }
    return g_fDumpFile != 0;
}

BOOL g_InMinidumpSafeMode = FALSE;

BOOL IsMiniDumpFileNODAC ()
{
#ifndef FEATURE_PAL
    ULONG Class;
    ULONG Qualifier;
    g_ExtControl->GetDebuggeeType(&Class,&Qualifier);
    if (Qualifier == DEBUG_DUMP_SMALL)
    {
        g_ExtControl->GetDumpFormatFlags(&Qualifier);
        if ((Qualifier & DEBUG_FORMAT_USER_SMALL_FULL_MEMORY) == 0)
        {
            return TRUE;
        }
    }

#endif // FEATURE_PAL
    return FALSE;
}


// We use this predicate to mean the smallest, most restrictive kind of
// minidump file. There is no heap dump, only that set of information
// gathered to make !clrstack, !threads, !help, !eeversion and !pe work.
BOOL IsMiniDumpFile ()
{
#ifndef FEATURE_PAL
    // It is okay for this to be static, because although the debugger may debug multiple
    // managed processes at once, I don't believe multiple dumpfiles of different
    // types is a scenario to worry about.
    if (IsMiniDumpFileNODAC())
    {
        // Beyond recognizing the dump type above, all we can rely on for this
        // is a flag set by the user indicating they want a safe mode minidump
        // experience. This is primarily for testing.
        return g_InMinidumpSafeMode;
    }

#endif // FEATURE_PAL
    return FALSE;
}

ULONG DebuggeeType()
{
    static ULONG Class = DEBUG_CLASS_UNINITIALIZED;
    if (Class == DEBUG_CLASS_UNINITIALIZED) {
        ULONG Qualifier;
        g_ExtControl->GetDebuggeeType(&Class,&Qualifier);
    }
    return Class;
}

const WCHAR GetTargetDirectorySeparatorW()
{
    if (IsWindowsTarget()) {
        return W('\\');
    }
    else {
        return W('/');
    }
}

#ifndef FEATURE_PAL

// Check if a file exist
BOOL FileExist (const char *filename)
{
    WIN32_FIND_DATAA FindFileData;
    HANDLE handle = FindFirstFileA(filename, &FindFileData);
    if (handle != INVALID_HANDLE_VALUE) {
        FindClose (handle);
        return TRUE;
    }
    else
        return FALSE;
}


BOOL FileExist (const WCHAR *filename)
{
    WIN32_FIND_DATAW FindFileData;
    HANDLE handle = FindFirstFileW (filename, &FindFileData);
    if (handle != INVALID_HANDLE_VALUE) {
        FindClose (handle);
        return TRUE;
    }
    else
        return FALSE;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to find out if a dll is bbt-ized          *
*                                                                      *
\**********************************************************************/
BOOL IsRetailBuild (size_t base)
{
    IMAGE_DOS_HEADER DosHeader;
    if (g_ExtData->ReadVirtual(TO_CDADDR(base), &DosHeader, sizeof(DosHeader), NULL) != S_OK)
        return FALSE;
    IMAGE_NT_HEADERS32 Header32;
    if (g_ExtData->ReadVirtual(TO_CDADDR(base + DosHeader.e_lfanew), &Header32, sizeof(Header32), NULL) != S_OK)
        return FALSE;
    // If there is no COMHeader, this can not be managed code.
    if (Header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress == 0)
        return FALSE;

    size_t debugDirAddr = base + Header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress;
    size_t nSize = Header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].Size;
    IMAGE_DEBUG_DIRECTORY debugDir;
    size_t nbytes = 0;
    while (nbytes < nSize) {
        if (g_ExtData->ReadVirtual(TO_CDADDR(debugDirAddr+nbytes), &debugDir, sizeof(debugDir), NULL) != S_OK)
            return FALSE;
        if (debugDir.Type == 0xA) {
            return TRUE;
        }
        nbytes += sizeof(debugDir);
    }
    return FALSE;
}

#endif // !FEATURE_PAL

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to read memory from the debugee's         *
*    address space.  If the initial read fails, it attempts to read    *
*    only up to the edge of the page containing "offset".              *
*                                                                      *
\**********************************************************************/
BOOL SafeReadMemory (TADDR offset, PVOID lpBuffer, ULONG cb, PULONG lpcbBytesRead)
{
    BOOL bRet = SUCCEEDED(g_ExtData->ReadVirtual(TO_CDADDR(offset), lpBuffer, cb, lpcbBytesRead));
    if (!bRet)
    {
        cb = _min(cb, (ULONG)(NextOSPageAddress(offset) - offset));
        bRet = SUCCEEDED(g_ExtData->ReadVirtual(TO_CDADDR(offset), lpBuffer, cb, lpcbBytesRead));
    }
    return bRet;
}

ULONG OSPageSize ()
{
    static ULONG pageSize = 0;
    if (pageSize == 0)
        g_ExtControl->GetPageSize(&pageSize);

    return pageSize;
}

size_t NextOSPageAddress (size_t addr)
{
    size_t pageSize = OSPageSize();
    return (addr+pageSize)&(~(pageSize-1));
}


/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to get the address of MethodDesc          *
*    given an ip address                                               *
*                                                                      *
\**********************************************************************/
void IP2MethodDesc (DWORD_PTR IP, DWORD_PTR &methodDesc, JITTypes &jitType,
                    DWORD_PTR &gcinfoAddr)
{

    CLRDATA_ADDRESS EIP = TO_CDADDR(IP);
    DacpCodeHeaderData codeHeaderData;

    methodDesc = NULL;
    gcinfoAddr = NULL;

    if (codeHeaderData.Request(g_sos, EIP) != S_OK)
    {
        return;
    }

    methodDesc = (DWORD_PTR) codeHeaderData.MethodDescPtr;
    jitType = (JITTypes) codeHeaderData.JITType;
    gcinfoAddr = (DWORD_PTR) codeHeaderData.GCInfo;
}

BOOL IsValueField (DacpFieldDescData *pFD)
{
    return (pFD->Type == ELEMENT_TYPE_VALUETYPE);
}

static DWORD_PTR ResolveByRefField(DacpFieldDescData* pFD, DWORD_PTR dwAddr, CLRDATA_ADDRESS* methodTable)
{
    if (dwAddr == 0)
        return 0;

    ToRelease<IMetaDataImport> pImport = MDImportForModule(TO_TADDR(pFD->ModuleOfType));

    PCCOR_SIGNATURE   pSignatureBlob = NULL;
    ULONG             sigBlobLength = 0;
    if(FAILED(pImport->GetFieldProps(pFD->mb, NULL, NULL, 0, NULL, NULL, &pSignatureBlob, &sigBlobLength, NULL, NULL, NULL)))
        return 0;

    SigParser sigParser(pSignatureBlob, sigBlobLength);
    sigParser.SkipExactlyOne();

    // Move past and assert the ByRef
    CorElementType etype;
    if (FAILED(sigParser.GetElemType(&etype)))
        return 0;

    _ASSERTE(etype == ELEMENT_TYPE_BYREF);

    // Get the byref's type. If this is also a byref we give up.
    if (FAILED(sigParser.GetElemType(&etype)) || etype == ELEMENT_TYPE_BYREF)
        return 0;

    // If the type was determined to be a valuetype, we need the methodtable
    // to be able to properly display it.
    if (etype == ELEMENT_TYPE_VALUETYPE)
    {
        mdToken token = mdTokenNil;
        if (FAILED(sigParser.GetToken(&token)))
            return 0;

        CLRDATA_ADDRESS methodTableMaybe = 0;
        if (FAILED(g_sos->GetMethodDescFromToken(pFD->ModuleOfType, token, &methodTableMaybe)))
            return 0;

        *methodTable = methodTableMaybe;
    }

    // The byref has been confirmed and we now have a concrete type to read.
    // Now get the target of the byref.
    DWORD_PTR tgt;
    CLRDATA_ADDRESS address = TO_CDADDR(dwAddr);
    if (FAILED(g_ExtData->ReadVirtual(dwAddr, &tgt, sizeof(DWORD_PTR), NULL)))
        return 0;

    return tgt;
}

void DisplayDataMember (DacpFieldDescData* pFD, DWORD_PTR dwAddr, BOOL fAlign=TRUE)
{
    if (dwAddr > 0)
    {
        // we must have called this function for a "real" (non-zero size) data type
        PREFIX_ASSUME(gElementTypeInfo[pFD->Type] != 0);

        DWORD_PTR dwTmp = dwAddr;
        bool bVTStatic = (pFD->bIsStatic && pFD->Type == ELEMENT_TYPE_VALUETYPE);

        if (gElementTypeInfo[pFD->Type] != NO_SIZE || bVTStatic)
        {
            union Value
            {
                char ch;
                short Short;
                DWORD_PTR ptr;
                int Int;
                unsigned int UInt;
                __int64 Int64;
                unsigned __int64 UInt64;
                float Float;
                double Double;
            } value;

            ZeroMemory(&value, sizeof(value));
            if (bVTStatic)
            {
                // static VTypes are boxed
                moveBlock (value, dwTmp, gElementTypeInfo[ELEMENT_TYPE_CLASS]);
            }
            else
            {
                moveBlock (value, dwTmp, gElementTypeInfo[pFD->Type]);
            }

            switch (pFD->Type)
            {
                case ELEMENT_TYPE_I1:
                    // there's no ANSI conformant type specifier for
                    // signed char, so use the next best thing,
                    // signed short (sign extending)
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "hd", (short)value.ch);
                    else
                        ExtOut("%d", value.ch);
                    break;
                case ELEMENT_TYPE_I2:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "hd", value.Short);
                    else
                        ExtOut("%d", value.Short);
                    break;
                case ELEMENT_TYPE_I4:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "d", value.Int);
                    else
                        ExtOut("%d", value.Int);
                    break;
                case ELEMENT_TYPE_I8:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "I64d", value.Int64);
                    else
                        ExtOut("%I64d", value.Int64);
                    break;
                case ELEMENT_TYPE_U1:
                case ELEMENT_TYPE_BOOLEAN:
                    if (fAlign)
                    // there's no ANSI conformant type specifier for
                    // unsigned char, so use the next best thing,
                    // unsigned short, not extending the sign
                        ExtOut("%" POINTERSIZE "hu", (USHORT)value.Short);
                    else
                        ExtOut("%u", value.ch);
                    break;
                case ELEMENT_TYPE_U2:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "hu", value.Short);
                    else
                        ExtOut("%u", value.Short);
                    break;
                case ELEMENT_TYPE_U4:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "u", value.UInt);
                    else
                        ExtOut("%u", value.UInt);
                    break;
                case ELEMENT_TYPE_U8:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "I64u", value.UInt64);
                    else
                        ExtOut("%I64u", value.UInt64);
                    break;
                case ELEMENT_TYPE_I:
                case ELEMENT_TYPE_U:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "p", SOS_PTR(value.ptr));
                    else
                        ExtOut("%p", SOS_PTR(value.ptr));
                    break;
                case ELEMENT_TYPE_R4:
                    ExtOut("%f", value.Float);
                    break;
                case ELEMENT_TYPE_R8:
                    ExtOut("%f", value.Double);
                    break;
                case ELEMENT_TYPE_CHAR:
                    if (fAlign)
                        ExtOut("%" POINTERSIZE "hx", value.Short);
                    else
                        ExtOut("%x", value.Short);
                    break;
                case ELEMENT_TYPE_VALUETYPE:
                    if (value.ptr)
                        DMLOut(DMLValueClass(pFD->MTOfType, dwTmp));
                    else
                        ExtOut("%p", SOS_PTR(0));
                    break;
                case ELEMENT_TYPE_BYREF:
                {
                    CLRDATA_ADDRESS methodTable = 0;
                    DWORD_PTR tgt = ResolveByRefField(pFD, value.ptr, &methodTable);
                    if (tgt)
                    {
                        if (methodTable)
                            DMLOut(DMLByRefValueClass(value.ptr, methodTable, tgt));
                        else
                            DMLOut(DMLByRefObject(value.ptr, tgt));
                    }
                    else
                    {
                        // This will display zero if that is what the value was or the
                        // supplied value if ResolveByRefField() failed.
                        ExtOut("%p", SOS_PTR(value.ptr));
                    }
                }
                    break;
                default:
                    if (value.ptr)
                        DMLOut(DMLObject(value.ptr));
                    else
                        ExtOut("%p", SOS_PTR(0));
                    break;
            }
        }
        else
        {
            if (pFD->Type == ELEMENT_TYPE_VALUETYPE)
                DMLOut(DMLValueClass(pFD->MTOfType, dwTmp));
            else
                ExtOut("%p", SOS_PTR(0));
        }
    }
    else
    {
        ExtOut("%" POINTERSIZE "s", " ");
    }
}

void GetStaticFieldPTR(DWORD_PTR* pOutPtr, DacpDomainLocalModuleData* pDLMD, DacpMethodTableData* pMTD, DacpFieldDescData* pFDD, BYTE* pFlags = 0)
{
    DWORD_PTR dwTmp;

    if (pFDD->Type == ELEMENT_TYPE_VALUETYPE
            || pFDD->Type == ELEMENT_TYPE_CLASS)
    {
        dwTmp = (DWORD_PTR) pDLMD->pGCStaticDataStart + pFDD->dwOffset;
    }
    else
    {
        dwTmp = (DWORD_PTR) pDLMD->pNonGCStaticDataStart + pFDD->dwOffset;
    }

    *pOutPtr = 0;

    if (pMTD->bIsDynamic)
    {
        ExtOut("dynamic statics NYI");
        return;
    }
    else
    {
        if (pFlags && pMTD->bIsShared)
        {
            BYTE flags;
            DWORD_PTR pTargetFlags = (DWORD_PTR) pDLMD->pClassData + RidFromToken(pMTD->cl) - 1;
            move_xp (flags, pTargetFlags);

            *pFlags = flags;
        }


        *pOutPtr = dwTmp;
    }
    return;
}

void GetDLMFlags(DacpDomainLocalModuleData* pDLMD, DacpMethodTableData* pMTD, BYTE* pFlags)
{
    if (pMTD->bIsDynamic)
    {
        ExtOut("dynamic statics NYI");
        return;
    }
    else
    {
        if (pFlags)
        {
            BYTE flags;
            DWORD_PTR pTargetFlags = (DWORD_PTR) pDLMD->pClassData + RidFromToken(pMTD->cl) - 1;
            move_xp (flags, pTargetFlags);

            *pFlags = flags;
        }
    }
    return;
}

void GetThreadStaticFieldPTR(DWORD_PTR* pOutPtr, DacpThreadLocalModuleData* pTLMD, DacpMethodTableData* pMTD, DacpFieldDescData* pFDD, BYTE* pFlags = 0)
{
    DWORD_PTR dwTmp;

    if (pFDD->Type == ELEMENT_TYPE_VALUETYPE
            || pFDD->Type == ELEMENT_TYPE_CLASS)
    {
        dwTmp = (DWORD_PTR) pTLMD->pGCStaticDataStart + pFDD->dwOffset;
    }
    else
    {
        dwTmp = (DWORD_PTR) pTLMD->pNonGCStaticDataStart + pFDD->dwOffset;
    }

    *pOutPtr = 0;

    if (pMTD->bIsDynamic)
    {
        ExtOut("dynamic thread statics NYI");
        return;
    }
    else
    {
        if (pFlags)
        {
            BYTE flags;
            DWORD_PTR pTargetFlags = (DWORD_PTR) pTLMD->pClassData + RidFromToken(pMTD->cl) - 1;
            move_xp (flags, pTargetFlags);

            *pFlags = flags;
        }

        *pOutPtr = dwTmp;
    }
    return;
}

void DisplaySharedStatic(ULONG64 dwModuleDomainID, DacpMethodTableData* pMT, DacpFieldDescData *pFD)
{
    DacpAppDomainStoreData adsData;
    if (adsData.Request(g_sos)!=S_OK)
    {
        ExtOut("Unable to get AppDomain information\n");
    }

    ArrayHolder<CLRDATA_ADDRESS> pArray = new CLRDATA_ADDRESS[adsData.DomainCount];
    if (pArray==NULL)
    {
        ReportOOM();
        return;
    }

    if (g_sos->GetAppDomainList(adsData.DomainCount,pArray, NULL)!=S_OK)
    {
        ExtOut("Unable to get array of AppDomains\n");
        return;
    }

#if defined(_TARGET_WIN64_)
    ExtOut("                                 >> Domain:Value ");
#else
    ExtOut("    >> Domain:Value ");
#endif
    // Skip the SystemDomain and SharedDomain
    for (int i = 0; i < adsData.DomainCount ; i ++)
    {
        DacpAppDomainData appdomainData;
        if (appdomainData.Request(g_sos,pArray[i])!=S_OK)
        {
            ExtOut("Unable to get AppDomain %lx\n",pArray[i]);
            return;
        }

        DacpDomainLocalModuleData vDomainLocalModule;
        if (g_sos->GetDomainLocalModuleDataFromAppDomain(appdomainData.AppDomainPtr, (int)dwModuleDomainID, &vDomainLocalModule) != S_OK)
        {
            // On .NET Core, dwModuleDomainID is the address of the DomainLocalModule.
            if (vDomainLocalModule.Request(g_sos, dwModuleDomainID) != S_OK)
            {
                DMLOut(" %s:NotInit ", DMLDomain(pArray[i]));
                continue;
            }
        }

        DWORD_PTR dwTmp;
        BYTE Flags = 0;
        GetStaticFieldPTR(&dwTmp, &vDomainLocalModule , pMT, pFD, &Flags);

        if ((Flags&1) == 0) {
            // We have not initialized this yet.
            DMLOut(" %s:NotInit ", DMLDomain(pArray[i]));
            continue;
        }
        else if (Flags & 2) {
            // We have not initialized this yet.
            DMLOut(" %s:FailInit", DMLDomain(pArray[i]));
            continue;
        }

        DMLOut(" %s:", DMLDomain(appdomainData.AppDomainPtr));
        DisplayDataMember(pFD, dwTmp, FALSE);
    }
    ExtOut(" <<\n");
}

void DisplayThreadStatic (DacpModuleData* pModule, DacpMethodTableData* pMT, DacpFieldDescData *pFD, BOOL fIsShared)
{
    SIZE_T dwModuleIndex = (SIZE_T)pModule->dwModuleIndex;
    SIZE_T dwModuleDomainID = (SIZE_T)pModule->dwModuleID;

    DacpThreadStoreData ThreadStore;
    ThreadStore.Request(g_sos);

    ExtOut("    >> Thread:Value");
    CLRDATA_ADDRESS CurThread = ThreadStore.firstThread;
    while (CurThread)
    {
        DacpThreadData vThread;
        if (vThread.Request(g_sos, CurThread) != S_OK)
        {
            ExtOut("  error getting thread %p, aborting this field\n", SOS_PTR(CurThread));
            return;
        }

        if (vThread.osThreadId != 0)
        {
            CLRDATA_ADDRESS appDomainAddr = vThread.domain;

            // Get the DLM (we need this to check the ClassInit flags).
            // It's annoying that we have to issue one request for
            // domain-neutral modules and domain-specific modules.
            DacpDomainLocalModuleData vDomainLocalModule;
            if (fIsShared)
            {
                if (g_sos->GetDomainLocalModuleDataFromAppDomain(appDomainAddr, (int)dwModuleDomainID, &vDomainLocalModule) != S_OK)
                {
                    // On .NET Core, dwModuleDomainID is the address of the DomainLocalModule.
                    if (vDomainLocalModule.Request(g_sos, dwModuleDomainID) != S_OK)
                    {
                        // Not initialized, go to next thread and continue looping
                        CurThread = vThread.nextThread;
                        continue;
                    }
                }
            }
            else
            {
                if (g_sos->GetDomainLocalModuleDataFromModule(pMT->Module, &vDomainLocalModule) != S_OK)
                {
                    // Not initialized, go to next thread
                    // and continue looping
                    CurThread = vThread.nextThread;
                    continue;
                }
            }

            // Get the TLM
            DacpThreadLocalModuleData vThreadLocalModule;
            if (g_sos->GetThreadLocalModuleData(CurThread, (int)dwModuleIndex, &vThreadLocalModule) != S_OK)
            {
                // Not initialized, go to next thread
                // and continue looping
                CurThread = vThread.nextThread;
                continue;
            }

            DWORD_PTR dwTmp;
            BYTE Flags = 0;
            GetThreadStaticFieldPTR(&dwTmp, &vThreadLocalModule, pMT, pFD, &Flags);

            if ((Flags&4) == 0)
            {
                // Not allocated, go to next thread
                // and continue looping
                CurThread = vThread.nextThread;
                continue;
            }

            Flags = 0;
            GetDLMFlags(&vDomainLocalModule, pMT, &Flags);

            if ((Flags&1) == 0)
            {
                // Not initialized, go to next thread
                // and continue looping
                CurThread = vThread.nextThread;
                continue;
            }

            ExtOut(" %x:", vThread.osThreadId);
            DisplayDataMember(pFD, dwTmp, FALSE);
        }

        // Go to next thread
        CurThread = vThread.nextThread;
    }
    ExtOut(" <<\n");
}

const char * ElementTypeName(unsigned type)
{
    switch (type) {
    case ELEMENT_TYPE_PTR:
        return "PTR";
        break;
    case ELEMENT_TYPE_BYREF:
        return "BYREF";
        break;
    case ELEMENT_TYPE_VALUETYPE:
        return "VALUETYPE";
        break;
    case ELEMENT_TYPE_CLASS:
        return "CLASS";
        break;
    case ELEMENT_TYPE_VAR:
        return "VAR";
        break;
    case ELEMENT_TYPE_ARRAY:
        return "ARRAY";
        break;
    case ELEMENT_TYPE_FNPTR:
        return "FNPTR";
        break;
    case ELEMENT_TYPE_SZARRAY:
        return "SZARRAY";
        break;
    case ELEMENT_TYPE_MVAR:
        return "MVAR";
        break;
    default:
        if ((type >= ARRAY_SIZE(CorElementTypeName)) || (CorElementTypeName[type] == NULL))
        {
            return "";
        }
        return CorElementTypeName[type];
        break;
    }
} // ElementTypeName

const char * ElementTypeNamespace(unsigned type)
{
    if ((type >= ARRAY_SIZE(CorElementTypeName)) || (CorElementTypeNamespace[type] == NULL))
    {
        return "";
    }
    return CorElementTypeNamespace[type];
}

void ComposeName_s(CorElementType Type, __out_ecount(capacity_buffer) LPSTR buffer, size_t capacity_buffer)
{
    const char *p = ElementTypeNamespace(Type);
    if ((p) && (*p != '\0'))
    {
        strcpy_s(buffer,capacity_buffer,p);
        strcat_s(buffer,capacity_buffer,".");
        strcat_s(buffer,capacity_buffer,ElementTypeName(Type));
    }
    else
    {
        strcpy_s(buffer,capacity_buffer,ElementTypeName(Type));
    }
}

// NOTE: pszName is changed
// INPUT            MAXCHARS        RETURN
// HelloThere       5               ...re
// HelloThere       8               ...There
LPWSTR FormatTypeName (__out_ecount (maxChars) LPWSTR pszName, UINT maxChars)
{
    UINT iStart = 0;
    UINT iLen = (int) _wcslen(pszName);
    if (iLen > maxChars)
    {
        iStart = iLen - maxChars;
        UINT numDots = (maxChars < 3) ? maxChars : 3;
        for (UINT i=0; i < numDots; i++)
            pszName[iStart+i] = '.';
    }
    return pszName + iStart;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to dump all fields of a managed object.   *
*    dwStartAddr specifies the beginning memory address.               *
*    bFirst is used to avoid printing header every time.               *
*                                                                      *
\**********************************************************************/
void DisplayFields(CLRDATA_ADDRESS cdaMT, DacpMethodTableData *pMTD, DacpMethodTableFieldData *pMTFD, DWORD_PTR dwStartAddr, BOOL bFirst, BOOL bValueClass)
{
    static DWORD numInstanceFields = 0;
    if (bFirst)
    {
        ExtOutIndent();
        ExtOut("%" POINTERSIZE "s %8s %8s %20s %2s %8s %" POINTERSIZE "s %s\n",
            "MT", "Field", "Offset", "Type", "VT", "Attr", "Value", "Name");
        numInstanceFields = 0;
    }

    BOOL fIsShared = pMTD->bIsShared;

    if (pMTD->ParentMethodTable)
    {
        DacpMethodTableData vParentMethTable;
        if (vParentMethTable.Request(g_sos,pMTD->ParentMethodTable) != S_OK)
        {
            ExtOut("Invalid parent MethodTable\n");
            return;
        }

        DacpMethodTableFieldData vParentMethTableFields;
        if (vParentMethTableFields.Request(g_sos,pMTD->ParentMethodTable) != S_OK)
        {
            ExtOut("Invalid parent EEClass\n");
            return;
        }

        DisplayFields(pMTD->ParentMethodTable, &vParentMethTable, &vParentMethTableFields, dwStartAddr, FALSE, bValueClass);
    }

    DWORD numStaticFields = 0;
    CLRDATA_ADDRESS dwAddr = pMTFD->FirstField;
    DacpFieldDescData vFieldDesc;

    // Get the module name
    DacpModuleData module;
    if (module.Request(g_sos, pMTD->Module)!=S_OK)
        return;

    ToRelease<IMetaDataImport> pImport = MDImportForModule(&module);

    while (numInstanceFields < pMTFD->wNumInstanceFields
           || numStaticFields < pMTFD->wNumStaticFields)
    {
        if (IsInterrupt())
            return;

        ExtOutIndent ();

        if ((vFieldDesc.Request(g_sos, dwAddr)!=S_OK) ||
            (vFieldDesc.Type >= ELEMENT_TYPE_MAX))
        {
            ExtOut("Unable to display fields\n");
            return;
        }
        dwAddr = vFieldDesc.NextField;

        DWORD offset = vFieldDesc.dwOffset;
        if(!((vFieldDesc.bIsThreadLocal || vFieldDesc.bIsContextLocal || fIsShared) && vFieldDesc.bIsStatic))
        {
            if (!bValueClass)
            {
                offset += sizeof(BaseObject);
            }
        }

        DMLOut("%s %8x %8x ", DMLMethodTable(vFieldDesc.MTOfType),
                 TokenFromRid(vFieldDesc.mb, mdtFieldDef),
                 offset);

        char ElementName[mdNameLen];
        if ((vFieldDesc.Type == ELEMENT_TYPE_VALUETYPE ||
            vFieldDesc.Type == ELEMENT_TYPE_CLASS) && vFieldDesc.MTOfType)
        {
            NameForMT_s((DWORD_PTR)vFieldDesc.MTOfType, g_mdName, mdNameLen);
            ExtOut("%20.20S ", FormatTypeName(g_mdName, 20));
        }
        else
        {
            if (vFieldDesc.Type == ELEMENT_TYPE_CLASS && vFieldDesc.TokenOfType != mdTypeDefNil)
            {
                // Get the name from Metadata!!!
                NameForToken_s(TokenFromRid(vFieldDesc.TokenOfType, mdtTypeDef), pImport, g_mdName, mdNameLen, false);
                ExtOut("%20.20S ", FormatTypeName(g_mdName, 20));
            }
            else
            {
                // If ET type from signature is different from fielddesc, then the signature one is more descriptive.
                // For example, E_T_STRING in field desc will be E_T_CLASS. In minidump's case, we won't have
                // the method table for it.
                ComposeName_s(vFieldDesc.Type != vFieldDesc.sigType ? vFieldDesc.sigType : vFieldDesc.Type, ElementName, ARRAY_SIZE(ElementName));
                ExtOut("%20.20s ", ElementName);
            }
        }

        ExtOut("%2s ", (IsElementValueType(vFieldDesc.Type)) ? "1" : "0");

        if (vFieldDesc.bIsStatic && (vFieldDesc.bIsThreadLocal || vFieldDesc.bIsContextLocal))
        {
            numStaticFields ++;
            if (fIsShared)
                ExtOut("%8s %" POINTERSIZE "s", "shared", vFieldDesc.bIsThreadLocal ? "TLstatic" : "CLstatic");
            else
                ExtOut("%8s ", vFieldDesc.bIsThreadLocal ? "TLstatic" : "CLstatic");

            NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
            ExtOut(" %S\n", g_mdName);

            if (IsMiniDumpFile())
            {
                ExtOut(" <no information>\n");
            }
            else
            {
                if (vFieldDesc.bIsThreadLocal)
                {
                    DacpModuleData vModule;
                    if (vModule.Request(g_sos,pMTD->Module) == S_OK)
                    {
                        DisplayThreadStatic(&vModule, pMTD, &vFieldDesc, fIsShared);
                    }
                }
                else if (vFieldDesc.bIsContextLocal)
                {
                    ExtOut("\nDisplay of context static variables is not implemented\n");
                }
            }

        }
        else if (vFieldDesc.bIsStatic)
        {
            numStaticFields ++;

            if (fIsShared)
            {
                ExtOut("%8s %" POINTERSIZE "s", "shared", "static");

                NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
                ExtOut(" %S\n", g_mdName);

                if (IsMiniDumpFile())
                {
                    ExtOut(" <no information>\n");
                }
                else
                {
                    DacpModuleData vModule;
                    if (vModule.Request(g_sos,pMTD->Module) == S_OK)
                    {
                        DisplaySharedStatic(vModule.dwModuleID, pMTD, &vFieldDesc);
                    }
                }
            }
            else
            {
                ExtOut("%8s ", "static");

                DacpDomainLocalModuleData vDomainLocalModule;

                // The MethodTable isn't shared, so the module must not be loaded domain neutral.  We can
                // get the specific DomainLocalModule instance without needing to know the AppDomain in advance.
                if (g_sos->GetDomainLocalModuleDataFromModule(pMTD->Module, &vDomainLocalModule) != S_OK)
                {
                    ExtOut(" <no information>\n");
                }
                else
                {
                    DWORD_PTR dwTmp;
                    GetStaticFieldPTR(&dwTmp, &vDomainLocalModule, pMTD, &vFieldDesc);
                    DisplayDataMember(&vFieldDesc, dwTmp);

                    NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
                    ExtOut(" %S\n", g_mdName);
                }
            }
        }
        else
        {
            numInstanceFields ++;

            ExtOut("%8s ", "instance");

            if (dwStartAddr > 0)
            {
                DWORD_PTR dwTmp = dwStartAddr + vFieldDesc.dwOffset + (bValueClass ? 0 : sizeof(BaseObject));
                DisplayDataMember(&vFieldDesc, dwTmp);
            }
            else
            {
                ExtOut(" %8s", " ");
            }


            NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
            ExtOut(" %S\n", g_mdName);
        }

    }

    return;
}

HRESULT GetNonSharedStaticFieldValueFromName(
    UINT64* pValue,
    DWORD_PTR moduleAddr,
    const char *typeName,
    __in_z LPCWSTR wszFieldName,
    CorElementType fieldType)
{
    HRESULT hr = S_OK;

    mdTypeDef mdType = 0;
    GetInfoFromName(moduleAddr, typeName, &mdType);
    if (mdType == 0)
    {
        return E_FAIL; // Failed to find type token
    }

    CLRDATA_ADDRESS cdaMethodTable = 0;
    if (FAILED(hr = g_sos->GetMethodDescFromToken(moduleAddr, mdType, &cdaMethodTable)) ||
        !IsValidToken(moduleAddr, mdType) ||
        cdaMethodTable == 0)
    {
        return FAILED(hr) ? hr : E_FAIL; // Invalid type token or type is not loaded yet
    }

    DacpMethodTableData vMethodTable;
    if ((hr = vMethodTable.Request(g_sos, cdaMethodTable)) != S_OK)
    {
        return FAILED(hr) ? hr : E_FAIL; // Failed to get method table data
    }
    if (vMethodTable.bIsShared)
    {
        ExtOut("    %s: %s\n", "Method table is shared (not implemented)", typeName);
        return E_NOTIMPL;
    }

    DacpMethodTableFieldData vMethodTableFields;
    if (FAILED(hr = vMethodTableFields.Request(g_sos, cdaMethodTable)))
    {
        return hr; // Failed to get field data
    }

    DacpModuleData vModule;
    if ((hr = vModule.Request(g_sos, vMethodTable.Module)) != S_OK)
    {
        return FAILED(hr) ? hr : E_FAIL; // Failed to get module data
    }

    DacpDomainLocalModuleData vDomainLocalModule;
    if ((hr = g_sos->GetDomainLocalModuleDataFromModule(vMethodTable.Module, &vDomainLocalModule)) != S_OK)
    {
        return FAILED(hr) ? hr : E_FAIL; // Failed to get domain local module data
    }

    ToRelease<IMetaDataImport> pImport = MDImportForModule(&vModule);
    CLRDATA_ADDRESS cdaField = vMethodTableFields.FirstField;
    DacpFieldDescData vFieldDesc;
    bool found = false;
    for (DWORD staticFieldIndex = 0; staticFieldIndex < vMethodTableFields.wNumStaticFields; )
    {
        if ((hr = vFieldDesc.Request(g_sos, cdaField)) != S_OK || vFieldDesc.Type >= ELEMENT_TYPE_MAX)
        {
            return FAILED(hr) ? hr : E_FAIL; // Failed to get member field desc
        }
        cdaField = vFieldDesc.NextField;

        if (!vFieldDesc.bIsStatic)
        {
            continue;
        }

        ++staticFieldIndex;

        if (vFieldDesc.Type != fieldType)
        {
            continue;
        }

        if (FAILED(hr = NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false)))
        {
            return hr; // Failed to get member field name
        }

        if (_wcscmp(g_mdName, wszFieldName) != 0)
        {
            continue;
        }

        if (vFieldDesc.bIsThreadLocal || vFieldDesc.bIsContextLocal)
        {
            ExtOut("    %s: %s.%S\n", "Static field is thread-local or context-local (not implemented)", typeName, wszFieldName);
            return E_NOTIMPL;
        }

        found = true;
        break;
    }

    if (!found)
    {
        return E_FAIL; // Static field not found
    }

    DWORD_PTR pValueAddr = 0;
    GetStaticFieldPTR(&pValueAddr, &vDomainLocalModule, &vMethodTable, &vFieldDesc);
    if (pValueAddr == 0)
    {
        return E_FAIL; // Failed to get static field address
    }

    UINT64 value = 0;
    if (FAILED(MOVEBLOCK(value, pValueAddr, gElementTypeInfo[fieldType])))
    {
        return E_FAIL; // Failed to read static field
    }

    *pValue = value;
    return S_OK;
}

// Return value: -1 = error,
//                0 = field not found,
//              > 0 = offset to field from objAddr
int GetObjFieldOffset(CLRDATA_ADDRESS cdaObj, __in_z LPCWSTR wszFieldName, BOOL bFirst)
{
    TADDR mt = NULL;
    if FAILED(GetMTOfObject(TO_TADDR(cdaObj), &mt))
        return -1;

    return GetObjFieldOffset(cdaObj, TO_CDADDR(mt), wszFieldName, bFirst);
}

// Return value: -1 = error,
//                0 = field not found,
//              > 0 = offset to field from objAddr
int GetObjFieldOffset(CLRDATA_ADDRESS cdaObj, CLRDATA_ADDRESS cdaMT, __in_z LPCWSTR wszFieldName,
                        BOOL bFirst/*=TRUE*/, DacpFieldDescData* pDacpFieldDescData/*=NULL*/)
{

#define EXITPOINT(EXPR) do { if(!(EXPR)) { return -1; } } while (0)

    DacpObjectData objData;
    DacpMethodTableData dmtd;
    DacpMethodTableFieldData vMethodTableFields;
    DacpFieldDescData vFieldDesc;
    DacpModuleData module;
    static DWORD numInstanceFields = 0; // Static due to recursion visiting parents

    if (bFirst)
    {
        numInstanceFields = 0;
    }

    EXITPOINT(objData.Request(g_sos, cdaObj) == S_OK);
    EXITPOINT(dmtd.Request(g_sos, cdaMT) == S_OK);

    if (dmtd.ParentMethodTable)
    {
        DWORD retVal = GetObjFieldOffset (cdaObj, dmtd.ParentMethodTable,
                                          wszFieldName, FALSE, pDacpFieldDescData);
        if (retVal != 0)
        {
            // return in case of error or success.
            // Fall through for field-not-found.
            return retVal;
        }
    }

    EXITPOINT (vMethodTableFields.Request(g_sos,cdaMT) == S_OK);
    EXITPOINT (module.Request(g_sos,dmtd.Module) == S_OK);

    CLRDATA_ADDRESS dwAddr = vMethodTableFields.FirstField;
    ToRelease<IMetaDataImport> pImport = MDImportForModule(&module);

    while (numInstanceFields < vMethodTableFields.wNumInstanceFields)
    {
        EXITPOINT (vFieldDesc.Request(g_sos, dwAddr) == S_OK);

        if (!vFieldDesc.bIsStatic)
        {
            DWORD offset = vFieldDesc.dwOffset + sizeof(BaseObject);
            NameForToken_s (TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
            if (_wcscmp (wszFieldName, g_mdName) == 0)
            {
                if (pDacpFieldDescData != NULL)
                {
                    *pDacpFieldDescData = vFieldDesc;
                }
                return offset;
            }
            numInstanceFields ++;
        }

        dwAddr = vFieldDesc.NextField;
    }

    // Field name not found...
    return 0;

#undef EXITPOINT
}


// Return value: -1 = error
//               -2 = not found
//             >= 0 = offset to field from cdaValue
int GetValueFieldOffset(CLRDATA_ADDRESS cdaMT, __in_z LPCWSTR wszFieldName, DacpFieldDescData* pDacpFieldDescData)
{
#define EXITPOINT(EXPR) do { if(!(EXPR)) { return -1; } } while (0)

    const int NOT_FOUND = -2;
    DacpMethodTableData dmtd;
    DacpMethodTableFieldData vMethodTableFields;
    DacpFieldDescData vFieldDesc;
    DacpModuleData module;
    static DWORD numInstanceFields = 0; // Static due to recursion visiting parents
    numInstanceFields = 0;

    EXITPOINT(vMethodTableFields.Request(g_sos, cdaMT) == S_OK);

    EXITPOINT(dmtd.Request(g_sos, cdaMT) == S_OK);
    EXITPOINT(module.Request(g_sos, dmtd.Module) == S_OK);
    if (dmtd.ParentMethodTable)
    {
        DWORD retVal = GetValueFieldOffset(dmtd.ParentMethodTable, wszFieldName, pDacpFieldDescData);
        if (retVal != (DWORD)NOT_FOUND)
        {
            // Return in case of error or success. Fall through for field-not-found.
            return retVal;
        }
    }

    CLRDATA_ADDRESS dwAddr = vMethodTableFields.FirstField;
    ToRelease<IMetaDataImport> pImport = MDImportForModule(&module);

    while (numInstanceFields < vMethodTableFields.wNumInstanceFields)
    {
        EXITPOINT(vFieldDesc.Request(g_sos, dwAddr) == S_OK);

        if (!vFieldDesc.bIsStatic)
        {
            NameForToken_s(TokenFromRid(vFieldDesc.mb, mdtFieldDef), pImport, g_mdName, mdNameLen, false);
            if (_wcscmp(wszFieldName, g_mdName) == 0)
            {
                if (pDacpFieldDescData != NULL)
                {
                    *pDacpFieldDescData = vFieldDesc;
                }
                return vFieldDesc.dwOffset;
            }
            numInstanceFields++;
        }

        dwAddr = vFieldDesc.NextField;
    }

    // Field name not found...
    return NOT_FOUND;

#undef EXITPOINT
}

// Returns an AppDomain address if AssemblyPtr is loaded into that domain only. Otherwise
// returns NULL
CLRDATA_ADDRESS IsInOneDomainOnly(CLRDATA_ADDRESS AssemblyPtr)
{
    CLRDATA_ADDRESS appDomain = NULL;

    DacpAppDomainStoreData adstore;
    if (adstore.Request(g_sos) != S_OK)
    {
        ExtOut("Unable to get appdomain store\n");
        return NULL;
    }

    size_t AllocSize;
    if (!ClrSafeInt<size_t>::multiply(sizeof(CLRDATA_ADDRESS), adstore.DomainCount, AllocSize))
    {
        ReportOOM();
        return NULL;
    }

    ArrayHolder<CLRDATA_ADDRESS> pArray = new CLRDATA_ADDRESS[adstore.DomainCount];
    if (pArray==NULL)
    {
        ReportOOM();
        return NULL;
    }

    if (g_sos->GetAppDomainList(adstore.DomainCount, pArray, NULL)!=S_OK)
    {
        ExtOut ("Failed to get appdomain list\n");
        return NULL;
    }

    for (int i = 0; i < adstore.DomainCount; i++)
    {
        if (IsInterrupt())
            return NULL;

        DacpAppDomainData dadd;
        if (dadd.Request(g_sos, pArray[i]) != S_OK)
        {
            ExtOut ("Unable to get AppDomain %p\n", SOS_PTR(pArray[i]));
            return NULL;
        }

        if (dadd.AssemblyCount)
        {
            size_t AssemblyAllocSize;
            if (!ClrSafeInt<size_t>::multiply(sizeof(CLRDATA_ADDRESS), dadd.AssemblyCount, AssemblyAllocSize))
            {
                ReportOOM();
                return NULL;
            }

            ArrayHolder<CLRDATA_ADDRESS> pAsmArray = new CLRDATA_ADDRESS[dadd.AssemblyCount];
            if (pAsmArray==NULL)
            {
                ReportOOM();
                return NULL;
            }

            if (g_sos->GetAssemblyList(dadd.AppDomainPtr,dadd.AssemblyCount,pAsmArray, NULL)!=S_OK)
            {
                ExtOut("Unable to get array of Assemblies\n");
                return NULL;
            }

            for (LONG n = 0; n < dadd.AssemblyCount; n ++)
            {
                if (IsInterrupt())
                    return NULL;

                if (AssemblyPtr == pAsmArray[n])
                {
                    if (appDomain != NULL)
                    {
                        // We have found more than one AppDomain that loaded this
                        // assembly, we must return NULL.
                        return NULL;
                    }
                    appDomain = dadd.AppDomainPtr;
                }
            }
        }
    }


    return appDomain;
}

CLRDATA_ADDRESS GetAppDomainForMT(CLRDATA_ADDRESS mtPtr)
{
    DacpMethodTableData mt;
    if (mt.Request(g_sos, mtPtr) != S_OK)
    {
        return NULL;
    }

    DacpModuleData module;
    if (module.Request(g_sos, mt.Module) != S_OK)
    {
        return NULL;
    }

    DacpAssemblyData assembly;
    if (assembly.Request(g_sos, module.Assembly) != S_OK)
    {
        return NULL;
    }

    DacpAppDomainStoreData adstore;
    if (adstore.Request(g_sos) != S_OK)
    {
        return NULL;
    }

    return (assembly.ParentDomain == adstore.sharedDomain) ?
            IsInOneDomainOnly(assembly.AssemblyPtr) :
            assembly.ParentDomain;
}

CLRDATA_ADDRESS GetAppDomain(CLRDATA_ADDRESS objPtr)
{
    CLRDATA_ADDRESS appDomain = NULL;

    DacpObjectData objData;
    if (objData.Request(g_sos,objPtr) != S_OK)
    {
        return NULL;
    }

    // First check  eeclass->module->assembly->domain.
    // Then check the object flags word
    // finally, search threads for a reference to the object, and look at the thread context.

    DacpMethodTableData mt;
    if (mt.Request(g_sos,objData.MethodTable) != S_OK)
    {
        return NULL;
    }

    DacpModuleData module;
    if (module.Request(g_sos,mt.Module) != S_OK)
    {
        return NULL;
    }

    DacpAssemblyData assembly;
    if (assembly.Request(g_sos,module.Assembly) != S_OK)
    {
        return NULL;
    }

    DacpAppDomainStoreData adstore;
    if (adstore.Request(g_sos) != S_OK)
    {
        return NULL;
    }

    if (assembly.ParentDomain == adstore.sharedDomain)
    {
        sos::Object obj(TO_TADDR(objPtr));
        ULONG value = 0;
        if (!obj.TryGetHeader(value))
        {
            return NULL;
        }

        DWORD adIndex = (value >> SBLK_APPDOMAIN_SHIFT) & SBLK_MASK_APPDOMAININDEX;
        if ( ((value & BIT_SBLK_IS_HASH_OR_SYNCBLKINDEX) != 0) || adIndex==0)
        {
            // No AppDomainID information. We'll make use of a heuristic.
            // If the assembly is in the shared domain, we can report it as
            // being in domain X if the only other domain that has the assembly
            // loaded is domain X.
            appDomain = IsInOneDomainOnly(assembly.AssemblyPtr);
            if (appDomain == NULL && ((value & BIT_SBLK_IS_HASH_OR_SYNCBLKINDEX) != 0))
            {
                if ((value & BIT_SBLK_IS_HASHCODE) == 0)
                {
                    UINT index = value & MASK_SYNCBLOCKINDEX;
                    // We have a syncblock, the appdomain ID may be in there.
                    DacpSyncBlockData syncBlockData;
                    if (syncBlockData.Request(g_sos,index) == S_OK)
                    {
                        appDomain = syncBlockData.appDomainPtr;
                    }
                }
            }
        }
        else if ((value & BIT_SBLK_IS_HASH_OR_SYNCBLKINDEX) == 0)
        {
            size_t AllocSize;
            if (!ClrSafeInt<size_t>::multiply(sizeof(CLRDATA_ADDRESS), adstore.DomainCount, AllocSize))
            {
                return NULL;
            }
            // we know we have a non-zero adIndex. Find the appdomain.
            ArrayHolder<CLRDATA_ADDRESS> pArray = new CLRDATA_ADDRESS[adstore.DomainCount];
            if (pArray==NULL)
            {
                return NULL;
            }

            if (g_sos->GetAppDomainList(adstore.DomainCount, pArray, NULL)!=S_OK)
            {
                return NULL;
            }

            for (int i = 0; i < adstore.DomainCount; i++)
            {
                DacpAppDomainData dadd;
                if (dadd.Request(g_sos, pArray[i]) != S_OK)
                {
                    return NULL;
                }
                if (dadd.dwId == adIndex)
                {
                    appDomain = pArray[i];
                    break;
                }
            }
        }
    }
    else
    {
        appDomain = assembly.ParentDomain;
    }

    return appDomain;
}

HRESULT FileNameForModule (DWORD_PTR pModuleAddr, __out_ecount (MAX_LONGPATH) WCHAR *fileName)
{
    DacpModuleData ModuleData;
    fileName[0] = L'\0';

    HRESULT hr = ModuleData.Request(g_sos, TO_CDADDR(pModuleAddr));
    if (SUCCEEDED(hr))
    {
        hr = FileNameForModule(&ModuleData,fileName);
    }

    return hr;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to find the file name given a Module.     *
*                                                                      *
\**********************************************************************/
// fileName should be at least MAX_LONGPATH
HRESULT FileNameForModule(const DacpModuleData* const pModuleData, __out_ecount(MAX_LONGPATH) WCHAR* fileName)
{
    fileName[0] = W('\0');

    HRESULT hr = S_OK;
    CLRDATA_ADDRESS dwAddr = pModuleData->PEAssembly;
    if (dwAddr == 0)
    {
        // TODO:  We have dynamic module
        return E_NOTIMPL;
    }

    CLRDATA_ADDRESS base = 0;
    hr = g_sos->GetPEFileBase(dwAddr, &base);
    if (SUCCEEDED(hr))
    {
        hr = g_sos->GetPEFileName(dwAddr, MAX_LONGPATH, fileName, NULL);
        if (SUCCEEDED(hr) && fileName[0] != W('\0'))
            return hr; // done

#ifndef FEATURE_PAL
        // Try the base *
        if (base)
        {
            hr = DllsName((ULONG_PTR)base, fileName);
            if (SUCCEEDED(hr) && fileName[0] != W('\0'))
                return hr; // done
        }
#endif // !FEATURE_PAL
    }

    ToRelease<IXCLRDataModule> pModule;
    if (SUCCEEDED(g_sos->GetModule(pModuleData->Address, &pModule)))
    {
        ULONG32 nameLen = 0;
        hr = pModule->GetFileName(MAX_LONGPATH, &nameLen, fileName);
    }

    return hr;
}

void AssemblyInfo(DacpAssemblyData *pAssembly)
{
    ExtOut("ClassLoader:        %p\n", SOS_PTR(pAssembly->ClassLoader));
    if ((ULONG64)pAssembly->AssemblySecDesc != NULL)
        ExtOut("SecurityDescriptor: %p\n", SOS_PTR(pAssembly->AssemblySecDesc));
    ExtOut("  Module\n");

    ArrayHolder<CLRDATA_ADDRESS> Modules = new CLRDATA_ADDRESS[pAssembly->ModuleCount];
    if (Modules == NULL
        || g_sos->GetAssemblyModuleList(pAssembly->AssemblyPtr, pAssembly->ModuleCount, Modules, NULL) != S_OK)
    {
       ReportOOM();
       return;
    }

    for (UINT n = 0; n < pAssembly->ModuleCount; n++)
    {
        if (IsInterrupt())
        {
            return;
        }
        CLRDATA_ADDRESS ModuleAddr = Modules[n];
        DMLOut("  %s    " WIN86_8SPACES, DMLModule(ModuleAddr));
        DacpModuleData moduleData;
        if (moduleData.Request(g_sos, ModuleAddr) == S_OK)
        {
            WCHAR fileName[MAX_LONGPATH];
            FileNameForModule (&moduleData, fileName);
            if (fileName[0])
            {
                ExtOut("%S\n", fileName);
            }
            else
            {
                ExtOut("%S\n", (moduleData.bIsReflection) ? W("Dynamic Module") : W("Unknown Module"));
            }
        }
        else
        {
            ExtOut("Request module data FAILED\n");
        }
    }
}

const char *GetStageText(DacpAppDomainDataStage stage)
{
    switch(stage)
    {
        case STAGE_CREATING:
            return "CREATING";
        case STAGE_READYFORMANAGEDCODE:
            return "READYFORMANAGEDCODE";
        case STAGE_ACTIVE:
            return "ACTIVE";
        case STAGE_OPEN:
            return "OPEN";
        case STAGE_UNLOAD_REQUESTED:
            return "UNLOAD_REQUESTED";
        case STAGE_EXITING:
            return "EXITING";
        case STAGE_EXITED:
            return "EXITED";
        case STAGE_FINALIZING:
            return "FINALIZING";
        case STAGE_FINALIZED:
            return "FINALIZED";
        case STAGE_HANDLETABLE_NOACCESS:
            return "HANDLETABLE_NOACCESS";
        case STAGE_CLEARED:
            return "CLEARED";
        case STAGE_COLLECTED:
            return "COLLECTED";
        case STAGE_CLOSED:
            return "CLOSED";
    }
    return "UNKNOWN";
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to dump the contents of a domain.         *
*                                                                      *
\**********************************************************************/
void DomainInfo (DacpAppDomainData *pDomain)
{
    ExtOut("LowFrequencyHeap:   %p\n", SOS_PTR(pDomain->pLowFrequencyHeap));
    ExtOut("HighFrequencyHeap:  %p\n", SOS_PTR(pDomain->pHighFrequencyHeap));
    ExtOut("StubHeap:           %p\n", SOS_PTR(pDomain->pStubHeap));
    ExtOut("Stage:              %s\n", GetStageText(pDomain->appDomainStage));
    if ((ULONG64)pDomain->AppSecDesc != NULL)
        ExtOut("SecurityDescriptor: %p\n", SOS_PTR(pDomain->AppSecDesc));
    ExtOut("Name:               ");

    if (g_sos->GetAppDomainName(pDomain->AppDomainPtr, mdNameLen, g_mdName, NULL)!=S_OK)
    {
        ExtOut("Error getting AppDomain friendly name\n");
    }
    else
    {
        ExtOut("%S\n", (g_mdName[0] != L'\0') ? g_mdName : W("None"));
    }

    if (pDomain->AssemblyCount == 0)
        return;

    ArrayHolder<CLRDATA_ADDRESS> pArray = new CLRDATA_ADDRESS[pDomain->AssemblyCount];
    if (pArray==NULL)
    {
        ReportOOM();
        return;
    }

    if (g_sos->GetAssemblyList(pDomain->AppDomainPtr,pDomain->AssemblyCount,pArray, NULL)!=S_OK)
    {
        ExtOut("Unable to get array of Assemblies\n");
        return;
    }

    LONG n;
    // Assembly vAssembly;
    for (n = 0; n < pDomain->AssemblyCount; n ++)
    {
        if (IsInterrupt())
            return;

        if (n != 0)
            ExtOut("\n");

        DMLOut("Assembly:           %s", DMLAssembly(pArray[n]));
        DacpAssemblyData assemblyData;
        if (assemblyData.Request(g_sos, pArray[n], pDomain->AppDomainPtr) == S_OK)
        {
            if (assemblyData.isDynamic)
                ExtOut(" (Dynamic)");

            ExtOut(" [");
            if (g_sos->GetAssemblyName(pArray[n], mdNameLen, g_mdName, NULL) == S_OK)
                ExtOut("%S", g_mdName);
            ExtOut("]\n");

            AssemblyInfo(&assemblyData);
        }
    }

    ExtOut("\n");
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to find the name of a MethodDesc using    *
*    metadata API.                                                     *
*                                                                      *
\**********************************************************************/
BOOL NameForMD_s (DWORD_PTR pMD, __out_ecount (capacity_mdName) WCHAR *mdName, size_t capacity_mdName)
{
    mdName[0] = L'\0';
    CLRDATA_ADDRESS StartAddr = TO_CDADDR(pMD);
    DacpMethodDescData MethodDescData;

    // don't need to check for minidump file as all commands are seals
    // We also do not have EEJitManager to validate anyway.
    //
    if (!IsMiniDumpFile() && MethodDescData.Request(g_sos,StartAddr) != S_OK)
    {
        ExtOut("%p is not a MethodDesc\n", SOS_PTR(StartAddr));
        return FALSE;
    }

    if (g_sos->GetMethodDescName(StartAddr, mdNameLen, mdName, NULL) != S_OK)
    {
        wcscpy_s(mdName, capacity_mdName, W("UNKNOWN"));
        return FALSE;
    }
    return TRUE;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to find the name of a MethodTable using   *
*    metadata API.                                                     *
*                                                                      *
\**********************************************************************/
BOOL NameForMT_s(DWORD_PTR MTAddr, __out_ecount (capacity_mdName) WCHAR *mdName, size_t capacity_mdName)
{
    HRESULT hr = g_sos->GetMethodTableName(TO_CDADDR(MTAddr), (ULONG32)capacity_mdName, mdName, NULL);
    return SUCCEEDED(hr);
}

WCHAR *CreateMethodTableName(TADDR mt, TADDR cmt)
{
    bool array = false;
    WCHAR *res = NULL;

    if (mt == sos::MethodTable::GetFreeMT())
    {
        res = new WCHAR[5];
        wcscpy_s(res, 5, W("Free"));
        return res;
    }

    if (mt == sos::MethodTable::GetArrayMT() && cmt != NULL)
    {
        mt = cmt;
        array = true;
    }

    unsigned int needed = 0;
    HRESULT hr = g_sos->GetMethodTableName(mt, 0, NULL, &needed);

    // If failed, we will return null.
    if (SUCCEEDED(hr))
    {
        // +2 for [], if we need it.
        res = new WCHAR[needed+2];
        hr = g_sos->GetMethodTableName(mt, needed, res, NULL);

        if (FAILED(hr))
        {
            delete [] res;
            res = NULL;
        }
        else if (array)
        {
            res[needed-1] = '[';
            res[needed] = ']';
            res[needed+1] = 0;
        }
    }

    return res;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Return TRUE if str2 is a substring of str1 and str1 and str2      *
*    share the same file path.
*                                                                      *
\**********************************************************************/
BOOL IsSameModuleName (const char *str1, const char *str2)
{
    if (strlen (str1) < strlen (str2))
        return FALSE;
    const char *ptr1 = str1 + strlen(str1)-1;
    const char *ptr2 = str2 + strlen(str2)-1;
    while (ptr2 >= str2)
    {
#ifndef FEATURE_PAL
        if (tolower(*ptr1) != tolower(*ptr2))
#else
        if (*ptr1 != *ptr2)
#endif
        {
            return FALSE;
        }
        ptr2--;
        ptr1--;
    }
    if (ptr1 >= str1 && *ptr1 != GetTargetDirectorySeparatorW() && *ptr1 != ':')
    {
        return FALSE;
    }
    return TRUE;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Return TRUE if moduleAddr is the address of a module.             *
*                                                                      *
\**********************************************************************/
BOOL IsModule (DWORD_PTR moduleAddr)
{
    DacpModuleData module;
    return (module.Request(g_sos, TO_CDADDR(moduleAddr))==S_OK);
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Return TRUE if value is the address of a MethodTable.             *
*    We verify that MethodTable and EEClass are right.
*                                                                      *
\**********************************************************************/
BOOL IsMethodTable (DWORD_PTR value)
{
    DacpMethodTableData mtabledata;
    if (mtabledata.Request(g_sos, TO_CDADDR(value))!=S_OK)
    {
        return FALSE;
    }

    return TRUE;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Return TRUE if value is the address of a MethodDesc.              *
*    We verify that MethodTable and EEClass are right.
*                                                                      *
\**********************************************************************/
BOOL IsMethodDesc (DWORD_PTR value)
{
    // Just by retrieving one successfully from the DAC, we know we have a MethodDesc.
    DacpMethodDescData MethodDescData;
    if (MethodDescData.Request(g_sos, TO_CDADDR(value)) != S_OK)
    {
        return FALSE;
    }

    return TRUE;
}

DacpUsefulGlobalsData g_special_usefulGlobals;

BOOL IsObjectArray (DacpObjectData *pData)
{
    if (pData->ObjectType == OBJ_ARRAY)
        return g_special_usefulGlobals.ArrayMethodTable == pData->MethodTable;

    return FALSE;
}

BOOL IsObjectArray (DWORD_PTR obj)
{
    DWORD_PTR mtAddr = NULL;
    if (SUCCEEDED(GetMTOfObject(obj, &mtAddr)))
        return TO_TADDR(g_special_usefulGlobals.ArrayMethodTable) == mtAddr;

    return FALSE;
}

BOOL IsStringObject (size_t obj)
{
    DWORD_PTR mtAddr = NULL;

    if (SUCCEEDED(GetMTOfObject(obj, &mtAddr)))
        return TO_TADDR(g_special_usefulGlobals.StringMethodTable) == mtAddr;

    return FALSE;
}

BOOL IsDerivedFrom(CLRDATA_ADDRESS mtObj, __in_z LPCWSTR baseString)
{
    DacpMethodTableData dmtd;
    CLRDATA_ADDRESS walkMT = mtObj;
    while (walkMT != NULL)
    {
        if (dmtd.Request(g_sos, walkMT) != S_OK)
        {
            break;
        }

        NameForMT_s(TO_TADDR(walkMT), g_mdName, mdNameLen);
        if (_wcscmp(baseString, g_mdName) == 0)
        {
            return TRUE;
        }

        walkMT = dmtd.ParentMethodTable;
    }

    return FALSE;
}

BOOL IsDerivedFrom(CLRDATA_ADDRESS mtObj, DWORD_PTR modulePtr, mdTypeDef typeDef)
{
    DacpMethodTableData dmtd;

    for (CLRDATA_ADDRESS walkMT = mtObj;
         walkMT != NULL && dmtd.Request(g_sos, walkMT) == S_OK;
         walkMT = dmtd.ParentMethodTable)
    {
        if (dmtd.Module == modulePtr && dmtd.cl == typeDef)
        {
            return TRUE;
        }
    }

    return FALSE;
}

BOOL TryGetMethodDescriptorForDelegate(CLRDATA_ADDRESS delegateAddr, CLRDATA_ADDRESS* pMD)
{
    if (!sos::IsObject(delegateAddr, false))
    {
        return FALSE;
    }

    sos::Object delegateObj = TO_TADDR(delegateAddr);

    for (int i = 0; i < 2; i++)
    {
        int offset;
        if ((offset = GetObjFieldOffset(delegateObj.GetAddress(), delegateObj.GetMT(), i == 0 ? W("_methodPtrAux") : W("_methodPtr"))) != 0)
        {
            CLRDATA_ADDRESS methodPtr;
            MOVE(methodPtr, delegateObj.GetAddress() + offset);
            if (methodPtr != NULL)
            {
                if (g_sos->GetMethodDescPtrFromIP(methodPtr, pMD) == S_OK)
                {
                    return TRUE;
                }

                DacpCodeHeaderData codeHeaderData;
                if (codeHeaderData.Request(g_sos, methodPtr) == S_OK)
                {
                    *pMD = codeHeaderData.MethodDescPtr;
                    return TRUE;
                }
            }
        }
    }

    return FALSE;
}

void DumpStackObjectsOutput(const char *location, DWORD_PTR objAddr, BOOL verifyFields)
{
    // rule out pointers that are outside of the gc heap.
    if (g_snapshot.GetHeap(objAddr) == NULL)
        return;

    DacpObjectData objectData;
    if (objectData.Request(g_sos, TO_CDADDR(objAddr)) != S_OK)
        return;

    if (sos::IsObject(objAddr, verifyFields != FALSE)
        && !sos::MethodTable::IsFreeMT(TO_TADDR(objectData.MethodTable)))
    {
        DMLOut("%-" POINTERSIZE "s %s ", location, DMLObject(objAddr));
        if (g_sos->GetObjectClassName(TO_CDADDR(objAddr), mdNameLen, g_mdName, NULL)==S_OK)
        {
            ExtOut("%S", g_mdName);

            if (IsStringObject(objAddr))
            {
                ExtOut("    ");
                StringObjectContent(objAddr, FALSE, 40);
            }
            else if (IsObjectArray(objAddr) &&
                     (g_sos->GetMethodTableName(objectData.ElementTypeHandle, mdNameLen, g_mdName, NULL) == S_OK))
            {
                ExtOut("    ");
                ExtOut("(%S[])", g_mdName);
            }
        }
        else
        {
            ExtOut("<unknown type>");
        }
        ExtOut("\n");
    }
}

void DumpStackObjectsOutput(DWORD_PTR ptr, DWORD_PTR objAddr, BOOL verifyFields)
{
    char location[64];
    sprintf_s(location, 64, "%p", (DWORD_PTR *)ptr);

    DumpStackObjectsOutput(location, objAddr, verifyFields);
}

void DumpStackObjectsInternal(size_t StackTop, size_t StackBottom, BOOL verifyFields)
{
    for (DWORD_PTR ptr = StackTop; ptr <= StackBottom; ptr += sizeof(DWORD_PTR))
    {
        if (IsInterrupt())
            return;

        DWORD_PTR objAddr;
        move_xp(objAddr, ptr);

        DumpStackObjectsOutput(ptr, objAddr, verifyFields);
    }
}

void DumpRegObjectHelper(const char *regName, BOOL verifyFields)
{
    DWORD_PTR reg;
#ifdef FEATURE_PAL
    if (FAILED(g_ExtRegisters->GetValueByName(regName, &reg)))
        return;
#else
    DEBUG_VALUE value;
    ULONG IREG;
    if (FAILED(g_ExtRegisters->GetIndexByName(regName, &IREG)) ||
        FAILED(g_ExtRegisters->GetValue(IREG, &value)))
        return;

#if defined(SOS_TARGET_X86) || defined(SOS_TARGET_ARM)
    reg = (DWORD_PTR) value.I32;
#elif defined(SOS_TARGET_AMD64) || defined(SOS_TARGET_ARM64)
    reg = (DWORD_PTR) value.I64;
#else
#error Unsupported target
#endif
#endif // FEATURE_PAL

    DumpStackObjectsOutput(regName, reg, verifyFields);
}

void DumpStackObjectsHelper (
                TADDR StackTop,
                TADDR StackBottom,
                BOOL verifyFields)
{
    ExtOut(g_targetMachine->GetDumpStackObjectsHeading());

    LPCSTR* regs;
    unsigned int cnt;
    g_targetMachine->GetGCRegisters(&regs, &cnt);

    for (size_t i = 0; i < cnt; ++i)
        DumpRegObjectHelper(regs[i], verifyFields);

    // Make certain StackTop is dword aligned:
    DumpStackObjectsInternal(StackTop & ~ALIGNCONST, StackBottom, verifyFields);
}

void AddToModuleList(DWORD_PTR * &moduleList, int &numModule, int &maxList,
                     DWORD_PTR dwModuleAddr)
{
    int i;
    for (i = 0; i < numModule; i ++)
    {
        if (moduleList[i] == dwModuleAddr)
            break;
    }
    if (i == numModule)
    {
        moduleList[numModule] = dwModuleAddr;
        numModule ++;
        if (numModule == maxList)
        {
            int listLength = 0;
            if (!ClrSafeInt<int>::multiply(maxList, 2, listLength))
            {
                ExtOut("<integer overflow>\n");
                numModule = 0;
                ControlC = 1;
                return;
            }
            DWORD_PTR *list = new DWORD_PTR [listLength];

            if (list == NULL)
            {
                numModule = 0;
                ControlC = 1;
                return;
            }
            memcpy (list, moduleList, maxList * sizeof(PVOID));
            delete[] moduleList;
            moduleList = list;
            maxList *= 2;
        }
    }
}

BOOL IsFusionLoadedModule (LPCSTR fusionName, LPCSTR mName)
{
    // The fusion name will be in this format:
    // <module name>, Version=<version>, Culture=<culture>, PublicKeyToken=<token>
    // If fusionName up to the comma matches mName (case insensitive),
    // we consider that a match was found.
    LPCSTR commaPos = strchr (fusionName, ',');
    if (commaPos)
    {
        // verify that fusionName and mName match up to a comma.
        while (*fusionName != ',')
        {
            if (*mName == '\0')
            {
                return FALSE;
            }

#ifndef FEATURE_PAL
            if (tolower(*fusionName) != tolower(*mName))
#else
            if (*fusionName != *mName)
#endif
            {
                return FALSE;
            }
            fusionName++;
            mName++;
        }
        return TRUE;
    }
    return FALSE;
}

BOOL DebuggerModuleNamesMatch (CLRDATA_ADDRESS PEFileAddr, ___in __in_z LPSTR mName)
{
    // Another way to see if a module is the same is
    // to accept that mName may be the debugger's name for
    // a loaded module. We can get the debugger's name for
    // the module we are looking at right now, and compare
    // it with mName, if they match exactly, we can add
    // the module to the list.
    if (PEFileAddr)
    {
        CLRDATA_ADDRESS pebase = 0;
        if (g_sos->GetPEFileBase(PEFileAddr, &pebase) == S_OK)
        {
            if (pebase)
            {
                ULONG Index;
                ULONG64 base;
                if (g_ExtSymbols->GetModuleByOffset(pebase, 0, &Index, &base) == S_OK)
                {
                    CHAR ModuleName[MAX_LONGPATH+1];

                    if (g_ExtSymbols->GetModuleNames(Index, base, NULL, 0, NULL, ModuleName,
                        MAX_LONGPATH, NULL, NULL, 0, NULL) == S_OK)
                    {
                        if (_stricmp (ModuleName, mName) == 0)
                        {
                            return TRUE;
                        }
                    }
                }
            }
        }
    }
    return FALSE;
}

DWORD_PTR *ModuleFromName(__in_opt LPSTR mName, int *numModule)
{
    if (numModule == NULL)
        return NULL;

    DWORD_PTR *moduleList = NULL;
    *numModule = 0;

    HRESULT hr;
    DacpAppDomainStoreData adsData;
    if ((hr = adsData.Request(g_sos)) != S_OK)
    {
        ExtDbgOut("DacpAppDomainStoreData.Request FAILED %08x\n", hr);
        return NULL;
    }

    ArrayHolder<CLRDATA_ADDRESS> pAssemblyArray = NULL;
    ArrayHolder<CLRDATA_ADDRESS> pModules = NULL;
    int arrayLength = 0;
    int numSpecialDomains = (adsData.sharedDomain != NULL) ? 2 : 1;
    if (!ClrSafeInt<int>::addition(adsData.DomainCount, numSpecialDomains, arrayLength))
    {
        ExtOut("<integer overflow>\n");
        return NULL;
    }
    ArrayHolder<CLRDATA_ADDRESS> pArray = new CLRDATA_ADDRESS[arrayLength];
    if (pArray == NULL)
    {
        ReportOOM();
        return NULL;
    }

    pArray[0] = adsData.systemDomain;
    if (adsData.sharedDomain != NULL)
    {
        pArray[1] = adsData.sharedDomain;
    }
    if ((hr = g_sos->GetAppDomainList(adsData.DomainCount, pArray.GetPtr() + numSpecialDomains, NULL)) != S_OK)
    {
        ExtOut("Unable to get array of AppDomains: %08x\n", hr);
        return NULL;
    }

    // List all domain
    size_t AllocSize;
    int maxList = arrayLength; // account for system and shared domains
    if (maxList <= 0 || !ClrSafeInt<size_t>::multiply(maxList, sizeof(PVOID), AllocSize))
    {
        ExtOut("<integer overflow>\n");
        return NULL;
    }

    moduleList = new DWORD_PTR[maxList];
    if (moduleList == NULL)
    {
        ReportOOM();
        return NULL;
    }

    ArrayHolder<char> fileName = new char[MAX_LONGPATH];

    // Search all domains to find a module
    for (int n = 0; n < adsData.DomainCount+numSpecialDomains; n++)
    {
        if (IsInterrupt())
        {
            ExtOut("<interrupted>\n");
            goto Failure;
        }

        DacpAppDomainData appDomain;
        if (FAILED(hr = appDomain.Request(g_sos, pArray[n])))
        {
            // Don't print a failure message here, there is a very normal case when checking
            // for modules after clr is loaded but before any AppDomains or assemblies are created
            // for example:
            // >sxe ld:clr
            // >g
            // ...
            // ModLoad: runtime dll
            // >!bpmd Foo.dll Foo.Bar

            // we will correctly give the answer that whatever module you were looking for, it isn't loaded yet
            ExtDbgOut("DacpAppDomainData.Request FAILED %08x\n", hr);
            goto Failure;
        }

        if (appDomain.AssemblyCount)
        {
            pAssemblyArray = new CLRDATA_ADDRESS[appDomain.AssemblyCount];
            if (pAssemblyArray==NULL)
            {
                ReportOOM();
                goto Failure;
            }

            if (FAILED(hr = g_sos->GetAssemblyList(appDomain.AppDomainPtr, appDomain.AssemblyCount, pAssemblyArray, NULL)))
            {
                ExtOut("Unable to get array of Assemblies for the given AppDomain: %08x\n", hr);
                goto Failure;
            }

            for (int nAssem = 0; nAssem < appDomain.AssemblyCount; nAssem ++)
            {
                if (IsInterrupt())
                {
                    ExtOut("<interrupted>\n");
                    goto Failure;
                }

                DacpAssemblyData assemblyData;
                if (FAILED(hr = assemblyData.Request(g_sos, pAssemblyArray[nAssem])))
                {
                    ExtOut("Failed to request assembly: %08x\n", hr);
                    goto Failure;
                }

                pModules = new CLRDATA_ADDRESS[assemblyData.ModuleCount];
                if (FAILED(hr = g_sos->GetAssemblyModuleList(assemblyData.AssemblyPtr, assemblyData.ModuleCount, pModules, NULL)))
                {
                    ExtOut("Failed to get the modules for the given assembly: %08x\n", hr);
                    goto Failure;
                }

                for (UINT nModule = 0; nModule < assemblyData.ModuleCount; nModule++)
                {
                    if (IsInterrupt())
                    {
                        ExtOut("<interrupted>\n");
                        goto Failure;
                    }

                    CLRDATA_ADDRESS ModuleAddr = pModules[nModule];
                    DacpModuleData ModuleData;
                    if (FAILED(hr = ModuleData.Request(g_sos, ModuleAddr)))
                    {
                        ExtDbgOut("Failed to request module data from assembly at %p %08x\n", ModuleAddr, hr);
                        continue;
                    }

                    if (mName != NULL)
                    {
                        ArrayHolder<WCHAR> moduleName = new WCHAR[MAX_LONGPATH];
                        FileNameForModule(&ModuleData, moduleName);

                        int bytesWritten = WideCharToMultiByte(CP_ACP, 0, moduleName, -1, fileName, MAX_LONGPATH, NULL, NULL);
                        _ASSERTE(bytesWritten > 0);
                    }

                    if ((mName == NULL) ||
                        IsSameModuleName(fileName, mName) ||
                        DebuggerModuleNamesMatch(ModuleData.PEAssembly, mName) ||
                        IsFusionLoadedModule(fileName, mName))
                    {
                        AddToModuleList(moduleList, *numModule, maxList, (DWORD_PTR)ModuleAddr);
                    }
                }

                pModules = NULL;
            }
            pAssemblyArray = NULL;
        }
    }

    return moduleList;

    // We do not want to return a half-constructed list.  Instead, we return NULL on a failure.
Failure:
    delete [] moduleList;
    return NULL;
}

#ifndef FEATURE_PAL

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Retrieve module base associated with the IXCLRDataModule          *
*    instance passed in, and the extent type requested.                *
*                                                                      *
\**********************************************************************/
HRESULT GetClrModuleImages(__in IXCLRDataModule* module, __in CLRDataModuleExtentType desiredType, __out PULONG64 pBase, __out PULONG64 pSize)
{
    CLRDATA_ENUM enumExtents;
    HRESULT hr;

    _ASSERTE(pBase != nullptr);
    _ASSERTE(pSize != nullptr);
    *pBase = 0;
    *pSize = 0;

    if (FAILED(hr = module->StartEnumExtents(&enumExtents)))
    {
        return hr;
    }
    CLRDATA_MODULE_EXTENT extent;
    while (module->EnumExtent(&enumExtents, &extent) == S_OK)
    {
        if ((desiredType == CLRDATA_MODULE_OTHER) || (desiredType == extent.type))
        {
            ULONG64 moduleBase;
            if (FAILED(hr = g_ExtSymbols->GetModuleByOffset(extent.base, 0, nullptr, &moduleBase)))
            {
                if (desiredType == CLRDATA_MODULE_PE_FILE)
                {
                    *pBase = extent.base;
                    *pSize = extent.length;
                    hr = S_OK;
                }
                break;
            }
            DEBUG_MODULE_PARAMETERS params;
            if (FAILED(hr = g_ExtSymbols->GetModuleParameters(1, &moduleBase, 0, &params)))
            {
                break;
            }
            *pBase = moduleBase;
            *pSize = params.Size;
            hr = S_OK;
            break;
        }
    }
    module->EndEnumExtents(enumExtents);
    return hr;
}

#endif // FEATURE_PAL

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the IXCLRDataModule instance for the base address.           *
*                                                                      *
\**********************************************************************/
HRESULT GetModuleFromAddress(___in CLRDATA_ADDRESS peAddress, ___out IXCLRDataModule** ppModule)
{
    *ppModule = nullptr;

    int numModule;
    ArrayHolder<DWORD_PTR> moduleList = ModuleFromName(NULL, &numModule);
    if (moduleList != nullptr)
    {
        for (int i = 0; i < numModule; i++)
        {
            ToRelease<IXCLRDataModule> module;
            HRESULT hr = g_sos->GetModule(moduleList[i], &module);
            if (FAILED(hr)) {
                return hr;
            }
            ULONG32 flags;
            if ((hr = module->GetFlags(&flags)) != S_OK) {
                continue;
            }
            if ((flags & (CLRDATA_MODULE_IS_DYNAMIC | CLRDATA_MODULE_IS_MEMORY_STREAM)) != 0) {
                continue;
            }
            DacpGetModuleData moduleData;
            hr = moduleData.Request(module);
            if (FAILED(hr)) {
#ifdef FEATURE_PAL
                return hr;
#else
                hr = GetClrModuleImages(module, CLRDATA_MODULE_PE_FILE, &moduleData.LoadedPEAddress, &moduleData.LoadedPESize);
                if (FAILED(hr))
                {
                    return hr;
                }
#endif
            }
            if (peAddress == moduleData.LoadedPEAddress)
            {
                *ppModule = module.Detach();
                return S_OK;
            }
        }
    }

    return E_INVALIDARG;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the EE data given a name.                                    *
*                                                                      *
\**********************************************************************/
void GetInfoFromName(DWORD_PTR ModulePtr, const char* name, mdTypeDef* retMdTypeDef)
{
    DWORD_PTR ignoredModuleInfoRet = NULL;
    if (retMdTypeDef)
        *retMdTypeDef = 0;

    ToRelease<IMetaDataImport> pImport = MDImportForModule (ModulePtr);
    if (pImport == 0)
        return;

    static WCHAR wszName[MAX_CLASSNAME_LENGTH];
    size_t n;
    size_t length = strlen (name);
    for (n = 0; n <= length; n ++)
        wszName[n] = name[n];

    // First enumerate methods. We're taking advantage of the DAC's
    // CLRDataModule::EnumMethodDefinitionByName which can parse
    // method names (whether in nested classes, or explicit interface
    // method implementations).
    ToRelease<IXCLRDataModule> ModuleDefinition;
    if (g_sos->GetModule(ModulePtr, &ModuleDefinition) == S_OK)
    {
        CLRDATA_ENUM h;
        if (ModuleDefinition->StartEnumMethodDefinitionsByName(wszName, 0, &h) == S_OK)
        {
            IXCLRDataMethodDefinition *pMeth = NULL;
            BOOL fStatus = FALSE;
            while (ModuleDefinition->EnumMethodDefinitionByName(&h, &pMeth) == S_OK)
            {
                if (fStatus && !retMdTypeDef)
                    ExtOut("-----------------------\n");

                mdTypeDef token;
                if (pMeth->GetTokenAndScope(&token, NULL) == S_OK)
                {
                    GetInfoFromModule(ModulePtr, token, retMdTypeDef ? &ignoredModuleInfoRet : NULL);
                    fStatus = TRUE;
                }
                pMeth->Release();
            }
            ModuleDefinition->EndEnumMethodDefinitionsByName(h);
            if (fStatus)
                return;
        }
    }

    // Now look for types, type members and fields
    mdTypeDef cl;
    mdToken tkEnclose = mdTokenNil;
    WCHAR *pName;
    WCHAR *pHead = wszName;
    while ( ((pName = _wcschr (pHead,L'+')) != NULL) ||
             ((pName = _wcschr (pHead,L'/')) != NULL)) {
        pName[0] = L'\0';
        if (FAILED(pImport->FindTypeDefByName(pHead,tkEnclose,&tkEnclose)))
            return;
        pHead = pName+1;
    }

    pName = pHead;

    // @todo:  Handle Nested classes correctly.
    if (SUCCEEDED (pImport->FindTypeDefByName (pName, tkEnclose, &cl)))
    {
        if (retMdTypeDef)
            *retMdTypeDef = cl;

        GetInfoFromModule(ModulePtr, cl, retMdTypeDef ? &ignoredModuleInfoRet : NULL);
        return;
    }

    // See if it is a method
    WCHAR *pwzMethod;
    if ((pwzMethod = _wcsrchr(pName, L'.')) == NULL)
        return;

    if (pwzMethod[-1] == L'.')
        pwzMethod --;
    pwzMethod[0] = L'\0';
    pwzMethod ++;

    // @todo:  Handle Nested classes correctly.
    if (SUCCEEDED(pImport->FindTypeDefByName (pName, tkEnclose, &cl)))
    {
        if (retMdTypeDef)
            *retMdTypeDef = cl;

        mdMethodDef token;
        ULONG cTokens;
        HCORENUM henum = NULL;

        // is Member?
        henum = NULL;
        if (SUCCEEDED (pImport->EnumMembersWithName (&henum, cl, pwzMethod,
                                                     &token, 1, &cTokens))
            && cTokens == 1)
        {
            if (!retMdTypeDef) ExtOut("Member (mdToken token) of\n");
            GetInfoFromModule(ModulePtr, cl, retMdTypeDef ? &ignoredModuleInfoRet : NULL);
            return;
        }

        // is Field?
        henum = NULL;
        if (SUCCEEDED (pImport->EnumFieldsWithName (&henum, cl, pwzMethod,
                                                     &token, 1, &cTokens))
            && cTokens == 1)
        {
            if (!retMdTypeDef) ExtOut("Field (mdToken token) of\n");
            GetInfoFromModule(ModulePtr, cl, retMdTypeDef ? &ignoredModuleInfoRet : NULL);
            return;
        }
    }
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the EE data given a token.                                   *
*                                                                      *
\**********************************************************************/
DWORD_PTR GetMethodDescFromModule(DWORD_PTR ModuleAddr, ULONG token)
{
    if (TypeFromToken(token) != mdtMethodDef)
        return NULL;

    CLRDATA_ADDRESS md = 0;
    if (FAILED(g_sos->GetMethodDescFromToken(ModuleAddr, token, &md)))
    {
        return NULL;
    }
    else if (0 == md)
    {
        // a NULL ReturnValue means the method desc is not loaded yet
        return MD_NOT_YET_LOADED;
    }
    else if ( !IsMethodDesc((DWORD_PTR)md))
    {
        return NULL;
    }

    return (DWORD_PTR)md;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the MethodDefinitions given a name.                          *
*                                                                      *
\**********************************************************************/
HRESULT GetMethodDefinitionsFromName(TADDR ModulePtr, IXCLRDataModule* mod, const char *name, IXCLRDataMethodDefinition **ppOut, int numMethods, int *numMethodsNeeded)
{
    if (name == NULL)
        return E_FAIL;

    size_t n;
    size_t length = strlen (name);
    for (n = 0; n <= length; n ++)
        g_mdName[n] = name[n];

    CLRDATA_ENUM h;
    int methodCount = 0;
    if (mod->StartEnumMethodDefinitionsByName(g_mdName, 0, &h) == S_OK)
    {
        IXCLRDataMethodDefinition *pMeth = NULL;
        while (mod->EnumMethodDefinitionByName(&h, &pMeth) == S_OK)
        {
            methodCount++;
            pMeth->Release();
        }
        mod->EndEnumMethodDefinitionsByName(h);
    }

    if(numMethodsNeeded != NULL)
        *numMethodsNeeded = methodCount;
    if(ppOut == NULL)
        return S_OK;
    if(numMethods > methodCount)
        numMethods = methodCount;

    if (methodCount > 0)
    {
        if (mod->StartEnumMethodDefinitionsByName(g_mdName, 0, &h) == S_OK)
        {
            IXCLRDataMethodDefinition *pMeth = NULL;
            for (int i = 0; i < numMethods && mod->EnumMethodDefinitionByName(&h, &pMeth) == S_OK; i++)
            {
                ppOut[i] = pMeth;
            }
            mod->EndEnumMethodDefinitionsByName(h);
        }
    }

    return S_OK;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the EE data given a name.                                    *
*                                                                      *
\**********************************************************************/
HRESULT GetMethodDescsFromName(TADDR ModulePtr, IXCLRDataModule* mod, const char *name, DWORD_PTR **pOut,int *numMethods)
{
    if (name == NULL || pOut == NULL || numMethods == NULL)
        return E_FAIL;

    *pOut = NULL;
    *numMethods = 0;

    size_t n;
    size_t length = strlen (name);
    for (n = 0; n <= length; n ++)
        g_mdName[n] = name[n];

    CLRDATA_ENUM h;
    int methodCount = 0;
    if (mod->StartEnumMethodDefinitionsByName(g_mdName, 0, &h) == S_OK)
    {
        IXCLRDataMethodDefinition *pMeth = NULL;
        while (mod->EnumMethodDefinitionByName(&h, &pMeth) == S_OK)
        {
            methodCount++;
            pMeth->Release();
        }
        mod->EndEnumMethodDefinitionsByName(h);
    }

    if (methodCount > 0)
    {
        *pOut = new TADDR[methodCount];
        if (*pOut==NULL)
        {
            ReportOOM();
            return E_OUTOFMEMORY;
        }

        *numMethods = methodCount;

        if (mod->StartEnumMethodDefinitionsByName(g_mdName, 0, &h) == S_OK)
        {
            int i = 0;
            IXCLRDataMethodDefinition *pMeth = NULL;
            while (mod->EnumMethodDefinitionByName(&h, &pMeth) == S_OK)
            {
                mdTypeDef token;
                if (pMeth->GetTokenAndScope(&token, NULL) != S_OK)
                    (*pOut)[i] = NULL;
                (*pOut)[i] = GetMethodDescFromModule(ModulePtr, token);
                if ((*pOut)[i] == NULL)
                {
                    *numMethods = 0;
                    return E_FAIL;
                }
                i++;
                pMeth->Release();
            }
            mod->EndEnumMethodDefinitionsByName(h);
        }
    }

    return S_OK;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    Find the EE data given a token.                                   *
*                                                                      *
\**********************************************************************/
void GetInfoFromModule (DWORD_PTR ModuleAddr, ULONG token, DWORD_PTR *ret)
{
    switch (TypeFromToken(token))
    {
        case mdtMethodDef:
            break;
        case mdtTypeDef:
            break;
        case mdtTypeRef:
            break;
        case mdtFieldDef:
            break;
        default:
            ExtOut("This token type is not supported\n");
            return;
            break;
    }

    CLRDATA_ADDRESS md = 0;
    if (FAILED(g_sos->GetMethodDescFromToken(ModuleAddr, token, &md)) || !IsValidToken (ModuleAddr, token))
    {
        ExtOut("<invalid module token>\n");
        return;
    }

    if (ret != NULL)
    {
        *ret = (DWORD_PTR)md;
        return;
    }

    ExtOut("Token:       %p\n", SOS_PTR(token));

    switch (TypeFromToken(token))
    {
        case mdtFieldDef:
        {
            NameForToken_s(ModuleAddr, token, g_mdName, mdNameLen);
            ExtOut("Field name:  %S\n", g_mdName);
            break;
        }
        case mdtMethodDef:
        {
            if (md)
            {
                DMLOut("MethodDesc:  %s\n", DMLMethodDesc(md));

                // Easiest to get full parameterized method name from ..::GetMethodName
                if (g_sos->GetMethodDescName(md, mdNameLen, g_mdName, NULL) != S_OK)
                {
                    // Fall back to just method name without parameters..
                    NameForToken_s(ModuleAddr, token, g_mdName, mdNameLen);
                }
            }
            else
            {
                ExtOut("MethodDesc:  <not loaded yet>\n");
                NameForToken_s(ModuleAddr, token, g_mdName, mdNameLen);
            }

            ExtOut("Name:        %S\n", g_mdName);
            // Nice to have a little more data
            if (md)
            {
                DacpMethodDescData MethodDescData;
                if (MethodDescData.Request(g_sos, md) == S_OK)
                {
                    if (MethodDescData.bHasNativeCode)
                    {
                        DMLOut("JITTED Code Address: %s\n", DMLIP(MethodDescData.NativeCodeAddr));
                    }
                    else
                    {
#ifndef FEATURE_PAL
                        if (IsDMLEnabled())
                            DMLOut("Not JITTED yet. Use <exec cmd=\"!bpmd -md %p\">!bpmd -md %p</exec> to break on run.\n",
                                SOS_PTR(md), SOS_PTR(md));
                        else
                            ExtOut("Not JITTED yet. Use !bpmd -md %p to break on run.\n", SOS_PTR(md));
#else
                        ExtOut("Not JITTED yet. Use 'bpmd -md %p' to break on run.\n", SOS_PTR(md));
#endif
                    }
                }
                else
                {
                    ExtOut ("<Error getting MethodDesc information>\n");
                }
            }
            else
            {
                ExtOut("Not JITTED yet.\n");
            }
            break;
        }
        case mdtTypeDef:
        case mdtTypeRef:
        {
            if (md)
            {
                DMLOut("MethodTable: %s\n", DMLMethodTable(md));
                DacpMethodTableData mtabledata;
                if (mtabledata.Request(g_sos, md) == S_OK)
                {
                    DMLOut("EEClass:     %s\n", DMLClass(mtabledata.Class));
                }
                else
                {
                    ExtOut("EEClass:     <error getting EEClass>\n");
                }
            }
            else
            {
                ExtOut("MethodTable: <not loaded yet>\n");
                ExtOut("EEClass:     <not loaded yet>\n");
            }
            NameForToken_s(ModuleAddr, token, g_mdName, mdNameLen);
            ExtOut("Name:        %S\n", g_mdName);
            break;
        }
        default:
            break;
    }
    return;
}

BOOL IsMTForFreeObj(DWORD_PTR pMT)
{
    return (pMT == g_special_usefulGlobals.FreeMethodTable);
}

const char *EHTypeName(EHClauseType et)
{
    if (et == EHFault)
        return "FAULT";
    else if (et == EHFinally)
        return "FINALLY";
    else if (et == EHFilter)
        return "FILTER";
    else if (et == EHTyped)
        return "TYPED";
    else
        return "UNKNOWN";
}

// 2.x version
void DumpTieredNativeCodeAddressInfo_2x(struct DacpTieredVersionData_2x * pTieredVersionData, const UINT cTieredVersionData)
{
    ExtOut("Code Version History:\n");

    for(int i = cTieredVersionData - 1; i >= 0; --i)
    {
        const char *descriptor = NULL;
        switch(pTieredVersionData[i].TieredInfo)
        {
        case DacpTieredVersionData_2x::TIERED_UNKNOWN:
        default:
            descriptor = "Unknown Tier";
            break;
        case DacpTieredVersionData_2x::NON_TIERED:
            descriptor = "Non-Tiered";
            break;
        case DacpTieredVersionData_2x::TIERED_0:
            descriptor = "Tier 0";
            break;
        case DacpTieredVersionData_2x::TIERED_1:
            descriptor = "Tier 1";
            break;
        }
        DMLOut("     CodeAddr:           %s  (%s)\n", DMLIP(pTieredVersionData[i].NativeCodeAddr), descriptor);
        ExtOut("     NativeCodeVersion:  %p\n", SOS_PTR(pTieredVersionData[i].NativeCodeVersionNodePtr));
    }
}

void DumpTieredNativeCodeAddressInfo(struct DacpTieredVersionData * pTieredVersionData, const UINT cTieredVersionData,
    ULONG rejitID, CLRDATA_ADDRESS ilAddr, CLRDATA_ADDRESS ilNodeAddr)
{
    ExtOut("  ILCodeVersion:      %p\n", SOS_PTR(ilNodeAddr));
    ExtOut("  ReJIT ID:           %d\n", rejitID);
    DMLOut("  IL Addr:            %s\n", DMLIL(ilAddr));

    if (IsRuntimeVersionAtLeast(3)) {
        for(int i = cTieredVersionData - 1; i >= 0; --i)
        {
            const char *descriptor = NULL;
            switch(pTieredVersionData[i].OptimizationTier)
            {
            case DacpTieredVersionData::OptimizationTier_Unknown:
            default:
                descriptor = "Unknown Tier";
                break;
            case DacpTieredVersionData::OptimizationTier_MinOptJitted:
                descriptor = "MinOptJitted";
                break;
            case DacpTieredVersionData::OptimizationTier_Optimized:
                descriptor = "Optimized";
                break;
            case DacpTieredVersionData::OptimizationTier_QuickJitted:
                descriptor = "QuickJitted";
                break;
            case DacpTieredVersionData::OptimizationTier_OptimizedTier1:
                descriptor = "OptimizedTier1";
                break;
            case DacpTieredVersionData::OptimizationTier_OptimizedTier1OSR:
                descriptor = "OptimizedTier1OSR";
                break;
            case DacpTieredVersionData::OptimizationTier_ReadyToRun:
                descriptor = "ReadyToRun";
                break;
            }
            DMLOut("     CodeAddr:           %s  (%s)\n", DMLIP(pTieredVersionData[i].NativeCodeAddr), descriptor);
            ExtOut("     NativeCodeVersion:  %p\n", SOS_PTR(pTieredVersionData[i].NativeCodeVersionNodePtr));
        }
    }
    else {
        DumpTieredNativeCodeAddressInfo_2x((DacpTieredVersionData_2x*)pTieredVersionData, cTieredVersionData);
    }
}

void DumpRejitData(CLRDATA_ADDRESS pMethodDesc, DacpReJitData * pReJitData)
{
    int rejitID = (int)pReJitData->rejitID;
    CLRDATA_ADDRESS ilAddr = 0;
    CLRDATA_ADDRESS ilNodeAddr = 0;

    struct DacpReJitData2 rejitData;
    ReleaseHolder<ISOSDacInterface7> sos7;
    if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface7), &sos7)) &&
        SUCCEEDED(sos7->GetReJITInformation(pMethodDesc,
                                            rejitID,
                                            &rejitData)))
    {
        ilAddr = rejitData.il;
        ilNodeAddr = rejitData.ilCodeVersionNodePtr;
    }

    struct DacpTieredVersionData codeAddrs[kcMaxTieredVersions];
    int cCodeAddrs;

    ReleaseHolder<ISOSDacInterface5> sos5;
    if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface5), &sos5)) &&
        SUCCEEDED(sos5->GetTieredVersions(pMethodDesc,
                                            rejitID,
                                            codeAddrs,
                                            kcMaxTieredVersions,
                                            &cCodeAddrs)))
    {
        DumpTieredNativeCodeAddressInfo(codeAddrs, cCodeAddrs, rejitID, ilAddr, ilNodeAddr);
    }
}

// For !ip2md requests, this function helps us ensure that rejitted version corresponding
// to the specified IP always gets dumped. It may have already been dumped if it was the
// current rejit version (which is always dumped) or one of the reverted versions that we
// happened to dump before we clipped their number down to kcRejitDataRevertedMax.
BOOL ShouldDumpRejitDataRequested(DacpMethodDescData * pMethodDescData, DacpReJitData * pRevertedRejitData, UINT cRevertedRejitData)
{
    if (pMethodDescData->rejitDataRequested.rejitID == 0)
        return FALSE;

    if (pMethodDescData->rejitDataRequested.rejitID == pMethodDescData->rejitDataCurrent.rejitID)
        return FALSE;

    for (ULONG i=0; i < cRevertedRejitData; i++)
    {
        if (pMethodDescData->rejitDataRequested.rejitID == pRevertedRejitData[i].rejitID)
            return FALSE;
    }

    return TRUE;
}


void DumpAllRejitDataIfNecessary(DacpMethodDescData * pMethodDescData, DacpReJitData * pRevertedRejitData, UINT cRevertedRejitData)
{
    // If there's no rejit info to output, then skip
    if ((pMethodDescData->rejitDataCurrent.rejitID == 0) &&
        (pMethodDescData->rejitDataRequested.rejitID == 0) &&
        (cRevertedRejitData == 0))
    {
        return;
    }

    // Dump reverted rejit infos
    for (ULONG i=0; i < cRevertedRejitData; i++)
    {
        DumpRejitData(pMethodDescData->MethodDescPtr, &pRevertedRejitData[i]);
    }

    // For !ip2md, ensure we dump the rejit version corresponding to the specified IP
    // (if not already dumped)
    if (ShouldDumpRejitDataRequested(pMethodDescData, pRevertedRejitData, cRevertedRejitData))
        DumpRejitData(pMethodDescData->MethodDescPtr, &pMethodDescData->rejitDataRequested);

    // If we maxed out the reverted versions we dumped, let user know there may be more
    if (cRevertedRejitData == kcMaxRevertedRejitData)
        ExtOut("    (... possibly more reverted versions ...)\n");
}

void DumpMDInfoFromMethodDescData(DacpMethodDescData * pMethodDescData, DacpReJitData * pRevertedRejitData, UINT cRevertedRejitData, BOOL fStackTraceFormat)
{
    static WCHAR wszNameBuffer[1024]; // should be large enough
    BOOL bFailed = FALSE;
    if (g_sos->GetMethodDescName(pMethodDescData->MethodDescPtr, 1024, wszNameBuffer, NULL) != S_OK)
    {
        wcscpy_s(wszNameBuffer, ARRAY_SIZE(wszNameBuffer), W("UNKNOWN"));
        bFailed = TRUE;
    }

    if (!fStackTraceFormat)
    {
        ExtOut("Method Name:          %S\n", wszNameBuffer);

        DacpMethodTableData mtdata;
        if (SUCCEEDED(mtdata.Request(g_sos, pMethodDescData->MethodTablePtr)))
        {
            DMLOut("Class:                %s\n", DMLClass(mtdata.Class));
        }

        DMLOut("MethodTable:          %s\n", DMLMethodTable(pMethodDescData->MethodTablePtr));
        ExtOut("mdToken:              %p\n", SOS_PTR(pMethodDescData->MDToken));
        DMLOut("Module:               %s\n", DMLModule(pMethodDescData->ModulePtr));
        ExtOut("IsJitted:             %s\n", pMethodDescData->bHasNativeCode ? "yes" : "no");

        DMLOut("Current CodeAddr:     %s\n", DMLIP(pMethodDescData->NativeCodeAddr));

        int rejitID = (int)pMethodDescData->rejitDataCurrent.rejitID;
        CLRDATA_ADDRESS ilAddr = 0;
        CLRDATA_ADDRESS ilNodeAddr = 0;

        ExtOut("Version History:\n");

        struct DacpReJitData2 rejitData;
        ReleaseHolder<ISOSDacInterface7> sos7;
        if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface7), &sos7)))
        {
            if SUCCEEDED(sos7->GetReJITInformation(pMethodDescData->MethodDescPtr,
                                                   rejitID,
                                                   &rejitData))
            {
                ilAddr = rejitData.il;
                ilNodeAddr = rejitData.ilCodeVersionNodePtr;
            }

            int pendingRejitID;
            struct DacpReJitData2 pendingRejitData;
            if (sos7->GetPendingReJITID(pMethodDescData->MethodDescPtr, &pendingRejitID) == S_OK &&
                SUCCEEDED(sos7->GetReJITInformation(pMethodDescData->MethodDescPtr, pendingRejitID, &pendingRejitData)))
            {
                // Special case, there is no jitted code yet but still need to output the IL information
                ExtOut("  ILCodeVersion:      %p (pending)\n", SOS_PTR(pendingRejitData.ilCodeVersionNodePtr));
                ExtOut("  ReJIT ID:           %d\n", pendingRejitID);
                DMLOut("  IL Addr:            %s\n", DMLIL(pendingRejitData.il));
            }
        }

        struct DacpTieredVersionData codeAddrs[kcMaxTieredVersions];
        int cCodeAddrs;

        ReleaseHolder<ISOSDacInterface5> sos5;
        if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface5), &sos5)) &&
            SUCCEEDED(sos5->GetTieredVersions(pMethodDescData->MethodDescPtr,
                                                                rejitID,
                                                                codeAddrs,
                                                                kcMaxTieredVersions,
                                                                &cCodeAddrs)))
        {
            DumpTieredNativeCodeAddressInfo(codeAddrs, cCodeAddrs, rejitID, ilAddr, ilNodeAddr);
        }

        DumpAllRejitDataIfNecessary(pMethodDescData, pRevertedRejitData, cRevertedRejitData);
    }
    else
    {
        if (!bFailed)
        {
            ExtOut("%S", wszNameBuffer);
        }
        else
        {
            // Only clutter the display with module/token for cases where we
            // can't get the MethodDesc name for some reason.
            DMLOut("Unknown MethodDesc (Module %s, mdToken %08x)",
                    DMLModule(pMethodDescData->ModulePtr),
                    pMethodDescData->MDToken);
        }
    }
}

void DumpMDInfo(DWORD_PTR dwMethodDescAddr, CLRDATA_ADDRESS dwRequestedIP /* = 0 */, BOOL fStackTraceFormat /*  = FALSE */)
{
    DacpMethodDescData MethodDescData;
    DacpReJitData revertedRejitData[kcMaxRevertedRejitData];
    ULONG cNeededRevertedRejitData;
    if (g_sos->GetMethodDescData(
        TO_CDADDR(dwMethodDescAddr),
        dwRequestedIP,
        &MethodDescData,
        ARRAY_SIZE(revertedRejitData),
        revertedRejitData,
        &cNeededRevertedRejitData) != S_OK)
    {
        ExtOut("%p is not a MethodDesc\n", SOS_PTR(dwMethodDescAddr));
        return;
    }

    DumpMDInfoFromMethodDescData(&MethodDescData, revertedRejitData, cNeededRevertedRejitData, fStackTraceFormat);
}

void GetDomainList (DWORD_PTR *&domainList, int &numDomain)
{
    DacpAppDomainStoreData adsData;

    numDomain = 0;

    if (adsData.Request(g_sos)!=S_OK)
    {
        return;
    }

    // Do prefast integer checks before the malloc.
    size_t AllocSize;
    LONG DomainAllocCount;
    LONG NumExtraDomains = (adsData.sharedDomain != NULL) ? 2 : 1;
    if (!ClrSafeInt<LONG>::addition(adsData.DomainCount, NumExtraDomains, DomainAllocCount) ||
        !ClrSafeInt<size_t>::multiply(DomainAllocCount, sizeof(PVOID), AllocSize) ||
        (domainList = new DWORD_PTR[DomainAllocCount]) == NULL)
    {
        return;
    }

    domainList[numDomain++] = (DWORD_PTR) adsData.systemDomain;
    if (adsData.sharedDomain != NULL)
    {
        domainList[numDomain++] = (DWORD_PTR) adsData.sharedDomain;
    }

    CLRDATA_ADDRESS *pArray = new CLRDATA_ADDRESS[adsData.DomainCount];
    if (pArray==NULL)
    {
        return;
    }

    if (g_sos->GetAppDomainList(adsData.DomainCount, pArray, NULL)!=S_OK)
    {
        delete [] pArray;
        return;
    }

    for (int n=0;n<adsData.DomainCount;n++)
    {
        if (IsInterrupt())
            break;
        domainList[numDomain++] = (DWORD_PTR) pArray[n];
    }

    delete [] pArray;
}


HRESULT GetThreadList(DWORD_PTR **threadList, int *numThread)
{
    _ASSERTE(threadList != NULL);
    _ASSERTE(numThread != NULL);

    if (threadList == NULL || numThread == NULL)
    {
        return E_FAIL;
    }

    *numThread = 0;

    DacpThreadStoreData ThreadStore;
    if ( ThreadStore.Request(g_sos) != S_OK)
    {
        ExtOut("Failed to request threads from the thread store.");
        return E_FAIL;
    }

    *threadList = new DWORD_PTR[ThreadStore.threadCount];
    if (*threadList == NULL)
    {
        ReportOOM();
        return E_OUTOFMEMORY;
    }

    CLRDATA_ADDRESS CurThread = ThreadStore.firstThread;
    while (CurThread != NULL)
    {
        if (IsInterrupt())
            return S_FALSE;

        DacpThreadData Thread;
        if (Thread.Request(g_sos, CurThread) != S_OK)
        {
            ExtOut("Failed to request Thread at %p\n", SOS_PTR(CurThread));
            return E_FAIL;
        }

        (*threadList)[(*numThread)++] = (DWORD_PTR)CurThread;
        CurThread = Thread.nextThread;
    }

    return S_OK;
}

CLRDATA_ADDRESS GetCurrentManagedThread ()
{
    DacpThreadStoreData ThreadStore;
    ThreadStore.Request(g_sos);

    ULONG Tid;
    g_ExtSystem->GetCurrentThreadSystemId(&Tid);

    CLRDATA_ADDRESS CurThread = ThreadStore.firstThread;
    while (CurThread)
    {
        DacpThreadData Thread;
        if (Thread.Request(g_sos, CurThread) != S_OK)
        {
            return NULL;
        }

        if (Thread.osThreadId == Tid)
        {
            return CurThread;
        }

        CurThread = Thread.nextThread;
    }
    return NULL;
}

#define MSCOREE_SHIM_A                "mscoree.dll"

void ReloadSymbolWithLineInfo()
{
    _ASSERTE(g_pRuntime != nullptr);
#ifndef FEATURE_PAL
    static BOOL bLoadSymbol = FALSE;
    if (!bLoadSymbol)
    {
        ULONG Options;
        g_ExtSymbols->GetSymbolOptions(&Options);
        if (!(Options & SYMOPT_LOAD_LINES))
        {
            g_ExtSymbols->AddSymbolOptions(SYMOPT_LOAD_LINES);
            if (SUCCEEDED(g_ExtSymbols->GetModuleByModuleName(MSCOREE_SHIM_A, 0, NULL, NULL)))
            {
                g_ExtSymbols->Reload("/f" MSCOREE_SHIM_A);
            }
            std::string reloadCommand;
            reloadCommand.append("/f ");
            reloadCommand.append(GetRuntimeDllName());
            g_ExtSymbols->Reload(reloadCommand.c_str());
        }

        // reload mscoree.pdb and clrjit.pdb to get line info
        bLoadSymbol = TRUE;
    }
#endif
}

// Return 1 if the function is our stub
// Return MethodDesc if the function is managed
// Otherwise return 0
size_t FunctionType (size_t EIP)
{
    ULONG64 base = 0;
    ULONG   ulLoaded, ulUnloaded, ulIndex;

    // Get the number of loaded and unloaded modules
    if (FAILED(g_ExtSymbols->GetNumberModules(&ulLoaded, &ulUnloaded)))
        return 0;


    if (SUCCEEDED(g_ExtSymbols->GetModuleByOffset(TO_CDADDR(EIP), 0, &ulIndex, &base)) && base != 0)
    {
        if (ulIndex < ulLoaded)
        {
            IMAGE_DOS_HEADER DosHeader;
            if (g_ExtData->ReadVirtual(TO_CDADDR(base), &DosHeader, sizeof(DosHeader), NULL) != S_OK)
                return 0;
            IMAGE_NT_HEADERS Header;
            if (g_ExtData->ReadVirtual(TO_CDADDR(base + DosHeader.e_lfanew), &Header, sizeof(Header), NULL) != S_OK)
                return 0;
            // If there is no COMHeader, this can not be managed code.
            if (Header.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress == 0)
                return 0;

            IMAGE_COR20_HEADER ComPlusHeader;
            if (g_ExtData->ReadVirtual(TO_CDADDR(base + Header.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress),
                                       &ComPlusHeader, sizeof(ComPlusHeader), NULL) != S_OK)
                return 0;

            // If there is no Precompiled image info, it can not be prejit code
            if (ComPlusHeader.ManagedNativeHeader.VirtualAddress == 0) {
                return 0;
            }
        }
    }

    CLRDATA_ADDRESS dwStartAddr = TO_CDADDR(EIP);
    CLRDATA_ADDRESS pMD;
    if (g_sos->GetMethodDescPtrFromIP(dwStartAddr, &pMD) != S_OK)
    {
        return 1;
    }

    return (size_t) pMD;
}

//
// Return true if major runtime version (logical product version like 2.1,
// 3.0 or 5.x). Currently only major versions of 3 or 5 are supported.
//
bool IsRuntimeVersion(DWORD major)
{
    VS_FIXEDFILEINFO fileInfo;
    if (SUCCEEDED(g_pRuntime->GetEEVersion(&fileInfo, nullptr, 0)))
    {
        return IsRuntimeVersion(fileInfo, major);
    }
    return false;
}

bool IsRuntimeVersion(VS_FIXEDFILEINFO& fileInfo, DWORD major)
{
    switch (major)
    {
        case 5:
            return HIWORD(fileInfo.dwFileVersionMS) == 5;
        case 3:
            return HIWORD(fileInfo.dwFileVersionMS) == 4 && LOWORD(fileInfo.dwFileVersionMS) == 700;
        default:
            _ASSERTE(FALSE);
            break;
    }
    return false;
}

bool IsRuntimeVersionAtLeast(DWORD major)
{
    VS_FIXEDFILEINFO fileInfo;
    if (SUCCEEDED(g_pRuntime->GetEEVersion(&fileInfo, nullptr, 0)))
    {
        return IsRuntimeVersionAtLeast(fileInfo, major);
    }
    return false;
}

bool IsRuntimeVersionAtLeast(VS_FIXEDFILEINFO& fileInfo, DWORD major)
{
    switch (major)
    {
        case 3:
            if (HIWORD(fileInfo.dwFileVersionMS) == 4 && LOWORD(fileInfo.dwFileVersionMS) == 700)
            {
                return true;
            }
            // fall through

        case 5:
            if (HIWORD(fileInfo.dwFileVersionMS) >= 5)
            {
                return true;
            }
            // fall through

            break;

        default:
            _ASSERTE(FALSE);
            break;
    }
    return false;
}

// Returns true if there is a change in the data structures that SOS depends on like
// stress log structs (StressMsg, StressLogChunck, ThreadStressLog, etc), exception
// stack traces (StackTraceElement), the PredefinedTlsSlots enums, etc.
bool CheckBreakingRuntimeChange(int* pVersion)
{
    bool result = false;

    // Assume version 1 if no ISOSDacInterface9 (runtimes < 5.0)
    int version = 1;

    if (g_sos != nullptr)
    {
        ReleaseHolder<ISOSDacInterface9> sos9;
        if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface9), &sos9)))
        {
            if (SUCCEEDED(sos9->GetBreakingChangeVersion(&version)))
            {
                if (version > SOS_BREAKING_CHANGE_VERSION)
                {
                    ExtWarn("WARNING: SOS needs to be upgraded for this version of the runtime. Some commands may not work correctly.\n");
                    ExtWarn("For more information see https://go.microsoft.com/fwlink/?linkid=2135652\n");
                    ExtWarn("\n");
                    result = true;
                }
            }
        }
    }
    if (pVersion != nullptr)
    {
        *pVersion = version;
    }
    return result;
}

#ifndef FEATURE_PAL

BOOL GetSOSVersion(VS_FIXEDFILEINFO *pFileInfo)
{
    _ASSERTE(pFileInfo);

    WCHAR wszFullPath[MAX_LONGPATH];
    DWORD cchFullPath = GetModuleFileNameW(g_hInstance, wszFullPath, ARRAY_SIZE(wszFullPath));

    DWORD dwHandle = 0;
    DWORD infoSize = GetFileVersionInfoSizeW(wszFullPath, &dwHandle);
    if (infoSize)
    {
        ArrayHolder<BYTE> pVersionInfo = new BYTE[infoSize];
        if (pVersionInfo)
        {
            if (GetFileVersionInfoW(wszFullPath, NULL, infoSize, pVersionInfo))
            {
                VS_FIXEDFILEINFO *pTmpFileInfo = NULL;
                UINT uLen = 0;
                if (VerQueryValueA(pVersionInfo, "\\", (LPVOID *) &pTmpFileInfo, &uLen))
                {
                    if (pFileInfo->dwFileVersionMS == (DWORD)-1) {
                        return FALSE;
                    }
                    *pFileInfo = *pTmpFileInfo; // Copy the info
                    return TRUE;
                }
            }
        }
    }

    return FALSE;
}

#endif // !FEATURE_PAL

size_t ObjectSize(DWORD_PTR obj,BOOL fIsLargeObject)
{
    DWORD_PTR dwMT;
    MOVE(dwMT, obj);
    return ObjectSize(obj, dwMT, FALSE, fIsLargeObject);
}

size_t ObjectSize(DWORD_PTR obj, DWORD_PTR mt, BOOL fIsValueClass, BOOL fIsLargeObject)
{
    BOOL bContainsPointers;
    size_t size = 0;
    if (!GetSizeEfficient(obj, mt, fIsLargeObject, size, bContainsPointers))
    {
        return 0;
    }
    return size;
}

// This takes an array of values and sets every non-printable character
// to be a period.
void Flatten(__out_ecount(len) char *data, unsigned int len)
{
    for (unsigned int i = 0; i < len; ++i)
        if (data[i] < 32 || data[i] > 126)
            data[i] = '.';
    data[len] = 0;
}

void CharArrayContent(TADDR pos, ULONG num, bool widechar)
{
    if (!pos || num <= 0)
        return;

    if (widechar)
    {
        ArrayHolder<WCHAR> data = new WCHAR[num+1];
        if (!data)
        {
            ReportOOM();
            return;
        }

        ULONG readLen = 0;
        if (!SafeReadMemory(pos, data, num<<1, &readLen))
            return;

        Flatten(data.GetPtr(), readLen >> 1);
        ExtOut("%S", data.GetPtr());
    }
    else
    {
        ArrayHolder<char> data = new char[num+1];
        if (!data)
        {
            ReportOOM();
            return;
        }

        ULONG readLen = 0;
        if (!SafeReadMemory(pos, data, num, &readLen))
            return;

        _ASSERTE(readLen <= num);
        Flatten(data, readLen);

        ExtOut("%s", data.GetPtr());
    }
}

void StringObjectContent(size_t obj, BOOL fLiteral, const int length)
{
    DacpObjectData objData;
    if (objData.Request(g_sos, TO_CDADDR(obj))!=S_OK)
    {
        ExtOut("<Invalid Object>");
        return;
    }

    strobjInfo stInfo { 0, 0 };
    if (MOVE(stInfo, obj) != S_OK)
    {
        ExtOut ("Error getting string data\n");
        return;
    }

    if (objData.Size > 0x200000 || stInfo.m_StringLength > 0x200000)
    {
        ExtOut ("<String is invalid or too large to print>\n");
        return;
    }

    ArrayHolder<WCHAR> pwszBuf = new WCHAR[stInfo.m_StringLength+1];
    if (pwszBuf == NULL)
    {
        return;
    }

    DWORD_PTR dwAddr = (DWORD_PTR)pwszBuf.GetPtr();
    if (g_sos->GetObjectStringData(TO_CDADDR(obj), stInfo.m_StringLength+1, pwszBuf, NULL)!=S_OK)
    {
        ExtOut("<Invalid Object>");
        return;
    }

    if (!fLiteral)
    {
        pwszBuf[stInfo.m_StringLength] = L'\0';
        ExtOut ("%S", pwszBuf.GetPtr());
    }
    else
    {
        ULONG32 count = stInfo.m_StringLength;
        WCHAR buffer[256];
        WCHAR out[512];
        while (count)
        {
            DWORD toRead = 255;
            if (count < toRead)
                toRead = count;

            ULONG bytesRead;
            wcsncpy_s(buffer,ARRAY_SIZE(buffer),(LPWSTR) dwAddr, toRead);
            bytesRead = toRead*sizeof(WCHAR);
            DWORD wcharsRead = bytesRead/2;
            buffer[wcharsRead] = L'\0';

            ULONG j,k=0;
            for (j = 0; j < wcharsRead; j ++)
            {
                if (_iswprint (buffer[j])) {
                    out[k] = buffer[j];
                    k ++;
                }
                else
                {
                    out[k++] = L'\\';
                    switch (buffer[j]) {
                    case L'\n':
                        out[k++] = L'n';
                        break;
                    case L'\0':
                        out[k++] = L'0';
                        break;
                    case L'\t':
                        out[k++] = L't';
                        break;
                    case L'\v':
                        out[k++] = L'v';
                        break;
                    case L'\b':
                        out[k++] = L'b';
                        break;
                    case L'\r':
                        out[k++] = L'r';
                        break;
                    case L'\f':
                        out[k++] = L'f';
                        break;
                    case L'\a':
                        out[k++] = L'a';
                        break;
                    case L'\\':
                        break;
                    case L'\?':
                        out[k++] = L'?';
                        break;
                    default:
                        out[k++] = L'?';
                        break;
                    }
                }
            }

            out[k] = L'\0';
            ExtOut ("%S", out);

            count -= wcharsRead;
            dwAddr += bytesRead;
        }
    }
}

#ifdef _TARGET_WIN64_

#include <limits.h>

__int64 str64hex(const char *ptr)
{
    __int64 value = 0;
    unsigned char nCount = 0;

    if(ptr==NULL)
        return 0;

    // Ignore leading 0x if present
    if (*ptr=='0' && toupper(*(ptr+1))=='X') {
        ptr = ptr + 2;
    }

    while (1) {

        char digit;

        if (isdigit(*ptr)) {
            digit = *ptr - '0';
        } else if (isalpha(*ptr)) {
            digit = (((char)toupper(*ptr)) - 'A') + 10;
            if (digit >= 16) {
                break; // terminate
            }
        } else {
            break;
        }

        if (nCount>15) {
            return _UI64_MAX;     // would be an overflow
        }

        value = value << 4;
        value |= digit;

        ptr++;
        nCount++;
    }

    return value;
}

#endif // _TARGET_WIN64_

BOOL GetValueForCMD (const char *ptr, const char *end, ARGTYPE type, size_t *value)
{
    if (type == COSTRING) {
        // Allocate memory for the length of the string. Whitespace terminates
        // User must free the string data.
        char *pszValue = NULL;
        size_t dwSize = (end - ptr);
        pszValue= new char[dwSize+1];
        if (pszValue == NULL)
        {
            return FALSE;
        }
        strncpy_s(pszValue,dwSize+1,ptr,dwSize); // _TRUNCATE
        *value = (size_t) pszValue;
    } else {
        char *last;
        if (type == COHEX) {
#ifdef _TARGET_WIN64_
            *value = str64hex(ptr);
#else
            *value = strtoul(ptr,&last,16);
#endif
        }
        else {
#ifdef _TARGET_WIN64_
            *value = _atoi64(ptr);
#else
            *value = strtoul(ptr,&last,10);
#endif
        }

#ifdef _TARGET_WIN64_
        last = (char *) ptr;
        // Ignore leading 0x if present
        if (*last=='0' && toupper(*(last+1))=='X') {
            last = last + 2;
        }

        while (isdigit(*last) || (toupper(*last)>='A' && toupper(*last)<='F')) {
            last++;
        }
#endif

        if (last != end) {
            return FALSE;
        }
    }

    return TRUE;
}

void SetValueForCMD (void *vptr, ARGTYPE type, size_t value)
{
    switch (type) {
    case COBOOL:
        *(BOOL*)vptr = (BOOL) value;
        break;
    case COSIZE_T:
    case COSTRING:
    case COHEX:
        *(SIZE_T*)vptr = value;
        break;
    }
}

BOOL GetCMDOption(const char *string, CMDOption *option, size_t nOption,
                  CMDValue *arg, size_t maxArg, size_t *nArg)
{
    const char *end;
    const char *ptr = string;
    BOOL endofOption = FALSE;

    for (size_t n = 0; n < nOption; n ++)
    {
        if (IsInterrupt())
            return FALSE;

        option[n].hasSeen = FALSE;
    }

    if (nArg) {
        *nArg = 0;
    }

    while (ptr[0] != '\0')
    {
        if (IsInterrupt())
            return FALSE;

        // skip any space
        if (isspace (ptr[0])) {
            while (isspace (ptr[0]))
            {
                if (IsInterrupt())
                    return FALSE;

                ptr ++;
            }

            continue;
        }

        end = ptr;

        // Arguments can be quoted with ". We'll remove the quotes and
        // allow spaces to exist in the string.
        BOOL bQuotedArg = FALSE;
        if (ptr[0] == '\'' && ptr[1] != '-')
        {
            bQuotedArg = TRUE;

            // skip quote
            ptr++;
            end++;

            while (end[0] != '\'' && end[0] != '\0')
            {
                if (IsInterrupt())
                    return FALSE;

                end ++;
            }
            if (end[0] != '\'')
            {
                // Error, th ere was a start quote but no end quote
                ExtOut ("Missing quote in %s\n", ptr);
                return FALSE;
            }
        }
        else // whitespace terminates
        {
            while (!isspace(end[0]) && end[0] != '\0')
            {
                if (IsInterrupt())
                    return FALSE;

                end ++;
            }
        }

#ifndef FEATURE_PAL
        if (ptr[0] != '-' && ptr[0] != '/') {
#else
        if (ptr[0] != '-') {
#endif
            if (maxArg == 0) {
                ExtOut ("Incorrect argument: %s\n", ptr);
                return FALSE;
            }
            endofOption = TRUE;
            if (*nArg >= maxArg) {
                ExtOut ("Incorrect argument: %s\n", ptr);
                return FALSE;
            }

            size_t value;
            if (!GetValueForCMD (ptr,end,arg[*nArg].type,&value)) {

                char oldChar = *end;
                *(char *)end = '\0';
                value = (size_t)GetExpression (ptr);
                *(char *)end = oldChar;

                /*

                    It is silly to do this, what if 0 is a valid expression for
                    the command?

                if (value == 0) {
                    ExtOut ("Invalid argument: %s\n", ptr);
                    return FALSE;
                }
                */
            }

            SetValueForCMD (arg[*nArg].vptr, arg[*nArg].type, value);

            (*nArg) ++;
        }
        else if (endofOption) {
            ExtOut ("Wrong option: %s\n", ptr);
            return FALSE;
        }
        else {
            char buffer[80];
            if (end-ptr > 79) {
                ExtOut ("Invalid option %s\n", ptr);
                return FALSE;
            }
            strncpy_s (buffer,ARRAY_SIZE(buffer), ptr, end-ptr);

            size_t n;
            for (n = 0; n < nOption; n ++)
            {
                if (IsInterrupt())
                    return FALSE;

                if (_stricmp (buffer, option[n].name) == 0) {
                    if (option[n].hasSeen) {
                        ExtOut ("Invalid option: option specified multiple times: %s\n", buffer);
                        return FALSE;
                    }
                    option[n].hasSeen = TRUE;
                    if (option[n].hasValue) {
                        // skip any space
                        ptr = end;
                        if (isspace (ptr[0])) {
                            while (isspace (ptr[0]))
                            {
                                if (IsInterrupt())
                                    return FALSE;

                                ptr ++;
                            }
                        }
                        if (ptr[0] == '\0') {
                            ExtOut ("Missing value for option %s\n", buffer);
                            return FALSE;
                        }
                        end = ptr;
                        while (!isspace(end[0]) && end[0] != '\0')
                        {
                            if (IsInterrupt())
                                return FALSE;

                            end ++;
                        }

                        size_t value;
                        if (!GetValueForCMD (ptr,end,option[n].type,&value)) {

                            char oldChar = *end;
                            *(char *)end = '\0';
                            value = (size_t)GetExpression (ptr);
                            *(char *)end = oldChar;
                        }

                        SetValueForCMD (option[n].vptr,option[n].type,value);
                    }
                    else {
                        SetValueForCMD (option[n].vptr,option[n].type,TRUE);
                    }
                    break;
                }
            }
            if (n == nOption) {
                ExtOut ("Unknown option: %s\n", buffer);
                return FALSE;
            }
        }

        ptr = end;
        if (bQuotedArg)
        {
            ptr++;
        }
    }
    return TRUE;
}

ReadVirtualCache g_special_rvCacheSpace;
ReadVirtualCache *rvCache = &g_special_rvCacheSpace;

void ResetGlobals(void)
{
    // There are some globals used in SOS that exist for efficiency in one command,
    // but should be reset because the next execution of an SOS command could be on
    // another managed process. Reset them to a default state here, as this command
    // is called on every SOS entry point.
    g_sos->GetUsefulGlobals(&g_special_usefulGlobals);
    g_special_mtCache.Clear();
    g_special_rvCacheSpace.Clear();
    Output::ResetIndent();
}

//---------------------------------------------------------------------------------------
//
// Loads private DAC interface, and points g_clrData to it.
//
// Return Value:
//      HRESULT indicating success or failure
//
HRESULT LoadClrDebugDll(void)
{
    _ASSERTE(g_pRuntime != nullptr);
    HRESULT hr = g_pRuntime->GetClrDataProcess(&g_clrData);
    if (FAILED(hr))
    {
#ifdef FEATURE_PAL
        return hr;
#else
        // Fail if ExtensionApis wasn't initialized because we are hosted under dotnet-dump
        if (Ioctl == nullptr) {
            return hr;
        }
        // Try getting the DAC interface from dbgeng if the above fails on Windows
        WDBGEXTS_CLR_DATA_INTERFACE Query;

        Query.Iid = &__uuidof(IXCLRDataProcess);
        if (!Ioctl(IG_GET_CLR_DATA_INTERFACE, &Query, sizeof(Query))) {
            return hr;
        }
        g_clrData = (IXCLRDataProcess*)Query.Iface;
        g_clrData->Flush();
#endif
    }
    else
    {
        g_clrData->AddRef();
        g_clrData->Flush();
    }
    hr = g_clrData->QueryInterface(__uuidof(ISOSDacInterface), (void**)&g_sos);
    if (FAILED(hr))
    {
        g_sos = NULL;
        return hr;
    }
    return S_OK;
}

/// <summary>
/// Loads the runtime module symbols for the commands like dumplog that
/// lookup runtime symbols. This is done on-demand because it takes a
/// long time under windbg/cdb and not needed for most commands.
/// </summary>
void LoadRuntimeSymbols()
{
    _ASSERTE(g_pRuntime != nullptr);
#ifndef FEATURE_PAL
    ULONG64 moduleAddress = g_pRuntime->GetModuleAddress();

    DEBUG_MODULE_PARAMETERS params;
    HRESULT hr = g_ExtSymbols->GetModuleParameters(1, &moduleAddress, 0, &params);
    if (SUCCEEDED(hr))
    {
        if (params.SymbolType == SymDeferred)
        {
            PCSTR runtimeDllName = ::GetRuntimeDllName();
            std::string reloadCommand;
            reloadCommand.append("/f ");
            reloadCommand.append(runtimeDllName);
            g_ExtSymbols->Reload(reloadCommand.c_str());
            g_ExtSymbols->GetModuleParameters(1, &moduleAddress, 0, &params);

            if (params.SymbolType != SymPdb && params.SymbolType != SymDia)
            {
                ExtOut("Symbols for %s not loaded. Some SOS commands may not work.\n", runtimeDllName);
            }
        }
    }
#endif
}

typedef enum
{
    GC_HEAP_INVALID = 0,
    GC_HEAP_WKS     = 1,
    GC_HEAP_SVR     = 2
} GC_HEAP_TYPE;

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to find out if runtime is server build    *
*                                                                      *
\**********************************************************************/

DacpGcHeapData *g_pHeapData = NULL;
DacpGcHeapData g_HeapData;

BOOL InitializeHeapData()
{
    if (g_pHeapData == NULL)
    {
        if (g_HeapData.Request(g_sos) != S_OK)
        {
            return FALSE;
        }
        g_pHeapData = &g_HeapData;
    }
    return TRUE;
}

BOOL IsServerBuild()
{
    return InitializeHeapData() ? g_pHeapData->bServerMode : FALSE;
}

UINT GetMaxGeneration()
{
    return InitializeHeapData() ? g_pHeapData->g_max_generation : 0;
}

UINT GetGcHeapCount()
{
    return InitializeHeapData() ? g_pHeapData->HeapCount : 0;
}

BOOL GetGcStructuresValid()
{
    // We don't want to use the cached HeapData, because this can change
    // each time the program runs for a while.
    DacpGcHeapData heapData;
    HRESULT hr;
    if ((hr = heapData.Request(g_sos)) != S_OK)
    {
        ExtOut("GetGcStructuresValid: request heap data FAILED %08x\n", hr);
        return FALSE;
    }

    return heapData.bGcStructuresValid;
}

void GetAllocContextPtrs(AllocInfo *pallocInfo)
{
    // gets the allocation contexts for all threads. This provides information about how much of
    // the current allocation quantum has been allocated and the heap to which the quantum belongs.
    // The allocation quantum is a fixed size chunk of zeroed memory from which allocations will come
    // until it's filled. Each managed thread has its own allocation context.

    pallocInfo->num = 0;
    pallocInfo->array = NULL;

    // get the thread store (See code:ClrDataAccess::RequestThreadStoreData for details)
    DacpThreadStoreData ThreadStore;
    if ( ThreadStore.Request(g_sos) != S_OK)
    {
        return;
    }

    int numThread = ThreadStore.threadCount;
    if (numThread)
    {
        pallocInfo->array = new needed_alloc_context[numThread + 1];
        if (pallocInfo->array == NULL)
        {
            return;
        }
    }

    // get details for each thread in the thread store
    CLRDATA_ADDRESS CurThread = ThreadStore.firstThread;
    while (CurThread != NULL)
    {
        if (IsInterrupt())
            return;

        DacpThreadData Thread;
        // Get information about the thread (we're getting the values of several of the
        // fields of the Thread instance from the target) See code:ClrDataAccess::RequestThreadData for
        // details
        if (Thread.Request(g_sos, CurThread) != S_OK)
        {
            return;
        }

        if (Thread.allocContextPtr != 0)
        {
            // get a list of all the allocation contexts
            int j;
            for (j = 0; j < pallocInfo->num; j ++)
            {
                if (pallocInfo->array[j].alloc_ptr == (BYTE *) Thread.allocContextPtr)
                    break;
            }
            if (j == pallocInfo->num)
            {
                pallocInfo->num ++;
                pallocInfo->array[j].alloc_ptr = (BYTE *) Thread.allocContextPtr;
                pallocInfo->array[j].alloc_limit = (BYTE *) Thread.allocContextLimit;
            }
        }

        CurThread = Thread.nextThread;
    }

    CLRDATA_ADDRESS allocPtr;
    CLRDATA_ADDRESS allocLimit;

    ReleaseHolder<ISOSDacInterface12> sos12;
    if (SUCCEEDED(g_sos->QueryInterface(__uuidof(ISOSDacInterface12), &sos12)) &&
        SUCCEEDED(sos12->GetGlobalAllocationContext(&allocPtr, &allocLimit)) &&
        allocPtr != 0)
    {
        int j = pallocInfo->num ++;
        pallocInfo->array[j].alloc_ptr = (BYTE *) allocPtr;
        pallocInfo->array[j].alloc_limit = (BYTE *) allocLimit;
    }
}

HRESULT ReadVirtualCache::Read(TADDR address, PVOID buffer, ULONG bufferSize, PULONG lpcbBytesRead)
{
    // address can be any random ULONG64, as it can come from VerifyObjectMember(), and this
    // can pass random pointer values in case of GC heap corruption

    if (bufferSize == 0)
        return S_OK;

    if (bufferSize > CACHE_SIZE)
    {
        // Don't even try with the cache
        return g_ExtData->ReadVirtual(TO_CDADDR(address), buffer, bufferSize, lpcbBytesRead);
    }

    if (!m_cacheValid || (address < m_startCache) || (address > (m_startCache + m_cacheSize - bufferSize)))
    {
        ULONG cbBytesRead = 0;

        m_cacheValid = FALSE;
        m_startCache = address;

        // Avoid an int overflow
        if (m_startCache + CACHE_SIZE < m_startCache)
            m_startCache = (TADDR)(-CACHE_SIZE);

        HRESULT hr = g_ExtData->ReadVirtual(TO_CDADDR(m_startCache), m_cache, CACHE_SIZE, &cbBytesRead);
        if (hr != S_OK)
        {
            return hr;
        }

        m_cacheSize = cbBytesRead;
        m_cacheValid = TRUE;
    }

    // If the address is within the cache, copy the cached memory to the input buffer
    LONG_PTR cacheOffset = address - m_startCache;
    if (cacheOffset >= 0 && cacheOffset < CACHE_SIZE)
    {
        int size = _min(bufferSize, m_cacheSize);
        memcpy(buffer, (LPVOID)(m_cache + cacheOffset), size);

        if (lpcbBytesRead != NULL)
        {
            *lpcbBytesRead = size;
        }
    }
    else
    {
        return E_FAIL;
    }

    return S_OK;
}

HRESULT GetMTOfObject(TADDR obj, TADDR *mt)
{
    if (!mt)
        return E_POINTER;

    // Read the MethodTable and if we succeed, get rid of the mark bits.
    HRESULT hr = rvCache->Read(obj, mt, sizeof(TADDR), NULL);
    if (SUCCEEDED(hr))
        *mt &= ~3;

    return hr;
}

#ifndef FEATURE_PAL

StressLogMem::~StressLogMem ()
{
    MemRange * range = list;

    while (range)
    {
        MemRange * temp = range->next;
        delete range;
        range = temp;
    }
}

bool StressLogMem::Init (ULONG64 stressLogAddr, IDebugDataSpaces* memCallBack)
{
    size_t ThreadStressLogAddr = NULL;
    HRESULT hr = memCallBack->ReadVirtual(UL64_TO_CDA(stressLogAddr + offsetof (StressLog, logs)),
            &ThreadStressLogAddr, sizeof (ThreadStressLogAddr), 0);
    if (hr != S_OK)
    {
        return false;
    }

    while(ThreadStressLogAddr != NULL)
    {
        size_t ChunkListHeadAddr = NULL;
        hr = memCallBack->ReadVirtual(TO_CDADDR(ThreadStressLogAddr + ThreadStressLog::OffsetOfListHead ()),
            &ChunkListHeadAddr, sizeof (ChunkListHeadAddr), 0);
        if (hr != S_OK || ChunkListHeadAddr == NULL)
        {
            return false;
        }

        size_t StressLogChunkAddr = ChunkListHeadAddr;

        do
        {
            AddRange (StressLogChunkAddr, sizeof (StressLogChunk));
            hr = memCallBack->ReadVirtual(TO_CDADDR(StressLogChunkAddr + offsetof (StressLogChunk, next)),
                &StressLogChunkAddr, sizeof (StressLogChunkAddr), 0);
            if (hr != S_OK)
            {
                return false;
            }
            if (StressLogChunkAddr == NULL)
            {
                return true;
            }
        } while (StressLogChunkAddr != ChunkListHeadAddr);

        hr = memCallBack->ReadVirtual(TO_CDADDR(ThreadStressLogAddr + ThreadStressLog::OffsetOfNext ()),
            &ThreadStressLogAddr, sizeof (ThreadStressLogAddr), 0);
        if (hr != S_OK)
        {
            return false;
        }
    }

    return true;
}

bool StressLogMem::IsInStressLog (ULONG64 addr)
{
    MemRange * range = list;
    while (range)
    {
        if (range->InRange (addr))
            return true;
        range = range->next;
    }

    return false;
}

#endif // !FEATURE_PAL

unsigned int Output::g_bSuppressOutput = 0;
unsigned int Output::g_Indent = 0;
bool Output::g_bDbgOutput = false;
bool Output::g_bDMLExposed = false;
unsigned int Output::g_DMLEnable = 0;

template <class T, int count, int size> const int StaticData<T, count, size>::Count = count;
template <class T, int count, int size> const int StaticData<T, count, size>::Size  = size;

StaticData<char, 4, 1024> CachedString::cache;

CachedString::CachedString()
: mPtr(0), mRefCount(0), mIndex(~0), mSize(cache.Size)
{
    Create();
}

CachedString::CachedString(const CachedString &rhs)
: mPtr(0), mRefCount(0), mIndex(~0), mSize(cache.Size)
{
    Copy(rhs);
}

CachedString::~CachedString()
{
    Clear();
}

const CachedString &CachedString::operator=(const CachedString &rhs)
{
    Clear();
    Copy(rhs);
    return *this;
}

void CachedString::Copy(const CachedString &rhs)
{
    if (rhs.IsOOM())
    {
        SetOOM();
    }
    else
    {
        mPtr = rhs.mPtr;
        mIndex = rhs.mIndex;
        mSize = rhs.mSize;

        if (rhs.mRefCount)
        {
            mRefCount = rhs.mRefCount;
            (*mRefCount)++;
        }
        else
        {
            // We only create count the first time we copy it, so
            // we initialize it to 2.
            mRefCount = rhs.mRefCount = new unsigned int(2);
            if (!mRefCount)
                SetOOM();
        }
    }
}

void CachedString::Clear()
{
    if (!mRefCount || --*mRefCount == 0)
    {
        if (mIndex == -1)
        {
            if (mPtr)
                delete [] mPtr;
        }
        else if (mIndex >= 0 && mIndex < cache.Count)
        {
            cache.InUse[mIndex] = false;
        }

        if (mRefCount)
            delete mRefCount;
    }

    mPtr = 0;
    mIndex = ~0;
    mRefCount = 0;
    mSize = cache.Size;
}


void CachedString::Create()
{
    mIndex = -1;
    mRefCount = 0;

    // First try to find a string in the cache to use.
    for (int i = 0; i < cache.Count; ++i)
        if (!cache.InUse[i])
        {
            cache.InUse[i] = true;
            mPtr = cache.Data[i];
            mIndex = i;
            break;
        }

    // We did not find a string to use, so we'll create a new one.
    if (mIndex == -1)
    {
        mPtr = new char[cache.Size];
        if (!mPtr)
            SetOOM();
    }
}


void CachedString::SetOOM()
{
    Clear();
    mIndex = -2;
}

void CachedString::Allocate(int size)
{
    Clear();
    mPtr = new char[size];

    if (mPtr)
    {
        mSize = size;
        mIndex = -1;
    }
    else
    {
        SetOOM();
    }
}

size_t CountHexCharacters(CLRDATA_ADDRESS val)
{
    size_t ret = 0;

    while (val)
    {
        val >>= 4;
        ret++;
    }

    return ret;
}

// SOS is single threaded so a global buffer doesn't need any locking
char g_printBuffer[8192];

//---------------------------------------------------------------------
// Because debuggers and hosts SOS runs on now output formatting always
// happens with the C++ runtime functions and not dbgeng. This means
// the special dbgeng formatting charaters are not supported: %N, %I,
// %ma, %mu, %msa, %msu, %y, %ly and %p takes an architecture size
// pointer (size_t) instead of always a 64bit one.
//---------------------------------------------------------------------

HRESULT
OutputVaList(
    ULONG mask,
    PCSTR format,
    va_list args)
{
    int length = _vsnprintf_s((char* const)&g_printBuffer, sizeof(g_printBuffer), _TRUNCATE, format, args);
    if (length > 0)
    {
        return g_ExtControl->OutputVaList(mask, (char* const)&g_printBuffer, args);
    }
    return E_FAIL;
}

HRESULT
ControlledOutputVaList(
    ULONG outputControl,
    ULONG mask,
    PCSTR format,
    va_list args)
{
    int length = _vsnprintf_s((char* const)&g_printBuffer, sizeof(g_printBuffer), _TRUNCATE, format, args);
    if (length > 0)
    {
        return g_ExtControl->ControlledOutputVaList(outputControl, mask, (char* const)&g_printBuffer, args);
    }
    return E_FAIL;
}

HRESULT
OutputText(
    ULONG mask,
    PCSTR format,
    ...)
{
    va_list args;
    va_start (args, format);
    HRESULT result = OutputVaList(mask, format, args);
    va_end (args);
    return result;
}

void WhitespaceOut(int count)
{
    static const int FixedIndentWidth = 0x40;
    static const char FixedIndentString[FixedIndentWidth+1] =
        "                                                                ";

    if (count <= 0)
        return;

    int mod = count & 0x3F;
    count &= ~0x3F;

    if (mod > 0)
        OutputText(DEBUG_OUTPUT_NORMAL, "%.*s", mod, FixedIndentString);

    for ( ; count > 0; count -= FixedIndentWidth)
        OutputText(DEBUG_OUTPUT_NORMAL, FixedIndentString);
}

void DMLOut(PCSTR format, ...)
{
    if (Output::IsOutputSuppressed())
        return;

    va_list args;
    va_start(args, format);
    ExtOutIndent();

    if (IsDMLEnabled() && !Output::IsDMLExposed())
    {
        ControlledOutputVaList(DEBUG_OUTCTL_AMBIENT_DML, DEBUG_OUTPUT_NORMAL, format, args);
    }
    else
    {
        OutputVaList(DEBUG_OUTPUT_NORMAL, format, args);
    }

    va_end(args);
}

void IfDMLOut(PCSTR format, ...)
{
    if (Output::IsOutputSuppressed() || !IsDMLEnabled())
        return;

    va_list args;

    va_start(args, format);
    ExtOutIndent();
    g_ExtControl->ControlledOutputVaList(DEBUG_OUTCTL_AMBIENT_DML, DEBUG_OUTPUT_NORMAL, format, args);
    va_end(args);
}

void ExtOut(PCSTR Format, ...)
{
    if (Output::IsOutputSuppressed())
        return;

    va_list Args;

    va_start(Args, Format);
    ExtOutIndent();
    OutputVaList(DEBUG_OUTPUT_NORMAL, Format, Args);
    va_end(Args);
}

void ExtWarn(PCSTR Format, ...)
{
    if (Output::IsOutputSuppressed())
        return;

    va_list Args;

    va_start(Args, Format);
    OutputVaList(DEBUG_OUTPUT_WARNING, Format, Args);
    va_end(Args);
}

void ExtErr(PCSTR Format, ...)
{
    va_list Args;

    va_start(Args, Format);
    OutputVaList(DEBUG_OUTPUT_ERROR, Format, Args);
    va_end(Args);
}

/// <summary>
/// Internal trace output for extensions library
/// </summary>
void TraceError(PCSTR format, ...)
{
    if (Output::g_bDbgOutput)
    {
        va_list args;
        va_start(args, format);
        OutputVaList(DEBUG_OUTPUT_ERROR, format, args);
        va_end(args);
    }
}

void ExtDbgOut(PCSTR Format, ...)
{
    if (Output::g_bDbgOutput)
    {
        va_list Args;
        va_start(Args, Format);
        ExtOutIndent();
        OutputVaList(DEBUG_OUTPUT_NORMAL, Format, Args);
        va_end(Args);
    }
}

const char * const DMLFormats[] =
{
    NULL,                                           // DML_None (do not use)
    "<exec cmd=\"!DumpMT /d %s\">%s</exec>",        // DML_MethodTable
    "<exec cmd=\"!DumpMD /d %s\">%s</exec>",        // DML_MethodDesc
    "<exec cmd=\"!DumpClass /d %s\">%s</exec>",     // DML_EEClass
    "<exec cmd=\"!DumpModule /d %s\">%s</exec>",    // DML_Module
    "<exec cmd=\"!U /d %s\">%s</exec>",             // DML_IP
    "<exec cmd=\"!DumpObj /d %s\">%s</exec>",       // DML_Object
    "<exec cmd=\"!DumpDomain /d %s\">%s</exec>",    // DML_Domain
    "<exec cmd=\"!DumpAssembly /d %s\">%s</exec>",  // DML_Assembly
    "<exec cmd=\"~~[%s]s\">%s</exec>",              // DML_ThreadID
    "<exec cmd=\"!DumpVC /d %s %s\">%s</exec>",     // DML_ValueClass
    "<exec cmd=\"!DumpHeap /d -mt %s\">%s</exec>",  // DML_DumpHeapMT
    "<exec cmd=\"!ListNearObj /d %s\">%s</exec>",   // DML_ListNearObj
    "<exec cmd=\"!ThreadState %s\">%s</exec>",      // DML_ThreadState
    "<exec cmd=\"!PrintException /d %s\">%s</exec>",// DML_PrintException
    "<exec cmd=\"!DumpRCW /d %s\">%s</exec>",       // DML_RCWrapper
    "<exec cmd=\"!DumpCCW /d %s\">%s</exec>",       // DML_CCWrapper
    "<exec cmd=\"!ClrStack -i %S %d\">%S</exec>",   // DML_ManagedVar
    "<exec cmd=\"!DumpObj /d %s\">%s</exec>",       // DML_Async
    "<exec cmd=\"!DumpIL /i %s\">%s</exec>",         // DML_IL
    "<exec cmd=\"!DumpRCW -cw /d %s\">%s</exec>",    // DML_ComWrapperRCW
    "<exec cmd=\"!DumpCCW -cw /d %s\">%s</exec>",    // DML_ComWrapperCCW
    "<exec cmd=\"dps %s L%d\">%s</exec>",            // DML_TaggedMemory
};

static_assert(ARRAY_SIZE(DMLFormats) == Output::DML_Last, "Output types and formats must match in length");

void ConvertToLower(__out_ecount(len) char *buffer, size_t len)
{
    for (size_t i = 0; i < len && buffer[i]; ++i)
        buffer[i] = (char)tolower(buffer[i]);
}

/* Build a hex display of addr.
 */
int GetHex(CLRDATA_ADDRESS addr, __out_ecount(len) char *out, size_t len, bool fill)
{
    int count = sprintf_s(out, len, fill ? "%p" : "%x", (size_t)addr);

    ConvertToLower(out, len);

    return count;
}

CachedString Output::BuildHexValue(CLRDATA_ADDRESS disp, CLRDATA_ADDRESS addr, FormatType type, bool fill)
{
    CachedString ret;
    if (ret.IsOOM())
    {
        ReportOOM();
        return ret;
    }

    if (IsDMLEnabled())
    {
        char hex1[POINTERSIZE_BYTES*2 + 1];
        char hex2[POINTERSIZE_BYTES*2 + 1];
        char* d = hex1;
        char* a = hex1;
        GetHex(addr, hex1, ARRAY_SIZE(hex1), fill);
        if (disp != addr)
        {
            GetHex(disp, hex2, ARRAY_SIZE(hex2), fill);
            d = hex2;
        }

        sprintf_s(ret, ret.GetStrLen(), DMLFormats[type], a, d);
    }
    else
    {
        GetHex(disp, ret, ret.GetStrLen(), fill);
    }

    return ret;
}

CachedString Output::BuildHexValueWithLength(CLRDATA_ADDRESS addr, size_t len, FormatType type, bool fill)
{
    CachedString ret;
    if (ret.IsOOM())
    {
        ReportOOM();
        return ret;
    }

    if (IsDMLEnabled())
    {
        char hex[POINTERSIZE_BYTES*2 + 1];
        GetHex(addr, hex, ARRAY_SIZE(hex), fill);
        sprintf_s(ret, ret.GetStrLen(), DMLFormats[type], hex, len, hex);
    }
    else
    {
        GetHex(addr, ret, ret.GetStrLen(), fill);
    }

    return ret;
}

CachedString Output::BuildVCValue(CLRDATA_ADDRESS disp, CLRDATA_ADDRESS mt, CLRDATA_ADDRESS addr, FormatType type, bool fill)
{
    _ASSERTE(type == DML_ValueClass);
    CachedString ret;
    if (ret.IsOOM())
    {
        ReportOOM();
        return ret;
    }

    if (IsDMLEnabled())
    {
        char hexaddr1[POINTERSIZE_BYTES*2 + 1];
        char hexaddr2[POINTERSIZE_BYTES*2 + 1];
        char hexmt[POINTERSIZE_BYTES*2 + 1];
        char* d = hexaddr1;
        char* a = hexaddr1;

        GetHex(addr, hexaddr1, ARRAY_SIZE(hexaddr1), fill);
        if (disp != addr)
        {
            GetHex(disp, hexaddr2, ARRAY_SIZE(hexaddr2), fill);
            d = hexaddr2;
        }
        GetHex(mt, hexmt, ARRAY_SIZE(hexmt), fill);

        sprintf_s(ret, ret.GetStrLen(), DMLFormats[type], hexmt, a, d);
    }
    else
    {
        GetHex(addr, ret, ret.GetStrLen(), fill);
    }

    return ret;
}

CachedString Output::BuildManagedVarValue(__in_z LPCWSTR expansionName, ULONG frame, __in_z LPCWSTR simpleName, FormatType type)
{
    _ASSERTE(type == DML_ManagedVar);
    CachedString ret;
    if (ret.IsOOM())
    {
        ReportOOM();
        return ret;
    }

    // calculate the number of digits in frame (this assumes base-10 display of frames)
    int numFrameDigits = 0;
    if (frame > 0)
    {
        ULONG tempFrame = frame;
        while (tempFrame > 0)
        {
            ++numFrameDigits;
            tempFrame /= 10;
        }
    }
    else
    {
        numFrameDigits = 1;
    }

    size_t totalStringLength = strlen(DMLFormats[type]) + _wcslen(expansionName) + numFrameDigits + _wcslen(simpleName) + 1;
    if (totalStringLength > ret.GetStrLen())
    {
        ret.Allocate(static_cast<int>(totalStringLength));
        if (ret.IsOOM())
        {
            ReportOOM();
            return ret;
        }
    }

    if (IsDMLEnabled())
    {
        sprintf_s(ret, ret.GetStrLen(), DMLFormats[type], expansionName, frame, simpleName);
    }
    else
    {
        sprintf_s(ret, ret.GetStrLen(), "%S", simpleName);
    }

    return ret;
}

CachedString Output::BuildManagedVarValue(__in_z LPCWSTR expansionName, ULONG frame, int indexInArray, FormatType type)
{
    WCHAR indexString[24];
    swprintf_s(indexString, ARRAY_SIZE(indexString), W("[%d]"), indexInArray);
    return BuildManagedVarValue(expansionName, frame, indexString, type);
}

EnableDMLHolder::EnableDMLHolder(BOOL enable)
    : mEnable(enable)
{
#ifndef FEATURE_PAL
    // If the user has not requested that we use DML, it's still possible that
    // they have instead specified ".prefer_dml 1".  If enable is false,
    // we will check here for .prefer_dml.  Since this class is only used once
    // per command issued to SOS, this should only check the setting once per
    // sos command issued.
    if (!mEnable && Output::g_DMLEnable <= 0)
    {
        ULONG opts;
        HRESULT hr = g_ExtControl->GetEngineOptions(&opts);
        mEnable = SUCCEEDED(hr) && (opts & DEBUG_ENGOPT_PREFER_DML) == DEBUG_ENGOPT_PREFER_DML;
    }

    if (mEnable)
    {
        Output::g_DMLEnable++;
    }
#endif // FEATURE_PAL
}

EnableDMLHolder::~EnableDMLHolder()
{
#ifndef FEATURE_PAL
    if (mEnable)
        Output::g_DMLEnable--;
#endif
}

bool IsDMLEnabled()
{
    return IsInitializedByDbgEng() && Output::g_DMLEnable > 0;
}

NoOutputHolder::NoOutputHolder(BOOL bSuppress)
    : mSuppress(bSuppress)
{
    if (mSuppress)
        Output::g_bSuppressOutput++;
}

NoOutputHolder::~NoOutputHolder()
{
    if (mSuppress)
        Output::g_bSuppressOutput--;
}

//
// Code to support mapping RVAs to managed code line numbers.
//

//
// Retrieves the IXCLRDataMethodInstance* instance associated with the
// passed in native offset.
HRESULT
GetClrMethodInstance(
    ___in ULONG64 NativeOffset,
    ___out IXCLRDataMethodInstance** Method)
{
    HRESULT Status;
    CLRDATA_ENUM MethEnum;

    Status = g_clrData->StartEnumMethodInstancesByAddress(NativeOffset, NULL, &MethEnum);

    if (Status == S_OK)
    {
        Status = g_clrData->EnumMethodInstanceByAddress(&MethEnum, Method);
        g_clrData->EndEnumMethodInstancesByAddress(MethEnum);
    }

    // Any alternate success is a true failure here.
    return (Status == S_OK || FAILED(Status)) ? Status : E_NOINTERFACE;
}

//
// Enumerates over the IL address map associated with the passed in
// managed method, and returns the highest non-epilog offset.
HRESULT
GetLastMethodIlOffset(
    ___in IXCLRDataMethodInstance* Method,
    ___out PULONG32 MethodOffs)
{
    HRESULT Status;
    CLRDATA_IL_ADDRESS_MAP MapLocal[16];
    CLRDATA_IL_ADDRESS_MAP* Map = MapLocal;
    ULONG32 MapCount = ARRAY_SIZE(MapLocal);
    ULONG32 MapNeeded;
    ULONG32 HighestOffset;

    for (;;)
    {
        if ((Status = Method->GetILAddressMap(MapCount, &MapNeeded, Map)) != S_OK)
        {
            return Status;
        }

        if (MapNeeded <= MapCount)
        {
            break;
        }

        // Need more map entries.
        if (Map != MapLocal)
        {
            // Already went around and the answer changed,
            // which should not be possible.
            delete[] Map;
            return E_UNEXPECTED;
        }

        Map = new CLRDATA_IL_ADDRESS_MAP[MapNeeded];
        if (!Map)
        {
            return E_OUTOFMEMORY;
        }

        MapCount = MapNeeded;
    }

    HighestOffset = 0;
    for (size_t i = 0; i < MapNeeded; i++)
    {
        if (Map[i].ilOffset != (ULONG32)CLRDATA_IL_OFFSET_NO_MAPPING &&
            Map[i].ilOffset != (ULONG32)CLRDATA_IL_OFFSET_PROLOG &&
            Map[i].ilOffset != (ULONG32)CLRDATA_IL_OFFSET_EPILOG &&
            Map[i].ilOffset > HighestOffset)
        {
            HighestOffset = Map[i].ilOffset;
        }
    }

    if (Map != MapLocal)
    {
        delete[] Map;
    }

    *MethodOffs = HighestOffset;
    return S_OK;
}

//
// Convert a native offset (possibly already associated with a managed
// method identified by the passed in IXCLRDataMethodInstance) to a
// triplet (ImageInfo, MethodToken, MethodOffset) that can be used to
// represent an "IL offset".
HRESULT
ConvertNativeToIlOffset(
    ___in ULONG64 nativeOffset,
    ___in BOOL bAdjustOffsetForLineNumber,
    ___out IXCLRDataModule** ppModule,
    ___out mdMethodDef* methodToken,
    ___out PULONG32 methodOffs)
{
    ToRelease<IXCLRDataMethodInstance> pMethodInst(NULL);
    HRESULT Status;

    if ((Status = GetClrMethodInstance(nativeOffset, &pMethodInst)) != S_OK)
    {
        ExtDbgOut("ConvertNativeToIlOffset(%p): GetClrMethodInstance FAILED %08x\n", nativeOffset, Status);
        return Status;
    }

    if (bAdjustOffsetForLineNumber)
    {
        CLRDATA_ADDRESS startAddr;
        if (pMethodInst->GetRepresentativeEntryAddress(&startAddr) == S_OK)
        {
            if (nativeOffset >= (startAddr + g_targetMachine->StackWalkIPAdjustOffset()))
            {
                nativeOffset -= g_targetMachine->StackWalkIPAdjustOffset();
            }
        }
    }

    if ((Status = pMethodInst->GetILOffsetsByAddress(nativeOffset, 1, NULL, methodOffs)) != S_OK)
    {
        ExtDbgOut("ConvertNativeToIlOffset(%p): GetILOffsetsByAddress FAILED %08x\n", nativeOffset, Status);
        *methodOffs = 0;
    }
    else
    {
        switch((LONG)*methodOffs)
        {
        case CLRDATA_IL_OFFSET_NO_MAPPING:
            return E_NOINTERFACE;

        case CLRDATA_IL_OFFSET_PROLOG:
            // Treat all of the prologue as part of
            // the first source line.
            *methodOffs = 0;
            break;

        case CLRDATA_IL_OFFSET_EPILOG:
            // Back up until we find the last real
            // IL offset.
            if ((Status = GetLastMethodIlOffset(pMethodInst, methodOffs)) != S_OK)
            {
                return Status;
            }
            break;
        }
    }

    return pMethodInst->GetTokenAndScope(methodToken, ppModule);
}

// Based on a native offset, passed in the first argument this function
// identifies the corresponding source file name and line number.
HRESULT
GetLineByOffset(
    ___in ULONG64 nativeOffset,
    ___out ULONG *pLinenum,
    __out_ecount(cchFileName) WCHAR* pwszFileName,
    ___in ULONG cchFileName,
    ___in BOOL bAdjustOffsetForLineNumber /* = FALSE */)
{
    HRESULT status = S_OK;
    ULONG32 methodToken;
    ULONG32 methodOffs;

    // Find the image, method token and IL offset that correspond to "nativeOffset"
    ToRelease<IXCLRDataModule> pModule(NULL);
    status = ConvertNativeToIlOffset(nativeOffset, bAdjustOffsetForLineNumber, &pModule, &methodToken, &methodOffs);
    if (FAILED(status))
    {
        ExtDbgOut("GetLineByOffset(%p): ConvertNativeToIlOffset FAILED %08x\n", nativeOffset, status);
        return status;
    }
    ToRelease<IMetaDataImport> pMDImport(NULL);
    status = pModule->QueryInterface(IID_IMetaDataImport, (LPVOID *) &pMDImport);
    if (FAILED(status))
    {
        ExtDbgOut("GetLineByOffset(%p): QueryInterface(IID_IMetaDataImport) FAILED %08x\n", nativeOffset, status);
    }
    SymbolReader symbolReader;
    status = symbolReader.LoadSymbols(pMDImport, pModule);
    if (FAILED(status))
    {
        return status;
    }
    return symbolReader.GetLineByILOffset(methodToken, methodOffs, pLinenum, pwszFileName, cchFileName);
}

void TableOutput::ReInit(int numColumns, int defaultColumnWidth, Alignment alignmentDefault, int indent, int padding)
{
    Clear();

    mColumns = numColumns;
    mDefaultWidth = defaultColumnWidth;
    mIndent = indent;
    mPadding = padding;
    mCurrCol = 0;
    mDefaultAlign = alignmentDefault;
}

void TableOutput::SetWidths(int columns, ...)
{
    SOS_Assert(columns > 0);
    SOS_Assert(columns <= mColumns);

    AllocWidths();

    va_list list;
    va_start(list, columns);

    for (int i = 0; i < columns; ++i)
        mWidths[i] = va_arg(list, int);

    va_end(list);
}

void TableOutput::SetColWidth(int col, int width)
{
    SOS_Assert(col >= 0 && col < mColumns);
    SOS_Assert(width >= 0);

    AllocWidths();

    mWidths[col] = width;
}

void TableOutput::SetColAlignment(int col, Alignment align)
{
    SOS_Assert(col >= 0 && col < mColumns);

    if (!mAlignments)
    {
        mAlignments = new Alignment[mColumns];
        for (int i = 0; i < mColumns; ++i)
            mAlignments[i] = mDefaultAlign;
    }

    mAlignments[col] = align;
}



void TableOutput::Clear()
{
    if (mAlignments)
    {
        delete [] mAlignments;
        mAlignments = 0;
    }

    if (mWidths)
    {
        delete [] mWidths;
        mWidths = 0;
    }
}

void TableOutput::AllocWidths()
{
    if (!mWidths)
    {
        mWidths = new int[mColumns];
        for (int i = 0; i < mColumns; ++i)
            mWidths[i] = mDefaultWidth;
    }
}

int TableOutput::GetColumnWidth(int col)
{
    SOS_Assert(col < mColumns);

    if (mWidths)
        return mWidths[col];

    return mDefaultWidth;
}

Alignment TableOutput::GetColAlign(int col)
{
    SOS_Assert(col < mColumns);
    if (mAlignments)
        return mAlignments[col];

    return mDefaultAlign;
}

const char *TableOutput::GetWhitespace(int amount)
{
    static char WhiteSpace[256] = "";
    static int count = 0;

    if (count == 0)
    {
        count = ARRAY_SIZE(WhiteSpace);
        for (int i = 0; i < count-1; ++i)
            WhiteSpace[i] = ' ';
        WhiteSpace[count-1] = 0;
    }

    SOS_Assert(amount < count);
    return &WhiteSpace[count-amount-1];
}

void TableOutput::OutputBlankColumns(int col)
{
    if (col < mCurrCol)
    {
        ExtOut("\n");
        mCurrCol = 0;
    }

    int whitespace = 0;
    for (int i = mCurrCol; i < col; ++i)
        whitespace += GetColumnWidth(i) + mPadding;

    ExtOut(GetWhitespace(whitespace));
}

void TableOutput::OutputIndent()
{
    if (mIndent)
        ExtOut(GetWhitespace(mIndent));
}

#ifndef FEATURE_PAL

PEOffsetMemoryReader::PEOffsetMemoryReader(TADDR moduleBaseAddress) :
    m_moduleBaseAddress(moduleBaseAddress),
    m_refCount(1)
    {}

HRESULT __stdcall PEOffsetMemoryReader::QueryInterface(REFIID riid, VOID** ppInterface)
{
    if(riid == __uuidof(IDiaReadExeAtOffsetCallback))
    {
        *ppInterface = static_cast<IDiaReadExeAtOffsetCallback*>(this);
        AddRef();
        return S_OK;
    }
    else if(riid == __uuidof(IUnknown))
    {
        *ppInterface = static_cast<IUnknown*>(this);
        AddRef();
        return S_OK;
    }
    else
    {
        return E_NOINTERFACE;
    }
}

ULONG __stdcall PEOffsetMemoryReader::AddRef()
{
    return InterlockedIncrement((volatile LONG *) &m_refCount);
}

ULONG __stdcall PEOffsetMemoryReader::Release()
{
    ULONG count = InterlockedDecrement((volatile LONG *) &m_refCount);
    if(count == 0)
    {
        delete this;
    }
    return count;
}

// IDiaReadExeAtOffsetCallback implementation
HRESULT __stdcall PEOffsetMemoryReader::ReadExecutableAt(DWORDLONG fileOffset, DWORD cbData, DWORD* pcbData, BYTE data[])
{
    return SafeReadMemory(m_moduleBaseAddress + fileOffset, data, cbData, pcbData) ? S_OK : E_FAIL;
}

PERvaMemoryReader::PERvaMemoryReader(TADDR moduleBaseAddress) :
    m_moduleBaseAddress(moduleBaseAddress),
    m_refCount(1)
    {}

HRESULT __stdcall PERvaMemoryReader::QueryInterface(REFIID riid, VOID** ppInterface)
{
    if(riid == __uuidof(IDiaReadExeAtRVACallback))
    {
        *ppInterface = static_cast<IDiaReadExeAtRVACallback*>(this);
        AddRef();
        return S_OK;
    }
    else if(riid == __uuidof(IUnknown))
    {
        *ppInterface = static_cast<IUnknown*>(this);
        AddRef();
        return S_OK;
    }
    else
    {
        return E_NOINTERFACE;
    }
}

ULONG __stdcall PERvaMemoryReader::AddRef()
{
    return InterlockedIncrement((volatile LONG *) &m_refCount);
}

ULONG __stdcall PERvaMemoryReader::Release()
{
    ULONG count = InterlockedDecrement((volatile LONG *) &m_refCount);
    if(count == 0)
    {
        delete this;
    }
    return count;
}

// IDiaReadExeAtOffsetCallback implementation
HRESULT __stdcall PERvaMemoryReader::ReadExecutableAtRVA(DWORD relativeVirtualAddress, DWORD cbData, DWORD* pcbData, BYTE data[])
{
    return SafeReadMemory(m_moduleBaseAddress + relativeVirtualAddress, data, cbData, pcbData) ? S_OK : E_FAIL;
}

#endif // FEATURE_PAL

static void AddAssemblyName(WString& methodOutput, CLRDATA_ADDRESS mdesc)
{
    DacpMethodDescData mdescData;
    if (SUCCEEDED(mdescData.Request(g_sos, mdesc)))
    {
        DacpModuleData dmd;
        if (SUCCEEDED(dmd.Request(g_sos, mdescData.ModulePtr)))
        {
            ToRelease<IXCLRDataModule> pModule;
            if (SUCCEEDED(g_sos->GetModule(mdescData.ModulePtr, &pModule)))
            {
                ArrayHolder<WCHAR> wszFileName = new WCHAR[MAX_LONGPATH + 1];
                ULONG32 nameLen = 0;
                if (SUCCEEDED(pModule->GetFileName(MAX_LONGPATH, &nameLen, wszFileName)))
                {
                    if (wszFileName[0] != W('\0'))
                    {
                        WCHAR *pJustName = _wcsrchr(wszFileName, GetTargetDirectorySeparatorW());
                        if (pJustName == NULL)
                            pJustName = wszFileName - 1;
                        methodOutput += (pJustName + 1);
                        methodOutput += W("!");
                    }
                }
            }
        }
    }
}

WString GetFrameFromAddress(TADDR frameAddr, IXCLRDataStackWalk *pStackWalk, BOOL bAssemblyName)
{
    TADDR vtAddr;
    MOVE(vtAddr, frameAddr);

    WString frameOutput;
    frameOutput += W("[");

    if (SUCCEEDED(g_sos->GetFrameName(TO_CDADDR(vtAddr), mdNameLen, g_mdName, NULL)))
        frameOutput += g_mdName;
    else
        frameOutput += W("Frame");

    frameOutput += WString(W(": ")) + Pointer(frameAddr) + W("] ");

    // Print the frame's associated function info, if it has any.
    CLRDATA_ADDRESS mdesc = 0;
    if (SUCCEEDED(g_sos->GetMethodDescPtrFromFrame(frameAddr, &mdesc)))
    {
        if (SUCCEEDED(g_sos->GetMethodDescName(mdesc, mdNameLen, g_mdName, NULL)))
        {
            if (bAssemblyName)
            {
                AddAssemblyName(frameOutput, mdesc);
            }

            frameOutput += g_mdName;
        }
        else
        {
            frameOutput += W("<unknown method>");
        }
    }
    else if (pStackWalk)
    {
        // The Frame did not have direct function info, so try to get the method instance
        // (in this case a MethodDesc), and read the name from it.
        ToRelease<IXCLRDataFrame> frame;
        if (SUCCEEDED(pStackWalk->GetFrame(&frame)))
        {
            ToRelease<IXCLRDataMethodInstance> methodInstance;
            if (SUCCEEDED(frame->GetMethodInstance(&methodInstance)))
            {
                // GetName can return S_FALSE if mdNameLen is not large enough.  However we are already
                // passing a pretty big buffer in.  If this returns S_FALSE (meaning the buffer is too
                // small) then we should not output it anyway.
                if (methodInstance->GetName(0, mdNameLen, NULL, g_mdName) == S_OK)
                    frameOutput += g_mdName;
            }
        }
    }

    return frameOutput;
}

WString MethodNameFromIP(CLRDATA_ADDRESS ip, BOOL bSuppressLines, BOOL bAssemblyName, BOOL bDisplacement, BOOL bAdjustIPForLineNumber)
{
    ULONG linenum;
    WString methodOutput;
    CLRDATA_ADDRESS mdesc = 0;

    if (FAILED(g_sos->GetMethodDescPtrFromIP(ip, &mdesc)))
    {
        methodOutput = W("<unknown>");
    }
    else
    {
        DacpMethodDescData mdescData;
        if (SUCCEEDED(g_sos->GetMethodDescName(mdesc, mdNameLen, g_mdName, NULL)))
        {
            if (bAssemblyName)
            {
                AddAssemblyName(methodOutput, mdesc);
            }

            methodOutput += g_mdName;

            if (bDisplacement)
            {
                if (SUCCEEDED(mdescData.Request(g_sos, mdesc)))
                {
                    ULONG64 disp = (ip - mdescData.NativeCodeAddr);
                    if (disp)
                    {
                        methodOutput += W(" + ");
                        methodOutput += Decimal(disp);
                    }
                }
            }
        }
        else if (SUCCEEDED(mdescData.Request(g_sos, mdesc)))
        {
            DacpModuleData dmd;
            BOOL bModuleNameWorked = FALSE;
            ULONG64 addrInModule = ip;
            if (SUCCEEDED(dmd.Request(g_sos, mdescData.ModulePtr)))
            {
                CLRDATA_ADDRESS peFileBase = 0;
                if (SUCCEEDED(g_sos->GetPEFileBase(dmd.PEAssembly, &peFileBase)))
                {
                    if (peFileBase)
                    {
                        addrInModule = peFileBase;
                    }
                }
            }
            ULONG Index;
            ULONG64 moduleBase;
            if (SUCCEEDED(g_ExtSymbols->GetModuleByOffset(UL64_TO_CDA(addrInModule), 0, &Index, &moduleBase)))
            {
                ArrayHolder<char> szModuleName = new char[MAX_LONGPATH+1];

                if (SUCCEEDED(g_ExtSymbols->GetModuleNames(Index, moduleBase, NULL, 0, NULL, szModuleName, MAX_LONGPATH, NULL, NULL, 0, NULL)))
                {
                    MultiByteToWideChar (CP_ACP, 0, szModuleName, MAX_LONGPATH, g_mdName, ARRAY_SIZE(g_mdName));
                    methodOutput += g_mdName;
                    methodOutput += W("!");
                }
            }
            methodOutput += W("<unknown method>");
        }
        else
        {
            methodOutput = W("<unknown>");
        }

        ArrayHolder<WCHAR> wszFileName = new WCHAR[MAX_LONGPATH];
        if (!bSuppressLines &&
            SUCCEEDED(GetLineByOffset(TO_CDADDR(ip), &linenum, wszFileName, MAX_LONGPATH, bAdjustIPForLineNumber)))
        {
            methodOutput += WString(W(" [")) + wszFileName + W(" @ ") + Decimal(linenum) + W("]");
        }
    }

    return methodOutput;
}

HRESULT GetGCRefs(ULONG osID, SOSStackRefData **ppRefs, unsigned int *pRefCnt, SOSStackRefError **ppErrors, unsigned int *pErrCount)
{
    if (ppRefs == NULL || pRefCnt == NULL)
        return E_POINTER;

    if (pErrCount)
        *pErrCount = 0;

    *pRefCnt = 0;
    unsigned int count = 0;
    ToRelease<ISOSStackRefEnum> pEnum;
    if (FAILED(g_sos->GetStackReferences(osID, &pEnum)) || FAILED(pEnum->GetCount(&count)))
    {
        ExtOut("Failed to enumerate GC references.\n");
                return E_FAIL;
    }

    *ppRefs = new SOSStackRefData[count];
    if (FAILED(pEnum->Next(count, *ppRefs, pRefCnt)))
    {
        ExtOut("Failed to enumerate GC references.\n");
        return E_FAIL;
    }

    SOS_Assert(count == *pRefCnt);

    // Enumerate errors found.  Any bad HRESULT recieved while enumerating errors is NOT a fatal error.
    // Hence we return S_FALSE if we encounter one.

    if (ppErrors && pErrCount)
    {
        ToRelease<ISOSStackRefErrorEnum> pErrors;
        if (FAILED(pEnum->EnumerateErrors(&pErrors)))
        {
            ExtOut("Failed to enumerate GC reference errors.\n");
            return S_FALSE;
        }

        if (FAILED(pErrors->GetCount(&count)))
        {
            ExtOut("Failed to enumerate GC reference errors.\n");
            return S_FALSE;
        }

        *ppErrors = new SOSStackRefError[count];
        if (FAILED(pErrors->Next(count, *ppErrors, pErrCount)))
        {
            ExtOut("Failed to enumerate GC reference errors.\n");
            *pErrCount = 0;
            return S_FALSE;
        }

        SOS_Assert(count == *pErrCount);
    }
    return S_OK;
}


InternalFrameManager::InternalFrameManager() : m_cInternalFramesActual(0), m_iInternalFrameCur(0) {}

HRESULT InternalFrameManager::Init(ICorDebugThread3 * pThread3)
{
    _ASSERTE(pThread3 != NULL);

    return pThread3->GetActiveInternalFrames(
        ARRAY_SIZE(m_rgpInternalFrame2),
        &m_cInternalFramesActual,
        &(m_rgpInternalFrame2[0]));
}

HRESULT InternalFrameManager::PrintPrecedingInternalFrames(ICorDebugFrame * pFrame)
{
    HRESULT Status;

    for (; m_iInternalFrameCur < m_cInternalFramesActual; m_iInternalFrameCur++)
    {
        BOOL bIsCloser = FALSE;
        IfFailRet(m_rgpInternalFrame2[m_iInternalFrameCur]->IsCloserToLeaf(pFrame, &bIsCloser));

        if (!bIsCloser)
        {
            // Current internal frame is now past pFrame, so we're done
            return S_OK;
        }

        IfFailRet(PrintCurrentInternalFrame());
    }

    // Exhausted list of internal frames.  Done!
    return S_OK;
}

HRESULT InternalFrameManager::PrintCurrentInternalFrame()
{
    _ASSERTE(m_iInternalFrameCur < m_cInternalFramesActual);

    HRESULT Status;

    CORDB_ADDRESS address;
    IfFailRet(m_rgpInternalFrame2[m_iInternalFrameCur]->GetAddress(&address));

    ToRelease<ICorDebugInternalFrame> pInternalFrame;
    IfFailRet(m_rgpInternalFrame2[m_iInternalFrameCur]->QueryInterface(IID_ICorDebugInternalFrame, (LPVOID *) &pInternalFrame));

    CorDebugInternalFrameType type;
    IfFailRet(pInternalFrame->GetFrameType(&type));

    LPCSTR szFrameType = NULL;
    switch(type)
    {
    default:
        szFrameType = "Unknown internal frame.";
        break;

    case STUBFRAME_M2U:
        szFrameType = "Managed to Unmanaged transition";
        break;

    case STUBFRAME_U2M:
        szFrameType = "Unmanaged to Managed transition";
        break;

    case STUBFRAME_APPDOMAIN_TRANSITION:
        szFrameType = "AppDomain transition";
        break;

    case STUBFRAME_LIGHTWEIGHT_FUNCTION:
        szFrameType = "Lightweight function";
        break;

    case STUBFRAME_FUNC_EVAL:
        szFrameType = "Function evaluation";
        break;

    case STUBFRAME_INTERNALCALL:
        szFrameType = "Internal call";
        break;

    case STUBFRAME_CLASS_INIT:
        szFrameType = "Class initialization";
        break;

    case STUBFRAME_EXCEPTION:
        szFrameType = "Exception";
        break;

    case STUBFRAME_SECURITY:
        szFrameType = "Security";
        break;

    case STUBFRAME_JIT_COMPILATION:
        szFrameType = "JIT Compilation";
        break;
    }

    DMLOut("%p %s ", SOS_PTR(address), SOS_PTR(0));
    ExtOut("[%s: %p]\n", szFrameType, SOS_PTR(address));

    return S_OK;
}

#ifdef FEATURE_PAL

struct MemoryRegion
{
private:
    uint64_t m_startAddress;
    uint64_t m_endAddress;
    CLRDATA_ADDRESS m_peFile;
    BYTE* m_metadataMemory;
    volatile LONG m_busy;

    HRESULT CacheMetadata()
    {
        if (m_metadataMemory == nullptr)
        {
            HRESULT hr;
            CLRDATA_ADDRESS baseAddress;
            if (FAILED(hr = g_sos->GetPEFileBase(m_peFile, &baseAddress))) {
                return hr;
            }
            ArrayHolder<WCHAR> imagePath = new WCHAR[MAX_LONGPATH];
            if (FAILED(hr = g_sos->GetPEFileName(m_peFile, MAX_LONGPATH, imagePath.GetPtr(), NULL))) {
                return hr;
            }
            IMAGE_DOS_HEADER DosHeader;
            if (FAILED(hr = g_ExtData->ReadVirtual(baseAddress, &DosHeader, sizeof(DosHeader), NULL))) {
                return hr;
            }
            IMAGE_NT_HEADERS Header;
            if (FAILED(hr = g_ExtData->ReadVirtual(baseAddress + DosHeader.e_lfanew, &Header, sizeof(Header), NULL))) {
                return hr;
            }
            // If there is no COMHeader, this can not be managed code.
            if (Header.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress == 0) {
                return E_ACCESSDENIED;
            }
            ULONG32 imageSize = Header.OptionalHeader.SizeOfImage;
            ULONG32 timeStamp = Header.FileHeader.TimeDateStamp;
            ULONG32 bufferSize = (ULONG32)Size();

            ArrayHolder<BYTE> buffer = new NOTHROW BYTE[bufferSize];
            if (buffer == nullptr) {
                return E_OUTOFMEMORY;
            }
            ULONG32 actualSize = 0;
            if (FAILED(hr = GetMetadataLocator(imagePath, timeStamp, imageSize, nullptr, 0, 0, bufferSize, buffer, &actualSize))) {
                return hr;
            }
            m_metadataMemory = buffer.Detach();
        }
        return S_OK;
    }

public:
    MemoryRegion(uint64_t start, uint64_t end, CLRDATA_ADDRESS peFile) :
        m_startAddress(start),
        m_endAddress(end),
        m_peFile(peFile),
        m_metadataMemory(nullptr),
        m_busy(0)
    {
    }

    uint64_t StartAddress() const { return m_startAddress; }
    uint64_t EndAddress() const { return m_endAddress; }
    uint64_t Size() const { return m_endAddress - m_startAddress; }

    CLRDATA_ADDRESS const PEFile() { return m_peFile; }

    bool operator<(const MemoryRegion& rhs) const
    {
        return (m_startAddress < rhs.m_startAddress) && (m_endAddress <= rhs.m_startAddress);
    }

    // Returns true if "rhs" is wholly contained in this one
    bool Contains(const MemoryRegion& rhs) const
    {
        return (m_startAddress <= rhs.m_startAddress) && (m_endAddress >= rhs.m_endAddress);
    }

    HRESULT ReadMetadata(CLRDATA_ADDRESS address, ULONG32 bufferSize, BYTE* buffer)
    {
        _ASSERTE((m_startAddress <= address) && (m_endAddress >= (address + bufferSize)));

        HRESULT hr = E_ACCESSDENIED;

        // Skip in-memory and dynamic modules or if CacheMetadata failed
        if (m_peFile != 0)
        {
            if (InterlockedIncrement(&m_busy) == 1)
            {
                // Attempt to get the assembly metadata from local file or by downloading from a symbol server
                hr = CacheMetadata();
                if (FAILED(hr)) {
                    // If we can get the metadata from the assembly, mark this region to always fail.
                    m_peFile = 0;
                }
            }
            InterlockedDecrement(&m_busy);
        }

        if (FAILED(hr)) {
            return hr;
        }

        // Read the memory from the cached metadata blob
        _ASSERTE(m_metadataMemory != nullptr);
        uint64_t offset = address - m_startAddress;
        memcpy(buffer, m_metadataMemory + offset, bufferSize);
        return S_OK;
    }

    void Dispose()
    {
        if (m_metadataMemory != nullptr)
        {
            delete[] m_metadataMemory;
            m_metadataMemory = nullptr;
        }
    }
};

std::set<MemoryRegion> g_metadataRegions;
bool g_metadataRegionsPopulated = false;

void FlushMetadataRegions()
{
    for (const MemoryRegion& region : g_metadataRegions)
    {
        const_cast<MemoryRegion&>(region).Dispose();
    }
    g_metadataRegions.clear();
    g_metadataRegionsPopulated = false;
}

void PopulateMetadataRegions()
{
    g_metadataRegions.clear();

    // Only populate the metadata regions if core dump
    if (IsDumpFile())
    {
        int numModule;
        ArrayHolder<DWORD_PTR> moduleList = ModuleFromName(NULL, &numModule);
        if (moduleList != nullptr)
        {
            for (int i = 0; i < numModule; i++)
            {
                DacpModuleData moduleData;
                if (SUCCEEDED(moduleData.Request(g_sos, moduleList[i])))
                {
                    if (moduleData.metadataStart != 0)
                    {
                        MemoryRegion region(moduleData.metadataStart, moduleData.metadataStart + moduleData.metadataSize, moduleData.PEAssembly);
                        g_metadataRegions.insert(region);
#ifdef DUMP_METADATA_INFO
                        ArrayHolder<WCHAR> name = new WCHAR[MAX_LONGPATH];
                        name[0] = '\0';
                        if (moduleData.File != 0)
                        {
                            g_sos->GetPEFileName(moduleData.File, MAX_LONGPATH, name.GetPtr(), NULL);
                        }
                        ExtOut("%016x %016x %016x %S\n", moduleData.metadataStart, moduleData.metadataStart + moduleData.metadataSize, moduleData.metadataSize, name.GetPtr());
#endif
                    }
                }
            }
        }
        else
        {
            ExtDbgOut("PopulateMetadataRegions ModuleFromName returns null\n");
        }
    }
}

HRESULT GetMetadataMemory(CLRDATA_ADDRESS address, ULONG32 bufferSize, BYTE* buffer)
{
    // Populate the metadata memory region map
    if (!g_metadataRegionsPopulated)
    {
        g_metadataRegionsPopulated = true;
        PopulateMetadataRegions();
    }
    // Check if the memory address is in a metadata memory region
    MemoryRegion region(address, address + bufferSize, 0);
    const auto& found = g_metadataRegions.find(region);
    if (found != g_metadataRegions.end() && found->Contains(region)) {
        return const_cast<MemoryRegion&>(*found).ReadMetadata(address, bufferSize, buffer);
    }
    return E_ACCESSDENIED;
}

#endif // FEATURE_PAL

///////////////////////////////////////////////////////////////////////////////////////////
//
// Miscellaneous helper methods
//

void EnumerateThreadPoolGlobalWorkItemConcurrentQueue(
    DWORD_PTR workItemsConcurrentQueuePtr,
    const char *queueName,
    HeapStat *stats)
{
    // Get its head segment.
    sos::Object workItemsConcurrentQueue = TO_TADDR(workItemsConcurrentQueuePtr);
    int offset = GetObjFieldOffset(workItemsConcurrentQueue.GetAddress(), workItemsConcurrentQueue.GetMT(), W("_head"));
    if (offset <= 0)
    {
        return;
    }

    // Now, walk from segment to segment, each of which contains an array of work items.
    DWORD_PTR segmentPtr;
    MOVE(segmentPtr, workItemsConcurrentQueue.GetAddress() + offset);
    while (sos::IsObject(segmentPtr, false))
    {
        sos::Object segment = TO_TADDR(segmentPtr);

        // Get the work items array.  It's an array of Slot structs, which starts with the T.
        offset = GetObjFieldOffset(segment.GetAddress(), segment.GetMT(), W("_slots"));
        if (offset <= 0)
        {
            break;
        }

        DWORD_PTR slotsPtr;
        MOVE(slotsPtr, segment.GetAddress() + offset);
        if (!sos::IsObject(slotsPtr, false))
        {
            break;
        }

        // Walk every element in the array, outputting details on non-null work items.
        DacpObjectData slotsArray;
        if (slotsArray.Request(g_sos, TO_CDADDR(slotsPtr)) == S_OK && slotsArray.ObjectType == OBJ_ARRAY)
        {
            for (int i = 0; i < slotsArray.dwNumComponents; i++)
            {
                DWORD_PTR workItemPtr;
                MOVE(workItemPtr, slotsArray.ArrayDataPtr + (i * slotsArray.dwComponentSize)); // the item object reference is at the beginning of the Slot
                if (workItemPtr != NULL && sos::IsObject(TO_CDADDR(workItemPtr), false))
                {
                    sos::Object workItem = TO_TADDR(workItemPtr);
                    stats->Add((DWORD_PTR)workItem.GetMT(), (DWORD)workItem.GetSize());
                    DMLOut("%" THREAD_POOL_WORK_ITEM_TABLE_QUEUE_WIDTH "s %s %S", queueName, DMLObject(workItem.GetAddress()), workItem.GetTypeName());
                    if ((offset = GetObjFieldOffset(workItem.GetAddress(), workItem.GetMT(), W("_callback"))) > 0 ||
                        (offset = GetObjFieldOffset(workItem.GetAddress(), workItem.GetMT(), W("m_action"))) > 0)
                    {
                        DWORD_PTR delegatePtr;
                        MOVE(delegatePtr, workItem.GetAddress() + offset);
                        CLRDATA_ADDRESS md;
                        if (TryGetMethodDescriptorForDelegate(TO_CDADDR(delegatePtr), &md))
                        {
                            NameForMD_s((DWORD_PTR)md, g_mdName, mdNameLen);
                            ExtOut(" => %S", g_mdName);
                        }
                    }
                    ExtOut("\n");
                }
            }
        }

        // Move to the next segment.
        DacpFieldDescData segmentField;
        offset = GetObjFieldOffset(segment.GetAddress(), segment.GetMT(), W("_nextSegment"), TRUE, &segmentField);
        if (offset <= 0)
        {
            break;
        }

        MOVE(segmentPtr, segment.GetAddress() + offset);
        if (segmentPtr == NULL)
        {
            break;
        }
    }
}
