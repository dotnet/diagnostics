// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
#include "debugshim.h"
#include "runtimeimpl.h"
#include "datatarget.h"
#include "cordebugdatatarget.h"
#include "cordebuglibraryprovider.h"
#include "runtimeinfo.h"

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dlfcn.h>
#include <unistd.h>
#endif // !FEATURE_PAL

// Current runtime instance
IRuntime* g_pRuntime = nullptr;

#if !defined(__APPLE__)

extern bool TryGetSymbol(uint64_t baseAddress, const char* symbolName, uint64_t* symbolAddress);

bool ElfReaderReadMemory(void* address, void* buffer, size_t size)
{
    ULONG read = 0;
    return SUCCEEDED(g_ExtData->ReadVirtual((ULONG64)address, buffer, (ULONG)size, &read));
}

/**********************************************************************\
 * Search all the modules in the process for the single-file host
\**********************************************************************/
static HRESULT GetSingleFileInfo(PULONG pModuleIndex, PULONG64 pModuleAddress, RuntimeInfo** ppRuntimeInfo)
{
    _ASSERTE(pModuleIndex != nullptr);
    _ASSERTE(pModuleAddress != nullptr);

    ULONG loaded, unloaded;
    HRESULT hr = g_ExtSymbols->GetNumberModules(&loaded, &unloaded);
    if (FAILED(hr)) {
        return hr;
    }

    for (ULONG index = 0; index < loaded; index++)
    {
        ULONG64 baseAddress;
        hr = g_ExtSymbols->GetModuleByIndex(index, &baseAddress);
        if (FAILED(hr)) {
            return hr;
        }
        ULONG64 symbolAddress;
        if (TryGetSymbol(baseAddress, "DotNetRuntimeInfo", &symbolAddress))
        {
            ULONG read = 0;
            ArrayHolder<BYTE> buffer = new BYTE[sizeof(RuntimeInfo)];
            hr = g_ExtData->ReadVirtual(symbolAddress, buffer, sizeof(RuntimeInfo), &read);
            if (FAILED(hr)) {
                return hr;
            }
            if (strcmp(((RuntimeInfo*)buffer.GetPtr())->Signature, "DotNetRuntimeInfo") != 0) {
                break;
            }
            *pModuleIndex = index;
            *pModuleAddress = baseAddress;
            *ppRuntimeInfo = (RuntimeInfo*)buffer.Detach();
            return S_OK;
        }
    }

    return E_FAIL;
}

#endif // !defined(__APPLE__)

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
#if !defined(__APPLE__)
        if (FAILED(hr))
        {
            // If the standard runtime module isn't loaded, try looking for a single-file program
            if (configuration == IRuntime::UnixCore)
            {
                hr = GetSingleFileInfo(&moduleIndex, &moduleAddress, &runtimeInfo);
            }
        }
#endif // !defined(__APPLE__)

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
    m_dbiFilePath(nullptr),
    m_clrDataProcess(nullptr),
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
}

