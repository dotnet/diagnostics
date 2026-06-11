// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "strike.h"
#include "util.h"
#include <string>
#include <corhdr.h>
#include <cor.h>
#include <clrdata.h>
#include <dbghelp.h>
#include <cordebug.h>
#include <xcordebug.h>
#include <mscoree.h>
#include <psapi.h>
#include <clrinternal.h>
#include <metahost.h>
#include "runtimeimpl.h"
#include "datatarget.h"
#include "cordebugdatatarget.h"
#include "runtimeinfo.h"

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dlfcn.h>
#include <unistd.h>
#endif // !FEATURE_PAL

#define CORDBG_E_NO_IMAGE_AVAILABLE EMAKEHR(0x1c64)

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcessImpl2FnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    LPCWSTR pDacModulePath,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcessImplFnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    HMODULE hDacDll,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcess2FnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    HMODULE hDacDll,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HMODULE (STDAPICALLTYPE  *LoadLibraryWFnPtr)(LPCWSTR lpLibFileName);

// Current runtime instance
IRuntime* g_pRuntime = nullptr;

extern "C" bool TryGetSymbolWithCallback(
    bool (*readMemory)(void* address, void* buffer, size_t size),
    ULONG64 baseAddress,
    const char* symbolName,
    ULONG64* symbolAddress);

bool ReaderReadMemory(void* address, void* buffer, size_t size)
{
    ULONG read = 0;
    return SUCCEEDED(g_ExtData->ReadVirtual((ULONG64)address, buffer, (ULONG)size, &read));
}

/**********************************************************************\
 * Search all the modules in the process for the single-file host
\**********************************************************************/
static HRESULT GetSingleFileInfo(ITarget* target, PULONG pModuleIndex, PULONG64 pModuleAddress, RuntimeInfo** ppRuntimeInfo)
{
    _ASSERTE(pModuleIndex != nullptr);
    _ASSERTE(pModuleAddress != nullptr);

    // No debugger service instance means that SOS is hosted by dotnet-dump,
    // which does runtime enumeration in CLRMD. We should never get here.
    IDebuggerServices* debuggerServices = GetDebuggerServices();
    if (debuggerServices == nullptr) {
        return E_NOINTERFACE;
    }

    ULONG loaded, unloaded;
    HRESULT hr = g_ExtSymbols->GetNumberModules(&loaded, &unloaded);
    if (FAILED(hr)) {
        return hr;
    }

    const char* symbolName = "DotNetRuntimeInfo";
    for (ULONG index = 0; index < loaded; index++)
    {
        ULONG64 baseAddress;
        hr = g_ExtSymbols->GetModuleByIndex(index, &baseAddress);
        if (FAILED(hr)) {
            return hr;
        }
        ULONG64 symbolAddress;
        if (target->GetOperatingSystem() == ITarget::OperatingSystem::Linux ||
            target->GetOperatingSystem() == ITarget::OperatingSystem::OSX)
        {
            if (!::TryGetSymbolWithCallback(ReaderReadMemory, baseAddress, symbolName, &symbolAddress)) {
                continue;
            }
        }
        else
        {
            hr = debuggerServices->GetOffsetBySymbol(index, symbolName, &symbolAddress);
            if (FAILED(hr)) {
                continue;
            }
        }
        ULONG read = 0;
        ArrayHolder<BYTE> buffer = new BYTE[sizeof(RuntimeInfo)];
        hr = g_ExtData->ReadVirtual(symbolAddress, buffer, sizeof(RuntimeInfo), &read);
        if (FAILED(hr)) {
            return hr;
        }
        if (strcmp(((RuntimeInfo*)buffer.GetPtr())->Signature, "DotNetRuntimeInfo") != 0) {
            break;
        }
        if (((RuntimeInfo*)buffer.GetPtr())->Version <= 0) {
            break;
        }
        *pModuleIndex = index;
        *pModuleAddress = baseAddress;
        *ppRuntimeInfo = (RuntimeInfo*)buffer.Detach();
        return S_OK;
    }

    return E_FAIL;
}

