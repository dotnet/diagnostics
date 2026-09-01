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
#include <vector>
#include "runtimeimpl.h"
#include "datatarget.h"
#include "cordebugdatatarget.h"
#include "runtimeinfo.h"

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dlfcn.h>
#include <unistd.h>
#else
#include <softpub.h>
#include <wintrust.h>
#endif // !FEATURE_PAL

#define CORDBG_E_NO_IMAGE_AVAILABLE EMAKEHR(0x1c64)

typedef HRESULT (STDAPICALLTYPE *CLRCreateInstanceFnPtr)(REFCLSID clsid, REFIID riid, LPVOID *ppInterface);

enum class DbgShimCDacLoadPolicy : DWORD
{
    PreferCDac = 0,
    CDacOnly = 1,
    LegacyDacOnly = 2
};

MIDL_INTERFACE("2D3B4F6A-1C7E-4B2A-9E5D-7F1A6C0B8D34")
ICLRDebuggingPolicy : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE SetCDacLoadPolicy(DbgShimCDacLoadPolicy policy) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCDacLoadPolicy(DbgShimCDacLoadPolicy* policy) = 0;
};

// Current runtime instance
IRuntime* g_pRuntime = nullptr;

static CDacLoadPolicy s_cdacLoadPolicy = CDacLoadPolicy::PreferCDac;

extern "C" bool TryGetSymbolWithCallback(
    bool (*readMemory)(void* address, void* buffer, size_t size),
    ULONG64 baseAddress,
    const char* symbolName,
    ULONG64* symbolAddress);

