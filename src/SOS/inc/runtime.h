// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#pragma once

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

/// <summary>
/// IRuntime - the native interface 
/// </summary>
MIDL_INTERFACE("A5F152B9-BA78-4512-9228-5091A4CB7E35")
IRuntime : public IUnknown
{
public:
    /// <summary>
    /// The runtime OS and type. Must match RuntimeWrapper.RuntimeConfiguration.
    /// </summary>
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

    /// <summary>
    /// Returns the runtime configuration 
    /// </summary>
    virtual RuntimeConfiguration STDMETHODCALLTYPE GetRuntimeConfiguration() const = 0;

    /// <summary>
    /// Returns the runtime module base address 
    /// </summary>
    virtual ULONG64 STDMETHODCALLTYPE GetModuleAddress() const = 0;

    /// <summary>
    /// Returns the runtime module size 
    /// </summary>
    virtual ULONG64 STDMETHODCALLTYPE GetModuleSize() const = 0;

    /// <summary>
    /// Set the runtime module directory to search for DAC/DBI
    /// </summary>
    virtual void STDMETHODCALLTYPE SetRuntimeDirectory(LPCSTR runtimeModuleDirectory) = 0;

    /// <summary>
    /// Returns the directory of the runtime module
    /// </summary>
    virtual LPCSTR STDMETHODCALLTYPE GetRuntimeDirectory() = 0;

    /// <summary>
    /// Returns the DAC data process instance
    /// </summary>
    virtual HRESULT STDMETHODCALLTYPE GetClrDataProcess(IXCLRDataProcess** ppClrDataProcess) = 0;

    /// <summary>
    /// Initializes and returns the DBI debugging interface instance 
    /// </summary>
    virtual HRESULT STDMETHODCALLTYPE GetCorDebugInterface(ICorDebugProcess** ppCorDebugProcess) = 0;

    /// <summary>
    /// Gets version info for the CLR in the debuggee process. 
    /// </summary>
    /// <param name="pFileInfo">the file version fields are filled with the runtime module's version</param>
    /// <param name="fileVersionBuffer">buffer to return the full file version string with commit id, etc. or null</param>
    /// <param name="fileVersionBufferSizeInBytes">size of fileVersionBuffer</param>
    /// <returns></returns>
    virtual HRESULT STDMETHODCALLTYPE GetEEVersion(VS_FIXEDFILEINFO* pFileInfo, char* fileVersionBuffer, int fileVersionBufferSizeInBytes) = 0;
};

#ifdef __cplusplus
};
#endif
