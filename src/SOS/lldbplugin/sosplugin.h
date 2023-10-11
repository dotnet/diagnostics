// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <lldb/API/LLDB.h>
#include "mstypes.h"
#define DEFINE_EXCEPTION_RECORD
#include "lldbservices.h"
#include "extensions.h"
#include "dbgtargetcontext.h"
#include "specialdiaginfo.h"
#include "specialthreadinfo.h"
#include "services.h"

#define SOSInitialize "SOSInitializeByHost"

typedef HRESULT (*CommandFunc)(ILLDBServices* services, const char* args);
typedef HRESULT (*InitializeFunc)(IUnknown* punk, IDebuggerServices* debuggerServices);

extern char *g_coreclrDirectory;
extern LLDBServices* g_services;

bool 
sosCommandInitialize(lldb::SBDebugger debugger);

bool
setsostidCommandInitialize(lldb::SBDebugger debugger);

bool
sethostruntimeCommandInitialize(lldb::SBDebugger debugger);

//-----------------------------------------------------------------------------------------
// Extension helper class
//-----------------------------------------------------------------------------------------
class PluginExtensions : public Extensions
{
    PluginExtensions(IDebuggerServices* debuggerServices) :
        Extensions(debuggerServices)
    {
    }

public:
    static void Initialize()
    {
        if (s_extensions == nullptr)
        {
            s_extensions = new PluginExtensions(g_services);
        }
    }

    static bool Uninitialize(void* baton, const char** argv)
    {
        if (s_extensions != nullptr)
        {
            s_extensions->DestroyTarget();
        }
        return false;
    }

    /// <summary>
    /// Returns the host instance or null
    /// 
    /// SOS.Extensions provides the instance via the InitializeHostServices callback
    /// </summary>
    IHost* GetHost()
    {
        if (m_pHost == nullptr)
        {
            // If we can get the host instance from the client, initialize the hosting runtime which will
            // call InitializeHostServices and give us a host instance.
            InitializeHosting();
        }
        return m_pHost;
    }
};