bool ReaderReadMemory(void* address, void* buffer, size_t size)
{
    IDebuggerServices* debuggerServices = GetDebuggerServices();
    if (debuggerServices == nullptr)
    {
        return false;
    }
    ULONG read = 0;
    return SUCCEEDED(debuggerServices->ReadVirtual((ULONG64)address, buffer, (ULONG)size, &read));
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
    HRESULT hr = debuggerServices->GetNumberModules(&loaded, &unloaded);
    if (FAILED(hr)) {
        return hr;
    }

    const char* symbolName = "DotNetRuntimeInfo";
    for (ULONG index = 0; index < loaded; index++)
    {
        ULONG64 baseAddress;
        hr = debuggerServices->GetModuleByIndex(index, &baseAddress);
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
        hr = debuggerServices->ReadVirtual(symbolAddress, buffer, sizeof(RuntimeInfo), &read);
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
        IDebuggerServices* debuggerServices = GetDebuggerServices();
        if (debuggerServices == nullptr)
        {
            return E_NOINTERFACE;
        }
        // Check if the normal runtime module (coreclr.dll, libcoreclr.so, etc.) is loaded
        hr = debuggerServices->GetModuleByModuleName(runtimeModuleName, 0, &moduleIndex, &moduleAddress);
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
            hr = debuggerServices->GetModuleInfo(moduleIndex, nullptr, &moduleSize, nullptr, nullptr);
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
    m_dbgShimFilePath(nullptr),
    m_dbiFilePath(nullptr),
    m_dbgShimHandle(nullptr),
    m_clrDataProcess(nullptr),
    m_cdacDataProcess(nullptr),
    m_hasCDacActivationResult(false),
    m_cdacActivationResult(E_UNEXPECTED),
    m_contractDescriptorAddressResolved(false),
    m_contractDescriptorAddress(0),
    m_pCorDebugProcess(nullptr)
{
    _ASSERTE(index != -1);
    _ASSERTE(address != 0);
    _ASSERTE(size != 0);

    ArrayHolder<char> szModuleName = new char[MAX_LONGPATH + 1];
    IDebuggerServices* debuggerServices = GetDebuggerServices();
    HRESULT hr = debuggerServices != nullptr
        ? debuggerServices->GetModuleNames(index, 0, szModuleName, MAX_LONGPATH, NULL, NULL, 0, NULL, NULL, 0, NULL)
        : E_NOINTERFACE;
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
    if (m_dbgShimFilePath != nullptr)
    {
        free((void*)m_dbgShimFilePath);
        m_dbgShimFilePath = nullptr;
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
    if (m_dbgShimHandle != nullptr)
    {
        FreeLibrary(m_dbgShimHandle);
        m_dbgShimHandle = nullptr;
    }
}

/**********************************************************************\
 * Returns the DAC module path to the rest of SOS.
\**********************************************************************/
LPCSTR Runtime::GetDacFilePath()
{
    if (m_dacFilePath == nullptr)
    {
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
static void DbgShimModuleAnchor() {}
#endif

/**********************************************************************\
 * Returns the dbgshim module path next to sos.
\**********************************************************************/
LPCSTR Runtime::GetDbgShimFilePath()
{
    if (m_dbgShimFilePath == nullptr)
    {
        ArrayHolder<char> szSOSModulePath = new char[MAX_LONGPATH + 1];
#ifdef FEATURE_PAL
        Dl_info info;
        if (dladdr((void*)&DbgShimModuleAnchor, &info) == 0 || info.dli_fname == nullptr)
        {
            ExtDbgOut("GetDbgShimFilePath: dladdr failed to locate the sos module\n");
            return nullptr;
        }
        strcpy_s(szSOSModulePath.GetPtr(), MAX_LONGPATH, info.dli_fname);
#else
        if (GetModuleFileNameA(g_hInstance, szSOSModulePath, MAX_LONGPATH) == 0)
        {
            ExtDbgOut("GetDbgShimFilePath: GetModuleFileNameA failed %08x\n", HRESULT_FROM_WIN32(GetLastError()));
            return nullptr;
        }
#endif
        std::string dbgShimModulePath(szSOSModulePath.GetPtr());
        size_t lastSlash = dbgShimModulePath.rfind(DIRECTORY_SEPARATOR_CHAR_A);
        if (lastSlash == std::string::npos)
        {
            ExtDbgOut("GetDbgShimFilePath: failed to parse sos module directory from %s\n", dbgShimModulePath.c_str());
            return nullptr;
        }
        dbgShimModulePath.erase(lastSlash + 1);
#ifdef FEATURE_PAL
#ifdef __APPLE__
        dbgShimModulePath.append("libdbgshim.dylib");
#else
        dbgShimModulePath.append("libdbgshim.so");
#endif
#else
        dbgShimModulePath.append("dbgshim.dll");
#endif
#ifdef FEATURE_PAL
        bool exists = access(dbgShimModulePath.c_str(), F_OK) == 0;
#else
        bool exists = GetFileAttributesA(dbgShimModulePath.c_str()) != INVALID_FILE_ATTRIBUTES;
#endif
        if (exists)
        {
            m_dbgShimFilePath = _strdup(dbgShimModulePath.c_str());
        }
    }
    return m_dbgShimFilePath;
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
    else
    {
        m_hasCDacActivationResult = false;
        m_cdacActivationResult = E_UNEXPECTED;
        m_contractDescriptorAddressResolved = false;
        m_contractDescriptorAddress = 0;
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
HRESULT Runtime::GetClrDataProcess(CDacLoadPolicy policy, IXCLRDataProcess** ppClrDataProcess)
{
    policy = GetEffectiveCDacLoadPolicy(policy);
    bool cdacOnly = policy == CDacLoadPolicy::OnlyUseCDac;

    if (policy != CDacLoadPolicy::UseLegacyDac)
    {
        if (m_cdacDataProcess == nullptr && !m_hasCDacActivationResult)
        {
            m_cdacActivationResult = CreateClrDataProcessViaDbgShim(&m_cdacDataProcess);
            m_hasCDacActivationResult = true;
            if (FAILED(m_cdacActivationResult) && cdacOnly)
            {
                *ppClrDataProcess = nullptr;
                return m_cdacActivationResult;
            }
        }
        if (m_cdacDataProcess != nullptr)
        {
            *ppClrDataProcess = m_cdacDataProcess;
            return S_OK;
        }
        if (cdacOnly)
        {
            *ppClrDataProcess = nullptr;
            return m_cdacActivationResult;
        }
        // Fall through to the DAC.
    }

    if (m_clrDataProcess == nullptr)
    {
        *ppClrDataProcess = nullptr;

        IDebuggerServices* debuggerServices = GetDebuggerServices();
        BOOL signatureVerificationEnabled = FALSE;
        HRESULT signatureResult = debuggerServices != nullptr
            ? debuggerServices->GetDacSignatureVerificationSettings(&signatureVerificationEnabled)
            : E_NOINTERFACE;
        if (FAILED(signatureResult) || signatureVerificationEnabled)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }

        LPCSTR dacFilePath = GetDacFilePath();
        if (dacFilePath == nullptr)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }
        m_clrDataProcess = CreateClrDataProcessDirect(dacFilePath);
        if (m_clrDataProcess == nullptr)
        {
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
        }
    }
    *ppClrDataProcess = m_clrDataProcess;
    return S_OK;
}

// Returns true if the named environment variable is set to "1".
static bool IsEnvironmentVariableSetToOne(const char* name)
{
    char buffer[16];
    DWORD length = GetEnvironmentVariableA(name, buffer, ARRAY_SIZE(buffer));
    return length > 0 && length < ARRAY_SIZE(buffer) && strcmp(buffer, "1") == 0;
}

/**********************************************************************\
 * Returns the effective cDAC loading policy for this runtime.
\**********************************************************************/
CDacLoadPolicy Runtime::GetEffectiveCDacLoadPolicy(CDacLoadPolicy policy)
{
    if (policy == CDacLoadPolicy::PreferCDac &&
        (IsEnvironmentVariableSetToOne("DOTNET_ENABLE_CDAC") ||
         IsEnvironmentVariableSetToOne("COMPlus_ENABLE_CDAC")))
    {
        // Let the legacy DAC host cDAC instead of loading the standalone cDAC.
        return CDacLoadPolicy::UseLegacyDac;
    }
    return policy;
}

CDacLoadPolicy Runtime::GetConfiguredCDacLoadPolicy()
{
    return s_cdacLoadPolicy;
}

CDacLoadPolicy Runtime::GetCDacLoadPolicy() const
{
    return GetConfiguredCDacLoadPolicy();
}

void Runtime::SetCDacLoadPolicy(CDacLoadPolicy policy)
{
    s_cdacLoadPolicy = policy;
}

/**********************************************************************\
 * Loads the given DAC module and creates an IXCLRDataProcess from it.
 * Returns nullptr on failure.
\**********************************************************************/
IXCLRDataProcess* Runtime::CreateClrDataProcessDirect(LPCSTR dacFilePath)
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
    ICLRDataTarget *target = new DataTarget(GetModuleAddress(), 0);
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
 * Creates an IXCLRDataProcess through dbgshim.
\**********************************************************************/
HRESULT Runtime::CreateClrDataProcessViaDbgShim(IXCLRDataProcess** ppClrDataProcess)
{
    if (ppClrDataProcess == nullptr)
    {
        return E_INVALIDARG;
    }
    *ppClrDataProcess = nullptr;

    if (m_dbgShimHandle == nullptr)
    {
        LPCSTR dbgShimFilePath = GetDbgShimFilePath();
        if (dbgShimFilePath == nullptr)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }
        m_dbgShimHandle = LoadLibraryA(dbgShimFilePath);
        if (m_dbgShimHandle == nullptr)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
    }

    CLRCreateInstanceFnPtr createInstance =
        (CLRCreateInstanceFnPtr)GetProcAddress(m_dbgShimHandle, "CLRCreateInstance");
    if (createInstance == nullptr)
    {
        return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
    }

    ToRelease<ICLRDebugging> debugging;
    HRESULT hr = createInstance(CLSID_CLRDebugging, IID_ICLRDebugging, (void**)&debugging);
    if (FAILED(hr))
    {
        return hr;
    }

    ToRelease<ICLRDebuggingPolicy> policy;
    hr = debugging->QueryInterface(__uuidof(ICLRDebuggingPolicy), (void**)&policy);
    if (FAILED(hr))
    {
        return hr;
    }
    hr = policy->SetCDacLoadPolicy(DbgShimCDacLoadPolicy::CDacOnly);
    if (FAILED(hr))
    {
        return hr;
    }

    ICLRDataTarget* target = new DataTarget(GetModuleAddress(), GetContractDescriptorAddress());
    target->AddRef();

    CLR_DEBUGGING_VERSION maxVersion = {};
    maxVersion.wStructVersion = 0;
    maxVersion.wMajor = 4;
    CLR_DEBUGGING_VERSION version = {};
    CLR_DEBUGGING_PROCESS_FLAGS processFlags = (CLR_DEBUGGING_PROCESS_FLAGS)0;
    IUnknown* process = nullptr;
    hr = debugging->OpenVirtualProcess(
        GetModuleAddress(),
        target,
        nullptr,
        &maxVersion,
        __uuidof(IXCLRDataProcess),
        &process,
        &version,
        &processFlags);
    target->Release();
    if (FAILED(hr))
    {
        if (process != nullptr)
        {
            process->Release();
        }
        return hr;
    }
    if (process == nullptr)
    {
        return E_NOINTERFACE;
    }

    *ppClrDataProcess = (IXCLRDataProcess*)process;
    ULONG32 notificationFlags = 0;
    if (SUCCEEDED((*ppClrDataProcess)->GetOtherNotificationFlags(&notificationFlags)))
    {
        notificationFlags |=
            CLRDATA_NOTIFY_ON_MODULE_LOAD |
            CLRDATA_NOTIFY_ON_MODULE_UNLOAD |
            CLRDATA_NOTIFY_ON_EXCEPTION;
        (*ppClrDataProcess)->SetOtherNotificationFlags(notificationFlags);
    }
    return S_OK;
}

ULONG64 Runtime::GetContractDescriptorAddress()
{
    if (!m_contractDescriptorAddressResolved)
    {
        m_contractDescriptorAddressResolved = true;
        const char* symbolName = CONTRACT_DESCRIPTOR_SYMBOL;
        if (m_target->GetOperatingSystem() == ITarget::OperatingSystem::Linux ||
            m_target->GetOperatingSystem() == ITarget::OperatingSystem::OSX)
        {
            ::TryGetSymbolWithCallback(
                ReaderReadMemory,
                m_address,
                symbolName,
                &m_contractDescriptorAddress);
        }
        else
        {
            IDebuggerServices* debuggerServices = GetDebuggerServices();
            if (debuggerServices != nullptr)
            {
                debuggerServices->GetOffsetBySymbol(
                    m_index,
                    symbolName,
                    &m_contractDescriptorAddress);
            }
        }
    }
    return m_contractDescriptorAddress;
}

class RuntimeLibraryProvider final :
    public ICLRDebuggingLibraryProvider,
    public ICLRDebuggingLibraryProvider2
{
private:
    LONG m_ref;
    class Runtime* m_runtime;
#ifndef FEATURE_PAL
    bool m_verifySignature;
    std::vector<HANDLE> m_verifiedFiles;
#endif

public:
    RuntimeLibraryProvider(class Runtime* runtime) :
        m_ref(1),
        m_runtime(runtime)
#ifndef FEATURE_PAL
        , m_verifySignature(true)
#endif
    {
#ifndef FEATURE_PAL
        IDebuggerServices* debuggerServices = GetDebuggerServices();
        BOOL enabled = TRUE;
        if (debuggerServices != nullptr &&
            SUCCEEDED(debuggerServices->GetDacSignatureVerificationSettings(&enabled)))
        {
            m_verifySignature = enabled != FALSE;
        }
#endif
    }

    ~RuntimeLibraryProvider()
    {
#ifndef FEATURE_PAL
        for (HANDLE file : m_verifiedFiles)
        {
            CloseHandle(file);
        }
#endif
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** ppvObject) override
    {
        if (ppvObject == nullptr)
        {
            return E_INVALIDARG;
        }
        *ppvObject = nullptr;

        if (iid == IID_IUnknown || iid == IID_ICLRDebuggingLibraryProvider)
        {
            *ppvObject = static_cast<ICLRDebuggingLibraryProvider*>(this);
            AddRef();
            return S_OK;
        }
        if (iid == IID_ICLRDebuggingLibraryProvider2)
        {
            *ppvObject = static_cast<ICLRDebuggingLibraryProvider2*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return InterlockedIncrement(&m_ref);
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG ref = InterlockedDecrement(&m_ref);
        if (ref == 0)
        {
            delete this;
        }
        return ref;
    }

    HRESULT STDMETHODCALLTYPE ProvideLibrary(
        const WCHAR* fileName,
        DWORD timestamp,
        DWORD sizeOfImage,
        HMODULE* moduleHandle) override
    {
        if (fileName == nullptr || moduleHandle == nullptr)
        {
            return E_INVALIDARG;
        }
        *moduleHandle = nullptr;

        LPCSTR path = _wcsstr(fileName, W("mscordbi")) != nullptr
            ? m_runtime->GetDbiFilePath()
            : m_runtime->GetDacFilePath();
        if (path == nullptr)
        {
            return CORDBG_E_LIBRARY_PROVIDER_ERROR;
        }
        if (!VerifyLibrary(path))
        {
            return CORDBG_E_LIBRARY_PROVIDER_ERROR;
        }

        *moduleHandle = LoadLibraryA(path);
        return *moduleHandle != nullptr
            ? S_OK
            : HRESULT_FROM_WIN32(GetLastError());
    }

    HRESULT STDMETHODCALLTYPE ProvideLibrary2(
        const WCHAR* fileName,
        DWORD timestamp,
        DWORD sizeOfImage,
        LPWSTR* resolvedModulePath) override
    {
        if (fileName == nullptr || resolvedModulePath == nullptr)
        {
            return E_INVALIDARG;
        }
        *resolvedModulePath = nullptr;

        LPCSTR path = _wcsstr(fileName, W("mscordbi")) != nullptr
            ? m_runtime->GetDbiFilePath()
            : m_runtime->GetDacFilePath();
        if (path == nullptr)
        {
            return CORDBG_E_LIBRARY_PROVIDER_ERROR;
        }
        if (!VerifyLibrary(path))
        {
            return CORDBG_E_LIBRARY_PROVIDER_ERROR;
        }

        int length = MultiByteToWideChar(CP_ACP, 0, path, -1, nullptr, 0);
        if (length <= 0)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        LPWSTR result = (LPWSTR)CoTaskMemAlloc(length * sizeof(WCHAR));
        if (result == nullptr)
        {
            return E_OUTOFMEMORY;
        }
        if (MultiByteToWideChar(CP_ACP, 0, path, -1, result, length) <= 0)
        {
            HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
            CoTaskMemFree(result);
            return hr;
        }

        *resolvedModulePath = result;
        return S_OK;
    }

private:
    bool VerifyLibrary(LPCSTR path)
    {
#ifndef FEATURE_PAL
        if (m_verifySignature)
        {
            HANDLE file = CreateFileA(
                path,
                GENERIC_READ,
                FILE_SHARE_READ,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (file == INVALID_HANDLE_VALUE)
            {
                ExtErr("RuntimeLibraryProvider: CreateFile(%s) FAILED %08x\n",
                    path, HRESULT_FROM_WIN32(GetLastError()));
                return false;
            }

            WINTRUST_FILE_INFO trustInfo = {};
            trustInfo.cbStruct = sizeof(trustInfo);
            trustInfo.hFile = file;

            WINTRUST_DATA trustData = {};
            trustData.cbStruct = sizeof(trustData);
            trustData.dwUIChoice = WTD_UI_NONE;
            trustData.fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN;
            trustData.dwUnionChoice = WTD_CHOICE_FILE;
            trustData.pFile = &trustInfo;
            trustData.dwStateAction = WTD_STATEACTION_VERIFY;
            trustData.dwProvFlags = WTD_REVOCATION_CHECK_CHAIN | WTD_CACHE_ONLY_URL_RETRIEVAL;

            GUID action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            LONG status = WinVerifyTrust(nullptr, &action, &trustData);
            if (status != ERROR_SUCCESS)
            {
                ExtErr("RuntimeLibraryProvider: WinVerifyTrust(%s) FAILED %08x\n", path, status);
                trustData.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(nullptr, &action, &trustData);
                CloseHandle(file);
                return false;
            }

            CRYPT_PROVIDER_DATA* provider = WTHelperProvDataFromStateData(trustData.hWVTStateData);
            CRYPT_PROVIDER_SGNR* signer = provider != nullptr
                ? WTHelperGetProvSignerFromChain(provider, 0, FALSE, 0)
                : nullptr;
            CERT_CHAIN_POLICY_PARA policyParameters = {};
            policyParameters.cbSize = sizeof(policyParameters);
            CERT_CHAIN_POLICY_STATUS policyStatus = {};
            policyStatus.cbSize = sizeof(policyStatus);
            bool valid = signer != nullptr &&
                CertVerifyCertificateChainPolicy(
                    (LPCSTR)CERT_CHAIN_POLICY_MICROSOFT_ROOT,
                    signer->pChainContext,
                    &policyParameters,
                    &policyStatus) &&
                policyStatus.dwError == ERROR_SUCCESS;

            CRYPT_PROVIDER_CERT* leafCertificate = valid
                ? WTHelperGetProvCertFromChain(signer, 0)
                : nullptr;
            valid = leafCertificate != nullptr;
            if (valid)
            {
                PCERT_EXTENSION usageExtension = CertFindExtension(
                    szOID_ENHANCED_KEY_USAGE,
                    leafCertificate->pCert->pCertInfo->cExtension,
                    leafCertificate->pCert->pCertInfo->rgExtension);
                CERT_ENHKEY_USAGE* usages = nullptr;
                DWORD usageSize = 0;
                if (usageExtension == nullptr ||
                    !CryptDecodeObjectEx(
                        X509_ASN_ENCODING,
                        X509_ENHANCED_KEY_USAGE,
                        usageExtension->Value.pbData,
                        usageExtension->Value.cbData,
                        CRYPT_DECODE_ALLOC_FLAG,
                        nullptr,
                        &usages,
                        &usageSize))
                {
                    valid = false;
                }
                else
                {
                    valid = false;
                    for (DWORD i = 0; i < usages->cUsageIdentifier; i++)
                    {
                        bool validDacOid =
                            strcmp(usages->rgpszUsageIdentifier[i], "1.3.6.1.4.1.311.84.4.1") == 0;
                        if (validDacOid)
                        {
                            valid = true;
                            break;
                        }
                    }
                    LocalFree(usages);
                }
            }

            trustData.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(nullptr, &action, &trustData);
            if (!valid)
            {
                ExtErr("RuntimeLibraryProvider: certificate policy validation failed for %s\n", path);
                CloseHandle(file);
                return false;
            }
            m_verifiedFiles.push_back(file);
        }
#endif
        return true;
    }
};

HRESULT Runtime::CreateCorDebugProcessViaDbgShim(ICorDebugProcess** ppCorDebugProcess)
{
    if (ppCorDebugProcess == nullptr)
    {
        return E_INVALIDARG;
    }
    *ppCorDebugProcess = nullptr;

    if (m_dbgShimHandle == nullptr)
    {
        LPCSTR dbgShimFilePath = GetDbgShimFilePath();
        if (dbgShimFilePath == nullptr)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }
        m_dbgShimHandle = LoadLibraryA(dbgShimFilePath);
        if (m_dbgShimHandle == nullptr)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
    }

    CLRCreateInstanceFnPtr createInstance =
        (CLRCreateInstanceFnPtr)GetProcAddress(m_dbgShimHandle, "CLRCreateInstance");
    if (createInstance == nullptr)
    {
        return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
    }

    ToRelease<ICLRDebugging> debugging;
    HRESULT hr = createInstance(CLSID_CLRDebugging, IID_ICLRDebugging, (void**)&debugging);
    if (FAILED(hr))
    {
        return hr;
    }

    ToRelease<ICLRDebuggingPolicy> policy;
    hr = debugging->QueryInterface(__uuidof(ICLRDebuggingPolicy), (void**)&policy);
    if (FAILED(hr))
    {
        return hr;
    }

    CDacLoadPolicy configuredPolicy = GetCDacLoadPolicy();
    DbgShimCDacLoadPolicy loadPolicy = (DbgShimCDacLoadPolicy)GetEffectiveCDacLoadPolicy(configuredPolicy);
    hr = policy->SetCDacLoadPolicy(loadPolicy);
    if (FAILED(hr))
    {
        return hr;
    }

    CLR_DEBUGGING_VERSION clrDebuggingVersionRequested = {0, 4, 0, 0, 0};
    CLR_DEBUGGING_PROCESS_FLAGS clrDebuggingFlags = (CLR_DEBUGGING_PROCESS_FLAGS)0;
    ToRelease<ICorDebugMutableDataTarget> pDataTarget = new CorDebugDataTarget;
    ToRelease<ICLRDebuggingLibraryProvider> libraryProvider =
        static_cast<ICLRDebuggingLibraryProvider*>(new RuntimeLibraryProvider(this));
    ToRelease<IUnknown> pUnkProcess = nullptr;
    CLR_DEBUGGING_VERSION version = {};
    hr = debugging->OpenVirtualProcess(
        GetModuleAddress(),
        pDataTarget,
        libraryProvider,
        &clrDebuggingVersionRequested,
        IID_ICorDebugProcess,
        &pUnkProcess,
        &version,
        &clrDebuggingFlags);
    if (FAILED(hr))
    {
        ExtErr("DbgShim OpenVirtualProcess DBI activation FAILED %08x\n", hr);
        return hr;
    }
    if (pUnkProcess == nullptr)
    {
        return E_NOINTERFACE;
    }

    hr = pUnkProcess->QueryInterface(IID_ICorDebugProcess, (PVOID*)&m_pCorDebugProcess);
    if (FAILED(hr))
    {
        return hr;
    }
    *ppCorDebugProcess = m_pCorDebugProcess;
    return hr;
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
    return CreateCorDebugProcessViaDbgShim(ppCorDebugProcess);
}

/**********************************************************************\
 * Gets the runtime version
\**********************************************************************/
HRESULT Runtime::GetEEVersion(VS_FIXEDFILEINFO* pFileInfo, char* fileVersionBuffer, int fileVersionBufferSizeInBytes)
{
    _ASSERTE(pFileInfo);
    IDebuggerServices* debuggerServices = GetDebuggerServices();
    if (debuggerServices == nullptr)
    {
        return E_NOINTERFACE;
    }

    HRESULT hr = debuggerServices->GetModuleVersionInformation(
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
        debuggerServices->GetModuleVersionInformation(
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
