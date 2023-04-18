// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
// 
 
// 
// ==--==
#ifndef __exts_h__
#define __exts_h__

#define KDEXT_64BIT

#include <windows.h>
#include <winternl.h>

#if defined(_MSC_VER)
#pragma warning(disable:4245)   // signed/unsigned mismatch
#pragma warning(disable:4100)   // unreferenced formal parameter
#pragma warning(disable:4201)   // nonstandard extension used : nameless struct/union
#pragma warning(disable:4127)   // conditional expression is constant
#pragma warning(disable:4430)   // missing type specifier: C++ doesn't support default-int
#endif
#include "strike.h"
#include <wdbgexts.h>
#include <dbgeng.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <functional>

// wdbgexts.h defines StackTrace which interferes with other parts of the
// system that use the StackTrace identifier
#ifdef StackTrace 
#undef StackTrace
#endif

#include "platformspecific.h"

// We need to define the target address type.  This has to be used in the
// functions that read directly from the debuggee address space, vs. using
// the DAC to read the DAC-ized data structures.
#include "daccess.h"
#include "gcinfo.h"

// Convert between CLRDATA_ADDRESS and TADDR.
#define TO_TADDR(cdaddr) ((TADDR)(cdaddr))
#define TO_CDADDR(taddr) ((CLRDATA_ADDRESS)(LONG_PTR)(taddr))

// the macro below "corrects" a CDADDR to always represent the
// sign-extended equivalent ULONG64 value of the original TADDR
#define UL64_TO_CDA(ul64) (TO_CDADDR(TO_TADDR(ul64)))

// The macro below removes the sign extension, returning the  
// equivalent ULONG64 value to the original TADDR. Useful when 
// printing CDA values.
#define CDA_TO_UL64(cda) ((ULONG64)(TO_TADDR(cda)))

typedef struct _TADDR_RANGE
{
    TADDR start;
    TADDR end;
} TADDR_RANGE;

typedef struct _TADDR_SEGINFO
{
    TADDR segAddr;
    TADDR start;
    TADDR end;
} TADDR_SEGINFO;

#include "util.h"

#ifndef FEATURE_PAL
#include "dbgengservices.h"
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Cleanup tasks to be executed when the extension is unloaded
class OnUnloadTask
{
public:
    FORCEINLINE static void Register(void (*fn)())
    {
        // append a new unload task to the head of the list
        OnUnloadTask *pNew = new OnUnloadTask(fn);
        pNew->pNext = s_pUnloadTaskList;
        s_pUnloadTaskList = pNew;
    }

    static void Run()
    {
        // walk the list of UnloadTasks and execute each in turn
        OnUnloadTask* pCur = s_pUnloadTaskList;
        while (pCur != NULL)
        {
            OnUnloadTask* pNext = pCur->pNext;
            pCur->OnUnloadFn();
            delete pCur;
            pCur = pNext;
        }
        s_pUnloadTaskList = NULL;
    }

private:
    OnUnloadTask(void(*fn)())
        : OnUnloadFn(fn)
        , pNext(NULL)
    { }

private:
    void (*OnUnloadFn)();
    OnUnloadTask* pNext;

    static OnUnloadTask *s_pUnloadTaskList;
};

//-----------------------------------------------------------------------------------------
// Extension helper class
//-----------------------------------------------------------------------------------------
class SOSExtensions : public Extensions
{
    SOSExtensions(IDebuggerServices* debuggerServices, IHost* host) :
        Extensions(debuggerServices)
    {
        m_pHost = host;
        OnUnloadTask::Register(SOSExtensions::Uninitialize);
    }

#ifndef FEATURE_PAL
    ~SOSExtensions()
    {
        if (m_pDebuggerServices != nullptr)
        {
            ((DbgEngServices*)m_pDebuggerServices)->Uninitialize();
            m_pDebuggerServices->Release();
            m_pDebuggerServices = nullptr;
        }
    }
#endif

public:

#ifndef FEATURE_PAL
    static HRESULT Initialize(IDebugClient* client)
    {
        if (s_extensions == nullptr)
        {
            DbgEngServices* debuggerServices = new DbgEngServices(client);
            HRESULT hr = debuggerServices->Initialize();
            if (FAILED(hr)) {
                return hr;
            }
            s_extensions = new SOSExtensions(debuggerServices, nullptr);
        }
        return S_OK;
    }
#endif