/**********************************************************************\
 * Creates a desktop or .NET Core instance of the runtime class
\**********************************************************************/
HRESULT Runtime::CreateInstance(ITarget* target, RuntimeConfiguration configuration, Runtime **ppRuntime)
{
    PCSTR runtimeModuleName = ::GetRuntimeModuleName(configuration);
    ULONG moduleIndex = 0;
    ULONG64 moduleAddress = 0;
    ULONG64 moduleSize = 0;
    RuntimeInfo* runtimeInfo = nullptr;
    HRESULT hr = S_OK;

    if (*ppRuntime == nullptr)
    {
        // Check if the normal runtime module (coreclr.dll, libcoreclr.so, etc.) is loaded
        hr = g_ExtSymbols->GetModuleByModuleName(runtimeModuleName, 0, &moduleIndex, &moduleAddress);
        if (FAILED(hr))
        {
            // If the standard runtime module isn't loaded, try looking for a single-file program
            if (configuration != IRuntime::WindowsDesktop)
            {
                hr = GetSingleFileInfo(target, &moduleIndex, &moduleAddress, &runtimeInfo);
            }
        }

        // If the previous operations were successful, get the size of the runtime module
        if (SUCCEEDED(hr))
        {
#ifdef FEATURE_PAL
            hr = g_ExtServices2->GetModuleInfo(moduleIndex, nullptr, &moduleSize, nullptr, nullptr);
#else
            _ASSERTE(moduleAddress != 0);
            DEBUG_MODULE_PARAMETERS params;
            hr = g_ExtSymbols->GetModuleParameters(1, &moduleAddress, 0, &params);
            if (SUCCEEDED(hr))
            {
                moduleSize = params.Size;
            }
#endif
        }

        // If the previous operations were successful, create the Runtime instance
        if (SUCCEEDED(hr))
        {
            if (moduleSize > 0)
            {
                *ppRuntime = new Runtime(target, configuration, moduleIndex, moduleAddress, moduleSize, runtimeInfo);
            }
            else
            {
                ExtOut("Runtime (%s) module size == 0\n", runtimeModuleName);
                hr = E_INVALIDARG;
            }
        }
    }
    return hr;
}

/**********************************************************************\
 * Constructor
\**********************************************************************/
Runtime::Runtime(ITarget* target, RuntimeConfiguration configuration, ULONG index, ULONG64 address, ULONG64 size, RuntimeInfo* runtimeInfo) :
    m_ref(1),
    m_target(target),
    m_configuration(configuration),
    m_index(index),
    m_address(address),
    m_size(size),
    m_name(nullptr),
    m_runtimeInfo(runtimeInfo),
    m_runtimeDirectory(nullptr),
    m_dacFilePath(nullptr),
    m_cdacFilePath(nullptr),
    m_dbiFilePath(nullptr),
    m_clrDataProcess(nullptr),
    m_cdacDataProcess(nullptr),
    m_pCorDebugProcess(nullptr)
{
    _ASSERTE(index != -1);
    _ASSERTE(address != 0);
    _ASSERTE(size != 0);

    ArrayHolder<char> szModuleName = new char[MAX_LONGPATH + 1];
    HRESULT hr = g_ExtSymbols->GetModuleNames(index, 0, szModuleName, MAX_LONGPATH, NULL, NULL, 0, NULL, NULL, 0, NULL);
    if (SUCCEEDED(hr))
    {
        m_name = szModuleName.Detach();
    }
}

