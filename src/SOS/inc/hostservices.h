// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#pragma once

#include <stdarg.h>
#include <unknwn.h>
#include <palclr.h>

static const char*  ExtensionsDllName = "SOS.Extensions";
static const WCHAR* ExtensionsDllNameW = W("SOS.Extensions.dll");
static const char*  ExtensionsClassName = "SOS.Extensions.HostServices";
static const WCHAR* ExtensionsClassNameW = W("SOS.Extensions.HostServices");
static const char*  ExtensionsInitializeFunctionName = "Initialize";
static const WCHAR* ExtensionsInitializeFunctionNameW = W("Initialize");

typedef HRESULT (*ExtensionsInitializeDelegate)(PCSTR extensionPath);

#ifdef __cplusplus
extern "C" {
#endif

struct IHost;

/// <summary>
/// IHostServices
/// 
/// Services from the managed extension infrastructure (SOS.Extensions) provides to the native 
/// debugger (dbgeng/lldb) plugins (sos.dll, libsosplugin.*). Isn't presented when SOS is hosted
/// by SOS.Hosting (i.e. dotnet-dump).
/// </summary>
MIDL_INTERFACE("27B2CB8D-BDEE-4CBD-B6EF-75880D76D46F")
IHostServices : public IUnknown
{
public:
    /// <summary>
    /// Returns the host interface
    /// </summary>
    /// <param name="ppHost">pointer to return host interface</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE GetHost(
        IHost** ppHost) = 0;

    /// <summary>
    /// Register the IDebuggerServices instance with the managed extension services.
    /// </summary>
    /// <param name="iunk">IDebuggerServices instance</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE RegisterDebuggerServices(
        IUnknown* iunk) = 0;

    /// <summary>
    /// Creates a target instance for the registered debugger services.
    /// </summary>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE CreateTarget() = 0;

    /// <summary>
    /// Creates and/or destroys the target based on the processId.
    /// </summary>
    /// <param name="processId">process id or 0 if none</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE UpdateTarget(ULONG processId) = 0;

    /// <summary>
    /// Flushes the target instance.
    /// </summary>
    virtual void STDMETHODCALLTYPE FlushTarget() = 0;

    /// <summary>
    /// Destroy the target instance
    /// </summary>
    virtual void STDMETHODCALLTYPE DestroyTarget() = 0;

    /// <summary>
    /// Dispatches the command line to managed extension
    /// </summary>
    /// <param name="commandLine">full command line</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE DispatchCommand( 
        PCSTR commandLine) = 0;

    /// <summary>
    /// Displays the help for a managed extension command
    /// </summary>
    /// <param name="command"></param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE DisplayHelp( 
        PCSTR command) = 0;

    /// <summary>
    /// Uninitialize the extension infrastructure
    /// </summary>
    virtual void STDMETHODCALLTYPE Uninitialize() = 0;
};

#ifdef __cplusplus
};
#endif