    static HRESULT Initialize(IHost* host, IDebuggerServices* debuggerServices)
    {
        if (s_extensions == nullptr) 
        {
            s_extensions = new SOSExtensions(debuggerServices, host);
        }
        return S_OK;
    }

    static void Uninitialize()
    {
        if (s_extensions != nullptr)
        {
            delete s_extensions;
            s_extensions = nullptr;
        }
    }

#ifndef FEATURE_PAL
    void FlushCheck()
    {
        if (m_pDebuggerServices != nullptr)
        {
            ((DbgEngServices*)m_pDebuggerServices)->FlushCheck(this);
        }
    }
#endif

    IHost* GetHost();
};

extern HRESULT GetRuntime(IRuntime** ppRuntime);

#ifndef MINIDUMP
 
#define EXIT_API     ExtRelease

// Safe release and NULL.
#define EXT_RELEASE(Unk) \
    ((Unk) != NULL ? ((Unk)->Release(), (Unk) = NULL) : NULL)

extern PDEBUG_CONTROL2       g_ExtControl;
extern PDEBUG_DATA_SPACES    g_ExtData;
extern PDEBUG_SYMBOLS        g_ExtSymbols;
extern PDEBUG_SYMBOLS2       g_ExtSymbols2;
extern PDEBUG_SYSTEM_OBJECTS g_ExtSystem;
extern PDEBUG_REGISTERS      g_ExtRegisters;

#ifndef FEATURE_PAL

// Global variables initialized by query.
extern PDEBUG_CLIENT         g_ExtClient;
extern PDEBUG_DATA_SPACES2   g_ExtData2;
extern PDEBUG_ADVANCED       g_ExtAdvanced;

bool
IsInitializedByDbgEng();

#else // FEATURE_PAL

extern ILLDBServices*        g_ExtServices;    
extern ILLDBServices2*       g_ExtServices2;    

#define IsInitializedByDbgEng() false

#endif // FEATURE_PAL

HRESULT
ExtQuery(PDEBUG_CLIENT client);

HRESULT 
ArchQuery(void);

void
ExtRelease(void);

extern BOOL ControlC;

inline BOOL IsInterrupt()
{
    if (!ControlC && g_ExtControl->GetInterrupt() == S_OK)
    {
        ExtOut("Command canceled at the user's request.\n");
        ControlC = TRUE;
    }
    return ControlC;
}

//
// undef the wdbgexts
//
#undef DECLARE_API

#define DECLARE_API(extension)     \
CPPMOD HRESULT CALLBACK extension(PDEBUG_CLIENT client, PCSTR args)

class __ExtensionCleanUp
{
public:
    __ExtensionCleanUp(){}
    ~__ExtensionCleanUp(){ExtRelease();}
};

inline void EENotLoadedMessage(HRESULT Status)
{
#ifdef FEATURE_PAL
    ExtOut("Failed to find runtime module (%s), 0x%08x\n", GetRuntimeDllName(IRuntime::Core), Status);
#else
    ExtOut("Failed to find runtime module (%s or %s or %s), 0x%08x\n", GetRuntimeDllName(IRuntime::Core), GetRuntimeDllName(IRuntime::WindowsDesktop), GetRuntimeDllName(IRuntime::UnixCore), Status);
#endif
    ExtOut("Extension commands need it in order to have something to do.\n");
    ExtOut("For more information see https://go.microsoft.com/fwlink/?linkid=2135652\n");
}