/**********************************************************************\
 * Destroys the runtime instance
\**********************************************************************/
Runtime::~Runtime()
{
    if (m_name != nullptr)
    {
        delete [] m_name;
        m_name = nullptr;
    }
    if (m_runtimeDirectory != nullptr)
    {
        free((void*)m_runtimeDirectory);
        m_runtimeDirectory = nullptr;
    }
    if (m_dacFilePath != nullptr)
    {
        free((void*)m_dacFilePath);
        m_dacFilePath = nullptr;
    }
    if (m_cdacFilePath != nullptr)
    {
        free((void*)m_cdacFilePath);
        m_cdacFilePath = nullptr;
    }
    if (m_dbiFilePath != nullptr)
    {
        free((void*)m_dbiFilePath);
        m_dbiFilePath = nullptr;
    }
    if (m_pCorDebugProcess != NULL)
    {
        m_pCorDebugProcess->Detach();
        m_pCorDebugProcess->Release();
        m_pCorDebugProcess = nullptr;
    }
    if (m_clrDataProcess != nullptr)
    {
        m_clrDataProcess->Release();
        m_clrDataProcess = nullptr;
    }
    if (m_cdacDataProcess != nullptr)
    {
        m_cdacDataProcess->Release();
        m_cdacDataProcess = nullptr;
    }
}

/**********************************************************************\
 * Returns the DAC module path to the rest of SOS.
\**********************************************************************/
LPCSTR Runtime::GetDacFilePath()
{
    // If the DAC path hasn't been set by the symbol download support, use the one in the runtime directory.
    if (m_dacFilePath == nullptr)
    {
        // No debugger service instance means that SOS is hosted by dotnet-dump,
        // which does runtime enumeration in CLRMD. We should never get here.
        IDebuggerServices* debuggerServices = GetDebuggerServices();
        if (debuggerServices == nullptr)
        {
            ExtDbgOut("GetDacFilePath: GetDebuggerServices returned nullptr\n");
            return nullptr;
        }
        BOOL dacSignatureVerificationEnabled = FALSE;
        HRESULT hr = debuggerServices->GetDacSignatureVerificationSettings(&dacSignatureVerificationEnabled);
        if (FAILED(hr) || dacSignatureVerificationEnabled)
        {
            ExtDbgOut("GetDacFilePath: GetDacSignatureVerificationSettings FAILED %08x or returned TRUE\n", hr);
            return nullptr;
        }
        LPCSTR directory = GetRuntimeDirectory();
        if (directory != nullptr)
        {
            std::string dacModulePath(directory);
            dacModulePath.append(DIRECTORY_SEPARATOR_STR_A);
            dacModulePath.append(GetDacDllName());
#ifdef FEATURE_PAL
            // If DAC file exists in the runtime directory
            if (access(dacModulePath.c_str(), F_OK) == 0)
#endif
            {
                m_dacFilePath = _strdup(dacModulePath.c_str());
            }
        }
    }
    return m_dacFilePath;
}

#ifndef FEATURE_PAL
extern HMODULE g_hInstance;
#else
// A file-local anchor used to resolve the directory of the SOS module via dladdr.
static void CDacModuleAnchor() {}
#endif

/**********************************************************************\
 * Returns the cDAC (mscordaccore_universal) module path bundled next to
 * sos in the diagnostics tool package, or nullptr when it isn't present.
 * The cDAC is shipped with the tool and is never downloaded.
\**********************************************************************/
LPCSTR Runtime::GetCDacFilePath()
{
    if (m_cdacFilePath == nullptr)
    {
        // The cDAC lives in the same directory as the loaded sos module (the host's platform
        // subfolder of the package), not in the target runtime directory.
        ArrayHolder<char> szSOSModulePath = new char[MAX_LONGPATH + 1];
#ifdef FEATURE_PAL
        Dl_info info;
        if (dladdr((void*)&CDacModuleAnchor, &info) == 0 || info.dli_fname == nullptr)
        {
            ExtDbgOut("GetCDacFilePath: dladdr failed to locate the sos module\n");
            return nullptr;
        }
        strcpy_s(szSOSModulePath.GetPtr(), MAX_LONGPATH, info.dli_fname);
#else
        if (GetModuleFileNameA(g_hInstance, szSOSModulePath, MAX_LONGPATH) == 0)
        {
            ExtDbgOut("GetCDacFilePath: GetModuleFileNameA failed %08x\n", HRESULT_FROM_WIN32(GetLastError()));
            return nullptr;
        }
#endif
        std::string cdacModulePath(szSOSModulePath.GetPtr());
        size_t lastSlash = cdacModulePath.rfind(DIRECTORY_SEPARATOR_CHAR_A);
        if (lastSlash == std::string::npos)
        {
            ExtDbgOut("GetCDacFilePath: failed to parse sos module directory from %s\n", cdacModulePath.c_str());
            return nullptr;
        }
        cdacModulePath.erase(lastSlash + 1);
        cdacModulePath.append(NETCORE_CDAC_DLL_NAME_A);

        // The cDAC must exist on disk next to sos; it is never downloaded. When it is not
        // bundled (for example, RIDs without a cDAC), callers fall back to the in-box DAC.
#ifdef FEATURE_PAL
        bool exists = access(cdacModulePath.c_str(), F_OK) == 0;
#else
        bool exists = GetFileAttributesA(cdacModulePath.c_str()) != INVALID_FILE_ATTRIBUTES;
#endif
        if (exists)
        {
            m_cdacFilePath = _strdup(cdacModulePath.c_str());
        }
    }
    return m_cdacFilePath;
}