/**********************************************************************\
 * Returns the DAC module path to the rest of SOS.
\**********************************************************************/
LPCSTR Runtime::GetDacFilePath()
{
    // If the DAC path hasn't been set by the symbol download support, use the one in the runtime directory.
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
#if defined(__linux__)
                // We are creating a symlink to the DAC in a temp directory
                // where libcoreclrtraceptprovider.so doesn't exist so it 
                // doesn't get loaded by the DAC causing a LTTng-UST exception.
                //
                // Issue #https://github.com/dotnet/coreclr/issues/20205
                LPCSTR tmpPath = m_target->GetTempDirectory();
                if (tmpPath != nullptr) 
                {
                    std::string dacSymLink(tmpPath);
                    dacSymLink.append(NETCORE_DAC_DLL_NAME_A);

                    // Check if the DAC file already exists in the temp directory because
                    // of a "loadsymbols" command which downloads everything.
                    if (access(dacSymLink.c_str(), F_OK) == 0)
                    {
                        dacModulePath.assign(dacSymLink);
                    }
                    else
                    {
                        int error = symlink(dacModulePath.c_str(), dacSymLink.c_str());
                        if (error == 0)
                        {
                            dacModulePath.assign(dacSymLink);
                        }
                        else
                        {
                            ExtErr("symlink(%s, %s) FAILED %s\n", dacModulePath.c_str(), dacSymLink.c_str(), strerror(errno));
                        }
                    }
                }
#endif
                m_dacFilePath = _strdup(dacModulePath.c_str());
            }
        }

        if (m_dacFilePath == nullptr)
        {
            // Attempt to only load the DAC/DBI modules
            LoadRuntimeModules();
        }
    }
    return m_dacFilePath;
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

        if (m_dbiFilePath == nullptr)
        {
            // Attempt to only load the DAC/DBI modules
            LoadRuntimeModules();
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
 * Returns the runtime directory of the target
\**********************************************************************/
LPCSTR Runtime::GetRuntimeDirectory()
{
    if (m_runtimeDirectory == nullptr)
    {
        LPCSTR runtimeDirectory = m_target->GetRuntimeDirectory();
        if (runtimeDirectory != nullptr)
        {
            m_runtimeDirectory = _strdup(runtimeDirectory);
        }
        else 
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
    }
    return m_runtimeDirectory;
}

/**********************************************************************\
 * Creates an instance of the DAC clr data process
\**********************************************************************/
HRESULT Runtime::GetClrDataProcess(IXCLRDataProcess** ppClrDataProcess)
{
    if (m_clrDataProcess == nullptr)
    {
        *ppClrDataProcess = nullptr;

        LPCSTR dacFilePath = GetDacFilePath();
        if (dacFilePath == nullptr)
        {
            return CORDBG_E_NO_IMAGE_AVAILABLE;
        }
        HMODULE hdac = LoadLibraryA(dacFilePath);
        if (hdac == NULL)
        {
            ExtDbgOut("LoadLibrary(%s) FAILED %08x\n", dacFilePath, HRESULT_FROM_WIN32(GetLastError()));
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
        }
        PFN_CLRDataCreateInstance pfnCLRDataCreateInstance = (PFN_CLRDataCreateInstance)GetProcAddress(hdac, "CLRDataCreateInstance");
        if (pfnCLRDataCreateInstance == nullptr)
        {
            FreeLibrary(hdac);
            return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
        }
        ICLRDataTarget *target = new DataTarget(GetModuleAddress());
        HRESULT hr = pfnCLRDataCreateInstance(__uuidof(IXCLRDataProcess), target, (void**)&m_clrDataProcess);
        if (FAILED(hr))
        {
            m_clrDataProcess = nullptr;
            return hr;
        }
        ULONG32 flags = 0;
        m_clrDataProcess->GetOtherNotificationFlags(&flags);
        flags |= (CLRDATA_NOTIFY_ON_MODULE_LOAD | CLRDATA_NOTIFY_ON_MODULE_UNLOAD | CLRDATA_NOTIFY_ON_EXCEPTION);
        m_clrDataProcess->SetOtherNotificationFlags(flags);
    }
    *ppClrDataProcess = m_clrDataProcess;
    return S_OK;
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
    HMODULE hModule = NULL;
    HRESULT hr;
    ToRelease<ICLRDebugging> pClrDebugging;

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

    // SOS now has a statically linked version of the loader code that is normally found in mscoree/mscoreei.dll
    // Its not much code and takes a big step towards 0 install dependencies
    // Need to pick the appropriate SKU of CLR to detect
#if defined(FEATURE_CORESYSTEM)
    GUID skuId = CLR_ID_ONECORE_CLR;
#else
    GUID skuId = CLR_ID_CORECLR;
#endif
#ifndef FEATURE_PAL
    if (GetRuntimeConfiguration() == IRuntime::WindowsDesktop)
    {
        skuId = CLR_ID_V4_DESKTOP;
    }
#endif
    CLRDebuggingImpl* pDebuggingImpl = new CLRDebuggingImpl(skuId, IsWindowsTarget());
    hr = pDebuggingImpl->QueryInterface(IID_ICLRDebugging, (LPVOID *)&pClrDebugging);
    if (FAILED(hr))
    {
        delete pDebuggingImpl;
        return hr;
    }

    ToRelease<ICorDebugMutableDataTarget> pCorDebugDataTarget = new CorDebugDataTarget;
    pCorDebugDataTarget->AddRef();

    ToRelease<ICLRDebuggingLibraryProvider> pCorDebugLibraryProvider = new CorDebugLibraryProvider(this);
    pCorDebugLibraryProvider->AddRef();

    CLR_DEBUGGING_VERSION clrDebuggingVersionRequested = {0};
    clrDebuggingVersionRequested.wMajor = 4;

    CLR_DEBUGGING_VERSION clrDebuggingVersionActual = {0};

    CLR_DEBUGGING_PROCESS_FLAGS clrDebuggingFlags = (CLR_DEBUGGING_PROCESS_FLAGS)0;

    ToRelease<IUnknown> pUnkProcess;
    hr = pClrDebugging->OpenVirtualProcess(
        GetModuleAddress(),
        pCorDebugDataTarget,
        pCorDebugLibraryProvider,
        &clrDebuggingVersionRequested,
        IID_ICorDebugProcess,
        &pUnkProcess,
        &clrDebuggingVersionActual,
        &clrDebuggingFlags);

    if (FAILED(hr)) {
        return hr;
    }
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

#ifdef FEATURE_PAL
    // Load the symbols for runtime. On Linux we are looking for the "sccsid" 
    // global so "libcoreclr.so/.dylib" symbols need to be loaded.
    LoadNativeSymbols(true);
#endif

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
        ExtOut("    Runtime directory: %s\n", m_runtimeDirectory);
    }
    if (m_dacFilePath != nullptr) {
        ExtOut("    DAC file path: %s\n", m_dacFilePath);
    }
    if (m_dbiFilePath != nullptr) {
        ExtOut("    DBI file path: %s\n", m_dbiFilePath);
    }
}

