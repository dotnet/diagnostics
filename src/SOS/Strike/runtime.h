// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#ifndef __runtime_h__
#define __runtime_h__

#include <runtimeinfo.h>

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

/**********************************************************************\
 * Runtime interface
\**********************************************************************/
class IRuntime
{
public:
    enum RuntimeConfiguration
    {
        WindowsDesktop      = 0,
        WindowsCore         = 1,
        UnixCore            = 2,
        OSXCore             = 3,
        ConfigurationEnd,
#ifdef FEATURE_PAL
#ifdef __APPLE__
        Core = OSXCore
#else
        Core = UnixCore
#endif
#else
        Core = WindowsCore
#endif
    };

    // Returns the runtime configuration
    virtual RuntimeConfiguration GetRuntimeConfiguration() const = 0;

    // Returns the runtime module index
    virtual ULONG GetModuleIndex() const = 0;

    // Returns the runtime module base address
    virtual ULONG64 GetModuleAddress() const = 0;

    // Returns the runtime module size
    virtual ULONG64 GetModuleSize() const = 0;

    // Returns the directory of the runtime file
    virtual LPCSTR GetRuntimeDirectory() = 0;

    // Returns the DAC module path to the rest of SOS
    virtual LPCSTR GetDacFilePath() = 0;

    // Returns the DBI module path to the rest of SOS
    virtual LPCSTR GetDbiFilePath() = 0;

    // Returns the DAC data process instance
    virtual HRESULT GetClrDataProcess(IXCLRDataProcess** ppClrDataProcess) = 0;

    // Initializes and returns the DBI debugging interface instance
    virtual HRESULT GetCorDebugInterface(ICorDebugProcess** ppCorDebugProcess) = 0;

    // Displays the runtime internal status
    virtual void DisplayStatus() = 0;
};

extern LPCSTR g_runtimeModulePath;
extern IRuntime* g_pRuntime;

// Returns the runtime configuration as a string
inline static const char* GetRuntimeConfigurationName(IRuntime::RuntimeConfiguration config)
{
    static const char* name[IRuntime::ConfigurationEnd] = {
        "Desktop",
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

inline bool IsWindowsTarget(IRuntime::RuntimeConfiguration config)
{
    return (config == IRuntime::WindowsCore) || (config == IRuntime::WindowsDesktop);
}

inline bool IsWindowsTarget()
{
    return IsWindowsTarget(g_pRuntime->GetRuntimeConfiguration());
}

/**********************************************************************\
 * Local Runtime interface implementation
\**********************************************************************/
class Runtime : public IRuntime
{
private:
    RuntimeConfiguration m_configuration;
    ULONG m_index;
    ULONG64 m_address;
    ULONG64 m_size;
    RuntimeInfo* m_runtimeInfo;
    LPCSTR m_runtimeDirectory;
    LPCSTR m_dacFilePath;
    LPCSTR m_dbiFilePath;
    IXCLRDataProcess* m_clrDataProcess;
    ICorDebugProcess* m_pCorDebugProcess;

    static Runtime* s_netcore;
#ifndef FEATURE_PAL
    static Runtime* s_desktop;
#endif
    static RuntimeConfiguration s_configuration;
    static LPCSTR s_dacFilePath;
    static LPCSTR s_dbiFilePath;

    Runtime(RuntimeConfiguration configuration, ULONG index, ULONG64 address, ULONG64 size, RuntimeInfo* runtimeInfo) :
        m_configuration(configuration),
        m_index(index),
        m_address(address),
        m_size(size),
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
        if (configuration == s_configuration) {
            SetDacFilePath(s_dacFilePath);
            SetDbiFilePath(s_dbiFilePath);
        }
    }

    virtual Runtime::~Runtime();

    static HRESULT CreateInstance(RuntimeConfiguration configuration, Runtime** ppRuntime);

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
    static HRESULT CreateInstance();

    static void CleanupRuntimes();

#ifndef FEATURE_PAL
    static bool SwitchRuntime(bool desktop);
#endif

    static void SetDacDbiPath(bool isDesktop, LPCSTR dacFilePath, LPCSTR dbiFilePath)
    {
        s_configuration = isDesktop ? IRuntime::WindowsDesktop : IRuntime::Core;
        if (dacFilePath != nullptr) {
            s_dacFilePath = _strdup(dacFilePath);
        }
        if (dbiFilePath != nullptr) {
            s_dbiFilePath = _strdup(dbiFilePath);
        }
    }

    static void Flush();

    virtual RuntimeConfiguration GetRuntimeConfiguration() const { return m_configuration; }

    virtual ULONG GetModuleIndex() const { return m_index; }

    virtual ULONG64 GetModuleAddress() const { return m_address; }

    virtual ULONG64 GetModuleSize() const { return m_size; }

    LPCSTR GetRuntimeDirectory();

    LPCSTR GetDacFilePath();

    LPCSTR GetDbiFilePath();

    HRESULT GetClrDataProcess(IXCLRDataProcess** ppClrDataProcess);

    HRESULT GetCorDebugInterface(ICorDebugProcess** ppCorDebugProcess);

    void DisplayStatus();

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

#endif // __runtime_h__