inline void DACMessage(HRESULT Status)
{
    ExtOut("Failed to load data access module, 0x%08x\n", Status);
    if (GetHost()->GetHostType() == IHost::HostType::DbgEng)
    {
        ExtOut("Verify that 1) you have a recent build of the debugger (10.0.18317.1001 or newer)\n");
        ExtOut("            2) the file %s that matches your version of %s is\n", GetDacDllName(), GetRuntimeDllName());
        ExtOut("                in the version directory or on the symbol path\n");
        ExtOut("            3) or, if you are debugging a dump file, verify that the file\n");
        ExtOut("                %s_<arch>_<arch>_<version>.dll is on your symbol path.\n", GetDacModuleName());
        ExtOut("            4) you are debugging on a platform and architecture that supports this\n");
        ExtOut("                the dump file. For example, an ARM dump file must be debugged\n");
        ExtOut("                on an X86 or an ARM machine; an AMD64 dump file must be\n");
        ExtOut("                debugged on an AMD64 machine.\n");
        ExtOut("\n");
        ExtOut("You can run the command '!setclrpath <directory>' to control the load path of %s.\n", GetDacDllName());
        ExtOut("\n");
        ExtOut("Or you can also run the debugger command .cordll to control the debugger's\n");
        ExtOut("load of %s. .cordll -ve -u -l will do a verbose reload.\n", GetDacDllName());
        ExtOut("If that succeeds, the SOS command should work on retry.\n");
        ExtOut("\n");
        ExtOut("If you are debugging a minidump, you need to make sure that your executable\n");
        ExtOut("path is pointing to %s as well.\n", GetRuntimeDllName());
    }
    else
    {
        if (Status == CORDBG_E_MISSING_DEBUGGER_EXPORTS)
        {
            ExtOut("You can run the debugger command 'setclrpath <directory>' to control the load of %s.\n", GetDacDllName());
            ExtOut("If that succeeds, the SOS command should work on retry.\n");
        }
        else
        {
            ExtOut("Can not load or initialize %s. The target runtime may not be initialized.\n", GetDacDllName());
        }
    }
    ExtOut("\n");
    ExtOut("For more information see https://go.microsoft.com/fwlink/?linkid=2135652\n");
}

// The minimum initialization for a command
#define INIT_API_EXT()                                          \
    HRESULT Status;                                             \
    __ExtensionCleanUp __extensionCleanUp;                      \
    if ((Status = ExtQuery(client)) != S_OK) return Status;     \
    ControlC = FALSE;                                           \
    g_bDacBroken = TRUE;                                        \
    g_clrData = NULL;                                           \
    g_sos = NULL;                                        

// Also initializes the target machine
#define INIT_API_NOEE()                                         \
    INIT_API_EXT()                                              \
    if ((Status = ArchQuery()) != S_OK) return Status;

#define INIT_API_EE()                                           \
    if ((Status = GetRuntime(&g_pRuntime)) != S_OK)             \
    {                                                           \
        EENotLoadedMessage(Status);                             \
        return Status;                                          \
    }                                                           

#define INIT_API_NODAC()                                        \
    INIT_API_NOEE()                                             \
    INIT_API_EE()

#define INIT_API_DAC()                                          \
    if ((Status = LoadClrDebugDll()) != S_OK)                   \
    {                                                           \
        DACMessage(Status);                                     \
        return Status;                                          \
    }                                                           \
    g_bDacBroken = FALSE;                                       \
    /* If LoadClrDebugDll() succeeded make sure we release g_clrData. */  \
    /* We may reconsider caching g_clrData in the future */     \
    ToRelease<IXCLRDataProcess> spIDP(g_clrData);               \
    ToRelease<ISOSDacInterface> spISD(g_sos);                   \
    ResetGlobals();

#define INIT_API()                                              \
    INIT_API_NODAC()                                            \
    INIT_API_DAC()

// Attempt to initialize DAC and SOS globals, but do not "return" on failure.
// Instead, mark the failure to initialize the DAC by setting g_bDacBroken to TRUE.
// This should be used from extension commands that should work OK even when no
// runtime is loaded in the debuggee, e.g. DumpLog, DumpStack. These extensions
// and functions they call should test g_bDacBroken before calling any DAC enabled
// feature.
#define INIT_API_NO_RET_ON_FAILURE()                            \
    INIT_API_NODAC()                                             \
    if ((Status = LoadClrDebugDll()) != S_OK)              \
    {                                                           \
        ExtOut("Failed to load data access module (%s), 0x%08x\n", GetDacDllName(), Status); \
        ExtOut("Some functionality may be impaired\n");         \
    }                                                           \
    else                                                        \
    {                                                           \
        g_bDacBroken = FALSE;                                   \
        ResetGlobals();                                         \
    }                                                           \
    /* If LoadClrDebugDll() succeeded make sure we release g_clrData. */  \
    /* We may reconsider caching g_clrData in the future */     \
    ToRelease<ISOSDacInterface> spISD(g_sos);                   \
    ToRelease<IXCLRDataProcess> spIDP(g_clrData);
    