/**********************************************************************\
 * Returns the DBI module path to the rest of SOS
\**********************************************************************/
LPCSTR Runtime::GetDbiFilePath()
{
    if (m_dbiFilePath == nullptr)
    {
        LPCSTR directory = GetRuntimeDirectory();
        if (directory != nullptr)
        {
            std::string dbiModulePath(directory);
            dbiModulePath.append(DIRECTORY_SEPARATOR_STR_A);
            dbiModulePath.append(NET_DBI_DLL_NAME_A);
#ifdef FEATURE_PAL
            // If DBI file exists in the runtime directory
            if (access(dbiModulePath.c_str(), F_OK) == 0)
#endif
            {
                m_dbiFilePath = _strdup(dbiModulePath.c_str());
            }
        }
    }
    return m_dbiFilePath;
}

/**********************************************************************\
 * Flushes DAC caches
\**********************************************************************/
void Runtime::Flush()
{
    if (m_clrDataProcess != nullptr)
    {
        m_clrDataProcess->Flush();
    }
    if (m_cdacDataProcess != nullptr)
    {
        m_cdacDataProcess->Flush();
    }
}

//----------------------------------------------------------------------------
// IUnknown
//----------------------------------------------------------------------------

HRESULT Runtime::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(IRuntime))
    {
        *Interface = (IRuntime*)this;
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

ULONG Runtime::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);
    return ref;
}

ULONG Runtime::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

//----------------------------------------------------------------------------
// IRuntime
//----------------------------------------------------------------------------

/**********************************************************************\
 * Set the runtime module directory to search for DAC/DBI
\**********************************************************************/
void Runtime::SetRuntimeDirectory(LPCSTR runtimeModuleDirectory)
{
    if (m_runtimeDirectory != nullptr)
    {
        free((void*)m_runtimeDirectory);
        m_runtimeDirectory = nullptr;
    }
    if (runtimeModuleDirectory != nullptr)
    {
        m_runtimeDirectory = _strdup(runtimeModuleDirectory);
    }
}

/**********************************************************************\
 * Returns the runtime directory
\**********************************************************************/
LPCSTR Runtime::GetRuntimeDirectory()
{
    if (m_runtimeDirectory == nullptr)
    {
        if (GetFileAttributesA(m_name) == INVALID_FILE_ATTRIBUTES)
        {
            ExtDbgOut("Error: Runtime module %s doesn't exist %08x\n", m_name, HRESULT_FROM_WIN32(GetLastError()));
            return nullptr;
        }
        // Parse off the file name
        char* runtimeDirectory = _strdup(m_name);
        char* lastSlash = strrchr(runtimeDirectory, GetTargetDirectorySeparatorW());
        if (lastSlash != nullptr)
        {
            *lastSlash = '\0';
        }
        m_runtimeDirectory = runtimeDirectory;
    }
    return m_runtimeDirectory;
}

