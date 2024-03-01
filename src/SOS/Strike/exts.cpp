// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
// 
 
// 
// ==--==
#include "exts.h"
#include "disasm.h"
#ifndef FEATURE_PAL

#define VER_PRODUCTVERSION_W        (0x0100)

//
// globals
//
WINDBG_EXTENSION_APIS   ExtensionApis;

//
// Valid for the lifetime of the debug session.
//

PDEBUG_CLIENT         g_ExtClient;    
PDEBUG_DATA_SPACES2   g_ExtData2;
PDEBUG_ADVANCED       g_ExtAdvanced;

#else

DebugClient*          g_DebugClient;
ILLDBServices*        g_ExtServices;    
ILLDBServices2*       g_ExtServices2;    
bool                  g_palInitialized = false;

#endif // FEATURE_PAL

OnUnloadTask *OnUnloadTask::s_pUnloadTaskList = NULL;

IMachine* g_targetMachine = NULL;
BOOL      g_bDacBroken = FALSE;

PDEBUG_CONTROL2       g_ExtControl;
PDEBUG_DATA_SPACES    g_ExtData;
PDEBUG_REGISTERS      g_ExtRegisters;
PDEBUG_SYMBOLS        g_ExtSymbols;
PDEBUG_SYMBOLS2       g_ExtSymbols2;
PDEBUG_SYSTEM_OBJECTS g_ExtSystem;

#define SOS_ExtQueryFailGo(var, riid)                       \
    var = NULL;                                             \
    if ((Status = client->QueryInterface(__uuidof(riid),    \
                                 (void **)&var)) != S_OK)   \
    {                                                       \
        goto Fail;                                          \
    }

