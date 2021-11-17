// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#pragma once

#include "extensions.h"
#include "runtime.h"
#include "runtimeinfo.h"

#ifdef FEATURE_PAL

#define NETCORE_DAC_MODULE_NAME_W       MAKEDLLNAME_W(W("mscordaccore"))
#define NETCORE_DAC_MODULE_NAME_A       MAKEDLLNAME_A("mscordaccore")
#define NETCORE_DAC_DLL_NAME_W          NETCORE_DAC_MODULE_NAME_W
#define NETCORE_DAC_DLL_NAME_A          NETCORE_DAC_MODULE_NAME_A

#define NET_DBI_MODULE_NAME_W           MAKEDLLNAME_W(W("mscordbi"))
#define NET_DBI_MODULE_NAME_A           MAKEDLLNAME_A("mscordbi")
#define NET_DBI_DLL_NAME_W              NET_DBI_MODULE_NAME_W       
#define NET_DBI_DLL_NAME_A              NET_DBI_MODULE_NAME_A       

#else

#define NETCORE_DAC_MODULE_NAME_W       W("mscordaccore")
#define NETCORE_DAC_MODULE_NAME_A       "mscordaccore"
#define NETCORE_DAC_DLL_NAME_W          MAKEDLLNAME_W(NETCORE_DAC_MODULE_NAME_W)
#define NETCORE_DAC_DLL_NAME_A          MAKEDLLNAME_A(NETCORE_DAC_MODULE_NAME_A)

#define NET_DBI_MODULE_NAME_W           W("mscordbi")
#define NET_DBI_MODULE_NAME_A           "mscordbi"
#define NET_DBI_DLL_NAME_W              MAKEDLLNAME_W(W("mscordbi"))
#define NET_DBI_DLL_NAME_A              MAKEDLLNAME_A("mscordbi")

#endif // FEATURE_PAL

#define DESKTOP_DAC_MODULE_NAME_W       W("mscordacwks")
#define DESKTOP_DAC_MODULE_NAME_A       "mscordacwks"
#define DESKTOP_DAC_DLL_NAME_W          MAKEDLLNAME_W(W("mscordacwks"))
#define DESKTOP_DAC_DLL_NAME_A          MAKEDLLNAME_A("mscordacwks")

extern IRuntime* g_pRuntime;

// Returns the runtime configuration as a string
inline static const char* GetRuntimeConfigurationName(IRuntime::RuntimeConfiguration config)
{
    static const char* name[IRuntime::ConfigurationEnd] = {
        "Desktop .NET Framework",
        ".NET Core (Windows)",
        ".NET Core (Unix)",
        ".NET Core (Mac)"
    };
    return (config < IRuntime::ConfigurationEnd) ? name[config] : nullptr;
}

// Returns the runtime module DLL name (clr.dll, coreclr.dll, libcoreclr.so, libcoreclr.dylib)
inline static const char* GetRuntimeDllName(IRuntime::RuntimeConfiguration config)
{
    static const char* name[IRuntime::ConfigurationEnd] = {
        "clr.dll",
        "coreclr.dll",
        "libcoreclr.so",
        "libcoreclr.dylib"
    };
    return (config < IRuntime::ConfigurationEnd) ? name[config] : nullptr;
}

// Returns the runtime module name (clr, coreclr, libcoreclr.so, libcoreclr.dylib).
inline static const char* GetRuntimeModuleName(IRuntime::RuntimeConfiguration config)
{
#ifdef FEATURE_PAL
    return GetRuntimeDllName(config);
#else
    // On a windows host the module name does not include the extension.
    static const char* name[IRuntime::ConfigurationEnd] = {
        "clr",
        "coreclr",
        "libcoreclr",
        "libcoreclr"
    };
    return (config < IRuntime::ConfigurationEnd) ? name[config] : nullptr;
#endif
}

// Returns the runtime module name (clr, coreclr, libcoreclr.so, libcoreclr.dylib).
inline const char* GetRuntimeModuleName()
{
    return GetRuntimeModuleName(g_pRuntime->GetRuntimeConfiguration());
}

// Returns the runtime module DLL name (clr.dll, coreclr.dll, libcoreclr.so, libcoreclr.dylib)
inline const char* GetRuntimeDllName()
{
    return GetRuntimeDllName(g_pRuntime->GetRuntimeConfiguration());
}

// Returns the DAC module name (mscordacwks, mscordaccore, libmscordaccore.so, libmscordaccore.dylib) 
inline const char* GetDacModuleName()
{
    return (g_pRuntime->GetRuntimeConfiguration() == IRuntime::WindowsDesktop) ? DESKTOP_DAC_MODULE_NAME_A : NETCORE_DAC_MODULE_NAME_A;
}

// Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
inline const char* GetDacDllName()
{
    return (g_pRuntime->GetRuntimeConfiguration() == IRuntime::WindowsDesktop) ? DESKTOP_DAC_DLL_NAME_A : NETCORE_DAC_DLL_NAME_A;
}

/**********************************************************************\
 * Local Runtime interface implementation
\**********************************************************************/
class Runtime : public IRuntime
{
private:
    LONG m_ref;
    ITarget* m_target;
    RuntimeConfiguration m_configuration;
    ULONG m_index;
    ULONG64 m_address;
    ULONG64 m_size;
    const char* m_name;
    RuntimeInfo* m_runtimeInfo;
    LPCSTR m_runtimeDirectory;
    LPCSTR m_dacFilePath;
    LPCSTR m_dbiFilePath;
    IXCLRDataProcess* m_clrDataProcess;
    ICorDebugProcess* m_pCorDebugProcess;

    Runtime(ITarget* target, RuntimeConfiguration configuration, ULONG index, ULONG64 address, ULONG64 size, RuntimeInfo* runtimeInfo);

    virtual ~Runtime();

    void LoadRuntimeModules();

    void SymbolFileCallback(const char* moduleFileName, const char* symbolFilePath);

    static void SymbolFileCallback(void* param, const char* moduleFileName, const char* symbolFilePath)
    {
        ((Runtime*)param)->SymbolFileCallback(moduleFileName, symbolFilePath);
    }

    void SetDacFilePath(LPCSTR dacFilePath)
    { 
        if (m_dacFilePath == nullptr && dacFilePath != nullptr) {
            m_dacFilePath = _strdup(dacFilePath);
        }
    }

    void SetDbiFilePath(LPCSTR dbiFilePath) 
    { 
        if (m_dbiFilePath == nullptr && dbiFilePath != nullptr) {
            m_dbiFilePath = _strdup(dbiFilePath);
        }
    }

public:
    static HRESULT CreateInstance(ITarget* target, RuntimeConfiguration configuration, Runtime** ppRuntime);

    void Flush();

    LPCSTR GetDacFilePath();

    LPCSTR GetDbiFilePath();

    void DisplayStatus();

    //----------------------------------------------------------------------------
    // IUnknown
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* Interface);

    ULONG STDMETHODCALLTYPE AddRef();

    ULONG STDMETHODCALLTYPE Release();

    //----------------------------------------------------------------------------
    // IRuntime
    //----------------------------------------------------------------------------

    RuntimeConfiguration STDMETHODCALLTYPE GetRuntimeConfiguration() const { return m_configuration; }

    ULONG64 STDMETHODCALLTYPE GetModuleAddress() const { return m_address; }

    ULONG64 STDMETHODCALLTYPE GetModuleSize() const { return m_size; }

    void STDMETHODCALLTYPE SetRuntimeDirectory(LPCSTR runtimeModuleDirectory);

    LPCSTR STDMETHODCALLTYPE GetRuntimeDirectory();

    HRESULT STDMETHODCALLTYPE GetClrDataProcess(IXCLRDataProcess** ppClrDataProcess);

    HRESULT STDMETHODCALLTYPE GetCorDebugInterface(ICorDebugProcess** ppCorDebugProcess);

    HRESULT STDMETHODCALLTYPE GetEEVersion(VS_FIXEDFILEINFO* pFileInfo, char* fileVersionBuffer, int fileVersionBufferSizeInBytes);


    // Returns the runtime module DLL name (clr.dll, coreclr.dll, libcoreclr.so, libcoreclr.dylib)
    inline const char* GetRuntimeDllName() const
    {
        return ::GetRuntimeDllName(GetRuntimeConfiguration());
    }

    // Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
    inline const char* GetDacDllName() const
    {
        return (GetRuntimeConfiguration() == IRuntime::WindowsDesktop) ? DESKTOP_DAC_DLL_NAME_A : NETCORE_DAC_DLL_NAME_A;
    }

    // Returns the DAC module name (mscordacwks, mscordaccore, libmscordaccore.so, libmscordaccore.dylib) 
    inline const WCHAR* GetDacModuleNameW() const
    {
        return (GetRuntimeConfiguration() == IRuntime::WindowsDesktop) ? DESKTOP_DAC_MODULE_NAME_W : NETCORE_DAC_MODULE_NAME_W;
    }

    // Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
    inline const WCHAR* GetDacDllNameW() const
    {
        return (GetRuntimeConfiguration() == IRuntime::WindowsDesktop) ? DESKTOP_DAC_DLL_NAME_W : NETCORE_DAC_DLL_NAME_W;
    }
};