/**********************************************************************\
 * Creates an instance of the DAC clr data process
\**********************************************************************/
HRESULT Runtime::GetClrDataProcess(ClrDataProcessFlags flags, IXCLRDataProcess** ppClrDataProcess)
{
    // When the cDAC is requested (e.g. by CLRMA or the main SOS DAC-load path) and the policy
    // selects it (supported runtime version, DOTNET_ENABLE_CDAC not deferring to the in-box DAC),
    // prefer it for the data-access path and fall back to the in-box DAC if it isn't bundled or
    // fails to initialize.
    if ((flags & ClrDataProcessFlags::UseCDac) != 0 && ShouldUseCDac())
    {
        if (m_cdacDataProcess == nullptr)
        {
            LPCSTR cdacFilePath = GetCDacFilePath();
            if (cdacFilePath != nullptr)
            {
                m_cdacDataProcess = CreateClrDataProcessInstance(cdacFilePath, GetContractDescriptorAddress());
            }
        }
        if (m_cdacDataProcess != nullptr)
        {
            *ppClrDataProcess = m_cdacDataProcess;
            return S_OK;
        }
        // Fall through to the DAC.
    }

    if (m_clrDataProcess == nullptr)
    {
        *ppClrDataProcess = nullptr;

        LPCSTR dacFilePath = GetDacFilePath();
        if (dacFilePath == nullptr)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }
        m_clrDataProcess = CreateClrDataProcessInstance(dacFilePath, 0);
        if (m_clrDataProcess == nullptr)
        {
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
        }
    }
    *ppClrDataProcess = m_clrDataProcess;
    return S_OK;
}

// The minimum runtime major version that supports the cDAC.
static const DWORD MinCDacRuntimeMajorVersion = 11;

// Returns true if the named environment variable is set to "1".
static bool IsEnvironmentVariableSetToOne(const char* name)
{
    char buffer[16];
    DWORD length = GetEnvironmentVariableA(name, buffer, ARRAY_SIZE(buffer));
    return length > 0 && length < ARRAY_SIZE(buffer) && strcmp(buffer, "1") == 0;
}

/**********************************************************************\
 * Evaluates the cDAC loading policy for this runtime.
\**********************************************************************/
bool Runtime::ShouldUseCDac()
{
    // When DOTNET_ENABLE_CDAC is requested, the in-box (legacy) DAC loads and drives the cDAC
    // contract reader itself (including its own dac-vs-cdac fallback/comparison). Defer to that
    // mechanism rather than loading the cDAC directly so those scenarios keep working.
    if (IsEnvironmentVariableSetToOne("DOTNET_ENABLE_CDAC") || IsEnvironmentVariableSetToOne("COMPlus_ENABLE_CDAC"))
    {
        return false;
    }

    // Use the cDAC only for runtimes that support it (.NET 11+).
    VS_FIXEDFILEINFO fileInfo;
    if (FAILED(GetEEVersion(&fileInfo, nullptr, 0)))
    {
        return false;
    }
    DWORD majorVersion = (fileInfo.dwFileVersionMS >> 16) & 0xFFFF;
    return majorVersion >= MinCDacRuntimeMajorVersion;
}