// Queries for all debugger interfaces.
#ifndef FEATURE_PAL    
HRESULT
ExtQuery(PDEBUG_CLIENT client)
{
    HRESULT Status;
    g_ExtClient = client;
#else
HRESULT
ExtQuery(ILLDBServices* services)
{
    if (!InitializePAL())
    {
        return E_FAIL;
    }
    g_ExtServices = services;

    HRESULT Status = services->QueryInterface(__uuidof(ILLDBServices2), (void**)&g_ExtServices2);
    if (FAILED(Status)) 
    {
        g_ExtServices = NULL;
        return Status;
    }
    DebugClient* client = new DebugClient(services, g_ExtServices2);
    g_DebugClient = client;
#endif
    SOS_ExtQueryFailGo(g_ExtControl, IDebugControl2);
    SOS_ExtQueryFailGo(g_ExtData, IDebugDataSpaces);
    SOS_ExtQueryFailGo(g_ExtRegisters, IDebugRegisters);
    SOS_ExtQueryFailGo(g_ExtSymbols, IDebugSymbols);
    SOS_ExtQueryFailGo(g_ExtSymbols2, IDebugSymbols2);
    SOS_ExtQueryFailGo(g_ExtSystem, IDebugSystemObjects);
#ifndef FEATURE_PAL
    SOS_ExtQueryFailGo(g_ExtData2, IDebugDataSpaces2);
    SOS_ExtQueryFailGo(g_ExtAdvanced, IDebugAdvanced);
#endif // FEATURE_PAL
    return Status;

 Fail:
    if (Status == E_OUTOFMEMORY)
        ReportOOM();
    
    ExtRelease();
    return Status;
}

IMachine*
GetTargetMachine(ULONG processorType)
{
    IMachine* targetMachine = NULL;
#ifdef SOS_TARGET_AMD64
    if (processorType == IMAGE_FILE_MACHINE_AMD64)
    {
        targetMachine = AMD64Machine::GetInstance();
    }
#endif // SOS_TARGET_AMD64
#ifdef SOS_TARGET_X86
    if (processorType == IMAGE_FILE_MACHINE_I386)
    {
        targetMachine = X86Machine::GetInstance();
    }
#endif // SOS_TARGET_X86
#ifdef SOS_TARGET_ARM
    switch (processorType)
    {
        case IMAGE_FILE_MACHINE_ARM:
        case IMAGE_FILE_MACHINE_THUMB:
        case IMAGE_FILE_MACHINE_ARMNT:
            targetMachine = ARMMachine::GetInstance();
            break;
    }
#endif // SOS_TARGET_ARM
#ifdef SOS_TARGET_ARM64
    if (processorType == IMAGE_FILE_MACHINE_ARM64)
    {
        targetMachine = ARM64Machine::GetInstance();
    }
#endif // SOS_TARGET_ARM64
#ifdef SOS_TARGET_RISCV64
    if (processorType == IMAGE_FILE_MACHINE_RISCV64)
    {
        targetMachine = RISCV64Machine::GetInstance();
    }
#endif // SOS_TARGET_RISCV64
    return targetMachine;
}

HRESULT
ArchQuery(void)
{
    ULONG processorType = 0;
    g_ExtControl->GetExecutingProcessorType(&processorType);

    g_targetMachine = GetTargetMachine(processorType);
    if (g_targetMachine == NULL)
    {
        const char* architecture = "";
        switch (processorType)
        {
            case IMAGE_FILE_MACHINE_AMD64:
                architecture = "x64";
                break;
            case IMAGE_FILE_MACHINE_I386:
                architecture = "x86";
                break;
            case IMAGE_FILE_MACHINE_ARM:
            case IMAGE_FILE_MACHINE_THUMB:
            case IMAGE_FILE_MACHINE_ARMNT:
                architecture = "arm32";
                break;
            case IMAGE_FILE_MACHINE_ARM64:
                architecture = "arm64";
                break;
            case IMAGE_FILE_MACHINE_RISCV64:
                architecture = "riscv64";
                break;
        }
        ExtErr("SOS does not support the current target architecture '%s' (0x%04x). A 32 bit target may require a 32 bit debugger or vice versa. In general, try to use the same bitness for the debugger and target process.\n",
            architecture, processorType);
        return E_FAIL;
    }
    return S_OK;
}

// Cleans up all debugger interfaces.
void
ExtRelease(void)
{
    EXT_RELEASE(g_ExtControl);
    EXT_RELEASE(g_ExtData);
    EXT_RELEASE(g_ExtRegisters);
    EXT_RELEASE(g_ExtSymbols);
    EXT_RELEASE(g_ExtSymbols2);
    EXT_RELEASE(g_ExtSystem);
#ifndef FEATURE_PAL
    EXT_RELEASE(g_ExtData2);
    EXT_RELEASE(g_ExtAdvanced);
    g_ExtClient = nullptr;
#else 
    EXT_RELEASE(g_DebugClient);
    EXT_RELEASE(g_ExtServices2);
    g_ExtServices = nullptr;
#endif // FEATURE_PAL
    ReleaseTarget();
}

// Executes managed extension commands. Returns E_NOTIMPL if the command doesn't exists.
HRESULT 
ExecuteCommand(PCSTR commandName, PCSTR args)
{
    if (commandName != nullptr && strlen(commandName) > 0)
    {
        IHostServices* hostServices = GetHostServices();
        if (hostServices != nullptr)
        {
            return hostServices->DispatchCommand(commandName, args, /* displayCommandNotFound */ false);
        }
    }
    return E_NOTIMPL;
}

void 
EENotLoadedMessage(HRESULT Status)
{
#ifdef FEATURE_PAL
    ExtOut("Failed to find runtime module (%s), 0x%08x\n", GetRuntimeDllName(IRuntime::Core), Status);
#else
    ExtOut("Failed to find runtime module (%s or %s or %s), 0x%08x\n", GetRuntimeDllName(IRuntime::Core), GetRuntimeDllName(IRuntime::WindowsDesktop), GetRuntimeDllName(IRuntime::UnixCore), Status);
#endif
    ExtOut("Extension commands need it in order to have something to do.\n");
    ExtOut("For more information see https://go.microsoft.com/fwlink/?linkid=2135652\n");
}

void 
DACMessage(HRESULT Status)
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

#ifndef FEATURE_PAL

BOOL IsMiniDumpFileNODAC();
extern HMODULE g_hInstance;

bool g_Initialized = false;
const char* g_sosPrefix = "";

bool IsInitializedByDbgEng()
{
    return g_Initialized;
}

extern "C"
HRESULT
CALLBACK
DebugExtensionInitialize(PULONG Version, PULONG Flags)
{
    HRESULT hr;

    *Version = DEBUG_EXTENSION_VERSION(2, 0);
    *Flags = 0;

    if (g_Initialized)
    {
        return S_OK;
    }
    g_Initialized = true;
    g_sosPrefix = "!";

    ReleaseHolder<IDebugClient> debugClient;
    if ((hr = DebugCreate(__uuidof(IDebugClient), (void **)&debugClient)) != S_OK)
    {
        return hr;
    }

    if ((hr = SOSExtensions::Initialize(debugClient)) != S_OK)
    {
        return hr;
    }

    ReleaseHolder<IDebugControl> debugControl;
    if ((hr = debugClient->QueryInterface(__uuidof(IDebugControl), (void **)&debugControl)) != S_OK)
    {
        return hr;
    }

    ExtensionApis.nSize = sizeof (ExtensionApis);
    if ((hr = debugControl->GetWindbgExtensionApis64(&ExtensionApis)) != S_OK)
    {
        return hr;
    }
    
    // Fixes the "Unable to read dynamic function table entries" error messages by disabling the WinDbg security
    // feature that prevents the loading of unknown out of proc stack walkers.
    debugControl->Execute(DEBUG_OUTCTL_IGNORE, ".settings set EngineInitialization.VerifyFunctionTableCallbacks=false", 
        DEBUG_EXECUTE_NOT_LOGGED | DEBUG_EXECUTE_NO_REPEAT);

    ExtQuery(debugClient);
    if (IsMiniDumpFileNODAC())
    {
        ExtOut (
            "----------------------------------------------------------------------------\n"
            "The user dump currently examined is a minidump. Consequently, only a subset\n"
            "of sos.dll functionality will be available. If needed, attaching to the live\n"
            "process or debugging a full dump will allow access to sos.dll's full feature\n"
            "set.\n"
            "To create a full user dump use the command: .dump /ma <filename>\n"
            "----------------------------------------------------------------------------\n");
    }
    ExtRelease();
    
    return S_OK;
}

extern "C"
void
CALLBACK
DebugExtensionNotify(ULONG Notify, ULONG64 /*Argument*/)
{
}

extern "C"
void
CALLBACK
DebugExtensionUninitialize(void)
{
    // Execute all registered cleanup tasks
    OnUnloadTask::Run();
    g_pRuntime = nullptr;
    g_Initialized = false;
}

BOOL WINAPI 
DllMain(HANDLE hInstance, DWORD dwReason, LPVOID lpReserved)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        g_hInstance = (HMODULE) hInstance;
    }
    return true;
}