extern bool g_symbolStoreInitialized;

/**********************************************************************\
 * Attempt to download the runtime modules (runtime, DAC and DBI)
\**********************************************************************/
void Runtime::LoadRuntimeModules()
{
    HRESULT hr = InitializeSymbolService();
    if (SUCCEEDED(hr) && g_symbolStoreInitialized)
    {
        if (m_runtimeInfo != nullptr)
        {
            GetSymbolService()->LoadNativeSymbolsFromIndex(
                SymbolFileCallback,
                this,
                GetRuntimeConfiguration(),
                GetRuntimeDllName(),
                true,                                   // special keys (runtime, DAC and DBI)
                m_runtimeInfo->RuntimeModuleIndex[0],   // size of module index
                &m_runtimeInfo->RuntimeModuleIndex[1]); // beginning of index 
        }
        else
        {
            GetSymbolService()->LoadNativeSymbols(
                SymbolFileCallback,
                this,
                GetRuntimeConfiguration(),
                m_name,
                m_address,
                (int)m_size);
        }
    }
}

/**********************************************************************\
 * Called by LoadRuntimeModules to set the DAC and DBI file paths
\**********************************************************************/
void Runtime::SymbolFileCallback(const char* moduleFileName, const char* symbolFilePath)
{
    if (strcmp(moduleFileName, GetRuntimeDllName()) == 0) {
        return;
    }
    if (strcmp(moduleFileName, GetDacDllName()) == 0) {
        SetDacFilePath(symbolFilePath);
        return;
    }
    if (strcmp(moduleFileName, NET_DBI_DLL_NAME_A) == 0) {
        SetDbiFilePath(symbolFilePath);
        return;
    }
}

#ifndef FEATURE_PAL

/**********************************************************************\
 * Internal function to load and check the version of the module
\**********************************************************************/
HMODULE LoadLibraryAndCheck(
    PCWSTR filename,
    DWORD timestamp,
    DWORD filesize)
{
    HMODULE hModule = LoadLibraryExW(
        filename,
        NULL,                               //  __reserved
        LOAD_WITH_ALTERED_SEARCH_PATH);     // Ensure we check the dir in wszFullPath first

    if (hModule == NULL)
    {
        ExtOut("Unable to load '%S'. hr = 0x%x.\n", filename, HRESULT_FROM_WIN32(GetLastError()));
        return NULL;
    }
    
    // Did we load the right one?
    MODULEINFO modInfo = {0};
    if (!GetModuleInformation(
        GetCurrentProcess(),
        hModule,
        &modInfo,
        sizeof(modInfo)))
    {
        ExtOut("Failed to read module information for '%S'. hr = 0x%x.\n", filename, HRESULT_FROM_WIN32(GetLastError()));
        FreeLibrary(hModule);
        return NULL;
    }

    IMAGE_DOS_HEADER * pDOSHeader = (IMAGE_DOS_HEADER *) modInfo.lpBaseOfDll;
    IMAGE_NT_HEADERS * pNTHeaders = (IMAGE_NT_HEADERS *) (((LPBYTE) modInfo.lpBaseOfDll) + pDOSHeader->e_lfanew);
    DWORD dwSizeActual = pNTHeaders->OptionalHeader.SizeOfImage;
    DWORD dwTimeStampActual = pNTHeaders->FileHeader.TimeDateStamp;
    if ((dwSizeActual != filesize) || (dwTimeStampActual != timestamp))
    {
        ExtOut("Found '%S', but it does not match the CLR being debugged.\n", filename);
        ExtOut("Size: Expected '0x%x', Actual '0x%x'\n", filesize, dwSizeActual);
        ExtOut("Time stamp: Expected '0x%x', Actual '0x%x'\n", timestamp, dwTimeStampActual);
        FreeLibrary(hModule);
        return NULL;
    }

    return hModule;
}

#endif // FEATURE_PAL