/**********************************************************************\
 * Loads the given DAC/cDAC module and creates an IXCLRDataProcess from it.
 * Returns nullptr on failure.
\**********************************************************************/
IXCLRDataProcess* Runtime::CreateClrDataProcessInstance(LPCSTR dacFilePath, ULONG64 contractDescriptorAddress)
{
    HMODULE hdac = LoadLibraryA(dacFilePath);
    if (hdac == NULL)
    {
        ExtDbgOut("LoadLibraryA(%s) FAILED %08x\n", dacFilePath, HRESULT_FROM_WIN32(GetLastError()));
        return nullptr;
    }
    PFN_CLRDataCreateInstance pfnCLRDataCreateInstance = (PFN_CLRDataCreateInstance)GetProcAddress(hdac, "CLRDataCreateInstance");
    if (pfnCLRDataCreateInstance == nullptr)
    {
        FreeLibrary(hdac);
        return nullptr;
    }
    ICLRDataTarget *target = new DataTarget(GetModuleAddress(), contractDescriptorAddress);
    IXCLRDataProcess* clrDataProcess = nullptr;
    HRESULT hr = pfnCLRDataCreateInstance(__uuidof(IXCLRDataProcess), target, (void**)&clrDataProcess);
    if (FAILED(hr))
    {
        // CLRDataCreateInstance only AddRefs the data target on success; release our reference
        // (created at ref count 0) to delete it, and unload the module.
        target->AddRef();
        target->Release();
        FreeLibrary(hdac);
        return nullptr;
    }
    // Best-effort: enable module load/unload and exception notifications so SOS flushes its caches
    // across stop states when the cDAC/DAC is used against a live target. Ignore failures (the
    // cDAC may not implement these yet).
    ULONG32 notificationFlags = 0;
    if (SUCCEEDED(clrDataProcess->GetOtherNotificationFlags(&notificationFlags)))
    {
        notificationFlags |= (CLRDATA_NOTIFY_ON_MODULE_LOAD | CLRDATA_NOTIFY_ON_MODULE_UNLOAD | CLRDATA_NOTIFY_ON_EXCEPTION);
        clrDataProcess->SetOtherNotificationFlags(notificationFlags);
    }
    return clrDataProcess;
}

/**********************************************************************\
 * Resolves the address of the cDAC contract descriptor export
 * (DotNetRuntimeContractDescriptor) in the runtime module, or 0 if it
 * can't be located. Mirrors the export lookup in GetSingleFileInfo: the
 * cross-platform reader-based lookup for ELF/Mach-O targets and the
 * debugger's symbol resolution for Windows (PE) targets.
\**********************************************************************/
ULONG64 Runtime::GetContractDescriptorAddress()
{
    const char* symbolName = "DotNetRuntimeContractDescriptor";
    ULONG64 symbolAddress = 0;
    if (m_target->GetOperatingSystem() == ITarget::OperatingSystem::Linux ||
        m_target->GetOperatingSystem() == ITarget::OperatingSystem::OSX)
    {
        if (!::TryGetSymbolWithCallback(ReaderReadMemory, m_address, symbolName, &symbolAddress))
        {
            return 0;
        }
    }
    else
    {
        IDebuggerServices* debuggerServices = GetDebuggerServices();
        if (debuggerServices == nullptr ||
            FAILED(debuggerServices->GetOffsetBySymbol(m_index, symbolName, &symbolAddress)))
        {
            return 0;
        }
    }
    return symbolAddress;
}