#else // FEATURE_PAL

BOOL
InitializePAL()
{
    // Initialize the PAL only once
    if (!g_palInitialized)
    {
        if (PAL_InitializeDLL() != 0)
        {
            return false;
        }
        g_palInitialized = true;
    }
    return true;
}

HRESULT
DebugClient::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(IDebugControl2) ||
        InterfaceId == __uuidof(IDebugControl4) ||
        InterfaceId == __uuidof(IDebugDataSpaces) ||
        InterfaceId == __uuidof(IDebugSymbols) ||
        InterfaceId == __uuidof(IDebugSymbols2) ||
        InterfaceId == __uuidof(IDebugSystemObjects) ||
        InterfaceId == __uuidof(IDebugRegisters))
    {
        *Interface = this;
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

ULONG
DebugClient::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

ULONG
DebugClient::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

#endif // FEATURE_PAL

/// <summary>
/// Returns the host instance
/// 
/// * dotnet-dump - m_pHost has already been set by SOSInitializeByHost by SOS.Hosting
/// * lldb - m_pHost has already been set by SOSInitializeByHost by libsosplugin which gets it via the InitializeHostServices callback
/// * dbgeng - SOS.Extensions provides the instance via the InitializeHostServices callback
/// </summary>
IHost* SOSExtensions::GetHost()
{
    if (m_pHost == nullptr)
    {
#ifndef FEATURE_PAL
        // Initialize the hosting runtime which will call InitializeHostServices and set m_pHost to the host instance
        InitializeHosting();
#endif
        // Otherwise, use the local host instance (hostimpl.*) that creates a local target instance (targetimpl.*)
        if (m_pHost == nullptr)
        {
            m_pHost = Host::GetInstance();
        }
    }
    return m_pHost;
}

/// <summary>
/// Returns the runtime or fails if no target or current runtime
/// </summary>
/// <param name="ppRuntime">runtime instance</param>
/// <returns>error code</returns>
HRESULT GetRuntime(IRuntime** ppRuntime)
{
    SOSExtensions* extensions = (SOSExtensions*)Extensions::GetInstance();
    ITarget* target = extensions->GetTarget();
    if (target == nullptr)
    {
        return E_FAIL;
    }
#ifndef FEATURE_PAL
    extensions->FlushCheck();
#endif
    return target->GetRuntime(ppRuntime);
}

void FlushCheck()
{
#ifndef FEATURE_PAL
    SOSExtensions* extensions = (SOSExtensions*)Extensions::GetInstance();
    if (extensions != nullptr)
    {
        extensions->FlushCheck();
    }
#endif // !FEATURE_PAL
}