extern BOOL g_bDacBroken;

//-----------------------------------------------------------------------------------------
//
//  Target platform abstraction
//
//-----------------------------------------------------------------------------------------

// some needed forward declarations
struct StackTrace_SimpleContext;
struct GCEncodingInfo;
struct SOSEHInfo;
class GCDump; 

///
/// IMachine interface
///
/// Note: 
/// The methods accepting target address args take them as size_t==DWORD_PTR==TADDR,
/// which means this can only provide cross platform support for same-word size 
/// architectures (only ARM on x86 currently). Since this is not exposed outside SOS
/// and since the some-word-size limitation exists across EE/DAC/SOS this is not an
/// actual limitation.
///

class IMachine
{
public:
    // Returns the IMAGE_FILE_MACHINE_*** constant corresponding to the target machine
    virtual ULONG GetPlatform() const = 0;

    // Returns the size of the CONTEXT for the target machine
    virtual ULONG GetContextSize() const = 0;

    // Returns the architecture's DT_CONTEXT_FULL flags 
    virtual ULONG GetFullContextFlags() const = 0;

    // Sets the context flags in the context
    virtual void SetContextFlags(BYTE* context, ULONG32 contextFlags) = 0;

    // Disassembles a managed method specified by the IPBegin-IPEnd range
    virtual void Unassembly(
                TADDR IPBegin, 
                TADDR IPEnd, 
                TADDR IPAskedFor, 
                TADDR GCStressCodeCopy, 
                GCEncodingInfo *pGCEncodingInfo, 
                SOSEHInfo *pEHInfo,
                BOOL bSuppressLines,
                BOOL bDisplayOffsets,
                std::function<void(ULONG*, UINT*, BYTE*)> displayIL) const = 0;

    // Validates whether retAddr represents a return address by unassembling backwards.
    // If the instruction before retAddr represents a target-specific call instruction
    // it attempts to identify the target of the call. If successful it sets *whereCalled
    // to the call target, otherwise it sets it to 0xffffffff.
    virtual void IsReturnAddress(
                TADDR retAddr, 
                TADDR* whereCalled) const = 0;

    // If, while unwinding the stack, "PC" represents a known return address in 
    // KiUserExceptionDispatcher, "stack" is used to retrieve an exception context record
    // in "cxr", and an exception record in "exr"
    virtual BOOL GetExceptionContext (
                TADDR stack, 
                TADDR PC, 
                TADDR *cxrAddr, 
                CROSS_PLATFORM_CONTEXT * cxr,
                TADDR *exrAddr, 
                PEXCEPTION_RECORD exr) const = 0;

    // Retrieves stack pointer, frame pointer, and instruction pointer from the target context
    virtual TADDR GetSP(const CROSS_PLATFORM_CONTEXT & ctx) const = 0;
    virtual TADDR GetBP(const CROSS_PLATFORM_CONTEXT & ctx) const = 0;
    virtual TADDR GetIP(const CROSS_PLATFORM_CONTEXT & ctx) const = 0;

    // Fills dest's data fields from a target specific context
    virtual void  FillSimpleContext(StackTrace_SimpleContext * dest, LPVOID srcCtx) const = 0;
    // Fills a target specific context, destCtx, from the idx-th location in a target specific
    // array of contexts that start at srcCtx
    virtual void  FillTargetContext(LPVOID destCtx, LPVOID srcCtx, int idx = 0) const = 0;

    // Retrieve some target specific output strings
    virtual LPCSTR GetDumpStackHeading() const = 0;
    virtual LPCSTR GetSPName() const = 0;
    // Retrieves the non-volatile registers reported to the GC
    virtual void GetGCRegisters(LPCSTR** regNames, unsigned int* cntRegs) const = 0;

    typedef void (*printfFtn)(const char* fmt, ...);
    // Dumps the GCInfo
    virtual void DumpGCInfo(GCInfoToken gcInfoToken, unsigned methodSize, printfFtn gcPrintf, bool encBytes, bool bPrintHeader) const = 0;
    // The amount of bytes to adjust the IP for software exception throw instructions (the STACKWALK_CONTROLPC_ADJUST_OFFSET define in the runtime)
    virtual int StackWalkIPAdjustOffset() const = 0;

protected:
    IMachine()           {}
    virtual ~IMachine()  {}
    
private:
    IMachine(const IMachine& machine);      // undefined
    IMachine & operator=(const IMachine&);   // undefined
}; // class IMachine