/**********************************************************************\
 * Loads and initializes the public ICorDebug interfaces. This should be
 * called at least once per debugger stop state to ensure that the
 * interface is available and that it doesn't hold stale data. Calling
 * it more than once isn't an error, but does have perf overhead from
 * needlessly flushing memory caches.
\**********************************************************************/
HRESULT Runtime::GetCorDebugInterface(ICorDebugProcess** ppCorDebugProcess)
{
    HRESULT hr;

    // We may already have an ICorDebug instance we can use
    if (m_pCorDebugProcess != nullptr)
    {
        // ICorDebugProcess4 is currently considered a private experimental interface on ICorDebug, it might go away so
        // we need to be sure to handle its absence gracefully
        ToRelease<ICorDebugProcess4> pProcess4 = NULL;
        if (SUCCEEDED(m_pCorDebugProcess->QueryInterface(__uuidof(ICorDebugProcess4), (void**)&pProcess4)))
        {
            // FLUSH_ALL is more expensive than PROCESS_RUNNING, but this allows us to be safe even if things
            // like IDNA are in use where we might be looking at non-sequential snapshots of process state
            if (SUCCEEDED(pProcess4->ProcessStateChanged(FLUSH_ALL)))
            {
                // We already have an ICorDebug instance loaded and flushed, nothing more to do
                *ppCorDebugProcess = m_pCorDebugProcess;
                return S_OK;
            }
        }

        // This is a very heavy handed way of reseting
        m_pCorDebugProcess->Detach();
        m_pCorDebugProcess->Release();
        m_pCorDebugProcess = nullptr;
    }
    GUID skuId = CLR_ID_ONECORE_CLR;
#ifndef FEATURE_PAL
    if (GetRuntimeConfiguration() == IRuntime::WindowsDesktop)
    {
        skuId = CLR_ID_V4_DESKTOP;
    }
#endif
    const char* dacFilePath = GetDacFilePath();
    if (dacFilePath == nullptr)
    {
        ExtErr("Could not find matching DAC\n");
        return CORDBG_E_NO_IMAGE_AVAILABLE;
    }
    ArrayHolder<WCHAR> pDacModulePath = new WCHAR[MAX_LONGPATH + 1];
    int length = MultiByteToWideChar(CP_ACP, 0, dacFilePath, -1, pDacModulePath, MAX_LONGPATH);
    if (0 >= length)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        ExtErr("MultiByteToWideChar() DAC FAILED %08x\n", hr);
        return hr;
    }
    const char* dbiFilePath = GetDbiFilePath();
    if (dbiFilePath == nullptr)
    {
        ExtErr("Could not find matching DBI\n");
        return CORDBG_E_NO_IMAGE_AVAILABLE;
    }
    HMODULE hDbi = LoadLibraryA(dbiFilePath);
    if (hDbi == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        ExtErr("LoadLibraryA(%s) FAILED %08x\n", dbiFilePath, hr);
        return hr;
    }
    CLR_DEBUGGING_VERSION clrDebuggingVersionRequested = {0, 4, 0, 0, 0};
    CLR_DEBUGGING_PROCESS_FLAGS clrDebuggingFlags = (CLR_DEBUGGING_PROCESS_FLAGS)0;
    ToRelease<ICorDebugMutableDataTarget> pDataTarget = new CorDebugDataTarget;
    ToRelease<IUnknown> pUnkProcess = nullptr;

    // Get access to the latest OVP implementation and call it
    OpenVirtualProcessImpl2FnPtr ovpFn = (OpenVirtualProcessImpl2FnPtr)GetProcAddress(hDbi, "OpenVirtualProcessImpl2");
    if (ovpFn != nullptr)
    {
        hr = ovpFn(GetModuleAddress(), pDataTarget, pDacModulePath, &clrDebuggingVersionRequested, IID_ICorDebugProcess, &pUnkProcess, &clrDebuggingFlags);
        if (FAILED(hr)) {
	        ExtErr("DBI OpenVirtualProcessImpl2 FAILED %08x\n", hr);
            return hr;
        }
    }
    else
    {
        HMODULE hDac = LoadLibraryA(dacFilePath);
        if (hDac == NULL)
        {
            ExtErr("LoadLibraryA(%s) FAILED %08x\n", dacFilePath, HRESULT_FROM_WIN32(GetLastError()));
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
        }
#ifdef FEATURE_PAL
        // On Linux/MacOS the DAC module handle needs to be re-created using the DAC PAL instance
        // before being passed to DBI's OpenVirtualProcess* implementation. The DBI and DAC share
        // the same PAL where dbgshim has it's own.
        LoadLibraryWFnPtr loadLibraryWFn = (LoadLibraryWFnPtr)GetProcAddress(hDac, "LoadLibraryW");
        if (loadLibraryWFn != nullptr)
        {
            hDac = loadLibraryWFn(pDacModulePath);
            if (hDac == NULL)
            {
		        ExtErr("DBI LoadLibraryW(%S) FAILED\n", pDacModulePath.GetPtr());
	            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
            }
        }
        else
        {
	        ExtErr("DBI GetProcAddress(LoadLibraryW) FAILED\n");
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
	    }
#endif // FEATURE_PAL

        // Get access to OVP and call it
        OpenVirtualProcessImplFnPtr ovpFn = (OpenVirtualProcessImplFnPtr)GetProcAddress(hDbi, "OpenVirtualProcessImpl");
        if (ovpFn != nullptr)
        {
            // Have a CLR v4 Beta2+ DBI, call it and let it do the version check
            hr = ovpFn(GetModuleAddress(), pDataTarget, hDac, &clrDebuggingVersionRequested, IID_ICorDebugProcess, &pUnkProcess, &clrDebuggingFlags);
            if (FAILED(hr)) {
		        ExtErr("DBI OpenVirtualProcessImpl FAILED %08x\n", hr);
                return hr;
            }
        }
        else
        {
            // Fallback to CLR v4 Beta1 path, but skip some of the checking we'd normally do (maxSupportedVersion, etc.)
            OpenVirtualProcess2FnPtr ovp2Fn = (OpenVirtualProcess2FnPtr)GetProcAddress(hDbi, "OpenVirtualProcess2");
            if (ovp2Fn != nullptr)
            {
	            hr = ovp2Fn(GetModuleAddress(), pDataTarget, hDac, IID_ICorDebugProcess, &pUnkProcess, &clrDebuggingFlags);
            }
            else
            {
                hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
            }
		    if (FAILED(hr)) {
		        ExtErr("DBI OpenVirtualProcess2 FAILED %08x\n", hr);
		        return hr;
		    }
        }
    }
    _ASSERTE(pUnkProcess != nullptr);
    hr = pUnkProcess->QueryInterface(IID_ICorDebugProcess, (PVOID*)&m_pCorDebugProcess);
    if (FAILED(hr)) {
        return hr;
    }
    *ppCorDebugProcess = m_pCorDebugProcess;
    return hr;
}

