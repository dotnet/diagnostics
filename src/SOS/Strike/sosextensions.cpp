// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "exts.h"
#ifndef FEATURE_PAL
#include "dbgengservices.h"
#endif

SOSExtensions::SOSExtensions(IDebuggerServices* debuggerServices, IHost* host) :
    Extensions(debuggerServices)
{
    m_pHost = host;
    OnUnloadTask::Register(SOSExtensions::Uninitialize);
}

#ifndef FEATURE_PAL

SOSExtensions::~SOSExtensions()
{
    if (m_pDebuggerServices != nullptr)
    {
        ((DbgEngServices*)m_pDebuggerServices)->Uninitialize();
        m_pDebuggerServices->Release();
        m_pDebuggerServices = nullptr;
    }
}

HRESULT
SOSExtensions::Initialize(IDebugClient* client)
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

HRESULT
SOSExtensions::Initialize(IHost* host, IDebuggerServices* debuggerServices)
{
    if (s_extensions == nullptr) 
    {
        s_extensions = new SOSExtensions(debuggerServices, host);
    }
    return S_OK;
}

void
SOSExtensions::Uninitialize()
{
    if (s_extensions != nullptr)
    {
        delete s_extensions;
        s_extensions = nullptr;
    }
}

/// <summary>
/// Returns the host instance
/// 
/// * dotnet-dump - m_pHost has already been set by SOSInitializeByHost by SOS.Hosting
/// * lldb - m_pHost has already been set by SOSInitializeByHost by libsosplugin which gets it via the InitializeHostServices callback
/// * dbgeng - SOS.Extensions provides the instance via the InitializeHostServices callback
/// </summary>
IHost*
SOSExtensions::GetHost()
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
HRESULT
GetRuntime(IRuntime** ppRuntime)
{
    Extensions* extensions = Extensions::GetInstance();
    ITarget* target = extensions->GetTarget();
    if (target == nullptr)
    {
        return E_FAIL;
    }
    // Flush here only on Windows under dbgeng. The lldb sos plugin handles it for Linux/MacOS.
#ifndef FEATURE_PAL
    extensions->FlushCheck();
#endif
    return target->GetRuntime(ppRuntime);
}