extern IMachine* g_targetMachine;


inline BOOL IsDbgTargetX86()    { return g_targetMachine->GetPlatform() == IMAGE_FILE_MACHINE_I386; }
inline BOOL IsDbgTargetAmd64()  { return g_targetMachine->GetPlatform() == IMAGE_FILE_MACHINE_AMD64; }
inline BOOL IsDbgTargetArm()    { return g_targetMachine->GetPlatform() == IMAGE_FILE_MACHINE_ARMNT; }
inline BOOL IsDbgTargetArm64()  { return g_targetMachine->GetPlatform() == IMAGE_FILE_MACHINE_ARM64; }
inline BOOL IsDbgTargetWin64()  { return IsDbgTargetAmd64(); }

/* Returns the instruction pointer for the given CONTEXT.  We need this and its family of
 * functions because certain headers are inconsistantly included on the various platforms,
 * meaning that we cannot use GetIP and GetSP as defined by CLR.
 */
inline CLRDATA_ADDRESS GetIP(const CROSS_PLATFORM_CONTEXT& context)
{
    return TO_CDADDR(g_targetMachine->GetIP(context));
}

/* Returns the stack pointer for the given CONTEXT.
 */
inline CLRDATA_ADDRESS GetSP(const CROSS_PLATFORM_CONTEXT& context)
{
    return TO_CDADDR(g_targetMachine->GetSP(context));
}

/* Returns the base/frame pointer for the given CONTEXT.
 */
inline CLRDATA_ADDRESS GetBP(const CROSS_PLATFORM_CONTEXT& context)
{
    return TO_CDADDR(g_targetMachine->GetBP(context));
}

//-----------------------------------------------------------------------------------------
//
//  api declaration macros & api access macros
//
//-----------------------------------------------------------------------------------------

#ifndef FEATURE_PAL

extern WINDBG_EXTENSION_APIS ExtensionApis;
#define GetExpression (ExtensionApis.lpGetExpressionRoutine)

#else // FEATURE_PAL

#define GetExpression(exp) g_ExtServices->GetExpression(exp)

#endif // FEATURE_PAL

#define CACHE_SIZE  DT_OS_PAGE_SIZE
 
struct ReadVirtualCache
{
    BYTE   m_cache[CACHE_SIZE];
    TADDR  m_startCache;
    BOOL   m_cacheValid;
    ULONG  m_cacheSize;

    ReadVirtualCache() { Clear(); }
    HRESULT Read(TADDR Offset, PVOID Buffer, ULONG BufferSize, PULONG lpcbBytesRead);    
    void Clear() { m_cacheValid = FALSE; m_cacheSize = CACHE_SIZE; }
};

extern ReadVirtualCache *rvCache;

#define MOVE(dst, src) rvCache->Read(TO_TADDR(src), &(dst), sizeof(dst), NULL)
#define MOVEBLOCK(dst, src, size) rvCache->Read(TO_TADDR(src), &(dst), size, NULL)

#define moveN(dst, src)                               \
{                                                     \
    HRESULT ret = MOVE(dst, src);                     \
    if (FAILED(ret)) return ret;                      \
}

#define moveBlockN(dst, src, size)                    \
{                                                     \
    HRESULT ret = MOVEBLOCK(dst, src, size);          \
    if (FAILED(ret)) return ret;                      \
}

// move cross-process: reads memory from the debuggee into
// debugger address space and returns in case of error
#define move_xp(dst, src)                             \
{                                                     \
    HRESULT ret = MOVE(dst, src);                     \
    if (FAILED(ret)) return;                          \
}

#define moveBlock(dst, src, size)                     \
{                                                     \
    HRESULT ret = MOVEBLOCK(dst, src, size);          \
    if (FAILED(ret)) return;                          \
}

#ifdef __cplusplus
#define CPPMOD extern "C"
#else
#define CPPMOD
#endif

#endif

#ifdef __cplusplus
}
#endif

#endif // __exts_h__