/**********************************************************************\
 * Gets the runtime version
\**********************************************************************/
HRESULT Runtime::GetEEVersion(VS_FIXEDFILEINFO* pFileInfo, char* fileVersionBuffer, int fileVersionBufferSizeInBytes)
{
    _ASSERTE(pFileInfo);
    _ASSERTE(g_ExtSymbols2 != nullptr);

    HRESULT hr = g_ExtSymbols2->GetModuleVersionInformation(
        m_index, 0, "\\", pFileInfo, sizeof(VS_FIXEDFILEINFO), NULL);

    // 0.0.0.0 is not a valid version. This is sometime returned by windbg for Linux core dumps
    if (SUCCEEDED(hr) && (pFileInfo->dwFileVersionMS == (DWORD)-1 || (pFileInfo->dwFileVersionLS == 0 && pFileInfo->dwFileVersionMS == 0))) {
        return E_FAIL;
    }

    // Attempt to get the FileVersion string that contains version and the "built by" and commit id info
    if (fileVersionBuffer != nullptr)
    {
        if (fileVersionBufferSizeInBytes > 0) {
            fileVersionBuffer[0] = '\0';
        }
        // We can assume the English/CP_UNICODE lang/code page for the runtime modules
        g_ExtSymbols2->GetModuleVersionInformation(
            m_index, 0, "\\StringFileInfo\\040904B0\\FileVersion", fileVersionBuffer, fileVersionBufferSizeInBytes, NULL);
    }

    return hr;
}

/**********************************************************************\
 * Displays the runtime internal status
\**********************************************************************/
void Runtime::DisplayStatus()
{
    char current = g_pRuntime == this ? '*' : ' ';
    ExtOut("%c%s runtime at %08llx size %08llx\n", current, GetRuntimeConfigurationName(GetRuntimeConfiguration()), m_address, m_size);
    if (m_runtimeInfo != nullptr) {
        ExtOut("    Single-file module path: %s\n", m_name);
    }
    else {
        ExtOut("    Runtime module path: %s\n", m_name);
    }
    if (m_runtimeDirectory != nullptr) {
        ExtOut("    Runtime module directory: %s\n", m_runtimeDirectory);
    }
    if (m_dacFilePath != nullptr) {
        ExtOut("    DAC file path: %s\n", m_dacFilePath);
    }
    if (m_dbiFilePath != nullptr) {
        ExtOut("    DBI file path: %s\n", m_dbiFilePath);
    }
}
