// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#ifndef __runtime_h__
#define __runtime_h__

#ifdef HOST_UNIX

#define NETCORE_RUNTIME_MODULE_NAME_W   MAKEDLLNAME_W(W("coreclr"))
#define NETCORE_RUNTIME_MODULE_NAME_A   MAKEDLLNAME_A("coreclr")
#define NETCORE_RUNTIME_DLL_NAME_W      NETCORE_RUNTIME_MODULE_NAME_W
#define NETCORE_RUNTIME_DLL_NAME_A      NETCORE_RUNTIME_MODULE_NAME_A

#define NETCORE_DAC_MODULE_NAME_W       MAKEDLLNAME_W(W("mscordaccore"))
#define NETCORE_DAC_MODULE_NAME_A       MAKEDLLNAME_A("mscordaccore")
#define NETCORE_DAC_DLL_NAME_W          NETCORE_DAC_MODULE_NAME_W
#define NETCORE_DAC_DLL_NAME_A          NETCORE_DAC_MODULE_NAME_A

#define NET_DBI_MODULE_NAME_W           MAKEDLLNAME_W(W("mscordbi"))
#define NET_DBI_MODULE_NAME_A           MAKEDLLNAME_A("mscordbi")
#define NET_DBI_DLL_NAME_W              NET_DBI_MODULE_NAME_W       
#define NET_DBI_DLL_NAME_A              NET_DBI_MODULE_NAME_A       

#else

#define NETCORE_RUNTIME_MODULE_NAME_W   W("coreclr")
#define NETCORE_RUNTIME_MODULE_NAME_A   "coreclr"
#define NETCORE_RUNTIME_DLL_NAME_W      MAKEDLLNAME_W(NETCORE_RUNTIME_MODULE_NAME_W)
#define NETCORE_RUNTIME_DLL_NAME_A      MAKEDLLNAME_A(NETCORE_RUNTIME_MODULE_NAME_A)

#define NETCORE_DAC_MODULE_NAME_W       W("mscordaccore")
#define NETCORE_DAC_MODULE_NAME_A       "mscordaccore"
#define NETCORE_DAC_DLL_NAME_W          MAKEDLLNAME_W(NETCORE_DAC_MODULE_NAME_W)
#define NETCORE_DAC_DLL_NAME_A          MAKEDLLNAME_A(NETCORE_DAC_MODULE_NAME_A)

#define NET_DBI_MODULE_NAME_W           W("mscordbi")
#define NET_DBI_MODULE_NAME_A           "mscordbi"
#define NET_DBI_DLL_NAME_W              MAKEDLLNAME_W(W("mscordbi"))
#define NET_DBI_DLL_NAME_A              MAKEDLLNAME_A("mscordbi")

#endif // HOST_UNIX

#define DESKTOP_RUNTIME_MODULE_NAME_W   W("clr")
#define DESKTOP_RUNTIME_MODULE_NAME_A   "clr"
#define DESKTOP_RUNTIME_DLL_NAME_W      MAKEDLLNAME_W(DESKTOP_RUNTIME_MODULE_NAME_W)
#define DESKTOP_RUNTIME_DLL_NAME_A      MAKEDLLNAME_A(DESKTOP_RUNTIME_MODULE_NAME_A)

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
    // Returns true if desktop CLR; false if .NET Core
    virtual bool IsDesktop() const = 0;

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

/**********************************************************************\
 * Local Runtime interface implementation
\**********************************************************************/
class Runtime : public IRuntime
{
private:
    bool m_isDesktop;
    ULONG m_index;
    ULONG64 m_address;
    ULONG64 m_size;
    LPCSTR m_runtimeDirectory;
    LPCSTR m_dacFilePath;
    LPCSTR m_dbiFilePath;
    IXCLRDataProcess* m_clrDataProcess;
    ICorDebugProcess* m_pCorDebugProcess;

    static Runtime* s_netcore;
#ifndef HOST_UNIX
    static Runtime* s_desktop;
#endif
    static bool s_isDesktop;
    static LPCSTR s_dacFilePath;
    static LPCSTR s_dbiFilePath;

    Runtime(bool isDesktop, ULONG index, ULONG64 address, ULONG64 size) : 
        m_isDesktop(isDesktop),
        m_index(index),
        m_address(address),
        m_size(size),
        m_runtimeDirectory(nullptr),
        m_dacFilePath(nullptr),
        m_dbiFilePath(nullptr),
        m_clrDataProcess(nullptr),
        m_pCorDebugProcess(nullptr)
    {
        _ASSERTE(index != -1);
        _ASSERTE(address != 0);
        _ASSERTE(size != 0);
        if (isDesktop == s_isDesktop) {
            SetDacFilePath(s_dacFilePath);
            SetDbiFilePath(s_dbiFilePath);
        }
    }

    virtual Runtime::~Runtime();

    static HRESULT CreateInstance(bool isDesktop, Runtime** ppRuntime);

    HRESULT GetRuntimeDirectory(std::string& runtimeDirectory);

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

#ifndef HOST_UNIX
    static bool SwitchRuntime(bool desktop);
#endif

    static void SetDacDbiPath(bool isDesktop, LPCSTR dacFilePath, LPCSTR dbiFilePath)
    {
        s_isDesktop = isDesktop;
        if (dacFilePath != nullptr) {
            s_dacFilePath = _strdup(dacFilePath);
        }
        if (dbiFilePath != nullptr) {
            s_dbiFilePath = _strdup(dbiFilePath);
        }
    }

    static void Flush();

    virtual bool IsDesktop() const { return m_isDesktop; }

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
        return IsDesktop() ? DESKTOP_RUNTIME_DLL_NAME_A : NETCORE_RUNTIME_DLL_NAME_A;
    }

    // Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
    inline const char* GetDacDllName() const
    {
        return IsDesktop() ? DESKTOP_DAC_DLL_NAME_A : NETCORE_DAC_DLL_NAME_A;
    }

    // Returns the DAC module name (mscordacwks, mscordaccore, libmscordaccore.so, libmscordaccore.dylib) 
    inline const WCHAR* GetDacModuleNameW() const
    {
        return IsDesktop() ? DESKTOP_DAC_MODULE_NAME_W : NETCORE_DAC_MODULE_NAME_W;
    }

    // Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
    inline const WCHAR* GetDacDllNameW() const
    {
        return IsDesktop() ? DESKTOP_DAC_DLL_NAME_W : NETCORE_DAC_DLL_NAME_W;
    }
};

// Returns the runtime module name (clr, coreclr, libcoreclr.so, libcoreclr.dylib).
inline const char* GetRuntimeModuleName()
{
    return g_pRuntime->IsDesktop() ? DESKTOP_RUNTIME_MODULE_NAME_A : NETCORE_RUNTIME_MODULE_NAME_A;
}

// Returns the runtime module DLL name (clr.dll, coreclr.dll, libcoreclr.so, libcoreclr.dylib)
inline const char* GetRuntimeDllName()
{
    return g_pRuntime->IsDesktop() ? DESKTOP_RUNTIME_DLL_NAME_A : NETCORE_RUNTIME_DLL_NAME_A;
}

// Returns the DAC module name (mscordacwks, mscordaccore, libmscordaccore.so, libmscordaccore.dylib) 
inline const char* GetDacModuleName()
{
    return g_pRuntime->IsDesktop() ? DESKTOP_DAC_MODULE_NAME_A : NETCORE_DAC_MODULE_NAME_A;
}

// Returns the DAC module name (mscordacwks.dll, mscordaccore.dll, libmscordaccore.so, libmscordaccore.dylib) 
inline const char* GetDacDllName()
{
    return g_pRuntime->IsDesktop() ? DESKTOP_DAC_DLL_NAME_A : NETCORE_DAC_DLL_NAME_A;
}

#endif // __runtime_h__
